using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RoaringBitmap.Simd {

    /// <summary>
    /// A MemoryManager over a raw pointer
    /// </summary>
    /// <remarks>The pointer is assumed to be fully unmanaged, or externally pinned - no attempt will be made to pin this data</remarks>
    public sealed unsafe class UnmanagedMemoryManager<T> : MemoryManager<T>
        where T : unmanaged {
        private readonly T* _pointer;
        private readonly int _length;

        //https://github.com/mgravell/Pipelines.Sockets.Unofficial/blob/master/src/Pipelines.Sockets.Unofficial/UnsafeMemory.cs
        //https://stackoverflow.com/questions/52190423/c-sharp-access-unmanaged-array-using-memoryt-or-arraysegmentt


        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        /// <remarks>It is assumed that the span provided is already unmanaged or externally pinned</remarks>
        public UnmanagedMemoryManager(Span<T> span) {
            fixed (T* ptr = &MemoryMarshal.GetReference(span)) {
                _pointer = ptr;
                _length = span.Length;
            }
        }

        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        [CLSCompliant(false)]
        public UnmanagedMemoryManager(T* pointer, int length) {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

            _pointer = pointer;
            _length = length;
        }

        /// <summary>
        /// Create a new UnmanagedMemoryManager instance at the given pointer and size
        /// </summary>
        public UnmanagedMemoryManager(IntPtr pointer, int length) : this((T*)pointer.ToPointer(), length) { }

        /// <summary>
        /// Obtains a span that represents the region
        /// </summary>
        public override Span<T> GetSpan() => new Span<T>(_pointer, _length);

        /// <summary>
        /// Provides access to a pointer that represents the data (note: no actual pin occurs)
        /// </summary>
        public override MemoryHandle Pin(int elementIndex = 0) {
            if (elementIndex < 0 || elementIndex >= _length)
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            return new MemoryHandle(_pointer + elementIndex);
        }
        /// <summary>
        /// Has no effect
        /// </summary>
        public override void Unpin() { }

        /// <summary>
        /// Releases all resources associated with this object
        /// </summary>
        protected override void Dispose(bool disposing) { }
    }

    /// <summary>
    /// Aims to manually manage the memory address of arrays given by the pool
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AlignedInt64ArrayPool : IDisposable { //where T : struct 
        private readonly int constantArraySize;
        private readonly int Size;
        private bool disposedValue;

        Stack<int> freePositions;
        Dictionary<Memory<ulong>, int> rented;
        IntPtr memoryBlock;
        UnmanagedMemoryManager<ulong> memMgr;
        const int alignTo = 32;

        // the .net pool is fancy
        // https://github.com/dotnet/corert/blob/000584cb4fd45d845990a1f1cd3891d04066c1a7/src/System.Private.CoreLib/shared/System/Buffers/TlsOverPerCoreLockedStacksArrayPool.cs

        public AlignedInt64ArrayPool() {
            freePositions = new Stack<int>();
            rented = new Dictionary<Memory<ulong>, int>();
        }

        [Obsolete("Just the usage of this method implies doing VERY BAD THINGS."), EditorBrowsable(EditorBrowsableState.Never)]
        public void RepossessAll() {
            lock (freePositions) {
                rented.Clear();
                for (int i = 0; i < Size; i++) {
                    freePositions.Push(i);
                }
            }
        }

        public int InUse => Size - freePositions.Count;

        public AlignedInt64ArrayPool(int ConstantArraySize, int Size) : this() { // , int AlignTo = 32
            constantArraySize = ConstantArraySize;
            this.Size = Size;

            RequestMemory();
        }

        public Memory<ulong> Rent() {
            if (freePositions.Count == 0) {
                throw new InvalidOperationException("No Free blocks");
            }

            lock (freePositions) {
                var pos = freePositions.Pop();
                var slice = memMgr.Memory.Slice(pos, constantArraySize);

                rented[slice] = pos;
                return slice;
            }
        }

        public void Return(Memory<ulong> rentedMemory) {

            if (rented.TryGetValue(rentedMemory, out int position)) {
                lock (freePositions) {
                    freePositions.Push(position);
                    rented.Remove(rentedMemory);
                }
            } else {
                throw new InvalidOperationException("Returned Memory<T> does not come from this block");
            }
        }


        private unsafe void RequestMemory() {
            var blockSize = (constantArraySize * Size * sizeof(ulong)) + alignTo;
            memoryBlock = Marshal.AllocCoTaskMem(blockSize);
            //var prev = alignTo - 1;
            var ptr = (ulong*)memoryBlock.ToPointer();
            var aligned = (ulong*)(((ulong)ptr + 31UL) & ~31UL);
            int size = (int)(blockSize - (aligned - ptr));

            memMgr = new UnmanagedMemoryManager<ulong>(aligned, size);
            for (int i = 0; i < Size; i++) {
                freePositions.Push(i);
            }
            //memoryBlocks.Add((new UnmanagedMemoryManager<ulong>(ptr, size), memBlockPtr));
        }

        protected virtual void Dispose(bool disposing) {
            if (!disposedValue) {
                if (disposing) {
                    // TODO: dispose managed state (managed objects)
                }


                //foreach (var ptr in memoryBlocks) {
                //    Marshal.FreeCoTaskMem(ptr);
                //}
                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                Marshal.FreeCoTaskMem(memoryBlock);

                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~AlignedArrayPool()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose() {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
