// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

// Etervale fork: the audio of the browser build (monogame/issues/021-webgpu-backend.md), in place of
// faudio/MGA_faudio.cpp until the Web Audio backend exists. Voices keep the state the framework reads back (playing,
// paused, stopped) and consume every buffer at once, so sound effects and streamed songs run their course silently.

#include "api_MGA.h"
#include "mg_common.h"

struct MGA_System
{
};

struct MGA_Buffer
{
	mgulong duration = 0;
};

struct MGA_Voice
{
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
	delete buffer;
}

void MGA_Buffer_InitializeFormat(MGA_Buffer* buffer, mgbyte* waveHeader, mgbyte* waveData, mgint length, mgint loopStart, mgint loopLength)
{
}

void MGA_Buffer_InitializePCM(MGA_Buffer* buffer, mgbyte* waveData, mgint offset, mgint length, mgint sampleBits, mgint sampleRate, mgint channels, mgint loopStart, mgint loopLength)
{
	mgint frameBytes = sampleBits / 8 * channels;
	if (frameBytes > 0 && sampleRate > 0)
	{
		buffer->duration = (mgulong)length / (mgulong)frameBytes * 1000 / (mgulong)sampleRate;
	}
}

void MGA_Buffer_InitializeXact(MGA_Buffer* buffer, mguint codec, mgbyte* waveData, mgint length, mgint sampleRate, mgint blockAlignment, mgint channels, mgint loopStart, mgint loopLength)
{
}

mgulong MGA_Buffer_GetDuration(MGA_Buffer* buffer)
{
	return buffer->duration;
}

MGA_Voice* MGA_Voice_Create(MGA_System* system, mgint sampleRate, mgint channels)
{
	return new MGA_Voice();
}

void MGA_Voice_Destroy(MGA_Voice* voice)
{
	delete voice;
}

mgint MGA_Voice_GetBufferCount(MGA_Voice* voice)
{
	return 0;
}

mgint MGA_Voice_GetFinishedBufferCount(MGA_Voice* voice)
{
	mgint finished = voice->finished;
	voice->finished = 0;
	return finished;
}

void MGA_Voice_SetBuffer(MGA_Voice* voice, MGA_Buffer* buffer)
{
}

void MGA_Voice_AppendBuffer(MGA_Voice* voice, mgbyte* buffer, mguint size)
{
	voice->finished++;
}

// A silent voice ends as soon as it starts, unless it loops (a looped sound stays playing until it is stopped).
void MGA_Voice_Play(MGA_Voice* voice, mgbyte looped)
{
	voice->state = looped != 0 ? MGSoundState::Playing : MGSoundState::Stopped;
}

void MGA_Voice_Pause(MGA_Voice* voice)
{
	if (voice->state == MGSoundState::Playing)
	{
		voice->state = MGSoundState::Paused;
	}
}

void MGA_Voice_Resume(MGA_Voice* voice)
{
	if (voice->state == MGSoundState::Paused)
	{
		voice->state = MGSoundState::Playing;
	}
}

void MGA_Voice_Stop(MGA_Voice* voice, mgbyte immediate)
{
	voice->state = MGSoundState::Stopped;
}

MGSoundState MGA_Voice_GetState(MGA_Voice* voice)
{
	return voice->state;
}

mgulong MGA_Voice_GetPosition(MGA_Voice* voice)
{
	return 0;
}

void MGA_Voice_SetPan(MGA_Voice* voice, mgfloat pan)
{
}

void MGA_Voice_SetPitch(MGA_Voice* voice, mgfloat pitch)
{
}

void MGA_Voice_SetVolume(MGA_Voice* voice, mgfloat volume)
{
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
