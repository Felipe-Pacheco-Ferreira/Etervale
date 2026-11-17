// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the graphics backend of the browser build (monogame/issues/021-webgpu-backend.md,
// 022-webgpu-effects.md). It implements the same exports as MGG_Vulkan.cpp over WebGPU through Emscripten's webgpu.h.
// The page creates the WebGPU device before the .NET runtime starts (device creation is asynchronous in the browser)
// and hands it to Emscripten as Module.preinitializedWebGPUDevice; the swapchain draws into the canvas named by the
// platform's window handle. This file: the device, the frame, render passes, clears, states and draws.
//
// Commands are recorded into one command encoder per submission, in render passes opened when a draw or a clear needs
// one. WebGPU has no clear inside a pass, so a clear ends the open pass and becomes the load operation of the next one.
// Uploads (queue writes) run before the next submission, so writing a resource that recorded commands still read ends
// the submission first (MGWGPU_Flush).

#include "MGG_WebGPU.h"
#include "mg_etervale.h"

#include "effects/SpriteEffect.wgpu.mgfxo.h"

#define MG_BUILTIN_EFFECT_SYMBOL(name) name##_wgpu_mgfxo
#define MG_BUILTIN_EFFECT_BYTES(name) ((mgbyte*)MG_BUILTIN_EFFECT_SYMBOL(name))
#define MG_BUILTIN_EFFECT_SIZE(name) (sizeof(MG_BUILTIN_EFFECT_SYMBOL(name)))

// The stock effects compiled for WebGPU (effects/*.fx, monogame/pipeline/StockEffects). An effect not among them gets
// no bytecode, and the framework refuses to create it.
void MGG_EffectResource_GetBytecode(const char* name, mgbyte*& bytecode, mgint& size)
{
	bytecode = nullptr;
	size = 0;
	if (strcmp(name, "SpriteEffect") == 0)
	{
		bytecode = MG_BUILTIN_EFFECT_BYTES(SpriteEffect);
		size = MG_BUILTIN_EFFECT_SIZE(SpriteEffect);
	}
}

static void MGWGPU_ReleaseView(WGPUTextureView& view)
{
	if (view != nullptr)
	{
		wgpuTextureViewRelease(view);
		view = nullptr;
	}
}

// The canvas texture of this frame: one per frame, taken at its first use (the browser presents it when control returns
// to the event loop).
static WGPUTextureView MGWGPU_FrameView(MGG_GraphicsDevice* device)
{
	if (device->frameView == nullptr && device->swapchain != nullptr)
	{
		device->frameView = wgpuSwapChainGetCurrentTextureView(device->swapchain);
	}

	return device->frameView;
}

static WGPUCommandEncoder MGWGPU_Encoder(MGG_GraphicsDevice* device)
{
	if (device->encoder == nullptr)
	{
		device->encoder = wgpuDeviceCreateCommandEncoder(device->device, nullptr);
	}

	return device->encoder;
}

static void MGWGPU_EndPass(MGG_GraphicsDevice* device)
{
	if (device->pass != nullptr)
	{
		wgpuRenderPassEncoderEnd(device->pass);
		wgpuRenderPassEncoderRelease(device->pass);
		device->pass = nullptr;
	}
}

void MGWGPU_TargetSize(const MGG_GraphicsDevice* device, mgint& width, mgint& height)
{
	if (device->targetCount > 0)
	{
		width = device->targets[0]->width;
		height = device->targets[0]->height;
	}
	else
	{
		width = device->width;
		height = device->height;
	}
}

// Opens a render pass on the bound targets unless one is open on them, with a pending clear as its load operation.
bool MGWGPU_BeginPass(MGG_GraphicsDevice* device)
{
	if (device->pass != nullptr && !device->passTargetsStale && !device->clearPending)
	{
		return true;
	}

	MGWGPU_EndPass(device);

	WGPURenderPassColorAttachment colors[MGWGPU_MAX_TARGETS] = { };
	mgint colorCount = 0;
	WGPUTextureView depthView = nullptr;
	MGDepthFormat depthFormat = MGDepthFormat::None;
	if (device->targetCount == 0)
	{
		colors[0].view = MGWGPU_FrameView(device);
		if (colors[0].view == nullptr)
		{
			return false;
		}

		colorCount = 1;
		depthView = device->backBufferDepthView;
		depthFormat = device->backBufferDepthFormat;
	}
	else if (device->targetCount == 1 && device->targets[0]->format == WGPUTextureFormat_Depth32Float)
	{
		auto target = device->targets[0];
		auto slice = (size_t)device->targetSlices[0];
		depthView = slice < target->targetViews.size() ? target->targetViews[slice] : target->view;
		depthFormat = MGDepthFormat::Depth32Float;
		target->usedSubmission = device->submission;
	}
	else
	{
		for (mgint i = 0; i < device->targetCount; i++)
		{
			auto target = device->targets[i];
			auto slice = (size_t)device->targetSlices[i];
			colors[i].view = slice < target->targetViews.size() ? target->targetViews[slice] : target->view;
			target->usedSubmission = device->submission;
		}

		colorCount = device->targetCount;
		depthView = device->targets[0]->depthView;
		depthFormat = device->targets[0]->depthFormat;
	}

	auto clearMask = device->clearPending ? (mgint)device->clearOptions : 0;
	for (mgint i = 0; i < colorCount; i++)
	{
		colors[i].depthSlice = WGPU_DEPTH_SLICE_UNDEFINED;
		colors[i].loadOp = (clearMask & (mgint)MGClearOptions::Target) != 0 ? WGPULoadOp_Clear : WGPULoadOp_Load;
		colors[i].storeOp = WGPUStoreOp_Store;
		colors[i].clearValue = device->clearColor;
	}

	WGPURenderPassDepthStencilAttachment depth = { };
	WGPURenderPassDescriptor info = { };
	info.colorAttachmentCount = (size_t)colorCount;
	info.colorAttachments = colors;
	if (depthView != nullptr)
	{
		depth.view = depthView;
		depth.depthLoadOp = (clearMask & (mgint)MGClearOptions::DepthBuffer) != 0 ? WGPULoadOp_Clear : WGPULoadOp_Load;
		depth.depthStoreOp = WGPUStoreOp_Store;
		depth.depthClearValue = device->clearDepth;
		if (MGWGPU_HasStencil(depthFormat))
		{
			depth.stencilLoadOp = (clearMask & (mgint)MGClearOptions::Stencil) != 0 ? WGPULoadOp_Clear : WGPULoadOp_Load;
			depth.stencilStoreOp = WGPUStoreOp_Store;
			depth.stencilClearValue = device->clearStencil;
		}

		info.depthStencilAttachment = &depth;
	}

	device->pass = wgpuCommandEncoderBeginRenderPass(MGWGPU_Encoder(device), &info);
	device->clearPending = false;
	device->passTargetsStale = false;
	device->passStateStale = true;
	return true;
}

// Runs a pending clear in a pass of its own, for targets that are left or presented before anything draws on them.
static void MGWGPU_ResolveClear(MGG_GraphicsDevice* device)
{
	if (device->clearPending && MGWGPU_BeginPass(device))
	{
		MGWGPU_EndPass(device);
	}
}

void MGWGPU_Flush(MGG_GraphicsDevice* device)
{
	MGWGPU_ResolveClear(device);
	MGWGPU_EndPass(device);
	if (device->encoder != nullptr)
	{
		WGPUCommandBuffer commands = wgpuCommandEncoderFinish(device->encoder, nullptr);
		wgpuQueueSubmit(device->queue, 1, &commands);
		wgpuCommandBufferRelease(commands);
		wgpuCommandEncoderRelease(device->encoder);
		device->encoder = nullptr;
	}

	device->submission++;
	device->uniformRingOffset = 0;
	device->passTargetsStale = true;
}

MGG_GraphicsSystem* MGG_GraphicsSystem_Create()
{
	auto system = new MGG_GraphicsSystem();
	system->instance = wgpuCreateInstance(nullptr);
	return system;
}

void MGG_GraphicsSystem_Destroy(MGG_GraphicsSystem* system)
{
	if (system == nullptr)
	{
		return;
	}

	if (system->instance != nullptr)
	{
		wgpuInstanceRelease(system->instance);
	}

	delete system;
}

MGG_GraphicsAdapter* MGG_GraphicsAdapter_Get(MGG_GraphicsSystem* system, mgint index)
{
	static MGG_GraphicsAdapter adapter;
	return index == 0 && system != nullptr ? &adapter : nullptr;
}

void MGG_GraphicsAdapter_GetInfo(MGG_GraphicsAdapter* adapter, MGG_GraphicsAdaptor_Info& info)
{
	static const char* name = "WebGPU";
	static const char* description = "";
	info.DeviceName = (void*)name;
	info.Description = (void*)description;
	info.DeviceId = 0;
	info.Revision = 0;
	info.VendorId = 0;
	info.SubSystemId = 0;
	info.MonitorHandle = nullptr;
	info.DisplayModes = &adapter->mode;
	info.DisplayModeCount = 1;
	info.CurrentDisplayMode = adapter->mode;
}

MGG_GraphicsDevice* MGG_GraphicsDevice_Create(MGG_GraphicsSystem* system, MGG_GraphicsAdapter* adapter)
{
	WGPUDevice wgpuDevice = emscripten_webgpu_get_device();
	if (wgpuDevice == nullptr)
	{
		MG_ERROR_PRINT("WebGPU: the page handed over no device (Module.preinitializedWebGPUDevice).");
		return nullptr;
	}

	auto device = new MGG_GraphicsDevice();
	device->device = wgpuDevice;
	device->queue = wgpuDeviceGetQueue(wgpuDevice);

	WGPUBufferDescriptor ring = { };
	ring.usage = WGPUBufferUsage_Uniform | WGPUBufferUsage_CopyDst;
	ring.size = MGWGPU_UNIFORM_RING_SIZE;
	device->uniformRing = wgpuDeviceCreateBuffer(wgpuDevice, &ring);

	device->fallbackTexture = MGWGPU_CreateFallbackTexture(device, MGTextureType::_2D);
	device->fallbackCube = MGWGPU_CreateFallbackTexture(device, MGTextureType::Cube);
	device->fallbackVolume = MGWGPU_CreateFallbackTexture(device, MGTextureType::_3D);
	WGPUSamplerDescriptor sampler = { };
	sampler.addressModeU = WGPUAddressMode_ClampToEdge;
	sampler.addressModeV = WGPUAddressMode_ClampToEdge;
	sampler.addressModeW = WGPUAddressMode_ClampToEdge;
	sampler.magFilter = WGPUFilterMode_Linear;
	sampler.minFilter = WGPUFilterMode_Linear;
	sampler.mipmapFilter = WGPUMipmapFilterMode_Linear;
	sampler.lodMaxClamp = 32.0f;
	sampler.maxAnisotropy = 1;
	device->fallbackSampler = wgpuDeviceCreateSampler(wgpuDevice, &sampler);
	return device;
}

static void MGWGPU_ReleaseBackBufferDepth(MGG_GraphicsDevice* device)
{
	MGWGPU_ReleaseView(device->backBufferDepthView);
	if (device->backBufferDepth != nullptr)
	{
		wgpuTextureDestroy(device->backBufferDepth);
		wgpuTextureRelease(device->backBufferDepth);
		device->backBufferDepth = nullptr;
	}
}

void MGG_GraphicsDevice_Destroy(MGG_GraphicsDevice* device)
{
	if (device == nullptr)
	{
		return;
	}

	MGWGPU_Flush(device);
	MGWGPU_ReleaseView(device->frameView);
	MGWGPU_ReleaseBackBufferDepth(device);
	MGWGPU_ReleasePipelines(device);
	MGG_Texture_Destroy(device, device->fallbackTexture);
	MGG_Texture_Destroy(device, device->fallbackCube);
	MGG_Texture_Destroy(device, device->fallbackVolume);
	wgpuSamplerRelease(device->fallbackSampler);
	wgpuBufferRelease(device->uniformRing);
	if (device->swapchain != nullptr)
	{
		wgpuSwapChainRelease(device->swapchain);
	}

	if (device->surface != nullptr)
	{
		wgpuSurfaceRelease(device->surface);
	}

	wgpuQueueRelease(device->queue);
	wgpuDeviceRelease(device->device);
	delete device;
}

void MGG_GraphicsDevice_GetCaps(MGG_GraphicsDevice* device, MGG_GraphicsDevice_Caps& caps)
{
	// WebGPU's guaranteed limits (maxSampledTexturesPerShaderStage 16, maxVertexBuffers 8); BC formats only where the
	// adapter offers texture-compression-bc, which the device does not request yet.
	caps.MaxTextureSlots = MGWGPU_MAX_TEXTURE_SLOTS;
	caps.MaxVertexBufferSlots = MGWGPU_MAX_VERTEX_BUFFERS;
	caps.MaxVertexTextureSlots = MGWGPU_MAX_TEXTURE_SLOTS;
	caps.MaxTextureAnisotropy = 16;
	caps.SupportsNonPowerOfTwo = true;
	caps.SupportsTextureFilterAnisotropic = true;
	caps.MaxMultiSampleCount = 1;
	caps.SupportsDepth24 = true;
	caps.SupportsPackedDepthStencil = true;
	caps.SupportsDepthNonLinear = false;
	caps.SupportsTextureMaxLevel = true;
	caps.SupportsDxt1 = false;
	caps.SupportsS3tc = false;
	caps.SupportsSRgb = true;
	caps.SupportsDepthClamp = false;
	caps.SupportsTextureArrays = true;
	caps.SupportsVertexTextures = true;
	caps.SupportsFloatTextures = true;
	caps.SupportsHalfFloatTextures = true;
	caps.SupportsNormalized = true;
	caps.SupportsInstancing = true;
	caps.SupportsBaseIndexInstancing = true;
	caps.SupportsSeparateBlendStates = true;

	// The WebGPU profile of the effect processor (WebGpuShaderProfile): the framework loads only effects built for it.
	caps.ShaderProfile = 81;
}

mgint MGG_GraphicsDevice_GetBackBufferMultiSampleCount(MGG_GraphicsDevice* device)
{
	return 1;
}

mgbyte MGG_GraphicsDevice_ResizeSwapchain(
	MGG_GraphicsDevice* device,
	void* nativeWindowHandle,
	mgint width,
	mgint height,
	MGSurfaceFormat color,
	MGDepthFormat depth,
	mgint multiSampleCount,
	mgint syncInterval)
{
	assert(device != nullptr);
	if (nativeWindowHandle == nullptr || width <= 0 || height <= 0)
	{
		return 0;
	}

	MGWGPU_Flush(device);
	const char* canvas = static_cast<const char*>(nativeWindowHandle);
	if (device->surface == nullptr || device->canvas != canvas)
	{
		if (device->surface != nullptr)
		{
			wgpuSurfaceRelease(device->surface);
		}

		WGPUSurfaceDescriptorFromCanvasHTMLSelector selector = { };
		selector.chain.sType = WGPUSType_SurfaceDescriptorFromCanvasHTMLSelector;
		selector.selector = canvas;
		WGPUSurfaceDescriptor surfaceInfo = { };
		surfaceInfo.nextInChain = &selector.chain;
		device->surface = wgpuInstanceCreateSurface(nullptr, &surfaceInfo);
		device->canvas = canvas;
		device->format = wgpuSurfaceGetPreferredFormat(device->surface, nullptr);
	}

	MGWGPU_ReleaseView(device->frameView);
	if (device->swapchain != nullptr)
	{
		wgpuSwapChainRelease(device->swapchain);
	}

	WGPUSwapChainDescriptor info = { };
	info.usage = WGPUTextureUsage_RenderAttachment;
	info.format = device->format;
	info.width = (uint32_t)width;
	info.height = (uint32_t)height;
	info.presentMode = WGPUPresentMode_Fifo;
	device->swapchain = wgpuDeviceCreateSwapChain(device->device, device->surface, &info);
	device->width = width;
	device->height = height;

	MGWGPU_ReleaseBackBufferDepth(device);
	device->backBufferDepthFormat = depth;
	if (depth != MGDepthFormat::None)
	{
		WGPUTextureDescriptor depthInfo = { };
		depthInfo.usage = WGPUTextureUsage_RenderAttachment;
		depthInfo.dimension = WGPUTextureDimension_2D;
		depthInfo.size = { (uint32_t)width, (uint32_t)height, 1 };
		depthInfo.format = MGWGPU_ToDepthFormat(depth);
		depthInfo.mipLevelCount = 1;
		depthInfo.sampleCount = 1;
		device->backBufferDepth = wgpuDeviceCreateTexture(device->device, &depthInfo);
		device->backBufferDepthView = wgpuTextureCreateView(device->backBufferDepth, nullptr);
	}

	device->passTargetsStale = true;
	return device->swapchain != nullptr ? 1 : 0;
}

mgint MGG_GraphicsDevice_BeginFrame(MGG_GraphicsDevice* device)
{
	return device->frameIndex;
}

void MGG_GraphicsDevice_Clear(MGG_GraphicsDevice* device, MGClearOptions options, Vector4& color, mgfloat depth, mgint stencil)
{
	assert(device != nullptr);
	MGWGPU_EndPass(device);
	device->clearPending = true;
	device->clearOptions = (MGClearOptions)((mgint)device->clearOptions | (mgint)options);
	if (((mgint)options & (mgint)MGClearOptions::Target) != 0)
	{
		device->clearColor = { color.X, color.Y, color.Z, color.W };
	}

	if (((mgint)options & (mgint)MGClearOptions::DepthBuffer) != 0)
	{
		device->clearDepth = depth;
	}

	if (((mgint)options & (mgint)MGClearOptions::Stencil) != 0)
	{
		device->clearStencil = (uint32_t)stencil;
	}
}

void MGG_GraphicsDevice_Present(MGG_GraphicsDevice* device, mgint currentFrame, mgint syncInterval)
{
	assert(device != nullptr);
	MGWGPU_Flush(device);
	device->clearOptions = (MGClearOptions)0;
	MGWGPU_ReleaseView(device->frameView);
	device->frameIndex++;
}

void MGG_GraphicsDevice_SetBlendState(MGG_GraphicsDevice* device, MGG_BlendState* state, mgfloat factorR, mgfloat factorG, mgfloat factorB, mgfloat factorA)
{
	device->blendState = state;
	device->blendFactor = { factorR, factorG, factorB, factorA };
	device->passStateStale = true;
}

void MGG_GraphicsDevice_SetDepthStencilState(MGG_GraphicsDevice* device, MGG_DepthStencilState* state)
{
	device->depthStencilState = state;
	device->passStateStale = true;
}

void MGG_GraphicsDevice_SetRasterizerState(MGG_GraphicsDevice* device, MGG_RasterizerState* state)
{
	device->rasterizerState = state;
	device->passStateStale = true;
}

void MGG_GraphicsDevice_GetTitleSafeArea(mgint& x, mgint& y, mgint& width, mgint& height)
{
}

void MGG_GraphicsDevice_SetViewport(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height, mgfloat minDepth, mgfloat maxDepth)
{
	device->viewport[0] = x;
	device->viewport[1] = y;
	device->viewport[2] = width;
	device->viewport[3] = height;
	device->viewportDepth[0] = minDepth;
	device->viewportDepth[1] = maxDepth;
	device->passStateStale = true;
}

void MGG_GraphicsDevice_SetScissorRectangle(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height)
{
	device->scissor[0] = x;
	device->scissor[1] = y;
	device->scissor[2] = width;
	device->scissor[3] = height;
	device->passStateStale = true;
}

void MGG_GraphicsDevice_SetRenderTargets(MGG_GraphicsDevice* device, MGG_Texture** targets, mgint* arraySlices, mgint count)
{
	MGWGPU_ResolveClear(device);
	MGWGPU_EndPass(device);
	device->clearOptions = (MGClearOptions)0;
	device->targetCount = std::min(count, MGWGPU_MAX_TARGETS);
	for (mgint i = 0; i < MGWGPU_MAX_TARGETS; i++)
	{
		device->targets[i] = i < device->targetCount ? targets[i] : nullptr;
		device->targetSlices[i] = i < device->targetCount && arraySlices != nullptr ? arraySlices[i] : 0;
	}

	device->passTargetsStale = true;
}

static bool MGWGPU_IsStage(MGShaderStage stage)
{
	return (mgint)stage >= 0 && (mgint)stage < MGWGPU_STAGES;
}

void MGG_GraphicsDevice_SetConstantBuffer(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Buffer* buffer)
{
	if (MGWGPU_IsStage(stage))
	{
		device->constantBuffers[(mgint)stage] = buffer;
	}
}

void MGG_GraphicsDevice_SetTexture(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Texture* texture)
{
	if (MGWGPU_IsStage(stage) && slot >= 0 && slot < MGWGPU_MAX_TEXTURE_SLOTS)
	{
		device->textures[(mgint)stage][slot] = texture;
	}
}

void MGG_GraphicsDevice_SetSamplerState(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_SamplerState* state)
{
	if (MGWGPU_IsStage(stage) && slot >= 0 && slot < MGWGPU_MAX_TEXTURE_SLOTS)
	{
		device->samplers[(mgint)stage][slot] = state;
	}
}

void MGG_GraphicsDevice_SetIndexBuffer(MGG_GraphicsDevice* device, MGIndexElementSize size, MGG_Buffer* buffer)
{
	device->indexBuffer = buffer;
	device->indexSize = size;
}

void MGG_GraphicsDevice_SetVertexBuffer(MGG_GraphicsDevice* device, mgint slot, MGG_Buffer* buffer, mgint vertexOffset)
{
	if (slot >= 0 && slot < MGWGPU_MAX_VERTEX_BUFFERS)
	{
		device->vertexBuffers[slot] = buffer;
		device->vertexOffsets[slot] = vertexOffset;
	}
}

void MGG_GraphicsDevice_SetShader(MGG_GraphicsDevice* device, MGShaderStage stage, MGG_Shader* shader)
{
	if (stage == MGShaderStage::Vertex)
	{
		device->vertexShader = shader;
	}
	else if (stage == MGShaderStage::Pixel)
	{
		device->pixelShader = shader;
	}
	else if (stage == MGShaderStage::Compute)
	{
		device->computeShader = shader;
	}
}

void MGG_GraphicsDevice_SetInputLayout(MGG_GraphicsDevice* device, MGG_InputLayout* layout)
{
	device->inputLayout = layout;
}

static uint32_t MGWGPU_IndexCount(MGPrimitiveType primitiveType, mgint primitiveCount)
{
	switch (primitiveType)
	{
	case MGPrimitiveType::LineList:
		return (uint32_t)primitiveCount * 2;
	case MGPrimitiveType::LineStrip:
		return (uint32_t)primitiveCount + 1;
	case MGPrimitiveType::TriangleStrip:
		return (uint32_t)primitiveCount + 2;
	case MGPrimitiveType::PointList:
		return (uint32_t)primitiveCount;
	case MGPrimitiveType::TriangleList:
	default:
		return (uint32_t)primitiveCount * 3;
	}
}

void MGG_GraphicsDevice_Draw(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, mgint vertexStart, mgint vertexCount)
{
	if (vertexCount > 0 && MGWGPU_PrepareDraw(device, primitiveType, false))
	{
		wgpuRenderPassEncoderDraw(device->pass, (uint32_t)vertexCount, 1, (uint32_t)vertexStart, 0);
	}
}

void MGG_GraphicsDevice_DrawIndexed(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, mgint primitiveCount, mgint indexStart, mgint vertexStart)
{
	if (primitiveCount > 0 && MGWGPU_PrepareDraw(device, primitiveType, true))
	{
		wgpuRenderPassEncoderDrawIndexed(device->pass, MGWGPU_IndexCount(primitiveType, primitiveCount), 1, (uint32_t)indexStart, vertexStart, 0);
	}
}

void MGG_GraphicsDevice_DrawIndexedInstanced(
	MGG_GraphicsDevice* device,
	MGPrimitiveType primitiveType,
	mgint primitiveCount,
	mgint indexStart,
	mgint vertexStart,
	mgint baseInstance,
	mgint instanceCount)
{
	if (primitiveCount > 0 && instanceCount > 0 && MGWGPU_PrepareDraw(device, primitiveType, true))
	{
		wgpuRenderPassEncoderDrawIndexed(device->pass, MGWGPU_IndexCount(primitiveType, primitiveCount), (uint32_t)instanceCount, (uint32_t)indexStart,
			vertexStart, (uint32_t)baseInstance);
	}
}

void MGG_GraphicsDevice_ResolveRenderTargets(MGG_GraphicsDevice* device)
{
}

// Reading the canvas back needs an asynchronous buffer map, which a synchronous export cannot wait for in the browser:
// the data comes back zeroed.
void MGG_GraphicsDevice_GetBackBufferData(MGG_GraphicsDevice* device, mgint x, mgint y, mgint width, mgint height, void* data, mgint count, mgint dataBytes)
{
	memset(data, 0, (size_t)dataBytes);
}

MGG_OcclusionQuery* MGG_OcclusionQuery_Create(MGG_GraphicsDevice* device)
{
	return new MGG_OcclusionQuery();
}

void MGG_OcclusionQuery_Destroy(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
	delete query;
}

void MGG_OcclusionQuery_Begin(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
}

void MGG_OcclusionQuery_End(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query)
{
}

mgbyte MGG_OcclusionQuery_GetResult(MGG_GraphicsDevice* device, MGG_OcclusionQuery* query, mgint& pixelCount)
{
	pixelCount = query->pixels;
	return 1;
}

MGG_Texture* MGG_RenderTarget_WrapNativeHandle(
	MGG_GraphicsDevice* device,
	void* nativeHandle,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	MGDepthFormat depthFormat,
	mgint multiSampleCount,
	mgbyte externalPresentation)
{
	return nullptr;
}

MG_EXPORT void MGG_GraphicsDevice_GetNativeHandles(const MGG_GraphicsDevice* device, MGP_NativeGraphicsHandles* handles)
{
	if (device == nullptr || handles == nullptr)
	{
		return;
	}

	handles->Backend = MGGraphicsBackend::WebGPU;
	handles->Instance = nullptr;
	handles->PhysicalDevice = nullptr;
	handles->LogicalDevice = device->device;
	handles->Queue = device->queue;
	handles->QueueFamilyIndex = 0;
	handles->QueueIndex = 0;
}

MG_EXPORT mgint MGG_Etervale_RuntimeVersion()
{
	return MG_ETERVALE_RUNTIME_VERSION;
}

MG_EXPORT mgint MGG_GraphicsDevice_GetPipelineStatistics(MGG_GraphicsDevice* device, mgint compute, char* buffer, mgint capacity)
{
	return -1;
}

// WebGPU timestamp queries need the optional timestamp-query feature, which the device does not request: the pools
// report that they cannot be used, as the Vulkan runtime does on a queue without timestamps.
MG_EXPORT void* MGG_TimestampPool_Create(MGG_GraphicsDevice* device, mguint count)
{
	return nullptr;
}

MG_EXPORT void MGG_TimestampPool_Destroy(MGG_GraphicsDevice* device, void* pool)
{
}

MG_EXPORT mgbool MGG_TimestampPool_Reset(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count)
{
	return false;
}

MG_EXPORT void MGG_TimestampPool_Write(MGG_GraphicsDevice* device, void* pool, mguint index)
{
}

MG_EXPORT mgbool MGG_TimestampPool_Read(MGG_GraphicsDevice* device, void* pool, mguint first, mguint count, mgulong* ticks)
{
	return false;
}

MG_EXPORT mgfloat MGG_TimestampPool_GetPeriod(MGG_GraphicsDevice* device)
{
	return 0.0f;
}

MG_EXPORT mgbool MGG_FrameCapture_Start(MGG_GraphicsDevice* device, mgint downscale, mgint maxQueued)
{
	return false;
}

MG_EXPORT void MGG_FrameCapture_Stop(MGG_GraphicsDevice* device)
{
}

MG_EXPORT mgbool MGG_FrameCapture_Pop(MGG_GraphicsDevice* device, mgbyte* data, mgint dataBytes, mgint* width, mgint* height, mgulong* frame, mgbool* drawn)
{
	return false;
}

MG_EXPORT mgulong MGG_FrameCapture_Dropped(MGG_GraphicsDevice* device)
{
	return 0;
}

// A read-only storage buffer has a t register, a writable one a u register (the binding header's storage buffers).
MG_EXPORT void MGG_GraphicsDevice_SetShaderBuffer(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Buffer* buffer, mgbool writable)
{
	assert(buffer == nullptr || buffer->type == MGBufferType::Storage);
	if (!MGWGPU_IsStage(stage))
	{
		return;
	}

	if (writable && slot >= 0 && slot < MGWGPU_MAX_STORAGE_SLOTS)
	{
		device->storageBuffers[(mgint)stage][slot] = buffer;
	}
	else if (!writable && slot >= 0 && slot < MGWGPU_MAX_TEXTURE_SLOTS)
	{
		device->readBuffers[(mgint)stage][slot] = buffer;
	}
}

// Storage images (RWTexture2D) are not bound yet: no shader of the client writes one, and WGSL declares a storage
// texture's exact format, which an HLSL RWTexture2D<float4> does not give.
MG_EXPORT void MGG_GraphicsDevice_SetShaderImage(MGG_GraphicsDevice* device, MGShaderStage stage, mgint slot, MGG_Texture* texture, mgint mip)
{
}

// A dispatch runs in a compute pass of its own, after the render pass open on the targets (with a pending clear run
// first, since the shader may read them). WebGPU orders the passes' reads and writes, so no barrier is recorded.
WGPUComputePassEncoder MGWGPU_BeginComputePass(MGG_GraphicsDevice* device)
{
	MGWGPU_ResolveClear(device);
	MGWGPU_EndPass(device);
	return wgpuCommandEncoderBeginComputePass(MGWGPU_Encoder(device), nullptr);
}

static void MGWGPU_EndComputePass(WGPUComputePassEncoder pass)
{
	wgpuComputePassEncoderEnd(pass);
	wgpuComputePassEncoderRelease(pass);
}

MG_EXPORT void MGG_GraphicsDevice_Dispatch(MGG_GraphicsDevice* device, mgint groupsX, mgint groupsY, mgint groupsZ)
{
	if (groupsX <= 0 || groupsY <= 0 || groupsZ <= 0)
	{
		return;
	}

	auto pass = MGWGPU_PrepareDispatch(device);
	if (pass != nullptr)
	{
		wgpuComputePassEncoderDispatchWorkgroups(pass, (uint32_t)groupsX, (uint32_t)groupsY, (uint32_t)groupsZ);
		MGWGPU_EndComputePass(pass);
	}
}

// The group counts are three uints at byteOffset in a storage buffer, typically written by an earlier dispatch.
MG_EXPORT void MGG_GraphicsDevice_DispatchIndirect(MGG_GraphicsDevice* device, MGG_Buffer* arguments, mgint byteOffset)
{
	assert(arguments != nullptr && arguments->type == MGBufferType::Storage);
	assert(byteOffset >= 0 && (byteOffset % 4) == 0 && byteOffset + 12 <= arguments->size);
	auto pass = MGWGPU_PrepareDispatch(device);
	if (pass != nullptr)
	{
		arguments->usedSubmission = device->submission;
		wgpuComputePassEncoderDispatchWorkgroupsIndirect(pass, arguments->buffer, (uint64_t)byteOffset);
		MGWGPU_EndComputePass(pass);
	}
}
