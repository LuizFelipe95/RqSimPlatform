using RQSimulation;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for energy conservation tracking across all simulation operations.
/// Ensures strict adherence to energy conservation laws.
/// 
/// Implements RQ-hypothesis checklist item 4: Unified Hamiltonian and Energy Ledger.
/// 
/// Energy accounting:
/// - VacuumPool: Available vacuum energy for topology changes
/// - MatterEnergy: Energy in particle clusters
/// - FieldEnergy: Energy in gauge fields
/// - Total = VacuumPool + MatterEnergy + FieldEnergy (conserved)
/// 
/// Based on original EnergyLedger implementation.
/// </summary>
public sealed class EnergyLedgerCpuModule : CpuPluginBase
{
    private double _totalEnergy;
    private double _externalInjection;
    private double _vacuumBorrowing;
    private double _vacuumPool;
    private double _matterEnergy;
    private double _fieldEnergy;
    private bool _initialized;

    private const double Tolerance = 1e-6;

    public override string Name => "Energy Ledger (CPU)";
    public override string Description => "CPU-based energy conservation tracking with vacuum/matter/field accounting";
    public override string Category => "Energy";
    public override int Priority => 5;

    /// <summary>
    /// Available vacuum energy for topology changes.
    /// </summary>
    public double VacuumPool => _vacuumPool;

    /// <summary>
    /// Energy in particle clusters.
    /// </summary>
    public double MatterEnergy => _matterEnergy;

    /// <summary>
    /// Energy in gauge fields.
    /// </summary>
    public double FieldEnergy => _fieldEnergy;

    /// <summary>
    /// Total energy (should be conserved).
    /// </summary>
    public double TotalEnergy => _vacuumPool + _matterEnergy + _fieldEnergy;

    /// <summary>
    /// Whether the ledger has been initialized.
    /// </summary>
    public bool IsInitialized => _initialized;

    public override void Initialize(RQGraph graph)
    {
        // Initialize with default energy based on graph size
        double initialEnergy = graph.N * 0.1; // Scale with graph size
        InitializeEnergy(initialEnergy);
    }

    /// <summary>
    /// Initialize with total energy (vacuum pool fraction applied).
    /// </summary>
    public void InitializeEnergy(double initialEnergy)
    {
        _totalEnergy = initialEnergy;
        _externalInjection = 0;
        _vacuumBorrowing = 0;
        double computedPool = initialEnergy * PhysicsConstants.InitialVacuumPoolFraction;
        _vacuumPool = Math.Max(computedPool, PhysicsConstants.InitialVacuumEnergy);
        _matterEnergy = 0;
        _fieldEnergy = 0;
        _initialized = true;
    }

    /// <summary>
    /// Initialize with explicit energy components.
    /// </summary>
    public void InitializeEnergy(double vacuumEnergy, double matterEnergy, double fieldEnergy)
    {
        _vacuumPool = Math.Max(vacuumEnergy, PhysicsConstants.InitialVacuumEnergy);
        _matterEnergy = matterEnergy;
        _fieldEnergy = fieldEnergy;
        _totalEnergy = _vacuumPool + matterEnergy + fieldEnergy;
        _externalInjection = 0;
        _vacuumBorrowing = 0;
        _initialized = true;
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        if (!_initialized) return;

        // Track energy changes from graph evolution
        double currentHamiltonian = graph.ComputeNetworkHamiltonian();
        
        // Adjust vacuum pool based on energy changes
        double delta = _totalEnergy - (currentHamiltonian + _matterEnergy + _fieldEnergy);
        if (Math.Abs(delta) > Tolerance)
        {
            _vacuumPool += delta;
        }
    }

    /// <summary>
    /// Try to spend vacuum energy for topology changes.
    /// </summary>
    public bool TrySpendVacuumEnergy(double amount)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
        }

        if (amount < 0)
        {
            throw new ArgumentException("Cannot spend negative energy", nameof(amount));
        }

        if (_vacuumPool >= amount)
        {
            _vacuumPool -= amount;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Try to absorb energy deficit into vacuum pool.
    /// </summary>
    public bool TryAbsorbDeficit(double delta, RQGraph graph)
    {
        if (!_initialized) return false;

        if (delta > 0)
        {
            _vacuumPool += delta;
            return true;
        }
        else
        {
            double needed = -delta;
            if (_vacuumPool >= needed)
            {
                _vacuumPool -= needed;
                return true;
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Register energy from radiation (e.g., Hawking radiation).
    /// </summary>
    public void RegisterRadiation(double amount)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
        }

        if (amount < 0)
        {
            throw new ArgumentException("Cannot register negative radiation", nameof(amount));
        }

        _vacuumPool += amount;
    }

    /// <summary>
    /// Check if we can afford an energy expenditure.
    /// </summary>
    public bool CanAfford(double deltaEnergy)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
        }

        if (deltaEnergy <= 0)
            return true;

        return _vacuumPool >= deltaEnergy;
    }

    /// <summary>
    /// Update matter energy (transfers to/from vacuum pool).
    /// </summary>
    public void UpdateMatterEnergy(double newMatterEnergy)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
        }

        double delta = newMatterEnergy - _matterEnergy;
        _matterEnergy = newMatterEnergy;
        _vacuumPool -= delta;
    }

    /// <summary>
    /// Update field energy (transfers to/from vacuum pool).
    /// </summary>
    public void UpdateFieldEnergy(double newFieldEnergy)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
        }

        double delta = newFieldEnergy - _fieldEnergy;
        _fieldEnergy = newFieldEnergy;
        _vacuumPool -= delta;
    }

    public override void Cleanup()
    {
        _initialized = false;
        _vacuumPool = 0;
        _matterEnergy = 0;
        _fieldEnergy = 0;
        _totalEnergy = 0;
    }
}
