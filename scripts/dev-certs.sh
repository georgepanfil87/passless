#!/usr/bin/env bash
#
# Issues the local TLS certificate the API and the Angular dev server use.
#
# WebAuthn is only available in a secure context. `localhost` qualifies without
# TLS, so this is not strictly required to make a ceremony run -- we serve HTTPS
# locally anyway so that Secure cookie attributes, RP ID derivation and
# mixed-content behaviour are exercised the same way they will be in production
# rather than for the first time after deploying.

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
certs_dir="$repo_root/certs"

cert_file="$certs_dir/passless.pem"
key_file="$certs_dir/passless-key.pem"

mkdir -p "$certs_dir"

if command -v mkcert >/dev/null 2>&1; then
  echo "==> Using mkcert"

  # Idempotent: installs mkcert's CA into the system and browser trust stores
  # the first time only.
  mkcert -install

  mkcert -cert-file "$cert_file" -key-file "$key_file" \
    localhost passless.localhost 127.0.0.1 ::1
else
  echo "==> mkcert not found, falling back to the .NET SDK development certificate"
  echo "    (covers 'localhost' only -- install mkcert for custom hostnames)"

  if ! dotnet dev-certs https --check --trust >/dev/null 2>&1; then
    # Prompts for the login keychain password on macOS.
    dotnet dev-certs https --trust
  fi

  dotnet dev-certs https --export-path "$cert_file" --format PEM --no-password
  mv -f "${cert_file%.pem}.key" "$key_file"
fi

# Kestrel and the Angular dev server both read this PEM pair directly. No
# PKCS#12 bundle and no passphrase: an unencrypted PFX is rejected by macOS, and
# an encrypted one would only move the problem to where the password is kept.
chmod 600 "$key_file"

echo
echo "Wrote:"
echo "  $cert_file"
echo "  $key_file"
