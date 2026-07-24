using System;

namespace Necronomi.Rhythm
{
    /// <summary>
    /// In place radix 2 FFT with precomputed twiddle and bit reversal tables.
    /// One instance per transform size, reused across every frame of a bake.
    /// </summary>
    public sealed class FFT
    {
        private readonly int size;
        private readonly float[] cosTable;
        private readonly float[] sinTable;
        private readonly int[] reversed;

        public int Size => size;

        public FFT(int size)
        {
            if (size < 2 || (size & (size - 1)) != 0)
                throw new ArgumentException("FFT size must be a power of two.", nameof(size));

            this.size = size;

            int levels = 0;
            for (int temp = size; temp > 1; temp >>= 1)
                levels++;

            cosTable = new float[size / 2];
            sinTable = new float[size / 2];
            for (int i = 0; i < size / 2; i++)
            {
                double angle = 2d * Math.PI * i / size;
                cosTable[i] = (float)Math.Cos(angle);
                sinTable[i] = (float)Math.Sin(angle);
            }

            reversed = new int[size];
            for (int i = 0; i < size; i++)
            {
                int value = 0;
                for (int bit = 0; bit < levels; bit++)
                    value |= ((i >> bit) & 1) << (levels - 1 - bit);

                reversed[i] = value;
            }
        }

        /// <summary>Forward transform. Both arrays must be exactly Size long and are overwritten.</summary>
        public void Forward(float[] real, float[] imaginary)
        {
            if (real.Length != size || imaginary.Length != size)
                throw new ArgumentException("Buffers must match the FFT size.");

            for (int i = 0; i < size; i++)
            {
                int j = reversed[i];
                if (j <= i)
                    continue;

                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }

            for (int span = 2; span <= size; span <<= 1)
            {
                int half = span >> 1;
                int step = size / span;

                for (int i = 0; i < size; i += span)
                {
                    for (int j = i, k = 0; j < i + half; j++, k += step)
                    {
                        int l = j + half;
                        float cos = cosTable[k];
                        float sin = sinTable[k];

                        float tRe = real[l] * cos + imaginary[l] * sin;
                        float tIm = -real[l] * sin + imaginary[l] * cos;

                        real[l] = real[j] - tRe;
                        imaginary[l] = imaginary[j] - tIm;
                        real[j] += tRe;
                        imaginary[j] += tIm;
                    }
                }
            }
        }

        /// <summary>Hann window of the given length, precomputed once per bake.</summary>
        public static float[] HannWindow(int length)
        {
            float[] window = new float[length];
            for (int i = 0; i < length; i++)
                window[i] = 0.5f * (1f - (float)Math.Cos(2d * Math.PI * i / (length - 1)));

            return window;
        }
    }
}
