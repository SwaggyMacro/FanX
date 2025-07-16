using System.ComponentModel.DataAnnotations;

namespace FanX.Models.DTO
{
    public class LoginDto
    {
        [Required(ErrorMessage = "Username cannot be empty")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password cannot be empty")]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}