using System;
using System.Collections.Generic;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Represents the dynamic state of a cluster including momentum and mass
    /// </summary>
    public class ClusterState
    {
        public int Id { get; set; }
        public List<int> NodeIds { get; set; } = new();
        public Vector3 CenterOfMass { get; set; }  // in spectral coordinates
        public Vector3 Momentum { get; set; }      // cluster momentum
        public double RestMass { get; set; }       // from topology
        public Dictionary<int, double> Membership { get; set; } = new(); // NodeId -> [0..1] fuzzy membership
        
        /// <summary>
        /// Predicted center position based on momentum
        /// </summary>
        public Vector3 PredictedCenter(double dt)
        {
            return CenterOfMass + Momentum * (float)dt;
        }
    }
}
