using System.Text;
using Dataverse.Fluent.IntegrationTests.Models;
using Dataverse.Fluent.Models;

namespace Dataverse.Fluent.IntegrationTests;

[Collection("Dataverse Integration Tests")]
public class AnnotationTests : IAsyncLifetime
{
    private readonly DataverseTestFixture _fixture;
    private readonly List<Guid> _createdAccountIds = new();
    private readonly List<Guid> _createdAnnotationIds = new();

    public AnnotationTests(DataverseTestFixture fixture)
    {
        _fixture = fixture;
    }

    #region Basic CRUD Tests

    [Fact]
    public async Task CanCreate_AnnotationWithoutFile()
    {
        // Arrange: Create a test account first
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        // Act: Create annotation without file
        var annotation = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Test Note")
            .WithNoteText("This is a test note without a file attachment")
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Test Note", annotation.Subject);
        Assert.Equal("This is a test note without a file attachment", annotation.NoteText);
        Assert.Null(annotation.FileName);
        Assert.False(annotation.IsDocument ?? false);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    public async Task CanCreate_AnnotationWithTextFile()
    {
        // Arrange: Create a test account
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var fileContent = Encoding.UTF8.GetBytes("This is a test file content.\nLine 2 of the test file.");
        var fileName = $"test-{Guid.NewGuid()}.txt";

        // Act: Create annotation with text file
        var annotation = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Test Document")
            .WithNoteText("Test document attachment")
            .WithFile(fileName, fileContent)
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Test Document", annotation.Subject);
        Assert.Equal(fileName, annotation.FileName);
        Assert.Equal("text/plain", annotation.MimeType);
        Assert.True(annotation.IsDocument);
        Assert.NotNull(annotation.FileSize);
        Assert.True(annotation.FileSize > 0);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    public async Task CanCreate_AnnotationWithPdfFile()
    {
        // Arrange: Create a test account
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        // Create a simple PDF-like content (not a real PDF, just for testing)
        var fileContent = Encoding.UTF8.GetBytes("%PDF-1.4\nFake PDF content for testing");
        var fileName = $"document-{Guid.NewGuid()}.pdf";

        // Act: Create annotation with PDF file
        var annotation = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("PDF Document")
            .WithFile(fileName, fileContent, "application/pdf")
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.Equal(fileName, annotation.FileName);
        Assert.Equal("application/pdf", annotation.MimeType);
        Assert.True(annotation.IsDocument);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    public async Task CanCreate_AnnotationWithStream()
    {
        // Arrange: Create a test account
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var fileContent = Encoding.UTF8.GetBytes("Stream content test");
        var fileName = $"stream-test-{Guid.NewGuid()}.txt";

        // Act: Create annotation using stream
        using var stream = new MemoryStream(fileContent);
        var annotation = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Stream Test")
            .WithFile(fileName, stream)
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.Equal(fileName, annotation.FileName);
        Assert.True(annotation.IsDocument);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    public async Task CanCreate_AnnotationWithStreamAsync()
    {
        // Arrange: Create a test account
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var fileContent = Encoding.UTF8.GetBytes("Async stream content test");
        var fileName = $"async-stream-{Guid.NewGuid()}.txt";

        // Act: Create annotation using async stream
        using var stream = new MemoryStream(fileContent);
        var builder = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Async Stream Test")
            .WithFileAsync(fileName, stream);

        var annotation = await builder
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.Equal(fileName, annotation.FileName);
        Assert.True(annotation.IsDocument);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    public async Task CanRetrieve_Annotation()
    {
        // Arrange: Create annotation
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Retrieve Test")
            .WithNoteText("Test retrieval")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Retrieve annotation
        var retrieved = await _fixture.Client.Annotation()
            .RetrieveAsync(created.AnnotationId);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(created.AnnotationId, retrieved.AnnotationId);
        Assert.Equal("Retrieve Test", retrieved.Subject);
        Assert.Equal("Test retrieval", retrieved.NoteText);
    }

    [Fact]
    public async Task CanRetrieve_AnnotationWithWithId()
    {
        // Arrange: Create annotation
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Retrieve WithId Test")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Retrieve using WithId
        var retrieved = await _fixture.Client.Annotation()
            .WithId(created.AnnotationId)
            .RetrieveAsync();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(created.AnnotationId, retrieved.AnnotationId);
        Assert.Equal("Retrieve WithId Test", retrieved.Subject);
    }

    [Fact]
    public async Task CanUpdate_AnnotationSubject()
    {
        // Arrange: Create annotation
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Original Subject")
            .WithNoteText("Original text")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Update annotation
        await _fixture.Client.Annotation()
            .WithId(created.AnnotationId)
            .WithSubject("Updated Subject")
            .WithNoteText("Updated text")
            .UpdateAsync();

        // Assert: Retrieve and verify
        var updated = await _fixture.Client.Annotation()
            .RetrieveAsync(created.AnnotationId);

        Assert.Equal("Updated Subject", updated.Subject);
        Assert.Equal("Updated text", updated.NoteText);
    }

    [Fact]
    public async Task CanUpdate_AnnotationFile()
    {
        // Arrange: Create annotation with file
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var originalContent = Encoding.UTF8.GetBytes("Original content");
        var originalFileName = $"original-{Guid.NewGuid()}.txt";

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("File Update Test")
            .WithFile(originalFileName, originalContent)
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Update file
        var updatedContent = Encoding.UTF8.GetBytes("Updated content with more text");
        var updatedFileName = $"updated-{Guid.NewGuid()}.txt";

        await _fixture.Client.Annotation()
            .WithId(created.AnnotationId)
            .WithFile(updatedFileName, updatedContent)
            .UpdateAsync();

        // Assert: Retrieve and verify
        var updated = await _fixture.Client.Annotation()
            .RetrieveAsync(created.AnnotationId);

        Assert.Equal(updatedFileName, updated.FileName);
        Assert.True(updated.FileSize > originalContent.Length);
    }

    [Fact]
    public async Task CanDelete_Annotation()
    {
        // Arrange: Create annotation
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Delete Test")
            .ReturnRepresentation()
            .CreateAsync();

        // Act: Delete annotation
        await _fixture.Client.Annotation()
            .DeleteAsync(created.AnnotationId);

        // Assert: Verify it's deleted
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Annotation()
                .RetrieveAsync(created.AnnotationId);
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task CanDelete_AnnotationWithWithId()
    {
        // Arrange: Create annotation
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Delete WithId Test")
            .ReturnRepresentation()
            .CreateAsync();

        // Act: Delete using WithId
        await _fixture.Client.Annotation()
            .WithId(created.AnnotationId)
            .DeleteAsync();

        // Assert: Verify it's deleted
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Annotation()
                .RetrieveAsync(created.AnnotationId);
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    #endregion

    #region File Download Tests

    [Fact]
    public async Task CanDownload_FileFromAnnotation()
    {
        // Arrange: Create annotation with file
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var originalContent = Encoding.UTF8.GetBytes("Download test content with some data");
        var fileName = $"download-{Guid.NewGuid()}.txt";

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Download Test")
            .WithFile(fileName, originalContent)
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Download file
        var (downloadedFileName, mimeType, content) = await _fixture.Client.Annotation()
            .DownloadFileAsync(created.AnnotationId);

        // Assert
        Assert.Equal(fileName, downloadedFileName);
        Assert.Equal("text/plain", mimeType);
        Assert.Equal(originalContent, content);
        Assert.Equal(Encoding.UTF8.GetString(originalContent), Encoding.UTF8.GetString(content));
    }

    [Fact]
    public async Task CanDownload_FileWithWithId()
    {
        // Arrange: Create annotation with file
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var originalContent = Encoding.UTF8.GetBytes("Download WithId test");
        var fileName = $"download-withid-{Guid.NewGuid()}.txt";

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithFile(fileName, originalContent)
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Download using WithId
        var (downloadedFileName, mimeType, content) = await _fixture.Client.Annotation()
            .WithId(created.AnnotationId)
            .DownloadFileAsync();

        // Assert
        Assert.Equal(fileName, downloadedFileName);
        Assert.Equal(originalContent, content);
    }

    [Fact]
    public async Task CanDownload_LargerFile()
    {
        // Arrange: Create annotation with larger file
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        // Create a larger file (10 KB)
        var largeContent = new byte[10240];
        var random = new Random();
        random.NextBytes(largeContent);

        var fileName = $"large-file-{Guid.NewGuid()}.bin";

        var created = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Large File Test")
            .WithFile(fileName, largeContent, "application/octet-stream")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(created.AnnotationId);

        // Act: Download file
        var (downloadedFileName, mimeType, content) = await _fixture.Client.Annotation()
            .DownloadFileAsync(created.AnnotationId);

        // Assert
        Assert.Equal(fileName, downloadedFileName);
        Assert.Equal("application/octet-stream", mimeType);
        Assert.Equal(largeContent.Length, content.Length);
        Assert.Equal(largeContent, content);
    }

    #endregion

    #region Query Integration Tests

    [Fact]
    public async Task CanQuery_AnnotationsByAccount()
    {
        // Arrange: Create account and multiple annotations
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var annotation1 = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Query Test 1")
            .ReturnRepresentation()
            .CreateAsync();

        var annotation2 = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Query Test 2")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(annotation1.AnnotationId);
        _createdAnnotationIds.Add(annotation2.AnnotationId);

        // Act: Query annotations for the account
        var annotations = await _fixture.Client.Query<Annotation>()
            .Where(a => a.ObjectId == account.AccountId)
            .GetListAsync();

        // Assert
        Assert.NotNull(annotations);
        Assert.True(annotations.Count >= 2);
        Assert.Contains(annotations, a => a.AnnotationId == annotation1.AnnotationId);
        Assert.Contains(annotations, a => a.AnnotationId == annotation2.AnnotationId);
    }

    [Fact]
    public async Task CanQuery_AnnotationsWithFiles()
    {
        // Arrange: Create account and annotations (with and without files)
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var withFile = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("With File")
            .WithFile($"test-{Guid.NewGuid()}.txt", Encoding.UTF8.GetBytes("test"))
            .ReturnRepresentation()
            .CreateAsync();

        var withoutFile = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Without File")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(withFile.AnnotationId);
        _createdAnnotationIds.Add(withoutFile.AnnotationId);

        // Act: Query only annotations with documents
        var documentsOnly = await _fixture.Client.Query<Annotation>()
            .Where(a => a.ObjectId == account.AccountId && a.IsDocument == true)
            .GetListAsync();

        // Assert
        Assert.NotNull(documentsOnly);
        Assert.Contains(documentsOnly, a => a.AnnotationId == withFile.AnnotationId);
        Assert.DoesNotContain(documentsOnly, a => a.AnnotationId == withoutFile.AnnotationId);
    }

    #endregion

    #region Multiple File Types Tests

    [Fact]
    public async Task CanCreate_AnnotationsWithDifferentFileTypes()
    {
        // Arrange: Create account
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var testFiles = new[]
        {
            (FileName: $"test-{Guid.NewGuid()}.txt", MimeType: "text/plain", Content: "Text file"),
            (FileName: $"test-{Guid.NewGuid()}.json", MimeType: "application/json", Content: "{\"test\": true}"),
            (FileName: $"test-{Guid.NewGuid()}.xml", MimeType: "application/xml", Content: "<test>data</test>"),
            (FileName: $"test-{Guid.NewGuid()}.csv", MimeType: "text/csv", Content: "col1,col2\nval1,val2"),
        };

        // Act & Assert: Create annotations with different file types
        foreach (var file in testFiles)
        {
            var content = Encoding.UTF8.GetBytes(file.Content);
            var annotation = await _fixture.Client.Annotation()
                .Regarding<Account>(account.AccountId)
                .WithSubject($"Test {Path.GetExtension(file.FileName)}")
                .WithFile(file.FileName, content)
                .ReturnRepresentation()
                .CreateAsync();

            Assert.NotNull(annotation);
            Assert.Equal(file.FileName, annotation.FileName);
            Assert.Equal(file.MimeType, annotation.MimeType);

            _createdAnnotationIds.Add(annotation.AnnotationId);
        }
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task Create_WithoutRegarding_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .WithSubject("Missing Regarding")
                .CreateAsync();
        });
    }

    [Fact]
    public async Task Update_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .WithSubject("Update Test")
                .UpdateAsync();
        });
    }

    [Fact]
    public async Task Delete_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .DeleteAsync();
        });
    }

    [Fact]
    public async Task Retrieve_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .RetrieveAsync();
        });
    }

    [Fact]
    public async Task DownloadFile_WithoutAnnotationId_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .DownloadFileAsync();
        });
    }

    [Fact]
    public async Task Retrieve_NonExistentAnnotation_ThrowsNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Annotation()
                .RetrieveAsync(nonExistentId);
        });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, exception.StatusCode);
    }

    #endregion

    #region Fluent Chaining Tests

    [Fact]
    public async Task FluentChaining_ComplexAnnotation()
    {
        // Arrange
        var account = await CreateTestAccountAsync();
        _createdAccountIds.Add(account.AccountId);

        var fileContent = Encoding.UTF8.GetBytes("Complex annotation test");
        var fileName = $"complex-{Guid.NewGuid()}.txt";

        // Act: Test fluent chaining with all methods
        var annotation = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Complex Annotation")
            .WithNoteText("This tests all fluent methods chained together")
            .WithFile(fileName, fileContent)
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Complex Annotation", annotation.Subject);
        Assert.Equal("This tests all fluent methods chained together", annotation.NoteText);
        Assert.Equal(fileName, annotation.FileName);
        Assert.True(annotation.IsDocument);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    #endregion

    #region Alternate Key Tests

    /// <summary>
    /// Returns the column behind the Account alternate key these tests use, or skips the test when
    /// <c>Dataverse:AccountAlternateKeyColumn</c> is not configured. Out of the box, Account has no alternate key.
    /// </summary>
    private string RequireAlternateKeyColumn()
    {
        var column = _fixture.Configuration.AccountAlternateKeyColumn;
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(column),
            "Set Dataverse:AccountAlternateKeyColumn to a text column with a single-column alternate key on Account " +
            "to run the alternate-key tests.");
        return column!;
    }

    /// <summary>
    /// Verifies that <paramref name="keyColumn"/> has an active alternate key on Account by attempting a lookup.
    /// Throws a descriptive exception if it does not.
    /// </summary>
    private async Task EnsureAlternateKeyAsync(string keyColumn, string keyValue)
    {
        try
        {
            await _fixture.Client.Entity<Account>()
                .UsingAlternateKey(keyColumn, keyValue)
                .RetrieveAsync();
        }
        catch (DataverseException ex) when (ex.ErrorCode == "0x80060888")
        {
            throw new InvalidOperationException(
                $"Prerequisite not met: Dataverse:AccountAlternateKeyColumn is '{keyColumn}', but no active alternate " +
                "key on that column is configured on the Account table in the target Dataverse environment.", ex);
        }
        catch (DataverseException)
        {
            // Any other Dataverse error (e.g., 404 Not Found) means the key IS configured
            // but the record doesn't exist with that value — that's fine for our probe.
        }
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task CanCreate_AnnotationWithAlternateKey_StringBased()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Arrange: Create a test account with an alternate key value
        var keyValue = $"ACC-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Alternate Key Test Account {Guid.NewGuid()}")
            .Set(keyColumn, keyValue)
            .Set("revenue", 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Verify alternate key is configured in the environment
        await EnsureAlternateKeyAsync(keyColumn, keyValue);

        // Act: Create annotation using the alternate key
        var annotation = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("Test with Alternate Key")
            .WithNoteText("This annotation was created using an alternate key")
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Test with Alternate Key", annotation.Subject);
        Assert.Equal("This annotation was created using an alternate key", annotation.NoteText);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task CanCreate_AnnotationWithAlternateKeyAndFile()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Arrange: Create a test account with an alternate key value
        var keyValue = $"ACC-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Alternate Key File Test {Guid.NewGuid()}")
            .Set(keyColumn, keyValue)
            .Set("revenue", 2000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Verify alternate key is configured in the environment
        await EnsureAlternateKeyAsync(keyColumn, keyValue);

        var fileContent = Encoding.UTF8.GetBytes("File attached using alternate key");
        var fileName = $"altkey-test-{Guid.NewGuid()}.txt";

        // Act: Create annotation with file using alternate key
        var annotation = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("Alternate Key with File")
            .WithNoteText("File attachment test")
            .WithFile(fileName, fileContent)
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Alternate Key with File", annotation.Subject);
        Assert.Equal(fileName, annotation.FileName);
        Assert.True(annotation.IsDocument);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task CanCreate_AnnotationWithCompositeAlternateKey()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Note: This test demonstrates the API pattern for composite keys
        // In a real scenario, you'd need an entity with a composite alternate key defined
        // For this test, we'll use a single key but demonstrate the chaining syntax

        // Arrange: Create a test account with an alternate key value
        var keyValue = $"ACC-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Composite Key Test {Guid.NewGuid()}")
            .Set(keyColumn, keyValue)
            .Set("revenue", 2500000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Verify alternate key is configured in the environment
        await EnsureAlternateKeyAsync(keyColumn, keyValue);

        // Act: Demonstrate composite key syntax (even though account only has single key)
        var annotation = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("Composite Key Pattern")
            .WithNoteText("Demonstrates composite key syntax")
            .ReturnRepresentation()
            .CreateAsync();

        // Assert
        Assert.NotNull(annotation);
        Assert.NotEqual(Guid.Empty, annotation.AnnotationId);
        Assert.Equal("Composite Key Pattern", annotation.Subject);

        _createdAnnotationIds.Add(annotation.AnnotationId);
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task Create_WithInvalidAlternateKey_ThrowsDataverseException()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Verify alternate key is configured in the environment before testing error handling
        var probeKeyValue = $"PROBE-{Guid.NewGuid()}";
        await EnsureAlternateKeyAsync(keyColumn, probeKeyValue);

        // Arrange: Use an alternate key value that matches no account
        var invalidKeyValue = $"INVALID-{Guid.NewGuid()}";

        // Act & Assert: Should fail with not found or bad request
        await Assert.ThrowsAsync<DataverseException>(async () =>
        {
            await _fixture.Client.Annotation()
                .Regarding("account", keyColumn, invalidKeyValue)
                .WithSubject("Should Fail")
                .CreateAsync();
        });
    }

    [Fact]
    public async Task Create_WithoutRegardingEntityType_ThrowsInvalidOperationException()
    {
        // Act & Assert: Should fail because Regarding() was never called
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _fixture.Client.Annotation()
                .WithSubject("Missing Regarding")
                .CreateAsync();
        });
    }

    [Fact]
    public async Task WithAlternateKey_WithoutPriorRegarding_ThrowsInvalidOperationException()
    {
        // Act & Assert: Should fail because WithAlternateKey requires prior Regarding with alternate key
        Assert.Throws<InvalidOperationException>(() =>
        {
            _fixture.Client.Annotation()
                .WithAlternateKey("somekey", "somevalue");
        });
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task CanCreate_AnnotationSwitchingBetweenIdAndAlternateKey()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Arrange: Create a test account
        var keyValue = $"ACC-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Switch Test Account {Guid.NewGuid()}")
            .Set(keyColumn, keyValue)
            .Set("revenue", 3000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Verify alternate key is configured in the environment
        await EnsureAlternateKeyAsync(keyColumn, keyValue);

        // Act 1: Create annotation with alternate key
        var annotation1 = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("Created with Alternate Key")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(annotation1.AnnotationId);

        // Act 2: Create annotation with ID (should clear alternate key)
        var annotation2 = await _fixture.Client.Annotation()
            .Regarding<Account>(account.AccountId)
            .WithSubject("Created with ID")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(annotation2.AnnotationId);

        // Assert: Both should succeed
        Assert.NotEqual(Guid.Empty, annotation1.AnnotationId);
        Assert.NotEqual(Guid.Empty, annotation2.AnnotationId);
        Assert.NotEqual(annotation1.AnnotationId, annotation2.AnnotationId);
        Assert.Equal("Created with Alternate Key", annotation1.Subject);
        Assert.Equal("Created with ID", annotation2.Subject);
    }

    [Fact]
    [Trait("Requires", "AccountAlternateKey")]
    public async Task CanCreate_MultipleAnnotationsWithSameAlternateKey()
    {
        var keyColumn = RequireAlternateKeyColumn();

        // Arrange: Create a test account
        var keyValue = $"ACC-{Guid.NewGuid().ToString().Substring(0, 8)}";
        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", $"Multiple Annotations Test {Guid.NewGuid()}")
            .Set(keyColumn, keyValue)
            .Set("revenue", 3500000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        _createdAccountIds.Add(account.AccountId);

        // Act: Create multiple annotations for the same account using alternate key
        var annotation1 = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("First Annotation")
            .WithNoteText("First note")
            .ReturnRepresentation()
            .CreateAsync();

        var annotation2 = await _fixture.Client.Annotation()
            .Regarding("account", keyColumn, keyValue)
            .WithSubject("Second Annotation")
            .WithNoteText("Second note")
            .ReturnRepresentation()
            .CreateAsync();

        _createdAnnotationIds.Add(annotation1.AnnotationId);
        _createdAnnotationIds.Add(annotation2.AnnotationId);

        // Assert: Both annotations should be created successfully
        Assert.NotEqual(Guid.Empty, annotation1.AnnotationId);
        Assert.NotEqual(Guid.Empty, annotation2.AnnotationId);
        Assert.NotEqual(annotation1.AnnotationId, annotation2.AnnotationId);
        Assert.Equal("First Annotation", annotation1.Subject);
        Assert.Equal("Second Annotation", annotation2.Subject);

        // Verify both annotations are linked to the same account
        var annotations = await _fixture.Client.Query<Annotation>()
            .Where(a => a.ObjectId == account.AccountId)
            .GetListAsync();

        Assert.True(annotations.Count >= 2);
        Assert.Contains(annotations, a => a.AnnotationId == annotation1.AnnotationId);
        Assert.Contains(annotations, a => a.AnnotationId == annotation2.AnnotationId);
    }

    #endregion

    #region Helper Methods

    private async Task<Account> CreateTestAccountAsync()
    {
        var accountName = $"Test Account for Annotations {Guid.NewGuid()}";

        var account = (await _fixture.Client.Entity<Account>()
            .Set("name", accountName)
            .Set("revenue", 1000000)
            .ReturnRepresentation()
            .CreateAsync()).Entity!;

        return account;
    }

    #endregion

    #region Cleanup

    public async ValueTask DisposeAsync()
    {
        // Clean up annotations first
        foreach (var annotationId in _createdAnnotationIds)
        {
            try
            {
                await _fixture.Client.Annotation()
                    .DeleteAsync(annotationId);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Then clean up accounts
        foreach (var accountId in _createdAccountIds)
        {
            try
            {
                await _fixture.Client.Entity<Account>()
                    .WithId(accountId)
                    .DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #endregion
}
