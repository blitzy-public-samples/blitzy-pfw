<!-- Markdown lint policy for this file, matching the one the seven authored documents under docs/ carry.
     MD013 is 120 rather than the 80-character default, and is disabled for tables and code blocks: an
     evidence row carrying a locator and the finding it proves cannot be wrapped without splitting the two
     apart, and a wrapped command is a command that does not run. Prose IS wrapped and is held to 120.
     The rationale in full, including why the policy is self-declared per file rather than placed in a
     repository-root configuration artifact the plan does not provide for, and why no linter command line
     is published for it, is docs/BUILD.md section 14 - that document is the authority and this directive
     is only its application here. No Markdown linter appears in this repository's one approved, locked
     npm manifest, which is the manifest in this very directory; lint with tooling already installed. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# PowerFramework cross-service end-to-end suite

The runbook for `tests/e2e/` — the Playwright suite that drives the four Phase-1 .NET 10 services
(Gateway, DataServices, Persistence, Security) through **Gateway** and verifies the cross-service
workflows the decomposition creates. **Playwright is used here purely as an HTTP/API workflow
driver.** There is no user interface in this phase, so there is no page object, no locator, no
browser engine, no screenshot, no visual comparison and no component library or design system
anywhere in this directory — the suite issues requests and asserts on status codes and JSON. No
design input exists for this phase and none is expected: the capability area that would own a
presentation surface is deferred, and nothing in this folder renders anything.

> ⚠️ **STOP — `tests/` IS NOT A GREENFIELD DIRECTORY.**
> **Read this before running any tool, script or command that touches `tests/`.**
>
> `tests/e2e/` is **purely additive**. It sits beside three pre-existing directories that hold
> read-only legacy browser-harness assets. Those assets are part of the **behavioural oracle** the
> whole migration is measured against, and the verified inventory is:
>
> | Read-only sibling | Direct children | What they are |
> | --- | ---: | --- |
> | `tests/blink/` | 7 | MiniBlink harness pages, a vendored script, and two vendored asset directories |
> | `tests/sciter/` | **8** | Sciter harness pages, scripts, stylesheets and one image |
> | `tests/webview/` | 1 | A single WebView harness asset directory |
>
> **Inventory correction, recorded deliberately: `tests/sciter/` has EIGHT direct children, not
> seven.** Its children are `interop.htm`, `md.css`, `md.js`, `notification.css`,
> `notification.js`, `progress.gif`, `test.js` and `window.htm` — `progress.gif` is additionally
> present, and an untouched-check driven by a seven-file list would wrongly flag it as an extra.
>
> Nothing in those three directories may be **edited, reformatted, re-encoded, renamed, moved,
> deleted, linted, prettified, minified, or have a vendored dependency upgraded** — not to fix a
> lint warning, not to normalise a line ending, not to bump a transitive advisory. A reformatted
> oracle asset is a corrupted oracle, and a corrupted oracle cannot be detected later by reading
> the diff, because the diff will look like an improvement.
>
> **Nothing may ever be created at `tests/` level — above all no `tests/package.json`.** A manifest
> one level up would make the three legacy directories look like npm workspace members and expose
> read-only oracle assets to `npm`, to a formatter and to every tool that follows a manifest. This
> suite's manifest belongs here, in `tests/e2e/`, and nowhere else. The same applies to a lockfile,
> a `node_modules/`, a `tsconfig.json`, an ignore file or a CI config: scope every operation to
> `tests/e2e/` explicitly, and never run a recursive delete, a `git clean`, or a scaffolding tool
> against `tests/`.
>
> **There is an untouched proof, it is two commands long, and it is immediately below this box.**

**The untouched proof.** The three read-only siblings have a stable content fingerprint, so the claim
that they were not touched is checkable rather than asserted:

```bash
git ls-files -s tests/blink tests/sciter tests/webview | sha256sum
git status --porcelain tests/blink tests/sciter tests/webview
```

The first must print `e9de966fe49b982b30aea135c6381f0901bba498c31a181704e027066cdb84b8`, over 170
tracked paths. The second must print nothing at all. Both were run while authoring this file and both
held.

**Note the scope carefully:** that fingerprint and the 170-path count belong to **the three read-only
siblings**, not to the whole of `tests/`. A whole-`tests/` digest is not a usable baseline and no value
for one is quoted anywhere in this file, because `tests/e2e/` is additive and still growing — and
because this document is itself one of the paths such a digest would cover, so any value written here
would be wrong the moment it was committed. [§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed)
records that reasoning in full.

---

## Contents

1. [What this suite is, and what it is not](#1-what-this-suite-is-and-what-it-is-not)
2. [Prerequisites](#2-prerequisites)
3. [Install and run](#3-install-and-run)
4. [Bring-up and readiness](#4-bring-up-and-readiness)
5. [What the suite covers](#5-what-the-suite-covers)
6. [Preserved defects a spec must NOT "correct"](#6-preserved-defects-a-spec-must-not-correct)
7. [The primary fixture: `dw_sqlite.srd` and `COMPANY`](#7-the-primary-fixture-dw_sqlitesrd-and-company)
8. [Layout](#8-layout)
9. [Technology and boundary decisions](#9-technology-and-boundary-decisions)
10. [Secrets: never replicate, document, rotate](#10-secrets-never-replicate-document-rotate)
11. [Prohibitions](#11-prohibitions)
12. [Governing constraints — and the absence of user rules](#12-governing-constraints--and-the-absence-of-user-rules)
13. [Cross-references](#13-cross-references)
14. [As-verified / not verified](#14-as-verified--not-verified)

---

## 1. What this suite is, and what it is not

The legacy PowerFramework is a PowerBuilder **library**: it has no process of its own, no listener,
no route table and no serialization layer, so it has no cross-service behaviour to regress against.
Every contract this suite exercises is therefore **new** — created by the decomposition itself — and
that is exactly why an end-to-end suite exists at this layer. The unit and service tests inside each
.NET project prove that a service behaves; this suite proves that the **four services behave as one
system** across the boundaries the refactor introduced.

**It is:**

- an HTTP client suite, driving the system through Gateway as an external client would;
- a contract check on the boundaries the refactor created — readiness aggregation, authentication,
  capability projection, the reserved deferred routes, the DataWindow workflow, and the
  optimistic-concurrency conflict path;
- serialized and deterministic by construction, because it mutates shared database state. The
  serialization comes from the runner (`fullyParallel: false`, `workers: 1`), not from any spec:
  **no spec declares `mode: 'serial'`**, because none needs an ordering guarantee of its own. The
  concurrency workflow is one atomic test whose six steps pass their results along as return values,
  and the DataWindow workflow's tests each arrange their own row.

**It is not:**

- a UI test suite. No browser is launched and no browser binary is installed;
- a performance or load suite. No latency, throughput, availability or service-level figure is
  asserted anywhere, because the repository publishes none — see [§6](#6-preserved-defects-a-spec-must-not-correct);
- a coverage vehicle. The 80 % line-coverage gate is a .NET-side, per-service obligation measured
  from each service's own `coverage.cobertura.xml`, and **nothing in this npm project measures or
  reports coverage**;
- a seeder, migrator or fixture loader. It never creates, resets or reseeds a database, because
  doing so would break the paired-capture rule that the characterization comparison depends on;
- a way to reach DataServices or Persistence directly. Those are reached **through Gateway**.

---

## 2. Prerequisites

Node and npm only. **Observed on the host this document was written on:**

| Tool | Observed | Declared floor in `package.json` |
| --- | --- | --- |
| Node.js | **v22.23.2** (`node -v`) | `engines.node` `>=22.12.0` |
| npm | **11.18.0** (`npm -v`) | `packageManager` `npm@11.18.0` |

Both satisfy `@playwright/test` 1.62.1. The floor is deliberately the environment's own
`>=22.12.0` rather than Playwright's lower minimum, so an install cannot succeed on a host the
environment would reject — see [`../../docs/BUILD.md`](../../docs/BUILD.md) §4.

**Neither a .NET SDK, nor Docker, nor a database is needed to *install* this suite or to run its
stack-free gates.** Docker and a built stack are needed only for the assertions that require live
services, and that path belongs to `orchestration/` — see [§4](#4-bring-up-and-readiness).

**No browser binaries are required, and `npx playwright install` must not be run.** The suite
drives HTTP only; downloading Chromium here would add several hundred megabytes to no purpose and
would imply a presentation surface that does not exist in this phase.

**`openssl` is required only for the certificate half of the caller credential**, and not for the
stack-free gates. A full run authenticated with the HTTP `Basic` `clientCredential` — the scheme the
documented Compose bring-up uses — needs no `openssl` at all. `npm run provision:identity` uses it to
write the ephemeral client certificate the `mutualTls` scheme presents; see
[§3.2](#32-the-full-run--this-one-needs-a-running-stack-and-an-issuance-identity) and
[§4.6](#46-the-caller-credential-this-suite-presents-and-the-one-it-must-not).
It is present on the environment's Linux container and on any host with a standard TLS toolchain;
the script checks for it and fails naming it rather than part way through.

---

## 3. Install and run

Every command below is run **from inside `tests/e2e/`**. Nothing here is ever run from `tests/`.

```bash
cd tests/e2e
npm ci
```

`npm ci` installs exactly what `package-lock.json` pins and fails if the lockfile and manifest
disagree — which is the property that makes the install reproducible. Use `npm install` only when
deliberately changing a dependency, since it is the command that may *rewrite* the lockfile.

### 3.1 The stack-free gates — what can be proven with nothing running

```bash
npm run verify        # typecheck, then collect: tsc --noEmit && playwright test --list
```

or, individually:

```bash
npm run typecheck     # tsc --noEmit   — the ONLY type check in this directory
npm run test:list     # playwright test --list — loads and collects every spec
```

Two things about this pair are worth knowing before relying on either:

- **`--list` collects and loads; it does not type-check.** Playwright transpiles each file and
  strips the types without checking them, so a genuine type error can collect cleanly. That is why
  `typecheck` is a separate command and why `verify` runs both.
  [`../../docs/BUILD.md`](../../docs/BUILD.md) §9.1 records the experiment that established this.
- **Neither needs a running stack.** The fixture modules are free of import-time side effects: they
  read the environment and nothing else, start nothing, and probe nothing while loading.

### 3.2 The full run — this one needs a running stack **and an issuance identity**

> ⚠️ **This command needs a stack, and no run of it against one is reported anywhere.** The bring-up path
> exists — [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) assembles all
> four services and
> [`../../orchestration/README.md` §10](../../orchestration/README.md#10-what-has-and-has-not-been-exercised)
> is the single statement of what has and has not been exercised, which records that **this suite was not
> run against it**. So: bring the stack up first by that path, and treat §3.1's gates as the whole of what
> can be proven without one.

```bash
# Option A - the Basic clientCredential. No script, no openssl, no certificate.
export SECURITY_CLIENT_ID='pfw-e2e-suite'
read -rs -p 'SECURITY_CLIENT_SECRET: ' SECURITY_CLIENT_SECRET && export SECURITY_CLIENT_SECRET
npm test                       # playwright test

# Option B - the mutualTls certificate. Provisioned locally and thrown away.
npm run provision:identity     # once - writes an ephemeral client certificate/key under .mtls/
eval "$(npm run --silent provision:identity -- --export-only)"
npm test
```

**A caller credential is not optional, and the suite says so rather than discovering it fifteen times
over.** `POST /v1/tokens` on Security is authenticated by a **caller credential and never by a bearer
token**, because a caller cannot present a bearer token in order to obtain its first bearer token. It
accepts **either of two, and either alone satisfies it** — an HTTP `Basic` `clientCredential` from
Security's issuance roster, or a trusted client certificate (`mutualTls`).
[§4.6](#46-the-caller-credential-this-suite-presents-and-the-one-it-must-not) states that contract in
full; C-01 in [`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) is its authority. With **neither**
configured, no token is minted at any address, local or deployed, and every authenticated assertion in
the suite is unrunnable.

**Which of the two you use is a deployment choice, and Option B above provisions the certificate half.**
The `Basic` pair — `SECURITY_CLIENT_ID` and `SECURITY_CLIENT_SECRET` — needs no script at all and is
what the documented Compose bring-up uses, because the secret has to be one Security already holds for
that caller. `npm run provision:identity` exists for the other half: a certificate can be issued
locally and thrown away, which a shared secret cannot. Security declares its single TLS listener with
`ClientCertificateMode` `AllowCertificate` in its **base** settings file, so the certificate scheme is
live in Development too.

`npm run provision:identity` writes a throwaway certificate authority and a client leaf whose
subject common name is exactly the identity the suite claims (`pfw-e2e-suite`), into a **gitignored**
`.mtls/` directory, with fresh material on every run. It reports three variables — in the `0600`
`e2e-identity.env` file and on stdout as `%q`-quoted `export` lines — the two the
runner needs, and `SECURITY_MTLS_CLIENT_CA_PATH`, which is what makes the **stack** trust the leaf.
**Both halves are required** — point Security's `SECURITY_MTLS_CLIENT_CA_PATH` at that CA in
`orchestration/.env` and bring the stack up (or restart Security) so it reloads the anchor.
That one variable is sufficient on its own: Security reads it as its listener anchor and, when
`Security:ClientCertificateAuthorityPath` is unset, adopts it as the issuance anchor as well, so a
handshake that completes also establishes an identity. Presenting a certificate Security does not
trust earns a `401`, which is the transport behaving correctly rather than a defect. See
[§10](#10-secrets-never-replicate-document-rotate); no key material is ever printed, and none of it may
be committed.

**On `eval` and the `--export-only` form.** Every value the script prints is **shell-quoted before it
is printed**, so a path containing a space, a semicolon or a `$(...)` sequence round-trips as a literal
string through `eval` rather than being re-interpreted. If you would rather not `eval` at all, redirect
the three lines to a file you create with `install -m 600` and `source` it; the output is a plain
`export` triple with no side effects.

**On the output directory, because the script deletes it.** A fresh identity needs an empty directory,
so the script removes the one it is given before writing — and `E2E_IDENTITY_DIR` makes that a
caller-supplied recursive delete. The target is therefore **canonicalized first and then judged, and the
default is refusal rather than a deny-list consulted before proceeding.** To be accepted, the final path
component must be one of four disposable identity names — `.mtls`, `mtls`, `e2e-identity`,
`.e2e-identity` — and the path must not be any of: within two levels of the filesystem root; `$HOME`
itself; a well-known directory directly beneath `$HOME`; inside this repository's working tree but
outside `tests/e2e/`; a symbolic link; something that already exists and is not a directory; or a path
whose parent is not itself an existing directory. Each refusal **exits 2 naming the reason and the offending path, before
anything is removed.** Leaving `E2E_IDENTITY_DIR` unset — the documented path — writes
`tests/e2e/.mtls`, which is gitignored. Canonicalization is what makes the check meaningful rather than
cosmetic: judging the raw string would let `$repo/tests/e2e/../../..` read as a path under the suite.

**With no identity, a full run fails its setup.** Each authenticated group's first act is a
`beforeAll` precondition, so the report says once — naming the two variables and the command — that
the run was not a valid acceptance run. That is deliberate: a run that quietly omitted every
authenticated workflow and still reported green would prove nothing about the one property this
suite exists to demonstrate.

**A deliberately partial run is supported, and has to be asked for by name:**

```bash
E2E_ALLOW_MISSING_ISSUANCE_IDENTITY=1 npm test
```

The token-dependent tests then **skip with a stated reason** and everything else runs — the
readiness probes, the capability-table parity checks and the standing `401`-without-a-token
assertion. The acknowledgement is an opt-in and is never inferred from the certificate's absence,
because absence is precisely the state a misconfigured pipeline is in. Accepted values are `1` and
`true`; anything else set is reported as a configuration error rather than quietly ignored. The
whole policy lives in one module, `fixtures/token-issuance.ts`.

**Use `npm test`, not `npx playwright test`, and the difference is a supply-chain one.** The `test`
script runs the `playwright` binary that `npm ci` just installed from the lockfile at the pinned
`1.62.1`. `npx` prefers a local binary too, but when there is not one — precisely the state left by
a failed install — it will go and **acquire** a package to run instead. `npx playwright test` and
`npx playwright test --list` are correct only *after* a successful `npm ci`.

**Without a running stack, `npm test` FAILS, and that is the acceptance gate rather than an
inconvenience.** An earlier form of this suite probed Gateway's anonymous `/health` once per worker
and, finding nothing, called `test.skip` on every stack-dependent test. A skip is honest about the
individual test — but a *run* whose every HTTP assertion skipped still exits **zero**, and an exit
code is what a pipeline reads. The one state a misconfigured acceptance pipeline is in — nothing
running — was therefore the state that reported success. So:

```bash
npm test                          # FULL ACCEPTANCE RUN. An absent stack fails it.
npm run test:partial              # PARTIAL ON THE TOPOLOGY AXIS. An absent stack skips, and says so.
npm run test:partial:no-identity  # PARTIAL ON BOTH AXES. Also acknowledges having no client identity.
```

- **`npm test` is the acceptance run.** With no stack reachable it is refused in `globalSetup` **before
  a single test runs**, and the refusal names every service it probed with its URL and its fault class,
  the bring-up command, and this partial alternative. Nothing is skipped and nothing is softened.
- **`npm run test:partial` sets `E2E_ALLOW_ABSENT_STACK=1`, and that variable only.** The
  stack-dependent tests then skip with a stated reason, everything stack-free still runs, and **every
  reported line carries the project label `api-partial-no-stack`** instead of `api`. That label is the
  point: a summary line shows counts and a project name and nothing else, so `27 skipped` under `api`
  is indistinguishable from an acceptance run that happened to skip a few tests, while the same counts
  under `api-partial-no-stack` cannot be mistaken for one. **It does not on its own exit zero when
  there is no client identity** — the credential half is a separate acknowledgement, which is the next
  bullet.
- **`npm run test:partial:no-identity` sets that variable AND
  `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY=1`.** It is the scripted form of the one stack-free invocation
  that exits zero, and it exists as its own key rather than as extra variables on the one above
  precisely because the two acknowledgements are independent: an operator who means to acknowledge only
  the absent stack must be able to do that without also silencing the credential finding.
- **The acknowledgement is an opt-in and is never inferred from the stack being absent**, for the same
  reason the issuance acknowledgement above is not inferred from a missing certificate: absence is
  precisely the state a misconfigured pipeline is in. Accepted values are `1` and `true`, matched
  case-insensitively; anything else set is reported as a configuration error rather than quietly
  treated as "off".
- **The two acknowledgements are separate variables on purpose.**
  `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY` says nothing about whether a stack is running, and conflating
  the two would let one opt-in suppress two different findings.
- **Tests tagged `@no-stack` are exempt from the precondition entirely** and run in both modes — the
  capability table, the port map, the mask domains. A tag rather than a title substring, so that
  rewording a test name cannot silently change what gets skipped.

The whole policy lives in two modules and nowhere else: `fixtures/run-mode.ts` decides the mode (and
imports nothing, so both the config and the specs can read it), and `fixtures/live-stack.ts` applies
it through the single `requireLiveStack()` that all six specs call.

Run `npm run verify` when there is nothing up, and `npm test` once
[§4](#4-bring-up-and-readiness) reports ready. [§14](#14-as-verified--not-verified) records exactly
what each of these commands did when this file was written, including the failures.

### The two run modes, and why the strict one is the default

`playwright.config.ts` installs `globalSetup: './global-setup.ts'`, which probes all four services'
anonymous `/health` **before any test runs** and **refuses the run** when the topology is incomplete —
naming the offending service, classifying the fault as `unreachable`, `untrusted` or `timeout`, and
quoting the remedy for that class. A `503` is not a fault: it means the service is running and
reporting on itself, which [§5](#5-what-the-suite-covers) asserts on.

| Command | Mode | Behaviour with an absent or untrusted stack |
| --- | --- | --- |
| `npm test` | **strict** (default) | **fails**, exit 1, with the per-service diagnosis |
| `npm run test:partial` | partial on the **topology** axis | runs the `@no-stack` assertions and skips the stack-dependent ones. Exit 0 **when a client identity is present**; exit 1 when it is not, because the credential axis is a separate acknowledgement |
| `npm run test:partial:no-identity` | partial on **both** axes | as above, and the token-dependent groups decline themselves too, exit 0. The only stack-free invocation that exits zero with no identity provisioned |
| `npm run verify` | collection only | type-checks and lists; opens no socket |

**Two scripts rather than one, and the split is the design rather than a convenience.**
`npm run test:partial` sets `E2E_ALLOW_ABSENT_STACK=1` and nothing else;
`npm run test:partial:no-identity` sets that **and** `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY=1`. Those are
the two axes of "deliberately partial", one for the topology and one for the caller credential, and a
single script that set both would hand an operator who meant to acknowledge only the absent stack a
silent acknowledgement of the credential finding as well — which is exactly the conflation the
acknowledgement bullets earlier in [§3](#3-install-and-run) rule out when they say the two variables are
separate on purpose. Both accept `1` or `true`, matched case-insensitively, and a value outside that set
is reported as a mistake rather than silently treated as "off".

🔴 **This manifest once declared `test:partial` twice, and the second declaration won.** Two `scripts`
entries carried that one key: the first set `E2E_ALLOW_ABSENT_STACK=1`, the second set both variables.
JSON keeps the last occurrence of a repeated key and every parser does so silently, so the
topology-only command this section documents was **unreachable** — asking for it ran the two-variable
form instead, and an operator who wanted the credential finding reported got it suppressed. Nothing
could have caught it by reading: the two entries were two lines apart with a `test:list` between them,
and `npm run` reports no diagnostic for a duplicate. It is now two distinct keys, and
`shared/PowerFramework.Contracts.Tests/E2eManifestGuardTests` fails the build on a repeated key in this
file rather than leaving the next one to be found by its symptoms.

🔴 **Defaulting this the other way round is the tempting choice, and it matters.** If every spec begins by
probing Gateway and calling `test.skip` when the probe does not answer, a plain `npx playwright test`
exits 0 with every live assertion skipped and a green run proves nothing about the four services. Worse,
such a probe converts a **TLS** fault into the same "unreachable" as a refused connection — and since every
listener here is `https` presenting a certificate from a throwaway private authority, the single most
likely local misconfiguration skipped the entire suite while the stack was up and serving, hiding a
real deployment finding instead of reporting it.

**Trusting the certificate is a precondition, not an option.** `ignoreHTTPSErrors` stays `false`
deliberately. Node reads `NODE_EXTRA_CA_CERTS` **once, at process start**, so nothing in this suite
can install a trust anchor into a run already under way — export it first:

```bash
# docker compose bring-up: the CA the manifest projects, named on the host by INTERNAL_TLS_CA_PATH
export NODE_EXTRA_CA_CERTS="$INTERNAL_TLS_CA_PATH"

# host dotnet run: the local ASP.NET Core development certificate
dotnet dev-certs https --trust
```

Useful narrowing flags, all of which keep the runner's own configuration intact:

```bash
npx playwright test specs/02-authentication.spec.ts     # one file
npx playwright test -g "401"                            # by title substring
npx playwright test --list                              # collection only
```

Do **not** add `--workers`, `--retries`, `--fully-parallel` or a reporter that writes artifacts.
Each of those overrides a decision taken for a correctness or a secrets reason —
[§9](#9-technology-and-boundary-decisions) records which, and why.

---

## 4. Bring-up and readiness

### 4.1 There is exactly one bring-up path, and it is not this suite

> ⚠️ **THE PATH EXISTS; THIS SUITE HAS NOT BEEN RUN AGAINST IT.**
> [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) and
> [`../../orchestration/README.md`](../../orchestration/README.md) are both present, alongside all four
> service `Dockerfile`s, and the bring-up has been exercised — reported gate by gate in
> [`../../orchestration/README.md` §10](../../orchestration/README.md#10-what-has-and-has-not-been-exercised),
> the only execution-status statement in this repository, which also records that **`npm test` here has not
> been run against a live stack**. So every live-stack assertion below is *runnable* and **unreported**:
> bring the stack up by that path first. [§3](#3-install-and-run)'s install, type check and collection pass
> need no stack at all.

The four services are brought up **only** by
[`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml). This suite starts
nothing: `playwright.config.ts` deliberately carries **no `webServer` block** and no global setup or
teardown, so it points at an already-running stack and does nothing else. Two independent reasons:

- **Spawning anything from here would create a second, competing bring-up path** and would bypass
  the health-condition dependency chain described below.
- **Anything that seeded or reset the store would break the paired-capture rule**, under which a
  legacy-side and a target-side recording for one workflow are comparable only when taken against
  the same unrecreated `persistence-db` volume state.

The bring-up commands, the environment file and the readiness gates are the authority of
[`../../orchestration/README.md`](../../orchestration/README.md) and
[`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml). This document does not
restate their commands as a second copy. For the .NET-side view of the same path,
[`../../docs/BUILD.md`](../../docs/BUILD.md) §8 carries the same command and, at §8.3, points at the single
execution-status statement rather than keeping its own.

### 4.2 The readiness chain

`/health` is **anonymous on all four services** — contract C-10 declares it on every one of them —
and **Gateway reports healthy only after Persistence, DataServices and Security do.** Compose
expresses that ordering with `depends_on: condition: service_healthy`, which is a specific
capability: the requirement for it is what ruled out the .NET Aspire Compose publisher as the
orchestration path, because the generated model could not express the health condition.

The practical consequence for this suite: **a healthy Gateway is a sufficient readiness signal.**
Wait for Gateway's `/health`, then run. A Gateway that answers `503` is still *running* and still
reporting on its upstreams, which is itself a contract outcome the readiness spec is entitled to
assert on — so an unhealthy answer is a result, not a reason to retry.

### 4.3 Port map

| Service | Port | Scheme | HTTP protocol | Transport | Token role |
| --- | ---: | --- | --- | --- | --- |
| `PowerFramework.Persistence` | 5101 | `https` | `Http1AndHttp2` | REST `/health` and `/v1/ping` — **the documented readiness address** — and gRPC (C-05..C-08) on the same listener | verification only |
| `PowerFramework.DataServices` | 5102 | `https` | `Http1AndHttp2` | REST `/health`, `/v1/ping`, the thin projection consumed only by Gateway, and gRPC (C-03, C-04) on the same listener | verification only |
| *(reserved)* | 5103 | — | — | — | commented-out DesignSystem Phase-2 slot |
| `PowerFramework.Security` | 5104 | `https` | `Http1` | REST + `/.well-known/jwks.json` + OIDC discovery | **SOLE ISSUER** |
| `PowerFramework.Gateway` | **5105** | `https` | `Http1` | REST + OpenAPI | verification only |

Three notes that belong with the table rather than inside it:

- **Every one of the four ports is published to the host** by
  [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) — `5101`, `5102`,
  `5104`, `5105` — each published from a host-port variable so a second checkout on one host can move them.
  Persistence's and DataServices' single listeners carry their gRPC contracts on the same published port as
  their REST surface, and **this suite never addresses those contracts**: it goes through Gateway only,
  because a spec that called Persistence directly would test a boundary no external caller has
  ([§4.5](#45-topology--layered-acyclic-and-asserted-as-such)).

- **Scheme and protocol are separate columns, and every scheme is `https`.** All four services publish
  TLS listeners on **all four ports**, in *every* environment: each declares its Kestrel endpoint in its
  **base** settings file and the Development overlay restates rather than relaxes it. There is no
  cleartext listener anywhere in this system, so `http` never appears above. The `HTTP protocol` column
  is a separate axis — `Http1` on Security's and Gateway's listeners and `Http1AndHttp2` on Persistence's
  and DataServices', which is Kestrel's `Protocols` setting and not a scheme. Two earlier
  versions of this suite defaulted `SECURITY_BASE_URL` and then `GATEWAY_BASE_URL` to plain `http`,
  and each meant the same thing: every request the suite made addressed a listener that does not
  exist, and on the issuance edge the configured client certificate could never be presented at all,
  because a certificate only exists inside a TLS handshake.
  `02-authentication.spec.ts` now asserts that default against Security's own OpenAPI server entry
  and its Kestrel endpoint, so the two cannot drift apart again without a test failing. The listener
  configuration and the reasoning are in
  [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4; this suite reads each address from
  the environment (§4.4) rather than hardcoding a scheme.
- **5103 is reserved, not reassigned.** The attached environment had allocated it to a design
  service, and DesignSystem is precisely one of the four capability areas this phase does not build.
  Leaving the slot commented out is the honest Phase-2 placeholder; nothing listens there, and the
  suite's endpoint table deliberately has **no entry at all** for it.
- **Transport: one listener per service, and two of them carry both protocol versions.** Persistence and
  DataServices declare `Protocols: Http1AndHttp2` on their single endpoint, so TLS application-protocol
  negotiation gives this suite's `fetch` HTTP/1.1 and an internal gRPC caller HTTP/2 on the same port.
  That is why the table has four rows and not six: giving each of those two services a second
  `Http2`-only listener on 5111 and 5112 is the alternative, and it is refused because AAP 0.3.2.2
  assigns C-05..C-08 to 5101 and C-03/C-04 to 5102. **Every port in the table is reachable by `fetch`**,
  which those two extra ports would not have been — an `Http2`-only listener answers an HTTP/1.1 `GET`
  with `400`.

The endpoints this suite touches, and nothing besides these:

| Endpoint | Where | Auth |
| --- | --- | --- |
| `GET /health` | all four services | anonymous |
| `GET /v1/ping` | through Gateway | **JWT required — `401` without one** |
| `GET /v1/capabilities` | through Gateway | JWT |
| `/v1/datawindow/**` | through Gateway | JWT |
| `/v1/design/**`, `/v1/documents/**`, `/v1/integration/**`, `/v1/scripting/**` | through Gateway | reserved routes, `501` |
| `POST /v1/tokens` | Security | **caller credential required — `Basic` roster credential *or* client certificate (C-01)** |
| `GET /.well-known/jwks.json` | Security | anonymous |
| `GET /.well-known/openid-configuration` | Security | anonymous |

[`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) is the authority for what each one carries; the
table above exists only so a reader can see the whole surface this suite exercises at a glance.

### 4.4 Base URL, and the environment this suite reads

**Gateway on 5105 is the suite's sole functional base URL.** `playwright.config.ts` sets
`use.baseURL` from `GATEWAY_BASE_URL`, defaulting to `https://localhost:5105`, and validates it
structurally before the run rather than degrading: an empty, malformed, non-`https`,
credential-bearing, query-bearing or fragment-bearing value **fails the run immediately, naming the
variable at fault.** **Every base URL in the table below must be `https`** - `http` is refused, not
merely discouraged. For `GATEWAY_BASE_URL` and `SECURITY_BASE_URL` that is a secrecy control, because
those two carry a bearer token and the issuance credential behind it; for the two health-only bases it
is a correctness one, because each of those services declares a single TLS listener and a plaintext
address reaches nothing. That fail-fast posture is deliberate and mirrors the framework being migrated,
which treats a structural fault as fatal rather than as something to continue past.

Variables the suite reads. These are **names only** — no value appears in this repository, and none
may be added to it:

| Variable | Default | Purpose |
| --- | --- | --- |
| `GATEWAY_BASE_URL` | `https://localhost:5105` | The sole functional base URL |
| `SECURITY_BASE_URL` | `https://localhost:5104` | Token issuance, JWKS and OIDC discovery |
| `DATASERVICES_BASE_URL` | `https://localhost:5102` | Anonymous `/health` probe only |
| `PERSISTENCE_BASE_URL` | `https://localhost:5101` | Anonymous `/health` probe only |
| `SECURITY_MTLS_CERT_PATH` | *(unset)* | Client-certificate **path** for the issuance edge |
| `SECURITY_MTLS_KEY_PATH` | *(unset)* | Client-key **path** for the issuance edge |
| `SECURITY_MTLS_KEY_PASSPHRASE` | *(unset)* | Read only if the key needs one |
| `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY` | *(unset)* | `1` or `true` acknowledges a deliberately partial run; token-dependent tests then skip with a reason |
| `CI` | *(unset)* | When set, a stray `test.only` fails the run instead of narrowing it |

**Every default in that table names the address the repository actually binds.** That agreement is
the point of stating them: an earlier form of this table defaulted two of them to `https` while the
settings files bound `http`, so the table named listeners nobody had configured, and a reader
following it reached a refused connection rather than a service.

**Five variables, two credentials, one requirement.** `POST /v1/tokens` is the one operation a bearer
token cannot protect — a caller cannot present a token in order to obtain its first token — so
[`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) C-01 publishes two schemes for it and accepts
**either**:

- `SECURITY_CLIENT_ID` + `SECURITY_CLIENT_SECRET` — an HTTP `Basic` credential naming a subject on
  Security's issuance roster. **This is the primary path**, because it works on every topology - a
  header the operation reads itself needs no cooperation from whatever terminates TLS. The subject is
  not a free
  label: it resolves to the roster entry deciding which audiences and scopes that caller may request,
  so a request naming one the entry does not permit is refused `403` even though the caller
  authenticated. `fixtures/auth.ts` attaches the header to the issuance request **and to nothing
  else** — never through `extraHTTPHeaders`, which would send Security's issuance secret to Gateway,
  DataServices and Persistence as well.
- The three `SECURITY_MTLS_*` variables — a client certificate, which exists only inside a TLS
  handshake and so is reachable only against a deployment that terminates TLS at Security. That is
  the mutual-TLS fallback the AAP describes for that one pair. The suite reads **filesystem paths**,
  never inline material; the certificate and key live outside the repository.

A request presenting **neither** is refused with `401`, on every topology. That is a contract outcome
[`specs/02-authentication.spec.ts`](specs/02-authentication.spec.ts) asserts on directly, and it is
what keeps this newly created boundary authenticated (C-G).

Each pair is resolved all-or-nothing and **a half-configured pair fails the run immediately, naming
the variable that is missing.** Neither half set is the ordinary case and yields `undefined` — a spec
needing a token skips itself and says so, and `playwright test --list` collects with no stack running
and nothing configured. Exactly one half set is a mistake every time, and merging it into "no
credential" would send an operator to debug a `401` at Security that a typo in one variable name
caused.

**Scope is a third gate, and it is why a `403` can arrive after a token was issued.** The token
Security returns carries a `scope` claim holding exactly the space-delimited scopes the request asked
for and the roster granted — nothing is added and nothing is silently dropped, because a request for
a scope the entry does not grant is *refused* rather than narrowed. `fixtures/auth.ts` requests
`ping capabilities datawindow`, the three its Gateway requests need, and the roster subject it
authenticates as grants exactly those three, so the minted token carries all three.

A protected route then reads that claim. Each one declares the single scope it requires, and an
authenticated caller whose token does not carry it is answered **`403`, not `401`** — the two are
different facts and this suite treats them as such. `401` means no usable credential reached the
service. `403` means one did, was accepted, and does not carry this capability.

**Both services this suite talks to enforce it.** Security's `/v1/ping` requires `ping` and its
`/v1/crypto/**` group requires `security.crypto` — a scope this suite never requests, because it makes
no cryptographic call and a grant nobody needs is a permission nobody should hold. Gateway requires
`ping` on `/v1/ping`, `capabilities` on `/v1/capabilities`, and `datawindow` on the whole
`/v1/datawindow/**` projection. Those three are exactly what `fixtures/auth.ts` requests, which is why
the request list above is a requirement rather than a convenience: drop one and the corresponding spec
starts failing with `403` while every credential in play is perfectly valid.

**The four reserved `/v1/{design,documents,integration,scripting}` families are the deliberate
exception, and `04-deferred-routes.spec.ts` depends on it.** They require authentication and no scope
at all, so the default token reaches them and receives `501`. Requiring a capability scope there would
invent an entitlement for a service Phase 1 must not implement, and since the roster grants no such
scope the reserved answer would become unreachable for every caller alike.

So **no request this suite makes is expected to see a route-level `403`.** If one appears, it is a
real failure rather than a configuration wrinkle to retry around: either the token was minted with a
narrower scope set than the route wants, or the roster entry behind `SECURITY_CLIENT_ID` was widened
in one place and not the other. The fix is a corrected grant on Security's roster, never a relaxed
assertion here.

One detail in that table needs saying out loud rather than being left to be discovered at run time:
**all four defaults are `https`.** Each service declares its TLS listener in its *base* settings file,
so the scheme applies in every environment including Development: `https://+:5101`, `https://+:5102`,
`https://+:5104` and `https://+:5105`. Gateway is the published ingress and serves REST only, which
changes what its edge carries but not how it is reached.

Defaulting `SECURITY_BASE_URL` to a plain-`http` address and telling the reader to override it would mean
**the documented default run could not bootstrap** — and it breaks *both* caller-credential schemes
rather than one. `POST /v1/tokens` accepts an HTTP `Basic`
`clientCredential` or a trusted client certificate and no third thing. A certificate cannot be presented
on a listener that terminates no TLS at all, so `mutualTls` was unreachable; and a `Basic` secret is a
base64 of the shared secret in a request header, so sending it in clear on the one request whose
*response body is itself a credential* is CWE-319 introduced by a test fixture. The default now names the
listener the repository actually declares. Nothing in the suite restricts the scheme, so a deployment
that genuinely terminates TLS elsewhere still states that in one variable and changes no code.

Two consequences of the `https` defaults for a local run. The first is that you must trust whichever
certificate the listeners actually present, and that differs by bring-up. A host `dotnet run`
presents the local ASP.NET Core development certificate, so trust it once with `dotnet dev-certs
https --trust`. A `docker compose up` presents the certificate the manifest projects as a Compose
secret at `/run/secrets/internal-tls/server.crt`, issued by the throwaway private authority whose
public half is projected beside it as `ca.crt` — `dotnet dev-certs` cannot help with a different
issuer, so export `NODE_EXTRA_CA_CERTS=$INTERNAL_TLS_CA_PATH` instead, naming the same host file the
manifest's `internal-tls-ca-certificate` secret is sourced from. Node honours that variable natively
and it needs nothing from this repository. Either way `playwright.config.ts` keeps
`ignoreHTTPSErrors` **false** deliberately, because an untrusted certificate is a real finding
rather than noise. And a handshake
happening is not the same as a certificate being presented: Security is configured
`AllowCertificate` rather than `RequireCertificate`, so with no `SECURITY_MTLS_*` variables set the
handshake still completes, the anonymous `/health` probe and the key-set read still work, and only
the mutual-TLS issuance call is out of reach.

The authoritative listener configuration is in
[`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4; treat it, not this table, as the
source of truth for the address, and treat these rows as defaults rather than as claims about a
deployment.

### 4.5 Topology — layered, acyclic, and asserted as such

- **External clients reach Gateway, and only Gateway.** Nothing else calls Gateway.
- **Gateway reaches DataServices and Security.** Nothing but Gateway calls DataServices.
- **DataServices reaches Persistence and Security.** Nothing but DataServices calls Persistence.
- **Persistence reads Security's published verification material** and nothing more.

Every functional request this suite makes therefore goes to Gateway. The only permitted non-Gateway
traffic is Security's token endpoint and its published verification material — because a client must
be able to obtain a token and a consumer must be able to fetch a key set — plus the anonymous
`/health` probes that C-10 declares on all four services. **No spec talks to DataServices or
Persistence functionally**; doing so would test a boundary no external caller has and would prove
nothing about the ingress.

### 4.6 The caller credential this suite presents, and the one it must not

`POST /v1/tokens` is authenticated by a **caller credential and by no bearer token** — a caller cannot
present a bearer token in order to obtain its first bearer token — and it accepts **either of two**:

- **`clientCredential`** — a shared secret presented as an HTTP `Basic` credential whose user-id is a
  subject on Security's issuance roster, supplied through `SECURITY_CLIENT_ID` and
  `SECURITY_CLIENT_SECRET`. **This is the path the documented bring-up uses**, and it needs no certificate
  material at all.
- **`mutualTls`** — a client certificate, supplied through `SECURITY_MTLS_CERT_PATH` and
  `SECURITY_MTLS_KEY_PATH`, for a deployment that terminates TLS at Security and issues caller
  certificates.

Either satisfies the operation; a request presenting neither is refused with `401`. The suite therefore
needs one of the two, and it treats a run configuring **neither** as unprovisioned — not a run that merely
configured no certificate, which is the ordinary case.

**The identity must match the `subject` the suite requests, under either scheme.** Security establishes the
caller identity from whichever credential was presented — the `Basic` user-id, or the certificate — and
then reconciles the request body's `subject` against it, refusing a mismatch with `403` — deliberately,
because a caller that could name any subject it liked would make the credential decorative. The suite
requests `pfw-e2e-suite` by default, and **the recipe in
[`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §9.3.1 generates a client certificate for that
identity alongside Gateway's and DataServices'.** An earlier form of that recipe generated only the two
service identities, on the reasoning that only the two services call the issuance endpoint — which
overlooked that this suite is a third caller of it.

If a deployment's certificate establishes some other name, **override `E2E_TOKEN_SUBJECT` rather than
editing a spec**: the subject is read from that variable and defaults to `pfw-e2e-suite`, so aligning the
two needs no code change.

**Do not point the suite at `SECURITY_MTLS_CERT_PATH` expecting it to work by coincidence.** The suite
reads that name, and `../../orchestration/.env.example` uses the same name to record where Security's
**server** certificate went. The two are opposite halves of one handshake: the server certificate
identifies Security to its callers, a client certificate identifies a caller to Security. Handing the
suite the server pair produces a completed handshake, an established identity of `security-service`, and a
`403` on the subject reconciliation — a refusal that is entirely correct and looks nothing like the
mistake that caused it. The orchestration template does not *set* those two variables (it declares
neither; the server half is supplied per service through `<SERVICE>_TLS_CERTIFICATE_PATH` and
`<SERVICE>_TLS_CERTIFICATE_KEY_PATH`),
so an operator following the template leaves them unset and the authenticated specs **skip** with that
reason rather than failing — which is why this hazard only bites someone who fills them in by hand.

**With NEITHER credential configured, the authenticated specs skip and say so.** That is the intended
posture rather than a gap: `GET /v1/ping` answering `401` without a token is the property C-G actually
requires, and it is provable with no credential at all. Note the boundary carefully — it is *neither*, not
*no certificate*: a run that supplies the `Basic` pair is fully provisioned and every authenticated spec
runs, which is what the documented bring-up produces.

---

## 5. What the suite covers

Six assertion groups, one per workflow area.

**① Health and readiness aggregation (contract C-10).** `/health` answers `200` anonymously on all
four services, and Gateway's report **names each of its three upstreams**. The property under test
is the *aggregation*, not Gateway's own liveness: a Gateway that is up but whose upstreams are not
must not report itself healthy, and each upstream's self-report must agree with Gateway's view of
it. This is the group that proves the readiness gate is real rather than declared.

**② Authentication (constraint C-G).** `/v1/ping` **returns `401` without a token**, asserted
explicitly as a stated acceptance behaviour rather than treated as incidental — it is the standing
proof that every boundary the decomposition created is authenticated, and it is why the runner
attaches no global `Authorization` header. The suite then mints a token **at run time** from
Security, presents it, and expects the authenticated path to succeed; and it checks that Security
publishes its verification material anonymously, including the OIDC discovery document that lets a
consumer's stock bearer handler self-configure with no bespoke code.

**③ Capability gating.** `/v1/capabilities` projects the legacy eight-bit initialization gate as
configuration. The aggregate is **`INIT_FLAG_ENABLE_ALL` = 3847**, with the fast-engine bit clear.
Several assertions in this group need no stack at all: they compare the projected values against the
eight constants transcribed from the legacy declaration, check that the aggregate sums exactly the
seven bits the legacy sums, and confirm that the response names no deferred service, no deferred
route family and no secret-shaped material.

**④ The four reserved deferred routes (constraint C-D).** `/v1/design/**`, `/v1/documents/**`,
`/v1/integration/**` and `/v1/scripting/**` each answer **`501 Not Implemented`** with a
machine-readable body naming the deferred capability area and carrying the marker
`reserved for Phase 2`. **These are routing declarations, not stubs.** There is no project, no
container, no test project, no partial implementation and no exception-throwing placeholder class
behind any of the four; the route exists so the shape of the eventual system is legible from
Gateway's own contract. Accordingly this group asserts **only the route contract** — the status, the
body shape and the marker — and never exercises deferred functionality, because there is none to
exercise. See [`../../docs/DEFERRED.md`](../../docs/DEFERRED.md), which together with Gateway's
routing metadata is one of only two places these capability areas are described at all.

**⑤ The DataWindow retrieve / validate / update workflow.** Over `/v1/datawindow/**`, Gateway's REST
projection of C-03, against the `COMPANY` fixture of [§7](#7-the-primary-fixture-dw_sqlitesrd-and-company):
retrieval arrives as an ordered chunk sequence carrying the DataWindow buffer shape rather than a
flat rowset; an insert round-trips its engine-assigned identity; a `NOT NULL` violation is refused as
a **structured error** and persists nothing; and an update carrying original values for all six
marked columns applies cleanly.

**⑥ The optimistic-concurrency conflict path.** A stale update — one whose original values no longer
match the stored row — is refused with **HTTP 409**, projected from the gRPC `Aborted` status the
services return internally. The conflict detail carries **both the current and the original value**
of every marked column, so a caller can decide between retry and surface without a second round
trip. The group then proves the property that matters most: the refused update **changed nothing**.
There is **no silent overwrite anywhere in this system**, and the conflict is recoverable only by an
explicit refresh-and-retry.

---

## 6. Preserved defects a spec must NOT "correct"

The migration preserves behaviour **exactly**, replicating documented legacy defects rather than
correcting them. A spec that asserts the corrected value does not catch a bug — it *enforces a
behaviour change*, and it will keep enforcing it long after everyone has forgotten why the number
looked wrong.

**No performance assertion of any kind may be added to this suite.** The repository publishes no
service-level agreement, no latency budget, no throughput target and no availability commitment
anywhere, so there is no baseline for such an assertion to be measured against and inventing one
would fabricate a requirement. The only quantitative non-functional requirement in the entire brief
is the 80 % line-coverage gate, and that is a .NET-side obligation ([§9](#9-technology-and-boundary-decisions)).
The `timeout` and `expect.timeout` values in `playwright.config.ts` are **hang guards** — they exist
so a wedged request ends the run instead of hanging a pipeline — and they assert nothing whatsoever
about response time. The built-in slowest-test ranking is suppressed for the same reason: a timing
league table in the output would read as a performance signal that nothing in the requirements
sanctions.

### 6.1 `INIT_FLAG_ENABLE_ALL` is 3847, and never 3855

The legacy declaration, verified verbatim at `ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49`:

| Constant | Value |
| --- | ---: |
| `INIT_FLAG_ENABLE_UI` | 1 |
| `INIT_FLAG_ENABLE_SCITER` | 2 |
| `INIT_FLAG_ENABLE_BLINK` | 4 |
| `INIT_FLAG_ENABLE_BLINKFAST` | 8 |
| `INIT_FLAG_ENABLE_ORCA` | 256 |
| `INIT_FLAG_ENABLE_SQLITE` | 512 |
| `INIT_FLAG_ENABLE_DPIAWARE` | 1024 |
| `INIT_FLAG_ENABLE_WEBVIEW` | 2048 |

`INIT_FLAG_ENABLE_ALL` sums **seven** of those eight and **deliberately omits
`INIT_FLAG_ENABLE_BLINKFAST`**, because `blink.dll` and `blinkfast.dll` are two alternative builds of
one engine — enabling both would be contradictory rather than more complete:

```text
1 + 2 + 4 + 256 + 512 + 1024 + 2048 = 3847
```

With the fast-engine bit included it would be 3855. **A spec asserting 3855 would enforce a
behaviour change**, so 3847 is the only correct expectation. Of the eight bits, only
`INIT_FLAG_ENABLE_SQLITE` has an in-scope Phase-1 consumer; the interface and DPI bits belong to
DesignSystem, the three engine bits and the embedded-web bit to ScriptBridge, and the packaging bit
to tooling — none of which is built in this phase.

The constant **identifier spellings are preserved verbatim** in the .NET code, screaming snake case
and all, contrary to C# naming convention. That is deliberate: these identifiers appear in serialized
payloads, in log records and in characterization recordings, so renaming one would silently
invalidate every stored comparison. Analyzer suppressions scoped to the affected files accompany the
decision on the .NET side. Specs must spell them the legacy way too.

### 6.2 The four DataWindow-versus-DDL type mismatches

The legacy DataWindow definition and the legacy DDL disagree about **four** of the six `COMPANY`
columns. Both are read-only, both are authoritative for their own side, and the disagreement is
reproduced rather than reconciled:

| Column | DataWindow declaration | DDL declaration | Direction of the disagreement |
| --- | --- | --- | --- |
| `name` | `char(100)` — `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L9` | `NAME TEXT NOT NULL` — **unbounded** — `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L465` | The DataWindow is **stricter** than the schema: it bounds a column the DDL leaves unbounded |
| `address` | `char(200)` — `dw_sqlite.srd:L11` | `ADDRESS CHAR(50)` — `w_test_sqlite.srw:L467` | The DataWindow is **looser** than the declaration, and SQLite enforces neither length |
| `salary` | `decimal(2)` — `dw_sqlite.srd:L12` | `SALARY REAL` — `w_test_sqlite.srw:L468` | A fixed two-place decimal declared over a floating-point column |
| `birth` | `date`, with the `yyyy-mm-dd` edit mask on the column control at `dw_sqlite.srd:L26` — `dw_sqlite.srd:L13` | `BIRTH TEXT` — `w_test_sqlite.srw:L469` | A date type declared over a text column, so the format is a convention rather than a constraint |

**`name` is the one most easily missed, and it is why the count is four.** A 100-character bound over an
unbounded `TEXT NOT NULL` column looks benign beside the other three — but it disagrees in the
*opposite direction* to `address`, and the two together are why the entity cannot adopt either side's
types wholesale. `services/persistence-service/PowerFramework.Persistence/Data/CompanyEntity.cs`
records all four against these same locators and **explicitly declines** to impose the DataWindow's
`char(100)` and `char(200)` bounds, because imposing them would correct the defect rather than preserve
it (C-B). [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §8.4 and
[`../../docs/PARITY.md`](../../docs/PARITY.md) §7 are the authorities and both count four.

Consequences for a spec author, stated as rules:

- **Do not assert client-side length validation at 50, at 100 or at 200.** None of the three is a
  validated boundary; they are the two sides of two unreconciled disagreements. A spec that pins any of
  them would freeze an accident into a requirement — and for `name` specifically, a spec asserting that
  a 101-character value is rejected would assert the **opposite** of what the storage does, since
  SQLite stores it in full.
- **Keep happy-path fixture strings at 50 characters or fewer**, which stays inside every one of the
  four declarations at once, so no mismatch is ever accidentally the subject of a happy-path assertion.
  The row builders in `fixtures/` already do this; keep it that way when adding one.
- **Treat `birth` as text on the wire and as a `yyyy-mm-dd` string in a fixture.** SQLite stores it
  as `TEXT` while the DataWindow calls it a date, and the edit mask is what fixes the spelling.
- **Compare `salary` with a tolerance rather than for exact equality.** It is a two-place decimal in
  the DataWindow and a floating-point `REAL` in storage; the fixtures expose an explicit comparison
  tolerance for precisely this reason.

---

## 7. The primary fixture: `dw_sqlite.srd` and `COMPANY`

`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` is the **only updatable DataWindow in the repository**,
which makes it the golden-master fixture for the entire retrieve / validate / update triple. Its
table specification, at `:L14`, is exactly:

```text
retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no  sort="age A salary A " )
```

All six columns at `:L8-L13` carry `update=yes updatewhereclause=yes`; `id` is additionally
`key=yes identity=yes`; and the footer compute at `:L27` is `sum(salary for page)` — a page-scoped
aggregate, which is why even this small fixture exercises the expression evaluator.

The backing DDL — the **only DDL anywhere in the repository**, at
`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469` — is:

```sql
CREATE TABLE IF NOT EXISTS COMPANY(
  ID       INTEGER PRIMARY KEY NOT NULL,
  NAME     TEXT    NOT NULL,
  AGE      INT     NOT NULL,
  ADDRESS  CHAR(50),
  SALARY   REAL,
  BIRTH    TEXT)
```

What follows from that, and what every spec against this fixture must respect:

- **`ID` is the auto-increment column.** An insert must **not** supply it; the engine assigns it and
  the identity value **round-trips back** to the caller. A spec that sends an `id` is not testing the
  identity round trip, it is bypassing it.
- **`NAME` and `AGE` are `NOT NULL`.** Omitting either is the negative case, and the expected
  outcome is a **structured error that persists nothing** — not a partial write, and not a dialog.
- **`updatewhere=1` is the "key and updateable columns" concurrency mode.** Because all six columns
  are marked, the optimistic-concurrency check spans **all six columns' original values**. The
  payload must therefore carry, per row, both the current and the original value of every marked
  column — which is why a flat rowset would be insufficient and why the conflict detail of
  assertion group ⑥ carries two value sets.
- **`updatekeyinplace=no`** means a key change is performed as delete-plus-insert rather than as an
  in-place update. The fixture sets it itself, so that path is exercised by the fixture and is not a
  rare branch.
- **The sort is part of the fixture**, `age` ascending then `salary` ascending. Retrieval order is
  a property of the DataWindow, not of the spec's expectations.

The `COMPANY` schema, the marked-column list, the deterministic row builders, the salary comparison
tolerance and the stale-original marker used by the conflict path all live in `fixtures/`, so a spec
never transcribes the schema for itself.

---

## 8. Layout

Every path below was checked before being listed here.

| Child | Role |
| --- | --- |
| `package.json` | The manifest: six scripts, three exactly-pinned dev dependencies, the Node floor and the npm `packageManager` pin. Private, so it can never be published |
| `package-lock.json` | The npm-generated lockfile. **Committed deliberately** — `npm ci` requires it and fails without it, and it is what makes an install reproducible |
| `playwright.config.ts` | The runner configuration, and the file that records each of the decisions in [§9](#9-technology-and-boundary-decisions) at its point of effect |
| `tsconfig.json` | The type-check gate: `strict` plus the additional checks, and `noEmit` so nothing is ever written beside the sources |
| `.gitignore` | Nested ignore rules for this directory's generated output. See [§9](#9-technology-and-boundary-decisions) for why it is nested rather than a root change |
| `fixtures/` | The shared surface every spec imports through one barrel: the four base URLs and the endpoint table, the verified `COMPANY` schema with its deterministic row builders, the eight capability constants, run-time token acquisition, and a stack-availability probe. Beside them, on their own import paths, three modules the barrel deliberately does not re-export — `token-issuance.ts`, the one place that decides what a run does when no mutual-TLS identity is provisioned; `run-mode.ts`, the one place that decides whether a run is a full acceptance run or an acknowledged partial one; and `contract-shape.ts`, the one place that decides what "the boundary conformed" means |
| `specs/` | The workflow suites — the six assertion groups of [§5](#5-what-the-suite-covers), one area per file. Exactly six files are present and all six are the suite; see the note below |
| `scripts/` | `provision-e2e-client-identity.sh`, which writes the ephemeral client identity of [§3.2](#32-the-full-run--this-one-needs-a-running-stack-and-an-issuance-identity). It provisions only: it starts nothing, contacts nothing, prints no key material, and writes exclusively into the gitignored `.mtls/` |

`fixtures/index.ts` is a pure barrel and holds no value of its own, which is both why it can carry no
credential and why loading it cannot start, probe or fail on anything. Three modules keep their own
import path and are deliberately not re-exported through it: the stack precondition because it performs
I/O and imports the runner's `test` object, `run-mode.ts` because the *config* must read it and the
config must not import the runner, and `contract-shape.ts` because it reads the repository from disk.
The barrel stays a pure re-export of runner-independent data.

**`fixtures/contract-shape.ts` is where every exact assertion in this suite lives**, and it exists
because the tolerant readings are each individually defensible and collectively fatal — a media type
accepted by `toContain('json')`, a verdict matched by `/\b(healthy|ok|up|pass)\b/i`, an upstream "named"
by a case-insensitive substring anywhere in the body, a member resolved through a list of three or four
alternative spellings, an advertised key-set URI checked by `endsWith`. Each has a defensible local
reason and together they leave a suite that cannot fail for the single most likely defect on a freshly
decomposed boundary: **a response-field rename or wire-shape drift**. It
asserts exact media types, exact required-and-permitted member sets, lowerCamelCase member names,
exact case-sensitive enum tokens, the contract's `const` values, and full URIs rather than suffixes —
with every expected value taken from `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` and
**re-derived from that document by a `@no-stack` guard test**, so the fixture cannot drift from the
contract in either direction. The tolerant reads survive in exactly one role: `describeShapeForFailure`
reports a body's *structure* — parsed or not, object or array, member names, length — and never a
value, for use inside a failure message after an exact assertion has already decided the outcome.

Generated directories — `node_modules/`, `test-results/` and any report directory — are ignored and
**must never be committed**. See [§9](#9-technology-and-boundary-decisions); the trace and report
rules in particular are a secrets control rather than housekeeping.

> **`specs/` holds exactly the six numbered files, and discovery is constrained to exactly those.**
> The suite is `01-health-readiness`, `02-authentication`, `03-capability-gating`, `04-deferred-routes`,
> `05-datawindow-workflow` and `06-concurrency-conflict`. **A second, unnumbered generation covering the
> same six areas (`readiness`, `authentication`, `capability-projection`, `deferred-routes`,
> `datawindow-workflow`, `concurrency-conflict`) must never sit beside them.**
>
> A `playwright.config.ts` declaring `testDir: './specs'` with no `testMatch` collects both generations —
> twelve files. That is not merely untidy: both generations write `COMPANY`
> rows in the same single `persistence-db` volume, so the row state the optimistic-concurrency
> assertion depends on is changed by files nobody reviewed for that purpose, and every
> pass, fail and skip total covers twice as many files as this document describes. Two generations
> also disagree about an absent stack — an unnumbered generation probing and skipping while the
> numbered one fails — so a run with nothing up produces a confusing partial result instead of one
> answer, with nothing selecting which is authoritative.
>
> **Both halves of the guard are in place.** The directory holds exactly one generation, and the config
> names the six specs explicitly and **verifies that inventory against the directory before the run**, so
> a renamed or deleted spec stops the run rather than quietly shrinking it. Adding a spec is a deliberate
> two-part act: create the file and add it to `SUITE_SPECS`. `fixtures/live-stack.ts` carries only its
> live-stack probe, which guards all six specs; token acquisition lives once in `fixtures/auth.ts`, which
> is the path the specs use. Measured
> figures are in [§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed).

---

## 9. Technology and boundary decisions

This section is where the decisions taken for this subtree are recorded, each with its reason. None
is a preference dressed as a rule.

| Decision | Reason |
| --- | --- |
| **`@playwright/test` pinned to exactly `1.62.1`** — no `^`, no `~`, no range operator | A range operator would let a future minor change the runner under a suite whose whole purpose is a reproducible comparison. The pin is the same discipline the .NET side applies through central package management, where every version is fixed in one place and no project may drift |
| **`typescript` and `@types/node` pinned exactly too** | Same reason. The TypeScript line is chosen deliberately rather than taken as `latest`, because the pinned Playwright release's own type definitions are validated against the mature compiler line |
| **The npm-generated lockfile is committed** | `npm ci` requires it and fails without it. It is the artifact that makes an install reproducible, and it is deliberately *not* ignored |
| **Serialized execution — `fullyParallel: false`, `workers: 1`** | A **correctness** decision, not a performance one. The mutating workflows share `COMPANY` rows in a single `persistence-db` volume, and the concurrency assertion depends on a *known* row state: with `updatewhere=1` and all six columns marked, a conflict is a function of all six original values. Parallel workers would interleave those mutations and make the `409` non-deterministic. Repeatability is also the one hard prerequisite of the golden-master comparison this suite feeds |
| **`retries: 0`** | A retry must never be allowed to convert a real failure into a pass. The sharpest case is the stale update: it **must** fail, because there is no silent overwrite anywhere in this system. Were a retry count ever introduced for CI flake, the state-mutating specs would have to pin it back to zero locally — `06-concurrency-conflict` carries that pin today, and it matters more now that its six steps are one atomic test, because a retry would re-run the entire mutation sequence |
| **`forbidOnly` when `CI` is set** | A stray `test.only` silently narrows a run to one test while still reporting green |
| **No browser binaries, and no `playwright install`** | The suite drives HTTP only. There is no presentation surface in this phase, so a browser download would be several hundred megabytes serving nothing and would imply a UI that does not exist |
| **No `webServer` block, and no global setup or teardown** | The single bring-up path is the Compose manifest under `orchestration/`, whose health-condition chain is what makes Gateway report healthy only after its upstreams. A second bring-up path would bypass that gate; a seeding hook would break the paired-capture rule |
| **`testMatch` is an explicit six-file inventory, verified against the directory at config load** | `testMatch` entries resolve relative to `testDir`, so a list of leaf filenames **narrows** discovery and cannot reach outside `tests/e2e/` — the read-only siblings stay structurally undiscoverable either way. Naming the files is what stops a bare sweep collecting an unreviewed spec generation beside these, which would run twelve files and mutate shared `COMPANY` state from two generations at once. The inventory is checked both ways — the sorted on-disk list is compared exactly against the enumeration — so a renamed or deleted file stops the run instead of silently covering less, AND an unenumerated `.spec.ts` file stops it instead of silently never running. A one-sided check is the tempting shortcut and, in its usual form, tautological: comparing the enumeration's length with its own cannot fail |
| **Console reporter only; artifact capture off; slowest-test ranking suppressed** | Defence in depth on two fronts. A trace records request and response bodies, which for this suite means a bearer token written verbatim into an artifact; not generating the artifact is the control and the ignore rule is the safety net. And a duration report would read as a performance signal this project does not sanction |
| **No coverage tooling in this npm project** | The 80 % line-coverage gate is a per-service .NET obligation, measured from each service's own `coverage.cobertura.xml` and enforced per service so one service cannot mask another. It is **not** measured from this suite, and wiring a JavaScript coverage tool here would produce a second, meaningless number. See [`../../docs/PARITY.md`](../../docs/PARITY.md) and [`../../docs/BUILD.md`](../../docs/BUILD.md) |
| **No local `.dockerignore`** | The repository-root `.dockerignore` already excludes `node_modules/` anywhere in the tree, the three read-only legacy directories, and `tests/e2e/` itself. The container build context is the repository root, so the root file is the one that applies; a local copy would be a second authority that could drift |
| **No root `.gitignore` change; the ignore rules are nested here instead** | The plan records that no root `.gitignore` change is required, and that file is left exactly as the legacy repository has it. `tests/e2e/` is a directory this refactor creates, so the rules for what it generates belong inside it — where they apply to this directory alone and cannot suppress a same-named path elsewhere. Either way, `node_modules/`, `test-results/` and any report directory must simply never be committed |
| **Nothing added to `PowerFramework.slnx`** | This is an npm and Playwright project, not an MSBuild one. The root solution correctly enumerates exactly **twenty** .NET projects and excludes this suite, mentioning it only in a comment noting that it is driven by its own tooling |
| **`npm test` rather than `npx playwright test` in the documented path** | A supply-chain reason, recorded in [§3.2](#32-the-full-run--this-one-needs-a-running-stack-and-an-issuance-identity): `npx` will acquire a package when no local binary is present, which is exactly the state a failed install leaves behind |
| **A separate `typecheck` script** | `--list` collects without type-checking, so the type check has to be its own gate. `npm run verify` runs both and is everything that can be verified with no stack running |
| **`tsconfig.json` is strict RFC 8259 JSON, with no comments** | TypeScript reads JSONC, so comments there are legal and were used — and the repository now holds every committed JSON artifact to a strict parse with no carve-out, because a gate with one documented exception is a gate future files can hide behind. The rationale each flag carried moved here, to [§9.1](#91-the-type-check-gate-every-tsconfigjson-flag-and-why-it-is-set), which is where a contributor to this directory reads |

### 9.1 The type-check gate: every `tsconfig.json` flag and why it is set

**Without this file there is no type check of this directory at all.** `playwright test` transpiles each
spec with Babel and **deliberately performs no type checking** — it strips the types and runs the
JavaScript — so a spec that reads a misspelled export, passes a string where a number is required, or
ignores a possibly-undefined value runs happily and fails, if at all, as a confusing runtime error against
a live stack. `playwright test --list` does not close that gap either: it loads and collects the files,
which catches a syntax error or a missing module, and catches nothing else. The type check is therefore a
**separate gate**, run as `npm run typecheck`, and it is the only mechanism in this directory that reads
the types the fixtures so carefully declare.

**No emit, ever.** This project produces no JavaScript. Playwright owns the transform at run time, so a
second compiled copy of every spec would be an untracked artifact that could drift from its source and be
run by accident. `noEmit` is not a convenience — it is what keeps the transform in exactly one place.

**Why every extra strictness flag is here.** The project has no user-specified rules — the rules document
contains exactly one line stating that none were provided — so the enterprise-standard baseline applies in
their place, and on the .NET side of this repository that baseline is `Nullable` enabled with
`TreatWarningsAsErrors`. The nearest equivalent available here is `strict` plus the additional checks
below, so this file is the TypeScript counterpart of that setting rather than a looser local choice.

| Flag | The mistake it catches |
| --- | --- |
| `target: ES2023`, `lib: [ES2023]` | The runtime this suite actually runs on. Node 22 is an LTS line and is what `engines` in the sibling `package.json` requires, so targeting and libbing it means the type checker models the same platform the runner uses rather than a hypothetical older one |
| `module`/`moduleResolution: NodeNext` | Node's own module resolution as Node 20+ implements it, which is what Playwright's loader uses. `nodenext` rather than `bundler` because there is no bundler anywhere in this suite |
| `noEmit: true` | See above — a containment decision, not a convenience |
| `types: ["node"]` | Node's platform types, which is what makes `process.env` a known symbol. Naming the set explicitly rather than letting every package under `node_modules/@types` load implicitly keeps the ambient surface auditable: only Node's types and whatever a spec imports by name are in scope |
| `strict: true` | The whole strict family, matching the .NET side's nullable-plus-warnings-as-errors posture |
| `noUncheckedIndexedAccess` | An indexed read yields `T \| undefined`. This is the flag that turns "the payload had no such field" from a silent `undefined` flowing onward into a compile error **at the point of the read** — the exact class of defect the capability fixture's own domain guard exists to catch at run time |
| `noPropertyAccessFromIndexSignature` | A property read through an index signature must be written as one, so a misspelled dotted access cannot silently resolve to `any` |
| `noUnusedLocals`, `noUnusedParameters` | A dead local or an unused parameter in a spec is nearly always a partly finished assertion, so both are errors rather than hints |
| `noImplicitReturns`, `noFallthroughCasesInSwitch` | Every branch of a function that returns a value must return one, and a `switch` case may not fall through implicitly. Both are silent-wrong-answer bugs rather than crashes, which is what makes them worth failing the build over |
| `noImplicitOverride` | `override` must be written where it applies, and a class field that shadows a base accessor is an error rather than a surprise |
| `exactOptionalPropertyTypes` | An optional property may be absent, but it may not be explicitly `undefined` — so an absent field and a present-but-undefined field stay distinguishable. That distinction is load-bearing in this repository: the published contracts use an explicit "unspecified" marker precisely because absence and a zero value mean different things |
| `useUnknownInCatchVariables` | A caught value is `unknown`, so an error has to be narrowed before its message is read. The fixtures already throw typed errors and the specs assert on them; this keeps that honest |
| `isolatedModules` | Every file must be independently transformable. This is the flag that matches Playwright's actual mechanism: it hands each spec to Babel one file at a time with no whole-program knowledge, so a construct that needs cross-file type information to lower correctly — a re-export of something that turns out to be a type, say — would transpile to something subtly wrong. `isolatedModules` rejects exactly those constructs |
| `forceConsistentCasingInFileNames` | Case-sensitive imports, matching the Linux container this runs in |
| `esModuleInterop`, `allowSyntheticDefaultImports`, `resolveJsonModule`, `skipLibCheck` | Interop settings that let the CommonJS-shaped Playwright entry point be imported by name |

**`verbatimModuleSyntax` is deliberately NOT set, and the reason is measured rather than assumed.** The
sibling `package.json` declares no `"type": "module"`, so Node — and therefore TypeScript under
`NodeNext` — treats every `.ts` file here as CommonJS. With `verbatimModuleSyntax` enabled, the
`import`/`export` syntax these files are authored in becomes an error (TS1287 and TS1295, **31 of them**
across the two fixtures and the runner config). That syntax is correct as written: Playwright transpiles
each file to CommonJS at load time, which is its documented arrangement. The two ways to satisfy the flag
would be to add `"type": "module"`, changing how the runner loads every file, or to rewrite the fixtures in
CommonJS syntax — both are behavioural changes made to satisfy a stylistic check, so neither is done. Every
other strict flag is on.

**The read-only boundary (C-C) is enforced by `include` and `exclude`.** `include` names only this
directory's own sources, and `exclude` names `node_modules` and the runner's artifact directories. The
three sibling directories under `tests/` — `blink`, `sciter` and `webview` — hold read-only
behavioural-oracle assets, and nothing here may reach them: this file lives inside `tests/e2e`, every path
in it is relative to it, and no path ascends. **A `../` anywhere in that file would be the one way to break
that containment by accident.**

---

## 10. Secrets: never replicate, document, rotate

**No key, certificate, password, token or other credential material appears in this document or
anywhere in this directory, and none may be added.**

`tests/blink/test_jws.htm` — one of the read-only siblings — is **hardcoded-secret site 1** of the
**eight in-source sites** the repository-wide sweep found. The requirements named three; the sweep
found eight in source plus three inside vendored binaries. Every one of the eight lies inside the
test, demonstration or legacy-browser-asset regions, which is to say **inside the read-only region**,
and that is what fixes the remediation posture:

> **Never replicate → document → rotate. Never "edit the legacy file".**

Deleting or editing the offending file would corrupt the behavioural oracle and buy nothing, because
the value is already in the repository's history. So the material is never reproduced in any new
artifact, every locator is recorded once — **value-free** — in
[`../../docs/SECRETS.md`](../../docs/SECRETS.md), and the credentials themselves are rotated by their
owners outside the codebase. This document therefore names the file and nothing else: **no line
numbers, no marker strings, no digest of that file and no fragment of its contents are restated
here**, because `SECRETS.md` is the single home for that detail and a second copy is a second thing
to leak. (The one digest this document does quote is the collective content fingerprint of the three
read-only sibling directories, used as the untouched proof — see
[§14.5](#145-one-expected-hit-in-the-secret-sweep-cleared-explicitly).)

Site 1 is additionally worth naming as **the anti-pattern that Security's
`Tokens/SigningKeyProvider.cs` replaces**: a page that carries signing key material inline and signs
with it. What replaces it is structural rather than cosmetic —

- **Exactly one signing secret exists in the whole system**, held by Security, injected from the
  orchestration secret layer through the options pattern. It appears in no source file, no
  `appsettings.json` and no container definition.
- **Security is the sole issuer.** Gateway, DataServices and Persistence hold **verification
  material only** and validate with the framework's stock bearer handler against the published key
  set. None of them is an independent signing authority.
- **This suite mints nothing.** It obtains tokens **at run time** from Security's token endpoint,
  presents them per request, and stores none. The runner attaches no global `Authorization` header —
  which is also what makes the standing `401`-without-a-token assertion possible to write at all.
- **Mutual-TLS material is read as filesystem paths from the environment**, never inline — see
  [§4.4](#44-base-url-and-the-environment-this-suite-reads).
- **The client identity this suite presents is EPHEMERAL and generated, never committed.**
  `npm run provision:identity` writes a throwaway authority and leaf into the gitignored `.mtls/`,
  fresh on every run, with the private keys `0600` inside a `0700` directory. It prints **paths only**
  — no key, no certificate body, no passphrase — and neither the script nor any fixture message ever
  echoes a value; variable names and fixed prose are all any of them carry. Nothing it produces is
  reused between environments, so there is nothing there worth keeping and everything there worth not
  committing. This is site 1's anti-pattern inverted: generated and disposable rather than authored
  and tracked.
- **The provisioning script constrains both what it destroys and what it emits.** Its output directory
  override must resolve strictly inside `tests/e2e/`, it contains no `rm -rf`, it removes only the seven
  filenames it writes and refuses a directory holding anything else, and every value it reports is
  quoted — single-quoted in the dotenv file it writes, `%q`-quoted in the `export` stream that the
  documented `eval` consumes. Leaving either unconstrained is the failure mode: an override reaching
  `rm -rf` deletes unrelated data, and an unquoted emitted path containing a shell metacharacter is
  command execution in the operator's shell. A script that provisions credentials is exactly the wrong place to
  leave either.
- **The base URL may not embed credentials.** The configuration rejects such a value outright,
  because Playwright records request URLs in failure messages and artifacts, so a credential in the
  base address would be a credential written into every artifact of the run.

---

## 11. Prohibitions

Short, explicit, and each one traceable to a constraint in
[§12](#12-governing-constraints--and-the-absence-of-user-rules):

1. **No secret value anywhere.** No key, certificate, password, token, passphrase or long encoded
   blob in a spec, a fixture, a configuration file, an environment file or this document. Credentials
   are read from the environment, or obtained at run time from the sole issuer.
2. **No performance, latency, throughput, availability or service-level assertion.** None is
   published for this system, so none may be implied. Timeouts are hang guards.
3. **No UI dependency.** No browser engine, no page object, no locator, no device preset, no
   screenshot, no visual comparison, no component library, no design system, no styling of any kind.
4. **No spec may talk to DataServices or Persistence functionally.** Everything functional goes
   through Gateway. The only non-Gateway traffic permitted is Security's token endpoint and its
   published verification material, plus the anonymous `/health` probes C-10 declares on all four.
5. **No spec may exercise a deferred capability area beyond its `501` route contract.** There is
   nothing behind those four routes to exercise, and reaching for it would imply otherwise.
6. **Nothing added to `PowerFramework.slnx`.** This is an npm project, not an MSBuild project.
7. **No local `.dockerignore`**, and **no root `.gitignore` change**. Generated output is ignored by
   the nested rules here and must never be committed regardless.
8. **Nothing at `tests/` level, ever** — no manifest, no lockfile, no config, no `node_modules/`, no
   file of any kind. Everything this suite needs lives in `tests/e2e/`.
9. **No edit, reformat, re-encode, rename, move, delete, lint, prettify or dependency upgrade** of
   `tests/blink/`, `tests/sciter/` or `tests/webview/`.
10. **No coverage tool in this npm project**, and no second reporter that writes artifacts.
11. **No seeding, resetting or reseeding of the database**, and no bring-up of a service from here.
12. **No correction of a preserved legacy defect** ([§6](#6-preserved-defects-a-spec-must-not-correct)).

---

## 12. Governing constraints — and the absence of user rules

**No user-specified rules exist for this project.** The rules document was retrieved and it contains
exactly one statement: that no user rules were provided. There is nothing further in it to read, and
nothing in this directory — including this file — is in scope because a rule demanded it. That
absence is **not latitude**: the enterprise-standard baseline applies in the rules' place, which for
this subtree means exact version pins, a committed lockfile, the strictest available type checking as
the counterpart of the .NET side's nullable-plus-warnings-as-errors posture, no credential in source,
and no artifact that could carry one.

What does bind this folder is the set of non-rule constraints the migration plan enumerates. Each is
cited by name; none is reproduced, because the plan is their home:

| Constraint | What it requires of this folder |
| --- | --- |
| **C-B** — no new features, no behaviour improvements, no performance objective | Preserve the legacy defects of [§6](#6-preserved-defects-a-spec-must-not-correct) verbatim; assert no latency, throughput, availability or service-level figure |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | The warning at the top of this file, the corrected sibling inventory, the untouched proof, and the ban on creating anything at `tests/` level |
| **C-D** — do not implement the four deferred capability areas, even partially, even to stub them out | Assert the four reserved routes as **routing declarations**, never as services; never imply a project, container, test project or placeholder class exists behind one |
| **C-F** — nothing hardcoded may be carried forward; the three named secret sites are a floor, not a ceiling | The never-replicate posture of [§10](#10-secrets-never-replicate-document-rotate); run-time tokens; a single system-wide signing secret held by Security alone; value-free references only |
| **C-G** — no new attack surface: every new boundary authenticated | The explicit `401`-without-a-token assertion, anonymous `/health`, sole-issuer token minting, verification-only peers |
| **C-H** — 80 % line coverage per in-scope service | Recorded as a .NET-side, per-service obligation that is **not** measured here; no coverage tool in this project |
| **C-J** — a single local-orchestration path brings all four services up together | The Compose manifest is the only bring-up path; no `webServer`, no global setup; Gateway healthy only after its three upstreams |
| **C-K** — document every technology-specific and boundary-specific decision | [§9](#9-technology-and-boundary-decisions) — this file is what discharges C-K for this subtree |
| **C-L** — the attached environment's setup instructions are binding operational constraints | The install-and-run path of [§3](#3-install-and-run), followable verbatim from inside `tests/e2e/`, and the 5101–5105 port band with the composition root on 5105 |

---

## 13. Cross-references

Each document below is the **authority** for its subject. This README deliberately does not restate
their content, and must never contradict them; where a detail is needed here it is summarised and
attributed rather than copied.

| Document | Authority for |
| --- | --- |
| [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) | Service topology, the listener and port map, transport selection per service, capability gating |
| [`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) | The ten cross-service contracts C-01..C-10 and the four reserved extension points |
| [`../../docs/PARITY.md`](../../docs/PARITY.md) | The characterization model, the determinism seams, and the shared-volume paired-capture rule |
| [`../../docs/SECRETS.md`](../../docs/SECRETS.md) | Every hardcoded-secret locator, value-free, with severity and required action |
| [`../../docs/DEFERRED.md`](../../docs/DEFERRED.md) | The deferred roster and the four reserved routes |
| [`../../docs/BUILD.md`](../../docs/BUILD.md) | The .NET build, the per-service commands, and the coverage gate |
| [`../../orchestration/README.md`](../../orchestration/README.md) | Bring-up, the environment file, the readiness gates — and **section 10, the single statement of what has and has not been exercised** |
| [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) | The four services, the `persistence-db` volume, the TLS secret projections, and the health-condition chain |

Every row is linked, `orchestration/README.md` section 10 being the authority this document defers to for
anything about what has actually been run.

The legacy sources this suite's fixtures were derived from — `dw_sqlite.srd`, `w_test_sqlite.srw` and
`enums.sru` — are cited by path and line in [§6](#6-preserved-defects-a-spec-must-not-correct) and
[§7](#7-the-primary-fixture-dw_sqlitesrd-and-company). They are read-only. The three legacy sibling
directories under `tests/` are deliberately **not** linked from this document, only named, so that no
tool following a link reaches an oracle asset.

---

## 14. As-verified / not verified

This section reports what was **actually observed in this repository**, on this host, in this
clone. It is not a restatement of the plan's expectations, and it claims nothing that was not run.
The vocabulary is the one the sibling documents use and
[`../../docs/BUILD.md`](../../docs/BUILD.md) §1 defines: *Present and verified*, *Present but
unexercised*, *Validated only on a throwaway skeleton*, and *Planned — not yet present*. **Nothing this
document references carries the last label** — every artifact named here is in the tree, and what remains
is a distinction between present-and-run and present-and-not-yet-run.

### 14.1 Commands that were run, and what they printed

**Every count below is a POINT-IN-TIME observation, and each is re-measurable by running the command in
its own row.** A pass, skip and did-not-run breakdown moves whenever a case is added, so a row here is
evidence that a command behaved as this document says it does — not a figure to be defended. An earlier
revision of this table had drifted in exactly that way, and both of the partial-run rows below were
re-measured rather than adjusted: one of them additionally described a command that
[§3](#3-install-and-run) records as having been unreachable at the time.
[§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed) states the rule this follows —
a collected-test total belongs in a runnable command rather than in prose.

| Command (from `tests/e2e/`) | Result |
| --- | --- |
| `node -v` | **`v22.23.2`** |
| `npm -v` | **`11.18.0`** |
| `npm ci` | **Succeeded**, exit 0 — *"added 6 packages, and audited 7 packages"*, *"found 0 vulnerabilities"* |
| `npm run typecheck` (`tsc --noEmit`) | **Succeeded**, exit 0, **zero errors** |
| `npm run test:list` (`playwright test --list`) | **Succeeded**, exit 0 — *"Total: 29 tests in 6 files"*, and only the six numbered ones |
| `npx playwright test --list --grep "@no-stack"` | **Succeeded**, exit 0 — the stack-free subset, three files |
| `npm test` (`playwright test`), no identity, no stack | **Failed**, exit 1 — **refused in `globalSetup`, before any test ran**, so there is no test summary at all: *"the end-to-end suite runs against a live four-service stack and 4 of 4 services did not answer"*, each named with its URL and classified `unreachable`, and the refusal quotes the partial alternative. A genuine setup failure, which is what an absent stack must produce |
| `npm run test:partial`, no identity, no stack | **Failed**, exit 1 — **5 failed, 3 skipped, 20 did not run, 1 passed**, every line labelled `[api-partial-no-stack]`. The five failures are the token-issuance `beforeAll` of the five authenticated groups. Acknowledging the *stack* does not acknowledge the *identity*: the two variables are independent, and this run is what proves one cannot suppress the other's finding |
| `npm run test:partial:no-identity`, no stack | **Succeeded**, exit 0 — **9 passed, 20 skipped**, every line labelled `[api-partial-no-stack]`. Both acknowledgements are given, so each precondition declines its own tests with a stated reason and the stack-free assertions pass. This is the only stack-free invocation that exits zero, and its project label is why it cannot be misread as an acceptance result. `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY=1 npm run test:partial` is the same run spelled by hand, and measures identically |
| `E2E_ALLOW_ABSENT_STACK=ture` (a deliberate typo), identity acknowledged, no stack | **Failed**, exit 1 — *"E2E_ALLOW_ABSENT_STACK is set to a value this suite does not recognise. Accepted values are 1 and true, matched case-insensitively"*, raised in `globalSetup` so nothing ran. An unrecognised value is a configuration error rather than a silent "off", which is the branch that stops a typo from producing the very failure the author was opting out of. The configured value is not echoed |
| `E2E_ALLOW_ABSENT_STACK=TRUE`, identity acknowledged, no stack | **Succeeded**, exit 0 — **9 passed, 20 skipped**, labelled `[api-partial-no-stack]`, i.e. identical to the acknowledged run above: the accepted values are matched case-insensitively |
| `npm run provision:identity` | **Succeeded**, exit 0 — wrote `.mtls/` 0700 holding `client-ca.crt`/`client-ca.key`/`e2e-client.crt`/`e2e-client.key`/`e2e-identity.env`, with the two keys **and** the environment file at 0600, and printed three `export` lines and no key material. `openssl x509 -noout -subject` reports **`subject=CN=pfw-e2e-suite`**, the extended key usage is **TLS Web Client Authentication**, and `openssl verify -CAfile` reports **OK**. A second run is idempotent, exit 0 |
| `bash -n` and `shellcheck` on the provisioning script | **Both clean**, exit 0, zero findings |
| Containment refusals — `E2E_IDENTITY_DIR` set to `/`, to the empty string, to whitespace only, to `/tmp/...`, to the suite directory itself, to `../..`, and to the read-only `../blink` sibling | **All seven refused**, exit 1, each naming the resolved path and the accepted region, and **nothing written or removed** in any of them. `pwd -P` canonicalization is what makes `../..` and a symlink fail rather than pass a string-prefix test |
| Unexpected-content refusal — a stray file placed in `.mtls/` | **Refused**, exit 1, naming the stray entry. **The stray file survived and so did the existing key material**: the script clears only the seven filenames it writes, so a mistyped-but-contained override cannot empty a directory it does not own |
| Shell-injection probe — a suite-contained directory named `.mtls-probe;touch /tmp/…;x $(touch /tmp/…)` `` `touch /tmp/…` `` | **Injection blocked.** The emitted stream carries `\;`, `\$\$`, `\$\(` and `` \` ``; `eval` of it exits 0, binds the path as exactly one word, and **no injected file exists** afterwards |
| The same probe against the **pre-fix** script, as a negative control | **Injection SUCCEEDED** — `eval` of its unquoted output executed the embedded `touch` and the file appeared. The same pre-fix script also accepted an **outside-suite** target and created it. Both are the defects the two remedies above remove, measured rather than assumed |
| A renamed spec, as a negative control on the inventory | **Failed at config load**, naming the absent file — the run stops rather than quietly covering five files |
| The three-sibling digest — the first of the two untouched-proof commands near the top of this file | `e9de966fe49b982b30aea135c6381f0901bba498c31a181704e027066cdb84b8`, over **170** tracked paths |
| `git status --porcelain tests/blink tests/sciter tests/webview` | **Empty** — the read-only siblings are untouched |
| `find tests -maxdepth 1 -mindepth 1 -type f` | **Printed nothing** — no file exists at `tests/` level |

**The two preconditions are distinct, and keeping them distinct is the point.** An **absent issuance
identity** is a setup fault: `POST /v1/tokens` on Security requires a caller credential — an HTTP
`Basic` `clientCredential` **or** a trusted client certificate, either alone being sufficient and never a
bearer token, since a caller cannot present a token to obtain its first one — so with **neither**
configured no authenticated assertion is runnable, and the five authenticated
groups fail their **setup** with one clear statement per group rather than letting fifteen token calls
fail one at a time with transport errors that never say why. An **absent stack** is a distinct
condition with a distinct owner, so it is detected once per worker — before any assertion, by a probe
of the one anonymous endpoint the contract guarantees needs no credential — and reported with a reason
that names what was probed and the exact bring-up command.

**Both preconditions behave the same way, and that symmetry is load-bearing.** Reporting an absent stack
as a SKIP is the plausible reading, on the reasoning that it is "neither pass nor fail". That is true of a
test and false of a run: a run whose every HTTP assertion skipped still exits zero, so the one state a
misconfigured pipeline is in would report success. An absent stack therefore **fails** a full
acceptance run exactly as an absent identity does, and both admit the same shape of explicit,
separately named opt-in — `E2E_ALLOW_ABSENT_STACK` alongside
`E2E_ALLOW_MISSING_ISSUANCE_IDENTITY` — which skips with a stated reason and additionally relabels the
project `api-partial-no-stack` so a summary line cannot be mistaken for an acceptance result.

**Nothing was softened to achieve either.** No assertion tolerates an unreachable host, and a stack
that IS up and violates a contract still fails the run; the distinction is drawn once, before the
assertions, rather than inside them — which is the only way to keep "absent" and "broken" from looking
alike. A run that declares itself partial with `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY` has its
token-dependent tests decline themselves with the reason stated, and acknowledgement is never inferred
from the certificate's absence.

The passes with nothing running are the stack-free assertions, which still run because they carry the
`@no-stack` tag that exempts them from the reachability guard: the capability-table parity checks, the
`COMPANY` column contract check and the Security base-URL coherence check. A tag rather than a title
match, deliberately — two of those tests never carried the `(no stack)` wording, so a substring rule
would have skipped exactly the coverage that is available before a bring-up.

### 14.2 Whether the suite was executed against a running stack

**Stated plainly: this suite has NOT been executed against a running stack, and no pass is claimed for any
stack-dependent assertion.** That is a statement about *this suite*, and it is the only part of the
execution picture this document owns.

The surrounding facts, as observations rather than assumptions:

- **Docker is available here** — `docker --version` reports **29.7.0** and `docker info` answers with
  a matching server version.
- **The bring-up path is present and has been exercised**, which is a different act from running this
  suite. [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) and
  [`../../orchestration/README.md`](../../orchestration/README.md) both exist, and section 10 of that readme
  is the **single execution-status statement** for the repository — it records the bring-up gate by gate and
  states, among the things not exercised, that `tests/e2e` was not run against it. This document does not
  restate that record and does not contradict it.
- **All four container definitions exist and all four services build and test independently** in Release
  with zero warnings — `services/gateway-service/Dockerfile`,
  `services/dataservices-service/Dockerfile`, `services/security-service/Dockerfile` and
  `services/persistence-service/Dockerfile`, each with its own `Program.cs` entry point.
- **No container has been started from this suite at all**, so no assertion in `specs/` has reported on a
  live endpoint and no inter-service edge has been exercised *by this suite*.

**What the consistency check could and could not compare.** The port map, the endpoint list and the
install-and-run commands in this document were checked line by line against
[`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4 and
[`../../docs/BUILD.md`](../../docs/BUILD.md) §4 and §9, and they agree — including the `npm test`
over `npx` preference, the node and npm floors, the `1.62.1` pin, and the 5101–5105 allocation with
5103 reserved. **Both comparators exist**: [`../../orchestration/README.md`](../../orchestration/README.md)
carries the bring-up and the readiness gates, and the repository-root
[`../../README.md`](../../README.md) carries the service roster with the same ports. The port map and the endpoint list here agree with both. What this document still does **not** claim
is that its own live-stack assertions have been run — that is §4.1's statement, and it defers to the single
execution-status statement rather than making a second one.

### 14.3 Two discrepancies, found and reported rather than absorbed

**Discrepancy 1 — the scope of the `tests` fingerprint, and why only one digest is quoted.** The
baseline `e9de966fe49b982b30aea135c6381f0901bba498c31a181704e027066cdb84b8` over **170** tracked paths
is the digest of **`tests/blink` + `tests/sciter` + `tests/webview`**, and it reproduces exactly. It is
*not* the digest of the whole of `tests/`, and the distinction is the whole point of quoting a scoped
one.

**No whole-`tests/` digest and no path count is quoted anywhere in this file, deliberately.** Such a
digest **cannot be quoted correctly in the file it covers**: `tests/e2e/README.md` is itself one of the
tracked paths inside `tests/`, so every edit to this document changes the value, and the figure is stale
the instant it is committed. Quoting a value and a count here and then committing the README moves both
— which is why the figure does not belong here at all, rather than an argument for keeping it with a
caveat.

**The three-sibling digest has none of that problem, which is why it is the untouched check.** It covers
only the three read-only legacy directories, so it is invariant under every addition this refactor makes
to `tests/e2e/` and it changes if — and only if — an oracle asset is touched. That is precisely the
property an untouched proof needs, and it is what the proof near the top of this file states.
Reproduce it with:

```bash
git ls-files -s tests/blink tests/sciter tests/webview | sha256sum
git ls-files tests/blink tests/sciter tests/webview | wc -l
```

**Only one spec generation exists, and that is enforced rather than assumed.** A second, unnumbered
generation alongside the numbered one would write `COMPANY` rows in the same volume and inflate every
`--list` total, so `playwright.config.ts` names the six numbered files and **verifies that inventory
against the directory** at config load: a rename stops the run instead of quietly shrinking it.

**The collection is six files, and it is checked rather than quoted from memory.** Reproduce the
breakdown with the published command instead of trusting a figure in prose — a collected-test count
changes whenever a parameterised case is added, so it is the kind of number this documentation set keeps
in one runnable place:

```bash
cd tests/e2e && npx playwright test --list --reporter=list \
  | grep -oE '^  \[api\] › [0-9]+-[a-z-]+\.spec\.ts' | sed 's/.*› //' | sort | uniq -c
```

Two properties of that total are worth knowing before reading it. It counts **collected tests, not
declarations**, so the four reserved deferred routes in `04-deferred-routes` appear as four
parameterised cases from a single declaration. And the stack-free subset is a **tag**, not a title
match: `npx playwright test --list --grep "@no-stack"` reports it separately, and it is a strict subset
spanning fewer files than the whole.

### 14.4 Residual risk

The four service images are authored separately from this suite. **The exact response payload shapes are
confirmed against the running stack** — the precise JSON of `/v1/capabilities`, the body of the four `501`
responses, and the structure of the `409` conflict detail — and the readers are tightened to the published
shapes accordingly: the specs accept no alias keys, no alternate
containers and no PascalCase/snake_case variants, because a reader that tolerates them can pass while a
client generated from the OpenAPI document or from protobuf JSON fails. The specs assert those contracts
**as specified** in [`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) and
[`../../docs/DEFERRED.md`](../../docs/DEFERRED.md), which is the correct thing for a contract test to
do; but the first run against a real stack is also the first opportunity to discover a field name or a
nesting level that differs from the specification. Where such a difference appears, the resolution is
to reconcile the **service** with the published contract, or to amend the contract deliberately and
then the specs — never to loosen an assertion until it passes.

Two further limits worth stating, so nobody infers more from a green `npm run verify` than it earns:

- **A collection pass proves that every spec loads, and a type check proves that every spec is
  type-correct. Neither proves that any assertion holds.** Only a run against a live stack does that.
- **The pinyin-matching filter behaviour that the drop-down search path depends on cannot be proven
  bit-exact from this repository alone**, because its lookup table lives inside a closed binary. If it
  ever surfaces in an end-to-end assertion it must be characterized against the behavioural oracle
  and reported as blocked if the oracle cannot be exercised — never approximated.

### 14.5 One expected hit in the secret sweep, cleared explicitly

A credential sweep over this file — searching for key and certificate block markers, credential-shaped
assignments and long encoded runs — matches only **one kind of value**: a **single** 64-character
hexadecimal SHA-256 digest — the three-sibling untouched proof, which occurs three times: in the proof
near the top of this file, in the table at
[§14.1](#141-commands-that-were-run-and-what-they-printed) and at
[§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed). A long-hexadecimal
pattern
cannot distinguish a digest from an encoded key, so the match is expected by construction and is
cleared here rather than left for a reader to worry about. Every narrower pattern — key and
certificate block markers, provider-specific key prefixes, bearer-token shapes and credentials
embedded in a URL — returns nothing at all.

That is **one distinct** value: no whole-`tests/` digest is quoted, for the reason §14.3 gives, so this
clearance covers one digest at three occurrences rather than two digests at four.

It is a **content fingerprint of tracked files**, produced by `git ls-files -s` piped
through `sha256sum`, and it is reproducible by anyone holding this repository. It is not a key, a
token, a certificate or a credential of any kind, and it carries no secret: a digest of a file
listing is a *checksum over paths, modes and blob identifiers*, which is exactly why it works as an
untouched proof. It is quoted deliberately, because a proof nobody can reproduce is not a proof.

The words "password" and "passphrase" appear here only where a credential is being **forbidden**, or
as the *name* of an environment variable in the table at
[§4.4](#44-base-url-and-the-environment-this-suite-reads) — never attached to a value. And no line
number, marker string or content fragment of any secret site is restated anywhere in this document.
