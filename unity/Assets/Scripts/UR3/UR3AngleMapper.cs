using UnityEngine;

/// <summary>
/// Convierte los ángulos del simulador de Unity al marco de referencia del UR3 real.
///
/// La simulación y el robot real usan convenciones DIFERENTES (cero de cada joint,
/// dirección de rotación). Esta clase centraliza la conversión para que cualquier
/// script que mande datos al robot real la use.
///
/// Tabla de mapeo (calibrada empíricamente):
///   Joint 0 (Base):     real = -sim
///   Joint 1 (Shoulder): real = sim - 90°
///   Joint 2 (Elbow):    real = sim
///   Joint 3 (Wrist 1):  real = sim - 90°
///   Joint 4 (Wrist 2):  real = -sim
///   Joint 5 (Wrist 3):  real = 0 (siempre fijo)
///
/// Si cambian estas convenciones, SOLO hay que modificar este archivo.
/// </summary>
public static class UR3AngleMapper
{
    /// <summary>
    /// Convierte un array de 6 ángulos del simulador (en grados) al formato esperado
    /// por el UR3 real (también en grados). No convierte a radianes; eso lo hace
    /// el código que envía al robot.
    /// </summary>
    public static float[] SimToReal(float[] simAnglesDeg)
    {
        if (simAnglesDeg == null || simAnglesDeg.Length != 6)
        {
            Debug.LogError("[UR3AngleMapper] Se esperaban 6 ángulos de simulación.");
            return new float[6];
        }

        float[] real = new float[6];
        real[0] = -simAnglesDeg[0];           // Base: cambio de signo
        real[1] =  simAnglesDeg[1] - 90f;     // Shoulder: resta 90°
        real[2] =  simAnglesDeg[2];           // Elbow: igual
        real[3] =  simAnglesDeg[3] - 90f;     // Wrist 1: resta 90°
        real[4] = -simAnglesDeg[4];           // Wrist 2: cambio de signo
        real[5] =  0f;                        // Wrist 3: siempre 0

        return real;
    }

    /// <summary>
    /// Conversión directa de grados del simulador a radianes del UR3 real.
    /// Útil cuando el destino final espera radianes (URScript, Flask, etc.).
    /// </summary>
    public static float[] SimToRealRadians(float[] simAnglesDeg)
    {
        float[] realDeg = SimToReal(simAnglesDeg);
        float[] realRad = new float[6];
        for (int i = 0; i < 6; i++)
            realRad[i] = realDeg[i] * Mathf.Deg2Rad;
        return realRad;
    }

    /// <summary>
    /// Útil para debugging: imprime la conversión frente a frente.
    /// </summary>
    public static string FormatConversion(float[] simAnglesDeg)
    {
        float[] realDeg = SimToReal(simAnglesDeg);
        var sb = new System.Text.StringBuilder();
        string[] names = { "Base", "Shoulder", "Elbow", "Wrist1", "Wrist2", "Wrist3" };
        sb.AppendLine("  Joint       Sim (°)   →   Real (°)");
        for (int i = 0; i < 6; i++)
            sb.AppendLine($"  {names[i],-10}  {simAnglesDeg[i],8:F2}  →  {realDeg[i],8:F2}");
        return sb.ToString();
    }
}