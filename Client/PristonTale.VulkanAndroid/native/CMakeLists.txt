cmake_minimum_required(VERSION 3.22)
project(EtervaleAndroidRuntime LANGUAGES C CXX)

if(NOT ANDROID)
    message(FATAL_ERROR "Configure this target with the Android NDK toolchain.")
endif()

get_filename_component(workspace "../../.." ABSOLUTE BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}")
set(runtime "${workspace}/monogame/runtime/src")
set(dependencies "${runtime}/external")
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
add_link_options("-Wl,-z,max-page-size=16384")

set(SDL_SHARED ON CACHE BOOL "" FORCE)
set(SDL_STATIC OFF CACHE BOOL "" FORCE)
set(SDL_TEST OFF CACHE BOOL "" FORCE)
add_subdirectory("${dependencies}/sdl2/sdl" sdl2)

set(BUILD_SDL3 OFF CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(FAUDIO_INSTALL OFF CACHE BOOL "" FORCE)
add_subdirectory("${dependencies}/faudio" faudio)

add_library(mgruntime SHARED
    "${runtime}/common/MG_Asset.cpp"
    "${runtime}/common/MG_Storage.cpp"
    "${runtime}/common/mg_audio.cpp"
    "${runtime}/common/mg_hash.cpp"
    "${runtime}/common/mg_library.cpp"
    "${runtime}/common/MGI.cpp"
    "${runtime}/common/MGM.cpp"
    "${runtime}/sdl/MGP_sdl.cpp"
    "${runtime}/vulkan/MGG_Vulkan.cpp"
    "${runtime}/faudio/MGA_faudio.cpp")
target_compile_features(mgruntime PRIVATE cxx_std_17)
target_compile_definitions(mgruntime PRIVATE DLL_EXPORT MG_SDL2 MG_VULKAN MG_FAUDIO)
target_include_directories(mgruntime PRIVATE
    "${runtime}/include"
    "${runtime}/external/stb"
    "${runtime}/external/volk"
    "${runtime}/external/vma/include"
    "${runtime}/external/vulkan-headers/include"
    "${dependencies}/faudio/include")
target_link_libraries(mgruntime PRIVATE SDL2::SDL2 FAudio-static android log dl)

add_library(main SHARED probe.cpp)
target_compile_features(main PRIVATE cxx_std_17)
target_include_directories(main PRIVATE "${runtime}/include")
target_link_libraries(main PRIVATE mgruntime SDL2::SDL2 log)
