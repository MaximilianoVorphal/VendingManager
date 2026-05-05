using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using VendingManager.Web.Middleware;
using Xunit;

namespace VendingManager.Tests.Middleware;

public class GlobalProblemDetailsMiddlewareTests
{
    private readonly Mock<ILogger<GlobalProblemDetailsMiddleware>> _loggerMock;
    private readonly Mock<IHostEnvironment> _envMock;

    public GlobalProblemDetailsMiddlewareTests()
    {
        _loggerMock = new Mock<ILogger<GlobalProblemDetailsMiddleware>>();
        _envMock = new Mock<IHostEnvironment>();
    }

    private GlobalProblemDetailsMiddleware CreateMiddleware(RequestDelegate next, bool isProduction = false)
    {
        _envMock.Setup(e => e.EnvironmentName).Returns(isProduction ? "Production" : "Development");
        return new GlobalProblemDetailsMiddleware(next, _envMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsArgumentException_Returns400WithRfc7807()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new ArgumentException("Month must be between 1 and 12."));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        context.Response.ContentType.Should().Be("application/problem+json; charset=utf-8");
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsUnauthorizedAccessException_Returns401()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new UnauthorizedAccessException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsKeyNotFoundException_Returns404()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new KeyNotFoundException("Producto with Id=99 not found."));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsInvalidOperationException_Returns409()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Caja is already closed for this period."));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsForbiddenAccessException_Returns403()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new ForbiddenAccessException());

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrowsGenericException_Returns500()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(_ => throw new Exception("Something went wrong"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextSucceeds_DoesNotWriteProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var nextCalled = false;

        var middleware = CreateMiddleware(c =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_SuppressesDetailInProduction()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = CreateMiddleware(
            _ => throw new ArgumentException("Internal detail: stack trace here"),
            isProduction: true);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_IncludesTraceIdInResponse()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.TraceIdentifier = "test-trace-id-123";

        var middleware = CreateMiddleware(_ => throw new Exception("boom"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
    }
}
