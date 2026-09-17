using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Purgers.Progression
{
    public sealed class JsonGameSaveRepository : IGameSaveRepository
    {
        private const string SaveDirectoryName = "Saves";
        private const string SaveFileExtension = ".json";

        private readonly Func<DateTime> utcNowProvider;

        public JsonGameSaveRepository()
            : this(
                Path.Combine(
                    Application.persistentDataPath,
                    "Purgers",
                    SaveDirectoryName),
                () => DateTime.UtcNow)
        {
        }

        public JsonGameSaveRepository(
            string rootDirectory,
            Func<DateTime> utcNowProvider = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException(
                    "Save root directory cannot be empty.",
                    nameof(rootDirectory));

            RootDirectory = Path.GetFullPath(rootDirectory);
            this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        public string RootDirectory { get; }

        public GameSaveRepositoryResult<GameSaveData> CreateNew(string displayName)
        {
            DateTime utcNow = utcNowProvider().ToUniversalTime();
            string saveId = Guid.NewGuid().ToString("N");
            string resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
                ? $"新存檔 {utcNow:yyyy-MM-dd HH-mm-ss}"
                : displayName.Trim();

            GameSaveData save =
                GameSaveData.CreateNew(saveId, resolvedDisplayName, utcNow);
            return Write(save);
        }

        public GameSaveCatalog List()
        {
            var summaries = new List<GameSaveSummary>();
            var errors = new List<string>();

            try
            {
                Directory.CreateDirectory(RootDirectory);
                string[] files =
                    Directory.GetFiles(RootDirectory, "*" + SaveFileExtension);

                for (int i = 0; i < files.Length; i++)
                {
                    string saveId = Path.GetFileNameWithoutExtension(files[i]);
                    GameSaveRepositoryResult<GameSaveData> loadResult =
                        Load(saveId);

                    if (!loadResult.Success)
                    {
                        errors.Add(loadResult.Error);
                        continue;
                    }

                    summaries.Add(CreateSummary(loadResult.Value));
                }

                summaries.Sort(CompareByLastPlayedDescending);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                errors.Add(
                    $"無法讀取存檔目錄 '{RootDirectory}': {exception.Message}");
            }

            return new GameSaveCatalog(summaries, errors);
        }

        public GameSaveRepositoryResult<GameSaveData> Load(string saveId)
        {
            if (!TryGetSavePath(saveId, out string path, out string pathError))
                return GameSaveRepositoryResult<GameSaveData>.Failed(pathError);

            try
            {
                if (!File.Exists(path))
                {
                    return GameSaveRepositoryResult<GameSaveData>.Failed(
                        $"找不到存檔 '{saveId}'。");
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                GameSaveRepositoryResult<GameSaveData> parseResult =
                    DeserializeAndMigrate(json);

                if (!parseResult.Success)
                {
                    return GameSaveRepositoryResult<GameSaveData>.Failed(
                        $"存檔 '{saveId}' 無法載入：{parseResult.Error}");
                }

                GameSaveData save = parseResult.Value;
                if (!string.Equals(
                        save.SaveId,
                        saveId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return GameSaveRepositoryResult<GameSaveData>.Failed(
                        $"存檔檔名與內容 ID 不一致：'{saveId}' / '{save.SaveId}'。");
                }

                return GameSaveRepositoryResult<GameSaveData>.Succeeded(save);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException)
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    $"讀取存檔 '{saveId}' 失敗：{exception.Message}");
            }
        }

        public GameSaveRepositoryResult<GameSaveData> Write(GameSaveData save)
        {
            string validationError = ValidateForWrite(save);
            if (!string.IsNullOrEmpty(validationError))
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    validationError);
            }

            if (!TryGetSavePath(
                    save.SaveId,
                    out string destinationPath,
                    out string pathError))
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(pathError);
            }

            string temporaryPath =
                destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backupPath = destinationPath + ".bak";

            try
            {
                Directory.CreateDirectory(RootDirectory);
                save.Normalize();
                save.LastPlayedUtc =
                    utcNowProvider().ToUniversalTime().ToString("O");
                string json = JsonUtility.ToJson(save, true);

                WriteAndFlush(temporaryPath, json);

                if (File.Exists(destinationPath))
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Replace(
                        temporaryPath,
                        destinationPath,
                        backupPath,
                        true);

                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                else
                {
                    File.Move(temporaryPath, destinationPath);
                }

                return GameSaveRepositoryResult<GameSaveData>.Succeeded(save);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is NotSupportedException)
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    $"寫入存檔 '{save.SaveId}' 失敗：{exception.Message}");
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        public GameSaveRepositoryResult<bool> Delete(string saveId)
        {
            if (!TryGetSavePath(saveId, out string path, out string pathError))
                return GameSaveRepositoryResult<bool>.Failed(pathError);

            try
            {
                if (!File.Exists(path))
                    return GameSaveRepositoryResult<bool>.Succeeded(false);

                File.Delete(path);
                return GameSaveRepositoryResult<bool>.Succeeded(true);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException)
            {
                return GameSaveRepositoryResult<bool>.Failed(
                    $"刪除存檔 '{saveId}' 失敗：{exception.Message}");
            }
        }

        private static GameSaveRepositoryResult<GameSaveData>
            DeserializeAndMigrate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    "檔案內容為空。");
            }

            try
            {
                GameSaveVersionHeader header =
                    JsonUtility.FromJson<GameSaveVersionHeader>(json);

                if (header == null || header.SaveVersion < 1)
                {
                    return GameSaveRepositoryResult<GameSaveData>.Failed(
                        "缺少有效的 SaveVersion。");
                }

                if (header.SaveVersion > GameSaveSchema.CurrentVersion)
                {
                    return GameSaveRepositoryResult<GameSaveData>.Failed(
                        $"存檔版本 {header.SaveVersion} 高於目前支援版本 " +
                        $"{GameSaveSchema.CurrentVersion}。");
                }

                switch (header.SaveVersion)
                {
                    case 1:
                    {
                        GameSaveData save =
                            JsonUtility.FromJson<GameSaveData>(json);
                        if (save == null)
                        {
                            return GameSaveRepositoryResult<GameSaveData>.Failed(
                                "JSON 未產生有效存檔資料。");
                        }

                        save.Normalize();
                        string error = ValidateLoadedData(save);
                        return string.IsNullOrEmpty(error)
                            ? GameSaveRepositoryResult<GameSaveData>.Succeeded(save)
                            : GameSaveRepositoryResult<GameSaveData>.Failed(error);
                    }
                    default:
                        return GameSaveRepositoryResult<GameSaveData>.Failed(
                            $"尚未提供存檔版本 {header.SaveVersion} 的遷移器。");
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException ||
                exception is InvalidOperationException)
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    $"JSON 格式無效：{exception.Message}");
            }
        }

        private static string ValidateForWrite(GameSaveData save)
        {
            if (save == null)
                return "不能寫入空的存檔資料。";

            if (save.SaveVersion != GameSaveSchema.CurrentVersion)
            {
                return $"只能寫入目前版本 {GameSaveSchema.CurrentVersion}，" +
                       $"收到版本 {save.SaveVersion}。";
            }

            return ValidateLoadedData(save);
        }

        private static string ValidateLoadedData(GameSaveData save)
        {
            if (!IsValidSaveId(save.SaveId))
                return $"SaveId '{save.SaveId}' 不是有效的 GUID N 格式。";

            if (string.IsNullOrWhiteSpace(save.DisplayName))
                return "DisplayName 不可為空。";

            if (!TryParseUtc(save.CreatedUtc))
                return "CreatedUtc 不是有效的 UTC 時間。";

            if (!TryParseUtc(save.LastPlayedUtc))
                return "LastPlayedUtc 不是有效的 UTC 時間。";

            if (save.PermanentProgression == null)
                return "PermanentProgression 不可為空。";

            if (save.RunProgression == null)
                return "RunProgression 不可為空。";

            return string.Empty;
        }

        private bool TryGetSavePath(
            string saveId,
            out string path,
            out string error)
        {
            path = string.Empty;
            error = string.Empty;

            if (!IsValidSaveId(saveId))
            {
                error = $"SaveId '{saveId}' 不是有效的 GUID N 格式。";
                return false;
            }

            string candidate = Path.GetFullPath(
                Path.Combine(RootDirectory, saveId + SaveFileExtension));
            string rootWithSeparator =
                RootDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(
                    rootWithSeparator,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "存檔路徑超出允許的目錄。";
                return false;
            }

            path = candidate;
            return true;
        }

        private static bool IsValidSaveId(string saveId)
        {
            return !string.IsNullOrWhiteSpace(saveId) &&
                   Guid.TryParseExact(saveId, "N", out _);
        }

        private static bool TryParseUtc(string value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed) &&
                parsed.Kind == DateTimeKind.Utc;
        }

        private static void WriteAndFlush(string path, string content)
        {
            var utf8WithoutBom = new UTF8Encoding(false);
            using (var stream = new FileStream(
                       path,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, utf8WithoutBom))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static void TryDeleteTemporaryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static GameSaveSummary CreateSummary(GameSaveData save)
        {
            int hostPlayerLevel = 1;
            List<PlayerRunProgressionData> players =
                save.RunProgression.PlayerProgressionEntries;

            for (int i = 0; i < players.Count; i++)
            {
                if (string.Equals(
                        players[i].StablePlayerId,
                        GameSaveSchema.HostPlayerId,
                        StringComparison.Ordinal))
                {
                    hostPlayerLevel = players[i].PlayerLevel;
                    break;
                }
            }

            return new GameSaveSummary
            {
                SaveId = save.SaveId,
                DisplayName = save.DisplayName,
                CreatedUtc = save.CreatedUtc,
                LastPlayedUtc = save.LastPlayedUtc,
                StageLevel = save.RunProgression.StageLevel,
                HostPlayerLevel = hostPlayerLevel
            };
        }

        private static int CompareByLastPlayedDescending(
            GameSaveSummary left,
            GameSaveSummary right)
        {
            DateTime.TryParse(
                left.LastPlayedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime leftTime);
            DateTime.TryParse(
                right.LastPlayedUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime rightTime);
            return rightTime.CompareTo(leftTime);
        }

        [Serializable]
        private sealed class GameSaveVersionHeader
        {
            public int SaveVersion;
        }
    }
}
