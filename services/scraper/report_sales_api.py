"""
Scraper de ventas Ourvend — via API (sin Playwright).

Endpoints reverse-engineered desde sesión browser. Usa las mismas funciones
de login RSA que machine_status.py.

ANTI-BANEO:
    - Usa requests.Session() para reutilizar cookies (login una sola vez)
    - Headers de browser realistas (User-Agent, X-Requested-With, Referer)
    - Delays configurables entre requests (--delay, default 1.0s)
    - Sin requests redundantes: en modo --all-pages, itera solo las páginas necesarias
    - rows=2000 para minimizar requests (la API no tiene hard cap en 500)

Modos:
    python report_sales_api.py --list-json              # query JSON de ventas (rápido)
    python report_sales_api.py --export-excel            # dispara export Excel
    python report_sales_api.py --poll-exports            # lista archivos en ReserveList
    python report_sales_api.py --full-flow               # flujo completo: query JSON → export → poll

Uso avanzado:
    python report_sales_api.py --list-json --start "2026-06-01" --end "2026-06-30"
    python report_sales_api.py --list-json --machine-id 2410280012
    python report_sales_api.py --list-json --all-pages --rows 2000 --delay 2.0
    python report_sales_api.py --list-json --pay-type "Cash"

Environment:
    OURVEND_USER  — usuario Ourvend
    OURVEND_PASS  — contraseña Ourvend
"""

from __future__ import annotations

import os
import sys
import base64
import argparse
import json as _json
import time
import re
import asyncio
from datetime import datetime, timedelta
from urllib.parse import urljoin

import requests
from cryptography.hazmat.primitives import serialization, hashes
from cryptography.hazmat.primitives.asymmetric import padding

# ──────────────────────────────────────────────
# CONFIG
# ──────────────────────────────────────────────
BASE_URL = "https://os.ourvend.com"
USERNAME = os.environ.get("OURVEND_USER")
PASSWORD = os.environ.get("OURVEND_PASS")

# Valores por defecto (descubiertos por reverse engineering)
DEFAULT_GROUP_UUID = "53aed77a-1c1b-40b2-a9be-d40d98adb348"  # UNIDAD PREDETERMINADA
DEFAULT_ROWS = 500
DEFAULT_START = (datetime.now() - timedelta(days=7)).strftime("%Y-%m-%d")
DEFAULT_END = datetime.now().strftime("%Y-%m-%d")

# ──────────────────────────────────────────────
# RSA (mismo que machine_status.py)
# ──────────────────────────────────────────────
def encrypt_password(password: str, pub_key_base64: str) -> str:
    """Encripta la password con la clave pública RSA (PKCS#1 v1.5)."""
    pem = (
        "-----BEGIN PUBLIC KEY-----\n"
        + pub_key_base64
        + "\n-----END PUBLIC KEY-----"
    )
    public_key = serialization.load_pem_public_key(pem.encode())
    encrypted = public_key.encrypt(
        password.encode("utf-8"),
        padding.PKCS1v15(),
    )
    return base64.b64encode(encrypted).decode()


# ──────────────────────────────────────────────
# SESSION & LOGIN
# ──────────────────────────────────────────────
def create_session() -> requests.Session:
    """Crea una requests.Session con los headers base."""
    session = requests.Session()
    session.headers.update({
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36"
        ),
        "Accept": "application/json, text/javascript, */*; q=0.01",
        "X-Requested-With": "XMLHttpRequest",
        "Referer": f"{BASE_URL}/",
    })
    return session


def login(session: requests.Session) -> bool:
    """Obtiene clave pública, encripta password, inicia sesión."""
    # 1. Obtener clave pública
    resp = session.post(f"{BASE_URL}/Account/GetPubKey")
    resp.raise_for_status()
    pub_key = resp.text.strip()

    if not pub_key or len(pub_key) < 100:
        print("[!] Error: clave pública inválida", file=sys.stderr)
        return False

    # 2. Encriptar password
    try:
        encrypted_pwd = encrypt_password(PASSWORD, pub_key)
    except Exception as e:
        print(f"[!] Error encriptando password: {e}", file=sys.stderr)
        return False

    # 3. Login
    resp = session.post(
        f"{BASE_URL}/Account/Login",
        data={
            "userAccount": USERNAME,
            "userPwd": encrypted_pwd,
            "LoginUrl": "Account",
        },
    )
    resp.raise_for_status()

    # Verificar login exitoso
    if "YSTemplet" not in resp.text and resp.status_code != 200:
        print(f"[!] Login fallido ({resp.status_code})", file=sys.stderr)
        return False

    return True


# ──────────────────────────────────────────────
# SALES DATA QUERY (ListJson)
# ──────────────────────────────────────────────
def query_sales(
    session: requests.Session,
    start_date: str = DEFAULT_START,
    end_date: str = DEFAULT_END,
    machine_id: str = "",
    group_uuid: str = DEFAULT_GROUP_UUID,
    pay_type: str = "",
    page: int = 1,
    rows: int = DEFAULT_ROWS,
    debug: bool = False,
) -> dict:
    """
    POST /OutReport/ListJson/?firstload=0
    Devuelve datos de ventas en JSON (jqGrid format).
    """
    params = {
        "_search": "false",
        "rows": str(rows),
        "page": str(page),
        "sidx": "TrTime",
        "sord": "asc",
    }

    # --- Determinar si usamos POST form-urlencoded o query params ---
    # Los datos pueden ir como form data en POST o como query en GET.
    # Según reverse engineering: POST con Content-Type form-urlencoded.
    headers = {
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
    }

    form_data = {
        "Group": group_uuid,
        "MachineID": machine_id,
        "IndexTime": f"{start_date} 00:00:00",
        "LastTime": f"{end_date} 23:59:59",
        "CardNo": "",
        "PayType": pay_type,
        "Type": "",
        "CommodityName": "",
    }

    url = f"{BASE_URL}/OutReport/ListJson/"
    firstload = "0"

    if debug:
        print(f"[DEBUG] POST {url}?firstload={firstload}", file=sys.stderr)
        print(f"[DEBUG] Form data: {form_data}", file=sys.stderr)

    resp = session.post(
        url,
        params={**params, "firstload": firstload},
        data=form_data,
        headers=headers,
    )

    if debug:
        print(f"[DEBUG] Status: {resp.status_code}", file=sys.stderr)
        print(f"[DEBUG] Response ({len(resp.text)} chars): {resp.text[:500]}", file=sys.stderr)

    resp.raise_for_status()

    try:
        return resp.json()
    except _json.JSONDecodeError as e:
        print(f"[!] Error decodificando JSON: {e}", file=sys.stderr)
        print(f"[!] Response text: {resp.text[:1000]}", file=sys.stderr)
        raise


def query_all_pages(
    session: requests.Session,
    start_date: str = DEFAULT_START,
    end_date: str = DEFAULT_END,
    machine_id: str = "",
    group_uuid: str = DEFAULT_GROUP_UUID,
    pay_type: str = "",
    rows: int = 2000,  # default alto, la API lo acepta
    delay: float = 1.0,  # anti-baneo
    debug: bool = False,
) -> list[dict]:
    """
    Itera todas las páginas de ListJson hasta obtener todos los registros.
    Devuelve lista plana de diccionarios (rows).

    Usa rows altos (2000+) para minimizar requests. La API de Ourvend no
    tiene hard cap en 500 — ese límite es solo del dropdown del frontend.
    """
    first_page = query_sales(
        session,
        start_date=start_date,
        end_date=end_date,
        machine_id=machine_id,
        group_uuid=group_uuid,
        pay_type=pay_type,
        page=1,
        rows=rows,
        debug=debug,
    )

    total_pages = int(first_page.get("total", 1))
    total_records = int(first_page.get("records", 0))
    all_rows = list(first_page.get("rows", []))

    if debug:
        log(f"[DEBUG] Total pages: {total_pages}, Total records: {total_records}")

    for p in range(2, total_pages + 1):
        if debug:
            log(f"[DEBUG] Fetching page {p}/{total_pages} (delay={delay}s)...")
        time.sleep(delay)  # anti-baneo: esperar entre páginas
        page_data = query_sales(
            session,
            start_date=start_date,
            end_date=end_date,
            machine_id=machine_id,
            group_uuid=group_uuid,
            pay_type=pay_type,
            page=p,
            rows=rows,
            debug=False,
        )
        all_rows.extend(page_data.get("rows", []))

    return all_rows

    return all_rows


# ──────────────────────────────────────────────
# EXCEL EXPORT
# ──────────────────────────────────────────────
def trigger_excel_export(
    session: requests.Session,
    start_date: str = DEFAULT_START,
    end_date: str = DEFAULT_END,
    machine_id: str = "",
    group_uuid: str = DEFAULT_GROUP_UUID,
    debug: bool = False,
) -> dict:
    """
    POST /OutReport/OutReportExecl
    Dispara la generación del archivo Excel en el servidor (~5 min).
    """
    form_data = {
        "Group": group_uuid,
        "MachineID": machine_id,
        "IndexTime": f"{start_date} 00:00:00",
        "LastTime": f"{end_date} 23:59:59",
        "CardNo": "",
        "PayType": "",
        "Type": "",
        "CommodityName": "",
        "LanguageType": "Export all data",
    }

    headers = {
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
    }

    if debug:
        print(f"[DEBUG] POST {BASE_URL}/OutReport/OutReportExecl", file=sys.stderr)
        print(f"[DEBUG] Form data: {form_data}", file=sys.stderr)

    resp = session.post(
        f"{BASE_URL}/OutReport/OutReportExecl",
        data=form_data,
        headers=headers,
    )

    if debug:
        print(f"[DEBUG] Status: {resp.status_code}", file=sys.stderr)
        print(f"[DEBUG] Response: {resp.text}", file=sys.stderr)

    resp.raise_for_status()

    try:
        return resp.json()
    except _json.JSONDecodeError:
        return {"raw": resp.text}


# ──────────────────────────────────────────────
# POLL EXPORT STATUS (ReserveList)
# ──────────────────────────────────────────────
def init_outreport_session(session: requests.Session, debug: bool = False) -> bool:
    """
    Inicializa la sesión del módulo OutReport.

    1. GET OutReport/Index → obtiene cookies de seguridad (aliyungf_tc, acw_tc)
    2. POST OutReport/getSession → inicializa el contexto del módulo

    Requerido antes de ReserveList, ListJson, y OutReportExecl en el módulo OutReport.
    """
    headers = {
        "Referer": f"{BASE_URL}/YSTemplet/index",
        "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    }

    # 1. Visitar la página OutReport para obtener cookies de seguridad
    if debug:
        print(f"[DEBUG] GET {BASE_URL}/OutReport/Index", file=sys.stderr)
    resp = session.get(f"{BASE_URL}/OutReport/Index", headers=headers, timeout=30)
    if debug:
        print(f"[DEBUG]   Status: {resp.status_code}, Cookies: {len(session.cookies)}", file=sys.stderr)

    # 2. getSession para inicializar el módulo
    headers = {
        "X-Requested-With": "XMLHttpRequest",
        "Referer": f"{BASE_URL}/OutReport/Index",
        "Accept": "*/*",
    }
    resp = session.post(f"{BASE_URL}/OutReport/getSession", headers=headers)
    if debug:
        print(f"[DEBUG] POST getSession -> Status: {resp.status_code}, Body: {resp.text[:200]}", file=sys.stderr)
    return resp.status_code == 200


def get_reserve_list(session: requests.Session, debug: bool = False) -> list[dict]:
    """
    POST /OutReport/ReserveList
    Devuelve lista de archivos Excel generados y su estado.
    RDState=1 significa listo para descargar.

    Requiere: haber llamado init_outreport_session() antes para
    inicializar el contexto del módulo OutReport.
    """
    headers = {
        "X-Requested-With": "XMLHttpRequest",
        "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
        "Referer": f"{BASE_URL}/OutReport/Index",
        "Accept": "application/json, text/javascript, */*; q=0.01",
    }

    resp = session.post(
        f"{BASE_URL}/OutReport/ReserveList",
        data={
            "type": "Registros de envío",
            "_search": "false",
            "nd": str(int(datetime.now().timestamp() * 1000)),
            "rows": "100",
            "page": "1",
            "sidx": "RDTime",
            "sord": "desc",
        },
        headers=headers,
    )

    if debug:
        print(f"[DEBUG] POST ReserveList -> Status: {resp.status_code}", file=sys.stderr)
        print(f"[DEBUG] Response: {resp.text[:500]}", file=sys.stderr)

    resp.raise_for_status()

    try:
        data = resp.json()
        return data.get("rows", [])
    except _json.JSONDecodeError:
        print(f"[!] ReserveList no devolvió JSON: {resp.text[:500]}", file=sys.stderr)
        return []


def poll_until_ready(
    session: requests.Session,
    timeout_sec: int = 360,
    poll_interval: int = 15,
    known_rdids: set = None,
    delay: float = 1.0,
    debug: bool = False,
) -> dict | None:
    """
    Espera hasta que aparezca un nuevo archivo en ReserveList (RDState=1).
    Compara con known_rdids para detectar nuevos.
    Devuelve el dict del nuevo archivo, o None si timeout.
    """
    if known_rdids is None:
        # Inicializar sesión OutReport y snapshot inicial de RDIDs
        init_outreport_session(session, debug=debug)
        current = get_reserve_list(session, debug=debug)
        known_rdids = {item["RDID"] for item in current}

    waited = 0
    while waited < timeout_sec:
        time.sleep(poll_interval)
        waited += poll_interval

        # Anti-baneo: pausa extra entre llamadas a ReserveList
        time.sleep(delay)
        items = get_reserve_list(session, debug=False)
        for item in items:
            rdid = item.get("RDID")
            if rdid and rdid not in known_rdids:
                if item.get("RDState") == 1:
                    if debug:
                        print(f"[DEBUG] Nuevo archivo listo! RDID={rdid} tras {waited}s", file=sys.stderr)
                    return item
                elif debug:
                    print(f"[DEBUG] Nuevo RDID {rdid} pero RDState={item.get('RDState')}, esperando...", file=sys.stderr)

    return None


# ──────────────────────────────────────────────
# DOWNLOAD (PENDIENTE: URL exacta a descubrir)
# ──────────────────────────────────────────────
def download_reserve(
    session: requests.Session,
    rdid: int,
    output_dir: str = None,
    debug: bool = False,
) -> str | None:
    """
    Descarga un archivo de ReserveList por RDID.
    La URL exacta se descubrió navegando con Playwright — verificar.
    """
    if output_dir is None:
        output_dir = os.path.join(os.getcwd(), "downloads")

    os.makedirs(output_dir, exist_ok=True)

    # --- Candidates de URL de descarga ---
    candidates = [
        f"/OutReport/DownloadReserve?rdid={rdid}",
        f"/OutReport/DownloadReserve/?rdid={rdid}",
        f"/OutReport/DownloadReserve/{rdid}",
        f"/OutReport/ExcelScheduleDownload?rdid={rdid}",
        f"/OutReport/Download?rdid={rdid}",
    ]

    for path in candidates:
        url = urljoin(BASE_URL, path)
        if debug:
            print(f"[DEBUG] Probando: GET {url}", file=sys.stderr)

        try:
            resp = session.get(url, allow_redirects=True, timeout=30)
            content_type = resp.headers.get("Content-Type", "")

            if debug:
                print(f"[DEBUG]   Status: {resp.status_code}, Content-Type: {content_type}, Len: {len(resp.content)}", file=sys.stderr)

            # Si es un archivo (no HTML, no JSON vacío)
            if resp.status_code == 200 and len(resp.content) > 1000:
                # Intentar deducir filename del Content-Disposition
                cd = resp.headers.get("Content-Disposition", "")
                filename = None
                if cd:
                    match = re.search(r'filename[^;=\n]*=["\']?([^"\'\n;]+)', cd, re.IGNORECASE)
                    if match:
                        filename = match.group(1)

                if not filename:
                    ext = ".xls" if "excel" in content_type.lower() or "xls" in content_type.lower() else ".bin"
                    filename = f"ourvend_export_{rdid}_{datetime.now():%Y%m%d_%H%M%S}{ext}"

                filepath = os.path.join(output_dir, filename)
                with open(filepath, "wb") as f:
                    f.write(resp.content)

                log(f"    ✅ Descargado: {filepath}")
                return filepath

        except Exception as e:
            if debug:
                print(f"[DEBUG]   Error: {e}", file=sys.stderr)
            continue

    return None


# ──────────────────────────────────────────────
# RESPONSE CLASSIFICATION
# ──────────────────────────────────────────────
WAF_MARKERS = [
    "acw_sc", "acw_tc", "aliyungf_tc",
    "阿里云", "js_challenge", "no-browser",
]


def classify_response(response_data, http_status=None) -> str:
    """
    Classify an OurVend ListJson response.

    Accepts either:
    - A dict from fetch_sales_via_browser (with ``_classified`` / metadata keys)
    - A raw ListJson response dict (with ``rows`` key)
    - A string body (with optional ``http_status``) from the legacy requests path

    Returns one of: ``"ok"``, ``"empty"``, ``"blocked"``, ``"error"``, ``"timeout"``.
    Keeps the legacy ``requests`` path callable — does not modify existing functions.
    """
    # ── Case A: Pre-classified result from fetch_sales_via_browser ──
    if isinstance(response_data, dict) and "_classified" in response_data:
        return response_data["_classified"]

    # ── Case B: raw ListJson response dict (from fetch or legacy path) ──
    if isinstance(response_data, dict) and "rows" in response_data:
        rows = response_data.get("rows", [])
        return "ok" if rows else "empty"

    # ── Case C: string body (legacy requests path) ──
    if isinstance(response_data, str):
        body = response_data.lower()

        # HTTP status–based checks
        if http_status is not None:
            if http_status == 429 or http_status == 403:
                return "blocked"
            if http_status >= 400:
                return "error"

        # WAF/aliyun challenge markers
        for marker in WAF_MARKERS:
            if marker.lower() in body:
                return "blocked"

        # Login-page redirect (session expired / blocked)
        if "account/login" in body or "useraccount" in body or "loginurl" in body:
            return "blocked"

        return "error"

    # ── Fallback ──
    return "error"


# ──────────────────────────────────────────────
# BROWSER-BASED FETCH (stealth Playwright)
# ──────────────────────────────────────────────
async def fetch_sales_via_browser(
    start_date: str = DEFAULT_START,
    end_date: str = DEFAULT_END,
    machine_id: str = "",
    group_uuid: str = DEFAULT_GROUP_UUID,
    rows: int = 2000,
    debug: bool = False,
) -> dict:
    """
    Login to OurVend via headless Chromium (Playwright + stealth), then fetch
    ListJson sales data via ``page.evaluate(fetch())`` INSIDE the authenticated
    browser context — this makes the HTTP request look identical to a real
    browser (same JA3, same cookies, same headers).

    Returns the raw ListJson response dict (same shape as ``query_sales()``)
    on success. On failure returns a dict with ``_classified`` and ``_reason``
    keys that ``classify_response`` understands.

    Raises ``RuntimeError`` if login itself (wrong credentials, WAF login
    block, etc.) or the Playwright launch fails.

    Environment variables: ``OURVEND_USER``, ``OURVEND_PASS``, ``OURVEND_BASE_URL``.
    """
    # Lazy imports — only needed when this function is called
    from playwright.async_api import async_playwright
    from playwright_stealth import Stealth

    base = (os.environ.get("OURVEND_BASE_URL") or BASE_URL).rstrip("/")
    username = os.environ.get("OURVEND_USER")
    password = os.environ.get("OURVEND_PASS")

    if not username or not password:
        raise ValueError("OURVEND_USER and OURVEND_PASS must be set")

    if debug:
        log(f"[browser] Launching stealth Chromium  {start_date}  {end_date} "
            f"machine={machine_id or 'all'}")

    _stealth = Stealth()

    async with _stealth.use_async(async_playwright()) as pw:
        browser = await pw.chromium.launch(
            headless=True,
            args=[
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-blink-features=AutomationControlled",
            ],
        )
        context = await browser.new_context(
            viewport={"width": 1920, "height": 1080},
            user_agent=(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                "AppleWebKit/537.36 (KHTML, like Gecko) "
                "Chrome/120.0.0.0 Safari/537.36"
            ),
            locale="es-CL",
        )
        page = await context.new_page()

        # Fallback init-script — extra safety net over playwright-stealth
        await page.add_init_script("""
            Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
            window.chrome = window.chrome || {};
            window.chrome.runtime = window.chrome.runtime || {};
        """)

        try:
            # ── 0. Navigate to login page to establish same-origin ──
            if debug:
                log("[browser] Navigating to login page...")
            await page.goto(
                f"{base}/Account/Login",
                wait_until="domcontentloaded",
                timeout=30000,
            )

            # ── 1. Get RSA public key ──
            if debug:
                log("[browser] Getting RSA public key...")
            pub_key = await page.evaluate("""
                async (url) => {
                    const resp = await fetch(url + '/Account/GetPubKey', {
                        method: 'POST',
                        credentials: 'include',
                    });
                    return await resp.text();
                }
            """, base)
            pub_key = pub_key.strip()
            if not pub_key or len(pub_key) < 100:
                raise RuntimeError(
                    f"Invalid public key from {base}/Account/GetPubKey "
                    f"(len={len(pub_key)})"
                )
            if debug:
                log(f"[browser] PubKey OK ({len(pub_key)} chars)")

            # ── 2. Encrypt password in Python (reusing existing RSA helper) ──
            encrypted_pwd = encrypt_password(password, pub_key)

            # ── 3. Login via in-page fetch (JA3-consistent) ──
            if debug:
                log("[browser] Logging in via in-page fetch...")
            login_result = await page.evaluate("""
                async ([url, data]) => {
                    const resp = await fetch(url + '/Account/Login', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                            'X-Requested-With': 'XMLHttpRequest',
                        },
                        body: new URLSearchParams(data),
                        credentials: 'include',
                    });
                    const text = await resp.text();
                    return {
                        ok: resp.ok,
                        status: resp.status,
                        redirected: resp.redirected,
                        finalUrl: resp.url,
                        textPreview: text.substring(0, 300),
                    };
                }
            """, [base, {
                "userAccount": username,
                "userPwd": encrypted_pwd,
                "LoginUrl": "Account",
            }])

            # Verify login success — YSTemplet in response = dashboard redirect
            if "YSTemplet" not in login_result.get("textPreview", ""):
                raise RuntimeError(
                    f"Login failed (status={login_result['status']}, "
                    f"redirected={login_result['redirected']}, "
                    f"url={login_result.get('finalUrl', '')}, "
                    f"body={login_result.get('textPreview', '')})"
                )
            if debug:
                log("[browser] Login OK")

            # ── 4. Visit OutReport/Index to establish module session ──
            if debug:
                log("[browser] Visiting OutReport/Index...")
            await page.goto(
                f"{base}/OutReport/Index",
                wait_until="networkidle",
                timeout=45000,
            )

            # ── 5. Initialize OutReport module ──
            if debug:
                log("[browser] getSession...")
            await page.evaluate("""
                async (url) => {
                    await fetch(url + '/OutReport/getSession', {
                        method: 'POST',
                        credentials: 'include',
                        headers: { 'X-Requested-With': 'XMLHttpRequest' },
                    });
                }
            """, base)

            # ── 6. Fetch ListJson data via in-page fetch ──
            if debug:
                log("[browser] Fetching ListJson...")
            list_json_url = f"{base}/OutReport/ListJson/"
            params = {
                "_search": "false",
                "rows": str(rows),
                "page": "1",
                "sidx": "TrTime",
                "sord": "asc",
                "firstload": "0",
            }
            form_data = {
                "Group": group_uuid,
                "MachineID": machine_id,
                "IndexTime": f"{start_date} 00:00:00",
                "LastTime": f"{end_date} 23:59:59",
                "CardNo": "",
                "PayType": "",
                "Type": "",
                "CommodityName": "",
            }

            fetch_result = await page.evaluate("""
                async ([url, params, formData]) => {
                    const qs = Object.entries(params).map(([k, v]) =>
                        encodeURIComponent(k) + '=' + encodeURIComponent(v)
                    ).join('&');
                    const resp = await fetch(url + '?' + qs, {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                            'X-Requested-With': 'XMLHttpRequest',
                        },
                        body: new URLSearchParams(formData),
                        credentials: 'include',
                    });
                    const text = await resp.text();
                    let json = null;
                    try { json = JSON.parse(text); } catch (_) {}
                    return {
                        status: resp.status,
                        redirected: resp.redirected,
                        finalUrl: resp.url,
                        textPreview: text.substring(0, 500),
                        json: json,
                    };
                }
            """, [list_json_url, params, form_data])

            # ── 7. Return / classify result ──
            if fetch_result["json"] is not None:
                rows_count = len(fetch_result["json"].get("rows", []))
                if debug:
                    log(f"[browser] ListJson OK: {rows_count} rows")
                return fetch_result["json"]

            # Non-JSON — determine failure type
            if debug:
                log(f"[browser] Non-JSON response: "
                    f"status={fetch_result['status']}, "
                    f"redirected={fetch_result['redirected']}, "
                    f"url={fetch_result.get('finalUrl', '')}")

            text_lower = (fetch_result.get("textPreview") or "").lower()
            if fetch_result.get("redirected") and \
               "/account/login" in fetch_result.get("finalUrl", "").lower():
                return {
                    "_classified": "blocked",
                    "_reason": "Redirected to login page",
                }
            for marker in WAF_MARKERS:
                if marker in text_lower:
                    return {"_classified": "blocked", "_reason": f"WAF marker: {marker}"}

            return {
                "_classified": "error",
                "_reason": f"Non-JSON response (status {fetch_result['status']})",
            }

        finally:
            await browser.close()


def fetch_sales_via_browser_sync(
    start_date: str = DEFAULT_START,
    end_date: str = DEFAULT_END,
    machine_id: str = "",
    group_uuid: str = DEFAULT_GROUP_UUID,
    rows: int = 2000,
    debug: bool = False,
) -> dict:
    """Synchronous wrapper around ``fetch_sales_via_browser`` for CLI use."""
    return asyncio.run(
        fetch_sales_via_browser(
            start_date=start_date,
            end_date=end_date,
            machine_id=machine_id,
            group_uuid=group_uuid,
            rows=rows,
            debug=debug,
        )
    )


# ──────────────────────────────────────────────
# FORMATTERS
# ──────────────────────────────────────────────
# TrPayType es numérico en la API (string "0", "1", etc.)
# Mapeo descubierto del HTML del reporte y la respuesta JSON
PAY_TYPE_MAP = {
    "0": "💵 Efectivo (Cash)",
    "1": "💳 Online/Tarjeta",
    "2": "📱 WeChat",
    "3": "📱 Alipay",
    "4": "🎫 Otro",
}

# TrResult: "0" = success, "1" = cancelled, otros = error
RESULT_MAP = {
    "0": "✅ OK",
    "1": "❌ Cancelado",
    "2": "⚠️ Error",
}

# Nombres de máquinas (obtenidos de MiAlias en la respuesta)
# Si MiAlias está vacío, usamos TrMachineID


def format_row(row: dict) -> dict:
    """Convierte una row de jqGrid a un dict más amigable."""
    tr_result = str(row.get("TrResult", "0"))
    price_raw = float(row.get("TrSalePrice", 0) or 0)
    # Los precios vienen en centavos? Verificar. Por ahora asumimos que están en unidades.
    return {
        "machine_id": row.get("TrMachineID", ""),
        "machine_name": row.get("MiAlias", "") or row.get("TrMachineID", "?"),
        "slot": row.get("TrCoilID", ""),
        "pay_type": str(row.get("TrPayType", "")),
        "pay_label": PAY_TYPE_MAP.get(str(row.get("TrPayType", "")), f"Tipo {row.get('TrPayType', '?')}"),
        "price": price_raw,
        "result": RESULT_MAP.get(tr_result, f"Estado {tr_result}"),
        "machine_time": row.get("TrTime", ""),
        "server_time": row.get("Addtime", ""),
        "is_remote": row.get("IsRemoteshipment") == "1",
        # Raw fields for debugging
        "_tr_id": row.get("TrID", ""),
        "_serial": row.get("TrSerialNumber", ""),
    }


def print_summary(rows: list[dict]) -> None:
    """Imprime resumen de ventas."""
    if not rows:
        print("   ⚠️ Sin datos de ventas")
        return

    total = len(rows)
    total_amount = sum(
        float(r.get("TrSalePrice", 0) or 0) for r in rows
    )

    # Agrupar por tipo de pago (TrPayType es string numérico "0", "1", etc.)
    pay_counts = {}
    pay_amounts = {}
    for r in rows:
        pt = str(r.get("TrPayType", "?"))
        pay_counts[pt] = pay_counts.get(pt, 0) + 1
        pay_amounts[pt] = pay_amounts.get(pt, 0) + float(r.get("TrSalePrice", 0) or 0)

    # Agrupar por máquina
    machine_counts = {}
    for r in rows:
        mid = r.get("TrMachineID", "?")
        machine_counts[mid] = machine_counts.get(mid, 0) + 1

    # Resultados
    result_counts = {}
    for r in rows:
        res = str(r.get("TrResult", "0"))
        result_counts[res] = result_counts.get(res, 0) + 1

    print(f"\n{'═' * 70}")
    print(f"  📊 VENTAS OURVEND — {datetime.now():%Y-%m-%d %H:%M:%S}")
    print(f"{'═' * 70}")
    print(f"  Total transacciones: {total}")
    print(f"  Total vendido:       ¥{total_amount:,.2f}")
    print()

    # Resultados
    results_str = " | ".join(
        f"{RESULT_MAP.get(k, k)}: {v}" for k, v in sorted(result_counts.items())
    )
    print(f"  Estados: {results_str}")
    print()

    print(f"  {'Tipo de pago':<25} {'Transacciones':>14} {'Monto':>12}")
    print(f"  {'─' * 53}")
    for pt, count in sorted(pay_counts.items(), key=lambda x: -x[1]):
        amount = pay_amounts.get(pt, 0)
        label = PAY_TYPE_MAP.get(pt, f"Tipo {pt}")
        print(f"  {label:<25} {count:>14} ¥{amount:>11,.2f}")

    print(f"\n  Máquinas activas: {len(machine_counts)}")
    if len(machine_counts) <= 10:
        for mid, count in sorted(machine_counts.items(), key=lambda x: -x[1])[:10]:
            print(f"     {mid}: {count} ventas")


# ──────────────────────────────────────────────
# LOGGING
# ──────────────────────────────────────────────
def log(msg: str) -> None:
    """Log con timestamp."""
    ts = datetime.now().strftime("%H:%M:%S")
    print(f"[{ts}] {msg}", file=sys.stderr, flush=True)


# ──────────────────────────────────────────────
# MAIN
# ──────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(
        description="Ourvend Sales Report — API scraper (sin Playwright)",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Ejemplos:
  python report_sales_api.py --list-json
  python report_sales_api.py --list-json --start "2026-07-01" --end "2026-07-07"
  python report_sales_api.py --list-json --machine-id 2410280012 --pay-type Cash
  python report_sales_api.py --export-excel
  python report_sales_api.py --poll-exports
  python report_sales_api.py --full-flow --start "2026-06-01" --end "2026-06-30"
        """,
    )

    # --- Modos principales ---
    parser.add_argument("--list-json", action="store_true",
                        help="Query JSON de ventas (rápido, 2-3 segundos)")
    parser.add_argument("--export-excel", action="store_true",
                        help="Dispara generación de Excel en el servidor")
    parser.add_argument("--poll-exports", action="store_true",
                        help="Lista archivos en ReserveList (cola de exportación)")
    parser.add_argument("--download", type=int, metavar="RDID",
                        help="Descarga un archivo por RDID")
    parser.add_argument("--full-flow", action="store_true",
                        help="Flujo completo: query JSON → export Excel → poll → download")
    parser.add_argument("--self-test", action="store_true",
                        help="Prueba manual: browser login + fetch contra portal real (no CI)")

    # --- Filtros ---
    parser.add_argument("--start", type=str, default=DEFAULT_START,
                        help=f"Fecha inicio (default: {DEFAULT_START})")
    parser.add_argument("--end", type=str, default=DEFAULT_END,
                        help=f"Fecha fin (default: {DEFAULT_END})")
    parser.add_argument("--machine-id", type=str, default="",
                        help="Filtrar por ID de máquina")
    parser.add_argument("--group-uuid", type=str, default=DEFAULT_GROUP_UUID,
                        help=f"UUID del grupo (default: UNIDAD PREDETERMINADA)")
    parser.add_argument("--pay-type", type=str, default="",
                        help="Filtrar por tipo de pago (Cash, Card, Online, WX, AliPay)")

    # --- Paginación ---
    parser.add_argument("--page", type=int, default=1, help="Página (default: 1)")
    parser.add_argument("--rows", type=int, default=DEFAULT_ROWS,
                        help=f"Filas por página (default: {DEFAULT_ROWS}). La API acepta >500.")
    parser.add_argument("--delay", type=float, default=1.0,
                        help="Segundos de espera entre requests (anti-baneo, default: 1.0)")
    parser.add_argument("--all-pages", action="store_true",
                        help="Iterar todas las páginas automáticamente")

    # --- Output ---
    parser.add_argument("--json", action="store_true",
                        help="Salida JSON cruda (sin summary)")
    parser.add_argument("--output-dir", type=str, default=None,
                        help="Directorio de descarga (default: ./downloads)")
    parser.add_argument("--debug", action="store_true",
                        help="Mostrar requests/responses detallados")

    args = parser.parse_args()

    # Validar credenciales
    if not USERNAME or not PASSWORD:
        print("[!] Seteá OURVEND_USER y OURVEND_PASS como variables de entorno", file=sys.stderr)
        sys.exit(1)

    # ── MODO: Self-test (browser path, never in CI) ──
    if args.self_test:
        print(f"\n{'=' * 60}")
        print("  SELF-TEST: Browser-based fetch_sales_via_browser")
        print(f"{'=' * 60}")
        print(f"  Fechas:  {args.start} → {args.end}")
        print(f"  Máquina: {args.machine_id or 'todas'}")
        print(f"  Target:  {BASE_URL}")
        print(f"\n  ⏱️  3-minute max-cycle budget applies")
        print(f"\n  Launching stealth Chromium...")
        sys.stdout.flush()

        try:
            result = fetch_sales_via_browser_sync(
                start_date=args.start,
                end_date=args.end,
                machine_id=args.machine_id,
                group_uuid=args.group_uuid,
                rows=args.rows,
                debug=args.debug,
            )
        except Exception as e:
            print(f"\n  ❌ FETCH EXCEPTION: {e}")
            import traceback
            traceback.print_exc()
            sys.exit(1)

        # Classify
        classification = classify_response(result)
        rows = result.get("rows", []) if isinstance(result, dict) and "rows" in result else []

        print(f"\n  Classification: {classification}")
        print(f"  Rows returned:  {len(rows)}")

        if classification in ("ok", "empty"):
            print(f"\n  ✅ SELF-TEST PASSED")
            sys.exit(0)
        else:
            reason = result.get("_reason", "") if isinstance(result, dict) else ""
            print(f"\n  ❌ SELF-TEST FAILED: {classification} — {reason}")
            sys.exit(1)

    # Crear sesión
    session = create_session()

    # Login
    log("🔐 Autenticando...")
    if not login(session):
        sys.exit(1)
    log("   ✅ Login exitoso")

    # ── MODO: List JSON ──
    if args.list_json or args.json or not any([
        args.list_json, args.export_excel, args.poll_exports,
        args.download, args.full_flow,
    ]):
        # Si no se especifica ningún modo, default a list-json
        log(f"📡 Query ventas: {args.start} → {args.end}")

        if args.all_pages:
            rows = query_all_pages(
                session,
                start_date=args.start,
                end_date=args.end,
                machine_id=args.machine_id,
                group_uuid=args.group_uuid,
                pay_type=args.pay_type,
                rows=args.rows,
                delay=args.delay,
                debug=args.debug,
            )
        else:
            data = query_sales(
                session,
                start_date=args.start,
                end_date=args.end,
                machine_id=args.machine_id,
                group_uuid=args.group_uuid,
                pay_type=args.pay_type,
                page=args.page,
                rows=args.rows,
                debug=args.debug,
            )
            rows = data.get("rows", [])
            total_pages = data.get("total", 1)
            total_records = data.get("records", 0)
            log(f"   Página {args.page}/{total_pages}, {len(rows)} filas (total: {total_records})")

        if args.json:
            output = [format_row(r) for r in rows]
            print(_json.dumps(output, indent=2, ensure_ascii=False))
        else:
            print_summary(rows)
            # Mostrar primeras filas
            if rows and not args.json:
                print(f"\n  Primeras 5 transacciones:")
                for r in rows[:5]:
                    f = format_row(r)
                    print(f"     [{f['machine_time']}] {f['pay_label']} ¥{f['price']} — {f['machine_name']} ({f['machine_id']}) [{f['result']}]")

    # ── MODO: Export Excel ──
    if args.export_excel or args.full_flow:
        log("📂 Inicializando sesión OutReport...")
        init_outreport_session(session, debug=args.debug)
        log("📤 Disparando export Excel...")
        result = trigger_excel_export(
            session,
            start_date=args.start,
            end_date=args.end,
            machine_id=args.machine_id,
            group_uuid=args.group_uuid,
            debug=args.debug,
        )
        log(f"   Resultado: {result}")
        log("   ⏳ Ourvend tarda ~5 min en generar el archivo")

    # ── MODO: Poll Exports ──
    if args.poll_exports or args.full_flow:
        log("📂 Inicializando sesión OutReport...")
        init_outreport_session(session, debug=args.debug)
        log("📂 Consultando ReserveList...")
        items = get_reserve_list(session, debug=args.debug)

        if not items:
            print("   ⚠️ No hay archivos en ReserveList")
        else:
            print(f"\n{'═' * 80}")
            print(f"  📂 RESERVE LIST — {len(items)} archivos")
            print(f"{'═' * 80}")
            print(f"  {'RDID':<10} {'Estado':<10} {'Tipo':<25} {'Nombre'}")
            print(f"  {'─' * 78}")
            for item in items:
                rdid = item.get("RDID", "?")
                state = "✅ LISTO" if item.get("RDState") == 1 else f"⏳ State={item.get('RDState')}"
                rdtype = item.get("RDType", "?")
                rdname = item.get("RDName", "?")
                # Truncar nombre largo
                if len(rdname) > 40:
                    rdname = rdname[:37] + "..."
                print(f"  {rdid:<10} {state:<10} {rdtype:<25} {rdname}")

    # ── MODO: Download ──
    if args.download or args.full_flow:
        if args.download:
            rdid = args.download
        elif args.full_flow:
            # En full-flow, esperar que aparezca y descargar
            log("⏳ Esperando que se genere el archivo...")
            items_before = get_reserve_list(session, debug=False)
            known = {item["RDID"] for item in items_before}
            new_item = poll_until_ready(
                session,
                known_rdids=known,
                debug=args.debug,
            )
            if new_item:
                rdid = new_item["RDID"]
                log(f"   ✅ Nuevo archivo: RDID={rdid}, {new_item.get('RDName', '?')}")
            else:
                log("   ❌ Timeout esperando archivo nuevo")
                sys.exit(1)
        else:
            sys.exit(0)

        log(f"⬇️ Descargando RDID={rdid}...")
        path = download_reserve(session, rdid, output_dir=args.output_dir, debug=args.debug)
        if path:
            log(f"   ✅ Archivo guardado: {path}")
        else:
            log("   ❌ No se pudo descargar — URL exacta pendiente de verificar")
            log("   💡 Probá abrir Ourvend en Playwright MCP para capturar la URL de download")


if __name__ == "__main__":
    main()
