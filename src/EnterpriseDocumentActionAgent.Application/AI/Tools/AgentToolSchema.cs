using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.AI.Tools;

/// <summary>
/// Describes a callable AI agent tool and its input schema.
/// </summary>
/// <remarks>
/// Tool schemas provide metadata for model tool calling
/// and application-level tool dispatch.
///
/// A tool schema does not grant execution permission.
/// </remarks>
public sealed class AgentToolSchema
{
    public string Name { get; }

    public string Description { get; }

    public string ParametersJsonSchema { get; }

    public bool Strict { get; }

    public AgentToolSchema(
        string name,
        string description,
        string parametersJsonSchema,
        bool strict = true)
    {
        ValidateName(name);

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Tool description is required.",
                nameof(description));
        }

        if (description.Length > 4_000)
        {
            throw new ArgumentException(
                "Tool description cannot exceed 4000 characters.",
                nameof(description));
        }

        if (string.IsNullOrWhiteSpace(parametersJsonSchema))
        {
            throw new ArgumentException(
                "Tool parameter schema is required.",
                nameof(parametersJsonSchema));
        }

        if (parametersJsonSchema.Length > 100_000)
        {
            throw new ArgumentException(
                "Tool parameter schema exceeds the maximum length.",
                nameof(parametersJsonSchema));
        }

        using var document = ParseJson(
            parametersJsonSchema,
            nameof(parametersJsonSchema));

        ValidateRootSchema(document.RootElement);

        Name = name;

        Description = description;

        ParametersJsonSchema =
            JsonSerializer.Serialize(document.RootElement);

        Strict = strict;
    }

    /// <summary>
    /// Creates a tool schema that accepts an empty object.
    /// </summary>
    public static AgentToolSchema WithoutParameters(
        string name,
        string description)
    {
        return new AgentToolSchema(
            name,
            description,
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """);
    }

    /// <summary>
    /// Parses tool arguments and confirms that
    /// the root JSON value is an object.
    /// </summary>
    /// <remarks>
    /// This method validates JSON syntax and root shape.
    /// It does not implement complete JSON Schema validation.
    /// </remarks>
    public JsonElement ParseArguments(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            throw new ArgumentException(
                "Tool arguments are required.",
                nameof(argumentsJson));
        }

        if (argumentsJson.Length > 1_000_000)
        {
            throw new ArgumentException(
                "Tool arguments exceed the maximum length.",
                nameof(argumentsJson));
        }

        using var document = ParseJson(
            argumentsJson,
            nameof(argumentsJson));

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool arguments must be a JSON object.",
                nameof(argumentsJson));
        }

        return document.RootElement.Clone();
    }

    /// <summary>
    /// Determines whether a JSON argument payload
    /// has valid syntax and an object root.
    /// </summary>
    public bool HasValidArgumentShape(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return false;
        }

        try
        {
            ParseArguments(argumentsJson);

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns a detached JSON representation
    /// of the tool's parameter schema.
    /// </summary>
    public JsonElement GetParameterSchema()
    {
        using var document = JsonDocument.Parse(
            ParametersJsonSchema);

        return document.RootElement.Clone();
    }

    private static JsonDocument ParseJson(
        string json,
        string parameterName)
    {
        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "Value must contain valid JSON.",
                parameterName,
                exception);
        }
    }

    private static void ValidateRootSchema(
        JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool parameter schema must be a JSON object.");
        }

        if (!schema.TryGetProperty(
                "type",
                out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String ||
            typeProperty.GetString() != "object")
        {
            throw new ArgumentException(
                "Tool parameter schema must declare " +
                "'type' as 'object'.");
        }

        if (schema.TryGetProperty(
                "properties",
                out var properties) &&
            properties.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool schema 'properties' must be an object.");
        }

        if (schema.TryGetProperty(
                "required",
                out var required) &&
            required.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException(
                "Tool schema 'required' must be an array.");
        }
    }

    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Tool name is required.",
                nameof(name));
        }

        if (name.Length > 128)
        {
            throw new ArgumentException(
                "Tool name cannot exceed 128 characters.",
                nameof(name));
        }

        foreach (var character in name)
        {
            var valid =
                character is >= 'a' and <= 'z' or
                    >= 'A' and <= 'Z' or
                    >= '0' and <= '9' or
                    '_' or '-' or '.';

            if (!valid)
            {
                throw new ArgumentException(
                    "Tool names may contain only letters, " +
                    "digits, underscores, hyphens, and periods.",
                    nameof(name));
            }
        }
    }
}