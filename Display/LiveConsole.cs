using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Display;

public class LiveConsole
{
    // ── Belső állapot ───────────────────────────────────

    // Mutex: megakadályozza, hogy két szál egyszerre írjon a konzolra
    private readonly object _lock = new();

    // Az eseménynapló sorai (max MaxEvents db)
    private readonly List<string> _events = [];

    // Az értesítési panel sorai (max MaxNotifications db)
    // külön lista a késési értesítéseknek
    private readonly List<string> _notifications = [];

    // Eseménynapló maximális sorszáma — ha betelik, a legrégebbi kiesik
    private const int MaxEvents = 10;

    // Értesítési panel maximális sorszáma — görgős, max 5 sor
    // ez határozza meg hány késési értesítés látható egyszerre
    private const int MaxNotifications = 5;

    // Hányadik konzolsorban kezdődik a futárpanel (Init() tölti ki)
    private int _courierPanelRow;

    // Hányadik konzolsorban kezdődik az eseménynapló (Init() tölti ki)
    private int _eventPanelRow;

    // Hányadik konzolsorban kezdődik az értesítési panel (Init() tölti ki)
    // külön pozíció az értesítések szekciójának
    private int _notificationPanelRow;

    // Hány futár van — az Init()-ben kapjuk meg, a sorok lefoglalásához kell
    private int _courierCount;

    // Igaz, ha az Init() már lefutott — a többi metódus csak ekkor ír a konzolra
    private bool _ready;

    // ── Inicializálás ───────────────────────────────────

    /// <summary>
    /// Képernyő törlése, fejléc + mindhárom panel megrajzolása,
    /// sor-pozíciók rögzítése.
    ///
    /// FONTOS: csak egyszer hívható, a szimuláció legelején!
    /// Ha többször hívnánk, a pozíciók összecsuknának.
    /// </summary>
    /// <param name="title">Fejlécben megjelenő cím</param>
    /// <param name="courierCount">Futárok száma — ennyi sort foglalunk le</param>
    public void Init(string title, int courierCount)
    {
        lock (_lock)
        {
            _courierCount = courierCount;
            Console.CursorVisible = false;
            Console.Clear();

            // ── Fejléc ──────────────────────────────────
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(title);
            Console.ResetColor();
            Console.WriteLine();

            // ── 1. Futárpanel fejléce ────────────────────
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("━━━ FUTÁROK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.ResetColor();

            // MOST rögzítjük — itt van a kurzor a futársorok első sora előtt
            _courierPanelRow = Console.CursorTop;

            for (int i = 0; i < courierCount; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();

            // ── 2. Eseménynapló fejléce ──────────────────
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("━━━ ESEMÉNYEK ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Console.ResetColor();

            // MOST rögzítjük az eseménynapló első sorát
            _eventPanelRow = Console.CursorTop;

            for (int i = 0; i < MaxEvents; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();

            // ── 3. Értesítési panel fejléce ──────────────
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
            Console.WriteLine("━━━ ÉRTESÍTÉSEK (késési figyelmeztetések) ━━━━━━━━━━━━━");
            Console.ResetColor();

            // MOST rögzítjük az értesítési panel első sorát
            _notificationPanelRow = Console.CursorTop;

            for (int i = 0; i < MaxNotifications; i++)
                Console.WriteLine(new string(' ', Console.WindowWidth - 1));

            Console.WriteLine();

            _ready = true;
        }
    }

    // ── Futár státusz frissítés ─────────────────────────

    /// <summary>
    /// Egy futár sorának frissítése a futárpanelban.
    ///
    /// Visszaugrik a megfelelő konzolsorba (Console.SetCursorPosition),
    /// felülírja a régi tartalmat, majd visszahelyezi a kurzort.
    ///
    /// Ez adja a "frissülési" hatást görgetés nélkül.
    /// </summary>
    /// <param name="courierId">Futár azonosítója (1-től indul)</param>
    /// <param name="name">Futár neve</param>
    /// <param name="status">Aktuális státusz szöveg (pl. "🚚 kézbesítés")</param>
    /// <param name="location">Jelenlegi helyszín neve</param>
    /// <param name="target">Következő célpont neve (null ha nincs)</param>
    /// <param name="completedCount">Eddig kézbesített rendelések száma</param>
    /// <param name="estimatedMin">Becsült menetidő percben (null ha ismeretlen)</param>
    public void UpdateCourier(
        int courierId,
        string name,
        string status,
        string location,
        string? target = null,
        int completedCount = 0,
        int? estimatedMin = null)
    {
        if (!_ready) return;

        lock (_lock)
        {
            // Ha van célpont, "jelenlegi → cél" formátum; különben csak a hely
            string loc = target != null
                ? $"{Clip(location, 14)} → {Clip(target, 14)}"
                : Clip(location, 30);

            // ETA szöveg: "~5p" ha ismert, különben üres szóközök
            string eta = estimatedMin.HasValue ? $"~{estimatedMin}p" : "     ";

            // Formázott sor: státusz | név | helyszín | ETA | kézbesítésszám
            string line = $"  {status,-25} │ {name,-30} │ {loc,-45} │ {eta,-8} │ {completedCount} kézb.";

            // courierId 1-től indul, de a panel 0-tól indexelt → ezért (-1)
            int row = _courierPanelRow + (courierId - 1);
            Overwrite(row, line);
        }
    }

    // ── Eseménynapló ────────────────────────────────────

    /// <summary>
    /// Új esemény hozzáadása az eseménynaplóhoz.
    ///
    /// GÖRGŐS: ha betelik (MaxEvents), a legrégebbi sor (_events[0]) kiesik,
    /// az új a végére kerül, majd az egész panel újrarajzolódik.
    /// </summary>
    /// <param name="type">Esemény típusa (meghatározza az ikont)</param>
    /// <param name="message">Esemény szövege</param>
    public void LogEvent(string type, string message)
    {
        if (!_ready) return;

        lock (_lock)
        {
            // Aktuális idő az esemény előtt
            string ts = DateTime.Now.ToString("HH:mm:ss");

            // Típusonkénti ikon a vizuális elkülönítéshez
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

            // Görgős lista: ha tele, a legrégebbi kiesik
            if (_events.Count >= MaxEvents) _events.RemoveAt(0);
            _events.Add(line);

            // Az egész eseménynapló-panel újrarajzolása.
            // A szín az esemény tartalmától függ (ikon alapján döntünk).
            RedrawPanel(_eventPanelRow, _events, MaxEvents,
                e => e.Contains("✅") ? ConsoleColor.Green
                   : e.Contains("⚠️") ? ConsoleColor.Yellow
                   : e.Contains("🏁") ? ConsoleColor.Cyan
                   : ConsoleColor.Gray);
        }
    }

    // ── Értesítési panel ────────────────────────────────

    /// <summary>
    /// Késési értesítés küldése a megrendelőnek — KÜLÖN panelban jelenik meg.
    ///
    /// ÚJ METÓDUS: elkülönített az eseménynaplótól.
    ///
    /// GÖRGŐS: ha betelik (MaxNotifications = 5), a legrégebbi kiesik,
    /// az új a végére kerül, majd az értesítési panel újrarajzolódik.
    ///
    /// Valós rendszerben: itt lenne az e-mail / SMS küldés logikája.
    /// </summary>
    /// <param name="customer">Ügyfél neve (megrendelő)</param>
    /// <param name="orderNumber">Rendelésszám (pl. "ORD-007")</param>
    /// <param name="lateMinutes">Várható késés percekben</param>
    public void LogNotification(string customer, string orderNumber, int lateMinutes)
    {
        if (!_ready) return;

        lock (_lock)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");

            // Formázott értesítési sor: idő | ügyfél | rendelésszám | késés mértéke
            string line = $"  [{ts}] 📬 {customer,-20} │ {orderNumber,-10} │ +{lateMinutes} perc késés várható";

            // Görgős lista: ha tele, a legrégebbi értesítés kiesik
            if (_notifications.Count >= MaxNotifications) _notifications.RemoveAt(0);
            _notifications.Add(line);

            // Az egész értesítési panel újrarajzolása — minden sor lila
            RedrawPanel(_notificationPanelRow, _notifications, MaxNotifications,
                _ => ConsoleColor.Magenta);
        }
    }

    // ── Szimuláció vége ─────────────────────────────────

    /// <summary>
    /// Kurzor visszaállítása a konzol aljára a szimuláció végén.
    ///
    /// Az összes panel után 2 sorral lejjebb helyezi a kurzort,
    /// hogy a riportok ne csússzanak bele a panelekbe.
    /// </summary>
    public void Finish()
    {
        lock (_lock)
        {
            // A legalsó panel (értesítések) utáni 2. sorba ugrunk.
            // ÚJ: _notificationPanelRow-tól számolunk (korábban _eventPanelRow volt)
            int finalRow = _notificationPanelRow + MaxNotifications + 2;
            Console.SetCursorPosition(0, finalRow);
            Console.CursorVisible = true;
            Console.ResetColor();
        }
    }

    // ── Segédmetódusok ──────────────────────────────────

    /// <summary>
    /// Egy görgős panel teljes újrarajzolása.
    ///
    /// Lépések:
    ///   1. Elmenti a kurzor aktuális pozícióját
    ///   2. Soronként felülírja a panel tartalmát
    ///   3. Visszahelyezi a kurzort az eredeti pozícióba
    /// </summary>
    /// <param name="panelRow">A panel kezdő sora a konzolon</param>
    /// <param name="lines">A megjelenítendő sorok listája</param>
    /// <param name="maxLines">A panel maximális sorszáma</param>
    /// <param name="colorPicker">Függvény, amely egy sorhoz színt rendel</param>
    private void RedrawPanel(
        int panelRow,
        List<string> lines,
        int maxLines,
        Func<string, ConsoleColor> colorPicker)
    {
        // Kurzor jelenlegi pozíciójának mentése — visszatérünk ide
        int origRow = Console.CursorTop;
        int origCol = Console.CursorLeft;

        for (int i = 0; i < maxLines; i++)
        {
            Console.SetCursorPosition(0, panelRow + i);

            if (i < lines.Count)
            {
                // Van tartalom: megjelenítjük a megfelelő színnel
                Console.ForegroundColor = colorPicker(lines[i]);
                Console.Write(PadRight(lines[i], Console.WindowWidth - 1));
                Console.ResetColor();
            }
            else
            {
                // Üres sor: szóközökkel töröljük a régi tartalmat
                Console.Write(new string(' ', Console.WindowWidth - 1));
            }
        }

        // Kurzor visszahelyezése az eredeti pozícióba
        Console.SetCursorPosition(origCol, origRow);
    }

    /// <summary>
    /// Szöveg kiírása adott színnel, majd szín visszaállítása.
    /// </summary>
    private static void Write(ConsoleColor color, string text)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    /// <summary>
    /// Egy konzolsor felülírása: visszaugrik a megadott sorra,
    /// kiírja a szöveget, majd visszahelyezi a kurzort.
    /// </summary>
    private static void Overwrite(int row, string text)
    {
        int origRow = Console.CursorTop;
        int origCol = Console.CursorLeft;
        Console.SetCursorPosition(0, row);
        Console.Write(PadRight(text, Console.WindowWidth - 1));
        Console.SetCursorPosition(origCol, origRow);
    }

    /// <summary>
    /// Szöveg levágása adott hosszra — ha hosszabb, ".." jelzi a csonkítást.
    /// Pl. "Keleti piac" → max 8 → "Keleti.."
    /// </summary>
    private static string Clip(string s, int max) =>
        s.Length <= max ? s : s[..(max - 2)] + "..";

    /// <summary>
    /// Szöveg kitöltése jobbra szóközökkel a megadott szélességig,
    /// vagy levágása ha hosszabb — konzolsor mindig pontosan w karakter.
    /// </summary>
    private static string PadRight(string s, int w) =>
        s.Length >= w ? s[..w] : s.PadRight(w);
}
