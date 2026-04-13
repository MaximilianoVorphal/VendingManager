# 003. Uso de Google Gemini Vision para extracción OCR de facturas

## Estado
Aceptado

## Contexto
Actualmente, el ingreso de compras de mercadería se hacía manualmente, transcribiendo los datos desde fotografías enviadas por WhatsApp. Para agilizar este proceso y reducir el error humano al teclear facturas con múltiples ítems y distintos costos unitarios, se evaluaron soluciones de Reconocimiento Óptico de Caracteres (OCR).

Las soluciones de OCR tradicionales a menudo fallan al interpretar la estructura tabular irregular de distintas facturas de proveedores. Los Modelos de Lenguaje Grande multimodales (como GPT-4o o Gemini 1.5) han demostrado una capacidad superior para entender un formato no estructurado y devolver JSON directamente con los datos mapeados.

## Decisión
Se decidió implementar **Google Gemini (gemini-1.5-flash o gemini-1.5-pro)** utilizando la librería `google-generativeai` en el microservicio en Python (Scraper Service).

**Flujo:**
1. El usuario sube la imagen de la factura mediante el Frontend de Blazor (`NuevaCompra.razor`).
2. El API backend (`ComprasController`) hace un proxy multipart del archivo hacia el endpoint Python `/api/ocr/invoice`.
3. Python utiliza Gemini para interpretar la boleta/factura y extraer Proveedor, Fecha, N° Documento e Ítems.
4. Python retorna el JSON estructurado, y el Backend realiza un "Fuzzy Match" básico para sugerir el ID del producto que ya existe en nuestra BD.
5. El Frontend prellena la tabla y el usuario corrobora antes de guardar.

## Consecuencias

### Positivas
*   **Ahorro de Tiempo:** Reducirá drásticamente el tiempo empleado en la gestión de compras.
*   **Flexibilidad:** Gemini puede interpretar cualquier formato de factura de distintos proveedores chilenos o extranjeros sin necesidad de crear plantillas estáticas.
*   **Bajo Acoplamiento:** Delegar el manejo de la IA al microservicio Python aísla las cargas de trabajo intensas, manteniendo el backend en .NET enfocado en la lógica de dominio (Costo Promedio, Inventario).

### Negativas / Riesgos
*   Requiere gestionar un token API (`GEMINI_API_KEY`) que debe rotarse y mantenerse seguro.
*   Al depender de una API en la nube (Google), si esta presenta intermitencias o problemas de red, el OCR automático fallará. Para mitigar esto, el ingreso manual de productos sigue estando habilitado 100% de manera auxiliar.
*   "Fuzzy Match" no es perfecto. Nombres abrevidados en boletas (ej: "Beb. Coc.Zero") podrían no hacer match siempre. El operador humano debe supervisar.
