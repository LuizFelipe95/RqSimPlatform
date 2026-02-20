using RqSimGraphEngine.Experiments;
using RQSimulation;

namespace RqSimGraphEngine.Experiments.Definitions
{
    public class FlatlandExperiment : IExperiment
    {
        public string Name => "Flatland Emergence (2D)";
        public string Description => "Attempts to force the emergence of a 2D manifold (sheet/membrane) instead of 4D by restricting TargetDegree and increasing Gravity.";

        public StartupConfig GetConfig()
        {
            // Physical intent: squeeze a 3D network into a 2D membrane via strong pressure but moderate heat.
            return new StartupConfig
            {
                NodeCount = 400,
                TotalSteps = 10000,
                InitialEdgeProb = 0.02,
                TargetDegree = 4,
                GravitationalCoupling = 0.5,
                HotStartTemperature = 5.0,
                AnnealingCoolingRate = 0.999,
                UseSpectralGeometry = true,
                UseNetworkGravity = true
            };
        }

        public void ApplyPhysicsOverrides() { }
        public Action<RQGraph>? CustomInitializer => null;
    }
}
