using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Base
{
    public interface IChainRepository : IDisposable
    {
        /// <summary>Loads the chain of headers from the database.</summary>
        /// <returns>Tip of the loaded chain.</returns>
        Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader);

        /// <summary>Persists chain of headers to the database.</summary>
        Task SaveAsync(ChainIndexer chainIndexer);
    }

    public class ChainRepository : IChainRepository
    {
        private readonly DBreezeSerializer dBreezeSerializer;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Access to DBreeze database.</summary>
        private readonly string folder;
        private readonly BsonMapper mapper;
        private readonly LiteDatabase db;

        private BlockLocator locator;

        public ChainRepository(string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
        {
            this.dBreezeSerializer = dBreezeSerializer;
            Guard.NotEmpty(folder, nameof(folder));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            Directory.CreateDirectory(folder);
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord<int>>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<int, byte[]>>().Id(p => p.Key);
            this.db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");
        }

        public ChainRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
            : this(dataFolder.ChainPath, loggerFactory, dBreezeSerializer)
        {
        }

        /// <inheritdoc />
        public Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader)
        {
            Task<ChainedHeader> task = Task.Run(() =>
            {
                LiteCollection<BsonDocument> collection = this.db.GetCollection("Chain");
                ChainedHeader tip = null;
                var record = collection.FindById(0);

                if (record == null)
                    return genesisHeader;

                var value = this.mapper.ToObject<DbRecord<int>>(record).Value;
                BlockHeader previousHeader = this.dBreezeSerializer.Deserialize<BlockHeader>(value);
                Guard.Assert(previousHeader.GetHash() == genesisHeader.HashBlock); // can't swap networks

                foreach (DbRecord<int> row in collection.FindAll().Select(d => this.mapper.ToObject<DbRecord<int>>(d)).Skip(1))
                {
                    if ((tip != null) && (previousHeader.HashPrevBlock != tip.HashBlock))
                        break;

                    BlockHeader blockHeader = this.dBreezeSerializer.Deserialize<BlockHeader>(row.Value);
                    tip = new ChainedHeader(previousHeader, blockHeader.HashPrevBlock, tip);
                    previousHeader = blockHeader;
                }

                if (previousHeader != null)
                    tip = new ChainedHeader(previousHeader, previousHeader.GetHash(), tip);

                if (tip == null)
                    tip = genesisHeader;

                this.locator = tip.GetLocator();
                return tip;
            });

            return task;
        }

        /// <inheritdoc />
        public Task SaveAsync(ChainIndexer chainIndexer)
        {
            Guard.NotNull(chainIndexer, nameof(chainIndexer));

            Task task = Task.Run(() =>
            {
                LiteCollection<BsonDocument> collection = this.db.GetCollection("Chain");
                ChainedHeader fork = this.locator == null ? null : chainIndexer.FindFork(this.locator);
                ChainedHeader tip = chainIndexer.Tip;
                ChainedHeader toSave = tip;

                var headers = new List<ChainedHeader>();
                while (toSave != fork)
                {
                    headers.Add(toSave);
                    toSave = toSave.Previous;
                }

                // DBreeze is faster on ordered insert.
                IOrderedEnumerable<ChainedHeader> orderedChainedHeaders = headers.OrderBy(b => b.Height);
                foreach (ChainedHeader block in orderedChainedHeaders)
                {
                    BlockHeader header = block.Header;
                    if (header is ProvenBlockHeader)
                    {
                        // copy the header parameters, untill we dont make PH a normal header we store it in its own repo.
                        BlockHeader newHeader = chainIndexer.Network.Consensus.ConsensusFactory.CreateBlockHeader();
                        newHeader.Bits = header.Bits;
                        newHeader.Time = header.Time;
                        newHeader.Nonce = header.Nonce;
                        newHeader.Version = header.Version;
                        newHeader.HashMerkleRoot = header.HashMerkleRoot;
                        newHeader.HashPrevBlock = header.HashPrevBlock;

                        header = newHeader;
                    }

                    BsonDocument document = this.mapper.ToDocument(new DbRecord<int> { Key = block.Height, Value = this.dBreezeSerializer.Serialize(header) });
                    collection.Upsert(document);
                }

                this.locator = tip.GetLocator();
            });

            return task;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.db?.Dispose();
        }
    }
}
