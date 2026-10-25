using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 使用 Luban 格式化文本表（TbFormatText）驱动 TextMeshProUGUI 的组件。
    /// 在 Inspector 上填写 Key，调用 Set 传入格式化参数即可更新文本。
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class FormatTextUI : MonoBehaviour
    {
        [SerializeField] private string key;

        private TextMeshProUGUI _text;

        private void Awake()
        {
            _text = GetComponent<TextMeshProUGUI>();
        }

        public void Set(params object[] args)
        {
            FormatText format = GameEntry.Luban.Tables.TbFormatText.GetOrDefault(key);
            if (format == null)
            {
                Log.Warning("Can not find format text '{0}'.", key);
                return;
            }

            _text.SetText(string.Format(format.Format, args));
        }

        public void Clear()
        {
            _text.SetText(string.Empty);
        }
    }
}