#!/usr/bin/env bash
# ======================================================================================================
#  Provision an EPHEMERAL mutual-TLS client identity for the cross-service end-to-end suite.
#
#  WHY THIS SCRIPT EXISTS
#  ----------------------
#  `POST /v1/tokens` on the Security service is authenticated by a CLIENT CERTIFICATE and by nothing
#  else, because a caller cannot present a bearer token in order to obtain its first bearer token.
#  Security declares exactly one listener - `https://+:5104` with `ClientCertificateMode`
#  `AllowCertificate` - in its BASE settings file, so the requirement holds in Development too. Without
#  an identity, every authenticated workflow in this suite is unrunnable, which is why
#  `fixtures/token-issuance.ts` fails a full acceptance run outright rather than skipping quietly.
#
#  This script is the documented way to satisfy that precondition for a local or CI full-stack run.
#
#  WHAT IT PRODUCES, AND THE TWO PROPERTIES THAT MAKE IT WORK
#  ---------------------------------------------------------
#    1. A throwaway certificate authority - `client-ca.crt` / `client-ca.key`. Security must trust it,
#       which is what `SECURITY_MTLS_CLIENT_CA_PATH` in `orchestration/.env.example` is for. A private
#       authority is used rather than a self-signed leaf because Security validates the presented chain
#       at the transport, and a leaf that is its own issuer has no chain to validate.
#
#       THAT ONE VARIABLE IS SUFFICIENT, AND IT WAS NOT ALWAYS. Security decides twice about a caller
#       certificate: its LISTENER anchor decides whether the handshake completes at all, and its
#       ISSUANCE anchor (`Security:ClientCertificateAuthorityPath`) decides whether the completed
#       handshake's certificate may establish an identity. Only the listener key has a published
#       variable, so a run that followed step 2 below to the letter used to get a completed handshake
#       followed by `401` on every certificate. Security's composition root now adopts the listener
#       anchor as the issuance anchor when the issuance key is unset, so there is no third variable to
#       export and none is published - if a `401` survives step 2 now, the cause is the CA, the leaf's
#       common name, or a Security instance that has not reloaded, and not a missing setting.
#    2. A client leaf - `e2e-client.crt` / `e2e-client.key` - whose SUBJECT COMMON NAME IS EXACTLY THE
#       IDENTITY THE SUITE CLAIMS. That is not cosmetic: the token endpoint resolves the caller identity
#       from the certificate's simple name and then compares it ORDINALLY against the `subject` in the
#       request body, refusing a mismatch with `403`. `fixtures/auth.ts` requests `pfw-e2e-suite`, so the
#       common name must be that string, character for character. Override with E2E_CLIENT_IDENTITY only
#       if `E2E_TOKEN_SUBJECT` in that fixture has been changed to match.
#       The leaf also carries the TLS Web Client Authentication extended key usage, which is what makes it
#       usable as a client credential rather than merely as a certificate.
#
#  EPHEMERAL, AND THAT IS THE POINT (constraint C-F)
#  ------------------------------------------------
#  Fresh material on EVERY run. Nothing is reused, nothing is shared between environments and nothing is
#  ever committed: the output directory is ignored by `tests/e2e/.gitignore`, the private keys are written
#  0600, and the repository's own secrets sweep exists precisely because committed key material - a
#  plaintext PEM RSA private key in a legacy browser asset among them - is the defect this refactor is
#  remediating. Do not adapt this script to write into a tracked path, and do not check its output in.
#
#  IT PROVISIONS ONLY. It starts nothing, brings nothing up and contacts nothing (C-J): bringing the
#  stack up is the orchestration path's job, and this script has no opinion about whether anything is
#  running.
#
#  USAGE
#  -----
#      cd tests/e2e && npm run provision:identity          # writes ./.mtls, prints the exports
#      eval "$(cd tests/e2e && npm run --silent provision:identity -- --export-only)"
#
#  Environment overrides, all optional:
#      E2E_IDENTITY_DIR       output directory                  (default: <this suite>/.mtls)
#      E2E_CLIENT_IDENTITY    subject common name of the leaf    (default: pfw-e2e-suite)
#      E2E_IDENTITY_DAYS      validity in days                   (default: 30)
# ======================================================================================================

set -o errexit
set -o nounset
set -o pipefail

# ------------------------------------------------------------------------------------------------------
#  Resolve paths relative to THIS FILE rather than to the working directory, so the script behaves the
#  same whether npm invoked it from `tests/e2e/` or an operator ran it from the repository root.
# ------------------------------------------------------------------------------------------------------
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
suite_dir="$(cd -- "${script_dir}/.." && pwd)"

identity_dir="${E2E_IDENTITY_DIR:-${suite_dir}/.mtls}"
client_identity="${E2E_CLIENT_IDENTITY:-pfw-e2e-suite}"
validity_days="${E2E_IDENTITY_DAYS:-30}"

export_only="false"
if [ "${1:-}" = "--export-only" ]; then
  export_only="true"
fi

ca_key="${identity_dir}/client-ca.key"
ca_cert="${identity_dir}/client-ca.crt"
client_key="${identity_dir}/e2e-client.key"
client_csr="${identity_dir}/e2e-client.csr"
client_cert="${identity_dir}/e2e-client.crt"
client_ext="${identity_dir}/e2e-client.ext"

# ------------------------------------------------------------------------------------------------------
#  Fail fast on a missing tool rather than part way through, so the operator is told what to install
#  instead of being handed a half-written directory.
# ------------------------------------------------------------------------------------------------------
if ! command -v openssl > /dev/null 2>&1; then
  echo "provision-e2e-client-identity: openssl is required and was not found on PATH." >&2
  exit 1
fi

# A run always starts from nothing. Re-using a leaf whose validity had lapsed produces a handshake
# failure that reads as a trust problem, and re-using one generated for a different identity produces a
# 403 that reads as an authorization defect at Security. Both are expensive to diagnose and neither is
# worth the microsecond saved.
rm -rf -- "${identity_dir}"
mkdir -p -- "${identity_dir}"
chmod 700 -- "${identity_dir}"

umask 077

# ------------------------------------------------------------------------------------------------------
#  1. The throwaway authority.
# ------------------------------------------------------------------------------------------------------
openssl req -x509 -newkey rsa:2048 -sha256 -nodes \
  -keyout "${ca_key}" \
  -out "${ca_cert}" \
  -days "${validity_days}" \
  -subj "/CN=PowerFramework E2E Ephemeral Client CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  > /dev/null 2>&1

# ------------------------------------------------------------------------------------------------------
#  2. The client leaf, whose common name IS the claimed identity.
# ------------------------------------------------------------------------------------------------------
openssl req -newkey rsa:2048 -sha256 -nodes \
  -keyout "${client_key}" \
  -out "${client_csr}" \
  -subj "/CN=${client_identity}" \
  > /dev/null 2>&1

{
  echo "basicConstraints=critical,CA:FALSE"
  echo "keyUsage=critical,digitalSignature,keyEncipherment"
  echo "extendedKeyUsage=critical,clientAuth"
  echo "subjectAltName=DNS:${client_identity}"
} > "${client_ext}"

openssl x509 -req -sha256 \
  -in "${client_csr}" \
  -CA "${ca_cert}" \
  -CAkey "${ca_key}" \
  -set_serial 1 \
  -days "${validity_days}" \
  -extfile "${client_ext}" \
  -out "${client_cert}" \
  > /dev/null 2>&1

rm -f -- "${client_csr}" "${client_ext}"

chmod 600 -- "${ca_key}" "${client_key}"
chmod 644 -- "${ca_cert}" "${client_cert}"

# ------------------------------------------------------------------------------------------------------
#  Report. Paths only - no key, no certificate body and no passphrase is ever printed.
#
#  The two suite variables come first because they are what makes `npm test` able to obtain a token; the
#  third is what Security needs in order to trust the leaf, and it is a property of the STACK rather than
#  of the suite, so it is reported separately and labelled as such.
# ------------------------------------------------------------------------------------------------------
echo "export SECURITY_MTLS_CERT_PATH=${client_cert}"
echo "export SECURITY_MTLS_KEY_PATH=${client_key}"
echo "export SECURITY_MTLS_CLIENT_CA_PATH=${ca_cert}"

if [ "${export_only}" = "true" ]; then
  exit 0
fi

cat >&2 <<REPORT

Provisioned an ephemeral client identity for the end-to-end suite.

  Directory        ${identity_dir}   (gitignored; regenerated on every run)
  Client identity  CN=${client_identity}   (must equal E2E_TOKEN_SUBJECT in fixtures/auth.ts)
  Validity         ${validity_days} days

NEXT, AND BOTH HALVES ARE REQUIRED:

  1. Export the two suite variables above, so the runner presents the certificate:

       eval "\$(npm run --silent provision:identity -- --export-only)"

  2. Make the stack trust the authority, by pointing the Security service's
     SECURITY_MTLS_CLIENT_CA_PATH at the CA certificate above in orchestration/.env
     and bringing the stack up (or restarting Security) so it reloads it.

Without step 2 the handshake presents a certificate Security does not trust and the token
call is refused with 401 - which is the transport behaving correctly, not a defect.

No key material is printed by this script, and none of it may be committed.
REPORT
