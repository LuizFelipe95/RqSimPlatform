using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    /// <summary>
    /// A topology supporting dynamic updates and weighted edges. This class
    /// extends the basic adjacency lists with metrics on physical and
    /// informational layers. Users can query distances and delays, and
    /// update or remove edges on the fly.
    /// </summary>
    public class DynamicTopology
    {
        private readonly Dictionary<string, HashSet<string>> _physical = new();
        private readonly Dictionary<string, HashSet<string>> _information = new();
        private readonly Dictionary<(string, string), double> _physicalDistances = new();
        private readonly Dictionary<(string, string), double> _informationDelays = new();
        private readonly Dictionary<(string, string), double> _trust = new();

        public void AddNode(string id)
        {
            if (!_physical.ContainsKey(id)) _physical[id] = new HashSet<string>();
            if (!_information.ContainsKey(id)) _information[id] = new HashSet<string>();
        }

        /// <summary>
        /// Add or update an undirected physical edge with a given distance.
        /// </summary>
        public void AddPhysicalEdge(string a, string b, double distance = 1.0)
        {
            AddNode(a);
            AddNode(b);
            _physical[a].Add(b);
            _physical[b].Add(a);
            _physicalDistances[(a, b)] = distance;
            _physicalDistances[(b, a)] = distance;
        }

        /// <summary>
        /// Remove a physical edge if it exists.
        /// </summary>
        public void RemovePhysicalEdge(string a, string b)
        {
            if (_physical.TryGetValue(a, out var setA))
            {
                setA.Remove(b);
            }
            if (_physical.TryGetValue(b, out var setB))
            {
                setB.Remove(a);
            }
            _physicalDistances.Remove((a, b));
            _physicalDistances.Remove((b, a));
        }

        /// <summary>
        /// Add or update a directed information edge with an associated delay.
        /// </summary>
        public void AddInformationEdge(string source, string target, double delay = 1.0)
        {
            AddNode(source);
            AddNode(target);
            _information[source].Add(target);
            _informationDelays[(source, target)] = delay;
        }

        /// <summary>
        /// Remove an information edge if it exists.
        /// </summary>
        public void RemoveInformationEdge(string source, string target)
        {
            if (_information.TryGetValue(source, out var set))
            {
                set.Remove(target);
            }
            _informationDelays.Remove((source, target));
        }

        /// <summary>
        /// Set or update the trust weight from one observer to another. Values
        /// outside [0,1] are clamped. Setting weight <= 0 removes the edge.
        /// </summary>
        public void SetTrust(string from, string to, double weight)
        {
            if (weight <= 0.0)
            {
                _trust.Remove((from, to));
            }
                else
            {
                _trust[(from, to)] = Math.Clamp(weight, 0.0, 1.0);
            }
        }

        public IEnumerable<string> PhysicalNeighbours(string id) =>
            _physical.TryGetValue(id, out var set) ? set : Enumerable.Empty<string>();

        public IEnumerable<string> InformationTargets(string id) =>
            _information.TryGetValue(id, out var set) ? set : Enumerable.Empty<string>();

        public double GetPhysicalDistance(string a, string b)
        {
            return _physicalDistances.TryGetValue((a, b), out var d) ? d : double.PositiveInfinity;
        }

        public double GetInformationDelay(string source, string target)
        {
            return _informationDelays.TryGetValue((source, target), out var d) ? d : double.PositiveInfinity;
        }

        public double GetTrust(string from, string to)
        {
            return _trust.TryGetValue((from, to), out var w) ? w : 0.0;
        }
    }

    public partial class RQGraph
    {
        
    }
}