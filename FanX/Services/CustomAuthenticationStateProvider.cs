using System.Security.Claims;
using FanX.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace FanX.Services
{
    public class CustomAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly BrowserStorageService _browserStorage;
        private readonly IServiceProvider _serviceProvider;
        private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

        public CustomAuthenticationStateProvider(BrowserStorageService browserStorage, IServiceProvider serviceProvider)
        {
            _browserStorage = browserStorage;
            _serviceProvider = serviceProvider;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var token = await _browserStorage.GetItemAsync("authToken");
            
            using var scope = _serviceProvider.CreateScope();
            var userService = scope.ServiceProvider.GetRequiredService<UserService>();
            
            var user = await userService.ValidateTokenAsync(token);

            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Name, user.Username),
                    new(ClaimTypes.Email, user.Email),
                    new(ClaimTypes.Role, user.Role ?? "User")
                };
                var identity = new ClaimsIdentity(claims, "Custom");
                _currentUser = new ClaimsPrincipal(identity);
            }
            else
            {
                _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
                await _browserStorage.RemoveItemAsync("authToken");
            }

            return new AuthenticationState(_currentUser);
        }

        public void MarkUserAsAuthenticated(User user)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Email, user.Email),
                new(ClaimTypes.Role, user.Role ?? "User")
            };

            var identity = new ClaimsIdentity(claims, "Custom");
            _currentUser = new ClaimsPrincipal(identity);

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        }

        public void MarkUserAsLoggedOut()
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
        }
    }
} 