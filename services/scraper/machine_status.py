"""
Scraper de estado de máquinas Ourvend — via API (sin Playwright).

Uso:
    python machine_status.py                           # todas las máquinas
    python machine_status.py --online                  # solo online
    python machine_status.py --offline                 # solo offline
    python machine_status.py --machine 2410280012      # máquina específica
    python machine_status.py --json                    # salida JSON cruda
"""

import os
import sys
import base64
import argparse
import json as _json
from datetime import datetime

import requests
from cryptography.hazmat.primitives import serialization, hashes
from cryptography.hazmat.primitives.asymmetric import padding

# ──────────────────────────────────────────────
# CONFIG
# ──────────────────────────────────────────────
BASE_URL = "https://os.ourvend.com"
USERNAME = os.environ.get("OURVEND_USER")
PASSWORD = os.environ.get("OURVEND_PASS")

# ──────────────────────────────────────────────
# RSA
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
# API
# ──────────────────────────────────────────────
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
        headers={
            "X-Requested-With": "XMLHttpRequest",
            "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
        },
    )
    resp.raise_for_status()

    # Verificar login exitoso: el dashboard redirige a YSTemplet/index
    if "YSTemplet" not in resp.text and resp.status_code != 200:
        print(f"[!] Login fallido ({resp.status_code})", file=sys.stderr)
        return False

    return True


def login_and_get_session() -> requests.Session | None:
    """Crea una sesión autenticada y la devuelve. Para uso desde la API."""
    if not USERNAME or not PASSWORD:
        print("[!] OURVEND_USER/PASS no configurados", file=sys.stderr)
        return None

    session = requests.Session()
    session.headers.update({
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36"
        ),
        "Accept": "application/json, text/javascript, */*; q=0.01",
    })

    if not login(session):
        return None

    return session


def get_machine_list(session: requests.Session) -> list[dict]:
    """Obtiene lista completa de máquinas desde OperateMonitor/ListJson."""
    params = {
        "firstload": "0",
        "_search": "false",
        "nd": str(int(datetime.now().timestamp() * 1000)),
        "rows": "500",
        "page": "1",
        "sidx": "MiNoline",
        "sord": "desc",
    }
    resp = session.get(f"{BASE_URL}/OperateMonitor/ListJson/", params=params)
    resp.raise_for_status()
    data = resp.json()
    return data.get("rows", [])


def get_quick_stats(session: requests.Session) -> dict:
    """Obtiene stats rápidos del dashboard."""
    stats = {}
    endpoints = {
        "online_offline": "/YSHome/MachineOnline",
        "day_sales": "/YSHome/DayData",
        "fault": "/YSHome/Fault",
        "out_of_stock": "/YSHome/Stock",
    }
    for key, path in endpoints.items():
        try:
            resp = session.post(f"{BASE_URL}{path}")
            stats[key] = resp.text.strip()
        except Exception:
            stats[key] = "N/A"
    return stats


# ──────────────────────────────────────────────
# STATUS LABELS
# ──────────────────────────────────────────────
STATUS_MAP = {
    4: ("🟢 ONLINE", "online"),
    3: ("🟢 ONLINE", "online"),
    2: ("🟡 WARNING", "warning"),
    1: ("🟡 WARNING", "warning"),
    0: ("🔴 OFFLINE", "offline"),
}


def status_label(noline: int) -> tuple[str, str]:
    return STATUS_MAP.get(noline, ("⚪ UNKNOWN", "unknown"))


# ──────────────────────────────────────────────
# MAIN
# ──────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(description="Ourvend Machine Status — API scraper")
    parser.add_argument("--online", action="store_true", help="Solo máquinas online")
    parser.add_argument("--offline", action="store_true", help="Solo máquinas offline")
    parser.add_argument("--warning", action="store_true", help="Solo máquinas en warning")
    parser.add_argument("--machine", type=str, help="Filtrar por Machine ID")
    parser.add_argument("--json", action="store_true", help="Salida en JSON crudo")
    parser.add_argument(
        "--quick", action="store_true", help="Solo stats rápidos del dashboard"
    )
    args = parser.parse_args()

    if not USERNAME or not PASSWORD:
        print("[!] Seteá OURVEND_USER y OURVEND_PASS como variables de entorno", file=sys.stderr)
        sys.exit(1)

    session = requests.Session()
    session.headers.update({
        "User-Agent": (
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
            "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/149.0.0.0 Safari/537.36"
        ),
        "Accept": "application/json, text/javascript, */*; q=0.01",
    })

    # Login
    print("🔐 Autenticando...", file=sys.stderr)
    if not login(session):
        sys.exit(1)
    print("   ✅ Login exitoso", file=sys.stderr)

    # Quick stats
    if args.quick:
        stats = get_quick_stats(session)
        if args.json:
            print(_json.dumps(stats, indent=2))
        else:
            online, offline = stats["online_offline"].split("|")
            total, cash, noncash = stats["day_sales"].split("|")
            print(f"\n📊 Dashboard:")
            print(f"   Online: {online}  |  Offline: {offline}")
            print(f"   Ventas hoy: ¥{total}  (Cash: ¥{cash}  Non-cash: ¥{noncash})")
            print(f"   Fallas: {stats['fault']}  |  Sin stock: {stats['out_of_stock']}")
        return

    # Machine list
    print("📡 Obteniendo lista de máquinas...", file=sys.stderr)
    machines = get_machine_list(session)

    if not machines:
        print("   ⚠️ Sin datos", file=sys.stderr)
        return

    # Filtros
    if args.machine:
        machines = [m for m in machines if m["MId"] == args.machine]
    if args.online:
        machines = [m for m in machines if m["MiNoline"] >= 3]
    if args.warning:
        machines = [m for m in machines if m["MiNoline"] in (1, 2)]
    if args.offline:
        machines = [m for m in machines if m["MiNoline"] == 0]

    # Salida JSON
    if args.json:
        output = [
            {
                "machine_id": m["MId"],
                "name": m["MiAlias"],
                "status": status_label(m["MiNoline"])[1],
                "noline": m["MiNoline"],
                "temperature": m.get("MiInsideTemp", "N/A"),
                "door": "open" if m.get("Door") == 1 else "closed",
                "coin_acceptor": m.get("Coin", "N/A"),
                "selection": m.get("Selection", "N/A"),
                "version": m.get("Versions", "N/A"),
                "group": m.get("MGroupName", "N/A"),
            }
            for m in machines
        ]
        print(_json.dumps(output, indent=2, ensure_ascii=False))
        return

    # Salida formateada
    print(f"\n{'═' * 75}")
    print(f"  📡 ESTADO DE MÁQUINAS — {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print(f"{'═' * 75}")
    print(f"  {'ID':<14} {'Status':<18} {'Temp':<8} {'Puerta':<8} {'Coin':<8} {'Versión'}")
    print(f"  {'─' * 73}")

    online_count = 0
    offline_count = 0
    warning_count = 0

    for m in machines:
        label, _ = status_label(m["MiNoline"])
        door = "open" if m.get("Door") == 1 else "closed"
        temp = m.get("MiInsideTemp", "—")
        coin = m.get("Coin", "—")
        version = (m.get("Versions", "—") or "—")[:28]

        if m["MiNoline"] >= 3:
            online_count += 1
        elif m["MiNoline"] == 0:
            offline_count += 1
        else:
            warning_count += 1

        print(
            f"  {m['MId']:<14} {label:<18} {temp:<8} {door:<8} {coin:<8} {version}"
        )

    print(f"  {'─' * 73}")
    print(f"  Total: {len(machines)}  |  🟢 Online: {online_count}  |  🟡 Warning: {warning_count}  |  🔴 Offline: {offline_count}")
    print(f"{'═' * 75}\n")


if __name__ == "__main__":
    main()
