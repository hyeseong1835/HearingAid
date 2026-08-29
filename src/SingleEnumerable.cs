using System.Collections;
using System.Collections.Generic;

namespace HS.Common.Collection;

public struct SingleEnumerable<T> : IEnumerable<T>
{
    public T value;

    public SingleEnumerable(T value)
    {
        this.value = value;
    }

    public IEnumerator<T> GetEnumerator()
    {
        yield return value;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
