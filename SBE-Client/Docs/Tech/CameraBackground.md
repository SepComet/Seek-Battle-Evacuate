# 地牢相机背景

贴图：`Assets/GameMain/Textures/DungeonBackground.png`。使用内置 imagegen 生成，非天空盒。

`MainCamera.prefab` 的 `DungeonBackground` 子物体使用 URP Sprite-Unlit-Default 材质，Default 排序层、Order -32768，位于相机局部 Z=90（当前远裁剪面为 100）。`CameraBackground` 按正交相机视口等比覆盖，超出部分裁剪；子物体直接继承相机移动。贴图使用 Point、无压缩、无 mipmap、Full Rect、不使用透明通道。

保留相机已有的 Solid Color 清屏和深色背景颜色。该实现面向当前正交相机及单位父级缩放；修改相机远裁剪面时需同步调整背景深度。

静态核对资源引用、排序和覆盖公式；Unity 编译、运行效果、移动残影与不同窗口比例待用户在 Editor 验收。

## 生成提示词

Use case: stylized-concept. Asset type: opaque 2D camera-follow background for a top-down pixel-art dungeon roguelike, landscape 16:9. Create a subdued dark dungeon abyss backdrop, distant rough cavern rock silhouettes and hints of ruined stone arches fading into charcoal darkness, sparse layers of muted mist. Style: restrained retro pixel art with visible crisp pixel clusters and limited desaturated palette matching 32-pixel dungeon tiles, charcoal black, slate blue gray, dusty dark purple. Extremely low contrast, dark throughout, middle mostly empty shadow; details only gently around outer edges. No walkable floor, no strong perspective corridor, no focal object, no characters, no torches, no bright light sources, no text, no UI, no borders, no watermark. Full rectangular opaque image, not a skybox, not an equirectangular panorama. Intended to remain behind brighter playable map tiles, never competing with gameplay.
