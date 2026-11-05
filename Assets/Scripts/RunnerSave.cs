using UnityEngine;

// Persistent player progress: best score, coin bank, power-up upgrade levels and sound toggles.
public static class RunnerSave
{
    public enum Upgrade { Magnet, Shield, DoubleCoins, Boost }
    public const int MaxLevel = 5;
    public static readonly string[] UpgradeNames = { "COIN MAGNET", "SHIELD", "DOUBLE COINS", "FUEL BOOST" };
    static readonly int[] Costs = { 150, 400, 800, 1400, 2200 };

    // Batch-mode tests and captures keep their progress in memory so they never touch the player's real save.
    static readonly System.Collections.Generic.Dictionary<string, int> memory = new();
    static int Get(string key, int fallback) => Application.isBatchMode ? (memory.TryGetValue(key, out var v) ? v : fallback) : PlayerPrefs.GetInt(key, fallback);
    static void Set(string key, int value) { if (Application.isBatchMode) memory[key] = value; else PlayerPrefs.SetInt(key, value); }

    public static int Best { get => Get("best", 0); set => Set("best", value); }
    public static int Bank { get => Get("bank", 0); set => Set("bank", Mathf.Max(0, value)); }
    public static bool Music { get => Get("music", 1) == 1; set => Set("music", value ? 1 : 0); }
    public static bool Sfx { get => Get("sfx", 1) == 1; set => Set("sfx", value ? 1 : 0); }

    public static int Level(Upgrade u) => Get("lvl_" + u, 0);
    public static int NextCost(Upgrade u) => Level(u) >= MaxLevel ? -1 : Costs[Level(u)];

    public static bool TryBuy(Upgrade u)
    {
        int cost = NextCost(u);
        if (cost < 0 || Bank < cost) return false;
        Bank -= cost;
        Set("lvl_" + u, Level(u) + 1);
        Save();
        return true;
    }

    // Seconds the power-up lasts at its current level.
    public static float Duration(Upgrade u) => u switch
    {
        Upgrade.Magnet => 7f + 2f * Level(u),
        Upgrade.Shield => 9f + 2f * Level(u),
        Upgrade.DoubleCoins => 10f + 2.5f * Level(u),
        _ => 3.5f + .7f * Level(u),
    };

    // Market: what is owned and chosen. The first item of each kind is free.
    public static bool Owns(string kind, int id) => id == 0 || Get("own_" + kind + id, 0) == 1;
    public static int GetChoice(string kind) => Get("use_" + kind, 0);
    public static void Choose(string kind, int id) { if (!Owns(kind, id)) return; Set("use_" + kind, id); Save(); }
    public static bool TryBuyItem(string kind, int id, int price)
    {
        if (Owns(kind, id) || Bank < price) return false;
        Bank -= price;
        Set("own_" + kind + id, 1);
        Save();
        return true;
    }

    public static void Save() { if (!Application.isBatchMode) PlayerPrefs.Save(); }
}
