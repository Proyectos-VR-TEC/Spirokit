using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// One button press -> several objects rise, spotlight glows in sync, holographic text appears on the wall.
/// Hook Trigger() to your VR button (XR Simple Interactable > Select Entered, or a UI Button OnClick).
/// </summary>
public class RiseAndHologramSequence : MonoBehaviour
{
    [System.Serializable]
    public class RisingObject
    {
        public Transform target;
        public float riseHeight = 1.5f;
        [Tooltip("Seconds to wait after the button press before this object starts rising.")]
        public float startDelay = 0f;
    }

    [Header("Button")]
    public GameObject buttonToHide;        // the button that triggers this
    public float buttonShrinkDuration = 0.25f; // 0 = disappear instantly

    [Header("Next step (optional)")]
    public SparkleTrailChain chain;        // audio + sparkle trail chain, starts when the rise ends
    public Transform chainTarget;          // object the chain follows; defaults to the first rising object

    [Header("Rising objects")]
    public RisingObject[] objectsToRise;
    public float riseDuration = 4f;        // duration of each individual object's rise
    public AnimationCurve riseCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Spotlight (glows as the objects rise)")]
    public Light spotLight;
    public float maxIntensity = 8f;
    public AnimationCurve glowCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Holographic text on the wall")]
    public TMP_Text hologramText;          // TextMeshPro (3D) placed on the wall
    public float hologramDelay = 0.5f;     // wait after button press
    public float hologramFadeDuration = 1.5f;
    public Color hologramColor = new Color(0.2f, 0.9f, 1f, 1f);
    [Range(0f, 1f)] public float flickerAmount = 0.2f;
    public float flickerSpeed = 20f;
    public bool typewriter = true;
    public float charactersPerSecond = 25f;

    bool running;
    Vector3[] startPositions;
    Vector3 buttonScale;
    string fullText;

    void Awake()
    {
        CacheStartPositions();
        if (spotLight) spotLight.intensity = 0f;
        if (hologramText)
        {
            fullText = hologramText.text;
            SetHologramAlpha(0f);
        }
    }

    void CacheStartPositions()
    {
        int n = objectsToRise != null ? objectsToRise.Length : 0;
        startPositions = new Vector3[n];
        for (int i = 0; i < n; i++)
            if (objectsToRise[i] != null && objectsToRise[i].target != null)
                startPositions[i] = objectsToRise[i].target.position;
    }

    // Call this from the button.
    public void Trigger()
    {
        if (running) return;
        if (buttonToHide) StartCoroutine(HideButton());
        StartCoroutine(Sequence());
    }

    IEnumerator HideButton()
    {
        Transform b = buttonToHide.transform;
        buttonScale = b.localScale;

        float t = 0f;
        while (t < 1f && buttonShrinkDuration > 0f)
        {
            t += Time.deltaTime / buttonShrinkDuration;
            b.localScale = Vector3.Lerp(buttonScale, Vector3.zero, Mathf.Clamp01(t));
            yield return null;
        }

        buttonToHide.SetActive(false);
        b.localScale = buttonScale; // restored so it looks normal when re-enabled
    }

    // Optional: call to put everything back (e.g. from a second button).
    public void ResetSequence()
    {
        StopAllCoroutines();
        running = false;
        if (chain) chain.ResetChain();
        if (buttonToHide)
        {
            if (buttonScale != Vector3.zero) buttonToHide.transform.localScale = buttonScale;
            buttonToHide.SetActive(true);
        }
        for (int i = 0; i < objectsToRise.Length; i++)
            if (objectsToRise[i] != null && objectsToRise[i].target != null)
                objectsToRise[i].target.position = startPositions[i];
        if (spotLight) spotLight.intensity = 0f;
        if (hologramText)
        {
            hologramText.text = fullText;
            SetHologramAlpha(0f);
        }
    }

    IEnumerator Sequence()
    {
        running = true;

        // Hologram runs in parallel and keeps flickering after the fade-in.
        if (hologramText) StartCoroutine(Hologram());

        float duration = Mathf.Max(0.01f, riseDuration);

        // Total time = longest start delay + one rise duration.
        float maxDelay = 0f;
        foreach (var item in objectsToRise)
            if (item != null) maxDelay = Mathf.Max(maxDelay, item.startDelay);
        float totalTime = maxDelay + duration;

        // All objects share one clock; each one maps it to its own progress.
        // The light follows overall progress, so it peaks when the last object arrives.
        float elapsed = 0f;
        while (elapsed < totalTime)
        {
            elapsed += Time.deltaTime;

            for (int i = 0; i < objectsToRise.Length; i++)
            {
                var item = objectsToRise[i];
                if (item == null || item.target == null) continue;

                float p = Mathf.Clamp01((elapsed - item.startDelay) / duration);
                Vector3 endPos = startPositions[i] + Vector3.up * item.riseHeight;
                item.target.position = Vector3.LerpUnclamped(startPositions[i], endPos, riseCurve.Evaluate(p));
            }

            if (spotLight)
                spotLight.intensity = maxIntensity * glowCurve.Evaluate(Mathf.Clamp01(elapsed / totalTime));

            yield return null;
        }

        // Snap to exact final values.
        for (int i = 0; i < objectsToRise.Length; i++)
        {
            var item = objectsToRise[i];
            if (item == null || item.target == null) continue;
            item.target.position = startPositions[i] + Vector3.up * item.riseHeight;
        }
        if (spotLight) spotLight.intensity = maxIntensity;

        if (chain) chain.Begin(GetChainTarget());
        // 'running' stays true so the button can't be spammed; call ResetSequence() to replay.
    }

    Transform GetChainTarget()
    {
        if (chainTarget) return chainTarget;
        foreach (var item in objectsToRise)
            if (item != null && item.target != null) return item.target;
        return null;
    }

    IEnumerator Hologram()
    {
        yield return new WaitForSeconds(hologramDelay);

        if (typewriter) hologramText.text = "";
        float t = 0f;
        float baseAlpha = 0f;
        int shown = 0;

        while (true)
        {
            t += Time.deltaTime;
            baseAlpha = Mathf.Clamp01(t / Mathf.Max(0.01f, hologramFadeDuration));

            // Flicker only once it's mostly visible, like an unstable projection.
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f);
            float flicker = 1f - flickerAmount * noise * baseAlpha;
            SetHologramAlpha(baseAlpha * flicker);

            if (typewriter && shown < fullText.Length)
            {
                shown = Mathf.Min(fullText.Length, Mathf.FloorToInt(t * charactersPerSecond));
                hologramText.text = fullText.Substring(0, shown);
            }

            yield return null;
        }
    }

    void SetHologramAlpha(float a)
    {
        var c = hologramColor;
        c.a = a;
        hologramText.color = c;
    }
}