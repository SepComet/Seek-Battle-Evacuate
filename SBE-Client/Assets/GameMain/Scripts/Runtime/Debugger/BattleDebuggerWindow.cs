using GameFramework.Debugger;
using SepCore.Battle;
using SepCore.CustomComponent;
using SepCore.Definition;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Debugger
{
    /// <summary>
    /// 战斗壳层调试入口（仅供 Editor/Development Build，正式构建不注册）。
    /// 模拟探索层：缺少单局状态时用配表角色与固定种子初始化临时状态，
    /// 然后通过 TurnBattleComponent 以调试遭遇开始/结束战斗，不依赖尚未实现的地图敌人。
    /// 支持 1v1 / 2v2 / 4v4 与先制开关。
    /// </summary>
    public class BattleDebuggerWindow : IDebuggerWindow
    {
        private const int DebugRunSeed = 240829;
        private const int DebugEncounterId = 1;

        private int _playerCount = 1;
        private int _enemyPartyConfigId = 4001;
        private bool _preemptive;
        private int _builtPlayerCount = -1;

        public void Initialize(params object[] args)
        {
        }

        public void Shutdown()
        {
        }

        public void OnEnter()
        {
        }

        public void OnLeave()
        {
        }

        public void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
        }

        public void OnDraw()
        {
            GUILayout.Label("<b>Battle Shell</b>");

            GUILayout.BeginVertical("box");
            {
                GUILayout.Label("队伍规模（玩家数/敌人队伍）：");
                GUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("1v1", GUILayout.Height(26)))
                    {
                        _playerCount = 1;
                        _enemyPartyConfigId = 4001;
                    }

                    if (GUILayout.Button("2v2", GUILayout.Height(26)))
                    {
                        _playerCount = 2;
                        _enemyPartyConfigId = 4002;
                    }

                    if (GUILayout.Button("4v4", GUILayout.Height(26)))
                    {
                        _playerCount = 4;
                        _enemyPartyConfigId = 4005;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("当前选择：" + _playerCount + " 玩家 / 敌人队伍 " + _enemyPartyConfigId);

                _preemptive = GUILayout.Toggle(_preemptive, "先制（第一轮玩家全先行）");

                GUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button("Open", GUILayout.Height(30)))
                    {
                        OpenShell();
                    }

                    if (GUILayout.Button("Close", GUILayout.Height(30)))
                    {
                        GameEntry.TurnBattle.CloseBattle();
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            DrawRunState();
        }

        private static void DrawRunState()
        {
            GUILayout.Label("<b>Run State</b>");
            GUILayout.BeginVertical("box");
            {
                TurnBattleComponent runBattle = GameEntry.TurnBattle;
                if (runBattle == null)
                {
                    GUILayout.Label("TurnBattleComponent missing.");
                    return;
                }

                GUILayout.Label("RunElapsedMs: " + runBattle.RunElapsedMs.ToString());
                GUILayout.Label("IsTimerPaused: " + runBattle.IsTimerPaused);
                GUILayout.Label("IsExplorationPaused: " + runBattle.IsExplorationPaused);
                GUILayout.Label("IsBattleActive: " + runBattle.IsBattleActive);
            }
            GUILayout.EndVertical();
        }

        private void OpenShell()
        {
            if (GameEntry.Random.Random == null)
            {
                GameEntry.Random.BeginRun(DebugRunSeed);
            }

            // 规模变化时重建玩家列表（重置为满状态）；同规模重开保留上次战斗回写的 HP/MP
            if (_builtPlayerCount != _playerCount)
            {
                GameEntry.TurnBattle.ReplacePlayers(BuildDebugPlayers(_playerCount));
                _builtPlayerCount = _playerCount;
            }

            BattleEncounter encounter = new BattleEncounter(DebugEncounterId, _enemyPartyConfigId, _preemptive);
            bool started = GameEntry.TurnBattle.TryStartBattle(encounter, null);
            if (!started)
            {
                Log.Warning("Battle debug shell can not start, maybe a battle is already active.");
            }
        }

        private static List<PlayerUnitState> BuildDebugPlayers(int count)
        {
            List<PlayerUnitState> players = new List<PlayerUnitState>();
            GlobalConfig global = GameEntry.Luban.Global != null ? GameEntry.Luban.Global.Data : null;
            if (global == null)
            {
                Log.Warning("Can not build debug players without global config.");
                return players;
            }

            if (global.NewGameCharacterIds == null || global.NewGameCharacterIds.Count == 0)
            {
                Log.Warning("Can not build debug players without new game character ids.");
                return players;
            }

            int order = 1;
            foreach (int characterId in global.NewGameCharacterIds)
            {
                if (order > count)
                {
                    break;
                }

                CharacterConfig config = GameEntry.Luban.Get<CharacterConfig>(characterId);
                if (config == null)
                {
                    Log.Warning("Debug character '{0}' missing in config, skipped.", characterId.ToString());
                    continue;
                }

                players.Add(new PlayerUnitState
                {
                    CharacterId = characterId,
                    PartyOrder = order++,
                    CurrentHp = config.MaxHp,
                    CurrentMp = config.MaxMp,
                    MaxHp = config.MaxHp,
                    MaxMp = config.MaxMp,
                    Atk = config.Atk,
                    Mat = config.Mat,
                    Speed = config.Speed,
                    AttackActionId = config.AttackActionId,
                    SkillActionId = config.SkillActionId,
                });
            }

            return players;
        }
    }
}