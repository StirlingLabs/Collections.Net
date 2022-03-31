using System;
using System.Collections.Generic;
using System.Threading;
using JetBrains.Annotations;

namespace StirlingLabs.Utilities.Collections;


[PublicAPI]
public static class AsyncConsumerIMux
{
    public static AsyncConsumerIMux<T> Fair<T>(IEnumerable<AsyncProducerConsumerCollection<T>> collections)
        => new FairAsyncConsumerIMux<T>(collections);
    public static AsyncConsumerIMux<T> Fair<T>(params AsyncProducerConsumerCollection<T>[] collections)
        => new FairAsyncConsumerIMux<T>(collections);
    public static AsyncConsumerIMux<T> Fair<T>(IEnumerable<IAsyncConsumer<T>> collections)
        => new FairAsyncConsumerIMux<T>(collections);
    public static AsyncConsumerIMux<T> Fair<T>(params IAsyncConsumer<T>[] collections)
        => new FairAsyncConsumerIMux<T>(collections);
}

public abstract class AsyncConsumerIMux<T>
    : IAsyncEnumerable<T>, IDisposable
{
    public abstract void Dispose();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => GetAsyncEnumeratorImpl(cancellationToken);

    protected abstract IAsyncEnumerator<T> GetAsyncEnumeratorImpl(CancellationToken cancellationToken = default);
}
