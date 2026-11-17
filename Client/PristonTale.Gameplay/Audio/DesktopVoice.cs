using Microsoft.Xna.Framework.Audio;
using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.Social;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.Audio;

internal sealed class DesktopVoice : IDisposable
{
    internal static bool Available => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    private readonly Dictionary<ulong, DynamicSoundEffectInstance> _speakers = [];
    private readonly byte[] _capture = new byte[VoiceFormat.FrameBytes];
    private Microphone _microphone;
    private int _captureOffset;
    private int _advertisedMask = -1;
    private ulong _entity;
    private bool _held;
    private Task _outgoing;

    public void Update(ClientSettings settings, ulong entity, bool held, Func<string, bool, Task> send)
    {
        if (!Available)
        {
            return;
        }

        if (_entity != entity)
        {
            _entity = entity;
            _advertisedMask = -1;
        }

        var mask = settings.VoiceEnabled ? settings.VoiceReceiveMask & VoiceFormat.ChannelsMask : 0;
        if (entity != 0 && mask != _advertisedMask && (mask != 0 || _advertisedMask > 0))
        {
            _advertisedMask = mask;
            _ = send("/voice listen " + mask, false);
        }

        var talking = held && settings.VoiceEnabled && entity != 0;
        if (talking && !_held)
        {
            _microphone = Microphone.All.FirstOrDefault(device => device.Name == settings.VoiceMicrophone) ?? Microphone.Default;
            _microphone?.Start();
        }
        else if (!talking && _held)
        {
            StopCapture();
        }

        _held = talking;
        if (_microphone?.State == MicrophoneState.Started && !(_outgoing is { IsCompleted: false }))
        {
            _captureOffset += _microphone.GetData(_capture, _captureOffset, _capture.Length - _captureOffset);
            if (_captureOffset == _capture.Length)
            {
                var channel = Math.Clamp(settings.VoiceTransmitChannel, 0, 3);
                _outgoing = send($"/voice {channel} {Convert.ToBase64String(_capture)}", false);
                _captureOffset = 0;
            }
        }

        foreach (var speaker in _speakers.Values)
        {
            speaker.Volume = settings.VoiceEnabled ? Math.Clamp(settings.VoiceVolumePercent, 0, 100) / 100f : 0f;
        }
    }

    public void Receive(ChatLineInfo line, ClientSettings settings)
    {
        var text = line.Text;
        if (!Available || !settings.VoiceEnabled || text.Length < 3 || text[0] is < '0' or > '3' ||
            text[1] != ' ' || (settings.VoiceReceiveMask & (1 << (text[0] - '0'))) == 0)
        {
            return;
        }

        var audio = new byte[VoiceFormat.FrameBytes];
        if (!Convert.TryFromBase64Chars(text.AsSpan(2), audio, out var count) || count != audio.Length)
        {
            return;
        }

        if (!_speakers.TryGetValue(line.SpeakerId, out var speaker))
        {
            if (_speakers.Count >= 32)
            {
                foreach (var entry in _speakers.Where(entry => entry.Value.PendingBufferCount == 0).ToArray())
                {
                    entry.Value.Dispose();
                    _speakers.Remove(entry.Key);
                }

                if (_speakers.Count >= 32)
                {
                    return;
                }
            }

            speaker = new DynamicSoundEffectInstance(VoiceFormat.SampleRate, AudioChannels.Mono);
            _speakers.Add(line.SpeakerId, speaker);
        }

        if (speaker.PendingBufferCount < 5)
        {
            speaker.Volume = Math.Clamp(settings.VoiceVolumePercent, 0, 100) / 100f;
            speaker.SubmitBuffer(audio);
            if (speaker.State != SoundState.Playing)
            {
                speaker.Play();
            }
        }
    }

    private void StopCapture()
    {
        _microphone?.Stop();
        _microphone = null;
        _captureOffset = 0;
    }

    public void Dispose()
    {
        StopCapture();
        foreach (var speaker in _speakers.Values)
        {
            speaker.Dispose();
        }

        _speakers.Clear();
    }
}
