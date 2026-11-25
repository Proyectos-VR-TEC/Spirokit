using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The step after LightsOutOnClick brings the lights back. Everything in ONE script:
///
///  1) An arrival sound plays (knock / hologram chime). The guide robot says LOC12.
///  2) The patient robot appears as a HOLOGRAM (fade + flicker + grows from the floor), glides close to the user
///     and says LOC13.
///  3) The guide robot says LOC14.
///  4) The whole room goes dark and a feather appears floating in the middle of the room.
///  5) The guide robot says LOC15.
///  6) The feather rises (inhale) and falls (exhale) 3 times; the user breathes with it.
///  7) The guide robot says LOC16.
///  8) The 4th and last breath: the feather explodes and the light comes back to the room.
///  9) The guide robot says LOC17.
/// 10) The patient robot breathes in/out 4 times, the last one the strongest.
///
/// Start: assign Lights Out Script (it listens to its On Lights Back event by itself), or call Begin().
/// Only real-time lights can be dimmed at runtime: baked lighting stays as baked.
/// </summary>
public class BreathingExerciseSequence : MonoBehaviour
{
    [System.Serializable]
    public class VoiceLine
    {
        [Tooltip("Just a name for the console, e.g. LOC12.")]
        public string id;
        public AudioClip clip;
        [Tooltip("Seconds of silence after the line, before the next step.")]
        public float pauseAfter = 0.5f;
        public VoiceLine(string id) { this.id = id; }
    }

    // =====================================================================
    //  Inspector
    // =====================================================================

    [Header("Start signal")]
    [Tooltip("The LightsOutOnClick in the scene. When its lights come back on, this sequence starts.")]
    public LightsOutOnClick lightsOutScript;
    public bool startWhenLightsComeBack = true;
    [Tooltip("Seconds between the lights coming back and step 1.")]
    public float delayBeforeStart = 1f;

    [Header("Voices")]
    [Tooltip("AudioSource on the guide robot (plays LOC12, 14, 15, 16, 17).")]
    public AudioSource guideVoice;
    [Tooltip("AudioSource on the patient robot (plays LOC13 and its breathing).")]
    public AudioSource patientVoice;
    [Tooltip("If a line has no clip yet, wait this many seconds instead (handy while the audio isn't ready).")]
    public float secondsIfNoClip = 3f;
    public VoiceLine loc12 = new VoiceLine("LOC12");
    public VoiceLine loc13 = new VoiceLine("LOC13");
    public VoiceLine loc14 = new VoiceLine("LOC14");
    public VoiceLine loc15 = new VoiceLine("LOC15");
    public VoiceLine loc16 = new VoiceLine("LOC16");
    public VoiceLine loc17 = new VoiceLine("LOC17");

    [Header("1) Arrival sound")]
    public AudioSource sfx;
    [Tooltip("The knock (or a hologram chime, now that there is no door).")]
    public AudioClip arrivalClip;
    [Tooltip("Seconds between the arrival sound and LOC12.")]
    public float pauseAfterArrivalSound = 0.5f;

    [Header("2) Patient hologram")]
    [Tooltip("The patient robot. It can be disabled in the scene: it is turned on when it arrives.")]
    public GameObject patient;
    [Tooltip("Where the hologram appears. Empty = where the patient already is.")]
    public Transform arrivalPoint;
    [Tooltip("Where the patient stands, close to the user. Empty = in front of the user (Main Camera) at Distance From User.")]
    public Transform standPoint;
    public float distanceFromUser = 1.2f;
    public float hologramAppearSeconds = 2f;
    [Tooltip("The hologram flickers while it appears.")]
    public bool hologramFlicker = true;
    [Tooltip("The hologram grows from the floor up (needs the patient's pivot at its feet).")]
    public bool growFromFloor = true;
    [Tooltip("Optional particles / effect played when the hologram appears (e.g. a scan ring).")]
    public ParticleSystem hologramEffect;
    public AudioClip hologramAppearClip;
    [Tooltip("Seconds the hologram takes to glide to the user.")]
    public float moveToUserSeconds = 2.5f;
    [Tooltip("Extra turn (degrees) so the patient's FACE looks at the user. Use it if the model shows its side or back: try 90, -90 or 180.")]
    public float patientFacingOffset = 0f;

    [Header("4) Darkness")]
    public float darkFadeSeconds = 2.5f;
    [Tooltip("How dark the room gets: 0 = completely black, 0.15 = 15% of the normal light (you still see the room a little).")]
    [Range(0f, 1f)] public float darknessLevel = 0.15f;
    [Tooltip("Lights that stay on in the dark (e.g. the patient hologram's own light). The feather light is kept automatically.")]
    public Light[] lightsToKeep;
    public bool dimAmbientLight = true;
    public bool dimSkybox = true;
    [Tooltip("Seconds the light takes to come back after the feather explodes.")]
    public float lightsBackSeconds = 1.5f;

    [Header("4) Feather")]
    [Tooltip("The feather. It can be disabled in the scene: it is turned on when the room goes dark.")]
    public GameObject feather;
    [Tooltip("The center of the room, where the feather floats. Empty = where the feather already is.")]
    public Transform featherPoint;
    [Tooltip("Optional light that makes the feather visible in the dark. Empty + Auto Feather Light = one is created for you.")]
    public Light featherLight;
    [Tooltip("If Feather Light is empty, create a soft point light on the feather so it can be seen in the dark.")]
    public bool autoFeatherLight = true;
    public Color featherLightColor = new Color(1f, 0.95f, 0.85f);
    public float featherLightIntensity = 2f;
    public float featherLightRange = 2.5f;
    [Tooltip("ON: Feather Light is moved onto the feather while it floats, and goes back to where it was (e.g. the balloon) after the explosion.")]
    public bool moveFeatherLightToFeather = true;
    [Tooltip("ON: while it lights the feather, Feather Light uses the color / intensity / range below (not the balloon's red/yellow/green). They are restored after.")]
    public bool useFeatherLightSettings = true;
    [Tooltip("Make the feather's own material glow (emission), so it is visible even with no light.")]
    public bool featherGlow = true;
    [ColorUsage(false, true)] public Color featherGlowColor = new Color(1f, 0.95f, 0.85f) * 1.5f;
    public float featherAppearSeconds = 2f;
    [Tooltip("TEST: puts the feather 1.5 m in front of the user's eyes, ignoring Feather Point. If you see it like this, the problem is where Feather Point is.")]
    public bool debugFeatherInFrontOfUser = false;
    [Tooltip("The feather always turns to face the user. Turn it on if the feather is flat (a plane / quad / sprite): seen from the side or the back it is invisible.")]
    public bool featherFacesUser = false;
    [Tooltip("Gentle idle floating.")]
    public float bobAmount = 0.03f;
    public float bobSpeed = 1.5f;
    [Tooltip("Degrees per second the feather slowly spins.")]
    public float spinSpeed = 15f;

    [Header("6-8) Breathing with the feather")]
    [Tooltip("How high the feather rises on each inhale.")]
    public float breathHeight = 0.35f;
    public float inhaleSeconds = 4f;
    public float holdSeconds = 1f;
    public float exhaleSeconds = 5f;
    [Tooltip("Pause after each exhale, before the next inhale.")]
    public float restSeconds = 1f;
    [Tooltip("Optional soft cues on each inhale / exhale.")]
    public AudioClip inhaleCue;
    public AudioClip exhaleCue;

    [Header("8) Explosion")]
    [Tooltip("A particle prefab spawned where the feather is (e.g. a burst of feathers / sparkles).")]
    public GameObject explosionPrefab;
    [Tooltip("Or a particle system already in the scene (it is moved to the feather and played).")]
    public ParticleSystem explosionParticles;
    public AudioClip explosionClip;
    [Tooltip("Seconds between the explosion and the light coming back.")]
    public float pauseAfterExplosion = 0.3f;

    [Header("10) Patient comes close")]
    [Tooltip("Where the patient stands to breathe. Empty = it walks straight toward the user until Close Distance From User.")]
    public Transform closeStandPoint;
    public float closeDistanceFromUser = 0.9f;
    [Tooltip("Seconds it takes to come close.")]
    public float approachSeconds = 2f;
    [Tooltip("Seconds between arriving and the first breath.")]
    public float pauseBeforeBreathing = 0.5f;

    [Header("10) Patient breathing")]
    [Tooltip("What swells when the patient breathes (its chest / body). Empty = the whole patient.")]
    public Transform patientBreathTarget;
    [Tooltip("How much it swells on each breath (0.05 = 5%). The last one should be the biggest.")]
    public float[] patientBreathAmounts = { 0.03f, 0.03f, 0.04f, 0.09f };
    [Tooltip("Multiplies all the Patient Breath Amounts. 1 = as written, 2.5 = the patient swells 2.5x more on every breath.")]
    [Range(0.5f, 6f)] public float patientBreathSize = 2.5f;
    public float patientInhaleSeconds = 2f;
    public float patientExhaleSeconds = 2.5f;
    public float patientRestSeconds = 0.6f;
    [Tooltip("The last (strongest) breath lasts this many times longer.")]
    public float lastBreathLengthMultiplier = 1.6f;
    public AudioClip patientInhaleClip;
    public AudioClip patientExhaleClip;
    [Tooltip("Optional: an Animator trigger fired on each patient breath (empty = not used).")]
    public Animator patientAnimator;
    public string patientBreathTrigger = "";

    [Header("Volume (above 1 = louder than the clip; too high can distort)")]
    [Range(0f, 3f)] public float voiceVolume = 1.6f;
    [Range(0f, 3f)] public float patientBreathVolume = 2f;
    [Range(0f, 3f)] public float sfxVolume = 1.6f;

    [Header("Events")]
    public UnityEvent onStarted;
    public UnityEvent onFeatherExploded;
    public UnityEvent onFinished;

    // =====================================================================
    //  State
    // =====================================================================

    bool running;

    bool featherActive;
    Vector3 featherBase;
    float featherLift;

    // How the scene looked before going dark.
    List<Light> dimmedLights;
    float[] dimmedFrom;
    Color savedAmbColor;
    float savedAmbIntensity, savedReflIntensity;
    Material skyCopy;
    float savedSkyExposure;

    // Hologram materials and their original transparency.
    struct MatAlpha { public Material mat; public string prop; public bool isColor; public float alpha; }
    readonly List<MatAlpha> hologramMats = new List<MatAlpha>();

    void Start()
    {
        if (lightsOutScript && startWhenLightsComeBack) lightsOutScript.onLightsBack.AddListener(Begin);
    }

    void OnDestroy()
    {
        if (lightsOutScript) lightsOutScript.onLightsBack.RemoveListener(Begin);
    }

    [ContextMenu("Begin (test)")]
    public void Begin()
    {
        if (running) return;
        running = true;
        StartCoroutine(Run());
    }

    void Update()
    {
        if (!featherActive || !feather) return;
        float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmount;
        feather.transform.position = featherBase + Vector3.up * (featherLift + bob);

        Camera cam = featherFacesUser ? Camera.main : null;
        if (cam)
        {
            // Flat feather: its front (like a Unity Quad) always looks at the user.
            Vector3 away = feather.transform.position - cam.transform.position;
            if (away.sqrMagnitude > 0.0001f) feather.transform.rotation = Quaternion.LookRotation(away);
        }
        else feather.transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }

    // =====================================================================
    //  The sequence
    // =====================================================================

    IEnumerator Run()
    {
        if (delayBeforeStart > 0f) yield return new WaitForSeconds(delayBeforeStart);
        Log("Started.");
        onStarted.Invoke();

        // 1) Arrival sound + LOC12
        PlaySfx(arrivalClip, transform.position);
        if (arrivalClip) yield return new WaitForSeconds(arrivalClip.length);
        yield return new WaitForSeconds(pauseAfterArrivalSound);
        yield return Say(guideVoice, loc12);

        // 2) The patient appears as a hologram, comes close to the user and says LOC13
        if (patient)
        {
            yield return HologramAppear();
            yield return MovePatientToUser(standPoint, distanceFromUser, moveToUserSeconds);
        }
        else Debug.LogWarning("[BreathingExerciseSequence] No Patient assigned: skipping its arrival.");
        yield return Say(patientVoice, loc13);

        // 3) LOC14
        yield return Say(guideVoice, loc14);

        // 4) The room goes dark and the feather appears in the middle
        PrepareFeather();
        EnsureFeatherLight();
        CaptureLights();
        StartCoroutine(FeatherAppear());
        yield return FadeLights(1f, darknessLevel, darkFadeSeconds);

        // 5) LOC15
        yield return Say(guideVoice, loc15);

        // 6) Three breaths with the feather
        for (int i = 0; i < 3; i++)
        {
            Log($"Breath {i + 1} of 4.");
            yield return FeatherBreath();
        }

        // 7) LOC16
        yield return Say(guideVoice, loc16);

        // 8) The last breath: the feather explodes and the light comes back
        Log("Breath 4 of 4 (the last).");
        yield return FeatherBreath(skipRest: true);
        ExplodeFeather();
        if (pauseAfterExplosion > 0f) yield return new WaitForSeconds(pauseAfterExplosion);
        yield return FadeLights(darknessLevel, 1f, lightsBackSeconds);

        // 9) LOC17
        yield return Say(guideVoice, loc17);

        // 10) The patient comes close to the user, then breathes 4 times, the last the strongest
        if (patient && patient.activeInHierarchy)
        {
            Log("The patient comes close to the user.");
            yield return MovePatientToUser(closeStandPoint, closeDistanceFromUser, approachSeconds, straightToUser: true);
            if (pauseBeforeBreathing > 0f) yield return new WaitForSeconds(pauseBeforeBreathing);
        }
        yield return PatientBreathing();

        Log("Finished.");
        running = false;
        onFinished.Invoke();
    }

    // =====================================================================
    //  Voices and sounds
    // =====================================================================

    IEnumerator Say(AudioSource source, VoiceLine line)
    {
        Log($"{line.id}");
        if (line.clip && source)
        {
            source.Stop();
            source.PlayOneShot(line.clip, voiceVolume);   // PlayOneShot allows a volume above 1
            yield return new WaitForSeconds(line.clip.length);
        }
        else
        {
            Debug.LogWarning($"[BreathingExerciseSequence] {line.id} has no clip or no AudioSource: waiting {secondsIfNoClip}s instead.");
            yield return new WaitForSeconds(secondsIfNoClip);
        }
        if (line.pauseAfter > 0f) yield return new WaitForSeconds(line.pauseAfter);
    }

    void PlaySfx(AudioClip clip, Vector3 where, float volume = 1f)
    {
        if (!clip) return;
        volume *= sfxVolume;
        if (sfx) sfx.PlayOneShot(clip, volume);
        else AudioSource.PlayClipAtPoint(clip, where, volume);
    }

    // =====================================================================
    //  2) Patient hologram
    // =====================================================================

    IEnumerator HologramAppear()
    {
        Transform root = patient.transform;
        if (arrivalPoint) root.SetPositionAndRotation(arrivalPoint.position, arrivalPoint.rotation);

        CollectHologramMaterials();
        SetHologramAlpha(0f);
        Vector3 fullScale = root.localScale;
        if (growFromFloor) root.localScale = new Vector3(fullScale.x, fullScale.y * 0.01f, fullScale.z);
        patient.SetActive(true);

        if (hologramEffect)
        {
            hologramEffect.transform.position = root.position;
            hologramEffect.Play();
        }
        PlaySfx(hologramAppearClip, root.position);

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, hologramAppearSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            float flicker = (hologramFlicker && t < 1f && Random.value < 0.15f) ? 0.25f : 1f;
            SetHologramAlpha(k * flicker);
            if (growFromFloor) root.localScale = new Vector3(fullScale.x, fullScale.y * Mathf.Max(0.01f, k), fullScale.z);
            yield return null;
        }
        SetHologramAlpha(1f);
        root.localScale = fullScale;
    }

    // point: where to go (empty = computed from the user). straightToUser: walk along the line patient → user
    // (used for the final approach), instead of going in front of where the user is looking.
    IEnumerator MovePatientToUser(Transform point, float distance, float seconds, bool straightToUser = false)
    {
        Transform root = patient.transform;
        Transform cam = Camera.main ? Camera.main.transform : null;

        Vector3 target;
        if (point) target = point.position;
        else if (cam)
        {
            Vector3 dir;
            if (straightToUser) { dir = root.position - cam.position; }
            else dir = cam.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = Vector3.forward;
            target = cam.position + dir.normalized * distance;
            target.y = root.position.y;   // stays on its floor height
        }
        else
        {
            Debug.LogWarning("[BreathingExerciseSequence] No Stand Point and no Main Camera: the patient stays where it appeared.");
            yield break;
        }

        // Faces the user (or the Stand Point's direction if there is no camera).
        Quaternion endRot = point ? point.rotation : root.rotation;
        if (cam)
        {
            Vector3 look = cam.position - target; look.y = 0f;
            // + the model's own offset, in case its face isn't on its blue (Z) axis.
            if (look.sqrMagnitude > 0.001f) endRot = Quaternion.LookRotation(look) * Quaternion.Euler(0f, patientFacingOffset, 0f);
        }

        Vector3 startPos = root.position;
        Quaternion startRot = root.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, seconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            root.SetPositionAndRotation(Vector3.Lerp(startPos, target, k), Quaternion.Slerp(startRot, endRot, k));
            yield return null;
        }
    }

    void CollectHologramMaterials()
    {
        hologramMats.Clear();
        foreach (var r in patient.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)   // .materials makes per-object copies, so the assets aren't changed
            {
                if (m.HasProperty("_Alpha")) hologramMats.Add(new MatAlpha { mat = m, prop = "_Alpha", alpha = m.GetFloat("_Alpha") });
                else if (m.HasProperty("_Opacity")) hologramMats.Add(new MatAlpha { mat = m, prop = "_Opacity", alpha = m.GetFloat("_Opacity") });
                else if (m.HasProperty("_BaseColor")) hologramMats.Add(new MatAlpha { mat = m, prop = "_BaseColor", isColor = true, alpha = m.GetColor("_BaseColor").a });
                else if (m.HasProperty("_Color")) hologramMats.Add(new MatAlpha { mat = m, prop = "_Color", isColor = true, alpha = m.GetColor("_Color").a });
            }
        }
        if (hologramMats.Count == 0)
            Debug.LogWarning("[BreathingExerciseSequence] The patient has no material with _Alpha / _Opacity / _BaseColor / _Color: it can't fade in.");
    }

    // k: 0 = invisible, 1 = its normal transparency. Only works if the materials are Transparent.
    void SetHologramAlpha(float k)
    {
        foreach (var h in hologramMats)
        {
            if (!h.mat) continue;
            if (h.isColor) { var c = h.mat.GetColor(h.prop); c.a = h.alpha * k; h.mat.SetColor(h.prop, c); }
            else h.mat.SetFloat(h.prop, h.alpha * k);
        }
    }

    // =====================================================================
    //  4) Darkness
    // =====================================================================

    void CaptureLights()
    {
        var keep = new HashSet<Light>();
        if (featherLight) keep.Add(featherLight);
        if (lightsToKeep != null) foreach (var l in lightsToKeep) if (l) keep.Add(l);
        if (feather) foreach (var l in feather.GetComponentsInChildren<Light>(true)) keep.Add(l);

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

        if (dimSkybox && RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            if (RenderSettings.skybox != skyCopy)
            {
                skyCopy = new Material(RenderSettings.skybox);   // a copy, so the skybox asset is never changed
                RenderSettings.skybox = skyCopy;
            }
            savedSkyExposure = skyCopy.GetFloat("_Exposure");
        }
        else skyCopy = null;
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

    // 0 = dark, 1 = exactly as it was.
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

    // =====================================================================
    //  4-8) The feather
    // =====================================================================

    IEnumerator FeatherAppear()
    {
        if (!feather)
        {
            Debug.LogWarning("[BreathingExerciseSequence] No Feather assigned.");
            yield break;
        }
        if (featherPoint) feather.transform.position = featherPoint.position;
        if (debugFeatherInFrontOfUser && Camera.main)
        {
            Transform c = Camera.main.transform;
            feather.transform.position = c.position + c.forward * 1.5f;
            Log("TEST mode: the feather is placed 1.5 m in front of the user.");
        }
        featherBase = feather.transform.position;
        featherLift = 0f;
        if (featherGlow) MakeFeatherGlow();
        Log($"Feather appears at {featherBase}. If you can't see it, check Feather Point and that the feather is in front of the user.");

        Vector3 fullScale = feather.transform.localScale;
        feather.transform.localScale = Vector3.zero;
        feather.SetActive(true);
        featherActive = true;

        PrepareFeatherLight();
        float lightFull = featherLight ? featherLight.intensity : 0f;
        if (featherLight) featherLight.intensity = 0f;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, featherAppearSeconds);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            feather.transform.localScale = fullScale * k;
            if (featherLight) featherLight.intensity = lightFull * k;
            yield return null;
        }
        feather.transform.localScale = fullScale;
        if (featherLight) featherLight.intensity = lightFull;
        ReportFeather();
    }

    // Makes sure the (disabled) feather can really be shown.
    void PrepareFeather()
    {
        if (!feather) return;

        // Dragged from the Project window (a prefab asset): it isn't in the scene, so make a copy in the scene.
        if (!feather.scene.IsValid())
        {
            feather = Instantiate(feather);
            feather.SetActive(false);
            Log("Feather was a prefab from the Project window, not an object in the scene: a copy was created in the scene.");
        }

        // SetActive(true) does nothing visible if a parent is disabled.
        for (Transform x = feather.transform.parent; x != null; x = x.parent)
            if (!x.gameObject.activeSelf)
            {
                x.gameObject.SetActive(true);
                Log($"The feather's parent '{x.name}' was disabled: turned it on.");
            }

        // A Rigidbody with gravity would make it fall / fight the floating.
        foreach (var rb in feather.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    // Tells in the console where the feather is and why it might not be seen.
    void ReportFeather()
    {
        if (!feather) return;
        int total = 0, on = 0;
        foreach (var r in feather.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            total++;
            if (r.enabled) on++;
        }
        var cam = Camera.main;
        string dist = cam ? Vector3.Distance(cam.transform.position, feather.transform.position).ToString("0.00") + " m" : "no Main Camera";
        Log($"Feather check: activeInHierarchy={feather.activeInHierarchy}, position={feather.transform.position}, " +
            $"size={feather.transform.lossyScale}, renderers on={on}/{total}, distance to user={dist}.");

        if (!feather.activeInHierarchy) Debug.LogWarning("[BreathingExerciseSequence] The feather is still not active in the scene.");
        if (total == 0) Debug.LogWarning("[BreathingExerciseSequence] The feather has no MeshRenderer / SpriteRenderer: there is nothing to see.");
        else if (on == 0) Debug.LogWarning("[BreathingExerciseSequence] All the feather's renderers are turned off: enable its Mesh Renderer.");
        if (feather.transform.lossyScale.magnitude < 0.01f) Debug.LogWarning("[BreathingExerciseSequence] The feather is tiny (almost 0 scale).");

        // Where the mesh is REALLY drawn (an imported model can be drawn far from its pivot), and whether the camera renders its layer.
        foreach (var r in feather.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            float offset = Vector3.Distance(r.bounds.center, feather.transform.position);
            Log($"Feather mesh '{r.name}': drawn at {r.bounds.center} (size {r.bounds.size}), {offset:0.00} m from the feather's pivot, " +
                $"layer '{LayerMask.LayerToName(r.gameObject.layer)}', material '{(r.sharedMaterial ? r.sharedMaterial.shader.name : "none")}'.");
            if (offset > 0.5f)
                Debug.LogWarning($"[BreathingExerciseSequence] The feather's mesh is drawn {offset:0.00} m away from its pivot: " +
                                 "the model has an offset. Put the mesh as a child of an empty object and center it there, then use the empty as the Feather.");
            if (cam && (cam.cullingMask & (1 << r.gameObject.layer)) == 0)
                Debug.LogWarning($"[BreathingExerciseSequence] The camera does not render layer '{LayerMask.LayerToName(r.gameObject.layer)}': " +
                                 "change the feather's layer or the camera's Culling Mask.");
        }
    }

    // How the (shared) feather light was before the feather borrowed it, e.g. the balloon's light.
    bool featherLightIsAuto, featherLightSaved;
    Transform flParent; Vector3 flLocalPos; Quaternion flLocalRot;
    Color flColor; float flIntensity, flRange; bool flEnabled, flActive;

    // Borrows the assigned light (e.g. the balloon's): moves it onto the feather, turns it on, sets the feather's look.
    void PrepareFeatherLight()
    {
        if (!featherLight) return;
        if (!featherLightIsAuto)
        {
            Transform t = featherLight.transform;
            flParent = t.parent; flLocalPos = t.localPosition; flLocalRot = t.localRotation;
            flColor = featherLight.color; flIntensity = featherLight.intensity; flRange = featherLight.range;
            flEnabled = featherLight.enabled; flActive = featherLight.gameObject.activeSelf;
            featherLightSaved = true;

            // Move it first, so turning it on doesn't also turn the (popped) balloon back on.
            if (moveFeatherLightToFeather && feather)
            {
                t.SetParent(feather.transform, false);
                t.localPosition = new Vector3(0f, 0.15f, 0f);
                t.localRotation = Quaternion.identity;
            }
            if (useFeatherLightSettings)
            {
                featherLight.color = featherLightColor;
                featherLight.intensity = featherLightIntensity;
                featherLight.range = featherLightRange;
            }
            else if (featherLight.intensity <= 0f) featherLight.intensity = featherLightIntensity;   // it was faded out: give it light
        }
        featherLight.gameObject.SetActive(true);
        featherLight.enabled = true;
        if (!featherLight.gameObject.activeInHierarchy)
            Debug.LogWarning("[BreathingExerciseSequence] Feather Light's parent is disabled, so it can't light the feather. " +
                             "Turn on Move Feather Light To Feather, or use another light.");
    }

    // Gives the light back exactly as it was (e.g. to the balloon).
    void RestoreFeatherLight()
    {
        if (!featherLight) return;
        if (featherLightIsAuto || !featherLightSaved) { featherLight.enabled = false; return; }

        Transform t = featherLight.transform;
        t.SetParent(flParent, false);
        t.localPosition = flLocalPos; t.localRotation = flLocalRot;
        featherLight.color = flColor; featherLight.intensity = flIntensity; featherLight.range = flRange;
        featherLight.enabled = flEnabled;
        featherLight.gameObject.SetActive(flActive);
        featherLightSaved = false;
    }

    // Creates a soft point light on the feather if none was assigned.
    void EnsureFeatherLight()
    {
        if (featherLight || !autoFeatherLight || !feather) return;
        featherLightIsAuto = true;
        var go = new GameObject("Feather Light (auto)");
        go.transform.SetParent(feather.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        featherLight = go.AddComponent<Light>();
        featherLight.type = LightType.Point;
        featherLight.color = featherLightColor;
        featherLight.intensity = featherLightIntensity;
        featherLight.range = featherLightRange;
        featherLight.shadows = LightShadows.None;
    }

    // Turns on emission in the feather's materials (URP Lit: _EmissionColor + _EMISSION keyword).
    void MakeFeatherGlow()
    {
        bool any = false;
        foreach (var r in feather.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)
            {
                if (!m.HasProperty("_EmissionColor")) continue;
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", featherGlowColor);
                any = true;
            }
        }
        if (!any) Debug.LogWarning("[BreathingExerciseSequence] The feather's material has no _EmissionColor, so it can't glow. " +
                                   "Use URP Lit (or an Unlit material) on the feather.");
    }

    // One breath: the feather rises (inhale), holds, falls (exhale).
    IEnumerator FeatherBreath(bool skipRest = false)
    {
        PlaySfx(inhaleCue, featherBase);
        yield return MoveFeather(0f, breathHeight, inhaleSeconds);
        if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);

        PlaySfx(exhaleCue, featherBase);
        yield return MoveFeather(breathHeight, 0f, exhaleSeconds);
        if (!skipRest && restSeconds > 0f) yield return new WaitForSeconds(restSeconds);
    }

    IEnumerator MoveFeather(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, seconds);
            featherLift = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        featherLift = to;
    }

    void ExplodeFeather()
    {
        Vector3 pos = feather ? feather.transform.position : featherBase;
        Log("The feather explodes.");

        if (explosionPrefab) Instantiate(explosionPrefab, pos, Quaternion.identity);
        else if (explosionParticles)
        {
            explosionParticles.transform.position = pos;
            explosionParticles.gameObject.SetActive(true);
            explosionParticles.Play();
        }
        PlaySfx(explosionClip, pos);

        featherActive = false;
        RestoreFeatherLight();
        if (feather) feather.SetActive(false);
        onFeatherExploded.Invoke();
    }

    // =====================================================================
    //  10) The patient breathes
    // =====================================================================

    IEnumerator PatientBreathing()
    {
        Transform target = patientBreathTarget ? patientBreathTarget : (patient ? patient.transform : null);
        if (!target)
        {
            Debug.LogWarning("[BreathingExerciseSequence] No patient to breathe.");
            yield break;
        }

        Vector3 baseScale = target.localScale;
        int count = patientBreathAmounts.Length > 0 ? patientBreathAmounts.Length : 4;
        float maxAmount = 0.0001f;
        foreach (var a in patientBreathAmounts) maxAmount = Mathf.Max(maxAmount, a);

        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;
            float amount = patientBreathAmounts.Length > 0 ? patientBreathAmounts[i] : (last ? 0.09f : 0.03f);
            float strength = amount / maxAmount;                 // the strongest is 1
            float length = last ? lastBreathLengthMultiplier : 1f;
            Log($"Patient breath {i + 1} of {count}{(last ? " (the strongest)" : "")}.");

            if (patientAnimator && !string.IsNullOrEmpty(patientBreathTrigger)) patientAnimator.SetTrigger(patientBreathTrigger);

            PlayPatient(patientInhaleClip, strength);
            yield return Swell(target, baseScale, 0f, amount * patientBreathSize, patientInhaleSeconds * length);

            PlayPatient(patientExhaleClip, strength);
            yield return Swell(target, baseScale, amount * patientBreathSize, 0f, patientExhaleSeconds * length);

            if (!last && patientRestSeconds > 0f) yield return new WaitForSeconds(patientRestSeconds);
        }
        target.localScale = baseScale;
    }

    void PlayPatient(AudioClip clip, float volume)
    {
        if (!clip) return;
        if (patientVoice) patientVoice.PlayOneShot(clip, Mathf.Clamp(volume, 0.3f, 1f) * patientBreathVolume);
        else PlaySfx(clip, patient ? patient.transform.position : transform.position, volume);
    }

    IEnumerator Swell(Transform target, Vector3 baseScale, float from, float to, float seconds)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, seconds);
            float a = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            target.localScale = baseScale * (1f + a);
            yield return null;
        }
    }

    static void Log(string msg) => Debug.Log("[BreathingExerciseSequence] " + msg);
}