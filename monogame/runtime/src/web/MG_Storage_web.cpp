// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the XNA storage API of the browser build (monogame/issues/021-webgpu-backend.md), in place of
// common/MG_Storage.cpp, which needs a desktop file system. A page has no user storage device: opening one fails, as it
// does on a desktop without a writable profile, and the game keeps its settings elsewhere.

#include "api_MG_Storage.h"
#include "mg_common.h"

MG_StorageDevice* MG_Storage_OpenDevice(const char* titleName, mgint playerIndex)
{
	return nullptr;
}

void MG_Storage_CloseDevice(MG_StorageDevice* device)
{
}

mglong MG_Storage_GetTotalSpace(MG_StorageDevice* device)
{
	return 0;
}

mglong MG_Storage_GetFreeSpace(MG_StorageDevice* device)
{
	return 0;
}

MG_StorageContainer* MG_Storage_OpenContainer(MG_StorageDevice* device, const char* name, mglong requiredFreeBytes)
{
	return nullptr;
}

mgbool MG_Storage_DeleteContainer(MG_StorageDevice* device, const char* name)
{
	return false;
}

void MG_Storage_CloseContainer(MG_StorageContainer* container)
{
}

void MG_Storage_EnumerateContent(MG_StorageContainer* container, mgbyte**& filesAndDirectories, mgint& size)
{
	filesAndDirectories = nullptr;
	size = 0;
}

mgbyte* MG_Storage_FileLoad(MG_StorageContainer* container, const char* name, mgint& size)
{
	size = 0;
	return nullptr;
}

mgbool MG_Storage_FileSave(MG_StorageContainer* container, const char* name, mgbyte* data, mgint size)
{
	return false;
}

mgbool MG_Storage_FileDelete(MG_StorageContainer* container, const char* name)
{
	return false;
}

mgbool MG_Storage_DirectoryDelete(MG_StorageContainer* container, const char* name)
{
	return false;
}

mgbool MG_Storage_DirectoryCreate(MG_StorageContainer* container, const char* name)
{
	return false;
}

void MG_Storage_CommitContainer(MG_StorageContainer* container)
{
}
