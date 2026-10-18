using System;
using System.Threading.Tasks;
using Fusion.Menu;
using Purgers.Progression;
namespace MultiClimb.Menu
{
    public partial class MenuConnection
    {
        // The same session owner performs save then shutdown; clients never write the Host repository.
        public async Task LeaveAndSaveAsync()
        {
            var runner = _runner;
            if (runner && runner.IsRunning && runner.IsServer)
            {
                var context = runner.GetComponent<GameSaveRuntimeContext>();
                var logic = GameLogic.GetPrimaryForRunner(runner);
                if (!context || !context.HasWritableHostSave || !logic || !logic.TryWriteHostExperienceToSave(context.ActiveSave))
                    throw new InvalidOperationException("無法取得房主進度，房間保留，請稍後重試。");
                var result = context.WriteActiveSave();
                if (!result.Success) throw new InvalidOperationException(result.Error);
            }
            await DisconnectAsync(ConnectFailReason.UserRequest);
        }
    }
}
