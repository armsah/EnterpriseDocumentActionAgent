using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Represents a reusable AI prompt template
/// containing named variable placeholders.
/// </summary>
/// <remarks>
/// Placeholders use the format {variableName}.
///
/// Variable names must begin with a letter
/// or underscore and may contain letters,
/// digits, and underscores.
///
/// Double braces can be used to represent
/// literal braces:
///
/// {{ becomes {
/// }} becomes }
///
/// Rendering performs literal substitution.
/// It does not provide prompt-injection
/// protection or content authorization.
/// </remarks>
public sealed class AiPromptTemplate
    : IAiRunnable<IReadOnlyDictionary<string, string>, string>
{
    private static readonly Regex VariableNamePattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyList<TemplateSegment> _segments;

    private readonly FrozenSet<string> _requiredVariables;

    /// <summary>
    /// Gets the original template text.
    /// </summary>
    public string Template { get; }

    /// <summary>
    /// Gets the variable names required
    /// to render this template.
    /// </summary>
    public IReadOnlySet<string> RequiredVariables =>
        _requiredVariables;

    /// <summary>
    /// Initializes and validates a prompt template.
    /// </summary>
    public AiPromptTemplate(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);

        if (template.Length > 100_000)
        {
            throw new ArgumentException(
                "Template cannot exceed 100,000 characters.",
                nameof(template));
        }

        Template = template;

        _segments = ParseTemplate(template);

        _requiredVariables = _segments
            .Where(static segment => segment.IsVariable)
            .Select(static segment => segment.Value)
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Renders the template using supplied variables.
    /// </summary>
    /// <param name="variables">
    /// Variable values indexed by placeholder name.
    /// </param>
    /// <returns>
    /// The rendered prompt text.
    /// </returns>
    public string Render(
        IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        var missing = _requiredVariables
            .Where(name =>
                !variables.ContainsKey(name) ||
                variables[name] is null)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new ArgumentException(
                "Missing required prompt variables: " +
                string.Join(", ", missing),
                nameof(variables));
        }

        var builder = new StringBuilder();

        foreach (var segment in _segments)
        {
            if (segment.IsVariable)
            {
                builder.Append(variables[segment.Value]);
            }
            else
            {
                builder.Append(segment.Value);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders the prompt as an asynchronous
    /// AI pipeline operation.
    /// </summary>
    public Task<string> RunAsync(
        IReadOnlyDictionary<string, string> input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = Render(input);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Determines whether all required variables
    /// have non-null values.
    /// </summary>
    public bool CanRender(
        IReadOnlyDictionary<string, string>? variables)
    {
        if (variables is null)
        {
            return false;
        }

        return _requiredVariables.All(name =>
            variables.TryGetValue(name, out var value) &&
            value is not null);
    }

    /// <summary>
    /// Returns the required variable names
    /// that are absent or contain null values.
    /// </summary>
    public IReadOnlyList<string> GetMissingVariables(
        IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);

        return Array.AsReadOnly(
            _requiredVariables
                .Where(name =>
                    !variables.TryGetValue(
                        name,
                        out var value) ||
                    value is null)
                .OrderBy(
                    static name => name,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Parses a template into literal and
    /// variable segments.
    /// </summary>
    private static IReadOnlyList<TemplateSegment> ParseTemplate(
        string template)
    {
        var segments = new List<TemplateSegment>();

        var literal = new StringBuilder();

        var index = 0;

        while (index < template.Length)
        {
            var current = template[index];

            if (current == '{')
            {
                if (index + 1 < template.Length &&
                    template[index + 1] == '{')
                {
                    literal.Append('{');

                    index += 2;

                    continue;
                }

                FlushLiteral(segments, literal);

                var closingIndex = template.IndexOf(
                    '}',
                    index + 1);

                if (closingIndex < 0)
                {
                    throw new FormatException(
                        "Template contains an unclosed placeholder.");
                }

                var variableName = template.Substring(
                    index + 1,
                    closingIndex - index - 1);

                ValidateVariableName(variableName);

                segments.Add(
                    new TemplateSegment(
                        variableName,
                        true));

                index = closingIndex + 1;

                continue;
            }

            if (current == '}')
            {
                if (index + 1 < template.Length &&
                    template[index + 1] == '}')
                {
                    literal.Append('}');

                    index += 2;

                    continue;
                }

                throw new FormatException(
                    "Template contains an unmatched closing brace.");
            }

            literal.Append(current);

            index++;
        }

        FlushLiteral(segments, literal);

        return Array.AsReadOnly(segments.ToArray());
    }

    /// <summary>
    /// Appends a pending literal segment
    /// to the parsed segment collection.
    /// </summary>
    private static void FlushLiteral(
        List<TemplateSegment> segments,
        StringBuilder literal)
    {
        if (literal.Length == 0)
        {
            return;
        }

        segments.Add(
            new TemplateSegment(
                literal.ToString(),
                false));

        literal.Clear();
    }

    /// <summary>
    /// Validates a placeholder variable name.
    /// </summary>
    private static void ValidateVariableName(
        string variableName)
    {
        if (variableName.Length == 0 ||
            variableName.Length > 128 ||
            !VariableNamePattern.IsMatch(variableName))
        {
            throw new FormatException(
                $"Invalid prompt variable name: '{variableName}'.");
        }
    }

    /// <summary>
    /// Represents one parsed template segment.
    /// </summary>
    private sealed record TemplateSegment(
        string Value,
        bool IsVariable);
}