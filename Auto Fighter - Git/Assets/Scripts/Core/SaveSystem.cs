using System;
using System.IO;
using UnityEngine;

namespace AutoFighter.Core
{
    /// <summary>
    /// Single-slot JSON save system. Writes to
    /// <see cref="Application.persistentDataPath"/>/save.json with an
    /// atomic swap so a crash mid-save can't corrupt the existing file.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "save.json";
        private const string TempSuffix = ".tmp";
        private const string BackupSuffix = ".bak";

        public static SaveData Current { get; private set; }

        public static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string TempPath => SavePath + TempSuffix;
        private static string BackupPath => SavePath + BackupSuffix;

        public static bool Exists() => File.Exists(SavePath);

        /// <summary>
        /// Loads the save from disk. If no save exists (or it's corrupted
        /// and the backup also fails), a fresh default save is created.
        /// The result is cached in <see cref="Current"/>.
        /// </summary>
        public static SaveData Load()
        {
            if (TryReadFrom(SavePath, out var data))
            {
                Current = Migrate(data);
                return Current;
            }

            if (File.Exists(BackupPath) && TryReadFrom(BackupPath, out var backup))
            {
                Debug.LogWarning("[SaveSystem] Primary save unreadable; restored from backup.");
                Current = Migrate(backup);
                Save();
                return Current;
            }

            Debug.Log("[SaveSystem] No save found; creating new save.");
            Current = SaveData.CreateDefault();
            Save();
            return Current;
        }

        /// <summary>
        /// Persists <see cref="Current"/> (or a provided payload) to disk.
        /// Uses a temp-file + rename pattern so the existing save is only
        /// replaced once the new one is fully written.
        /// </summary>
        public static void Save(SaveData data = null)
        {
            if (data != null) Current = data;
            if (Current == null) Current = SaveData.CreateDefault();

            Current.version = SaveData.CurrentVersion;
            Current.lastSavedUtcTicks = DateTime.UtcNow.Ticks;

            try
            {
                string json = JsonUtility.ToJson(Current, prettyPrint: true);
                File.WriteAllText(TempPath, json);

                if (File.Exists(SavePath))
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    File.Replace(TempPath, SavePath, BackupPath);
                }
                else
                {
                    File.Move(TempPath, SavePath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to save: {e}");
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(BackupPath)) File.Delete(BackupPath);
                if (File.Exists(TempPath)) File.Delete(TempPath);
                Current = null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to delete save: {e}");
            }
        }

        private static bool TryReadFrom(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path)) return false;

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return false;
                data = JsonUtility.FromJson<SaveData>(json);
                return data != null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Failed to read {path}: {e}");
                return false;
            }
        }

        /// <summary>
        /// Upgrade older save versions to the current schema.
        /// Add cases here as the schema evolves.
        /// </summary>
        private static SaveData Migrate(SaveData data)
        {
            if (data.cursorSpeedPixelsPerSecond <= 0f)
                data.cursorSpeedPixelsPerSecond = SaveData.DefaultCursorSpeedPixelsPerSecond;

            if (data.version == SaveData.CurrentVersion) return data;

            // Example pattern for future migrations:
            // if (data.version < 2) { /* fill in new fields */ data.version = 2; }

            data.version = SaveData.CurrentVersion;
            return data;
        }
    }
}
