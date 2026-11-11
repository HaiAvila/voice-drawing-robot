using System.Collections;
using UnityEngine;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("Micrófono")]
    public MicrophoneCapture microphoneCapture;

    [Header("Audio Source")]
    public AudioSource audioSource;

    [Header("Tutorial")]
    public AudioClip tutorialInicio;
    public AudioClip tutorialFinal;

    [Header("Etapa 1 — Menú inactivo")]
    public AudioClip[] etapa1;

    [Header("Etapa 2 — Al presionar Iniciar")]
    public AudioClip[] etapa2;

    [Header("Etapa 3 — Función + panel (con tutorial)")]
    public AudioClip[] etapa3;

    [Header("Etapa 3 — Sin tutorial")]
    public AudioClip etapa3SinTutorial;

    [Header("Etapa 4 — Interrupción")]
    public AudioClip[] etapa4;

    [Header("Etapa 5 — Procesando / enviando")]
    public AudioClip[] etapa5;

    [Header("Etapa 6 — Resultado recibido")]
    public AudioClip[] etapa6;

    [Header("Etapa 7 — Muchos waypoints")]
    public AudioClip[] etapa7Muchos;

    [Header("Etapa 7 — Pocos waypoints")]
    public AudioClip etapa7Pocos;

    [Header("Etapa 8 — Rants durante simulación")]
    public AudioClip[] etapa8;

    [Header("Etapa 9 — Simulación completada")]
    public AudioClip[] etapa9;

    [Header("Panel decisión")]
    public PanelDecision panelDecision;

    [Header("Etapa 10 — Decisión final")]
    public AudioClip etapa10SiA;
    public AudioClip etapa10SiB;
    public AudioClip etapa10NoA;
    public AudioClip etapa10NoB;

    [Header("Configuración de tiempos")]
    [Tooltip("Segundos de inactividad antes de que el robot empiece a hablar en el menú")]
    public float idleTimeBeforeEtapa1 = 5f;
    [Tooltip("Segundos de espera antes de reproducir el Tutorial 1 al cargar la escena")]
    public float tutorialStartDelay = 3f;
    [Tooltip("Segundos entre rants durante la simulación")]
    public float rantInterval = 5f;
    [Tooltip("Número de letras/waypoints que se considera 'muchos'")]
    public int waypointThreshold = 3;

    // Estado interno
    private bool tutorialActivo;
    private bool tutorialFinalReproducido = false;
    private bool enMenuInactivo = false;
    private bool enEtapa8 = false;
    private float idleTimer = 0f;
    private float rantTimer = 0f;
    private int lastRantIndex = -1;

    // ── Ciclo de vida ──────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Si ya vio el tutorial antes, ir directo
        tutorialActivo = PlayerPrefs.GetInt("tutorial_visto", 0) == 0;
    }

    void Update()
    {
        // Idle en menú principal → disparar Etapa 1
        if (enMenuInactivo && !audioSource.isPlaying)
        {
            idleTimer += Time.deltaTime;
            if (idleTimer >= idleTimeBeforeEtapa1)
            {
                idleTimer = 0f;
                ReproducirAleatorio(etapa1);
            }
        }

        // Rants de Etapa 8 cada N segundos
        if (enEtapa8 && !audioSource.isPlaying)
        {
            rantTimer += Time.deltaTime;
            if (rantTimer >= rantInterval)
            {
                rantTimer = 0f;
                ReproducirRant();
            }
        }
    }

    // ── API pública — llamada desde otros scripts ──────────────────────────

    /// <summary>
    /// Llamar cuando la escena carga o el usuario regresa al menú (Home).
    /// Si tutorialActivo, reproduce Tutorial 1 primero y luego activa el idle.
    /// Si no, activa el idle directamente.
    /// </summary>
    public void IniciarMenuPrincipal()
    {
        StopAllCoroutines();
        enMenuInactivo = false;
        enEtapa8 = false;
        idleTimer = 0f;
        rantTimer = 0f;
        audioSource.Stop();

        if (tutorialActivo)
        {
            StartCoroutine(EsperarYReproducir(tutorialInicio, tutorialStartDelay, () =>
            {
                enMenuInactivo = true;
                idleTimer = 0f;
            }));
        }
        else
        {
            enMenuInactivo = true;
            idleTimer = 0f;
        }
    }

    /// <summary>
    /// Llamar cuando el usuario presiona Iniciar.
    /// Reproduce Etapa 2 y encadena automáticamente con Etapa 3.
    /// </summary>
    public void IniciarEtapa2()
    {
        enMenuInactivo = false;
        idleTimer = 0f;
        StopAllCoroutines();
        ReproducirAleatorio(etapa2);
        StartCoroutine(EsperarAudioYLuego(IniciarEtapa3));
    }

    /// <summary>
    /// Se encadena automáticamente después de Etapa 2.
    /// Con tutorial: explica el panel completo.
    /// Sin tutorial: solo saludo breve.
    /// </summary>
    public void IniciarEtapa3()
    {
        if (tutorialActivo)
            ReproducirAleatorio(etapa3);
        else
            Reproducir(etapa3SinTutorial);
    }

    /// <summary>
    /// Llamar si el usuario interrumpe mientras el robot habla o procesa.
    /// Solo dispara si no hay audio reproduciéndose para no interrumpir al robot.
    /// </summary>
    public void IniciarEtapa4()
    {
        if (!audioSource.isPlaying)
            ReproducirAleatorio(etapa4);
    }

    /// <summary>
    /// Llamar cuando MicrophoneCapture entra en estado Sending.
    /// </summary>
    public void IniciarEtapa5()
    {
        enEtapa8 = false;
        StopAllCoroutines();
        ReproducirAleatorio(etapa5);
    }

    /// <summary>
    /// Llamar cuando el servidor respondió (estado Sent en MicrophoneCapture).
    /// </summary>
    public void IniciarEtapa6()
    {
        StopAllCoroutines();
        ReproducirAleatorio(etapa6);
    }

    /// <summary>
    /// Llamar cuando RobotProgram empieza a dibujar.
    /// waypointCount = número de letras o waypoints reales cuando llegue el pipeline.
    /// </summary>
    public void IniciarEtapa7(int waypointCount)
    {
        StopAllCoroutines();
        bool muchos = waypointCount > waypointThreshold;

        if (muchos)
            ReproducirAleatorio(etapa7Muchos);
        else
            Reproducir(etapa7Pocos);

        // Encadenar con rants después de que termine Etapa 7
        StartCoroutine(EsperarAudioYLuego(() =>
        {
            enEtapa8 = true;
            rantTimer = 0f;
        }));
    }

    /// <summary>
    /// Llamar cuando RobotProgram llega a Phase.Finished.
    /// </summary>
   public void IniciarEtapa9()
    {
        enEtapa8 = false;
        StopAllCoroutines();
        microphoneCapture?.BloquearMicrofono(); // ← bloquear aquí
        ReproducirAleatorio(etapa9);
        StartCoroutine(EsperarAudioYLuego(() => panelDecision?.Mostrar()));
    }

    /// <summary>
    /// Llamar cuando el usuario presiona Sí o No en la decisión final.
    /// aprobo = true si presionó Sí, false si presionó No.
    /// </summary>
    public void IniciarEtapa10(bool aprobo)
    {
        StopAllCoroutines();
        bool opcionA = Random.value > 0.5f;

        if (aprobo)
            Reproducir(opcionA ? etapa10SiA : etapa10SiB);
        else
            Reproducir(opcionA ? etapa10NoA : etapa10NoB);

        if (!tutorialFinalReproducido)
        {
            tutorialFinalReproducido = true;
            PlayerPrefs.SetInt("tutorial_visto", 1);
            tutorialActivo = false;
            StartCoroutine(EsperarAudioYLuego(() =>
            {
                Reproducir(tutorialFinal);
                StartCoroutine(EsperarAudioYLuego(() =>
                    microphoneCapture?.DesbloquearMicrofono()));
            }));
        }
        else
        {
            // Sin tutorial final: desbloquear cuando termine el audio de decisión
            StartCoroutine(EsperarAudioYLuego(() =>
                microphoneCapture?.DesbloquearMicrofono()));
        }
    }

    /// <summary>
    /// Elegir modo tutorial desde el panel de selección al inicio.
    /// Llamar antes de IniciarMenuPrincipal().
    /// </summary>
    public void SetTutorialActivo(bool activo)
    {
       tutorialActivo = activo;
    }

    /// <summary>
    /// Reinicio completo. Llamar desde ControladorTransicion.RegresarAlInicio().
    /// </summary>
    public void Reiniciar()
    {
        StopAllCoroutines();
        enEtapa8 = false;
        enMenuInactivo = false;
        idleTimer = 0f;
        rantTimer = 0f;
        audioSource.Stop();
        tutorialActivo = true; // ← resetear para que el panel vuelva a preguntar
        IniciarMenuPrincipal();
    }

    // ── Métodos internos ───────────────────────────────────────────────────

    void Reproducir(AudioClip clip)
    {
        if (clip == null) return;
        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.Play();
    }

    void ReproducirAleatorio(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return;
        int idx = Random.Range(0, clips.Length);
        Reproducir(clips[idx]);
    }

    void ReproducirRant()
    {
        if (etapa8 == null || etapa8.Length == 0) return;

        // Evitar repetir el mismo rant dos veces seguidas
        int idx;
        int intentos = 0;
        do
        {
            idx = Random.Range(0, etapa8.Length);
            intentos++;
        }
        while (idx == lastRantIndex && etapa8.Length > 1 && intentos < 10);

        lastRantIndex = idx;
        Reproducir(etapa8[idx]);
    }

    IEnumerator EsperarYReproducir(AudioClip clip, float delay, System.Action onDone = null)
    {
        yield return new WaitForSeconds(delay);
        Reproducir(clip);
        onDone?.Invoke();
    }

    IEnumerator EsperarAudioYLuego(System.Action accion)
    {
        // Espera a que el audio actual termine
        yield return new WaitUntil(() => !audioSource.isPlaying);
        yield return new WaitForSeconds(0.3f); // pequeña pausa entre etapas
        accion?.Invoke();
    }
}