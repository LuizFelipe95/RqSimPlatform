using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public partial class RQGraph
    {
        private ClusterTracker? _clusterTrackerShared;

        public void AttachClusterTracker(ClusterTracker tracker)
        {
            _clusterTrackerShared = tracker;
        }

        public IEnumerable<ClusterTrack>? GetClusterTracks()
        {
            return _clusterTrackerShared?.GetTracks();
        }

        public ClusterTrack? GetLongestClusterTrack()
        {
            return _clusterTrackerShared?.GetTracks()?.OrderByDescending(t => t.LifetimeSteps).FirstOrDefault();
        }
    }
}