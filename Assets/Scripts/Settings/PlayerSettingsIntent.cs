using UnityEngine;
namespace Purgers.Settings
{
    // Render-frame intent only; gameplay state stays in the existing network controllers.
    public sealed class PlayerSettingsIntent
    {
        uint blocked;
        bool aim, crouch;
        public void BlockHeld(uint held) { blocked |= held & ~Bit(PlayerBinding.Crouch); aim = false; }
        public uint Rearm(uint held) { blocked &= held; return held & ~blocked; }
        public bool Aim(bool held, bool pressed, bool toggle) => aim = toggle ? (pressed ? !aim : aim) : held;
        public bool Crouch(bool held, bool pressed, bool toggle) => crouch = toggle ? (pressed ? !crouch : crouch) : held;
        public void ResetModes() { aim = false; crouch = false; }
        public static uint Bit(PlayerBinding action) => 1u << (int)action;
    }
}
