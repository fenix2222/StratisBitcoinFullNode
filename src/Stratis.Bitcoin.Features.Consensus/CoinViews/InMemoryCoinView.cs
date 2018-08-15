﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Utilities;
using ReaderWriterLock = NBitcoin.ReaderWriterLock;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// TODO Activation: Move to common tests projects as it is only used for tests. Might need to implement ICoinViewStorage
    /// Coinview that holds all information in the memory, which is used in tests.
    /// </summary>
    /// <remarks>Rewinding is not supported in this implementation.</remarks>
    public class InMemoryCoinView : ICachedCoinView, ICoinViewStorage
    {
        /// <summary>Lock object to protect access to <see cref="unspents"/> and <see cref="tipHash"/>.</summary>
        private readonly ReaderWriterLock lockobj = new ReaderWriterLock();

        /// <summary>Information about unspent outputs mapped by transaction IDs the outputs belong to.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private readonly Dictionary<uint256, UnspentOutputs> unspents = new Dictionary<uint256, UnspentOutputs>();

        /// <summary>Hash of the block header which is the tip of the coinview.</summary>
        /// <remarks>All access to this object has to be protected by <see cref="lockobj"/>.</remarks>
        private uint256 tipHash;

        private CachePerformanceCounter performanceCounter;
        private BackendPerformanceCounter performanceCounter1;

        /// <summary>
        /// Initializes an instance of the object.
        /// </summary>
        /// <param name="tipHash">Hash of the block headers of the tip of the coinview.</param>
        public InMemoryCoinView(uint256 tipHash)
        {
            this.tipHash = tipHash;
        }

        /// <inheritdoc />
        public Task<uint256> GetTipHashAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(this.tipHash);
        }

        /// <inheritdoc />
        public Task PersistDataAsync(IEnumerable<UnspentOutputs> unspentOutputs, List<RewindData> rewindDataCollection, uint256 oldBlockHash, uint256 nextBlockHash)
        {
            Guard.NotNull(oldBlockHash, nameof(oldBlockHash));
            Guard.NotNull(nextBlockHash, nameof(nextBlockHash));
            Guard.NotNull(unspentOutputs, nameof(unspentOutputs));

            using (this.lockobj.LockWrite())
            {
                if ((this.tipHash != null) && (oldBlockHash != this.tipHash))
                    return Task.FromException(new InvalidOperationException("Invalid oldBlockHash"));

                this.tipHash = nextBlockHash;
                foreach (UnspentOutputs unspent in unspentOutputs)
                {
                    if (this.unspents.TryGetValue(unspent.TransactionId, out UnspentOutputs existing))
                    {
                        existing.Spend(unspent);
                    }
                    else
                    {
                        existing = unspent.Clone();
                        this.unspents.Add(unspent.TransactionId, existing);
                    }

                    if (existing.IsPrunable)
                        this.unspents.Remove(unspent.TransactionId);
                }
            }

            return Task.FromResult(true);
        }

        public async Task InitializeAsync()
        {
            await Task.CompletedTask;
        }

        BackendPerformanceCounter ICoinViewStorage.PerformanceCounter
        {
            get { return this.performanceCounter1; }
        }

        public Task GetStakeAsync(IEnumerable<StakeItem> blocklist)
        {
            throw new NotImplementedException();
        }

        public Task PutStakeAsync(IEnumerable<StakeItem> stakeEntries)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public Task<FetchCoinsResponse> FetchCoinsAsync(uint256[] txIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            Guard.NotNull(txIds, nameof(txIds));

            using (this.lockobj.LockRead())
            {
                var result = new UnspentOutputs[txIds.Length];
                for (int i = 0; i < txIds.Length; i++)
                {
                    result[i] = this.unspents.TryGet(txIds[i]);
                    if (result[i] != null)
                        result[i] = result[i].Clone();
                }

                return Task.FromResult(new FetchCoinsResponse(result, this.tipHash));
            }
        }

        /// <inheritdoc />
        public Task AddRewindDataAsync(IEnumerable<UnspentOutputs> unspentOutputs, ChainedHeader currentBlock)
        {
            Guard.NotNull(currentBlock, nameof(currentBlock));
            Guard.NotNull(unspentOutputs, nameof(unspentOutputs));

            using (this.lockobj.LockWrite())
            {
                if (this.tipHash != null)
                    return Task.FromException(new InvalidOperationException("Invalid oldBlockHash"));

                this.tipHash = currentBlock.HashBlock;
                foreach (UnspentOutputs unspent in unspentOutputs)
                {
                    UnspentOutputs existing;
                    if (this.unspents.TryGetValue(unspent.TransactionId, out existing))
                    {
                        existing.Spend(unspent);
                    }
                    else
                    {
                        existing = unspent.Clone();
                        this.unspents.Add(unspent.TransactionId, existing);
                    }

                    if (existing.IsPrunable)
                        this.unspents.Remove(unspent.TransactionId);
                }
            }

            return Task.FromResult(true);
        }

        public void Initialize()
        {  
        }

        CachePerformanceCounter ICachedCoinView.PerformanceCounter
        {
            get { return this.performanceCounter; }
            set { this.performanceCounter = value; }
        }

        public int CacheEntryCount { get; }

        /// <inheritdoc />
        public Task<uint256> Rewind()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {   
        }
    }
}
