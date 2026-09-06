using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using SepCore.AsyncTask;
using SepCore.Definition;
using SepCore.Exploration;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 玩家角色实体逻辑。
    /// 领队（队伍第 1 位）挂载移动控制器接入全局输入，并持有蛇形编队控制器；
    /// 随从仅作为跟随体存在，移动由领队的编队控制器沿轨迹驱动。
    /// 实体显示时按角色 ID 查配置表加载图标资源，初始化角色贴图。
    /// </summary>
    public sealed class PlayerCharacterLogic : EntityBase
    {
        private PlayerCharacterData _data;
        private PlayerCharacterController _leaderController;
        private SnakePartyController _partyController;
        private CharacterInteractDetector _interactDetector;
        private SpriteRenderer _spriteRenderer;
        private int _spriteVersion;

        /// <summary>
        /// 当前绑定的玩家角色实体数据。
        /// </summary>
        public PlayerCharacterData Data => _data;

        /// <summary>
        /// 队伍序号（1 起，1 为领队）；数据未就绪时为 0。
        /// </summary>
        public int PartyOrder => _data != null ? _data.PartyOrder : 0;

        /// <summary>
        /// 是否为领队。
        /// </summary>
        public bool IsLeader => PartyOrder == 1;

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            _data = userData as PlayerCharacterData;
            if (_data == null)
            {
                Log.Error("Player character entity data is invalid.");
                return;
            }

            _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            if (_spriteRenderer != null)
            {
                // 序号越小的角色渲染在越上层，队伍重叠时领队不被随从遮挡
                _spriteRenderer.sortingOrder = -_data.PartyOrder;
                // 实体组会池化复用实例，先清掉上一位角色的残留贴图，避免新贴图加载完成前显示错误角色
                _spriteRenderer.sprite = null;
            }

            _spriteVersion++;
            ShowCharacterSpriteAsync(_spriteVersion).Forget();

            if (!_data.IsLeader)
            {
                return;
            }

            // 实体组会池化复用实例，领队组件的挂载与配置均需幂等
            if (GetComponent<Rigidbody2D>() == null)
            {
                Log.Error("Player leader entity for character '{0}' has no Rigidbody2D configured on prefab.",
                    _data.CharacterId);
            }

            _leaderController = GetComponent<PlayerCharacterController>();
            if (_leaderController == null)
            {
                _leaderController = gameObject.AddComponent<PlayerCharacterController>();
            }

            _leaderController.SetInputSource(CharacterInputBridge.DefaultInput);
            _leaderController.CanMove = true;

            _partyController = GetComponent<SnakePartyController>();
            if (_partyController == null)
            {
                _partyController = gameObject.AddComponent<SnakePartyController>();
            }

            Transform triggerTransform = transform.Find("InteractTrigger");
            if (triggerTransform != null)
            {
                _interactDetector = triggerTransform.GetComponent<CharacterInteractDetector>();
                if (_interactDetector == null)
                {
                    _interactDetector = triggerTransform.gameObject.AddComponent<CharacterInteractDetector>();
                }

                _interactDetector.Initialize(_leaderController);
            }
            else
            {
                Log.Warning("Player leader entity has no 'InteractTrigger' child object.");
            }
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            _data = null;
            _leaderController = null;
            _partyController = null;
            _interactDetector = null;
            _spriteRenderer = null;
            base.OnHide(isShutdown, userData);
        }

        /// <summary>
        /// 绑定随从实体（按跟随顺序），由地图构建流程在全部玩家角色实体生成完成后调用；
        /// 单人出战时传入空列表，仅启用领队移动与探索暂停门禁。
        /// </summary>
        public void BindParty(IReadOnlyList<PlayerCharacterLogic> retinues)
        {
            if (_partyController == null)
            {
                Log.Error("Can not bind party because leader entity has no snake party controller.");
                return;
            }

            _partyController.Bind(_leaderController, retinues);
        }

        /// <summary>
        /// 按角色 ID 查配置表异步加载图标资源并应用到实体贴图；
        /// 版本号用于防止池化复用时旧加载结果覆盖新角色。
        /// </summary>
        private async UniTaskVoid ShowCharacterSpriteAsync(int version)
        {
            CharacterConfig characterConfig = GameEntry.Luban.Get<CharacterConfig>(_data.CharacterId);
            if (characterConfig == null)
            {
                Log.Error("Player character config '{0}' is missing.", _data.CharacterId);
                return;
            }

            if (characterConfig.Icon_Ref == null)
            {
                Log.Error("Player character '{0}' icon '{1}' is missing in SpriteConfig.",
                    _data.CharacterId, characterConfig.Icon);
                return;
            }

            Sprite sprite = await SpriteLoader.LoadSpriteAsync(characterConfig.Icon_Ref);
            if (sprite == null || _spriteRenderer == null || _spriteVersion != version)
            {
                return;
            }

            _spriteRenderer.sprite = sprite;
        }
    }
}
