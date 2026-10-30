// Procedural monster sound synthesizer.
// Compiled on the fly by generate-sounds.ps1 (C# 5 syntax, works with Windows PowerShell 5.1).
using System;
using System.IO;
using System.Collections.Generic;

public class Biquad
{
    double b0, b1, b2, a1, a2, x1, x2, y1, y2;

    static void Prep(double f, double q, out double c, out double alpha)
    {
        f = Math.Max(10, Math.Min(f, Synth.SR * 0.45));
        double w = 2 * Math.PI * f / Synth.SR;
        c = Math.Cos(w);
        alpha = Math.Sin(w) / (2 * q);
    }

    public Biquad LP(double f, double q)
    {
        double c, a; Prep(f, q, out c, out a);
        double a0 = 1 + a;
        b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - a) / a0;
        return this;
    }

    public Biquad HP(double f, double q)
    {
        double c, a; Prep(f, q, out c, out a);
        double a0 = 1 + a;
        b0 = (1 + c) / 2 / a0; b1 = -(1 + c) / a0; b2 = b0; a1 = -2 * c / a0; a2 = (1 - a) / a0;
        return this;
    }

    // Band-pass with 0 dB peak gain.
    public Biquad BP(double f, double q)
    {
        double c, a; Prep(f, q, out c, out a);
        double a0 = 1 + a;
        b0 = a / a0; b1 = 0; b2 = -a / a0; a1 = -2 * c / a0; a2 = (1 - a) / a0;
        return this;
    }

    public double Run(double x)
    {
        double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
        x2 = x1; x1 = x; y2 = y1; y1 = y;
        return y;
    }
}

// Smoothly interpolated random values in [-1, 1] changing at roughly `rate` Hz.
public class SmoothNoise
{
    double rate, phase, a, b;
    public SmoothNoise(double rate) { this.rate = rate; a = Synth.Noise(); b = Synth.Noise(); }
    public double Next()
    {
        phase += rate / Synth.SR;
        if (phase >= 1) { phase -= 1; a = b; b = Synth.Noise(); }
        double t = phase * phase * (3 - 2 * phase);
        return a + (b - a) * t;
    }
}

public static class Synth
{
    public const int SR = 44100;
    public static Random Rng = new Random(31);

    // Formants (Hz) for a huge vocal tract: lower than a human's.
    static readonly double[] UH = { 450, 900, 1700 };
    static readonly double[] AW = { 520, 800, 1900 };
    static readonly double[] OO = { 300, 650, 1800 };
    static readonly double[] AA = { 600, 1000, 2000 };
    static readonly double[] EE = { 280, 1700, 2500 };
    static readonly double[] UR = { 380, 950, 1300 };
    static readonly double[] MM = { 220, 900, 2000 };

    public static double Noise() { return Rng.NextDouble() * 2 - 1; }
    static double Rand(double lo, double hi) { return lo + Rng.NextDouble() * (hi - lo); }
    static float[] Buf(double sec) { return new float[(int)(sec * SR)]; }
    static double T(int i) { return (double)i / SR; }

    static void Mix(float[] dst, float[] src, double at, double gain)
    {
        int off = (int)(at * SR);
        for (int i = 0; i < src.Length && off + i < dst.Length; i++)
            if (off + i >= 0) dst[off + i] += (float)(src[i] * gain);
    }

    // Linear attack / release envelope.
    static double Env(double t, double dur, double att, double rel)
    {
        if (t < 0 || t > dur) return 0;
        if (t < att) return t / att;
        if (t > dur - rel) return Math.Max(0, (dur - t) / rel);
        return 1;
    }

    static float[] Normalize(float[] x, double peak)
    {
        double m = 1e-9;
        foreach (float v in x) m = Math.Max(m, Math.Abs(v));
        for (int i = 0; i < x.Length; i++) x[i] = (float)(x[i] / m * peak);
        return x;
    }

    static float[] Drive(float[] x, double drive)
    {
        Normalize(x, 1);
        double d = Math.Tanh(drive);
        for (int i = 0; i < x.Length; i++) x[i] = (float)(Math.Tanh(x[i] * drive) / d);
        return x;
    }

    static float[] Fade(float[] x, double fadeOut)
    {
        int n = Math.Min(x.Length, (int)(fadeOut * SR));
        for (int i = 0; i < n; i++) x[x.Length - 1 - i] *= (float)((double)i / n);
        int m = Math.Min(x.Length, 64);
        for (int i = 0; i < m; i++) x[i] *= (float)((double)i / m);
        return x;
    }

    // Glottal-pulse voice through morphing formant filters, with subharmonics,
    // jitter and amplitude roughness for growl texture.
    static float[] Voice(double dur, Func<double, double> f0, Func<double, double> amp,
        double[] vA, double[] vB, Func<double, double> morph,
        double sub, double jitter, double rough, double roughRate, double breath, double drive)
    {
        float[] o = Buf(dur);
        SmoothNoise jit = new SmoothNoise(14), rn = new SmoothNoise(roughRate), rn2 = new SmoothNoise(roughRate * 1.73);
        Biquad[] fl = { new Biquad(), new Biquad(), new Biquad() };
        double[] fg = { 1.0, 0.75, 0.4 };
        Biquad body = new Biquad().LP(280, 0.8);
        double phase = 0; int period = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i);
            double f = f0(t) * (1 + jitter * jit.Next());
            phase += f / SR;
            if (phase >= 1) { phase -= 1; period++; }
            double src = 1 - 2 * phase;
            if (period % 2 == 1) src *= 1 - sub;
            src += breath * Noise() * (phase < 0.35 ? 1.0 : 0.35);
            if (i % 32 == 0)
            {
                double m = Math.Max(0, Math.Min(1, morph(t)));
                for (int k = 0; k < 3; k++)
                {
                    double ff = vA[k] + (vB[k] - vA[k]) * m;
                    fl[k].BP(ff, ff / (70 + ff * 0.06));
                }
            }
            double y = 0.6 * body.Run(src);
            for (int k = 0; k < 3; k++) y += fg[k] * fl[k].Run(src);
            double r = Math.Max(0, 1 + rough * (0.6 * rn.Next() + 0.4 * rn2.Next()));
            o[i] = (float)(y * amp(t) * r);
        }
        return Drive(o, drive);
    }

    static float[] FilteredNoise(double dur, Biquad f, Func<double, double> amp)
    {
        float[] o = Buf(dur);
        for (int i = 0; i < o.Length; i++) o[i] = (float)(f.Run(Noise()) * amp(T(i)));
        return o;
    }

    // Sine sweep with a quick attack and exponential decay: a bubble / glug.
    static float[] Bubble(double dur, double fStart, double fEnd)
    {
        float[] o = Buf(dur);
        double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i), p = t / dur;
            ph += 2 * Math.PI * (fStart * Math.Pow(fEnd / fStart, p)) / SR;
            double env = Math.Min(1, t / 0.004) * Math.Exp(-p * 4);
            o[i] = (float)(Math.Sin(ph) * env);
        }
        return o;
    }

    static float[] Thump(double dur, double fStart, double fEnd)
    {
        float[] o = Buf(dur);
        double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i), p = t / dur;
            ph += 2 * Math.PI * (fStart + (fEnd - fStart) * p) / SR;
            o[i] = (float)(Math.Sin(ph) * Math.Min(1, t / 0.003) * Math.Exp(-p * 5));
        }
        return o;
    }

    // Dense random clicks: teeth crunching.
    static float[] Crunch(double dur, double density)
    {
        float[] o = Buf(dur);
        Biquad bp = new Biquad().BP(2600, 0.7), hp = new Biquad().HP(900, 0.7);
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i);
            double x = Rng.NextDouble() < density ? Noise() * 8 : Noise() * 0.15;
            o[i] = (float)(hp.Run(bp.Run(x)) * Math.Exp(-t / dur * 3));
        }
        return o;
    }

    // ----- The sounds -----

    static float[] Growl()
    {
        double d = 2.8;
        float[] o = Voice(d, t => 52 + 14 * Math.Sin(Math.PI * t / d) + 4 * Math.Sin(2 * Math.PI * 0.7 * t),
            t => Env(t, d, 0.35, 0.8) * (0.8 + 0.2 * Math.Sin(2 * Math.PI * 1.3 * t)),
            UR, AW, t => Math.Sin(Math.PI * t / d), 0.5, 0.05, 0.8, 28, 0.5, 2.5);
        Mix(o, FilteredNoise(d, new Biquad().LP(110, 0.7), t => Env(t, d, 0.4, 0.8)), 0, 0.5);
        return o;
    }

    static float[] Roar()
    {
        double d = 3.0;
        Func<double, double> f0 = t => 62 + 62 * Math.Exp(-Math.Pow((t - 0.7) / 0.7, 2));
        Func<double, double> amp = t => Env(t, d, 0.12, 1.3);
        float[] o = Voice(d, f0, amp, OO, AA, t => t / 0.45, 0.35, 0.06, 0.9, 35, 1.2, 3.5);
        Mix(o, Voice(d, t => f0(t) * 0.5, amp, OO, AW, t => t / 0.6, 0.6, 0.08, 1.0, 22, 0.8, 3), 0, 0.6);
        Mix(o, FilteredNoise(d, new Biquad().BP(520, 0.7), amp), 0, 0.4);
        return Drive(o, 2);
    }

    static float[] Grunt()
    {
        float[] o = Buf(1.6);
        double d1 = 0.45, d2 = 0.85;
        Mix(o, Voice(d1, t => 100 - 40 * t / d1, t => Math.Min(1, t / 0.03) * Math.Exp(-(t - 0.03) * 5),
            UH, UR, t => t / d1, 0.4, 0.04, 0.5, 25, 0.6, 3), 0, 0.7);
        Mix(o, Voice(d2, t => 90 - 35 * t / d2, t => Math.Min(1, t / 0.03) * Math.Exp(-(t - 0.03) * 3.2),
            UH, UR, t => t / d2, 0.5, 0.05, 0.6, 22, 0.7, 3), 0.55, 1.0);
        return o;
    }

    static float[] Snarl()
    {
        double d = 2.0;
        SmoothNoise wob = new SmoothNoise(9);
        float[] o = Voice(d, t => 130 + 25 * Math.Sin(2 * Math.PI * 3 * t),
            t => Env(t, d, 0.15, 0.5) * (0.75 + 0.25 * wob.Next()),
            EE, AA, t => 0.3 + 0.2 * Math.Sin(2 * Math.PI * 1.5 * t), 0.5, 0.1, 1.0, 40, 1.5, 4);
        Mix(o, FilteredNoise(d, new Biquad().HP(3500, 0.7), t => Env(t, d, 0.1, 0.5)), 0, 0.25);
        return o;
    }

    static float[] Chomp()
    {
        float[] o = Buf(2.6);
        double[] at = { 0.0, 0.42, 0.86, 1.3, 1.75 };
        foreach (double t0 in at)
        {
            double s = t0 + Rand(-0.03, 0.03);
            Mix(o, Thump(0.14, Rand(85, 100), 45), s, 0.8);
            Mix(o, Crunch(Rand(0.06, 0.11), 0.05), s + 0.005, 0.55);
            Biquad sq = new Biquad().BP(Rand(600, 900), 2);
            SmoothNoise am = new SmoothNoise(35);
            Mix(o, FilteredNoise(0.18, sq, t => Math.Exp(-t * 18) * (0.5 + 0.5 * am.Next())), s + 0.02, 0.6);
            double nd = 0.32;
            Mix(o, Voice(nd, t => 85 - 12 * t / nd, t => Env(t, nd, 0.05, 0.2), MM, UH, t => t / nd,
                0.3, 0.04, 0.4, 25, 0.3, 2), s + 0.06, 0.35);
        }
        return o;
    }

    static float[] Slurp()
    {
        double d = 1.6;
        float[] o = Buf(d);
        Biquad bp = new Biquad(), low = new Biquad().BP(400, 3);
        SmoothNoise am = new SmoothNoise(32);
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i);
            bp.BP(250 * Math.Pow(12, t / d), 6);
            double a = Env(t, d, 0.1, 0.25) * (0.4 + 0.6 * Math.Abs(am.Next()));
            o[i] = (float)((bp.Run(Noise()) * 1.4 + low.Run(Noise()) * 0.5) * a);
        }
        for (int k = 0; k < 14; k++)
            Mix(o, Bubble(Rand(0.02, 0.04), Rand(250, 400), Rand(700, 1200)), Rand(0.1, d - 0.2), Rand(0.15, 0.35));
        return o;
    }

    static float[] Burp()
    {
        double d = 1.5;
        float[] o = Voice(d, t => 78 - 12 * t / d + 5 * Math.Sin(2 * Math.PI * 2 * t),
            t => Env(t, d, 0.04, 0.5), UR, AW, t => t / d * 0.8, 0.7, 0.12, 1.2, 18, 0.4, 4);
        for (int i = 0; i < o.Length; i++)
            o[i] *= (float)(0.6 + 0.4 * Math.Sign(Math.Sin(2 * Math.PI * 22 * T(i))));
        return o;
    }

    static float[] Gulp()
    {
        float[] o = Buf(2.2);
        foreach (double s in new double[] { 0.0, 0.45 })
        {
            Mix(o, Thump(0.09, 140, 60), s, 0.6);
            Mix(o, Bubble(0.08, 500, 180), s + 0.01, 0.6);
            Mix(o, Bubble(0.06, 180, 420), s + 0.08, 0.4);
        }
        double d = 1.1;
        Mix(o, Voice(d, t => 90 - 20 * t, t => Env(t, d, 0.05, 0.6) * 0.7, AA, AW, t => t / d,
            0.3, 0.05, 0.4, 25, 1.5, 1.5), 0.95, 0.6);
        return o;
    }

    static float[] Sniff()
    {
        float[] o = Buf(2.0);
        double[] at = { 0.0, 0.28, 0.56, 1.0 };
        double[] len = { 0.16, 0.16, 0.18, 0.7 };
        for (int k = 0; k < at.Length; k++)
        {
            double l = len[k];
            Biquad hi = new Biquad().BP(3200, 0.9), lo = new Biquad().BP(900, 1.5);
            float[] s = Buf(l);
            for (int i = 0; i < s.Length; i++)
            {
                double x = Noise();
                s[i] = (float)((hi.Run(x) + 0.4 * lo.Run(x)) * Math.Pow(Math.Sin(Math.PI * T(i) / l), 0.7));
            }
            Mix(o, s, at[k], 1.0);
        }
        return o;
    }

    static float[] Tummy()
    {
        double d = 3.2;
        float[] o = Buf(d);
        Biquad lp = new Biquad().LP(120, 0.7);
        SmoothNoise slow = new SmoothNoise(3);
        double brown = 0;
        for (int i = 0; i < o.Length; i++)
        {
            brown = brown * 0.995 + Noise() * 0.1;
            o[i] = (float)(lp.Run(brown) * 4 * Env(T(i), d, 0.4, 0.6) * (0.6 + 0.4 * slow.Next()));
        }
        // Tonal groan of the stomach.
        double gd = 1.3, ph = 0;
        float[] g = Buf(gd);
        Biquad glp = new Biquad().LP(450, 1.5);
        for (int i = 0; i < g.Length; i++)
        {
            double t = T(i);
            double f = 170 + 80 * Math.Sin(Math.PI * t / gd) + 8 * Math.Sin(2 * Math.PI * 7 * t);
            ph += f / SR; ph -= Math.Floor(ph);
            g[i] = (float)(glp.Run(1 - 2 * ph) * Math.Sin(Math.PI * t / gd));
        }
        Mix(o, g, 0.8, 0.5);
        for (int k = 0; k < 18; k++)
            Mix(o, Bubble(Rand(0.04, 0.1), Rand(120, 250), Rand(250, 600)), Rand(0.3, d - 0.3), Rand(0.3, 0.6));
        return o;
    }

    static float[] Hiss()
    {
        double d = 2.2;
        float[] o = Buf(d);
        Biquad hp = new Biquad().HP(2500, 0.7), bp = new Biquad().BP(5500, 0.8), rat = new Biquad().BP(400, 2);
        SmoothNoise wob = new SmoothNoise(8);
        for (int i = 0; i < o.Length; i++)
        {
            double t = T(i), x = Noise();
            double h = hp.Run(x);
            double rattle = rat.Run(Noise()) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 30 * t));
            o[i] = (float)((h + bp.Run(h) + 0.3 * rattle) * Env(t, d, 0.15, 0.6) * (0.85 + 0.15 * wob.Next()));
        }
        return o;
    }

    static float[] Snore()
    {
        float[] o = Buf(3.6);
        double di = 1.7;
        float[] inh = Buf(di);
        Biquad bp = new Biquad().BP(450, 1.5);
        SmoothNoise jit = new SmoothNoise(6);
        double ph = 0;
        for (int i = 0; i < inh.Length; i++)
        {
            double t = T(i);
            ph += (30 + 4 * jit.Next()) / SR;
            double flutter = Math.Pow(0.5 + 0.5 * Math.Sin(2 * Math.PI * ph), 3);
            inh[i] = (float)(bp.Run(Noise()) * flutter * Math.Pow(Math.Sin(Math.PI * t / di), 0.8));
        }
        Mix(o, Normalize(inh, 1), 0, 0.8);
        Mix(o, Voice(di, t => 45, t => Math.Pow(Math.Sin(Math.PI * t / di), 0.8), UH, UR, t => t / di,
            0.6, 0.1, 1.2, 30, 0.8, 3), 0, 0.5);
        double de = 1.4;
        Biquad ex = new Biquad().BP(1100, 0.8);
        Mix(o, FilteredNoise(de, ex, t => Math.Pow(Math.Sin(Math.PI * t / de), 1.5)), 2.0, 0.6);
        return o;
    }

    public static void WriteWav(string path, float[] x)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            int bytes = x.Length * 2;
            w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SR); w.Write(SR * 2);
            w.Write((short)2); w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (float v in x) w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, v)) * 32767));
        }
    }

    public static void RenderAll(string dir)
    {
        Dictionary<string, Func<float[]>> all = new Dictionary<string, Func<float[]>>();
        all["growl"] = Growl; all["roar"] = Roar; all["grunt"] = Grunt; all["snarl"] = Snarl;
        all["chomp"] = Chomp; all["slurp"] = Slurp; all["burp"] = Burp; all["gulp"] = Gulp;
        all["sniff"] = Sniff; all["tummy-rumble"] = Tummy; all["hiss"] = Hiss; all["snore"] = Snore;
        foreach (KeyValuePair<string, Func<float[]>> kv in all)
            WriteWav(Path.Combine(dir, kv.Key + ".wav"), Fade(Normalize(kv.Value(), 0.95), 0.08));
    }
}
