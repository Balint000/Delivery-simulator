using DeliverySimulator.Models;

namespace DeliverySimulator.Display;

// ══════════════════════════════════════════════════════
//  ÉLŐ KONZOL MEGJELENÍTŐ
//
//  A futárok státusza és az eseménynapló folyamatosan
//  frissül a képernyőn (nem görgeti le a szöveget).
//
//  TRÜKK: Console.SetCursorPosition(x, y)
//    A konzolon minden sor és oszlop koordinátával
//    megcímezhető. Ha visszaugrunk egy korábbi sorra
//    és felülírjuk → "frissülésnek" látszik.
//
//  THREAD-SAFETY:
//    lock(_lock) → egyszerre csak egy futár írhat
//    a konzolra, nem csúszik össze a kimenet.
// ══════════════════════════════════════════════════════

public class LiveConsole
{
    // Belső állapot
    private readonly object       _lock      = new();
    private readonly List<string> _events    = [];
    private const int             MaxEvents  = 10;

    private int  _courierPanelRow;  // Hányadik konzolsorban kezdődik a futárpanel
    private int  _eventPanelRow;    // Hányadik konzolsorban kezdődik az eseménynapló
    private int  _courierCount;
    private bool _ready;

    // ── Inicializálás ───────────────────────────────────

    /// <summary>
    /// Képernyő törlése, keretek megjelenítése, pozíciók rögzítése.
    /// CSAK EGYSZER hívjuk, a szimuláció elején!
    /// </summary>
    public void Init(string title, int courierCount)
    {
        lock (_lock)
        {
            _courierCount = courierCount;
            Console.CursorVisible = false;
            Console.Clear();

            // Fejléc
            Write(ConsoleColor.Cyan,
                "╔══════════════════════════════════════════════════════╗\n" +
                $"║  🚚 {title,-47}║\n" +
                "╚══════════════════════════════════════════════════════╝");
            Console.WriteLine("\n");

            // Futárpanel fejléce
            Write(ConsoleColor.DarkYellow,
                "━━━ FUTÁROK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            _courierPanelRow = Console.CursorTop;

            // Üres sorok lefoglalása futároknak
            for (int i = 0; i < courierCount; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();

            // Eseménynapló fejléce
            Write(ConsoleColor.DarkYellow,
                "━━━ ESEMÉNYEK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            _eventPanelRow = Console.CursorTop;

            for (int i = 0; i < MaxEvents; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();
            _ready = true;
        }
    }

    // ── Futár státusz frissítés ─────────────────────────

    /// <summary>
    /// Egy futár sorának frissítése — visszaugrás a megfelelő sorba,
    /// felülírás, majd kurzor visszahelyezése.
    /// </summary>
    public void UpdateCourier(
        int    courierId,
        string name,
        string status,
        string location,
        string? target          = null,
        int    completedCount   = 0,
        int?   estimatedMin     = null)
    {
        if (!_ready) return;

        lock (_lock)
        {
            string loc  = target != null
                ? $"{Clip(location, 14)} → {Clip(target, 14)}"
                : Clip(location, 30);

            string eta  = estimatedMin.HasValue ? $"~{estimatedMin}p" : "     ";

            string line = $"  {status,-15} │ {name,-20} │ {loc,-30} │ {eta,-5} │ {completedCount} kézb.";

            // Melyik sorban van ez a futár? (courierId 1-től indul → -1)
            int row = _courierPanelRow + (courierId - 1);

            Overwrite(row, line);
        }
    }

    // ── Eseménynapló ────────────────────────────────────

    /// <summary>
    /// Új esemény hozzáadása. Ha betelt a napló, a legrégebbi kiesik.
    /// </summary>
    public void LogEvent(string type, string message)
    {
        if (!_ready) return;

        lock (_lock)
        {
            string ts   = DateTime.Now.ToString("HH:mm:ss");
            string icon = type switch
            {
                "delivery" => "✅",
                "delay"    => "⚠️ ",
                "moving"   => "🚗",
                "pickup"   => "📦",
                "start"    => "🚀",
                "done"     => "🏁",
                "refill"   => "📥",
                _          => "ℹ️ "
            };

            string line = $"  [{ts}] {icon} {message}";

            if (_events.Count >= MaxEvents) _events.RemoveAt(0);
            _events.Add(line);

            // Teljes eseménynapló újrarajzolása
            int origRow = Console.CursorTop;
            int origCol = Console.CursorLeft;

            for (int i = 0; i < MaxEvents; i++)
            {
                Console.SetCursorPosition(0, _eventPanelRow + i);

                if (i < _events.Count)
                {
                    var ev = _events[i];
                    Console.ForegroundColor = ev.Contains("✅") ? ConsoleColor.Green
                                            : ev.Contains("⚠️") ? ConsoleColor.Yellow
                                            : ev.Contains("🏁") ? ConsoleColor.Cyan
                                            : ConsoleColor.Gray;

                    Console.Write(PadRight(ev, Console.WindowWidth - 1));
                    Console.ResetColor();
                }
                else
                {
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                }
            }

            Console.SetCursorPosition(origCol, origRow);
        }
    }

    /// <summary>
    /// Szimuláció vége — kurzor a panel alá, visszaállítás.
    /// </summary>
    public void Finish()
    {
        lock (_lock)
        {
            int finalRow = _eventPanelRow + MaxEvents + 2;
            Console.SetCursorPosition(0, finalRow);
            Console.CursorVisible = true;
            Console.ResetColor();
        }
    }

    // ── Segédmetódusok ──────────────────────────────────

    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static void Overwrite(int row, string text)
    {
        int origRow = Console.CursorTop;
        int origCol = Console.CursorLeft;
        Console.SetCursorPosition(0, row);
        Console.Write(PadRight(text, Console.WindowWidth - 1));
        Console.SetCursorPosition(origCol, origRow);
    }

    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 2)] + "..";

    private static string PadRight(string s, int w) =>
        s.Length >= w ? s[..w] : s.PadRight(w);
}
