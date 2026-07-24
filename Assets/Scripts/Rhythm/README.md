# Rhythm system

Bake any song into a note chart, then play it. Analysis happens in the Editor, the
runtime only reads baked data.

## Adding a song

1. Drop the audio file into `Assets/` (`.wav` preferred, `.mp3` / `.ogg` also work).
2. Select the clip. In the importer set **Load Type = Decompress On Load** and tick
   **Preload Audio Data**. The baker offers a one click fix if you forget.
3. `Necronomi > Rhythm > Chart Baker`, or right click the clip and pick
   `Necronomi > Bake Rhythm Chart`.
4. Assign a **Bake Settings** asset (or press *Create Settings Asset* once and reuse it),
   press **Bake Chart**.
5. A `SongChart` asset lands in `Assets/Rhythm/Charts`. Assign it to a
   `RhythmGameController` or a `RhythmInteractable`.

Re-baking with a chart in the **Overwrite Chart** slot keeps the same asset, so
scene references survive.

## How the notes are found

| Stage | File | What it does |
|---|---|---|
| Decode | `AudioSampleUtility` | Clip to mono float samples |
| STFT | `SpectralAnalysis` | 1024 sample windows every 512 samples, FFT each |
| Onset envelope | `SpectralAnalysis` | Spectral flux: sum of per bin energy *increase* between frames |
| Peak pick | `OnsetDetector` | Local maxima above a rolling median threshold |
| Tempo | `TempoEstimator` | Autocorrelation of the envelope, plus octave and phase correction |
| Hook | `HookDetector` | Loudest and most repeated section, used to trim playback |
| Chart | `ChartGenerator` | Quantize to the beat grid, assign lanes by frequency band, filter to the difficulty density |

Lane assignment uses the band that spiked hardest, so with the default edges
(150 / 800 / 4000 Hz) kicks land on lane 0 and hats on lane 3.

## Scene setup

```
RhythmGame                 Conductor + RhythmGameController + AudioSource
├── Input                  RhythmInput
├── Track                  NoteTrack   (assign the note prefab)
├── Lane0..Lane3           NoteLane    (each with a Spawn and Hit child transform)
└── Pool                   empty, assign as NoteTrack > Pool Parent
```

Wire the four `NoteLane` components into `RhythmGameController > Lanes` **in left to
right order** — the array order is the lane index.

The note prefab needs a `NoteView`. Optional slots on it: `holdBody` (scaled to cover
hold notes), `tintTargets` / `spriteTargets` (tinted with the lane colour).

## Events

Every event is a `UnityEvent`, so MMFeedbacks drop straight in.

On `RhythmGameController`:

| Event | Fires when |
|---|---|
| `onSongStarted` | Audio scheduled, notes inbound |
| `onNoteHit(Judgement)` | Note pressed in time |
| `onNoteMissed` | Note was **not** pressed and slipped past the hit line |
| `onEmptyPress` | Lane key pressed with no note in range |
| `onHoldBroken` | Hold note released early |
| `onComboBreak` | Combo dropped to zero for any reason |
| `onFailed` | Health ran out |
| `onSongFinished` | Song over, cleared or not |

On each `NoteLane`, the same signals scoped to that lane: `onPressed`, `onHit`,
`onMissed`, `onEmptyPress`. Use these for per lane screen shake or a lane flash.

Code side: `NoteJudged(Judgement, NoteData)` and `SongFinished(RhythmResult)`.

## Timing

`Conductor` runs on `AudioSettings.dspTime`, never `Time.time`. `SongPosition` is
authoritative and used for judging; `VisualPosition` is the same clock interpolated
between audio buffers and is used for note movement only.

Run `LatencyCalibrator` once per player: they tap along with a metronome, the median
offset is stored in `PlayerPrefs` and every chart uses it. Per song fudge lives in
`SongChart.audioOffset`.

## Tuning a bad chart

| Symptom | Fix |
|---|---|
| Notes feel random / off beat | Low `tempoConfidence`. Set `manualBpm` in the bake settings, or set `quantizeMode = Off` |
| Too many notes | Lower the difficulty, or raise `thresholdMultiplier` |
| No notes in quiet sections | Keep `logCompression` on, lower `thresholdFloor` |
| Ambient track produces nothing | Narrow `fluxMinHz` / `fluxMaxHz` to 150–4000 Hz |
| Hiss generating notes | Lower `fluxMaxHz` |
| Whole bars stuck on one lane | Lower `maxLaneRun`, or move `laneBandEdgesHz` |
| Chart drifts late over time | BPM is slightly off. Fix `bpm` and `firstBeatTime` on the chart by hand |
