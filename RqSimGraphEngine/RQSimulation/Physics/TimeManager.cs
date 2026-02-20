using System;

namespace RQSimulation.Physics
{
    /// <summary>
    /// Provides unified relational time stepping for RQ-compliant simulation.
    /// 
    /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Pure Relational Time
    /// =====================================================
    /// This class resolves the "Hybrid Time" problem by providing:
    /// 1. Relational time steps (dTau) computed from internal clock subsystem
    /// 2. Node-level proper time with lapse function support
    /// 3. Separation of time scales (topology vs quantum evolution)
    /// 
    /// USAGE:
    /// - For global synchronous mode: Use ComputeRelationalTimeStep()
    /// - For per-node async mode: Use ComputeNodeProperTimeStep()
    /// - For event-driven engine: Use ComputeActionQuantum()
    /// </summary>
    public static class TimeManager
    {
        // ================================================================
        // CONSTANTS
        // ================================================================

        /// <summary>Minimum allowed time step (prevents zero-time updates)</summary>
        public const double MinTimeStep = 1e-6;

        /// <summary>Maximum allowed time step (prevents instability)</summary>
        public const double MaxTimeStep = 0.1;

        /// <summary>
        /// Ratio of topology update interval to quantum evolution steps.
        /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Prevents Zeno effect.
        /// Topology changes are "slow" (classical) vs "fast" (quantum).
        /// </summary>
        public const int TopologyToQuantumTimeScale = 10;

        /// <summary>
        /// Action quantum threshold for event-driven updates.
        /// Node updates when accumulated action exceeds this threshold.
        /// </summary>
        public const double ActionQuantumThreshold = 0.01;

        // ================================================================
        // RELATIONAL TIME STEP (GLOBAL)
        // ================================================================

        /// <summary>
        /// Computes the relational time step dTau based on a set of
        /// clock amplitudes. The result is the norm of the difference
        /// between successive clock states. The returned value is
        /// clamped to a reasonable range [min, max] to avoid unstable
        /// integration.
        /// 
        /// RQ-HYPOTHESIS: This implements Page-Wootters mechanism where
        /// time emerges from quantum correlations of a clock subsystem.
        /// </summary>
        /// <param name="previousClockState">State of the clock at the previous step.</param>
        /// <param name="currentClockState">State of the clock at the current step.</param>
        /// <param name="minStep">Minimum allowed time step.</param>
        /// <param name="maxStep">Maximum allowed time step.</param>
        /// <returns>The relational time increment dTau.</returns>
        public static double ComputeRelationalTimeStep(
            double[] previousClockState,
            double[] currentClockState,
            double minStep = 1e-3,
            double maxStep = 1e-1)
        {
            ArgumentNullException.ThrowIfNull(previousClockState);
            ArgumentNullException.ThrowIfNull(currentClockState);

            if (previousClockState.Length != currentClockState.Length)
            {
                throw new ArgumentException("Clock states must be of equal length.");
            }

            // Euclidean norm of the difference between clock states
            double sumSq = 0.0;
            for (int i = 0; i < previousClockState.Length; i++)
            {
                double delta = currentClockState[i] - previousClockState[i];
                sumSq += delta * delta;
            }
            double norm = Math.Sqrt(sumSq);

            // Clamp to [minStep, maxStep] to avoid extremes
            if (double.IsNaN(norm) || double.IsInfinity(norm))
                norm = minStep;

            return Math.Clamp(norm, minStep, maxStep);
        }

        // ================================================================
        // NODE-LEVEL PROPER TIME (RQ-HYPOTHESIS ITEM 3)
        // ================================================================

        /// <summary>
        /// Computes the proper time step for a specific node based on
        /// its local lapse function (gravitational time dilation).
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Pure Relational Time
        /// =====================================================
        /// d?_i = N_i ? dt where:
        /// - d?_i = proper time elapsed at node i
        /// - N_i = lapse function (time dilation factor)
        /// - dt = coordinate time step (or relational dTau)
        /// 
        /// The lapse function encodes gravitational time dilation:
        /// - Near massive objects (high entropy): N ? 0 (time slows)
        /// - In vacuum (low entropy): N ? 1 (normal time flow)
        /// </summary>
        /// <param name="coordinateTimeStep">Global or relational time step.</param>
        /// <param name="lapseFactor">Local lapse function N_i ? (0, 1].</param>
        /// <returns>Proper time step d?_i for the node.</returns>
        public static double ComputeNodeProperTimeStep(
            double coordinateTimeStep,
            double lapseFactor)
        {
            if (lapseFactor <= 0)
                lapseFactor = PhysicsConstants.MinTimeDilation;

            double properTime = coordinateTimeStep * lapseFactor;
            return Math.Clamp(properTime, MinTimeStep, MaxTimeStep);
        }

        /// <summary>
        /// Computes the action quantum for event-driven updates.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Asynchronous Event-Driven Engine
        /// ==================================================================
        /// In true relational quantum gravity, nodes don't update on a global
        /// clock. Instead, each node accumulates "action" and updates only
        /// when the action quantum is reached.
        /// 
        /// ?S_i = ? L_i d?_i where L_i is the local Lagrangian
        /// 
        /// Node updates when ?S_i exceeds ActionQuantumThreshold.
        /// </summary>
        /// <param name="localEnergy">Local energy density at node.</param>
        /// <param name="properTimeStep">Proper time step d?.</param>
        /// <returns>Action increment ?S for this node.</returns>
        public static double ComputeActionQuantum(
            double localEnergy,
            double properTimeStep)
        {
            // Action = Energy ? Time (in natural units where ? = 1)
            double action = localEnergy * properTimeStep;
            return Math.Abs(action);
        }

        /// <summary>
        /// Determines if a node should update based on accumulated action.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Event-Driven Updates
        /// =====================================================
        /// Instead of updating all nodes every dt, nodes update only
        /// when their local action accumulates to a quantum of action.
        /// </summary>
        /// <param name="accumulatedAction">Action accumulated since last update.</param>
        /// <param name="threshold">Action quantum threshold (default: ActionQuantumThreshold).</param>
        /// <returns>True if node should update now.</returns>
        public static bool ShouldNodeUpdate(
            double accumulatedAction,
            double threshold = ActionQuantumThreshold)
        {
            return accumulatedAction >= threshold;
        }

        // ================================================================
        // TIME SCALE SEPARATION (RQ-HYPOTHESIS ITEM 6)
        // ================================================================

        /// <summary>
        /// Determines if topology update should occur at this step.
        ///
        /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Zeno Effect Prevention
        /// =======================================================
        /// Frequent topology changes (measurements) freeze quantum evolution
        /// (Quantum Zeno Effect). We separate time scales:
        ///
        /// T_topology >> T_quantum
        ///
        /// Topology updates happen every N quantum steps, where N is
        /// TopologyToQuantumTimeScale (default: 10).
        /// </summary>
        /// <param name="quantumStepCount">Current quantum step number.</param>
        /// <param name="separationFactor">Ratio of scales (default: TopologyToQuantumTimeScale).</param>
        /// <returns>True if topology should update this step.</returns>
        public static bool ShouldUpdateTopology(
            int quantumStepCount,
            int separationFactor = TopologyToQuantumTimeScale)
        {
            if (separationFactor <= 0)
                separationFactor = TopologyToQuantumTimeScale;

            return quantumStepCount % separationFactor == 0;
        }

        /// <summary>
        /// Determines if topology update should occur at this step using adaptive interval.
        ///
        /// RQ-HYPOTHESIS CHECKLIST ITEM 43 (10.2): Adaptive Topology Decoherence
        /// ======================================================================
        /// This variant supports adaptive intervals that depend on:
        /// - Graph size (N): Larger graphs have more degrees of freedom
        /// - Energy density (E/N): Higher energy increases quantum fluctuations
        ///
        /// The adaptive interval is computed externally and passed to this method.
        /// </summary>
        /// <param name="quantumStepCount">Current quantum step number.</param>
        /// <param name="adaptiveInterval">Dynamically computed interval (from AdaptiveTopologyDecoherence)</param>
        /// <returns>True if topology should update this step.</returns>
        public static bool ShouldUpdateTopologyAdaptive(
            int quantumStepCount,
            int adaptiveInterval)
        {
            if (adaptiveInterval <= 0)
                adaptiveInterval = TopologyToQuantumTimeScale;

            return quantumStepCount % adaptiveInterval == 0;
        }

        /// <summary>
        /// Computes the topology update probability based on local conditions.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 6: Adaptive Decoherence
        /// ====================================================
        /// Instead of fixed-interval topology updates, we can use a
        /// probability-based approach where the flip probability depends
        /// on the local interaction amplitude:
        /// 
        /// P_flip = P_base ? exp(-|?_ij|? / kT)
        /// 
        /// High amplitude edges (strong correlations) flip less frequently.
        /// </summary>
        /// <param name="edgeAmplitude">Quantum amplitude on the edge.</param>
        /// <param name="temperature">Effective temperature for topology.</param>
        /// <param name="baseRate">Base flip probability.</param>
        /// <returns>Probability of topology change for this edge.</returns>
        public static double ComputeTopologyFlipProbability(
            double edgeAmplitude,
            double temperature,
            double baseRate = 0.1)
        {
            if (temperature <= 0)
                temperature = 1.0;

            double amplitudeSquared = edgeAmplitude * edgeAmplitude;
            double boltzmannFactor = Math.Exp(-amplitudeSquared / temperature);

            return baseRate * boltzmannFactor;
        }

        // ================================================================
        // LAPSE FUNCTION UTILITIES
        // ================================================================

        /// <summary>
        /// Computes the entropic lapse function for a node.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST ITEM 3: Entropic Time Dilation
        /// =======================================================
        /// N_i = exp(-? ? S_i) where:
        /// - S_i = entanglement entropy of node i
        /// - ? = coupling constant (PhysicsConstants.TimeDilationAlpha)
        /// 
        /// This replaces ADM-style N = 1/?(1 + |R| + m) which depends
        /// on classical curvature. Entropic formulation is more fundamental.
        /// </summary>
        /// <param name="entanglementEntropy">Shannon-like entropy of node correlations.</param>
        /// <param name="alpha">Coupling constant for entropy-time relation.</param>
        /// <returns>Lapse function N ? (0, 1].</returns>
        public static double ComputeEntropicLapse(
            double entanglementEntropy,
            double alpha = 0.1)
        {
            if (alpha <= 0)
                alpha = PhysicsConstants.TimeDilationAlpha;

            double lapse = Math.Exp(-alpha * entanglementEntropy);

            return Math.Clamp(lapse,
                PhysicsConstants.MinTimeDilation,
                PhysicsConstants.MaxTimeDilation);
        }

        /// <summary>
        /// Computes edge-level lapse function for geometry evolution.
        /// 
        /// RQ-HYPOTHESIS CHECKLIST: Lapse-Synchronized Geometry
        /// =====================================================
        /// When evolving edge weights, use the geometric mean of
        /// endpoint lapse functions:
        /// 
        /// N_ij = ?(N_i ? N_j)
        /// 
        /// This ensures geometry evolution respects time dilation.
        /// </summary>
        /// <param name="lapseI">Lapse function at node i.</param>
        /// <param name="lapseJ">Lapse function at node j.</param>
        /// <returns>Edge lapse function N_ij.</returns>
        public static double ComputeEdgeLapse(double lapseI, double lapseJ)
        {
            if (lapseI <= 0) lapseI = PhysicsConstants.MinTimeDilation;
            if (lapseJ <= 0) lapseJ = PhysicsConstants.MinTimeDilation;

            return Math.Sqrt(lapseI * lapseJ);
        }

        // ================================================================
        // VALIDATION UTILITIES
        // ================================================================

        /// <summary>
        /// Validates that a time step is within acceptable bounds.
        /// </summary>
        /// <param name="dt">Time step to validate.</param>
        /// <returns>Clamped time step within [MinTimeStep, MaxTimeStep].</returns>
        public static double ClampTimeStep(double dt)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt))
                return PhysicsConstants.BaseTimestep;

            return Math.Clamp(dt, MinTimeStep, MaxTimeStep);
        }

        /// <summary>
        /// Computes the next event time for event-driven simulation.
        /// </summary>
        /// <param name="currentTime">Current simulation time.</param>
        /// <param name="properTimeStep">Proper time step for node.</param>
        /// <param name="timeDilation">Time dilation factor (1/N).</param>
        /// <returns>Time of next scheduled event.</returns>
        public static double ComputeNextEventTime(
            double currentTime,
            double properTimeStep,
            double timeDilation = 1.0)
        {
            if (timeDilation <= 0)
                timeDilation = 1.0;

            // Convert proper time to coordinate time
            double coordinateStep = properTimeStep / timeDilation;

            return currentTime + Math.Max(coordinateStep, MinTimeStep);
        }
    }
}
