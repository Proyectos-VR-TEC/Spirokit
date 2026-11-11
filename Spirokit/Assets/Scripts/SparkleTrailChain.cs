using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// After the object finishes rising: plays an audio, then a sparkle trail travels from the object
/// to the first station while that station's light glows up. When the user touches the object it
/// moves to a spot, then the sparkle goes to the next station, and so on.
/// </summary>
public class SparkleTrailChain : MonoBehaviour
{
    [System.Serializable]
    public class Station
    {
        public string name;
        [Tooltip("Object the sparkle flies to.")]
        public Transform target;
        [Tooltip("Light that glows up while the sparkle travels here.")]
        public Light light;
        public float maxIntensity = 50f;
        [Tooltip("Where the main object moves when touched. Leave empty to use the target's position.")]
        public Transform moveSpot;
    }

    [Header("Audio (plays when the rise ends)")]
    public AudioSource audioSource;
    public AudioClip clip;                 // optional: overrides the AudioSource clip

    [Header("Sparkle trail")]
    public ParticleSystem sparkle;         // Play On Awake OFF, Simulation Space = World
    public float travelDuration = 2.5f;
    public float arcHeight = 0.3f;
    public AnimationCurve travelCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Stations (in order)")]
    public Station[] stations;
    public bool turnOffPreviousLight = true;
    public float lightFadeOutDuration = 1f;

    [Header("When the user touches the object")]
    public float moveDuration = 1.5f;
    public AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Events")]
    public UnityEvent onChainFinished;

    Transform movingObject;
    int currentIndex = -1;
    bool busy;
    bool waitingForTouch;

    void Awake()
    {
        ClearVisuals();
    }

    // Called by RiseAndHologramSequence when the rise ends.
    public void Begin(Transform obj)
    {
        if (busy || stations == null || stations.Length == 0) return;
        movingObject = obj;
        StartCoroutine(BeginRoutine());
    }

    // Hook this to the moving object's XR Simple Interactable (Hover Entered or Select Entered).
    public void OnObjectTouched()
    {
        Debug.Log($"[SparkleTrailChain] OnObjectTouched called. waitingForTouch={waitingForTouch}, busy={busy}");
        if (!waitingForTouch || busy) return;
        StartCoroutine(TouchRoutine());
    }

    // Debug helper: in Play mode, right-click the component header > "Test Touch".
    // Lets you check the chain works without any XR interaction.
    [ContextMenu("Test Touch")]
    void TestTouch()
    {
        OnObjectTouched();
    }

    public void ResetChain()
    {
        StopAllCoroutines();
        busy = false;
        waitingForTouch = false;
        currentIndex = -1;
        if (audioSource) audioSource.Stop();
        ClearVisuals();
    }

    IEnumerator BeginRoutine()
    {
        busy = true;

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

        yield return SendSparkleTo(0);
        waitingForTouch = true;
        busy = false;
    }

    IEnumerator TouchRoutine()
    {
        busy = true;
        waitingForTouch = false;

        // Move the object to the current station's spot.
        Station s = stations[currentIndex];
        Transform spot = s.moveSpot ? s.moveSpot : s.target;
        Vector3 from = movingObject.position;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, moveDuration);
            movingObject.position = Vector3.LerpUnclamped(from, spot.position, moveCurve.Evaluate(Mathf.Clamp01(t)));
            yield return null;
        }
        movingObject.position = spot.position;

        // Then the sparkle goes to the next station, or the chain ends.
        if (currentIndex + 1 < stations.Length)
        {
            yield return SendSparkleTo(currentIndex + 1);
            waitingForTouch = true;
        }
        else
        {
            onChainFinished.Invoke();
        }

        busy = false;
    }

    IEnumerator SendSparkleTo(int index)
    {
        Station s = stations[index];

        if (turnOffPreviousLight && index > 0)
            StartCoroutine(FadeOut(stations[index - 1].light, stations[index - 1].maxIntensity));

        Vector3 from = movingObject.position;
        if (sparkle)
        {
            sparkle.transform.position = from;
            sparkle.Play();
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, travelDuration);
            float p = travelCurve.Evaluate(Mathf.Clamp01(t));

            if (sparkle)
            {
                Vector3 pos = Vector3.Lerp(from, s.target.position, p);
                pos += Vector3.up * Mathf.Sin(p * Mathf.PI) * arcHeight;
                sparkle.transform.position = pos;
            }
            // Same "slowly glow" behaviour as the first spotlight, synced to the sparkle's progress.
            if (s.light) s.light.intensity = s.maxIntensity * p;

            yield return null;
        }

        if (sparkle) sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting); // existing sparks fade out
        if (s.light) s.light.intensity = s.maxIntensity;
        currentIndex = index;
    }

    IEnumerator FadeOut(Light l, float from)
    {
        if (!l) yield break;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, lightFadeOutDuration);
            l.intensity = Mathf.Lerp(from, 0f, Mathf.Clamp01(t));
            yield return null;
        }
        l.intensity = 0f;
    }

    void ClearVisuals()
    {
        if (sparkle) sparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (stations == null) return;
        foreach (var s in stations)
            if (s != null && s.light) s.light.intensity = 0f;
    }
}