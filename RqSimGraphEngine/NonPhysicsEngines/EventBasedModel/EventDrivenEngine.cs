using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation.EventBasedModel
{
    /// <summary>
    /// Event-Driven Simulation Engine using Discrete Event Simulation (DES)
    /// Implements proper relational time evolution per RQ-Hypothesis
    /// 
    /// Key improvement: Each node has its own proper time ? instead of global synchronous time steps.
    /// Events are scheduled in a priority queue by local time, respecting relativity.
    /// </summary>
    public class EventDrivenEngine
    {
        private readonly RQGraph _graph;
        private readonly PriorityQueue<int, double> _eventQueue;
        private double _globalClock;
        private readonly double _totalTime;
        private readonly double[] _nodeProperTimes;
        private readonly Random _rng;

        public EventDrivenEngine(RQGraph graph, double totalTime, int seed = 42)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _totalTime = totalTime;
            _eventQueue = new PriorityQueue<int, double>();
            _nodeProperTimes = new double[graph.N];
            _globalClock = 0.0;
            _rng = new Random(seed);
        }

        /// <summary>
        /// Initialize the event queue with all nodes at time 0
        /// </summary>
        public void Initialize()
        {
            _eventQueue.Clear();
            for (int i = 0; i < _graph.N; i++)
            {
                _nodeProperTimes[i] = 0.0;
                _eventQueue.Enqueue(i, 0.0);
            }
            _globalClock = 0.0;
        }

        /// <summary>
        /// Run the event-driven simulation
        /// </summary>
        public void Run()
        {
            Initialize();

            int eventCount = 0;
            while (_globalClock < _totalTime)
            {
                if (!_eventQueue.TryDequeue(out int nodeId, out double localTime))
                    break;

                // Update global clock (for statistics only - not used in physics)
                _globalClock = localTime;

                // 1. Compute local proper time increment for this node
                double dt_node = _graph.ComputeLocalProperTime(nodeId);
                if (dt_node <= 0) dt_node = 0.01; // Fallback

                // 2. Update physics for this node and its neighbors
                _graph.UpdateNodePhysics(nodeId, dt_node);

                // 3. Compute time dilation factor (from local curvature/energy)
                double timeDilation = _graph.GetTimeDilation(nodeId);

                // 4. Schedule next event for this node
                double nextTime = localTime + dt_node * timeDilation;
                if (nextTime < _totalTime)
                {
                    _eventQueue.Enqueue(nodeId, nextTime);
                }

                _nodeProperTimes[nodeId] = nextTime;
                eventCount++;

                // Periodic statistics (every 1000 events)
                if (eventCount % 1000 == 0)
                {
                    //Console.WriteLine($"[DES] Events: {eventCount}, GlobalClock: {_globalClock:F3}, QueueSize: {_eventQueue.Count}");
                }
            }

            //Console.WriteLine($"[DES] Simulation complete. Total events: {eventCount}, Final time: {_globalClock:F3}");
        }

        /// <summary>
        /// Get the proper time for a specific node
        /// </summary>
        public double GetNodeProperTime(int nodeId)
        {
            if (nodeId < 0 || nodeId >= _nodeProperTimes.Length)
                return 0.0;
            return _nodeProperTimes[nodeId];
        }

        /// <summary>
        /// Get current global clock value (coordinate time, for statistics only)
        /// </summary>
        public double GlobalClock => _globalClock;
    }
}
