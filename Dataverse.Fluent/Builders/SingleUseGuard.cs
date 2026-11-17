namespace Dataverse.Fluent.Builders;

/// <summary>
/// Lets a builder be executed once. A builder holds the state of one request; running it twice would
/// silently repeat a write or reuse options meant for another call.
/// </summary>
/// <remarks>
/// A mutable struct: declare the field without <see langword="readonly"/>.
/// </remarks>
internal struct SingleUseGuard
{
    private int _used;

    /// <summary>
    /// Marks the builder as used. Throws <see cref="InvalidOperationException"/> when it already was.
    /// </summary>
    public void Use()
    {
        if (Interlocked.CompareExchange(ref _used, 1, 0) != 0)
            throw new InvalidOperationException(
                "This builder instance has already been executed and cannot be reused. Create a new builder for each operation.");
    }
}
