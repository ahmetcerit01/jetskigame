using UnityEngine;

// The opening: in the menu the pirate is at the head of a T-shaped dock, stealing from a treasure chest. Pressing play
// gets them spotted by a rival: the pirate runs down the dock and jumps onto the jetski first, the rival follows onto
// their own, and the camera swings round into the run, leaving the dock behind.
public sealed partial class JetskiRunnerGame
{
    const float MenuJetskiX = -2.45f, DockTop = .55f, WalkX = -4.2f, HeadZ = -1.2f;
    const float HeroRunAt = .45f, HeroJumpAt = 1.75f, HeroLandAt = 2.35f;
    const float RivalRunAt = .9f, RivalJumpAt = 2.4f, RivalLandAt = 3f;
    const float CameraSwingAt = 2.45f, PullAwayAt = 2.6f, IntroLength = 3.8f;
    static readonly Vector3 HeroSpot = new Vector3(-3.1f, DockTop, -3.1f), RivalSpot = new Vector3(-6.3f, DockTop, -2.2f);
    static readonly Vector3 ChestSpot = HeroSpot + new Vector3(1.15f, 0, -.35f);
    static readonly Vector3 RivalJetskiSpot = new Vector3(-2.45f, WaterY, 1.5f);
    static readonly Vector3[] HeroPath = { HeroSpot, new Vector3(WalkX, DockTop, HeadZ), new Vector3(WalkX, DockTop, 4.4f) };
    static readonly Vector3[] RivalPath = { RivalSpot, new Vector3(WalkX, DockTop, HeadZ), new Vector3(WalkX, DockTop, 1.6f) };

    Transform introRoot;
    IntroPirate heroActor, rivalActor;
    GameObject alertMark;
    Material dockPlank, dockBeam, dockPost, dockGold, dockRope;
    readonly System.Collections.Generic.List<Transform> treasure = new();
    float introTime, menuClock;
    bool heroLanded, rivalLanded;
    Vector3 cameraFrom, heroLeapFrom, rivalLeapFrom; Quaternion cameraFromRotation;

    void CreateIntroScene()
    {
        if (!riderPrefab || !rider || !chaser) return;
        introRoot = new GameObject("Intro dock").transform;
        introRoot.SetParent(movingWorld, false);
        BuildDock();
        IntroProp("24-treasure-chest", ChestSpot, -25f, .95f);
        IntroProp("15-crate", new Vector3(-7f, DockTop, -1.9f), 15f, 1f);
        IntroProp("16-barrel-closed", new Vector3(-7f, DockTop, -3.6f), 40f, 1f);
        // gold rising out of the open chest
        if (goldPrefab)
            for (int i = 0; i < 4; i++)
            {
                var coin = Instantiate(goldPrefab, introRoot);
                foreach (var c in coin.GetComponentsInChildren<Collider>(true)) Destroy(c);
                coin.transform.localScale = Vector3.one * .4f;
                coin.SetActive(false);
                treasure.Add(coin.transform);
            }
        heroActor = MakeActor("Hero on the dock", false, HeroSpot, 125f);
        rivalActor = MakeActor("Rival on the dock", true, RivalSpot, 120f);
        var mark = new GameObject("Rival alert");
        mark.transform.SetParent(introRoot, false);
        var text = mark.AddComponent<TextMesh>();
        text.text = "!"; text.fontSize = 90; text.characterSize = .1f; text.anchor = TextAnchor.MiddleCenter;
        text.color = new Color(1f, .22f, .15f); text.fontStyle = FontStyle.Bold;
        mark.transform.position = RivalSpot + Vector3.up * 2.4f;
        mark.SetActive(false);
        alertMark = mark;
        heroActor.Dress();
        // both riders start off their jetskis
        SetRendered(rider.gameObject, false);
        var rivalRider = chaser.GetComponentInChildren<PirateRider>();
        if (rivalRider) SetRendered(rivalRider.gameObject, false);
        chaser.position = RivalJetskiSpot; chaser.rotation = Quaternion.identity;
        chaser.gameObject.SetActive(true);
        chaserZ = RivalJetskiSpot.z;
        PlaceMenuCamera(true);
    }

    // ---------- the dock: a wide head towards the camera and a narrow walkway running out to the moored jetskis ----------

    void BuildDock()
    {
        dockPlank = Make("Dock plank", new Color(.46f, .19f, .05f), 0f, .25f);
        dockBeam = Make("Dock beam", new Color(.6f, .29f, .07f), 0f, .3f);
        dockPost = Make("Dock post", new Color(.36f, .14f, .04f), 0f, .25f);
        dockGold = Make("Dock brass", new Color(1f, .68f, .12f), .7f, .75f);
        dockRope = Make("Dock rope", new Color(.56f, .43f, .27f), 0f, .15f);

        const float left = -7.6f, right = -.8f, front = -5f, back = HeadZ + .2f, plankY = DockTop - .08f;
        // head: planks run away from the camera, framed by a lighter beam
        int planks = 13;
        float width = (right - left) / planks;
        for (int i = 0; i < planks; i++)
            DockBox(new Vector3(left + (i + .5f) * width, plankY, (front + back) * .5f), new Vector3(width - .05f, .16f, back - front), dockPlank);
        DockBox(new Vector3((left + right) * .5f, plankY - .22f, front - .02f), new Vector3(right - left, .42f, .3f), dockBeam);
        DockBox(new Vector3((left + right) * .5f, plankY + .03f, back), new Vector3(right - left, .2f, .22f), dockBeam);
        DockBox(new Vector3(left - .05f, plankY - .1f, (front + back) * .5f), new Vector3(.3f, .5f, back - front + .3f), dockBeam);
        DockBox(new Vector3(right + .05f, plankY - .1f, (front + back) * .5f), new Vector3(.3f, .5f, back - front + .3f), dockBeam);
        // walkway: planks run across
        const float walkLeft = WalkX - 1f, walkRight = WalkX + 1f, walkEnd = 5.2f;
        for (float z = back + .3f; z < walkEnd; z += .72f)
            DockBox(new Vector3(WalkX, plankY, z), new Vector3(walkRight - walkLeft, .16f, .66f), dockPlank);
        foreach (float x in new[] { walkLeft, walkRight })
            DockBox(new Vector3(x, plankY + .03f, (back + walkEnd) * .5f), new Vector3(.16f, .2f, walkEnd - back), dockBeam);
        DockBox(new Vector3(WalkX, plankY + .03f, walkEnd), new Vector3(walkRight - walkLeft + .16f, .2f, .18f), dockBeam);
        for (float z = back + .5f; z < walkEnd; z += 1.44f)
            foreach (float x in new[] { walkLeft + .28f, walkRight - .28f }) DockStud(new Vector3(x, plankY + .1f, z), .045f);

        // posts along the front of the head with sagging rope and diagonal braces
        float[] frontPosts = { left + .55f, (left + right) * .5f, right - .55f };
        foreach (float x in frontPosts) DockPost(new Vector3(x, 0, front), .34f, true);
        DockRope(new Vector3(frontPosts[0], 1.02f, front), new Vector3(frontPosts[1], 1.02f, front), .28f);
        DockRope(new Vector3(frontPosts[1], 1.02f, front), new Vector3(frontPosts[2], 1.02f, front), .28f);
        DockBrace(new Vector3(frontPosts[0] + .2f, -.25f, front), new Vector3(frontPosts[0] + 1.1f, .25f, front));
        DockBrace(new Vector3(frontPosts[2] - .2f, -.25f, front), new Vector3(frontPosts[2] - 1.1f, .25f, front));
        foreach (float x in new[] { left + 1.5f, right - 1.5f }) DockStud(new Vector3(x, plankY - .22f, front - .19f), .09f);

        // three posts a side down the walkway, rope between them
        float[] sideZ = { back + .1f, 2.3f, walkEnd - .1f };
        foreach (float x in new[] { walkLeft - .1f, walkRight + .1f })
        {
            for (int i = 0; i < sideZ.Length; i++)
            {
                DockPost(new Vector3(x, 0, sideZ[i]), .25f, false);
                if (i > 0) DockRope(new Vector3(x, 1f, sideZ[i - 1]), new Vector3(x, 1f, sideZ[i]), .2f);
            }
        }
    }

    Transform DockPart(PrimitiveType type, Vector3 at, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(introRoot, false);
        part.transform.localPosition = at; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part.transform;
    }

    void DockBox(Vector3 at, Vector3 size, Material material) => DockPart(PrimitiveType.Cube, at, size, material);
    void DockStud(Vector3 at, float radius) => DockPart(PrimitiveType.Sphere, at, Vector3.one * radius * 2f, dockGold);

    // A round pile from the sea bed up past the deck, with rope wound round its top and a brass band low down.
    void DockPost(Vector3 foot, float radius, bool band)
    {
        const float bottom = -1.2f, top = 1.3f;
        DockPart(PrimitiveType.Cylinder, new Vector3(foot.x, (bottom + top) * .5f, foot.z), new Vector3(radius * 2f, (top - bottom) * .5f, radius * 2f), dockPost);
        DockPart(PrimitiveType.Cylinder, new Vector3(foot.x, top + .005f, foot.z), new Vector3(radius * 1.5f, .012f, radius * 1.5f), dockBeam);
        for (int i = 0; i < 2; i++)
            DockPart(PrimitiveType.Cylinder, new Vector3(foot.x, .96f + i * .13f, foot.z), new Vector3(radius * 2.45f, .065f, radius * 2.45f), dockRope);
        if (band) DockPart(PrimitiveType.Cylinder, new Vector3(foot.x, -.2f, foot.z), new Vector3(radius * 2.25f, .1f, radius * 2.25f), dockGold);
    }

    void DockRope(Vector3 from, Vector3 to, float sag)
    {
        const int segments = 8;
        for (int i = 0; i < segments; i++)
        {
            float a = i / (float)segments, b = (i + 1) / (float)segments;
            DockSpan(RopePoint(from, to, sag, a), RopePoint(from, to, sag, b), .055f, dockRope);
        }
    }

    static Vector3 RopePoint(Vector3 from, Vector3 to, float sag, float t) => Vector3.Lerp(from, to, t) + Vector3.down * sag * 4f * t * (1f - t);
    void DockBrace(Vector3 from, Vector3 to) => DockSpan(from, to, .1f, dockPost);

    void DockSpan(Vector3 from, Vector3 to, float radius, Material material)
    {
        var span = DockPart(PrimitiveType.Cylinder, (from + to) * .5f, new Vector3(radius * 2f, Vector3.Distance(from, to) * .55f, radius * 2f), material);
        span.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
    }

    static void SetRendered(GameObject go, bool on)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    void IntroProp(string name, Vector3 at, float yaw, float size)
    {
        if (shoreModels == null) return;
        foreach (var model in shoreModels)
        {
            if (!model || model.name != name) continue;
            var prop = Instantiate(model, introRoot);
            foreach (var c in prop.GetComponentsInChildren<Collider>(true)) Destroy(c);
            prop.transform.localPosition = at; prop.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            // shore models are authored at scenery scale: fit each to the dock and stand it on the planks
            var renderers = prop.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            prop.transform.localScale *= size / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            prop.transform.position += Vector3.up * (at.y - bounds.min.y);
            return;
        }
    }

    IntroPirate MakeActor(string name, bool rival, Vector3 at, float yaw)
    {
        var whole = Instantiate(riderPrefab);
        var seated = whole.GetComponentInChildren<PirateRider>();
        var go = seated.gameObject;
        go.transform.SetParent(introRoot, true);
        go.name = name;
        Destroy(seated);
        Destroy(whole);
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        if (rival)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                foreach (var m in mats)
                    foreach (var prop in new[] { "_Color", "_BaseColor", "baseColorFactor" })
                        if (m.HasProperty(prop)) m.SetColor(prop, m.GetColor(prop) * new Color(.3f, .22f, .26f, 1f) + new Color(.14f, 0, .02f, 0));
                r.materials = mats;
            }
        go.transform.position = at; go.transform.rotation = Quaternion.Euler(0, yaw, 0);
        var actor = go.AddComponent<IntroPirate>();
        actor.GroundY = at.y;
        actor.Play("MenuIdle");
        return actor;
    }

    // Framed so the pirate stands in the gap between the title and the play button; on a wide screen it moves aside of the buttons.
    void PlaceMenuCamera(bool snap)
    {
        var target = HeroSpot + new Vector3(.55f, .85f, 0);
        float aspect = gameCamera.aspect;
        var position = target + new Vector3(1.2f, 1.1f, -5f);
        var rotation = Quaternion.LookRotation(target - position);
        if (aspect > 1f)
        {
            float halfWidth = Vector3.Distance(position, target) * Mathf.Tan(gameCamera.fieldOfView * .5f * Mathf.Deg2Rad) * aspect;
            position += rotation * Vector3.right * halfWidth * .55f;
        }
        float k = snap ? 1f : 1f - Mathf.Exp(-5f * Time.deltaTime);
        gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, position, k);
        gameCamera.transform.rotation = Quaternion.Slerp(gameCamera.transform.rotation, rotation, k);
    }

    // Menu loop: stands by the chest, bends down to it, gold comes up, cheers.
    void UpdateMenuPirate()
    {
        if (!heroActor) return;
        menuClock += Time.deltaTime;
        float t = menuClock % 8f;
        bool open = t > 2.6f && t < 6.6f;
        heroActor.Lean = t > 2f && t < 4.8f ? 1f : 0f;
        if (t - Time.deltaTime < 4.8f && t >= 4.8f) heroActor.Play("Victory");
        if (t - Time.deltaTime < 6.8f && t >= 6.8f) heroActor.Play("MenuIdle");
        UpdateTreasure(open);
        PlaceMenuCamera(false);
    }

    void UpdateTreasure(bool open)
    {
        var chest = ChestSpot + Vector3.up * .55f;
        for (int i = 0; i < treasure.Count; i++)
        {
            var coin = treasure[i];
            if (coin.gameObject.activeSelf != open) coin.gameObject.SetActive(open);
            if (!open) continue;
            float phase = (Time.time * .55f + i * .25f) % 1f;
            coin.position = chest + new Vector3(Mathf.Sin(i * 2.4f) * .28f * phase, phase * 1.1f, Mathf.Cos(i * 2.4f) * .2f * phase);
            coin.rotation = Quaternion.Euler(90f, Time.time * 240f + i * 60f, 0);
            coin.localScale = Vector3.one * .4f * Mathf.Sin(phase * Mathf.PI);
        }
    }

    public GameObject RiderPrefab => riderPrefab;

    // Puts the market choices on the player's jetski and pirate, including the one waiting on the dock.
    public void WearLoadout()
    {
        RunnerCatalog.DressJetski(player.gameObject, RunnerCatalog.SelectedJetski);
        if (rider)
        {
            RunnerCatalog.DressPirate(rider.gameObject, RunnerCatalog.SelectedPirate);
            if (state == RunState.Menu && introRoot) SetRendered(rider.gameObject, false);
        }
        if (heroActor) heroActor.Dress();
    }

    void BeginIntro()
    {
        state = RunState.Intro;
        introTime = 0f;
        UpdateTreasure(false);
        heroActor.Lean = 0f;
        heroActor.Play("MenuIdle");
        heroActor.FaceTowards(rivalActor.transform.position, .25f);
        rivalActor.FaceTowards(heroActor.transform.position, .15f);
        alertMark.SetActive(true);
        audioFx?.Stumble();
    }

    void UpdateIntro()
    {
        float before = introTime;
        introTime += Time.deltaTime;
        bool Crossed(float at) => before < at && introTime >= at;

        if (Crossed(RivalRunAt)) alertMark.SetActive(false);
        if (!heroLanded) ActOut(heroActor, HeroPath, HeroRunAt, HeroJumpAt, HeroLandAt, player.position, ref heroLeapFrom);
        if (!rivalLanded) ActOut(rivalActor, RivalPath, RivalRunAt, RivalJumpAt, RivalLandAt, chaser.position + Vector3.up * .5f, ref rivalLeapFrom);
        if (Crossed(HeroJumpAt) || Crossed(RivalJumpAt)) audioFx?.Jump();
        if (!heroLanded && introTime >= HeroLandAt)
        {
            heroLanded = true;
            heroActor.gameObject.SetActive(false);
            SetRendered(rider.gameObject, true);
            LandingSplash(7f); audioFx?.Splash(1f);
        }
        if (!rivalLanded && introTime >= RivalLandAt)
        {
            rivalLanded = true;
            rivalActor.gameObject.SetActive(false);
            var rivalRider = chaser.GetComponentInChildren<PirateRider>();
            if (rivalRider) SetRendered(rivalRider.gameObject, true);
            ImpactSplash(chaser.position, .8f);
        }

        // the jetski rocks under the jump, then pulls out towards the middle lane
        float pull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PullAwayAt, IntroLength, introTime));
        var p = player.position;
        p.x = Mathf.Lerp(MenuJetskiX, 0f, pull);
        p.y = .15f + Mathf.Sin(Time.time * 2.2f) * .05f;
        p.z = ScreenBase;
        player.position = p;
        player.rotation = Quaternion.Euler(heroLanded ? -6f * (1f - pull) : 0f, 14f * Mathf.Sin(pull * Mathf.PI), 0);
        UpdateWake(); UpdateSeagulls(); ApplyAtmosphere();
        audioFx?.SetMotion(0f, pull, false, false, false);

        // the camera follows the run down the dock, then swings behind the jetski
        if (introTime < CameraSwingAt)
        {
            var followed = heroLanded ? player.position : heroActor.transform.position;
            var look = new Vector3(followed.x, DockTop + .8f, followed.z);
            var position = look + new Vector3(1.4f, 1.5f, -5.4f);
            float k = introTime < HeroRunAt ? 0f : 1f - Mathf.Exp(-4f * Time.deltaTime);
            gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, position, k);
            gameCamera.transform.rotation = Quaternion.Slerp(gameCamera.transform.rotation, Quaternion.LookRotation(look - position), k);
            cameraFrom = gameCamera.transform.position; cameraFromRotation = gameCamera.transform.rotation;
        }
        else
        {
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CameraSwingAt, IntroLength, introTime));
            var chaseCamera = CameraOffset + new Vector3(p.x * .78f, 0, 0);
            gameCamera.transform.position = Vector3.Lerp(cameraFrom, chaseCamera, swing);
            gameCamera.transform.rotation = Quaternion.Slerp(cameraFromRotation, Quaternion.Euler(CameraPitch, 0, 0), swing);
        }

        if (introTime >= IntroLength) { state = RunState.Running; introChase = 3.2f; }
    }

    // One pirate's part: wait, run along the path, then leap onto the jetski.
    void ActOut(IntroPirate actor, Vector3[] path, float runAt, float jumpAt, float landAt, Vector3 seat, ref Vector3 leapFrom)
    {
        if (introTime < runAt) return;
        if (introTime < jumpAt)
        {
            actor.Running = true;
            float along = Mathf.InverseLerp(runAt, jumpAt, introTime) * PathLength(path);
            for (int i = 1; i < path.Length; i++)
            {
                float leg = Vector3.Distance(path[i - 1], path[i]);
                if (along > leg && i < path.Length - 1) { along -= leg; continue; }
                var at = Vector3.Lerp(path[i - 1], path[i], along / leg);
                var current = actor.transform.position;
                actor.transform.position = new Vector3(at.x, current.y, at.z);
                actor.FaceTowards(path[i], .12f);
                break;
            }
            return;
        }
        if (actor.Grounded)
        {
            actor.Grounded = false; actor.Running = false;
            actor.Play("Jump");
            actor.FaceTowards(seat + Vector3.forward * 8f, .2f);
            leapFrom = actor.transform.position;
        }
        float t = Mathf.Clamp01((introTime - jumpAt) / (landAt - jumpAt));
        actor.transform.position = Vector3.Lerp(leapFrom, seat, t) + Vector3.up * 6f * t * (1f - t);
    }

    static float PathLength(Vector3[] path)
    {
        float length = 0f;
        for (int i = 1; i < path.Length; i++) length += Vector3.Distance(path[i - 1], path[i]);
        return length;
    }

    void RemoveIntroWhenPassed()
    {
        if (introRoot && distanceTravelled > 45f) { Destroy(introRoot.gameObject); introRoot = null; }
    }
}

// A pirate on the dock: plays the rider's clips, and poses the bones by hand for what has no clip (bending over the chest, running).
sealed class IntroPirate : MonoBehaviour
{
    public float Lean;
    public bool Grounded = true, Running;
    public float GroundY;
    float leanShown, runShown, turnSpeed;
    Quaternion turnTo;
    bool turning;
    Animator animator;
    Transform spine, chest, neck, leftFoot, rightFoot, leftThigh, rightThigh, leftShin, rightShin, leftArm, rightArm;

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (!animator.isHuman) return;
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        rightShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
    }

    public void Dress() => RunnerCatalog.DressPirate(gameObject, RunnerCatalog.SelectedPirate);

    public void Play(string clip) => animator.CrossFadeInFixedTime(clip, .15f, 0);

    public void FaceTowards(Vector3 point, float seconds)
    {
        var flat = point - transform.position; flat.y = 0;
        if (flat.sqrMagnitude < .001f) return;
        turnTo = Quaternion.LookRotation(flat); turnSpeed = 180f / Mathf.Max(.05f, seconds); turning = true;
    }

    static void Bend(Transform bone, Vector3 axis, float degrees)
    {
        if (bone) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    void LateUpdate()
    {
        if (turning) transform.rotation = Quaternion.RotateTowards(transform.rotation, turnTo, turnSpeed * Time.deltaTime);
        var side = transform.right;

        leanShown = Mathf.MoveTowards(leanShown, Lean, Time.deltaTime * 2.2f);
        if (leanShown > 0f)
        {
            Bend(spine, side, leanShown * 13f);
            Bend(chest, side, leanShown * 13f);
            Bend(neck, side, leanShown * -8f);
        }

        runShown = Mathf.MoveTowards(runShown, Running ? 1f : 0f, Time.deltaTime * 8f);
        if (runShown > 0f)
        {
            float stride = Mathf.Sin(Time.time * 15f);
            Bend(spine, side, runShown * 14f);
            Bend(leftThigh, side, runShown * -48f * stride);
            Bend(rightThigh, side, runShown * 48f * stride);
            Bend(leftShin, side, runShown * 55f * Mathf.Max(0f, stride));
            Bend(rightShin, side, runShown * 55f * Mathf.Max(0f, -stride));
            Bend(leftArm, side, runShown * 42f * stride);
            Bend(rightArm, side, runShown * -42f * stride);
        }

        // the model's origin is set for the jetski seat, so stand it on the planks by its feet
        if (Grounded && leftFoot && rightFoot)
            transform.position += Vector3.up * (GroundY + .08f - Mathf.Min(leftFoot.position.y, rightFoot.position.y));
    }
}
