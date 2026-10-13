using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartInventory.Authorization;
using SmartInventory.Models;

namespace SmartInventory.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        // Корень: авторизован → дашборд продукта, иначе → вход
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Inventory");
        }

        return RedirectToAction("Login", "Account");
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