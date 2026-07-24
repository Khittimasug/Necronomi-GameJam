using System;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Frame by frame view of a song: how much new energy appeared (flux), where in
    /// the spectrum it appeared (per lane band), and how loud the frame was.
    /// </summary>
    public sealed class SpectralAnalysis
    {
        public int sampleRate;
        public int fftSize;
        public int hopSize;
        public int frameCount;

        /// <summary>Onset strength per frame, normalised so the loudest onset is 1.</summary>
        public float[] flux;

        /// <summary>Per lane band onset strength. [band][frame].</summary>
        public float[][] bandFlux;

        /// <summary>RMS level per frame, used for hook detection and hold sustain.</summary>
        public float[] rms;

        /// <summary>Coarse log spaced band energy per frame, used for section similarity. [band][frame].</summary>
        public float[][] timbre;

        public const int TimbreBands = 12;

        public double FrameDuration => (double)hopSize / sampleRate;

        public double FrameToTime(int frame) => frame * (double)hopSize / sampleRate;

        public int TimeToFrame(double time) => (int)Math.Round(time * sampleRate / hopSize);
    }

    /// <summary>Runs the short time Fourier transform and derives every curve the baker needs.</summary>
    public static class SpectralAnalyzer
    {
        public static SpectralAnalysis Analyze(float[] mono, int sampleRate, ChartBakeSettings settings,
            Action<float, string> progress = null)
        {
            int fftSize = NextPowerOfTwo(settings.fftSize);
            int hopSize = Math.Max(64, Math.Min(settings.hopSize, fftSize));
            int bins = fftSize / 2;

            int frameCount = mono.Length >= fftSize ? (mono.Length - fftSize) / hopSize + 1 : 0;
            if (frameCount <= 2)
                return null;

            int laneCount = Math.Max(1, settings.laneCount);

            SpectralAnalysis result = new SpectralAnalysis
            {
                sampleRate = sampleRate,
                fftSize = fftSize,
                hopSize = hopSize,
                frameCount = frameCount,
                flux = new float[frameCount],
                rms = new float[frameCount],
                bandFlux = new float[laneCount][],
                timbre = new float[SpectralAnalysis.TimbreBands][]
            };

            for (int b = 0; b < laneCount; b++)
                result.bandFlux[b] = new float[frameCount];

            for (int b = 0; b < SpectralAnalysis.TimbreBands; b++)
                result.timbre[b] = new float[frameCount];

            float binHz = sampleRate / (float)fftSize;
            int fluxLowBin = Math.Clamp((int)(settings.fluxMinHz / binHz), 1, bins - 1);
            int fluxHighBin = Math.Clamp((int)(settings.fluxMaxHz / binHz), fluxLowBin + 1, bins);

            int[] laneStart = new int[laneCount];
            int[] laneEnd = new int[laneCount];
            BuildLaneBins(settings, laneCount, binHz, bins, laneStart, laneEnd);

            int[] timbreStart = new int[SpectralAnalysis.TimbreBands];
            int[] timbreEnd = new int[SpectralAnalysis.TimbreBands];
            BuildLogBins(60f, 10000f, binHz, bins, timbreStart, timbreEnd);

            FFT fft = new FFT(fftSize);
            float[] window = FFT.HannWindow(fftSize);
            float[] re = new float[fftSize];
            float[] im = new float[fftSize];
            float[] magnitude = new float[bins];
            float[] previous = new float[bins];

            bool compress = settings.logCompression;
            float lambda = Math.Max(1f, settings.compressionLambda);
            int progressStep = Math.Max(1, frameCount / 50);

            for (int frame = 0; frame < frameCount; frame++)
            {
                int offset = frame * hopSize;

                float energy = 0f;
                for (int i = 0; i < fftSize; i++)
                {
                    float sample = mono[offset + i];
                    energy += sample * sample;
                    re[i] = sample * window[i];
                    im[i] = 0f;
                }

                result.rms[frame] = (float)Math.Sqrt(energy / fftSize);

                fft.Forward(re, im);

                for (int k = 0; k < bins; k++)
                {
                    float mag = (float)Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    magnitude[k] = compress ? (float)Math.Log(1d + lambda * mag) : mag;
                }

                if (frame > 0)
                {
                    float total = 0f;
                    for (int k = fluxLowBin; k < fluxHighBin; k++)
                    {
                        float diff = magnitude[k] - previous[k];
                        if (diff > 0f)
                            total += diff;
                    }

                    result.flux[frame] = total;

                    for (int b = 0; b < laneCount; b++)
                    {
                        float bandTotal = 0f;
                        for (int k = laneStart[b]; k < laneEnd[b]; k++)
                        {
                            float diff = magnitude[k] - previous[k];
                            if (diff > 0f)
                                bandTotal += diff;
                        }

                        result.bandFlux[b][frame] = bandTotal;
                    }
                }

                for (int b = 0; b < SpectralAnalysis.TimbreBands; b++)
                {
                    float sum = 0f;
                    int count = timbreEnd[b] - timbreStart[b];
                    for (int k = timbreStart[b]; k < timbreEnd[b]; k++)
                        sum += magnitude[k];

                    result.timbre[b][frame] = count > 0 ? sum / count : 0f;
                }

                Array.Copy(magnitude, previous, bins);

                if (progress != null && frame % progressStep == 0)
                    progress(frame / (float)frameCount, $"Analysing spectrum {frame}/{frameCount}");
            }

            Normalize(result.flux);
            for (int b = 0; b < laneCount; b++)
                Normalize(result.bandFlux[b]);

            return result;
        }

        private static void BuildLaneBins(ChartBakeSettings settings, int laneCount, float binHz, int bins,
            int[] start, int[] end)
        {
            float[] edges = settings.laneBandEdgesHz;

            for (int b = 0; b < laneCount; b++)
            {
                float lowHz = b == 0 ? 20f : EdgeAt(edges, b - 1, b, laneCount);
                float highHz = b == laneCount - 1 ? binHz * bins : EdgeAt(edges, b, b + 1, laneCount);

                start[b] = Math.Clamp((int)(lowHz / binHz), 1, bins - 1);
                end[b] = Math.Clamp((int)(highHz / binHz), start[b] + 1, bins);
            }
        }

        private static float EdgeAt(float[] edges, int index, int fallbackIndex, int laneCount)
        {
            if (edges != null && index >= 0 && index < edges.Length)
                return edges[index];

            // Fallback: log spread between 120 Hz and 8 kHz so a mismatched edge list still bakes.
            float t = laneCount <= 1 ? 0.5f : fallbackIndex / (float)laneCount;
            return 120f * (float)Math.Pow(8000d / 120d, t);
        }

        private static void BuildLogBins(float lowHz, float highHz, float binHz, int bins, int[] start, int[] end)
        {
            int count = start.Length;
            for (int b = 0; b < count; b++)
            {
                float t0 = b / (float)count;
                float t1 = (b + 1) / (float)count;
                float f0 = lowHz * (float)Math.Pow(highHz / lowHz, t0);
                float f1 = lowHz * (float)Math.Pow(highHz / lowHz, t1);

                start[b] = Math.Clamp((int)(f0 / binHz), 1, bins - 1);
                end[b] = Math.Clamp((int)(f1 / binHz), start[b] + 1, bins);
            }
        }

        private static void Normalize(float[] values)
        {
            float max = 0f;
            for (int i = 0; i < values.Length; i++)
                max = Math.Max(max, values[i]);

            if (max <= 0.0001f)
                return;

            float scale = 1f / max;
            for (int i = 0; i < values.Length; i++)
                values[i] *= scale;
        }

        private static int NextPowerOfTwo(int value)
        {
            int power = 256;
            while (power < value)
                power <<= 1;

            return power;
        }
    }
}
