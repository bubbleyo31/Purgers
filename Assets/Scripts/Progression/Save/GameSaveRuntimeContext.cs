using System;
using UnityEngine;

namespace Purgers.Progression
{
    public enum GameSaveAccessMode
    {
        None = 0,
        HostWritable = 1,
        ClientReadOnly = 2
    }

    public sealed class GameSaveRuntimeContext : MonoBehaviour
    {
        private IGameSaveRepository repository;

        public GameSaveAccessMode AccessMode { get; private set; }
        public GameSaveData ActiveSave { get; private set; }

        public bool HasWritableHostSave =>
            AccessMode == GameSaveAccessMode.HostWritable &&
            ActiveSave != null &&
            repository != null;

        public void InitializeHost(
            IGameSaveRepository saveRepository,
            GameSaveData save)
        {
            repository = saveRepository ??
                throw new ArgumentNullException(nameof(saveRepository));
            ActiveSave = save ?? throw new ArgumentNullException(nameof(save));
            AccessMode = GameSaveAccessMode.HostWritable;
        }

        public void InitializeClientReadOnly()
        {
            repository = null;
            ActiveSave = null;
            AccessMode = GameSaveAccessMode.ClientReadOnly;
        }

        public GameSaveRepositoryResult<GameSaveData> WriteActiveSave()
        {
            if (!HasWritableHostSave)
            {
                return GameSaveRepositoryResult<GameSaveData>.Failed(
                    "目前 Runner 沒有可由 Host 寫入的存檔。");
            }

            return repository.Write(ActiveSave);
        }

        public void Clear()
        {
            repository = null;
            ActiveSave = null;
            AccessMode = GameSaveAccessMode.None;
        }
    }
}
