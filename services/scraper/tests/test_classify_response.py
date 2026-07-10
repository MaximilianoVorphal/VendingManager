"""Tests for classify_response in report_sales_api.py."""
import json
import os
import sys
from pathlib import Path

# Add parent to path so we can import report_sales_api
sys.path.insert(0, str(Path(__file__).parent.parent))

from report_sales_api import classify_response

FIXTURES = Path(__file__).parent / "fixtures"


def load_fixture(name: str) -> str:
    return (FIXTURES / name).read_text(encoding="utf-8")


def load_json_fixture(name: str) -> dict:
    return json.loads(load_fixture(name))


class TestClassifyResponse:
    """Test classify_response for all outcome types."""

    def test_ok_with_rows(self):
        """Valid JSON with rows → ok."""
        data = load_json_fixture("valid_ok.json")
        assert classify_response(data) == "ok"

    def test_empty_no_rows(self):
        """Valid JSON with empty rows → empty."""
        data = load_json_fixture("valid_empty.json")
        assert classify_response(data) == "empty"

    def test_blocked_waf_challenge_html(self):
        """WAF challenge HTML body → blocked."""
        body = load_fixture("waf_challenge.html")
        assert classify_response(body, http_status=200) == "blocked"

    def test_blocked_waf_challenge_no_status(self):
        """WAF challenge HTML body without explicit status → blocked."""
        body = load_fixture("waf_challenge.html")
        assert classify_response(body) == "blocked"

    def test_blocked_login_redirect(self):
        """Login page redirect → blocked (session expired = WAF-like)."""
        body = load_fixture("login_redirect.html")
        assert classify_response(body, http_status=200) == "blocked"

    def test_error_server_500(self):
        """500 status → error."""
        body = load_fixture("server_error.html")
        assert classify_response(body, http_status=500) == "error"

    def test_error_malformed_json(self):
        """Non-JSON text → error."""
        body = load_fixture("malformed_json.txt")
        assert classify_response(body, http_status=200) == "error"

    def test_error_empty_response(self):
        """Empty response body → error."""
        assert classify_response("") == "error"

    def test_error_none_response(self):
        """None response → error."""
        assert classify_response(None) == "error"
