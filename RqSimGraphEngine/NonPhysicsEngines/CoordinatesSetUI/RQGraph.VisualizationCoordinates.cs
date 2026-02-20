using System;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// Visualization and UI-related coordinate functionality for RQGraph.
    /// 
    /// NOTE: This is a NON-PHYSICS module - coordinates are ONLY for rendering.
    /// The physics engine uses graph distances (ShortestPathDistance, GetGraphDistanceWeighted).
    /// 
    /// IMPORTANT: The Coordinates array should NOT be used in any physics calculations.
    /// It exists solely for Form_Main/UI rendering purposes.
    /// </summary>
    public partial class RQGraph
    {
        /// <summary>
        /// Initialize random coordinates for visualization.
        /// These coordinates are ONLY for rendering - physics uses graph distances.
        /// </summary>
        public void InitCoordinatesRandom()
        {
            Coordinates = new (double X, double Y)[N];
            
            for (int i = 0; i < N; i++)
            {
                // Random positions in unit square centered at origin
                Coordinates[i] = (
                    _rng.NextDouble() * 2.0 - 1.0,
                    _rng.NextDouble() * 2.0 - 1.0
                );
            }
        }

        /// <summary>
        /// Initialize coordinates in a circular layout.
        /// Useful for initial visualization before force-directed layout.
        /// </summary>
        public void InitCoordinatesCircular()
        {
            Coordinates = new (double X, double Y)[N];
            
            for (int i = 0; i < N; i++)
            {
                double angle = 2.0 * Math.PI * i / N;
                Coordinates[i] = (Math.Cos(angle), Math.Sin(angle));
            }
        }

        /// <summary>
        /// Initialize coordinates in a grid layout.
        /// </summary>
        public void InitCoordinatesGrid()
        {
            Coordinates = new (double X, double Y)[N];
            
            int cols = (int)Math.Ceiling(Math.Sqrt(N));
            int rows = (int)Math.Ceiling((double)N / cols);
            
            for (int i = 0; i < N; i++)
            {
                int row = i / cols;
                int col = i % cols;
                
                double x = (2.0 * col / (cols - 1)) - 1.0;
                double y = (2.0 * row / (rows - 1)) - 1.0;
                
                if (cols == 1) x = 0;
                if (rows == 1) y = 0;
                
                Coordinates[i] = (x, y);
            }
        }

        /// <summary>
        /// Apply simple force-directed layout for visualization.
        /// Uses Fruchterman-Reingold algorithm.
        /// </summary>
        /// <param name="iterations">Number of layout iterations</param>
        /// <param name="temperature">Initial temperature for simulated annealing</param>
        public void ApplyForceDirectedLayout(int iterations = 50, double temperature = 1.0)
        {
            if (Coordinates == null || Coordinates.Length != N)
                InitCoordinatesRandom();

            double area = 4.0; // -1 to 1 in both dimensions
            double k = Math.Sqrt(area / N); // Optimal edge length
            
            var displacement = new (double X, double Y)[N];
            
            for (int iter = 0; iter < iterations; iter++)
            {
                // Calculate repulsive forces between all pairs
                for (int i = 0; i < N; i++)
                {
                    displacement[i] = (0.0, 0.0);
                    
                    for (int j = 0; j < N; j++)
                    {
                        if (i == j) continue;
                        
                        double dx = Coordinates[i].X - Coordinates[j].X;
                        double dy = Coordinates[i].Y - Coordinates[j].Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                        
                        // Repulsive force: k? / dist
                        double force = k * k / dist;
                        
                        displacement[i].X += (dx / dist) * force;
                        displacement[i].Y += (dy / dist) * force;
                    }
                }
                
                // Calculate attractive forces along edges
                for (int i = 0; i < N; i++)
                {
                    foreach (int j in Neighbors(i))
                    {
                        if (j <= i) continue; // Process each edge once
                        
                        double dx = Coordinates[i].X - Coordinates[j].X;
                        double dy = Coordinates[i].Y - Coordinates[j].Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                        
                        // Attractive force: dist? / k, scaled by edge weight
                        double weight = Weights[i, j];
                        double force = dist * dist / k * weight;
                        
                        double fx = (dx / dist) * force;
                        double fy = (dy / dist) * force;
                        
                        displacement[i].X -= fx;
                        displacement[i].Y -= fy;
                        displacement[j].X += fx;
                        displacement[j].Y += fy;
                    }
                }
                
                // Apply displacements with temperature cooling
                double t = temperature * (1.0 - (double)iter / iterations);
                
                for (int i = 0; i < N; i++)
                {
                    double dx = displacement[i].X;
                    double dy = displacement[i].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy) + 0.01;
                    
                    // Limit movement by temperature
                    double limitedDist = Math.Min(dist, t);
                    
                    Coordinates[i].X += (dx / dist) * limitedDist;
                    Coordinates[i].Y += (dy / dist) * limitedDist;
                    
                    // Keep within bounds
                    Coordinates[i].X = Math.Clamp(Coordinates[i].X, -1.5, 1.5);
                    Coordinates[i].Y = Math.Clamp(Coordinates[i].Y, -1.5, 1.5);
                }
            }
            
            // Normalize to [-1, 1] range
            NormalizeCoordinates();
        }

        /// <summary>
        /// Normalize coordinates to fit within [-1, 1] bounds while preserving aspect ratio.
        /// </summary>
        public void NormalizeCoordinates()
        {
            if (Coordinates == null || Coordinates.Length == 0) return;
            
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            
            for (int i = 0; i < N; i++)
            {
                minX = Math.Min(minX, Coordinates[i].X);
                maxX = Math.Max(maxX, Coordinates[i].X);
                minY = Math.Min(minY, Coordinates[i].Y);
                maxY = Math.Max(maxY, Coordinates[i].Y);
            }
            
            double rangeX = maxX - minX;
            double rangeY = maxY - minY;
            double maxRange = Math.Max(rangeX, rangeY);
            
            if (maxRange < 1e-10) maxRange = 1.0;
            
            double centerX = (minX + maxX) / 2.0;
            double centerY = (minY + maxY) / 2.0;
            
            for (int i = 0; i < N; i++)
            {
                Coordinates[i].X = 2.0 * (Coordinates[i].X - centerX) / maxRange;
                Coordinates[i].Y = 2.0 * (Coordinates[i].Y - centerY) / maxRange;
            }
        }

        /// <summary>
        /// Get coordinates for a specific node.
        /// Returns (0, 0) if coordinates not initialized.
        /// </summary>
        public (double X, double Y) GetNodeCoordinates(int nodeId)
        {
            if (Coordinates == null || nodeId < 0 || nodeId >= Coordinates.Length)
                return (0.0, 0.0);
            
            return Coordinates[nodeId];
        }

        /// <summary>
        /// Set coordinates for a specific node.
        /// Useful for interactive node dragging in UI.
        /// </summary>
        public void SetNodeCoordinates(int nodeId, double x, double y)
        {
            if (Coordinates == null)
                Coordinates = new (double X, double Y)[N];
            
            if (nodeId >= 0 && nodeId < N)
            {
                Coordinates[nodeId] = (x, y);
            }
        }

        /// <summary>
        /// Get center of mass for a set of nodes (for cluster visualization).
        /// Uses visualization coordinates, NOT for physics.
        /// </summary>
        public (double X, double Y) GetClusterCenter(IEnumerable<int> nodeIds)
        {
            if (Coordinates == null) return (0.0, 0.0);
            
            double sumX = 0, sumY = 0;
            int count = 0;
            
            foreach (int nodeId in nodeIds)
            {
                if (nodeId >= 0 && nodeId < N)
                {
                    sumX += Coordinates[nodeId].X;
                    sumY += Coordinates[nodeId].Y;
                    count++;
                }
            }
            
            if (count == 0) return (0.0, 0.0);
            
            return (sumX / count, sumY / count);
        }

        /// <summary>
        /// Get bounding box for all nodes (for UI viewport calculations).
        /// </summary>
        public (double MinX, double MinY, double MaxX, double MaxY) GetBoundingBox()
        {
            if (Coordinates == null || Coordinates.Length == 0)
                return (-1, -1, 1, 1);
            
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            
            for (int i = 0; i < N; i++)
            {
                minX = Math.Min(minX, Coordinates[i].X);
                maxX = Math.Max(maxX, Coordinates[i].X);
                minY = Math.Min(minY, Coordinates[i].Y);
                maxY = Math.Max(maxY, Coordinates[i].Y);
            }
            
            return (minX, minY, maxX, maxY);
        }
    }
}
