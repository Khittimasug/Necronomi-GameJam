using System;

namespace Necronomi.Rhythm
{
    public struct HookRange
    {
        public double start;
        public double end;
        public float score;

        public double Length => end - start;
    }

    /// <summary>
    /// Finds the section worth charting. The hook is the part that is both loud and
    /// repeated, so score every window on energy plus how similar it is to windows
    /// elsewhere in the song, then take the best run of the requested length.
    /// </summary>
    public static class HookDetector
    {
        private const double WindowSeconds = 4d;
        private const double HopSeconds = 1d;
        private const double MinSimilarityDistanceSeconds = 8d;

        public static HookRange Detect(SpectralAnalysis analysis, double hookLength, double clipLength)
        {
            double usable = Math.Min(clipLength, analysis.FrameToTime(analysis.frameCount - 1));

            if (hookLength <= 0d || usable <= hookLength + WindowSeconds)
                return new HookRange { start = 0d, end = usable, score = 0f };

            int windowFrames = Math.Max(1, (int)Math.Round(WindowSeconds / analysis.FrameDuration));
            int hopFrames = Math.Max(1, (int)Math.Round(HopSeconds / analysis.FrameDuration));
            int windowCount = (analysis.frameCount - windowFrames) / hopFrames + 1;
            if (windowCount < 4)
                return new HookRange { start = 0d, end = usable, score = 0f };

            int bands = SpectralAnalysis.TimbreBands;
            float[][] features = new float[windowCount][];
            float[] energy = new float[windowCount];

            for (int w = 0; w < windowCount; w++)
            {
                int start = w * hopFrames;
                int end = Math.Min(analysis.frameCount, start + windowFrames);
                int count = end - start;

                float[] vector = new float[bands];
                for (int b = 0; b < bands; b++)
                {
                    float sum = 0f;
                    for (int f = start; f < end; f++)
                        sum += analysis.timbre[b][f];

                    vector[b] = sum / count;
                }

                NormalizeVector(vector);
                features[w] = vector;

                float rmsSum = 0f;
                for (int f = start; f < end; f++)
                    rmsSum += analysis.rms[f];

                energy[w] = rmsSum / count;
            }

            NormalizeArray(energy);

            int minDistanceWindows = Math.Max(1, (int)Math.Round(MinSimilarityDistanceSeconds / HopSeconds));
            float[] repetition = new float[windowCount];

            for (int w = 0; w < windowCount; w++)
            {
                float best = 0f;
                for (int other = 0; other < windowCount; other++)
                {
                    if (Math.Abs(other - w) < minDistanceWindows)
                        continue;

                    best = Math.Max(best, CosineSimilarity(features[w], features[other]));
                }

                repetition[w] = best;
            }

            NormalizeArray(repetition);

            float[] combined = new float[windowCount];
            for (int w = 0; w < windowCount; w++)
                combined[w] = energy[w] * 0.55f + repetition[w] * 0.45f;

            int runWindows = Math.Max(1, (int)Math.Round(hookLength / HopSeconds));
            runWindows = Math.Min(runWindows, windowCount);

            float bestSum = float.MinValue;
            int bestStart = 0;
            float running = 0f;

            for (int w = 0; w < windowCount; w++)
            {
                running += combined[w];
                if (w >= runWindows)
                    running -= combined[w - runWindows];

                if (w < runWindows - 1)
                    continue;

                if (running <= bestSum)
                    continue;

                bestSum = running;
                bestStart = w - runWindows + 1;
            }

            double startTime = bestStart * HopSeconds;
            double endTime = Math.Min(usable, startTime + hookLength);

            return new HookRange
            {
                start = startTime,
                end = endTime,
                score = runWindows > 0 ? bestSum / runWindows : 0f
            };
        }

        private static float CosineSimilarity(float[] a, float[] b)
        {
            float dot = 0f;
            for (int i = 0; i < a.Length; i++)
                dot += a[i] * b[i];

            return Math.Clamp(dot, 0f, 1f);
        }

        private static void NormalizeVector(float[] vector)
        {
            float sum = 0f;
            for (int i = 0; i < vector.Length; i++)
                sum += vector[i] * vector[i];

            float length = (float)Math.Sqrt(sum);
            if (length <= 0.00001f)
                return;

            for (int i = 0; i < vector.Length; i++)
                vector[i] /= length;
        }

        private static void NormalizeArray(float[] values)
        {
            float min = float.MaxValue;
            float max = float.MinValue;

            for (int i = 0; i < values.Length; i++)
            {
                min = Math.Min(min, values[i]);
                max = Math.Max(max, values[i]);
            }

            float range = max - min;
            if (range <= 0.00001f)
            {
                Array.Fill(values, 0f);
                return;
            }

            for (int i = 0; i < values.Length; i++)
                values[i] = (values[i] - min) / range;
        }
    }
}
