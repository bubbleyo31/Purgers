using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>只用複製的 HUD 做靜態版面驗證，不能作為實際網路技能執行證據。</summary>
public static class ActiveAbilityVisualPreview
{
    const string PrefabPath = "Assets/Prefabs/UI/StageHUD.prefab";
    const string ShapePath = "Assets/_Project_Assets/UI/BattleHUD/Layout/";
    static Sprite Shape(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ShapePath + name + ".png");
    public static string RenderHudPreview(string abilityKey = "Shield", string abilityName = "快速護盾", string phase = "效果持續", int width = 1920, int height = 1080)
    {
        bool rewards = false, healthDetail = false; float healthValue = 80f, reloadProgress = -1f;
        var activeScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        RenderTexture target = null; Texture2D output = null;
        Camera previewCamera = null;
        var previous = RenderTexture.active;
        try
        {
            var cameraObject = new GameObject("HUD proof camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();previewCamera=camera;camera.orthographic=true;
            camera.orthographicSize=healthDetail?70f:height*.5f;camera.transform.position=healthDetail?new Vector3(-660,-450,-10):new Vector3(0,0,-10);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.09f,.12f,.16f);
            camera.nearClipPlane=.1f;camera.farClipPlane=100;camera.cullingMask=1<<31;
            var canvasObject=new GameObject("HUD proof canvas",typeof(RectTransform),typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasObject,scene);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=camera;
            var canvasRect=(RectTransform)canvas.transform;canvasRect.sizeDelta=healthDetail?new Vector2(1920,1080):new Vector2(width,height);
            var playerCanvas=UnityEngine.Object.FindObjectsOfType<Canvas>(true).First(c=>c.name=="PlayerHUDCanvas");
            var player=UnityEngine.Object.Instantiate(playerCanvas.transform.Find("ReferenceFrame").gameObject,canvas.transform);
            var stageAsset=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var stage=UnityEngine.Object.Instantiate(stageAsset.transform.Find("ReferenceFrame").gameObject,canvas.transform);
            foreach(var root in new[]{player,stage})
            {
                root.SetActive(true);
                foreach(var b in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if(b is LocalPlayerRewardHUD || b is LocalPlayerBattleAbilityHUD || b is LocalPlayerWeaponHUD || b is LocalPlayerHealthSlider || b is LocalPlayerSpeedSlider || b is LocalChatPanel || b is LocalPlayerGrappleAimIndicator)b.enabled=false;
                root.GetComponent<BattleHudReferenceFrame>().Refresh();
                foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            }
            var playerRoot=player.transform;var stageRoot=stage.transform;
            var pc=playerRoot.Find("PlayerHUDController");
            pc.Find("GrappleValidIndicatorGrpple").gameObject.SetActive(false);pc.Find("GrappleValidIndicatorEnemy").gameObject.SetActive(false);
            playerRoot.Find("Speed_UI").gameObject.SetActive(true);
            var health=player.GetComponentInChildren<LocalHealthSegmentView>(true);health.gameObject.SetActive(true);health.GetComponentInParent<Slider>().value=healthValue;health.SendMessage("Update");
            var weapon=pc.Find("WeaponHUDRoot");weapon.gameObject.SetActive(true);
            weapon.Find("WeaponIconImage").gameObject.SetActive(true);
            weapon.Find("CurrentAmmoText").GetComponent<TMP_Text>().text="16";weapon.Find("MagazineCapacityText").GetComponent<TMP_Text>().text="/25";
            weapon.Find("ReloadReminder").gameObject.SetActive(true);weapon.Find("ReloadProgressRoot").gameObject.SetActive(false);
            foreach(Transform child in playerRoot.Find("Crosshair"))child.gameObject.SetActive(child.name=="Center");
            var messages=playerRoot.Find("ChatController/ChatVisualRoot");messages.gameObject.SetActive(true);
            foreach(var group in messages.GetComponentsInChildren<CanvasGroup>(true))group.alpha=1;
            messages.Find("MessageScrollView/Viewport/Content/MessagesText").GetComponent<TMP_Text>().text="Player1234加入了遊戲";
            health.SetShieldHealth(abilityKey == "Shield" ? 25f : 0f);
            var battle=stageRoot.Find("BattleAbilityHUD");battle.GetComponent<CanvasGroup>().alpha=1;
            battle.Find("EmptyGrappleMark").gameObject.SetActive(false);
            var slots=battle.Find("AbilitySlots");var template=slots.Find("AbilitySlotTemplate").gameObject;
            var previewLoadout=AssetDatabase.LoadAssetAtPath<PlayerAbilityLoadoutDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Loadouts/DefaultPlayerAbilityLoadout.asset");
            for(int i=0;i<2;i++)
            {
                var slot=UnityEngine.Object.Instantiate(template,slots);slot.name="PreviewSlot"+i;slot.SetActive(true);
                var previewAbility=previewLoadout.EquippedAbilities.FirstOrDefault(a=>a!=null && a.Category==(i==0?PlayerAbilityCategory.GrappleHit:PlayerAbilityCategory.GrappleFocus));
                slot.transform.Find("Icon").GetComponent<Image>().sprite=previewAbility!=null?previewAbility.HudIcon:Shape("SkillStar");slot.transform.Find("Icon").GetComponent<Image>().enabled=true;
                slot.transform.Find("Fallback").gameObject.SetActive(false);slot.transform.Find("CooldownShade").gameObject.SetActive(false);slot.transform.Find("CooldownText").gameObject.SetActive(false);
                slot.transform.Find("Key/Label").GetComponent<TMP_Text>().text=i==0?"Q":"E";
                if(i==1)
                {
                    var so=new SerializedObject(battle.GetComponent<LocalPlayerBattleAbilityHUD>());
                    ((RectTransform)slot.transform.Find("Key")).anchoredPosition+=so.FindProperty("focusKeyOffset").vector2Value;
                }
            }
            var icon=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Active/"+abilityKey+".asset").HudIcon;
            slots.Find("PreviewSlot1/Icon").GetComponent<Image>().sprite=icon;
            var bar=battle.Find("TimedAbilityBar"); bar.gameObject.SetActive(true);
            bar.Find("Name").GetComponent<TMP_Text>().text=abilityName;
            bar.Find("State").GetComponent<TMP_Text>().text=phase;
            bar.Find("Seconds").GetComponent<TMP_Text>().text="1.2s";
            float duration = phase == "待命" ? 5f : abilityKey == "Shield" ? 1.5f : 2f;
            ((RectTransform)bar.Find("Fill")).anchorMax=new Vector2(1.2f / duration,1f);
            var hudSettings = new SerializedObject(battle.GetComponent<LocalPlayerBattleAbilityHUD>());
            bar.Find("Fill").GetComponent<Image>().color = hudSettings.FindProperty(
                phase == "待命" ? "armedBarColor" : phase == "施放準備" ? "castingBarColor" : "activeBarColor").colorValue;
            var reward=stageRoot.Find("Phase6RewardHud");reward.Find("ExperienceGroup").GetComponent<CanvasGroup>().alpha=1;
            reward.Find("ExperienceGroup/AltPrompt").GetComponent<CanvasGroup>().alpha=0;
            reward.Find("ChoiceWindow").GetComponent<CanvasGroup>().alpha=rewards?1:0;
            reward.Find("BackgroundDim").GetComponent<CanvasGroup>().alpha=0;
            string[] names={"LeftCard","MiddleCard","RightCard"},icons={"GrappleMark","AerialSlow","GrappleGather"};
            for(int i=0;i<3;i++)
            {
                var card=reward.Find("ChoiceWindow/"+names[i]);card.gameObject.SetActive(true);
                card.Find("Icon").GetComponent<Image>().sprite=i==0?Shape("SkillStar"):AssetDatabase.LoadAllAssetsAtPath(ShapePath+icons[i]+"Tight.png").OfType<Sprite>().First();card.Find("Icon").GetComponent<Image>().enabled=true;card.Find("FallbackIcon").gameObject.SetActive(false);
                card.Find("Title").GetComponent<TMP_Text>().text=new[]{"命中標記","空中緩降","鈎索聚集"}[i];
                card.Find("Description").GetComponent<TMP_Text>().text=new[]{"鈎索命中後標記目標，提升後續攻擊效果。","鈎索騰空時按住 E，延長空中停留時間。","鈎索命中後聚集周圍目標。"}[i];
            }
            stageRoot.Find("TopLeftColumn/StagePanel/Timer").GetComponent<TMP_Text>().text="08:36";
            stageRoot.Find("TopLeftColumn/StagePanel/ExtractionStatus").gameObject.SetActive(false);
            stageRoot.Find("TopLeftColumn/StagePanel/ExtractionProgress").gameObject.SetActive(false);
            if(reloadProgress>=0)
            {
                var hud=player.GetComponentInChildren<LocalPlayerWeaponHUD>(true);
                var snapshot=new PlayerWeaponHUDSnapshot(null,PlayerWeaponHUDValueMode.Ammunition,2,25,false,false,true,true,reloadProgress);
                typeof(LocalPlayerWeaponHUD).GetMethod("RefreshUIIfChanged",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(hud,new object[]{snapshot});
            }
            foreach(var t in canvasObject.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.antiAliasing=1;target.Create();camera.targetTexture=target;
            health.SendMessage("Update");
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)slots);
            foreach(var text in canvasObject.GetComponentsInChildren<TMP_Text>(true))
                if(text.isActiveAndEnabled) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active=target;output=new Texture2D(width,height,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,width,height),0,0);output.Apply();
            var directory="Documentation/ProjectArchitecture/Validation/BattleHUD";Directory.CreateDirectory(directory);
            string path=directory+"/E-"+abilityKey+"-"+(healthDetail?"health-detail":rewards?"reward":"normal")+"-"+width+"x"+height+(reloadProgress>=0?"-reload"+Mathf.RoundToInt(reloadProgress*100):"")+(healthValue<100?"-hp"+healthValue.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"")+".png";File.WriteAllBytes(path,output.EncodeToPNG());return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active=previous;if(previewCamera!=null)previewCamera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(output!=null)UnityEngine.Object.DestroyImmediate(output);
            EditorSceneManager.CloseScene(scene, true);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
        }
    }
}
