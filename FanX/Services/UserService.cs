using FanX.Models;
using FanX.Models.DTO;
using Microsoft.AspNetCore.Components.Authorization;

namespace FanX.Services
{
    public class UserService
    {
        private readonly DatabaseService _databaseService;
        private readonly CustomAuthenticationStateProvider _authenticationStateProvider;

        public UserService(DatabaseService databaseService, AuthenticationStateProvider authenticationStateProvider)
        {
            _databaseService = databaseService;
            _authenticationStateProvider = (CustomAuthenticationStateProvider)authenticationStateProvider;
        }
        
        public async Task<string?> LoginAsync(LoginDto loginDto)
        {
            LoggerService.Info($"Attempting to log in user: {loginDto.Username}");
            var user = await _databaseService.Db.Queryable<User>().FirstAsync(u => u.Username == loginDto.Username && u.IsActive);

            if (user != null && BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                var token = Guid.NewGuid().ToString();
                user.SessionToken = token;
                user.SessionExpiresAt = DateTime.UtcNow.AddDays(30);
                await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
                LoggerService.Info($"User '{loginDto.Username}' logged in successfully.");
                return token;
            }
            
            LoggerService.Warn($"Failed login attempt for user: {loginDto.Username}");
            return null;
        }

        public async Task LogoutAsync(string? token)
        {
            if (string.IsNullOrEmpty(token)) return;

            var user = await _databaseService.Db.Queryable<User>().FirstAsync(u => u.SessionToken == token);
            if (user != null)
            {
                LoggerService.Info($"User '{user.Username}' logging out.");
                user.SessionToken = null;
                user.SessionExpiresAt = null;
                await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
            }
        }

        public async Task<User?> ValidateTokenAsync(string? token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            return await _databaseService.Db.Queryable<User>()
                .FirstAsync(u => u.SessionToken == token && u.SessionExpiresAt > DateTime.UtcNow);
        }

        public async Task<User?> ValidateUserAsync(string username, string password)
        {
            try
            {
                // 检查用户是否存在
                var user = await _databaseService.Db.Queryable<User>()
                    .Where(u => u.Username == username && u.IsActive)
                    .FirstAsync();

                if (user == null)
                {
                    LoggerService.Warn($"User '{username}' not found or inactive.");
                    return null;
                }

                // 验证密码
                var isValidPassword = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                LoggerService.Info($"Password validation result for user '{username}': {isValidPassword}");
                
                if (isValidPassword)
                {
                    LoggerService.Info($"User '{username}' authentication succeeded.");
                    return user;
                }

                LoggerService.Warn($"User '{username}' authentication failed: invalid password.");
                return null;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"Error validating user '{username}'.", ex);
                return null;
            }
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            return await _databaseService.Db.Queryable<User>()
                .Where(u => u.Id == id)
                .FirstAsync();
        }

        public async Task<bool> RegisterUserAsync(RegisterDto registerDto)
        {
            LoggerService.Info($"Attempting to register new user: {registerDto.Username}");
            if (await _databaseService.Db.Queryable<User>().AnyAsync(u => u.Username == registerDto.Username))
            {
                LoggerService.Warn($"Registration failed for user '{registerDto.Username}': Username already exists.");
                return false;
            }

            try
            {
                var user = new User
                {
                    Username = registerDto.Username,
                    Email = registerDto.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password),
                    Role = "User",
                    CreatedAt = DateTime.Now,
                    IsActive = true
                };

                await _databaseService.Db.Insertable(user).ExecuteCommandAsync();
                LoggerService.Info($"User '{registerDto.Username}' registered successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LoggerService.Error($"An error occurred during registration for user '{registerDto.Username}'.", ex);
                return false;
            }
        }

        public async Task<bool> ChangePasswordAsync(string? token, string oldPassword, string newPassword)
        {
            var user = await ValidateTokenAsync(token);
            if (user == null)
            {
                LoggerService.Warn("Change password failed: invalid or expired session.");
                return false;
            }
            if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.PasswordHash))
            {
                LoggerService.Warn($"Change password failed for user '{user.Username}': old password mismatch.");
                return false;
            }
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
            LoggerService.Info($"User '{user.Username}' changed password successfully.");
            return true;
        }

        public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
        {
            var user = await _databaseService.Db.Queryable<User>()
                .Where(u => u.Id == userId)
                .FirstAsync();
            if (user == null) return false;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
            LoggerService.Info($"Admin reset password for user '{user.Username}'.");
            return true;
        }

        // Admin user management methods
        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _databaseService.Db.Queryable<User>().ToListAsync();
        }

        public async Task<bool> UpdateUserRoleAsync(int userId, string newRole)
        {
            var user = await _databaseService.Db.Queryable<User>().Where(u => u.Id == userId).FirstAsync();
            if (user == null) return false;
            user.Role = newRole;
            await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
            LoggerService.Info($"User '{user.Username}' role changed to {newRole}.");
            return true;
        }

        public async Task<bool> SetUserActiveAsync(int userId, bool isActive)
        {
            var user = await _databaseService.Db.Queryable<User>().Where(u => u.Id == userId).FirstAsync();
            if (user == null) return false;
            user.IsActive = isActive;
            await _databaseService.Db.Updateable(user).ExecuteCommandAsync();
            LoggerService.Info($"User '{user.Username}' active set to {isActive}.");
            return true;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            var result = await _databaseService.Db.Deleteable<User>().Where(u => u.Id == userId).ExecuteCommandAsync();
            return result > 0;
        }
    }
}