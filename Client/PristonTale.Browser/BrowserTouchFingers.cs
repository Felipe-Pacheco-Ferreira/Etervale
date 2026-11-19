using PristonTale.Gameplay.Input;

namespace PristonTale.Browser;

// The fingers on the canvas as the page reports them (pointer events of the touch kind), in the order they landed.
internal sealed class BrowserTouchFingers : ITouchFingers
{
    // More fingers than the gestures read (two) are kept so that lifting one keeps the others' order.
    private const int MostFingers = 10;
    private readonly TouchFinger[] fingers = new TouchFinger[MostFingers];

    public int Count { get; private set; }

    public bool TryRead(int index, out TouchFinger finger)
    {
        if (index < 0 || index >= Count)
        {
            finger = default;
            return false;
        }

        finger = fingers[index];
        return true;
    }

    // A finger landed or moved (down) or left the canvas (up); x and y are fractions of the canvas.
    public void Report(long id, float x, float y, bool down)
    {
        var index = IndexOf(id);
        if (!down)
        {
            if (index >= 0)
            {
                Array.Copy(fingers, index + 1, fingers, index, Count - index - 1);
                Count--;
            }
            return;
        }

        if (index >= 0)
        {
            fingers[index] = new TouchFinger(id, x, y);
        }
        else if (Count < MostFingers)
        {
            fingers[Count++] = new TouchFinger(id, x, y);
        }
    }

    public void Clear() => Count = 0;

    private int IndexOf(long id)
    {
        for (var index = 0; index < Count; index++)
        {
            if (fingers[index].Id == id)
            {
                return index;
            }
        }

        return -1;
    }
}
