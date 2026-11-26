using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The step after BreathingExerciseSequence:
///
///  1) The guide robot says LOC18.
///  2) The assembled object (the pieces RobotDemoSequence merged at its finale, which include the mouthpiece) lights up
///     (a pulsing light + glow). A translucent ghost on the patient shows where it goes (the mouthpiece at the mouth).
///  3) The user grabs it and puts it on the patient robot: when the MOUTHPIECE gets close to its target on the patient,
///     the whole object snaps into place and stays there. If dropped far away it flies back to where it was floating.
///  4) When it is on: a sound + On All Placed.
///  5) About 3 seconds later the guide robot says a line (After Placed Line).
///  6) Finally the lights go out and the guide robot says another line (Lights Off Line). Then On Finished.
///
/// The merged object only exists while the game runs, so it can't be dragged into the Inspector: assign the real
/// mouthpiece piece (the same one used in RobotDemoSequence's steps) and the script finds the merged object it belongs to.
/// Start: assign Breathing Sequence (it listens to its On Finished by itself), or call Begin().
/// </summary>
public class MouthpieceSequence : MonoBehaviour
{
    // =====================================================================
    //  Inspector
    // =====================================================================

    [Header("Start signal")]
    [Tooltip("The BreathingExerciseSequence in the scene. When it finishes, this step starts.")]
    public BreathingExerciseSequence breathingSequence;
    public bool startWhenBreathingEnds = true;
    public float delayBeforeStart = 1f;

    [Header("1) Voice")]
    [Tooltip("AudioSource on the guide robot.")]
    public AudioSource guideVoice;
    public AudioClip loc18;
    [Range(0f, 3f)] public float voiceVolume = 1.6f;
    [Tooltip("If LOC18 has no clip yet, wait this many seconds instead.")]
    public float secondsIfNoClip = 3f;
    [Tooltip("ON: the object lights up while LOC18 is playing. OFF: after it ends.")]
    public bool highlightDuringLine = false;

    [Header("2) What the user grabs")]
    [Tooltip("The REAL mouthpiece piece (the same one in RobotDemoSequence's steps).")]
    public Transform mouthpiece;
    [Tooltip("ON: the user grabs the whole merged object the mouthpiece belongs to (made by RobotDemoSequence's finale). " +
             "OFF: only the mouthpiece itself.")]
    public bool grabMergedObject = true;
    [Tooltip("The RobotDemoSequence: its floating is stopped so the merged object can be grabbed.")]
    public RobotDemoSequence robotDemo;
    [Tooltip("Where the MOUTHPIECE goes: an empty object as a CHILD of the patient, at its mouth. " +
             "Its position AND rotation are how the mouthpiece will sit (the rest of the object comes along).")]
    public Transform target;
    [Tooltip("Extra rotation (degrees on X, Y, Z) added to the Target, so you can turn how the object sits without touching the Target. " +
             "E.g. (0, 180, 0) = turned around, (90, 0, 0) = tilted a quarter turn.")]
    public Vector3 targetRotation = Vector3.zero;
    [Tooltip("Extra offset (meters) from the Target, in the Target's own axes. E.g. (0, 0, 0.02) = 2 cm further along its blue arrow.")]
    public Vector3 targetOffset = Vector3.zero;
    [Tooltip("ON: changing Target Rotation / Offset during Play moves the ghost and the placed object right away, so you can find the right values. " +
             "Values changed in Play mode are lost when you stop: write them down and set them again after stopping.")]
    public bool liveAdjust = true;
    [Tooltip("Optional: your own marker / ghost at the target (disabled in the scene). Empty + Auto Ghost = one is made from the object.")]
    public GameObject targetMarker;

    [Header("2) Highlight")]
    [Tooltip("Optional: your own light. Empty + Auto Light = one is created on the object.")]
    public Light highlightLight;
    public bool autoLight = true;
    public Color lightColor = new Color(1f, 0.85f, 0.55f);
    public float lightIntensity = 2.5f;
    public float lightRange = 0.8f;
    [Tooltip("How high above the object the auto light sits.")]
    public float lightHeight = 0.2f;
    [Tooltip("The object's materials glow (emission).")]
    public bool glow = true;
    [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.85f, 0.55f) * 1.2f;
    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 1.2f;
    [Range(0f, 1f)] public float pulseMin = 0.4f;
    public float highlightAppearSeconds = 1f;
    [Tooltip("ON: it keeps its light while held. OFF: the light turns off when it is grabbed.")]
    public bool keepHighlightWhileHeld = false;

    [Header("2) Ghost on the patient")]
    public bool autoGhost = true;
    public Color ghostColor = new Color(0.6f, 0.9f, 1f, 0.35f);
    [Tooltip("ON: the ghost only shows while the user is holding the object.")]
    public bool ghostOnlyWhileHeld = false;

    [Header("3) Placing")]
    [Tooltip("How close (meters) the mouthpiece must be to the target to snap.")]
    public float snapDistance = 0.15f;
    [Tooltip("ON: it snaps as soon as it's close, even while held. OFF: only when the user lets go near the target.")]
    public bool snapWhileHeld = true;
    public float snapSeconds = 0.35f;
    [Tooltip("Turn off the object's colliders once placed, so it doesn't push the patient.")]
    public bool disableCollidersWhenPlaced = true;
    public AudioClip placedClip;
    [Tooltip("Dropped far from the target, it flies back to where it was.")]
    public bool returnIfDropped = true;
    public float returnDelay = 1f;
    public float returnSeconds = 0.8f;

    [Header("4) End")]
    public AudioSource sfx;
    public AudioClip allPlacedClip;
    [Range(0f, 3f)] public float sfxVolume = 1.6f;

    [Header("5) Line after it is placed")]
    [Tooltip("Seconds after the object is placed before the line.")]
    public float secondsBeforeLine = 3f;
    [Tooltip("What the guide robot says ~3 seconds after the object is placed (e.g. LOC19).")]
    public AudioClip afterPlacedLine;
    [Tooltip("Seconds of silence after that line, before the lights go out.")]
    public float pauseAfterLine = 0.5f;

    [Header("6) Lights out + last line")]
    [Tooltip("Seconds the lights take to go out.")]
    public float lightsOffSeconds = 2.5f;
    [Tooltip("How dark it gets: 0 = completely dark, 0.1 = 10% of the light.")]
    [Range(0f, 1f)] public float lightsOffLevel = 0f;
    [Tooltip("Lights that stay on (e.g. a light on the patient).")]
    public Light[] lightsToKeep;
    public bool dimAmbientLight = true;
    public bool dimSkybox = true;
    [Tooltip("What the guide robot says once the lights are out (e.g. LOC20).")]
    public AudioClip lightsOffLine;
    [Tooltip("Seconds between the lights being out and the last line.")]
    public float pauseBeforeLastLine = 0.5f;

    [Header("Events")]
    public UnityEvent onStarted;
    public UnityEvent onAllPlaced;
    [Tooltip("Fires when the lights are out (before the last line).")]
    public UnityEvent onLightsOff;
    [Tooltip("Fires after the last line: the signal for the next script.")]
    public UnityEvent onFinished;

    // =====================================================================
    //  State
    // =====================================================================

    bool running, active, placed, held, wasHeld, snapping;
    float highlightLevel;
    Transform item;          // what the user grabs (the merged object, or the mouthpiece)
    Transform refPoint;      // what has to reach the target (the mouthpiece)
    Behaviour interactable;
    Rigidbody rb;
    bool rbKinematic, rbGravity;
    Vector3 homePos; Quaternion homeRot;
    Vector3 relPos; Quaternion relRot;   // the mouthpiece's pose inside the item
    float lightFull;
    GameObject ghost;
    Coroutine returning;
    readonly List<Material> glowMats = new List<Material>();
    readonly List<Color> glowOriginal = new List<Color>();
    readonly List<bool> glowHadKeyword = new List<bool>();

    void Start()
    {
        if (targetMarker) targetMarker.SetActive(false);
        if (breathingSequence && startWhenBreathingEnds) breathingSequence.onFinished.AddListener(Begin);
    }

    void OnDestroy()
    {
        if (breathingSequence) breathingSequence.onFinished.RemoveListener(Begin);
    }

    [ContextMenu("Begin (test)")]
    public void Begin()
    {
        if (running) return;
        running = true;
        StartCoroutine(Run());
    }

    // =====================================================================
    //  The sequence
    // =====================================================================

    IEnumerator Run()
    {
        if (delayBeforeStart > 0f) yield return new WaitForSeconds(delayBeforeStart);
        Log("Started.");
        onStarted.Invoke();

        yield return Setup();
        if (!item) { running = false; yield break; }

        // 1) LOC18
        if (highlightDuringLine) StartStep();
        yield return Say(loc18, "LOC18");

        // 2) It lights up and can be grabbed
        if (!highlightDuringLine) StartStep();

        // 3) Wait until it is on the patient
        while (!placed) yield return null;
        yield return new WaitForSeconds(snapSeconds);

        // 4) It is on
        active = false;
        PlaySfx(allPlacedClip);
        Log("The mouthpiece is on the patient.");
        onAllPlaced.Invoke();

        // 5) ~3 seconds later, a line
        if (secondsBeforeLine > 0f) yield return new WaitForSeconds(secondsBeforeLine);
        yield return Say(afterPlacedLine, "After Placed Line");
        if (pauseAfterLine > 0f) yield return new WaitForSeconds(pauseAfterLine);

        // 6) The lights go out, then the last line
        Log("Lights out.");
        CaptureLights();
        yield return FadeLights(1f, lightsOffLevel, lightsOffSeconds);
        onLightsOff.Invoke();
        if (pauseBeforeLastLine > 0f) yield return new WaitForSeconds(pauseBeforeLastLine);
        yield return Say(lightsOffLine, "Lights Off Line");

        Log("Finished.");
        running = false;
        onFinished.Invoke();
    }

    // =====================================================================
    //  Voice
    // =====================================================================

    AudioSource backupVoice;

    // The guide robot says a line and we wait until it ends. Never overlaps: anything still playing on the voice is cut.
    IEnumerator Say(AudioClip clip, string label)
    {
        if (!clip)
        {
            Debug.LogWarning($"[MouthpieceSequence] {label}: no clip assigned: waiting {secondsIfNoClip}s instead.");
            yield return new WaitForSeconds(secondsIfNoClip);
            yield break;
        }
        AudioSource src = guideVoice;
        if (src && !src.enabled) src.enabled = true;
        if (!src || !src.isActiveAndEnabled)
        {
            Debug.LogWarning($"[MouthpieceSequence] {label}: Guide Voice is empty or on an inactive object: playing from a backup AudioSource.");
            if (!backupVoice) { backupVoice = gameObject.AddComponent<AudioSource>(); backupVoice.playOnAwake = false; backupVoice.spatialBlend = 0f; }
            src = backupVoice;
        }
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        Log($"{label}: '{clip.name}'");
        src.Stop();
        src.PlayOneShot(clip, voiceVolume);
        yield return new WaitForSeconds(clip.length);
    }

    // =====================================================================
    //  Lights out
    // =====================================================================

    List<Light> dimmedLights;
    float[] dimmedFrom;
    Color savedAmbColor;
    float savedAmbIntensity, savedReflIntensity;
    Material skyCopy;
    float savedSkyExposure;

    void CaptureLights()
    {
        var keep = new HashSet<Light>();
        if (lightsToKeep != null) foreach (var l in lightsToKeep) if (l) keep.Add(l);

        dimmedLights = new List<Light>();
#if UNITY_2022_2_OR_NEWER
        dimmedLights.AddRange(FindObjectsByType<Light>(FindObjectsSortMode.None));
#else
        dimmedLights.AddRange(FindObjectsOfType<Light>());
#endif
        dimmedLights.RemoveAll(l => !l || keep.Contains(l));
        dimmedFrom = new float[dimmedLights.Count];
        for (int i = 0; i < dimmedLights.Count; i++) dimmedFrom[i] = dimmedLights[i].intensity;

        savedAmbColor = RenderSettings.ambientLight;
        savedAmbIntensity = RenderSettings.ambientIntensity;
        savedReflIntensity = RenderSettings.reflectionIntensity;

        skyCopy = null;
        if (dimSkybox && RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            skyCopy = new Material(RenderSettings.skybox);   // a copy, so the skybox asset is never changed
            RenderSettings.skybox = skyCopy;
            savedSkyExposure = skyCopy.GetFloat("_Exposure");
        }
    }

    IEnumerator FadeLights(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, seconds);
            ApplyLightLevel(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))));
            yield return null;
        }
        ApplyLightLevel(to);
    }

    // 0 = dark, 1 = as it was.
    void ApplyLightLevel(float k)
    {
        if (dimmedLights != null)
            for (int i = 0; i < dimmedLights.Count; i++)
                if (dimmedLights[i]) dimmedLights[i].intensity = dimmedFrom[i] * k;
        if (dimAmbientLight)
        {
            RenderSettings.ambientIntensity = savedAmbIntensity * k;
            RenderSettings.ambientLight = savedAmbColor * k;
            RenderSettings.reflectionIntensity = savedReflIntensity * k;
        }
        if (skyCopy) skyCopy.SetFloat("_Exposure", savedSkyExposure * k);
    }

    /// <summary>Brings the lights back to how they were before going out (for a later script).</summary>
    public void LightsOn(float seconds = 1.5f)
    {
        if (dimmedLights != null) StartCoroutine(FadeLights(lightsOffLevel, 1f, seconds));
    }

    void StartStep()
    {
        if (active) return;
        active = true;
        if (interactable) interactable.enabled = true;
        if (highlightLight)
        {
            for (Transform x = highlightLight.transform; x != null; x = x.parent)
                if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
            highlightLight.intensity = 0f;
            highlightLight.enabled = true;
        }
        if (glow) SetupGlow();
        if (!ghostOnlyWhileHeld) ShowGhost(true);
        StartCoroutine(HighlightFadeIn());
        Log("The object is highlighted: the user can grab it.");
    }

    IEnumerator HighlightFadeIn()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, highlightAppearSeconds);
            highlightLevel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            yield return null;
        }
        highlightLevel = 1f;
    }

    // =====================================================================
    //  Every frame: highlight pulse, grab / release, snapping
    // =====================================================================

    void Update()
    {
        if (!active || placed || !item) return;

        held = IsSelected(interactable);
        if (held && !wasHeld)
        {
            Log("Grabbed.");
            if (returning != null) { StopCoroutine(returning); returning = null; }
            if (ghostOnlyWhileHeld) ShowGhost(true);
        }
        else if (!held && wasHeld)
        {
            if (ghostOnlyWhileHeld) ShowGhost(false);
            if (!IsNear() && returnIfDropped) returning = StartCoroutine(ReturnHome());
        }
        wasHeld = held;

        // Highlight
        float pulse = Mathf.Lerp(pulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        float k = (!held || keepHighlightWhileHeld) ? highlightLevel * pulse : 0f;
        if (highlightLight) highlightLight.intensity = lightFull * k;
        for (int i = 0; i < glowMats.Count; i++)
            if (glowMats[i]) glowMats[i].SetColor("_EmissionColor", glowOriginal[i] + glowColor * k);

        // Snap
        if ((snapWhileHeld || !held) && IsNear()) StartCoroutine(Place());
    }

    bool IsNear() => target && Vector3.Distance(refPoint.position, target.position + target.rotation * targetOffset) <= snapDistance;

    // Where the item has to be so the mouthpiece sits exactly on the target.
    void GoalPose(out Vector3 pos, out Quaternion rot)
    {
        Quaternion goalRot = target.rotation * Quaternion.Euler(targetRotation);
        rot = goalRot * Quaternion.Inverse(relRot);
        pos = target.position + target.rotation * targetOffset - rot * relPos;
    }

    // Keeps the ghost (and the placed object) on the goal pose while Target Rotation / Offset are being tweaked.
    void LateUpdate()
    {
        if (!liveAdjust || !target || !item) return;
        GoalPose(out var gp, out var gr);
        if (ghost && ghost.activeSelf && !targetMarker) ghost.transform.SetPositionAndRotation(gp, gr);
        if (placed && !snapping) item.SetPositionAndRotation(gp, gr);
    }

    // Shows where the mouthpiece will sit (Scene view, with this object selected): the arrows include the extra rotation.
    void OnDrawGizmosSelected()
    {
        if (!target) return;
        Vector3 p = target.position + target.rotation * targetOffset;
        Quaternion r = target.rotation * Quaternion.Euler(targetRotation);
        const float len = 0.08f;
        Gizmos.color = Color.red; Gizmos.DrawLine(p, p + r * Vector3.right * len);
        Gizmos.color = Color.green; Gizmos.DrawLine(p, p + r * Vector3.up * len);
        Gizmos.color = Color.blue; Gizmos.DrawLine(p, p + r * Vector3.forward * len);
        Gizmos.color = new Color(1f, 0.85f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(p, snapDistance);
    }

    // =====================================================================
    //  Placing on the patient
    // =====================================================================

    IEnumerator Place()
    {
        placed = true;
        snapping = true;
        if (returning != null) { StopCoroutine(returning); returning = null; }
        Log("Placed on the patient.");

        // Disabling the interactable makes the hand let go of it.
        if (interactable) interactable.enabled = false;
        yield return null;

        if (rb) { rb.isKinematic = true; rb.useGravity = false; }
        if (disableCollidersWhenPlaced)
            foreach (var c in item.GetComponentsInChildren<Collider>()) c.enabled = false;

        if (highlightLight) highlightLight.enabled = false;
        RestoreGlow();
        ShowGhost(false);
        PlaySfx(placedClip);

        Vector3 fromPos = item.position;
        Quaternion fromRot = item.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, snapSeconds);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            GoalPose(out var gp, out var gr);
            item.SetPositionAndRotation(Vector3.Lerp(fromPos, gp, s), Quaternion.Slerp(fromRot, gr, s));
            yield return null;
        }
        GoalPose(out var p, out var r);
        item.SetPositionAndRotation(p, r);
        item.SetParent(target, true);   // follows the patient from now on
        snapping = false;
    }

    IEnumerator ReturnHome()
    {
        yield return new WaitForSeconds(returnDelay);
        if (held || placed) yield break;
        Log("Dropped: it goes back.");

        if (rb) { rb.isKinematic = true; rb.useGravity = false; }
        Vector3 fromPos = item.position;
        Quaternion fromRot = item.rotation;
        float t = 0f;
        while (t < 1f)
        {
            if (held || placed) yield break;
            t += Time.deltaTime / Mathf.Max(0.01f, returnSeconds);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            item.SetPositionAndRotation(Vector3.Lerp(fromPos, homePos, s), Quaternion.Slerp(fromRot, homeRot, s));
            yield return null;
        }
        item.SetPositionAndRotation(homePos, homeRot);
        if (rb)
        {
            rb.isKinematic = rbKinematic;
            rb.useGravity = rbGravity;
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }
        returning = null;
    }

    // =====================================================================
    //  Setup (at the start of the step, because the merged object is only made at runtime)
    // =====================================================================

    IEnumerator Setup()
    {
        if (!mouthpiece)
        {
            Debug.LogWarning("[MouthpieceSequence] No Mouthpiece assigned.");
            yield break;
        }
        if (!target) Debug.LogWarning("[MouthpieceSequence] No Target on the patient, so it can't be placed.");

        // The merged object stops floating, so it doesn't fight the hand.
        if (robotDemo) robotDemo.StopAllCoroutines();

        refPoint = mouthpiece;
        item = (grabMergedObject && mouthpiece.parent) ? mouthpiece.parent : mouthpiece;
        if (grabMergedObject && !mouthpiece.parent)
            Debug.LogWarning("[MouthpieceSequence] The mouthpiece has no parent (it wasn't merged): only the mouthpiece will be grabbed.");
        Log($"The user will grab '{item.name}'.");

        for (Transform x = item; x != null; x = x.parent)
            if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);

        // The pieces inside the merged object keep their own (disabled) XR interactables from the robot demo:
        // they are removed so ONE grab interactable on the merged object owns all the colliders.
        object layers = null;
        if (item != mouthpiece || FindInteractable(item, onlyOnSelf: true) == null)
        {
            foreach (var b in item.GetComponentsInChildren<Behaviour>(true))
            {
                if (b.transform == item || !IsInteractable(b)) continue;
                if (layers == null) layers = GetProp(b, "interactionLayers");
                Destroy(b);
            }
            yield return null;   // the removed components are gone next frame
        }

        int colliders = 0;
        foreach (var c in item.GetComponentsInChildren<Collider>(true)) { c.enabled = true; colliders++; }
        if (colliders == 0) Debug.LogWarning($"[MouthpieceSequence] '{item.name}' has no Collider, so it can't be grabbed.");

        // Rigidbodies of the pieces inside: kinematic, so they just ride along.
        foreach (var body in item.GetComponentsInChildren<Rigidbody>(true))
            if (body.transform != item) { body.isKinematic = true; body.useGravity = false; }

        interactable = FindInteractable(item, onlyOnSelf: true);
        if (!interactable)
        {
            var grabType = FindXRType("XRGrabInteractable");
            if (grabType != null) interactable = item.gameObject.AddComponent(grabType) as Behaviour;
            if (interactable) Log($"An XR Grab Interactable was added to '{item.name}'.");
            else Debug.LogWarning($"[MouthpieceSequence] Couldn't add an XR Grab Interactable to '{item.name}'. Add one yourself.");
        }
        if (interactable)
        {
            if (layers != null) SetProp(interactable, "interactionLayers", layers);   // same hands / rays as the pieces
            SetEnumProp(interactable, "movementType", "Instantaneous");             // follows the hand even if kinematic
            interactable.enabled = false;                                           // on when the step starts
        }

        rb = item.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = true;   // it floats where it is until grabbed
            rb.useGravity = false;
            rbKinematic = true; rbGravity = false;
        }

        homePos = item.position;
        homeRot = item.rotation;
        relRot = Quaternion.Inverse(item.rotation) * refPoint.rotation;
        relPos = Quaternion.Inverse(item.rotation) * (refPoint.position - item.position);

        // Light
        if (!highlightLight && autoLight)
        {
            var go = new GameObject("Mouthpiece Highlight (auto)");
            go.transform.SetParent(item, false);
            go.transform.position = item.position + Vector3.up * lightHeight;
            highlightLight = go.AddComponent<Light>();
            highlightLight.type = LightType.Point;
            highlightLight.color = lightColor;
            highlightLight.range = lightRange;
            highlightLight.shadows = LightShadows.None;
            lightFull = lightIntensity;
        }
        else if (highlightLight) lightFull = Mathf.Max(highlightLight.intensity, lightIntensity);
        if (highlightLight) highlightLight.enabled = false;

        // Ghost on the patient
        if (targetMarker) ghost = targetMarker;
        else if (autoGhost && target) ghost = MakeGhost();
        if (ghost) ghost.SetActive(false);
    }

    // =====================================================================
    //  Glow / ghost
    // =====================================================================

    void SetupGlow()
    {
        glowMats.Clear(); glowOriginal.Clear(); glowHadKeyword.Clear();
        foreach (var r in item.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;   // skips hidden ghosts left inside
            foreach (var m in r.materials)
            {
                if (!m.HasProperty("_EmissionColor")) continue;
                bool had = m.IsKeywordEnabled("_EMISSION");
                glowHadKeyword.Add(had);
                glowOriginal.Add(had ? m.GetColor("_EmissionColor") : Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                glowMats.Add(m);
            }
        }
    }

    void RestoreGlow()
    {
        for (int i = 0; i < glowMats.Count; i++)
        {
            var m = glowMats[i];
            if (!m) continue;
            m.SetColor("_EmissionColor", glowOriginal[i]);
            if (!glowHadKeyword[i]) m.DisableKeyword("_EMISSION");
        }
        glowMats.Clear();
    }

    void ShowGhost(bool on)
    {
        if (ghost) ghost.SetActive(on && !placed);
    }

    // A translucent copy of the object's visible meshes, posed so its mouthpiece sits on the target.
    GameObject MakeGhost()
    {
        Material mat = MakeGhostMaterial();
        if (!mat)
        {
            Debug.LogWarning("[MouthpieceSequence] Couldn't make the ghost material (URP Unlit not found). Assign your own Target Marker instead.");
            return null;
        }

        var root = new GameObject("Mouthpiece Ghost (auto)").transform;
        GoalPose(out var p, out var r);
        root.SetPositionAndRotation(p, r);
        root.localScale = item.lossyScale;

        foreach (var mf in item.GetComponentsInChildren<MeshFilter>(true))
        {
            var src = mf.GetComponent<Renderer>();
            if (!mf.sharedMesh || !src || !src.enabled) continue;   // only what is actually visible
            var child = new GameObject(mf.name).transform;
            child.SetParent(root, false);
            child.localPosition = item.InverseTransformPoint(mf.transform.position);
            child.localRotation = Quaternion.Inverse(item.rotation) * mf.transform.rotation;
            child.localScale = Divide(mf.transform.lossyScale, item.lossyScale);
            child.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = child.gameObject.AddComponent<MeshRenderer>();
            var mats = new Material[mf.sharedMesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        root.SetParent(target, true);   // follows the patient
        return root.gameObject;
    }

    Material MakeGhostMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) return null;
        var m = new Material(shader);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", ghostColor);
        return m;
    }

    static Vector3 Divide(Vector3 a, Vector3 b) =>
        new Vector3(b.x != 0 ? a.x / b.x : 1f, b.y != 0 ? a.y / b.y : 1f, b.z != 0 ? a.z / b.z : 1f);

    // =====================================================================
    //  XR helpers (by name, so no fixed reference to the XR Interaction Toolkit version)
    // =====================================================================

    static bool IsInteractable(Behaviour b)
    {
        for (var t = b.GetType(); t != null; t = t.BaseType)
            if (t.Name == "XRBaseInteractable") return true;
        return false;
    }

    static Behaviour FindInteractable(Transform t, bool onlyOnSelf)
    {
        var list = onlyOnSelf ? t.GetComponents<Behaviour>() : t.GetComponentsInChildren<Behaviour>(true);
        foreach (var b in list) if (b && IsInteractable(b)) return b;
        return null;
    }

    static bool IsSelected(Behaviour b)
    {
        if (!b) return false;
        var prop = b.GetType().GetProperty("isSelected", BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.GetValue(b, null) is bool sel && sel;
    }

    static object GetProp(object o, string name)
    {
        var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        return p != null && p.CanRead ? p.GetValue(o, null) : null;
    }

    static void SetProp(object o, string name, object value)
    {
        try
        {
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) p.SetValue(o, value, null);
        }
        catch (System.Exception e) { Debug.LogWarning($"[MouthpieceSequence] Couldn't set {name}: {e.Message}"); }
    }

    static void SetEnumProp(object o, string name, string valueName)
    {
        var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) return;
        try { p.SetValue(o, System.Enum.Parse(p.PropertyType, valueName), null); }
        catch (System.Exception e) { Debug.LogWarning($"[MouthpieceSequence] Couldn't set {name}: {e.Message}"); }
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

    void PlaySfx(AudioClip clip)
    {
        if (!clip) return;
        if (sfx) sfx.PlayOneShot(clip, sfxVolume);
        else AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
    }

    static void Log(string msg) => Debug.Log("[MouthpieceSequence] " + msg);
}