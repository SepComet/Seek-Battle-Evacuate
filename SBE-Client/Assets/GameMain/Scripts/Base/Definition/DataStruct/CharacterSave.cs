namespace SepCore.Definition
{
    /// <summary>
    /// 存档中的角色。
    /// </summary>
    [System.Serializable]
    public struct CharacterSave
    {
        /// <summary>
        /// 角色 ID，对应 CharacterConfig.Id。
        /// </summary>
        public int characterId;

        /// <summary>
        /// 武器栏物品 ID，0 表示空栏。
        /// </summary>
        public int weaponItemId;

        /// <summary>
        /// 防具栏物品 ID，0 表示空栏。
        /// </summary>
        public int armorItemId;

        public CharacterSave(int characterId, int weaponItemId, int armorItemId)
        {
            this.characterId = characterId;
            this.weaponItemId = weaponItemId;
            this.armorItemId = armorItemId;
        }
    }
}