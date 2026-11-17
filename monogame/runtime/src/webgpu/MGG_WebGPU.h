// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the WebGPU backend's shared state (monogame/issues/021-webgpu-backend.md, 022-webgpu-effects.md),
// used by MGG_WebGPU.cpp (device, frame, passes, draws), MGG_WebGPU_Resources.cpp (buffers, textures, states) and
// MGG_WebGPU_Pipelines.cpp (shaders, bind groups, pipelines).

#pragma once

#include "api_MGG.h"
#include "mg_common.h"

#include <emscripten/html5_webgpu.h>
#include <webgpu/webgpu.h>

#include <unordered_map>

const int MGWGPU_MAX_TEXTURE_SLOTS = 16;
const int MGWGPU_MAX_STORAGE_SLOTS = 8;
const int MGWGPU_MAX_VERTEX_BUFFERS = 8;
const int MGWGPU_MAX_TARGETS = 4;
// Vertex, pixel and compute (MGShaderStage).
const int MGWGPU_STAGES = 3;

// The WGSL bindings the effect processor's WebGPU profile gives a stage's registers (WebGpuShaderProfile): every
// resource in bind group 0, b0 at the stage's base, t registers at +32, s registers at +48, u registers at +64, and the
// pixel stage's base at 128. The Vulkan binding header that precedes the WGSL numbers the registers 0 (b0), 32 + n
// (t and s) and 64 + n (u).
const uint32_t MGWGPU_BINDING_TEXTURE = 32;
const uint32_t MGWGPU_BINDING_SAMPLER = 48;
const uint32_t MGWGPU_BINDING_STORAGE = 64;
const uint32_t MGWGPU_BINDING_PIXEL_BASE = 128;
const uint32_t MGWGPU_VULKAN_TEXTURE_BINDING = 32;
const uint32_t MGWGPU_VULKAN_UAV_BINDING = 64;

// The uniform ring: every draw's constant buffers are copied into it at the queue's next submission, at offsets that
// meet WebGPU's minUniformBufferOffsetAlignment guarantee.
const uint32_t MGWGPU_UNIFORM_RING_SIZE = 4 * 1024 * 1024;
const uint32_t MGWGPU_UNIFORM_ALIGNMENT = 256;

struct MGG_GraphicsSystem
{
	WGPUInstance instance = nullptr;
};

struct MGG_GraphicsAdapter
{
	MGG_DisplayMode mode = { MGSurfaceFormat::Color, 0, 0 };
};

struct MGG_BlendState
{
	MGG_BlendState_Info targets[MGWGPU_MAX_TARGETS];
	uint32_t id;
};

struct MGG_DepthStencilState
{
	MGG_DepthStencilState_Info info;
	uint32_t id;
};

struct MGG_RasterizerState
{
	MGG_RasterizerState_Info info;
	uint32_t id;
};

struct MGG_SamplerState
{
	WGPUSampler sampler = nullptr;
};

// A buffer keeps a copy of its contents: strided writes land in it first, a constant buffer lives only there (its
// draws copy it into the uniform ring), and the GPU buffer is written from it.
struct MGG_Buffer
{
	MGBufferType type;
	mgint size;
	WGPUBuffer buffer = nullptr;
	std::vector<mgbyte> shadow;
	uint64_t usedSubmission = 0;
	uint64_t ringSubmission = 0;
	uint32_t ringOffset = 0;
	bool ringStale = true;
};

struct MGG_Texture
{
	MGTextureType type;
	WGPUTextureFormat format;
	mgint width;
	mgint height;
	mgint depth;
	mgint mipmaps;
	mgint slices;
	WGPUTexture texture = nullptr;
	WGPUTextureView view = nullptr;
	std::vector<WGPUTextureView> targetViews;
	MGDepthFormat depthFormat = MGDepthFormat::None;
	WGPUTexture depthTexture = nullptr;
	WGPUTextureView depthView = nullptr;
	uint64_t usedSubmission = 0;
};

struct MGWGPU_InputLocation
{
	MGVertexElementUsage usage;
	mguint index;
	mguint location;
};

struct MGWGPU_Binding
{
	uint32_t binding;
	WGPUShaderStageFlags visibility;
	enum class Kind { Uniform, Texture, Sampler, ReadStorage, Storage } kind;
	mgint slot;
	MGTextureType textureType;
};

struct MGG_Shader
{
	MGShaderStage stage;
	uint32_t id;
	WGPUShaderModule module = nullptr;
	std::vector<MGWGPU_Binding> bindings;
	std::vector<MGWGPU_InputLocation> inputs;
	// A compute shader's pipeline, made at its first dispatch.
	WGPUComputePipeline computePipeline = nullptr;
};

struct MGG_InputLayout
{
	uint32_t id;
	std::vector<MGG_InputElement> elements;
	std::vector<mgint> strides;
};

struct MGG_OcclusionQuery
{
	mgint pixels = 0;
};

// The bind group layout and pipeline layout of a vertex and pixel shader pair, or of a compute shader.
struct MGWGPU_Layout
{
	WGPUBindGroupLayout bindGroupLayout = nullptr;
	WGPUPipelineLayout pipelineLayout = nullptr;
	std::vector<MGWGPU_Binding> bindings;
};

struct MGG_GraphicsDevice
{
	WGPUDevice device = nullptr;
	WGPUQueue queue = nullptr;
	WGPUSurface surface = nullptr;
	WGPUSwapChain swapchain = nullptr;
	WGPUTextureFormat format = WGPUTextureFormat_BGRA8Unorm;
	std::string canvas;
	mgint width = 0;
	mgint height = 0;
	mgint frameIndex = 0;
	WGPUTextureView frameView = nullptr;
	MGDepthFormat backBufferDepthFormat = MGDepthFormat::None;
	WGPUTexture backBufferDepth = nullptr;
	WGPUTextureView backBufferDepthView = nullptr;

	// The submission the recorded commands belong to; a resource stamped with it is read by them.
	uint64_t submission = 1;
	WGPUCommandEncoder encoder = nullptr;
	WGPURenderPassEncoder pass = nullptr;
	WGPUBuffer uniformRing = nullptr;
	uint32_t uniformRingOffset = 0;

	MGG_Texture* targets[MGWGPU_MAX_TARGETS] = { };
	mgint targetSlices[MGWGPU_MAX_TARGETS] = { };
	mgint targetCount = 0;
	bool passTargetsStale = true;

	bool clearPending = false;
	MGClearOptions clearOptions = (MGClearOptions)0;
	WGPUColor clearColor = { 0, 0, 0, 0 };
	float clearDepth = 1.0f;
	uint32_t clearStencil = 0;

	MGG_BlendState* blendState = nullptr;
	WGPUColor blendFactor = { 1, 1, 1, 1 };
	MGG_DepthStencilState* depthStencilState = nullptr;
	MGG_RasterizerState* rasterizerState = nullptr;
	mgint viewport[4] = { };
	float viewportDepth[2] = { 0, 1 };
	mgint scissor[4] = { };
	// The pass's dynamic state (viewport, scissor, blend constant, stencil reference) must be set before the next draw.
	bool passStateStale = true;

	MGG_Shader* vertexShader = nullptr;
	MGG_Shader* pixelShader = nullptr;
	MGG_Shader* computeShader = nullptr;
	MGG_InputLayout* inputLayout = nullptr;
	MGG_Buffer* constantBuffers[MGWGPU_STAGES] = { };
	MGG_Texture* textures[MGWGPU_STAGES][MGWGPU_MAX_TEXTURE_SLOTS] = { };
	MGG_SamplerState* samplers[MGWGPU_STAGES][MGWGPU_MAX_TEXTURE_SLOTS] = { };
	// Storage buffers: read-only ones share the t registers with textures, writable ones have the u registers.
	MGG_Buffer* readBuffers[MGWGPU_STAGES][MGWGPU_MAX_TEXTURE_SLOTS] = { };
	MGG_Buffer* storageBuffers[MGWGPU_STAGES][MGWGPU_MAX_STORAGE_SLOTS] = { };
	MGG_Buffer* vertexBuffers[MGWGPU_MAX_VERTEX_BUFFERS] = { };
	mgint vertexOffsets[MGWGPU_MAX_VERTEX_BUFFERS] = { };
	MGG_Buffer* indexBuffer = nullptr;
	MGIndexElementSize indexSize = MGIndexElementSize::SixteenBits;

	uint32_t nextStateId = 1;
	MGG_Texture* fallbackTexture = nullptr;
	MGG_Texture* fallbackCube = nullptr;
	MGG_Texture* fallbackVolume = nullptr;
	WGPUSampler fallbackSampler = nullptr;
	std::unordered_map<uint64_t, MGWGPU_Layout> layouts;
	std::unordered_map<std::string, WGPURenderPipeline> pipelines;
	std::unordered_map<std::string, WGPUBindGroup> bindGroups;

	// Scratch storage of a draw's cache keys and bind group, reused so a draw allocates only when a cache misses.
	std::string pipelineKey;
	std::string bindGroupKey;
	std::vector<WGPUBindGroupEntry> bindGroupEntries;
	std::vector<uint32_t> dynamicOffsets;
};

// MGG_WebGPU.cpp
void MGWGPU_Flush(MGG_GraphicsDevice* device);
bool MGWGPU_BeginPass(MGG_GraphicsDevice* device);
WGPUComputePassEncoder MGWGPU_BeginComputePass(MGG_GraphicsDevice* device);
void MGWGPU_TargetSize(const MGG_GraphicsDevice* device, mgint& width, mgint& height);

// MGG_WebGPU_Resources.cpp
WGPUTextureFormat MGWGPU_ToTextureFormat(MGSurfaceFormat format);
WGPUTextureFormat MGWGPU_ToDepthFormat(MGDepthFormat format);
bool MGWGPU_HasStencil(MGDepthFormat format);
WGPUCompareFunction MGWGPU_ToCompare(MGCompareFunction function);
MGG_Texture* MGWGPU_CreateFallbackTexture(MGG_GraphicsDevice* device, MGTextureType type);
void MGWGPU_ForgetBindGroups(MGG_GraphicsDevice* device);

// MGG_WebGPU_Pipelines.cpp
bool MGWGPU_PrepareDraw(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, bool indexed);
WGPUComputePassEncoder MGWGPU_PrepareDispatch(MGG_GraphicsDevice* device);
void MGWGPU_ReleasePipelines(MGG_GraphicsDevice* device);
