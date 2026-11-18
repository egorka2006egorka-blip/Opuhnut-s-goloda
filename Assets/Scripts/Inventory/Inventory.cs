using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инвентарь игрока: патроны, аптечки, важные предметы. Хранит пары «вид предмета — количество».
/// Добавляется к игроку автоматически (см. FirstPersonController.Awake) вместе с экраном InventoryScreen.
/// Патроны из инвентаря тратятся при перезарядке (PlayerWeaponHandler).
/// </summary>
public class Inventory : MonoBehaviour
{
    [Serializable]
    public class Stack
    {
        public string id;
        public int count;
        public Stack() { }
        public Stack(string id, int count) { this.id = id; this.count = count; }
    }

    /// <summary>С чем игрок начинает игру (если не загружено сохранение).</summary>
    public static readonly Stack[] StartingItems =
    {
        new Stack(ItemDatabase.Ammo9, 36),
        new Stack(ItemDatabase.Ammo545, 30),
        new Stack(ItemDatabase.Ammo12, 10),
        new Stack(ItemDatabase.Medkit, 1),
        new Stack(ItemDatabase.StudentCard, 1),
    };

    readonly List<Stack> items = new List<Stack>();

    public IReadOnlyList<Stack> Items => items;

    /// <summary>Вызывается при любом изменении содержимого.</summary>
    public event Action Changed;

    void Awake()
    {
        foreach (var s in StartingItems) Add(s.id, s.count);
    }

    public int Count(string id)
    {
        foreach (var s in items) if (s.id == id) return s.count;
        return 0;
    }

    public void Add(string id, int count)
    {
        if (string.IsNullOrEmpty(id) || count <= 0 || ItemDatabase.Get(id) == null) return;
        var s = items.Find(x => x.id == id);
        if (s != null) s.count += count;
        else
        {
            items.Add(new Stack(id, count));
            items.Sort((a, b) => ItemDatabase.Order(a.id).CompareTo(ItemDatabase.Order(b.id)));
        }
        Changed?.Invoke();
    }

    /// <summary>Забирает до count штук. Возвращает, сколько удалось взять.</summary>
    public int Take(string id, int count)
    {
        var s = items.Find(x => x.id == id);
        if (s == null || count <= 0) return 0;
        int n = Mathf.Min(count, s.count);
        s.count -= n;
        if (s.count <= 0) items.Remove(s);
        Changed?.Invoke();
        return n;
    }

    /// <summary>Использовать предмет (аптечку). Возвращает текст для подсказки или null, если использовано успешно.</summary>
    public string Use(string id)
    {
        var def = ItemDatabase.Get(id);
        if (def == null || Count(id) <= 0) return "Предмета нет";
        if (def.heal > 0f)
        {
            var health = GetComponent<PlayerHealth>();
            if (health == null || health.IsDead) return "Сейчас нельзя";
            if (health.Health >= health.maxHealth - 0.5f) return "Здоровье и так в порядке";
            health.Heal(def.heal);
            Take(id, 1);
            return null;
        }
        return "Этот предмет нельзя использовать";
    }

    // ---------- Сохранение ----------

    public List<Stack> Snapshot()
    {
        var list = new List<Stack>();
        foreach (var s in items) list.Add(new Stack(s.id, s.count));
        return list;
    }

    public void Restore(List<Stack> saved)
    {
        items.Clear();
        if (saved != null)
            foreach (var s in saved)
                if (s != null && s.count > 0 && ItemDatabase.Get(s.id) != null)
                {
                    var have = items.Find(x => x.id == s.id);
                    if (have != null) have.count += s.count; else items.Add(new Stack(s.id, s.count));
                }
        items.Sort((a, b) => ItemDatabase.Order(a.id).CompareTo(ItemDatabase.Order(b.id)));
        Changed?.Invoke();
    }
}
