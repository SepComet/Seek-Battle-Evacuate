using System;
using System.Collections.Generic;
using System.IO;
using SepCore.Definition;
using SepCore.Run;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// 模块化存档组件。
    /// 负责游戏存档的拆分存储与按需落盘：
    /// 1. 仓库数据 (warehouse_data.json)：主仓库物品清单与数量；
    /// 2. 仓库布局 (warehouse_layout.json)：主仓库二维网格坐标与旋转；
    /// 3. 角色数据 (character_data.json)：角色装备与战备配置；
    /// 4. 元数据 (meta_data.json)：版本、时间戳与历史战绩。
    /// 支持脏标记管理（Dirty Flags），仅落盘被修改的文件，杜绝频繁 I/O。
    /// </summary>
    public class SaveComponent : GameFrameworkComponent
    {
        private const string SaveSubFolder = "Save";
        private const string WarehouseDataFileName = "warehouse_data.json";
        private const string WarehouseLayoutFileName = "warehouse_layout.json";
        private const string CharacterDataFileName = "character_data.json";
        private const string MetaDataFileName = "meta_data.json";
        private const string LegacySaveFileName = "save.json";

        private SaveData _data = null;
        private bool _hasSave = false;

        // 脏标记
        private bool _isWarehouseDataDirty = false;
        private bool _isWarehouseLayoutDirty = false;
        private bool _isCharacterDataDirty = false;
        private bool _isMetaDirty = false;

        public SaveData Data => _data;
        public bool HasSave => _hasSave;
        public bool IsReady => _data != null;

        public string SaveDirectoryPath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, SaveSubFolder));

        public string WarehouseDataFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(SaveDirectoryPath, WarehouseDataFileName));

        public string WarehouseLayoutFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(SaveDirectoryPath, WarehouseLayoutFileName));

        public string CharacterDataFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(SaveDirectoryPath, CharacterDataFileName));

        public string MetaDataFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(SaveDirectoryPath, MetaDataFileName));

        public string LegacySaveFilePath => GameFramework.Utility.Path.GetRegularPath(
            Path.Combine(Application.persistentDataPath, LegacySaveFileName));

        // 标记脏状态
        public void MarkWarehouseDataDirty() => _isWarehouseDataDirty = true;
        public void MarkWarehouseLayoutDirty() => _isWarehouseLayoutDirty = true;
        public void MarkCharacterDataDirty() => _isCharacterDataDirty = true;
        public void MarkMetaDirty() => _isMetaDirty = true;

        public void MarkAllDirty()
        {
            _isWarehouseDataDirty = true;
            _isWarehouseLayoutDirty = true;
            _isCharacterDataDirty = true;
            _isMetaDirty = true;
        }

        /// <summary>
        /// 从磁盘加载存档。
        /// 优先读取模块化拆分文件；若不存在则自动尝试读取老版 save.json 并平滑迁移为新格式。
        /// </summary>
        public bool Load()
        {
            if (File.Exists(WarehouseDataFilePath) || File.Exists(CharacterDataFilePath))
            {
                return LoadModularSave();
            }

            if (File.Exists(LegacySaveFilePath))
            {
                return LoadAndMigrateLegacySave();
            }

            _hasSave = false;
            _data = null;
            Log.Info("No save file exists.");
            return true;
        }

        private bool LoadModularSave()
        {
            try
            {
                WarehouseDataSave whData = null;
                if (File.Exists(WarehouseDataFilePath))
                {
                    string json = File.ReadAllText(WarehouseDataFilePath);
                    whData = WarehouseDataSave.FromJson(json);
                }

                WarehouseLayoutSave whLayout = null;
                if (File.Exists(WarehouseLayoutFilePath))
                {
                    string json = File.ReadAllText(WarehouseLayoutFilePath);
                    whLayout = WarehouseLayoutSave.FromJson(json);
                }

                CharacterDataSave charData = null;
                if (File.Exists(CharacterDataFilePath))
                {
                    string json = File.ReadAllText(CharacterDataFilePath);
                    charData = CharacterDataSave.FromJson(json);
                }

                MetaDataSave metaData = null;
                if (File.Exists(MetaDataFilePath))
                {
                    string json = File.ReadAllText(MetaDataFilePath);
                    metaData = MetaDataSave.FromJson(json);
                }

                GlobalConfig global = GameEntry.Luban.Global?.Data;
                if (global == null)
                {
                    Log.Error("Global config is not ready.");
                    _hasSave = true;
                    _data = null;
                    return false;
                }

                int columns = global.WarehouseFixedColumn;
                int totalSlotCount = global.WarehouseSlotCount;
                int rows = (totalSlotCount + columns - 1) / columns;
                GridItemContainer container = new GridItemContainer(columns, rows, id => GameEntry.Luban.Get<ItemConfig>(id));
                container.LoadFromModularSave(whData, whLayout);

                _data = new SaveData
                {
                    version = metaData != null ? metaData.version : SaveData.CurrentVersion,
                    updatedAt = metaData != null ? metaData.updatedAt : GetTimestamp(),
                    mainWarehouse = container.ToSaveData(),
                    characters = charData != null && charData.characters != null ? charData.characters : new List<CharacterSave>(),
                    loadout = charData != null && charData.loadout != null ? charData.loadout : new LoadoutSave(),
                    runHistory = metaData != null && metaData.runHistory != null ? metaData.runHistory : new List<RoundRecord>()
                };

                _data.loadout.Normalize();
                _hasSave = true;
                ClearDirtyFlags();
                Log.Info("Load modular save files OK.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Load modular save files failure: {0}", ex);
                _hasSave = true;
                _data = null;
                return false;
            }
        }

        private bool LoadAndMigrateLegacySave()
        {
            try
            {
                string legacyPath = LegacySaveFilePath;
                string json = File.ReadAllText(legacyPath);
                SaveData legacyData = SaveData.FromJson(json);
                if (legacyData == null)
                {
                    Log.Error("Parse legacy save file '{0}' failure.", legacyPath);
                    _hasSave = true;
                    _data = null;
                    return false;
                }

                _data = legacyData;
                _hasSave = true;
                MarkAllDirty();
                SaveDirty();
                Log.Info("Migrated legacy save.json to modular save files successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Load legacy save file failure: {0}", ex);
                _hasSave = true;
                _data = null;
                return false;
            }
        }

        /// <summary>
        /// 创建新存档数据（不写盘），并标记全量脏状态。
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
            MarkAllDirty();
            Log.Info("Create new game save data OK.");
            return true;
        }

        /// <summary>
        /// 按需落盘：仅将标记为 dirty 的文件写入磁盘，未变动的文件不执行任何 I/O。
        /// </summary>
        public bool SaveDirty()
        {
            if (_data == null)
            {
                return false;
            }

            if (!_isWarehouseDataDirty && !_isWarehouseLayoutDirty && !_isCharacterDataDirty && !_isMetaDirty)
            {
                return true;
            }

            bool allSuccess = true;
            _data.updatedAt = GetTimestamp();

            // 1. 仓库物资或布局发生变动
            if (_isWarehouseDataDirty || _isWarehouseLayoutDirty)
            {
                GlobalConfig global = GameEntry.Luban.Global?.Data;
                if (global == null)
                {
                    Log.Error("Global config is not ready.");
                    return false;
                }

                int columns = global.WarehouseFixedColumn;
                int totalSlotCount = global.WarehouseSlotCount;
                int rows = (totalSlotCount + columns - 1) / columns;
                GridItemContainer container = new GridItemContainer(columns, rows, id => GameEntry.Luban.Get<ItemConfig>(id));
                container.LoadFromSaveData(_data.mainWarehouse);
                var (whData, whLayout) = container.ExportModularSave();

                if (_isWarehouseDataDirty)
                {
                    if (SafeWriteJson(WarehouseDataFilePath, whData.ToJson()))
                    {
                        _isWarehouseDataDirty = false;
                    }
                    else
                    {
                        allSuccess = false;
                    }
                }

                if (_isWarehouseLayoutDirty)
                {
                    if (SafeWriteJson(WarehouseLayoutFilePath, whLayout.ToJson()))
                    {
                        _isWarehouseLayoutDirty = false;
                    }
                    else
                    {
                        allSuccess = false;
                    }
                }
            }

            // 2. 角色或战备变动
            if (_isCharacterDataDirty)
            {
                CharacterDataSave charData = new CharacterDataSave
                {
                    characters = _data.characters,
                    loadout = _data.loadout
                };

                if (SafeWriteJson(CharacterDataFilePath, charData.ToJson()))
                {
                    _isCharacterDataDirty = false;
                }
                else
                {
                    allSuccess = false;
                }
            }

            // 3. 元数据或战绩变动
            if (_isMetaDirty)
            {
                MetaDataSave meta = new MetaDataSave
                {
                    version = _data.version,
                    updatedAt = _data.updatedAt,
                    runHistory = _data.runHistory
                };

                if (SafeWriteJson(MetaDataFilePath, meta.ToJson()))
                {
                    _isMetaDirty = false;
                }
                else
                {
                    allSuccess = false;
                }
            }

            if (allSuccess)
            {
                _hasSave = true;
                Log.Info("Save dirty save files OK.");
            }

            return allSuccess;
        }

        /// <summary>
        /// 全量落盘所有文件（覆盖 Save() 接口契约）。
        /// </summary>
        public bool Save()
        {
            MarkAllDirty();
            return SaveDirty();
        }

        private static bool SafeWriteJson(string filePath, string json)
        {
            string tempPath = filePath + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(tempPath, json);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                File.Move(tempPath, filePath);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Save save file '{0}' failure with exception '{1}'.", filePath, ex);
                return false;
            }
        }

        private void ClearDirtyFlags()
        {
            _isWarehouseDataDirty = false;
            _isWarehouseLayoutDirty = false;
            _isCharacterDataDirty = false;
            _isMetaDirty = false;
        }

        private void OnApplicationQuit()
        {
            SaveDirty();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                SaveDirty();
            }
        }

        private static List<GridItemStack> CreateInitialWarehouse()
        {
            return new List<GridItemStack>
            {
                new GridItemStack(5001, 20), // 旧硬币   战利品/白
                new GridItemStack(5002, 7),  // 宝石碎片 战利品/绿
                new GridItemStack(5003, 3),  // 古代零件 战利品/蓝
                new GridItemStack(5004, 1),  // 精致核心 战利品/金
                new GridItemStack(5005, 1),  // 神秘遗物 战利品/红
                new GridItemStack(5101, 1),  // 训练剑   装备/白
                new GridItemStack(5102, 1),  // 法杖     装备/蓝
                new GridItemStack(5201, 1),  // 布甲     装备/绿
                new GridItemStack(5202, 1),  // 术士长袍 装备/金
                new GridItemStack(5301, 5),  // 恢复药   消耗品/白
            };
        }

        private static long GetTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}