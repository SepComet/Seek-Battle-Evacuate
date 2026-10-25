using System.Globalization;
using Cysharp.Threading.Tasks;
using SepCore.Net;

namespace SepCore.UI
{
    /// <summary>
    /// 登录/注册表单：账密输入提交给网络层，服务器错误码映射 FormatText 文案。
    /// 成功由 ProcedureLogin 轮询 OnlineReady 感知并切换场景（CloseSceneUIForms 会关闭本表单）。
    /// </summary>
    public partial class LoginForm
    {
        private bool _listenersBound;
        private bool _submitting;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            EnsureListenersBound();

            // 预填上次登录的用户名；密码出于安全不落盘，始终为空
            AccountSession.Load(out _, out _, out string username);
            View.usernameInput.text = username ?? string.Empty;
            View.passwordInput.text = string.Empty;
            SetError(string.Empty);
        }

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.loginButton.onClick.AddListener(() => Submit(false));
            View.registerButton.onClick.AddListener(() => Submit(true));
        }

        private void Submit(bool register)
        {
            if (_submitting)
            {
                return;
            }

            // 客户端预校验与服务端规则一致（服务端会 trim 用户名）
            string username = View.usernameInput.text != null ? View.usernameInput.text.Trim() : string.Empty;
            string password = View.passwordInput.text ?? string.Empty;
            if (username.Length < 2 || username.Length > 32)
            {
                SetError(Format("AccountErrorInvalidUsername"));
                return;
            }
            if (password.Length < 6 || password.Length > 64)
            {
                SetError(Format("AccountErrorInvalidPassword"));
                return;
            }

            SubmitAsync(username, password, register).Forget();
        }

        private async UniTaskVoid SubmitAsync(string username, string password, bool register)
        {
            _submitting = true;
            SetInputsInteractable(false);
            SetError(Format("AccountSubmitInProgress"));

            LoginResult result = await GameEntry.Net.TryCredentialLoginAsync(username, password, register);

            _submitting = false;
            SetInputsInteractable(true);
            if (result.Success)
            {
                // ProcedureLogin 轮询 OnlineReady 后切场景，本表单随 CloseSceneUIForms 关闭
                SetError(string.Empty);
                return;
            }

            SetError(Format(KeyForResult(result)));
        }

        private static string KeyForResult(LoginResult result)
        {
            if (result.Outcome == NetLoginOutcome.Failed)
            {
                return "AccountErrorNetwork";
            }

            switch (result.ErrCode)
            {
                case NetErrCode.Conflict: return "AccountErrorUsernameTaken";
                case NetErrCode.Unauthorized: return "AccountErrorCredentials";
                case NetErrCode.Rejected: return "AccountErrorInvalid";
                default: return "AccountErrorInternal";
            }
        }

        private void SetInputsInteractable(bool interactable)
        {
            View.usernameInput.interactable = interactable;
            View.passwordInput.interactable = interactable;
            View.loginButton.interactable = interactable;
            View.registerButton.interactable = interactable;
        }

        private void SetError(string text)
        {
            View.errorText.text = text;
        }

        private static string Format(string key, params object[] args)
        {
            return string.Format(CultureInfo.InvariantCulture, GameEntry.Luban.Tables.TbFormatText.Get(key).Format, args);
        }
    }
}
