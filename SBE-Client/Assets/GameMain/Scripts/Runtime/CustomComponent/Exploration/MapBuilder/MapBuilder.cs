using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using SepCore.Utility;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 根据固定地图定义、难度和本局主种子生成完整地图数据，不创建任何实体。
    /// 生成采用子种子结构（exploration-online-v1 契约 §3）：每个点位从
    /// SubSeed(masterSeed, kind, index) 派生独立 PRNG，点位之间不共享随机流。
    /// 同一 (masterSeed, 配置) 必然产出同一张图。
    /// </summary>
    public sealed class MapBuilder
    {
        private readonly DifficultyTier _difficulty;
        private readonly long _masterSeed;

        /// <param name="difficulty">本局难度档位。</param>
        /// <param name="masterSeed">本局主种子（int64 语义）；同一主种子必然生成同一张图。</param>
        public MapBuilder(DifficultyTier difficulty, long masterSeed)
        {
            _difficulty = difficulty;
            _masterSeed = masterSeed;
        }

        /// <summary>
        /// 从全局表指定的路径加载 MapDefinition，生成结果后卸载地图定义资产。
        /// </summary>
        public async UniTask<MapBuildResult> BuildAsync()
        {
            MapDefinition mapDefinition = await LoadMapDefinitionAsync();

            try
            {
                DifficultyConfig difficultyConfig = GameEntry.Luban.Get<DifficultyConfig>((int)_difficulty);
                return Build(mapDefinition, difficultyConfig,
                    GameEntry.Luban.GetTable<ResourcePointConfig>(),
                    GameEntry.Luban.GetTable<ItemConfig>(),
                    GameEntry.Luban.GetTable<EnemyPartyConfig>(), _masterSeed);
            }
            finally
            {
                GameEntry.Resource.UnloadAsset(mapDefinition);
            }
        }

        /// <summary>
        /// 完整生成：在地图定义上产出全部点位内容（物资清单、敌队伍、出生与撤离点）。
        /// 各 stage 的消费顺序见契约 §3；stage 之间不共享随机流。
        /// </summary>
        internal static MapBuildResult Build(MapDefinition mapDefinition, DifficultyConfig difficultyConfig,
            IReadOnlyList<ResourcePointConfig> resourceConfigs, IReadOnlyList<ItemConfig> itemConfigs,
            IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs, long masterSeed)
        {
            ResourcePointGenerator resourcePointGenerator = new ResourcePointGenerator(
                resourceConfigs, itemConfigs);
            EnemyPointGenerator enemyPointGenerator = new EnemyPointGenerator(enemyPartyConfigs);

            List<ResourcePointBuildData> resourcePoints = GenerateResourcePoints(
                mapDefinition.ResourcePoints, difficultyConfig.Tier, masterSeed, resourcePointGenerator);
            List<EnemyPointBuildData> enemyPoints = GenerateEnemyPoints(
                mapDefinition.EnemySpawnPoints, difficultyConfig.EnemyParties, masterSeed, enemyPointGenerator);
            Vector2 extractionPoint = ExtractionPointGenerator.PlacePoint(
                mapDefinition.ExtractionPoints, masterSeed);
            Vector2 playerSpawnPoint = PlayerSpawnPointGenerator.PlacePoint(
                mapDefinition.PlayerSpawnPoints, masterSeed);

            return new MapBuildResult(difficultyConfig.Tier, playerSpawnPoint, resourcePoints, enemyPoints,
                extractionPoint, mapDefinition.Rooms);
        }

        /// <summary>
        /// 从全局表指定的路径加载 MapDefinition，产出摆位预测结果后卸载地图定义资产。
        /// 联网流程的独立入口：只加载布局与配置，不做完整生成。
        /// </summary>
        public async UniTask<MapPlacement> BuildPlacementAsync()
        {
            MapDefinition mapDefinition = await LoadMapDefinitionAsync();

            try
            {
                DifficultyConfig difficultyConfig = GameEntry.Luban.Get<DifficultyConfig>((int)_difficulty);
                return BuildPlacement(mapDefinition, difficultyConfig, _masterSeed);
            }
            finally
            {
                GameEntry.Resource.UnloadAsset(mapDefinition);
            }
        }

        /// <summary>
        /// 摆位预测出口（契约 §4）：只取完整生成各 stage 的摆位子集——物资点存在性与位置、
        /// 敌点存在性与威胁等级、出生与撤离点位置。与 <see cref="Build"/> 共用同一套 stage 代码
        /// （同一条威胁选择与候选下标路径），因此同一 (masterSeed, 配置) 下逐位一致；
        /// 不产出物品清单与敌队伍。
        /// </summary>
        public static MapPlacement BuildPlacement(MapDefinition mapDefinition, DifficultyConfig difficultyConfig,
            long masterSeed)
        {
            List<ResourcePointPlacementData> resourcePoints =
                new List<ResourcePointPlacementData>(mapDefinition.ResourcePoints.Count);
            for (int i = 0; i < mapDefinition.ResourcePoints.Count; i++)
            {
                resourcePoints.Add(ResourcePointGenerator.PlacePoint(mapDefinition.ResourcePoints[i]));
            }

            List<EnemyPointPlacementData> enemyPoints =
                new List<EnemyPointPlacementData>(mapDefinition.EnemySpawnPoints.Count);
            for (int i = 0; i < mapDefinition.EnemySpawnPoints.Count; i++)
            {
                enemyPoints.Add(EnemyPointGenerator.PlacePoint(
                    mapDefinition.EnemySpawnPoints[i], i, masterSeed, difficultyConfig.EnemyParties));
            }

            Vector2 extractionPoint = ExtractionPointGenerator.PlacePoint(
                mapDefinition.ExtractionPoints, masterSeed);
            Vector2 playerSpawnPoint = PlayerSpawnPointGenerator.PlacePoint(
                mapDefinition.PlayerSpawnPoints, masterSeed);

            return new MapPlacement(difficultyConfig.Tier, playerSpawnPoint, extractionPoint,
                resourcePoints, enemyPoints, mapDefinition.Rooms);
        }

        private static List<ResourcePointBuildData> GenerateResourcePoints(
            IReadOnlyList<ResourcePointDefinition> definitions, DifficultyTier difficulty, long masterSeed,
            ResourcePointGenerator generator)
        {
            List<ResourcePointBuildData> results = new List<ResourcePointBuildData>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                results.Add(generator.BuildPoint(definitions[i], i, difficulty, masterSeed));
            }

            return results;
        }

        private static List<EnemyPointBuildData> GenerateEnemyPoints(
            IReadOnlyList<Vector2> definitions, IReadOnlyList<WeightedEnemyParty> threatWeights,
            long masterSeed, EnemyPointGenerator generator)
        {
            List<EnemyPointBuildData> results = new List<EnemyPointBuildData>();
            for (int i = 0; i < definitions.Count; i++)
            {
                EnemyPointBuildData enemyPoint = generator.BuildPoint(
                    definitions[i], i, masterSeed, threatWeights);
                if (enemyPoint != null)
                {
                    results.Add(enemyPoint);
                }
            }

            return results;
        }

        private static async UniTask<MapDefinition> LoadMapDefinitionAsync()
        {
            GlobalConfig global = GameEntry.Luban.Global.Data;
            string assetName = AssetUtility.GetGameMainAsset(global.MapDefinitionPath);
            return await GameEntry.Resource.LoadAssetAsync<MapDefinition>(
                assetName, Constant.AssetPriority.ConfigAsset);
        }
    }
}
