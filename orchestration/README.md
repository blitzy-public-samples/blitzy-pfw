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
decisions taken here and the reason for each — and, in [§10](#10-what-was-not-exercised--stated-plainly),
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

Everything this document instructs a reader to *run* is present in the tree. One artifact it *names* is
not, and it is named because it is where the corresponding work belongs rather than because a reader can
open it today.

| Artifact | What it carries | State |
| --- | --- | --- |
| `characterization/`, and `characterization/README.md` inside it | The paired legacy and target recordings, and a restatement of the capture rule of [§7.2](#72-decision-2--the-persistence-db-volume-rename) | **Planned — not yet present, and therefore named rather than linked** |

Everything else referenced below — the manifest and the environment template beside this file, all four
container definitions, all four service applications, the six shared libraries, the contract definitions,
[`../.github/workflows/ci.yml`](../.github/workflows/ci.yml), the seven documents under
[`../docs/`](../docs), [`tests/e2e`](../tests/e2e) and the read-only legacy tree — **is present today**.

**What has never happened is the whole stack running.** [§10](#10-what-was-not-exercised--stated-plainly)
is the authority for that and states it without softening. Read it before treating any command below as a
transcript of a successful run; it is a specification a reader can execute, not a report of an execution.

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
10. [What was not exercised — stated plainly](#10-what-was-not-exercised--stated-plainly)
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
**independently scalable**. That holds structurally: one container per service, with no in-process
dependency on any other, so instance counts can vary per service. [§6.3](#63-scaling-and-container-names)
records the one manifest decision that keeps it expressible. No figure is attached to it, because no
baseline exists to compare one against.

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

**For the one-off database provisioning step of [§3.4](#34-step-4--provision-the-persistence-database)**
you additionally need the .NET SDK and the `dotnet-ef` tool on the host, because the runtime image
deliberately carries neither. [`../docs/BUILD.md`](../docs/BUILD.md) §4 lists the toolchain and §5.6 gives
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
install -d -m 700 "$HOME/.config/powerframework"
cp orchestration/.env.example "$HOME/.config/powerframework/pfw.env"
chmod 600 "$HOME/.config/powerframework/pfw.env"
# Then populate it using the roster in .env.example and the generation commands in step 2.
```

**The in-tree form the attached environment documents remains supported — with one prior step.** Add a
per-clone exclusion *before* writing any key material. `.git/info/exclude` is untracked and per-clone, so it
excludes the file without editing the read-only root ignore file:

```bash
set -euo pipefail
echo 'orchestration/.env' >> .git/info/exclude
cd orchestration
[ -f .env ] || cp .env.example .env
```

Two details in those four lines are load-bearing. The copy is **guarded** — an unguarded `cp` silently
overwrites a populated `.env` with the template, destroying the key and leaving a stack that starts and then
rejects every token, a failure whose cause is nowhere near its symptom. And `cd` is on its **own line**
under `set -euo pipefail`: in a `cd x && test || cp` list a failing `cd` does not abort the shell, and the
copy then lands in whatever directory you were actually standing in.

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
| `TLS_CERTIFICATE_PATH`, `TLS_CERTIFICATE_KEY_PATH` | Paths | The shared multi-SAN server certificate and its key. Every listener in this stack terminates TLS, so these are required in effect |
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

# One shared secret per caller. Run it once per variable, and use a DIFFERENT value for each.
openssl rand -base64 32
```

**The certificate set is one command block, and it is not duplicated here.**
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1 is the single place it lives: the local
authority, the shared server certificate **with its subject alternative names**, and the two optional caller
certificates. Read that section rather than improvising, because one property of it is not obvious and
breaks everything quietly when missed — one certificate is presented under several names
(`security-service`, `persistence-service`, `dataservices-service` and `localhost`, plus the loopback
addresses), current TLS stacks ignore the common name and read `subjectAltName` only, and a certificate
carrying a common name alone therefore matches **nothing at all**, including the name it appears to carry.

### 3.3 Step 3 — start the stack

One command brings all four services up together. Both forms below are equivalent, and both auto-load
`orchestration/.env` when no `--env-file` is given, because Compose reads `.env` from the **project
directory** and the project directory is the directory holding the manifest:

```bash
# From the repository root.
docker compose -f orchestration/docker-compose.yml up --build -d

# Equivalent, and the shape the attached environment documents.
cd orchestration && docker compose up --build -d

# With the environment file kept outside the working tree, as step 1 recommends.
cd orchestration && docker compose --env-file "$HOME/.config/powerframework/pfw.env" up --build -d
```

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
error while interpolating services.security-service.environment.Kestrel__Certificates__Default__Path:
required variable TLS_CERTIFICATE_PATH is missing a value: TLS_CERTIFICATE_PATH must be set in
orchestration/.env - every listener in this stack is https and Kestrel refuses to start without a
certificate. Generate one with docs/ARCHITECTURE.md section 9.3.1 and point this at the PEM chain.
```

**To check the manifest without starting anything**, ask Compose to resolve it. This validates YAML,
interpolation and the resolved model, and it is *only* a static check — it starts no container and proves
nothing about runtime behaviour:

```bash
docker compose -f orchestration/docker-compose.yml config -q       # exit 0 = resolves cleanly
docker compose -f orchestration/docker-compose.yml config          # print the resolved model
```

### 3.4 Step 4 — provision the Persistence database

**Once per volume, and the stack is not ready until it is done.** Persistence is the only service with
storage and it **does not create its own schema** — there is no `EnsureCreated` and no `Migrate` anywhere in
it. Its `/health` reports not ready until the `COMPANY` table exists, and against a brand-new
`persistence-db` volume the container starts, binds both its listeners and answers, while its storage check
reports the database file may not exist yet. The dependency chain then correctly holds DataServices and
Gateway back.

**No init container does this, deliberately.** The runtime image carries neither the SDK nor the `dotnet-ef`
tool, so a migration container would be a *fifth* service built from a different base image — and this
manifest is exactly four services by requirement. Provisioning is a deployment step.

The migration command itself belongs to [`../docs/BUILD.md`](../docs/BUILD.md) §5.6 and is not restated
here; what is specific to this directory is **where the file has to land**. On the default Compose topology
the configured data directory is `/var/lib/powerframework` *inside the named volume*, so the schema is
generated on the host and then placed on the volume with the ownership the unprivileged runtime account
needs:

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
  mcr.microsoft.com/dotnet/aspnet:10.0 \
  sh -c 'cp /provision/test.db /var/lib/powerframework/test.db \
      && chown --reference=/var/lib/powerframework /var/lib/powerframework/test.db'
```

Four notes on that second command, and the first is a precondition rather than an explanation.

- **Run it after step 3, not before.** Docker seeds a new named volume from the image directory it is first
  mounted at, ownership included, and the Persistence image creates `/var/lib/powerframework` and chowns it
  to its unprivileged account. Mount a *brand-new* volume with the plain base image instead and the
  directory is created **root-owned**, at which point `--reference` faithfully copies the wrong owner. Let
  the Persistence container seed the volume first; then this command inherits the right answer.
- **It uses the same runtime base image the Persistence stage is built from**, so it adds no image the stack
  does not already need, and no package is installed into anything.
- **It runs as that image's default root user rather than as the application account**, which is what makes
  the `chown` possible. Ownership is the whole point: a file left owned by root is unwritable by the
  service, and SQLite needs write access to the file *and* to the directory, because it creates and removes
  a rollback journal beside it.
- **The file name is `test.db`** — the legacy name, retained deliberately because it is the name the
  behavioural oracle opens [`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456`]. It is configured, not
  hardcoded, in Persistence's own settings alongside the rest of the URI grammar the legacy documents:
  `mode=rwc`, no integrity check, and journal mode `DELETE`, which is the legacy default among the six that
  grammar admits [`:L452-L455`].

**The migration is idempotent and non-destructive**, which is what makes it safe to re-run and — importantly
— safe *between* the two halves of a paired characterization capture. Re-running reports that the database
is already up to date and leaves existing rows byte-identical.

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

**Every gate addresses the HTTP/1.1 listener** — 5101, 5102, 5104 and 5105. The gRPC surfaces on **5111**
and **5112** are deliberately unprobed: each pins HTTP/2 only and would answer an HTTP/1.1 `GET` with
`400`, and a host binds all of its endpoints or refuses to start, so the REST port answering already proves
the process is serving.

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
certificate against the mounted trust anchor, requests the anonymous `GET /health` on its own HTTP/1.1 port
and matches `' 200 '` in the status line. **None of them passes `-k`, `--insecure` or `-noverify`**, because
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

curl -sf --cacert "$CA" https://localhost:5104/health   # 1. Security
curl -sf --cacert "$CA" https://localhost:5101/health   # 2. Persistence
curl -sf --cacert "$CA" https://localhost:5102/health   # 3. DataServices
curl -sf --cacert "$CA" https://localhost:5105/health   # 4. Gateway ingress, and the aggregate of 1-3
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
| `persistence-service` | `https://localhost:5101` | `Http1` | Anonymous `/health`, authenticated `/v1/ping` — **the documented readiness address** | verification only |
| `persistence-service` | `https://localhost:5111` | `Http2` | gRPC contracts C-05..C-08. Its only caller is DataServices, inside the network | verification only |
| `dataservices-service` | `https://localhost:5102` | `Http1` | Anonymous `/health`, authenticated `/v1/ping`, and the thin REST projection consumed only by Gateway | verification only |
| `dataservices-service` | `https://localhost:5112` | `Http2` | gRPC contracts C-03 and C-04. Its only caller is Gateway | verification only |
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

To exercise it, ask Security for a token and then present it. Security is the only service that mints:

```bash
set -euo pipefail
CA="$HOME/.config/powerframework/secrets/mtls-ca.crt"

# 1. Obtain a token from the sole issuer (contract C-01). The caller authenticates with its own shared
#    secret as an HTTP Basic credential -- a caller cannot present a bearer token to obtain its first one.
#    Take both values from the environment; never paste a literal onto a command line. Read the request
#    body shape from the published contract rather than guessing it:
#    shared/PowerFramework.Contracts/OpenApi/security.v1.yaml
curl -sf --cacert "$CA" https://localhost:5104/v1/tokens \
     -u "$PFW_CALLER:$PFW_CALLER_SECRET" \
     -H 'content-type: application/json' \
     --data '<request body per security.v1.yaml>'

# 2. Present the token. Without it, every one of the four /v1/ping endpoints answers 401 -- the point.
curl -sf --cacert "$CA" -H "authorization: Bearer $PFW_TOKEN" https://localhost:5105/v1/ping
```

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
> provisioned schema and every row in it, which means step 4 of
> [§3.4](#34-step-4--provision-the-persistence-database) has to be repeated — and, far more consequentially,
> **it invalidates any paired characterization capture taken against that volume state.** Between the two
> halves of a paired capture, tear the stack down with a plain `down` and never with `-v`. The rule and its
> reason are in [§7.2](#72-decision-2--the-persistence-db-volume-rename).

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

### 6.3 Scaling and container names

**No `container_name:` is set on any service, deliberately.** A fixed container name makes
`docker compose up --scale <service>=N` impossible, and independent scalability per service is a requirement
rather than a nicety — it is half of what *independently deployable and independently scalable* means.
Compose's generated names (`<project>-<service>-<n>`) are what make more than one replica expressible, and
they also keep two projects on one host from colliding. Please do not helpfully add them.

**Running more than one stack on one host** — which parallel clones need — is a matter of setting the project
name and the host ports, not of editing the manifest:

```bash
# A second, independent stack. -p renames the project, and therefore the volume: pfw-2_persistence-db.
docker compose -p pfw-2 -f orchestration/docker-compose.yml up --build -d
```

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
because **a volume named after a service that does not exist is a standing invitation to mount it on the
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

[`../docs/PARITY.md`](../docs/PARITY.md) §4.2 is the canonical text, and `characterization/README.md`
restates the identical wording once that directory exists (it is not present yet, which is why it is named
here and not linked). The duplication across three places is deliberate and mandated rather than an oversight
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
[`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) §9.3.1 generates the certificates. It is **not**
scaffolded: the manifest mounts no certificate, no key and no TLS volume anywhere, and every piece of
material named above arrives **by path** from the deployment's own secret layer — the canonical default being
`/run/secrets/internal-tls/ca.crt`, which is where a Docker or Kubernetes secret lands.

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
The two gRPC listeners take 5111 and 5112, which sit **outside** the documented band precisely so they cannot
collide with it or encroach on 5103 — the environment documents no gRPC address at all, so these are new
addresses rather than reassigned ones. The volume is renamed to `persistence-db` to match its owning service,
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

## 10. What was not exercised — stated plainly

> ### ⚠️ THE BRING-UP IN THIS DOCUMENT HAS NEVER BEEN RUN
>
> **No multi-service stack has ever been started from this manifest, and its ordered health-probe readiness
> gates have never been exercised.** Docker was not installed and no daemon was available in the environment
> where this migration was planned, so the bring-up and its gates could not have been exercised there in any
> case. **No request in this system has crossed a real network boundary between two services**, and no
> aggregated `/health` has answered against three live upstreams.
>
> Everything in [§3](#3-bring-up), [§4](#4-the-ordered-readiness-gates) and [§5](#5-the-access-surface) is a
> **specification a reader can execute** — not a transcript of a successful run. Treat it as such.

**Container correctness is asserted by container-definition and compose review plus CI, and naming that
mechanism is not the same as reporting that it has run.** [`../.github/workflows/ci.yml`](../.github/workflows/ci.yml)
exists and defines the four-service matrix; what it has not done is run on a hosted runner from this working
tree.

**What has been exercised, precisely.** Each item below is scoped to exactly what it covers:

| What | Result | What it does *not* cover |
| --- | --- | --- |
| The per-service restore, release build and coverage-collecting test path — `cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"` — and the whole-solution build and test | **Zero warnings, zero errors**, and every test project passes. Figures are tied to the commands that produced them rather than quoted from elsewhere: `dotnet build PowerFramework.slnx -c Release` reports `0 Warning(s)` and `0 Error(s)`, and `dotnet test PowerFramework.slnx -c Release --no-build` reports **21,445 passing, 4 skipped, zero failing across ten test projects**. The four skips are the pinyin oracle characterization hooks, which have no paired legacy recording to read yet and are honestly skipped rather than passed. The run emits `coverage.cobertura.xml`, the exact artifact the coverage gate is measured from | An in-process host is not a network. It exercises no TLS, no ALPN negotiation, no real gRPC channel, no client-certificate handshake, no container probe and no Compose ordering |
| The per-service coverage gate, measured on one service end to end | `gateway-service` reports an **88.83%** top-level line rate on its own `coverage.cobertura.xml` when the run is scoped by [`../coverage.runsettings`](../coverage.runsettings), against a **19.55%** rate for the same tests unscoped — which is why the settings file exists rather than being a convenience, and why the floor is read **per service, on that service's own assembly** | One service is not four. [`../docs/BUILD.md`](../docs/BUILD.md) §5.5 carries the figure for all four and the reason an unscoped gate reads so much lower |
| The `security-service` and `persistence-service` images, each **built and run individually** | Both built clean; each answered `200` on the anonymous `/health` over TLS and `401` on `/v1/ping`, reached Docker health status `healthy`, and had its probe driven negative rather than merely positive. Persistence additionally ran against a fresh named volume, seeded to the unprivileged account, moving `/health` from `503` to `200` as the schema was applied — and an unwritable storage directory made the process refuse to start and terminate | Two images passing their own probes say **nothing** about the dependency chain between four of them. **The Gateway and DataServices images have not been built** |
| Static resolution of the manifest — `docker compose config` | The manifest resolves cleanly; interpolation, the four service definitions, the single network and the single volume all resolve as written, and a bare invocation aborts by name on the first missing required variable | **This is a parse, not a bring-up.** It starts no container, builds no image, opens no socket and proves nothing whatsoever about runtime behaviour |

**No paired characterization recording exists**, because no legacy oracle run exists to pair against yet. The
capture rule of [§7.2](#72-decision-2--the-persistence-db-volume-rename) is therefore an obligation on the
work that produces the first pair, not a description of something already done.

---

## 11. Troubleshooting

**Bring-up aborts naming a variable, before anything is built.** `.env` was not created, the `--env-file`
path is wrong, or the variable is empty. This is the manifest working as intended: the material variables are
marked required so an issuer never starts without a key, and the abort message names the variable and what to
do about it. Note the order you meet them in — with no environment file at all the first is
`TLS_CERTIFICATE_PATH`, not the signing key, because every listener needs a certificate.

**A health gate never turns green.** Work through these in order:

- **Is the schema provisioned?** Against a fresh volume, Persistence reports not ready until the `COMPANY`
  table exists, which correctly holds DataServices and Gateway back. See
  [§3.4](#34-step-4--provision-the-persistence-database). This is the most common cause of a chain that looks
  stuck on a first bring-up.
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

**`400` from something you expected to answer.** You have addressed the wrong listener. `/health` and
`/v1/ping` are HTTP/1.1 on 5101, 5102, 5104 and 5105; gRPC is HTTP/2 on 5111 and 5112. Each endpoint pins one
protocol version deliberately, so a gRPC channel aimed at a REST port fails with `HTTP_1_1_REQUIRED` and an
HTTP/1.1 request aimed at a gRPC port answers `400`.

**A port is already in use.** The 5101–5105 band and Gateway's 5105 are fixed by the documented access
contract, so **free the port rather than remapping it**. If what you actually need is a second concurrent
stack, give it its own project name instead — `docker compose -p <name> …`, per
[§6.3](#63-scaling-and-container-names).

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
| `characterization/README.md` | The paired capture store and its restatement of the capture rule. **Planned — not yet present, hence named rather than linked** |
| [`../tests/e2e/README.md`](../tests/e2e/README.md) | Cross-service workflow verification. Note that `tests/e2e/` is **purely additive** beside `tests/blink/`, `tests/sciter/` and `tests/webview/`, which are pre-existing read-only legacy browser assets — `tests/` is not a greenfield directory and must never be treated as one |
| [`../README.md`](../README.md) and [`../NOTICE`](../NOTICE) | The licence and the third-party attributions. **Not restated here** — the root readme holds the BSD 2-Clause text and its Chinese restatement, and `NOTICE` carries the upstream attributions |
| [`../docs/README.md`](../docs/README.md) | The read-only legacy framework documentation, including the initialize/finalize pairing this orchestration's startup ordering descends from. Never edited |
