// RQSimulation/Experiments/IExperiment.cs
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments
{
    /// <summary>
    /// Interface for defining experimental simulation modes.
    /// 
    /// Each experiment defines:
    /// - Name and Description for UI display
    /// - Startup configuration (nodes, steps, probabilities, physics params)
    /// - Optional physics constant overrides
    /// - Optional custom graph initialization (e.g., linear chain for DNA folding)
    /// </summary>
    public interface IExperiment
    {
        /// <summary>
        /// Display name of the experiment for UI.
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// Detailed description of what this experiment simulates.
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// Returns the startup configuration for this experiment.
        /// This includes node count, steps, initial edge probability, 
        /// gravitational coupling, and other parameters.
        /// </summary>
        StartupConfig GetConfig();
        
        /// <summary>
        /// Applies experiment-specific overrides to PhysicsConstants.
        /// Called before simulation starts.
        /// 
        /// Examples:
        /// - Set TargetDegree for specific topology
        /// - Set MinimumClusterSize for cluster detection
        /// - Adjust CosmologicalConstant for external pressure
        /// </summary>
        void ApplyPhysicsOverrides();
        
        /// <summary>
        /// Optional custom graph initializer.
        /// If non-null, this action is called after basic graph creation
        /// to set up special topology (e.g., linear chain for DNA).
        /// 
        /// If null, standard random graph initialization is used.
        /// </summary>
        Action<RQGraph>? CustomInitializer { get; }
    }
}
