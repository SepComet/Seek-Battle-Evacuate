using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.CustomComponent;
using SepCore.Definition;
using SepCore.Utility;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 根据固定地图定义、难度和本局随机源生成完整地图数据，不创建任何实体。
    /// </summary>
    public sealed class MapBuilder
    {
        private readonly DifficultyTier _difficulty;
        private readonly IRoundRandomSource _random;

        public MapBuilder(DifficultyTier difficulty, IRoundRandomSource random)
        {
            _difficulty = difficulty;
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>
        /// 从全局表指定的路径加载 MapDefinition，生成结果后卸载地图定义资产。
        /// </summary>
        public async UniTask<MapBuildResult> BuildAsync()
        {
            GlobalConfig global = GameEntry.Luban.Global.Data;
            string assetName = AssetUtility.GetGameMainAsset(global.MapDefinitionPath);
            MapDefinition mapDefinition = await GameEntry.Resource.LoadAssetAsync<MapDefinition>(
                assetName, Constant.AssetPriority.ConfigAsset);

            try
            {
                DifficultyConfig difficultyConfig = GameEntry.Luban.Get<DifficultyConfig>((int)_difficulty);
                return Build(mapDefinition, difficultyConfig,
                    GameEntry.Luban.GetTable<ResourcePointConfig>(),
                    GameEntry.Luban.GetTable<ItemConfig>(),
                    GameEntry.Luban.GetTable<EnemyPartyConfig>(), _random);
            }
            finally
            {
                GameEntry.Resource.UnloadAsset(mapDefinition);
            }
        }

        internal static MapBuildResult Build(MapDefinition mapDefinition, DifficultyConfig difficultyConfig,
            IReadOnlyList<ResourcePointConfig> resourceConfigs, IReadOnlyList<ItemConfig> itemConfigs,
            IReadOnlyList<EnemyPartyConfig> enemyPartyConfigs, IRoundRandomSource random)
        {
            ResourcePointGenerator resourcePointGenerator = new ResourcePointGenerator(
                resourceConfigs, itemConfigs, random);
            EnemyPointGenerator enemyPointGenerator = new EnemyPointGenerator(enemyPartyConfigs, random);
            ExtractionPointGenerator extractionPointGenerator = new ExtractionPointGenerator(random);
            PlayerSpawnPointGenerator playerSpawnPointGenerator = new PlayerSpawnPointGenerator(random);

            // Random-consuming stages are append-only. New stages must be added after the existing stages.
            List<ResourcePointBuildData> resourcePoints = resourcePointGenerator.Generate(
                mapDefinition.ResourcePoints, difficultyConfig.Tier);
            List<EnemyPointBuildData> enemyPoints = enemyPointGenerator.Generate(
                mapDefinition.EnemySpawnPoints, difficultyConfig.EnemyParties);
            Vector2 extractionPoint = extractionPointGenerator.Generate(mapDefinition.ExtractionPoints);
            Vector2 playerSpawnPoint = playerSpawnPointGenerator.Generate(mapDefinition.PlayerSpawnPoints);

            return new MapBuildResult(difficultyConfig.Tier, playerSpawnPoint, resourcePoints, enemyPoints,
                extractionPoint);
        }
    }
}
