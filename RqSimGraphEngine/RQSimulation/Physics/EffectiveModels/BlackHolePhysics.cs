using System;

namespace RQSimulation.Physics
{
    /// <summary>
    /// Provides utilities for identifying and evolving black-hole-like
    /// clusters. Implements checklist item #14. The functions here
    /// define a black hole in terms of cluster mass and radius and
    /// estimate its temperature and entropy. They also provide a
    /// simplistic evaporation model where clusters lose mass over
    /// time according to their Hawking temperature.
    /// </summary>
    public static class BlackHolePhysics
    {
        /// <summary>
        /// Default density threshold for black hole identification.
        /// </summary>
        public const double DefaultDensityThreshold = 10.0;

        /// <summary>
        /// Default evaporation constant (mass loss rate coefficient).
        /// </summary>
        public const double DefaultEvaporationConstant = 1e-4;

        /// <summary>
        /// Determines whether a cluster should be considered a black
        /// hole based on its mass and effective radius. A simple
        /// criterion is used: if the cluster mass divided by its
        /// radius exceeds a threshold (analogous to density) then it
        /// is flagged as a black hole. Units are arbitrary.
        /// </summary>
        /// <param name="mass">Cluster mass.</param>
        /// <param name="radius">Effective cluster radius.</param>
        /// <param name="threshold">Density threshold for black hole classification.</param>
        /// <returns>True if cluster qualifies as black hole.</returns>
        public static bool IsBlackHole(double mass, double radius, double threshold = DefaultDensityThreshold)
        {
            if (radius <= 0) return false;
            return (mass / radius) >= threshold;
        }

        /// <summary>
        /// Computes the Schwarzschild radius for a given mass.
        /// r_s = 2GM/c? (in natural units where G=c=1, this simplifies to r_s = 2M).
        /// </summary>
        /// <param name="mass">Black hole mass.</param>
        /// <returns>Schwarzschild radius.</returns>
        public static double SchwarzschildRadius(double mass)
        {
            return 2.0 * mass;
        }

        /// <summary>
        /// Estimates the Hawking-like temperature of a black hole as
        /// inverse proportional to its mass. T_H = ?c?/(8?GMk_B).
        /// In natural units this simplifies to T ~ 1/(8?M).
        /// </summary>
        /// <param name="mass">Black hole mass.</param>
        /// <returns>Hawking temperature.</returns>
        public static double Temperature(double mass)
        {
            if (mass <= 0) return 0.0;
            return 1.0 / (8.0 * Math.PI * mass);
        }

        /// <summary>
        /// Computes the Bekenstein-Hawking entropy of a black hole cluster
        /// proportional to the square of its mass (or equivalently, area).
        /// S = A/(4l_p?) where A ~ M?.
        /// </summary>
        /// <param name="mass">Black hole mass.</param>
        /// <returns>Black hole entropy.</returns>
        public static double Entropy(double mass)
        {
            if (mass <= 0) return 0.0;
            return 4.0 * Math.PI * mass * mass; // S = 4?M? in Planck units
        }

        /// <summary>
        /// Applies a simple evaporation step to a black hole cluster
        /// mass. Mass decreases in proportion to the temperature and
        /// an evaporation constant: dM/dt ~ -T? ~ -1/M?.
        /// Returns the new mass. Mass is clipped to be non-negative.
        /// </summary>
        /// <param name="mass">Current black hole mass.</param>
        /// <param name="dt">Time step.</param>
        /// <param name="evaporationConstant">Evaporation rate coefficient.</param>
        /// <returns>New mass after evaporation.</returns>
        public static double Evaporate(double mass, double dt = 1.0, double evaporationConstant = DefaultEvaporationConstant)
        {
            if (mass <= 0) return 0.0;

            // Hawking radiation power ~ T? ~ 1/M?
            // dM/dt ~ -1/M?
            double temp = Temperature(mass);
            double dM = evaporationConstant * temp * temp * temp * temp * dt;
            double newMass = mass - dM;

            return Math.Max(0, newMass);
        }

        /// <summary>
        /// Computes the expected lifetime of a black hole before complete evaporation.
        /// ? ~ M? in Planck units.
        /// </summary>
        /// <param name="mass">Initial black hole mass.</param>
        /// <param name="evaporationConstant">Evaporation rate coefficient.</param>
        /// <returns>Expected lifetime.</returns>
        public static double Lifetime(double mass, double evaporationConstant = DefaultEvaporationConstant)
        {
            if (mass <= 0 || evaporationConstant <= 0) return 0.0;
            // From dM/dt ~ -1/M?, integrate to get ? ~ M?
            return mass * mass * mass / (3.0 * evaporationConstant);
        }

        /// <summary>
        /// Checks if a cluster with given mass and radius is within its own
        /// Schwarzschild radius (i.e., has collapsed into a black hole).
        /// </summary>
        /// <param name="mass">Cluster mass.</param>
        /// <param name="radius">Cluster radius.</param>
        /// <returns>True if cluster is collapsed.</returns>
        public static bool IsCollapsed(double mass, double radius)
        {
            if (radius <= 0) return true;
            return radius <= SchwarzschildRadius(mass);
        }
    }
}
