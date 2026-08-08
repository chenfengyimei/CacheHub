namespace CacheHub.Core.Benchmarks.Agent;

/// <summary>
/// V6 (review #18): Determines which files are safe/fair to feed into the "without CacheHub"
/// Agent baseline context. Mirrors CacheHub's own indexing safety so the baseline stays fair:
/// it must never dump secrets (.env, *.pem, *.key), binary blobs, or build artifacts into a prompt.
/// V7-W07: Fixed false-positive exclusions (TokenService.cs, Tokenizer.cs, etc.) and
/// added stable sort + relative path in context labels.
/// </summary>
public static class BaselineFileFilter
{
    /// <summary>
    /// Returns true if the file at the given absolute path should be EXCLUDED from baseline context.
    /// V7-W07: Removed loose name.Contains("token") check that falsely excluded source code files.
    /// </summary>
    public static bool IsExcluded(string path)
    {
        var fn = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var name = fn.ToLowerInvariant();

        // Secrets / auth — exact filenames only, not substring match
        if (name is ".env" or ".env.local" or ".env.production" or ".env.development"
            or "id_rsa" or "id_ed25519" or ".npmrc" or ".netrc"
            or "secrets.json" or "secrets.yaml" or "secrets.yml"
            or "credentials.json" or "credentials.yaml" or "credentials.yml"
            or "credential.json" or "credential.yaml" or "credential.yml"
            or "apikey.txt" or "api_keys.json" or "api-keys.json"
            or ".htpasswd" or ".git-credentials")
            return true;
        if (ext is ".pem" or ".key" or ".pfx" or ".p12" or ".crt" or ".pub" or ".jks" or ".keystore")
            return true;

        // Binary / non-text
        if (ext is ".dll" or ".so" or ".dylib" or ".exe" or ".pdb" or ".pyd" or ".a" or ".lib"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".webp" or ".ico" or ".bmp"
            or ".ttf" or ".otf" or ".woff" or ".woff2" or ".eot"
            or ".zip" or ".gz" or ".tar" or ".7z" or ".rar" or ".jar" or ".war" or ".parquet" or ".db" or ".sqlite")
            return true;

        // Generated / minified / lockfiles / OS junk
        if (fn.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
            fn.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase) ||
            name is "package-lock.json" or "yarn.lock" or "pnpm-lock.yaml" or ".DS_Store")
            return true;

        return false;
    }
}

