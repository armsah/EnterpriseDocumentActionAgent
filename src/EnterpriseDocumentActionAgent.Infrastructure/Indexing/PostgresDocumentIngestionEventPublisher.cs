using System.Data;
using System.Data.Common;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.Documents;

namespace EnterpriseDocumentActionAgent.Infrastructure.Indexing;

/// <summary>
/// Configures PostgreSQL document ingestion event
/// outbox persistence.
/// </summary>
public sealed record PostgresDocumentIngestionEventPublisherOptions(
    string SchemaName = "public",
    string TableName = "document_ingestion_outbox");

/// <summary>
/// Represents the outcome of persisting an ingestion
/// event in the PostgreSQL outbox.
/// </summary>
public enum PostgresDocumentIngestionEventPublishOutcome
{
    Published = 1,
    AlreadyPublished = 2
}

/// <summary>
/// Represents the result of writing an ingestion
/// event to the outbox.
/// </summary>
public sealed record PostgresDocumentIngestionEventPublishResult(
    Guid EventId,
    string TenantId,
    PostgresDocumentIngestionEventPublishOutcome Outcome);

/// <summary>
/// Writes document ingestion events to a PostgreSQL
/// transactional outbox.
/// </summary>
/// <remarks>
/// The publisher accepts an injected DbConnection
/// factory so it does not depend directly on Npgsql.
///
/// A production implementation must configure the
/// connection factory with a PostgreSQL provider.
///
/// The outbox table must have a unique constraint
/// on event_id.
///
/// This component does not create database tables,
/// dispatch events, or process outbox messages.
///
/// When called with an existing transaction, the
/// caller is responsible for committing or rolling
/// back that transaction.
///
/// Database failures propagate to the caller.
/// </remarks>
public sealed class PostgresDocumentIngestionEventPublisher
{
    private readonly Func<CancellationToken, Task<DbConnection>>
        _connectionFactory;

    private readonly PostgresDocumentIngestionEventPublisherOptions
        _options;

    private readonly string _insertSql;

    private static readonly JsonSerializerOptions
        SerializerOptions = new(JsonSerializerDefaults.Web);

    public PostgresDocumentIngestionEventPublisher(
        Func<CancellationToken, Task<DbConnection>> connectionFactory,
        PostgresDocumentIngestionEventPublisherOptions? options = null)
    {
        _connectionFactory = connectionFactory
            ?? throw new ArgumentNullException(
                nameof(connectionFactory));

        _options = options
            ?? new PostgresDocumentIngestionEventPublisherOptions();

        ValidateSqlIdentifier(
            _options.SchemaName,
            nameof(_options.SchemaName));

        ValidateSqlIdentifier(
            _options.TableName,
            nameof(_options.TableName));

        _insertSql = BuildInsertSql(
            _options.SchemaName,
            _options.TableName);
    }

    /// <summary>
    /// Persists an ingestion event using a connection
    /// supplied by the connection factory.
    /// </summary>
    public async Task<PostgresDocumentIngestionEventPublishResult>
        PublishAsync(
            DocumentIngestionEvent ingestionEvent,
            CancellationToken cancellationToken = default)
    {
        ValidateEvent(ingestionEvent);

        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            await _connectionFactory(cancellationToken);

        if (connection is null)
        {
            throw new InvalidOperationException(
                "The database connection factory returned null.");
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return await PublishAsync(
            ingestionEvent,
            connection,
            transaction: null,
            cancellationToken);
    }

    /// <summary>
    /// Persists an ingestion event using an existing
    /// database connection and optional transaction.
    /// </summary>
    /// <remarks>
    /// Use this overload with the same transaction
    /// as the document mutation to obtain atomic
    /// document-and-outbox persistence.
    /// </remarks>
    public async Task<PostgresDocumentIngestionEventPublishResult>
        PublishAsync(
            DocumentIngestionEvent ingestionEvent,
            DbConnection connection,
            DbTransaction? transaction,
            CancellationToken cancellationToken = default)
    {
        ValidateEvent(ingestionEvent);

        ArgumentNullException.ThrowIfNull(connection);

        cancellationToken.ThrowIfCancellationRequested();

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "The supplied database connection must be open.");
        }

        if (transaction is not null &&
            !ReferenceEquals(
                transaction.Connection,
                connection))
        {
            throw new ArgumentException(
                "The transaction must belong to the supplied connection.",
                nameof(transaction));
        }

        var payload = JsonSerializer.Serialize(
            ingestionEvent,
            SerializerOptions);

        await using var command = connection.CreateCommand();

        command.CommandText = _insertSql;
        command.CommandType = CommandType.Text;
        command.Transaction = transaction;

        AddParameter(
            command,
            "@event_id",
            DbType.Guid,
            ingestionEvent.EventId);

        AddParameter(
            command,
            "@document_id",
            DbType.Guid,
            ingestionEvent.DocumentId);

        AddParameter(
            command,
            "@tenant_id",
            DbType.String,
            ingestionEvent.TenantId);

        AddParameter(
            command,
            "@event_type",
            DbType.Int32,
            (int)ingestionEvent.EventType);

        AddParameter(
            command,
            "@content_version",
            DbType.String,
            ingestionEvent.ContentVersion);

        AddParameter(
            command,
            "@initiated_by",
            DbType.String,
            ingestionEvent.InitiatedBy);

        AddParameter(
            command,
            "@occurred_at_utc",
            DbType.DateTimeOffset,
            ingestionEvent.OccurredAtUtc);

        AddParameter(
            command,
            "@payload_json",
            DbType.String,
            payload);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "Unexpected number of rows affected by the outbox insert.");
        }

        return new PostgresDocumentIngestionEventPublishResult(
            EventId: ingestionEvent.EventId,
            TenantId: ingestionEvent.TenantId,
            Outcome: affectedRows == 1
                ? PostgresDocumentIngestionEventPublishOutcome.Published
                : PostgresDocumentIngestionEventPublishOutcome.AlreadyPublished);
    }

    private static string BuildInsertSql(
        string schemaName,
        string tableName)
    {
        var qualifiedTable =
            $"\"{schemaName}\".\"{tableName}\"";

        return $"""
            INSERT INTO {qualifiedTable}
            (
                event_id,
                document_id,
                tenant_id,
                event_type,
                content_version,
                initiated_by,
                occurred_at_utc,
                payload_json
            )
            VALUES
            (
                @event_id,
                @document_id,
                @tenant_id,
                @event_type,
                @content_version,
                @initiated_by,
                @occurred_at_utc,
                @payload_json
            )
            ON CONFLICT (event_id) DO NOTHING;
            """;
    }

    private static void AddParameter(
        DbCommand command,
        string parameterName,
        DbType dbType,
        object? value)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = parameterName;
        parameter.DbType = dbType;
        parameter.Value = value ?? DBNull.Value;

        command.Parameters.Add(parameter);
    }

    private static void ValidateEvent(
        DocumentIngestionEvent ingestionEvent)
    {
        ArgumentNullException.ThrowIfNull(
            ingestionEvent);

        if (ingestionEvent.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "Event identifier must not be empty.",
                nameof(ingestionEvent));
        }

        if (ingestionEvent.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Document identifier must not be empty.",
                nameof(ingestionEvent));
        }

        ValidateText(
            ingestionEvent.TenantId,
            nameof(ingestionEvent.TenantId),
            256);

        ValidateText(
            ingestionEvent.InitiatedBy,
            nameof(ingestionEvent.InitiatedBy),
            256);

        if (!Enum.IsDefined(ingestionEvent.EventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ingestionEvent),
                "Unsupported ingestion event type.");
        }

        if (ingestionEvent.ContentVersion is not null)
        {
            ValidateText(
                ingestionEvent.ContentVersion,
                nameof(ingestionEvent.ContentVersion),
                256);
        }

        if (ingestionEvent.EventType is
                DocumentIngestionEventType.Created or
                DocumentIngestionEventType.ContentUpdated)
        {
            if (ingestionEvent.ContentVersion is null)
            {
                throw new ArgumentException(
                    "A content version is required for this event type.",
                    nameof(ingestionEvent));
            }
        }

        if (ingestionEvent.EventType ==
                DocumentIngestionEventType.Deleted &&
            ingestionEvent.ContentVersion is not null)
        {
            throw new ArgumentException(
                "Deletion events must not contain a content version.",
                nameof(ingestionEvent));
        }

        if (ingestionEvent.OccurredAtUtc == default ||
            ingestionEvent.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Event timestamp must be expressed in UTC.",
                nameof(ingestionEvent));
        }
    }

    private static void ValidateText(
        string value,
        string parameterName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length > maximumLength ||
            value != value.Trim() ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Identifier is invalid.",
                parameterName);
        }
    }

    private static void ValidateSqlIdentifier(
        string identifier,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            identifier,
            parameterName);

        if (identifier.Length > 63 ||
            !(char.IsLetter(identifier[0]) ||
              identifier[0] == '_') ||
            identifier.Any(character =>
                !(char.IsLetterOrDigit(character) ||
                  character == '_')))
        {
            throw new ArgumentException(
                "PostgreSQL identifier is invalid.",
                parameterName);
        }
    }
}