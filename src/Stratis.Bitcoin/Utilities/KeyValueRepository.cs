using System;
using System.IO;
using System.Text;
using LiteDB;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Utilities
{
    /// <summary>Allows saving and loading single values to and from key-value storage.</summary>
    public interface IKeyValueRepository : IDisposable
    {
        /// <summary>Persists byte array to the database.</summary>
        void SaveBytes(string key, byte[] bytes);

        /// <summary>Persists any object that <see cref="DBreezeSerializer"/> can serialize to the database.</summary>
        void SaveValue<T>(string key, T value);

        /// <summary>Persists any object to the database. Object is stored as JSON.</summary>
        void SaveValueJson<T>(string key, T value);

        /// <summary>Loads byte array from the database.</summary>
        byte[] LoadBytes(string key);

        /// <summary>Loads an object that <see cref="DBreezeSerializer"/> can deserialize from the database.</summary>
        T LoadValue<T>(string key);

        /// <summary>Loads JSON from the database and deserializes it.</summary>
        T LoadValueJson<T>(string key);
    }

    public class KeyValueRepository : IKeyValueRepository
    {
        private const string TableName = "common";
        private readonly string folder;
        private readonly BsonMapper mapper;

        private readonly DBreezeSerializer dBreezeSerializer;

        public KeyValueRepository(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this (dataFolder.KeyValueRepositoryPath, dBreezeSerializer)
        {
        }

        public KeyValueRepository(string folder, DBreezeSerializer dBreezeSerializer)
        {
            Directory.CreateDirectory(folder);
            this.folder = folder;
            this.dBreezeSerializer = dBreezeSerializer;
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes)
        {
            using (var db = new LiteDatabase($"FileName={this.folder}/main.db;Mode=Exclusive;"))
            {
                var dbRecord = new DbRecord { Key = key, Value = bytes };
                LiteCollection<BsonDocument> collection = db.GetCollection(TableName);

                BsonDocument document = this.mapper.ToDocument(dbRecord);
                collection.Insert(document);
            }
        }

        /// <inheritdoc />
        public void SaveValue<T>(string key, T value)
        {
            this.SaveBytes(key, this.dBreezeSerializer.Serialize(value));
        }

        /// <inheritdoc />
        public void SaveValueJson<T>(string key, T value)
        {
            string json = Serializer.ToString(value);
            byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

            this.SaveBytes(key, jsonBytes);
        }

        /// <inheritdoc />
        public byte[] LoadBytes(string key)
        {
            using (var db = new LiteDatabase($"FileName={this.folder}/main.db;Mode=Exclusive;"))
            {
                LiteCollection<BsonDocument> collection = db.GetCollection(TableName);
                BsonDocument document = collection.FindById(key);

                if (document == null)
                    return null;

                return this.mapper.ToObject<DbRecord>(document).Value;
            }
        }

        /// <inheritdoc />
        public T LoadValue<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            T value = this.dBreezeSerializer.Deserialize<T>(bytes);
            return value;
        }

        /// <inheritdoc />
        public T LoadValueJson<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            string json = Encoding.ASCII.GetString(bytes);

            T value = Serializer.ToObject<T>(json);

            return value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
