using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dibuja cualquier letra del alfabeto A-Z.
/// Puede operar en dos modos:
///   - Autónomo: usa los valores del Inspector (letterToDraw, origin).
///   - Controlado: otro script (RobotProgram) le llama a Reinitialize(letter, origin)
///     para dibujar letras en secuencia formando palabras.
/// </summary>
public class LetterDrawer : MonoBehaviour
{
    public enum DrawPlane { XY_Vertical, XZ_Horizontal }

    public enum Letter
    {
        A, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z
    }

    public enum StartCornerType
    {
        TopLeft, BottomLeft, TopRight, BottomRight
    }

    [Header("Letra a dibujar (modo autónomo)")]
    [Tooltip("Usado solo si nadie llama a Reinitialize() desde código.")]
    public Letter letterToDraw = Letter.A;

    [Header("Referencias")]
    public Transform tracer;
    public Transform anchorToEndEffector;
    public Vector3 anchorOffset = new Vector3(-0.0375f, 0f, -0.05f);

    [Header("Parámetros de la letra")]
    public Vector3 origin = new Vector3(0.3f, 0.3f, 0.2f);
    public DrawPlane drawPlane = DrawPlane.XZ_Horizontal;
    public float width = 0.075f;
    public float height = 0.1f;

    [Range(8, 60)] public int curveSegments = 24;

    [Header("Pen-up / Pen-down")]
    public float liftHeight = 0.03f;

    [Header("Sincronización con robot (IK)")]
    public bool waitForEndEffector = true;
    public float reachTolerance = 0.01f;

    [Header("Movimiento")]
    public float drawSpeed = 0.02f;
    public float travelSpeed = 0.05f;
    public bool loop = false;

    [Header("Visualización")]
    public Color trailColor = Color.cyan;
    public float trailWidth = 0.003f;
    public bool showTravelMoves = true;
    public Color travelColor = new Color(1f, 0.5f, 0f, 0.4f);

    // --- Internos ---
    private enum MoveType { Draw, PenUp, Travel, PenDown }
    private struct Waypoint { public Vector3 start, end; public MoveType type; }

    private List<List<Vector3>> strokes;
    private List<Waypoint> waypoints;
    private int currentWp = 0;
    private float wpProgress = 0f;
    private bool finished = false;

    public bool IsFinished => finished;
    public StartCornerType StartCorner => GetStartCornerFor(letterToDraw);

    private List<LineRenderer> drawLines = new List<LineRenderer>();
    private List<LineRenderer> travelLines = new List<LineRenderer>();
    private LineRenderer currentDrawLine;
    private LineRenderer currentTravelLine;

    void Start()    { InitializeTrajectory(clearLines: true); }

    /// <summary>
    /// OnEnable se llama cada vez que el GameObject/componente se reactiva.
    /// Cuando RobotProgram nos reactiva para dibujar la siguiente letra, NO queremos
    /// borrar los trazos de las letras anteriores (para ver la palabra completa).
    /// Por eso aquí llamamos a InitializeTrajectory con clearLines=false.
    /// </summary>
    void OnEnable()
    {
        if (waypoints != null)
            InitializeTrajectory(clearLines: false);
    }

    /// <summary>
    /// Método PÚBLICO para borrar TODOS los trazos dibujados (limpia el "pizarrón").
    /// Útil para llamarlo desde RobotProgram cuando se va a empezar una palabra nueva.
    /// </summary>
    public void ClearAllDrawings()
    {
        ClearLines();
        Debug.Log("[LetterDrawer] Pizarrón limpiado: todos los trazos borrados.");
    }

    /// <summary>
    /// Método PÚBLICO para reinicializar la letra desde código.
    /// RobotProgram lo llama para dibujar letra por letra de una palabra.
    /// No borra los trazos previamente dibujados (así se ve la palabra acumulada).
    /// </summary>
    public void Reinitialize(Letter newLetter, Vector3 newOrigin, bool clearPreviousDrawings = false)
    {
        letterToDraw = newLetter;
        origin = newOrigin;

        BuildLetter();
        BuildWaypointsFromStrokes();

        if (clearPreviousDrawings)
            ClearLines();

        finished = false;
        currentWp = 0;
        wpProgress = 0f;
        // Cerrar las "líneas activas" para que el próximo Draw abra una nueva
        currentDrawLine = null;
        currentTravelLine = null;

        if (tracer != null && waypoints.Count > 0)
            tracer.position = waypoints[0].start;
    }

    void InitializeTrajectory(bool clearLines = true)
    {
        if (anchorToEndEffector != null)
        {
            origin = anchorToEndEffector.position + anchorOffset;
            Debug.Log($"[LetterDrawer] Anclado al EndEffector. Origen = {origin}");
        }

        BuildLetter();
        BuildWaypointsFromStrokes();

        if (clearLines)
            ClearLines();
        else
        {
            // Solo cerrar las líneas activas para que la próxima letra cree sus propias líneas
            currentDrawLine = null;
            currentTravelLine = null;
        }

        finished = false;
        currentWp = 0;
        wpProgress = 0f;

        if (tracer != null && waypoints.Count > 0)
            tracer.position = waypoints[0].start;
    }

    // =========================================================================
    // CLASIFICACIÓN POR ESQUINA DE INICIO
    // =========================================================================

    public static StartCornerType GetStartCornerFor(Letter letter)
    {
        switch (letter)
        {
            case Letter.I: case Letter.L: case Letter.T:
            case Letter.V: case Letter.W: case Letter.Y: case Letter.Z:
            case Letter.O: case Letter.Q: case Letter.U:
                return StartCornerType.TopLeft;

            case Letter.A: case Letter.B: case Letter.H: case Letter.K:
            case Letter.M: case Letter.N: case Letter.X:
            case Letter.D: case Letter.P: case Letter.R:
                return StartCornerType.BottomLeft;

            case Letter.E: case Letter.F:
            case Letter.C: case Letter.G: case Letter.J: case Letter.S:
                return StartCornerType.TopRight;

            default:
                return StartCornerType.BottomLeft;
        }
    }

    // =========================================================================
    // DEFINICIÓN DE LETRAS
    // =========================================================================

    void BuildLetter()
    {
        strokes = new List<List<Vector3>>();

        switch (letterToDraw)
        {
            case Letter.A: BuildA(); break;
            case Letter.B: BuildB(); break;
            case Letter.C: BuildC(); break;
            case Letter.D: BuildD(); break;
            case Letter.E: BuildE(); break;
            case Letter.F: BuildF(); break;
            case Letter.G: BuildG(); break;
            case Letter.H: BuildH(); break;
            case Letter.I: BuildI(); break;
            case Letter.J: BuildJ(); break;
            case Letter.K: BuildK(); break;
            case Letter.L: BuildL(); break;
            case Letter.M: BuildM(); break;
            case Letter.N: BuildN(); break;
            case Letter.O: BuildO(); break;
            case Letter.P: BuildP(); break;
            case Letter.Q: BuildQ(); break;
            case Letter.R: BuildR(); break;
            case Letter.S: BuildS(); break;
            case Letter.T: BuildT(); break;
            case Letter.U: BuildU(); break;
            case Letter.V: BuildV(); break;
            case Letter.W: BuildW(); break;
            case Letter.X: BuildX(); break;
            case Letter.Y: BuildY(); break;
            case Letter.Z: BuildZ(); break;
            default: BuildA(); break;
        }
    }

    Vector3 P(float u, float v) => LocalToWorld(u * width, v * height);

    void BuildA() {
        strokes.Add(new List<Vector3> { P(0, 0), P(0.5f, 1), P(1, 0) });
        strokes.Add(new List<Vector3> { P(0.25f, 0.5f), P(0.75f, 0.5f) });
    }
    void BuildE() {
        strokes.Add(new List<Vector3> { P(1, 1), P(0, 1), P(0, 0), P(1, 0) });
        strokes.Add(new List<Vector3> { P(0, 0.5f), P(0.7f, 0.5f) });
    }
    void BuildF() {
        strokes.Add(new List<Vector3> { P(1, 1), P(0, 1), P(0, 0) });
        strokes.Add(new List<Vector3> { P(0, 0.5f), P(0.7f, 0.5f) });
    }
    void BuildH() {
        strokes.Add(new List<Vector3> { P(0, 0), P(0, 1) });
        strokes.Add(new List<Vector3> { P(1, 0), P(1, 1) });
        strokes.Add(new List<Vector3> { P(0, 0.5f), P(1, 0.5f) });
    }
    void BuildI() {
        strokes.Add(new List<Vector3> { P(0.2f, 1), P(0.8f, 1) });
        strokes.Add(new List<Vector3> { P(0.5f, 1), P(0.5f, 0) });
        strokes.Add(new List<Vector3> { P(0.2f, 0), P(0.8f, 0) });
    }
    void BuildK() {
        strokes.Add(new List<Vector3> { P(0, 0), P(0, 1) });
        strokes.Add(new List<Vector3> { P(1, 1), P(0, 0.5f), P(1, 0) });
    }
    void BuildL() { strokes.Add(new List<Vector3> { P(0, 1), P(0, 0), P(1, 0) }); }
    void BuildM() { strokes.Add(new List<Vector3> { P(0, 0), P(0, 1), P(0.5f, 0.4f), P(1, 1), P(1, 0) }); }
    void BuildN() { strokes.Add(new List<Vector3> { P(0, 0), P(0, 1), P(1, 0), P(1, 1) }); }
    void BuildT() {
        strokes.Add(new List<Vector3> { P(0, 1), P(1, 1) });
        strokes.Add(new List<Vector3> { P(0.5f, 1), P(0.5f, 0) });
    }
    void BuildV() { strokes.Add(new List<Vector3> { P(0, 1), P(0.5f, 0), P(1, 1) }); }
    void BuildW() { strokes.Add(new List<Vector3> { P(0, 1), P(0.25f, 0), P(0.5f, 0.6f), P(0.75f, 0), P(1, 1) }); }
    void BuildX() {
        strokes.Add(new List<Vector3> { P(0, 0), P(1, 1) });
        strokes.Add(new List<Vector3> { P(0, 1), P(1, 0) });
    }
    void BuildY() {
        strokes.Add(new List<Vector3> { P(0, 1), P(0.5f, 0.5f), P(1, 1) });
        strokes.Add(new List<Vector3> { P(0.5f, 0.5f), P(0.5f, 0) });
    }
    void BuildZ() { strokes.Add(new List<Vector3> { P(0, 1), P(1, 1), P(0, 0), P(1, 0) }); }

    void BuildB() {
        float r = height * 0.25f;
        var s = new List<Vector3> { P(0, 0), P(0, 1) };
        AppendSemicircle(s, P(0, 0.75f), r);
        AppendSemicircle(s, P(0, 0.25f), r);
        strokes.Add(s);
    }
    void BuildC() {
        Vector3 c = P(0.5f, 0.5f);
        float r = Mathf.Min(width, height) * 0.5f;
        var s = new List<Vector3>();
        AppendArc(s, c, r, 45f, 360f - 45f, true);
        strokes.Add(s);
    }
    void BuildD() {
        float r = height * 0.5f;
        var s = new List<Vector3> { P(0, 0), P(0, 1) };
        AppendSemicircle(s, P(0, 0.5f), r);
        strokes.Add(s);
    }
    void BuildG() {
        Vector3 c = P(0.5f, 0.5f);
        float r = Mathf.Min(width, height) * 0.5f;
        var s = new List<Vector3>();
        AppendArc(s, c, r, 45f, 360f - 45f, true);
        s.Add(P(0.5f, 0.35f));
        s.Add(P(1f, 0.35f));
        strokes.Add(s);
    }
    void BuildJ() {
        var s = new List<Vector3> { P(1, 1), P(1, 0.25f) };
        float r = width * 0.4f;
        AppendArc(s, P(0.6f, 0.25f), r, 0f, -180f, false);
        strokes.Add(s);
    }
    void BuildO() {
        var s = new List<Vector3>();
        AppendEllipse(s, P(0.5f, 0.5f), width * 0.5f, height * 0.5f, 90f, 90f + 360f, true);
        strokes.Add(s);
    }
    void BuildP() {
        float r = height * 0.25f;
        var s = new List<Vector3> { P(0, 0), P(0, 1) };
        AppendSemicircle(s, P(0, 0.75f), r);
        strokes.Add(s);
    }
    void BuildQ() {
        var s1 = new List<Vector3>();
        AppendEllipse(s1, P(0.5f, 0.5f), width * 0.5f, height * 0.5f, 90f, 90f + 360f, true);
        strokes.Add(s1);
        strokes.Add(new List<Vector3> { P(0.65f, 0.2f), P(1f, 0f) });
    }
    void BuildR() {
        float r = height * 0.25f;
        var s = new List<Vector3> { P(0, 0), P(0, 1) };
        AppendSemicircle(s, P(0, 0.75f), r);
        s.Add(P(1f, 0f));
        strokes.Add(s);
    }
    void BuildS() {
        var s = new List<Vector3>();
        Vector3 topC = P(0.5f, 0.75f);
        AppendEllipse(s, topC, width * 0.5f, height * 0.25f, 0f, 180f, true);
        AppendEllipse(s, topC, width * 0.5f, height * 0.25f, 180f, 270f, false);
        Vector3 botC = P(0.5f, 0.25f);
        AppendEllipse(s, botC, width * 0.5f, height * 0.25f, 90f, -180f, false);
        strokes.Add(s);
    }
    void BuildU() {
        var s = new List<Vector3> { P(0, 1), P(0, 0.3f) };
        AppendEllipse(s, P(0.5f, 0.3f), width * 0.5f, 0.3f * height, 180f, 360f, false);
        s.Add(P(1, 1));
        strokes.Add(s);
    }

    // =========================================================================
    // HELPERS DE CURVAS Y GEOMETRÍA
    // =========================================================================

    void AppendSemicircle(List<Vector3> stroke, Vector3 center, float radius)
    {
        AppendEllipse(stroke, center, radius, radius, 90f, -90f, false);
    }

    void AppendArc(List<Vector3> stroke, Vector3 center, float radius, float fromAngle, float toAngle, bool firstPoint)
    {
        AppendEllipse(stroke, center, radius, radius, fromAngle, toAngle, firstPoint);
    }

    void AppendEllipse(List<Vector3> stroke, Vector3 center, float radiusH, float radiusV,
                       float fromAngle, float toAngle, bool firstPoint)
    {
        int segments = Mathf.Max(4, Mathf.RoundToInt(curveSegments * Mathf.Abs(toAngle - fromAngle) / 360f));
        int startIdx = firstPoint ? 0 : 1;

        for (int i = startIdx; i <= segments; i++)
        {
            float t = i / (float)segments;
            float theta = Mathf.Lerp(fromAngle, toAngle, t) * Mathf.Deg2Rad;

            Vector3 offset = drawPlane == DrawPlane.XY_Vertical
                ? new Vector3(radiusH * Mathf.Cos(theta), radiusV * Mathf.Sin(theta), 0f)
                : new Vector3(radiusH * Mathf.Cos(theta), 0f, radiusV * Mathf.Sin(theta));

            stroke.Add(center + offset);
        }
    }

    Vector3 PlaneNormal() => drawPlane == DrawPlane.XY_Vertical ? Vector3.forward : Vector3.up;

    Vector3 LocalToWorld(float u, float v) =>
        drawPlane == DrawPlane.XY_Vertical
            ? origin + new Vector3(u, v, 0f)
            : origin + new Vector3(u, 0f, v);

    // =========================================================================
    // WAYPOINTS Y EJECUCIÓN
    // =========================================================================

    void BuildWaypointsFromStrokes()
    {
        waypoints = new List<Waypoint>();
        Vector3 up = PlaneNormal() * liftHeight;

        for (int s = 0; s < strokes.Count; s++)
        {
            var stroke = strokes[s];
            if (stroke.Count < 2) continue;

            if (s > 0)
            {
                var prev = strokes[s - 1];
                Vector3 prevEnd = prev[prev.Count - 1];
                Vector3 curStart = stroke[0];
                Vector3 liftedPrev = prevEnd + up;
                Vector3 liftedCur = curStart + up;

                waypoints.Add(new Waypoint { start = prevEnd,    end = liftedPrev, type = MoveType.PenUp });
                waypoints.Add(new Waypoint { start = liftedPrev, end = liftedCur,  type = MoveType.Travel });
                waypoints.Add(new Waypoint { start = liftedCur,  end = curStart,   type = MoveType.PenDown });
            }

            for (int i = 0; i < stroke.Count - 1; i++)
                waypoints.Add(new Waypoint { start = stroke[i], end = stroke[i + 1], type = MoveType.Draw });
        }
    }

    void ClearLines()
    {
        foreach (var lr in drawLines)   if (lr != null) Destroy(lr.gameObject);
        foreach (var lr in travelLines) if (lr != null) Destroy(lr.gameObject);
        drawLines.Clear();
        travelLines.Clear();
        currentDrawLine = null;
        currentTravelLine = null;
    }

    LineRenderer CreateLineRenderer(string name, Color color, float widthMul = 1f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, worldPositionStays: false);
        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = color; lr.endColor = color;
        lr.startWidth = trailWidth * widthMul; lr.endWidth = trailWidth * widthMul;
        lr.positionCount = 0; lr.useWorldSpace = true;
        return lr;
    }

    void Update()
    {
        if (tracer == null || waypoints == null || waypoints.Count == 0) return;
        if (finished && !loop) return;
        if (currentWp >= waypoints.Count) return;

        Waypoint wp = waypoints[currentWp];
        float len = Vector3.Distance(wp.start, wp.end);
        if (len < 1e-6f) { currentWp++; wpProgress = 0f; return; }

        if (waitForEndEffector && anchorToEndEffector != null)
        {
            if (Vector3.Distance(anchorToEndEffector.position, tracer.position) > reachTolerance)
                return;
        }

        float speed = (wp.type == MoveType.Draw) ? drawSpeed : travelSpeed;
        wpProgress += (speed * Time.deltaTime) / len;
        wpProgress = Mathf.Clamp01(wpProgress);

        Vector3 pos = Vector3.Lerp(wp.start, wp.end, wpProgress);
        tracer.position = pos;

        if (wp.type == MoveType.Draw)
        {
            if (wpProgress > 0f && currentDrawLine == null)
            {
                currentDrawLine = CreateLineRenderer($"Stroke_{drawLines.Count}", trailColor);
                drawLines.Add(currentDrawLine);
                AddPointToLine(currentDrawLine, wp.start);
            }
            AddPointToLine(currentDrawLine, pos);
        }
        else if (showTravelMoves)
        {
            if (wpProgress > 0f && currentTravelLine == null)
            {
                currentTravelLine = CreateLineRenderer($"Travel_{travelLines.Count}", travelColor, 0.5f);
                travelLines.Add(currentTravelLine);
                AddPointToLine(currentTravelLine, wp.start);
            }
            AddPointToLine(currentTravelLine, pos);
        }

        if (wpProgress >= 1f)
        {
            bool nextDiff = (currentWp + 1 < waypoints.Count) && (waypoints[currentWp + 1].type != wp.type);
            if (nextDiff)
            {
                if (wp.type == MoveType.Draw) currentDrawLine = null;
                else currentTravelLine = null;
            }

            currentWp++;
            wpProgress = 0f;

            if (currentWp >= waypoints.Count)
            {
                if (loop) { currentWp = 0; ClearLines(); finished = false; }
                else
                {
                    finished = true;
                    Debug.Log($"[LetterDrawer] Letra '{letterToDraw}' completada.");
                }
            }
        }
    }

    void AddPointToLine(LineRenderer lr, Vector3 p)
    {
        if (lr == null) return;
        int n = lr.positionCount;
        lr.positionCount = n + 1;
        lr.SetPosition(n, p);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying)
        {
            if (anchorToEndEffector != null)
                origin = anchorToEndEffector.position + anchorOffset;
            BuildLetter();
        }

        if (strokes == null) return;

        Gizmos.color = Color.yellow;
        foreach (var stroke in strokes)
            for (int i = 0; i < stroke.Count - 1; i++)
                Gizmos.DrawLine(stroke[i], stroke[i + 1]);

        Gizmos.color = Color.red;
        foreach (var stroke in strokes)
        {
            if (stroke.Count > 0)
            {
                Gizmos.DrawSphere(stroke[0], 0.004f);
                Gizmos.DrawSphere(stroke[stroke.Count - 1], 0.004f);
            }
        }
    }
}
