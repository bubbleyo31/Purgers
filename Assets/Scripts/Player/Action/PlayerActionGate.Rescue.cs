public partial class PlayerActionGate
{
    private PlayerHealth rescueHealth;
    private PlayerActionBlockMask RescueBlockedActions
    {
        get
        {
            if (rescueHealth == null) rescueHealth = GetComponent<PlayerHealth>();
            return rescueHealth != null ? rescueHealth.RescueBlockedActions : PlayerActionBlockMask.None;
        }
    }
}
