using UnityEngine;
using UnityEngine.Sprites;

namespace SepCore.Exploration
{
    /// <summary>
    /// Sprite 交互描边效果辅助器。
    /// 统一管理 2D Sprite 描边着色器、材质替换及 MaterialPropertyBlock 属性设置，
    /// 开启时同步切片边界与外扩包围盒，关闭时还原初始材质与默认包围盒。
    /// </summary>
    public static class SpriteOutlineApplier
    {
        private static Material s_OutlineMaterial = null;
        private static readonly int OutlineSizeId = Shader.PropertyToID("_OutlineSize");
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int SpriteUVRectId = Shader.PropertyToID("_SpriteUVRect");
        private static readonly int SpriteSizeId = Shader.PropertyToID("_SpriteSize");

        private const string OutlineShaderName = "SepCore/2D/SpriteOutline";

        /// <summary>
        /// 为指定的 SpriteRenderer 应用或移除描边高亮。
        /// </summary>
        /// <param name="spriteRenderer">目标 Sprite 渲染器。</param>
        /// <param name="originalMaterial">缓存的实体原始材质（引用传递，首次自动初始化）。</param>
        /// <param name="propertyBlock">缓存的 MaterialPropertyBlock（引用传递，首次自动初始化）。</param>
        /// <param name="enable">是否开启描边。</param>
        /// <param name="outlineColor">描边颜色。</param>
        /// <param name="outlineSize">描边像素宽度（默认 1.5 像素）。</param>
        public static void ApplyOutline(
            SpriteRenderer spriteRenderer,
            ref Material originalMaterial,
            ref MaterialPropertyBlock propertyBlock,
            bool enable,
            Color outlineColor,
            float outlineSize = 1.5f)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (originalMaterial == null)
            {
                originalMaterial = spriteRenderer.sharedMaterial;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            if (enable)
            {
                EnsureOutlineMaterial();
                if (s_OutlineMaterial != null)
                {
                    spriteRenderer.material = s_OutlineMaterial;
                }

                spriteRenderer.GetPropertyBlock(propertyBlock);
                Sprite sprite = spriteRenderer.sprite;
                float size = Mathf.Max(0.5f, outlineSize);
                // 道具图片可能仍在异步加载；加载完成后由调用方重新应用高亮。
                propertyBlock.SetFloat(OutlineSizeId, sprite != null ? size : 0f);
                propertyBlock.SetColor(OutlineColorId, outlineColor);
                if (sprite != null)
                {
                    Vector4 uvRect = DataUtility.GetOuterUV(sprite);
                    Vector2 spriteSize = sprite.rect.size / sprite.pixelsPerUnit;
                    propertyBlock.SetVector(SpriteUVRectId, uvRect);
                    // 非实例化 SpriteRenderer 已翻转输入顶点；UV 方向需要同样翻转。
                    propertyBlock.SetVector(SpriteSizeId, new Vector4(
                        spriteRenderer.flipX ? -spriteSize.x : spriteSize.x,
                        spriteRenderer.flipY ? -spriteSize.y : spriteSize.y, 0f, 0f));

                    // 与顶点外扩保持一致，防止原图离开相机后仍可见的描边被提前剔除。
                    Vector2 padding = new Vector2(
                        size * spriteSize.x / ((uvRect.z - uvRect.x) * sprite.texture.width),
                        size * spriteSize.y / ((uvRect.w - uvRect.y) * sprite.texture.height));
                    spriteRenderer.ResetLocalBounds();
                    Bounds bounds = spriteRenderer.localBounds;
                    bounds.Expand(new Vector3(padding.x * 2f, padding.y * 2f, 0f));
                    spriteRenderer.localBounds = bounds;
                }
                spriteRenderer.SetPropertyBlock(propertyBlock);
            }
            else
            {
                if (originalMaterial != null)
                {
                    spriteRenderer.material = originalMaterial;
                }

                spriteRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(OutlineSizeId, 0f);
                spriteRenderer.SetPropertyBlock(propertyBlock);
                spriteRenderer.ResetLocalBounds();
            }
        }

        private static void EnsureOutlineMaterial()
        {
            if (s_OutlineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find(OutlineShaderName);
            if (shader != null)
            {
                s_OutlineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.DontSave
                };
            }
        }
    }
}
