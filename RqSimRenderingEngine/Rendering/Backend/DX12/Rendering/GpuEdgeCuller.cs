using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// GPU-driven edge culling system using compute shaders.
/// Implements frustum culling and subpixel culling to reduce overdraw.
/// </summary>
internal sealed class GpuEdgeCuller : IDisposable
{
    // Compute pipeline objects
    private ID3D12RootSignature? _cullingRootSignature;
    private ID3D12PipelineState? _cullingPso;
    private ID3D12PipelineState? _resetArgsPso;

    // Descriptor heaps for compute UAVs
    private ID3D12DescriptorHeap? _uavHeap;
    private int _uavDescriptorSize;

    // Buffers
    private ID3D12Resource? _inputEdgesBuffer;        // All edges (SRV)
    private ID3D12Resource? _visibleEdgesBuffer;      // Culled edges (UAV with counter)
    private ID3D12Resource? _indirectArgsBuffer;      // Draw indirect args (UAV)
    private ID3D12Resource? _cullingConstantsBuffer;  // Camera/culling params (CBV)
    private IntPtr _constantsPtr;

    // Command signature for ExecuteIndirect
    private ID3D12CommandSignature? _drawCommandSignature;

    // Capacity tracking
    private int _maxInputEdges;
    private int _maxOutputEdges;

    private ID3D12Device? _device;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Whether GPU culling is available and initialized.
    /// </summary>
    public bool IsAvailable => _initialized && !_disposed;

    /// <summary>
    /// Initialize the GPU culling system.
    /// </summary>
    public void Initialize(ID3D12Device device)
    {
        ArgumentNullException.ThrowIfNull(device);

        _device = device;

        CreateRootSignature(device);
        CreateComputePipelines(device);
        CreateDescriptorHeap(device);
        CreateCommandSignature(device);
        CreateConstantBuffer(device);

        _initialized = true;
        System.Diagnostics.Debug.WriteLine("[GpuCuller] Initialized successfully");
    }

    /// <summary>
    /// Ensure buffers have capacity for the given edge count.
    /// </summary>
    public void EnsureCapacity(ID3D12Device device, int maxEdges)
    {
        if (maxEdges <= _maxInputEdges)
            return;

        // Round up to power of 2 for efficiency
        int newCapacity = Math.Max(1024, (int)BitOperations.RoundUpToPowerOf2((uint)maxEdges));
        
        ReleaseBuffers();
        CreateEdgeBuffers(device, newCapacity);
        
        _maxInputEdges = newCapacity;
        _maxOutputEdges = newCapacity; // Worst case: all edges visible
        
        System.Diagnostics.Debug.WriteLine($"[GpuCuller] Resized buffers to {newCapacity} edges");
    }

    /// <summary>
    /// Upload input edges to GPU buffer.
    /// </summary>
    public unsafe void UploadEdges(ReadOnlySpan<Dx12LineVertex> vertices)
    {
        if (_inputEdgesBuffer is null || vertices.Length == 0)
            return;

        void* mapped;
        _inputEdgesBuffer.Map(0, null, &mapped);
        vertices.CopyTo(new Span<Dx12LineVertex>(mapped, vertices.Length));
        _inputEdgesBuffer.Unmap(0);
    }

    /// <summary>
    /// Update culling constants (camera, frustum, thresholds).
    /// </summary>
    public void UpdateCullingConstants(in CullingConstants constants)
    {
        if (_constantsPtr == IntPtr.Zero)
            return;

        Marshal.StructureToPtr(constants, _constantsPtr, fDeleteOld: false);
    }

    /// <summary>
    /// Execute GPU culling and return the visible edges buffer for rendering.
    /// </summary>
    /// <param name="commandList">Command list to record culling commands.</param>
    /// <param name="edgeCount">Total number of edges (vertex count / 2).</param>
    /// <returns>True if culling was executed; false if not available.</returns>
    public bool ExecuteCulling(ID3D12GraphicsCommandList commandList, int edgeCount)
    {
        if (!_initialized || _cullingPso is null || _resetArgsPso is null || 
            _cullingRootSignature is null || _uavHeap is null ||
            _visibleEdgesBuffer is null || _indirectArgsBuffer is null ||
            _inputEdgesBuffer is null || _cullingConstantsBuffer is null)
            return false;

        if (edgeCount <= 0)
            return false;

        // Set compute root signature and descriptor heap
        commandList.SetComputeRootSignature(_cullingRootSignature);
        commandList.SetDescriptorHeaps(_uavHeap);

        // Step 1: Reset indirect args
        commandList.SetPipelineState(_resetArgsPso);
        commandList.SetComputeRootUnorderedAccessView(3, _indirectArgsBuffer.GPUVirtualAddress);
        commandList.Dispatch(1, 1, 1);

        // UAV barrier to ensure reset completes before culling
        commandList.ResourceBarrierUnorderedAccessView(_indirectArgsBuffer);
        commandList.ResourceBarrierUnorderedAccessView(_visibleEdgesBuffer);

        // Step 2: Execute culling shader
        commandList.SetPipelineState(_cullingPso);
        commandList.SetComputeRootConstantBufferView(0, _cullingConstantsBuffer.GPUVirtualAddress);
        commandList.SetComputeRootShaderResourceView(1, _inputEdgesBuffer.GPUVirtualAddress);
        
        // UAV descriptor table for visible edges (with counter)
        var uavTableStart = _uavHeap.GetGPUDescriptorHandleForHeapStart();
        commandList.SetComputeRootDescriptorTable(2, uavTableStart);
        commandList.SetComputeRootUnorderedAccessView(3, _indirectArgsBuffer.GPUVirtualAddress);

        // Dispatch: 64 threads per group
        uint dispatchCount = (uint)((edgeCount + 63) / 64);
        commandList.Dispatch(dispatchCount, 1, 1);

        // UAV barrier to ensure culling completes before drawing
        commandList.ResourceBarrierUnorderedAccessView(_visibleEdgesBuffer);
        commandList.ResourceBarrierUnorderedAccessView(_indirectArgsBuffer);

        return true;
    }

    /// <summary>
    /// Get the visible edges buffer for rendering via ExecuteIndirect.
    /// </summary>
    public ID3D12Resource? GetVisibleEdgesBuffer() => _visibleEdgesBuffer;

    /// <summary>
    /// Get the indirect arguments buffer for ExecuteIndirect.
    /// </summary>
    public ID3D12Resource? GetIndirectArgsBuffer() => _indirectArgsBuffer;

    /// <summary>
    /// Get the command signature for ExecuteIndirect draw calls.
    /// </summary>
    public ID3D12CommandSignature? GetDrawCommandSignature() => _drawCommandSignature;

    /// <summary>
    /// Get vertex buffer view for the visible edges buffer.
    /// </summary>
    public VertexBufferView GetVisibleEdgesVBView()
    {
        if (_visibleEdgesBuffer is null)
            return default;

        return new VertexBufferView
        {
            BufferLocation = _visibleEdgesBuffer.GPUVirtualAddress,
            SizeInBytes = (uint)(_maxOutputEdges * 2 * Unsafe.SizeOf<Dx12LineVertex>()),
            StrideInBytes = (uint)Unsafe.SizeOf<Dx12LineVertex>()
        };
    }

    private void CreateRootSignature(ID3D12Device device)
    {
        // Compile a minimal shader just to extract root signature
        Compiler.Compile(Dx12CullingShaders.EdgeCullingCs, "main", null, "cs_5_0", out var csBlob, out var errors);
        
        string? errorMsg = null;
        if (errors is not null && errors.BufferSize > 0)
        {
            errorMsg = Marshal.PtrToStringAnsi(errors.BufferPointer);
            System.Diagnostics.Debug.WriteLine($"[GpuCuller] Shader compilation output: {errorMsg}");
        }

        // Check if compilation failed
        if (csBlob is null || csBlob.BufferSize == 0)
        {
            errors?.Dispose();
            throw new InvalidOperationException($"GPU edge culling shader compilation failed: {errorMsg ?? "Unknown error"}. GPU culling will be disabled.");
        }

        try
        {
            var rootSigBlob = Compiler.GetBlobPart(csBlob.BufferPointer, csBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
            _cullingRootSignature = device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);
            rootSigBlob.Dispose();
        }
        finally
        {
            csBlob.Dispose();
            errors?.Dispose();
        }
    }

    private void CreateComputePipelines(ID3D12Device device)
    {
        // Culling compute shader
        Compiler.Compile(Dx12CullingShaders.EdgeCullingCs, "main", null, "cs_5_0", out var cullingBlob, out _);
        
        var cullingDesc = new ComputePipelineStateDescription
        {
            RootSignature = _cullingRootSignature,
            ComputeShader = cullingBlob.AsMemory()
        };
        _cullingPso = device.CreateComputePipelineState(cullingDesc);
        cullingBlob.Dispose();

        // Reset args compute shader
        Compiler.Compile(Dx12CullingShaders.ResetIndirectArgsCs, "main", null, "cs_5_0", out var resetBlob, out _);
        
        var resetDesc = new ComputePipelineStateDescription
        {
            RootSignature = _cullingRootSignature,
            ComputeShader = resetBlob.AsMemory()
        };
        _resetArgsPso = device.CreateComputePipelineState(resetDesc);
        resetBlob.Dispose();
    }

    private void CreateDescriptorHeap(ID3D12Device device)
    {
        // UAV heap for visible edges buffer (shader visible)
        _uavHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 
            2, // visible edges UAV + indirect args UAV
            DescriptorHeapFlags.ShaderVisible));

        _uavDescriptorSize = (int)device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    private void CreateCommandSignature(ID3D12Device device)
    {
        // Command signature for Draw (non-indexed)
        var argDesc = new IndirectArgumentDescription
        {
            Type = IndirectArgumentType.Draw
        };

        _drawCommandSignature = device.CreateCommandSignature<ID3D12CommandSignature>(
            new CommandSignatureDescription(
                Unsafe.SizeOf<DrawArguments>(),
                [argDesc]),
            null); // No root signature needed for simple Draw
    }

    private unsafe void CreateConstantBuffer(ID3D12Device device)
    {
        // Align to 256 bytes for constant buffer
        ulong size = (ulong)((Unsafe.SizeOf<CullingConstants>() + 255) & ~255);
        
        _cullingConstantsBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(size),
            ResourceStates.GenericRead);

        void* mapped;
        _cullingConstantsBuffer.Map(0, null, &mapped);
        _constantsPtr = (IntPtr)mapped;
    }

    private void CreateEdgeBuffers(ID3D12Device device, int maxEdges)
    {
        int vertexCount = maxEdges * 2;
        ulong vertexBufferSize = (ulong)(vertexCount * Unsafe.SizeOf<Dx12LineVertex>());

        // Input edges buffer (upload heap for CPU writes, SRV)
        _inputEdgesBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(vertexBufferSize),
            ResourceStates.GenericRead);

        // Visible edges buffer (default heap, UAV with counter)
        _visibleEdgesBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(vertexBufferSize, ResourceFlags.AllowUnorderedAccess),
            ResourceStates.UnorderedAccess);

        // Indirect args buffer (4 uints for Draw command)
        _indirectArgsBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(16, ResourceFlags.AllowUnorderedAccess), // 4 * sizeof(uint)
            ResourceStates.UnorderedAccess);

        // Create UAV descriptors
        if (_uavHeap is not null)
        {
            var uavHandle = _uavHeap.GetCPUDescriptorHandleForHeapStart();

            // Visible edges UAV
            var visibleEdgesUav = new UnorderedAccessViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new BufferUnorderedAccessView
                {
                    FirstElement = 0,
                    NumElements = (uint)vertexCount,
                    StructureByteStride = (uint)Unsafe.SizeOf<Dx12LineVertex>(),
                    CounterOffsetInBytes = 0,
                    Flags = BufferUnorderedAccessViewFlags.None
                }
            };
            device.CreateUnorderedAccessView(_visibleEdgesBuffer, null, visibleEdgesUav, uavHandle);

            // Indirect args UAV
            uavHandle += _uavDescriptorSize;
            var indirectArgsUav = new UnorderedAccessViewDescription
            {
                Format = Format.R32_UInt,
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new BufferUnorderedAccessView
                {
                    FirstElement = 0,
                    NumElements = 4,
                    Flags = BufferUnorderedAccessViewFlags.None
                }
            };
            device.CreateUnorderedAccessView(_indirectArgsBuffer, null, indirectArgsUav, uavHandle);
        }
    }

    private void ReleaseBuffers()
    {
        _inputEdgesBuffer?.Dispose();
        _inputEdgesBuffer = null;

        _visibleEdgesBuffer?.Dispose();
        _visibleEdgesBuffer = null;

        _indirectArgsBuffer?.Dispose();
        _indirectArgsBuffer = null;

        _maxInputEdges = 0;
        _maxOutputEdges = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _initialized = false;

        if (_constantsPtr != IntPtr.Zero)
        {
            _cullingConstantsBuffer?.Unmap(0);
            _constantsPtr = IntPtr.Zero;
        }

        ReleaseBuffers();

        _cullingConstantsBuffer?.Dispose();
        _drawCommandSignature?.Dispose();
        _uavHeap?.Dispose();
        _resetArgsPso?.Dispose();
        _cullingPso?.Dispose();
        _cullingRootSignature?.Dispose();

        _device = null;

        System.Diagnostics.Debug.WriteLine("[GpuCuller] Disposed");
    }
}

/// <summary>
/// Constants passed to culling compute shader.
/// Must match HLSL cbuffer layout exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CullingConstants
{
    public Matrix4x4 ViewProj;      // 64 bytes
    public Vector3 CameraPosition;  // 12 bytes
    public float MinProjectedSize;  // 4 bytes
    public uint TotalEdgeCount;     // 4 bytes
    public Vector3 _pad;            // 12 bytes padding to align to 16 bytes

    /// <summary>
    /// Create culling constants from view-projection matrix.
    /// </summary>
    public static CullingConstants Create(
        Matrix4x4 view, 
        Matrix4x4 projection, 
        Vector3 cameraPosition,
        float minProjectedSize = 0.002f)
    {
        var viewProj = view * projection;
        return new CullingConstants
        {
            ViewProj = viewProj,
            CameraPosition = cameraPosition,
            MinProjectedSize = minProjectedSize,
            TotalEdgeCount = 0, // Set by caller
            _pad = Vector3.Zero
        };
    }
}

/// <summary>
/// Draw arguments structure for ExecuteIndirect.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrawArguments
{
    public uint VertexCountPerInstance;
    public uint InstanceCount;
    public uint StartVertexLocation;
    public uint StartInstanceLocation;
}
