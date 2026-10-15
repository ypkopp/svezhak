using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SmartInventory.Controllers;

/// <summary>
/// /Docs — «Как это работает»: гид по системе для жюри и пользователей.
/// Доступна всем авторизованным ролям.
/// </summary>
[Authorize]
[Route("Docs")]
public sealed class DocsController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
