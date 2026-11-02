using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RPGStrategy.Core.Combat;

namespace RPGStrategy.Demo
{
    /// <summary>
    /// Modul Visual Feedback 2D untuk aksi kombat:
    /// Bullet Tracers (LineRenderer proyektil kilat), Floating Combat Text (damage popup),
    /// dan Grid Range Highlight Overlay.
    /// </summary>
    public class VisualFeedbackFX2D : MonoBehaviour
    {
        private static VisualFeedbackFX2D _instance;
        public static VisualFeedbackFX2D Instance => _instance;

        private readonly List<GameObject> _activeOverlays = new List<GameObject>();

        private void Awake()
        {
            if (_instance == null) _instance = this;
            else if (_instance != this) Destroy(gameObject);
        }

        // =========================================================================
        // 1. BULLET TRACER 2D (PROYEKTIL KILAT)
        // =========================================================================

        public void PlayBulletTracer(Vector3 from, Vector3 to, bool isBurst = false, Action onHit = null)
        {
            StartCoroutine(BulletTracerRoutine(from, to, isBurst, onHit));
        }

        private IEnumerator BulletTracerRoutine(Vector3 from, Vector3 to, bool isBurst, Action onHit)
        {
            int shots = isBurst ? 3 : 1;
            Color tracerColor = isBurst ? new Color(0.2f, 1.0f, 0.9f) : new Color(1.0f, 0.85f, 0.2f);

            for (int i = 0; i < shots; i++)
            {
                var tracerGo = new GameObject("BulletTracer");
                var line = tracerGo.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = tracerColor;
                line.endColor = new Color(tracerColor.r, tracerColor.g, tracerColor.b, 0.1f);
                line.startWidth = 0.08f;
                line.endWidth = 0.02f;
                line.positionCount = 2;
                line.useWorldSpace = true;
                line.sortingOrder = 50;

                Vector3 startPos = from + new Vector3(0f, 0f, -0.1f);
                Vector3 endPos = to + new Vector3(0f, 0f, -0.1f);

                line.SetPosition(0, startPos);
                line.SetPosition(1, startPos);

                float elapsed = 0f;
                float duration = 0.06f;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    line.SetPosition(1, Vector3.Lerp(startPos, endPos, t));
                    yield return null;
                }

                line.SetPosition(1, endPos);
                Destroy(tracerGo, 0.04f);

                if (i == shots - 1)
                {
                    onHit?.Invoke();
                }

                if (isBurst)
                {
                    yield return new WaitForSeconds(0.04f);
                }
            }
        }

        // =========================================================================
        // 2. FLOATING COMBAT TEXT (POPUP DAMAGE)
        // =========================================================================

        public void SpawnFloatingText(Vector3 worldPos, string message, Color color)
        {
            StartCoroutine(FloatingTextRoutine(worldPos, message, color));
        }

        private IEnumerator FloatingTextRoutine(Vector3 worldPos, string message, Color color)
        {
            var textGo = new GameObject("FloatingCombatText");
            textGo.transform.position = worldPos + new Vector3(0f, 0.6f, -0.5f);

            var textMesh = textGo.AddComponent<TextMesh>();
            textMesh.text = message;
            textMesh.fontSize = 28;
            textMesh.characterSize = 0.08f;
            textMesh.color = color;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontStyle = FontStyle.Bold;

            var renderer = textGo.GetComponent<MeshRenderer>();
            renderer.sortingOrder = 100;

            float elapsed = 0f;
            float duration = 0.75f;
            Vector3 startPos = textGo.transform.position;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                textGo.transform.position = startPos + new Vector3(0f, t * 0.75f, 0f);
                textMesh.color = new Color(color.r, color.g, color.b, 1f - (t * t));
                yield return null;
            }

            Destroy(textGo);
        }

        // =========================================================================
        // 3. GRID RANGE HIGHLIGHT OVERLAYS
        // =========================================================================

        public void ClearOverlays()
        {
            foreach (var go in _activeOverlays)
            {
                if (go != null) Destroy(go);
            }
            _activeOverlays.Clear();
        }

        public void ShowMoveRange(List<Vector2Int> tiles, TacticalGrid2D grid)
        {
            ClearOverlays();
            if (grid == null || tiles == null) return;

            var sprite = TacticalSpriteFactory.CreateOverlayTileSprite(new Color(0.2f, 0.7f, 1.0f, 0.35f));

            foreach (var tile in tiles)
            {
                var go = new GameObject($"MoveOverlay_{tile.x}_{tile.y}");
                go.transform.position = grid.GridToWorld(tile);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingOrder = 5;
                _activeOverlays.Add(go);
            }
        }
    }
}
