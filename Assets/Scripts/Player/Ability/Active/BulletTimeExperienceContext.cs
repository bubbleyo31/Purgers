using System;
using System.Collections.Generic;
using Fusion;

/// <summary>施放當下的玩家與 PlayerObject 身分快照，防止重生或重連者繼承舊施放的資格。</summary>
public struct BulletTimeParticipant : INetworkStruct
{
    public PlayerRef Player;
    public NetworkId PlayerObjectId;
}

/// <summary>
/// 只跟著本次延後傷害傳入正式死亡事件的權威分配資料。
/// 不放入存檔、不另發死亡事件，且不依賴到期後可能已 Despawn 的技能 Runtime。
/// </summary>
public sealed class BulletTimeExperienceContext
{
    private readonly BulletTimeParticipant[] participants;
    public NetworkRunner Runner { get; }
    public int ParticipantCount => participants.Length;

    public BulletTimeExperienceContext(NetworkRunner runner, IReadOnlyList<BulletTimeParticipant> snapshot)
    {
        Runner = runner;
        participants = new BulletTimeParticipant[snapshot != null ? snapshot.Count : 0];
        for (int i = 0; i < participants.Length; i++) participants[i] = snapshot[i];
    }

    public BulletTimeParticipant GetParticipant(int index) => participants[index];
}
