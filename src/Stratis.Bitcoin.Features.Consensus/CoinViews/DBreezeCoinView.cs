using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.BitcoinCore;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.CoinViews
{
    /// <summary>
    /// Persistent implementation of coinview using dBreeze database.
    /// </summary>
    public class DBreezeCoinView : ICoinView, IDisposable
    {
        /// <summary>Database key under which the block hash of the coin view's current tip is stored.</summary>
        private static readonly byte[] blockHashKey = new byte[0];

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Hash of the block which is currently the tip of the coinview.</summary>
        private uint256 blockHash;

        /// <summary>Performance counter to measure performance of the database insert and query operations.</summary>
        private readonly BackendPerformanceCounter performanceCounter;

        private BackendPerformanceSnapshot latestPerformanceSnapShot;

        /// <summary>Access to dBreeze database.</summary>
        private readonly LiteDatabase db;

        private readonly BsonMapper mapper;

        private DBreezeSerializer dBreezeSerializer;
        
        private LiteCollection<BsonDocument> CoinsCollection => this.db.GetCollection("Coins");
        
        private LiteCollection<BsonDocument> BlockHashCollection => this.db.GetCollection("BlockHash");
        
        private LiteCollection<BsonDocument> RewindCollection => this.db.GetCollection("Rewind");
        
        private LiteCollection<BsonDocument> StakeCollection => this.db.GetCollection("Stake");

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="dataFolder">Information about path locations to important folders and files on disk.</param>
        /// <param name="dateTimeProvider">Provider of time functions.</param>
        /// <param name="loggerFactory">Factory to be used to create logger for the puller.</param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public DBreezeCoinView(Network network, DataFolder dataFolder, IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory, INodeStats nodeStats, DBreezeSerializer dBreezeSerializer)
            : this(network, dataFolder.CoinViewPath, dateTimeProvider, loggerFactory, nodeStats, dBreezeSerializer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - regtest/testnet/mainnet.</param>
        /// <param name="folder">Path to the folder with coinview database files.</param>
        /// <param name="dateTimeProvider">Provider of time functions.</param>
        /// <param name="loggerFactory">Factory to be used to create logger for the puller.</param>
        /// <param name="nodeStats"></param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public DBreezeCoinView(Network network, string folder, IDateTimeProvider dateTimeProvider,
            ILoggerFactory loggerFactory, INodeStats nodeStats, DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotEmpty(folder, nameof(folder));

            this.dBreezeSerializer = dBreezeSerializer;

            // Create the coinview folder if it does not exist.
            Directory.CreateDirectory(folder);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<int, byte[]>>().Id(p => p.Key);
            this.network = network;
            this.performanceCounter = new BackendPerformanceCounter(dateTimeProvider);

            nodeStats.RegisterStats(this.AddBenchStats, StatsType.Benchmark, 400);

        }

        /// <summary>
        /// Initializes the database tables used by the coinview.
        /// </summary>
        public void Initialize()
        {
            Block genesis = this.network.GetGenesis();

            if (this.GetTipHash() == null)
            {
                this.SetBlockHash(genesis.GetHash());
            }
        }

        /// <inheritdoc />
        public uint256 GetTipHash(CancellationToken cancellationToken = default(CancellationToken))
        {
            uint256 tipHash;

            tipHash = this.GetTipHash();

            return tipHash;
        }

        /// <inheritdoc />
        public FetchCoinsResponse FetchCoins(uint256[] txIds, CancellationToken cancellationToken = default(CancellationToken))
        {
            FetchCoinsResponse res = null;
            using (new StopwatchDisposable(o => this.performanceCounter.AddQueryTime(o)))
            {
                uint256 blockHash = this.GetTipHash();
                var result = new UnspentOutputs[txIds.Length];
                this.performanceCounter.AddQueriedEntities(txIds.Length);

                int i = 0;
                foreach (uint256 input in txIds)
                {
                    BsonDocument row = this.CoinsCollection.FindById(input.ToBytes(false));
                    UnspentOutputs outputs = row != null ? new UnspentOutputs(input, this.dBreezeSerializer.Deserialize<Coins>(row.ToDbRecord<byte[], byte[]>(this.mapper).Value)) : null;

                    this.logger.LogTrace("Outputs for '{0}' were {1}.", input, outputs == null ? "NOT loaded" : "loaded");

                    result[i++] = outputs;
                }

                res = new FetchCoinsResponse(result, blockHash);
            }

            return res;
        }

        /// <summary>
        /// Obtains a block header hash of the coinview's current tip.
        /// </summary>
        /// <param name="transaction">Open dBreeze transaction.</param>
        /// <returns>Block header hash of the coinview's current tip.</returns>
        private uint256 GetTipHash()
        {
            if (this.blockHash == null)
            {
                var row = this.BlockHashCollection.FindById(blockHashKey);
                if (row != null)
                    this.blockHash = new uint256(row.ToDbRecord<byte[], byte[]>(this.mapper).Value);
            }

            return this.blockHash;
        }

        /// <summary>
        /// Set's the tip of the coinview to a new block hash.
        /// </summary>
        /// <param name="transaction">Open dBreeze transaction.</param>
        /// <param name="nextBlockHash">Hash of the block to become the new tip.</param>
        private void SetBlockHash(uint256 nextBlockHash)
        {
            this.blockHash = nextBlockHash;
            this.BlockHashCollection.Upsert(new DbRecord<byte[], byte[]>(blockHashKey, nextBlockHash.ToBytes()).ToDocument(this.mapper));
        }

        /// <inheritdoc />
        public void SaveChanges(IList<UnspentOutputs> unspentOutputs, IEnumerable<TxOut[]> originalOutputs, uint256 oldBlockHash, uint256 nextBlockHash, int height, List<RewindData> rewindDataList = null)
        {
            int insertedEntities = 0;

            using (new StopwatchDisposable(o => this.performanceCounter.AddInsertTime(o)))
            {
                uint256 current = this.GetTipHash();
                if (current != oldBlockHash)
                {
                    this.logger.LogTrace("(-)[BLOCKHASH_MISMATCH]");
                    throw new InvalidOperationException("Invalid oldBlockHash");
                }

                this.SetBlockHash(nextBlockHash);

                // Here we'll add items to be inserted in a second pass.
                List<UnspentOutputs> toInsert = new List<UnspentOutputs>();

                foreach (var coin in unspentOutputs.OrderBy(utxo => utxo.TransactionId, new UInt256Comparer()))
                {
                    if (coin.IsPrunable)
                    {
                        this.logger.LogTrace("Outputs of transaction ID '{0}' are prunable and will be removed from the database.", coin.TransactionId);
                        this.CoinsCollection.Delete(coin.TransactionId.ToBytes(false));
                    }
                    else
                    {
                        // Add the item to another list that will be used in the second pass.
                        // This is for performance reasons: dBreeze is optimized to run the same kind of operations, sorted.
                        toInsert.Add(coin);
                    }
                }

                for (int i = 0; i < toInsert.Count; i++)
                {
                    var coin = toInsert[i];
                    this.logger.LogTrace("Outputs of transaction ID '{0}' are NOT PRUNABLE and will be inserted into the database. {1}/{2}.", coin.TransactionId, i, toInsert.Count);

                    this.CoinsCollection.Upsert(new DbRecord<byte[], byte[]>(coin.TransactionId.ToBytes(false), this.dBreezeSerializer.Serialize(coin.ToCoins())).ToDocument(this.mapper));
                }

                if (rewindDataList != null)
                {
                    int nextRewindIndex = this.GetRewindIndex() + 1;
                    foreach (RewindData rewindData in rewindDataList)
                    {
                        this.logger.LogTrace("Rewind state #{0} created.", nextRewindIndex);

                        this.RewindCollection.Upsert(
                            new DbRecord<int, byte[]>(nextRewindIndex, this.dBreezeSerializer.Serialize(rewindData))
                                .ToDocument(this.mapper));
                        nextRewindIndex++;
                    }
                }

                insertedEntities += unspentOutputs.Count;
            }

            this.performanceCounter.AddInsertedEntities(insertedEntities);
        }

        /// <summary>
        /// Obtains order number of the last saved rewind state in the database.
        /// </summary>
        /// <param name="transaction">Open dBreeze transaction.</param>
        /// <returns>Order number of the last saved rewind state, or <c>0</c> if no rewind state is found in the database.</returns>
        /// <remarks>TODO: Using <c>0</c> is hacky here, and <see cref="SaveChanges"/> exploits that in a way that if no such rewind data exist
        /// the order number of the first rewind data is 0 + 1 = 1.</remarks>
        private int GetRewindIndex()
        {
            var firstRow = this.RewindCollection.FindAll().LastOrDefault();

            return firstRow != null ? firstRow.AsDocument.ToDbRecord<int, byte[]>(this.mapper).Key : 0;
        }

        public RewindData GetRewindData(int height)
        {
            var row = this.RewindCollection.FindById(height);
            return row != null ? this.dBreezeSerializer.Deserialize<RewindData>(row.ToDbRecord<int, byte[]>(this.mapper).Value) : null;
        }

        /// <inheritdoc />
        public uint256 Rewind()
        {
            uint256 res = null;
            if (this.GetRewindIndex() == 0)
            {
                this.CoinsCollection.Delete(c => true);
                this.SetBlockHash(this.network.GenesisHash);

                res = this.network.GenesisHash;
            }
            else
            {
                var firstRow = this.RewindCollection.FindAll().LastOrDefault();
                if (firstRow != null)
                {
                    var record = firstRow.AsDocument.ToDbRecord<int, byte[]>(this.mapper);
                    this.RewindCollection.Delete(record.Key);
                    var rewindData = this.dBreezeSerializer.Deserialize<RewindData>(record.Value);
                    this.SetBlockHash(rewindData.PreviousBlockHash);

                    foreach (uint256 txId in rewindData.TransactionsToRemove)
                    {
                        this.logger.LogTrace("Outputs of transaction ID '{0}' will be removed.", txId);
                        this.CoinsCollection.Delete(txId.ToBytes(false));
                    }

                    foreach (UnspentOutputs coin in rewindData.OutputsToRestore)
                    {
                        this.logger.LogTrace("Outputs of transaction ID '{0}' will be restored.", coin.TransactionId);
                        this.CoinsCollection.Upsert(new DbRecord<byte[], byte[]>(coin.TransactionId.ToBytes(false), this.dBreezeSerializer.Serialize(coin.ToCoins())).ToDocument(this.mapper));
                    }

                    res = rewindData.PreviousBlockHash;
                }
            }

            return res;
        }

        /// <summary>
        /// Persists unsaved POS blocks information to the database.
        /// </summary>
        /// <param name="stakeEntries">List of POS block information to be examined and persists if unsaved.</param>
        public void PutStake(IEnumerable<StakeItem> stakeEntries)
        {
            this.PutStakeInternal(stakeEntries);
        }

        /// <summary>
        /// Persists unsaved POS blocks information to the database.
        /// </summary>
        /// <param name="transaction">Open dBreeze transaction.</param>
        /// <param name="stakeEntries">List of POS block information to be examined and persists if unsaved.</param>
        private void PutStakeInternal(IEnumerable<StakeItem> stakeEntries)
        {
            foreach (StakeItem stakeEntry in stakeEntries)
            {
                if (!stakeEntry.InStore)
                {
                    this.StakeCollection.Upsert(new DbRecord<byte[], byte[]>(stakeEntry.BlockId.ToBytes(false),
                        this.dBreezeSerializer.Serialize(stakeEntry.BlockStake)).ToDocument(this.mapper));
                    stakeEntry.InStore = true;
                }
            }
        }

        /// <summary>
        /// Retrieves POS blocks information from the database.
        /// </summary>
        /// <param name="blocklist">List of partially initialized POS block information that is to be fully initialized with the values from the database.</param>
        public void GetStake(IEnumerable<StakeItem> blocklist)
        {
            foreach (StakeItem blockStake in blocklist)
            {
                this.logger.LogTrace("Loading POS block hash '{0}' from the database.", blockStake.BlockId);
                BsonDocument stakeRow = this.StakeCollection.FindById(blockStake.BlockId.ToBytes(false));

                if (stakeRow != null)
                {
                    blockStake.BlockStake = this.dBreezeSerializer.Deserialize<BlockStake>(stakeRow.ToDbRecord<byte[], byte[]>(this.mapper).Value);
                    blockStake.InStore = true;
                }
            }
        }

        private void AddBenchStats(StringBuilder log)
        {
            log.AppendLine("======DBreezeCoinView Bench======");

            BackendPerformanceSnapshot snapShot = this.performanceCounter.Snapshot();

            if (this.latestPerformanceSnapShot == null)
                log.AppendLine(snapShot.ToString());
            else
                log.AppendLine((snapShot - this.latestPerformanceSnapShot).ToString());

            this.latestPerformanceSnapShot = snapShot;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.db.Dispose();
        }
    }
}
