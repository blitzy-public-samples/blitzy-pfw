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
siblings**, not to the whole of `tests/`. A whole-`tests/` digest is not a usable baseline, because
`tests/e2e/` is additive and still growing — see
[§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed), which records the measured
figures for both scopes and explains why using the wrong one produces a false alarm rather than a
finding.

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
- serialized and deterministic by construction, because it mutates shared database state.

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

### 3.2 The full run — this one needs a running stack

```bash
npm test              # playwright test
```

**Use `npm test`, not `npx playwright test`, and the difference is a supply-chain one.** The `test`
script runs the `playwright` binary that `npm ci` just installed from the lockfile at the pinned
`1.62.1`. `npx` prefers a local binary too, but when there is not one — precisely the state left by
a failed install — it will go and **acquire** a package to run instead. `npx playwright test` and
`npx playwright test --list` are correct only *after* a successful `npm ci`.

**Without a running stack, `npm test` does not pass, and it is not supposed to.** The
stack-dependent assertions are the point of the suite; there is no configuration in which a missing
stack is reported as success. Run `npm run verify` when there is nothing up, and `npm test` once
[§4](#4-bring-up-and-readiness) reports ready. [§14](#14-as-verified--not-verified) records exactly
what each of these commands did when this file was written, including the failures.

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

The four services are brought up **only** by `orchestration/docker-compose.yml`. This suite starts
nothing: `playwright.config.ts` deliberately carries **no `webServer` block** and no global setup or
teardown, so it points at an already-running stack and does nothing else. Two independent reasons:

- **Spawning anything from here would create a second, competing bring-up path** and would bypass
  the health-condition dependency chain described below.
- **Anything that seeded or reset the store would break the paired-capture rule**, under which a
  legacy-side and a target-side recording for one workflow are comparable only when taken against
  the same unrecreated `persistence-db` volume state.

For the bring-up commands, the environment file and the readiness gates, see
[`../../orchestration/README.md`](../../orchestration/README.md) and
[`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml). Those are the
authority; this document does not restate their commands as a second one.

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

| Service | Port | Transport | Token role |
| --- | ---: | --- | --- |
| `PowerFramework.Persistence` | 5101 | gRPC (C-05..C-08), plus REST `/health` and `/v1/ping` | verification only |
| `PowerFramework.DataServices` | 5102 | gRPC (C-03, C-04), plus a thin REST projection consumed only by Gateway | verification only |
| *(reserved)* | 5103 | — | commented-out DesignSystem Phase-2 slot |
| `PowerFramework.Security` | 5104 | REST + `/.well-known/jwks.json` + OIDC discovery | **SOLE ISSUER** |
| `PowerFramework.Gateway` | **5105** | REST + OpenAPI | verification only |

Two notes that belong with the table rather than inside it:

- **Scheme.** Persistence, DataServices and Security publish TLS listeners; Gateway's published
  ingress is the plaintext `http://localhost:5105` the environment documents. Security's listener is
  TLS in *every* environment, so its readiness probe is `https://localhost:5104/health` rather than
  `http`. The listener configuration and the reasoning are in
  [`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4; this suite reads each address from
  the environment (§4.4) rather than hardcoding a scheme.
- **5103 is reserved, not reassigned.** The attached environment had allocated it to a design
  service, and DesignSystem is precisely one of the four capability areas this phase does not build.
  Leaving the slot commented out is the honest Phase-2 placeholder; nothing listens there, and the
  suite's endpoint table deliberately has **no entry at all** for it.

The endpoints this suite touches, and nothing besides these:

| Endpoint | Where | Auth |
| --- | --- | --- |
| `GET /health` | all four services | anonymous |
| `GET /v1/ping` | through Gateway | **JWT required — `401` without one** |
| `GET /v1/capabilities` | through Gateway | JWT |
| `/v1/datawindow/**` | through Gateway | JWT |
| `/v1/design/**`, `/v1/documents/**`, `/v1/integration/**`, `/v1/scripting/**` | through Gateway | reserved routes, `501` |
| `POST /v1/tokens` | Security | mutual TLS (C-01) |
| `GET /.well-known/jwks.json` | Security | anonymous |
| `GET /.well-known/openid-configuration` | Security | anonymous |

[`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) is the authority for what each one carries; the
table above exists only so a reader can see the whole surface this suite exercises at a glance.

### 4.4 Base URL, and the environment this suite reads

**Gateway on 5105 is the suite's sole functional base URL.** `playwright.config.ts` sets
`use.baseURL` from `GATEWAY_BASE_URL`, defaulting to `http://localhost:5105`, and validates it
structurally before the run rather than degrading: an empty, malformed, non-`http(s)`,
credential-bearing, query-bearing or fragment-bearing value **fails the run immediately, naming the
variable at fault.** That fail-fast posture is deliberate and mirrors the framework being migrated,
which treats a structural fault as fatal rather than as something to continue past.

Variables the suite reads. These are **names only** — no value appears in this repository, and none
may be added to it:

| Variable | Default | Purpose |
| --- | --- | --- |
| `GATEWAY_BASE_URL` | `http://localhost:5105` | The sole functional base URL |
| `SECURITY_BASE_URL` | `http://localhost:5104` | Token issuance, JWKS and OIDC discovery |
| `DATASERVICES_BASE_URL` | `https://localhost:5102` | Anonymous `/health` probe only |
| `PERSISTENCE_BASE_URL` | `https://localhost:5101` | Anonymous `/health` probe only |
| `SECURITY_MTLS_CERT_PATH` | *(unset)* | Client-certificate **path** for the issuance edge |
| `SECURITY_MTLS_KEY_PATH` | *(unset)* | Client-key **path** for the issuance edge |
| `SECURITY_MTLS_KEY_PASSPHRASE` | *(unset)* | Read only if the key needs one |
| `CI` | *(unset)* | When set, a stray `test.only` fails the run instead of narrowing it |

The three mutual-TLS variables exist because token issuance is a **mutual-TLS edge** —
[`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) C-01 — so a client certificate must be
presented to mint a token. The suite reads **filesystem paths**, never inline material; the
certificate and key themselves live outside the repository and are supplied by the environment.

One detail in that table needs saying out loud rather than being left to be discovered at run time:
**`SECURITY_BASE_URL` defaults to a plain-`http` address, while Security's actual listener is TLS.**
The default is deliberately permissive — nothing in the suite restricts the scheme — so against a
real stack it must be overridden to the `https` address, and a client certificate must be configured
for issuance to succeed. The authoritative listener configuration is in
[`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4; treat it, not this table, as the
source of truth for the address, and treat this row as a default rather than as a claim about the
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

### 6.2 The three DataWindow-versus-DDL type mismatches

The legacy DataWindow definition and the legacy DDL disagree about three of the six `COMPANY`
columns. Both are read-only, both are authoritative for their own side, and the disagreement is
reproduced rather than reconciled:

| Column | DataWindow declaration | DDL declaration |
| --- | --- | --- |
| `address` | `char(200)` — `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L11` | `ADDRESS CHAR(50)` — `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469` |
| `salary` | `decimal(2)` — `dw_sqlite.srd:L12` | `SALARY REAL` |
| `birth` | `date`, with the `yyyy-mm-dd` edit mask on the column control at `dw_sqlite.srd:L26` — `dw_sqlite.srd:L13` | `BIRTH TEXT` |

Consequences for a spec author, stated as rules:

- **Do not assert client-side length validation at 50 or at 200.** Neither number is a validated
  boundary; they are two sides of an unreconciled disagreement. A spec that pins either one would
  freeze an accident into a requirement.
- **Keep happy-path fixture strings at 50 characters or fewer**, so the mismatch is never
  accidentally the subject of a happy-path assertion. The row builders in `fixtures/` already do
  this; keep it that way when adding one.
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
| `package.json` | The manifest: four scripts, three exactly-pinned dev dependencies, the Node floor and the npm `packageManager` pin. Private, so it can never be published |
| `package-lock.json` | The npm-generated lockfile. **Committed deliberately** — `npm ci` requires it and fails without it, and it is what makes an install reproducible |
| `playwright.config.ts` | The runner configuration, and the file that records each of the decisions in [§9](#9-technology-and-boundary-decisions) at its point of effect |
| `tsconfig.json` | The type-check gate: `strict` plus the additional checks, and `noEmit` so nothing is ever written beside the sources |
| `.gitignore` | Nested ignore rules for this directory's generated output. See [§9](#9-technology-and-boundary-decisions) for why it is nested rather than a root change |
| `fixtures/` | The shared surface every spec imports through one barrel: the four base URLs and the endpoint table, the verified `COMPANY` schema with its deterministic row builders, the eight capability constants, run-time token acquisition, and a stack-availability probe |
| `specs/` | The workflow suites — the six assertion groups of [§5](#5-what-the-suite-covers), one area per file |

`fixtures/index.ts` is a pure barrel and holds no value of its own, which is both why it can carry no
credential and why loading it cannot start, probe or fail on anything. The stack-availability probe
keeps its own import path and is deliberately not re-exported through the barrel.

Generated directories — `node_modules/`, `test-results/` and any report directory — are ignored and
**must never be committed**. See [§9](#9-technology-and-boundary-decisions); the trace and report
rules in particular are a secrets control rather than housekeeping.

> **Observed discrepancy in `specs/`, recorded rather than quietly resolved.** At the time this file
> was written, `specs/` held **twelve** spec files: the numbered set `01-health-readiness`,
> `02-authentication`, `03-capability-gating`, `04-deferred-routes`, `05-datawindow-workflow` and
> `06-concurrency-conflict`, **plus an earlier unnumbered set covering the same six areas**
> (`readiness`, `authentication`, `capability-projection`, `deferred-routes`,
> `datawindow-workflow`, `concurrency-conflict`). Because `playwright.config.ts` declares
> `testDir: './specs'` with no `testMatch` or `testIgnore`, **all twelve are collected** — 103 tests.
> The numbered set is the one the file plan names. The two generations should be reconciled to one;
> until they are, expect the mutating workflows to be exercised twice in a single run, which the
> serialized execution of [§9](#9-technology-and-boundary-decisions) makes orderly but does not make
> redundant. Measured figures are in [§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed).

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
| **`retries: 0`** | A retry must never be allowed to convert a real failure into a pass. The sharpest case is the stale update: it **must** fail, because there is no silent overwrite anywhere in this system. Were a retry count ever introduced for CI flake, the concurrency and ordering specs would have to pin it back to zero locally |
| **`forbidOnly` when `CI` is set** | A stray `test.only` silently narrows a run to one test while still reporting green |
| **No browser binaries, and no `playwright install`** | The suite drives HTTP only. There is no presentation surface in this phase, so a browser download would be several hundred megabytes serving nothing and would imply a UI that does not exist |
| **No `webServer` block, and no global setup or teardown** | The single bring-up path is the Compose manifest under `orchestration/`, whose health-condition chain is what makes Gateway report healthy only after its upstreams. A second bring-up path would bypass that gate; a seeding hook would break the paired-capture rule |
| **No `testMatch` or `testIgnore` glob** | The built-in spec pattern is evaluated relative to `testDir`, so discovery is contained by construction. A glob added here would be the one way to widen that surface — and reach a read-only sibling — by accident |
| **Console reporter only; artifact capture off; slowest-test ranking suppressed** | Defence in depth on two fronts. A trace records request and response bodies, which for this suite means a bearer token written verbatim into an artifact; not generating the artifact is the control and the ignore rule is the safety net. And a duration report would read as a performance signal this project does not sanction |
| **No coverage tooling in this npm project** | The 80 % line-coverage gate is a per-service .NET obligation, measured from each service's own `coverage.cobertura.xml` and enforced per service so one service cannot mask another. It is **not** measured from this suite, and wiring a JavaScript coverage tool here would produce a second, meaningless number. See [`../../docs/PARITY.md`](../../docs/PARITY.md) and [`../../docs/BUILD.md`](../../docs/BUILD.md) |
| **No local `.dockerignore`** | The repository-root `.dockerignore` already excludes `node_modules/` anywhere in the tree, the three read-only legacy directories, and `tests/e2e/` itself. The container build context is the repository root, so the root file is the one that applies; a local copy would be a second authority that could drift |
| **No root `.gitignore` change; the ignore rules are nested here instead** | The plan records that no root `.gitignore` change is required, and that file is left exactly as the legacy repository has it. `tests/e2e/` is a directory this refactor creates, so the rules for what it generates belong inside it — where they apply to this directory alone and cannot suppress a same-named path elsewhere. Either way, `node_modules/`, `test-results/` and any report directory must simply never be committed |
| **Nothing added to `PowerFramework.slnx`** | This is an npm and Playwright project, not an MSBuild one. The root solution correctly enumerates exactly **twenty** .NET projects and excludes this suite, mentioning it only in a comment noting that it is driven by its own tooling |
| **`npm test` rather than `npx playwright test` in the documented path** | A supply-chain reason, recorded in [§3.2](#32-the-full-run--this-one-needs-a-running-stack): `npx` will acquire a package when no local binary is present, which is exactly the state a failed install leaves behind |
| **A separate `typecheck` script** | `--list` collects without type-checking, so the type check has to be its own gate. `npm run verify` runs both and is everything that can be verified with no stack running |

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
| [`../../orchestration/README.md`](../../orchestration/README.md) | Bring-up, the environment file, and the readiness gates |
| [`../../orchestration/docker-compose.yml`](../../orchestration/docker-compose.yml) | The four services, the `persistence-db` volume, and the health-condition chain |

The legacy sources this suite's fixtures were derived from — `dw_sqlite.srd`, `w_test_sqlite.srw` and
`enums.sru` — are cited by path and line in [§6](#6-preserved-defects-a-spec-must-not-correct) and
[§7](#7-the-primary-fixture-dw_sqlitesrd-and-company). They are read-only. The three legacy sibling
directories under `tests/` are deliberately **not** linked from this document, only named, so that no
tool following a link reaches an oracle asset.

---

## 14. As-verified / not verified

This section reports what was **actually observed while writing this file**, on this host, in this
clone. It is not a restatement of the plan's expectations, and it claims nothing that was not run.
The vocabulary is the one the sibling documents use: *Present and verified*, *Present but
unexercised*, and *Planned — not yet present*.

### 14.1 Commands that were run, and what they printed

| Command (from `tests/e2e/`) | Result |
| --- | --- |
| `node -v` | **`v22.23.2`** |
| `npm -v` | **`11.18.0`** |
| `npm ci` | **Succeeded**, exit 0 — *"added 6 packages, and audited 7 packages"*, *"found 0 vulnerabilities"* |
| `npm run typecheck` (`tsc --noEmit`) | **Succeeded**, exit 0, **zero errors** |
| `npm run test:list` (`playwright test --list`) | **Succeeded**, exit 0 — **`Total: 103 tests in 12 files`** |
| `npm test` (`playwright test`) | **Failed**, exit 1 — **17 failed, 38 passed, 40 skipped, 8 did not run** |
| The three-sibling digest — the first of the two untouched-proof commands near the top of this file | `e9de966fe49b982b30aea135c6381f0901bba498c31a181704e027066cdb84b8`, over **170** tracked paths |
| `git status --porcelain tests/blink tests/sciter tests/webview` | **Empty** — the read-only siblings are untouched |
| `find tests -maxdepth 1 -mindepth 1 -type f` | **Printed nothing** — no file exists at `tests/` level |

**The `npm test` failure is fully explained, and it is not a defect in the system under test.** Every
one of the 17 failures is a transport error against an **absent stack** — connection failures to
`5105`, `5101` and `5104`, including the token request that could not reach Security. The 40 skips
come from the earlier unnumbered spec generation, which probes for a live stack first and skips with
an explicit reason; the numbered set has no such guard and therefore fails rather than skips. The 38
passes are the stack-free assertions: the capability-table parity checks, the endpoint-table checks
and the pure helper checks.

### 14.2 Whether the stack was brought up, and whether the suite was executed

**Stated plainly: the Compose stack was NOT brought up, and this suite has NOT been executed against
a running stack. No pass is claimed for any stack-dependent assertion.**

The reasons are specific to this clone at this moment, and they are observations rather than
assumptions:

- **Docker is available here** — `docker --version` reports **29.7.0** and `docker info` answers with
  a matching server version — so the absence of a run is not an absence of Docker.
- **`orchestration/` currently contains only `.env.example`.** There is **no
  `orchestration/docker-compose.yml`** and **no `orchestration/README.md`** in this clone, and the
  Compose manifest is the only sanctioned bring-up path. Both are *Planned — not yet present*, which
  is also how the sibling build and parity documents label them.
- **Three of the four container definitions are absent.** Only `services/gateway-service/Dockerfile`
  exists; DataServices, Persistence and Security have none yet. All four `Program.cs` entry points do
  exist.

Consequently the two links in [§13](#13-cross-references) that point into `orchestration/` currently
resolve to planned files rather than present ones. They are named because that is where the
corresponding work belongs, and they are the paths the plan declares.

**What the consistency check could and could not compare.** The port map, the endpoint list and the
install-and-run commands in this document were checked line by line against
[`../../docs/ARCHITECTURE.md`](../../docs/ARCHITECTURE.md) §4 and
[`../../docs/BUILD.md`](../../docs/BUILD.md) §4 and §9, and they agree — including the `npm test`
over `npx` preference, the node and npm floors, the `1.62.1` pin, and the 5101–5105 allocation with
5103 reserved. Two intended comparators could not be made: `orchestration/README.md` does not exist
yet, and the repository-root `README.md` still carries only its original licence content with no .NET
section, so there was nothing there to compare a port map against. Neither absence is a discrepancy —
both files are declared work that had not landed in this clone — but neither is a confirmation either,
and this document does not claim one.

### 14.3 Two discrepancies, found and reported rather than absorbed

**Discrepancy 1 — the scope of the `tests` fingerprint.** The baseline
`e9de966fe49b982b30aea135c6381f0901bba498c31a181704e027066cdb84b8` over **170** tracked paths is the
digest of **`tests/blink` + `tests/sciter` + `tests/webview`**, and it reproduces exactly. It is *not*
the digest of the whole of `tests/`: measured immediately before this file was committed,
`git ls-files -s tests | sha256sum` was
`5a8d7097109d303a0334bbe04e6cb024d055b234da37fb04d25f7da7d2a241d9` over **193** tracked paths — the
same 170 legacy paths plus 23 additive `tests/e2e/` paths.

**Committing this very README then moved it**, to a different digest over **194** paths, while the
three-sibling digest did not budge. That is a demonstration rather than an argument: a whole-`tests/`
digest changes every time this directory gains a file, so using it as the untouched check would raise
a false alarm over the perfectly legitimate addition of a document. The three-sibling digest is the
one to use, exactly as the untouched proof near the top of this file states it.

**Discrepancy 2 — two spec generations coexist.** Recorded in
[§8](#8-layout) with the measured breakdown: the numbered set contributes 32 collected tests
(3 + 4 + 10 + 4 + 5 + 6) and the earlier unnumbered set 71 (10 + 12 + 17 + 16 + 8 + 8), which is the
103 that `--list` reports. They should be reconciled to one set. This is reported rather than
silently resolved here, because deleting spec files is not this document's to do.

### 14.4 Residual risk

The four service images are authored separately from this suite. **At the time this suite and this
document were written, the exact response payload shapes could not be confirmed against a live
service** — in particular the precise JSON of `/v1/capabilities`, the body of the four `501`
responses, and the structure of the `409` conflict detail. The specs assert those contracts **as
specified** in [`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) and
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
assignments and long encoded runs — matches only **one kind of value**: the two distinct
64-character hexadecimal SHA-256 digests, quoted at four places between the untouched proof near the
top and [§14.3](#143-two-discrepancies-found-and-reported-rather-than-absorbed). A long-hexadecimal
pattern
cannot distinguish a digest from an encoded key, so the match is expected by construction and is
cleared here rather than left for a reader to worry about. Every narrower pattern — key and
certificate block markers, provider-specific key prefixes, bearer-token shapes and credentials
embedded in a URL — returns nothing at all.

Both values are **content fingerprints of tracked files**, produced by `git ls-files -s` piped
through `sha256sum`, and both are reproducible by anyone holding this repository. Neither is a key, a
token, a certificate or a credential of any kind, and neither carries any secret: a digest of a file
listing is a *checksum over paths, modes and blob identifiers*, which is exactly why it works as an
untouched proof. They are quoted deliberately, because a proof nobody can reproduce is not a proof.

The words "password" and "passphrase" appear here only where a credential is being **forbidden**, or
as the *name* of an environment variable in the table at
[§4.4](#44-base-url-and-the-environment-this-suite-reads) — never attached to a value. And no line
number, marker string or content fragment of any secret site is restated anywhere in this document.
