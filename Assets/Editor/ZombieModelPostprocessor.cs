using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Модели зомби:
///  • в иерархии есть и кость Head (Armature/Root/Hips/.../Head), и меш Head в корне. Для Humanoid-аватара
///    имена костей должны быть уникальны, поэтому объекты вне скелета с именем кости получают суффикс _Mesh
///    (до создания аватара);
///  • лишний шар Icosphere из сцены Blender удаляется;
///  • материалы — как в исходных .glb (Assets/Characters/Zombies/Materials/ZombieMaterials.json, выписано из glb):
///    при экспорте в FBX цвета материалов частично потерялись (серый 0,8 Blender вместо настоящего цвета).
///    Кожа, раны, одежда в glb — цвет материала × цвет вершин; URP Lit цвета вершин не учитывает, поэтому такие
///    материалы переводятся на шейдер glTFast (как было у .glb), остальные остаются на URP Lit с цветом из glb.
/// </summary>
public class ZombieModelPostprocessor : AssetPostprocessor
{
    const string VertexColorShaderPath = "Packages/com.unity.cloud.gltfast/Runtime/Shader/glTF-pbrMetallicRoughness.shadergraph";
    const string MaterialTablePath = "Assets/Characters/Zombies/Materials/ZombieMaterials.json";
    static readonly HashSet<string> BoneNames = new HashSet<string> { "Root", "Hips", "Spine", "Chest", "Neck", "Head", "Jaw" };

    [System.Serializable] class MatInfo { public string name; public float[] baseColor; public float metallic, roughness; public bool vertexColor; }
    [System.Serializable] class ModelInfo { public string model; public MatInfo[] materials; }
    [System.Serializable] class Table { public ModelInfo[] models; }

    public override uint GetVersion() => 6;

    bool IsZombie => assetPath.Replace('\\', '/').StartsWith("Assets/Characters/Zombies/Models/");

    // Вызывается для каждой иерархии верхнего уровня (Armature, Body, Head, ...).
    void OnPostprocessMeshHierarchy(GameObject root)
    {
        if (!IsZombie || root.name == "Armature") return;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (BoneNames.Contains(t.name)) t.name += "_Mesh";
    }

    void OnPostprocessModel(GameObject root)
    {
        if (!IsZombie) return;
        var ico = root.transform.Find("Icosphere");
        if (ico != null) Object.DestroyImmediate(ico.gameObject);

        context.DependsOnSourceAsset(MaterialTablePath); // таблица поменялась — модели переимпортируются
        var info = LoadMaterials(Path.GetFileNameWithoutExtension(assetPath));
        if (info == null) { Debug.LogWarning($"ZombieModelPostprocessor: нет материалов для {Path.GetFileName(assetPath)} в {MaterialTablePath}"); return; }
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(VertexColorShaderPath);
        if (shader == null) Debug.LogWarning("ZombieModelPostprocessor: нет шейдера " + VertexColorShaderPath + " — цвета вершин не будут видны.");

        var done = new HashSet<Material>();
        var vertexColored = new List<string>();
        var missing = new List<string>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            var colors = mesh.colors32;
            bool hasColors = colors != null && colors.Length == mesh.vertexCount;
            if (hasColors)
            {
                // Blender пишет цвета вершин в FBX в sRGB, шейдер glTFast ждёт линейные (как в glTF).
                var linear = new Color[colors.Length];
                for (int i = 0; i < colors.Length; i++) linear[i] = ((Color)colors[i]).linear;
                mesh.colors = linear;
            }
            var mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
            {
                var m = mats[i];
                if (m == null || done.Contains(m)) continue;
                done.Add(m);
                var mi = Find(info, m.name);
                if (mi == null) { missing.Add(m.name); continue; }
                // glTF: цвет в линейном пространстве; в материал — в sRGB, как делает glTFast.
                var c = mi.baseColor != null && mi.baseColor.Length >= 3
                    ? new Color(mi.baseColor[0], mi.baseColor[1], mi.baseColor[2], mi.baseColor.Length > 3 ? mi.baseColor[3] : 1f).gamma
                    : Color.white;
                if (mi.vertexColor && shader != null && hasColors && Painted(mesh, i, colors))
                {
                    m.shader = shader;
                    m.SetColor("baseColorFactor", c);
                    m.SetFloat("metallicFactor", mi.metallic);
                    m.SetFloat("roughnessFactor", mi.roughness);
                    vertexColored.Add(m.name);
                }
                else
                {
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_Metallic", mi.metallic);
                    m.SetFloat("_Smoothness", 1f - mi.roughness);
                }
            }
        }
        Debug.Log($"ZombieModelPostprocessor: {Path.GetFileName(assetPath)} — материалы из glb, с цветами вершин: {string.Join(", ", vertexColored)}" +
                  (missing.Count > 0 ? $"; нет в таблице: {string.Join(", ", missing)}" : ""));
    }

    static ModelInfo LoadMaterials(string model)
    {
        if (!File.Exists(MaterialTablePath)) return null;
        var table = JsonUtility.FromJson<Table>(File.ReadAllText(MaterialTablePath));
        if (table?.models == null) return null;
        foreach (var mi in table.models) if (mi.model == model) return mi;
        return null;
    }

    /// <summary>Материал по имени; Blender добавляет к копиям суффикс .001.</summary>
    static MatInfo Find(ModelInfo info, string name)
    {
        string bare = name;
        int dot = name.LastIndexOf('.');
        if (dot > 0 && int.TryParse(name.Substring(dot + 1), out _)) bare = name.Substring(0, dot);
        MatInfo fallback = null;
        foreach (var m in info.materials)
        {
            if (m.name == name) return m;
            if (m.name == bare) fallback = m;
        }
        return fallback;
    }

    /// <summary>Вершины этой части меша действительно раскрашены (не белые).</summary>
    static bool Painted(Mesh mesh, int sub, Color32[] colors)
    {
        var tris = mesh.GetTriangles(sub);
        for (int i = 0; i < tris.Length; i += 3)
        {
            var v = colors[tris[i]];
            if (v.r < 245 || v.g < 245 || v.b < 245) return true;
        }
        return false;
    }
}
