using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using RPGStrategy.Core.Command;
using RPGStrategy.Core.Data;
using RPGStrategy.Core.Extractor;
using RPGStrategy.AI.Traits;

namespace RPGStrategy.Core.Combat
{
    public class BattleManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private int maxTurns = 50;

        [Header("Combatants")]
        [SerializeField] private List<CombatEntity> combatants = new List<CombatEntity>();

        private readonly TurnCoordinator _turnCoordinator = new TurnCoordinator();
        private readonly CombatStateExtractor _stateExtractor = new CombatStateExtractor();
        private readonly PersonalityEvolutionEngine _evolutionEngine = new PersonalityEvolutionEngine();
        private readonly List<string> _recentEvents = new List<string>();

        private BattleState _currentState = BattleState.Uninitialized;
        private BattleOutcome _outcome = BattleOutcome.InProgress;
        private int _globalTurnCounter = 0;
        private CommanderDirectiveDTO _activeDirective;

        public BattleState CurrentState => _currentState;
        public BattleOutcome Outcome => _outcome;
        public int GlobalTurnCounter => _globalTurnCounter;
        public int RoundNumber => _turnCoordinator.RoundNumber;
        public IReadOnlyList<CombatEntity> Combatants => combatants;
        public IReadOnlyList<string> RecentEvents => _recentEvents;
        public CommanderDirectiveDTO ActiveDirective => _activeDirective;

        public event Action<CombatStateSnapshot> OnTurnStarted;
        public event Action<ActionExecutionResult> OnActionExecuted;
        public event Action<BattleOutcome> OnBattleEnded;
        public event Action<TraitShiftResult> OnTraitShifted;

        public void SetCommanderDirective(DirectiveType type, string targetId, string text)
        {
            _activeDirective = new CommanderDirectiveDTO(type, targetId, text, _globalTurnCounter);
            AddEventLog($"[COMMAND BROADCAST] \"{text}\" ({type})");
        }

        public void InitializeBattle(IEnumerable<CombatEntity> initialCombatants, int maxTurnLimit = 50)
        {
            combatants = initialCombatants?.Where(c => c != null).ToList() ?? new List<CombatEntity>();
            maxTurns = maxTurnLimit;
            _globalTurnCounter = 0;
            _recentEvents.Clear();
            _activeDirective = new CommanderDirectiveDTO(DirectiveType.FreeEngage, "", "Engage enemy at your discretion.", 0);
            _outcome = BattleOutcome.InProgress;

            _currentState = BattleState.RoundStarting;
            _turnCoordinator.StartNewRound(combatants);
            AddEventLog("Battle initialized.");
        }

        public async Task<BattleOutcome> RunBattleToEndAsync(CancellationToken cancellationToken = default)
        {
            while (_outcome == BattleOutcome.InProgress && !cancellationToken.IsCancellationRequested)
            {
                await ExecuteSingleTurnAsync(cancellationToken);
            }
            return _outcome;
        }

        public async Task<ActionExecutionResult> ExecuteSingleTurnAsync(CancellationToken cancellationToken = default)
        {
            _outcome = CheckBattleOutcome();
            if (_outcome != BattleOutcome.InProgress)
            {
                _currentState = BattleState.BattleOver;
                OnBattleEnded?.Invoke(_outcome);
                return null;
            }

            CombatEntity currentActor = _turnCoordinator.CurrentActor;
            if (currentActor == null || !currentActor.IsAlive)
            {
                bool hasMoreActors = _turnCoordinator.AdvanceToNextActor();
                if (!hasMoreActors)
                {
                    _turnCoordinator.StartNewRound(combatants);
                }
                currentActor = _turnCoordinator.CurrentActor;
            }

            if (currentActor == null || !currentActor.IsAlive)
            {
                _outcome = CheckBattleOutcome();
                return null;
            }

            _globalTurnCounter++;
            _currentState = BattleState.TurnStarting;
            currentActor.StartTurn();

            var statusLogs = currentActor.ProcessTurnStatusEffects();
            foreach (var log in statusLogs)
            {
                AddEventLog(log);
            }

            if (!currentActor.IsAlive)
            {
                AddEventLog($"{currentActor.EntityName} succumbed to status effects.");
                NotifyTeammatesOfDefeat(currentActor);
                _turnCoordinator.PruneDeadActors();
                _turnCoordinator.AdvanceToNextActor();
                _outcome = CheckBattleOutcome();
                return null;
            }

            // Ekstraksi State Snapshot (termasuk perintah Komandan saat ini)
            _currentState = BattleState.StateExtraction;
            var snapshot = _stateExtractor.ExtractSnapshot(combatants, currentActor, _globalTurnCounter, _recentEvents, _activeDirective);
            OnTurnStarted?.Invoke(snapshot);

            // Permintaan Keputusan dari Kontroler AI
            _currentState = BattleState.WaitingForDecision;
            AgentDecision decision;

            if (currentActor.AssignedController != null)
            {
                decision = await currentActor.AssignedController.DecideActionAsync(snapshot, cancellationToken);
            }
            else
            {
                var enemy = combatants.FirstOrDefault(c => c.Team != currentActor.Team && c.IsAlive);
                decision = new AgentDecision(currentActor.EntityId, "BasicAttack", enemy?.EntityId ?? string.Empty, "Default fallback");
            }

            if (decision == null)
            {
                decision = new AgentDecision(currentActor.EntityId, "Action_Defend", currentActor.EntityId, "Null decision fallback");
            }

            // Catat radio chatter kepatuhan/pembangkangan jika ada
            if (!string.IsNullOrEmpty(decision.RadioChatter))
            {
                AddEventLog($"[RADIO: {decision.Compliance}] {currentActor.EntityName}: \"{decision.RadioChatter}\"");
            }

            // Resolusi Aksi
            _currentState = BattleState.ResolvingAction;
            var result = ProcessActionResolution(currentActor, decision);
            OnActionExecuted?.Invoke(result);

            // Cek Outcome & Majukan Giliran
            _currentState = BattleState.CheckingOutcome;
            _turnCoordinator.PruneDeadActors();
            _turnCoordinator.AdvanceToNextActor();

            _outcome = CheckBattleOutcome();
            if (_outcome != BattleOutcome.InProgress)
            {
                _currentState = BattleState.BattleOver;
                OnBattleEnded?.Invoke(_outcome);
            }

            return result;
        }

        public ActionExecutionResult ProcessActionResolution(CombatEntity actor, AgentDecision decision)
        {
            if (actor == null || decision == null)
            {
                return ActionExecutionResult.CreateFailure("Unknown", "None", "None", "Invalid actor or decision.");
            }

            string actionId = decision.ActionId;
            string targetId = decision.TargetId;
            bool isDemoralized = actor.HasStatusEffect(StatusEffectType.Demoralized);

            // =========================================================
            // 1. AKSI STANDAR & RETREAT
            // =========================================================
            if (actionId == "Action_Defend")
            {
                actor.SetDefending(true);
                string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} took Defensive Stance behind barricade.";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, actor.EntityId, 0, 0, desc);
            }

            if (actionId == "TacticalRetreat")
            {
                actor.SetRetreating(true);
                actor.FieldDistance = Mathf.Max(0f, actor.FieldDistance - 10f); // Bergerak 10m mendekati titik jemput transport
                string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} executed Tactical Retreat toward transport (Pos: {actor.FieldDistance:F1}m).";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, actor.EntityId, 0, 0, desc);
            }

            if (actionId == "BasicAttack")
            {
                var target = FindEntity(targetId);
                if (target == null || !target.IsAlive)
                {
                    string failDesc = $"Turn {_globalTurnCounter}: {actor.EntityName}'s shot missed (Target dead/missing).";
                    AddEventLog(failDesc);
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, failDesc);
                }

                int rawDmg = actor.Stats.AttackPower;
                int actualDmg = DeliverDamageWithCover(actor, target, rawDmg, isDemoralized, out string coverDesc);
                string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} attacked {target.EntityName} dealing {actualDmg} damage.{coverDesc}";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, actualDmg, 0, desc);
            }

            // =========================================================
            // 2. AKSI TAKTIS MILITER LANJUTAN
            // =========================================================
            if (actionId.StartsWith("Carry_") || actionId == "CarryWounded")
            {
                string woundedId = actionId.StartsWith("Carry_") ? actionId.Substring("Carry_".Length) : targetId;
                var wounded = FindEntity(woundedId);
                if (wounded == null || !wounded.IsAlive || !actor.CanReach(wounded))
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, woundedId, "Cannot reach wounded comrade.");
                }

                // Pindahkan korban ke posisi aman bersama aktor
                wounded.FieldDistance = actor.FieldDistance;
                wounded.ApplyStatusEffect(StatusEffectType.Carried, 1, 0);
                actor.SetCarrying(wounded.EntityId);

                // Evaluasi momen heroik bagi medic
                var shiftResult = _evolutionEngine.EvaluateMiracleRescue(actor, wounded);
                if (shiftResult.ShiftOccurred)
                {
                    OnTraitShifted?.Invoke(shiftResult);
                    AddEventLog(shiftResult.RadioMonologue);
                }

                string desc = $"Turn {_globalTurnCounter}: [CARRY WOUNDED] {actor.EntityName} ran under fire, dragging {wounded.EntityName} to safety behind cover!";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, wounded.EntityId, 0, 0, desc);
            }

            if (actionId == "DecoyRun")
            {
                actor.ApplyStatusEffect(StatusEffectType.DecoyActive, 1, 30);
                string desc = $"Turn {_globalTurnCounter}: [DECOY RUN] {actor.EntityName} sprinted into the open, firing flares to draw all enemy targeting!";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, actor.EntityId, 0, 0, desc);
            }

            if (actionId == "FlankManeuver")
            {
                var target = FindEntity(targetId);
                if (target == null || !target.IsAlive)
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Target dead.");
                }

                int rawDmg = Mathf.RoundToInt(actor.Stats.AttackPower * 1.35f);
                int actualDmg = target.ApplyDamage(rawDmg, isDemoralized); // Bypass cover
                string desc = $"Turn {_globalTurnCounter}: [FLANK MANEUVER] {actor.EntityName} bypassed {target.EntityName}'s cover, dealing {actualDmg} flanking critical damage!";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, actualDmg, 0, desc);
            }

            if (actionId == "Overwatch")
            {
                actor.SetOverwatch(true);
                actor.ApplyStatusEffect(StatusEffectType.OverwatchActive, 1, 0);
                string desc = $"Turn {_globalTurnCounter}: [OVERWATCH] {actor.EntityName} locked their weapon onto the kill-zone, holding for reaction shots.";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, actor.EntityId, 0, 0, desc);
            }

            // =========================================================
            // 3. PENGORBANAN REALISTIS (BERBASIS JARAK PROXIMITY)
            // =========================================================
            if (actionId.StartsWith("Cover_") || actionId == "CoverAlly")
            {
                string allyId = actionId.StartsWith("Cover_") ? actionId.Substring("Cover_".Length) : targetId;
                var ally = FindEntity(allyId);
                if (ally == null || !ally.IsAlive || !actor.CanReach(ally))
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, allyId, "Ally out of proximity range.");
                }

                actor.SetCoveringTarget(ally.EntityId);
                ally.SetCoveredBy(actor.EntityId);
                string relType = actor.Relationships.GetRelationshipType(ally.EntityId).ToString();
                string desc = $"Turn {_globalTurnCounter}: [BODYGUARD COVER] {actor.EntityName} dashed {actor.DistanceTo(ally):F1}m to shield {ally.EntityName} (Bond: {relType})!";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, ally.EntityId, 0, 0, desc);
            }

            if (actionId.StartsWith("Sacrifice_") || actionId == "DesperateSacrifice")
            {
                string allyId = actionId.StartsWith("Sacrifice_") ? actionId.Substring("Sacrifice_".Length) : targetId;
                var ally = FindEntity(allyId);
                if (ally == null || !ally.IsAlive || !actor.CanReach(ally))
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, allyId, "Ally unreachable for sacrifice.");
                }

                // Pengorbanan murni: kurangi 50% HP aktor (jika HP tinggal 5, sisa 2 HP; jika fatal, gugur!)
                int hpTransfused = Mathf.Max(1, Mathf.RoundToInt(actor.Stats.CurrentHP * 0.5f));
                actor.Stats.CurrentHP -= hpTransfused;
                int healed = ally.ApplyHeal(hpTransfused * 2);
                ally.ApplyMPRestore(ally.Stats.MaxMP);
                ally.ModifyRationality(40);

                string relType = actor.Relationships.GetRelationshipType(ally.EntityId).ToString();
                string desc = $"Turn {_globalTurnCounter}: [HEROIC SACRIFICE] {actor.EntityName} threw themselves to shield {ally.EntityName} ({relType}), transfusing {healed} HP and full MP!";
                
                if (!actor.IsAlive)
                {
                    desc += $" {actor.EntityName} was KILLED IN ACTION saving their comrade!";
                    NotifyTeammatesOfDefeat(actor);
                }

                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, ally.EntityId, 0, healed, desc);
            }

            // =========================================================
            // 4. AKSI EMOSIONAL & PSIKOLOGIS LAINNYA
            // =========================================================
            if (actionId == "RushIn")
            {
                var target = FindEntity(targetId);
                if (target == null || !target.IsAlive)
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "RushIn target invalid.");
                }

                actor.SetVulnerable(1);
                int rawDmg = Mathf.RoundToInt(actor.Stats.AttackPower * 1.5f);
                int actualDmg = DeliverDamageWithCover(actor, target, rawDmg, isDemoralized, out string coverDesc);
                string desc = $"Turn {_globalTurnCounter}: [RUSH IN] {actor.EntityName} recklessly charged {target.EntityName} dealing {actualDmg} damage!{coverDesc}";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, actualDmg, 0, desc);
            }

            if (actionId == "Execute")
            {
                var target = FindEntity(targetId);
                if (target == null || !target.IsAlive || target.Stats.HPRatio > 0.25f || !actor.ConsumeMP(15))
                {
                    return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Conditions not met for Execute.");
                }

                int rawDmg = Mathf.RoundToInt(actor.Stats.AttackPower * 2.5f);
                int actualDmg = DeliverDamageWithCover(actor, target, rawDmg, isDemoralized, out string coverDesc);
                string desc = $"Turn {_globalTurnCounter}: [EXECUTE] {actor.EntityName} executed {target.EntityName} with {actualDmg} lethal damage!{coverDesc}";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, actualDmg, 0, desc);
            }

            if (actionId == "Intimidate")
            {
                var target = FindEntity(targetId);
                if (target == null || !target.IsAlive) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Target dead.");
                target.ApplyStatusEffect(StatusEffectType.Demoralized, 2, 25);
                target.ModifyRationality(-10);
                string desc = $"Turn {_globalTurnCounter}: [INTIMIDATE] {actor.EntityName} roared at {target.EntityName} (-25% Attack, -10 Rationality).";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, 0, 0, desc);
            }

            if (actionId == "PleaForMercy")
            {
                var target = FindEntity(targetId);
                if (target != null && target.IsAlive) target.ApplyStatusEffect(StatusEffectType.Hesitation, 1, 0);
                string desc = $"Turn {_globalTurnCounter}: [PLEA FOR MERCY] {actor.EntityName} pleaded for their life, causing enemy hesitation.";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, targetId, 0, 0, desc);
            }

            if (actionId == "EncourageAlly")
            {
                var ally = FindEntity(targetId);
                if (ally == null || !ally.IsAlive) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Ally dead.");
                ally.ClearMentalDebuffs();
                ally.ApplyMPRestore(10);
                ally.ModifyRationality(25);
                string desc = $"Turn {_globalTurnCounter}: [ENCOURAGE] {actor.EntityName} encouraged {ally.EntityName} (+25 Rationality, +10 MP).";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, ally.EntityId, 0, 0, desc);
            }

            if (actionId == "Rally")
            {
                if (!actor.ConsumeMP(15)) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Insufficient MP for Rally.");
                var allies = combatants.Where(c => c.Team == actor.Team && c.IsAlive).ToList();
                foreach (var a in allies)
                {
                    a.ApplyStatusEffect(StatusEffectType.AttackBuff, 2, 25);
                    a.ModifyRationality(15);
                }
                string desc = $"Turn {_globalTurnCounter}: [RALLY] {actor.EntityName} raised a battle cry! (+25% Attack, +15 Rationality to all allies).";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, "AllAllies", 0, 0, desc);
            }

            // Skills & Items
            var skill = actor.EquippedSkills.FirstOrDefault(s => s != null && s.SkillId == actionId);
            if (skill != null)
            {
                if (!actor.ConsumeMP(skill.MPCost)) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Insufficient MP.");
                actor.SetSkillCooldown(skill.SkillId, skill.BaseCooldown);

                if (skill.Category == SkillCategory.Support)
                {
                    var target = FindEntity(targetId) ?? actor;
                    int healAmount = Mathf.RoundToInt(actor.Stats.AttackPower * (skill.PowerMultiplierPercent / 100f));
                    int actualHealed = target.ApplyHeal(healAmount);
                    string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} used {skill.SkillName} on {target.EntityName} (+{actualHealed} HP).";
                    AddEventLog(desc);
                    return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, 0, actualHealed, desc);
                }
                else
                {
                    var target = FindEntity(targetId);
                    if (target == null || !target.IsAlive) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Target dead.");
                    int rawDmg = Mathf.RoundToInt(actor.Stats.AttackPower * (skill.PowerMultiplierPercent / 100f));
                    int actualDmg = DeliverDamageWithCover(actor, target, rawDmg, isDemoralized, out string coverDesc);
                    string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} cast {skill.SkillName} on {target.EntityName} dealing {actualDmg} damage.{coverDesc}";
                    AddEventLog(desc);
                    return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, actualDmg, 0, desc);
                }
            }

            var itemData = actor.GetItem(actionId);
            if (itemData != null)
            {
                if (!actor.ConsumeItem(actionId)) return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, "Item depleted.");
                var target = FindEntity(targetId) ?? actor;
                int healed = itemData.EffectType == ItemEffectType.RestoreHP ? target.ApplyHeal(itemData.EffectValue) : 0;
                string desc = $"Turn {_globalTurnCounter}: {actor.EntityName} used {itemData.ItemName} on {target.EntityName}.";
                AddEventLog(desc);
                return ActionExecutionResult.CreateSuccess(actor.EntityId, actionId, target.EntityId, 0, healed, desc);
            }

            string unkDesc = $"Turn {_globalTurnCounter}: {actor.EntityName} performed '{actionId}'.";
            AddEventLog(unkDesc);
            return ActionExecutionResult.CreateFailure(actor.EntityId, actionId, targetId, unkDesc);
        }

        private int DeliverDamageWithCover(CombatEntity attacker, CombatEntity intendedTarget, int rawDamage, bool isDemoralized, out string coverDesc)
        {
            coverDesc = "";

            if (!string.IsNullOrEmpty(intendedTarget.CoveredByActorId))
            {
                var protector = FindEntity(intendedTarget.CoveredByActorId);
                if (protector != null && protector.IsAlive && protector != intendedTarget)
                {
                    int mitigatedCoverDamage = Mathf.RoundToInt(rawDamage * 0.75f);
                    int actualDamage = protector.ApplyDamage(mitigatedCoverDamage, isDemoralized);
                    coverDesc = $" (INTERCEPTED by {protector.EntityName} for {actualDamage} damage!)";
                    
                    if (!protector.IsAlive)
                    {
                        coverDesc += $" {protector.EntityName} fell KILLED IN ACTION protecting their ally!";
                        NotifyTeammatesOfDefeat(protector);
                    }
                    else
                    {
                        NotifyTeammatesOfDamage(protector, actualDamage);
                    }
                    return actualDamage;
                }
            }

            int dmg = intendedTarget.ApplyDamage(rawDamage, isDemoralized);
            if (!intendedTarget.IsAlive)
            {
                coverDesc += $" {intendedTarget.EntityName} was defeated!";
                NotifyTeammatesOfDefeat(intendedTarget);
            }
            else
            {
                NotifyTeammatesOfDamage(intendedTarget, dmg);
            }
            return dmg;
        }

        private void NotifyTeammatesOfDamage(CombatEntity woundedEntity, int damage)
        {
            var teammates = combatants.Where(c => c != null && c.Team == woundedEntity.Team && c.IsAlive && c != woundedEntity).ToList();
            foreach (var mate in teammates)
            {
                mate.NotifyAllyDamaged(woundedEntity, damage);
            }
        }

        private void NotifyTeammatesOfDefeat(CombatEntity deadEntity)
        {
            var teammates = combatants.Where(c => c != null && c.Team == deadEntity.Team && c.IsAlive && c != deadEntity).ToList();
            foreach (var mate in teammates)
            {
                mate.NotifyAllyDefeated(deadEntity);

                // Periksa apakah kematian ini memicu evolusi/pergeseran watak traumatis
                var shiftResult = _evolutionEngine.EvaluateTraumaOnAllyDefeat(mate, deadEntity);
                if (shiftResult.ShiftOccurred)
                {
                    OnTraitShifted?.Invoke(shiftResult);
                    AddEventLog(shiftResult.RadioMonologue);
                }
            }
        }

        public BattleOutcome CheckBattleOutcome()
        {
            bool hasLivingPlayer = combatants.Any(c => c != null && c.Team == TeamSide.PlayerTeam && c.IsAlive);
            bool hasLivingEnemy = combatants.Any(c => c != null && c.Team == TeamSide.EnemyTeam && c.IsAlive);

            if (!hasLivingPlayer && !hasLivingEnemy) return BattleOutcome.Draw;
            if (!hasLivingPlayer) return BattleOutcome.EnemyVictory;
            if (!hasLivingEnemy) return BattleOutcome.PlayerVictory;
            if (_globalTurnCounter >= maxTurns) return BattleOutcome.TurnLimitReached;

            return BattleOutcome.InProgress;
        }

        public CombatEntity FindEntity(string entityId)
        {
            if (string.IsNullOrEmpty(entityId)) return null;
            return combatants.FirstOrDefault(c => c != null && c.EntityId == entityId);
        }

        private void AddEventLog(string log)
        {
            _recentEvents.Add(log);
            if (_recentEvents.Count > 6)
            {
                _recentEvents.RemoveAt(0);
            }
        }
    }
}
