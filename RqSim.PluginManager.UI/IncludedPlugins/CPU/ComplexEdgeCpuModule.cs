using System.Numerics;
using RQSimulation;
using RQSimulation.Core.Plugins;

namespace RqSimPlatform.PluginManager.UI.IncludedPlugins.CPU;

/// <summary>
/// CPU module for complex edge calculations in the RQ simulation.
/// Provides polar coordinate representation of edge weights with phase information.
/// 
/// Implements ISpanPhysicsModule for zero-copy Span-based execution when available.
/// Based on original ComplexEdge implementation.
/// </summary>
public sealed class ComplexEdgeCpuModule : CpuPluginBase, ISpanPhysicsModule
{
    public override string Name => "Complex Edge (CPU)";
    public override string Description => "CPU-based complex edge weight calculations with polar coordinates";
    public override string Category => "Topology";
    public override int Priority => 15;

    /// <summary>
    /// Minimum magnitude threshold for edge existence.
    /// </summary>
    public double MinMagnitude { get; set; } = 1e-10;
    
    /// <summary>
    /// Branch pruning threshold for dead edges.
    /// </summary>
    public double PruneEpsilon { get; set; } = 1e-15;

    public override void Initialize(RQGraph graph)
    {
        // Initialize edge phases if not already set (use 2D matrix property)
        if (graph.EdgePhaseU1 is null || graph.EdgePhaseU1.GetLength(0) != graph.N)
        {
            graph.InitEdgeGaugePhases();
        }
    }

    public override void ExecuteStep(RQGraph graph, double dt)
    {
        // Complex edge evolution - update phases based on local curvature
        int n = graph.N;
        
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (!graph.Edges[i, j]) continue;

                double magnitude = graph.Weights[i, j];
                
                // BRANCH PRUNING: Skip and potentially remove dead edges
                if (magnitude < PruneEpsilon)
                {
                    // Zero out effectively dead edge weights
                    graph.Weights[i, j] = 0.0;
                    graph.Weights[j, i] = 0.0;
                    continue;
                }
                
                if (magnitude < MinMagnitude) continue;

                double phase = graph.GetEdgePhase(i, j);
                
                // NUMERICAL GUARD: Check for NaN/Inf
                if (double.IsNaN(phase) || double.IsInfinity(phase))
                {
                    graph.SetEdgePhase(i, j, 0.0);
                    continue;
                }

                // Phase evolution proportional to local Hamiltonian
                double localH = GetLocalHamiltonian(graph, i, j);
                
                // NUMERICAL GUARD: Check Hamiltonian
                if (double.IsNaN(localH) || double.IsInfinity(localH))
                {
                    continue;
                }
                
                double phaseShift = localH * dt;

                // Apply phase shift with normalization
                double newPhase = NormalizePhase(phase + phaseShift);
                
                // NUMERICAL GUARD: Final check
                if (double.IsNaN(newPhase) || double.IsInfinity(newPhase))
                {
                    newPhase = 0.0;
                }
                
                graph.SetEdgePhase(i, j, newPhase);
            }
        }
    }

    /// <summary>
    /// Zero-copy Span-based execution for high-performance scenarios.
    /// Uses direct memory access without heap allocations.
    /// </summary>
    public void ExecuteSpan(Span<double> weights, Span<double> edgePhases, ReadOnlySpan<bool> edges, int nodeCount, double dt)
    {
        // Process upper triangle of adjacency matrix (symmetric)
        for (int i = 0; i < nodeCount; i++)
        {
            int rowOffset = i * nodeCount;
            
            for (int j = i + 1; j < nodeCount; j++)
            {
                int idx = rowOffset + j;
                int idxSym = j * nodeCount + i;
                
                // Skip non-edges
                if (!edges[idx]) continue;

                double magnitude = weights[idx];
                
                // BRANCH PRUNING: Skip and zero out dead edges
                if (magnitude < PruneEpsilon)
                {
                    weights[idx] = 0.0;
                    weights[idxSym] = 0.0;
                    continue;
                }
                
                if (magnitude < MinMagnitude) continue;

                double phase = edgePhases[idx];
                
                // NUMERICAL GUARD: Check for NaN/Inf
                if (double.IsNaN(phase) || double.IsInfinity(phase))
                {
                    edgePhases[idx] = 0.0;
                    edgePhases[idxSym] = 0.0;
                    continue;
                }

                // Phase evolution: H = w? (simple approximation)
                double localH = magnitude * magnitude;
                
                // NUMERICAL GUARD: Clamp Hamiltonian
                if (double.IsNaN(localH) || double.IsInfinity(localH))
                {
                    continue;
                }
                
                const double maxWeight = 1e6;
                if (localH > maxWeight * maxWeight)
                {
                    localH = maxWeight * maxWeight;
                }
                
                double phaseShift = localH * dt;
                double newPhase = NormalizePhase(phase + phaseShift);
                
                // NUMERICAL GUARD: Final check
                if (double.IsNaN(newPhase) || double.IsInfinity(newPhase))
                {
                    newPhase = 0.0;
                }
                
                // Update with antisymmetry
                edgePhases[idx] = newPhase;
                edgePhases[idxSym] = -newPhase;
            }
        }
    }

    private static double GetLocalHamiltonian(RQGraph graph, int i, int j)
    {
        // Simple approximation: use weight as proxy for energy
        double weight = graph.Weights[i, j];
        
        // NUMERICAL GUARD: Prevent overflow
        if (double.IsNaN(weight) || double.IsInfinity(weight))
        {
            return 0.0;
        }
        
        // Clamp to prevent H?? overflow
        const double maxWeight = 1e6;
        if (weight > maxWeight) weight = maxWeight;
        
        return weight * weight;
    }

    private static double NormalizePhase(double phase)
    {
        // NUMERICAL GUARD: Handle NaN/Inf
        if (double.IsNaN(phase) || double.IsInfinity(phase))
        {
            return 0.0;
        }
        
        const double twoPi = 2.0 * Math.PI;
        phase %= twoPi;
        if (phase < 0) phase += twoPi;
        return phase;
    }

    /// <summary>
    /// Creates a complex edge from magnitude and phase.
    /// </summary>
    public static ComplexEdgeData CreateEdge(double magnitude, double phase)
    {
        return new ComplexEdgeData(magnitude, phase);
    }

    /// <summary>
    /// Converts edge weight and phase to Complex number.
    /// </summary>
    public static Complex ToComplex(double magnitude, double phase)
    {
        // NUMERICAL GUARD
        if (double.IsNaN(magnitude) || double.IsInfinity(magnitude) ||
            double.IsNaN(phase) || double.IsInfinity(phase))
        {
            return Complex.Zero;
        }
        
        return Complex.FromPolarCoordinates(magnitude, phase);
    }
}

/// <summary>
/// Readonly struct representing a complex-valued edge with polar coordinates.
/// </summary>
public readonly struct ComplexEdgeData
{
    private readonly double _magnitude;
    public readonly double Phase;

    public ComplexEdgeData(double magnitude, double phase)
    {
        _magnitude = magnitude;
        Phase = phase;
    }

    public double GetMagnitude() => _magnitude;
    public Complex ToComplex() => Complex.FromPolarCoordinates(_magnitude, Phase);
    public ComplexEdgeData WithMagnitude(double magnitude) => new(magnitude, Phase);
    public ComplexEdgeData WithPhase(double phase) => new(_magnitude, phase);
}
