using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace GodotXaml.Addon;

/// <summary>
/// The preview server's discovery file: <c>{port, token, pid, project}</c>, one file per server start, named
/// <c>&lt;pid&gt;-&lt;32 hex&gt;.json</c> in the per-user local data folder's <c>godot-xaml/editors/</c>. The folders are
/// owner-only (0700 on Unix), the file is created exclusively and owner-only (0600 on Unix) under a <c>.tmp</c> name,
/// flushed to disk and moved into place, so a client that reads only <c>*.json</c> never sees half of one. A stale file a
/// crash leaves is the client's to skip.
/// </summary>
internal static class XscnPreviewDiscovery
{
    private const string FileExtension = ".json";

    private const string StagingExtension = ".tmp";

    /// <summary>
    /// The live discovery folder: <c>godot-xaml/editors</c> under <see cref="Environment.SpecialFolder.LocalApplicationData"/>.
    /// It is relative when the platform names no such folder, and the server then does not start.
    /// </summary>
    /// <returns>The folder.</returns>
    internal static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "godot-xaml", "editors");

    /// <summary>Writes a new discovery file in the folder, creating the folder and its parent owner-only.</summary>
    /// <param name="folder">The discovery folder, a full path.</param>
    /// <param name="info">The file's content.</param>
    /// <returns>The file's path.</returns>
    internal static string Write(string folder, PreviewDiscoveryInfo info)
    {
        string? parent = Path.GetDirectoryName(folder);
        if (parent is not null)
        {
            AddonTempFolder.CreateOwnerOnly(parent);
        }

        AddonTempFolder.CreateOwnerOnly(folder);
        string path = Path.Combine(folder, $"{info.Pid}-{RandomNumberGenerator.GetHexString(32, lowercase: true)}{FileExtension}");
        string staging = path + StagingExtension;
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(info, XscnPreviewJsonContext.Default.PreviewDiscoveryInfo);
        bool created = false;
        try
        {
            using (var stream = new FileStream(staging, StagingOptions()))
            {
                created = true;
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(staging, path, overwrite: false);
            return path;
        }
        catch when (created)
        {
            // Only a staging file this call created is removed; one it found is left alone.
            File.Delete(staging);
            throw;
        }
    }

    /// <summary>Removes a discovery file this process wrote.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>Why it could not be removed, or null once it is gone.</returns>
    internal static string? Delete(string path)
    {
        try
        {
            File.Delete(path);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    // CreateNew is O_CREAT|O_EXCL: a planted file or link at the name fails the write rather than being written through.
    private static FileStreamOptions StagingOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }
}
