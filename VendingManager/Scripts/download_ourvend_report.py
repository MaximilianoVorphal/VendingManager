import time
import os
import asyncio
from datetime import datetime
from playwright.async_api import async_playwright, expect

# Configuración
DOWNLOAD_DIR = os.path.join(os.getcwd(), "downloads")
USERNAME = os.environ.get("OURVEND_USER")
PASSWORD = os.environ.get("OURVEND_PASS")

if not USERNAME or not PASSWORD:
    print("❌ ERROR: Credenciales OURVEND_USER / OURVEND_PASS no encontradas en variables de entorno.")
    # No salimos aquí para permitir que argumentos de CLI (si los hubiera en el futuro) pudieran sobreescribirlo,
    # pero para este script simple, es crítico.

import argparse
import sys

# Defaults
DEFAULT_MACHINE_ID = "2410280012"
now = datetime.now()
DEFAULT_START = now.strftime("%Y-%m-01")
DEFAULT_END = now.strftime("%Y-%m-%d")

async def run_async(machine_id, start_date, end_date):
    if not os.path.exists(DOWNLOAD_DIR):
        os.makedirs(DOWNLOAD_DIR)

    async with async_playwright() as p:
        # Lanzar navegador
        browser = await p.chromium.launch(headless=True, args=["--no-sandbox", "--disable-setuid-sandbox"])
        context = await browser.new_context(accept_downloads=True)
        page = await context.new_page()

        print(f"--> Iniciando proceso para Máquina: {machine_id} | Fechas: {start_date} a {end_date}")
        
        try:
            print("--> Accesando al login de Ourvend...")
            await page.goto("https://os.ourvend.com/Account/Login", timeout=60000)

            # 1. Verificar/Cambiar idioma a Inglés
            try:
                lang_en = page.locator('a[href*="lang=en-us"]')
                if await lang_en.is_visible(timeout=3000):
                    await lang_en.click()
                    await page.wait_for_load_state('networkidle')
            except:
                pass

            # 2. Login
            print(f"--> Iniciando sesión como {USERNAME}...")
            await page.fill("#userName", USERNAME)
            await page.fill("#passWord", PASSWORD)
            
            # Disparar login
            print("--> Ejecutando UserLogin() via JS...")
            await page.evaluate("UserLogin()")

            # Esperar dashboard
            await page.wait_for_url("**/YSTemplet/index*", timeout=60000)
            print("--> Login exitoso.")

            # 3. Navegar a Reporte
            print("--> Navegando a History > Product release record...")
            await page.get_by_role("link", name="History").click()
            await page.get_by_role("link", name="Product realease record").click()
            
            # Buscar el frame del reporte
            target_frame = None
            print("    Buscando frame del reporte...")
            
            # Reintentos inteligentes para encontrar el frame
            for attempt in range(20):
                for frame in page.frames:
                    try:
                        # Buscamos un elemento único del reporte dentro del frame
                        if await frame.locator("#IndexTime").count() > 0:
                            target_frame = frame
                            print("    ✅ Frame encontrado.")
                            break
                    except:
                        continue
                if target_frame: break
                await asyncio.sleep(1) # Polling necesario para frames dinámicos

            if not target_frame:
                raise Exception("No se encontró el frame del reporte.")

            # 4. Establecer Filtros
            print("--> Configurando filtros...")

            # MACHINE GROUPING
            try:
                # Intentar selector más específico primero
                group_select = target_frame.locator("#MiGroup")
                if await group_select.is_visible(timeout=2000):
                     await group_select.select_option(label="UNIDAD PREDETERMINADA")
                else:
                    # Fallback UI
                    await target_frame.locator("text=Please choose").first.click()
                    await target_frame.locator("text=UNIDAD PREDETERMINADA").first.click()
            except Exception as e:
                print(f"    ⚠️ Warning en selección de grupo: {e}")

            # MACHINE ID (DINÁMICO)
            print(f"--> Seleccionando Máquina: {machine_id}")
            # Inyección JS directa para mayor fiabilidad con dropdowns custom
            js_select_machine = f"""
                (() => {{
                    var options = document.querySelectorAll('option');
                    for(var i=0; i<options.length; i++){{
                        if(options[i].text.includes('{machine_id}')){{
                            options[i].selected = true;
                            options[i].parentElement.dispatchEvent(new Event('change'));
                            return true;
                        }}
                    }}
                    return false;
                }})()
            """
            found = await target_frame.evaluate(js_select_machine)
            if not found:
                print(f"    ⚠️ No se encontró la máquina {machine_id} en el listado.")

            # FECHAS (DINÁMICAS via JS)
            print(f"--> Inyectando fechas: {start_date} a {end_date}")
            js_dates = f"""
                var start = document.getElementById('IndexTime');
                var startHMS = document.getElementById('IndexTimeHMS');
                var end = document.getElementById('LastTime');
                var endHMS = document.getElementById('LastTimeHMS');
                
                if(start) start.value = '{start_date}';
                if(startHMS) startHMS.value = '00:00:00';
                if(end) end.value = '{end_date}';
                if(endHMS) endHMS.value = '23:59:59';
            """
            await target_frame.evaluate(js_dates)

            # QUERY
            print("--> Ejecutando consulta (Query)...")
            await target_frame.get_by_text("Query").click()
            
            # Esperar a que la tabla cargue o cambie (indicador visual opcional, o espera fija reducida)
            await asyncio.sleep(2) # Breve pausa para que la tabla refresque

            # 5. Exportar
            print("--> Iniciando Exportación...")
            export_btn = target_frame.get_by_text("Export", exact=True)
            if await export_btn.is_visible():
                await export_btn.click()
                
                # Esperar y clickear "Export all"
                export_all = target_frame.get_by_text("Export all")
                await export_all.wait_for(state="visible", timeout=5000)
                await export_all.click()

                # Manejo robusto de Modales en bucle (Polling)
                print("    Gestionando modales de interrupción (Loop)...")
                
                # Intentamos gestionar los modales durante 10 segundos
                # Esto es necesario porque el Role_Message puede tardar en aparecer tras el CheckExportExecl
                start_time = time.time()
                while time.time() - start_time < 15:
                    handled_something = False
                    
                    # 1. Modal "CheckExportExecl" (Choose data to export...)
                    try:
                        check_export_modal = target_frame.locator("#CheckExportExecl")
                        if await check_export_modal.is_visible(timeout=500):
                            print("    Detectado modal CheckExportExecl.")
                            confirm_btn = check_export_modal.get_by_role("button", name="OK").or_(
                                          check_export_modal.get_by_role("button", name="Export")).or_(
                                          check_export_modal.locator(".btn-primary"))
                            if await confirm_btn.count() > 0:
                                await confirm_btn.first.click()
                                await check_export_modal.wait_for(state="hidden", timeout=3000)
                                handled_something = True
                    except: pass

                    # 2. Modal "Role_Message" (Alertas de éxito/info)
                    try:
                        role_message_modal = target_frame.locator("#Role_Message")
                        if await role_message_modal.is_visible(timeout=500):
                            print("    Detectado modal Role_Message.")
                            # Buscar botón para cerrar (OK/Close)
                            ok_btn = role_message_modal.get_by_role("button", name="OK").or_(
                                     role_message_modal.locator("button.btn-primary")).or_(
                                     role_message_modal.locator("button.btn-default"))
                            
                            if await ok_btn.count() > 0:
                                await ok_btn.first.click()
                                await role_message_modal.wait_for(state="hidden", timeout=3000)
                                handled_something = True
                    except: pass
                    
                    if not handled_something:
                        await asyncio.sleep(0.5)
                    else:
                        # Si gestionamos algo, damos un respiro y seguimos por si sale otro
                        await asyncio.sleep(1)

                
                # Espera final de seguridad
                await asyncio.sleep(1)

            # 6. Descargar
            print("--> Yendo a ExcelSchedule download...")
            # Forzamos cierre de cualquier modal residual con JS si sigue ahí, por si acaso
            await target_frame.evaluate("""
                $('.modal').modal('hide');
                $('.modal-backdrop').remove();
            """)
            
            await target_frame.get_by_text("ExcelSchedule download").click()
            
            # Esperar a que aparezca el modal con la lista de descargas
            print("--> Esperando lista de descargas (ReserveList_View)...")
            reserve_list_modal = target_frame.locator("#ReserveList_View")
            await reserve_list_modal.wait_for(state="visible", timeout=30000)

            # Selector específico DENTRO del modal
            # Buscamos el link "Download" dentro del modal #ReserveList_View para evitar ambigüedad
            print("--> Buscando link de descarga en el modal...")
            download_link = reserve_list_modal.locator("a", has_text="Download").first
            await download_link.wait_for(state="visible", timeout=30000)

            print("--> Descargando archivo...")
            async with page.expect_download(timeout=60000) as download_info:
                await download_link.click()
            
            download = await download_info.value
            filename = f"Report_{machine_id}_{download.suggested_filename}"
            save_path = os.path.join(DOWNLOAD_DIR, filename)
            await download.save_as(save_path)
            
            print(f"--> ¡ARCHIVO DESCARGADO!: {save_path}")
            return save_path

        except Exception as e:
            print(f"❌ ERROR: {e}")
            return None
        finally:
            await browser.close()

def run(machine_id, start_date, end_date):
    return asyncio.run(run_async(machine_id, start_date, end_date))

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Descargar reporte Ourvend')
    parser.add_argument('--machine-id', type=str, default=DEFAULT_MACHINE_ID)
    parser.add_argument('--start-date', type=str, default=DEFAULT_START)
    parser.add_argument('--end-date', type=str, default=DEFAULT_END)
    args = parser.parse_args()
    
    run(args.machine_id, args.start_date, args.end_date)
