using System;
using System.Collections.Generic;

namespace Purgers.Progression
{
    public interface IGameSaveRepository
    {
        string RootDirectory { get; }

        GameSaveRepositoryResult<GameSaveData> CreateNew(string displayName);
        GameSaveCatalog List();
        GameSaveRepositoryResult<GameSaveData> Load(string saveId);
        GameSaveRepositoryResult<GameSaveData> Write(GameSaveData save);
        GameSaveRepositoryResult<bool> Delete(string saveId);
    }

    public readonly struct GameSaveRepositoryResult<T>
    {
        private GameSaveRepositoryResult(bool success, T value, string error)
        {
            Success = success;
            Value = value;
            Error = error;
        }

        public bool Success { get; }
        public T Value { get; }
        public string Error { get; }

        public static GameSaveRepositoryResult<T> Succeeded(T value)
        {
            return new GameSaveRepositoryResult<T>(true, value, string.Empty);
        }

        public static GameSaveRepositoryResult<T> Failed(string error)
        {
            return new GameSaveRepositoryResult<T>(
                false,
                default(T),
                string.IsNullOrWhiteSpace(error) ? "Unknown save error." : error);
        }
    }

    public sealed class GameSaveCatalog
    {
        public GameSaveCatalog(
            IReadOnlyList<GameSaveSummary> summaries,
            IReadOnlyList<string> errors)
        {
            Summaries = summaries ?? Array.Empty<GameSaveSummary>();
            Errors = errors ?? Array.Empty<string>();
        }

        public IReadOnlyList<GameSaveSummary> Summaries { get; }
        public IReadOnlyList<string> Errors { get; }
    }

    public sealed class GameSaveSummary
    {
        public string SaveId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string CreatedUtc { get; set; } = string.Empty;
        public string LastPlayedUtc { get; set; } = string.Empty;
        public int StageLevel { get; set; } = 1;
        public int HostPlayerLevel { get; set; } = 1;
    }
}
