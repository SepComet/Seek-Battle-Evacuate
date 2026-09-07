using TMPro;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 世界空间飘字实体逻辑。
    /// 对应预制体 WorldFloatText.prefab，负责在场景世界空间中显示飘字动画（微弹放大、平滑上浮、后半程淡出并自动回池）。
    /// </summary>
    public sealed class WorldFloatTextLogic : EntityBase
    {
        private WorldFloatTextData _data = null;
        private TextMeshPro _textMesh = null;
        private MeshRenderer _meshRenderer = null;
        private float _elapsed = 0f;
        private Vector3 _startPosition = Vector3.zero;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _textMesh = GetComponent<TextMeshPro>();
            _meshRenderer = GetComponent<MeshRenderer>();
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _data = userData as WorldFloatTextData;
            if (_data == null)
            {
                Log.Error("WorldFloatTextData is invalid.");
                return;
            }

            if (_textMesh == null)
            {
                _textMesh = GetComponent<TextMeshPro>();
            }

            if (_meshRenderer == null)
            {
                _meshRenderer = GetComponent<MeshRenderer>();
            }

            if (_meshRenderer != null)
            {
                _meshRenderer.sortingLayerName = "Top";
                _meshRenderer.sortingOrder = 100;
            }

            _startPosition = _data.Position;
            CachedTransform.position = _startPosition;
            CachedTransform.localScale = Vector3.one * 0.5f;

            if (_textMesh != null)
            {
                _textMesh.text = _data.Text;
                _textMesh.color = _data.Color;
                _textMesh.alpha = 1f;
            }

            _elapsed = 0f;
        }

        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            if (_data == null)
            {
                return;
            }

            _elapsed += elapseSeconds;
            float duration = _data.Duration > 0f ? _data.Duration : 0.65f;
            float k = Mathf.Clamp01(_elapsed / duration);

            // 平滑缓动上浮
            float easeOutY = Mathf.Sin(k * Mathf.PI * 0.5f);
            CachedTransform.position = _startPosition + Vector3.up * (_data.FloatDistance * easeOutY);

            // 微弹动效：前 20% 时间自 0.5 倍微弹至 1.15 倍，后续回落至 1.0 倍
            float scale = k < 0.2f
                ? Mathf.Lerp(0.5f, 1.15f, k / 0.2f)
                : Mathf.Lerp(1.15f, 1f, (k - 0.2f) / 0.8f);
            CachedTransform.localScale = Vector3.one * scale;

            // 后半程（自 60% 进度开始）渐隐淡出
            if (_textMesh != null)
            {
                float alpha = k < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (k - 0.6f) / 0.4f);
                _textMesh.alpha = alpha;
            }

            // 动画播放完毕后回收至实体对象池
            if (k >= 1f)
            {
                GameEntry.Entity.HideEntity(this);
            }
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            _data = null;
            _elapsed = 0f;
            base.OnHide(isShutdown, userData);
        }
    }
}
