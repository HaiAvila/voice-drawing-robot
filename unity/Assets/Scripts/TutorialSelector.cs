using UnityEngine;

public class TutorialSelector : MonoBehaviour
{
    [Header("Referencias")]
    public DialogueManager dialogueManager;
    public MicrophoneCapture microphoneCapture;

    [Header("Panel a ocultar al elegir")]
    public GameObject canvasTutorial;  // arrastra Canvas_tutorial aquí

    public void ElegirConTutorial()
    {
        dialogueManager?.SetTutorialActivo(true);
        canvasTutorial.SetActive(false);  // ahora apunta a panel_gris ✓
        microphoneCapture?.DesbloquearMicrofono();
        dialogueManager?.IniciarEtapa3();
    }

    public void ElegirSinTutorial()
    {
        dialogueManager?.SetTutorialActivo(false);
        canvasTutorial.SetActive(false);  // panel_gris ✓
        microphoneCapture?.DesbloquearMicrofono();
    }
}