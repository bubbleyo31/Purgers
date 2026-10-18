using Fusion;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Assert = NUnit.Framework.Assert;

[Category("PurgersRegression")]
public sealed class EnemyJellyfishCollisionPoseTests
{
    [Test]
    public void FittedCapsuleContainsSamplesAtRoundedEnds()
    {
        var points = new[] {new Vector3(1,3,0),new Vector3(-1,-2,0),new Vector3(0,5,.2f)};
        EnemyJellyfishCollisionPose.Fit(points,true,.04f,out var center,out _,out float radius,out float height);
        foreach(var point in points) {
            Vector3 delta=point-center;
            delta.y-=Mathf.Clamp(delta.y,-(height/2-radius),height/2-radius);
            Assert.That(delta.magnitude,Is.LessThanOrEqualTo(radius+.0001f));
        }
    }
    [TestCase(true)]
    [TestCase(false)]
    public void WeightedPoseKeepsPhysXAndFusionTogetherWithoutEditingBones(bool capsule)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("PoseFixture");root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        try {
            var a=new GameObject("BoneA").transform;a.SetParent(root.transform,false);
            var b=new GameObject("BoneB").transform;b.SetParent(root.transform,false);
            var node=new GameObject("HitPart");node.transform.SetParent(a,false);
            var hitbox=node.AddComponent<Hitbox>();
            Collider collider=capsule?(Collider)node.AddComponent<CapsuleCollider>():node.AddComponent<BoxCollider>();
            hitbox.Type=capsule?HitboxTypes.Capsule:HitboxTypes.Box;
            var samples=new EnemyJellyfishCollisionPose.Sample[4];
            var vertices=new[]{new Vector3(-.2f,-1,-.1f),new Vector3(.2f,1,.1f),new Vector3(.1f,0,-.2f),new Vector3(-.1f,0,.2f)};
            for(int i=0;i<samples.Length;i++)samples[i]=new EnemyJellyfishCollisionPose.Sample {
                Bone0=0,Bone1=1,Weights=new Vector4(.5f,.5f,0,0),Point0=vertices[i],Point1=vertices[i]
            };
            var pose=root.AddComponent<EnemyJellyfishCollisionPose>();
            pose.Configure(new[]{a,b},new[]{new EnemyJellyfishCollisionPose.Part {
                Anchor=a,Hitbox=hitbox,Collider=collider,Samples=samples,Padding=.04f
            }});
            var previous=node.transform.position;
            b.localPosition=Vector3.right*4;
            collider.enabled=false;
            pose.RefreshPose();
            Assert.That(Vector3.Distance(node.transform.position,previous+Vector3.right*2),Is.LessThan(.0001f));
            Assert.That(a.localPosition,Is.EqualTo(Vector3.zero));
            Assert.That(b.localPosition,Is.EqualTo(Vector3.right*4));
            Assert.That(collider.enabled,Is.False);
            if(capsule) {
                Assert.That(hitbox.CapsuleRadius,Is.EqualTo(((CapsuleCollider)collider).radius));
                Assert.That(hitbox.CapsuleExtents,Is.EqualTo(((CapsuleCollider)collider).height));
            } else Assert.That(hitbox.BoxExtents*2,Is.EqualTo(((BoxCollider)collider).size));
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}