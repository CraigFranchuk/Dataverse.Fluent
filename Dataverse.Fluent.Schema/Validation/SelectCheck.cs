using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dataverse.Fluent.Models;
using Dataverse.Fluent.Schema.Metadata;

namespace Dataverse.Fluent.Schema.Validation;

/// <summary>
/// Retrieves a random ID with every mapped column in <c>$select</c>. Dataverse checks the <c>$select</c> before it looks
/// for the record: a 404 proves every column can be selected, and a 400 names the first one that cannot
/// ("Could not find a property named 'x' on type 'Microsoft.Dynamics.CRM.account'"). That one is removed and the request
/// sent again; a 400 that names no column is narrowed down by halving the list.
/// </summary>
internal sealed partial class SelectCheck
{
    // A direct GET fails above about 29,700 characters (see LongUrlTests); stay far below.
    private const int MaxSelectLength = 6000;

    private readonly SchemaReader _reader;
    private readonly ModelChecker _checker;
    private Dictionary<string, DataverseMemberMapping> _members = [];
    private bool _stopped;

    public SelectCheck(SchemaReader reader, ModelChecker checker)
    {
        _reader = reader;
        _checker = checker;
    }

    public async Task RunAsync(TableSchema table, DataverseEntityMapping mapping, IReadOnlySet<string> knownBad, CancellationToken ct)
    {
        _members = mapping.Members
            .Where(m => m.Kind is DataverseMemberKind.Column or DataverseMemberKind.Lookup or DataverseMemberKind.PolymorphicLookup
                && !m.Name.Contains('@', StringComparison.Ordinal) && !knownBad.Contains(m.Name))
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var chunk in Chunk(_members.Keys))
        {
            await CheckAsync(table.EntitySetName, chunk, ct).ConfigureAwait(false);
            if (_stopped)
                return;
        }
    }

    private async Task CheckAsync(string entitySet, List<string> names, CancellationToken ct)
    {
        while (names.Count > 0 && !_stopped)
        {
            try
            {
                await _reader.GetAsync($"{entitySet}({Guid.NewGuid()})?$select={string.Join(",", names)}", ct).ConfigureAwait(false);
                return;
            }
            catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
            catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                var named = UnknownProperty().Match(ex.Message) is { Success: true } match ? match.Groups[1].Value : null;
                if (named is not null && names.Remove(named))
                {
                    Reject(named, ex.Message);
                    continue;
                }

                if (names.Count == 1)
                {
                    Reject(names[0], ex.Message);
                    return;
                }

                var half = names.Count / 2;
                await CheckAsync(entitySet, names[..half], ct).ConfigureAwait(false);
                await CheckAsync(entitySet, names[half..], ct).ConfigureAwait(false);
                return;
            }
            catch (DataverseException ex)
            {
                _stopped = true;
                _checker.Add(SchemaCodes.SelectCheckSkipped, SchemaSeverity.Info, null,
                    $"The live $select check could not run: {(int)ex.StatusCode} {ex.Message}");
                return;
            }
        }
    }

    private void Reject(string name, string message)
    {
        var member = _members[name];
        _checker.Add(SchemaCodes.RejectedBySelect, SchemaSeverity.Error, member.Property,
            $"Dataverse rejects '{name}' in a $select: {TrimErrorCode(message)}");
    }

    private static IEnumerable<List<string>> Chunk(IEnumerable<string> names)
    {
        var chunk = new List<string>();
        var length = 0;
        foreach (var name in names)
        {
            if (chunk.Count > 0 && length + name.Length + 1 > MaxSelectLength)
            {
                yield return chunk;
                chunk = [];
                length = 0;
            }

            chunk.Add(name);
            length += name.Length + 1;
        }

        if (chunk.Count > 0)
            yield return chunk;
    }

    private static string TrimErrorCode(string message)
    {
        var at = message.LastIndexOf(" (ErrorCode:", StringComparison.Ordinal);
        var text = new StringBuilder(at > 0 ? message[..at] : message);
        return text.ToString();
    }

    [GeneratedRegex("property named '([^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex UnknownProperty();
}
