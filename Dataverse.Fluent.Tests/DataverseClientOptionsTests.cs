namespace Dataverse.Fluent.Tests;

/// <summary>
/// Tests the validation rules of <see cref="DataverseClientOptions"/>.
/// </summary>
public class DataverseClientOptionsTests
{
    private const string BaseUrl = "https://test.crm.dynamics.com/api/data/v9.2";

    private static DataverseClientOptions Valid() => new()
    {
        BaseUrl = BaseUrl,
        TokenProvider = () => Task.FromResult("t")
    };

    public static TheoryData<string, string> InvalidOptions => new()
    {
        { "no base url", "BaseUrl is required" },
        { "blank base url", "BaseUrl is required" },
        { "http base url", "absolute HTTPS URL" },
        { "relative base url", "absolute HTTPS URL" },
        { "no authentication", "Either Credential or TokenProvider must be set" },
        { "both authentication methods", "not both" },
        { "null resilience", "Resilience must not be null" },
        { "negative retries", "MaxRetryAttempts must be zero or greater" },
        { "zero attempt timeout", "AttemptTimeout must be positive" },
        { "total equal to attempt timeout", "TotalRequestTimeout must be greater than" },
        { "negative retry delay", "MedianFirstRetryDelay must not be negative" },
    };

    private static DataverseClientOptions Configure(string scenario)
    {
        var options = Valid();
        switch (scenario)
        {
            case "no base url": options.BaseUrl = null!; break;
            case "blank base url": options.BaseUrl = "  "; break;
            case "http base url": options.BaseUrl = "http://test.crm.dynamics.com/api/data/v9.2"; break;
            case "relative base url": options.BaseUrl = "/api/data/v9.2"; break;
            case "no authentication": options.TokenProvider = null; break;
            case "both authentication methods": options.Credential = new FakeTokenCredential(TimeProvider.System, TimeSpan.FromHours(1)); break;
            case "null resilience": options.Resilience = null!; break;
            case "negative retries": options.Resilience.MaxRetryAttempts = -1; break;
            case "zero attempt timeout": options.Resilience.AttemptTimeout = TimeSpan.Zero; break;
            case "total equal to attempt timeout": options.Resilience.TotalRequestTimeout = options.Resilience.AttemptTimeout; break;
            case "negative retry delay": options.Resilience.MedianFirstRetryDelay = TimeSpan.FromSeconds(-1); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }
        return options;
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void InvalidOptions_AreReported(string scenario, string expectedError)
    {
        var errors = Configure(scenario).GetValidationErrors(requireAuthentication: true);

        Assert.Contains(errors, e => e.Contains(expectedError));
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void Constructor_WithInvalidOptions_Throws(string scenario, string expectedError)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => new DataverseClient(Configure(scenario)));

        Assert.Contains(expectedError, ex.Message);
    }

    [Fact]
    public void ValidOptions_HaveNoErrors()
    {
        Assert.Empty(Valid().GetValidationErrors(requireAuthentication: true));
    }

    [Fact]
    public void SeveralProblems_AreAllReported()
    {
        var options = new DataverseClientOptions { BaseUrl = "http://x" };
        options.Resilience.MaxRetryAttempts = -1;

        var errors = options.GetValidationErrors(requireAuthentication: true);

        Assert.Equal(3, errors.Count); // HTTPS, authentication, retries
    }

    [Fact]
    public void DisabledResilience_IgnoresTheResilienceSettings()
    {
        var options = Valid();
        options.Resilience.Enabled = false;
        options.Resilience.MaxRetryAttempts = -1;
        options.Resilience.AttemptTimeout = TimeSpan.Zero;

        Assert.Empty(options.GetValidationErrors(requireAuthentication: true));
    }

    [Fact]
    public void WithoutRequiredAuthentication_MissingCredentialsAreAllowed()
    {
        // The DI path authenticates in the HttpClient pipeline, so the transport only needs the URL.
        var options = new DataverseClientOptions { BaseUrl = BaseUrl };

        Assert.Empty(options.GetValidationErrors(requireAuthentication: false));
    }
}
