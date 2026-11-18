// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the WebGPU backend's resources (monogame/issues/022-webgpu-effects.md): buffers, textures, render
// targets, samplers and the fixed-function states.

#include "MGG_WebGPU.h"

WGPUTextureFormat MGWGPU_ToTextureFormat(MGSurfaceFormat format)
{
	switch (format)
	{
	case MGSurfaceFormat::Color:
		return WGPUTextureFormat_RGBA8Unorm;
	case MGSurfaceFormat::ColorSRgb:
		return WGPUTextureFormat_RGBA8UnormSrgb;
	case MGSurfaceFormat::Bgr32:
	case MGSurfaceFormat::Bgra32:
		return WGPUTextureFormat_BGRA8Unorm;
	case MGSurfaceFormat::Bgr32SRgb:
	case MGSurfaceFormat::Bgra32SRgb:
		return WGPUTextureFormat_BGRA8UnormSrgb;
	case MGSurfaceFormat::Alpha8:
		return WGPUTextureFormat_R8Unorm;
	case MGSurfaceFormat::NormalizedByte2:
		return WGPUTextureFormat_RG8Snorm;
	case MGSurfaceFormat::NormalizedByte4:
		return WGPUTextureFormat_RGBA8Snorm;
	case MGSurfaceFormat::Rgba1010102:
		return WGPUTextureFormat_RGB10A2Unorm;
	case MGSurfaceFormat::Single:
		return WGPUTextureFormat_R32Float;
	case MGSurfaceFormat::Vector2:
		return WGPUTextureFormat_RG32Float;
	case MGSurfaceFormat::Vector4:
		return WGPUTextureFormat_RGBA32Float;
	case MGSurfaceFormat::HalfSingle:
		return WGPUTextureFormat_R16Float;
	case MGSurfaceFormat::HalfVector2:
		return WGPUTextureFormat_RG16Float;
	case MGSurfaceFormat::HalfVector4:
	case MGSurfaceFormat::HdrBlendable:
		return WGPUTextureFormat_RGBA16Float;
	case MGSurfaceFormat::Dxt1:
	case MGSurfaceFormat::Dxt1a:
		return WGPUTextureFormat_BC1RGBAUnorm;
	case MGSurfaceFormat::Dxt1SRgb:
		return WGPUTextureFormat_BC1RGBAUnormSrgb;
	case MGSurfaceFormat::Dxt3:
		return WGPUTextureFormat_BC2RGBAUnorm;
	case MGSurfaceFormat::Dxt3SRgb:
		return WGPUTextureFormat_BC2RGBAUnormSrgb;
	case MGSurfaceFormat::Dxt5:
		return WGPUTextureFormat_BC3RGBAUnorm;
	case MGSurfaceFormat::Dxt5SRgb:
		return WGPUTextureFormat_BC3RGBAUnormSrgb;
	case MGSurfaceFormat::Depth32Float:
		return WGPUTextureFormat_Depth32Float;
	default:
		// 16-bit packed colours, 16-bit unorm and the mobile compressed formats have no core WebGPU format.
		MG_ERROR_PRINT("WebGPU: a surface format without a WebGPU format; using RGBA8.");
		return WGPUTextureFormat_RGBA8Unorm;
	}
}

WGPUTextureFormat MGWGPU_ToDepthFormat(MGDepthFormat format)
{
	switch (format)
	{
	case MGDepthFormat::Depth16:
		return WGPUTextureFormat_Depth16Unorm;
	case MGDepthFormat::Depth24:
		return WGPUTextureFormat_Depth24Plus;
	case MGDepthFormat::Depth24Stencil8:
		return WGPUTextureFormat_Depth24PlusStencil8;
	case MGDepthFormat::Depth32Float:
		return WGPUTextureFormat_Depth32Float;
	case MGDepthFormat::None:
	default:
		return WGPUTextureFormat_Undefined;
	}
}

bool MGWGPU_HasStencil(MGDepthFormat format)
{
	return format == MGDepthFormat::Depth24Stencil8;
}

// The bytes of one block (a texel, or a 4x4 block of a compressed format) and the block's width in texels.
static void MGWGPU_BlockSize(WGPUTextureFormat format, uint32_t& bytes, uint32_t& texels)
{
	texels = 1;
	switch (format)
	{
	case WGPUTextureFormat_R8Unorm:
		bytes = 1;
		break;
	case WGPUTextureFormat_RG8Snorm:
	case WGPUTextureFormat_R16Float:
		bytes = 2;
		break;
	case WGPUTextureFormat_RG16Float:
	case WGPUTextureFormat_R32Float:
	case WGPUTextureFormat_Depth32Float:
	case WGPUTextureFormat_RGB10A2Unorm:
	case WGPUTextureFormat_RGBA8Unorm:
	case WGPUTextureFormat_RGBA8UnormSrgb:
	case WGPUTextureFormat_RGBA8Snorm:
	case WGPUTextureFormat_BGRA8Unorm:
	case WGPUTextureFormat_BGRA8UnormSrgb:
		bytes = 4;
		break;
	case WGPUTextureFormat_RG32Float:
	case WGPUTextureFormat_RGBA16Float:
		bytes = 8;
		break;
	case WGPUTextureFormat_RGBA32Float:
		bytes = 16;
		break;
	case WGPUTextureFormat_BC1RGBAUnorm:
	case WGPUTextureFormat_BC1RGBAUnormSrgb:
		bytes = 8;
		texels = 4;
		break;
	case WGPUTextureFormat_BC2RGBAUnorm:
	case WGPUTextureFormat_BC2RGBAUnormSrgb:
	case WGPUTextureFormat_BC3RGBAUnorm:
	case WGPUTextureFormat_BC3RGBAUnormSrgb:
		bytes = 16;
		texels = 4;
		break;
	default:
		bytes = 4;
		break;
	}
}

void MGWGPU_ForgetBindGroups(MGG_GraphicsDevice* device)
{
	for (auto& entry : device->bindGroups)
	{
		wgpuBindGroupRelease(entry.second);
	}

	device->bindGroups.clear();
}

MGG_BlendState* MGG_BlendState_Create(MGG_GraphicsDevice* device, MGG_BlendState_Info* infos)
{
	auto state = new MGG_BlendState();
	memcpy(state->targets, infos, sizeof(state->targets));
	state->id = device->nextStateId++;
	return state;
}

void MGG_BlendState_Destroy(MGG_GraphicsDevice* device, MGG_BlendState* state)
{
	delete state;
}

MGG_DepthStencilState* MGG_DepthStencilState_Create(MGG_GraphicsDevice* device, MGG_DepthStencilState_Info* info)
{
	return new MGG_DepthStencilState { *info, device->nextStateId++ };
}

void MGG_DepthStencilState_Destroy(MGG_GraphicsDevice* device, MGG_DepthStencilState* state)
{
	delete state;
}

MGG_RasterizerState* MGG_RasterizerState_Create(MGG_GraphicsDevice* device, MGG_RasterizerState_Info* info)
{
	return new MGG_RasterizerState { *info, device->nextStateId++ };
}

void MGG_RasterizerState_Destroy(MGG_GraphicsDevice* device, MGG_RasterizerState* state)
{
	delete state;
}

static WGPUAddressMode MGWGPU_ToAddressMode(MGTextureAddressMode mode)
{
	switch (mode)
	{
	case MGTextureAddressMode::Wrap:
		return WGPUAddressMode_Repeat;
	case MGTextureAddressMode::Mirror:
		return WGPUAddressMode_MirrorRepeat;
	case MGTextureAddressMode::Clamp:
	case MGTextureAddressMode::Border:
	default:
		// WebGPU has no border colour: a border address clamps to the edge.
		return WGPUAddressMode_ClampToEdge;
	}
}

WGPUCompareFunction MGWGPU_ToCompare(MGCompareFunction function)
{
	switch (function)
	{
	case MGCompareFunction::Never:
		return WGPUCompareFunction_Never;
	case MGCompareFunction::Less:
		return WGPUCompareFunction_Less;
	case MGCompareFunction::LessEqual:
		return WGPUCompareFunction_LessEqual;
	case MGCompareFunction::Equal:
		return WGPUCompareFunction_Equal;
	case MGCompareFunction::GreaterEqual:
		return WGPUCompareFunction_GreaterEqual;
	case MGCompareFunction::Greater:
		return WGPUCompareFunction_Greater;
	case MGCompareFunction::NotEqual:
		return WGPUCompareFunction_NotEqual;
	case MGCompareFunction::Always:
	default:
		return WGPUCompareFunction_Always;
	}
}

MGG_SamplerState* MGG_SamplerState_Create(MGG_GraphicsDevice* device, MGG_SamplerState_Info* info)
{
	// Minification, magnification and mip filters of each XNA filter.
	static const WGPUFilterMode L = WGPUFilterMode_Linear;
	static const WGPUFilterMode P = WGPUFilterMode_Nearest;
	WGPUFilterMode min = L;
	WGPUFilterMode mag = L;
	WGPUMipmapFilterMode mip = WGPUMipmapFilterMode_Linear;
	switch (info->Filter)
	{
	case MGTextureFilter::Point:
		min = P;
		mag = P;
		mip = WGPUMipmapFilterMode_Nearest;
		break;
	case MGTextureFilter::LinearMipPoint:
		mip = WGPUMipmapFilterMode_Nearest;
		break;
	case MGTextureFilter::PointMipLinear:
		min = P;
		mag = P;
		break;
	case MGTextureFilter::MinLinearMagPointMipLinear:
		mag = P;
		break;
	case MGTextureFilter::MinLinearMagPointMipPoint:
		mag = P;
		mip = WGPUMipmapFilterMode_Nearest;
		break;
	case MGTextureFilter::MinPointMagLinearMipLinear:
		min = P;
		break;
	case MGTextureFilter::MinPointMagLinearMipPoint:
		min = P;
		mip = WGPUMipmapFilterMode_Nearest;
		break;
	case MGTextureFilter::Linear:
	case MGTextureFilter::Anisotropic:
	default:
		break;
	}

	WGPUSamplerDescriptor descriptor = { };
	descriptor.addressModeU = MGWGPU_ToAddressMode(info->AddressU);
	descriptor.addressModeV = MGWGPU_ToAddressMode(info->AddressV);
	descriptor.addressModeW = MGWGPU_ToAddressMode(info->AddressW);
	descriptor.minFilter = min;
	descriptor.magFilter = mag;
	descriptor.mipmapFilter = mip;
	// XNA's MaxMipLevel is the most detailed level a sampler may use.
	descriptor.lodMinClamp = (float)std::max(info->MaxMipLevel, 0);
	descriptor.lodMaxClamp = 32.0f;
	descriptor.compare = info->FilterMode == MGTextureFilterMode::Comparison ? MGWGPU_ToCompare(info->ComparisonFunction) : WGPUCompareFunction_Undefined;
	// WebGPU allows anisotropy only with linear filters everywhere.
	descriptor.maxAnisotropy = info->Filter == MGTextureFilter::Anisotropic ? (uint16_t)std::clamp(info->MaximumAnisotropy, 1, 16) : 1;
	return new MGG_SamplerState { wgpuDeviceCreateSampler(device->device, &descriptor) };
}

void MGG_SamplerState_Destroy(MGG_GraphicsDevice* device, MGG_SamplerState* state)
{
	MGWGPU_ForgetBindGroups(device);
	wgpuSamplerRelease(state->sampler);
	delete state;
}

MGG_Buffer* MGG_Buffer_Create(MGG_GraphicsDevice* device, MGBufferType type, mgbool dynamic, mgint sizeInBytes)
{
	auto buffer = new MGG_Buffer();
	buffer->type = type;
	buffer->size = sizeInBytes;
	buffer->shadow.assign((size_t)sizeInBytes, 0);
	if (type == MGBufferType::Constant)
	{
		return buffer;
	}

	WGPUBufferDescriptor descriptor = { };
	descriptor.size = ((uint64_t)sizeInBytes + 3) & ~(uint64_t)3;
	descriptor.usage = WGPUBufferUsage_CopyDst | WGPUBufferUsage_CopySrc;
	switch (type)
	{
	case MGBufferType::Index:
		descriptor.usage |= WGPUBufferUsage_Index;
		break;
	case MGBufferType::Vertex:
		descriptor.usage |= WGPUBufferUsage_Vertex;
		break;
	case MGBufferType::Storage:
	default:
		// A storage buffer may also hold a dispatch's group counts (DispatchIndirect).
		descriptor.usage |= WGPUBufferUsage_Storage | WGPUBufferUsage_Indirect;
		break;
	}

	buffer->buffer = wgpuDeviceCreateBuffer(device->device, &descriptor);
	return buffer;
}

void MGG_Buffer_Destroy(MGG_GraphicsDevice* device, MGG_Buffer* buffer)
{
	if (buffer->buffer != nullptr)
	{
		if (buffer->usedSubmission == device->submission)
		{
			MGWGPU_Flush(device);
		}

		MGWGPU_ForgetBindGroups(device);
		wgpuBufferDestroy(buffer->buffer);
		wgpuBufferRelease(buffer->buffer);
	}

	delete buffer;
}

// Copies the written range of the shadow into the GPU buffer. Queue writes are 4-byte aligned in offset and size.
static void MGWGPU_UploadRange(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint first, mgint end, bool noOverwrite)
{
	if (buffer->type == MGBufferType::Constant)
	{
		buffer->ringStale = true;
		return;
	}

	if (!noOverwrite && buffer->usedSubmission == device->submission)
	{
		MGWGPU_Flush(device);
	}

	auto start = (uint64_t)first & ~(uint64_t)3;
	auto stop = std::min(((uint64_t)end + 3) & ~(uint64_t)3, (uint64_t)buffer->shadow.size() & ~(uint64_t)3);
	if (stop > start)
	{
		wgpuQueueWriteBuffer(device->queue, buffer->buffer, start, buffer->shadow.data() + start, (size_t)(stop - start));
	}

	if (stop < (uint64_t)end)
	{
		// The tail of a buffer whose size is not a multiple of 4: written from a padded copy.
		mgbyte tail[4] = { };
		memcpy(tail, buffer->shadow.data() + stop, buffer->shadow.size() - stop);
		wgpuQueueWriteBuffer(device->queue, buffer->buffer, stop, tail, sizeof(tail));
	}
}

static void MGWGPU_WriteShadow(MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint elementCount, mgint vertexStride, mgint elementSizeInBytes, mgint& end)
{
	end = offset;
	if ((size_t)offset >= buffer->shadow.size())
	{
		return;
	}

	if (elementSizeInBytes == vertexStride)
	{
		auto bytes = std::min((size_t)elementCount * (size_t)elementSizeInBytes, buffer->shadow.size() - (size_t)offset);
		memcpy(buffer->shadow.data() + offset, data, bytes);
		end = offset + (mgint)bytes;
		return;
	}

	end = offset;
	for (mgint i = 0; i < elementCount; i++)
	{
		auto at = (size_t)offset + (size_t)i * (size_t)vertexStride;
		if (at + (size_t)elementSizeInBytes > buffer->shadow.size())
		{
			break;
		}

		memcpy(buffer->shadow.data() + at, data + (size_t)i * (size_t)elementSizeInBytes, (size_t)elementSizeInBytes);
		end = (mgint)(at + (size_t)elementSizeInBytes);
	}
}

void MGG_Buffer_SetData(MGG_GraphicsDevice* device, MGG_Buffer*& buffer, mgint offset, mgbyte* data, mgint elementCount, mgint vertexStride, mgint elementSizeInBytes, mgbool discard)
{
	assert(buffer != nullptr && data != nullptr && offset >= 0);
	mgint end = offset;
	MGWGPU_WriteShadow(buffer, offset, data, elementCount, vertexStride, elementSizeInBytes, end);
	MGWGPU_UploadRange(device, buffer, offset, end, false);
}

MG_EXPORT void MGG_Buffer_SetDataNoOverwrite(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint elementCount, mgint vertexStride, mgint elementSizeInBytes)
{
	assert(buffer != nullptr && data != nullptr && offset >= 0);
	mgint end = offset;
	MGWGPU_WriteShadow(buffer, offset, data, elementCount, vertexStride, elementSizeInBytes, end);
	MGWGPU_UploadRange(device, buffer, offset, end, true);
}

// The shadow is the buffer's contents as the CPU wrote them; GPU writes (compute) are not read back.
void MGG_Buffer_GetData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataCount, mgint dataBytes, mgint dataStride)
{
	auto elementBytes = dataCount > 0 ? dataBytes / dataCount : 0;
	for (mgint i = 0; i < dataCount; i++)
	{
		auto at = (size_t)offset + (size_t)i * (size_t)dataStride;
		if (at + (size_t)elementBytes > buffer->shadow.size())
		{
			break;
		}

		memcpy(data + (size_t)i * (size_t)elementBytes, buffer->shadow.data() + at, (size_t)elementBytes);
	}
}

// As SetData: a queue write lands before the commands already recorded, so a buffer they use is submitted first.
MG_EXPORT void MGG_Buffer_SetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataBytes)
{
	MGG_Buffer_SetData(device, buffer, offset, data, 1, dataBytes, dataBytes, false);
}

MG_EXPORT void MGG_Buffer_GetStorageData(MGG_GraphicsDevice* device, MGG_Buffer* buffer, mgint offset, mgbyte* data, mgint dataBytes)
{
	MGG_Buffer_GetData(device, buffer, offset, data, 1, dataBytes, dataBytes);
}

static WGPUTextureViewDimension MGWGPU_ViewDimension(MGTextureType type, mgint slices)
{
	switch (type)
	{
	case MGTextureType::_3D:
		return WGPUTextureViewDimension_3D;
	case MGTextureType::Cube:
		return WGPUTextureViewDimension_Cube;
	case MGTextureType::_2D:
	default:
		return slices > 1 ? WGPUTextureViewDimension_2DArray : WGPUTextureViewDimension_2D;
	}
}

static MGG_Texture* MGWGPU_CreateTexture(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices,
	WGPUTextureUsageFlags usage)
{
	auto texture = new MGG_Texture();
	texture->type = type;
	texture->format = MGWGPU_ToTextureFormat(format);
	texture->width = std::max(width, 1);
	texture->height = std::max(height, 1);
	texture->depth = std::max(depth, 1);
	texture->mipmaps = std::max(mipmaps, 1);
	texture->slices = type == MGTextureType::Cube ? 6 * std::max(slices, 1) : std::max(slices, 1);

	WGPUTextureDescriptor descriptor = { };
	descriptor.usage = usage | WGPUTextureUsage_TextureBinding | WGPUTextureUsage_CopyDst | WGPUTextureUsage_CopySrc;
	descriptor.dimension = type == MGTextureType::_3D ? WGPUTextureDimension_3D : WGPUTextureDimension_2D;
	descriptor.size = { (uint32_t)texture->width, (uint32_t)texture->height, (uint32_t)(type == MGTextureType::_3D ? texture->depth : texture->slices) };
	descriptor.format = texture->format;
	descriptor.mipLevelCount = (uint32_t)texture->mipmaps;
	descriptor.sampleCount = 1;
	texture->texture = wgpuDeviceCreateTexture(device->device, &descriptor);

	WGPUTextureViewDescriptor view = { };
	view.format = texture->format;
	view.dimension = MGWGPU_ViewDimension(type, texture->slices);
	view.mipLevelCount = (uint32_t)texture->mipmaps;
	view.arrayLayerCount = type == MGTextureType::_3D ? 1 : (uint32_t)texture->slices;
	view.aspect = WGPUTextureAspect_All;
	texture->view = wgpuTextureCreateView(texture->texture, &view);
	return texture;
}

MGG_Texture* MGWGPU_CreateFallbackTexture(MGG_GraphicsDevice* device, MGTextureType type)
{
	// Opaque white, the value an unbound texture slot reads.
	auto texture = MGWGPU_CreateTexture(device, type, MGSurfaceFormat::Color, 1, 1, 1, 1, 1, 0);
	const mgbyte white[4] = { 255, 255, 255, 255 };
	for (mgint slice = 0; slice < texture->slices; slice++)
	{
		MGG_Texture_SetData(device, texture, 0, slice, 0, 0, 0, 1, 1, 1, (mgbyte*)white, sizeof(white));
	}

	return texture;
}

MGG_Texture* MGG_Texture_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices)
{
	return MGWGPU_CreateTexture(device, type, format, width, height, depth, mipmaps, slices, 0);
}

MG_EXPORT MGG_Texture* MGG_Texture_CreateStorage(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices)
{
	return MGWGPU_CreateTexture(device, type, format, width, height, depth, mipmaps, slices, WGPUTextureUsage_StorageBinding);
}

MGG_Texture* MGG_RenderTarget_Create(
	MGG_GraphicsDevice* device,
	MGTextureType type,
	MGSurfaceFormat format,
	mgint width,
	mgint height,
	mgint depth,
	mgint mipmaps,
	mgint slices,
	MGDepthFormat depthFormat,
	mgint multiSampleCount,
	MGRenderTargetUsage usage)
{
	auto texture = MGWGPU_CreateTexture(device, type, format, width, height, depth, mipmaps, slices, WGPUTextureUsage_RenderAttachment);

	// One attachment view per array layer (a cube face or an array slice) of the top mip.
	for (mgint layer = 0; layer < texture->slices; layer++)
	{
		WGPUTextureViewDescriptor view = { };
		view.format = texture->format;
		view.dimension = WGPUTextureViewDimension_2D;
		view.baseArrayLayer = (uint32_t)layer;
		view.mipLevelCount = 1;
		view.arrayLayerCount = 1;
		view.aspect = WGPUTextureAspect_All;
		texture->targetViews.push_back(wgpuTextureCreateView(texture->texture, &view));
	}

	texture->depthFormat = depthFormat;
	if (depthFormat != MGDepthFormat::None)
	{
		WGPUTextureDescriptor descriptor = { };
		descriptor.usage = WGPUTextureUsage_RenderAttachment;
		descriptor.dimension = WGPUTextureDimension_2D;
		descriptor.size = { (uint32_t)texture->width, (uint32_t)texture->height, 1 };
		descriptor.format = MGWGPU_ToDepthFormat(depthFormat);
		descriptor.mipLevelCount = 1;
		descriptor.sampleCount = 1;
		texture->depthTexture = wgpuDeviceCreateTexture(device->device, &descriptor);
		texture->depthView = wgpuTextureCreateView(texture->depthTexture, nullptr);
	}

	return texture;
}

void MGG_Texture_Destroy(MGG_GraphicsDevice* device, MGG_Texture* texture)
{
	if (texture == nullptr)
	{
		return;
	}

	MGWGPU_ForgetBindGroups(device);
	for (auto view : texture->targetViews)
	{
		wgpuTextureViewRelease(view);
	}

	if (texture->depthView != nullptr)
	{
		wgpuTextureViewRelease(texture->depthView);
		wgpuTextureDestroy(texture->depthTexture);
		wgpuTextureRelease(texture->depthTexture);
	}

	wgpuTextureViewRelease(texture->view);
	wgpuTextureDestroy(texture->texture);
	wgpuTextureRelease(texture->texture);
	delete texture;
}

void MGG_Texture_SetData(MGG_GraphicsDevice* device, MGG_Texture* texture, mgint level, mgint slice, mgint x, mgint y, mgint z, mgint width, mgint height, mgint depth, mgbyte* data, mgint dataBytes)
{
	if (texture->usedSubmission == device->submission)
	{
		MGWGPU_Flush(device);
	}

	// The framework's convention (as the Vulkan runtime reads it): a 0x0 region is the whole mip level, and a 0 depth of a
	// volume the level's whole depth; a 2D texture's region has one layer.
	if (width == 0 && height == 0)
	{
		width = std::max(texture->width >> level, 1);
		height = std::max(texture->height >> level, 1);
	}

	if (texture->type != MGTextureType::_3D)
	{
		depth = 1;
		z = 0;
	}
	else if (depth == 0)
	{
		depth = std::max(texture->depth >> level, 1);
	}

	uint32_t blockBytes = 4;
	uint32_t blockTexels = 1;
	MGWGPU_BlockSize(texture->format, blockBytes, blockTexels);
	auto blocksWide = ((uint32_t)width + blockTexels - 1) / blockTexels;
	auto blocksHigh = ((uint32_t)height + blockTexels - 1) / blockTexels;

	WGPUImageCopyTexture destination = { };
	destination.texture = texture->texture;
	destination.mipLevel = (uint32_t)level;
	destination.origin = { (uint32_t)x, (uint32_t)y, (uint32_t)(texture->type == MGTextureType::_3D ? z : slice) };
	destination.aspect = WGPUTextureAspect_All;
	WGPUTextureDataLayout layout = { };
	layout.bytesPerRow = blocksWide * blockBytes;
	layout.rowsPerImage = blocksHigh;
	// A compressed copy covers whole blocks, also at a mip level smaller than one block.
	WGPUExtent3D size = { blocksWide * blockTexels, blocksHigh * blockTexels, (uint32_t)std::max(depth, 1) };
	wgpuQueueWriteTexture(device->queue, &destination, data, (size_t)dataBytes, &layout, &size);
}

// Reading a texture back needs an asynchronous buffer map, which a synchronous export cannot wait for in the browser:
// the data comes back zeroed.
void MGG_Texture_GetData(MGG_GraphicsDevice* device, MGG_Texture* texture, mgint level, mgint slice, mgint x, mgint y, mgint z, mgint width, mgint height, mgint depth, mgbyte* data, mgint dataBytes)
{
	memset(data, 0, (size_t)dataBytes);
}
