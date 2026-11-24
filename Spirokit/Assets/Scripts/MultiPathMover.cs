using System;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

public class MultiPathMover : MonoBehaviour
{
    public List<PathData> paths = new List<PathData>();

    [Header("Inicio")]
    public bool playOnStart = true;
    public int startPathIndex = 0;

    [Header("Gizmos")]
    public Color waypointColor = Color.yellow;
    public float waypointRadius = 0.15f;
    [Range(5, 40)] public int curveResolution = 20;

    public int CurrentIndex { get; private set; } = -1;
    readonly HashSet<int> playedPaths = new HashSet<int>();
    Tween currentTween;
    Vector3 startPosition;
    bool started;

    void Start()
    {
        startPosition = transform.position;
        started = true;

        if (playOnStart && paths.Count > 0)
            PlayPath(startPathIndex);
    }

    void OnDestroy()
    {
        currentTween?.Kill();
    }

    // ---------- API pública (llámala desde otros scripts, botones, UnityEvents...) ----------

    public void PlayPath(int index)
    {
        if (index < 0 || index >= paths.Count)
        {
            Debug.LogWarning($"MultiPathMover: no existe el path {index}");
            return;
        }

        PathData data = paths[index];
        if (data.waypoints == null || data.waypoints.Length == 0)
        {
            Debug.LogWarning($"MultiPathMover: el path '{data.name}' no tiene waypoints");
            return;
        }

        // --- NUEVO ---
        if (data.playOnlyOnce && playedPaths.Contains(index))
            return; // ya se reprodujo, se ignora

        playedPaths.Add(index);
        // --------------

        // Corta el path anterior antes de empezar el nuevo
        currentTween?.Kill();
        CurrentIndex = index;

        if (data.snapToFirstWaypoint)
            transform.position = data.waypoints[0].position;

        // Si salta al primer waypoint, ese punto ya es la posición actual, así que se omite
        int first = data.snapToFirstWaypoint ? 1 : 0;
        int count = data.waypoints.Length - first;
        if (count <= 0) return;

        Vector3[] pts = new Vector3[count];
        for (int i = 0; i < count; i++)
            pts[i] = data.waypoints[i + first].position;

        var t = transform.DOPath(pts, data.duration, data.pathType, data.pathMode)
            .SetEase(data.ease);

        if (data.lookAtPath)
            t.SetLookAt(0.01f);

        if (data.loop)
        {
            t.SetLoops(-1, data.loopType);
        }
        else
        {
            t.SetLoops(1);
            t.OnComplete(() =>
            {
                data.onComplete?.Invoke();
                if (data.playNextOnComplete)
                    PlayNext();
            });
        }

        currentTween = t;
    }

    public void PlayPathByName(string pathName)
    {
        int index = paths.FindIndex(p => p.name == pathName);
        if (index >= 0) PlayPath(index);
        else Debug.LogWarning($"MultiPathMover: no existe el path '{pathName}'");
    }

    public void PlayNext()
    {
        if (paths.Count == 0) return;
        PlayPath((CurrentIndex + 1) % paths.Count);
    }

    public void Pause() => currentTween?.Pause();
    public void Resume() => currentTween?.Play();
    public void Stop() => currentTween?.Kill();

    // ---------- Gizmos ----------

    void OnDrawGizmos()
    {
        if (paths == null) return;

        Vector3 origin = started ? startPosition : transform.position;
        Vector3 previousEnd = origin;

        for (int p = 0; p < paths.Count; p++)
        {
            PathData data = paths[p];
            if (data == null || data.waypoints == null) continue;

            List<Vector3> points = new List<Vector3>();

            // Si no salta al primer waypoint, el recorrido arranca desde donde terminó
            // el path anterior (aproximación: en juego depende de cuándo lo actives)
            if (!data.snapToFirstWaypoint)
                points.Add(previousEnd);

            foreach (Transform w in data.waypoints)
                if (w != null) points.Add(w.position);

            if (points.Count == 0) continue;
            previousEnd = points[points.Count - 1];
            if (points.Count < 2) continue;

            // Esferas en los waypoints
            Gizmos.color = waypointColor;
            foreach (Transform w in data.waypoints)
                if (w != null) Gizmos.DrawSphere(w.position, waypointRadius);

            // Línea o curva con el color del path
            Gizmos.color = data.gizmoColor;
            if (data.pathType == PathType.Linear)
            {
                for (int i = 0; i < points.Count - 1; i++)
                    Gizmos.DrawLine(points[i], points[i + 1]);
            }
            else
            {
                DrawCatmullRom(points);
            }
        }
    }

    void DrawCatmullRom(List<Vector3> pts)
    {
        Vector3 prev = pts[0];
        for (int i = 0; i < pts.Count - 1; i++)
        {
            Vector3 p0 = pts[Mathf.Max(i - 1, 0)];
            Vector3 p1 = pts[i];
            Vector3 p2 = pts[i + 1];
            Vector3 p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];

            for (int s = 1; s <= curveResolution; s++)
            {
                float t = s / (float)curveResolution;
                Vector3 next = CatmullRom(p0, p1, p2, p3, t);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
            2f * p1 +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }
}