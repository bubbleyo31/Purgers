using System.Collections.Generic;
using System.Reflection;
using Fusion;
using NUnit.Framework;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class NetworkChatDepartureTests
{
    private GameObject root;
    private NetworkChatSystem chat;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Chat departure test");
        chat = root.AddComponent<NetworkChatSystem>();
    }

    [TearDown]
    public void TearDown() => Object.DestroyImmediate(root);

    [Test]
    public void DepartureRetainsValidatedNameAndConsumesItOnce()
    {
        var field = typeof(NetworkChatSystem).GetField("registeredPlayers", PrivateInstance);
        Assert.That(field.GetValue(chat), Is.InstanceOf<Dictionary<PlayerRef, string>>());
        var names = (Dictionary<PlayerRef, string>)field.GetValue(chat);
        var player = PlayerRef.FromIndex(1);
        names.Add(player, "已驗證名稱");
        var take = typeof(NetworkChatSystem).GetMethod("TryTakeDepartingPlayerName", PrivateInstance);
        Assert.That(take, Is.Not.Null);
        object[] args = { player, null };
        Assert.That(take.Invoke(chat, args), Is.True);
        Assert.That(args[1], Is.EqualTo("已驗證名稱"));
        Assert.That(take.Invoke(chat, args), Is.False);
        names.Add(player, "重新加入名稱");
        Assert.That(take.Invoke(chat, args), Is.True);
        Assert.That(args[1], Is.EqualTo("重新加入名稱"));
    }

    [Test]
    public void DepartureUsesExistingLocalHistoryAndSharedSequence()
    {
        var receive = typeof(NetworkChatSystem).GetMethod("ReceivePlayerPresenceMessage", PrivateInstance);
        Assert.That(receive, Is.Not.Null);
        receive.Invoke(chat, new object[] { 1, "小明", false });
        receive.Invoke(chat, new object[] { 2, "小明", true });
        receive.Invoke(chat, new object[] { 2, "小明", true });
        receive.Invoke(chat, new object[] { 1, "小明", false });
        Assert.That(chat.LocalHistory.Count, Is.EqualTo(2));
        Assert.That(chat.LocalHistory[0].Text, Is.EqualTo("小明 加入了遊戲"));
        Assert.That(chat.LocalHistory[1].Text, Is.EqualTo("小明 離開了遊戲"));
        Assert.That(chat.LocalHistory[1].Kind.ToString(), Is.EqualTo("PlayerLeft"));
    }

    [Test]
    public void UnknownDepartureCannotInventANameOrConsumeAnotherPlayer()
    {
        var field = typeof(NetworkChatSystem).GetField("registeredPlayers", PrivateInstance);
        Assert.That(field.GetValue(chat), Is.InstanceOf<Dictionary<PlayerRef, string>>());
        var names = (Dictionary<PlayerRef, string>)field.GetValue(chat);
        names.Add(PlayerRef.FromIndex(1), "保留玩家");
        var take = typeof(NetworkChatSystem).GetMethod("TryTakeDepartingPlayerName", PrivateInstance);
        Assert.That(take, Is.Not.Null);
        Assert.That(take.Invoke(chat, new object[] { PlayerRef.FromIndex(2), null }), Is.False);
        Assert.That(names.Count, Is.EqualTo(1));
    }
}
