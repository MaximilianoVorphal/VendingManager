import time
import os
import asyncio
from datetime import datetime
from playwright.async_api import async_playwright

# Configuración
DOWNLOAD_DIR = os.path.join(os.getcwd(), "downloads")
USERNAME = "comercialflf"
PASSWORD = "Flf2121#"

import argparse
import sys

# Defaults (por si se ejecuta sin argumentos)
DEFAULT_MACHINE_ID = "2410280012"
now = datetime.now()
DEFAULT_START = now.strftime("%Y-%m-01")
DEFAULT_END = now.strftime("%Y-%m-%d")

async def run_async(machine_id, start_date, end_date):
    if not os.path.exists(DOWNLOAD_DIR):
        os.makedirs(DOWNLOAD_DIR)

    async with async_playwright() as p:
        # Lanzar navegador (headless=True para servidores Linux/Docker)
        browser = await p.chromium.launch(headless=True, args=["--no-sandbox", "--disable-setuid-sandbox"])
        context = await browser.new_context(accept_downloads=True)
        page = await context.new_page()

        print(f"--> Iniciando proceso para Máquina: {machine_id} | Fechas: {start_date} a {end_date}")
        print("--> Accesando al login de Ourvend...")
        await page.goto("https://os.ourvend.com/Account/Login")

        # 1. Verificar/Cambiar idioma a Inglés
        try:
            lang_en = page.locator('a[href*="lang=en-us"]')
            if await lang_en.is_visible():
                await lang_en.click()
                await asyncio.sleep(2)
        except:
            pass

        # 2. Login
        print(f"--> Iniciando sesión como {USERNAME}...")
        await page.fill("#userName", USERNAME)
        await page.fill("#passWord", PASSWORD)
        
        # Disparar login directamente via JS para evitar problemas de UI/Overlays
        print("--> Ejecutando UserLogin() via JS...")
        await page.evaluate("UserLogin()")

        # Esperar a que cargue el dashboard
        try:
            await page.wait_for_url("**/YSTemplet/index*", timeout=60000)
            print("--> Login exitoso.")
        except Exception as e:
             print(f"❌ ERROR CRÍTICO LOGIN: No se detectó redirección al Dashboard tras 60s. {e}")
             # Intentar capturar screenshot de error si es posible, o simplemente salir
             await browser.close()
             return None

        # 3. Navegar a Reporte
        print("--> Navegando a History > Product release record...")
        await page.locator("a:has-text('History')").click()
        await asyncio.sleep(1)
        await page.locator("a:has-text('Product realease record')").click()
        await asyncio.sleep(3) 

        # Buscar el frame con Reintentos
        target_frame = None
        for attempt in range(10): # Intentar por 20 segundos
            print(f"    Buscando frame del reporte (Intento {attempt+1}/10)...")
            
            # Recargar lista de frames (en async property se actualiza sola en el objeto page, pero igual)
            for frame in page.frames:
                try:
                    # Check if frame has the specific element
                    count = await frame.locator("#IndexTime").count()
                    if count > 0:
                        target_frame = frame
                        print("    ✅ Frame encontrado.")
                        break
                except:
                    continue
            
            if target_frame: break
            await asyncio.sleep(2)
        
        if not target_frame:
            print("ERROR: No se encontró el frame del reporte tras múltiples intentos.")
            # Debug: imprimir titulos de frames
            print(f"Frames disponibles: {len(page.frames)}")
            await browser.close()
            return None

        # 4. Establecer Filtros
        print("--> Configurando filtros...")
        
        # MACHINE GROUPING
        print("--> Seleccionando Grupo: UNIDAD PREDETERMINADA")
        try:
            await target_frame.locator("text=Please choose").first.click()
            await asyncio.sleep(1)
            await target_frame.locator("text=UNIDAD PREDETERMINADA").first.click()
        except:
            # Fallback a la bruta
            try:
                 await target_frame.select_option("#MiGroup", label="UNIDAD PREDETERMINADA")
            except: pass

        await asyncio.sleep(2)

        # MACHINE ID (DINÁMICO)
        print(f"--> Seleccionando Máquina: {machine_id}")
        await asyncio.sleep(2)
        try:
            # Opción A: Visual
            try:
                ph = target_frame.locator("text=Please choose")
                if await ph.count() > 0:
                    await ph.first.click()
                    await asyncio.sleep(0.5)
                
                await target_frame.locator(f"text={machine_id}").first.click(timeout=3000)
                print("    Máquina seleccionada (Click texto).")
            except:
                # Opción B: JS Injection usando el ID dinámico
                print("    Intentando selección forzada por JS...")
                js_select_machine = f"""
                    var options = document.querySelectorAll('option');
                    for(var i=0; i<options.length; i++){{
                        if(options[i].text.includes('{machine_id}')){{
                            options[i].selected = true;
                            options[i].parentElement.dispatchEvent(new Event('change'));
                            break;
                        }}
                    }}
                """
                await target_frame.evaluate(js_select_machine)

        except Exception as e:
             print(f"    Error seleccionando ID: {e}")

        await asyncio.sleep(2)

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
        await asyncio.sleep(1)

        # QUERY
        print("--> Ejecutando consulta (Query)...")
        await target_frame.click("text=Query")
        await asyncio.sleep(3)

        # 5. Exportar
        print("--> Iniciando Exportación...")
        try:
            await target_frame.click("text=Export")
        except: pass
        
        await asyncio.sleep(2)
        print("--> Confirmando 'Export all'...")
        try:
            await target_frame.click("text=Export all", timeout=5000)
        except: pass
        
        # Cerrar Diálogo Close (si existe)
        await asyncio.sleep(3)
        try:
            close_btn = target_frame.locator("text=Close").or_(target_frame.locator("button:has-text('Close')"))
            if await close_btn.count() > 0 and await close_btn.first.is_visible():
                await close_btn.first.click()
        except: pass
        
        await asyncio.sleep(1)

        # 6. Descargar - LÓGICA FINAL JS
        print("--> Yendo a ExcelSchedule download...")
        await target_frame.click("text=ExcelSchedule download")
        await asyncio.sleep(4) 

        print("--> Descargando el archivo más reciente (Método JS)...")
        try:
            async with page.expect_download(timeout=60000) as download_info:
                found = await target_frame.evaluate("""
                    () => {
                        var links = Array.from(document.querySelectorAll('a'));
                        var dlLink = links.find(el => el.textContent.trim() === 'Download');
                        if (dlLink) { dlLink.click(); return true; }
                        return false;
                    }
                """)
                if not found: print("    JS Warning: No se encontró link 'Download'.")
            
            download = await download_info.value
            # Incluir machine_id en el nombre del archivo para orden
            filename = f"Report_{machine_id}_{download.suggested_filename}"
            save_path = os.path.join(DOWNLOAD_DIR, filename)
            await download.save_as(save_path)
            print(f"--> ¡ARCHIVO DESCARGADO!: {save_path}")
            return save_path
            
        except Exception as e:
            print(f"--> Error fatal en descarga: {e}")
            return None

        await asyncio.sleep(2)
        await browser.close()

# Wrapper síncrono para mantener compatibilidad si se ejecuta directo
def run(machine_id, start_date, end_date):
    return asyncio.run(run_async(machine_id, start_date, end_date))

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description='Descargar reporte Ourvend')
    parser.add_argument('--machine-id', type=str, default=DEFAULT_MACHINE_ID, help='ID de la máquina (ej: 2410280012)')
    parser.add_argument('--start-date', type=str, default=DEFAULT_START, help='Fecha inicio YYYY-MM-DD')
    parser.add_argument('--end-date', type=str, default=DEFAULT_END, help='Fecha fin YYYY-MM-DD')
    
    args = parser.parse_args()
    run(args.machine_id, args.start_date, args.end_date)
