using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Models;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Controllers;

public class HomeController(ApplicationDbContext db) : Controller
{
    // GET / — публичный лендинг для гостей; авторизованным — их раздел
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole(nameof(AppRole.PlatformOwner)))
                return RedirectToAction("Leads", "Platform");
            if (User.IsInRole(nameof(AppRole.OrganizationOwner)))
                return RedirectToAction("Index", "Org");
            return RedirectToAction("Index", "Inventory");
        }
        return View("Landing");
    }

    // POST / — заявка с лендинга (сохранить лид)
    [HttpPost("/")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Lead(string networkName, string contact, int restaurantCount)
    {
        if (string.IsNullOrWhiteSpace(networkName) || string.IsNullOrWhiteSpace(contact))
        {
            TempData["LeadError"] = "Укажите название сети и контакт.";
            return RedirectToAction(nameof(Index));
        }
        db.LeadRequests.Add(new LeadRequest
        {
            NetworkName = networkName.Trim(),
            Contact = contact.Trim(),
            RestaurantCount = Math.Max(1, restaurantCount),
        });
        await db.SaveChangesAsync();
        TempData["LeadOk"] = networkName.Trim();
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Privacy()
    {
        return View();
    }
    
    [AuthorizeRole(AppRole.ServerAdmin, AppRole.OrganizationOwner, AppRole.RestaurantManager)]
    public IActionResult BuildingSettings()
    {
        return View();
    }
    
    [Route("Home/Error")]
    public IActionResult Error(int statusCode)
    {
        if (statusCode == 404)
        {
            return View("NotFound");
        }
    
        return View("Error");
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}