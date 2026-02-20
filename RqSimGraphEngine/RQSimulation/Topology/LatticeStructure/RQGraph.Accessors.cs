using System;
using System.Collections.Generic;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Gets the current refractory counter for the specified node.  A
        /// positive value indicates how many additional recovery steps are
        /// required before the node can return to the Rest state.  A value
        /// of zero means the node is ready to excite again.
        /// </summary>
        /// <param name="node">Index of the node.</param>
        /// <returns>Remaining refractory steps or zero if out of range.</returns>
        public int GetRefractoryCounter(int node)
        {
            if (_refractoryCounter == null || node < 0 || node >= N)
                return 0;
            return _refractoryCounter[node];
        }

        /// <summary>
        /// Sets the refractory counter for a given node.  Use this to
        /// initialise or adjust the refractory period when interfacing with
        /// alternative simulation schemes such as kinetic Monte Carlo.
        /// Values less than zero are clipped to zero.
        /// </summary>
        /// <param name="node">Index of the node.</param>
        /// <param name="value">New refractory counter value.</param>
        public void SetRefractoryCounter(int node, int value)
        {
            if (_refractoryCounter == null || node < 0 || node >= N)
                return;
            if (value < 0) value = 0;
            _refractoryCounter[node] = value;
        }

        /// <summary>
        /// Base refractory steps derived from correlation time or relational dt.
        /// Guarantees at least one step refractory period.
        /// </summary>
        public int BaseRefractorySteps
        {
            get
            {
                double dt = ComputeRelationalDt();
                double tau = CorrelationTime > 0.0 ? CorrelationTime : dt;
                double denom = Math.Max(dt, 1e-9);
                int val = (int)Math.Round(tau / denom);
                if (val < 1) val = 1;
                return val;
            }
        }
    }
}