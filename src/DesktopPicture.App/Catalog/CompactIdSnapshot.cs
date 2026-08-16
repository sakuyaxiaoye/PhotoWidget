using System;

namespace DesktopPicture.Catalog;

public sealed class CompactIdSnapshot
{
    public static readonly CompactIdSnapshot Empty = new(Array.Empty<int>());

    private readonly int[] _ids;

    public int Count => _ids.Length;
    public bool IsEmpty => _ids.Length == 0;

    public CompactIdSnapshot(int[] ids)
    {
        _ids = ids;
    }

    public int GetIdAt(int index)
    {
        if (index < 0 || index >= _ids.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        return _ids[index];
    }

    public int FindIndex(int id)
    {
        return Array.BinarySearch(_ids, id);
    }

    public int[] RawArray => _ids;
}
