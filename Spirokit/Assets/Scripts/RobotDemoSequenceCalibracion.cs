using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Teaches the user how to place the pieces, in two phases:
///
/// 1) ROBOT DEMO: for each step a translucent copy of the piece (made automatically, the real piece stays on the
///    tray) lifts off and FLIES by itself (nothing is held by the robot) to the snap point. There it moves in and
///    out of place a few times, showing how the piece has to be pushed in, and finishes with a click.
///    All the motion is computed from the snap point's position, rotation and approach direction.
///
/// 2) USER'S TURN: the snap points (each appears as a translucent ghost as soon as the robot has shown its piece) show where the pieces go,
///    and a light glows on each real piece on the tray. The user grabs a piece (XR Grab Interactable) and lets go
///    of it near its ghost: it slides in and clicks into place. A piece dropped far away glides back to the tray.
///
/// The snap points can (and should) be DISABLED in the scene: this script switches them on only when it needs them.
///
/// Starts automatically when SparkleTrailChain finishes (Auto Start After Chain), when this object gets enabled
/// (Begin When Activated), or when you call Begin().
/// </summary>
public class RobotDemoSequence : MonoBehaviour
{
    [System.Serializable]
    public class Step
    {
        public string name;

        [Tooltip("The REAL piece on the floating tray. The demo only uses a ghost copy of it; the user grabs this one.")]
        public Transform piece;
        [Tooltip("Where the piece ends up: a DUPLICATE of the piece placed exactly where it should end up (can be disabled in the scene), " +
                 "or an empty object. The piece takes the position and rotation of this object.")]
        public Transform snapPoint;
        [Tooltip("ON (use this with a duplicated piece): the Snap Point appears as a translucent ghost as soon as the robot's copy has shown that piece, and " +
                 "stays visible while the next piece is demonstrated; it " +
                 "disappears when the user's piece seats. Its colliders, lights and touch/interaction components are switched off " +
                 "automatically. Leave OFF if the Snap Point is an empty object or part of the real flow head (its renderers would get hidden).")]
        public bool snapPointIsGhost = true;

        [Header("Ghost copy that flies in the demo (optional)")]
        [Tooltip("Leave empty to have a ghost copy of the piece made automatically (see Auto Create Demo Copy). " +
                 "Or assign your own duplicate of the piece.")]
        public Transform demoCopy;

        [Header("Lights on the piece")]
        [Tooltip("Light near the piece. It glows while the demo shows the piece, fades afterwards, and (unless you set a " +
                 "separate Guide Light) glows again in the user's turn until the piece is grabbed.")]
        public Light highlightLight;
        [Tooltip("Optional separate light for the user's turn. Empty = the Highlight Light is used again.")]
        public Light userGuideLight;
        [Tooltip("Intensity of the Highlight Light and of the Guide Light.")]
        public float highlightIntensity = 20f;

        [Header("How the piece is shown going in")]
        [Tooltip("How far from the snap point (in meters) the copy hovers before moving in, and the distance it pulls back each time.")]
        public float approachDistance = 0.12f;
        [Tooltip("Direction the piece comes in FROM, relative to the snap point. Default (0,0,1) = along the snap point's blue arrow. " +
                 "With a duplicated piece the model's own axes can be arbitrary: tick World Space below and use e.g. (0,1,0) = from above.")]
        public Vector3 approachDirection = Vector3.forward;
        [Tooltip("ON: Approach Direction is in world axes (e.g. (0,1,0) = comes from above). OFF: it is relative to the snap point.")]
        public bool approachDirectionIsWorld = false;
        [Tooltip("ON: the piece turns to match the snap point's rotation. OFF: it keeps its own rotation and only moves.")]
        public bool alignRotationToSnap = true;
        [Tooltip("ON: the piece also takes the snap point's size (useful if the tray piece was resized). OFF (default): scale is never touched.")]
        public bool matchScaleToSnap = false;
        [Tooltip("Optional screw-in feel: while hovering the copy is twisted by this many degrees and untwists as it moves in. 0 = no twist.")]
        public float twistDegrees = 0f;
        [Tooltip("Axis of the twist, in the snap point's local space (usually Z, the push-in direction).")]
        public Vector3 twistAxis = Vector3.forward;

        [Header("User's turn")]
        [Tooltip("Letting go of the piece within this distance (meters) of the snap point makes it slide in and click.")]
        public float userSnapRadius = 0.2f;
        [Tooltip("ON: after the user seats the real piece, it becomes a child of the snap point so it moves with the flow head " +
                 "(world position, rotation and scale are kept). OFF (default): it is left where it is, with its own parent.")]
        public bool stayAttached = false;

        [Header("Animator triggers for this step (optional, override the defaults below)")]
        public string startTrigger;
        public string endTrigger;
    }

    [Header("Start")]
    public SparkleTrailChain chain;
    [Tooltip("Subscribe to the chain's finale automatically. Needs this object to be enabled from the beginning.")]
    public bool autoStartAfterChain = true;
    [Tooltip("Tick this if you keep this object DISABLED and enable it later (e.g. from the chain's On Chain Finished event): " +
             "the demo starts as soon as the object is enabled.")]
    public bool beginWhenActivated = false;
    public float startDelay = 1.5f;

    [Header("Steps (in order)")]
    public Step[] steps;

    [Header("Ghost look")]
    [Tooltip("Optional material for the ghosts (the flying copies and the snap points), e.g. a glowing hologram material. " +
             "Empty = a translucent material is created automatically (needs URP).")]
    public Material ghostMaterial;
    [Tooltip("Only used when Ghost Material is empty: the color and transparency (alpha) of the automatic ghost material.")]
    public Color ghostColor = new Color(0.35f, 0.9f, 1f, 0.35f);
    [Tooltip("ON: when a step has no Demo Copy assigned, a ghost copy of the piece is created automatically at start.")]
    public bool autoCreateDemoCopy = true;
    [Range(0f, 1f), Tooltip("How much the snap point ghosts pulse (0 = steady). Works when the ghost material has a Base Color or Color.")]
    public float snapGhostPulse = 0.35f;

    [Header("Robot demo motion (seconds / meters)")]
    public float highlightPause = 1.2f;    // the piece glows alone before its copy lifts off
    public float lightFadeDuration = 1f;
    public float liftHeight = 0.15f;       // the copy rises this much above the tray piece
    public float liftDuration = 0.8f;
    public float flightDuration = 1.8f;    // copy flies from the tray to the front of the snap point
    public float flightArcHeight = 0.1f;
    [Tooltip("How many times the copy moves in, showing how to place it. The last one ends with the click.")]
    public int insertRepeats = 2;
    public float insertDuration = 0.7f;    // moving in towards the snap point
    public float insertPause = 0.35f;      // short pause when it's in
    public float pullBackDuration = 0.6f;  // moving back out
    public float finalHold = 0.8f;         // stays seated after the last click, before disappearing
    public float hoverBobAmount = 0.01f;   // gentle floating up and down while it waits
    public float hoverBobSpeed = 3f;
    public float pauseBetweenSteps = 0.8f;
    [Tooltip("Time the real piece takes to slide into the snap point when the user lets go near it.")]
    public float seatDuration = 0.5f;

    [Header("User's turn")]
    public bool userTurnAfterDemo = true;
    public float userTurnDelay = 1.5f;
    [Tooltip("Played when the user's turn starts (e.g. the robot saying 'now you').")]
    public AudioClip yourTurnClip;
    public AudioClip allDoneClip;
    [Tooltip("ON: a piece the user is holding doesn't rotate with the hand; it keeps its own rotation while it moves.")]
    public bool lockRotationWhileGrabbed = true;
    [Tooltip("ON: as soon as the user grabs a piece, it turns by itself to the rotation shown in the demo (the snap point's rotation) " +
             "and keeps it while held. Only for steps with Align Rotation To Snap on.")]
    public bool rotateToDemoOnGrab = true;
    [Tooltip("How fast (degrees per second) a grabbed piece turns to the demo rotation.")]
    public float grabRotateSpeed = 360f;
    [Tooltip("ON: the piece links in the moment it gets close to its ghost, without having to let go.")]
    public bool snapWhileHeld = true;
    [Tooltip("Distance (meters) from the snap point at which a HELD piece links in. Keep it smaller than the distance between the tray and the ghost, " +
             "or the piece would link in as soon as it is grabbed. Letting go still uses each step's User Snap Radius.")]
    public float heldSnapRadius = 0.1f;
    public float guidePulseSpeed = 3f;
    [Range(0f, 1f)] public float guidePulseAmount = 0.25f;
    [Tooltip("A piece let go far from the snap point glides back to the tray.")]
    public bool returnDroppedPieces = true;
    public float returnDelay = 1f;
    public float returnToTrayDuration = 0.8f;
    public UnityEvent onUserTurnStarted;
    [Tooltip("Fires when the user has placed every piece.")]
    public UnityEvent onUserFinished;

    [Header("Robot Animator (optional)")]
    public Animator robotAnimator;
    [Tooltip("Default Animator triggers: startTrigger fires when a piece's demo begins, endTrigger when its click happens. " +
             "Leave empty to skip. A step can override them.")]
    public string startTrigger;
    public string endTrigger;

    [Header("Click feedback")]
    public Light snapFlashLight;           // optional: briefly flashes when a piece clicks in
    public float snapFlashIntensity = 30f;
    public float snapFlashDuration = 0.4f;
    public AudioSource sfx;
    public AudioClip grabClip;
    public AudioClip snapClip;

    [Header("Events")]
    [Tooltip("Fires when the robot demo has finished (before the user's turn).")]
    public UnityEvent onSequenceFinished;

    bool running;

    // Original state of the real pieces (tray pose), captured when the sequence begins.
    Transform[] origParent;
    Vector3[] origPos;
    Quaternion[] origRot;
    Vector3[] origScale;
    bool[] origKinematic;
    bool[] origGravity;

    Transform demoHolder;                  // inactive parent used while building demo copies
    Transform[] demoCopies;
    bool[] snapWasActive;

    // Ghost materials: one for the flying copies, and one PER snap point (each pulses on its own).
    Material copyMat;
    string copyAlphaProp;
    Color copyBaseColor;
    Material[] snapMats;
    string snapAlphaProp;
    Color snapBaseColor;
    bool[] snapShown;

    // User's turn state
    bool[] seated, wasHeld, busy, guideOn;
    Behaviour[] grab;
    System.Reflection.PropertyInfo[] heldProp;

    void Awake()
    {
        var h = new GameObject("DemoCopyHolder (inactive)");
        h.SetActive(false);
        h.transform.SetParent(transform, false);
        demoHolder = h.transform;
    }

    void Start()
    {
        if (chain && autoStartAfterChain) chain.onChainFinished.AddListener(Begin);
        SetLight(snapFlashLight, 0f);
        if (steps == null) return;

        copyMat = MakeGhostMaterial();
        if (copyMat)
        {
            copyAlphaProp = copyMat.HasProperty("_BaseColor") ? "_BaseColor" : copyMat.HasProperty("_Color") ? "_Color" : null;
            if (copyAlphaProp != null) copyBaseColor = copyMat.GetColor(copyAlphaProp);
        }
        snapMats = new Material[steps.Length];
        snapShown = new bool[steps.Length];

        demoCopies = new Transform[steps.Length];
        snapWasActive = new bool[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            if (s == null) continue;
            SetLight(s.highlightLight, 0f);
            SetLight(s.userGuideLight, 0f);

            if (s.snapPoint)
            {
                snapWasActive[i] = s.snapPoint.gameObject.activeSelf;
                if (s.snapPointIsGhost)
                {
                    snapMats[i] = MakeGhostMaterial();
                    if (snapMats[i] && snapAlphaProp == null)
                    {
                        snapAlphaProp = snapMats[i].HasProperty("_BaseColor") ? "_BaseColor" : snapMats[i].HasProperty("_Color") ? "_Color" : null;
                        if (snapAlphaProp != null) snapBaseColor = snapMats[i].GetColor(snapAlphaProp);
                    }
                    PrepareGhost(s.snapPoint, snapMats[i]);   // stays disabled/hidden until needed
                }
            }

            if (s.demoCopy) demoCopies[i] = s.demoCopy;
            else if (autoCreateDemoCopy && s.piece) demoCopies[i] = CreateDemoCopy(s.piece);
            if (demoCopies[i]) PrepareGhost(demoCopies[i], copyMat);
        }

        if (beginWhenActivated) Begin();
    }

    void OnDestroy()
    {
        if (chain) chain.onChainFinished.RemoveListener(Begin);
    }

    void Update()
    {
        UpdateSnapGhostAlpha();
    }

    // Hook this to SparkleTrailChain's On Chain Finished (or let Auto Start / Begin When Activated do it).
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
        if (snapShown != null) System.Array.Clear(snapShown, 0, snapShown.Length);

        if (origParent != null)
        {
            for (int i = 0; i < steps.Length; i++)
            {
                var s = steps[i];
                if (s == null || !s.piece) continue;
                if (s.piece.parent != origParent[i]) s.piece.SetParent(origParent[i], true);
                s.piece.position = origPos[i];
                s.piece.rotation = origRot[i];
                s.piece.localScale = origScale[i];
                var rb = s.piece.GetComponent<Rigidbody>();
                if (rb) { rb.isKinematic = origKinematic[i]; rb.useGravity = origGravity[i]; }
                if (grab != null && grab[i]) grab[i].enabled = false;
                SetLight(s.highlightLight, 0f);
                SetLight(s.userGuideLight, 0f);
                if (demoCopies != null && demoCopies[i]) SetRenderers(demoCopies[i], false);
            }
        }

        for (int i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            if (s == null || !s.snapPoint || !s.snapPointIsGhost) continue;
            SetRenderers(s.snapPoint, false);
            if (snapWasActive != null && !snapWasActive[i]) s.snapPoint.gameObject.SetActive(false);   // disabled again, as in the scene
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
        origGravity = new bool[n];
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
            origGravity[i] = rb && rb.useGravity;
        }
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

        // Any snap point that hasn't appeared yet (e.g. a skipped step) appears now.
        ShowSnapGhosts();
        onSequenceFinished.Invoke();

        if (userTurnAfterDemo) yield return UserTurn();
        // 'running' stays true so it can't replay by accident; call ResetSequence() to replay.
    }

    // =====================================================================
    //  PHASE 1: the robot demo (a ghost copy flies and shows how to push the piece in)
    // =====================================================================

    IEnumerator DoStep(int i)
    {
        Step s = steps[i];
        Transform p = s.piece;
        Transform c = demoCopies != null ? demoCopies[i] : null;
        if (!c)
        {
            Debug.LogWarning($"[RobotDemoSequence] Step {i} has no demo copy (assign one or turn on Auto Create Demo Copy), the demo skips it.");
            yield break;
        }

        // Everything is computed from the snap point.
        Vector3 dir = s.approachDirection.sqrMagnitude > 0.0001f ? s.approachDirection.normalized : Vector3.forward;
        Vector3 approachDir = s.approachDirectionIsWorld ? dir : s.snapPoint.TransformDirection(dir);
        Vector3 seatPos = s.snapPoint.position;
        Vector3 farPos = seatPos + approachDir * s.approachDistance;

        // 1. The real piece glows; its ghost copy appears right on top of it and starts pulsing right away.
        c.position = p.position;
        c.rotation = p.rotation;
        c.localScale = LocalScaleFor(c, p.lossyScale);
        ShowObject(c);

        Quaternion seatRot = s.alignRotationToSnap ? s.snapPoint.rotation : c.rotation;
        Quaternion farRot = (s.alignRotationToSnap && Mathf.Abs(s.twistDegrees) > 0.01f)
            ? seatRot * Quaternion.AngleAxis(s.twistDegrees, s.twistAxis)
            : seatRot;
        Vector3 seatScale = s.matchScaleToSnap ? LocalScaleFor(c, s.snapPoint.lossyScale) : c.localScale;

        Fire(Pick(s.startTrigger, startTrigger));
        if (s.highlightLight) StartCoroutine(FadeLight(s.highlightLight, s.highlightIntensity, lightFadeDuration));
        yield return Hover(c, highlightPause);

        // 2. The copy lifts off the tray.
        yield return MoveCopy(c, c.position + Vector3.up * liftHeight, c.rotation, c.localScale, liftDuration, 0f);

        // 3. It flies to the front of the snap point.
        yield return MoveCopy(c, farPos, farRot, seatScale, flightDuration, flightArcHeight);
        yield return Hover(c, insertPause);

        // 4. It moves in, and out, and in again: this is how the piece has to be pushed in.
        int reps = Mathf.Max(1, insertRepeats);
        for (int r = 0; r < reps; r++)
        {
            bool last = r == reps - 1;
            yield return MoveCopy(c, seatPos, seatRot, seatScale, insertDuration, 0f);

            if (last)
            {
                PlayClip(snapClip);
                if (snapFlashLight) StartCoroutine(Flash(snapFlashLight, snapFlashIntensity, snapFlashDuration));
                Fire(Pick(s.endTrigger, endTrigger));
            }
            yield return Hover(c, last ? finalHold : insertPause);

            if (!last) yield return MoveCopy(c, farPos, farRot, seatScale, pullBackDuration, 0f);
        }

        // 5. The snap point appears as a translucent ghost right where the copy is (same look, same pulse) and STAYS there
        //    for the rest of the demo; the copy then disappears. The glow on the real piece fades.
        ShowSnapGhost(i);
        StartCoroutine(HideAfter(c, 0.05f));
        if (s.highlightLight) StartCoroutine(FadeLight(s.highlightLight, 0f, lightFadeDuration));
    }

    IEnumerator MoveCopy(Transform c, Vector3 toPos, Quaternion toRot, Vector3 toScale, float duration, float arc)
    {
        Vector3 fromPos = c.position;
        Quaternion fromRot = c.rotation;
        Vector3 fromScale = c.localScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            c.position = Vector3.Lerp(fromPos, toPos, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * arc;
            c.rotation = Quaternion.Slerp(fromRot, toRot, e);
            c.localScale = Vector3.Lerp(fromScale, toScale, e);
            yield return null;
        }
        c.position = toPos;
        c.rotation = toRot;
        c.localScale = toScale;
    }

    // Floats gently up and down in place for 'duration' seconds.
    IEnumerator Hover(Transform c, float duration)
    {
        Vector3 basePos = c.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            c.position = basePos + Vector3.up * Mathf.Sin(t * hoverBobSpeed) * hoverBobAmount;
            yield return null;
        }
        c.position = basePos;
    }

    // =====================================================================
    //  PHASE 2: the user does the same thing with the real pieces
    // =====================================================================

    IEnumerator HideAfter(Transform t, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (t) SetRenderers(t, false);
    }

    // One snap point switches on (it may be disabled in the scene) and appears as a translucent ghost.
    void ShowSnapGhost(int i)
    {
        var s = steps[i];
        if (s == null || !s.snapPoint || !s.snapPointIsGhost || snapShown[i]) return;
        ShowObject(s.snapPoint);
        snapShown[i] = true;
        UpdateSnapGhostAlpha();
    }

    // Shows every snap point that hasn't appeared yet.
    void ShowSnapGhosts()
    {
        for (int i = 0; i < steps.Length; i++) ShowSnapGhost(i);
    }

    // Ghosts always keep their normal translucent look; only the optional pulse changes the alpha.
    // The flying copy and the snap point ghosts use the same pulse, so the copy already pulses from the moment it appears
    // and the swap to the snap ghost is seamless.
    void UpdateSnapGhostAlpha()
    {
        float pulse = 1f - snapGhostPulse * 0.5f + snapGhostPulse * 0.5f * Mathf.Sin(Time.time * guidePulseSpeed);

        if (copyMat && copyAlphaProp != null)
        {
            Color cc = copyBaseColor;
            cc.a = copyBaseColor.a * pulse;
            copyMat.SetColor(copyAlphaProp, cc);
        }

        if (snapShown == null || snapAlphaProp == null) return;
        for (int i = 0; i < snapShown.Length; i++)
        {
            if (!snapShown[i] || !snapMats[i]) continue;
            Color col = snapBaseColor;
            col.a = snapBaseColor.a * pulse;
            snapMats[i].SetColor(snapAlphaProp, col);
        }
    }

    IEnumerator UserTurn()
    {
        if (userTurnDelay > 0f) yield return new WaitForSeconds(userTurnDelay);

        PlayClip(yourTurnClip);
        onUserTurnStarted.Invoke();

        int n = steps.Length;
        seated = new bool[n];
        wasHeld = new bool[n];
        busy = new bool[n];
        guideOn = new bool[n];
        grab = new Behaviour[n];
        heldProp = new System.Reflection.PropertyInfo[n];

        for (int i = 0; i < n; i++)
        {
            var s = steps[i];
            if (s == null || !s.piece || !s.snapPoint) { seated[i] = true; continue; }
            PrepareUserPiece(i);
            guideOn[i] = true;
        }

        while (true)
        {
            bool allDone = true;
            for (int i = 0; i < n; i++)
            {
                if (seated[i]) { ApplyGuide(i, false); continue; }
                allDone = false;

                Step s = steps[i];
                ApplyGuide(i, guideOn[i]);
                if (busy[i]) continue;

                bool held = IsHeld(i);
                if (held && !wasHeld[i])
                {
                    // Grabbed: the light on the piece goes out.
                    guideOn[i] = false;
                    PlayClip(grabClip);
                }
                else if (!held && wasHeld[i])
                {
                    // Let go: close enough to the ghost = it slides in, otherwise it glides back to the tray.
                    float d = Vector3.Distance(s.piece.position, s.snapPoint.position);
                    if (d <= s.userSnapRadius) StartCoroutine(SeatUserPiece(i));
                    else if (returnDroppedPieces) StartCoroutine(ReturnToTray(i));
                    else guideOn[i] = true;
                }
                if (held)
                {
                    // While held: turn to the rotation the demo showed, and link in as soon as it is close to the ghost.
                    if (rotateToDemoOnGrab && s.alignRotationToSnap)
                    {
                        s.piece.rotation = Quaternion.RotateTowards(s.piece.rotation, s.snapPoint.rotation, grabRotateSpeed * Time.deltaTime);
                        var heldRb = s.piece.GetComponent<Rigidbody>();
                        if (heldRb && !heldRb.isKinematic) heldRb.angularVelocity = Vector3.zero;
                    }
                    if (snapWhileHeld && Vector3.Distance(s.piece.position, s.snapPoint.position) <= heldSnapRadius)
                        StartCoroutine(SeatUserPiece(i));
                }
                wasHeld[i] = held;
            }
            if (allDone) break;
            yield return null;
        }

        PlayClip(allDoneClip);
        onUserFinished.Invoke();
    }

    // Makes the real piece grabbable: touch components off, grab interactable on, no gravity.
    void PrepareUserPiece(int i)
    {
        Transform p = steps[i].piece;

        // The piece and all its parents must be switched on.
        for (Transform x = p; x != null; x = x.parent)
            if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);

        int colliderCount = 0;
        foreach (var c in p.GetComponentsInChildren<Collider>(true)) { c.enabled = true; colliderCount++; }
        if (colliderCount == 0)
            Debug.LogWarning($"[RobotDemoSequence] Piece '{p.name}' has no Collider, so it can't be grabbed. Add one.");

        foreach (var b in p.GetComponentsInChildren<Behaviour>(true))
        {
            string n = b.GetType().Name;
            if (n == "XRSimpleInteractable" || b is StationTouchRelay) b.enabled = false;   // they would fight the grab
        }
        foreach (var b in p.GetComponentsInChildren<Behaviour>(true))
        {
            if (b.GetType().Name == "XRGrabInteractable")
            {
                b.enabled = false;   // off and on again: it registers fresh with the XR Interaction Manager, now that the colliders are on
                b.enabled = true;
                grab[i] = b;
                heldProp[i] = b.GetType().GetProperty("isSelected");
                break;
            }
        }
        if (!grab[i])
        {
            // The piece only has a touch (Simple) interactable: give it a Grab Interactable so the ray can pick it up.
            grab[i] = AddGrabInteractable(p);
            if (grab[i]) heldProp[i] = grab[i].GetType().GetProperty("isSelected");
        }
        if (!grab[i])
            Debug.LogWarning($"[RobotDemoSequence] Piece '{p.name}' has no XR Grab Interactable and one couldn't be added automatically. " +
                             "Add an XR Grab Interactable (and a Rigidbody) to it.");
        else if (heldProp[i] == null)
            Debug.LogWarning($"[RobotDemoSequence] Couldn't read 'isSelected' on the XR Grab Interactable of '{p.name}'; " +
                             "the script won't notice when it is grabbed or released.");

        // The held piece keeps its own rotation instead of turning with the hand.
        if (grab[i] && lockRotationWhileGrabbed)
        {
            var trackRot = grab[i].GetType().GetProperty("trackRotation");
            if (trackRot != null && trackRot.CanWrite) trackRot.SetValue(grab[i], false, null);
            else Debug.LogWarning($"[RobotDemoSequence] Couldn't turn off 'Track Rotation' on the Grab Interactable of '{p.name}'; untick it in the Inspector.");
        }

        var rb = p.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.useGravity = false;     // the pieces float, like the tray
            rb.isKinematic = false;    // a kinematic body can't follow the hand with the default Velocity Tracking
        }
        else
            Debug.LogWarning($"[RobotDemoSequence] Piece '{p.name}' has no Rigidbody; the XR Grab Interactable needs one.");

        Debug.Log($"[RobotDemoSequence] Grab check for '{p.name}': colliders={colliderCount}, rigidbody={(rb ? "yes" : "NO")}, " +
                  $"XR components: {DescribeXR(p)}");
    }

    // Adds an XR Grab Interactable to the piece at runtime (found by name, so it works with any XR Interaction Toolkit version)
    // and copies the Interaction Layer Mask from the piece's Simple Interactable, so the same hands/rays can reach it.
    Behaviour AddGrabInteractable(Transform p)
    {
        System.Type grabType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.GetName().Name.StartsWith("Unity.XR.Interaction.Toolkit")) continue;
            System.Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
                if (t != null && t.Name == "XRGrabInteractable") { grabType = t; break; }
            if (grabType != null) break;
        }
        if (grabType == null) return null;

        var added = p.gameObject.AddComponent(grabType) as Behaviour;
        if (!added) return null;

        try
        {
            foreach (var b in p.GetComponents<Behaviour>())
            {
                if (b.GetType().Name != "XRSimpleInteractable") continue;
                var layersProp = b.GetType().GetProperty("interactionLayers");
                if (layersProp != null) layersProp.SetValue(added, layersProp.GetValue(b, null), null);
                break;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[RobotDemoSequence] Couldn't copy the Interaction Layer Mask to the new Grab Interactable on '{p.name}': {e.Message}");
        }

        added.enabled = true;
        Debug.Log($"[RobotDemoSequence] '{p.name}' had no XR Grab Interactable, so one was added automatically.");
        return added;
    }

    // Lists the XR components on the piece, its children and its parents (for the grab check log).
    static string DescribeXR(Transform p)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in p.GetComponentsInParent<Behaviour>(true))
            if (b.GetType().Name.StartsWith("XR")) sb.Append($"[{(b.transform == p ? "on piece" : "on parent '" + b.name + "'")}: {b.GetType().Name}, enabled={b.enabled}] ");
        foreach (var b in p.GetComponentsInChildren<Behaviour>(true))
            if (b.transform != p && b.GetType().Name.StartsWith("XR")) sb.Append($"[on child '{b.name}': {b.GetType().Name}, enabled={b.enabled}] ");
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    bool IsHeld(int i)
    {
        var b = grab[i];
        var pr = heldProp[i];
        if (!b || pr == null) return false;
        return (bool)pr.GetValue(b, null);
    }

    // Light on the real piece: pulses while waiting to be grabbed, fades out otherwise.
    void ApplyGuide(int i, bool on)
    {
        Step s = steps[i];
        Light l = s.userGuideLight ? s.userGuideLight : s.highlightLight;
        if (!l) return;

        float baseI = Mathf.Max(0.01f, s.highlightIntensity);
        float target = on ? baseI * (1f + guidePulseAmount * Mathf.Sin(Time.time * guidePulseSpeed)) : 0f;
        if (on && l.intensity < 0.01f)
        {
            if (!l.gameObject.activeSelf) l.gameObject.SetActive(true);
            l.enabled = true;
        }
        l.intensity = Mathf.MoveTowards(l.intensity, target, baseI * Time.deltaTime / Mathf.Max(0.05f, lightFadeDuration));
    }

    // The user let go near the ghost: the piece slides in and clicks, like the robot's copy did.
    IEnumerator SeatUserPiece(int i)
    {
        busy[i] = true;
        guideOn[i] = false;
        Step s = steps[i];
        Transform p = s.piece;

        if (grab[i]) grab[i].enabled = false;      // it can't be picked up again
        var rb = p.GetComponent<Rigidbody>();
        if (rb) rb.isKinematic = true;

        Quaternion seatRot = s.alignRotationToSnap ? s.snapPoint.rotation : p.rotation;
        Vector3 seatScale = s.matchScaleToSnap ? LocalScaleFor(p, s.snapPoint.lossyScale) : p.localScale;
        yield return MoveCopy(p, s.snapPoint.position, seatRot, seatScale, seatDuration, 0f);

        PlayClip(snapClip);
        if (snapFlashLight) StartCoroutine(Flash(snapFlashLight, snapFlashIntensity, snapFlashDuration));
        if (s.snapPointIsGhost) SetRenderers(s.snapPoint, false);   // before reparenting, so the piece stays visible
        if (s.stayAttached) p.SetParent(s.snapPoint, true);

        seated[i] = true;
        busy[i] = false;
    }

    // The user let go far from the ghost: after a moment the piece glides back to the tray.
    IEnumerator ReturnToTray(int i)
    {
        busy[i] = true;
        Transform p = steps[i].piece;

        float w = 0f;
        while (w < returnDelay)
        {
            if (IsHeld(i)) { busy[i] = false; yield break; }
            w += Time.deltaTime;
            yield return null;
        }

        var rb = p.GetComponent<Rigidbody>();
        bool wasKinematic = rb && rb.isKinematic;
        if (rb) rb.isKinematic = true;

        Vector3 fromPos = p.position;
        Quaternion fromRot = p.rotation;
        float t = 0f;
        while (t < 1f)
        {
            if (IsHeld(i))
            {
                if (rb) rb.isKinematic = wasKinematic;
                busy[i] = false;
                yield break;
            }
            t += Time.deltaTime / Mathf.Max(0.05f, returnToTrayDuration);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            p.position = Vector3.Lerp(fromPos, origPos[i], e);
            p.rotation = Quaternion.Slerp(fromRot, origRot[i], e);
            yield return null;
        }
        p.position = origPos[i];
        p.rotation = origRot[i];
        if (rb) rb.isKinematic = wasKinematic;

        guideOn[i] = true;
        busy[i] = false;
    }

    // ---------- Ghost helpers ----------

    // A translucent material for the ghosts: a copy of Ghost Material, or an automatic URP Unlit one.
    Material MakeGhostMaterial()
    {
        if (ghostMaterial) return new Material(ghostMaterial);

        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (!sh)
        {
            Debug.LogWarning("[RobotDemoSequence] Couldn't create the automatic ghost material (URP Unlit shader not found, or stripped " +
                             "from the build). Assign a translucent material to Ghost Material; until then ghosts keep their own materials.");
            return null;
        }

        var m = new Material(sh);
        m.SetFloat("_Surface", 1f);                                                              // transparent
        m.SetFloat("_Blend", 0f);                                                                // alpha blend
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetColor("_BaseColor", ghostColor);
        return m;
    }

    // Builds a visual-only copy of a piece: no scripts, colliders, rigidbody, lights or sounds, so it can't be touched.
    Transform CreateDemoCopy(Transform piece)
    {
        // Built under an inactive parent so nothing on the copy wakes up (no Awake/OnEnable) while it is being stripped.
        GameObject go = Instantiate(piece.gameObject, piece.position, piece.rotation, demoHolder);
        go.name = piece.name + " (demo copy)";

        foreach (var m in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (m == null) continue;
            if (m.GetType().Namespace == "TMPro") continue;   // keep text labels
            DestroyImmediate(m);
        }
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) DestroyImmediate(l);
        foreach (var a in go.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);

        go.transform.SetParent(null, true);
        go.transform.localScale = piece.lossyScale;
        go.SetActive(true);
        return go.transform;
    }

    // Makes a copy or snap point safe to use as a ghost: nothing to touch, no physics, no lights, ghost material, hidden.
    // Does NOT switch the object on: a disabled snap point stays disabled until it is needed.
    void PrepareGhost(Transform g, Material mat)
    {
        foreach (var c in g.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var body in g.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (var l in g.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var b in g.GetComponentsInChildren<Behaviour>(true))
        {
            string n = b.GetType().Name;
            if (b is StationTouchRelay || n == "XRSimpleInteractable" || n == "XRGrabInteractable") b.enabled = false;
        }

        if (mat)
        {
            foreach (var r in g.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                r.sharedMaterials = mats;
            }
        }

        SetRenderers(g, false);
    }

    // Switches an object on (and any disabled parents) and makes it visible.
    static void ShowObject(Transform t)
    {
        for (Transform x = t; x != null; x = x.parent)
            if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
        SetRenderers(t, true);
    }

    static void SetRenderers(Transform root, bool on)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = on;
    }

    // Local scale that gives 'piece' the requested world scale under its current parent.
    static Vector3 LocalScaleFor(Transform piece, Vector3 worldScale)
    {
        if (!piece.parent) return worldScale;
        Vector3 ps = piece.parent.lossyScale;
        return new Vector3(
            ps.x != 0f ? worldScale.x / ps.x : worldScale.x,
            ps.y != 0f ? worldScale.y / ps.y : worldScale.y,
            ps.z != 0f ? worldScale.z / ps.z : worldScale.z);
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