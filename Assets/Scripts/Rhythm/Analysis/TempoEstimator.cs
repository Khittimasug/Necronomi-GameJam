using System;

namespace Necronomi.Rhythm
{
    public struct TempoResult
    {
        public float bpm;
        /// <summary>Time of the first beat on the clip timeline, seconds.</summary>
        public double firstBeatTime;
        /// <summary>0..1. Below roughly 0.35 the grid is a guess, check it by hand.</summary>
        public float confidence;

        public double SecondsPerBeat => bpm > 0f ? 60d / bpm : 0.5d;
    }

    /// <summary>
    /// Finds tempo by autocorrelating the onset envelope: shift the curve against
    /// itself and the lag that lines up best is one beat.
    /// </summary>
    public static class TempoEstimator
    {
        public static TempoResult Estimate(SpectralAnalysis analysis, ChartBakeSettings settings)
        {
            float[] envelope = analysis.flux;
            double frameDuration = analysis.FrameDuration;
            int frameCount = analysis.frameCount;

            TempoResult fallback = new TempoResult { bpm = 120f, firstBeatTime = 0d, confidence = 0f };
            if (frameCount < 64)
                return fallback;

            // Mean subtract so the autocorrelation measures rhythm, not overall loudness.
            double mean = 0d;
            for (int i = 0; i < frameCount; i++)
                mean += envelope[i];

            mean /= frameCount;

            float[] centered = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
                centered[i] = (float)(envelope[i] - mean);

            int minLag = Math.Max(2, (int)Math.Round(60d / settings.bpmMax / frameDuration));
            int maxLag = Math.Min(frameCount / 2, (int)Math.Round(60d / settings.bpmMin / frameDuration));
            if (maxLag <= minLag)
                return fallback;

            float[] correlation = new float[maxLag + 1];
            float best = 0f;
            int bestLag = minLag;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0d;
                int count = frameCount - lag;
                for (int i = 0; i < count; i++)
                    sum += centered[i] * (double)centered[i + lag];

                float score = (float)(sum / count);
                correlation[lag] = score;

                if (score <= best)
                    continue;

                best = score;
                bestLag = lag;
            }

            if (best <= 0f)
                return fallback;

            bestLag = CorrectOctave(correlation, bestLag, minLag, maxLag, frameDuration);

            float bpm = (float)(60d / (bestLag * frameDuration));
            if (settings.manualBpm > 0f)
                bpm = settings.manualBpm;

            double beatSeconds = 60d / bpm;
            double lagFrames = beatSeconds / frameDuration;

            // Confidence: how far the winning lag stands above the average correlation.
            double average = 0d;
            int considered = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                average += Math.Abs(correlation[lag]);
                considered++;
            }

            average = considered > 0 ? average / considered : 1d;
            float confidence = average > 0d
                ? (float)Math.Clamp((best / average - 1d) / 3d, 0d, 1d)
                : 0f;

            double firstBeat = FindPhase(envelope, frameCount, lagFrames, frameDuration);

            return new TempoResult
            {
                bpm = bpm,
                firstBeatTime = firstBeat,
                confidence = confidence
            };
        }

        /// <summary>
        /// Autocorrelation happily reports half or double the real tempo. Prefer the
        /// candidate that lands in the comfortable 85-165 BPM range when scores are close.
        /// </summary>
        private static int CorrectOctave(float[] correlation, int bestLag, int minLag, int maxLag, double frameDuration)
        {
            float bestScore = correlation[bestLag];
            int chosen = bestLag;
            float chosenBpm = (float)(60d / (bestLag * frameDuration));
            float chosenPenalty = RangePenalty(chosenBpm);

            Span<int> candidates = stackalloc int[] { bestLag / 2, bestLag * 2 };

            foreach (int lag in candidates)
            {
                if (lag < minLag || lag > maxLag)
                    continue;

                if (correlation[lag] < bestScore * 0.7f)
                    continue;

                float bpm = (float)(60d / (lag * frameDuration));
                float penalty = RangePenalty(bpm);
                if (penalty >= chosenPenalty)
                    continue;

                chosen = lag;
                chosenPenalty = penalty;
            }

            return chosen;
        }

        private static float RangePenalty(float bpm)
        {
            if (bpm >= 85f && bpm <= 165f)
                return 0f;

            return bpm < 85f ? 85f - bpm : bpm - 165f;
        }

        /// <summary>Slides the beat grid across one beat and keeps the offset that collects the most onset energy.</summary>
        private static double FindPhase(float[] envelope, int frameCount, double lagFrames, double frameDuration)
        {
            int steps = Math.Max(8, (int)Math.Round(lagFrames));
            double bestScore = double.MinValue;
            double bestOffset = 0d;

            for (int s = 0; s < steps; s++)
            {
                double offset = s * lagFrames / steps;
                double score = 0d;

                for (double position = offset; position < frameCount; position += lagFrames)
                {
                    int index = (int)Math.Round(position);
                    if (index < 0 || index >= frameCount)
                        continue;

                    // Include neighbours: a beat rarely lands exactly on a frame boundary.
                    score += envelope[index];
                    if (index > 0) score += envelope[index - 1] * 0.5f;
                    if (index + 1 < frameCount) score += envelope[index + 1] * 0.5f;
                }

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestOffset = offset;
            }

            return bestOffset * frameDuration;
        }
    }
}
