<!-- Markdown lint policy for this file. Rationale is in docs/BUILD.md section 14, which directs that every
     document this refactor authors carry the same directive. MD013 is 120 rather than the 80-character
     default, and is disabled for tables and code blocks: an evidence row carrying a legacy locator and the
     behaviour it proves cannot be wrapped without splitting the locator from what it proves, and a wrapped
     command is a command that does not run. Prose IS wrapped, and is held to the 120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It applies to the documents this refactor authored and CANNOT reach the five read-only legacy Chinese
     documents under docs/, which are the behavioural oracle, are never edited, and carry pre-existing
     violations of their own that this refactor must not act on.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e, and
     no Markdown linter appears in it. A documented on-demand package-runner invocation would instruct an
     unpinned version to be resolved and executed from the network outside that lockfile every time
     somebody followed this document. Lint with tooling that is already installed; the directive below is
     what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# PowerFramework → .NET 10 — Local Orchestration

This directory holds the **one** local orchestration path for the four Phase-1 services, and this document
is its operating manual. It covers bring-up, the ordered readiness gates, the access surface, teardown, the
decisions taken here and the reason for each — and, in [§10](#10-what-has-and-has-not-been-exercised),
what has and has not actually been run.

**Three files, and no others.**

| File | Role |
| --- | --- |
| [`docker-compose.yml`](docker-compose.yml) | The hand-authored manifest. **Authoritative for topology** — service names, ports, the volume, the network and the dependency chain |
| [`.env.example`](.env.example) | The committed template for the *uncommitted* environment file the manifest reads. It declares the variable roster and the ASP.NET Core configuration key each entry maps onto |
| `README.md` | This document |

**This is the primary and only built orchestration path.** There is no second manifest, no override file, no
profile, no Kubernetes, Helm or Terraform artifact, and no .NET Aspire AppHost anywhere in the repository
([§7.1](#71-decision-1--net-aspire-was-evaluated-and-rejected) records why the last of those was rejected
rather than merely omitted). If a stack is running, it was started from here.

**It brings up four services and nothing else.** Gateway, DataServices, Persistence and Security. The four
deferred services — DesignSystem, Documents, Integration and ScriptBridge — are not built in this phase,
not partially and not as stubs, so there is nothing here for a manifest to orchestrate;
[§7.3](#73-decision-3--5103-is-reserved-and-a-comment-is-not-a-stub) states that boundary in the terms an
auditor would check it in.

**Nothing here reaches the legacy tree.** No mount, bind, volume or build input names `ws_objects/`,
`pack/`, `oldversion/`, `res/`, `samples/`, `sciter_control/`, `tests/blink/`, `tests/sciter/`,
`tests/webview/`, or any root `*.dll`, `*.pbl`, `*.pbt`, `*.pbw`, `*.pbr` or `*.pbd`. The read-only
behavioural oracle stays read-only, and the repository-root [`.dockerignore`](../.dockerignore) keeps every
one of those paths out of the build context as well —
[§3.5](#35-the-build-context-is-the-repository-root) explains why that second control is not a restatement
of the first.

---

## Current state of the artifacts this document references

**Every artifact this document names is present in the tree.** An earlier revision of this section listed
[`characterization/`](../characterization) as planned and absent; it exists, and so does everything else
referenced below — the manifest and the environment template beside this file, all four container
definitions, all four service applications, the six shared libraries, the contract definitions,
[`../.github/workflows/ci.yml`](../.github/workflows/ci.yml), the seven documents under
[`../docs/`](../docs), [`tests/e2e`](../tests/e2e) and the read-only legacy tree.

What is genuinely absent inside `characterization/` is **content, not structure**, and the distinction is
the difference between a scaffold to fill and work still to be designed:

| Artifact | What it carries | State |
| --- | --- | --- |
| [`../characterization/workflows/`](../characterization/workflows) | Fifteen workflow definitions and the JSON schema they validate against | **Present** |
| [`../characterization/README.md`](../characterization/README.md), and the two `recordings/` half-store readmes | The capture model, and a restatement of the rule of [§7.2](#72-decision-2--the-persistence-db-volume-rename) | **Present** |
| `characterization/recordings/legacy/<workflowId>/`, `.../dotnet/<workflowId>/` | The paired recordings themselves | **Absent — no capture has been taken on either side.** The legacy half needs the PowerBuilder oracle, which no Linux container can run |

**What this document is, and what it is not.** Its commands are a specification a reader can execute.
[§10](#10-what-has-and-has-not-been-exercised) is the single authority in this repository for what has
actually been run, and every other document defers to it rather than restating it — read it before treating
any command below as a transcript.

---

## Table of contents

1. [Position and ground rules](#1-position-and-ground-rules)
2. [Prerequisites](#2-prerequisites)
3. [Bring-up](#3-bring-up)
4. [The ordered readiness gates](#4-the-ordered-readiness-gates)
5. [The access surface](#5-the-access-surface)
6. [Teardown, restart policy and scaling](#6-teardown-restart-policy-and-scaling)
7. [The four documented decisions](#7-the-four-documented-decisions)
8. [Token topology and secrets handling](#8-token-topology-and-secrets-handling)
9. [Five deviations from the environment's instructions](#9-five-deviations-from-the-environments-instructions)
10. [What has and has not been exercised](#10-what-has-and-has-not-been-exercised)
11. [Troubleshooting](#11-troubleshooting)
12. [Where to read more](#12-where-to-read-more)

---

## 1. Position and ground rules

### 1.1 No user-specified rules exist

The project's rules document contains exactly one statement: **no user rules were provided.** It is a
single line, and re-reading it returns the same result. Nothing in this directory exists because a coding
guideline demanded it, and no rule has been invented, inferred or back-filled from convention.

**The absence of rules is not permission to lower the bar.** Enterprise-standard best practice applies in
their place, and the parts of it this directory is responsible for are stated rather than assumed: no
secret in the manifest, in the template or in any container definition; no floating, wildcard or `latest`
version anywhere in the images the manifest builds; multi-stage container definitions running as an
unprivileged user; one explicitly declared network and one explicitly declared volume rather than
Compose's implicit defaults; and every decision recorded here **with its reason**, so that a later reader
has to overrule a decision rather than fill in a blank.

What *does* bind this work is the brief's own clauses and the attached environment's setup instructions.
The instructions are treated as binding operational constraints, and the five points where they and the
brief genuinely conflict are resolved openly in
[§9](#9-five-deviations-from-the-environments-instructions) rather than absorbed silently.

### 1.2 Why a decomposition needs a readiness contract at all

The legacy framework is a **library**: 39 PowerBuilder libraries loaded into one process, with no process
of its own, no listener and no server tier. It opens no socket, registers no route and receives no
unsolicited request. So this decomposition creates the system's **first-ever ingress**, and every boundary
in the topology below is new — none of them translates an existing wire format.

Two consequences shape this whole document. Every new surface is **authenticated from the outset** rather
than only at the edge, which is why [§8](#8-token-topology-and-secrets-handling) is as long as it is. And a
call that could not fail in transit now can, which is why start ordering is expressed as a health condition
in [§4](#4-the-ordered-readiness-gates) instead of being left to chance.

The startup and shutdown ordering has a direct legacy ancestor. `pfwInitialize([flags])` goes at the very
start of `Application Open` and `pfwFinalize` at the very end of `Application Close`, and the read-only
framework documentation carries an explicit warning that **the two must be paired**
[`../docs/README.md` §初始化]; the legacy application shows both halves at
[`ws_objects/pfw.pbl.src/pfw.sra:L91`] and [`:L108`]. In the .NET services that pairing is host startup and
shutdown, and its failure mode is preserved — see [§6.2](#62-there-is-no-restart-policy-deliberately).

### 1.3 No performance objective is asserted anywhere in this document

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment. None is claimed here, and no command below is presented as fast, efficient or
tuned. The only quantitative non-functional requirement in the brief is the **80% line-coverage floor per
service**, which is a build property and not an orchestration one.

What *is* required, and is architectural rather than performance-related, is that each service be
**independently scalable**. That holds structurally — four images with no in-process dependency between them,
so instance counts can differ per service — but it is a property of the architecture rather than of this
manifest: as written, **the manifest runs exactly one container per service**, because each publishes a fixed
host port. [§6.3](#63-scaling-replicas-and-container-names) states that precisely, gives the override that
makes a replicated service work, and names the sticky-routing contract a second replica requires. No figure
is attached to any of it, because no baseline exists to compare one against.

### 1.4 There is no user interface, and none is expected

This phase is API and service-level only. No presentation surface is created, no component library or
design system is introduced, and nothing below describes a UI, a dashboard or an admin console — the
manifest deliberately runs no such thing. The capability area that would own a presentation surface is
DesignSystem, which is precisely one of the four deferred services. Consistently with that, the legacy
application disables its own theming outright with `themename = "Do Not Use Themes"`
[`ws_objects/pfw.pbl.src/pfw.sra:L25`].

---

## 2. Prerequisites

**Docker Engine with the Compose plugin.** Every command in this document uses `docker compose` (the
plugin subcommand). If your tooling predates the plugin, the standalone `docker-compose` binary accepts the
same arguments — substitute it verbatim wherever `docker compose` appears below.

**Nothing else.** In particular, nothing in the .NET tree depends on the PowerBuilder toolchain, the
PowerBuilder runtime, or any of the shipped native binaries. No image carries `pfw.dll`, `pfwx.dll`,
`sciter.dll`, `blink.dll`, `blinkfast.dll`, `sqlite3.dll` or `pfw.pack.pbd`, and nothing in any composition
root loads one. That is not a coincidence: the read-only documentation states that a module which is not
explicitly initialized is unusable **and its DLL need not be shipped** [`../docs/README.md` §高级初始化],
which is exactly the property that lets these images ship without native material at all.

**Only if you opt out of automatic schema provisioning** — the manual route of
[§3.4.1](#341-the-manual-route-for-a-stack-that-has-opted-out) — do you additionally need the .NET SDK and
the `dotnet-ef` tool on the host, because the runtime image deliberately carries neither. The default
bring-up needs neither: Persistence applies its own pending migrations at startup. [`../docs/BUILD.md`](../docs/BUILD.md) §4 lists the toolchain and §5.6 gives
the migration command; both are host-side concerns and neither is needed to *start* the stack.

**`openssl` on the host**, for generating the local signing identity and certificate set in
[§3.2](#32-step-2--generate-the-local-material). The generation recipe is not duplicated here — it lives in
one place, [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1.

---

## 3. Bring-up

Four steps, in this order. Steps 1 and 2 are one-time-per-environment, step 3 starts the stack, and step 4
is one-time-per-volume. Step 4 is listed **after** step 3 for a reason that is easy to get wrong: the
database lives on a named volume the manifest declares, so the volume has to exist before anything can be
written into it — and until the schema is applied, Persistence reports *not ready* and the dependency chain
correctly holds DataServices and Gateway back. That is the readiness gate working, not a fault in it.

**Every command below is run from the repository root** unless the block itself changes directory. Where a
block writes key material it changes directory first, deliberately, so that nothing lands in the working
tree.

### 3.1 Step 1 — the environment file

The manifest reads its `${...}` references from an environment file. [`.env.example`](.env.example) is the
committed template; it declares the roster and names the configuration key each variable maps onto, so it
documents itself and the roster is not duplicated here.

**Keep the populated file outside the working tree.** This is the documented default, and the reason is
concrete rather than stylistic: the repository-root [`.gitignore`](../.gitignore) contains **no `.env`
pattern** — it carries exactly `/pack/*`, `/packuse/*`, `/blink/*`, `/bak/*`, `/*.usr.opt`,
`/**/pfw.dev.*`, `*.dmp`, `*.log`, `*.bak`, `*.bat`, `*.zip`, `*.rar`, `thumbs.db` and two `!` re-inclusions
— and **this refactor adds none**, because the root ignore file belongs to the read-only legacy repository.
A populated `orchestration/.env` holding the system's one signing key would therefore be an ordinary
untracked file that `git add -A` would stage.

```bash
set -euo pipefail
# From the repository root.
PFW_ENV="${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env"
install -d -m 700 "$(dirname "$PFW_ENV")"

# THE GUARD IS THE POINT, NOT THE COPY. Re-running this block after the file is populated would otherwise
# replace a live signing key and three caller secrets with the empty template - a stack that starts and then
# rejects every token in the system, with the cause nowhere near the symptom, and nothing to restore from.
if [ -e "$PFW_ENV" ]; then
  printf 'Keeping the existing environment file at %s\n' "$PFW_ENV"
else
  cp orchestration/.env.example "$PFW_ENV"
  chmod 600 "$PFW_ENV"
  printf 'Created %s from the template.\n' "$PFW_ENV"
fi
# Then populate it using the roster in .env.example and the generation commands in step 2.
```

**The in-tree form the attached environment documents remains supported — with one prior step.** Add a
per-clone exclusion *before* writing any key material. `.git/info/exclude` is untracked and per-clone, so it
excludes the file without editing the read-only root ignore file:

```bash
set -euo pipefail
# From the repository root.
grep -qxF 'orchestration/.env' .git/info/exclude 2>/dev/null \
  || echo 'orchestration/.env' >> .git/info/exclude

if [ -e orchestration/.env ]; then
  printf 'Keeping the existing orchestration/.env\n'
else
  cp orchestration/.env.example orchestration/.env
  chmod 600 orchestration/.env
fi
```

Three details there are load-bearing. The exclusion is written **first**, before any key material can
exist, and **idempotently** — `grep -qxF` keeps a re-run from appending the same line a second time. The
copy is **guarded and reports which branch it took**, so a re-run cannot overwrite a populated file and you
are told that it did not; `cp -n` would also refuse, but silently, and current coreutils warns that its
behaviour is non-portable. And there is **no `cd`**: an earlier revision ran `cd orchestration` and then
copied, and in a `cd x && test || cp` list a failing `cd` does not abort even under `set -e`, so the copy
landed in whatever directory you were actually standing in. Paths relative to the repository root have no
such failure mode.

**One control that is already in place, and what it does not cover.** The repository-root
[`.dockerignore`](../.dockerignore) excludes `orchestration/` outright plus `**/.env` and `**/.env.*`, so no
environment file can enter a build context or an image layer — this template included. That control is about
**images**. It does nothing about version control and is not a substitute for either option above.

### 3.2 Step 2 — generate the local material

Two kinds of artifact are required, and they are produced by different commands. Conflating them is the
single most common way to get a stack that starts and then fails on its first token.

| Variable | Kind | What it receives |
| --- | --- | --- |
| `SECURITY_JWT_SIGNING_KEY` | **Material, not a path** | The signing key **value** — base64 of the PKCS#8 DER encoding **on one line**, because the Compose dotenv format has no line continuation and a PEM block cannot be written in it. PEM is also accepted, and tried first, for a secret store that can carry newlines |
| `SECURITY_CLIENT_SECRET_GATEWAY`, `SECURITY_CLIENT_SECRET_DATASERVICES` | Material | One shared secret **per caller** that may ask Security for a token. A different value each. Holding one lets a service *ask* for a token; it does not let it *mint* one |
| `TLS_CERTIFICATE_PATH`, `TLS_CERTIFICATE_KEY_PATH`, `INTERNAL_TLS_CA_PATH` | **Paths on THIS HOST** | The shared multi-SAN server certificate, its key, and the CA that signed it. Every listener in this stack terminates TLS and every image probe verifies the certificate it is presented, so all three are **required**: the manifest declares each as a Compose **secret source** and projects it read-only into all four containers under `/run/secrets/internal-tls/`. Bring-up aborts by name if one is unset **or names a file that does not exist**. Do not point them at `/run/secrets/...` — that is where they land, not where they come from |
| `SECURITY_MTLS_CLIENT_CA_PATH`, and the two `*_MTLS_CERT_PATH` / `*_MTLS_KEY_PATH` pairs | Paths | **Optional.** The client-certificate alternative on the issuance edge — see [§8.3](#83-mutual-tls-is-a-documented-fallback-not-scaffolding) |

**The signing key is an RSA private key, not random bytes.** Security signs **RS256** over a closed
RS256/RS384/RS512 allow-list and publishes an RSA-only key set, so a random symmetric string cannot sign it
and cannot be published as an RSA JWK: `openssl rand` produces material the import path rejects and the host
refuses to start. A 2048-bit floor is enforced, is raisable and is **not** lowerable.

```bash
set -euo pipefail
# Generate OUTSIDE the working tree, for the reason step 1 gives: nothing here excludes key material.
install -d -m 700 "$HOME/.config/powerframework/secrets"
cd "$HOME/.config/powerframework/secrets"

# The signing identity. The single base64 line is the VALUE of SECURITY_JWT_SIGNING_KEY, never a path.
# It is written to a file rather than printed, so it never reaches your scrollback or shell history.
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out security-signing.key
chmod 600 security-signing.key
openssl pkey -in security-signing.key -outform DER | base64 -w0 > security-signing.b64
chmod 600 security-signing.b64
# Copy the one line out of security-signing.b64 into the environment file. Do not echo it.

# One shared secret per caller. Written to a 0600 file rather than printed, for the same reason the
# signing key is: `openssl rand -base64 32` on its own puts the value in your terminal scrollback, and
# from there into any session log or screen capture. Run this once PER CALLER and use a DIFFERENT value
# for each -- a shared value makes the callers indistinguishable to the permission matrix.
for caller in gateway dataservices; do
  ( umask 077; openssl rand -base64 32 > "caller-${caller}.secret" )
done
# Copy each one line into the environment file. Do not echo it, and do not pass it on a command line --
# section 5.2 shows the form that keeps it off argv.
```

**One permission in the certificate recipe looks lax and is required.** The server private key is the one
file the manifest **projects into the containers**, and Compose accepts `mode:`, `uid:` and `gid:` on a
secret while **ignoring all three outside Swarm** — measured, not assumed. A host key at `0600` therefore
arrives inside the container as `-rw------- root root`, every image runs as an unprivileged account, and
Kestrel refuses to start for want of read permission. §9.3.1 generates that one key `0644` inside the `0700`
directory created above; the directory is the real host control, and the file never leaves your machine. The
signing key, the CA key and the two caller keys are **not** projected and stay `0600`.

**The certificate set is one command block, and it is not duplicated here.**
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1 is the single place it lives: the local
authority, the shared server certificate **with its subject alternative names**, and the two optional caller
certificates. Read that section rather than improvising, because one property of it is not obvious and
breaks everything quietly when missed — one certificate is presented under several names
(`security-service`, `persistence-service`, `dataservices-service` and `localhost`, plus the loopback
addresses), current TLS stacks ignore the common name and read `subjectAltName` only, and a certificate
carrying a common name alone therefore matches **nothing at all**, including the name it appears to carry.

**Then name the three files in `orchestration/.env`, which is §9.3.1 step 6 and is not optional.** Nothing
is copied or assembled: the manifest declares three top-level Compose **secrets** whose `file:` sources are
those three host paths, and projects them **read-only** into all four services at three fixed container
paths — `/run/secrets/internal-tls/server.crt`, `…/server.key` and `…/ca.crt`, which are the canonical
defaults every service setting and all four image `HEALTHCHECK`s already read. Point
`TLS_CERTIFICATE_PATH`, `TLS_CERTIFICATE_KEY_PATH` and `INTERNAL_TLS_CA_PATH` at the generated files as
**absolute** paths, because Compose resolves a relative secret source against the manifest's own directory.

> ### ⚠ The private key must be readable by UID 1654, and the projection will not arrange that for you
>
> Compose accepts `mode:`, `uid:` and `gid:` on a secret and **ignores all three outside Swarm**, so the host
> file's ownership and mode arrive numerically unchanged, and every runtime stage drops to the unprivileged
> `app` account the base image publishes as UID 1654. A key at mode `0600` owned by your own account is
> therefore unreadable inside the container, and **Kestrel then fails exactly as if the file were absent** —
> the container crash-loops with an unresolvable certificate and nothing distinguishes the two causes from
> the outside. §9.3.1 part 4b makes the key `0644` inside a `0700` directory for exactly this reason, which
> is safe because the directory is the real host control. Where root is available, the closed alternative is
> to give the key to that account instead:
>
> ```bash
> sudo chown 1654 server.key && chmod 600 server.key
> ```
>
> **Per-service certificates are the alternative and are equally correct**, and they are the right choice
> when each service carries its own pair: declare one certificate and one key secret per service, source
> each from its own variable, and grant each service only its own — the three container-side `target:`
> paths stay exactly as they are, so no service setting and no `HEALTHCHECK` changes. The manifest records
> why one shared pair is the default: it is what a single multi-SAN certificate buys, and it keeps the
> secret roster at three entries rather than nine.

### 3.3 Step 3 — start the stack

One command brings all four services up together. Both forms below are equivalent, and both auto-load
`orchestration/.env` when no `--env-file` is given, because Compose reads `.env` from the **project
directory** and the project directory is the directory holding the manifest:

```bash
# THE DOCUMENTED FORM: the environment file kept outside the working tree, as step 1 recommends.
# From the repository root.
docker compose -f orchestration/docker-compose.yml \
  --env-file "${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env" up --build -d

# Equivalent, and the shape the attached environment documents. Compose auto-loads `orchestration/.env`
# when no --env-file is given, because it reads `.env` from the PROJECT directory and the project directory
# is the one holding the manifest. Use this only with the in-tree form of step 1, exclusion first.
cd orchestration && docker compose up --build -d
```

**The three host paths are projected for you, and that is what makes this one command enough.** The manifest
does not inject them into any container: it consumes them as Compose **secret sources** and decides the
container-side paths itself, as literals, so the material arrives read-only at
`/run/secrets/internal-tls/server.crt`, `…/server.key` and `…/ca.crt` in all four services with no override
file and no `volumes:` entry of your own. What the deployment owns is the host half — which file feeds each
of the three secrets — and nothing else
([§8.3](#83-mutual-tls-is-a-documented-fallback-not-scaffolding) states the one exception, the caller
material of the mutual-TLS fallback, which is *not* projected and needs a secret source and grant of its
own).

**Make the files readable by the unprivileged runtime account.** Every service runs as the base image's
non-root `app` account, so material readable only by your host user makes the process fail to start rather
than fall back — the log names the unreadable file and the container exits. `chmod 644` on the server key,
as §9.3.1 part 4b does, or `chown 1654` to match `APP_UID` in the container definitions, both work; a `600`
file owned by your host user does not, because Compose ignores `mode:`, `uid:` and `gid:` on a secret
outside Swarm and hands the file over exactly as it found it.

Then watch the chain converge. `docker compose ps` reports each service's health, and
[§4](#4-the-ordered-readiness-gates) is the ordered list of what has to go green and why:

```bash
docker compose -f orchestration/docker-compose.yml ps
docker compose -f orchestration/docker-compose.yml logs -f gateway-service
```

**A missing required variable aborts bring-up by name, before anything is built.** That is deliberate: the
manifest marks the material variables required with the `:?` form so an issuer never starts without a key,
and the abort message names the variable and what to do. Run against no environment file at all, the first
one you meet is the certificate path:

```text
error while interpolating secrets.tls-server-certificate.file: required variable
TLS_CERTIFICATE_PATH is missing a value: TLS_CERTIFICATE_PATH must be set in orchestration/.env to a
path ON THIS HOST holding the PEM server certificate chain. Every listener in this stack is https and
Kestrel refuses to start without one. Generate the set with docs/ARCHITECTURE.md section 9.3.1.
```

A path that is *set* but names no existing file aborts just as early, from Docker rather than from the
interpolator, and names the path it could not read. Both are the same guarantee: a container is never created
for a stack whose TLS material is not actually present.

**To check the manifest without starting anything**, ask Compose to resolve it. This validates YAML,
interpolation and the resolved model, and it is *only* a static check — it starts no container and proves
nothing about runtime behaviour:

```bash
docker compose -f orchestration/docker-compose.yml config -q       # exit 0 = resolves cleanly
docker compose -f orchestration/docker-compose.yml config          # print the resolved model
```

### 3.4 Step 4 — provision the Persistence database *(not required on the default bring-up)*

**On the documented bring-up this step is already done for you, and nothing here needs running.** The
manifest sets `Schema__ApplyMigrationsOnStartup` to true for `persistence-service`, so on a brand-new
`persistence-db` volume the container applies its pending migrations between its startup gate and its
request pipeline — before its readiness probe is ever asked — and then answers `/health` 200, which opens
the dependency gate for DataServices and Gateway. `docker compose --env-file .env up --build -d` reaches a
healthy stack from an empty volume in one command.

**What it does is `Database.Migrate` and nothing else**: additive and idempotent, creating what the migration
history table does not already record and dropping, deleting and reseeding nothing. There is no
`EnsureCreated`, no `EnsureDeleted` and no `DROP` anywhere in the service. Concurrent replicas serialize
through an exclusive lock file on the volume, and a failure terminates the container rather than starting a
service that could answer nothing — so a provisioning fault is visible as a container that will not stay up,
never as a service that reports healthy and refuses every call.

**What this looked like before, recorded because the old behaviour read as a broken stack.** Persistence
applied no migration at all, so a fresh volume had no `COMPANY` table, its `/health` reported not ready for
ever, and the dependency chain correctly held two services back — a stack in which three of four services
never became healthy, with nothing in the manifest able to fix it. The remedy was the manual sequence below,
which an operator following this document had no reason to know they needed.

**No init container does this, and that is still deliberate.** The runtime image carries neither the SDK nor
the `dotnet-ef` tool, so a migration container would be a *fifth* service built from a different base image
— and this manifest is exactly four services by requirement. That is precisely why the step lives inside the
service that owns the storage.

#### 3.4.1 The manual route, for a stack that has opted out

**Set `PERSISTENCE_APPLY_MIGRATIONS_ON_STARTUP=false` in `orchestration/.env`** and provisioning returns to
being an operator step — which is the right choice when a deployment pipeline or a DBA owns the schema, when
you want to inspect a migration before it runs, or when a characterization capture must be able to state that
nothing but the workflow under characterization opened the database at all. Persistence's own
`appsettings.json` defaults the switch to false, so this is the code default rather than an override, and the
manifest is the only thing that opts in.

**On the binding spelling, because two are plausible and only one works.** The manifest passes
`Schema__ApplyMigrationsOnStartup`, unprefixed, and that is the one that binds: Persistence's configuration
root is its settings file itself rather than a named section, exactly as its `Sqlite` and `TransactionPool`
sections are. `Persistence__Schema__ApplyMigrationsOnStartup` is not a second name for it and sets nothing.

**With the switch off the container creates nothing at all** — no database file, no lock file, no directory
entry of any kind — so a fresh volume stays *not ready* and the health-conditioned chain in §4 stays shut
behind it. That is the honest report rather than a fault, and it is why the schema then has to reach the
volume by the route below.

#### Provisioning the volume out of band, when the startup path is switched off

This is that route, and it is the same migration the startup path would have applied:

```bash
set -euo pipefail
# From the repository root.
PROVISION="$HOME/.config/powerframework/provision"
install -d -m 700 "$PROVISION"

# 1. Generate the schema on the host, per docs/BUILD.md section 5.6. Idempotent and non-destructive.
cd services/persistence-service/PowerFramework.Persistence
dotnet build -c Release
dotnet ef database update --no-build --configuration Release \
  -c PowerFrameworkDbContext --connection "Data Source=$PROVISION/test.db"

# 2. Place it on the persistence-db volume. The volume is named <project>_persistence-db, and the project
#    name defaults to the manifest's directory - so `orchestration_persistence-db` unless you set -p.
#    --reference copies the OWNER OF THE DIRECTORY onto the file, so no account id is hardcoded here and
#    nothing drifts if the runtime base image ever renumbers its unprivileged account.
docker run --rm \
  -v orchestration_persistence-db:/var/lib/powerframework \
  -v "$PROVISION:/provision:ro" \
  mcr.microsoft.com/dotnet/aspnet:10.0.11 \
  sh -c 'cp /provision/test.db /var/lib/powerframework/test.db \
      && chown --reference=/var/lib/powerframework /var/lib/powerframework/test.db'
```

The copy is idempotent and non-destructive: re-running it against a volume that already carries the file
replaces it with the same schema, which is why a paired capture may repeat it between runs without changing
what the two halves see.

**Applying a migration never touches a row either way**, so the switch is a belt-and-braces guarantee rather
than the thing that protects a capture. What actually destroys a paired capture is recreating the volume,
which is `docker compose down -v` and `docker volume rm`; §6.1 and §7.2 carry that rule.

**The database file is named `test.db`** — the legacy name, retained deliberately because it is the name the
behavioural oracle opens [`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456`]. It is configured, not
hardcoded, in Persistence's own settings alongside the rest of the URI grammar the legacy documents:
`mode=rwc`, no integrity check, and journal mode `DELETE`, which is the legacy default among the six that
grammar admits [`:L452-L455`]. It lives at `Sqlite:DataDirectory`, which is `/var/lib/powerframework` — the
path the Persistence image creates, chowns to its unprivileged account and declares a `VOLUME`, and the path
the manifest mounts the named volume at.

### 3.5 The build context is the repository root

The manifest sets `context: ..` with `dockerfile: services/<service-name>/Dockerfile` for all four services.
A relative build context resolves against the directory of the manifest, so from `orchestration/` the `..` is
the repository root.

**Do not "simplify" it to `.`.** That is not a style point — narrowing the context breaks the build outright
rather than degrading it, for two independent reasons:

- every service project carries `ProjectReference` entries that rise **out of** its own directory into
  `shared/` — the published contracts project and the ported shared libraries; and
- MSBuild resolves [`../Directory.Build.props`](../Directory.Build.props),
  [`../Directory.Packages.props`](../Directory.Packages.props) and [`../global.json`](../global.json) by
  walking **up** from each project to the repository root, which is where central package management lives.
  Every `PackageReference` in this repository is deliberately **versionless**, so without
  `Directory.Packages.props` restore fails rather than falling back to a guess.

A Docker build cannot read anything outside its context, so a context scoped to `services/<name>/` can see
none of that.

**The root [`.dockerignore`](../.dockerignore) exists specifically because the context is the repository
root, and it is doing two jobs rather than one.** The first is what it looks like: it keeps roughly 160 MB
of read-only legacy material — `ws_objects/`, `pack/`, `oldversion/`, `res/`, `samples/`,
`sciter_control/`, the legacy browser assets under `tests/`, and every root native binary and PowerBuilder
artifact — out of every upload and every image layer, which is how the images end up carrying no native
material at all. The second is that it is **also a secrets control**: six of the eight in-source credential
sites the repository-wide sweep found sit inside those excluded regions, so excluding them keeps that
material out of image layers as a structural property rather than as something a review has to catch. The
locators are in [`../docs/SECRETS.md`](../docs/SECRETS.md) and no value is reproduced anywhere.

---

## 4. The ordered readiness gates

### 4.1 The chain, and the dependency reason for each link

Bring-up converges in this order, and the order is not a convention — each link waits on the one above it
because it cannot serve without it. The manifest declares the four service definitions in exactly this
order so the chain reads top to bottom.

| # | Gate | Waits on | Why it waits |
| --- | --- | --- | --- |
| 1 | `security-service` `/health` on **5104** | nothing | Security is the root of the trust graph: it mints, and the other three verify against what it publishes. It has **no `depends_on` at all**, and giving it one would create a cycle Compose refuses outright |
| 2 | `persistence-service` `/health` on **5101** | Security **healthy** | Its bearer handler fetches Security's published key set to verify inbound tokens. Started first it would answer its own anonymous `/health` while rejecting every authenticated call for want of a key — which reads to an operator as a token problem when it is a start-ordering problem |
| 3 | `dataservices-service` `/health` on **5102** | Persistence **and** Security, both **healthy** | Every retrieval, validation and update it serves is ultimately a gRPC call into Persistence; and it both verifies inbound tokens against Security's key set *and* obtains tokens from Security for its own outbound calls. It is the only service that is both caller and callee of two others, which is why it carries two conditions |
| 4 | `gateway-service` `/health` on **5105** | Persistence, DataServices **and** Security, all **healthy** | It is the sole ingress and composition root, and it can compose nothing until the three services beneath it are serving |
| 5 | **The aggregate:** Gateway reports healthy **only after** Persistence, DataServices and Security do | — | This is the property the whole manifest exists to express, and [§4.2](#42-the-gate-has-two-halves-and-both-are-required) explains why it is not the same statement as gate 4 |

**Every gate addresses `/health`, and never `/v1/ping`.** `/health` is anonymous, which is what lets a
container probe satisfy it with no credential at all. `/v1/ping` requires a bearer token and answers `401`
by design, so a probe pointed at it would mark every healthy container unhealthy for ever.

**Every gate addresses the one listener each service binds** — 5101, 5102, 5104 and 5105. Persistence and
DataServices carry their gRPC contracts on those same two endpoints, which declare `Protocols:
Http1AndHttp2`, so the gate's HTTP/1.1 `GET` and a caller's HTTP/2 gRPC call negotiate independently over TLS
on one port. There is consequently no unprobed listener anywhere in the stack: the address that answers a
gate is the address that carries the contracts.

**`Degraded` and `Unhealthy` both answer `503`; only `Healthy` answers `200`.** The three tokens stay
distinct in the response body — a service still completing startup validation is not the same as one whose
dependency has failed — but a readiness gate observes the status **code**, and `Degraded` means *not ready*.
One consequence is worth knowing before you read a red gate as a fault: **Gateway and DataServices report
`Degraded` until each holds one accepted caller credential**, so gates 3 and 4 depend respectively on
`SECURITY_CLIENT_SECRET_DATASERVICES` and `SECURITY_CLIENT_SECRET_GATEWAY` — or on that service's optional
client-certificate pair — actually being supplied. Each names the unmet setting in a `credentials` component
of its `/health` body.

### 4.2 The gate has two halves, and both are required

Gate 5 is not a restatement of gate 4. It has two mechanisms, one in this directory and one in Gateway's own
code, and **neither substitutes for the other**:

- **Start ordering** lives in the manifest, as `depends_on` with `condition: service_healthy`. The condition
  is the mechanism: a plain `depends_on` defaults to `service_started`, which waits only for the container to
  be created — and for a .NET host that is satisfied *before* Kestrel has bound a listener, let alone before
  Security can issue a token or Persistence can open its database. There is no `sleep`, no retry wrapper
  script and no entry-point poll anywhere in the manifest doing this job instead.
- **Reporting** lives in Gateway's own `/health` implementation, which aggregates its three upstreams and is
  what makes Gateway *report* healthy only once they are. Compose alone would order the starts and nothing
  more. The aggregate names all three upstreams individually, so an operator reading a failed aggregate
  learns *which* upstream is responsible.

The Persistence entry in that aggregate is **health observation only**, and that coexists with the layering
rule that Gateway never calls Persistence: the three probe addresses live in a separate configuration group
from the two services Gateway invokes, and each authorises exactly one anonymous `GET /health` and nothing
further. Gateway holds no Persistence client, channel or generated stub.

**This is the single most important readiness property in the orchestration, and it is the specific reason
.NET Aspire was rejected** — see [§7.1](#71-decision-1--net-aspire-was-evaluated-and-rejected).

### 4.3 The probes are declared by the images, not by the manifest

All four container definitions declare their own `HEALTHCHECK`, and the manifest deliberately declares
**none** — a compose-level `healthcheck:` would override the image's. Three reasons, and the first two are
failure modes rather than preferences:

1. **The image probes speak TLS, and a naive one cannot.** Every listener in this stack terminates TLS, so
   the two obvious manifest-side probes both fail *permanently* rather than intermittently. `curl --fail` is
   not available at all — the .NET runtime image ships **neither `curl` nor `wget`**, and carries `openssl`,
   `bash`, `timeout` and `head` instead — and a bash `/dev/tcp` redirection writes plaintext bytes into a TLS
   endpoint, so it never produces a status line. A probe that can only ever fail keeps a `service_healthy`
   gate shut for ever, which from the outside is indistinguishable from a service that never started.
2. **Compose interpolates `$`, and a shell probe is full of it.** Pasting an image probe into a compose
   `healthcheck:` renders its `$line` reference as an unset compose variable, silently, so the comparison can
   never match and the probe exits non-zero for ever. Every `$` would have to be doubled to `$$`, and one
   missed escape reproduces exactly that outcome with no error at bring-up. An image is simply the safer
   place for a shell pipeline to live.
3. **The Persistence definition asks for this explicitly**, stating that a compose manifest should inherit
   its declaration rather than write one of its own.

So the readiness contract stays legible from the manifest — it is stated there and cited to the line rather
than duplicated. Each image completes a TLS handshake with `openssl s_client`, verifies the presented
certificate against the mounted trust anchor, requests the anonymous `GET /health` over HTTP/1.1 against the
one port its service binds, and matches `' 200 '` in the status line. **None of them passes `-k`, `--insecure` or `-noverify`**, because
a probe that skips verification would report a service healthy while it presents material the rest of the
stack is about to reject.

### 4.4 Probing from the host, and why the trust anchor has to be named

The attached environment's readiness gates are `curl -sf http://localhost:<port>/health`. Every listener here
terminates TLS, so the corrected form is the same probe with the scheme changed **and the local authority
named** — the certificate each listener presents is issued by the throwaway authority of step 2 and is
trusted by nothing by default, so a probe that omits it fails on chain validation rather than on readiness,
which is a false negative that looks exactly like a service that never came up:

```bash
set -euo pipefail
CA="$HOME/.config/powerframework/secrets/mtls-ca.crt"

# The four *_HOST_PORT defaults are the documented map, so an operator who set none of them can read these
# four lines as literal 5104 / 5101 / 5102 / 5105. A second stack on this host overrides them (§6.3), and
# these gates then follow it without being edited.
curl -sf --cacert "$CA" "https://localhost:${SECURITY_HOST_PORT:-5104}/health"      # 1. Security
curl -sf --cacert "$CA" "https://localhost:${PERSISTENCE_HOST_PORT:-5101}/health"   # 2. Persistence
curl -sf --cacert "$CA" "https://localhost:${DATASERVICES_HOST_PORT:-5102}/health"  # 3. DataServices
curl -sf --cacert "$CA" "https://localhost:${GATEWAY_HOST_PORT:-5105}/health"       # 4. Gateway ingress,
                                                                                   #    and the aggregate
                                                                                   #    of 1-3
```

`localhost` is deliberate and is not interchangeable with an arbitrary alias: the server certificate carries
`localhost` and the loopback addresses as subject alternative names alongside the four Compose service names,
and hostname verification reads **only** that extension. Installing the authority into the host trust store
instead is equally valid and lets `--cacert` be dropped.

> **Never answer a probe failure with `-k` or `--insecure`.** It suppresses chain and hostname validation
> entirely, so the gate stops distinguishing the intended service from any listener on the port and stops
> being evidence of anything. If a gate fails, fix the trust anchor or fix the name — those are the two
> things this probe is able to teach you.

### 4.5 An honest note on the count of gates

The attached environment specifies **five** ordered health-probe gates, one per directory in its own
five-directory service roster, whose fifth entry was a `design-service` on 5103. DesignSystem is precisely
one of the four deferred services, so no fifth service exists to probe. The fifth gate here is therefore the
**Gateway aggregate**, which is a stronger check than a fifth service-level probe would have been because it
is the one gate that can only pass when all the others already have. The reserved 5103 slot
([§7.3](#73-decision-3--5103-is-reserved-and-a-comment-is-not-a-stub)) is where a fifth service-level gate
returns in Phase 2.

---

## 5. The access surface

### 5.1 The map

| Service | Host address | Listener | Carries | Token role |
| --- | --- | --- | --- | --- |
| `persistence-service` | `https://localhost:5101` | `Http1AndHttp2` | Anonymous `/health`, authenticated `/v1/ping` — **the documented readiness address** — and gRPC contracts C-05..C-08, whose only caller is DataServices, inside the network | verification only |
| `dataservices-service` | `https://localhost:5102` | `Http1AndHttp2` | Anonymous `/health`, authenticated `/v1/ping`, the thin REST projection consumed only by Gateway, and gRPC contracts C-03 and C-04, whose only caller is Gateway | verification only |
| *(reserved)* | — | — | **Port 5103 is reserved and unallocated** — see [§7.3](#73-decision-3--5103-is-reserved-and-a-comment-is-not-a-stub) | — |
| `security-service` | `https://localhost:5104` | `Http1` | The issuance edge, the C-02 crypto surface, `/.well-known/jwks.json`, OIDC discovery, `/health`, `/v1/ping` | **SOLE ISSUER** |
| `gateway-service` | `https://localhost:5105` | `Http1` | REST + OpenAPI. **The composition root and the only intended ingress** | verification only |

**`https://localhost:5105` is the composition root**, and the port is preserved exactly as the attached
environment fixes it so the documented access URL still resolves. It is the only address an external client
is meant to address.

**The other three ports are published for diagnosis, not for traffic.** They exist on the host so that the
per-service `/health` gates the environment documents can be exercised from outside, and so an operator can
reach one service directly while diagnosing. Nothing routes external traffic to them: the call graph is
layered and acyclic, and inside the network the services address each other by the Compose service names
rather than through a published port. A deployment that does not want them reachable from the host deletes
those three `ports:` blocks, which changes nothing about how the services talk to each other.

**Every listener terminates TLS, and no plaintext listener exists on any service.** That is a correctness
property on a decomposition rather than a hardening option: every one of these boundaries is created by this
refactor, every request across one carries a bearer token, and Security additionally publishes the key set
the whole estate verifies against — so on a readable channel every token is replayable and the trust
bootstrap is substitutable on path. The gRPC surfaces are consequently **TLS-terminated HTTP/2, not
plaintext h2c**, and they sit on their own ports rather than sharing the REST port so that a misaddressed
call fails loudly: a gRPC channel aimed at an `Http1` port fails with `HTTP_1_1_REQUIRED` before the request
arrives, and an HTTP/1.1 probe aimed at an `Http2` port gets a `400`.

### 5.2 `/health` is anonymous; `/v1/ping` is not

`/health` is **anonymous on all four services** — which is exactly why the container probes need no
credential — and `/v1/ping` **requires a JWT on all four and returns `401` without one**. `/v1/ping` is the
standing proof that the authenticated-boundary requirement holds on every service and not merely at the
ingress.

To exercise it, ask Security for a token and then present it. Security is the only service that mints.

**Three things in the block below are corrections to an earlier revision of it, and each one is the
difference between a runnable command and a plausible-looking one.** The request body was
`'<request body per security.v1.yaml>'` — a placeholder that `POST /v1/tokens` answers `400` to, because the
schema sets `additionalProperties: false` over three required members. The token was never captured, so
`$PFW_TOKEN` in the second command was unset and, under the `set -u` this block itself declares, the shell
aborted before curl ran. And the credential was passed as `-u "$PFW_CALLER:$PFW_CALLER_SECRET"`, which puts
a live secret into the process's `argv` — visible to `ps` for every account on the host, and captured by any
audit or shell-history mechanism that records command lines.

**Credentials are fed to `curl` on standard input, never as arguments.** `--config -` reads its
directives from stdin, so neither the Basic credential nor the bearer token is ever an element of
`curl`'s argument vector. Three exposure channels close at once, and the measurement behind each is
worth recording because two of them are commonly assumed to be safe when they are not:

- **Process inspection.** `-H "authorization: Bearer $TOKEN"` is fully visible in
  `/proc/<pid>/cmdline` and in `ps` output for the life of the request -- **curl does not redact
  headers**. Recent curl *does* overwrite `-u` in its own argv, but that is a version-dependent
  mitigation applied after `exec`, not a guarantee, and it never covered `-H` at all.
- **Shell tracing.** Under `set -x` both `-u` and `-H` are echoed **in full, after expansion**, so
  taking the value from an environment variable does not help. A heredoc body is not traced, so the
  form below emits only `+ curl -sf --config - ...`.
- **Shell history and command capture**, for the same reason: there is no credential on the line.

```bash
set -euo pipefail

CA="$HOME/.config/powerframework/secrets/mtls-ca.crt"

# The caller, the audience it may address and the scope it needs. These three are not free choices: they
# have to match a row in Security's grant matrix, or issuance refuses the request. `pfw-e2e-suite` ->
# audience `powerframework-gateway` with scopes `ping`, `capabilities` and `datawindow` is registered in
# Security's DEVELOPMENT overlay, which is what the documented bring-up selects. Gateway's /v1/ping
# additionally requires the `ping` scope specifically.
PFW_CALLER="pfw-e2e-suite"
PFW_AUDIENCE="powerframework-gateway"

# The caller's shared secret, read from the environment file rather than typed. It is the value of
# SECURITY_CLIENT_SECRET, which is the configuration key Security's roster names for this subject.
: "${SECURITY_CLIENT_SECRET:?export SECURITY_CLIENT_SECRET from your environment file first}"

# 1. THE CREDENTIAL GOES IN A FILE, NOT ON THE COMMAND LINE. `-u user:secret` publishes the secret in the
#    process's argv, where `ps` shows it to every account on the host and shell history keeps it. curl's
#    --config file is read privately; `umask 077` creates it unreadable to anyone else, and the trap
#    removes it on every exit path including a failure under `set -e`.
CURLRC="$(umask 077 && mktemp)"
trap 'rm -f "$CURLRC"' EXIT
printf 'user = "%s:%s"\n' "$PFW_CALLER" "$SECURITY_CLIENT_SECRET" > "$CURLRC"

# 2. Obtain a token from the sole issuer (contract C-01). A caller cannot present a bearer token to obtain
#    its first one, which is why this one operation authenticates with a shared secret.
#
#    THE BODY IS THE EXACT TokenRequest SHAPE: `subject`, `audience` and `scopes`, all three REQUIRED, and
#    `additionalProperties: false` - so an extra member is a 400 rather than an ignored field. The
#    authority is shared/PowerFramework.Contracts/OpenApi/security.v1.yaml.
#    THE ASSIGNMENT IS THE `if` CONDITION, and that shape is load-bearing under `set -e`. Written as a
#    plain assignment, a refusal status would abort the script at this line and the response body - the one
#    thing that says WHY - would be swallowed with it. As a condition, `set -e` is suspended, the body is
#    still captured because `--fail-with-body` writes it to stdout, and the failure is reported with it.
if ! TOKEN_RESPONSE="$(
  curl -sS --fail-with-body --cacert "$CA" --config "$CURLRC" \
       -H 'content-type: application/json' \
       --data "{\"subject\":\"$PFW_CALLER\",\"audience\":\"$PFW_AUDIENCE\",\"scopes\":[\"ping\"]}" \
       "https://localhost:${SECURITY_HOST_PORT:-5104}/v1/tokens"
)"; then
  printf 'Issuance refused the request. Response body:\n%s\n' "$TOKEN_RESPONSE" >&2
  exit 1
fi

# 3. CAPTURE THE TOKEN AND PROVE IT IS THERE BEFORE USING IT. A JWT contains no double quote, so the
#    field can be lifted without a JSON parser; `jq -r .access_token` is the equivalent where jq is
#    installed. The trailing `|| true` is required rather than defensive: `grep` exits 1 when it matches
#    nothing, and under the `pipefail` this block declares that would abort the script one line before the
#    guard that exists to explain it.
PFW_TOKEN="$(
  printf '%s' "$TOKEN_RESPONSE" \
    | grep -o '"access_token"[[:space:]]*:[[:space:]]*"[^"]*"' \
    | sed 's/.*"\([^"]*\)"$/\1/' || true
)"

if [ -z "$PFW_TOKEN" ]; then
  printf 'Issuance answered 2xx but carried no access_token. Response body:\n%s\n' \
         "$TOKEN_RESPONSE" >&2
  exit 1
fi

# 4. Present it. Without it, every one of the four /v1/ping endpoints answers 401 - that is the point of
#    the endpoint. `-o /dev/null -w` prints the status rather than the body, so nothing echoes the token.
curl -sS --cacert "$CA" -o /dev/null -w '/v1/ping -> %{http_code}\n' \
     -H "authorization: Bearer $PFW_TOKEN" \
     "https://localhost:${GATEWAY_HOST_PORT:-5105}/v1/ping"

# 5. And the negative half, which is the assertion rather than an afterthought: the same address with no
#    credential must answer 401.
curl -sS --cacert "$CA" -o /dev/null -w 'unauthenticated /v1/ping -> %{http_code}\n' \
     "https://localhost:${GATEWAY_HOST_PORT:-5105}/v1/ping"
```

**The token is a bearer credential for its whole lifetime**, so it is held in a shell variable and never
written to a file, never echoed, and never passed as a URL parameter. `--fail-with-body` rather than `-f` on
the issuance call is deliberate: `-f` discards the response body on an error status, which is exactly the
body the guard in step 3 needs to print.

Security also publishes `/.well-known/jwks.json` and `/.well-known/openid-configuration`
**anonymously**, and that is the reason Security speaks REST rather than gRPC: a stock bearer handler
consumes those two documents and self-configures with **zero bespoke code**, so signature checking, key
rollover, issuer and audience validation and clock-skew handling all stay inside framework code. Choosing
gRPC there would have forced hand-written key-set retrieval into three services — a net *increase* in
hand-written security code, which is the opposite of what the requirement asks for.

### 5.3 One line on the conflict response

On an optimistic-concurrency mismatch — an `updatewhereclause` conflict — Persistence and DataServices
return gRPC `StatusCode.Aborted` and Gateway's REST projection surfaces it as **HTTP `409`** carrying a
structured conflict detail with the current row state; callers implement an explicit retry-or-surface policy
and **there is no silent overwrite anywhere in the system**.
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §4.2 owns the mapping and
[`../docs/CONTRACTS.md`](../docs/CONTRACTS.md) owns the payload; neither is forked here.

---

## 6. Teardown, restart policy and scaling

### 6.1 Stopping the stack

```bash
# Stop and remove the containers and the network. NAMED VOLUMES SURVIVE. This is the default teardown.
docker compose -f orchestration/docker-compose.yml down
```

> ### ⚠️ `down -v` destroys the `persistence-db` volume
>
> `docker compose down -v` removes named volumes, and so does `docker volume rm`. Either one discards the
> provisioned schema and **every row in it**. The schema itself comes back on the next start, because
> Persistence provisions it ([§3.4](#34-step-4--provision-the-persistence-database-not-required-on-the-default-bring-up)) — the
> rows do not, and that is the part that matters: **it invalidates any paired characterization capture taken
> against that volume state.** Between the two halves of a paired capture, tear the stack down with a plain
> `down` and never with `-v`. The rule and its reason are in
> [§7.2](#72-decision-2--the-persistence-db-volume-rename).

### 6.2 There is no restart policy, deliberately

The manifest sets `restart: "no"` on all four services. It is stated explicitly rather than left to the
default so that the choice is visible and a later reader has to overrule a decision instead of filling a
blank.

`restart: always` or `unless-stopped` would convert a **structural fault** — an unresolvable certificate, an
unimportable signing key, a missing roster secret — into an invisible crash loop that `docker compose ps`
reads as *restarting* rather than as *misconfigured*. That would be graceful degradation dressed as
robustness, and it is the one thing this migration is required not to do.

The posture has a precise legacy ancestor. The legacy application's `systemerror` event decodes an assertion
failure by unpacking a seven-field payload split on `~r~n` and then **terminates the application** with
`HALT CLOSE` [`ws_objects/pfw.pbl.src/pfw.sra:L111-L144`, reaching `HALT CLOSE` at `:L143`]. The .NET
equivalent is fail-fast startup validation and process termination on a structural fault — **never graceful
degradation** — and a container that has stopped is how that failure stays visible. The same posture is why a
missing required variable aborts bring-up by name in [§3.3](#33-step-3--start-the-stack) rather than
producing four containers that build, start and then crash-loop for a reason only their logs carry.

### 6.3 Scaling, replicas and container names

**No `container_name:` is set on any service, deliberately.** A fixed container name is a hard blocker on
`docker compose up --scale <service>=N`, and Compose's generated names (`<project>-<service>-<n>`) also keep
two projects on one host from colliding. Please do not helpfully add them.

**But the omission does not, on its own, make `--scale` work — and an earlier revision of this section said it
did.** `--scale <service>=N` for N>1 fails against the manifest as written, for any of the four services,
because each publishes a **fixed host port** and a host port can be bound once. The second replica is created
and then fails at start:

```text
Error response from daemon: failed to set up container networking: driver failed programming external
connectivity on endpoint <project>-dataservices-service-2 (…): Bind for 0.0.0.0:5102 failed: port is
already allocated
```

That is measured on this manifest rather than inferred, and it is the worse of the two possible failure
shapes: it arrives **after** the image build and after the other services have re-satisfied their health
gates, so an operator is left with a partially mutated stack and an error naming a port rather than the claim
that misled them.

**The fixed publishes are not the thing to remove.** The 5101–5105 band and the per-service `/health`
addresses are the attached environment's own readiness gates, and they are what the port map of
[§5.1](#51-the-map), the end-to-end fixture and the documentation all agree on. **So this manifest
describes a one-container-per-service topology, and that is what to rely on when reading it.**

#### 6.3.1 Running more than one replica of a service

Independent scalability is a property of the **architecture** — four images with no in-process dependency
between them, so instance counts can differ per service — and not a property this manifest exercises. To
exercise it, take the replicated service's host publish away and scale it, in an override file so the
manifest keeps its documented publishes:

```yaml
# scale.override.yml
services:
  dataservices-service:
    ports: !reset []
```

```bash
docker compose -f orchestration/docker-compose.yml -f scale.override.yml   up -d --scale dataservices-service=3
```

**Nothing else is required, and nothing else may be added.** The compose network carries an embedded DNS
server, so `dataservices-service` resolves to every healthy replica; both replicas start, pass their own
health probes and are reachable under that name, and Gateway continues to report `Healthy` over the
replicated upstream. All of that was measured on this manifest. No reverse proxy, no service mesh and no
sidecar is introduced for it. The trade is explicit: a replicated service stops being reachable from the
**host**, which is what its `ports:` block was for, so do not scale the service you are probing from the host
at the same time.

**What replication buys here is availability, not request spreading — and the measurement is what settles
it.** Every gRPC client in this system is configured with a plain `https://<service-name>:<port>` address and
a retry service config, and **no load-balancing policy**. A channel built that way resolves the name once,
opens one connection and multiplexes every call over it, so one replica takes all of that caller's traffic
for the life of the connection. With two healthy replicas up, one served every logged call and the other
served none. So a second replica is something to fail over to across a restart or a fault; it is not a way to
divide load. Dividing load would need the client to opt in — gRPC's `dns:///` resolver plus a `round_robin`
policy — which this refactor does not configure, and §6.3.2 is why that is not a free win.

#### 6.3.2 The one thing a second replica requires of the caller — sticky routing on the handle

Four operations in this stack hand back an **opaque handle** and expect it on every subsequent call, and the
state behind that handle lives in the process that issued it. Two replicas do not share it, deliberately:
sharing would mean a distributed session store this refactor has no evidence for and no requirement to
build. So a follow-up call carrying a handle **must reach the replica that issued it**, and the affinity key
is a named contract field rather than something to infer:

| Service | Handle | Affinity key — the field it travels in | Issued by |
| --- | --- | --- | --- |
| Persistence | Transaction session | `persistence.v1.SessionHandle.session_id` | `TransactionService.BeginSession` |
| Persistence | Query task | `persistence.v1.TaskHandle.task_id` | `QueryService` |
| Persistence | Update task | `persistence.v1.TaskHandle.task_id` | `UpdateService` |
| Persistence | Command task | `persistence.v1.TaskHandle.task_id` | `CommandService` |
| DataServices | Validation session | `dataservices.v1` `session_id` | `OpenValidationSession` |
| DataServices | Expression session | `dataservices.v1` `datawindow_handle` | the column-expression surface |

**A mis-routed handle is already refused rather than silently honoured, which is what makes this an
operational contract and not a correctness risk.** A replica that never issued a handle does not recognise
it: Persistence answers `RetCode.E_INVALID_HANDLE` (−11), which its status mapping projects as gRPC
`FailedPrecondition`, and DataServices answers the same way for a session or DataWindow handle it does not
hold; Gateway surfaces that as an HTTP 400 naming the upstream. So the worst outcome of scaling without
affinity is a refusal an operator can read — never a partial write and never a silently different result set.

**Connection pinning is why this usually works, and why it is still not safe to assume.** Because a channel
pins to one replica for the life of its connection, a caller's handles stay valid as long as that connection
lives — which is most of the time, and is what makes the hazard easy to miss. They stop being recognised at a
**reconnect**: a replica restart, a rolling update, an idle-connection close, a network blip — and
immediately, for every caller, if a `round_robin` policy is ever enabled. Two consequences follow, and both
are operational rather than code changes: **drain a replica before removing it** rather than killing it under
load, and **treat `E_INVALID_HANDLE` after a reconnect as expected**, to be answered by starting the
operation again rather than by retrying the same handle.

**What is deliberately not done about it:** no instance discriminator is embedded in a handle value. Handles
are unguessable by construction — 32 hexadecimal characters from a cryptographic source, derived from no
caller-supplied data — and encoding the serving instance into one would weaken that property and disclose
internal topology to every caller. Stickiness in a real load balancer is keyed on a header or a consistent
hash **over** the handle value; it never requires parsing it.

**But an absent container name is necessary and not sufficient, and an earlier revision of this section
overstated what it bought.** A service with a **published host port cannot exceed one replica whatever it is
named**, because the second replica would have to bind a host port the first already holds and Compose
refuses. That is a property of publishing, not of naming, and conflating the two produced a claim this stack
does not satisfy.

So the two cases separate cleanly:

**Scaling one service past one replica** means not publishing it. Deleting a `ports:` entry costs nothing a
caller needs — in-network callers resolve the Compose service name and Compose load-balances across its
replicas, so no published port is involved in any internal call. The three diagnostic publications (5101,
5102, 5104) exist only so the attached environment's per-service `/health` gate can be exercised from the
host, and Gateway's is the documented composition-root URL, which is scaled behind a proxy rather than by
publishing a range. Nothing in this phase needs more than one replica of anything; what *independently
scalable* requires is that instance counts be able to vary per service, which they can, because no service
holds an in-process dependency on any other.

**Running a second full stack on one host** — which parallel clones need — is a matter of setting the project
name **and the four host ports**, not of editing the manifest:

```bash
# A second, independent stack. -p renames the project, and therefore the network, the container names and
# the volume: pfw-2_persistence-db. The four *_HOST_PORT variables are what keep the published ports from
# colliding with the first stack's; without them this recipe fails on all four, which is what it used to do.
GATEWAY_HOST_PORT=6105 \
SECURITY_HOST_PORT=6104 \
PERSISTENCE_HOST_PORT=6101 \
DATASERVICES_HOST_PORT=6102 \
docker compose -p pfw-2 -f orchestration/docker-compose.yml \
  --env-file "$HOME/.config/powerframework/pfw.env" up --build -d
```

Each variable moves only the **host** side of its mapping. The container side is fixed by that service's own
`Kestrel:Endpoints` and is deliberately not overridable here: a published port whose container half is wrong
forwards to nothing. There is no separate internal gRPC port needing a fifth variable: each service binds
ONE listener carrying both protocol versions, so these four cover every port the stack binds.

Note the consequence for characterization work before you do this: **both halves of a paired capture belong
to one clone**, against one unrecreated volume. Where several clones run concurrently, each needs its own
Compose project and its own volume so that one clone's teardown cannot invalidate another clone's
half-finished pair.

---

## 7. The four documented decisions

Four decisions were taken in this directory that a reader could reasonably have expected to go the other way.
Each is recorded **with its reason**, because a decision recorded without its reason is a decision the next
reader will reverse.

### 7.1 Decision 1 — .NET Aspire was evaluated and rejected

Aspire's Docker Compose publishing integration generates a compose file and an `.env` from an AppHost model,
so on the face of it it could have replaced the manifest beside this file. It was **rejected**, on two
findings, the second of which is decisive:

1. **The Dockerfile-builder APIs remain experimental** and require suppressing an experimental-API
   diagnostic to use at all. This repository treats warnings as errors on every project and admits no
   floating or preview dependency, so adopting them would have meant carrying a standing suppression in the
   build in order to generate a file that is a few hundred lines of YAML.
2. **`depends_on` with a `service_healthy` condition was not expressible through the generated model.** That
   is not a cosmetic gap. The condition is the single most important readiness property in this
   orchestration — it is what makes Gateway start only once Persistence, DataServices and Security are
   actually serving, and it is the specific requirement the brief and the attached environment both name. A
   generated manifest that could only emit a plain `depends_on` would wait for container **start** and not
   for **readiness**, which is precisely the failure [§4.2](#42-the-gate-has-two-halves-and-both-are-required)
   is arranged to prevent.

So the manifest is hand-authored, it is the only built path, and **no `Aspire.Hosting.AppHost` package is
referenced anywhere in this repository.** Recording the rejected alternative and its reason — rather than
quietly omitting it — is what stops a later reader "modernising" the manifest back into a tool that cannot
express its central property.

### 7.2 Decision 2 — the `persistence-db` volume rename

The attached environment names the persistence volume after a `data-service`, one of the five directories in
its own placeholder roster. That roster is superseded — the environment's own STEP 0 labels those five
directories placeholders lifted verbatim from a *not prescriptive* example grouping — and the service that
actually owns storage in this phase is `persistence-service`. So the volume is renamed to match its owner,
because **a volume named after a service this phase never builds is a standing invitation to mount it on the
wrong thing.**

**The environment's paired-capture persistence rule survives the rename intact.** It is reproduced below word
for word, with `data-service-db` replaced by `persistence-db` in both places it occurs and **no other edit** —
not a shortening, not a re-ordering, not a summary. Renaming a volume in an instruction and paraphrasing the
instruction at the same time is exactly how an operational rule quietly loses its force:

> **Persistence rule: legacy characterization recordings (PowerBuilder behavioral oracle output, keyed per
> workflow ID) and this scaffold's target-side recordings must be captured against the SAME `persistence-db`
> Docker volume state for a given workflow ID comparison to be valid. Do not recreate or reseed
> `persistence-db` between the legacy-side capture and the .NET-side capture for the same workflow ID, or the
> paired recordings required by the Agent Action Plan's success criteria will not be comparable.**

In operational terms, for one workflow identifier: capture the legacy side, capture the target side, and run
nothing between them that destroys, recreates, re-initializes or re-seeds the volume. If the volume state has
moved, the pair is void — discard **both** captures and start the workflow again. Do not repair a broken pair
by re-taking one half of it; a pair re-taken in halves is two recordings of two different worlds.

**Why this matters beyond compliance.** It would be easy to file the rule as environment compliance and move
on, and that reading understates it. The golden-master characterization technique the parity model is built
on has exactly one hard prerequisite — **repeatability**, with non-deterministic values masked from **both**
the master and the candidate. The input state of a workflow that touches storage *is* part of its input, so a
volume reseeded between the two captures changes that input, the two recordings answer two different
questions, and the diff between them is noise wearing the costume of a finding. **The rule would have had to
be invented if the environment had not supplied it.**

[`../docs/PARITY.md`](../docs/PARITY.md) §4.2 is the canonical text, and
[`../characterization/README.md`](../characterization/README.md) carries the identical wording at the point
of capture. The duplication across three places is deliberate and mandated rather than an oversight
to consolidate: an operator capturing a recording is working inside `characterization/`, and a rule that
lives only in a documentation folder they have no reason to open is a rule that will be broken by someone
acting in good faith.

### 7.3 Decision 3 — 5103 is reserved, and a comment is not a stub

The attached environment assigns port 5103 to a `design-service`. DesignSystem is precisely one of the four
deferred services this phase must not implement — *not partially, and not even to stub them out* — so that
assignment is **not carried out**, and the port is **reserved rather than reused**. In the manifest it appears
as a commented block sitting between the 5102 service and the 5105 service, so the file reads in port order at
exactly the point a reader would look for it.

Leaving it commented is literally the obvious Phase-2 slot a manifest should provide. Reassigning the port to
one of the four services that do exist would erase that signal, and a future reader would have no way to tell
that a service had been planned there.

**Stated plainly so it is auditable rather than argued: a commented port slot is not a stub, and neither is a
route declaration.** For DesignSystem, Documents, Integration and ScriptBridge there is, in this directory and
in the repository as a whole:

- no service directory and no project file,
- no container definition,
- no test project,
- no service key, `image:`, `build:`, `ports:`, `healthcheck:`, `environment:`, volume or network entry in the
  manifest,
- no signing, verification or mutual-TLS variable in the environment roster,
- no partial implementation, and
- no exception-throwing placeholder class.

The four deferred services surface in the running system in exactly one way: as four reserved routes on
Gateway — `/v1/design/**`, `/v1/documents/**`, `/v1/integration/**` and `/v1/scripting/**` — each answering
**`501 Not Implemented`** with a machine-readable body naming the deferred service it will eventually reach and
the marker *reserved for Phase 2*. Those routes are metadata inside Gateway's own contract, describing the
shape of the eventual system; they are not something this manifest starts, and a declaration in a route table
implements nothing. The capabilities each route will eventually reach are enumerated in
[`../docs/DEFERRED.md`](../docs/DEFERRED.md) and are not repeated here.

When DesignSystem is genuinely built it acquires its own container definition, its own service definition and
its own health condition on the chain of [§4.1](#41-the-chain-and-the-dependency-reason-for-each-link), and
`/v1/design/**` stops answering `501`. **None of that is prepared for here.**

### 7.4 Decision 4 — SQLite only, and the encrypted path is out of scope

**SQLite is the only database provisioned, and that is a discovery outcome rather than a preference.** There
is no `mssql`, `oracle`, `postgres`, `mysql` or `redis` service in the manifest and there must not be one.

The evidence is narrow and decisive. The only DDL anywhere in the repository creates the `COMPANY` table
[`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469`], and it is SQLite; the connection URI grammar is
documented in the comments immediately above it [`:L452-L455`], with the database opened at `:L456` — `mode=rwc`,
an optional password element, `check[=quick]`, and `journal[=DELETE|TRUNCATE|PERSIST|MEMORY|WAL|OFF]`
defaulting to `DELETE`. Meanwhile the legacy transaction object declares exactly **two** database types —
`DBT_MSSQL = 0` [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L60`] and `DBT_ORACLE = 1`
[`:L61`] — and **SQLite is not in that enumeration at all**, while neither SQL Server nor Oracle has a schema,
a connection string or a line of DDL anywhere in the repository.

**So the two DBMS behaviours are preserved without provisioning either.** They live inside Persistence as
paging rewriters that are **pure string transforms** — an input statement plus a page size and index yields an
output statement — and are unit-tested with **no instance of either kind**, byte-for-byte against the
statements the legacy emits. Standing up a container for a database that has no schema in the repository would
fabricate one, which this refactor forbids outright.

**The encrypted-SQLite path is out of Phase-1 scope, and that is recorded as a known limitation rather than
silently attempted.** The `[,password]` element of the URI grammar is deliberately left unpopulated and no
password variable appears in the roster in any spelling. The reason: the shipped cipher library is a
materially older build than the plain one, and its key-derivation and per-page integrity options are not
reachable through any framework API, so the older page format cannot be reproduced and page-format parity
cannot be claimed. Supplying a passphrase would imply a parity this phase does not have.
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §8.3 carries the full finding.

**Nothing resembling a database credential appears in the manifest or the roster.** The legacy transaction
descriptor carries `logid` and `logpass` among its fields, and `logpass` is write-only by construction —
never echoed in a response and never logged. There is no `logpass` here, no `logid`, and no credentialed
connection string, because SQLite needs none and inventing one would be exactly the fabrication this decision
avoids.

---

## 8. Token topology and secrets handling

### 8.1 One issuer, three verifiers, one signing secret

**Exactly one signing secret exists in the whole system: `SECURITY_JWT_SIGNING_KEY`, and only
`security-service` receives it.** Security mints short-lived service tokens on request and publishes the
verification material the rest of the estate trusts, at `/.well-known/jwks.json` with OIDC discovery beside
it.

**Gateway, DataServices and Persistence hold verification material only.** None of them can mint a token.
Each is given Security's **address**, and its stock bearer handler fetches the discovery document, follows
the `jwks_uri` it publishes and validates from there — which keeps signature checking, key rollover, issuer
and audience validation and clock-skew handling inside framework code rather than in anything hand-written.
There is deliberately **no JWKS variable anywhere in the roster**: adding one would be dead configuration,
because standard discovery is what locates the key set.

You will not find a per-service signing key in the roster under any spelling. Any per-service key name, if
retained at all in an operator's own environment, is **verification-side** and is **not an independent
signing authority** — the security properties of a sole-issuer topology depend on there being exactly one
issuer, and giving a second service the ability to mint would end them.

**Persistence holds no caller credential and needs none.** It requests no token at all: it reads Security's
key set anonymously, because verification material is public by design. That is why the issuance secrets
appear on Security, Gateway and DataServices and not on Persistence.

### 8.2 Caller credentials are not signing keys, and the distinction is what keeps the topology intact

`POST /v1/tokens` is the one operation a bearer token cannot protect, because **a caller cannot present a
token in order to obtain its first token**. Its callers therefore authenticate with a shared secret sent as
an HTTP `Basic` credential — `SECURITY_CLIENT_SECRET_GATEWAY` and `SECURITY_CLIENT_SECRET_DATASERVICES`,
which the documented bring-up supplies — or with a client certificate where a deployment prefers one.

Holding one of those secrets lets a service **ask** for a token; it does not let it **mint** one. So none of
them makes its holder a second issuer, and rotating one invalidates nothing already in flight — a roster
secret authenticates the *request for* a token and is not the material any token is signed with. Rotating the
signing key is the opposite: it invalidates every token in flight.

Security resolves every secret its roster names at startup and reports the roster **position** of any that
resolves to nothing, so a missing one refuses the host rather than surfacing later as an unexplained `401`.

### 8.3 Mutual TLS is a documented fallback, not scaffolding

Mutual TLS is the documented alternative for any pair where a shared secret is inappropriate, and it is
configured **for that pair only** — a trust anchor on Security's issuance edge, and a certificate and key
path on each calling service. The roster carries those variables and leaves them **empty by default**,
because the documented bring-up authenticates with the shared secrets.

The distinction that matters here is between *supported* and *scaffolded*. Mutual TLS is supported: setting
those paths requires no code change anywhere, each pair is enforced as both-or-neither (half-configured is a
refusal to start with a names-only message, entirely unset is a legitimate state), and
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1 generates the certificates. **The server material
is projected rather than left to be arranged, and the caller material deliberately is not.** Three top-level
Compose secrets — sourced from `TLS_CERTIFICATE_PATH`, `TLS_CERTIFICATE_KEY_PATH` and
`INTERNAL_TLS_CA_PATH`, each with the `:?` form and therefore no default — land read-only at
`/run/secrets/internal-tls/server.crt`, `…/server.key` and `…/ca.crt` in all four services, which are the
canonical paths every service setting and every image `HEALTHCHECK` already reads; an unset or absent source
aborts bring-up by name rather than inventing one. The four `GATEWAY_MTLS_*` / `DATASERVICES_MTLS_*` paths
and `SECURITY_MTLS_CLIENT_CA_PATH` are **container** paths injected verbatim with nothing projected for them,
so a deployment adopting the fallback declares its own secret source and grant and then names the projected
path here — [`.env.example`](.env.example) carries the three-line shape under `SECURITY_MTLS_CLIENT_CA_PATH`,
and mounting the authority's **public** half only is part of it. One property of a Compose secret decides
whether any of this works: `mode:`, `uid:` and `gid:` are accepted and **ignored outside Swarm**, so a `0600`
private key owned by your host account is unreadable by the non-root runtime user (UID 1654) and Kestrel
fails exactly as if the file were absent — §3 gives the ownership the generated files must carry.

**No signing, verification or mutual-TLS variable is scaffolded for any deferred service.** The attached
environment's `design-service` and `i18n-service` key names are not provisioned at all, because neither
service exists.

### 8.4 What the environment template may and may not contain

**`.env.example` carries placeholder names and empty or clearly-fake values only, and it must never carry a
real secret.** Every value in it is one of six shapes: empty, a plain in-network address, a filesystem path,
a boolean, an integer, or a bare identifier. There is no key, no certificate, no password and no token in it
— **not even a realistic-looking example of one**, because an example key is indistinguishable from a real
one to a reader and placeholder keys have a long history of reaching production unchanged. The one signing
secret in the roster is therefore left **empty** rather than pre-filled.

The same rule governs this document: **no secret value appears anywhere in it, in any form** — not a key,
not a token, not a password, not a certificate, not a passphrase, not a credentialed connection string, and
not a "default" standing in for one.

### 8.5 The sweep result, and where locators live

The brief named three hardcoded secret sites in the legacy tree and warned that they were *a floor, not a
ceiling*. A repository-wide sweep found **eight in-source sites plus three inside vendored binaries** — more
than double the three named.

**Locators live in [`../docs/SECRETS.md`](../docs/SECRETS.md); values live nowhere.** That register carries
every locator, its severity and the action it requires, and reproduces no value. Nothing from it is
referenced or reproduced here, and no .NET file this refactor produced contains any of those values in any
form.

**Every one of the eight in-source sites lies inside the read-only region** — the test, demo and legacy
browser-asset trees. Combined with the read-only rule, that makes the remediation posture **never replicate,
document, and rotate — never "edit the legacy file."** Deleting a literal from a working tree removes the
evidence while leaving the credential valid in every clone and every fetch that has ever occurred; rotation
at the owning system is the only thing that actually fixes it.

### 8.6 Two follow-ups code generation cannot discharge

These two are operational. They are named here because they are actions an operator has to take, and no
amount of generated code can take them:

1. **The live broker credential triple must be treated as compromised and rotated by its owner.** One site
   carries a broker host address, an account name whose spelling indicates administrative intent, and that
   account's cleartext password — a production-shaped credential triple committed to version control. Whether
   the host still resolves and the credential still authenticates cannot be determined from the repository,
   so it is treated as compromised **until its owner confirms otherwise**. Rotation happens at the broker.
2. **The shared configuration-encryption key must be rotated *with* a migration.** One site carries a
   hardcoded symmetric key under which an application-settings object encrypts and decrypts values. Because
   there is no re-encryption path, rotation **alone** would orphan every value already encrypted under the
   old key — so the action is rotation **plus** a migration that decrypts existing values with the old key
   and re-encrypts them with the new one.

[`../docs/SECRETS.md`](../docs/SECRETS.md) §3.6 is the authority for both, and neither is described here with
any value attached.

---

## 9. Five deviations from the environment's instructions

The attached environment's setup instructions are treated as binding operational constraints. Five points
genuinely conflict with the brief, and each is resolved here rather than absorbed silently. **Everything else
in the instructions is honoured without deviation** — the per-service build-command shape, the compose
bring-up shape with its paired example environment file, the anonymous `/health` and authenticated `/v1/ping`
contract with its `401` absent a token, the *Gateway healthy only after its upstreams* gate, the 5101–5105
band with Gateway on 5105, the end-to-end test directory and its documented install-and-run path, and the
read-only status of the legacy tree as the behavioural oracle.

### 9.1 The service roster

**The conflict.** The instructions describe five service directories named for data, utility, design,
localization and gateway concerns. The brief names a four-service Phase-1 roster: Gateway, DataServices,
Persistence and Security.

**The resolution — the brief is authoritative, and the instructions supply the reason themselves.** Their own
STEP 0 states that the five directories are *placeholders lifted verbatim from the Agent Action Plan's own
"not prescriptive" example grouping*. So no `design-service` is built, because DesignSystem is precisely one
of the deferred services; and the utility and localization directories are not in the eight-service target
roster at all — their capability content maps to Documents and Integration, both deferred, and to a shared
**library** rather than to a service.

### 9.2 Greenfield versus an existing scaffold

**The conflict.** The instructions read as though project files, container definitions and a compose manifest
were already present. The brief states that no build, CI or configuration artifact existed.

**The resolution — the filesystem is the arbiter, and the brief was right.** Full-depth scans of the source
branch returned zero `*.csproj`, zero `*.sln` or `*.slnx`, zero `Dockerfile`, zero `*.yml` or `*.yaml`, zero
`package.json`, and neither `services/` nor `orchestration/` existed at all. Every .NET artifact is therefore
a creation rather than an edit, and the instructions are correctly read as **forward-looking acceptance
criteria** describing the post-build target state — which is how this document treats them.

### 9.3 Token authority

**The conflict.** The instructions list five independent per-service signing secrets, each service validating
inbound tokens against its own.

**The resolution — the brief wins, and it is explicitly corrective.** Security is the **sole** issuer, exactly
one signing secret exists in the system, and the other names are verification-side only and are not
independent signing authorities. The `design-service` and `i18n-service` secrets are **not provisioned at
all**, since neither service exists. [§8](#8-token-topology-and-secrets-handling) states the resulting
topology in full.

### 9.4 Port allocation and volume naming

**The conflict.** The instructions fix a 5101–5105 band against the placeholder roster, assign 5103 to a
design service, and name the persistence volume after a data service.

**The resolution — the band and the documented access URL are preserved; the rest is re-mapped.** Gateway
keeps 5105, the band is re-mapped onto the four real services, and **5103 is left commented** as the obvious
Phase-2 slot rather than reassigned ([§7.3](#73-decision-3--5103-is-reserved-and-a-comment-is-not-a-stub)).
The gRPC contracts take **no additional port at all**: Persistence serves C-05..C-08 on 5101 and DataServices
serves C-03/C-04 on 5102, the ports AAP 0.3.2.2 assigns those contracts, using `Protocols: Http1AndHttp2` so
that TLS application-protocol negotiation separates the probe from the call. An earlier revision put those two
surfaces on 5111 and 5112, outside the band so they could not collide with it or encroach on 5103; that was
withdrawn, because placing a published contract beside its assigned port is not placing it on its assigned
port. Nothing was moved into 5103 either way. The volume is renamed to `persistence-db` to match its owning service,
and the environment's paired-capture rule is restated **verbatim** against the new name
([§7.2](#72-decision-2--the-persistence-db-volume-rename)).

One further consequence of preserving the documented probe *shape* rather than its literal text: the
instructions gate readiness on `curl -sf http://localhost:<port>/health` and supply no certificate material.
Every listener here terminates TLS, so the gate becomes `curl -sf --cacert <anchor> https://…/health` — the
same probe with its scheme and trust configuration corrected, as
[§4.4](#44-probing-from-the-host-and-why-the-trust-anchor-has-to-be-named) sets out. A probe command is a
documentation detail; a substitutable trust bootstrap is not restatable at all.

### 9.5 The blocking STEP 0 mapping gate

**The conflict.** The instructions direct that work stop if `docs/SERVICE_MAPPING.md` still reads *TBD —
Phase 0* rather than a reviewed, signed-off mapping.

**The resolution — the gate is moot in a stronger sense than compliance: the document did not exist at all.**
It has been created, and it carries the full 39-library, 544-object mapping with every object assigned to a
target service or shared library and the arithmetic reconciled so that nothing is unassigned. See
[`../docs/SERVICE_MAPPING.md`](../docs/SERVICE_MAPPING.md). No stop condition remains.

---

## 10. What has and has not been exercised

**This section is the only place in this repository that reports execution status.** Every other document —
the root readme, `.env.example`, the four container definitions, `docs/BUILD.md`, `docs/PARITY.md`,
`docs/ARCHITECTURE.md`, `characterization/README.md` and the CI workflow — defers to it rather than restating
it in its own words. That is deliberate: an execution claim restated in nine files is nine claims to keep
true, and an earlier revision of this tree carried several that contradicted each other.

### 10.1 The stack has been brought up, and here is exactly what was observed

**`docker compose up --build -d` against this manifest built all four images and brought all four services
to Docker health status `healthy`.** The readiness chain converged in the documented order —
`security-service`, then `persistence-service`, then `dataservices-service`, then `gateway-service` — and
`docker compose ps` reported all four `Up (healthy)`.

| What was checked | What was observed |
| --- | --- |
| The four ordered `/health` gates of [§4](#4-the-ordered-readiness-gates), over TLS with `--cacert` | `200` on all four. Gateway's aggregate body reported `{"status":"Healthy"}` |
| `/v1/ping` with no credential, on all four | `401` on all four |
| The token-and-ping block of [§5.2](#52-health-is-anonymous-v1ping-is-not), run **verbatim** | A token was issued by `POST /v1/tokens`, `/v1/ping` answered `200` with it and `401` without it |
| A **brand-new** `persistence-db` volume | Persistence reached `healthy` with **no operator step of any kind**, logging `Applied 1 pending migration(s) to the database before reporting ready. The step is additive and idempotent: nothing was dropped, recreated or seeded.` |
| Restarting on the same volume, and again after a plain `down` and `up` | `The database schema already carries every migration this build declares, so no schema statement was issued.` The provisioning path is a genuine no-op on a provisioned volume |
| The TLS projection | `/run/secrets/internal-tls/{ca.crt,server.crt,server.key}` present inside a container, all three readable by the unprivileged `app` account, all three mounted `ro`, and a write attempt refused with `Permission denied` |
| The gRPC contracts' port, which is now each service's only port | **No separate unpublished listener remains to probe.** C-05..C-08 answer on Persistence's 5101 and C-03/C-04 on DataServices' 5102, and `Protocols: Http1AndHttp2` held on both: the HTTP/1.1 `/health` gate above succeeded on the very port a gRPC caller negotiates HTTP/2 on. In-network TLS reachability was verified against the projected anchor with hostname verification, which is what Gateway's aggregate reporting `Healthy` required of all three upstreams |
| `ASPNETCORE_ENVIRONMENT=Development`, which this template selects | Gateway logged `Now listening on: https://[::]:5105` and answered `200` from the host both on loopback and via the host's non-loopback address |
| Security's startup on its **own shipped settings** | Started clean. The only warnings were the documented fail-closed client-certificate-anchor warning and the framework's data-protection key-ring warning |
| The generation recipe of [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1, extracted and run verbatim | Exit `0`; every `openssl verify` passed; the permission split emitted `server.key` readable and the CA, signing and caller keys `0600` |
| Teardown with a plain `down` | Containers and network removed, **and the named volume survived** — the property [§7.2](#72-decision-2--the-persistence-db-volume-rename)'s capture rule depends on |

**The build and test path has been run, and its FIGURES are not restated here.** `dotnet build
PowerFramework.slnx -c Release` and `dotnet test PowerFramework.slnx -c Release --no-build` were both
executed against this tree and both succeeded, and the per-service `dotnet test -c Release
--collect:"XPlat Code Coverage"` was run from each of the four service directories and emitted the
`coverage.cobertura.xml` the coverage gate is measured from.
[`../docs/BUILD.md`](../docs/BUILD.md) §1.3 is the single canonical record of **every measured figure** —
the warning and error counts, each test project's totals, and each service's line and branch rates — and
this section deliberately carries none of them. That division is the same one this section claims for
itself in the other direction: §1.3 owns the numbers, §10 owns whether something was run at all, and a
figure restated in both is two facts to keep true.

**What the gate's scope selection is, stated because it is a property rather than a number.** A service's
own `coverage.cobertura.xml` carries **one Cobertura package per instrumented assembly** — its own, plus
the shared libraries and generated protocol stubs that arrive by `ProjectReference` — so the gate SELECTS
the service's own package by assembly name, inline in the workflow with no settings file anywhere in this
repository, and prints the others without gating on them. The report's own top-level rate spans every
package it loaded and is therefore not the gate and must never be read as one.

### 10.2 What has *not* been exercised, and none of it is glossed

- **No characterization recording exists on either side.** `characterization/workflows/` carries fifteen
  workflow definitions and the schema they validate against, and both `recordings/` half-stores carry their
  readmes — but no capture has been taken. The legacy half needs the PowerBuilder oracle, which no Linux
  container can run, so the capture rule of
  [§7.2](#72-decision-2--the-persistence-db-volume-rename) is an obligation on the work that produces the
  first pair rather than a description of something already done.
- **`tests/e2e` has not been run against the stack.** The Playwright suite exists and its readme carries the
  install-and-run path; no run of it against a live stack is reported here.
- **The mutual-TLS arm of `POST /v1/tokens` has not been exercised end to end.** The documented bring-up
  authenticates callers with shared secrets and leaves the five `*_MTLS_*` paths empty, which is the
  supported fail-closed posture. A deployment choosing the certificate arm must project its own anchor —
  [`.env.example`](.env.example) carries the shape, and setting a *host* path there refuses startup, which is
  measured rather than predicted.
- **No gRPC RPC has been invoked.** Every listener was proven reachable at the TLS layer from its
  legitimate in-network caller, and since the split listeners were withdrawn that is the same port the
  contracts answer on; no C-03 to C-08 call has been made across a container boundary.
- **CI has not run on a hosted runner from this working tree.**
  [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml) exists and defines the four-service matrix;
  what is reported above was run on a developer host.
- **`docker compose config` is a parse and is reported as one.** It resolves the manifest cleanly and aborts
  by name on the first missing required variable, and it proves nothing about runtime behaviour on its own —
  it is listed here only because it is a distinct check from the bring-up above, not a substitute for it.

---

## 11. Troubleshooting

**Bring-up aborts naming a variable, before anything is built.** `.env` was not created, the `--env-file`
path is wrong, or the variable is empty. This is the manifest working as intended: the material variables are
marked required so an issuer never starts without a key, and the abort message names the variable and what to
do about it. Note the order you meet them in — with no environment file at all the first is
`TLS_CERTIFICATE_PATH`, not the signing key, because every listener needs a certificate.

**A health gate never turns green.** Work through these in order:

- **Is the schema provisioned?** Persistence reports not ready until the `COMPANY` table exists, which
  correctly holds DataServices and Gateway back. On a fresh volume it provisions that itself before answering
  at all, so this should no longer be the cause — check it only if the startup gate was switched off for a
  capture run, or if the volume was replaced under a running container. See
  [§3.4](#34-step-4--provision-the-persistence-database-not-required-on-the-default-bring-up) and read the service's own startup log, which
  records what it applied.
- **Is a caller credential missing?** Gateway and DataServices report `Degraded` — and therefore `503` —
  until each holds one accepted caller credential. Each names the unmet setting in a `credentials` component
  of its `/health` body.
- **Are you probing from the host without the trust anchor?** Then the failure is chain validation, not
  readiness. Use `--cacert`, and never `-k`
  ([§4.4](#44-probing-from-the-host-and-why-the-trust-anchor-has-to-be-named)).
- **Have you added a compose-level `healthcheck:`?** It overrides the image's, and the two obvious hand-written
  probes both fail permanently against a TLS listener. The runtime image ships **neither `curl` nor `wget`** —
  both were removed from the .NET runtime images from .NET 8 onward — so a probe assuming either can never
  succeed. A `/dev/tcp` redirection cannot work either: it writes plaintext into a TLS endpoint, and it
  additionally requires **`bash`**, so a `CMD-SHELL` form would not even parse it, because Debian's `/bin/sh`
  is dash and has no such facility. Remove the override and let the image's own probe run
  ([§4.3](#43-the-probes-are-declared-by-the-images-not-by-the-manifest)).

**`401` from `/v1/ping`.** Expected without a bearer token — that is the endpoint's contract on all four
services. Obtain a token from Security first ([§5.2](#52-health-is-anonymous-v1ping-is-not)).

**`400` from something you expected to answer.** You have addressed a listener that cannot serve what you
sent. `/health` and `/v1/ping` are HTTP/1.1 on 5101, 5102, 5104 and 5105, and gRPC answers over HTTP/2 on
5101 and 5102 — the same two endpoints, which declare `Http1AndHttp2`. Security's 5104 and Gateway's 5105
pin `Http1` and serve no gRPC at all, so a gRPC channel aimed at either fails with `HTTP_1_1_REQUIRED`. Note
that this only works over TLS: against a plaintext listener Kestrel disables HTTP/2 entirely, so a `http://`
address would answer the probe and refuse every gRPC call.

**A port is already in use.** The 5101–5105 band and Gateway's 5105 are fixed by the documented access
contract, so **free the port rather than remapping it**. If what you actually need is a second concurrent
stack, give it its own project name instead — `docker compose -p <name> …`, per
[§6.3](#63-scaling-replicas-and-container-names).

**Gateway is unhealthy while an upstream is too.** The aggregate gate is doing its job: Gateway reports
healthy only after all three upstreams do. Read the failing service's logs —
`docker compose -f orchestration/docker-compose.yml logs <service>` — rather than Gateway's, and check
Gateway's `/health` body, which names which upstream is responsible.

**A NuGet restore inside `docker build` fails with `NU1301`.** Read it as a network-layer problem rather than
a broken feed. The manifest pins the bridge network's MTU below the 1500 default because a host path MTU
smaller than that black-holes large TLS records instead of fragmenting or rejecting them. If your host path
MTU differs again, adjust that one `driver_opts` value; a *smaller* MTU always works and costs only marginal
per-packet efficiency.

**The database seems to have vanished after a teardown.** You almost certainly ran `down -v`. See the warning
in [§6.1](#61-stopping-the-stack) — and if a paired characterization capture was in progress, both halves of
it are void.

---

## 12. Where to read more

Nothing in this document forks another document's content; each link below is to the file that **owns** the
subject.

| Document | What it owns |
| --- | --- |
| [`docker-compose.yml`](docker-compose.yml) | **Authoritative for topology.** Every service name, port, hostname, volume, network and dependency condition, each annotated with its reason |
| [`.env.example`](.env.example) | The variable roster, the configuration key each entry maps onto, and the parity rule between the roster and the manifest |
| [`../docs/BUILD.md`](../docs/BUILD.md) | Building, testing and packaging. §5.6 is the Persistence migration; §7 is the container build; §8 is the bring-up specification; §14 is the Markdown lint policy this document follows |
| [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) | The port map, the transport chosen per service, the layered acyclic call graph, the capability gate, and §9.3.1 — the one place the local signing identity and certificate set are generated |
| [`../docs/CONTRACTS.md`](../docs/CONTRACTS.md) | The ten cross-service contracts C-01..C-10 and the four reserved Gateway extension points |
| [`../docs/SERVICE_MAPPING.md`](../docs/SERVICE_MAPPING.md) | The full 39-library, 544-object mapping, including the deferred assignments |
| [`../docs/PARITY.md`](../docs/PARITY.md) | The characterization model, the fixtures, the determinism seams, and §4.2 — the canonical text of the paired-capture rule |
| [`../docs/SECRETS.md`](../docs/SECRETS.md) | The credential register: every locator, its severity and its required action, plus the token topology and the two operational follow-ups. **No value, here or there** |
| [`../docs/DEFERRED.md`](../docs/DEFERRED.md) | The four deferred services, their assigned objects, and the reserved routes |
| [`../characterization/README.md`](../characterization/README.md) | The paired capture store and its restatement of the capture rule, at its §2, plus [`../characterization/workflows/README.md`](../characterization/workflows/README.md) and the fifteen pairing keys it fixes. Present — **and both recording roots are still empty**, which §10.2 above states as the first thing not exercised |
| [`../tests/e2e/README.md`](../tests/e2e/README.md) | Cross-service workflow verification. Note that `tests/e2e/` is **purely additive** beside `tests/blink/`, `tests/sciter/` and `tests/webview/`, which are pre-existing read-only legacy browser assets — `tests/` is not a greenfield directory and must never be treated as one |
| [`../README.md`](../README.md) and [`../NOTICE`](../NOTICE) | The licence and the third-party attributions. **Not restated here** — the root readme holds the BSD 2-Clause text and its Chinese restatement, and `NOTICE` carries the upstream attributions |
| [`../docs/README.md`](../docs/README.md) | The read-only legacy framework documentation, including the initialize/finalize pairing this orchestration's startup ordering descends from. Never edited |
