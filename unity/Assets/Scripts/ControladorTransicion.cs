using UnityEngine;

public class ControladorTransicion : MonoBehaviour
{
    [Header("Tutorial")]
    public GameObject panelGrisTutorial;

    [Header("Reset")]
    public PanelDecision panelDecision;
    public StrokeDrawer strokeDrawer;
    public UR3DirectSender ur3DirectSender;

    [Header("Elementos de UI Principal")]
    public CanvasGroup panelMenu;
    public GameObject canvasRaizInicio;
    public GameObject objetoMicrofono;

    [Header("Botón Home")]
    public GameObject objetoHome;

    [Header("Conexión Lógica")]
    public MicrophoneCapture scriptMicrofono;

    [Header("Entorno Real (Passthrough)")]
    public GameObject capaPassthrough;

    [Header("Parámetros de Transición")]
    public float duracionTransicion = 1.5f;

    [Header("Diálogos")]
    public DialogueManager dialogueManager;

    [Header("Trayectoria")]
    public TrajectoryReceiver trajectoryReceiver;

    [Header("Escena 3D")]
    public GameObject brazoUR3;
    public GameObject mesaPatioTable;

    private bool enTransicion = false;
    private float tiempoActual = 0f;

    void Start()
    {
        PlayerPrefs.DeleteKey("tutorial_visto"); // ← quitar después de probar
        PlayerPrefs.Save();

        if (capaPassthrough != null) capaPassthrough.SetActive(false);
        if (objetoHome != null)      objetoHome.SetActive(false);
        if (brazoUR3 != null)        brazoUR3.SetActive(false);
        if (mesaPatioTable != null)  mesaPatioTable.SetActive(false);

        dialogueManager?.IniciarMenuPrincipal();
    }

    void Update()
    {
        if (enTransicion)
        {
            tiempoActual += Time.deltaTime;
            float progreso = tiempoActual / duracionTransicion;

            if (panelMenu != null)
                panelMenu.alpha = Mathf.Lerp(1.0f, 0.0f, progreso);

            if (progreso >= 1.0f)
            {
                enTransicion = false;

                if (canvasRaizInicio != null) canvasRaizInicio.SetActive(false);
                if (objetoMicrofono != null)  objetoMicrofono.SetActive(true);
                if (objetoHome != null)       objetoHome.SetActive(true);
                if (brazoUR3 != null)         brazoUR3.SetActive(true);
                if (mesaPatioTable != null)   mesaPatioTable.SetActive(true);

                trajectoryReceiver?.ResetAndStartPolling();

                // Mostrar tutorial o desbloquear directo
                if (PlayerPrefs.GetInt("tutorial_visto", 0) == 0)
                {
                    scriptMicrofono?.BloquearMicrofono();
                    if (panelGrisTutorial != null) panelGrisTutorial.SetActive(true);
                }
                else
                {
                    scriptMicrofono?.DesbloquearMicrofono();
                }
            }
        }
    }

    public void ArrancarSecuencia()
    {
        enTransicion = true;
        tiempoActual = 0f;
        dialogueManager?.IniciarEtapa2();

        if (panelMenu != null)
        {
            panelMenu.interactable   = false;
            panelMenu.blocksRaycasts = false;
        }

        if (capaPassthrough != null) capaPassthrough.SetActive(true);
        if (brazoUR3 != null)        brazoUR3.SetActive(true);
        if (mesaPatioTable != null)  mesaPatioTable.SetActive(true);
    }

    public void RegresarAlInicio()
    {
        if (scriptMicrofono != null) scriptMicrofono.ForzarReinicio();
        if (objetoMicrofono != null) objetoMicrofono.SetActive(false);
        if (objetoHome != null)      objetoHome.SetActive(false);

        // Ocultar panel tutorial por si estaba visible
        if (panelGrisTutorial != null) panelGrisTutorial.SetActive(false);

        panelDecision?.Ocultar();
        strokeDrawer?.ResetCompleto();
        ur3DirectSender?.ResetCompleto();
        trajectoryReceiver?.StopPolling();

        if (canvasRaizInicio != null) canvasRaizInicio.SetActive(true);
        if (panelMenu != null)
        {
            panelMenu.alpha          = 1.0f;
            panelMenu.interactable   = true;
            panelMenu.blocksRaycasts = true;
        }

        if (capaPassthrough != null) capaPassthrough.SetActive(false);

        dialogueManager?.Reiniciar();

        if (brazoUR3 != null)        brazoUR3.SetActive(false);
        if (mesaPatioTable != null)  mesaPatioTable.SetActive(false);
    }
}