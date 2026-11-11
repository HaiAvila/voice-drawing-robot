using UnityEngine;

public class PanelDecision : MonoBehaviour
{
    [Header("Referencias")]
    public StrokeDrawer      strokeDrawer;
    public MicrophoneCapture microphoneCapture;
    public TrajectoryReceiver trajectoryReceiver;

    [Header("Botones")]
    public GameObject botonSi;
    public GameObject botonNo;

    public void Mostrar()
    {
        if (botonSi != null) botonSi.SetActive(true);
        if (botonNo != null) botonNo.SetActive(true);
    }

    public void OnSi()
    {
        botonSi?.SetActive(false);
        botonNo?.SetActive(false);
        DialogueManager.Instance?.IniciarEtapa10(true);
    }

    public void OnNo()
    {
        botonSi?.SetActive(false);
        botonNo?.SetActive(false);
        DialogueManager.Instance?.IniciarEtapa10(false);
        strokeDrawer?.LimpiarDibujo();
        microphoneCapture?.ForzarReinicio();
        //trajectoryReceiver?.StartPolling(); // ← reactivar polling
    }
    public void Ocultar()
    {
        botonSi?.SetActive(false);
        botonNo?.SetActive(false);
    }
}