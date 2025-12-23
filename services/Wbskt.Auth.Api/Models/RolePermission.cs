using System.ComponentModel.DataAnnotations.Schema;

namespace Wbskt.Auth.Api.Models;

public class RolePermission
{
    public string RoleId { get; set; } = string.Empty;
    public int PermissionId { get; set; }

    [ForeignKey(nameof(PermissionId))]
    public Permission Permission { get; set; } = null!;
}
