using System;

namespace RQSimulation.Physics
{
    /// <summary>
    /// Tracks total energy of the simulation and validates conservation
    /// across updates. Implements checklist item #4 by providing a
    /// unified energy accounting mechanism. Each subsystem should
    /// register its energy contributions through the <see cref="_computeTotalEnergy"/>
    /// delegate and call <see cref="CheckEnergyConservation"/> after
    /// performing updates.
    /// </summary>
    public sealed class EnergyBook
    {
        private readonly Func<double> _computeTotalEnergy;
        private readonly double _tolerance;
        private double _previousEnergy;

        /// <summary>
        /// Initializes a new instance of the <see cref="EnergyBook"/> class.
        /// </summary>
        /// <param name="computeTotalEnergy">A delegate that computes the
        /// current total energy of the system.</param>
        /// <param name="tolerance">Relative tolerance for energy conservation.
        /// Exceeding this triggers an exception.</param>
        public EnergyBook(Func<double> computeTotalEnergy, double tolerance = 1e-6)
        {
            ArgumentNullException.ThrowIfNull(computeTotalEnergy);
            _computeTotalEnergy = computeTotalEnergy;
            _tolerance = tolerance;
            _previousEnergy = _computeTotalEnergy();
        }

        /// <summary>
        /// Gets the last recorded total energy.
        /// </summary>
        public double PreviousEnergy => _previousEnergy;

        /// <summary>
        /// Gets the configured tolerance for energy conservation checks.
        /// </summary>
        public double Tolerance => _tolerance;

        /// <summary>
        /// Checks energy conservation. Should be called after each
        /// simulation step. Throws an exception if the relative
        /// difference between the current total energy and the
        /// previous recorded energy exceeds the configured tolerance.
        /// </summary>
        /// <returns>True if energy is conserved within tolerance.</returns>
        public bool CheckEnergyConservation()
        {
            double currentEnergy = _computeTotalEnergy();
            double delta = currentEnergy - _previousEnergy;
            double relative = Math.Abs(delta) / (Math.Abs(_previousEnergy) + 1e-12);

            if (relative > _tolerance)
            {
                throw new InvalidOperationException(
                    $"Energy conservation violated: ?E={delta:E3}, relative={relative:E3}, tolerance={_tolerance:E3}");
            }

            _previousEnergy = currentEnergy;
            return true;
        }

        /// <summary>
        /// Checks energy conservation without throwing. Returns the relative
        /// energy change and whether it's within tolerance.
        /// </summary>
        /// <param name="relativeChange">Output: the relative energy change.</param>
        /// <returns>True if energy is conserved within tolerance.</returns>
        public bool TryCheckEnergyConservation(out double relativeChange)
        {
            double currentEnergy = _computeTotalEnergy();
            double delta = currentEnergy - _previousEnergy;
            relativeChange = Math.Abs(delta) / (Math.Abs(_previousEnergy) + 1e-12);

            bool isConserved = relativeChange <= _tolerance;
            _previousEnergy = currentEnergy;

            return isConserved;
        }

        /// <summary>
        /// Resets the energy baseline to the current computed energy.
        /// Useful after topology changes that legitimately change total energy.
        /// </summary>
        public void ResetBaseline()
        {
            _previousEnergy = _computeTotalEnergy();
        }
    }
}
