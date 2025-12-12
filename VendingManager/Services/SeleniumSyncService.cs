using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.IO;

namespace VendingManager.Services
{
    public class SeleniumSyncService : IDisposable
    {
        private IWebDriver _driver;
        private readonly string _downloadPath;

        public SeleniumSyncService()
        {
            _downloadPath = Path.Combine(Directory.GetCurrentDirectory(), "TempDownloads");
            if (Directory.Exists(_downloadPath)) Directory.Delete(_downloadPath, true);
            Directory.CreateDirectory(_downloadPath);

            var options = new ChromeOptions();
            options.AddUserProfilePreference("download.default_directory", _downloadPath);
            options.AddUserProfilePreference("download.prompt_for_download", false);

            // options.AddArgument("--headless"); // Mantenlo comentado para ver qué hace
            _driver = new ChromeDriver(options);
        }

        public Task<bool> Login()
        {
            return Task.FromResult(true);
        }

        public async Task<Stream?> DescargarReporte(DateTime inicio, DateTime fin)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(30));

                    // =======================================================
                    // FASE 1: LOGIN AGRESIVO
                    // =======================================================
                    if (!_driver.Url.Contains("index"))
                    {
                        Console.WriteLine("🤖 SELENIUM: Navegando al Login...");
                        _driver.Navigate().GoToUrl("https://os.ourvend.com/Account/Login");

                        // Esperar a que aparezca cualquier campo de contraseña
                        wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("input[type='Flf2121#']")));
                        Thread.Sleep(1000); // Pequeña pausa para asegurar carga

                        // --- TRUCO: Escribir usando JavaScript (Infalible) ---
                        IJavaScriptExecutor jsLogin = (IJavaScriptExecutor)_driver;

                        // 1. Poner Usuario (Busca el primer input de texto visible)
                        jsLogin.ExecuteScript("document.querySelector(\"input[type='text']\").value = 'Comercialflf';");

                        // 2. Poner Contraseña (Busca el primer input de password)
                        // ⚠️⚠️⚠️ ¡PON TU CONTRASEÑA AQUÍ ABAJO! ⚠️⚠️⚠️
                        string miClave = "PON_TU_CLAVE_REAL_AQUI";
                        jsLogin.ExecuteScript($"document.querySelector(\"input[type='password']\").value = '{miClave}';");

                        Console.WriteLine("🤖 SELENIUM: Credenciales inyectadas.");
                        Thread.Sleep(500);

                        // 3. Click en el botón de entrar (Buscamos el botón verde o ID Login_btn)
                        try
                        {
                            _driver.FindElement(By.Id("Login_btn")).Click();
                        }
                        catch
                        {
                            // Si falla por ID, busca por clase de botón
                            _driver.FindElement(By.CssSelector("button[type='button']")).Click();
                        }

                        // Esperar a que cambie la página (Login exitoso)
                        wait.Until(d => d.Url.ToLower().Contains("index"));
                        Console.WriteLine("✅ SELENIUM: Login Exitoso.");
                    }

                    // =======================================================
                    // FASE 2: NAVEGAR Y FILTRAR
                    // =======================================================
                    _driver.Navigate().GoToUrl("https://os.ourvend.com/OutReport/Index");
                    wait.Until(ExpectedConditions.ElementIsVisible(By.Id("IndexTime")));

                    // Inyectar Fechas con JS
                    IJavaScriptExecutor js = (IJavaScriptExecutor)_driver;
                    string sInicio = inicio.ToString("yyyy-MM-dd HH:mm:ss");
                    string sFin = fin.ToString("yyyy-MM-dd HH:mm:ss");

                    js.ExecuteScript($"document.getElementById('IndexTime').value = '{sInicio}'");
                    js.ExecuteScript($"document.getElementById('LastTime').value = '{sFin}'");

                    // =======================================================
                    // FASE 3: GENERAR REPORTE (Query -> Export)
                    // =======================================================

                    // Click Query (Buscar por texto exacto es más seguro)
                    var btnQuery = _driver.FindElement(By.XPath("//button[text()='Query']"));
                    btnQuery.Click();
                    Thread.Sleep(3000); // Esperar refresco de tabla

                    // Click Export
                    var btnExport = _driver.FindElement(By.XPath("//button[text()='Export']"));
                    btnExport.Click();
                    Thread.Sleep(1000);

                    // Click "Export all data" (en el popup)
                    var btnExportAll = _driver.FindElement(By.XPath("//button[contains(text(),'Export all data')]"));
                    btnExportAll.Click();

                    Console.WriteLine("🤖 SELENIUM: Generando... Esperando 5 seg...");
                    Thread.Sleep(5000);

                    // Cerrar posibles avisos flotantes
                    try { ((IJavaScriptExecutor)_driver).ExecuteScript("layer.closeAll();"); } catch { }

                    // =======================================================
                    // FASE 4: DESCARGAR (Schedule)
                    // =======================================================

                    // Abrir lista de descargas
                    var btnSchedule = _driver.FindElement(By.XPath("//button[contains(text(),'ExcelSchedule')]"));
                    btnSchedule.Click();
                    Thread.Sleep(2000);

                    // Buscar el primer botón "Download" de la tabla
                    var linkDescarga = _driver.FindElement(By.XPath("(//a[contains(text(),'Download')])[1]"));
                    Console.WriteLine("🤖 SELENIUM: Click en Descargar.");
                    linkDescarga.Click();

                    // =======================================================
                    // FASE 5: ESPERAR ARCHIVO
                    // =======================================================
                    for (int i = 0; i < 30; i++)
                    {
                        Thread.Sleep(1000);
                        var archivo = Directory.GetFiles(_downloadPath, "*.xls").FirstOrDefault();
                        if (archivo != null)
                        {
                            Thread.Sleep(2000); // Esperar que termine de escribir
                            var memory = new MemoryStream();
                            using (var fs = new FileStream(archivo, FileMode.Open, FileAccess.Read))
                            {
                                fs.CopyTo(memory);
                            }
                            memory.Position = 0;
                            return memory;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("❌ Error Selenium: " + ex.Message);
                    // Opcional: Tomar captura de pantalla si falla
                    try
                    {
                        Screenshot ss = ((ITakesScreenshot)_driver).GetScreenshot();
                        ss.SaveAsFile("error_selenium.png");
                    }
                    catch { }
                }
                return null;
            });
        }

        public void Dispose()
        {
            try { _driver?.Quit(); } catch { }
        }
    }
}