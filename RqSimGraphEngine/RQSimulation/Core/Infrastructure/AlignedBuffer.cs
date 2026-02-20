using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RQSimulation
{
    // Unsafe aligned buffer wrapper for 64-byte alignment. Dispose to free.
    internal unsafe sealed class AlignedBuffer<T> : IDisposable where T : unmanaged
    {
        public T* Ptr { get; private set; }
        public nuint Length { get; }
        public nuint ByteLength => Length * (nuint)sizeof(T);
        public bool IsAllocated => Ptr != null;

        public AlignedBuffer(nuint length, nuint alignment = 64)
        {
            if (length == 0) throw new ArgumentOutOfRangeException(nameof(length));
            Length = length;
            // Allocate with alignment
            void* mem = NativeMemory.AlignedAlloc(ByteLength, alignment);
            if (mem == null) throw new OutOfMemoryException("AlignedAlloc failed");
            Ptr = (T*)mem;
            // Zero-init
            NativeMemory.Clear(mem, ByteLength);
        }

        public Span<T> AsSpan()
        {
            if (Ptr == null) return Span<T>.Empty;
            return new Span<T>(Ptr, (int)Length);
        }

        public void Dispose()
        {
            if (Ptr != null)
            {
                NativeMemory.AlignedFree(Ptr);
                Ptr = null;
            }
            GC.SuppressFinalize(this);
        }

        ~AlignedBuffer()
        {
            if (Ptr != null)
                NativeMemory.AlignedFree(Ptr);
        }
    }
}
