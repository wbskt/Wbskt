using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Wbskt.Auth.Api.Models;

public class RolePermission
{
    [MaxLength(100)]
    public string RoleId { get; set; } = string.Empty;

    public int PermissionId { get; set; }

    [ForeignKey(nameof(PermissionId))]
    public Permission Permission { get; set; } = null!;
} 
