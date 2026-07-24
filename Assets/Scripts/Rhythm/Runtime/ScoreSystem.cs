using System;
using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// Score, combo and optional health. Plain class owned by the controller so a scene
    /// needs one component instead of three.
    /// </summary>
    [Serializable]
    public sealed class ScoreSystem
    {
        [Header("Points")]
        [SerializeField, Min(0)] private int perfectPoints = 1000;
        [SerializeField, Min(0)] private int greatPoints = 700;
        [SerializeField, Min(0)] private int goodPoints = 300;
        [Tooltip("Bonus for finishing a hold note")]
        [SerializeField, Min(0)] private int holdCompletePoints = 500;

        [Header("Combo")]
        [Tooltip("Notes per multiplier step")]
        [SerializeField, Min(1)] private int comboStep = 25;
        [SerializeField, Range(1f, 8f)] private float maxMultiplier = 4f;

        [Header("Health")]
        [Tooltip("Off means the song can never be failed and the run is scored only. " +
                 "Turn it on to enable the damage and fail out rules below")]
        [SerializeField] private bool useHealth = false;
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [SerializeField, Min(0f)] private float missDamage = 8f;
        [SerializeField, Min(0f)] private float emptyPressDamage = 1f;
        [SerializeField, Min(0f)] private float hitHeal = 1.5f;

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public float Health { get; private set; }
        public bool UseHealth => useHealth;
        public float HealthNormalized => maxHealth > 0f ? Health / maxHealth : 1f;
        public bool Dead => useHealth && Health <= 0f;

        public float Multiplier => Mathf.Min(maxMultiplier, 1f + Combo / (float)comboStep);

        private int perfect;
        private int great;
        private int good;
        private int miss;

        public void Reset()
        {
            Score = 0;
            Combo = 0;
            MaxCombo = 0;
            Health = maxHealth;
            perfect = great = good = miss = 0;
        }

        /// <summary>Applies one judgement. Returns the points gained.</summary>
        public int Register(Judgement judgement)
        {
            switch (judgement)
            {
                case Judgement.Perfect: perfect++; break;
                case Judgement.Great: great++; break;
                case Judgement.Good: good++; break;
                default: miss++; break;
            }

            if (judgement == Judgement.Miss)
            {
                Combo = 0;
                Damage(missDamage);
                return 0;
            }

            int basePoints = judgement switch
            {
                Judgement.Perfect => perfectPoints,
                Judgement.Great => greatPoints,
                _ => goodPoints
            };

            int gained = Mathf.RoundToInt(basePoints * Multiplier);
            Score += gained;

            Combo++;
            MaxCombo = Mathf.Max(MaxCombo, Combo);
            Heal(hitHeal);

            return gained;
        }

        public int RegisterHoldComplete()
        {
            int gained = Mathf.RoundToInt(holdCompletePoints * Multiplier);
            Score += gained;
            Heal(hitHeal);
            return gained;
        }

        /// <summary>Key pressed with no note anywhere near it. Breaks combo but is not a miss on the tally.</summary>
        public void RegisterEmptyPress()
        {
            Combo = 0;
            Damage(emptyPressDamage);
        }

        public RhythmResult BuildResult(bool failed)
        {
            int judged = perfect + great + good + miss;

            // Weighted accuracy: a Good counts a third as much as a Perfect.
            float weighted = perfect + great * 0.66f + good * 0.33f;

            return new RhythmResult
            {
                score = Score,
                maxCombo = MaxCombo,
                perfect = perfect,
                great = great,
                good = good,
                miss = miss,
                accuracy = judged > 0 ? weighted / judged : 0f,
                failed = failed
            };
        }

        private void Damage(float amount)
        {
            if (!useHealth)
                return;

            Health = Mathf.Max(0f, Health - amount);
        }

        private void Heal(float amount)
        {
            if (!useHealth)
                return;

            Health = Mathf.Min(maxHealth, Health + amount);
        }
    }
}
