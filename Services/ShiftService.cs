using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Расписание смен: что показывает виджет «Смена» в сайдбаре.
/// Определяет активную смену на «сейчас» для ресторана пользователя.
/// </summary>
public sealed class ShiftService(ApplicationDbContext db)
{
    /// <summary>Активная прямо сейчас смена ресторана (или null, если никого нет).</summary>
    public async Task<CurrentShift?> GetCurrentAsync(int restaurantId, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);

        var shifts = await db.Shifts.AsNoTracking()
            .Include(s => s.AccessCode)
            .Where(s => s.RestaurantId == restaurantId && s.Date == today
                        && s.StartTime <= time && s.EndTime >= time)
            .OrderBy(s => s.StartTime)
            .ToListAsync(ct);

        if (shifts.Count == 0) return null;

        // Заголовок виджета — шеф на смене, иначе первый по времени начала
        var lead = shifts.FirstOrDefault(s => s.Position == "шеф") ?? shifts[0];
        return new CurrentShift(
            Name: lead.AccessCode?.EmployeeName ?? "Сотрудник",
            Position: lead.Position,
            EndTime: lead.EndTime,
            OnDutyCount: shifts.Count);
    }

    /// <summary>Есть ли вообще смены, запланированные у ресторана (для подсказки админу).</summary>
    public Task<bool> HasAnyAsync(int restaurantId, CancellationToken ct = default) =>
        db.Shifts.AsNoTracking().AnyAsync(s => s.RestaurantId == restaurantId, ct);
}

public sealed record CurrentShift(string Name, string Position, TimeOnly EndTime, int OnDutyCount);
