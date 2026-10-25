//------------------------------------------------------------
// Game Framework
// Copyright © 2013-2021 Jiang Yin. All rights reserved.
// Homepage: https://gameframework.cn/
// Feedback: mailto:ellan@gameframework.cn
//------------------------------------------------------------

using GameFramework;
using TMPro;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    public class DialogForm : UGuiForm
    {
        [SerializeField] private TMP_Text _titleText = null;
        [SerializeField] private TMP_Text _messageText = null;
        [SerializeField] private GameObject[] _modeObjects = null;
        [SerializeField] private TMP_Text[] _confirmTexts = null;
        [SerializeField] private TMP_Text[] _cancelTexts = null;
        [SerializeField] private TMP_Text[] _otherTexts = null;

        private int _dialogMode = 1;
        private bool _pauseGame = false;
        private object _userData = null;
        private GameFrameworkAction<object> _onClickConfirmGFAction = null;
        private GameFrameworkAction<object> _onClickCancelGFAction = null;
        private GameFrameworkAction<object> _onClickOtherGFAction = null;

        public int DialogMode => _dialogMode;

        public bool PauseGame => _pauseGame;

        public object UserData => _userData;

        public void OnConfirmButtonClick()
        {
            Close();

            if (_onClickConfirmGFAction != null)
            {
                _onClickConfirmGFAction(_userData);
            }
        }

        public void OnCancelButtonClick()
        {
            Close();

            if (_onClickCancelGFAction != null)
            {
                _onClickCancelGFAction(_userData);
            }
        }

        public void OnOtherButtonClick()
        {
            Close();

            if (_onClickOtherGFAction != null)
            {
                _onClickOtherGFAction(_userData);
            }
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnOpen(object userData)
#else
        protected internal override void OnOpen(object userData)
#endif
        {
            base.OnOpen(userData);

            DialogParams dialogParams = (DialogParams)userData;
            if (dialogParams == null)
            {
                Log.Warning("DialogParams is invalid.");
                return;
            }

            _dialogMode = dialogParams.Mode;
            RefreshDialogMode();

            _titleText.text = dialogParams.Title;
            _messageText.text = dialogParams.Message;

            _pauseGame = dialogParams.PauseGame;
            RefreshPauseGame();

            _userData = dialogParams.UserData;

            RefreshConfirmText(dialogParams.ConfirmText);
            _onClickConfirmGFAction = dialogParams.OnClickConfirm;

            RefreshCancelText(dialogParams.CancelText);
            _onClickCancelGFAction = dialogParams.OnClickCancel;

            RefreshOtherText(dialogParams.OtherText);
            _onClickOtherGFAction = dialogParams.OnClickOther;
        }

#if UNITY_2017_3_OR_NEWER
        protected override void OnClose(bool isShutdown, object userData)
#else
        protected internal override void OnClose(bool isShutdown, object userData)
#endif
        {
            if (_pauseGame)
            {
                GameEntry.Base.ResumeGame();
            }

            _dialogMode = 1;
            _titleText.text = string.Empty;
            _messageText.text = string.Empty;
            _pauseGame = false;
            _userData = null;

            RefreshConfirmText(string.Empty);
            _onClickConfirmGFAction = null;

            RefreshCancelText(string.Empty);
            _onClickCancelGFAction = null;

            RefreshOtherText(string.Empty);
            _onClickOtherGFAction = null;

            base.OnClose(isShutdown, userData);
        }

        private void RefreshDialogMode()
        {
            for (int i = 1; i <= _modeObjects.Length; i++)
            {
                _modeObjects[i - 1].SetActive(i == _dialogMode);
            }
        }

        private void RefreshPauseGame()
        {
            if (_pauseGame)
            {
                GameEntry.Base.PauseGame();
            }
        }

        private void RefreshConfirmText(string confirmText)
        {
            if (string.IsNullOrEmpty(confirmText))
            {
                confirmText = GameEntry.Localization.GetString("Dialog.ConfirmButton");
            }

            for (int i = 0; i < _confirmTexts.Length; i++)
            {
                _confirmTexts[i].text = confirmText;
            }
        }

        private void RefreshCancelText(string cancelText)
        {
            if (string.IsNullOrEmpty(cancelText))
            {
                cancelText = GameEntry.Localization.GetString("Dialog.CancelButton");
            }

            for (int i = 0; i < _cancelTexts.Length; i++)
            {
                _cancelTexts[i].text = cancelText;
            }
        }

        private void RefreshOtherText(string otherText)
        {
            if (string.IsNullOrEmpty(otherText))
            {
                otherText = GameEntry.Localization.GetString("Dialog.OtherButton");
            }

            for (int i = 0; i < _otherTexts.Length; i++)
            {
                _otherTexts[i].text = otherText;
            }
        }
    }
}
