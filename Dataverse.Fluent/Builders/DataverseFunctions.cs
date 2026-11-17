namespace Dataverse.Fluent.Builders;

/// <summary>
/// Fluent escape hatches for Dataverse-specific OData query functions
/// (the <c>Microsoft.Dynamics.CRM.*</c> functions) that have no C# language equivalent,
/// such as hierarchy (<see cref="Above"/>, <see cref="Under"/>), security
/// (<see cref="EqualUserId"/>), and relative-date (<see cref="LastXDays"/>) filters.
/// </summary>
/// <remarks>
/// These methods are markers intended to be used <b>only</b> inside a <c>Where</c> lambda
/// (e.g., <c>q.Where(x =&gt; DataverseFunctions.EqualUserId(x.OwnerId))</c>). The expression
/// tree is translated to OData by <see cref="ODataExpressionVisitor"/>; they are never executed
/// directly and will throw <see cref="NotSupportedException"/> if invoked at runtime.
/// </remarks>
/// <example>
/// <code>
/// // WHERE owner is the current user AND record is under a parent in the hierarchy
/// var rows = await client.Query&lt;Account&gt;()
///     .Where(x =&gt; DataverseFunctions.EqualUserId(x.OwnerId)
///             &amp;&amp; DataverseFunctions.Under(x.ParentAccountId, parentId))
///     .GetListAsync();
/// </code>
/// </example>
public static class DataverseFunctions
{
    private const string DirectCallError =
        "Dataverse query functions are markers for use inside a Where(...) lambda expression " +
        "and cannot be invoked directly.";

    private static bool Throw() => throw new NotSupportedException(DirectCallError);

    // ────────── Hierarchy functions ──────────

    /// <summary>Matches records above the referenced record in the hierarchy.</summary>
    public static bool Above(object? property, Guid value) => Throw();

    /// <summary>Matches the referenced record and records above it in the hierarchy.</summary>
    public static bool AboveOrEqual(object? property, Guid value) => Throw();

    /// <summary>Matches child records below the referenced record in the hierarchy.</summary>
    public static bool Under(object? property, Guid value) => Throw();

    /// <summary>Matches the referenced record and child records below it in the hierarchy.</summary>
    public static bool UnderOrEqual(object? property, Guid value) => Throw();

    /// <summary>Matches records that are not below the referenced record in the hierarchy.</summary>
    public static bool NotUnder(object? property, Guid value) => Throw();

    // ────────── Security / identity functions (no value) ──────────

    /// <summary>Matches records where the column equals the current user's id.</summary>
    public static bool EqualUserId(object? property) => Throw();

    /// <summary>Matches records where the column does not equal the current user's id.</summary>
    public static bool NotEqualUserId(object? property) => Throw();

    /// <summary>Matches records owned by the teams the current user is a member of.</summary>
    public static bool EqualUserTeams(object? property) => Throw();

    /// <summary>Matches records owned by the current user or the teams they are a member of.</summary>
    public static bool EqualUserOrUserTeams(object? property) => Throw();

    /// <summary>Matches records where the column equals the current user's business unit id.</summary>
    public static bool EqualBusinessId(object? property) => Throw();

    /// <summary>Matches records where the column does not equal the current user's business unit id.</summary>
    public static bool NotEqualBusinessId(object? property) => Throw();

    // ────────── Relative date functions (no value) ──────────

    /// <summary>Matches records where the date column falls today.</summary>
    public static bool Today(object? property) => Throw();

    /// <summary>Matches records where the date column falls yesterday.</summary>
    public static bool Yesterday(object? property) => Throw();

    /// <summary>Matches records where the date column falls tomorrow.</summary>
    public static bool Tomorrow(object? property) => Throw();

    /// <summary>Matches records where the date column falls in the current week.</summary>
    public static bool ThisWeek(object? property) => Throw();

    /// <summary>Matches records where the date column falls in the current month.</summary>
    public static bool ThisMonth(object? property) => Throw();

    /// <summary>Matches records where the date column falls in the current year.</summary>
    public static bool ThisYear(object? property) => Throw();

    // ────────── Relative date functions (with count) ──────────

    /// <summary>Matches records where the date column falls within the last <paramref name="count"/> days.</summary>
    public static bool LastXDays(object? property, int count) => Throw();

    /// <summary>Matches records where the date column falls within the next <paramref name="count"/> days.</summary>
    public static bool NextXDays(object? property, int count) => Throw();

    /// <summary>Matches records where the date column is older than <paramref name="count"/> days.</summary>
    public static bool OlderThanXDays(object? property, int count) => Throw();

    /// <summary>Matches records where the date column is older than <paramref name="count"/> months.</summary>
    public static bool OlderThanXMonths(object? property, int count) => Throw();

    /// <summary>Matches records where the date column is older than <paramref name="count"/> years.</summary>
    public static bool OlderThanXYears(object? property, int count) => Throw();

    // ────────── Set membership functions ──────────

    /// <summary>Matches records where the column value is in the supplied set.</summary>
    public static bool In(object? property, params object[] values) => Throw();

    /// <summary>Matches records where the column value is not in the supplied set.</summary>
    public static bool NotIn(object? property, params object[] values) => Throw();

    /// <summary>Matches records where the multi-select option set column contains the supplied values.</summary>
    public static bool ContainValues(object? property, params object[] values) => Throw();

    /// <summary>Matches records where the multi-select option set column does not contain the supplied values.</summary>
    public static bool DoesNotContainValues(object? property, params object[] values) => Throw();
}
