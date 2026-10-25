using System;
using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 世界空间飘字实体数据。
    /// 承载飘字坐标、文本内容、字体颜色、持续时间与漂浮高度。
    /// </summary>
    [Serializable]
    public sealed class WorldFloatTextData : EntityDataBase
    {
        [SerializeField] private string _text;
        [SerializeField] private Color _color;
        [SerializeField] private float _duration;
        [SerializeField] private float _floatDistance;

        public WorldFloatTextData(
            int entityId,
            string assetName,
            Vector3 position,
            string text,
            Color color,
            float duration = 0.65f,
            float floatDistance = 0.8f,
            Quaternion? rotation = null) : base(assetName, entityId)
        {
            Position = position;
            Rotation = rotation ?? Quaternion.identity;
            _text = text;
            _color = color;
            _duration = duration;
            _floatDistance = floatDistance;
        }

        /// <summary>
        /// 飘字显示文本。
        /// </summary>
        public string Text => _text;

        /// <summary>
        /// 飘字颜色（跟随稀有度）。
        /// </summary>
        public Color Color => _color;

        /// <summary>
        /// 漂浮淡出持续时长（秒）。
        /// </summary>
        public float Duration => _duration;

        /// <summary>
        /// 向上漂浮距离。
        /// </summary>
        public float FloatDistance => _floatDistance;
    }
}
