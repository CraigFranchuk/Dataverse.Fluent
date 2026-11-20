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
            "--no-select-check", "-c", "Release", "-f", "net10.0", "--no-build", "--api-version", "v9.1", "--verbose");

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
        Assert.True(settings.Verbose);
        Assert.False(settings.DryRun);
    }

    [Fact]
    public async Task TheProject_CanFollowTheOptions()
    {
        var (_, settings, _, _) = await RunAsync(
            "fix", "--url", "https://org.crm.dynamics.com", "--type", "Account", "--ignore", "DVS015", "src/App/App.csproj");

        Assert.Equal("src/App/App.csproj", settings!.Target);
        Assert.Equal(["Account"], settings.Types);
        Assert.Equal(["DVS015"], settings.Ignore);
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
    public async Task Scaffold_ReadsItsOptions()
    {
        ToolSettings? settings = null;
        var root = Cli.Build((_, _, _) => Task.FromResult(0), (s, _) =>
        {
            settings = s;
            return Task.FromResult(5);
        });

        var exit = await root.Parse(["scaffold", "--url", "https://org.crm.dynamics.com", "--table", "incident", "--columns", "title, customerid",
                "--columns", "duedate", "-o", "Models/Incident.cs", "--namespace", "App.Models", "--class", "Case", "--enums", "--dry-run"])
            .InvokeAsync(new InvocationConfiguration { Error = new StringWriter(), Output = new StringWriter() }, TestContext.Current.CancellationToken);

        Assert.Equal(5, exit);
        Assert.NotNull(settings);
        Assert.Equal(("incident", "Models/Incident.cs", "App.Models", "Case"), (settings.Table, settings.Output, settings.Namespace, settings.ClassName));
        Assert.Equal(["title", "customerid", "duedate"], settings.Columns);
        Assert.True(settings.Enums);
        Assert.True(settings.DryRun);
        Assert.Equal(".", settings.Target);
    }

    [Fact]
    public async Task Scaffold_TakesAProject_AndModelNames()
    {
        ToolSettings? settings = null;
        var root = Cli.Build((_, _, _) => Task.FromResult(0), (s, _) =>
        {
            settings = s;
            return Task.FromResult(0);
        });

        await root.Parse(["scaffold", "--url", "https://org.crm.dynamics.com", "-t", "Incident", "src/App"])
            .InvokeAsync(new InvocationConfiguration { Error = new StringWriter(), Output = new StringWriter() }, TestContext.Current.CancellationToken);

        Assert.Equal("src/App", settings!.Target);
        Assert.Equal(["Incident"], settings.Types);
        Assert.Null(settings.Table);
    }

    [Fact]
    public void DryRun_IsAnOptionOfFixOnly()
    {
        var root = Cli.Build((_, _, _) => Task.FromResult(0));

        Assert.DoesNotContain(root.Subcommands.Single(c => c.Name == "validate").Options, o => o.Name == "--dry-run");
        Assert.Contains(root.Subcommands.Single(c => c.Name == "fix").Options, o => o.Name == "--dry-run");
    }

    [Fact]
    public async Task Force_IsAnOptionOfTheCommandsThatWrite()
    {
        ToolSettings? scaffold = null;
        var root = Cli.Build((_, _, _) => Task.FromResult(0), (s, _) =>
        {
            scaffold = s;
            return Task.FromResult(0);
        });

        Assert.DoesNotContain(root.Subcommands.Single(c => c.Name == "validate").Options, o => o.Name == "--force");

        var (_, fix, _, _) = await RunAsync("fix", "--url", "https://org.crm.dynamics.com", "--force");
        Assert.True(fix!.Force);
        (_, fix, _, _) = await RunAsync("fix", "--url", "https://org.crm.dynamics.com");
        Assert.False(fix!.Force);

        await root.Parse(["scaffold", "--url", "https://org.crm.dynamics.com", "--force", "--skip", "Incident.Summary", "--skip", "Incident.Scratch"])
            .InvokeAsync(new InvocationConfiguration { Error = new StringWriter(), Output = new StringWriter() }, TestContext.Current.CancellationToken);
        Assert.True(scaffold!.Force);
        Assert.Equal(["Incident.Summary", "Incident.Scratch"], scaffold.Skip);
    }
}
