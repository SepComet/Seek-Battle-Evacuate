using System;
using System.Collections.Generic;
using SepCore.Base;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 单局结算界面逻辑（手写 partial，与自动生成的 RoundSettlementForm.cs 合并）。
    /// 作为顶级表单展示单局撤离结果、探索耗时、带出物资总价值与红品质战利品列表，并响应返回大厅交互。
    /// </summary>
    public partial class RoundSettlementForm : UGuiForm
    {
        private static readonly Color ColorSuccess = new Color(0.37f, 0.77f, 0.67f, 1f);
        private static readonly Color ColorFailed = new Color(0.85f, 0.35f, 0.35f, 1f);
        private static readonly Color ColorWarning = new Color(0.85f, 0.55f, 0.30f, 1f);

        private readonly List<RedLootItem> _lootItems = new List<RedLootItem>();
        private Action _onReturnCallback;
        private bool _isReturnTriggered;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            _isReturnTriggered = false;
            View.redLootItemTemplate.gameObject.SetActive(false);
            View.returnToLobbyButton.onClick.AddListener(OnReturnToLobbyButtonClick);

            if (userData is RoundSettlementData settlementData)
            {
                Refresh(settlementData);
            }
            else
            {
                RefreshFromCurrentSession(RoundResultType.Extracted);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            View.returnToLobbyButton.onClick.RemoveListener(OnReturnToLobbyButtonClick);

            for (int i = 0; i < _lootItems.Count; i++)
            {
                _lootItems[i].ResetItem();
            }

            _onReturnCallback = null;

            base.OnClose(isShutdown, userData);
        }

        /// <summary>
        /// 设置返回大厅按钮的本地回调委托。
        /// </summary>
        public void SetOnReturnCallback(Action callback)
        {
            _onReturnCallback = callback;
        }

        /// <summary>
        /// 使用结算数据实体刷新界面所有展示内容。
        /// </summary>
        public void Refresh(RoundSettlementData data)
        {
            if (data == null)
            {
                return;
            }

            UpdateResultHeader(data.Outcome);
            View.retainedHint.gameObject.SetActive(data.OnlyRetainedSafeCase);

            int minutes = Math.Max(0, data.ElapsedSeconds) / 60;
            int seconds = Math.Max(0, data.ElapsedSeconds) % 60;
            View.elapsedTime.Set(minutes, seconds);

            FormatText totalValueFormat = GameEntry.Luban.Tables?.TbFormatText?.GetOrDefault("SettlementTotalValue");
            View.totalValueText.text = totalValueFormat != null
                ? string.Format(totalValueFormat.Format, data.TotalValue)
                : data.TotalValue.ToString("N0");

            UpdateRedLootList(data.BroughtItems);
        }

        /// <summary>
        /// 便捷重载：直接传入字段参数刷新界面。
        /// </summary>
        public void Refresh(RoundResultType outcome, int elapsedSeconds, int totalValue, IReadOnlyList<ItemStack> broughtItems)
        {
            Refresh(new RoundSettlementData(outcome, elapsedSeconds, totalValue, broughtItems));
        }

        /// <summary>
        /// 从当前单局会话（RoundSession / TurnBattleComponent）采样并刷新界面（用于快速测试与自适应展示）。
        /// </summary>
        public void RefreshFromCurrentSession(RoundResultType outcome)
        {
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

            Refresh(new RoundSettlementData(outcome, elapsedSeconds, totalValue, broughtItems));
        }

        private void UpdateResultHeader(RoundResultType outcome)
        {
            string titleText;
            Color accentColor;

            switch (outcome)
            {
                case RoundResultType.Extracted:
                    titleText = "撤离成功";
                    accentColor = ColorSuccess;
                    break;
                case RoundResultType.Defeated:
                    titleText = "全员阵亡";
                    accentColor = ColorFailed;
                    break;
                case RoundResultType.TimedOut:
                    titleText = "撤离超时";
                    accentColor = ColorWarning;
                    break;
                case RoundResultType.Quit:
                    titleText = "放弃行动";
                    accentColor = ColorFailed;
                    break;
                default:
                    titleText = "行动结束";
                    accentColor = ColorWarning;
                    break;
            }

            View.resultText.text = titleText;
            View.resultAccent.color = accentColor;
        }

        private void UpdateRedLootList(IReadOnlyList<ItemStack> broughtItems)
        {
            Dictionary<int, int> redItemCounts = new Dictionary<int, int>();
            int totalRedCount = 0;

            if (broughtItems != null)
            {
                for (int i = 0; i < broughtItems.Count; i++)
                {
                    ItemStack stack = broughtItems[i];
                    if (stack.itemId <= 0 || stack.count <= 0)
                    {
                        continue;
                    }

                    ItemConfig config = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
                    if (config != null && config.Rarity == Rarity.Red)
                    {
                        if (redItemCounts.TryGetValue(stack.itemId, out int currentCount))
                        {
                            redItemCounts[stack.itemId] = currentCount + stack.count;
                        }
                        else
                        {
                            redItemCounts[stack.itemId] = stack.count;
                        }

                        totalRedCount += stack.count;
                    }
                }
            }

            View.redLootCount.Set(totalRedCount);

            if (totalRedCount == 0)
            {
                View.emptyRedLootState.gameObject.SetActive(true);
                for (int i = 0; i < _lootItems.Count; i++)
                {
                    _lootItems[i].gameObject.SetActive(false);
                }

                return;
            }

            View.emptyRedLootState.gameObject.SetActive(false);

            int slotIndex = 0;
            foreach (KeyValuePair<int, int> pair in redItemCounts)
            {
                EnsureLootSlotCreated(slotIndex + 1);
                RedLootItem slot = _lootItems[slotIndex];
                slot.gameObject.SetActive(true);
                slot.SetItem(pair.Key, pair.Value);
                slotIndex++;
            }

            for (int i = slotIndex; i < _lootItems.Count; i++)
            {
                _lootItems[i].gameObject.SetActive(false);
            }
        }

        private void EnsureLootSlotCreated(int requiredCount)
        {
            while (_lootItems.Count < requiredCount)
            {
                RedLootItem slot = Instantiate(View.redLootItemTemplate, View.redLootContent);
                _lootItems.Add(slot);
            }
        }

        private void OnReturnToLobbyButtonClick()
        {
            if (_isReturnTriggered)
            {
                return;
            }

            _isReturnTriggered = true;

            GameEntry.Event.Fire(this, RoundSettlementReturnEventArgs.Create());
            _onReturnCallback?.Invoke();

            Close();
        }
    }
}
