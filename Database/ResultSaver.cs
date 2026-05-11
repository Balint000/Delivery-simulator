using DeliverySimulator.Database.Models;
using DeliverySimulator.Services;

namespace DeliverySimulator.Database;

/// <summary>
/// Szimuláció végeredményének mentése DB-be.
/// </summary>
public static class ResultSaver
{
    public static async Task SaveAsync(
        AppDbContext db,
        int cityId,
        SimResult result,
        List<Order> orders,
        List<Courier> couriers)
    {
        // ── 1. Összesített futás rekord ──────────────────
        var run = new SimulationRun
        {
            CityId = cityId,
            RunAt = DateTime.Now,
            Total = result.Total,
            Delivered = result.Delivered,
            Late = result.Late,
            Unassigned = result.Unassigned,
            ElapsedSeconds = result.Elapsed.TotalSeconds
        };

        db.SimulationRuns.Add(run);
        await db.SaveChangesAsync(); // kell a run.Id-hez a logokhoz

        // ── 2. Per-rendelés napló ────────────────────────
        var courierMap = couriers.ToDictionary(c => c.Id);

        foreach (var order in orders)
        {
            string? courierName = order.AssignedCourierId.HasValue
                ? courierMap.GetValueOrDefault(order.AssignedCourierId.Value)?.Name
                : null;

            db.DeliveryLogs.Add(new DeliveryLog
            {
                SimRunId = run.Id,
                OrderNumber = order.Number,
                Customer = order.Customer,
                CourierId = order.AssignedCourierId,
                CourierName = courierName,
                WasDelivered = order.Status == OrderStatus.Delivered,
                WasLate = order.WasLate,
                IdealMinutes = order.IdealMinutes,
                ActualMinutes = order.ActualMinutes
            });
        }

        await db.SaveChangesAsync();
    }
}
