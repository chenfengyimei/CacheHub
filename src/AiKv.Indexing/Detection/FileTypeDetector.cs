using System.Text;

namespace AiKv.Indexing.Detection;

/// <summary>
/// Detected file category.
/// </summary>
public enum FileCategory
{
    Text,
    Binary,
    Image,
    Archive,
    Certificate,
    Empty,
}

/// <summary>
/// Result of file type detection.
/// </summary>
public sealed record FileTypeInfo
{
    public required FileCategory Category { get; init; }
    public required string Language { get; init; }
    public required bool IsBinary { get; init; }
    public required bool ShouldIndex { get; init; }
}

/// <summary>
/// Detects file type using extension mapping and content sampling.
/// </summary>
public static class FileTypeDetector
{
    private static readonly Dictionary<string, string> ExtensionToLanguage = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "csharp",
        [".ts"] = "typescript",
        [".tsx"] = "typescript",
        [".js"] = "javascript",
        [".jsx"] = "javascript",
        [".py"] = "python",
        [".java"] = "java",
        [".kt"] = "kotlin",
        [".go"] = "go",
        [".rs"] = "rust",
        [".c"] = "c",
        [".cpp"] = "cpp",
        [".cc"] = "cpp",
        [".cxx"] = "cpp",
        [".h"] = "c",
        [".hpp"] = "cpp",
        [".rb"] = "ruby",
        [".php"] = "php",
        [".swift"] = "swift",
        [".json"] = "json",
        [".yaml"] = "yaml",
        [".yml"] = "yaml",
        [".xml"] = "xml",
        [".html"] = "html",
        [".css"] = "css",
        [".scss"] = "scss",
        [".sql"] = "sql",
        [".sh"] = "shell",
        [".ps1"] = "powershell",
        [".md"] = "markdown",
        [".toml"] = "toml",
        [".cfg"] = "config",
        [".conf"] = "config",
        [".ini"] = "ini",
        [".txt"] = "text",
        [".dockerfile"] = "dockerfile",
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".a", ".lib", ".o", ".obj",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff",
        ".zip", ".gz", ".tar", ".bz2", ".7z", ".rar", ".xz",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".mp3", ".mp4", ".avi", ".mov", ".wav", ".flac",
        ".class", ".jar", ".war",
        ".pdb", ".apk", ".aab",
        ".dylib", ".bin",
    };

    private static readonly HashSet<string> CertificateExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".p12", ".pfx", ".crt", ".cer", ".der",
    };

    private const int SampleSize = 8192;

    public static FileTypeInfo Detect(string filePath, long fileSize)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (fileSize == 0)
        {
            return new FileTypeInfo
            {
                Category = FileCategory.Empty,
                Language = "unknown",
                IsBinary = false,
                ShouldIndex = false,
            };
        }

        if (CertificateExtensions.Contains(ext))
        {
            return new FileTypeInfo
            {
                Category = FileCategory.Certificate,
                Language = "unknown",
                IsBinary = true,
                ShouldIndex = false,
            };
        }

        if (BinaryExtensions.Contains(ext))
        {
            var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" or ".tiff";
            var isArchive = ext is ".zip" or ".gz" or ".tar" or ".bz2" or ".7z" or ".rar" or ".xz";
            return new FileTypeInfo
            {
                Category = isImage ? FileCategory.Image
                         : isArchive ? FileCategory.Archive
                         : FileCategory.Binary,
                Language = "unknown",
                IsBinary = true,
                ShouldIndex = false,
            };
        }

        var language = ExtensionToLanguage.TryGetValue(ext, out var lang) ? lang : "unknown";

        // For unknown extensions, sample content to detect binary.
        if (language == "unknown")
        {
            var isBinary = IsContentBinary(filePath);
            return new FileTypeInfo
            {
                Category = isBinary ? FileCategory.Binary : FileCategory.Text,
                Language = isBinary ? "unknown" : "text",
                IsBinary = isBinary,
                ShouldIndex = !isBinary,
            };
        }

        return new FileTypeInfo
        {
            Category = FileCategory.Text,
            Language = language,
            IsBinary = false,
            ShouldIndex = true,
        };
    }

    private static bool IsContentBinary(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[SampleSize];
            var read = stream.Read(buffer, 0, Math.Min(SampleSize, (int)stream.Length));

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == 0) return true;
            }

            // Check for high ratio of non-printable characters.
            var nonPrintable = 0;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] < 9 || (buffer[i] > 13 && buffer[i] < 32))
                    nonPrintable++;
            }

            return read > 0 && (double)nonPrintable / read > 0.3;
        }
        catch
        {
            return true; // If we can't read it, treat as binary.
        }
    }
}
