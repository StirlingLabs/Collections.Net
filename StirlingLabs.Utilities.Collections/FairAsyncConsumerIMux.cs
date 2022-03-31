using System;
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
public static class FairAsyncCollectionsConsumer
{
    public static FairAsyncConsumerIMux<T> Create<T>(IEnumerable<AsyncProducerConsumerCollection<T>> collections)
        => new(collections);
    public static FairAsyncConsumerIMux<T> Create<T>(params AsyncProducerConsumerCollection<T>[] collections)
        => new(collections);
    public static FairAsyncConsumerIMux<T> Create<T>(IEnumerable<IAsyncConsumer<T>> collections)
        => new(collections);
    public static FairAsyncConsumerIMux<T> Create<T>(params IAsyncConsumer<T>[] collections)
        => new(collections);
}

[PublicAPI]
[DebuggerTypeProxy(typeof(FairAsyncConsumerIMux<>.DebugView))]
public sealed class FairAsyncConsumerIMux<T> : AsyncConsumerIMux<T>
{
    [PublicAPI]
    internal sealed class DebugView
    {
        private FairAsyncConsumerIMux<T> _c;

        public DebugView(FairAsyncConsumerIMux<T> c)
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

    public int Index
    {
        [Pure]
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return IndexInternal;
        }
    }

    private int IndexInternal
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _index % _consumers.Length;
    }

    public IAsyncConsumer<T> CurrentConsumer
    {
        [Pure]
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to access index.");
            return CurrentConsumerInternal;
        }
    }

    private IAsyncConsumer<T> CurrentConsumerInternal
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _consumers[Index];
    }

    public bool IsCompleted
    {
        [Pure]
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return IsCompletedInternal;
        }
    }

    private bool IsCompletedInternal
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _consumers.All(c => c.IsCompleted);
    }

    public bool HasAny
    {
        [Pure]
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return HasAnyInternal;
        }
    }

    private bool HasAnyInternal
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _consumers.Any(c => !c.IsEmpty);
    }

    public bool IsEmpty
    {
        [Pure]
        get {
            if (!Monitor.IsEntered(_lock))
                throw new InvalidOperationException("Must be in lock to read collection state.");
            return IsEmptyInternal;
        }
    }

    private bool IsEmptyInternal
    {
        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _consumers.All(c => c.IsEmpty);
    }

    public void WithLock(Action<FairAsyncConsumerIMux<T>> action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        lock (_lock) action(this);
    }
    public TResult WithLock<TResult>(Func<FairAsyncConsumerIMux<T>, TResult> fn)
    {
        if (fn is null) throw new ArgumentNullException(nameof(fn));
        lock (_lock) return fn(this);
    }

    public FairAsyncConsumerIMux(IEnumerable<AsyncProducerConsumerCollection<T>> collections)
        : this(collections.Select(c => (IAsyncConsumer<T>)c.GetConsumer()).ToArray()) { }

    public FairAsyncConsumerIMux(params AsyncProducerConsumerCollection<T>[] collections)
        : this(collections.Select(c => (IAsyncConsumer<T>)c.GetConsumer()).ToArray()) { }
    public FairAsyncConsumerIMux(IEnumerable<IAsyncConsumer<T>> consumers)
        : this(consumers.ToArray()) { }
    public FairAsyncConsumerIMux(params IAsyncConsumer<T>[] consumers)
        => _consumers = consumers;

    protected override IAsyncEnumerator<T> GetAsyncEnumeratorImpl(CancellationToken cancellationToken = default)
        => GetAsyncEnumerator(cancellationToken);

    public new Enumerator GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new(this, cancellationToken);

    [PublicAPI]
    [SuppressMessage("Design", "CA1034", Justification = "Design choice")]
    public sealed class Enumerator : IAsyncEnumerator<T>, IDisposable
    {
        private FairAsyncConsumerIMux<T>? _consumerIMux;
        private readonly CancellationToken _cancellationToken;
        public Enumerator(FairAsyncConsumerIMux<T> consumerIMux, CancellationToken cancellationToken)
        {
            _consumerIMux = consumerIMux;
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
            _consumerIMux = null;
            Current = default!;
        }

        public async ValueTask<bool> MoveNextAsync(bool continueOnCapturedContext)
        {
            if (_consumerIMux is null) return false;
            while (!_consumerIMux.WithLock(TryConsume))
            {
                try
                {
                    var remainingConsumers = _consumerIMux._consumers
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
        private bool TryConsume(FairAsyncConsumerIMux<T> c)
        {
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
                return true;
            } while (c.HasAny);
            return false;
        }

        [SuppressMessage("Reliability", "CA2007")]
        public async ValueTask<bool> MoveNextAsync()
            => await MoveNextAsync(true);

        public T Current { get; private set; }
    }

    public override void Dispose()
    {
        for (var i = 0; i < _consumers.Length; ++i)
            _consumers[i] = AsyncConsumer<T>.Empty;
    }
}
