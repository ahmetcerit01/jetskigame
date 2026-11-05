using UnityEngine;

// What the market sells: the jetskis and pirates, what the player owns and has chosen, and dressing a model in them.
public static class RunnerCatalog
{
    public readonly struct Jetski
    {
        public readonly string Name, Skin; public readonly int Price, Speed, Handling, Boost;
        public Jetski(string name, string skin, int price, int speed, int handling, int boost) { Name = name; Skin = skin; Price = price; Speed = speed; Handling = handling; Boost = boost; }
    }

    public readonly struct Pirate
    {
        public readonly string Name, Skin; public readonly int Price, Speed, Handling, Bonus;
        public Pirate(string name, string skin, int price, int speed, int handling, int bonus) { Name = name; Skin = skin; Price = price; Speed = speed; Handling = handling; Bonus = bonus; }
    }

    public static readonly Jetski[] Jetskis =
    {
        new("KORSAN JET SKİ", null, 0, 7, 6, 7),
        new("MAVİ DALGA", "jetski-blue", 7500, 7, 8, 6),
        new("YEŞİL OK", "jetski-green", 10000, 8, 7, 7),
        new("MOR BIÇAK", "jetski-purple", 15000, 8, 8, 8),
        new("ALTIN MARLİN", "jetski-yellow", 25000, 9, 9, 9),
    };

    public static readonly Pirate[] Pirates =
    {
        new("KORSAN ÇOCUK", null, 0, 5, 5, 5),
        new("MAVİ KAPTAN", "pirate-captain", 2000, 6, 5, 6),
        new("KUM İZCİSİ", "pirate-scout", 3000, 5, 7, 6),
        new("DENİZCİ", "pirate-sailor", 4000, 6, 7, 6),
        new("GÖLGE HAYDUT", "pirate-rogue", 6000, 7, 7, 7),
        new("DERİN DALGIÇ", "pirate-diver", 8000, 8, 7, 8),
    };

    public static int SelectedJetski => RunnerSave.GetChoice("jetski");
    public static int SelectedPirate => RunnerSave.GetChoice("pirate");

    // The chosen jetski's handling on the water; the starter is the baseline.
    public static float SpeedFactor => 1f + (Jetskis[SelectedJetski].Speed - Jetskis[0].Speed) * .015f + (Pirates[SelectedPirate].Speed - Pirates[0].Speed) * .005f;
    public static float HandlingFactor => 1f + (Jetskis[SelectedJetski].Handling - Jetskis[0].Handling) * .06f + (Pirates[SelectedPirate].Handling - Pirates[0].Handling) * .02f;
    public static float BonusFactor => 1f + (Pirates[SelectedPirate].Bonus - Pirates[0].Bonus) * .05f;   // score from coins
    public static float BoostFactor => 1f + (Jetskis[SelectedJetski].Boost - Jetskis[0].Boost) * .08f;

    sealed class OriginalSkin : MonoBehaviour { public Texture Texture; }

    static void SetSkin(Renderer renderer, string skin)
    {
        var material = renderer.material;
        string property = material.HasProperty("baseColorTexture") ? "baseColorTexture" : "_MainTex";
        if (!material.HasProperty(property)) return;
        var original = renderer.GetComponent<OriginalSkin>();
        if (!original) { original = renderer.gameObject.AddComponent<OriginalSkin>(); original.Texture = material.GetTexture(property); }
        var texture = skin != null ? Resources.Load<Texture2D>("Skins/" + skin) : null;
        material.SetTexture(property, texture ? texture : original.Texture);
    }

    public static void DressJetski(GameObject root, int id)
    {
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            if (r.name.StartsWith("Jetski_")) SetSkin(r, Jetskis[id].Skin);
    }

    public static void DressPirate(GameObject character, int id)
    {
        foreach (var r in character.GetComponentsInChildren<SkinnedMeshRenderer>(true)) SetSkin(r, Pirates[id].Skin);
    }
}
