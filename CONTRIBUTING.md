# Contributing

## Building and testing

```bash
dotnet build Dataverse.Fluent.slnx
dotnet test Dataverse.Fluent.Tests
dotnet test Dataverse.Fluent.Sales.Tests
dotnet test Dataverse.Fluent.Testing.Tests
```

The unit tests need no Dataverse environment and run on .NET 10 and .NET 11; install both SDKs, or pass
`-f net10.0`. The integration tests run against a live environment; see the
[integration test README](Dataverse.Fluent.IntegrationTests/README.md).

## Conventions

- **Warnings are errors** in the library projects, with the recommended .NET analyzers enabled. Fix a warning rather
  than suppressing it; a suppression needs a justification.
- **Every public member has XML documentation.**
- **The public API is pinned.** `PublicApiTests` compares each package's public surface with
  `PublicApi/*.approved.txt`. When you change the public API on purpose, the test writes a `.received.txt` file next
  to the test output; copy it over the approved file and describe the change in [CHANGELOG.md](CHANGELOG.md).
- **No metadata dependency.** The library never downloads or caches the environment's schema; behaviour follows from
  the POCO and the calls the developer makes. See [Design Philosophy](Dataverse.Fluent/docs/DESIGN_PHILOSOPHY.md).
- **Library code awaits with `ConfigureAwait(false)`** and takes a `CancellationToken` on every asynchronous method.
- **Tests assert on the request sent**, through `TestHttpMessageHandler.Requests` or a fake `IDataverseTransport`,
  rather than on mocks of the client.
- **Dataverse behaviour is verified, not assumed.** When a change depends on how Dataverse treats a request (headers,
  names, paging), cover it with an integration test, or check it against the environment's `$metadata`.

## Pull requests

CI builds the solution and runs the unit tests on Linux and Windows. Keep a pull request to one change, include tests,
and update the documentation and changelog for anything user-visible.

## Releasing

The packages share one version, `VersionPrefix` in [Directory.Build.props](Directory.Build.props). CI packages are
versioned `<VersionPrefix>-ci.<run>`; only the release workflow publishes.

1. On a branch, set `VersionPrefix` to the new version and change the top of [CHANGELOG.md](CHANGELOG.md) to
   `## <version> (<date>)`. Run the integration tests, then merge to main.
2. Tag the merged commit and push the tag:

   ```bash
   git checkout main && git pull
   git tag -a v1.1.0 -m "1.1.0"
   git push origin v1.1.0
   ```

[release.yml](.github/workflows/release.yml) then checks that the tag matches `VersionPrefix`, builds and runs the
unit tests, and pushes every package and its symbols to nuget.org. It does not check the changelog or that the tag is on
`main`, and it does not create a GitHub release: create that from the changelog section afterwards
(`gh release create v1.1.0 --notes-file <section>`). A version with a suffix (`1.1.0-beta.1`, tagged `v1.1.0-beta.1`)
publishes a prerelease; its changelog heading is `## 1.1.0-beta.1 (<date>)`.

nuget.org trusts the workflow through Trusted Publishing, so no API key is stored. The one-time setup is a Trusted
Publishing policy on nuget.org for this repository and `release.yml`, and a `NUGET_USER` repository secret holding the
nuget.org account name.
