using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace StirlingLabs.Utilities.Collections.Tests;

public class AsyncProducerConsumerCollectionTests
{
    public static Thread RunThread(Action a)
    {
        static void Exec(object? o)
            => ((Action)o!)();

        var thread = new Thread(Exec);
        thread.Start(a);
        return thread;
    }

    [Test]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public void GeneralTest1()
    {
        AsyncProducerConsumerCollection<int>? a;

        using (a = new())
        {
            a.Count.Should().Be(0);
            a.IsEmpty.Should().BeTrue();
            a.IsAddingCompleted.Should().BeFalse();
            a.IsCompleted.Should().BeFalse();
            a.IsDisposed.Should().BeFalse();
        }

        a.IsAddingCompleted.Should().BeTrue();
        a.IsCompleted.Should().BeTrue();
        a.IsDisposed.Should().BeTrue();
    }

    public static IEnumerable<IEnumerable<int>> GeneralTest2ValueSource()
    {
        yield return new[] { 1, 2, 3 };
        yield return new ConcurrentQueue<int>(new[] { 1, 2, 3 });
        yield return new ConcurrentStack<int>(new[] { 3, 2, 1 });
    }

    [Theory]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public void GeneralTest2([ValueSource(nameof(GeneralTest2ValueSource))] IEnumerable<int> items)
    {
        AsyncProducerConsumerCollection<int>? a;

        using (a = new(items))
        {
            a.Count.Should().Be(3);
            a.IsEmpty.Should().BeFalse();
            a.IsAddingCompleted.Should().BeFalse();
            a.IsCompleted.Should().BeFalse();
            a.IsDisposed.Should().BeFalse();

            a.CompleteAdding();
            a.IsEmpty.Should().BeFalse();
            a.IsAddingCompleted.Should().BeTrue();
            a.IsCompleted.Should().BeFalse();
            a.IsDisposed.Should().BeFalse();

            for (var i = 1; i <= 3; ++i)
            {
                a.IsEmpty.Should().BeFalse();
                a.TryTake(out var x).Should().BeTrue();
                x.Should().Be(i);
            }

            a.IsEmpty.Should().BeTrue();
            a.IsAddingCompleted.Should().BeTrue();
            a.IsCompleted.Should().BeTrue();
            a.IsDisposed.Should().BeFalse();
        }

        a.IsAddingCompleted.Should().BeTrue();
        a.IsCompleted.Should().BeTrue();
        a.IsDisposed.Should().BeTrue();
    }


    [Test]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public async Task GeneralQueueOperations()
    {
        using var cts = new CancellationTokenSource();
        Thread? qt1 = null;
        Thread? qt2 = null;
        try
        {

            var objects = new object[300];

            for (var o = 0; o < 150; ++o) objects[o] = new();

            using var q = new AsyncProducerConsumerCollection<object>(objects);

            q.IsEmpty.Should().BeFalse();
            var initCount = objects.Length;
            q.Count.Should().Be(initCount);

            using var mre1 = new ManualResetEventSlim();

            qt1 = RunThread(() => {
                try { mre1.Wait(cts.Token); }
                catch (OperationCanceledException) { }
                mre1.Dispose();
                if (cts.IsCancellationRequested) return;
                q.TryAdd(objects.First());
                q.TryAddRange(objects.Skip(1));
            });

            using var mre2 = new ManualResetEventSlim();

            qt2 = RunThread(() => {
                try { mre2.Wait(cts.Token); }
                catch (OperationCanceledException) { }
                mre2.Dispose();
                if (cts.IsCancellationRequested) return;
                q.TryAddRange(objects);
                q.CompleteAdding();
            });

            var i = 0;
            var c = q.GetConsumer();
            c.Should().NotBeNull();

            c.Equals(c).Should().BeTrue();

            c.Equals((object)c).Should().BeTrue();

            c.GetHashCode().Should().Be(q.GetHashCode());

#pragma warning disable 1718 // intentional, calls overloaded operator
            // ReSharper disable once EqualExpressionComparison
            (c == c).Should().BeTrue();

            // ReSharper disable once EqualExpressionComparison
            (c != c).Should().BeFalse();
#pragma warning restore 1718

            foreach (var item in c)
            {
                item.Should().Be(objects[i++]);
                q.IsAddingCompleted.Should().BeFalse();
                c.IsCompleted.Should().BeFalse();
                if (i >= initCount) break;
                c.IsEmpty.Should().BeFalse();
                //q.Count.Should().Be(initCount - i);
            }
            i.Should().Be(initCount);
            q.IsEmpty.Should().BeTrue();
            q.Count.Should().Be(0);

            mre1.Set();
            Thread.Sleep(10);
            i = 0;

            await foreach (var item in c)
            {
                item.Should().Be(objects[i++]);
                q.IsAddingCompleted.Should().BeFalse();
                c.IsCompleted.Should().BeFalse();
                if (i >= initCount) break;
                c.IsEmpty.Should().BeFalse();
                //q.Count.Should().Be(initCount - i);
            }
            i.Should().Be(initCount);
            c.IsEmpty.Should().BeTrue();
            q.Count.Should().Be(0);

            mre2.Set();
            i = 0;
            await foreach (var item in c)
            {
                item.Should().Be(objects[i++]);
                if (i >= initCount) break;
                c.IsCompleted.Should().BeFalse();
                c.IsEmpty.Should().BeFalse();
                //q.Count.Should().Be(initCount - i);
            }

            q.IsAddingCompleted.Should().BeTrue();
            q.IsCompleted.Should().BeTrue();

            i.Should().Be(initCount);

            q.IsEmpty.Should().BeTrue();
            q.Count.Should().Be(0);
        }
        finally
        {
            cts.Cancel();

            qt1?.Join();
            qt2?.Join();
        }
    }


    [Test]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public async Task MultipleQueueConsumption()
    {
        using var cts = new CancellationTokenSource();
        Thread? qt1 = null;
        Thread? qt2 = null;
        Thread? qt3 = null;
        try
        {
            const int objectsArrayPop = 1000;
            const int objectsArraySize = 2000;

            var objects1 = new object[objectsArraySize];
            var objects2 = new object[objectsArraySize];

            for (var o = 0; o < objectsArrayPop; ++o) objects1[o] = new();
            for (var o = 0; o < objectsArrayPop; ++o) objects2[o] = new();

            var comparer = Comparer<object>.Create((a, b) => a.GetHashCode().CompareTo(b.GetHashCode()));
            Array.Sort(objects1, 0, objectsArrayPop, comparer);
            Array.Sort(objects2, 0, objectsArrayPop, comparer);

            using var q1 = new AsyncProducerConsumerCollection<object>(objects1);
            using var q2 = new AsyncProducerConsumerCollection<object>(objects2);

            using var mre1 = new ManualResetEventSlim();

            qt1 = RunThread(() => {
                try { mre1.Wait(cts.Token); }
                catch (OperationCanceledException) { }
                mre1.Dispose();
                if (cts.IsCancellationRequested) return;
                q1.TryAddRange(objects1);
            });

            using var mre2 = new ManualResetEventSlim();

            qt2 = RunThread(() => {
                try { mre2.Wait(cts.Token); }
                catch (OperationCanceledException) { }
                mre2.Dispose();
                if (cts.IsCancellationRequested) return;
                q1.TryAddRange(objects1);
                q1.CompleteAdding();
            });

            qt3 = RunThread(() => {
                try { mre2.Wait(cts.Token); }
                catch (OperationCanceledException) { }
                mre2.Dispose();
                if (cts.IsCancellationRequested) return;
                q2.TryAddRange(objects2);
                q2.CompleteAdding();
            });

            var i = 0;
            var c1 = q1.GetConsumer();
            var c2 = q2.GetConsumer();

            using var c = FairAsyncCollectionsConsumer.Create(c1, c2);

            var last1 = -1;
            var last2 = -1;
            await foreach (var item in c)
            {
                ++i;
                if (i > objectsArraySize)
                    item.Should().BeNull();
                else
                {
                    item.Should().NotBeNull();
                    var odd = (i & 1) != 0;
                    if (odd)
                    {
                        var start = last1 == -1 ? 0 : last1;
                        var found = Array.IndexOf(objects1, item, start, objectsArrayPop - start);
                        found.Should().NotBe(-1);
                        found.Should().Be(last1 + 1);
                        last1 = found;
                    }
                    else
                    {
                        var start = last2 == -1 ? 0 : last2;
                        var found = Array.IndexOf(objects2, item, start, objectsArrayPop - start);
                        found.Should().NotBe(-1);
                        found.Should().Be(last2 + 1);
                        last2 = found;
                    }
                }
                q1.IsAddingCompleted.Should().BeFalse();
                q2.IsAddingCompleted.Should().BeFalse();

                if (i >= 4000) break;

                // ReSharper disable once VariableHidesOuterVariable
                c.WithLock(c => {
                    c.IsCompleted.Should().BeFalse();
                    c.IsEmpty.Should().BeFalse();
                });
                //q.Count.Should().Be(initCount - i);
            }
            // ReSharper disable once VariableHidesOuterVariable
            c.WithLock(c => {
                c.IsEmpty.Should().BeTrue();
            });
            q1.IsEmpty.Should().BeTrue();
            q2.IsEmpty.Should().BeTrue();

            mre1.Set();

            last1 = -1;
            await foreach (var item in c)
            {
                ++i;
                if (i > objectsArraySize * 2 + objectsArrayPop)
                    item.Should().BeNull();
                else
                {
                    item.Should().NotBeNull();
                    var start = last1 == -1 ? 0 : last1;
                    var found = Array.IndexOf(objects1, item, start, objectsArrayPop - start);
                    found.Should().NotBe(-1);
                    found.Should().Be(last1 + 1);
                    last1 = found;
                }
                if (i >= 6000) break;
                // ReSharper disable once VariableHidesOuterVariable
                c.WithLock(c => {
                    c.IsCompleted.Should().BeFalse();
                    c.IsEmpty.Should().BeFalse();
                });
            }
            // ReSharper disable once VariableHidesOuterVariable
            c.WithLock(c => {
                c.IsEmpty.Should().BeTrue();
            });
            q1.IsEmpty.Should().BeTrue();
            q2.IsEmpty.Should().BeTrue();

            mre2.Set();

            last1 = -1;
            last2 = -1;
            bool? lastWas1 = null;
            await foreach (var item in c)
            {
                ++i;
                if (i > objectsArraySize * 4)
                    item.Should().BeNull();
                else
                {
                    item.Should().NotBeNull();
                    if (lastWas1 is not null)
                    {
                        if (!lastWas1.Value)
                        {
                            var start = last1 == -1 ? 0 : last1;
                            var found = Array.IndexOf(objects1, item, start, objectsArrayPop - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last1 + 1);
                            last1 = found;
                            lastWas1 = true;
                        }
                        else
                        {
                            var start = last2 == -1 ? 0 : last2;
                            var found = Array.IndexOf(objects2, item, start, objectsArrayPop - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last2 + 1);
                            last2 = found;
                            lastWas1 = false;
                        }
                    }
                    else
                    {
                        var start = last1 == -1 ? 0 : last1;
                        var found = Array.IndexOf(objects1, item, start, objectsArrayPop - start);
                        if (found == -1)
                        {
                            found = Array.IndexOf(objects2, item, start, objectsArrayPop - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last2 + 1);
                            last2 = found;
                            lastWas1 = false;
                        }
                        else
                        {
                            found.Should().NotBe(-1);
                            found.Should().Be(last1 + 1);
                            last1 = found;
                            lastWas1 = true;
                        }
                    }
                }
                if (i >= objectsArraySize * 5) break;
                // ReSharper disable once VariableHidesOuterVariable
                c.WithLock(c => {
                    c.IsCompleted.Should().BeFalse();
                    c.IsEmpty.Should().BeFalse();
                });
            }
            // ReSharper disable once VariableHidesOuterVariable
            c.WithLock(c => {
                c.IsEmpty.Should().BeTrue();
            });
            q1.IsCompleted.Should().BeTrue();
            q2.IsCompleted.Should().BeTrue();
            q1.IsAddingCompleted.Should().BeTrue();
            q2.IsAddingCompleted.Should().BeTrue();
        }
        finally
        {
            cts.Cancel();

            qt1?.Join();
            qt2?.Join();
            qt3?.Join();
        }
    }


    [Test]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    public async Task UnevenQueueConsumption()
    {
        using var cts = new CancellationTokenSource();
        try
        {

            const int objects1Length = 600;
            const int objects2Length = 200;
            var objects1 = new object[objects1Length];
            var objects2 = new object[objects2Length];

            for (var o = 0; o < objects1Length; ++o) objects1[o] = new();
            for (var o = 0; o < objects2Length; ++o) objects2[o] = new();

            var comparer = Comparer<object>.Create((a, b) => a.GetHashCode().CompareTo(b.GetHashCode()));
            Array.Sort(objects1, 0, objects1Length, comparer);
            Array.Sort(objects2, 0, objects2Length, comparer);

            using var q1 = new AsyncProducerConsumerCollection<object>(objects1);
            using var q2 = new AsyncProducerConsumerCollection<object>(objects2);

            q1.CompleteAdding();
            q2.CompleteAdding();

            var i = 0;
            var c1 = q1.GetConsumer();
            var c2 = q2.GetConsumer();

            using var c = FairAsyncCollectionsConsumer.Create(c1, c2);

            // objects1Length + objects2Length
            //var seen = new SortedSet<object>(comparer);

            var last1 = -1;
            var last2 = -1;
            bool? lastWas1 = null;
            const int total = objects1Length + objects2Length;
            const int cut = (objects1Length > objects2Length ? objects2Length : objects1Length) * 2;
            await foreach (var item in c)
            {
                ++i;
                //TestContext.Out.WriteLine($"object {i}: H{item.GetHashCode()}, 1#{Array.IndexOf(objects1, item)}, 2#{Array.IndexOf(objects2, item)}");
                if (i > cut)
                {
                    item.Should().NotBeNull();
                    //seen.Add(item).Should().BeTrue();
                    var start = last1 == -1 ? 0 : last1;
                    var found = Array.IndexOf(objects1, item, start, objects1Length - start);
                    found.Should().NotBe(-1, $"object {i} was not found in objects1");
                    found.Should().Be(last1 + 1);
                    last1 = found;
                }
                else
                {
                    item.Should().NotBeNull();
                    //seen.Add(item).Should().BeTrue();
                    if (lastWas1 is not null)
                    {
                        if (!lastWas1.Value)
                        {
                            var start = last1 == -1 ? 0 : last1;
                            var found = Array.IndexOf(objects1, item, start, objects1Length - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last1 + 1);
                            last1 = found;
                            lastWas1 = true;
                        }
                        else
                        {
                            var start = last2 == -1 ? 0 : last2;
                            var found = Array.IndexOf(objects2, item, start, objects2Length - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last2 + 1);
                            last2 = found;
                            lastWas1 = false;
                        }
                    }
                    else
                    {
                        var start = last1 == -1 ? 0 : last1;
                        var found = Array.IndexOf(objects1, item, start, objects1Length - start);
                        if (found == -1)
                        {
                            found = Array.IndexOf(objects2, item, start, objects2Length - start);
                            found.Should().NotBe(-1);
                            found.Should().Be(last2 + 1);
                            last2 = found;
                            lastWas1 = false;
                        }
                        else
                        {
                            found.Should().NotBe(-1);
                            found.Should().Be(last1 + 1);
                            last1 = found;
                            lastWas1 = true;
                        }
                    }
                }
                if (i >= total) break;
                // ReSharper disable once VariableHidesOuterVariable
                c.WithLock(c => {
                    c.IsCompleted.Should().BeFalse();
                    c.IsEmpty.Should().BeFalse();
                });
            }
            // ReSharper disable once VariableHidesOuterVariable
            c.WithLock(c => {
                c.IsEmpty.Should().BeTrue();
            });
            q1.IsCompleted.Should().BeTrue();
            q2.IsCompleted.Should().BeTrue();
            q1.IsAddingCompleted.Should().BeTrue();
            q2.IsAddingCompleted.Should().BeTrue();
        }
        finally
        {
            cts.Cancel();
        }
    }
}
