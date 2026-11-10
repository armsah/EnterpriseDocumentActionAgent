using EnterpriseDocumentActionAgent.Application.AI.Memory;
using EnterpriseDocumentActionAgent.Application.AI.Messages;
using EnterpriseDocumentActionAgent.Application.AI.Providers;
using EnterpriseDocumentActionAgent.Application.AI.Runtime;
using EnterpriseDocumentActionAgent.Application.AI.Templates;

using EnterpriseDocumentActionAgent.Infrastructure.AI.DependencyInjection;
using EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Testing;

/// <summary>
/// Represents the result of an individual framework test.
/// </summary>
public sealed record UniversalAgentTestResult(
    string Name,
    bool Passed,
    string? Error,
    TimeSpan Duration);

/// <summary>
/// Represents the result of a complete framework test run.
/// </summary>
public sealed record UniversalAgentTestReport(
    int Total,
    int Passed,
    int Failed,
    TimeSpan Duration,
    IReadOnlyList<UniversalAgentTestResult> Results)
{
    public bool Succeeded => Failed == 0;
}

/// <summary>
/// Provides executable integration checks for the
/// universal AI agent framework.
/// </summary>
/// <remarks>
/// This is a self-contained test harness.
///
/// It does not require xUnit, NUnit, MSTest,
/// network access, API credentials, or a database.
///
/// Tests must be invoked explicitly through RunAllAsync.
///
/// These checks do not replace dedicated unit,
/// integration, security, or performance test suites.
/// </remarks>
public static class UniversalAgentFrameworkTests
{
    /// <summary>
    /// Runs all framework checks and returns a report.
    /// </summary>
    public static async Task<UniversalAgentTestReport> RunAllAsync(
        CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.UtcNow;

        var results = new List<UniversalAgentTestResult>();

        await RunTestAsync(
            results,
            "Fake provider supports configured model",
            TestSupportedModelAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider rejects unsupported model",
            TestUnsupportedModelAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider generates deterministic response",
            TestDeterministicResponseAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider records invocations",
            TestInvocationHistoryAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider simulates failures",
            TestSimulatedFailureAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider honors cancellation",
            TestCancellationAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Provider registry resolves fake provider",
            TestProviderRegistryAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Agent runtime executes model request",
            TestAgentRuntimeAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Agent template creates configuration",
            TestAgentTemplateAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Dependency injection initializes providers",
            TestDependencyInjectionAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Dependency injection shares memory instance",
            TestSharedMemoryAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Provider router resolves fake model",
            TestProviderRouterAsync,
            cancellationToken);

        await RunTestAsync(
            results,
            "Fake provider supports concurrent invocations",
            TestConcurrentInvocationsAsync,
            cancellationToken);

        var passed = results.Count(result => result.Passed);

        var failed = results.Count - passed;

        return new UniversalAgentTestReport(
            Total: results.Count,
            Passed: passed,
            Failed: failed,
            Duration: DateTimeOffset.UtcNow - started,
            Results: results.AsReadOnly());
    }

    /// <summary>
    /// Runs the framework checks and throws
    /// if any test fails.
    /// </summary>
    public static async Task RunAndThrowAsync(
        CancellationToken cancellationToken = default)
    {
        var report = await RunAllAsync(cancellationToken);

        if (report.Succeeded)
        {
            return;
        }

        var failures = report.Results
            .Where(result => !result.Passed)
            .Select(result =>
                $"{result.Name}: {result.Error}");

        throw new InvalidOperationException(
            "Universal agent framework tests failed:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, failures));
    }

    private static async Task RunTestAsync(
        ICollection<UniversalAgentTestResult> results,
        string name,
        Func<CancellationToken, Task> test,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var started = DateTimeOffset.UtcNow;

        try
        {
            await test(cancellationToken);

            results.Add(
                new UniversalAgentTestResult(
                    name,
                    true,
                    null,
                    DateTimeOffset.UtcNow - started));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            results.Add(
                new UniversalAgentTestResult(
                    name,
                    false,
                    exception.ToString(),
                    DateTimeOffset.UtcNow - started));
        }
    }

    private static Task TestSupportedModelAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new FakeAiModelProvider(
            model: "supported-model");

        Assert(
            provider.SupportsModel("supported-model"),
            "Configured model should be supported.");

        Assert(
            !provider.SupportsModel("other-model"),
            "Unregistered model should not be supported.");

        provider.AddSupportedModel("second-model");

        Assert(
            provider.SupportsModel("second-model"),
            "Newly registered model should be supported.");

        return Task.CompletedTask;
    }

    private static async Task TestUnsupportedModelAsync(
        CancellationToken cancellationToken)
    {
        var provider = new FakeAiModelProvider(
            model: "supported-model");

        var request = CreateRequest(
            "unsupported-model");

        await AssertThrowsAsync<InvalidOperationException>(
            () => provider.GenerateAsync(
                request,
                cancellationToken));
    }

    private static async Task TestDeterministicResponseAsync(
        CancellationToken cancellationToken)
    {
        const string expected =
            "Deterministic framework test response.";

        var provider = new FakeAiModelProvider(
            responseText: expected);

        var request = CreateRequest();

        var response = await provider.GenerateAsync(
            request,
            cancellationToken);

        Assert(
            response.Text == expected,
            "Response text did not match.");

        Assert(
            response.ConversationId ==
                request.ConversationId,
            "Conversation identifier did not match.");

        Assert(
            response.Provider == provider.ProviderId,
            "Provider identifier did not match.");

        Assert(
            response.Model == request.Model,
            "Model identifier did not match.");
    }

    private static async Task TestInvocationHistoryAsync(
        CancellationToken cancellationToken)
    {
        var provider = new FakeAiModelProvider();

        await provider.GenerateAsync(
            CreateRequest(),
            cancellationToken);

        await provider.GenerateAsync(
            CreateRequest(),
            cancellationToken);

        Assert(
            provider.InvocationCount == 2,
            "Expected exactly two invocations.");

        var history = provider.GetInvocations();

        Assert(
            history.Count == 2,
            "Expected two recorded requests.");

        Assert(
            history[0].SequenceNumber == 1,
            "First invocation sequence was incorrect.");

        Assert(
            history[1].SequenceNumber == 2,
            "Second invocation sequence was incorrect.");

        provider.ClearInvocations();

        Assert(
            provider.GetInvocations().Count == 0,
            "Invocation history was not cleared.");

        Assert(
            provider.InvocationCount == 2,
            "Lifetime invocation count should be preserved.");
    }

    private static async Task TestSimulatedFailureAsync(
        CancellationToken cancellationToken)
    {
        var provider = new FakeAiModelProvider()
            .SetFailure(
                new HttpRequestException(
                    "Simulated provider failure."));

        await AssertThrowsAsync<HttpRequestException>(
            () => provider.GenerateAsync(
                CreateRequest(),
                cancellationToken));

        provider.ClearFailure();

        var response = await provider.GenerateAsync(
            CreateRequest(),
            cancellationToken);

        Assert(
            !string.IsNullOrWhiteSpace(response.Text),
            "Provider should recover after clearing failure.");
    }

    private static async Task TestCancellationAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new FakeAiModelProvider()
            .SetLatency(TimeSpan.FromSeconds(5));

        using var cancellationSource =
            new CancellationTokenSource();

        cancellationSource.CancelAfter(
            TimeSpan.FromMilliseconds(50));

        await AssertThrowsAsync<OperationCanceledException>(
            () => provider.GenerateAsync(
                CreateRequest(),
                cancellationSource.Token));
    }

    private static Task TestProviderRegistryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var registry = new AiProviderRegistry();

        var provider = new FakeAiModelProvider();

        registry.Register(provider);

        var resolved = registry.Resolve(
            FakeAiModelProvider.DefaultModel);

        Assert(
            ReferenceEquals(provider, resolved),
            "Registry returned an unexpected provider.");

        return Task.CompletedTask;
    }

    private static async Task TestAgentRuntimeAsync(
        CancellationToken cancellationToken)
    {
        const string expected =
            "Agent runtime execution succeeded.";

        var provider = new FakeAiModelProvider(
            responseText: expected);

        var registry = new AiProviderRegistry();

        registry.Register(provider);

        var runtime = new AgentRuntime(registry);

        var request = CreateRequest();

        var response = await runtime.ExecuteAsync(
            request,
            cancellationToken: cancellationToken);

        Assert(
            response.Text == expected,
            "Agent runtime returned unexpected text.");
    }

    private static Task TestAgentTemplateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var template =
            AgentProjectTemplate.DocumentAnalysis(
                FakeAiModelProvider.DefaultModel,
                FakeAiModelProvider.DefaultProviderId);

        var configuration =
            template.CreateConfiguration(
                "test-document-agent",
                new AgentTemplateOverrides
                {
                    Temperature = 0.1,
                    MaxOutputTokens = 1024
                });

        Assert(
            configuration.AgentId ==
                "test-document-agent",
            "Agent identifier was incorrect.");

        Assert(
            configuration.Model ==
                FakeAiModelProvider.DefaultModel,
            "Model identifier was incorrect.");

        Assert(
            configuration.Temperature == 0.1,
            "Temperature override was not applied.");

        Assert(
            configuration.MaxOutputTokens == 1024,
            "Output token override was not applied.");

        return Task.CompletedTask;
    }

    private static Task TestDependencyInjectionAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new FakeAiModelProvider();

        var services = new ServiceCollection();

        services.AddUniversalAgentFramework();

        services.AddUniversalAgentProvider(provider);

        using var serviceProvider =
            services.BuildServiceProvider();

        serviceProvider.InitializeUniversalAgentFramework();

        var registry =
            serviceProvider.GetRequiredService<
                AiProviderRegistry>();

        var router =
            serviceProvider.GetRequiredService<
                AiProviderRouter>();

        Assert(
            registry.ContainsProvider(provider.ProviderId),
            "Provider was not registered in the registry.");

        Assert(
            router.GetProviderIds().Contains(
                provider.ProviderId),
            "Provider was not registered in the router.");

        serviceProvider.InitializeUniversalAgentFramework();

        Assert(
            registry.GetProviderIds().Count == 1,
            "Repeated initialization duplicated providers.");

        return Task.CompletedTask;
    }

    private static Task TestSharedMemoryAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new ServiceCollection();

        services.AddUniversalAgentFramework();

        using var serviceProvider =
            services.BuildServiceProvider();

        var memory =
            serviceProvider.GetRequiredService<
                IAgentMemory>();

        var concrete =
            serviceProvider.GetRequiredService<
                InMemoryAgentMemory>();

        Assert(
            ReferenceEquals(memory, concrete),
            "Memory registrations should share one instance.");

        return Task.CompletedTask;
    }

    private static Task TestProviderRouterAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var provider = new FakeAiModelProvider();

        var router = new AiProviderRouter();

        router.RegisterProvider(provider);

        var route = router.ResolveRoute(
            FakeAiModelProvider.DefaultModel);

        Assert(
            route.ProviderId == provider.ProviderId,
            "Router selected an unexpected provider.");

        return Task.CompletedTask;
    }

    private static async Task TestConcurrentInvocationsAsync(
        CancellationToken cancellationToken)
    {
        var provider = new FakeAiModelProvider();

        const int invocationCount = 20;

        var tasks = Enumerable.Range(
                0,
                invocationCount)
            .Select(_ =>
                provider.GenerateAsync(
                    CreateRequest(),
                    cancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert(
            responses.Length == invocationCount,
            "Unexpected response count.");

        Assert(
            provider.InvocationCount == invocationCount,
            "Concurrent invocation count was incorrect.");

        var history = provider.GetInvocations();

        Assert(
            history.Count == invocationCount,
            "Concurrent invocation history was incomplete.");

        Assert(
            history.Select(item => item.SequenceNumber)
                .Distinct()
                .Count() == invocationCount,
            "Invocation sequence numbers were not unique.");
    }

    private static AiModelRequest CreateRequest(
        string model = FakeAiModelProvider.DefaultModel)
    {
        var conversationId = Guid.NewGuid();

        return new AiModelRequest(
            conversationId: conversationId,
            model: model,
            messages:
            [
                AiMessage.User(
                    conversationId,
                    "Framework test prompt.")
            ]);
    }

    private static void Assert(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(
                message);
        }
    }

    private static async Task AssertThrowsAsync<TException>(
        Func<Task> operation)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected exception of type " +
            $"'{typeof(TException).Name}' was not thrown.");
    }
}