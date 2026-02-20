using System;
using System.Numerics;

namespace RQSimulation
{
    /// <summary>
    /// Quantum edge support for RQGraph - Quantum Graphity implementation.
    /// 
    /// RQ-HYPOTHESIS: Quantum Graphity
    /// ================================
    /// In quantum gravity, the geometry itself is in superposition.
    /// Edges can exist and not-exist simultaneously with complex amplitudes.
    /// 
    /// |edge_ij? = ?_ij|exists? + ?_ij|not-exists?
    /// 
    /// This partial class adds quantum edge functionality while preserving
    /// backward compatibility with classical edge operations.
    /// 
    /// When _quantumEdges is null, all operations fall back to classical behavior.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Quantum edge amplitudes. When null, classical mode is active.
        /// Each entry stores the amplitude for edge existence.
        /// </summary>
        private ComplexEdge[,]? _quantumEdges;
        
        /// <summary>
        /// Flag indicating whether quantum edge mode is enabled.
        /// </summary>
        public bool IsQuantumEdgeMode => _quantumEdges != null;
        
        /// <summary>
        /// Enable quantum edge mode by initializing quantum amplitudes
        /// from current classical edge configuration.
        /// 
        /// After calling this, edges are in definite classical states
        /// (amplitude = 1 for existing edges, 0 for non-existing).
        /// Use SetQuantumEdge to create superpositions.
        /// </summary>
        public void EnableQuantumEdges()
        {
            _quantumEdges = new ComplexEdge[N, N];
            
            // Initialize from classical edges
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (Edges[i, j])
                    {
                        double w = Weights[i, j];
                        double phase = _edgePhaseU1?[i, j] ?? 0.0;
                        _quantumEdges[i, j] = new ComplexEdge(w, phase);
                        _quantumEdges[j, i] = _quantumEdges[i, j];
                    }
                    else
                    {
                        _quantumEdges[i, j] = ComplexEdge.NotExists();
                        _quantumEdges[j, i] = _quantumEdges[i, j];
                    }
                }
            }
        }
        
        /// <summary>
        /// Disable quantum edge mode and return to classical behavior.
        /// Current classical edge state is preserved.
        /// </summary>
        public void DisableQuantumEdges()
        {
            _quantumEdges = null;
        }
        
        /// <summary>
        /// Get quantum amplitude for edge (i, j).
        /// If quantum mode is not enabled, returns amplitude based on classical state.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <returns>ComplexEdge representing the quantum state of the edge</returns>
        public ComplexEdge GetQuantumEdge(int i, int j)
        {
            if (i < 0 || j < 0 || i >= N || j >= N || i == j)
            {
                return ComplexEdge.NotExists();
            }
            
            if (_quantumEdges != null)
            {
                return _quantumEdges[i, j];
            }
            
            // Fallback: construct from classical state
            if (Edges[i, j])
            {
                double phase = _edgePhaseU1?[i, j] ?? 0.0;
                return new ComplexEdge(Weights[i, j], phase);
            }
            
            return ComplexEdge.NotExists();
        }
        
        /// <summary>
        /// Set quantum edge amplitude.
        /// Automatically enables quantum mode if not already enabled.
        /// Also updates classical representation for compatibility.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <param name="edge">Quantum edge state to set</param>
        public void SetQuantumEdge(int i, int j, ComplexEdge edge)
        {
            if (i < 0 || j < 0 || i >= N || j >= N || i == j)
            {
                return;
            }
            
            if (_quantumEdges == null)
            {
                EnableQuantumEdges();
            }
            
            _quantumEdges![i, j] = edge;
            _quantumEdges[j, i] = edge;
            
            // Update classical representation for compatibility
            // Edge "exists" classically if probability > 0.5
            bool exists = edge.ExistenceProbability > 0.5;
            
            bool wasEdge = Edges[i, j];
            Edges[i, j] = exists;
            Edges[j, i] = exists;
            
            if (exists)
            {
                Weights[i, j] = edge.GetMagnitude();
                Weights[j, i] = Weights[i, j];
            }
            else
            {
                Weights[i, j] = 0.0;
                Weights[j, i] = 0.0;
            }
            
            // Update degrees if edge state changed
            if (wasEdge != exists)
            {
                if (exists)
                {
                    _degree[i]++;
                    _degree[j]++;
                }
                else
                {
                    _degree[i]--;
                    _degree[j]--;
                }
                TopologyVersion++;
            }
        }
        
        /// <summary>
        /// Create superposition state for an edge.
        /// </summary>
        /// <param name="i">First node index</param>
        /// <param name="j">Second node index</param>
        /// <param name="alphaExists">Amplitude for edge existing</param>
        /// <param name="betaNotExists">Amplitude for edge not existing</param>
        public void SetEdgeSuperposition(int i, int j, Complex alphaExists, Complex betaNotExists)
        {
            var edge = ComplexEdge.Superposition(alphaExists, betaNotExists);
            SetQuantumEdge(i, j, edge);
        }
        
        /// <summary>
        /// Collapse all quantum edges to classical states via measurement.
        /// Each edge's existence is determined probabilistically based on |?|?.
        /// After calling this, quantum mode remains active but all edges are in
        /// definite classical states.
        /// </summary>
        public void CollapseQuantumEdges()
        {
            if (_quantumEdges == null) 
            {
                return;
            }
            
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    bool wasEdge = Edges[i, j];
                    bool exists = _quantumEdges[i, j].Measure(_rng);
                    
                    Edges[i, j] = exists;
                    Edges[j, i] = exists;
                    
                    if (exists)
                    {
                        // Keep magnitude from quantum state
                        double mag = _quantumEdges[i, j].GetMagnitude();
                        if (mag < 0.01) mag = 0.5; // Minimum weight for existing edge
                        
                        Weights[i, j] = mag;
                        Weights[j, i] = mag;
                        
                        // Update quantum state to definite "exists"
                        _quantumEdges[i, j] = ComplexEdge.Exists(mag, _quantumEdges[i, j].Phase);
                        _quantumEdges[j, i] = _quantumEdges[i, j];
                    }
                    else
                    {
                        Weights[i, j] = 0.0;
                        Weights[j, i] = 0.0;
                        
                        // Update quantum state to definite "not exists"
                        _quantumEdges[i, j] = ComplexEdge.NotExists();
                        _quantumEdges[j, i] = _quantumEdges[i, j];
                    }
                    
                    // Track degree changes
                    if (wasEdge && !exists)
                    {
                        _degree[i]--;
                        _degree[j]--;
                    }
                    else if (!wasEdge && exists)
                    {
                        _degree[i]++;
                        _degree[j]++;
                    }
                }
            }
            
            TopologyVersion++;
        }
        
        /// <summary>
        /// Recalculate all node degrees from scratch.
        /// Useful after bulk edge modifications.
        /// </summary>
        public void RecalculateAllDegrees()
        {
            if (_degree == null || _degree.Length != N)
            {
                _degree = new int[N];
            }
            
            for (int i = 0; i < N; i++)
            {
                int deg = 0;
                for (int j = 0; j < N; j++)
                {
                    if (i != j && Edges[i, j])
                    {
                        deg++;
                    }
                }
                _degree[i] = deg;
            }
        }
        
        /// <summary>
        /// Apply unitary evolution to quantum edge amplitudes.
        /// Implements time evolution: |?(t+dt)? = exp(-iHdt)|?(t)?
        /// 
        /// For edges, the local Hamiltonian is based on neighbor connectivity.
        /// </summary>
        /// <param name="dt">Time step</param>
        public void EvolveQuantumEdges(double dt)
        {
            if (_quantumEdges == null)
            {
                return;
            }
            
            // Create buffer for new amplitudes (to avoid order-dependent updates)
            var newEdges = new ComplexEdge[N, N];
            
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    var currentEdge = _quantumEdges[i, j];
                    
                    // Local "energy" based on neighbor configuration
                    // More connected neighbors = lower energy for edge existence
                    double neighborEnergy = ComputeEdgeLocalEnergy(i, j);
                    
                    // Unitary evolution: multiply by exp(-i * E * dt)
                    double phase = -neighborEnergy * dt;
                    Complex evolutionFactor = Complex.FromPolarCoordinates(1.0, phase);
                    
                    Complex newAmplitude = currentEdge.Amplitude * evolutionFactor;
                    
                    // Preserve classical weight while evolving amplitude
                    newEdges[i, j] = new ComplexEdge(currentEdge.GetMagnitude(), newAmplitude);
                    newEdges[j, i] = newEdges[i, j];
                }
            }
            
            // Copy back
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    _quantumEdges[i, j] = newEdges[i, j];
                    _quantumEdges[j, i] = newEdges[j, i];
                }
            }
        }
        
        /// <summary>
        /// Compute local energy for edge (i,j) based on neighborhood.
        /// Used in quantum edge evolution.
        /// </summary>
        private double ComputeEdgeLocalEnergy(int i, int j)
        {
            // Count common neighbors (triangles)
            int commonNeighbors = 0;
            for (int k = 0; k < N; k++)
            {
                if (k == i || k == j) continue;
                if (Edges[i, k] && Edges[j, k])
                {
                    commonNeighbors++;
                }
            }
            
            // More triangles = lower energy (favors clustered structures)
            // This implements a simple "graphity" energy functional
            double triangleEnergy = -0.1 * commonNeighbors;
            
            // Degree penalty (too many edges = higher energy)
            double degreeEnergy = 0.01 * (_degree[i] + _degree[j] - 2.0 * _targetDegree);
            
            return triangleEnergy + degreeEnergy;
        }
        
        /// <summary>
        /// Get total quantum edge existence probability.
        /// Sum of all edge existence probabilities.
        /// </summary>
        public double GetTotalQuantumEdgeProbability()
        {
            if (_quantumEdges == null)
            {
                // Count classical edges
                int count = 0;
                for (int i = 0; i < N; i++)
                {
                    for (int j = i + 1; j < N; j++)
                    {
                        if (Edges[i, j]) count++;
                    }
                }
                return count;
            }
            
            double total = 0.0;
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    total += _quantumEdges[i, j].ExistenceProbability;
                }
            }
            
            return total;
        }
        
        /// <summary>
        /// Get quantum purity of edge configuration.
        /// Purity = 1 for pure classical state, &lt; 1 for superposition.
        /// Computed as average of |2p-1| where p is existence probability.
        /// </summary>
        public double GetQuantumEdgePurity()
        {
            if (_quantumEdges == null)
            {
                return 1.0; // Classical state is pure
            }
            
            double sumPurity = 0.0;
            int count = 0;
            
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    double p = _quantumEdges[i, j].ExistenceProbability;
                    // |2p - 1| is 1 for p=0 or p=1, 0 for p=0.5
                    sumPurity += Math.Abs(2.0 * p - 1.0);
                    count++;
                }
            }
            
            return count > 0 ? sumPurity / count : 1.0;
        }
    }
}
