using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.BlockStore
{
    /// <summary>
    /// <see cref="IBlockRepository"/> is the interface to all the logics interacting with the blocks stored in the database.
    /// </summary>
    public interface IBlockRepository : IBlockStore
    {
        /// <summary> The dbreeze database engine.</summary>
        LiteDatabase Db { get; }

        /// <summary>
        /// Deletes blocks and indexes for transactions that belong to deleted blocks.
        /// <para>
        /// It should be noted that this does not delete the entries from disk (only the references are removed) and
        /// as such the file size remains the same.
        /// </para>
        /// </summary>
        /// <remarks>TODO: This will need to be revisited once DBreeze has been fixed or replaced with a solution that works.</remarks>
        /// <param name="hashes">List of block hashes to be deleted.</param>
        void DeleteBlocks(List<uint256> hashes);

        /// <summary>
        /// Persist the next block hash and insert new blocks into the database.
        /// </summary>
        /// <param name="newTip">Hash and height of the new repository's tip.</param>
        /// <param name="blocks">Blocks to be inserted.</param>
        void PutBlocks(HashHeightPair newTip, List<Block> blocks);

        /// <summary>
        /// Get the blocks from the database by using block hashes.
        /// </summary>
        /// <param name="hashes">A list of unique block hashes.</param>
        /// <returns>The blocks (or null if not found) in the same order as the hashes on input.</returns>
        List<Block> GetBlocks(List<uint256> hashes);

        /// <summary>
        /// Wipe out blocks and their transactions then replace with a new block.
        /// </summary>
        /// <param name="newTip">Hash and height of the new repository's tip.</param>
        /// <param name="hashes">List of all block hashes to be deleted.</param>
        /// <exception cref="DBreezeException">Thrown if an error occurs during database operations.</exception>
        void Delete(HashHeightPair newTip, List<uint256> hashes);

        /// <summary>
        /// Determine if a block already exists
        /// </summary>
        /// <param name="hash">The hash.</param>
        /// <returns><c>true</c> if the block hash can be found in the database, otherwise return <c>false</c>.</returns>
        bool Exist(uint256 hash);

        /// <summary>
        /// Iterate over every block in the database.
        /// If <see cref="TxIndex"/> is true, we store the block hash alongside the transaction hash in the transaction table, otherwise clear the transaction table.
        /// </summary>
        void ReIndex();

        /// <summary>
        /// Set whether to index transactions by block hash, as well as storing them inside of the block.
        /// </summary>
        /// <param name="txIndex">Whether to index transactions.</param>
        void SetTxIndex(bool txIndex);

        /// <summary>Hash and height of the repository's tip.</summary>
        HashHeightPair TipHashAndHeight { get; }

        /// <summary> Indicates that the node should store all transaction data in the database.</summary>
        bool TxIndex { get; }
    }

    public class BlockRepository : IBlockRepository
    {
        internal const string BlockTableName = "Block";

        internal const string CommonTableName = "Common";
        
        internal const string CommonBytesTableName = "CommonBytes";

        internal const string TransactionTableName = "Transaction";

        public LiteDatabase Db { get; }

        private LiteCollection<BsonDocument> TransactionsCollection => this.Db.GetCollection(TransactionTableName);
        
        private LiteCollection<BsonDocument> CommonCollection => this.Db.GetCollection(CommonTableName);
        
        private LiteCollection<BsonDocument> CommonBytesCollection => this.Db.GetCollection(CommonBytesTableName);

        private LiteCollection<BsonDocument> BlocksCollection => this.Db.GetCollection(BlockTableName);

        private readonly ILogger logger;

        private readonly Network network;

        private readonly BsonMapper mapper;

        private static readonly byte[] RepositoryTipKey = new byte[0];

        private static readonly byte[] TxIndexKey = new byte[1];

        /// <inheritdoc />
        public HashHeightPair TipHashAndHeight { get; private set; }

        /// <inheritdoc />
        public bool TxIndex { get; private set; }

        private readonly DBreezeSerializer dBreezeSerializer;

        public BlockRepository(Network network, DataFolder dataFolder,
            ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
            : this(network, dataFolder.BlockPath, loggerFactory, dBreezeSerializer)
        {
        }

        public BlockRepository(Network network, string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.Db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.dBreezeSerializer = dBreezeSerializer;
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);
        }

        /// <inheritdoc />
        public virtual void Initialize()
        {
            Block genesis = this.network.GetGenesis();

            if (this.LoadTipHashAndHeight() == null)
            {
                this.SaveTipHashAndHeight(new HashHeightPair(genesis.GetHash(), 0));
            }

            if (this.LoadTxIndex() == null)
            {
                this.SaveTxIndex(false);
            }
        }

        /// <inheritdoc />
        public Transaction GetTransactionById(uint256 trxid)
        {
            Guard.NotNull(trxid, nameof(trxid));

            if (!this.TxIndex)
            {
                this.logger.LogTrace("(-)[TX_INDEXING_DISABLED]:null");
                return default(Transaction);
            }

            Transaction res = null;
            BsonDocument transactionRow = this.TransactionsCollection.FindById(trxid.ToBytes());
            if (transactionRow == null)
            {
                this.logger.LogTrace("(-)[NO_BLOCK]:null");
                return null;
            }

            DbRecord<byte[]> record = transactionRow.ToDbRecord<byte[]>(this.mapper);
            BsonDocument blockRow = this.BlocksCollection.FindById(record.Value);

            if (blockRow != null)
            {
                DbRecord<byte[]> blockRecord = blockRow.ToDbRecord<byte[]>(this.mapper);
                var block = this.dBreezeSerializer.Deserialize<Block>(blockRecord.Value);
                res = block.Transactions.FirstOrDefault(t => t.GetHash() == trxid);
            }

            return res;
        }

        /// <inheritdoc/>
        public Transaction[] GetTransactionsByIds(uint256[] trxids)
        {
            if (!this.TxIndex)
            {
                this.logger.LogTrace("(-)[TX_INDEXING_DISABLED]:null");
                return null;
            }

            Transaction[] txes = new Transaction[trxids.Length];
            for (int i = 0; i < trxids.Length; i++)
            {
                BsonDocument transactionRow = this.TransactionsCollection.FindById(trxids[i].ToBytes());
                if (transactionRow == null)
                {
                    this.logger.LogTrace("(-)[NO_TX_ROW]:null");
                    return null;
                }

                DbRecord<byte[]> record = transactionRow.ToDbRecord<byte[]>(this.mapper);
                BsonDocument blockRow = this.BlocksCollection.FindById(record.Value);

                if (blockRow == null)
                {
                    this.logger.LogTrace("(-)[NO_BLOCK]:null");
                    return null;
                }

                DbRecord<byte[]> blockRecord = blockRow.ToDbRecord<byte[]>(this.mapper);
                var block = this.dBreezeSerializer.Deserialize<Block>(blockRecord.Value);
                Transaction tx = block.Transactions.FirstOrDefault(t => t.GetHash() == trxids[i]);

                txes[i] = tx;
            }

            return txes;
        }

        /// <inheritdoc />
        public uint256 GetBlockIdByTransactionId(uint256 trxid)
        {
            Guard.NotNull(trxid, nameof(trxid));

            if (!this.TxIndex)
            {
                this.logger.LogTrace("(-)[NO_TXINDEX]:null");
                return default(uint256);
            }

            uint256 res = null;
            BsonDocument transactionRow = this.TransactionsCollection.FindById(trxid.ToBytes());
            if (transactionRow != null)
            {
                DbRecord<byte[]> record = transactionRow.ToDbRecord<byte[]>(this.mapper);
                res = new uint256(record.Value);
            }

            return res;
        }

        protected virtual void OnInsertBlocks(List<Block> blocks)
        {
            var transactions = new List<(Transaction, Block)>();
            var byteListComparer = new ByteListComparer();
            var blockDict = new Dictionary<uint256, Block>();

            // Gather blocks.
            foreach (Block block in blocks)
            {
                uint256 blockId = block.GetHash();
                blockDict[blockId] = block;
            }

            // Sort blocks. Be consistent in always converting our keys to byte arrays using the ToBytes method.
            List<KeyValuePair<uint256, Block>> blockList = blockDict.ToList();
            blockList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

            // Index blocks.
            foreach (KeyValuePair<uint256, Block> kv in blockList)
            {
                uint256 blockId = kv.Key;
                Block block = kv.Value;

                // If the block is already in store don't write it again.
                var blockRow = this.BlocksCollection.FindById(blockId.ToBytes());
                if (blockRow == null)
                {
                    this.BlocksCollection.Insert(new DbRecord<byte[], byte[]>(blockId.ToBytes(), this.dBreezeSerializer.Serialize(block)).ToDocument(this.mapper));

                    if (this.TxIndex)
                    {
                        foreach (Transaction transaction in block.Transactions)
                            transactions.Add((transaction, block));
                    }
                }
            }

            if (this.TxIndex)
                this.OnInsertTransactions(transactions);
        }

        protected virtual void OnInsertTransactions(List<(Transaction, Block)> transactions)
        {
            var byteListComparer = new ByteListComparer();
            transactions.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Item1.GetHash().ToBytes(), pair2.Item1.GetHash().ToBytes()));

            // Index transactions.
            foreach ((Transaction transaction, Block block) in transactions)
                this.TransactionsCollection.Insert(new DbRecord<byte[], byte[]>(transaction.GetHash().ToBytes(), block.GetHash().ToBytes()).ToDocument(this.mapper));
        }

        /// <inheritdoc />
        public void ReIndex()
        {
            if (this.TxIndex)
            {
                // Insert transactions to database.
                IEnumerable<BsonDocument> blockRows = this.BlocksCollection.FindAll();
                foreach (BsonDocument blockRow in blockRows)
                {
                    var block = this.dBreezeSerializer.Deserialize<Block>(blockRow.ToDbRecord<byte[]>(this.mapper).Value);
                    foreach (Transaction transaction in block.Transactions)
                    {
                        this.TransactionsCollection.Insert(new DbRecord<byte[], byte[]>(transaction.GetHash().ToBytes(), block.GetHash().ToBytes()).ToDocument(this.mapper));
                    }
                }
            }
            else
            {
                // Clear tx from database.
                this.TransactionsCollection.Delete(r => true);
            }
        }

        /// <inheritdoc />
        public void PutBlocks(HashHeightPair newTip, List<Block> blocks)
        {
            Guard.NotNull(newTip, nameof(newTip));
            Guard.NotNull(blocks, nameof(blocks));

            // DBreeze is faster if sort ascending by key in memory before insert
            // however we need to find how byte arrays are sorted in DBreeze.
            this.OnInsertBlocks(blocks);

            // Commit additions
            this.SaveTipHashAndHeight(newTip);
        }

        private bool? LoadTxIndex()
        {
            bool? res = null;
            var row = this.CommonCollection.FindById(TxIndexKey);
            if (row != null)
            {
                var mappedValue = row.ToDbRecord<byte[], bool>(this.mapper).Value;
                this.TxIndex = mappedValue;
                res = mappedValue;
            }

            return res;
        }

        private void SaveTxIndex(bool txIndex)
        {
            this.TxIndex = txIndex;
            this.CommonCollection.Insert(new DbRecord<byte[], bool>(TxIndexKey, txIndex).ToDocument(this.mapper));
        }

        /// <inheritdoc />
        public void SetTxIndex(bool txIndex)
        {
            this.SaveTxIndex(txIndex);
        }

        private HashHeightPair LoadTipHashAndHeight()
        {
            if (this.TipHashAndHeight == null)
            {
                var row = this.CommonBytesCollection.FindById(RepositoryTipKey);
                if (row != null)
                    this.TipHashAndHeight = this.dBreezeSerializer.Deserialize<HashHeightPair>(row.ToDbRecord<byte[], byte[]>(this.mapper).Value);
            }

            return this.TipHashAndHeight;
        }

        private void SaveTipHashAndHeight(HashHeightPair newTip)
        {
            this.TipHashAndHeight = newTip;
            this.CommonCollection.Insert(new DbRecord<byte[], byte[]>(RepositoryTipKey, this.dBreezeSerializer.Serialize(newTip)).ToDocument(this.mapper));
        }

        /// <inheritdoc />
        public Block GetBlock(uint256 hash)
        {
            Guard.NotNull(hash, nameof(hash));

            Block res = null;

            byte[] key = hash.ToBytes();
            var blockRow = this.BlocksCollection.FindById(key);
            if (blockRow != null)
                res = this.dBreezeSerializer.Deserialize<Block>(blockRow.ToDbRecord<byte[]>(this.mapper).Value);

            // If searching for genesis block, return it.
            if (res == null && hash == this.network.GenesisHash)
            {
                res = this.network.GetGenesis();
            }

            return res;
        }

        /// <inheritdoc />
        public List<Block> GetBlocks(List<uint256> hashes)
        {
            Guard.NotNull(hashes, nameof(hashes));

            List<Block> blocks;
            blocks = this.GetBlocksFromHashes(hashes);

            return blocks;
        }

        /// <inheritdoc />
        public bool Exist(uint256 hash)
        {
            Guard.NotNull(hash, nameof(hash));

            bool res = false;

            // Lazy loading is on so we don't fetch the whole value, just the row.
            byte[] key = hash.ToBytes();
            BsonDocument blockRow = this.BlocksCollection.FindById(key);
            if (blockRow != null)
                res = true;

            return res;
        }

        protected virtual void OnDeleteTransactions(List<(Transaction, Block)> transactions)
        {
            foreach ((Transaction transaction, Block block) in transactions)
                this.TransactionsCollection.Delete(transaction.GetHash().ToBytes());
        }

        protected virtual void OnDeleteBlocks(List<Block> blocks)
        {
            if (this.TxIndex)
            {
                var transactions = new List<(Transaction, Block)>();

                foreach (Block block in blocks)
                {
                    foreach (Transaction transaction in block.Transactions)
                        transactions.Add((transaction, block));
                }

                this.OnDeleteTransactions(transactions);
            }

            foreach (Block block in blocks)
                this.BlocksCollection.Delete(block.GetHash().ToBytes());
        }

        public List<Block> GetBlocksFromHashes(List<uint256> hashes)
        {
            var results = new Dictionary<uint256, Block>();

            // Access hash keys in sorted order.
            var byteListComparer = new ByteListComparer();
            List<(uint256, byte[])> keys = hashes.Select(hash => (hash, hash.ToBytes())).ToList();

            keys.Sort((key1, key2) => byteListComparer.Compare(key1.Item2, key2.Item2));

            foreach ((uint256, byte[]) key in keys)
            {
                BsonDocument blockRow = this.BlocksCollection.FindById(key.Item2);
                if (blockRow != null)
                {
                    results[key.Item1] = this.dBreezeSerializer.Deserialize<Block>(blockRow.ToDbRecord<byte[]>(this.mapper).Value);

                    this.logger.LogTrace("Block hash '{0}' loaded from the store.", key.Item1);
                }
                else
                {
                    results[key.Item1] = null;

                    this.logger.LogTrace("Block hash '{0}' not found in the store.", key.Item1);
                }
            }

            // Return the result in the order that the hashes were presented.
            return hashes.Select(hash => results[hash]).ToList();
        }

        /// <inheritdoc />
        public void Delete(HashHeightPair newTip, List<uint256> hashes)
        {
            Guard.NotNull(newTip, nameof(newTip));
            Guard.NotNull(hashes, nameof(hashes));

            List<Block> blocks = this.GetBlocksFromHashes(hashes);
            this.OnDeleteBlocks(blocks.Where(b => b != null).ToList());
            this.SaveTipHashAndHeight(newTip);
        }

        /// <inheritdoc />
        public void DeleteBlocks(List<uint256> hashes)
        {
            Guard.NotNull(hashes, nameof(hashes));

            List<Block> blocks = this.GetBlocksFromHashes(hashes);
            this.OnDeleteBlocks(blocks.Where(b => b != null).ToList());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.Db.Dispose();
        }
    }
}
