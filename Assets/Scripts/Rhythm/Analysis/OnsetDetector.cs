using System;
using System.Collections.Generic;

namespace Necronomi.Rhythm
{
    /// <summary>One detected attack in the audio.</summary>
    public struct Onset
    {
        public int frame;
        public double time;
        public float strength;
        public int lane;
    }

    /// <summary>
    /// Picks peaks out of the onset envelope using a rolling median threshold, so a
    /// quiet verse and a loud chorus both produce notes instead of one drowning the other.
    /// </summary>
    public static class OnsetDetector
    {
        public static List<Onset> Detect(SpectralAnalysis analysis, ChartBakeSettings settings)
        {
            List<Onset> onsets = new List<Onset>(1024);

            float[] flux = analysis.flux;
            int frameCount = analysis.frameCount;
            if (frameCount < 3)
                return onsets;

            int half = Math.Clamp(settings.thresholdWindow, 3, 80);
            int minGapFrames = Math.Max(1, (int)Math.Round(settings.minNoteSpacing / analysis.FrameDuration));

            float[] scratch = new float[half * 2 + 1];
            float[] bandMean = BuildBandMeans(analysis);

            // Far enough back that the first candidate always passes the gap test,
            // without underflowing the subtraction below.
            int lastAccepted = -minGapFrames - 1;

            for (int frame = 1; frame < frameCount - 1; frame++)
            {
                float value = flux[frame];
                if (value <= 0f)
                    continue;

                // Local maximum test first: it is cheap and rejects most frames.
                if (value < flux[frame - 1] || value < flux[frame + 1])
                    continue;

                float median = RollingMedian(flux, frame, half, scratch);
                float threshold = median * settings.thresholdMultiplier + settings.thresholdFloor;
                if (value < threshold)
                    continue;

                if (frame - lastAccepted < minGapFrames)
                {
                    // Too close to the previous note: keep whichever hit harder.
                    if (onsets.Count > 0 && value > onsets[^1].strength)
                    {
                        Onset replaced = Build(analysis, frame, value, bandMean);
                        onsets[^1] = replaced;
                        lastAccepted = frame;
                    }

                    continue;
                }

                onsets.Add(Build(analysis, frame, value, bandMean));
                lastAccepted = frame;
            }

            return onsets;
        }

        private static Onset Build(SpectralAnalysis analysis, int frame, float strength, float[] bandMean)
        {
            return new Onset
            {
                frame = frame,
                time = analysis.FrameToTime(frame),
                strength = Math.Clamp(strength, 0f, 1f),
                lane = DominantLane(analysis, frame, bandMean)
            };
        }

        /// <summary>
        /// Average flux per band across the whole song. Used to judge each band against
        /// its own baseline instead of against the others.
        /// </summary>
        private static float[] BuildBandMeans(SpectralAnalysis analysis)
        {
            int bands = analysis.bandFlux.Length;
            float[] means = new float[bands];

            for (int b = 0; b < bands; b++)
            {
                float[] band = analysis.bandFlux[b];
                double sum = 0d;

                for (int f = 0; f < analysis.frameCount; f++)
                    sum += band[f];

                means[b] = (float)(sum / Math.Max(1, analysis.frameCount));
            }

            return means;
        }

        /// <summary>
        /// Lane comes from which frequency band spiked hardest around the onset, so the
        /// kick pattern lands on one lane and the hats on another.
        /// Scores are relative to each band's own average: the top band spans hundreds of
        /// FFT bins and the bass band spans a handful, so raw sums would send almost every
        /// note to the widest band.
        /// </summary>
        private static int DominantLane(SpectralAnalysis analysis, int frame, float[] bandMean)
        {
            int bands = analysis.bandFlux.Length;
            int best = 0;
            float bestValue = float.MinValue;

            for (int b = 0; b < bands; b++)
            {
                float[] band = analysis.bandFlux[b];

                // Sum a tiny window: the peak of a band can sit one frame off the global peak.
                float sum = band[frame];
                if (frame > 0) sum += band[frame - 1] * 0.5f;
                if (frame + 1 < analysis.frameCount) sum += band[frame + 1] * 0.5f;

                float score = sum / (bandMean[b] + 0.0001f);

                if (score <= bestValue)
                    continue;

                bestValue = score;
                best = b;
            }

            return best;
        }

        private static float RollingMedian(float[] values, int center, int half, float[] scratch)
        {
            int start = Math.Max(0, center - half);
            int end = Math.Min(values.Length - 1, center + half);
            int count = end - start + 1;

            Array.Copy(values, start, scratch, 0, count);
            Array.Sort(scratch, 0, count);

            return scratch[count / 2];
        }
    }
}
