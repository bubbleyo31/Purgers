using System.Linq;
using NUnit.Framework;
using Purgers.Progression;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

[Category("PurgersRegression")]
public sealed class ApprovedHudVisualTests
{
    [Test] public void EveryRewardFitsAllThreeUnchangedDescriptionPanels()
    {
        var root=PrefabUtility.LoadPrefabContents("Assets/Prefabs/UI/StageHUD.prefab");
        try{
            var catalog=Resources.Load<PlayerRewardCatalog>("Progression/PlayerRewardCatalog");
            var window=root.transform.Find("ReferenceFrame/Phase6RewardHud/ChoiceWindow");
            foreach(string name in new[]{"LeftCard","MiddleCard","RightCard"}){
                var card=window.Find(name);var text=card.Find("Description").GetComponent<TMP_Text>();
                var title=card.Find("Title").GetComponent<TMP_Text>();
                Assert.That(text.alignment,Is.EqualTo(TextAlignmentOptions.TopLeft));
                Assert.That(text.enableAutoSizing,Is.False,"三框不可各自縮成不同字級");
                foreach(var reward in catalog.Rewards.Where(r=>r!=null)){
                    var preferred=text.GetPreferredValues(reward.Description,text.rectTransform.rect.width,Mathf.Infinity);
                    Assert.That(preferred.y,Is.LessThanOrEqualTo(text.rectTransform.rect.height+.1f),name+"/"+reward.name);
                    text.text=reward.Description; text.ForceMeshUpdate(true);
                    Assert.That(text.textInfo.characterCount,Is.GreaterThan(0));
                    foreach(var ch in text.textInfo.characterInfo.Take(text.textInfo.characterCount).Where(c=>c.isVisible)){Assert.That(ch.bottomLeft.x,Is.GreaterThanOrEqualTo(text.rectTransform.rect.xMin-.1f),reward.name);Assert.That(ch.topRight.x,Is.LessThanOrEqualTo(text.rectTransform.rect.xMax+.1f),reward.name);}
                    if(reward.AbilityDefinition!=null){
                        var tp=title.GetPreferredValues(reward.AbilityDefinition.DisplayName,title.rectTransform.rect.width,Mathf.Infinity);
                        Assert.That(tp.y,Is.LessThanOrEqualTo(title.rectTransform.rect.height+.1f),reward.name+" title");
                    }
                }
            }
        }finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    [Test] public void SkillTemplateIsSquareAndKeepsIndependentKeyAndCooldown()
    {
        var root=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/StageHUD.prefab");
        var slot=(RectTransform)root.transform.Find("ReferenceFrame/BattleAbilityHUD/AbilitySlots/AbilitySlotTemplate");
        Assert.That(slot.sizeDelta,Is.EqualTo(new Vector2(80,80)));
        Assert.That(slot.Find("Icon").GetComponent<Image>().preserveAspect,Is.True);
        Assert.That(slot.Find("Key"),Is.Not.Null);Assert.That(slot.Find("CooldownShade"),Is.Not.Null);
        Assert.That(slot.Find("AccentLine"),Is.Not.Null);
    }
}
