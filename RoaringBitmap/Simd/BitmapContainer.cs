using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Collections.Special.Simd {
    internal class BitmapContainer : Container, IEquatable<BitmapContainer> {

        private static ArrayPool<ulong> pool = ArrayPool<ulong>.Shared;

        /// <summary>
        /// MUST be divisible by Vector256<byte>.Count
        /// </summary>
        private const int BitmapLength = 1024;
        public static readonly BitmapContainer One;
        private readonly ulong[] m_Bitmap;
        private readonly int m_Cardinality;
        private readonly bool pooledBuffer;

        static BitmapContainer() {
            var data = new ulong[BitmapLength];
            for (var i = 0; i < BitmapLength; i++) {
                data[i] = ulong.MaxValue;
            }
            One = new BitmapContainer(1 << 16, data, false);
        }

        ~BitmapContainer() {
            if (pooledBuffer && m_Bitmap != null) {
                pool.Return(m_Bitmap);
            }
        }
        private BitmapContainer(int cardinality) {
            m_Bitmap = pool.Rent(BitmapLength);
            pooledBuffer = true;
            //m_Bitmap = new ulong[BitmapLength];
            m_Cardinality = cardinality;
        }

        private BitmapContainer(int cardinality, ulong[] data, bool isPooled) {
            m_Bitmap = data;
            m_Cardinality = cardinality;
            pooledBuffer = isPooled;
        }

        private BitmapContainer(int cardinality, ushort[] values, bool negated) : this(negated ? MaxCapacity - cardinality : cardinality) {
            if (negated) {
                for (var i = 0; i < BitmapLength; i++) {
                    m_Bitmap[i] = ulong.MaxValue;
                }
                for (var i = 0; i < cardinality; i++) {
                    var v = values[i];
                    m_Bitmap[v >> 6] &= ~(1UL << v);
                }
            } else {
                for (var i = 0; i < cardinality; i++) {
                    var v = values[i];
                    m_Bitmap[v >> 6] |= 1UL << v;
                }
            }
        }

        protected internal override int Cardinality => m_Cardinality;

        public override int ArraySizeInBytes => MaxCapacity / 8;

        public unsafe bool Equals(BitmapContainer other) {
            if (ReferenceEquals(this, other)) {
                return true;
            }
            if (ReferenceEquals(null, other)) {
                return false;
            }
            if (m_Cardinality != other.m_Cardinality) {
                return false;
            }

            if (m_Cardinality == (BitmapLength * 64) && other.m_Cardinality == (BitmapLength * 64)) {
                return true;
            }

            fixed (ulong* ptra = m_Bitmap)
            fixed (ulong* ptrb = other.m_Bitmap) {
                var AllSet = Vector256.Create((byte)0xFF);
                for (int i = 0; i < BitmapLength; i+=16) {
                    var x = Avx2.CompareEqual(Avx2.LoadDquVector256(ptra + i), Avx2.LoadDquVector256(ptrb + i)).AsByte();
                    if (!Avx2.TestC(x, AllSet))
                        return false;
                }
            }

            return true;
        }


        internal static BitmapContainer Create(ushort[] values) {
            return new BitmapContainer(values.Length, values, false);
        }

        internal static BitmapContainer Create(int cardinality, ushort[] values) {
            return new BitmapContainer(cardinality, values, false);
        }

        internal static BitmapContainer Create(int cardinality, ushort[] values, bool negated) {
            return new BitmapContainer(cardinality, values, negated);
        }


        internal static BitmapContainer CreateXor(ushort[] first, int firstCardinality, ushort[] second, int secondCardinality) {
            var data = pool.Rent(BitmapLength);
            for (var i = 0; i < firstCardinality; i++) {
                var v = first[i];
                data[v >> 6] ^= 1UL << v;
            }

            for (var i = 0; i < secondCardinality; i++) {
                var v = second[i];
                data[v >> 6] ^= 1UL << v;
            }
            var cardinality = Util.BitCount(data);
            return new BitmapContainer(cardinality, data, true);
        }

        /// <summary>
        ///     Java version has an optimized version of this, but it's using bitcount internally which should make it slower in
        ///     .NET
        /// </summary>
        public static Container operator &(BitmapContainer x, BitmapContainer y) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(AndInternal(data, y.m_Bitmap), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }

        private static ulong[] Clone(ulong[] data, bool pooled) {
            var result = pooled ? pool.Rent(BitmapLength) : new ulong[BitmapLength];
            Buffer.BlockCopy(data, 0, result, 0, BitmapLength * sizeof(ulong));
            return result;
        }

        public static ArrayContainer operator &(BitmapContainer x, ArrayContainer y) {
            return y & x;
        }

        public static BitmapContainer operator |(BitmapContainer x, BitmapContainer y) {
            var data = Clone(x.m_Bitmap, true);
            return new BitmapContainer(OrInternal(data, y.m_Bitmap), data, true);
        }

        public static BitmapContainer operator |(BitmapContainer x, ArrayContainer y) {
            var data = Clone(x.m_Bitmap, true);
            return new BitmapContainer(x.m_Cardinality + y.OrArray(data), data, true);
        }

        public static Container operator ~(BitmapContainer x) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(NotInternal(data), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }

        /// <summary>
        ///     Java version has an optimized version of this, but it's using bitcount internally which should make it slower in
        ///     .NET
        /// </summary>
        public static Container operator ^(BitmapContainer x, BitmapContainer y) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(XorInternal(data, y.m_Bitmap), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }


        public static Container operator ^(BitmapContainer x, ArrayContainer y) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(x.m_Cardinality + y.XorArray(data), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }

        public static Container AndNot(BitmapContainer x, BitmapContainer y) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(AndNotInternal(data, y.m_Bitmap), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }

        public static Container AndNot(BitmapContainer x, ArrayContainer y) {
            var data = Clone(x.m_Bitmap, true);
            var bc = new BitmapContainer(x.m_Cardinality + y.AndNotArray(data), data, true);
            return bc.m_Cardinality <= MaxSize ? (Container)ArrayContainer.Create(bc) : bc;
        }

        private static int XorInternal(ulong[] first, ulong[] second) {
            for (var k = 0; k < BitmapLength; k++) {
                first[k] = first[k] ^ second[k];
            }
            var c = Util.BitCount(first);
            return c;
        }

        private static int AndNotInternal(ulong[] first, ulong[] second) {
            for (var k = 0; k < first.Length; k++) {
                first[k] &= ~second[k];
            }
            var c = Util.BitCount(first);
            return c;
        }

        private unsafe static int NotInternal(ulong[] data) {
            fixed (ulong* ptrul = data) {
                byte* ptr = (byte*)ptrul;
                int i = 0;
                var allOnes = Vector256.Create((byte)0xff);

                // there is no NOT operation, the closest is XOR with allones
                while (i < BitmapLength) {
                    var va = Avx2.Xor(Avx.LoadVector256(ptr + i), allOnes);
                    Avx.Store(ptr + i, va);
                    i += Vector256<byte>.Count;
                }
            }
            var c = Util.BitCount(data);
            return c;
        }

        private static int OrInternal(ulong[] first, ulong[] second) {

            int cardinality = 0;
            unsafe {
                fixed (ulong* srcPtrRO = first)
                fixed (ulong* cmpPtrRO = second) {
                    ulong* firstPtr = srcPtrRO;
                    ulong* secondPtr = cmpPtrRO;
                    var alignedfirst = (ulong*)(((ulong)firstPtr + 31UL) & ~31UL);
                    var alignedsecond = (ulong*)(((ulong)secondPtr + 31UL) & ~31UL);
                    var i = 0;

                    Vector256<ulong> va;
                    bool aligned = alignedfirst == firstPtr && alignedsecond == secondPtr;

                    long diffA = 0, diffB = 0;
                    //https://stackoverflow.com/questions/1951290/memory-alignment-of-classes-in-c

                    /*
Interesting look in the gears that run the machine. I have a bit of a problem explaining why there are multiple distinct values (I got 4) when a double can be aligned only two ways. 
                    I think alignment to the CPU cache line plays a role as well, although that only adds up to 3 possible timings.
Well, nothing you can do about it, the CLR only promises alignment for 4 byte values so that atomic updates on 32-bit machines are guaranteed. 
                    This is not just an issue with managed code, C/C++ has this problem too. Looks like the chip makers need to solve this one.
If it is critical then you could allocate unmanaged memory with Marshal.AllocCoTaskMem() and use an unsafe pointer that you can align just right.
                    Same kind of thing you'd have to do if you allocate memory for code that uses SIMD instructions, they require a 16 byte alignment. Consider it a desperation-move though.
                     */
                    if (!aligned) {
                        diffA = alignedfirst - firstPtr;
                        diffB = alignedsecond - secondPtr;
                        Console.WriteLine($"Diff A : {diffA}, Diff B: {diffB}");
                        if (diffA == diffB) { // if we can treat them as aligned, but with an offset, then come back and do the rest...
                            firstPtr = alignedfirst;
                            secondPtr = alignedsecond;
                            aligned = true;
                        }
                    }

                    if (aligned) {
                        while (i < BitmapLength) {
                            va = Avx.LoadAlignedVector256(alignedfirst);
                            va = Avx2.Or(va, Avx.LoadAlignedVector256(alignedsecond));
                            Avx.StoreAligned(alignedfirst, va);

                            //cardinality += (int)Popcnt.X64.PopCount(*alignedfirst++);
                            //cardinality += (int)Popcnt.X64.PopCount(*alignedfirst++);
                            //cardinality += (int)Popcnt.X64.PopCount(*alignedfirst++);
                            //cardinality += (int)Popcnt.X64.PopCount(*alignedfirst++);

                            //alignedfirst  +=4;
                            alignedsecond +=4;
                            i += 4; // 256 bit / 64 bit ulong
                        }
                    } else {
                        //var vaa = Avx.LoadAlignedVector256(srcPtr);
                        //var vab = Avx.LoadAlignedVector256(cmpPtr);
                        while (i < BitmapLength) {
                            va = Avx2.Or(Avx.LoadVector256(firstPtr + i), Avx.LoadVector256(secondPtr + i));
                            Avx.Store(firstPtr, va);

                            //cardinality += (int)Popcnt.X64.PopCount(*firstPtr++);
                            //cardinality += (int)Popcnt.X64.PopCount(*firstPtr++);
                            //cardinality += (int)Popcnt.X64.PopCount(*firstPtr++);
                            //cardinality += (int)Popcnt.X64.PopCount(*firstPtr++);
                            i += 4; // 256 bit / 64 bit ulong
                        }
                    }
                    if (i != BitmapLength) {
                        // we jodge-jobbed an aligned-compare on unaligned data, and need to include recover the first and last blocks
                        // as we only do this when the misalignment is equal ( 1/32 chance ) its an edge case, but when its hit, there is a marked improvement
                        long a = diffA;
                    }
                }
            }
            return Util.BitCount(first);
            //return cardinality;
            //for (var k = 0; k < BitmapLength; k++) {
            //    first[k] = first[k] | second[k];
            //}
            //var c = Util.BitCount(first);
            //return c;
        }

        private static int AndInternal(ulong[] first, ulong[] second) {
            for (var k = 0; k < BitmapLength; k++) {
                first[k] = first[k] & second[k];
            }
            var c = Util.BitCount(first);
            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(ushort x) {
            return Contains(m_Bitmap, x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Contains(ulong[] bitmap, ushort x) {
            return (bitmap[x >> 6] & (1UL << x)) != 0;
        }

        protected override bool EqualsInternal(Container other) {
            var bc = other as BitmapContainer;
            return (bc != null) && Equals(bc);
        }

        public override IEnumerator<ushort> GetEnumerator() {
            for (var k = 0; k < BitmapLength; k++) {
                var bitset = m_Bitmap[k];
                var shiftedK = k << 6;
                while (bitset != 0) {
                    var t = bitset & (~bitset + 1);
                    var result = (ushort) (shiftedK + Util.BitCount(t - 1));
                    yield return result;
                    bitset ^= t;
                }
            }
        }

        internal int FillArray(ushort[] data) {
            var pos = 0;
            for (var k = 0; k < BitmapLength; k++) {
                var bitset = m_Bitmap[k];
                var shiftedK = k << 6;
                while (bitset != 0) {
                    var t = bitset & (~bitset + 1);
                    data[pos++] = (ushort)(shiftedK + Util.BitCount(t - 1));
                    bitset ^= t;
                }
            }
            return m_Cardinality;
        }

        public override bool Equals(object obj) {
            var bc = obj as BitmapContainer;
            return (bc != null) && Equals(bc);
        }

        public override int GetHashCode() {
            unchecked {
                var code = 17;
                code = code * 23 + m_Cardinality;
                for (var i = 0; i < BitmapLength; i++) {
                    code = code * 23 + m_Bitmap[i].GetHashCode();
                }
                return code;
            }
        }

        public static void Serialize(BitmapContainer bc, BinaryWriter binaryWriter) {
            for (var i = 0; i < BitmapLength; i++) {
                binaryWriter.Write(bc.m_Bitmap[i]);
            }
        }

        public static BitmapContainer Deserialize(BinaryReader binaryReader, int cardinality) {
            var data = pool.Rent(BitmapLength);// new ulong[BitmapLength];
            for (var i = 0; i < BitmapLength; i++) {
                data[i] = binaryReader.ReadUInt64();
            }
            return new BitmapContainer(cardinality, data, true);
        }
    }
}