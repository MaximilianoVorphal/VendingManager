using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Services;
using VendingManager.Shared.DTOs;
using Xunit;

namespace VendingManager.Tests.Services;

public class AutomatedReportServiceTests
{
    [Fact]
    public async Task RunDownloadProcessAsync_CallsSincronizarUnaSolaVez()
    {
        // Arrange
        var syncMock = new Mock<ISyncOrchestratorService>();
        syncMock
            .Setup(s => s.SincronizarDesdePortal(It.IsAny<int>(), It.IsAny<DateTime?>()))
            .ReturnsAsync("OK");

        var ventasMock = new Mock<IVentasService>();
        ventasMock
            .Setup(v => v.GetMaquinasAsync())
            .ReturnsAsync(new List<MaquinaSimpleDto>
            {
                new() { Id = 1, Nombre = "Máquina A" },
                new() { Id = 2, Nombre = "Máquina B" },
                new() { Id = 3, Nombre = "Máquina C" }
            });

        var scopeMock = new Mock<IServiceScope>();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        var providerMock = new Mock<IServiceProvider>();

        var scopedProviderMock = new Mock<IServiceProvider>();
        scopedProviderMock.Setup(p => p.GetService(typeof(ISyncOrchestratorService))).Returns(syncMock.Object);
        scopedProviderMock.Setup(p => p.GetService(typeof(IVentasService))).Returns(ventasMock.Object);

        scopeMock.Setup(s => s.ServiceProvider).Returns(scopedProviderMock.Object);
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
        providerMock.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactoryMock.Object);

        var logger = Mock.Of<ILogger<AutomatedReportService>>();
        var config = Mock.Of<IConfiguration>();
        var tracker = new LastSyncTracker();

        var service = new AutomatedReportService(logger, null!, config, providerMock.Object, tracker);

        // Act — invoke private RunDownloadProcessAsync via reflection
        var method = typeof(AutomatedReportService)
            .GetMethod("RunDownloadProcessAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(service, null)!;

        // Assert — SincronizarDesdePortal should be called exactly once with maquinaId=0
        syncMock.Verify(
            s => s.SincronizarDesdePortal(0, null),
            Times.Once,
            "SincronizarDesdePortal should be called once with maquinaId=0, not once per machine");
    }
}
