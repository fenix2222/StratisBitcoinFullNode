using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteDB;

namespace Stratis.Bitcoin.Utilities
{
    public class DbRecord : DbRecord<string>
    {
    }

    public class DbRecord<T> : DbRecord<T, byte[]>
    {
    }

    public class DbRecord<TKey, TValue>
    {
        public DbRecord()
        {
        }

        public DbRecord(TKey key, TValue value)
        {
            this.Key = key;
            this.Value = value;
        }

        public TKey Key { get; set; }

        public TValue Value { get; set; }
    }

    public static class DbRecordExtensions
    {
        public static BsonDocument ToDocument<TKey, TValue>(this DbRecord<TKey, TValue> record, BsonMapper mapper)
        {
            return mapper.ToDocument(record);
        }

        public static DbRecord<TKey, TValue> ToDbRecord<TKey, TValue>(this BsonDocument document, BsonMapper mapper)
        {
            return mapper.ToObject<DbRecord<TKey, TValue>>(document);
        }

        public static DbRecord<T> ToDbRecord<T>(this BsonDocument document, BsonMapper mapper)
        {
            return mapper.ToObject<DbRecord<T>>(document);
        }
    }

    public class ByteListComparer : IComparer<IList<byte>>
    {
        public int Compare(IList<byte> x, IList<byte> y)
        {
            int result;
            for(int index = 0; index < Math.Min(x.Count, y.Count); index++)
            {
                result = x[index].CompareTo(y[index]);
                if (result != 0) return result;
            }

            return x.Count.CompareTo(y.Count);
        }
    }
}