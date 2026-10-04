using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ActiveAbilityGizmoPreview
{
    public string Title, Description;
    public Color Color;
    public Vector3 Origin, Direction;
    public float EffectRadius;
    public readonly List<Vector3[]> RangePaths = new List<Vector3[]>();
    public readonly List<Vector3[]> Paths = new List<Vector3[]>();
    public readonly List<ActiveAbilityGizmoSphere> Spheres = new List<ActiveAbilityGizmoSphere>();
    public readonly List<ActiveAbilityGizmoLabel> Labels = new List<ActiveAbilityGizmoLabel>();
}
public struct ActiveAbilityGizmoSphere
{
    public Vector3 Center; public float Radius;
    public ActiveAbilityGizmoSphere(Vector3 center,float radius) { Center=center; Radius=radius; }
}
public struct ActiveAbilityGizmoLabel
{
    public Vector3 Position; public string Text;
    public ActiveAbilityGizmoLabel(Vector3 position,string text) { Position=position; Text=text; }
}

/// <summary>Editor-only：讀當前序列化設定，建立世界公尺的幾何；只查 PhysicsScene，不呼叫施放／傷害／治療。</summary>
public static class ActiveAbilityGizmoPreviewBuilder
{
    private const int Segments = 48;
    private static float F(SerializedObject data,string name) => data.FindProperty(name).floatValue;
    private static int I(SerializedObject data,string name) => data.FindProperty(name).intValue;
    private static string M(float value) => value.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture);
    public static Vector3[] SphericalRim(Vector3 origin,Vector3 forward,float radius,float halfAngle,int segments)
    {
        forward=forward.sqrMagnitude>.000001f?forward.normalized:Vector3.forward;
        Vector3 right=Vector3.Cross(forward,Mathf.Abs(forward.y)>.99f?Vector3.forward:Vector3.up).normalized;
        Vector3 up=Vector3.Cross(right,forward).normalized;
        float angle=halfAngle*Mathf.Deg2Rad;
        var points=new Vector3[Mathf.Max(3,segments)+1];
        for(int n=0;n<points.Length;n++)
        {
            float phi=n*Mathf.PI*2f/(points.Length-1);
            points[n]=origin+radius*(forward*Mathf.Cos(angle)+(right*Mathf.Cos(phi)+up*Mathf.Sin(phi))*Mathf.Sin(angle));
        }
        return points;
    }
    private static void Arrow(List<Vector3[]> paths,Vector3 origin,Vector3 end)
    {
        paths.Add(new[]{origin,end});
        Vector3 delta=end-origin;
        if(delta.sqrMagnitude<.000001f)return;
        Vector3 direction=delta.normalized;
        Vector3 side=Vector3.Cross(direction,Mathf.Abs(direction.y)>.99f?Vector3.forward:Vector3.up).normalized;
        float size=Mathf.Min(.5f,delta.magnitude*.12f);
        paths.Add(new[]{end-direction*size+side*size*.45f,end,end-direction*size-side*size*.45f});
    }
    private static void Mark(ActiveAbilityGizmoPreview p,Vector3 center)
    {
        p.RangePaths.Add(new[]{center-Vector3.right*.12f,center+Vector3.right*.12f});
        p.RangePaths.Add(new[]{center-Vector3.up*.12f,center+Vector3.up*.12f});
        p.RangePaths.Add(new[]{center-Vector3.forward*.12f,center+Vector3.forward*.12f});
    }
    private static void Sphere(ActiveAbilityGizmoPreview p,Vector3 center,float radius,string label)
    {
        if(radius<=0f)return;
        p.Spheres.Add(new ActiveAbilityGizmoSphere(center,radius));
        if(!string.IsNullOrEmpty(label))p.Labels.Add(new ActiveAbilityGizmoLabel(center+Vector3.right*radius,label));
    }
    public static Color SkillColor(string key)
    {
        switch(key)
        {
            case "Grenade":return new Color(1f,.48f,.12f);
            case "PiercingCannon":return new Color(1f,.3f,.6f);
            case "Shockwave":return new Color(1f,.25f,.22f);
            case "Shield":return Color.white;
            case "HealingPack":return new Color(.25f,1f,.4f);
            case "Ricochet":return new Color(.35f,.65f,1f);
            case "Experience":return new Color(1f,.87f,.12f);
            case "Blink":return new Color(.5f,.9f,1f);
            case "BulletTime":return new Color(.78f,.4f,1f);
            default:return new Color(.15f,1f,.82f);
        }
    }
    public static ActiveAbilityGizmoPreview Build(PlayerActiveAbilityBase skill)
    {
        var data=new SerializedObject(skill);
        var settings=skill.RangeGizmos;
        var owner=skill.GizmoOwner;
        bool live=owner!=null;
        var p=new ActiveAbilityGizmoPreview();
        Vector3 feet=live&&owner.Movement!=null&&owner.Movement.KCC!=null?owner.Movement.KCC.Data.TargetPosition:skill.transform.position;
        Vector3 aim=live&&owner.Movement!=null?owner.Movement.GetAimDirection():skill.transform.forward;
        aim=aim.sqrMagnitude>.000001f?aim.normalized:Vector3.forward;
        Vector3 forward=live&&owner.Movement!=null&&owner.Movement.KCC!=null
            ?Quaternion.Euler(0f,owner.Movement.KCC.Data.LookYaw,0f)*Vector3.forward
            :Vector3.ProjectOnPlane(skill.transform.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.000001f)forward=Vector3.forward;
        p.Origin=feet;p.Direction=aim;
        Scene sourceScene=live?owner.gameObject.scene:skill.gameObject.scene;
        bool query=settings.previewCollisions&&sourceScene.IsValid()&&!EditorSceneManager.IsPreviewScene(sourceScene);
        if(PrefabStageUtility.GetPrefabStage(skill.gameObject)!=null)query=false;
        PhysicsScene? physics=query?(live?owner.Runner.GetPhysicsScene():sourceScene.GetPhysicsScene()):(PhysicsScene?)null;
        Transform ignore=live?owner.transform:skill.transform;
        string mode=live?"目前玩家預覽":"未綁定玩家：Prefab／配置預覽（使用物件位置與方向）";
        string key=skill.GetType().Name.Replace("Player","").Replace("Ability","");
        if(skill is PlayerProjectileAbility)
        {
            var kind=(PlayerAbilityProjectileKind)I(data,"projectileKind");
            key=kind==PlayerAbilityProjectileKind.Grenade?"Grenade":kind==PlayerAbilityProjectileKind.HealingPack?"HealingPack":"Ricochet";
            p.Title=key=="Grenade"?"爆破榴彈":key=="HealingPack"?"治療包":"彈射彈";
            p.Origin=feet+Vector3.up*F(data,"originHeight");
            float radius=Mathf.Max(.001f,F(data,"projectileRadius"));
            p.EffectRadius=kind==PlayerAbilityProjectileKind.Grenade?Mathf.Max(.1f,F(data,"explosionRadius")):
                kind==PlayerAbilityProjectileKind.HealingPack?Mathf.Max(radius,F(data,"pickupRadius")):0f;
            float lifetime=kind==PlayerAbilityProjectileKind.HealingPack?10f:F(data,"projectileLifetime");
            float seconds=Mathf.Min(Mathf.Clamp(settings.trajectorySeconds,.1f,10f),Mathf.Max(0f,lifetime));
            p.Description=mode+"\n碰撞球 R "+M(radius)+"m｜初速 "+M(F(data,"projectileSpeed"))+"m/s";
            p.Description+="\n路徑只預覽前 "+M(seconds)+"s（存活 "+M(lifetime)+"s）";
            if(kind==PlayerAbilityProjectileKind.Grenade)p.Description+="\n爆炸 R "+M(p.EffectRadius)+"m；候選範圍受牆阻擋";
            else if(kind==PlayerAbilityProjectileKind.HealingPack)p.Description+="\n拾取 R "+M(p.EffectRadius)+"m；以權威位置計算，非漂浮模型";
            else p.Description+="\n最多反彈 3 次：×1 → ×2 → ×4 → ×8";
            p.Description+="\n"+(physics.HasValue?"目前場景碰撞預估，非未來／權威命中結果":"無場景碰撞的理論路徑");
            Trace(p,physics,ignore,skill.transform,kind,aim*Mathf.Max(.1f,F(data,"projectileSpeed")),
                F(data,"gravity"),radius,I(data,"collisionMask"),seconds,0);
        }
        else if(skill is PlayerPiercingCannonAbility)
        {
            p.Title="貫穿砲";p.Origin=feet+Vector3.up*F(data,"originHeight");
            float distance=F(data,"maximumDistance");
            Vector3 end=p.Origin+aim*distance;
            if(FirstHit(physics,p.Origin,aim,distance,0f,I(data,"hitMask"),ignore,skill.transform,true,out var hit))end=hit.point;
            Arrow(p.Paths,p.Origin,end);
            if(Vector3.Distance(p.Origin,end)+.001f<distance)
                p.Labels.Add(new ActiveAbilityGizmoLabel(end,"目前場景阻擋；敵人可穿透、玩家會阻擋"));
            p.Description=mode+"\n最大射線 "+M(distance)+"m｜零傷害半徑，線寬只是外觀\n"+(physics.HasValue?"目前 PhysX 預估；非 Lag Compensation 歷史命中":"配置上限；未計遮擋");
            p.Labels.Add(new ActiveAbilityGizmoLabel(p.Origin+aim*distance,"設定射程 "+M(distance)+"m"));
        }
        else if(skill is PlayerShockwaveAbility)
        {
            p.Title="衝擊扇波";p.Direction=forward;
            float radius=Mathf.Max(0f,F(data,"radius")),half=Mathf.Clamp(F(data,"halfAngleDegrees"),0f,180f);
            p.EffectRadius=radius;
            Wedge(p,forward,radius,half);
            Vector3 edge=feet+forward*radius;
            Arrow(p.Paths,edge,edge+forward*F(data,"knockbackDistance"));
            p.Labels.Add(new ActiveAbilityGizmoLabel(edge+forward*F(data,"knockbackDistance"),"擊退上限 "+M(F(data,"knockbackDistance"))+"m（方向僅示意）"));
            p.Description=mode+"\n球距 R "+M(radius)+"m ∩ 水平半角 ±"+M(half)+"°（總角 "+M(half*2)+"°）\n命中仍逐目標檢查遮擋；只撞牆才追加傷害／暈眩";
        }
        else if(skill is PlayerShieldAbility)
        {
            p.Title="快速護盾";Mark(p,feet);
            p.Description=mode+"\n僅自身：最大生命 ×"+M(F(data,"maximumHealthFraction")*100f)+"%｜無空間範圍\n持續 "+M(F(data,"levelOneSeconds"))+" + (Lv−1)×"+M(F(data,"secondsPerLevel"))+" 秒";
        }
        else if(skill is PlayerExperienceAbility)
        {
            p.Title="經驗增加";Mark(p,feet);
            p.Description=mode+"\n僅自身經驗 ×2、承傷 ×2｜無空間範圍\n持續 "+M(F(data,"durationSeconds"))+" 秒";
        }
        else if(skill is PlayerBlinkAbility blink)
        {
            p.Title="閃現";float distance=Mathf.Max(0f,F(data,"dashDistance"));
            string directionLabel="無方向輸入時向角色前方；第二次 E 才取樣方向";
            if(live&&blink.IsDashing)
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                forward=(Vector3)typeof(PlayerBlinkAbility).GetProperty("DashDirection",flags).GetValue(blink);
                distance=(float)typeof(PlayerBlinkAbility).GetProperty("RemainingDistance",flags).GetValue(blink);
                directionLabel="衝刺中：固定方向／剩餘行程";
            }
            p.Direction=forward;
            Arrow(p.Paths,feet,feet+forward*distance);
            if(live&&owner.Movement!=null&&owner.Movement.KCC!=null)
            {
                float bodyRadius=owner.Movement.KCC.Settings.Radius,height=owner.Movement.KCC.Settings.Height;
                Capsule(p,feet,bodyRadius,height);Capsule(p,feet+forward*distance,bodyRadius,height);
            }
            p.Description=mode+"\n"+directionLabel+"\n水平行程 "+M(distance)+"m｜待命 "+M(F(data,"primedSeconds"))+"s\n未預算碰撞；遇障由正式 KCC 提前停止";
        }
        else if(skill is PlayerBulletTimeAbility bullet)
        {
            p.Title="子彈時間";p.Origin=live?owner.transform.position:feet;
            bool active=live&&bullet.IsSessionActive;
            if(active)p.Origin=bullet.SnapshotCenter;
            p.EffectRadius=Mathf.Max(.01f,F(data,"effectRadius"));
            Sphere(p,p.Origin,p.EffectRadius,null);
            p.Description=mode+"\n"+(active?"施放快照中心（不跟隨玩家）；目前半徑設定 ":"初始名單取樣球 R ")+M(p.EffectRadius)+"m\n不檢查牆壁；新進不加入、離開不退出\n作用中改半徑不重抓既有名單";
        }
        else if(skill is PlayerPrecisionLockAbility)
        {
            p.Title="精準鎖敵";float radius=Mathf.Max(.1f,F(data,"focusLockDistance"));
            string weapon="未綁定武器：只顯示設定上限";
            bool allowed=!live;
            if(live&&PlayerAbilityQualification.HasRangedWeapon(owner))
            {
                var weapons=owner.ProfessionRuntimeManager.CurrentRuntimeObject.GetComponent<PlayerWeaponController>();
                if(weapons.AttackRifle!=null)
                {
                    radius=Mathf.Min(radius,weapons.AttackRifle.MaxShotDistance);
                    p.Origin=weapons.AttackRifle.GetGameplayShotOrigin();aim=weapons.AttackRifle.GetGameplayAimDirection();
                }
                else
                {
                    radius=Mathf.Min(radius,weapons.SupportSMG.MaxShotDistance);
                    p.Origin=weapons.SupportSMG.GetGameplayShotOrigin();aim=weapons.SupportSMG.GetGameplayAimDirection();
                }
                weapon="已套用當前遠程武器射程上限";allowed=true;
            }
            else if(live)weapon="目前非遠程武器：禁止使用（不顯示可用鎖定範圍）";
            p.Direction=aim.normalized;p.EffectRadius=allowed?radius:0f;
            float angle=F(data,"focusLockAngle");
            if(allowed)
            {
                var rim=SphericalRim(p.Origin,aim,radius,angle,Segments);
                p.RangePaths.Add(rim);
                for(int n=0;n<Segments;n+=Segments/4)p.RangePaths.Add(new[]{p.Origin,rim[n]});
                Arrow(p.Paths,p.Origin,p.Origin+aim.normalized*radius);
            }
            p.Description=mode+"\n"+weapon+"\n三維距離 R "+M(radius)+"m、半角 ±"+M(angle)+"°\n候選搜尋仍需可見且合法；遮擋餘量不是額外射程";
        }
        else { p.Title="未定義技能";p.Description=mode; }
        p.Color=settings.overrideColor?settings.color:SkillColor(key);
        return p;
    }
    public static ActiveAbilityGizmoPreview BuildProjectile(PlayerAbilityProjectile projectile)
    {
        var settings=projectile.RangeGizmos;
        var p=new ActiveAbilityGizmoPreview { Title="技能投射物",Origin=projectile.transform.position,Color=settings.overrideColor?settings.color:new Color(.8f,.8f,.8f) };
        if(!projectile.TryGetGizmoState(out var state))
        {
            p.Description="Prefab／Proxy 預覽：物理範圍由施放 Runtime 注入\n請選擇技能 Runtime 查看設定；未 Spawn 不讀網路狀態";
            Mark(p,p.Origin);return p;
        }
        string key=state.Kind==PlayerAbilityProjectileKind.Grenade?"Grenade":state.Kind==PlayerAbilityProjectileKind.HealingPack?"HealingPack":"Ricochet";
        p.Title=key=="Grenade"?"爆破榴彈｜權威投射物":key=="HealingPack"?"治療包｜權威投射物":"彈射彈｜權威投射物";
        p.Color=settings.overrideColor?settings.color:SkillColor(key);
        p.Origin=state.Position;p.EffectRadius=state.EffectRadius;p.Direction=state.Velocity.normalized;
        p.Description=(state.Impacted?"實際命中效果中心":"實際物理位置｜碰撞球 R "+M(state.CollisionRadius)+"m")+"\n"+
            (state.Impacted?"已命中":state.Resting?"已停留":"剩餘路徑預估")+"；範圍 R "+M(state.EffectRadius)+"m";
        if(state.Kind==PlayerAbilityProjectileKind.Ricochet)p.Description+="\n單體接觸命中；示意線環不是傷害範圍";
        if(state.Resting||state.Impacted)
        {
            if(!state.Impacted)Sphere(p,p.Origin,state.CollisionRadius,null);
            Sphere(p,p.Origin,p.EffectRadius,state.Kind==PlayerAbilityProjectileKind.HealingPack?"實際拾取球（不跟隨漂浮）":"命中候選球；仍受遮擋");
        }
        else Trace(p,settings.previewCollisions?(PhysicsScene?)state.Scene:null,state.Owner,projectile.transform,state.Kind,
            state.Velocity,state.Gravity,state.CollisionRadius,state.CollisionMask,
            Mathf.Min(Mathf.Clamp(settings.trajectorySeconds,.1f,10f),state.RemainingSeconds),state.Bounces);
        return p;
    }
    private static void Wedge(ActiveAbilityGizmoPreview p,Vector3 forward,float radius,float half)
    {
        for(int elevation=-60;elevation<=60;elevation+=30)
        {
            var points=new Vector3[Segments+1];
            float pitch=elevation*Mathf.Deg2Rad;
            for(int n=0;n<=Segments;n++)
            {
                float yaw=Mathf.Lerp(-half,half,(float)n/Segments);
                points[n]=p.Origin+radius*(Quaternion.AngleAxis(yaw,Vector3.up)*forward*Mathf.Cos(pitch)+Vector3.up*Mathf.Sin(pitch));
            }
            p.RangePaths.Add(points);
        }
        foreach(float yaw in new[]{-half,0f,half})
        {
            var points=new Vector3[Segments+1];Vector3 direction=Quaternion.AngleAxis(yaw,Vector3.up)*forward;
            for(int n=0;n<=Segments;n++)
            {
                float angle=Mathf.Lerp(-90f,90f,(float)n/Segments)*Mathf.Deg2Rad;
                points[n]=p.Origin+radius*(direction*Mathf.Cos(angle)+Vector3.up*Mathf.Sin(angle));
            }
            p.RangePaths.Add(points);
            p.RangePaths.Add(new[]{p.Origin,p.Origin+direction*radius});
        }
    }
    private static void Capsule(ActiveAbilityGizmoPreview p,Vector3 feet,float radius,float height)
    {
        radius=Mathf.Max(.01f,radius);height=Mathf.Max(radius*2,height);
        Vector3 low=feet+Vector3.up*radius,high=feet+Vector3.up*(height-radius);
        // 膠囊端部線圈與側緣只是角色體積；不冒充縮小 skin 後的精確掃掠。
        p.RangePaths.Add(SphericalRim(low,Vector3.up,radius,90f,Segments));
        p.RangePaths.Add(SphericalRim(high,Vector3.up,radius,90f,Segments));
        foreach(var side in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
        {
            p.RangePaths.Add(new[]{low+side*radius,high+side*radius});
            var arc=new Vector3[17];
            for(int n=0;n<17;n++){float a=n*Mathf.PI/16;arc[n]=high+radius*(side*Mathf.Cos(a)+Vector3.up*Mathf.Sin(a));}
            p.RangePaths.Add((Vector3[])arc.Clone());
            for(int n=0;n<17;n++){float a=n*Mathf.PI/16;arc[n]=low+radius*(side*Mathf.Cos(a)-Vector3.up*Mathf.Sin(a));}
            p.RangePaths.Add((Vector3[])arc.Clone());
        }
    }
    private static bool Ignore(Collider c,Transform owner,Transform source) => c==null||
        (owner!=null&&(c.transform==owner||c.transform.IsChildOf(owner)))||
        (source!=null&&(c.transform==source||c.transform.IsChildOf(source)));
    private static bool FirstHit(PhysicsScene? scene,Vector3 origin,Vector3 direction,float distance,float radius,
        int mask,Transform ignore,Transform source,bool piercing,out RaycastHit hit)
    {
        hit=default;if((!scene.HasValue||!scene.Value.IsValid())||distance<=.000001f)return false;
        var hits=new RaycastHit[16];int count;
        do
        {
            count=radius>0?scene.Value.SphereCast(origin,radius,direction,hits,distance,mask,QueryTriggerInteraction.Ignore):
                scene.Value.Raycast(origin,direction,hits,distance,mask,QueryTriggerInteraction.Ignore);
            if(count<hits.Length)break;System.Array.Resize(ref hits,hits.Length*2);
        }while(true);
        float nearest=float.PositiveInfinity;
        for(int n=0;n<count;n++)
        {
            var c=hits[n].collider;if(Ignore(c,ignore,source))continue;
            var receiver=ActiveAbilityPhysics.ResolveReceiver(c.gameObject);
            if(piercing&&receiver!=null&&!(receiver is PlayerHealth))continue;
            if(hits[n].distance<nearest){nearest=hits[n].distance;hit=hits[n];}
        }
        return !float.IsPositiveInfinity(nearest);
    }
    private static bool InitialOverlap(PhysicsScene? scene,Vector3 origin,float radius,int mask,Transform owner,Transform source)
    {
        if((!scene.HasValue||!scene.Value.IsValid()))return false;
        var colliders=new Collider[16];int count;
        do
        {
            count=scene.Value.OverlapSphere(origin,radius*.99f,colliders,mask,QueryTriggerInteraction.Ignore);
            if(count<colliders.Length)break;System.Array.Resize(ref colliders,colliders.Length*2);
        }while(true);
        for(int n=0;n<count;n++)if(!Ignore(colliders[n],owner,source))return true;
        return false;
    }
    private static bool OverlapsOwner(PhysicsScene? scene,Vector3 position,float radius,int mask,Transform owner)
    {
        if(owner==null||(!scene.HasValue||!scene.Value.IsValid()))return false;
        var colliders=new Collider[16];int count;
        do
        {
            count=scene.Value.OverlapSphere(position,radius,colliders,mask,QueryTriggerInteraction.Ignore);
            if(count<colliders.Length)break;
            System.Array.Resize(ref colliders,colliders.Length*2);
        }while(true);
        for(int i=0;i<count;i++)
            if(colliders[i]!=null&&(colliders[i].transform==owner||colliders[i].transform.IsChildOf(owner)))return true;
        return false;
    }

    private static void Trace(ActiveAbilityGizmoPreview p,PhysicsScene? scene,Transform ignore,Transform source,
        PlayerAbilityProjectileKind kind,Vector3 velocity,float gravity,float radius,int mask,float seconds,int bounces)
    {
        Vector3 position=p.Origin;var points=new List<Vector3>{position};
        Sphere(p,position,radius,null);
        if(InitialOverlap(scene,position,radius,mask,ignore,source))
        {
            Sphere(p,position,p.EffectRadius,"半徑參考；起點重疊，停止路徑預覽");
            p.Labels.Add(new ActiveAbilityGizmoLabel(position,"起點重疊（結果依正式碰撞／受擊判定）"));return;
        }
        bool contact=false;Vector3 effectCenter=position;
        Vector3 acceleration=kind==PlayerAbilityProjectileKind.Ricochet?Vector3.zero:Vector3.down*Mathf.Max(0,gravity);
        const float step=.04f;int steps=Mathf.Min(250,Mathf.CeilToInt(Mathf.Max(0f,seconds)/step));
        float remaining=seconds;
        float clearanceRadius=kind==PlayerAbilityProjectileKind.HealingPack?p.EffectRadius:radius;
        for(int n=0;n<steps&&!contact;n++)
        {
            float dt=Mathf.Min(step,remaining);remaining-=dt;
            Vector3 delta=velocity*dt+acceleration*(dt*dt*.5f);velocity+=acceleration*dt;
            float distance=delta.magnitude;
            Vector3 direction=distance>.000001f?delta/distance:Vector3.zero;
            // 同一時間步內完成反彈剩餘行程，最多三次反彈及一次終止碰撞。
            while(distance>.000001f&&!contact)
            {
                if(ignore!=null&&!OverlapsOwner(scene,position,clearanceRadius,mask,ignore))ignore=null;
                if(!FirstHit(scene,position,direction,distance,radius,mask,ignore,source,false,out var hit))
                {
                    position+=direction*distance;points.Add(position);break;
                }
                float travelled=Mathf.Clamp(hit.distance,0f,distance);
                position+=direction*travelled;distance-=travelled;points.Add(position);
                effectCenter=kind==PlayerAbilityProjectileKind.Grenade?hit.point+hit.normal*.015f:position;contact=true;
                if(kind!=PlayerAbilityProjectileKind.Ricochet)break;
                bool damageable=ActiveAbilityPhysics.ResolveReceiver(hit.collider.gameObject)!=null;
                if(damageable||bounces>=3)
                {
                    p.Labels.Add(new ActiveAbilityGizmoLabel(position,damageable?"碰到可受擊物件：結束預估，是否消失依實際傷害":"第 4 次碰撞：銷毀"));
                    break;
                }
                bounces++;
                p.Paths.Add(points.ToArray());points.Clear();points.Add(position);
                p.Labels.Add(new ActiveAbilityGizmoLabel(position,"第 "+bounces+" 次反彈｜×"+(1<<bounces)));
                velocity=Vector3.Reflect(velocity,hit.normal);direction=velocity.normalized;
                position+=hit.normal*.002f;contact=false;
            }
        }
        if(points.Count>1)p.Paths.Add(points.ToArray());
        if(kind!=PlayerAbilityProjectileKind.Ricochet)
            Sphere(p,contact?effectCenter:p.Origin,p.EffectRadius,contact?
                (kind==PlayerAbilityProjectileKind.Grenade?"預估接觸點｜爆炸候選球（牆後仍需遮擋判定）":"預估停留點｜拾取球（拾取可能更早發生）"):
                null);
        if(!contact&&kind!=PlayerAbilityProjectileKind.Ricochet)p.Description+="\n起點球為半徑參考；路徑截斷不是爆炸／落地";
        if(!contact)p.Labels.Add(new ActiveAbilityGizmoLabel(position,"預覽時間上限 "+M(seconds)+"s"));
    }
}
