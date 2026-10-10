using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using System.Security.Claims;

[Route("Account")]
public class AccountController(ApplicationDbContext db) : Controller
{
    [HttpGet("AccessDenied")]
    public IActionResult AccessDenied()
    {
        return View();
    }

    [HttpGet("Login")]
    public IActionResult Login()
    {
        return View();
    }

    [HttpPost("Login")]
    public async Task<IActionResult> Login(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ViewBag.Error = "Введите код доступа.";
            return View();
        }

        var accessCode = await db.AccessCodes
            .FirstOrDefaultAsync(c => c.Code == code);

        if (accessCode == null || !accessCode.IsActive)
        {
            ViewBag.Error = "Неверный код доступа или доступ заблокирован.";
            return View();
        }

        if (!accessCode.IsActivated)
        {
            accessCode.IsActivated = true;
            await db.SaveChangesAsync();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, accessCode.EmployeeName ?? $"User_{accessCode.Id}"),
            new(ClaimTypes.Role, accessCode.RoleName),
            new("AccessCodeId", accessCode.Id.ToString()),
            new(ClaimTypes.NameIdentifier, accessCode.Id.ToString()),
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity));

        return RedirectToAction("Index", "Home");
    }

    [HttpPost("Logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}