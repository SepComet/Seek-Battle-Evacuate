using System;
using SepCore.Battle;
using SepCore.Definition;

namespace SepCore.Run
{
    /// <summary>
    /// 单局出战角色实时状态。
    /// 跨战斗持续维护当前 HP/MP、装备与最终属性，是单局探索角色的唯一事实来源。
    /// </summary>
    public sealed class RoundCharacterState
    {
        public int CharacterId { get; set; }

        public int PartyOrder { get; set; }

        public int CurrentHp { get; set; }

        public int CurrentMp { get; set; }

        public int MaxHp { get; set; }

        public int MaxMp { get; set; }

        public int Atk { get; set; }

        public int Mat { get; set; }

        public int Speed { get; set; }

        public int WeaponItemId { get; set; }

        public int ArmorItemId { get; set; }

        public int AttackActionId { get; set; }

        public int SkillActionId { get; set; }

        /// <summary>
        /// 角色当前是否存活。
        /// </summary>
        public bool IsAlive => CurrentHp > 0;

        /// <summary>
        /// 转化为战斗组件所需初始玩家状态契约。
        /// </summary>
        public PlayerUnitState ToPlayerUnitState()
        {
            return new PlayerUnitState
            {
                CharacterId = CharacterId,
                PartyOrder = PartyOrder,
                CurrentHp = CurrentHp,
                CurrentMp = CurrentMp,
                MaxHp = MaxHp,
                MaxMp = MaxMp,
                Atk = Atk,
                Mat = Mat,
                Speed = Speed,
                AttackActionId = AttackActionId,
                SkillActionId = SkillActionId
            };
        }

        /// <summary>
        /// 从 PlayerUnitState 构建出战角色状态。
        /// </summary>
        public static RoundCharacterState FromPlayerUnitState(PlayerUnitState state, int weaponItemId = 0, int armorItemId = 0)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return new RoundCharacterState
            {
                CharacterId = state.CharacterId,
                PartyOrder = state.PartyOrder,
                CurrentHp = state.CurrentHp,
                CurrentMp = state.CurrentMp,
                MaxHp = state.MaxHp,
                MaxMp = state.MaxMp,
                Atk = state.Atk,
                Mat = state.Mat,
                Speed = state.Speed,
                WeaponItemId = weaponItemId,
                ArmorItemId = armorItemId,
                AttackActionId = state.AttackActionId,
                SkillActionId = state.SkillActionId
            };
        }

        /// <summary>
        /// 战后同步回写 HP 与 MP，阵亡者按复活值恢复（非全员阵亡时）。
        /// </summary>
        public void ApplyBattleResult(BattlePlayerResult result, int reviveHp, int reviveMp)
        {
            if (result == null)
            {
                return;
            }

            if (result.WasDefeated)
            {
                CurrentHp = Math.Min(MaxHp, Math.Max(1, reviveHp));
                CurrentMp = Math.Min(MaxMp, Math.Max(1, reviveMp));
            }
            else
            {
                CurrentHp = Math.Clamp(result.CurrentHp, 0, MaxHp);
                CurrentMp = Math.Clamp(result.CurrentMp, 0, MaxMp);
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0) return;
            CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
        }

        public void TakeDamage(int amount)
        {
            if (amount <= 0) return;
            CurrentHp = Math.Max(0, CurrentHp - amount);
        }

        public void RecoverMp(int amount)
        {
            if (amount <= 0) return;
            CurrentMp = Math.Min(MaxMp, CurrentMp + amount);
        }

        public void ConsumeMp(int amount)
        {
            if (amount <= 0) return;
            CurrentMp = Math.Max(0, CurrentMp - amount);
        }

        /// <summary>
        /// 穿戴装备并应用属性加成。
        /// </summary>
        public void EquipItem(ItemConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (config.EquipSlot == EquipmentSlotType.Weapon)
            {
                WeaponItemId = config.Id;
            }
            else if (config.EquipSlot == EquipmentSlotType.Armor)
            {
                ArmorItemId = config.Id;
            }
            else
            {
                return;
            }

            MaxHp += config.MaxHpBonus;
            MaxMp += config.MaxMpBonus;
            Atk += config.AtkBonus;
            Mat += config.MatBonus;
            Speed += config.SpeedBonus;

            CurrentHp = Math.Clamp(CurrentHp, 1, MaxHp);
            CurrentMp = Math.Clamp(CurrentMp, 0, MaxMp);
        }

        /// <summary>
        /// 卸下装备并扣除属性加成，生命值截断时至少保留 1 点。
        /// </summary>
        public void UnequipItem(ItemConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (config.EquipSlot == EquipmentSlotType.Weapon && WeaponItemId == config.Id)
            {
                WeaponItemId = 0;
            }
            else if (config.EquipSlot == EquipmentSlotType.Armor && ArmorItemId == config.Id)
            {
                ArmorItemId = 0;
            }
            else
            {
                return;
            }

            MaxHp -= config.MaxHpBonus;
            MaxMp -= config.MaxMpBonus;
            Atk -= config.AtkBonus;
            Mat -= config.MatBonus;
            Speed -= config.SpeedBonus;

            CurrentHp = Math.Max(1, Math.Min(CurrentHp, MaxHp));
            CurrentMp = Math.Max(0, Math.Min(CurrentMp, MaxMp));
        }
    }
}
