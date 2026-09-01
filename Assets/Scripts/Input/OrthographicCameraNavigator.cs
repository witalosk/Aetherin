using UnityEngine;
using UnityEngine.InputSystem;

namespace Aetherin
{
    /// <summary>
    /// Game View上の平行投影カメラをScene View風にパン・ズームする簡易操作。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class OrthographicCameraNavigator : MonoBehaviour
    {
        [SerializeField, Min(0.001f)] private float _minOrthographicSize = 0.1f;
        [SerializeField, Min(0.001f)] private float _maxOrthographicSize = 100f;
        [SerializeField, Min(0.001f)] private float _zoomSensitivity = 0.15f;
        [SerializeField, Min(0.001f)] private float _panSensitivity = 1f;

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (_camera == null || !_camera.orthographic) return;

            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            ApplyZoom(mouse.scroll.ReadValue().y);
            if (mouse.middleButton.isPressed) ApplyPan(mouse.delta.ReadValue());
        }

        private void ApplyZoom(float scrollY)
        {
            if (Mathf.Approximately(scrollY, 0f)) return;

            // 一般的なマウスホイールの1ノッチ(120)を基準に、サイズへ比例するズームにする。
            float zoomFactor = Mathf.Exp(-scrollY / 120f * _zoomSensitivity);
            _camera.orthographicSize = Mathf.Clamp(
                _camera.orthographicSize * zoomFactor,
                Mathf.Min(_minOrthographicSize, _maxOrthographicSize),
                Mathf.Max(_minOrthographicSize, _maxOrthographicSize));
        }

        private void ApplyPan(Vector2 mouseDelta)
        {
            if (mouseDelta.sqrMagnitude <= 0f) return;

            // 画面上の1pxを現在の平行投影範囲に換算する。Scene View同様、掴んだ画面を動かす向き。
            float worldUnitsPerPixel = _camera.orthographicSize * 2f /
                                       Mathf.Max(1, _camera.pixelHeight);
            Vector3 movement = (-transform.right * mouseDelta.x - transform.up * mouseDelta.y) *
                               (worldUnitsPerPixel * _panSensitivity);
            transform.position += movement;
        }

        private void OnValidate()
        {
            _minOrthographicSize = Mathf.Max(0.001f, _minOrthographicSize);
            _maxOrthographicSize = Mathf.Max(_minOrthographicSize, _maxOrthographicSize);
            _zoomSensitivity = Mathf.Max(0.001f, _zoomSensitivity);
            _panSensitivity = Mathf.Max(0.001f, _panSensitivity);
        }
    }
}
