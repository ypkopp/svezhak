using SmartInventory.Data;
using SmartInventory.Models.Organizations;
using SmartInventory.Models.Structure;

namespace SmartInventory.Extensions;

public static class OrganizationExtensions
{
    /// <summary>
    /// Синхронизирует коллекцию зданий организации на основе данных из ViewModel
    /// </summary>
    public static void UpdateBuildings(this Organization organization, List<BuildingEditViewModel> incomingBuildings, ApplicationDbContext context)
    {
        var incomingIds = incomingBuildings.Select(b => b.Id).ToList();

        // 1. Удаляем те, которых больше нет во входящих данных
        var toRemove = organization.Buildings
            .Where(b => !incomingIds.Contains(b.Id))
            .ToList();
        
        context.Buildings.RemoveRange(toRemove);

        // 2. Добавляем новые или обновляем существующие
        foreach (var bModel in incomingBuildings)
        {
            if (bModel.Id == 0)
            {
                // Новое здание
                organization.Buildings.Add(new Building
                {
                    Address = bModel.Address,
                    ManagerId = bModel.ManagerId,
                    OrganizationId = organization.Id
                });
            }
            else
            {
                // Существующее здание — обновляем поля
                var building = organization.Buildings.FirstOrDefault(b => b.Id == bModel.Id);
                if (building != null)
                {
                    building.Address = bModel.Address;
                    building.ManagerId = bModel.ManagerId;
                }
            }
        }
    }

    /// <summary>
    /// Синхронизирует коллекцию кодов доступа организации на основе данных из ViewModel
    /// </summary>
    public static void UpdateAccessCodes(this Organization organization, List<AccessCodeEditViewModel> incomingCodes, ApplicationDbContext context)
    {
        var incomingIds = incomingCodes.Select(a => a.Id).ToList();

        // 1. Удаляем отсутствующие
        var toRemove = organization.AccessCodes
            .Where(a => !incomingIds.Contains(a.Id))
            .ToList();
        
        context.AccessCodes.RemoveRange(toRemove);

        // 2. Добавляем или обновляем
        foreach (var cModel in incomingCodes)
        {
            if (cModel.Id == 0)
            {
                organization.AccessCodes.Add(new AccessCode(cModel.Code, cModel.Role, organization.Id)
                {
                    IsActive = cModel.IsActive,
                    IsActivated = cModel.IsActivated,
                    EmployeeName = cModel.EmployeeName
                });
            }
            else
            {
                // Существующий код — обновляем поля
                var code = organization.AccessCodes.FirstOrDefault(a => a.Id == cModel.Id);
                if (code != null)
                {
                    code.Role = cModel.Role;
                    code.IsActive = cModel.IsActive;
                    code.IsActivated = cModel.IsActivated;
                    code.EmployeeName = cModel.EmployeeName;
                }
            }
        }
    }
}