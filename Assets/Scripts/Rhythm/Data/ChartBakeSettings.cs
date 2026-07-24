using UnityEngine;

namespace Necronomi.Rhythm
{
    public enum ChartDifficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Custom = 3
    }

    public enum QuantizeMode
    {
        /// <summary>Keep raw onset times. Most faithful, least musical.</summary>
        Off = 0,
        /// <summary>Pull every note to the nearest grid line, however far it is.</summary>
        Snap = 1,
        /// <summary>Snap notes near a grid line, throw away the rest. Cleanest charts.</summary>
        SnapOrDrop = 2
    }

    /// <summary>
    /// Every knob the baker uses. Saved as an asset so each song can keep its own
    /// preset instead of re-tuning sliders every bake.
    /// </summary>
    [CreateAssetMenu(menuName = "Necronomi/Rhythm/Chart Bake Settings", fileName = "BakeSettings_Default")]
    public sealed class ChartBakeSettings : ScriptableObject
    {
        [Header("Difficulty")]
        public ChartDifficulty difficulty = ChartDifficulty.Normal;
        [Tooltip("Only used when difficulty is Custom")]
        [Range(0.5f, 12f)] public float customNotesPerSecond = 4f;

        [Header("Layout")]
        [Min(1)] public int laneCount = 4;
        [Tooltip("Crossover frequencies between lanes, ascending. Needs laneCount - 1 entries. " +
                 "Default splits kick / snare / lead / hats")]
        public float[] laneBandEdgesHz = { 150f, 800f, 4000f };
        [Tooltip("How many notes in a row may share a lane before one is nudged sideways. 0 disables")]
        [Min(0)] public int maxLaneRun = 3;

        [Header("Analysis")]
        [Tooltip("FFT window in samples. Must be a power of two")]
        public int fftSize = 1024;
        [Tooltip("Samples between analysis frames. Half the FFT size is the usual choice")]
        public int hopSize = 512;
        [Tooltip("Ignore flux below this frequency. Raise it for rumbly or droning tracks")]
        [Min(0f)] public float fluxMinHz = 40f;
        [Tooltip("Ignore flux above this frequency. Lower it if hiss is generating notes")]
        [Min(100f)] public float fluxMaxHz = 12000f;
        [Tooltip("Log compress the spectrum before differencing. Keeps quiet passages detectable")]
        public bool logCompression = true;
        [Min(1f)] public float compressionLambda = 1000f;

        [Header("Onset picking")]
        [Tooltip("Frames either side used for the local median threshold")]
        [Range(3, 80)] public int thresholdWindow = 20;
        [Tooltip("Multiplier on the local median. Higher means fewer, stronger onsets")]
        [Range(1f, 4f)] public float thresholdMultiplier = 1.5f;
        [Tooltip("Absolute floor so silence cannot produce onsets")]
        [Range(0f, 0.5f)] public float thresholdFloor = 0.02f;
        [Tooltip("Hard minimum spacing between any two notes, seconds")]
        [Range(0.02f, 0.5f)] public float minNoteSpacing = 0.09f;

        [Header("Tempo")]
        [Tooltip("0 means estimate from the audio")]
        [Min(0f)] public float manualBpm = 0f;
        [Range(40f, 200f)] public float bpmMin = 70f;
        [Range(60f, 260f)] public float bpmMax = 180f;

        [Header("Quantize")]
        public QuantizeMode quantizeMode = QuantizeMode.SnapOrDrop;
        [Tooltip("Grid divisions per beat. 2 = eighths, 4 = sixteenths")]
        [Range(1, 8)] public int subdivision = 4;
        [Tooltip("A note further than this from a grid line is dropped in SnapOrDrop mode")]
        [Range(0.01f, 0.2f)] public float snapToleranceSeconds = 0.06f;

        [Header("Hook")]
        [Tooltip("Trim playback to the strongest, most repeated section of the song")]
        public bool trimToHook = true;
        [Tooltip("Length of the trimmed section, seconds")]
        [Range(10f, 180f)] public float hookLengthSeconds = 40f;
        [Tooltip("Extra quiet lead in kept before the hook, seconds")]
        [Range(0f, 4f)] public float hookPreRoll = 1f;

        [Header("Holds")]
        [Tooltip("Turn strong onsets followed by a sustain into hold notes")]
        public bool generateHolds = false;
        [Range(0.2f, 3f)] public float minHoldSeconds = 0.45f;
        [Tooltip("Sustain must stay above this fraction of the onset energy to count")]
        [Range(0.1f, 0.95f)] public float holdSustainRatio = 0.5f;

        /// <summary>Notes per second the density filter aims for.</summary>
        public float TargetNotesPerSecond => difficulty switch
        {
            ChartDifficulty.Easy => 2f,
            ChartDifficulty.Normal => 3.5f,
            ChartDifficulty.Hard => 5.5f,
            _ => Mathf.Max(0.5f, customNotesPerSecond)
        };

        /// <summary>Used when no settings asset is assigned, so baking always works out of the box.</summary>
        public static ChartBakeSettings CreateDefault()
        {
            ChartBakeSettings settings = CreateInstance<ChartBakeSettings>();
            settings.name = "BakeSettings_Runtime";
            return settings;
        }

        private void OnValidate()
        {
            fftSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(fftSize, 256, 8192));
            hopSize = Mathf.Clamp(hopSize, 64, fftSize);
            bpmMax = Mathf.Max(bpmMax, bpmMin + 10f);
            fluxMaxHz = Mathf.Max(fluxMaxHz, fluxMinHz + 100f);
            laneCount = Mathf.Max(1, laneCount);

            int wanted = laneCount - 1;
            if (laneBandEdgesHz == null || laneBandEdgesHz.Length == wanted)
                return;

            // Keep the edge list matching the lane count so the baker never indexes past it.
            float[] resized = new float[wanted];
            for (int i = 0; i < wanted; i++)
            {
                resized[i] = laneBandEdgesHz != null && i < laneBandEdgesHz.Length
                    ? laneBandEdgesHz[i]
                    : Mathf.Lerp(120f, 8000f, wanted <= 1 ? 0.5f : i / (float)(wanted - 1));
            }

            laneBandEdgesHz = resized;
        }
    }
}
