using System.Runtime.InteropServices;

namespace HearingAid.Core;

public static class MemoryUtility
{
    public static MemoryStream ReadonlyMemoryToMemoryStream(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> seg))
            return new MemoryStream(seg.Array!, seg.Offset, seg.Count, writable: false);
        else
            return new MemoryStream(memory.ToArray(), writable: false);
    }
}