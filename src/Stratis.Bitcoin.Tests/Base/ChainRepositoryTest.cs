using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Tests.Base
{
    public class ChainRepositoryTest : TestBase
    {
        private readonly DBreezeSerializer dBreezeSerializer;

        public ChainRepositoryTest() : base(KnownNetworks.StratisRegTest)
        {
            this.dBreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
        }

        [Fact]
        public void SaveWritesChainToDisk()
        {
            string dir = CreateTestDir(this);
            var chain = new ChainIndexer(KnownNetworks.StratisRegTest);
            this.AppendBlock(chain);

            using (var repo = new ChainRepository(dir, new LoggerFactory(), this.dBreezeSerializer))
            {
                repo.SaveAsync(chain).GetAwaiter().GetResult();
            }

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
            using (var db = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                ChainedHeader tip = null;
                LiteCollection<BsonDocument> collection = db.GetCollection("Chain");
                foreach (DbRecord<int, byte[]> row in collection.FindAll().Select(r => r.ToDbRecord<int, byte[]>(mapper)))
                {
                    var blockHeader = this.dBreezeSerializer.Deserialize<BlockHeader>(row.Value);

                    if (tip != null && blockHeader.HashPrevBlock != tip.HashBlock)
                        break;
                    tip = new ChainedHeader(blockHeader, blockHeader.GetHash(), tip);
                }
                Assert.Equal(tip, chain.Tip);
            }
        }

        [Fact]
        public void GetChainReturnsConcurrentChainFromDisk()
        {
            string dir = CreateTestDir(this);
            var chain = new ChainIndexer(KnownNetworks.StratisRegTest);
            ChainedHeader tip = this.AppendBlock(chain);

            BsonMapper mapper = BsonMapper.Global;
            mapper.Entity<DbRecord<int, byte[]>>().Id(p => p.Key);
            using (var db = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = db.GetCollection("Chain");
                ChainedHeader toSave = tip;
                var blocks = new List<ChainedHeader>();
                while (toSave != null)
                {
                    blocks.Insert(0, toSave);
                    toSave = toSave.Previous;
                }

                foreach (ChainedHeader block in blocks)
                {
                    collection.Insert(new DbRecord<int, byte[]>(block.Height, this.dBreezeSerializer.Serialize(block.Header)).ToDocument(mapper));
                }
            }

            using (var repo = new ChainRepository(dir, new LoggerFactory(), this.dBreezeSerializer))
            {
                var testChain = new ChainIndexer(KnownNetworks.StratisRegTest);
                testChain.SetTip(repo.LoadAsync(testChain.Genesis).GetAwaiter().GetResult());
                Assert.Equal(tip, testChain.Tip);
            }
        }

        public ChainedHeader AppendBlock(ChainedHeader previous, params ChainIndexer[] chainsIndexer)
        {
            ChainedHeader last = null;
            uint nonce = RandomUtils.GetUInt32();
            foreach (ChainIndexer chain in chainsIndexer)
            {
                Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();
                block.AddTransaction(this.Network.CreateTransaction());
                block.UpdateMerkleRoot();
                block.Header.HashPrevBlock = previous == null ? chain.Tip.HashBlock : previous.HashBlock;
                block.Header.Nonce = nonce;
                if (!chain.TrySetTip(block.Header, out last))
                    throw new InvalidOperationException("Previous not existing");
            }
            return last;
        }

        private ChainedHeader AppendBlock(params ChainIndexer[] chainsIndexer)
        {
            ChainedHeader index = null;
            return this.AppendBlock(index, chainsIndexer);
        }
    }
}