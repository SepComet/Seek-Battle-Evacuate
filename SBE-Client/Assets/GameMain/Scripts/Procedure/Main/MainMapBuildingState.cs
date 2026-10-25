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
using SepCore.Net;
using SepCore.Utility;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Procedure
{
    /// <summary>
    /// 主流程状态：地图构建。
    /// 负责启动单局探索计时、初始化出战角色状态、加载地图定义、生成/预测物资点与敌人分布、实例化场景实体，并准备单局探索环境。
    /// 联网在线时先 start_run 取服务器分配的种子与显式出生/撤离点，敌人与物资点摆位走本地预测；
    /// 离线（场景调试）时用本地时间戳种子跑完整本地生成。开局失败不进入半成品局：提示后回大厅。
    /// </summary>
    public sealed class MainMapBuildingState : FsmState<ProcedureMain>
    {
        private CancellationTokenSource _cts;

        protected override void OnEnter(IFsm<ProcedureMain> fsm)
        {
            base.OnEnter(fsm);
            Log.Info("[ProcedureMain] Entering MainMapBuildingState...");

            NetworkNotice.ResetThrottle();
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

                // 2. 获取单局难度
                DifficultyTier difficulty = DifficultyTier.Tier1;
                if (GameEntry.Save.Data?.loadout != null)
                {
                    difficulty = GameEntry.Save.Data.loadout.difficultyId;
                }
                fsm.Owner.Difficulty = difficulty;

                // 3. 取本局主种子（联网取服务器种子，离线取本地时间戳种子）
                RunSeedSource seedSource = await ResolveRunSeedAsync(difficulty, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (seedSource == null)
                {
                    // 已在 ResolveRunSeedAsync 内记录原因并提示；不进入半成品局。
                    AbortToMenu(fsm);
                    return;
                }

                // 4. 初始化单局数据层（共享背包、保险箱、出战队伍状态），种子与内容生成同一来源
                if (GameEntry.Round != null)
                {
                    GameEntry.Round.BeginRun(difficulty, seedSource.MasterSeed);
                }

                // 5. 构建并装配本局出战角色状态（优先使用 RunSession 导出）
                List<PlayerUnitState> partyPlayers = GameEntry.Round?.Session != null
                    ? GameEntry.Round.Session.ToPlayerUnitStates()
                    : PlayerPartyBuilder.Build(GameEntry.Save.Data, GameEntry.Luban.Tables);
                GameEntry.TurnBattle.ReplacePlayers(partyPlayers);
                Log.Info("[ProcedureMain] Initialized {0} party players for exploration.", partyPlayers.Count);

                // 6. 确保实体组存在
                EnsureEntityGroup("Map");
                EnsureEntityGroup("ResourcePoint");
                EnsureEntityGroup("Enemy");
                EnsureEntityGroup("Player");
                EnsureEntityGroup("Camera");
                EnsureEntityGroup("Item");

                // 7. 生成/预测地图内容（联网只取摆位，离线走完整生成）
                MapSpawnPlan plan = await BuildSpawnPlanAsync(difficulty, seedSource, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (plan == null)
                {
                    // 生成/预测失败（配置缺失等已在内部报错）：不生成半成品实体，回大厅。
                    AbortToMenu(fsm);
                    return;
                }

                fsm.Owner.ExtractionPoint = TileToWorldPosition2D(plan.ExtractionTile);
                fsm.Owner.PlayerSpawnPoint = TileToWorldPosition2D(plan.PlayerSpawnTile);

                // 预先构建所有房间与走廊的迷雾淡出遮罩（避免运行时进入房间临时动态创建）；雾纯演出，不等网络
                FogTilemapController fogController = FogTilemapController.Instance ?? UnityEngine.Object.FindObjectOfType<FogTilemapController>();
                if (fogController != null)
                {
                    fogController.InitializeRoomOverlays(plan.Rooms);
                }
                else
                {
                    Log.Warning("[ProcedureMain] FogTilemapController is not found in the scene; fog fade overlay will not be initialized.");
                }

                GlobalConfig global = GameEntry.Luban.Global?.Data;
                if (global == null)
                {
                    Log.Error("[ProcedureMain] GlobalConfig is missing from Luban tables.");
                    return;
                }

                // 8. 实例化撤离点实体（预制体资产名取自 GlobalConfig.EvacuatePointEntity）
                string evacAssetName = global.EvacuatePointEntity;
                if (string.IsNullOrEmpty(evacAssetName))
                {
                    Log.Error("[ProcedureMain] EvacuatePointEntity is not configured in GlobalConfig.");
                }
                else
                {
                    int evacEntityId = GameEntry.Entity.SerialId();
                    Vector3 evacPos = TileToWorldPosition(plan.ExtractionTile);
                    EvacuatePointData evacData = new EvacuatePointData(evacEntityId, evacAssetName, evacPos);
                    GameEntry.Entity.ShowEntity<EvacuatePointLogic>(evacData, "Map", Constant.AssetPriority.SceneAsset);
                }

                // 9. 实例化资源点实体（预制体资产名取自 ResourcePointConfig.Prefab）
                foreach (ResourcePointSpawnSpec resPoint in plan.ResourcePoints)
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

                    // 服务器幂等：uid 已有活跃 run 时 start_run 无论 resume 真假都返回该 run 现状（契约 §7）。
                    // 落入已开箱的点必须装配成"已开完"（清单就绪、掉落计数填满），否则箱子会被重新拾取。
                    ResourcePointData resData;
                    if (seedSource.Online &&
                        ResourcePointRecovery.TryGetLootedItems(seedSource.StartRun.Looted, resPoint.PointId,
                            out List<int> lootedItems))
                    {
                        resData = ResourcePointData.CreatePendingContents(resEntityId, assetName, resPos,
                            resPoint.ResourcePointId, resPoint.PointId);
                        resData.ApplyItems(lootedItems);
                        resData.DroppedItemCount = resData.ItemCount;
                    }
                    // 联网流程开局无清单：内容待 loot 响应灌入；离线路径清单随方案带上。
                    else if (resPoint.ContentsKnown)
                    {
                        resData = new ResourcePointData(resEntityId, assetName, resPos, resPoint.ResourcePointId,
                            resPoint.ItemIds, pointId: resPoint.PointId);
                    }
                    else
                    {
                        resData = ResourcePointData.CreatePendingContents(resEntityId, assetName, resPos,
                            resPoint.ResourcePointId, resPoint.PointId);
                    }

                    GameEntry.Entity.ShowEntity<ResourcePointLogic>(resData, "ResourcePoint", Constant.AssetPriority.SceneAsset);
                }

                // 10. 实例化敌人队伍实体（预制体资产名根据威胁等级取自 GlobalConfig）
                foreach (EnemySpawnSpec enemyPoint in plan.EnemyPoints)
                {
                    string assetName = GetEnemyPartyAssetName(global, enemyPoint.ThreatLevel);
                    if (string.IsNullOrEmpty(assetName))
                    {
                        Log.Error("[ProcedureMain] Enemy party entity asset name is missing in GlobalConfig for threat level '{0}'.",
                            enemyPoint.ThreatLevel);
                        continue;
                    }

                    int enemyEntityId = GameEntry.Entity.SerialId();
                    Vector3 enemyPos = TileToWorldPosition(enemyPoint.Position);

                    int roomIndex = -1;
                    Vector2 roomMin = Vector2.zero;
                    Vector2 roomMax = Vector2.zero;
                    if (plan.Rooms != null)
                    {
                        for (int r = 0; r < plan.Rooms.Count; r++)
                        {
                            if (plan.Rooms[r].Contains(enemyPos))
                            {
                                roomIndex = r;
                                roomMin = plan.Rooms[r].Min;
                                roomMax = plan.Rooms[r].Max;
                                break;
                            }
                        }
                    }

                    // 联网流程的队伍构成等 encounter 响应下发，EnemyPartyId 传 0 只作占位。
                    EnemyPartyData enemyData = new EnemyPartyData(
                        enemyEntityId, assetName, enemyPos, enemyPoint.EnemyPartyId, enemyPoint.ThreatLevel,
                        roomIndex, roomMin, roomMax, pointId: enemyPoint.PointId);
                    GameEntry.Entity.ShowEntity<EnemyPartyLogic>(enemyData, "Enemy", Constant.AssetPriority.EnemyAsset);
                }

                // 11. 生成玩家角色实体（出战第 1 位为领队，其余为蛇形跟随者）并绑定编队
                PlayerCharacterLogic leader = await SpawnPlayerPartyAsync(partyPlayers, global, plan, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // 12. 生成主摄像机实体并设置跟随领队目标
                if (leader != null)
                {
                    fsm.Owner.MainCamera = await SpawnMainCameraAsync(leader, cancellationToken);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }

                Log.Info("[ProcedureMain] Map build completed (online: {0}). Resource points: {1}, Enemies: {2}. Switching to ExplorationBattleState.",
                    seedSource.Online, plan.ResourcePoints.Count, plan.EnemyPoints.Count);

                ChangeState<MainExplorationBattleState>(fsm);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // 构建中途异常（配置缺失、资源加载失败等）会留下半成品场景，
                // 直接回大厅而不是停在这个状态里无从操作。
                Log.Error("[ProcedureMain] Failed to build map with error: {0}", ex);
                AbortToMenu(fsm);
            }
        }

        /// <summary>
        /// 本局种子来源：在线时来自 start_run（服务器分配），离线时为本地时间戳。
        /// <see cref="MasterSeed"/> 同时用于内容生成与战斗随机源，保证结算上报的 seed 与生成种子同值。
        /// </summary>
        private sealed class RunSeedSource
        {
            public bool Online;
            public long MasterSeed;
            public StartRunResult StartRun;
        }

        /// <summary>
        /// 决定本局主种子：在线走 start_run（服务器是唯一权威，超时/失败即中止进局）；
        /// 离线（场景调试）用本地时间戳种子，只跑 C# 生成器。
        /// </summary>
        private async UniTask<RunSeedSource> ResolveRunSeedAsync(DifficultyTier difficulty,
            CancellationToken cancellationToken)
        {
            bool online = GameEntry.Net != null && GameEntry.Net.OnlineReady;
            if (online)
            {
                StartRunResult result = await GameEntry.Net.Run.StartRunAsync(difficulty, resume: false);
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                if (!result.Success)
                {
                    Log.Error("[ProcedureMain] start_run failed ({0}/{1}); aborting run.", result.Outcome,
                        result.ErrCode ?? result.ErrMsg ?? "no detail");
                    NetworkNotice.ShowInteractionRejected();
                    return null;
                }

                // 服务器种子同时用于内容生成与战斗随机源（契约 §5）。
                GameEntry.Random.BeginRun((int)result.Seed);
                Log.Info("[ProcedureMain] start_run ok. RunId: {0}, Seed: {1}, Consumed: {2}, Looted: {3}.",
                    result.RunId, result.Seed, result.Consumed.Count, result.Looted.Count);

                return new RunSeedSource
                {
                    Online = true,
                    MasterSeed = result.Seed,
                    StartRun = result,
                };
            }

            // 场景调试路径：未经 start_run 直接进图，用本地时间戳种子跑完整本地生成。
            int fallbackSeed = (int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF);
            Log.Warning("Offline (scene debug) path: using local seed '{0}' for full local generation.", fallbackSeed);
            GameEntry.Random.BeginRun(fallbackSeed);

            return new RunSeedSource
            {
                Online = false,
                MasterSeed = fallbackSeed,
                StartRun = null,
            };
        }

        /// <summary>
        /// 产出待实例化的地图实体方案：在线只用预测摆位 + 服务器显式出生/撤离点（物资清单与队伍留空），
        /// 离线跑完整本地生成。
        /// </summary>
        private async UniTask<MapSpawnPlan> BuildSpawnPlanAsync(DifficultyTier difficulty, RunSeedSource seedSource,
            CancellationToken cancellationToken)
        {
            MapBuilder builder = new MapBuilder(difficulty, seedSource.MasterSeed);

            if (!seedSource.Online)
            {
                MapBuildResult buildResult = await builder.BuildAsync();
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                return MapSpawnPlan.FromBuildResult(buildResult);
            }

            MapPlacement placement = await builder.BuildPlacementAsync();
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            // 出生/撤离点用服务器显式值（瓦片坐标），不参与预测（契约 §9）。
            Vector2 spawnTile = new Vector2(seedSource.StartRun.SpawnPoint.x, seedSource.StartRun.SpawnPoint.y);
            Vector2 extractionTile = new Vector2(seedSource.StartRun.ExtractionPoint.x,
                seedSource.StartRun.ExtractionPoint.y);
            // 敌队伍用同威胁等级的本地占位配置：实体需要它选 prefab、取警惕参数与掉落档位，
            // 真正参战队伍等 encounter 响应下发。
            return MapSpawnPlan.FromPlacement(placement, spawnTile, extractionTile,
                GameEntry.Luban.GetTable<EnemyPartyConfig>());
        }

        /// <summary>
        /// 开局失败（start_run 超时/拒绝、服务器应答缺损）时中止进局：清理已启动的单局状态并回大厅，
        /// 不留下半成品局（不生成任何实体、不进入探索流程）。
        /// </summary>
        private static void AbortToMenu(IFsm<ProcedureMain> fsm)
        {
            Log.Error("[ProcedureMain] Aborting run before any entity was spawned; returning to menu.");

            if (GameEntry.Round != null && GameEntry.Round.HasActiveRun)
            {
                GameEntry.Round.EndRun();
            }

            GameEntry.TurnBattle.EndRun();
            GameEntry.Random.EndRun();

            if (fsm.Owner.AutoReturnToMenu)
            {
                fsm.Owner.ReturnToMenu();
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
            MapSpawnPlan plan,
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

            Vector3 spawnPos = TileToWorldPosition(plan.PlayerSpawnTile);

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
            partyLogics[0].InitializeRoomTracker(plan.Rooms);
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
