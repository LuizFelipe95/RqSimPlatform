using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.Physics
{
    /// <summary>
    /// 3D vector for momentum representation.
    /// </summary>
    public readonly struct Vector3D
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Vector3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3D Zero => new(0, 0, 0);

        public static Vector3D operator +(Vector3D a, Vector3D b) =>
            new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3D operator *(Vector3D v, double s) =>
            new(v.X * s, v.Y * s, v.Z * s);

        public static Vector3D operator /(Vector3D v, double s) =>
            new(v.X / s, v.Y / s, v.Z / s);

        public double MagnitudeSquared => X * X + Y * Y + Z * Z;
        public double Magnitude => Math.Sqrt(MagnitudeSquared);
    }

    /// <summary>
    /// Represents momentum and inertial properties of clusters of nodes.
    /// Provides conservation and transfer of momentum during
    /// topological changes, addressing checklist item #8.
    /// </summary>
    /// <typeparam name="TNode">Type representing a graph node.</typeparam>
    public sealed class ClusterMomentum<TNode> where TNode : notnull
    {
        private readonly Dictionary<TNode, Vector3D> _nodeVelocities = new();
        private readonly Dictionary<TNode, double> _nodeMasses = new();

        /// <summary>
        /// Sets the mass and velocity of a node.
        /// </summary>
        public void SetNodeState(TNode node, double mass, Vector3D velocity)
        {
            _nodeMasses[node] = mass;
            _nodeVelocities[node] = velocity;
        }

        /// <summary>
        /// Gets the mass of a node. Returns 0 if node not registered.
        /// </summary>
        public double GetMass(TNode node) =>
            _nodeMasses.TryGetValue(node, out var m) ? m : 0.0;

        /// <summary>
        /// Gets the velocity of a node. Returns zero vector if node not registered.
        /// </summary>
        public Vector3D GetVelocity(TNode node) =>
            _nodeVelocities.TryGetValue(node, out var v) ? v : Vector3D.Zero;

        /// <summary>
        /// Computes the total momentum of a cluster as mass-weighted
        /// sum of velocities. p = ? m_i v_i
        /// </summary>
        public Vector3D ComputeTotalMomentum(IEnumerable<TNode> cluster)
        {
            double px = 0, py = 0, pz = 0;

            foreach (var node in cluster)
            {
                double m = _nodeMasses.TryGetValue(node, out var mass) ? mass : 0.0;
                var v = _nodeVelocities.TryGetValue(node, out var vel) ? vel : Vector3D.Zero;

                px += m * v.X;
                py += m * v.Y;
                pz += m * v.Z;
            }

            return new Vector3D(px, py, pz);
        }

        /// <summary>
        /// Computes the total mass of a cluster.
        /// </summary>
        public double ComputeTotalMass(IEnumerable<TNode> cluster) =>
            cluster.Sum(n => _nodeMasses.TryGetValue(n, out var m) ? m : 0.0);

        /// <summary>
        /// Computes the center of mass velocity for a cluster.
        /// v_cm = (? m_i v_i) / (? m_i)
        /// </summary>
        public Vector3D ComputeCenterOfMassVelocity(IEnumerable<TNode> cluster)
        {
            var p = ComputeTotalMomentum(cluster);
            double totalMass = ComputeTotalMass(cluster);

            if (totalMass <= 0) return Vector3D.Zero;

            return p / totalMass;
        }

        /// <summary>
        /// Distributes momentum across nodes in a cluster based on mass ratios.
        /// Sets all nodes to the same center-of-mass velocity.
        /// </summary>
        public void DistributeMomentum(IEnumerable<TNode> cluster, Vector3D momentum)
        {
            var nodes = cluster.ToList();
            double totalMass = ComputeTotalMass(nodes);

            if (totalMass <= 0) return;

            var vcm = momentum / totalMass;

            foreach (var node in nodes)
                _nodeVelocities[node] = vcm;
        }

        /// <summary>
        /// Handles cluster merger: combines momentum of two clusters and
        /// redistributes to the merged cluster.
        /// </summary>
        public void MergeClusters(IEnumerable<TNode> cluster1, IEnumerable<TNode> cluster2)
        {
            var p1 = ComputeTotalMomentum(cluster1);
            var p2 = ComputeTotalMomentum(cluster2);

            var totalMomentum = p1 + p2;
            var mergedCluster = cluster1.Concat(cluster2);

            DistributeMomentum(mergedCluster, totalMomentum);
        }

        /// <summary>
        /// Handles cluster split: conserves total momentum by distributing
        /// parent momentum to child clusters based on their mass ratios.
        /// </summary>
        public void SplitCluster(IEnumerable<TNode> parentCluster, IEnumerable<IEnumerable<TNode>> childClusters)
        {
            var parentMomentum = ComputeTotalMomentum(parentCluster);
            double parentMass = ComputeTotalMass(parentCluster);

            if (parentMass <= 0) return;

            foreach (var child in childClusters)
            {
                double fraction = ComputeTotalMass(child) / parentMass;
                DistributeMomentum(child, parentMomentum * fraction);
            }
        }

        /// <summary>
        /// Computes kinetic energy of a cluster: E = ? (1/2) m_i v_i?
        /// </summary>
        public double ComputeKineticEnergy(IEnumerable<TNode> cluster)
        {
            double energy = 0;

            foreach (var node in cluster)
            {
                double m = _nodeMasses.TryGetValue(node, out var mass) ? mass : 0.0;
                var v = _nodeVelocities.TryGetValue(node, out var vel) ? vel : Vector3D.Zero;

                energy += 0.5 * m * v.MagnitudeSquared;
            }

            return energy;
        }

        /// <summary>
        /// Removes a node from tracking.
        /// </summary>
        public void RemoveNode(TNode node)
        {
            _nodeMasses.Remove(node);
            _nodeVelocities.Remove(node);
        }

        /// <summary>
        /// Clears all tracked nodes.
        /// </summary>
        public void Clear()
        {
            _nodeMasses.Clear();
            _nodeVelocities.Clear();
        }
    }
}
