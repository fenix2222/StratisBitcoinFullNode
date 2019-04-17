﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDB;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;
using Xunit;

namespace Stratis.Bitcoin.Features.Consensus.Tests.ProvenBlockHeaders
{
    public class ProvenBlockHeaderRepositoryTests : LogsTestBase
    {
        private readonly Mock<ILoggerFactory> loggerFactory;
        private readonly DBreezeSerializer dBreezeSerializer;
        private const string ProvenBlockHeaderTable = "ProvenBlockHeader";
        private const string BlockHashTable = "BlockHashHeight";

        public ProvenBlockHeaderRepositoryTests() : base(KnownNetworks.StratisTest)
        {
            this.loggerFactory = new Mock<ILoggerFactory>();
            this.dBreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
        }

        [Fact]
        public void Initializes_Genesis_ProvenBlockHeader_OnLoadAsync()
        {
            string folder = CreateTestDir(this);

            // Initialise the repository - this will set-up the genesis blockHash (blockId).
            using (IProvenBlockHeaderRepository repository = this.SetupRepository(this.Network, folder))
            {
                // Check the BlockHash (blockId) exists.
                repository.TipHashHeight.Height.Should().Be(0);
                repository.TipHashHeight.Hash.Should().Be(this.Network.GetGenesis().GetHash());
            }
        }

        [Fact]
        public async Task PutAsync_WritesProvenBlockHeaderAndSavesBlockHashAsync()
        {
            string folder = CreateTestDir(this);

            ProvenBlockHeader provenBlockHeaderIn = CreateNewProvenBlockHeaderMock();

            var blockHashHeightPair = new HashHeightPair(provenBlockHeaderIn.GetHash(), 0);
            var items = new SortedDictionary<int, ProvenBlockHeader>() { { 0, provenBlockHeaderIn } };

            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(items, blockHashHeightPair);
            }

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            using (var db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> provenBlockHeadersCollection = db.GetCollection(ProvenBlockHeaderTable);
                LiteCollection<BsonDocument> blockHashCollection = db.GetCollection(BlockHashTable);
                var headerOut = this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(provenBlockHeadersCollection.FindById(blockHashHeightPair.Height.ToBytes()).ToDbRecord<byte[], byte[]>(mapper).Value);
                var hashHeightPairOut = this.DBreezeSerializer.Deserialize<HashHeightPair>(blockHashCollection.FindById(new byte[0]).ToDbRecord<byte[], byte[]>(mapper).Value);

                headerOut.Should().NotBeNull();
                headerOut.GetHash().Should().Be(provenBlockHeaderIn.GetHash());

                hashHeightPairOut.Should().NotBeNull();
                hashHeightPairOut.Hash.Should().Be(provenBlockHeaderIn.GetHash());
            }
        }

        [Fact]
        public async Task PutAsync_Inserts_MultipleProvenBlockHeadersAsync()
        {
            string folder = CreateTestDir(this);

            PosBlock posBlock = CreatePosBlock();
            ProvenBlockHeader header1 = CreateNewProvenBlockHeaderMock(posBlock);
            ProvenBlockHeader header2 = CreateNewProvenBlockHeaderMock(posBlock);

            var items = new SortedDictionary<int, ProvenBlockHeader>() { { 0, header1 }, { 1, header2 } };

            // Put the items in the repository.
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(items, new HashHeightPair(header2.GetHash(), items.Count - 1));
            }

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);

            // Check the ProvenBlockHeader exists in the database.
            using (var db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> provenBlockHeadersCollection = db.GetCollection(ProvenBlockHeaderTable);
                var headersOut = provenBlockHeadersCollection.FindAll()
                    .Select(ph => ph.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);
                headersOut.Keys.Count.Should().Be(2);
                this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.First().Value).GetHash().Should().Be(items[0].GetHash());
                this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(headersOut.Last().Value).GetHash().Should().Be(items[1].GetHash());
            }
        }

        [Fact]
        public async Task GetAsync_ReadsProvenBlockHeaderAsync()
        {
            string folder = CreateTestDir(this);

            ProvenBlockHeader headerIn = CreateNewProvenBlockHeaderMock();

            int blockHeight = 1;

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            using (var db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> provenBlockHeadersCollection = db.GetCollection(ProvenBlockHeaderTable);
                provenBlockHeadersCollection.Insert(new DbRecord<byte[], byte[]>(blockHeight.ToBytes(), this.dBreezeSerializer.Serialize(headerIn)).ToDocument(mapper));
            }

            // Query the repository for the item that was inserted in the above code.
            using (ProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                var headerOut = await repo.GetAsync(blockHeight).ConfigureAwait(false);

                headerOut.Should().NotBeNull();
                uint256.Parse(headerOut.ToString()).Should().Be(headerOut.GetHash());
            }
        }

        [Fact]
        public async Task GetAsync_WithWrongBlockHeightReturnsNullAsync()
        {
            string folder = CreateTestDir(this);

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            using (var db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> provenBlockHeadersCollection = db.GetCollection(ProvenBlockHeaderTable);
                LiteCollection<BsonDocument> blockHashCollection = db.GetCollection(BlockHashTable);

                provenBlockHeadersCollection.Insert(new DbRecord<byte[], byte[]>(1.ToBytes(), this.dBreezeSerializer.Serialize(CreateNewProvenBlockHeaderMock())).ToDocument(mapper));
                blockHashCollection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(new uint256(), 1))).ToDocument(mapper));
            }

            using (ProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                // Select a different block height.
                ProvenBlockHeader outHeader = await repo.GetAsync(2).ConfigureAwait(false);
                outHeader.Should().BeNull();

                // Select the original item inserted into the table
                outHeader = await repo.GetAsync(1).ConfigureAwait(false);
                outHeader.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task PutAsync_Add_Ten_ProvenBlockHeaders_Dispose_On_Initialise_Repo_TipHeight_Should_Be_At_Last_Saved_TipAsync()
        {
            string folder = CreateTestDir(this);

            PosBlock posBlock = CreatePosBlock();
            var headers = new SortedDictionary<int, ProvenBlockHeader>();

            for (int i = 0; i < 10; i++)
            {
                headers.Add(i, CreateNewProvenBlockHeaderMock(posBlock));
            }

            // Put the items in the repository.
            using (IProvenBlockHeaderRepository repo = this.SetupRepository(this.Network, folder))
            {
                await repo.PutAsync(headers, new HashHeightPair(headers.Last().Value.GetHash(), headers.Count - 1));
            }

            using (IProvenBlockHeaderRepository newRepo = this.SetupRepository(this.Network, folder))
            {
                newRepo.TipHashHeight.Hash.Should().Be(headers.Last().Value.GetHash());
                newRepo.TipHashHeight.Height.Should().Be(headers.Count - 1);
            }
        }

        private ProvenBlockHeaderRepository SetupRepository(Network network, string folder)
        {
            var repo = new ProvenBlockHeaderRepository(network, folder, this.LoggerFactory.Object, this.dBreezeSerializer);

            Task task = repo.InitializeAsync();

            task.Wait();

            return repo;
        }
    }
}
