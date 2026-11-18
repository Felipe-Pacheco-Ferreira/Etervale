// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in LICENSE.txt.
// Etervale fork: CPU resource and sprite/VFX adapter for the Three.js browser renderer.
// No graphics device is created here. Three owns the active WebGPU or WebGL 2 device.
#include "api_MGG.h"
#include "mg_common.h"
#include "mg_etervale.h"
#include <emscripten.h>
#include <algorithm>
#include <array>
#include <cstdio>
#include <limits>
#include <string_view>
#include <vector>
#include "../webgpu/effects/SpriteEffect.wgpu.mgfxo.h"

struct MGG_GraphicsSystem {};
struct MGG_GraphicsAdapter { MGG_DisplayMode mode = { MGSurfaceFormat::Color, 1280, 720 }; };
struct MGG_Buffer { MGBufferType type; std::vector<mgbyte> data; bool dynamic; uint64_t revision = 0; MGG_GraphicsDevice* owner; };
struct MGG_Texture { int width, height; MGSurfaceFormat format; bool target; std::vector<mgbyte> rgba; };
struct MGG_BlendState { MGG_BlendState_Info info; };
struct MGG_DepthStencilState { MGG_DepthStencilState_Info info; };
struct MGG_RasterizerState { MGG_RasterizerState_Info info; };
struct MGG_SamplerState { MGG_SamplerState_Info info; };
enum class MGThree_ShaderKind { Unknown = 0, Sprite = 1, Unlit = 2, Vfx = 3, RainDrops = 4, RainSplashes = 5 };
struct MGG_Shader { MGShaderStage stage; MGThree_ShaderKind kind; std::array<char, 96> group; };
struct MGG_InputLayout { std::vector<int> strides; std::vector<MGG_InputElement> elements; };
struct MGG_OcclusionQuery {};
struct MGThree_RainGeometry
{
    MGG_Buffer* vertices; MGG_Buffer* indices;
    uint64_t vertexRevision, indexRevision;
    int64_t baseVertex;
    MGIndexElementSize indexSize;
    uint32_t id;
};
struct MGG_GraphicsDevice
{
    int width = 1280, height = 720, frame = 0;
    MGG_Buffer* vertices[8] = {};
    int offsets[8] = {};
    MGG_Buffer* indices = nullptr;
    MGIndexElementSize indexSize = MGIndexElementSize::SixteenBits;
    MGG_Buffer* constants[3][16] = {};
    MGG_Texture* textures[3][16] = {};
    MGG_SamplerState* samplers[3][16] = {};
    MGG_InputLayout* layout = nullptr;
    MGG_Shader* shaders[3] = {};
    MGG_BlendState* blend = nullptr;
    float blendFactor[4] = { 1, 1, 1, 1 };
    MGG_DepthStencilState* depth = nullptr;
    MGG_RasterizerState* raster = nullptr;
    MGG_Texture* target = nullptr;
    int scissor[4] = { 0, 0, 1280, 720 };
    int viewport[4] = { 0, 0, 1280, 720 };
    std::vector<float> positions, colors, uvs;
    std::vector<uint32_t> drawIndices;
    std::vector<MGThree_RainGeometry> rainGeometries;
    std::vector<MGG_Buffer*> buffers;
};

static uint32_t MGThree_NextRainGeometry = 1;

EM_JS(void, MGThree_Begin, (int width, int height), { Module.noxiusThreeGraphics.beginFrame(width, height); });
EM_JS(void, MGThree_Present, (), { Module.noxiusThreeGraphics.present(); });
EM_JS(void, MGThree_Target, (uintptr_t id), { Module.noxiusThreeGraphics.setRenderTarget(id >>> 0); });
EM_JS(void, MGThree_Clear, (int options, float r, float g, float b, float a), {
    Module.noxiusThreeGraphics.clear(r, g, b, a, (options & 2) !== 0, (options & 1) !== 0);
});
EM_JS(void, MGThree_TextureCreate, (uintptr_t id, int width, int height, int target, int encoding, int format), {
    Module.noxiusThreeGraphics.createTexture(id >>> 0, width, height, null, !!target, encoding, format);
});
EM_JS(void, MGThree_TextureUpdate, (uintptr_t id, int x, int y, int width, int height, const mgbyte* data), {
    Module.noxiusThreeGraphics.updateTexture(id >>> 0, x, y, width, height, HEAPU8.subarray(data, data + width * height * 4));
});
EM_JS(void, MGThree_TextureDestroy, (uintptr_t id), { Module.noxiusThreeGraphics.destroyTexture(id >>> 0); });
EM_JS(void, MGThree_Draw, (const float* positions, const float* uvs, const float* colors, int vertices,
    const uint32_t* indices, int count, uintptr_t texture, const float* matrix, int blend,
    int depthTest, int depthWrite, const int* scissor, const int* viewport, uintptr_t target, int srgbColors,
    const int* blendState, const float* blendFactor, int depthCompare, const int* sampler, int shaderKind, const float* pixelParams), {
    Module.noxiusThreeGraphics.drawNativeMemory(HEAPU8.buffer,
        positions, uvs, colors, vertices, indices, count, texture >>> 0, matrix, blend,
        depthTest, depthWrite, scissor, viewport, target >>> 0, srgbColors, blendState, blendFactor, depthCompare, sampler, shaderKind, pixelParams);
});
EM_JS(void, MGThree_RainDraw, (const float* positions, const float* shapes, int vertices,
    const uint32_t* indices, int count, uintptr_t texture, const float* matrix, const float* vertexParams, const float* pixelParams,
    int shaderKind, int depthTest, int depthWrite, const int* scissor, const int* viewport, uintptr_t target,
    const int* blendState, const float* blendFactor, int depthCompare), {
    Module.noxiusThreeGraphics.drawRainMemory(HEAPU8.buffer,
        positions, shapes, vertices, indices, count, texture >>> 0, matrix, vertexParams, pixelParams,
        shaderKind, depthTest, depthWrite, scissor, viewport, target >>> 0, blendState, blendFactor, depthCompare);
});
EM_JS(void, MGThree_RainGeometryCreate, (uint32_t id, const float* positions, const float* shapes, int vertices, const uint32_t* indices, int count), {
    Module.noxiusThreeGraphics.createRainGeometryMemory(id >>> 0, HEAPU8.buffer, positions, shapes, vertices, indices, count);
});
EM_JS(void, MGThree_RainGeometryDestroy, (uint32_t id), { Module.noxiusThreeGraphics.destroyRainGeometry(id >>> 0); });
EM_JS(void, MGThree_RainGeometryDraw, (uint32_t id, int start, int count, uintptr_t texture, const float* matrix,
    const float* vertexParams, const float* pixelParams, int depthTest, int depthWrite, const int* scissor,
    const int* viewport, uintptr_t target, const int* blendState, const float* blendFactor, int depthCompare), {
    Module.noxiusThreeGraphics.drawRainGeometryMemory(id >>> 0, start, count, HEAPU8.buffer, texture >>> 0, matrix, vertexParams, pixelParams,
        depthTest, depthWrite, scissor, viewport, target >>> 0, blendState, blendFactor, depthCompare);
});

static void MGThree_InvalidateRain(MGG_GraphicsDevice* device, MGG_Buffer* buffer = nullptr)
{
    if (!device) return;
    auto& resources = device->rainGeometries;
    for (size_t i = 0; i < resources.size();)
    {
        if (!buffer || resources[i].vertices == buffer || resources[i].indices == buffer)
        {
            MGThree_RainGeometryDestroy(resources[i].id);
            resources.erase(resources.begin() + i);
        }
        else i++;
    }
}

static void MGThree_Unsupported(const char* operation)
{
    EM_ASM({ throw new Error('Three graphics adapter: unsupported operation ' + UTF8ToString($0)); }, operation);
}

void MGG_EffectResource_GetBytecode(const char* name, mgbyte*& bytes, mgint& size)
{
    bytes = nullptr; size = 0;
    if (strcmp(name, "SpriteEffect") == 0) { bytes = (mgbyte*)SpriteEffect_wgpu_mgfxo; size = sizeof(SpriteEffect_wgpu_mgfxo); }
}
MGG_GraphicsSystem* MGG_GraphicsSystem_Create() { return new MGG_GraphicsSystem(); }
void MGG_GraphicsSystem_Destroy(MGG_GraphicsSystem* system) { delete system; }
MGG_GraphicsAdapter* MGG_GraphicsAdapter_Get(MGG_GraphicsSystem* system, mgint index)
{ static MGG_GraphicsAdapter adapter; return system && index == 0 ? &adapter : nullptr; }
void MGG_GraphicsAdapter_GetInfo(MGG_GraphicsAdapter*, MGG_GraphicsAdaptor_Info& info)
{
    static MGG_GraphicsAdapter adapter;
    info = {}; info.DeviceName = (void*)"Three.js"; info.Description = (void*)"Browser scene and UI adapter";
    info.DisplayModes = &adapter.mode; info.DisplayModeCount = 1; info.CurrentDisplayMode = adapter.mode;
}
MGG_GraphicsDevice* MGG_GraphicsDevice_Create(MGG_GraphicsSystem*, MGG_GraphicsAdapter*) { return new MGG_GraphicsDevice(); }
void MGG_GraphicsDevice_Destroy(MGG_GraphicsDevice* device)
{
    MGThree_InvalidateRain(device);
    for (auto buffer : device->buffers) buffer->owner = nullptr;
    delete device;
}
void MGG_GraphicsDevice_GetCaps(MGG_GraphicsDevice*, MGG_GraphicsDevice_Caps& caps)
{
    caps = {}; caps.MaxTextureSlots = 16; caps.MaxVertexTextureSlots = 16; caps.MaxVertexBufferSlots = 8;
    caps.ShaderProfile = 81; caps.MaxTextureAnisotropy = 16; caps.MaxMultiSampleCount = 1;
    caps.SupportsNonPowerOfTwo = caps.SupportsTextureFilterAnisotropic = caps.SupportsDepth24 = true;
    caps.SupportsPackedDepthStencil = caps.SupportsTextureMaxLevel = caps.SupportsSRgb = true;
    caps.SupportsTextureArrays = caps.SupportsVertexTextures = caps.SupportsFloatTextures = true;
    caps.SupportsHalfFloatTextures = caps.SupportsNormalized = caps.SupportsSeparateBlendStates = true;
}
mgint MGG_GraphicsDevice_GetBackBufferMultiSampleCount(MGG_GraphicsDevice*) { return 1; }
mgbyte MGG_GraphicsDevice_ResizeSwapchain(MGG_GraphicsDevice* d, void*, mgint width, mgint height, MGSurfaceFormat, MGDepthFormat, mgint, mgint)
{
    if (width <= 0 || height <= 0) return 0;
    d->width = width; d->height = height; d->viewport[2] = width; d->viewport[3] = height;
    d->scissor[2] = width; d->scissor[3] = height;
    return 1;
}
mgint MGG_GraphicsDevice_BeginFrame(MGG_GraphicsDevice* d) { MGThree_Begin(d->width, d->height); return d->frame; }
void MGG_GraphicsDevice_Clear(MGG_GraphicsDevice*, MGClearOptions options, Vector4& color, mgfloat, mgint)
{ MGThree_Clear((int)options, color.X, color.Y, color.Z, color.W); }
void MGG_GraphicsDevice_Present(MGG_GraphicsDevice* d, mgint, mgint) { MGThree_Present(); d->frame++; }
void MGG_GraphicsDevice_SetBlendState(MGG_GraphicsDevice* d, MGG_BlendState* s, mgfloat r, mgfloat g, mgfloat b, mgfloat a)
{ d->blend = s; d->blendFactor[0] = r; d->blendFactor[1] = g; d->blendFactor[2] = b; d->blendFactor[3] = a; }
void MGG_GraphicsDevice_SetDepthStencilState(MGG_GraphicsDevice* d, MGG_DepthStencilState* s) { d->depth = s; }
void MGG_GraphicsDevice_SetRasterizerState(MGG_GraphicsDevice* d, MGG_RasterizerState* s) { d->raster = s; }
void MGG_GraphicsDevice_GetTitleSafeArea(mgint& x, mgint& y, mgint& width, mgint& height) { x = y = 0; width = height = 0; }
void MGG_GraphicsDevice_SetViewport(MGG_GraphicsDevice* d, mgint x, mgint y, mgint w, mgint h, mgfloat, mgfloat)
{ d->viewport[0] = x; d->viewport[1] = y; d->viewport[2] = w; d->viewport[3] = h; }
void MGG_GraphicsDevice_SetScissorRectangle(MGG_GraphicsDevice* d, mgint x, mgint y, mgint w, mgint h)
{ d->scissor[0] = x; d->scissor[1] = y; d->scissor[2] = w; d->scissor[3] = h; }
void MGG_GraphicsDevice_SetRenderTargets(MGG_GraphicsDevice* d, MGG_Texture** targets, mgint*, mgint count)
{ d->target = count > 0 ? targets[0] : nullptr; MGThree_Target((uintptr_t)d->target); }
void MGG_GraphicsDevice_SetConstantBuffer(MGG_GraphicsDevice* d, MGShaderStage s, mgint slot, MGG_Buffer* b)
{ if (slot >= 0 && slot < 16) d->constants[(int)s][slot] = b; }
void MGG_GraphicsDevice_SetTexture(MGG_GraphicsDevice* d, MGShaderStage s, mgint slot, MGG_Texture* t)
{ if (slot >= 0 && slot < 16) d->textures[(int)s][slot] = t; }
void MGG_GraphicsDevice_SetSamplerState(MGG_GraphicsDevice* d, MGShaderStage stage, mgint slot, MGG_SamplerState* state)
{ if (slot >= 0 && slot < 16) d->samplers[(int)stage][slot] = state; }
void MGG_GraphicsDevice_SetIndexBuffer(MGG_GraphicsDevice* d, MGIndexElementSize s, MGG_Buffer* b) { d->indexSize = s; d->indices = b; }
void MGG_GraphicsDevice_SetVertexBuffer(MGG_GraphicsDevice* d, mgint slot, MGG_Buffer* b, mgint offset)
{ if (slot >= 0 && slot < 8) { d->vertices[slot] = b; d->offsets[slot] = offset; } }
void MGG_GraphicsDevice_SetShader(MGG_GraphicsDevice* d, MGShaderStage stage, MGG_Shader* shader) { d->shaders[(int)stage] = shader; }
void MGG_GraphicsDevice_SetInputLayout(MGG_GraphicsDevice* d, MGG_InputLayout* l) { d->layout = l; }

static float MGThree_Component(const mgbyte* bytes, MGVertexElementFormat format, int component)
{
    if (format == MGVertexElementFormat::Color) return bytes[component] / 255.0f;
    float value = 0; memcpy(&value, bytes + component * 4, 4); return value;
}
static void MGThree_DrawState(MGG_GraphicsDevice* d, float (&matrix)[16], int (&rawBlend)[6], int& blend)
{
    const auto constants = d->constants[(int)MGShaderStage::Vertex][0];
    if (constants && constants->data.size() >= sizeof(matrix))
    {
        const float* source = (const float*)constants->data.data();
        for (int row = 0; row < 4; row++) for (int col = 0; col < 4; col++) matrix[row * 4 + col] = source[col * 4 + row];
    }
    if (!d->blend) return;
    const auto& info = d->blend->info;
    if (info.colorDestBlend != MGBlend::Zero) blend = info.colorDestBlend == MGBlend::One ? 2 : 1;
    rawBlend[0] = (int)info.colorSourceBlend; rawBlend[1] = (int)info.colorDestBlend; rawBlend[2] = (int)info.colorBlendFunc;
    rawBlend[3] = (int)info.alphaSourceBlend; rawBlend[4] = (int)info.alphaDestBlend; rawBlend[5] = (int)info.alphaBlendFunc;
}

static bool MGThree_StaticRain(MGG_GraphicsDevice* d, int start, int count, int base)
{
    const auto shader = d->shaders[(int)MGShaderStage::Vertex];
    const auto layout = d->layout;
    auto vertices = d->vertices[0];
    auto indices = d->indices;
    if (!shader || shader->kind != MGThree_ShaderKind::RainDrops || !vertices || !indices || vertices->dynamic || indices->dynamic
        || vertices->owner != d || indices->owner != d
        || !layout || layout->elements.size() != 2 || layout->strides.size() != 1 || layout->strides[0] != 32) return false;
    bool position = false, shape = false;
    for (const auto& element : layout->elements)
    {
        if (element.VertexBufferSlot != 0 || element.SemanticIndex != 0 || element.Format != MGVertexElementFormat::Vector4) return false;
        if (element.SemanticUsage == MGVertexElementUsage::Position && element.AlignedByteOffset == 0 && !position) position = true;
        else if (element.SemanticUsage == MGVertexElementUsage::TextureCoordinate && element.AlignedByteOffset == 16 && !shape) shape = true;
        else return false;
    }
    if (!position || !shape) return false;
    const int64_t first = (int64_t)base + d->offsets[0];
    if (first < 0 || vertices->data.size() % 32 || (uint64_t)first >= vertices->data.size() / 32) return false;
    const size_t vertexCount = vertices->data.size() / 32 - first;
    const size_t indexSize = d->indexSize == MGIndexElementSize::SixteenBits ? 2 : 4;
    const size_t indexCount = indices->data.size() / indexSize;
    if (indices->data.size() % indexSize || vertexCount > (size_t)std::numeric_limits<int>::max()
        || indexCount > (size_t)std::numeric_limits<int>::max()) return false;
    uint32_t id = 0;
    for (const auto& cached : d->rainGeometries)
    {
        if (cached.vertices == vertices && cached.indices == indices && cached.baseVertex == first && cached.indexSize == d->indexSize
            && cached.vertexRevision == vertices->revision && cached.indexRevision == indices->revision) { id = cached.id; break; }
    }
    if (!id)
    {
        d->drawIndices.resize(indexCount);
        for (size_t i = 0; i < indexCount; i++)
        {
            uint32_t index = 0; memcpy(&index, indices->data.data() + i * indexSize, indexSize);
            if (index >= vertexCount) return false;
            d->drawIndices[i] = index;
        }
        d->positions.resize(vertexCount * 4); d->uvs.resize(vertexCount * 4);
        for (size_t i = 0; i < vertexCount; i++)
        {
            const auto source = vertices->data.data() + ((size_t)first + i) * 32;
            memcpy(d->positions.data() + i * 4, source, 16);
            memcpy(d->uvs.data() + i * 4, source + 16, 16);
        }
        if (!MGThree_NextRainGeometry) { MGThree_Unsupported("rain geometry ID exhausted"); return true; }
        id = MGThree_NextRainGeometry++;
        MGThree_RainGeometryCreate(id, d->positions.data(), d->uvs.data(), (int)vertexCount, d->drawIndices.data(), (int)indexCount);
        d->rainGeometries.push_back({ vertices, indices, vertices->revision, indices->revision, first, d->indexSize, id });
    }
    const auto constants = d->constants[(int)MGShaderStage::Vertex][0];
    const auto pixels = d->constants[(int)MGShaderStage::Pixel][0];
    const auto occlusion = d->textures[(int)MGShaderStage::Pixel][0];
    if (!constants || constants->data.size() < 40 * sizeof(float)) { MGThree_Unsupported("rain vertex constants"); return true; }
    if (!pixels || pixels->data.size() < 9 * sizeof(float)) { MGThree_Unsupported("rain pixel constants"); return true; }
    if (!occlusion || occlusion->format != MGSurfaceFormat::Single) { MGThree_Unsupported("rain occlusion texture format"); return true; }
    float matrix[16] = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
    int blend = 0, rawBlend[6] = { 0,1,0,0,1,0 };
    MGThree_DrawState(d, matrix, rawBlend, blend);
    float pixelParams[12] = {};
    memcpy(pixelParams, pixels->data.data(), std::min(pixels->data.size(), sizeof(pixelParams)));
    MGThree_RainGeometryDraw(id, start, count, (uintptr_t)occlusion, matrix, (const float*)(constants->data.data() + 16 * sizeof(float)), pixelParams,
        d->depth && d->depth->info.depthBufferEnable, d->depth && d->depth->info.depthBufferWriteEnable,
        d->raster && d->raster->info.scissorTestEnable ? d->scissor : nullptr, d->viewport, (uintptr_t)d->target, rawBlend, d->blendFactor,
        d->depth ? (int)d->depth->info.depthBufferFunction : (int)MGCompareFunction::Always);
    return true;
}
static void MGThree_DrawIndices(MGG_GraphicsDevice* d, int baseVertex)
{
    if (!d->layout || d->drawIndices.empty()) return;
    const auto vertexShader = d->shaders[(int)MGShaderStage::Vertex];
    const auto shaderKind = vertexShader ? vertexShader->kind : MGThree_ShaderKind::Sprite;
    if (shaderKind == MGThree_ShaderKind::Unknown)
    {
        std::array<char, 128> operation = {};
        std::snprintf(operation.data(), operation.size(), "shader draw [%s]", vertexShader->group.data());
        MGThree_Unsupported(operation.data()); return;
    }
    const bool rain = shaderKind == MGThree_ShaderKind::RainDrops || shaderKind == MGThree_ShaderKind::RainSplashes;
    const auto maxIndex = *std::max_element(d->drawIndices.begin(), d->drawIndices.end());
    const size_t count = (size_t)maxIndex + 1;
    const int positionStride = rain ? 4 : 3;
    d->positions.assign(count * positionStride, 0); d->uvs.assign(count * (rain ? 4 : 2), 0);
    d->colors.assign(rain ? 0 : count * 4, 1);
    bool srgbColors = false;
    bool hasRainPosition = false, hasRainShape = false;
    for (const auto& e : d->layout->elements)
    {
        if (e.VertexBufferSlot >= 8 || e.VertexBufferSlot >= d->layout->strides.size()) continue;
        auto buffer = d->vertices[e.VertexBufferSlot];
        if (!buffer) continue;
        int components = 0; float* destination = nullptr;
        if (e.SemanticUsage == MGVertexElementUsage::Position)
        {
            if (rain && e.Format != MGVertexElementFormat::Vector4) { MGThree_Unsupported("rain position format"); return; }
            components = rain ? 4 : e.Format == MGVertexElementFormat::Vector2 ? 2 : 3; destination = d->positions.data(); hasRainPosition = rain;
        }
        else if (!rain && e.SemanticUsage == MGVertexElementUsage::Color) { components = 4; destination = d->colors.data(); srgbColors = e.Format == MGVertexElementFormat::Color; }
        else if (e.SemanticUsage == MGVertexElementUsage::TextureCoordinate && e.SemanticIndex == 0)
        {
            if (rain && e.Format != MGVertexElementFormat::Vector4) { MGThree_Unsupported("rain shape format"); return; }
            components = rain ? 4 : 2; destination = d->uvs.data(); hasRainShape = rain;
        }
        if (!destination) continue;
        const int stride = d->layout->strides[e.VertexBufferSlot];
        const int step = e.SemanticUsage == MGVertexElementUsage::Position ? positionStride : components;
        for (size_t i = 0; i < count; i++)
        {
            const int64_t at = ((int64_t)i + baseVertex + d->offsets[e.VertexBufferSlot]) * stride + e.AlignedByteOffset;
            const int bytes = e.Format == MGVertexElementFormat::Color ? 4 : components * 4;
            if (at < 0 || (uint64_t)at + bytes > buffer->data.size()) { MGThree_Unsupported("vertex range"); return; }
            for (int c = 0; c < components; c++) destination[i * step + c] = MGThree_Component(buffer->data.data() + at, e.Format, c);
        }
    }
    if (rain && (!hasRainPosition || !hasRainShape)) { MGThree_Unsupported("rain vertex layout"); return; }
    float matrix[16] = { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 };
    const auto constants = d->constants[(int)MGShaderStage::Vertex][0];
    int blend = 0;
    int rawBlend[6] = { 0, 1, 0, 0, 1, 0 };
    MGThree_DrawState(d, matrix, rawBlend, blend);
    if (rain)
    {
        const auto pixelConstants = d->constants[(int)MGShaderStage::Pixel][0];
        const auto occlusion = d->textures[(int)MGShaderStage::Pixel][0];
        if (!constants || constants->data.size() < 40 * sizeof(float)) { MGThree_Unsupported("rain vertex constants"); return; }
        if (!pixelConstants || pixelConstants->data.size() < 9 * sizeof(float)) { MGThree_Unsupported("rain pixel constants"); return; }
        if (shaderKind == MGThree_ShaderKind::RainDrops && (!occlusion || occlusion->format != MGSurfaceFormat::Single))
        { MGThree_Unsupported("rain occlusion texture format"); return; }
        float pixelParams[12] = {};
        memcpy(pixelParams, pixelConstants->data.data(), std::min(pixelConstants->data.size(), sizeof(pixelParams)));
        MGThree_RainDraw(d->positions.data(), d->uvs.data(), (int)count, d->drawIndices.data(), (int)d->drawIndices.size(),
            (uintptr_t)occlusion, matrix, (const float*)(constants->data.data() + 16 * sizeof(float)), pixelParams,
            (int)shaderKind, d->depth && d->depth->info.depthBufferEnable, d->depth && d->depth->info.depthBufferWriteEnable,
            d->raster && d->raster->info.scissorTestEnable ? d->scissor : nullptr, d->viewport, (uintptr_t)d->target, rawBlend, d->blendFactor,
            d->depth ? (int)d->depth->info.depthBufferFunction : (int)MGCompareFunction::Always);
        return;
    }
    int sampler[4] = { 1, 1, 0, 1 };
    if (d->samplers[(int)MGShaderStage::Pixel][0])
    {
        const auto& info = d->samplers[(int)MGShaderStage::Pixel][0]->info;
        sampler[0] = (int)info.AddressU; sampler[1] = (int)info.AddressV; sampler[2] = (int)info.Filter; sampler[3] = info.MaximumAnisotropy;
    }
    float pixelParams[8] = { 1, 1, 1, 1, 1, 1, 0, 0 };
    if (shaderKind == MGThree_ShaderKind::Vfx) { memset(pixelParams, 0, sizeof(pixelParams)); pixelParams[0] = pixelParams[1] = 1; }
    const auto pixelConstants = d->constants[(int)MGShaderStage::Pixel][0];
    if (shaderKind != MGThree_ShaderKind::Sprite && pixelConstants)
    {
        const size_t bytes = shaderKind == MGThree_ShaderKind::Vfx ? 16 : 32;
        if (pixelConstants->data.size() < bytes) { MGThree_Unsupported("pixel constant buffer range"); return; }
        memcpy(pixelParams, pixelConstants->data.data(), bytes);
    }
    MGThree_Draw(d->positions.data(), d->uvs.data(), d->colors.data(), (int)count, d->drawIndices.data(), (int)d->drawIndices.size(),
        (uintptr_t)d->textures[(int)MGShaderStage::Pixel][0], matrix, blend,
        d->depth && d->depth->info.depthBufferEnable, d->depth && d->depth->info.depthBufferWriteEnable,
        d->raster && d->raster->info.scissorTestEnable ? d->scissor : nullptr, d->viewport, (uintptr_t)d->target, srgbColors, rawBlend, d->blendFactor,
        d->depth ? (int)d->depth->info.depthBufferFunction : (int)MGCompareFunction::Always, sampler, (int)shaderKind, pixelParams);
}
static int MGThree_IndexCount(MGPrimitiveType type, int count)
{ return type == MGPrimitiveType::TriangleStrip ? count + 2 : count * 3; }
void MGG_GraphicsDevice_Draw(MGG_GraphicsDevice* d, MGPrimitiveType type, mgint start, mgint count)
{
    d->drawIndices.resize(count);
    for (int i = 0; i < count; i++) d->drawIndices[i] = i;
    if (type != MGPrimitiveType::TriangleList) { MGThree_Unsupported("non-triangle draw"); return; }
    MGThree_DrawIndices(d, start);
}
void MGG_GraphicsDevice_DrawIndexed(MGG_GraphicsDevice* d, MGPrimitiveType type, mgint primitives, mgint start, mgint base)
{
    if (!d->indices || primitives <= 0) return;
    const int count = MGThree_IndexCount(type, primitives);
    const int size = d->indexSize == MGIndexElementSize::SixteenBits ? 2 : 4;
    if (start < 0 || (size_t)(start + count) * size > d->indices->data.size()) { MGThree_Unsupported("index range"); return; }
    if (type == MGPrimitiveType::TriangleList && MGThree_StaticRain(d, start, count, base)) return;
    d->drawIndices.resize(count);
    for (int i = 0; i < count; i++)
    {
        uint32_t index = 0; memcpy(&index, d->indices->data.data() + (start + i) * size, size); d->drawIndices[i] = index;
    }
    if (type != MGPrimitiveType::TriangleList) { MGThree_Unsupported("non-triangle indexed draw"); return; }
    MGThree_DrawIndices(d, base);
}
void MGG_GraphicsDevice_DrawIndexedInstanced(MGG_GraphicsDevice*, MGPrimitiveType, mgint, mgint, mgint, mgint, mgint) { MGThree_Unsupported("instanced native draw"); }
void MGG_GraphicsDevice_ResolveRenderTargets(MGG_GraphicsDevice*) {}
void MGG_GraphicsDevice_GetBackBufferData(MGG_GraphicsDevice*, mgint, mgint, mgint, mgint, void* data, mgint, mgint bytes) { memset(data, 0, bytes); }

MGG_BlendState* MGG_BlendState_Create(MGG_GraphicsDevice*, MGG_BlendState_Info* info) { return new MGG_BlendState { *info }; }
void MGG_BlendState_Destroy(MGG_GraphicsDevice*, MGG_BlendState* state) { delete state; }
MGG_DepthStencilState* MGG_DepthStencilState_Create(MGG_GraphicsDevice*, MGG_DepthStencilState_Info* info) { return new MGG_DepthStencilState { *info }; }
void MGG_DepthStencilState_Destroy(MGG_GraphicsDevice*, MGG_DepthStencilState* state) { delete state; }
MGG_RasterizerState* MGG_RasterizerState_Create(MGG_GraphicsDevice*, MGG_RasterizerState_Info* info) { return new MGG_RasterizerState { *info }; }
void MGG_RasterizerState_Destroy(MGG_GraphicsDevice*, MGG_RasterizerState* state) { delete state; }
MGG_SamplerState* MGG_SamplerState_Create(MGG_GraphicsDevice*, MGG_SamplerState_Info* info) { return new MGG_SamplerState { *info }; }
void MGG_SamplerState_Destroy(MGG_GraphicsDevice*, MGG_SamplerState* state) { delete state; }
MGG_Buffer* MGG_Buffer_Create(MGG_GraphicsDevice* device, MGBufferType type, mgbool dynamic, mgint bytes)
{
    auto buffer = new MGG_Buffer { type, std::vector<mgbyte>((size_t)bytes), dynamic, 0, device };
    if (device) device->buffers.push_back(buffer);
    return buffer;
}
void MGG_Buffer_Destroy(MGG_GraphicsDevice*, MGG_Buffer* buffer)
{
    if (!buffer) return;
    if (auto device = buffer->owner)
    {
        MGThree_InvalidateRain(device, buffer);
        auto& buffers = device->buffers;
        buffers.erase(std::remove(buffers.begin(), buffers.end(), buffer), buffers.end());
    }
    delete buffer;
}
void MGG_Buffer_SetData(MGG_GraphicsDevice*, MGG_Buffer*& b, mgint offset, mgbyte* data, mgint count, mgint stride, mgint size, mgbool)
{
    if (!b || !data || offset < 0 || count < 0 || stride < size) { MGThree_Unsupported("buffer write"); return; }
    if (b->revision == std::numeric_limits<uint64_t>::max()) { MGThree_Unsupported("buffer revision exhausted"); return; }
    MGThree_InvalidateRain(b->owner, b);
    b->revision++;
    for (int i = 0; i < count; i++)
    {
        const size_t at = (size_t)offset + (size_t)i * stride;
        if (at + size > b->data.size()) { MGThree_Unsupported("buffer write range"); return; }
        memcpy(b->data.data() + at, data + (size_t)i * size, size);
    }
}
void MGG_Buffer_SetDataNoOverwrite(MGG_GraphicsDevice* d, MGG_Buffer* b, mgint o, mgbyte* data, mgint c, mgint stride, mgint size) { MGG_Buffer_SetData(d, b, o, data, c, stride, size, false); }
void MGG_Buffer_GetData(MGG_GraphicsDevice*, MGG_Buffer* b, mgint offset, mgbyte* data, mgint count, mgint bytes, mgint stride)
{
    if (!b || offset < 0 || count <= 0) return;
    const int size = bytes / count;
    for (int i = 0; i < count; i++) { const size_t at = (size_t)offset + (size_t)i * stride; if (at + size <= b->data.size()) memcpy(data + i * size, b->data.data() + at, size); }
}
MG_EXPORT void MGG_Buffer_SetStorageData(MGG_GraphicsDevice* d, MGG_Buffer* b, mgint o, mgbyte* data, mgint bytes) { MGG_Buffer_SetData(d, b, o, data, bytes, 1, 1, false); }
MG_EXPORT void MGG_Buffer_GetStorageData(MGG_GraphicsDevice* d, MGG_Buffer* b, mgint o, mgbyte* data, mgint bytes) { MGG_Buffer_GetData(d, b, o, data, bytes, bytes, 1); }

static MGG_Texture* MGThree_CreateTexture(MGSurfaceFormat format, int width, int height, bool target)
{
    auto texture = new MGG_Texture { width, height, format, target, {} };
    // SurfaceFormat.Color is raw UNORM storage; only explicit sRGB formats ask the GPU to decode sampled bytes.
    const int encoding = format == MGSurfaceFormat::ColorSRgb || format == MGSurfaceFormat::Bgr32SRgb || format == MGSurfaceFormat::Bgra32SRgb ? 1 : 0;
    MGThree_TextureCreate((uintptr_t)texture, width, height, target, encoding, (int)format); return texture;
}
MGG_Texture* MGG_Texture_Create(MGG_GraphicsDevice*, MGTextureType, MGSurfaceFormat f, mgint w, mgint h, mgint, mgint, mgint) { return MGThree_CreateTexture(f, w, h, false); }
MGG_Texture* MGG_RenderTarget_Create(MGG_GraphicsDevice*, MGTextureType, MGSurfaceFormat f, mgint w, mgint h, mgint, mgint, mgint, MGDepthFormat, mgint, MGRenderTargetUsage) { return MGThree_CreateTexture(f, w, h, true); }
MG_EXPORT MGG_Texture* MGG_Texture_CreateStorage(MGG_GraphicsDevice*, MGTextureType, MGSurfaceFormat f, mgint w, mgint h, mgint, mgint, mgint) { return MGThree_CreateTexture(f, w, h, false); }
void MGG_Texture_Destroy(MGG_GraphicsDevice*, MGG_Texture* texture) { if (texture) { MGThree_TextureDestroy((uintptr_t)texture); delete texture; } }
void MGG_Texture_SetData(MGG_GraphicsDevice*, MGG_Texture* t, mgint level, mgint, mgint x, mgint y, mgint, mgint w, mgint h, mgint, mgbyte* data, mgint bytes)
{
    if (level != 0) return;
    if (w == 0 && h == 0) { w = t->width; h = t->height; }
    if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > t->width || y + h > t->height) { MGThree_Unsupported("texture write range"); return; }
    const bool alpha = t->format == MGSurfaceFormat::Alpha8;
    const bool bgra = t->format == MGSurfaceFormat::Bgra32 || t->format == MGSurfaceFormat::Bgra32SRgb
        || t->format == MGSurfaceFormat::Bgr32 || t->format == MGSurfaceFormat::Bgr32SRgb;
    const bool opaque = t->format == MGSurfaceFormat::Bgr32 || t->format == MGSurfaceFormat::Bgr32SRgb;
    if (bytes < w * h * (alpha ? 1 : 4)) { MGThree_Unsupported("texture format"); return; }
    t->rgba.resize((size_t)t->width * t->height * 4);
    std::vector<mgbyte> converted;
    if (alpha) { converted.resize((size_t)w * h * 4); for (int i = 0; i < w * h; i++) { converted[i * 4] = converted[i * 4 + 1] = converted[i * 4 + 2] = 255; converted[i * 4 + 3] = data[i]; } data = converted.data(); }
    else if (bgra)
    {
        converted.resize((size_t)w * h * 4);
        for (int i = 0; i < w * h; i++) { converted[i * 4] = data[i * 4 + 2]; converted[i * 4 + 1] = data[i * 4 + 1]; converted[i * 4 + 2] = data[i * 4]; converted[i * 4 + 3] = opaque ? 255 : data[i * 4 + 3]; }
        data = converted.data();
    }
    for (int row = 0; row < h; row++) memcpy(t->rgba.data() + ((size_t)(y + row) * t->width + x) * 4, data + (size_t)row * w * 4, (size_t)w * 4);
    MGThree_TextureUpdate((uintptr_t)t, x, y, w, h, data);
}
void MGG_Texture_GetData(MGG_GraphicsDevice*, MGG_Texture* t, mgint, mgint, mgint x, mgint y, mgint, mgint w, mgint h, mgint, mgbyte* data, mgint bytes)
{
    memset(data, 0, bytes);
    if (w == 0 && h == 0) { w = t->width; h = t->height; }
    const bool alpha = t->format == MGSurfaceFormat::Alpha8;
    if (t->rgba.empty() || bytes < w * h * (alpha ? 1 : 4) || x < 0 || y < 0 || x + w > t->width || y + h > t->height) return;
    const bool bgra = t->format == MGSurfaceFormat::Bgra32 || t->format == MGSurfaceFormat::Bgra32SRgb
        || t->format == MGSurfaceFormat::Bgr32 || t->format == MGSurfaceFormat::Bgr32SRgb;
    for (int row = 0; row < h; row++) for (int col = 0; col < w; col++)
    {
        const auto source = t->rgba.data() + ((size_t)(y + row) * t->width + x + col) * 4;
        const auto index = (size_t)row * w + col;
        if (alpha) { data[index] = source[3]; continue; }
        auto destination = data + index * 4;
        destination[0] = source[bgra ? 2 : 0]; destination[1] = source[1]; destination[2] = source[bgra ? 0 : 2]; destination[3] = source[3];
    }
}
MGG_InputLayout* MGG_InputLayout_Create(MGG_GraphicsDevice*, MGG_Shader*, mgint* strides, mgint streams, MGG_InputElement* elements, mgint count)
{ return new MGG_InputLayout { std::vector<int>(strides, strides + streams), std::vector<MGG_InputElement>(elements, elements + count) }; }
void MGG_InputLayout_Destroy(MGG_GraphicsDevice*, MGG_InputLayout* layout) { delete layout; }
MGG_Shader* MGG_Shader_Create(MGG_GraphicsDevice*, MGShaderStage stage, mgbyte* bytes, mgint size)
{
    auto kind = MGThree_ShaderKind::Unknown;
    const std::string_view source((const char*)bytes, bytes && size > 0 ? (size_t)size : 0);
    if (source.find("SLANG_ParameterGroup_SpriteConstants_default_0") != std::string_view::npos) kind = MGThree_ShaderKind::Sprite;
    else if (source.find("SLANG_ParameterGroup_Unlit") != std::string_view::npos) kind = MGThree_ShaderKind::Unlit;
    else if (source.find("SLANG_ParameterGroup_Vfx") != std::string_view::npos) kind = MGThree_ShaderKind::Vfx;
    else if (stage == MGShaderStage::Vertex && source.find("SLANG_ParameterGroup_RainVertexConstants") != std::string_view::npos)
    {
        // The pinned WGSL compiler retains the authored vertex field names (also used by its input reflection).
        const auto input = source.find("struct vertexInput_");
        const auto open = input == std::string_view::npos ? input : source.find('{', input);
        const auto close = open == std::string_view::npos ? open : source.find('}', open);
        if (close != std::string_view::npos)
        {
            const auto fields = source.substr(open, close - open);
            if (fields.find(" Seed_") != std::string_view::npos && fields.find(" Corner_") != std::string_view::npos) kind = MGThree_ShaderKind::RainDrops;
            else if (fields.find(" Position_") != std::string_view::npos && fields.find(" Shape_") != std::string_view::npos) kind = MGThree_ShaderKind::RainSplashes;
        }
    }
    auto shader = new MGG_Shader { stage, kind, {} };
    const auto marker = source.find("SLANG_ParameterGroup_");
    if (marker == std::string_view::npos)
        std::snprintf(shader->group.data(), shader->group.size(), "no constant-buffer marker");
    else
    {
        for (size_t index = 0; index + 1 < shader->group.size() && marker + index < source.size(); index++)
        {
            const auto c = source[marker + index];
            if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_') break;
            shader->group[index] = c;
        }
    }
    return shader;
}
void MGG_Shader_Destroy(MGG_GraphicsDevice*, MGG_Shader* shader) { delete shader; }
MGG_OcclusionQuery* MGG_OcclusionQuery_Create(MGG_GraphicsDevice*) { return new MGG_OcclusionQuery(); }
void MGG_OcclusionQuery_Destroy(MGG_GraphicsDevice*, MGG_OcclusionQuery* q) { delete q; }
void MGG_OcclusionQuery_Begin(MGG_GraphicsDevice*, MGG_OcclusionQuery*) {}
void MGG_OcclusionQuery_End(MGG_GraphicsDevice*, MGG_OcclusionQuery*) {}
mgbyte MGG_OcclusionQuery_GetResult(MGG_GraphicsDevice*, MGG_OcclusionQuery*, mgint& pixels) { pixels = 0; return 0; }
MGG_Texture* MGG_RenderTarget_WrapNativeHandle(MGG_GraphicsDevice*, void*, MGSurfaceFormat, mgint, mgint, MGDepthFormat, mgint, mgbyte) { MGThree_Unsupported("external texture"); return nullptr; }
void MGG_GraphicsDevice_GetNativeHandles(const MGG_GraphicsDevice*, MGP_NativeGraphicsHandles* handles) { if (handles) *handles = {}; }
MG_EXPORT mgint MGG_Etervale_RuntimeVersion() { return MG_ETERVALE_RUNTIME_VERSION; }
MG_EXPORT mgint MGG_GraphicsDevice_GetPipelineStatistics(MGG_GraphicsDevice*, mgint, char*, mgint) { return -1; }
MG_EXPORT void* MGG_TimestampPool_Create(MGG_GraphicsDevice*, mguint) { return nullptr; }
MG_EXPORT void MGG_TimestampPool_Destroy(MGG_GraphicsDevice*, void*) {}
MG_EXPORT mgbool MGG_TimestampPool_Reset(MGG_GraphicsDevice*, void*, mguint, mguint) { return false; }
MG_EXPORT void MGG_TimestampPool_Write(MGG_GraphicsDevice*, void*, mguint) {}
MG_EXPORT mgbool MGG_TimestampPool_Read(MGG_GraphicsDevice*, void*, mguint, mguint, mgulong*) { return false; }
MG_EXPORT mgfloat MGG_TimestampPool_GetPeriod(MGG_GraphicsDevice*) { return 0; }
MG_EXPORT mgbool MGG_FrameCapture_Start(MGG_GraphicsDevice*, mgint, mgint) { return false; }
MG_EXPORT void MGG_FrameCapture_Stop(MGG_GraphicsDevice*) {}
MG_EXPORT mgbool MGG_FrameCapture_Pop(MGG_GraphicsDevice*, mgbyte*, mgint, mgint*, mgint*, mgulong*, mgbool*) { return false; }
MG_EXPORT mgulong MGG_FrameCapture_Dropped(MGG_GraphicsDevice*) { return 0; }
MG_EXPORT void MGG_GraphicsDevice_SetShaderBuffer(MGG_GraphicsDevice*, MGShaderStage, mgint, MGG_Buffer*, mgbool) {}
MG_EXPORT void MGG_GraphicsDevice_SetShaderImage(MGG_GraphicsDevice*, MGShaderStage, mgint, MGG_Texture*, mgint) {}
MG_EXPORT void MGG_GraphicsDevice_Dispatch(MGG_GraphicsDevice*, mgint, mgint, mgint) { MGThree_Unsupported("native compute dispatch"); }
MG_EXPORT void MGG_GraphicsDevice_DispatchIndirect(MGG_GraphicsDevice*, MGG_Buffer*, mgint) { MGThree_Unsupported("native indirect dispatch"); }
