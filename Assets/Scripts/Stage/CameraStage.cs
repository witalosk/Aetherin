using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// カメラで撮ったシーンをそのまま出力するステージ
    /// カメラと被写体はこのオブジェクトの子に置く想定
    /// (Nextとして複製されたときはStageManagerがワールドオフセットを加えるため、複製元と互いに映り込まない)
    /// </summary>
    public class CameraStage : StageBase
    {
        [SerializeField] private Camera _camera;

        protected override void Start()
        {
            base.Start();

            if (_camera == null) _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
            {
                Debug.LogError($"[CameraStage] {name} にカメラが設定されていません", this);
                return;
            }

            _camera.targetTexture = OutputTexture;
        }

        protected override void OnDestroy()
        {
            if (_camera != null) _camera.targetTexture = null;
            base.OnDestroy();
        }
    }
}
