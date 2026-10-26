using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Временная проверка: капсула игрока спускается и поднимается по лестнице из кухни,
/// скорости пишутся в Temp/StairSpeedTest.txt. Запускается после перекомпиляции и через Tools → Canteen → Stair Speed Test.
/// </summary>
[InitializeOnLoad]
public static class StairSpeedTest
{
    static StairSpeedTest()
    {
        EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Run(); };
    }

    [MenuItem("Tools/Canteen/Stair Speed Test")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var stairs = GameObject.Find("Entrance3_StairTunnel_Down");
        if (stairs == null) { File.WriteAllText("Temp/StairSpeedTest.txt", "no stairs in open scene"); return; }
        var player = Object.FindFirstObjectByType<CharacterController>();
        sb.AppendLine("ramp: " + (stairs.transform.Find("Stair_Ramp") != null) + "  player cc: " + (player != null));

        var go = new GameObject("StairTest") { hideFlags = HideFlags.HideAndDontSave };
        var cc = go.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.03f;
        if (player != null) { cc.height = player.height; cc.radius = player.radius; cc.center = player.center; cc.stepOffset = player.stepOffset; cc.slopeLimit = player.slopeLimit; cc.skinWidth = player.skinWidth; }
        var fpc = go.AddComponent<FirstPersonController>();
        if (player != null && player.TryGetComponent(out FirstPersonController src)) { fpc.walkSpeed = src.walkSpeed; fpc.groundSnap = src.groundSnap; }
        try
        {
            var t = stairs.transform;
            go.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(0f, 4.45f, -6.6f)), Quaternion.LookRotation(t.forward));
            if (player != null) Physics.IgnoreCollision(cc, player);
            Physics.SyncTransforms();
            Simulate(sb, "DOWN", fpc, cc, t);
            go.transform.rotation = Quaternion.LookRotation(-t.forward);
            Physics.SyncTransforms();
            Simulate(sb, "UP", fpc, cc, t);
        }
        catch (System.Exception e) { sb.AppendLine(e.ToString()); }
        Object.DestroyImmediate(go);
        File.WriteAllText("Temp/StairSpeedTest.txt", sb.ToString());
    }

    static void Simulate(StringBuilder sb, string label, FirstPersonController fpc, CharacterController cc, Transform stairs)
    {
        const float dt = 1f / 60f;
        sb.AppendLine("== " + label);
        for (int i = 0; i < 150; i++)
        {
            Vector3 before = fpc.transform.position;
            fpc.Locomotion(new Vector2(0f, 1f), false, false, dt);
            Physics.SyncTransforms();
            if (i % 6 == 0)
            {
                Vector3 l = stairs.InverseTransformPoint(fpc.transform.position);
                Vector3 d = (fpc.transform.position - before) / dt;
                sb.AppendLine($"t={i * dt:F2} local y={l.y:F2} z={l.z:F2} hSpeed={fpc.HorizontalSpeed:F2} vy={d.y:F2} grounded={cc.isGrounded} flags={cc.collisionFlags}");
            }
        }
    }
}
