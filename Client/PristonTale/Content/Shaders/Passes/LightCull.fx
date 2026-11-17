// Tiled culling of the local lights (Forward+; PristonLightCulling): one compute group per 16x16-pixel tile reads the
// depth pre-pass's linear depth (one pixel of every 2x2 block, enough for spheres hundreds of units wide) for the
// tile's depth range, then tests every local light's sphere against the tile's four side planes and that range
// (Harada, McKee and Yang 2012). The lit passes loop over the bits of their tile's mask only (Lighting/Shading.hlsli
// AddLocalLights). Without a pre-pass the range is the whole view depth.
#include "../Core/Common.hlsli"

#define MAX_LOCAL_LIGHTS 32
#define TILE_SIZE 16
#define GROUP_SIZE 8

cbuffer LightCullConstants : register(b0)
{
    float4x4 View;                               // world -> view (the camera looks along -z)
    float4 Projection;                           // x 1 / tan(half fov x), y 1 / tan(half fov y), z near, w far
    float4 Grid;                                 // x tiles across, y tiles down, z light count, w 1 = depth pre-pass bound
    float4 Target;                               // xy target size (px)
    float4 LightPosition[MAX_LOCAL_LIGHTS];      // xyz world position, w radius
};

Texture2D<float4> LinearDepth : register(t0);
SamplerState LinearDepthSampler : register(s0) = sampler_state
{
    Texture = (LinearDepth); MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp;
};

RWStructuredBuffer<uint> TileMasks : register(u0);


// Atomic counters of the group, through the effect processor's MGFX_ATOMIC_* macros (HLSL atomics on Vulkan, WGSL atomics
// on WebGPU).
groupshared MGFX_ATOMIC_UINT TileMin;
groupshared MGFX_ATOMIC_UINT TileMax;
groupshared MGFX_ATOMIC_UINT TileMask;

[numthreads(GROUP_SIZE, GROUP_SIZE, 1)]
void CS_Cull(uint3 group : SV_GroupID, uint3 thread : SV_GroupThreadID, uint index : SV_GroupIndex)
{
    if (index == 0)
    {
        MGFX_ATOMIC_STORE(TileMin, asuint(1.0e9));
        MGFX_ATOMIC_STORE(TileMax, 0u);
        MGFX_ATOMIC_STORE(TileMask, 0u);
    }

    GroupMemoryBarrierWithGroupSync();
    uint2 pixel = group.xy * TILE_SIZE + thread.xy * 2;
    if (Grid.w > 0.5 && pixel.x < (uint)Target.x && pixel.y < (uint)Target.y)
    {
        float depth = LinearDepth.SampleLevel(LinearDepthSampler, (float2(pixel) + 0.5) / Target.xy, 0).r;
        if (depth < SkyDepthThreshold)
        {
            // Positive floats keep their order as unsigned integers.
            MGFX_ATOMIC_MIN(TileMin, asuint(depth));
            MGFX_ATOMIC_MAX(TileMax, asuint(depth));
        }
    }

    GroupMemoryBarrierWithGroupSync();
    float minDepth = Projection.z;
    float maxDepth = Projection.w;
    if (Grid.w > 0.5)
    {
        // A tile of sky only: no surface for the lights to reach.
        uint tileMax = MGFX_ATOMIC_LOAD(TileMax);
        minDepth = tileMax == 0 ? 1.0e9 : asfloat(MGFX_ATOMIC_LOAD(TileMin));
        maxDepth = tileMax == 0 ? 0.0 : asfloat(tileMax);
    }

    uint light = index;
    if (light < (uint)Grid.z)
    {
        float4 sphere = LightPosition[light];
        float3 p = mul(float4(sphere.xyz, 1.0), View).xyz;
        float radius = sphere.w;
        float depth = -p.z;
        bool inside = radius > 0.0 && depth + radius >= minDepth && depth - radius <= maxDepth;
        // The tile's side planes through the eye; a point at view depth d has z = -d.
        float2 ndc0 = float2(group.xy * TILE_SIZE) / Target.xy * 2.0 - 1.0;
        float2 ndc1 = float2((group.xy + 1) * TILE_SIZE) / Target.xy * 2.0 - 1.0;
        float left = ndc0.x / Projection.x;
        float right = ndc1.x / Projection.x;
        float top = -ndc0.y / Projection.y;
        float bottom = -ndc1.y / Projection.y;
        float3 nLeft = normalize(float3(1.0, 0.0, left));
        float3 nRight = normalize(float3(-1.0, 0.0, -right));
        float3 nBottom = normalize(float3(0.0, 1.0, bottom));
        float3 nTop = normalize(float3(0.0, -1.0, -top));
        inside = inside && dot(nLeft, p) >= -radius && dot(nRight, p) >= -radius
            && dot(nBottom, p) >= -radius && dot(nTop, p) >= -radius;
        if (inside)
        {
            MGFX_ATOMIC_OR(TileMask, 1u << light);
        }
    }

    GroupMemoryBarrierWithGroupSync();
    if (index == 0)
    {
        TileMasks[group.y * (uint)Grid.x + group.x] = MGFX_ATOMIC_LOAD(TileMask);
    }
}

technique Cull { pass P0 { ComputeShader = compile cs_6_0 CS_Cull(); } }
