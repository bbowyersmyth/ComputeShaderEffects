using System;

public static class IDisposableExtensions
{
    public static void DisposeIfNotNull(this IDisposable disposable)
    {
        if (disposable != null)
            disposable.Dispose();
    }
}
