import os
import json
import google.generativeai as genai
from PIL import Image

def init_gemini():
    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        raise ValueError("GEMINI_API_KEY environment variable is not set")
    genai.configure(api_key=api_key)

def extract_invoice_data(image_path: str) -> dict:
    """
    Extracts invoice data from an image using Gemini 1.5 Flash.
    Returns a dictionary structured with the invoice items.
    """
    init_gemini()
    
    model = genai.GenerativeModel("gemini-3-flash-preview")
    
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
    
    img = Image.open(image_path)
    response = model.generate_content([prompt, img])
    
    text_response = response.text.replace("```json", "").replace("```", "").strip()
    
    try:
        return json.loads(text_response)
    except json.JSONDecodeError as e:
        print(f"Error parsing JSON from Gemini: {text_response}")
        raise ValueError(f"No se pudo decodificar la respuesta JSON de Gemini: {e}")
