using Dataverse.Fluent.Tool;
using Dataverse.Fluent.Tool.Projects;

var output = Console.Out;
var runner = new ToolRunner(output, new DotnetProjectResolver(output), ToolRunner.Connect);
var root = Cli.Build((settings, fix, ct) => fix ? runner.FixAsync(settings, ct) : runner.ValidateAsync(settings, ct), runner.ScaffoldAsync);

return await root.Parse(args).InvokeAsync().ConfigureAwait(false);
