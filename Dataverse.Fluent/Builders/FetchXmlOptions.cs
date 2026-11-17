namespace Dataverse.Fluent.Builders;

/// <summary>
/// Options for FetchXML execution.
/// </summary>
public class FetchXmlOptions
{
    /// <summary>
    /// Whether to use no-lock hint to avoid read locks on records.
    /// </summary>
    public bool NoLock { get; set; }

    /// <summary>
    /// Whether to include formatted values in the response.
    /// </summary>
    public bool IncludeFormattedValues { get; set; }

    /// <summary>
    /// Maximum page size for pagination (default: 5000, max allowed by Dataverse).
    /// </summary>
    public int MaxPageSize { get; set; } = 5000;

    /// <summary>
    /// Creates default options.
    /// </summary>
    public static FetchXmlOptions Default => new();
}
