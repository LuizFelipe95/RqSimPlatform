// RQSimulation/Experiments/Definitions/VacuumGenesisExperiment.cs
using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    /// <summary>
    /// Experiment A: "Vacuum Genesis" (Вакуумный Генезис)
    /// 
    /// Goal: Observe phase transition from chaos (dS ≈ ∞) to 4D spacetime (dS ≈ 4).
    /// 
    /// Interpretation: Testing the hypothesis that spacetime dimension is a 
    /// dynamically emergent property from quantum graph topology.
    /// 
    /// What to observe:
    /// - SpectralDimension chart: Should smoothly descend and stabilize around 3.5-4.5
    /// - If it drops to 1-2, gravity is too strong
    /// </summary>
    public class VacuumGenesisExperiment : IExperiment
    {
        public string Name => "Vacuum Genesis";
        
        public string Description => 
            "Observes phase transition from chaotic high-dimensional state (dS ≈ ∞) " +
            "to emergent 4D spacetime (dS ≈ 4). Tests the hypothesis that spatial dimension " +
            "is a dynamic, emergent property. Start hot with dense random graph, cool slowly.";
        
        public StartupConfig GetConfig()
        {
            // Physical intent: provide enough volume for a 4D vacuum to self-average while slow cooling lets spacetime crystallize.
            return new StartupConfig
            {
                // 2000 nodes - enough for reliable 4D statistics
                NodeCount = 2000,
                
                // Long evolution to let thermal history unfold
                TotalSteps = 15000,
                
                // Sparse hypercubic seed (~degree 12)
                InitialEdgeProb = 0.006,
                
                // Soft gravity to prevent instant collapse
                GravitationalCoupling = 0.25,
                
                // Hot but not plasma-level start to avoid lock-in
                HotStartTemperature = 20.0,
                
                // Ultra-slow cooling for crystallization
                AnnealingCoolingRate = 0.9995,
                
                // Low initial excitation
                InitialExcitedProb = 0.05,
                
                // Standard values
                TargetDegree = 6,
                Temperature = 10.0,
                LambdaState = 0.5,
                EdgeTrialProbability = 0.02,
                DecoherenceRate = 0.005,
                WarmupDuration = 300,
                GravityTransitionDuration = 200,
                
                // Enable spectral geometry for dS measurement
                UseSpectralGeometry = true,
                UseNetworkGravity = true,
                UseHotStartAnnealing = true,
                UseQuantumDrivenStates = true,
                UseVacuumFluctuations = true,
                
                // No fractal topology - pure random start
                FractalLevels = 0,
                FractalBranchFactor = 0
            };
        }
        
        public void ApplyPhysicsOverrides()
        {
            // Physics override: Target sparse lattice (degree 6)
            // This is the expected average degree for 4D simplicial manifold
            // Note: PhysicsConstants uses const, so we document the expected value
            // The actual TargetDegree is set in StartupConfig
        }
        
        public Action<RQGraph>? CustomInitializer => null;
        // Uses standard random graph initialization
    }
}
