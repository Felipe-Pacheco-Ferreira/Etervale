namespace PristonTale.Gameplay.Effects;

// The life a potion restored to the local player: the server says only that a potion took effect, so the life gained in
// the owner state that follows within WindowMs is the potion's.
public sealed class PotionHealWatch
{
    public const long WindowMs = 1500;
    private int _before = -1;
    private long _until;

    public void Drank(int life, long nowMs)
    {
        _before = life;
        _until = nowMs + WindowMs;
    }

    // The life gained since the potion, once; 0 while nothing was gained or after the window.
    public int Gained(int life, long nowMs)
    {
        if (_before < 0)
        {
            return 0;
        }

        if (nowMs > _until)
        {
            _before = -1;
            return 0;
        }

        if (life <= _before)
        {
            return 0;
        }

        var gained = life - _before;
        _before = -1;
        return gained;
    }
}
