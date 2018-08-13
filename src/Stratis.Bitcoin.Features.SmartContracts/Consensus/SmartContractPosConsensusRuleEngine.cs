﻿using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.CoinViews;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.Consensus.Rules;
using Stratis.Bitcoin.Utilities;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Bitcoin.Features.SmartContracts.Consensus
{
    /// <summary>
    /// Extension of consensus rules that provide access to a PoS store.
    /// </summary>
    public sealed class SmartContractPosConsensusRuleEngine : PosConsensusRuleEngine, ISmartContractCoinviewRule
    {
        public ISmartContractExecutorFactory ExecutorFactory { get; private set; }
        public ContractStateRepositoryRoot OriginalStateRoot { get; private set; }
        public ISmartContractReceiptStorage ReceiptStorage { get; private set; }

        public SmartContractPosConsensusRuleEngine(
            ConcurrentChain chain,
            ICheckpoints checkpoints,
            ConsensusSettings consensusSettings,
            IDateTimeProvider dateTimeProvider,
            ISmartContractExecutorFactory executorFactory,
            ILoggerFactory loggerFactory,
            Network network,
            NodeDeployments nodeDeployments,
            ContractStateRepositoryRoot originalStateRoot,
            ISmartContractReceiptStorage receiptStorage,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            ICachedCoinView utxoSet,
            IChainState chainState)
            : base(network, loggerFactory, dateTimeProvider, chain, nodeDeployments, consensusSettings, checkpoints, utxoSet, stakeChain, stakeValidator, chainState)
        {
            this.ExecutorFactory = executorFactory;
            this.OriginalStateRoot = originalStateRoot;
            this.ReceiptStorage = receiptStorage;
        }

        /// <inheritdoc />
        public override RuleContext CreateRuleContext(ValidationContext validationContext)
        {
            return new PosRuleContext(validationContext, this.DateTimeProvider.GetTimeOffset());
        }
    }
}