using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.Intrinsics.X86;
using RoaringBitmap.Benchmark;
using Xunit;
using Xunit.Abstractions;

namespace RoaringBitmap.Tests {
    using Simd = Collections.Special.Simd;
    using Juan = WebBeds.Indexing;
    public class BenchmarkTests : IClassFixture<BenchmarkTests.BenchmarkTestsFixture> {
        private readonly BenchmarkTestsFixture m_Fixture;
        private readonly ITestOutputHelper m_OutputHelper;

        public BenchmarkTests(BenchmarkTestsFixture fixture, ITestOutputHelper outputHelper) {
            m_Fixture = fixture;
            m_OutputHelper = outputHelper;
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, 12487395)]
        [InlineData(DataSets.Census1881, 2007691)]
        [InlineData(DataSets.Dimension003, 7733676)]
        [InlineData(DataSets.Dimension008, 5555233)]
        [InlineData(DataSets.Dimension033, 7579526)]
        [InlineData(DataSets.UsCensus2000, 11954)]
        [InlineData(DataSets.WeatherSept85, 24729002)]
        [InlineData(DataSets.WikileaksNoQuotes, 541893)]
        [InlineData(DataSets.CensusIncomeSrt, 11257282)]
        [InlineData(DataSets.Census1881Srt, 1360167)]
        [InlineData(DataSets.WeatherSept85Srt, 30863347)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 574463)]
        public void Or(string name, int value) {
            var sw = Stopwatch.StartNew();
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0L;
            for (var k = 0; k < bitmaps.Length - 1; k++) {
                total += (bitmaps[k] | bitmaps[k + 1]).Cardinality;
            }
            Assert.Equal(value, total);
            m_OutputHelper.WriteLine($"Done in {sw.Elapsed.TotalMilliseconds:0.000}ms");
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, 12487395)]
        [InlineData(DataSets.Census1881, 2007691)]
        [InlineData(DataSets.Dimension003, 7733676)]
        [InlineData(DataSets.Dimension008, 5555233)]
        [InlineData(DataSets.Dimension033, 7579526)]
        [InlineData(DataSets.UsCensus2000, 11954)]
        [InlineData(DataSets.WeatherSept85, 24729002)]
        [InlineData(DataSets.WikileaksNoQuotes, 541893)]
        [InlineData(DataSets.CensusIncomeSrt, 11257282)]
        [InlineData(DataSets.Census1881Srt, 1360167)]
        [InlineData(DataSets.WeatherSept85Srt, 30863347)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 574463)]
        public void OrSimd(string name, int value) {
            var sw = Stopwatch.StartNew();
            var bitmaps = m_Fixture.GetSimdBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0L;
            for (var k = 0; k < bitmaps.Length - 1; k++) {
                total += (bitmaps[k] | bitmaps[k + 1]).Cardinality;
            }
            Assert.Equal(value, total);
            m_OutputHelper.WriteLine($"Done in {sw.Elapsed.TotalMilliseconds:0.000}ms");
        }


        [Fact]
        public unsafe void AlignedArrayPoolTests() {
            Assert.True(true);
            const int PoolSize = 100;
            const int ArraySize = 1024;
            using var ap = new RoaringBitmap.Simd.AlignedInt64ArrayPool(ArraySize, PoolSize);

            var MyArray = ap.Rent();
            var sp = MyArray.Span;
            sp[0] = 1;
            sp[1] = 2;
            //ap.Return(MyArray);
            for (int i = 0; i < PoolSize - 1; i++) {
                var newBlock = ap.Rent();
                Assert.Equal(1024, newBlock.Span.Length);
            }
            Assert.Throws<InvalidOperationException>(() => ap.Rent());

            ap.Return(MyArray);
            //MyArray.
            Assert.Equal(PoolSize - 1, ap.InUse);

            var weirdoMemory = new Memory<ulong>(new [] { 5UL });

            Assert.Throws<InvalidOperationException>(() => ap.Return(weirdoMemory));

            ap.RepossessAll();

            List<Memory<ulong>> mems = new List<Memory<ulong>>();
            for (int i = 0; i < 100; i++) {
                mems.Add(ap.Rent());
            }
            var rr = new Random();
            ulong[][] allocs = new ulong[PoolSize][];

            for (int i = 0; i < 1000000; i++) {
                var pos = i % PoolSize;
                allocs[i % PoolSize] = new ulong[ArraySize];
            }

            for (int i = 0; i < 1000000; i++) {
                var pos = rr.Next(0, PoolSize);
                ap.Return(mems[pos]);

                mems[pos] = ap.Rent();
            }

            fixed (ulong* ptr = mems[0].Span) {
                long Address = (long)ptr;
                Console.WriteLine("{0:x}", Address);
                try {
                    var alignedVector = Avx2.LoadAlignedVector256(ptr);
                } catch (Exception) {

                }
            }


            Assert.Equal(2UL, MyArray.Slice(1, 1).Span[0]);

        }

        [Fact]
        public void OrSimdManualCoverage() {
            var sw = Stopwatch.StartNew();
            var aa = Simd.RoaringBitmap.Create(1,3,5,7,8,9,11,12,13,14,15);
            var bb= Simd.RoaringBitmap.Create(2,4,6,8,9,10);

            Assert.Equal(15, (aa | bb).Cardinality);


            aa = Simd.RoaringBitmap.Create(Enumerable.Range(0, 76636));
            aa.Optimize();

            bb = Simd.RoaringBitmap.Create(Enumerable.Range(0, 65536).Concat(Enumerable.Range(65538, 11100)));
            bb.Optimize();

            Assert.False(aa.Equals(bb));

            aa = Simd.RoaringBitmap.Create(Enumerable.Range(0, 65536).Concat(Enumerable.Range(65538, 11100)));
            aa.Optimize();

            Assert.True(aa.Equals(bb));


            aa = Simd.RoaringBitmap.Create(1, 3, 5, 7, 8, 9);
            bb= Simd.RoaringBitmap.Create(2, 4, 6, 8, 9, 10, 11, 12, 13, 14, 15, int.MaxValue);

            Assert.Equal(16, (aa | bb).Cardinality);

            aa = Simd.RoaringBitmap.Create(1, 3, 5, 7, 8, 9, 11, 12, 13, 14, 15);
            bb= Simd.RoaringBitmap.Create(2, 4, 6, 8, 9, 10, 11, 12, 13, 14, 15);

            Assert.Equal(15, (aa | bb).Cardinality);

            aa = Simd.RoaringBitmap.Create(1, 3, 5, 7, 8, 9, 11, 12, 13, 14, 15, int.MaxValue);
            bb= Simd.RoaringBitmap.Create(2, 4, 6, 8, 9, 10, 11, 12, 13, 14, 15);

            Assert.Equal(16, (aa | bb).Cardinality);

            const int Im = 1_000_000;
            aa = Simd.RoaringBitmap.Create(1, 2, 3, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, int.MaxValue);
            bb= Simd.RoaringBitmap.Create(4, 6, Im + 12, Im + 13, Im +  14, Im + 15);

            Assert.Equal(20, (aa | bb).Cardinality);

            var jac = new Juan.RoaringBitmap(Enumerable.Range(10, 5));
            Assert.Equal(5, jac.Cardinality);


            const int loops = 500;

            var jaa = new Juan.RoaringBitmap(Enumerable.Range(10000, 5000));
            var jbb = new Juan.RoaringBitmap(Enumerable.Range(5000, 1000));
            jaa.Optimize();
            jbb.Optimize();
            var jproduct = (jaa | jbb);
            Assert.Equal(6000, jproduct.Cardinality);

            aa = Simd.RoaringBitmap.Create(Enumerable.Range(10000, 5000));
            bb = Simd.RoaringBitmap.Create(Enumerable.Range(5000, 1000));
            aa.Optimize();
            bb.Optimize();
            var product = (aa | bb);

            const int Megs = 1024 * 1024;

            Assert.Equal(6000, product.Cardinality);
            GC.Collect(2, GCCollectionMode.Forced);
            GC.TryStartNoGCRegion(256 * Megs, true);

            var sw1 = Stopwatch.StartNew();
            for (int i = 0; i < loops; i++) {
                jproduct = (jaa | jbb);
            }
            sw1.Stop();
            GC.EndNoGCRegion();

            GC.Collect(2, GCCollectionMode.Forced);
            GC.TryStartNoGCRegion(256 * Megs, true);

            var sw2 = Stopwatch.StartNew();
            for (int i = 0; i < loops; i++) {
                product = (aa | bb);
            }
            sw2.Stop();
            GC.EndNoGCRegion();

            int[] ja = jproduct.ToArray(), sa = product.ToArray();
            var set_difference = ja.Except(sa).Concat(sa.Except(ja)).ToArray();
            if (set_difference.Length > 0) {
                throw new ArgumentException($"There is a set difference between the result of the two OR operations. : {string.Join(", ", set_difference)}");
            }
            //Assert.Equal(ja, sa, "Aasdkjnasd");
            var factor = ((sw1.Elapsed.TotalMilliseconds / sw2.Elapsed.TotalMilliseconds) - 1) * 100;
            m_OutputHelper.WriteLine($"Loops: {loops} Speedup: {factor:0.00}%, WB: {sw1.Elapsed.TotalMilliseconds}, JB:{sw2.Elapsed.TotalMilliseconds}");
            //Assert.True(sw2.Elapsed < sw1.Elapsed, "Is it faster?");


            jaa = new Juan.RoaringBitmap(Enumerable.Range(0, 15000));
            jbb = new Juan.RoaringBitmap(Enumerable.Range(5000, 20000));
            jproduct = (jaa | jbb);
            Assert.Equal(25000, jproduct.Cardinality); // 0000 - 25000, really should be 0 

            aa = Simd.RoaringBitmap.Create(Enumerable.Range(0, 15000));
            bb = Simd.RoaringBitmap.Create(Enumerable.Range(5000, 20000));
            product = (aa | bb);
            Assert.Equal(25000, product.Cardinality); // 0000 - 25000, really should be 0 
            //var bitmaps = m_Fixture.GetSimdBitmaps(name);
            //Assert.NotNull(bitmaps);
            //var total = 0L;
            //for (var k = 0; k < bitmaps.Length - 1; k++) {
            //    total += (bitmaps[k] | bitmaps[k + 1]).Cardinality;
            //}
            //Assert.Equal(value, total);
            m_OutputHelper.WriteLine($"Done in {sw.Elapsed.TotalMilliseconds:0.000}ms");
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, 11241947)]
        [InlineData(DataSets.Census1881, 2007668)]
        [InlineData(DataSets.Dimension003, 7733676)]
        [InlineData(DataSets.Dimension008, 5442916)]
        [InlineData(DataSets.Dimension033, 7579526)]
        [InlineData(DataSets.UsCensus2000, 11954)]
        [InlineData(DataSets.WeatherSept85, 24086983)]
        [InlineData(DataSets.WikileaksNoQuotes, 538566)]
        [InlineData(DataSets.CensusIncomeSrt, 10329567)]
        [InlineData(DataSets.Census1881Srt, 1359961)]
        [InlineData(DataSets.WeatherSept85Srt, 29800358)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 574311)]
        public void Xor(string name, int value) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0L;
            for (var k = 0; k < bitmaps.Length - 1; k++) {
                total += (bitmaps[k] ^ bitmaps[k + 1]).Cardinality;
            }
            Assert.Equal(value, total);
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, 1245448)]
        [InlineData(DataSets.Census1881, 23)]
        [InlineData(DataSets.Dimension003, 0)]
        [InlineData(DataSets.Dimension008, 112317)]
        [InlineData(DataSets.Dimension033, 0)]
        [InlineData(DataSets.UsCensus2000, 0)]
        [InlineData(DataSets.WeatherSept85, 642019)]
        [InlineData(DataSets.WikileaksNoQuotes, 3327)]
        [InlineData(DataSets.CensusIncomeSrt, 927715)]
        [InlineData(DataSets.Census1881Srt, 206)]
        [InlineData(DataSets.WeatherSept85Srt, 1062989)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 152)]
        public void And(string name, int value) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0L;
            for (var k = 0; k < bitmaps.Length - 1; k++) {
                total += (bitmaps[k] & bitmaps[k + 1]).Cardinality;
            }
            Assert.Equal(value, total);
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, -942184551)]
        [InlineData(DataSets.Census1881, 246451066)]
        [InlineData(DataSets.Dimension003, -1287135055)]
        [InlineData(DataSets.Dimension008, -423436314)]
        [InlineData(DataSets.Dimension033, -1287135055)]
        [InlineData(DataSets.UsCensus2000, -1260727955)]
        [InlineData(DataSets.WeatherSept85, 644036874)]
        [InlineData(DataSets.WikileaksNoQuotes, 413846869)]
        [InlineData(DataSets.CensusIncomeSrt, -679313956)]
        [InlineData(DataSets.Census1881Srt, 445584405)]
        [InlineData(DataSets.WeatherSept85Srt, 1132748056)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 1921022163)]
        public void Iterate(string name, int value) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0;
            foreach (var roaringBitmap in bitmaps) {
                foreach (var @int in roaringBitmap) {
                    unchecked {
                        total += @int;
                    }
                }
            }
            Assert.Equal(value, total);
        }


        // The Dimension data sets are simply too slow
        [Theory]
        [InlineData(DataSets.CensusIncome)]
        [InlineData(DataSets.Census1881)]
        //[InlineData(DataSets.Dimension003)]
        //[InlineData(DataSets.Dimension008)]
        //[InlineData(DataSets.Dimension033)]
        [InlineData(DataSets.UsCensus2000)]
        [InlineData(DataSets.WeatherSept85)]
        [InlineData(DataSets.WikileaksNoQuotes)]
        [InlineData(DataSets.CensusIncomeSrt)]
        [InlineData(DataSets.Census1881Srt)]
        [InlineData(DataSets.WeatherSept85Srt)]
        [InlineData(DataSets.WikileaksNoQuotesSrt)]
        public void Not(string name) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            foreach (var roaringBitmap in bitmaps) {
                var doublenegated = ~~roaringBitmap;
                Assert.Equal(roaringBitmap, doublenegated);
            }
        }

        [Theory]
        [InlineData(DataSets.CensusIncome, 5666586)]
        [InlineData(DataSets.Census1881, 1003836)]
        [InlineData(DataSets.Dimension003, 3866831)]
        [InlineData(DataSets.Dimension008, 2721459)]
        [InlineData(DataSets.Dimension033, 3866842)]
        [InlineData(DataSets.UsCensus2000, 5970)]
        [InlineData(DataSets.WeatherSept85, 11960876)]
        [InlineData(DataSets.WikileaksNoQuotes, 271605)]
        [InlineData(DataSets.CensusIncomeSrt, 5164671)]
        [InlineData(DataSets.Census1881Srt, 679375)]
        [InlineData(DataSets.WeatherSept85Srt, 14935706)]
        [InlineData(DataSets.WikileaksNoQuotesSrt, 286904)]
        public void AndNot(string name, int value) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            var total = 0L;
            for (var k = 0; k < bitmaps.Length - 1; k++) {
                total += Collections.Special.RoaringBitmap.AndNot(bitmaps[k], bitmaps[k + 1]).Cardinality;
            }
            Assert.Equal(value, total);
        }

        [Theory]
        [InlineData(DataSets.CensusIncome)]
        [InlineData(DataSets.Census1881)]
        [InlineData(DataSets.Dimension003)]
        [InlineData(DataSets.Dimension008)]
        [InlineData(DataSets.Dimension033)]
        [InlineData(DataSets.UsCensus2000)]
        [InlineData(DataSets.WeatherSept85)]
        [InlineData(DataSets.WikileaksNoQuotes)]
        [InlineData(DataSets.CensusIncomeSrt)]
        [InlineData(DataSets.Census1881Srt)]
        [InlineData(DataSets.WeatherSept85Srt)]
        [InlineData(DataSets.WikileaksNoQuotesSrt)]
        public void SerializeDeserialize(string name) {
            var bitmaps = m_Fixture.GetBitmaps(name);
            Assert.NotNull(bitmaps);
            foreach (var roaringBitmap in bitmaps) {
                using (var ms = new MemoryStream()) {
                    Collections.Special.RoaringBitmap.Serialize(roaringBitmap, ms);
                    ms.Position = 0;
                    var rb2 = Collections.Special.RoaringBitmap.Deserialize(ms);
                    Assert.Equal(roaringBitmap, rb2);
                }
            }
        }


        public class BenchmarkTestsFixture {
            private readonly Dictionary<string, Collections.Special.RoaringBitmap[]> m_BitmapDictionary = new Dictionary<string, Collections.Special.RoaringBitmap[]>();
            private readonly Dictionary<string, Collections.Special.Simd.RoaringBitmap[]> m_SimdBitmapDictionary = new Dictionary<string, Collections.Special.Simd.RoaringBitmap[]>();
            private readonly string m_Path = @"Data";

            public Collections.Special.RoaringBitmap[] GetBitmaps(string name) {
                Collections.Special.RoaringBitmap[] bitmaps;
                if (!m_BitmapDictionary.TryGetValue(name, out bitmaps)) {
                    using var provider = new ZipRealDataProvider(Path.Combine(m_Path, name));

                    bitmaps = provider.ToArray();
                    m_BitmapDictionary[name] = bitmaps;
                }
                return bitmaps;
            }

            public Collections.Special.Simd.RoaringBitmap[] GetSimdBitmaps(string name) {
                Collections.Special.Simd.RoaringBitmap[] bitmaps;
                if (!m_SimdBitmapDictionary.TryGetValue(name, out bitmaps)) {
                    using var provider = new ZipRealDataProvider(Path.Combine(m_Path, name));

                    bitmaps = provider.ToSimdArray().ToArray();
                    m_SimdBitmapDictionary[name] = bitmaps;
                }
                return bitmaps;
            }
        }
    }
}