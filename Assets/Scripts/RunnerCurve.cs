using System.Collections.Generic;
using UnityEngine;

// Curved world: sets how much the world bends and moves the game's materials onto the bending shaders.
public static class RunnerCurve
{
    const float Amount = .0007f, End = 170f;
    static readonly int AmountId = Shader.PropertyToID("_CurveAmount"), EndId = Shader.PropertyToID("_CurveEnd");

    static Shader surface, particle;
    static Material text;

    public static Shader Surface => surface ? surface : surface = Resources.Load<Shader>("Shaders/RunnerCurvedStandard");
    static Shader Particle => particle ? particle : particle = Resources.Load<Shader>("Shaders/RunnerCurvedParticle");

    public static void Apply()
    {
        Shader.SetGlobalFloat(AmountId, Amount);
        Shader.SetGlobalFloat(EndId, End);
    }

    public static Material TextMaterial(Font font)
    {
        if (!text)
        {
            var shader = Resources.Load<Shader>("Shaders/RunnerCurvedText");
            if (!shader) return font.material;
            text = new Material(shader) { name = "Curved text", mainTexture = font.material.mainTexture };
        }
        return text;
    }

    // Moves one material onto its bending shader, in place, so everything already sharing it follows.
    public static void Convert(Material m)
    {
        if (!m || !m.shader) return;
        string name = m.shader.name;
        if (name == "Standard") ToSurface(m);
        else if (name.Contains("glTF")) FromGltf(m);
        else if (name.Contains("Particles/Alpha Blended") && Particle) Swap(m, Particle);
    }

    static void ToSurface(Material m)
    {
        if (!Surface) return;
        bool normal = m.IsKeywordEnabled("_NORMALMAP"), emission = m.IsKeywordEnabled("_EMISSION");
        Swap(m, Surface);
        SetKeyword(m, "_NORMALMAP", normal);
        SetKeyword(m, "_EMISSION", emission);
    }

    // glTF materials name their inputs differently; read them before the shader that knows those names is gone.
    static void FromGltf(Material m)
    {
        if (!Surface) return;
        var albedo = Read(m, "baseColorTexture") ?? (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
        var normalMap = Read(m, "normalTexture");
        var emissionMap = Read(m, "emissiveTexture");
        var color = m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : Color.white;
        var glow = m.HasProperty("emissiveFactor") ? m.GetColor("emissiveFactor") : Color.black;
        float metal = m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : 0f;
        float smooth = m.HasProperty("roughnessFactor") ? 1f - m.GetFloat("roughnessFactor") : .5f;

        Swap(m, Surface);
        m.mainTexture = albedo;
        m.color = color;
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Glossiness", smooth);
        if (normalMap) m.SetTexture("_BumpMap", normalMap);
        SetKeyword(m, "_NORMALMAP", normalMap);
        bool glows = glow.maxColorComponent > 0f;
        if (glows) { m.SetColor("_EmissionColor", glow); if (emissionMap) m.SetTexture("_EmissionMap", emissionMap); }
        SetKeyword(m, "_EMISSION", glows);
    }

    static Texture Read(Material m, string property) => m.HasProperty(property) ? m.GetTexture(property) : null;

    static void SetKeyword(Material m, string keyword, bool on)
    {
        if (on) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
    }

    static void Swap(Material m, Shader shader)
    {
#if UNITY_EDITOR
        Remember(m);
#endif
        m.shader = shader;
        m.renderQueue = -1;
        m.enableInstancing = shader == Surface;
    }

#if UNITY_EDITOR
    // Play mode must not leave material assets changed, so everything the conversion touches goes back exactly.
    static readonly List<(Material material, Shader shader, int queue, string[] keywords, bool instancing)> originals = new();

    static void Remember(Material m)
    {
        if (originals.Count == 0) Application.quitting += Restore;
        originals.Add((m, m.shader, m.rawRenderQueue, m.shaderKeywords, m.enableInstancing));
    }

    static void Restore()
    {
        Application.quitting -= Restore;
        foreach (var (material, shader, queue, keywords, instancing) in originals)
            if (material)
            {
                material.shader = shader;
                material.renderQueue = queue;
                material.shaderKeywords = keywords;
                material.enableInstancing = instancing;
            }
        originals.Clear();
    }
#endif
}
