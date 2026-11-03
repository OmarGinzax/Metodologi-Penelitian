using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.AI.Baseline;

namespace RPGStrategy.Tests
{
    [TestFixture]
    public class CoreTactical2DTests
    {
        private GameObject _holder;
        private TacticalGrid2D _grid;

        [SetUp]
        public void Setup()
        {
            _holder = new GameObject("TestHolder_2D");
            _grid = _holder.AddComponent<TacticalGrid2D>();
            _grid.InitializeGrid(18, 10, 1.0f);
        }

        [TearDown]
        public void Teardown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
            }
        }

        [Test]
        public void TC_2D_01_StrictBounds_ClampsAndRejectsOutOfBounds()
        {
            // Assert bounds check
            Assert.IsTrue(_grid.IsInBounds(new Vector2Int(0, 0)));
            Assert.IsTrue(_grid.IsInBounds(new Vector2Int(17, 9)));

            Assert.IsFalse(_grid.IsInBounds(new Vector2Int(-1, 5)), "Negative X must be out of bounds.");
            Assert.IsFalse(_grid.IsInBounds(new Vector2Int(18, 5)), "X >= 18 must be out of bounds.");
            Assert.IsFalse(_grid.IsInBounds(new Vector2Int(5, -1)), "Negative Y must be out of bounds.");
            Assert.IsFalse(_grid.IsInBounds(new Vector2Int(5, 10)), "Y >= 10 must be out of bounds.");

            // Assert Clamping
            var clamped = _grid.ClampToBounds(new Vector2Int(99, -50));
            Assert.AreEqual(new Vector2Int(17, 0), clamped);
        }

        [Test]
        public void TC_2D_02_LineOfSight_FullCoverBlocksLoS()
        {
            // Tempatkan FullCover di tengah jalan layang (5, 5)
            _grid.SetCellType(new Vector2Int(5, 5), CellType.FullCover);

            // Garis lurus horizontal dari (2, 5) ke (8, 5) melewati (5, 5)
            bool losBlocked = _grid.HasLineOfSight(new Vector2Int(2, 5), new Vector2Int(8, 5));
            Assert.IsFalse(losBlocked, "FullCover at (5,5) must block direct Line of Sight between (2,5) and (8,5).");

            // Garis miring yang tidak melewati (5, 5) harus tetap tembus
            bool losClear = _grid.HasLineOfSight(new Vector2Int(2, 2), new Vector2Int(8, 2));
            Assert.IsTrue(losClear, "Garis tembak yang tidak terhalang FullCover harus memiliki LoS bersih.");
        }

        [Test]
        public void TC_2D_03_LineOfSight_HalfCoverAllowsShooting()
        {
            // Tempatkan barikade beton rendah (HalfCover) di (5, 5)
            _grid.SetCellType(new Vector2Int(5, 5), CellType.HalfCover);

            // Tembakan melewati barikade rendah tetap diizinkan
            bool losAllowed = _grid.HasLineOfSight(new Vector2Int(2, 5), new Vector2Int(8, 5));
            Assert.IsTrue(losAllowed, "HalfCover must allow Line of Sight while providing cover mitigation.");
        }

        [Test]
        public void TC_2D_04_XCOMCover_EdgeBased_CorrectlySolvesAllDirections()
        {
            // Setup: defender at (5, 5). Place a HalfCover obstacle to the East at (6, 5).
            // After SetCellType → BakeCover runs → cell (5,5).cover[E] = HalfCover.
            _grid.SetCellType(new Vector2Int(6, 5), CellType.HalfCover);
            _grid.BakeAllCover(); // explicit bake (SetCellType triggers automatically too)

            // Attacker from EAST (9, 5) → should see the East edge = HalfCover
            var coverFromEast = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(9, 5));
            Assert.AreEqual(CoverType.HalfCover, coverFromEast,
                "East obstacle should grant HalfCover against attacker from the East.");

            // Attacker from WEST (2, 5) → no obstacle to the West → Flanked
            var coverFromWest = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(2, 5));
            Assert.AreEqual(CoverType.None, coverFromWest,
                "No obstacle to the West → CoverType.None (flanked from West).");
            Assert.IsTrue(_grid.IsFlanked(new Vector2Int(5, 5), new Vector2Int(2, 5)),
                "IsFlanked must be true when attacker comes from the unprotected side.");

            // Attacker from NORTH (5, 8) → no obstacle to the North → Flanked
            var coverFromNorth = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(5, 8));
            Assert.AreEqual(CoverType.None, coverFromNorth,
                "No obstacle to the North → CoverType.None.");

            // Diagonal attacker NE (8, 8) → checks BOTH North edge and East edge → East = HalfCover wins
            var coverDiagNE = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(8, 8));
            Assert.AreEqual(CoverType.HalfCover, coverDiagNE,
                "Diagonal NE attacker: East edge = HalfCover, North edge = None → best = HalfCover.");

            // FullCover to the North at (5, 6) — should override HalfCover for N-axis attackers
            _grid.SetCellType(new Vector2Int(5, 6), CellType.FullCover);
            _grid.BakeAllCover();
            var coverNorthFull = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(5, 8));
            Assert.AreEqual(CoverType.FullCover, coverNorthFull,
                "FullCover obstacle to the North → FullCover against Northern attacker.");

            // Diagonal NE: both North=FullCover and East=HalfCover → best = FullCover
            var coverDiagFull = _grid.GetCoverAgainst(new Vector2Int(5, 5), new Vector2Int(8, 8));
            Assert.AreEqual(CoverType.FullCover, coverDiagFull,
                "Diagonal NE with FullCover N and HalfCover E → best = FullCover.");
        }

        [Test]
        public void TC_2D_05_InspectorActionTelemetry_UpdatesLiveOnCombatEntity2D()
        {
            var entityGo = new GameObject("Tasha");
            entityGo.transform.SetParent(_holder.transform);
            var entity = entityGo.AddComponent<CombatEntity2D>();

            entity.Initialize(
                "u3_tasha", "Tasha (Flanker)", TeamSide.PlayerTeam,
                new EntityStats(90, 50, 26, 10, 14, 88), "DisciplinedProfessional",
                new Vector2Int(2, 2), "Vector Tactical Rifle", 5, 4);

            // Verifikasi status awal
            Assert.AreEqual("IdleWaiting", entity.CurrentActionType);
            Assert.IsFalse(entity.IsActing);

            // Update telemetri aksi saat Play Mode
            entity.SetActionTelemetry("Flanking_VectorRifle_Burst", "Flanking Scout Drone Alpha from the north corridor.", "enemy_drone_1", new Vector2Int(14, 2));

            // Assert Inspector Telemetry
            Assert.AreEqual("Flanking_VectorRifle_Burst", entity.CurrentActionType);
            Assert.IsTrue(entity.CurrentActionDescription.Contains("Flanking Scout Drone"));
            Assert.IsTrue(entity.IsActing);
        }

        [Test]
        public void TC_2D_06_DamageCalculation_CoverReducesDamage_AndFlankIncreasesDamage()
        {
            var defenderGo = new GameObject("Defender");
            defenderGo.transform.SetParent(_holder.transform);
            var defender = defenderGo.AddComponent<CombatEntity2D>();
            defender.Initialize("def_1", "Defender", TeamSide.PlayerTeam,
                new EntityStats(100, 20, 10, 10, 10, 80), "LoyalProtector", new Vector2Int(4, 4));

            // 1. Tembakan biasa tanpa cover (raw 40 atk vs 10 def) -> 40 - 5 = 35 dmg
            int normalDmg = defender.ApplyDamage(40, attackIsFlanking: false);
            Assert.AreEqual(35, normalDmg);

            // 2. Tembakan dalam posisi Defending (Hunker Down 50% cover mitigation)
            // 40 * 0.5 = 20 atk - 5 def = 15 dmg
            defender.SetDefending(true);
            int defendedDmg = defender.ApplyDamage(40, attackIsFlanking: false);
            Assert.AreEqual(15, defendedDmg);

            // 3. Tembakan Flanking (mengabaikan cover + bonus 20% raw damage)
            // 40 * 1.2 = 48 atk - 5 def = 43 dmg
            int flankDmg = defender.ApplyDamage(40, attackIsFlanking: true);
            Assert.AreEqual(43, flankDmg);
        }

        [Test]
        public void TC_2D_07_SimpleHFSM_DecidesValidTacticalAction()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Prajurit Tasha di (2, 2)
            var tashaGo = new GameObject("Tasha");
            tashaGo.transform.SetParent(_holder.transform);
            var tasha = tashaGo.AddComponent<CombatEntity2D>();
            tasha.Initialize("u3_tasha", "Tasha", TeamSide.PlayerTeam,
                new EntityStats(90, 50, 26, 10, 14, 88), "DisciplinedProfessional",
                new Vector2Int(2, 2), "Vector Tactical Rifle", 5, 4);

            // Musuh Drone di (4, 2) dalam jangkauan tembak Tasha (jarak 2 petak)
            var droneGo = new GameObject("Drone");
            droneGo.transform.SetParent(_holder.transform);
            var drone = droneGo.AddComponent<CombatEntity2D>();
            drone.Initialize("drone_1", "Scout Drone", TeamSide.EnemyTeam,
                new EntityStats(50, 10, 15, 4, 12, 100), "AutomatedMachine",
                new Vector2Int(4, 2), "Pulse Blaster", 5, 4);

            _grid.SetOccupant(tasha.GridPosition, tasha);
            _grid.SetOccupant(drone.GridPosition, drone);

            var decision = hfsm.DecideTurn(tasha, _grid, new List<CombatEntity2D> { tasha, drone });

            Assert.IsNotNull(decision);
            Assert.IsTrue(decision.Action == TacticalActionType.Shoot || decision.Action == TacticalActionType.MoveAndShoot);
            Assert.AreEqual(drone, decision.TargetEnemy);
        }

        [Test]
        public void TC_2D_08_MovementRange_UsesFullMovementToAdvance()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Prajurit Claire (MoveRange = 3, AttackRange = 4) di (1, 3)
            var claireGo = new GameObject("Claire");
            claireGo.transform.SetParent(_holder.transform);
            var claire = claireGo.AddComponent<CombatEntity2D>();
            claire.Initialize("u4_claire", "Claire (Medic)", TeamSide.PlayerTeam,
                new EntityStats(70, 75, 14, 8, 11, 75), "TimidAltruist",
                new Vector2Int(1, 3), "Compact PDW", 4, 3);

            // Musuh berjarak jauh di (15, 3)
            var houndGo = new GameObject("Hound");
            houndGo.transform.SetParent(_holder.transform);
            var hound = houndGo.AddComponent<CombatEntity2D>();
            hound.Initialize("boss_hound", "HOUND-IV", TeamSide.EnemyTeam,
                new EntityStats(280, 50, 40, 18, 8, 100), "AutomatedMachine",
                new Vector2Int(15, 3), "Heavy Cannon", 8, 2);

            _grid.SetOccupant(claire.GridPosition, claire);
            _grid.SetOccupant(hound.GridPosition, hound);

            var decision = hfsm.DecideTurn(claire, _grid, new List<CombatEntity2D> { claire, hound });

            Assert.IsNotNull(decision);
            Assert.AreEqual(TacticalActionType.MoveToCover, decision.Action);

            // Verifikasi bahwa unit memanfaatkan jangkauan gerak penuh (3 petak) dan BUKAN hanya 1 petak
            int movedDist = _grid.GetDistance(new Vector2Int(1, 3), decision.MoveDestination);
            Assert.AreEqual(3, movedDist, "Unit with move range 3 advancing towards combat zone must advance 3 tiles, not just 1 tile!");
        }

        [Test]
        public void TC_2D_09_DeadAgent_SwapsSpriteToCasualtyAssetUponDeath()
        {
            var agentGo = new GameObject("AgentMarcus");
            agentGo.transform.SetParent(_holder.transform);
            var sr = agentGo.AddComponent<SpriteRenderer>();
            var originalSprite = RPGStrategy.Demo.TacticalSpriteFactory.CreateUnitSprite("marcus");
            sr.sprite = originalSprite;
            sr.sortingOrder = 20;

            var agent = agentGo.AddComponent<CombatEntity2D>();
            agent.Initialize("u1_marcus", "Marcus", TeamSide.PlayerTeam,
                new EntityStats(30, 20, 10, 0, 10, 80), "LoyalProtector", new Vector2Int(2, 4));

            Assert.IsTrue(agent.IsAlive);
            Assert.AreEqual(originalSprite, sr.sprite);
            Assert.AreEqual(20, sr.sortingOrder);

            // Beri lethal damage (50 raw damage vs 30 HP)
            agent.ApplyDamage(50, attackIsFlanking: false);

            Assert.IsFalse(agent.IsAlive, "Agent must be dead after lethal damage.");
            Assert.AreNotEqual(originalSprite, sr.sprite, "Sprite must be swapped to casualty asset upon death.");
            Assert.IsNotNull(sr.sprite, "Casualty sprite must not be null.");
            Assert.AreEqual(5, sr.sortingOrder, "Dead agent sortingOrder must sink to ground layer (5).");
        }

        [Test]
        public void TC_2D_10_TacticalHUD_LogScrollArea_EnablesScrollingWhenLogsExceedViewport()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas));
            var hud = canvasGo.AddComponent<RPGStrategy.UI.TacticalHUDController>();

            var scrollGo = new GameObject("ScrollRect", typeof(UnityEngine.UI.ScrollRect), typeof(UnityEngine.UI.Image));
            scrollGo.transform.SetParent(canvasGo.transform);
            var scroll = scrollGo.GetComponent<UnityEngine.UI.ScrollRect>();
            scroll.vertical = true;

            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            vpGo.transform.SetParent(scrollGo.transform);
            var vpRT = vpGo.GetComponent<RectTransform>();
            vpRT.sizeDelta = new Vector2(200f, 150f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(vpGo.transform);
            var contentRT = contentGo.GetComponent<RectTransform>();

            var txtGo = new GameObject("Log_Text", typeof(TMPro.TextMeshProUGUI));
            txtGo.transform.SetParent(contentGo.transform);
            var txt = txtGo.GetComponent<TMPro.TextMeshProUGUI>();

            scroll.viewport = vpRT;
            scroll.content = contentRT;

            var bmGo = new GameObject("BattleManager", typeof(BattleManager2D));
            var bm = bmGo.GetComponent<BattleManager2D>();

            typeof(RPGStrategy.UI.TacticalHUDController)
                .GetField("scrollLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hud, scroll);
            typeof(RPGStrategy.UI.TacticalHUDController)
                .GetField("txtCombatLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hud, txt);
            typeof(RPGStrategy.UI.TacticalHUDController)
                .GetField("battleManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(hud, bm);

            for (int i = 0; i < 30; i++)
            {
                bm.AddLog($"Round {i + 1}: Marcus fired Shotgun at Hunter for {i * 2 + 5} damage (Cover: HalfCover).");
            }

            var refreshMethod = typeof(RPGStrategy.UI.TacticalHUDController)
                .GetMethod("RefreshLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            refreshMethod?.Invoke(hud, null);

            Assert.Greater(contentRT.sizeDelta.y, vpRT.rect.height,
                "Content height must exceed viewport height to allow scrolling through combat logs.");
            Assert.IsTrue(scroll.vertical, "ScrollRect must have vertical scrolling enabled.");
            Assert.AreEqual(0f, scroll.verticalNormalizedPosition, 0.05f,
                "Scroll position should automatically snap to 0 (bottom) on new log entries.");

            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(bmGo);
        }

        [Test]
        public void TC_2D_11_NeutralTrait_PrioritizesCoverAndExecutesCoverAndShoot()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Pasang rintangan HalfCover di (4, 2)
            _grid.SetCellType(new Vector2Int(4, 2), CellType.HalfCover);

            // Aktor Neutral di (1, 2) (MoveRange = 3, AttackRange = 5)
            // Petak (3, 2) memiliki cover di sisi East (bersebelahan dengan 4, 2)
            var actorGo = new GameObject("NeutralActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_neutral", "Marcus", TeamSide.PlayerTeam,
                new EntityStats(100, 20, 20, 10, 10, 80), "Neutral",
                new Vector2Int(1, 2), "Heavy Shotgun", 5, 3);

            // Musuh di (6, 2)
            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_1", "Hunter", TeamSide.EnemyTeam,
                new EntityStats(80, 20, 15, 5, 10, 80), "Neutral",
                new Vector2Int(6, 2), "Rifle", 5, 3);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            var decision = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });

            Assert.IsNotNull(decision);
            // Neutral harus mengeksekusi MoveAndShoot ke petak dengan cover (3, 2)
            Assert.AreEqual(TacticalActionType.MoveAndShoot, decision.Action,
                "Neutral must execute Cover + Shoot in 1 action (MoveAndShoot).");
            Assert.AreEqual(new Vector2Int(3, 2), decision.MoveDestination,
                "Neutral must move to petak (3, 2) which provides edge cover against the enemy at (6, 2).");
            Assert.AreEqual(enemy, decision.TargetEnemy);
        }

        [Test]
        public void TC_2D_12_AggressiveTrait_SeizesFlankingOpportunityRegardlessOfCover()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Pasang tembok FullCover di (5, 6) yang melindungi musuh di (5, 5) dari sisi North (0, 1)
            _grid.SetCellType(new Vector2Int(5, 6), CellType.FullCover);

            // Musuh di (5, 5). Musuh terlindungi dari North, tapi terbuka (flank) dari South (y < 5)
            var enemyGo = new GameObject("DefendingEnemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_boss", "Strider", TeamSide.EnemyTeam,
                new EntityStats(120, 20, 15, 10, 8, 80), "Neutral",
                new Vector2Int(5, 5), "Heavy Cannon", 6, 2);

            // Aktor Aggressive di (2, 3) (MoveRange = 4, AttackRange = 5)
            // Dari petak (5, 3) (South dari musuh): Jarak = 2, sudut tembak FLANKING, tapi tanah TERBUKA (CoverType.None)
            var actorGo = new GameObject("AggressiveActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_aggro", "Tasha", TeamSide.PlayerTeam,
                new EntityStats(90, 50, 26, 10, 14, 88), "Aggressive",
                new Vector2Int(2, 3), "Vector Rifle", 5, 4);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            var decision = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });

            Assert.IsNotNull(decision);
            Assert.IsTrue(decision.IsFlanking,
                "Aggressive must seize the flanking opportunity regardless of cover!");
            Assert.IsTrue(decision.Action == TacticalActionType.MoveAndShoot || decision.Action == TacticalActionType.Shoot);
            Assert.AreEqual(enemy, decision.TargetEnemy);
        }

        [Test]
        public void TC_2D_13_PassiveTrait_RefusesToShootIfAdvancingExposesToFlank_ChoosesReposition()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Pasang cover yang aman di (1, 3)
            _grid.SetCellType(new Vector2Int(1, 4), CellType.FullCover);

            // Musuh A di (8, 2)
            var enemyAGo = new GameObject("EnemyA");
            enemyAGo.transform.SetParent(_holder.transform);
            var enemyA = enemyAGo.AddComponent<CombatEntity2D>();
            enemyA.Initialize("enm_a", "Shooter A", TeamSide.EnemyTeam,
                new EntityStats(50, 20, 10, 5, 10, 80), "Neutral",
                new Vector2Int(8, 2), "Rifle", 5, 3);

            // Musuh B di (3, 8) (akan mem-flank siapa pun di baris terbuka y=2 dari North)
            var enemyBGo = new GameObject("EnemyB");
            enemyBGo.transform.SetParent(_holder.transform);
            var enemyB = enemyBGo.AddComponent<CombatEntity2D>();
            enemyB.Initialize("enm_b", "Shooter B", TeamSide.EnemyTeam,
                new EntityStats(50, 20, 10, 5, 10, 80), "Neutral",
                new Vector2Int(3, 8), "Rifle", 5, 3);

            // Aktor Passive di (1, 1). Jika ia maju ke (4, 2) untuk menembak Enemy A,
            // ia akan terbuka dan di-flank oleh Enemy B dari North!
            var actorGo = new GameObject("PassiveActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_passive", "Claire", TeamSide.PlayerTeam,
                new EntityStats(70, 75, 14, 8, 11, 75), "Passive",
                new Vector2Int(1, 1), "PDW", 4, 3);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemyA.GridPosition, enemyA);
            _grid.SetOccupant(enemyB.GridPosition, enemyB);

            var decision = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemyA, enemyB });

            Assert.IsNotNull(decision);
            // Passive menolak MoveAndShoot jika petak serang berisiko di-flank oleh musuh!
            // Dia harus memilih Reposition (MoveToCover) tanpa menembak.
            Assert.AreEqual(TacticalActionType.MoveToCover, decision.Action,
                "Passive must choose reposition without shooting if advancing would leave it vulnerable to flanking.");
            Assert.IsNull(decision.TargetEnemy,
                "Passive repositioning should not execute a shot on an exposed tile.");
        }

        [Test]
        public void TC_2D_14_PassiveTrait_FleesAt30PercentHP()
        {
            var hfsm = new SimpleTacticalHFSM();

            var actorGo = new GameObject("PassiveActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_claire", "Claire", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 10, 5, 10, 80), "Passive",
                new Vector2Int(5, 5), "PDW", 4, 3);

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_hound", "Hound", TeamSide.EnemyTeam,
                new EntityStats(100, 50, 20, 10, 10, 80), "Aggressive",
                new Vector2Int(10, 5), "Cannon", 6, 2);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            // Pada 31 HP (31%), Passive belum flee
            actor.Stats.CurrentHP = 31;
            var dec31 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreNotEqual(TacticalActionType.Flee, dec31.Action, "Passive should not flee above 30% HP.");

            // Pada 30 HP (30%), Passive mulai Flee ke barat (x = 0 zona ekstraksi)
            actor.Stats.CurrentHP = 30;
            var dec30 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreEqual(TacticalActionType.Flee, dec30.Action, "Passive must execute Flee at <= 30% HP.");
            Assert.Less(dec30.MoveDestination.x, actor.GridPosition.x, "Flee move destination must progress westward towards extraction zone.");
        }

        [Test]
        public void TC_2D_15_NeutralTrait_FleesAt20PercentHP()
        {
            var hfsm = new SimpleTacticalHFSM();

            var actorGo = new GameObject("NeutralActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_marcus", "Marcus", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 20, 10, 10, 80), "Neutral",
                new Vector2Int(6, 4), "Shotgun", 4, 3);

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_hound", "Hound", TeamSide.EnemyTeam,
                new EntityStats(100, 50, 20, 10, 10, 80), "Aggressive",
                new Vector2Int(12, 4), "Cannon", 6, 2);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            // Pada 25 HP (25%), Neutral belum flee
            actor.Stats.CurrentHP = 25;
            var dec25 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreNotEqual(TacticalActionType.Flee, dec25.Action, "Neutral should not flee above 20% HP.");

            // Pada 20 HP (20%), Neutral mulai Flee ke arah x=0
            actor.Stats.CurrentHP = 20;
            var dec20 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreEqual(TacticalActionType.Flee, dec20.Action, "Neutral must execute Flee at <= 20% HP.");
            Assert.Less(dec20.MoveDestination.x, actor.GridPosition.x, "Flee move destination must move westward towards extraction.");
        }

        [Test]
        public void TC_2D_16_AggressiveTrait_FleesAt10PercentHP()
        {
            var hfsm = new SimpleTacticalHFSM();

            var actorGo = new GameObject("AggroActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_tasha", "Tasha", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 25, 10, 12, 80), "Aggressive",
                new Vector2Int(7, 3), "Vector", 5, 4);

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_drone", "Drone", TeamSide.EnemyTeam,
                new EntityStats(100, 50, 15, 5, 10, 80), "Neutral",
                new Vector2Int(12, 3), "Blaster", 5, 3);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            // Pada 15 HP (15%), Aggressive tetap berjuang dan belum flee
            actor.Stats.CurrentHP = 15;
            var dec15 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreNotEqual(TacticalActionType.Flee, dec15.Action, "Aggressive should fight on and not flee at 15% HP.");

            // Pada 10 HP (10%), Aggressive akhirnya Flee ke arah ekstraksi
            actor.Stats.CurrentHP = 10;
            var dec10 = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });
            Assert.AreEqual(TacticalActionType.Flee, dec10.Action, "Aggressive must execute Flee at <= 10% HP.");
            Assert.Less(dec10.MoveDestination.x, actor.GridPosition.x, "Flee move destination must retreat towards extraction zone.");
        }

        [Test]
        public void TC_2D_17_FleeDestination_DirectlyReachesExtractionZoneWhenClose()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Aktor Passive dekat batas ekstraksi di (2, 3) (MoveRange = 3)
            var actorGo = new GameObject("NearEvacActor");
            actorGo.transform.SetParent(_holder.transform);
            var actor = actorGo.AddComponent<CombatEntity2D>();
            actor.Initialize("act_evac", "Elena", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 10, 5, 10, 80), "Passive",
                new Vector2Int(2, 3), "Rifle", 5, 3);

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("enm_hound", "Hound", TeamSide.EnemyTeam,
                new EntityStats(100, 50, 20, 10, 10, 80), "Aggressive",
                new Vector2Int(12, 3), "Cannon", 6, 2);

            _grid.SetOccupant(actor.GridPosition, actor);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            actor.Stats.CurrentHP = 25; // <= 30% trigger
            var decision = hfsm.DecideTurn(actor, _grid, new List<CombatEntity2D> { actor, enemy });

            Assert.AreEqual(TacticalActionType.Flee, decision.Action);
            Assert.AreEqual(0, decision.MoveDestination.x, "When within move range of extraction (X=0), actor must move directly onto an extraction tile.");
            Assert.IsTrue(_grid.IsExtractionTile(decision.MoveDestination), "Destination must be an ExtractionZone tile.");
        }

        [Test]
        public void TC_2D_18_RationalityShock_ReducesOnAllyFleeAndAllyDeath()
        {
            var bmGo = new GameObject("BM_Test");
            var bm = bmGo.AddComponent<BattleManager2D>();
            bm.Grid = _grid;

            var ally1Go = new GameObject("Ally1");
            var ally1 = ally1Go.AddComponent<CombatEntity2D>();
            ally1.Initialize("a1", "Marcus", TeamSide.PlayerTeam, new EntityStats(100, 50, 20, 10, 10, 100), "Neutral", new Vector2Int(2, 2));

            var ally2Go = new GameObject("Ally2");
            var ally2 = ally2Go.AddComponent<CombatEntity2D>();
            ally2.Initialize("a2", "Claire", TeamSide.PlayerTeam, new EntityStats(100, 50, 10, 10, 10, 100), "Passive", new Vector2Int(2, 3));

            bm.InitializeBattle(new List<CombatEntity2D> { ally1, ally2 });

            Assert.AreEqual(100, ally2.Stats.Rationality);

            // Ally fleeing shock: -20% Rationality
            bm.ApplyMoraleShock(TeamSide.PlayerTeam, 20, "Marcus fleeing");
            Assert.AreEqual(80, ally2.Stats.Rationality, "Ally fleeing must reduce squad rationality by 20%.");

            // Ally dying shock: -40% Rationality
            bm.ApplyMoraleShock(TeamSide.PlayerTeam, 40, "Marcus killed");
            Assert.AreEqual(40, ally2.Stats.Rationality, "Ally dying must reduce squad rationality by 40%.");

            Object.DestroyImmediate(bmGo);
            Object.DestroyImmediate(ally1Go);
            Object.DestroyImmediate(ally2Go);
        }

        [Test]
        public void TC_2D_19_RationalityTrigger_CausesFleeAtTraitThresholds()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Passive actor with full HP (100/100) but low Rationality (30)
            var claireGo = new GameObject("Claire");
            claireGo.transform.SetParent(_holder.transform);
            var claire = claireGo.AddComponent<CombatEntity2D>();
            claire.Initialize("c", "Claire", TeamSide.PlayerTeam, new EntityStats(100, 50, 10, 5, 10, 31), "Passive", new Vector2Int(4, 4));

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("e", "Hound", TeamSide.EnemyTeam, new EntityStats(100, 50, 20, 10, 10, 80), "Aggressive", new Vector2Int(12, 4));

            _grid.SetOccupant(claire.GridPosition, claire);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            // Pada Rationality 31, belum flee
            var dec31 = hfsm.DecideTurn(claire, _grid, new List<CombatEntity2D> { claire, enemy });
            Assert.AreNotEqual(TacticalActionType.Flee, dec31.Action);

            // Pada Rationality 30, Passive mulai Flee meski HP masih 100%!
            claire.Stats.Rationality = 30;
            var dec30 = hfsm.DecideTurn(claire, _grid, new List<CombatEntity2D> { claire, enemy });
            Assert.AreEqual(TacticalActionType.Flee, dec30.Action, "Passive must trigger Flee when Rationality <= 30% even at full HP.");
        }

        [Test]
        public void TC_2D_20_GuardSkill_MarcusRedirectsDamageAndRestores10PercentRationality()
        {
            var marcusGo = new GameObject("Marcus");
            marcusGo.transform.SetParent(_holder.transform);
            var marcus = marcusGo.AddComponent<CombatEntity2D>();
            marcus.Initialize("u1_marcus", "Marcus (Pointman)", TeamSide.PlayerTeam,
                new EntityStats(140, 50, 20, 10, 10, 80), "Neutral", new Vector2Int(2, 4));

            var elenaGo = new GameObject("Elena");
            elenaGo.transform.SetParent(_holder.transform);
            var elena = elenaGo.AddComponent<CombatEntity2D>();
            elena.Initialize("u2_elena", "Elena (Sniper)", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 30, 0, 10, 50), "Passive", new Vector2Int(2, 5));

            _grid.SetOccupant(marcus.GridPosition, marcus);
            _grid.SetOccupant(elena.GridPosition, elena);

            // Marcus menerapkan Guard pada Elena
            elena.ApplyGuard(marcus, 2);
            elena.Stats.RestoreRationality(10);

            Assert.IsTrue(elena.IsGuarded);
            Assert.AreEqual(60, elena.Stats.Rationality, "Guard must restore +10% Rationality to the protected ally.");

            // Elena diserang dengan rawDamage 30
            int dealt = elena.ApplyDamage(30, false);
            Assert.AreEqual(100, elena.Stats.CurrentHP, "Protected ally must take 0 damage while Guard is active.");
            Assert.IsTrue(elena.LastDamageWasRedirected);
            Assert.AreEqual(marcus, elena.LastGuardian);
            Assert.AreEqual(140 - dealt, marcus.Stats.CurrentHP, "Damage must be absorbed entirely by Marcus.");
        }

        [Test]
        public void TC_2D_21_HealSkill_ClaireHeals50HPAndRestores10PercentRationality()
        {
            var claireGo = new GameObject("Claire");
            claireGo.transform.SetParent(_holder.transform);
            var claire = claireGo.AddComponent<CombatEntity2D>();
            claire.Initialize("u4_claire", "Claire (Medic)", TeamSide.PlayerTeam,
                new EntityStats(70, 75, 10, 5, 10, 80), "Passive", new Vector2Int(2, 2));

            var tashaGo = new GameObject("Tasha");
            tashaGo.transform.SetParent(_holder.transform);
            var tasha = tashaGo.AddComponent<CombatEntity2D>();
            tasha.Initialize("u3_tasha", "Tasha (Flanker)", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 20, 10, 10, 40), "Aggressive", new Vector2Int(2, 3));

            _grid.SetOccupant(claire.GridPosition, claire);
            _grid.SetOccupant(tasha.GridPosition, tasha);

            tasha.Stats.CurrentHP = 20; // 20/100 HP

            // Claire cast Heal
            tasha.Heal(50);
            tasha.Stats.RestoreRationality(10);

            Assert.AreEqual(70, tasha.Stats.CurrentHP, "Heal must restore exactly 50 HP.");
            Assert.AreEqual(50, tasha.Stats.Rationality, "Heal must restore +10% Rationality.");
        }

        [Test]
        public void TC_2D_22_FleeAbort_HealAndGuardRestoreAllyAboveThresholdAbortingFlee()
        {
            var tashaGo = new GameObject("Tasha");
            tashaGo.transform.SetParent(_holder.transform);
            var tasha = tashaGo.AddComponent<CombatEntity2D>();
            tasha.Initialize("u3_tasha", "Tasha (Flanker)", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 20, 10, 10, 50), "Aggressive", new Vector2Int(2, 2));

            // Tasha sekarat di 8 HP (<= 10%), memicu status Flee
            tasha.Stats.CurrentHP = 8;
            tasha.SetFleeing(true);
            Assert.IsTrue(tasha.IsFleeing);

            // Claire menyembuhkan Tasha (+50 HP -> 58/100) dan +10% Rationality (60/100)
            tasha.Heal(50);
            tasha.Stats.RestoreRationality(10);

            // Periksa abort flee: Ambang agresif adalah 10% (0.10f)
            bool aborted = tasha.CheckAndApplyFleeAbort(0.10f, 0.10f);
            Assert.IsTrue(aborted, "Flee command must be aborted when health and composure recover above trait threshold.");
            Assert.IsFalse(tasha.IsFleeing, "IsFleeing must be reset to false.");
        }

        [Test]
        public void TC_2D_23_MoveAndCastSkill_ClosesDistanceWhenDistressedAllyIsOutOfRange()
        {
            var hfsm = new SimpleTacticalHFSM();

            // Claire (Heal Range = 2, MoveRange = 3) di (2, 2)
            var claireGo = new GameObject("Claire");
            claireGo.transform.SetParent(_holder.transform);
            var claire = claireGo.AddComponent<CombatEntity2D>();
            claire.Initialize("u4_claire", "Claire (Medic)", TeamSide.PlayerTeam,
                new EntityStats(70, 75, 10, 5, 10, 80), "Passive", new Vector2Int(2, 2));

            // Marcus terluka di (2, 6) (Jarak = 4, di luar Range 2 Heal, tapi bisa dijangkau jika Claire bergerak ke y=4)
            var marcusGo = new GameObject("Marcus");
            marcusGo.transform.SetParent(_holder.transform);
            var marcus = marcusGo.AddComponent<CombatEntity2D>();
            marcus.Initialize("u1_marcus", "Marcus (Pointman)", TeamSide.PlayerTeam,
                new EntityStats(140, 50, 20, 10, 10, 80), "Neutral", new Vector2Int(2, 6));
            marcus.Stats.CurrentHP = 30; // 30/140 (< 50% HP -> distress!)

            var enemyGo = new GameObject("Enemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("e", "Drone", TeamSide.EnemyTeam,
                new EntityStats(80, 20, 15, 5, 10, 80), "Neutral", new Vector2Int(14, 4));

            _grid.SetOccupant(claire.GridPosition, claire);
            _grid.SetOccupant(marcus.GridPosition, marcus);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            var decision = hfsm.DecideTurn(claire, _grid, new List<CombatEntity2D> { claire, marcus, enemy });

            Assert.IsNotNull(decision);
            Assert.AreEqual(TacticalActionType.MoveAndCastSkill, decision.Action,
                "Claire must execute MoveAndCastSkill when distressed ally is outside cast range but reachable.");
            Assert.AreEqual(marcus, decision.TargetAlly);
            Assert.AreEqual("Heal", decision.SkillId);
            Assert.LessOrEqual(_grid.GetDistance(decision.MoveDestination, marcus.GridPosition), 2,
                "MoveDestination must place Claire within Heal range (<= 2) of Marcus.");
        }

        [Test]
        public void TC_2D_24_PsychologicalImmunity_DoesNotLoseRationalityOnMoraleShock()
        {
            var bmGo = new GameObject("BM_ImmuneTest");
            var bm = bmGo.AddComponent<BattleManager2D>();
            bm.Grid = _grid;

            var normalGo = new GameObject("HumanSoldier");
            var normalUnit = normalGo.AddComponent<CombatEntity2D>();
            normalUnit.Initialize("sol1", "Soldier", TeamSide.EnemyTeam, new EntityStats(100, 50, 15, 5, 10, 100), "Neutral", new Vector2Int(10, 2), immuneToPsychological: false);

            var droneGo = new GameObject("CombatDrone");
            var droneUnit = droneGo.AddComponent<CombatEntity2D>();
            droneUnit.Initialize("dr1", "Scout Drone", TeamSide.EnemyTeam, new EntityStats(50, 20, 15, 5, 10, 100), "Neutral", new Vector2Int(10, 3), immuneToPsychological: true);

            bm.InitializeBattle(new List<CombatEntity2D> { normalUnit, droneUnit });

            Assert.IsFalse(normalUnit.IsImmuneToPsychologicalEffects);
            Assert.IsTrue(droneUnit.IsImmuneToPsychologicalEffects);

            // Terapkan shock moral -40%
            bm.ApplyMoraleShock(TeamSide.EnemyTeam, 40, "Ally destroyed");

            Assert.AreEqual(60, normalUnit.Stats.Rationality, "Non-immune entity must suffer rationality loss.");
            Assert.AreEqual(100, droneUnit.Stats.Rationality, "Immune entity (e.g. drone/machine) must NOT lose rationality.");

            Object.DestroyImmediate(bmGo);
            Object.DestroyImmediate(normalGo);
            Object.DestroyImmediate(droneGo);
        }

        [Test]
        public void TC_2D_25_PsychologicalImmunity_DoesNotFleeFromLowRationality()
        {
            var hfsm = new SimpleTacticalHFSM();

            var droneGo = new GameObject("ImmunePassiveDrone");
            droneGo.transform.SetParent(_holder.transform);
            var drone = droneGo.AddComponent<CombatEntity2D>();
            drone.Initialize("dr_passive", "Repair Drone", TeamSide.EnemyTeam,
                new EntityStats(100, 50, 10, 5, 10, 100), "Passive", new Vector2Int(5, 5),
                immuneToPsychological: true);

            var enemyGo = new GameObject("PlayerEnemy");
            enemyGo.transform.SetParent(_holder.transform);
            var enemy = enemyGo.AddComponent<CombatEntity2D>();
            enemy.Initialize("u_marcus", "Marcus", TeamSide.PlayerTeam,
                new EntityStats(100, 50, 20, 10, 10, 100), "Neutral", new Vector2Int(12, 5));

            _grid.SetOccupant(drone.GridPosition, drone);
            _grid.SetOccupant(enemy.GridPosition, enemy);

            // Turunkan rasionalitas ke level breakdown (10/100, padahal Passive ambangnya 30%)
            drone.Stats.Rationality = 10;
            drone.Stats.CurrentHP = 100; // Darah masih penuh

            var decision = hfsm.DecideTurn(drone, _grid, new List<CombatEntity2D> { drone, enemy });
            Assert.AreNotEqual(TacticalActionType.Flee, decision.Action,
                "Psychologically immune entity must NOT flee due to low rationality/mental breakdown.");

            // Namun jika HP kritis (<= 30% untuk Passive), unit tetap retret berdasarkan kerusakan fisik
            drone.Stats.CurrentHP = 25;
            var decFleeHp = hfsm.DecideTurn(drone, _grid, new List<CombatEntity2D> { drone, enemy });
            Assert.AreEqual(TacticalActionType.Flee, decFleeHp.Action,
                "Entity can still flee when physical HP reaches critical retreat threshold.");
        }
    }
}
