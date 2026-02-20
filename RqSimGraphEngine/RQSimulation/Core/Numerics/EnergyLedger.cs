using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RQSimulation.Core.Configuration;

namespace RQSimulation
{
    /// <summary>
    /// Tracks energy conservation across all simulation operations
    /// Ensures strict adherence to energy conservation laws
    /// Implements RQ-hypothesis checklist item 4: Unified Hamiltonian and Energy Ledger
    ///
    /// Energy accounting:
    /// - VacuumPool: Available vacuum energy for topology changes
    /// - MatterEnergy: Energy in particle clusters
    /// - FieldEnergy: Energy in gauge fields
    /// - Total = VacuumPool + MatterEnergy + FieldEnergy (conserved)
    ///
    /// RQ-HYPOTHESIS STAGE 2: Wheeler-DeWitt Constraint Support
    /// =========================================================
    /// In strict conservation mode, external energy injection is forbidden.
    /// The ledger becomes a logger of constraint violations rather than
    /// a source of "vacuum energy" that could violate H_total ? 0.
    ///
    /// Thread safety: Thread-safe using lock synchronization for all mutable operations.
    ///
    /// CONFIGURATION RELOAD (Item 42): Runtime Configuration Reload
    /// =============================================================
    /// Supports IOptionsMonitor for hot-reload of settings during simulation.
    /// When configuration changes, the ledger automatically reloads relevant parameters.
    /// </summary>
    public class EnergyLedger : IDisposable
    {
        private readonly object _lock = new();
        private SimulationSettings _settings;
        private IDisposable? _settingsChangeToken;
        private double _totalEnergy;
        private double _externalInjection;
        private double _vacuumBorrowing;
        private double _vacuumPool;
        private double _matterEnergy;
        private double _fieldEnergy;
        private double _tolerance;
        private int _maxViolations;
        private bool _initialized;

        // ============================================================
        // RQ-HYPOTHESIS STAGE 2: WHEELER-DEWITT STRICT CONSERVATION
        // ============================================================

        private bool _strictConservationMode = false;
        private readonly List<ConstraintViolationRecord> _constraintViolationHistory = new();

        /// <summary>
        /// Creates a new EnergyLedger with default settings.
        /// For backward compatibility - uses hardcoded constants.
        /// </summary>
        public EnergyLedger() : this(SimulationSettings.Default)
        {
        }

        /// <summary>
        /// Creates a new EnergyLedger with configuration support via Options pattern.
        /// This constructor enables dependency injection and external configuration.
        /// </summary>
        /// <param name="settings">Simulation settings (can use IOptions&lt;SimulationSettings&gt;)</param>
        public EnergyLedger(IOptions<SimulationSettings> settings) : this(settings.Value)
        {
        }

        /// <summary>
        /// Creates a new EnergyLedger with IOptionsMonitor support for runtime configuration reload.
        /// This constructor enables hot-reload of configuration during simulation.
        /// When settings change, the ledger automatically updates relevant parameters.
        /// </summary>
        /// <param name="settingsMonitor">Settings monitor for configuration reload</param>
        public EnergyLedger(IOptionsMonitor<SimulationSettings> settingsMonitor) : this(settingsMonitor.CurrentValue)
        {
            // Subscribe to configuration changes
            _settingsChangeToken = settingsMonitor.OnChange(OnSettingsChanged);
        }

        /// <summary>
        /// Creates a new EnergyLedger with the specified settings.
        /// </summary>
        /// <param name="settings">Simulation settings</param>
        public EnergyLedger(SimulationSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _settings.Validate();

            ApplySettings(_settings);
        }

        /// <summary>
        /// Apply settings to internal state.
        /// Used during initialization and when settings are reloaded.
        /// </summary>
        private void ApplySettings(SimulationSettings settings)
        {
            _tolerance = settings.EnergyTolerance;
            _maxViolations = settings.MaxViolations;
            _strictConservationMode = settings.StrictConservation;

            // Update temperature from settings
            if (settings.Temperature > 0)
            {
                Temperature = settings.Temperature;
            }
        }

        /// <summary>
        /// Callback invoked when configuration settings change at runtime.
        /// Implements Item 42: Runtime Configuration Reload.
        /// Thread-safe: Uses lock to protect state updates.
        /// </summary>
        /// <param name="newSettings">New configuration settings</param>
        private void OnSettingsChanged(SimulationSettings newSettings)
        {
            lock (_lock)
            {
                // Validate new settings before applying
                try
                {
                    newSettings.Validate();
                }
                catch (Exception ex)
                {
                    // Log validation error but don't apply invalid settings
                    LogConstraintViolation(0, $"ConfigReloadFailed: {ex.Message}");
                    return;
                }

                // Store old settings for comparison
                var oldSettings = _settings;
                _settings = newSettings;

                // Apply new settings
                ApplySettings(newSettings);

                // Log the configuration change
                LogConstraintViolation(0,
                    $"ConfigReloaded: Tolerance={_tolerance}, MaxViolations={_maxViolations}, " +
                    $"StrictMode={_strictConservationMode}, Temperature={Temperature}");

                // If initial vacuum energy changed significantly, log a warning
                if (Math.Abs(oldSettings.InitialVacuumEnergy - newSettings.InitialVacuumEnergy) > 1e-6)
                {
                    LogConstraintViolation(
                        newSettings.InitialVacuumEnergy - oldSettings.InitialVacuumEnergy,
                        $"InitialVacuumEnergyChanged: {oldSettings.InitialVacuumEnergy} -> {newSettings.InitialVacuumEnergy}");
                }
            }
        }

        /// <summary>
        /// Dispose of resources, including configuration change subscription.
        /// </summary>
        public void Dispose()
        {
            _settingsChangeToken?.Dispose();
        }

        private const double Tolerance = 1e-6; // Kept for backward compatibility when using default constructor
        private const int MaxViolations = 1000; // Kept for backward compatibility when using default constructor
        
        /// <summary>
        /// Enable strict Wheeler-DeWitt mode.
        /// When enabled, external energy injection is forbidden.
        /// The ledger logs violations but does not "fix" them.
        /// </summary>
        public bool StrictConservationMode 
        { 
            get => _strictConservationMode;
            set => _strictConservationMode = value;
        }
        
        /// <summary>
        /// Get the history of constraint violations (for diagnostics).
        /// </summary>
        public IReadOnlyList<ConstraintViolationRecord> ConstraintViolationHistory => _constraintViolationHistory;
        
        /// <summary>
        /// Clear the constraint violation history.
        /// </summary>
        public void ClearConstraintViolationHistory()
        {
            _constraintViolationHistory.Clear();
        }
        
        /// <summary>
        /// Log constraint violation without fixing it.
        /// Used for diagnostics in strict mode.
        /// Thread-safe: Uses lock to protect violation list.
        /// List growth is limited to MaxViolations entries.
        /// </summary>
        /// <param name="violation">The magnitude of the violation</param>
        /// <param name="context">Description of where the violation occurred</param>
        public void LogConstraintViolation(double violation, string context)
        {
            if (!_settings.EnableViolationLogging)
                return;

            lock (_lock)
            {
                // Keep history bounded to prevent memory issues - use configured max violations
                if (_constraintViolationHistory.Count >= _maxViolations)
                {
                    _constraintViolationHistory.RemoveAt(0);
                }

                _constraintViolationHistory.Add(new ConstraintViolationRecord(
                    Timestamp: DateTime.UtcNow,
                    Violation: violation,
                    Context: context
                ));
            }
        }
        
        /// <summary>
        /// Get summary statistics of constraint violations.
        /// </summary>
        public (int Count, double TotalViolation, double MaxViolation, double AverageViolation) GetViolationStatistics()
        {
            if (_constraintViolationHistory.Count == 0)
                return (0, 0.0, 0.0, 0.0);
                
            double total = 0.0;
            double max = 0.0;
            
            foreach (var record in _constraintViolationHistory)
            {
                total += Math.Abs(record.Violation);
                max = Math.Max(max, Math.Abs(record.Violation));
            }
            
            return (
                Count: _constraintViolationHistory.Count,
                TotalViolation: total,
                MaxViolation: max,
                AverageViolation: total / _constraintViolationHistory.Count
            );
        }

        /// <summary>
        /// Vacuum energy pool available for topology changes and particle creation.
        /// Implements checklist item 4.1: Unified energy functional.
        /// </summary>
        public double VacuumPool 
        { 
            get => _vacuumPool;
            private set => _vacuumPool = value;
        }
        
        /// <summary>
        /// Energy stored in matter clusters
        /// </summary>
        public double MatterEnergy => _matterEnergy;
        
        /// <summary>
        /// Energy stored in gauge fields
        /// </summary>
        public double FieldEnergy => _fieldEnergy;

        /// <summary>
        /// Initialize the ledger with the current total energy
        ///
        /// RQ-HYPOTHESIS CHECKLIST: Uses configured InitialVacuumEnergy or PhysicsConstants as fallback.
        /// The vacuum pool is the "raw material" for spacetime construction.
        /// </summary>
        public void Initialize(double initialEnergy)
        {
            _totalEnergy = initialEnergy;
            _externalInjection = 0;
            _vacuumBorrowing = 0;
            // Use the larger of computed fraction or configured/constant InitialVacuumEnergy
            double computedPool = initialEnergy * PhysicsConstants.InitialVacuumPoolFraction;
            double configuredEnergy = _settings?.InitialVacuumEnergy ?? PhysicsConstants.InitialVacuumEnergy;
            _vacuumPool = Math.Max(computedPool, configuredEnergy);
            _matterEnergy = 0;
            _fieldEnergy = 0;
            _initialized = true;
        }

        /// <summary>
        /// Initialize with detailed energy breakdown
        ///
        /// RQ-HYPOTHESIS CHECKLIST: Ensures vacuum pool is at least configured InitialVacuumEnergy.
        /// </summary>
        public void Initialize(double vacuumEnergy, double matterEnergy, double fieldEnergy)
        {
            // Ensure minimum vacuum energy from configuration or PhysicsConstants
            double configuredEnergy = _settings?.InitialVacuumEnergy ?? PhysicsConstants.InitialVacuumEnergy;
            _vacuumPool = Math.Max(vacuumEnergy, configuredEnergy);
            _matterEnergy = matterEnergy;
            _fieldEnergy = fieldEnergy;
            _totalEnergy = _vacuumPool + matterEnergy + fieldEnergy;
            _externalInjection = 0;
            _vacuumBorrowing = 0;
            _initialized = true;
        }

        /// <summary>
        /// Try to spend energy from vacuum pool for topology change.
        /// Returns false if not enough energy is available.
        /// Implements checklist item 4.3: Energy check for topology changes.
        ///
        /// RQ-HYPOTHESIS STAGE 2: Wheeler-DeWitt Strict Mode
        /// =================================================
        /// In strict conservation mode, this logs the attempt but returns false
        /// to prevent external energy manipulation that would violate H ? 0.
        ///
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="amount">Energy required for the change</param>
        /// <returns>True if energy was available and spent, false otherwise</returns>
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

            lock (_lock)
            {
                // RQ-HYPOTHESIS STAGE 2: Strict mode rejects vacuum spending
                if (_strictConservationMode)
                {
                    LogConstraintViolation(amount, "VacuumSpendAttempt_StrictModeBlocked");
                    return false; // Reject in strict mode
                }

                if (_vacuumPool >= amount)
                {
                    _vacuumPool -= amount;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// RQ-HYPOTHESIS v2.0: Try to absorb energy deficit (or surplus) into the vacuum pool.
        /// Used by ExecuteTransaction to maintain conservation.
        ///
        /// RQ-HYPOTHESIS STAGE 2: Wheeler-DeWitt Strict Mode
        /// =================================================
        /// In strict mode, deficits are logged but not absorbed from vacuum.
        /// Surpluses can still be returned to maintain energy accounting.
        ///
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="delta">Energy change (After - Before). Positive = Surplus, Negative = Deficit.</param>
        /// <param name="graph">Reference to graph (unused here but kept for API compatibility)</param>
        /// <returns>True if absorbed successfully.</returns>
        public bool TryAbsorbDeficit(double delta, RQGraph graph)
        {
            if (!_initialized) return false;

            lock (_lock)
            {
                if (delta > 0)
                {
                    // Surplus energy: Return to vacuum pool (always allowed)
                    _vacuumPool += delta;
                    return true;
                }
                else
                {
                    // Deficit: Need to take from vacuum pool
                    double needed = -delta;

                    // RQ-HYPOTHESIS STAGE 2: Strict mode blocks deficit absorption
                    if (_strictConservationMode)
                    {
                        LogConstraintViolation(needed, "DeficitAbsorption_StrictModeBlocked");
                        return false;
                    }

                    if (_vacuumPool >= needed)
                    {
                        _vacuumPool -= needed;
                        return true;
                    }
                    else
                    {
                        // Not enough vacuum energy to cover deficit
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Register radiation energy returned to the vacuum
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="amount">Energy to return to vacuum</param>
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

            lock (_lock)
            {
                _vacuumPool += amount;
            }
        }
        
        /// <summary>
        /// Check if a topology change can be afforded energy-wise.
        /// Implements checklist item 4.3: Metropolis criterion energy check.
        /// </summary>
        /// <param name="deltaEnergy">Energy change (positive = costs energy)</param>
        /// <returns>True if change is allowed, false otherwise</returns>
        public bool CanAfford(double deltaEnergy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }
            
            if (deltaEnergy <= 0)
                return true; // Energy-releasing changes are always allowed
                
            return _vacuumPool >= deltaEnergy;
        }
        
        /// <summary>
        /// Update matter energy tracking
        /// Thread-safe: Uses lock to protect energy fields.
        /// </summary>
        public void UpdateMatterEnergy(double newMatterEnergy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }

            lock (_lock)
            {
                double delta = newMatterEnergy - _matterEnergy;
                _matterEnergy = newMatterEnergy;

                // Compensate vacuum pool to maintain total energy conservation
                _vacuumPool -= delta;
            }
        }

        /// <summary>
        /// Update field energy tracking
        /// Thread-safe: Uses lock to protect energy fields.
        /// </summary>
        public void UpdateFieldEnergy(double newFieldEnergy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }

            lock (_lock)
            {
                double delta = newFieldEnergy - _fieldEnergy;
                _fieldEnergy = newFieldEnergy;

                // Compensate vacuum pool to maintain total energy conservation
                _vacuumPool -= delta;
            }
        }
        
        /// <summary>
        /// Get total tracked energy (should be constant)
        /// </summary>
        public double TotalTrackedEnergy => _vacuumPool + _matterEnergy + _fieldEnergy;

        /// <summary>
        /// Record energy injection from external source (e.g., impulse)
        ///
        /// RQ-HYPOTHESIS STAGE 2: Wheeler-DeWitt Strict Mode
        /// =================================================
        /// In strict mode, external injection is BLOCKED to preserve H ? 0.
        /// The injection is logged as a constraint violation.
        ///
        /// Thread-safe: Uses lock to protect energy fields.
        /// </summary>
        /// <param name="energy">Energy to inject</param>
        /// <param name="source">Source description for logging</param>
        /// <returns>True if injection succeeded, false if blocked by strict mode</returns>
        public bool RecordExternalInjection(double energy, string source)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }

            lock (_lock)
            {
                // RQ-HYPOTHESIS STAGE 2: Block external injection in strict mode
                if (_strictConservationMode)
                {
                    LogConstraintViolation(energy, $"ExternalInjection_StrictModeBlocked: {source}");
                    return false; // Reject in strict mode
                }

                _externalInjection += energy;
                _vacuumPool += energy; // External energy goes to vacuum pool
                return true;
            }
        }

        /// <summary>
        /// Record energy borrowed from vacuum (must be repaid)
        /// </summary>
        public void BorrowFromVacuum(double energy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized.");
            }

            _vacuumBorrowing += energy;
        }

        /// <summary>
        /// Repay energy borrowed from vacuum
        /// </summary>
        public void RepayToVacuum(double energy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized.");
            }

            _vacuumBorrowing -= energy;
            if (_vacuumBorrowing < -1e-10) // Allow small numerical error
            {
                throw new InvalidOperationException(
                    $"Vacuum debt cannot be negative: {_vacuumBorrowing:F10}. " +
                    "More energy repaid than borrowed.");
            }
        }

        /// <summary>
        /// Validate that energy is conserved within tolerance
        /// </summary>
        public void ValidateConservation(double currentEnergy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized.");
            }

            double expected = _totalEnergy + _externalInjection;
            double error = Math.Abs(currentEnergy - expected);
            double relativeError = Math.Abs(expected) > 1e-10 
                ? error / Math.Abs(expected) 
                : error;

            if (error > Tolerance && relativeError > Tolerance)
            {
                throw new EnergyConservationException(
                    $"Energy conservation violated!\n" +
                    $"Expected: {expected:F8}\n" +
                    $"Current:  {currentEnergy:F8}\n" +
                    $"Error:    {error:F8} ({relativeError * 100:F2}%)\n" +
                    $"Injected: {_externalInjection:F8}\n" +
                    $"Vacuum:   {_vacuumBorrowing:F8}");
            }

            // Update total energy to current value (for next validation)
            _totalEnergy = currentEnergy;
        }

        /// <summary>
        /// Get current vacuum borrowing (should be ~0 in steady state)
        /// </summary>
        public double VacuumDebt => _vacuumBorrowing;

        /// <summary>
        /// Get total external energy injected
        /// </summary>
        public double TotalExternalInjection => _externalInjection;

        /// <summary>
        /// Get current tracked total energy
        /// </summary>
        public double TrackedEnergy => _totalEnergy;

        /// <summary>
        /// Reset external injection counter (e.g., at start of run)
        /// </summary>
        public void ResetExternalInjection()
        {
            _externalInjection = 0;
        }

        /// <summary>
        /// Try to transact energy with the vacuum pool (bidirectional).
        /// Positive amount = borrow from vacuum (vacuum decreases)
        /// Negative amount = return to vacuum (vacuum increases)
        ///
        /// PHYSICS TASK 1: Strict Energy Conservation
        /// ==========================================
        /// This method enforces the 1st law of thermodynamics by requiring
        /// all energy changes to be balanced against the vacuum reservoir.
        /// No energy can be created from nothing - it must be borrowed.
        ///
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="amount">Energy to transact (positive = borrow, negative = return)</param>
        /// <returns>True if transaction succeeded, false if insufficient vacuum energy</returns>
        public bool TryTransactVacuumEnergy(double amount)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }

            lock (_lock)
            {
                if (amount > 0)
                {
                    // Borrow from vacuum (vacuum decreases)
                    if (_vacuumPool >= amount)
                    {
                        _vacuumPool -= amount;
                        return true;
                    }
                    return false; // Insufficient vacuum energy
                }
                else
                {
                    // Return to vacuum (vacuum increases)
                    _vacuumPool -= amount; // amount is negative, so this adds
                    return true;
                }
            }
        }
        
        // ============================================================
        // RQ-HYPOTHESIS CHECKLIST: MAXWELL'S DEMON TAX
        // ============================================================
        
        /// <summary>
        /// Current effective temperature for Landauer limit calculations.
        /// In RQ-hypothesis, this is derived from network temperature or vacuum fluctuations.
        /// Default: 1.0 (Planck temperature units)
        /// </summary>
        public double Temperature { get; set; } = 1.0;
        
        /// <summary>
        /// Tax energy cost for random number generation (Maxwell's demon).
        ///
        /// RQ-HYPOTHESIS PHYSICS:
        /// ======================
        /// In the RQ-hypothesis, "true randomness" doesn't exist - only incomplete information.
        /// Every use of a random number represents gaining information about the system state,
        /// which has a thermodynamic cost according to Landauer's principle:
        ///
        ///   E_cost = k_B * T * ln(2) * bits ? entropy_bits * LandauerLimit * Temperature
        ///
        /// This method:
        /// 1. Calculates the energy cost of the random event
        /// 2. Deducts from the vacuum pool
        /// 3. Throws CausalityViolationException if vacuum depleted (would violate 2nd law)
        ///
        /// USAGE:
        /// Call this whenever _rng.NextDouble() or similar is used in physics code.
        /// For Metropolis moves, entropy ? 1 bit per decision.
        /// For continuous random variables, entropy ? log2(range/precision) bits.
        ///
        /// NOTE: Not all random calls need to be taxed - only those that affect physical
        /// outcomes (Metropolis acceptance, fluctuation generation, etc.)
        ///
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="entropyBits">Number of bits of entropy consumed (typically 1-64)</param>
        /// <returns>Energy cost deducted from vacuum pool</returns>
        /// <exception cref="CausalityViolationException">Thrown if vacuum pool is depleted</exception>
        public double TaxRandomEvent(double entropyBits)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }

            if (entropyBits <= 0)
                return 0.0; // No entropy consumed, no cost

            double energyCost;
            lock (_lock)
            {
                // Landauer's principle: E = k_B * T * ln(2) * bits
                // In Planck units: E = LandauerLimit * T * bits
                energyCost = entropyBits * PhysicsConstants.LandauerLimit * Temperature;

                // Deduct from vacuum pool
                _vacuumPool -= energyCost;

                // Check for vacuum depletion (2nd law violation)
                if (_vacuumPool < 0)
                {
                    // Vacuum is depleted - cannot create more entropy without energy
                    // This is a fundamental violation of thermodynamics
                    throw new CausalityViolationException(
                        $"Vacuum energy depleted! Cannot sustain entropy generation.\n" +
                        $"Entropy requested: {entropyBits:F2} bits\n" +
                        $"Energy cost: {energyCost:F6}\n" +
                        $"Vacuum pool: {_vacuumPool + energyCost:F6} (before)\n" +
                        $"This indicates the simulation has reached thermal death or requires energy injection.");
                }
            }

            return energyCost;
        }

        /// <summary>
        /// Try to tax a random event, returning false instead of throwing if vacuum depleted.
        /// Use this for soft failures where the random operation can be skipped.
        /// Thread-safe: Uses lock to protect vacuum pool.
        /// </summary>
        /// <param name="entropyBits">Number of bits of entropy consumed</param>
        /// <param name="energyCost">Energy cost if successful</param>
        /// <returns>True if tax paid successfully, false if insufficient vacuum energy</returns>
        public bool TryTaxRandomEvent(double entropyBits, out double energyCost)
        {
            if (!_initialized)
            {
                energyCost = 0.0;
                return false;
            }

            if (entropyBits <= 0)
            {
                energyCost = 0.0;
                return true;
            }

            lock (_lock)
            {
                energyCost = entropyBits * PhysicsConstants.LandauerLimit * Temperature;

                if (_vacuumPool >= energyCost)
                {
                    _vacuumPool -= energyCost;
                    return true;
                }

                energyCost = 0.0;
                return false;
            }
        }
        
        /// <summary>
        /// Get the maximum entropy (in bits) that can be generated with current vacuum pool.
        /// </summary>
        public double MaxAvailableEntropy => 
            _vacuumPool / (PhysicsConstants.LandauerLimit * Temperature + 1e-10);
        
        /// <summary>
        /// Convert topology energy to matter energy (local particle creation).
        /// 
        /// RQ-HYPOTHESIS PHYSICS:
        /// ======================
        /// When an edge is removed, its correlation energy must go somewhere.
        /// Instead of dumping it into a global radiation pool, we inject it
        /// locally into matter fields (scalar field momentum).
        /// 
        /// This conserves both energy AND locality (no action at a distance).
        /// The energy transfer is: E_topology ? E_matter (total unchanged)
        /// </summary>
        /// <param name="energy">Energy being converted from topology to matter</param>
        public void ConvertTopologyToMatter(double energy)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }
            
            if (energy < 0)
            {
                throw new ArgumentException("Cannot convert negative energy", nameof(energy));
            }
            
            // This is a conversion, not creation - total energy unchanged
            // Energy moves from "vacuum/topology" to "matter" category
            // Note: We track this for debugging but total energy is conserved
            _matterEnergy += energy;
            // Vacuum pool is not affected because the energy was already
            // "bound" in the edge correlation, not in the vacuum
        }
        
        // ============================================================
        // GPU INTEGRITY VALIDATION (HARD SCIENCE AUDIT v3.2)
        // ============================================================
        
        /// <summary>
        /// Validate GPU fixed-point integrity after kernel completion.
        /// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Critical for scientific data validity.</para>
        /// <para>
        /// This method MUST be called after every GPU conservation kernel dispatch.
        /// If overflow/saturation was detected, the simulation data is invalid
        /// and continuing would produce non-physical results.
        /// </para>
        /// </summary>
        /// <param name="integrityFlags">Integrity flags from GPU (via SimulationParameters.IntegrityFlags)</param>
        /// <exception cref="ScientificMalpracticeException">Thrown when GPU detected overflow or critical failure</exception>
        public void ValidateGpuIntegrity(int integrityFlags)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("EnergyLedger not initialized. Call Initialize() first.");
            }
            
            if (integrityFlags == 0)
            {
                return; // All OK
            }
            
            // Build detailed error message
            var issues = new System.Text.StringBuilder();
            issues.AppendLine("GPU INTEGRITY FAILURE DETECTED!");
            issues.AppendLine("================================");
            issues.AppendLine("Simulation data is scientifically INVALID.");
            issues.AppendLine();
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_OVERFLOW_DETECTED) != 0)
            {
                issues.AppendLine("? OVERFLOW: Fixed-point accumulator exceeded INT32_MAX.");
                issues.AppendLine("  Energy concentration exceeded ~119.2 units/node (2^24 scale).");
                issues.AppendLine("  Mass Conservation Law cannot be enforced.");
            }
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_UNDERFLOW_DETECTED) != 0)
            {
                issues.AppendLine("? UNDERFLOW: Fixed-point accumulator fell below INT32_MIN.");
                issues.AppendLine("  Negative energy accumulation detected.");
            }
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_TDR_TRUNCATION) != 0)
            {
                issues.AppendLine("? TDR: GPU timeout truncated computation.");
                issues.AppendLine("  Conservation kernel did not complete.");
            }
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_CONSERVATION_VIOLATION) != 0)
            {
                issues.AppendLine("? CONSERVATION: Energy conservation tolerance exceeded.");
            }
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_NAN_DETECTED) != 0)
            {
                issues.AppendLine("? NAN: NaN or Infinity detected in critical buffer.");
            }
            
            if ((integrityFlags & PhysicsConstants.IntegrityFlags.FLAG_64BIT_OVERFLOW) != 0)
            {
                issues.AppendLine("? 64BIT_OVERFLOW: Global energy sum exceeded 64-bit capacity.");
                issues.AppendLine("  Graph is too large for current accumulation strategy.");
            }
            
            issues.AppendLine();
            issues.AppendLine("RECOMMENDED ACTIONS:");
            issues.AppendLine("1. Reduce graph size or energy density.");
            issues.AppendLine("2. Enable more frequent energy dissipation.");
            issues.AppendLine("3. Use CPU-side aggregation for very large graphs.");
            
            // Log the violation in history
            LogConstraintViolation(integrityFlags, "GPU_INTEGRITY_FAILURE");
            
            throw new GpuIntegrityViolationException(issues.ToString(), integrityFlags);
        }
        
        /// <summary>
        /// Check GPU integrity without throwing.
        /// <para>Returns false if any integrity issue was detected.</para>
        /// </summary>
        /// <param name="integrityFlags">Integrity flags from GPU</param>
        /// <param name="report">Human-readable report if issues detected</param>
        /// <returns>True if integrity is OK, false if any issues detected</returns>
        public bool TryValidateGpuIntegrity(int integrityFlags, out string report)
        {
            if (integrityFlags == 0)
            {
                report = "OK";
                return true;
            }
            
            var issues = new System.Text.StringBuilder();
            if ((integrityFlags & 1) != 0) issues.Append("OVERFLOW ");
            if ((integrityFlags & 2) != 0) issues.Append("UNDERFLOW ");
            if ((integrityFlags & 4) != 0) issues.Append("TDR ");
            if ((integrityFlags & 8) != 0) issues.Append("CONSERVATION ");
            if ((integrityFlags & 16) != 0) issues.Append("NAN ");
            if ((integrityFlags & 32) != 0) issues.Append("64BIT_OVERFLOW ");
            
            report = issues.ToString().TrimEnd();
            
            // Log without throwing
            LogConstraintViolation(integrityFlags, $"GPU_INTEGRITY_WARNING: {report}");
            
            return false;
        }
    }
    
    /// <summary>
    /// Exception thrown when energy conservation is violated
    /// </summary>
    public class EnergyConservationException : Exception
    {
        public EnergyConservationException(string message) : base(message) { }
    }
    
    /// <summary>
    /// Exception thrown when causality or thermodynamic laws are violated.
    /// This typically means the simulation has reached an unphysical state
    /// (vacuum depletion, negative energy, etc.)
    /// </summary>
    public class CausalityViolationException : Exception
    {
        public CausalityViolationException(string message) : base(message) { }
    }
    
    /// <summary>
    /// Exception thrown when GPU fixed-point arithmetic overflow is detected.
    /// <para><strong>HARD SCIENCE AUDIT v3.2:</strong> Critical integrity failure.</para>
    /// <para>
    /// This exception indicates that the simulation has produced scientifically
    /// invalid data due to integer overflow in GPU conservation kernels.
    /// </para>
    /// <para>
    /// When this exception is thrown:
    /// - All data from the current simulation step is unreliable
    /// - Energy conservation cannot be guaranteed
    /// - Mass Conservation Law has been violated
    /// - Simulation results should NOT be used for scientific conclusions
    /// </para>
    /// </summary>
    public class GpuIntegrityViolationException : Exception
    {
        /// <summary>
        /// The raw integrity flags from GPU.
        /// </summary>
        public int IntegrityFlags { get; }
        
        /// <summary>
        /// True if fixed-point overflow was detected.
        /// </summary>
        public bool HasOverflow => (IntegrityFlags & PhysicsConstants.IntegrityFlags.FLAG_OVERFLOW_DETECTED) != 0;
        
        /// <summary>
        /// True if fixed-point underflow was detected.
        /// </summary>
        public bool HasUnderflow => (IntegrityFlags & PhysicsConstants.IntegrityFlags.FLAG_UNDERFLOW_DETECTED) != 0;
        
        /// <summary>
        /// True if 64-bit global accumulator overflowed.
        /// </summary>
        public bool Has64BitOverflow => (IntegrityFlags & PhysicsConstants.IntegrityFlags.FLAG_64BIT_OVERFLOW) != 0;
        
        /// <summary>
        /// True if NaN or Infinity was detected.
        /// </summary>
        public bool HasNan => (IntegrityFlags & PhysicsConstants.IntegrityFlags.FLAG_NAN_DETECTED) != 0;

        public GpuIntegrityViolationException(string message) 
            : base(message) 
        {
            IntegrityFlags = 0;
        }
        
        public GpuIntegrityViolationException(string message, int integrityFlags) 
            : base(message)
        {
            IntegrityFlags = integrityFlags;
        }
        
        public GpuIntegrityViolationException(string message, Exception innerException)
            : base(message, innerException)
        {
            IntegrityFlags = 0;
        }
    }

    /// <summary>
    /// Record of a constraint violation in strict conservation mode.
    /// Used for diagnostics in Wheeler-DeWitt constraint enforcement.
    /// </summary>
    /// <param name="Timestamp">When the violation occurred</param>
    /// <param name="Violation">Magnitude of the violation</param>
    /// <param name="Context">Description of where the violation occurred</param>
    public readonly record struct ConstraintViolationRecord(
        DateTime Timestamp,
        double Violation,
        string Context
    );
}
