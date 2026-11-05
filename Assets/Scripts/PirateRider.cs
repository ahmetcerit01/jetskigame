using UnityEngine;

// Drives the Meshy pirate on the jetski: plays the ride / lean / jump / land / hit / fall clips,
// keeps the hips on the seat and pins both hands to the handlebar grips with humanoid IK.
[RequireComponent(typeof(Animator))]
public sealed class PirateRider : MonoBehaviour
{
    [SerializeField] Transform seat;
    [SerializeField] Transform leftGrip;
    [SerializeField] Transform rightGrip;
    [SerializeField] float seatLift = .04f;
    [SerializeField] float ikBlendSpeed = 8f;

    static readonly int JumpId = Animator.StringToHash("Jump");
    static readonly int LandId = Animator.StringToHash("Land");
    static readonly int HitId = Animator.StringToHash("Hit");
    static readonly int FallId = Animator.StringToHash("Fall");
    static readonly int VictoryId = Animator.StringToHash("Victory");
    static readonly int SeatedTag = Animator.StringToHash("Seated");

    Animator animator;
    Transform hips;
    float handWeight = 1f;
    int settleFrames = 3;
    bool thrown;

    public void Setup(Transform seatAnchor, Transform left, Transform right)
    {
        seat = seatAnchor; leftGrip = left; rightGrip = right;
    }

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (animator.isHuman)
        {
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        }
    }

    // Lane changes only tilt the jetski; the rider keeps riding (no lean clip by design).
    public void Lean(int direction) { }
    public void Jump() { if (!thrown) { animator.ResetTrigger(LandId); animator.SetTrigger(JumpId); } }
    public void Land() { if (!thrown) animator.SetTrigger(LandId); }
    public void Hit() { if (!thrown) animator.SetTrigger(HitId); }
    public void Fall() { if (thrown) return; thrown = true; animator.SetTrigger(FallId); }
    public void Victory() { animator.SetTrigger(VictoryId); }
    // Back on the seat after a paid continue.
    public void Revive()
    {
        thrown = false; animator.Rebind(); animator.Update(0f); handWeight = 1f;
        if (flying || landed)
        {
            transform.SetParent(seatedParent, false);
            transform.localPosition = seatedPosition; transform.localRotation = seatedRotation; transform.localScale = seatedScale;
        }
        flying = landed = false;
    }

    // Flung off the jetski: an arc with a tumble, a splash where it hits the water, then floating there.
    const float ThrowGravity = 19f, FloatDepth = .55f, FloatBob = .07f;
    bool flying, landed;
    Vector3 flightVelocity, flightSpin, seatedPosition, seatedScale;
    Transform seatedParent;
    Quaternion seatedRotation;
    float waterLevel;
    System.Action<Vector3> onSplash;

    public void Throw(Vector3 velocity, float water, System.Action<Vector3> splash)
    {
        if (flying || landed) return;
        seatedPosition = transform.localPosition; seatedRotation = transform.localRotation; seatedScale = transform.localScale;
        seatedParent = transform.parent; transform.SetParent(null, true);   // from here on it moves on its own, apart from the jetski
        flightVelocity = velocity; waterLevel = water; onSplash = splash; flying = true;
        flightSpin = new Vector3(Random.Range(260f, 430f), Random.Range(-90f, 90f), Random.Range(-140f, 140f));
    }

    // Where and when a throw with this velocity reaches the water.
    public Vector3 ThrowLanding(Vector3 velocity, float water, out float seconds)
    {
        var from = transform.position;
        seconds = (velocity.y + Mathf.Sqrt(velocity.y * velocity.y + 2f * ThrowGravity * Mathf.Max(0f, from.y - (water - FloatDepth)))) / ThrowGravity;
        return new Vector3(from.x + velocity.x * seconds, water, from.z + velocity.z * seconds);
    }

    void UpdateFlight()
    {
        if (flying)
        {
            flightVelocity += Vector3.down * ThrowGravity * Time.deltaTime;
            transform.position += flightVelocity * Time.deltaTime;
            transform.Rotate(flightSpin * Time.deltaTime, Space.World);
            if (transform.position.y > waterLevel - FloatDepth) return;
            flying = false; landed = true;
            onSplash?.Invoke(transform.position);
        }
        if (landed)
        {
            var p = transform.position; p.y = waterLevel - FloatDepth + Mathf.Sin(Time.time * 2.4f) * FloatBob; transform.position = p;
        }
    }
    // 0 = riding upright, 1 = folded low over the handlebar (ducking under hanging obstacles).
    public float Tuck { get; set; }
    float tuckShown;
    Transform spine, chest, neck;

    bool Seated => !thrown && animator.GetCurrentAnimatorStateInfo(0).tagHash == SeatedTag;

    void LateUpdate()
    {
        UpdateFlight();
        UpdateHeldMagnet();
        // The editor setup places the rider from the bind pose; once the first ride frame is posed,
        // shift the whole rig so the hips sit exactly on the seat. Done once so the hand IK stays stable.
        if (settleFrames > 0 && hips && seat && Seated)
        {
            if (--settleFrames == 0)
            {
                Vector3 delta = seat.position + seat.up * seatLift - hips.position;
                transform.position += delta;
            }
        }
        // Tuck: bend the spine forward on top of the animated pose (bend axis = the rider's right).
        tuckShown = Mathf.MoveTowards(tuckShown, thrown ? 0f : Tuck, Time.deltaTime * 7f);
        if (tuckShown > .001f)
        {
            var axis = transform.right;
            if (spine) spine.Rotate(axis, 28f * tuckShown, Space.World);
            if (chest) chest.Rotate(axis, 22f * tuckShown, Space.World);
            if (neck) neck.Rotate(axis, -18f * tuckShown, Space.World);   // keep looking ahead
        }
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !leftGrip || !rightGrip) return;
        handWeight = Mathf.MoveTowards(handWeight, Seated ? 1f : 0f, Time.deltaTime * ikBlendSpeed);
        Pin(AvatarIKGoal.LeftHand, leftGrip);
        Pin(AvatarIKGoal.RightHand, rightGrip);
        magnetShown = Mathf.MoveTowards(magnetShown, HoldMagnet && !thrown ? 1f : 0f, Time.deltaTime * 5f);
        if (magnetShown > 0f)
        {
            // the right hand leaves the grip and reaches out ahead, towards the coins
            float eased = Mathf.SmoothStep(0f, 1f, magnetShown);
            // aim well past arm's length from the shoulder, so the arm goes out straight
            var shoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var reach = shoulder.position + MagnetAim * 3f + Vector3.up * Mathf.Sin(Time.time * 5f) * .06f;
            animator.SetIKPosition(AvatarIKGoal.RightHand, Vector3.Lerp(rightGrip.position, reach, eased));
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, Mathf.Max(handWeight, eased));
        }
    }

    // ---------- the magnet power-up: held out in the right hand while it lasts ----------

    public bool HoldMagnet { get; set; }
    float magnetShown;
    Vector3 MagnetAim => (transform.forward * .8f + transform.right * .5f + Vector3.up * .22f).normalized;
    Transform heldMagnet;

    void UpdateHeldMagnet()
    {
        bool show = magnetShown > .02f;
        if (!show) { if (heldMagnet && heldMagnet.gameObject.activeSelf) heldMagnet.gameObject.SetActive(false); return; }
        var hand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (!hand) return;
        if (!heldMagnet) heldMagnet = BuildMagnet();
        if (!heldMagnet.gameObject.activeSelf) heldMagnet.gameObject.SetActive(true);
        heldMagnet.position = hand.position + MagnetAim * .1f;
        heldMagnet.rotation = Quaternion.LookRotation(MagnetAim, Vector3.up);   // poles towards the coins, whatever the hand bone's own angle
        heldMagnet.localScale = Vector3.one * Mathf.SmoothStep(0f, MagnetSize, magnetShown);
    }

    Transform BuildMagnet()
    {
        var root = new GameObject("Held magnet").transform;
        var red = new Color(.9f, .08f, .06f);
        foreach (float x in new[] { -.36f, .36f })
        {
            MagnetPart(root, PrimitiveType.Cube, new Vector3(x, 0, .22f), new Vector3(.3f, .32f, .7f), red);
            MagnetPart(root, PrimitiveType.Cube, new Vector3(x, 0, .66f), new Vector3(.31f, .33f, .2f), Color.white);
        }
        MagnetPart(root, PrimitiveType.Cube, new Vector3(0, 0, -.2f), new Vector3(1.02f, .32f, .3f), red);
        return root;
    }

    static void MagnetPart(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, Color color)
    {
        var part = GameObject.CreatePrimitive(type);
        Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = at; part.transform.localScale = scale;
        part.GetComponent<Renderer>().material.color = color;
    }

    const float MagnetSize = .3f;

    void Pin(AvatarIKGoal goal, Transform grip)
    {
        // Position only: the ride clips already hold the wrists in a grip pose.
        animator.SetIKPositionWeight(goal, handWeight);
        animator.SetIKPosition(goal, grip.position);
    }
}
