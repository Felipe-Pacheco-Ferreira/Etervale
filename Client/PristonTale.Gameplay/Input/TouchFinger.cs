namespace PristonTale.Gameplay.Input;

// A finger on the screen: its identity while it stays down and its place in fractions of the screen.
public readonly record struct TouchFinger(long Id, float X, float Y);

// The fingers down now, in the order they landed; each client reads its own platform's touches.
public interface ITouchFingers
{
    int Count { get; }

    bool TryRead(int index, out TouchFinger finger);
}
