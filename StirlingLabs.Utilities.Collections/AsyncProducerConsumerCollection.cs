#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace StirlingLabs.Utilities.Collections
{
    [PublicAPI]
    public sealed partial class AsyncProducerConsumerCollection<T>
        : IProducerConsumerCollection<T>, IReadOnlyCollection<T>, IDisposable, INotifyCompletion
    {
        private readonly SemaphoreSlim _semaphore = new(0);
        private readonly IProducerConsumerCollection<T> _collection = null!;

        private readonly CancellationTokenSource _complete;
        private readonly CancellationTokenSource _addingComplete;

        private int _isCompleted;

        private Action? _completedDispatch;

        private int _isDisposed;

        public AsyncProducerConsumerCollection(IProducerConsumerCollection<T> collection)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _complete = new();
            _complete.Token.Register(Completed);
            _addingComplete = CancellationTokenSource.CreateLinkedTokenSource(_complete.Token);
        }

        public AsyncProducerConsumerCollection()
            : this(new ConcurrentQueue<T>()) { }

        public AsyncProducerConsumerCollection(IEnumerable<T> items)
            : this(items is IProducerConsumerCollection<T> pcc ? pcc : new ConcurrentQueue<T>())
            => TryAddRange(items);


        public bool IsAddingCompleted
            => _addingComplete.IsCancellationRequested;

        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => TryToComplete();
        }

        private bool IsCompletedInternal
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Interlocked.CompareExchange(ref _isCompleted, 0, 0) != 0;
        }

        public bool IsDisposed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetIsCompleted()
            => Interlocked.Exchange(ref _isCompleted, 1) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CheckDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException("AsyncQueue");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool SetIsDisposed()
            => Interlocked.Exchange(ref _isDisposed, 1) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryAdd(T item)
        {
            if (IsAddingCompleted)
                return false;

            var success = _collection.TryAdd(item);
            _semaphore.Release();
            return success;
        }

        public int TryAddRange(IEnumerable<T> source)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));

            if (IsAddingCompleted)
                return 0;

            var count = 0;
            foreach (var item in source)
            {
                if (!_collection.TryAdd(item))
                    return count;
                count++;
            }
            _semaphore.Release(count);
            return count;
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask<T> TakeAsync(CancellationToken cancellationToken = default)
            => TakeAsync(true, cancellationToken);

        public async ValueTask<T> TakeAsync(bool continueOnCapturedContext, CancellationToken cancellationToken = default)
        {

            for (;;)
            {
                await WaitForAvailableAsync(continueOnCapturedContext, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext);

                if (_collection.TryTake(out var item))
                {
                    TryToComplete();
                    return item;
                }

                if (TryToComplete())
                    throw new OperationCanceledException("The collection has completed.");

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueTask WaitForAvailableAsync(CancellationToken cancellationToken = default)
            => WaitForAvailableAsync(true, cancellationToken);

        public async ValueTask WaitForAvailableAsync(bool continueOnCapturedContext, CancellationToken cancellationToken)
        {
            CheckDisposed();

            if (IsCompletedInternal)
                throw new InvalidOperationException("The AsyncQueue has already fully completed.");

            if (!IsEmpty) return;

            if (IsAddingCompleted)
                throw new OperationCanceledException("The AsyncQueue completed adding, therefore there will not be any more available items.");

            if (cancellationToken == default)
                await _semaphore.WaitAsync(_addingComplete.Token).ConfigureAwait(continueOnCapturedContext);
            else
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_addingComplete.Token, cancellationToken);
                await _semaphore.WaitAsync(cts.Token).ConfigureAwait(continueOnCapturedContext);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsEmptyInternal()
            => _collection switch
            {
                ConcurrentQueue<T> q => q.IsEmpty,
                ConcurrentStack<T> s => s.IsEmpty,
                ConcurrentBag<T> b => b.IsEmpty,
                _ => _collection.Count == 0
            };

        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryToComplete()
        {
            if (IsCompletedInternal)
                return true;

            if (!IsEmptyInternal() || !_addingComplete.IsCancellationRequested)
                return false;

            _complete.Cancel();
            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            CheckDisposed();

            foreach (var item in _collection)
                yield return item;
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public int Count
        {
            [DebuggerStepThrough]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                CheckDisposed();

                return _collection.Count;
            }
        }

        bool ICollection.IsSynchronized
        {
            [DebuggerStepThrough]
            get {
                CheckDisposed();

                return _collection.IsSynchronized;
            }
        }

        object ICollection.SyncRoot
        {
            [DebuggerStepThrough]
            get {
                CheckDisposed();

                return _collection.SyncRoot;
            }
        }

        public bool IsEmpty
        {
            [DebuggerStepThrough]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                CheckDisposed();

                return IsEmptyInternal();
            }
        }

        public void CopyTo(T[] array, int index)
        {
            CheckDisposed();
            if (array is null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));

            var i = 0;
            foreach (var item in _collection)
                array[index + i++] = item;
        }

        public void CopyTo(Array array, int index)
        {
            CheckDisposed();
            if (array is null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));

            var i = 0;
            foreach (var item in _collection)
                array.SetValue(item, index + i++);
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ToArray()
        {
            CheckDisposed();

            return _collection.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryTake(out T item)
        {
            CheckDisposed();

            var success = _collection.TryTake(out item!);
            TryToComplete();
            return success;
        }

        public void CompleteAdding()
        {
            _addingComplete.Cancel();

            TryToComplete();
        }


        private void ClearInternal()
        {
            switch (_collection)
            {
                case ConcurrentStack<T> s:
                    s.Clear();
                    break;
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
                case ConcurrentQueue<T> q:
                    q.Clear();
                    break;
                case ConcurrentBag<T> b:
                    b.Clear();
                    break;
#endif
                default:
                    while (_collection.TryTake(out _)) { }
                    break;
            }

            TryToComplete();
        }

        public void Clear()
        {
            CheckDisposed();

            ClearInternal();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            if (!SetIsDisposed()) Debug.Fail("Was already disposed.");

            _addingComplete.Cancel();
            _complete.Cancel();

            ClearInternal();

            _addingComplete.Dispose();
            _complete.Dispose();
            _semaphore.Dispose();
        }

        public void OnCompleted(Action continuation)
        {
            lock (_complete)
                _completedDispatch = (Action)Delegate.Combine(_completedDispatch, continuation);
        }

        private void Completed()
        {
            Debug.Assert(!IsCompletedInternal);
            lock (_complete)
            {
                _completedDispatch?.Invoke();
                _completedDispatch = null;
                if (!SetIsCompleted()) Debug.Fail("Was already completed.");
            }
        }

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SuppressMessage("Microsoft.Design", "CA1024", Justification = "Should not be a property")]
        public Consumer GetConsumer() => new(this);

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [SuppressMessage("Microsoft.Design", "CA1024", Justification = "Awaitable implementation")]
        public AsyncProducerConsumerCollection<T> GetAwaiter()
            => this;
    }
}
