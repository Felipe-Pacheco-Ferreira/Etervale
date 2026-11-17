// SpriteBatch's effect in the browser runtime (monogame/issues/022-webgpu-effects.md): the framework's SpriteEffect sets
// MatrixTransform (the sprite batch's projection) and SpriteBatch binds the sprite texture at slot 0. Compiled for the
// WebGPU profile by monogame/pipeline/StockEffects into SpriteEffect.wgpu.mgfxo.h.

cbuffer SpriteConstants : register(b0)
{
    float4x4 MatrixTransform;
};

Texture2D<float4> Texture : register(t0);
SamplerState TextureSampler : register(s0) = sampler_state { Texture = (Texture); };

struct SpriteVertexInput
{
    float4 Position : POSITION0;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

struct SpriteVertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR0;
    float2 Uv : TEXCOORD0;
};

SpriteVertexOutput SpriteVertex(SpriteVertexInput input)
{
    SpriteVertexOutput output;
    output.Position = mul(input.Position, MatrixTransform);
    output.Color = input.Color;
    output.Uv = input.Uv;
    return output;
}

float4 SpritePixel(SpriteVertexOutput input) : SV_Target0
{
    return Texture.Sample(TextureSampler, input.Uv) * input.Color;
}

technique SpriteBatch
{
    pass
    {
        VertexShader = compile vs_6_0 SpriteVertex();
        PixelShader = compile ps_6_0 SpritePixel();
    }
}
