using UnityEngine;

/// <summary>
/// Основа для материалов, создаваемых из кода (продавщица, Шапик, предметы, клетка, лом, обломки).
/// Нельзя брать материал у GameObject.CreatePrimitive: в сборке под URP это встроенный Default-Material
/// со стандартным шейдером — всё становится фиолетовым. Поэтому основа — настоящие материалы URP Lit
/// из Assets/Resources/RuntimeMaterials (вместе с ними в сборку попадают нужные варианты шейдера, в том числе со свечением).
/// </summary>
public static class RuntimeMaterials
{
    static Material lit, litEmissive;

    public static Material Base(bool emissive)
    {
        ref Material m = ref emissive ? ref litEmissive : ref lit;
        if (m != null) return m;
        m = Resources.Load<Material>(emissive ? "RuntimeMaterials/RuntimeLitEmissive" : "RuntimeMaterials/RuntimeLit");
        if (m == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null) m = new Material(shader) { hideFlags = HideFlags.DontSave };
            else
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                m = tmp.GetComponent<Renderer>().sharedMaterial;
                Object.DestroyImmediate(tmp);
            }
            Debug.LogWarning("RuntimeMaterials: нет Assets/Resources/RuntimeMaterials — материалы могут быть фиолетовыми в сборке.");
        }
        return m;
    }

    /// <summary>Новый материал URP Lit заданного цвета (и свечения).</summary>
    public static Material Create(Color c, float smooth = 0.3f, float metal = 0f, Color? emission = null)
    {
        var m = new Material(Base(emission.HasValue)) { hideFlags = HideFlags.DontSave, color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_EmissionColor"))
        {
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
        }
        return m;
    }
}
