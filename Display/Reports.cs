using DeliverySimulator.Models;
using DeliverySimulator.Services;

namespace DeliverySimulator.Display;

public static class Reports
{
    // ── 1. Késési riport ─────────────────────────────────
    // Késett rendelések listája, késés szerint csökkentő sorrendben.
    public static void PrintDelayReport(List<Order> orders, List<Courier> couriers)
    {
        Console.WriteLine();
        Header("KÉSÉSI RIPORT");

        var late = orders
            .Where(o => o.WasLate && o.Status == OrderStatus.Delivered)
            .OrderByDescending(o => o.LateMinutes)
            .ToList();

        int delivered = orders.Count(o => o.Status == OrderStatus.Delivered);
        double rate   = delivered > 0 ? (double)late.Count / delivered * 100 : 0;

        if (late.Count == 0)
        {
            Green("  Minden rendelés idöben érkezett!");
            return;
        }

        Yellow($"  Késett: {late.Count} / {delivered} ({rate:F1}%)");
        Console.WriteLine();

        Gray($"  {"Rendelés",-12} | {"Ügyfél",-20} | {"Zóna",4} | {"Késés",8} | {"Futár",-18}");
        Gray($"  {new string('-', 78)}");

        foreach (var o in late)
        {
            string courierName = couriers.FirstOrDefault(c => c.Id == o.AssignedCourierId)?.Name ?? "—";
            Console.ForegroundColor = o.LateMinutes >= 5 ? ConsoleColor.Red : ConsoleColor.Yellow;
            Console.WriteLine($"  {o.Number,-12} | {o.Customer,-20} | {o.ZoneId,4} | +{o.LateMinutes,6}p | {courierName,-18}");
            Console.ResetColor();
        }
    }

    // ── 2. Futár teljesítmény rangsor ────────────────────
    // Rendezés: legtöbb kézbesítés, legkevesebb késés, leggyorsabb átlag.
    public static void PrintCourierReport(List<Courier> couriers)
    {
        Console.WriteLine();
        Header("FUTÁR TELJESÍTMÉNY RANGSOR");

        var ranked = couriers
            .OrderByDescending(c => c.DeliveriesCompleted)
            .ThenBy(c => c.LateDeliveries)
            .ThenBy(c => c.AvgTime)
            .ToList();

        Gray($"  {"Rang",4} | {"Futár",-20} | {"Zónák",-8} | {"Kézb.",5} | {"Késés",5} | {"Átlag idő",9}");
        Gray($"  {new string('-', 68)}");

        for (int i = 0; i < ranked.Count; i++)
        {
            var c = ranked[i];
            string zones = string.Join(",", c.ZoneIds);

            Console.ForegroundColor = i == 0 ? ConsoleColor.Yellow
                                    : c.LateDeliveries > 0 ? ConsoleColor.DarkYellow
                                    : ConsoleColor.Gray;
            Console.Write($"  {i + 1,4} | {c.Name,-20} | {zones,-8} | {c.DeliveriesCompleted,5} | ");

            Console.ForegroundColor = c.LateDeliveries > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            Console.Write($"{c.LateDeliveries,5}");

            Console.ForegroundColor = i == 0 ? ConsoleColor.Yellow : ConsoleColor.Gray;
            Console.WriteLine($" | {c.AvgTime,7:F1}p");
            Console.ResetColor();
        }

        if (ranked.Count >= 2)
        {
            Console.WriteLine();
            var best  = ranked.First();
            var worst = ranked.Last();
            Green ($"  Legjobb:      {best.Name} ({best.DeliveriesCompleted} kézb., {best.AvgTime:F1}p)");
            Yellow($"  Fejlesztendő: {worst.Name} ({worst.LateDeliveries} késés, {worst.AvgTime:F1}p)");
        }
    }

    // ── 3. Zónánkénti terhelés ────────────────────────────
    // Zónánkénti statisztika: rendelések száma, hatékonyság, futárok.
    public static void PrintZoneReport(List<Order> orders, List<Courier> couriers)
    {
        Console.WriteLine();
        Header("ZÓNÁNKÉNTI TERHELÉS");

        var zoneIds = orders.Select(o => o.ZoneId).Distinct().OrderBy(z => z).ToList();

        Gray($"  {"Zóna",5} | {"Összes",7} | {"Kézb.",6} | {"Késett",7} | {"Hatékony.",10} | Futárok");
        Gray($"  {new string('-', 70)}");

        var stats = new List<(int Zone, int Total)>();

        foreach (int z in zoneIds)
        {
            var zo    = orders.Where(o => o.ZoneId == z).ToList();
            int total = zo.Count;
            int del   = zo.Count(o => o.Status == OrderStatus.Delivered);
            int late  = zo.Count(o => o.WasLate);
            double eff = total > 0 ? (double)del / total * 100 : 0;
            stats.Add((z, total));

            string cNames = string.Join(", ",
                couriers.Where(c => c.CanServe(z)).Select(c => c.Name.Split(' ')[0]));

            Console.ForegroundColor = eff >= 100 ? ConsoleColor.Green
                                    : eff >= 80  ? ConsoleColor.Yellow
                                    : ConsoleColor.Red;
            Console.Write($"  {z,5} | {total,7} | {del,6} | ");

            Console.ForegroundColor = late > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            Console.Write($"{late,7}");

            Console.ForegroundColor = eff >= 100 ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.Write($" | {eff,9:F1}%");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine($" | {cNames}");
            Console.ResetColor();
        }

        if (stats.Count >= 2)
        {
            var most  = stats.MaxBy(s => s.Total);
            var least = stats.MinBy(s => s.Total);
            Console.WriteLine();
            Yellow($"  Legterheltebb: Zóna {most.Zone} ({most.Total} rendelés)");
            Green ($"  Legkevésbé:    Zóna {least.Zone} ({least.Total} rendelés)");
        }
    }

    private static void Header(string title)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"--- {title} ---");
        Console.ResetColor();
    }

    private static void Green (string s) { Console.ForegroundColor = ConsoleColor.Green;    Console.WriteLine(s); Console.ResetColor(); }
    private static void Yellow(string s) { Console.ForegroundColor = ConsoleColor.Yellow;   Console.WriteLine(s); Console.ResetColor(); }
    private static void Gray  (string s) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(s); Console.ResetColor(); }
}
