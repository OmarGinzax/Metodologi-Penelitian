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
    }
}
