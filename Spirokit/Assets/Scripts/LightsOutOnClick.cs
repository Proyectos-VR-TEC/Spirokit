using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The step after RobotDemoSequence's finale:
///
/// 1) WAIT: this script does nothing until it gets the signal. The signal is the finale sound of RobotDemoSequence
///    (assign it to Sequence and it listens by itself), or a call to Begin() (e.g. from a UnityEvent).
///
/// 2) CLICK: from then on it waits for the user to click a third object (Click Target: a ray press on it, XR Select).
///    If the object has no XR interactable one is added automatically (it only needs a collider).
///
/// 3) LIGHTS OUT: when the user clicks it, all the lights glow down to dark, except one specific light (Keep Light)
///    that stays on. The ambient light and the sky go dark too, so the scene really becomes dark.
///
/// Only real-time lights can be dimmed at runtime: baked lighting stays as baked.
/// </summary>
public class LightsOutOnClick : MonoBehaviour
{
    [Header("Start signal")]
    [Tooltip("The RobotDemoSequence in the scene. When it plays its finale sound, this script starts waiting for the click.")]
    public RobotDemoSequence sequence;
    [Tooltip("ON: start waiting when the Sequence's finale sound plays. OFF: only when Begin() is called.")]
    public bool startOnFinaleSound = true;
    [Tooltip("ON: clicks count from the very beginning, without waiting for the signal.")]
    public bool listenFromTheStart = false;

    [Header("The object the user clicks")]
    [Tooltip("The third object. It needs a Collider. If it has no XR interactable, an XR Simple Interactable is added automatically. " +
             "Its Interaction Layer Mask must match your ray's.")]
    public GameObject clickTarget;

    [Header("Lights")]
    [Tooltip("The ONE light that stays on when everything else goes dark.")]
    public Light keepLight;
    [Tooltip("Optional: only dim these lights. Empty = ALL the lights in the scene except Keep Light.")]
    public Light[] lightsToDim;
    [Tooltip("Seconds the lights take to glow down to dark.")]
    public float fadeDuration = 3f;
    [Tooltip("Seconds between the click and the start of the fade.")]
    public float delayAfterClick = 0f;
    [Tooltip("Also fade the ambient light and the reflections to black, so the scene is really dark.")]
    public bool dimAmbientLight = true;
    [Tooltip("Also fade the sky (only works with a skybox material that has an Exposure property).")]
    public bool dimSkybox = true;

    [Header("Feedback")]
    public AudioSource sfx;
    [Tooltip("Played when the user clicks the object.")]
    public AudioClip clickClip;
    [Tooltip("Fires when the scene has finished going dark.")]
    public UnityEvent onLightsOut;

    bool listening;
    bool triggered;

    void Start()
    {
        if (sequence && startOnFinaleSound) sequence.onFinaleSound.AddListener(Begin);
        if (listenFromTheStart) listening = true;
        HookClickTarget();
    }

    void OnDestroy()
    {
        if (sequence) sequence.onFinaleSound.RemoveListener(Begin);
    }

    // The signal: from now on a click on the Click Target turns the lights out.
    public void Begin()
    {
        listening = true;
        Debug.Log("[LightsOutOnClick] Signal received: waiting for the click on the Click Target.");
    }

    // Can also be hooked by hand to any event (e.g. the third object's Select Entered). Ignored until Begin() has been called.
    public void Click()
    {
        if (!listening || triggered) return;
        triggered = true;
        StartCoroutine(LightsOut());
    }

    // Lets this script be used again.
    public void ResetScript()
    {
        StopAllCoroutines();
        triggered = false;
        listening = listenFromTheStart;
    }

    // =====================================================================
    //  The click
    // =====================================================================

    void HookClickTarget()
    {
        if (!clickTarget)
        {
            Debug.LogWarning("[LightsOutOnClick] No Click Target assigned: nothing can be clicked. " +
                             "Assign the third object, or hook Click() to an event by hand.");
            return;
        }
        if (!clickTarget.GetComponentInChildren<Collider>(true))
            Debug.LogWarning($"[LightsOutOnClick] '{clickTarget.name}' has no Collider, so the ray can't hit it. Add one.");

        // Any XR interactable on the target (XR Simple / Grab / ...). Found by name so it works with any XR Interaction Toolkit version.
        Behaviour interactable = null;
        foreach (var b in clickTarget.GetComponentsInChildren<Behaviour>(true))
        {
            for (var t = b.GetType(); t != null; t = t.BaseType)
                if (t.Name == "XRBaseInteractable") { interactable = b; break; }
            if (interactable) break;
        }

        if (!interactable)
        {
            var simpleType = FindXRType("XRSimpleInteractable");
            if (simpleType != null) interactable = clickTarget.AddComponent(simpleType) as Behaviour;
            if (interactable) Debug.Log($"[LightsOutOnClick] '{clickTarget.name}' had no XR interactable, so an XR Simple Interactable was added.");
        }
        if (!interactable)
        {
            Debug.LogWarning($"[LightsOutOnClick] Couldn't find or add an XR interactable on '{clickTarget.name}'. " +
                             "Add an XR Simple Interactable yourself and hook its Select Entered event to this script's Click().");
            return;
        }

        interactable.enabled = true;

        // Subscribe to its 'selectEntered' event (a ray press) without a fixed reference to the XR types.
        try
        {
            object evt = null;
            var flags = BindingFlags.Public | BindingFlags.Instance;
            var prop = interactable.GetType().GetProperty("selectEntered", flags);
            if (prop != null) evt = prop.GetValue(interactable, null);
            else
            {
                var field = interactable.GetType().GetField("selectEntered", flags);
                if (field != null) evt = field.GetValue(interactable);
            }
            if (evt == null) throw new System.Exception("no 'selectEntered' event found");

            var add = evt.GetType().GetMethod("AddListener");
            var actionType = add.GetParameters()[0].ParameterType;          // UnityAction<SelectEnterEventArgs>
            var argType = actionType.GetGenericArguments()[0];
            var handler = typeof(LightsOutOnClick)
                .GetMethod(nameof(OnSelectEvent), BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(argType);
            add.Invoke(evt, new object[] { System.Delegate.CreateDelegate(actionType, this, handler) });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[LightsOutOnClick] Couldn't listen to '{clickTarget.name}' automatically ({e.Message}). " +
                             "Hook its Select Entered event to this script's Click() in the Inspector.");
        }
    }

    // Receives the XR event, whatever its argument type is.
    void OnSelectEvent<T>(T args)
    {
        Click();
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
    //  The lights glow down
    // =====================================================================

    IEnumerator LightsOut()
    {
        Debug.Log("[LightsOutOnClick] Clicked: the lights are going out.");
        if (clickClip)
        {
            if (sfx) sfx.PlayOneShot(clickClip);
            else AudioSource.PlayClipAtPoint(clickClip, clickTarget ? clickTarget.transform.position : transform.position);
        }
        if (delayAfterClick > 0f) yield return new WaitForSeconds(delayAfterClick);

        // The ONE light that stays: make sure it is on.
        if (keepLight)
        {
            for (Transform x = keepLight.transform; x != null; x = x.parent)
                if (!x.gameObject.activeSelf) x.gameObject.SetActive(true);
            keepLight.enabled = true;
        }

        // Which lights go dark.
        var lights = new List<Light>();
        if (lightsToDim != null && lightsToDim.Length > 0) lights.AddRange(lightsToDim);
        else
        {
#if UNITY_2022_2_OR_NEWER
            lights.AddRange(FindObjectsByType<Light>(FindObjectsSortMode.None));
#else
            lights.AddRange(FindObjectsOfType<Light>());
#endif
        }
        lights.RemoveAll(l => !l || l == keepLight);

        var from = new float[lights.Count];
        for (int i = 0; i < lights.Count; i++) from[i] = lights[i].intensity;

        // Ambient light, reflections and sky.
        Color ambColor = RenderSettings.ambientLight;
        float ambIntensity = RenderSettings.ambientIntensity;
        float reflIntensity = RenderSettings.reflectionIntensity;

        Material sky = null;
        float skyExposure = 1f;
        if (dimSkybox && RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure"))
        {
            sky = new Material(RenderSettings.skybox);   // a copy, so the skybox asset itself is never changed
            RenderSettings.skybox = sky;
            skyExposure = sky.GetFloat("_Exposure");
        }

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, fadeDuration);
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            for (int i = 0; i < lights.Count; i++)
                if (lights[i]) lights[i].intensity = from[i] * k;

            if (dimAmbientLight)
            {
                RenderSettings.ambientIntensity = ambIntensity * k;
                RenderSettings.ambientLight = ambColor * k;
                RenderSettings.reflectionIntensity = reflIntensity * k;
            }
            if (sky) sky.SetFloat("_Exposure", skyExposure * k);

            yield return null;
        }

        for (int i = 0; i < lights.Count; i++)
            if (lights[i]) lights[i].intensity = 0f;
        if (dimAmbientLight)
        {
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0f;
        }
        if (sky) sky.SetFloat("_Exposure", 0f);

        onLightsOut.Invoke();
    }
}