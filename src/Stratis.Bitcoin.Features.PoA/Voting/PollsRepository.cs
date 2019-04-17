using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public class PollsRepository : IDisposable
    {
        private readonly ILogger logger;

        private readonly DBreezeSerializer dBreezeSerializer;

        internal const string TableName = "DataTable";
        
        private LiteDatabase db;
        private readonly BsonMapper mapper;
        private LiteCollection<BsonDocument> DataCollection => this.db.GetCollection(TableName);

        private static readonly byte[] RepositoryHighestIndexKey = new byte[0];

        private int highestPollId;

        public PollsRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
            : this(dataFolder.PollsPath, loggerFactory, dBreezeSerializer)
        {
        }

        public PollsRepository(string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.db = new LiteDatabase($"FileName={folder}/main.db;Mode=Exclusive;");
            this.mapper = BsonMapper.Global;
            this.mapper.Entity<DbRecord>().Id(p => p.Key);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dBreezeSerializer = dBreezeSerializer;
        }

        public void Initialize()
        {
            // Load highest index.
            this.highestPollId = -1;
           
            var row = this.DataCollection.FindById(RepositoryHighestIndexKey);

            if (row != null)
                this.highestPollId = row.ToDbRecord<byte[], int>(this.mapper).Value;

            this.logger.LogDebug("Polls repo initialized with highest id: {0}.", this.highestPollId);
        }

        /// <summary>Provides Id of the most recently added poll.</summary>
        public int GetHighestPollId()
        {
            return this.highestPollId;
        }

        private void SaveHighestPollId()
        {
            this.DataCollection.Insert(
                new DbRecord<byte[], int>(RepositoryHighestIndexKey, this.highestPollId).ToDocument(this.mapper));
        }

        /// <summary>Removes polls under provided ids.</summary>
        public void RemovePolls(params int[] ids)
        {
            foreach (int pollId in ids.Reverse())
            {
                if (this.highestPollId != pollId)
                    throw new ArgumentException("Only deletion of the most recent item is allowed!");

                this.DataCollection.Delete(this.ToBytes(pollId));

                this.highestPollId--;
                this.SaveHighestPollId();
            }
        }

        /// <summary>Adds new poll.</summary>
        public void AddPolls(params Poll[] polls)
        {
            foreach (Poll pollToAdd in polls)
            {
                if (pollToAdd.Id != this.highestPollId + 1)
                    throw new ArgumentException("Id is incorrect. Gaps are not allowed.");

                byte[] bytes = this.dBreezeSerializer.Serialize(pollToAdd);

                this.DataCollection.Insert(
                    new DbRecord<byte[], byte[]>(ToBytes(pollToAdd.Id), bytes).ToDocument(this.mapper));

                this.highestPollId++;
                this.SaveHighestPollId();
            }
        }

        /// <summary>Updates existing poll.</summary>
        public void UpdatePoll(Poll poll)
        {
            BsonDocument row = this.DataCollection.FindById(ToBytes(poll.Id));

            if (row == null)
                throw new ArgumentException("Value doesn't exist!");

            byte[] bytes = this.dBreezeSerializer.Serialize(poll);

            this.DataCollection.Insert(new DbRecord<byte[], byte[]>(this.ToBytes(poll.Id), bytes).ToDocument(this.mapper));
        }

        /// <summary>Loads polls under provided keys from the database.</summary>
        public List<Poll> GetPolls(params int[] ids)
        {
            var polls = new List<Poll>(ids.Length);

            foreach (int id in ids)
            {
                BsonDocument row = this.DataCollection.FindById(this.ToBytes(id));

                if (row == null)
                    throw new ArgumentException("Value under provided key doesn't exist!");

                Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.ToDbRecord<byte[], byte[]>(this.mapper).Value);

                polls.Add(poll);
            }

            return polls;
        }

        /// <summary>Loads all polls from the database.</summary>
        public List<Poll> GetAllPolls()
        {
            var polls = new List<Poll>(this.highestPollId + 1);

            for (int i = 0; i < this.highestPollId + 1; i++)
            {
                BsonDocument row = this.DataCollection.FindById(this.ToBytes(i));

                Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.ToDbRecord<byte[], byte[]>(this.mapper).Value);

                polls.Add(poll);
            }

            return polls;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.db.Dispose();
        }

        private byte[] ToBytes(int value)
        {
            byte[] key = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(key);
            return key;
        }
    }
}
