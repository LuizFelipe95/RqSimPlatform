namespace RQSimulation.Physics
{
    /// <summary>
    /// Represents a node (or vertex) in the relational graph. Each node
    /// has various contributions to its mass/energy which are unified
    /// through the TotalMass property. Implements checklist item #9.
    /// </summary>
    public sealed class NodeMassModel
    {
        /// <summary>
        /// Mass contribution from the fermionic field on this node.
        /// Represents matter content (quarks, leptons).
        /// </summary>
        public double FermionMass { get; set; }

        /// <summary>
        /// Mass contribution from the correlation structure (i.e.
        /// connectivity strength) of this node in the graph.
        /// Represents gravitational/geometric mass.
        /// </summary>
        public double CorrelationMass { get; set; }

        /// <summary>
        /// Energy contribution from gauge fields terminating at this
        /// node. For U(1) this is electric field energy, for SU(2)/SU(3)
        /// this reflects color charges and weak isospin.
        /// </summary>
        public double GaugeFieldEnergy { get; set; }

        /// <summary>
        /// Energy contribution from the scalar (Higgs) field at this node.
        /// </summary>
        public double ScalarFieldEnergy { get; set; }

        /// <summary>
        /// Vacuum energy contribution (cosmological constant term).
        /// </summary>
        public double VacuumEnergy { get; set; }

        /// <summary>
        /// Kinetic energy contribution from node motion/momentum.
        /// </summary>
        public double KineticEnergy { get; set; }

        /// <summary>
        /// Unified total mass/energy for gravitational coupling and
        /// other interactions. This is the source term for gravity.
        /// E = m (natural units where c=1).
        /// </summary>
        public double TotalMass =>
            FermionMass +
            CorrelationMass +
            GaugeFieldEnergy +
            ScalarFieldEnergy +
            VacuumEnergy +
            KineticEnergy;

        /// <summary>
        /// Returns the rest mass (excluding kinetic energy).
        /// </summary>
        public double RestMass =>
            FermionMass +
            CorrelationMass +
            ScalarFieldEnergy;

        /// <summary>
        /// Resets all mass contributions to zero.
        /// </summary>
        public void Reset()
        {
            FermionMass = 0;
            CorrelationMass = 0;
            GaugeFieldEnergy = 0;
            ScalarFieldEnergy = 0;
            VacuumEnergy = 0;
            KineticEnergy = 0;
        }

        /// <summary>
        /// Creates a copy of this node mass model.
        /// </summary>
        public NodeMassModel Clone()
        {
            return new NodeMassModel
            {
                FermionMass = FermionMass,
                CorrelationMass = CorrelationMass,
                GaugeFieldEnergy = GaugeFieldEnergy,
                ScalarFieldEnergy = ScalarFieldEnergy,
                VacuumEnergy = VacuumEnergy,
                KineticEnergy = KineticEnergy
            };
        }
    }
}
