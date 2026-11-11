using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dibuja trazos arbitrarios desde un JSON con coordenadas UV normalizadas.
///
/// Integra la misma lógica de RobotProgram:
///   - MoveJ (interpolación de ángulos en espacio de joints) entre trazos
///   - CalculateApproachPose para encontrar configuración estable antes de dibujar
///   - PenUp entre trazos antes de reconfigurarse
///   - MoveJ_Home al terminar
///
/// Flujo por trazo:
///   CalculateApproachPose → MoveJ_Approach → MoveL_Draw → PenUp → siguiente trazo
///   Al terminar todos los trazos → MoveJ_Home → Finished
/// </summary>
[RequireComponent(typeof(CCDIKSolver))]
public class StrokeDrawer : MonoBehaviour
{
    // ── Enums ─────────────────────────────────────────────────────────────────

    public enum DrawPlane { XZ_Horizontal, XY_Vertical }

    public enum Phase
    {
        Idle,
        MoveJ_Approach,          // Interpolación de joints hacia pose de inicio del trazo
        MoveL_Draw,              // Tracer siguiendo los puntos del trazo (IK activo)
        PenUp_BetweenStrokes,    // Levantamiento del marcador entre trazos
        MoveJ_Home,              // Regreso a pose home al terminar
        Finished
    }

    [System.Serializable]
    public class JointAngles { public float[] angles = new float[6]; }

    // ── Clases para parsear JSON ───────────────────────────────────────────────

    [System.Serializable]
    private class UVPoint { public float u; public float v; }

    [System.Serializable]
    private class StrokeJSON { public List<UVPoint> points; }

    [System.Serializable]
    private class StrokeDataJSON
    {
        public int image_width  = 1;
        public int image_height = 1;
        public List<StrokeJSON> strokes;
    }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Referencias")]
    public Transform tracer;
    public Transform anchorToEndEffector;

    [Header("Pizarrón")]
    public Transform drawOrigin;

    [Tooltip("XZ_Horizontal: mesa. U→Right, V→Forward, Lift→Up.\n" +
             "XY_Vertical: pared. U→Right, V→Up, Lift→Forward.")]
    public DrawPlane drawPlane = DrawPlane.XZ_Horizontal;

    public float boardWidthMeters  = 0.6f;
    public float boardHeightMeters = 0.25f;

    [Header("Pen-up entre trazos")]
    public float liftHeight = 0.04f;
    public float penUpSpeed = 0.08f;

    [Header("Resampleo de segmentos largos")]
    [Tooltip("Distancia máxima entre puntos consecutivos dentro de un trazo. " +
             "Segmentos más largos se subdividen para evitar saltos bruscos del IK.")]
    public float maxSegmentMeters = 0.02f;

    [Header("Sincronización con IK")]
    public bool waitForEndEffector = true;
    public float reachTolerance = 0.01f;

    [Header("Velocidad de dibujo")]
    public float drawSpeed   = 0.04f;
    public float travelSpeed = 0.08f;

    [Header("Pose HOME")]
    [Tooltip("Pose a la que vuelve el robot al terminar todos los trazos. " +
             "Igual que en RobotProgram. Copia los ángulos desde el Inspector de RobotProgram.")]
    public JointAngles homePose = new JointAngles();

    [Header("Velocidad MoveJ")]
    [Tooltip("Grados por segundo para la interpolación de joints.")]
    public float moveJSpeed = 30f;
    public float minMoveJDuration = 2f;

    [Header("Cálculo de pose de aproximación")]
    [Range(10, 200)]
    [Tooltip("Iteraciones del IK para calcular la pose de inicio de cada trazo. " +
             "Más iteraciones = pose más estable pero más lento al iniciar.")]
    public int approachSolverIterations = 80;

    [Header("Visualización")]
    public Color trailColor  = Color.cyan;
    public Color travelColor = new Color(1f, 0.5f, 0f, 0.4f);
    public float trailWidth  = 0.003f;
    public bool showTravelMoves = true;

    [Header("Estado (solo lectura)")]
    [SerializeField] private Phase currentPhase = Phase.Idle;
    [SerializeField] private int currentStrokeIndex = 0;
    [SerializeField] private string info = "";

    [Header("Prueba")]
    public bool dibujarPruebaAlInicio = false;

    public enum FiguraPrueba
    {
        Cuadrado,
        DosBandas,
        Cruz,
        EleInvertida,
        Escalera,
        TrianguloCentrado,
    }
    public FiguraPrueba figuraPrueba = FiguraPrueba.Cruz;

    // ── Estado interno ────────────────────────────────────────────────────────

    private CCDIKSolver ccdSolver;

    // MoveJ (igual que RobotProgram)
    private float[] moveJStart;
    private float[] moveJTarget;
    private float moveJProgress;
    private float moveJDuration;

    // PenUp
    private Vector3 penUpStartPos;
    private Vector3 penUpEndPos;
    private float penUpProgress;

    // Trazos en espacio mundo (ya resampleados)
    private List<List<Vector3>> allStrokes;
    private int totalStrokeCount;
    private float imageAspectRatio = 1f; // ancho/alto de la imagen original

    // Dibujo del trazo actual
    private int currentPointIndex;
    private float drawProgress;

    // LineRenderers
    private List<LineRenderer> drawLines   = new List<LineRenderer>();
    private List<LineRenderer> travelLines = new List<LineRenderer>();
    private LineRenderer currentDrawLine;

    public bool IsDrawing  => currentPhase != Phase.Idle && currentPhase != Phase.Finished;
    public bool IsFinished => currentPhase == Phase.Finished;

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    void Awake()
    {
        // Awake corre antes de cualquier Start() — así ccdSolver siempre está
        // disponible cuando otros scripts llaman a IniciarDibujo() desde su Start().
        ccdSolver = GetComponent<CCDIKSolver>();
        if (ccdSolver == null) { Debug.LogError("[StrokeDrawer] Sin CCDIKSolver."); enabled = false; return; }

        if (homePose.angles == null || homePose.angles.Length != 6)
            homePose.angles = new float[6];

        ccdSolver.enabled = false;
    }

    void Start()
    {
        // Posicionar Tracer en el centro del área para que el IK no busque un target raro
        if (tracer != null)
            tracer.position = UVtoWorld(0.5f, 0.5f);

        if (dibujarPruebaAlInicio)
            IniciarDibujo(JsonFiguraPrueba(figuraPrueba));
    }

    // ── API pública ───────────────────────────────────────────────────────────

    /// <summary>
    /// Punto de entrada principal. Recibe el JSON del servidor y arranca el dibujo.
    /// </summary>
    public void IniciarDibujo(string json)
    {
        StrokeDataJSON data;
        try { data = JsonUtility.FromJson<StrokeDataJSON>(json); }
        catch (System.Exception e)
        {
            Debug.LogError($"[StrokeDrawer] Error al parsear JSON: {e.Message}");
            return;
        }

        if (data == null || data.strokes == null || data.strokes.Count == 0)
        {
            Debug.LogWarning("[StrokeDrawer] JSON vacío o sin trazos.");
            return;
        }

        LimpiarDibujo();

        // Relación de aspecto de la imagen original (ancho/alto)
        // Se usa para escalar el dibujo correctamente en el board.
        imageAspectRatio = (data.image_width > 0 && data.image_height > 0)
            ? (float)data.image_width / data.image_height
            : 1f;

        // Construir lista de trazos en espacio mundo con resampleo
        allStrokes = new List<List<Vector3>>();
        int totalPuntos = 0;

        foreach (var s in data.strokes)
        {
            if (s.points == null || s.points.Count < 2) continue;

            var raw = new List<Vector3>();
            foreach (var p in s.points)
                raw.Add(UVtoWorld(p.u, p.v));

            var resampled = Resamplear(raw, maxSegmentMeters);
            allStrokes.Add(resampled);
            totalPuntos += resampled.Count;
        }

        if (allStrokes.Count == 0)
        {
            Debug.LogWarning("[StrokeDrawer] No se generaron trazos válidos.");
            return;
        }

        totalStrokeCount = allStrokes.Count;
        ccdSolver.enabled = false;
        currentPhase = Phase.Idle;

        Debug.Log($"[StrokeDrawer] Iniciando: {totalStrokeCount} trazos, {totalPuntos} puntos.");
        DialogueManager.Instance?.IniciarEtapa7(totalPuntos);

        StartStroke(0);
    }

    /// <summary>
    /// Detiene todo lo que esté ocurriendo y regresa el robot a homePose
    /// de forma instantánea. Para usar cuando el usuario presiona Home.
    /// </summary>
    public void ResetCompleto()
    {
        // 1. Detener coroutines activas (MoveJ, PenUp, etc.)
        StopAllCoroutines();

        // 2. Limpiar trazos visuales
        foreach (var lr in drawLines)   if (lr != null) Destroy(lr.gameObject);
        foreach (var lr in travelLines) if (lr != null) Destroy(lr.gameObject);
        drawLines.Clear();
        travelLines.Clear();
        currentDrawLine = null;
        allStrokes = null;

        // 3. Apagar IK
        if (ccdSolver != null) ccdSolver.enabled = false;

        // 4. Regresar joints a homePose instantáneamente
        if (homePose.angles != null && homePose.angles.Length == 6)
        {
            for (int i = 0; i < 6; i++)
                SetJointAngle(i, homePose.angles[i]);
        }

        imageAspectRatio = 1f;
        // 5. Regresar Tracer al centro del board
        if (tracer != null)
            tracer.position = UVtoWorld(0.5f, 0.5f);

        // 6. Reset de estado interno
        currentPhase       = Phase.Idle;
        currentStrokeIndex = 0;
        currentPointIndex  = 0;
        drawProgress       = 0f;
        penUpProgress      = 0f;
        moveJProgress      = 0f;
        info               = "";

        Debug.Log("[StrokeDrawer] Reset completo. Robot en homePose.");
    }

    /// <summary>
    /// Borra todos los trazos dibujados y resetea el estado.
    /// </summary>
    public void LimpiarDibujo()
    {
        foreach (var lr in drawLines)   if (lr != null) Destroy(lr.gameObject);
        foreach (var lr in travelLines) if (lr != null) Destroy(lr.gameObject);
        drawLines.Clear();
        travelLines.Clear();
        currentDrawLine = null;
        allStrokes = null;
        currentPhase = Phase.Idle;
        if (ccdSolver != null) ccdSolver.enabled = false;
    }

    // ── Máquina de estados ────────────────────────────────────────────────────

    void Update()
    {
        switch (currentPhase)
        {
            case Phase.MoveJ_Approach:
            case Phase.MoveJ_Home:
                UpdateMoveJ();
                break;

            case Phase.MoveL_Draw:
                UpdateDraw();
                break;

            case Phase.PenUp_BetweenStrokes:
                UpdatePenUp();
                break;
        }
    }

    // ── Inicio de trazo ───────────────────────────────────────────────────────

    void StartStroke(int index)
    {
        if (index >= allStrokes.Count)
        {
            Debug.Log("[StrokeDrawer] Todos los trazos completados. MoveJ hacia HOME.");
            StartMoveJ(GetCurrentJointAngles(), homePose.angles, Phase.MoveJ_Home);
            return;
        }

        currentStrokeIndex = index;
        currentPointIndex  = 0;
        drawProgress       = 0f;
        currentDrawLine    = null;
        info = $"[{index + 1}/{totalStrokeCount}]";

        // Posicionar Tracer en el inicio del trazo y calcular pose de aproximación
        if (tracer != null)
            tracer.position = allStrokes[index][0];

        float[] approachAngles = CalculateApproachPose();
        StartMoveJ(GetCurrentJointAngles(), approachAngles, Phase.MoveJ_Approach);

        Debug.Log($"[StrokeDrawer] --- Iniciando trazo {info} ({allStrokes[index].Count} puntos) ---");
    }

    // ── Cálculo de pose de aproximación (igual que RobotProgram) ─────────────

    float[] CalculateApproachPose()
    {
        // Guardar estado
        float[] initialAngles = GetCurrentJointAngles();
        var initialRotations = new Quaternion[ccdSolver.joints.Length];
        for (int i = 0; i < ccdSolver.joints.Length; i++)
            initialRotations[i] = ccdSolver.joints[i].joint.localRotation;

        Transform originalTarget = ccdSolver.target;
        if (tracer != null) ccdSolver.target = tracer;

        // Resolver IK en seco para el inicio del trazo
        int saved = ccdSolver.maxIterations;
        ccdSolver.maxIterations = approachSolverIterations;
        ccdSolver.SolveOneFrame();
        ccdSolver.maxIterations = saved;

        float[] solved = GetCurrentJointAngles();

        // Restaurar estado
        for (int i = 0; i < ccdSolver.joints.Length; i++)
        {
            ccdSolver.joints[i].joint.localRotation = initialRotations[i];
            ccdSolver.joints[i].currentAngle        = initialAngles[i];
        }
        ccdSolver.target = originalTarget;

        return solved;
    }

    // ── MoveJ (igual que RobotProgram) ────────────────────────────────────────

    void StartMoveJ(float[] from, float[] to, Phase newPhase)
    {
        moveJStart    = (float[])from.Clone();
        moveJTarget   = (float[])to.Clone();
        moveJProgress = 0f;

        float maxDelta = 0f;
        for (int i = 0; i < 6; i++)
            maxDelta = Mathf.Max(maxDelta, Mathf.Abs(to[i] - from[i]));

        moveJDuration = (moveJSpeed > 0.1f) ? (maxDelta / moveJSpeed) : 1f;
        if (moveJDuration < minMoveJDuration) moveJDuration = minMoveJDuration;

        currentPhase = newPhase;
        Debug.Log($"[StrokeDrawer] MoveJ ({newPhase}). maxDelta={maxDelta:F1}°, duración={moveJDuration:F2}s");
    }

    void UpdateMoveJ()
    {
        moveJProgress += Time.deltaTime / moveJDuration;
        moveJProgress  = Mathf.Clamp01(moveJProgress);

        float t = SmoothStep(moveJProgress);
        for (int i = 0; i < 6; i++)
            SetJointAngle(i, Mathf.Lerp(moveJStart[i], moveJTarget[i], t));

        if (moveJProgress >= 1f) OnMoveJFinished();
    }

    void OnMoveJFinished()
    {
        if (currentPhase == Phase.MoveJ_Approach)
        {
            // Activar IK y empezar a dibujar
            ccdSolver.enabled = true;
            currentPhase      = Phase.MoveL_Draw;
            Debug.Log($"[StrokeDrawer] MoveJ_Approach completado. Iniciando MoveL_Draw {info}.");
        }
        else if (currentPhase == Phase.MoveJ_Home)
        {
            currentPhase = Phase.Finished;
            Debug.Log("[StrokeDrawer] ✅ Dibujo completado. Robot en HOME.");
            DialogueManager.Instance?.IniciarEtapa9();
        }
    }

    // ── Dibujo del trazo (MoveL) ──────────────────────────────────────────────

    void UpdateDraw()
    {
        var stroke = allStrokes[currentStrokeIndex];

        // Trazo completado — el IK se mantiene activo durante el pen-up
        // para que el robot pueda seguir al Tracer mientras sube.
        // Se desactiva en FinishPenUp(), igual que RobotProgram.
        if (currentPointIndex >= stroke.Count - 1)
        {
            StartPenUp();
            return;
        }

        // Esperar a que el IK converja antes de avanzar
        if (waitForEndEffector && anchorToEndEffector != null)
        {
            if (Vector3.Distance(anchorToEndEffector.position, tracer.position) > reachTolerance)
                return;
        }

        Vector3 segStart = stroke[currentPointIndex];
        Vector3 segEnd   = stroke[currentPointIndex + 1];
        float   len      = Vector3.Distance(segStart, segEnd);

        if (len < 1e-6f) { currentPointIndex++; drawProgress = 0f; return; }

        drawProgress += (drawSpeed * Time.deltaTime) / len;
        drawProgress  = Mathf.Clamp01(drawProgress);

        Vector3 pos = Vector3.Lerp(segStart, segEnd, drawProgress);
        tracer.position = pos;

        // Visualización
        if (currentDrawLine == null)
        {
            currentDrawLine = CrearLineRenderer($"Draw_{drawLines.Count}", trailColor);
            drawLines.Add(currentDrawLine);
            AgregarPunto(currentDrawLine, segStart);
        }
        AgregarPunto(currentDrawLine, pos);

        if (drawProgress >= 1f)
        {
            currentPointIndex++;
            drawProgress    = 0f;
            currentDrawLine = null;
        }
    }

    // ── PenUp entre trazos ────────────────────────────────────────────────────

    void StartPenUp()
    {
        penUpStartPos = tracer != null ? tracer.position : Vector3.zero;
        penUpEndPos   = penUpStartPos + LiftDirection() * liftHeight;
        penUpProgress = 0f;
        currentPhase  = Phase.PenUp_BetweenStrokes;
        Debug.Log($"[StrokeDrawer] Pen-up trazo {info}.");
    }

    void UpdatePenUp()
    {
        float len = Vector3.Distance(penUpStartPos, penUpEndPos);
        if (len < 1e-6f) { FinishPenUp(); return; }

        penUpProgress += (penUpSpeed * Time.deltaTime) / len;
        penUpProgress  = Mathf.Clamp01(penUpProgress);

        if (tracer != null)
            tracer.position = Vector3.Lerp(penUpStartPos, penUpEndPos, penUpProgress);

        if (penUpProgress >= 1f)
        {
            // Esperar al IK si es necesario
            if (waitForEndEffector && anchorToEndEffector != null &&
                Vector3.Distance(anchorToEndEffector.position, tracer.position) > reachTolerance)
                return;

            FinishPenUp();
        }
    }

    void FinishPenUp()
    {
        ccdSolver.enabled = false;
        StartStroke(currentStrokeIndex + 1);
    }

    // ── Helpers de conversión y geometría ────────────────────────────────────

    Vector3 UVtoWorld(float u, float v)
    {
        // Escalar preservando la proporción de la imagen original.
        // La figura ocupa el máximo espacio posible dentro del board
        // sin distorsionarse, y queda centrada.
        float boardRatio = boardWidthMeters / boardHeightMeters;
        float uu, vv;

        if (imageAspectRatio <= boardRatio)
        {
            // Imagen más alta que el board → limitada por altura
            float anchoEscalado = boardHeightMeters * imageAspectRatio;
            float uOffset = (boardWidthMeters - anchoEscalado) * 0.5f / boardWidthMeters;
            float uScale  = anchoEscalado / boardWidthMeters;
            uu = u * uScale + uOffset;
            vv = v;
        }
        else
        {
            // Imagen más ancha que el board → limitada por ancho
            float altoEscalado = boardWidthMeters / imageAspectRatio;
            float vOffset = (boardHeightMeters - altoEscalado) * 0.5f / boardHeightMeters;
            float vScale  = altoEscalado / boardHeightMeters;
            uu = u;
            vv = v * vScale + vOffset;
        }

        if (drawOrigin == null)
        {
            return drawPlane == DrawPlane.XZ_Horizontal
                ? new Vector3(uu * boardWidthMeters, 0f, vv * boardHeightMeters)
                : new Vector3(uu * boardWidthMeters, vv * boardHeightMeters, 0f);
        }

        return drawPlane == DrawPlane.XZ_Horizontal
            ? drawOrigin.position + drawOrigin.right * (uu * boardWidthMeters) + drawOrigin.forward * (vv * boardHeightMeters)
            : drawOrigin.position + drawOrigin.right * (uu * boardWidthMeters) + drawOrigin.up      * (vv * boardHeightMeters);
    }

    Vector3 LiftDirection()
    {
        if (drawPlane == DrawPlane.XZ_Horizontal)
            return drawOrigin != null ? drawOrigin.up      : Vector3.up;
        else
            return drawOrigin != null ? drawOrigin.forward : Vector3.forward;
    }

    List<Vector3> Resamplear(List<Vector3> puntos, float maxDist)
    {
        var resultado = new List<Vector3> { puntos[0] };
        for (int i = 1; i < puntos.Count; i++)
        {
            float dist = Vector3.Distance(puntos[i - 1], puntos[i]);
            if (dist > maxDist)
            {
                int subs = Mathf.CeilToInt(dist / maxDist);
                for (int j = 1; j <= subs; j++)
                    resultado.Add(Vector3.Lerp(puntos[i - 1], puntos[i], j / (float)subs));
            }
            else resultado.Add(puntos[i]);
        }
        return resultado;
    }

    // ── Joint helpers (igual que RobotProgram) ────────────────────────────────

    float[] GetCurrentJointAngles()
    {
        var a = new float[6];
        for (int i = 0; i < 6; i++)
            a[i] = ccdSolver.joints[i].currentAngle;
        return a;
    }

    void SetJointAngle(int idx, float angleDeg)
    {
        var j = ccdSolver.joints[idx];
        if (j.joint == null) return;
        float delta = angleDeg - j.currentAngle;
        j.currentAngle = angleDeg;
        j.joint.localRotation *= Quaternion.AngleAxis(delta, AxisToVector(j.rotationAxis));
    }

    Vector3 AxisToVector(CCDIKSolver.Axis a)
    {
        switch (a)
        {
            case CCDIKSolver.Axis.X: return Vector3.right;
            case CCDIKSolver.Axis.Y: return Vector3.up;
            case CCDIKSolver.Axis.Z: return Vector3.forward;
            default:                  return Vector3.forward;
        }
    }

    float SmoothStep(float t) => t * t * (3f - 2f * t);

    // ── LineRenderer helpers ──────────────────────────────────────────────────

    LineRenderer CrearLineRenderer(string nombre, Color color, float factor = 1f)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, worldPositionStays: false);
        var lr = go.AddComponent<LineRenderer>();
        lr.material      = new Material(Shader.Find("Sprites/Default"));
        lr.startColor    = color; lr.endColor = color;
        lr.startWidth    = trailWidth * factor; lr.endWidth = trailWidth * factor;
        lr.positionCount = 0; lr.useWorldSpace = true;
        return lr;
    }

    void AgregarPunto(LineRenderer lr, Vector3 p)
    {
        if (lr == null) return;
        int n = lr.positionCount;
        lr.positionCount = n + 1;
        lr.SetPosition(n, p);
    }

    // ── Figuras de prueba ─────────────────────────────────────────────────────

    string JsonFiguraPrueba(FiguraPrueba fig)
    {
        switch (fig)
        {
            case FiguraPrueba.Cuadrado:
                return J(new[]{ S(P(0f,0f), P(1f,0f), P(1f,1f), P(0f,1f), P(0f,0f)) });
            case FiguraPrueba.DosBandas:
                return J(new[]{ S(P(0f,0.33f), P(1f,0.33f)), S(P(0f,0.66f), P(1f,0.66f)) });
            case FiguraPrueba.Cruz:
                return J(new[]{ S(P(0f,0.5f), P(1f,0.5f)), S(P(0.5f,0f), P(0.5f,1f)) });
            case FiguraPrueba.EleInvertida:
                return J(new[]{ S(P(0.1f,0.9f), P(0.1f,0.1f)), S(P(0.1f,0.1f), P(0.9f,0.1f)) });
            case FiguraPrueba.Escalera:
                return J(new[]{ S(P(0f,0.1f), P(0.33f,0.1f)), S(P(0.33f,0.5f), P(0.66f,0.5f)), S(P(0.66f,0.9f), P(1f,0.9f)) });
            case FiguraPrueba.TrianguloCentrado:
                return J(new[]{ S(P(0.5f,0.7f), P(0.3f,0.3f), P(0.7f,0.3f), P(0.5f,0.7f)) });
            default:
                return JsonFiguraPrueba(FiguraPrueba.Cruz);
        }
    }

    static readonly System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;
    string P(float u, float v) => $"{{\"u\":{u.ToString("F3", IC)},\"v\":{v.ToString("F3", IC)}}}";
    string S(params string[] pts) => $"{{\"points\":[{string.Join(",", pts)}]}}";
    string J(string[] strokes)    => $"{{\"strokes\":[{string.Join(",", strokes)}]}}";

    // ── Gizmos ────────────────────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (drawOrigin == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(UVtoWorld(0,0), UVtoWorld(1,0));
        Gizmos.DrawLine(UVtoWorld(1,0), UVtoWorld(1,1));
        Gizmos.DrawLine(UVtoWorld(1,1), UVtoWorld(0,1));
        Gizmos.DrawLine(UVtoWorld(0,1), UVtoWorld(0,0));
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(UVtoWorld(0.5f, 0.5f), 0.005f);
    }
}