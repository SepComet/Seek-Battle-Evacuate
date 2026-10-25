using System;
using System.Collections.Generic;
using UnityEngine;

namespace SepCore.Exploration
{
    /// <summary>
    /// 蛇形跟随面包屑轨迹（纯逻辑，可独立测试）。
    /// 按采样距离记录领队走过的位置点，支持从轨迹起点按弧长回溯任意位置，供随从严格沿领队足迹移动。
    /// 轨迹只会增长与裁剪，不回滚；位置查询基于已记录的折线做线性插值。
    /// </summary>
    public class SnakeTrail
    {
        private readonly float _sampleDistance;
        private readonly List<Vector2> _points = new List<Vector2>();
        private readonly List<float> _arcFromStart = new List<float>();

        /// <summary>
        /// 轨迹头部（最新记录点）距轨迹起点的累计弧长。
        /// </summary>
        public float HeadArc { get; private set; }

        /// <summary>
        /// 当前轨迹记录点数量（含起点）。
        /// </summary>
        public int PointCount => _points.Count;

        /// <summary>
        /// 构造轨迹。
        /// </summary>
        /// <param name="sampleDistance">记录点之间的最小采样距离（米），必须大于 0。</param>
        public SnakeTrail(float sampleDistance)
        {
            if (sampleDistance <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleDistance));
            }

            _sampleDistance = sampleDistance;
        }

        /// <summary>
        /// 重置轨迹到指定起点。
        /// </summary>
        public void Reset(Vector2 startPosition)
        {
            _points.Clear();
            _arcFromStart.Clear();
            _points.Add(startPosition);
            _arcFromStart.Add(0f);
            HeadArc = 0f;
        }

        /// <summary>
        /// 追加轨迹点；距上一记录点不足采样距离时不记录。
        /// </summary>
        /// <returns>本次是否实际记录了新点。</returns>
        public bool Append(Vector2 position)
        {
            Vector2 lastPoint = _points[_points.Count - 1];
            float distance = Vector2.Distance(lastPoint, position);
            if (distance < _sampleDistance)
            {
                return false;
            }

            HeadArc += distance;
            _points.Add(position);
            _arcFromStart.Add(HeadArc);
            return true;
        }

        /// <summary>
        /// 获取从轨迹起点沿轨迹走过 arc 距离后的位置（折线线性插值，超出轨迹范围时钳制到两端）。
        /// </summary>
        public Vector2 GetPointFromStart(float arc)
        {
            if (arc <= _arcFromStart[0])
            {
                return _points[0];
            }

            if (arc >= HeadArc)
            {
                return _points[_points.Count - 1];
            }

            // 定位 arc 所在折线段：_arcFromStart[index - 1] < arc <= _arcFromStart[index]
            int index = 1;
            while (_arcFromStart[index] < arc)
            {
                index++;
            }

            float segmentStartArc = _arcFromStart[index - 1];
            float t = (arc - segmentStartArc) / (_arcFromStart[index] - segmentStartArc);
            return Vector2.Lerp(_points[index - 1], _points[index], t);
        }

        /// <summary>
        /// 裁剪距轨迹起点弧长超过 maxLength 的旧点，防止轨迹无限增长。
        /// 保留至少头尾两点。
        /// </summary>
        public void Trim(float maxLength)
        {
            while (_points.Count > 1 && HeadArc - _arcFromStart[0] > maxLength)
            {
                _points.RemoveAt(0);
                _arcFromStart.RemoveAt(0);
            }
        }
    }
}
