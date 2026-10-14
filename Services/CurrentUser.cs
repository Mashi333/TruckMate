using System.Security.Claims;
using TruckMate.Domain;

namespace TruckMate.Services;

public static class CurrentUser
{
    public static int? Id(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static string DisplayName(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Name) ?? "System";

    public static string RoleName(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Role) ?? OperatorRole.Operator.ToString();

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.IsInRole(OperatorRole.Admin.ToString()) || user.IsInRole(OperatorRole.SuperAdmin.ToString());

    public static bool IsSuperAdmin(this ClaimsPrincipal user)
        => user.IsInRole(OperatorRole.SuperAdmin.ToString());

    public static string HomeController(this ClaimsPrincipal user)
    {
        if (user.IsInRole(OperatorRole.SuperAdmin.ToString())) return "SuperAdmin";
        if (user.IsInRole(OperatorRole.Admin.ToString())) return "Admin";
        return "Operator";
    }
}
