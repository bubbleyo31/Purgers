using System.Collections.Generic;
using Fusion;
using TMPro;
using UnityEngine;

/// <summary>每台玩家只讀所屬 Runner 的名冊／生命／護盾；UI 與描邊不回寫 Gameplay。</summary>
[DisallowMultipleComponent,RequireComponent(typeof(RectTransform)),DefaultExecutionOrder(220)]
public sealed class LocalTeammateHUD : MonoBehaviour
{
    [Header("隊友顯示引用")]
    [SerializeField,Tooltip("共用隊友顯示設定。需指定；可於 Play Mode 調整。")] private TeammateDisplaySettings settings;
    [SerializeField,Tooltip("所屬 Runner；留空時只接受唯一有效本機 Runner，Multi Peer 須明確指定。")] private NetworkRunner runner;
    [SerializeField,Tooltip("世界相機；不得用第一人稱手部相機。單一 Runner 留空用 Camera.main。")] private Camera worldCamera;
    private bool explicitRunner,explicitCamera;
    private readonly StatusEffectSnapshot[] statusBuffer = new StatusEffectSnapshot[StatusEffectSources.Capacity];
    private RectTransform root;
    private Canvas canvas;
    private readonly Dictionary<PlayerRef,View> views=new Dictionary<PlayerRef,View>();
    private readonly List<PlayerRef> removed=new List<PlayerRef>();
    private readonly List<Vector2> arrowPositions=new List<Vector2>();
    private readonly List<OutlineTarget> outlineTargets=new List<OutlineTarget>();
    private static readonly List<LocalTeammateHUD> active=new List<LocalTeammateHUD>();
    public readonly struct OutlineTarget
    {
        public readonly TeammateVisualAnchor Anchor;
        public readonly int Slot;
        public OutlineTarget(TeammateVisualAnchor anchor,int slot){Anchor=anchor;Slot=slot;}
    }
    private sealed class View
    {
        public RectTransform Root;
        public TextMeshProUGUI Name;
        public LocalHealthSegmentView Health;
        public StatusEffectListView Status;
        public StatusEffectSources Sources;
        public TeammateMarkerGraphic Marker;
        public NetworkObject Target;
        public TeammateVisualAnchor Anchor;
        public bool Seen,Onscreen;
        public string DisplayName;
    }
    public TeammateDisplaySettings Settings=>settings;
    public IReadOnlyList<OutlineTarget> OutlineTargets=>outlineTargets;
    public int NameplateCount{get;private set;}
    public int ArrowCount{get;private set;}
    public static LocalTeammateHUD ForCamera(Camera camera)
    {
        foreach(var hud in active)if(hud!=null && hud.isActiveAndEnabled && hud.worldCamera==camera && hud.outlineTargets.Count>0)return hud;
        return null;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()=>active.Clear();
    private void Awake(){root=(RectTransform)transform;canvas=GetComponentInParent<Canvas>();explicitRunner=runner!=null;explicitCamera=worldCamera!=null;}
    private void OnEnable(){if(!active.Contains(this))active.Add(this);}
    private void OnDisable(){active.Remove(this);Clear();}
    private void OnDestroy(){active.Remove(this);Clear();}
    public void Configure(TeammateDisplaySettings value,NetworkRunner owner,Camera camera)
    {Clear();settings=value;runner=owner;worldCamera=camera;explicitRunner=owner!=null;explicitCamera=camera!=null;}
    private void Clear()
    {
        foreach(var view in views.Values)if(view.Root!=null){if(Application.isPlaying)Destroy(view.Root.gameObject);else DestroyImmediate(view.Root.gameObject);}
        views.Clear();outlineTargets.Clear();NameplateCount=ArrowCount=0;
    }
    private void Resolve()
    {
        if(explicitRunner && explicitCamera)return;
        NetworkRunner unique=null;int count=0;
        foreach(var item in NetworkRunner.Instances)if(item.IsRunning && item.LocalPlayer.IsRealPlayer){unique=item;count++;}
        if(!explicitRunner){var next=count==1?unique:null;if(next!=runner){Clear();runner=next;}}
        if(!explicitCamera)worldCamera=count==1?Camera.main:null;
    }
    private Vector2 Local(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,screen,canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var value);
        return value-root.rect.center;
    }
    private static RectTransform Rect(string name,Transform parent,Vector2 size)
    {
        var value=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();value.SetParent(parent,false);
        value.anchorMin=value.anchorMax=value.pivot=Vector2.one*.5f;value.sizeDelta=size;return value;
    }
    private View Create(PlayerRef player)
    {
        var v=new View{Root=Rect("Teammate "+player.RawEncoded,root,Vector2.zero)};
        v.Name=Rect("Name",v.Root,new Vector2(180,32)).gameObject.AddComponent<TextMeshProUGUI>();
        v.Name.alignment=TextAlignmentOptions.Center;v.Name.richText=false;v.Name.raycastTarget=false;v.Name.enableWordWrapping=false;v.Name.overflowMode=TextOverflowModes.Ellipsis;
        v.Health=Rect("Health and Shield",v.Root,new Vector2(110,9)).gameObject.AddComponent<LocalHealthSegmentView>();
        v.Status=StatusEffectListView.Create(v.Health.rectTransform);
        v.Marker=Rect("Direction or Death",v.Root,new Vector2(26,26)).gameObject.AddComponent<TeammateMarkerGraphic>();
        views.Add(player,v);return v;
    }
    private void LateUpdate()
    {
        Resolve();outlineTargets.Clear();arrowPositions.Clear();NameplateCount=ArrowCount=0;
        var logic=GameLogic.GetPrimaryForRunner(runner);
        if(settings==null || canvas==null || canvas.renderMode==RenderMode.WorldSpace || worldCamera==null || !worldCamera.isActiveAndEnabled ||
            runner==null || !runner.IsRunning || logic==null || !logic.IsTeammateRosterReady ||
            runner.IsSceneManagerBusy || (!explicitCamera && (!LocalPlayerSpectatorController.IsObserving(runner) && !Purgers.GameFlow.Transition.LocalSceneTransition.TryGetReadyLocalPlayer(runner,out _))))
        {foreach(var v in views.Values)v.Root.gameObject.SetActive(false);return;}
        float pixels=worldCamera.pixelHeight/1080f;
        float units=Mathf.Abs((Local(worldCamera.pixelRect.center+Vector2.up)-Local(worldCamera.pixelRect.center)).y);
        float scale=pixels*units;
        foreach(var v in views.Values)v.Seen=false;
        foreach(var player in runner.ActivePlayers)
        {
            if(player==LocalPlayerSpectatorController.ViewedPlayer(runner) || !logic.TryGetTeammateIdentity(player,out var identity) ||
                !logic.TryGetTeammateTarget(player,out var target,out var position,out bool dead))continue;
            if(!views.TryGetValue(player,out var v))v=Create(player);
            if(v.Target!=target){v.Health.ResetPresentation();v.Status.Clear();v.Target=target;v.Anchor=target!=null?target.GetComponent<TeammateVisualAnchor>():null;v.Sources=target!=null?new StatusEffectSources(target.gameObject):null;}
            v.Seen=true;v.Root.gameObject.SetActive(true);
            Color tint=TeammateDisplayRules.IdentityColor(identity.ColorSlot);
            Vector3 head=(!dead && v.Anchor!=null && v.Anchor.headAnchor!=null)?v.Anchor.headAnchor.position:position+Vector3.up*settings.fallbackHeadHeight;
            Vector3 screen=worldCamera.WorldToScreenPoint(head);
            float margin=(v.Onscreen?0:settings.boundaryHysteresis)*pixels+settings.nameWidth*.5f*pixels;
            Rect area=worldCamera.pixelRect;
            int statusCount=!dead && v.Sources!=null?v.Sources.Read(runner.LocalPlayer,statusBuffer):0;
            var statusSettings=StatusEffectHudSettings.Default;
            float statusHeight=statusSettings!=null?statusSettings.Height(statusCount,false):0;
            v.Status.Present(statusBuffer,statusCount,statusSettings,settings.nameWidth*.66f,false);
            bool onscreen=screen.z>worldCamera.nearClipPlane && screen.x>=area.xMin+margin && screen.x<=area.xMax-margin &&
                screen.y>=area.yMin+32*pixels && screen.y<=area.yMax-(Mathf.Max(48,statusHeight+36)+settings.headPadding)*pixels;
            v.Onscreen=onscreen;
            v.Name.gameObject.SetActive(onscreen);v.Health.gameObject.SetActive(onscreen && !dead);v.Marker.gameObject.SetActive(!onscreen || dead);
            if(!dead && target!=null && v.Anchor!=null)outlineTargets.Add(new OutlineTarget(v.Anchor,identity.ColorSlot));
            v.Marker.color=tint;
            if(onscreen)
            {
                NameplateCount++;
                v.Root.anchoredPosition=Local(screen)+Vector2.up*settings.headPadding*scale;
                string name=identity.Name.ToString();if(dead)name+=" · 已死亡";
                if(name!=v.DisplayName){v.Name.text=name;v.DisplayName=name;}
                if(settings.font!=null && v.Name.font!=settings.font)v.Name.font=settings.font;
                v.Name.color=tint;v.Name.fontSize=settings.nameSize;
                v.Name.rectTransform.sizeDelta=new Vector2(settings.nameWidth,32);v.Name.rectTransform.anchoredPosition=new Vector2(0,(20+statusHeight)*scale);v.Name.rectTransform.localScale=Vector3.one*scale;
                if(dead){v.Marker.SetShape(TeammateMarkerGraphic.Shape.Death);v.Marker.rectTransform.localRotation=Quaternion.identity;v.Marker.rectTransform.sizeDelta=Vector2.one*26*scale;}
                else
                {
                    var hp=target.GetComponent<PlayerHealth>();var body=target.GetComponent<Player>();
                    var gate=target.GetComponent<PlayerActionGate>();
                    if(hp!=null && gate!=null && gate.IsFusionSpawned)
                    {
                        v.Health.ConfigureTeammatePresentation(settings);
                        v.Health.ApplyHealthSnapshot(hp.CurrentHealth,hp.MaximumHealth);
                        var shield=PlayerAbilityQualification.GetModule<PlayerShieldAbility>(body);
                        v.Health.SetShieldHealth(shield!=null?shield.CurrentShield:0);
                    }
                    v.Health.rectTransform.sizeDelta=new Vector2(settings.nameWidth*.66f,settings.barHeight);v.Health.rectTransform.localScale=Vector3.one*scale;
                }
            }
            else
            {
                ArrowCount++;
                Vector3 local=worldCamera.transform.InverseTransformPoint(head);
                Vector2 direction=TeammateDisplayRules.EdgeDirection(local,worldCamera.fieldOfView,worldCamera.aspect);
                float inset=Mathf.Max(settings.edgeInset,settings.arrowSize*.75f)*pixels;
                Vector2 point=TeammateDisplayRules.EdgePoint(area,direction,inset);
                point=TeammateDisplayRules.SeparateEdgePoint(area,point,inset,settings.arrowSize*pixels*1.3f,arrowPositions);
                arrowPositions.Add(point);v.Root.anchoredPosition=Local(point);
                v.Marker.SetShape(TeammateMarkerGraphic.Shape.Arrow);v.Marker.rectTransform.sizeDelta=Vector2.one*settings.arrowSize*scale;
                v.Marker.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            }
        }
        removed.Clear();foreach(var pair in views)if(!pair.Value.Seen)removed.Add(pair.Key);
        foreach(var key in removed){var v=views[key];Destroy(v.Root.gameObject);views.Remove(key);}
    }
}
