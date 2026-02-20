using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Wrapper exposing per-node correlation mass distribution for gravity.
        /// </summary>
        public double[] ComputeCorrelationMass()
        {
            if (N <= 0) return Array.Empty<double>();
            var m = ComputePerNodeCorrelationMass();
            return m ?? new double[N];
        }
    }
}