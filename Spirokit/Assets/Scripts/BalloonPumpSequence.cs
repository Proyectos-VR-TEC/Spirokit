using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The step after LightsOutOnClick (the user clicked the phone and the room went dark):
///
/// 1) The deflated balloon appears, a light fades on so everything can be seen,
///    and the TWO IMPORTANT OBJECTS fly to their places. One of them is the PUMP HANDLE.
///
/// 2) Three PHASES (LOC6, LOC7, LOC8). In each one the robot says its line, then the user does ONE SINGLE STROKE
///    at the speed of that phase (fast, slower, slowest). A stroke = grab the pump handle, push it in all the way and bring it
///    back out. The handle can only slide along a straight line, up to a set length (it can't be pulled out or pushed in
///    further than that). The speed of the stroke is judged by how long it takes (see Min / Max Duration in each phase).
///
/// 3) THE COLOR OF THE LIGHT shows how well the user is doing (it follows the pump speed):
///      GREEN  = right speed: the balloon inflates.
///      YELLOW = a bit too slow / too fast: the balloon stays as it is.
///      RED    = way off: the balloon deflates (never below where the phase started).
///    The light follows the stroke LIVE, so the user can correct the speed while moving. The balloon inflates only while the
///    light is green. If the stroke ends without being green enough, the balloon goes back to where the phase started,
///    the robot says LOC9 (too fast) or LOC10 (too slow), and the user tries the same phase again.
///
/// 4) When the last phase is full, the balloon pops, the objects and the extra light go away, the room lights come back
///    (LightsOutOnClick.LightsOn) and the robot says LOC11.
///
/// Starts by itself when LightsOutOnClick finishes going dark (Start On Lights Out), or when you call Begin().
/// The balloon and the extra light should be DISABLED in the scene: this script switches them on when it needs them.
/// </summary>
public class BalloonPumpSequence : MonoBehaviour
{
    [System.Serializable]
    public class Phase
    {
        public string name;
        [Tooltip("What the robot says when this phase starts (LOC6, LOC7, LOC8...).")]
        public AudioClip introClip;
        [Tooltip("Right speed = the whole stroke (push in AND back out) takes between these two durations, in seconds. " +
                 "Min Duration is the fastest allowed stroke, Max Duration the slowest.")]
        public float minDuration = 0.6f;
        public float maxDuration = 1.0f;
        [Range(0f, 1f), Tooltip("How inflated the balloon is when this phase is done (0 = flat, 1 = full). The last phase should be 1: then it pops.")]
        public float endInflation = 0.35f;
    }

    [System.Serializable]
    public class FocusObject
    {
        [Tooltip("The object that flies into place when the balloon appears. It can be disabled in the scene: it is switched on.")]
        public Transform obj;
        [Tooltip("Where it ends up: an empty object placed exactly where you want it. The object takes its position (and rotation).")]
        public Transform target;
        [Tooltip("ON: the object also turns to match the target's rotation. OFF: it keeps its own rotation.")]
        public bool matchRotation = true;
        [Tooltip("ON: the object also grows or shrinks to the target's size. OFF: it keeps its own size.")]
        public bool matchScale = true;

        [System.NonSerialized] public Vector3 homePos;
        [System.NonSerialized] public Quaternion homeRot;
        [System.NonSerialized] public Vector3 homeScale;
        [System.NonSerialized] public bool homeActive;

        public void CaptureHome()
        {
            if (!obj) return;
            homePos = obj.position;
            homeRot = obj.rotation;
            homeScale = obj.localScale;
            homeActive = obj.gameObject.activeSelf;
        }

        public void RestoreHome()
        {
            if (!obj) return;
            obj.position = homePos;
            obj.rotation = homeRot;
            obj.localScale = homeScale;
            obj.gameObject.SetActive(homeActive);
        }

        // The local scale the object needs so that its WORLD size equals the target's world size
        // (works even if the object has a scaled parent).
        public Vector3 TargetLocalScale()
        {
            Vector3 world = target.lossyScale;
            Transform p = obj.parent;
            if (!p) return world;
            Vector3 ps = p.lossyScale;
            return new Vector3(
                Mathf.Abs(ps.x) > 0.0001f ? world.x / ps.x : world.x,
                Mathf.Abs(ps.y) > 0.0001f ? world.y / ps.y : world.y,
                Mathf.Abs(ps.z) > 0.0001f ? world.z / ps.z : world.z);
        }
    }

    [Header("Start signal")]
    [Tooltip("The LightsOutOnClick in the scene. It starts this script when the room has gone dark, and it gets the lights back when the balloon pops.")]
    public LightsOutOnClick lights;
    [Tooltip("ON: start when the Lights script finishes going dark. OFF: only when Begin() is called.")]
    public bool startOnLightsOut = true;
    public float startDelay = 1f;
    [Tooltip("Seconds between the objects being in place and the robot's first line (LOC6).")]
    public float showToFirstLineDelay = 1f;

    [Header("Balloon")]
    [Tooltip("The balloon, DISABLED in the scene. Its scale in the scene is the FULL size; it starts smaller (Balloon Min Scale).")]
    public Transform balloon;
    [Range(0.05f, 1f), Tooltip("Size of the flat balloon compared to the full size.")]
    public float balloonMinScale = 0.35f;
    [Tooltip("Tiny swell on every stroke, as a fraction of the size. 0 = none.")]
    public float strokePunch = 0.04f;
    [Tooltip("Seconds between the light starting to fade on and the balloon starting to appear, so the light is there first.")]
    public float balloonAppearDelay = 1f;
    [Tooltip("Seconds the balloon takes to grow from nothing to its flat size (it swells in with a tiny bounce at the end).")]
    public float balloonAppearDuration = 2.5f;

    [Header("Light on the balloon and the objects (also the feedback)")]
    [Tooltip("A light that fades on when the balloon appears, so the user can see the balloon and the two objects in the dark. " +
             "Its COLOR is the feedback: red / yellow / green. " +
             "It is switched off automatically at the start (so the 'lights out' doesn't touch it) and switched on here.")]
    public Light balloonLight;
    [Tooltip("How bright the light gets.")]
    public float balloonLightIntensity = 10f;
    [Tooltip("Seconds the light takes to fade on.")]
    public float balloonLightFadeIn = 2f;
    [Tooltip("Seconds the light takes to fade out when the balloon pops.")]
    public float balloonLightFadeOut = 0.4f;

    [Header("Light colors (feedback)")]
    [Tooltip("Way off the right speed: the balloon deflates.")]
    public Color redColor = new Color(1f, 0.15f, 0.1f);
    [Tooltip("A bit off the right speed: the balloon stays as it is.")]
    public Color yellowColor = new Color(1f, 0.85f, 0.1f);
    [Tooltip("Right speed: the balloon inflates.")]
    public Color greenColor = new Color(0.15f, 1f, 0.25f);
    [Tooltip("How fast the light changes from one color to the next (higher = faster).")]
    public float colorChangeSpeed = 6f;

    [Header("The two important objects")]
    [Tooltip("The PUMP HANDLE: the object the user grabs and moves back and forth to pump. It needs a Collider. " +
             "If it has no XR interactable, an XR Simple Interactable is added (its Interaction Layer Mask must match your ray's / hand's).")]
    public FocusObject pumpHandle = new FocusObject();
    [Tooltip("The other important object (e.g. the pump body). It only flies into place.")]
    public FocusObject otherObject = new FocusObject();
    [Tooltip("Seconds the two objects take to fly into place.")]
    public float objectsMoveDuration = 3.5f;
    [Tooltip("ON: the two objects disappear (with the balloon) when it pops.")]
    public bool hideObjectsAfterPop = true;

    [Header("Pump handle movement")]
    [Tooltip("EASIEST: an empty object placed where the handle is when pushed in all the way. The handle slides in a straight line from its place " +
             "(the Target above) to this point, and can't go beyond either end. Leave empty to use Pump Direction and Pump Length instead.")]
    public Transform pumpEndPoint;
    [Tooltip("Only used when Pump End Point is empty: direction the handle is pushed in. " +
             "See 'Direction In Handle Space' for what the axes mean.")]
    public Vector3 pumpDirection = Vector3.down;
    [Tooltip("ON: the direction is in the handle's OWN axes, as it is once it has flown into place (so 0,0,-1 = the handle's own -Z, " +
             "the blue arrow of its Move gizmo in Local mode). OFF: world axes (0,-1,0 = straight down in the scene).")]
    public bool directionInHandleSpace = true;
    [Tooltip("Only used when Pump End Point is empty: how far (meters) the handle can move.")]
    public float pumpLength = 0.25f;
    [Tooltip("ON: when the user lets go, the handle slides back to its starting place.")]
    public bool returnHandleOnRelease = true;
    [Tooltip("Meters per second the handle slides back at.")]
    public float handleReturnSpeed = 0.5f;

    [Header("Quality zones")]
    [Range(0f, 1f), Tooltip("Quality (0 = way off, 1 = perfect speed) from which the light is YELLOW. Below it: RED.")]
    public float yellowFrom = 0.2f;
    [Range(0f, 1f), Tooltip("Quality from which the light is GREEN.")]
    public float greenFrom = 0.5f;
    [Tooltip("How fast the quality follows the pumping speed (per second). Lower = calmer, less flickering color.")]
    public float markerSpeed = 1.5f;

    [Header("The single stroke")]
    [Range(0.02f, 0.4f), Tooltip("The stroke starts when the handle has moved this fraction of its length.")]
    public float startFraction = 0.1f;
    [Range(0.01f, 0.2f), Tooltip("The stroke ends when the handle is back within this fraction of its starting place (keep it smaller than Start Fraction).")]
    public float endFraction = 0.05f;
    [Range(0.5f, 1f), Tooltip("The handle has to be pushed in at least this fraction of its length for the stroke to count.")]
    public float depthFraction = 0.85f;
    [Range(0.2f, 1f), Tooltip("Fraction of the stroke that has to be in GREEN for the phase to be done.")]
    public float requiredGreenFraction = 0.5f;
    [Range(0f, 1f), Tooltip("The phase is also done if the whole stroke took about the right time: the allowed range widened by this fraction " +
             "(0.3 = 30% extra on each side), even if the light wasn't green long enough.")]
    public float durationSlack = 0.3f;
    [Range(0f, 0.8f), Tooltip("Seconds at the start of a stroke that don't count (the hand is still getting up to speed).")]
    public float warmupSeconds = 0.3f;
    [Tooltip("How forgiving it is. The light turns fully red when the speed is this fraction OFF the right range (1 = 100% too slow or too fast).")]
    public float rateTolerance = 1f;
    [Tooltip("Seconds the user can wait before starting the stroke before the robot says LOC10 (too slow).")]
    public float idleSeconds = 6f;
    [Tooltip("In the editor (no headset): Space = a perfect stroke, Space + Left Shift = too fast, Space + Left Ctrl = too slow.")]
    public bool testWithSpaceKey = true;

    [Header("Robot voice")]
    [Tooltip("The robot's AudioSource. Empty = the lines play at this object's position.")]
    public AudioSource robotVoice;
    [Tooltip("The phases in order: LOC6, LOC7, LOC8.")]
    public Phase[] phases;
    [Tooltip("LOC9: said when the user pumps TOO FAST (swap with LOC10 if it should be the other way around).")]
    public AudioClip tooFastClip;
    [Tooltip("LOC10: said when the user pumps TOO SLOW or stops.")]
    public AudioClip tooSlowClip;
    [Tooltip("Minimum seconds between two 'you haven't started' reminders (LOC10).")]
    public float correctionCooldown = 6f;
    [Tooltip("LOC11: said after the balloon popped and the lights are back.")]
    public AudioClip finalClip;
    public float finalClipDelay = 1.5f;

    [Header("Sounds and pop")]
    [Tooltip("Optional: plays the stroke and pop sounds. Empty = they play at the balloon.")]
    public AudioSource sfx;
    [Tooltip("Plays on every stroke (when the handle comes back out).")]
    public AudioClip pumpClip;
    public AudioClip popClip;
    [Tooltip("Optional particles for the pop. Keep it OUTSIDE the balloon (the balloon is switched off when it pops); it is moved to the balloon.")]
    public ParticleSystem popEffect;

    [Header("Events")]
    [Tooltip("Fires the moment the balloon pops.")]
    public UnityEvent onBalloonPopped;
    [Tooltip("Fires when the robot has finished LOC11: the signal for the next script.")]
    public UnityEvent onFinished;

    enum Zone { Red, Yellow, Green }

    bool running;
    bool evaluating;        // the user's pumping counts
    bool speaking;
    bool balloonShown;

    float inflation;        // 0 = flat, 1 = full
    float displayQ;         // smoothed quality of the pumping (0..1)
    float punch;
    float appearT = 1f;     // 0 = balloon not there yet, 1 = fully appeared
    Vector3 balloonFullScale = Vector3.one;

    float nextCorrectionTime;
    int currentPhase;

    // Editor test stroke (space key).
    bool simActive;
    float simT;
    float simDuration;

    int movingCount;
    Coroutine lightRoutine;

    // The pump handle: it slides on a line from handleOrigin along handleAxis, handleT meters (0..handleLength).
    bool handleReady;
    Transform handle;
    Vector3 handleOrigin;
    Vector3 handleAxis = Vector3.down;
    float handleLength = 0.25f;
    float handleT;
    Behaviour handleInteractable;
    PropertyInfo isSelectedProp;
    bool wasHeld;
    Component handInteractor;
    MethodInfo attachMethod;
    Vector3 grabHandPos;
    float grabT;

    // Default phases when the component is added (change them in the Inspector).
    void Reset()
    {
        phases = new Phase[]
        {
            new Phase { name = "LOC6 - fast",    minDuration = 0.4f, maxDuration = 1.2f, endInflation = 0.35f },
            new Phase { name = "LOC7 - slower",  minDuration = 1.2f, maxDuration = 2.6f, endInflation = 0.7f },
            new Phase { name = "LOC8 - slowest", minDuration = 2.4f, maxDuration = 5.0f, endInflation = 1f },
        };
    }

    void Awake()
    {
        if (balloon) balloonFullScale = balloon.localScale;
        if (pumpHandle != null) pumpHandle.CaptureHome();
        if (otherObject != null) otherObject.CaptureHome();

        // The extra light starts switched off, so the 'lights out' script never sees it (it would dim it and bring it back).
        if (balloonLight && balloonLight.gameObject != gameObject) balloonLight.gameObject.SetActive(false);
    }

    void Start()
    {
        if (lights && startOnLightsOut) lights.onLightsOut.AddListener(Begin);
    }

    void OnDestroy()
    {
        if (lights) lights.onLightsOut.RemoveListener(Begin);
    }

    // Hook this to LightsOutOnClick's On Lights Out (or let Start On Lights Out do it).
    public void Begin()
    {
        if (running) return;
        if (phases == null || phases.Length == 0)
        {
            Debug.LogWarning("[BalloonPumpSequence] No phases set up, nothing to do.");
            return;
        }
        running = true;
        StartCoroutine(Run());
    }

    // Lets the sequence be played again (the room lights are not touched).
    public void ResetSequence()
    {
        StopAllCoroutines();
        running = false;
        evaluating = false;
        speaking = false;
        balloonShown = false;
        handleReady = false;
        wasHeld = false;
        handleT = 0f;
        inflation = 0f;
        displayQ = 0.5f;
        punch = 0f;
        simActive = false;
        lightRoutine = null;
        if (handleInteractable) handleInteractable.enabled = false;
        if (balloon) balloon.gameObject.SetActive(false);
        if (popEffect) popEffect.gameObject.SetActive(false);
        if (balloonLight) { balloonLight.intensity = 0f; balloonLight.gameObject.SetActive(false); }
        if (pumpHandle != null) pumpHandle.RestoreHome();
        if (otherObject != null) otherObject.RestoreHome();
    }

    void Update()
    {
        UpdateHandle();

#if ENABLE_LEGACY_INPUT_MANAGER
        if (evaluating && testWithSpaceKey && handleReady && !simActive && Input.GetKeyDown(KeyCode.Space))
            StartTestStroke();
#endif
        punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 0.3f);
        if (balloonShown) ApplyBalloon();
    }

    // =====================================================================
    //  The sequence
    // =====================================================================

    IEnumerator Run()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        // The flat balloon appears...
        inflation = 0f;
        displayQ = 0.5f;
        appearT = 0f;   // it starts at no size and swells in (see AppearBalloon)
        if (balloon) { Activate(balloon.gameObject); balloonShown = true; ApplyBalloon(); StartCoroutine(AppearBalloon()); }
        else { appearT = 1f; Debug.LogWarning("[BalloonPumpSequence] No Balloon assigned."); }

        // ...the light fades on (starting in yellow, "waiting"), and the two important objects fly into place.
        if (balloonLight) balloonLight.color = yellowColor;
        StartLightFade(balloonLightIntensity, balloonLightFadeIn, false);
        movingCount = 0;
        StartMove(pumpHandle);
        StartMove(otherObject);
        while (movingCount > 0) yield return null;
        while (appearT < 1f) yield return null;   // everything is in place before the robot speaks

        SetupHandle();

        if (showToFirstLineDelay > 0f) yield return new WaitForSeconds(showToFirstLineDelay);

        // Each phase = robot line, then the user pumps.
        for (int i = 0; i < phases.Length; i++)
        {
            yield return Say(phases[i].introClip, phases[i].name);
            yield return PumpPhase(i);
        }

        // The balloon pops and the lights come back, then LOC11.
        yield return Pop();
    }

    IEnumerator PumpPhase(int idx)
    {
        Phase ph = phases[idx];
        currentPhase = idx;
        float phaseStart = idx == 0 ? 0f : phases[idx - 1].endInflation;
        float L = Mathf.Max(0.01f, handleLength);

        // A whole stroke covers 2 x L (in and out). Right speed = the stroke takes between minDuration and maxDuration.
        float minSpeed = 2f * L / Mathf.Max(0.05f, ph.maxDuration);   // slowest allowed
        float maxSpeed = 2f * L / Mathf.Max(0.05f, ph.minDuration);   // fastest allowed

        evaluating = true;
        bool success = false;

        while (!success)
        {
            inflation = phaseStart;
            displayQ = 0.5f;

            // 1) Waiting for the user to start the stroke (the light waits in yellow).
            float idleTimer = 0f;
            while (handleT < startFraction * L)
            {
                ApplyLightColor(Zone.Yellow);
                idleTimer += Time.deltaTime;
                if (idleTimer >= idleSeconds && !speaking && Time.time >= nextCorrectionTime)
                {
                    StartCoroutine(SayCorrection(tooSlowClip, "LOC10 (too slow)"));
                    idleTimer = 0f;
                }
                yield return null;
            }

            // 2) The stroke is happening: the light follows it live.
            float t0 = Time.time;
            float lastT = handleT;
            float maxT = handleT;
            float travelled = 0f;
            float greenDist = 0f;
            float judged = 0f;     // distance covered after the warm-up
            bool complete = false, aborted = false;

            while (!complete && !aborted)
            {
                float dT = Mathf.Abs(handleT - lastT);
                lastT = handleT;
                travelled += dT;
                maxT = Mathf.Max(maxT, handleT);
                float elapsed = Time.time - t0;

                // Average speed of the stroke so far (a little pause at the bottom doesn't flip the color).
                // The first moments of a stroke don't count (the hand is still getting up to speed): the light waits in yellow.
                bool warmedUp = elapsed > warmupSeconds;
                float q = warmedUp ? QualityFor(minSpeed, maxSpeed, travelled / elapsed) : 0.5f;
                displayQ = Mathf.MoveTowards(displayQ, q, markerSpeed * Time.deltaTime);
                Zone z = ZoneOf(displayQ);
                ApplyLightColor(z);

                // The balloon inflates with the distance covered in green; red takes it back.
                if (warmedUp)
                {
                    judged += dT;
                    if (z == Zone.Green) greenDist += dT;
                    else if (z == Zone.Red) greenDist = Mathf.Max(0f, greenDist - dT);
                }
                inflation = Mathf.Lerp(phaseStart, ph.endInflation, Mathf.Clamp01(greenDist / (2f * L)));

                // The stroke is over when the handle is back at the start.
                if (handleT <= endFraction * L)
                {
                    if (maxT >= depthFraction * L) complete = true;
                    else aborted = true;   // went back before being pushed in enough: doesn't count
                }
                yield return null;
            }

            // 3) How did it go?
            float total = Time.time - t0;
            float avgSpeed = total > 0.01f ? travelled / total : 0f;
            float greenFrac = judged > 0.0001f ? greenDist / judged : 0f;

            // The phase is done if enough of the stroke was green, OR the whole stroke took about the right time
            // (the allowed range, widened by Duration Slack).
            bool durationOk = total >= ph.minDuration * (1f - durationSlack) && total <= ph.maxDuration * (1f + durationSlack);

            if (complete && (greenFrac >= requiredGreenFraction || durationOk))
            {
                success = true;
                inflation = ph.endInflation;
                punch = strokePunch;
                PlayAt(pumpClip, balloon ? balloon.position : transform.position);
                for (float h = 0f; h < 0.4f; h += Time.deltaTime)
                {
                    ApplyLightColor(Zone.Green);
                    yield return null;
                }
            }
            else
            {
                // Failed: the robot says what went wrong and the balloon goes back to where the phase started.
                if (complete && !speaking)
                {
                    bool tooFast = avgSpeed > maxSpeed;
                    StartCoroutine(SayCorrection(tooFast ? tooFastClip : tooSlowClip, tooFast ? "LOC9 (too fast)" : "LOC10 (too slow)"));
                }
                float from = inflation;
                for (float h = 0f; h < 0.5f; h += Time.deltaTime)
                {
                    inflation = Mathf.Lerp(from, phaseStart, h / 0.5f);
                    ApplyLightColor(complete ? Zone.Red : Zone.Yellow);
                    yield return null;
                }
                inflation = phaseStart;
            }
        }

        evaluating = false;
    }

    // Editor test (no headset): a stroke of the right duration, or too fast / too slow with Shift / Ctrl.
    void StartTestStroke()
    {
        if (phases == null || currentPhase < 0 || currentPhase >= phases.Length) return;
        Phase ph = phases[currentPhase];
        float d = (ph.minDuration + ph.maxDuration) * 0.5f;
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.LeftShift)) d = ph.minDuration * 0.4f;
        else if (Input.GetKey(KeyCode.LeftControl)) d = ph.maxDuration * 2.5f;
#endif
        simDuration = d;
        simT = 0f;
        simActive = true;
    }

    IEnumerator Pop()
    {
        evaluating = false;
        balloonShown = false;

        if (balloon)
        {
            if (popEffect)
            {
                popEffect.transform.position = balloon.position;
                popEffect.gameObject.SetActive(true);
                popEffect.Play();
            }
            PlayAt(popClip, balloon.position);
            balloon.gameObject.SetActive(false);
        }

        // The pump handle can't be grabbed any more; the two objects go away; the extra light fades out.
        handleReady = false;
        wasHeld = false;
        if (handleInteractable) handleInteractable.enabled = false;
        if (hideObjectsAfterPop)
        {
            if (pumpHandle != null && pumpHandle.obj) pumpHandle.obj.gameObject.SetActive(false);
            if (otherObject != null && otherObject.obj) otherObject.obj.gameObject.SetActive(false);
        }
        StartLightFade(0f, balloonLightFadeOut, true);

        if (lights) lights.LightsOn();
        else Debug.LogWarning("[BalloonPumpSequence] No Lights (LightsOutOnClick) assigned, so the room lights don't come back.");

        onBalloonPopped.Invoke();

        if (finalClipDelay > 0f) yield return new WaitForSeconds(finalClipDelay);
        yield return Say(finalClip, "LOC11");
        onFinished.Invoke();
    }

    // =====================================================================
    //  The two objects fly into place
    // =====================================================================

    void StartMove(FocusObject f)
    {
        if (f == null || !f.obj || !f.target) return;
        movingCount++;
        StartCoroutine(MoveOne(f));
    }

    IEnumerator MoveOne(FocusObject f)
    {
        Transform o = f.obj;
        Activate(o.gameObject);

        var rb = o.GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.useGravity = false; }   // it must not fall while flying

        Vector3 fromPos = o.position;
        Quaternion fromRot = o.rotation;
        Vector3 toPos = f.target.position;
        Quaternion toRot = f.matchRotation ? f.target.rotation : fromRot;
        Vector3 fromScale = o.localScale;
        Vector3 toScale = f.matchScale ? f.TargetLocalScale() : fromScale;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, objectsMoveDuration);
            float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            o.position = Vector3.Lerp(fromPos, toPos, e);
            o.rotation = Quaternion.Slerp(fromRot, toRot, e);
            o.localScale = Vector3.Lerp(fromScale, toScale, e);
            yield return null;
        }
        o.position = toPos;
        o.rotation = toRot;
        o.localScale = toScale;
        movingCount--;
    }

    // =====================================================================
    //  The pump handle: grabbed, and limited to a line
    // =====================================================================

    // Called once the handle is in place: works out the line it slides on and makes it grabbable.
    void SetupHandle()
    {
        handleReady = false;
        if (pumpHandle == null || !pumpHandle.obj)
        {
            Debug.LogWarning("[BalloonPumpSequence] No Pump Handle assigned: the user can't pump (only the space key works in the editor).");
            return;
        }

        handle = pumpHandle.obj;
        handleOrigin = handle.position;

        if (pumpEndPoint)
        {
            Vector3 d = pumpEndPoint.position - handleOrigin;
            handleLength = d.magnitude;
            handleAxis = handleLength > 0.0001f ? d / handleLength : Vector3.down;
        }
        else
        {
            Vector3 dir = pumpDirection.sqrMagnitude > 0.0001f ? pumpDirection.normalized : Vector3.down;
            handleAxis = directionInHandleSpace ? handle.TransformDirection(dir).normalized : dir;
            handleLength = Mathf.Max(0.01f, pumpLength);
        }
        handleT = 0f;
        wasHeld = false;

        int colliderCount = 0;
        foreach (var c in handle.GetComponentsInChildren<Collider>(true)) { c.enabled = true; colliderCount++; }
        if (colliderCount == 0)
            Debug.LogWarning($"[BalloonPumpSequence] '{handle.name}' has no Collider, so it can't be grabbed. Add one.");

        var rb = handle.GetComponent<Rigidbody>();
        if (rb) { rb.isKinematic = true; rb.useGravity = false; }

        // Any XR interactable on the handle (found by name, so it works with any XR Interaction Toolkit version).
        handleInteractable = null;
        foreach (var b in handle.GetComponentsInChildren<Behaviour>(true))
        {
            for (var t = b.GetType(); t != null; t = t.BaseType)
                if (t.Name == "XRBaseInteractable") { handleInteractable = b; break; }
            if (handleInteractable) break;
        }
        if (!handleInteractable)
        {
            var simpleType = FindXRType("XRSimpleInteractable");
            if (simpleType != null) handleInteractable = handle.gameObject.AddComponent(simpleType) as Behaviour;
            if (handleInteractable) Debug.Log($"[BalloonPumpSequence] '{handle.name}' had no XR interactable, so an XR Simple Interactable was added.");
        }
        if (!handleInteractable)
        {
            Debug.LogWarning($"[BalloonPumpSequence] Couldn't find or add an XR interactable on '{handle.name}'. Add an XR Simple Interactable yourself.");
            return;
        }

        // If it is a Grab Interactable it must not move the handle by itself: this script moves it, inside the limits.
        SetBool(handleInteractable, "trackPosition", false);
        SetBool(handleInteractable, "trackRotation", false);
        SetBool(handleInteractable, "throwOnDetach", false);

        handleInteractable.enabled = false;   // off and on again: registers fresh now that the colliders are on
        handleInteractable.enabled = true;
        isSelectedProp = handleInteractable.GetType().GetProperty("isSelected");
        if (isSelectedProp == null)
            Debug.LogWarning($"[BalloonPumpSequence] Couldn't read 'isSelected' on the XR interactable of '{handle.name}'; the handle can't be pumped.");

        handleReady = true;
    }

    // While the handle is held it follows the hand ALONG the line only, never past either end.
    void UpdateHandle()
    {
        if (!handleReady || !handle) return;

        // Editor test stroke: the handle goes in and out by itself.
        if (simActive)
        {
            simT += Time.deltaTime;
            float u = simT / Mathf.Max(0.05f, simDuration);
            if (u >= 1f) { simActive = false; handleT = 0f; }
            else handleT = handleLength * (1f - Mathf.Abs(2f * u - 1f));
            handle.position = handleOrigin + handleAxis * handleT;
            return;
        }

        bool held = IsHandleHeld();
        if (held && !wasHeld)
        {
            handInteractor = GetSelectingInteractor();
            attachMethod = FindAttachMethod(handInteractor);
            grabHandPos = HandPosition();
            grabT = handleT;
        }

        if (held)
        {
            float moved = Vector3.Dot(HandPosition() - grabHandPos, handleAxis);
            handleT = Mathf.Clamp(grabT + moved, 0f, handleLength);
        }
        else if (returnHandleOnRelease)
        {
            handleT = Mathf.MoveTowards(handleT, 0f, handleReturnSpeed * Time.deltaTime);
        }

        handle.position = handleOrigin + handleAxis * handleT;
        wasHeld = held;
    }

    bool IsHandleHeld()
    {
        if (!handleInteractable || isSelectedProp == null) return false;
        return (bool)isSelectedProp.GetValue(handleInteractable, null);
    }

    // The hand / ray that is holding the handle.
    Component GetSelectingInteractor()
    {
        var type = handleInteractable.GetType();
        foreach (var name in new[] { "firstInteractorSelecting", "selectingInteractor" })
        {
            var p = type.GetProperty(name);
            if (p == null) continue;
            var c = p.GetValue(handleInteractable, null) as Component;
            if (c) return c;
        }
        Debug.LogWarning("[BalloonPumpSequence] Couldn't find which hand is holding the handle, so it can't follow the hand.");
        return null;
    }

    static MethodInfo FindAttachMethod(Component interactor)
    {
        if (!interactor) return null;
        foreach (var m in interactor.GetType().GetMethods())
            if (m.Name == "GetAttachTransform" && m.GetParameters().Length == 1) return m;
        return null;
    }

    // Where the hand is holding the handle (the interactor's attach point; for a ray it moves with the ray).
    Vector3 HandPosition()
    {
        if (!handInteractor) return grabHandPos;
        if (attachMethod != null)
        {
            try
            {
                var at = attachMethod.Invoke(handInteractor, new object[] { handleInteractable }) as Transform;
                if (at) return at.position;
            }
            catch (System.Exception) { /* fall back to the hand itself */ }
        }
        return handInteractor.transform.position;
    }

    static void SetBool(object target, string propName, bool value)
    {
        var p = target.GetType().GetProperty(propName);
        if (p != null && p.CanWrite && p.PropertyType == typeof(bool)) p.SetValue(target, value, null);
    }

    static System.Type FindXRType(string typeName)
    {
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.GetName().Name.StartsWith("Unity.XR.Interaction.Toolkit")) continue;
            System.Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
                if (t != null && t.Name == typeName) return t;
        }
        return null;
    }

    // =====================================================================
    //  Pumping: strokes and speed
    // =====================================================================

    // 1 inside the right speed range (meters per second of handle travel), falling to 0 as the speed gets further off.
    float QualityFor(float minSpeed, float maxSpeed, float speed)
    {
        if (speed < minSpeed) return Mathf.Clamp01(1f - (minSpeed - speed) / Mathf.Max(0.001f, minSpeed * rateTolerance));
        if (speed > maxSpeed) return Mathf.Clamp01(1f - (speed - maxSpeed) / Mathf.Max(0.001f, maxSpeed * rateTolerance));
        return 1f;
    }

    Zone ZoneOf(float q)
    {
        if (q >= greenFrom) return Zone.Green;
        if (q >= yellowFrom) return Zone.Yellow;
        return Zone.Red;
    }

    // =====================================================================
    //  Visuals, light, sound
    // =====================================================================

    void ApplyBalloon()
    {
        if (!balloon) return;
        float size = Mathf.Lerp(balloonMinScale, 1f, Mathf.Clamp01(inflation)) * (1f + punch);
        float appear = Mathf.Max(0.0001f, EaseOutBack(appearT));
        balloon.localScale = balloonFullScale * size * appear;
    }

    // The balloon swells in from nothing, slowly, with a tiny bounce at the end.
    IEnumerator AppearBalloon()
    {
        if (balloonAppearDelay > 0f) yield return new WaitForSeconds(balloonAppearDelay);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.05f, balloonAppearDuration);
            appearT = Mathf.Clamp01(t);
            yield return null;
        }
        appearT = 1f;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.2f;
        const float c3 = c1 + 1f;
        float p = Mathf.Clamp01(x) - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // The light's color is the feedback: it glides to red / yellow / green.
    void ApplyLightColor(Zone z)
    {
        if (!balloonLight) return;
        Color target = z == Zone.Green ? greenColor : (z == Zone.Yellow ? yellowColor : redColor);
        float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, colorChangeSpeed) * Time.deltaTime);
        balloonLight.color = Color.Lerp(balloonLight.color, target, k);
    }

    // The extra light fades on (or out). Only one fade runs at a time.
    void StartLightFade(float to, float duration, bool switchOffAtEnd)
    {
        if (!balloonLight) return;
        if (lightRoutine != null) StopCoroutine(lightRoutine);
        lightRoutine = StartCoroutine(FadeBalloonLight(to, duration, switchOffAtEnd));
    }

    IEnumerator FadeBalloonLight(float to, float duration, bool switchOffAtEnd)
    {
        if (to > 0f)
        {
            balloonLight.intensity = 0f;                 // starts dark, then dims on
            Activate(balloonLight.gameObject);
            balloonLight.enabled = true;
        }
        float from = balloonLight.intensity;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, duration);
            balloonLight.intensity = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        balloonLight.intensity = to;
        if (switchOffAtEnd) balloonLight.gameObject.SetActive(false);
        lightRoutine = null;
    }

    // The robot says a line and we wait until it has finished.
    IEnumerator Say(AudioClip clip, string label)
    {
        if (!clip)
        {
            Debug.LogWarning($"[BalloonPumpSequence] No clip assigned for {label}, skipping the line.");
            yield break;
        }
        speaking = true;
        PlayVoice(clip);
        yield return new WaitForSeconds(clip.length);
        speaking = false;
    }

    // A correction (LOC9 / LOC10): same, and then a pause before the next one.
    IEnumerator SayCorrection(AudioClip clip, string label)
    {
        yield return Say(clip, label);
        nextCorrectionTime = Time.time + correctionCooldown;
    }

    void PlayVoice(AudioClip clip)
    {
        if (robotVoice) robotVoice.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    void PlayAt(AudioClip clip, Vector3 pos)
    {
        if (!clip) return;
        if (sfx) sfx.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, pos);
    }

    // Switches an object on, and any disabled parents.
    static void Activate(GameObject go)
    {
        for (Transform x = go.transform; x != null; x = x.parent)
            if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
    }
}