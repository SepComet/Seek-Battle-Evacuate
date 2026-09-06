using System;
using System.Collections.Generic;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Exploration;

namespace SepCore.Run
{
    /// <summary>
    /// 单局探索运行时数据核心（聚合根）。
    /// 持有共享背包、保险箱、出战队伍状态与单局元数据，是单局探索事实的唯一来源。
    /// </summary>
    public sealed class RoundSession
    {
        public DifficultyTier Difficulty { get; }

        public long Seed { get; }

        public long StartTimeUtcMs { get; }

        public RoundItemContainer Backpack { get; }

        public RoundItemContainer SafeCase { get; }

        public IReadOnlyList<RoundCharacterState> Party { get; }

        public int TotalLootValue => Backpack.TotalValue + SafeCase.TotalValue;

        public int FreeBackpackSlots => Backpack.FreeSlotsCount;

        public RoundSession(
            DifficultyTier difficulty,
            long seed,
            long startTimeUtcMs,
            RoundItemContainer backpack,
            RoundItemContainer safeCase,
            IReadOnlyList<RoundCharacterState> party)
        {
            Difficulty = difficulty;
            Seed = seed;
            StartTimeUtcMs = startTimeUtcMs;
            Backpack = backpack ?? throw new ArgumentNullException(nameof(backpack));
            SafeCase = safeCase ?? throw new ArgumentNullException(nameof(safeCase));
            Party = party ?? throw new ArgumentNullException(nameof(party));
        }

        /// <summary>
        /// 在共享背包与保险箱之间转移物品。
        /// </summary>
        /// <param name="fromBackpackToSafe">true 为背包移入保险箱，false 为保险箱移入背包。</param>
        /// <param name="fromSlotIndex">源容器槽位索引。</param>
        /// <param name="count">转移数量。</param>
        /// <param name="movedCount">实际成功转移的数量。</param>
        public bool TryMoveBetweenContainers(bool fromBackpackToSafe, int fromSlotIndex, int count, out int movedCount)
        {
            RoundItemContainer source = fromBackpackToSafe ? Backpack : SafeCase;
            RoundItemContainer target = fromBackpackToSafe ? SafeCase : Backpack;
            return source.TryMoveTo(fromSlotIndex, target, count, out movedCount);
        }

        /// <summary>
        /// 获取供战斗系统使用的 PlayerUnitState 列表。
        /// </summary>
        public List<PlayerUnitState> ToPlayerUnitStates()
        {
            List<PlayerUnitState> list = new List<PlayerUnitState>(Party.Count);
            for (int i = 0; i < Party.Count; i++)
            {
                list.Add(Party[i].ToPlayerUnitState());
            }
            return list;
        }

        /// <summary>
        /// 回写战斗结果中的玩家血量、蓝量与阵亡复活状态。
        /// </summary>
        public void ApplyBattleResult(BattleResult result, int reviveHp, int reviveMp)
        {
            if (result == null || result.Players == null)
            {
                return;
            }

            if (result.Outcome == BattleOutcomeType.TotalDefeat)
            {
                return;
            }

            for (int i = 0; i < result.Players.Count; i++)
            {
                BattlePlayerResult playerRes = result.Players[i];
                for (int j = 0; j < Party.Count; j++)
                {
                    if (Party[j].CharacterId == playerRes.CharacterId)
                    {
                        Party[j].ApplyBattleResult(playerRes, reviveHp, reviveMp);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 工厂方法：基于存档、配置与配表提供者构建完整的 RunSession 实例。
        /// </summary>
        public static RoundSession Create(
            DifficultyTier difficulty,
            long seed,
            SaveData save,
            GlobalConfig global,
            Func<int, CharacterConfig> charGetter,
            Func<int, ItemConfig> itemGetter,
            long startTimeUtcMs = 0)
        {
            if (global == null)
            {
                throw new ArgumentNullException(nameof(global));
            }

            if (itemGetter == null)
            {
                throw new ArgumentNullException(nameof(itemGetter));
            }

            if (startTimeUtcMs <= 0)
            {
                startTimeUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            // 1. 构建出战角色状态
            List<PlayerUnitState> playerUnits = PlayerPartyBuilder.Build(
                save,
                charGetter,
                itemGetter,
                global.NewGameCharacterIds);

            List<RoundCharacterState> party = new List<RoundCharacterState>(playerUnits.Count);
            foreach (PlayerUnitState u in playerUnits)
            {
                int weaponId = 0;
                int armorId = 0;
                if (save?.characters != null)
                {
                    foreach (CharacterSave cs in save.characters)
                    {
                        if (cs.characterId == u.CharacterId)
                        {
                            weaponId = cs.weaponItemId;
                            armorId = cs.armorItemId;
                            break;
                        }
                    }
                }

                party.Add(RoundCharacterState.FromPlayerUnitState(u, weaponId, armorId));
            }

            // 2. 构建背包与保险箱
            int backpackSlots = global.BackpackSlotCount > 0 ? global.BackpackSlotCount : 16;
            int safeSlots = global.SecureSlotCount > 0 ? global.SecureSlotCount : 4;

            RoundItemContainer backpack = new RoundItemContainer(backpackSlots, itemGetter);
            RoundItemContainer safeCase = new RoundItemContainer(safeSlots, itemGetter);

            // 3. 载入战备携带物品进入背包
            if (save?.loadout?.carriedItems != null)
            {
                foreach (ItemStack stack in save.loadout.carriedItems)
                {
                    if (stack.itemId > 0 && stack.count > 0)
                    {
                        backpack.TryAddItem(stack.itemId, stack.count);
                    }
                }
            }

            return new RoundSession(difficulty, seed, startTimeUtcMs, backpack, safeCase, party);
        }
    }
}
