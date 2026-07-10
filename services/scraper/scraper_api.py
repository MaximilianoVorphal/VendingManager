from fastapi import FastAPI, HTTPException, Query
from pydantic import BaseModel
import download_ourvend_report_alt
import machine_status
import report_sales_api
import os
import uvicorn

app = FastAPI()

# Definir el modelo de datos que esperamos recibir desde .NET
class DownloadRequest(BaseModel):
    machine_id: str = "2410280012"
    start_date: str
    end_date: str

from fastapi.responses import FileResponse, JSONResponse
from fastapi import BackgroundTasks, UploadFile, File

# Lazy import: solo se necesita para los endpoints OCR
gemini_ocr = None
def _get_gemini_ocr():
    global gemini_ocr
    if gemini_ocr is None:
        import gemini_ocr as _gemini_ocr
        gemini_ocr = _gemini_ocr
    return gemini_ocr

@app.post("/download")
async def download_report(req: DownloadRequest, background_tasks: BackgroundTasks):
    """
    Scraper Ourvend:
    - Idioma inglés
    - Solo selecciona grupo (todas las máquinas)
    - Orden correcto: Query → Export → Export all data → ExcelSchedule download
    """
    try:
        print(f"[ALT] Recibida solicitud para máquina '{req.machine_id}' [{req.start_date} - {req.end_date}]")

        # Ejecutar el script alternativo
        file_path = await download_ourvend_report_alt.run_async(req.machine_id, req.start_date, req.end_date)

        if not file_path or not os.path.exists(file_path):
             raise HTTPException(status_code=500, detail="No se pudo descargar el archivo (ALT)")

        # Programar borrado
        def cleanup():
            try:
                os.remove(file_path)
            except: pass

        background_tasks.add_task(cleanup)

        # Retornar Archivo
        filename = os.path.basename(file_path)
        return FileResponse(file_path, media_type='application/vnd.ms-excel', filename=filename)

    except Exception as e:
        print(f"[ALT] Error: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/ocr/invoice")
async def ocr_invoice(file: UploadFile = File(...)):
    try:
        # Save temp file
        temp_file_path = f"downloads/temp_{file.filename}"
        with open(temp_file_path, "wb") as buffer:
            buffer.write(await file.read())
        
        # Process with Gemini
        result = _get_gemini_ocr().extract_invoice_data(temp_file_path)
        
        # Clean up temp file
        try:
            os.remove(temp_file_path)
        except:
            pass
            
        return JSONResponse(content=result)
    except Exception as e:
        print(f"Error OCR: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.post("/api/ocr/recarga")
async def ocr_recarga(file: UploadFile = File(...)):
    try:
        # Save temp file
        temp_file_path = f"downloads/temp_recarga_{file.filename}"
        with open(temp_file_path, "wb") as buffer:
            buffer.write(await file.read())

        try:
            # Process with Gemini
            result = _get_gemini_ocr().extract_recarga_data(temp_file_path)
        finally:
            # Clean up temp file
            try:
                os.remove(temp_file_path)
            except:
                pass

        return JSONResponse(content=result)
    except Exception as e:
        print(f"Error OCR recarga: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/api/machines/status")
async def get_machines_status():
    """
    Devuelve el estado en tiempo real de todas las máquinas desde Ourvend.
    Usa la API JSON (OperateMonitor/ListJson), sin Playwright.
    """
    try:
        session = machine_status.login_and_get_session()
        if not session:
            raise HTTPException(status_code=502, detail="No se pudo autenticar en Ourvend")

        machines = machine_status.get_machine_list(session)
        return {
            "machines": [
                {
                    "machine_id": m["MId"],
                    "name": m.get("MiAlias", m["MId"]),
                    "status": (
                        "online" if m.get("MiNoline", 0) >= 1 else
                        "offline"
                    ),
                    "noline": m.get("MiNoline", 0),
                    "temperature": m.get("MiInsideTemp", "N/A"),
                    "door": "open" if m.get("Door") == 1 else "closed",
                    "coin_acceptor": m.get("Coin", "N/A"),
                    "selection": m.get("Selection", "N/A"),
                    "version": m.get("Versions", "N/A"),
                    "group": m.get("MGroupName", "N/A"),
                }
                for m in machines
            ],
            "summary": machine_status.get_quick_stats(session),
        }

    except Exception as e:
        print(f"[API] Error obteniendo estado de máquinas: {e}")
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/api/sales/report")
async def get_sales_report(
    start: str = Query(..., description="Fecha inicio YYYY-MM-DD"),
    end: str = Query(..., description="Fecha fin YYYY-MM-DD"),
    machine_id: str = Query("", description="ID de máquina (vacío = todas)"),
):
    """
    Devuelve ventas de Ourvend en JSON usando el browser path (stealth
    Playwright login + in-page fetch). La respuesta incluye un ``status``
    field que permite al caller (``ScraperClient`` en .NET) distinguir
    entre éxito, sin datos, bloqueo WAF, error y timeout.

    Respuesta (200): { status: "ok"|"empty", total, total_amount, rows }
    Respuesta (503): { status: "blocked"|"error"|"timeout", reason }
    """
    try:
        data = await report_sales_api.fetch_sales_via_browser(
            start_date=start,
            end_date=end,
            machine_id=machine_id,
            rows=2000,
            debug=False,
        )

        classification = report_sales_api.classify_response(data)

        if classification in ("ok", "empty"):
            rows = data.get("rows", [])
            formatted = [report_sales_api.format_row(r) for r in rows]
            return {
                "status": classification,
                "total": len(formatted),
                "total_amount": sum(r.get("price", 0) or 0 for r in formatted),
                "rows": formatted,
            }
        else:
            reason = data.get("_reason", str(data)) if isinstance(data, dict) else str(data)
            print(f"[API] Sales report classified as {classification}: {reason}")
            raise HTTPException(
                status_code=503,
                detail={
                    "status": classification,
                    "reason": reason,
                },
            )

    except HTTPException:
        raise
    except Exception as e:
        print(f"[API] Error obteniendo ventas: {e}")
        raise HTTPException(
            status_code=503,
            detail={"status": "error", "reason": str(e)},
        )


@app.get("/health")
def health_check():
    return {"status": "ok", "service": "Vending Scraper"}

if __name__ == "__main__":
    # Escuchar en todas las interfaces para que Docker lo vea
    uvicorn.run(app, host="0.0.0.0", port=8000)