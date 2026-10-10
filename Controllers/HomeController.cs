using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartInventory.Authorization;
using SmartInventory.Models;

namespace SmartInventory.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
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

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}