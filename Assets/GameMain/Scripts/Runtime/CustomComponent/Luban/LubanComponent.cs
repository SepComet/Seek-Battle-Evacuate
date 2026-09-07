using GameFramework.Resource;
using System;
using System.Collections.Generic;
using Luban;
using SepCore.Definition;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.CustomComponent
{
    /// <summary>
    /// Luban 数据表组件。
    /// 生成的数据结构与枚举位于 Assets/GameMain/Scripts/Base/Gen。
    /// 负责加载 Luban 导出的二进制数据表（.bytes）并构建 SepCore.Definition.Tables。
    /// 注意：表名列表需与 SepCore.Definition.Tables 构造函数保持一致。
    /// </summary>
    public class LubanComponent : GameFrameworkComponent
    {
        private static readonly string[] TableNames = new string[]
        {
            "tbglobalconfig",
            "tbrarityconfig",
            "tbdifficultyconfig",
            "tbcharacterconfig",
            "tbbattleactionconfig",
            "tbthreatlevelconfig",
            "tbenemyconfig",
            "tbenemypartyconfig",
            "tbitemconfig",
            "tbresourcepointconfig",
            "tbenemydropconfig",
            "tbentityconfig",
            "tbmusicconfig",
            "tbsceneconfig",
            "tbsoundconfig",
            "tbuiformconfig",
            "tbuisoundconfig",
            "tbformattext",
            "tbspriteconfig"
        };

        private sealed class TableAccessor
        {
            public Func<Tables, int, object> RowGetter;
            public Func<Tables, string, object> StringRowGetter;
            public Func<Tables, object> ListGetter;
        }

        private static readonly Dictionary<Type, TableAccessor> TableAccessors =
            new()
            {
                {
                    typeof(CharacterConfig),
                    Accessor(tables => id => tables.TbCharacterConfig.GetOrDefault(id),
                        tables => tables.TbCharacterConfig.DataList)
                },
                {
                    typeof(BattleActionConfig),
                    Accessor(tables => id => tables.TbBattleActionConfig.GetOrDefault(id),
                        tables => tables.TbBattleActionConfig.DataList)
                },
                {
                    typeof(ThreatLevelConfig),
                    Accessor(tables => id => tables.TbThreatLevelConfig.GetOrDefault((EnemyPartyThreatLevel)id),
                        tables => tables.TbThreatLevelConfig.DataList)
                },
                {
                    typeof(EnemyConfig),
                    Accessor(tables => id => tables.TbEnemyConfig.GetOrDefault(id),
                        tables => tables.TbEnemyConfig.DataList)
                },
                {
                    typeof(EnemyPartyConfig),
                    Accessor(tables => id => tables.TbEnemyPartyConfig.GetOrDefault(id),
                        tables => tables.TbEnemyPartyConfig.DataList)
                },
                {
                    typeof(ItemConfig),
                    Accessor(tables => id => tables.TbItemConfig.GetOrDefault(id),
                        tables => tables.TbItemConfig.DataList)
                },
                {
                    typeof(ResourcePointConfig),
                    Accessor(tables => id => tables.TbResourcePointConfig.GetOrDefault(id),
                        tables => tables.TbResourcePointConfig.DataList)
                },
                {
                    typeof(EnemyDropConfig),
                    Accessor(tables => id => tables.TbEnemyDropConfig.GetOrDefault((EnemyPartyThreatLevel)id),
                        tables => tables.TbEnemyDropConfig.DataList)
                },
                {
                    typeof(RarityConfig),
                    Accessor(tables => id => tables.TbRarityConfig.GetOrDefault((Rarity)id),
                        tables => tables.TbRarityConfig.DataList)
                },
                {
                    typeof(DifficultyConfig),
                    Accessor(tables => id => tables.TbDifficultyConfig.GetOrDefault((DifficultyTier)id),
                        tables => tables.TbDifficultyConfig.DataList)
                },
                {
                    typeof(EntityConfig),
                    StringAccessor(tables => key => tables.TbEntityConfig.GetOrDefault(key),
                        tables => tables.TbEntityConfig.DataList)
                },
                {
                    typeof(MusicConfig),
                    Accessor(tables => id => tables.TbMusicConfig.GetOrDefault(id),
                        tables => tables.TbMusicConfig.DataList)
                },
                {
                    typeof(SceneConfig),
                    Accessor(tables => id => tables.TbSceneConfig.GetOrDefault((SceneType)id),
                        tables => tables.TbSceneConfig.DataList)
                },
                {
                    typeof(SoundConfig),
                    Accessor(tables => id => tables.TbSoundConfig.GetOrDefault(id),
                        tables => tables.TbSoundConfig.DataList)
                },
                {
                    typeof(UIFormConfig),
                    Accessor(tables => id => tables.TbUIFormConfig.GetOrDefault((UIFormType)id),
                        tables => tables.TbUIFormConfig.DataList)
                },
                {
                    typeof(UISoundConfig),
                    Accessor(tables => id => tables.TbUISoundConfig.GetOrDefault(id),
                        tables => tables.TbUISoundConfig.DataList)
                },
                {
                    typeof(SpriteConfig),
                    StringAccessor(tables => key => tables.TbSpriteConfig.GetOrDefault(key),
                        tables => tables.TbSpriteConfig.DataList)
                },
            };

        private static TableAccessor Accessor<TValue>(Func<Tables, Func<int, object>> rowGetterFactory,
            Func<Tables, List<TValue>> listGetter) where TValue : BeanBase
        {
            return new TableAccessor
            {
                RowGetter = (tables, id) => rowGetterFactory(tables)(id),
                ListGetter = tables => listGetter(tables)
            };
        }

        private static TableAccessor StringAccessor<TValue>(Func<Tables, Func<string, object>> rowGetterFactory,
            Func<Tables, List<TValue>> listGetter) where TValue : BeanBase
        {
            return new TableAccessor
            {
                StringRowGetter = (tables, key) => rowGetterFactory(tables)(key),
                ListGetter = tables => listGetter(tables)
            };
        }

        private Dictionary<string, byte[]> _tableBytes = null;
        private int _remainCount = 0;
        private Action _onLoadSuccess = null;
        private Action<string> _onLoadFailure = null;

        /// <summary>
        /// 获取 Luban 数据表管理器，加载完成后可用。
        /// </summary>
        public Tables Tables { get; private set; }

        /// <summary>
        /// 获取数据表是否已加载完成。
        /// </summary>
        public bool IsReady => Tables != null;

        public TbGlobalConfig Global => Tables.TbGlobalConfig;
        
        /// <summary>
        /// 按 id 获取数据行，未找到时返回 null。
        /// </summary>
        /// <typeparam name="T">数据行类型（对应生成的 xxxConfig 类）。</typeparam>
        /// <param name="id">数据行主键。</param>
        /// <returns>数据行，未找到时返回 null。</returns>
        public T Get<T>(int id) where T : class
        {
            if (Tables == null)
            {
                throw new InvalidOperationException("Luban data tables are not loaded yet.");
            }

            TableAccessor accessor;
            if (!TableAccessors.TryGetValue(typeof(T), out accessor))
            {
                throw new NotSupportedException("No table getter is registered for type '" + typeof(T).FullName + "'.");
            }

            return (T)accessor.RowGetter(Tables, id);
        }

        /// <summary>
        /// 按字符串主键获取数据行，未找到时返回 null。
        /// </summary>
        /// <typeparam name="T">数据行类型（对应生成的 xxxConfig 类）。</typeparam>
        /// <param name="key">数据行字符串主键。</param>
        /// <returns>数据行，未找到时返回 null。</returns>
        public T Get<T>(string key) where T : class
        {
            if (Tables == null)
            {
                throw new InvalidOperationException("Luban data tables are not loaded yet.");
            }

            TableAccessor accessor;
            if (!TableAccessors.TryGetValue(typeof(T), out accessor))
            {
                throw new NotSupportedException("No table getter is registered for type '" + typeof(T).FullName + "'.");
            }

            if (accessor.StringRowGetter == null)
            {
                throw new NotSupportedException("Table for type '" + typeof(T).FullName + "' does not use a string key.");
            }

            return (T)accessor.StringRowGetter(Tables, key);
        }

        /// <summary>
        /// 获取整个表的所有数据行。
        /// </summary>
        /// <typeparam name="T">数据行类型（对应生成的 xxxConfig 类）。</typeparam>
        /// <returns>表内全部数据行，可安全遍历。</returns>
        public IReadOnlyList<T> GetTable<T>() where T : class
        {
            if (Tables == null)
            {
                throw new InvalidOperationException("Luban data tables are not loaded yet.");
            }

            TableAccessor accessor;
            if (!TableAccessors.TryGetValue(typeof(T), out accessor))
            {
                throw new NotSupportedException("No table getter is registered for type '" + typeof(T).FullName + "'.");
            }

            return (IReadOnlyList<T>)accessor.ListGetter(Tables);
        }

        /// <summary>
        /// 异步加载所有数据表。
        /// </summary>
        /// <param name="onSuccess">加载成功回调。</param>
        /// <param name="onFailure">加载失败回调，参数为错误信息。</param>
        public void LoadTables(Action onSuccess, Action<string> onFailure)
        {
            if (IsReady)
            {
                onSuccess?.Invoke();
                return;
            }

            _onLoadSuccess = onSuccess;
            _onLoadFailure = onFailure;
            _tableBytes = new Dictionary<string, byte[]>(TableNames.Length);
            _remainCount = TableNames.Length;

            foreach (string tableName in TableNames)
            {
                GameEntry.Resource.LoadAsset(GetTableAssetPath(tableName), Constant.AssetPriority.DataTableAsset,
                    new LoadAssetCallbacks(OnLoadTableSuccess, OnLoadTableFailure), tableName);
            }
        }

        private void OnLoadTableSuccess(string assetName, object asset, float duration, object userData)
        {
            string tableName = (string)userData;
            TextAsset textAsset = asset as TextAsset;
            if (textAsset == null)
            {
                OnLoadTableFailure(assetName, LoadResourceStatus.AssetError, "Table asset is invalid.", userData);
                return;
            }

            _tableBytes[tableName] = textAsset.bytes;
            if (--_remainCount > 0)
            {
                return;
            }

            try
            {
                Tables = new SepCore.Definition.Tables(name => new Luban.ByteBuf(_tableBytes[name]));
                _tableBytes = null;
                Log.Info("Load Luban data tables OK.");
                Action onLoadSuccess = _onLoadSuccess;
                _onLoadSuccess = null;
                _onLoadFailure = null;
                onLoadSuccess?.Invoke();
            }
            catch (Exception exception)
            {
                Log.Error("Build Luban data tables failure with exception '{0}'.", exception);
                Action<string> onLoadFailure = _onLoadFailure;
                _onLoadSuccess = null;
                _onLoadFailure = null;
                onLoadFailure?.Invoke(exception.ToString());
            }
        }

        private void OnLoadTableFailure(string assetName, LoadResourceStatus status, string errorMessage,
            object userData)
        {
            Log.Error("Can not load data table '{0}' from '{1}' with error message '{2}'.", (string)userData, assetName,
                errorMessage);
            Action<string> onLoadFailure = _onLoadFailure;
            _onLoadSuccess = null;
            _onLoadFailure = null;
            onLoadFailure?.Invoke(errorMessage);
        }

        private static string GetTableAssetPath(string tableName)
        {
            return GameFramework.Utility.Text.Format("Assets/GameMain/DataTables/{0}.bytes", tableName);
        }
    }
}
