# Integration Tests

These tests run the library against a live Microsoft Dataverse environment. Use a dedicated development or test
environment, never production. The unit tests in `Dataverse.Fluent.Tests` need no environment.

The project holds no credentials. Supply them through user secrets, environment variables or an untracked local file.

## Setup

### Configuration sources

Highest priority first:

1. Environment variables, prefix `DATAVERSE_`
2. User secrets (recommended for local development)
3. `appsettings.local.json` (git-ignored; start from `appsettings.local.json.example`)
4. `appsettings.json` (template with empty values)

| Setting | Required | Description |
|---------|----------|-------------|
| `Dataverse:EnvironmentUrl` | Yes | For example `https://yourorg.crm.dynamics.com` |
| `Dataverse:ApiVersion` | No | Defaults to `v9.2` |
| `Dataverse:AuthMode` | Yes | `ClientCredentials` or `InteractiveBrowser` |
| `Dataverse:TenantId` | Yes | Azure AD tenant |
| `Dataverse:ClientId` | Yes | App registration (client) id |
| `Dataverse:ClientSecret` | `ClientCredentials` only | Client secret |
| `Dataverse:AccountAlternateKeyColumn` | No | A text column on Account with a single-column alternate key. Without it the alternate-key tests are skipped; see [Environment-specific tests](#environment-specific-tests) |

### User secrets

The project already has a `UserSecretsId` (`fluent-dataverse-client-integration-tests`). Either run the helper script
(`setup-secrets.ps1` on Windows, `setup-secrets.sh` on Linux/macOS) from this folder, or set the values by hand:

```bash
cd Dataverse.Fluent.IntegrationTests

dotnet user-secrets set "Dataverse:EnvironmentUrl" "https://yourorg.crm.dynamics.com"
dotnet user-secrets set "Dataverse:AuthMode" "ClientCredentials"
dotnet user-secrets set "Dataverse:TenantId" "<tenant-id>"
dotnet user-secrets set "Dataverse:ClientId" "<client-id>"
dotnet user-secrets set "Dataverse:ClientSecret" "<client-secret>"
dotnet user-secrets set "Dataverse:AccountAlternateKeyColumn" "<column>"   # optional

dotnet user-secrets list     # verify
dotnet user-secrets remove "Dataverse:ClientSecret"   # remove one
dotnet user-secrets clear                              # remove all
```

The secrets file lives outside the repository, at `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` on
Windows and `~/.microsoft/usersecrets/<UserSecretsId>/secrets.json` elsewhere.

### Environment variables

The `DATAVERSE_` prefix is removed and `__` stands for `:`, so `DATAVERSE_Dataverse__EnvironmentUrl` sets
`Dataverse:EnvironmentUrl`.

```powershell
$env:DATAVERSE_Dataverse__EnvironmentUrl = "https://yourorg.crm.dynamics.com"
$env:DATAVERSE_Dataverse__AuthMode       = "ClientCredentials"
$env:DATAVERSE_Dataverse__TenantId       = "<tenant-id>"
$env:DATAVERSE_Dataverse__ClientId       = "<client-id>"
$env:DATAVERSE_Dataverse__ClientSecret   = "<client-secret>"
```

```bash
export DATAVERSE_Dataverse__EnvironmentUrl="https://yourorg.crm.dynamics.com"
export DATAVERSE_Dataverse__AuthMode="ClientCredentials"
export DATAVERSE_Dataverse__TenantId="<tenant-id>"
export DATAVERSE_Dataverse__ClientId="<client-id>"
export DATAVERSE_Dataverse__ClientSecret="<client-secret>"
```

### Azure AD app registration

**Client credentials (service principal)** is best for CI and unattended runs.

1. Register an application in Azure AD (Entra ID).
2. Under **Certificates & secrets**, create a client secret.
3. In Dataverse, create an **Application User** linked to the app (Power Platform admin center > environment >
   Settings > Users + permissions > Application users) and assign a security role with Create, Read, Write, Delete,
   Append and Append To privileges on the tables the tests use: Account, Contact, Note and Case, and for the Sales
   tests also Lead, Opportunity, Quote and Product.

**Interactive browser** is best for local development with your own account.

1. Register an application and, under **Authentication**, add the *Mobile and desktop applications* platform with the
   redirect URI `http://localhost`.
2. Add the **Dynamics CRM** *user_impersonation* delegated permission and grant consent.
3. Set `AuthMode` to `InteractiveBrowser`; no client secret is needed.

## Running the tests

```bash
dotnet test                                                         # everything
dotnet test --filter "FullyQualifiedName~QueryTests"                # one class
dotnet test --filter "FullyQualifiedName~QueryTests.CanQuery_Accounts_WithTop"   # one test
dotnet test -v detailed                                             # verbose output
dotnet test --filter "Requires!=AccountAlternateKey"                # leave out the alternate-key tests
```

A test that cannot run in the configured environment is reported as **skipped**, with the reason, rather than
passed. Read the skipped tests in the output to see what the run did not cover.

### CI

`.github/workflows/integration.yml` runs both integration suites when started by hand. It reads the repository
secrets `DATAVERSE_ENVIRONMENT_URL`, `DATAVERSE_TENANT_ID`, `DATAVERSE_CLIENT_ID` and `DATAVERSE_CLIENT_SECRET`, and
optionally `DATAVERSE_ACCOUNT_ALTERNATE_KEY_COLUMN`. Other CI systems pass the same settings as environment variables:

```yaml
# Azure DevOps (store the values in a variable group and mark the secret as secret)
- task: DotNetCoreCLI@2
  displayName: Integration tests
  inputs:
    command: test
    projects: Dataverse.Fluent.IntegrationTests
  env:
    DATAVERSE_Dataverse__EnvironmentUrl: $(DataverseUrl)
    DATAVERSE_Dataverse__TenantId: $(DataverseTenantId)
    DATAVERSE_Dataverse__ClientId: $(DataverseClientId)
    DATAVERSE_Dataverse__ClientSecret: $(DataverseClientSecret)
    DATAVERSE_Dataverse__AuthMode: ClientCredentials
```

## Test classes

All classes share one `DataverseTestFixture` through the `DataverseIntegrationTestCollection` collection, so they run
sequentially and share a single authenticated client.

| Class | Covers |
|-------|--------|
| `ConnectionTests` | Authentication, `WhoAmI`, reading system users |
| `QueryTests` | `$top`, `$select`, string and lambda filters, ordering, `$count`, streaming |
| `PagingAndFilterTests` | Paging, filter edge cases |
| `ExpandTests` | `$expand` on related records |
| `CrudTests` | Create, retrieve, update, delete, `SetFrom`/`SetFromExcept`, not-found handling |
| `BatchTests` | Batches of creates, updates, mixed operations, deep insert, continue-on-error, custom batch id, cancellation |
| `AssociationTests` | Associating and disassociating records |
| `DeepOperationsTests` | Deep insert of parents with children |
| `UpsertConflictResolutionTests` | Upsert, `PreventUpdate`, `PreventCreate`, `IfUnmodifiedSince` |
| `AnnotationTests` | Notes and file attachments |
| `FetchXmlTests` | Fluent and raw FetchXML, paging, options |
| `DeltaQueryTests` | Change tracking, deletions, streaming, token persistence |
| `ActionAndFunctionTests` | Bound and unbound actions and functions, error handling |
| `LookupMappingTests` | `[DataverseLookup]` binds on create, update and in batches; clearing a lookup; writing back an expanded record |

### Cleanup

Tests create their own records, with a GUID in the name. The fixture's client sends every request through
`RecordTrackingTransport`, which records each record a request creates: a POST to an entity set, a create-only
upsert, and the creates inside a `$batch`. When the run ends, the fixture deletes those records, newest first, in
batches. Tests do not need to clean up after themselves, although some still do.

Two kinds of record are not tracked:

- Related records created by a deep insert. They are removed with their parent when the relationship cascades deletes,
  as account → contact does.
- Records created by an action, such as the account, contact and opportunity from QualifyLead. The tests that call
  such actions delete what they create.

Nothing is deleted if a run is killed before the fixture is disposed, or if the application user lacks the delete
privilege. Remove such leftovers by name (for example `FDC-IT-<guid>` or `Test Account ... <guid>`), never by
creator: the application user may also create real records.

## Environment-specific tests

The tests use only standard tables, so they run against any Dataverse environment. Two groups need something an
environment does not have by default, and are skipped without it:

- **Alternate keys.** Account has no alternate key out of the box. The `AnnotationTests` marked
  `[Trait("Requires", "AccountAlternateKey")]` run when `Dataverse:AccountAlternateKeyColumn` names a text column with
  an active single-column alternate key on Account. If the setting names a column without an active key, the tests
  fail with a message saying so, rather than skipping.
- **Change tracking.** `DeltaQueryTests` need change tracking enabled on the Account table.

`BulkOperationTests.Performance_CreateMultiple_ComparedWithBatch` creates 1,500 accounts to compare `$batch` with
`CreateMultiple`, so it is skipped unless the environment variable `DATAVERSE_RUN_PERFORMANCE_TESTS` is `true`. It
writes the timings to the test output:

```bash
DATAVERSE_RUN_PERFORMANCE_TESTS=true dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"
```

Keep values that identify your environment, such as custom column names, in user secrets or CI secrets rather than
in the tests.

## Known issues and environment requirements

**Delta queries need change tracking.** Enable it on the Account table. Without it the server returns no delta token and
the `DeltaQueryTests` that need one are skipped. Dataverse also rejects `$top` and `$count` on change-tracked queries
and may reject `$filter` and `$orderby`, so the tests that use them are skipped when Dataverse rejects the option.
See [DELTA_QUERIES.md](../Dataverse.Fluent/docs/DELTA_QUERIES.md#limitations).

**Deep insert relationship names vary by environment.** Names such as `contact_customer_accounts` depend on the
environment and Dataverse version. If `DeepOperationsTests` fail with a navigation-property error, look up the
relationship schema name in the Power Apps maker portal or `$metadata` and update the test.

**ETags must come from Dataverse.** Dataverse rejects a fabricated ETag (for example one built from
`ModifiedOn.Ticks`) with HTTP 500 instead of 412, so the `IfUnmodifiedSince` tests read a real one with
`RetrieveWithMetadataAsync()`. See [UPSERT_CONFLICT_RESOLUTION.md](../Dataverse.Fluent/docs/UPSERT_CONFLICT_RESOLUTION.md).

**Request options need privileges.** The `RequestOptionsTests` that bypass business logic need
`prvBypassCustomBusinessLogic`, and the impersonation tests need `prvActOnBehalfOfAnotherUser` assigned directly to the
calling user, not through a team. An application user with the System Administrator role has both. Without them,
Dataverse rejects the requests and the tests fail.

**`If-Match: *` requires the record to exist.** This is Dataverse behaviour: a PATCH with `If-Match: *` returns 404 for a
missing record and never creates it. Use the library's upsert modes rather than setting the header by hand.

**Fixture errors.** If a test class fails with "constructor parameters did not have matching fixture data:
DataverseTestFixture fixture", add `[Collection("Dataverse Integration Tests")]` to the class.

**`SetFrom` and system fields.** Mark system-managed properties on test models with `[DataverseReadOnly]`; see
[SETFROM.md](../Dataverse.Fluent/docs/SETFROM.md).

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| "EnvironmentUrl is not configured" | Check `dotnet user-secrets list` from this folder, and that environment variables use the `DATAVERSE_` prefix |
| Secrets not loading | Confirm `UserSecretsId` in the `.csproj`, that `DataverseTestConfiguration` calls `AddUserSecrets`, and that nested keys use `:` |
| Authentication failure | Check the secret has not expired, admin consent was granted, and (client credentials) the Application User exists in the environment |
| 403 Forbidden | The Application User or account lacks a security role with the needed privileges |
| Leftover test records | Delete them by name; see [Cleanup](#cleanup) |

## Security

- Keep credentials in user secrets or CI secret stores, never in `appsettings.json` or source control.
- Use a dedicated test environment and a least-privilege service principal.
- Rotate the client secret regularly.
- Watch Dataverse API limits when running the whole suite repeatedly.

## References

- [Dataverse Web API](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/overview)
- [Safe storage of app secrets in development](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Authenticate with OAuth](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth)
