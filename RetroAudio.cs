using System.Runtime.InteropServices;

namespace GamingStackGUI
{
    /// <summary>
    /// Retro sound effects: the terminal typewriter (a mechanical key "clack" for every few letters
    /// typed, a heavier thunk for spaces, and a bell when a whole screen has finished
    /// typing out - the sort of sound you hear at a Resident Evil save-room typewriter.
    ///
    /// Why not just use SoundPlayer? It plays one sound at a time through a single
    /// global channel, so fast typing cut each clack off and sounded like a buzz.
    /// This class instead runs a tiny software mixer on its own thread: every clack is
    /// a separate "voice" mixed with the others and streamed to the sound card through
    /// winmm (built into Windows - no extra packages or files). All sounds are
    /// synthesised in code, so there are no audio files to ship or lose.
    /// </summary>
    internal sealed class RetroAudio : IDisposable
    {
        private const int SampleRate = 44100;
        private const int ChunkSamples = 512;     // ~11.6 ms per buffer
        private const int BufferCount = 4;        // ~46 ms of audio queued in total

        private readonly float[][] _clacks;
        private readonly float[][] _spaces;
        private readonly float[] _bell;
        private readonly Random _rng = new();

        private sealed class Voice
        {
            public float[] Data = Array.Empty<float>();
            public int Pos;
            public int Delay;      // samples to wait before it starts (spreads a burst out)
            public float Gain;
        }

        private readonly List<Voice> _voices = new();
        private readonly object _lock = new();
        private Thread? _thread;
        private volatile bool _stop;
        private bool _failed;

        public RetroAudio()
        {
            _clacks = Enumerable.Range(0, 7).Select(i => SynthKey(100 + i, heavy: false)).ToArray();
            _spaces = Enumerable.Range(0, 2).Select(i => SynthKey(200 + i, heavy: true)).ToArray();
            _bell = SynthBell();
        }

        // ---------------------------------------------------------------- public API

        /// <summary>One key strike. <paramref name="delayMs"/> staggers several strikes
        /// that belong to the same animation frame so they don't land on top of each other.</summary>
        public void Key(int delayMs = 0, bool space = false)
        {
            var bank = space ? _spaces : _clacks;
            Enqueue(bank[_rng.Next(bank.Length)], delayMs, 0.75f + (float)_rng.NextDouble() * 0.25f);
        }

        /// <summary>The "ding" at the end of a line / screen.</summary>
        public void Bell() => Enqueue(_bell, 0, 0.55f);

        private void Enqueue(float[] data, int delayMs, float gain)
        {
            if (_failed) return;
            EnsureStarted();
            lock (_lock)
            {
                // Never let a flood of sounds pile up without limit.
                if (_voices.Count > 48) return;
                _voices.Add(new Voice { Data = data, Gain = gain, Delay = delayMs * SampleRate / 1000 });
            }
        }

        public void Dispose()
        {
            _stop = true;
            _thread?.Join(500);
        }

        // ---------------------------------------------------------------- mixer thread

        private void EnsureStarted()
        {
            if (_thread != null) return;
            _thread = new Thread(MixLoop) { IsBackground = true, Name = "RetroAudio" };
            _thread.Start();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveFormatEx
        {
            public ushort FormatTag, Channels;
            public uint SamplesPerSec, AvgBytesPerSec;
            public ushort BlockAlign, BitsPerSample, Size;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveHdr
        {
            public IntPtr Data;
            public uint BufferLength, BytesRecorded;
            public IntPtr User;
            public uint Flags, Loops;
            public IntPtr Next, Reserved;
        }

        private const uint WaveMapper = 0xFFFFFFFF;
        private const uint WhdrDone = 0x1;

        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr hwo, uint device, ref WaveFormatEx fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr hwo);

        private void MixLoop()
        {
            IntPtr dev = IntPtr.Zero;
            var hdrs = new IntPtr[BufferCount];
            var bufs = new IntPtr[BufferCount];
            var queued = new bool[BufferCount];
            var hdrSize = Marshal.SizeOf<WaveHdr>();
            var flagsOffset = (int)Marshal.OffsetOf<WaveHdr>(nameof(WaveHdr.Flags));
            var mix = new float[ChunkSamples];
            var pcm = new short[ChunkSamples];

            try
            {
                var fmt = new WaveFormatEx
                {
                    FormatTag = 1, Channels = 1, SamplesPerSec = SampleRate,
                    AvgBytesPerSec = SampleRate * 2, BlockAlign = 2, BitsPerSample = 16, Size = 0
                };
                if (waveOutOpen(out dev, WaveMapper, ref fmt, IntPtr.Zero, IntPtr.Zero, 0) != 0)
                {
                    _failed = true; // no sound device - the app just stays quiet
                    return;
                }

                for (int i = 0; i < BufferCount; i++)
                {
                    bufs[i] = Marshal.AllocHGlobal(ChunkSamples * 2);
                    hdrs[i] = Marshal.AllocHGlobal(hdrSize);
                    var h = new WaveHdr { Data = bufs[i], BufferLength = (uint)(ChunkSamples * 2) };
                    Marshal.StructureToPtr(h, hdrs[i], false);
                    waveOutPrepareHeader(dev, hdrs[i], hdrSize);
                }

                var cur = 0;
                while (!_stop)
                {
                    if (queued[cur])
                    {
                        // Wait for the sound card to finish this buffer before refilling it.
                        while (!_stop && (Marshal.ReadInt32(hdrs[cur], flagsOffset) & WhdrDone) == 0)
                            Thread.Sleep(2);
                        if (_stop) break;
                    }

                    Array.Clear(mix, 0, mix.Length);
                    lock (_lock)
                    {
                        for (int v = _voices.Count - 1; v >= 0; v--)
                        {
                            var voice = _voices[v];
                            var start = 0;
                            if (voice.Delay > 0)
                            {
                                if (voice.Delay >= ChunkSamples) { voice.Delay -= ChunkSamples; continue; }
                                start = voice.Delay;
                                voice.Delay = 0;
                            }
                            var n = Math.Min(ChunkSamples - start, voice.Data.Length - voice.Pos);
                            for (int s = 0; s < n; s++)
                                mix[start + s] += voice.Data[voice.Pos + s] * voice.Gain;
                            voice.Pos += n;
                            if (voice.Pos >= voice.Data.Length) _voices.RemoveAt(v);
                        }
                    }

                    for (int s = 0; s < ChunkSamples; s++)
                    {
                        // Soft clip so a dense burst of clacks saturates gently, not harshly.
                        var x = mix[s];
                        x = x / (1 + Math.Abs(x) * 0.6f);
                        pcm[s] = (short)(Math.Clamp(x, -1f, 1f) * 32000);
                    }
                    Marshal.Copy(pcm, 0, bufs[cur], ChunkSamples);
                    waveOutWrite(dev, hdrs[cur], hdrSize);
                    queued[cur] = true;
                    cur = (cur + 1) % BufferCount;
                }
            }
            catch
            {
                _failed = true;
            }
            finally
            {
                if (dev != IntPtr.Zero)
                {
                    waveOutReset(dev);
                    for (int i = 0; i < BufferCount; i++)
                    {
                        if (hdrs[i] != IntPtr.Zero)
                        {
                            waveOutUnprepareHeader(dev, hdrs[i], hdrSize);
                            Marshal.FreeHGlobal(hdrs[i]);
                        }
                        if (bufs[i] != IntPtr.Zero) Marshal.FreeHGlobal(bufs[i]);
                    }
                    waveOutClose(dev);
                }
            }
        }

        // ---------------------------------------------------------------- synthesis

        // One mechanical key strike, built from the same four things a real typewriter
        // does in the first ~60 ms:
        //   1. a sharp broadband "snap" as the type-bar hits the paper,
        //   2. a bright metallic ring (a few inharmonic partials, fast decay),
        //   3. a low wooden/steel body thump that carries the pitch of the "clack",
        //   4. a soft second tick ~14 ms later as the key bottoms out and springs back.
        // Each variant differs slightly, so a run of keys never sounds like one looped
        // sample. "heavy" is the space bar: wider, lower, less metallic.
        private static float[] SynthKey(int seed, bool heavy)
        {
            var rng = new Random(seed);
            var len = (int)(SampleRate * (heavy ? 0.14 : 0.11));
            var d = new float[len];

            var body1 = (heavy ? 82 : 150) + rng.NextDouble() * (heavy ? 18 : 40);
            var body2 = body1 * (1.9 + rng.NextDouble() * 0.3);
            var ring = new[] { 2350 + rng.NextDouble() * 300, 3480 + rng.NextDouble() * 400, 5210 + rng.NextDouble() * 500 };
            var lowpass = 0.0;
            var bounceAt = (int)(SampleRate * (0.012 + rng.NextDouble() * 0.006));

            for (int i = 0; i < len; i++)
            {
                var t = i / (double)SampleRate;

                // snap: noise, kept bright by subtracting a smoothed copy (a crude high-pass)
                var noise = rng.NextDouble() * 2 - 1;
                lowpass += (noise - lowpass) * 0.25;
                var snap = (noise - lowpass) * Math.Exp(-t / (heavy ? 0.0030 : 0.0014));

                // paper/platen "thwack": mid-band noise
                var mid = lowpass * Math.Exp(-t / (heavy ? 0.012 : 0.007));

                // metallic ring
                var metal = 0.0;
                for (int p = 0; p < ring.Length; p++)
                    metal += Math.Sin(2 * Math.PI * ring[p] * t) * Math.Exp(-t / (0.004 - p * 0.0007)) / (p + 1.2);

                // body thump, pitch dropping slightly as it dies away
                var drop = 1 - 0.12 * Math.Min(1, t / 0.03);
                var body = (Math.Sin(2 * Math.PI * body1 * drop * t) * 0.75 +
                            Math.Sin(2 * Math.PI * body2 * drop * t) * 0.25) * Math.Exp(-t / (heavy ? 0.034 : 0.020));

                var sample = snap * 0.55 + mid * 0.5 + metal * (heavy ? 0.12 : 0.34) + body * (heavy ? 1.05 : 0.8);

                // the little bounce/spring tick
                if (i >= bounceAt)
                {
                    var tb = (i - bounceAt) / (double)SampleRate;
                    sample += (rng.NextDouble() * 2 - 1) * Math.Exp(-tb / 0.0012) * 0.22
                            + Math.Sin(2 * Math.PI * ring[0] * 0.8 * tb) * Math.Exp(-tb / 0.003) * 0.10;
                }
                d[i] = (float)sample;
            }
            return Normalise(d, 0.85f);
        }

        // Carriage-return bell: a bright two-partial "ding" with a long ring-out,
        // preceded by a short scrape of the carriage sliding back.
        private static float[] SynthBell()
        {
            var rng = new Random(900);
            var slideLen = (int)(SampleRate * 0.09);
            var dingLen = (int)(SampleRate * 0.9);
            var d = new float[slideLen + dingLen];
            double lp = 0;
            for (int i = 0; i < slideLen; i++)
            {
                var t = i / (double)slideLen;
                lp += ((rng.NextDouble() * 2 - 1) - lp) * (0.08 + 0.3 * t); // brightening scrape
                d[i] = (float)(lp * Math.Sin(Math.PI * t) * 0.5);
            }
            for (int i = 0; i < dingLen; i++)
            {
                var t = i / (double)SampleRate;
                var env = Math.Exp(-t / 0.28) * Math.Min(1, t / 0.002);
                var s = Math.Sin(2 * Math.PI * 2093 * t) + 0.55 * Math.Sin(2 * Math.PI * 3140 * t) + 0.25 * Math.Sin(2 * Math.PI * 5270 * t);
                d[slideLen + i] = (float)(s * env * 0.5);
            }
            return Normalise(d, 0.8f);
        }

        // ---------------------------------------------------------------- BIOS sounds

        // The classic PC-speaker POST "all OK" beep: a plain square wave, with a hint of
        // low-pass so it is harsh-but-not-painful on modern speakers. These are made with
        // the same mixer as the typewriter, so they play on any sound card - the old
        // Console.Beep route needs a motherboard speaker, which most PCs no longer have.
        private static float[] SynthSquareBeep(double freq, double seconds, float gain)
        {
            var len = (int)(SampleRate * seconds);
            var d = new float[len];
            double lp = 0;
            for (int i = 0; i < len; i++)
            {
                var t = i / (double)SampleRate;
                var sq = Math.Sin(2 * Math.PI * freq * t) >= 0 ? 1.0 : -1.0;
                lp += (sq - lp) * 0.35;
                var env = Math.Min(1.0, t / 0.002) * Math.Min(1.0, (seconds - t) / 0.004);
                d[i] = (float)(lp * env * gain);
            }
            return d;
        }

        // A hard-drive spinning up: a low whine that climbs in pitch, with a little
        // motor rumble underneath, fading in and out.
        private static float[] SynthSpinUp()
        {
            var rng = new Random(77);
            var len = (int)(SampleRate * 1.6);
            var d = new float[len];
            double phase = 0, rumble = 0;
            for (int i = 0; i < len; i++)
            {
                var t = i / (double)SampleRate;
                var f = 48 + 170 * (1 - Math.Exp(-t / 0.55));            // 48 Hz up toward ~218 Hz
                phase += 2 * Math.PI * f / SampleRate;
                rumble += ((rng.NextDouble() * 2 - 1) - rumble) * 0.02;
                var env = Math.Min(1.0, t / 0.25) * Math.Min(1.0, (1.6 - t) / 0.5);
                d[i] = (float)((Math.Sin(phase) * 0.5 + Math.Sin(phase * 2) * 0.22 + Math.Sin(phase * 3.01) * 0.1 + rumble * 2.2) * env * 0.5);
            }
            return d;
        }

        // A drive head seeking: a couple of tiny dry clicks.
        private static float[] SynthSeek(int seed)
        {
            var rng = new Random(seed);
            var len = (int)(SampleRate * 0.05);
            var d = new float[len];
            var second = (int)(SampleRate * (0.012 + rng.NextDouble() * 0.012));
            for (int i = 0; i < len; i++)
            {
                var t = i / (double)SampleRate;
                var v = (rng.NextDouble() * 2 - 1) * Math.Exp(-t / 0.0012) * 0.8
                      + Math.Sin(2 * Math.PI * 520 * t) * Math.Exp(-t / 0.006) * 0.35;
                if (i >= second)
                {
                    var tb = (i - second) / (double)SampleRate;
                    v += (rng.NextDouble() * 2 - 1) * Math.Exp(-tb / 0.001) * 0.5;
                }
                d[i] = (float)v;
            }
            return d;
        }

        private readonly float[] _postBeep = SynthSquareBeep(1000, 0.20, 0.5f);
        private readonly float[] _memTick = SynthSquareBeep(1800, 0.018, 0.35f);
        private readonly float[] _doneBeep = SynthSquareBeep(800, 0.12, 0.5f);
        private readonly float[] _shortBeep = SynthSquareBeep(900, 0.07, 0.5f);
        private readonly float[] _spinUp = SynthSpinUp();
        private readonly float[][] _seeks = Enumerable.Range(0, 4).Select(i => SynthSeek(300 + i)).ToArray();

        /// <summary>The single "all OK" POST beep.</summary>
        public void PostBeep() => Enqueue(_postBeep, 0, 0.8f);
        /// <summary>A tiny tick for each step of the memory count.</summary>
        public void MemTick() => Enqueue(_memTick, 0, 0.6f);
        /// <summary>The longer beep when the memory test finishes.</summary>
        public void DoneBeep() => Enqueue(_doneBeep, 0, 0.8f);
        /// <summary>A short beep when the BIOS finishes listing devices.</summary>
        public void ShortBeep() => Enqueue(_shortBeep, 0, 0.8f);
        /// <summary>Hard-drive spin-up whine.</summary>
        public void SpinUp() => Enqueue(_spinUp, 0, 0.45f);
        /// <summary>A random drive-seek click.</summary>
        public void Seek() => Enqueue(_seeks[_rng.Next(_seeks.Length)], 0, 0.6f);

        private static float[] Normalise(float[] d, float peakTarget)
        {
            var peak = d.Select(Math.Abs).DefaultIfEmpty(0).Max();
            if (peak <= 0) return d;
            var k = peakTarget / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
            return d;
        }
    }
}
