using System;
using System.Collections.Generic;
using RosettaUI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aetherin
{
    /// <summary>
    /// キーボードのタップでテンポを管理する<see cref="IBeatManager"/>の実装
    ///
    /// サブ拍 (拍) のタップでテンポを決め、主拍 (小節頭) のタップで小節の頭を合わせる
    /// 主拍が何拍ごとに来るかから拍子を推定する
    /// </summary>
    public class BeatManager : MonoBehaviour, IBeatManager, IUiTarget
    {
        public float Bpm => _beat.Bpm;
        public bool IsRunning => _beat.IsRunning;
        public int BeatsPerBar => _beat.BeatsPerBar;
        public int LastCountedBeatsPerBar { get; private set; }
        public int CountingBeats => _tapsSinceMainTap;
        public bool IsBeatsPerBarEstimated { get; private set; }

        public float BeatPhase => _beat.Phase;
        public int BeatCount => _beat.BeatCount;
        public int BeatInBar => _beat.BeatInBar;
        public bool WasBeat => _beat.WasBeat;

        public float BarPhase => BeatsPerBar <= 0 ? 0f : (BeatInBar + BeatPhase) / BeatsPerBar;
        public int BarCount { get; private set; }
        public bool WasBar => _lastBarFrame == Time.frameCount;

        public IParams Params => _params;
        public bool FoldParams => true;

        public event Action<int> OnBeat;
        public event Action<int> OnBar;

        [SerializeField]
        private BeatManagerParams _params = new();

        private readonly BeatTrack _beat = new();

        /// <summary> 直近の主拍から数えたタップ数 (主拍自身は含まない) </summary>
        private int _tapsSinceMainTap;

        /// <summary> 主拍から連続してタップされているか (途切れたら拍子のカウントは無効) </summary>
        private bool _isGroupValid;

        /// <summary> 1つ前の小節で数えたタップ数 (同じ値が2回続いたら拍子の変更を採用する) </summary>
        private int _previousCountedBeatsPerBar;

        private int _lastBarFrame = -1;

        private static readonly Color BarColor = new(0.35f, 0.85f, 1f);
        private static readonly Color BeatColor = new(1f, 0.65f, 0.25f);

        // 入力が届いているかの確認用
        private bool _isKeyboardFound;
        private Key _lastPressedKey = Key.None;
        private int _lastPressedFrame = -1;

        #region IBeatManager

        public void TapMain()
        {
            bool isContinued = _beat.Tap(isBarStart: true);

            // 主拍自身を含めた拍数が、直前の小節の拍子になる
            if (isContinued && _isGroupValid) AdoptBeatsPerBar(_tapsSinceMainTap + 1);
            if (!isContinued) ResetEstimation();

            _tapsSinceMainTap = 0;
            _isGroupValid = true;
        }

        public void TapSub()
        {
            bool isContinued = _beat.Tap(isBarStart: false);

            if (isContinued)
            {
                _tapsSinceMainTap++;
                return;
            }

            // タップ列が途切れた場合、次の主拍までのタップ数は小節の拍数にならない
            _tapsSinceMainTap = 0;
            _isGroupValid = false;
            ResetEstimation();
        }

        public void Stop()
        {
            _beat.Stop();

            BarCount = 0;
            _tapsSinceMainTap = 0;
            _isGroupValid = false;
            ResetEstimation();
        }

        public void SetBpm(float bpm) => _beat.SetBpm(bpm);

        public void SetBeatsPerBar(int beatsPerBar) => _params.BeatsPerBar = Mathf.Clamp(beatsPerBar, 1, 16);

        /// <summary>
        /// 主拍から次の主拍までに数えたタップ数を拍子として採用する
        /// </summary>
        private void AdoptBeatsPerBar(int count)
        {
            LastCountedBeatsPerBar = count;

            if (!_params.EstimateBeatsPerBar || count < 1 || count > _params.MaxBeatsPerBar) return;

            // 未確定なら即採用、確定後の変更は同じ拍数が2回続いたときだけ採用する
            // (サブ拍を1回叩き損ねただけで拍子が変わってしまうのを防ぐ)
            if (!IsBeatsPerBarEstimated || count == _previousCountedBeatsPerBar)
            {
                _params.BeatsPerBar = count;
                IsBeatsPerBarEstimated = true;
            }

            _previousCountedBeatsPerBar = count;
        }

        private void ResetEstimation()
        {
            IsBeatsPerBarEstimated = false;
            LastCountedBeatsPerBar = 0;
            _previousCountedBeatsPerBar = 0;
        }

        #endregion

        private void Awake()
        {
            _beat.OnBeat += HandleBeat;
        }

        private void OnDestroy()
        {
            _beat.OnBeat -= HandleBeat;
        }

        private void Update()
        {
            ApplyParams();
            ReadKeyboard();

            _beat.Update(Time.unscaledDeltaTime);
        }

        private void ApplyParams()
        {
            _beat.BeatsPerBar = _params.BeatsPerBar;
            _beat.TapTimeout = _params.TapTimeout;
            _beat.TapHistoryCount = _params.TapHistoryCount;
            _beat.MinBpm = _params.MinBpm;
            _beat.MaxBpm = _params.MaxBpm;
        }

        private void HandleBeat(int beatCount)
        {
            if (_beat.BeatInBar == 0)
            {
                BarCount++;
                _lastBarFrame = Time.frameCount;
                OnBar?.Invoke(BarCount);
            }

            OnBeat?.Invoke(beatCount);
        }

        private void ReadKeyboard()
        {
            var keyboard = Keyboard.current;
            _isKeyboardFound = keyboard != null;
            if (keyboard == null) return;

            // キー入力が届いているかを画面で確認できるようにする
            if (keyboard.anyKey.wasPressedThisFrame)
            {
                foreach (var keyControl in keyboard.allKeys)
                {
                    if (!keyControl.wasPressedThisFrame) continue;

                    _lastPressedKey = keyControl.keyCode;
                    _lastPressedFrame = Time.frameCount;
                    break;
                }
            }

            if (WasKeyPressed(keyboard, _params.MainTapKey)) TapMain();
            if (WasKeyPressed(keyboard, _params.SubTapKey)) TapSub();
        }

        private static bool WasKeyPressed(Keyboard keyboard, Key key)
        {
            if (key == Key.None) return false;

            return keyboard[key]?.wasPressedThisFrame ?? false;
        }

        #region UI

        public Element AdditiveUi()
        {
            return UI.Column(
                UI.Row(
                    UI.Label(() => IsRunning ? $"<size=24>{Bpm:F1}</size> BPM" : "-- BPM").SetWidth(200f),
                    UI.Label(() => IsRunning
                        ? $"Bar {BarCount} : {BeatInBar + 1}/{BeatsPerBar}"
                        : $"tap [{_params.MainTapKey}] [{_params.SubTapKey}] to start").SetWidth(180f),
                    UI.Label(() => !IsRunning
                        ? "counted -"
                        : IsBeatsPerBarEstimated
                            ? $"counted {LastCountedBeatsPerBar} beats/bar (now {_tapsSinceMainTap + 1})"
                            : $"counting {_tapsSinceMainTap + 1} ...")
                ),
                UI.Row(
                    // 左が小節頭 (主拍)、右が拍 (サブ拍) のパルス
                    CreatePulseElement(_params.CellSize * 1.5f, () => BarPhase, () => WasBar, BarColor),
                    CreatePulseElement(_params.CellSize * 1.5f, () => BeatPhase, () => WasBeat, BeatColor),
                    UI.DynamicElementOnStatusChanged(
                        // 拍子が変わったらインジケータを作り直す
                        readStatus: () => BeatsPerBar,
                        build: _ => UI.Row(CreateBeatCells()))
                ),
                CreatePhaseBarElement("Beat", () => BeatPhase, BeatColor),
                CreatePhaseBarElement("Bar ", () => BarPhase, BarColor),
                UI.Slider("BPM", () => Bpm, SetBpm, _params.MinBpm, _params.MaxBpm),
                UI.Slider("Beats / Bar", () => BeatsPerBar, SetBeatsPerBar, 1, 16),
                UI.Row(
                    UI.Button(UI.Label(() => $"Tap Main [{_params.MainTapKey}]"), TapMain),
                    UI.Button(UI.Label(() => $"Tap Sub [{_params.SubTapKey}]"), TapSub),
                    UI.Button("Stop", Stop)
                ),
                UI.Label(() => _isKeyboardFound
                    ? $"Keyboard : OK / last key : {_lastPressedKey} (frame {_lastPressedFrame})"
                    : "Keyboard : not found")
            );
        }

        /// <summary>
        /// 位相に応じて減衰するパルスインジケータ
        /// </summary>
        private static Element CreatePulseElement(float size, Func<float> readPhase, Func<bool> readTrigger, Color color)
        {
            return UI.Label("")
                .SetWidth(size)
                .SetHeight(size)
                .RegisterUpdateCallback(element =>
                {
                    var lit = readTrigger() ? Color.white : color;
                    float brightness = Mathf.Max(0.12f, 1f - Mathf.Clamp01(readPhase()));
                    element.SetBackgroundColor(Color.Lerp(Color.black, lit, brightness));
                });
        }

        /// <summary>
        /// 小節内の拍位置を示すセル列
        /// </summary>
        private IEnumerable<Element> CreateBeatCells()
        {
            for (int i = 0; i < BeatsPerBar; i++)
            {
                int index = i;

                yield return UI.Label("")
                    .SetWidth(_params.CellSize)
                    .SetHeight(_params.CellSize)
                    .RegisterUpdateCallback(element =>
                    {
                        bool isCurrent = IsRunning && BeatInBar == index;
                        var baseColor = index == 0 ? BarColor : BeatColor;
                        element.SetBackgroundColor(isCurrent ? baseColor : baseColor * 0.18f);
                    });
            }
        }

        /// <summary>
        /// 位相を示すバー
        /// </summary>
        private Element CreatePhaseBarElement(string label, Func<float> readPhase, Color color)
        {
            float width = _params.CellSize * 10f;

            var fill = UI.Label("")
                .SetHeight(6f)
                .SetBackgroundColor(color)
                .RegisterUpdateCallback(element =>
                    element.SetWidth(Mathf.Max(1f, width * Mathf.Clamp01(readPhase()))));

            return UI.Row(
                UI.Label(label).SetWidth(40f),
                UI.Row(fill)
                    .SetWidth(width)
                    .SetHeight(6f)
                    .SetBackgroundColor(new Color(1f, 1f, 1f, 0.08f))
            );
        }

        #endregion
    }
}
