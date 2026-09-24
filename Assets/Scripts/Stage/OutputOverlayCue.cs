using System;
using UnityEngine;

namespace Aetherin
{
    public enum OverlayCueTriggerMode
    {
        OneShot,
        Toggle,
        Hold,
    }

    [Serializable]
    public sealed class OutputOverlayCueData
    {
        public string Id;
        public string Name = "Overlay Cue";
        public OverlayCueTriggerMode TriggerMode = OverlayCueTriggerMode.OneShot;
        [Min(0f)] public float Duration = 1f;
        [Min(0f)] public float StartDelay;
        [Min(0f)] public float PlaybackSpeed = 1f;
        public bool Loop;
        [Tooltip("Starting this cue stops other playing cues with the same non-empty group.")]
        public string ExclusiveGroup;
        public bool RestartOnTrigger = true;
        public bool TriggerOnStageChange;
        public MidiBinding TriggerButton = new();
        public CameraStageLayerSaveData RootLayer;

        public void EnsureInitialized()
        {
            if (string.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(Name)) Name = "Overlay Cue";
            Duration = Mathf.Max(0f, Duration);
            StartDelay = Mathf.Max(0f, StartDelay);
            PlaybackSpeed = Mathf.Max(0f, PlaybackSpeed);
            TriggerButton ??= new MidiBinding();
        }
    }

    public sealed class OutputOverlayCueRuntime : MonoBehaviour, IStageLayerTimeSource
    {
        public OutputOverlayCueData Data { get; private set; }
        public GroupLayer Root { get; private set; }
        public bool IsPlaying { get; private set; }
        public long TriggerEventId { get; private set; }
        public double ElapsedTime => IsPlaying
            ? Math.Max(0d, (Time.unscaledTimeAsDouble - _startedAt - Data.StartDelay) * Data.PlaybackSpeed)
            : 0d;
        public bool IsWaiting => IsPlaying && Time.unscaledTimeAsDouble - _startedAt < Data.StartDelay;

        private double _startedAt;
        private OutputOverlayStage _owner;

        public void Initialize(OutputOverlayCueData data, GroupLayer root, OutputOverlayStage owner)
        {
            Data = data;
            Root = root;
            _owner = owner;
            Data.EnsureInitialized();
            TriggerEventId = 0;
            Stop();
        }

        public void Tick()
        {
            if (Data == null) return;
            Data.EnsureInitialized();

            if (Data.TriggerButton.WasNoteOn)
            {
                switch (Data.TriggerMode)
                {
                    case OverlayCueTriggerMode.Toggle:
                        if (IsPlaying) Stop(); else Trigger();
                        break;
                    default:
                        Trigger();
                        break;
                }
            }
            if (Data.TriggerMode == OverlayCueTriggerMode.Hold && Data.TriggerButton.WasNoteOff)
                Stop();

            if (IsPlaying && Data.TriggerMode == OverlayCueTriggerMode.OneShot &&
                Data.Duration > 0f && ElapsedTime >= Data.Duration)
            {
                if (Data.Loop && Data.PlaybackSpeed > 0f) RestartCycle();
                else Stop();
            }

            if (Root != null) Root.Visible = IsPlaying && !IsWaiting;

            bool blinkOff = IsPlaying && !IsWaiting && (int)(Time.unscaledTimeAsDouble * 3d) % 2 != 0;
            Data.TriggerButton.SetLed(blinkOff ? Color.black : Color.red);
        }

        public void Trigger()
        {
            if (IsPlaying && Data?.RestartOnTrigger == false) return;
            _owner?.StopExclusivePeers(this);
            TriggerEventId++;
            IsPlaying = true;
            _startedAt = Time.unscaledTimeAsDouble;
            if (Root != null)
            {
                Root.Visible = Data.StartDelay <= 0f;
                foreach (StageLayer layer in Root.GetComponentsInChildren<StageLayer>(true))
                    layer.RestartElapsedTime();
            }
        }

        private void RestartCycle()
        {
            _startedAt = Time.unscaledTimeAsDouble;
            if (Root != null)
                foreach (StageLayer layer in Root.GetComponentsInChildren<StageLayer>(true))
                    layer.RestartElapsedTime();
        }

        public void Stop()
        {
            IsPlaying = false;
            if (Root != null) Root.Visible = false;
        }

        public bool TryGetElapsedTime(out double elapsedTime)
        {
            elapsedTime = ElapsedTime;
            return IsPlaying;
        }

        private void OnDestroy() => Data?.TriggerButton?.ClearLed();
    }
}
