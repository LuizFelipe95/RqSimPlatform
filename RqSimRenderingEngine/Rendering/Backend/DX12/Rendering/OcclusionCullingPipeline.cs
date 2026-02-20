using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.D3DCompiler;
using Vortice.DXGI;

namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Occlusion culling constants for compute shader.
/// Must match HLSL OcclusionCB cbuffer layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct OcclusionCullingConstants
{
    public Matrix4x4 ViewProj;      // 64 bytes
    public Vector2 ScreenSize;      // 8 bytes
    public float DepthBias;         // 4 bytes
    public float MinProjectedSize;  // 4 bytes
    public uint TotalEdgeCount;     // 4 bytes
    public Vector3 _pad;            // 12 bytes padding

    /// <summary>
    /// Create occlusion culling constants.
    /// </summary>
    public static OcclusionCullingConstants Create(
        Matrix4x4 view,
        Matrix4x4 projection,
        Vector2 screenSize,
        float depthBias = 0.001f,
        float minProjectedSize = 0.002f)
    {
        return new OcclusionCullingConstants
        {
            ViewProj = view * projection,
            ScreenSize = screenSize,
            DepthBias = depthBias,
            MinProjectedSize = minProjectedSize,
            TotalEdgeCount = 0,
            _pad = Vector3.Zero
        };
    }
}

/// <summary>
/// GPU-driven occlusion culling pipeline for edges.
/// Uses Depth Pre-Pass to cull edges hidden behind spheres.
/// </summary>
internal sealed class OcclusionCullingPipeline : IDisposable
{
    // Compute pipeline objects
    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _occlusionCullPso;
    private ID3D12PipelineState? _resetArgsPso;

    // Descriptor heaps
    private ID3D12DescriptorHeap? _srvUavHeap;
    private int _descriptorSize;

    // Buffers
    private ID3D12Resource? _inputEdgesBuffer;      // All packed edges (SRV)
    private ID3D12Resource? _inputNodesBuffer;      // All packed nodes (SRV)
    private ID3D12Resource? _visibleEdgesBuffer;    // Culled edges (UAV)
    private ID3D12Resource? _indirectArgsBuffer;    // Draw indirect args
    private ID3D12Resource? _constantsBuffer;       // Culling constants (CBV)
    private IntPtr _constantsPtr;

    // Command signature for ExecuteIndirect (instanced draw)
    private ID3D12CommandSignature? _drawInstancedSignature;

    // Capacity tracking
    private int _maxEdges;
    private int _maxNodes;

    private ID3D12Device? _device;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Whether occlusion culling is available.
    /// </summary>
    public bool IsAvailable => _initialized && !_disposed;

    /// <summary>
    /// Initialize the occlusion culling pipeline.
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
        System.Diagnostics.Debug.WriteLine("[OcclusionCulling] Initialized successfully");
    }

    /// <summary>
    /// Ensure buffers have capacity for the given counts.
    /// </summary>
    public void EnsureCapacity(ID3D12Device device, int maxEdges, int maxNodes)
    {
        bool needsResize = maxEdges > _maxEdges || maxNodes > _maxNodes;
        if (!needsResize)
            return;

        int newEdgeCapacity = Math.Max(1024, (int)BitOperations.RoundUpToPowerOf2((uint)maxEdges));
        int newNodeCapacity = Math.Max(1024, (int)BitOperations.RoundUpToPowerOf2((uint)maxNodes));

        ReleaseDataBuffers();
        CreateDataBuffers(device, newEdgeCapacity, newNodeCapacity);

        _maxEdges = newEdgeCapacity;
        _maxNodes = newNodeCapacity;

        System.Diagnostics.Debug.WriteLine($"[OcclusionCulling] Resized buffers: {newEdgeCapacity} edges, {newNodeCapacity} nodes");
    }

    /// <summary>
    /// Upload packed edge data to GPU.
    /// </summary>
    public unsafe void UploadEdges(ReadOnlySpan<Dx12PackedEdgeData> edges)
    {
        if (_inputEdgesBuffer is null || edges.Length == 0)
            return;

        void* mapped;
        _inputEdgesBuffer.Map(0, null, &mapped);
        edges.CopyTo(new Span<Dx12PackedEdgeData>(mapped, edges.Length));
        _inputEdgesBuffer.Unmap(0);
    }

    /// <summary>
    /// Upload packed node data to GPU.
    /// </summary>
    public unsafe void UploadNodes(ReadOnlySpan<Dx12PackedNodeData> nodes)
    {
        if (_inputNodesBuffer is null || nodes.Length == 0)
            return;

        void* mapped;
        _inputNodesBuffer.Map(0, null, &mapped);
        nodes.CopyTo(new Span<Dx12PackedNodeData>(mapped, nodes.Length));
        _inputNodesBuffer.Unmap(0);
    }

    /// <summary>
    /// Update occlusion culling constants.
    /// </summary>
    public void UpdateConstants(in OcclusionCullingConstants constants)
    {
        if (_constantsPtr == IntPtr.Zero)
            return;

        Marshal.StructureToPtr(constants, _constantsPtr, fDeleteOld: false);
    }

    /// <summary>
    /// Register depth buffer SRV in descriptor heap.
    /// Call this after depth pre-pass before culling.
    /// </summary>
    public void SetDepthBufferSRV(ID3D12Device device, ID3D12Resource depthBuffer, Format depthFormat)
    {
        if (_srvUavHeap is null)
            return;

        // Depth buffer SRV is at slot 2 (t2)
        var srvHandle = _srvUavHeap.GetCPUDescriptorHandleForHeapStart();
        srvHandle += _descriptorSize * 2; // Skip edges and nodes SRVs

        // Convert depth format to SRV-compatible format
        Format srvFormat = depthFormat switch
        {
            Format.D32_Float => Format.R32_Float,
            Format.D24_UNorm_S8_UInt => Format.R24_UNorm_X8_Typeless,
            Format.D16_UNorm => Format.R16_UNorm,
            _ => Format.R32_Float
        };

        var srvDesc = new ShaderResourceViewDescription
        {
            Format = srvFormat,
            ViewDimension = ShaderResourceViewDimension.Texture2D,
            Shader4ComponentMapping = ShaderComponentMapping.Default,
            Texture2D = new Texture2DShaderResourceView
            {
                MipLevels = 1,
                MostDetailedMip = 0
            }
        };

        device.CreateShaderResourceView(depthBuffer, srvDesc, srvHandle);
    }

    /// <summary>
    /// Execute occlusion culling.
    /// </summary>
    /// <param name="commandList">Command list to record commands.</param>
    /// <param name="edgeCount">Total number of edges.</param>
    /// <returns>True if culling was executed.</returns>
    public bool ExecuteCulling(ID3D12GraphicsCommandList commandList, int edgeCount)
    {
        if (!_initialized || _occlusionCullPso is null || _resetArgsPso is null ||
            _rootSignature is null || _srvUavHeap is null ||
            _visibleEdgesBuffer is null || _indirectArgsBuffer is null ||
            _inputEdgesBuffer is null || _inputNodesBuffer is null ||
            _constantsBuffer is null)
            return false;

        if (edgeCount <= 0)
            return false;

        // Set compute pipeline
        commandList.SetComputeRootSignature(_rootSignature);
        commandList.SetDescriptorHeaps(_srvUavHeap);

        // Step 1: Reset indirect args
        commandList.SetPipelineState(_resetArgsPso);
        commandList.SetComputeRootUnorderedAccessView(6, _indirectArgsBuffer.GPUVirtualAddress);
        commandList.Dispatch(1, 1, 1);

        // UAV barriers
        commandList.ResourceBarrierUnorderedAccessView(_indirectArgsBuffer);
        commandList.ResourceBarrierUnorderedAccessView(_visibleEdgesBuffer);

        // Step 2: Execute occlusion culling
        commandList.SetPipelineState(_occlusionCullPso);

        // Bind resources
        commandList.SetComputeRootConstantBufferView(0, _constantsBuffer.GPUVirtualAddress);
        commandList.SetComputeRootShaderResourceView(1, _inputEdgesBuffer.GPUVirtualAddress);
        commandList.SetComputeRootShaderResourceView(2, _inputNodesBuffer.GPUVirtualAddress);

        // Depth buffer SRV via descriptor table
        var srvTableStart = _srvUavHeap.GetGPUDescriptorHandleForHeapStart();
        srvTableStart += _descriptorSize * 2; // Depth buffer is at slot 2
        commandList.SetComputeRootDescriptorTable(3, srvTableStart);

        // Output UAVs
        commandList.SetComputeRootUnorderedAccessView(5, _visibleEdgesBuffer.GPUVirtualAddress);
        commandList.SetComputeRootUnorderedAccessView(6, _indirectArgsBuffer.GPUVirtualAddress);

        // Dispatch: 256 threads per group
        uint dispatchCount = (uint)((edgeCount + 255) / 256);
        commandList.Dispatch(dispatchCount, 1, 1);

        // Final UAV barriers
        commandList.ResourceBarrierUnorderedAccessView(_visibleEdgesBuffer);
        commandList.ResourceBarrierUnorderedAccessView(_indirectArgsBuffer);

        return true;
    }

    /// <summary>
    /// Get visible edges buffer for rendering.
    /// </summary>
    public ID3D12Resource? GetVisibleEdgesBuffer() => _visibleEdgesBuffer;

    /// <summary>
    /// Get indirect args buffer for ExecuteIndirect.
    /// </summary>
    public ID3D12Resource? GetIndirectArgsBuffer() => _indirectArgsBuffer;

    /// <summary>
    /// Get command signature for DrawInstanced.
    /// </summary>
    public ID3D12CommandSignature? GetDrawInstancedSignature() => _drawInstancedSignature;

    private void CreateRootSignature(ID3D12Device device)
    {
        Compiler.Compile(Dx12CullingShaders.EdgeOcclusionCullCs, "main", null, "cs_5_0", out var csBlob, out var errors);

        string? errorMsg = null;
        if (errors is not null && errors.BufferSize > 0)
        {
            errorMsg = Marshal.PtrToStringAnsi(errors.BufferPointer);
            System.Diagnostics.Debug.WriteLine($"[OcclusionCulling] Shader compilation output: {errorMsg}");
        }

        // Check if compilation failed
        if (csBlob is null || csBlob.BufferSize == 0)
        {
            errors?.Dispose();
            System.Diagnostics.Debug.WriteLine("**csBlob** было null.");
            throw new InvalidOperationException($"Occlusion culling shader compilation failed: {errorMsg ?? "Unknown error"}. Occlusion culling will be disabled.");
        }

        try
        {
            var rootSigBlob = Compiler.GetBlobPart(csBlob.BufferPointer, csBlob.BufferSize, ShaderBytecodePart.RootSignature, 0);
            _rootSignature = device.CreateRootSignature(0, rootSigBlob.BufferPointer, rootSigBlob.BufferSize);
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
        // Occlusion culling shader
        Compiler.Compile(Dx12CullingShaders.EdgeOcclusionCullCs, "main", null, "cs_5_0", out var cullBlob, out _);

        var cullDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = cullBlob.AsMemory()
        };
        _occlusionCullPso = device.CreateComputePipelineState(cullDesc);
        cullBlob.Dispose();

        // Reset args shader
        Compiler.Compile(Dx12CullingShaders.ResetOcclusionArgsCs, "main", null, "cs_5_0", out var resetBlob, out _);

        var resetDesc = new ComputePipelineStateDescription
        {
            RootSignature = _rootSignature,
            ComputeShader = resetBlob.AsMemory()
        };
        _resetArgsPso = device.CreateComputePipelineState(resetDesc);
        resetBlob.Dispose();
    }

    private void CreateDescriptorHeap(ID3D12Device device)
    {
        // Need descriptors for:
        // 0: Edges SRV (t0)
        // 1: Nodes SRV (t1)
        // 2: Depth buffer SRV (t2)
        // 3: Visible edges UAV (u0)
        // 4: Indirect args UAV (u1)
        _srvUavHeap = device.CreateDescriptorHeap(new DescriptorHeapDescription(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
            5,
            DescriptorHeapFlags.ShaderVisible));

        _descriptorSize = (int)device.GetDescriptorHandleIncrementSize(
            DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
    }

    private void CreateCommandSignature(ID3D12Device device)
    {
        // DrawInstanced command signature
        var argDesc = new IndirectArgumentDescription
        {
            Type = IndirectArgumentType.Draw
        };

        _drawInstancedSignature = device.CreateCommandSignature<ID3D12CommandSignature>(
            new CommandSignatureDescription(
                Unsafe.SizeOf<DrawArguments>(),
                [argDesc]),
            null);
    }

    private unsafe void CreateConstantBuffer(ID3D12Device device)
    {
        ulong size = (ulong)((Unsafe.SizeOf<OcclusionCullingConstants>() + 255) & ~255);

        _constantsBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(size),
            ResourceStates.GenericRead);

        void* mapped;
        _constantsBuffer.Map(0, null, &mapped);
        _constantsPtr = (IntPtr)mapped;
    }

    private void CreateDataBuffers(ID3D12Device device, int maxEdges, int maxNodes)
    {
        ulong edgesSize = (ulong)(maxEdges * Dx12PackedEdgeData.SizeInBytes);
        ulong nodesSize = (ulong)(maxNodes * Dx12PackedNodeData.SizeInBytes);

        // Input edges (upload heap)
        _inputEdgesBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(edgesSize),
            ResourceStates.GenericRead);

        // Input nodes (upload heap)
        _inputNodesBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Upload),
            HeapFlags.None,
            ResourceDescription.Buffer(nodesSize),
            ResourceStates.GenericRead);

        // Visible edges output (default heap, UAV)
        _visibleEdgesBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(edgesSize, ResourceFlags.AllowUnorderedAccess),
            ResourceStates.UnorderedAccess);

        // Indirect args buffer (4 uints)
        _indirectArgsBuffer = device.CreateCommittedResource(
            new HeapProperties(HeapType.Default),
            HeapFlags.None,
            ResourceDescription.Buffer(16, ResourceFlags.AllowUnorderedAccess),
            ResourceStates.UnorderedAccess);

        // Create SRV/UAV descriptors
        if (_srvUavHeap is not null)
        {
            var handle = _srvUavHeap.GetCPUDescriptorHandleForHeapStart();

            // Edges SRV (t0)
            var edgesSrv = new ShaderResourceViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Buffer = new BufferShaderResourceView
                {
                    FirstElement = 0,
                    NumElements = (uint)maxEdges,
                    StructureByteStride = (uint)Dx12PackedEdgeData.SizeInBytes
                }
            };
            device.CreateShaderResourceView(_inputEdgesBuffer, edgesSrv, handle);
            handle += _descriptorSize;

            // Nodes SRV (t1)
            var nodesSrv = new ShaderResourceViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = ShaderResourceViewDimension.Buffer,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Buffer = new BufferShaderResourceView
                {
                    FirstElement = 0,
                    NumElements = (uint)maxNodes,
                    StructureByteStride = (uint)Dx12PackedNodeData.SizeInBytes
                }
            };
            device.CreateShaderResourceView(_inputNodesBuffer, nodesSrv, handle);
            handle += _descriptorSize;

            // Skip slot 2 (depth buffer SRV - set dynamically)
            handle += _descriptorSize;

            // Visible edges UAV (u0)
            var visibleEdgesUav = new UnorderedAccessViewDescription
            {
                Format = Format.Unknown,
                ViewDimension = UnorderedAccessViewDimension.Buffer,
                Buffer = new BufferUnorderedAccessView
                {
                    FirstElement = 0,
                    NumElements = (uint)maxEdges,
                    StructureByteStride = (uint)Dx12PackedEdgeData.SizeInBytes,
                    Flags = BufferUnorderedAccessViewFlags.None
                }
            };
            device.CreateUnorderedAccessView(_visibleEdgesBuffer, null, visibleEdgesUav, handle);
            handle += _descriptorSize;

            // Indirect args UAV (u1)
            var argsUav = new UnorderedAccessViewDescription
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
            device.CreateUnorderedAccessView(_indirectArgsBuffer, null, argsUav, handle);
        }
    }

    private void ReleaseDataBuffers()
    {
        _inputEdgesBuffer?.Dispose();
        _inputEdgesBuffer = null;

        _inputNodesBuffer?.Dispose();
        _inputNodesBuffer = null;

        _visibleEdgesBuffer?.Dispose();
        _visibleEdgesBuffer = null;

        _indirectArgsBuffer?.Dispose();
        _indirectArgsBuffer = null;

        _maxEdges = 0;
        _maxNodes = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _initialized = false;

        if (_constantsPtr != IntPtr.Zero)
        {
            _constantsBuffer?.Unmap(0);
            _constantsPtr = IntPtr.Zero;
        }

        ReleaseDataBuffers();

        _constantsBuffer?.Dispose();
        _drawInstancedSignature?.Dispose();
        _srvUavHeap?.Dispose();
        _resetArgsPso?.Dispose();
        _occlusionCullPso?.Dispose();
        _rootSignature?.Dispose();

        _device = null;

        System.Diagnostics.Debug.WriteLine("[OcclusionCulling] Disposed");
    }
}
