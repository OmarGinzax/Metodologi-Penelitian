using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RPGStrategy.Core.Combat
{
    /// <summary>
    /// Kontroler Kamera 2D Taktis WASD dengan batas arena ketat (clamped to grid bounds),
    /// zoom scrollwheel, dan transisi pergerakan halus (smooth damping).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TacticalCameraController2D : MonoBehaviour
    {
        [Header("Grid Reference")]
        [SerializeField] private TacticalGrid2D grid;

        [Header("Pan Speeds & Controls")]
        [SerializeField] private float panSpeed = 12.0f;
        [SerializeField] private float smoothTime = 0.12f;

        [Header("Zoom Settings")]
        [SerializeField] private float minZoom = 3.5f;
        [SerializeField] private float maxZoom = 8.5f;
        [SerializeField] private float zoomSpeed = 3.5f;

        private Camera _cam;
        private Vector3 _targetPosition;
        private Vector3 _currentVelocity = Vector3.zero;
        private float _targetZoom;

        public Camera CameraComponent => _cam;
        public TacticalGrid2D Grid { get => grid; set => grid = value; }

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _targetPosition = transform.position;
            _targetZoom = _cam.orthographicSize;

            if (grid == null)
            {
                grid = FindFirstObjectByType<TacticalGrid2D>();
            }
        }

        private void Start()
        {
            CenterOnMap();
        }

        public void CenterOnMap()
        {
            if (grid != null)
            {
                float centerX = (grid.Width * grid.CellSize) * 0.5f + grid.OriginOffset.x;
                float centerY = (grid.Height * grid.CellSize) * 0.5f + grid.OriginOffset.y;
                _targetPosition = new Vector3(centerX, centerY, transform.position.z);
                transform.position = _targetPosition;
            }
        }

        public void FocusOn(Vector3 worldPos)
        {
            _targetPosition = new Vector3(worldPos.x, worldPos.y, transform.position.z);
            ClampTargetPosition();
        }

        private void Update()
        {
            HandlePanInput();
            HandleZoomInput();
            ApplyClampedMovement();
        }

        private void HandlePanInput()
        {
            float h = 0f;
            float v = 0f;
            bool resetFocus = false;

#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;
                if (kb.spaceKey.wasPressedThisFrame) resetFocus = true;
            }
#else
            try
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
                if (Input.GetKeyDown(KeyCode.Space)) resetFocus = true;
            }
            catch (System.Exception) { }
#endif

            if (h != 0f || v != 0f)
            {
                Vector3 moveDir = new Vector3(h, v, 0f).normalized;
                _targetPosition += moveDir * (panSpeed * Time.unscaledDeltaTime);
            }

            if (resetFocus)
            {
                CenterOnMap();
            }
        }

        private void HandleZoomInput()
        {
            float scroll = 0f;

#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                scroll = mouse.scroll.ReadValue().y * 0.01f;
            }
#else
            try
            {
                scroll = Input.GetAxisRaw("Mouse ScrollWheel");
            }
            catch (System.Exception) { }
#endif

            if (Mathf.Abs(scroll) > 0.01f)
            {
                // #region agent log
                try { bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(); File.AppendAllText(@"C:\Ginza\Github\Metodologi-Penelitian\debug-72c643.log", "{\"sessionId\":\"72c643\",\"hypothesisId\":\"E\",\"location\":\"TacticalCameraController2D.HandleZoomInput\",\"message\":\"camera consumed scroll\",\"data\":{\"scroll\":"+scroll.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"overUI\":"+(overUI?"true":"false")+"},\"timestamp\":"+System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+"}\n"); } catch {}
                // #endregion
                _targetZoom -= scroll * zoomSpeed * 10f;
                _targetZoom = Mathf.Clamp(_targetZoom, minZoom, maxZoom);
            }

            _cam.orthographicSize = Mathf.Lerp(_cam.orthographicSize, _targetZoom, Time.unscaledDeltaTime * 10f);
        }

        private void ApplyClampedMovement()
        {
            ClampTargetPosition();

            // Transisi halus damping
            transform.position = Vector3.SmoothDamp(transform.position, _targetPosition, ref _currentVelocity, smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        }

        private void ClampTargetPosition()
        {
            if (grid == null) return;

            float vertExtent = _cam.orthographicSize;
            float horzExtent = vertExtent * (Screen.width / (float)Screen.height);

            float mapMinX = grid.OriginOffset.x;
            float mapMaxX = grid.OriginOffset.x + (grid.Width * grid.CellSize);
            float mapMinY = grid.OriginOffset.y;
            float mapMaxY = grid.OriginOffset.y + (grid.Height * grid.CellSize);

            float minX = mapMinX + horzExtent;
            float maxX = mapMaxX - horzExtent;
            float minY = mapMinY + vertExtent;
            float maxY = mapMaxY - vertExtent;

            // Jika peta lebih kecil dari layar, kunci di tengah sumbu tersebut
            if (minX > maxX)
            {
                _targetPosition.x = (mapMinX + mapMaxX) * 0.5f;
            }
            else
            {
                _targetPosition.x = Mathf.Clamp(_targetPosition.x, minX, maxX);
            }

            if (minY > maxY)
            {
                _targetPosition.y = (mapMinY + mapMaxY) * 0.5f;
            }
            else
            {
                _targetPosition.y = Mathf.Clamp(_targetPosition.y, minY, maxY);
            }
        }
    }
}
