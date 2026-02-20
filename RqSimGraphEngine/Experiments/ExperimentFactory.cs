// RQSimulation/Experiments/ExperimentFactory.cs
using System.Collections.Generic;
using RqSimGraphEngine.Experiments.Definitions;

namespace RqSimGraphEngine.Experiments
{
    /// <summary>
    /// Factory providing access to all available experiments.
    /// Used by UI to populate experiment selection dropdown.
    /// </summary>
    public static class ExperimentFactory
    {
        /// <summary>
        /// Returns all available experiment definitions.
        /// Order determines display order in UI dropdown.
        /// </summary>
        public static IEnumerable<IExperiment> AvailableExperiments => new IExperiment[]
        {
            new VacuumGenesisExperiment(),
            new MassNucleationExperiment(),
            new BioFoldingExperiment(),
            // Newly implemented experiments
            new BlackHoleEvaporationExperiment(),
            new FlatlandExperiment(),
            new WormholeExperiment(),
            new TetrahedronExperiment(),
            new NanoWireExperiment(),
            new MicroCrystalExperiment(),
            new BuckyballExperiment(),
            new TunnelingExperiment(),
            new HypercubeExperiment(),
            new BinaryMergerExperiment(),
            new QuantumRingExperiment(),
            new LatticeMeltingExperiment(),
            new InflationExperiment(),
            // Scaling / verification experiments
            new VacuumScalingExperiment()
        };

        /// <summary>
        /// Gets experiment by name (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        public static IExperiment? GetByName(string name)
        {
            foreach (var exp in AvailableExperiments)
            {
                if (string.Equals(exp.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return exp;
                }
            }
            return null;
        }
    }
}
