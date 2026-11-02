#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// Turns the Meshy P0 pack (Assets/Models/Meshy) into the playable rider:
// humanoid import for the pirate and his 9 clips, an Animator Controller, and a
// "MeshyPirateJetski" prefab (red jetski + seated pirate with hand IK) wired into every runner scene.
public static class JetskiMeshySetup
{
    const string Root = "Assets/Models/Meshy/";
    const string CharacterPath = Root + "Character/Pirate_Rigged.fbx";
    const string AnimDir = Root + "Character/Animations/";
    const string JetskiPath = Root + "Jetski/Jetski_Red_7k.fbx";
    const string OutDir = "Assets/Prefabs/Meshy/";
    const string MaterialDir = "Assets/Materials/Meshy/";
    const string ControllerPath = OutDir + "PirateRider.controller";
    const string MaskPath = OutDir + "PirateUpperBody.mask";
    const string PrefabPath = OutDir + "MeshyPirateJetski.prefab";
    static readonly string[] Scenes = { "Assets/Scenes/OceanRun.unity", "Assets/Scenes/PirateCove.unity", "Assets/Scenes/IceCave.unity" };

    // Proportions measured on the red jetski mesh (0 = nose, 1 = tail; heights as a fraction of the hull height).
    const float JetskiLength = 2.6f, RiderHeight = 1.0f, Waterline = .35f;
    const float SeatAlong = .50f, SeatHeight = .75f;
    const float GripAlong = .41f, GripHeight = .97f, GripSide = .39f;

    static readonly (string clip, bool loop)[] Clips =
    {
        ("Ride_Loop", true), ("Lean_Left", false), ("Lean_Right", false), ("Jump", false), ("Land", false),
        ("Hit_Reaction", false), ("Fall", false), ("Victory", false), ("Menu_Idle", true),
    };

    [MenuItem("Jetski Runner/Apply Meshy Pirate Rider")]
    public static void Apply()
    {
        System.IO.Directory.CreateDirectory(OutDir);
        System.IO.Directory.CreateDirectory(MaterialDir);
        AssetDatabase.Refresh();

        var avatar = ImportCharacter();
        var clips = ImportClips(avatar);
        ImportStatic(JetskiPath);
        var controller = BuildController(clips);
        var prefab = BuildPrefab(controller);
        AssetDatabase.SaveAssets();

        foreach (var path in Scenes)
        {
            if (!System.IO.File.Exists(path)) continue;
            var scene = EditorSceneManager.OpenScene(path);
            var game = Object.FindFirstObjectByType<JetskiRunnerGame>();
            if (!game) { Debug.LogWarning(path + " has no JetskiRunnerGame, skipped"); continue; }
            var so = new SerializedObject(game);
            so.FindProperty("riderPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        Debug.Log("Meshy pirate rider applied: " + PrefabPath);
    }

    // ---------- import ----------

    static Avatar ImportCharacter()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(CharacterPath);
        if (importer == null) throw new System.Exception("Missing " + CharacterPath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        ExtractTextures(importer, CharacterPath);

        var avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isHuman || !avatar.isValid)
            throw new System.Exception("Pirate avatar is not a valid humanoid. Open " + CharacterPath + " > Rig > Configure and check the bone mapping.");
        return avatar;
    }

    static Dictionary<string, AnimationClip> ImportClips(Avatar avatar)
    {
        var result = new Dictionary<string, AnimationClip>();
        foreach (var (name, loop) in Clips)
        {
            string path = AnimDir + "Pirate@" + name + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new System.Exception("Missing " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            var defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) { importer.SaveAndReimport(); defaults = importer.defaultClipAnimations; }
            var clip = defaults[0];
            clip.name = name;
            clip.loopTime = loop;
            clip.loopPose = loop;
            // In-place clips: bake every root channel into the pose so seated hips keep their authored height.
            clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true; clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = true; clip.keepOriginalPositionXZ = true;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
            var asset = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (!asset) throw new System.Exception("No animation clip in " + path);
            result[name] = asset;
        }
        return result;
    }

    static void ImportStatic(string path)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new System.Exception("Missing " + path);
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.SaveAndReimport();
        ExtractTextures(importer, path);
    }

    static string TextureFolder(string modelPath) => System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/') + "/Textures";

    static void ExtractTextures(ModelImporter importer, string modelPath)
    {
        string folder = TextureFolder(modelPath);
        if (System.IO.Directory.Exists(folder) && System.IO.Directory.GetFiles(folder, "*.png").Length > 0) return;
        System.IO.Directory.CreateDirectory(folder);
        if (importer.ExtractTextures(folder)) { AssetDatabase.Refresh(); importer.SaveAndReimport(); }
    }

    // Meshy bakes everything into one base-colour map; give it a plain, non-metallic Standard material
    // so the toy look survives the built-in pipeline.
    static Material MakeMaterial(string name, string modelPath)
    {
        string folder = TextureFolder(modelPath);
        var textures = (System.IO.Directory.Exists(folder) ? System.IO.Directory.GetFiles(folder) : new string[0])
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg") || p.EndsWith(".tga"))
            .Where(p => { var n = System.IO.Path.GetFileName(p).ToLower(); return !n.Contains("metal") && !n.Contains("rough") && !n.Contains("normal"); })
            .OrderByDescending(p => p.ToLower().Contains("base"))
            .ToList();
        string path = MaterialDir + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(mat, path); }
        if (textures.Count > 0) mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(textures[0]);
        else Debug.LogWarning("No base colour texture found next to " + modelPath + "; keeping imported materials.");
        mat.color = Color.white;
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Glossiness", name.Contains("Jetski") ? .55f : .22f);
        EditorUtility.SetDirty(mat);
        return textures.Count > 0 ? mat : null;
    }

    // ---------- animator ----------

    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
    {
        AssetDatabase.DeleteAsset(ControllerPath);
        AssetDatabase.DeleteAsset(MaskPath);

        var mask = new AvatarMask();
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                         part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                         part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers;
            mask.SetHumanoidBodyPartActive(part, upper);
        }
        AssetDatabase.CreateAsset(mask, MaskPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var p in new[] { "LeanLeft", "LeanRight", "Jump", "Land", "Hit", "Fall", "Victory" })
            controller.AddParameter(p, AnimatorControllerParameterType.Trigger);

        var layers = controller.layers;
        layers[0].iKPass = true;
        controller.layers = layers;
        var sm = controller.layers[0].stateMachine;

        AnimatorState State(string name, string clip, string tag, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = clips[clip];
            s.tag = tag;
            return s;
        }
        var ride = State("Ride", "Ride_Loop", "Seated", new Vector3(300, 0));
        var leanL = State("LeanLeft", "Lean_Left", "Seated", new Vector3(560, -120));
        var leanR = State("LeanRight", "Lean_Right", "Seated", new Vector3(560, 120));
        var jump = State("Jump", "Jump", "Seated", new Vector3(300, 200));
        var land = State("Land", "Land", "Seated", new Vector3(560, 260));
        var fall = State("Fall", "Fall", "Thrown", new Vector3(40, 260));
        var victory = State("Victory", "Victory", "Thrown", new Vector3(40, 340));
        State("MenuIdle", "Menu_Idle", "Thrown", new Vector3(40, 420));
        sm.defaultState = ride;

        void FromAny(AnimatorState to, string trigger, float fade)
        {
            var t = sm.AddAnyStateTransition(to);
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.hasExitTime = false; t.duration = fade; t.canTransitionToSelf = false;
        }
        void Back(AnimatorState from, float exit)
        {
            var t = from.AddTransition(ride);
            t.hasExitTime = true; t.exitTime = exit; t.duration = .2f;
        }
        FromAny(leanL, "LeanLeft", .1f); FromAny(leanR, "LeanRight", .1f);
        FromAny(jump, "Jump", .08f); FromAny(land, "Land", .1f);
        FromAny(fall, "Fall", .08f); FromAny(victory, "Victory", .25f);
        Back(leanL, .85f); Back(leanR, .85f); Back(land, .9f);

        // Hit plays on the upper body only, so the pirate stays seated while he flinches.
        controller.AddLayer("UpperBodyHit");
        layers = controller.layers;
        layers[1].avatarMask = mask;
        layers[1].defaultWeight = 1f;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        controller.layers = layers;
        var upper = controller.layers[1].stateMachine;
        var empty = upper.AddState("Empty", new Vector3(300, 0));
        upper.defaultState = empty;
        var hit = upper.AddState("Hit", new Vector3(300, 120));
        hit.motion = clips["Hit_Reaction"];
        var toHit = upper.AddAnyStateTransition(hit);
        toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
        toHit.hasExitTime = false; toHit.duration = .06f; toHit.canTransitionToSelf = false;
        var back = hit.AddTransition(empty);
        back.hasExitTime = true; back.exitTime = .9f; back.duration = .15f;

        EditorUtility.SetDirty(controller);
        return controller;
    }

    // ---------- prefab ----------

    static GameObject BuildPrefab(RuntimeAnimatorController controller)
    {
        var root = new GameObject("MeshyPirateJetski");

        // Jetski: find the nose from the handlebar (highest point sits in the front half), face it to +Z,
        // scale to length and drop the hull so the waterline is at y = 0.
        var jetPivot = new GameObject("Jetski").transform;
        jetPivot.SetParent(root.transform, false);
        var jet = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(JetskiPath), jetPivot, false);
        jet.name = "Jetski_Red_7k";
        var pts = MeshPoints(jet, jetPivot);
        var b = BoundsOf(pts);
        bool alongX = b.size.x > b.size.z;
        var top = pts.OrderByDescending(p => p.y).Take(Mathf.Max(8, pts.Count / 50)).ToList();
        float topAlong = top.Average(p => alongX ? p.x : p.z), mid = alongX ? b.center.x : b.center.z;
        bool noseNegative = topAlong < mid;
        float yaw = alongX ? (noseNegative ? 90f : -90f) : (noseNegative ? 180f : 0f);
        jet.transform.localRotation = Quaternion.Euler(0, yaw, 0) * jet.transform.localRotation;
        b = BoundsOf(MeshPoints(jet, jetPivot));
        jet.transform.localPosition -= new Vector3(b.center.x, b.min.y + Waterline * b.size.y, b.center.z);
        float s = JetskiLength / b.size.z;
        jetPivot.localScale = Vector3.one * s;
        float L = JetskiLength, H = b.size.y * s, W = b.size.x * s, minY = -Waterline * H;
        var jetMat = MakeMaterial("Meshy_Jetski_Red", JetskiPath);
        Restyle(jet, jetMat);

        Transform Anchor(string name, Vector3 local)
        {
            var t = new GameObject(name).transform; t.SetParent(root.transform, false); t.localPosition = local; return t;
        }
        var seat = Anchor("Seat", new Vector3(0, minY + SeatHeight * H, L * .5f - SeatAlong * L));
        var gripL = Anchor("Grip_L", new Vector3(-GripSide * W, minY + GripHeight * H, L * .5f - GripAlong * L));
        var gripR = Anchor("Grip_R", new Vector3(GripSide * W, minY + GripHeight * H, L * .5f - GripAlong * L));

        // Pirate: face +Z (headfront bone points forward), scale to height, hips over the seat.
        var rider = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath), root.transform, false);
        rider.name = "Pirate";
        var head = Find(rider.transform, "Head"); var front = Find(rider.transform, "headfront");
        if (head && front)
        {
            var f = front.position - head.position; f.y = 0;
            if (f.sqrMagnitude > 1e-6f) rider.transform.rotation = Quaternion.FromToRotation(f.normalized, Vector3.forward) * rider.transform.rotation;
        }
        var rb = BoundsOf(MeshPoints(rider, root.transform));
        rider.transform.localScale *= RiderHeight / Mathf.Max(.01f, rb.size.y);
        var hips = Find(rider.transform, "Hips");
        var hipsPos = hips ? hips.position : RendererBounds(rider).center;
        rider.transform.position += seat.position - hipsPos;
        Restyle(rider, MakeMaterial("Meshy_Pirate", CharacterPath));

        var animator = rider.GetComponent<Animator>();
        if (!animator) animator = rider.AddComponent<Animator>();
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterPath).OfType<Avatar>().First();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        rider.AddComponent<PirateRider>().Setup(seat, gripL, gripR);

        foreach (var light in root.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light.gameObject);
        foreach (var cam in root.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam.gameObject);
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void Restyle(GameObject go, Material mat)
    {
        if (!mat) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
    }

    static List<Vector3> MeshPoints(GameObject go, Transform space)
    {
        var pts = new List<Vector3>();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh) foreach (var v in mf.sharedMesh.vertices) pts.Add(space.InverseTransformPoint(mf.transform.TransformPoint(v)));
        foreach (var sk in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (sk.sharedMesh) foreach (var v in sk.sharedMesh.vertices) pts.Add(space.InverseTransformPoint(sk.transform.TransformPoint(v)));
        if (pts.Count == 0) throw new System.Exception(go.name + " has no mesh");
        return pts;
    }

    static Bounds BoundsOf(List<Vector3> pts)
    {
        var b = new Bounds(pts[0], Vector3.zero);
        foreach (var p in pts) b.Encapsulate(p);
        return b;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = Find(c, name); if (f) return f; }
        return null;
    }
}
#endif
