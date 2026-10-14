using System.ComponentModel.DataAnnotations;

namespace TruckMate.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your username.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter your username.")]
    public string Username { get; set; } = string.Empty;
}
