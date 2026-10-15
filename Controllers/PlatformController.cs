using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;

namespace SmartInventory.Controllers;

/// <summary>
/// Служебный раздел платформы: заявки с лендинга.
/// Доступ — только PlatformOwner (код HQ000001). В меню не добавляется.
/// </summary>
[AuthorizeRole(AppRole.PlatformOwner)]
[Route("Platform")]
public sealed class PlatformController(ApplicationDbContext db) : Controller
{
    // GET /Platform/Leads — таблица заявок
    [HttpGet("Leads")]
    public async Task<IActionResult> Leads()
    {
        var leads = await db.LeadRequests.AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        ViewBag.Leads = leads;
        return View();
    }

    // POST /Platform/Leads/Status — сменить статус заявки
    [HttpPost("Leads/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, string status)
    {
        var lead = await db.LeadRequests.FirstOrDefaultAsync(l => l.Id == id);
        if (lead is null) return NotFound();
        var allowed = new[] { "новая", "в работе", "доступ выдан" };
        if (!allowed.Contains(status)) status = "новая";
        lead.Status = status;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Leads));
    }
}
