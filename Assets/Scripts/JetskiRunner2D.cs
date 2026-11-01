using System.Collections.Generic;
using UnityEngine;

public sealed class JetskiRunner2D : MonoBehaviour
{
    [Header("Reference artwork")]
    public Sprite ocean, rider, pirateShip, coin, barrel, rocks, fuel;

    const float HalfHeight = 8.89f;
    const float PlayerY = -5.45f;
    readonly List<RunnerItem> items = new List<RunnerItem>();
    readonly List<SpriteRenderer> foam = new List<SpriteRenderer>();
    Camera camera2D;
    Transform player;
    SpriteRenderer ship;
    Texture2D white;
    Sprite foamSprite;
    int lane = 1, coins, fuelLevel = 100;
    float distance, spawnClock, foamClock, fuelClock, swipeX, previousX, hitFlash;
    bool paused, ended;

    struct RunnerItem
    {
        public Transform transform;
        public SpriteRenderer renderer;
        public int lane;
        public int kind;
        public float progress;
        public float phase;
    }

    void Awake()
    {
        camera2D = Camera.main;
        if (!camera2D)
        {
            var go = new GameObject("Portrait Camera");
            camera2D = go.AddComponent<Camera>();
            go.tag = "MainCamera";
        }
        camera2D.orthographic = true;
        camera2D.orthographicSize = HalfHeight;
        camera2D.transform.position = new Vector3(0, 0, -10);
        camera2D.clearFlags = CameraClearFlags.SolidColor;
        camera2D.backgroundColor = new Color(.12f, .64f, .88f);

        if (ocean) {
            var background = MakeSprite("Tropical ocean", ocean, -5);
            float height = 2 * HalfHeight;
            float width = height * camera2D.aspect;
            float factor = Mathf.Max(width / ocean.bounds.size.x, height / ocean.bounds.size.y);
            background.transform.localScale = Vector3.one * factor;
        }
        if (pirateShip) {
            ship = MakeSprite("Pirate pursuer", pirateShip, 1);
            SetWidth(ship.transform, pirateShip, 2.15f);
        }
        if (rider) {
            var sprite = MakeSprite("Jetski and rider", rider, 20);
            player = sprite.transform;
            SetWidth(player, rider, 3.8f);
            player.position = new Vector3(0, PlayerY, 0);
        }
        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
        foamSprite = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
        SpawnCoinTrail(1, 0f);
        SpawnCoinTrail(0, .5f);
    }

    SpriteRenderer MakeSprite(string name, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        return renderer;
    }

    static void SetWidth(Transform target, Sprite sprite, float width)
    {
        if (sprite) target.localScale = Vector3.one * (width / sprite.bounds.size.x);
    }

    void Update()
    {
        ReadInput();
        if (paused || ended) return;
        float dt = Time.deltaTime;
        distance += dt * (17f + Mathf.Min(distance * .025f, 11f));
        fuelClock += dt;
        if (fuelClock >= 2f) { fuelClock -= 2f; fuelLevel = Mathf.Max(0, fuelLevel - 1); }
        if (fuelLevel == 0) ended = true;
        hitFlash = Mathf.Max(0, hitFlash - dt);

        if (player)
        {
            float targetX = (lane - 1) * 2.3f;
            float x = Mathf.SmoothDamp(player.position.x, targetX, ref previousX, .105f);
            player.position = new Vector3(x, PlayerY + Mathf.Sin(Time.time * 12) * .035f, 0);
            player.rotation = Quaternion.Euler(0, 0, Mathf.Clamp((player.position.x - targetX) * 10, -13, 13));
            if (player.GetComponent<SpriteRenderer>())
                player.GetComponent<SpriteRenderer>().color = hitFlash > 0 && Mathf.Sin(Time.time * 28) > 0 ? new Color(1, .55f, .55f) : Color.white;
        }
        if (ship)
        {
            ship.transform.position = new Vector3(Mathf.Sin(Time.time * .35f) * .12f, 4.85f + Mathf.Sin(Time.time * 3.5f) * .06f, 0);
            ship.transform.rotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 2) * 1.5f);
        }

        spawnClock += dt;
        if (spawnClock > .78f)
        {
            spawnClock = 0;
            int pickedLane = Random.Range(0, 3);
            int roll = Random.Range(0, 100);
            Spawn(roll < 66 ? 0 : roll < 80 ? 1 : roll < 94 ? 2 : 3, pickedLane, 0);
            if (roll >= 66) Spawn(0, (pickedLane + Random.Range(1, 3)) % 3, 0);
        }
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            item.progress += dt * (.39f + Mathf.Min(distance * .0004f, .18f));
            Position(item);
            if (item.progress > .79f && item.progress < .9f && item.lane == lane)
            {
                if (item.kind == 0) { coins++; DestroyItem(i); continue; }
                if (item.kind == 3) { fuelLevel = Mathf.Min(100, fuelLevel + 30); DestroyItem(i); continue; }
                if (hitFlash <= 0) { hitFlash = 1.2f; fuelLevel = Mathf.Max(0, fuelLevel - 22); DestroyItem(i); continue; }
            }
            if (item.progress > 1.12f) { DestroyItem(i); continue; }
            items[i] = item;
        }
        foamClock += dt;
        if (foamClock > .07f) { foamClock = 0; SpawnFoam(); }
        for (int i = foam.Count - 1; i >= 0; i--)
        {
            if (!foam[i]) { foam.RemoveAt(i); continue; }
            var f = foam[i];
            f.transform.position += Vector3.down * dt * 7;
            f.color = new Color(1, 1, 1, Mathf.Max(0, f.color.a - dt * 1.7f));
            if (f.color.a <= 0) { Destroy(f.gameObject); foam.RemoveAt(i); }
        }
    }

    void ReadInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) paused = !paused;
        if (ended && (Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0))) { Restart(); return; }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) lane = Mathf.Max(0, lane - 1);
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) lane = Mathf.Min(2, lane + 1);
        if (Input.touchCount > 0)
        {
            var touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began) swipeX = touch.position.x;
            if (touch.phase == TouchPhase.Ended) Swipe(touch.position.x - swipeX);
        }
        else if (Input.GetMouseButtonDown(0)) swipeX = Input.mousePosition.x;
        else if (Input.GetMouseButtonUp(0)) Swipe(Input.mousePosition.x - swipeX);
    }

    void Swipe(float delta)
    {
        if (Mathf.Abs(delta) < Screen.width * .055f) return;
        lane = Mathf.Clamp(lane + (delta > 0 ? 1 : -1), 0, 2);
    }

    void SpawnCoinTrail(int targetLane, float start)
    {
        for (int n = 0; n < 5; n++) Spawn(0, targetLane, start + n * .105f);
    }

    void Spawn(int kind, int targetLane, float progress)
    {
        var art = kind == 0 ? coin : kind == 1 ? barrel : kind == 2 ? rocks : fuel;
        if (!art) return;
        var renderer = MakeSprite(kind == 0 ? "Anchor coin" : kind == 1 ? "Barrel obstacle" : kind == 2 ? "Rock obstacle" : "Fuel pickup", art, 8);
        var item = new RunnerItem { transform = renderer.transform, renderer = renderer, lane = targetLane, kind = kind, progress = progress, phase = Random.Range(0f, 6.28f) };
        Position(item);
        items.Add(item);
    }

    void Position(RunnerItem item)
    {
        float p = item.progress;
        float curved = p * p;
        float y = Mathf.Lerp(2.65f, -10.7f, p);
        float laneSpacing = Mathf.Lerp(.42f, 3.25f, curved);
        float x = (item.lane - 1) * laneSpacing;
        float width = (item.kind == 0 ? .68f : item.kind == 3 ? 1.3f : 1.75f) * Mathf.Lerp(.27f, 1.45f, curved);
        SetWidth(item.transform, item.renderer.sprite, width);
        if (item.kind == 0) item.transform.localScale = new Vector3(item.transform.localScale.x * (.75f + .25f * Mathf.Abs(Mathf.Cos(Time.time * 6 + item.phase))), item.transform.localScale.y, 1);
        item.transform.position = new Vector3(x, y + Mathf.Sin(Time.time * 4 + item.phase) * .045f, 0);
        item.renderer.sortingOrder = p > .73f ? 22 : 8;
    }

    void SpawnFoam()
    {
        if (!rider || foam.Count > 60) return;
        for (int i = 0; i < 2; i++)
        {
            var f = MakeSprite("Moving wake", foamSprite, 2);
            f.color = new Color(1, 1, 1, .7f);
            bool distantGlint = Random.value < .35f;
            f.transform.position = distantGlint
                ? new Vector3(Random.Range(-4.6f, 4.6f), Random.Range(-1.5f, 2.5f), 0)
                : new Vector3(player.position.x + Random.Range(-1.6f, 1.6f), -7.9f + Random.Range(-.2f, .2f), 0);
            f.transform.localScale = distantGlint ? new Vector3(.12f, .025f, 1) : Vector3.one * .08f;
            foam.Add(f);
            // The foam uses a generated white texture so it is independent of the coin artwork.
        }
    }

    void DestroyItem(int index) { Destroy(items[index].transform.gameObject); items.RemoveAt(index); }

    void Restart()
    {
        for (int i = items.Count - 1; i >= 0; i--) DestroyItem(i);
        lane = 1; coins = 0; fuelLevel = 100; fuelClock = 0; distance = 0; ended = false; paused = false;
        SpawnCoinTrail(1, 0);
    }

    void OnGUI()
    {
        float s = Mathf.Min(Screen.width / 940f, Screen.height / 1672f);
        float ox = (Screen.width - 940 * s) * .5f;
        float oy = (Screen.height - 1672 * s) * .5f;
        GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, Vector3.one * s);
        Box(new Rect(31, 30, 100, 91), new Color(.05f, .25f, .41f, .8f));
        Label(new Rect(32, 31, 98, 82), paused ? "▶" : "Ⅱ", 58, TextAnchor.MiddleCenter);
        if (GUI.Button(new Rect(30, 30, 102, 95), "", GUIStyle.none)) paused = !paused;
        Label(new Rect(285, 22, 370, 100), Mathf.FloorToInt(distance) + " m", 67, TextAnchor.MiddleCenter);
        Box(new Rect(730, 41, 184, 69), new Color(.04f, .23f, .39f, .86f));
        if (coin) GUI.DrawTexture(new Rect(726, 29, 84, 84), coin.texture, ScaleMode.ScaleToFit, true);
        Label(new Rect(812, 43, 100, 64), coins.ToString("000"), 46, TextAnchor.MiddleCenter);
        if (fuelLevel < 35) Label(new Rect(340, 132, 260, 50), "FUEL  " + fuelLevel + "%", 28, TextAnchor.MiddleCenter);
        if (paused || ended)
        {
            Box(new Rect(150, 680, 640, 250), new Color(.02f, .2f, .33f, .87f));
            Label(new Rect(170, 700, 600, 90), ended ? "RUN OVER" : "PAUSED", 55, TextAnchor.MiddleCenter);
            Label(new Rect(170, 795, 600, 80), ended ? "Tap to restart" : "Tap pause to continue", 32, TextAnchor.MiddleCenter);
            if (ended && GUI.Button(new Rect(150, 680, 640, 250), "", GUIStyle.none)) Restart();
        }
    }

    void Box(Rect r, Color color) { GUI.color = color; GUI.DrawTexture(r, white); GUI.color = Color.white; }
    void Label(Rect r, string text, int fontSize, TextAnchor align)
    {
        var style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = align };
        style.normal.textColor = new Color(.02f, .14f, .24f);
        GUI.Label(new Rect(r.x + 3, r.y + 4, r.width, r.height), text, style);
        style.normal.textColor = Color.white;
        GUI.Label(r, text, style);
    }
}
