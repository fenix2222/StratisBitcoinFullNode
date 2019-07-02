using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stratis.FederatedSidechains.AdminDashboard.Services
{
    public class NodeStatus
    {
        public float SyncingProgress => ConsensusHeight > 0 ? (BlockStoreHeight / ConsensusHeight) * 100 : 0; 
        public float BlockStoreHeight { get; set; } = 0;

        public float ConsensusHeight { get; set; } = 0;

        public string Uptime { get; set; } = string.Empty;

        public string State { get; set; } = "Not Operational";
        
        public dynamic OutboundPeers { get; set; }
        
        public dynamic InboundPeers { get; set; }
    }

    public class NodeDashboardStats
    {
        public int HeaderHeight { get; set; } = 0;
        public string AsyncLoops { get; set; } = string.Empty;
        public int AddressIndexerHeight { get; set; } = 0;
        public string OrphanSize { get; set; } = string.Empty;
        public int MissCount { get; set; } = 0;
        public bool IsMining { get; set; } = false;
        public int LastMinedIndex { get; set; } = 0;
    }
}
