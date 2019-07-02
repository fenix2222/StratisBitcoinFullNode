using System.Collections.Generic;
using Stratis.FederatedSidechains.AdminDashboard.Entities;

namespace Stratis.FederatedSidechains.AdminDashboard.Models
{
    public class SidechainNodeModel : StratisNodeModel
    {
        public SidechainNodeModel()
        {
            
        }

        public SidechainNodeModel(StratisNodeModel model)
        {
            this.History = model.History;
            this.Peers = model.Peers;
            this.Uptime = model.Uptime;
            this.BlockHeight = model.BlockHeight;
            this.FederationMembers = model.FederationMembers;
            this.InboundPeers = model.InboundPeers;
            this.OutboundPeers = model.OutboundPeers;
            this.PoAPendingPolls = new List<PendingPoll>();
            this.AddressIndexer = model.AddressIndexer;
            this.AsyncLoops = model.AsyncLoops;
            this.IsMining = model.IsMining;
            this.SyncingStatus = model.SyncingStatus;
            this.WebAPIUrl = model.WebAPIUrl;
            this.SwaggerUrl = model.SwaggerUrl;
            this.MempoolSize = model.MempoolSize;
            this.BlockHash = model.BlockHash;
            this.ConfirmedBalance = model.ConfirmedBalance;
            this.UnconfirmedBalance = model.UnconfirmedBalance;
            this.LogRules = model.LogRules;
            this.CoinTicker = model.CoinTicker;
            this.OrphanSize = model.OrphanSize;
            this.HeaderHeight = model.HeaderHeight;
        }
        
        public List<PendingPoll> PoAPendingPolls { get; set; }
    }
}