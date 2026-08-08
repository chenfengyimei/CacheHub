using CacheHub.Core.Benchmarks;
using CacheHub.Core.Benchmarks.Agent;
using CacheHub.Core.Tokens;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// Tests for the Agent Benchmark framework (V4-W014).
/// Verifies task→model→patch→test→cost loop mechanics without external models.
/// </summary>
public class AgentBenchmarkTests
{
    private static readonly BenchmarkTask TestTask = new()
    {
        Id = "agent-001",
        RepositoryId = "test-repo",
        Language = "csharp",
        TaskDescription = "Fix the async exception swallowing bug",
        CommitHash = "HEAD",
        RequiredFiles = ["src/Commands.cs", "src/Errors.cs"],
        HelpfulFiles = ["src/Models.cs"],
        DistractorFiles = ["README.md"],
    };

    [Fact]
    public async Task Runner_SuccessOnFirstRound_RecordsTokensAndStats()
    {
        var model = new MockAgentModel(roundsToSucceed: 1);
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 3);

        var result = await runner.RunTaskAsync(
            TestTask,
            BenchmarkConfig(),
            desc => new AgentContextPackage
            {
                TaskDescription = desc,
                SelectedFilePaths = ["src/Config.cs", "src/Errors.cs"],
                FileSnippets = ["// src/Config.cs\nclass Config {}", "// src/Errors.cs\nclass Errors {}"],
                EstimatedTokens = 50,
            },
            patch => Task.FromResult(new AgentTestResult { Success = true, Passed = 5, Total = 5 }));

        Assert.True(result.TaskCompleted);
        Assert.Equal(1, result.Rounds);
        Assert.True(result.PromptTokens > 0);
        Assert.Equal(5, result.TestsPassed);
        Assert.Equal(5, result.TestsTotal);
        Assert.True(result.TotalCost >= 0);
    }

    [Fact]
    public async Task Runner_TaskNeverSucceeds_ExhaustsMaxRounds()
    {
        var model = new MockAgentModel(roundsToSucceed: int.MaxValue);
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 2);

        var result = await runner.RunAllAsync(
            [TestTask],
            BenchmarkConfig(),
            context => new AgentContextPackage
            {
                TaskDescription = context,
                SelectedFilePaths = ["src/Config.cs"],
                FileSnippets = ["code"],
                EstimatedTokens = 10,
            },
            patch => Task.FromResult(new AgentTestResult { Success = false, Passed = 0, Total = 5 }));

        Assert.Equal(2, result.AvgRounds);
        Assert.Equal(0, result.SuccessRate);
        Assert.True(result.TotalTokens > 0);
    }

    [Fact]
    public async Task Runner_AggregatesAcrossTasks_ComputesCostAndTokens()
    {
        var model = new MockAgentModel(roundsToSucceed: 1);
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 3);

        var tasks = new[]
        {
            TestTask,
            TestTask with { Id = "agent-002" },
            TestTask with { Id = "agent-003" },
        };

        var result = await runner.RunAllAsync(
            tasks,
            BenchmarkConfig(),
            context => new AgentContextPackage
            {
                TaskDescription = context,
                SelectedFilePaths = ["src/Config.cs"],
                FileSnippets = ["code"],
                EstimatedTokens = 10,
            },
            patch => Task.FromResult(new AgentTestResult { Success = true, Passed = 3, Total = 3 }));

        Assert.Equal(3, result.Runs.Count);
        Assert.Equal(1.0, result.SuccessRate);
        Assert.True(result.TotalPromptTokens > 0);
        Assert.True(result.TotalCompletionTokens > 0);
        Assert.Equal(1.0, result.AvgTestPassRatio);
    }

    // V6: Provider actual usage and local estimate are tracked separately (review #16)
    [Fact]
    public async Task Runner_ProviderActualAndLocalEstimate_AreSeparate()
    {
        var model = new MockAgentModel(roundsToSucceed: 1);
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 3);

        var result = await runner.RunTaskAsync(
            TestTask,
            BenchmarkConfig(),
            desc => new AgentContextPackage
            {
                TaskDescription = desc,
                SelectedFilePaths = ["src/Config.cs", "src/Errors.cs"],
                FileSnippets = ["// src/Config.cs\nclass Config {}", "// src/Errors.cs\nclass Errors {}"],
                EstimatedTokens = 50,
            },
            patch => Task.FromResult(new AgentTestResult { Success = true, Passed = 1, Total = 1 }));

        // Provider actual comes from the model response (mock returns 100).
        // Local estimate comes from the tokenizer counting the assembled prompt.
        Assert.Equal(100, result.PromptTokens);          // provider actual
        Assert.True(result.LocalEstimatedContextTokens > 0);  // local estimate of assembled prompt
        Assert.NotEqual(result.PromptTokens, result.LocalEstimatedContextTokens);
    }

    // V7-W03: Multi-round success rate bug — first round fail, second round pass should = TaskCompleted
    [Fact]
    public async Task Runner_FirstRoundFail_SecondRoundPass_TaskCompleted()
    {
        var model = new MockAgentModel(roundsToSucceed: int.MaxValue); // model always "succeeds" from its perspective
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 3);

        var callCount = 0;
        var result = await runner.RunTaskAsync(
            TestTask,
            BenchmarkConfig(),
            desc => new AgentContextPackage
            {
                TaskDescription = desc,
                SelectedFilePaths = ["src/Config.cs"],
                FileSnippets = ["code"],
                EstimatedTokens = 10,
            },
            patch =>
            {
                callCount++;
                // Round 1: 90/100 pass (fail), Round 2: 100/100 pass (success)
                if (callCount == 1)
                    return Task.FromResult(new AgentTestResult { Success = false, Passed = 90, Total = 100 });
                return Task.FromResult(new AgentTestResult { Success = true, Passed = 100, Total = 100 });
            });

        // V7-W03: Bug was — cumulative 190/200 → TaskCompleted=false (WRONG)
        // Fix: per-round success tracking → TaskCompleted=true (CORRECT)
        Assert.True(result.TaskCompleted);  // Second round succeeded
        Assert.Equal(2, result.Rounds);
        Assert.Equal(100, result.TestsPassed);  // Last round's results, not cumulative
        Assert.Equal(100, result.TestsTotal);
    }

    // V7-W03: Three rounds, first two fail, third passes
    [Fact]
    public async Task Runner_ThreeRounds_LastSucceeds_TaskCompleted()
    {
        var model = new MockAgentModel(roundsToSucceed: int.MaxValue);
        var runner = new AgentBenchmarkRunner(model, new CodeTokenizer(), maxRounds: 3);

        var callCount = 0;
        var result = await runner.RunTaskAsync(
            TestTask,
            BenchmarkConfig(),
            desc => new AgentContextPackage
            {
                TaskDescription = desc,
                SelectedFilePaths = ["src/Config.cs"],
                FileSnippets = ["code"],
                EstimatedTokens = 10,
            },
            patch =>
            {
                callCount++;
                return callCount switch
                {
                    1 => Task.FromResult(new AgentTestResult { Success = false, Passed = 50, Total = 100 }),
                    2 => Task.FromResult(new AgentTestResult { Success = false, Passed = 80, Total = 100 }),
                    _ => Task.FromResult(new AgentTestResult { Success = true, Passed = 100, Total = 100 }),
                };
            });

        Assert.True(result.TaskCompleted);
        Assert.Equal(3, result.Rounds);
        Assert.Equal(100, result.TestsPassed);
        Assert.Equal(100, result.TestsTotal);
    }

    private static BenchmarkConfig BenchmarkConfig() => new()
    {
        ModelId = "test-model",
        AgentId = "test-agent",
        SystemPrompt = "You are a test assistant.",
        RunsPerTask = 1,
        ResetBetweenRuns = true,
        ShareBuildCache = false,
    };

    private sealed class MockAgentModel : IAgentModelExecutor
    {
        private readonly int _roundsToSucceed;
        private int _calls;

        public MockAgentModel(int roundsToSucceed) => _roundsToSucceed = roundsToSucceed;
        public string ModelId => "mock";

        public Task<AgentModelResponse> GenerateAsync(string systemPrompt, string userContent, CancellationToken ct = default)
        {
            _calls++;
            var done = _calls >= _roundsToSucceed;
            return Task.FromResult(new AgentModelResponse
            {
                Content = done
                    ? "```csharp\n// Fixed code\n```"
                    : "```\n// Not fixed\n```",
                PromptTokens = 100,
                CompletionTokens = 20,
                Cost = 0.001,
            });
        }
    }
}
