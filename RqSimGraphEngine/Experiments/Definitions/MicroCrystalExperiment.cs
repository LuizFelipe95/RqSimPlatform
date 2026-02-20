using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class MicroCrystalExperiment : IExperiment
    {
        public string Name => "Solid: Micro-Crystal (3x3x3)";
        public string Description => "Attempts to pack 27 nodes into a dense cluster. Watch d_S approach 3.0.";

        public StartupConfig GetConfig()
        {
            // Physical intent: seed a dense liquid-like lattice that slowly cools into a 3D crystal without collapsing.
            return new StartupConfig
            {
                NodeCount = 27,
                TotalSteps = 8000,
                InitialEdgeProb = 0.4,
                TargetDegree = 4,
                GravitationalCoupling = 0.25,
                HotStartTemperature = 2.0,
                AnnealingCoolingRate = 0.998,
                UseSpectralGeometry = true,
                UseNetworkGravity = true
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => null;
    }
}
