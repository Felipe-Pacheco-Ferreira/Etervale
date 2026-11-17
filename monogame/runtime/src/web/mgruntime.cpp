// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the browser build of the runtime as one translation unit (monogame/issues/021-webgpu-backend.md).
// The .NET WebAssembly SDK links native code into dotnet.native.wasm and resolves a DllImport by the file name of the
// native file it links, so the framework's DllImport("mgruntime") needs a file named mgruntime: this one, compiled by
// the SDK's Emscripten (NativeFileReference in Client/PristonTale.Browser).

#include "../common/MGI.cpp"
#include "../common/MGM.cpp"
#include "../common/MG_Asset.cpp"
#include "../common/mg_audio.cpp"
#include "../common/mg_hash.cpp"
#include "../common/mg_library.cpp"

// The common sources' decoders define min and max as macros, which would break std::min and std::max below.
#undef min
#undef max

#include "MGA_web.cpp"
#include "MGP_web.cpp"
#include "MG_Storage_web.cpp"

#include "../webgpu/MGG_WebGPU.cpp"
#include "../webgpu/MGG_WebGPU_Pipelines.cpp"
#include "../webgpu/MGG_WebGPU_Resources.cpp"

/* Desktop capture is provided by SDL. Browser clients do not open native microphones. */
extern "C" {
int MGP_Microphone_Count() { return 0; }
const char* MGP_Microphone_Name(int) { return nullptr; }
unsigned int MGP_Microphone_Open(const char*) { return 0; }
void MGP_Microphone_Close(unsigned int) {}
int MGP_Microphone_Read(unsigned int, void*, int) { return 0; }
}
