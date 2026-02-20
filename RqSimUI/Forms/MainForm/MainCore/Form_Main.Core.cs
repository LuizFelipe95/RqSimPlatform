using RqSimForms.ProcessesDispatcher;
using RqSimForms.ProcessesDispatcher.Managers;
using System.Diagnostics;

namespace RqSimForms;

partial class Form_Main_RqSim
{
    // === Simulation API (contains all non-UI logic) ===
    private readonly Forms.Interfaces.RqSimEngineApi _simApi = new();
    private readonly LifeCycleManager _lifeCycleManager = new();

    public Form_Main_RqSim() : this(new LifeCycleManager())
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _lifeCycleManager?.Dispose();
        }
        base.Dispose(disposing);
    }
    private void InitializeUiAfterDesigner()
    {
        InitializeGpuControls();
        InitializeMultiGpuControls();
        InitializeAllSettingsControls();
        InitializeUniPipelineTab();
        WireLiveParameterHandlers();
        WireModuleCheckboxHandlers();
        InitializeEmbeddedForms();
    }
    public Form_Main_RqSim(LifeCycleManager lifeCycleManager)
    {
        ArgumentNullException.ThrowIfNull(lifeCycleManager);
        _lifeCycleManager = lifeCycleManager;
        _lifeCycleManager.Logger = msg => Debug.WriteLine(msg);

        // Ensure new directory structure and migrate legacy settings once
        SessionStoragePaths.EnsureDefaultDirectoryStructure();
        TryMigrateOldSettings();

        InitializeComponent();
        InitializeUiAfterDesigner();

        FormClosing += Form_Main_FormClosing;
        Shown += Form_Main_Shown;
    }

    private async void Form_Main_Shown(object? sender, EventArgs e)
    {
        try
        {
            await _lifeCycleManager.OnFormLoadAsync();
            Debug.WriteLine("[Form_Main_Shown] Form loaded");

            // Probe for external console simulation (up to 15 attempts, 500ms apart)
            for (int i = 0; i < 15; i++)
            {
                var externalState = _lifeCycleManager.TryGetExternalSimulationState();
                Debug.WriteLine($"[Form_Main_Shown] Attempt {i + 1}: externalState={(externalState is not null ? $"Nodes={externalState.Value.NodeCount}, Status={externalState.Value.Status}" : "null")}");

                if (externalState is not null)
                {
                    SyncToExternalSimulation(externalState.Value);
                    return;
                }

                await Task.Delay(500);
            }

            // If process is attached but no shared memory data yet, still enable external mode
            if (_lifeCycleManager.IsExternalProcessAttached)
            {
                Debug.WriteLine("[Form_Main_Shown] External process attached but no state - enabling external mode");
                _isExternalSimulation = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Simulation Process Dispatcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool _isAsyncCloseCompleted = false;


    private void Form_Main_Load(object? sender, EventArgs e)
    {
        LoadAndApplySettings();
    }
    private async void Form_Main_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_isAsyncCloseCompleted) return;

        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            try
            {
                bool shouldClose = await _lifeCycleManager.HandleClosingAsync(e.CloseReason);
                if (shouldClose)
                {
                    SaveCurrentSettings();
                    _simApi.Cleanup();
                    _isAsyncCloseCompleted = true;
                    Close();
                }
            }
            catch
            {
                _isAsyncCloseCompleted = true;
                Close();
            }
        }
        else
        {
            try
            {
                _lifeCycleManager.HandleClosingAsync(e.CloseReason).GetAwaiter().GetResult();
                SaveCurrentSettings();
                _simApi.Cleanup();
            }
            catch { }
        }
    }
}
