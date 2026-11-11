using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

/// <summary>
/// Graba los ángulos del UR3 simulado y los manda por HTTP POST al servidor Flask
/// del robot real cuando StrokeDrawer termina de dibujar.
/// La IP del robot se configura en NetworkConfig.cs — campo IPRobot.
/// </summary>
[RequireComponent(typeof(CCDIKSolver))]
public class UR3DirectSender : MonoBehaviour
{
    [Header("Parámetros URScript")]
    public float jointAcceleration = 1.0f;
    public float jointVelocity     = 0.3f;
    [Tooltip("Dejar en 0 para evitar que el UR3 real crashee en curvas pequeñas")]
    public float blendRadius = 0.0f;
    public string topicName  = "letra_ur3";

    [Header("Grabación")]
    public float sampleInterval        = 0.2f;
    public bool  sendOnSimulationFinish = true;

    // Privados
    private CCDIKSolver      ccdSolver;
    private StrokeDrawer     strokeDrawer;
    private List<float[]>    recordedAngles  = new List<float[]>();
    private bool             isRecording     = false;
    private float            timer           = 0f;
    private bool             simFinishedHandled = false;

    void Start()
    {
        ccdSolver    = GetComponent<CCDIKSolver>();
        strokeDrawer = GetComponent<StrokeDrawer>();
        StartRecording();
    }

    void Update()
    {
        // Grabar ángulos cada sampleInterval segundos
        if (isRecording)
        {
            timer += Time.deltaTime;
            if (timer >= sampleInterval)
            {
                timer = 0f;
                RecordCurrentAngles();
            }
        }

        // Detectar cuando StrokeDrawer termina para enviar al robot real
        if (sendOnSimulationFinish && !simFinishedHandled && strokeDrawer != null)
        {
            if (strokeDrawer.IsFinished)
            {
                simFinishedHandled = true;
                isRecording        = false;
                Debug.Log($"[UR3Direct] Dibujo terminado. Enviando {recordedAngles.Count} waypoints al robot real...");
                StartCoroutine(SendToFlaskRoutine());
            }
        }
    }

    public void StartRecording()
    {
        recordedAngles.Clear();
        isRecording        = true;
        simFinishedHandled = false;
        timer              = 0f;
    }

    void RecordCurrentAngles()
    {
        if (ccdSolver == null || ccdSolver.joints == null || ccdSolver.joints.Length != 6) return;

        float[] angles = new float[6];
        for (int i = 0; i < 6; i++)
            angles[i] = ccdSolver.joints[i].currentAngle;

        recordedAngles.Add(angles);
    }

    public void ResetCompleto()
    {
        isRecording        = false;
        simFinishedHandled = false;
        recordedAngles.Clear();
        timer = 0f;
    }

    [ContextMenu("Forzar Envío a Flask")]
    public void ForceSend()
    {
        isRecording = false;
        StartCoroutine(SendToFlaskRoutine());
    }

    public void PrepararNuevoDibujo()
    {
        recordedAngles.Clear();
        isRecording        = true;
        simFinishedHandled = false;
        timer              = 0f;
    }

    IEnumerator SendToFlaskRoutine()
    {
        if (recordedAngles.Count == 0)
        {
            Debug.LogWarning("[UR3Direct] No hay waypoints grabados.");
            yield break;
        }

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append("{");
        sb.Append($"\"topic\":\"{topicName}\",");
        sb.Append($"\"acceleration\":{jointAcceleration.ToString("F3", ci)},");
        sb.Append($"\"velocity\":{jointVelocity.ToString("F3", ci)},");
        sb.Append($"\"blend\":{blendRadius.ToString("F4", ci)},");
        sb.Append("\"waypoints\":[");

        for (int i = 0; i < recordedAngles.Count; i++)
        {
            float[] rads = UR3AngleMapper.SimToRealRadians(recordedAngles[i]);
            sb.Append($"[{rads[0].ToString("F4",ci)},{rads[1].ToString("F4",ci)}," +
                      $"{rads[2].ToString("F4",ci)},{rads[3].ToString("F4",ci)}," +
                      $"{rads[4].ToString("F4",ci)},{rads[5].ToString("F4",ci)}]");
            if (i < recordedAngles.Count - 1) sb.Append(",");
        }
        sb.Append("]}");

        string finalJson = sb.ToString();

        // URL del robot real — cambiar NetworkConfig.IPRobot cuando tengas la IP
        string url = NetworkConfig.UrlRobot;

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(finalJson);
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError ||
                req.result == UnityWebRequest.Result.ProtocolError)
                Debug.LogError($"[UR3Direct] Error: {req.error}");
            else
                Debug.Log($"[UR3Direct] Éxito: {req.downloadHandler.text}");
        }
    }
}