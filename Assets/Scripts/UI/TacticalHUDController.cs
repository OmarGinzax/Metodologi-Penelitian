using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.UI
{
    /// <summary>
    /// World-space tactical HUD for Sector 7 Overpass.
    /// All elements are real GameObjects on a Screen-Space Overlay Canvas
    /// so you can edit layout/fonts/colours in the Inspector at design-time.
    ///
    /// Wired automatically by TestChamberHUDSetup editor script.
    /// BattleManager2D is auto-found if not assigned in Inspector.
    /// </summary>
    public class TacticalHUDController : MonoBehaviour
    {
        // ===================================================================
        // Inspector References  (assigned by TestChamberHUDSetup)
        // ===================================================================

        [Header("Data Source")]
        [Tooltip("Drag BattleManager2D here. Auto-found on Awake if empty.")]
        [SerializeField] private BattleManager2D battleManager;

        [Header("HUD Visibility & Toggle")]
        [SerializeField] private GameObject hudPanel;
        [SerializeField] private Button     btnToggleHUD;
        [SerializeField] private TMP_Text   txtBtnToggleHUD;

        [Header("Header Panel")]
        [SerializeField] private TMP_Text txtTitle;
        [SerializeField] private TMP_Text txtRoundInfo;

        [Header("Control Buttons")]
        [SerializeField] private Button    btnStep;
        [SerializeField] private TMP_Text  txtBtnStep;
        [SerializeField] private Button    btnAutoRun;
        [SerializeField] private TMP_Text  txtBtnAutoRun;

        [Header("Squad Status Panel")]
        [Tooltip("VerticalLayoutGroup parent. Rows are instantiated here at runtime.")]
        [SerializeField] private Transform  squadRowContainer;
        [Tooltip("Prefab: any GameObject with a TMP_Text child.")]
        [SerializeField] private GameObject squadRowPrefab;

        [Header("Combat Log Panel")]
        [SerializeField] private ScrollRect scrollLog;
        [SerializeField] private TMP_Text   txtCombatLog;
        [SerializeField] private int        logLinesToShow = 100;

        // ===================================================================
        // Runtime
        // ===================================================================

        private readonly List<TMP_Text> _squadRowTexts = new List<TMP_Text>();
        private bool _isUIVisible = true;
        private int _lastLogCount = -1;
        private const float HudWidth = 430f;
        private const float Margin = 12f;

        // ===================================================================
        // Lifecycle
        // ===================================================================

        private void Awake()
        {
            if (battleManager == null)
                battleManager = FindFirstObjectByType<BattleManager2D>();

            EnsureEventSystemRuntime();

            if (hudPanel == null)
            {
                var hp = transform.Find("HUD_Panel");
                if (hp != null) hudPanel = hp.gameObject;
            }

            if (btnToggleHUD == null)
            {
                var tb = transform.Find("Btn_ToggleHUD");
                if (tb != null) btnToggleHUD = tb.GetComponent<Button>();
            }

            if (scrollLog == null)
            {
                scrollLog = GetComponentInChildren<ScrollRect>(true);
            }

            if (scrollLog != null)
            {
                scrollLog.scrollSensitivity = 35f;
                var bg = scrollLog.GetComponent<Image>();
                if (bg != null) bg.raycastTarget = true;
                if (scrollLog.viewport != null)
                {
                    var vpBg = scrollLog.viewport.GetComponent<Image>();
                    if (vpBg != null) vpBg.raycastTarget = true;
                }
            }

            if (txtCombatLog == null && scrollLog != null && scrollLog.content != null)
            {
                txtCombatLog = scrollLog.content.GetComponentInChildren<TMP_Text>(true);
            }

            if (txtCombatLog != null)
            {
                var le = txtCombatLog.GetComponent<LayoutElement>();
                if (le != null && le.preferredHeight > 0 && le.preferredHeight < 100f)
                {
                    Destroy(le);
                }
                txtCombatLog.enableWordWrapping = true;
                txtCombatLog.overflowMode = TextOverflowModes.Overflow;
            }
        }

        private void Start()
        {
            if (battleManager == null)
                battleManager = FindFirstObjectByType<BattleManager2D>();

            EnsureEventSystemRuntime();

            if (btnStep != null)
            {
                btnStep.onClick.RemoveListener(OnStepClicked);
                btnStep.onClick.AddListener(OnStepClicked);
            }

            if (btnAutoRun != null)
            {
                btnAutoRun.onClick.RemoveListener(OnAutoRunClicked);
                btnAutoRun.onClick.AddListener(OnAutoRunClicked);
            }

            if (btnToggleHUD != null)
            {
                btnToggleHUD.onClick.RemoveListener(ToggleUIVisible);
                btnToggleHUD.onClick.AddListener(ToggleUIVisible);
            }

            RebuildSquadRows();
            SetUIVisible(true);
        }

        public static void EnsureEventSystemRuntime()
        {
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var existing = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
                if (existing == null)
                {
                    var esGo = new GameObject("[EventSystem]");
                    existing = esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();

#if ENABLE_INPUT_SYSTEM
                    esGo.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                    esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                    Debug.Log("[TacticalHUDController] Runtime created [EventSystem] with UI input module.");
                }
                else
                {
                    var module = existing.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                    if (module == null)
                    {
#if ENABLE_INPUT_SYSTEM
                        existing.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                        existing.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
                    }
                }
            }
        }

        private void Update()
        {
            HandleToggleInput();

            if (battleManager == null)
            {
                battleManager = FindFirstObjectByType<BattleManager2D>();
                if (battleManager == null) return;
            }

            if (_isUIVisible)
            {
                RefreshHeader();
                RefreshButtons();
                RefreshSquadRows();
                RefreshLog();
            }
        }

        // ===================================================================
        // HUD Visibility Controls (Button & Arrow Keys)
        // ===================================================================

        public void ToggleUIVisible()
        {
            SetUIVisible(!_isUIVisible);
        }

        public void SetUIVisible(bool visible)
        {
            _isUIVisible = visible;
            if (hudPanel != null)
            {
                hudPanel.SetActive(_isUIVisible);
            }

            if (txtBtnToggleHUD != null)
            {
                txtBtnToggleHUD.text = _isUIVisible ? "◄ Hide (H / ◄)" : "► Show UI (H / ►)";
            }

            if (btnToggleHUD != null)
            {
                var rt = btnToggleHUD.GetComponent<RectTransform>();
                if (rt != null)
                {
                    float targetX = _isUIVisible ? (HudWidth + Margin + 6f) : Margin;
                    rt.anchoredPosition = new Vector2(targetX, -Margin);
                }
            }
        }

        private void HandleToggleInput()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame) SetUIVisible(false);
                if (kb.rightArrowKey.wasPressedThisFrame) SetUIVisible(true);
                if (kb.hKey.wasPressedThisFrame) ToggleUIVisible();
            }
#else
            try
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow)) SetUIVisible(false);
                if (Input.GetKeyDown(KeyCode.RightArrow)) SetUIVisible(true);
                if (Input.GetKeyDown(KeyCode.H)) ToggleUIVisible();
            }
            catch (System.Exception) { }
#endif
        }

        // ===================================================================
        // Button callbacks
        // ===================================================================

        private void OnStepClicked()
        {
            Debug.Log("[TacticalHUDController] 'Step Turn' clicked.");
            if (battleManager != null) battleManager.StepSingleTurn();
        }

        private void OnAutoRunClicked()
        {
            if (battleManager != null)
            {
                battleManager.AutoRun = !battleManager.AutoRun;
                Debug.Log($"[TacticalHUDController] 'Auto Run' clicked. Current state: {battleManager.AutoRun}");
            }
        }

        // ===================================================================
        // Refresh helpers
        // ===================================================================

        private void RefreshHeader()
        {
            if (txtTitle != null)
                txtTitle.text = "SECTOR 7 OVERPASS  TACTICAL HUD";

            if (txtRoundInfo != null)
            {
                string controller = battleManager.ActiveAgentController != null
                    ? battleManager.ActiveAgentController.ControllerType
                    : "NoController";

                txtRoundInfo.text =
                    string.Format("Round {0}  |  Turn {1}  |  {2}  |  {3}",
                        battleManager.RoundNumber,
                        battleManager.GlobalTurnCounter,
                        battleManager.Outcome,
                        controller);
            }
        }

        private void RefreshButtons()
        {
            bool canStep = battleManager.Outcome == BattleOutcome.InProgress
                        && !battleManager.IsTurnInProgress;

            if (btnStep  != null) btnStep.interactable  = canStep;
            if (txtBtnStep != null)
                txtBtnStep.text = battleManager.IsTurnInProgress ? "Processing..." : "Step Turn";

            if (btnAutoRun != null)
                btnAutoRun.interactable = battleManager.Outcome == BattleOutcome.InProgress;
            if (txtBtnAutoRun != null)
                txtBtnAutoRun.text = battleManager.AutoRun ? "Stop Auto" : "Auto Run";
        }

        private void RebuildSquadRows()
        {
            if (squadRowContainer == null || squadRowPrefab == null) return;

            foreach (Transform child in squadRowContainer)
                Destroy(child.gameObject);
            _squadRowTexts.Clear();

            if (battleManager == null) return;

            foreach (var c in battleManager.Combatants)
            {
                if (c == null || c.Team != TeamSide.PlayerTeam) continue;

                var row = Instantiate(squadRowPrefab, squadRowContainer);
                var le = row.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = 42f;

                var lbl = row.GetComponentInChildren<TMP_Text>();
                if (lbl != null)
                {
                    lbl.name = "lbl_" + c.EntityId;
                    _squadRowTexts.Add(lbl);
                }
            }
        }

        private void RefreshSquadRows()
        {
            if (battleManager == null) return;

            int idx = 0;
            foreach (var c in battleManager.Combatants)
            {
                if (c == null || c.Team != TeamSide.PlayerTeam) continue;
                if (idx >= _squadRowTexts.Count) break;

                var lbl = _squadRowTexts[idx++];
                if (lbl == null) continue;

                // Extract clean unit name (e.g. "Marcus" from "Marcus (Pointman)")
                string name = c.EntityName;
                if (name.Contains(" ("))
                    name = name.Substring(0, name.IndexOf(" (")).Trim();

                string trait = !string.IsNullOrEmpty(c.CurrentTraitId) ? c.CurrentTraitId : "None";

                if (!c.IsAlive)
                {
                    lbl.text =
                        $"<color=#888888><b>{name}</b> ({trait}) - HP 0/{c.Stats.MaxHP}</color>\n" +
                        $"<color=#FF4444>[K.I.A.] - Unit Eliminated</color>";
                    continue;
                }

                if (c.IsExtracted)
                {
                    lbl.text =
                        $"<color=#88CCFF><b>{name}</b> ({trait}) - HP {c.Stats.CurrentHP}/{c.Stats.MaxHP}</color>\n" +
                        $"<color=#44FFCC>[EXTRACTED] - Safely Evacuated</color>";
                    continue;
                }

                string coverColour;
                string coverLabel;
                switch (c.ActiveCoverType)
                {
                    case CoverType.FullCover:
                        coverColour = "#44CCFF"; coverLabel = "Full Cover"; break;
                    case CoverType.HalfCover:
                        coverColour = "#AADDFF"; coverLabel = "Half Cover"; break;
                    default:
                        coverColour = "#888888"; coverLabel = "No Cover";   break;
                }

                string flankedTag = c.IsFlanked ? " <color=#FF8833>[FLANKED]</color>" : "";
                string guardTag = c.IsGuarded && c.GuardedBy != null 
                    ? $" <color=#44CCFF>[GUARDED ({c.GuardTurnsRemaining}t)]</color>" 
                    : "";

                string statusText = c.IsFleeing 
                    ? "<color=#FFAA33>[FLEEING] Retreating to Evac</color>" 
                    : (c.IsDefending ? "Defending (Hunker Down)" : $"<color=#CCCCCC>{c.CurrentActionType}</color>");

                string hpCol = c.Stats.CurrentHP > c.Stats.MaxHP * 0.5f 
                    ? "#33FF77" 
                    : (c.Stats.CurrentHP > c.Stats.MaxHP * 0.25f ? "#FFCC33" : "#FF4444");

                string ratCol = c.Stats.Rationality >= 75 ? "#88CCFF" : (c.Stats.Rationality >= 40 ? "#FFCC33" : "#FF6633");
                string ratInfo = c.IsImmuneToPsychologicalEffects
                    ? "<color=#88CCFF>RAT [IMMUNE]</color>"
                    : $"<color={ratCol}>RAT {c.Stats.Rationality}</color> <color=#AAAAAA>[{c.Stats.ComposureLevel}]</color>";

                // 2 Baris saja:
                // Baris 1: nama (trait) - HP - RAT [Composure]
                // Baris 2: Cover [FLANKED] [GUARDED] - status
                lbl.text =
                    $"<b>{name}</b> ({trait}) - HP <color={hpCol}>{c.Stats.CurrentHP}/{c.Stats.MaxHP}</color> | {ratInfo}\n" +
                    $"<color={coverColour}>{coverLabel}</color>{flankedTag}{guardTag} - {statusText}";
            }
        }

        private void RefreshLog()
        {
            if (txtCombatLog == null || battleManager == null) return;

            var logs = battleManager.CombatLogs;
            if (logs.Count == _lastLogCount) return;
            _lastLogCount = logs.Count;

            int start = Mathf.Max(0, logs.Count - logLinesToShow);
            var sb = new StringBuilder();
            for (int i = start; i < logs.Count; i++)
                sb.AppendLine("> " + logs[i]);

            txtCombatLog.text = sb.ToString();
            txtCombatLog.ForceMeshUpdate();

            // Auto-scroll to bottom on new combat log entry & ensure content height accommodates text
            if (scrollLog != null && scrollLog.content != null)
            {
                float prefH = txtCombatLog.preferredHeight;
                float viewH = scrollLog.viewport != null ? scrollLog.viewport.rect.height : 180f;
                float targetH = Mathf.Max(viewH, prefH + 24f);

                // Explicitly set content sizeDelta height to guarantee ScrollRect can scroll
                var sd = scrollLog.content.sizeDelta;
                scrollLog.content.sizeDelta = new Vector2(sd.x, targetH);

                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollLog.content);

                scrollLog.verticalNormalizedPosition = 0f;
            }
        }

        private static string Trunc(string s, int max)
            => (s != null && s.Length > max) ? s.Substring(0, max) + "..." : (s ?? "");
    }
}
