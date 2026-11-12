using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Flow: the rising object finishes rising -> audio plays -> a sparkle flies from the rising object to
/// station 0 while that station's light glows up. The active station pulses until the user touches it ->
/// it moves to its Move Spot -> the sparkle flies from it to station 1 -> ... After the last station is
/// touched, a finale lights up everything: all station lights, the room lights and a final light.
/// Each station is touched via a StationTouchRelay.
/// </summary>
public class SparkleTrailChain : MonoBehaviour
{
    [System.Serializable]
    public class Station
    {
        public string name;
        [Tooltip("The touchable object. The sparkle flies to it, and it is what the user touches.")]
        public Transform target;
        [Tooltip("Light that glows up while the sparkle travels to this station.")]
        public Light light;
        public float maxIntensity = 50f;
        [Tooltip("Where this station's object moves to when touched. Leave empty to not move.")]
        public Transform moveSpot;
        [Tooltip("Hide this station until the sparkle starts flying to it.")]
        public bool startHidden;

        [Header("Sparkle to this station (optional overrides)")]
        [Tooltip("Particle system used for the trail that flies TO this station. Empty = use the default Sparkle.")]
        public ParticleSystem sparkle;
        [Tooltip("Tick to use the Travel Duration, Arc Height and Side Curve below instead of the defaults.")]
        public bool useCustomPath;
        public float travelDuration = 2.5f;
        [Tooltip("Positive = arcs up, negative = dips down, 0 = straight line.")]
        public float arcHeight = 0.3f;
        [Tooltip("Sideways bend: moves the middle of the path to the side (meters).")]
        public float sideCurve = 0f;
        [Tooltip("Corkscrew radius for this leg (used when Use Custom Path is ticked).")]
        public float spiralRadius = 0.15f;

        [Header("Where the sparkle aims")]
        [Tooltip("Optional. An empty child object placed at the visible center of the station. " +
                 "The sparkle flies to and from this point instead of the station's pivot. Empty = use the station's position.")]
        public Transform aimPoint;

        [Header("Pulse while waiting to be touched (optional)")]
        [Tooltip("Renderer whose material emission pulses. The material needs Emission enabled. Empty = no emission pulse.")]
        public Renderer glowRenderer;
        [ColorUsage(false, true)]
        public Color glowColor = new Color(1f, 0.7f, 0.3f);
    }

    [Header("Audio (plays when the rise ends)")]
    public AudioSource audioSource;
    public AudioClip clip;                 // optional: overrides the AudioSource clip

    [Header("Sparkle trail")]
    public ParticleSystem sparkle;         // Play On Awake OFF, Simulation Space = World
    [Tooltip("Optional. Where the very first sparkle starts (e.g. an empty at the center of the rising object). Empty = the rising object's position.")]
    public Transform startPoint;
    public float travelDuration = 2.5f;
    public float arcHeight = 0.3f;
    public AnimationCurve travelCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [Tooltip("Pause (seconds) with the sparkle emitting at the start point before it departs.")]
    public float startDelay = 0.5f;
    [Tooltip("The sparkle corkscrews around the straight line. 0 = off.")]
    public float spiralRadius = 0.15f;
    public float spiralTurns = 2f;
    [Tooltip("Keep the sparkle hovering over the station until it is touched.")]
    public bool keepSparkleAtStation = true;
    [Tooltip("ON: the sparkle flies in a perfectly straight line (ignores arc, side curve and spiral).")]
    public bool directPath = true;

    [Header("Stations (in order)")]
    public Station[] stations;

    [Header("Focus on the active station")]
    [Tooltip("When the sparkle heads to a new station, earlier stations' lights dim to this fraction of their max (0 = off, 1 = stay full).")]
    [Range(0f, 1f)] public float previousLightLevel = 0.25f;
    public float lightFadeOutDuration = 1f;
    [Tooltip("Optional room lights (e.g. ceiling light, directional light) that dim when the chain begins and come back at the finale.")]
    public Light[] roomLights;
    [Range(0f, 1f)] public float roomDimLevel = 0.15f;
    public float roomDimDuration = 2f;

    [Header("Pulse while waiting for the touch")]
    public float pulseSpeed = 3f;
    [Range(0f, 1f)] public float lightPulseAmount = 0.25f;
    [Range(0f, 0.5f)] public float scalePulseAmount = 0.05f;
    [Range(0f, 1f)] public float emissionPulseAmount = 0.5f;

    [Header("Finale (after the last station is touched)")]
    [Tooltip("The big final light. Starts at 0 and glows up at the end.")]
    public Light finalLight;
    public float finalLightIntensity = 150f;
    public float finaleDelay = 0.5f;
    public float finaleDuration = 4f;
    [Tooltip("Station lights fade to this fraction of their max at the finale (0 = off, so the final light is the only one).")]
    [Range(0f, 1f)] public float stationLightsAtFinale = 0f;
    [Tooltip("Tick to bring the room lights back at the finale. Off = they stay dimmed.")]
    public bool restoreRoomLights = false;
    public AudioSource finaleSound;

    [Header("When a station is touched")]
    public float moveDuration = 1.5f;
    public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Events")]
    public UnityEvent onChainFinished;

    Transform startSource;                 // the rising object
    Vector3[] stationStartPositions;
    Vector3[] stationBaseScales;
    float[] roomOriginalIntensities;
    int currentIndex = -1;                 // station the sparkle has reached and is waiting to be touched
    bool busy;
    bool waitingForTouch;
    ParticleSystem activePs;               // sparkle currently hovering at a station
    Coroutine pulseRoutine;
    int pulsingIndex = -1;

    void Awake()
    {
        if (stations != null)
        {
            stationStartPositions = new Vector3[stations.Length];
            stationBaseScales = new Vector3[stations.Length];
            for (int i = 0; i < stations.Length; i++)
            {
                if (stations[i] != null && stations[i].target)
                {
                    stationStartPositions[i] = stations[i].target.position;
                    stationBaseScales[i] = stations[i].target.localScale;
                }
            }
        }

        if (roomLights != null)
        {
            roomOriginalIntensities = new float[roomLights.Length];
            for (int i = 0; i < roomLights.Length; i++)
                roomOriginalIntensities[i] = roomLights[i] ? roomLights[i].intensity : 0f;
        }

        ClearVisuals();
    }

    // Called by RiseAndHologramSequence when the rise ends.
    public void Begin(Transform source)
    {
        if (busy || stations == null || stations.Length == 0) return;
        startSource = source;
        StartCoroutine(BeginRoutine());
    }

    // Called by StationTouchRelay when a station is touched.
    public void OnStationTouched(Transform touched)
    {
        Debug.Log($"[SparkleTrailChain] Touched '{(touched ? touched.name : "null")}'. waitingForTouch={waitingForTouch}, busy={busy}, activeStation={currentIndex}");
        if (!waitingForTouch || busy) return;

        // Accept the target itself, or a parent/child of it (in case the relay sits on a part of the station).
        Transform active = currentIndex >= 0 ? stations[currentIndex].target : null;
        bool isActive = active && touched &&
                        (touched == active || touched.IsChildOf(active) || active.IsChildOf(touched));

        if (!isActive)
        {
            Debug.Log("[SparkleTrailChain] That is not the active station, ignoring.");
            return;
        }
        StartCoroutine(TouchRoutine());
    }

    // Debug helper: in Play mode, right-click the component header > "Test Touch".
    [ContextMenu("Test Touch")]
    void TestTouch()
    {
        if (currentIndex >= 0) OnStationTouched(stations[currentIndex].target);
        else Debug.Log("[SparkleTrailChain] Nothing to touch yet.");
    }

    public void ResetChain()
    {
        StopPulse();
        StopAllCoroutines();
        busy = false;
        waitingForTouch = false;
        currentIndex = -1;
        if (audioSource) audioSource.Stop();
        if (finaleSound) finaleSound.Stop();

        if (stations != null && stationStartPositions != null)
        {
            for (int i = 0; i < stations.Length && i < stationStartPositions.Length; i++)
            {
                if (stations[i] != null && stations[i].target)
                {
                    stations[i].target.position = stationStartPositions[i];
                    stations[i].target.localScale = stationBaseScales[i];
                }
            }
        }

        if (roomLights != null && roomOriginalIntensities != null)
            for (int i = 0; i < roomLights.Length; i++)
                if (roomLights[i]) roomLights[i].intensity = roomOriginalIntensities[i];

        ClearVisuals();
    }

    IEnumerator BeginRoutine()
    {
        busy = true;

        // Dim the room so the glowing station stands out.
        if (roomLights != null && roomOriginalIntensities != null)
            for (int i = 0; i < roomLights.Length; i++)
                if (roomLights[i])
                    StartCoroutine(FadeTo(roomLights[i], roomOriginalIntensities[i] * roomDimLevel, roomDimDuration));

        if (audioSource)
        {
            if (clip) audioSource.clip = clip;
            if (audioSource.clip)
            {
                audioSource.Play();
                yield return null;                       // let isPlaying become true
                while (audioSource.isPlaying) yield return null;
            }
        }

        // First leg: from the rising object to station 0.
        Vector3 origin = startPoint ? startPoint.position
                       : startSource ? startSource.position
                       : AimPoint(stations[0]);
        yield return SendSparkleTo(0, origin);
        waitingForTouch = true;
        StartPulse(0);
        busy = false;
    }

    IEnumerator TouchRoutine()
    {
        busy = true;
        waitingForTouch = false;

        // Stop the pulse and the sparkle that was hovering over this station.
        StopPulse();
        if (activePs) activePs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        activePs = null;

        Station s = stations[currentIndex];

        // The touched station moves to its spot.
        if (s.moveSpot)
        {
            Vector3 from = s.target.position;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, moveDuration);
                s.target.position = Vector3.LerpUnclamped(from, s.moveSpot.position, moveCurve.Evaluate(Mathf.Clamp01(t)));
                yield return null;
            }
            s.target.position = s.moveSpot.position;
        }

        // Then the sparkle flies from it to the next station, or the finale begins.
        if (currentIndex + 1 < stations.Length)
        {
            // Straight from the object that was just touched to the next station.
            yield return SendSparkleTo(currentIndex + 1, AimPoint(s));
            waitingForTouch = true;
            StartPulse(currentIndex);
        }
        else
        {
            StartCoroutine(Finale());
            onChainFinished.Invoke();
        }

        busy = false;
    }

    IEnumerator SendSparkleTo(int index, Vector3 from)
    {
        Station s = stations[index];

        // Make sure the station and its light are switched on before the sparkle heads there.
        if (s.target && !s.target.gameObject.activeSelf) s.target.gameObject.SetActive(true);
        if (s.light)
        {
            if (!s.light.gameObject.activeSelf) s.light.gameObject.SetActive(true);
            s.light.enabled = true;
        }
        if (s.target)
        {
            // Re-enable the interaction components too, in case they were left disabled in the scene.
            foreach (var c in s.target.GetComponentsInChildren<Collider>(true)) c.enabled = true;
            foreach (var b in s.target.GetComponentsInChildren<Behaviour>(true))
                if (b is StationTouchRelay || b.GetType().Name == "XRSimpleInteractable") b.enabled = true;
        }

        // The previous station's light dims (instead of switching off) so the new one stands out.
        if (index > 0 && stations[index - 1].light && previousLightLevel < 1f)
            StartCoroutine(FadeTo(stations[index - 1].light, stations[index - 1].maxIntensity * previousLightLevel, lightFadeOutDuration));

        // Each station can have its own particle system and path shape.
        ParticleSystem ps = s.sparkle ? s.sparkle : sparkle;
        float duration = s.useCustomPath ? s.travelDuration : travelDuration;
        float arc = s.useCustomPath ? s.arcHeight : arcHeight;
        float side = s.useCustomPath ? s.sideCurve : 0f;
        float spiral = s.useCustomPath ? s.spiralRadius : spiralRadius;
        if (directPath) { arc = 0f; side = 0f; spiral = 0f; }

        if (ps) PrepareSparkle(ps);
        Debug.Log($"[SparkleTrailChain] Sparkle to station {index}: from {from} to {AimPoint(s)} (world positions), particles = {(ps ? ps.name : "NONE (no Sparkle assigned)")}");
        Debug.DrawLine(from, AimPoint(s), Color.yellow, 15f);   // yellow line in the Scene view (and Game view with Gizmos on)

        if (ps)
        {
            ps.transform.position = from;
            ps.Play();
        }
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        float tt = 0f;
        while (tt < 1f)
        {
            tt += Time.deltaTime / Mathf.Max(0.01f, duration);
            float p = travelCurve.Evaluate(Mathf.Clamp01(tt));

            if (ps)
            {
                Vector3 to = AimPoint(s);
                Vector3 pos = Vector3.Lerp(from, to, p);
                float bend = Mathf.Sin(p * Mathf.PI);
                pos += Vector3.up * bend * arc;

                if (side != 0f)
                {
                    Vector3 d = to - from;
                    Vector3 sideDir = Vector3.Cross(d, Vector3.up).normalized;
                    pos += sideDir * bend * side;
                }
                if (spiral > 0f)
                {
                    Vector3 dir = (to - from);
                    dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
                    Vector3 right = Vector3.Cross(dir, Vector3.up);
                    right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
                    Vector3 up2 = Vector3.Cross(right, dir).normalized;
                    float angle = p * spiralTurns * Mathf.PI * 2f;
                    pos += (right * Mathf.Cos(angle) + up2 * Mathf.Sin(angle)) * spiral * bend;
                }
                ps.transform.position = pos;
            }
            // Same "slowly glow" behaviour as the first spotlight, synced to the sparkle's progress.
            if (s.light) s.light.intensity = s.maxIntensity * p;

            yield return null;
        }

        activePs = ps;
        if (ps && !keepSparkleAtStation) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing sparks fade out
        if (s.light) s.light.intensity = s.maxIntensity;
        currentIndex = index;
    }

    // ---------- Pulse (active station, until it is touched) ----------

    void StartPulse(int index)
    {
        StopPulse();
        if (stations == null || index < 0 || index >= stations.Length) return;
        pulsingIndex = index;
        pulseRoutine = StartCoroutine(Pulse(index));
    }

    void StopPulse()
    {
        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        pulseRoutine = null;

        if (pulsingIndex >= 0 && stations != null && pulsingIndex < stations.Length)
        {
            Station s = stations[pulsingIndex];
            if (s.light) s.light.intensity = s.maxIntensity;
            if (s.target && stationBaseScales != null) s.target.localScale = stationBaseScales[pulsingIndex];
            SetEmission(s, 1f);
        }
        pulsingIndex = -1;
    }

    IEnumerator Pulse(int index)
    {
        Station s = stations[index];
        if (s.glowRenderer) s.glowRenderer.material.EnableKeyword("_EMISSION");

        while (true)
        {
            float k = Mathf.Sin(Time.time * pulseSpeed);
            if (s.light) s.light.intensity = s.maxIntensity * (1f + lightPulseAmount * k);
            if (s.target) s.target.localScale = stationBaseScales[index] * (1f + scalePulseAmount * k);
            SetEmission(s, 1f + emissionPulseAmount * k);
            yield return null;
        }
    }

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    MaterialPropertyBlock emissionBlock;

    void SetEmission(Station s, float multiplier)
    {
        if (!s.glowRenderer) return;
        if (emissionBlock == null) emissionBlock = new MaterialPropertyBlock();
        s.glowRenderer.GetPropertyBlock(emissionBlock);
        emissionBlock.SetColor(EmissionColorId, s.glowColor * multiplier);
        s.glowRenderer.SetPropertyBlock(emissionBlock);
    }

    // ---------- Finale ----------

    IEnumerator Finale()
    {
        if (finaleDelay > 0f) yield return new WaitForSeconds(finaleDelay);

        if (finaleSound) finaleSound.Play();

        // Station lights fade away (or to the chosen level) so the final light is the one that lights the room.
        foreach (var st in stations)
            if (st != null && st.light) StartCoroutine(FadeTo(st.light, st.maxIntensity * stationLightsAtFinale, finaleDuration));

        // Room lights only come back if you ask for it.
        if (restoreRoomLights && roomLights != null && roomOriginalIntensities != null)
            for (int i = 0; i < roomLights.Length; i++)
                if (roomLights[i]) StartCoroutine(FadeTo(roomLights[i], roomOriginalIntensities[i], finaleDuration));

        // And the final light glows up.
        if (finalLight)
        {
            if (!finalLight.gameObject.activeSelf) finalLight.gameObject.SetActive(true);
            finalLight.enabled = true;
            StartCoroutine(FadeTo(finalLight, finalLightIntensity, finaleDuration));
        }
    }

    // ---------- Helpers ----------

    // World position the sparkle aims at for a station.
    Vector3 AimPoint(Station s)
    {
        if (s.aimPoint) return s.aimPoint.position;
        return s.target ? s.target.position : transform.position;
    }

    // Makes sure the particle system can actually be seen, and warns about common setup mistakes.
    void PrepareSparkle(ParticleSystem ps)
    {
        if (!ps.gameObject.scene.IsValid())
            Debug.LogWarning($"[SparkleTrailChain] '{ps.name}' is an asset (prefab), not an object in the scene. Drag the Particle System from the Hierarchy instead.");

        if (!ps.gameObject.activeSelf) ps.gameObject.SetActive(true);
        if (!ps.gameObject.activeInHierarchy)
            Debug.LogWarning($"[SparkleTrailChain] '{ps.name}' is inside an inactive parent, so it can't play.");

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;   // leaves a trail behind the moving emitter
        main.loop = true;                                             // keeps emitting for the whole flight
        main.playOnAwake = false;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend && !rend.enabled) rend.enabled = true;
    }

    IEnumerator FadeTo(Light l, float to, float duration)
    {
        if (!l) yield break;
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

    void ClearVisuals()
    {
        activePs = null;
        if (sparkle) sparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (finalLight) finalLight.intensity = 0f;
        if (stations == null) return;
        foreach (var s in stations)
        {
            if (s == null) continue;
            if (s.light) s.light.intensity = 0f;
            if (s.startHidden && s.target) s.target.gameObject.SetActive(false);
            if (s.sparkle) s.sparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}