using SepCore.Base;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Entity;
using SepCore.Run;
using UnityEngine;
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
            return MoveBetweenBackpackAndSafe(fromBackpackToSafe, fromSlotIndex, count, out movedCount, out _);
        }

        /// <summary>
        /// 在共享背包与保险箱之间转移物品，并输出目标容器新接收物品的槽位索引。
        /// </summary>
        /// <param name="fromBackpackToSafe">true 为从背包移入保险箱，false 为从保险箱移入背包。</param>
        /// <param name="fromSlotIndex">源容器槽位索引。</param>
        /// <param name="count">转移数量。</param>
        /// <param name="movedCount">实际转移数量。</param>
        /// <param name="targetSlotIndex">目标容器接收槽位索引。</param>
        public bool MoveBetweenBackpackAndSafe(bool fromBackpackToSafe, int fromSlotIndex, int count, out int movedCount, out int targetSlotIndex)
        {
            movedCount = 0;
            targetSlotIndex = -1;
            if (_session == null)
            {
                return false;
            }

            bool result = _session.TryMoveBetweenContainers(fromBackpackToSafe, fromSlotIndex, count, out movedCount, out targetSlotIndex);
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

        /// <summary>
        /// 尝试为指定角色穿戴来自背包或保险箱的装备。
        /// 若角色对应装备槽为空，扣减容器物品并穿戴，自动派发变更事件。
        /// </summary>
        public bool TryEquipFromContainer(int characterIndex, bool fromBackpack, int slotIndex)
        {
            if (_session == null || characterIndex < 0 || characterIndex >= _session.Party.Count)
            {
                return false;
            }

            RoundItemContainer container = fromBackpack ? _session.Backpack : _session.SafeCase;
            if (slotIndex < 0 || slotIndex >= container.Slots.Count)
            {
                return false;
            }

            ItemStack stack = container.Slots[slotIndex];
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                return false;
            }

            ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            if (itemConfig == null)
            {
                return false;
            }

            RoundCharacterState character = _session.Party[characterIndex];
            if (itemConfig.EquipSlot == EquipmentSlotType.Weapon)
            {
                if (character.WeaponItemId != 0)
                {
                    return false;
                }
            }
            else if (itemConfig.EquipSlot == EquipmentSlotType.Armor)
            {
                if (character.ArmorItemId != 0)
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (!container.TryRemoveItemAt(slotIndex, 1, out int removed) || removed <= 0)
            {
                return false;
            }

            character.EquipItem(itemConfig);

            if (fromBackpack)
            {
                FireBackpackChanged();
            }
            else
            {
                FireSafeCaseChanged();
            }

            FirePartyStateChanged();
            return true;
        }

        /// <summary>
        /// 尝试卸下角色指定装备栏的装备，优先放回背包，背包满时放入保险箱。
        /// </summary>
        public bool TryUnequipToContainer(int characterIndex, EquipmentSlotType slotType, out bool toBackpack, out int targetSlotIndex)
        {
            toBackpack = true;
            targetSlotIndex = -1;

            if (_session == null || characterIndex < 0 || characterIndex >= _session.Party.Count)
            {
                return false;
            }

            RoundCharacterState character = _session.Party[characterIndex];
            int itemId = slotType == EquipmentSlotType.Weapon ? character.WeaponItemId : character.ArmorItemId;
            if (itemId <= 0)
            {
                return false;
            }

            ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(itemId);
            if (itemConfig == null)
            {
                return false;
            }

            // 1. 优先尝试放入背包
            int added = _session.Backpack.TryAddItem(itemId, 1, out int backpackSlotIndex);
            if (added > 0)
            {
                toBackpack = true;
                targetSlotIndex = backpackSlotIndex;
                character.UnequipItem(itemConfig);
                FireBackpackChanged();
                FirePartyStateChanged();
                return true;
            }

            // 2. 背包满时尝试放入保险箱
            added = _session.SafeCase.TryAddItem(itemId, 1, out int safeSlotIndex);
            if (added > 0)
            {
                toBackpack = false;
                targetSlotIndex = safeSlotIndex;
                character.UnequipItem(itemConfig);
                FireSafeCaseChanged();
                FirePartyStateChanged();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将背包或保险箱指定槽位的物品作为场景实体丢弃到地图上。
        /// </summary>
        public bool DiscardItemFromContainer(bool fromBackpack, int slotIndex)
        {
            if (_session == null)
            {
                return false;
            }

            RoundItemContainer container = fromBackpack ? _session.Backpack : _session.SafeCase;
            if (slotIndex < 0 || slotIndex >= container.Slots.Count)
            {
                return false;
            }

            ItemStack stack = container.Slots[slotIndex];
            if (stack.itemId <= 0 || stack.count <= 0)
            {
                return false;
            }

            ItemConfig itemConfig = GameEntry.Luban.Get<ItemConfig>(stack.itemId);
            if (itemConfig == null)
            {
                return false;
            }

            if (!container.TryRemoveItemAt(slotIndex, stack.count, out int removedCount) || removedCount <= 0)
            {
                return false;
            }

            if (fromBackpack)
            {
                FireBackpackChanged();
            }
            else
            {
                FireSafeCaseChanged();
            }

            SpawnDroppedItemEntity(stack.itemId, removedCount, itemConfig.Rarity);
            return true;
        }

        private void SpawnDroppedItemEntity(int itemId, int count, Rarity rarity)
        {
            Vector3 spawnCenter = PlayerCharacterLogic.Leader != null
                ? PlayerCharacterLogic.Leader.transform.position
                : Vector3.zero;

            GlobalConfig global = GameEntry.Luban.Global?.Data;
            float minRadius = (global != null && global.LootRangeMinRadius > 0 ? global.LootRangeMinRadius : 500) / 1000f;
            float maxRadius = (global != null && global.LootRangeMaxRadius > 0 ? global.LootRangeMaxRadius : 1200) / 1000f;

            if (maxRadius <= minRadius)
            {
                maxRadius = Mathf.Max(minRadius + 0.1f, 1.2f);
            }

            Vector3 dropPosition = CalculateDropPosition(spawnCenter, minRadius, maxRadius);

            string itemEntityAsset = global != null ? global.ItemEntity : "ItemEntity";
            if (string.IsNullOrEmpty(itemEntityAsset))
            {
                Log.Error("GlobalConfig.ItemEntity is not configured.");
                return;
            }

            if (GameEntry.Entity != null && !GameEntry.Entity.HasEntityGroup("Item"))
            {
                GameEntry.Entity.AddEntityGroup("Item", 60f, 32, 60f, 0);
            }

            int serialId = GameEntry.Entity.SerialId();
            ItemEntityData itemData = new ItemEntityData(
                serialId,
                itemEntityAsset,
                dropPosition,
                itemId,
                count,
                rarity,
                rotation: Quaternion.identity,
                spawnFromPosition: spawnCenter);

            GameEntry.Entity.ShowEntity<ItemEntityLogic>(itemData, "Item", Constant.AssetPriority.SceneAsset);
            Log.Info("Discarded item '{0}' x{1} (Rarity: {2}) at position {3} from leader.",
                itemId, count, rarity, dropPosition);
        }

        private Vector3 CalculateDropPosition(Vector3 center, float minRadius, float maxRadius)
        {
            int lootCheckLayerId = LayerMask.NameToLayer(Constant.Layer.LootCheckLayerName);
            int layerMask = lootCheckLayerId >= 0 ? (1 << lootCheckLayerId) : 0;
            IRoundRandomSource random = GameEntry.Random?.Random;

            if (layerMask != 0)
            {
                const int maxAttempts = 16;
                for (int i = 0; i < maxAttempts; i++)
                {
                    float angle = random != null
                        ? random.NextInt(0, 360) * Mathf.Deg2Rad
                        : UnityEngine.Random.Range(0f, Mathf.PI * 2f);

                    float distanceRatio = random != null
                        ? (random.NextInt(0, 1001) / 1000f)
                        : UnityEngine.Random.value;
                    float distance = Mathf.Lerp(minRadius, maxRadius, distanceRatio);

                    Vector2 candidate = (Vector2)center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    Collider2D hit = Physics2D.OverlapPoint(candidate, layerMask);
                    if (hit != null)
                    {
                        return new Vector3(candidate.x, candidate.y, center.z);
                    }
                }

                Collider2D centerHit = Physics2D.OverlapPoint(center, layerMask);
                if (centerHit != null)
                {
                    return center;
                }
            }

            return center;
        }
    }
}
