namespace DigitalBrain.UI.Models.Dsp;

/// <summary>Transformada rápida de Fourier compleja, radix 2, in situ (Cooley–Tukey iterativa).</summary>
public static class Fft
{
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>La menor potencia de dos mayor o igual que <paramref name="n"/>.</summary>
    public static int NextPowerOfTwo(int n)
    {
        int size = 1;
        while (size < n) size <<= 1;
        return size;
    }

    /// <summary>Sustituye (<paramref name="re"/>, <paramref name="im"/>) por su DFT: X[k] = Σ x[n]·e^(−2πikn/N).</summary>
    public static void Transform(double[] re, double[] im)
    {
        int n = re.Length;
        if (im.Length != n || !IsPowerOfTwo(n))
            throw new ArgumentException("La FFT necesita dos vectores del mismo tamaño, potencia de dos.");

        // Reordenación por inversión de bits.
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        // Mariposas.
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            double wRe = Math.Cos(angle), wIm = Math.Sin(angle);
            int half = length / 2;
            for (int start = 0; start < n; start += length)
            {
                double curRe = 1, curIm = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k, b = a + half;
                    double tRe = re[b] * curRe - im[b] * curIm;
                    double tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    double nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
