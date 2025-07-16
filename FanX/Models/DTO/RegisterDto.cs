using System.ComponentModel.DataAnnotations;

namespace FanX.Models.DTO
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Username cannot be empty")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "username length must be between 3 and 50 characters")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "email cannot be empty")]
        [EmailAddress(ErrorMessage = "email format is not valid")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "password cannot be empty")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "password length must be between 6 and 100 characters")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "confirm password cannot be empty")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}