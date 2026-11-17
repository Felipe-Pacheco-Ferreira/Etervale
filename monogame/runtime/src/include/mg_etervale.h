// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

#pragma once

// Etervale fork: the version of the runtime's exported interface, which the fork's framework (monogame/framework,
// MGG.EnsureRuntimeVersion) checks before anything else: a library built from other sources (the official package, or
// a stale build of the fork) would fail later at the first missing export, or worse, call an export whose arguments
// changed. Bumped with every change of an exported function (monogame/issues/002-runtime-version.md). Every graphics
// backend of the fork (vulkan/, webgpu/) implements the same exports and returns this one version.
#define MG_ETERVALE_RUNTIME_VERSION 7
