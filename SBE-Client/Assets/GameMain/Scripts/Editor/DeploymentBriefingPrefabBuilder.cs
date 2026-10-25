#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using SepCore.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SepCore.Editor
{
    /// <summary>
    /// 战术部署与战局横幅（DeploymentBriefingForm）轻量化预制体构建器。
    /// 设计分辨率：2868 x 1320。
    /// 视觉表现：半透明横幅过场，突出展示地图名称与威胁等级，快节奏转场后立即交出控制权。
    /// </summary>
    public static class DeploymentBriefingPrefabBuilder
    {
        public const string PrefabPath = "Assets/GameMain/UI/UIForms/DeploymentBriefingForm.prefab";
        private const string FontPath = "Assets/GameMain/Fonts/MainTMPFont.asset";

        private static readonly Color BannerBg = new Color(0.04f, 0.06f, 0.08f, 0.85f);
        private static readonly Color GoldAccent = new Color32(214, 169, 61, 255);
        private static readonly Color White = new Color32(245, 247, 248, 255);
        private static readonly Color MutedWhite = new Color32(170, 182, 190, 255);

        private const string PreviewCameraName = "__BriefingPreviewCamera";
        private const string PreviewRootName = "__BriefingPreview";
        private const string PreviousScenePathKey = "BriefingUI.PreviousScenePath";

        private static TMP_FontAsset s_font;

        [MenuItem("Utility/UI/Build Deployment Briefing UI")]
        public static void Build()
        {
            LoadAssets();

            Scene previousScene = SceneManager.GetActiveScene();
            Scene temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;

            try
            {
                SceneManager.SetActiveScene(temporaryScene);
                root = BuildRoot();

                UISerializationRoot serializationRoot = root.GetComponent<UISerializationRoot>();
                serializationRoot.RefreshItems();

                // 自动生成 View/Form 脚本
                UIAssetsTools.Generate(serializationRoot, false);
                UIAssetsTools.ApplyGeneratedComponentsToRoot(serializationRoot);

                string directory = Path.GetDirectoryName(PrefabPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save DeploymentBriefingForm prefab.");
                }
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (previousScene.IsValid())
                {
                    SceneManager.SetActiveScene(previousScene);
                }

                EditorSceneManager.CloseScene(temporaryScene, true);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("[DeploymentBriefingPrefabBuilder] Rebuilt lightweight banner prefab successfully at: " + PrefabPath);
        }

        [MenuItem("Utility/UI/Preview Deployment Briefing UI")]
        public static void OpenPreview()
        {
            ClosePreviewInternal();

            Scene previousScene = SceneManager.GetActiveScene();
            SessionState.SetString(PreviousScenePathKey, previousScene.path);
            Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(previewScene);

            GameObject cameraObject = new GameObject(PreviewCameraName, typeof(Camera));
            Camera previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.08f, 0.11f, 0.14f, 1f);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("Build DeploymentBriefingForm before opening its preview.");
            }

            GameObject previewRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
            previewRoot.name = PreviewRootName;
            Canvas canvas = previewRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = previewCamera;
            canvas.planeDistance = 1f;
            previewRoot.GetComponent<RectTransform>().localScale = Vector3.one;
            Selection.activeGameObject = previewRoot;
            Debug.Log("Deployment Briefing UI preview opened in an unsaved scene.");
        }

        [MenuItem("Utility/UI/Close Deployment Briefing UI Preview")]
        public static void ClosePreview()
        {
            ClosePreviewInternal();
        }

        private static void ClosePreviewInternal()
        {
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            Camera previewCamera = cameras.FirstOrDefault(candidate => candidate != null &&
                candidate.gameObject.name == PreviewCameraName && candidate.gameObject.scene.IsValid());
            if (previewCamera == null)
            {
                return;
            }

            Scene previewScene = previewCamera.gameObject.scene;
            string previousPath = SessionState.GetString(PreviousScenePathKey, string.Empty);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loadedScene = SceneManager.GetSceneAt(i);
                if (loadedScene.IsValid() && loadedScene.path == previousPath)
                {
                    SceneManager.SetActiveScene(loadedScene);
                    break;
                }
            }

            EditorSceneManager.CloseScene(previewScene, true);
            SessionState.EraseString(PreviousScenePathKey);
        }

        private static void LoadAssets()
        {
            s_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (s_font == null)
            {
                s_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            }

            if (s_font == null)
            {
                throw new InvalidOperationException("Main TMP Font asset is missing.");
            }
        }

        private static GameObject BuildRoot()
        {
            GameObject root = new GameObject("DeploymentBriefingForm",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup),
                typeof(UISerializationRoot));

            root.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 950;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(2868f, 1320f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            CanvasGroup rootCanvasGroup = root.GetComponent<CanvasGroup>();
            rootCanvasGroup.alpha = 1f;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;
            RegisterReference(rootCanvasGroup, "canvasGroup");

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            // 1. 全屏淡黑遮罩（用于场景加载瞬间的平滑暗转，透明度可独立控制）
            RectTransform maskRect = Stretch("BlackMask", rootRect, 0f, 0f, 0f, 0f);
            Image maskImage = AddImage(maskRect, Color.black, null);
            maskImage.raycastTarget = false;
            CanvasGroup maskCanvasGroup = maskRect.gameObject.AddComponent<CanvasGroup>();
            maskCanvasGroup.alpha = 0f;
            RegisterReference(maskCanvasGroup, "maskCanvasGroup");

            // 2. 核心半透明过场横幅（位于屏幕水平全通，上下带战术金边，微靠上方避开角色）
            BuildBanner(rootRect);

            return root;
        }

        private static void BuildBanner(RectTransform rootRect)
        {
            // 横幅容器：高度 210，垂直稍微偏上 (+80) 留出角色视野
            RectTransform bannerContainer = Fixed("BannerContainer", rootRect,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 80f), new Vector2(2868f, 210f));
            bannerContainer.anchorMin = new Vector2(0f, 0.5f);
            bannerContainer.anchorMax = new Vector2(1f, 0.5f);
            bannerContainer.sizeDelta = new Vector2(0f, 210f);

            CanvasGroup bannerCanvasGroup = bannerContainer.gameObject.AddComponent<CanvasGroup>();
            bannerCanvasGroup.alpha = 1f;
            RegisterReference(bannerCanvasGroup, "bannerCanvasGroup");

            // 半透明底板
            Image bannerBg = AddImage(Stretch("BannerBackground", bannerContainer, 0f, 0f, 0f, 0f), BannerBg, null);
            bannerBg.raycastTarget = false;

            // 上下细金边
            AddImage(TopStretch("TopRule", bannerContainer, 0f, 0f, 0f, 2.5f), GoldAccent, null);
            AddImage(BottomStretch("BottomRule", bannerContainer, 0f, 0f, 0f, 2.5f), GoldAccent, null);

            // 居中内容区域 (宽 2400)
            RectTransform content = Fixed("Content", bannerContainer,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(2400f, 190f));

            // 行 1：难度与战术标签 (Gold / Cyan)
            TextMeshProUGUI threatTierText = AddText(Fixed("ThreatTierText", content,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 44f), new Vector2(2200f, 36f)),
                "// INFILTRATION PROTOCOL  •  THREAT LEVEL: TIER I", 20, GoldAccent, TextAlignmentOptions.Center, FontStyles.Bold);
            threatTierText.characterSpacing = 3f;
            RegisterReference(threatTierText, "threatTierText");

            // 行 2：主地图名称
            TextMeshProUGUI mapNameText = AddText(Fixed("MapNameText", content,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -4f), new Vector2(2200f, 58f)),
                "SECTOR 04 — ABANDONED DEPOT", 42, White, TextAlignmentOptions.Center, FontStyles.Bold);
            mapNameText.characterSpacing = 2f;
            RegisterReference(mapNameText, "mapNameText");

            // 行 3：迷雾环境与指令简述
            TextMeshProUGUI subInfoText = AddText(Fixed("SubInfoText", content,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, -48f), new Vector2(2200f, 30f)),
                "TACTICAL SCAN: DENSE FOG DETECTED  •  OBJECTIVE: SCAVENGE & EVACUATE", 16, MutedWhite, TextAlignmentOptions.Center, FontStyles.Normal);
            subInfoText.characterSpacing = 1.5f;
            RegisterReference(subInfoText, "subInfoText");
        }

        #region Helpers

        private static Image AddImage(RectTransform rect, Color color, Sprite sprite)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI AddText(RectTransform rect, string value, float fontSize, Color color,
            TextAlignmentOptions alignment, FontStyles style)
        {
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = s_font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.layer = LayerMask.NameToLayer("UI");
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Fixed(string name, Transform parent, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Stretch(string name, Transform parent, float left, float right, float bottom, float top)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform TopStretch(string name, Transform parent, float left, float right, float top, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform BottomStretch(string name, Transform parent, float left, float right, float bottom, float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, bottom + height);
            return rect;
        }

        private static void RegisterReference(Component component, string variableName)
        {
            UISerializationItem item = component.GetComponent<UISerializationItem>();
            if (item == null)
            {
                item = component.gameObject.AddComponent<UISerializationItem>();
            }

            item.RefreshComponents();
            item.SetReference(component, true, variableName);
        }

        #endregion
    }
}
#endif
