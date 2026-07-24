using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Turns an AudioClip into a SongChart. Every stage is deterministic, so the same
    /// clip and settings always bake the same chart.
    /// </summary>
    public static class ChartGenerator
    {
        /// <summary>
        /// Fills <paramref name="target"/> in place. Returns false and sets
        /// <paramref name="error"/> when the clip cannot be analysed at all.
        /// </summary>
        public static bool Generate(AudioClip clip, ChartBakeSettings settings, SongChart target,
            Action<float, string> progress, out string error)
        {
            if (target == null)
            {
                error = "No chart asset to write into.";
                return false;
            }

            if (settings == null)
                settings = ChartBakeSettings.CreateDefault();

            progress?.Invoke(0.02f, "Reading samples");

            if (!AudioSampleUtility.TryGetMonoSamples(clip, out float[] mono, out int sampleRate, out error))
                return false;

            progress?.Invoke(0.08f, "Analysing spectrum");

            SpectralAnalysis analysis = SpectralAnalyzer.Analyze(mono, sampleRate, settings,
                (t, label) => progress?.Invoke(0.08f + t * 0.55f, label));

            if (analysis == null)
            {
                error = "Clip is too short to analyse.";
                return false;
            }

            progress?.Invoke(0.66f, "Picking onsets");
            List<Onset> onsets = OnsetDetector.Detect(analysis, settings);

            progress?.Invoke(0.74f, "Estimating tempo");
            TempoResult tempo = TempoEstimator.Estimate(analysis, settings);

            progress?.Invoke(0.80f, "Finding hook");
            double clipLength = clip.length;
            HookRange hook = settings.trimToHook
                ? HookDetector.Detect(analysis, settings.hookLengthSeconds, clipLength)
                : new HookRange { start = 0d, end = clipLength, score = 0f };

            double windowStart = Math.Max(0d, hook.start - (settings.trimToHook ? settings.hookPreRoll : 0d));
            double windowEnd = Math.Min(clipLength, hook.end);
            if (windowEnd <= windowStart)
            {
                windowStart = 0d;
                windowEnd = clipLength;
            }

            StringBuilder report = new StringBuilder();
            report.AppendLine($"Clip        : {clip.name}  ({clipLength:F1}s, {sampleRate} Hz, {clip.channels}ch)");
            report.AppendLine($"Frames      : {analysis.frameCount} @ {analysis.FrameDuration * 1000d:F1} ms");
            report.AppendLine($"Raw onsets  : {onsets.Count}");
            report.AppendLine($"Tempo       : {tempo.bpm:F1} BPM, first beat {tempo.firstBeatTime:F3}s, " +
                              $"confidence {tempo.confidence:P0}{(settings.manualBpm > 0f ? " (manual)" : string.Empty)}");
            report.AppendLine($"Window      : {windowStart:F2}s -> {windowEnd:F2}s" +
                              (settings.trimToHook ? $"  (hook score {hook.score:F2})" : "  (full clip)"));

            progress?.Invoke(0.85f, "Building notes");

            List<Onset> inWindow = new List<Onset>(onsets.Count);
            for (int i = 0; i < onsets.Count; i++)
            {
                if (onsets[i].time >= windowStart && onsets[i].time <= windowEnd)
                    inWindow.Add(onsets[i]);
            }

            report.AppendLine($"In window   : {inWindow.Count}");

            List<Onset> quantized = Quantize(inWindow, tempo, settings, out int dropped);
            if (settings.quantizeMode != QuantizeMode.Off)
                report.AppendLine($"Quantized   : {quantized.Count} kept, {dropped} off grid dropped " +
                                  $"(1/{settings.subdivision * 4} grid)");

            List<Onset> spaced = EnforceSpacing(quantized, settings.minNoteSpacing);
            report.AppendLine($"After gap   : {spaced.Count} (min {settings.minNoteSpacing * 1000f:F0} ms apart)");

            double playable = Math.Max(0.1d, windowEnd - windowStart);
            int targetCount = Mathf.Max(1, Mathf.RoundToInt((float)(settings.TargetNotesPerSecond * playable)));
            List<Onset> filtered = ApplyDensity(spaced, targetCount);
            report.AppendLine($"Density     : {filtered.Count} notes for {playable:F1}s " +
                              $"= {filtered.Count / playable:F2}/s (target {settings.TargetNotesPerSecond:F2}/s)");

            BreakLaneRuns(filtered, settings);

            NoteData[] notes = BuildNotes(filtered, analysis, settings);
            report.AppendLine($"Final       : {notes.Length} notes across {settings.laneCount} lanes");
            report.Append(LaneHistogram(notes, settings.laneCount));

            target.clip = clip;
            if (string.IsNullOrEmpty(target.title))
                target.title = clip.name;

            target.bpm = tempo.bpm;
            target.firstBeatTime = tempo.firstBeatTime;
            target.tempoConfidence = tempo.confidence;
            target.startTime = windowStart;
            target.endTime = windowEnd;
            target.laneCount = settings.laneCount;
            target.notes = notes;
            target.bakeReport = report.ToString();
            target.SortNotes();

            progress?.Invoke(1f, "Done");
            error = null;
            return true;
        }

        private static List<Onset> Quantize(List<Onset> onsets, TempoResult tempo, ChartBakeSettings settings,
            out int dropped)
        {
            dropped = 0;

            if (settings.quantizeMode == QuantizeMode.Off || tempo.bpm <= 0f)
                return new List<Onset>(onsets);

            double step = tempo.SecondsPerBeat / Math.Max(1, settings.subdivision);
            double origin = tempo.firstBeatTime;
            List<Onset> result = new List<Onset>(onsets.Count);

            for (int i = 0; i < onsets.Count; i++)
            {
                Onset onset = onsets[i];
                double index = Math.Round((onset.time - origin) / step);
                double snapped = origin + index * step;
                double distance = Math.Abs(snapped - onset.time);

                if (settings.quantizeMode == QuantizeMode.SnapOrDrop && distance > settings.snapToleranceSeconds)
                {
                    dropped++;
                    continue;
                }

                if (snapped < 0d)
                {
                    dropped++;
                    continue;
                }

                onset.time = snapped;
                result.Add(onset);
            }

            return result;
        }

        /// <summary>Quantizing stacks notes onto the same grid line. Keep the strongest of each cluster.</summary>
        private static List<Onset> EnforceSpacing(List<Onset> onsets, double minSpacing)
        {
            List<Onset> result = new List<Onset>(onsets.Count);

            for (int i = 0; i < onsets.Count; i++)
            {
                Onset onset = onsets[i];

                if (result.Count > 0 && onset.time - result[^1].time < minSpacing)
                {
                    if (onset.strength > result[^1].strength)
                        result[^1] = onset;

                    continue;
                }

                result.Add(onset);
            }

            return result;
        }

        /// <summary>Keeps the hardest hitting notes until the chart matches the difficulty density.</summary>
        private static List<Onset> ApplyDensity(List<Onset> onsets, int targetCount)
        {
            if (onsets.Count <= targetCount)
                return onsets;

            int[] order = new int[onsets.Count];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;

            List<Onset> source = onsets;
            Array.Sort(order, (a, b) => source[b].strength.CompareTo(source[a].strength));

            bool[] keep = new bool[onsets.Count];
            for (int i = 0; i < targetCount; i++)
                keep[order[i]] = true;

            List<Onset> result = new List<Onset>(targetCount);
            for (int i = 0; i < onsets.Count; i++)
            {
                if (keep[i])
                    result.Add(onsets[i]);
            }

            return result;
        }

        /// <summary>
        /// Band mapping can park a whole bar on one lane. Nudge every note past the run
        /// limit to a neighbour so the player's hands keep moving.
        /// </summary>
        private static void BreakLaneRuns(List<Onset> onsets, ChartBakeSettings settings)
        {
            if (settings.maxLaneRun <= 0 || settings.laneCount < 2)
                return;

            int run = 0;
            int previousLane = -1;

            for (int i = 0; i < onsets.Count; i++)
            {
                Onset onset = onsets[i];

                if (onset.lane == previousLane)
                {
                    run++;
                    if (run >= settings.maxLaneRun)
                    {
                        int shifted = onset.lane + (i % 2 == 0 ? 1 : -1);
                        if (shifted < 0 || shifted >= settings.laneCount)
                            shifted = onset.lane + (shifted < 0 ? 1 : -1);

                        onset.lane = Math.Clamp(shifted, 0, settings.laneCount - 1);
                        onsets[i] = onset;
                        run = 0;
                    }
                }
                else
                {
                    run = 0;
                }

                previousLane = onsets[i].lane;
            }
        }

        private static NoteData[] BuildNotes(List<Onset> onsets, SpectralAnalysis analysis, ChartBakeSettings settings)
        {
            NoteData[] notes = new NoteData[onsets.Count];

            for (int i = 0; i < onsets.Count; i++)
            {
                Onset onset = onsets[i];

                NoteData note = new NoteData
                {
                    time = onset.time,
                    lane = Math.Clamp(onset.lane, 0, settings.laneCount - 1),
                    type = NoteType.Tap,
                    holdDuration = 0d,
                    strength = onset.strength
                };

                if (settings.generateHolds)
                    TryMakeHold(ref note, onsets, i, analysis, settings);

                notes[i] = note;
            }

            return notes;
        }

        /// <summary>
        /// A hold is an onset with room after it and energy that keeps going, so the
        /// note reads as a sustained sound rather than a stray gap in the chart.
        /// </summary>
        private static void TryMakeHold(ref NoteData note, List<Onset> onsets, int index,
            SpectralAnalysis analysis, ChartBakeSettings settings)
        {
            double nextTime = index + 1 < onsets.Count ? onsets[index + 1].time : double.MaxValue;
            double gap = nextTime - note.time;
            double minHold = settings.minHoldSeconds;

            if (gap < minHold * 1.5d)
                return;

            int onsetFrame = analysis.TimeToFrame(note.time);
            if (onsetFrame < 0 || onsetFrame >= analysis.frameCount)
                return;

            float onsetRms = analysis.rms[onsetFrame];
            if (onsetRms <= 0.0001f)
                return;

            double duration = Math.Min(gap - minHold * 0.5d, 4d);
            int endFrame = Math.Min(analysis.frameCount - 1, analysis.TimeToFrame(note.time + duration));

            float sustain = 0f;
            int counted = 0;
            for (int f = onsetFrame + 1; f <= endFrame; f++)
            {
                sustain += analysis.rms[f];
                counted++;
            }

            if (counted == 0)
                return;

            sustain /= counted;
            if (sustain < onsetRms * settings.holdSustainRatio)
                return;

            note.type = NoteType.Hold;
            note.holdDuration = duration;
        }

        private static string LaneHistogram(NoteData[] notes, int laneCount)
        {
            int[] counts = new int[Math.Max(1, laneCount)];
            for (int i = 0; i < notes.Length; i++)
            {
                int lane = Math.Clamp(notes[i].lane, 0, counts.Length - 1);
                counts[lane]++;
            }

            StringBuilder builder = new StringBuilder("Lanes       : ");
            for (int i = 0; i < counts.Length; i++)
                builder.Append($"L{i}={counts[i]}  ");

            return builder.ToString();
        }
    }
}
