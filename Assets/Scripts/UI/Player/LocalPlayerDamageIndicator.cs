using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>只訂閱本地 Input Authority 的已結算受傷事件。2D 與 3D 共用位置快照，不追蹤來源 Transform。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class LocalPlayerDamageIndicator : MonoBehaviour
{
    [Header("受傷來源指示 V1")]
    [SerializeField, Tooltip("共用外觀與效能設定；可於 Play Mode 調整。圖形由本地物件池呈現，不產生 NetworkObject。")]
    private DamageIndicatorSettings settings;
    [SerializeField, Tooltip("所屬 Runner；留空僅在唯一有效本機 Runner 時自動綁定。Multi Peer 請明確指定。")]
    private NetworkRunner runner;
    [SerializeField, Tooltip("世界相機，不能指定 ViewModel 相機；留空使用 Camera.main。")]
    private Camera worldCamera;
    [SerializeField, Tooltip("全螢幕 UI 容器；留空使用本物件 RectTransform。")]
    private RectTransform visualRoot;
    private bool explicitRunner;
    private PlayerHealth health;
    private Canvas canvas;
    private readonly Entry[] entries = new Entry[8];
    private readonly DamageIndicatorOcclusionQuery occlusion = new DamageIndicatorOcclusionQuery();
    private int probeCursor;
    private float nextResolve;
    public int ActiveCount { get; private set; }
    public int ProbeCountLastFrame { get; private set; }
    public int PhysicsQueriesLastFrame { get; private set; }
    public int ReceivedEventCount { get; private set; }
    public PlayerHealth BoundHealth => health;
    public DamageIndicatorSettings Settings => settings;

    private sealed class Entry
    {
        public bool active, hasPosition, inViewport, occluded, sampled;
        public Vector3 position, direction;
        public Transform source; // 僅排除來源自己的 Collider，不讀取位置。
        public float start, nextProbe;
        public DamageIndicatorGraphic arc, marker;
        public Outline arcOutline, markerOutline;
        public CanvasGroup arcGroup, markerGroup;
    }
    private void Awake()
    {
        explicitRunner=runner!=null;
        if(visualRoot==null) visualRoot=transform as RectTransform;
        canvas=GetComponentInParent<Canvas>();
    }
    public void Configure(DamageIndicatorSettings value, NetworkRunner ownerRunner, Camera camera)
    {
        Unbind();settings=value;runner=ownerRunner;explicitRunner=ownerRunner!=null;worldCamera=camera;nextResolve=0;
    }
    private void OnDisable() => Unbind();
    private void OnDestroy() => Unbind();
    private void Unbind()
    {
        if(health!=null)health.LocalDamageReceived-=Receive;
        health=null;Clear();
    }
    public void Clear()
    {
        ActiveCount=0;ProbeCountLastFrame=PhysicsQueriesLastFrame=0;
        for(int i=0;i<entries.Length;i++) if(entries[i]!=null)Deactivate(entries[i]);
    }
    private void Deactivate(Entry e)
    {
        e.active=false;e.source=null;
        if(e.arc!=null)e.arc.gameObject.SetActive(false);
        if(e.marker!=null)e.marker.gameObject.SetActive(false);
    }
    private void ResolveHealth(float now)
    {
        if(runner==null || !runner.IsRunning)
        {
            if(health!=null)Unbind();
            if(explicitRunner || now<nextResolve)return;
            nextResolve=now+.2f;runner=null;
            foreach(var candidate in NetworkRunner.Instances)
            {
                if(!candidate.IsRunning || !candidate.LocalPlayer.IsRealPlayer)continue;
                if(runner!=null){runner=null;return;} // 不猜另一個 Peer。
                runner=candidate;
            }
        }
        PlayerHealth found=null;
        if(runner!=null && runner.IsRunning && runner.TryGetPlayerObject(runner.LocalPlayer,out var player) &&
            player!=null && player.IsValid && player.HasInputAuthority)found=player.GetComponent<PlayerHealth>();
        if(found==health)return;
        Unbind();health=found;
        if(health!=null)health.LocalDamageReceived+=Receive;
    }
    private void Receive(PlayerDamageReceivedInfo info)
    {
        if(settings==null || health==null || health.Object==null || !health.Object.IsValid || !health.Object.HasInputAuthority)return;
        if(info.AppliedDamage<=0 && info.BlockedDamage<=0)return;
        ReceivedEventCount++;
        AddSnapshot(info,Time.unscaledTime);
    }
    private void AddSnapshot(PlayerDamageReceivedInfo info,float now)
    {
        if(!info.HasSourceWorldPosition && info.DirectionToSource.sqrMagnitude<.00001f)return;
        if(visualRoot==null || canvas==null)return;
        int limit=Mathf.Clamp(settings.maximumIndicators,1,entries.Length);
        Transform source=info.SourceNetworkObject!=null ? info.SourceNetworkObject.transform : null;
        int index=-1;float oldest=float.PositiveInfinity;
        // 同一來源的新命中刷新快照；不於之後逐幀追蹤來源。
        for(int i=0;i<limit;i++)
            if(entries[i]!=null && entries[i].active && source!=null && entries[i].source==source){index=i;break;}
        if(index<0)
            for(int i=0;i<limit;i++)
            {
                if(entries[i]==null || !entries[i].active){index=i;break;}
                if(entries[i].start<oldest){oldest=entries[i].start;index=i;}
            }
        var e=entries[index]??(entries[index]=CreateEntry(index));
        e.active=true;e.hasPosition=info.HasSourceWorldPosition;e.position=info.SourceWorldPosition;
        e.direction=info.DirectionToSource;e.source=source;e.start=now;e.nextProbe=now;
        e.sampled=false;e.occluded=true;e.inViewport=false;
    }
    private Entry CreateEntry(int index)
    {
        var e=new Entry();
        e.arc=CreateGraphic("Damage Arc "+index,out e.arcGroup,out e.arcOutline);
        e.marker=CreateGraphic("Damage Source "+index,out e.markerGroup,out e.markerOutline);
        return e;
    }
    private DamageIndicatorGraphic CreateGraphic(string label,out CanvasGroup group,out Outline outline)
    {
        var go=new GameObject(label,typeof(RectTransform),typeof(CanvasRenderer),typeof(CanvasGroup),typeof(DamageIndicatorGraphic),typeof(Outline));
        go.transform.SetParent(visualRoot,false);
        var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
        var graphic=go.GetComponent<DamageIndicatorGraphic>();graphic.raycastTarget=false;graphic.preserveAspect=true;
        group=go.GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;
        outline=go.GetComponent<Outline>();outline.useGraphicAlpha=true;
        return graphic;
    }
    private void LateUpdate()
    {
        float now=Time.unscaledTime;ProbeCountLastFrame=PhysicsQueriesLastFrame=0;
        ResolveHealth(now);
        if(settings==null || health==null || health.Object==null || !health.Object.IsValid || !health.IsAlive)
        {Clear();return;}
        if(worldCamera==null || !worldCamera.isActiveAndEnabled)worldCamera=Camera.main;
        if(worldCamera==null || visualRoot==null || canvas==null){Clear();return;}
        RenderSnapshots(now,worldCamera,health.gameObject.scene.GetPhysicsScene(),health.transform);
    }
    private Vector2 CanvasPosition(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(visualRoot,screen,
            canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,out var local);
        // 子圖形使用中心錨點；補償巢狀 Canvas 非中心 pivot。
        return local - visualRoot.rect.center;
    }
    private void RenderSnapshots(float now,Camera camera,PhysicsScene scene,Transform receiver)
    {
        int limit=Mathf.Clamp(settings.maximumIndicators,1,entries.Length);
        float scale=camera.pixelHeight/1080f/Mathf.Max(.0001f,canvas.scaleFactor);
        Vector2 screenCenter=CanvasPosition(camera.pixelRect.center);
        ActiveCount=0;
        for(int i=0;i<entries.Length;i++)
        {
            var e=entries[i];if(e==null || !e.active)continue;
            float age=now-e.start;
            if(i>=limit || age>=Mathf.Max(settings.arc.Duration,settings.marker.Duration)){Deactivate(e);continue;}
            ActiveCount++;
            Vector3 direction=e.hasPosition?e.position-camera.transform.position:e.direction;
            Vector2 bearing=DamageIndicatorRules.ScreenDirection(camera.transform.InverseTransformDirection(direction));
            var ar=e.arc.rectTransform;
            e.arc.gameObject.SetActive(settings.arc.enabled && bearing.sqrMagnitude>.001f && age<settings.arc.Duration);
            ApplyStyle(e.arc,e.arcOutline,e.arcGroup,settings.arc,DamageIndicatorGraphic.Geometry.Arc,scale,age);
            ar.pivot=new Vector2(.5f,.5f);
            bool custom=settings.arc.sprite!=null;
            ar.anchoredPosition=screenCenter+(custom?bearing*Mathf.Max(1,settings.arcRadius)*scale:Vector2.zero);
            ar.sizeDelta=custom?settings.arcSpriteSize*scale:Vector2.one*(settings.arcRadius*2+settings.arcThickness)*scale;
            ar.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(bearing.y,bearing.x)*Mathf.Rad2Deg-90+(custom?settings.arcSpriteRotation:0));
            Vector3 viewport=e.hasPosition?camera.WorldToViewportPoint(e.position):new Vector3(0,0,-1);
            bool inView=settings.marker.enabled && age<settings.marker.Duration && e.hasPosition && viewport.z>camera.nearClipPlane &&
                viewport.x>=0 && viewport.x<=1 && viewport.y>=0 && viewport.y<=1;
            if(inView && !e.inViewport){e.sampled=false;e.nextProbe=now;}
            e.inViewport=inView;
            e.marker.gameObject.SetActive(inView);
            if(!inView)continue;
            var mr=e.marker.rectTransform;mr.pivot=settings.markerPivot;mr.sizeDelta=settings.markerSize*scale;
            mr.anchoredPosition=CanvasPosition(camera.WorldToScreenPoint(e.position))+settings.markerOffset*scale;
            ApplyStyle(e.marker,e.markerOutline,e.markerGroup,settings.marker,DamageIndicatorGraphic.Geometry.Triangle,scale,age);
        }
        // 即使 marker alpha=0，仍按相同排程重查；每幀限制及輪流游標避免來源飢餓。
        int budget=Mathf.Clamp(settings.maximumProbesPerFrame,1,8);
        int start=probeCursor;
        for(int n=0;n<limit && ProbeCountLastFrame<budget;n++)
        {
            int i=(start+n)%limit;var e=entries[i];
            if(e==null || !DamageIndicatorRules.ShouldProbe(e.active,e.inViewport,now,e.nextProbe))continue;
            e.occluded=occlusion.IsOccluded(scene,camera.transform.position,e.position,settings.obstructionMask,receiver,e.source);
            e.sampled=true;e.nextProbe=now+Mathf.Clamp(settings.occlusionInterval,.02f,.2f);
            ProbeCountLastFrame++;PhysicsQueriesLastFrame+=occlusion.PhysicsQueryCount;probeCursor=(i+1)%limit;
        }
        for(int i=0;i<limit;i++)
        {
            var e=entries[i];if(e==null || !e.active || !e.inViewport)continue;
            float alpha=!e.sampled?0:e.occluded?(settings.occlusionMode==DamageIndicatorOcclusionMode.HideBehindWalls?0:settings.occludedOpacity):settings.marker.opacity;
            e.markerGroup.alpha=Mathf.Clamp01(alpha)*DamageIndicatorRules.Envelope(now-e.start,settings.marker.fadeIn,settings.marker.hold,settings.marker.fadeOut);
        }
    }
    private void ApplyStyle(DamageIndicatorGraphic graphic,Outline outline,CanvasGroup group,DamageIndicatorStyle style,
        DamageIndicatorGraphic.Geometry shape,float scale,float age)
    {
        graphic.sprite=style.sprite;graphic.color=style.color;graphic.Configure(shape,settings,scale);
        outline.effectColor=style.outlineColor;outline.effectDistance=Vector2.one*Mathf.Max(0,style.outlineWidth)*scale;
        outline.enabled=style.outlineWidth>0;
        group.alpha=Mathf.Clamp01(style.opacity)*DamageIndicatorRules.Envelope(age,style.fadeIn,style.hold,style.fadeOut);
    }
}
