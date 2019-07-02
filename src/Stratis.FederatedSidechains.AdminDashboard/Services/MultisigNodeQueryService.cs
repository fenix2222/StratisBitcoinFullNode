using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stratis.FederatedSidechains.AdminDashboard.Entities;
using Stratis.FederatedSidechains.AdminDashboard.Models;
using Stratis.FederatedSidechains.AdminDashboard.Settings;

namespace Stratis.FederatedSidechains.AdminDashboard.Services
{
    public interface IMultisigNodeQueryService
    {
        Task<T> GetMultisigData<T>() where T : StratisNodeModel;
    }
    
    public class MultisigNodeQueryServiceBase : NodeQueryServiceBase, IMultisigNodeQueryService
    {
        private readonly ILogger<MultisigNodeQueryServiceBase> logger;
        private const int Stratoshi = 100_000_000;
        
        public MultisigNodeQueryServiceBase(ApiRequester apiRequester, string endpoint, ILoggerFactory loggerFactory, IMiningKeyReader miningKeyReader, DefaultEndpointsSettings settings) 
            : base(apiRequester, endpoint, loggerFactory, miningKeyReader, settings)
        {
            this.logger = loggerFactory.CreateLogger<MultisigNodeQueryServiceBase>();
        }

        public async Task<T> GetMultisigData<T>() where T : StratisNodeModel
        {
            StratisNodeModel model = await this.GetData();
            
            (double confirmedBalance, double unconfirmedBalance) balance = await this.GetWalletBalance();
            dynamic history = await this.GetHistory();
            string federationInfo = await this.GetFedInfo();
            
            model.ConfirmedBalance = balance.confirmedBalance;
            model.UnconfirmedBalance = balance.unconfirmedBalance;
            model.History = history ?? new JArray();
            this.ParsePeers(federationInfo ?? string.Empty, model);
            return model as T;
        }
        
        private async Task<(double confirmedBalance, double unconfirmedBalance)> GetWalletBalance()
        {
            this.logger.LogInformation("GetWalletBalance");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/FederationWallet/balance");
                double confirmed = response.Content.balances[0].amountConfirmed / Stratoshi;
                double unconfirmed = response.Content.balances[0].amountUnconfirmed / Stratoshi;
                this.logger.LogInformation("GetWalletBalance Success");
                return (confirmed, unconfirmed);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetWalletBalance: Failed to get wallet balance");
                return (0, 0);
            }
        }

        private async Task<dynamic> GetHistory()
        {
            this.logger.LogInformation("GetHistory");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/FederationWallet/history", "maxEntriesToReturn=30");
                this.logger.LogInformation("GetHistory Success");
                return response.Content;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetHistory: Failed to get history");
                return null;
            }
        }

        private async Task<string> GetFedInfo()
        {
            this.logger.LogInformation("GetFedInfo");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/FederationGateway/info");
                string fedAddress = response.Content.multisigAddress;
                this.logger.LogInformation("GetFedInfo Success");
                return fedAddress;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetFedInfo: Failed to fed info");
                return null;
            }
        }
        
        private void ParsePeers(string fedEndpoints, StratisNodeModel model)
        {
            if (model.OutboundPeers != null)
            {
                this.LoadPeers(fedEndpoints, model.OutboundPeers, "outbound", model);
            }

            if (model.InboundPeers != null)
            {
                this.LoadPeers(fedEndpoints, model.InboundPeers, "inbound", model);
            }
        }
        
        private void LoadPeers(string fedEndpoints, JArray peersToProcess, string direction, StratisNodeModel model)
        {
            foreach (dynamic peer in peersToProcess)
            {
                string peerIp = GetPeerIp(peer);
                var peerToAdd = new Peer
                {
                    Endpoint = peer.remoteSocketEndpoint,
                    Type = direction,
                    Height = peer.tipHeight,
                    Version = peer.version
                };

                if (fedEndpoints.Contains(peerIp))
                    model.FederationMembers.Add(peerToAdd);
                else
                    model.Peers.Add(peerToAdd);
            }
        }
    }
    
    public class MainchainMultisigNodeQueryService : MultisigNodeQueryServiceBase, IMultisigNodeQueryService
    {
        public MainchainMultisigNodeQueryService(ApiRequester apiRequester, ILoggerFactory loggerFactory, IMiningKeyReader miningKeyReader, DefaultEndpointsSettings settings) 
            : base(apiRequester, settings.StratisNode, loggerFactory, miningKeyReader, settings)
        {
        }
    }
    
    public class SidechainMultisigNodeQueryService : MultisigNodeQueryServiceBase, IMultisigNodeQueryService
    {
        private readonly ILogger<MultisigNodeQueryServiceBase> logger;
        
        public SidechainMultisigNodeQueryService(ApiRequester apiRequester, ILoggerFactory loggerFactory, IMiningKeyReader miningKeyReader, DefaultEndpointsSettings settings) 
            : base(apiRequester, settings.SidechainNode, loggerFactory, miningKeyReader, settings)
        {
        }

        public async Task<T> GetMultisigData<T>() where T : StratisNodeModel
        {
            StratisNodeModel model = await base.GetMultisigData<StratisNodeModel>();
            var sidechainNode = new SidechainNodeModel(model);
            
            return sidechainNode as T;
        }

        private async Task<List<PendingPoll>> GetPolls()
        {
            this.logger.LogInformation("GetPolls");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/DefaultVoting/pendingpolls");
                List<PendingPoll> polls = JsonConvert.DeserializeObject<List<PendingPoll>>(response.Content.ToString());
                this.logger.LogInformation("GetPolls Success");
                return polls;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetPolls: Failed to update polls");
                return new List<PendingPoll>();
            }
        }
    }
}