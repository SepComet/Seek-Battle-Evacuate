using SepCore.Base;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Run;
using UnityGameFramework.Runtime;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// 单局数据层运行时组件。
    /// 承接并持有单局聚合根 RunSession，对外提供背包、保险箱、队伍状态的读写能力并派发 UGF 事件。
    /// </summary>
    public class RoundComponent : GameFrameworkComponent
    {
        private RoundSession _session;

        /// <summary>
        /// 当前单局数据聚合根（未开始单局时为 null）。
        /// </summary>
        public RoundSession Session => _session;

        /// <summary>
        /// 是否存在活跃的单局数据。
        /// </summary>
        public bool HasActiveRun => _session != null;

        /// <summary>
        /// 使用指定的 RunSession 开始新单局，并立即派发初始变更事件。
        /// </summary>
        public void BeginRun(RoundSession session)
        {
            _session = session;
            FireBackpackChanged();
            FireSafeCaseChanged();
            FirePartyStateChanged();
        }

        /// <summary>
        /// 根据单局难度和随机种子，从当前存档和配表加载并初始化 RunSession。
        /// </summary>
        public void BeginRun(DifficultyTier difficulty, long seed)
        {
            GlobalConfig global = GameEntry.Luban.Global?.Data;
            if (global == null)
            {
                Log.Error("Can not begin run because GlobalConfig is missing from Luban tables.");
                return;
            }

            RoundSession session = RoundSession.Create(
                difficulty,
                seed,
                GameEntry.Save?.Data,
                global,
                id => GameEntry.Luban.Get<CharacterConfig>(id),
                id => GameEntry.Luban.Get<ItemConfig>(id));

            BeginRun(session);
        }

        /// <summary>
        /// 结束当前单局，清空 RunSession。
        /// </summary>
        public void EndRun()
        {
            _session = null;
        }

        /// <summary>
        /// 尝试将物品放入共享背包。成功放入时自动派发 RunBackpackChangedEventArgs。
        /// </summary>
        /// <param name="itemId">物品配置 ID。</param>
        /// <param name="count">欲放入数量。</param>
        /// <returns>实际放入数量。</returns>
        public int AddItemToBackpack(int itemId, int count)
        {
            if (_session == null)
            {
                return 0;
            }

            int added = _session.Backpack.TryAddItem(itemId, count);
            if (added > 0)
            {
                FireBackpackChanged();
            }

            return added;
        }

        /// <summary>
        /// 扣减或移除背包指定槽位中的物品。成功扣减时自动派发 RunBackpackChangedEventArgs。
        /// </summary>
        public bool RemoveItemFromBackpack(int slotIndex, int count, out int removedCount)
        {
            removedCount = 0;
            if (_session == null)
            {
                return false;
            }

            bool result = _session.Backpack.TryRemoveItemAt(slotIndex, count, out removedCount);
            if (result && removedCount > 0)
            {
                FireBackpackChanged();
            }

            return result;
        }

        /// <summary>
        /// 交换背包内两个槽位的内容或执行同类合并。成功时自动派发 RunBackpackChangedEventArgs。
        /// </summary>
        public bool SwapBackpackSlots(int slotA, int slotB)
        {
            if (_session == null)
            {
                return false;
            }

            bool result = _session.Backpack.SwapSlots(slotA, slotB);
            if (result)
            {
                FireBackpackChanged();
            }

            return result;
        }

        /// <summary>
        /// 在共享背包与保险箱之间转移物品。成功转移时自动派发背包与保险箱变更事件。
        /// </summary>
        /// <param name="fromBackpackToSafe">true 为从背包移入保险箱，false 为从保险箱移入背包。</param>
        /// <param name="fromSlotIndex">源容器槽位索引。</param>
        /// <param name="count">转移数量。</param>
        /// <param name="movedCount">实际转移数量。</param>
        public bool MoveBetweenBackpackAndSafe(bool fromBackpackToSafe, int fromSlotIndex, int count, out int movedCount)
        {
            movedCount = 0;
            if (_session == null)
            {
                return false;
            }

            bool result = _session.TryMoveBetweenContainers(fromBackpackToSafe, fromSlotIndex, count, out movedCount);
            if (result && movedCount > 0)
            {
                FireBackpackChanged();
                FireSafeCaseChanged();
            }

            return result;
        }

        /// <summary>
        /// 战后同步回写队伍状态，并派发 RunPartyStateChangedEventArgs。
        /// </summary>
        public void ApplyBattleResult(BattleResult result, int reviveHp, int reviveMp)
        {
            if (_session == null)
            {
                return;
            }

            _session.ApplyBattleResult(result, reviveHp, reviveMp);
            FirePartyStateChanged();
        }

        /// <summary>
        /// 派发背包变更事件。
        /// </summary>
        public void FireBackpackChanged()
        {
            if (_session == null)
            {
                return;
            }

            GameEntry.Event.Fire(this, RoundBackpackChangedEventArgs.Create(
                _session.Backpack.UsedSlotsCount,
                _session.Backpack.MaxSlots,
                _session.Backpack.TotalValue));
        }

        /// <summary>
        /// 派发保险箱变更事件。
        /// </summary>
        public void FireSafeCaseChanged()
        {
            if (_session == null)
            {
                return;
            }

            GameEntry.Event.Fire(this, RoundSafeCaseChangedEventArgs.Create(
                _session.SafeCase.UsedSlotsCount,
                _session.SafeCase.MaxSlots,
                _session.SafeCase.TotalValue));
        }

        /// <summary>
        /// 派发队伍状态变更事件。
        /// </summary>
        public void FirePartyStateChanged()
        {
            GameEntry.Event.Fire(this, RoundPartyStateChangedEventArgs.Create());
        }
    }
}
