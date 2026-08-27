using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aetherin
{
    /// <summary>
    /// タップテンポで駆動する拍のクロック
    /// MonoBehaviourではなく、<see cref="BeatManager"/>から毎フレーム<see cref="Update"/>される
    /// </summary>
    public class BeatTrack
    {
        public float Bpm { get; private set; } = 120f;
        public float Phase { get; private set; }
        public int BeatCount { get; private set; }
        public int BeatsPerBar { get; set; } = 4;
        public int BeatInBar => BeatsPerBar <= 0 ? 0 : BeatCount % BeatsPerBar;
        public bool IsRunning { get; private set; }

        public bool WasBeat => _lastBeatFrame == Time.frameCount;

        /// <summary> 1拍の長さ (秒) </summary>
        public float BeatPeriod => Bpm > 0f ? 60f / Bpm : 0f;

        public event Action<int> OnBeat;

        /// <summary> 現在のタップ列に含まれるタップ回数 (UI表示用) </summary>
        public int TapCount { get; private set; }

        /// <summary> タップ間隔の履歴 (秒) </summary>
        private readonly List<float> _intervals = new();

        private float _lastTapTime = float.NegativeInfinity;
        private int _lastBeatFrame = -1;

        // BeatManagerのParamsから設定される
        public float TapTimeout { get; set; } = 2f;
        public int TapHistoryCount { get; set; } = 6;
        public float MinBpm { get; set; } = 40f;
        public float MaxBpm { get; set; } = 300f;

        /// <summary>
        /// 拍をタップする。タップ間隔からBPMを求めつつ、拍の頭をこの瞬間に合わせる
        /// 主拍・サブ拍のどちらも1つの拍として同じタップ列に入る
        /// </summary>
        /// <param name="isBarStart">小節頭 (主拍) のタップか</param>
        /// <returns>直前のタップから連続したタップ列として扱われた場合はtrue</returns>
        public bool Tap(bool isBarStart)
        {
            float time = Time.unscaledTime;
            float interval = time - _lastTapTime;
            bool isContinued = interval <= TapTimeout;

            // タップが拍の頭を少し過ぎてから来た場合、その拍は自動進行側で既に数えられている
            // 二重に数えるとタップ数より拍が先に進んでしまうため、その場合は数えない
            bool isCountedByClock = isContinued && Phase < 0.5f;

            if (isContinued)
            {
                _intervals.Add(interval);
                while (_intervals.Count > Mathf.Max(1, TapHistoryCount)) _intervals.RemoveAt(0);

                Bpm = CalculateBpm();
                if (!isCountedByClock) BeatCount++;
            }
            else
            {
                // 間隔が空きすぎている場合は新しいタップ列として取り直す
                _intervals.Clear();
                TapCount = 0;
                BeatCount = 0;
            }

            // 自動進行で数えた拍と重複する場合は拍の通知もしない (1拍で2回発火させない)
            bool shouldFireBeat = !isCountedByClock;

            if (isBarStart)
            {
                // 直前に自動進行で数えた拍が小節頭でなかった場合は、改めて小節頭として通知する
                if (BeatInBar != 0) shouldFireBeat = true;

                BeatCount = 0;
            }

            TapCount++;
            _lastTapTime = time;

            Phase = 0f;
            IsRunning = true;

            if (shouldFireBeat) FireBeat();

            return isContinued;
        }

        public void Stop()
        {
            IsRunning = false;
            Phase = 0f;
            BeatCount = 0;
            TapCount = 0;
            _intervals.Clear();
            _lastTapTime = float.NegativeInfinity;
        }

        public void SetBpm(float bpm)
        {
            float clamped = Mathf.Clamp(bpm, MinBpm, MaxBpm);

            // UIから毎フレーム同じ値が書き込まれてもタップ列を壊さないようにする
            if (Mathf.Approximately(Bpm, clamped)) return;

            Bpm = clamped;

            // 手動でBPMを変えた場合はタップ列を破棄する (次のタップから測り直す)
            _intervals.Clear();
            _lastTapTime = float.NegativeInfinity;
            IsRunning = true;
        }

        public void Update(float deltaTime)
        {
            if (!IsRunning || Bpm <= 0f) return;

            Phase += deltaTime * Bpm / 60f;

            // 1フレームで複数拍進む場合 (低フレームレート/高BPM) も取りこぼさない
            while (Phase >= 1f)
            {
                Phase -= 1f;
                BeatCount++;
                FireBeat();
            }
        }

        private void FireBeat()
        {
            _lastBeatFrame = Time.frameCount;
            OnBeat?.Invoke(BeatCount);
        }

        /// <summary>
        /// タップ間隔の平均からBPMを求める
        /// 極端に外れた間隔 (タップミス) は中央値から離れているものとして除外する
        /// </summary>
        private float CalculateBpm()
        {
            if (_intervals.Count == 0) return Bpm;

            float median = GetMedian();
            float sum = 0f;
            int count = 0;

            foreach (float interval in _intervals)
            {
                if (interval < median * 0.5f || interval > median * 2f) continue;

                sum += interval;
                count++;
            }

            if (count == 0) return Bpm;

            return Mathf.Clamp(60f / (sum / count), MinBpm, MaxBpm);
        }

        private float GetMedian()
        {
            var sorted = new List<float>(_intervals);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }
    }
}
