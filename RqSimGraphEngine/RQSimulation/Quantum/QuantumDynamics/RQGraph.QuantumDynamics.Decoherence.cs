using System;
using System.Numerics;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Apply decoherence to quantum wavefunction through phase noise.
        /// This simulates environmental interaction that destroys coherence.
        /// </summary>
        public void ApplyDecoherence(double rate)
        {
            if (_waveMulti == null || _waveMulti.Length == 0) return;
            if (rate <= 0) return;

            var rnd = new Random();

            // Phase noise: add random phase shifts to each amplitude
            for (int idx = 0; idx < _waveMulti.Length; idx++)
            {
                double maxAngle = rate; // maximum ±rate radians
                double d = (rnd.NextDouble() * 2 - 1) * maxAngle;
                Complex phaseShift = Complex.FromPolarCoordinates(1.0, d);
                _waveMulti[idx] *= phaseShift;
            }

            // Optional: stochastic collapse with small probability
            double collapseProbability = rate * 0.01; // Much smaller than phase noise
            if (rnd.NextDouble() < collapseProbability && GaugeDimension > 0)
            {
                // Pick a random node to collapse
                int nodeToCollapse = rnd.Next(N);
                int d = GaugeDimension;

                // Find basis state with max amplitude for this node
                double maxAmp = 0.0;
                int maxIndex = 0;
                for (int a = 0; a < d; a++)
                {
                    double amp = _waveMulti[nodeToCollapse * d + a].Magnitude;
                    if (amp > maxAmp)
                    {
                        maxAmp = amp;
                        maxIndex = a;
                    }
                }

                // Collapse: zero out all components except maxIndex
                for (int a = 0; a < d; a++)
                {
                    _waveMulti[nodeToCollapse * d + a] = (a == maxIndex) ? Complex.One : Complex.Zero;
                }
            }
        }
    }
}
