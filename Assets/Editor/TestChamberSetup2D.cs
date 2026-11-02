using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using RPGStrategy.Core.Combat;
using RPGStrategy.Demo;

namespace RPGStrategy.Editor
{
    /// <summary>
    /// Editor Script untuk membangun scene 2D Test Chamber Sector 7 Overpass
    /// dengan kamera WASD, grid terbatas 18x10, barikade cover, regu 4 prajurit
    /// (Marcus, Elena, Tasha berbekal Vector Rifle, Claire Medic), dan musuh mesin.
    /// </summary>
    public static class TestChamberSetup2D
    {
        [MenuItem("RPGStrategy/Test Chamber 2D/Prepare 2D Test Chamber (4v3 Sector 7 Overpass)", priority = 1)]
        public static void Prepare2DChamber()
        {
            // 1. Bersihkan chamber 2D lama jika ada
            var existingRoot = GameObject.Find("[TestChamber2D]");
            if (existingRoot != null)
            {
                Undo.DestroyObjectImmediate(existingRoot);
            }

            var root = new GameObject("[TestChamber2D]");
            Undo.RegisterCreatedObjectUndo(root, "Create 2D Test Chamber");

            // 2. Siapkan Kamera 2D WASD
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                cam = camGo.AddComponent<Camera>();
            }

            cam.orthographic = true;
            cam.orthographicSize = 6.0f;
            cam.transform.position = new Vector3(9f, 5f, -10f);

            var camCtrl = cam.GetComponent<TacticalCameraController2D>();
            if (camCtrl == null) camCtrl = cam.gameObject.AddComponent<TacticalCameraController2D>();

            // 3. Buat TacticalGrid2D
            var gridGo = new GameObject("_TacticalGrid2D");
            gridGo.transform.SetParent(root.transform);
            var grid = gridGo.AddComponent<TacticalGrid2D>();
            grid.InitializeGrid(18, 10, 1.0f);
            camCtrl.Grid = grid;

            // 4. Bangun Visual Tiles dan Cover
            var terrainParent = new GameObject("_TerrainTiles").transform;
            terrainParent.SetParent(root.transform);

            var asphaltSprite = TacticalSpriteFactory.CreateAsphaltTileSprite();
            var extractSprite = TacticalSpriteFactory.CreateExtractionTileSprite();
            var halfCoverSprite = TacticalSpriteFactory.CreateHalfCoverSprite();
            var fullCoverSprite = TacticalSpriteFactory.CreateFullCoverSprite();

            // Pasang petak dasar jalan aspal
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    var tileGo = new GameObject($"Tile_{x}_{y}");
                    tileGo.transform.SetParent(terrainParent);
                    tileGo.transform.position = grid.GridToWorld(new Vector2Int(x, y));
                    var sr = tileGo.AddComponent<SpriteRenderer>();
                    sr.sprite = (x == 0) ? extractSprite : asphaltSprite;
                    sr.sortingOrder = 0;
                }
            }

            // Pasang Objek Cover Taktis di Sector 7 Overpass
            var coverParent = new GameObject("_CoverObjects").transform;
            coverParent.SetParent(root.transform);

            // Daftar posisi Half-Cover (Barikade beton rendah)
            var halfCovers = new List<Vector2Int>
            {
                new Vector2Int(2, 3), // Barikade penutup tepat di depan Claire (1, 3)
                new Vector2Int(3, 2), // Barikade di depan Tasha (2, 2)
                new Vector2Int(3, 4), // Barikade di depan Marcus (2, 4)
                new Vector2Int(3, 7), // Barikade koridor utara (Elena)
                new Vector2Int(6, 3), new Vector2Int(6, 6),
                new Vector2Int(9, 2), new Vector2Int(9, 7),
                new Vector2Int(12, 3), new Vector2Int(12, 6)
            };

            foreach (var pos in halfCovers)
            {
                grid.SetCellType(pos, CellType.HalfCover);
                var covGo = new GameObject($"HalfCover_{pos.x}_{pos.y}");
                covGo.transform.SetParent(coverParent);
                covGo.transform.position = grid.GridToWorld(pos);
                var sr = covGo.AddComponent<SpriteRenderer>();
                sr.sprite = halfCoverSprite;
                sr.sortingOrder = 10;
            }

            // Daftar posisi Full-Cover (Pilar beton pejal / rongsokan kontainer)
            var fullCovers = new List<Vector2Int>
            {
                new Vector2Int(6, 1), new Vector2Int(6, 8),
                new Vector2Int(10, 4), new Vector2Int(10, 5)
            };

            foreach (var pos in fullCovers)
            {
                grid.SetCellType(pos, CellType.FullCover);
                var covGo = new GameObject($"FullCover_{pos.x}_{pos.y}");
                covGo.transform.SetParent(coverParent);
                covGo.transform.position = grid.GridToWorld(pos);
                var sr = covGo.AddComponent<SpriteRenderer>();
                sr.sprite = fullCoverSprite;
                sr.sortingOrder = 10;
            }

            // Bake XCOM edge cover data now that all obstacles are placed
            grid.BakeAllCover();

            // 5. Instansiasi Regu 4 Prajurit
            var squadParent = new GameObject("_Squad").transform;
            squadParent.SetParent(root.transform);
            var allCombatants = new List<CombatEntity2D>();

            // Marcus "Grizzly" Vance - Pointman Heavy
            var marcus = CreateEntity2D(squadParent, "u1_marcus", "Marcus (Pointman)", TeamSide.PlayerTeam,
                new EntityStats(140, 40, 24, 16, 10, 85), "LoyalProtector",
                new Vector2Int(2, 4), "Heavy Shotgun", 4, 3,
                TacticalSpriteFactory.CreateUnitSprite("marcus"), grid);
            allCombatants.Add(marcus);

            // Elena "Wraith" Ramos - Sniper Recon
            var elena = CreateEntity2D(squadParent, "u2_elena", "Elena (Sniper)", TeamSide.PlayerTeam,
                new EntityStats(75, 60, 36, 8, 12, 90), "CautiousTactician",
                new Vector2Int(1, 7), "High-Velocity Sniper", 10, 3,
                TacticalSpriteFactory.CreateUnitSprite("elena"), grid);
            allCombatants.Add(elena);

            // Tasha "Viper" Lin - Recon Flanker (Vector Tactical Rifle)
            var tasha = CreateEntity2D(squadParent, "u3_tasha", "Tasha (Flanker)", TeamSide.PlayerTeam,
                new EntityStats(90, 50, 26, 10, 14, 88), "DisciplinedProfessional",
                new Vector2Int(2, 2), "Vector Tactical Rifle", 5, 4,
                TacticalSpriteFactory.CreateUnitSprite("tasha"), grid);
            allCombatants.Add(tasha);

            // Claire "Angel" Miller - Field Medic
            var claire = CreateEntity2D(squadParent, "u4_claire", "Claire (Medic)", TeamSide.PlayerTeam,
                new EntityStats(70, 75, 14, 8, 11, 75), "TimidAltruist",
                new Vector2Int(1, 3), "Compact PDW", 4, 3,
                TacticalSpriteFactory.CreateUnitSprite("claire"), grid);
            allCombatants.Add(claire);

            // 6. Instansiasi Musuh Mesin (Mechanized Hostiles)
            var enemyParent = new GameObject("_Enemies").transform;
            enemyParent.SetParent(root.transform);

            // HOUND-IV Heavy Strider (Mecha Boss)
            var hound = CreateEntity2D(enemyParent, "boss_hound", "HOUND-IV Heavy Strider", TeamSide.EnemyTeam,
                new EntityStats(280, 50, 40, 18, 8, 100), "AutomatedMachine",
                new Vector2Int(15, 4), "Heavy Rotary Cannon", 8, 2,
                TacticalSpriteFactory.CreateHoundBossSprite(), grid);
            allCombatants.Add(hound);

            // Scout Drone Alpha
            var drone1 = CreateEntity2D(enemyParent, "enemy_drone_1", "Scout Drone Alpha", TeamSide.EnemyTeam,
                new EntityStats(50, 20, 16, 4, 15, 100), "AutomatedMachine",
                new Vector2Int(14, 2), "Pulse Blaster", 5, 4,
                TacticalSpriteFactory.CreateScoutDroneSprite(), grid);
            allCombatants.Add(drone1);

            // Scout Drone Beta
            var drone2 = CreateEntity2D(enemyParent, "enemy_drone_2", "Scout Drone Beta", TeamSide.EnemyTeam,
                new EntityStats(50, 20, 16, 4, 15, 100), "AutomatedMachine",
                new Vector2Int(14, 7), "Pulse Blaster", 5, 4,
                TacticalSpriteFactory.CreateScoutDroneSprite(), grid);
            allCombatants.Add(drone2);

            // 7. Siapkan BattleManager2D & FX
            var bmGo = new GameObject("_BattleManager2D");
            bmGo.transform.SetParent(root.transform);
            var bm = bmGo.AddComponent<BattleManager2D>();
            var fx = bmGo.AddComponent<VisualFeedbackFX2D>();

            bm.Grid = grid;
            bm.InitializeBattle(allCombatants);

            // 8. Pastikan EventSystem siap untuk Canvas UI
            TestChamberHUDSetup.EnsureEventSystem();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = bmGo;
            EditorGUIUtility.PingObject(bmGo);

            Debug.Log("[TestChamberSetup2D] 2D Tactical Battle Chamber prepared! " +
                      "(4-Man Squad vs HOUND-IV + Drones). " +
                      "Next: RPGStrategy > Test Chamber 2D > Build Tactical HUD (Canvas). " +
                      "Then Press Play to test.");
        }

        private static CombatEntity2D CreateEntity2D(
            Transform parent, 
            string id, 
            string name, 
            TeamSide side, 
            EntityStats stats, 
            string trait, 
            Vector2Int pos, 
            string weapon, 
            int atkRng, 
            int mvRng, 
            Sprite sprite, 
            TacticalGrid2D grid)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = grid.GridToWorld(pos);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = 20;

            var entity = go.AddComponent<CombatEntity2D>();
            entity.Initialize(id, name, side, stats, trait, pos, weapon, atkRng, mvRng);

            grid.SetOccupant(pos, entity);
            return entity;
        }

        [MenuItem("RPGStrategy/Test Chamber 2D/Clear 2D Test Chamber", priority = 10)]
        public static void Clear2DChamber()
        {
            var root = GameObject.Find("[TestChamber2D]");
            if (root != null)
            {
                Undo.DestroyObjectImmediate(root);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log("[TestChamberSetup2D] Cleared [TestChamber2D] from active scene.");
            }
        }
    }
}
