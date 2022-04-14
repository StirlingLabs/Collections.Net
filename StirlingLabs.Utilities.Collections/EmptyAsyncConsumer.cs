using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StirlingLabs.Utilities.Collections;

public sealed class EmptyAsyncConsumer<T> : IAsyncConsumer<T>
{
    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
        => this;

    public IEnumerator<T> GetEnumerator()
        => this;

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public ValueTask DisposeAsync()
        => default;

    public ValueTask<bool> MoveNextAsync()
        => new(false);

    public bool MoveNext()
        => false;

    public void Reset()
        => throw new NotImplementedException();

    object IEnumerator.Current => default!;

    public T Current => default!;

    public bool IsEmpty => true;

    public bool IsCompleted => true;

    public bool TryMoveNext(out T? item)
    {
        item = default;
        return false;
    }

    public ValueTask WaitForAvailableAsync(bool continueOnCapturedContext, CancellationToken cancellationToken)
        => default;

    public void Dispose() { }
}
