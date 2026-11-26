using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The step after BreathingExerciseSequence:
///
///  1) The guide robot says LOC18.
///  2) The mouthpiece on the floating tray lights up (a pulsing light + glow).
///     A translucent ghost shows where it goes on the patient (the mouth).
///  3) The user grabs it and puts it on the patient robot: when it gets close to its spot
///     it snaps into place and stays there. If dropped far away it flies back to the tray.
///  4) When it is on: a sound + On All Placed.
///
/// Start: assign Breathing Sequence (it listens to its On Finished by itself), or call Begin().
/// The mouthpiece needs a Collider. If they have no XR Grab Interactable, one is added automatically.
/// </summary>
public class MouthpieceSequence : MonoBehaviour
{
    [System.Serializable]
    public class Piece
    {
        public string name = "Piece";
        [Tooltip("The piece on the floating tray.")]
        public GameObject item;
        [Tooltip("Where it goes on the patient: an empty object as a CHILD of the patient (on the nose / the mouth). " +
                 "Its position AND rotation are how the piece will sit, so rotate it the way the piece should look.")]
        public Transform target;
        [Tooltip("Optional: your own marker / ghost at the target (disabled in the scene). Empty + Auto Ghost = one is made from the piece.")]
        public GameObject targetMarker;
        [Tooltip("Optional: the light that highlights the piece. Empty + Auto Light = one is created.")]
        public Light highlightLight;

        [System.NonSerialized] public bool placed, held, wasHeld;
        [System.NonSerialized] public Behaviour interactable;
        [System.NonSerialized] public Rigidbody rb;
        [System.NonSerialized] public bool rbKinematic, rbGravity;
        [System.NonSerialized] public Transform homeParent;
        [System.NonSerialized] public Vector3 homeLocalPos;
        [System.NonSerialized] public Quaternion homeLocalRot;
        [System.NonSerialized] public float lightFull;
        [System.NonSerialized] public bool lightIsAuto;
        [System.NonSerialized] public GameObject ghost;
        [System.NonSerialized] public Coroutine returning;
        [System.NonSerialized] public readonly List<Material> glowMats = new List<Material>();
        [System.NonSerialized] public readonly List<Color> glowOriginal = new List<Color>();
        [System.NonSerialized] public readonly List<bool> glowHadKeyword = new List<bool>();
    }

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
    [Tooltip("ON: the mouthpiece lights up while LOC18 is playing. OFF: after it ends.")]
    public bool highlightDuringLine = false;

    [Header("2) The mouthpiece")]
    public Piece mouthpiece = new Piece { name = "Mouthpiece" };
    [Tooltip("ON: the mouthpiece can't be grabbed before this step starts.")]
    public bool lockUntilStep = true;

    [Header("2) Highlight")]
    public bool autoLight = true;
    public Color lightColor = new Color(1f, 0.85f, 0.55f);
    public float lightIntensity = 2.5f;
    public float lightRange = 0.6f;
    [Tooltip("How high above the piece the auto light sits.")]
    public float lightHeight = 0.15f;
    [Tooltip("The piece's material glows (emission).")]
    public bool glow = true;
    [ColorUsage(false, true)] public Color glowColor = new Color(1f, 0.85f, 0.55f) * 1.2f;
    [Tooltip("Pulses per second.")]
    public float pulseSpeed = 1.2f;
    [Tooltip("The lowest point of the pulse (0.4 = 40% of full).")]
    [Range(0f, 1f)] public float pulseMin = 0.4f;
    public float highlightAppearSeconds = 1f;
    [Tooltip("ON: the piece keeps its light while the user holds it. OFF: the light turns off when it is grabbed.")]
    public bool keepHighlightWhileHeld = false;

    [Header("2) Ghost on the patient")]
    [Tooltip("If a piece has no Target Marker, make a translucent copy of it at its target.")]
    public bool autoGhost = true;
    public Color ghostColor = new Color(0.6f, 0.9f, 1f, 0.35f);
    [Tooltip("ON: the ghost only shows while the user is holding that piece.")]
    public bool ghostOnlyWhileHeld = false;

    [Header("3) Placing")]
    [Tooltip("How close (meters) the piece must be to its target to snap.")]
    public float snapDistance = 0.12f;
    [Tooltip("ON: it snaps as soon as it's close, even while held. OFF: only when the user lets go near the target.")]
    public bool snapWhileHeld = true;
    public float snapSeconds = 0.3f;
    [Tooltip("Turn off the piece's colliders once placed, so it doesn't push the patient.")]
    public bool disableCollidersWhenPlaced = true;
    public AudioClip placedClip;
    [Tooltip("A piece dropped far from its target flies back to the tray.")]
    public bool returnIfDropped = true;
    public float returnDelay = 1f;
    public float returnSeconds = 0.8f;

    [Header("4) End")]
    public AudioSource sfx;
    public AudioClip allPlacedClip;
    [Range(0f, 3f)] public float sfxVolume = 1.6f;

    [Header("Events")]
    public UnityEvent onStarted;
    public UnityEvent onPiecePlaced;
    public UnityEvent onAllPlaced;

    // =====================================================================
    //  State
    // =====================================================================

    bool running, active, highlighting;
    float highlightLevel;   // 0..1 fade-in of the highlight
    Piece[] pieces;

    void Start()
    {
        pieces = new[] { mouthpiece };
        foreach (var p in pieces) SetupPiece(p);
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

        // 1) LOC18 (2: the highlight can start with it)
        if (highlightDuringLine) StartStep();
        Log("LOC18");
        if (loc18 && guideVoice)
        {
            guideVoice.Stop();
            guideVoice.PlayOneShot(loc18, voiceVolume);
            yield return new WaitForSeconds(loc18.length);
        }
        else
        {
            Debug.LogWarning($"[MouthpieceSequence] LOC18 has no clip or no Guide Voice: waiting {secondsIfNoClip}s instead.");
            yield return new WaitForSeconds(secondsIfNoClip);
        }

        // 2) The mouthpiece lights up and can be grabbed
        if (!highlightDuringLine) StartStep();

        // 3) Wait until it is on the patient
        while (!AllPlaced()) yield return null;

        // 4) Done
        yield return new WaitForSeconds(snapSeconds);
        active = false;
        PlaySfx(allPlacedClip);
        Log("The mouthpiece is on the patient. Finished.");
        running = false;
        onAllPlaced.Invoke();
    }

    void StartStep()
    {
        active = true;
        foreach (var p in pieces)
        {
            if (!p.item) continue;
            if (p.interactable) p.interactable.enabled = true;
            SetLight(p, true);
            if (glow) SetupGlow(p);
            if (!ghostOnlyWhileHeld) ShowGhost(p, true);
        }
        StartCoroutine(HighlightFadeIn());
        Log("The mouthpiece is highlighted: the user can grab it.");
    }

    IEnumerator HighlightFadeIn()
    {
        highlighting = true;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, highlightAppearSeconds);
            highlightLevel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            yield return null;
        }
        highlightLevel = 1f;
    }

    bool AllPlaced()
    {
        foreach (var p in pieces) if (p.item && !p.placed) return false;
        return true;
    }

    // =====================================================================
    //  Every frame: highlight pulse, grab / release, snapping
    // =====================================================================

    void Update()
    {
        if (!active || pieces == null) return;

        float pulse = Mathf.Lerp(pulseMin, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f));
        float k = highlighting ? highlightLevel * pulse : 0f;

        foreach (var p in pieces)
        {
            if (!p.item || p.placed) continue;

            p.held = IsSelected(p.interactable);

            // Grabbed / released
            if (p.held && !p.wasHeld)
            {
                Log($"{p.name} grabbed.");
                if (p.returning != null) { StopCoroutine(p.returning); p.returning = null; }
                if (ghostOnlyWhileHeld) ShowGhost(p, true);
            }
            else if (!p.held && p.wasHeld)
            {
                if (ghostOnlyWhileHeld) ShowGhost(p, false);
                bool near = p.target && Vector3.Distance(p.item.transform.position, p.target.position) <= snapDistance;
                if (!near && returnIfDropped) p.returning = StartCoroutine(ReturnToTray(p));
            }
            p.wasHeld = p.held;

            // Highlight
            bool lit = !p.held || keepHighlightWhileHeld;
            float pk = lit ? k : 0f;
            if (p.highlightLight) p.highlightLight.intensity = p.lightFull * pk;
            for (int i = 0; i < p.glowMats.Count; i++)
                if (p.glowMats[i]) p.glowMats[i].SetColor("_EmissionColor", p.glowOriginal[i] + glowColor * pk);

            // Snap
            if (p.target && (snapWhileHeld || !p.held) &&
                Vector3.Distance(p.item.transform.position, p.target.position) <= snapDistance)
                StartCoroutine(Place(p));
        }
    }

    // =====================================================================
    //  Placing on the patient
    // =====================================================================

    IEnumerator Place(Piece p)
    {
        p.placed = true;
        if (p.returning != null) { StopCoroutine(p.returning); p.returning = null; }
        Log($"{p.name} placed on the patient.");

        // Disabling the interactable makes the hand let go of it.
        if (p.interactable) p.interactable.enabled = false;
        yield return null;

        if (p.rb) { p.rb.isKinematic = true; p.rb.useGravity = false; }
        if (disableCollidersWhenPlaced)
            foreach (var c in p.item.GetComponentsInChildren<Collider>()) c.enabled = false;

        SetLight(p, false);
        RestoreGlow(p);
        ShowGhost(p, false);
        PlaySfx(placedClip);

        Transform t = p.item.transform;
        t.SetParent(p.target, true);   // follows the patient from now on
        Vector3 fromPos = t.localPosition;
        Quaternion fromRot = t.localRotation;
        float k = 0f;
        while (k < 1f)
        {
            k += Time.deltaTime / Mathf.Max(0.01f, snapSeconds);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            t.localPosition = Vector3.Lerp(fromPos, Vector3.zero, s);
            t.localRotation = Quaternion.Slerp(fromRot, Quaternion.identity, s);
            yield return null;
        }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        onPiecePlaced.Invoke();
    }

    IEnumerator ReturnToTray(Piece p)
    {
        yield return new WaitForSeconds(returnDelay);
        if (p.held || p.placed) yield break;
        Log($"{p.name} was dropped: it goes back to the tray.");

        if (p.rb) { p.rb.isKinematic = true; p.rb.useGravity = false; }
        Transform t = p.item.transform;
        Vector3 fromPos = t.position;
        Quaternion fromRot = t.rotation;
        float k = 0f;
        while (k < 1f)
        {
            if (p.held || p.placed) yield break;
            k += Time.deltaTime / Mathf.Max(0.01f, returnSeconds);
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            Vector3 homePos = p.homeParent ? p.homeParent.TransformPoint(p.homeLocalPos) : p.homeLocalPos;
            Quaternion homeRot = p.homeParent ? p.homeParent.rotation * p.homeLocalRot : p.homeLocalRot;
            t.SetPositionAndRotation(Vector3.Lerp(fromPos, homePos, s), Quaternion.Slerp(fromRot, homeRot, s));
            yield return null;
        }
        t.SetParent(p.homeParent, true);
        t.localPosition = p.homeLocalPos;
        t.localRotation = p.homeLocalRot;
        if (p.rb)
        {
            p.rb.linearVelocity = Vector3.zero;
            p.rb.angularVelocity = Vector3.zero;
            p.rb.isKinematic = p.rbKinematic;
            p.rb.useGravity = p.rbGravity;
        }
        p.returning = null;
    }

    // =====================================================================
    //  Setup
    // =====================================================================

    void SetupPiece(Piece p)
    {
        if (!p.item)
        {
            Debug.LogWarning($"[MouthpieceSequence] {p.name}: no Item assigned.");
            return;
        }
        if (!p.target) Debug.LogWarning($"[MouthpieceSequence] {p.name}: no Target on the patient, so it can't be placed.");
        if (!p.item.GetComponentInChildren<Collider>(true))
            Debug.LogWarning($"[MouthpieceSequence] {p.name} has no Collider, so it can't be grabbed. Add one.");

        Transform t = p.item.transform;
        p.homeParent = t.parent;
        p.homeLocalPos = t.localPosition;
        p.homeLocalRot = t.localRotation;

        // XR Grab Interactable (found / added by name, so it works with any XR Interaction Toolkit version).
        p.interactable = FindInteractable(p.item);
        if (!p.interactable)
        {
            var grabType = FindXRType("XRGrabInteractable");
            if (grabType != null) p.interactable = p.item.AddComponent(grabType) as Behaviour;
            if (p.interactable) Log($"{p.name} had no XR interactable: an XR Grab Interactable was added.");
            else Debug.LogWarning($"[MouthpieceSequence] Couldn't add an XR Grab Interactable to {p.name}. Add one yourself.");
        }
        else if (!p.interactable.GetType().Name.Contains("Grab"))
            Debug.LogWarning($"[MouthpieceSequence] {p.name} has a '{p.interactable.GetType().Name}', which can't be carried. " +
                             "Replace it with an XR Grab Interactable.");
        if (p.interactable && lockUntilStep) p.interactable.enabled = false;

        p.rb = p.item.GetComponent<Rigidbody>();
        if (p.rb) { p.rbKinematic = p.rb.isKinematic; p.rbGravity = p.rb.useGravity; }

        // Light
        if (!p.highlightLight && autoLight)
        {
            var go = new GameObject($"{p.name} Highlight (auto)");
            go.transform.SetParent(t, false);
            go.transform.position = t.position + Vector3.up * lightHeight;
            p.highlightLight = go.AddComponent<Light>();
            p.highlightLight.type = LightType.Point;
            p.highlightLight.color = lightColor;
            p.highlightLight.intensity = lightIntensity;
            p.highlightLight.range = lightRange;
            p.highlightLight.shadows = LightShadows.None;
            p.lightIsAuto = true;
        }
        if (p.highlightLight)
        {
            p.lightFull = p.lightIsAuto ? lightIntensity : Mathf.Max(p.highlightLight.intensity, 0.01f);
            p.highlightLight.enabled = false;
        }

        // Ghost at the target
        if (p.targetMarker) { p.ghost = p.targetMarker; p.ghost.SetActive(false); }
        else if (autoGhost && p.target) p.ghost = MakeGhost(p);
    }

    void SetLight(Piece p, bool on)
    {
        if (!p.highlightLight) return;
        if (on)
        {
            for (Transform x = p.highlightLight.transform; x != null; x = x.parent)
                if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
            p.highlightLight.intensity = 0f;
        }
        p.highlightLight.enabled = on;
    }

    void SetupGlow(Piece p)
    {
        p.glowMats.Clear(); p.glowOriginal.Clear(); p.glowHadKeyword.Clear();
        foreach (var r in p.item.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (var m in r.materials)
            {
                if (!m.HasProperty("_EmissionColor")) continue;
                p.glowHadKeyword.Add(m.IsKeywordEnabled("_EMISSION"));
                p.glowOriginal.Add(m.IsKeywordEnabled("_EMISSION") ? m.GetColor("_EmissionColor") : Color.black);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                p.glowMats.Add(m);
            }
        }
    }

    void RestoreGlow(Piece p)
    {
        for (int i = 0; i < p.glowMats.Count; i++)
        {
            var m = p.glowMats[i];
            if (!m) continue;
            m.SetColor("_EmissionColor", p.glowOriginal[i]);
            if (!p.glowHadKeyword[i]) m.DisableKeyword("_EMISSION");
        }
        p.glowMats.Clear();
    }

    void ShowGhost(Piece p, bool on)
    {
        if (p.ghost) p.ghost.SetActive(on && !p.placed);
    }

    // A translucent copy of the piece's meshes, sitting at its target on the patient.
    GameObject MakeGhost(Piece p)
    {
        Material mat = MakeGhostMaterial();
        if (!mat)
        {
            Debug.LogWarning("[MouthpieceSequence] Couldn't make the ghost material (URP Unlit not found). " +
                             "Assign your own Target Marker instead.");
            return null;
        }

        Transform item = p.item.transform;
        var root = new GameObject($"{p.name} Ghost (auto)").transform;
        root.SetParent(p.target, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Divide(item.lossyScale, p.target.lossyScale);

        foreach (var mf in p.item.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf.sharedMesh) continue;
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
        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    Material MakeGhostMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) return null;
        var m = new Material(shader);
        m.SetFloat("_Surface", 1f);   // Transparent
        m.SetFloat("_Blend", 0f);     // Alpha
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetColor("_BaseColor", ghostColor);
        return m;
    }

    static Vector3 Divide(Vector3 a, Vector3 b) =>
        new Vector3(b.x != 0 ? a.x / b.x : 1f, b.y != 0 ? a.y / b.y : 1f, b.z != 0 ? a.z / b.z : 1f);

    // =====================================================================
    //  XR helpers (by name, so no fixed reference to the XR Interaction Toolkit version)
    // =====================================================================

    static Behaviour FindInteractable(GameObject go)
    {
        foreach (var b in go.GetComponentsInChildren<Behaviour>(true))
            for (var t = b.GetType(); t != null; t = t.BaseType)
                if (t.Name == "XRBaseInteractable") return b;
        return null;
    }

    static bool IsSelected(Behaviour b)
    {
        if (!b) return false;
        var prop = b.GetType().GetProperty("isSelected", BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.GetValue(b, null) is bool sel && sel;
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