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
    Sprite rounded, circle, fadeDown, solid;   // a Filled image ignores its fill amount without a sprite
    bool showShop, showSettings;
    Image curtain, loadingBar;
    RectTransform loadingDot;
    Text loadingTip;
    GameObject muteSlash;
    RectTransform playButton;
    float loadingShown, curtainTarget;
    static bool curtainClosed;       // the last scene left through the curtain, so this one opens it again
    const float LoadingSeconds = 1.5f, FadeSpeed = 6f, CurtainSpeed = 4f, ShadeOverscan = 300f, MaxLoadingStep = .05f, BannerPopFrom = 2.55f, BannerPopTo = 2.8f, BannerPop = 1.25f;
    static readonly string[] Tips =
    {
        "SWIPE LEFT OR RIGHT TO CHANGE LANES", "SWIPE UP TO JUMP OVER LOGS AND BARRELS", "SWIPE DOWN TO DUCK UNDER BANNERS",
        "ONE LANE IS ALWAYS OPEN", "A BOOST SMASHES THROUGH BARRELS", "WATCH THE SHIPS: EACH HAS ITS OWN SPEED",
    };
    RectTransform root;
    GameObject hud, menu, shop, settings, pause, over, loading;
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
        font = Resources.Load<Font>("Fonts/JetwakeBrush");
        rounded = MakeRounded(64, 22); circle = MakeRounded(64, 32); fadeDown = MakeFade(64); solid = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(.5f, .5f));
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
        gameObject.AddComponent<GraphicRaycaster>();
        // Everything hangs off the safe area, so nothing sits under the notch or the home bar; the dim panels are oversized and still cover the screen.
        root = (RectTransform)new GameObject("Safe area", typeof(RectTransform)).transform;
        root.SetParent(transform, false);
        ApplySafeArea();
        if (!FindFirstObjectByType<EventSystem>())
        {
            var es = new GameObject("EventSystem"); es.AddComponent<EventSystem>(); es.AddComponent<StandaloneInputModule>();
        }
        BuildHud(); BuildMenu(); BuildShop(); BuildSettings(); BuildPause(); BuildOver(); BuildLoading(); BuildCurtain();
    }

    Rect appliedSafeArea;
    void ApplySafeArea()
    {
        var area = Screen.safeArea;
        if (area == appliedSafeArea || Screen.width <= 0 || Screen.height <= 0) return;
        appliedSafeArea = area;
        var size = new Vector2(Screen.width, Screen.height);
        root.anchorMin = area.position / size; root.anchorMax = (area.position + area.size) / size;
        root.offsetMin = root.offsetMax = Vector2.zero;
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
            fill.sprite = solid; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
            var label = Label(row, "", 32, new Vector2(0, 6), new Vector2(280, 44), Anchor.Center);
            powerRows.Add((row, label, fill));
        }

        bannerBack = Box(hud, new Vector2(0, -400), new Vector2(820, 110), new Color(.02f, .06f, .11f, .7f), Anchor.Top).GetComponent<Image>();
        banner = Label(bannerBack.gameObject, "", 62, Vector2.zero, new Vector2(820, 110), Anchor.Center);
        chaseWarning = Label(hud, "RIVAL ON YOUR TAIL!", 50, new Vector2(0, -320), new Vector2(900, 70), Anchor.Top, new Color(1f, .3f, .25f));
        var hintBack = Box(hud, new Vector2(0, 36), new Vector2(940, 120), new Color(.02f, .06f, .11f, .6f), Anchor.Bottom);
        hint = Label(hintBack, "SWIPE  LEFT / RIGHT  STEER\nUP  JUMP  -  DOWN  DUCK", 36, Vector2.zero, new Vector2(940, 120), Anchor.Center, new Color(1, 1, 1, .95f));
    }

    // Laid out from the menu design: sizes are its pixels at DesignScale, with the safe area taken off the edges.
    const float DesignScale = 1.15f, SkySolid = 250f, SkyFade = 420f;

    void BuildMenu()
    {
        menu = Group("Main menu");
        Sky(menu);
        var coins = ArtPill(menu, "UI/pill-coins", new Vector2(-24, -4), 262, 87, Anchor.TopRight, 96, 24);
        bankMenu = Label(coins, "0", 46, new Vector2(30, 0), new Vector2(190, 80), Anchor.Center, Cream);
        bankMenu.alignment = TextAnchor.MiddleRight;

        var sound = ArtButton(menu, "UI/sound", new Vector2(20, -4), Anchor.TopLeft, ToggleSound);
        muteSlash = Box(sound, Vector2.zero, new Vector2(88, 9), new Color(.95f, .2f, .15f));
        muteSlash.GetComponent<Image>().sprite = null; muteSlash.transform.localRotation = Quaternion.Euler(0, 0, 45f);

        Title(menu, new Vector2(0, -106), Anchor.Top);
        Art(menu, "UI/pirate-escape", new Vector2(0, -271), Anchor.Top);

        var record = ArtPill(menu, "UI/pill-record", new Vector2(0, -345), 446, 89, Anchor.Top, 54, 54);
        Art(record, "UI/rekor", new Vector2(-105, 0), Anchor.Center);
        bestMenu = Label(record, "0", 46, new Vector2(112, 0), new Vector2(220, 70), Anchor.Center, Cream);

        playButton = (RectTransform)ArtButton(menu, "UI/oyna", new Vector2(0, 228), Anchor.Bottom, () => { game.Audio?.Click(); game.BeginRun(); }).transform;
        ArtButton(menu, "UI/gelistir", new Vector2(-205, 66), Anchor.Bottom, () => Show(shop));
        ArtButton(menu, "UI/ayarlar", new Vector2(205, 66), Anchor.Bottom, () => Show(settings));
    }

    void ToggleSound()
    {
        bool on = !(RunnerSave.Music || RunnerSave.Sfx);
        RunnerSave.Music = RunnerSave.Sfx = on; RunnerSave.Save();
        game.Audio?.Click();
    }

    // Deep blue from the top of the screen fading into the scene, as in the design.
    void Sky(GameObject parent)
    {
        var deep = new Color(.03f, .16f, .34f);
        var solid = Box(parent, new Vector2(0, ShadeOverscan), new Vector2(2400, ShadeOverscan + SkySolid), deep, Anchor.Top);
        solid.GetComponent<Image>().sprite = null; solid.GetComponent<Image>().raycastTarget = false;
        var fade = Box(parent, new Vector2(0, -SkySolid), new Vector2(2400, SkyFade), deep, Anchor.Top);
        var image = fade.GetComponent<Image>(); image.sprite = fadeDown; image.type = Image.Type.Simple; image.raycastTarget = false;
    }

    static Sprite LoadSprite(string path, Vector4 border = default)
    {
        var tex = Resources.Load<Texture2D>(path);
        return tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect, border) : null;
    }

    // Sprites are twice the size of the design, so half a pixel of art is one design pixel.
    GameObject Art(GameObject parent, string path, Vector2 pos, Anchor anchor)
    {
        var sprite = LoadSprite(path);
        var size = sprite ? new Vector2(sprite.rect.width, sprite.rect.height) * .5f * DesignScale : new Vector2(200, 80);
        var box = Box(parent, pos, size, Color.white, anchor);
        var image = box.GetComponent<Image>(); image.sprite = sprite; image.type = Image.Type.Simple; image.raycastTarget = false;
        return box;
    }

    GameObject ArtButton(GameObject parent, string path, Vector2 pos, Anchor anchor, Action onClick)
    {
        var sprite = LoadSprite(path);
        var size = sprite ? new Vector2(sprite.rect.width, sprite.rect.height) * .5f * DesignScale : new Vector2(300, 120);
        var go = ButtonAt(parent, pos, size, anchor, "", Color.white, onClick);
        var image = go.GetComponent<Image>(); image.sprite = sprite; image.type = Image.Type.Simple; image.color = Color.white;
        return go;
    }

    GameObject ArtPill(GameObject parent, string path, Vector2 pos, float width, float height, Anchor anchor, float left, float right)
    {
        var box = Box(parent, pos, new Vector2(width, height) * DesignScale, Color.white, anchor);
        var image = box.GetComponent<Image>();
        image.sprite = LoadSprite(path, new Vector4(left * 2f, 0, right * 2f, 0)); image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 2f / DesignScale; image.raycastTarget = false;
        return box;
    }

    static readonly Color Cream = new(1f, .965f, .88f);

    // The game's title artwork, or plain text if the image is missing.
    const string TitleImage = "UI/jetski-runner-title";
    void Title(GameObject parent, Vector2 pos, Anchor anchor)
    {
        if (!Resources.Load<Texture2D>(TitleImage)) { Label(parent, "JETSKI RUNNER", 110, pos, new Vector2(1000, 200), anchor); return; }
        var shadow = Art(parent, TitleImage, pos, anchor).AddComponent<Shadow>();
        shadow.effectColor = new Color(0, .03f, .07f, .7f); shadow.effectDistance = new Vector2(3f, -4f);
    }

    void BuildSettings()
    {
        settings = Group("Settings");
        Box(settings, Vector2.zero, new Vector2(2400, 3000), new Color(0, .03f, .07f, .72f), Anchor.Center).GetComponent<Image>().sprite = null;
        var card = Box(settings, new Vector2(0, 60), new Vector2(820, 620), Panel, Anchor.Center);
        Label(card, "SETTINGS", 84, new Vector2(0, 220), new Vector2(780, 110), Anchor.Center);
        musicLabel = TextButton(card, "MUSIC ON", new Vector2(0, 60), new Vector2(620, 130), Anchor.Center, Teal, 52, () => { RunnerSave.Music = !RunnerSave.Music; RunnerSave.Save(); game.Audio?.Click(); });
        sfxLabel = TextButton(card, "SOUND ON", new Vector2(0, -100), new Vector2(620, 130), Anchor.Center, Teal, 52, () => { RunnerSave.Sfx = !RunnerSave.Sfx; RunnerSave.Save(); game.Audio?.Click(); });
        TextButton(settings, "BACK", new Vector2(0, -400), new Vector2(480, 130), Anchor.Center, Orange, 56, () => Show(menu));
    }

    // Shown while the first frame prepares every model; on a restart nothing needs preparing and it never appears.
    void BuildLoading()
    {
        loading = new GameObject("Loading screen", typeof(RectTransform));
        var rt = (RectTransform)loading.transform; rt.SetParent(transform, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        loading.AddComponent<CanvasGroup>();
        Box(loading, Vector2.zero, new Vector2(2400, 3400), new Color(.02f, .1f, .17f), Anchor.Center).GetComponent<Image>().sprite = null;
        Title(loading, new Vector2(0, 170), Anchor.Center);
        Label(loading, "PIRATE ESCAPE", 50, new Vector2(0, -20), new Vector2(800, 70), Anchor.Center, Gold);
        var track = Box(loading, new Vector2(0, -220), new Vector2(720, 26), new Color(1, 1, 1, .16f), Anchor.Center);
        loadingBar = Box(track, Vector2.zero, new Vector2(720, 26), Gold).GetComponent<Image>();
        loadingBar.sprite = solid; loadingBar.type = Image.Type.Filled; loadingBar.fillMethod = Image.FillMethod.Horizontal; loadingBar.fillAmount = 0;
        var dot = Box(track, new Vector2(-360, 0), new Vector2(58, 58), Gold); dot.GetComponent<Image>().sprite = circle;
        loadingDot = dot.GetComponent<RectTransform>();
        loadingTip = Label(loading, Tips[UnityEngine.Random.Range(0, Tips.Length)], 34, new Vector2(0, -320), new Vector2(940, 110), Anchor.Center, new Color(1, 1, 1, .8f));
        loading.SetActive(!JetskiRunnerGame.Ready);
    }

    void BuildCurtain()
    {
        var go = Box(gameObject, Vector2.zero, new Vector2(2400, 3400), Color.black, Anchor.Center);
        curtain = go.GetComponent<Image>(); curtain.sprite = null; curtain.raycastTarget = false;
        SetCurtain(curtainClosed ? 1f : 0f);
        curtainClosed = false;
    }

    void SetCurtain(float a) { var c = curtain.color; c.a = a; curtain.color = c; curtain.enabled = a > 0f; }

    // Closes the curtain before something abrupt such as a scene reload; the next scene opens it again.
    void AfterCurtain(Action then) { game.Audio?.Click(); StartCoroutine(CloseCurtain(then)); }
    System.Collections.IEnumerator CloseCurtain(Action then)
    {
        curtainTarget = 1f;
        while (curtain.color.a < 1f) yield return null;
        curtainClosed = true;
        then();
    }

    // Fades a whole panel, which stays active until it is fully gone.
    static void Present(GameObject panel, bool on)
    {
        var g = panel.GetComponent<CanvasGroup>();
        if (on && !panel.activeSelf) { panel.SetActive(true); g.alpha = 0f; }
        if (!panel.activeSelf) return;
        g.alpha = Mathf.MoveTowards(g.alpha, on ? 1f : 0f, Time.unscaledDeltaTime * FadeSpeed);
        g.interactable = g.blocksRaycasts = on;
        panel.transform.localScale = Vector3.one * Mathf.Lerp(.97f, 1f, g.alpha);
        if (!on && g.alpha <= 0f) panel.SetActive(false);
    }

    void UpdateTransitions()
    {
        SetCurtain(Mathf.MoveTowards(curtain.color.a, curtainTarget, Time.unscaledDeltaTime * CurtainSpeed));
        if (!loading.activeSelf) return;
        loadingShown += Mathf.Min(Time.unscaledDeltaTime, MaxLoadingStep);   // the long first frame must not use up the screen
        float fill = Mathf.Clamp01(loadingShown / LoadingSeconds);
        if (!JetskiRunnerGame.Ready) fill = Mathf.Min(fill, .8f);
        loadingBar.fillAmount = fill;
        loadingDot.anchoredPosition = new Vector2(Mathf.Lerp(-360f, 360f, fill), 0f);
        Present(loading, fill < 1f);
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
        TextButton(pause, "MAIN MENU", new Vector2(0, -150), new Vector2(560, 130), Anchor.Center, Teal, 54, () => AfterCurtain(() => game.Restart(true)));
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
        continueBar.sprite = solid; continueBar.type = Image.Type.Filled; continueBar.fillMethod = Image.FillMethod.Horizontal;
        TextButton(over, "PLAY AGAIN", new Vector2(0, -280), new Vector2(620, 150), Anchor.Center, Orange, 66, () => AfterCurtain(() => game.Restart(false)));
        TextButton(over, "MENU", new Vector2(0, -450), new Vector2(420, 120), Anchor.Center, Teal, 50, () => AfterCurtain(() => game.Restart(true)));
    }

    // ---------- per frame ----------

    GameObject shown;
    void Show(GameObject panel) { showShop = panel == shop; showSettings = panel == settings; game.Audio?.Click(); }

    void Update()
    {
        if (!game) return;
        ApplySafeArea();
        var state = game.State;
        bool inMenu = state == JetskiRunnerGame.RunState.Menu;
        game.Audio?.SetMenu(inMenu);
        UpdateTransitions();
        Present(menu, inMenu && !showShop && !showSettings);
        Present(shop, inMenu && showShop);
        Present(settings, inMenu && showSettings);
        Present(hud, !inMenu);
        Present(pause, game.Paused && !game.Crashed);
        Present(over, game.Crashed && game.OverTime > 1.1f);

        if (inMenu)
        {
            bestMenu.text = RunnerSave.Best.ToString("N0");
            muteSlash.SetActive(!RunnerSave.Music && !RunnerSave.Sfx);
            bankMenu.text = RunnerSave.Bank.ToString("N0");
            playButton.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 3.2f) * .025f);
            musicLabel.text = RunnerSave.Music ? "MUSIC ON" : "MUSIC OFF";
            sfxLabel.text = RunnerSave.Sfx ? "SOUND ON" : "SOUND OFF";
            if (!loading.activeSelf && !showShop && !showSettings && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) game.BeginRun();
            if (shop.activeSelf) RefreshShop();
            return;
        }

        bannerBack.transform.localScale = Vector3.one * Mathf.Lerp(1f, BannerPop, Mathf.InverseLerp(BannerPopFrom, BannerPopTo, game.BannerTime));
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
        go.AddComponent<CanvasGroup>();
        go.SetActive(false);
        return go;
    }

    // A dark band fading out from the top or bottom edge, so text reads while the scene stays visible.
    void Shade(GameObject parent, bool top, float height, float strength)
    {
        // reaches past the safe area, or its edge would show as a hard line at the notch and the home bar
        var box = Box(parent, new Vector2(0, top ? ShadeOverscan : -ShadeOverscan), new Vector2(2400, height + ShadeOverscan), new Color(0, .04f, .09f, strength), top ? Anchor.Top : Anchor.Bottom);
        var image = box.GetComponent<Image>(); image.sprite = fadeDown; image.type = Image.Type.Simple; image.raycastTarget = false;
        if (!top) box.transform.localScale = new Vector3(1, -1, 1);
    }

    static Sprite MakeFade(int n)
    {
        var tex = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++) tex.SetPixel(0, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, y / (n - 1f))));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, n), new Vector2(.5f, .5f));
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
        t.font = font; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter;
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
