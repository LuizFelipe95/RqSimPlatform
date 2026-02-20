using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class TetrahedronExperiment : IExperiment
    {
        public string Name => "Unit: Tetrahedron Genesis";
        public string Description => "The smallest possible 3D simplex (4 nodes). Should form a complete graph (K4) instantly.";

        public StartupConfig GetConfig()
        {
            return new StartupConfig
            {
                NodeCount = 4,
                TotalSteps = 1000,
                InitialEdgeProb = 0.0,
                TargetDegree = 3,
                GravitationalCoupling = 1.0,
                HotStartTemperature = 0.1,
                UseNetworkGravity = true,
                EdgeTrialProbability = 0.5
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => null;
    }
}
