﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Stratis.FederatedSidechains.AdminDashboard.Entities
{
    public class PendingPoll
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        
        [JsonProperty("isPending")]
        public bool IsPending { get; set; }
        
        [JsonProperty("isExecuted")]
        public bool IsExecuted { get; set; }
        
        [JsonProperty("pollVotedInFavorBlockData")]
        public string PollVotedInFavorBlockData { get; set; }
        
        [JsonProperty("pollStartBlockData")]
        public string PollStartBlockData { get; set; }
        
        [JsonProperty("pollExecutedBlockData")]
        public string PollExecutedBlockData { get; set; }
        
        [JsonProperty("pubKeysHexVotedInFavor")]
        public List<string> PubKeysHexVotedInFavor { get; set; }

        [JsonProperty("votingDataString")]
        public string VotingDataString { get; set; }

        [JsonIgnore]
        public string Hash
        {
            get
            {
                if (string.IsNullOrEmpty(this.VotingDataString)) return string.Empty;
                string[] tokens = this.VotingDataString.Split(',');
                if (tokens.Length < 1) return string.Empty;
                string hashToken = tokens.FirstOrDefault(t => t.StartsWith("hash", StringComparison.OrdinalIgnoreCase));
                if (hashToken == null) return string.Empty;
                string[] hashTokens = hashToken.Split(':');
                return hashTokens.Length < 2 ? string.Empty : hashTokens[1].Replace("'", string.Empty);
            }
        }
    }

    public class HashHeightPairModel
    {
        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }
}
