using System.Collections.Generic;

namespace StirlingLabs.Utilities.Collections
{
    public interface IAsyncConsumer<out T> : IAsyncEnumerable<T>, IEnumerable<T>
    {
        bool IsEmpty { get; }

        bool IsCompleted { get; }
    }
}
