namespace RqSimRenderingEngine.Rendering.Backend.DX12.Rendering;

internal static class Dx12Shaders
{
    // Root signature definition - embedded in shader
    private const string RootSigDef = @"
#define RS ""RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), CBV(b0, visibility=SHADER_VISIBILITY_ALL)""
";

    // ImGui root signature - CBV for projection + descriptor table for font texture SRV
    private const string ImGuiRootSigDef = @"
#define IMGUI_RS ""RootFlags(ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT), \
    CBV(b0, visibility=SHADER_VISIBILITY_VERTEX), \
    DescriptorTable(SRV(t0), visibility=SHADER_VISIBILITY_PIXEL), \
    StaticSampler(s0, filter=FILTER_MIN_MAG_MIP_LINEAR, addressU=TEXTURE_ADDRESS_WRAP, addressV=TEXTURE_ADDRESS_WRAP, addressW=TEXTURE_ADDRESS_WRAP, visibility=SHADER_VISIBILITY_PIXEL)""
";

    // CRITICAL: System.Numerics.Matrix4x4 is row-major in memory.
    // HLSL by default interprets matrices as column-major.
    // Using row_major modifier ensures correct interpretation of C# matrices.
    internal const string NodeVs = RootSigDef + @"
cbuffer Camera : register(b0)
{
    row_major float4x4 View;
    row_major float4x4 Projection;
};

struct VSInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;

    float3 InstancePos : INSTANCEPOS;
    float  InstanceRadius : INSTANCERADIUS;
    float4 InstanceColor : INSTANCECOLOR;
};

struct VSOutput
{
    float4 Position : SV_Position;
    float3 NormalWs : NORMAL;
    float4 Color : COLOR0;
};

[RootSignature(RS)]
VSOutput main(VSInput input)
{
    VSOutput o;

    float3 worldPos = input.InstancePos + input.Position * input.InstanceRadius;
    float4 viewPos = mul(float4(worldPos, 1.0), View);
    o.Position = mul(viewPos, Projection);

    o.NormalWs = input.Normal;
    o.Color = input.InstanceColor;

    return o;
}";

    internal const string NodePs = RootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float3 NormalWs : NORMAL;
    float4 Color : COLOR0;
};

[RootSignature(RS)]
float4 main(PSInput input) : SV_Target
{
    float3 lightDir = normalize(float3(0.3, 0.8, 0.4));
    float ndl = saturate(dot(normalize(input.NormalWs), lightDir));
    float3 lit = input.Color.rgb * (0.25 + 0.75 * ndl);

    return float4(lit, input.Color.a);
}";

    /// <summary>
    /// Depth-only pixel shader for Depth Pre-Pass.
    /// Outputs nothing - only used to fill the depth buffer.
    /// </summary>
    internal const string DepthOnlyPs = RootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float3 NormalWs : NORMAL;
    float4 Color : COLOR0;
};

[RootSignature(RS)]
void main(PSInput input)
{
    // No output - depth-only pass
    // The pixel shader is required but does nothing
}";

    internal const string LineVs = RootSigDef + @"
cbuffer Camera : register(b0)
{
    row_major float4x4 View;
    row_major float4x4 Projection;
};

struct VSInput
{
    float3 Position : POSITION;
    float4 Color : COLOR0;
};

struct VSOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

[RootSignature(RS)]
VSOutput main(VSInput input)
{
    VSOutput o;

    float4 viewPos = mul(float4(input.Position, 1.0), View);
    o.Position = mul(viewPos, Projection);
    o.Color = input.Color;

    return o;
}";

    internal const string LinePs = RootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
};

[RootSignature(RS)]
float4 main(PSInput input) : SV_Target
{
    return input.Color;
}";

    /// <summary>
    /// ImGui vertex shader - transforms 2D vertices with orthographic projection.
    /// Uses column-major matrix multiplication as in original imgui_impl_dx12.
    /// </summary>
    internal const string ImGuiVs = ImGuiRootSigDef + @"
cbuffer ProjectionMatrix : register(b0)
{
    float4x4 ProjectionMatrix;
};

struct VSInput
{
    float2 Position : POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
};

struct VSOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;  // Back to COLOR0 semantic for proper interpolation
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
VSOutput main(VSInput input)
{
    VSOutput o;
    // Column-major: matrix * vector (matches original imgui_impl_dx12)
    float4 pos = mul(ProjectionMatrix, float4(input.Position.xy, 0.0, 1.0));
    o.Position = pos;
    o.DebugPos = pos;
    o.TexCoord = input.TexCoord;
    o.Color = input.Color;
    return o;
}";

    /// <summary>
    /// ImGui pixel shader - samples font texture and modulates with vertex color.
    /// NOTE: ImGui font atlas stores glyph coverage in the alpha channel.
    /// For RGBA textures from GetTexDataAsRGBA32, the RGB channels are set to 0xFF (white),
    /// and the alpha channel contains the glyph coverage.
    /// We use texture alpha to modulate both vertex color RGB and alpha.
    /// </summary>
    internal const string ImGuiPs = ImGuiRootSigDef + @"
Texture2D FontTexture : register(t0);
SamplerState FontSampler : register(s0);

struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    float4 texColor = FontTexture.Sample(FontSampler, input.TexCoord);
    
    // ImGui font texture: RGB is white (1,1,1), alpha is glyph coverage.
    // Multiply vertex color by texture to get final color.
    // For font rendering: output = vertexColor * texAlpha
    return input.Color * texColor;
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that outputs solid magenta.
    /// Use this to verify vertex transformation works.
    /// </summary>
    internal const string ImGuiPsDebug = ImGuiRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    // Solid magenta to verify geometry is being rasterized
    return float4(1.0, 0.0, 1.0, 1.0);
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that outputs vertex color only (no texture).
    /// Forces alpha to 1.0 to make colors visible.
    /// </summary>
    internal const string ImGuiPsVertexColorOnly = ImGuiRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    // DIAGNOSTIC: Output all 4 color components as visible colors
    // This shows R in red channel, G in green, B in blue, A as overall brightness
    
    // If color is all zeros, show CYAN to indicate the problem
    float colorSum = input.Color.r + input.Color.g + input.Color.b + input.Color.a;
    if (colorSum < 0.01)
        return float4(0.0, 1.0, 1.0, 1.0); // CYAN = all zeros
    
    // Otherwise output the actual vertex color with forced alpha
    return float4(input.Color.rgb, 1.0);
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that visualizes texture UV coordinates.
    /// Red = U, Green = V
    /// </summary>
    internal const string ImGuiPsUvDebug = ImGuiRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    // Visualize UV: Red = U, Green = V
    return float4(input.TexCoord.x, input.TexCoord.y, 0.0, 1.0);
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that shows texture alpha only.
    /// If alpha is 0, shows red. If alpha > 0, shows white scaled by alpha.
    /// This helps diagnose if texture is uploading correctly.
    /// </summary>
    internal const string ImGuiPsAlphaDebug = ImGuiRootSigDef + @"
Texture2D FontTexture : register(t0);
SamplerState FontSampler : register(s0);

struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    float4 texColor = FontTexture.Sample(FontSampler, input.TexCoord);
    
    // If texture alpha is 0, show red to indicate texture not working
    // If alpha > 0, show white modulated by alpha
    if (texColor.a < 0.01)
        return float4(1.0, 0.0, 0.0, 1.0); // Red = texture not working
    else
        return float4(texColor.a, texColor.a, texColor.a, 1.0); // White = texture working
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that shows NDC position from vertex shader.
    /// This helps diagnose if the projection matrix is working correctly.
    /// Shows: Red = NDC.X (should be -1 to 1), Green = NDC.Y (should be -1 to 1)
    /// Remap to 0-1 range for visualization: (ndc + 1) / 2
    /// </summary>
    internal const string ImGuiPsPosDebug = ImGuiRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    // Visualize NDC position: remap from [-1,1] to [0,1]
    float2 ndc = input.DebugPos.xy / input.DebugPos.w;
    float r = (ndc.x + 1.0) * 0.5;
    float g = (ndc.y + 1.0) * 0.5;
    return float4(r, g, 0.0, 1.0);
}";

    /// <summary>
    /// ImGui pixel shader - DEBUG version that directly outputs raw vertex color bytes.
    /// This shader outputs the exact color values received from the vertex shader.
    /// Used to diagnose if color data is passing through correctly.
    /// </summary>
    internal const string ImGuiPsColorBytesDebug = ImGuiRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float4 DebugPos : TEXCOORD1;
};

[RootSignature(IMGUI_RS)]
float4 main(PSInput input) : SV_Target
{
    // DIAGNOSTIC: Show different colors based on which color component has value
    // This helps identify byte order issues
    
    // If R > 0.5: show RED
    if (input.Color.r > 0.5)
        return float4(1.0, 0.0, 0.0, 1.0);
    
    // If G > 0.5: show GREEN  
    if (input.Color.g > 0.5)
        return float4(0.0, 1.0, 0.0, 1.0);
    
    // If B > 0.5: show BLUE
    if (input.Color.b > 0.5)
        return float4(0.0, 0.0, 1.0, 1.0);
    
    // If A > 0.5 but RGB all low: show YELLOW (alpha-only issue)
    if (input.Color.a > 0.5)
        return float4(1.0, 1.0, 0.0, 1.0);
    
    // If all components are near zero: show MAGENTA
    return float4(1.0, 0.0, 1.0, 1.0);
}";

    #region Edge Quad Shaders (Vertex Pulling)

    /// <summary>
    /// Root signature for Edge Quad shaders using Vertex Pulling.
    /// - b0: Camera constants (ViewProj, CameraPos, BaseThickness)
    /// - t0: Edges StructuredBuffer
    /// - t1: Nodes StructuredBuffer
    /// </summary>
    private const string EdgeQuadRootSigDef = @"
#define EDGE_QUAD_RS ""RootFlags(0), \
    CBV(b0, visibility=SHADER_VISIBILITY_VERTEX), \
    SRV(t0, visibility=SHADER_VISIBILITY_VERTEX), \
    SRV(t1, visibility=SHADER_VISIBILITY_VERTEX)""
";

    /// <summary>
    /// Edge Quad Vertex Shader - generates 6 vertices (2 triangles) per edge using Vertex Pulling.
    /// No vertex buffer needed - geometry is generated procedurally from StructuredBuffers.
    /// Called via DrawInstanced(6, edgeCount, 0, 0).
    /// </summary>
    internal const string EdgeQuadVs = EdgeQuadRootSigDef + @"
cbuffer CameraData : register(b0)
{
    row_major float4x4 View;
    row_major float4x4 Projection;
    float3 CameraPos;
    float BaseThickness;
};

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

StructuredBuffer<PackedEdgeData> Edges : register(t0);
StructuredBuffer<PackedNodeData> Nodes : register(t1);

struct VSOutput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
    float Tension : TEXCOORD1;
    float4 Color : COLOR0;
};

// Decode RGBA8 to float4
float4 DecodeColor(uint encoded)
{
    float r = float(encoded & 0xFF) / 255.0;
    float g = float((encoded >> 8) & 0xFF) / 255.0;
    float b = float((encoded >> 16) & 0xFF) / 255.0;
    float a = float((encoded >> 24) & 0xFF) / 255.0;
    return float4(r, g, b, a);
}

[RootSignature(EDGE_QUAD_RS)]
VSOutput main(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    VSOutput output;

    // 1. Read edge data
    PackedEdgeData edge = Edges[instanceID];
    PackedNodeData nodeA = Nodes[edge.NodeIndexA];
    PackedNodeData nodeB = Nodes[edge.NodeIndexB];
    
    float3 p0 = nodeA.Position;
    float3 p1 = nodeB.Position;

    // 2. Compute billboard basis
    float3 edgeDir = p1 - p0;
    float edgeLength = length(edgeDir);
    
    // Handle degenerate edges
    if (edgeLength < 0.0001)
    {
        output.Position = float4(0, 0, -1, 1); // Behind camera
        output.UV = float2(0, 0);
        output.Tension = 0;
        output.Color = float4(0, 0, 0, 0);
        return output;
    }
    
    float3 edgeDirNorm = edgeDir / edgeLength;
    float3 edgeCenter = (p0 + p1) * 0.5;
    float3 viewDir = normalize(CameraPos - edgeCenter);
    
    // Right vector perpendicular to edge and view direction
    float3 right = cross(edgeDirNorm, viewDir);
    float rightLen = length(right);
    
    // Handle edge parallel to view direction
    if (rightLen < 0.0001)
    {
        right = float3(1, 0, 0); // Fallback
    }
    else
    {
        right = right / rightLen;
    }

    // 3. Compute UV for this vertex (6 vertices = 2 triangles forming a quad)
    // Triangle 1: 0, 1, 2
    // Triangle 2: 3, 4, 5
    // Quad layout:
    //   2---5
    //   |\ /|
    //   | X |  (degenerate, actually 2 triangles)
    //   |/ \|
    //   0---1/4 (vertex 3 = vertex 2, vertex 4 = vertex 1)
    
    float2 uv;
    uint v = vertexID % 6;
    
    // UV mapping for quad vertices
    if (v == 0)      uv = float2(0.0, 0.0); // Bottom-left
    else if (v == 1) uv = float2(1.0, 0.0); // Bottom-right
    else if (v == 2) uv = float2(0.0, 1.0); // Top-left
    else if (v == 3) uv = float2(0.0, 1.0); // Top-left (same as 2)
    else if (v == 4) uv = float2(1.0, 0.0); // Bottom-right (same as 1)
    else             uv = float2(1.0, 1.0); // Top-right

    // 4. Compute thickness based on tension
    // High tension = thin line, low tension = thick line
    float thickness = BaseThickness * (1.0 - edge.Tension * 0.5);
    thickness = max(thickness, BaseThickness * 0.1); // Minimum thickness

    // 5. Compute world position
    float3 pos = lerp(p0, p1, uv.y);           // Interpolate along edge
    pos += right * (uv.x - 0.5) * thickness;   // Offset perpendicular to edge

    // 6. Transform to clip space
    float4 viewPos = mul(float4(pos, 1.0), View);
    output.Position = mul(viewPos, Projection);
    
    output.UV = uv;
    output.Tension = edge.Tension;
    
    // Blend colors from both nodes
    float4 colorA = DecodeColor(nodeA.ColorEncoded);
    float4 colorB = DecodeColor(nodeB.ColorEncoded);
    output.Color = lerp(colorA, colorB, uv.y);

    return output;
}";

    /// <summary>
    /// Edge Quad Pixel Shader - renders edge quads with anti-aliasing and tension-based coloring.
    /// </summary>
    internal const string EdgeQuadPs = EdgeQuadRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
    float Tension : TEXCOORD1;
    float4 Color : COLOR0;
};

[RootSignature(EDGE_QUAD_RS)]
float4 main(PSInput input) : SV_Target
{
    // Anti-aliasing: fade out at edges of the quad
    float distFromCenter = abs(input.UV.x - 0.5) * 2.0; // 0 at center, 1 at edges
    float alpha = 1.0 - smoothstep(0.7, 1.0, distFromCenter);
    
    // Also fade at the ends of the edge
    float endFade = 1.0 - smoothstep(0.9, 1.0, abs(input.UV.y - 0.5) * 2.0);
    alpha *= endFade;
    
    // Use vertex color (blended from nodes)
    float3 color = input.Color.rgb;
    
    // Optional: modulate by tension (uncomment for tension-based coloring)
    // float3 tensionColor = lerp(float3(0.2, 0.4, 1.0), float3(1.0, 0.2, 0.1), input.Tension);
    // color = lerp(color, tensionColor, 0.5);
    
    return float4(color, alpha * input.Color.a);
}";

    /// <summary>
    /// Edge Quad Pixel Shader - DEBUG version with solid color for visibility testing.
    /// </summary>
    internal const string EdgeQuadPsDebug = EdgeQuadRootSigDef + @"
struct PSInput
{
    float4 Position : SV_Position;
    float2 UV : TEXCOORD0;
    float Tension : TEXCOORD1;
    float4 Color : COLOR0;
};

[RootSignature(EDGE_QUAD_RS)]
float4 main(PSInput input) : SV_Target
{
    // Solid color based on tension for debugging
    float3 color = lerp(float3(0.0, 0.0, 1.0), float3(1.0, 0.0, 0.0), input.Tension);
    return float4(color, 1.0);
}";
    #endregion
}
