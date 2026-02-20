using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        /// <summary>
        /// Storage for unified node mass models
        /// </summary>
        private Physics.NodeMassModel[]? _nodeMasses;

        /// <summary>
        /// Get or create node mass models array
        /// </summary>
        public Physics.NodeMassModel[] NodeMasses
        {
            get
            {
                if (_nodeMasses == null || _nodeMasses.Length != N)
                {
                    _nodeMasses = new Physics.NodeMassModel[N];
                    for (int i = 0; i < N; i++)
                    {
                        _nodeMasses[i] = new Physics.NodeMassModel();
                    }
                }
                return _nodeMasses;
            }
        }

        /// <summary>
        /// Update all node mass models from current field values.
        /// Should be called periodically to keep masses synchronized.
        /// </summary>
        public void UpdateNodeMassModels()
        {
            var masses = NodeMasses;

            for (int i = 0; i < N; i++)
            {
                masses[i].Reset();

                // Correlation mass (topological)
                if (_correlationMass != null && i < _correlationMass.Length)
                {
                    masses[i].CorrelationMass = _correlationMass[i];
                }

                // Fermion mass from spinor field
                if (_spinorA != null && i < _spinorA.Length)
                {
                    double spinorNorm = _spinorA[i].Magnitude;
                    if (_spinorB != null && i < _spinorB.Length)
                        spinorNorm += _spinorB[i].Magnitude;
                    masses[i].FermionMass = spinorNorm;
                }

                // Scalar field energy
                if (ScalarField != null && i < ScalarField.Length)
                {
                    double phi = ScalarField[i];
                    if (UseMexicanHatPotential)
                    {
                        masses[i].ScalarFieldEnergy = -HiggsMuSquared * phi * phi + HiggsLambda * Math.Pow(phi, 4);
                    }
                    else
                    {
                        masses[i].ScalarFieldEnergy = 0.5 * ScalarMass * ScalarMass * phi * phi;
                    }
                }

                // Gauge field energy at node
                double gaugeE = 0.0;
                foreach (int j in Neighbors(i))
                {
                    if (_edgePhaseU1 != null)
                    {
                        gaugeE += _edgePhaseU1[i, j] * _edgePhaseU1[i, j];
                    }
                }
                masses[i].GaugeFieldEnergy = 0.5 * gaugeE;

                // Vacuum energy (cosmological)
                masses[i].VacuumEnergy = PhysicsConstants.CosmologicalConstant;
            }
        }
    }
}
