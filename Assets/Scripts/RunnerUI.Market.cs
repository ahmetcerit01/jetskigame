using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The market: pick a pirate or jetski on a turning 3D preview with the real sea behind it, buy with coins, choose what to play with.
public sealed partial class RunnerUI
{
    static readonly Color MarketWood = new(.45f, .24f, .11f), MarketWoodDark = new(.27f, .13f, .06f), MarketWoodLight = new(.72f, .43f, .19f), MarketRed = new(.72f, .1f, .1f);
    static readonly Color MarketBlue = new(.07f, .36f, .72f), MarketBlueDeep = new(.04f, .2f, .48f), MarketGreen = new(.2f, .72f, .18f), MarketGreenDark = new(.06f, .4f, .08f), MarketCellOff = new(.7f, .78f, .85f);
    const int StatCells = 10;
    const float CardWidth = 138f, CardGap = 8f;

    GameObject market;
    Text marketTitle, marketBank, marketName, marketAction;
    Image marketActionBack, marketActionEdge, jetskiTab, pirateTab;
    bool showMarket, marketJetskis = true, marketReady;
    int marketShown;
    readonly Image[][] statBars = new Image[3][];
    readonly Text[] statLabels = new Text[3];
    readonly List<(GameObject root, GameObject check, Text price, RawImage icon, Image edge)> marketCards = new();

    Transform stage, stageJetski, stagePirate, stageWater, stageShadow;
    readonly List<(Transform ring, Material material)> stageRipples = new();
    RectTransform backdropRect;
    float stageJetskiWidth, stageJetskiHeight, stageJetskiY;
    Camera stageCamera;
    RenderTexture stageView;
    RawImage stageImage;
    RenderTexture[] jetskiIcons, pirateIcons;

    GameObject Framed(GameObject parent, Vector2 pos, Vector2 size, Color edge, Color fill, Anchor anchor)
    {
        var outer = Box(parent, pos, size, edge, anchor);
        Box(outer, Vector2.zero, size - new Vector2(16, 16), fill);
        return outer;
    }

    void BuildMarket()
    {
        market = Group("Market");

        // painted backdrop, placed so its round platform sits above the name plank; plain deck continues below it
        var deck = Box(market, Vector2.zero, new Vector2(2400, 700), new Color(.72f, .4f, .18f), Anchor.Bottom);
        deck.GetComponent<Image>().sprite = null;
        var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage));
        backdropRect = (RectTransform)backdrop.transform;
        backdropRect.SetParent(market.transform, false);
        backdropRect.anchorMin = backdropRect.anchorMax = backdropRect.pivot = new Vector2(.5f, 1f);
        backdropRect.anchoredPosition = new Vector2(0, 60); backdropRect.sizeDelta = new Vector2(1180, 1564);
        var backdropImage = backdrop.GetComponent<RawImage>();
        backdropImage.texture = Resources.Load<Texture2D>("UI/market-bg"); backdropImage.raycastTarget = false;

        var back = ButtonAt(market, new Vector2(30, -24), new Vector2(104, 104), Anchor.TopLeft, "<", MarketBlue, () => Show(menu));
        Label(back, "<", 66, Vector2.zero, new Vector2(100, 100), Anchor.Center);
        var coins = ArtPill(market, "UI/pill-coins", new Vector2(-24, -24), 240, 80, Anchor.TopRight, 96, 24);
        marketBank = Label(coins, "0", 46, new Vector2(30, 0), new Vector2(190, 80), Anchor.Center, Cream);
        marketBank.alignment = TextAnchor.MiddleRight;

        // hanging wooden sign
        foreach (float x in new[] { -220f, 220f }) Box(market, new Vector2(x, -128), new Vector2(18, 44), MarketWoodDark, Anchor.Top);
        var sign = Framed(market, new Vector2(0, -160), new Vector2(660, 124), MarketWoodDark, MarketWoodLight, Anchor.Top);
        marketTitle = Label(sign, "", 64, Vector2.zero, new Vector2(630, 110), Anchor.Center, Cream);

        var view = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
        var rt = (RectTransform)view.transform;
        rt.SetParent(market.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -290); rt.sizeDelta = new Vector2(860, 860);
        stageImage = view.GetComponent<RawImage>();
        // the model only turns when the player drags it
        var drag = view.AddComponent<EventTrigger>();
        var dragEntry = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        dragEntry.callback.AddListener(e => TurnStage(((PointerEventData)e).delta.x));
        drag.triggers.Add(dragEntry);
        foreach (int step in new[] { -1, 1 })
        {
            var arrow = ButtonAt(market, new Vector2(step * 375, -640), new Vector2(96, 150), Anchor.Top, step < 0 ? "<" : ">", Gold, () => StepMarket(step));
            Label(arrow, step < 0 ? "<" : ">", 72, Vector2.zero, new Vector2(90, 140), Anchor.Center, MarketWoodDark);
        }

        var plank = Framed(market, new Vector2(0, -1120), new Vector2(620, 104), MarketWoodDark, MarketWoodLight, Anchor.Top);
        marketName = Label(plank, "", 50, Vector2.zero, new Vector2(590, 90), Anchor.Center, Cream);

        var panel = Framed(market, new Vector2(0, -1236), new Vector2(780, 250), new Color(.6f, .82f, .98f), MarketBlue, Anchor.Top);
        for (int s = 0; s < 3; s++)
        {
            float y = 68 - s * 68;
            statLabels[s] = Label(panel, "", 38, new Vector2(-235, y), new Vector2(250, 60), Anchor.Center, Cream);
            statLabels[s].alignment = TextAnchor.MiddleLeft;
            statBars[s] = new Image[StatCells];
            for (int i = 0; i < StatCells; i++) statBars[s][i] = Box(panel, new Vector2(-70 + i * 44, y), new Vector2(38, 38), Color.white).GetComponent<Image>();
        }

        var action = ButtonAt(market, new Vector2(0, 350), new Vector2(580, 128), Anchor.Bottom, "", MarketGreenDark, MarketAction);
        marketActionEdge = action.GetComponent<Image>();
        marketActionBack = Box(action, new Vector2(0, 5), new Vector2(556, 104), MarketGreen).GetComponent<Image>();
        marketActionBack.raycastTarget = false;
        marketAction = Label(action, "", 56, new Vector2(0, 5), new Vector2(540, 100), Anchor.Center, Color.white);

        int cards = Mathf.Max(RunnerCatalog.Jetskis.Length, RunnerCatalog.Pirates.Length);
        for (int i = 0; i < cards; i++)
        {
            int index = i;
            var card = ButtonAt(market, new Vector2((i - (cards - 1) * .5f) * (CardWidth + CardGap), 160), new Vector2(CardWidth, 168), Anchor.Bottom, "", MarketWoodDark, () => { marketShown = index; game.Audio?.Click(); RefreshMarket(); });
            Box(card, Vector2.zero, new Vector2(CardWidth - 14, 154), MarketBlueDeep).GetComponent<Image>().raycastTarget = false;
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(RawImage));
            var iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(card.transform, false);
            iconRect.anchoredPosition = new Vector2(0, 12); iconRect.sizeDelta = new Vector2(124, 124);
            var icon = iconObject.GetComponent<RawImage>(); icon.raycastTarget = false;
            var price = Label(card, "", 24, new Vector2(0, -60), new Vector2(CardWidth - 8, 34), Anchor.Center, Gold);
            var check = Box(card, new Vector2(52, 66), new Vector2(46, 46), MarketGreen);
            Label(check, "OK", 20, Vector2.zero, new Vector2(46, 46), Anchor.Center);
            check.GetComponent<Image>().raycastTarget = false;
            marketCards.Add((card, check, price, icon, card.GetComponent<Image>()));
        }

        var charactersTab = ButtonAt(market, new Vector2(-215, 30), new Vector2(410, 112), Anchor.Bottom, "", MarketBlue, () => SetMarketTab(false));
        pirateTab = charactersTab.GetComponent<Image>();
        Label(charactersTab, "KARAKTERLER", 40, Vector2.zero, new Vector2(400, 104), Anchor.Center);
        var jetskisTab = ButtonAt(market, new Vector2(215, 30), new Vector2(410, 112), Anchor.Bottom, "", Gold, () => SetMarketTab(true));
        jetskiTab = jetskisTab.GetComponent<Image>();
        Label(jetskisTab, "JET SKI'LER", 40, Vector2.zero, new Vector2(400, 104), Anchor.Center);
    }

    void OpenMarket()
    {
        if (!marketReady) BuildStage();
        stage.gameObject.SetActive(true);
        marketShown = marketJetskis ? RunnerCatalog.SelectedJetski : RunnerCatalog.SelectedPirate;
        RefreshMarket();
        Show(market);
    }

    void BuildStage()
    {
        marketReady = true;
        stage = new GameObject("Market stage").transform;
        stage.position = new Vector3(0, -300f, 0);
        var jetski = Instantiate(game.RiderPrefab, stage);
        jetski.transform.localPosition = Vector3.zero;
        var seated = jetski.GetComponentInChildren<PirateRider>();
        foreach (var r in seated.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        seated.gameObject.SetActive(false);
        stageJetski = jetski.transform;
        var whole = Instantiate(game.RiderPrefab);
        var character = whole.GetComponentInChildren<PirateRider>();
        var pirateObject = character.gameObject;
        pirateObject.transform.SetParent(stage, false);
        pirateObject.transform.localPosition = Vector3.zero; pirateObject.transform.localRotation = Quaternion.Euler(0, 180f, 0);
        Destroy(character); Destroy(whole);
        var animator = pirateObject.GetComponent<Animator>();
        animator.applyRootMotion = false; animator.Play("MenuIdle", 0, 0f); animator.Update(.05f);
        stagePirate = pirateObject.transform;

        BuildStageWater();
        var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        shadow.name = "Pirate shadow"; shadow.transform.SetParent(stage, false);
        shadow.transform.rotation = Quaternion.Euler(90f, 0, 0);
        shadow.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = RadialTexture(r => Mathf.SmoothStep(.5f, 0f, r)), color = new Color(.12f, .05f, 0f, 1f) };
        stageShadow = shadow.transform;
        stageShadow.gameObject.SetActive(false);

        foreach (var c in stage.GetComponentsInChildren<Collider>(true)) Destroy(c);

        var cameraObject = new GameObject("Market camera");
        cameraObject.transform.SetParent(stage, false);
        stageCamera = cameraObject.AddComponent<Camera>();
        stageCamera.clearFlags = CameraClearFlags.SolidColor; stageCamera.backgroundColor = new Color(0, 0, 0, 0);
        stageCamera.fieldOfView = 30f; stageCamera.nearClipPlane = .1f; stageCamera.farClipPlane = 60f;
        stageCamera.enabled = false;
        stageView = new RenderTexture(860, 860, 16, RenderTextureFormat.ARGB32);
        stageImage.texture = stageView;

        jetskiIcons = new RenderTexture[RunnerCatalog.Jetskis.Length];
        for (int i = 0; i < jetskiIcons.Length; i++) { ShowOnStage(true, i, true); jetskiIcons[i] = RenderIcon(); }
        pirateIcons = new RenderTexture[RunnerCatalog.Pirates.Length];
        for (int i = 0; i < pirateIcons.Length; i++) { ShowOnStage(false, i, true); pirateIcons[i] = RenderIcon(); }
        stageJetski.rotation = Quaternion.Euler(0, 150f, 0);   // starts three-quarters on, like the design
    }

    RenderTexture RenderIcon()
    {
        var icon = new RenderTexture(220, 220, 16, RenderTextureFormat.ARGB32);
        stageCamera.targetTexture = icon; stageCamera.Render();
        return icon;
    }

    void ShowOnStage(bool jetskis, int id, bool icon = false)
    {
        stageJetski.gameObject.SetActive(jetskis);
        stagePirate.gameObject.SetActive(!jetskis);
        if (jetskis) RunnerCatalog.DressJetski(stageJetski.gameObject, id);
        else RunnerCatalog.DressPirate(stagePirate.gameObject, id);
        var bounds = new Bounds(); bool any = false;
        foreach (var r in (jetskis ? stageJetski : stagePirate).GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        // measured once: the box around a turned model changes size and the camera must not zoom with it
        if (jetskis && stageJetskiWidth <= 0f) { stageJetskiWidth = Mathf.Max(bounds.size.x, bounds.size.z); stageJetskiHeight = bounds.size.y; stageJetskiY = bounds.min.y; }
        float width = jetskis ? stageJetskiWidth : bounds.size.y;
        float bottom = jetskis ? stageJetskiY : PirateFeetY(bounds.min.y);
        var pivot = (jetskis ? stageJetski : stagePirate).position;
        // in the big view the camera is aimed so the jetski floats on the backdrop's water and the pirate stands on its platform
        var target = icon ? bounds.center : new Vector3(pivot.x, bottom + width * (jetskis ? .34f : .3f), pivot.z);
        float distance = width * (icon ? 2.4f : jetskis ? 2f : 2.5f);
        stageCamera.transform.position = target + new Vector3(0, .2f, -1f).normalized * distance;
        stageCamera.transform.rotation = Quaternion.LookRotation(target - stageCamera.transform.position);
        stageWater.gameObject.SetActive(jetskis && !icon);
        stageShadow.gameObject.SetActive(!jetskis && !icon);
        if (!jetskis)
        {
            stageShadow.position = new Vector3(pivot.x, bottom + width * .005f, pivot.z);
            stageShadow.localScale = new Vector3(width * .95f, width * .6f, 1f);
        }
        if (jetskis)
        {
            // a third of the hull sits below the surface
            stageWater.position = new Vector3(pivot.x, bottom + stageJetskiHeight * .3f, pivot.z);
            stageWater.localScale = new Vector3(width, 1f, width);
        }
    }

    // The mesh's box reaches below the soles, so stand on the foot bones instead.
    float PirateFeetY(float fallback)
    {
        var animator = stagePirate.GetComponent<Animator>();
        if (!animator || !animator.isHuman) return fallback;
        var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot); var right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        return left && right ? Mathf.Min(left.position.y, right.position.y) - .06f : fallback;
    }

    public void TurnStage(float dragPixels)
    {
        // a drag across the whole screen turns it once round; dragging right turns the near side right
        float degrees = -dragPixels / Mathf.Max(1, Screen.width) * 360f;
        (marketJetskis ? stageJetski : stagePirate).Rotate(0, degrees, 0, Space.World);
    }

    // The water around a floating jetski: a sheet that tints what is under the surface, a foam collar and rings spreading out.
    void BuildStageWater()
    {
        stageWater = new GameObject("Stage water").transform;
        stageWater.SetParent(stage, false);
        WaterSheet("Surface", RadialTexture(r => Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(.45f, 1f, r)) * .72f), new Color(.1f, .62f, .88f), 1.7f);
        WaterSheet("Foam", RadialTexture(r => Mathf.Exp(-Mathf.Pow((r - .62f) / .1f, 2f))), new Color(1f, 1f, 1f, .9f), 1.05f);
        var ring = RadialTexture(r => Mathf.Exp(-Mathf.Pow((r - .88f) / .05f, 2f)));
        for (int i = 0; i < 4; i++)
        {
            var sheet = WaterSheet("Ripple", ring, Color.white, 1f);
            stageRipples.Add((sheet, sheet.GetComponent<Renderer>().sharedMaterial));
        }
        stageWater.gameObject.SetActive(false);
    }

    Transform WaterSheet(string name, Texture2D texture, Color color, float size)
    {
        var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
        sheet.name = name;
        sheet.transform.SetParent(stageWater, false);
        sheet.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        sheet.transform.localScale = new Vector3(size, size * .62f, 1f);   // longer along the hull
        sheet.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture, color = color };
        return sheet.transform;
    }

    static Texture2D RadialTexture(System.Func<float, float> alpha)
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2((x + .5f) / size * 2f - 1f, (y + .5f) / size * 2f - 1f).magnitude;
                texture.SetPixel(x, y, new Color(1, 1, 1, r >= 1f ? 0f : alpha(r)));
            }
        texture.Apply();
        return texture;
    }

    void UpdateStageWater()
    {
        // long and wide along the hull, so the sheets turn with the jetski
        stageWater.rotation = Quaternion.Euler(0, stageJetski.eulerAngles.y + 90f, 0);
        for (int i = 0; i < stageRipples.Count; i++)
        {
            float t = (Time.unscaledTime * .3f + i / (float)stageRipples.Count) % 1f;
            var (ring, material) = stageRipples[i];
            float size = Mathf.Lerp(1.05f, 1.9f, t);
            ring.localScale = new Vector3(size, size * .62f, 1f);
            material.color = new Color(1f, 1f, 1f, Mathf.Min(t * 6f, 1f) * (1f - t) * .75f);
        }
    }

    void SetMarketTab(bool jetskis)
    {
        marketJetskis = jetskis;
        marketShown = jetskis ? RunnerCatalog.SelectedJetski : RunnerCatalog.SelectedPirate;
        game.Audio?.Click();
        RefreshMarket();
    }

    void StepMarket(int step)
    {
        int count = marketJetskis ? RunnerCatalog.Jetskis.Length : RunnerCatalog.Pirates.Length;
        marketShown = (marketShown + step + count) % count;
        game.Audio?.Click();
        RefreshMarket();
    }

    string MarketKind => marketJetskis ? "jetski" : "pirate";
    int ShownPrice => marketJetskis ? RunnerCatalog.Jetskis[marketShown].Price : RunnerCatalog.Pirates[marketShown].Price;

    void MarketAction()
    {
        if (!RunnerSave.Owns(MarketKind, marketShown))
        {
            if (!RunnerSave.TryBuyItem(MarketKind, marketShown, ShownPrice)) { game.Audio?.Stumble(); return; }
            game.Audio?.Buy();
        }
        else game.Audio?.Click();
        RunnerSave.Choose(MarketKind, marketShown);
        game.WearLoadout();
        RefreshMarket();
    }

    void RefreshMarket()
    {
        int count = marketJetskis ? RunnerCatalog.Jetskis.Length : RunnerCatalog.Pirates.Length;
        marketTitle.text = marketJetskis ? "JET SKI'LER" : "KARAKTERLER";
        marketName.text = marketJetskis ? RunnerCatalog.Jetskis[marketShown].Name : RunnerCatalog.Pirates[marketShown].Name;
        // jetskis float on the sea: zoomed in, the backdrop's platform goes behind the panels; pirates stand on it
        backdropRect.sizeDelta = marketJetskis ? new Vector2(1500, 1989) : new Vector2(1180, 1564);
        jetskiTab.color = marketJetskis ? Gold : MarketBlue;
        pirateTab.color = marketJetskis ? MarketBlue : Gold;

        string[] labels = marketJetskis ? new[] { "Hız", "Kontrol", "Takviye" } : new[] { "Hız", "Kontrol", "Bonus" };
        int[] values;
        if (marketJetskis) { var j = RunnerCatalog.Jetskis[marketShown]; values = new[] { j.Speed, j.Handling, j.Boost }; }
        else { var p = RunnerCatalog.Pirates[marketShown]; values = new[] { p.Speed, p.Handling, p.Bonus }; }
        for (int s = 0; s < 3; s++)
        {
            statLabels[s].text = labels[s];
            for (int i = 0; i < StatCells; i++) statBars[s][i].color = i < values[s] ? MarketGreen : MarketCellOff;
        }

        bool owned = RunnerSave.Owns(MarketKind, marketShown);
        bool chosen = owned && (marketJetskis ? RunnerCatalog.SelectedJetski : RunnerCatalog.SelectedPirate) == marketShown;
        marketAction.text = chosen ? "SEÇİLDİ" : owned ? "SEÇ" : ShownPrice.ToString("N0") + " COIN";
        bool cannotPay = !owned && RunnerSave.Bank < ShownPrice;
        marketActionBack.color = cannotPay ? new Color(.5f, .5f, .5f) : MarketGreen;
        marketActionEdge.color = cannotPay ? new Color(.3f, .3f, .3f) : MarketGreenDark;

        var icons = marketJetskis ? jetskiIcons : pirateIcons;
        for (int i = 0; i < marketCards.Count; i++)
        {
            var card = marketCards[i];
            card.root.SetActive(i < count);
            if (i >= count) continue;
            int price = marketJetskis ? RunnerCatalog.Jetskis[i].Price : RunnerCatalog.Pirates[i].Price;
            bool has = RunnerSave.Owns(MarketKind, i);
            card.price.text = has ? "" : price.ToString("N0");
            card.check.SetActive((marketJetskis ? RunnerCatalog.SelectedJetski : RunnerCatalog.SelectedPirate) == i);
            card.icon.texture = icons != null ? icons[i] : null;
            card.edge.color = i == marketShown ? Gold : MarketWoodDark;
        }
        if (marketReady) ShowOnStage(marketJetskis, marketShown);
    }

    void UpdateMarket()
    {
        if (!marketReady) return;
        bool open = market.activeSelf && showMarket;
        if (stage.gameObject.activeSelf != open) stage.gameObject.SetActive(open);
        if (!open) return;
        marketBank.text = RunnerSave.Bank.ToString("N0");
        if (marketJetskis)
        {
            var bob = stageJetski.localPosition; bob.y = Mathf.Sin(Time.unscaledTime * 2f) * stageJetskiWidth * .006f;
            stageJetski.localPosition = bob;
            UpdateStageWater();
        }
        stageCamera.targetTexture = stageView;
        stageCamera.Render();
    }
}
