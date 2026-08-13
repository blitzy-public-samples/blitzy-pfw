#!/usr/bin/env bash
# ======================================================================================================
#  Provision an EPHEMERAL mutual-TLS client identity for the cross-service end-to-end suite.
#
#  WHY THIS SCRIPT EXISTS
#  ----------------------
#  `POST /v1/tokens` on the Security service cannot be authenticated with a bearer token, because a
#  caller cannot present a token in order to obtain its first one. It accepts EXACTLY TWO credentials
#  instead, and a caller needs ONE of them, not both:
#
#    A. AN HTTP BASIC SECRET - the `clientCredential` scheme the published contract declares. The
#       user-id is the caller's service identity and the password is a shared secret the deployment
#       supplies through a flat configuration key. Security resolves the identity by looking the
#       user-id up in its `Security:Clients` roster and comparing the secret, so THIS PATH REQUIRES A
#       ROSTER ENTRY. `appsettings.Development.json` ships one for `pfw-e2e-suite` naming the key
#       `SECURITY_CLIENT_SECRET`, which is what `fixtures/auth.ts` presents by default.
#    B. A TRUSTED CLIENT CERTIFICATE - which is what this script provisions. Security validates the
#       presented chain at the transport against its configured authority and takes the certificate's
#       subject common name AS the caller identity, so this path needs NO roster entry: the chain is a
#       stronger check than a secret comparison, and the roster's remaining job - deciding what the
#       identity may ASK FOR - is the permission matrix, which is consulted identically for both
#       schemes.
#
#  EITHER WAY THE PERMISSION MATRIX STILL DECIDES. An identity the matrix does not name is refused
#  however well it authenticates, so a certificate alone is not a grant: `pfw-e2e-suite` also needs the
#  `(pfw-e2e-suite -> powerframework-gateway)` row carrying `ping`, `capabilities` and `datawindow`,
#  published in `appsettings.Development.json` and in `orchestration/.env.example` /
#  `docker-compose.yml` as `Security__CallerAuthorizations__3__*`.
#
#  Security declares exactly one listener - `https://+:5104` with `ClientCertificateMode`
#  `AllowCertificate` - in its BASE settings file, so scheme B is available in Development too.
#  `AllowCertificate` rather than `RequireCertificate` is what makes the two schemes genuine
#  alternatives: the handshake completes whether or not a certificate is offered.
#
#  SO WHY PREFER B, AND WHY THIS SCRIPT. Scheme A is simpler and is the suite's default. Scheme B is
#  what a TLS-terminating deployment uses, it needs no shared secret to exist anywhere, and it is the
#  only one of the two that exercises the mutual-TLS caller authentication the contract publishes -
#  which is otherwise negotiated but never used. Without SOME identity, every authenticated workflow in
#  this suite is unrunnable, which is why `fixtures/token-issuance.ts` fails a full acceptance run
#  outright rather than skipping quietly.
#
#  This script is the documented way to satisfy that precondition via scheme B for a local or CI
#  full-stack run.
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
#  DESTRUCTION AND CONSUMPTION ARE BOTH CONSTRAINED, AND THAT IS A SECURITY PROPERTY
#  ---------------------------------------------------------------------------------
#  An earlier form of this script did two things that a provisioning script must not do, and both are
#  worth naming because both look harmless in isolation.
#
#    1. IT PASSED AN UNRESTRICTED `E2E_IDENTITY_DIR` STRAIGHT TO `rm -rf`. The override exists so a CI
#       job can place the material on a writable volume, so the value is attacker-influenced in exactly
#       the environments that matter. `E2E_IDENTITY_DIR=/` or a stray path in a job template deleted
#       unrelated data, and the "always start from nothing" reasoning below - which is sound - was doing
#       the deleting. THE REASONING IS KEPT AND THE MECHANISM IS REPLACED: the target is now required to
#       resolve strictly beneath this suite directory, `rm -rf` is gone entirely, and only the seven
#       filenames this script itself writes are removed, BY NAME. A directory holding anything else is
#       refused outright rather than cleaned, so pointing the override at `specs/` or at a home
#       directory stops the run instead of emptying it.
#
#    2. IT EMITTED `export NAME=<value>` UNQUOTED FOR A DOCUMENTED `eval`. The values are paths derived
#       from that same override, so a directory name containing `;`, a backtick or `$(...)` became
#       COMMAND EXECUTION in the operator's shell - a shell-injection sink reached by a variable whose
#       whole purpose is to be set from outside. Two independent remedies are now in place, and the
#       eval-free one is the documented path:
#         * A DOTENV FILE, `e2e-identity.env`, written beside the material with single-quoted values.
#           It is data, never evaluated, and `set -a; . <file>; set +a` loads it with no shell parsing
#           the values at all.
#         * The `--export-only` stream is still emitted for compatibility, but every value now passes
#           through `printf %q`, so the documented `eval` is safe for any path this script can produce.
#
#  USAGE
#  -----
#      cd tests/e2e && npm run provision:identity          # writes ./.mtls, prints the exports
#
#      # Preferred, and evaluates nothing:
#      set -a; . tests/e2e/.mtls/e2e-identity.env; set +a
#
#      # Still supported; the emitted values are %q-quoted, so this is injection-safe:
#      eval "$(cd tests/e2e && npm run --silent provision:identity -- --export-only)"
#
#  Environment overrides, all optional:
#      E2E_IDENTITY_DIR       output directory, WHICH MUST RESOLVE STRICTLY INSIDE THIS SUITE
#                                                                (default: <this suite>/.mtls)
#      E2E_CLIENT_IDENTITY    subject common name of the leaf     (default: pfw-e2e-suite)
#      E2E_IDENTITY_DAYS      validity in days                    (default: 30)
# ======================================================================================================

set -o errexit
set -o nounset
set -o pipefail

# ------------------------------------------------------------------------------------------------------
#  Resolve paths relative to THIS FILE rather than to the working directory, so the script behaves the
#  same whether npm invoked it from `tests/e2e/` or an operator ran it from the repository root.
# ------------------------------------------------------------------------------------------------------
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
suite_dir="$(cd -- "${script_dir}/.." && pwd -P)"

# `-` rather than `:-`, deliberately. `:-` substitutes the default for an UNSET variable and for one set
# to the empty string alike, which would let `E2E_IDENTITY_DIR=` in a job template mean "the default"
# silently. An unset variable is a run that never asked to relocate anything and gets the default; an
# empty one is a variable somebody meant to populate, and it is refused below by name.
requested_identity_dir="${E2E_IDENTITY_DIR-${suite_dir}/.mtls}"
client_identity="${E2E_CLIENT_IDENTITY:-pfw-e2e-suite}"
validity_days="${E2E_IDENTITY_DAYS:-30}"

export_only="false"
if [ "${1:-}" = "--export-only" ]; then
  export_only="true"
fi

# ------------------------------------------------------------------------------------------------------
#  Emit a diagnostic and stop.
#
#  Every refusal below goes through here so that all of them reach stderr - never stdout, which in
#  `--export-only` mode is a stream an operator pipes into `eval`, and where a diagnostic would be
#  evaluated as shell input.
# ------------------------------------------------------------------------------------------------------
refuse() {
  printf 'provision-e2e-client-identity: %s\n' "$1" >&2
  exit 1
}

# ------------------------------------------------------------------------------------------------------
#  Canonicalize a path that MAY NOT EXIST YET.
#
#  `pwd -P` resolves symlinks and `..`, which is what makes the containment test below meaningful -
#  a literal string prefix check would be defeated by `${suite_dir}/../../etc` or by a symlink. But it
#  needs an existing directory to `cd` into, and the target legitimately does not exist on a first run.
#  So the NEAREST EXISTING ANCESTOR is canonicalized and the remaining segments are appended.
#
#  The loop walks up at most as far as `/`, and `dirname /` is `/`, so the guard against a
#  non-advancing parent is what terminates it rather than an assumption that an ancestor exists.
# ------------------------------------------------------------------------------------------------------
canonicalize() {
  local target="$1"
  local suffix=""
  local parent

  if [ -z "${target}" ]; then
    return 1
  fi

  case "${target}" in
    /*) ;;
    *) target="${PWD}/${target}" ;;
  esac

  while [ ! -d "${target}" ]; do
    parent="$(dirname -- "${target}")"

    if [ "${parent}" = "${target}" ]; then
      return 1
    fi

    suffix="/$(basename -- "${target}")${suffix}"
    target="${parent}"
  done

  printf '%s%s\n' "$(cd -- "${target}" && pwd -P)" "${suffix}"
}

# ------------------------------------------------------------------------------------------------------
#  Containment. The override may relocate the material; it may not escape the suite.
#
#  Three refusals, in the order a bad value is most likely to arrive:
#    * EMPTY OR WHITESPACE-ONLY. `E2E_IDENTITY_DIR=` in a job template is the common accident. A
#      whitespace-only value was the dangerous one: it is non-empty, so the old default-substitution
#      passed it through, and `rm -rf -- "   "` then targeted a path relative to whatever the working
#      directory happened to be. A bare empty value took the default silently, which is not dangerous
#      but is also not what an operator who set the variable asked for, so it is now named too.
#    * THE FILESYSTEM ROOT, or the suite directory itself. Both are targets whose "clean before use"
#      step would remove things nobody asked to remove.
#    * ANYTHING OUTSIDE THIS SUITE. The trailing slash on the prefix is what makes this STRICT
#      containment: `${suite_dir}` itself and a sibling such as `${suite_dir}-other` both fail it.
# ------------------------------------------------------------------------------------------------------
if [ -z "${requested_identity_dir//[[:space:]]/}" ]; then
  refuse "E2E_IDENTITY_DIR is set but empty. Unset it to use the default (${suite_dir}/.mtls), or set it to a directory inside this suite."
fi

if ! identity_dir="$(canonicalize "${requested_identity_dir}")"; then
  refuse "E2E_IDENTITY_DIR could not be resolved to an absolute path. Set it to a directory inside this suite, or unset it to use the default."
fi

if [ "${identity_dir}" = "/" ] || [ "${identity_dir}" = "${suite_dir}" ]; then
  refuse "E2E_IDENTITY_DIR resolves to ${identity_dir}, which this script will not write into or clear. Choose a directory strictly inside ${suite_dir}."
fi

case "${identity_dir}/" in
  "${suite_dir}/"*) ;;
  *)
    refuse "E2E_IDENTITY_DIR resolves outside this suite (${identity_dir}). This script writes private keys and clears its own output, so it only accepts a target strictly inside ${suite_dir} - which is also the only region tests/e2e/.gitignore keeps out of version control."
    ;;
esac

# AND THE FINAL COMPONENT MUST NAME A DISPOSABLE IDENTITY DIRECTORY. Containment already keeps the target
# inside this suite and the removal below only ever deletes files this script generated by name, so this is
# the third independent guard rather than the only one - and it is the one that stops a mistyped override
# from pointing at a real suite directory such as `specs` or `fixtures`, where nothing should ever be
# written and a future refactor of the removal step would have no other line standing in its way.
identity_leaf="$(basename -- "${identity_dir}")"

case "${identity_leaf}" in
  .mtls | mtls | e2e-identity | .e2e-identity) ;;
  *)
    refuse "E2E_IDENTITY_DIR's final component '${identity_leaf}' is not a disposable identity directory name. Use one of .mtls, mtls, e2e-identity or .e2e-identity inside ${suite_dir}."
    ;;
esac

ca_key="${identity_dir}/client-ca.key"
ca_cert="${identity_dir}/client-ca.crt"
client_key="${identity_dir}/e2e-client.key"
client_csr="${identity_dir}/e2e-client.csr"
client_cert="${identity_dir}/e2e-client.crt"
client_ext="${identity_dir}/e2e-client.ext"
identity_env="${identity_dir}/e2e-identity.env"

# Every filename this script writes, and therefore the ONLY filenames it will remove. Kept beside the
# path variables above so the two cannot drift: a file added there and not here would survive a clean
# and then trip the unexpected-content refusal on the next run, which is the safe direction to fail.
generated_names=(
  "client-ca.key"
  "client-ca.crt"
  "e2e-client.key"
  "e2e-client.csr"
  "e2e-client.crt"
  "e2e-client.ext"
  "e2e-identity.env"
)

# ------------------------------------------------------------------------------------------------------
#  Fail fast on a missing tool rather than part way through, so the operator is told what to install
#  instead of being handed a half-written directory.
# ------------------------------------------------------------------------------------------------------
if ! command -v openssl > /dev/null 2>&1; then
  echo "provision-e2e-client-identity: openssl is required and was not found on PATH." >&2
  exit 1
fi

# ------------------------------------------------------------------------------------------------------
#  A run always starts from nothing, BY NAME.
#
#  The reason for starting clean is unchanged and still sound: re-using a leaf whose validity had lapsed
#  produces a handshake failure that reads as a trust problem, and re-using one generated for a different
#  identity produces a `403` that reads as an authorization defect at Security. Both are expensive to
#  diagnose and neither is worth the microsecond saved.
#
#  What changed is HOW. There is no `rm -rf` anywhere in this script. Each of the seven filenames this
#  script writes is removed individually, and a directory containing ANYTHING ELSE - including a
#  subdirectory, and including a dotfile - is refused rather than cleaned. So the worst outcome of a
#  mistyped override that still lands inside the suite is a refusal naming the unexpected entry, not the
#  loss of whatever was there.
#
#  `dotglob` and `nullglob` are set inside a subshell for the scan so a dotfile cannot hide from it and
#  an empty directory does not yield the literal pattern; neither option leaks into the rest of the
#  script.
# ------------------------------------------------------------------------------------------------------
if [ -e "${identity_dir}" ] || [ -L "${identity_dir}" ]; then
  if [ -L "${identity_dir}" ]; then
    refuse "${identity_dir} is a symbolic link. This script will not clear or write through a link, because the link target is outside its containment check. Remove the link, or point E2E_IDENTITY_DIR at a real directory inside ${suite_dir}."
  fi

  if [ ! -d "${identity_dir}" ]; then
    refuse "${identity_dir} exists and is not a directory. Remove it, or choose another target inside ${suite_dir}."
  fi

  unexpected="$(
    shopt -s dotglob nullglob
    for entry in "${identity_dir}"/*; do
      name="$(basename -- "${entry}")"
      known="false"
      for candidate in "${generated_names[@]}"; do
        if [ "${name}" = "${candidate}" ]; then
          known="true"
          break
        fi
      done
      if [ "${known}" = "false" ]; then
        printf '%s\n' "${name}"
      fi
    done
  )"

  if [ -n "${unexpected}" ]; then
    refuse "$(printf '%s holds content this script did not write, so it will not be cleared:\n%s\nThis script only ever removes the files it generates, by name. Point E2E_IDENTITY_DIR at a directory it owns, or empty this one yourself.' "${identity_dir}" "${unexpected}")"
  fi

  for name in "${generated_names[@]}"; do
    rm -f -- "${identity_dir}/${name}"
  done
else
  mkdir -p -- "${identity_dir}"
fi

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
#  3. The dotenv file - the consumption path that evaluates NOTHING.
#
#  Values are single-quoted, with any embedded single quote closed, escaped and reopened in the standard
#  `'\''` form. Inside single quotes a POSIX shell expands nothing at all, so `;`, a backtick and
#  `$(...)` are literal characters here and a loader reads data rather than code. `set -a; . file; set +a`
#  is the documented way to consume it.
#
#  It is written 0600 like the keys beside it, even though it carries only paths: it names where two
#  private keys live, which is not something to leave world-readable in a shared CI workspace.
# ------------------------------------------------------------------------------------------------------
dotenv_quote() {
  printf "'%s'" "${1//\'/\'\\\'\'}"
}

{
  printf '# Ephemeral client identity for the PowerFramework end-to-end suite.\n'
  printf '# Generated by scripts/provision-e2e-client-identity.sh. Regenerated on every run.\n'
  printf '# Load with:  set -a; . %s; set +a\n' "e2e-identity.env"
  printf '# Paths only. No key material, certificate body or passphrase is ever written here.\n'
  printf 'SECURITY_MTLS_CERT_PATH=%s\n' "$(dotenv_quote "${client_cert}")"
  printf 'SECURITY_MTLS_KEY_PATH=%s\n' "$(dotenv_quote "${client_key}")"
  printf 'SECURITY_MTLS_CLIENT_CA_PATH=%s\n' "$(dotenv_quote "${ca_cert}")"
} > "${identity_env}"

chmod 600 -- "${identity_env}"

# ------------------------------------------------------------------------------------------------------
#  Report. Paths only - no key, no certificate body and no passphrase is ever printed.
#
#  The two suite variables come first because they are what makes `npm test` able to obtain a token; the
#  third is what Security needs in order to trust the leaf, and it is a property of the STACK rather than
#  of the suite, so it is reported separately and labelled as such.
#
#  EVERY VALUE PASSES THROUGH `printf %q`. This stream is documented as `eval` input, so an unquoted path
#  containing a shell metacharacter was command execution in the operator's shell - reached by
#  `E2E_IDENTITY_DIR`, a variable whose entire purpose is to be set from outside. `%q` emits a form bash
#  re-reads as exactly one word, so the documented `eval` is now safe for every path this script can
#  produce. The dotenv file above remains the preferred path, because it needs no `eval` at all.
# ------------------------------------------------------------------------------------------------------
printf 'export SECURITY_MTLS_CERT_PATH=%q\n' "${client_cert}"
printf 'export SECURITY_MTLS_KEY_PATH=%q\n' "${client_key}"
printf 'export SECURITY_MTLS_CLIENT_CA_PATH=%q\n' "${ca_cert}"

if [ "${export_only}" = "true" ]; then
  exit 0
fi

cat >&2 <<REPORT

Provisioned an ephemeral client identity for the end-to-end suite.

  Directory        ${identity_dir}   (gitignored; regenerated on every run)
  Client identity  CN=${client_identity}   (must equal E2E_TOKEN_SUBJECT in fixtures/auth.ts)
  Validity         ${validity_days} days
  Environment file ${identity_env}   (0600; paths only, and it is data - nothing evaluates it)

NEXT, AND BOTH HALVES ARE REQUIRED:

  1. Load the two suite variables, so the runner presents the certificate. The first form
     evaluates nothing and is the one to prefer; the second is still supported, and every
     value it prints is %q-quoted so it is safe to evaluate:

       set -a; . ${identity_env}; set +a

       eval "\$(npm run --silent provision:identity -- --export-only)"

  2. Make the stack trust the authority, by pointing the Security service's
     SECURITY_MTLS_CLIENT_CA_PATH at the CA certificate above in orchestration/.env
     and bringing the stack up (or restarting Security) so it reloads it.

Without step 2 the handshake presents a certificate Security does not trust and the token
call is refused with 401 - which is the transport behaving correctly, not a defect.

No key material is printed by this script, and none of it may be committed.
REPORT
