using System;
using System.IO;
using Fusion.Menu;
using MultiClimb.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit V2 authoring. Keeps existing screens, controls, events and prefab links.</summary>
public static class MenuWindowStyleBuilder
{
    const string Art = "Assets/_Project_Assets/UI/MenuBrushV1";
    public const string RowPath = "Assets/Prefabs/UI/Menu/SaveSlotRowBrushV2.prefab";
    static TMP_FontAsset font;
    static Sprite brush, panel;
    static readonly Color Gold = new Color32(207,191,80,255), Cream = new Color32(241,237,217,255),
        Muted = new Color32(174,184,151,255), Ink = new Color32(27,34,20,255),
        Surface = new Color32(29,38,25,250), Field = new Color32(16,25,15,235);

    [MenuItem("Tools/Purgers/Menu/Apply approved window style V2")]
    public static void Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/_Menu.unity")
            throw new InvalidOperationException("請在非 Play Mode 的 _Menu 套用。");
        if(scene.isDirty) throw new InvalidOperationException("請先保存 Scene；此工具不會代存。");
        var menu = GameObject.Find("GameplayHUD Canvas/Menu").transform;
        if(menu.Find("FusionMenuViewSettings/WindowPanelV2"))
            throw new InvalidOperationException("已套用 V2，請直接調整 Inspector，避免覆寫人工配置。");
        LoadArt();
        Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply approved Menu windows V2");
        foreach(var c in menu.GetComponentsInChildren<Component>(true)) if(c) Undo.RecordObject(c,"Menu V2 style");
        Settings(menu.Find("FusionMenuViewSettings"));
        Party(menu.Find("FusionMenuViewPartyMenu"));
        Continue(menu);
        Name(menu.Find("FusionMenuViewMainMenu/NameInputView"));
        Loading(menu.Find("FusionMenuViewLoading"));
        Popup(menu.Find("FusionMenuViewPopUp"));
        foreach(var c in menu.GetComponentsInChildren<Component>(true)) if(c)
        {
            EditorUtility.SetDirty(c);
            if(PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undo);
    }
    static void LoadArt()
    {
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/Noto_Sans_TC/NotoSansTC-Bold SDF.asset");
        brush = AssetDatabase.LoadAssetAtPath<Sprite>(Art+"/Brush.png");
        if(!font || !brush) throw new InvalidOperationException("缺少字型或共用筆刷。");
        string path=Art+"/WindowPanel.png";
        if(!File.Exists(path))
        {
            var tex=new Texture2D(128,128,TextureFormat.RGBA32,false);
            var pixels=new Color[128*128];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {
                int corner=Mathf.Min(x+y,x+127-y,127-x+y,254-x-y);
                float shade=.94f+.06f*x/127f;
                pixels[y*128+x]=new Color(shade,shade,shade,Mathf.Clamp01(corner-13));
            }
            tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);AssetDatabase.ImportAsset(path);
            var imp=(TextureImporter)AssetImporter.GetAtPath(path);imp.textureType=TextureImporterType.Sprite;
            imp.spriteImportMode=SpriteImportMode.Single;imp.spriteBorder=new Vector4(20,20,20,20);
            imp.alphaIsTransparency=true;imp.mipmapEnabled=false;imp.textureCompression=TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }
        panel=AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    static void Screen(Transform root, float w=1280, float h=780)
    {
        var anim=root.GetComponent<Animator>();
        if(anim){anim.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Art+"/MenuBrushFadeV1.controller");anim.updateMode=AnimatorUpdateMode.UnscaledTime;}
        foreach(var cg in root.GetComponentsInChildren<CanvasGroup>(true))cg.alpha=1;
        var bg=root.Find("Background");
        if(bg)
        {
            var im=bg.Find("Panel").GetComponent<Image>();
            im.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project_Assets/Image/Menu/MenuV2.jpg");im.color=Color.white;
            im.raycastTarget=false;
            foreach(var tx in bg.GetComponentsInChildren<TMP_Text>(true))tx.enabled=false;
            foreach(var name in new[]{"PhotonLogo","ProductLogo"}){var t=bg.Find(name);if(t)t.gameObject.SetActive(false);}
        }
        var dim=Image(root,"WindowDimV2",new Color(0.025f,.045f,.015f,.72f));Stretch(dim.rectTransform);dim.raycastTarget=true;dim.transform.SetSiblingIndex(bg?bg.GetSiblingIndex()+1:0);
        var p=Image(root,"WindowPanelV2",Surface,panel);At(p.transform,0,0,w,h);p.type=UnityEngine.UI.Image.Type.Sliced;p.raycastTarget=true;p.transform.SetSiblingIndex(dim.transform.GetSiblingIndex()+1);
        var foot=Image(root,"WindowFooterV2",new Color(0.035f,.065f,.025f,.38f));At(foot.transform,0,-h/2+52,w-30,84);foot.transform.SetSiblingIndex(p.transform.GetSiblingIndex()+1);
    }
    static void Header(Transform root, string title,string kicker,string description)
    {
        var h=root.Find("MenuHeader");Stretch((RectTransform)h);
        var oldbg=h.Find("Background");if(oldbg)oldbg.gameObject.SetActive(false);
        var titleText=h.Find("HeaderLabel").GetComponent<TMP_Text>();Text(titleText,title,48,Cream);At(titleText.transform,-20,272,1120,72);
        Stripe(h,-474,251,235,30);
        var stripe=h.Find("TitleBrushV2");stripe.SetAsFirstSibling();
        Label(root,"KickerV2",kicker,-20,338,1120,26,17,Muted);
        Label(root,"DescriptionV2",description,-20,200,1120,50,22,Muted);
        var version=h.Find("VersionLabel");if(version)version.GetComponent<TMP_Text>().enabled=false;
        var b=h.Find("IconButton").GetComponent<Button>();At(b.transform,470,-315,210,72);Brush(b,"返回",false);
    }
    static void Settings(Transform root)
    {
        Screen(root);Header(root,"設定","FIELD CONFIGURATION / 01","讓畫面與連線，符合你的行動方式。");
        var group=root.Find("SettingsMenu");DisableLayout(group);At(group,0,-35,1120,390);
        Label(root,"GraphicsHeadingV2","01    畫面",-300,135,520,40,26,Gold);
        Label(root,"NetworkHeadingV2","02    連線",300,135,520,40,26,Gold);
        string[] names={"Resolution","GraphicsQuality","Framerate","FullscreenSettings","VSyncSettings","PhotonRegion","MaxPlayerSettings","AppVersion"};
        string[] labels={"解析度","畫面品質","幀率上限","全螢幕","垂直同步","連線區域","最大玩家數","遊戲版本"};
        for(int i=0;i<names.Length;i++)
        {
            var t=group.Find(names[i]);int row=i<5?i:i-5;At(t,i<5?-300:300,120-row*75,520,60);
            var tx=t.GetComponentInChildren<TMP_Text>(true);Text(tx,labels[i],24,Cream);At(tx.transform,-158,0,204,48);
            var dd=t.GetComponentInChildren<TMP_Dropdown>(true);
            if(dd){At(dd.transform,123,0,274,52);Dropdown(dd);}
            var input=t.GetComponentInChildren<TMP_InputField>(true);
            if(input){At(input.transform,185,0,150,52);Input(input);}
            if(i==3||i==4)ToggleField(t.Find("ToggleButton"));
            var tip=t.Find("Tooltip");if(tip){At(tip,-35,0,24,24);tip.GetComponent<Image>().color=Color.clear;foreach(var im in tip.GetComponentsInChildren<Image>(true))if(im.transform!=tip)im.color=Muted;}
            var line=Image(t,"RuleV2",new Color(.55f,.58f,.37f,.18f));At(line.transform,0,-34,520,1);
        }
        var footer=root.Find("Footer");if(footer){Stretch((RectTransform)footer);var tx=footer.GetComponentInChildren<TMP_Text>(true);Text(tx,null,16,Muted);At(tx.transform,-255,-315,600,55);}
    }
    static void Dropdown(TMP_Dropdown dd)
    {
        foreach(var tx in dd.GetComponentsInChildren<TMP_Text>(true))Text(tx,null,22,Cream);
        foreach(var im in dd.GetComponentsInChildren<Image>(true))
        {
            if(im.name=="Icon" || im.name.Contains("Checkmark")){im.color=Gold;continue;}
            if(im.GetComponent<Mask>())continue;
            im.sprite=null;im.type=UnityEngine.UI.Image.Type.Simple;
            im.color=im.name=="Item Background"?new Color(.45f,.46f,.23f,.7f):Field;
        }
        dd.targetGraphic=dd.GetComponent<Image>();dd.transition=Selectable.Transition.ColorTint;dd.colors=ControlColors();
        if(dd.captionText){Stretch(dd.captionText.rectTransform,15,6,-42,-6);dd.captionText.alignment=TextAlignmentOptions.MidlineLeft;dd.captionText.overflowMode=TextOverflowModes.Ellipsis;}
        var icon=dd.transform.Find("Icon");if(icon)At(icon,112,0,18,18);
        var template=dd.template;
        template.anchorMin=new Vector2(0,0);template.anchorMax=new Vector2(1,0);template.pivot=new Vector2(.5f,1);template.anchoredPosition=new Vector2(0,-5);template.sizeDelta=new Vector2(0,230);
        foreach(var toggle in dd.GetComponentsInChildren<Toggle>(true)) toggle.colors=ControlColors();
        var item=dd.itemText.transform.parent as RectTransform;item.sizeDelta=new Vector2(item.sizeDelta.x,46);
        Stretch(dd.itemText.rectTransform,20,4,-10,-4);
    }
    static void Input(TMP_InputField input)
    {
        foreach(var im in input.GetComponentsInChildren<Image>(true)){im.sprite=null;im.type=UnityEngine.UI.Image.Type.Simple;im.color=Field;}
        var icon=input.transform.Find("Icon");if(icon)icon.gameObject.SetActive(false);
        foreach(var tx in input.GetComponentsInChildren<TMP_Text>(true)){Text(tx,null,26,Cream);tx.enableWordWrapping=false;}
        if(input.placeholder)input.placeholder.color=Muted;
        var bg=input.transform.Find("Background");if(bg)Stretch((RectTransform)bg);
        Stretch(input.textViewport,18,8,-18,-8);
        input.caretColor=Gold;input.selectionColor=new Color(.8f,.75f,.3f,.3f);
        input.targetGraphic=input.GetComponent<Image>();input.transition=Selectable.Transition.ColorTint;input.colors=ControlColors();
    }
    static void ToggleField(Transform t)
    {
        At(t,170,0,180,46);var bg=t.Find("Background").GetComponent<Image>();bg.sprite=null;bg.color=Field;Stretch(bg.rectTransform);
        var group=t.Find("ToggleGroup");Stretch((RectTransform)group);
        foreach(var toggle in group.GetComponentsInChildren<Toggle>(true))
        {
            bool on=toggle.name=="ActiveBackground";At(toggle.transform,on?43:-43,0,84,38);
            var image=toggle.GetComponent<Image>();image.sprite=null;image.type=UnityEngine.UI.Image.Type.Simple;image.color=on?Gold:new Color(.33f,.38f,.27f);
            toggle.colors=ControlColors();
        }
        Text(t.Find("OffLabel").GetComponent<TMP_Text>(),"關",21,Cream);At(t.Find("OffLabel"),-43,0,80,38);
        Text(t.Find("OnLabel").GetComponent<TMP_Text>(),"開",21,Ink);At(t.Find("OnLabel"),43,0,80,38);
        t.Find("OnLabel").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.Center;
        t.Find("OffLabel").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.Center;
    }
    static void Party(Transform root)
    {
        Screen(root);Header(root,"多人選單","SQUAD OPERATIONS / 03","一起出發。建立新行動，或加入夥伴的隊伍。");
        var cards=root.Find("MenuCards");DisableLayout(cards);At(cards,0,-35,1120,400);
        for(int i=0;i<2;i++)
        {
            var card=cards.Find(i==0?"CreateCard":"JoinCard");At(card,i==0?-300:300,0,520,400);
            var bg=card.Find("Background").GetComponent<Image>();bg.sprite=panel;bg.type=UnityEngine.UI.Image.Type.Sliced;bg.color=new Color(.07f,.11f,.05f,.55f);Stretch(bg.rectTransform);
            Text(card.Find("CardHeader").GetComponent<TMP_Text>(),i==0?"發起一場行動":"跟上你的夥伴",34,Cream);At(card.Find("CardHeader"),0,96,440,60);
            Text(card.Find("CardText").GetComponent<TMP_Text>(),i==0?"建立新遊戲，帶領這次行動。\n進入遊戲後取得代碼，邀請夥伴加入。":"輸入夥伴提供的遊戲代碼，\n接上同一場行動。",22,Muted);At(card.Find("CardText"),0,40,440,80);
            Label(card,"RouteV2",i==0?"01   HOST / 建立":"02   JOIN / 加入",0,157,440,30,17,Gold);
            var input=card.Find("CodeInputButton").GetComponent<TMP_InputField>();
            if(i==0){input.gameObject.SetActive(false);Label(card,"CreateNoteV2","新的隊伍，新的進度。",0,-65,440,56,22,Muted);}
            else{At(input.transform,0,-72,440,56);Input(input);Label(card,"CodeLabelV2","遊戲代碼",0,-20,440,22,17,Muted);}
            var b=card.Find(i==0?"CreateButton":"JoinButton").GetComponent<Button>();At(b.transform,0,-150,460,72);Brush(b,i==0?"建立遊戲":"加入遊戲",true);
        }
    }
    static void Continue(Transform menu)
    {
        var root=menu.Find("ContinueOverlay");var pane=root.Find("SaveSelectionPanel");
        var blocker=root.Find("ScreenBlocker").GetComponent<Image>();blocker.color=new Color(.025f,.04f,.015f,.82f);Stretch(blocker.rectTransform);
        At(pane,0,0,1280,780);var p=pane.GetComponent<Image>();p.sprite=panel;p.type=UnityEngine.UI.Image.Type.Sliced;p.color=Surface;
        pane.Find("TopAccent").gameObject.SetActive(false);
        Text(pane.Find("Title").GetComponent<TMP_Text>(),"繼續遊戲",48,Cream);At(pane.Find("Title"),0,270,1120,74);
        Text(pane.Find("Subtitle").GetComponent<TMP_Text>(),"選擇一份存檔，接續尚未完成的行動。",22,Muted);At(pane.Find("Subtitle"),0,202,1120,50);
        Label(pane,"KickerV2","OPERATION ARCHIVE / 02",0,337,1120,28,17,Muted);Stripe(pane,-416,250,310,30);pane.Find("Title").SetAsLastSibling();
        var scroll=pane.Find("SaveScrollView").GetComponent<ScrollRect>();At(scroll.transform,-205,-23,710,390);scroll.GetComponent<Image>().color=Color.clear;
        var vp=scroll.viewport;Stretch(vp);vp.GetComponent<Image>().color=Color.clear;
        var content=scroll.content;content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=Vector2.zero;
        var layout=content.GetComponent<VerticalLayoutGroup>();layout.spacing=10;layout.padding=new RectOffset(0,0,0,0);layout.childControlWidth=true;layout.childForceExpandWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;
        Text(vp.Find("EmptyState").GetComponent<TMP_Text>(),null,25,Muted);Stretch((RectTransform)vp.Find("EmptyState"),20,30,-20,-30);
        var feedback=pane.Find("Feedback").GetComponent<TMP_Text>();Text(feedback,null,20,new Color32(232,174,137,255));At(feedback.transform,0,-250,1120,68);feedback.enableWordWrapping=true;feedback.overflowMode=TextOverflowModes.Truncate;
        var back=pane.Find("Back").GetComponent<Button>();At(back.transform,237,-320,205,72);Brush(back,"返回",false);
        var start=pane.Find("StartSelectedSave").GetComponent<Button>();At(start.transform,475,-320,230,72);Brush(start,"開始遊戲",true);
        var summary=Image(pane,"SelectionSummaryV2",new Color(.055f,.09f,.04f,.6f));At(summary.transform,390,-23,340,390);
        var view=Undo.AddComponent<MenuSaveSummaryView>(summary.gameObject);
        var empty=Label(summary.transform,"Empty","／\n選擇一份存檔\n查看行動進度",0,0,290,230,26,Muted);empty.alignment=TextAlignmentOptions.Center;
        var detail=new GameObject("Details",typeof(RectTransform));Undo.RegisterCreatedObjectUndo(detail,"Menu summary");detail.transform.SetParent(summary.transform,false);Stretch((RectTransform)detail.transform);
        Label(detail.transform,"Caption","SELECTED OPERATION",0,153,285,30,15,Muted);
        var stage=Label(detail.transform,"Stage","",0,78,285,125,80,Gold);
        var name=Label(detail.transform,"Name","",0,-15,285,52,27,Cream);name.enableWordWrapping=false;name.overflowMode=TextOverflowModes.Ellipsis;name.richText=false;
        Label(detail.transform,"LevelCaption","玩家等級",-50,-84,185,40,18,Muted);
        var level=Label(detail.transform,"Level","",84,-84,116,40,24,Cream);level.alignment=TextAlignmentOptions.MidlineRight;
        Label(detail.transform,"DateCaption","最後遊玩",0,-132,285,30,18,Muted);
        var date=Label(detail.transform,"Date","",0,-163,285,32,20,Cream);
        var count=Label(pane,"SaveCountV2","0 份存檔",-380,-320,360,48,20,Muted);
        Set(view,"emptyState",empty.gameObject);Set(view,"detailsRoot",detail);Set(view,"stageLabel",stage);Set(view,"nameLabel",name);Set(view,"levelLabel",level);Set(view,"dateLabel",date);Set(view,"countLabel",count);view.Show(null);
        var flow=menu.GetComponent<MenuSaveFlowController>();Set(flow,"selectionSummary",view);
        BuildRow();Set(flow,"saveSlotRowPrefab",AssetDatabase.LoadAssetAtPath<GameObject>(RowPath).GetComponent<MenuSaveSlotRow>());
        var delete=root.Find("DeleteConfirmation");var block=delete.Find("ModalBlocker").GetComponent<Image>();block.color=new Color(.015f,.025f,.01f,.88f);
        var dp=delete.Find("ConfirmationPanel");At(dp,0,0,830,510);var di=dp.GetComponent<Image>();di.sprite=panel;di.type=UnityEngine.UI.Image.Type.Sliced;di.color=Surface;
        dp.Find("DangerAccent").gameObject.SetActive(false);
        Text(dp.Find("Title").GetComponent<TMP_Text>(),"刪除存檔？",42,Cream);At(dp.Find("Title"),0,153,700,70);
        Stripe(dp,-225,135,260,27);dp.Find("Title").SetAsLastSibling();
        Label(dp,"KickerV2","ARCHIVE / DELETE",0,210,700,25,16,Muted);
        var msg=dp.Find("Message").GetComponent<TMP_Text>();Text(msg,null,26,Cream);At(msg.transform,0,0,700,175);msg.enableWordWrapping=true;msg.richText=false;
        var cancel=dp.Find("CancelDelete").GetComponent<Button>();At(cancel.transform,50,-181,215,70);Brush(cancel,"取消",false);
        var confirm=dp.Find("ConfirmDelete").GetComponent<Button>();At(confirm.transform,290,-181,235,70);Brush(confirm,"確認刪除",true,true);
    }
    static void BuildRow()
    {
        if(AssetDatabase.LoadAssetAtPath<GameObject>(RowPath))throw new InvalidOperationException("V2 存檔列已存在，請避免覆寫人工修改。");
        if(!AssetDatabase.CopyAsset("Assets/Prefabs/UI/Menu/SaveSlotRow.prefab",RowPath))throw new InvalidOperationException("無法建立 V2 存檔列。");
        var root=PrefabUtility.LoadPrefabContents(RowPath);
        try
        {
            var layout=root.GetComponent<LayoutElement>();layout.preferredHeight=146;layout.minHeight=146;
            root.GetComponent<Image>().sprite=null;root.GetComponent<Image>().color=new Color(.065f,.1f,.04f,.65f);
            root.GetComponent<Button>().colors=ControlColors();
            var selected=root.transform.Find("SelectedIndicator").GetComponent<Image>();selected.sprite=null;selected.color=new Color(.62f,.59f,.22f,.35f);Stretch(selected.rectTransform);selected.transform.SetAsFirstSibling();selected.raycastTarget=false;
            foreach(var tx in root.GetComponentsInChildren<TMP_Text>(true)){Text(tx,null,23,Cream);tx.richText=false;tx.enableWordWrapping=false;tx.overflowMode=TextOverflowModes.Ellipsis;}
            RowText(root.transform.Find("Level"),20,-10,32);root.transform.Find("Level").GetComponent<TMP_Text>().color=Gold;root.transform.Find("Level").GetComponent<TMP_Text>().fontSize=19;
            RowText(root.transform.Find("DisplayName"),20,-44,48);root.transform.Find("DisplayName").GetComponent<TMP_Text>().fontSize=28;
            RowText(root.transform.Find("LastPlayed"),20,-102,30);root.transform.Find("LastPlayed").GetComponent<TMP_Text>().fontSize=18;root.transform.Find("LastPlayed").GetComponent<TMP_Text>().color=Muted;
            var del=root.transform.Find("DeleteButton");var dr=(RectTransform)del;dr.anchorMin=dr.anchorMax=new Vector2(1,.5f);dr.pivot=new Vector2(1,.5f);dr.anchoredPosition=new Vector2(-7,0);dr.sizeDelta=new Vector2(54,64);
            var im=del.GetComponent<Image>();im.sprite=null;im.color=Color.clear;del.GetComponent<Button>().colors=ControlColors();
            var label=del.GetComponentInChildren<TMP_Text>();Text(label,"×",34,Muted);Stretch(label.rectTransform);label.alignment=TextAlignmentOptions.Center;
            PrefabUtility.SaveAsPrefabAsset(root,RowPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void RowText(Transform t,float x,float y,float h){var r=(RectTransform)t;r.anchorMin=new Vector2(0,1);r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,1);r.offsetMin=new Vector2(x,y-h);r.offsetMax=new Vector2(-70,y);r.localScale=Vector3.one;}
    static void Name(Transform root)
    {
        var block=root.Find("Blocker").GetComponent<Image>();block.color=new Color(.025f,.04f,.015f,.85f);
        var pane=root.Find("PoUp");DisableLayout(pane);At(pane,0,0,840,500);
        var bg=pane.Find("Background").GetComponent<Image>();bg.sprite=panel;bg.type=UnityEngine.UI.Image.Type.Sliced;bg.color=Surface;Stretch(bg.rectTransform);
        var title=pane.Find("PopUpHeader").GetComponent<TMP_Text>();DisableLayout(title.transform);Text(title,"玩家名稱",44,Cream);At(title.transform,0,130,700,80);
        var description=pane.Find("PopUpText").GetComponent<TMP_Text>();DisableLayout(description.transform);description.gameObject.SetActive(true);Text(description,"讓隊友知道，這次是誰一起出發。",22,Muted);At(description.transform,0,60,700,55);
        Label(pane,"KickerV2","IDENTITY / PLAYER",0,205,700,26,16,Muted);Stripe(pane,-225,109,270,26);title.transform.SetAsLastSibling();
        var input=pane.Find("InputFieldButton").GetComponent<TMP_InputField>();At(input.transform,0,-40,700,70);Input(input);
        var go=new GameObject("ConfirmNameV2",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));Undo.RegisterCreatedObjectUndo(go,"Name confirmation");go.transform.SetParent(pane,false);
        var target=go.GetComponent<Button>();var from=new SerializedObject(root.Find("Blocker").GetComponent<Button>());var to=new SerializedObject(target);to.CopyFromSerializedProperty(from.FindProperty("m_OnClick"));to.ApplyModifiedPropertiesWithoutUndo();
        At(go.transform,235,-173,235,74);Brush(target,"確認",true);
        Label(pane,"NameHintV2","按 Enter 或點確認完成",-150,-173,380,45,18,Muted);
        root.SetAsLastSibling();
    }
    static void Loading(Transform root)
    {
        Screen(root,840,480);
        Label(root,"KickerV2","DEPLOYMENT",0,186,700,28,16,Muted);
        Label(root,"TitleV2","準備行動",0,124,700,74,44,Cream);Stripe(root,-225,105,280,28);root.Find("TitleV2").SetAsLastSibling();
        var icon=root.Find("LoadingIcon");At(icon,-300,-12,64,64);
        foreach(var im in icon.GetComponentsInChildren<Image>(true)) im.color=Gold;
        var label=icon.Find("LoadingLabel").GetComponent<TMP_Text>();Text(label,null,25,Cream);At(label.transform,345,0,570,104);
        var b=root.Find("SecondaryButton").GetComponent<Button>();At(b.transform,237,-171,235,72);Brush(b,"取消連線",false);
    }
    static void Popup(Transform root)
    {
        var anim=root.GetComponent<Animator>();anim.runtimeAnimatorController=AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Art+"/MenuBrushFadeV1.controller");anim.updateMode=AnimatorUpdateMode.UnscaledTime;
        root.GetComponent<CanvasGroup>().alpha=1;
        root.Find("Blocker").GetComponent<Image>().color=new Color(.02f,.035f,.01f,.84f);
        var pane=root.Find("PoUp");DisableLayout(pane);At(pane,0,0,900,550);var cg=pane.GetComponent<CanvasGroup>();if(cg)cg.alpha=1;
        var bg=pane.Find("Background").GetComponent<Image>();bg.sprite=panel;bg.type=UnityEngine.UI.Image.Type.Sliced;bg.color=Surface;Stretch(bg.rectTransform);
        var header=pane.Find("PopUpHeader").GetComponent<TMP_Text>();DisableLayout(header.transform);Text(header,null,42,Cream);At(header.transform,0,158,760,90);
        Stripe(pane,-240,136,300,28);header.transform.SetAsLastSibling();
        Label(pane,"KickerV2","OPERATION / NOTICE",0,223,760,26,16,Muted);
        var text=pane.Find("PopUpText").GetComponent<TMP_Text>();DisableLayout(text.transform);Text(text,null,25,Cream);At(text.transform,0,-5,760,205);text.enableWordWrapping=true;text.enableAutoSizing=true;text.fontSizeMin=18;text.fontSizeMax=25;text.overflowMode=TextOverflowModes.Overflow;
        var container=pane.Find("ButtonContainer");DisableLayout(container);Stretch((RectTransform)container);
        var b=container.Find("PrimaryButton").GetComponent<Button>();At(b.transform,260,-199,235,72);Brush(b,"確認",true);
    }
    static void Brush(Button b,string caption,bool primary,bool danger=false)
    {
        var a=b.GetComponent<Animator>();if(a)a.enabled=false;b.transition=Selectable.Transition.None;
        var hit=b.GetComponent<Image>();hit.sprite=null;hit.type=UnityEngine.UI.Image.Type.Simple;hit.color=Color.clear;hit.raycastTarget=true;
        var bg=b.transform.Find("Background")?.GetComponent<Image>();
        if(!bg)bg=Image(b.transform,"Background",Color.white);
        Stretch(bg.rectTransform);bg.sprite=brush;bg.type=UnityEngine.UI.Image.Type.Simple;bg.color=danger?new Color(.66f,.41f,.27f):primary?Gold:new Color(.25f,.3f,.19f);bg.raycastTarget=false;
        var cg=bg.GetComponent<CanvasGroup>();if(!cg)cg=Undo.AddComponent<CanvasGroup>(bg.gameObject);cg.alpha=1;cg.blocksRaycasts=false;
        var hi=Image(bg.transform,"BrushHighlight",danger?new Color(.76f,.5f,.34f):Gold,brush);Stretch(hi.rectTransform);hi.type=UnityEngine.UI.Image.Type.Filled;hi.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;hi.fillOrigin=0;hi.fillAmount=0;hi.transform.SetAsFirstSibling();
        TMP_Text label=null;var old=bg.transform.Find("ButtonLabel");if(old)label=old.GetComponent<TMP_Text>();else label=Label(bg.transform,"ButtonLabel","",0,0,180,60,26,Cream);
        Text(label,caption,26,primary&&!danger?Ink:Cream);Stretch(label.rectTransform,16,5,-16,-5);label.alignment=TextAlignmentOptions.Center;
        var icon=b.transform.Find("ButtonIcon");if(icon)icon.gameObject.SetActive(false);
        var visual=b.GetComponent<MenuBrushButtonVisual>();if(!visual)visual=Undo.AddComponent<MenuBrushButtonVisual>(b.gameObject);
        Set(visual,"button",b);Set(visual,"visualRoot",bg.rectTransform);Set(visual,"highlight",hi);Set(visual,"label",label);Set(visual,"visualGroup",cg);Set(visual,"labelRestPosition",Vector2.zero);Set(visual,"normalText",primary&&!danger?Ink:Cream);Set(visual,"highlightedText",danger?Cream:Ink);
        var group=b.GetComponent<CanvasGroup>();if(group)group.alpha=1;
        b.targetGraphic=hit;
    }
    static ColorBlock ControlColors(){var c=ColorBlock.defaultColorBlock;c.highlightedColor=new Color(1.28f,1.28f,1.12f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.72f,.72f,.64f);c.disabledColor=new Color(.4f,.4f,.4f,.6f);c.fadeDuration=.12f;return c;}
    static void DisableLayout(Transform t){var g=t.GetComponent<LayoutGroup>();if(g)g.enabled=false;var f=t.GetComponent<ContentSizeFitter>();if(f)f.enabled=false;}
    static void Text(TMP_Text t,string value,float size,Color color){t.font=font;t.fontSharedMaterial=font.material;t.fontStyle=FontStyles.Normal;t.fontWeight=FontWeight.Regular;t.overflowMode=TextOverflowModes.Overflow;if(value!=null)t.text=value;t.fontSize=size;t.enableAutoSizing=false;t.color=color;t.raycastTarget=false;t.alignment=TextAlignmentOptions.MidlineLeft;}
    static TMP_Text Label(Transform parent,string name,string text,float x,float y,float w,float h,float size,Color color){var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(TextMeshProUGUI));Undo.RegisterCreatedObjectUndo(go,"Menu text");go.transform.SetParent(parent,false);var t=go.GetComponent<TMP_Text>();Text(t,text,size,color);At(go.transform,x,y,w,h);return t;}
    static Image Image(Transform parent,string name,Color color,Sprite sprite=null){var go=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));Undo.RegisterCreatedObjectUndo(go,"Menu decoration");go.transform.SetParent(parent,false);var im=go.GetComponent<Image>();im.color=color;im.sprite=sprite;im.raycastTarget=false;return im;}
    static void Stripe(Transform p,float x,float y,float w,float h){var im=Image(p,"TitleBrushV2",new Color(.69f,.64f,.22f,.6f),brush);At(im.transform,x,y,w,h);}
    static void At(Transform t,float x,float y,float w,float h){var r=(RectTransform)t;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;r.localRotation=Quaternion.identity;}
    static void Stretch(RectTransform r,float left=0,float bottom=0,float right=0,float top=0){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=Vector2.one*.5f;r.offsetMin=new Vector2(left,bottom);r.offsetMax=new Vector2(right,top);r.localScale=Vector3.one;r.localRotation=Quaternion.identity;}
    static void Set(UnityEngine.Object obj,string name,object value){var so=new SerializedObject(obj);var p=so.FindProperty(name);if(p==null)throw new InvalidOperationException(name);if(value is UnityEngine.Object o)p.objectReferenceValue=o;else if(value is Color c)p.colorValue=c;else if(value is Vector2 v)p.vector2Value=v;so.ApplyModifiedPropertiesWithoutUndo();}
}
