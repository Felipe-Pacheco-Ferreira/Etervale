// Etervale fork: WebGpuRepro's compute check (monogame/issues/022). Count: each thread adds 1 to its group's counter
// (workgroup atomics) and each group adds its count to Totals[0] (storage atomics). Resolve: one thread compares the
// total with the threads the count ran, writes the verdict colour to Colors[0] and clears Totals[0] for the next frame.
// Show: a quad whose pixel shader reads that colour from a read-only storage buffer. The page samples the quad.

cbuffer CheckConstants : register(b0)
{
    float4 Expected;   // x the number of threads of the count dispatch
};

RWStructuredBuffer<MGFX_ATOMIC_UINT> Totals : register(u0);
RWStructuredBuffer<float4> Colors : register(u1);
StructuredBuffer<float4> Verdict : register(t0);

groupshared MGFX_ATOMIC_UINT GroupCount;

[numthreads(64, 1, 1)]
void CS_Count(uint index : SV_GroupIndex)
{
    if (index == 0)
    {
        MGFX_ATOMIC_STORE(GroupCount, 0u);
    }

    GroupMemoryBarrierWithGroupSync();
    MGFX_ATOMIC_ADD(GroupCount, 1u);
    GroupMemoryBarrierWithGroupSync();
    if (index == 0)
    {
        MGFX_ATOMIC_ADD(Totals[0], MGFX_ATOMIC_LOAD(GroupCount));
    }
}

[numthreads(1, 1, 1)]
void CS_Resolve()
{
    uint total = MGFX_ATOMIC_LOAD(Totals[0]);
    Colors[0] = total == (uint)Expected.x ? float4(0.1, 0.8, 0.3, 1.0) : float4(0.9, 0.1, 0.1, 1.0);
    MGFX_ATOMIC_STORE(Totals[0], 0u);
}

float4 VS_Show(float4 position : POSITION0) : SV_Position
{
    return position;
}

float4 PS_Show() : SV_Target0
{
    return Verdict[0];
}

technique Count { pass P0 { ComputeShader = compile cs_6_0 CS_Count(); } }
technique Resolve { pass P0 { ComputeShader = compile cs_6_0 CS_Resolve(); } }
technique Show { pass P0 { VertexShader = compile vs_6_0 VS_Show(); PixelShader = compile ps_6_0 PS_Show(); } }
