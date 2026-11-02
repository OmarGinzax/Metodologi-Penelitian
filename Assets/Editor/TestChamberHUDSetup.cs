using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RPGStrategy.Core.Combat;
using RPGStrategy.UI;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Editor-only script that constructs the Canvas-based Tactical HUD
    /// for the Sector 7 Overpass Test Chamber.
    ///
    /// Menu:  RPGStrategy > Test Chamber 2D > Build Tactical HUD (Canvas)
    ///
    /// Builds a Screen-Space Overlay Canvas with:
    ///   - HUD panel (left-side, 420 px wide)
    ///       * Header (title + round info)
    ///       * Control buttons (Step / Auto Run)
    ///       * Squad status rows (auto-populated at runtime)
    ///       * Combat log scroll area
    ///
    /// All GameObjects are named descriptively and use real UI components
    /// so you can select, inspect and restyle them in the Editor at any time.
    /// </summary>
    public static class TestChamberHUDSetup
    {
        // ===================================================================
        // Palette — edit these colours to re-theme the whole HUD
        // ===================================================================
        private static readonly Color ColBackground   = new Color(0.04f, 0.07f, 0.12f, 0.92f);
        private static readonly Color ColPanelHeader  = new Color(0.08f, 0.14f, 0.24f, 1.00f);
        private static readonly Color ColPanelBody    = new Color(0.05f, 0.09f, 0.15f, 0.95f);
        private static readonly Color ColBtnStep      = new Color(0.15f, 0.55f, 0.95f, 1.00f);
        private static readonly Color ColBtnAuto      = new Color(0.12f, 0.42f, 0.15f, 1.00f);
        private static readonly Color ColText         = new Color(0.88f, 0.92f, 1.00f, 1.00f);
        private static readonly Color ColTextDim      = new Color(0.55f, 0.60f, 0.70f, 1.00f);
        private static readonly Color ColAccent       = new Color(0.25f, 0.70f, 1.00f, 1.00f);
        private static readonly Color ColLogBg        = new Color(0.03f, 0.05f, 0.08f, 0.98f);

        private const float HudWidth = 430f;   // left panel width
        private const float Margin   = 12f;

        // ===================================================================
        // Menu items
        // ===================================================================

        [MenuItem("RPGStrategy/Test Chamber 2D/Build Tactical HUD (Canvas)", priority = 5)]
        public static void BuildHUD()
        {
            // Remove any existing HUD canvas first
            var existing = GameObject.Find("[TacticalHUD_Canvas]");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            // Ensure EventSystem exists so uGUI can receive mouse clicks
            EnsureEventSystem();

            // Find the BattleManager in the scene (must exist first)
            var bm = Object.FindFirstObjectByType<BattleManager2D>();
            if (bm == null)
            {
                Debug.LogWarning(
                    "[TestChamberHUDSetup] No BattleManager2D found in scene. " +
                    "Run 'Prepare 2D Test Chamber' first, then build the HUD.");
                return;
            }

            // ------------------------------------------------------------------
            // Root Canvas
            // ------------------------------------------------------------------
            var canvasGo = new GameObject("[TacticalHUD_Canvas]");
            Undo.RegisterCreatedObjectUndo(canvasGo, "Build Tactical HUD");

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1920, 1080);
            scaler.screenMatchMode      = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight   = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // ------------------------------------------------------------------
            // HUD background panel (left strip)
            // ------------------------------------------------------------------
            var hudPanel = MakePanel(canvasGo.transform, "HUD_Panel",
                new Vector2(0, 0), new Vector2(0, 1),          // anchor: left strip
                new Vector2(0, 0), new Vector2(0, 1),          // pivot
                new Vector2(Margin, Margin),                    // offset min
                new Vector2(HudWidth + Margin, -Margin));       // offset max — anchored coords
            SetColor(hudPanel, ColBackground);

            // Override: use anchored position approach via RectTransform directly
            var hudRT = hudPanel.GetComponent<RectTransform>();
            hudRT.anchorMin = new Vector2(0, 0);
            hudRT.anchorMax = new Vector2(0, 1);
            hudRT.pivot     = new Vector2(0, 0.5f);
            hudRT.offsetMin = new Vector2(Margin, Margin);
            hudRT.offsetMax = new Vector2(HudWidth + Margin, -Margin);

            // Add vertical layout group
            var vlg = hudPanel.AddComponent<VerticalLayoutGroup>();
            vlg.padding    = new RectOffset(10, 10, 10, 10);
            vlg.spacing    = 6f;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth   = true;
            vlg.childControlHeight  = true;

            // ------------------------------------------------------------------
            // Section: Header
            // ------------------------------------------------------------------
            var headerPanel = MakeSection(hudPanel.transform, "Header_Panel", 56f, ColPanelHeader);
            var txtTitle    = MakeTMP(headerPanel.transform, "Title_Text", "SECTOR 7 OVERPASS", 14, FontStyles.Bold, ColAccent, TextAlignmentOptions.Center);
            var txtRound    = MakeTMP(headerPanel.transform, "Round_Text", "Round 1 | Turn 0 | InProgress", 11, FontStyles.Normal, ColTextDim, TextAlignmentOptions.Center);

            var headerVLG   = headerPanel.AddComponent<VerticalLayoutGroup>();
            headerVLG.padding = new RectOffset(6, 6, 6, 6);
            headerVLG.spacing = 2f;
            headerVLG.childForceExpandWidth  = true;
            headerVLG.childForceExpandHeight = false;
            headerVLG.childControlWidth   = true;
            headerVLG.childControlHeight  = false;

            // ------------------------------------------------------------------
            // Section: Buttons
            // ------------------------------------------------------------------
            var btnPanel = MakeSection(hudPanel.transform, "Buttons_Panel", 44f, ColPanelBody);
            var btnHLG   = btnPanel.AddComponent<HorizontalLayoutGroup>();
            btnHLG.padding  = new RectOffset(6, 6, 6, 6);
            btnHLG.spacing  = 8f;
            btnHLG.childForceExpandWidth  = true;
            btnHLG.childForceExpandHeight = true;
            btnHLG.childControlWidth   = true;
            btnHLG.childControlHeight  = true;

            var (btnStep, txtStep)    = MakeButton(btnPanel.transform, "Btn_Step",    "Step Turn",  ColBtnStep);
            var (btnAuto, txtAutoRun) = MakeButton(btnPanel.transform, "Btn_AutoRun", "Auto Run",   ColBtnAuto);

            // ------------------------------------------------------------------
            // Section: Squad status rows container
            // ------------------------------------------------------------------
            var squadSection = MakeSection(hudPanel.transform, "Squad_Section", 30f, ColPanelHeader);
            MakeTMP(squadSection.transform, "Squad_Header_Label", "SQUAD STATUS", 10, FontStyles.Bold, ColAccent, TextAlignmentOptions.Left);

            var squadBody = MakeSection(hudPanel.transform, "Squad_Rows_Container", 200f, ColPanelBody);
            var squadVLG  = squadBody.AddComponent<VerticalLayoutGroup>();
            squadVLG.padding  = new RectOffset(8, 8, 4, 4);
            squadVLG.spacing  = 4f;
            squadVLG.childForceExpandWidth  = true;
            squadVLG.childForceExpandHeight = false;
            squadVLG.childControlWidth   = true;
            squadVLG.childControlHeight  = false;

            // Build the squad-row prefab as a template child (hidden child approach)
            var rowTemplate = BuildSquadRowTemplate(squadBody.transform);

            // ------------------------------------------------------------------
            // Section: Combat Log (Scrollable ScrollRect)
            // ------------------------------------------------------------------
            var logSection  = MakeSection(hudPanel.transform, "Log_Section", 30f, ColPanelHeader);
            MakeTMP(logSection.transform, "Log_Header_Label", "COMBAT LOG (Scrollable)", 10, FontStyles.Bold, ColAccent, TextAlignmentOptions.Left);

            var logBody = MakeSection(hudPanel.transform, "Log_ScrollArea", 200f, ColLogBg);
            var logImg  = logBody.GetComponent<Image>();
            if (logImg != null) logImg.raycastTarget = true;
            var logLE   = logBody.GetComponent<LayoutElement>();
            if (logLE != null)
            {
                logLE.minHeight = 150f;
                logLE.preferredHeight = 220f;
                logLE.flexibleHeight = 1f; // Fills remaining vertical height in HUD panel
            }

            var logScrollRect = logBody.AddComponent<ScrollRect>();
            logScrollRect.horizontal = false;
            logScrollRect.vertical   = true;
            logScrollRect.movementType = ScrollRect.MovementType.Clamped;
            logScrollRect.scrollSensitivity = 35f;

            // Vertical Scrollbar
            var scrollbarGo = new GameObject("Scrollbar_Vertical");
            scrollbarGo.transform.SetParent(logBody.transform, false);
            var sbImg = scrollbarGo.AddComponent<Image>();
            sbImg.color = new Color(0.08f, 0.11f, 0.16f, 0.9f);
            var sbRT = scrollbarGo.GetComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1, 0);
            sbRT.anchorMax = new Vector2(1, 1);
            sbRT.pivot     = new Vector2(1, 0.5f);
            sbRT.sizeDelta = new Vector2(10f, 0f);
            sbRT.anchoredPosition = new Vector2(-4f, 0f);

            var slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(scrollbarGo.transform, false);
            var saRT = slidingArea.AddComponent<RectTransform>();
            saRT.anchorMin = Vector2.zero;
            saRT.anchorMax = Vector2.one;
            saRT.offsetMin = Vector2.zero;
            saRT.offsetMax = Vector2.zero;

            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(slidingArea.transform, false);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = new Color(0.28f, 0.46f, 0.68f, 0.95f);
            handleImg.raycastTarget = true;
            var handleRT = handleGo.GetComponent<RectTransform>();
            handleRT.sizeDelta = new Vector2(10f, 20f);

            var scrollbar = scrollbarGo.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleImg;
            scrollbar.handleRect = handleRT;

            logScrollRect.verticalScrollbar = scrollbar;
            logScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // Viewport with RectMask2D
            var viewportGo = new GameObject("Viewport");
            viewportGo.transform.SetParent(logBody.transform, false);
            var vpImg = viewportGo.AddComponent<Image>();
            vpImg.color = Color.clear;
            vpImg.raycastTarget = true; // Enables scrollwheel & drag anywhere in log area
            viewportGo.AddComponent<RectMask2D>();
            var vpRT = viewportGo.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(6, 6);
            vpRT.offsetMax = new Vector2(-18, -6); // Leave room for scrollbar on the right

            // Content container
            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRT = contentGo.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;

            var contentVLG = contentGo.AddComponent<VerticalLayoutGroup>();
            contentVLG.padding = new RectOffset(4, 4, 4, 4);
            contentVLG.spacing = 2f;
            contentVLG.childForceExpandWidth  = true;
            contentVLG.childForceExpandHeight = false;
            contentVLG.childControlWidth   = true;
            contentVLG.childControlHeight  = true;

            var csf = contentGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            // Text inside Content
            var txtLog = MakeTMP(contentGo.transform, "Log_Text", "", 9, FontStyles.Normal, ColTextDim, TextAlignmentOptions.TopLeft, autoHeight: true);
            txtLog.enableWordWrapping = true;
            txtLog.overflowMode       = TextOverflowModes.Overflow;
            var txtLogRT = txtLog.GetComponent<RectTransform>();
            txtLogRT.anchorMin = new Vector2(0, 1);
            txtLogRT.anchorMax = new Vector2(1, 1);
            txtLogRT.pivot     = new Vector2(0.5f, 1f);
            txtLogRT.offsetMin = Vector2.zero;
            txtLogRT.offsetMax = Vector2.zero;
            var txtCsf = txtLog.gameObject.AddComponent<ContentSizeFitter>();
            txtCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            txtCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            logScrollRect.viewport = vpRT;
            logScrollRect.content  = contentRT;

            // ------------------------------------------------------------------
            // Section: Toggle HUD Button (Always on Canvas root so it's always accessible)
            // ------------------------------------------------------------------
            var (btnToggle, txtToggle) = MakeButton(canvasGo.transform, "Btn_ToggleHUD", "◄ Hide (H / ◄)", ColBtnStep);
            var toggleRT = btnToggle.GetComponent<RectTransform>();
            toggleRT.anchorMin = new Vector2(0, 1);
            toggleRT.anchorMax = new Vector2(0, 1);
            toggleRT.pivot     = new Vector2(0, 1);
            toggleRT.anchoredPosition = new Vector2(HudWidth + Margin + 6f, -Margin);
            toggleRT.sizeDelta = new Vector2(130f, 32f);

            // ------------------------------------------------------------------
            // Wire TacticalHUDController on the canvas root
            // ------------------------------------------------------------------
            var hud = canvasGo.AddComponent<TacticalHUDController>();

            // Use SerializedObject to assign references in-editor (no reflection hacks)
            var so = new SerializedObject(hud);
            so.FindProperty("battleManager").objectReferenceValue   = bm;
            so.FindProperty("hudPanel").objectReferenceValue        = hudPanel;
            so.FindProperty("btnToggleHUD").objectReferenceValue    = btnToggle;
            so.FindProperty("txtBtnToggleHUD").objectReferenceValue = txtToggle;
            so.FindProperty("txtTitle").objectReferenceValue         = txtTitle;
            so.FindProperty("txtRoundInfo").objectReferenceValue     = txtRound;
            so.FindProperty("btnStep").objectReferenceValue          = btnStep;
            so.FindProperty("txtBtnStep").objectReferenceValue       = txtStep;
            so.FindProperty("btnAutoRun").objectReferenceValue       = btnAuto;
            so.FindProperty("txtBtnAutoRun").objectReferenceValue    = txtAutoRun;
            so.FindProperty("squadRowContainer").objectReferenceValue= squadBody.transform;
            so.FindProperty("squadRowPrefab").objectReferenceValue   = rowTemplate;
            so.FindProperty("scrollLog").objectReferenceValue        = logScrollRect;
            so.FindProperty("txtCombatLog").objectReferenceValue     = txtLog;
            so.FindProperty("logLinesToShow").intValue               = 100;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = canvasGo;
            EditorGUIUtility.PingObject(canvasGo);

            Debug.Log("[TestChamberHUDSetup] Tactical HUD Canvas built successfully. " +
                      "Scrollable Log and Toggle UI Button configured.");
        }

        [MenuItem("RPGStrategy/Test Chamber 2D/Clear Tactical HUD", priority = 15)]
        public static void ClearHUD()
        {
            var go = GameObject.Find("[TacticalHUD_Canvas]");
            if (go != null) Undo.DestroyObjectImmediate(go);

            var es = GameObject.Find("[EventSystem]");
            if (es != null) Undo.DestroyObjectImmediate(es);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[TestChamberHUDSetup] Cleared [TacticalHUD_Canvas] and [EventSystem].");
        }

        public static void EnsureEventSystem()
        {
            var es = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                var esGo = new GameObject("[EventSystem]");
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
                es = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                Debug.Log("[TestChamberHUDSetup] Created [EventSystem] with UI input module.");
            }
            else
            {
                var baseModule = es.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                if (baseModule == null)
                {
#if ENABLE_INPUT_SYSTEM
                    es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                    es.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                }
            }
        }

        // ===================================================================
        // Private helpers
        // ===================================================================

        private static GameObject MakePanel(Transform parent, string name,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivotMin, Vector2 pivotMax,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.clear;
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot     = new Vector2(0, 0.5f);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return go;
        }

        private static GameObject MakeSection(Transform parent, string name, float preferredHeight, Color bg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            le.flexibleHeight  = 0;
            return go;
        }

        private static TMP_Text MakeTMP(Transform parent, string name, string defaultText,
            int fontSize, FontStyles style, Color colour, TextAlignmentOptions align, bool autoHeight = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text           = defaultText;
            t.fontSize       = fontSize;
            t.fontStyle      = style;
            t.color          = colour;
            t.alignment      = align;
            t.enableWordWrapping = false;
            t.overflowMode   = TextOverflowModes.Truncate;
            t.raycastTarget  = false;
            if (!autoHeight)
            {
                var le = go.AddComponent<LayoutElement>();
                le.preferredHeight = fontSize + 6f;
            }
            return t;
        }

        private static (Button btn, TMP_Text label) MakeButton(Transform parent, string name, string text, Color bg)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            ColorBlock cb = btn.colors;
            cb.normalColor      = bg;
            cb.highlightedColor = Color.Lerp(bg, Color.white, 0.25f);
            cb.pressedColor     = Color.Lerp(bg, Color.black, 0.25f);
            cb.disabledColor    = new Color(bg.r, bg.g, bg.b, 0.35f);
            btn.colors = cb;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var lbl = labelGo.AddComponent<TextMeshProUGUI>();
            lbl.text      = text;
            lbl.fontSize  = 12;
            lbl.fontStyle = FontStyles.Bold;
            lbl.color     = ColText;
            lbl.alignment = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;

            return (btn, lbl);
        }

        /// <summary>
        /// Build a single squad-row template as a child of the container (will be
        /// used by TacticalHUDController as the prefab to Instantiate at runtime).
        /// </summary>
        private static GameObject BuildSquadRowTemplate(Transform parent)
        {
            var go = new GameObject("SquadRow_Template");
            go.transform.SetParent(parent, false);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.09f, 0.13f, 0.20f, 0.80f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 42f;
            le.flexibleHeight  = 0f;

            var lbl = MakeTMP(go.transform, "Label", "", 10, FontStyles.Normal, ColText, TextAlignmentOptions.TopLeft);
            lbl.enableWordWrapping = true;
            lbl.overflowMode = TextOverflowModes.Truncate;
            var lblRT = lbl.GetComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = new Vector2(6, 4);
            lblRT.offsetMax = new Vector2(-6, -4);

            // Keep template inactive so it doesn't show on its own
            go.SetActive(false);

            return go;
        }

        private static void SetColor(GameObject go, Color c)
        {
            var img = go.GetComponent<Image>();
            if (img != null) img.color = c;
        }
    }
}
