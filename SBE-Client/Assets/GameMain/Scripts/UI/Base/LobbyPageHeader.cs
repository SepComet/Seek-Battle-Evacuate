using System;
using SepCore.Base;
using SepCore.Definition;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 大厅顶级页面共用的返回入口与连接状态栏。
    /// 由所属 Form 的 OnOpen/OnClose 管理绑定；连接状态与信用点余额为只读展示
    /// （余额来自 GameEntry.Net 的服务器快照，本组件不发起任何经济操作）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyPageHeader : MonoBehaviour
    {
        [SerializeField] private Button _backButton = null;
        [SerializeField] private FormatTextUI _connectionText = null;
        [SerializeField] private FormatTextUI _currencyText = null;
        [SerializeField] private bool _canReturnToLobby = false;

        private int _ownerSerialId;
        private bool _returning;
        private string _statusKey;
        private bool _renderedHasCredits;
        private long _renderedCredits;

        public void Open(int serialId)
        {
            if (serialId <= 0)
            {
                Log.Error("Lobby page header requires a valid form serial ID.");
                throw new ArgumentOutOfRangeException(nameof(serialId));
            }

            Close();
            _ownerSerialId = serialId;
            _returning = false;
            _statusKey = null;
            _backButton.gameObject.SetActive(_canReturnToLobby);
            _backButton.interactable = _canReturnToLobby;
            _backButton.onClick.AddListener(ReturnToLobby);
            RefreshCurrency();
            RefreshStatus();
        }

        public void Close()
        {
            _backButton.onClick.RemoveListener(ReturnToLobby);
            _ownerSerialId = 0;
            _returning = false;
            _statusKey = null;
        }

        public void RefreshStatus()
        {
            string statusKey;
            if (GameEntry.Net != null)
            {
                statusKey = GameEntry.Net.OnlineReady
                    ? "LobbyStatusConnected"
                    : "LobbyStatusReconnecting";
            }
            else
            {
                statusKey = GameEntry.Save != null && GameEntry.Save.IsReady
                    ? "LobbyStatusLocalReady"
                    : "LobbyStatusUnavailable";
            }

            if (_statusKey == statusKey)
            {
                return;
            }

            FormatText status = GameEntry.Luban.Tables.TbFormatText.GetOrDefault(statusKey);
            if (status == null)
            {
                Log.Error("Can not find lobby status text '{0}'.", statusKey);
                throw new InvalidOperationException("Missing lobby status text: " + statusKey);
            }

            _connectionText.Set(status.Format);
            _statusKey = statusKey;
        }

        private void Update()
        {
            if (!IsCurrentPage())
            {
                return;
            }

            RefreshStatus();
            RefreshCurrency();
            if (_canReturnToLobby && !_returning && Input.GetKeyDown(KeyCode.Escape) && !IsEditingText())
            {
                ReturnToLobby();
            }
        }

        /// <summary>
        /// 渲染信用点余额：拿到过服务器快照显示真实数值，否则保留占位文案。
        /// 与连接状态一样逐帧调用，按已渲染值做缓存守卫避免每帧重设文本。
        /// </summary>
        private void RefreshCurrency()
        {
            bool hasCredits = GameEntry.Net != null && GameEntry.Net.Warehouse != null &&
                              GameEntry.Net.Warehouse.HasCredits;
            long credits = hasCredits ? GameEntry.Net.Warehouse.Credits : 0;
            if (_renderedHasCredits == hasCredits && (!_renderedHasCredits || _renderedCredits == credits))
            {
                return;
            }

            _currencyText.Set(hasCredits ? credits : (object)"—");
            _renderedHasCredits = hasCredits;
            _renderedCredits = credits;
        }

        private bool IsCurrentPage()
        {
            if (_ownerSerialId == 0)
            {
                return false;
            }

            UIForm owner = GameEntry.UI.GetUIForm(_ownerSerialId);
            return owner != null && owner.Logic is UGuiForm form && form.Available && form.Visible && !form.IsTransitioning &&
                   !owner.UIGroup.Pause && owner.UIGroup.CurrentUIForm == owner;
        }

        private static bool IsEditingText()
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
            {
                return false;
            }

            GameObject selected = eventSystem.currentSelectedGameObject;
            TMP_InputField tmpInput = selected.GetComponentInParent<TMP_InputField>();
            if (tmpInput != null && tmpInput.isFocused)
            {
                return true;
            }

            InputField input = selected.GetComponentInParent<InputField>();
            return input != null && input.isFocused;
        }

        private void ReturnToLobby()
        {
            if (!_canReturnToLobby || _returning || !IsCurrentPage())
            {
                return;
            }

            _returning = true;
            _backButton.interactable = false;
            GameEntry.Event.Fire(this, LobbyPageBackRequestedEventArgs.Create(_ownerSerialId));
        }
    }
}
