import os
import json
from PIL import Image
from google import genai
from google.genai import types


def _get_client() -> genai.Client:
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        raise ValueError("GEMINI_API_KEY environment variable is not set")
    return genai.Client(api_key=api_key)


_MODEL = "gemini-3-flash-preview"
_THINKING_CONFIG = types.GenerateContentConfig(
    thinking_config=types.ThinkingConfig(thinking_level="HIGH"),
)


def _call_gemini(prompt: str, image_path: str) -> str:
    """Generic helper: sends prompt + image to Gemini and returns raw text."""
    client = _get_client()
    img = Image.open(image_path)
    response = client.models.generate_content(
        model=_MODEL,
        contents=[prompt, img],
        config=_THINKING_CONFIG,
    )
    return response.text.replace("```json", "").replace("```", "").strip()


def _parse_json(text: str, label: str) -> dict:
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        print(f"Error parsing JSON from Gemini ({label}): {text}")
        raise ValueError(f"No se pudo decodificar la respuesta JSON de Gemini: {e}")


def extract_invoice_data(image_path: str) -> dict:
    """
    Extracts invoice data from an image using Gemini.
    Returns a dictionary structured with the invoice items and tax information.
    """
    prompt = """
Analiza esta imagen que corresponde a una factura, boleta o ticket de supermercado de Chile (ej: Alvi, Acuenta, Mayorista, Distribuidoras).
Extrae la información y retorna ÚNICAMENTE un objeto JSON válido con la siguiente estructura estricta, sin texto adicional ni formateos como ```json:

Sigue estos 3 pasos de razonamiento para analizar el documento:

PASO 1 — Identificar tipo de documento y base de precios:
- ¿Es "Factura Electrónica" o "Boleta"? Revisa el encabezado del documento.
- Si ves "Factura Electrónica", "RUT proveedor" o IVA desglosado en el pie → es FACTURA (precios NETOS + IVA)
- Si ves solo precios por producto y un total final sin desglose de IVA → es BOLETA (precios BRUTOS con IVA incluido)
- Guarda en 'tipo_documento': "FACTURA" o "BOLETA"

PASO 2 — Extraer resumen de impuestos del pie del documento:
- Busca en la parte inferior del documento la sección de resumen de impuestos.
- Extrae: 'total_neto' (suma neta antes de impuestos), 'total_iva' (19% del neto si aplica), 'total_ila' (si aparece, generalmente 10% o 18% sobre ciertos productos)
- Estos son totales a nivel de factura, NO por item.
- Si el documento es BOLETA, estos campos deben ir como null.

PASO 3 — Clasificación de impuestos por item:
Para CADA item en la lista, identifica el tipo de producto para determinar los impuestos:

- Bebidas azucaradas (Coca-Cola, Fanta, Sprite, Pepsi, etc.) → IVA + ILA 18%
- Bebidas zero/light (Coca-Cola Zero, Sprite Zero, etc.) → IVA + ILA 10%
- Abarrotes (papas, galletas, arroz, fideos, etc.) → IVA solo (ILA 0%)
- Agua mineral sin gas → IVA solo
- Otros productos → IVA solo (por defecto)

Para CADA item:
- 'tiene_iva': true si el producto lleva IVA (casi todos los productos en Chile)
- 'tiene_ila': true solo si es bebida azucarada o zero/light
- 'tipo_ila': "18" para azucaradas, "10" para zero/light, null si no aplica
- Si es FACTURA: calcula 'neto_unitario' (precio antes de IVA e ILA)
- 'costo_unitario' es el PRECIO FINAL pagado por unidad (con TODOS los impuestos incluidos)
- 'subtotal' = cantidad * costo_unitario

CRÍTICO — Invariantes que DEBES cumplir SIEMPRE:
1. 'costo_unitario' SIEMPRE debe ser el precio final pagado (IVA + ILA incluidos)
2. 'subtotal' DEBE ser exactamente (cantidad * costo_unitario)
3. La SUMA de todos los subtotales DEBE coincidir con 'monto_total'
4. Si es FACTURA: costo_unitario = neto × 1.19 (si solo IVA) o neto × 1.19 × 1.18 (si IVA + ILA 18%) o neto × 1.19 × 1.10 (si IVA + ILA 10%)
5. Si es BOLETA (precios ya brutos): neto_unitario se calcula al revés: costo_unitario / 1.19 para productos solo IVA, más complejo si tiene ILA

Reglas IMPORTANTES para los NÚMEROS en el JSON:
1. Usa el punto (.) SOLO para los decimales. No uses separadores de miles.
2. VERIFICACIÓN MATEMÁTICA: La suma de todos los "subtotal" DEBE coincidir exactamente con el "monto_total".
3. REGLA ESPECIAL PARA COMBUSTIBLE (bencina, petróleo, diésel, gasolina, COPEC, Shell, Petrobras, Enex): NO intentes desglosar. Usa un ÚNICO item con:
   - "producto": "Combustible"
   - "cantidad": 1
   - "costo_unitario": monto_total
   - "subtotal": monto_total
   (no aplican impuestos especiales ni neto)

Formato requerido:
{
  "proveedor": "Nombre del proveedor o tienda (Ej: ALVI, EL MOLINO, VICTOR ROJAS)",
  "numero_documento": "Numero de factura o ticket",
  "fecha": "Fecha ESTRICTAMENTE en formato AAAA-MM-DD. NUNCA uses formatos como DD-MM-AA ni barras.",
  "tipo_documento": "FACTURA",
  "monto_total": 0.0,
  "total_neto": 0.0,
  "total_iva": 0.0,
  "total_ila": 0.0,
  "items": [
    {
      "producto": "Nombre descriptivo (Ej: COCA COLA LATA 350 CC)",
      "cantidad": 0,
      "neto_unitario": 0.0,
      "costo_unitario": 0.0,
      "subtotal": 0.0,
      "tiene_iva": true,
      "tiene_ila": true,
      "tipo_ila": "18",
      "ean": "Código de barras EAN (opcional, 13 dígitos)",
      "sku": "SKU o código interno (opcional)"
    }
  ]
}

NOTA sobre ean y sku: Son OPCIONALES. NO inventes ni alucines códigos. Si ves un código de barras (generalmente 13 dígitos), inclúyelo en 'ean'.

REGLAS DE DESAMBIGÜACIÓN DE MARCAS — Bebidas Chilenas:

1. **Bilz** es una marca INDEPENDIENTE de jugo/gaseosa (CCU). NO la confundas con "Kem". Bilz es burbujeante, sabor frutal, típicamente en latas de 350cc o botellas 500cc.
2. **Pap** es una marca INDEPENDIENTE de jugo/gaseosa (CCU). NO la confundas con "Kem" ni con "Bilz". Pap es de color naranjo, sabor a papaya.
3. **Kem** es una marca INDEPENDIENTE de jugo/gaseosa (CCU). NO uses "Kem Lata 350cc" como nombre genérico para otras marcas.
4. **Coca-Cola** (The Coca-Cola Company) incluye: Coca-Cola (original, zero, light), Sprite, Fanta. NO las confundas con marcas CCU.

Ejemplos correctos vs incorrectos:
- ✔ "Bilz 500cc" — correcto, marca específica
- ✔ "Pap Lata 350cc" — correcto, marca específica  
- ✔ "Kem Lata 350cc" — correcto, marca específica
- ✔ "Coca-Cola Zero 600cc" — correcto, marca específica
- ✘ "Kem Lata 350cc" para una Bilz — INCORRECTO, cada marca es distinta

Ejemplos few-shot para bebidas chilenas:

Ejemplo 1 — Bilz 500cc (EL MOLINO):
```json
{
  "proveedor": "EL MOLINO",
  "items": [{"producto": "Bilz 500cc", "cantidad": 12, "costo_unitario": 890, "subtotal": 10680, "tiene_iva": true, "tiene_ila": true, "tipo_ila": "18"}]
}
```

Ejemplo 2 — Pap Lata 350cc:
```json
{
  "proveedor": "DISTRIBUIDORA ABC",
  "items": [{"producto": "Pap Lata 350cc", "cantidad": 24, "costo_unitario": 650, "subtotal": 15600, "tiene_iva": true, "tiene_ila": true, "tipo_ila": "18"}]
}
```

Ejemplo 3 — Coca-Cola Zero 600cc (ILA 10% por ser zero):
```json
{
  "proveedor": "COCA-COLA EMBONOR",
  "items": [{"producto": "Coca-Cola Zero 600cc", "cantidad": 12, "costo_unitario": 1200, "subtotal": 14400, "tiene_iva": true, "tiene_ila": true, "tipo_ila": "10"}]
}
```
"""
    text = _call_gemini(prompt, image_path)
    return _parse_json(text, "invoice")


def extract_recarga_data(image_path: str) -> dict:
    """
    Extracts slot number + quantity pairs from a handwritten refill list photo using Gemini.
    Returns a dictionary with slots array: {"slots": [{"slot_number": "10", "quantity": 5}, ...]}
    """
    prompt = """
Analiza esta imagen de una lista de recarga de máquinas vending. La lista contiene
números de slot y cantidades escritas a mano.

Por cada item reconocido, extrae:
- slot_number: el número de slot escrito (ej: "10", "A1", "23")
- quantity: la cantidad como número entero (0 es válido, significa que el slot no se recargó)

Devuelve ÚNICAMENTE un JSON válido con esta estructura exacta, sin texto adicional:
{"slots": [{"slot_number": "10", "quantity": 5}, {"slot_number": "12", "quantity": 0}]}

Reglas:
- Ignora nombres de productos — solo interesa el slot y la cantidad
- Si un slot tiene varias cantidades, usa la ÚLTIMA escrita
- Devuelve slot_number como texto plano (ej: "10", "A1")
- Devuelve quantity como entero (ej: 5, no "cinco")
- INCLUÍ slots con cantidad 0 — son válidos, significa que ese slot no se recargó
- Si no hay texto legible, devuelve {"slots": []}
"""
    text = _call_gemini(prompt, image_path)
    return _parse_json(text, "recarga")
