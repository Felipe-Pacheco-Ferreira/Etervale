// MonoGame - Copyright (C) MonoGame Foundation, Inc
// This file is subject to the terms and conditions defined in
// file 'LICENSE.txt', which is part of this source code package.

using System;
using System.Runtime.InteropServices;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework.Audio;

public sealed partial class Microphone
{
    private static bool _enumerated;
    private uint _captureDevice;

    private static void PlatformEnumerate()
    {
        if (_enumerated || !(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
        {
            return;
        }

        _enumerated = true;
        try
        {
            var count = CaptureCount();
            _default = new Microphone { _sampleRate = 16000 };
            for (var index = 0; index < count; index++)
            {
                var name = Marshal.PtrToStringUTF8(CaptureName(index));
                if (!string.IsNullOrEmpty(name))
                {
                    _allMicrophones.Add(new Microphone(name) { _sampleRate = 16000 });
                }
            }
        }
        catch (EntryPointNotFoundException)
        {
            _default = null;
        }
    }

    private void PlatformStart()
    {
        if (_captureDevice != 0 || !(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
        {
            return;
        }

        try
        {
            _captureDevice = CaptureOpen(Name);
            if (_captureDevice != 0)
            {
                _state = MicrophoneState.Started;
            }
        }
        catch (EntryPointNotFoundException)
        {
            _state = MicrophoneState.Stopped;
        }
    }

    private void PlatformStop()
    {
        if (_captureDevice != 0)
        {
            CaptureClose(_captureDevice);
            _captureDevice = 0;
        }

        _state = MicrophoneState.Stopped;
    }

    private void Update()
    {
    }

    private unsafe int PlatformGetData(byte[] buffer, int offset, int count)
    {
        if (_captureDevice == 0 || count == 0)
        {
            return 0;
        }

        fixed (byte* destination = buffer.AsSpan(offset, count))
        {
            return CaptureRead(_captureDevice, destination, count & ~1);
        }
    }

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGP_Microphone_Count", ExactSpelling = true)]
    private static extern int CaptureCount();

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGP_Microphone_Name", ExactSpelling = true)]
    private static extern IntPtr CaptureName(int index);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGP_Microphone_Open", ExactSpelling = true)]
    private static extern uint CaptureOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGP_Microphone_Close", ExactSpelling = true)]
    private static extern void CaptureClose(uint device);

    [DllImport(MGP.MonoGameNativeDLL, EntryPoint = "MGP_Microphone_Read", ExactSpelling = true)]
    private static extern unsafe int CaptureRead(uint device, byte* destination, int length);
}
