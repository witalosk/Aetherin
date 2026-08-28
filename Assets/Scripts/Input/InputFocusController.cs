using System;
using RosettaUI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aetherin
{
    [Serializable]
    public class InputFocusParams : IParams
    {
        [Tooltip("Game Viewにフォーカスが無くてもキー入力をゲーム側で受け取る (Editorでのみ効果あり)")]
        public bool AlwaysSendInputToGame = true;

        [Tooltip("ウィンドウが非アクティブでも入力を受け取る (ビルド後にも効果あり)")]
        public bool IgnoreApplicationFocus = true;
    }
    
    /// <summary>
    /// フォーカスに関係なく入力を受け取るようにInput Systemの設定を実行時に切り替える
    ///
    /// EditorのGame Viewや、ビルド後のウィンドウがアクティブでない状態でも
    /// キーボード入力をゲーム側で受け取れるようにする
    /// </summary>
    public class InputFocusController : MonoBehaviour, ISaveAndUiTarget
    {
        public IParams Params => _params;
        public string Category => UiCategory.System;

        [SerializeField]
        private InputFocusParams _params = new();

        // 元の設定 (このコンポーネントの設定を永続化させないため、終了時に戻す)
        private InputSettings.EditorInputBehaviorInPlayMode _originalEditorBehaviour;
        private InputSettings.BackgroundBehavior _originalBackgroundBehaviour;
        private bool _originalRunInBackground;

        private bool _lastAlwaysSendInputToGame;
        private bool _lastIgnoreApplicationFocus;

        private void Awake()
        {
            var settings = InputSystem.settings;
            _originalEditorBehaviour = settings.editorInputBehaviorInPlayMode;
            _originalBackgroundBehaviour = settings.backgroundBehavior;
            _originalRunInBackground = Application.runInBackground;

            Apply();
        }

        private void Update()
        {
            // UIやInspectorから変更されたときに追従する
            if (_lastAlwaysSendInputToGame == _params.AlwaysSendInputToGame &&
                _lastIgnoreApplicationFocus == _params.IgnoreApplicationFocus) return;

            Apply();
        }

        private void OnDestroy()
        {
            var settings = InputSystem.settings;
            settings.editorInputBehaviorInPlayMode = _originalEditorBehaviour;
            settings.backgroundBehavior = _originalBackgroundBehaviour;
            Application.runInBackground = _originalRunInBackground;
        }

        private void Apply()
        {
            var settings = InputSystem.settings;

            // Game Viewにフォーカスが無くても入力をゲームに流す
            settings.editorInputBehaviorInPlayMode = _params.AlwaysSendInputToGame
                ? InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView
                : _originalEditorBehaviour;

            // フォーカスを失ってもデバイスを無効化・リセットしない
            settings.backgroundBehavior = _params.IgnoreApplicationFocus
                ? InputSettings.BackgroundBehavior.IgnoreFocus
                : _originalBackgroundBehaviour;

            Application.runInBackground = _params.IgnoreApplicationFocus || _originalRunInBackground;

            _lastAlwaysSendInputToGame = _params.AlwaysSendInputToGame;
            _lastIgnoreApplicationFocus = _params.IgnoreApplicationFocus;
        }

        public Element AdditiveUi()
        {
            return UI.Column(
                UI.Label(() => $"Editor : {InputSystem.settings.editorInputBehaviorInPlayMode}"),
                UI.Label(() => $"Background : {InputSystem.settings.backgroundBehavior}"),
                UI.Label(() => $"Run In Background : {Application.runInBackground}")
            );
        }
    }
}
