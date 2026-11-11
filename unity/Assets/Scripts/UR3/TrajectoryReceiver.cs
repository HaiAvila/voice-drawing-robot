using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class TrajectoryReceiver : MonoBehaviour
{
    [Tooltip("Segundos entre cada consulta al servidor.")]
    public float pollInterval = 2f;

    [Header("Referencias")]
    public StrokeDrawer strokeDrawer;

    [Header("Estado (solo lectura)")]
    [SerializeField] private int    lastSeenId = -1;
    [SerializeField] private bool   isPolling  = false;
    [SerializeField] private string statusMsg  = "Inactivo";

    // ── Ciclo de vida ─────────────────────────────────────────────────────────

    void Start()
    {
        if (strokeDrawer == null)
            strokeDrawer = GetComponent<StrokeDrawer>();
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void StartPolling()
    {
        if (isPolling) return;
        isPolling = true;
        // ← ya NO reseteamos lastSeenId aquí
        statusMsg = "Polling iniciado";
        StartCoroutine(PollLoop());
        Debug.Log("[TrajectoryReceiver] Polling iniciado.");
    }

    // Llama esto solo cuando quieras empezar desde cero (primera vez, o reinicio total)
    public void ResetAndStartPolling()
    {
        lastSeenId = -1;
        StopPolling();
        isPolling = true;
        statusMsg = "Polling iniciado (reset)";
        StartCoroutine(PollLoop());
        Debug.Log("[TrajectoryReceiver] Polling reiniciado desde cero.");
    }

    public void StopPolling()
    {
        isPolling = false;
        StopAllCoroutines();
        statusMsg = "Inactivo";
        Debug.Log("[TrajectoryReceiver] Polling detenido.");
    }

    // ── Loop de polling ───────────────────────────────────────────────────────

    IEnumerator PollLoop()
    {
        while (isPolling)
        {
            yield return StartCoroutine(FetchTrayectoria());
            yield return new WaitForSeconds(pollInterval);
        }
    }

    IEnumerator FetchTrayectoria()
    {
        string url = NetworkConfig.Url + "/trayectoria";

        using var req = UnityWebRequest.Get(url);
        req.timeout = 5;
        yield return req.SendWebRequest();

        if (req.responseCode == 204)
        {
            statusMsg = "Esperando trayectoria...";
            yield break;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            statusMsg = $"Error: {req.error}";
            Debug.LogWarning($"[TrajectoryReceiver] {req.error}");
            yield break;
        }

        string json = req.downloadHandler.text;
        statusMsg = "Trayectoria recibida";
        Debug.Log($"[TrajectoryReceiver] Trayectoria recibida. Iniciando dibujo.");

        if (!strokeDrawer.IsDrawing)
        {
            GetComponent<UR3DirectSender>()?.PrepararNuevoDibujo();
            strokeDrawer.IniciarDibujo(json);
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    int ParseId(string json)
    {
        const string key = "\"_id\":";
        int idx = json.IndexOf(key);
        if (idx < 0) return 0;

        int start = idx + key.Length;
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t'))
            start++;

        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '-'))
            end++;

        return int.TryParse(json.Substring(start, end - start), out int id) ? id : 0;
    }
}