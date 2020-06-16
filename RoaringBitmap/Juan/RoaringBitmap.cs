using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WebBeds.Indexing
{
    public class RoaringBitmap : IEnumerable<int>, IEquatable<RoaringBitmap>
    {
        protected readonly RoaringArray m_HighLowContainer;

        /// <summary>
        ///     Creates a new RoaringBitmap from an existing RoaringArray
        /// </summary>
        private RoaringBitmap(RoaringArray input)
        {
            m_HighLowContainer = input;
        }

        /// <summary>
        ///     Creates a new RoaringBitmap from an existing list of integers
        /// </summary>
        public RoaringBitmap(params int[] values)
        {
            m_HighLowContainer = CreateRoaringArray(values.AsEnumerable());
        }

        /// <summary>
        ///     Creates a new RoaringBitmap from an existing list of integers
        /// </summary>
        public RoaringBitmap(IEnumerable<int> values)
        {
            m_HighLowContainer = CreateRoaringArray(values);
        }

        public long Cardinality => m_HighLowContainer.Cardinality;

        public IEnumerator<int> GetEnumerator()
        {
            return m_HighLowContainer.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Equals(RoaringBitmap other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }
            if (ReferenceEquals(null, other))
            {
                return false;
            }
            return m_HighLowContainer.Equals(other.m_HighLowContainer);
        }



        public override bool Equals(object obj)
        {
            var ra = obj as RoaringArray;
            return (ra != null) && Equals(ra);
        }

        public override int GetHashCode()
        {
            return (13 ^ m_HighLowContainer.GetHashCode()) << 3;
        }

        /// <summary>
        ///     Serializes a RoaringBitmap into a stream using the 'official' RoaringBitmap file format
        /// </summary>
        /// <param name="roaringBitmap">RoaringBitmap</param>
        /// <param name="stream">Stream</param>
        public static void Serialize(RoaringBitmap roaringBitmap, Stream stream)
        {
            RoaringArray.Serialize(roaringBitmap.m_HighLowContainer, stream);
        }

        /// <summary>
        ///     Optimizes a RoaringBitmap to prepare e.g. for Serialization/Deserialization
        /// </summary>
        /// <returns>RoaringBitmap</returns>
        public RoaringBitmap Optimize()
        {
            return new RoaringBitmap(RoaringArray.Optimize(m_HighLowContainer));
        }

        /// <summary>
        ///     Creates a new immutable RoaringBitmap from an existing list of integers
        /// </summary>
        /// <param name="values">List of integers</param>
        /// <returns>RoaringBitmap</returns>
        private static RoaringArray CreateRoaringArray(IEnumerable<int> values)
        {
            var groupbyHb = GetGroupedValues(values);
            var keys = new List<ushort>();
            var containers = new List<Container>();
            var size = 0;
            foreach (var group in groupbyHb)
            {
                keys.Add(group.Key);
                if (group.Count() > Container.MaxSize)
                {
                    containers.Add(BitmapContainer.Create(group.Select(Util.LowBits).ToArray()));
                }
                else
                {
                    containers.Add(ArrayContainer.Create(group.Select(Util.LowBits).ToArray()));
                }
                size++;
            }
            return new RoaringArray(size, keys, containers);
        }

        public RoaringBitmap And(RoaringBitmap other)
        {
            return this & other;
        }

        public RoaringBitmap Or(RoaringBitmap other)
        {
            return this | other;
        }
        public static RoaringBitmap CustomOr(RoaringBitmap[] items)
        {

            RoaringBitmap ret = null;
            if (items != null)
            {
                if (items.Length > 1)
                {
                    ret = items[0];

                    for (int i = 1; i < items.Length; i++)
                    {
                        ret = ret | items[i];
                    }

                }
                else if (items.Length == 1)
                {
                    ret = items[0];
                }
                else
                {
                    ret = new RoaringBitmap();
                }
            }
            else
            {
                ret = new RoaringBitmap();
            }

            return ret;
        }

        private static RoaringBitmap MultipleOr(RoaringBitmap x, IList<RoaringBitmap> y)
        {

            var p = RoaringArray.OrWithMark(x.m_HighLowContainer, y.Select(fddd => fddd.m_HighLowContainer));
            return new RoaringBitmap(p);
        }

        /// <summary>
        ///     Bitwise Or operation of multiple RoaringBitmaps
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <param name="y">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap operator |(RoaringBitmap x, IList<RoaringBitmap> y)
        {
            return RoaringBitmap.MultipleOr(x, y);
        }

        /// <summary>
        ///     Bitwise Or operation of two RoaringBitmaps
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <param name="y">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap operator |(RoaringBitmap x, RoaringBitmap y)
        {
            return new RoaringBitmap(x.m_HighLowContainer | y.m_HighLowContainer);
        }

        /// <summary>
        ///     Bitwise And operation of two RoaringBitmaps
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <param name="y">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap operator &(RoaringBitmap x, RoaringBitmap y)
        {
            return new RoaringBitmap(x.m_HighLowContainer & y.m_HighLowContainer);
        }

        /// <summary>
        ///     Bitwise Not operation of a RoaringBitmap
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap operator ~(RoaringBitmap x)
        {
            return new RoaringBitmap(~x.m_HighLowContainer);
        }

        /// <summary>
        ///     Bitwise Xor operation of two RoaringBitmaps
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <param name="y">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap operator ^(RoaringBitmap x, RoaringBitmap y)
        {
            return new RoaringBitmap(x.m_HighLowContainer ^ y.m_HighLowContainer);
        }

        /// <summary>
        ///     Bitwise AndNot operation of two RoaringBitmaps
        /// </summary>
        /// <param name="x">RoaringBitmap</param>
        /// <param name="y">RoaringBitmap</param>
        /// <returns>RoaringBitmap</returns>
        public static RoaringBitmap AndNot(RoaringBitmap x, RoaringBitmap y)
        {
            return new RoaringBitmap(RoaringArray.AndNot(x.m_HighLowContainer, y.m_HighLowContainer));
        }



        /// <summary>
        ///     Deserializes a RoaringBitmap from a stream using the 'official' RoaringBitmap file format
        /// </summary>
        /// <param name="stream">Stream</param>
        public static RoaringBitmap Deserialize(Stream stream)
        {
            var ra = RoaringArray.Deserialize(stream);
            return new RoaringBitmap(ra);
        }

        public static RoaringBitmap DeserializeReusingStream(BinaryReader binaryReader)
        {
            var ra = RoaringArray.Deserialize(binaryReader);
            return new RoaringBitmap(ra);
        }

        /// <summary>
        ///     Add a value to the current bitmap
        /// </summary>
        /// <param name="value">Value</param>
        public void AddInPlace(int value)
        {
            var key = Util.HighBits(value);

            var index = Array.BinarySearch(m_HighLowContainer.m_Keys, key);
            bool containerExists = index > -1;

            var lbValue = new ushort[] { Util.LowBits(value) };
            var newContainer = BitmapContainer.Create(lbValue);

            InlineAdd(key, index, containerExists, newContainer);
        }

        /// <summary>
        ///     Add a list of values to the current bitmap
        /// </summary>
        /// <param name="values">Values</param>
        public void AddInPlaceMany(IEnumerable<int> values)
        {
            var groupedValues = GetGroupedValues(values);
            foreach (var bucketGroup in groupedValues)
            {
                var key = bucketGroup.Key;
                var index = Array.BinarySearch(m_HighLowContainer.m_Keys, key);
                bool containerExists = index > -1;

                Container newContainer;
                var arrayContainerValues = bucketGroup.Select(Util.LowBits).ToArray();
                if (bucketGroup.Count() > Container.MaxSize)
                {
                    newContainer = BitmapContainer.Create(arrayContainerValues);
                }
                else
                {
                    newContainer = ArrayContainer.Create(arrayContainerValues);
                }

                InlineAdd(key, index, containerExists, newContainer);
            }
        }

        private static IEnumerable<IGrouping<ushort, int>> GetGroupedValues(IEnumerable<int> values)
        {
            return values.Distinct().OrderBy(t => t).GroupBy(Util.HighBits);
        }

        private void InlineAdd(ushort key, int index, bool containerExists, Container newContainer)
        {
            if (!containerExists)
            {
                var position = ~index;
                var currentLength = m_HighLowContainer.m_Keys.Length;
                var newKeys = new ushort[currentLength + 1];

                // add key
                Array.Copy(m_HighLowContainer.m_Keys, 0, newKeys, 0, position);
                newKeys[position] = key;
                if (position + 1 < newKeys.Length)
                    Array.Copy(m_HighLowContainer.m_Keys, position, newKeys, position + 1, currentLength - position);
                m_HighLowContainer.m_Keys = newKeys;

                // add data
                var newData = new Container[currentLength + 1];
                Array.Copy(m_HighLowContainer.m_Values, 0, newData, 0, position);
                newData[position] = newContainer;
                if (position + 1 < newKeys.Length)
                    Array.Copy(m_HighLowContainer.m_Values, position, newData, position + 1, currentLength - position);
                m_HighLowContainer.m_Values = newData;
                m_HighLowContainer.m_Size++;
                m_HighLowContainer._cardinality += newContainer.Cardinality;
            }
            else
            {
                // merge the container
                var len = m_HighLowContainer.m_Values[index].Cardinality;

                // heuristic - if the array is already of a certain size, we may save some time
                Container.InlineOrWith(ref m_HighLowContainer.m_Values[index], newContainer);
                var len2 = m_HighLowContainer.m_Values[index].Cardinality;
                if (len2 > len)
                {
                    m_HighLowContainer._cardinality += len2 - len;
                }
            }
        }

        /// <summary>
        ///     Remove a value from the current bitmap
        /// </summary>
        /// <param name="value">Value</param>
        public void RemoveInPlace(int value)
        {
            var key = Util.HighBits(value);

            var index = Array.BinarySearch(m_HighLowContainer.m_Keys, key);
            bool containerExists = index > -1;
            if (containerExists)
            {
                var lbValue = new ushort[] { Util.LowBits(value) };
                var containerToRemove = BitmapContainer.Create(lbValue);

                InlineRemove(index, containerToRemove);
            }
        }
        /// <summary>
        ///     Remove a list of values from the current bitmap
        /// </summary>
        /// <param name="values">Values</param>
        public void RemoveInPlaceMany(IEnumerable<int> values)
        {
            var groupedValues = GetGroupedValues(values);
            foreach (var bucketGroup in groupedValues)
            {
                var key = bucketGroup.Key;
                var index = Array.BinarySearch(m_HighLowContainer.m_Keys, key);
                bool containerExists = index > -1;

                if (containerExists)
                {
                    Container containerToRemove;
                    var arrayContainerValues = bucketGroup.Select(Util.LowBits).ToArray();
                    if (bucketGroup.Count() > Container.MaxSize)
                    {
                        containerToRemove = BitmapContainer.Create(arrayContainerValues);
                    }
                    else
                    {
                        containerToRemove = ArrayContainer.Create(arrayContainerValues);
                    }

                    InlineRemove(index, containerToRemove);
                }
            }
        }
        private void InlineRemove(int index, Container containerToRemove)
        {
            var len = m_HighLowContainer.m_Values[index].Cardinality;

            Container.InlineAndNotWith(ref m_HighLowContainer.m_Values[index], containerToRemove);
            var len2 = m_HighLowContainer.m_Values[index].Cardinality;
            if (len2 < len)
            {
                m_HighLowContainer._cardinality -= len - len2;
            }
            if (len2 == 0)
            {
                //Container is empty, we need to remove it
                var position = index;
                var currentLength = m_HighLowContainer.m_Keys.Length;
                var newKeys = new ushort[currentLength - 1];

                // remove key
                Array.Copy(m_HighLowContainer.m_Keys, 0, newKeys, 0, position);
                if (position < newKeys.Length)
                    Array.Copy(m_HighLowContainer.m_Keys, position + 1, newKeys, position , currentLength - position - 1);
                m_HighLowContainer.m_Keys = newKeys;

                // remove data
                var newData = new Container[currentLength - 1];
                Array.Copy(m_HighLowContainer.m_Values, 0, newData, 0, position);
                if (position < newKeys.Length)
                    Array.Copy(m_HighLowContainer.m_Values, position + 1, newData, position, currentLength - position - 1);

                m_HighLowContainer.m_Values = newData;
                m_HighLowContainer.m_Size--;
            }
        }
    }
}
