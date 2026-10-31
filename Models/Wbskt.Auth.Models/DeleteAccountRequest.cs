using System.ComponentModel.DataAnnotations;

namespace Wbskt.Auth.Models;

/// <summary>Deleting the signed-in account. The password is asked again, as for changing it.</summary>
public record DeleteAccountRequest(
    [Required]
    [StringLength(128)]
    string Password);
