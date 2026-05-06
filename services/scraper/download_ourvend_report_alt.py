import time
import os
import asyncio
from datetime import datetime
from playwright.async_api import async_playwright

# Configuración
DOWNLOAD_DIR = os.path.join(os.getcwd(), "downloads")
USERNAME = os.environ.get("OURVEND_USER")
PASSWORD = os.environ.get("OURVEND_PASS")

if not USERNAME or not PASSWORD:
    print("❌ ERROR: Credenciales OURVEND_USER / OURVEND_PASS no encontradas en variables de entorno.")

import argparse
import sys

# Defaults
DEFAULT_MACHINE_ID = ""   # Vacío = todas las máquinas
now = datetime.now()
DEFAULT_START = now.strftime("%Y-%m-01")
DEFAULT_END = now.strftime("%Y-%m-%d")

MACHINE_GROUP_VALUE = "53aed77a-1c1b-40b2-a9be-d40d98adb348"  # UUID de UNIDAD PREDETERMINADA


async def run_async(machine_id, start_date, end_date):
    """
    Versión alternativa del scraper con:
    - Idioma inglés (click en English al inicio)
    - Solo selecciona grupo de máquinas (NO selecciona MachineID = todas las máquinas)
    - Orden correcto: Query → Export → Export all data → Role_Message → ExcelSchedule download → Download
    """
    if not os.path.exists(DOWNLOAD_DIR):
        os.makedirs(DOWNLOAD_DIR)

    # Crear directorio de debug para screenshots
    debug_dir = os.path.join(DOWNLOAD_DIR, "debug_alt")
    if not os.path.exists(debug_dir):
        os.makedirs(debug_dir)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=["--no-sandbox", "--disable-setuid-sandbox"])
        context = await browser.new_context(accept_downloads=True)
        page = await context.new_page()

        print(f"--> [ALT] Iniciando proceso para TODAS las máquinas | Fechas: {start_date} a {end_date}")

        try:
            print("--> Accesando al login de Ourvend...")
            await page.goto("https://os.ourvend.com/Account/Login", timeout=60000)

            # =====================================================
            # 1. CAMBIAR A INGLÉS
            # =====================================================
            print("--> Cambiando idioma a Inglés...")
            try:
                lang_en = page.locator('a[href*="lang=en-us"]')
                if await lang_en.is_visible(timeout=3000):
                    await lang_en.click()
                    await page.wait_for_load_state('networkidle')
                    print("    ✅ Idioma cambiado a Inglés")
            except Exception as e:
                print(f"    ⚠️ Warning cambio de idioma: {e}")

            await page.wait_for_timeout(1000)

            # =====================================================
            # 2. LOGIN
            # =====================================================
            print(f"--> Iniciando sesión como {USERNAME}...")
            await page.fill("#userName", USERNAME)
            await page.fill("#passWord", PASSWORD)

            print("--> Ejecutando UserLogin() via JS...")
            await page.evaluate("UserLogin()")

            await page.wait_for_url("**/YSTemplet/index*", timeout=60000)
            print("--> Login exitoso.")

            # =====================================================
            # 3. NAVEGAR A History > Product realease record
            # =====================================================
            print("--> Navegando a History > Product realease record...")
            await page.get_by_role("link", name="History").click()
            await page.wait_for_timeout(500)
            await page.get_by_role("link", name="Product realease record").click()
            await page.wait_for_timeout(1000)

            # =====================================================
            # 4. ENCONTRAR IFRAME #22 (OutReport/Index)
            # =====================================================
            target_frame = None
            print("    Buscando iframe del reporte...")

            for attempt in range(20):
                for frame in page.frames:
                    try:
                        if await frame.locator("#MiGroup").count() > 0:
                            target_frame = frame
                            print("    ✅ Iframe encontrado (OutReport/Index)")
                            break
                    except:
                        continue
                if target_frame:
                    break
                await asyncio.sleep(1)

            if not target_frame:
                raise Exception("No se encontró el iframe del reporte (OutReport/Index).")

            # =====================================================
            # 5. SELECCIONAR GRUPO SOLO (SIN MÁQUINA)
            # =====================================================
            print("--> Seleccionando grupo de máquinas (sin filtrar por máquina)...")
            try:
                await target_frame.select_option("#MiGroup", value=MACHINE_GROUP_VALUE)
                print("    ✅ Grupo seleccionado: UNIDAD PREDETERMINADA")
                await asyncio.sleep(3)  # Esperar a que carguen las máquinas
            except Exception as e:
                print(f"    ⚠️ Warning: {e}")

            # Verificar que MachineID se pobló (pero NO seleccionamos ninguna)
            machine_options = await target_frame.locator("#MachineID option").all()
            print(f"    Máquinas disponibles ({len(machine_options) - 1}): "
                  f"{[await opt.text_content() for opt in machine_options[1:5]]}...")

            # =====================================================
            # 6. CONFIGURAR FECHAS
            # =====================================================
            print(f"--> Configurando fechas: {start_date} a {end_date}")
            try:
                await target_frame.fill("#IndexTime", start_date)
                await target_frame.fill("#IndexTimeHMS", "00:00:00")
                await target_frame.fill("#LastTime", end_date)
                await target_frame.fill("#LastTimeHMS", "23:59:59")
                print("    ✅ Fechas configuradas")
            except Exception as e:
                # Fallback: inyectar via JavaScript
                print(f"    ⚠️ Fill falló, usando JS: {e}")
                js_dates = f"""
                    (() => {{
                        var start = document.getElementById('IndexTime');
                        var startHMS = document.getElementById('IndexTimeHMS');
                        var end = document.getElementById('LastTime');
                        var endHMS = document.getElementById('LastTimeHMS');
                        if(start) {{ start.value = '{start_date}'; }}
                        if(startHMS) {{ startHMS.value = '00:00:00'; }}
                        if(end) {{ end.value = '{end_date}'; }}
                        if(endHMS) {{ endHMS.value = '23:59:59'; }}
                        return 'done';
                    }})()
                """
                await target_frame.evaluate(js_dates)
                print("    ✅ Fechas configuradas via JS")

            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_01_before_query.png"))
            print(f"    📸 Screenshot: {timestamp}_01_before_query.png")

            # =====================================================
            # 7. EJECUTAR QUERY
            # =====================================================
            print("--> Ejecutando Query (sin filtrar por máquina)...")
            try:
                await target_frame.locator("#btnSearch").click()
                print("    ✅ Query ejecutado")
            except Exception as e:
                print(f"    ⚠️ Click falló, intentando JS: {e}")
                await target_frame.evaluate("document.getElementById('btnSearch').click()")

            # Esperar a que carguen los datos
            await asyncio.sleep(5)

            # Verificar filas
            table_rows = await target_frame.locator("table tbody tr.ui-row-ltr, table tbody tr.ui-widget-content").all()
            row_count = len(table_rows)
            print(f"    Filas obtenidas: {row_count}")

            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_02_after_query.png"))
            print(f"    📸 Screenshot: {timestamp}_02_after_query.png")

            # =====================================================
            # 8. EXPORT (primero, SIN abrir ExcelSchedule download)
            # =====================================================
            print("--> Iniciando Exportación...")

            # 8a. Click Export
            try:
                await target_frame.locator("#btnExport").click()
                print("    ✅ Click en Export")
            except Exception as e:
                print(f"    ⚠️ Click Export falló, intentando JS: {e}")
                await target_frame.evaluate("document.getElementById('btnExport').click()")

            await asyncio.sleep(2)

            # 8b. Click "Export all data"
            print("    Click en 'Export all data'...")
            try:
                export_all_btn = target_frame.get_by_role("button", name="Export all data")
                if await export_all_btn.is_visible(timeout=5000):
                    await export_all_btn.click()
                    print("    ✅ Click en Export all data")
                else:
                    raise Exception("Botón no visible")
            except Exception as e:
                print(f"    ⚠️ Click falló, intentando JS: {e}")
                js_export_all = """
                    (() => {
                        var modal = document.getElementById('CheckExportExecl');
                        if (modal) {
                            var buttons = modal.querySelectorAll('button');
                            for (var btn of buttons) {
                                if (btn.textContent.includes('Export all data')) {
                                    btn.click();
                                    return 'clicked';
                                }
                            }
                        }
                        return 'not found';
                    })()
                """
                result = await target_frame.evaluate(js_export_all)
                print(f"    Resultado JS: {result}")

            # 8c. Esperar Role_Message
            print("    Esperando confirmación (Role_Message)...")
            start_time = time.time()
            role_found = False
            while time.time() - start_time < 20:
                try:
                    role_message = target_frame.locator("#Role_Message")
                    if await role_message.is_visible(timeout=1000):
                        msg_text = await role_message.text_content()
                        print(f"    ✅ Role_Message detectado: {msg_text[:100] if msg_text else 'N/A'}")

                        close_btn = role_message.locator("button")
                        if await close_btn.count() > 0:
                            await close_btn.first.click()
                            print("    ✅ Role_Message cerrado")
                            role_found = True
                            await asyncio.sleep(1)
                            break
                except:
                    pass
                await asyncio.sleep(0.5)

            if not role_found:
                print("    ⚠️ No se detectó Role_Message, continuando de todas formas...")

            # Cerrar cualquier modal abierto
            await target_frame.evaluate("""
                $('.modal').modal('hide');
                $('.modal-backdrop').remove();
            """)
            await asyncio.sleep(1)

            # =====================================================
            # 9. CONTAR ARCHIVOS EXISTENTES (antes de generar nuevo)
            # =====================================================
            print("--> Contando archivos existentes antes de exportar...")
            await target_frame.get_by_text("ExcelSchedule download").click()
            reserve_list_modal = target_frame.locator("#ReserveList_View")
            await reserve_list_modal.wait_for(state="visible", timeout=30000)

            existing_downloads = await reserve_list_modal.locator("a", has_text="Download").all()
            count_before = len(existing_downloads)
            print(f"    Archivos existentes ANTES de exportar: {count_before}")

            # Cerrar modal
            try:
                await target_frame.evaluate("$('#ReserveList_View').modal('hide');")
            except:
                pass
            await asyncio.sleep(1)

            # =====================================================
            # 10. RE-ABRIR RESERVE LIST Y POLL PARA NUEVO ARCHIVO
            # =====================================================
            print("--> Esperando que el nuevo reporte esté disponible...")
            await target_frame.get_by_text("ExcelSchedule download").click()
            await reserve_list_modal.wait_for(state="visible", timeout=30000)

            max_wait = 300  # 5 minutos (Ourvend tarda ~5 min en generar)
            poll_interval = 5
            waited = 0
            count_after = count_before

            while waited < max_wait:
                current_downloads = await reserve_list_modal.locator("a", has_text="Download").all()
                count_after = len(current_downloads)

                if count_after > count_before:
                    print(f"    ✅ Nuevo archivo detectado! ({count_before} -> {count_after})")
                    break

                print(f"    Esperando nuevo archivo... ({waited}s, archivos: {count_after})")
                await asyncio.sleep(poll_interval)
                waited += poll_interval

                # Re-abrir modal por si se cerró
                await target_frame.evaluate("$('#ReserveList_View').modal('hide');")
                await asyncio.sleep(0.5)
                await target_frame.get_by_text("ExcelSchedule download").click()
                await reserve_list_modal.wait_for(state="visible", timeout=10000)

            if count_after <= count_before:
                print(f"    ⚠️ No se detectó nuevo archivo después de {max_wait}s")

            # =====================================================
            # 11. DESCARGAR EL ARCHIVO MÁS RECIENTE
            # =====================================================
            print("--> Descargando el archivo más reciente...")
            download_link = reserve_list_modal.locator("a", has_text="Download").first
            await download_link.wait_for(state="visible", timeout=30000)

            print("--> Iniciando descarga...")
            async with page.expect_download(timeout=360000) as download_info:  # 6 min timeout
                await download_link.click()

            download = await download_info.value
            machine_label = machine_id if machine_id else "ALL"
            filename = f"Report_{machine_label}_ALT_{download.suggested_filename}"
            save_path = os.path.join(DOWNLOAD_DIR, filename)
            await download.save_as(save_path)

            print(f"--> ✅ ¡ARCHIVO DESCARGADO! (ALT): {save_path}")
            return save_path

        except Exception as e:
            print(f"❌ ERROR: {e}")
            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_FINAL_ERROR.png"))
            return None
        finally:
            await browser.close()


def run(machine_id, start_date, end_date):
    return asyncio.run(run_async(machine_id, start_date, end_date))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Descargar reporte Ourvend (ALT)')
    parser.add_argument('--machine-id', type=str, default=DEFAULT_MACHINE_ID)
    parser.add_argument('--start-date', type=str, default=DEFAULT_START)
    parser.add_argument('--end-date', type=str, default=DEFAULT_END)
    args = parser.parse_args()

    run(args.machine_id, args.start_date, args.end_date)
