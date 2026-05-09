from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import download_ourvend_report_alt
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
import gemini_ocr

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
        result = gemini_ocr.extract_invoice_data(temp_file_path)
        
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
            result = gemini_ocr.extract_recarga_data(temp_file_path)
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

@app.get("/health")
def health_check():
    return {"status": "ok", "service": "Vending Scraper"}

if __name__ == "__main__":
    # Escuchar en todas las interfaces para que Docker lo vea
    uvicorn.run(app, host="0.0.0.0", port=8000)