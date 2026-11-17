using PristonTale.Contracts.Social;

namespace PristonTale.Server.Hosting;

internal sealed class WhisperActivity
{
    private readonly Queue<PublicGameMessage> _messages = new();
    private long _sequence;

    public void Record(string sender, string recipient, string text) => RecordChannel("whisper", $"{sender} → {recipient}: {text}");

    public void RecordChannel(string channel, string text)
    {
        lock (_messages)
        {
            if (_messages.Count == 100)
            {
                _messages.Dequeue();
            }

            _messages.Enqueue(new PublicGameMessage(++_sequence, DateTimeOffset.UtcNow, channel, text));
        }
    }

    public PublicGameMessage[] Recent()
    {
        lock (_messages)
        {
            return _messages.ToArray();
        }
    }
}
