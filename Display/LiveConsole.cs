using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Display;

public class LiveConsole
{
    // ── Belső állapot ───────────────────────────────────

    private readonly object _lock = new();
    private readonly List<string> _events = [];
    private readonly List<string> _notifications = [];

    private const int MaxEvents = 10;
    private const int MaxNotifications = 5;

    private int _courierPanelRow;
    private int _eventPanelRow;
    private int _notificationPanelRow;
    private int _courierCount;
    private bool _ready;

    // FIX #1 – Az adatbázis-ID NEM feltétlenül 1-től növekvő egész.
    // Ezért Id→sorindex szótárat használunk a kurzorsor kiszámításához.
    private Dictionary<int, int> _courierIndexMap = [];

    // ── Input mód ───────────────────────────────────────
    private volatile bool _inputMode = false;

    // Inicializálás

    /// <param name="courierIndexMap">Futár DB-Id → 0-alapú sorindex</param>
    public void Init(string title, int courierCount, Dictionary<int, int> courierIndexMap)
    {
        lock (_lock)
        {
            _courierCount = courierCount;
            _courierIndexMap = courierIndexMap;

            int minHeight = 24 + courierCount;
            if (Console.WindowHeight < minHeight)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠  A terminálablak túl kicsi a megjelenítéshez.");
                Console.WriteLine($"   Szükséges: legalább {minHeight} sor magas ablak.");
                Console.WriteLine($"   Jelenlegi: {Console.WindowHeight} sor.");
                Console.WriteLine();
                Console.ResetColor();
                Console.WriteLine("Nagyítsd meg az ablakot, majd nyomj Entert.");
                Console.ReadLine();
                Console.Clear();
            }

            Console.CursorVisible = false;
            Console.Clear();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(title);
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("━━━ FUTÁROK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.ResetColor();

            _courierPanelRow = Console.CursorTop;
            for (int i = 0; i < courierCount; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("━━━ ESEMÉNYEK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.ResetColor();

            _eventPanelRow = Console.CursorTop;
            for (int i = 0; i < MaxEvents; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("━━━ ÉRTESÍTÉSEK (késési figyelmeztetések) ━━━━━━━━━━━━━");
            Console.ResetColor();

            _notificationPanelRow = Console.CursorTop;
            for (int i = 0; i < MaxNotifications; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  [Ctrl+C] Leállítás");
            Console.ResetColor();

            _ready = true;
        }
    }

    // Futár státusz frissítés

    public void UpdateCourier(
        int courierId,
        string name,
        string status,
        string location,
        string? target = null,
        int completedCount = 0,
        int? estimatedMin = null)
    {
        if (!_ready || _inputMode) return;

        lock (_lock)
        {
            if (_inputMode) return;

            int rowIndex = _courierIndexMap.TryGetValue(courierId, out int idx)
                ? idx
                : _courierCount - 1;

            string loc = target != null
                ? $"{Clip(location, 14)} → {Clip(target, 14)}"
                : Clip(location, 30);

            string eta = estimatedMin.HasValue ? $"~{estimatedMin}p" : "     ";
            string line = $"  {status,-25} │ {name,-30} │ {loc,-45} │ {eta,-8} │ {completedCount} kézb.";

            int row = _courierPanelRow + rowIndex;
            Overwrite(row, line);
        }
    }

    // Eseménynapló

    public void LogEvent(string type, string message)
    {
        if (!_ready || _inputMode) return;

        lock (_lock)
        {
            if (_inputMode) return;

            string ts = DateTime.Now.ToString("HH:mm:ss");

            string icon = type switch
            {
                "delivery" => "✅",
                "delay" => "⚠️ ",
                "moving" => "🚗",
                "pickup" => "📦",
                "start" => "🟢",
                "done" => "🏁",
                "refill" => "📥",
                _ => "ℹ️ "
            };

            string line = $"  [{ts}] {icon} {message}";

            if (_events.Count >= MaxEvents) _events.RemoveAt(0);
            _events.Add(line);

            RedrawPanel(_eventPanelRow, _events, MaxEvents,
                e => e.Contains("✅") ? ConsoleColor.Green
                   : e.Contains("⚠️") ? ConsoleColor.Yellow
                   : e.Contains("🏁") ? ConsoleColor.Cyan
                   : ConsoleColor.Gray);
        }
    }

    // Értesítési panel

    public void LogNotification(string customer, string orderNumber, int lateMinutes)
    {
        if (!_ready || _inputMode) return;

        lock (_lock)
        {
            if (_inputMode) return;

            string ts = DateTime.Now.ToString("HH:mm:ss");
            string line = $"  [{ts}] 📬 {customer,-20} │ {orderNumber,-10} │ +{lateMinutes} perc késés várható";

            if (_notifications.Count >= MaxNotifications) _notifications.RemoveAt(0);
            _notifications.Add(line);

            RedrawPanel(_notificationPanelRow, _notifications, MaxNotifications,
                _ => ConsoleColor.Magenta);
        }
    }

    // Szimuláció vége

    public void Finish()
    {
        lock (_lock)
        {
            int finalRow = _notificationPanelRow + MaxNotifications + 2;
            Console.SetCursorPosition(0, finalRow);
            Console.CursorVisible = true;
            Console.ResetColor();
        }
    }

    // Segédmetódusok

    private static void RedrawPanel(
        int panelRow,
        List<string> lines,
        int maxLines,
        Func<string, ConsoleColor> colorPicker)
    {
        int origRow = Console.CursorTop;
        int origCol = Console.CursorLeft;

        for (int i = 0; i < maxLines; i++)
        {
            int row = panelRow + i;
            if (row < 0 || row >= Console.WindowHeight) continue;
            Console.SetCursorPosition(0, panelRow + i);

            if (i < lines.Count)
            {
                Console.ForegroundColor = colorPicker(lines[i]);
                Console.Write(PadRight(lines[i], Console.WindowWidth - 1));
                Console.ResetColor();
            }
            else
            {
                Console.Write(new string(' ', Console.WindowWidth - 1));
            }
        }

        Console.SetCursorPosition(origCol, origRow);
    }

    private static void Overwrite(int row, string text)
    {
        if (row < 0 || row >= Console.WindowHeight) return;

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
