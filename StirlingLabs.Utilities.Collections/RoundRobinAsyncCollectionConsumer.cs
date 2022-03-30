using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace StirlingLabs.Utilities.Collections;

[PublicAPI]
public static class RoundRobinAsyncCollectionConsumer
{
    public static RoundRobinAsyncCollectionConsumer<T> Create<T>(IEnumerable<AsyncProducerConsumerCollection<T>> collections)
        => new(collections);
    public static RoundRobinAsyncCollectionConsumer<T> Create<T>(params AsyncProducerConsumerCollection<T>[] collections)
        => new(collections);
    public static RoundRobinAsyncCollectionConsumer<T> Create<T>(IEnumerable<IAsyncConsumer<T>> collections)
        => new(collections);
    public static RoundRobinAsyncCollectionConsumer<T> Create<T>(params IAsyncConsumer<T>[] collections)
        => new(collections);
}

[PublicAPI]
[DebuggerTypeProxy(typeof(RoundRobinAsyncCollectionConsumer<>.DebugView))]
public sealed class RoundRobinAsyncCollectionConsumer<T> : IAsyncEnumerable<T>, IDisposable
{
    internal sealed class DebugView
    {
        private RoundRobinAsyncCollectionConsumer<T> _c;

        public DebugView(RoundRobinAsyncCollectionConsumer<T> c)
            => _c = c;

        public object Lock => _c._lock;
        public object RealIndex => _c._index;
        public int Index => _c.IndexInternal;
        public bool HasAny => _c.HasAnyInternal;
        public bool IsEmpty => _c.IsEmptyInternal;
        public bool IsCompleted => _c.IsCompletedInternal;
    }
    
    private readonly object _lock = new();

    private readonly IAsyncConsumer<T>[] _consumers;

    private int _index = -1;

    public int IncrementIndex()
    {
        if (!Monitor.IsEntered(_lock))
            throw new InvalidOperationException("Must be in lock to increment index.");
        return _index = (_index + 1) % _consumers.Length;
    }

    [DebuggerDisplay("{"+nameof(IndexInternal)+"}")]
    public int Index
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return IndexInternal;
        }
    }

    private int IndexInternal => _index % _consumers.Length;

    public IAsyncConsumer<T> CurrentConsumer
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return CurrentConsumerInternal;
        }
    }

    private IAsyncConsumer<T> CurrentConsumerInternal => _consumers[Index];

    public bool IsCompleted
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return IsCompletedInternal;
        }
    }

    private bool IsCompletedInternal => _consumers.All(c => c.IsCompleted);

    public bool HasAny
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return HasAnyInternal;
        }
    }

    private bool HasAnyInternal => _consumers.Any(c => !c.IsEmpty);

    public bool IsEmpty
    {
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return IsEmptyInternal;
        }
    }

    private bool IsEmptyInternal => _consumers.All(c => c.IsEmpty);

    public void WithLock(Action<RoundRobinAsyncCollectionConsumer<T>> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        lock (_lock) action(this);
    }
    public TResult WithLock<TResult>(Func<RoundRobinAsyncCollectionConsumer<T>, TResult> fn)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
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

    public Enumerator GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new(this, cancellationToken);

    IAsyncEnumerator<T> IAsyncEnumerable<T>.GetAsyncEnumerator(CancellationToken cancellationToken)
        => GetAsyncEnumerator(cancellationToken);

    [PublicAPI]
    [SuppressMessage("Design", "CA1034", Justification = "Design choice")]
    public sealed class Enumerator : IAsyncEnumerator<T>, IDisposable
    {
        private RoundRobinAsyncCollectionConsumer<T>? _consumer;
        private readonly CancellationToken _cancellationToken;
        public Enumerator(RoundRobinAsyncCollectionConsumer<T> consumer, CancellationToken cancellationToken)
        {
            _consumer = consumer;
            _cancellationToken = cancellationToken;
            Current = default!;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Dispose()
        {
            _consumer = null;
            Current = default!;
        }

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
