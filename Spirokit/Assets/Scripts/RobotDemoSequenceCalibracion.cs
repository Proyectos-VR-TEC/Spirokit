using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Robot demo with two plain hands: for each step, the piece (e.g. nozzle filter) glows on the tray, the robot
/// reaches it with its right hand, left hand or both, the piece rests on the hand(s) and follows them while they
/// carry it to the snap point on the flow head, where it slides in with a small twist, a flash and a click.
///
/// Starts automatically when SparkleTrailChain finishes (see Auto Start After Chain), or call Begin() yourself.
/// The hands can be moved by this script (Script Moves Hands) or by your own Animator via triggers.
/// </summary>
public class RobotDemoSequence : MonoBehaviour
{
    public enum HandUse { Right, Left, Both }

    [System.Serializable]
    public class Hand
    {
        [Tooltip("Empty object parented to this hand, placed where a piece should rest (palm center, a bit above the palm). " +
                 "Keep its scale at 1 and don't put it under a scaled object.")]
        public Transform anchor;
        [Tooltip("The object this script moves to carry things: the hand itself, or the arm tip. Must contain the Anchor.")]
        public Transform root;
    }

    [System.Serializable]
    public class Step
    {
        public string name;

        [Tooltip("The piece on the floating tray that the robot picks up.")]
        public Transform piece;
        [Tooltip("Empty object on the flow head where the piece ends up. Its blue (Z) arrow should point OUT of the " +
                 "flow head: the piece arrives from that side, pushes in against the arrow and twists to seat.")]
        public Transform snapPoint;
        [Tooltip("Which hand(s) carry this piece. Both = the piece rests between the two hands.")]
        public HandUse hands = HandUse.Right;

        [Header("Light effect on the piece")]
        [Tooltip("Optional light near the piece that glows up when the robot starts to show it and fades after it is placed.")]
        public Light highlightLight;
        public float highlightIntensity = 20f;

        [Header("How the piece rests on the hand(s)")]
        [Tooltip("Position of the piece relative to the Hand Anchor (or, with Both, to the point halfway between the hands).")]
        public Vector3 holdLocalPosition = Vector3.zero;
        [Tooltip("Rotation (degrees) of the piece relative to the Hand Anchor (or the midpoint with Both).")]
        public Vector3 holdLocalEuler = Vector3.zero;
        [Tooltip("Both hands only: distance in meters between the two hands while holding. 0 = keep the distance they have now.")]
        public float bothHandsGap = 0f;

        [Header("Placing on the flow head")]
        [Tooltip("How far in front of the snap point (along its blue arrow, in meters) the hands stop before the piece slides in.")]
        public float approachDistance = 0.12f;
        [Tooltip("The piece starts the slide-in twisted by this many degrees and untwists as it seats (screw-in feel). 0 = no twist.")]
        public float twistDegrees = 120f;
        [Tooltip("Axis of the twist, in the snap point's local space (usually Z, the push-in direction).")]
        public Vector3 twistAxis = Vector3.forward;
        [Tooltip("ON: the piece stays a child of the snap point (moves with the flow head). OFF: it goes back to its original parent.")]
        public bool stayAttached = true;

        [Header("Animator triggers for this step (optional, override the defaults below)")]
        public string reachTrigger;
        public string carryTrigger;
        public string releaseTrigger;
    }

    [Header("Start")]
    public SparkleTrailChain chain;
    [Tooltip("Subscribe to the chain's finale automatically. Turn off if you call Begin() from the chain's On Chain Finished event yourself.")]
    public bool autoStartAfterChain = true;
    public float startDelay = 1.5f;

    [Header("Steps (in order)")]
    public Step[] steps;

    [Header("Robot hands (plain hands: the piece rests on / against the palm)")]
    public Hand rightHand;
    public Hand leftHand;
    [Tooltip("ON: this script moves the Hand Roots. OFF: your Animator moves the hands (use the triggers below) " +
             "and the durations are just how long the script waits.")]
    public bool scriptMovesHands = true;
    public float handArcHeight = 0.15f;

    [Header("Timing (seconds)")]
    public float highlightPause = 1.2f;    // the piece glows alone before the robot reaches for it
    public float lightFadeDuration = 1f;
    public float reachDuration = 1.5f;     // hand(s) go to the piece
    public float grabDuration = 0.4f;      // piece settles onto the hand(s)
    public float carryDuration = 2f;       // hand(s) carry the piece to the flow head
    public float alignDuration = 0.4f;     // piece lines up with the snap point
    public float seatDuration = 0.5f;      // piece slides in and untwists
    public float returnDuration = 1.5f;    // hand(s) go back home
    public float pauseBetweenSteps = 0.8f;

    [Header("Animator (optional)")]
    public Animator robotAnimator;
    [Tooltip("Default Animator trigger names, fired when each phase starts. Leave empty to skip. A step can override them.")]
    public string reachTrigger;
    public string carryTrigger;
    public string releaseTrigger;

    [Header("Snap feedback")]
    public Light snapFlashLight;           // optional: briefly flashes when a piece clicks in
    public float snapFlashIntensity = 30f;
    public float snapFlashDuration = 0.4f;
    public AudioSource sfx;
    public AudioClip grabClip;
    public AudioClip snapClip;

    [Header("Events")]
    public UnityEvent onSequenceFinished;

    struct RootMove { public Transform root; public Vector3 from, to; }

    bool running;
    Vector3 homeRightPos, homeLeftPos;
    Quaternion homeRightRot, homeLeftRot;
    Transform[] origParent;
    Vector3[] origPos;
    Quaternion[] origRot;
    Vector3[] origScale;
    bool[] origKinematic;

    Transform carrier;                     // sits halfway between the two hands when both carry a piece
    bool carrierActive;

    void Awake()
    {
        var go = new GameObject("BothHandsCarrier");
        carrier = go.transform;
        carrier.SetParent(transform, false);
    }

    void Start()
    {
        if (chain && autoStartAfterChain) chain.onChainFinished.AddListener(Begin);
        SetLight(snapFlashLight, 0f);
        if (steps != null) foreach (var s in steps) if (s != null) SetLight(s.highlightLight, 0f);
    }

    void OnDestroy()
    {
        if (chain) chain.onChainFinished.RemoveListener(Begin);
    }

    void LateUpdate()
    {
        if (!carrierActive || !HandOk(rightHand) || !HandOk(leftHand)) return;
        carrier.position = (rightHand.anchor.position + leftHand.anchor.position) * 0.5f;
        carrier.rotation = rightHand.anchor.rotation;
    }

    // Hook this to SparkleTrailChain's On Chain Finished (or let Auto Start do it).
    public void Begin()
    {
        if (running || steps == null || steps.Length == 0) return;
        running = true;
        CaptureOriginals();
        StartCoroutine(Run());
    }

    public void ResetSequence()
    {
        StopAllCoroutines();
        running = false;
        carrierActive = false;

        if (origParent != null)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (s == null || !s.piece) continue;
                s.piece.SetParent(origParent[i], true);
                s.piece.position = origPos[i];
                s.piece.rotation = origRot[i];
                s.piece.localScale = origScale[i];
                var rb = s.piece.GetComponent<Rigidbody>();
                if (rb) rb.isKinematic = origKinematic[i];
                SetLight(s.highlightLight, 0f);
            }
            if (rightHand.root) { rightHand.root.position = homeRightPos; rightHand.root.rotation = homeRightRot; }
            if (leftHand.root) { leftHand.root.position = homeLeftPos; leftHand.root.rotation = homeLeftRot; }
        }
        SetLight(snapFlashLight, 0f);
        if (sfx) sfx.Stop();
    }

    void CaptureOriginals()
    {
        int n = steps.Length;
        origParent = new Transform[n];
        origPos = new Vector3[n];
        origRot = new Quaternion[n];
        origScale = new Vector3[n];
        origKinematic = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var s = steps[i];
            if (s == null || !s.piece) continue;
            origParent[i] = s.piece.parent;
            origPos[i] = s.piece.position;
            origRot[i] = s.piece.rotation;
            origScale[i] = s.piece.localScale;
            var rb = s.piece.GetComponent<Rigidbody>();
            origKinematic[i] = rb && rb.isKinematic;
        }
        if (rightHand.root) { homeRightPos = rightHand.root.position; homeRightRot = rightHand.root.rotation; }
        if (leftHand.root) { homeLeftPos = leftHand.root.position; homeLeftRot = leftHand.root.rotation; }
    }

    IEnumerator Run()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i] != null && steps[i].piece && steps[i].snapPoint)
            {
                yield return DoStep(i);
                if (pauseBetweenSteps > 0f) yield return new WaitForSeconds(pauseBetweenSteps);
            }
            else
            {
                Debug.LogWarning($"[RobotDemoSequence] Step {i} is missing its Piece or Snap Point, skipping it.");
            }
        }

        onSequenceFinished.Invoke();
        // 'running' stays true so it can't replay by accident; call ResetSequence() to replay.
    }

    IEnumerator DoStep(int i)
    {
        Step s = steps[i];
        Transform p = s.piece;
        bool both = s.hands == HandUse.Both;
        Hand single = s.hands == HandUse.Left ? leftHand : rightHand;   // used when not Both

        bool handsOk = both ? (HandOk(rightHand) && HandOk(leftHand)) : HandOk(single);
        if (!handsOk)
            Debug.LogWarning($"[RobotDemoSequence] Step {i}: the hand(s) it needs have no Anchor" +
                             (scriptMovesHands ? " or Root" : "") + " assigned.");
        bool move = scriptMovesHands && handsOk;

        // Where each hand's anchor goes, relative to the point the piece is carried to.
        Vector3 rightOffset = Vector3.zero, leftOffset = Vector3.zero;
        if (both && handsOk)
        {
            Vector3 side = rightHand.anchor.position - leftHand.anchor.position;
            float gap = s.bothHandsGap > 0f ? s.bothHandsGap : side.magnitude;
            Vector3 half = (side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.right) * gap * 0.5f;
            rightOffset = half;
            leftOffset = -half;
        }

        var rb = p.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        // 1. The piece glows on the tray.
        if (s.highlightLight) StartCoroutine(FadeLight(s.highlightLight, s.highlightIntensity, lightFadeDuration));
        if (highlightPause > 0f) yield return new WaitForSeconds(highlightPause);

        // 2. The hand(s) reach the piece.
        Fire(Pick(s.reachTrigger, reachTrigger));
        if (move) yield return MoveHandsTo(s, both, single, p.position, rightOffset, leftOffset, reachDuration);
        else yield return new WaitForSeconds(reachDuration);

        // 3. The piece settles onto the hand(s) and follows them from now on.
        PlayClip(grabClip);
        if (handsOk)
        {
            Transform holder;
            if (both)
            {
                carrier.position = (rightHand.anchor.position + leftHand.anchor.position) * 0.5f;
                carrier.rotation = rightHand.anchor.rotation;
                carrierActive = true;
                holder = carrier;
            }
            else holder = single.anchor;

            p.SetParent(holder, true);
            yield return LerpLocalPose(p, s.holdLocalPosition, Quaternion.Euler(s.holdLocalEuler), grabDuration);
        }

        // 4. The hand(s) carry it in front of the snap point.
        Vector3 approach = s.snapPoint.position + s.snapPoint.forward * s.approachDistance;
        Fire(Pick(s.carryTrigger, carryTrigger));
        if (move) yield return MoveHandsTo(s, both, single, approach, rightOffset, leftOffset, carryDuration);
        else yield return new WaitForSeconds(carryDuration);

        // 5. The piece leaves the hand(s), lines up, then slides in and untwists.
        carrierActive = false;
        p.SetParent(s.snapPoint, true);
        Vector3 approachLocal = s.snapPoint.InverseTransformPoint(approach);
        Quaternion twisted = Quaternion.AngleAxis(s.twistDegrees, s.twistAxis);
        yield return LerpLocalPose(p, approachLocal, twisted, alignDuration);
        yield return LerpLocalPose(p, Vector3.zero, Quaternion.identity, seatDuration);

        // 6. Click: sound, flash, and the tray glow fades.
        PlayClip(snapClip);
        if (snapFlashLight) StartCoroutine(Flash(snapFlashLight, snapFlashIntensity, snapFlashDuration));
        if (s.highlightLight) StartCoroutine(FadeLight(s.highlightLight, 0f, lightFadeDuration));
        if (!s.stayAttached) p.SetParent(origParent[i], true);

        // 7. The hand(s) go back home.
        Fire(Pick(s.releaseTrigger, releaseTrigger));
        if (move) yield return ReturnHands(both, single, returnDuration);
        else yield return new WaitForSeconds(returnDuration);
    }

    // ---------- Movement helpers ----------

    static bool HandOk(Hand h) { return h != null && h.anchor != null; }

    // Moves the Hand Root(s) so the Hand Anchor(s) end up at 'point' (plus each hand's offset when both carry).
    IEnumerator MoveHandsTo(Step s, bool both, Hand single, Vector3 point, Vector3 rightOffset, Vector3 leftOffset, float duration)
    {
        RootMove[] moves;
        if (both)
        {
            moves = new[]
            {
                MakeMove(rightHand, point + rightOffset),
                MakeMove(leftHand, point + leftOffset)
            };
        }
        else moves = new[] { MakeMove(single, point) };

        yield return RunMoves(moves, duration, handArcHeight);
    }

    IEnumerator ReturnHands(bool both, Hand single, float duration)
    {
        RootMove[] moves;
        if (both)
        {
            moves = new[]
            {
                new RootMove { root = rightHand.root, from = rightHand.root.position, to = homeRightPos },
                new RootMove { root = leftHand.root, from = leftHand.root.position, to = homeLeftPos }
            };
        }
        else
        {
            bool isLeft = single == leftHand;
            moves = new[]
            {
                new RootMove { root = single.root, from = single.root.position, to = isLeft ? homeLeftPos : homeRightPos }
            };
        }
        yield return RunMoves(moves, duration, handArcHeight * 0.5f);
    }

    // The hand isn't rotated, so the anchor keeps a constant offset from its root.
    static RootMove MakeMove(Hand h, Vector3 anchorTarget)
    {
        Vector3 anchorOffset = h.anchor.position - h.root.position;
        return new RootMove { root = h.root, from = h.root.position, to = anchorTarget - anchorOffset };
    }

    IEnumerator RunMoves(RootMove[] moves, float duration, float arc)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            foreach (var m in moves)
            {
                Vector3 pos = Vector3.Lerp(m.from, m.to, k);
                pos += Vector3.up * Mathf.Sin(k * Mathf.PI) * arc;
                m.root.position = pos;
            }
            yield return null;
        }
        foreach (var m in moves) m.root.position = m.to;
    }

    IEnumerator LerpLocalPose(Transform t, Vector3 toPos, Quaternion toRot, float duration)
    {
        Vector3 fromPos = t.localPosition;
        Quaternion fromRot = t.localRotation;
        float k = 0f;
        while (k < 1f)
        {
            k += Time.deltaTime / Mathf.Max(0.01f, duration);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            t.localPosition = Vector3.Lerp(fromPos, toPos, e);
            t.localRotation = Quaternion.Slerp(fromRot, toRot, e);
            yield return null;
        }
        t.localPosition = toPos;
        t.localRotation = toRot;
    }

    // ---------- Light / sound / animator helpers ----------

    IEnumerator FadeLight(Light l, float to, float duration)
    {
        if (!l) yield break;
        if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        l.enabled = true;
        float from = l.intensity;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            l.intensity = Mathf.Lerp(from, to, Mathf.Clamp01(t));
            yield return null;
        }
        l.intensity = to;
    }

    IEnumerator Flash(Light l, float peak, float duration)
    {
        if (!l) yield break;
        if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
        l.enabled = true;
        float half = Mathf.Max(0.01f, duration) * 0.5f;
        float t = 0f;
        while (t < half) { t += Time.deltaTime; l.intensity = Mathf.Lerp(0f, peak, t / half); yield return null; }
        t = 0f;
        while (t < half) { t += Time.deltaTime; l.intensity = Mathf.Lerp(peak, 0f, t / half); yield return null; }
        l.intensity = 0f;
    }

    static void SetLight(Light l, float intensity)
    {
        if (l) l.intensity = intensity;
    }

    static string Pick(string stepValue, string defaultValue)
    {
        return string.IsNullOrEmpty(stepValue) ? defaultValue : stepValue;
    }

    void Fire(string trigger)
    {
        if (robotAnimator && !string.IsNullOrEmpty(trigger)) robotAnimator.SetTrigger(trigger);
    }

    void PlayClip(AudioClip c)
    {
        if (sfx && c) sfx.PlayOneShot(c);
    }
}