#!/usr/bin/env python3
"""
Generator hash gerbang akses Nomhorrant-165.

Prinsip keamanan: kode verifikasi TIDAK PERNAH disimpan — yang dihasilkan hanyalah
SHA-256(salt + code). Jalankan skrip ini di mesin Anda sendiri, lalu tempel hasilnya
ke config.local.json (field "accessHash") atau env var NOMHORRANT_ACCESS_HASH.

Pemakaian interaktif:
    python3 tools/gen_access_hash.py

Pemakaian non-interaktif (contoh otomasi):
    echo -n 'kode-anda' | python3 tools/gen_access_hash.py --stdin
"""
from __future__ import annotations

import argparse
import getpass
import hashlib
import os
import secrets
import sys


def make_hash(code: str, salt_hex: str | None = None) -> str:
    if salt_hex is None:
        salt_hex = secrets.token_hex(16)
    digest = hashlib.sha256(bytes.fromhex(salt_hex) + code.encode("utf-8")).hexdigest()
    return f"sha256:{salt_hex}:{digest}"


def verify(code: str, stored: str) -> bool:
    try:
        scheme, salt_hex, expected = stored.strip().split(":")
    except ValueError:
        return False
    if scheme != "sha256":
        return False
    return make_hash(code, salt_hex) == stored.strip()


def main() -> int:
    parser = argparse.ArgumentParser(description="Buat hash gerbang akses Nomhorrant-165")
    parser.add_argument("--stdin", action="store_true", help="baca kode dari stdin (tanpa echo ke terminal)")
    parser.add_argument("--verify", metavar="STORED", help="verifikasi kode terhadap hash yang sudah ada")
    args = parser.parse_args()

    if args.stdin:
        code = sys.stdin.readline().rstrip("\n")
    else:
        code = getpass.getpass("Kode verifikasi (tidak akan ditampilkan): ")

    if not code:
        print("Kode kosong — dibatalkan.", file=sys.stderr)
        return 1

    if args.verify:
        ok = verify(code, args.verify)
        print("COCOK" if ok else "TIDAK COCOK")
        return 0 if ok else 2

    stored = make_hash(code)
    print()
    print("Simpan baris berikut di config.local.json (field accessHash):")
    print()
    print('  "accessHash": "' + stored + '"')
    print()
    print("atau set sebagai environment variable:")
    print()
    print(f"  NOMHORRANT_ACCESS_HASH={stored}")
    print()
    print("⚠ Jangan pernah menyimpan/mengirim kodenya sendiri — hanya hash ini.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
