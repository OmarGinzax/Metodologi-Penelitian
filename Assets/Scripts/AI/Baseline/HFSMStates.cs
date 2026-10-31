using System.Linq;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.Baseline
{
    public class EmergencyState : IHFSMState
    {
        public string StateName => "EmergencyState";

        public AgentDecision Evaluate(CombatStateSnapshot snapshot)
        {
            var actor = snapshot.CurrentActor;

            // 1. Periksa item penyembuh pribadi
            var healItem = snapshot.AvailableActions.FirstOrDefault(a => a.ActionType == "UseItem");
            if (healItem != null)
            {
                return new AgentDecision(actor.EntityId, healItem.ActionId, actor.EntityId, 
                    "Emergency survival: Using healing item.", "OBEY", "Using item to stabilize vitals!");
            }

            // 2. Periksa skill penyembuh jika ada
            var healSkill = snapshot.AvailableActions.FirstOrDefault(a => a.ActionId.Contains("Heal"));
            if (healSkill != null)
            {
                return new AgentDecision(actor.EntityId, healSkill.ActionId, actor.EntityId, 
                    "Emergency survival: Casting heal spell.", "OBEY", "Healing self under critical pressure!");
            }

            // 3. Ambil posisi mundur taktis jika tersedia
            if (snapshot.IsActionAvailable("TacticalRetreat"))
            {
                return new AgentDecision(actor.EntityId, "TacticalRetreat", actor.EntityId, 
                    "Emergency survival: Falling back to transport.", "ADAPT", "Falling back to cover!");
            }

            // 4. Fallback: Defend
            return new AgentDecision(actor.EntityId, "Action_Defend", actor.EntityId, 
                "Emergency survival: Bracing for impact.", "OBEY", "Taking defensive stance!");
        }
    }

    public class TacticalSupportState : IHFSMState
    {
        public string StateName => "TacticalSupportState";

        public AgentDecision Evaluate(CombatStateSnapshot snapshot)
        {
            var actor = snapshot.CurrentActor;

            // Cari rekan yang paling kritis
            var woundedAlly = snapshot.Allies
                .Where(a => a.IsAlive && a.HPPercentage <= 30f)
                .OrderBy(a => a.CurrentHP)
                .FirstOrDefault();

            if (woundedAlly != null)
            {
                // Prioritas 1: Carry Wounded jika tersedia dalam daftar aksi legal
                string carryActionId = $"Carry_{woundedAlly.EntityId}";
                if (snapshot.IsActionAvailable(carryActionId))
                {
                    return new AgentDecision(actor.EntityId, carryActionId, woundedAlly.EntityId, 
                        $"Tactical support: Dragging critically wounded {woundedAlly.Name} to safety.", "OBEY", 
                        $"Hold on {woundedAlly.Name}, dragging you behind cover!");
                }

                // Prioritas 2: Cover Ally
                string coverActionId = $"Cover_{woundedAlly.EntityId}";
                if (snapshot.IsActionAvailable(coverActionId))
                {
                    return new AgentDecision(actor.EntityId, coverActionId, woundedAlly.EntityId, 
                        $"Tactical support: Intercepting incoming fire for {woundedAlly.Name}.", "OBEY", 
                        $"Get down {woundedAlly.Name}! I've got you covered!");
                }

                // Prioritas 3: Toss Item
                var tossItem = snapshot.AvailableActions.FirstOrDefault(a => a.ActionId.StartsWith("Toss_"));
                if (tossItem != null)
                {
                    return new AgentDecision(actor.EntityId, tossItem.ActionId, woundedAlly.EntityId, 
                        $"Tactical support: Tossing medical supplies to {woundedAlly.Name}.", "OBEY", 
                        $"Catch, {woundedAlly.Name}! Patch yourself up!");
                }
            }

            // Prioritas 4: Encourage rekan yang panik
            var panickedAlly = snapshot.Allies.FirstOrDefault(a => a.IsAlive && a.ComposureLevel == "Panicked");
            if (panickedAlly != null && snapshot.IsActionAvailable("EncourageAlly"))
            {
                return new AgentDecision(actor.EntityId, "EncourageAlly", panickedAlly.EntityId, 
                    $"Tactical support: Lifting morale of panicked {panickedAlly.Name}.", "OBEY", 
                    $"Steady, {panickedAlly.Name}! Keep your head in the fight!");
            }

            // Fallback: Aksi standar jika tidak ada yang mendesak
            return new OffensiveState().Evaluate(snapshot);
        }
    }

    public class OffensiveState : IHFSMState
    {
        public string StateName => "OffensiveState";

        public AgentDecision Evaluate(CombatStateSnapshot snapshot)
        {
            var actor = snapshot.CurrentActor;
            var livingEnemies = snapshot.Enemies.Where(e => e.IsAlive).ToList();
            if (livingEnemies.Count == 0)
            {
                return new AgentDecision(actor.EntityId, "Action_Defend", actor.EntityId, "No enemies living.", "OBEY");
            }

            // 1. Cek peluang Execute (jika ada musuh sekarat dan aksi Execute legal)
            var executeTarget = livingEnemies.FirstOrDefault(e => e.HPPercentage <= 25f);
            if (executeTarget != null && snapshot.IsActionAvailable("Execute"))
            {
                return new AgentDecision(actor.EntityId, "Execute", executeTarget.EntityId, 
                    $"Offensive finisher: Executing critically wounded {executeTarget.Name}.", "OBEY", 
                    $"Eliminating target {executeTarget.Name} now!");
            }

            // 2. Cek Suppression Fire jika ada musuh ancaman tinggi dan aksi tersedia
            var highThreat = livingEnemies.OrderByDescending(e => e.AttackPower).First();
            if (snapshot.IsActionAvailable("SuppressionFire"))
            {
                return new AgentDecision(actor.EntityId, "SuppressionFire", highThreat.EntityId, 
                    $"Offensive tactical: Suppressing high threat {highThreat.Name}.", "OBEY", 
                    $"Suppressing {highThreat.Name}! Move up!");
            }

            // 3. Flank Maneuver jika tersedia
            if (snapshot.IsActionAvailable("FlankManeuver"))
            {
                var flankTarget = livingEnemies.OrderBy(e => e.CurrentHP).First();
                return new AgentDecision(actor.EntityId, "FlankManeuver", flankTarget.EntityId, 
                    $"Offensive flank: Hitting {flankTarget.Name} from blind spot.", "OBEY", 
                    $"Flanking {flankTarget.Name}!");
            }

            // 4. Jurus Serang Skill terpasang
            var attackSkill = snapshot.AvailableActions.FirstOrDefault(a => a.ActionType == "CastSkill");
            if (attackSkill != null)
            {
                var skillTarget = livingEnemies.OrderBy(e => e.CurrentHP).First();
                return new AgentDecision(actor.EntityId, attackSkill.ActionId, skillTarget.EntityId, 
                    $"Offensive skill: Casting {attackSkill.ActionName} on {skillTarget.Name}.", "OBEY", 
                    $"Firing {attackSkill.ActionName}!");
            }

            // 5. Basic Attack terfokus pada musuh dengan HP terendah (Focus Fire)
            var weakest = livingEnemies.OrderBy(e => e.CurrentHP).First();
            return new AgentDecision(actor.EntityId, "BasicAttack", weakest.EntityId, 
                $"Offensive standard: Firing standard attack at {weakest.Name}.", "OBEY", 
                $"Firing on {weakest.Name}.");
        }
    }

    public class ResourceConservingState : IHFSMState
    {
        public string StateName => "ResourceConservingState";

        public AgentDecision Evaluate(CombatStateSnapshot snapshot)
        {
            var actor = snapshot.CurrentActor;
            var livingEnemies = snapshot.Enemies.Where(e => e.IsAlive).ToList();

            if (livingEnemies.Count > 0)
            {
                var target = livingEnemies.OrderBy(e => e.CurrentHP).First();
                return new AgentDecision(actor.EntityId, "BasicAttack", target.EntityId, 
                    "Resource conserving: Free basic attack.", "OBEY", "Conserving ammo, firing single shots.");
            }

            return new AgentDecision(actor.EntityId, "Action_Defend", actor.EntityId, 
                "Resource conserving: Defending position.", "OBEY");
        }
    }
}
