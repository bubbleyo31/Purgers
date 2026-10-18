using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TeamInfoTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public LocalTeamInfoPanel Owner;
    public string Title, Description, State;
    public bool HasContent;
    public void OnPointerEnter(PointerEventData data) { if (HasContent) Owner.ShowTooltip(this); }
    public void OnPointerExit(PointerEventData data) => Owner.HideTooltip(this);
    void OnDisable() { if (Owner != null) Owner.HideTooltip(this); }
}
