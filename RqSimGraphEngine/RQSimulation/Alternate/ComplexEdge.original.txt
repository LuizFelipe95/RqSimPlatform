using System;
using System.Numerics;

namespace RQSimulation
{
    public readonly struct ComplexEdge
    {
        private readonly double _magnitude;
        public readonly double Phase;

        public ComplexEdge(double magnitude, double phase)
        {
            _magnitude = magnitude;
            Phase = phase;
        }

        public double GetMagnitude() => _magnitude;
        public Complex ToComplex() => Complex.FromPolarCoordinates(_magnitude, Phase);
        public ComplexEdge WithMagnitude(double magnitude) => new ComplexEdge(magnitude, Phase);
        public ComplexEdge WithPhase(double phase) => new ComplexEdge(_magnitude, phase);
    }
}
