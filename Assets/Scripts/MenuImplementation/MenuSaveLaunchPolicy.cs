namespace MultiClimb.Menu
{
    public enum MenuSaveLaunchKind
    {
        QuickPlayNewHost = 0,
        FreshHost = 1,
        ContinueHost = 2,
        ClientJoin = 3
    }

    public static class MenuSaveLaunchPolicy
    {
        public static MenuSaveLaunchKind Resolve(
            bool creating,
            string sessionName,
            bool hasWritableHostSave)
        {
            if (!creating && string.IsNullOrEmpty(sessionName))
                return MenuSaveLaunchKind.QuickPlayNewHost;

            if (!creating)
                return MenuSaveLaunchKind.ClientJoin;

            return hasWritableHostSave
                ? MenuSaveLaunchKind.ContinueHost
                : MenuSaveLaunchKind.FreshHost;
        }
    }
}
