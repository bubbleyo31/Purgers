using System;
using System.Threading;
using System.Threading.Tasks;

namespace MultiClimb.Menu
{
    /// <summary>單次連線的等待期限；取消不依賴 Photon 工作是否願意結束。</summary>
    public sealed class MenuConnectionAttempt : IDisposable
    {
        private readonly CancellationTokenSource source = new CancellationTokenSource();
        public CancellationToken Token => source.Token;
        public bool CancelledByUser { get; private set; }

        public MenuConnectionAttempt(TimeSpan timeout) => source.CancelAfter(timeout);

        public void CancelByUser()
        {
            CancelledByUser = true;
            source.Cancel();
        }

        public async Task<T> WaitAsync<T>(Task<T> work)
        {
            // 原工作可能在取消後失敗；觀察例外，但不允許它更新選單或下一次連線。
            ObserveFault(work);
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (Token.Register(() => stopped.TrySetResult(true)))
            {
                await Task.WhenAny(work, stopped.Task);
                Token.ThrowIfCancellationRequested();
                return await work;
            }
        }

        public static void ObserveFault(Task work)
        {
            _ = work.ContinueWith(t => { var observed = t.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose() => source.Dispose();
    }
}
