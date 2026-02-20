using System.Diagnostics;
using System.Windows.Forms;
using Dx12WinForm.ProcessesDispatcher.Contracts;
using Dx12WinForm.ProcessesDispatcher.IPC;

namespace Dx12WinForm.ProcessesDispatcher.Managers;

/// <summary>
/// Manages the lifecycle of simulation processes and IPC communication.
/// 
/// CRITICAL: Implements safe cleanup to prevent zombie processes and shared memory leaks.
/// - Always call Dispose() when done
/// - Use Cleanup() for emergency/crash scenarios
/// - Global exception handlers should call Cleanup() before exit
/// </summary>
public sealed class LifeCycleManager : IDisposable
{
    private readonly SimProcessDispatcher _dispatcher;
    private readonly IpcController _ipc;
    private readonly UiStateSynchronizer _stateSynchronizer;
    private bool _disposed;

    public SimProcessDispatcher Dispatcher => _dispatcher;

    /// <summary>
    /// IPC controller for UI commands (Start/Pause/Stop).
    /// </summary>
    public IpcController Ipc => _ipc;

    /// <summary>
    /// Indicates if an external simulation process is attached and running.
    /// </summary>
    public bool IsExternalProcessAttached => _dispatcher.IsConnected;

    public LifeCycleManager()
    {
        _dispatcher = new SimProcessDispatcher();
        _ipc = new IpcController();
        _stateSynchronizer = new UiStateSynchronizer();
    }

    public async Task OnFormLoadAsync(CancellationToken cancellationToken = default)
    {
        await _dispatcher.EnsureSimulationRunningAsync(cancellationToken).ConfigureAwait(false);
        await _ipc.SendHandshakeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the external simulation state if shared memory is active and data is fresh.
    /// Returns: SimState indicating Running/Paused/Stopped, or null if unavailable.
    /// </summary>
    public SimState? TryGetExternalSimulationState(TimeSpan? maxAge = null)
    {
        var age = maxAge ?? DispatcherConfig.StaleDataThreshold;

        if (!_stateSynchronizer.TryDetectExternalSimulationRunning(age))
            return null;

        return _stateSynchronizer.GetCurrentState();
    }

    public async Task OnFormClosingAsync(FormClosingEventArgs e, CancellationToken cancellationToken = default)
    {
        if (e.CloseReason != CloseReason.UserClosing)
        {
            await RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = MessageBox.Show(
            "«авершить симул€цию?\n\nYes Ч закрыть с симул€цией.\nNo Ч оставить симул€цию работать в фоне.",
            "Simulation Process Dispatcher",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (result == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == DialogResult.Yes)
        {
            await RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // ћ€гкий детач: UI отключаетс€, симул€ци€ продолжает жить
            _dispatcher.Detach();
        }
    }

    private async Task RequestShutdownAsync(CancellationToken cancellationToken)
    {
        bool acknowledged = await _ipc.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(DispatcherConfig.ShutdownGracePeriod, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_dispatcher.IsConnected && acknowledged)
            await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken.None).ConfigureAwait(false);

        if (_dispatcher.IsConnected)
            await _dispatcher.ForceKillAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Emergency cleanup for crash scenarios.
    /// 
    /// Call this from:
    /// - AppDomain.CurrentDomain.UnhandledException handler
    /// - Application.ThreadException handler  
    /// - Program.cs try/finally block
    /// 
    /// This method is synchronous and does not throw exceptions.
    /// It forcefully terminates child processes and releases shared memory.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            // 1. Dispose state synchronizer (releases MemoryMappedFile)
            try
            {
                _stateSynchronizer?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[LifeCycleManager] StateSynchronizer cleanup failed: {ex.Message}");
            }

            // 2. Force kill child process if still running
            if (_dispatcher?.IsConnected == true)
            {
                try
                {
                    // Synchronous kill - don't await in cleanup
                    _dispatcher.ForceKillAsync(CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[LifeCycleManager] Process kill failed: {ex.Message}");
                }
            }

            Trace.WriteLine("[LifeCycleManager] Emergency cleanup completed");
        }
        catch (Exception ex)
        {
            // Swallow all exceptions in cleanup - we're likely in a crash handler
            Trace.WriteLine($"[LifeCycleManager] Cleanup failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes resources. Prefer calling Cleanup() in exception handlers,
    /// and Dispose() for normal shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Normal disposal - can throw if something goes wrong
        _stateSynchronizer?.Dispose();
        
        // Don't kill process on dispose - that's handled by OnFormClosingAsync
        // Only detach the handle
        _dispatcher?.Detach();
    }
}