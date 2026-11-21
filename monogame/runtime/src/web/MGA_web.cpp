// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the audio of the browser build over Web Audio (monogame/issues/025-web-audio.md), in place of
// faudio/MGA_faudio.cpp. The page supplies the Web Audio side as Module.noxiusAudio (Client/PristonTale.Browser,
// wwwroot/web-audio.js), which keeps the AudioBuffers and voices by id. A page without it (the WebGpuRepro) gets silent
// voices that keep the state the framework reads back and consume every buffer at once.

#include "api_MGA.h"
#include "mg_common.h"

#include <emscripten.h>

// The sample formats of a wave header (WAVEFORMATEX's wFormatTag).
static const mgint MGA_WebFormatPcm = 1;
static const mgint MGA_WebFormatFloat = 3;

EM_JS(int, MGA_Web_BufferCreate, (const mgbyte* data, int length, int format, int bits, int rate, int channels, int loopStart, int loopLength), {
	const audio = Module.noxiusAudio;
	return audio ? audio.createBuffer(HEAPU8.subarray(data, data + length), format, bits, rate, channels, loopStart, loopLength) : 0;
});
EM_JS(void, MGA_Web_BufferDestroy, (int id), {
	const audio = Module.noxiusAudio;
	if (audio) audio.destroyBuffer(id);
});
EM_JS(int, MGA_Web_VoiceCreate, (int rate, int channels), {
	const audio = Module.noxiusAudio;
	return audio ? audio.createVoice(rate, channels) : 0;
});
EM_JS(void, MGA_Web_VoiceDestroy, (int id), { Module.noxiusAudio.destroyVoice(id); });
EM_JS(void, MGA_Web_VoiceSetBuffer, (int id, int buffer), { Module.noxiusAudio.setBuffer(id, buffer); });
EM_JS(void, MGA_Web_VoiceAppend, (int id, const mgbyte* data, int length), {
	Module.noxiusAudio.appendBuffer(id, HEAPU8.subarray(data, data + length));
});
EM_JS(int, MGA_Web_VoiceBufferCount, (int id), { return Module.noxiusAudio.bufferCount(id); });
EM_JS(int, MGA_Web_VoiceFinishedCount, (int id), { return Module.noxiusAudio.finishedCount(id); });
EM_JS(void, MGA_Web_VoicePlay, (int id, int looped), { Module.noxiusAudio.play(id, looped); });
EM_JS(void, MGA_Web_VoicePause, (int id), { Module.noxiusAudio.pause(id); });
EM_JS(void, MGA_Web_VoiceResume, (int id), { Module.noxiusAudio.resume(id); });
EM_JS(void, MGA_Web_VoiceStop, (int id), { Module.noxiusAudio.stop(id); });
EM_JS(int, MGA_Web_VoiceState, (int id), { return Module.noxiusAudio.state(id); });
EM_JS(double, MGA_Web_VoicePosition, (int id), { return Module.noxiusAudio.position(id); });
EM_JS(void, MGA_Web_VoiceSetVolume, (int id, float volume), { Module.noxiusAudio.setVolume(id, volume); });
EM_JS(void, MGA_Web_VoiceSetPitch, (int id, float pitch), { Module.noxiusAudio.setPitch(id, pitch); });
EM_JS(void, MGA_Web_VoiceSetPan, (int id, float pan), { Module.noxiusAudio.setPan(id, pan); });

struct MGA_System
{
};

struct MGA_Buffer
{
	int id = 0;
	mgulong duration = 0;
};

// id 0: no Web Audio on the page, or a buffer it could not convert. Such a voice plays silently.
struct MGA_Voice
{
	int id = 0;
	MGSoundState state = MGSoundState::Stopped;
	mgint finished = 0;
};

MGA_System* MGA_System_Create()
{
	return new MGA_System();
}

void MGA_System_Destroy(MGA_System* system)
{
	delete system;
}

mgint MGA_System_GetMaxInstances()
{
	return 256;
}

void MGA_System_SetReverbSettings(MGA_System* system, ReverbSettings& settings)
{
}

MGA_Buffer* MGA_Buffer_Create(MGA_System* system)
{
	return new MGA_Buffer();
}

void MGA_Buffer_Destroy(MGA_Buffer* buffer)
{
	if (buffer->id != 0)
	{
		MGA_Web_BufferDestroy(buffer->id);
	}

	delete buffer;
}

static void MGA_Web_InitializeSamples(MGA_Buffer* buffer, const mgbyte* data, mgint length, mgint format, mgint bits, mgint rate, mgint channels,
	mgint loopStart, mgint loopLength)
{
	mgint frameBytes = bits / 8 * channels;
	if (frameBytes <= 0 || rate <= 0 || length <= 0)
	{
		return;
	}

	buffer->duration = (mgulong)length / (mgulong)frameBytes * 1000 / (mgulong)rate;
	if (data != nullptr)
	{
		buffer->id = MGA_Web_BufferCreate(data, length, format, bits, rate, channels, loopStart, loopLength);
	}
}

// WAVEFORMATEX, little-endian: format tag, channels, sample rate, bytes per second, block alignment, bits per sample.
void MGA_Buffer_InitializeFormat(MGA_Buffer* buffer, mgbyte* waveHeader, mgbyte* waveData, mgint length, mgint loopStart, mgint loopLength)
{
	mgint format = waveHeader[0] | waveHeader[1] << 8;
	mgint channels = waveHeader[2] | waveHeader[3] << 8;
	mgint rate = waveHeader[4] | waveHeader[5] << 8 | waveHeader[6] << 16 | waveHeader[7] << 24;
	mgint bits = waveHeader[14] | waveHeader[15] << 8;
	if (format != MGA_WebFormatPcm && format != MGA_WebFormatFloat)
	{
		return;
	}

	MGA_Web_InitializeSamples(buffer, waveData, length, format, bits, rate, channels, loopStart, loopLength);
}

void MGA_Buffer_InitializePCM(MGA_Buffer* buffer, mgbyte* waveData, mgint offset, mgint length, mgint sampleBits, mgint sampleRate, mgint channels,
	mgint loopStart, mgint loopLength)
{
	MGA_Web_InitializeSamples(buffer, waveData == nullptr ? nullptr : waveData + offset, length, MGA_WebFormatPcm, sampleBits, sampleRate, channels,
		loopStart, loopLength);
}

void MGA_Buffer_InitializeXact(MGA_Buffer* buffer, mguint codec, mgbyte* waveData, mgint length, mgint sampleRate, mgint blockAlignment, mgint channels,
	mgint loopStart, mgint loopLength)
{
}

mgulong MGA_Buffer_GetDuration(MGA_Buffer* buffer)
{
	return buffer->duration;
}

MGA_Voice* MGA_Voice_Create(MGA_System* system, mgint sampleRate, mgint channels)
{
	auto voice = new MGA_Voice();
	voice->id = MGA_Web_VoiceCreate(sampleRate, channels);
	return voice;
}

void MGA_Voice_Destroy(MGA_Voice* voice)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceDestroy(voice->id);
	}

	delete voice;
}

mgint MGA_Voice_GetBufferCount(MGA_Voice* voice)
{
	return voice->id != 0 ? MGA_Web_VoiceBufferCount(voice->id) : 0;
}

mgint MGA_Voice_GetFinishedBufferCount(MGA_Voice* voice)
{
	if (voice->id != 0)
	{
		return MGA_Web_VoiceFinishedCount(voice->id);
	}

	mgint finished = voice->finished;
	voice->finished = 0;
	return finished;
}

void MGA_Voice_SetBuffer(MGA_Voice* voice, MGA_Buffer* buffer)
{
	voice->state = MGSoundState::Stopped;
	if (voice->id != 0)
	{
		MGA_Web_VoiceSetBuffer(voice->id, buffer != nullptr ? buffer->id : 0);
	}
}

void MGA_Voice_AppendBuffer(MGA_Voice* voice, mgbyte* buffer, mguint size)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceAppend(voice->id, buffer, (int)size);
		return;
	}

	voice->finished++;
}

// A silent voice ends as soon as it starts, unless it loops (a looped sound stays playing until it is stopped).
void MGA_Voice_Play(MGA_Voice* voice, mgbyte looped)
{
	if (voice->id != 0)
	{
		MGA_Web_VoicePlay(voice->id, looped);
		return;
	}

	voice->state = looped != 0 ? MGSoundState::Playing : MGSoundState::Stopped;
}

void MGA_Voice_Pause(MGA_Voice* voice)
{
	if (voice->id != 0)
	{
		MGA_Web_VoicePause(voice->id);
		return;
	}

	if (voice->state == MGSoundState::Playing)
	{
		voice->state = MGSoundState::Paused;
	}
}

void MGA_Voice_Resume(MGA_Voice* voice)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceResume(voice->id);
		return;
	}

	if (voice->state == MGSoundState::Paused)
	{
		voice->state = MGSoundState::Playing;
	}
}

void MGA_Voice_Stop(MGA_Voice* voice, mgbyte immediate)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceStop(voice->id);
		return;
	}

	voice->state = MGSoundState::Stopped;
}

MGSoundState MGA_Voice_GetState(MGA_Voice* voice)
{
	return voice->id != 0 ? (MGSoundState)MGA_Web_VoiceState(voice->id) : voice->state;
}

mgulong MGA_Voice_GetPosition(MGA_Voice* voice)
{
	return voice->id != 0 ? (mgulong)MGA_Web_VoicePosition(voice->id) : 0;
}

void MGA_Voice_SetPan(MGA_Voice* voice, mgfloat pan)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceSetPan(voice->id, pan);
	}
}

void MGA_Voice_SetPitch(MGA_Voice* voice, mgfloat pitch)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceSetPitch(voice->id, pitch);
	}
}

void MGA_Voice_SetVolume(MGA_Voice* voice, mgfloat volume)
{
	if (voice->id != 0)
	{
		MGA_Web_VoiceSetVolume(voice->id, volume);
	}
}

void MGA_Voice_SetReverbMix(MGA_Voice* voice, mgfloat mix)
{
}

void MGA_Voice_SetFilterMode(MGA_Voice* voice, MGFilterMode mode, mgfloat filterQ, mgfloat frequency)
{
}

void MGA_Voice_ClearFilterMode(MGA_Voice* voice)
{
}

void MGA_Voice_Apply3D(MGA_Voice* voice, Listener& listener, Emitter& emitter, mgfloat distanceScale)
{
}
