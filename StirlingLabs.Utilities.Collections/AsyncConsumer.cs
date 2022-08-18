namespace StirlingLabs.Utilities.Collections;

public static class AsyncConsumer<T>
{
    public static IAsyncConsumer<T> Empty { get; } = new EmptyAsyncConsumer<T>();
}
