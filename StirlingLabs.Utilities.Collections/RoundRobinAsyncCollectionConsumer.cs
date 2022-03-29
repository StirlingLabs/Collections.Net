using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StirlingLabs.Utilities.Collections;

public class RoundRobinAsyncCollectionConsumer<T> : IAsyncEnumerable<T>, IDisposable
{
    private readonly object _lock = new();

    private readonly IAsyncConsumer<T>[] _consumers;

    private int _index = -1;

    public int Index
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return _index % _consumers.Length;
        }
    }

    public int IncrementIndex()
    {
        if (!Monitor.IsEntered(_lock))
            throw new InvalidOperationException("Must be in lock to increment index.");
        var value = _index;
        _index = (value + 1) % _consumers.Length;
        return value;
    }

    public IAsyncConsumer<T> CurrentConsumer
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return _consumers[Index];
        }
    }

    public bool HasAny
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return _consumers.Any(c => !c.IsEmpty);
        }
    }

    public bool IsEmpty => !HasAny;

    private void WithLock(Action<RoundRobinAsyncCollectionConsumer<T>> action)
    {
        lock (_lock) action(this);
    }
    private TResult WithLock<TResult>(Func<RoundRobinAsyncCollectionConsumer<T>, TResult> fn)
    {
        lock (_lock) return fn(this);
    }

    public RoundRobinAsyncCollectionConsumer(IEnumerable<AsyncProducerConsumerCollection<T>> collections)
        : this(collections.Select(c => (IAsyncConsumer<T>)c.GetConsumer()).ToArray()) { }

    public RoundRobinAsyncCollectionConsumer(params AsyncProducerConsumerCollection<T>[] collections)
        : this(collections.Select(c => (IAsyncConsumer<T>)c.GetConsumer()).ToArray()) { }
    public RoundRobinAsyncCollectionConsumer(IEnumerable<IAsyncConsumer<T>> consumers)
        : this(consumers.ToArray()) { }
    public RoundRobinAsyncCollectionConsumer(params IAsyncConsumer<T>[] consumers)
        => _consumers = consumers;

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(this, cancellationToken);

    private class Enumerator : IAsyncEnumerator<T>, IDisposable
    {
        private RoundRobinAsyncCollectionConsumer<T>? _consumer;
        private readonly CancellationToken _cancellationToken;
        public Enumerator(RoundRobinAsyncCollectionConsumer<T> consumer, CancellationToken cancellationToken)
        {
            _consumer = consumer;
            _cancellationToken = cancellationToken;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Dispose()
            => _consumer = null;

        public async ValueTask<bool> MoveNextAsync(bool continueOnCapturedContext)
        {
            if (_consumer is null) return false;
            while (_consumer.WithLock(c => {
                       var consumers = c._consumers;
                       do
                       {
                           var index = c.IncrementIndex();
                           var consumer = consumers[index];
                           if (!consumer.TryMoveNext(out var item))
                           {
                               // release gc the ref if it's completed
                               if (consumer.IsCompleted)
                                   consumers[index] = AsyncConsumer<T>.Empty;
                               continue;
                           }
                           Current = item!;
                           return false;
                       } while (c.HasAny);
                       return true;
                   }))
            {
                try
                {
                    var remainingConsumers = _consumer._consumers
                        .Where(c => !c.IsCompleted)
                        .Select(c => c.WaitForAvailableAsync(continueOnCapturedContext, _cancellationToken).AsTask())
                        .ToArray();

                    if (remainingConsumers.Length == 0)
                        return false;

                    await Task.WhenAny(remainingConsumers)
                        .ConfigureAwait(continueOnCapturedContext);
                }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
            }
            return true;
        }

        [SuppressMessage("Reliability", "CA2007")]
        public async ValueTask<bool> MoveNextAsync()
            => await MoveNextAsync(true);

        public T Current { get; private set; }
    }

    public void Dispose()
    {
        for (var i = 0; i < _consumers.Length; ++i)
            _consumers[i] = AsyncConsumer<T>.Empty;
    }
}
