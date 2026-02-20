using System;
using System.Runtime.CompilerServices;
using Vortice.Direct3D12;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

internal static class Dx12UploadHelper
{
    internal static unsafe void UploadBuffer<T>(ID3D12Device device, ID3D12GraphicsCommandList commandList, ReadOnlySpan<T> data, ID3D12Resource destination, out ID3D12Resource upload)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(commandList);
        ArgumentNullException.ThrowIfNull(destination);

        ulong sizeInBytes = (ulong)(Unsafe.SizeOf<T>() * data.Length);

        upload = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(sizeInBytes),
            ResourceStates.GenericRead);

        void* mapped;
        upload.Map(0, null, &mapped);
        data.CopyTo(new Span<T>(mapped, data.Length));
        upload.Unmap(0);

        commandList.CopyBufferRegion(destination, 0, upload, 0, sizeInBytes);
    }
}
