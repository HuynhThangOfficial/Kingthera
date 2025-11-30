using UnityEngine;
using UnityEngine.EventSystems;

public class PieceButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public PieceData pieceData; 

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (pieceData != null)
            TooltipUI.Instance.ShowTooltip(pieceData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance.HideTooltip();
    }
}
