using UnityEngine;

namespace SepCore.Entity
{
    /// <summary>
    /// 将摄像机子物体上的背景精灵等比铺满正交视口，随父摄像机移动。
    /// </summary>
    public sealed class CameraBackground : MonoBehaviour
    {
        [SerializeField] private Camera _camera = null;
        [SerializeField] private SpriteRenderer _background = null;

        private void OnEnable()
        {
            FitViewport();
        }

        private void LateUpdate()
        {
            FitViewport();
        }

        private void FitViewport()
        {
            Vector3 spriteSize = _background.sprite.bounds.size;
            float height = _camera.orthographicSize * 2f;
            float scale = Mathf.Max(height * _camera.aspect / spriteSize.x, height / spriteSize.y);
            _background.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
