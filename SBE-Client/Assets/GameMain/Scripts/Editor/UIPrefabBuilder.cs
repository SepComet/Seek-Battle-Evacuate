using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SepCore.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SepCore.Editor
{
    public static class UIPrefabBuilder
    {
        private const string RawUiPath = "Assets/RawResources/kenney_ui-pack-rpg-expansion/PNG";
        private const string UiSpritePath = "Assets/GameMain/Texture/Sprites";
        private const string PrefabPath = "Assets/GameMain/UI";
        private const string TerrainPath = "Assets/GameMain/Texture/Terrian.png";

        private static readonly Color Backdrop = new Color32(17, 24, 32, 255);
        private static readonly Color BackdropRaised = new Color32(24, 34, 44, 255);
        private static readonly Color BackdropSoft = new Color32(34, 48, 58, 255);
        private static readonly Color Ink = new Color32(46, 42, 37, 255);
        private static readonly Color MutedInk = new Color32(111, 101, 90, 255);
        private static readonly Color Paper = new Color32(242, 231, 201, 255);
        private static readonly Color PaperMuted = new Color32(214, 194, 142, 255);
        private static readonly Color White = new Color32(245, 247, 248, 255);
        private static readonly Color MutedWhite = new Color32(170, 182, 190, 255);
        private static readonly Color Blue = new Color32(52, 127, 168, 255);
        private static readonly Color Green = new Color32(93, 155, 97, 255);
        private static readonly Color Red = new Color32(185, 87, 79, 255);
        private static readonly Color Gold = new Color32(214, 169, 61, 255);

        private static Font uiFont;
        private static Sprite panelBrown;
        private static Sprite panelBeige;
        private static Sprite panelBeigeLight;
        private static Sprite panelBlue;
        private static Sprite panelInsetBrown;
        private static Sprite panelInsetLight;
        private static Sprite buttonBlue;
        private static Sprite buttonBluePressed;
        private static Sprite buttonBrown;
        private static Sprite buttonBrownPressed;
        private static Sprite buttonGrey;
        private static Sprite buttonGreyPressed;
        private static Sprite buttonBeige;
        private static Sprite buttonBeigePressed;
        private static Sprite buttonSquareGrey;
        private static Sprite buttonSquareGreyPressed;
        private static Sprite buttonSquareBlue;
        private static Sprite buttonSquareBluePressed;
        private static Sprite iconCheck;
        private static Sprite iconClose;
        private static Sprite iconBack;
        private static Dictionary<int, Sprite> terrainSprites;

        [MenuItem("Utility/UI/Build UI Prefabs")]
        public static void BuildAll()
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (uiFont == null)
            {
                throw new InvalidOperationException("Unity built-in UI font is unavailable.");
            }

            EnsureFolder(UiSpritePath);
            EnsureFolder(PrefabPath);
            PrepareSprites();

            BuildPrefab("LobbyForm", BuildLobby);
            BuildPrefab("MainWarehouseForm", BuildWarehouse);
            BuildPrefab("RunBackpackForm", BuildRunBackpack);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath + "/LobbyForm.prefab");
            Debug.Log("UI prefabs rebuilt: LobbyForm, MainWarehouseForm, RunBackpackForm.");
        }

        private static void PrepareSprites()
        {
            panelBrown = PrepareSprite("panel_brown.png", new Vector4(12f, 12f, 12f, 12f));
            panelBeige = PrepareSprite("panel_beige.png", new Vector4(12f, 12f, 12f, 12f));
            panelBeigeLight = PrepareSprite("panel_beigeLight.png", new Vector4(12f, 12f, 12f, 12f));
            panelBlue = PrepareSprite("panel_blue.png", new Vector4(12f, 12f, 12f, 12f));
            panelInsetBrown = PrepareSprite("panelInset_brown.png", new Vector4(11f, 11f, 11f, 11f));
            panelInsetLight = PrepareSprite("panelInset_beigeLight.png", new Vector4(11f, 11f, 11f, 11f));
            buttonBlue = PrepareSprite("buttonLong_blue.png", new Vector4(15f, 12f, 15f, 12f));
            buttonBluePressed = PrepareSprite("buttonLong_blue_pressed.png", new Vector4(15f, 12f, 15f, 12f));
            buttonBrown = PrepareSprite("buttonLong_brown.png", new Vector4(15f, 12f, 15f, 12f));
            buttonBrownPressed = PrepareSprite("buttonLong_brown_pressed.png", new Vector4(15f, 12f, 15f, 12f));
            buttonGrey = PrepareSprite("buttonLong_grey.png", new Vector4(15f, 12f, 15f, 12f));
            buttonGreyPressed = PrepareSprite("buttonLong_grey_pressed.png", new Vector4(15f, 12f, 15f, 12f));
            buttonBeige = PrepareSprite("buttonLong_beige.png", new Vector4(15f, 12f, 15f, 12f));
            buttonBeigePressed = PrepareSprite("buttonLong_beige_pressed.png", new Vector4(15f, 12f, 15f, 12f));
            buttonSquareGrey = PrepareSprite("buttonSquare_grey.png", new Vector4(10f, 10f, 10f, 10f));
            buttonSquareGreyPressed = PrepareSprite("buttonSquare_grey_pressed.png", new Vector4(10f, 10f, 10f, 10f));
            buttonSquareBlue = PrepareSprite("buttonSquare_blue.png", new Vector4(10f, 10f, 10f, 10f));
            buttonSquareBluePressed = PrepareSprite("buttonSquare_blue_pressed.png", new Vector4(10f, 10f, 10f, 10f));
            iconCheck = PrepareSprite("iconCheck_blue.png", Vector4.zero);
            iconClose = PrepareSprite("iconCross_grey.png", Vector4.zero);
            iconBack = PrepareSprite("arrowSilver_left.png", Vector4.zero);

            terrainSprites = AssetDatabase.LoadAllAssetsAtPath(TerrainPath)
                .OfType<Sprite>()
                .Where(sprite => sprite.name.StartsWith("Terrian_", StringComparison.Ordinal))
                .ToDictionary(sprite => ParseSuffix(sprite.name), sprite => sprite);
        }

        private static void BuildPrefab(string name, Func<GameObject> factory)
        {
            Scene previousScene = SceneManager.GetActiveScene();
            Scene temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            GameObject root = null;

            try
            {
                SceneManager.SetActiveScene(temporaryScene);
                root = factory();
                root.GetComponent<UISerializationRoot>().RefreshItems();
                UIAssetsTools.ApplyGeneratedComponentsToRoot(root.GetComponent<UISerializationRoot>());
                string path = PrefabPath + "/" + name + ".prefab";
                bool success;
                PrefabUtility.SaveAsPrefabAsset(root, path, out success);
                if (!success)
                {
                    throw new InvalidOperationException("Failed to save prefab at " + path + ".");
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
        }

        private static GameObject BuildLobby()
        {
            GameObject root = CreateCanvas("LobbyForm", 0);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            AddImage(Stretch("Backdrop", rootRect, 0f, 0f, 0f, 0f), Backdrop, null);
            RectTransform safeFrame = Fixed("SafeFrame", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1920f, 1080f));
            AddShell(safeFrame, "HOME");

            AddText(
                Fixed("ScreenTitle", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(344f, -144f),
                    new Vector2(850f, 58f)),
                "PREPARE THE RUN", 34, White, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("ScreenSubtitle", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(344f, -194f),
                    new Vector2(880f, 34f)),
                "Squad, threat tier and deterministic run seed", 17, MutedWhite, TextAnchor.MiddleLeft,
                FontStyle.Normal);

            RectTransform squadPanel =
                Panel(
                    Fixed("SquadPanel", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(336f, -248f),
                        new Vector2(960f, 690f)), panelBrown, Color.white);
            AddSectionHeading(squadPanel, "SQUAD", "4 / 4 SELECTED", Green);

            int[] portraits = { 84, 85, 86, 87 };
            int[] hp = { 120, 150, 90, 100 };
            int[] mp = { 40, 30, 80, 50 };
            int[] speed = { 12, 8, 10, 16 };
            for (int i = 0; i < 4; i++)
            {
                float x = 34f + (i % 2) * 448f;
                float y = 102f + (i / 2) * 270f;
                CreateCharacterCard(squadPanel, i + 1, x, y, hp[i], mp[i], speed[i], Terrain(portraits[i]));
            }

            RectTransform deploymentPanel =
                Panel(
                    Fixed("DeploymentPanel", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(1328f, -248f), new Vector2(528f, 690f)), panelBeige, Color.white);
            AddText(
                Fixed("Title", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -28f),
                    new Vector2(460f, 42f)),
                "DEPLOYMENT", 25, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("DifficultyLabel", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(34f, -98f), new Vector2(460f, 26f)),
                "THREAT TIER", 15, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            CreateSegmentedToggles(deploymentPanel, "DifficultyTabs", new[] { "TIER I", "TIER II", "TIER III" }, 34f,
                134f, 460f, 62f, 0);

            AddText(
                Fixed("SeedLabel", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -230f),
                    new Vector2(460f, 26f)),
                "RUN SEED", 15, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            InputField seedInput = CreateInputField(deploymentPanel, "SeedInput", "240829", "ENTER SEED", 34f, 266f,
                302f, 64f, true);
            RegisterReference(seedInput, "seedInput");
            Button randomizeButton = CreateButton(
                Fixed("RandomizeButton", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(350f, -266f), new Vector2(144f, 64f)),
                "RANDOMIZE", ButtonKind.Brown, 15);
            RegisterReference(randomizeButton, "randomizeButton");

            RectTransform runSummary =
                Panel(
                    Fixed("RunSummary", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(34f, -362f), new Vector2(460f, 174f)), panelInsetLight, Color.white);
            CreateStatLine(runSummary, "PartySize", "PARTY", "4", 24f, 24f, Green);
            CreateStatLine(runSummary, "MapCount", "MAP", "01", 24f, 72f, Blue);
            CreateStatLine(runSummary, "TimeLimit", "TIME LIMIT", "25:00", 24f, 120f, Red);

            Button beginRunButton = CreateButton(
                Fixed("BeginRunButton", deploymentPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(34f, -574f), new Vector2(460f, 82f)),
                "BEGIN RUN", ButtonKind.Blue, 24);
            RegisterReference(beginRunButton, "beginRunButton");

            RectTransform footer =
                Panel(
                    Fixed("FooterStatus", safeFrame, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(336f, 38f),
                        new Vector2(1520f, 62f)), panelInsetBrown, Color.white);
            AddText(
                Fixed("Storage", footer, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f),
                    new Vector2(360f, 40f)),
                "MAIN WAREHOUSE   0 ITEMS", 16, Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("SeedStatus", footer, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f),
                    new Vector2(440f, 40f)),
                "READY FOR DEPLOYMENT", 15, Green, TextAnchor.MiddleRight, FontStyle.Bold);
            return root;
        }

        private static GameObject BuildWarehouse()
        {
            GameObject root = CreateCanvas("MainWarehouseForm", 10);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            AddImage(Stretch("Backdrop", rootRect, 0f, 0f, 0f, 0f), Backdrop, null);
            RectTransform safeFrame = Fixed("SafeFrame", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(1920f, 1080f));
            AddShell(safeFrame, "WAREHOUSE");

            AddText(
                Fixed("ScreenTitle", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(344f, -144f),
                    new Vector2(620f, 58f)),
                "MAIN WAREHOUSE", 34, White, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Capacity", safeFrame, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-64f, -158f),
                    new Vector2(360f, 38f)),
                "UNLIMITED STORAGE", 16, MutedWhite, TextAnchor.MiddleRight, FontStyle.Bold);

            RectTransform inventoryPanel =
                Panel(
                    Fixed("InventoryPanel", safeFrame, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(336f, -220f), new Vector2(1080f, 800f)), panelBrown, Color.white);
            CreateSegmentedToggles(inventoryPanel, "CategoryTabs", new[] { "ALL", "LOOT", "EQUIPMENT", "CONSUMABLES" },
                30f, 26f, 650f, 58f, 0);
            InputField searchInput = CreateInputField(inventoryPanel, "SearchInput", string.Empty, "SEARCH ITEMS", 706f,
                26f, 260f, 58f, false);
            RegisterReference(searchInput, "searchInput");
            Button sortButton = CreateButton(
                Fixed("SortButton", inventoryPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(980f, -26f),
                    new Vector2(70f, 58f)),
                "A-Z", ButtonKind.Grey, 15);
            RegisterReference(sortButton, "sortButton");

            RectTransform scrollRoot =
                Panel(
                    Fixed("ItemScroll", inventoryPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(30f, -112f), new Vector2(1020f, 620f)), panelInsetBrown, Color.white);
            ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            RegisterReference(scroll, "itemScroll");
            RectTransform viewport = Stretch("Viewport", scrollRoot, 20f, 24f, 22f, 20f);
            Image viewportImage = AddImage(viewport, new Color(1f, 1f, 1f, 0.001f), null);
            viewportImage.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = TopStretch("Content", viewport, 0f, 12f, 0f, 610f);
            content.pivot = new Vector2(0.5f, 1f);
            GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(104f, 104f);
            grid.spacing = new Vector2(16f, 16f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 8;

            int[] icons = { 101, 102, 117, 123, 124, 104, 130, 118, 129, 115 };
            Color[] rarities = { White, Green, Blue, Gold, Red, White, Blue, Green, Gold, White };
            string[] quantities = { "28", "7", "3", "1", "1", "1", "1", "1", "1", "4" };
            for (int i = 0; i < 40; i++)
            {
                bool populated = i < icons.Length;
                CreateItemSlot(content, "Slot_" + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    populated ? Terrain(icons[i]) : null,
                    populated ? rarities[i] : MutedInk,
                    populated ? quantities[i] : string.Empty,
                    populated && i == 5);
            }

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.verticalNormalizedPosition = 1f;

            AddText(
                Fixed("ItemCount", inventoryPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(34f, 25f),
                    new Vector2(400f, 34f)),
                "10 ITEMS   /   48 TOTAL UNITS", 15, PaperMuted, TextAnchor.MiddleLeft, FontStyle.Bold);

            RectTransform details =
                Panel(
                    Fixed("ItemDetailsPanel", safeFrame, new Vector2(1f, 1f), new Vector2(1f, 1f),
                        new Vector2(-64f, -220f), new Vector2(408f, 800f)), panelBeige, Color.white);
            AddText(
                Fixed("Heading", details, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -26f),
                    new Vector2(348f, 40f)),
                "SELECTED ITEM", 20, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform iconPanel =
                Panel(
                    Fixed("IconPanel", details, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f),
                        new Vector2(184f, 184f)), panelInsetLight, Color.white);
            AddSprite(
                Fixed("Icon", iconPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(112f, 112f)), Terrain(104), Color.white, true);
            AddText(
                Fixed("Name", details, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -300f),
                    new Vector2(348f, 42f)),
                "TRAINING SWORD", 24, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            AddText(
                Fixed("Type", details, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -344f),
                    new Vector2(348f, 30f)),
                "WEAPON  /  COMMON", 15, MutedInk, TextAnchor.MiddleCenter, FontStyle.Bold);

            RectTransform stats =
                Panel(
                    Fixed("Stats", details, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -404f),
                        new Vector2(352f, 176f)), panelInsetLight, Color.white);
            CreateStatLine(stats, "Attack", "ATK", "+5", 22f, 22f, Red);
            CreateStatLine(stats, "Stack", "STACK LIMIT", "1", 22f, 72f, Blue);
            CreateStatLine(stats, "ItemId", "ITEM ID", "5101", 22f, 122f, MutedInk);

            AddText(
                Fixed("Description", details, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -608f),
                    new Vector2(344f, 70f)),
                "A simple weapon prepared for the first expedition.", 16, MutedInk, TextAnchor.UpperLeft,
                FontStyle.Normal);
            Button openLoadoutButton = CreateButton(
                Fixed("OpenLoadoutButton", details, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 30f),
                    new Vector2(348f, 68f)),
                "OPEN LOADOUT", ButtonKind.Blue, 19);
            RegisterReference(openLoadoutButton, "openLoadoutButton");
            return root;
        }

        private static GameObject BuildRunBackpack()
        {
            GameObject root = CreateCanvas("RunBackpackForm", 100);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            AddImage(Stretch("WorldScrim", rootRect, 0f, 0f, 0f, 0f), new Color(0.035f, 0.055f, 0.07f, 0.88f), null);

            RectTransform window =
                Panel(
                    Fixed("BackpackWindow", rootRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                        new Vector2(1700f, 910f)), panelBrown, Color.white);
            RectTransform topBar = Panel(TopStretch("TopBar", window, 24f, 24f, 22f, 86f), panelInsetBrown,
                Color.white);
            AddText(
                Fixed("Title", topBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f),
                    new Vector2(420f, 54f)),
                "RUN BACKPACK", 28, Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Seed", topBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(90f, 0f),
                    new Vector2(330f, 46f)),
                "SEED  240829", 16, PaperMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            AddText(
                Fixed("Timer", topBar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-106f, 0f),
                    new Vector2(190f, 54f)),
                "18:42", 30, Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            Button closeButton =
                CreateIconButton(
                    Fixed("CloseButton", topBar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 0f),
                        new Vector2(58f, 58f)), iconClose, false);
            RegisterReference(closeButton, "closeButton");

            RectTransform backpackPanel =
                Panel(
                    Fixed("BackpackPanel", window, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -132f),
                        new Vector2(1080f, 748f)), panelBeige, Color.white);
            AddText(
                Fixed("Heading", backpackPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -24f),
                    new Vector2(420f, 40f)),
                "SHARED BACKPACK", 23, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Capacity", backpackPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-30f, -26f),
                    new Vector2(250f, 38f)),
                "12 / 30", 20, Blue, TextAnchor.MiddleRight, FontStyle.Bold);

            RectTransform bagGrid = Fixed("BackpackGrid", backpackPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -84f), new Vector2(1020f, 510f));
            GridLayoutGroup bagLayout = bagGrid.gameObject.AddComponent<GridLayoutGroup>();
            bagLayout.cellSize = new Vector2(150f, 94f);
            bagLayout.spacing = new Vector2(18f, 10f);
            bagLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            bagLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            bagLayout.childAlignment = TextAnchor.UpperLeft;
            bagLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            bagLayout.constraintCount = 6;

            int[] bagIcons = { 101, 101, 102, 117, 123, 124, 104, 130, 118, 129, 115, 126 };
            string[] bagCounts = { "12", "16", "7", "3", "1", "1", "1", "1", "1", "1", "4", "2" };
            Color[] bagRarity = { White, White, Green, Blue, Gold, Red, White, Green, Blue, Gold, White, Green };
            for (int i = 0; i < 30; i++)
            {
                bool populated = i < bagIcons.Length;
                CreateItemSlot(bagGrid, "Slot_" + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    populated ? Terrain(bagIcons[i]) : null,
                    populated ? bagRarity[i] : MutedInk,
                    populated ? bagCounts[i] : string.Empty,
                    i == 0);
            }

            AddText(
                Fixed("SafeHeading", backpackPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 124f),
                    new Vector2(360f, 34f)),
                "SAFE CASE", 19, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("SafeCapacity", backpackPanel, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(300f, 124f),
                    new Vector2(130f, 34f)),
                "3 / 5", 17, Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform safeGrid = Fixed("SafeGrid", backpackPanel, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(30f, 24f), new Vector2(830f, 90f));
            GridLayoutGroup safeLayout = safeGrid.gameObject.AddComponent<GridLayoutGroup>();
            safeLayout.cellSize = new Vector2(150f, 90f);
            safeLayout.spacing = new Vector2(18f, 0f);
            safeLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            safeLayout.constraintCount = 5;
            for (int i = 0; i < 5; i++)
            {
                CreateItemSlot(safeGrid, "SafeSlot_" + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    i < 3 ? Terrain(bagIcons[i]) : null,
                    i < 3 ? bagRarity[i] : MutedInk,
                    i < 3 ? bagCounts[i] : string.Empty,
                    false);
            }

            RectTransform characterPanel =
                Panel(
                    Fixed("CharacterPanel", window, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -132f),
                        new Vector2(548f, 748f)), panelBeige, Color.white);
            AddText(
                Fixed("Heading", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -24f),
                    new Vector2(320f, 38f)),
                "CHARACTER LOADOUT", 20, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            CreateCharacterSelector(characterPanel);

            RectTransform portrait =
                Panel(
                    Fixed("PortraitPanel", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(30f, -128f), new Vector2(142f, 142f)), panelBlue, Color.white);
            AddSprite(
                Fixed("Portrait", portrait, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(104f, 104f)), Terrain(84), Color.white, true);
            AddText(
                Fixed("CharacterName", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(194f, -138f), new Vector2(322f, 38f)),
                "CHARACTER 01", 22, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("CharacterId", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(194f, -178f),
                    new Vector2(322f, 26f)),
                "ID 1001", 14, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Hp", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(194f, -220f),
                    new Vector2(150f, 28f)),
                "HP  120 / 120", 15, Red, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Mp", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(358f, -220f),
                    new Vector2(150f, 28f)),
                "MP  40 / 40", 15, Blue, TextAnchor.MiddleLeft, FontStyle.Bold);

            RectTransform stats =
                Panel(
                    Fixed("Stats", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -294f),
                        new Vector2(488f, 110f)), panelInsetLight, Color.white);
            CreateStatLine(stats, "Attack", "ATK", "14", 22f, 14f, Red, 210f);
            CreateStatLine(stats, "Magic", "MAT", "6", 262f, 14f, Blue, 204f);
            CreateStatLine(stats, "Speed", "SPEED", "12", 22f, 60f, Green, 210f);
            CreateStatLine(stats, "Skill", "SKILL", "01", 262f, 60f, Gold, 204f);

            AddText(
                Fixed("EquipmentHeading", characterPanel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(30f, -430f), new Vector2(488f, 34f)),
                "EQUIPMENT", 18, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            CreateEquipmentSlot(characterPanel, "WeaponSlot", "WEAPON", "TRAINING SWORD", Terrain(104), 30f, 470f, Red);
            CreateEquipmentSlot(characterPanel, "ArmorSlot", "Armor", "EMPTY", null, 282f, 470f, Blue);

            RectTransform selectedItem =
                Panel(
                    Fixed("SelectedItem", characterPanel, new Vector2(0f, 0f), new Vector2(0f, 0f),
                        new Vector2(30f, 28f), new Vector2(488f, 142f)), panelInsetLight, Color.white);
            AddSprite(
                Fixed("Icon", selectedItem, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(58f, 0f),
                    new Vector2(76f, 76f)), Terrain(101), Color.white, true);
            AddText(
                Fixed("Name", selectedItem, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(112f, -22f),
                    new Vector2(196f, 32f)),
                "OLD COIN  x12", 18, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Rarity", selectedItem, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(112f, -58f),
                    new Vector2(196f, 24f)),
                "COMMON LOOT", 13, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            Button moveToSafeButton = CreateButton(
                Fixed("MoveToSafeButton", selectedItem, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-18f, 0f), new Vector2(150f, 64f)),
                "TO SAFE", ButtonKind.Blue, 17);
            RegisterReference(moveToSafeButton, "moveToSafeButton");
            return root;
        }

        private static GameObject CreateCanvas(string name, int sortingOrder)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup));
            root.layer = LayerMask.NameToLayer("UI");
            root.AddComponent<UISerializationRoot>();
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return root;
        }

        private static void AddShell(RectTransform root, string activeSection)
        {
            RectTransform header = Stretch("Header", root, 0f, 0f, 972f, 0f);
            AddImage(header, BackdropRaised, null);
            AddImage(
                Fixed("Accent", header, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(288f, 6f)), Blue, null);
            AddText(
                Fixed("Brand", header, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(62f, 4f),
                    new Vector2(150f, 78f)),
                "SBE", 50, White, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Product", header, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(182f, -4f),
                    new Vector2(360f, 46f)),
                "EXPEDITION DESK", 16, MutedWhite, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Profile", header, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-64f, 0f),
                    new Vector2(360f, 46f)),
                "LOCAL PROFILE", 15, MutedWhite, TextAnchor.MiddleRight, FontStyle.Bold);

            RectTransform sidebar = Stretch("Sidebar", root, 0f, 1632f, 0f, 108f);
            AddImage(sidebar, BackdropRaised, null);
            AddText(
                Fixed("NavigationLabel", sidebar, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -44f),
                    new Vector2(220f, 28f)),
                "NAVIGATION", 13, MutedWhite, TextAnchor.MiddleLeft, FontStyle.Bold);
            CreateNavigationButton(sidebar, "HomeButton", "HOME", 98f, activeSection == "HOME", "homeButton");
            CreateNavigationButton(sidebar, "WarehouseButton", "WAREHOUSE", 172f, activeSection == "WAREHOUSE",
                "warehouseButton");
            CreateNavigationButton(sidebar, "LoadoutButton", "LOADOUT", 246f, activeSection == "LOADOUT",
                "loadoutButton");

            RectTransform status =
                Panel(
                    Fixed("ProfileStatus", sidebar, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(28f, 34f),
                        new Vector2(232f, 126f)), panelInsetBrown, Color.white);
            AddText(
                Fixed("SaveLabel", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -16f),
                    new Vector2(196f, 24f)),
                "LOCAL SAVE", 13, PaperMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("SaveState", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -48f),
                    new Vector2(196f, 30f)),
                "READY", 18, Green, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Version", status, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(18f, 12f),
                    new Vector2(196f, 22f)),
                "PROTOTYPE 01", 12, MutedWhite, TextAnchor.MiddleLeft, FontStyle.Bold);
        }

        private static void CreateNavigationButton(RectTransform parent, string name, string label, float top,
            bool selected, string variableName)
        {
            RectTransform rect = Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -top),
                new Vector2(232f, 58f));
            Button button = CreateButton(rect, label, selected ? ButtonKind.Blue : ButtonKind.Grey, 17);
            RegisterReference(button, variableName);
            if (selected)
            {
                AddImage(
                    Fixed("SelectionMarker", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-8f, 0f),
                        new Vector2(5f, 34f)), Gold, null);
            }
        }

        private static void CreateCharacterCard(RectTransform parent, int index, float x, float y, int hp, int mp,
            int speed, Sprite portraitSprite)
        {
            RectTransform card = Panel(Fixed("CharacterSlot_" + index.ToString("00", CultureInfo.InvariantCulture),
                    parent,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(414f, 236f)), panelBeige,
                Color.white);
            Toggle toggle = card.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = card.GetComponent<Image>();
            toggle.targetGraphic.raycastTarget = true;

            RectTransform portrait =
                Panel(
                    Fixed("PortraitPanel", card, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(24f, 0f),
                        new Vector2(132f, 180f)), panelBlue, Color.white);
            AddSprite(
                Fixed("Portrait", portrait, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(104f, 104f)), portraitSprite, Color.white, true);
            AddText(
                Fixed("Name", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -24f),
                    new Vector2(190f, 34f)),
                "CHARACTER " + index.ToString("00", CultureInfo.InvariantCulture), 19, Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            AddText(
                Fixed("Id", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -60f),
                    new Vector2(150f, 24f)),
                "ID " + (1000 + index).ToString(CultureInfo.InvariantCulture), 12, MutedInk, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            AddText(
                Fixed("Hp", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -104f),
                    new Vector2(190f, 28f)),
                "HP   " + hp.ToString(CultureInfo.InvariantCulture), 15, Red, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Mp", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -138f),
                    new Vector2(190f, 28f)),
                "MP   " + mp.ToString(CultureInfo.InvariantCulture), 15, Blue, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Speed", card, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(178f, -172f),
                    new Vector2(190f, 28f)),
                "SPEED   " + speed.ToString(CultureInfo.InvariantCulture), 15, Green, TextAnchor.MiddleLeft,
                FontStyle.Bold);

            RectTransform check = Fixed("Selected", card, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(32f, 32f));
            Image checkImage = AddImage(check, Color.white, iconCheck);
            checkImage.preserveAspect = true;
            toggle.graphic = checkImage;
            toggle.isOn = true;
        }

        private static void CreateCharacterSelector(RectTransform parent)
        {
            RectTransform groupRoot = Fixed("CharacterTabs", parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(30f, -74f), new Vector2(488f, 52f));
            ToggleGroup group = groupRoot.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            HorizontalLayoutGroup layout = groupRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            for (int i = 0; i < 4; i++)
            {
                RectTransform tab = NewRect("CharacterTab_" + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    groupRoot);
                Image baseImage = AddImage(tab, Color.white, buttonGrey);
                baseImage.type = Image.Type.Sliced;
                baseImage.color = BackdropSoft;
                baseImage.raycastTarget = true;
                Toggle toggle = tab.gameObject.AddComponent<Toggle>();
                toggle.targetGraphic = baseImage;
                toggle.group = group;
                RectTransform selected = Stretch("Selected", tab, 0f, 0f, 0f, 0f);
                Image selectedImage = AddImage(selected, Color.white, buttonBlue);
                selectedImage.type = Image.Type.Sliced;
                toggle.graphic = selectedImage;
                AddText(Stretch("Label", tab, 0f, 0f, 0f, 0f), (i + 1).ToString("00", CultureInfo.InvariantCulture), 16,
                    White, TextAnchor.MiddleCenter, FontStyle.Bold);
                toggle.isOn = i == 0;
            }
        }

        private static void CreateEquipmentSlot(RectTransform parent, string name, string heading, string itemName,
            Sprite icon, float x, float y, Color accent)
        {
            RectTransform slot =
                Panel(
                    Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                        new Vector2(236f, 108f)), panelInsetLight, Color.white);
            AddImage(
                Fixed("Accent", slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f),
                    new Vector2(5f, 72f)), accent, null);
            if (icon != null)
            {
                AddSprite(
                    Fixed("Icon", slot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(44f, 0f),
                        new Vector2(56f, 56f)), icon, Color.white, true);
            }

            AddText(
                Fixed("Heading", slot, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -20f),
                    new Vector2(140f, 24f)),
                heading.ToUpperInvariant(), 12, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("ItemName", slot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(82f, 18f),
                    new Vector2(140f, 42f)),
                itemName, 14, Ink, TextAnchor.MiddleLeft, FontStyle.Bold, true, 11);
            Button button = slot.gameObject.AddComponent<Button>();
            button.targetGraphic = slot.GetComponent<Image>();
            button.targetGraphic.raycastTarget = true;
        }

        private static void CreateSegmentedToggles(RectTransform parent, string name, string[] labels, float x, float y,
            float width, float height, int selectedIndex)
        {
            RectTransform groupRoot = Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(width, height));
            ToggleGroup group = groupRoot.gameObject.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;
            HorizontalLayoutGroup layout = groupRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;

            for (int i = 0; i < labels.Length; i++)
            {
                RectTransform tab = NewRect(labels[i].Replace(" ", string.Empty) + "Toggle", groupRoot);
                Image baseImage = AddImage(tab, Color.white, buttonGrey);
                baseImage.type = Image.Type.Sliced;
                baseImage.color = BackdropSoft;
                baseImage.raycastTarget = true;
                Toggle toggle = tab.gameObject.AddComponent<Toggle>();
                toggle.targetGraphic = baseImage;
                toggle.group = group;
                RectTransform selected = Stretch("Selected", tab, 0f, 0f, 0f, 0f);
                Image selectedImage = AddImage(selected, Color.white, buttonBlue);
                selectedImage.type = Image.Type.Sliced;
                toggle.graphic = selectedImage;
                AddText(Stretch("Label", tab, 0f, 0f, 0f, 0f), labels[i], 15, White, TextAnchor.MiddleCenter,
                    FontStyle.Bold, true, 11);
                toggle.isOn = i == selectedIndex;
            }
        }

        private static void CreateItemSlot(RectTransform parent, string name, Sprite icon, Color rarity,
            string quantity, bool selected)
        {
            RectTransform slot = NewRect(name, parent);
            Image background = AddImage(slot, selected ? new Color(0.86f, 0.95f, 1f, 1f) : Color.white,
                panelInsetLight);
            background.type = Image.Type.Sliced;
            background.raycastTarget = true;
            Button button = slot.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.97f, 1f, 1f);
            colors.pressedColor = new Color(0.76f, 0.86f, 0.9f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            AddImage(TopStretch("Rarity", slot, 7f, 7f, 7f, 6f), rarity, null);
            if (icon != null)
            {
                AddSprite(
                    Fixed("Icon", slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f),
                        new Vector2(62f, 62f)), icon, Color.white, true);
            }

            if (!string.IsNullOrEmpty(quantity))
            {
                RectTransform badge = Fixed("QuantityBadge", slot, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-9f, 8f), new Vector2(34f, 24f));
                AddImage(badge, new Color(0.09f, 0.12f, 0.15f, 0.9f), null);
                AddText(Stretch("Quantity", badge, 0f, 0f, 0f, 0f), quantity, 12, White, TextAnchor.MiddleCenter,
                    FontStyle.Bold);
            }

            if (selected)
            {
                AddImage(
                    Fixed("SelectedMarker", slot, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8f, 8f),
                        new Vector2(16f, 16f)), Blue, iconCheck).preserveAspect = true;
            }
        }

        private static void CreateStatLine(RectTransform parent, string name, string label, string value, float x,
            float y, Color valueColor, float explicitWidth = 0f)
        {
            float width = explicitWidth > 0f ? explicitWidth : parent.rect.width - x - 20f;
            RectTransform line = Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(width, 34f));
            AddText(
                Fixed("Label", line, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero,
                    new Vector2(Mathf.Max(80f, width - 78f), 30f)),
                label, 14, MutedInk, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Value", line, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(76f, 30f)),
                value, 16, valueColor, TextAnchor.MiddleRight, FontStyle.Bold);
        }

        private static void AddSectionHeading(RectTransform panel, string heading, string value, Color valueColor)
        {
            AddText(
                Fixed("Heading", panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -28f),
                    new Vector2(400f, 42f)),
                heading, 25, Paper, TextAnchor.MiddleLeft, FontStyle.Bold);
            AddText(
                Fixed("Value", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -30f),
                    new Vector2(300f, 38f)),
                value, 16, valueColor, TextAnchor.MiddleRight, FontStyle.Bold);
        }

        private static InputField CreateInputField(RectTransform parent, string name, string value,
            string placeholderText, float x, float y, float width, float height, bool integerOnly)
        {
            RectTransform rect =
                Panel(
                    Fixed(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                        new Vector2(width, height)), panelInsetLight, Color.white);
            InputField input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = rect.GetComponent<Image>();
            input.targetGraphic.raycastTarget = true;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = integerOnly ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;

            Text placeholder = AddText(Stretch("Placeholder", rect, 18f, 18f, 10f, 10f), placeholderText, 14,
                new Color(MutedInk.r, MutedInk.g, MutedInk.b, 0.65f), TextAnchor.MiddleLeft, FontStyle.Bold);
            Text text = AddText(Stretch("Text", rect, 18f, 18f, 10f, 10f), value, 21, Ink, TextAnchor.MiddleLeft,
                FontStyle.Bold);
            input.placeholder = placeholder;
            input.textComponent = text;
            input.text = value;
            input.caretColor = Blue;
            input.selectionColor = new Color(Blue.r, Blue.g, Blue.b, 0.35f);
            return input;
        }

        private static Button CreateButton(RectTransform rect, string label, ButtonKind kind, int fontSize)
        {
            Sprite normal;
            Sprite pressed;
            switch (kind)
            {
                case ButtonKind.Blue:
                    normal = buttonBlue;
                    pressed = buttonBluePressed;
                    break;
                case ButtonKind.Brown:
                    normal = buttonBrown;
                    pressed = buttonBrownPressed;
                    break;
                case ButtonKind.Beige:
                    normal = buttonBeige;
                    pressed = buttonBeigePressed;
                    break;
                default:
                    normal = buttonGrey;
                    pressed = buttonGreyPressed;
                    break;
            }

            Image image = AddImage(rect, Color.white, normal);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState state = button.spriteState;
            state.highlightedSprite = normal;
            state.selectedSprite = normal;
            state.pressedSprite = pressed;
            state.disabledSprite = pressed;
            button.spriteState = state;
            AddText(Stretch("Label", rect, 12f, 12f, 7f, 7f), label, fontSize,
                kind == ButtonKind.Beige || kind == ButtonKind.Grey ? Ink : White,
                TextAnchor.MiddleCenter, FontStyle.Bold, true, Mathf.Max(10, fontSize - 5));
            return button;
        }

        private static Button CreateIconButton(RectTransform rect, Sprite icon, bool primary)
        {
            Image image = AddImage(rect, Color.white, primary ? buttonSquareBlue : buttonSquareGrey);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.SpriteSwap;
            SpriteState state = button.spriteState;
            state.highlightedSprite = primary ? buttonSquareBlue : buttonSquareGrey;
            state.selectedSprite = state.highlightedSprite;
            state.pressedSprite = primary ? buttonSquareBluePressed : buttonSquareGreyPressed;
            button.spriteState = state;
            AddSprite(
                Fixed("Icon", rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    new Vector2(24f, 24f)), icon, Color.white, true);
            return button;
        }

        private static RectTransform Panel(RectTransform rect, Sprite sprite, Color color)
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

        private static Text AddText(RectTransform rect, string value, int fontSize, Color color, TextAnchor alignment,
            FontStyle style, bool bestFit = false, int minSize = 10)
        {
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = uiFont;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = bestFit;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = fontSize;
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

        private static Sprite PrepareSprite(string fileName, Vector4 border)
        {
            string source = RawUiPath + "/" + fileName;
            string destination = UiSpritePath + "/" + fileName;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(destination) == null)
            {
                if (!AssetDatabase.CopyAsset(source, destination))
                {
                    throw new InvalidOperationException("Failed to copy UI sprite from " + source + ".");
                }
            }

            TextureImporter importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException("No texture importer for " + destination + ".");
            }

            bool changed = importer.textureType != TextureImporterType.Sprite ||
                           importer.spriteImportMode != SpriteImportMode.Single ||
                           importer.mipmapEnabled ||
                           importer.filterMode != FilterMode.Point ||
                           importer.wrapMode != TextureWrapMode.Clamp ||
                           importer.textureCompression != TextureImporterCompression.Uncompressed ||
                           importer.spriteBorder != border;
            if (changed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destination);
            if (sprite == null)
            {
                throw new InvalidOperationException("Failed to load UI sprite at " + destination + ".");
            }

            return sprite;
        }

        private static Sprite Terrain(int index)
        {
            Sprite sprite;
            return terrainSprites != null && terrainSprites.TryGetValue(index, out sprite) ? sprite : null;
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

        private static int ParseSuffix(string value)
        {
            int separator = value.LastIndexOf('_');
            int parsed;
            return separator >= 0 && int.TryParse(value.Substring(separator + 1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : -1;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            folder = folder.Replace('\\', '/');
            string[] parts = folder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private enum ButtonKind
        {
            Blue,
            Brown,
            Grey,
            Beige
        }
    }
}