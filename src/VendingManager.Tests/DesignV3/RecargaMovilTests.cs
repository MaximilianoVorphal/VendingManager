using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using VendingManager.Web.Components;
using VendingManager.Web.Pages;
using VendingManager.Web.Shared;
using Xunit;

namespace VendingManager.Tests.DesignV3;

public class RecargaMovilTests : TestContext
{
    private readonly RecargaMovilMockHttpMessageHandler _mockHandler;

    public RecargaMovilTests()
    {
        _mockHandler = new RecargaMovilMockHttpMessageHandler();
        Services.AddScoped(_ => new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("http://localhost")
        });
        Services.AddAuthorizationCore();
        Services.AddSingleton<AuthenticationStateProvider, AuthenticatedAuthStateProvider>();
        Services.AddSingleton<IAuthorizationService, FakeAuthorizationService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── Test 1: All Four Views Are Reachable ──────────────────────────

    [Fact]
    public void RecargaMovil_Renders_All_Four_Views()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        // Initial view: Lista
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Terreno · Recarga");
            cut.Markup.Should().Contain("Cargas");
        });

        // Click first template → Overview
        var cards = cut.FindAll(".rm-card");
        cards.Should().HaveCount(3);
        cards[0].Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Resumen de carga");
            cut.Markup.Should().Contain("Máquinas");
        });

        // Click "Agregar máquina" → PickMachine
        var dashedButtons = cut.FindAll(".rm-cta--dashed");
        dashedButtons.Should().NotBeEmpty();
        dashedButtons[0].Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Paso 1 de 2");
            cut.Markup.Should().Contain("Elegir máquina");
        });

        // Go back to Overview via back button
        var backButtons = cut.FindAll(".rm-iconbtn");
        backButtons[0].Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Resumen de carga");
        });

        // Click machine card → EditSlots
        var machineCards = cut.FindAll(".rm-card");
        machineCards.Should().NotBeEmpty();
        machineCards[0].Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Paso 2 · Editar carga");
            cut.Markup.Should().Contain("PISO 1");
        });
    }

    // ── Test 2: Lista Empty State ─────────────────────────────────────

    [Fact]
    public void RecargaMovil_Lista_Renders_Empty_State()
    {
        _mockHandler.SetEmptyTemplates();
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Sin cargas registradas");
        });

        // No cards should render
        cut.FindAll(".rm-card").Should().BeEmpty();
    }

    // ── Test 3: Lista Renders Cards with status badges ────────────────

    [Fact]
    public void RecargaMovil_Lista_Renders_Cards()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() =>
        {
            var cards = cut.FindAll(".rm-card");
            cards.Should().HaveCount(3);
        });

        // Status badges: Finalizado and Pendiente
        cut.Markup.Should().Contain("Pendiente");
        cut.Markup.Should().Contain("Finalizado");

        // Progress bars rendered
        cut.FindAll(".rm-progress").Should().HaveCount(3);

        // Machine chips rendered
        cut.FindAll(".rm-chip").Should().NotBeEmpty();
    }

    // ── Test 4: Lista Progress Bar No Negative With Pending Slots ────
    
    [Fact]
    public void RecargaMovil_Lista_Progress_Bar_No_Negative_With_Pending_Slots()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Cargas");
        });

        // Template 1 (Carga Semanal) has 2 periodos, 1 loaded + 1 with pending slots
        // After per-periodo fix: loadedCount=1, periodoCount=2, pct=50%
        
        // The sub text should show non-negative machine count
        cut.Markup.Should().Contain("2 máq. · 1/2 cargadas");

        // Progress bar should show 50%, not negative
        var progressFills = cut.FindAll(".rm-progress__fill");
        progressFills.Should().HaveCount(3);

        // First template's fill width should be 50%
        var firstFill = progressFills[0];
        var style = firstFill.GetAttribute("style");
        style.Should().Be("width:50%");

        // The percentage text should be non-negative
        cut.Markup.Should().Contain("50%");
    }

    // ── Test 5: Resumen Renders Stats ─────────────────────────────────
    
    [Fact]
    public void RecargaMovil_Resumen_Renders_Stats()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Click first template card to navigate to Resumen
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();

        cut.WaitForAssertion(() =>
        {
            // Stats row should show machines and units
            cut.Markup.Should().Contain("Máquinas");
            cut.Markup.Should().Contain("cargadas");
            cut.Markup.Should().Contain("Unidades");
            cut.Markup.Should().Contain("u.");

            // First template has 2 machines, 1 loaded → "1/2 cargadas"
            cut.Markup.Should().Contain("1/2");
        });
    }

    // ── Test 5: Primary CTA Disabled When Not All Loaded ──────────────

    [Fact]
    public void RecargaMovil_Resumen_PrimaryCTA_Disabled_When_Not_All_Loaded()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Click template 1 (2 machines, 1 loaded → not all loaded)
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();

        cut.WaitForAssertion(() =>
        {
            // CTA should be disabled (shows "Faltan máquinas por cargar")
            cut.Markup.Should().Contain("Faltan máquinas por cargar");
            cut.Markup.Should().NotContain("Finalizar carga");
        });
    }

    // ── Test 6: Primary CTA Enabled When All Loaded ──────────────────

    [Fact]
    public void RecargaMovil_Resumen_PrimaryCTA_Enabled_When_All_Loaded()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Click template 2 (1 machine, loaded → all loaded)
        var cards = cut.FindAll(".rm-card");
        cards[1].Click();

        cut.WaitForAssertion(() =>
        {
            // CTA should be enabled (shows "Finalizar carga")
            cut.Markup.Should().Contain("Finalizar carga");
            cut.Markup.Should().NotContain("Faltan máquinas por cargar");
        });
    }

    // ── Test 7: Elegir Renders Pool of Available Machines ────────────

    [Fact]
    public void RecargaMovil_Elegir_Renders_Pool()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Navigate to Resumen
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Resumen de carga"));

        // Click "Agregar máquina"
        var addMachineBtn = cut.FindAll(".rm-cta--dashed")[0];
        addMachineBtn.Click();

        cut.WaitForAssertion(() =>
        {
            // Pick view renders with machine pool
            cut.Markup.Should().Contain("Elegir máquina");
            cut.Markup.Should().Contain("Paso 1 de 2");
            // Pool should show available machines
            cut.FindAll(".rm-row").Should().NotBeEmpty();
        });
    }

    // ── Test 8: EditSlots Renders Floor Tabs and Grid ────────────────

    [Fact]
    public void RecargaMovil_EditSlots_Renders_Floor_Tabs_And_Grid()
    {
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Navigate to Resumen via template 1
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Resumen de carga"));

        // Click a machine card to go to EditSlots
        var machineCards = cut.FindAll(".rm-card");
        machineCards[0].Click();

        cut.WaitForAssertion(() =>
        {
            // EditSlots renders
            cut.Markup.Should().Contain("Paso 2 · Editar carga");

            // Floor tabs (PISO 1, etc.)
            var floorTags = cut.FindAll(".rm-floor__tag");
            floorTags.Should().NotBeEmpty();

            // Slot grid rendered
            var slots = cut.FindAll(".rm-slot");
            slots.Should().NotBeEmpty();

            // Dock with Guardar button
            cut.Markup.Should().Contain("Guardar");
        });
    }

    // ── Test 9: Resumen Empty State (0 machines) ──────────────────────

    [Fact]
    public void RecargaMovil_Resumen_Renders_Empty_State_When_No_Machines()
    {
        _mockHandler.SetSingleEmptyTemplate();
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() =>
        {
            // Single 0-machine template card
            var cards = cut.FindAll(".rm-card");
            cards.Should().HaveCount(1);
        });

        // Click the empty template to navigate to Resumen
        var card = cut.FindAll(".rm-card")[0];
        card.Click();

        cut.WaitForAssertion(() =>
        {
            // Empty state text
            cut.Markup.Should().Contain("Carga sin máquinas");
            // CTA to add machine
            cut.Markup.Should().Contain("Cargar máquina");
            // Finalizar carga NOT present (no machines)
            cut.Markup.Should().NotContain("Finalizar carga");
        });
    }

    // ── Test 10: PickMachine Empty State (pool empty) ──────────────────

    [Fact]
    public void RecargaMovil_PickMachine_Renders_Empty_State_When_Pool_Empty()
    {
        _mockHandler.SetEmptyPool();
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Navigate to Resumen via template 1
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Resumen de carga"));

        // Click "Agregar máquina" to go to PickMachine
        var addBtn = cut.FindAll(".rm-cta--dashed")[0];
        addBtn.Click();

        cut.WaitForAssertion(() =>
        {
            // Pool is empty — shows all-machines-included message
            cut.Markup.Should().Contain("Todas las máquinas del pool ya están en esta carga");
            // Escanear button still present
            cut.FindAll(".rm-scan-btn").Should().NotBeEmpty();
        });
    }

    // ── Test 11: EditSlots Empty State (0 slots) ───────────────────────

    [Fact]
    public void RecargaMovil_EditSlots_Renders_Empty_State_When_No_Slots()
    {
        _mockHandler.SetZeroSlotsForMachine();
        var cut = RenderComponent<RecargaMovilTestHost>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Cargas"));

        // Navigate to Resumen via template 1 (returned with 1 machine, 0 slots)
        var firstCard = cut.FindAll(".rm-card")[0];
        firstCard.Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Resumen de carga"));

        // Click the machine card to go to EditSlots
        var machineCards = cut.FindAll(".rm-card");
        machineCards[0].Click();

        cut.WaitForAssertion(() =>
        {
            // Empty slots text
            cut.Markup.Should().Contain("No hay slots configurados para esta máquina");
            // Guardar button still visible in the dock
            cut.Markup.Should().Contain("Guardar");
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHOTO SHEET TESTS (PR 3)
    // ═══════════════════════════════════════════════════════════════════

    // ── Test 13: Primary CTA disabled before capture ─────────────────

    [Fact]
    public void MobileMachinePhotoSheet_PrimaryCTA_Disabled_Before_Capture()
    {
        var cut = RenderComponent<MobileMachinePhotoSheet>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnPhotoAccepted, (IBrowserFile _) => { })
        );

        var submitBtn = cut.Find("button[aria-label='Subir y finalizar']");
        submitBtn.HasAttribute("disabled").Should().BeTrue();
    }

    // ── Test 14: Rejects HEIC via magic bytes ────────────────────────

    [Fact]
    public void MobileMachinePhotoSheet_Rejects_HEIC()
    {
        var cut = RenderComponent<MobileMachinePhotoSheet>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnPhotoAccepted, (IBrowserFile _) => { })
        );

        // Construct minimal HEIC bytes with ftyp-heic magic marker
        var heicBytes = new byte[12];
        // ftyp box (offset 4)
        heicBytes[4] = (byte)'f'; heicBytes[5] = (byte)'t';
        heicBytes[6] = (byte)'y'; heicBytes[7] = (byte)'p';
        heicBytes[8] = (byte)'h'; heicBytes[9] = (byte)'e';
        heicBytes[10] = (byte)'i'; heicBytes[11] = (byte)'c';

        var heicFile = new MockBrowserFile(heicBytes, "image/jpeg", "test.heic");

        // Trigger InputFile selection
        var inputFile = cut.FindComponent<InputFile>();
        var args = new InputFileChangeEventArgs(new[] { heicFile });
        cut.InvokeAsync(() => inputFile.Instance.OnChange.InvokeAsync(args));

        // Assert error is shown
        cut.Markup.Should().Contain("HEIC no soportado");

        // CTA should remain disabled
        var submitBtn = cut.Find("button[aria-label='Subir y finalizar']");
        submitBtn.HasAttribute("disabled").Should().BeTrue();
    }

    // ── Test 15: Valid JPEG triggers OnPhotoAccepted on submit ──────

    [Fact]
    public void MobileMachinePhotoSheet_Invokes_OnPhotoAccepted_On_Submit()
    {
        IBrowserFile? receivedFile = null;
        var cut = RenderComponent<MobileMachinePhotoSheet>(parameters => parameters
            .Add(p => p.Visible, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnPhotoAccepted, (IBrowserFile f) => { receivedFile = f; })
        );

        // Create minimal JPEG bytes (no HEIC magic)
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var jpegFile = new MockBrowserFile(jpegBytes, "image/jpeg", "test.jpg");

        // Trigger InputFile selection
        var inputFile = cut.FindComponent<InputFile>();
        var args = new InputFileChangeEventArgs(new[] { jpegFile });
        cut.InvokeAsync(() => inputFile.Instance.OnChange.InvokeAsync(args));

        cut.WaitForState(() => !cut.Markup.Contains("formato", StringComparison.OrdinalIgnoreCase));

        // Tap submit
        var submitBtn = cut.Find("button[aria-label='Subir y finalizar']");
        submitBtn.Click();

        receivedFile.Should().NotBeNull();
        receivedFile!.Name.Should().Be("test.jpg");
        receivedFile.ContentType.Should().Be("image/jpeg");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MOCK BROWSER FILE
    // ═══════════════════════════════════════════════════════════════════

    private class MockBrowserFile : IBrowserFile
    {
        private readonly byte[] _content;

        public MockBrowserFile(byte[] content, string contentType, string name)
        {
            _content = content;
            ContentType = contentType;
            Name = name;
        }

        public string Name { get; }
        public long Size => _content.Length;
        public DateTimeOffset LastModified => DateTimeOffset.Now;
        public string ContentType { get; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => new MemoryStream(_content);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MOCK HTTP MESSAGE HANDLER
    // ═══════════════════════════════════════════════════════════════════

    private class RecargaMovilMockHttpMessageHandler : HttpMessageHandler
    {
        private bool _emptyTemplates;
        private bool _emptyPool;
        private bool _singleEmptyTemplate;
        private bool _zeroSlotsForMachine;

        public void SetEmptyTemplates() => _emptyTemplates = true;
        public void SetEmptyPool() => _emptyPool = true;
        public void SetSingleEmptyTemplate() => _singleEmptyTemplate = true;
        public void SetZeroSlotsForMachine() => _zeroSlotsForMachine = true;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimEnd('/') ?? "";

            if (path == "/api/Ventas/lista-maquinas")
            {
                var machines = _emptyPool
                    ? Array.Empty<object>()
                    : new object[]
                    {
                        new { Id = 3, Nombre = "Máquina 003" },
                        new { Id = 4, Nombre = "Máquina 004" }
                    };
                var json = JsonSerializer.Serialize(machines, JsonOptions);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            if (path == "/api/Ventas/lista-productos")
            {
                var json = JsonSerializer.Serialize(new[]
                {
                    new { Id = 1, Nombre = "Coca Cola", StockBodega = 50, CostoPromedio = 800m, PrecioVenta = 1200m },
                    new { Id = 2, Nombre = "Pepsi", StockBodega = 30, CostoPromedio = 700m, PrecioVenta = 1100m },
                    new { Id = 3, Nombre = "Ramitas", StockBodega = 20, CostoPromedio = 300m, PrecioVenta = 500m }
                }, JsonOptions);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json)
                });
            }

            // GET /api/TemplateRecarga/maquina/{machineId}/slots
            if (path.StartsWith("/api/TemplateRecarga/maquina/") && path.EndsWith("/slots"))
            {
                var segments = path.Split('/');
                // Path: /api/TemplateRecarga/maquina/{id}/slots
                if (segments.Length >= 6 && int.TryParse(segments[4], out var machineId))
                {
                    var slots = GenerateSlotConfig(machineId, 10);
                    var json = JsonSerializer.Serialize(slots, JsonOptions);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    });
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            // Match any /api/TemplateRecarga or /api/TemplateRecarga/{id} path
            if (path == "/api/TemplateRecarga")
            {
                if (_emptyTemplates)
                {
                    var json = JsonSerializer.Serialize(Array.Empty<object>(), JsonOptions);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    });
                }

                // Template list
                var list = CreateTemplateList();
                var listJson = JsonSerializer.Serialize(list, JsonOptions);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(listJson)
                });
            }

            // GET /api/TemplateRecarga/{id}
            if (path.StartsWith("/api/TemplateRecarga/"))
            {
                var segments = path.Split('/');
                if (segments.Length >= 4 && int.TryParse(segments[3], out var templateId))
                {
                    var tpl = CreateTemplateDetail(templateId);
                    var json = JsonSerializer.Serialize(tpl, JsonOptions);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json)
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private object CreateTemplateDetail(int templateId)
        {
            if (templateId == 4 && _singleEmptyTemplate)
            {
                return new
                {
                    Id = 4,
                    Nombre = "Template Vacío",
                    Descripcion = (string?)"Sin máquinas",
                    FechaCreacion = DateTime.Now,
                    Estado = 0,
                    EsActivo = false,
                    Periodos = Array.Empty<object>()
                };
            }

            if (templateId == 2)
            {
                // Template 2: all machines loaded
                return new
                {
                    Id = 2,
                    Nombre = "Carga Express",
                    Descripcion = "Carga rápida",
                    FechaCreacion = DateTime.Now.AddDays(-1),
                    Estado = 0,
                    EsActivo = false,
                    Periodos = new[]
                    {
                        new
                        {
                            Id = 3,
                            MaquinaId = 3,
                            MaquinaNombre = "Máquina 003",
                            IdInternoMaquina = "2410280033",
                            FechaRecarga = DateTime.Now,
                            FechaFin = DateTime.Now.AddDays(7),
                            TieneFotoGuia = false,
                            TieneFotoOcr = false,
                            SnapshotSlots = GenerateSlotConfig(3, 5)
                        }
                    }
                };
            }

            if (templateId == 3)
            {
                // Template 3: finalized
                return new
                {
                    Id = 3,
                    Nombre = "Carga Mensual",
                    Descripcion = "Finalizada",
                    FechaCreacion = DateTime.Now.AddDays(-30),
                    Estado = 2,
                    EsActivo = true,
                    Periodos = new[]
                    {
                        new
                        {
                            Id = 4,
                            MaquinaId = 4,
                            MaquinaNombre = "Máquina 004",
                            IdInternoMaquina = "2410280044",
                            FechaRecarga = DateTime.Now.AddDays(-30),
                            FechaFin = DateTime.Now.AddDays(-23),
                            TieneFotoGuia = true,
                            TieneFotoOcr = false,
                            SnapshotSlots = GenerateSlotConfig(4, 10, loaded: true)
                        }
                    }
                };
            }

            if (templateId == 1 && _zeroSlotsForMachine)
            {
                // Template 1 variant: 1 machine with 0 slots
                return new
                {
                    Id = 1,
                    Nombre = "Carga Semanal",
                    Descripcion = "Template con 1 máquina sin slots",
                    FechaCreacion = DateTime.Now.AddDays(-7),
                    Estado = 0,
                    EsActivo = false,
                    Periodos = new[]
                    {
                        new
                        {
                            Id = 5,
                            MaquinaId = 5,
                            MaquinaNombre = "Máquina 005",
                            IdInternoMaquina = "0005",
                            FechaRecarga = DateTime.Now,
                            FechaFin = DateTime.Now.AddDays(7),
                            TieneFotoGuia = false,
                            TieneFotoOcr = false,
                            SnapshotSlots = Array.Empty<object>()
                        }
                    }
                };
            }

            // Template 1: one machine loaded, one not
            return new
            {
                Id = 1,
                Nombre = "Carga Semanal",
                Descripcion = "Template con 2 máquinas",
                FechaCreacion = DateTime.Now.AddDays(-7),
                Estado = 0,
                EsActivo = false,
                Periodos = new[]
                {
                    new
                    {
                        Id = 1,
                        MaquinaId = 1,
                        MaquinaNombre = "Máquina 001",
                        IdInternoMaquina = "2410280022",
                        FechaRecarga = DateTime.Now,
                        FechaFin = DateTime.Now.AddDays(7),
                        TieneFotoGuia = false,
                        TieneFotoOcr = false,
                        SnapshotSlots = GenerateSlotConfig(1, 10, loaded: true)
                    },
                    new
                    {
                        Id = 2,
                        MaquinaId = 2,
                        MaquinaNombre = "Máquina 002",
                        IdInternoMaquina = "2410280023",
                        FechaRecarga = DateTime.Now,
                        FechaFin = DateTime.Now.AddDays(7),
                        TieneFotoGuia = false,
                        TieneFotoOcr = false,
                        SnapshotSlots = GenerateSlotConfig(2, 10, loaded: false)
                    }
                }
            };
        }

        private object[] CreateTemplateList()
        {
            if (_singleEmptyTemplate)
            {
                return new[]
                {
                    new
                    {
                        Id = 4,
                        Nombre = "Template Vacío",
                        Descripcion = (string?)"Sin máquinas",
                        FechaCreacion = DateTime.Now,
                        Estado = 0,
                        EsActivo = false,
                        CantidadMaquinas = 0,
                        CantidadSlotsPendientes = 0,
                        Periodos = Array.Empty<object>()
                    }
                };
            }

            return new[]
            {
                new
                {
                    Id = 1,
                    Nombre = "Carga Semanal",
                    Descripcion = "Template con 2 máquinas",
                    FechaCreacion = DateTime.Now.AddDays(-7),
                    Estado = 0,
                    EsActivo = false,
                    CantidadMaquinas = 2,
                    CantidadSlotsPendientes = 0, // Computed from Periodos.SnapshotSlots
                    Periodos = new[]
                    {
                        CreateListPeriodo(1, 1, "2410280022", loaded: true, slotCount: 10),
                        CreateListPeriodo(2, 2, "2410280023", loaded: false, slotCount: 10)
                    }
                },
                new
                {
                    Id = 2,
                    Nombre = "Carga Express",
                    Descripcion = "Carga rápida",
                    FechaCreacion = DateTime.Now.AddDays(-1),
                    Estado = 0,
                    EsActivo = false,
                    CantidadMaquinas = 1,
                    CantidadSlotsPendientes = 0, // Computed from Periodos.SnapshotSlots
                    Periodos = new[]
                    {
                        CreateListPeriodo(3, 3, "2410280033", loaded: true, slotCount: 10)
                    }
                },
                new
                {
                    Id = 3,
                    Nombre = "Carga Mensual",
                    Descripcion = "Finalizada",
                    FechaCreacion = DateTime.Now.AddDays(-30),
                    Estado = 2,
                    EsActivo = true,
                    CantidadMaquinas = 1,
                    CantidadSlotsPendientes = 0, // Computed from Periodos.SnapshotSlots
                    Periodos = new[]
                    {
                        CreateListPeriodo(4, 4, "2410280044", loaded: true, slotCount: 10)
                    }
                }
            };
        }

        private static object CreateListPeriodo(int id, int maquinaId, string idInterno, bool loaded, int slotCount)
        {
            return new
            {
                Id = id,
                MaquinaId = maquinaId,
                IdInternoMaquina = idInterno,
                FechaRecarga = DateTime.Now,
                SnapshotSlots = GenerateSlotConfig(maquinaId, slotCount, loaded)
            };
        }

        private static object[] GenerateSlotConfig(int machineId, int count, bool loaded = true)
        {
            var slots = new List<object>();
            for (int i = 1; i <= count; i++)
            {
                slots.Add(new
                {
                    Id = (machineId * 100) + i,
                    NumeroSlot = $"S{i:D2}",
                    ProductoId = loaded ? (int?)((i % 3) + 1) : null,
                    ProductoNombre = loaded ? new[] { "Coca Cola", "Pepsi", "Ramitas" }[i % 3] : "",
                    CantidadInicial = loaded ? ((i * 2) % 5) + 1 : 0,
                    CapacidadSlot = 5,
                    Estado = loaded ? 0 : 1
                });
            }
            return slots.ToArray();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AUTH HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private class AuthenticatedAuthStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Name, "test") },
                "test");
            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }

    private class FakeAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TEST HOST COMPONENT
    // ═══════════════════════════════════════════════════════════════════

    private class RecargaMovilTestHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<RecargaMovil>(2);
                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
