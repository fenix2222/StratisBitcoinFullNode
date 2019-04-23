using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders
{
    /// <summary>
    /// Persistent implementation of the <see cref="ProvenBlockHeader"/> DBreeze repository.
    /// </summary>
    public class ProvenBlockHeaderRepository : IProvenBlockHeaderRepository
    {
        /// <summary>
        /// Instance logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Access to DBreeze database.
        /// </summary>
        private readonly LiteDatabase db;

        private readonly BsonMapper mapper;

        /// <summary>
        /// Specification of the network the node runs on - RegTest/TestNet/MainNet.
        /// </summary>
        private readonly Network network;

        /// <summary>
        /// Database key under which the block hash and height of a <see cref="ProvenBlockHeader"/> tip is stored.
        /// </summary>
        private static readonly byte[] blockHashHeightKey = new byte[0];

        /// <summary>
        /// DBreeze table names.
        /// </summary>
        private const string ProvenBlockHeaderTable = "ProvenBlockHeader";
        private const string BlockHashHeightTable = "BlockHashHeight";
        
        private LiteCollection<BsonDocument> ProvenBlockHeaderCollection => this.db.GetCollection(ProvenBlockHeaderTable);
        
        private LiteCollection<BsonDocument> BlockHashHeightCollection => this.db.GetCollection(BlockHashHeightTable);

        /// <summary>
        /// Current <see cref="ProvenBlockHeader"/> tip.
        /// </summary>
        private ProvenBlockHeader provenBlockHeaderTip;

        private readonly DBreezeSerializer dBreezeSerializer;

        /// <inheritdoc />
        public HashHeightPair TipHashHeight { get; private set; }

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - RegTest/TestNet/MainNet.</param>
        /// <param name="folder"><see cref="ProvenBlockHeaderRepository"/> folder path to the DBreeze database files.</param>
        /// <param name="loggerFactory">Factory to create a logger for this type.</param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public ProvenBlockHeaderRepository(Network network, DataFolder folder, ILoggerFactory loggerFactory,
            DBreezeSerializer dBreezeSerializer)
        : this(network, folder.ProvenBlockHeaderPath, loggerFactory, dBreezeSerializer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - RegTest/TestNet/MainNet.</param>
        /// <param name="folder"><see cref="ProvenBlockHeaderRepository"/> folder path to the DBreeze database files.</param>
        /// <param name="loggerFactory">Factory to create a logger for this type.</param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public ProvenBlockHeaderRepository(Network network, string folder, ILoggerFactory loggerFactory,
            DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(folder, nameof(folder));
            this.dBreezeSerializer = dBreezeSerializer;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            Directory.CreateDirectory(folder);

            this.db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            this.mapper.Entity<DbRecord<int, byte[]>>().Id(p => p.Key);
            this.network = network;
        }

        /// <inheritdoc />
        public Task InitializeAsync()
        {
            Task task = Task.Run(() =>
            {
                this.TipHashHeight = this.GetTipHash();

                if (this.TipHashHeight != null)
                    return;

                var hashHeight = new HashHeightPair(this.network.GetGenesis().GetHash(), 0);

                this.SetTip(hashHeight);

                this.TipHashHeight = hashHeight;
            });

            return task;
        }

        /// <inheritdoc />
        public Task<ProvenBlockHeader> GetAsync(int blockHeight)
        {
            Task<ProvenBlockHeader> task = Task.Run(() =>
            {
                var row = this.ProvenBlockHeaderCollection.FindById(BitConverter.GetBytes(blockHeight));

                if (row != null)
                    return this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(row.ToDbRecord<byte[]>(this.mapper).Value);

                return null;
            });

            return task;
        }

        /// <inheritdoc />
        public Task PutAsync(SortedDictionary<int, ProvenBlockHeader> headers, HashHeightPair newTip)
        {
            Guard.NotNull(headers, nameof(headers));
            Guard.NotNull(newTip, nameof(newTip));

            Guard.Assert(newTip.Hash == headers.Values.Last().GetHash());

            Task task = Task.Run(() =>
            {
                this.logger.LogTrace("({0}.Count():{1})", nameof(headers), headers.Count());

                this.InsertHeaders(headers);

                this.SetTip(newTip);

                this.TipHashHeight = newTip;
            });

            return task;
        }

        /// <summary>
        /// Set's the hash and height tip of the new <see cref="ProvenBlockHeader"/>.
        /// </summary>
        /// <param name="transaction"> Open DBreeze transaction.</param>
        /// <param name="newTip"> Hash height pair of the new block tip.</param>
        private void SetTip(HashHeightPair newTip)
        {
            Guard.NotNull(newTip, nameof(newTip));

            this.BlockHashHeightCollection.Upsert(new DbRecord<byte[], byte[]>(blockHashHeightKey, this.dBreezeSerializer.Serialize(newTip)).ToDocument(this.mapper));
        }

        /// <summary>
        /// Inserts <see cref="ProvenBlockHeader"/> items into to the database.
        /// </summary>
        /// <param name="transaction"> Open DBreeze transaction.</param>
        /// <param name="headers"> List of <see cref="ProvenBlockHeader"/> items to save.</param>
        private void InsertHeaders(SortedDictionary<int, ProvenBlockHeader> headers)
        {
            foreach (KeyValuePair<int, ProvenBlockHeader> header in headers)
                this.ProvenBlockHeaderCollection.Insert(new DbRecord<byte[], byte[]>(BitConverter.GetBytes(header.Key), this.dBreezeSerializer.Serialize(header.Value)).ToDocument(this.mapper));

            // Store the latest ProvenBlockHeader in memory.
            this.provenBlockHeaderTip = headers.Last().Value;
        }

        /// <summary>
        /// Retrieves the current <see cref="HashHeightPair"/> tip from disk.
        /// </summary>
        /// <param name="transaction"> Open DBreeze transaction.</param>
        /// <returns> Hash of blocks current tip.</returns>
        private HashHeightPair GetTipHash()
        {
            HashHeightPair tipHash = null;

            var row = this.BlockHashHeightCollection.FindById(blockHashHeightKey);

            if (row != null)
                tipHash = this.dBreezeSerializer.Deserialize<HashHeightPair>(row.ToDbRecord<byte[]>(this.mapper).Value);

            return tipHash;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.db?.Dispose();
        }
    }
}
