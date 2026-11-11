using UnityEngine;
using UnityEngine.EventSystems; // Necesario para detectar el mouse
using TMPro; // Necesario para controlar TextMeshPro
// Este script se encarga de cambiar el color del texto del botón cuando el cursor del mouse entra o sale del área del botón.

// Las interfaces IPointerEnterHandler y IPointerExitHandler son los sensores del ratón
public class BotonHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Referencia al Texto")]
    public TextMeshProUGUI textoBoton;

    [Header("Configuración de Colores")]
    public Color colorNormal = Color.black;
    public Color colorHover = Color.white;

    void Start()
    {
        // Establecer el color inicial al arrancar
        if (textoBoton != null)
        {
            textoBoton.color = colorNormal;
        }
    }

    // Se ejecuta en el frame exacto en que el cursor entra al área del botón
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (textoBoton != null)
        {
            textoBoton.color = colorHover;
        }
    }

    // Se ejecuta en el frame exacto en que el cursor sale del área del botón
    public void OnPointerExit(PointerEventData eventData)
    {
        if (textoBoton != null)
        {
            textoBoton.color = colorNormal;
        }
    }
}