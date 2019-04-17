using System;
using LiteDB;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;
using Stratis.Patricia;

namespace Stratis.SmartContracts.Core.State
{
    /// <summary>
    /// A basic Key/Value store in DBreeze.
    /// </summary>
    public class DBreezeByteStore : ISource<byte[], byte[]>
    {
        private LiteDatabase db;
        private readonly BsonMapper mapper;
        private string table;

        public DBreezeByteStore(LiteDatabase db, string table)
        {
            this.db = db;
            this.table = table;
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord<byte[], byte[]>>().Id(p => p.Key);
        }

        public byte[] Get(byte[] key)
        {
            var collection = this.db.GetCollection(this.table);
            var row = collection.FindById(key);

            if (row != null)
                return row.ToDbRecord<byte[], byte[]>(this.mapper).Value;

            return null;
        }

        public void Put(byte[] key, byte[] val)
        {
            LiteCollection<BsonDocument> collection = this.db.GetCollection(this.table);
            collection.Insert(new DbRecord<byte[], byte[]>(key, val).ToDocument(this.mapper));
        }

        public void Delete(byte[] key)
        {
            LiteCollection<BsonDocument> collection = this.db.GetCollection(this.table);
            collection.Delete(key);
        }

        public bool Flush()
        {
            throw new NotImplementedException("Can't flush - no underlying DB");
        }

        /// <summary>
        /// Only use for testing at the moment.
        /// </summary>
        public void Empty()
        {
            LiteCollection<BsonDocument> collection = this.db.GetCollection(this.table);
            collection.Delete(k => true);
        }
    }

    /// <summary>
    /// Used for dependency injection. A contract state specific implementation of the above class.
    /// </summary>
    public class DBreezeContractStateStore : DBreezeByteStore
    {
        public DBreezeContractStateStore(DataFolder dataFolder) : base(new LiteDatabase($"FileName={dataFolder.SmartContractStatePath}/main.db;Mode=Exclusive;"), "state") { }
    }
}
