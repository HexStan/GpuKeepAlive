namespace GpuKeepAlive.Core;

/// <summary>保活服务的三级负载着色器源码。</summary>
internal static class Shaders
{
    public const string VertexShader = @"
struct VSOutput {
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
};

VSOutput VSMain(uint id : SV_VertexID) {
    VSOutput output;
    output.UV = float2((id << 1) & 2, id & 2);
    output.Pos = float4(output.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return output;
}
";

    public const string PixelShader = @"
cbuffer TimeBuffer : register(b0) {
    float Time;
    float3 Padding;
};

struct VSOutput {
    float4 Pos : SV_POSITION;
    float2 UV : TEXCOORD0;
};

float4 PSMain(VSOutput input) : SV_Target {
    float2 uv = input.UV;
    float r = 0.5f + 0.5f * sin(uv.x * 10.0f + Time);
    float g = 0.5f + 0.5f * cos(uv.y * 10.0f + Time * 0.7f);
    float b = 0.5f + 0.5f * sin((uv.x + uv.y) * 5.0f + Time * 1.3f);
    return float4(r, g, b, 1.0f);
}
";

    public const string ComputeShader = @"
cbuffer TimeBuffer : register(b0) {
    float Time;
    float3 Padding;
};

RWTexture2D<float4> OutputTexture : register(u0);

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID) {
    float2 uv = float2(id.xy) / 512.0f;
    float r = sin(uv.x * 20.0f + Time) * cos(uv.y * 20.0f + Time);
    float g = cos(uv.x * 15.0f - Time * 0.8f);
    float b = sin(uv.y * 15.0f + Time * 1.2f);
    OutputTexture[id.xy] = float4(r, g, b, 1.0f);
}
";
}
