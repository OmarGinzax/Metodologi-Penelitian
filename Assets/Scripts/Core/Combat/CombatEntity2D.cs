using System;
using System.Collections;
using UnityEngine;
using RPGStrategy.Demo;

namespace RPGStrategy.Core.Combat
{
    /// <summary>
    /// Entitas tempur 2D untuk prajurit regu dan musuh di TacticalGrid2D.
    /// Menyediakan telemetri aksi langsung di Inspector Unity (currentActionType, target, cover),
    /// animasi gerak meluncur halus (tweening), dan perhitungan reduksi cover/flanking.
    /// </summary>
    public class CombatEntity2D : MonoBehaviour
    {
        [Header("=== IDENTITY & ROLE ===")]
        [SerializeField] private string entityId = "unit_01";
        [SerializeField] private string entityName = "Vanguard";
        [SerializeField] private TeamSide team = TeamSide.PlayerTeam;
        [SerializeField] private string currentTraitId = "DisciplinedProfessional";

        [Header("=== WEAPON & COMBAT RANGES ===")]
        [SerializeField] private string weaponName = "Vector Tactical Rifle";
        [SerializeField] private int attackRange = 5;
        [SerializeField] private int moveRange = 3;

        [Header("=== LIVE ACTION TELEMETRY (INSPECTOR) ===")]
        [SerializeField] private string currentActionType = "IdleWaiting";
        [SerializeField] private string currentActionDescription = "Ready for combat turn.";
        [SerializeField] private string currentTargetId = "";
        [SerializeField] private Vector2Int targetGridCoordinates = new Vector2Int(-1, -1);
        [SerializeField] private bool isActing = false;

        [Header("=== SPATIAL & COVER STATE ===")]
        [SerializeField] private Vector2Int gridPosition;
        [SerializeField] private bool inCover = false;
        [SerializeField] private CoverType activeCoverType = CoverType.None;
        [SerializeField] private bool isFlanked = false;
        [SerializeField] private bool isDefending = false;
        [SerializeField] private bool isFleeing = false;
        [SerializeField] private bool isExtracted = false;

        [Header("=== SUPPORT SKILLS & STATUS EFFECTS ===")]
        [SerializeField] private CombatEntity2D guardedBy = null;
        [SerializeField] private int guardTurnsRemaining = 0;
        [SerializeField] private int skillCooldownTurns = 0;

        [Header("=== PSYCHOLOGICAL & RATIONALITY STATUS ===")]
        [SerializeField] private bool isImmuneToPsychologicalEffects = false;

        [Header("=== CORE STATS ===")]
        [SerializeField] private EntityStats stats = new EntityStats(100, 50, 20, 10, 12, 80);

        private SpriteRenderer _spriteRenderer;
        private Coroutine _moveCoroutine;

        // Properti Publik
        public string EntityId => entityId;
        public string EntityName => entityName;
        public TeamSide Team => team;
        public string CurrentTraitId => currentTraitId;
        public string WeaponName => weaponName;
        public int AttackRange => attackRange;
        public int MoveRange => moveRange;
        public Vector2Int GridPosition { get => gridPosition; set => gridPosition = value; }
        public bool InCover => inCover;
        public CoverType ActiveCoverType => activeCoverType;
        public bool IsFlanked => isFlanked;
        public bool IsDefending => isDefending;
        public bool IsFleeing => isFleeing;
        public bool IsExtracted => isExtracted;
        public bool IsImmuneToPsychologicalEffects
        {
            get => isImmuneToPsychologicalEffects;
            set => isImmuneToPsychologicalEffects = value;
        }
        public bool IsImmuneToRationality
        {
            get => isImmuneToPsychologicalEffects;
            set => isImmuneToPsychologicalEffects = value;
        }
        public CombatEntity2D GuardedBy => guardedBy;
        public int GuardTurnsRemaining => guardTurnsRemaining;
        public bool IsGuarded => guardedBy != null && guardedBy.IsAlive && guardTurnsRemaining > 0;
        public int SkillCooldownTurns => skillCooldownTurns;
        public bool IsSkillReady => skillCooldownTurns <= 0;
        public bool LastDamageWasRedirected { get; private set; } = false;
        public CombatEntity2D LastGuardian { get; private set; } = null;
        public EntityStats Stats => stats;
        public bool IsAlive => stats != null && stats.IsAlive;
        public bool IsActing => isActing;

        // Telemetri Inspector Akses
        public string CurrentActionType => currentActionType;
        public string CurrentActionDescription => currentActionDescription;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer == null)
            {
                _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }
        }

        public void Initialize(
            string id, 
            string name, 
            TeamSide side, 
            EntityStats initialStats, 
            string trait, 
            Vector2Int startGridPos, 
            string weapon = "Vector Tactical Rifle", 
            int atkRng = 5, 
            int mvRng = 3,
            bool immuneToPsychological = false)
        {
            entityId = id;
            entityName = name;
            team = side;
            stats = initialStats?.Clone() ?? new EntityStats();
            currentTraitId = trait;
            gridPosition = startGridPos;
            weaponName = weapon;
            attackRange = atkRng;
            moveRange = mvRng;
            isImmuneToPsychologicalEffects = immuneToPsychological;

            currentActionType = "IdleWaiting";
            currentActionDescription = "Unit standing by.";
            currentTargetId = "";
            targetGridCoordinates = new Vector2Int(-1, -1);
            isActing = false;
            inCover = false;
            activeCoverType = CoverType.None;
            isFlanked = false;
            isDefending = false;
            isFleeing = false;
            isExtracted = false;
            guardedBy = null;
            guardTurnsRemaining = 0;
            skillCooldownTurns = 0;
            LastDamageWasRedirected = false;
            LastGuardian = null;

            if (stats != null && !stats.IsAlive)
            {
                ApplyDeathVisuals();
            }
        }

        // =========================================================================
        // TELEMETRI LANGSUNG UNTUK INSPECTOR
        // =========================================================================

        public void SetActionTelemetry(string actionType, string description, string targetId = "", Vector2Int targetCoords = default)
        {
            currentActionType = actionType;
            currentActionDescription = description;
            currentTargetId = targetId;
            targetGridCoordinates = targetCoords;
            isActing = actionType != "IdleWaiting" && actionType != "TurnEnded";
        }

        public void SetDefending(bool defending)
        {
            isDefending = defending;
            if (defending)
            {
                SetActionTelemetry("Defending_HunkerDown", "Fortified in cover; incoming damage reduced 50%.");
            }
        }

        public void SetFleeing(bool fleeing)
        {
            isFleeing = fleeing;
            if (fleeing)
            {
                SetActionTelemetry("Fleeing", "Unit critical; retreating towards extraction zone.", "", targetGridCoordinates);
            }
        }

        public void SetExtracted()
        {
            isExtracted = true;
            isFleeing = false;
            isDefending = false;
            SetActionTelemetry("Extracted", "Unit safely evacuated via Extraction Zone.");

            if (_spriteRenderer != null)
            {
                _spriteRenderer.enabled = false;
            }
            gameObject.SetActive(false);
        }

        public void ApplyGuard(CombatEntity2D guardian, int turns = 2)
        {
            guardedBy = guardian;
            guardTurnsRemaining = turns;
        }

        public void DecrementGuardDuration()
        {
            if (guardTurnsRemaining > 0)
            {
                guardTurnsRemaining--;
                if (guardTurnsRemaining <= 0)
                {
                    guardedBy = null;
                }
            }
        }

        public void ClearGuard()
        {
            guardedBy = null;
            guardTurnsRemaining = 0;
        }

        public void PutSkillOnCooldown(int turns = 2)
        {
            skillCooldownTurns = turns;
        }

        public void DecrementSkillCooldown()
        {
            if (skillCooldownTurns > 0)
            {
                skillCooldownTurns--;
            }
        }

        public void ReduceRationality(int amount)
        {
            if (isImmuneToPsychologicalEffects || stats == null) return;
            stats.ReduceRationality(amount);
        }

        public bool CheckAndApplyFleeAbort(float hpThresh, float ratThresh)
        {
            if (!isFleeing || !IsAlive || isExtracted) return false;

            float hpRatio = stats.HPRatio;
            float ratRatio = (float)stats.Rationality / 100f;
            bool ratOk = isImmuneToPsychologicalEffects || ratRatio > (ratThresh + 0.0001f);

            if (hpRatio > (hpThresh + 0.0001f) && ratOk)
            {
                SetFleeing(false);
                SetActionTelemetry("Rallied", "Health and composure restored above retreat threshold; returning to combat.");
                return true;
            }
            return false;
        }

        public void UpdateCoverStatus(TacticalGrid2D grid, CombatEntity2D nearestThreat)
        {
            if (grid == null) return;

            if (nearestThreat != null)
            {
                // XCOM-style: cover is relative to attacker direction — purely edge-based
                activeCoverType = grid.GetCoverAgainst(gridPosition, nearestThreat.GridPosition);
                isFlanked       = grid.IsFlanked(gridPosition, nearestThreat.GridPosition);
            }
            else
            {
                // No known threat — report max potential cover from any direction
                activeCoverType = CoverType.None;
                var edges = grid.GetCellCoverEdges(gridPosition);
                if (edges != null)
                {
                    foreach (var e in edges)
                        if (e > activeCoverType) activeCoverType = e;
                }
                isFlanked = false;
            }

            inCover = activeCoverType != CoverType.None;
        }

        // =========================================================================
        // PERGERAKAN HALUS (SMOOTH TWEENING)
        // =========================================================================

        public void MoveTo(Vector2Int newGridPos, TacticalGrid2D grid, Action onComplete = null)
        {
            if (grid == null) return;

            if (_moveCoroutine != null)
            {
                StopCoroutine(_moveCoroutine);
            }

            grid.ClearOccupant(gridPosition);
            gridPosition = grid.ClampToBounds(newGridPos);
            grid.SetOccupant(gridPosition, this);

            Vector3 targetWorldPos = grid.GridToWorld(gridPosition);
            SetActionTelemetry("MovingToCover", $"Repositioning to ({gridPosition.x}, {gridPosition.y}).", "", gridPosition);

            _moveCoroutine = StartCoroutine(AnimateMoveRoutine(targetWorldPos, onComplete));
        }

        private IEnumerator AnimateMoveRoutine(Vector3 targetWorldPos, Action onComplete)
        {
            Vector3 startPos = transform.position;
            float elapsed = 0f;
            float duration = 0.25f; // Gerakan gesit taktis

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetWorldPos, t);
                yield return null;
            }

            transform.position = targetWorldPos;
            _moveCoroutine = null;
            onComplete?.Invoke();
        }

        // =========================================================================
        // COMBAT & DAMAGE MITIGATION (COVER & FLANKING)
        // =========================================================================

        public int ApplyDamage(int rawDamage, bool attackIsFlanking = false)
        {
            LastDamageWasRedirected = false;
            LastGuardian = null;

            if (!IsAlive) return 0;

            int effectiveAtk = rawDamage;
            float coverMitigation = 0f;

            if (!attackIsFlanking)
            {
                if (isDefending)
                {
                    coverMitigation = 0.50f; // Hunker Down 50%
                }
                else if (activeCoverType == CoverType.HalfCover)
                {
                    coverMitigation = 0.25f; // Half-Cover 25%
                }
                else if (activeCoverType == CoverType.FullCover)
                {
                    coverMitigation = 0.50f; // Full-Cover 50%
                }
            }
            else
            {
                // Flanking attack: mengabaikan cover dan menambah damage 20%
                effectiveAtk = Mathf.RoundToInt(rawDamage * 1.20f);
            }

            int mitigatedAtk = Mathf.RoundToInt(effectiveAtk * (1f - coverMitigation));
            int defMitigation = stats.DefensePower / 2;
            int finalDmg = Mathf.Max(1, mitigatedAtk - defMitigation);

            // Redirection jika target sedang dilindungi Guard
            if (IsGuarded && guardedBy != null && guardedBy.IsAlive && guardedBy != this)
            {
                LastDamageWasRedirected = true;
                LastGuardian = guardedBy;
                guardedBy.TakeRedirectedDamage(finalDmg, this);
                StartCoroutine(HitFlashRoutine());
                return finalDmg;
            }

            stats.CurrentHP -= finalDmg;

            // Trigger visual hit flash
            StartCoroutine(HitFlashRoutine());

            if (!IsAlive)
            {
                SetActionTelemetry("KIA_Destroyed", "Unit eliminated in combat.");
                ApplyDeathVisuals();
            }

            return finalDmg;
        }

        public void TakeRedirectedDamage(int damage, CombatEntity2D protectedAlly)
        {
            if (!IsAlive) return;
            stats.CurrentHP -= damage;
            StartCoroutine(HitFlashRoutine());
            SetActionTelemetry("Guard_Intercept", $"Intercepted {damage} damage for {protectedAlly.EntityName}.");
            if (!IsAlive)
            {
                SetActionTelemetry("KIA_Destroyed", "Unit eliminated taking damage for ally.");
                ApplyDeathVisuals();
            }
        }

        /// <summary>
        /// Mengganti sprite unit menjadi aset prajurit gugur / rongsokan musuh saat tereliminasi.
        /// </summary>
        public void ApplyDeathVisuals()
        {
            if (_spriteRenderer == null) _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_spriteRenderer != null)
            {
                if (team == TeamSide.PlayerTeam)
                {
                    _spriteRenderer.sprite = TacticalSpriteFactory.CreateDeadAgentSprite();
                }
                else
                {
                    _spriteRenderer.sprite = TacticalSpriteFactory.CreateDeadEnemySprite();
                }
                _spriteRenderer.sortingOrder = 5; // Turun ke layer lantai di bawah unit yang masih aktif
                _spriteRenderer.color = new Color(0.85f, 0.85f, 0.88f, 0.95f);
            }
        }

        public void Heal(int amount)
        {
            if (!IsAlive || amount <= 0) return;
            stats.CurrentHP = Mathf.Min(stats.MaxHP, stats.CurrentHP + amount);
        }

        private IEnumerator HitFlashRoutine()
        {
            if (_spriteRenderer != null)
            {
                Color original = _spriteRenderer.color;
                _spriteRenderer.color = Color.white;
                yield return new WaitForSeconds(0.08f);
                _spriteRenderer.color = original;
            }
        }
    }
}
