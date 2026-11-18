using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Мелкий предмет на локации (кружка, тарелка, кастрюля…), который можно поднять, удерживая «Подобрать» (E),
/// и швырнуть левой кнопкой мыши. Нужен Rigidbody и выпуклый коллайдер (их ставит CanteenSceneBuilder).
/// Брошенный предмет, попав в зомби на скорости, наносит урон.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GrabbableProp : MonoBehaviour
{
    public static readonly List<GrabbableProp> All = new List<GrabbableProp>();

    public string displayName = "Предмет";
    [Tooltip("Множитель урона зомби при попадании брошенным предметом.")]
    public float impactDamageScale = 1f;

    [Tooltip("Материал — от него зависит звук удара. Определяется по имени объекта при запуске.")]
    public PropMaterial material = PropMaterial.Plastic;

    public Rigidbody Body { get; private set; }
    public bool IsHeld { get; set; }
    /// <summary>До какого момента (Time.time) удар этим предметом считается броском и ранит зомби.</summary>
    public float ThrownUntil { get; set; } = -1f;
    /// <summary>До какого момента предмет звучит при ударах — только после броска (ЛКМ).
    /// Просто выпущенный из рук или задетый чем-то предмет падает беззвучно.</summary>
    public float SoundUntil { get; set; } = -1f;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
        material = PropSounds.MaterialFor(gameObject.name);
        // Предметы стоят на столах и стойках чуть «внутри» них — спят, пока их не тронут, чтобы не подпрыгивать на старте.
        Body.Sleep();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    float nextSoundTime;

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        Vector3 point = c.contactCount > 0 ? c.GetContact(0).point : transform.position;

        // Звук — один, в момент первого удара брошенного предмета; отскоки и падение после него беззвучны.
        bool thrown = Time.time <= ThrownUntil;
        var hitbox = c.collider.GetComponent<ZombieHitbox>();
        bool fromLoot = c.collider.GetComponentInParent<ItemPickup>() != null;
        if (speed > 1.2f && Time.time >= nextSoundTime && !IsHeld && Time.time <= SoundUntil && !fromLoot)
        {
            nextSoundTime = Time.time + 0.09f;
            float strength = Mathf.Clamp01(Mathf.InverseLerp(1.2f, thrown ? 9f : 12f, speed));
            PropSounds.PlayImpact(material, point, strength, Body.mass);
            // По зомби — ещё и глухой удар по телу.
            if (hitbox != null) PropSounds.PlayImpact(PropMaterial.Soft, point, Mathf.Min(1f, strength * 1.3f), 2f);
            SoundUntil = -1f;
        }

        if (!thrown || speed < 3f) return;
        if (hitbox == null || !hitbox.HasOwner) return;
        // Урон от массы и скорости: кружка — немного, кастрюля или огнетушитель — ощутимо.
        float damage = Mathf.Clamp(Body.mass * speed * 1.6f, 6f, 60f) * impactDamageScale;
        hitbox.Hit(damage, point, c.relativeVelocity.sqrMagnitude > 1e-4f ? -c.relativeVelocity.normalized : transform.forward);
        ThrownUntil = -1f; // один бросок — одно попадание
    }

    /// <summary>Русские названия мелких предметов по началу имени объекта в модели.</summary>
    public static readonly (string prefix, string name)[] Names =
    {
        ("Drink_Compote", "Стакан компота"), ("Drink_Orange", "Стакан апельсинового сока"), ("Drink_Apple", "Стакан яблочного сока"),
        ("Drink_Tea", "Чашка чая"), ("TableSet", "Салфетница"), ("SaladPlate", "Тарелка с салатом"), ("BreadPlate", "Тарелка с хлебом"),
        ("PlateStack", "Стопка тарелок"), ("CutleryHolder_Forks", "Стакан с вилками"), ("CutleryHolder_Spoons", "Стакан с ложками"),
        ("CutleryHolder_Knives", "Стакан с ножами"), ("TrayStack", "Стопка подносов"), ("Bin_", "Урна"),
        ("Tomato", "Помидор"), ("Mug", "Кружка"), ("BowlStack", "Стопка мисок"), ("Fork", "Вилка"), ("Spoon", "Ложка"),
        ("Bowl_Potatoes", "Миска с картошкой"), ("Bowl_", "Миска"), ("TableKnife", "Столовый нож"), ("Carrot", "Морковь"),
        ("Cucumber", "Огурец"), ("OilBottle", "Бутылка масла"), ("Whisk", "Венчик"), ("OilCanister", "Канистра масла"),
        ("GN_Stack", "Стопка гастроёмкостей"), ("CutleryTray", "Лоток для приборов"), ("GN_12", "Гастроёмкость с морковью"),
        ("GN_11", "Гастроёмкость с крышкой"), ("MixingBowl", "Миска для замеса"), ("Spatula", "Лопатка"), ("ChefKnife", "Поварской нож"),
        ("Skimmer", "Шумовка"), ("KitchenScale", "Кухонные весы"), ("CardboardBox", "Картонная коробка"), ("CuttingBoard", "Разделочная доска"),
        ("Ladle", "Половник"), ("StockPot", "Кастрюля"), ("Saucepan", "Сотейник"), ("StorageBox", "Контейнер"), ("FryingPan", "Сковородка"),
        ("FireExtinguisher", "Огнетушитель"), ("Tray_GN", "Противень"), ("TrashBin", "Мусорное ведро"),
    };

    /// <summary>Название по имени объекта; null — объект не из списка мелких предметов.</summary>
    public static string NameFor(string objectName)
    {
        foreach (var (prefix, name) in Names)
            if (objectName.StartsWith(prefix)) return name;
        return null;
    }
}
