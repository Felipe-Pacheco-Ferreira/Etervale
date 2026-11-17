// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the WebGPU backend's shaders, layouts, render pipelines and bind groups
// (monogame/issues/022-webgpu-effects.md). A shader's bytecode is the effect processor's WebGPU profile output: the Vulkan
// binding header (uniform count, slot masks, texture types, descriptor bindings), the vertex inputs' locations, then WGSL.
// A pipeline layout is made per vertex and pixel shader pair from both headers, a pipeline per state combination, and
// a bind group per set of bound resources; all are cached.

#include "MGG_WebGPU.h"

#include <algorithm>
#include <cstdio>

// The Vulkan descriptor types of the header (VkDescriptorType).
const uint32_t MGWGPU_VK_SAMPLER = 0;
const uint32_t MGWGPU_VK_COMBINED_IMAGE_SAMPLER = 1;
const uint32_t MGWGPU_VK_SAMPLED_IMAGE = 2;
const uint32_t MGWGPU_VK_STORAGE_BUFFER = 7;
const uint32_t MGWGPU_VK_UNIFORM_BUFFER_DYNAMIC = 8;
const size_t MGWGPU_VK_BINDING_BYTES = 24;

template <typename T>
static T MGWGPU_Read(const mgbyte*& data, mgint& remaining)
{
	T value = { };
	if (remaining >= (mgint)sizeof(T))
	{
		memcpy(&value, data, sizeof(T));
		data += sizeof(T);
		remaining -= (mgint)sizeof(T);
	}

	return value;
}

MGG_Shader* MGG_Shader_Create(MGG_GraphicsDevice* device, MGShaderStage stage, mgbyte* bytecode, mgint sizeInBytes)
{
	auto shader = new MGG_Shader();
	shader->stage = stage;
	shader->id = device->nextStateId++;

	const mgbyte* data = bytecode;
	mgint remaining = sizeInBytes;
	MGWGPU_Read<mgint>(data, remaining);
	MGWGPU_Read<mguint>(data, remaining);
	MGWGPU_Read<mguint>(data, remaining);
	MGWGPU_Read<mguint>(data, remaining);
	MGTextureType textureTypes[MGWGPU_MAX_TEXTURE_SLOTS];
	for (auto& type : textureTypes)
	{
		type = (MGTextureType)MGWGPU_Read<mguint>(data, remaining);
	}

	auto stageBase = stage == MGShaderStage::Pixel ? MGWGPU_BINDING_PIXEL_BASE : 0;
	auto visibility = stage == MGShaderStage::Vertex ? WGPUShaderStage_Vertex : stage == MGShaderStage::Pixel ? WGPUShaderStage_Fragment : WGPUShaderStage_Compute;
	auto bindingCount = MGWGPU_Read<mguint>(data, remaining);
	for (mguint i = 0; i < bindingCount; i++)
	{
		auto binding = MGWGPU_Read<uint32_t>(data, remaining);
		auto type = MGWGPU_Read<uint32_t>(data, remaining);
		data += MGWGPU_VK_BINDING_BYTES - 2 * sizeof(uint32_t);
		remaining -= (mgint)(MGWGPU_VK_BINDING_BYTES - 2 * sizeof(uint32_t));

		auto slot = (mgint)binding - (mgint)MGWGPU_VULKAN_TEXTURE_BINDING;
		auto textureType = slot >= 0 && slot < MGWGPU_MAX_TEXTURE_SLOTS ? textureTypes[slot] : MGTextureType::_2D;
		using Kind = MGWGPU_Binding::Kind;
		switch (type)
		{
		case MGWGPU_VK_UNIFORM_BUFFER_DYNAMIC:
			shader->bindings.push_back({ stageBase, visibility, Kind::Uniform, 0, MGTextureType::_2D });
			break;
		case MGWGPU_VK_COMBINED_IMAGE_SAMPLER:
			shader->bindings.push_back({ stageBase + MGWGPU_BINDING_TEXTURE + (uint32_t)slot, visibility, Kind::Texture, slot, textureType });
			shader->bindings.push_back({ stageBase + MGWGPU_BINDING_SAMPLER + (uint32_t)slot, visibility, Kind::Sampler, slot, textureType });
			break;
		case MGWGPU_VK_SAMPLED_IMAGE:
			shader->bindings.push_back({ stageBase + MGWGPU_BINDING_TEXTURE + (uint32_t)slot, visibility, Kind::Texture, slot, textureType });
			break;
		case MGWGPU_VK_SAMPLER:
			shader->bindings.push_back({ stageBase + MGWGPU_BINDING_SAMPLER + (uint32_t)slot, visibility, Kind::Sampler, slot, textureType });
			break;
		case MGWGPU_VK_STORAGE_BUFFER:
			if (binding >= MGWGPU_VULKAN_UAV_BINDING)
			{
				auto uav = (mgint)(binding - MGWGPU_VULKAN_UAV_BINDING);
				shader->bindings.push_back({ stageBase + MGWGPU_BINDING_STORAGE + (uint32_t)uav, visibility, Kind::Storage, uav, MGTextureType::_2D });
			}
			else
			{
				shader->bindings.push_back({ stageBase + MGWGPU_BINDING_TEXTURE + (uint32_t)slot, visibility, Kind::ReadStorage, slot, MGTextureType::_2D });
			}
			break;
		default:
			MG_ERROR_PRINT("WebGPU: a shader resource type the backend does not bind yet (storage image, acceleration structure).");
			break;
		}
	}

	auto inputCount = MGWGPU_Read<mguint>(data, remaining);
	for (mguint i = 0; i < inputCount; i++)
	{
		MGWGPU_InputLocation input;
		input.usage = (MGVertexElementUsage)MGWGPU_Read<mguint>(data, remaining);
		input.index = MGWGPU_Read<mguint>(data, remaining);
		input.location = MGWGPU_Read<mguint>(data, remaining);
		shader->inputs.push_back(input);
	}

	std::string wgsl((const char*)data, remaining > 0 ? (size_t)remaining : 0);
	WGPUShaderModuleWGSLDescriptor code = { };
	code.chain.sType = WGPUSType_ShaderModuleWGSLDescriptor;
	code.code = wgsl.c_str();
	WGPUShaderModuleDescriptor descriptor = { };
	descriptor.nextInChain = &code.chain;
	shader->module = wgpuDeviceCreateShaderModule(device->device, &descriptor);
	return shader;
}

void MGG_Shader_Destroy(MGG_GraphicsDevice* device, MGG_Shader* shader)
{
	if (device->computeShader == shader)
	{
		device->computeShader = nullptr;
	}

	if (shader->computePipeline != nullptr)
	{
		wgpuComputePipelineRelease(shader->computePipeline);
	}

	if (shader->module != nullptr)
	{
		wgpuShaderModuleRelease(shader->module);
	}

	delete shader;
}

MGG_InputLayout* MGG_InputLayout_Create(
	MGG_GraphicsDevice* device,
	MGG_Shader* vertexShader,
	mgint* strides,
	mgint streamCount,
	MGG_InputElement* elements,
	mgint elementCount)
{
	auto layout = new MGG_InputLayout();
	layout->id = device->nextStateId++;
	layout->strides.assign(strides, strides + streamCount);
	layout->elements.assign(elements, elements + elementCount);
	return layout;
}

void MGG_InputLayout_Destroy(MGG_GraphicsDevice* device, MGG_InputLayout* layout)
{
	delete layout;
}

void MGWGPU_ReleasePipelines(MGG_GraphicsDevice* device)
{
	MGWGPU_ForgetBindGroups(device);
	for (auto& entry : device->pipelines)
	{
		wgpuRenderPipelineRelease(entry.second);
	}

	for (auto& entry : device->layouts)
	{
		wgpuPipelineLayoutRelease(entry.second.pipelineLayout);
		wgpuBindGroupLayoutRelease(entry.second.bindGroupLayout);
	}

	device->pipelines.clear();
	device->layouts.clear();
}

static WGPUTextureViewDimension MGWGPU_BindingDimension(MGTextureType type)
{
	switch (type)
	{
	case MGTextureType::Cube:
		return WGPUTextureViewDimension_Cube;
	case MGTextureType::_3D:
		return WGPUTextureViewDimension_3D;
	case MGTextureType::_2D:
	default:
		return WGPUTextureViewDimension_2D;
	}
}

// The layout of a vertex and pixel shader pair, or of a compute shader alone (pixel null; shader ids start at 1, so its
// key never meets a pair's).
static MGWGPU_Layout& MGWGPU_GetLayout(MGG_GraphicsDevice* device, MGG_Shader* first, MGG_Shader* pixel)
{
	auto key = ((uint64_t)first->id << 32) | (pixel != nullptr ? pixel->id : 0);
	auto found = device->layouts.find(key);
	if (found != device->layouts.end())
	{
		return found->second;
	}

	MGWGPU_Layout layout;
	layout.bindings = first->bindings;
	if (pixel != nullptr)
	{
		layout.bindings.insert(layout.bindings.end(), pixel->bindings.begin(), pixel->bindings.end());
	}

	// Dynamic offsets are given in binding order.
	std::sort(layout.bindings.begin(), layout.bindings.end(), [](const MGWGPU_Binding& a, const MGWGPU_Binding& b) { return a.binding < b.binding; });

	std::vector<WGPUBindGroupLayoutEntry> entries;
	for (auto& binding : layout.bindings)
	{
		WGPUBindGroupLayoutEntry entry = { };
		entry.binding = binding.binding;
		entry.visibility = binding.visibility;
		using Kind = MGWGPU_Binding::Kind;
		switch (binding.kind)
		{
		case Kind::Uniform:
			entry.buffer.type = WGPUBufferBindingType_Uniform;
			entry.buffer.hasDynamicOffset = true;
			break;
		case Kind::Texture:
			entry.texture.sampleType = WGPUTextureSampleType_Float;
			entry.texture.viewDimension = MGWGPU_BindingDimension(binding.textureType);
			break;
		case Kind::Sampler:
			entry.sampler.type = WGPUSamplerBindingType_Filtering;
			break;
		case Kind::ReadStorage:
			entry.buffer.type = WGPUBufferBindingType_ReadOnlyStorage;
			break;
		case Kind::Storage:
			entry.buffer.type = WGPUBufferBindingType_Storage;
			break;
		}

		entries.push_back(entry);
	}

	WGPUBindGroupLayoutDescriptor groupInfo = { };
	groupInfo.entryCount = entries.size();
	groupInfo.entries = entries.data();
	layout.bindGroupLayout = wgpuDeviceCreateBindGroupLayout(device->device, &groupInfo);
	WGPUPipelineLayoutDescriptor pipelineInfo = { };
	pipelineInfo.bindGroupLayoutCount = 1;
	pipelineInfo.bindGroupLayouts = &layout.bindGroupLayout;
	layout.pipelineLayout = wgpuDeviceCreatePipelineLayout(device->device, &pipelineInfo);
	return device->layouts.emplace(key, std::move(layout)).first->second;
}

static WGPUVertexFormat MGWGPU_ToVertexFormat(MGVertexElementFormat format)
{
	switch (format)
	{
	case MGVertexElementFormat::Single:
		return WGPUVertexFormat_Float32;
	case MGVertexElementFormat::Vector2:
		return WGPUVertexFormat_Float32x2;
	case MGVertexElementFormat::Vector3:
		return WGPUVertexFormat_Float32x3;
	case MGVertexElementFormat::Vector4:
		return WGPUVertexFormat_Float32x4;
	case MGVertexElementFormat::Color:
		return WGPUVertexFormat_Unorm8x4;
	case MGVertexElementFormat::Byte4:
		return WGPUVertexFormat_Uint8x4;
	case MGVertexElementFormat::Short2:
		return WGPUVertexFormat_Sint16x2;
	case MGVertexElementFormat::Short4:
		return WGPUVertexFormat_Sint16x4;
	case MGVertexElementFormat::NormalizedShort2:
		return WGPUVertexFormat_Snorm16x2;
	case MGVertexElementFormat::NormalizedShort4:
		return WGPUVertexFormat_Snorm16x4;
	case MGVertexElementFormat::HalfVector2:
		return WGPUVertexFormat_Float16x2;
	case MGVertexElementFormat::HalfVector4:
	default:
		return WGPUVertexFormat_Float16x4;
	}
}

static WGPUBlendFactor MGWGPU_ToBlendFactor(MGBlend blend)
{
	switch (blend)
	{
	case MGBlend::Zero:
		return WGPUBlendFactor_Zero;
	case MGBlend::SourceColor:
		return WGPUBlendFactor_Src;
	case MGBlend::InverseSourceColor:
		return WGPUBlendFactor_OneMinusSrc;
	case MGBlend::SourceAlpha:
		return WGPUBlendFactor_SrcAlpha;
	case MGBlend::InverseSourceAlpha:
		return WGPUBlendFactor_OneMinusSrcAlpha;
	case MGBlend::DestinationColor:
		return WGPUBlendFactor_Dst;
	case MGBlend::InverseDestinationColor:
		return WGPUBlendFactor_OneMinusDst;
	case MGBlend::DestinationAlpha:
		return WGPUBlendFactor_DstAlpha;
	case MGBlend::InverseDestinationAlpha:
		return WGPUBlendFactor_OneMinusDstAlpha;
	case MGBlend::BlendFactor:
		return WGPUBlendFactor_Constant;
	case MGBlend::InverseBlendFactor:
		return WGPUBlendFactor_OneMinusConstant;
	case MGBlend::SourceAlphaSaturation:
		return WGPUBlendFactor_SrcAlphaSaturated;
	case MGBlend::One:
	default:
		return WGPUBlendFactor_One;
	}
}

static WGPUBlendOperation MGWGPU_ToBlendOperation(MGBlendFunction function)
{
	switch (function)
	{
	case MGBlendFunction::Subtract:
		return WGPUBlendOperation_Subtract;
	case MGBlendFunction::ReverseSubtract:
		return WGPUBlendOperation_ReverseSubtract;
	case MGBlendFunction::Min:
		return WGPUBlendOperation_Min;
	case MGBlendFunction::Max:
		return WGPUBlendOperation_Max;
	case MGBlendFunction::Add:
	default:
		return WGPUBlendOperation_Add;
	}
}

static WGPUStencilOperation MGWGPU_ToStencilOperation(MGStencilOperation operation)
{
	switch (operation)
	{
	case MGStencilOperation::Zero:
		return WGPUStencilOperation_Zero;
	case MGStencilOperation::Replace:
		return WGPUStencilOperation_Replace;
	case MGStencilOperation::Increment:
		return WGPUStencilOperation_IncrementWrap;
	case MGStencilOperation::Decrement:
		return WGPUStencilOperation_DecrementWrap;
	case MGStencilOperation::IncrementSaturation:
		return WGPUStencilOperation_IncrementClamp;
	case MGStencilOperation::DecrementSaturation:
		return WGPUStencilOperation_DecrementClamp;
	case MGStencilOperation::Invert:
		return WGPUStencilOperation_Invert;
	case MGStencilOperation::Keep:
	default:
		return WGPUStencilOperation_Keep;
	}
}

static WGPUPrimitiveTopology MGWGPU_ToTopology(MGPrimitiveType type)
{
	switch (type)
	{
	case MGPrimitiveType::TriangleStrip:
		return WGPUPrimitiveTopology_TriangleStrip;
	case MGPrimitiveType::LineList:
		return WGPUPrimitiveTopology_LineList;
	case MGPrimitiveType::LineStrip:
		return WGPUPrimitiveTopology_LineStrip;
	case MGPrimitiveType::PointList:
		return WGPUPrimitiveTopology_PointList;
	case MGPrimitiveType::TriangleList:
	default:
		return WGPUPrimitiveTopology_TriangleList;
	}
}

// The formats of the bound color attachments and depth buffer.
static void MGWGPU_TargetFormats(const MGG_GraphicsDevice* device, WGPUTextureFormat* colors, mgint& colorCount, MGDepthFormat& depth)
{
	if (device->targetCount == 0)
	{
		colors[0] = device->format;
		colorCount = 1;
		depth = device->backBufferDepthFormat;
		return;
	}

	if (device->targetCount == 1 && device->targets[0]->format == WGPUTextureFormat_Depth32Float)
	{
		colorCount = 0;
		depth = MGDepthFormat::Depth32Float;
		return;
	}

	for (mgint i = 0; i < device->targetCount; i++)
	{
		colors[i] = device->targets[i]->format;
	}

	colorCount = device->targetCount;
	depth = device->targets[0]->depthFormat;
}

static void MGWGPU_StencilFace(WGPUStencilFaceState& face, MGCompareFunction function, MGStencilOperation fail, MGStencilOperation depthFail, MGStencilOperation pass)
{
	face.compare = MGWGPU_ToCompare(function);
	face.failOp = MGWGPU_ToStencilOperation(fail);
	face.depthFailOp = MGWGPU_ToStencilOperation(depthFail);
	face.passOp = MGWGPU_ToStencilOperation(pass);
}

static WGPURenderPipeline MGWGPU_GetPipeline(MGG_GraphicsDevice* device, MGWGPU_Layout& layout, MGPrimitiveType primitiveType, bool indexed)
{
	WGPUTextureFormat colorFormats[MGWGPU_MAX_TARGETS] = { };
	mgint colorCount = 0;
	MGDepthFormat depthFormat = MGDepthFormat::None;
	MGWGPU_TargetFormats(device, colorFormats, colorCount, depthFormat);

	auto topology = MGWGPU_ToTopology(primitiveType);
	auto strip = topology == WGPUPrimitiveTopology_TriangleStrip || topology == WGPUPrimitiveTopology_LineStrip;
	auto stripFormat = !strip || !indexed ? WGPUIndexFormat_Undefined
		: device->indexSize == MGIndexElementSize::ThirtyTwoBits ? WGPUIndexFormat_Uint32 : WGPUIndexFormat_Uint16;
	auto blend = device->blendState;
	auto depthState = depthFormat != MGDepthFormat::None ? device->depthStencilState : nullptr;
	auto raster = device->rasterizerState;

	uint32_t key[11 + MGWGPU_MAX_TARGETS] = {
		device->vertexShader->id, device->pixelShader->id, device->inputLayout->id, blend != nullptr ? blend->id : 0,
		depthState != nullptr ? depthState->id : 0, raster != nullptr ? raster->id : 0, (uint32_t)topology, (uint32_t)stripFormat,
		(uint32_t)depthFormat, (uint32_t)colorCount, 0 };
	for (mgint i = 0; i < colorCount; i++)
	{
		key[11 + i] = (uint32_t)colorFormats[i];
	}

	auto& keyBytes = device->pipelineKey;
	keyBytes.assign((const char*)key, sizeof(key));
	auto found = device->pipelines.find(keyBytes);
	if (found != device->pipelines.end())
	{
		return found->second;
	}

	// One vertex buffer layout per stream; an element the shader does not read has no attribute.
	auto input = device->inputLayout;
	std::vector<std::vector<WGPUVertexAttribute>> attributes(input->strides.size());
	std::vector<WGPUVertexStepMode> steps(input->strides.size(), WGPUVertexStepMode_Vertex);
	for (auto& element : input->elements)
	{
		if (element.VertexBufferSlot >= attributes.size())
		{
			continue;
		}

		for (auto& location : device->vertexShader->inputs)
		{
			if (location.usage == element.SemanticUsage && location.index == element.SemanticIndex)
			{
				attributes[element.VertexBufferSlot].push_back({ MGWGPU_ToVertexFormat(element.Format), element.AlignedByteOffset, location.location });
				if (element.InstanceDataStepRate > 0)
				{
					steps[element.VertexBufferSlot] = WGPUVertexStepMode_Instance;
				}
			}
		}
	}

	for (const auto& location : device->vertexShader->inputs)
	{
		bool matched = false;
		for (const auto& element : input->elements)
		{
			matched |= element.SemanticUsage == location.usage && element.SemanticIndex == location.index;
		}
		if (!matched)
		{
			char message[160];
			std::snprintf(message, sizeof(message), "WebGPU vertex semantic missing: shader=%u location=%u usage=%u index=%u.",
				device->vertexShader->id, (unsigned)location.location, (unsigned)location.usage, (unsigned)location.index);
			MG_ERROR_PRINT(message);
		}
	}

	std::vector<WGPUVertexBufferLayout> buffers(input->strides.size());
	for (size_t i = 0; i < buffers.size(); i++)
	{
		buffers[i].arrayStride = (uint64_t)input->strides[i];
		buffers[i].stepMode = attributes[i].empty() ? WGPUVertexStepMode_VertexBufferNotUsed : steps[i];
		buffers[i].attributeCount = attributes[i].size();
		buffers[i].attributes = attributes[i].data();
	}

	WGPUBlendState blends[MGWGPU_MAX_TARGETS] = { };
	WGPUColorTargetState targets[MGWGPU_MAX_TARGETS] = { };
	for (mgint i = 0; i < colorCount; i++)
	{
		MGG_BlendState_Info info = { MGBlend::One, MGBlend::Zero, MGBlendFunction::Add, MGBlend::One, MGBlend::Zero, MGBlendFunction::Add, MGColorWriteChannels::All };
		if (blend != nullptr)
		{
			info = blend->targets[i];
		}

		targets[i].format = colorFormats[i];
		targets[i].writeMask = (WGPUColorWriteMaskFlags)info.colorWriteChannels;
		auto opaque = info.colorSourceBlend == MGBlend::One && info.colorDestBlend == MGBlend::Zero && info.colorBlendFunc == MGBlendFunction::Add
			&& info.alphaSourceBlend == MGBlend::One && info.alphaDestBlend == MGBlend::Zero && info.alphaBlendFunc == MGBlendFunction::Add;
		if (!opaque)
		{
			blends[i].color = { MGWGPU_ToBlendOperation(info.colorBlendFunc), MGWGPU_ToBlendFactor(info.colorSourceBlend), MGWGPU_ToBlendFactor(info.colorDestBlend) };
			blends[i].alpha = { MGWGPU_ToBlendOperation(info.alphaBlendFunc), MGWGPU_ToBlendFactor(info.alphaSourceBlend), MGWGPU_ToBlendFactor(info.alphaDestBlend) };
			targets[i].blend = &blends[i];
		}
	}

	WGPUFragmentState fragment = { };
	fragment.module = device->pixelShader->module;
	fragment.entryPoint = "main";
	fragment.targetCount = (size_t)colorCount;
	fragment.targets = targets;

	std::string label = "vertex inputs";
	for (const auto& location : device->vertexShader->inputs)
		label += " " + std::to_string(location.location) + ":" + std::to_string((unsigned)location.usage) + "/" + std::to_string(location.index);
	label += " ->";
	for (const auto& stream : attributes)
		for (const auto& attribute : stream)
			label += " " + std::to_string(attribute.shaderLocation);
	WGPURenderPipelineDescriptor descriptor = { };
	descriptor.label = label.c_str();
	descriptor.layout = layout.pipelineLayout;
	descriptor.vertex.module = device->vertexShader->module;
	descriptor.vertex.entryPoint = "main";
	descriptor.vertex.bufferCount = buffers.size();
	descriptor.vertex.buffers = buffers.data();
	descriptor.primitive.topology = topology;
	descriptor.primitive.stripIndexFormat = stripFormat;
	// XNA culls by the winding of the faces on screen, clockwise faces being the front ones; WebGPU's framebuffer has
	// Direct3D's orientation, so the winding carries over.
	descriptor.primitive.frontFace = WGPUFrontFace_CW;
	descriptor.primitive.cullMode = raster == nullptr ? WGPUCullMode_Back
		: raster->info.cullMode == MGCullMode::None ? WGPUCullMode_None
		: raster->info.cullMode == MGCullMode::CullClockwiseFace ? WGPUCullMode_Front : WGPUCullMode_Back;
	descriptor.multisample.count = 1;
	descriptor.multisample.mask = 0xFFFFFFFF;
	descriptor.fragment = &fragment;

	WGPUDepthStencilState depth = { };
	if (depthFormat != MGDepthFormat::None)
	{
		depth.format = MGWGPU_ToDepthFormat(depthFormat);
		depth.depthCompare = WGPUCompareFunction_Always;
		depth.stencilFront = { WGPUCompareFunction_Always, WGPUStencilOperation_Keep, WGPUStencilOperation_Keep, WGPUStencilOperation_Keep };
		depth.stencilBack = depth.stencilFront;
		depth.stencilReadMask = 0xFF;
		depth.stencilWriteMask = 0xFF;
		if (depthState != nullptr)
		{
			auto& info = depthState->info;
			depth.depthWriteEnabled = info.depthBufferEnable && info.depthBufferWriteEnable;
			depth.depthCompare = info.depthBufferEnable ? MGWGPU_ToCompare(info.depthBufferFunction) : WGPUCompareFunction_Always;
			if (info.stencilEnable && MGWGPU_HasStencil(depthFormat))
			{
				MGWGPU_StencilFace(depth.stencilFront, info.stencilFunction, info.stencilFail, info.stencilDepthBufferFail, info.stencilPass);
				depth.stencilBack = depth.stencilFront;
				if (info.twoSidedStencilMode)
				{
					MGWGPU_StencilFace(depth.stencilBack, info.counterClockwiseStencilFunction, info.counterClockwiseStencilFail,
						info.counterClockwiseStencilDepthBufferFail, info.counterClockwiseStencilPass);
				}

				depth.stencilReadMask = (uint32_t)info.stencilMask & 0xFF;
				depth.stencilWriteMask = (uint32_t)info.stencilWriteMask & 0xFF;
			}
		}

		if (raster != nullptr)
		{
			// XNA's depth bias is in units of the depth buffer's range; WebGPU's is in units of its least significant bit.
			auto bits = depthFormat == MGDepthFormat::Depth16 ? 16 : 24;
			depth.depthBias = (int32_t)(raster->info.depthBias * (float)(1 << bits));
			depth.depthBiasSlopeScale = raster->info.slopeScaleDepthBias;
		}

		descriptor.depthStencil = &depth;
	}

	auto pipeline = wgpuDeviceCreateRenderPipeline(device->device, &descriptor);
	device->pipelines.emplace(keyBytes, pipeline);
	return pipeline;
}

static uint32_t MGWGPU_AlignUniform(uint32_t size)
{
	return (size + MGWGPU_UNIFORM_ALIGNMENT - 1) & ~(MGWGPU_UNIFORM_ALIGNMENT - 1);
}

// The ring offset of a constant buffer's contents in this submission, copying them in when they changed.
static uint32_t MGWGPU_UniformOffset(MGG_GraphicsDevice* device, MGG_Buffer* buffer)
{
	if (buffer->ringStale || buffer->ringSubmission != device->submission)
	{
		buffer->ringOffset = device->uniformRingOffset;
		buffer->ringSubmission = device->submission;
		buffer->ringStale = false;
		wgpuQueueWriteBuffer(device->queue, device->uniformRing, buffer->ringOffset, buffer->shadow.data(), buffer->shadow.size());
		device->uniformRingOffset += MGWGPU_AlignUniform((uint32_t)buffer->shadow.size());
	}

	return buffer->ringOffset;
}

static MGG_Texture* MGWGPU_FallbackTexture(MGG_GraphicsDevice* device, MGTextureType type)
{
	switch (type)
	{
	case MGTextureType::Cube:
		return device->fallbackCube;
	case MGTextureType::_3D:
		return device->fallbackVolume;
	case MGTextureType::_2D:
	default:
		return device->fallbackTexture;
	}
}

static void MGWGPU_SetPassState(MGG_GraphicsDevice* device)
{
	mgint width = 0;
	mgint height = 0;
	MGWGPU_TargetSize(device, width, height);

	// WebGPU refuses a viewport or scissor outside the attachment, which XNA allows: both are clipped to it.
	auto x = std::clamp(device->viewport[0], 0, width);
	auto y = std::clamp(device->viewport[1], 0, height);
	auto w = std::clamp(device->viewport[0] + device->viewport[2], 0, width) - x;
	auto h = std::clamp(device->viewport[1] + device->viewport[3], 0, height) - y;
	if (w <= 0 || h <= 0)
	{
		x = 0;
		y = 0;
		w = width;
		h = height;
	}

	wgpuRenderPassEncoderSetViewport(device->pass, (float)x, (float)y, (float)w, (float)h, device->viewportDepth[0], device->viewportDepth[1]);

	auto scissorEnabled = device->rasterizerState != nullptr && device->rasterizerState->info.scissorTestEnable;
	auto sx = scissorEnabled ? std::clamp(device->scissor[0], 0, width) : 0;
	auto sy = scissorEnabled ? std::clamp(device->scissor[1], 0, height) : 0;
	auto sw = scissorEnabled ? std::clamp(device->scissor[0] + device->scissor[2], 0, width) - sx : width;
	auto sh = scissorEnabled ? std::clamp(device->scissor[1] + device->scissor[3], 0, height) - sy : height;
	wgpuRenderPassEncoderSetScissorRect(device->pass, (uint32_t)sx, (uint32_t)sy, (uint32_t)std::max(sw, 0), (uint32_t)std::max(sh, 0));
	wgpuRenderPassEncoderSetBlendConstant(device->pass, &device->blendFactor);
	auto reference = device->depthStencilState != nullptr ? (uint32_t)device->depthStencilState->info.referenceStencil : 0;
	wgpuRenderPassEncoderSetStencilReference(device->pass, reference);
	device->passStateStale = false;
}

// The bound storage buffer of a binding, stamped as read by the recorded commands.
static MGG_Buffer* MGWGPU_StorageBuffer(MGG_GraphicsDevice* device, const MGWGPU_Binding& binding, int stage)
{
	auto buffer = binding.kind == MGWGPU_Binding::Kind::Storage ? device->storageBuffers[stage][binding.slot] : device->readBuffers[stage][binding.slot];
	if (buffer != nullptr)
	{
		buffer->usedSubmission = device->submission;
	}

	return buffer;
}

// The bind group of the bound resources for a draw (vertex and pixel stages, told apart by their bindings) or a
// dispatch (the compute stage), with the uniforms' ring offsets in device->dynamicOffsets; null when a constant or
// storage buffer the shaders use is not bound. A texture slot left empty reads a white fallback.
static WGPUBindGroup MGWGPU_BindGroup(MGG_GraphicsDevice* device, const MGWGPU_Layout& layout, bool compute)
{
	auto& entries = device->bindGroupEntries;
	auto& offsets = device->dynamicOffsets;
	auto& key = device->bindGroupKey;
	entries.clear();
	offsets.clear();
	key.assign((const char*)&layout.bindGroupLayout, sizeof(layout.bindGroupLayout));
	using Kind = MGWGPU_Binding::Kind;
	for (auto& binding : layout.bindings)
	{
		auto stage = compute ? (int)MGShaderStage::Compute : binding.binding >= MGWGPU_BINDING_PIXEL_BASE ? (int)MGShaderStage::Pixel : (int)MGShaderStage::Vertex;
		WGPUBindGroupEntry entry = { };
		entry.binding = binding.binding;
		switch (binding.kind)
		{
		case Kind::Uniform:
		{
			auto buffer = device->constantBuffers[stage];
			if (buffer == nullptr)
			{
				return nullptr;
			}

			entry.buffer = device->uniformRing;
			// WGSL rounds a uniform struct up to 16 bytes; the ring's 256-byte slots cover the rounding.
			entry.size = ((uint64_t)buffer->shadow.size() + 15) & ~(uint64_t)15;
			offsets.push_back(MGWGPU_UniformOffset(device, buffer));
			key.append((const char*)&entry.size, sizeof(entry.size));
			break;
		}
		case Kind::Texture:
		{
			auto texture = device->textures[stage][binding.slot];
			if (texture == nullptr)
			{
				texture = MGWGPU_FallbackTexture(device, binding.textureType);
			}

			texture->usedSubmission = device->submission;
			entry.textureView = texture->view;
			key.append((const char*)&entry.textureView, sizeof(entry.textureView));
			break;
		}
		case Kind::Sampler:
		{
			auto sampler = device->samplers[stage][binding.slot];
			entry.sampler = sampler != nullptr ? sampler->sampler : device->fallbackSampler;
			key.append((const char*)&entry.sampler, sizeof(entry.sampler));
			break;
		}
		case Kind::ReadStorage:
		case Kind::Storage:
		{
			auto buffer = MGWGPU_StorageBuffer(device, binding, stage);
			if (buffer == nullptr)
			{
				return nullptr;
			}

			entry.buffer = buffer->buffer;
			entry.size = WGPU_WHOLE_SIZE;
			key.append((const char*)&entry.buffer, sizeof(entry.buffer));
			break;
		}
		}

		entries.push_back(entry);
	}

	auto found = device->bindGroups.find(key);
	if (found != device->bindGroups.end())
	{
		return found->second;
	}

	WGPUBindGroupDescriptor descriptor = { };
	descriptor.layout = layout.bindGroupLayout;
	descriptor.entryCount = entries.size();
	descriptor.entries = entries.data();
	auto group = wgpuDeviceCreateBindGroup(device->device, &descriptor);
	device->bindGroups.emplace(key, group);
	return group;
}

// The bound stages' constants must fit in the ring; otherwise the recorded commands are submitted and the ring restarts.
static void MGWGPU_ReserveUniforms(MGG_GraphicsDevice* device, bool compute)
{
	uint32_t uniformBytes = 0;
	for (int stage = 0; stage < MGWGPU_STAGES; stage++)
	{
		auto buffer = device->constantBuffers[stage];
		if (buffer != nullptr && (stage == (int)MGShaderStage::Compute) == compute)
		{
			uniformBytes += MGWGPU_AlignUniform((uint32_t)buffer->shadow.size());
		}
	}

	if (device->uniformRingOffset + uniformBytes > MGWGPU_UNIFORM_RING_SIZE)
	{
		MGWGPU_Flush(device);
	}
}

// Opens a compute pass with the bound compute shader's pipeline and resources, or returns null (and skips the dispatch)
// when no compute shader or one of its buffers is bound.
WGPUComputePassEncoder MGWGPU_PrepareDispatch(MGG_GraphicsDevice* device)
{
	auto shader = device->computeShader;
	if (shader == nullptr || shader->stage != MGShaderStage::Compute)
	{
		MG_ERROR_PRINT("WebGPU: a dispatch without a compute shader is skipped.");
		return nullptr;
	}

	MGWGPU_ReserveUniforms(device, true);
	auto& layout = MGWGPU_GetLayout(device, shader, nullptr);
	if (shader->computePipeline == nullptr)
	{
		WGPUComputePipelineDescriptor descriptor = { };
		descriptor.layout = layout.pipelineLayout;
		descriptor.compute.module = shader->module;
		descriptor.compute.entryPoint = "main";
		shader->computePipeline = wgpuDeviceCreateComputePipeline(device->device, &descriptor);
	}

	auto group = MGWGPU_BindGroup(device, layout, true);
	if (group == nullptr)
	{
		MG_ERROR_PRINT("WebGPU: a dispatch with a constant or storage buffer of its shader unbound is skipped.");
		return nullptr;
	}

	auto pass = MGWGPU_BeginComputePass(device);
	wgpuComputePassEncoderSetPipeline(pass, shader->computePipeline);
	wgpuComputePassEncoderSetBindGroup(pass, 0, group, device->dynamicOffsets.size(), device->dynamicOffsets.data());
	return pass;
}

bool MGWGPU_PrepareDraw(MGG_GraphicsDevice* device, MGPrimitiveType primitiveType, bool indexed)
{
	if (device->vertexShader == nullptr || device->pixelShader == nullptr || device->inputLayout == nullptr || (indexed && device->indexBuffer == nullptr))
	{
		return false;
	}

	MGWGPU_ReserveUniforms(device, false);

	if (!MGWGPU_BeginPass(device))
	{
		return false;
	}

	auto& layout = MGWGPU_GetLayout(device, device->vertexShader, device->pixelShader);
	auto pipeline = MGWGPU_GetPipeline(device, layout, primitiveType, indexed);
	auto group = MGWGPU_BindGroup(device, layout, false);
	if (group == nullptr)
	{
		return false;
	}

	wgpuRenderPassEncoderSetPipeline(device->pass, pipeline);
	wgpuRenderPassEncoderSetBindGroup(device->pass, 0, group, device->dynamicOffsets.size(), device->dynamicOffsets.data());
	for (auto& element : device->inputLayout->elements)
	{
		auto slot = element.VertexBufferSlot;
		auto buffer = slot < MGWGPU_MAX_VERTEX_BUFFERS ? device->vertexBuffers[slot] : nullptr;
		if (buffer == nullptr)
		{
			return false;
		}

		buffer->usedSubmission = device->submission;
		wgpuRenderPassEncoderSetVertexBuffer(device->pass, slot, buffer->buffer, (uint64_t)device->vertexOffsets[slot], WGPU_WHOLE_SIZE);
	}

	if (indexed)
	{
		auto format = device->indexSize == MGIndexElementSize::ThirtyTwoBits ? WGPUIndexFormat_Uint32 : WGPUIndexFormat_Uint16;
		device->indexBuffer->usedSubmission = device->submission;
		wgpuRenderPassEncoderSetIndexBuffer(device->pass, device->indexBuffer->buffer, format, 0, WGPU_WHOLE_SIZE);
	}

	if (device->passStateStale)
	{
		MGWGPU_SetPassState(device);
	}

	return true;
}
