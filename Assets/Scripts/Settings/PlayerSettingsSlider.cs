
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Purgers.Settings
{
    // Slider's default horizontal increment is 10% of its range; the agreed preview uses the displayed precision.
    public sealed class PlayerSettingsSlider : Slider
    {
        public float Step = .01f;
        public override void OnMove(AxisEventData eventData)
        {
            if (IsActive() && IsInteractable() && (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right))
            { value += eventData.moveDir == MoveDirection.Left ? -Step : Step; eventData.Use(); }
            else base.OnMove(eventData);
        }
    }
}

