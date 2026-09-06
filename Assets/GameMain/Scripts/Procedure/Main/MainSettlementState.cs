using System;
using System.Collections.Generic;
using GameFramework.Event;
using GameFramework.Fsm;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Exploration;
using SepCore.Run;
using SepCore.UI;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主流程状态：撤离结算。
    /// 负责执行局内与局外物品结算、更新角色装备、追加 RunRecord 历史并写盘，重置战斗组件与随机源，
    /// 打开单局结算界面（RoundSettlementForm），并在玩家点击返回大厅后返回大厅菜单。
    /// </summary>
    public sealed class MainSettlementState : FsmState<ProcedureMain>
    {
        private ProcedureMain _procedureMain;

        protected override void OnEnter(IFsm<ProcedureMain> fsm)
        {
            base.OnEnter(fsm);
            _procedureMain = fsm.Owner;
            Log.Info("[ProcedureMain] Entering MainSettlementState...");

            GameEntry.Event.Subscribe(RoundSettlementReturnEventArgs.EventId, OnRoundSettlementReturn);

            RoundResultType outcome = fsm.Owner.PendingOutcome ?? RoundResultType.TimedOut;
            ExecuteSettlement(fsm.Owner, outcome);
        }

        protected override void OnLeave(IFsm<ProcedureMain> fsm, bool isShutdown)
        {
            GameEntry.Event.Unsubscribe(RoundSettlementReturnEventArgs.EventId, OnRoundSettlementReturn);
            _procedureMain = null;
            base.OnLeave(fsm, isShutdown);
        }

        private void OnRoundSettlementReturn(object sender, GameEventArgs e)
        {
            if (_procedureMain != null)
            {
                Log.Info("[ProcedureMain] Settlement return requested, returning to menu...");
                _procedureMain.ReturnToMenu();
            }
        }

        private void ExecuteSettlement(ProcedureMain procedureMain, RoundResultType outcome)
        {
            // 0. 预先采集结算界面展示数据（在清理单局运行时前采集）
            int elapsedSeconds = 0;
            if (GameEntry.TurnBattle != null)
            {
                elapsedSeconds = (int)(GameEntry.TurnBattle.RunElapsedMs / 1000);
            }

            List<ItemStack> broughtItems = new List<ItemStack>();
            int totalValue = 0;

            if (GameEntry.Round?.Session != null)
            {
                RoundSession session = GameEntry.Round.Session;
                List<ItemStack> safeItems = session.SafeCase.ToNonEmptyList();
                broughtItems.AddRange(safeItems);

                if (outcome == RoundResultType.Extracted)
                {
                    List<ItemStack> backpackItems = session.Backpack.ToNonEmptyList();
                    broughtItems.AddRange(backpackItems);
                    totalValue = session.TotalLootValue;
                }
                else
                {
                    totalValue = session.SafeCase.TotalValue;
                }
            }

            RoundSettlementData settlementData = new RoundSettlementData(outcome, elapsedSeconds, totalValue, broughtItems);

            SaveData save = GameEntry.Save.Data;
            if (save != null)
            {
                // 1. 结算单局战利品与背包/保险箱内容回写主仓库
                if (GameEntry.Round?.Session != null)
                {
                    RoundSession session = GameEntry.Round.Session;
                    if (save.mainWarehouse == null)
                    {
                        save.mainWarehouse = new List<ItemStack>();
                    }

                    // 保险箱物品无论胜败全部带出
                    List<ItemStack> safeItems = session.SafeCase.ToNonEmptyList();
                    foreach (ItemStack item in safeItems)
                    {
                        MergeIntoWarehouse(save.mainWarehouse, item);
                    }

                    // 背包物品仅撤离成功时带出
                    if (outcome == RoundResultType.Extracted)
                    {
                        List<ItemStack> backpackItems = session.Backpack.ToNonEmptyList();
                        foreach (ItemStack item in backpackItems)
                        {
                            MergeIntoWarehouse(save.mainWarehouse, item);
                        }
                    }
                }

                // 2. 死亡/超时/主动退出时，已穿戴装备随角色丢失（保留保险箱内容）
                if (outcome != RoundResultType.Extracted && save.characters != null)
                {
                    for (int i = 0; i < save.characters.Count; i++)
                    {
                        CharacterSave c = save.characters[i];
                        c.weaponItemId = 0;
                        c.armorItemId = 0;
                        save.characters[i] = c;
                    }
                }

                // 3. 追加本局历史结算记录
                long endedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                long startedAt = procedureMain.RunStartTimeUtcMs > 0 ? procedureMain.RunStartTimeUtcMs : endedAt;
                long seed = GameEntry.Random.Seed;
                DifficultyTier difficulty = procedureMain.Difficulty;

                if (save.runHistory == null)
                {
                    save.runHistory = new List<RoundRecord>();
                }

                save.runHistory.Add(new RoundRecord(outcome, difficulty, seed, startedAt, endedAt));

                // 4. 写入磁盘
                GameEntry.Save.Save();
                Log.Info("[ProcedureMain] Run record saved to disk. Outcome: {0}, Difficulty: {1}, Seed: {2}.",
                    outcome, difficulty, seed);
            }

            // 5. 清理单局数据层
            if (GameEntry.Round != null)
            {
                GameEntry.Round.EndRun();
            }

            // 6. 清理战斗组件单局临时状态与计时器
            GameEntry.TurnBattle.EndRun();

            // 7. 清理本局共享随机源
            GameEntry.Random.EndRun();

            // 8. 关闭探索常驻 UI 与辅助 UI，禁用探索输入
            procedureMain.CloseJoystickForm();
            procedureMain.CloseRoundHUDForm();
            CloseAuxiliaryUIForms();
            CharacterInputBridge.DisableInput(InputDisableReason.Custom);

            // 9. 打开结算界面并等待返回大厅；若无 UI 则直接切回大厅（测试/无头环境）
            if (GameEntry.UI != null)
            {
                Log.Info("[ProcedureMain] Opening RoundSettlementForm with outcome: {0}...", outcome);
                GameEntry.UI.OpenUIForm(UIFormType.RoundSettlementForm, settlementData);
            }
            else if (procedureMain.AutoReturnToMenu)
            {
                procedureMain.ReturnToMenu();
            }
        }

        private static void CloseAuxiliaryUIForms()
        {
            if (GameEntry.UI == null)
            {
                return;
            }

            if (GameEntry.UI.HasUIForm(UIFormType.RoundBackpackForm))
            {
                UGuiForm backpackForm = GameEntry.UI.GetUIForm(UIFormType.RoundBackpackForm);
                if (backpackForm != null)
                {
                    GameEntry.UI.CloseUIForm(backpackForm);
                }
            }

            if (GameEntry.UI.HasUIForm(UIFormType.BattleForm))
            {
                UGuiForm battleForm = GameEntry.UI.GetUIForm(UIFormType.BattleForm);
                if (battleForm != null)
                {
                    GameEntry.UI.CloseUIForm(battleForm);
                }
            }
        }

        private static void MergeIntoWarehouse(List<ItemStack> warehouse, ItemStack stack)
        {
            if (warehouse == null || stack.itemId <= 0 || stack.count <= 0)
            {
                return;
            }

            ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            int stackLimit = config != null && config.StackLimit > 0 ? config.StackLimit : 1;
            int remaining = stack.count;

            for (int i = 0; i < warehouse.Count; i++)
            {
                if (warehouse[i].itemId == stack.itemId && warehouse[i].count < stackLimit)
                {
                    int space = stackLimit - warehouse[i].count;
                    int toAdd = Math.Min(space, remaining);
                    ItemStack s = warehouse[i];
                    s.count += toAdd;
                    warehouse[i] = s;
                    remaining -= toAdd;
                    if (remaining <= 0)
                    {
                        return;
                    }
                }
            }

            while (remaining > 0)
            {
                int toAdd = Math.Min(stackLimit, remaining);
                warehouse.Add(new ItemStack(stack.itemId, toAdd));
                remaining -= toAdd;
            }
        }
    }
}
