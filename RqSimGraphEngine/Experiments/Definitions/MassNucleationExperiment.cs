// RQSimulation/Experiments/Definitions/MassNucleationExperiment.cs
using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    /// <summary>
    /// Experiment B: "Mass Nucleation" (Нуклеация Массы)
    /// 
    /// Goal: Formation of stable "heavy" clusters (particles) in empty space.
    /// 
    /// Interpretation: Birth of matter from topological vacuum defects.
    /// Strong gravity forces clustering while maintaining multiple particles.
    /// 
    /// What to observe:
    /// - HeavyMass metric: Should grow S-shaped (logistic curve) and plateau
    /// - LargestCluster: Should NOT be 100% (black hole), expect ~10-20% (particles)
    /// </summary>
    public class MassNucleationExperiment : IExperiment
    {
        public string Name => "Mass Nucleation";
        
        public string Description => 
            "Simulates birth of massive particles from vacuum topological defects. " +
            "Strong gravity forces cluster formation. Expect S-curve growth of HeavyMass " +
            "with plateau, and multiple particle-like clusters (not a single black hole).";
        
        public StartupConfig GetConfig()
        {
            // Physical intent: vast dilute vacuum where moderate gravity seeds multiple particle cores without single collapse.
            return new StartupConfig
            {
                // Provide plenty of vacuum for independent nucleation events
                NodeCount = 2500,
                
                // Longer timeline to separate growth and stabilization phases
                TotalSteps = 10000,
                
                // Extremely sparse start (degree ~6) - vacuum with rare seeds
                InitialEdgeProb = 0.003,
                
                // Strong but not singular gravity keeps multiple clusters apart
                GravitationalCoupling = 0.35,
                
                // Warm start maintains mobility without destroying seeds
                HotStartTemperature = 10.0,
                
                // Slow cooling so phases stay separated
                AnnealingCoolingRate = 0.999,
                
                // Low initial excitation
                InitialExcitedProb = 0.03,
                
                // Higher target degree for stable clusters
                TargetDegree = 10,
                Temperature = 8.0,
                LambdaState = 0.5,
                EdgeTrialProbability = 0.03,
                DecoherenceRate = 0.003,
                WarmupDuration = 400,
                GravityTransitionDuration = 250,
                
                // Enable all physics modules
                UseSpectralGeometry = true,
                UseNetworkGravity = true,
                UseHotStartAnnealing = true,
                UseQuantumDrivenStates = true,
                UseVacuumFluctuations = true,
                UseSpinorField = false,
                
                // No fractal topology
                FractalLevels = 0,
                FractalBranchFactor = 0
            };
        }
        
        public void ApplyPhysicsOverrides()
        {
            // Physics override: Look for tetrahedra (denser than triangles)
            // MinimumClusterSize = 4 in PhysicsConstants
            // Note: PhysicsConstants.MinimumClusterSize is const = 3
            // For this experiment, clusters of size >= 4 are considered "heavy"
        }
        
        public Action<RQGraph>? CustomInitializer => null;
        // Uses standard random graph initialization
    }
}
