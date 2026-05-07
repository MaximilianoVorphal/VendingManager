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
    Returns a dictionary structured with the invoice items.
    """
    prompt = """
Analiza esta imagen que corresponde a una factura, boleta o ticket de supermercado de Chile (ej: Alvi, Acuenta, Mayorista, Distribuidoras).
Extrae la información y retorna ÚNICAMENTE un objeto JSON válido con la siguiente estructura estricta, sin texto adicional ni formateos como ```json:

Reglas IMPORTANTES para los NÚMEROS en el JSON:
1. Usa el punto (.) SOLO para los decimales. No uses separadores de miles. (ej: 10.080 -> 10080, 840,336 -> 840.336).
2. Usa SIEMPRE VALORES CON IMPUESTOS (IVA) INCLUIDOS (Bruto). Si la factura detalla el valor de los productos de forma NETA (sin impuestos) y suma el IVA solo al final de la cuenta, DEBES agregar matemáticamente el porcentaje o margen de impuesto (ej: +19% IVA) al "costo_unitario" de cada producto para que refleje fielmente el costo real pagado con impuestos por unidad.
3. VERIFICACIÓN MATEMÁTICA: La suma de todos los "subtotal" de todos los items en la lista DEBE COINCIDIR o cuadrar casi exactamente con el "monto_total" final de la factura (Monto Final a Pagar con impuestos). Ajusta los decimales o agrega el impuesto si notas que la suma es inferior al monto total cobrado.
4. El "subtotal" por item DEBE ser estrictamente igual a (cantidad * costo_unitario). NUNCA devuelvas 0 si el producto fue efectivamente cobrado.

Formato requerido:
{
  "proveedor": "Nombre del proveedor o tienda (Ej: ALVI, EL MOLINO, VICTOR ROJAS)",
  "numero_documento": "Numero de factura o ticket",
  "fecha": "Fecha formato YYYY-MM-DD",
  "monto_total": 0.0,
  "items": [
    {
      "producto": "Nombre descriptivo (Ej: COCA COLA LATA 350 CC)",
      "cantidad": 0,
      "costo_unitario": 0.0,
      "subtotal": 0.0
    }
  ]
}
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
