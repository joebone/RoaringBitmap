using System;
using System.Runtime.CompilerServices;
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
            var result = 0;
            for (var i = 0; i < xArray.Length; i++) {
                result += (int)Popcnt.X64.PopCount(xArray[i]);
            }
            return result;
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

            //var pos = 0;
            //int k1 = 0, k2 = 0;
            int bufferSize = buffer.Length * sizeof(ushort);
            unsafe {
                fixed (ushort* srcPtrRO = set1) //&set1[k1]
                fixed (ushort* cmpPtrRO = set2)  //&set2[k2]
                fixed (ushort* bfrRO = buffer) {
                    ushort* srcPtr = srcPtrRO;
                    ushort* cmpPtr = cmpPtrRO;
                    ushort* bufferPtr = bfrRO;

                    var endSrc = srcPtr + length1;
                    var endCmp = cmpPtr + length2;

                    while (true) {
                        ushort v1 = *srcPtr;
                        ushort v2 = *cmpPtr;

                        if (v1 < v2) {
                            *bufferPtr++ = v1;
                            ++srcPtr;
                            if (srcPtr >= endSrc) {
                                Buffer.MemoryCopy(cmpPtr, bufferPtr, bufferSize, (endCmp - cmpPtr) * sizeof(ushort));
                                long k2x = (cmpPtr - cmpPtrRO), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length2 - k2x);
                            }
                        } else if (v1 == v2) {
                            *bufferPtr++ = v1; //buffer[pos++] = s1;
                            ++srcPtr; //++k1;
                            ++cmpPtr; //++k2;
                            if (srcPtr >= endSrc) {
                                Buffer.MemoryCopy(cmpPtr, bufferPtr, bufferSize, (endCmp - cmpPtr) * sizeof(ushort));
                                long k2x = (cmpPtr - cmpPtrRO), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length2 - k2x);
                            }
                            if (cmpPtr >= endCmp) {
                                Buffer.MemoryCopy(srcPtr, bufferPtr, bufferSize, (endSrc - srcPtr) * sizeof(ushort));
                                long k1x = (srcPtr - srcPtrRO), posx = (bufferPtr - bfrRO);
                                return (int)(posx + length1 - k1x);
                            }
                        } else { // if (set1[k1]>set2[k2])
                            *bufferPtr++ = v2; //buffer[pos++] = s2;
                            ++cmpPtr;//++k2;
                            if (cmpPtr >= endCmp) {
                                Buffer.MemoryCopy(srcPtr, bufferPtr, bufferSize, (endSrc - srcPtr) * sizeof(ushort));
                                long k1x = (srcPtr - srcPtrRO), posx = (bufferPtr - bfrRO);
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