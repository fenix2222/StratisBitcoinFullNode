using System.Collections.Generic;
using System.IO;
using LiteDB;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.SmartContracts.Core.Receipts
{
    public class PersistentReceiptRepository : IReceiptRepository
    {
        private const string TableName = "receipts";
        private LiteDatabase db;
        private readonly BsonMapper mapper;
        private LiteCollection<BsonDocument> ReceiptsCollection => this.db.GetCollection("receipts");

        public PersistentReceiptRepository(DataFolder dataFolder)
        {
            string folder = dataFolder.SmartContractStatePath + TableName;
            Directory.CreateDirectory(folder);
            this.db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
        }

        // TODO: Handle pruning old data in case of reorg.

        /// <inheritdoc />
        public void Store(IEnumerable<Receipt> receipts)
        {
            foreach(Receipt receipt in receipts)
            {
                this.ReceiptsCollection.Insert(
                    new DbRecord<byte[], byte[]>(receipt.TransactionHash.ToBytes(), receipt.ToStorageBytesRlp())
                        .ToDocument(this.mapper));
            }
        }

        /// <inheritdoc />
        public Receipt Retrieve(uint256 hash)
        {
            var result = this.ReceiptsCollection.FindById(hash.ToBytes());

            if (result == null)
                return null;

            return Receipt.FromStorageBytesRlp(result.ToDbRecord<byte[], byte[]>(this.mapper).Value);
        }
    }
}
