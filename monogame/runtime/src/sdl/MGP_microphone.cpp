#include "mg_common.h"
#include <SDL.h>

MG_EXPORT int MGP_Microphone_Count()
{
    if (SDL_WasInit(SDL_INIT_AUDIO) == 0 && SDL_InitSubSystem(SDL_INIT_AUDIO) != 0)
        return 0;
    const int count = SDL_GetNumAudioDevices(SDL_TRUE);
    return count > 0 ? count : 0;
}

MG_EXPORT const char* MGP_Microphone_Name(int index)
{
    return SDL_GetAudioDeviceName(index, SDL_TRUE);
}

MG_EXPORT unsigned int MGP_Microphone_Open(const char* name)
{
    SDL_AudioSpec format{};
    format.freq = 16000;
    format.format = AUDIO_S16LSB;
    format.channels = 1;
    format.samples = 320;
    const auto device = SDL_OpenAudioDevice(name, SDL_TRUE, &format, nullptr, 0);
    if (device != 0)
        SDL_PauseAudioDevice(device, 0);
    return device;
}

MG_EXPORT void MGP_Microphone_Close(unsigned int device)
{
    SDL_CloseAudioDevice(device);
}

MG_EXPORT int MGP_Microphone_Read(unsigned int device, void* destination, int length)
{
    if (SDL_GetQueuedAudioSize(device) > 16000)
        SDL_ClearQueuedAudio(device);
    return static_cast<int>(SDL_DequeueAudio(device, destination, static_cast<Uint32>(length)));
}
