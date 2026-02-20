using RqSimGraphEngine.Experiments;
using RQSimulation;
using System;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class InflationExperiment : IExperiment
    {
        public string Name => "Cosmos: Inflation (Big Bang)";
        public string Description => "Starts with a singularity (Complete Graph K50). Expands into a sparse graph.";

        public StartupConfig GetConfig()
        {
            // Physical intent: simulate an inflating hot universe where lambda-pressure beats gravity while cooling frees defects.
            return new StartupConfig
            {
                NodeCount = 200,
                TotalSteps = 10000,
                InitialEdgeProb = 0.9,
                TargetDegree = 4,
                GravitationalCoupling = 0.1,
                LambdaState = 0.05,
                HotStartTemperature = 25.0,
                AnnealingCoolingRate = 0.999,
                UseNetworkGravity = true,
                UseSpectralGeometry = true,
                EdgeTrialProbability = 0.5
            };
        }

        public void ApplyPhysicsOverrides() { }

        public Action<RQGraph>? CustomInitializer => null;
    }
}
