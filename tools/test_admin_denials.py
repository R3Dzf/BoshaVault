#!/usr/bin/env python3
"""Exercise *unauthenticated* admin API denial paths, without account credentials.

No privileged user actions, user data reads, recovery emails, or database writes.
"""
import json
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

ENDPOINT = "https://ghmbcfqiomggoxgwghjs.supabase.co/functions/v1/lockvyn-admin"
TRUSTED_ORIGIN = "https://lockvyn-admin-ui.r3dzf-studio.workers.dev"


def request(headers, body):
    req = Request(ENDPOINT, data=json.dumps(body).encode("utf-8"),
                  headers=headers, method="POST")
    try:
        with urlopen(req, timeout=18) as response:
            payload = response.read(4000)
            return response.status, payload
    except HTTPError as exc:
        return exc.code, exc.read(4000)


def deny(headers, body, expected, label):
    status, payload = request(headers, body)
    assert status == expected, f"{label}: HTTP {status}, expected {expected}"
    assert b'"users"' not in payload and b'"email"' not in payload, (
        f"{label}: unauthenticated API exposed account metadata"
    )
    print("PASS:", label, "denied with", status)


def main():
    deny({"Origin": "https://attacker.invalid",
          "Content-Type": "application/json"},
         {"action": "list", "page": 1}, 403, "Untrusted origin")
    deny({"Origin": TRUSTED_ORIGIN,
          "Content-Type": "application/json"},
         {"action": "list", "page": 1}, 401, "Unauthenticated account list")
    deny({"Origin": TRUSTED_ORIGIN,
          "Content-Type": "application/json"},
         {"action": "recover", "id": "00000000-0000-0000-0000-000000000000"},
         401, "Unauthenticated recovery")
    deny({"Origin": TRUSTED_ORIGIN,
          "Content-Type": "text/plain"},
         {"action": "list", "page": 1}, 415, "Invalid content type")


if __name__ == "__main__":
    try:
        main()
    except (URLError, TimeoutError) as exc:
        raise SystemExit(f"Could not verify live authorization: {exc}")
