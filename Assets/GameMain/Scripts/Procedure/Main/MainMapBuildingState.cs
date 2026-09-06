using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Fsm;
using SepCore.AsyncTask;
using SepCore.Battle;
using SepCore.Definition;
using SepCore.Entity;
using SepCore.Exploration;
using SepCore.Utility;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主流程状态：地图构建。
    /// 负责启动单局探索计时、初始化出战角色状态、加载地图定义、确定性生成物资点/敌人分布、实例化场景实体，并准备单局探索环境。
    /// </summary>
    public sealed class MainMapBuildingState : FsmState<ProcedureMain>
    {
        private CancellationTokenSource _cts;

        protected override void OnEnter(IFsm<ProcedureMain> fsm)
        {
            base.OnEnter(fsm);
            Log.Info("[ProcedureMain] Entering MainMapBuildingState...");

            _cts = new CancellationTokenSource();
            BuildMapAsync(fsm, _cts.Token).Forget();
        }

        protected override void OnLeave(IFsm<ProcedureMain> fsm, bool isShutdown)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            base.OnLeave(fsm, isShutdown);
        }

        private async UniTaskVoid BuildMapAsync(IFsm<ProcedureMain> fsm, CancellationToken cancellationToken)
        {
            try
            {
                // 1. 开始单局探索计时与状态管理（重置单局探索时间）
                GameEntry.TurnBattle.BeginRun();

                // 2. 获取单局难度与随机源
                DifficultyTier difficulty = DifficultyTier.Tier1;
                if (GameEntry.Save.Data?.loadout != null)
                {
                    difficulty = GameEntry.Save.Data.loadout.difficultyId;
                }
                fsm.Owner.Difficulty = difficulty;

                if (GameEntry.Random.Random == null)
                {
                    // 若未经过大厅战备输入种子直接进入场景调试，使用当前时间戳兜底初始化本局随机源
                    int fallbackSeed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
                    Log.Warning("RandomComponent is not initialized. Using fallback seed '{0}'.", fallbackSeed);
                    GameEntry.Random.BeginRun(fallbackSeed);
                }

                // 3. 初始化单局数据层（共享背包、保险箱、出战队伍状态）
                if (GameEntry.Round != null)
                {
                    GameEntry.Round.BeginRun(difficulty, GameEntry.Random.Seed);
                }

                // 4. 构建并装配本局出战角色状态（优先使用 RunSession 导出）
                List<PlayerUnitState> partyPlayers = GameEntry.Round?.Session != null
                    ? GameEntry.Round.Session.ToPlayerUnitStates()
                    : PlayerPartyBuilder.Build(GameEntry.Save.Data, GameEntry.Luban.Tables);
                GameEntry.TurnBattle.ReplacePlayers(partyPlayers);
                Log.Info("[ProcedureMain] Initialized {0} party players for exploration.", partyPlayers.Count);

                // 4. 确保实体组存在
                EnsureEntityGroup("Map");
                EnsureEntityGroup("ResourcePoint");
                EnsureEntityGroup("Enemy");
                EnsureEntityGroup("Player");
                EnsureEntityGroup("Camera");

                // 5. 执行地图构建
                MapBuilder builder = new MapBuilder(difficulty, GameEntry.Random.Random);
                MapBuildResult buildResult = await builder.BuildAsync();

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                fsm.Owner.BuildResult = buildResult;
                fsm.Owner.ExtractionPoint = TileToWorldPosition2D(buildResult.ExtractionPoint);
                fsm.Owner.PlayerSpawnPoint = TileToWorldPosition2D(buildResult.PlayerSpawnPoint);

                GlobalConfig global = GameEntry.Luban.Global?.Data;
                if (global == null)
                {
                    Log.Error("[ProcedureMain] GlobalConfig is missing from Luban tables.");
                    return;
                }

                // 6. 实例化撤离点实体（预制体资产名取自 GlobalConfig.EvacuatePointEntity）
                string evacAssetName = global.EvacuatePointEntity;
                if (string.IsNullOrEmpty(evacAssetName))
                {
                    Log.Error("[ProcedureMain] EvacuatePointEntity is not configured in GlobalConfig.");
                }
                else
                {
                    int evacEntityId = GameEntry.Entity.SerialId();
                    Vector3 evacPos = TileToWorldPosition(buildResult.ExtractionPoint);
                    EvacuatePointData evacData = new EvacuatePointData(evacEntityId, evacAssetName, evacPos);
                    GameEntry.Entity.ShowEntity<EvacuatePointLogic>(evacData, "Map", Constant.AssetPriority.SceneAsset);
                }

                // 7. 实例化资源点实体（预制体资产名取自 ResourcePointConfig.Prefab）
                foreach (ResourcePointBuildData resPoint in buildResult.ResourcePoints)
                {
                    ResourcePointConfig resConfig = GameEntry.Luban.Get<ResourcePointConfig>(resPoint.ResourcePointId);
                    if (resConfig == null || string.IsNullOrEmpty(resConfig.Prefab))
                    {
                        Log.Error("[ProcedureMain] ResourcePointConfig '{0}' or its Prefab is missing.", resPoint.ResourcePointId);
                        continue;
                    }

                    int resEntityId = GameEntry.Entity.SerialId();
                    string assetName = resConfig.Prefab;
                    Vector3 resPos = TileToWorldPosition(resPoint.Position);
                    ResourcePointData resData = new ResourcePointData(
                        resEntityId, assetName, resPos, resPoint.ResourcePointId, resPoint.ItemIds);
                    GameEntry.Entity.ShowEntity<ResourcePointLogic>(resData, "ResourcePoint", Constant.AssetPriority.SceneAsset);
                }

                // 8. 实例化敌人队伍实体（预制体资产名根据威胁等级取自 GlobalConfig）
                foreach (EnemyPointBuildData enemyPoint in buildResult.EnemyPoints)
                {
                    if (enemyPoint.ThreatLevel == EnemyPartyThreatLevel.None)
                    {
                        continue;
                    }

                    string assetName = GetEnemyPartyAssetName(global, enemyPoint.ThreatLevel);
                    if (string.IsNullOrEmpty(assetName))
                    {
                        Log.Error("[ProcedureMain] Enemy party entity asset name is missing in GlobalConfig for threat level '{0}'.",
                            enemyPoint.ThreatLevel);
                        continue;
                    }

                    int enemyEntityId = GameEntry.Entity.SerialId();
                    Vector3 enemyPos = TileToWorldPosition(enemyPoint.Position);
                    EnemyPartyData enemyData = new EnemyPartyData(
                        enemyEntityId, assetName, enemyPos, enemyPoint.EnemyPartyId, enemyPoint.ThreatLevel);
                    GameEntry.Entity.ShowEntity<EnemyPartyLogic>(enemyData, "Enemy", Constant.AssetPriority.EnemyAsset);
                }

                // 9. 生成玩家角色实体（出战第 1 位为领队，其余为蛇形跟随者）并绑定编队
                PlayerCharacterLogic leader = await SpawnPlayerPartyAsync(partyPlayers, global, buildResult, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // 10. 生成主摄像机实体并设置跟随领队目标
                if (leader != null)
                {
                    fsm.Owner.MainCamera = await SpawnMainCameraAsync(leader, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }

                Log.Info("[ProcedureMain] Map build completed. Resource points: {0}, Enemies: {1}. Switching to ExplorationBattleState.",
                    buildResult.ResourcePoints.Count, buildResult.EnemyPoints.Count);

                ChangeState<MainExplorationBattleState>(fsm);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Error("[ProcedureMain] Failed to build map with error: {0}", ex);
            }
        }

        /// <summary>
        /// 根据威胁等级从全局配置中获取对应的敌人队伍实体资产名称。
        /// </summary>
        public static string GetEnemyPartyAssetName(GlobalConfig global, EnemyPartyThreatLevel threatLevel)
        {
            if (global == null)
            {
                return null;
            }

            switch (threatLevel)
            {
                case EnemyPartyThreatLevel.Low:
                    return global.LowThreatEnemyPartyEntity;
                case EnemyPartyThreatLevel.Middle:
                    return global.MiddleThreatEnemyPartyEntity;
                case EnemyPartyThreatLevel.High:
                    return global.HighThreatEnemyPartyEntity;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 生成玩家角色实体并绑定蛇形编队。
        /// 出战第 1 位为领队（挂载移动控制器接入全局输入），其余为随从（由领队的编队控制器沿轨迹驱动）。
        /// 全部角色实体生成完成后才绑定编队，保证领队拿得到随从引用。
        /// </summary>
        private async UniTask<PlayerCharacterLogic> SpawnPlayerPartyAsync(
            List<PlayerUnitState> partyPlayers,
            GlobalConfig global,
            MapBuildResult buildResult,
            CancellationToken cancellationToken)
        {
            if (partyPlayers == null || partyPlayers.Count == 0)
            {
                Log.Error("[ProcedureMain] Player party is empty, can not spawn player characters.");
                return null;
            }

            string leaderAssetName = global.CharacterLeaderEntity;
            string retinueAssetName = global.CharacterRetinueEntity;
            if (string.IsNullOrEmpty(leaderAssetName) || string.IsNullOrEmpty(retinueAssetName))
            {
                Log.Error("[ProcedureMain] CharacterLeaderEntity/CharacterRetinueEntity is not configured in GlobalConfig.");
                return null;
            }

            string leaderAssetPath = ResolveEntityAssetPath(leaderAssetName, global.CharacterLeaderEntity_Ref);
            string retinueAssetPath = ResolveEntityAssetPath(retinueAssetName, global.CharacterRetinueEntity_Ref);
            if (leaderAssetPath == null || retinueAssetPath == null)
            {
                return null;
            }

            Vector3 spawnPos = TileToWorldPosition(buildResult.PlayerSpawnPoint);

            // 领队实体（出战第 1 位），其后为随从实体（出战第 2 位起，序号跟随战备顺序）
            List<UniTask<PlayerCharacterLogic>> spawnTasks = new List<UniTask<PlayerCharacterLogic>>(partyPlayers.Count);
            for (int i = 0; i < partyPlayers.Count; i++)
            {
                string assetPath = i == 0 ? leaderAssetPath : retinueAssetPath;
                string assetName = i == 0 ? leaderAssetName : retinueAssetName;
                int entityId = GameEntry.Entity.SerialId();
                PlayerCharacterData characterData = new PlayerCharacterData(
                    entityId, assetName, spawnPos, partyPlayers[i].CharacterId, i + 1);
                spawnTasks.Add(GameEntry.Entity.ShowEntityAsync<PlayerCharacterLogic>(
                    entityId, assetPath, "Player", characterData));
            }

            // WhenAll 结果按传入顺序排列，第 1 个为领队
            PlayerCharacterLogic[] partyLogics = await UniTask.WhenAll(spawnTasks);
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            PlayerCharacterLogic[] retinueLogics = new PlayerCharacterLogic[partyLogics.Length - 1];
            for (int i = 0; i < retinueLogics.Length; i++)
            {
                retinueLogics[i] = partyLogics[i + 1];
            }

            partyLogics[0].BindParty(retinueLogics);
            Log.Info("[ProcedureMain] Spawned {0} player characters at spawn point '{1}'.",
                partyPlayers.Count, spawnPos);
            return partyLogics[0];
        }

        /// <summary>
        /// 生成主摄像机实体并直接绑定跟随领队角色。
        /// 初始位置对齐领队坐标（Z 轴设为正交相机的 -10f），避免开局视角拉伸或漂移。
        /// </summary>
        private async UniTask<MainCameraLogic> SpawnMainCameraAsync(
            PlayerCharacterLogic leader,
            CancellationToken cancellationToken)
        {
            if (leader == null)
            {
                Log.Error("[ProcedureMain] Leader character is null, can not spawn main camera.");
                return null;
            }

            const string cameraAssetName = "MainCamera";
            EntityConfig cameraConfig = GameEntry.Luban.Get<EntityConfig>(cameraAssetName);
            if (cameraConfig == null || string.IsNullOrEmpty(cameraConfig.PrefabPath))
            {
                Log.Error("[ProcedureMain] Main camera entity asset '{0}' is missing or has no prefab path in EntityConfig.",
                    cameraAssetName);
                return null;
            }

            string assetPath = AssetUtility.GetEntityAsset(cameraConfig.PrefabPath);
            Vector3 leaderPos = leader.transform.position;
            Vector3 initialPos = new Vector3(leaderPos.x, leaderPos.y, -10f);

            int entityId = GameEntry.Entity.SerialId();
            MainCameraData cameraData = new MainCameraData(entityId, cameraAssetName, initialPos);

            MainCameraLogic cameraLogic = await GameEntry.Entity.ShowEntityAsync<MainCameraLogic>(
                entityId, assetPath, "Camera", cameraData);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (cameraLogic == null)
            {
                Log.Error("[ProcedureMain] Failed to show main camera entity.");
                return null;
            }

            cameraLogic.SetFollowTarget(leader.transform, immediate: true);
            Log.Info("[ProcedureMain] Main camera entity spawned and following leader at '{0}'.", initialPos);
            return cameraLogic;
        }

        /// <summary>
        /// 将实体资产名解析为完整资源路径。
        /// ShowEntityAsync 不经过 EntityExtension 的配表解析，必须传入可加载的完整路径。
        /// </summary>
        private static string ResolveEntityAssetPath(string assetName, EntityConfig entityConfigRef)
        {
            if (entityConfigRef == null || string.IsNullOrEmpty(entityConfigRef.PrefabPath))
            {
                Log.Error("[ProcedureMain] Entity asset '{0}' is missing or has no prefab path in EntityConfig.", assetName);
                return null;
            }

            return AssetUtility.GetEntityAsset(entityConfigRef.PrefabPath);
        }

        private static void EnsureEntityGroup(string groupName)
        {
            if (!GameEntry.Entity.HasEntityGroup(groupName))
            {
                GameEntry.Entity.AddEntityGroup(groupName, 60f, 32, 60f, 0);
            }
        }

        /// <summary>
        /// Tile 格子中心偏移（Tile 坐标原点在左下角，加 0.5 偏移对齐到网格中心）。
        /// </summary>
        private const float TileCenterOffset = 0.5f;

        private static Vector3 TileToWorldPosition(Vector2 tilePos)
        {
            return new Vector3(tilePos.x + TileCenterOffset, tilePos.y + TileCenterOffset, 0f);
        }

        private static Vector2 TileToWorldPosition2D(Vector2 tilePos)
        {
            return new Vector2(tilePos.x + TileCenterOffset, tilePos.y + TileCenterOffset);
        }
    }
}
