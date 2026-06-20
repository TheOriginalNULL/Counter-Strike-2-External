using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using NAudio.Vorbis;
using NAudio.Wave;
using CounterStrike2.Memory;

namespace CounterStrike2.Entities
{
    internal static class KillTracker
    {
        private static int  _last;
        private static bool _ready;

        // Diagnostics — exposed for the Misc tab debug label. TotalDetectedKills is persistent
        // (never resets to 0 on its own) since LastDelta only stays nonzero for the single
        // ~8ms Update() call where the increase actually happened — a 400ms UI refresh would
        // almost always see it already back at 0 even when detection worked correctly.
        public static bool LastControllerFound  { get; private set; }
        public static int  LastRawKillCount     { get; private set; }
        public static int  LastDelta            { get; private set; }
        public static int  TotalDetectedKills   { get; private set; }
        public static int  TotalPlaySoundCalls  { get; private set; }

        internal static int Update(ProcessMemory mem)
        {
            if (Offsets.LocalPlayerController == IntPtr.Zero) { LastControllerFound = false; return 0; }
            IntPtr ctrl = mem.Read<IntPtr>(Offsets.LocalPlayerController);
            if (ctrl == IntPtr.Zero) { LastControllerFound = false; return 0; }

            IntPtr svc = mem.Read<IntPtr>(IntPtr.Add(ctrl, EntitySchema.ActionTrackingServices));
            if (svc == IntPtr.Zero) { LastControllerFound = false; return 0; }
            LastControllerFound = true;

            int cur = mem.Read<int>(IntPtr.Add(svc, EntitySchema.NumRoundKills));
            LastRawKillCount = cur;

            if (!_ready) { _last = cur; _ready = true; LastDelta = 0; return 0; }

            int delta = cur > _last ? cur - _last : 0;
            _last = cur;
            LastDelta = delta;
            if (delta > 0) TotalDetectedKills += delta;
            return delta;
        }

        internal static void Reset() => _ready = false;

        // All sound triggers (kill/weapon-equip/hit) funnel through one queue, drained by a
        // single PERMANENT background thread — not thread-pool Task.Run calls. Windows audio
        // playback APIs commonly have thread-affinity expectations for their callback delivery;
        // reusing one WaveOutEvent across calls that could each land on a different pooled
        // thread is itself a likely cause of silent failures. Pinning all device interaction to
        // one dedicated thread for the process's whole lifetime removes that variable entirely.
        private static readonly BlockingCollection<string> _queue = new();

        static KillTracker()
        {
            var thread = new Thread(WorkerLoop) { IsBackground = true, Name = "SoundPlayback" };
            thread.Start();
        }

        internal static void PlaySound(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            TotalPlaySoundCalls++;
            _queue.Add(path);
        }

        private static void WorkerLoop()
        {
            WaveOutEvent? output = null;
            foreach (var path in _queue.GetConsumingEnumerable())
            {
                try
                {
                    using WaveStream reader = Path.GetExtension(path).ToLowerInvariant() == ".ogg"
                        ? new VorbisWaveReader(path)
                        : new AudioFileReader(path); // handles WAV, MP3, FLAC, AIFF, WMA, AAC

                    output ??= new WaveOutEvent();
                    output.Init(reader);
                    output.Play();
                    while (output.PlaybackState == PlaybackState.Playing)
                        Thread.Sleep(20);
                }
                catch
                {
                    // If the device ended up in a bad state, drop it so the next attempt opens
                    // a fresh one (still on this same thread) instead of repeatedly failing.
                    output?.Dispose();
                    output = null;
                }
            }
        }
    }
}
