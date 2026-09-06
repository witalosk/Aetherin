using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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
        private static double _lastFlushTime;
        private static double _lastCriticalFlushTime;

        private static string LogPath => Path.Combine(Application.persistentDataPath, "AetherinMidiDiagnostics.log");

        public static void Record(string message)
        {
            string entry = $"{DateTime.UtcNow:O}  {message}";
            while (Entries.Count >= MaxEntries) Entries.Dequeue();
            Entries.Enqueue(entry);
        }

        public static void RecordCritical(string message)
        {
            Record(message);
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastCriticalFlushTime < CriticalFlushIntervalSeconds) return;
            _lastCriticalFlushTime = now;
            Flush();
        }

        public static void FlushIfDue()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - _lastFlushTime < FlushIntervalSeconds) return;
            Flush();
        }

        private static void Flush()
        {
            _lastFlushTime = Time.realtimeSinceStartupAsDouble;
            try
            {
                File.WriteAllLines(LogPath, Entries);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[MidiDiagnostics] Failed to write diagnostic log: {exception.Message}");
            }
        }
    }
}
