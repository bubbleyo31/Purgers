using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;

[Category("PurgersRegression")]
public sealed class TeamInfoSnapshotTests
{
    [Test] public void DeathSnapshotFitsAllRuntimeSlotsWithoutLargeNetworkStrings()
    {
        Assert.AreEqual(PlayerAbilityRuntimeManager.MaximumAbilityRuntimeSlots,TeamAbilityDisplaySnapshot.Capacity);
        Assert.AreEqual(64,Marshal.SizeOf(typeof(TeamAbilityDisplaySnapshot)));
        var ids=Enumerable.Range(0,TeamAbilityDisplaySnapshot.Capacity).Select(i=>"test.ability."+i).ToArray();
        var saved=TeamAbilityDisplaySnapshot.Capture(ids);
        for(int i=0;i<ids.Length;i++)Assert.AreEqual(TeamAbilityDisplaySnapshot.Token(ids[i]),saved.At(i));
        Assert.IsFalse(saved.Contains(""));Assert.IsFalse(saved.Contains("other"));
    }
    [Test] public void ProductionCatalogTokensAreUniqueAndResolveToOriginalDefinitions()
    {
        var settings=TeamInfoPanelSettings.Load();Assert.NotNull(settings);Assert.NotNull(settings.catalog);
        var tokens=new Dictionary<ulong,string>();int checkedCount=0;
        foreach(var reward in settings.catalog.Rewards)
        {
            var definition=reward!=null?reward.AbilityDefinition:null;if(definition==null)continue;
            Assert.IsNotEmpty(definition.AbilityId);
            ulong token=TeamAbilityDisplaySnapshot.Token(definition.AbilityId);
            if(tokens.TryGetValue(token,out var previous))Assert.AreEqual(previous,definition.AbilityId,"Display token collision");
            tokens[token]=definition.AbilityId;
            var saved=TeamAbilityDisplaySnapshot.Capture(new[]{definition.AbilityId});
            Assert.AreEqual(definition.AbilityId,saved.Resolve(0,settings.catalog).AbilityId);checkedCount++;
        }
        Assert.Greater(checkedCount,0);
    }
}
