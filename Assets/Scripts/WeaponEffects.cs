using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Эффекты выстрела: вспышка и свет у дула, дым, трассер, вылетающая гильза,
/// искры/пыль и след от пули в месте попадания, звук выстрела.
/// Материалы создаются сборщиком сцены (Assets/Effects), частицы — в коде.
/// </summary>
public class WeaponEffects : MonoBehaviour
{
    [Header("Точки")]
    public Transform muzzle;        // forward — направление ствола
    public Transform ejectionPort;  // окно выброса гильз (справа на затворе)

    [Header("Материалы")]
    public Material flashMaterial;  // аддитивный, звезда вспышки
        public Material sparkMaterial;  // аддитивная точка (искры, трассер)
    public Material decalMaterial;  // след от пули
    public Material casingMaterial; // латунь

    [Header("Настройки")]
    public float damageForce = 6f;
    public int maxDecals = 60;
    [Tooltip("Ближе этого расстояния следы от пуль не создаются: вплотную они выглядят как серое облако перед камерой.")]
    public float minDecalDistance = 2.5f;
    [Tooltip("Выбрасывать гильзу в момент выстрела. У помпы — выключено: гильзу выбрасывает передёргивание цевья (EjectCasing).")]
    public bool ejectOnFire = true;
    [Tooltip("Размер гильзы (масштаб цилиндра-примитива высотой 2 и диаметром 1).")]
    public Vector3 casingScale = new Vector3(0.0095f, 0.0095f, 0.0095f); // 9×19 мм
    [Tooltip("Число дробин (лучей) за выстрел.")]
    public int pellets = 1;
    [Tooltip("Разброс дроби (градусы от оси).")]
    public float spreadAngle = 0f;

    ParticleSystem sparksPS;
    LineRenderer tracer;
    AudioSource audioSource;
    float tracerTimer;
    readonly Queue<GameObject> decals = new Queue<GameObject>();
    static AudioClip shotClip;
    static Mesh quadMesh;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;

        sparksPS = CreateSystem("ImpactSparks", null, sparkMaterial, ParticleSystemSimulationSpace.World);
        var spm = sparksPS.main;
        spm.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        spm.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
        spm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 5f);
        spm.startColor = new Color(1f, 0.75f, 0.35f, 1f);
        spm.gravityModifier = 1.2f;
        var spShape = sparksPS.shape; spShape.enabled = true; spShape.shapeType = ParticleSystemShapeType.Cone; spShape.angle = 40f; spShape.radius = 0.01f;
        var spCol = sparksPS.collision; spCol.enabled = true; spCol.type = ParticleSystemCollisionType.World; spCol.dampen = 0.5f; spCol.bounce = 0.4f; spCol.quality = ParticleSystemCollisionQuality.Low;
        var spr = sparksPS.GetComponent<ParticleSystemRenderer>();
        spr.renderMode = ParticleSystemRenderMode.Stretch; spr.velocityScale = 0.03f; spr.lengthScale = 1f;
        SetFade(sparksPS, 1f);

        var tracerGo = new GameObject("Tracer");
        tracer = tracerGo.AddComponent<LineRenderer>();
        tracer.sharedMaterial = sparkMaterial;
        tracer.positionCount = 2;
        tracer.useWorldSpace = true;
        tracer.widthMultiplier = 0.012f;
        tracer.startColor = new Color(1f, 0.8f, 0.5f, 0.0f);
        tracer.endColor = new Color(1f, 0.85f, 0.6f, 0.9f);
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.enabled = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.3f;
        if (shotClip == null) shotClip = CreateShotClip();
        if (quadMesh == null) quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
    }

    ParticleSystem CreateSystem(string name, Transform parent, Material mat, ParticleSystemSimulationSpace space)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.simulationSpace = space;
        main.maxParticles = 200;
        main.startSpeed = 0f;
        var emission = ps.emission; emission.enabled = false; // частицы выпускаются вручную через Emit
        var shape = ps.shape; shape.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static void SetFade(ParticleSystem ps, float startAlpha)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(startAlpha * 0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    /// <summary>Выстрел: эффекты у оружия + луч из камеры.</summary>
    public void Fire(Vector3 rayOrigin, Vector3 rayDir, Vector3 shooterVelocity, Collider ignore)
    {
        // Дуло.
        audioSource.pitch = Random.Range(0.92f, 1.08f);
        audioSource.PlayOneShot(shotClip, 0.9f);
        if (ejectOnFire) EjectCasing(shooterVelocity, ignore);

        // Попадание. Трассер рисуется по первой дробине (у пули — по единственному лучу).
        Vector3 end = rayOrigin + rayDir * 150f;
        for (int i = 0; i < Mathf.Max(1, pellets); i++)
        {
            Vector3 dir = rayDir;
            if (spreadAngle > 0f)
            {
                Vector2 c = Random.insideUnitCircle * Mathf.Tan(spreadAngle * Mathf.Deg2Rad);
                Vector3 side = Vector3.Cross(rayDir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(rayDir, Vector3.up).normalized : Vector3.right;
                Vector3 up = Vector3.Cross(side, rayDir);
                dir = (rayDir + side * c.x + up * c.y).normalized;
            }
            if (Physics.Raycast(rayOrigin, dir, out RaycastHit hit, 150f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (i == 0) end = hit.point;
                Impact(hit, dir);
            }
            else if (i == 0) end = rayOrigin + dir * 150f;
        }

        tracer.SetPosition(0, muzzle.position + muzzle.forward * 0.03f);
        tracer.SetPosition(1, end);
        tracer.enabled = true;
        tracerTimer = 0.035f;
    }

    void Impact(RaycastHit hit, Vector3 dir)
    {
        Quaternion outward = Quaternion.LookRotation(Vector3.Reflect(dir, hit.normal) * 0.5f + hit.normal);
        sparksPS.transform.SetPositionAndRotation(hit.point + hit.normal * 0.01f, outward);
        EmitSparks(hit.distance);

        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(dir * damageForce, hit.point, ForceMode.Impulse);

        if (hit.distance >= minDecalDistance) SpawnDecal(hit);
    }

    /// <summary>
    /// Искры у места попадания. Чем ближе предмет к камере, тем они меньше, медленнее и короче живут,
    /// чтобы не лететь в камеру; вплотную искр нет совсем.
    /// </summary>
    void EmitSparks(float distance)
    {
        if (distance < 0.4f) return;
        float f = Mathf.Clamp01(distance / 2.5f); // 1 — обычные искры, меньше — уменьшенные
        var m = sparksPS.main;
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * f, 5f * f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.008f * f, 0.018f * f);
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f * f, 0.4f * f);
        sparksPS.Emit(Mathf.Max(2, Mathf.RoundToInt(Random.Range(8, 14) * f)));
    }

    void SpawnDecal(RaycastHit hit)
    {
        if (decalMaterial == null || quadMesh == null) return;
        var go = new GameObject("BulletHole");
        go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.002f,
            Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        float size = Random.Range(0.045f, 0.065f);
        go.transform.localScale = new Vector3(size, size, size);
        go.transform.SetParent(hit.collider.transform, true);
        go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = decalMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        decals.Enqueue(go);
        while (decals.Count > maxDecals)
        {
            var old = decals.Dequeue();
            if (old != null) Destroy(old);
        }
    }

    public void EjectCasing(Vector3 shooterVelocity, Collider ignore)
    {
        Transform port = ejectionPort != null ? ejectionPort : muzzle;
        var casing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        casing.name = "Casing";
        casing.transform.SetPositionAndRotation(port.position, port.rotation * Quaternion.Euler(90f, 0f, 0f));
        casing.transform.localScale = casingScale;
        if (casingMaterial != null) casing.GetComponent<Renderer>().sharedMaterial = casingMaterial;
        casing.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var col = casing.GetComponent<Collider>();
        if (ignore != null) Physics.IgnoreCollision(col, ignore);

        var rb = casing.AddComponent<Rigidbody>();
        rb.mass = 0.012f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.linearVelocity = shooterVelocity + port.right * Random.Range(1.6f, 2.4f) + port.up * Random.Range(1.2f, 1.8f) - port.forward * Random.Range(0.1f, 0.5f);
        rb.angularVelocity = Random.insideUnitSphere * 25f;
        Destroy(casing, 6f);
    }

    void Update()
    {
        if (tracerTimer > 0f)
        {
            tracerTimer -= Time.deltaTime;
            if (tracerTimer <= 0f) tracer.enabled = false;
        }
    }

    /// <summary>Процедурный звук выстрела: резкий хлопок + низкий «удар» + затухающий хвост.</summary>
    static AudioClip CreateShotClip()
    {
        const int rate = 44100;
        int length = (int)(rate * 0.6f);
        var data = new float[length];
        var rng = new System.Random(12345);
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.35f;      // приглушённый шум
            lp2 += (noise - lp2) * 0.04f;    // глухой хвост (эхо помещения)
            float crack = noise * Mathf.Exp(-t * 90f);
            float body = lp * Mathf.Exp(-t * 22f);
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t * (1f - t)) * Mathf.Exp(-t * 18f);
            float tail = lp2 * 2.5f * Mathf.Exp(-t * 6f);
            float attack = Mathf.Clamp01(t / 0.0015f);
            data[i] = Mathf.Clamp((crack * 0.7f + body * 0.9f + thump * 0.8f + tail * 0.5f) * attack, -1f, 1f);
        }
        var clip = AudioClip.Create("PistolShot", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
