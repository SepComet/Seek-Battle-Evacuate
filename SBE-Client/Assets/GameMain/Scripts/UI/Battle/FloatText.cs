using TMPro;
using UnityEngine;

namespace SepCore.UI
{
    /// <summary>
    /// 独立飘字：出现后上浮淡出，全程不受界面刷新影响，结束自毁。
    /// 每个战斗事件生成一个，互不打断。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloatText : MonoBehaviour
    {
        private TextMeshProUGUI _label;
        private Vector2 _basePos;
        private float _elapsed;
        private float _durationSeconds = 0.8f;
        private float _heightPx = 36f;

        /// <summary>
        /// 以模板为样板生成一个飘字；模板不可用或文本为空时什么都不做。
        /// 时长与上浮高度来自全局表（毫秒/像素）。
        /// </summary>
        public static void Spawn(TextMeshProUGUI template, string text)
        {
            if (string.IsNullOrEmpty(text) || !template.gameObject.activeInHierarchy)
            {
                return;
            }

            GameObject go = Instantiate(template.gameObject, template.transform.parent);
            go.name = template.gameObject.name + "_Float";
            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            SepCore.Definition.GlobalConfig global = GameEntry.Luban.Global.Data;
            FloatText floatText = go.AddComponent<FloatText>();
            floatText.Play(label, text, global.CardStateTextDurationMs / 1000f, global.CardStateTextFloatPx);
        }

        private void Play(TextMeshProUGUI label, string text, float durationSeconds, float heightPx)
        {
            _label = label;
            _durationSeconds = durationSeconds > 0f ? durationSeconds : 0.8f;
            _heightPx = heightPx;
            _label.text = text;
            _label.alpha = 1f;
            _basePos = _label.rectTransform.anchoredPosition;
            _elapsed = 0f;
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(_elapsed / _durationSeconds);
            _label.rectTransform.anchoredPosition = _basePos + Vector2.up * (_heightPx * k);
            _label.alpha = 1f - k;
            if (k >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
