using System.Data;
using System.Data.Common;
using EnterpriseDocumentActionAgent.Application.AI.Graphs;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Graphs;

/// <summary>
/// Provides PostgreSQL-backed persistence
/// for agent graph checkpoints.
/// </summary>
/// <remarks>
/// Uses provider-neutral ADO.NET abstractions.
///
/// The caller supplies a connection factory
/// that creates PostgreSQL-compatible connections.
///
/// Database schema creation is explicit.
/// The store does not automatically run migrations.
///
/// Authorization, tenant isolation, encryption,
/// retention, and auditing are responsibilities
/// of the surrounding infrastructure.
/// </remarks>
public sealed class PostgresAgentCheckpointStore
    : IAgentCheckpointStore
{
    /// <summary>
    /// Default checkpoint table name.
    /// </summary>
    public const string DefaultTableName =
        "agent_graph_checkpoints";

    private readonly Func<DbConnection> _connectionFactory;

    private readonly string _tableName;

    /// <summary>
    /// Gets the configured checkpoint table name.
    /// </summary>
    public string TableName => _tableName;

    /// <summary>
    /// Initializes a PostgreSQL checkpoint store.
    /// </summary>
    /// <param name="connectionFactory">
    /// Factory producing new, unopened database
    /// connections. Each operation owns and disposes
    /// its connection.
    /// </param>
    /// <param name="tableName">
    /// PostgreSQL table name, optionally
    /// qualified by a schema name.
    /// </param>
    public PostgresAgentCheckpointStore(
        Func<DbConnection> connectionFactory,
        string tableName = DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(
            connectionFactory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            tableName);

        _connectionFactory = connectionFactory;

        _tableName = QuoteQualifiedIdentifier(
            tableName);
    }

    /// <summary>
    /// Returns the PostgreSQL DDL required
    /// to create the checkpoint table.
    /// </summary>
    /// <remarks>
    /// Run this SQL through an explicit
    /// database migration or provisioning step.
    /// </remarks>
    public string GetCreateTableSql()
    {
        return $"""
            CREATE TABLE IF NOT EXISTS {_tableName}
            (
                checkpoint_id UUID PRIMARY KEY,
                execution_id UUID NOT NULL,
                graph_id TEXT NOT NULL,
                sequence_number BIGINT NOT NULL,
                payload_json JSONB NOT NULL,
                created_at_utc TIMESTAMPTZ NOT NULL,

                CONSTRAINT checkpoint_sequence_nonnegative
                    CHECK (sequence_number >= 0),

                CONSTRAINT checkpoint_graph_nonempty
                    CHECK (length(trim(graph_id)) > 0),

                CONSTRAINT checkpoint_execution_sequence_unique
                    UNIQUE (execution_id, sequence_number)
            );

            CREATE INDEX IF NOT EXISTS
                agent_checkpoint_execution_sequence_idx
            ON {_tableName}
                (execution_id, sequence_number DESC);
            """;
    }

    /// <summary>
    /// Creates the checkpoint schema.
    /// </summary>
    /// <remarks>
    /// This method is intended for explicit
    /// provisioning, not automatic invocation
    /// during normal checkpoint operations.
    /// </remarks>
    public async Task EnsureSchemaAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = GetCreateTableSql();

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        AgentCheckpointRecord checkpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        cancellationToken.ThrowIfCancellationRequested();

        const string columns = """
            checkpoint_id,
            execution_id,
            graph_id,
            sequence_number,
            payload_json,
            created_at_utc
            """;

        var sql = $"""
            INSERT INTO {_tableName}
            (
                {columns}
            )
            VALUES
            (
                @checkpoint_id,
                @execution_id,
                @graph_id,
                @sequence_number,
                CAST(@payload_json AS jsonb),
                @created_at_utc
            );
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@checkpoint_id",
            checkpoint.CheckpointId,
            DbType.Guid);

        AddParameter(
            command,
            "@execution_id",
            checkpoint.ExecutionId,
            DbType.Guid);

        AddParameter(
            command,
            "@graph_id",
            checkpoint.GraphId,
            DbType.String);

        AddParameter(
            command,
            "@sequence_number",
            checkpoint.SequenceNumber,
            DbType.Int64);

        AddParameter(
            command,
            "@payload_json",
            checkpoint.PayloadJson,
            DbType.String);

        AddParameter(
            command,
            "@created_at_utc",
            checkpoint.CreatedAtUtc.UtcDateTime,
            DbType.DateTime);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "Checkpoint insertion did not affect " +
                "exactly one database row.");
        }
    }

    /// <inheritdoc />
    public async Task<AgentCheckpointRecord?> GetAsync(
        Guid executionId,
        Guid checkpointId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            executionId,
            nameof(executionId));

        ValidateIdentifier(
            checkpointId,
            nameof(checkpointId));

        cancellationToken.ThrowIfCancellationRequested();

        var sql = $"""
            SELECT
                checkpoint_id,
                execution_id,
                graph_id,
                sequence_number,
                payload_json::text,
                created_at_utc
            FROM {_tableName}
            WHERE execution_id = @execution_id
              AND checkpoint_id = @checkpoint_id
            LIMIT 1;
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@execution_id",
            executionId,
            DbType.Guid);

        AddParameter(
            command,
            "@checkpoint_id",
            checkpointId,
            DbType.Guid);

        await using var reader =
            await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCheckpoint(reader);
    }

    /// <inheritdoc />
    public async Task<AgentCheckpointRecord?> GetLatestAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            executionId,
            nameof(executionId));

        cancellationToken.ThrowIfCancellationRequested();

        var sql = $"""
            SELECT
                checkpoint_id,
                execution_id,
                graph_id,
                sequence_number,
                payload_json::text,
                created_at_utc
            FROM {_tableName}
            WHERE execution_id = @execution_id
            ORDER BY sequence_number DESC
            LIMIT 1;
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@execution_id",
            executionId,
            DbType.Guid);

        await using var reader =
            await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return ReadCheckpoint(reader);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentCheckpointDescriptor>>
        ListAsync(
            Guid executionId,
            CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            executionId,
            nameof(executionId));

        cancellationToken.ThrowIfCancellationRequested();

        var sql = $"""
            SELECT
                checkpoint_id,
                execution_id,
                graph_id,
                sequence_number,
                created_at_utc
            FROM {_tableName}
            WHERE execution_id = @execution_id
            ORDER BY sequence_number DESC;
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@execution_id",
            executionId,
            DbType.Guid);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        var descriptors =
            new List<AgentCheckpointDescriptor>();

        while (await reader.ReadAsync(
            cancellationToken))
        {
            descriptors.Add(
                ReadDescriptor(reader));
        }

        return descriptors.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid executionId,
        Guid checkpointId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            executionId,
            nameof(executionId));

        ValidateIdentifier(
            checkpointId,
            nameof(checkpointId));

        cancellationToken.ThrowIfCancellationRequested();

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE execution_id = @execution_id
              AND checkpoint_id = @checkpoint_id;
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@execution_id",
            executionId,
            DbType.Guid);

        AddParameter(
            command,
            "@checkpoint_id",
            checkpointId,
            DbType.Guid);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        return affectedRows > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(
            executionId,
            nameof(executionId));

        cancellationToken.ThrowIfCancellationRequested();

        var sql = $"""
            DELETE FROM {_tableName}
            WHERE execution_id = @execution_id;
            """;

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        AddParameter(
            command,
            "@execution_id",
            executionId,
            DbType.Guid);

        return await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    /// <summary>
    /// Creates a new database connection.
    /// </summary>
    private DbConnection CreateConnection()
    {
        var connection = _connectionFactory();

        return connection ??
            throw new InvalidOperationException(
                "Database connection factory returned null.");
    }

    /// <summary>
    /// Adds a typed parameter to a command.
    /// </summary>
    private static void AddParameter(
        DbCommand command,
        string name,
        object value,
        DbType type)
    {
        var parameter = command.CreateParameter();

        parameter.ParameterName = name;

        parameter.DbType = type;

        parameter.Value = value;

        command.Parameters.Add(parameter);
    }

    /// <summary>
    /// Reads a complete checkpoint record.
    /// </summary>
    private static AgentCheckpointRecord ReadCheckpoint(
        DbDataReader reader)
    {
        var checkpointId = reader.GetGuid(0);

        var executionId = reader.GetGuid(1);

        var graphId = reader.GetString(2);

        var sequenceNumber = reader.GetInt64(3);

        var payloadJson = reader.GetString(4);

        var createdAtUtc = ReadUtcTimestamp(
            reader,
            5);

        return new AgentCheckpointRecord(
            checkpointId,
            executionId,
            graphId,
            sequenceNumber,
            payloadJson,
            createdAtUtc);
    }

    /// <summary>
    /// Reads checkpoint metadata.
    /// </summary>
    private static AgentCheckpointDescriptor ReadDescriptor(
        DbDataReader reader)
    {
        var checkpointId = reader.GetGuid(0);

        var executionId = reader.GetGuid(1);

        var graphId = reader.GetString(2);

        var sequenceNumber = reader.GetInt64(3);

        var createdAtUtc = ReadUtcTimestamp(
            reader,
            4);

        return new AgentCheckpointDescriptor(
            checkpointId,
            executionId,
            graphId,
            sequenceNumber,
            createdAtUtc);
    }

    /// <summary>
    /// Converts database timestamps to UTC.
    /// </summary>
    private static DateTimeOffset ReadUtcTimestamp(
        DbDataReader reader,
        int ordinal)
    {
        var timestamp = reader.GetDateTime(
            ordinal);

        var utcTimestamp =
            timestamp.Kind switch
            {
                DateTimeKind.Utc => timestamp,
                DateTimeKind.Local =>
                    timestamp.ToUniversalTime(),
                _ => DateTime.SpecifyKind(
                    timestamp,
                    DateTimeKind.Utc)
            };

        return new DateTimeOffset(
            utcTimestamp);
    }

    /// <summary>
    /// Validates a required GUID.
    /// </summary>
    private static void ValidateIdentifier(
        Guid identifier,
        string parameterName)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "Identifier cannot be empty.",
                parameterName);
        }
    }

    /// <summary>
    /// Safely quotes a PostgreSQL identifier.
    /// </summary>
    /// <remarks>
    /// Supports a simple table name or
    /// schema-qualified table name.
    ///
    /// Identifier segments are restricted
    /// to ASCII letters, digits, and
    /// underscores, beginning with a
    /// letter or underscore.
    /// </remarks>
    private static string QuoteQualifiedIdentifier(
        string identifier)
    {
        var segments = identifier.Trim().Split('.');

        if (segments.Length is < 1 or > 2)
        {
            throw new ArgumentException(
                "Table name must be a simple or " +
                "schema-qualified PostgreSQL identifier.",
                nameof(identifier));
        }

        foreach (var segment in segments)
        {
            if (!IsValidIdentifier(segment))
            {
                throw new ArgumentException(
                    $"Invalid PostgreSQL identifier '{segment}'.",
                    nameof(identifier));
            }
        }

        return string.Join(
            ".",
            segments.Select(segment => $"\"{segment}\""));
    }

    /// <summary>
    /// Validates an identifier segment.
    /// </summary>
    private static bool IsValidIdentifier(
        string identifier)
    {
        if (identifier.Length is < 1 or > 63)
        {
            return false;
        }

        var first = identifier[0];

        if (!(char.IsAsciiLetter(first) ||
              first == '_'))
        {
            return false;
        }

        foreach (var character in identifier)
        {
            if (!(char.IsAsciiLetterOrDigit(character) ||
                  character == '_'))
            {
                return false;
            }
        }

        return true;
    }
}