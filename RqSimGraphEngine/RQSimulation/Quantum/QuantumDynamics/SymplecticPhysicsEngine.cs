using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace RQSimulation
{
    // Symplectic Velocity-Verlet engine for phase/momentum arrays
    public static unsafe class SymplecticPhysicsEngine
    {
        // UpdateVerlet: half-step momentum, full-step position; forces assumed at t
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void UpdateVerlet(double* phases, double* momenta, double* forces, int count, double dt)
        {
            if (count <= 0 || phases == null || momenta == null || forces == null) return;

            int i = 0;
            if (Avx.IsSupported)
            {
                int vecSize = Vector256<double>.Count; // 4 doubles
                var vDt = Vector256.Create(dt);
                var vHalfDt = Vector256.Create(dt * 0.5);
                for (; i <= count - vecSize; i += vecSize)
                {
                    var vP = Avx.LoadVector256(momenta + i);
                    var vF = Avx.LoadVector256(forces + i);
                    vP = Avx.Add(vP, Avx.Multiply(vF, vHalfDt));

                    var vQ = Avx.LoadVector256(phases + i);
                    vQ = Avx.Add(vQ, Avx.Multiply(vP, vDt));
                    Avx.Store(phases + i, vQ);
                    Avx.Store(momenta + i, vP);
                }
            }
            for (; i < count; i++)
            {
                momenta[i] += forces[i] * dt * 0.5;
                phases[i] += momenta[i] * dt;
            }
        }

        // U(1) force calculation: coupling * sin(phi_j - phi_i - A_ij)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double CalculateU1Force(double phi_i, double phi_j, double gaugeField_ij, double coupling)
            => coupling * Math.Sin(phi_j - phi_i - gaugeField_ij);

        // Metric update over edge list using spans (prevent runaway weights)
        public static void ApplyMetricUpdate(
            Span<double> edgeWeights,
            ReadOnlySpan<double> nodeEnergies,
            ReadOnlySpan<int> edgeSourceIndices,
            ReadOnlySpan<int> edgeTargetIndices,
            double gConst)
        {
            if (edgeWeights.Length == 0) return;
            int len = edgeWeights.Length;
            for (int idx = 0; idx < len; idx++)
            {
                int u = edgeSourceIndices[idx];
                int v = edgeTargetIndices[idx];
                if ((uint)u >= (uint)nodeEnergies.Length || (uint)v >= (uint)nodeEnergies.Length)
                    continue;
                double Eu = nodeEnergies[u];
                double Ev = nodeEnergies[v];
                double metricFactor = 1.0 + gConst * (Eu + Ev);
                double w = edgeWeights[idx] * metricFactor;
                if (w > 1000.0) w = 1000.0;
                if (w < 0.0) w = 0.0;
                edgeWeights[idx] = w;
            }
        }
    }
}
