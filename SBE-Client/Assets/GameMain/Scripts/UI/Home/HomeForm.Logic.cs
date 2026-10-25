using SepCore.Definition;
using UnityGameFramework.Runtime;

namespace SepCore.UI
{
    /// <summary>
    /// 主界面（战备）逻辑（手写 partial，与自动生成的 HomeForm.cs 合并）。
    /// 作为嵌套表单，编排子表单 SquadForm 与 DeploymentForm 的刷新。
    /// </summary>
    public partial class HomeForm : UGuiForm
    {
        private bool _listenersBound = false;

        /// <summary>
        /// 刷新主界面（战备编队角色列表与出战面板）。
        /// </summary>
        public void Refresh(SaveData save)
        {
            if (save == null)
            {
                Log.Error("Save data is null, cannot refresh HomeForm.");
                return;
            }

            EnsureListenersBound();
            View.squadForm.RefreshCharacterList(save.characters);
            View.deploymentForm.Refresh();
        }

        private void EnsureListenersBound()
        {
            if (_listenersBound)
            {
                return;
            }

            _listenersBound = true;
            View.squadForm.OnPartyChanged += OnSquadPartyChanged;
        }

        private void OnSquadPartyChanged()
        {
            View.deploymentForm.Refresh();
        }

        private void OnDestroy()
        {
            if (_listenersBound)
            {
                _listenersBound = false;
                View.squadForm.OnPartyChanged -= OnSquadPartyChanged;
            }
        }
    }
}
