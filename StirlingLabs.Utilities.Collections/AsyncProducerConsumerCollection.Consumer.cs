using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace StirlingLabs.Utilities.Collections
{
    public sealed partial class AsyncProducerConsumerCollection<T>
    {
        [SuppressMessage("Microsoft.Design", "CA1034", Justification = "Nested class has private member access")]
        public readonly struct Consumer : IAsyncConsumer<T>, IEquatable<Consumer>
        {
            public readonly AsyncProducerConsumerCollection<T> Collection;

            [DebuggerStepThrough]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Consumer(AsyncProducerConsumerCollection<T> collection)
                => Collection = collection;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(Consumer other)
                => Collection.Equals(other.Collection);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals(object? obj)
                => obj is Consumer other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode()
                => Collection.GetHashCode();

            public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                Collection.CheckDisposed();

                do
                {
                    T item;
                    try
                    {
                        item = await Collection.TakeAsync(cancellationToken)
                            .ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

                    yield return item;
                } while (!cancellationToken.IsCancellationRequested && !Collection.IsCompletedInternal);

                Collection.TryToComplete();

                Collection.CheckDisposed();
            }

            public IEnumerator<T> GetEnumerator()
            {
                Collection.CheckDisposed();

                do
                {
                    T item;
                    try
                    {
                        if (!Collection.TryTake(out item!))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        yield break;
                    }

                    yield return item;
                } while (!Collection.IsCompletedInternal);

                Collection.TryToComplete();

                Collection.CheckDisposed();
            }

            [DebuggerStepThrough]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator ==(Consumer left, Consumer right)
                => left.Equals(right);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator !=(Consumer left, Consumer right)
                => !left.Equals(right);

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
        }
    }
}
