using System.Collections.Generic;
using System.Linq;
using LiteDB;
using NBitcoin;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.BlockStore.Tests
{
    public class BlockRepositoryTest : LogsTestBase
    {
        [Fact]
        public void InitializesGenBlockAndTxIndexOnFirstLoad()
        {
            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                BsonDocument blockRow = collection.FindById(new byte[0]);
                BsonDocument txIndexRow = collection.FindById(new byte[1]);

                Assert.Equal(this.Network.GetGenesis().GetHash(), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockRow.ToDbRecord<byte[], byte[]>(mapper).Value).Hash);
                Assert.False(txIndexRow.ToDbRecord<byte[], bool>(mapper).Value);
            }
        }

        [Fact]
        public void DoesNotOverwriteExistingBlockAndTxIndexOnFirstLoad()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(new uint256(56), 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                var blockRow = collection.FindById(new byte[0]).ToDbRecord<byte[]>(mapper);
                var txIndexRow = collection.FindById(new byte[1]).ToDbRecord<byte[], bool>(mapper);

                Assert.Equal(new HashHeightPair(new uint256(56), 1), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockRow.Value));
                Assert.True(txIndexRow.Value);
            }
        }

        [Fact]
        public void GetTrxAsyncWithoutTransactionIndexReturnsNewTransaction()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], false).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(default(Transaction), repository.GetTransactionById(uint256.Zero));
            }
        }

        [Fact]
        public void GetTrxAsyncWithoutTransactionInIndexReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                var blockId = new uint256(8920);
                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetTransactionById(new uint256(65)));
            }
        }

        [Fact]
        public void GetTrxAsyncWithTransactionReturnsExistingTransaction()
        {
            string dir = CreateTestDir(this);
            Transaction trans = this.Network.CreateTransaction();
            trans.Version = 125;

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                Block block = this.Network.CreateBlock();
                block.Header.GetHash();
                block.Transactions.Add(trans);

                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.Header.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
                transCollection.Insert(new DbRecord<byte[], byte[]>(trans.GetHash().ToBytes(), block.Header.GetHash().ToBytes()).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal((uint)125, repository.GetTransactionById(trans.GetHash()).Version);
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithoutTxIndexReturnsDefaultId()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);
                
                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], false).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(default(uint256), repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithoutExistingTransactionReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithTransactionReturnsBlockId()
        {
            string dir = CreateTestDir(this);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> commonCollection = engine.GetCollection("Common");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                transCollection.Insert(
                    new DbRecord<byte[], byte[]>(new uint256(26).ToBytes(), new uint256(42).ToBytes())
                        .ToDocument(mapper));
                commonCollection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                commonCollection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(new uint256(42), repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void PutAsyncWritesBlocksAndTransactionsToDbAndSavesNextBlockHash()
        {
            string dir = CreateTestDir(this);

            var nextBlockHash = new uint256(1241256);
            var blocks = new List<Block>();
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();
            BlockHeader blockHeader = block.Header;
            blockHeader.Bits = new Target(12);
            Transaction transaction = this.Network.CreateTransaction();
            transaction.Version = 32;
            block.Transactions.Add(transaction);
            transaction = this.Network.CreateTransaction();
            transaction.Version = 48;
            block.Transactions.Add(transaction);
            blocks.Add(block);

            Block block2 = this.Network.Consensus.ConsensusFactory.CreateBlock();
            block2.Header.Nonce = 11;
            transaction = this.Network.CreateTransaction();
            transaction.Version = 15;
            block2.Transactions.Add(transaction);
            blocks.Add(block2);

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                collection.Insert(new DbRecord<byte[], byte[]>(new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1))).ToDocument(mapper));
                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.PutBlocks(new HashHeightPair(nextBlockHash, 100), blocks);
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> commonCollection = engine.GetCollection("Common");
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                DbRecord<byte[], byte[]> blockHashKeyRow = commonCollection.FindById(new byte[0]).ToDbRecord<byte[], byte[]>(mapper);
                Dictionary<byte[], byte[]> blockDict = blockCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);
                Dictionary<byte[], byte[]> transDict = transCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);

                Assert.Equal(new HashHeightPair(nextBlockHash, 100), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockHashKeyRow.Value));
                Assert.Equal(2, blockDict.Count);
                Assert.Equal(3, transDict.Count);

                foreach (KeyValuePair<byte[], byte[]> item in blockDict)
                {
                    Block bl = blocks.Single(b => b.GetHash() == new uint256(item.Key));
                    Assert.Equal(bl.Header.GetHash(), Block.Load(item.Value, this.Network.Consensus.ConsensusFactory).Header.GetHash());
                }

                foreach (KeyValuePair<byte[], byte[]> item in transDict)
                {
                    Block bl = blocks.Single(b => b.Transactions.Any(t => t.GetHash() == new uint256(item.Key)));
                    Assert.Equal(bl.GetHash(), new uint256(item.Value));
                }
            }
        }

        [Fact]
        public void SetTxIndexUpdatesTxIndex()
        {
            string dir = CreateTestDir(this);
            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                collection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(false);
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = engine.GetCollection("Common");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);
                DbRecord<byte[], bool> txIndexRow = collection.FindById(new byte[1]).ToDbRecord<byte[], bool>(mapper);
                Assert.False(txIndexRow.Value);
            }
        }

        [Fact]
        public void GetAsyncWithExistingBlockReturnsBlock()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(block.GetHash(), repository.GetBlock(block.GetHash()).GetHash());
            }
        }

        [Fact]
        public void GetAsyncWithExistingBlocksReturnsBlocks()
        {
            string dir = CreateTestDir(this);
            var blocks = new Block[10];

            blocks[0] = this.Network.Consensus.ConsensusFactory.CreateBlock();
            for (int i = 1; i < blocks.Length; i++)
            {
                blocks[i] = this.Network.Consensus.ConsensusFactory.CreateBlock();
                blocks[i].Header.HashPrevBlock = blocks[i - 1].Header.GetHash();
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                for (int i = 0; i < blocks.Length; i++)
                    blockCollection.Insert(new DbRecord<byte[], byte[]>(blocks[i].GetHash().ToBytes(), blocks[i].ToBytes()).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                List<Block> result = repository.GetBlocks(blocks.Select(b => b.GetHash()).ToList());

                Assert.Equal(blocks.Length, result.Count);
                for (int i = 0; i < 10; i++)
                    Assert.Equal(blocks[i].GetHash(), result[i].GetHash());
            }
        }

        [Fact]
        public void GetAsyncWithoutExistingBlockReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetBlock(new uint256()));
            }
        }

        [Fact]
        public void ExistAsyncWithExistingBlockReturnsTrue()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.True(repository.Exist(block.GetHash()));
            }
        }

        [Fact]
        public void ExistAsyncWithoutExistingBlockReturnsFalse()
        {
            string dir = CreateTestDir(this);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.False(repository.Exist(new uint256()));
            }
        }

        [Fact]
        public void DeleteAsyncRemovesBlocksAndTransactions()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            block.Transactions.Add(this.Network.CreateTransaction());

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> commonCollection = engine.GetCollection("Common");
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
                transCollection.Insert(new DbRecord<byte[], byte[]>(block.Transactions[0].GetHash().ToBytes(), block.GetHash().ToBytes()).ToDocument(mapper));
                commonCollection.Insert(new DbRecord<byte[], bool>(new byte[1], true).ToDocument(mapper));
            }

            var tip = new HashHeightPair(new uint256(45), 100);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.Delete(tip, new List<uint256> { block.GetHash() });
            }

            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> commonCollection = engine.GetCollection("Common");
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);


                DbRecord<byte[], byte[]> blockHashKeyRow = commonCollection.FindById(new byte[0]).ToDbRecord<byte[], byte[]>(mapper);
                Dictionary<byte[], byte[]> blockDict = blockCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);
                Dictionary<byte[], byte[]> transDict = transCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);

                Assert.Equal(tip, this.DBreezeSerializer.Deserialize<HashHeightPair>(blockHashKeyRow.Value));
                Assert.Empty(blockDict);
                Assert.Empty(transDict);
            }
        }

        [Fact]
        public void ReIndexAsync_TxIndex_OffToOn()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            Transaction transaction = this.Network.CreateTransaction();
            block.Transactions.Add(transaction);

            // Set up database to mimic that created when TxIndex was off. No transactions stored.
            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
            }

            // Turn TxIndex on and then reindex database, as would happen on node startup if -txindex and -reindex are set.
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(true);
                repository.ReIndex();
            }

            // Check that after indexing database, the transaction inside the block is now indexed.
            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                Dictionary<byte[], byte[]> blockDict = blockCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);
                Dictionary<byte[], byte[]> transDict = transCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);

                // Block stored as expected.
                Assert.Single(blockDict);
                Assert.Equal(block.GetHash(), this.DBreezeSerializer.Deserialize<Block>(blockDict.FirstOrDefault().Value).GetHash());

                // Transaction row in database stored as expected.
                Assert.Single(transDict);
                KeyValuePair<byte[], byte[]> savedTransactionRow = transDict.FirstOrDefault();
                Assert.Equal(transaction.GetHash().ToBytes(), savedTransactionRow.Key);
                Assert.Equal(block.GetHash().ToBytes(), savedTransactionRow.Value);
            }
        }

        [Fact]
        public void ReIndexAsync_TxIndex_OnToOff()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            Transaction transaction = this.Network.CreateTransaction();
            block.Transactions.Add(transaction);

            // Set up database to mimic that created when TxIndex was on. Transaction from block is stored.
            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                blockCollection.Insert(new DbRecord<byte[], byte[]>(block.GetHash().ToBytes(), block.ToBytes()).ToDocument(mapper));
                transCollection.Insert(new DbRecord<byte[], byte[]>(transaction.GetHash().ToBytes(), block.GetHash().ToBytes()).ToDocument(mapper));
            }

            // Turn TxIndex off and then reindex database, as would happen on node startup if -txindex=0 and -reindex are set.
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(false);
                repository.ReIndex();
            }

            // Check that after indexing database, the transaction is no longer stored.
            using (var engine = new LiteDatabase($"FileName={dir}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> blockCollection = engine.GetCollection("Block");
                LiteCollection<BsonDocument> transCollection = engine.GetCollection("Transaction");
                BsonMapper mapper = BsonMapper.Global;
                mapper.Entity<DbRecord>().Id(p => p.Key);

                Dictionary<byte[], byte[]> blockDict = blockCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);
                Dictionary<byte[], byte[]> transDict = transCollection.FindAll().Select(i => i.ToDbRecord<byte[], byte[]>(mapper)).ToDictionary(i => i.Key, i => i.Value);

                // Block still stored as expected.
                Assert.Single(blockDict);
                Assert.Equal(block.GetHash(), this.DBreezeSerializer.Deserialize<Block>(blockDict.FirstOrDefault().Value).GetHash());

                // No transactions indexed.
                Assert.Empty(transDict);
            }
        }

        private IBlockRepository SetupRepository(Network main, string dir)
        {
            var dBreezeSerializer = new DBreezeSerializer(main.Consensus.ConsensusFactory);

            var repository = new BlockRepository(main, dir, this.LoggerFactory.Object, dBreezeSerializer);
            repository.Initialize();

            return repository;
        }
    }
}
