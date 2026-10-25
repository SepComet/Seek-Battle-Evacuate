using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.Entity
{
    /// <summary>
    /// 主摄像机实体逻辑。
    /// 负责在 2D 探索场景中平滑跟随目标（领队角色），锁定 Z 轴深度并提供即时对齐与目标切换接口。
    /// </summary>
    public sealed class MainCameraLogic : EntityBase
    {
        [SerializeField] private float _smoothTime = 0.15f;
        [SerializeField] private Vector2 _offset = Vector2.zero;
        [SerializeField] private float _cameraZ = -10f;

        private Camera _camera;
        private Camera _minimapCamera;
        private Transform _target;
        private Vector3 _currentVelocity = Vector3.zero;

        /// <summary>
        /// 摄像机组件。
        /// </summary>
        public Camera Camera => _camera;

        /// <summary>
        /// 小地图相机视口覆盖的世界尺寸（宽、高）。
        /// </summary>
        public Vector2 MinimapViewportSize
        {
            get
            {
                float height = _minimapCamera.orthographicSize * 2f;
                float aspect = (float)_minimapCamera.targetTexture.width / _minimapCamera.targetTexture.height;
                return new Vector2(height * aspect, height);
            }
        }

        /// <summary>
        /// 当前跟随目标。
        /// </summary>
        public Transform Target => _target;

        /// <summary>
        /// 平滑过渡阻尼时间（秒）。
        /// </summary>
        public float SmoothTime
        {
            get => _smoothTime;
            set => _smoothTime = Mathf.Max(0f, value);
        }

        /// <summary>
        /// 相对于跟随目标的 XY 平面偏移。
        /// </summary>
        public Vector2 Offset
        {
            get => _offset;
            set => _offset = value;
        }

        /// <summary>
        /// 固定的摄像机 Z 轴深度。
        /// </summary>
        public float CameraZ
        {
            get => _cameraZ;
            set => _cameraZ = value;
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            _camera = GetComponent<Camera>();
            if (_camera == null)
            {
                Log.Error("MainCameraLogic on '{0}' has no Camera component attached.", gameObject.name);
            }

            foreach (Camera childCamera in GetComponentsInChildren<Camera>(true))
            {
                if (childCamera != _camera)
                {
                    _minimapCamera = childCamera;
                    break;
                }
            }

            if (_minimapCamera == null || _minimapCamera.targetTexture == null)
            {
                Log.Error("MainCameraLogic on '{0}' has no minimap camera rendering to a target texture.", gameObject.name);
            }
        }

        protected override void OnShow(object userData)
        {
            base.OnShow(userData);

            MainCameraData data = userData as MainCameraData;
            if (data == null)
            {
                Log.Error("Main camera entity data is invalid.");
                return;
            }

            _currentVelocity = Vector3.zero;
        }

        protected override void OnHide(bool isShutdown, object userData)
        {
            _target = null;
            _currentVelocity = Vector3.zero;
            base.OnHide(isShutdown, userData);
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            Vector3 targetPos = _target.position;
            Vector3 destination = new Vector3(targetPos.x + _offset.x, targetPos.y + _offset.y, _cameraZ);

            if (_smoothTime > 0f)
            {
                transform.position = Vector3.SmoothDamp(transform.position, destination, ref _currentVelocity, _smoothTime);
            }
            else
            {
                transform.position = destination;
            }
        }

        /// <summary>
        /// 设置摄像机跟随目标。
        /// </summary>
        /// <param name="target">跟随目标的 Transform。</param>
        /// <param name="immediate">是否立即对齐到目标位置（重置阻尼速度，避免跨越全图平滑拉伸）。</param>
        public void SetFollowTarget(Transform target, bool immediate = true)
        {
            _target = target;
            _currentVelocity = Vector3.zero;

            if (immediate && _target != null)
            {
                Vector3 targetPos = _target.position;
                transform.position = new Vector3(targetPos.x + _offset.x, targetPos.y + _offset.y, _cameraZ);
            }
        }

        /// <summary>
        /// 清除跟随目标。
        /// </summary>
        public void ClearFollowTarget()
        {
            _target = null;
            _currentVelocity = Vector3.zero;
        }
    }
}
