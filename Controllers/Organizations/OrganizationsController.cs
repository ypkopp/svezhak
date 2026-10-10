using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Models.Account;
using SmartInventory.Models.Organizations;
using SmartInventory.Models.Structure;

[AuthorizeRole(AppRole.ServerAdmin)]
public class OrganizationsController(ApplicationDbContext context) : Controller
{
    public async Task<IActionResult> Index()
    {
        var organizations = await context.Organizations
            .Include(o => o.Buildings)
            .Include(o => o.AccessCodes)
            .ToListAsync();

        var viewModels = organizations.Select(o => new OrganizationIndexViewModel
        {
            Id = o.Id,
            Name = o.Name,
            OwnerId = o.OwnerId,
            BuildingsCount = o.Buildings.Count,
            AccessCodesCount = o.AccessCodes.Count
        }).ToList();

        return View(viewModels);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var organization = await context.Organizations
            .Include(o => o.Buildings)
            .Include(o => o.AccessCodes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (organization == null) return NotFound();
        
        var viewModel = new OrganizationDetailsViewModel
        {
            Id = organization.Id,
            Name = organization.Name,
            OwnerId = organization.OwnerId,
            Buildings = organization.Buildings.Select(b => new BuildingViewModel
            {
                Id = b.Id,
                Address = b.Address,
                ManagerId = b.ManagerId
            }).ToList(),
            AccessCodes = organization.AccessCodes.Select(a => new AccessCodeViewModel
            {
                Id = a.Id,
                Code = a.Code,
                RoleName = a.RoleName,
                IsActive = a.IsActive,
                IsActivated = a.IsActivated,
                EmployeeName = a.EmployeeName
            }).ToList()
        };

        return View(viewModel);
    }
    
    [HttpGet]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();

        var organization = await context.Organizations
            .Include(o => o.Buildings)
            .Include(o => o.AccessCodes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (organization == null) return NotFound();

        var viewModel = new OrganizationEditViewModel
        {
            Id = organization.Id,
            Name = organization.Name,
            OwnerId = organization.OwnerId,
            Buildings = organization.Buildings.Select(b => new BuildingEditViewModel
            {
                Id = b.Id,
                Address = b.Address,
                ManagerId = b.ManagerId
            }).ToList(),
            AccessCodes = organization.AccessCodes.Select(a => new AccessCodeEditViewModel
            {
                Id = a.Id,
                Code = a.Code,
                RoleName = a.RoleName,
                IsActive = a.IsActive,
                IsActivated = a.IsActivated,
                EmployeeName = a.EmployeeName
            }).ToList()
        };

        return View(viewModel);
    }
    
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, OrganizationEditViewModel model)
    {
        if (id != model.Id) return NotFound();
        
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var organization = await context.Organizations
                .Include(o => o.Buildings)
                .Include(o => o.AccessCodes)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (organization == null) return NotFound();
            
            organization.Name = model.Name;
            organization.OwnerId = model.OwnerId;
            
            var incomingBuildingIds = model.Buildings.Select(b => b.Id).ToList();
            var buildingsToRemove = organization.Buildings.Where(b => !incomingBuildingIds.Contains(b.Id)).ToList();
            context.Buildings.RemoveRange(buildingsToRemove);

            foreach (var bModel in model.Buildings)
            {
                if (bModel.Id == 0)
                {
                    organization.Buildings.Add(new Building
                    {
                        Address = bModel.Address,
                        ManagerId = bModel.ManagerId,
                        OrganizationId = organization.Id
                    });
                }
                else
                {
                    var building = organization.Buildings.FirstOrDefault(b => b.Id == bModel.Id);
                    if (building != null)
                    {
                        building.Address = bModel.Address;
                        building.ManagerId = bModel.ManagerId;
                    }
                }
            }
            
            var incomingCodeIds = model.AccessCodes.Select(a => a.Id).ToList();
            var codesToRemove = organization.AccessCodes.Where(a => !incomingCodeIds.Contains(a.Id)).ToList();
            context.AccessCodes.RemoveRange(codesToRemove);

            foreach (var cModel in model.AccessCodes)
            {
                if (cModel.Id == 0)
                {
                    organization.AccessCodes.Add(new AccessCode(cModel.Code, cModel.RoleName, organization.Id)
                    {
                        IsActive = cModel.IsActive,
                        IsActivated = cModel.IsActivated,
                        EmployeeName = cModel.EmployeeName
                    });
                }
                else
                {
                    var code = organization.AccessCodes.FirstOrDefault(a => a.Id == cModel.Id);
                    if (code != null)
                    {
                        code.RoleName = cModel.RoleName;
                        code.IsActive = cModel.IsActive;
                        code.IsActivated = cModel.IsActivated;
                        code.EmployeeName = cModel.EmployeeName;
                    }
                }
            }

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
}