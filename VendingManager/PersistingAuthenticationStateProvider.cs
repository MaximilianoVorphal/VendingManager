using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Claims;

namespace VendingManager.Web.Auth
{
    // This class is intended to be used on the SERVER project to persist the authentication state
    // so the WASM client can pick it up without an extra HTTP call.
    public class PersistingAuthenticationStateProvider : IDisposable
    {
        private readonly PersistentComponentState _state;
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly PersistingComponentStateSubscription _subscription;

        public PersistingAuthenticationStateProvider(
            PersistentComponentState state,
            AuthenticationStateProvider authenticationStateProvider)
        {
            _state = state;
            _authenticationStateProvider = authenticationStateProvider;
            _subscription = _state.RegisterOnPersisting(OnPersistingAsync, RenderMode.InteractiveWebAssembly);
        }

        private async Task OnPersistingAsync()
        {
            var authenticationState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authenticationState.User;

            if (principal.Identity?.IsAuthenticated == true)
            {
                var name = principal.FindFirst(ClaimTypes.Name)?.Value;
                if (name != null)
                {
                    _state.PersistAsJson("UserInfo", new UserInfo { Name = name });
                }
            }
        }

        public void Dispose()
        {
            _subscription.Dispose();
        }
    }
}
