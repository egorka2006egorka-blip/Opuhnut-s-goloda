using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Эффекты выстрела: вспышка, пламя и свет у дула, пороховой дым (и дымок из перегретого ствола),
/// трассер, вылетающая гильза, на месте попадания — искры по металлу, пыль и осколки по остальному,
/// след от пули и звук попадания; звук выстрела своего типа для каждого оружия, тряска камеры.
/// Материалы создаются сборщиком сцены (Assets/Effects), частицы и звуки — в коде.
/// </summary>
public class WeaponEffects : MonoBehaviour
{
    public enum Profile { Pistol, Rifle, SMG, Shotgun }

    [Header("Точки")]
    public Transform muzzle;        // forward — направление ствола
    public Transform ejectionPort;  // окно выброса гильз (справа на затворе)

    [Header("Материалы")]
    public Material flashMaterial;  // аддитивный, звезда вспышки
    public Material sparkMaterial;  // аддитивная точка (искры, трассер)
    public Material decalMaterial;  // след от пули
    public Material casingMaterial; // латунь
    public Material smokeMaterial;  // мягкое облачко (дым, пыль)
    public Material debrisMaterial; // осколки (освещаемые, цвет — от поверхности)

    [Header("Тип оружия")]
    [Tooltip("Определяет звук выстрела.")]
    public Profile profile = Profile.Pistol;
    [Tooltip("Размер вспышки у дула.")]
    public float flashScale = 1f;
    [Tooltip("Сколько клубов порохового дыма за выстрел.")]
    public int smokePerShot = 2;
    [Tooltip("Тряска камеры при выстреле (градусы).")]
    public float cameraShake = 0.35f;

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

    ParticleSystem sparksPS, flashPS, flamePS, smokePS, dustPS, debrisPS;
    Light muzzleLight;
    LineRenderer tracer;
    AudioSource audioSource;
    float tracerTimer, lightTimer, heat, wispAccum;
    readonly Queue<GameObject> decals = new Queue<GameObject>();
    static readonly Dictionary<int, AudioClip> shotClips = new Dictionary<int, AudioClip>();
    static AudioClip[] impactClips;
    static Mesh quadMesh, cubeMesh;

    const float LightTime = 0.06f;
    const float LightIntensity = 3.5f;

    void Awake()
    {
        if (muzzle == null) muzzle = transform;

        // ---------- Искры при попадании в металл ----------
        sparksPS = CreateSystem("ImpactSparks", null, sparkMaterial, ParticleSystemSimulationSpace.World);
        var spm = sparksPS.main;
        spm.startColor = new Color(1f, 0.75f, 0.35f, 1f);
        spm.gravityModifier = 1.2f;
        var spShape = sparksPS.shape; spShape.enabled = true; spShape.shapeType = ParticleSystemShapeType.Cone; spShape.angle = 40f; spShape.radius = 0.01f;
        var spCol = sparksPS.collision; spCol.enabled = true; spCol.type = ParticleSystemCollisionType.World; spCol.dampen = 0.5f; spCol.bounce = 0.4f; spCol.quality = ParticleSystemCollisionQuality.Low;
        var spr = sparksPS.GetComponent<ParticleSystemRenderer>();
        spr.renderMode = ParticleSystemRenderMode.Stretch; spr.velocityScale = 0.03f; spr.lengthScale = 1f;
        SetFade(sparksPS, 1f);

        // ---------- Вспышка у дула: звезда лицом к камере + языки пламени вперёд по стволу ----------
        flashPS = CreateSystem("MuzzleFlash", muzzle, flashMaterial, ParticleSystemSimulationSpace.Local);
        var fm = flashPS.main;
        fm.startLifetime = 0.045f;
        fm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        fm.startColor = new Color(1f, 0.85f, 0.6f, 1f);
        SetFlashFade(flashPS);

        flamePS = CreateSystem("MuzzleFlame", muzzle, flashMaterial, ParticleSystemSimulationSpace.Local);
        var flm = flamePS.main;
        flm.startLifetime = new ParticleSystem.MinMaxCurve(0.025f, 0.045f);
        flm.startColor = new Color(1f, 0.7f, 0.35f, 1f);
        var flShape = flamePS.shape; flShape.enabled = true; flShape.shapeType = ParticleSystemShapeType.Cone; flShape.angle = 9f; flShape.radius = 0.004f;
        var flr = flamePS.GetComponent<ParticleSystemRenderer>();
        flr.renderMode = ParticleSystemRenderMode.Stretch; flr.velocityScale = 0.018f; flr.lengthScale = 1.2f;
        SetFlashFade(flamePS);

        var lightGo = new GameObject("MuzzleLight");
        lightGo.transform.SetParent(muzzle, false);
        lightGo.transform.localPosition = new Vector3(0f, 0f, 0.06f);
        muzzleLight = lightGo.AddComponent<Light>();
        muzzleLight.type = LightType.Point;
        muzzleLight.range = 6f;
        muzzleLight.color = new Color(1f, 0.72f, 0.38f);
        muzzleLight.shadows = LightShadows.None;
        muzzleLight.intensity = 0f;
        muzzleLight.enabled = false;

        // ---------- Пороховой дым: выходит из ствола, тормозит, расплывается и поднимается ----------
        smokePS = CreateSystem("GunSmoke", null, smokeMaterial, ParticleSystemSimulationSpace.World);
        SetupSmoke(smokePS, 4f, 0.12f);
        var smShape = smokePS.shape; smShape.enabled = true; smShape.shapeType = ParticleSystemShapeType.Cone; smShape.angle = 12f; smShape.radius = 0.005f;

        // ---------- Пыль и осколки при попадании ----------
        dustPS = CreateSystem("ImpactDust", null, smokeMaterial, ParticleSystemSimulationSpace.World);
        SetupSmoke(dustPS, 3f, 0.04f);
        var duShape = dustPS.shape; duShape.enabled = true; duShape.shapeType = ParticleSystemShapeType.Cone; duShape.angle = 35f; duShape.radius = 0.01f;

        debrisPS = CreateSystem("ImpactDebris", null, debrisMaterial, ParticleSystemSimulationSpace.World);
        var dbm = debrisPS.main;
        dbm.gravityModifier = 1f;
        dbm.startRotation3D = true;
        dbm.startRotationX = dbm.startRotationY = dbm.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var dbShape = debrisPS.shape; dbShape.enabled = true; dbShape.shapeType = ParticleSystemShapeType.Cone; dbShape.angle = 45f; dbShape.radius = 0.01f;
        var dbCol = debrisPS.collision; dbCol.enabled = true; dbCol.type = ParticleSystemCollisionType.World; dbCol.dampen = 0.4f; dbCol.bounce = 0.3f; dbCol.quality = ParticleSystemCollisionQuality.Low;
        var dbRot = debrisPS.rotationOverLifetime; dbRot.enabled = true; dbRot.separateAxes = true;
        dbRot.x = dbRot.y = dbRot.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
        if (cubeMesh == null) cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        var dbr = debrisPS.GetComponent<ParticleSystemRenderer>();
        dbr.renderMode = ParticleSystemRenderMode.Mesh;
        dbr.mesh = cubeMesh;
        dbr.alignment = ParticleSystemRenderSpace.World;
        var dbFade = debrisPS.colorOverLifetime; dbFade.enabled = true;
        var dg = new Gradient();
        dg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        dbFade.color = dg;
        var dbSize = debrisPS.sizeOverLifetime; dbSize.enabled = true;
        dbSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));

        // ---------- Трассер ----------
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
        main.maxParticles = 300;
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

    /// <summary>Вспышка: сразу ярко, к концу — тускло-оранжево и прозрачно.</summary>
    static void SetFlashFade(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.85f)));
    }

    /// <summary>Дым/пыль: облачко растёт в grow раз, тормозит, клубится и поднимается со скоростью rise.</summary>
    static void SetupSmoke(ParticleSystem ps, float grow, float rise)
    {
        var main = ps.main;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(grow, new AnimationCurve(new Keyframe(0f, 1f / grow), new Keyframe(0.3f, 0.6f), new Keyframe(1f, 1f)));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(rise);
        vel.z = new ParticleSystem.MinMaxCurve(0f);
        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.limit = 0f;
        drag.dampen = 0.08f;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.3f;
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
    }

    /// <summary>Выстрел: эффекты у оружия + луч из камеры.</summary>
    public void Fire(Vector3 rayOrigin, Vector3 rayDir, Vector3 shooterVelocity, Collider ignore)
    {
        // Звук: несколько вариантов на тип оружия, чтобы очередь не звучала как одна запись.
        audioSource.pitch = Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(ShotClip(profile, Random.Range(0, 3)), 0.9f);
        if (ejectOnFire) EjectCasing(shooterVelocity, ignore);

        MuzzleEffects();

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
                // У дроби звук удара — только от части дробин, иначе он заглушает выстрел.
                Impact(hit, dir, i < 3);
            }
            else if (i == 0) end = rayOrigin + dir * 150f;
        }

        tracer.SetPosition(0, muzzle.position + muzzle.forward * 0.03f);
        tracer.SetPosition(1, end);
        tracer.enabled = true;
        tracerTimer = 0.035f;
    }

    void MuzzleEffects()
    {
        // Звезда — каждый раз разного размера и поворота; языки пламени — тонкие и быстрые.
        var fm = flashPS.main;
        fm.startSize = new ParticleSystem.MinMaxCurve(0.09f * flashScale, 0.14f * flashScale);
        flashPS.transform.localPosition = new Vector3(0f, 0f, 0.02f * flashScale);
        flashPS.Emit(2);
        var flm = flamePS.main;
        flm.startSpeed = new ParticleSystem.MinMaxCurve(5f * flashScale, 9f * flashScale);
        flm.startSize = new ParticleSystem.MinMaxCurve(0.025f * flashScale, 0.05f * flashScale);
        flamePS.Emit(Random.Range(3, 6));

        muzzleLight.enabled = true;
        muzzleLight.intensity = LightIntensity * Mathf.Sqrt(flashScale);
        lightTimer = LightTime;

        // Клуб дыма из ствола.
        var sm = smokePS.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.04f * flashScale, 0.08f * flashScale);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.8f * flashScale);
        sm.startColor = new Color(0.82f, 0.82f, 0.8f, 0.22f);
        smokePS.transform.SetPositionAndRotation(muzzle.position + muzzle.forward * 0.04f, muzzle.rotation);
        smokePS.Emit(smokePerShot);

        heat = Mathf.Min(heat + 1f, 12f);
    }

    void Impact(RaycastHit hit, Vector3 dir, bool sound)
    {
        Quaternion outward = Quaternion.LookRotation(Vector3.Reflect(dir, hit.normal) * 0.5f + hit.normal);
        Vector3 pos = hit.point + hit.normal * 0.01f;
        var mat = SurfaceMaterial(hit);
        bool metal = IsMetal(mat);
        Color surface = SurfaceColor(mat);
        // Чем ближе предмет к камере, тем мельче и медленнее частицы, чтобы не лететь в камеру; вплотную их нет совсем.
        float f = Mathf.Clamp01(hit.distance / 2.5f);

        if (hit.distance >= 0.4f)
        {
            if (metal) EmitSparks(pos, outward, f);
            EmitDust(pos, outward, f, metal ? new Color(0.35f, 0.35f, 0.35f) : surface, metal ? 2 : 6);
            if (!metal) EmitDebris(pos, outward, f, surface);
        }

        if (sound) AudioSource.PlayClipAtPoint(ImpactClip(), hit.point, 0.7f);

        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(dir * damageForce, hit.point, ForceMode.Impulse);

        if (hit.distance >= minDecalDistance) SpawnDecal(hit);
    }

    void EmitSparks(Vector3 pos, Quaternion rot, float f)
    {
        sparksPS.transform.SetPositionAndRotation(pos, rot);
        var m = sparksPS.main;
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * f, 5f * f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.008f * f, 0.018f * f);
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.15f * f, 0.4f * f);
        sparksPS.Emit(Mathf.Max(2, Mathf.RoundToInt(Random.Range(8, 14) * f)));
    }

    /// <summary>Облачко пыли цвета поверхности (чуть светлее — пыль всегда белёсая).</summary>
    void EmitDust(Vector3 pos, Quaternion rot, float f, Color surface, int count)
    {
        dustPS.transform.SetPositionAndRotation(pos, rot);
        var m = dustPS.main;
        Color c = Color.Lerp(surface, new Color(0.78f, 0.76f, 0.72f), 0.45f);
        c.a = 0.55f;
        m.startColor = c;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.3f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.05f * f, 0.13f * f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * f, 2f * f);
        dustPS.Emit(Mathf.Max(1, Mathf.RoundToInt(count * f)));
    }

    /// <summary>Осколки материала: мелкие кубики, падают и отскакивают.</summary>
    void EmitDebris(Vector3 pos, Quaternion rot, float f, Color surface)
    {
        if (debrisMaterial == null) return;
        debrisPS.transform.SetPositionAndRotation(pos, rot);
        var m = debrisPS.main;
        m.startColor = new ParticleSystem.MinMaxGradient(surface * 0.7f, surface * 0.95f);
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.005f * f, 0.014f * f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * f, 3.5f * f);
        debrisPS.Emit(Mathf.Max(1, Mathf.RoundToInt(Random.Range(4, 9) * f)));
    }

    /// <summary>Материал в точке попадания (у меша с несколькими материалами — по треугольнику).</summary>
    static Material SurfaceMaterial(RaycastHit hit)
    {
        var r = hit.collider.GetComponent<Renderer>();
        if (r == null) return null;
        var mats = r.sharedMaterials;
        if (mats.Length > 1 && hit.collider is MeshCollider mc && mc.sharedMesh != null && hit.triangleIndex >= 0)
        {
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mc.sharedMesh.subMeshCount && s < mats.Length; s++)
            {
                var d = mc.sharedMesh.GetSubMesh(s);
                if (index >= d.indexStart && index < d.indexStart + d.indexCount) return mats[s];
            }
        }
        return mats.Length > 0 ? mats[0] : null;
    }

    static readonly string[] MetalWords = { "Steel", "Metal", "Chrome", "Iron", "Brass", "Alumin" };

    static bool IsMetal(Material m)
    {
        if (m == null) return false;
        if (MetalWords.Any(w => m.name.IndexOf(w, System.StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        // glTFast — metallicFactor, URP Lit — _Metallic.
        foreach (var p in new[] { "metallicFactor", "_Metallic" })
            if (m.HasProperty(p)) return m.GetFloat(p) > 0.5f;
        return false;
    }

    static Color SurfaceColor(Material m)
    {
        if (m != null)
            foreach (var p in new[] { "baseColorFactor", "_BaseColor", "_Color" })
                if (m.HasProperty(p)) { var c = m.GetColor(p); c.a = 1f; return c; }
        return new Color(0.6f, 0.6f, 0.6f);
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

        // Дымок из окна выброса вслед за гильзой.
        var sm = smokePS.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.03f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        sm.startColor = new Color(0.8f, 0.8f, 0.78f, 0.16f);
        smokePS.transform.SetPositionAndRotation(port.position, Quaternion.LookRotation(port.right + port.up * 0.5f));
        smokePS.Emit(1);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (tracerTimer > 0f)
        {
            tracerTimer -= dt;
            if (tracerTimer <= 0f) tracer.enabled = false;
        }

        // Свет вспышки гаснет за пару кадров.
        if (lightTimer > 0f)
        {
            lightTimer -= dt;
            float k = Mathf.Clamp01(lightTimer / LightTime);
            muzzleLight.intensity = LightIntensity * Mathf.Sqrt(flashScale) * k * k;
            if (lightTimer <= 0f) muzzleLight.enabled = false;
        }

        // После нескольких выстрелов подряд из ствола ещё несколько секунд тянется тонкий дымок.
        heat = Mathf.Max(0f, heat - dt * 0.6f);
        if (heat > 2f && muzzle != null)
        {
            wispAccum += dt * Mathf.Min(14f, (heat - 2f) * 5f);
            if (wispAccum >= 1f)
            {
                var sm = smokePS.main;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                sm.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.025f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
                sm.startColor = new Color(0.85f, 0.85f, 0.85f, Mathf.Min(0.14f, 0.03f * heat));
                smokePS.transform.SetPositionAndRotation(muzzle.position, Quaternion.LookRotation(Vector3.up));
                int n = (int)wispAccum;
                wispAccum -= n;
                smokePS.Emit(n);
            }
        }
        else wispAccum = 0f;
    }

    // ---------- Звуки (синтезируются один раз) ----------

    /// <summary>Параметры выстрела: резкий хлопок, «тело» звука, низкий удар и хвост (эхо помещения).</summary>
    struct ShotSound
    {
        public float length, crack, crackDecay, body, bodyDecay, thump, thumpHz, thumpDecay, tail, tailDecay, mech;
    }

    static ShotSound SoundFor(Profile p)
    {
        switch (p)
        {
            case Profile.Rifle: // 5,45/7,62: самый резкий и громкий хлопок, длинное эхо
                return new ShotSound { length = 0.9f, crack = 1f, crackDecay = 130f, body = 0.9f, bodyDecay = 16f, thump = 0.85f, thumpHz = 55f, thumpDecay = 14f, tail = 0.65f, tailDecay = 4.5f, mech = 0.12f };
            case Profile.SMG: // 9 мм из длинного ствола: суше и короче пистолета
                return new ShotSound { length = 0.5f, crack = 0.65f, crackDecay = 110f, body = 0.8f, bodyDecay = 26f, thump = 0.6f, thumpHz = 80f, thumpDecay = 20f, tail = 0.4f, tailDecay = 7f, mech = 0.18f };
            case Profile.Shotgun: // 12 калибр: тяжёлый гулкий удар
                return new ShotSound { length = 1.2f, crack = 0.6f, crackDecay = 70f, body = 1f, bodyDecay = 11f, thump = 1f, thumpHz = 45f, thumpDecay = 9f, tail = 0.75f, tailDecay = 3.5f, mech = 0f };
            default: // пистолет 9×19
                return new ShotSound { length = 0.6f, crack = 0.7f, crackDecay = 90f, body = 0.9f, bodyDecay = 22f, thump = 0.8f, thumpHz = 70f, thumpDecay = 18f, tail = 0.5f, tailDecay = 6f, mech = 0.15f };
        }
    }

    // Клипы хранятся в статических полях, а при выходе из Play Mode Unity их уничтожает (статика при этом
    // может пережить перезапуск, если перезагрузка домена выключена) — поэтому уничтоженный клип создаётся заново.
    static AudioClip ShotClip(Profile p, int variant)
    {
        int key = (int)p * 10 + variant;
        if (!shotClips.TryGetValue(key, out var clip) || clip == null) shotClips[key] = clip = CreateShotClip(p, variant);
        return clip;
    }

    static AudioClip ImpactClip()
    {
        if (impactClips == null) impactClips = new AudioClip[3];
        int i = Random.Range(0, impactClips.Length);
        if (impactClips[i] == null) impactClips[i] = CreateThudClip(i + 1);
        return impactClips[i];
    }

    static AudioClip CreateShotClip(Profile p, int variant)
    {
        var s = SoundFor(p);
        const int rate = 44100;
        int length = (int)(rate * s.length);
        var data = new float[length];
        var rng = new System.Random(12345 + variant * 7919 + (int)p * 104729);
        float hz = s.thumpHz * (1f + (variant - 1) * 0.06f);
        float lp = 0f, lp2 = 0f, hp = 0f, prev = 0f, peak = 1e-4f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.35f;      // приглушённый шум
            lp2 += (noise - lp2) * 0.04f;    // глухой хвост (эхо помещения)
            hp = 0.6f * (hp + noise - prev); // звонкий щелчок механики
            prev = noise;
            float crack = noise * Mathf.Exp(-t * s.crackDecay);
            float body = lp * Mathf.Exp(-t * s.bodyDecay);
            float thump = Mathf.Sin(2f * Mathf.PI * hz * t * (1f - t)) * Mathf.Exp(-t * s.thumpDecay);
            // Эхо: хвост нарастает не сразу, а с небольшой задержкой — как отражение от стен.
            float tail = lp2 * 2.5f * Mathf.Exp(-t * s.tailDecay) * Mathf.Clamp01((t - 0.01f) / 0.04f);
            float mt = t - 0.028f; // затвор/рама лязгают сразу после выстрела
            float mech = mt > 0f ? hp * Mathf.Exp(-mt * 250f) : 0f;
            float attack = Mathf.Clamp01(t / 0.0015f);
            data[i] = (crack * s.crack + body * s.body + thump * s.thump + tail * s.tail + mech * s.mech) * attack;
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        for (int i = 0; i < length; i++) data[i] *= 0.95f / peak;
        var clip = AudioClip.Create($"Shot_{p}_{variant}", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>Пуля по стене/дереву/пластику: короткий глухой удар с осыпающейся крошкой.</summary>
    static AudioClip CreateThudClip(int variant)
    {
        const int rate = 44100;
        int length = (int)(rate * 0.25f);
        var data = new float[length];
        var rng = new System.Random(4242 + variant * 17);
        float lp = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * 0.2f;
            float thud = lp * 2f * Mathf.Exp(-t * 45f) + Mathf.Sin(2f * Mathf.PI * (140f + variant * 25f) * t) * Mathf.Exp(-t * 60f) * 0.5f;
            float crumble = noise * 0.12f * Mathf.Exp(-t * 18f) * (rng.NextDouble() < 0.02 ? 4f : 1f);
            data[i] = Mathf.Clamp((thud + crumble) * Mathf.Clamp01(t / 0.001f), -1f, 1f);
        }
        var clip = AudioClip.Create("Thud" + variant, length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
