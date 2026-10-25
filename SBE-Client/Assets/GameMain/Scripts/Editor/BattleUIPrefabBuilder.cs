#if UNITY_EDITOR
using System;
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
    public static class BattleUIPrefabBuilder
    {
        private const string PrefabPath = "Assets/GameMain/UI/UIForms/BattleForm.prefab";
        private const string CharacterAtlasPath = "Assets/GameMain/Textures/Characters.png";
        private const string EnemyAtlasPath = "Assets/GameMain/Textures/Monsters.png";
        private const string ItemAtlasPath = "Assets/GameMain/Textures/Items.png";
        private const string PreviewCameraName = "__BattlePreviewCamera";
        private const string PreviewRootName = "__BattlePreview";
        private const string PreviousScenePathKey = "BattleUI.PreviousScenePath";

        private static readonly Color Overlay = new Color32(8, 7, 12, 184);
        private static readonly Color Charcoal = new Color32(25, 24, 30, 255);
        private static readonly Color CharcoalSoft = new Color32(42, 39, 47, 255);
        private static readonly Color Ivory = new Color32(244, 237, 214, 255);
        private static readonly Color Ink = new Color32(43, 37, 35, 255);
        private static readonly Color White = new Color32(248, 248, 246, 255);
        private static readonly Color MutedWhite = new Color32(192, 190, 184, 255);
        private static readonly Color Green = new Color32(62, 164, 94, 255);
        private static readonly Color Blue = new Color32(62, 119, 190, 255);
        private static readonly Color Red = new Color32(191, 69, 65, 255);
        private static readonly Color Gold = new Color32(218, 169, 64, 255);
        private static readonly Color EnemyFrame = new Color32(91, 39, 54, 235);

        private static TMP_FontAsset font;
        private static Sprite panelBeige;
        private static Sprite panelBrown;
        private static Sprite panelBlue;
        private static Sprite buttonBlue;
        private static Sprite buttonBluePressed;
        private static Sprite buttonBrown;
        private static Sprite buttonBrownPressed;
        private static Sprite buttonBeige;
        private static Sprite buttonBeigePressed;
        private static Sprite buttonGrey;
        private static Sprite buttonGreyPressed;
        private static Sprite escapeIcon;
        private static Sprite[] playerIcons;
        private static Sprite[] enemyIcons;
        private static Sprite attackIcon;
        private static Sprite skillIcon;
        private static Sprite itemIcon;

        [MenuItem("Utility/UI/Build Battle UI")]
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
                UIAssetsTools.ApplyGeneratedComponentsToRoot(serializationRoot);

                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save BattleForm prefab.");
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
            Debug.Log("Battle UI prefab rebuilt: " + PrefabPath);
        }

        [MenuItem("Utility/UI/Preview Battle UI")]
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
            previewCamera.backgroundColor = new Color(0.22f, 0.25f, 0.23f, 1f);
            previewCamera.orthographic = true;
            previewCamera.orthographicSize = 5f;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException("Build BattleForm before opening its preview.");
            }

            GameObject previewRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene);
            previewRoot.name = PreviewRootName;
            Canvas canvas = previewRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = previewCamera;
            canvas.planeDistance = 1f;
            previewRoot.GetComponent<RectTransform>().localScale = Vector3.one;
            Selection.activeGameObject = previewRoot;
            Debug.Log("Battle UI preview opened in an unsaved scene.");
        }

        [MenuItem("Utility/UI/Close Battle UI Preview")]
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

        private static GameObject BuildRoot()
        {
            GameObject root = new GameObject("BattleForm", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(UISerializationRoot));
            root.layer = LayerMask.NameToLayer("UI");

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            AddImage(Stretch("BattleOverlay", rootRect, 0f, 0f, 0f, 0f), Overlay, null);
            RectTransform safeFrame = Fixed("SafeFrame", rootRect, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));

            BuildTurnOrder(safeFrame);
            BuildPlayers(safeFrame);
            BuildEnemies(safeFrame);
            BuildActionPanel(safeFrame);
            return root;
        }

        private static void BuildTurnOrder(RectTransform parent)
        {
            RectTransform panel = AddPanel(
                Fixed("TurnOrderPanel", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -24f), new Vector2(1016f, 112f)), panelBrown, Color.white);
            AddImage(TopStretch("ActiveAccent", panel, 0f, 0f, 0f, 6f), Gold, null);

            TextMeshProUGUI round = AddText(
                Fixed("RoundText", panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(28f, 0f), new Vector2(156f, 74f)), "ROUND 01", 25, Ivory,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            RegisterReference(round, "roundText");

            Sprite[] icons =
            {
                playerIcons[3], enemyIcons[2], playerIcons[0], enemyIcons[0],
                playerIcons[2], enemyIcons[1], playerIcons[1], enemyIcons[3]
            };
            string[] labels = { "P4", "E3", "P1", "E1", "P3", "E2", "P2", "E4" };
            for (int i = 0; i < 8; i++)
            {
                RectTransform slotRect = Fixed("TurnSlot" + (i + 1), panel, new Vector2(0f, 0.5f),
                    new Vector2(0f, 0.5f), new Vector2(194f + i * 98f, 0f), new Vector2(82f, 82f));
                Image background = AddImage(slotRect, i == 0 ? Ivory : CharcoalSoft, null);
                background.raycastTarget = false;

                RectTransform marker = Stretch("ActiveMarker", slotRect, -3f, -3f, -3f, -3f);
                Image markerImage = AddImage(marker, Color.clear, null);
                Outline markerOutline = marker.gameObject.AddComponent<Outline>();
                markerOutline.effectColor = Gold;
                markerOutline.effectDistance = new Vector2(3f, -3f);
                markerOutline.useGraphicAlpha = false;
                marker.gameObject.SetActive(i == 0);

                Image icon = AddSprite(Fixed("Icon", slotRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -5f), new Vector2(54f, 54f)), icons[i], Color.white, true);
                TextMeshProUGUI label = AddText(
                    Fixed("Label", slotRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                        new Vector2(0f, 5f), new Vector2(68f, 22f)), labels[i], 15,
                    i == 0 ? Ink : MutedWhite, TextAlignmentOptions.Center, FontStyles.Bold);

                BattleTurnSlotItem slot = slotRect.gameObject.AddComponent<BattleTurnSlotItem>();
                RegisterReference(slot, "turnSlot" + (i + 1));
            }
        }

        private static void BuildPlayers(RectTransform parent)
        {
            int[] hp = { 92, 150, 58, 100 };
            int[] maxHp = { 120, 150, 90, 100 };
            int[] mp = { 24, 10, 72, 50 };
            int[] maxMp = { 40, 30, 80, 50 };

            CreatePlayerCard(parent, 0, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), false,
                hp[0], maxHp[0], mp[0], maxMp[0], false);
            CreatePlayerCard(parent, 1, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -32f), true,
                hp[1], maxHp[1], mp[1], maxMp[1], false);
            CreatePlayerCard(parent, 2, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 28f), false,
                hp[2], maxHp[2], mp[2], maxMp[2], false);
            CreatePlayerCard(parent, 3, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-32f, 28f), true,
                hp[3], maxHp[3], mp[3], maxMp[3], true);
        }

        private static void CreatePlayerCard(RectTransform parent, int index, Vector2 anchor, Vector2 pivot,
            Vector2 position, bool mirror, int hp, int maxHp, int mp, int maxMp, bool active)
        {
            RectTransform cardRect = AddPanel(Fixed("PlayerCard" + (index + 1), parent, anchor, pivot, position,
                new Vector2(400f, 176f)), panelBeige, Color.white);

            RectTransform marker = TopStretch("ActiveMarker", cardRect, 0f, 0f, 0f, 7f);
            AddImage(marker, Gold, null);
            marker.gameObject.SetActive(active);

            float iconX = mirror ? 278f : 18f;
            float contentX = mirror ? 18f : 140f;
            RectTransform iconFrame = Fixed("IconFrame", cardRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(iconX, -18f), new Vector2(104f, 140f));
            AddImage(iconFrame, Charcoal, null);
            Image icon = AddSprite(Fixed("Icon", iconFrame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -8f), new Vector2(88f, 96f)), playerIcons[index], Color.white, true);
            AddText(Fixed("PartyIndex", iconFrame, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 7f), new Vector2(84f, 24f)), "P" + (index + 1), 14, MutedWhite,
                TextAlignmentOptions.Center, FontStyles.Bold);

            TextMeshProUGUI name = AddText(
                Fixed("CharacterName", cardRect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(contentX, -14f), new Vector2(242f, 38f)), "角色 " + (index + 1), 24, Ink,
                mirror ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft, FontStyles.Bold);
            Image hpFill = CreateBar(cardRect, "HP", contentX, 64f, 242f, Green, hp, maxHp, out TextMeshProUGUI hpText);
            Image mpFill = CreateBar(cardRect, "MP", contentX, 112f, 242f, Blue, mp, maxMp, out TextMeshProUGUI mpText);

            BattleActorCardItem card = cardRect.gameObject.AddComponent<BattleActorCardItem>();
            RegisterReference(card, "playerCard" + (index + 1));
        }

        private static void BuildEnemies(RectTransform parent)
        {
            RectTransform enemyRoot = Fixed("EnemyRoot", parent, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0f, 32f), new Vector2(820f, 290f));
            int[] hp = { 36, 80, 44, 61 };
            int[] maxHp = { 50, 80, 60, 80 };

            for (int i = 0; i < 4; i++)
            {
                RectTransform slotRect = Fixed("EnemySlot" + (i + 1), enemyRoot, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(-300f + i * 200f, 0f), new Vector2(170f, 248f));
                Image targetGraphic = AddImage(slotRect, new Color(EnemyFrame.r, EnemyFrame.g, EnemyFrame.b, 0.68f), null);
                targetGraphic.raycastTarget = true;
                Button targetButton = slotRect.gameObject.AddComponent<Button>();
                targetButton.targetGraphic = targetGraphic;
                targetButton.transition = Selectable.Transition.ColorTint;

                RectTransform selected = Stretch("SelectedMarker", slotRect, -4f, -4f, -4f, -4f);
                AddImage(selected, Color.clear, null);
                Outline selectedOutline = selected.gameObject.AddComponent<Outline>();
                selectedOutline.effectColor = Gold;
                selectedOutline.effectDistance = new Vector2(4f, -4f);
                selectedOutline.useGraphicAlpha = false;
                selected.gameObject.SetActive(i == 1);

                Image icon = AddSprite(Fixed("Icon", slotRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -12f), new Vector2(142f, 146f)), enemyIcons[i], Color.white, true);
                TextMeshProUGUI name = AddText(
                    Fixed("EnemyName", slotRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                        new Vector2(0f, 55f), new Vector2(154f, 30f)), "敌人 " + (i + 1), 18, White,
                    TextAlignmentOptions.Center, FontStyles.Bold);
                Image hpFill = CreateEnemyBar(slotRect, hp[i], maxHp[i], out TextMeshProUGUI hpText);

                BattleEnemySlotItem slot = slotRect.gameObject.AddComponent<BattleEnemySlotItem>();
                RegisterReference(slot, "enemySlot" + (i + 1));
            }
        }

        private static void BuildActionPanel(RectTransform parent)
        {
            RectTransform panel = AddPanel(
                Fixed("ActionPanel", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                    new Vector2(0f, 28f), new Vector2(920f, 176f)), panelBlue, Color.white);
            AddImage(TopStretch("CurrentAccent", panel, 0f, 0f, 0f, 7f), Gold, null);

            TextMeshProUGUI currentActor = AddText(
                Fixed("CurrentActorText", panel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(28f, -15f), new Vector2(500f, 38f)), "角色 4", 24, White,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            RegisterReference(currentActor, "currentActorText");
            AddText(Fixed("TurnState", panel, new Vector2(1f, 1f), new Vector2(1f, 1f),
                    new Vector2(-28f, -17f), new Vector2(260f, 34f)), "YOUR MOVE", 15, Gold,
                TextAlignmentOptions.MidlineRight, FontStyles.Bold);

            Button attack = CreateActionButton(panel, "AttackButton", "攻击", attackIcon, 25f, ButtonStyle.Brown);
            Button skill = CreateActionButton(panel, "SkillButton", "技能", skillIcon, 243f, ButtonStyle.Blue);
            Button item = CreateActionButton(panel, "ItemButton", "道具", itemIcon, 461f, ButtonStyle.Grey);
            Button escape = CreateActionButton(panel, "EscapeButton", "逃跑", escapeIcon, 679f, ButtonStyle.Beige);
            item.interactable = false;

            RegisterReference(attack, "attackButton");
            RegisterReference(skill, "skillButton");
            RegisterReference(item, "itemButton");
            RegisterReference(escape, "escapeButton");
        }

        private static Button CreateActionButton(RectTransform parent, string name, string label, Sprite icon,
            float x, ButtonStyle style)
        {
            RectTransform rect = Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -66f), new Vector2(216f, 82f));
            Sprite normal;
            Sprite pressed;
            Color textColor;
            switch (style)
            {
                case ButtonStyle.Blue:
                    normal = buttonBlue;
                    pressed = buttonBluePressed;
                    textColor = White;
                    break;
                case ButtonStyle.Brown:
                    normal = buttonBrown;
                    pressed = buttonBrownPressed;
                    textColor = White;
                    break;
                case ButtonStyle.Beige:
                    normal = buttonBeige;
                    pressed = buttonBeigePressed;
                    textColor = Ink;
                    break;
                default:
                    normal = buttonGrey;
                    pressed = buttonGreyPressed;
                    textColor = Ink;
                    break;
            }

            Image background = AddImage(rect, Color.white, normal);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState state = button.spriteState;
            state.highlightedSprite = normal;
            state.selectedSprite = normal;
            state.pressedSprite = pressed;
            state.disabledSprite = pressed;
            button.spriteState = state;

            Color iconColor = style == ButtonStyle.Beige ? Ink : Color.white;
            AddSprite(Fixed("Icon", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24f, 0f), new Vector2(46f, 46f)), icon, iconColor, true);
            AddText(Fixed("Label", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(80f, 0f), new Vector2(112f, 50f)), label, 25, textColor,
                TextAlignmentOptions.Center, FontStyles.Bold);
            return button;
        }

        private static Image CreateBar(RectTransform parent, string label, float x, float top, float width,
            Color fillColor, int current, int maximum, out TextMeshProUGUI valueText)
        {
            RectTransform bar = Fixed(label + "Bar", parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, -top), new Vector2(width, 32f));
            AddImage(bar, Charcoal, null);
            RectTransform fillRect = Stretch("Fill", bar, 3f, 3f, 3f, 3f);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01((float)current / maximum), 1f);
            fillRect.offsetMax = new Vector2(0f, -3f);
            Image fill = AddImage(fillRect, fillColor, null);
            valueText = AddText(Stretch("Value", bar, 10f, 10f, 0f, 0f),
                label + "  " + current + " / " + maximum, 15, White, TextAlignmentOptions.Center, FontStyles.Bold);
            return fill;
        }

        private static Image CreateEnemyBar(RectTransform parent, int current, int maximum,
            out TextMeshProUGUI valueText)
        {
            RectTransform bar = Fixed("HPBar", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 17f), new Vector2(150f, 26f));
            AddImage(bar, Charcoal, null);
            RectTransform fillRect = Stretch("Fill", bar, 3f, 3f, 3f, 3f);
            fillRect.anchorMax = new Vector2(Mathf.Clamp01((float)current / maximum), 1f);
            fillRect.offsetMax = new Vector2(0f, -3f);
            Image fill = AddImage(fillRect, Red, null);
            valueText = AddText(Stretch("Value", bar, 6f, 6f, 0f, 0f), current + " / " + maximum, 13, White,
                TextAlignmentOptions.Center, FontStyles.Bold);
            return fill;
        }

        private static void LoadAssets()
        {
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/GameMain/Fonts/MainTMPFont.asset");
            panelBeige = LoadSprite("Assets/GameMain/Textures/UI/panel_beige.png");
            panelBrown = LoadSprite("Assets/GameMain/Textures/UI/panel_brown.png");
            panelBlue = LoadSprite("Assets/GameMain/Textures/UI/panel_blue.png");
            buttonBlue = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_blue.png");
            buttonBluePressed = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_blue_pressed.png");
            buttonBrown = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_brown.png");
            buttonBrownPressed = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_brown_pressed.png");
            buttonBeige = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_beige.png");
            buttonBeigePressed = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_beige_pressed.png");
            buttonGrey = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_grey.png");
            buttonGreyPressed = LoadSprite("Assets/GameMain/Textures/UI/buttonLong_grey_pressed.png");
            escapeIcon = LoadSprite("Assets/GameMain/Textures/UI/arrowSilver_left.png");
            playerIcons = Enumerable.Range(0, 4).Select(i => LoadSprite(CharacterAtlasPath, "rogues_" + i)).ToArray();
            int[] enemyIndexes = { 0, 1, 2, 3 };
            enemyIcons = enemyIndexes.Select(i => LoadSprite(EnemyAtlasPath, "monsters_" + i)).ToArray();
            attackIcon = LoadSprite(ItemAtlasPath, "items_0");
            skillIcon = LoadSprite(ItemAtlasPath, "items_110");
            itemIcon = LoadSprite(ItemAtlasPath, "items_220");

            if (font == null || playerIcons.Any(sprite => sprite == null) || enemyIcons.Any(sprite => sprite == null))
            {
                throw new InvalidOperationException("Battle UI assets are incomplete.");
            }
        }

        private static Sprite LoadSprite(string path)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite LoadSprite(string path, string name)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(sprite => sprite.name == name);
        }

        private static RectTransform AddPanel(RectTransform rect, Sprite sprite, Color color)
        {
            Image image = AddImage(rect, color, sprite);
            image.type = Image.Type.Sliced;
            return rect;
        }

        private static Image AddImage(RectTransform rect, Color color, Sprite sprite)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static Image AddSprite(RectTransform rect, Sprite sprite, Color color, bool preserveAspect)
        {
            Image image = AddImage(rect, color, sprite);
            image.preserveAspect = preserveAspect;
            return image;
        }

        private static TextMeshProUGUI AddText(RectTransform rect, string value, float fontSize, Color color,
            TextAlignmentOptions alignment, FontStyles style)
        {
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
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

        private static RectTransform Stretch(string name, Transform parent, float left, float right, float bottom,
            float top)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform TopStretch(string name, Transform parent, float left, float right, float top,
            float height)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
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

        private enum ButtonStyle
        {
            Blue,
            Brown,
            Beige,
            Grey
        }
    }
}
#endif