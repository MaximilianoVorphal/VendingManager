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
    
    # Crear directorio de debug para screenshots
    debug_dir = os.path.join(DOWNLOAD_DIR, "debug")
    if not os.path.exists(debug_dir):
        os.makedirs(debug_dir)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")

    async with async_playwright() as p:
        browser = await p.chromium.launch(headless=True, args=["--no-sandbox", "--disable-setuid-sandbox"])
        context = await browser.new_context(accept_downloads=True)
        page = await context.new_page()

        print(f"--> Iniciando proceso para Máquina: {machine_id} | Fechas: {start_date} a {end_date}")
        
        try:
            print("--> Accesando al login de Ourvend...")
            await page.goto("https://os.ourvend.com/Account/Login", timeout=60000)

            try:
                lang_en = page.locator('a[href*="lang=en-us"]')
                if await lang_en.is_visible(timeout=3000):
                    await lang_en.click()
                    await page.wait_for_load_state('networkidle')
            except:
                pass

            print(f"--> Iniciando sesión como {USERNAME}...")
            await page.fill("#userName", USERNAME)
            await page.fill("#passWord", PASSWORD)
            
            print("--> Ejecutando UserLogin() via JS...")
            await page.evaluate("UserLogin()")

            await page.wait_for_url("**/YSTemplet/index*", timeout=60000)
            print("--> Login exitoso.")

            print("--> Navegando a History > Product release record...")
            await page.get_by_role("link", name="History").click()
            await page.get_by_role("link", name="Product realease record").click()
            
            target_frame = None
            print("    Buscando frame del reporte...")
            
            for attempt in range(20):
                for frame in page.frames:
                    try:
                        if await frame.locator("#IndexTime").count() > 0:
                            target_frame = frame
                            print("    ✅ Frame encontrado.")
                            break
                    except:
                        continue
                if target_frame: break
                await asyncio.sleep(1)

            if not target_frame:
                raise Exception("No se encontró el frame del reporte.")

            # =====================================================
            # CONFIGURAR FILTROS (MEJORADO)
            # =====================================================
            print("--> Configurando filtros...")
            
            # Esperar a que el formulario esté completamente cargado
            await asyncio.sleep(2)

            # 1. MACHINE GROUPING - Seleccionar el grupo
            print("    Seleccionando grupo de máquinas...")
            try:
                group_select = target_frame.locator("#MiGroup")
                if await group_select.is_visible(timeout=3000):
                    await group_select.select_option(label="UNIDAD PREDETERMINADA")
                    print("    ✅ Grupo seleccionado: UNIDAD PREDETERMINADA")
                    await asyncio.sleep(1)  # Esperar a que se actualice el dropdown de máquinas
            except Exception as e:
                print(f"    ⚠️ Warning en selección de grupo: {e}")

            # 2. MACHINE ID - Solo si se especificó (si está vacío, dejamos "Please choose" para obtener todas)
            if machine_id and machine_id.strip():
                print(f"--> Seleccionando Máquina: {machine_id}")
                js_select_machine = f"""
                    (() => {{
                        var select = document.getElementById('MiMachineID');
                        if (select) {{
                            var options = select.querySelectorAll('option');
                            for(var i=0; i<options.length; i++){{
                                if(options[i].text.includes('{machine_id}')){{
                                    select.value = options[i].value;
                                    select.dispatchEvent(new Event('change'));
                                    return 'found: ' + options[i].text;
                                }}
                            }}
                            return 'not found in ' + options.length + ' options';
                        }}
                        return 'select not found';
                    }})()
                """
                result = await target_frame.evaluate(js_select_machine)
                print(f"    Resultado selección máquina: {result}")
            else:
                print("    Sin filtro de máquina específica (se descargarán todas)")

            # 3. FECHAS - Inyectar via JavaScript
            print(f"--> Inyectando fechas: {start_date} a {end_date}")
            js_dates = f"""
                (() => {{
                    var start = document.getElementById('IndexTime');
                    var startHMS = document.getElementById('IndexTimeHMS');
                    var end = document.getElementById('LastTime');
                    var endHMS = document.getElementById('LastTimeHMS');
                    
                    var results = [];
                    if(start) {{ start.value = '{start_date}'; results.push('start=' + start.value); }}
                    if(startHMS) {{ startHMS.value = '00:00:00'; results.push('startHMS=' + startHMS.value); }}
                    if(end) {{ end.value = '{end_date}'; results.push('end=' + end.value); }}
                    if(endHMS) {{ endHMS.value = '23:59:59'; results.push('endHMS=' + endHMS.value); }}
                    
                    return results.join(', ');
                }})()
            """
            result = await target_frame.evaluate(js_dates)
            print(f"    Fechas configuradas: {result}")

            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_01_before_query.png"))
            print(f"    📸 Screenshot: {timestamp}_01_before_query.png")

            # =====================================================
            # EJECUTAR QUERY (MEJORADO)
            # =====================================================
            print("--> Ejecutando consulta (Query)...")
            
            # Intentar click normal primero
            query_btn = target_frame.get_by_text("Query", exact=True)
            if await query_btn.is_visible(timeout=3000):
                await query_btn.click()
                print("    Click en Query ejecutado")
            else:
                # Fallback: usar JavaScript
                print("    Botón Query no visible, intentando via JS...")
                js_query = """
                    (() => {
                        var btn = document.querySelector('button.btn-info');
                        if (btn && btn.textContent.includes('Query')) {
                            btn.click();
                            return 'clicked via querySelector';
                        }
                        // Buscar por onclick
                        var buttons = document.querySelectorAll('button');
                        for (var b of buttons) {
                            if (b.textContent.trim() === 'Query') {
                                b.click();
                                return 'clicked: ' + b.outerHTML.substring(0, 100);
                            }
                        }
                        return 'Query button not found';
                    })()
                """
                result = await target_frame.evaluate(js_query)
                print(f"    JS Query result: {result}")

            # Esperar a que la tabla cargue
            print("    Esperando carga de datos...")
            await asyncio.sleep(5)  # Espera más larga para la consulta
            
            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_02_after_query.png"))
            print(f"    📸 Screenshot: {timestamp}_02_after_query.png")

            # =====================================================
            # VERIFICAR QUE HAY DATOS EN LA TABLA
            # =====================================================
            print("--> Verificando datos en la tabla...")
            
            # Contar filas en la tabla
            table_rows = await target_frame.locator("table tbody tr").all()
            row_count = len(table_rows)
            print(f"    Filas encontradas en tabla: {row_count}")
            
            # Verificar si hay un mensaje de "No records"
            no_records = await target_frame.locator("text=No records").count()
            view_info = target_frame.locator(".dataTables_info, .paginate_info")
            if await view_info.count() > 0:
                info_text = await view_info.first.text_content()
                print(f"    Info de paginación: {info_text}")
            
            if row_count == 0 or no_records > 0:
                print("    ⚠️ ADVERTENCIA: La tabla no tiene datos!")
                print("    Esto puede deberse a:")
                print("       - Filtros incorrectos")
                print("       - No hay ventas en el rango de fechas")
                print("       - El Query no se ejecutó correctamente")
                
                # Intentar Query de nuevo con JavaScript directo
                print("    Reintentando Query via JavaScript...")
                await target_frame.evaluate("typeof Query === 'function' && Query()")
                await asyncio.sleep(5)
                
                table_rows = await target_frame.locator("table tbody tr").all()
                row_count = len(table_rows)
                print(f"    Filas después de reintento: {row_count}")
                
                await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_03_after_retry_query.png"))

            # =====================================================
            # CONTAR ARCHIVOS EXISTENTES
            # =====================================================
            print("--> Contando archivos existentes antes de exportar...")
            await target_frame.evaluate("""
                $('.modal').modal('hide');
                $('.modal-backdrop').remove();
            """)
            
            await target_frame.get_by_text("ExcelSchedule download").click()
            reserve_list_modal = target_frame.locator("#ReserveList_View")
            await reserve_list_modal.wait_for(state="visible", timeout=30000)
            
            existing_downloads = await reserve_list_modal.locator("a", has_text="Download").all()
            count_before = len(existing_downloads)
            print(f"    Archivos existentes ANTES de exportar: {count_before}")
            
            if count_before > 0:
                print("    Lista de archivos existentes:")
                table_rows_dl = await reserve_list_modal.locator("table tbody tr").all()
                for idx, row in enumerate(table_rows_dl[:3]):
                    row_text = await row.text_content()
                    row_text = ' '.join(row_text.split())[:80]
                    print(f"       [{idx}] {row_text}")
            
            # Cerrar modal
            try:
                await target_frame.evaluate("$('#ReserveList_View').modal('hide');")
            except: pass
            await asyncio.sleep(1)

            # =====================================================
            # EXPORTAR (solo si hay datos)
            # =====================================================
            if row_count == 0:
                print("    ⚠️ No hay datos para exportar. Abortando exportación.")
                print("    Si hay archivos existentes, se descargará el más reciente.")
            else:
                print(f"--> Iniciando Exportación ({row_count} filas)...")
                
                export_btn = target_frame.get_by_text("Export", exact=True)
                if await export_btn.is_visible():
                    await export_btn.click()
                    await asyncio.sleep(1)
                    
                    export_all = target_frame.get_by_text("Export all")
                    await export_all.wait_for(state="visible", timeout=5000)
                    await export_all.click()
                    await asyncio.sleep(2)
                    
                    await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_04_export_modal.png"))

                    # Manejo del modal "CheckExportExecl"
                    print("    Esperando modal de exportación...")
                    
                    try:
                        check_export_modal = target_frame.locator("#CheckExportExecl")
                        await check_export_modal.wait_for(state="visible", timeout=10000)
                        print("    Modal CheckExportExecl visible")
                        
                        # Click en "Export all data" via JavaScript para mayor fiabilidad
                        js_click_export = """
                            (() => {
                                var modal = document.getElementById('CheckExportExecl');
                                if (modal) {
                                    var buttons = modal.querySelectorAll('button');
                                    for (var btn of buttons) {
                                        if (btn.textContent.includes('Export all data')) {
                                            btn.click();
                                            return 'clicked: Export all data';
                                        }
                                    }
                                }
                                return 'button not found';
                            })()
                        """
                        result = await target_frame.evaluate(js_click_export)
                        print(f"    {result}")
                        await asyncio.sleep(3)
                        
                        await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_05_after_export_click.png"))
                        
                    except Exception as e:
                        print(f"    ❌ Error en modal: {e}")

                    # Esperar modal Role_Message
                    print("    Esperando confirmación...")
                    start_time = time.time()
                    while time.time() - start_time < 20:
                        try:
                            role_message_modal = target_frame.locator("#Role_Message")
                            if await role_message_modal.is_visible(timeout=1000):
                                print("    ✅ Modal Role_Message detectado!")
                                msg_text = await role_message_modal.text_content()
                                print(f"    Mensaje: {msg_text[:100] if msg_text else 'N/A'}")
                                
                                ok_btn = role_message_modal.locator("button")
                                if await ok_btn.count() > 0:
                                    await ok_btn.first.click()
                                    await asyncio.sleep(1)
                                    break
                        except: pass
                        await asyncio.sleep(0.5)

            # =====================================================
            # ESPERAR NUEVO ARCHIVO
            # =====================================================
            print("--> Esperando que el nuevo reporte esté disponible...")
            await target_frame.evaluate("""
                $('.modal').modal('hide');
                $('.modal-backdrop').remove();
            """)
            await asyncio.sleep(1)
            
            await target_frame.get_by_text("ExcelSchedule download").click()
            await reserve_list_modal.wait_for(state="visible", timeout=30000)
            
            max_wait = 60
            poll_interval = 3
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
                
                await target_frame.evaluate("$('#ReserveList_View').modal('hide');")
                await asyncio.sleep(0.5)
                await target_frame.get_by_text("ExcelSchedule download").click()
                await reserve_list_modal.wait_for(state="visible", timeout=10000)
            
            if count_after <= count_before:
                print(f"    ⚠️ ADVERTENCIA: No se detectó nuevo archivo después de {max_wait}s")
            
            print("    Lista de archivos DESPUES de exportar:")
            table_rows_dl = await reserve_list_modal.locator("table tbody tr").all()
            for idx, row in enumerate(table_rows_dl[:3]):
                row_text = await row.text_content()
                row_text = ' '.join(row_text.split())[:80]
                marker = "--> " if idx == 0 else "    "
                print(f"       {marker}[{idx}] {row_text}")

            # =====================================================
            # DESCARGAR
            # =====================================================
            print("--> Descargando el archivo más reciente...")
            download_link = reserve_list_modal.locator("a", has_text="Download").first
            await download_link.wait_for(state="visible", timeout=30000)

            print("--> Iniciando descarga...")
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
            await page.screenshot(path=os.path.join(debug_dir, f"{timestamp}_FINAL_ERROR.png"))
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
