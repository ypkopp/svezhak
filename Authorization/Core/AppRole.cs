namespace SmartInventory.Authorization;

public enum AppRole
{
    Staff,
    Manager,
    HeadChef,
    RestaurantManager,
    OrganizationOwner,
    ServerAdmin,
    Admin,
    PlatformOwner,
}

public static class AppRoleExtensions
{
    public static int GetHierarchyLevel(this AppRole role) => role switch
    {
        AppRole.Staff => 1,
        AppRole.Manager => 2,
        AppRole.HeadChef => 3,
        AppRole.RestaurantManager => 4,
        AppRole.OrganizationOwner => 5,
        AppRole.Admin => 6,
        AppRole.ServerAdmin => 7,
        AppRole.PlatformOwner => 8,
        _ => 0
    };

    public static bool IsRoleOptionDisabled(this AppRole currentUserRole, AppRole originalRole, AppRole targetRole)
    {
        int currentWeight = currentUserRole.GetHierarchyLevel();
        int originalWeight = originalRole.GetHierarchyLevel();
        int targetWeight = targetRole.GetHierarchyLevel();

        // ВАЖНО: Если проверяемая опция в точности равна текущей роли в базе, 
        // она ВСЕГДА должна быть активна (иначе при отправке формы без изменений получим validation error).
        if (targetWeight == originalWeight)
        {
            return false;
        }

        // 1. Если сотрудник в базе равен вам или выше вас по рангу — вы вообще не можете его менять
        if (originalWeight >= currentWeight)
        {
            return true;
        }

        // 2. Максимальный уровень роли, который можно назначить (на 1 шаг ниже вашего)
        int maxAssignableWeight = currentWeight - 1;
        if (targetWeight > maxAssignableWeight)
        {
            return true;
        }

        return false;
    }
}