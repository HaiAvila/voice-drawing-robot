using UnityEngine;

/// <summary>
/// Coordinador del programa del robot UR3 para escribir PALABRAS.
///
/// Flujo:
///   Para cada letra de 'text':
///     1. Calcular origen de la letra (se desplaza hacia la derecha según la posición).
///     2. Reconfigurar el LetterDrawer con la nueva letra y origen.
///     3. Calcular pose de aproximación con IK automática.
///     4. MoveJ (aproximación) → MoveL (dibujar) → siguiente letra.
///   Al terminar la última letra: MoveJ hacia home.
///
/// Los espacios en el texto se ignoran como letras pero avanzan la posición
/// para separar palabras.
/// </summary>

[RequireComponent(typeof(CCDIKSolver))]
public class RobotProgram : MonoBehaviour
{
    public enum Phase { Idle, MoveJ_Approach, MoveL_Draw, PenUp_BetweenLetters, MoveJ_Home, Finished }

    [System.Serializable]
    public class JointAngles { public float[] angles = new float[6]; }

    [Header("Texto a escribir")]
    [Tooltip("Texto que el robot escribirá. Solo letras A-Z y espacios. Máximo 8 caracteres sugerido.")]
    public string text = "";

    [Tooltip("Origen de la PRIMERA letra (esquina inferior-izquierda). " +
             "Las siguientes letras se desplazan automáticamente.")]
    public Vector3 firstLetterOrigin = new Vector3(-2.6f, -0.28f, -0.3f);

    [Header("Espaciado")]
    [Tooltip("Espacio entre letras en metros.")]
    public float letterSpacing = 0.02f;

    [Tooltip("Espacio que ocupa un espacio en blanco (' ') en metros.")]
    public float spaceWidth = 0.05f;

    [Header("Referencias")]
    public LetterDrawer letterDrawer;

    [Header("Pen-up entre letras")]
    [Tooltip("Altura (m) a la que se eleva el efector al terminar cada letra, antes de " +
             "viajar a la siguiente. Evita que el marcador deje líneas al moverse entre letras.")]
    public float penUpBetweenLettersHeight = 0.04f;

    [Tooltip("Velocidad (m/s) del pen-up entre letras.")]
    public float penUpSpeed = 0.08f;

    [Header("Pose HOME")]
    [Tooltip("Pose a la que vuelve el robot al terminar la palabra.")]
    public JointAngles homePose = new JointAngles();

    [Header("Velocidad de los MoveJ")]
    public float moveJSpeed = 30f;
    public float minMoveJDuration = 2f;

    [Header("Cálculo de pose de aproximación")]
    [Range(10, 200)] public int approachSolverIterations = 80;

    [Header("Comportamiento al recibir nueva palabra")]
    [Tooltip("Si está activo, al llamar a SetTextAndRestart() se borran los trazos de la palabra anterior. " +
             "Útil para cuando el usuario escribe una palabra nueva desde el teclado VR.")]
    public bool clearDrawingsOnNewWord = true;

    [Header("Estado (solo lectura)")]
    [SerializeField] private Phase currentPhase = Phase.Idle;
    [SerializeField] private int currentLetterIndex = 0;
    [SerializeField] private string currentLetterInfo = "";

    private CCDIKSolver ccdSolver;
    private float[] moveJStart;
    private float[] moveJTarget;
    private float moveJProgress;
    private float moveJDuration;

    // Estado del pen-up entre letras
    private Vector3 penUpStartPos;
    private Vector3 penUpEndPos;
    private float penUpProgress;

    // Lista precomputada de letras a dibujar con su origen asignado
    private struct LetterEntry
    {
        public LetterDrawer.Letter letter;
        public Vector3 origin;
    }
    private System.Collections.Generic.List<LetterEntry> letterQueue;

    void Start()
    {
        ccdSolver = GetComponent<CCDIKSolver>();
        if (ccdSolver == null) { Debug.LogError("[RobotProgram] Sin CCDIKSolver."); enabled = false; return; }

        if (homePose.angles == null || homePose.angles.Length != 6)
            homePose.angles = new float[6];

        if (letterDrawer == null) { Debug.LogError("[RobotProgram] Sin LetterDrawer."); enabled = false; return; }

        ccdSolver.enabled = false;
        letterDrawer.enabled = false;

        // Pre-procesar el texto y calcular los orígenes de cada letra
        BuildLetterQueue();

        if (letterQueue.Count == 0)
        {
            Debug.LogWarning("[RobotProgram] No hay letras válidas en el texto. Volviendo a home.");
            StartMoveJ(GetCurrentJointAngles(), homePose.angles, Phase.MoveJ_Home);
            return;
        }

        Debug.Log($"[RobotProgram] Iniciado. Texto: '{text}'. Letras a dibujar: {letterQueue.Count}");

        // Empezar con la primera letra
        currentLetterIndex = 0;
        StartLetter(0);
    }

    /// <summary>
    /// Pre-procesa el string 'text' y calcula dónde va cada letra.
    /// Los espacios avanzan la posición pero no producen letras.
    /// </summary>
    void BuildLetterQueue()
    {
        letterQueue = new System.Collections.Generic.List<LetterEntry>();

        float currentX = 0f; // desplazamiento horizontal acumulado (local al texto)
        string upper = text.ToUpper();

        for (int i = 0; i < upper.Length; i++)
        {
            char c = upper[i];

            if (c == ' ')
            {
                currentX += spaceWidth;
                continue;
            }

            if (c < 'A' || c > 'Z')
            {
                Debug.LogWarning($"[RobotProgram] Carácter '{c}' ignorado (no es letra A-Z ni espacio).");
                continue;
            }

            // Calcular origen de esta letra
            Vector3 letterOrigin = firstLetterOrigin + GetHorizontalOffset(currentX);

            letterQueue.Add(new LetterEntry
            {
                letter = (LetterDrawer.Letter)(c - 'A'),
                origin = letterOrigin
            });

            // Avanzar X para la siguiente letra: ancho + espaciado
            currentX += letterDrawer.width + letterSpacing;
        }

        Debug.Log($"[RobotProgram] Cola de letras construida: {letterQueue.Count} letras.");
    }

    /// <summary>
    /// Convierte un desplazamiento "horizontal de la palabra" a un offset en el mundo,
    /// según el plano de dibujo.
    /// </summary>
    Vector3 GetHorizontalOffset(float distance)
    {
        switch (letterDrawer.drawPlane)
        {
            case LetterDrawer.DrawPlane.XY_Vertical:
                return new Vector3(distance, 0f, 0f);
            case LetterDrawer.DrawPlane.XZ_Horizontal:
            default:
                return new Vector3(distance, 0f, 0f);
        }
    }

    /// <summary>
    /// Inicia la ejecución de la letra en la posición 'index' de la cola.
    /// </summary>
    void StartLetter(int index)
    {
        if (index >= letterQueue.Count)
        {
            // Terminamos todas las letras, volver a home
            Debug.Log("[RobotProgram] Todas las letras completadas. MoveJ hacia HOME.");
            letterDrawer.enabled = false;
            ccdSolver.enabled = false;
            StartMoveJ(GetCurrentJointAngles(), homePose.angles, Phase.MoveJ_Home);
            return;
        }

        var entry = letterQueue[index];
        if (index == 0)
        {
            DialogueManager.Instance?.IniciarEtapa7(letterQueue.Count);
        }
        currentLetterInfo = $"[{index + 1}/{letterQueue.Count}] '{entry.letter}'";
        Debug.Log($"[RobotProgram] --- Iniciando letra {currentLetterInfo} en origen {entry.origin} ---");

        // Reconfigurar el LetterDrawer (pero sin habilitarlo aún)
        letterDrawer.Reinitialize(entry.letter, entry.origin, clearPreviousDrawings: false);

        // Calcular pose de aproximación usando IK
        float[] approachAngles = CalculateApproachPose();

        // MoveJ hacia esa pose
        StartMoveJ(GetCurrentJointAngles(), approachAngles, Phase.MoveJ_Approach);
    }

    /// <summary>
    /// Calcula los ángulos de aproximación usando el CCD en seco.
    /// </summary>
    float[] CalculateApproachPose()
    {
        // Guardar estado de los joints
        float[] initialAngles = GetCurrentJointAngles();
        Quaternion[] initialRotations = new Quaternion[ccdSolver.joints.Length];
        for (int i = 0; i < ccdSolver.joints.Length; i++)
            initialRotations[i] = ccdSolver.joints[i].joint.localRotation;

        Transform originalTarget = ccdSolver.target;
        if (letterDrawer.tracer != null)
            ccdSolver.target = letterDrawer.tracer;

        int saved = ccdSolver.maxIterations;
        ccdSolver.maxIterations = approachSolverIterations;
        ccdSolver.SolveOneFrame();
        ccdSolver.maxIterations = saved;

        float[] solved = GetCurrentJointAngles();

        for (int i = 0; i < ccdSolver.joints.Length; i++)
        {
            ccdSolver.joints[i].joint.localRotation = initialRotations[i];
            ccdSolver.joints[i].currentAngle = initialAngles[i];
        }
        ccdSolver.target = originalTarget;

        return solved;
    }

    void Update()
    {
        switch (currentPhase)
        {
            case Phase.MoveJ_Approach:
            case Phase.MoveJ_Home:
                UpdateMoveJ();
                break;

            case Phase.MoveL_Draw:
                if (letterDrawer != null && letterDrawer.IsFinished)
                {
                    Debug.Log($"[RobotProgram] Letra {currentLetterInfo} completada.");
                    letterDrawer.enabled = false;
                    StartPenUpBetweenLetters();
                }
                break;

            case Phase.PenUp_BetweenLetters:
                UpdatePenUp();
                break;
        }
    }

    void StartPenUpBetweenLetters()
    {
        if (letterDrawer == null || letterDrawer.tracer == null)
        {
            AdvanceToNextLetter();
            return;
        }

        penUpStartPos = letterDrawer.tracer.position;
        Vector3 upDir = GetPlaneNormal();
        penUpEndPos = penUpStartPos + upDir * penUpBetweenLettersHeight;
        penUpProgress = 0f;

        currentPhase = Phase.PenUp_BetweenLetters;
        Debug.Log($"[RobotProgram] Pen-up: elevando {penUpBetweenLettersHeight*100:F1} cm antes de la siguiente letra.");
    }

    Vector3 GetPlaneNormal()
    {
        if (letterDrawer.drawPlane == LetterDrawer.DrawPlane.XY_Vertical)
            return Vector3.forward;
        return Vector3.up;
    }

    void UpdatePenUp()
    {
        float len = Vector3.Distance(penUpStartPos, penUpEndPos);
        if (len < 1e-6f) { FinishPenUp(); return; }

        penUpProgress += (penUpSpeed * Time.deltaTime) / len;
        penUpProgress = Mathf.Clamp01(penUpProgress);

        letterDrawer.tracer.position = Vector3.Lerp(penUpStartPos, penUpEndPos, penUpProgress);

        if (penUpProgress >= 1f)
        {
            float robotError = Vector3.Distance(letterDrawer.anchorToEndEffector != null
                ? letterDrawer.anchorToEndEffector.position
                : letterDrawer.tracer.position,
                letterDrawer.tracer.position);

            if (!letterDrawer.waitForEndEffector || letterDrawer.anchorToEndEffector == null ||
                robotError <= letterDrawer.reachTolerance)
            {
                FinishPenUp();
            }
        }
    }

    void FinishPenUp()
    {
        ccdSolver.enabled = false;
        AdvanceToNextLetter();
    }

    void AdvanceToNextLetter()
    {
        currentLetterIndex++;
        StartLetter(currentLetterIndex);
    }

    void StartMoveJ(float[] from, float[] to, Phase newPhase)
    {
        moveJStart = (float[])from.Clone();
        moveJTarget = (float[])to.Clone();
        moveJProgress = 0f;

        float maxDelta = 0f;
        for (int i = 0; i < 6; i++)
            maxDelta = Mathf.Max(maxDelta, Mathf.Abs(to[i] - from[i]));

        moveJDuration = (moveJSpeed > 0.1f) ? (maxDelta / moveJSpeed) : 1f;
        if (moveJDuration < minMoveJDuration)
            moveJDuration = minMoveJDuration;

        currentPhase = newPhase;
        Debug.Log($"[RobotProgram] MoveJ iniciado ({newPhase}). maxDelta={maxDelta:F1}°, duración={moveJDuration:F2}s");
    }

    void UpdateMoveJ()
    {
        moveJProgress += Time.deltaTime / moveJDuration;
        moveJProgress = Mathf.Clamp01(moveJProgress);

        float t = SmoothStep(moveJProgress);
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Lerp(moveJStart[i], moveJTarget[i], t);
            SetJointAngle(i, angle);
        }

        if (moveJProgress >= 1f) OnMoveJFinished();
    }

    void OnMoveJFinished()
    {
        if (currentPhase == Phase.MoveJ_Approach)
        {
            ccdSolver.enabled = true;
            letterDrawer.enabled = true;
            currentPhase = Phase.MoveL_Draw;
        }
        else if (currentPhase == Phase.MoveJ_Home)
        {
            Debug.Log("[RobotProgram] ✅ Programa completado. Palabra escrita, robot en HOME.");
            currentPhase = Phase.Finished;
            DialogueManager.Instance?.IniciarEtapa9();
        }
    }

    float SmoothStep(float t) => t * t * (3f - 2f * t);

    void SetJointAngle(int jointIndex, float angleDeg)
    {
        var j = ccdSolver.joints[jointIndex];
        if (j.joint == null) return;

        float delta = angleDeg - j.currentAngle;
        j.currentAngle = angleDeg;

        Vector3 localAxis = AxisToVector(j.rotationAxis);
        j.joint.localRotation *= Quaternion.AngleAxis(delta, localAxis);
    }

    Vector3 AxisToVector(CCDIKSolver.Axis a)
    {
        switch (a)
        {
            case CCDIKSolver.Axis.X: return Vector3.right;
            case CCDIKSolver.Axis.Y: return Vector3.up;
            case CCDIKSolver.Axis.Z: return Vector3.forward;
            default: return Vector3.forward;
        }
    }

    public float[] GetCurrentJointAngles()
    {
        float[] a = new float[6];
        for (int i = 0; i < 6; i++)
            a[i] = ccdSolver.joints[i].currentAngle;
        return a;
    }

    [ContextMenu("Print Current Joint Angles")]
    void PrintCurrentJointAngles()
    {
        if (ccdSolver == null) ccdSolver = GetComponent<CCDIKSolver>();
        string s = "[RobotProgram] Ángulos actuales:\n";
        for (int i = 0; i < 6; i++)
            s += $"  {i}: {ccdSolver.joints[i].currentAngle:F2}°\n";
        Debug.Log(s);
    }

    /// <summary>
    /// Llamado desde el InputField (teclado VR del Meta Quest) para iniciar el dibujo
    /// de una nueva palabra.
    ///
    /// MEJORA: ahora limpia los trazos de la palabra anterior antes de empezar
    /// (configurable con clearDrawingsOnNewWord).
    /// </summary>
    public void SetTextAndRestart(string nuevoTexto)
    {
        if (string.IsNullOrWhiteSpace(nuevoTexto))
        {
            Debug.LogWarning("[RobotProgram] Texto vacío. No se ejecuta.");
            return;
        }

        text = nuevoTexto.ToUpper();
        Debug.Log("[RobotProgram] Nuevo texto recibido: " + text);

        ccdSolver = GetComponent<CCDIKSolver>();
        if (ccdSolver == null)
        {
            Debug.LogError("[RobotProgram] Sin CCDIKSolver.");
            return;
        }

        if (letterDrawer == null)
        {
            Debug.LogError("[RobotProgram] Sin LetterDrawer.");
            return;
        }

        // ====================================================================
        // NUEVO: Limpiar el "pizarrón" antes de empezar la nueva palabra
        // ====================================================================
        if (clearDrawingsOnNewWord)
        {
            letterDrawer.ClearAllDrawings();
            Debug.Log("[RobotProgram] Trazos de la palabra anterior eliminados.");
        }

        ccdSolver.enabled = false;
        letterDrawer.enabled = false;

        currentPhase = Phase.Idle;
        currentLetterIndex = 0;
        currentLetterInfo = "";

        BuildLetterQueue();

        if (letterQueue.Count == 0)
        {
            Debug.LogWarning("[RobotProgram] No hay letras válidas.");
            StartMoveJ(GetCurrentJointAngles(), homePose.angles, Phase.MoveJ_Home);
            return;
        }

        StartLetter(0);
    }

    void OnDrawGizmos()
    {
        if (letterDrawer == null || string.IsNullOrEmpty(text)) return;

        float currentX = 0f;
        string upper = text.ToUpper();

        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        for (int i = 0; i < upper.Length; i++)
        {
            char c = upper[i];
            if (c == ' ') { currentX += spaceWidth; continue; }
            if (c < 'A' || c > 'Z') continue;

            Vector3 letterOrigin = firstLetterOrigin + GetHorizontalOffset(currentX);
            Gizmos.DrawWireCube(letterOrigin + GetHorizontalOffset(letterDrawer.width * 0.5f) +
                                (letterDrawer.drawPlane == LetterDrawer.DrawPlane.XZ_Horizontal
                                    ? new Vector3(0f, 0f, letterDrawer.height * 0.5f)
                                    : new Vector3(0f, letterDrawer.height * 0.5f, 0f)),
                                letterDrawer.drawPlane == LetterDrawer.DrawPlane.XZ_Horizontal
                                    ? new Vector3(letterDrawer.width, 0.001f, letterDrawer.height)
                                    : new Vector3(letterDrawer.width, letterDrawer.height, 0.001f));

            currentX += letterDrawer.width + letterSpacing;
        }
    }
}
