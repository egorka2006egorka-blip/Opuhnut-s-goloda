using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Игровые действия, клавиши которых можно переназначить в настройках.</summary>
public enum GameAction
{
    MoveForward, MoveBack, MoveLeft, MoveRight, Sprint, Jump,
    Fire, Aim, Reload, Interact, Throw, ToggleView, QuickSave
}

/// <summary>
/// Назначение клавиш: каждое действие привязано к клавише клавиатуры или кнопке мыши.
/// Хранится в PlayerPrefs. Код привязки: ≥ 0 — значение Key, &lt; 0 — кнопка мыши.
/// </summary>
public static class Controls
{
    public const int MouseLeft = -1, MouseRight = -2, MouseMiddle = -3, MouseForward = -4, MouseBack = -5;

    public static readonly GameAction[] Actions = (GameAction[])System.Enum.GetValues(typeof(GameAction));

    static readonly string[] names =
    {
        "Вперёд", "Назад", "Влево", "Вправо", "Бег", "Прыжок",
        "Стрельба", "Прицеливание", "Перезарядка", "Подобрать / открыть", "Бросить оружие", "Вид от 3-го лица", "Быстрое сохранение"
    };

    static readonly int[] defaults =
    {
        (int)Key.W, (int)Key.S, (int)Key.A, (int)Key.D, (int)Key.LeftShift, (int)Key.Space,
        MouseLeft, MouseRight, (int)Key.R, (int)Key.E, (int)Key.G, (int)Key.V, (int)Key.F5
    };

    static int[] bindings;

    static int[] Bindings
    {
        get
        {
            if (bindings == null) Load();
            return bindings;
        }
    }

    static string Pref(GameAction a) => "Controls." + a;

    static void Load()
    {
        bindings = new int[defaults.Length];
        for (int i = 0; i < defaults.Length; i++)
            bindings[i] = PlayerPrefs.GetInt(Pref((GameAction)i), defaults[i]);
    }

    public static string ActionName(GameAction a) => names[(int)a];
    public static int Binding(GameAction a) => Bindings[(int)a];

    public static bool IsPressed(GameAction a) { var c = Control(Binding(a)); return c != null && c.isPressed; }
    public static bool WasPressed(GameAction a) { var c = Control(Binding(a)); return c != null && c.wasPressedThisFrame; }

    static ButtonControl Control(int code)
    {
        if (code >= 0)
        {
            var kb = Keyboard.current;
            if (kb == null || code == (int)Key.None) return null;
            try { return kb[(Key)code]; } catch { return null; }
        }
        var m = Mouse.current;
        if (m == null) return null;
        switch (code)
        {
            case MouseLeft: return m.leftButton;
            case MouseRight: return m.rightButton;
            case MouseMiddle: return m.middleButton;
            case MouseForward: return m.forwardButton;
            case MouseBack: return m.backButton;
            default: return null;
        }
    }

    /// <summary>Назначает клавишу. Если она уже занята другим действием — действия меняются клавишами.</summary>
    public static void Rebind(GameAction a, int code)
    {
        var b = Bindings;
        int old = b[(int)a];
        for (int i = 0; i < b.Length; i++)
            if (i != (int)a && b[i] == code)
            {
                b[i] = old;
                PlayerPrefs.SetInt(Pref((GameAction)i), old);
            }
        b[(int)a] = code;
        PlayerPrefs.SetInt(Pref(a), code);
        PlayerPrefs.Save();
    }

    public static void ResetDefaults()
    {
        bindings = (int[])defaults.Clone();
        foreach (var a in Actions) PlayerPrefs.SetInt(Pref(a), defaults[(int)a]);
        PlayerPrefs.Save();
    }

    /// <summary>Нажатая в этом кадре клавиша или кнопка мыши (кроме Esc — им отменяют переназначение).</summary>
    public static bool TryCapture(out int code)
    {
        code = 0;
        var kb = Keyboard.current;
        if (kb != null)
            foreach (var k in kb.allKeys)
                if (k != null && k.wasPressedThisFrame && k.keyCode != Key.Escape && k.keyCode != Key.None)
                {
                    code = (int)k.keyCode;
                    return true;
                }
        var m = Mouse.current;
        if (m != null)
        {
            if (m.leftButton.wasPressedThisFrame) { code = MouseLeft; return true; }
            if (m.rightButton.wasPressedThisFrame) { code = MouseRight; return true; }
            if (m.middleButton.wasPressedThisFrame) { code = MouseMiddle; return true; }
            if (m.forwardButton.wasPressedThisFrame) { code = MouseForward; return true; }
            if (m.backButton.wasPressedThisFrame) { code = MouseBack; return true; }
        }
        return false;
    }

    public static string KeyName(GameAction a) => CodeName(Binding(a));

    /// <summary>Название клавиши для интерфейса (латиница для букв — независимо от раскладки).</summary>
    public static string CodeName(int code)
    {
        switch (code)
        {
            case MouseLeft: return "ЛКМ";
            case MouseRight: return "ПКМ";
            case MouseMiddle: return "СКМ";
            case MouseForward: return "Мышь 5";
            case MouseBack: return "Мышь 4";
        }
        var key = (Key)code;
        switch (key)
        {
            case Key.None: return "—";
            case Key.Space: return "Пробел";
            case Key.LeftShift: return "Левый Shift";
            case Key.RightShift: return "Правый Shift";
            case Key.LeftCtrl: return "Левый Ctrl";
            case Key.RightCtrl: return "Правый Ctrl";
            case Key.LeftAlt: return "Левый Alt";
            case Key.RightAlt: return "Правый Alt";
            case Key.Enter: return "Enter";
            case Key.Backspace: return "Backspace";
            case Key.UpArrow: return "Стрелка вверх";
            case Key.DownArrow: return "Стрелка вниз";
            case Key.LeftArrow: return "Стрелка влево";
            case Key.RightArrow: return "Стрелка вправо";
            case Key.CapsLock: return "Caps Lock";
            case Key.Backquote: return "`";
            case Key.Minus: return "-";
            case Key.Equals: return "=";
            case Key.LeftBracket: return "[";
            case Key.RightBracket: return "]";
            case Key.Semicolon: return ";";
            case Key.Quote: return "'";
            case Key.Comma: return ",";
            case Key.Period: return ".";
            case Key.Slash: return "/";
            case Key.Backslash: return "\\";
        }
        string s = key.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        if (s.StartsWith("Numpad")) return "Num " + s.Substring(6);
        return s;
    }
}
