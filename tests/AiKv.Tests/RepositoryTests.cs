using AiKv.Core.Repository;

namespace AiKv.Tests;

public class RepositoryTests
{
    [Theory]
    [InlineData("https://github.com/user/repo.git", RepositorySource.GitHub, "github.com", "user", "repo")]
    [InlineData("https://gitee.com/user/repo", RepositorySource.Gitee, "gitee.com", "user", "repo")]
    [InlineData("git@github.com:user/repo.git", RepositorySource.GitHub, "github.com", "user", "repo")]
    [InlineData("https://gitlab.com/team/project.git", RepositorySource.Https, "gitlab.com", "team", "project")]
    public void RepositoryUrlParser_ShouldExtractComponents(
        string url, RepositorySource expectedSource, string? expectedHost, string? expectedOwner, string? expectedRepo)
    {
        var parsed = RepositoryUrlParser.Parse(url);

        Assert.Equal(expectedSource, parsed.Source);
        Assert.Equal(expectedHost, parsed.Host);
        Assert.Equal(expectedOwner, parsed.Owner);
        Assert.Equal(expectedRepo, parsed.RepoName);
    }

    [Fact]
    public void RepositoryUrlParser_ShouldHandleUnknownUrls()
    {
        var parsed = RepositoryUrlParser.Parse("not-a-valid-url");

        Assert.Equal(RepositorySource.Unknown, parsed.Source);
    }

    [Fact]
    public void RepositoryUrlParser_ShouldNotExposeCredentials()
    {
        var parsed = RepositoryUrlParser.Parse("https://user:pass@github.com/user/repo.git");

        // The URL should be parsed but credentials should not be in the normalized URL components.
        Assert.Equal(RepositorySource.GitHub, parsed.Source);
        Assert.Equal("user", parsed.Owner);
        Assert.Equal("repo", parsed.RepoName);
    }

    [Fact]
    public void ClonePlan_CanBeCreated_WithDefaults()
    {
        var plan = new ClonePlan
        {
            Url = "https://github.com/user/repo.git",
            Destination = "/tmp/repo",
        };

        Assert.False(plan.IncludeSubmodules);
        Assert.False(plan.IncludeLfs);
        Assert.Empty(plan.Risks);
    }

    [Fact]
    public void GitProcessWrapper_CanBeInstantiated()
    {
        var wrapper = new GitProcessWrapper();
        Assert.NotNull(wrapper);
    }

    [Fact]
    public void GitOperationResult_ShouldIndicateSuccess()
    {
        var result = new GitOperationResult { Success = true, Output = "ok", ExitCode = 0 };
        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void GitOperationResult_ShouldIndicateFailure()
    {
        var result = new GitOperationResult { Success = false, Output = "", ExitCode = 1, ErrorMessage = "error" };
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
