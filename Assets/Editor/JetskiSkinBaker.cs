#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes the market's colour variants from the one jetski and one pirate texture into Assets/Resources/Skins.
public static class JetskiSkinBaker
{
    const string JetskiSource = "Assets/Models/Meshy/Jetski/Jetski_Red_Skull.glb", PirateSource = "Assets/Models/Meshy/Character/Textures/Material_0_baseColor.jpg";
    const int Size = 1024;

    [MenuItem("Jetski Runner/Bake Market Skins")]
    public static void Bake()
    {
        Directory.CreateDirectory("Assets/Resources/Skins");
        Texture jetski = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(JetskiSource)) if (o is Texture t) { jetski = t; break; }
        var pirate = AssetDatabase.LoadAssetAtPath<Texture>(PirateSource);
        var jetskiPixels = Read(jetski); var piratePixels = Read(pirate);
        // red hull to another colour
        Save("jetski-blue", jetskiPixels, c => Red(c) ? Shift(c, .58f, 1f, 1.05f) : c);
        Save("jetski-green", jetskiPixels, c => Red(c) ? Shift(c, .33f, .95f, 1.05f) : c);
        Save("jetski-purple", jetskiPixels, c => Red(c) ? Shift(c, .78f, .9f, 1f) : c);
        Save("jetski-yellow", jetskiPixels, c => Red(c) ? Shift(c, .13f, 1f, 1.25f) : c);
        // bandana (red) and shorts (blue) to the costume's colours
        Save("pirate-captain", piratePixels, c => Red(c) ? Shift(c, .62f, .9f, .7f) : Blue(c) ? Shift(c, .1f, .5f, 1.2f) : c);
        Save("pirate-scout", piratePixels, c => Red(c) ? Shift(c, .09f, .75f, 1f) : Blue(c) ? Shift(c, .1f, .55f, .8f) : c);
        Save("pirate-sailor", piratePixels, c => Red(c) ? Shift(c, .6f, .05f, 1.5f) : Blue(c) ? Shift(c, .61f, 1f, .75f) : c);
        Save("pirate-rogue", piratePixels, c => Red(c) ? Shift(c, .77f, .85f, .75f) : Blue(c) ? Shift(c, .77f, .7f, .55f) : c);
        Save("pirate-diver", piratePixels, c => Red(c) ? Shift(c, .13f, 1f, 1.2f) : Blue(c) ? Shift(c, 0f, 0f, .35f) : c);
        AssetDatabase.Refresh();
    }

    static bool Red(Color c) { Color.RGBToHSV(c, out float h, out float s, out float v); return (h < .035f || h > .94f) && s > .42f && v > .2f; }
    static bool Blue(Color c) { Color.RGBToHSV(c, out float h, out float s, out float v); return h > .53f && h < .7f && s > .3f && v > .15f; }

    static Color Shift(Color c, float hue, float saturation, float value)
    {
        Color.RGBToHSV(c, out _, out float s, out float v);
        return Color.HSVToRGB(hue, Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * value));
    }

    static Color[] Read(Texture source)
    {
        var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, target);
        var was = RenderTexture.active; RenderTexture.active = target;
        var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0); copy.Apply();
        RenderTexture.active = was; RenderTexture.ReleaseTemporary(target);
        var pixels = copy.GetPixels(); Object.DestroyImmediate(copy);
        return pixels;
    }

    static void Save(string name, Color[] source, System.Func<Color, Color> map)
    {
        var pixels = new Color[source.Length];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = map(source[i]);
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels(pixels); texture.Apply();
        File.WriteAllBytes("Assets/Resources/Skins/" + name + ".png", texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
    }
}
#endif
