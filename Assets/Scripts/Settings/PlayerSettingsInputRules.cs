namespace Purgers.Settings
{
    public static class PlayerSettingsInputRules
    {
        public const byte Valid = 128, GrappleHold = 1, SprintHold = 2;
        public static byte Modes(PlayerSettingsData settings) => (byte)(Valid | (settings.grappleToggle ? 0 : GrappleHold) | (settings.sprintToggle ? 0 : SprintHold));
        public static bool SprintActive(bool active, bool pressed, bool held, bool moving, bool holdMode) { return moving && (holdMode ? held : pressed ? !active : active); }
    }
}
