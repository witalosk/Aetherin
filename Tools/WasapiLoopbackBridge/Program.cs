using System;
using System.IO;
using System.Text;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Aetherin.WasapiLoopbackBridge
{
    internal static class Program
    {
        private const int Magic = 0x324C4541; // "AEL2"
        private const byte AudioPacket = 1;
        private const byte OnsetPacket = 2;
        private static readonly object OutputLock = new object();
        private static readonly ManualResetEvent StopEvent = new ManualResetEvent(false);
        private static BinaryWriter _output;
        private static volatile bool _ready;
        private static HardRealtimeOnsetDetector _detector;
        private static readonly PercussiveOnset[] OnsetBuffer = new PercussiveOnset[8];

        private static int Main(string[] args)
        {
            try
            {
                bool probe = args.Length > 0 && args[0] == "--probe";
                int deviceArgument = probe ? 1 : 0;
                string deviceId = args.Length > deviceArgument
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(args[deviceArgument]))
                    : string.Empty;

                using (var enumerator = new MMDeviceEnumerator())
                using (MMDevice device = string.IsNullOrEmpty(deviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : enumerator.GetDevice(deviceId))
                using (var capture = new LowLatencyLoopbackCapture(device))
                {
                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;
                    capture.StartRecording();

                    WaveFormat format = capture.WaveFormat;
                    if (probe)
                    {
                        Console.Error.WriteLine("OK: " + device.FriendlyName + " / " + format);
                        Thread.Sleep(500);
                        capture.StopRecording();
                        return 0;
                    }

                    _output = new BinaryWriter(Console.OpenStandardOutput());
                    _detector = new HardRealtimeOnsetDetector(format);
                    lock (OutputLock)
                    {
                        _output.Write(Magic);
                        _output.Write(format.SampleRate);
                        _output.Write(format.Channels);
                        _output.Write(format.BitsPerSample);
                        _output.Write((int)format.Encoding);
                        _output.Flush();
                        _ready = true;
                    }

                    StopEvent.WaitOne();
                    capture.StopRecording();
                }
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }

        private static void OnDataAvailable(object sender, WaveInEventArgs args)
        {
            if (!_ready || args.BytesRecorded <= 0) return;
            try
            {
                long firstSampleIndex = _detector.TotalSamples;
                int onsetCount = _detector.Process(args.Buffer, args.BytesRecorded, OnsetBuffer);
                lock (OutputLock)
                {
                    _output.Write(AudioPacket);
                    _output.Write(firstSampleIndex);
                    _output.Write(args.BytesRecorded);
                    _output.Write(args.Buffer, 0, args.BytesRecorded);
                    for (int i = 0; i < onsetCount; i++)
                    {
                        _output.Write(OnsetPacket);
                        _output.Write(OnsetBuffer[i].SampleIndex);
                        _output.Write(OnsetBuffer[i].Kick);
                        _output.Write(OnsetBuffer[i].SnareClap);
                    }
                    _output.Flush();
                }
            }
            catch (IOException)
            {
                StopEvent.Set();
            }
        }

        private static void OnRecordingStopped(object sender, StoppedEventArgs args)
        {
            if (args.Exception != null) Console.Error.WriteLine(args.Exception);
            StopEvent.Set();
        }

        private sealed class LowLatencyLoopbackCapture : WasapiCapture
        {
            public LowLatencyLoopbackCapture(MMDevice device) : base(device, false, 10) { }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
            {
                return AudioClientStreamFlags.Loopback |
                       AudioClientStreamFlags.AutoConvertPcm |
                       AudioClientStreamFlags.SrcDefaultQuality;
            }
        }
    }
}
