#include "api_MGG.h"
#include <SDL.h>
#include <android/log.h>

using ManagedMain = int (*)();
static ManagedMain managedMain = nullptr;
extern "C" __attribute__((visibility("default"))) void etervale_set_managed_main(ManagedMain callback)
{
    managedMain = callback;
}

int main(int, char**)
{
    if (managedMain)
    {
        return managedMain();
    }
    constexpr const char* tag = "EtervaleVulkanProbe";
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_EVENTS) != 0)
    {
        __android_log_print(ANDROID_LOG_ERROR, tag, "SDL_Init: %s", SDL_GetError());
        return 1;
    }

    auto* window = SDL_CreateWindow("Etervale Vulkan Probe", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
        1280, 720, SDL_WINDOW_VULKAN | SDL_WINDOW_FULLSCREEN_DESKTOP);
    if (!window)
    {
        __android_log_print(ANDROID_LOG_ERROR, tag, "SDL_CreateWindow: %s", SDL_GetError());
        SDL_Quit();
        return 2;
    }

    auto* system = MGG_GraphicsSystem_Create();
    auto* adapter = system ? MGG_GraphicsAdapter_Get(system, 0) : nullptr;
    auto* device = adapter ? MGG_GraphicsDevice_Create(system, adapter) : nullptr;
    if (!device)
    {
        __android_log_print(ANDROID_LOG_ERROR, tag, "The original runtime could not create a Vulkan graphics device.");
        if (system)
        {
            MGG_GraphicsSystem_Destroy(system);
        }
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 3;
    }

    int width = 0, height = 0;
    SDL_GetWindowSize(window, &width, &height);
    MGG_GraphicsDevice_ResizeSwapchain(device, window, width, height, MGSurfaceFormat::Color, MGDepthFormat::None, 0, 1);
    MGG_GraphicsDevice_SetRenderTargets(device, nullptr, nullptr, 0);
    int presented = 0;
    bool quit = false;
    while (!quit && presented < 180)
    {
        SDL_Event event;
        while (SDL_PollEvent(&event))
        {
            quit = quit || event.type == SDL_QUIT || event.type == SDL_APP_WILLENTERBACKGROUND;
        }
        if (quit)
        {
            break;
        }
        const auto frame = MGG_GraphicsDevice_BeginFrame(device);
        if (frame < 0)
        {
            __android_log_print(ANDROID_LOG_ERROR, tag, "BeginFrame failed: %d", frame);
            break;
        }
        Vector4 color{0.08f, 0.24f, 0.36f, 1.f};
        MGG_GraphicsDevice_Clear(device, MGClearOptions::Target, color, 1.f, 0);
        MGG_GraphicsDevice_Present(device, frame, 1);
        if (++presented == 1)
        {
            __android_log_print(ANDROID_LOG_INFO, tag, "Original mgruntime presented its first Vulkan frame (%dx%d).", width, height);
        }
        SDL_Delay(16);
    }

    MGG_GraphicsDevice_Destroy(device);
    MGG_GraphicsSystem_Destroy(system);
    SDL_DestroyWindow(window);
    SDL_Quit();
    return presented > 0 ? 0 : 4;
}
