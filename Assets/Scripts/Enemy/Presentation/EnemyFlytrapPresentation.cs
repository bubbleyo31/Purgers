using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>僅讀同步狀態的動畫接口。無 Animator 也能執行 Gameplay；事件不得反向決定傷害。</summary>
[DisallowMultipleComponent]
public sealed class EnemyFlytrapPresentation : MonoBehaviour
{
    [Header("動畫接口（可留空）")]
    [SerializeField, Tooltip("只用於顯示，不啟用 Root Motion。參數：FlytrapPhase(int)、PullProgress(float)、FeedingProgress(float)、Bite/Release/Hurt(trigger)。")]
    private Animator animator;
    [SerializeField, Tooltip("方塊佔位 Renderer；依待機／拉動／吞食／冷卻／死亡換色，正式模型可留空。")]
    private Renderer placeholder;
    [Header("本機動畫／音效事件")]
    [SerializeField, Tooltip("狀態改變：0 待機、1 拉入、2 吞食、3 冷卻、4 死亡、5 追蹤、6 固定方向、7 刺出、8 收招；初次可見只套用當前狀態。")]
    private UnityEvent<int> onPhaseChanged = new UnityEvent<int>();
    [SerializeField, Tooltip("每次同步扣血序號變化，播放一次咬合表現；不負責實際扣血。")]
    private UnityEvent onBite = new UnityEvent();
    [SerializeField, Tooltip("解救或正常釋放時的表現接口；晚加入不重播舊事件。")]
    private UnityEvent onRelease = new UnityEvent();
    [SerializeField, Tooltip("受擊表現接口，建議局部疊加，不中斷抓取或吞食。")]
    private UnityEvent onHurt = new UnityEvent();
    [Header("藤蔓方塊表現")]
    [SerializeField, Tooltip("產生無碰撞的方塊藤蔓。蓄勢顯示方向與伸縮，刺出／拉入／收回依同步位置顯示；正式模型可關閉。")]
    private bool showPlaceholderVine = true;
    private Transform vineVisual;
    private Renderer vineRenderer;
    private EnemyFlytrapAttack attack;
    private TestDamageReceiver health;
    private MaterialPropertyBlock properties;
    private int phase = -1, bite, release;
    private bool initialized;
    private float lastHealth;
    private static readonly Color[] Colors = {new Color(.25f,.55f,.15f), new Color(1f,.65f,.1f), new Color(.8f,.15f,.12f), new Color(.3f,.3f,.3f), Color.black, new Color(.85f,.8f,.15f), new Color(1f,.35f,.05f), new Color(.75f,.25f,.85f), new Color(.5f,.45f,.2f)};
    private void Awake() { attack=GetComponent<EnemyFlytrapAttack>(); health=GetComponent<TestDamageReceiver>(); properties=new MaterialPropertyBlock(); }

    private void OnDisable() { initialized=false; if(vineVisual!=null) vineVisual.gameObject.SetActive(false); }

    private void Update()
    {
        if(attack==null || !attack.Ready) { if(vineVisual!=null) vineVisual.gameObject.SetActive(false); return; }
        int current=(int)attack.Phase;
        if(!initialized) { bite=attack.BiteSequence; release=attack.ReleaseSequence; phase=-1; initialized=true; lastHealth=health.CurrentHealth; }
        if(health.CurrentHealth < lastHealth) { Trigger("Hurt"); onHurt.Invoke(); }
        lastHealth=health.CurrentHealth;
        if(current!=phase)
        {
            phase=current; onPhaseChanged.Invoke(current);
            if(placeholder!=null) { placeholder.GetPropertyBlock(properties); properties.SetColor("_BaseColor",Colors[current]); properties.SetColor("_Color",Colors[current]); placeholder.SetPropertyBlock(properties); }
        }
        UpdateVine();
        Set("StrikeProgress",AnimatorControllerParameterType.Float,attack.StrikeProgress);
        Set("FlytrapPhase",AnimatorControllerParameterType.Int,current);
        Set("PullProgress",AnimatorControllerParameterType.Float,attack.PullProgress);
        Set("FeedingProgress",AnimatorControllerParameterType.Float,attack.FeedingProgress);
        if(bite!=attack.BiteSequence) { bite=attack.BiteSequence; Trigger("Bite"); onBite.Invoke(); }
        if(release!=attack.ReleaseSequence) { release=attack.ReleaseSequence; Trigger("Release"); onRelease.Invoke(); }
    }
    private void UpdateVine()
    {
        var state = attack.Phase;
        bool show = showPlaceholderVine && (state == EnemyFlytrapPhase.Tracking || state == EnemyFlytrapPhase.Locked ||
            state == EnemyFlytrapPhase.Striking || state == EnemyFlytrapPhase.Recovering || state == EnemyFlytrapPhase.Pulling);
        if (!show) { if (vineVisual != null) vineVisual.gameObject.SetActive(false); return; }
        if (vineVisual == null)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = "FlytrapVine_LocalVisual"; obj.layer = gameObject.layer;
            var collider = obj.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            vineVisual = obj.transform; vineVisual.SetParent(transform, false);
            var renderer = obj.GetComponent<Renderer>(); vineRenderer = renderer;
            if (placeholder != null) renderer.sharedMaterial = placeholder.sharedMaterial;
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor",new Color(.22f,.48f,.08f)); block.SetColor("_Color",new Color(.22f,.48f,.08f));
            renderer.SetPropertyBlock(block);
        }
        Vector3 from = attack.VineOrigin, to = attack.VineTip;
        if (state == EnemyFlytrapPhase.Tracking || state == EnemyFlytrapPhase.Locked)
        {
            // 大型人工方塊可能包住掛點；蓄勢提示從可見表面開始，不修改 Gameplay 起點。
            if (placeholder != null && placeholder.bounds.Contains(from))
            {
                float far = placeholder.bounds.size.magnitude + 1f;
                if (placeholder.bounds.IntersectRay(new Ray(from + attack.StrikeDirection * far, -attack.StrikeDirection), out float entry))
                    from += attack.StrikeDirection * Mathf.Max(0f, far - entry + .03f);
            }
            float length = state == EnemyFlytrapPhase.Locked ? .7f : Mathf.Lerp(.25f,.7f,attack.StrikeProgress);
            to = from + attack.StrikeDirection * length;
        }
        if (state == EnemyFlytrapPhase.Pulling && attack.CapturedPlayer != null)
        {
            var player = attack.CapturedPlayer.GetComponent<Player>();
            if (player != null && player.Movement != null)
                to = player.transform.position + Vector3.up * player.Movement.KCC.Settings.Height * .5f;
        }
        Vector3 delta = to - from;
        vineVisual.gameObject.SetActive(delta.sqrMagnitude > .000001f);
        if (delta.sqrMagnitude <= .000001f) return;
        vineVisual.position = (from + to) * .5f;
        vineVisual.rotation = Quaternion.LookRotation(delta.normalized);
        Vector3 parentScale = transform.lossyScale;
        vineVisual.localScale = new Vector3(attack.VineRadius * 2f / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
            attack.VineRadius * 2f / Mathf.Max(.001f, Mathf.Abs(parentScale.y)), delta.magnitude / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
        // 提示使用獨立 Renderer，避免與敵人既有受擊閃白材質控制互相覆寫。
        vineRenderer.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", Colors[(int)state]); properties.SetColor("_Color", Colors[(int)state]);
        vineRenderer.SetPropertyBlock(properties);
    }
    private void Set(string name,AnimatorControllerParameterType type,float value)
    {
        if(animator==null || animator.runtimeAnimatorController==null) return;
        foreach(var p in animator.parameters)
            if(p.name==name && p.type==type) { if(type==AnimatorControllerParameterType.Int) animator.SetInteger(name,(int)value); else animator.SetFloat(name,value); return; }
    }
    private void Trigger(string name)
    {
        if(animator==null || animator.runtimeAnimatorController==null) return;
        foreach(var p in animator.parameters) if(p.name==name && p.type==AnimatorControllerParameterType.Trigger) {animator.SetTrigger(name);return;}
    }
}
