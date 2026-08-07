using CacheHub.Core.Benchmarks.Agent;
using Xunit;

namespace CacheHub.Tests;

/// <summary>
/// V6: BaselineFileFilter must keep secrets/binary/build artifacts out of the
/// "without CacheHub" baseline prompt, but let real source files through.
/// </summary>
public class BaselineFileFilterTests
{
    [Theory]
    [InlineData("/repo/.env")]
    [InlineData("/repo/config/.env.local")]
    [InlineData("/repo/.env.production")]
    [InlineData("/repo/secrets/credential.json")]
    [InlineData("/repo/api/apikey.txt")]
    [InlineData("/repo/keys/ssh/id_rsa")]
    [InlineData("/repo/id_ed25519")]
    [InlineData("/repo/certs/server.pem")]
    [InlineData("/repo/ssl/server.key")]
    [InlineData("/repo/.npmrc")]
    public void Filters_SecretsAndKeys(string path)
    {
        Assert.True(BaselineFileFilter.IsExcluded(path));
    }

    [Theory]
    [InlineData("/repo/x/test.dll")]
    [InlineData("/repo/logo.png")]
    [InlineData("/repo/assets/font.woff2")]
    [InlineData("/repo/data/data.zip")]
    [InlineData("/repo/bin/app.exe")]
    [InlineData("/repo/thumb.ico")]
    public void Filters_BinaryAssets(string path)
    {
        Assert.True(BaselineFileFilter.IsExcluded(path));
    }

    [Theory]
    [InlineData("/repo/package-lock.json")]
    [InlineData("/repo/yarn.lock")]
    [InlineData("/repo/app.min.js")]
    public void Filters_LockAndMinified(string path)
    {
        Assert.True(BaselineFileFilter.IsExcluded(path));
    }

    [Theory]
    [InlineData("/repo/src/Program.cs")]
    [InlineData("/repo/app/services/AuthService.ts")]
    [InlineData("/repo/lib/helper.py")]
    [InlineData("/repo/tests/TestUtil.go")]
    [InlineData("/repo/src/main.rs")]
    [InlineData("/repo/README.md")]
    public void Allows_RealSourceFiles(string path)
    {
        Assert.False(BaselineFileFilter.IsExcluded(path));
    }
}
