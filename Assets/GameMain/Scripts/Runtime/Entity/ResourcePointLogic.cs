using SepCore.Definition;
using UnityGameFramework.Runtime;

using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 资源点实体逻辑。
    /// 实现 IInteractable 接口，支持被领队探测器识别为最高优先级可交互对象。
    /// </summary>
    public sealed class ResourcePointLogic : EntityBase, IInteractable
    {
        private ResourcePointData _data;
        private ResourcePointConfig _config;

        /// <summary>
        /// 当前绑定的资源点实体数据。
        /// </summary>
        public ResourcePointData Data => _data;

        /// <summary>
        /// 当前资源点对应的 Luban 静态配置。
        /// </summary>
        public ResourcePointConfig Config => _config;

        public InteractableType InteractableType => InteractableType.ResourcePoint;

        public Rarity Rarity => Rarity.None;

        public Vector3 Position => transform.position;

        public bool CanInteract => _data != null;

        public GameObject EntityGameObject => gameObject;

        public void OnInteract(GameObject interactor)
        {
            Log.Info("Interacting with ResourcePoint '{0}' (ConfigId: {1}).",
                Entity.Id, _data != null ? _data.ResourcePointId : 0);
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _data = userData as ResourcePointData;
            if (_data == null)
            {
                Log.Error("Resource point entity data is invalid.");
                return;
            }

            _config = GameEntry.Luban.Get<ResourcePointConfig>(_data.ResourcePointId);
            if (_config == null)
            {
                Log.Error("Resource point config '{0}' is invalid.", _data.ResourcePointId);
            }
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            _data = null;
            _config = null;
            base.OnHide(isShutdown, userData);
        }
    }
}
