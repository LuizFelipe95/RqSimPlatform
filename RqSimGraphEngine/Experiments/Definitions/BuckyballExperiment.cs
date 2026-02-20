using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class BuckyballExperiment : IExperiment
    {
        public string Name => "Chem: Buckyball (C60)";
        public string Description => "60 nodes seeking valence 3. Should form a hollow cage/sphere structure.";

        public StartupConfig GetConfig()
        {
            // Physical intent: encourage valence-3 bonding to close into a C60 cage without over-compressing it.
            return new StartupConfig
            {
                NodeCount = 60,
                TotalSteps = 8000,
                InitialEdgeProb = 0.1,
                TargetDegree = 3,
                GravitationalCoupling = 0.3,
                HotStartTemperature = 2.0,
                AnnealingCoolingRate = 0.999,
                UseNetworkGravity = true
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => null;
    }
}
