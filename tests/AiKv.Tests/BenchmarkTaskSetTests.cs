using AiKv.Core.Benchmarks;
using AiKv.Core.Benchmarks.Tasks;

namespace AiKv.Tests;

public class BenchmarkTaskSetTests
{
    [Fact]
    public void Tasks_ShouldHaveAtLeast6Tasks()
    {
        Assert.True(BenchmarkTaskSet.Tasks.Count >= 6);
    }

    [Fact]
    public void Tasks_ShouldCoverMultipleLanguages()
    {
        var languages = BenchmarkTaskSet.Tasks.Select(t => t.Language).Distinct().ToHashSet();

        Assert.Contains("csharp", languages);
        Assert.Contains("typescript", languages);
        Assert.Contains("python", languages);
    }

    [Fact]
    public void Tasks_ShouldHaveRequiredFiles()
    {
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            Assert.NotEmpty(task.RequiredFiles);
            Assert.NotEmpty(task.DistractorFiles);
        }
    }

    [Fact]
    public void Tasks_ShouldHaveUniqueIds()
    {
        var ids = BenchmarkTaskSet.Tasks.Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void GetGroundTruth_ShouldReturnCorrectSets()
    {
        var gt = BenchmarkTaskSet.GetGroundTruth("bench-001");

        Assert.NotEmpty(gt.RequiredFiles);
        Assert.NotEmpty(gt.HelpfulFiles);
        Assert.NotEmpty(gt.DistractorFiles);

        // Required and Distractor should not overlap
        var overlap = gt.RequiredFiles.Intersect(gt.DistractorFiles, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(overlap);
    }

    [Fact]
    public void GetGroundTruth_ShouldThrowForUnknownTask()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkTaskSet.GetGroundTruth("nonexistent"));
    }

    [Fact]
    public void GetAllGroundTruths_ShouldReturnAll()
    {
        var all = BenchmarkTaskSet.GetAllGroundTruths();

        Assert.Equal(BenchmarkTaskSet.Tasks.Count, all.Count);
    }

    [Fact]
    public void Tasks_ShouldIncludeSelfRepository()
    {
        var selfTasks = BenchmarkTaskSet.Tasks.Where(t => t.RepositoryId == "ai-kv-self");

        Assert.NotEmpty(selfTasks);
    }

    [Fact]
    public void Tasks_ShouldHaveTaskDescriptions()
    {
        foreach (var task in BenchmarkTaskSet.Tasks)
        {
            Assert.False(string.IsNullOrWhiteSpace(task.TaskDescription));
            Assert.True(task.TaskDescription.Length > 10);
        }
    }
}
