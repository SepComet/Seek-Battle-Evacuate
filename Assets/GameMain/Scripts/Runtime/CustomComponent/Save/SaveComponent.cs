using System;
using System.Collections.Generic;
using System.IO;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// 存档组件。
    /// 负责游戏存档的读取与写入，数据结构见 Docs/Tech/02_SaveData.md。
    /// 存档只包含局外状态与已结束单局的结算记录；进行中的单局使用临时状态，不写入存档。
    /// </summary>
    public class SaveComponent : GameFrameworkComponent
    {
        /// <summary>
        /// 存档文件名。
        /// </summary>
        [SerializeField] private string _fileName = "save.json";

        private SaveData _data = null;
        private bool _hasSave = false;

        /// <summary>
        /// 获取当前内存中的存档数据；未加载或未创建时为 null。
        /// </summary>
        public SaveData Data => _data;

        /// <summary>
        /// 获取磁盘上是否已存在存档文件。
        /// </summary>
        public bool HasSave => _hasSave;

        /// <summary>
        /// 获取存档数据是否可用。
        /// </summary>
        public bool IsReady => _data != null;

        /// <summary>
        /// 获取存档文件的完整路径。
        /// </summary>
        public string SaveFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, _fileName));

        /// <summary>
        /// 从磁盘加载存档。
        /// 文件不存在时返回 true 且 HasSave 为 false；解析失败时返回 false。
        /// </summary>
        public bool Load()
        {
            string path = SaveFilePath;
            if (!File.Exists(path))
            {
                _hasSave = false;
                _data = null;
                Log.Info("Save file '{0}' does not exist.", path);
                return true;
            }

            try
            {
                string json = File.ReadAllText(path);
                SaveData data = SaveData.FromJson(json);
                if (data == null)
                {
                    Log.Error("Parse save file '{0}' failure.", path);
                    _hasSave = true;
                    _data = null;
                    return false;
                }

                if (data.version != SaveData.CurrentVersion)
                {
                    Log.Warning("Save file '{0}' version '{1}' does not match current version '{2}'.",
                        path, data.version, SaveData.CurrentVersion);
                }

                _data = data;
                _hasSave = true;
                Log.Info("Load save file '{0}' OK.", path);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("Load save file '{0}' failure with exception '{1}'.", path, exception);
                _hasSave = true;
                _data = null;
                return false;
            }
        }

        /// <summary>
        /// 创建新存档数据（不写盘）。
        /// 角色与初始装备来自配表：GlobalConfig.NewGameCharacterIds 与 CharacterConfig 的初始装备栏。
        /// </summary>
        public bool CreateNewGame()
        {
            if (!GameEntry.Luban.IsReady)
            {
                Log.Error("Can not create new game before Luban data tables are loaded.");
                return false;
            }

            GlobalConfig global = GameEntry.Luban.Global.Data;
            SaveData data = new SaveData
            {
                version = SaveData.CurrentVersion,
                updatedAt = GetTimestamp(),
                mainWarehouse = CreateInitialWarehouse(),
                characters = new List<CharacterSave>(),
                loadout = new LoadoutSave(),
                runHistory = new List<RoundRecord>(),
            };

            foreach (int characterId in global.NewGameCharacterIds)
            {
                CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(characterId);
                int weaponItemId = config != null ? config.WeaponItemId : 0;
                int armorItemId = config != null ? config.ArmorItemId : 0;
                data.characters.Add(new CharacterSave(characterId, weaponItemId, armorItemId));
            }

            data.loadout.Normalize();
            _data = data;
            _hasSave = false;
            Log.Info("Create new game save data OK.");
            return true;
        }

        /// <summary>
        /// 将当前存档数据写入磁盘。
        /// 写入前自动更新 updatedAt；使用临时文件替换，避免写入中断损坏存档。
        /// </summary>
        public bool Save()
        {
            if (_data == null)
            {
                Log.Warning("Can not save because save data does not exist.");
                return false;
            }

            _data.updatedAt = GetTimestamp();
            string json = _data.ToJson();

            string path = SaveFilePath;
            string tempPath = path + ".tmp";
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
                _hasSave = true;
                Log.Info("Save save file '{0}' OK.", path);
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("Save save file '{0}' failure with exception '{1}'.", path, exception);
                return false;
            }
        }

        private static List<ItemStack> CreateInitialWarehouse()
        {
            // 原型调试种子：覆盖全部物品类型与稀有度，用于筛选等功能开发。
            // 后续由配表或正式新存档规则替代。
            return new List<ItemStack>
            {
                new ItemStack(5001, 20), // 旧硬币   战利品/白
                new ItemStack(5002, 7),  // 宝石碎片 战利品/绿
                new ItemStack(5003, 3),  // 古代零件 战利品/蓝
                new ItemStack(5004, 1),  // 精致核心 战利品/金
                new ItemStack(5005, 1),  // 神秘遗物 战利品/红
                new ItemStack(5101, 1),  // 训练剑   装备/白
                new ItemStack(5102, 1),  // 法杖     装备/蓝
                new ItemStack(5201, 1),  // 布甲     装备/绿
                new ItemStack(5202, 1),  // 术士长袍 装备/金
                new ItemStack(5301, 5),  // 恢复药   消耗品/白
            };
        }

        private static long GetTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}