using UnityEngine;

// All game audio is synthesised at start-up (no audio files): engine and water loops that follow the
// jetski, an original 6/8 sea-shanty loop, and one-shot effects. Respects the RunnerSave music/sfx toggles.
public sealed class RunnerAudio : MonoBehaviour
{
    const int Rate = 44100;
    AudioSource engine, water, music;
    readonly AudioSource[] pool = new AudioSource[8];
    int next;
    AudioClip coin, splash, bigSplash, whoosh, crash, thud, powerup, shieldBreak, thunder, lava, click, buy, carve;
    static readonly System.Random rng = new(1234);

    public static RunnerAudio Create(GameObject host)
    {
        var a = host.AddComponent<RunnerAudio>();
        a.Build();
        return a;
    }

    void Build()
    {
        engine = Loop("Engine", EngineLoop(), .0f);
        water = Loop("Water", WaterLoop(), .0f);
        music = Loop("Music", Shanty(), .32f);
        for (int i = 0; i < pool.Length; i++) { pool[i] = gameObject.AddComponent<AudioSource>(); pool[i].playOnAwake = false; pool[i].spatialBlend = 0; }
        coin = Clip("coin", .2f, Coin);
        splash = Clip("splash", .7f, t => Splash(t, .7f, 5200f));
        bigSplash = Clip("bigsplash", 1.1f, t => Splash(t, 1.1f, 3800f) * 1.1f);
        carve = Clip("carve", .45f, t => Splash(t, .45f, 7000f) * .6f);
        whoosh = Clip("whoosh", .4f, Whoosh);
        crash = Clip("crash", .9f, Crash);
        thud = Clip("thud", .3f, Thud);
        powerup = Clip("powerup", .45f, Powerup);
        shieldBreak = Clip("shieldbreak", .6f, ShieldBreak);
        thunder = Clip("thunder", 2.6f, Thunder);
        lava = Clip("lava", 1.4f, Lava);
        click = Clip("click", .05f, t => Mathf.Sin(t * 2200f * 6.283f) * Mathf.Exp(-t * 90f) * .6f);
        buy = Clip("buy", .4f, t => (Mathf.Sin(t * (t < .12f ? 1046f : 1568f) * 6.283f)) * Mathf.Exp(-(t < .12f ? t : t - .12f) * 9f) * .5f);
        ApplyToggles();
    }

    public void ApplyToggles()
    {
        music.mute = !RunnerSave.Music;
        bool sfx = RunnerSave.Sfx;
        engine.mute = water.mute = !sfx;
        foreach (var s in pool) s.mute = !sfx;
    }

    // Called every frame: speedProgress 0..1, throttle 0 (idle/menu) .. 1 (running).
    public void SetMotion(float speedProgress, float throttle, bool airborne, bool boost, bool paused)
    {
        float target = paused ? 0f : .16f + .1f * speedProgress;
        engine.volume = Mathf.MoveTowards(engine.volume, target * Mathf.Lerp(.45f, 1f, throttle), Time.unscaledDeltaTime * .8f);
        float pitch = Mathf.Lerp(.55f, .85f + .55f * speedProgress, throttle) + (boost ? .18f : 0f) + (airborne ? .22f : 0f);
        engine.pitch = Mathf.Lerp(engine.pitch, pitch, Time.unscaledDeltaTime * (airborne ? 6f : 3f));
        float hiss = paused || airborne ? 0f : (.05f + .17f * speedProgress) * throttle;
        water.volume = Mathf.MoveTowards(water.volume, hiss, Time.unscaledDeltaTime * 1.5f);
        water.pitch = .85f + .35f * speedProgress;
        music.volume = Mathf.MoveTowards(music.volume, paused ? .12f : .32f, Time.unscaledDeltaTime);
    }

    int coinStreak; float lastCoin;
    public void Coin()
    {
        coinStreak = Time.time - lastCoin < .45f ? Mathf.Min(coinStreak + 1, 12) : 0; lastCoin = Time.time;
        Play(coin, .45f, 1f + coinStreak * .035f);   // a run of coins climbs in pitch
    }
    public void Splash(float size) => Play(size > 1.15f ? bigSplash : splash, Mathf.Clamp(.35f + .3f * size, .3f, .9f), Random.Range(.9f, 1.08f));
    public void Carve() => Play(carve, .35f, Random.Range(.9f, 1.15f));
    public void Jump() => Play(whoosh, .5f, Random.Range(.95f, 1.08f));
    public void Crash() => Play(crash, .9f, 1f);
    public void Stumble() => Play(thud, .8f, Random.Range(.9f, 1.05f));
    public void PowerUp() => Play(powerup, .55f, 1f);
    public void ShieldBreak() => Play(shieldBreak, .7f, 1f);
    public void Thunder() => Play(thunder, .75f, Random.Range(.85f, 1.1f));
    public void Lava() => Play(lava, .7f, Random.Range(.9f, 1.1f));
    public void Click() => Play(click, .5f, 1f);
    public void Buy() => Play(buy, .6f, 1f);

    void Play(AudioClip clip, float volume, float pitch)
    {
        var s = pool[next]; next = (next + 1) % pool.Length;
        s.pitch = pitch; s.PlayOneShot(clip, volume);
    }

    // ---------- synthesis ----------

    AudioSource Loop(string name, AudioClip clip, float volume)
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.clip = clip; s.loop = true; s.volume = volume; s.spatialBlend = 0; s.playOnAwake = false; s.Play();
        return s;
    }

    static AudioClip Clip(string name, float seconds, System.Func<float, float> f)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
        // short fade-out so nothing ends with a click
        for (int i = Mathf.Max(0, n - 200); i < n; i++) data[i] *= (n - i) / 200f;
        var clip = AudioClip.Create(name, n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

    // stateful one-pole low-pass used by the noise effects (reset at t = 0)
    static float lp, lp2;
    static float LowPass(float x, float cutoff, ref float state)
    {
        float a = 1f - Mathf.Exp(-6.283f * cutoff / Rate);
        state += a * (x - state);
        return state;
    }

    static float Coin(float t)
    {
        float f = t < .06f ? 1318.5f : 1760f, local = t < .06f ? t : t - .06f;
        return (Mathf.Sin(t * f * 6.283f) + .3f * Mathf.Sin(t * f * 2f * 6.283f)) * Mathf.Exp(-local * 18f) * .55f;
    }

    static float Splash(float t, float length, float startCutoff)
    {
        if (t == 0) { lp = 0; lp2 = 0; }
        float cutoff = Mathf.Lerp(startCutoff, 500f, t / length);
        float n = LowPass(Noise(), cutoff, ref lp);
        float body = LowPass(Noise(), 300f, ref lp2) * 2.5f;           // low "plunge"
        float env = Mathf.Min(1f, t / .006f) * Mathf.Exp(-t * 5.5f / length);
        float bubbles = Mathf.Sin(t * (900f + 400f * Mathf.Sin(t * 70f)) * 6.283f) * .08f * Mathf.Exp(-t * 6f);
        return (n * .9f + body * Mathf.Exp(-t * 12f) + bubbles) * env;
    }

    static float Whoosh(float t)
    {
        if (t == 0) lp = 0;
        float n = LowPass(Noise(), 600f + 5000f * t / .4f, ref lp);
        return n * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / .4f)) * .9f;
    }

    static float Crash(float t)
    {
        if (t == 0) { lp = 0; lp2 = 0; }
        float boom = Mathf.Sin(6.283f * (110f * t - 40f * t * t)) * Mathf.Exp(-t * 5f);
        float crack = Noise() * Mathf.Exp(-t * 30f) * .8f;
        float wash = LowPass(Noise(), 1800f, ref lp) * Mathf.Exp(-t * 3.5f) * 1.6f;
        return (boom + crack + wash) * .7f;
    }

    static float Thud(float t)
    {
        if (t == 0) lp = 0;
        return (Mathf.Sin(6.283f * (95f * t - 60f * t * t)) * Mathf.Exp(-t * 14f) + LowPass(Noise(), 900f, ref lp) * Mathf.Exp(-t * 20f) * 2f) * .8f;
    }

    static float Powerup(float t)
    {
        float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
        int i = Mathf.Min(3, (int)(t / .07f));
        float local = t - i * .07f;
        float sq = Mathf.Sign(Mathf.Sin(t * notes[i] * 6.283f)) * .3f + Mathf.Sin(t * notes[i] * 6.283f) * .5f;
        return sq * Mathf.Exp(-local * (i == 3 ? 6f : 14f)) * .5f;
    }

    static float ShieldBreak(float t)
    {
        float tone = Mathf.Sin(6.283f * (1400f * t - 900f * t * t)) * Mathf.Exp(-t * 6f) * .4f;
        return tone + Noise() * Mathf.Exp(-t * 12f) * .5f;
    }

    static float Thunder(float t)
    {
        if (t == 0) { lp = 0; lp2 = 0; }
        float crack = Noise() * Mathf.Exp(-t * 18f);
        float rumble = LowPass(LowPass(Noise(), 260f, ref lp), 180f, ref lp2) * 9f;
        float env = Mathf.Min(1f, t / .05f) * Mathf.Exp(-t * 1.3f) * (1f + .4f * Mathf.Sin(t * 9f));
        return (crack * .8f + rumble * env) * .8f;
    }

    static float Lava(float t)
    {
        if (t == 0) { lp = 0; lp2 = 0; }
        float boom = Mathf.Sin(6.283f * (70f * t - 18f * t * t)) * Mathf.Exp(-t * 4f);
        float sizzle = (Noise() - LowPass(Noise(), 3000f, ref lp)) * Mathf.Exp(-t * 2.2f) * .35f * Mathf.Min(1f, t / .08f);
        return (boom * .9f + sizzle) * .8f;
    }

    // Engine: 40 Hz firing pulse with harmonics and a little rough noise; integer cycles so it loops cleanly.
    static AudioClip EngineLoop()
    {
        int n = Rate; var d = new float[n];
        float state = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, s = 0;
            for (int k = 1; k <= 9; k++) s += Mathf.Sin(6.283f * 40f * k * t + k * 1.7f) / (k * (k % 2 == 0 ? 1.6f : 1f));
            float pulse = .75f + .25f * Mathf.Sin(6.283f * 20f * t);
            state += .08f * (Noise() - state);
            d[i] = (s * .28f * pulse + state * .5f) * .8f;
        }
        return Seamless("engine", d);
    }

    static AudioClip WaterLoop()
    {
        int n = Rate * 2; var d = new float[n];
        float a = 0, b = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float x = Noise();
            a += .35f * (x - a); b += .05f * (x - b);
            d[i] = ((a - b) * .9f + b * .6f) * (.85f + .15f * Mathf.Sin(6.283f * 1.5f * t));
        }
        return Seamless("water", d);
    }

    // crossfades the last 0.1 s into the first so a noisy loop has no seam
    static AudioClip Seamless(string name, float[] d)
    {
        int fade = Rate / 10, n = d.Length - fade;
        var o = new float[n];
        for (int i = 0; i < n; i++) o[i] = d[i];
        for (int i = 0; i < fade; i++) { float w = i / (float)fade; o[i] = d[i] * w + d[n + i] * (1f - w); }
        var clip = AudioClip.Create(name, n, 1, Rate, false); clip.SetData(o, 0);
        return clip;
    }

    // Original 8-bar shanty in D minor (6/8), played twice with a harmony line the second time.
    static AudioClip Shanty()
    {
        const float eighth = .19f;
        int[,] melody =
        {
            {74,2},{69,1},{74,2},{77,1}, {76,2},{72,1},{69,3}, {74,2},{77,1},{81,2},{79,1}, {77,2},{76,1},{74,3},
            {72,2},{76,1},{79,2},{76,1}, {77,2},{74,1},{69,3}, {70,2},{74,1},{72,2},{76,1}, {74,6},
        };
        int[] bass = { 50, 45, 48, 43, 50, 45, 45, 50, 48, 43, 50, 45, 46, 53, 45, 45 };
        float bar = eighth * 6, pass = bar * 8, total = pass * 2;
        int n = Mathf.CeilToInt(total * Rate);
        var d = new float[n];

        void Note(float start, float length, float freq, float vol, int voice)
        {
            int s0 = (int)(start * Rate), s1 = Mathf.Min(n, (int)((start + length + .25f) * Rate));
            for (int i = s0; i < s1; i++)
            {
                float t = (i - s0) / (float)Rate;
                float env = Mathf.Min(1f, t / .008f) * (t < length ? Mathf.Exp(-t * (voice == 1 ? 2.2f : 3.2f)) : Mathf.Exp(-length * 3.2f) * Mathf.Exp(-(t - length) * 18f));
                float ph = t * freq;
                float w = voice == 1
                    ? Mathf.Sin(6.283f * ph) * .8f + Mathf.Sin(6.283f * ph * 2f) * .25f            // round bass
                    : (2f * Mathf.Abs(2f * (ph - Mathf.Floor(ph + .5f))) - 1f) * .7f                  // triangle lead
                      + Mathf.Sin(6.283f * ph * 2f) * .12f * Mathf.Exp(-t * 6f);                    // pluck shimmer
                d[i] += w * env * vol;
            }
        }
        float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        for (int p = 0; p < 2; p++)
        {
            float t = p * pass;
            for (int i = 0; i < melody.GetLength(0); i++)
            {
                float len = melody[i, 1] * eighth;
                Note(t, len * .92f, Hz(melody[i, 0]), .22f, 0);
                if (p == 1) Note(t, len * .92f, Hz(melody[i, 0] - (melody[i, 0] % 12 == 2 || melody[i, 0] % 12 == 9 ? 5 : 3)), .1f, 0);
                t += len;
            }
            for (int b = 0; b < 16; b++) Note(p * pass + b * bar * .5f, bar * .45f, Hz(bass[b]), .3f, 1);
            // percussion: soft kick on the downbeat, a brushed "snare" on beat 4 of each bar
            for (int b = 0; b < 8; b++)
            {
                float k = p * pass + b * bar;
                for (int i = (int)(k * Rate), e = Mathf.Min(n, i + Rate / 5); i < e; i++)
                { float tt = (i - k * Rate) / Rate; d[i] += Mathf.Sin(6.283f * (60f * tt - 40f * tt * tt)) * Mathf.Exp(-tt * 18f) * .35f; }
                float sn = k + eighth * 3;
                for (int i = (int)(sn * Rate), e = Mathf.Min(n, i + Rate / 8); i < e; i++)
                { float tt = (i - sn * Rate) / Rate; d[i] += Noise() * Mathf.Exp(-tt * 30f) * .09f; }
            }
        }
        float peak = .001f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
        for (int i = 0; i < n; i++) d[i] = d[i] / peak * .8f;
        var clip = AudioClip.Create("shanty", n, 1, Rate, false); clip.SetData(d, 0);
        return clip;
    }
}
