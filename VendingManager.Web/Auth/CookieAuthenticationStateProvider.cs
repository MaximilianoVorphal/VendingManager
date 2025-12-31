using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace VendingManager.Web.Auth
{
    public class CookieAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly HttpClient _httpClient;

        public CookieAuthenticationStateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity());

            try
            {
                var userInfo = await _httpClient.GetFromJsonAsync<UserInfo>("api/account/user");
                if (userInfo != null && !string.IsNullOrEmpty(userInfo.Name))
                {
                    var claims = new[] { new Claim(ClaimTypes.Name, userInfo.Name) };
                    var identity = new ClaimsIdentity(claims, "Cookies");
                    user = new ClaimsPrincipal(identity);
                }
            }
            catch
            {
                // User is not logged in or error checking
            }

            return new AuthenticationState(user);
        }

        public void NotifyAuthenticationStateChanged()
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
    }

    public class UserInfo
    {
        public string Name { get; set; }
    }
}
