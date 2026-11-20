using System.CommandLine;

namespace Dataverse.Fluent.Tool.Tests;

/// <summary>The command line turns arguments into <see cref="ToolSettings"/>.</summary>
public class CliTests
{
    private static async Task<(int Exit, ToolSettings? Settings, bool Fix, string Error)> RunAsync(params string[] args)
    {
        ToolSettings? captured = null;
        var fix = false;
        var root = Cli.Build((settings, isFix, _) =>
        {
            captured = settings;
            fix = isFix;
            return Task.FromResult(7);
        });

        var error = new StringWriter();
        var exit = await root.Parse(args).InvokeAsync(new InvocationConfiguration { Error = error, Output = new StringWriter() }, TestContext.Current.CancellationToken);
        return (exit, captured, fix, error.ToString());
    }

    [Fact]
    public async Task Validate_ReadsEveryOption()
    {
        var (exit, settings, fix, _) = await RunAsync(
            "validate", "src/App/App.csproj", "--url", "https://org.crm.dynamics.com/", "--interactive", "--tenant-id", "t",
            "--type", "Account", "--type", "Contact", "--resolver", "My.Resolver", "--ignore", "DVS015", "--warnings-as-errors",
            "--no-select-check", "-c", "Release", "-f", "net10.0", "--no-build", "--api-version", "v9.1");

        Assert.Equal(7, exit);
        Assert.False(fix);
        Assert.NotNull(settings);
        Assert.Equal("src/App/App.csproj", settings.Target);
        Assert.Equal("https://org.crm.dynamics.com/api/data/v9.1", settings.WebApiUrl);
        Assert.True(settings.Interactive);
        Assert.Equal("t", settings.TenantId);
        Assert.Equal(["Account", "Contact"], settings.Types);
        Assert.Equal("My.Resolver", settings.Resolver);
        Assert.Equal(["DVS015"], settings.Ignore);
        Assert.True(settings.WarningsAsErrors);
        Assert.True(settings.NoSelectCheck);
        Assert.Equal(("Release", "net10.0"), (settings.Configuration, settings.Framework));
        Assert.True(settings.NoBuild);
        Assert.False(settings.DryRun);
    }

    [Fact]
    public async Task Fix_TakesDryRun_AndDefaultsToTheCurrentDirectory()
    {
        var (_, settings, fix, _) = await RunAsync("fix", "--url", "https://org.crm.dynamics.com", "--dry-run");

        Assert.True(fix);
        Assert.Equal(".", settings!.Target);
        Assert.True(settings.DryRun);
        Assert.Equal("Debug", settings.Configuration);
        Assert.Equal("https://org.crm.dynamics.com/api/data/v9.2", settings.WebApiUrl);
    }

    [Fact]
    public async Task AWebApiUrl_IsUsedAsItIs()
    {
        var (_, settings, _, _) = await RunAsync("validate", "--url", "https://org.crm.dynamics.com/api/data/v9.0");

        Assert.Equal("https://org.crm.dynamics.com/api/data/v9.0", settings!.WebApiUrl);
    }

    [Fact]
    public async Task AnHttpUrl_IsRejected()
    {
        var (exit, settings, _, error) = await RunAsync("validate", "--url", "http://org.crm.dynamics.com");

        Assert.Equal(ExitCodes.Failure, exit);
        Assert.Null(settings);
        Assert.Contains("is not an https URL", error);
    }

    [Fact]
    public void DryRun_IsAnOptionOfFixOnly()
    {
        var root = Cli.Build((_, _, _) => Task.FromResult(0));

        Assert.DoesNotContain(root.Subcommands.Single(c => c.Name == "validate").Options, o => o.Name == "--dry-run");
        Assert.Contains(root.Subcommands.Single(c => c.Name == "fix").Options, o => o.Name == "--dry-run");
    }
}
