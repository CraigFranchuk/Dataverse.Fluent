# Notes and File Attachments

`client.Annotation()` creates, reads, updates and deletes Dataverse notes (the `annotation` table), including their
file attachments. File content is Base64-encoded on upload and decoded on download, and the MIME type is inferred from
the file name when you do not give one.

## Quick Start

### Creating an Annotation without a File

```csharp
var annotation = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Meeting Notes")
    .WithNoteText("Discussed Q4 sales targets and expansion plans.")
    .ReturnRepresentation()
    .CreateAsync();

Console.WriteLine($"Created annotation: {annotation.AnnotationId}");
```

### Creating an Annotation with a File

```csharp
var pdfBytes = File.ReadAllBytes("contract.pdf");

var annotation = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Signed Contract")
    .WithNoteText("Final executed agreement")
    .WithFile("contract.pdf", pdfBytes)
    .ReturnRepresentation()
    .CreateAsync();

Console.WriteLine($"File size: {annotation.FileSize} bytes");
```

### Creating from a Stream

```csharp
using var fileStream = File.OpenRead("document.docx");

var annotation = await client.Annotation()
    .Regarding("contact", contactId)
    .WithSubject("Resume")
    .WithFile("resume.docx", fileStream)
    .CreateAsync();
```

### Creating from a Stream (Async)

```csharp
using var fileStream = File.OpenRead("large-file.zip");

var builder = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Data Archive")
    .WithFileAsync("backup.zip", fileStream);

var annotation = await builder.CreateAsync();
```

## API Reference

### Fluent Methods

#### `Regarding(string entityType, Guid entityId, string? entitySet = null)`
Specifies the entity record this annotation is attached to. The entity set name defaults to the
English plural of the logical name (`account` → `accounts`, `opportunity` → `opportunities`,
`customeraddress` → `customeraddresses`); pass `entitySet` for tables whose entity set name differs.

```csharp
.Regarding("account", accountId)
.Regarding("contact", contactId)
.Regarding("opportunity", opportunityId)
.Regarding("new_widget", widgetId, entitySet: "new_widgetcollection")
```

#### `Regarding<T>(Guid entityId)`
Type-safe version. The table's logical name and entity set come from `[DataverseEntity]` (or `[EntitySet]` and a
registered `IDataverseNameResolver`), as for `Entity<T>()`; without a mapped logical name, the lowercased type name is
used. An `InvalidOperationException` is thrown when no entity set resolves.

```csharp
.Regarding<Account>(accountId)
.Regarding<Contact>(contactId)
.Regarding<Opportunity>(opportunityId)
```

#### `WithId(Guid annotationId)`
Sets the annotation ID for update/delete/retrieve operations.

```csharp
.WithId(existingAnnotationId)
```

#### `WithSubject(string subject)`
Sets the annotation subject (title).

```csharp
.WithSubject("Q4 Sales Report")
```

#### `WithNoteText(string? noteText)`
Sets the annotation note text (description/body).

```csharp
.WithNoteText("Detailed analysis of Q4 performance metrics...")
```

#### `WithFile(string fileName, byte[] content, string? mimeType = null)`
Attaches a file from a byte array. MIME type is optional and will be inferred if not provided.

```csharp
var bytes = File.ReadAllBytes("report.pdf");
.WithFile("report.pdf", bytes)
.WithFile("data.json", jsonBytes, "application/json")
```

#### `WithFile(string fileName, Stream stream, string? mimeType = null)`
Attaches a file from a stream (synchronous read).

```csharp
using var stream = File.OpenRead("image.png");
.WithFile("photo.png", stream)
```

#### `WithFileAsync(string fileName, Stream stream, string? mimeType = null, CancellationToken ct = default)`
Attaches a file from a stream asynchronously.

```csharp
using var stream = await GetFileStreamAsync();
var builder = await annotationBuilder.WithFileAsync("document.pdf", stream);
```

#### `ReturnRepresentation(bool returnIt = true)`
Requests the full annotation record in the response after creation.

```csharp
.ReturnRepresentation()  // Returns full annotation
.ReturnRepresentation(false)  // Returns minimal data
```

### Execution Methods

#### `CreateAsync(CancellationToken ct = default)`
Creates a new annotation in Dataverse.

```csharp
var annotation = await builder.CreateAsync();
var annotation = await builder.CreateAsync(cancellationToken);
```

**Throws:**
- `InvalidOperationException` - When `Regarding()` was not called

#### `UpdateAsync(CancellationToken ct = default)`
Updates an existing annotation.

```csharp
await client.Annotation()
    .WithId(annotationId)
    .WithSubject("Updated Title")
    .UpdateAsync();
```

**Throws:**
- `InvalidOperationException` - When `WithId()` was not called

#### `DeleteAsync(CancellationToken ct = default)`
Deletes the annotation specified by `WithId()`.

```csharp
await client.Annotation()
    .WithId(annotationId)
    .DeleteAsync();
```

**Throws:**
- `InvalidOperationException` - When `WithId()` was not called

#### `DeleteAsync(Guid annotationId, CancellationToken ct = default)`
Deletes an annotation by ID.

```csharp
await client.Annotation().DeleteAsync(annotationId);
```

#### `RetrieveAsync(CancellationToken ct = default)`
Retrieves the annotation specified by `WithId()`.

```csharp
var annotation = await client.Annotation()
    .WithId(annotationId)
    .RetrieveAsync();
```

**Throws:**
- `InvalidOperationException` - When `WithId()` was not called

#### `RetrieveAsync(Guid annotationId, CancellationToken ct = default)`
Retrieves an annotation by ID.

```csharp
var annotation = await client.Annotation().RetrieveAsync(annotationId);
```

#### `DownloadFileAsync(CancellationToken ct = default)`
Downloads file content from the annotation specified by `WithId()`.

```csharp
var (fileName, mimeType, content) = await client.Annotation()
    .WithId(annotationId)
    .DownloadFileAsync();

File.WriteAllBytes(fileName, content);
```

**Returns:** `(string FileName, string MimeType, byte[] Content)`

**Throws:**
- `InvalidOperationException` - When `WithId()` was not called

#### `DownloadFileAsync(Guid annotationId, CancellationToken ct = default)`
Downloads file content from an annotation by ID.

```csharp
var (fileName, mimeType, content) = await client.Annotation()
    .DownloadFileAsync(annotationId);

Console.WriteLine($"Downloaded: {fileName} ({mimeType}, {content.Length} bytes)");
```

## Complete Examples

### Example 1: Document Management System

```csharp
public async Task<Annotation> AttachContractToAccount(
    Guid accountId, 
    string contractPath,
    string description)
{
    var fileBytes = await File.ReadAllBytesAsync(contractPath);
    var fileName = Path.GetFileName(contractPath);

    return await _client.Annotation()
        .Regarding<Account>(accountId)
        .WithSubject($"Contract - {fileName}")
        .WithNoteText(description)
        .WithFile(fileName, fileBytes)
        .ReturnRepresentation()
        .CreateAsync();
}
```

### Example 2: Bulk File Upload

```csharp
public async Task<List<Annotation>> UploadDocuments(
    Guid accountId, 
    IEnumerable<string> filePaths)
{
    var annotations = new List<Annotation>();

    foreach (var filePath in filePaths)
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(filePath);

        var annotation = await _client.Annotation()
            .Regarding<Account>(accountId)
            .WithSubject(fileName)
            .WithFile(fileName, fileBytes)
            .ReturnRepresentation()
            .CreateAsync();

        annotations.Add(annotation);
    }

    return annotations;
}
```

### Example 3: Download and Process Attachments

```csharp
public async Task DownloadAllAccountAttachments(Guid accountId, string downloadPath)
{
    // Query all annotations for the account
    var annotations = await _client.Query<Annotation>()
        .Where(a => a.ObjectId == accountId && a.IsDocument == true)
        .GetListAsync();

    foreach (var annotation in annotations)
    {
        var (fileName, mimeType, content) = await _client.Annotation()
            .DownloadFileAsync(annotation.AnnotationId);

        var filePath = Path.Combine(downloadPath, fileName);
        await File.WriteAllBytesAsync(filePath, content);

        Console.WriteLine($"Downloaded: {fileName} ({content.Length:N0} bytes)");
    }
}
```

### Example 4: Update Annotation with New File

```csharp
public async Task ReplaceAttachment(
    Guid annotationId, 
    string newFilePath)
{
    var fileBytes = await File.ReadAllBytesAsync(newFilePath);
    var fileName = Path.GetFileName(newFilePath);

    await _client.Annotation()
        .WithId(annotationId)
        .WithSubject($"Updated - {fileName}")
        .WithFile(fileName, fileBytes)
        .UpdateAsync();
}
```

### Example 5: Add Note without File

```csharp
public async Task AddActivityNote(
    Guid regardingId, 
    string regardingEntity,
    string noteText)
{
    await _client.Annotation()
        .Regarding(regardingEntity, regardingId)
        .WithSubject($"Activity Log - {DateTime.Now:yyyy-MM-dd HH:mm}")
        .WithNoteText(noteText)
        .CreateAsync();
}
```

### Example 6: Error Handling

```csharp
public async Task<Annotation?> SafeCreateAnnotation(
    Guid accountId, 
    string filePath)
{
    try
    {
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var fileName = Path.GetFileName(filePath);

        return await _client.Annotation()
            .Regarding<Account>(accountId)
            .WithSubject(fileName)
            .WithFile(fileName, fileBytes)
            .ReturnRepresentation()
            .CreateAsync();
    }
    catch (DataverseException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
    {
        _logger.LogError(ex, 
            "Failed to create annotation. Error: {ErrorCode}, ActivityId: {ActivityId}", 
            ex.ErrorCode, ex.ActivityId);
        return null;
    }
    catch (FileNotFoundException ex)
    {
        _logger.LogError(ex, "File not found: {FilePath}", filePath);
        return null;
    }
}
```

### Example 7: Streaming Large Files

```csharp
public async Task UploadLargeFile(Guid accountId, string largeFilePath)
{
    using var fileStream = new FileStream(
        largeFilePath, 
        FileMode.Open, 
        FileAccess.Read, 
        FileShare.Read,
        bufferSize: 4096,
        useAsync: true);

    var builder = await _client.Annotation()
        .Regarding<Account>(accountId)
        .WithSubject(Path.GetFileName(largeFilePath))
        .WithFileAsync(Path.GetFileName(largeFilePath), fileStream);

    var annotation = await builder
        .ReturnRepresentation()
        .CreateAsync();

    Console.WriteLine($"Uploaded {annotation.FileSize:N0} bytes");
}
```

## Supported MIME Types

The `WithFile()` method automatically infers MIME types from file extensions:

| Extension | MIME Type |
|-----------|-----------|
| `.pdf` | `application/pdf` |
| `.doc` | `application/msword` |
| `.docx` | `application/vnd.openxmlformats-officedocument.wordprocessingml.document` |
| `.xls` | `application/vnd.ms-excel` |
| `.xlsx` | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| `.ppt` | `application/vnd.ms-powerpoint` |
| `.pptx` | `application/vnd.openxmlformats-officedocument.presentationml.presentation` |
| `.txt` | `text/plain` |
| `.csv` | `text/csv` |
| `.html`, `.htm` | `text/html` |
| `.xml` | `application/xml` |
| `.json` | `application/json` |
| `.jpg`, `.jpeg` | `image/jpeg` |
| `.png` | `image/png` |
| `.gif` | `image/gif` |
| `.bmp` | `image/bmp` |
| `.svg` | `image/svg+xml` |
| `.zip` | `application/zip` |
| `.rar` | `application/x-rar-compressed` |
| `.7z` | `application/x-7z-compressed` |
| `.mp3` | `audio/mpeg` |
| `.mp4` | `video/mp4` |
| `.avi` | `video/x-msvideo` |
| *other* | `application/octet-stream` |

You can override the inferred type by explicitly providing the `mimeType` parameter:

```csharp
.WithFile("custom-data.bin", bytes, "application/x-custom-format")
```

## Annotation Model

The `Annotation` class provides strongly-typed access to annotation properties:

```csharp
public class Annotation
{
    public Guid AnnotationId { get; set; }
    public string? Subject { get; set; }
    public string? NoteText { get; set; }
    public string? FileName { get; set; }
    public string? MimeType { get; set; }
    public string? DocumentBody { get; set; }  // Base64-encoded
    public int? FileSize { get; set; }
    public bool? IsDocument { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public Guid? ObjectId { get; set; }
    public string? ObjectTypeCode { get; set; }
}
```

## Best Practices

### 1. Always Dispose Streams

```csharp
using var fileStream = File.OpenRead("document.pdf");
var annotation = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithFile("document.pdf", fileStream)
    .CreateAsync();
```

### 2. Use ReturnRepresentation When You Need the Result

```csharp
// When you need the annotation data (including AnnotationId)
var annotation = await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Important")
    .ReturnRepresentation()
    .CreateAsync();

// When you just need to create without reading back
await client.Annotation()
    .Regarding<Account>(accountId)
    .WithSubject("Log Entry")
    .CreateAsync();
```

### 3. Check File Sizes Before Uploading

A note's file is sent Base64-encoded inside the JSON body, so the whole file is held in memory whichever overload you
use. Dataverse also limits the attachment size (5 MB by default; an administrator can raise it). Check the size before
uploading rather than waiting for the server to reject it:

```csharp
var info = new FileInfo(path);
if (info.Length > maxAttachmentBytes)
    throw new InvalidOperationException($"{info.Name} is larger than the attachment limit.");
```

### 4. Use Cancellation Tokens

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

try
{
    await client.Annotation()
        .Regarding<Account>(accountId)
        .WithFile("file.pdf", bytes)
        .CreateAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("Upload timeout after 30 seconds");
}
```

### 5. Log Activity IDs for Errors

```csharp
try
{
    await client.Annotation()
        .Regarding<Account>(accountId)
        .WithFile("file.pdf", bytes)
        .CreateAsync();
}
catch (DataverseException ex)
{
    _logger.LogError(ex,
        "Annotation creation failed. ErrorCode: {ErrorCode}, ActivityId: {ActivityId}",
        ex.ErrorCode,
        ex.ActivityId);
    throw;
}
```

## Integration with QueryBuilder

You can query annotations using the standard `QueryBuilder`:

```csharp
// Get all annotations for an account
var annotations = await client.Query<Annotation>()
    .Where(a => a.ObjectId == accountId)
    .OrderBy(a => a.CreatedOn, descending: true)
    .GetListAsync();

// Get only annotations with files
var documents = await client.Query<Annotation>()
    .Where(a => a.ObjectId == accountId && a.IsDocument == true)
    .GetListAsync();

// Stream large result sets
await foreach (var annotation in client.Query<Annotation>()
    .Where(a => a.ObjectId == accountId)
    .StreamAsync())
{
    if (annotation.IsDocument == true)
    {
        var (fileName, mimeType, content) = await client.Annotation()
            .DownloadFileAsync(annotation.AnnotationId);
        
        await ProcessFileAsync(fileName, content);
    }
}
```

## Thread Safety

The client can be used from several threads, but a builder cannot: create a new `client.Annotation()` for every
operation.

```csharp
// Safe: Each operation gets its own builder
await Task.WhenAll(
    client.Annotation().Regarding<Account>(id1).WithSubject("File 1").CreateAsync(),
    client.Annotation().Regarding<Account>(id2).WithSubject("File 2").CreateAsync()
);

// A builder runs once: reusing it throws
var builder = client.Annotation();
await builder.Regarding<Account>(id1).CreateAsync();
await builder.Regarding<Account>(id2).CreateAsync(); // throws InvalidOperationException: already executed
```

## Performance Considerations

1. **File size limits**: Dataverse limits attachments to 5 MB by default; an administrator can raise the limit.

2. **Base64 overhead**: Files are Base64-encoded, which adds about a third: a 3 MB file becomes about 4 MB on the wire.

3. **Memory**: The encoded file is part of the request body, so every overload holds the whole file in memory.
   `WithFileAsync(Stream)` only avoids blocking a thread while the stream is read.

4. **Batches**: Uploading files one request at a time keeps each request small; a batch of attachments can exceed
   request size limits.

## See Also

- [EntityBuilder Guide](../README.md#entity-operations)
- [QueryBuilder Guide](../README.md#query-operations)
- [BatchBuilder Guide](../README.md#batch-operations)
- [API Reference](API_REFERENCE.md)
