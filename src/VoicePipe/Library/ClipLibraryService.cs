using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoicePipe.Settings;

namespace VoicePipe.Library;

public static class ClipLibraryService
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string ClipsDirectory { get; } = Path.Combine(UserSettingsStore.AppDataDirectory, "Clips");

    public static string Import(string sourcePath)
    {
        Directory.CreateDirectory(ClipsDirectory);

        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            throw new FileNotFoundException("Clip file was not found.", sourcePath);
        }

        if (IsInLibrary(source.FullName))
        {
            return source.FullName;
        }

        var hash = ComputeShortHash(source.FullName);
        var safeName = CreateSafeFileName(Path.GetFileNameWithoutExtension(source.Name));
        var extension = source.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            ? ".mp3"
            : source.Extension;
        var destination = Path.Combine(ClipsDirectory, $"{safeName}_{hash}{extension}");

        if (!File.Exists(destination))
        {
            File.Copy(source.FullName, destination);
        }

        return destination;
    }

    public static void DeleteLibraryFile(string path)
    {
        try
        {
            if (!IsInLibrary(path) || !File.Exists(path))
            {
                return;
            }

            File.Delete(path);
            var metadataPath = GetMetadataPath(path);
            if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
            }
        }
        catch
        {
            // Clip deletion should not interrupt the UI.
        }
    }

    public static IEnumerable<string> EnumerateLibraryClips()
    {
        if (!Directory.Exists(ClipsDirectory))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(ClipsDirectory).ToList();
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
    }

    public static string CreateFallbackDisplayName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return StripImportHashSuffix(name);
    }

    public static ClipLibraryMetadata? TryReadMetadata(string path)
    {
        try
        {
            var metadataPath = GetMetadataPath(path);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var json = File.ReadAllText(metadataPath);
            return JsonSerializer.Deserialize<ClipLibraryMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveMetadata(string path, string displayName, string originalFileName, bool isLoopEnabled, bool isPinned, string hotkeyText)
    {
        try
        {
            if (!IsInLibrary(path))
            {
                return;
            }

            var metadata = new ClipLibraryMetadata
            {
                DisplayName = displayName,
                OriginalFileName = originalFileName,
                IsLoopEnabled = isLoopEnabled,
                IsPinned = isPinned,
                HotkeyText = hotkeyText
            };
            var json = JsonSerializer.Serialize(metadata, JsonOptions);
            File.WriteAllText(GetMetadataPath(path), json);
        }
        catch
        {
            // Clip metadata is a recovery aid; settings.json remains the primary store.
        }
    }

    public static bool IsInLibrary(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var libraryPath = Path.GetFullPath(ClipsDirectory);
        return fullPath.StartsWith(libraryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetMetadataPath(string path) => path + ".voicepipe.json";

    private static string ComputeShortHash(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    private static string CreateSafeFileName(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            builder.Append(InvalidFileNameChars.Contains(character) ? '_' : character);
        }

        var safe = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(safe) ? "clip" : safe;
    }

    private static string StripImportHashSuffix(string value)
    {
        const int hashLength = 12;
        var separatorIndex = value.Length - hashLength - 1;
        if (separatorIndex <= 0 || value[separatorIndex] != '_')
        {
            return value;
        }

        for (var index = separatorIndex + 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return value;
            }
        }

        return value[..separatorIndex];
    }
}
