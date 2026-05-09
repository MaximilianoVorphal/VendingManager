import time
import os
import asyncio
import sys
import traceback
from datetime import datetime
from playwright.async_api import async_playwright

# ──────────────────────────────────────────────
# CONFIGURACIÓN
# ──────────────────────────────────────────────
DOWNLOAD_DIR = os.path.join(os.getcwd(), "downloads")
DEBUG_DIR = os.path.join(DOWNLOAD_DIR, "debug_alt")
USERNAME = os.environ.get("OURVEND_USER")
PASSWORD = os.environ.get("OURVEND_PASS")

DEFAULT_MACHINE_ID = ""
now = datetime.now()
DEFAULT_START = now.strftime("%Y-%m-01")
DEFAULT_END = now.strftime("%Y-%m-%d")

MACHINE_GROUP_VALUE = "53aed77a-1c1b-40b2-a9be-d40d98adb348"

# ──────────────────────────────────────────────
# LOGGING (archivo + consola)
# ──────────────────────────────────────────────
_log_file = None

def log(msg):
    """Escribe a consola Y a archivo de log."""
    ts = datetime.now().strftime("%H:%M:%S")
    line = f"[{ts}] {msg}"
    print(line, flush=True)
    global _log_file
    if _log_file:
        _log_file.write(line + "\n")
        _log_file.flush()

def log_path():
    return os.path.join(DEBUG_DIR, f"scraper_alt_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log")


# ──────────────────────────────────────────────
# HELPERS
# ──────────────────────────────────────────────
async def screenshot(page, name):
    """Toma screenshot si la página está viva."""
    try:
        path = os.path.join(DEBUG_DIR, name)
        await page.screenshot(path=path)
        log(f"    📸 {name}")
    except:
        pass

async def cerrar_modales(frame):
    """Cierra modales vía JS."""
    try:
        await frame.evaluate("""
            $('.modal').modal('hide');
            $('.modal-backdrop').remove();
        """)
    except:
        pass

async def buscar_iframe(page, selector, max_attempts=20):
    """Busca un frame que contenga el selector dado."""
    for attempt in range(max_attempts):
        for frame in page.frames:
            try:
                if await frame.locator(selector).count() > 0:
                    return frame
            except:
                continue
        await asyncio.sleep(1)
    return None


# ──────────────────────────────────────────────
# FLUJO PRINCIPAL
# ──────────────────────────────────────────────
async def run_async(machine_id, start_date, end_date):
    global _log_file

    os.makedirs(DEBUG_DIR, exist_ok=True)
    _log_file = open(log_path(), "w", encoding="utf-8")
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    log("=" * 60)
    log(f"🧪 ALT SCRAPER INICIADO")
    log(f"   Fechas: {start_date} a {end_date}")
    log(f"   Debug: {DEBUG_DIR}")
    log("=" * 60)

    async with async_playwright() as p:
        browser = await p.chromium.launch(
            headless=True,
            args=["--no-sandbox", "--disable-setuid-sandbox"]
        )
        context = await browser.new_context(accept_downloads=True)
        page = await context.new_page()

        try:
            # ── 1. LOGIN PAGE ──
            log("🌐 [1/11] Accediendo al login...")
            await page.goto("https://os.ourvend.com/Account/Login", timeout=60000)
            await screenshot(page, f"{timestamp}_01_login.png")

            # ── 2. CAMBIAR A INGLÉS ──
            log("🌐 [2/11] Cambiando idioma a Inglés...")
            try:
                lang_en = page.locator('a[href*="lang=en-us"]')
                if await lang_en.is_visible(timeout=3000):
                    await lang_en.click()
                    await page.wait_for_load_state("networkidle")
                    log("    ✅ Inglés OK")
                else:
                    log("    ⚠️ Link English no visible")
            except Exception as e:
                log(f"    ⚠️ Error al cambiar idioma: {e}")
            await page.wait_for_timeout(1000)

            # ── 3. LOGIN ──
            log(f"🔐 [3/11] Iniciando sesión como {USERNAME}...")
            if not USERNAME or not PASSWORD:
                raise Exception("Credenciales OURVEND_USER/PASS no configuradas")

            await page.fill("#userName", USERNAME)
            await page.fill("#passWord", PASSWORD)
            await page.evaluate("UserLogin()")
            await page.wait_for_url("**/YSTemplet/index*", timeout=60000)
            log("    ✅ Login exitoso")
            await screenshot(page, f"{timestamp}_02_dashboard.png")

            # ── 4. NAVEGAR ──
            log("🧭 [4/11] Navegando a History > Product realease record...")
            await page.get_by_role("link", name="History").click()
            await page.wait_for_timeout(500)
            await page.get_by_role("link", name="Product realease record").click()
            await page.wait_for_timeout(2000)
            await screenshot(page, f"{timestamp}_03_navegacion.png")

            # ── 5. BUSCAR IFRAME DEL REPORTE (no el Home) ──
            log("🔍 [5/11] Buscando iframe del reporte (OutReport/Index)...")
            target_frame = None
            for attempt in range(30):
                for frame in page.frames:
                    try:
                        url = frame.url
                        # SALTAR el iframe del Home (YSHome/Index)
                        if "YSHome" in url:
                            continue
                        if await frame.locator("#MiGroup").count() > 0:
                            target_frame = frame
                            break
                    except:
                        continue
                if target_frame:
                    break
                await asyncio.sleep(1)

            if not target_frame:
                log(f"    ❌ No encontrado. Frames disponibles:")
                for i, f in enumerate(page.frames):
                    try:
                        url = f.url[:80] if f.url else "about:blank"
                        log(f"       [{i}] {url}")
                    except:
                        log(f"       [{i}] <error>")
                raise Exception("No se encontró iframe del reporte (OutReport/Index)")

            log(f"    ✅ Iframe encontrado: {target_frame.url[:80]}")

            # ── 6. GRUPO (sin máquina) - USAR JS porque Bootstrap Select oculta el <select> ──
            log("⚙️ [6/11] Seleccionando grupo (sin filtrar por máquina)...")
            result = await target_frame.evaluate(f"""
                (() => {{
                    var sel = document.getElementById('MiGroup');
                    if (!sel) return 'MiGroup no encontrado';
                    sel.value = '{MACHINE_GROUP_VALUE}';
                    sel.dispatchEvent(new Event('change'));
                    if (sel.value !== '{MACHINE_GROUP_VALUE}') {{
                        // El UUID no existe en los options — listar los disponibles
                        var opts = [];
                        for (var i = 0; i < sel.options.length; i++) {{
                            opts.push(sel.options[i].value + '=' + sel.options[i].text);
                        }}
                        return 'UUID NO ENCONTRADO. Options disponibles: ' + JSON.stringify(opts.slice(0, 20));
                    }}
                    var idx = sel.selectedIndex;
                    var text = idx >= 0 ? sel.options[idx].text : '(sin texto)';
                    return 'OK: ' + text;
                }})()
            """)
            log(f"    Resultado: {result}")

            await asyncio.sleep(3)

            # Verificar MachineID (JS porque Bootstrap Select oculta el <select>)
            try:
                machine_info = await target_frame.evaluate("""
                    (() => {
                        var sel = document.getElementById('MachineID');
                        if (!sel) return 'MachineID no encontrado';
                        var opts = [];
                        for (var i = 0; i < sel.options.length; i++) {
                            opts.push(sel.options[i].text);
                        }
                        return JSON.stringify({count: sel.options.length - 1, names: opts.slice(1, 6)});
                    })()
                """)
                log(f"    Máquinas disponibles: {machine_info}")
            except Exception as e:
                log(f"    ⚠️ No se pudo leer MachineID: {e}")

            # ── 7. FECHAS ──
            log(f"📅 [7/11] Configurando fechas: {start_date} → {end_date}")
            try:
                await target_frame.fill("#IndexTime", start_date)
                await target_frame.fill("#IndexTimeHMS", "00:00:00")
                await target_frame.fill("#LastTime", end_date)
                await target_frame.fill("#LastTimeHMS", "23:59:59")
                log("    ✅ Fechas OK")
            except Exception as e:
                log(f"    ⚠️ Fill falló: {e}, usando JS...")
                await target_frame.evaluate(f"""
                    document.getElementById('IndexTime').value = '{start_date}';
                    document.getElementById('IndexTimeHMS').value = '00:00:00';
                    document.getElementById('LastTime').value = '{end_date}';
                    document.getElementById('LastTimeHMS').value = '23:59:59';
                """)
                log("    ✅ Fechas OK (JS)")

            await screenshot(page, f"{timestamp}_04_antes_query.png")

            # ── 8. QUERY ──
            log("🔎 [8/11] Ejecutando Query...")
            try:
                await target_frame.locator("#btnSearch").click()
            except:
                await target_frame.evaluate("document.getElementById('btnSearch').click()")
            await asyncio.sleep(5)

            # Contar filas
            rows = await target_frame.locator(
                "table tbody tr.ui-row-ltr, table tbody tr.ui-widget-content"
            ).all()
            log(f"    Filas obtenidas: {len(rows)}")
            await screenshot(page, f"{timestamp}_05_post_query.png")

            # ── 9. EXPORT ──
            log("📤 [9/11] Iniciando exportación...")

            # 9a. Click Export
            try:
                await target_frame.locator("#btnExport").click()
                log("    ✅ Export clickeado")
            except:
                await target_frame.evaluate("document.getElementById('btnExport').click()")
                log("    ✅ Export clickeado (JS)")

            await asyncio.sleep(2)
            await screenshot(page, f"{timestamp}_06_export_modal.png")

            # 9b. Click "Export all data"
            log("    Click en 'Export all data'...")
            export_ok = False
            try:
                btn = target_frame.get_by_role("button", name="Export all data")
                if await btn.is_visible(timeout=5000):
                    await btn.click()
                    export_ok = True
                    log("    ✅ Export all data clickeado")
            except:
                pass

            if not export_ok:
                log("    ⚠️ Botón no visible, intentando JS...")
                result = await target_frame.evaluate("""
                    (() => {
                        var modal = document.getElementById('CheckExportExecl');
                        if (!modal) return 'CheckExportExecl no encontrado';
                        var btns = modal.querySelectorAll('button');
                        for (var b of btns) {
                            if (b.textContent.includes('Export all data')) {
                                b.click();
                                return 'clicked: ' + b.textContent.trim();
                            }
                        }
                        return 'Export all data no encontrado entre ' + btns.length + ' botones';
                    })()
                """)
                log(f"    Resultado JS: {result}")

            await screenshot(page, f"{timestamp}_07_post_export_all.png")

            # 9c. Esperar Role_Message
            log("    Esperando Role_Message...")
            role_found = False
            for _ in range(40):  # 20 segundos
                try:
                    rm = target_frame.locator("#Role_Message")
                    if await rm.is_visible(timeout=500):
                        txt = await rm.text_content()
                        log(f"    ✅ Role_Message: {txt[:120] if txt else 'N/A'}")
                        btn = rm.locator("button")
                        if await btn.count() > 0:
                            await btn.first.click()
                            log("    ✅ Role_Message cerrado")
                            role_found = True
                            break
                except:
                    pass
                await asyncio.sleep(0.5)

            if not role_found:
                log("    ⚠️ NO apareció Role_Message")

            await cerrar_modales(target_frame)
            await asyncio.sleep(1)
            await screenshot(page, f"{timestamp}_08_post_role_message.png")

            # ── 10. VER ARCHIVOS EXISTENTES ──
            log("📂 [10/11] Verificando archivos existentes...")
            await target_frame.get_by_text("ExcelSchedule download").click()
            rl_modal = target_frame.locator("#ReserveList_View")
            try:
                await rl_modal.wait_for(state="visible", timeout=30000)
                log("    ✅ ReserveList_View visible")
            except:
                log("    ❌ ReserveList_View NO se abrió")
                await screenshot(page, f"{timestamp}_08b_error_modal.png")
                # Intentar cerrar cualquier cosa y reintentar
                await cerrar_modales(target_frame)
                await asyncio.sleep(2)
                await target_frame.get_by_text("ExcelSchedule download").click()
                await rl_modal.wait_for(state="visible", timeout=30000)

            dl_links_before = await rl_modal.locator("a", has_text="Download").all()
            count_before = len(dl_links_before)
            log(f"    Descargas existentes: {count_before}")

            # Mostrar nombres
            for i, link in enumerate(dl_links_before[:3]):
                try:
                    row = link.locator("..")
                    txt = await row.text_content()
                    log(f"       [{i}] {txt.replace(chr(10), ' ').strip()[:80]}")
                except:
                    pass

            await cerrar_modales(target_frame)
            await asyncio.sleep(1)
            await screenshot(page, f"{timestamp}_09_reservelist_before.png")

            # ── 11. POLLING ESPERANDO EL ARCHIVO NUEVO ──
            log("⏳ [11/11] Esperando que se genere el nuevo archivo...")
            log("    (Ourvend tarda ~5 min en generar)")

            max_wait = 360  # 6 minutos
            poll_interval = 15  # revisar cada 15 segundos
            waited = 0
            new_file_found = False
            last_count = count_before

            while waited < max_wait:
                await asyncio.sleep(poll_interval)
                waited += poll_interval

                # Abrir modal y contar
                await cerrar_modales(target_frame)
                await asyncio.sleep(1)

                try:
                    await target_frame.get_by_text("ExcelSchedule download").click()
                    await rl_modal.wait_for(state="visible", timeout=15000)
                except Exception as e:
                    log(f"    ⚠️ Error al abrir modal (waited {waited}s): {e}")
                    continue

                current_links = await rl_modal.locator("a", has_text="Download").all()
                current_count = len(current_links)
                log(f"    Poll {waited}s: {current_count} archivos")

                if current_count > count_before:
                    log(f"    ✅ NUEVO ARCHIVO! {count_before} → {current_count}")
                    # Mostrar el nuevo
                    for i, link in enumerate(current_links):
                        try:
                            row = link.locator("..")
                            txt = await row.text_content()
                            log(f"       [{i}] {txt.replace(chr(10), ' ').strip()[:100]}")
                        except:
                            pass
                    new_file_found = True
                    await screenshot(page, f"{timestamp}_10_nuevo_archivo.png")
                    break

                last_count = current_count

            if not new_file_found:
                log(f"    ⚠️ No se detectó nuevo archivo tras {max_wait}s")
                await screenshot(page, f"{timestamp}_10_sin_archivo.png")

            # ── 12. DESCARGAR ──
            log("⬇️ Descargando archivo...")
            download_link = rl_modal.locator("a", has_text="Download").first
            try:
                await download_link.wait_for(state="visible", timeout=30000)
            except:
                log("    ❌ No hay link Download visible")
                await screenshot(page, f"{timestamp}_11_error_download.png")
                raise Exception("No se encontró link de descarga")

            log("    Iniciando descarga (timeout 6min)...")
            async with page.expect_download(timeout=360000) as download_info:
                await download_link.click()

            download = await download_info.value
            machine_label = machine_id if machine_id else "ALL"
            filename = f"Report_{machine_label}_ALT_{download.suggested_filename}"
            save_path = os.path.join(DOWNLOAD_DIR, filename)
            await download.save_as(save_path)

            log(f"    ✅ ARCHIVO DESCARGADO: {save_path}")
            log("=" * 60)
            log("🏁 ALT SCRAPER FINALIZADO CON ÉXITO")
            log("=" * 60)

            _log_file.close()
            return save_path

        except Exception as e:
            log(f"❌ ERROR: {e}")
            traceback.print_exc()
            try:
                await screenshot(page, f"{timestamp}_FINAL_ERROR.png")
            except:
                pass
            log("=" * 60)
            log("💥 ALT SCRAPER FINALIZÓ CON ERROR")
            log("=" * 60)
            if _log_file:
                _log_file.close()
            return None

        finally:
            try:
                await browser.close()
            except:
                pass


def run(machine_id, start_date, end_date):
    return asyncio.run(run_async(machine_id, start_date, end_date))


if __name__ == "__main__":
    import argparse
    parser = argparse.ArgumentParser(description="ALT Ourvend scraper")
    parser.add_argument("--machine-id", type=str, default=DEFAULT_MACHINE_ID)
    parser.add_argument("--start-date", type=str, default=DEFAULT_START)
    parser.add_argument("--end-date", type=str, default=DEFAULT_END)
    args = parser.parse_args()
    run(args.machine_id, args.start_date, args.end_date)
