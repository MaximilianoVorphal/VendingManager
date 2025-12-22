# Usamos la imagen oficial de Playwright (incluye Python y browsers)
FROM mcr.microsoft.com/playwright/python:v1.40.0-jammy

WORKDIR /app

# Instalar dependencias de Python explícitamente y actualizar pip
RUN pip install --no-cache-dir --upgrade pip && \
    pip install --no-cache-dir playwright fastapi uvicorn

# Asegurar instalación de navegadores (aunque la imagen base los traiga, esto repara links rotos)
RUN playwright install chromium

# Copiar los scripts al contenedor
COPY download_ourvend_report.py .
COPY scraper_api.py .

# Crear directorio de descargas
RUN mkdir -p downloads

# Exponer el puerto de la API
EXPOSE 8000

# Comando de inicio: Ejecutar la API usando uvicorn directamente para mejor rendimiento/logs
CMD ["uvicorn", "scraper_api:app", "--host", "0.0.0.0", "--port", "8000"]
