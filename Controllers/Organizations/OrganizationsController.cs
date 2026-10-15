using System.Security.Claims;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Extensions;
using SmartInventory.Models.Organizations;
using SmartInventory.Models.Structure;

[AuthorizeRole(AppRole.ServerAdmin, AppRole.OrganizationOwner)]
[Route("[controller]")]
public class OrganizationsController(
    IMapper mapper,
    ApplicationDbContext context,
    IAuthorizationService authorizationService) : Controller
{
    [HttpGet]
    [AuthorizeRole(AppRole.ServerAdmin)]
    public async Task<IActionResult> Index()
    {
        var organizations = await context.Organizations
            .Include(o => o.Restaurants)
            .Include(o => o.AccessCodes)
            .AsSplitQuery()
            .ToListAsync();

        var viewModels = mapper.Map<List<OrganizationIndexViewModel>>(organizations);

        return View(viewModels);
    }

    [HttpGet("{id:int}")]
    [HttpGet("{id:int}/Details")]
    public async Task<IActionResult> Details(int id)
    {
        var organization = await context.Organizations
            .Include(o => o.Restaurants)
            .Include(o => o.AccessCodes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (organization == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, organization, new ManageOrganizationRequirement());
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        return View(organization);
    }

    [HttpGet("{id:int}/Edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var organization = await context.Organizations
            .Include(o => o.Restaurants)
            .Include(o => o.AccessCodes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (organization == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, organization, new ManageOrganizationRequirement());
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        var viewModel = mapper.Map<OrganizationEditViewModel>(organization);

        return View(viewModel);
    }

    [HttpPost("{id:int}/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OrganizationEditViewModel model)
    {
        if (id != model.Id) return NotFound();

        var organization = await context.Organizations
            .Include(o => o.Restaurants)
            .Include(o => o.AccessCodes)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (organization == null) return NotFound();

        var authResult =
            await authorizationService.AuthorizeAsync(User, organization, new ManageOrganizationRequirement());

        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        ValidModel(model, organization);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            organization.Name = model.Name;
            organization.OwnerId = model.OwnerId;

            organization.UpdateRestaurants(model.Restaurants, context);
            organization.UpdateAccessCodes(model.AccessCodes, context);

            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!context.Organizations.Any(e => e.Id == model.Id))
                return NotFound();

            throw;
        }

        return RedirectToAction(nameof(Details), new { id = model.Id });
    }

    private void ValidModel(OrganizationEditViewModel model, Organization organization)
    {
        var userRoleString = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value!;
        var userRole = Enum.Parse<AppRole>(userRoleString);

        if (userRole != AppRole.ServerAdmin && organization.OwnerId != model.OwnerId)
        {
            ModelState.AddModelError(nameof(model.OwnerId), "У вас нет прав на изменение владельца организации.");
        }

        foreach (var modelCode in model.AccessCodes)
        {
            var existingCode = organization.AccessCodes.FirstOrDefault(c => c.Id == modelCode.Id);

            if (existingCode == null)
                continue;

            if (userRole.IsRoleOptionDisabled(existingCode.Role, modelCode.Role))
            {
                ModelState.AddModelError("",
                    $"У вас недостаточно прав для изменения роли с '{existingCode.Role}' на '{modelCode.Role}'.");
            }

            if (modelCode.IsActive != existingCode.IsActive)
            {
                if (existingCode.Role.GetHierarchyLevel() >= userRole.GetHierarchyLevel())
                {
                    ModelState.AddModelError("",
                        $"У вас недостаточно прав для изменения статуса активности кода доступа сотрудника '{existingCode.EmployeeName ?? existingCode.Code}'.");
                }
            }
        }
    }
}