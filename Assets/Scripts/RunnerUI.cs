using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// uGUI front end built in code: HUD, main menu, upgrade shop, pause and game-over/continue panels.
// Reads state from JetskiRunnerGame every frame; buttons call back into it.
public sealed class RunnerUI : MonoBehaviour
{
    JetskiRunnerGame game;
    Font font;
    Sprite rounded, circle;
    RectTransform root;
    GameObject hud, menu, shop, pause, over;
    Text score, multiplier, distance, coinCount, banner, chaseWarning, hint, bestMenu, bankMenu, bankShop;
    Text overTitle, overScore, overBest, overCoins, continueLabel, musicLabel, sfxLabel;
    Image bannerBack, continueBar;
    GameObject continueButton;
    readonly List<(GameObject row, Text label, Image fill)> powerRows = new();
    readonly List<(RunnerSave.Upgrade u, Text info, Text cost, Image[] pips, Button buy)> shopRows = new();

    static readonly Color Panel = new(.03f, .08f, .14f, .88f);
    static readonly Color Orange = new(1f, .42f, .12f);
    static readonly Color Teal = new(.05f, .62f, .7f);
    static readonly Color Gold = new(1f, .76f, .18f);

    public static RunnerUI Create(JetskiRunnerGame game)
    {
        var go = new GameObject("Runner UI");
        var ui = go.AddComponent<RunnerUI>();
        ui.game = game;
        ui.Build();
        return ui;
    }

    public bool IsOverUI(int fingerId) => EventSystem.current && EventSystem.current.IsPointerOverGameObject(fingerId);

    // ---------- construction ----------

    void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        rounded = MakeRounded(64, 22); circle = MakeRounded(64, 32);
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();
        root = (RectTransform)transform;
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
        }
        BuildHud(); BuildMenu(); BuildShop(); BuildPause(); BuildOver();
    }

    void BuildHud()
    {
        hud = Group("HUD");
        var pauseBtn = ButtonAt(hud, new Vector2(40, -60), new Vector2(120, 120), Anchor.TopLeft, "", Panel, () => game.TogglePause());
        Box(pauseBtn, new Vector2(-18, 0), new Vector2(18, 54), Color.white); Box(pauseBtn, new Vector2(18, 0), new Vector2(18, 54), Color.white);

        score = Label(hud, "0", 92, new Vector2(0, -50), new Vector2(700, 110), Anchor.Top);
        var multBack = Box(hud, new Vector2(0, -175), new Vector2(150, 64), Gold, Anchor.Top);
        multiplier = Label(multBack, "x1", 44, Vector2.zero, new Vector2(150, 64), Anchor.Center, new Color(.25f, .12f, 0f));
        distance = Label(hud, "0 m", 38, new Vector2(0, -250), new Vector2(500, 50), Anchor.Top, new Color(1, 1, 1, .85f));

        var coinBack = Box(hud, new Vector2(-40, -70), new Vector2(250, 88), Panel, Anchor.TopRight);
        var coinIcon = Box(coinBack, new Vector2(-82, 0), new Vector2(70, 70), Gold); coinIcon.GetComponent<Image>().sprite = circle;
        coinCount = Label(coinBack, "0", 50, new Vector2(30, 0), new Vector2(160, 80), Anchor.Center);
        coinCount.alignment = TextAnchor.MiddleRight;

        for (int i = 0; i < 4; i++)
        {
            var row = Box(hud, new Vector2(40, -230 - i * 78), new Vector2(300, 64), Panel, Anchor.TopLeft);
            var fill = Box(row, new Vector2(0, -26), new Vector2(276, 10), Color.white).GetComponent<Image>();
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.sprite = null;
            var label = Label(row, "", 32, new Vector2(0, 6), new Vector2(280, 44), Anchor.Center);
            powerRows.Add((row, label, fill));
        }

        bannerBack = Box(hud, new Vector2(0, -400), new Vector2(820, 110), new Color(.02f, .06f, .11f, .7f), Anchor.Top).GetComponent<Image>();
        banner = Label(bannerBack.gameObject, "", 62, Vector2.zero, new Vector2(820, 110), Anchor.Center);
        chaseWarning = Label(hud, "RIVAL ON YOUR TAIL!", 50, new Vector2(0, -320), new Vector2(900, 70), Anchor.Top, new Color(1f, .3f, .25f));
        var hintBack = Box(hud, new Vector2(0, 36), new Vector2(940, 120), new Color(.02f, .06f, .11f, .6f), Anchor.Bottom);
        hint = Label(hintBack, "SWIPE  LEFT / RIGHT  STEER\nUP  JUMP  -  DOWN  DUCK", 36, Vector2.zero, new Vector2(940, 120), Anchor.Center, new Color(1, 1, 1, .95f));
    }

    void BuildMenu()
    {
        menu = Group("Main menu");
        Box(menu, Vector2.zero, new Vector2(2400, 3000), new Color(0, .04f, .09f, .35f), Anchor.Center).GetComponent<Image>().sprite = null;
        Label(menu, "JETSKI\nRUNNER", 150, new Vector2(0, -230), new Vector2(1000, 360), Anchor.Top, Color.white).lineSpacing = .85f;
        Label(menu, "PIRATE ESCAPE", 58, new Vector2(0, -600), new Vector2(900, 80), Anchor.Top, Gold);
        var info = Box(menu, new Vector2(0, -760), new Vector2(760, 150), Panel, Anchor.Top);
        bestMenu = Label(info, "BEST 0", 50, new Vector2(0, 30), new Vector2(720, 60), Anchor.Center);
        bankMenu = Label(info, "COINS 0", 42, new Vector2(0, -36), new Vector2(720, 56), Anchor.Center, Gold);

        TextButton(menu, "PLAY", new Vector2(0, 520), new Vector2(620, 170), Anchor.Bottom, Orange, 84, () => { game.BeginRun(); });
        TextButton(menu, "UPGRADES", new Vector2(0, 330), new Vector2(620, 130), Anchor.Bottom, Teal, 56, () => Show(shop));
        musicLabel = TextButton(menu, "MUSIC ON", new Vector2(-200, 170), new Vector2(300, 100), Anchor.Bottom, Panel, 38, () => { RunnerSave.Music = !RunnerSave.Music; RunnerSave.Save(); game.Audio?.ApplyToggles(); });
        sfxLabel = TextButton(menu, "SOUND ON", new Vector2(200, 170), new Vector2(300, 100), Anchor.Bottom, Panel, 38, () => { RunnerSave.Sfx = !RunnerSave.Sfx; RunnerSave.Save(); game.Audio?.ApplyToggles(); });
        Label(menu, "ARROWS / WASD  -  SPACE JUMP  -  S DUCK  -  P PAUSE", 28, new Vector2(0, 70), new Vector2(1000, 50), Anchor.Bottom, new Color(1, 1, 1, .7f));
    }

    void BuildShop()
    {
        shop = Group("Upgrade shop");
        Box(shop, Vector2.zero, new Vector2(2400, 3000), new Color(0, .03f, .07f, .82f), Anchor.Center).GetComponent<Image>().sprite = null;
        Label(shop, "UPGRADES", 96, new Vector2(0, -160), new Vector2(900, 120), Anchor.Top);
        bankShop = Label(shop, "COINS 0", 54, new Vector2(0, -270), new Vector2(900, 70), Anchor.Top, Gold);
        foreach (RunnerSave.Upgrade u in Enum.GetValues(typeof(RunnerSave.Upgrade)))
        {
            int i = (int)u;
            var row = Box(shop, new Vector2(0, -400 - i * 280), new Vector2(940, 250), Panel, Anchor.Top);
            Label(row, RunnerSave.UpgradeNames[i], 50, new Vector2(-150, 70), new Vector2(600, 64), Anchor.Center).alignment = TextAnchor.MiddleLeft;
            var infoText = Label(row, "", 34, new Vector2(-150, 10), new Vector2(600, 50), Anchor.Center, new Color(1, 1, 1, .8f));
            infoText.alignment = TextAnchor.MiddleLeft;
            var pips = new Image[RunnerSave.MaxLevel];
            for (int p = 0; p < pips.Length; p++) pips[p] = Box(row, new Vector2(-420 + p * 70, -70), new Vector2(56, 34), Color.white).GetComponent<Image>();
            var costText = TextButton(row, "", new Vector2(300, -20), new Vector2(270, 130), Anchor.Center, Orange, 44, () =>
            {
                if (RunnerSave.TryBuy(u)) game.Audio?.Buy(); else game.Audio?.Stumble();
            });
            shopRows.Add((u, infoText, costText, pips, costText.GetComponentInParent<Button>(true)));   // (true): the shop is still hidden here
        }
        TextButton(shop, "BACK", new Vector2(0, 140), new Vector2(480, 130), Anchor.Bottom, Teal, 56, () => Show(menu));
    }

    void BuildPause()
    {
        pause = Group("Pause");
        Box(pause, Vector2.zero, new Vector2(2400, 3000), new Color(0, .03f, .07f, .6f), Anchor.Center).GetComponent<Image>().sprite = null;
        Label(pause, "PAUSED", 110, new Vector2(0, 260), new Vector2(900, 140), Anchor.Center);
        TextButton(pause, "RESUME", new Vector2(0, 40), new Vector2(560, 150), Anchor.Center, Orange, 70, () => game.TogglePause());
        TextButton(pause, "MAIN MENU", new Vector2(0, -150), new Vector2(560, 130), Anchor.Center, Teal, 54, () => game.Restart(true));
    }

    void BuildOver()
    {
        over = Group("Game over");
        Box(over, Vector2.zero, new Vector2(2400, 3000), new Color(0, .03f, .07f, .55f), Anchor.Center).GetComponent<Image>().sprite = null;
        overTitle = Label(over, "CRASHED!", 120, new Vector2(0, 560), new Vector2(1000, 150), Anchor.Center);
        var card = Box(over, new Vector2(0, 260), new Vector2(820, 360), Panel, Anchor.Center);
        Label(card, "SCORE", 40, new Vector2(0, 130), new Vector2(700, 50), Anchor.Center, new Color(1, 1, 1, .7f));
        overScore = Label(card, "0", 110, new Vector2(0, 40), new Vector2(780, 130), Anchor.Center);
        overBest = Label(card, "BEST 0", 46, new Vector2(0, -70), new Vector2(780, 60), Anchor.Center, Gold);
        overCoins = Label(card, "+0 COINS", 40, new Vector2(0, -136), new Vector2(780, 56), Anchor.Center);
        continueLabel = TextButton(over, "CONTINUE", new Vector2(0, -70), new Vector2(700, 160), Anchor.Center, new Color(.15f, .7f, .3f), 56, () => game.Revive());
        continueButton = continueLabel.transform.parent.gameObject;
        continueBar = Box(continueButton, new Vector2(0, -62), new Vector2(640, 14), Color.white).GetComponent<Image>();
        continueBar.type = Image.Type.Filled; continueBar.fillMethod = Image.FillMethod.Horizontal; continueBar.sprite = null;
        TextButton(over, "PLAY AGAIN", new Vector2(0, -280), new Vector2(620, 150), Anchor.Center, Orange, 66, () => game.Restart(false));
        TextButton(over, "MENU", new Vector2(0, -450), new Vector2(420, 120), Anchor.Center, Teal, 50, () => game.Restart(true));
    }

    // ---------- per frame ----------

    GameObject shown;
    void Show(GameObject panel) { if (panel == shop || panel == menu) { menu.SetActive(panel == menu); shop.SetActive(panel == shop); } game.Audio?.Click(); }

    void Update()
    {
        if (!game) return;
        var state = game.State;
        bool inMenu = state == JetskiRunnerGame.RunState.Menu;
        if (!inMenu && (menu.activeSelf || shop.activeSelf)) { menu.SetActive(false); shop.SetActive(false); }
        if (inMenu && !menu.activeSelf && !shop.activeSelf) menu.SetActive(true);
        hud.SetActive(!inMenu);
        pause.SetActive(game.Paused && !game.Crashed);
        over.SetActive(game.Crashed && game.OverTime > 1.1f);

        if (inMenu)
        {
            bestMenu.text = "BEST  " + RunnerSave.Best.ToString("N0");
            bankMenu.text = "COINS  " + RunnerSave.Bank.ToString("N0");
            musicLabel.text = RunnerSave.Music ? "MUSIC ON" : "MUSIC OFF";
            sfxLabel.text = RunnerSave.Sfx ? "SOUND ON" : "SOUND OFF";
            if (menu.activeSelf && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) game.BeginRun();
            if (shop.activeSelf) RefreshShop();
            return;
        }

        score.text = game.Score.ToString("N0");
        multiplier.text = "x" + game.Multiplier;
        distance.text = Mathf.FloorToInt(game.DistanceTravelled).ToString("N0") + " m";
        coinCount.text = game.RunCoins.ToString("N0");

        var p = game.ActivePowers;
        for (int i = 0; i < powerRows.Count; i++)
        {
            bool on = i < p.Count;
            powerRows[i].row.SetActive(on);
            if (!on) continue;
            powerRows[i].label.text = p[i].name;
            powerRows[i].fill.fillAmount = p[i].fraction;
            powerRows[i].fill.color = p[i].color;
        }

        var b = game.BannerText;
        bannerBack.gameObject.SetActive(b != null && !game.Crashed);
        if (b != null) { banner.text = b; float a = Mathf.Clamp01(game.BannerTime / .6f) * Mathf.Clamp01((2.8f - game.BannerTime) / .25f); bannerBack.color = new Color(.02f, .06f, .11f, .7f * a); banner.color = new Color(1, 1, 1, a); }
        chaseWarning.gameObject.SetActive(game.Chased && !game.Crashed);
        if (game.Chased) chaseWarning.color = new Color(1f, .3f, .25f, .6f + .4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)));
        hint.transform.parent.gameObject.SetActive(game.DistanceTravelled < 60f && !game.Crashed);

        if (over.activeSelf)
        {
            overTitle.text = game.Caught ? "CAUGHT!" : "CRASHED!";
            overScore.text = game.Score.ToString("N0");
            overBest.text = game.NewBest ? "NEW BEST!" : "BEST  " + RunnerSave.Best.ToString("N0");
            overCoins.text = "+" + game.RunCoins + " COINS   (BANK " + RunnerSave.Bank.ToString("N0") + ")";
            bool canContinue = game.CanRevive && game.OverTime < JetskiRunnerGame.ReviveWindow + 1.1f;
            continueButton.SetActive(canContinue);
            if (canContinue)
            {
                continueLabel.text = "CONTINUE  -  " + game.ReviveCost + " COINS";
                continueBar.fillAmount = 1f - (game.OverTime - 1.1f) / JetskiRunnerGame.ReviveWindow;
            }
        }
    }

    void RefreshShop()
    {
        bankShop.text = "COINS  " + RunnerSave.Bank.ToString("N0");
        foreach (var r in shopRows)
        {
            int lvl = RunnerSave.Level(r.u), cost = RunnerSave.NextCost(r.u);
            float now = RunnerSave.Duration(r.u);
            r.info.text = cost < 0 ? $"{now:0.#} s  (max)" : $"{now:0.#} s  ->  {now + Step(r.u):0.#} s";
            for (int i = 0; i < r.pips.Length; i++) r.pips[i].color = i < lvl ? Gold : new Color(1, 1, 1, .18f);
            r.cost.text = cost < 0 ? "MAX" : cost.ToString("N0");
            r.buy.interactable = cost >= 0 && RunnerSave.Bank >= cost;
        }
    }
    static float Step(RunnerSave.Upgrade u) => u == RunnerSave.Upgrade.DoubleCoins ? 2.5f : u == RunnerSave.Upgrade.Boost ? .7f : 2f;

    // ---------- widgets ----------

    enum Anchor { Center, Top, Bottom, TopLeft, TopRight }

    GameObject Group(string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.SetParent(root, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.SetActive(false);
        return go;
    }

    static RectTransform Place(GameObject go, GameObject parent, Vector2 pos, Vector2 size, Anchor anchor)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.SetParent(parent.transform, false);
        Vector2 a = anchor switch
        {
            Anchor.Top => new Vector2(.5f, 1), Anchor.Bottom => new Vector2(.5f, 0),
            Anchor.TopLeft => new Vector2(0, 1), Anchor.TopRight => new Vector2(1, 1), _ => new Vector2(.5f, .5f),
        };
        rt.anchorMin = rt.anchorMax = a;
        rt.pivot = anchor == Anchor.TopLeft ? new Vector2(0, 1) : anchor == Anchor.TopRight ? new Vector2(1, 1) : anchor == Anchor.Top ? new Vector2(.5f, 1) : anchor == Anchor.Bottom ? new Vector2(.5f, 0) : new Vector2(.5f, .5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    GameObject Box(GameObject parent, Vector2 pos, Vector2 size, Color color, Anchor anchor = Anchor.Center)
    {
        var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
        Place(go, parent, pos, size, anchor);
        var img = go.GetComponent<Image>(); img.sprite = rounded; img.type = Image.Type.Sliced; img.color = color; img.raycastTarget = false;
        return go;
    }

    Text Label(GameObject parent, string text, int size, Vector2 pos, Vector2 box, Anchor anchor, Color? color = null)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        Place(go, parent, pos, box, anchor);
        var t = go.GetComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter;
        t.color = color ?? Color.white; t.text = text; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        var shadow = go.AddComponent<Shadow>(); shadow.effectColor = new Color(0, 0, 0, .55f); shadow.effectDistance = new Vector2(3, -4);
        return t;
    }

    GameObject ButtonAt(GameObject parent, Vector2 pos, Vector2 size, Anchor anchor, string text, Color color, Action onClick)
    {
        var go = Box(parent, pos, size, color, anchor);
        var img = go.GetComponent<Image>(); img.raycastTarget = true;
        var b = go.AddComponent<Button>(); b.targetGraphic = img;
        var colors = b.colors; colors.pressedColor = new Color(.75f, .75f, .75f); colors.disabledColor = new Color(.5f, .5f, .5f, .6f); b.colors = colors;
        b.onClick.AddListener(() => { game.Audio?.Click(); onClick(); });
        return go;
    }

    Text TextButton(GameObject parent, string text, Vector2 pos, Vector2 size, Anchor anchor, Color color, int fontSize, Action onClick)
    {
        var go = ButtonAt(parent, pos, size, anchor, text, color, onClick);
        return Label(go, text, fontSize, Vector2.zero, size, Anchor.Center);
    }

    // white rounded rectangle with a 9-slice border so panels and buttons scale without stretching corners
    static Sprite MakeRounded(int n, int radius)
    {
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float cx = Mathf.Clamp(x + .5f, radius, n - radius), cy = Mathf.Clamp(y + .5f, radius, n - radius);
            float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(cx, cy));
            tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - d + .5f)));
        }
        tex.Apply();
        float b = Mathf.Min(radius, n / 2 - 1);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
    }
}
