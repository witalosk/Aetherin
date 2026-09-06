using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Aetherin
{
    /// <summary>
    /// MIDI/RtMidiのハング調査用。ネイティブ呼び出し直前の状態を残し、
    /// 強制終了後の次回起動時にも最後の処理を確認できるようにする。
    /// </summary>
    internal static class MidiDiagnostics
    {
        private const int MaxEntries = 64;
        private const double FlushIntervalSeconds = 5d;
        private const double CriticalFlushIntervalSeconds = 0.5d;

        private static readonly Queue<string> Entries = new();
        private static readonly object Sync = new();
        private static double _lastFlushTime;
        private static double _lastCriticalFlushTime;
        private static string _logPath;

        public static void Initialize(string persistentDataPath)
        {
            _logPath ??= Path.Combine(persistentDataPath, "AetherinMidiDiagnostics.log");
        }

        public static void Record(string message)
        {
            lock (Sync)
            {
                string entry = $"{DateTime.UtcNow:O}  {message}";
                while (Entries.Count >= MaxEntries) Entries.Dequeue();
                Entries.Enqueue(entry);
            }
        }

        public static void RecordCritical(string message)
        {
            Record(message);
            double now = GetMonotonicSeconds();
            if (now - _lastCriticalFlushTime < CriticalFlushIntervalSeconds) return;
            _lastCriticalFlushTime = now;
            Flush();
        }

        public static void FlushIfDue()
        {
            double now = GetMonotonicSeconds();
            if (now - _lastFlushTime < FlushIntervalSeconds) return;
            Flush();
        }

        private static void Flush()
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            _lastFlushTime = GetMonotonicSeconds();
            try
            {
                string[] snapshot;
                lock (Sync) snapshot = Entries.ToArray();
                File.WriteAllLines(_logPath, snapshot);
            }
            catch
            {
                // 診断ログの失敗がMIDI処理を妨げないよう、ここではUnity APIへ報告しない。
            }
        }

        private static double GetMonotonicSeconds() =>
            (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
}
