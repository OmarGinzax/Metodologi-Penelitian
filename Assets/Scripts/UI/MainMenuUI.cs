using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace RPGStrategy.UI
{
    /// <summary>
    /// Controller UI Menu Utama untuk memilih skenario eksperimen AI taktis 2D:
    ///   1. HFSM Baseline (Scene: "FSM")
    ///   2. Pure LLM Agent (Scene: "LLM")
    ///   3. Hybrid Agent (Scene: "Hybrid")
    ///
    /// Jika objek UI belum diset di scene, skrip ini secara otomatis membangun
    /// Canvas, EventSystem, dan 3 tombol interaktif secara prosedural saat Start.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Scene Destination Names")]
        [SerializeField] private string hfsmSceneName = "FSM";
        [SerializeField] private string llmSceneName = "LLM";
        [SerializeField] private string hybridSceneName = "Hybrid";

        [Header("Optional Explicit Button References")]
        [SerializeField] private Button btnHFSM;
        [SerializeField] private Button btnLLM;
        [SerializeField] private Button btnHybrid;

        [Header("Scene Status / Feedback")]
        [SerializeField] private TMP_Text txtStatus;

        private void Awake()
        {
            TacticalHUDController.EnsureEventSystemRuntime();
        }

        private void Start()
        {
            // Jika tombol di Inspector belum dihubungkan, cari atau bangun secara otomatis
            if (btnHFSM == null || btnLLM == null || btnHybrid == null)
            {
                AutoWireOrBuildUI();
            }
            else
            {
                HookButtonEvents();
            }
        }

        private void HookButtonEvents()
        {
            if (btnHFSM != null)
            {
                btnHFSM.onClick.RemoveListener(LoadHFSM);
                btnHFSM.onClick.AddListener(LoadHFSM);
            }

            if (btnLLM != null)
            {
                btnLLM.onClick.RemoveListener(LoadLLM);
                btnLLM.onClick.AddListener(LoadLLM);
            }

            if (btnHybrid != null)
            {
                btnHybrid.onClick.RemoveListener(LoadHybrid);
                btnHybrid.onClick.AddListener(LoadHybrid);
            }
        }

        public void LoadHFSM()
        {
            StartCoroutine(LoadSceneRoutine(hfsmSceneName, "Loading HFSM Baseline Scene..."));
        }

        public void LoadLLM()
        {
            StartCoroutine(LoadSceneRoutine(llmSceneName, "Loading Pure LLM Scene..."));
        }

        public void LoadHybrid()
        {
            StartCoroutine(LoadSceneRoutine(hybridSceneName, "Loading Hybrid Agent Scene..."));
        }

        private IEnumerator LoadSceneRoutine(string sceneName, string statusMsg)
        {
            if (txtStatus != null)
            {
                txtStatus.text = statusMsg;
            }
            Debug.Log($"[MainMenuUI] {statusMsg} (Target: {sceneName})");

            yield return new WaitForSeconds(0.15f);

            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
            }
            else
            {
                string errMsg = $"[MainMenuUI] Error: Scene '{sceneName}' belum terdaftar di EditorBuildSettings!";
                Debug.LogError(errMsg);
                if (txtStatus != null)
                {
                    txtStatus.text = $"<color=#FF4444>{errMsg}</color>";
                }
            }
        }

        /// <summary>
        /// Membangun UI Canvas, Background, Title, dan 3 Tombol secara prosedural jika belum ada di hierarchy.
        /// </summary>
        public void AutoWireOrBuildUI()
        {
            // Cek apakah tombol sudah ada sebagai child
            var buttons = GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                string bName = b.gameObject.name.ToLowerInvariant();
                if (bName.Contains("hfsm") || bName.Contains("fsm")) btnHFSM = b;
                else if (bName.Contains("pure") || bName.Contains("llm")) btnLLM = b;
                else if (bName.Contains("hybrid")) btnHybrid = b;
            }

            if (btnHFSM != null && btnLLM != null && btnHybrid != null)
            {
                HookButtonEvents();
                return;
            }

            // Bangun Procedural Canvas
            BuildProceduralUI();
            HookButtonEvents();
        }

        private void BuildProceduralUI()
        {
            Canvas canvas = GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                var canvasGo = new GameObject("MainMenu_Canvas");
                canvasGo.transform.SetParent(transform, false);
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<GraphicRaycaster>();
            }

            // 1. Fullscreen Dark Background
            var bgGo = new GameObject("Background_Panel");
            bgGo.transform.SetParent(canvas.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.07f, 0.08f, 0.11f, 0.98f);

            // 2. Center Card Container
            var cardGo = new GameObject("Card_Container");
            cardGo.transform.SetParent(bgGo.transform, false);
            var cardRect = cardGo.AddComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(880f, 720f);

            var cardImg = cardGo.AddComponent<Image>();
            cardImg.color = new Color(0.11f, 0.13f, 0.18f, 0.95f);

            var cardOutline = cardGo.AddComponent<Outline>();
            cardOutline.effectColor = new Color(0.2f, 0.3f, 0.45f, 0.5f);
            cardOutline.effectDistance = new Vector2(2, -2);

            // 3. Header Title
            var titleGo = new GameObject("Title_Text");
            titleGo.transform.SetParent(cardGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -40f);
            titleRect.sizeDelta = new Vector2(-40f, 80f);

            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "<b>TACTICAL AI EXPERIMENT SELECTOR</b>";
            titleTmp.fontSize = 34;
            titleTmp.color = new Color(0.95f, 0.97f, 1.0f);
            titleTmp.alignment = TextAlignmentOptions.Center;

            // 4. Subtitle
            var subGo = new GameObject("Subtitle_Text");
            subGo.transform.SetParent(cardGo.transform, false);
            var subRect = subGo.AddComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0f, 1f);
            subRect.anchorMax = new Vector2(1f, 1f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.anchoredPosition = new Vector2(0f, -115f);
            subRect.sizeDelta = new Vector2(-60f, 50f);

            var subTmp = subGo.AddComponent<TextMeshProUGUI>();
            subTmp.text = "Sector 7 Overpass (2D Tactical Grid) — Evaluasi Komparatif Arsitektur Pengambil Keputusan";
            subTmp.fontSize = 17;
            subTmp.color = new Color(0.6f, 0.72f, 0.88f);
            subTmp.alignment = TextAlignmentOptions.Center;

            // 5. Button Container (Vertical)
            float startY = -210f;
            float btnHeight = 110f;
            float spacing = 25f;

            // --- Button 1: HFSM ---
            btnHFSM = CreateMenuButton(
                cardGo.transform,
                "Btn_HFSM",
                new Vector2(0f, startY),
                new Vector2(760f, btnHeight),
                "1. HFSM BASELINE",
                "Arsitektur Deterministic Hierarchical FSM murni berbasis aturan dan profil trait (X1 = HFSM)",
                new Color(0.12f, 0.28f, 0.45f),
                new Color(0.35f, 0.75f, 1.0f)
            );

            // --- Button 2: Pure LLM ---
            btnLLM = CreateMenuButton(
                cardGo.transform,
                "Btn_LLM",
                new Vector2(0f, startY - (btnHeight + spacing)),
                new Vector2(760f, btnHeight),
                "2. PURE LLM AGENT",
                "Keputusan otonom LLM dengan akses penuh ke seluruh fungsi entitas tanpa safeguard (X1 = Pure LLM, X2 = Unconstrained)",
                new Color(0.35f, 0.16f, 0.48f),
                new Color(0.85f, 0.45f, 1.0f)
            );

            // --- Button 3: Hybrid ---
            btnHybrid = CreateMenuButton(
                cardGo.transform,
                "Btn_Hybrid",
                new Vector2(0f, startY - (btnHeight + spacing) * 2f),
                new Vector2(760f, btnHeight),
                "3. HYBRID AGENT",
                "Penalaran taktis LLM dikawal oleh Action Feasibility Guardrail + 1x Retry + HFSM Fallback (X1 = Hybrid, X2 = Guardrail)",
                new Color(0.12f, 0.38f, 0.28f),
                new Color(0.35f, 0.95f, 0.65f)
            );

            // 6. Footer / Status Text
            var statusGo = new GameObject("Status_Text");
            statusGo.transform.SetParent(cardGo.transform, false);
            var statusRect = statusGo.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.anchoredPosition = new Vector2(0f, 25f);
            statusRect.sizeDelta = new Vector2(-60f, 40f);

            txtStatus = statusGo.AddComponent<TextMeshProUGUI>();
            txtStatus.text = "<color=#88AACC>Klik salah satu tombol untuk memuat skenario pertarungan.</color>";
            txtStatus.fontSize = 15;
            txtStatus.alignment = TextAlignmentOptions.Center;
        }

        private Button CreateMenuButton(
            Transform parent,
            string goName,
            Vector2 anchoredPos,
            Vector2 size,
            string titleText,
            string descriptionText,
            Color normalColor,
            Color accentColor)
        {
            var btnGo = new GameObject(goName);
            btnGo.transform.SetParent(parent, false);

            var rect = btnGo.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var img = btnGo.AddComponent<Image>();
            img.color = normalColor;

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = normalColor;
            colors.highlightedColor = normalColor * 1.35f;
            colors.pressedColor = normalColor * 0.85f;
            colors.selectedColor = normalColor;
            btn.colors = colors;

            var outline = btnGo.AddComponent<Outline>();
            outline.effectColor = accentColor;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            // Title Label
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(btnGo.transform, false);
            var titleRect = titleGo.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(25f, 0f);
            titleRect.offsetMax = new Vector2(-25f, -10f);

            var titleTmp = titleGo.AddComponent<TextMeshProUGUI>();
            titleTmp.text = $"<b>{titleText}</b>";
            titleTmp.fontSize = 24;
            titleTmp.color = Color.white;
            titleTmp.alignment = TextAlignmentOptions.MidlineLeft;

            // Description Label
            var descGo = new GameObject("Description");
            descGo.transform.SetParent(btnGo.transform, false);
            var descRect = descGo.AddComponent<RectTransform>();
            descRect.anchorMin = new Vector2(0f, 0f);
            descRect.anchorMax = new Vector2(1f, 0.5f);
            descRect.offsetMin = new Vector2(25f, 10f);
            descRect.offsetMax = new Vector2(-25f, 0f);

            var descTmp = descGo.AddComponent<TextMeshProUGUI>();
            descTmp.text = descriptionText;
            descTmp.fontSize = 14;
            descTmp.color = new Color(0.82f, 0.90f, 0.98f);
            descTmp.alignment = TextAlignmentOptions.MidlineLeft;
            descTmp.enableWordWrapping = true;

            return btn;
        }
    }
}

