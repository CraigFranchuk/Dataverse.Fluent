namespace Dataverse.Fluent.Testing.Tests;

/// <summary>The examples in the package README, run as written.</summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task Onboarding_CreatesAContactForTheAccount()
    {
        var accountId = Guid.NewGuid();
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities([new Account { AccountId = accountId, Name = "Contoso" }]);
        fake.OnPost("contacts").RespondWithCreated();

        var service = new OnboardingService(fake.CreateClient());
        await service.OnboardAsync("Contoso", "Smith");

        var query = fake.Requests[0];
        Assert.Equal("name eq 'Contoso'", query.Query["$filter"]);

        var create = fake.Requests[1];
        Assert.Equal("Smith", (string?)create.Payload!["lastname"]);
        Assert.Equal($"/accounts({accountId})", (string?)create.Payload["parentcustomerid_account@odata.bind"]);
    }

    [Fact]
    public async Task When_WithGetQueryOption_SkipsRequestsWithoutTheOption()
    {
        var fake = new FakeDataverseTransport();
        fake.OnGet("accounts").RespondWithEntities([new Account { Name = "All" }]);
        fake.OnGet("accounts")
            .When(r => r.GetQueryOption("$filter")?.Contains("revenue", StringComparison.Ordinal) == true)
            .RespondWithEntities([new Account { Name = "Large" }]);

        var client = fake.CreateClient();

        Assert.Equal("All", Assert.Single(await client.Query<Account>().GetListAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Equal("Large", Assert.Single(await client.Query<Account>().Where(a => a.Revenue > 5).GetListAsync(TestContext.Current.CancellationToken)).Name);
        Assert.Null(fake.Requests[0].GetQueryOption("$filter"));
    }

    private sealed class OnboardingService(IDataverseClient client)
    {
        public async Task OnboardAsync(string accountName, string lastName)
        {
            var account = (await client.Query<Account>().Where(a => a.Name == accountName).Top(1).GetListAsync()).Single();

            await client.Entity<Contact>()
                .SetFrom(new Contact { LastName = lastName, ParentCustomerId = DataverseReference.To<Account>(account.AccountId) })
                .CreateAsync();
        }
    }
}
