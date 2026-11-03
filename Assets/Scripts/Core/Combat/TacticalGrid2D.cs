using System;
using System.Collections.Generic;
using UnityEngine;

namespace RPGStrategy.Core.Combat
{
    public enum CellType
    {
        Empty,          // Open floor — can walk through
        HalfCover,      // Low obstacle (sandbag / barricade) — blocks movement, NOT LoS
        FullCover,      // High obstacle (pillar / wreck) — blocks movement AND LoS
        Blocked,        // Impassable terrain (cliff / wall boundary)
        ExtractionZone  // Evac point on west edge (X = 0)
    }

    /// <summary>
    /// XCOM-style cover strength.  Stored on obstacle cells; baked into neighbour cover[] edges.
    /// </summary>
    public enum CoverType
    {
        None      = 0,
        HalfCover = 1,
        FullCover = 2
    }

    /// <summary>Cardinal direction index — must match DirVec[] order in TacticalGrid2D.</summary>
    public enum CoverDir { N = 0, E = 1, S = 2, W = 3 }

    /// <summary>
    /// Persisted obstacle so Play Mode can restore cover after GridCell[,] is rebuilt.
    /// Unity cannot serialize a 2D GridCell array, which previously wiped all cover on Awake.
    /// </summary>
    [Serializable]
    public class ObstaclePlacement
    {
        public Vector2Int Pos;
        public CellType Type;
    }

    [Serializable]
    public class GridCell
    {
        public Vector2Int Coordinates;

        /// <summary>What kind of obstacle this cell IS.</summary>
        public CellType Type = CellType.Empty;

        /// <summary>Unit currently standing on this cell (null = empty).</summary>
        public CombatEntity2D Occupant = null;

        /// <summary>
        /// XCOM edge cover baked at load-time (and whenever an obstacle changes).
        /// cover[d] = cover strength this cell receives from the neighbour in direction d.
        /// Example: cover[(int)CoverDir.E] = strength of the obstacle to the EAST of this cell.
        /// A unit here is protected against attackers coming from the East by that value.
        /// </summary>
        [NonSerialized]
        public CoverType[] cover = new CoverType[4];

        public bool IsWalkable =>
            Type != CellType.Blocked && Type != CellType.FullCover && Type != CellType.HalfCover && Occupant == null;

        /// <summary>Cover this cell PROVIDES as an obstacle to its neighbours.</summary>
        public CoverType ObstacleCoverStrength
        {
            get
            {
                switch (Type)
                {
                    case CellType.HalfCover: return CoverType.HalfCover;
                    case CellType.FullCover: return CoverType.FullCover;
                    default:                 return CoverType.None;
                }
            }
        }

        public GridCell(Vector2Int coords, CellType type = CellType.Empty)
        {
            Coordinates = coords;
            Type = type;
            Occupant = null;
            cover = new CoverType[4];
        }
    }

    /// <summary>
    /// Bounded 18×10 tactical grid.
    ///
    /// Cover model — XCOM-style edge cover:
    ///   • Obstacles live on cells (HalfCover / FullCover).
    ///   • BakeCover() stores each cell's 4 edge-cover values (N, E, S, W) from its neighbours.
    ///   • GetCoverAgainst(defender, attacker) reads the correct edge(s) based on
    ///     the attacker's offset delta (dx, dy).  Zero recursion; diagonal attacks
    ///     check both relevant axes and return the stronger cover.
    /// </summary>
    public class TacticalGrid2D : MonoBehaviour
    {
        // Direction vectors — index MUST match CoverDir enum
        private static readonly Vector2Int[] DirVec =
        {
            new Vector2Int( 0,  1),   // N  (index 0)
            new Vector2Int( 1,  0),   // E  (index 1)
            new Vector2Int( 0, -1),   // S  (index 2)
            new Vector2Int(-1,  0)    // W  (index 3)
        };

        [Header("Grid Dimensions (Strict Bounds)")]
        [SerializeField] private int width     = 18;
        [SerializeField] private int height    = 10;
        [SerializeField] private float cellSize = 1.0f;
        [SerializeField] private Vector2 originOffset = Vector2.zero;
        [SerializeField] private List<ObstaclePlacement> obstaclePlacements = new List<ObstaclePlacement>();

        private GridCell[,] _cells;

        public int Width       => width;
        public int Height      => height;
        public float CellSize  => cellSize;
        public Vector2 OriginOffset => originOffset;

        private void Awake() { EnsureGridInitialized(); }

        // ===================================================================
        // Initialisation
        // ===================================================================

        public void InitializeGrid(int w = 18, int h = 10, float size = 1.0f)
        {
            width    = Mathf.Max(4, w);
            height   = Mathf.Max(4, h);
            cellSize = size;
            _cells   = new GridCell[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    CellType t = (x == 0) ? CellType.ExtractionZone : CellType.Empty;
                    _cells[x, y] = new GridCell(new Vector2Int(x, y), t);
                }
            }
            RestorePersistedObstacles();
            BakeAllCover();
        }

        public void EnsureGridInitialized()
        {
            if (_cells == null || _cells.GetLength(0) != width || _cells.GetLength(1) != height)
                InitializeGrid(width, height, cellSize);
        }

        private void RestorePersistedObstacles()
        {
            if (obstaclePlacements == null)
                obstaclePlacements = new List<ObstaclePlacement>();

            if (obstaclePlacements.Count == 0)
                HarvestObstaclesFromCoverObjects();

            for (int i = 0; i < obstaclePlacements.Count; i++)
            {
                var o = obstaclePlacements[i];
                if (o == null || !IsInBounds(o.Pos)) continue;
                if (o.Type != CellType.HalfCover && o.Type != CellType.FullCover && o.Type != CellType.Blocked)
                    continue;
                _cells[o.Pos.x, o.Pos.y].Type = o.Type;
            }
        }

        private void HarvestObstaclesFromCoverObjects()
        {
            Transform searchRoot = transform.parent != null ? transform.parent : transform.root;
            var transforms = searchRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                CellType type;
                if (t.name.StartsWith("HalfCover_")) type = CellType.HalfCover;
                else if (t.name.StartsWith("FullCover_")) type = CellType.FullCover;
                else continue;

                Vector2Int pos;
                if (!TryParseCoverObjectName(t.name, out pos))
                    pos = WorldToGrid(t.position);

                if (!IsInBounds(pos)) continue;
                RecordObstaclePlacement(pos, type);
            }
        }

        private static bool TryParseCoverObjectName(string name, out Vector2Int pos)
        {
            pos = default;
            var parts = name.Split('_');
            if (parts.Length < 3) return false;
            if (!int.TryParse(parts[parts.Length - 2], out int x)) return false;
            if (!int.TryParse(parts[parts.Length - 1], out int y)) return false;
            pos = new Vector2Int(x, y);
            return true;
        }

        private void RecordObstaclePlacement(Vector2Int pos, CellType type)
        {
            if (obstaclePlacements == null)
                obstaclePlacements = new List<ObstaclePlacement>();

            for (int i = obstaclePlacements.Count - 1; i >= 0; i--)
            {
                if (obstaclePlacements[i] != null && obstaclePlacements[i].Pos == pos)
                    obstaclePlacements.RemoveAt(i);
            }

            if (type == CellType.HalfCover || type == CellType.FullCover || type == CellType.Blocked)
                obstaclePlacements.Add(new ObstaclePlacement { Pos = pos, Type = type });
        }

        // ===================================================================
        // 1.  STRICT BOUNDARY ENFORCEMENT
        // ===================================================================

        public bool IsInBounds(Vector2Int pos)
            => pos.x >= 0 && pos.x < width && pos.y >= 0 && pos.y < height;

        public Vector2Int ClampToBounds(Vector2Int pos)
            => new Vector2Int(Mathf.Clamp(pos.x, 0, width - 1),
                              Mathf.Clamp(pos.y, 0, height - 1));

        // ===================================================================
        // 2.  COORDINATE TRANSLATION  (Grid ↔ World 2D XY)
        // ===================================================================

        public Vector3 GridToWorld(Vector2Int gp)
        {
            return new Vector3((gp.x + 0.5f) * cellSize + originOffset.x,
                               (gp.y + 0.5f) * cellSize + originOffset.y, 0f);
        }

        public Vector2Int WorldToGrid(Vector3 wp)
        {
            int gx = Mathf.FloorToInt((wp.x - originOffset.x) / cellSize);
            int gy = Mathf.FloorToInt((wp.y - originOffset.y) / cellSize);
            return ClampToBounds(new Vector2Int(gx, gy));
        }

        // ===================================================================
        // 3.  CELL & OCCUPANCY MANAGEMENT
        // ===================================================================

        public GridCell GetCell(Vector2Int pos)
        {
            EnsureGridInitialized();
            return IsInBounds(pos) ? _cells[pos.x, pos.y] : null;
        }

        public bool IsExtractionTile(Vector2Int pos)
        {
            var c = GetCell(pos);
            return c != null && c.Type == CellType.ExtractionZone;
        }

        public void SetCellType(Vector2Int pos, CellType type)
        {
            EnsureGridInitialized();
            if (!IsInBounds(pos)) return;
            _cells[pos.x, pos.y].Type = type;
            RecordObstaclePlacement(pos, type);
            // Re-bake this cell AND its 4 neighbours (they gain/lose an edge from this obstacle)
            BakeCoverCell(_cells[pos.x, pos.y]);
            for (int d = 0; d < 4; d++)
            {
                var n = GetCell(pos + DirVec[d]);
                if (n != null) BakeCoverCell(n);
            }
        }

        public bool IsOccupied(Vector2Int pos) { var c = GetCell(pos); return c != null && c.Occupant != null; }

        public CombatEntity2D GetOccupant(Vector2Int pos) => GetCell(pos)?.Occupant;

        public bool SetOccupant(Vector2Int pos, CombatEntity2D entity)
        {
            var c = GetCell(pos);
            if (c == null || (!c.IsWalkable && c.Occupant != entity)) return false;
            c.Occupant = entity;
            return true;
        }

        public void ClearOccupant(Vector2Int pos) { var c = GetCell(pos); if (c != null) c.Occupant = null; }

        // ===================================================================
        // 4.  LINE OF SIGHT — Bresenham raycast
        //     HalfCover does NOT block LoS (XCOM rule).
        //     Only FullCover and Blocked block LoS.
        // ===================================================================

        public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        {
            EnsureGridInitialized();
            if (!IsInBounds(from) || !IsInBounds(to)) return false;
            if (from == to) return true;

            int x0 = from.x, y0 = from.y, x1 = to.x, y1 = to.y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int cx = x0, cy = y0;

            while (true)
            {
                if (cx == x1 && cy == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; cx += sx; }
                if (e2 <  dx) { err += dx; cy += sy; }
                if (cx == x1 && cy == y1) break;

                var mid = GetCell(new Vector2Int(cx, cy));
                if (mid != null && (mid.Type == CellType.FullCover || mid.Type == CellType.Blocked))
                    return false;
            }
            return true;
        }

        // ===================================================================
        // 5.  XCOM-STYLE EDGE COVER SYSTEM
        //
        //  BakeCover()   — runs once at load; re-runs on obstacle change.
        //  GetCoverAgainst() — no recursion; reads precomputed cover[] array.
        // ===================================================================

        /// <summary>Bake cover for every cell in the grid.</summary>
        public void BakeAllCover()
        {
            EnsureGridInitialized();
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    BakeCoverCell(_cells[x, y]);
        }

        /// <summary>
        /// For one cell, write cover[0..3] with the obstacle strength of each cardinal neighbour.
        ///   cover[N] = obstacle to the North.  A unit here is protected vs attackers from the North.
        /// </summary>
        public void BakeCoverCell(GridCell cell)
        {
            if (cell == null) return;
            if (cell.cover == null || cell.cover.Length != 4)
                cell.cover = new CoverType[4];

            for (int d = 0; d < 4; d++)
            {
                var nb = GetCell(cell.Coordinates + DirVec[d]);
                cell.cover[d] = nb != null ? nb.ObstacleCoverStrength : CoverType.None;
            }
        }

        /// <summary>
        /// XCOM-style cover calculation — completely recursion-free.
        ///
        /// Algorithm:
        ///   d = attackerPos − defenderPos
        ///   If d.y > 0 → attacker is NORTH  → check N edge (cover[0])
        ///   If d.y &lt; 0 → attacker is SOUTH  → check S edge (cover[2])
        ///   If d.x > 0 → attacker is EAST   → check E edge (cover[1])
        ///   If d.x &lt; 0 → attacker is WEST   → check W edge (cover[3])
        ///   Diagonal attack: checks both axes, returns the stronger value.
        /// </summary>
        public CoverType GetCoverAgainst(Vector2Int defenderPos, Vector2Int attackerPos)
        {
            EnsureGridInitialized();
            if (!IsInBounds(defenderPos) || !IsInBounds(attackerPos)) return CoverType.None;
            if (defenderPos == attackerPos) return CoverType.None;

            var cell = GetCell(defenderPos);
            if (cell == null) return CoverType.None;

            if (cell.cover == null || cell.cover.Length != 4)
                BakeCoverCell(cell);

            Vector2Int delta = attackerPos - defenderPos;
            CoverType best = CoverType.None;

            if (delta.y > 0) best = CoverMax(best, cell.cover[(int)CoverDir.N]);
            if (delta.y < 0) best = CoverMax(best, cell.cover[(int)CoverDir.S]);
            if (delta.x > 0) best = CoverMax(best, cell.cover[(int)CoverDir.E]);
            if (delta.x < 0) best = CoverMax(best, cell.cover[(int)CoverDir.W]);

            return best;
        }

        /// <summary>True when the attacker's side has no cover obstacle (flanked).</summary>
        public bool IsFlanked(Vector2Int defenderPos, Vector2Int attackerPos)
            => GetCoverAgainst(defenderPos, attackerPos) == CoverType.None;

        private static CoverType CoverMax(CoverType a, CoverType b)
            => (CoverType)Mathf.Max((int)a, (int)b);

        /// <summary>
        /// Returns the baked cover[4] array for UI shield-icon rendering.
        /// Index: 0=N, 1=E, 2=S, 3=W.  Returns null if pos is out of bounds.
        /// </summary>
        public CoverType[] GetCellCoverEdges(Vector2Int pos)
        {
            var cell = GetCell(pos);
            if (cell == null) return null;
            if (cell.cover == null || cell.cover.Length != 4) BakeCoverCell(cell);
            return cell.cover;
        }

        // ===================================================================
        // 6.  BFS MOVEMENT REACHABILITY SOLVER
        // ===================================================================

        public int GetDistance(Vector2Int a, Vector2Int b)
            => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        public List<Vector2Int> GetReachableTiles(Vector2Int start, int maxRange)
        {
            EnsureGridInitialized();
            var reachable = new List<Vector2Int>();
            if (!IsInBounds(start) || maxRange <= 0) return reachable;

            var startOccupant = GetOccupant(start);
            TeamSide startTeam = startOccupant != null ? startOccupant.Team : TeamSide.PlayerTeam;

            var queue   = new Queue<(Vector2Int pos, int dist)>();
            var visited = new HashSet<Vector2Int>();
            queue.Enqueue((start, 0));
            visited.Add(start);

            Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

            while (queue.Count > 0)
            {
                var (cur, dist) = queue.Dequeue();

                // Only tiles without an occupant are valid landing spots
                if (dist > 0 && GetOccupant(cur) == null)
                    reachable.Add(cur);

                if (dist >= maxRange) continue;

                foreach (var dir in dirs)
                {
                    Vector2Int next = cur + dir;
                    if (!IsInBounds(next) || visited.Contains(next)) continue;

                    var cell = GetCell(next);
                    if (cell == null || cell.Type == CellType.Blocked || cell.Type == CellType.FullCover || cell.Type == CellType.HalfCover) continue;

                    // Can pass through allies but NOT through enemies
                    if (cell.Occupant != null && cell.Occupant.Team != startTeam) continue;

                    visited.Add(next);
                    queue.Enqueue((next, dist + 1));
                }
            }

            return reachable;
        }
    }
}
