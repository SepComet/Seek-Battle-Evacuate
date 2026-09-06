using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using SepCore.Definition;
using SepCore.Utility;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace SepCore.AsyncTask
{
    /// <summary>
    /// 图集 Sprite 加载器，通过图集名 + 序号定位 Sprite。
    /// 序号 -1 表示图集本身就是一张完整 Sprite，不需要切分。
    /// </summary>
    public static class SpriteLoader
    {
        private const float DefaultPixelsPerUnit = 32f;

        private static readonly Dictionary<string, Texture2D> _textureCache = new();
        private static readonly Dictionary<string, Task<Texture2D>> _loadingTextures = new();
        private static readonly Dictionary<string, Dictionary<int, Sprite>> _spriteCache = new();

        /// <summary>
        /// 异步加载图集 Sprite
        /// </summary>
        /// <param name="config">Sprite 配置</param>
        /// <param name="tileSize">切分块尺寸（像素），默认 32</param>
        /// <param name="pixelsPerUnit">Sprite 像素单位比，默认 32</param>
        /// <param name="timeout">超时时间（秒），0 表示不超时</param>
        /// <returns>加载的 Sprite，失败时返回 null</returns>
        public static UniTask<Sprite> LoadSpriteAsync(SpriteConfig config, int tileSize = 32,
            float pixelsPerUnit = DefaultPixelsPerUnit, float timeout = 0f)
        {
            return LoadSpriteAsync(config.AtlasPath, config.SpriteIndex, tileSize, pixelsPerUnit, timeout);
        }

        /// <summary>
        /// 异步加载图集 Sprite
        /// </summary>
        /// <param name="atlasName">图集名称（相对 Assets/GameMain/Textures 的路径，含扩展名，如 32rogues/monsters.png）</param>
        /// <param name="index">序号，-1 表示整张图集作为一张 Sprite</param>
        /// <param name="tileSize">切分块尺寸（像素），默认 32</param>
        /// <param name="pixelsPerUnit">Sprite 像素单位比，默认 32</param>
        /// <param name="timeout">超时时间（秒），0 表示不超时</param>
        /// <returns>加载的 Sprite，失败时返回 null</returns>
        public static async UniTask<Sprite> LoadSpriteAsync(string atlasName, int index, int tileSize = 32,
            float pixelsPerUnit = DefaultPixelsPerUnit, float timeout = 0f)
        {
            Texture2D texture = await GetTextureAsync(atlasName, timeout);
            if (texture == null)
            {
                return null;
            }

            int cacheKey = index < 0 ? -1 : index;
            if (!_spriteCache.TryGetValue(atlasName, out Dictionary<int, Sprite> sprites))
            {
                sprites = new Dictionary<int, Sprite>();
                _spriteCache[atlasName] = sprites;
            }

            if (sprites.TryGetValue(cacheKey, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = CreateSprite(texture, cacheKey, tileSize, pixelsPerUnit);
            if (sprite != null)
            {
                sprites[cacheKey] = sprite;
            }
            return sprite;
        }

        /// <summary>
        /// 释放指定图集的所有 Sprite 和纹理引用
        /// </summary>
        public static void Release(string atlasName)
        {
            if (_spriteCache.TryGetValue(atlasName, out Dictionary<int, Sprite> sprites))
            {
                foreach (Sprite sprite in sprites.Values)
                {
                    if (sprite != null)
                    {
                        UnityEngine.Object.Destroy(sprite);
                    }
                }
                sprites.Clear();
                _spriteCache.Remove(atlasName);
            }

            if (_textureCache.TryGetValue(atlasName, out Texture2D texture))
            {
                global::GameEntry.Resource.UnloadAsset(texture);
                _textureCache.Remove(atlasName);
            }
        }

        /// <summary>
        /// 释放所有图集的 Sprite 和纹理引用
        /// </summary>
        public static void ReleaseAll()
        {
            List<string> atlasNames = new List<string>(_textureCache.Keys);
            foreach (string atlasName in atlasNames)
            {
                Release(atlasName);
            }
        }

        private static async UniTask<Texture2D> GetTextureAsync(string atlasName, float timeout)
        {
            if (_textureCache.TryGetValue(atlasName, out Texture2D cached))
            {
                return cached;
            }

            if (_loadingTextures.TryGetValue(atlasName, out Task<Texture2D> loading))
            {
                return await loading;
            }

            Task<Texture2D> task = LoadTextureAsync(atlasName, timeout).AsTask();
            _loadingTextures[atlasName] = task;
            try
            {
                Texture2D texture = await task;
                if (texture != null)
                {
                    _textureCache[atlasName] = texture;
                }
                return texture;
            }
            finally
            {
                _loadingTextures.Remove(atlasName);
            }
        }

        private static async UniTask<Texture2D> LoadTextureAsync(string atlasName, float timeout)
        {
            try
            {
                return await global::GameEntry.Resource.LoadAssetAsync<Texture2D>(
                    AssetUtility.GetSpriteAsset(atlasName), Constant.AssetPriority.SpriteAsset, timeout: timeout);
            }
            catch (Exception e)
            {
                Log.Error("加载图集失败: {0}, error: {1}", atlasName, e);
                return null;
            }
        }

        private static Sprite CreateSprite(Texture2D texture, int index, int tileSize, float pixelsPerUnit)
        {
            if (index < 0)
            {
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }

            int columns = Mathf.Max(1, texture.width / tileSize);
            int rows = Mathf.Max(1, texture.height / tileSize);
            int column = index % columns;
            int row = index / columns;
            if (row >= rows)
            {
                Log.Error("图集 {0} 序号 {1} 超出范围 (cols: {2}, rows: {3})", texture.name, index, columns, rows);
                return null;
            }

            float x = column * tileSize;
            float y = texture.height - (row + 1) * tileSize;
            // 交互描边需要完整矩形网格，才能在 Shader 中为图案四周扩展留白。
            return Sprite.Create(texture, new Rect(x, y, tileSize, tileSize),
                new Vector2(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
        }
    }
}
