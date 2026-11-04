using UnityEngine;

// Camera post-processing for the built-in pipeline: HDR bloom, highlight roll-off, per-biome grading,
// vignette and a speed blur. JetskiRunnerGame feeds the values every frame.
[RequireComponent(typeof(Camera))]
public sealed class RunnerPostFX : MonoBehaviour
{
    public float Bloom = .6f, Saturation = 1.1f, Contrast = 1.05f, Vignette = .25f, Radial;
    public Color Tint = Color.white;
    Material material;
    const int Levels = 5;
    readonly RenderTexture[] chain = new RenderTexture[Levels];

    bool edgeOnly;

    public static RunnerPostFX Attach(Camera cam, Shader shader, bool edgeOnly = false)
    {
        if (!shader || !shader.isSupported) return null;
        var fx = cam.gameObject.AddComponent<RunnerPostFX>();
        fx.material = new Material(shader) { hideFlags = HideFlags.DontSave };
        fx.edgeOnly = edgeOnly;
        if (!edgeOnly) cam.allowHDR = true;
        return fx;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (edgeOnly)
        {
            material.SetFloat("_Radial", Radial);
            Graphics.Blit(source, destination, material, 4);
            return;
        }
        int w = source.width / 2, h = source.height / 2;
        var format = source.format;
        material.SetFloat("_Threshold", 1.05f);
        var current = RenderTexture.GetTemporary(w, h, 0, format);
        chain[0] = current;
        Graphics.Blit(source, current, material, 0);
        int used = 1;
        for (; used < Levels; used++)
        {
            w /= 2; h /= 2;
            if (w < 8 || h < 8) break;
            chain[used] = RenderTexture.GetTemporary(w, h, 0, format);
            Graphics.Blit(chain[used - 1], chain[used], material, 1);
        }
        // additive tent-style upsampling back up the chain
        for (int i = used - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], material, 2);
        material.SetTexture("_BloomTex", chain[0]);
        material.SetFloat("_Bloom", Bloom);
        material.SetFloat("_Saturation", Saturation);
        material.SetFloat("_Contrast", Contrast);
        material.SetFloat("_Vignette", Vignette);
        material.SetFloat("_Radial", Radial);
        material.SetVector("_Tint", Tint);
        Graphics.Blit(source, destination, material, 3);
        for (int i = 0; i < used; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
    }
}
