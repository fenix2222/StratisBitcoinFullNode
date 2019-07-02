using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stratis.FederatedSidechains.AdminDashboard.Entities;
using Stratis.FederatedSidechains.AdminDashboard.Helpers;
using Stratis.FederatedSidechains.AdminDashboard.Models;
using Stratis.FederatedSidechains.AdminDashboard.Settings;

namespace Stratis.FederatedSidechains.AdminDashboard.Services
{
    public interface INodeQueryService
    {
        
    }
    public class NodeQueryServiceBase : INodeQueryService
    {
        protected readonly ApiRequester ApiRequester;
        protected readonly string Endpoint;
        private readonly IMiningKeyReader miningKeyReader;
        private readonly DefaultEndpointsSettings settings;
        private readonly ILogger<NodeQueryServiceBase> logger;

        public NodeQueryServiceBase(ApiRequester apiRequester, string endpoint, ILoggerFactory loggerFactory, IMiningKeyReader miningKeyReader, DefaultEndpointsSettings settings)
        {
            this.ApiRequester = apiRequester;
            this.Endpoint = endpoint;
            this.miningKeyReader = miningKeyReader;
            this.settings = settings;
            this.logger = loggerFactory.CreateLogger<NodeQueryServiceBase>();
        }

        public async Task<StratisNodeModel> GetData()
        {
            var stratisNodeModel = new StratisNodeModel();
            NodeDashboardStats stats = await this.GetDashboardStats();
            NodeStatus nodeStatus = await this.GetNodeStatus();
            List<LogRule> logRules = await this.GetLogRules();
            int memoryPoolSize = await this.GetMemoryPoolSize();
            string bestHash = await this.GetBestHash();
                
            stratisNodeModel.WebAPIUrl = UriHelper.BuildUri(this.settings.StratisNode, "/api").ToString();
            stratisNodeModel.SwaggerUrl = UriHelper.BuildUri(this.settings.StratisNode, "/swagger").ToString();
            stratisNodeModel.SyncingStatus = nodeStatus?.SyncingProgress ?? 0;
            stratisNodeModel.BlockHash = bestHash;
            stratisNodeModel.BlockHeight = (int)(nodeStatus?.BlockStoreHeight ?? 0);
            stratisNodeModel.MempoolSize = memoryPoolSize;

            stratisNodeModel.CoinTicker = "STRAT";
            stratisNodeModel.LogRules = logRules;
            stratisNodeModel.Uptime = nodeStatus?.Uptime;
            stratisNodeModel.IsMining = stats?.IsMining ?? false;
            stratisNodeModel.AddressIndexer = stats?.AddressIndexerHeight ?? 0;
            stratisNodeModel.HeaderHeight = stats?.HeaderHeight ?? 0;
            stratisNodeModel.AsyncLoops = stats?.AsyncLoops ?? string.Empty;
            stratisNodeModel.OrphanSize = stats?.OrphanSize ?? string.Empty;
            stratisNodeModel.OutboundPeers = nodeStatus?.OutboundPeers as JArray;
            stratisNodeModel.InboundPeers = nodeStatus?.InboundPeers as JArray;
            
            return stratisNodeModel;
        }

        private async Task<NodeStatus> GetNodeStatus()
        {
            this.logger.LogInformation("GetNodeStatus");
            var nodeStatus = new NodeStatus();
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/Node/status");
                nodeStatus.BlockStoreHeight = response.Content.blockStoreHeight;
                nodeStatus.ConsensusHeight = response.Content.consensusHeight;
                string upTimeLargePrecision = response.Content.runningTime;
                nodeStatus.Uptime = upTimeLargePrecision.Split('.').First();
                nodeStatus.State = response.Content.state;
                
                this.logger.LogInformation("GetNodeStatus Success");
                return nodeStatus;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetNodeStatus: Failed to update node status");
                return null;
            }
        }

        private async Task<List<LogRule>> GetLogRules()
        {
            this.logger.LogInformation("GetNodeStatus");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/Node/logrules");
                List<LogRule> responseLog = JsonConvert.DeserializeObject<List<LogRule>>(response.Content.ToString());
                this.logger.LogInformation("GetNodeStatus");
                return responseLog;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to get log rules");
                return new List<LogRule>();
            }
        }

        private async Task<int> GetMemoryPoolSize()
        {
            this.logger.LogInformation("GetMempool");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/Mempool/getrawmempool");
                int memoryPoolSize = response.Content.Count;
                this.logger.LogInformation("GetMempool Success");
                return memoryPoolSize;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetMempool: Failed to get mempool info");
                return 0;
            }
        }

        private async Task<string> GetBestHash()
        {
            this.logger.LogInformation("GetBestHash");
            try
            {
                ApiResponse response = await this.ApiRequester.GetRequestAsync(this.Endpoint, "/api/Consensus/getbestblockhash");
                string hash = response.Content;
                this.logger.LogInformation("GetBestHash Success");
                return hash;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetBestHash: Failed to get best hash");
                return null;
            }
        }

        private async Task<NodeDashboardStats> GetDashboardStats()
        {
            this.logger.LogInformation("GetDashboardStats");
            var headerHeight = new Regex("Headers\\.Height:\\s+([0-9]+)", RegexOptions.Compiled);
            var orphanSize = new Regex("OrphanSize:\\s+([0-9]+)", RegexOptions.Compiled);
            var miningHistory = new Regex("at the timestamp he was supposed to\\.\\r\\n(.*)\\.\\.\\.", RegexOptions.IgnoreCase);
            var asyncLoopStats = new Regex("====== Async loops ======   (.*)", RegexOptions.Compiled);
            var addressIndexer = new Regex("AddressIndexer\\.Height:\\s+([0-9]+)", RegexOptions.Compiled);

            var nodeDashboardStats = new NodeDashboardStats();
            try
            {
                string response;
                using (HttpClient client = new HttpClient())
                {
                    response = await client.GetStringAsync($"{this.Endpoint}/api/Dashboard/Stats").ConfigureAwait(false);
                    nodeDashboardStats.OrphanSize = orphanSize.Match(response).Groups[1].Value;
                    if (int.TryParse(headerHeight.Match(response).Groups[1].Value, out var headerHeightValue))
                    {
                        nodeDashboardStats.HeaderHeight = headerHeightValue;
                    }
                    if (int.TryParse(addressIndexer.Match(response).Groups[1].Value, out var height))
                    {
                        nodeDashboardStats.AddressIndexerHeight = height;
                    }

                    nodeDashboardStats.AsyncLoops = asyncLoopStats.Match(response).Groups[1].Value.Replace("[", "").Replace("]", "").Replace(" ", "").Replace("Running", "R").Replace("Faulted", ", F");
                    var hitOrMiss = miningHistory.Match(response).Groups[1].Value.Split("-".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
                    nodeDashboardStats.MissCount = Array.FindAll(hitOrMiss, x => x.Contains("MISS")).Length;

                    string miningPubKey = this.miningKeyReader.GetKey();
                    if (!string.IsNullOrEmpty(miningPubKey))
                    {
                        nodeDashboardStats.LastMinedIndex = Array.IndexOf(hitOrMiss, $"[{miningPubKey.Substring(0, 4)}]") + 1;
                        nodeDashboardStats.IsMining = 0 < nodeDashboardStats.LastMinedIndex;
                    }
                }
                this.logger.LogInformation("GetNodeStatus Success");
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "GetDashboardStats: Failed to get /api/Dashboard/Stats");
            }

            return nodeDashboardStats;
        }

        protected static string GetPeerIp(dynamic peer)
        {
            var endpointRegex = new Regex("\\[([A-Za-z0-9:.]*)\\]:([0-9]*)");
            MatchCollection endpointMatches = endpointRegex.Matches(Convert.ToString(peer.remoteSocketEndpoint));
            if (endpointMatches.Count <= 0 || endpointMatches[0].Groups.Count <= 1)
                return string.Empty;
            var endpoint = new IPEndPoint(IPAddress.Parse(endpointMatches[0].Groups[1].Value),
                int.Parse(endpointMatches[0].Groups[2].Value));

            return
                $"{endpoint.Address.MapToIPv4()}:{endpointMatches[0].Groups[2].Value}";
        }
    }
}