using UnityEngine;

namespace Necronomi.Rhythm
{
    /// <summary>Pulls raw PCM out of an AudioClip and folds it down to mono.</summary>
    public static class AudioSampleUtility
    {
        /// <summary>
        /// Reads the whole clip as mono samples. Fails loudly on the common trap:
        /// a clip set to Streaming or with audio data unloaded returns silence from GetData.
        /// </summary>
        public static bool TryGetMonoSamples(AudioClip clip, out float[] mono, out int sampleRate, out string error)
        {
            mono = null;
            sampleRate = 0;

            if (clip == null)
            {
                error = "No audio clip.";
                return false;
            }

            if (clip.loadType == AudioClipLoadType.Streaming)
            {
                error = $"'{clip.name}' is set to Streaming. Set Load Type to 'Decompress On Load' " +
                        "in the clip importer, then bake again.";
                return false;
            }

            if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            {
                error = $"Could not load audio data for '{clip.name}'. Enable 'Preload Audio Data' on the clip.";
                return false;
            }

            int channels = Mathf.Max(1, clip.channels);
            int frames = clip.samples;
            if (frames <= 0)
            {
                error = $"'{clip.name}' reports zero samples.";
                return false;
            }

            float[] interleaved = new float[frames * channels];
            if (!clip.GetData(interleaved, 0))
            {
                error = $"GetData failed on '{clip.name}'.";
                return false;
            }

            mono = new float[frames];
            if (channels == 1)
            {
                System.Array.Copy(interleaved, mono, frames);
            }
            else
            {
                float scale = 1f / channels;
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0f;
                    int baseIndex = i * channels;
                    for (int c = 0; c < channels; c++)
                        sum += interleaved[baseIndex + c];

                    mono[i] = sum * scale;
                }
            }

            sampleRate = clip.frequency;

            float peak = 0f;
            for (int i = 0; i < mono.Length; i += 64)
                peak = Mathf.Max(peak, Mathf.Abs(mono[i]));

            if (peak < 0.0001f)
            {
                error = $"'{clip.name}' decoded to silence. Check Load Type and Preload Audio Data on the importer.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
