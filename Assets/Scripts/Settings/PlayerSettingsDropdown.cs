using TMPro;
using UnityEngine;

namespace Purgers.Settings
{
    /// <summary>Keep native dropdown choices and their blocker above the owning settings Canvas.</summary>
    public sealed class PlayerSettingsDropdown : TMP_Dropdown
    {
        protected override GameObject CreateDropdownList(GameObject template)
        {
            var list = base.CreateDropdownList(template);
            var owner = GetComponentInParent<Canvas>();
            if (owner)
            {
                var popup = list.GetComponent<Canvas>();
                popup.overrideSorting = true;
                popup.sortingLayerID = owner.sortingLayerID;
                // Native CreateBlocker uses popup order - 1. Reserve both layers above the menu.
                popup.sortingOrder = owner.sortingOrder + 2;
            }
            return list;
        }
    }
}