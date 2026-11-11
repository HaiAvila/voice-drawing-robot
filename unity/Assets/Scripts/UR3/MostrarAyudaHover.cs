using UnityEngine;
using UnityEngine.EventSystems;

public class MostrarAyudaHover : MonoBehaviour, 
    IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("Imagen o panel que explica cómo hacer pinch")]
    public GameObject panelAyudaPinch;

    public void OnPointerEnter(PointerEventData e)
    {
        if (panelAyudaPinch != null)
            panelAyudaPinch.SetActive(true);
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (panelAyudaPinch != null)
            panelAyudaPinch.SetActive(false);
    }
}