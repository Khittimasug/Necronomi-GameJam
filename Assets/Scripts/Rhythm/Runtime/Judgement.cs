using System;
using UnityEngine;

namespace Necronomi.Rhythm
{
    public enum Judgement
    {
        Perfect = 0,
        Great = 1,
        Good = 2,
        Miss = 3
    }

    /// <summary>Hit window sizes in milliseconds. Widen these if the game feels unfair on laptops.</summary>
    [Serializable]
    public sealed class JudgementWindows
    {
        [Tooltip("Milliseconds either side of the note")]
        [Range(10f, 120f)] public float perfectMs = 45f;
        [Range(20f, 200f)] public float greatMs = 90f;
        [Range(40f, 300f)] public float goodMs = 135f;
        [Tooltip("Past this the note is gone and counts as a miss")]
        [Range(60f, 400f)] public float missMs = 180f;

        public double PerfectSeconds => perfectMs / 1000d;
        public double GreatSeconds => greatMs / 1000d;
        public double GoodSeconds => goodMs / 1000d;
        public double MissSeconds => missMs / 1000d;

        /// <summary>Widest window a press can still claim a note from.</summary>
        public double HitSeconds => GoodSeconds;

        public Judgement Classify(double delta)
        {
            double absolute = Math.Abs(delta);

            if (absolute <= PerfectSeconds) return Judgement.Perfect;
            if (absolute <= GreatSeconds) return Judgement.Great;
            if (absolute <= GoodSeconds) return Judgement.Good;

            return Judgement.Miss;
        }

        public void Validate()
        {
            greatMs = Mathf.Max(greatMs, perfectMs + 1f);
            goodMs = Mathf.Max(goodMs, greatMs + 1f);
            missMs = Mathf.Max(missMs, goodMs + 1f);
        }
    }

    /// <summary>Everything the game wants to know once a song is over.</summary>
    public struct RhythmResult
    {
        public int score;
        public int maxCombo;
        public int perfect;
        public int great;
        public int good;
        public int miss;
        public float accuracy;
        public bool failed;

        public int TotalNotes => perfect + great + good + miss;
        public bool FullCombo => miss == 0 && TotalNotes > 0;
        public bool Cleared => !failed;

        public override string ToString() =>
            $"score {score}  acc {accuracy:P1}  P{perfect}/G{great}/g{good}/M{miss}  combo {maxCombo}" +
            (failed ? "  FAILED" : string.Empty);
    }
}
