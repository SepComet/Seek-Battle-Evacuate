using System.Collections.Generic;
using UnityEngine;

namespace SepCore.Battle
{
    /// <summary>
    /// 战斗入场动画参数，封装从大地图世界向战斗 UI 投影的屏幕坐标。
    /// </summary>
    public sealed class BattleEnterAnimationParams
    {
        /// <summary>
        /// 触发战斗的地图敌人队伍屏幕像素坐标。
        /// </summary>
        public Vector3 EnemyScreenPosition { get; set; }

        /// <summary>
        /// 各玩家角色（领队及随从）在屏幕上的像素坐标列表。
        /// </summary>
        public List<Vector3> PlayerScreenPositions { get; } = new List<Vector3>();
    }
}
