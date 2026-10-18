using System.Reflection;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class SafeHouseGameplayRulesTests
{
    private static object Call(string method, params object[] args)
    {
        var type = typeof(Player).Assembly.GetType("Purgers.GameFlow.SafeHouse.SafeHouseGameplayRules");
        Assert.That(type, Is.Not.Null, "SafeHouse gameplay rules are not implemented.");
        return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, args);
    }
    [Test] public void SafeHouseBlocksOnlyGrappleAndActiveAbility()
    {
        var mask = (PlayerActionBlockMask)Call("BlockedActions", true);
        Assert.That((int)mask, Is.EqualTo((int)PlayerActionBlockMask.Grapple | (1 << 7)));
        Assert.That(mask & (PlayerActionBlockMask.Fire | PlayerActionBlockMask.Aim |
            PlayerActionBlockMask.Reload | PlayerActionBlockMask.QuickAction |
            PlayerActionBlockMask.Movement | PlayerActionBlockMask.Look), Is.EqualTo(PlayerActionBlockMask.None));
    }
    [Test] public void LeavingSafeHouseRemovesOnlyItsOwnRestrictions()
    {
        var other = PlayerActionBlockMask.Reload | PlayerActionBlockMask.Grapple;
        Assert.That(other | (PlayerActionBlockMask)Call("BlockedActions", false), Is.EqualTo(other));
    }
    [TestCase(true, true, true)] [TestCase(true, false, false)]
    [TestCase(false, true, false)] [TestCase(false, false, false)]
    public void DamagePolicyProtectsAgainstPlayersOnlyInsideSafeHouse(bool safeHouse, bool playerSource, bool blocked)
    {
        Assert.That((bool)Call("BlocksPlayerDamage", safeHouse, playerSource), Is.EqualTo(blocked));
    }
    [TestCase(DamageType.Bullet)] [TestCase(DamageType.Melee)] [TestCase(DamageType.Explosion)] [TestCase(DamageType.DamageOverTime)]
    public void PlayerAttributionSurvivesMissingSourceObjects(DamageType type)
    {
        var request = new DamageRequest { DamageType=type, Attacker=Fusion.PlayerRef.FromIndex(0) };
        Assert.That((bool)Call("HasPlayerSource", request), Is.True);
    }
    [Test] public void AnonymousEnvironmentIsNotPlayerDamage()
    {
        Assert.That((bool)Call("HasPlayerSource", new DamageRequest { Attacker=Fusion.PlayerRef.None, DamageType=DamageType.Environment }), Is.False);
    }
    [Test] public void ExistingHudSubtreesBindToSafeHouseWithoutStageController()
    {
        var root = new UnityEngine.GameObject("SafeHouse HUD test", typeof(UnityEngine.RectTransform));
        var owner = root.AddComponent<Purgers.GameFlow.SafeHouse.SafeHouseHudController>();
        var template = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        try
        {
            var ability = UnityEngine.Object.Instantiate(template.GetComponentInChildren<LocalPlayerBattleAbilityHUD>(true).gameObject, root.transform).GetComponent<LocalPlayerBattleAbilityHUD>();
            var reward = UnityEngine.Object.Instantiate(template.GetComponentInChildren<LocalPlayerRewardHUD>(true).gameObject, root.transform).GetComponent<LocalPlayerRewardHUD>();
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(LocalPlayerBattleAbilityHUD).GetMethod("Awake",flags).Invoke(ability,null);
            typeof(LocalPlayerRewardHUD).GetMethod("Awake",flags).Invoke(reward,null);
            Assert.That(typeof(LocalPlayerBattleAbilityHUD).GetField("safeHouseHud",flags).GetValue(ability), Is.SameAs(owner));
            Assert.That(typeof(LocalPlayerRewardHUD).GetField("ownerSafeHouseHud",flags).GetValue(reward), Is.SameAs(owner));
            Assert.That(reward.CanPresentRewardSelection, Is.True, "複製完整子樹須保留 ALT 與 XP 所有引用。");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    [Test] public void UnspawnedPlayerGateDoesNotReadNetworkedState()
    {
        var go = new UnityEngine.GameObject("SafeHouse gate lifecycle test");
        try
        {
            var gate = go.AddComponent<PlayerActionGate>();
            Assert.That(gate.CurrentBlockedActions, Is.EqualTo(PlayerActionBlockMask.None));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
