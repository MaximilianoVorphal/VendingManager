from fastapi import FastAPI, HTTPException
from pydantic import BaseModel
import download_ourvend_report
import os
import uvicorn

app = FastAPI()

# Definir el modelo de datos que esperamos recibir desde .NET
class DownloadRequest(BaseModel):
    machine_id: str = "2410280012"
    start_date: str
    end_date: str

from fastapi.responses import FileResponse
from fastapi import BackgroundTasks

@app.post("/download")
async def download_report(req: DownloadRequest, background_tasks: BackgroundTasks):
    try:
        print(f"Recibida solicitud para máquina {req.machine_id} [{req.start_date} - {req.end_date}]")
        
        # Ejecutar el script (ahora retorna el path)
        file_path = await download_ourvend_report.run_async(req.machine_id, req.start_date, req.end_date)
        
        if not file_path or not os.path.exists(file_path):
             raise HTTPException(status_code=500, detail="No se pudo descargar el archivo")

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
        print(f"Error: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/health")
def health_check():
    return {"status": "ok", "service": "Vending Scraper"}

if __name__ == "__main__":
    # Escuchar en todas las interfaces para que Docker lo vea
    uvicorn.run(app, host="0.0.0.0", port=8000)
