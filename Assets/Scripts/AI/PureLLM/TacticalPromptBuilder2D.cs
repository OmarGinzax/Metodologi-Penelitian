using System;
using System.Collections.Generic;
using System.Text;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>
    /// Menyusun System Prompt (aturan penuh + skema JSON) dan User Prompt (injeksi state)
    /// untuk agen Pure LLM. Prompt sengaja tidak menyembunyikan konsekuensi aturan:
    /// LLM diberi kebebasan penuh dan engine akan mengeksekusi keputusannya apa adanya.
    /// </summary>
    public class TacticalPromptBuilder2D
    {
        private readonly int _maxReachableTilesInPrompt;

        public TacticalPromptBuilder2D(int maxReachableTilesInPrompt = 40)
        {
            _maxReachableTilesInPrompt = Math.Max(4, maxReachableTilesInPrompt);
        }

        public string BuildSystemPrompt(CombatEntity2D actor)
        {
            var skill = TacticalSkillCatalog2D.Resolve(actor);
            string skillLine = skill != null
                ? "- \"" + skill.SkillId + "\" (your class skill): " + skill.Description +
                  " Range: " + skill.Range + " tiles. Cooldown: " + skill.CooldownTurns + " turns."
                : "- You do not have a class support skill. Do not attempt to cast a skill.";

            var sb = new StringBuilder(2200);
            sb.AppendLine("You are an expert tactical combat AI commanding a specialized operative in a turn-based tactical grid combat game (Sector 7 Overpass).");
            sb.AppendLine("Your objective is to achieve tactical victory by eliminating all hostile targets or securing survival/extraction when overwhelmed.");
            sb.AppendLine();
            sb.AppendLine("TACTICAL ENVIRONMENT RULES:");
            sb.AppendLine("1. Grid Dimensions: Width = 18 (X: 0 to 17), Height = 10 (Y: 0 to 9).");
            sb.AppendLine("2. Extraction Zone: Entire west border at X = 0. Entering X = 0 allows safe tactical evacuation.");
            sb.AppendLine("3. Movement: You can move up to your MoveRange per turn. Movement is blocked by obstacles (HalfCover, FullCover, Blocked) and enemy units.");
            sb.AppendLine("4. Line of Sight (LoS): FullCover obstacles completely block Line of Sight and weapon fire. HalfCover obstacles provide cover but ALLOW weapon fire through them.");
            sb.AppendLine("5. Cover & Flanking:");
            sb.AppendLine("   - HalfCover reduces incoming damage by 25%.");
            sb.AppendLine("   - FullCover reduces incoming damage by 50%.");
            sb.AppendLine("   - Flanking (shooting an enemy from an angle with no cover barricade) deals +20% Critical Damage and ignores cover.");
            sb.AppendLine("6. Defensive Stance (Defend / Hunker Down): Fortifies position behind cover, reducing incoming damage by 50%.");
            sb.AppendLine("7. Support Skills:");
            sb.AppendLine("   - \"Guard\" (Marcus): Protects an ally in range for 2 turns (redirecting damage to Marcus) and restores +10% Rationality. (Cooldown: 2 turns).");
            sb.AppendLine("   - \"Heal\" (Claire): Restores +50 HP and +10% Rationality to self or an ally. (Cooldown: 2 turns).");
            sb.AppendLine(skillLine);
            sb.AppendLine("8. Rationality & Morale: Units lose Rationality upon witnessing ally distress. Low Rationality causes psychological panic.");
            sb.AppendLine();
            sb.AppendLine("AVAILABLE ACTION TYPES:");
            sb.AppendLine("- \"Move\": Move to destination [x, y].");
            sb.AppendLine("- \"Shoot\": Fire weapon at target_id within AttackRange and with clear LoS.");
            sb.AppendLine("- \"MoveAndShoot\": Move to destination [x, y], then immediately fire at target_id from the new position.");
            sb.AppendLine("- \"Defend\": Hunker down behind cover to mitigate incoming damage.");
            sb.AppendLine("- \"CastSkill\": Activate your class support skill (\"Guard\" or \"Heal\") on target_id.");
            sb.AppendLine("- \"MoveAndCastSkill\": Move to destination [x, y], then cast support skill on target_id.");
            sb.AppendLine("- \"Flee\": Retreat toward the Extraction Zone at X = 0. Set destination to the retreat tile.");
            sb.AppendLine("- \"Idle\": Pass turn.");
            sb.AppendLine();
            sb.AppendLine("IMPORTANT: You have complete freedom to choose the best tactical strategy. There is no external validator and no HFSM correcting your decision. The engine executes exactly what you output, including its consequences.");
            sb.AppendLine();
            sb.AppendLine("OUTPUT REQUIREMENTS:");
            sb.AppendLine("Respond strictly with a single raw JSON object matching this schema without any markdown wrapping or conversational commentary:");
            sb.AppendLine("{");
            sb.AppendLine("  \"action\": \"MoveAndShoot\" | \"Shoot\" | \"Move\" | \"Defend\" | \"CastSkill\" | \"MoveAndCastSkill\" | \"Flee\" | \"Idle\",");
            sb.AppendLine("  \"destination\": {\"x\": int, \"y\": int},");
            sb.AppendLine("  \"target_id\": \"string (entity ID or null)\",");
            sb.AppendLine("  \"skill_id\": \"string (skill name or null)\",");
            sb.AppendLine("  \"reasoning\": \"string (brief tactical rationale for your decision)\"");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string BuildUserPrompt(TacticalStateSnapshot2D snapshot)
        {
            if (snapshot == null)
                return "=== CURRENT COMBAT STATE ===\nNo tactical state snapshot is available. Output an Idle action JSON.";

            var sb = new StringBuilder(3000);
            sb.Append("=== CURRENT COMBAT STATE (ROUND ").Append(snapshot.RoundNumber)
              .Append(", TURN ").Append(snapshot.TurnNumber).AppendLine(") ===");

            if (snapshot.Grid != null)
            {
                sb.Append("GRID: ").Append(snapshot.Grid.Width).Append('x').Append(snapshot.Grid.Height)
                  .Append(" | Extraction Zone: X=").Append(snapshot.Grid.ExtractionZoneX).AppendLine();
            }

            sb.AppendLine("YOUR OPERATIVE:");
            AppendActor(sb, snapshot.CurrentActor);

            sb.AppendLine();
            AppendReachableTiles(sb, snapshot);

            sb.AppendLine();
            sb.AppendLine("ACTIVE ALLIES:");
            if (snapshot.Allies.Count == 0) sb.AppendLine("- (none)");
            else
            {
                for (int i = 0; i < snapshot.Allies.Count; i++)
                    AppendAlly(sb, snapshot.Allies[i]);
            }

            sb.AppendLine();
            sb.AppendLine("DETECTED ENEMIES (Status, Distance, LoS, Cover):");
            if (snapshot.Enemies.Count == 0) sb.AppendLine("- (none)");
            else
            {
                for (int i = 0; i < snapshot.Enemies.Count; i++)
                    AppendEnemy(sb, snapshot.Enemies[i]);
            }

            sb.AppendLine();
            sb.AppendLine("RECENT COMBAT LOGS:");
            if (snapshot.RecentLogs.Count == 0) sb.AppendLine("- (no recent events)");
            else
            {
                for (int i = 0; i < snapshot.RecentLogs.Count; i++)
                    sb.Append("- ").AppendLine(snapshot.RecentLogs[i]);
            }

            sb.AppendLine();
            sb.AppendLine("Analyze the situation and output your chosen action JSON.");
            return sb.ToString();
        }

        private static void AppendActor(StringBuilder sb, ActorStateDTO2D a)
        {
            if (a == null) { sb.AppendLine("- (unknown actor)"); return; }

            sb.Append("- ID: ").Append(a.EntityId)
              .Append(" | Name: ").Append(a.Name)
              .Append(" | Team: ").Append(a.Team)
              .Append(" | Trait: ").AppendLine(a.Trait);
            sb.Append("- Position: (").Append(a.X).Append(", ").Append(a.Y).Append(")")
              .Append(" | InCover: ").Append(a.InCover ? "true" : "false").Append(" (").Append(a.CoverType).Append(')')
              .Append(" | Flanked: ").Append(a.IsFlanked ? "true" : "false").AppendLine();
            sb.Append("- HP: ").Append(a.HP).Append('/').Append(a.MaxHP)
              .Append(" | MP: ").Append(a.MP).Append('/').Append(a.MaxMP)
              .Append(" | Rationality: ").Append(a.Rationality).Append("/100 (")
              .Append(a.Composure).AppendLine(")");
            sb.Append("- Weapon: ").Append(a.WeaponName).Append(" (Range: ").Append(a.AttackRange)
              .Append(") | Move Range: ").Append(a.MoveRange).AppendLine();
            sb.Append("- Skill: ").Append(string.IsNullOrEmpty(a.SkillId) ? "(none)" : a.SkillId)
              .Append(" (Ready: ").Append(a.IsSkillReady ? "true" : "false")
              .Append(", Cooldown Remaining: ").Append(a.SkillCooldownTurns).Append(" turns)");
            if (!string.IsNullOrEmpty(a.SkillId)) sb.Append(" | Skill Range: ").Append(a.SkillRange);
            sb.AppendLine();
            sb.Append("- Status: Defending=").Append(a.IsDefending ? "true" : "false")
              .Append(", Fleeing=").Append(a.IsFleeing ? "true" : "false")
              .Append(", Guarded=").Append(a.IsGuarded ? "true" : "false").AppendLine();
        }

        private void AppendReachableTiles(StringBuilder sb, TacticalStateSnapshot2D snapshot)
        {
            sb.Append("REACHABLE TILES WITH COVER (showing ")
              .Append(Math.Min(_maxReachableTilesInPrompt, snapshot.ReachableTiles.Count))
              .Append(" of ").Append(snapshot.ReachableTiles.Count).AppendLine("):");

            if (snapshot.ReachableTiles.Count == 0)
            {
                sb.AppendLine("- (none)");
                return;
            }

            var indices = new List<int>(snapshot.ReachableTiles.Count);
            for (int i = 0; i < snapshot.ReachableTiles.Count; i++) indices.Add(i);

            indices.Sort((left, right) =>
            {
                var a = snapshot.ReachableTiles[left];
                var b = snapshot.ReachableTiles[right];
                int cover = CoverScore(b).CompareTo(CoverScore(a));
                if (cover != 0) return cover;
                int extraction = b.IsExtractionZone.CompareTo(a.IsExtractionZone);
                if (extraction != 0) return extraction;
                int flank = a.IsFlanked.CompareTo(b.IsFlanked);
                if (flank != 0) return flank;
                return a.DistanceFromActor.CompareTo(b.DistanceFromActor);
            });

            int count = Math.Min(_maxReachableTilesInPrompt, indices.Count);
            for (int i = 0; i < count; i++)
            {
                var t = snapshot.ReachableTiles[indices[i]];
                sb.Append("- (").Append(t.X).Append(", ").Append(t.Y).Append(')')
                  .Append(" cover=").Append(t.CoverType)
                  .Append(" flanked=").Append(t.IsFlanked ? "true" : "false")
                  .Append(" distance=").Append(t.DistanceFromActor)
                  .Append(" extraction=").Append(t.IsExtractionZone ? "true" : "false")
                  .AppendLine();
            }

            if (indices.Count > count) sb.AppendLine("- ... (more reachable tiles omitted)");
        }

        private static int CoverScore(ReachableTileDTO2D tile)
        {
            if (tile == null || string.IsNullOrEmpty(tile.CoverType)) return 0;
            if (tile.CoverType == "FullCover") return 2;
            if (tile.CoverType == "HalfCover") return 1;
            return 0;
        }

        private static void AppendAlly(StringBuilder sb, AllyStateDTO2D a)
        {
            if (a == null) return;
            sb.Append("- ID: ").Append(a.EntityId)
              .Append(" | Name: ").Append(a.Name)
              .Append(" | Pos: (").Append(a.X).Append(", ").Append(a.Y).Append(')')
              .Append(" | HP: ").Append(a.HP).Append('/').Append(a.MaxHP)
              .Append(" | RAT: ").Append(a.Rationality)
              .Append(" | Dist: ").Append(a.DistanceFromActor)
              .Append(" | LoS: ").Append(a.HasLineOfSight ? "clear" : "blocked")
              .Append(" | Cover: ").Append(a.CoverAgainstNearestEnemy)
              .Append(" | Guarded: ").Append(a.IsGuarded ? "true" : "false")
              .Append(" | Fleeing: ").Append(a.IsFleeing ? "true" : "false")
              .AppendLine();
        }

        private static void AppendEnemy(StringBuilder sb, EnemyStateDTO2D e)
        {
            if (e == null) return;
            sb.Append("- ID: ").Append(e.EntityId)
              .Append(" | Name: ").Append(e.Name)
              .Append(" | Pos: (").Append(e.X).Append(", ").Append(e.Y).Append(')')
              .Append(" | HP: ").Append(e.HP).Append('/').Append(e.MaxHP)
              .Append(" | RAT: ").Append(e.Rationality)
              .Append(" | Dist: ").Append(e.DistanceFromActor)
              .Append(" | LoS: ").Append(e.HasLineOfSight ? "clear" : "blocked")
              .Append(" | Cover: ").Append(e.CoverAgainstActor)
              .Append(" | Flankable: ").Append(e.IsFlankable ? "true" : "false")
              .Append(" | Alive: ").Append(e.IsAlive ? "true" : "false")
              .AppendLine();
        }
    }
}
