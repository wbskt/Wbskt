using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Api.Models;

public class Permission
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Description { get; set; }
}
