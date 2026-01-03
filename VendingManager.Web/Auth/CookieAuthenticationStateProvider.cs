using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace VendingManager.Web.Auth
{
    public class CookieAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;
        private readonly PersistentComponentState _state;
        private bool _isInitialized = false;
        private Task<AuthenticationState>? _authenticationStateTask;

        public CookieAuthenticationStateProvider(HttpClient httpClient, PersistentComponentState state)
        {
            _httpClient = httpClient;
            _state = state;

            if (_state.TryTakeFromJson<UserInfo>("UserInfo", out var userInfo) && userInfo != null)
            {
                var user = CreateUser(userInfo.Name);
                _authenticationStateTask = Task.FromResult(new AuthenticationState(user));
            }
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            if (_authenticationStateTask != null)
            {
                return await _authenticationStateTask;
            }

            var emptyUser = new ClaimsPrincipal(new ClaimsIdentity());
            try
            {
                var userInfo = await _httpClient.GetFromJsonAsync<UserInfo>("api/account/user");
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.Name))
                {
                    var user = CreateUser(userInfo.Name);
                    return new AuthenticationState(user);
                }
            }
            catch
            {
                // User is not logged in or error checking
            }

            return new AuthenticationState(emptyUser);
        }

        private ClaimsPrincipal CreateUser(string name)
        {
            var claims = new[] { new Claim(ClaimTypes.Name, name) };
            var identity = new ClaimsIdentity(claims, "Cookies");
            return new ClaimsPrincipal(identity);
        }

        public void NotifyAuthenticationStateChanged()
        {
            _authenticationStateTask = null; // Reset cached task
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }

    public class UserInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
