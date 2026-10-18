namespace MultiClimb.Menu
{
    /// <summary>每次連線獨立保存結束狀態；不在 Runner 銷毀後讀取其角色。</summary>
    public sealed class MenuSessionEndState
    {
        private bool connectedClient;
        private bool localExit;
        private bool ended;
        public bool HasEnded => ended;

        public void MarkConnected(bool isClient)
        {
            if (!ended) connectedClient = isClient;
        }

        public void MarkLocalExit()
        {
            localExit = true;
            ended = true;
        }

        public bool TryEndRemotely()
        {
            if (ended) return false;
            ended = true;
            return connectedClient && !localExit;
        }
    }
}
