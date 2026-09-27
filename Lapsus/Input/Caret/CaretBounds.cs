namespace Lapsus.Input;

internal readonly record struct CaretBounds(int Left, int Top, int Right, int Bottom)
{
    public bool IsEmpty => Bottom <= Top;
}
