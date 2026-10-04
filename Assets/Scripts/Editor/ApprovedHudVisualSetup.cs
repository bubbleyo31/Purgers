using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>僅遷移已確認的獎勵、技能槽與血格；不重建全版 HUD，不碰能力／獎勵資料。</summary>
public static class ApprovedHudVisualSetup
{
    const string PrefabPath="Assets/Prefabs/UI/StageHUD.prefab";
    const string ArtRoot="Assets/_Project_Assets/UI/BattleHUD/ApprovedV2/";
    static readonly Color Cream=new Color32(241,237,217,255), Ink=new Color32(25,32,20,255), Gold=new Color32(213,196,94,255);
    static Material paper;
    static Sprite square;
    static void Set(UnityEngine.Object target,string name,object value){
        var so=new SerializedObject(target);var p=so.FindProperty(name);
        if(p==null)throw new InvalidOperationException(target.name+" 缺少 "+name);
        if(value is Color c)p.colorValue=c;else if(value is float f)p.floatValue=f;
        else if(value is Vector2 v)p.vector2Value=v;else p.objectReferenceValue=value as UnityEngine.Object;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static RectTransform Rect(Transform parent,string name)=>parent.Find(name) as RectTransform;
    static void Box(RectTransform r,Vector2 pos,Vector2 size){r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=pos;r.sizeDelta=size;r.localScale=Vector3.one;}
    static RectTransform Child(Transform parent,string name){
        var r=Rect(parent,name);if(r!=null)return r;
        var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;
    }
    static Image Image(RectTransform r,Color tint,bool grain=false){
        var im=r.GetComponent<Image>()??r.gameObject.AddComponent<Image>();im.color=tint;im.raycastTarget=false;if(grain)im.material=paper;return im;
    }
    static void Type(TMP_Text text,float size,Color tint){
        text.fontSize=size;text.enableAutoSizing=false;text.color=tint;text.alignment=TextAlignmentOptions.TopLeft;
        text.enableWordWrapping=true;text.overflowMode=TextOverflowModes.Overflow;text.margin=Vector4.zero;
        text.characterSpacing=0;text.wordSpacing=0;text.lineSpacing=0;text.paragraphSpacing=0;text.richText=false;
    }
    static void PrepareArt(){
        Directory.CreateDirectory(ArtRoot);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(ArtRoot+"PurgersPaper.shader");
        if(shader==null || ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Paper shader 未就緒");
        paper=AssetDatabase.LoadAssetAtPath<Material>(ArtRoot+"Paper.mat");
        if(paper==null){paper=new Material(shader);paper.SetFloat("_Grain",.028f);AssetDatabase.CreateAsset(paper,ArtRoot+"Paper.mat");}
        string path=ArtRoot+"SkillSquare.png";
        if(!File.Exists(path)){
            const int n=128;var pixels=new Color32[n*n];
            for(int y=0;y<n;y++)for(int x=0;x<n;x++){
                float dx=Mathf.Max(Mathf.Abs(x+.5f-n*.5f)-(n*.5f-5),0),dy=Mathf.Max(Mathf.Abs(y+.5f-n*.5f)-(n*.5f-5),0);
                byte alpha=(byte)Mathf.RoundToInt(Mathf.Clamp01(5-Mathf.Sqrt(dx*dx+dy*dy))*255);
                pixels[y*n+x]=new Color32(255,255,255,alpha);
            }
            var tex=new Texture2D(n,n,TextureFormat.RGBA32,false);tex.SetPixels32(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.spriteBorder=new Vector4(6,6,6,6);importer.SaveAndReimport();
        }
        square=AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    [MenuItem("Tools/Purgers/UI/Apply Approved Reward Skill Health Visuals")]
    public static void Apply(){
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("請退出 Play Mode");
        var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/_Menu.unity" || scene.isDirty)throw new InvalidOperationException("需已儲存的 _Menu；避免連帶保存其他人工修改");
        PrepareArt();
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try{StyleStage(root);PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        foreach(var go in scene.GetRootGameObjects())foreach(var view in go.GetComponentsInChildren<LocalHealthSegmentView>(true))StyleHealth(view);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
    }
    static void StyleStage(GameObject root){
        var frame=root.transform.Find("ReferenceFrame");var battle=frame.Find("BattleAbilityHUD");
        var slots=Rect(battle,"AbilitySlots"); // 保留既有群組起點；縮小槽位，在原區域中留白。
        slots.sizeDelta=new Vector2(198,80);
        var layout=slots.GetComponent<HorizontalLayoutGroup>();layout.spacing=22;layout.childAlignment=TextAnchor.UpperLeft;
        var slot=Rect(slots,"AbilitySlotTemplate");slot.sizeDelta=new Vector2(80,80);
        var le=slot.GetComponent<LayoutElement>();if(le!=null){le.minWidth=le.preferredWidth=80;le.minHeight=le.preferredHeight=80;}
        var background=Image(slot,Cream,true);background.sprite=square;background.type=UnityEngine.UI.Image.Type.Sliced;
        var icon=Rect(slot,"Icon");icon.anchorMin=icon.anchorMax=icon.pivot=Vector2.one*.5f;icon.anchoredPosition=Vector2.zero;icon.sizeDelta=new Vector2(60,60);
        var im=Image(icon,Ink);im.preserveAspect=true;
        var fallback=slot.Find("Fallback").GetComponent<TMP_Text>();fallback.rectTransform.sizeDelta=new Vector2(60,60);fallback.color=Ink;fallback.fontSize=40;
        var shade=Rect(slot,"CooldownShade");var shadeImage=Image(shade,new Color(0,0,0,.65f));shadeImage.sprite=square;shadeImage.type=UnityEngine.UI.Image.Type.Sliced;
        var number=slot.Find("CooldownText").GetComponent<TMP_Text>();number.rectTransform.anchorMin=Vector2.zero;number.rectTransform.anchorMax=Vector2.one;number.rectTransform.offsetMin=number.rectTransform.offsetMax=Vector2.zero;number.alignment=TextAlignmentOptions.Center;number.fontSize=30;
        var line=Child(slot,"AccentLine");Box(line,new Vector2(7,-85),new Vector2(66,3));Image(line,Gold);
        var key=Rect(slot,"Key");Box(key,new Vector2(24,-99),new Vector2(32,32));Image(key,Cream,true).sprite=square;
        var label=key.Find("Label").GetComponent<TMP_Text>();label.color=Ink;label.fontSize=21;label.alignment=TextAlignmentOptions.Center;
        Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"focusKeyOffset",Vector2.zero);
        Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"slotReadyColor",Cream);
        Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"slotPressedColor",Gold);
        foreach(string name in new[]{"LeftCard","MiddleCard","RightCard"}){
            var card=frame.Find("Phase6RewardHud/ChoiceWindow/"+name);
            var panel=Rect(card,"DescriptionPanel");Image(panel,Cream,true);
            Image(Rect(card,"IconPlate"),Cream,true);Image(Rect(card,"IconBracket"),Gold,true);
            var rewardIcon=Rect(card,"Icon"); var iconCenter=rewardIcon.anchoredPosition+new Vector2(rewardIcon.sizeDelta.x*.5f,-rewardIcon.sizeDelta.y*.5f);
            rewardIcon.sizeDelta=new Vector2(72,72); rewardIcon.anchoredPosition=iconCenter+new Vector2(-36,36);Image(rewardIcon,Ink).preserveAspect=true;
            var p=panel.anchoredPosition;var size=panel.sizeDelta;
            var title=card.Find("Title").GetComponent<TMP_Text>();Box(title.rectTransform,p+new Vector2(16,-5),new Vector2(size.x-32,30));Type(title,20,Ink);
            var description=card.Find("Description").GetComponent<TMP_Text>();Box(description.rectTransform,p+new Vector2(16,-39),new Vector2(size.x-32,size.y-47));Type(description,16,Ink);
            var rule=Child(card,"DescriptionRule");Box(rule,p+new Vector2(16,-35),new Vector2(size.x-32,1));Image(rule,new Color(.34f,.38f,.26f,.35f));
            var accent=Child(card,"DescriptionAccent");Box(accent,p+new Vector2(16,-35),new Vector2(14,1));Image(accent,Gold);
            var fall=card.Find("FallbackIcon").GetComponent<TMP_Text>();fall.color=Ink;
        }
    }
    static void StyleHealth(LocalHealthSegmentView view){
        var r=view.rectTransform;r.pivot=Vector2.zero;r.anchoredPosition=new Vector2(0,-47);r.sizeDelta=new Vector2(420,26);
        Set(view,"segmentWidth",18f);Set(view,"segmentGap",3f);Set(view,"segmentSlant",4f);Set(view,"cornerRadius",0f);Set(view,"segmentRise",0f);
        Set(view,"healthyColor",Cream);Set(view,"shieldColor",Gold);Set(view,"animationDuration",.3f);
        var so=new SerializedObject(view);so.FindProperty("segmentOutline").arraySize=0;so.ApplyModifiedPropertiesWithoutUndo();
        var healthRoot=view.transform.parent.parent;var cross=view.transform.parent.Find("HealthCross")??healthRoot.Find("HealthCross");if(cross!=null)cross.gameObject.SetActive(false);
        var old=healthRoot.Find("Text (TMP)[HealthBar]");if(old!=null)old.gameObject.SetActive(false);
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project_Assets/UI/BattleHUD/Fonts/NotoSansTC-Bold HUD SDF.asset");
        var number=Child(view.transform.parent,"WholeHealthValue");Box(number,new Vector2(0,13),new Vector2(175,30));
        var text=number.GetComponent<TextMeshProUGUI>()??number.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;Type(text,22,Cream);text.raycastTarget=false;
        var shield=Child(view.transform.parent,"WholeShieldValue");Box(shield,new Vector2(180,13),new Vector2(150,30));
        var st=shield.GetComponent<TextMeshProUGUI>()??shield.gameObject.AddComponent<TextMeshProUGUI>();st.font=font;Type(st,22,Gold);st.raycastTarget=false;
        Set(view,"healthValueText",text);Set(view,"shieldValueText",st);view.ResetPresentation();view.SendMessage("Update");
        EditorUtility.SetDirty(view);
    }
}
