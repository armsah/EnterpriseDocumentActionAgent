using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.Documents;

namespace EnterpriseDocumentActionAgent.Infrastructure.Documents;

/// <summary>
/// Configuration for local document content storage.
/// </summary>
public sealed record LocalDocumentContentStoreOptions
{
    /// <summary>
    /// Root directory containing document content.
    /// </summary>
    public string RootDirectory { get; init; } =
        Path.Combine(
            AppContext.BaseDirectory,
            "document-content");

    /// <summary>
    /// Maximum allowed content size in bytes.
    /// </summary>
    public long MaximumContentBytes { get; init; } =
        25L * 1024 * 1024;
}

/// <summary>
/// Raised when the expected document content version
/// does not match the currently stored version.
/// </summary>
public sealed class DocumentContentConcurrencyException : Exception
{
    public DocumentContentConcurrencyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Raised when local document content storage
/// encounters invalid content or metadata.
/// </summary>
public sealed class LocalDocumentContentStoreException : Exception
{
    public LocalDocumentContentStoreException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Stores document content in a tenant-scoped
/// local filesystem directory.
/// </summary>
/// <remarks>
/// This implementation is intended for local development
/// and single-process use.
///
/// Version checks and manifest updates are serialized
/// within the current process.
///
/// Separate processes sharing the same root directory
/// are not synchronized.
///
/// The caller must authenticate and authorize access
/// before invoking this storage adapter.
/// </remarks>
public sealed class LocalDocumentContentStore : IDocumentContentStore
{
    private const string ManifestFileName = "current.json";

    private const int MaximumTenantIdLength = 200;
    private const int MaximumContentTypeLength = 150;
    private const int MaximumVersionLength = 100;

    private static readonly ConcurrentDictionary<
        string,
        SemaphoreSlim> DirectoryLocks =
        new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    private readonly string _rootDirectory;
    private readonly long _maximumContentBytes;

    /// <summary>
    /// Creates a local document content store.
    /// </summary>
    public LocalDocumentContentStore(
        LocalDocumentContentStoreOptions? options = null)
    {
        options ??= new LocalDocumentContentStoreOptions();

        if (string.IsNullOrWhiteSpace(options.RootDirectory))
        {
            throw new ArgumentException(
                "Content storage root directory is required.",
                nameof(options));
        }

        if (options.MaximumContentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum content size must be positive.");
        }

        _rootDirectory = Path.GetFullPath(
            options.RootDirectory);

        _maximumContentBytes = options.MaximumContentBytes;
    }

    /// <inheritdoc />
    public async Task<DocumentContentDescriptor> WriteAsync(
        WriteDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Content);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateKey(request.Key);
        ValidateContentType(request.ContentType);
        ValidateExpectedVersion(request.ExpectedVersion);

        if (!request.Content.CanRead)
        {
            throw new LocalDocumentContentStoreException(
                "Document content stream must be readable.");
        }

        var directory = ResolveDocumentDirectory(request.Key);

        var gate = GetDirectoryLock(directory);

        await gate.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(directory);

            var current = await ReadManifestAsync(
                directory,
                cancellationToken);

            ValidateWriteVersion(
                current,
                request.ExpectedVersion);

            var version = Guid.NewGuid().ToString("N");

            var contentFileName = $"content-{version}.bin";

            var contentPath = Path.Combine(
                directory,
                contentFileName);

            var temporaryPath = Path.Combine(
                directory,
                $"upload-{Guid.NewGuid():N}.tmp");

            var manifestTemporaryPath = Path.Combine(
                directory,
                $"manifest-{Guid.NewGuid():N}.tmp");

            var manifestPublished = false;

            try
            {
                long length;

                string hash;

                (length, hash) = await WriteContentFileAsync(
                    request.Content,
                    temporaryPath,
                    cancellationToken);

                File.Move(
                    temporaryPath,
                    contentPath);

                var descriptor = new DocumentContentDescriptor(
                    DocumentId: request.Key.DocumentId,
                    TenantId: request.Key.TenantId,
                    ContentType: request.ContentType.Trim(),
                    Length: length,
                    Version: version,
                    UpdatedAtUtc: DateTimeOffset.UtcNow);

                var manifest = new StoredContentManifest(
                    Descriptor: descriptor,
                    ContentFileName: contentFileName,
                    Sha256: hash);

                await WriteManifestFileAsync(
                    manifestTemporaryPath,
                    manifest,
                    cancellationToken);

                var manifestPath = Path.Combine(
                    directory,
                    ManifestFileName);

                File.Move(
                    manifestTemporaryPath,
                    manifestPath,
                    overwrite: true);

                manifestPublished = true;

                return descriptor;
            }
            finally
            {
                TryDeleteFile(temporaryPath);

                TryDeleteFile(manifestTemporaryPath);

                if (!manifestPublished)
                {
                    TryDeleteFile(contentPath);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DocumentContentReadResult?> ReadAsync(
        DocumentContentKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateKey(key);

        var directory = ResolveDocumentDirectory(key);

        var gate = GetDirectoryLock(directory);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var manifest = await ReadManifestAsync(
                directory,
                cancellationToken);

            if (manifest is null)
            {
                return null;
            }

            ValidateManifest(manifest, key);

            var contentPath = ResolveContentPath(
                directory,
                manifest.ContentFileName);

            if (!File.Exists(contentPath))
            {
                throw new LocalDocumentContentStoreException(
                    "Stored content metadata references a missing file.");
            }

            await using var source = new FileStream(
                contentPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            if (source.Length != manifest.Descriptor.Length)
            {
                throw new LocalDocumentContentStoreException(
                    "Stored content length does not match metadata.");
            }

            using var buffer = new MemoryStream();

            using var hash = IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

            var chunk = new byte[81920];

            int bytesRead;

            while ((bytesRead = await source.ReadAsync(
                       chunk.AsMemory(),
                       cancellationToken)) > 0)
            {
                hash.AppendData(
                    chunk,
                    0,
                    bytesRead);

                await buffer.WriteAsync(
                    chunk.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            var calculatedHash = Convert.ToHexString(
                hash.GetHashAndReset());

            if (!string.Equals(
                    calculatedHash,
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new LocalDocumentContentStoreException(
                    "Stored document content failed integrity verification.");
            }

            var content = new MemoryStream(
                buffer.ToArray(),
                writable: false);

            return new DocumentContentReadResult(
                manifest.Descriptor,
                content);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        DocumentContentKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateKey(key);

        var directory = ResolveDocumentDirectory(key);

        var gate = GetDirectoryLock(directory);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var manifest = await ReadManifestAsync(
                directory,
                cancellationToken);

            if (manifest is null)
            {
                return false;
            }

            ValidateManifest(manifest, key);

            var contentPath = ResolveContentPath(
                directory,
                manifest.ContentFileName);

            return File.Exists(contentPath);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        DeleteDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateKey(request.Key);
        ValidateRequiredVersion(request.ExpectedVersion);

        var directory = ResolveDocumentDirectory(request.Key);

        var gate = GetDirectoryLock(directory);

        await gate.WaitAsync(cancellationToken);

        try
        {
            var manifest = await ReadManifestAsync(
                directory,
                cancellationToken);

            if (manifest is null)
            {
                return false;
            }

            ValidateManifest(manifest, request.Key);

            if (!string.Equals(
                    manifest.Descriptor.Version,
                    request.ExpectedVersion,
                    StringComparison.Ordinal))
            {
                throw new DocumentContentConcurrencyException(
                    "Document content version does not match the expected version.");
            }

            var manifestPath = Path.Combine(
                directory,
                ManifestFileName);

            File.Delete(manifestPath);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(long Length, string Sha256)>
        WriteContentFileAsync(
            Stream source,
            string destinationPath,
            CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);

        var buffer = new byte[81920];

        long totalLength = 0;

        int bytesRead;

        while ((bytesRead = await source.ReadAsync(
                   buffer.AsMemory(),
                   cancellationToken)) > 0)
        {
            if (totalLength >
                _maximumContentBytes - bytesRead)
            {
                throw new LocalDocumentContentStoreException(
                    "Document content exceeds the maximum allowed size.");
            }

            totalLength += bytesRead;

            hash.AppendData(
                buffer,
                0,
                bytesRead);

            await destination.WriteAsync(
                buffer.AsMemory(0, bytesRead),
                cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);

        return (
            totalLength,
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());
    }

    private static async Task<StoredContentManifest?>
        ReadManifestAsync(
            string directory,
            CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            directory,
            ManifestFileName);

        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var manifest =
            await JsonSerializer.DeserializeAsync<
                StoredContentManifest>(
                stream,
                JsonOptions,
                cancellationToken);

        if (manifest is null)
        {
            throw new LocalDocumentContentStoreException(
                "Stored document content manifest is invalid.");
        }

        return manifest;
    }

    private static async Task WriteManifestFileAsync(
        string path,
        StoredContentManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            JsonOptions,
            cancellationToken);

        await stream.FlushAsync(cancellationToken);
    }

    private static void ValidateWriteVersion(
        StoredContentManifest? current,
        string? expectedVersion)
    {
        if (current is null)
        {
            if (expectedVersion is not null)
            {
                throw new DocumentContentConcurrencyException(
                    "Document content does not exist at the expected version.");
            }

            return;
        }

        if (expectedVersion is null ||
            !string.Equals(
                current.Descriptor.Version,
                expectedVersion,
                StringComparison.Ordinal))
        {
            throw new DocumentContentConcurrencyException(
                "Document content version does not match the expected version.");
        }
    }

    private static void ValidateManifest(
        StoredContentManifest manifest,
        DocumentContentKey key)
    {
        if (manifest.Descriptor is null ||
            manifest.Descriptor.DocumentId != key.DocumentId ||
            !string.Equals(
                manifest.Descriptor.TenantId,
                key.TenantId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(
                manifest.Descriptor.ContentType) ||
            manifest.Descriptor.Length < 0 ||
            string.IsNullOrWhiteSpace(
                manifest.Descriptor.Version) ||
            manifest.Descriptor.Version.Length >
                MaximumVersionLength ||
            manifest.Descriptor.UpdatedAtUtc == default ||
            manifest.Descriptor.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            manifest.Sha256.Length != 64)
        {
            throw new LocalDocumentContentStoreException(
                "Stored document content metadata is invalid.");
        }
    }

    private static string ResolveContentPath(
        string directory,
        string contentFileName)
    {
        if (string.IsNullOrWhiteSpace(contentFileName) ||
            !contentFileName.StartsWith(
                "content-",
                StringComparison.Ordinal) ||
            !contentFileName.EndsWith(
                ".bin",
                StringComparison.Ordinal) ||
            contentFileName.Contains('/') ||
            contentFileName.Contains('\\') ||
            contentFileName.Contains("..", StringComparison.Ordinal))
        {
            throw new LocalDocumentContentStoreException(
                "Stored content filename is invalid.");
        }

        return Path.Combine(
            directory,
            contentFileName);
    }

    private string ResolveDocumentDirectory(
        DocumentContentKey key)
    {
        var identity = string.Concat(
            key.TenantId.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ":",
            key.TenantId,
            ":",
            key.DocumentId.ToString("N"));

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(identity));

        var directoryName = Convert.ToHexString(
            hash).ToLowerInvariant();

        return Path.Combine(
            _rootDirectory,
            directoryName[..2],
            directoryName);
    }

    private static SemaphoreSlim GetDirectoryLock(
        string directory)
    {
        return DirectoryLocks.GetOrAdd(
            directory,
            static _ => new SemaphoreSlim(1, 1));
    }

    private static void ValidateKey(DocumentContentKey? key)
    {
        if (key is null ||
            key.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(key.TenantId) ||
            key.TenantId.Length > MaximumTenantIdLength)
        {
            throw new LocalDocumentContentStoreException(
                "Document content key is invalid.");
        }
    }

    private static void ValidateContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) ||
            contentType.Length > MaximumContentTypeLength)
        {
            throw new LocalDocumentContentStoreException(
                "Document content type is invalid.");
        }
    }

    private static void ValidateExpectedVersion(
        string? expectedVersion)
    {
        if (expectedVersion is not null)
        {
            ValidateRequiredVersion(expectedVersion);
        }
    }

    private static void ValidateRequiredVersion(
        string? version)
    {
        if (string.IsNullOrWhiteSpace(version) ||
            version.Length > MaximumVersionLength)
        {
            throw new LocalDocumentContentStoreException(
                "Expected document content version is invalid.");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private sealed record StoredContentManifest(
        DocumentContentDescriptor Descriptor,
        string ContentFileName,
        string Sha256);
}