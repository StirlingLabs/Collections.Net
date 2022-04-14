using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace StirlingLabs.Utilities.Collections;

[SuppressMessage("ReSharper", "PossibleInterfaceMemberAmbiguity")]
public interface IAsyncConsumer<T> : IAsyncEnumerable<T>, IEnumerable<T>, IAsyncEnumerator<T>, IEnumerator<T>
{
    bool IsEmpty { get; }

    bool IsCompleted { get; }

    bool TryMoveNext(out T? item);

    ValueTask WaitForAvailableAsync(bool continueOnCapturedContext, CancellationToken cancellationToken);
}
