using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.BlockStore.Pruning
{
    /// <inheritdoc />
    public class PrunedBlockRepository : IPrunedBlockRepository
    {
        private readonly IBlockRepository blockRepository;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly ILogger logger;
        private static readonly byte[] prunedTipKey = new byte[2];
        private readonly StoreSettings storeSettings;
        private readonly BsonMapper mapper;

        /// <inheritdoc />
        public HashHeightPair PrunedTip { get; private set; }

        public PrunedBlockRepository(IBlockRepository blockRepository, DBreezeSerializer dBreezeSerializer, ILoggerFactory loggerFactory, StoreSettings storeSettings)
        {
            this.blockRepository = blockRepository;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.storeSettings = storeSettings;
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);
        }

        /// <inheritdoc />
        public Task InitializeAsync()
        {
            Task task = Task.Run(() =>
            {
                this.LoadPrunedTip();
            });

            return task;
        }

        /// <inheritdoc />
        public async Task PruneAndCompactDatabase(ChainedHeader blockRepositoryTip, Network network, bool nodeInitializing)
        {
            this.logger.LogInformation($"Pruning started.");

            if (this.PrunedTip == null)
            {
                Block genesis = network.GetGenesis();

                this.PrunedTip = new HashHeightPair(genesis.GetHash(), 0);

                LiteCollection<BsonDocument> collection = this.blockRepository.Db.GetCollection(BlockRepository.CommonTableName);
                var dbRecord = new DbRecord<byte[]> { Key = prunedTipKey, Value = this.dBreezeSerializer.Serialize(this.PrunedTip) };
                collection.Insert(this.mapper.ToDocument(dbRecord));
            }

            if (nodeInitializing)
            {
                if (this.IsDatabasePruned())
                    return;

                this.PrepareDatabaseForCompacting(blockRepositoryTip);
            }

            this.CompactDataBase();

            this.logger.LogInformation($"Pruning complete.");

            return;
        }

        private bool IsDatabasePruned()
        {
            if (this.blockRepository.TipHashAndHeight.Height <= this.PrunedTip.Height + this.storeSettings.AmountOfBlocksToKeep)
            {
                this.logger.LogDebug("(-):true");
                return true;
            }
            else
            {
                this.logger.LogDebug("(-):false");
                return false;
            }
        }

        /// <summary>
        /// Compacts the block and transaction database by recreating the tables without the deleted references.
        /// </summary>
        /// <param name="blockRepositoryTip">The last fully validated block of the node.</param>
        private void PrepareDatabaseForCompacting(ChainedHeader blockRepositoryTip)
        {
            int upperHeight = this.blockRepository.TipHashAndHeight.Height - this.storeSettings.AmountOfBlocksToKeep;

            var toDelete = new List<ChainedHeader>();

            ChainedHeader startFromHeader = blockRepositoryTip.GetAncestor(upperHeight);
            ChainedHeader endAtHeader = blockRepositoryTip.FindAncestorOrSelf(this.PrunedTip.Hash);

            this.logger.LogInformation($"Pruning blocks from height {upperHeight} to {endAtHeader.Height}.");

            while (startFromHeader.Previous != null && startFromHeader != endAtHeader)
            {
                toDelete.Add(startFromHeader);
                startFromHeader = startFromHeader.Previous;
            }

            this.blockRepository.DeleteBlocks(toDelete.Select(cb => cb.HashBlock).ToList());

            this.UpdatePrunedTip(blockRepositoryTip.GetAncestor(upperHeight));
        }

        private void LoadPrunedTip()
        {
            if (this.PrunedTip == null)
            {
                var collection = this.blockRepository.Db.GetCollection(BlockRepository.CommonTableName);
                var record = collection.FindById(prunedTipKey);
                if (record != null)
                {
                    var value = this.mapper.ToObject<DbRecord<byte[]>>(record);
                    this.PrunedTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(value.Value);
                }
            }
        }

        /// <summary>
        /// Compacts the block and transaction database by recreating the tables without the deleted references.
        /// </summary>
        private void CompactDataBase()
        {
            this.blockRepository.Db.Shrink();
        }

        /// <inheritdoc />
        public void UpdatePrunedTip(ChainedHeader tip)
        {
            this.PrunedTip = new HashHeightPair(tip);
        }
    }
}
