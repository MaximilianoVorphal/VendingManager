using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using System.Security.Claims;

namespace VendingManager.Web.Auth;

// Este clase está pensada para ser usada en el PROYECTO SERVIDOR para persistir el estado de autenticación
// así el cliente WASM puede recuperarlo sin una llamada HTTP extra.
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
            var role = principal.FindFirst(ClaimTypes.Role)?.Value;

            if (name != null)
            {
                _state.PersistAsJson("UserInfo", new UserInfo { Name = name, Role = role ?? "User" });
            }
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
    }
}