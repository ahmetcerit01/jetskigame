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
    }

    // Lane changes only tilt the jetski; the rider keeps riding (no lean clip by design).
    public void Lean(int direction) { }
    public void Jump() { if (!thrown) { animator.ResetTrigger(LandId); animator.SetTrigger(JumpId); } }
    public void Land() { if (!thrown) animator.SetTrigger(LandId); }
    public void Hit() { if (!thrown) animator.SetTrigger(HitId); }
    public void Fall() { if (thrown) return; thrown = true; animator.SetTrigger(FallId); }
    public void Victory() { animator.SetTrigger(VictoryId); }

    bool Seated => !thrown && animator.GetCurrentAnimatorStateInfo(0).tagHash == SeatedTag;

    void LateUpdate()
    {
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
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0 || !leftGrip || !rightGrip) return;
        handWeight = Mathf.MoveTowards(handWeight, Seated ? 1f : 0f, Time.deltaTime * ikBlendSpeed);
        Pin(AvatarIKGoal.LeftHand, leftGrip);
        Pin(AvatarIKGoal.RightHand, rightGrip);
    }

    void Pin(AvatarIKGoal goal, Transform grip)
    {
        // Position only: the ride clips already hold the wrists in a grip pose.
        animator.SetIKPositionWeight(goal, handWeight);
        animator.SetIKPosition(goal, grip.position);
    }
}
