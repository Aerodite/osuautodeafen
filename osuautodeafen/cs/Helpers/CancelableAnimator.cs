using System;
using System.Threading;
using System.Threading.Tasks;

namespace osuautodeafen.cs.Helpers;

public sealed class CancelableAnimator : IDisposable
{
    private CancellationTokenSource? _cts;

    public async Task RunAsync(Func<CancellationToken, Task> animation)
    {
        Cancel();

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        try
        {
            await animation(token);
        }
        catch (OperationCanceledException)
        {
            // Expected — ignore
        }
    }

    public void Cancel()
    {
        if (_cts == null)
            return;

        _cts.Cancel();
        _cts.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Cancel();
    }
}