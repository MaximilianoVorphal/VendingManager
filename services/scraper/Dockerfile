# Usamos la imagen oficial de Playwright (incluye Python y browsers)
FROM mcr.microsoft.com/playwright/python:v1.40.0-jammy

WORKDIR /app

# Layer 1: Dependencias base — cambia solo cuando actualizás pip packages
# Esto mantiene cacheados Chromium y las libs aunque cambie el código
# Capa de dependencias — cambiada para incluir requests + cryptography
RUN pip install --no-cache-dir --upgrade pip && \
    pip install --no-cache-dir playwright fastapi uvicorn google-genai python-multipart pillow requests cryptography && \
    playwright install chromium

# Layer 2: Código — se invalida solo cuando tocás los scripts
COPY download_ourvend_report_alt.py .
COPY scraper_api.py .
COPY machine_status.py .
COPY report_sales_api.py .
COPY gemini_ocr.py .

# Crear directorio de descargas
RUN mkdir -p downloads

# Exponer el puerto de la API
EXPOSE 8000

# Comando de inicio
CMD ["uvicorn", "scraper_api:app", "--host", "0.0.0.0", "--port", "8000"]