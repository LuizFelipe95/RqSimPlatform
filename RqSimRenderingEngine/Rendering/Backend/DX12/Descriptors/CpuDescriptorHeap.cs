using System;
using Vortice.Direct3D12;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Descriptors;

internal sealed class CpuDescriptorHeap : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly DescriptorHeapType _type;
    private readonly ID3D12DescriptorHeap _heap;
    private readonly uint _descriptorSize;
    private readonly uint _capacity;
    private uint _next;

    public CpuDescriptorHeap(ID3D12Device device, DescriptorHeapType type, uint capacity)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (capacity == 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _device = device;
        _type = type;
        _capacity = capacity;
        _heap = _device.CreateDescriptorHeap(new DescriptorHeapDescription(type, (uint)capacity, DescriptorHeapFlags.None));
        _descriptorSize = _device.GetDescriptorHandleIncrementSize(type);
    }

    public ID3D12DescriptorHeap Heap => _heap;

    public CpuDescriptorHandle Allocate()
    {
        if (_next >= _capacity)
            throw new InvalidOperationException($"Descriptor heap overflow ({_type}), capacity={_capacity}.");

        CpuDescriptorHandle handle = _heap.GetCPUDescriptorHandleForHeapStart();
        handle.Ptr = (nuint)(handle.Ptr + _next * _descriptorSize);
        _next++;
        return handle;
    }

    public CpuDescriptorHandle GetHandle(uint index)
    {
        if (index >= _capacity)
            throw new ArgumentOutOfRangeException(nameof(index));

        CpuDescriptorHandle handle = _heap.GetCPUDescriptorHandleForHeapStart();
        handle.Ptr = (nuint)(handle.Ptr + index * _descriptorSize);
        return handle;
    }

    public void Reset()
    {
        _next = 0;
    }

    public void Dispose()
    {
        _heap.Dispose();
    }
}
