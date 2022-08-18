using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace StirlingLabs.Utilities.Collections;

public sealed partial class AsyncProducerConsumerCollection<T>
{
    [PublicAPI]
    [SuppressMessage("Microsoft.Design", "CA1034", Justification = "Nested class has private member access")]
    public sealed class Consumer : IAsyncConsumer<T>, IEquatable<Consumer>
    {
        private static readonly bool IsClassType = typeof(T).IsClass;
        private static readonly int SizeOfType = Unsafe.SizeOf<T>();

        public readonly AsyncProducerConsumerCollection<T> Collection;

        private int _isBeingEnumerated;

        private CancellationToken _cancellationToken;
        private T _current;
        private int _alsoCurrent;

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Consumer(AsyncProducerConsumerCollection<T> collection, CancellationToken cancellationToken = default)
        {
            Collection = collection;
            _cancellationToken = cancellationToken;
            _current = default!;
            _alsoCurrent = 0;
            _isBeingEnumerated = 0;
        }

        object IEnumerator.Current => Current!;

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public T Current
        {
            [DebuggerHidden]
            [MustUseReturnValue]
            get => ExchangeCurrentWithDefault();
            [SuppressMessage("ReSharper", "MustUseReturnValue")]
            private set => ExchangeCurrent(value);
        }

        public bool IsBeingEnumerated
        {
            get => Interlocked.CompareExchange(ref _isBeingEnumerated, 0, 0) != 0;
            private set => Interlocked.Exchange(ref _isBeingEnumerated, value ? 1 : 0);
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Collection.IsEmpty;
        }

        public bool IsCompleted
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Collection.IsCompletedInternal;
        }

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Consumer? other)
            => Collection.Equals(other?.Collection);

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj)
            => obj is Consumer other && Equals(other);

        [Pure]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
            => Collection.GetHashCode();

        public void SetCancellationToken(CancellationToken cancellationToken = default)
        {
            if (IsBeingEnumerated) throw new InvalidOperationException("May only be consumed once.");
            _cancellationToken = cancellationToken;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            if (IsBeingEnumerated)
                return new Consumer(Collection, cancellationToken);

            SetCancellationToken(cancellationToken);
            IsBeingEnumerated = true;
            return this;
        }

        public IEnumerator<T> GetEnumerator()
            => this;

        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(Consumer? left, Consumer? right)
            => left?.Equals(right) ?? right is null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(Consumer? left, Consumer? right)
            => !(left == right);

        [SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task")]
        public async ValueTask<bool> MoveNextAsync()
        {
            IsBeingEnumerated = true;
            Collection.CheckDisposed();
            do
            {
                if (Collection.TryTake(out var item))
                {
                    Current = item;
                    break;
                }
                if (!await Collection.TryWaitForAvailableAsync(_cancellationToken))
                {
                    Collection.TryToComplete();
                    Collection.CheckDisposed();
                    return false;
                }
            } while (!Collection.IsCompletedInternal);
            Collection.TryToComplete();
            Collection.CheckDisposed();
            return true;
        }

        public bool MoveNext()
        {
            IsBeingEnumerated = true;
            Collection.CheckDisposed();
            do
            {
                if (Collection.TryTake(out var item))
                {
                    Current = item;
                    break;
                }
                if (!Collection.TryWaitForAvailable(_cancellationToken))
                    break;
            } while (!Collection.IsCompletedInternal);
            Collection.TryToComplete();
            Collection.CheckDisposed();
            return true;
        }

        [MustUseReturnValue]
#if NETSTANDARD2_0
        public bool TryMoveNext(out T? item)
#else
        public bool TryMoveNext([NotNullWhen(true)] out T? item)
#endif
        {
            IsBeingEnumerated = true;
            Collection.CheckDisposed();
            if (!Collection.TryTake(out item!))
                return false;
            // ReSharper disable once MustUseReturnValue
            ExchangeCurrentWithDefault();
            Collection.TryToComplete();
            Collection.CheckDisposed();
            return true;
        }


        [MustUseReturnValue]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T ExchangeCurrent(T value)
        {
            if (IsClassType)
            {
                var o = Interlocked.Exchange(ref Unsafe.As<T, object>(ref _current)!, value);
                return Unsafe.As<object, T>(ref o);
            }

            switch (SizeOfType)
            {
                case 8: {
                    var x = Unsafe.As<T, long>(ref value);
                    var v = Interlocked.Exchange(ref Unsafe.As<T, long>(ref _current), x);
                    return Unsafe.As<long, T>(ref v);
                }
                case 4: {
                    var x = Unsafe.As<T, int>(ref value);
                    var v = Interlocked.Exchange(ref Unsafe.As<T, int>(ref _current), x);
                    return Unsafe.As<int, T>(ref v);
                }
                case 3:
                case 2:
                case 1: {
                    var x = Unsafe.As<T, int>(ref value);
                    x &= (int)(uint.MaxValue >> (4 - SizeOfType * 8));
                    var v = Interlocked.Exchange(ref _alsoCurrent, x);
                    return Unsafe.As<int, T>(ref v);
                }
                default:
                    throw new NotImplementedException(typeof(T).AssemblyQualifiedName);
            }
        }

        [MustUseReturnValue]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private T ExchangeCurrentWithDefault()
        {

            if (IsClassType)
            {
                var o = Interlocked.Exchange(ref Unsafe.As<T, object>(ref _current)!, null);
                return Unsafe.As<object, T>(ref o);
            }
            switch (SizeOfType)
            {
                case 8: {
                    var v = Interlocked.Exchange(ref Unsafe.As<T, long>(ref _current), default);
                    return Unsafe.As<long, T>(ref v);
                }
                case 4: {
                    var v = Interlocked.Exchange(ref Unsafe.As<T, int>(ref _current), default);
                    return Unsafe.As<int, T>(ref v);
                }
                case 3:
                case 2:
                case 1: {
                    var v = Interlocked.Exchange(ref _alsoCurrent, default);
                    return Unsafe.As<int, T>(ref v);
                }
                default:
                    throw new NotImplementedException(typeof(T).AssemblyQualifiedName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetCurrentToDefault()
        {
            [SuppressMessage("ReSharper", "UnusedParameter.Local")]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static void Discard(T v) { }

            Discard(ExchangeCurrentWithDefault());
        }

        public void Dispose()
        {
            IsBeingEnumerated = true;
            Current = default!;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public void Reset()
            => throw new NotSupportedException();

        public ValueTask WaitForAvailableAsync(bool continueOnCapturedContext, CancellationToken cancellationToken)
            => Collection.WaitForAvailableAsync(continueOnCapturedContext, cancellationToken);

        public ValueTask<bool> TryWaitForAvailableAsync(bool continueOnCapturedContext, CancellationToken cancellationToken)
            => Collection.TryWaitForAvailableAsync(continueOnCapturedContext, cancellationToken);
    }
}
