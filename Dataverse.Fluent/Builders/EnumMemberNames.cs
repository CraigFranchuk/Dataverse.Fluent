namespace Dataverse.Fluent.Builders;

/// <summary>
/// Writes a C# enum as the member names of the Dataverse enum (<c>EntityFilters</c>, <c>AccessRights</c>, ...) that
/// function and action parameters take. The library has no metadata, so the C# enum's member names must match
/// Dataverse's; a <c>[Flags]</c> combination is written as its members joined with <c>", "</c>.
/// </summary>
/// <remarks>
/// Not for choice (option set) columns, which Dataverse reads by their integer value.
/// </remarks>
internal static class EnumMemberNames
{
    /// <summary>The member name(s) of <paramref name="value"/>, e.g. <c>ReadAccess</c> or <c>ReadAccess, WriteAccess</c>.</summary>
    /// <exception cref="ArgumentException">The value is not a defined member or a combination of <c>[Flags]</c> members.</exception>
    public static string Format(Enum value)
    {
        var members = value.ToString();
        if (members.Length > 0 && (char.IsDigit(members[0]) || members[0] == '-'))
        {
            throw new ArgumentException(
                $"{value.GetType().Name} has no member for the value {members}. A Dataverse enum parameter is sent by member " +
                "name, so the value must be a defined member or a combination of [Flags] members.",
                nameof(value));
        }

        return members;
    }
}
