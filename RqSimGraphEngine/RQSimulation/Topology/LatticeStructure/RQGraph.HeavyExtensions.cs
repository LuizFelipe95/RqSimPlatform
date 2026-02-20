using System;
using System.Collections.Generic;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Heavy node flags and edge co-excitation tracking
        private bool[] _isHeavy; // per node heavy flag
        private int[,] _edgeCoExcite; // symmetric co-excitation counters
        private bool[,] _strongEdgeFlag; // strong edge markers

        private void EnsureHeavyArrays()
        {
            if (_isHeavy == null || _isHeavy.Length != N) _isHeavy = new bool[N];
            if (_edgeCoExcite == null || _edgeCoExcite.GetLength(0) != N || _edgeCoExcite.GetLength(1) != N)
                _edgeCoExcite = new int[N, N];
            if (_strongEdgeFlag == null || _strongEdgeFlag.GetLength(0) != N || _strongEdgeFlag.GetLength(1) != N)
                _strongEdgeFlag = new bool[N, N];
        }

        public void UpdateHeavyNodes(double energyThreshold = 1.0)
        {
            EnsureHeavyArrays();
            // simple heuristic: excited + correlation mass / local energy > threshold
            for (int i = 0; i < N; i++)
            {
                double localE = 0.0;
                foreach (int nb in Neighbors(i)) localE += Weights[i, nb];
                double mass = (_correlationMass != null && _correlationMass.Length == N) ? _correlationMass[i] : 0.0;
                _isHeavy[i] = (State[i] == NodeState.Excited) && (localE + mass >= energyThreshold);
            }
        }

        public (double totalMass, double maxMass, int largestSize) ComputeHeavyClustersEnergy()
        {
            EnsureHeavyArrays();
            var visited = new bool[N];
            double totalMass = 0.0; double maxMass = 0.0; int largestSize = 0;
            for (int i = 0; i < N; i++)
            {
                if (visited[i] || !_isHeavy[i]) continue;
                var stack = new Stack<int>();
                stack.Push(i); visited[i] = true;
                double clusterMass = 0.0; int clusterSize = 0;
                while (stack.Count > 0)
                {
                    int v = stack.Pop(); clusterSize++;
                    double localE = 0.0; foreach (int nb in Neighbors(v)) localE += Weights[v, nb];
                    clusterMass += localE;
                    foreach (int nb in Neighbors(v))
                    {
                        if (visited[nb] || !_isHeavy[nb]) continue;
                        visited[nb] = true; stack.Push(nb);
                    }
                }
                totalMass += clusterMass;
                if (clusterMass > maxMass) maxMass = clusterMass;
                if (clusterSize > largestSize) largestSize = clusterSize;
            }
            return (totalMass, maxMass, largestSize);
        }

        public void UpdateEdgeCoExcitation()
        {
            EnsureHeavyArrays();
            for (int i = 0; i < N; i++)
            {
                bool ei = State[i] == NodeState.Excited;
                foreach (int j in Neighbors(i))
                {
                    if (i >= j) continue;
                    bool ej = State[j] == NodeState.Excited;
                    if (ei && ej) _edgeCoExcite[i, j]++;
                }
            }
        }

        public void MarkStrongEdges(int coExciteThreshold)
        {
            EnsureHeavyArrays();
            for (int i = 0; i < N; i++)
            {
                for (int j = i + 1; j < N; j++)
                {
                    if (!Edges[i, j]) continue;
                    bool strong = _edgeCoExcite[i, j] >= coExciteThreshold;
                    _strongEdgeFlag[i, j] = strong;
                    _strongEdgeFlag[j, i] = strong;
                }
            }
        }

        public int CountStrongEdges()
        {
            EnsureHeavyArrays();
            int c = 0;
            for (int i = 0; i < N; i++)
                for (int j = i + 1; j < N; j++)
                    if (_strongEdgeFlag[i, j]) c++;
            return c;
        }

        public void RecomputeGlobalNeighbourSpontFactors()
        {
            double sumNF = 0.0, sumSF = 0.0; int count = 0;
            for (int i = 0; i < N; i++)
            {
                int deg = Degree(i);
                double neighbourFactor = (double)deg / Math.Max(1, _targetDegree);
                double spontFactor = neighbourFactor > 0 ? 1.0 / neighbourFactor : 1.0;
                neighbourFactor = Math.Clamp(neighbourFactor, 0.5, 2.0);
                spontFactor = Math.Clamp(spontFactor, 0.5, 2.0);
                sumNF += neighbourFactor; sumSF += spontFactor; count++;
            }
            if (count > 0)
            {
                GlobalNeighbourFactor = sumNF / count;
                GlobalSpontFactor = sumSF / count;
            }
        }
    }
}
