using System;
using UnityEngine;
using UnityEngine.Events;
using DG.Tweening;

[Serializable]
public class PathData
{
    public string name = "Path";
    public Transform[] waypoints;

    [Header("Movimiento")]
    public float duration = 3f;
    public PathType pathType = PathType.CatmullRom;
    public PathMode pathMode = PathMode.Full3D;
    public Ease ease = Ease.InOutSine;
    public bool lookAtPath = true;

    [Header("Loop")]
    public bool loop = false;
    public LoopType loopType = LoopType.Restart; // Restart o Yoyo (ida y vuelta)

    [Header("Inicio")]
    [Tooltip("Si está activo, el objeto salta al primer waypoint. Si no, viaja desde donde esté.")]
    public bool snapToFirstWaypoint = false;

    [Header("Al terminar (solo si no hace loop)")]
    public bool playNextOnComplete = false;
    public UnityEvent onComplete;

    [Header("Gizmo")]
    public Color gizmoColor = Color.cyan;

    [Header("Reproducción")]
    [Tooltip("Si está activo, este path solo puede reproducirse una vez. Las siguientes llamadas se ignoran.")]
    public bool playOnlyOnce = false;
}

/*
name: el nombre del path. Se usa en PlayPathByName("nombre"), que distingue mayúsculas de minúsculas, así 
que conviene que sea único y que lo escribas exactamente igual.

waypoints: los Transforms por los que pasa el objeto, en orden. Dos consejos:

Las posiciones se leen cuando llamas a PlayPath, así que puedes mover los waypoints entre ejecuciones y se 
respetan.
No los pongas como hijos del objeto que se mueve, porque se irían moviendo con él. Déjalos en la raíz de la
 escena o bajo un objeto vacío fijo.
Movimiento

duration: segundos que tarda en recorrer el path completo. Si el path tiene loop, es el tiempo de cada vuelta.
 La velocidad depende de la longitud del recorrido, así que un path largo con la misma duración irá más rápido 
 que uno corto.

pathType: la forma en que se unen los puntos.

Linear: líneas rectas entre waypoints, con esquinas marcadas.
CatmullRom: curva suave que pasa por todos los waypoints. Suele ser la mejor opción para efectos.
CubicBezier: curva con puntos de control. Los waypoints se leen en grupos de tres: destino y dos puntos 
de control, y los puntos de control no se recorren, solo curvan el trayecto. Es más preciso pero menos intuitivo. 
Ten en cuenta que mis gizmos lo dibujan solo de forma aproximada.

pathMode: cómo DOTween calcula la orientación del objeto y los cálculos de rotación.

Full3D: sin restricciones, para movimiento en cualquier eje. Es el que debes usar normalmente.
TopDown2D y Sidescroller2D: para juegos 2D, donde la rotación se limita a un solo eje.
Ignore: no aplica cálculos especiales.

ease: cómo cambia la velocidad a lo largo del recorrido.

Linear da velocidad constante.
InOutSine arranca y frena suavemente, y es una buena opción por defecto.
InQuad o InCubic aceleran hacia el final, y OutQuad u OutCubic frenan hacia el final.

Para un efecto que se mueve de forma uniforme, usa Linear.


lookAtPath: si está activo, el objeto rota para mirar hacia donde avanza (en su eje Z, es decir, su "forward"). 
Conviene si el efecto emite partículas en la dirección del movimiento. Si no lo necesitas, desactívalo.

Loop

loop: si está activo, el path se repite indefinidamente. Si no, se reproduce una sola vez. En un path con loop, 
el evento onComplete nunca se dispara y playNextOnComplete se ignora, porque el path no termina.

loopType: solo importa si loop está activo.

Restart: al llegar al final, salta al inicio y vuelve a empezar.
Yoyo: al llegar al final, hace el recorrido de vuelta en sentido contrario, y así sucesivamente.
Inicio

snapToFirstWaypoint: define de dónde parte el objeto.

Desactivado: viaja desde donde esté hasta el primer waypoint y sigue el path. Los cambios entre paths son continuos.
Activado: el objeto salta directamente al primer waypoint y desde ahí sigue el recorrido. Útil si cada path es 
independiente.
Al terminar (solo sin loop)

playNextOnComplete: al terminar este path, arranca automáticamente el siguiente de la lista (y vuelve al primero 
tras el último). Sirve para encadenar secuencias.

onComplete: un evento del Inspector que se dispara al terminar el path. Puedes enlazar métodos igual que en un botón: 
activar otro objeto, reproducir un sonido, apagar el Particle System, etc. Se ejecuta antes de pasar al siguiente path.

Gizmo

gizmoColor: el color con el que se dibuja el recorrido en la Scene view. Solo es visual, no afecta al movimiento.
*/