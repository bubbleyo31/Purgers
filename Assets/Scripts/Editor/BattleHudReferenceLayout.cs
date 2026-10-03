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

/// <summary>Editor-only authoring and deterministic previews of the 1920×1080 source canvas preserved in UI.pdf.</summary>
public static class BattleHudReferenceLayout
{
    const string ArtPath = "Assets/_Project_Assets/UI/BattleHUD/";
    const string ShapePath = ArtPath + "Layout/";
    const string PrefabPath = "Assets/Prefabs/UI/StageHUD.prefab";
    static readonly Color Blue = new Color32(54, 171, 246, 255);
    static readonly Color Lilac = new Color32(204, 190, 199, 255);
    static readonly Color DarkBlue = new Color32(25, 73, 104, 170);
    static Sprite Shape(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ShapePath + name + ".png");
    static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
    static RectTransform R(Transform root, string path) => (RectTransform)root.Find(path);

    static void CenterAbilitySlotContents(Transform template)
    {
        foreach (string name in new[] { "Icon", "Fallback" })
        {
            var rect = R(template, name);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = Vector2.zero;
        }
        R(template, "Icon").GetComponent<Image>().preserveAspect = true;
    }

    public static void RepairAbilitySlotCentering()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var battle = root.transform.Find("ReferenceFrame/BattleAbilityHUD");
            CenterAbilitySlotContents(battle.Find("AbilitySlots/AbilitySlotTemplate"));
            Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(), "focusIconOffset", Vector2.zero);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static RectTransform New(Transform parent, string name)
    {
        var old = parent.Find(name) as RectTransform;
        if (old != null) return old;
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); return rt;
    }
    static void Box(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one; rt.localRotation = Quaternion.identity;
    }
    static void Full(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = Vector2.one * 0.5f;
        rt.offsetMin = rt.offsetMax = Vector2.zero; rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
    static Image Image(RectTransform rt, Sprite sprite, Color color)
    {
        var image = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
        image.enabled = true; image.sprite = sprite; image.type = UnityEngine.UI.Image.Type.Simple;
        image.preserveAspect = false; image.color = color; image.raycastTarget = false;
        return image;
    }
    static void Text(RectTransform rt, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var text = rt.GetComponent<TMP_Text>(); if (text == null) return;
        text.fontSize = size; text.enableAutoSizing = false; text.color = color; text.alignment = align;
        text.margin = Vector4.zero; text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow; text.enableVertexGradient = false;
    }
    static void Set(UnityEngine.Object target, string name, object value)
    {
        var so = new SerializedObject(target); var p = so.FindProperty(name);
        if (p == null) throw new InvalidOperationException(target.name + ": " + name);
        if (value is Color c) p.colorValue = c;
        else if (value is Vector2 v) p.vector2Value = v;
        else if (value is float f) p.floatValue = f;
        else if (value is bool b) p.boolValue = b;
        else p.objectReferenceValue = value as UnityEngine.Object;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
    static RectTransform Frame(Transform root)
    {
        var frame = New(root, "ReferenceFrame");
        if (!frame.GetComponent<BattleHudReferenceFrame>()) frame.gameObject.AddComponent<BattleHudReferenceFrame>();
        frame.sizeDelta = BattleHudReferenceFrame.ReferenceSize;
        return frame;
    }
    static void MakeRing(string name, int diameter, float thickness)
    {
        var texture=new Texture2D(diameter*4,diameter*4,TextureFormat.RGBA32,false);
        var pixels=new Color32[texture.width*texture.height];float outer=diameter*.5f,inner=outer-thickness;
        for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)
        {float d=Vector2.Distance(new Vector2((x+.5f)/4,(y+.5f)/4),Vector2.one*outer);pixels[y*texture.width+x]=new Color32(255,255,255,(byte)((d<=outer&&d>=inner)?255:0));}
        texture.SetPixels32(pixels);texture.Apply();string path=ShapePath+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
    [Serializable] public sealed class PdfShape
    {
        public string name;
        public float x, y, width, height;
        public Vector2[] points;
    }
    [Serializable] sealed class PdfGeometry { public PdfShape[] shapes; }
    static PdfGeometry pdf;
    static PdfShape P(string name)
    {
        if (pdf == null) pdf = JsonUtility.FromJson<PdfGeometry>(File.ReadAllText(
            "Documentation/ProjectArchitecture/UI設計參考圖/UI-PDF-geometry.json"));
        return pdf.shapes.First(s => s.name == name);
    }
    static void PdfBox(RectTransform rect, string shape, Vector2 offset = default)
    {
        var p = P(shape); Box(rect,p.x-offset.x,p.y-offset.y,p.width,p.height);
    }
    static TMP_FontAsset Museo(string name)
    {
        string path=ArtPath+"Fonts/"+name+" SDF.asset";
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(font!=null)return font;
        var source=AssetDatabase.LoadAssetAtPath<Font>(ArtPath+"Fonts/"+name+".ttf");
        font=TMP_FontAsset.CreateFontAsset(source,90,9,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
        font.name=name+" SDF";AssetDatabase.CreateAsset(font,path);AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var texture in font.atlasTextures)AssetDatabase.AddObjectToAsset(texture,font);
        font.TryAddCharacters("0123456789/∞");EditorUtility.SetDirty(font);return font;
    }
    static void CjkWeight(RectTransform rect)
    {
        var text=rect.GetComponent<TMP_Text>();if(text==null)return;
        const string path=ArtPath+"Fonts/NotoSansTC-Bold HUD SDF.asset";
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(font==null)
        {
            var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/Noto_Sans_TC/static/NotoSansTC-Bold.ttf");
            font=TMP_FontAsset.CreateFontAsset(source,90,9,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);
            font.name="NotoSansTC-Bold HUD SDF";AssetDatabase.CreateAsset(font,path);
            AssetDatabase.AddObjectToAsset(font.material,font);
            foreach(var texture in font.atlasTextures)AssetDatabase.AddObjectToAsset(texture,font);
        }
        text.font=font;text.fontSharedMaterial=font.material;text.fontStyle=FontStyles.Normal;
    }
    static void ApplyHudFontsAndBackdrop(Transform frame)
    {
        foreach(var text in frame.GetComponentsInChildren<TMP_Text>(true))CjkWeight(text.rectTransform);
        foreach(var text in frame.GetComponentsInChildren<TMP_Text>(true))
            if(text.name=="CurrentAmmoText"||text.name=="MagazineCapacityText")
            {
                var font=Museo("MuseoModerno-Bold");text.font=font;text.fontSharedMaterial=font.material;
                text.fontStyle=FontStyles.Italic;
            }
        var reward=frame.GetComponentInChildren<LocalPlayerRewardHUD>(true);
        if(reward!=null)
        {
            Set(reward,"dimOpacity",0f);
            var dim=frame.Find("Phase6RewardHud/BackgroundDim");
            if(dim!=null){dim.GetComponent<CanvasGroup>().alpha=0;dim.gameObject.SetActive(false);}
        }
        foreach(var health in frame.GetComponentsInChildren<LocalHealthSegmentView>(true))Set(health,"edgeSoftnessPixels",1f);
    }
    // Targeted migration: keep all existing RectTransforms and gameplay references.
    public static string ApplyReadabilityFixes()
    {
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try{ApplyHudFontsAndBackdrop(root.transform.Find("ReferenceFrame"));PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);}
        finally{PrefabUtility.UnloadPrefabContents(root);}
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity",OpenSceneMode.Additive);
        var canvas=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Canvas>(true)).First(c=>c.name=="PlayerHUDCanvas");
        ApplyHudFontsAndBackdrop(canvas.transform.Find("ReferenceFrame"));
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "HUD fonts unified; reward backdrop disabled; health edge coverage configured";
    }
    public static string RepairWeaponHudReferences()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity",OpenSceneMode.Additive);
        var hud=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LocalPlayerWeaponHUD>(true)).Single();
        var weapon=R(hud.transform,"WeaponHUDRoot");
        Set(hud,"weaponHUDVisualRoot",weapon.gameObject);
        Set(hud,"weaponIconImage",R(weapon,"WeaponIconImage").GetComponent<Image>());
        Set(hud,"currentValueText",R(weapon,"CurrentAmmoText").GetComponent<TMP_Text>());
        Set(hud,"secondaryValueText",R(weapon,"MagazineCapacityText").GetComponent<TMP_Text>());
        R(weapon,"CurrentAmmoText").GetComponent<TMP_Text>().enableWordWrapping=false;
        R(weapon,"MagazineCapacityText").GetComponent<TMP_Text>().enableWordWrapping=false;
        Set(hud,"reloadReminderRoot",R(weapon,"ReloadReminder").gameObject);
        Set(hud,"reloadProgressRoot",R(weapon,"ReloadProgressRoot").gameObject);
        var fill=R(weapon,"ReloadProgressRoot/Fill");Set(hud,"reloadProgressFill",fill.GetComponent<Image>());
        fill.GetComponent<Image>().type=UnityEngine.UI.Image.Type.Simple;
        Full(fill);fill.anchorMax=new Vector2(0,1);
        R(weapon,"ReloadProgressRoot").gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Existing weapon HUD references repaired; no legacy objects recreated";
    }
    static void PdfSprite(string name, bool blue = false)
    {
        var shape=P(name);int width=Mathf.CeilToInt(shape.width*2),height=Mathf.CeilToInt(shape.height*2);
        var pixels=new Color32[width*height];var points=shape.points;
        // Scanline supersampling of the actual PDF cubic curves, flattened at 24 samples per curve.
        var intersections=new List<float>();
        for(int y=0;y<height;y++)for(int sy=0;sy<2;sy++)
        {
            float py=1-(y+(sy+.5f)/2)/height;intersections.Clear();
            for(int i=0,j=points.Length-1;i<points.Length;j=i++)
            {var a=points[i];var b=points[j];if((a.y>py)!=(b.y>py))intersections.Add((a.x+(py-a.y)*(b.x-a.x)/(b.y-a.y))*width);}
            intersections.Sort();
            for(int n=0;n+1<intersections.Count;n+=2)
            {
                int start=Mathf.Max(0,Mathf.FloorToInt(intersections[n])),end=Mathf.Min(width-1,Mathf.CeilToInt(intersections[n+1]));
                for(int x=start;x<=end;x++)for(int sx=0;sx<2;sx++)if(x+(sx+.5f)/2>=intersections[n]&&x+(sx+.5f)/2<intersections[n+1])
                {int index=y*width+x;byte alpha=(byte)Mathf.Min(255,pixels[index].a+64);pixels[index]=blue?new Color32(54,171,246,alpha):new Color32(255,255,255,alpha);}
            }
        }
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.SetPixels32(pixels);texture.Apply();
        string path=ShapePath+name+".png";File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spritePixelsPerUnit=100;
        importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
    }
    public static string GenerateShapes()
    {
        pdf=null;Directory.CreateDirectory(ShapePath);
        foreach(string name in new[]{"Minimap","Panel","Key","RewardDescription","Momentum","Experience","SkillSlot","RewardHex","BracketLeft","BracketMiddle","BracketRight","HealthCross"})PdfSprite(name);
        PdfSprite("SkillStar",true);
        MakeRing("GrappleExact",440,55.493f); // normalized to the PDF 109.6234 / 81.9715 outer/inner diameters
        MakeRing("LevelRing",126,15.891f);
        AssetDatabase.SaveAssets();return "PDF source curves and HUD font imported";
    }
    public static string ImportTightIcons()
    {
        string[] images={"GrappleMark","AerialSlow","AirDash","GrapplePull","GrappleGather"};
        string[] definitions={"AttackGrappleMark","SupportAerial","TankAirDash","SupportGrapplePull","TankGrappleGather"};
        for(int i=0;i<images.Length;i++)
        {
            Sprite sprite;
            if(i==0) sprite=Shape("SkillStar");
            else
            {
                string path=ShapePath+images[i]+"Tight.png";File.Copy(ArtPath+images[i]+".png",path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                var texture=new Texture2D(2,2);texture.LoadImage(File.ReadAllBytes(path));var pixels=texture.GetPixels32();
                int minX=texture.width,minY=texture.height,maxX=0,maxY=0;
                for(int y=0;y<texture.height;y++)for(int x=0;x<texture.width;x++)if(pixels[y*texture.width+x].a>24){minX=Mathf.Min(minX,x);minY=Mathf.Min(minY,y);maxX=Mathf.Max(maxX,x);maxY=Mathf.Max(maxY,y);}
                UnityEngine.Object.DestroyImmediate(texture);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Multiple;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.textureCompression=TextureImporterCompression.Uncompressed;
                importer.spritesheet=new[]{new SpriteMetaData{name=images[i],rect=new Rect(minX,minY,maxX-minX+1,maxY-minY+1),pivot=Vector2.one*.5f,alignment=0}};
                importer.SaveAndReimport();sprite=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().First();
            }
            var definition=AssetDatabase.LoadAssetAtPath<PlayerAbilityDefinition>("Assets/_Project_Assets/Data/PlayerAbility/Definitions/"+definitions[i]+".asset");Set(definition,"hudIcon",sprite);
        }
        AssetDatabase.SaveAssets();return "five ability icon import bounds updated";
    }
    static void Gauge(RectTransform root, Sprite sprite)
    {
        var background=R(root,"Background"); Full(background);Image(background,sprite,DarkBlue);
        var area=R(root,"Fill Area");Full(area);var fill=R(area,"Fill");Full(fill);
        var image=Image(fill,sprite,Blue);image.type=UnityEngine.UI.Image.Type.Filled;image.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;
        var slider=root.GetComponent<Slider>();slider.fillRect=fill;slider.interactable=false;slider.handleRect=null;slider.value=1;
    }
    // Position the visible alpha bounds, including the transparent padding in the supplied PNGs.
    static void Artwork(RectTransform rt,Sprite sprite,float x,float y,float w,float h,bool flip=false)
    {
        var texture=new Texture2D(2,2);texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(sprite)));
        int minX=texture.width,minY=texture.height,maxX=0,maxY=0;var pixels=texture.GetPixels32();
        for(int iy=0;iy<texture.height;iy++)for(int ix=0;ix<texture.width;ix++)if(pixels[iy*texture.width+ix].a>24){minX=Mathf.Min(minX,ix);minY=Mathf.Min(minY,iy);maxX=Mathf.Max(maxX,ix);maxY=Mathf.Max(maxY,iy);}
        float sx=w/(maxX-minX+1f),sy=h/(maxY-minY+1f);
        float offsetX=(flip?texture.width-1-maxX:minX)*sx;
        Box(rt,x-offsetX,y-(texture.height-1-maxY)*sy,texture.width*sx,texture.height*sy);
        var image=Image(rt,sprite,Color.white);
        if(flip){rt.pivot=new Vector2(1,1);rt.localScale=new Vector3(-1,1,1);}
        UnityEngine.Object.DestroyImmediate(texture);
    }
    public static string ApplyStage()
    {
        var root=PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var frame=Frame(root.transform);
            foreach(Transform child in root.transform.Cast<Transform>().ToArray())if(child!=frame)child.SetParent(frame,false);
            var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            var origin=new Vector2(P("Minimap").x,P("Minimap").y);
            var column=R(frame,"TopLeftColumn");Box(column,origin.x,origin.y,P("Panel").width,575);
            var map=R(column,"MinimapSlot");PdfBox(map,"Minimap",origin);
            Image(map,Shape("Minimap"),Color.white);var mask=map.GetComponent<Mask>()??map.gameObject.AddComponent<Mask>();mask.showMaskGraphic=false;
            Full(R(map,"Placeholder"));Image(R(map,"Placeholder"),Shape("Minimap"),Blue);
            R(map,"Placeholder/Label").gameObject.SetActive(false);Full(R(map,"MinimapContent"));
            var panel=R(column,"StagePanel");PdfBox(panel,"Panel",origin);Image(panel,Shape("Panel"),Blue);
            R(panel,"Accent").gameObject.SetActive(false);
            Box(R(panel,"StageLevel"),16,8,490,38);Text(R(panel,"StageLevel"),24.7f,Color.black,TextAlignmentOptions.MidlineLeft);
            Box(R(panel,"Objective"),16,49,490,41);Text(R(panel,"Objective"),23,Color.black,TextAlignmentOptions.MidlineLeft);
            Box(R(panel,"Timer"),72.6f,108.3f,172,65);Text(R(panel,"Timer"),45.333f,Color.black,TextAlignmentOptions.MidlineLeft);
            var font=Museo("MuseoModerno-Bold");
            var timer=R(panel,"Timer").GetComponent<TMP_Text>();timer.font=font;timer.fontStyle=FontStyles.Normal;
            Set(root.GetComponent<Purgers.GameFlow.Stage.StageHudController>(),"timerNormalColor",Color.black);
            Artwork(R(panel,"ExtractionIcon"),Art("ExtractionPrototype"),9.83f,111.86f,56.2f,67.2f);
            Box(R(panel,"ExtractionStatus"),0,193,522,39);Text(R(panel,"ExtractionStatus"),23,Color.white);
            Box(R(panel,"ExtractionProgress"),0,239,522,12);Box(R(column,"ReadyCheckSlot"),0,543,522,214);
            var battle=R(frame,"BattleAbilityHUD");Full(battle);
            foreach(string name in new[]{"GrappleBackground","GrappleFill"}){PdfBox(R(battle,name),"GrappleOuter");R(battle,name).GetComponent<Image>().sprite=Shape("GrappleExact");}
            R(battle,"GrappleBackground").GetComponent<Image>().color=DarkBlue;R(battle,"GrappleFill").GetComponent<Image>().color=Blue;
            Box(R(battle,"EmptyGrappleMark"),925,936,80,13.826f);R(battle,"EmptyGrappleMark").localEulerAngles=new Vector3(0,0,45);
            Box(R(battle,"GrappleKey"),943,992.67f,44,49);Image(R(battle,"GrappleKey"),Shape("Key"),Color.clear);
            Full(R(battle,"GrappleKey/Label"));Text(R(battle,"GrappleKey/Label"),35.575f,Color.black);CjkWeight(R(battle,"GrappleKey/Label"));
            Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"grappleKeyBackground",null);
            Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"focusIconOffset",Vector2.zero);
            Set(battle.GetComponent<LocalPlayerBattleAbilityHUD>(),"focusKeyOffset",new Vector2(-4.5877f,0));
            var slots=R(battle,"AbilitySlots");var slotShape=P("SkillSlot");const float step=121.3927f;
            Box(slots,slotShape.x,slotShape.y,slotShape.width+step,slotShape.height);
            var layout=slots.GetComponent<HorizontalLayoutGroup>();
            if(layout!=null){layout.spacing=step-slotShape.width;layout.childControlWidth=false;layout.childControlHeight=false;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;layout.childAlignment=TextAnchor.UpperRight;layout.padding=new RectOffset();}
            var template=R(slots,"AbilitySlotTemplate");Box(template,0,0,slotShape.width,slotShape.height);Image(template,Shape("SkillSlot"),Lilac);
            var le=template.GetComponent<LayoutElement>();if(le!=null){le.preferredWidth=slotShape.width;le.preferredHeight=slotShape.height;le.minWidth=slotShape.width;le.minHeight=slotShape.height;}
            var slotOrigin=new Vector2(slotShape.x,slotShape.y);
            PdfBox(R(template,"Icon"),"SkillStar",slotOrigin);PdfBox(R(template,"Fallback"),"SkillStar",slotOrigin);Text(R(template,"Fallback"),66,Blue);
            CenterAbilitySlotContents(template);
            Full(R(template,"CooldownShade"));Image(R(template,"CooldownShade"),Shape("SkillSlot"),new Color(0,0,0,.72f));
            Box(R(template,"CooldownText"),28,20,82,66);Text(R(template,"CooldownText"),39,Color.white);
            PdfBox(R(template,"Key"),"SkillKeyQ",slotOrigin);Image(R(template,"Key"),Shape("Key"),Lilac);Full(R(template,"Key/Label"));Text(R(template,"Key/Label"),20.87f,Color.black);CjkWeight(R(template,"Key/Label"));
            var reward=R(frame,"Phase6RewardHud");Full(reward);var xp=R(reward,"ExperienceGroup");Full(xp);
            Image(xp,null,Color.clear);Box(R(xp,"LevelLabel"),82.55f,1019.91f,31.3356f,31.3356f);Text(R(xp,"LevelLabel"),23.7f,Color.black);R(xp,"LevelLabel").GetComponent<TMP_Text>().text="1";R(xp,"LevelLabel").GetComponent<TMP_Text>().font=Museo("MuseoModerno-Bold");
            var circle=New(xp,"LevelCircle");Box(circle,82.55f,1019.915f,31.3356f,31.3356f);Image(circle,Shape("LevelRing"),Blue);circle.SetAsFirstSibling();
            R(xp,"ExperienceLabel").gameObject.SetActive(false);
            var gauge=R(xp,"ExperienceGauge");PdfBox(gauge,"Experience");Gauge(gauge,Shape("Experience"));
            Box(R(xp,"AltPrompt"),107,1054,376,24);Full(R(xp,"AltPrompt/PromptLabel"));Text(R(xp,"AltPrompt/PromptLabel"),19.7f,Color.black,TextAlignmentOptions.MidlineLeft);
            Set(reward.GetComponent<LocalPlayerRewardHUD>(),"normalExperienceColor",Blue);
            Image(R(reward,"BackgroundDim"),null,Color.white);Set(reward.GetComponent<LocalPlayerRewardHUD>(),"dimOpacity",0f);
            var window=R(reward,"ChoiceWindow");Full(window);R(window,"WindowTitle").gameObject.SetActive(false);
            string[] names={"LeftCard","MiddleCard","RightCard"};
            string[] boxes={"RewardDescription","RewardDescriptionMiddle","RewardDescriptionRight"};
            string[] hexes={"RewardHex","RewardHexMiddle","RewardHexRight"};
            string[] brackets={"BracketLeft","BracketMiddle","BracketRight"};string[] mice={"MouseLeft","MouseMiddle","MouseRight"};
            for(int i=0;i<3;i++)
            {
                var card=R(window,names[i]);Full(card);Image(card,null,Color.clear);var box=P(boxes[i]);
                var description=New(card,"DescriptionPanel");PdfBox(description,boxes[i]);Image(description,Shape("RewardDescription"),new Color32(204,191,203,255));description.SetAsFirstSibling();
                Box(R(card,"Title"),box.x+16,box.y+10,box.width-32,31);Text(R(card,"Title"),21.4f,Color.black,TextAlignmentOptions.TopLeft);CjkWeight(R(card,"Title"));
                Box(R(card,"Description"),box.x+16,box.y+43,box.width-32,62);Text(R(card,"Description"),18.1f,Color.black,TextAlignmentOptions.TopLeft);
                var hex=New(card,"IconPlate");PdfBox(hex,hexes[i]);Image(hex,Shape("RewardHex"),new Color32(204,191,203,255));hex.SetSiblingIndex(1);
                var bracket=New(card,"IconBracket");PdfBox(bracket,brackets[i]);Image(bracket,Shape(brackets[i]),new Color32(204,191,203,255));
                var plate=P(hexes[i]);float cx=plate.x+plate.width*.5f,cy=plate.y+plate.height*.5f;
                Box(R(card,"Icon"),cx-26.25f,cy-26.25f,52.5f,52.5f);Box(R(card,"FallbackIcon"),cx-26.25f,cy-26.25f,52.5f,52.5f);Text(R(card,"FallbackIcon"),49,Blue);
                PdfBox(R(card,"MouseGraphic"),mice[i]);R(card,"MouseButton").gameObject.SetActive(false);
            }
            ApplyHudFontsAndBackdrop(frame);
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        return "stage layout saved";
    }
    public static string ApplyPlayer()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/_Menu.unity");
        if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene("Assets/Scenes/_Menu.unity",OpenSceneMode.Additive);
        var all=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RectTransform>(true)).ToArray();
        var canvas=all.First(t=>t.name=="PlayerHUDCanvas");var frame=Frame(canvas);
        var controller=all.First(t=>t.name=="PlayerHUDController");controller.SetParent(frame,false);Full(controller);
        var health=R(controller,"Health_UI");Full(health);var bar=R(health,"HealthBar");var cell=P("HealthCell");Box(bar,cell.x,cell.y,401.9414f,cell.height);
        var slider=bar.GetComponent<Slider>();slider.minValue=0;slider.maxValue=100;slider.value=100;
        foreach(var im in bar.GetComponentsInChildren<Image>(true))im.enabled=false;
        var segments=bar.GetComponentInChildren<LocalHealthSegmentView>(true);Box(segments.rectTransform,0,0,401.9414f,cell.height);segments.rectTransform.pivot=Vector2.zero;segments.rectTransform.anchoredPosition=new Vector2(0,-cell.height);
        Set(segments,"segmentWidth",cell.width-22f);Set(segments,"segmentGap",76.8f-(cell.width-22f));Set(segments,"segmentSlant",22f);Set(segments,"segmentRise",6.2438f);Set(segments,"healthyColor",Blue);
        var segmentObject=new SerializedObject(segments);var outline=segmentObject.FindProperty("segmentOutline");outline.arraySize=cell.points.Length;
        for(int i=0;i<cell.points.Length;i++)outline.GetArrayElementAtIndex(i).vector2Value=new Vector2(cell.points[i].x,1-cell.points[i].y);
        segmentObject.ApplyModifiedPropertiesWithoutUndo();
        R(health,"Text (TMP)[HealthBar]").gameObject.SetActive(false);var plus=New(health,"HealthCross");PdfBox(plus,"HealthCross");Image(plus,Shape("HealthCross"),Color.black);
        var speed=all.First(t=>t.name=="Speed_UI");speed.SetParent(frame,false);Full(speed);speed.gameObject.SetActive(true);
        var speedBar=R(speed,"SpeedBar");PdfBox(speedBar,"Momentum");Gauge(speedBar,Shape("Momentum"));
        R(speed,"Text (TMP)[SpeedBar]").gameObject.SetActive(false);Artwork(R(speed,"MomentumArrow"),Art("MomentumArrow"),59.9f,891.8f,93.9f,38f,true);
        Box(R(speed,"ExtractionCountdownPanel"),45.47f,514,522,53);Full(R(speed,"ExtractionCountdownPanel/Label"));Text(R(speed,"ExtractionCountdownPanel/Label"),24.7f,Color.white);
        var speedController=controller.GetComponent<LocalPlayerSpeedSlider>();var momentum=P("Momentum");Set(speedController,"normalGaugeSize",new Vector2(momentum.width,momentum.height));Set(speedController,"maximumBoostGaugeSize",new Vector2(momentum.width,momentum.height));Set(speedController,"normalGaugeColor",Blue);
        var weapon=R(controller,"WeaponHUDRoot");Full(weapon);if(R(weapon,"WeaponIconBackdrop")!=null)R(weapon,"WeaponIconBackdrop").gameObject.SetActive(false);
        Artwork(R(weapon,"WeaponIconImage"),Art("WeaponPrototype"),1625.3f,929.4f,195.1f,74.5f,true);R(weapon,"WeaponIconImage").gameObject.SetActive(true);
        Box(R(weapon,"CurrentAmmoText"),1738.6f,892.97f,57,49.47f);Text(R(weapon,"CurrentAmmoText"),45.333f,Color.black,TextAlignmentOptions.BottomRight);R(weapon,"CurrentAmmoText").GetComponent<TMP_Text>().fontStyle=FontStyles.Normal;R(weapon,"CurrentAmmoText").GetComponent<TMP_Text>().font=Museo("MuseoModerno-Bold");
        Box(R(weapon,"MagazineCapacityText"),1794.42f,907.57f,60,33.33f);Text(R(weapon,"MagazineCapacityText"),33.333f,Color.black,TextAlignmentOptions.BottomLeft);R(weapon,"MagazineCapacityText").GetComponent<TMP_Text>().fontStyle=FontStyles.Normal;R(weapon,"MagazineCapacityText").GetComponent<TMP_Text>().font=Museo("MuseoModerno-Bold");
        var reload=R(weapon,"ReloadReminder");Box(reload,854.6525f,611.9003f,231,43.9647f);
        Box(R(reload,"Text (TMP)"),53.71f,-3.37f,178,49);Text(R(reload,"Text (TMP)"),41.746f,Color.black,TextAlignmentOptions.MidlineLeft);CjkWeight(R(reload,"Text (TMP)"));
        Box(R(reload,"Keyboard_Image"),0,0,43.9647f,43.9647f);Image(R(reload,"Keyboard_Image"),Shape("Key"),Lilac);
        Box(R(reload,"R"),0,-7.37f,43.9647f,49);Text(R(reload,"R"),41.746f,Color.black);CjkWeight(R(reload,"R"));
        Box(R(weapon,"ReloadProgressRoot"),910.1883f,573.0081f,109.6234f,10);Full(R(weapon,"ReloadProgressRoot/Fill"));R(weapon,"ReloadProgressRoot/Fill").GetComponent<Image>().type=UnityEngine.UI.Image.Type.Simple;R(weapon,"ReloadProgressRoot/Fill").anchorMax=new Vector2(0,1);R(weapon,"ReloadProgressRoot").gameObject.SetActive(false);
        var crosshair=all.First(t=>t.name=="Crosshair");crosshair.SetParent(frame,false);Box(crosshair,964.9844f,545,0,0);
        var center=R(crosshair,"Center");Box(center,-4.9844f,-5,9.9688f,10);if(center.GetComponent<Image>())center.GetComponent<Image>().color=Blue;
        var chat=all.First(t=>t.name=="ChatController");chat.SetParent(frame,false);Box(chat,1152,274,724.66f,230);
        var visual=R(chat,"ChatVisualRoot");Full(visual);R(visual,"Background").gameObject.SetActive(false);
        var scroll=R(visual,"MessageScrollView");Full(scroll);var viewport=R(scroll,"Viewport");Full(viewport);var content=R(viewport,"Content");Full(content);
        var messages=R(content,"MessagesText");Full(messages);Text(messages,41.74f,new Color32(216,222,28,255),TextAlignmentOptions.TopRight);
        messages.GetComponent<TMP_Text>().fontStyle=FontStyles.Bold;
        CjkWeight(messages);
        ApplyHudFontsAndBackdrop(frame);
        frame.GetComponent<BattleHudReferenceFrame>().Refresh();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return "player layout saved";
    }

    public static string RenderPreview(bool rewards, int width = 1920, int height = 1080, float healthValue = 100f, bool healthDetail = false, float reloadProgress = -1f)
    {
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
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.white;
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
            foreach(var text in canvasObject.GetComponentsInChildren<TMP_Text>(true))text.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active=target;output=new Texture2D(width,height,TextureFormat.RGB24,false);output.ReadPixels(new Rect(0,0,width,height),0,0);output.Apply();
            var directory="Documentation/ProjectArchitecture/Validation/BattleHUD";Directory.CreateDirectory(directory);
            string path=directory+"/"+(healthDetail?"health-detail":rewards?"reward":"normal")+"-"+width+"x"+height+(reloadProgress>=0?"-reload"+Mathf.RoundToInt(reloadProgress*100):"")+(healthValue<100?"-hp"+healthValue.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture):"")+".png";File.WriteAllBytes(path,output.EncodeToPNG());return Path.GetFullPath(path);
        }
        finally
        {
            RenderTexture.active=previous;if(previewCamera!=null)previewCamera.targetTexture=null;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(output!=null)UnityEngine.Object.DestroyImmediate(output);
            EditorSceneManager.CloseScene(scene, true);
            if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
        }
    }
}
