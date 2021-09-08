using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Common;
using JetBrains.Annotations;
using NUnit.Framework;
using StirlingLabs.Utilities.Collections;

namespace StirlingLabs.Utilities.Collections.Tests
{
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
        public async Task GeneralQueueOperations()
        {
            var objects = new object[8000];

            for (var o = 0; o < 1000; ++o) objects[o] = new();

            using var q = new AsyncProducerConsumerCollection<object>(objects);

            q.IsEmpty.Should().BeFalse();
            var initCount = objects.Length;
            q.Count.Should().Be(initCount);

            using var mre1 = new ManualResetEventSlim();

            var qt1 = RunThread(() => {
                mre1.Wait();
                mre1.Dispose();
                q.TryAdd(objects.First());
                q.TryAddRange(objects.Skip(1));
            });

            using var mre2 = new ManualResetEventSlim();

            var qt2 = RunThread(() => {
                mre2.Wait();
                mre2.Dispose();
                q.TryAddRange(objects);
                q.CompleteAdding();
            });

            var i = 0;
            var c = q.GetConsumer();
            c.Should().NotBeNull();

            c.Equals(c).Should().BeTrue();

            c.Equals((object)c).Should().BeTrue();

            c.GetHashCode().Should().Be(q.GetHashCode());

            // ReSharper disable once EqualExpressionComparison
            (c == c).Should().BeTrue();

            // ReSharper disable once EqualExpressionComparison
            (c != c).Should().BeFalse();

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

            qt1.Join();
            qt2.Join();
        }
    }
}
