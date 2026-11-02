using UnityEngine;

namespace RPGStrategy.Demo
{
    /// <summary>
    /// Factory prosedural pembuat Sprite 2D berkualitas tinggi langsung di memori.
    /// Menghasilkan tekstur modern post-apocalyptic untuk medan tempur Sector 7,
    /// barikade Half/Full Cover, siluet 4 prajurit, dan mecha/drone musuh.
    /// </summary>
    public static class TacticalSpriteFactory
    {
        // ─── TILE & TERRAIN SPRITES ──────────────────────────────────────────────

        public static Sprite CreateAsphaltTileSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color baseCol = new Color(0.18f, 0.20f, 0.22f); // Aspal abu-abu gelap
            Color borderCol = new Color(0.14f, 0.15f, 0.17f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    bool isBorder = (x == 0 || x == res - 1 || y == 0 || y == res - 1);
                    float noise = Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.04f;
                    Color c = isBorder ? borderCol : baseCol + new Color(noise, noise, noise);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        public static Sprite CreateExtractionTileSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color baseCol = new Color(0.15f, 0.28f, 0.24f); // Pelat baja hijau industrial
            Color stripeCol = new Color(0.2f, 0.8f, 0.5f, 0.8f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    bool isStripe = (x + y) % 16 < 4;
                    tex.SetPixel(x, y, isStripe ? stripeCol : baseCol);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        public static Sprite CreateHalfCoverSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color concrete = new Color(0.45f, 0.48f, 0.50f);
            Color hazardYellow = new Color(0.85f, 0.75f, 0.15f);
            Color hazardBlack = new Color(0.12f, 0.12f, 0.12f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    // Barikade rendah (mengisi setengah bawah petak)
                    if (y < res * 0.65f)
                    {
                        // Garis hazard di atas barikade
                        if (y > res * 0.48f)
                        {
                            bool isHazard = (x + y) % 12 < 6;
                            tex.SetPixel(x, y, isHazard ? hazardYellow : hazardBlack);
                        }
                        else
                        {
                            tex.SetPixel(x, y, concrete);
                        }
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        public static Sprite CreateFullCoverSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color steel = new Color(0.28f, 0.32f, 0.38f);
            Color darkBorder = new Color(0.15f, 0.18f, 0.22f);
            Color rust = new Color(0.48f, 0.25f, 0.15f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    bool isBorder = (x < 3 || x >= res - 3 || y < 3 || y >= res - 3);
                    float noise = Mathf.PerlinNoise(x * 0.2f, y * 0.2f);
                    Color c = isBorder ? darkBorder : (noise > 0.65f ? rust : steel);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        public static Sprite CreateBoundaryWallSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color railCol = new Color(0.35f, 0.38f, 0.42f);
            Color reflector = new Color(0.9f, 0.2f, 0.2f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    if (y >= res * 0.2f && y <= res * 0.8f)
                    {
                        bool isReflect = (x >= res * 0.4f && x <= res * 0.6f && y >= res * 0.45f && y <= res * 0.55f);
                        tex.SetPixel(x, y, isReflect ? reflector : railCol);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        // ─── SQUAD 4-MAN PLACEHOLDER SPRITES ────────────────────────────────────

        public static Sprite CreateUnitSprite(string role, int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            Color bodyCol;
            switch (role.ToLower())
            {
                case "marcus": // Pointman Heavy (Deep Navy & Titanium)
                    bodyCol = new Color(0.22f, 0.38f, 0.65f);
                    break;
                case "elena":  // Sniper Recon (Slate Charcoal)
                    bodyCol = new Color(0.30f, 0.35f, 0.42f);
                    break;
                case "tasha":  // Flanker (Vector Rifle Cyan/Teal)
                    bodyCol = new Color(0.18f, 0.58f, 0.55f);
                    break;
                case "claire": // Combat Medic (Field Green & White)
                    bodyCol = new Color(0.25f, 0.55f, 0.35f);
                    break;
                default:
                    bodyCol = new Color(0.3f, 0.4f, 0.5f);
                    break;
            }

            Color outline = new Color(0.08f, 0.10f, 0.12f);
            Color accent = Color.white;

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
            float radius = res * 0.38f;

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        bool isEdge = dist >= radius - 2.5f;
                        Color c = isEdge ? outline : bodyCol;

                        // Tambahkan simbol peran di tengah
                        if (role.ToLower() == "claire") // Palang Medis
                        {
                            bool inCross = (Mathf.Abs(x - center.x) < 3.5f && Mathf.Abs(y - center.y) < 10f) ||
                                           (Mathf.Abs(y - center.y) < 3.5f && Mathf.Abs(x - center.x) < 10f);
                            if (inCross) c = Color.white;
                        }
                        else if (role.ToLower() == "elena") // Crosshair
                        {
                            bool inRing = Mathf.Abs(dist - 8f) < 1.5f || (Mathf.Abs(x - center.x) < 1f && dist < 12f) || (Mathf.Abs(y - center.y) < 1f && dist < 12f);
                            if (inRing) c = Color.yellow;
                        }
                        else if (role.ToLower() == "tasha") // Vector Chevron / Flank Arrows
                        {
                            bool inArrow = (y - center.y > Mathf.Abs(x - center.x) - 4f) && (y - center.y < Mathf.Abs(x - center.x) + 3f) && dist < 12f;
                            if (inArrow) c = new Color(0.6f, 1.0f, 0.9f);
                        }
                        else if (role.ToLower() == "marcus") // Heavy Armor Shield
                        {
                            bool inShield = (dist < 10f && y < center.y + 6f);
                            if (inShield) c = new Color(0.85f, 0.9f, 1.0f);
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        // ─── CASUALTY & DOWNED PLACEHOLDER SPRITES ──────────────────────────────

        /// <summary>
        /// Sprite penanda prajurit gugur (K.I.A. Casualty Marker).
        /// Menampilkan pelat baja hangus dengan border merah hazard dan palang silang putih/merah.
        /// </summary>
        public static Sprite CreateDeadAgentSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            Color basePlate = new Color(0.14f, 0.16f, 0.18f);           // Pelat baja gelap hangus
            Color borderRed = new Color(0.70f, 0.18f, 0.18f);           // Garis peringatan casualty merah
            Color boneWhite = new Color(0.92f, 0.90f, 0.88f);           // Simbol silang / tulang
            Color bloodCrimson = new Color(0.55f, 0.10f, 0.12f, 0.85f); // Aksen darah / plasma

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
            float radius = res * 0.38f;

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        bool isEdge = dist >= radius - 2.5f;
                        Color c = isEdge ? borderRed : basePlate;

                        // Tambahkan bercak plasma / darah di pelat
                        float splash = Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                        if (splash > 0.65f && !isEdge)
                        {
                            c = Color.Lerp(c, bloodCrimson, 0.6f);
                        }

                        // Simbol casualty besar "X" (Palang Gugur Taktis)
                        float dx = Mathf.Abs(x - center.x);
                        float dy = Mathf.Abs(y - center.y);
                        float diagDist = Mathf.Abs(dx - dy);
                        if (diagDist < 2.5f && dist < radius - 3f)
                        {
                            c = boneWhite;
                        }

                        // Titik pusat merah peringatan
                        if (dist < 3.5f)
                        {
                            c = borderRed;
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        /// <summary>
        /// Sprite penanda rongsokan musuh hancur (Scrap / Wreckage).
        /// </summary>
        public static Sprite CreateDeadEnemySprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            Color scrapMetal = new Color(0.12f, 0.13f, 0.15f);
            Color sparkOrange = new Color(0.95f, 0.45f, 0.10f);
            Color burntDark = new Color(0.06f, 0.07f, 0.08f);

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
            float radius = res * 0.38f;

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        bool isEdge = dist >= radius - 2.5f;
                        float noise = Mathf.PerlinNoise(x * 0.3f, y * 0.3f);
                        Color c = isEdge ? burntDark : (noise > 0.55f ? burntDark : scrapMetal);

                        // Percikan api / sirkuit putus
                        if ((Mathf.Abs(x - center.x) < 2f && Mathf.Abs(y - center.y) < 8f) ||
                            (Mathf.Abs(y - center.y) < 2f && Mathf.Abs(x - center.x) < 8f))
                        {
                            if (noise > 0.4f) c = sparkOrange;
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        // ─── ENEMY MACHINE PLACEHOLDER SPRITES ──────────────────────────────────

        public static Sprite CreateHoundBossSprite(int res = 128)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color armorCol = new Color(0.20f, 0.15f, 0.18f); // Baja hitam pekat
            Color redGlow = new Color(0.95f, 0.15f, 0.15f);  // Sensor merah menyala
            Color border = new Color(0.08f, 0.06f, 0.08f);

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
            float radius = res * 0.42f;

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    if (dist <= radius)
                    {
                        bool isEdge = dist >= radius - 3.5f;
                        Color c = isEdge ? border : armorCol;

                        // Sensor mata merah tajam di depan
                        if (Mathf.Abs(y - center.y) < 6f && (x > center.x - 16f && x < center.x + 16f))
                        {
                            c = redGlow;
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res * 0.5f);
        }

        public static Sprite CreateScoutDroneSprite(int res = 64)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color droneCol = new Color(0.35f, 0.28f, 0.22f);
            Color orangeEye = new Color(1.0f, 0.55f, 0.1f);
            Color border = new Color(0.12f, 0.10f, 0.08f);

            Vector2 center = new Vector2(res * 0.5f, res * 0.5f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    // Bentuk diamond taktis
                    float manhattan = Mathf.Abs(x - center.x) + Mathf.Abs(y - center.y);
                    if (manhattan <= res * 0.42f)
                    {
                        bool isEdge = manhattan >= res * 0.42f - 2f;
                        Color c = isEdge ? border : droneCol;

                        if (Vector2.Distance(new Vector2(x, y), center) < 5f)
                        {
                            c = orangeEye;
                        }

                        tex.SetPixel(x, y, c);
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }

        // ─── TACTICAL OVERLAY SPRITES ───────────────────────────────────────────

        public static Sprite CreateOverlayTileSprite(Color color, int res = 32)
        {
            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            Color border = new Color(color.r, color.g, color.b, 0.9f);

            for (int x = 0; x < res; x++)
            {
                for (int y = 0; y < res; y++)
                {
                    bool isBorder = (x == 0 || x == res - 1 || y == 0 || y == res - 1);
                    tex.SetPixel(x, y, isBorder ? border : color);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), res);
        }
    }
}
