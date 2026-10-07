using System.IO;

namespace Player.App.Services.Storage;

/// <summary>Remove only generated flat metadata; never traverse unexpected staging directories.</summary>
internal static class BackupStageCleanup
{
    public static void Clean(string stage, DataDirectoryLease lease)
    {
        try
        {
            // Keep the Windows directory chain pinned while removing its generated children.
            foreach (var path in Directory.EnumerateFiles(stage))
                if (IsGeneratedName(Path.GetFileName(path))) File.Delete(path);
        }
        finally { lease.Dispose(); }
        // Windows requires releasing the directory pin to remove the root. A replacement
        // containing any data must fail this nonrecursive operation, never be traversed.
        Directory.Delete(stage, recursive: false);
    }

    private static bool IsGeneratedName(string name)
    {
        if (name is "library.db" or "library.db-wal" or "library.db-shm" or "library.db-journal" or
            "library.db.owner.lock" or "settings.json" or "settings.json.bak") return true;
        const string prefix = "library.db.pre-schema2-";
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".db", StringComparison.Ordinal) &&
            name.Length == prefix.Length + 50 && name[prefix.Length + 14] == '-' &&
            !name.AsSpan(prefix.Length, 14).ContainsAnyExcept("0123456789") &&
            Guid.TryParseExact(name.AsSpan(prefix.Length + 15, 32), "N", out _);
    }
}
