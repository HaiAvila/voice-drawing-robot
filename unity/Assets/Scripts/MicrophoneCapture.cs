using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Android;

public class MicrophoneCapture : MonoBehaviour
{
    [Header("Conexión Servidor")]
    private string serverUrl = NetworkConfig.Url + "/audio";
    public TMP_Text statusText;

    [Header("Referencias")]
    public TrajectoryReceiver trajectoryReceiver;

    [Header("UI - Botones Físicos")]
    public Button leftButton;
    public Image leftButtonImage;
    public Button rightButton;
    public Image rightButtonImage;

    [Header("UI - Visualización")]
    public Transform audioWaveContainer;
    public TMP_Text timerText;

    [Header("Assets - Botón Izquierdo")]
    public Sprite micIdle;
    public Sprite micHover;
    public Sprite pauseIdle;
    public Sprite pauseHover;
    public Sprite playIdle;
    public Sprite playHover;

    [Header("Assets - Botón Derecho")]
    public Sprite sendIdle;
    public Sprite sendHover;
    public Sprite sendDeactivated;

    // Nuevos sprites para el estado Sent
    public Sprite reloadIdle;
    public Sprite reloadHover;

    [Header("Configuración Onda de Audio")]
    public float baseHeight = 10f;
    public float maxVolumeHeight = 60f;
    public float waveSmoothness = 15f;

    // Se agrega el estado Sent a la máquina de estados
    private enum PanelState { Idle, Recording, Paused, Sending, Sent, Blocked }
    private PanelState currentState = PanelState.Idle;

    private string device;
    private AudioClip currentClip;
    private bool isRecording = false;
    private float totalRecordingTime = 0f;

    private List<float> audioBuffer = new List<float>();
    private int sampleRate = 16000;

    private RectTransform[] waveBars;
    private float[] bellCurveMultipliers;

    void Start()
    {
        device = Microphone.devices.Length > 0 ? Microphone.devices[0] : null;

        #if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
        #endif

        ConfigurarOndaVisual();
        SetState(PanelState.Idle);
    }

    void Update()
    {
        if (isRecording)
        {
            totalRecordingTime += Time.deltaTime;
            int minutes = Mathf.FloorToInt(totalRecordingTime / 60F);
            int seconds = Mathf.FloorToInt(totalRecordingTime - minutes * 60);

            if (timerText != null)
                timerText.text = string.Format("{0}:{1:00}", minutes, seconds);

            AnimarOndaDeAudio(ObtenerVolumenRMS());
        }
        else
        {
            AnimarOndaDeAudio(0f);
        }
    }

    private void SetState(PanelState newState)
    {
        currentState = newState;
        SpriteState leftState = leftButton.spriteState;
        SpriteState rightState = rightButton.spriteState;

        switch (currentState)
        {
            case PanelState.Idle:
                // Botón izquierdo: micrófono
                leftButtonImage.sprite = micIdle;
                leftState.highlightedSprite = micHover;
                leftState.pressedSprite = micHover;
                leftState.selectedSprite = micIdle;
                leftButton.interactable = true;

                // Botón derecho: desactivado
                rightButtonImage.sprite = sendDeactivated;
                rightState.disabledSprite = sendDeactivated;
                rightButton.interactable = false;

                if (audioWaveContainer != null) audioWaveContainer.gameObject.SetActive(false);
                if (timerText != null) timerText.text = "0:00";
                if (statusText != null) statusText.text = "Listo";
                break;

            case PanelState.Recording:
                // Botón izquierdo: pausa
                leftButtonImage.sprite = pauseIdle;
                leftState.highlightedSprite = pauseHover;
                leftState.pressedSprite = pauseHover;
                leftState.selectedSprite = pauseIdle;
                leftButton.interactable = true;

                // Botón derecho: send activo
                rightButtonImage.sprite = sendIdle;
                rightState.highlightedSprite = sendHover;
                rightState.pressedSprite = sendHover;
                rightState.selectedSprite = sendIdle;
                rightButton.interactable = true;

                if (audioWaveContainer != null) audioWaveContainer.gameObject.SetActive(true);
                if (statusText != null) statusText.text = "Grabando...";
                break;

            case PanelState.Paused:
                // Botón izquierdo: play
                leftButtonImage.sprite = playIdle;
                leftState.highlightedSprite = playHover;
                leftState.pressedSprite = playHover;
                leftState.selectedSprite = playIdle;
                leftButton.interactable = true;

                // Botón derecho: send activo
                rightButtonImage.sprite = sendIdle;
                rightState.highlightedSprite = sendHover;
                rightState.pressedSprite = sendHover;
                rightState.selectedSprite = sendIdle;
                rightButton.interactable = true;

                if (statusText != null) statusText.text = "Pausado";
                break;

            case PanelState.Sending:
                // Causa: el audio está en tránsito.
                // Efecto: ambos botones se desactivan para evitar
                // interacciones accidentales durante el envío.
                leftButtonImage.sprite = micIdle;
                leftButton.interactable = false;

                rightButtonImage.sprite = sendDeactivated;
                rightState.disabledSprite = sendDeactivated;
                rightButton.interactable = false;

                if (statusText != null) statusText.text = "Enviando...";
                break;

            case PanelState.Sent:
                // Causa: el envío terminó (éxito o error).
                // Efecto: el botón derecho cambia a reload para
                // permitir reiniciar sin volver al menú Home.
                // El botón izquierdo se desactiva — ya no tiene
                // sentido grabar más sobre este audio enviado.
                leftButtonImage.sprite = micIdle;
                leftButton.interactable = false;

                rightButtonImage.sprite = reloadIdle;
                rightState.highlightedSprite = reloadHover;
                rightState.pressedSprite = reloadHover;
                rightState.selectedSprite = reloadIdle;
                rightButton.interactable = true;

                if (audioWaveContainer != null) audioWaveContainer.gameObject.SetActive(false);
                break;

                case PanelState.Blocked:
                leftButtonImage.sprite = micIdle;
                leftButton.interactable = false;

                rightButtonImage.sprite = sendDeactivated;
                rightState.disabledSprite = sendDeactivated;
                rightButton.interactable = false;

                if (audioWaveContainer != null) audioWaveContainer.gameObject.SetActive(false);
                if (statusText != null) statusText.text = "Esperando tu decisión...";
                break;
        }

        leftButton.spriteState = leftState;
        rightButton.spriteState = rightState;
    }

    public void AccionBotonIzquierdo()
    {
        if (currentState == PanelState.Idle) IniciarGrabacionNueva();
        else if (currentState == PanelState.Recording) PausarGrabacion();
        else if (currentState == PanelState.Paused) ReanudarGrabacion();
        // Sending y Sent no hacen nada con el botón izquierdo
    }

    public void AccionBotonDerecho()
    {
        if (currentState == PanelState.Recording || currentState == PanelState.Paused)
        {
            EmpaquetarYEnviar();
        }
        else if (currentState == PanelState.Sent)
        {
            // Causa: el usuario quiere grabar otro audio.
            // Efecto: se reinicia todo al estado inicial.
            ReiniciarParaNuevaGrabacion();
        }
    }

    private void ReiniciarParaNuevaGrabacion()
    {
        audioBuffer.Clear();
        totalRecordingTime = 0f;
        SetState(PanelState.Idle);
    }

    private void IniciarGrabacionNueva()
    {
        audioBuffer.Clear();
        totalRecordingTime = 0f;
        ReanudarGrabacion();
    }

    private void ReanudarGrabacion()
    {
        isRecording = true;
        currentClip = Microphone.Start(device, false, 30, sampleRate);
        SetState(PanelState.Recording);
    }

    private void PausarGrabacion()
    {
        isRecording = false;
        GuardarFragmentoEnBuffer();
        SetState(PanelState.Paused);
    }

    private void EmpaquetarYEnviar()
    {
        isRecording = false;
        if (Microphone.IsRecording(device)) GuardarFragmentoEnBuffer();

        if (audioBuffer.Count > 0)
        {
            AudioClip clipFinal = AudioClip.Create("AudioCompleto", audioBuffer.Count, 1, sampleRate, false);
            clipFinal.SetData(audioBuffer.ToArray(), 0);
            StartCoroutine(EnviarWav(ToWav(clipFinal)));
        }
        else
        {
            SetState(PanelState.Idle);
        }
    }

    private void GuardarFragmentoEnBuffer()
    {
        int muestras = Microphone.GetPosition(device);
        Microphone.End(device);

        if (muestras > 0 && currentClip != null)
        {
            float[] data = new float[muestras * currentClip.channels];
            currentClip.GetData(data, 0);
            audioBuffer.AddRange(data);
        }
    }

    private void ConfigurarOndaVisual()
    {
        if (audioWaveContainer == null) return;
        int childCount = audioWaveContainer.childCount;
        waveBars = new RectTransform[childCount];
        bellCurveMultipliers = new float[childCount];

        for (int i = 0; i < childCount; i++)
        {
            waveBars[i] = audioWaveContainer.GetChild(i).GetComponent<RectTransform>();
            float distanceToCenter = Mathf.Abs(((float)i / (childCount - 1)) - 0.5f) * 2f;
            bellCurveMultipliers[i] = Mathf.Cos(distanceToCenter * Mathf.PI / 2f);
        }
    }

    private void AnimarOndaDeAudio(float volumeRMS)
    {
        if (waveBars == null) return;
        for (int i = 0; i < waveBars.Length; i++)
        {
            float targetHeight = baseHeight + (volumeRMS * maxVolumeHeight * bellCurveMultipliers[i]);
            Vector2 size = waveBars[i].sizeDelta;
            size.y = Mathf.Lerp(size.y, targetHeight, Time.deltaTime * waveSmoothness);
            waveBars[i].sizeDelta = size;
        }
    }

    private float ObtenerVolumenRMS()
    {
        int sampleSize = 256;
        float[] sampleData = new float[sampleSize];
        int micPosition = Microphone.GetPosition(device) - sampleSize + 1;

        if (micPosition < 0 || currentClip == null) return 0;

        currentClip.GetData(sampleData, micPosition);
        float sum = 0;
        for (int i = 0; i < sampleSize; i++) sum += sampleData[i] * sampleData[i];
        return Mathf.Sqrt(sum / sampleSize) * 80f;
    }

    IEnumerator EnviarWav(byte[] wav)
    {
        // Causa: inicia el envío.
        // Efecto: se bloquean ambos botones mientras dura la petición HTTP.
        SetState(PanelState.Sending);
        DialogueManager.Instance?.IniciarEtapa5();

        WWWForm form = new WWWForm();
        form.AddBinaryData("audio", wav, "audio.wav", "audio/wav");

        var req = new UnityWebRequest(serverUrl, "POST");
        req.uploadHandler = new UploadHandlerRaw(form.data);
        req.downloadHandler = new DownloadHandlerBuffer();

        foreach (var header in form.headers)
            req.SetRequestHeader(header.Key, header.Value);

        var operacion = req.SendWebRequest();
        float inicio = Time.time;

        while (!operacion.isDone)
        {
            if (Time.time - inicio > 180.5f)
            {
                req.Abort();
                req.Dispose();

                // Timeout: mostramos el error y pasamos a Sent
                // para que el usuario pueda reiniciar.
                if (statusText != null) statusText.text = "Error de red";
                SetState(PanelState.Sent);
                yield break;
            }
            yield return null;
        }

        // Causa: el servidor respondió (éxito o error HTTP).
        // Efecto: se muestra el resultado y aparece el botón reload.
        if (statusText != null)
            statusText.text = req.result == UnityWebRequest.Result.Success
                ? "Enviado"
                : "Error";

        req.Dispose();

        // Transición a Sent: el botón derecho se convierte en reload
        SetState(PanelState.Sent);
        DialogueManager.Instance?.IniciarEtapa6();
    }

    static byte[] ToWav(AudioClip c)
    {
        float[] s = new float[c.samples * c.channels];
        c.GetData(s, 0);
        short[] pcm = new short[s.Length];
        for (int i = 0; i < s.Length; i++) pcm[i] = (short)(Mathf.Clamp(s[i], -1f, 1f) * 32767f);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int data = pcm.Length * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + data);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); bw.Write((short)1);
        bw.Write((short)c.channels); bw.Write(c.frequency);
        bw.Write(c.frequency * c.channels * 2);
        bw.Write((short)(c.channels * 2)); bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(data);
        foreach (short v in pcm) bw.Write(v);
        return ms.ToArray();
    }

    public void BloquearMicrofono()
    {
        if (isRecording)
        {
            Microphone.End(device);
            isRecording = false;
        }
        SetState(PanelState.Blocked);
    }

    public void DesbloquearMicrofono()
    {
        SetState(PanelState.Idle);
    }

    public void ForzarReinicio()
    {
        if (isRecording)
        {
            Microphone.End(device);
            isRecording = false;
        }

        audioBuffer.Clear();
        totalRecordingTime = 0f;
        SetState(PanelState.Idle);
    }
}