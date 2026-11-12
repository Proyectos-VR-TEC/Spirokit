using UnityEngine;

/// <summary>
/// Put this on every station object (next to its XR Simple Interactable) and hook
/// Touched() to the interactable's Select Entered (or Hover Entered) event.
/// It tells the SparkleTrailChain which station was touched.
/// </summary>
public class StationTouchRelay : MonoBehaviour
{
    public SparkleTrailChain chain;

    [Tooltip("The station this touch belongs to (the same object used as Target in the chain's Stations list). " +
             "Leave empty if this component is on the station object itself.")]
    public Transform station;

    public void Touched()
    {
        if (chain) chain.OnStationTouched(station ? station : transform);
        else Debug.LogWarning("[StationTouchRelay] No chain assigned on " + name);
    }
}