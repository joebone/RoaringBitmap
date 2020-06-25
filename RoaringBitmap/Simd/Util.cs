using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Collections.Special.Simd {
    /// <summary>
    ///     Pretty much everything in here are straight conversions from the original Util class in the java Roaring Bitmap
    ///     project.
    /// </summary>
    internal static class Util {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitCount(ulong value) => (int)Popcnt.X64.PopCount(value);

        /// <summary>
        ///     see https://en.wikipedia.org/wiki/Hamming_weight
        ///     Unfortunately there is no popcnt in c#
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int BitCount(ulong[] xArray) {
            if (xArray.Length <= (22048 / 8)) { // based on research papers, 256B or less should be faster to just use popcnt - and on skylake + clang, potentially less than 2K
                var result = 0;
                for (var i = 0; i < xArray.Length; i++) {
                    result += (int)Popcnt.X64.PopCount(xArray[i]);
                }
                return result;
            }

            //Faster Population Counts Using AVX2 Instructions : https://arxiv.org/pdf/1611.07612.pdf
            /*                                                   https://github.com/WojciechMula/sse-popcount
             Using the dedicated popcnt instruction for the
population of an array of words requires loading the
word (movq), counting the bits (popcnt), and then
adding the result to the total (addq). The load and
the popcnt can be combined into a single assembly
instruction, but internally they are executed as
separate µops, and thus each 64-bit word requires
three µops. Apart from minimal loop overhead,
these three operations can be executed in a single
cycle on a modern x64 superscalar processor, for a
throughput of just over one cycle per 8 B word.
• The AVX2 Harley-Seal function processes sixteen
256-bit vectors (512 B) with 98 µops: 16 loads
(vpmov), 32 bitwise ANDs (vpand), 15 bitwise ORs
(vpor), and 30 bitwise XORs (vpxor). Each 64-bit
word (8 B) thus takes just over 1.5 µops—about
half as many as required to use the builtin popcnt
instruction on the same input.
             */

            var result2 = 0;
            for (var i = 0; i < xArray.Length; i++) {
                result2 += (int)Popcnt.X64.PopCount(xArray[i]);
            }

            // Harley-Seal’s algorithm using Avx2
            var x = PopCountHarleySeal(xArray);

            if (x != result2)
                throw new NotImplementedException($"HarleySeal algo is busted, {x} != {result2}");

            return x;
            //Vector256<int> total // horizontal adds: https://www.felixcloutier.com/x86/phaddw:phaddd
        }

        private unsafe static int PopCountHarleySeal(ulong[] array) { // uint64_t avx_hs ( __m256i * d, uint64_t size ) {
           
            var total = Vector256<ulong>.Zero;
            var ones = Vector256<ulong>.Zero;
            var twos = Vector256<ulong>.Zero;
            var fours = Vector256<ulong>.Zero;
            var eights = Vector256<ulong>.Zero;
            var sixteens = Vector256<ulong>.Zero;

            var twosA = Vector256<ulong>.Zero;
            var twosB = Vector256<ulong>.Zero;
            var foursA = Vector256<ulong>.Zero;
            var foursB = Vector256<ulong>.Zero;
            var eightsA = Vector256<ulong>.Zero;
            var eightsB = Vector256<ulong>.Zero;
            int i = 0;
            int size = array.Length;
            //1500010 = 111010100110002
            fixed (ulong* data = array) {
                // 1024 will be done in 64 loops
                for (i = 0; i < size; i+=16) {

                    // group A
                    CarrySubtractAdd(ref twosA, ref ones, ones, Avx.LoadDquVector256(data + i + 0).AsUInt64(), Avx.LoadDquVector256(data + i + 1).AsUInt64());
                    CarrySubtractAdd(ref twosB, ref ones, ones, Avx.LoadDquVector256(data + i + 2).AsUInt64(), Avx.LoadDquVector256(data + i + 3).AsUInt64());
                    CarrySubtractAdd(ref foursA, ref twos, twos, twosA, twosB);// Avx.LoadDquVector256(data + i + 2).AsUInt64(), Avx.LoadDquVector256(data + i + 3).AsUInt64());
                    // +
                    CarrySubtractAdd(ref twosA, ref ones, ones, Avx.LoadDquVector256(data + i + 4).AsUInt64(), Avx.LoadDquVector256(data + i + 5).AsUInt64());
                    CarrySubtractAdd(ref twosB, ref ones, ones, Avx.LoadDquVector256(data + i + 8).AsUInt64(), Avx.LoadDquVector256(data + i + 7).AsUInt64());
                    CarrySubtractAdd(ref foursB, ref twos, twos, twosA, twosB);// Avx.LoadDquVector256(data + i + 2).AsUInt64(), Avx.LoadDquVector256(data + i + 3).AsUInt64());
                    //=========
                    CarrySubtractAdd(ref eightsA, ref fours, fours, foursA, foursB);

                    // group B
                    CarrySubtractAdd(ref twosA, ref ones, ones, Avx.LoadDquVector256(data + i + 8).AsUInt64(), Avx.LoadDquVector256(data + i + 9).AsUInt64());
                    CarrySubtractAdd(ref twosB, ref ones, ones, Avx.LoadDquVector256(data + i + 10).AsUInt64(), Avx.LoadDquVector256(data + i + 11).AsUInt64());
                    CarrySubtractAdd(ref foursA, ref twos, twos, twosA, twosB);// Avx.LoadDquVector256(data + i + 2).AsUInt64(), Avx.LoadDquVector256(data + i + 3).AsUInt64());
                    // + 
                    CarrySubtractAdd(ref twosA, ref ones, ones, Avx.LoadDquVector256(data + i + 12).AsUInt64(), Avx.LoadDquVector256(data + i + 13).AsUInt64());
                    CarrySubtractAdd(ref twosB, ref ones, ones, Avx.LoadDquVector256(data + i + 14).AsUInt64(), Avx.LoadDquVector256(data + i + 15).AsUInt64());
                    CarrySubtractAdd(ref foursB, ref twos, twos, twosA, twosB);// Avx.LoadDquVector256(data + i + 2).AsUInt64(), Avx.LoadDquVector256(data + i + 3).AsUInt64());
                    //=========
                    CarrySubtractAdd(ref eightsB, ref fours, fours, foursA, foursB);
                    // 2s -> 4s -> 8s -> sixteens

                    CarrySubtractAdd(ref sixteens, ref eights, eights, eightsA, eightsB);

                    total = Avx2.Add(total, MulaPopCount(sixteens));
                }


                total = Avx2.ShiftLeftLogical(total, 4);     // * 16
                total = Avx2.Add(total, Avx2.ShiftLeftLogical(MulaPopCount(eights), 3)); // += 8 * ...
                total = Avx2.Add(total, Avx2.ShiftLeftLogical(MulaPopCount(fours), 2)); // += 4 * ...
                total = Avx2.Add(total, Avx2.ShiftLeftLogical(MulaPopCount(twos), 1)); // += 2 * ...
                total = Avx2.Add(total, MulaPopCount(ones));

                for (; i < size; i++)
                    total = Avx2.Add(total, MulaPopCount(Avx.LoadDquVector256(data + i)));

                // They forgot the 256bit extract function :( https://github.com/dotnet/corefx/pull/23489
                //_mm256_extract_epi64 

                /*
                 return (uint64_t)(_mm256_extract_epi64(total, 0))
                   + (uint64_t)(_mm256_extract_epi64(total, 1))
                   + (uint64_t)(_mm256_extract_epi64(total, 2))
                   + (uint64_t)(_mm256_extract_epi64(total, 3));
                 */
                /*
                 return (int)(
                      Avx2.X64.Extract(total, 0)
                    + Avx2.X64.Extract(total, 1)
                    + Avx2.X64.Extract(total, 2)
                    + Avx2.X64.Extract(total, 3));
                 */

                var upper = Avx2.ExtractVector128(total, 0);
                var lower = Avx2.ExtractVector128(total, 1);

                return (int)(
                    Avx2.X64.Extract(upper, 0) +
                    Avx2.X64.Extract(upper, 1) +
                    Avx2.X64.Extract(lower, 0) +
                    Avx2.X64.Extract(lower, 1)
                );
            }

            /*
             //https://github.com/CountOnes/hamming_weight/blob/9d06b93c4cd490a286420892d5144718d2f0b3c1/src/avx_harley_seal_hamming_weight.c
  for(; i < limit; i += 16) {

    CSA(&twosA, &ones, ones, _mm256_lddqu_si256(data + i + 8), _mm256_lddqu_si256(data + i + 9));
    CSA(&twosB, &ones, ones, _mm256_lddqu_si256(data + i + 10), _mm256_lddqu_si256(data + i + 11));
    CSA(&foursA, &twos, twos, twosA, twosB);
    CSA(&twosA, &ones, ones, _mm256_lddqu_si256(data + i + 12), _mm256_lddqu_si256(data + i + 13));
    CSA(&twosB, &ones, ones, _mm256_lddqu_si256(data + i + 14), _mm256_lddqu_si256(data + i + 15));
    CSA(&foursB, &twos, twos, twosA, twosB);
    CSA(&eightsB, &fours, fours, foursA, foursB);
    CSA(&sixteens, &eights, eights, eightsA, eightsB);

    total = _mm256_add_epi64(total, popcount_pshufb(sixteens));
  }

  total = _mm256_slli_epi64(total, 4);     // * 16
  total = _mm256_add_epi64(total, _mm256_slli_epi64(popcount_pshufb(eights), 3)); // += 8 * ...
  total = _mm256_add_epi64(total, _mm256_slli_epi64(popcount_pshufb(fours),  2)); // += 4 * ...
  total = _mm256_add_epi64(total, _mm256_slli_epi64(popcount_pshufb(twos),   1)); // += 2 * ...
  total = _mm256_add_epi64(total, popcount_pshufb(ones));
  for(; i < size; i++)
    total = _mm256_add_epi64(total, popcount_pshufb(_mm256_lddqu_si256(data + i)));


  return (uint64_t)(_mm256_extract_epi64(total, 0))
       + (uint64_t)(_mm256_extract_epi64(total, 1))
       + (uint64_t)(_mm256_extract_epi64(total, 2))
       + (uint64_t)(_mm256_extract_epi64(total, 3));
}
             */

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe Vector256<ulong> MulaPopCount(Vector256<ulong> v) {
            
            var lookup = Vector256.Create((byte)0,1,1,2, 1,2,2,3, 1,2,2,3, 2,3,3,4, 0,1,1,2, 1,2,2,3, 1,2,2,3, 2,3,3,4);
            
            //var lowMask = 0x0fUL;
            //var low_mask = Avx2.BroadcastScalarToVector256(&lowMask); //0x0fUL
            var low_mask = Vector256.Create(0x0fUL); //0x0fUL -> 15,15,15,15
            var lo = Avx2.And(v, low_mask);
            var hi = Avx2.And(Avx2.ShiftRightLogical(v, 4), low_mask);
            var popcount1 = Avx2.Shuffle(lookup, lo.AsByte());
            var popcount2 = Avx2.Shuffle(lookup, hi.AsByte());
            var total = Avx2.Add(popcount1, popcount2);
            return Avx2.SumAbsoluteDifferences(total, Vector256<byte>.Zero).AsUInt64();
            /*
             __m256i count ( __m256i v) {
__m256i lookup =
_mm256_setr_epi8 (0 , 1 , 1 , 2 , 1 , 2 , 2 , 3 , 1 , 2 ,2 , 3 , 2 , 3 , 3 , 4 , 0 , 1 , 1 , 2 , 1 , 2 , 2 , 3 ,1 , 2 , 2 , 3 , 2 , 3 , 3 , 4) ;
__m256i low_mask = _mm256_set1_epi8 (0 x0f ) ;
__m256i lo = = _mm256_and_si256 (v, low_mask ) ;
__m256i hi = _mm256_and_si256 ( _mm256_srli_epi32(v, 4) , low_mask ) ;
__m256i popcnt1 = _mm256_shuffle_epi8 (lookup , lo) ;
__m256i popcnt2 = _mm256_shuffle_epi8 (lookup , hi) ;
__m256i total = _mm256_add_epi8 ( popcnt1 , popcnt2 ) ;
return _mm256_sad_epu8 (total , _mm256_setzero_si256 () ) ;
}
             */
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CarrySubtractAdd(ref Vector256<ulong> high, ref Vector256<ulong> low, Vector256<ulong> prevLow, Vector256<ulong> b, Vector256<ulong> c) {
            var u = Avx2.Xor(prevLow, b);
            high = Avx2.Or(Avx2.And(prevLow, b), Avx2.And(u, c));
            low = Avx2.Xor(u, c);
            //            CSA(__m256i* h, __m256i* l, __m256i a,
            //__m256i b, __m256i c) {
            //            __m256i u = _mm256_xor_si256 (a , b) ;
            //            *h = _mm256_or_si256(_mm256_and_si256(a, b),
            //            _mm256_and_si256(u, c));
            //            *l = _mm256_xor_si256(u, c);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ArrayCopy(ushort[] input, int iStart, ushort[] output, int oStart, int length) {
            Buffer.BlockCopy(input, iStart * sizeof(ushort), output, oStart * sizeof(ushort), length * sizeof(ushort));
        }

        public static int UnionArrays(ushort[] set1, int length1, ushort[] set2, int length2, ushort[] buffer) {
            if (0 == length2) {
                ArrayCopy(set1, 0, buffer, 0, length1);
                return length1;
            }
            if (0 == length1) {
                ArrayCopy(set2, 0, buffer, 0, length2);
                return length2;
            }

            int bufferSize = buffer.Length * sizeof(ushort);
            unsafe {
                fixed (ushort* srcPtrStart = set1) //&set1[k1]
                fixed (ushort* cmpPtrStart = set2)  //&set2[k2]
                fixed (ushort* bfrRO = buffer) {
                    ushort* set1Ptr = srcPtrStart;
                    ushort* set2Ptr = cmpPtrStart;
                    ushort* bufferPtr = bfrRO;

                    var endSet1 = set1Ptr + length1;
                    var endSet2 = set2Ptr + length2;

                    while (true) {
                        ushort v1 = *set1Ptr;
                        ushort v2 = *set2Ptr;

                        if (v1 < v2) {
                            *bufferPtr++ = v1;
                            ++set1Ptr;
                            if (set1Ptr >= endSet1) {
                                Buffer.MemoryCopy(set2Ptr, bufferPtr, bufferSize, (endSet2 - set2Ptr) * sizeof(ushort));
                                long k2x = (set2Ptr - cmpPtrStart), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length2 - k2x);
                            }
                        } else if (v1 == v2) {
                            *bufferPtr++ = v1;
                            ++set1Ptr;
                            ++set2Ptr;
                            if (set1Ptr >= endSet1) {
                                Buffer.MemoryCopy(set2Ptr, bufferPtr, bufferSize, (endSet2 - set2Ptr) * sizeof(ushort));
                                long k2x = (set2Ptr - cmpPtrStart), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length2 - k2x);
                            }
                            if (set2Ptr >= endSet2) {
                                Buffer.MemoryCopy(set1Ptr, bufferPtr, bufferSize, (endSet1 - set1Ptr) * sizeof(ushort));
                                long k1x = (set1Ptr - srcPtrStart), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length1 - k1x);
                            }
                        } else { // if (set1[k1]>set2[k2])
                            *bufferPtr++ = v2; //buffer[pos++] = s2;
                            ++set2Ptr;//++k2;
                            if (set2Ptr >= endSet2) {
                                Buffer.MemoryCopy(set1Ptr, bufferPtr, bufferSize, (endSet1 - set1Ptr) * sizeof(ushort));
                                long k1x = (set1Ptr - srcPtrStart), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length1 - k1x);
                            }
                        }
                    }
                }
            }
        }

        public static int DifferenceArrays(ushort[] set1, int length1, ushort[] set2, int length2, ushort[] buffer) {
            var pos = 0;
            int k1 = 0, k2 = 0;
            if (0 == length2) {
                ArrayCopy(set1, 0, buffer, 0, length1);
                return length1;
            }
            if (0 == length1) {
                return 0;
            }
            var s1 = set1[k1];
            var s2 = set2[k2];
            while (true) {
                if (s1 < s2) {
                    buffer[pos++] = s1;
                    ++k1;
                    if (k1 >= length1) {
                        break;
                    }
                    s1 = set1[k1];
                } else if (s1 == s2) {
                    ++k1;
                    ++k2;
                    if (k1 >= length1) {
                        break;
                    }
                    if (k2 >= length2) {
                        ArrayCopy(set1, k1, buffer, pos, length1 - k1);
                        return pos + length1 - k1;
                    }
                    s1 = set1[k1];
                    s2 = set2[k2];
                } else // if (val1>val2)
                  {
                    ++k2;
                    if (k2 >= length2) {
                        ArrayCopy(set1, k1, buffer, pos, length1 - k1);
                        return pos + length1 - k1;
                    }
                    s2 = set2[k2];
                }
            }
            return pos;
        }

        public static int IntersectArrays(ushort[] set1, int length1, ushort[] set2, int length2, ushort[] buffer) {
            if (set1.Length << 6 < set2.Length) {
                return OneSidedGallopingIntersect2By2(set1, length1, set2, length2, buffer);
            }
            if (set2.Length << 6 < set1.Length) {
                return OneSidedGallopingIntersect2By2(set2, length2, set1, length1, buffer);
            }
            return LocalIntersect2By2(set1, length1, set2, length2, buffer);
        }

        private static int LocalIntersect2By2(ushort[] set1, int length1, ushort[] set2, int length2, ushort[] buffer) {
            if ((0 == length1) || (0 == length2)) {
                return 0;
            }
            var k1 = 0;
            var k2 = 0;
            var pos = 0;
            var s1 = set1[k1];
            var s2 = set2[k2];

            while (true) {
                int v1 = s1;
                int v2 = s2;
                if (v2 < v1) {
                    do {
                        ++k2;
                        if (k2 == length2) {
                            return pos;
                        }
                        s2 = set2[k2];
                        v2 = s2;
                    } while (v2 < v1);
                }
                if (v1 < v2) {
                    do {
                        ++k1;
                        if (k1 == length1) {
                            return pos;
                        }
                        s1 = set1[k1];
                        v1 = s1;
                    } while (v1 < v2);
                } else // (set2[k2] == set1[k1])
                  {
                    buffer[pos++] = s1;
                    ++k1;
                    if (k1 == length1) {
                        break;
                    }
                    ++k2;
                    if (k2 == length2) {
                        break;
                    }
                    s1 = set1[k1];
                    s2 = set2[k2];
                }
            }
            return pos;
        }

        private static int OneSidedGallopingIntersect2By2(ushort[] smallSet, int smallLength, ushort[] largeSet, int largeLength, ushort[] buffer) {
            if (0 == smallLength) {
                return 0;
            }
            var k1 = 0;
            var k2 = 0;
            var pos = 0;
            var s1 = largeSet[k1];
            var s2 = smallSet[k2];
            while (true) {
                if (s1 < s2) {
                    k1 = AdvanceUntil(largeSet, k1, largeLength, s2);
                    if (k1 == largeLength) {
                        break;
                    }
                    s1 = largeSet[k1];
                }
                if (s2 < s1) {
                    ++k2;
                    if (k2 == smallLength) {
                        break;
                    }
                    s2 = smallSet[k2];
                } else // (set2[k2] == set1[k1])
                  {
                    buffer[pos++] = s2;
                    ++k2;
                    if (k2 == smallLength) {
                        break;
                    }
                    s2 = smallSet[k2];
                    k1 = AdvanceUntil(largeSet, k1, largeLength, s2);
                    if (k1 == largeLength) {
                        break;
                    }
                    s1 = largeSet[k1];
                }
            }
            return pos;
        }

        /// <summary>
        ///     Find the smallest integer larger than pos such that array[pos]&gt;= min.
        ///     otherwise return length
        ///     -> The first line is BinarySearch with pos + 1, the second line is the bitwise complement if the value can't be
        ///     found
        /// </summary>
        public static int AdvanceUntil(ushort[] array, int pos, int length, ushort min) {
            var start = pos + 1; // check the next one
            if ((start >= length) || (array[start] >= min)) // the simple cases
            {
                return start;
            }
            var result = Array.BinarySearch(array, start, length - start, min);
            return result < 0 ? ~result : result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort HighBits(int value) {
            return (ushort)(value >> 16);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort LowBits(int value) {
            return (ushort)(value & 0xFFFF);
        }

        public static int XorArrays(ushort[] set1, int length1, ushort[] set2, int length2, ushort[] buffer) {
            var pos = 0;
            int k1 = 0, k2 = 0;
            if (0 == length2) {
                ArrayCopy(set1, 0, buffer, 0, length1);
                return length1;
            }
            if (0 == length1) {
                ArrayCopy(set2, 0, buffer, 0, length2);
                return length2;
            }
            var s1 = set1[k1];
            var s2 = set2[k2];
            while (true) {
                if (s1 < s2) {
                    buffer[pos++] = s1;
                    ++k1;
                    if (k1 >= length1) {
                        ArrayCopy(set2, k2, buffer, pos, length2 - k2);
                        return pos + length2 - k2;
                    }
                    s1 = set1[k1];
                } else if (s1 == s2) {
                    ++k1;
                    ++k2;
                    if (k1 >= length1) {
                        ArrayCopy(set2, k2, buffer, pos, length2 - k2);
                        return pos + length2 - k2;
                    }
                    if (k2 >= length2) {
                        ArrayCopy(set1, k1, buffer, pos, length1 - k1);
                        return pos + length1 - k1;
                    }
                    s1 = set1[k1];
                    s2 = set2[k2];
                } else // if (val1>val2)
                  {
                    buffer[pos++] = s2;
                    ++k2;
                    if (k2 >= length2) {
                        ArrayCopy(set1, k1, buffer, pos, length1 - k1);
                        return pos + length1 - k1;
                    }
                    s2 = set2[k2];
                }
            }
        }
    }
}