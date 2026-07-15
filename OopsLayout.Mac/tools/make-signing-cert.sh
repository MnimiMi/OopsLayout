#!/bin/bash
# Creates a local self-signed code-signing identity ("OopsLayout Local") in your
# login keychain. Signing the app with a *stable* identity is what makes the
# Accessibility permission survive rebuilds — ad-hoc signing changes identity
# every build, so macOS forgets the grant and nags you again each time.
#
# Run this ONCE:
#     ./tools/make-signing-cert.sh
#
# Then rebuild (./build-app.sh picks the identity up automatically) and grant
# Accessibility a final time. Future rebuilds keep the grant.
#
# macOS may ask you to unlock the keychain, and the first codesign may pop a
# "codesign wants to sign using key …" dialog — click "Always Allow".
set -euo pipefail

CN="OopsLayout Local"
KEYCHAIN="$HOME/Library/Keychains/login.keychain-db"

if security find-identity -v -p codesigning 2>/dev/null | grep -q "${CN}"; then
    echo "Identity '${CN}' already exists — nothing to do."
    exit 0
fi

WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

cat > "${WORK}/cfg" <<EOF
[req]
distinguished_name = dn
x509_extensions    = ext
prompt             = no
[dn]
CN = ${CN}
[ext]
keyUsage             = critical, digitalSignature
extendedKeyUsage     = critical, codeSigning
basicConstraints     = critical, CA:false
EOF

echo "==> generating self-signed code-signing certificate"
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout "${WORK}/key.pem" -out "${WORK}/cert.pem" -config "${WORK}/cfg" >/dev/null 2>&1

# OpenSSL 3 defaults to a PKCS12 MAC (PBKDF2/SHA-256) that macOS `security`
# can't verify ("MAC verification failed"). -legacy forces the SHA1 MAC that
# Apple supports. LibreSSL doesn't know -legacy and already uses old algos, so
# fall back to a plain export there.
if ! openssl pkcs12 -export -legacy -inkey "${WORK}/key.pem" -in "${WORK}/cert.pem" \
        -out "${WORK}/id.p12" -name "${CN}" -passout pass:oops >/dev/null 2>&1; then
    openssl pkcs12 -export -inkey "${WORK}/key.pem" -in "${WORK}/cert.pem" \
        -out "${WORK}/id.p12" -name "${CN}" -passout pass:oops >/dev/null 2>&1
fi

echo "==> importing into login keychain"
security import "${WORK}/id.p12" -k "${KEYCHAIN}" -P oops \
    -T /usr/bin/codesign -T /usr/bin/security

# Let codesign use the key without prompting on every build. Needs the keychain
# password; if it fails you'll just get a one-time "Always Allow" dialog instead.
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "" "${KEYCHAIN}" >/dev/null 2>&1 || true

echo
echo "Done. Identity '${CN}' created."
echo "Now run ./build-app.sh and grant Accessibility one last time."
