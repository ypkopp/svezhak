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
    public IActionResult Login(string? returnUrl = null)
    {
        // Уже вошли — сразу в продукт, а не на форму логина
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Inventory");
        }

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost("Login")]
    public async Task<IActionResult> Login(string code, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            ViewBag.Error = "Введите код доступа.";
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        var accessCode = await db.AccessCodes
            .FirstOrDefaultAsync(c => c.Code == code);

        if (accessCode == null || !accessCode.IsActive)
        {
            ViewBag.Error = "Неверный код доступа или доступ заблокирован.";
            ViewBag.ReturnUrl = returnUrl;
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
            new(ClaimTypes.Role, accessCode.Role.ToString()),
            // Идентификаторы разделены: код доступа, организация и субъект — разные клеймы
            new("AccessCodeId", accessCode.Id.ToString()),
            new("OrganizationId", accessCode.OrganizationId.ToString()),
            new(ClaimTypes.NameIdentifier, accessCode.Id.ToString()),
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(claimsIdentity));

        // Возвращаемся туда, откуда выкинуло на логин; по умолчанию — дашборд
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Inventory");
    }

    [HttpPost("Logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", "Account");
    }
}
