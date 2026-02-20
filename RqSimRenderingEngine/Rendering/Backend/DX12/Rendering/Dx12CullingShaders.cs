namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

/// <summary>
/// Contains HLSL compute shader source code for GPU-driven culling.
/// </summary>
internal static class Dx12CullingShaders
{
    /// <summary>
    /// Edge culling compute shader.
    /// Performs frustum culling and subpixel culling on edges.
    /// Uses RWStructuredBuffer for output with atomic counter.
    /// </summary>
    public const string EdgeCullingCs = """
        // Root signature embedded in shader
        #define CULL_RS "RootFlags(0), CBV(b0), SRV(t0), UAV(u0), UAV(u1)"

        struct EdgeVertex
        {
            float3 Position;
            float4 Color;
        };

        cbuffer CullingCB : register(b0)
        {
            float4x4 ViewProj;
            float3 CameraPosition;
            float MinProjectedSize;
            uint TotalEdgeCount;
            float3 _pad;
        };

        // Input: all edge vertices (2 vertices per edge)
        StructuredBuffer<EdgeVertex> AllEdges : register(t0);

        // Output: visible edge vertices
        RWStructuredBuffer<EdgeVertex> VisibleEdges : register(u0);

        // Output: indirect draw arguments and counter
        // [0] = VertexCount, [1] = InstanceCount, [2] = StartVertex, [3] = StartInstance
        RWBuffer<uint> IndirectArgs : register(u1);

        // Calculate projected size of edge on screen (for subpixel culling)
        float GetProjectedSize(float3 start, float3 end)
        {
            float4 clipStart = mul(float4(start, 1.0), ViewProj);
            float4 clipEnd = mul(float4(end, 1.0), ViewProj);
            
            // Handle behind-camera cases
            if (clipStart.w <= 0.001 && clipEnd.w <= 0.001)
                return 0;
            
            clipStart.w = max(clipStart.w, 0.001);
            clipEnd.w = max(clipEnd.w, 0.001);
            
            float2 ndcStart = clipStart.xy / clipStart.w;
            float2 ndcEnd = clipEnd.xy / clipEnd.w;
            
            return length(ndcEnd - ndcStart);
        }

        // Simple frustum check via clip space
        bool IsEdgeVisible(float3 start, float3 end)
        {
            float4 cs0 = mul(float4(start, 1.0), ViewProj);
            float4 cs1 = mul(float4(end, 1.0), ViewProj);
            
            // Check if completely behind near plane
            if (cs0.w < 0.001 && cs1.w < 0.001)
                return false;
            
            // Simple check: if any endpoint is in frustum, edge is visible
            bool p0Inside = abs(cs0.x) <= cs0.w && abs(cs0.y) <= cs0.w && cs0.z >= 0 && cs0.z <= cs0.w;
            bool p1Inside = abs(cs1.x) <= cs1.w && abs(cs1.y) <= cs1.w && cs1.z >= 0 && cs1.z <= cs1.w;
            
            return p0Inside || p1Inside;
        }

        [RootSignature(CULL_RS)]
        [numthreads(64, 1, 1)]
        void main(uint3 DTid : SV_DispatchThreadID)
        {
            uint edgeIndex = DTid.x;
            
            if (edgeIndex >= TotalEdgeCount)
                return;
            
            uint v0Index = edgeIndex * 2;
            uint v1Index = edgeIndex * 2 + 1;
            
            EdgeVertex v0 = AllEdges[v0Index];
            EdgeVertex v1 = AllEdges[v1Index];
            
            // Frustum culling
            if (!IsEdgeVisible(v0.Position, v1.Position))
                return;
            
            // Subpixel culling
            float projSize = GetProjectedSize(v0.Position, v1.Position);
            if (projSize < MinProjectedSize)
                return;
            
            // Edge passed culling - add to output
            uint outIndex;
            InterlockedAdd(IndirectArgs[0], 2, outIndex);
            
            VisibleEdges[outIndex] = v0;
            VisibleEdges[outIndex + 1] = v1;
        }
        """;
    /// <summary>
    /// Shader to reset indirect draw arguments buffer before culling.
    /// </summary>
    public const string ResetIndirectArgsCs = """
        #define CULL_RS "RootFlags(0), CBV(b0), SRV(t0), UAV(u0), UAV(u1)"

        RWBuffer<uint> IndirectArgs : register(u1);

        [RootSignature(CULL_RS)]
        [numthreads(1, 1, 1)]
        void main(uint3 DTid : SV_DispatchThreadID)
        {
            IndirectArgs[0] = 0;  // VertexCountPerInstance
            IndirectArgs[1] = 1;  // InstanceCount
            IndirectArgs[2] = 0;  // StartVertexLocation
            IndirectArgs[3] = 0;  // StartInstanceLocation
        }
        """;
    #region Occlusion Culling Shaders

    /// <summary>
    /// Root signature for occlusion culling with Depth Buffer access.
    /// - b0: Culling constants (ViewProj, ScreenSize, DepthBias, etc.)
    /// - t0: All edges (StructuredBuffer)
    /// - t1: All nodes (StructuredBuffer)
    /// - t2: Depth buffer (Texture2D SRV)
    /// - s0: Point sampler
    /// - u0: Visible edges output (AppendStructuredBuffer)
    /// - u1: Indirect args buffer
    /// </summary>
    public const string OcclusionCullRootSig = """
        #define OCCLUSION_RS "RootFlags(0), \
            CBV(b0), \
            SRV(t0), \
            SRV(t1), \
            DescriptorTable(SRV(t2)), \
            StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_POINT, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP), \
            UAV(u0), \
            UAV(u1)"
        """;
    /// <summary>
    /// Edge occlusion culling compute shader.
    /// Tests edges against depth buffer filled by Depth Pre-Pass.
    /// Culls edges where BOTH endpoints are occluded by geometry.
    /// </summary>
    public const string EdgeOcclusionCullCs = """
        #define OCCLUSION_RS "RootFlags(0), CBV(b0), SRV(t0), SRV(t1), DescriptorTable(SRV(t2)), StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_POINT, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP), UAV(u0), UAV(u1)"

        // PackedEdgeData: 16 bytes
        struct PackedEdgeData
        {
            int NodeIndexA;
            int NodeIndexB;
            float Weight;
            float Tension;
        };

        // PackedNodeData: 32 bytes
        struct PackedNodeData
        {
            float3 Position;
            float Scale;
            uint ColorEncoded;
            float Energy;
            uint Flags;
            float Padding;
        };

        cbuffer OcclusionCB : register(b0)
        {
            float4x4 ViewProj;
            float2 ScreenSize;
            float DepthBias;
            float MinProjectedSize;
            uint TotalEdgeCount;
            float3 _pad;
        };

        // Input buffers
        StructuredBuffer<PackedEdgeData> AllEdges : register(t0);
        StructuredBuffer<PackedNodeData> AllNodes : register(t1);
        Texture2D<float> DepthBuffer : register(t2);
        SamplerState PointSampler : register(s0);

        // Output buffers
        RWStructuredBuffer<PackedEdgeData> VisibleEdges : register(u0);
        RWBuffer<uint> IndirectArgs : register(u1);

        // Project world position to screen coordinates + NDC depth
        float3 ProjectToScreen(float3 worldPos)
        {
            float4 clip = mul(float4(worldPos, 1.0), ViewProj);
            
            // Handle behind-camera case
            if (clip.w <= 0.001)
                return float3(-1, -1, -1); // Invalid position
            
            float3 ndc = clip.xyz / clip.w;
            
            // NDC ? Screen coordinates
            float2 screen;
            screen.x = (ndc.x * 0.5 + 0.5) * ScreenSize.x;
            screen.y = (1.0 - (ndc.y * 0.5 + 0.5)) * ScreenSize.y; // Y flip for D3D
            
            return float3(screen, ndc.z);
        }

        // Check if a screen position is occluded by depth buffer
        bool IsOccluded(float3 screenPos)
        {
            // Bounds check - off-screen points are not occluded
            if (screenPos.x < 0 || screenPos.x >= ScreenSize.x ||
                screenPos.y < 0 || screenPos.y >= ScreenSize.y ||
                screenPos.z < 0) // Behind camera
                return false;
            
            float2 uv = screenPos.xy / ScreenSize;
            float depthSample = DepthBuffer.SampleLevel(PointSampler, uv, 0);
            
            // Reverse-Z: larger depth = closer to camera
            // Point is occluded if its depth is LESS than buffer (behind geometry)
            // Add bias to avoid self-occlusion artifacts
            return (screenPos.z + DepthBias) < depthSample;
        }

        // Frustum visibility check
        bool IsInFrustum(float3 pos)
        {
            float4 cs = mul(float4(pos, 1.0), ViewProj);
            if (cs.w <= 0.001)
                return false;
            return abs(cs.x) <= cs.w && abs(cs.y) <= cs.w && cs.z >= 0 && cs.z <= cs.w;
        }

        // Calculate projected edge size for subpixel culling
        float GetProjectedSize(float3 start, float3 end)
        {
            float4 clipStart = mul(float4(start, 1.0), ViewProj);
            float4 clipEnd = mul(float4(end, 1.0), ViewProj);
            
            if (clipStart.w <= 0.001 && clipEnd.w <= 0.001)
                return 0;
            
            clipStart.w = max(clipStart.w, 0.001);
            clipEnd.w = max(clipEnd.w, 0.001);
            
            float2 ndcStart = clipStart.xy / clipStart.w;
            float2 ndcEnd = clipEnd.xy / clipEnd.w;
            
            return length(ndcEnd - ndcStart);
        }

        [RootSignature(OCCLUSION_RS)]
        [numthreads(256, 1, 1)]
        void main(uint3 DTid : SV_DispatchThreadID)
        {
            uint edgeIndex = DTid.x;
            
            if (edgeIndex >= TotalEdgeCount)
                return;
            
            PackedEdgeData edge = AllEdges[edgeIndex];
            
            float3 posA = AllNodes[edge.NodeIndexA].Position;
            float3 posB = AllNodes[edge.NodeIndexB].Position;
            
            // 1. Frustum culling - at least one endpoint must be visible
            bool inFrustumA = IsInFrustum(posA);
            bool inFrustumB = IsInFrustum(posB);
            
            if (!inFrustumA && !inFrustumB)
                return; // Both endpoints outside frustum
            
            // 2. Subpixel culling
            float projSize = GetProjectedSize(posA, posB);
            if (projSize < MinProjectedSize)
                return; // Edge too small to see
            
            // 3. Occlusion culling - check depth buffer
            float3 screenA = ProjectToScreen(posA);
            float3 screenB = ProjectToScreen(posB);
            
            bool occludedA = IsOccluded(screenA);
            bool occludedB = IsOccluded(screenB);
            
            // Conservative strategy: cull only if BOTH endpoints are occluded
            if (occludedA && occludedB)
                return; // Fully hidden behind geometry
            
            // Edge is visible - add to output
            uint outIndex;
            InterlockedAdd(IndirectArgs[0], 1, outIndex);
            
            VisibleEdges[outIndex] = edge;
        }
        """;
    /// <summary>
    /// Reset indirect args for instanced draw (6 vertices per edge instance).
    /// </summary>
    public const string ResetOcclusionArgsCs = """
        #define OCCLUSION_RS "RootFlags(0), CBV(b0), SRV(t0), SRV(t1), DescriptorTable(SRV(t2)), StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_POINT, addressU=TEXTURE_ADDRESS_CLAMP, addressV=TEXTURE_ADDRESS_CLAMP), UAV(u0), UAV(u1)"

        RWBuffer<uint> IndirectArgs : register(u1);

        [RootSignature(OCCLUSION_RS)]
        [numthreads(1, 1, 1)]
        void main(uint3 DTid : SV_DispatchThreadID)
        {
            // For DrawInstanced(6, instanceCount, 0, 0)
            // IndirectArgs[0] = visible edge count (incremented by culling shader)
            IndirectArgs[0] = 0;  // InstanceCount - will be atomically incremented
            IndirectArgs[1] = 6;  // VertexCountPerInstance (6 vertices per quad)
            IndirectArgs[2] = 0;  // StartInstanceLocation
            IndirectArgs[3] = 0;  // StartVertexLocation
        }
        """;
    #endregion
}
