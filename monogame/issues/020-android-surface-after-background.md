# 020. Android: no frame without a swapchain, and a new surface after the background

Part: runtime.

## Symptom

On Android, sending the game to the background, or bringing it back, ended the process: a native crash on the SDL
thread, in `MGVK_UpdateRenderPass` from `MGG_GraphicsDevice_Clear` (a null target set) or in `vkQueueSubmit` inside the
GPU driver from `MGG_GraphicsDevice_Present` (seen on an Adreno phone, Android 14).

## Cause

Android takes the activity's window away when it goes to the background and gives it a new one when it comes back.
Going away, a present finds the swapchain out of date and cannot make a new one (the window is going), so the runtime is
left without a swapchain and binds no target; the next frame still ran, because SDL reports the window minimized only
later, and its clear dereferenced the missing target set. Coming back, SDL keeps the same `SDL_Window`, so the runtime
kept the `VkSurfaceKHR` made for the old window, its swapchain and the image the frame begun before the pause acquired
from it, and submitting to them crashed the driver.

## Change

`sdl/MGP_sdl.cpp`: on Android with Vulkan, `SDL_APP_DIDENTERFOREGROUND` marks the surface stale, and
`MGP_Platform_BeforeDraw` asks `MGVK_AndroidSurfaceReady` before every frame, skipping the frame when it says no.
`vulkan/MGG_Vulkan.cpp`: `MGVK_AndroidSurfaceReady` says no while SDL holds no Android window; without a swapchain, or
when stale, it waits for the device, drops the frame's recorded commands unsubmitted, destroys the surface, makes the
surface and the swapchain again for the window (`MGVK_RecreateSwapChain` now also makes a surface when there is none),
begins the frame and binds the swapchain again when it was the target. Both are internal: no export changes, the
interface version stays 7.

## Test

Android (Nubia NX769J, Adreno, Android 14): send the game to the background with the home button and bring it back from
the launcher; it keeps its process and draws again.
