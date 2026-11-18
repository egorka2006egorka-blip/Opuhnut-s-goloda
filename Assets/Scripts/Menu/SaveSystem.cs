using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Сохранения в 6 слотах (Application.persistentDataPath/saves/slotN.json + slotN.png — снимок экрана):
/// сцена, положение и взгляд игрока, здоровье, инвентарь, оружие (где лежит, сколько патронов, что в руках).
/// Зомби не сохраняются — после загрузки они появляются заново.
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 6;

    [Serializable]
    public class WeaponState
    {
        public string objectName;
        public string displayName;
        public Vector3 position;
        public Quaternion rotation;
        public int ammo;
        public bool held;
        /// <summary>Ещё не куплено у продавщицы (с версии 4).</summary>
        public bool forSale;
        /// <summary>Лежит в инвентаре игрока (с версии 6).</summary>
        public bool stowed;
    }

    [Serializable]
    public class SaveData
    {
        public int version = 6;
        public string scene;
        public string savedAt;
        public long savedTicks;
        public Vector3 playerPosition;
        public float yaw, pitch;
        public float health;
        public List<WeaponState> weapons = new List<WeaponState>();
        /// <summary>Содержимое инвентаря (с версии 3; в старых сохранениях — стартовый набор).</summary>
        public List<Inventory.Stack> items = new List<Inventory.Stack>();
        /// <summary>Подобранные предметы, расставленные в сцене (по имени объекта).</summary>
        public List<string> collectedLoot = new List<string>();
        /// <summary>Босс в подвале побеждён (с версии 4).</summary>
        public bool bossDefeated;
        /// <summary>Этап сюжета (с версии 5), см. StoryDirector.Stage.</summary>
        public int storyStage;
    }

    static string Dir => Path.Combine(Application.persistentDataPath, "saves");
    static string JsonPath(int slot) => Path.Combine(Dir, "slot" + (slot + 1) + ".json");
    static string PngPath(int slot) => Path.Combine(Dir, "slot" + (slot + 1) + ".png");
    const string LastSlotKey = "Save.LastSlot";

    /// <summary>Сохранение, которое нужно применить после загрузки сцены.</summary>
    public static SaveData Pending { get; set; }

    /// <summary>Слот, в который сохраняли или из которого загружались последним (для быстрого сохранения).</summary>
    public static int LastSlot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(LastSlotKey, 0), 0, SlotCount - 1);
        private set { PlayerPrefs.SetInt(LastSlotKey, value); PlayerPrefs.Save(); }
    }

    static bool migrated;

    /// <summary>Старое единственное сохранение (savegame.json) переносится в слот 1.</summary>
    static void Migrate()
    {
        if (migrated) return;
        migrated = true;
        try
        {
            string old = Path.Combine(Application.persistentDataPath, "savegame.json");
            if (!File.Exists(old)) return;
            Directory.CreateDirectory(Dir);
            if (!File.Exists(JsonPath(0))) File.Move(old, JsonPath(0));
            else File.Delete(old);
        }
        catch (Exception e) { Debug.LogWarning("SaveSystem: " + e.Message); }
    }

    public static SaveData Load(int slot)
    {
        Migrate();
        try
        {
            string path = JsonPath(slot);
            if (!File.Exists(path)) return null;
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            if (data == null || string.IsNullOrEmpty(data.scene)) return null;
            if (data.savedTicks == 0) data.savedTicks = File.GetLastWriteTime(path).Ticks;
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning("SaveSystem: не удалось прочитать слот " + (slot + 1) + ": " + e.Message);
            return null;
        }
    }

    public static SaveData[] LoadAll()
    {
        var all = new SaveData[SlotCount];
        for (int i = 0; i < SlotCount; i++) all[i] = Load(i);
        return all;
    }

    /// <summary>Слот с самым свежим сохранением, -1 — сохранений нет.</summary>
    public static int LatestSlot(SaveData[] all)
    {
        int best = -1;
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && (best < 0 || all[i].savedTicks > all[best].savedTicks)) best = i;
        return best;
    }

    /// <summary>Снимок экрана слота (null — нет). Текстуру нужно уничтожить, когда она больше не нужна.</summary>
    public static Texture2D LoadThumbnail(int slot)
    {
        try
        {
            string path = PngPath(slot);
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
            UnityEngine.Object.Destroy(tex);
        }
        catch (Exception e) { Debug.LogWarning("SaveSystem: " + e.Message); }
        return null;
    }

    /// <summary>Сохраняет текущее состояние игры в слот. false — сохранять нечего (нет игрока или он погиб).</summary>
    public static bool Save(int slot, Texture2D screenshot)
    {
        var fpc = UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
        if (fpc == null) return false;
        var health = fpc.GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) return false;
        var handler = fpc.GetComponent<PlayerWeaponHandler>();

        var now = DateTime.Now;
        var data = new SaveData
        {
            scene = SceneManager.GetActiveScene().name,
            savedAt = now.ToString("dd.MM.yyyy  HH:mm:ss"),
            savedTicks = now.Ticks,
            playerPosition = fpc.transform.position,
            yaw = fpc.transform.eulerAngles.y,
            pitch = fpc.Pitch,
            health = health != null ? health.Health : 100f,
            bossDefeated = ZombieBoss.Defeated,
            storyStage = StoryDirector.SaveStage,
        };
        var inventory = fpc.GetComponent<Inventory>();
        if (inventory != null) data.items = inventory.Snapshot();
        data.collectedLoot = new List<string>(ItemPickup.Collected);
        foreach (var w in WeaponPickup.All)
        {
            data.weapons.Add(new WeaponState
            {
                objectName = w.gameObject.name,
                displayName = w.displayName,
                position = w.transform.position,
                rotation = w.transform.rotation,
                ammo = w.Ammo,
                held = handler != null && handler.CurrentWeapon == w,
                forSale = w.forSale,
                stowed = w.Stowed,
            });
        }

        try
        {
            Migrate();
            Directory.CreateDirectory(Dir);
            File.WriteAllText(JsonPath(slot), JsonUtility.ToJson(data, true));
            if (screenshot != null) File.WriteAllBytes(PngPath(slot), screenshot.EncodeToPNG());
            else if (File.Exists(PngPath(slot))) File.Delete(PngPath(slot));
            LastSlot = slot;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("SaveSystem: не удалось записать слот " + (slot + 1) + ": " + e.Message);
            return false;
        }
    }

    public static void Delete(int slot)
    {
        try
        {
            if (File.Exists(JsonPath(slot))) File.Delete(JsonPath(slot));
            if (File.Exists(PngPath(slot))) File.Delete(PngPath(slot));
        }
        catch (Exception e) { Debug.LogWarning("SaveSystem: " + e.Message); }
    }

    /// <summary>Загружает сцену сохранения и применяет его. false — сцена недоступна.</summary>
    public static bool BeginLoad(int slot, SaveData data, out string scene)
    {
        scene = data?.scene;
        if (data == null || !Application.CanStreamedLevelBeLoaded(scene)) return false;
        Pending = data;
        LastSlot = slot;
        return true;
    }

    /// <summary>Применяет сохранение к только что загруженной сцене (до Start у объектов сцены).</summary>
    public static void Apply(SaveData data)
    {
        var fpc = UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
        if (data == null || fpc == null) return;
        fpc.Teleport(data.playerPosition, data.yaw, data.pitch);
        var health = fpc.GetComponent<PlayerHealth>();
        if (health != null) health.SetHealth(data.health);
        var inventory = fpc.GetComponent<Inventory>();
        if (inventory != null && data.version >= 3) inventory.Restore(data.items);
        ItemPickup.Collected.Clear();
        if (data.collectedLoot != null)
        {
            ItemPickup.Collected.AddRange(data.collectedLoot);
            foreach (var p in UnityEngine.Object.FindObjectsByType<ItemPickup>(FindObjectsSortMode.None))
                if (!p.spawned && data.collectedLoot.Contains(p.name)) UnityEngine.Object.Destroy(p.gameObject);
        }
        var handler = fpc.GetComponent<PlayerWeaponHandler>();

        // Оружие сопоставляем по имени объекта и названию (одинаковые — по порядку).
        var free = new List<WeaponPickup>(WeaponPickup.All);
        WeaponPickup toEquip = null;
        foreach (var s in data.weapons)
        {
            var w = free.Find(x => x.gameObject.name == s.objectName && x.displayName == s.displayName)
                    ?? free.Find(x => x.displayName == s.displayName);
            if (w == null) continue;
            free.Remove(w);
            w.SetAmmo(s.ammo);
            if (data.version >= 4) w.forSale = s.forSale && !s.held;
            if (s.held) { toEquip = w; continue; }
            if (s.stowed) { w.forSale = false; w.Stow(fpc.transform); continue; }
            var rb = w.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            w.transform.SetPositionAndRotation(s.position, s.rotation);
        }
        if (toEquip != null && handler != null) handler.Equip(toEquip);
        if (data.version >= 4 && data.bossDefeated) ZombieBoss.RemoveDefeated();
        if (data.version >= 5) StoryDirector.PendingStage = data.storyStage;
    }

    // ---------- Снимок экрана ----------

    /// <summary>Пока true, интерфейс игры (прицел, здоровье, патроны) не рисуется — для чистого снимка.</summary>
    public static bool HideHud { get; set; }

    /// <summary>Снимок текущего кадра, уменьшенный до width×height. Вызывать после WaitForEndOfFrame.</summary>
    public static Texture2D CaptureThumbnail(int width = 480, int height = 270)
    {
        var full = ScreenCapture.CaptureScreenshotAsTexture();
        if (full == null) return null;
        var thumb = Downscale(full, width, height);
        UnityEngine.Object.Destroy(full);
        return thumb;
    }

    /// <summary>Уменьшение с усреднением (на CPU — без проблем с цветовым пространством), с обрезкой под пропорции.</summary>
    static Texture2D Downscale(Texture2D src, int w, int h)
    {
        var px = src.GetPixels32();
        int sw = src.width, sh = src.height;
        // Обрезаем исходник по центру под пропорции снимка.
        float aspect = (float)w / h;
        int cw = sw, ch = sh;
        if ((float)sw / sh > aspect) cw = Mathf.RoundToInt(sh * aspect); else ch = Mathf.RoundToInt(sw / aspect);
        int ox = (sw - cw) / 2, oy = (sh - ch) / 2;

        var dst = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = oy + y * ch / h, y1 = Mathf.Max(y0 + 1, oy + (y + 1) * ch / h);
            for (int x = 0; x < w; x++)
            {
                int x0 = ox + x * cw / w, x1 = Mathf.Max(x0 + 1, ox + (x + 1) * cw / w);
                int r = 0, g = 0, b = 0, n = 0;
                for (int yy = y0; yy < y1; yy += 1)
                    for (int xx = x0; xx < x1; xx += 1)
                    {
                        var c = px[yy * sw + xx];
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                dst[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        tex.SetPixels32(dst);
        tex.Apply();
        return tex;
    }
}
