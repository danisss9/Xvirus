using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BaseLibrary.Serializers;
using Xvirus;
using Xvirus.Model;

namespace XvirusService.Services;

public sealed class Quarantine
{
    private List<QuarantineEntry> _entries = new List<QuarantineEntry>();

    public Quarantine()
    {
        LoadQuarantine();
    }

    public List<QuarantineEntry> GetFiles()
    {
        return _entries;
    }

    /// <summary>
    /// Registers a quarantine entry in the metadata without moving the file.
    /// Used when the actual file move has been scheduled to occur on the next
    /// system restart (e.g. via MoveFileEx MOVEFILE_DELAY_UNTIL_REBOOT).
    /// Returns the entry so the caller can derive the quarantine destination path.
    /// </summary>
    public QuarantineEntry RegisterPendingEntry(string sourceFilePath)
    {
        var info = new FileInfo(sourceFilePath);
        var entry = new QuarantineEntry
        {
            OriginalFileName = info.Name,
            OriginalFilePath = info.FullName,
            QuarantinedFileName = BuildQuarantinedName(info.Name),
        };

        try
        {
            entry.OriginalAttributes = info.Attributes.ToString();
            entry.OriginalCreationTime = info.CreationTimeUtc;
            entry.OriginalLastWriteTime = info.LastWriteTimeUtc;
            entry.OriginalLastAccessTime = info.LastAccessTimeUtc;
        }
        catch { /* metadata is best-effort; file may still be locked */ }

        _entries.Add(entry);
        SaveMetadata();

        return entry;
    }

    public QuarantineEntry AddFile(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found.", sourceFilePath);

        var info = new FileInfo(sourceFilePath);
        var entry = new QuarantineEntry
        {
            OriginalFileName = info.Name,
            OriginalFilePath = info.FullName,
            QuarantinedFileName = BuildQuarantinedName(info.Name),
            OriginalAttributes = info.Attributes.ToString(),
            OriginalCreationTime = info.CreationTimeUtc,
            OriginalLastWriteTime = info.LastWriteTimeUtc,
            OriginalLastAccessTime = info.LastAccessTimeUtc,
        };

        string dest = Utils.RelativeToFullPath("quarantine", entry.QuarantinedFileName);
        File.Move(sourceFilePath, dest, false);

        // Strip any special attributes so the file sits quietly in the vault
        File.SetAttributes(dest, FileAttributes.Normal);

        _entries.Add(entry);
        SaveMetadata();

        return entry;
    }

    public void DeleteFile(string entryId)
    {
        var entry = _entries.Find(e => e.Id == entryId) ?? throw new KeyNotFoundException("Quarantine entry not found.");
        string path = Utils.RelativeToFullPath("quarantine", entry.QuarantinedFileName);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        _entries.Remove(entry);
        SaveMetadata();
    }

    public string RestoreFile(string entryId)
    {
        var entry = _entries.Find(e => e.Id == entryId) ?? throw new KeyNotFoundException("Quarantine entry not found.");
        string src = Utils.RelativeToFullPath("quarantine", entry.QuarantinedFileName);

        if (!File.Exists(src))
            throw new FileNotFoundException(
                "Quarantined file is missing.", src);

        string dest = entry.OriginalFilePath;

        string? dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.Move(src, dest, overwrite: false);

        // Restore original timestamps & attributes
        try
        {
            File.SetCreationTimeUtc(dest, entry.OriginalCreationTime);
            File.SetLastWriteTimeUtc(dest, entry.OriginalLastWriteTime);
            File.SetLastAccessTimeUtc(dest, entry.OriginalLastAccessTime);

            if (Enum.TryParse<FileAttributes>(entry.OriginalAttributes, out var attrs))
                File.SetAttributes(dest, attrs);
        }
        catch { /* best-effort */ }

        _entries.Remove(entry);
        SaveMetadata();

        return dest;
    }

    private static string BuildQuarantinedName(string originalName)
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        string safe = Path.GetFileNameWithoutExtension(originalName);
        return $"{safe}_{stamp}.quarantine";
    }

    private void LoadQuarantine()
    {
        var quarantinePath = Utils.RelativeToFullPath("quarantine");
        var metadataFile = Utils.RelativeToFullPath("quarantine.json");

        if (!Directory.Exists(quarantinePath))
        {
            Directory.CreateDirectory(quarantinePath);
        }

        if (!File.Exists(metadataFile))
        {
            _entries = [];
            return;
        }

        var json = File.ReadAllText(metadataFile);
        _entries = JsonSerializer.Deserialize(json, SourceGenerationContextIndent.Default.ListQuarantineEntry) ?? [];
    }

    private void SaveMetadata()
    {
        var metadataFile = Utils.RelativeToFullPath("quarantine.json");
        var json = JsonSerializer.Serialize(_entries, SourceGenerationContextIndent.Default.ListQuarantineEntry);
        File.WriteAllText(metadataFile, json);
    }
}