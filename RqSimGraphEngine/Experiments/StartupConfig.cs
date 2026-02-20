// RQSimulation/Experiments/StartupConfig.cs
using RQSimulation;

namespace RqSimGraphEngine.Experiments
{
    /// <summary>
    /// Configuration for experiment startup parameters.
    /// Maps directly to SimulationConfig fields for UI synchronization.
    /// </summary>
    public class StartupConfig
    {
        // === Basic Graph Parameters ===
        public int NodeCount { get; set; } = 300;
        public double InitialEdgeProb { get; set; } = 0.05;
        public double InitialExcitedProb { get; set; } = 0.10;
        public int TargetDegree { get; set; } = 8;

        // === Simulation Parameters ===
        public int TotalSteps { get; set; } = 5000;
        public double Temperature { get; set; } = 10.0;
        public double LambdaState { get; set; } = 0.5;
        public double EdgeTrialProbability { get; set; } = 0.02;
        public double MeasurementThreshold { get; set; } = 0.3;

        // === Physics Parameters ===
        public double GravitationalCoupling { get; set; } = 0.025;
        public double VacuumEnergyScale { get; set; } = 0.0001;
        public double HotStartTemperature { get; set; } = 5.0;
        public double AnnealingCoolingRate { get; set; } = 0.995;
        public double DecoherenceRate { get; set; } = 0.005;
        public double AdaptiveThresholdSigma { get; set; } = 1.5;
        public double WarmupDuration { get; set; } = 200;
        public double GravityTransitionDuration { get; set; } = 137;

        // === Physics Modules ===
        public bool UseSpectralGeometry { get; set; } = true;
        public bool UseNetworkGravity { get; set; } = true;
        public bool UseQuantumDrivenStates { get; set; } = true;
        public bool UseSpinorField { get; set; } = false;
        public bool UseVacuumFluctuations { get; set; } = true;
        public bool UseHotStartAnnealing { get; set; } = true;
        public bool UseTopologicalProtection { get; set; } = true;

        // === Fractal Topology ===
        public int FractalLevels { get; set; } = 0;
        public int FractalBranchFactor { get; set; } = 0;

        /// <summary>
        /// Converts StartupConfig to SimulationConfig for simulation engine.
        /// </summary>
        public SimulationConfig ToSimulationConfig()
        {
            return new SimulationConfig
            {
                NodeCount = NodeCount,
                InitialEdgeProb = InitialEdgeProb,
                InitialExcitedProb = InitialExcitedProb,
                TargetDegree = TargetDegree,
                TotalSteps = TotalSteps,
                Temperature = Temperature,
                LambdaState = LambdaState,
                EdgeTrialProbability = EdgeTrialProbability,
                MeasurementThreshold = MeasurementThreshold,
                GravitationalCoupling = GravitationalCoupling,
                VacuumEnergyScale = VacuumEnergyScale,
                HotStartTemperature = HotStartTemperature,
                AnnealingCoolingRate = AnnealingCoolingRate,
                DecoherenceRate = DecoherenceRate,
                UseSpectralGeometry = UseSpectralGeometry,
                UseNetworkGravity = UseNetworkGravity,
                UseQuantumDrivenStates = UseQuantumDrivenStates,
                UseSpinorField = UseSpinorField,
                UseVacuumFluctuations = UseVacuumFluctuations,
                UseHotStartAnnealing = UseHotStartAnnealing,
                UseTopologicalProtection = UseTopologicalProtection,
                FractalLevels = FractalLevels,
                FractalBranchFactor = FractalBranchFactor
            };
        }
    }
}
