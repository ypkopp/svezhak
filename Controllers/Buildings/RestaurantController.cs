using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Models.Restaurants;

[AuthorizeRole(AppRole.ServerAdmin, AppRole.OrganizationOwner, AppRole.RestaurantManager)]
[Route("[controller]")]
public class RestaurantsController(
    IMapper mapper,
    ApplicationDbContext context,
    IAuthorizationService authorizationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userRoleString = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value!;
        var userRole = Enum.Parse<AppRole>(userRoleString);

        if (userRole == AppRole.ServerAdmin)
        {
            var restaurants = await context.Restaurants
                .Include(r => r.Organization)
                .ToListAsync();

            var viewModels = mapper.Map<List<RestaurantIndexViewModel>>(restaurants);
            return View(viewModels);
        }

        if (userRole == AppRole.OrganizationOwner)
        {
            var organizationIdClaim = User.Claims.FirstOrDefault(c => c.Type == "OrganizationId")?.Value;

            if (int.TryParse(organizationIdClaim, out var orgId))
            {
                var restaurants = await context.Restaurants
                    .Where(r => r.OrganizationId == orgId)
                    .Include(r => r.Organization)
                    .ToListAsync();

                var viewModels = mapper.Map<List<RestaurantIndexViewModel>>(restaurants);
                return View(viewModels);
            }

            return Forbid();
        }

        if (userRole == AppRole.RestaurantManager)
        {
            // ManagerId хранит Id access-кода менеджера — сверяем именно с AccessCodeId
            var accessCodeIdClaim = User.Claims.FirstOrDefault(c => c.Type == "AccessCodeId")?.Value;

            var restaurant = await context.Restaurants
                .Where(r => r.ManagerId == accessCodeIdClaim)
                .Include(r => r.Organization)
                .FirstOrDefaultAsync();

            if (restaurant != null)
            {
                return RedirectToAction(nameof(Dashboard), new { id = restaurant.Id });
            }
        }

        return Forbid();
    }

    [HttpGet("{id:int}")]
    [HttpGet("{id:int}/Dashboard")]
    public async Task<IActionResult> Dashboard(int id)
    {
        var restaurant = await context.Restaurants
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (restaurant == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, restaurant, new ManageRestaurantRequirement());
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var viewModel = mapper.Map<RestaurantIndexViewModel>(restaurant);

        return View(viewModel);
    }

    [HttpGet("{id:int}/Edit")]
    [AuthorizeRole(AppRole.ServerAdmin, AppRole.OrganizationOwner)]
    public async Task<IActionResult> Edit(int id)
    {
        // Handler обращается к resource.Organization.OwnerId, поэтому грузим с Include
        var restaurant = await context.Restaurants
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (restaurant == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, restaurant, new ManageRestaurantRequirement());
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var viewModel = mapper.Map<RestaurantEditViewModel>(restaurant);

        return View(viewModel);
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    [AuthorizeRole(AppRole.ServerAdmin, AppRole.OrganizationOwner)]
    public async Task<IActionResult> Edit(int id, RestaurantEditViewModel model)
    {
        if (id != model.Id) return NotFound();

        var restaurant = await context.Restaurants
            .Include(r => r.Organization)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (restaurant == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, restaurant, new ManageRestaurantRequirement());

        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Автоматический перенос данных из модели в сущность с помощью AutoMapper
            mapper.Map(model, restaurant);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!context.Restaurants.Any(e => e.Id == model.Id))
                return NotFound();

            throw;
        }

        return RedirectToAction(nameof(Dashboard), new { id = model.Id });
    }
}