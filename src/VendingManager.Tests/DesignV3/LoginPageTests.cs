using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Tests.Services;
using VendingManager.Web.Pages;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class LoginPageTests : TestContext
{
    private readonly MockHttpMessageHandler _mockHandler;

    public LoginPageTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthStateProvider());
    }

    [Fact]
    public void Login_RendersVendingWordmarkAndInputsAndSubmit()
    {
        var cut = RenderComponent<Login>();

        cut.Markup.Should().Contain("VENDING");

        var inputs = cut.FindAll("input");
        inputs.Count(i =>
        {
            var type = i.GetAttribute("type");
            return type is "text" or "password";
        }).Should().Be(2);

        var submitButton = cut.Find("button[type=\"submit\"]");
        submitButton.TextContent.Should().Contain("Ingresar");
    }

    [Fact]
    public void Login_RendersIndustrialCardBorder()
    {
        var cut = RenderComponent<Login>();

        cut.Markup.Should().Contain("var(--shadow-card)");
        cut.Markup.Should().Contain("border-radius:var(--radius-0)");
    }

    [Fact]
    public void Login_ErrorRendersDangerCard()
    {
        _mockHandler.SetDefaultResponse(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var cut = RenderComponent<Login>();

        cut.Find("input[type=\"text\"]").Input("usuario");
        cut.Find("input[type=\"password\"]").Input("clave");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("Usuario o contraseña incorrectos."));

        cut.Markup.Should().Contain("var(--signal-danger)");
    }

    private class TestAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "test") },
                "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
