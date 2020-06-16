using System;
using System.Collections.Generic;
using System.Text;

namespace WebBeds.Indexing
{
    public class Index<T>
    {
        protected Dictionary<T, RoaringBitmap> items = new Dictionary<T, RoaringBitmap>();

        public void Optmize()
        {
            foreach (var item in items.Values)
            {
                item.Optimize();

            }
        }
        public IEnumerable<T> ListKeys()
        {
            foreach (var item in items.Keys)
            {
                yield return item;
            }
        }
        public void Add(T value, IEnumerable<int> ids)
        {
            if (!items.TryGetValue(value, out var bmp))
            {
                bmp = new RoaringBitmap(ids);
                items[value] = bmp;
            }

        }
        public void AddOrUpdate(T value, IEnumerable<int> ids)
        {
            if (!items.TryGetValue(value, out var bmp))
            {
                bmp = new RoaringBitmap(ids);
                items[value] = bmp;
            }
            else
            {
                bmp.AddInPlaceMany(ids);
            }
        }

        public void AddOrUpdate(T value, int id)
        {
            if (!items.TryGetValue(value, out var bmp))
            {
                bmp = new RoaringBitmap(id);
                items[value] = bmp;
            }
            else
            {
                bmp.AddInPlace(id);
            }
        }

        public void Set(T value, RoaringBitmap bmp)
        {
            items[value] = bmp;
        }
        RoaringBitmap emptyBmp = new RoaringBitmap();
        public RoaringBitmap Get(T value, bool nullIfNotFound = false)
        {
            if (!items.TryGetValue(value, out var bmp) && !nullIfNotFound)
            {
                bmp = emptyBmp;
            }

            return bmp;
        }
        public IEnumerable<RoaringBitmap> GetMany(IEnumerable<T> values)
        {
            RoaringBitmap bmp;
            foreach (var value in values)
            {
                if (items.TryGetValue(value, out bmp))
                {
                    yield return bmp;
                }
            }
        }
        public void Clear()
        {
            items.Clear();
        }
    }
}
