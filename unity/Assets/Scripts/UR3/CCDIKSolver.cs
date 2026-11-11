using UnityEngine;

/// <summary>
/// Cyclic Coordinate Descent (CCD) IK Solver con restricción opcional de orientación.
///
/// En modo "con orientación", el solver primero mueve los joints de posición (1-3)
/// para que el EndEffector llegue al target, y luego ajusta los joints de muñeca
/// (4-6) para que el EndEffector apunte en una dirección deseada (ej: hacia abajo).
///
/// UBICACIÓN: este script va en un GameObject INDEPENDIENTE (ej: UR3_IK_Controller).
/// </summary>
public class CCDIKSolver : MonoBehaviour
{
    public enum Axis { X, Y, Z }

    [System.Serializable]
    public class IKJoint
    {
        public string name = "Joint";
        public Transform joint;
        public Axis rotationAxis = Axis.Z;
        public float minAngle = -360f;
        public float maxAngle = 360f;

        [HideInInspector] public Quaternion initialLocalRotation;
        [HideInInspector] public float currentAngle = 0f;
    }

    [Header("Cadena cinemática (BASE -> END EFFECTOR)")]
    public IKJoint[] joints = new IKJoint[6]
    {
        new IKJoint { name = "1 - Shoulder Pan",  rotationAxis = Axis.Y },
        new IKJoint { name = "2 - Shoulder Lift", rotationAxis = Axis.Z },
        new IKJoint { name = "3 - Elbow",         rotationAxis = Axis.Z },
        new IKJoint { name = "4 - Wrist 1",       rotationAxis = Axis.Z },
        new IKJoint { name = "5 - Wrist 2",       rotationAxis = Axis.Y },
        new IKJoint { name = "6 - Wrist 3",       rotationAxis = Axis.Z },
    };

    [Header("End effector y target")]
    public Transform endEffector;
    public Transform target;

    [Header("Restricción de orientación")]
    [Tooltip("Si está activo, los últimos 3 joints (muñeca) se ajustan para mantener " +
             "una orientación fija del EndEffector (ej: marcador apuntando hacia abajo).")]
    public bool constrainOrientation = true;

    [Tooltip("Dirección en el MUNDO hacia la cual el EndEffector debe apuntar. " +
             "Ej: (0,-1,0) para apuntar hacia abajo.")]
    public Vector3 desiredToolDirection = new Vector3(0f, -1f, 0f);

    [Tooltip("Eje LOCAL del EndEffector que se considera la 'dirección del marcador'. " +
             "Prueba distintos valores hasta que el marcador virtual apunte donde quieras.")]
    public Vector3 endEffectorLocalForward = new Vector3(0f, 0f, 1f);

    [Tooltip("Índice del PRIMER joint que pertenece a la muñeca (típicamente 3 = Wrist 1).")]
    [Range(3, 5)] public int wristStartIndex = 3;

    [Header("Parámetros del solver")]
    [Range(1, 50)] public int maxIterations = 20;
    public float positionTolerance = 0.001f;
    [Range(0.05f, 1f)] public float damping = 0.8f;

    [Tooltip("Damping específico para los joints de orientación (más bajo = más estable).")]
    [Range(0.05f, 1f)] public float orientationDamping = 0.5f;

    [Header("Debug")]
    public bool showDebugLines = true;
    public bool logConvergence = false;

    void Start()
    {
        foreach (var j in joints)
            if (j.joint != null)
                j.initialLocalRotation = j.joint.localRotation;

        if (endEffector == null) Debug.LogError("[CCDIKSolver] EndEffector no asignado!");
        if (target == null) Debug.LogError("[CCDIKSolver] Target no asignado!");
    }

    void LateUpdate()
    {
        if (target == null || endEffector == null) return;

        if (constrainOrientation)
            SolveIKWithOrientation();
        else
            SolveIKPositionOnly();
    }

    /// <summary>
    /// Ejecuta una pasada completa del solver. Útil para llamarlo bajo demanda
    /// (ej. desde RobotProgram para calcular la pose de aproximación sin animación).
    /// </summary>
    public void SolveOneFrame()
    {
        if (target == null || endEffector == null) return;

        if (constrainOrientation)
            SolveIKWithOrientation();
        else
            SolveIKPositionOnly();
    }

    /// <summary>
    /// IK solo posición: todos los joints participan. Lo que teníamos antes.
    /// </summary>
    void SolveIKPositionOnly()
    {
        for (int iter = 0; iter < maxIterations; iter++)
        {
            if (Vector3.Distance(endEffector.position, target.position) < positionTolerance) break;
            for (int i = joints.Length - 1; i >= 0; i--)
                RotateJointTowardsTargetPosition(joints[i], damping);
        }
    }

    /// <summary>
    /// IK en dos fases:
    /// - Fase 1: joints de posición (0..wristStartIndex-1) mueven el EE al target.
    /// - Fase 2: joints de muñeca (wristStartIndex..5) alinean la orientación del EE.
    /// </summary>
    void SolveIKWithOrientation()
    {
        for (int iter = 0; iter < maxIterations; iter++)
        {
            float posError = Vector3.Distance(endEffector.position, target.position);
            float orientError = OrientationError();

            if (posError < positionTolerance && orientError < 0.01f) break;

            // Fase 1: posición (joints del brazo)
            for (int i = wristStartIndex - 1; i >= 0; i--)
                RotateJointTowardsTargetPosition(joints[i], damping);

            // Fase 2: orientación (joints de muñeca)
            for (int i = joints.Length - 1; i >= wristStartIndex; i--)
                RotateJointForOrientation(joints[i], orientationDamping);
        }

        if (logConvergence)
        {
            Debug.Log($"[IK] Pos err: {Vector3.Distance(endEffector.position, target.position)*1000:F1}mm, " +
                      $"Orient err: {OrientationError():F3}");
        }
    }

    /// <summary>
    /// Error de orientación: 1 - coseno del ángulo entre dirección actual y deseada.
    /// 0 = perfectamente alineado, 2 = totalmente opuesto.
    /// </summary>
    float OrientationError()
    {
        Vector3 currentDir = endEffector.TransformDirection(endEffectorLocalForward.normalized);
        return 1f - Vector3.Dot(currentDir.normalized, desiredToolDirection.normalized);
    }

    /// <summary>
    /// Rota un joint para acercar el EndEffector al target (posición).
    /// </summary>
    void RotateJointTowardsTargetPosition(IKJoint j, float dampingFactor)
    {
        if (j.joint == null) return;

        Vector3 worldAxis = j.joint.TransformDirection(AxisToVector(j.rotationAxis));
        Vector3 toEE     = endEffector.position - j.joint.position;
        Vector3 toTarget = target.position       - j.joint.position;

        toEE     = Vector3.ProjectOnPlane(toEE,     worldAxis);
        toTarget = Vector3.ProjectOnPlane(toTarget, worldAxis);

        if (toEE.sqrMagnitude < 1e-8f || toTarget.sqrMagnitude < 1e-8f) return;

        float angleDeg = Vector3.SignedAngle(toEE, toTarget, worldAxis) * dampingFactor;
        ApplyJointRotation(j, angleDeg);
    }

    /// <summary>
    /// Rota un joint para alinear la "dirección del marcador" con la dirección deseada.
    /// </summary>
    void RotateJointForOrientation(IKJoint j, float dampingFactor)
    {
        if (j.joint == null) return;

        Vector3 worldAxis = j.joint.TransformDirection(AxisToVector(j.rotationAxis));
        Vector3 currentDir = endEffector.TransformDirection(endEffectorLocalForward.normalized);
        Vector3 desiredDir = desiredToolDirection.normalized;

        // Proyectar ambas direcciones al plano perpendicular al eje de rotación
        Vector3 currProj = Vector3.ProjectOnPlane(currentDir, worldAxis);
        Vector3 desProj  = Vector3.ProjectOnPlane(desiredDir, worldAxis);

        if (currProj.sqrMagnitude < 1e-8f || desProj.sqrMagnitude < 1e-8f) return;

        float angleDeg = Vector3.SignedAngle(currProj, desProj, worldAxis) * dampingFactor;
        ApplyJointRotation(j, angleDeg);
    }

    /// <summary>
    /// Aplica la rotación al joint, respetando los límites articulares.
    /// </summary>
    void ApplyJointRotation(IKJoint j, float angleDeg)
    {
        float newAngle = Mathf.Clamp(j.currentAngle + angleDeg, j.minAngle, j.maxAngle);
        float actualDelta = newAngle - j.currentAngle;
        j.currentAngle = newAngle;

        Vector3 localAxis = AxisToVector(j.rotationAxis);
        j.joint.localRotation *= Quaternion.AngleAxis(actualDelta, localAxis);
    }

    Vector3 AxisToVector(Axis a)
    {
        switch (a)
        {
            case Axis.X: return Vector3.right;
            case Axis.Y: return Vector3.up;
            case Axis.Z: return Vector3.forward;
            default: return Vector3.forward;
        }
    }

    public float[] GetJointAnglesRadians()
    {
        float[] angles = new float[joints.Length];
        for (int i = 0; i < joints.Length; i++)
            angles[i] = joints[i].currentAngle * Mathf.Deg2Rad;
        return angles;
    }

    public float[] GetJointAnglesDegrees()
    {
        float[] angles = new float[joints.Length];
        for (int i = 0; i < joints.Length; i++)
            angles[i] = joints[i].currentAngle;
        return angles;
    }

    void OnDrawGizmos()
    {
        if (!showDebugLines) return;

        // Línea EE -> target
        if (endEffector != null && target != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(endEffector.position, target.position);
            Gizmos.DrawWireSphere(endEffector.position, 0.01f);
        }

        // Cadena cinemática
        Gizmos.color = Color.green;
        for (int i = 0; i < joints.Length - 1; i++)
            if (joints[i].joint != null && joints[i + 1].joint != null)
                Gizmos.DrawLine(joints[i].joint.position, joints[i + 1].joint.position);
        if (joints.Length > 0 && joints[joints.Length - 1].joint != null && endEffector != null)
            Gizmos.DrawLine(joints[joints.Length - 1].joint.position, endEffector.position);

        // Dirección actual del marcador (azul) vs deseada (amarillo)
        if (endEffector != null)
        {
            Vector3 currentDir = endEffector.TransformDirection(endEffectorLocalForward.normalized);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(endEffector.position, currentDir * 0.1f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(endEffector.position, desiredToolDirection.normalized * 0.1f);
        }
    }
}