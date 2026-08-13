<!-- Markdown lint policy for this file. Rationale is in docs/BUILD.md section 14. MD013 is 120 rather
     than the 80-character default, and is disabled for tables and code blocks: an evidence row carrying
     a legacy locator and a quoted finding cannot be wrapped without splitting the locator from what it
     proves, and a wrapped command is a command that does not run. Prose IS wrapped, and is held to the
     120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It applies to the seven documents this refactor authored and CANNOT reach the five read-only legacy
     Chinese documents in this folder, which are the behavioural oracle, are never edited, and carry
     pre-existing violations of their own (hard tabs, unlabelled code fences and others) that this
     refactor must not "fix". That unreachability is precisely why a per-file directive was chosen over a
     repository-root configuration artifact the plan does not provide for.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e,
     and no Markdown linter appears in it. A documented on-demand package-runner invocation would
     therefore instruct an unpinned version to be resolved and executed from the network outside that
     lockfile every time somebody followed the documentation, which the deterministic-automation baseline
     forbids. Lint with tooling that is already installed; the directive below is what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# PowerFramework → .NET 10 — Build Reference

This document is the authoritative reference for **building, testing and packaging** the four-service
.NET 10 decomposition of PowerFramework. It exists to explain one command — quoted here in the **template**
form the attached environment specifies, with `<service-name>` standing for one of four values:

```text
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

**That block is fenced `text`, not `bash`, deliberately.** `<` and `>` are redirection operators in a
shell, so the template does not parse as bash and is not copy-runnable as written — every block in this
document that carries `<service-name>` is fenced `text` for that reason, and every block fenced `bash` is
one that runs verbatim. §5.2 gives the four concrete, runnable commands, and §5.1 gives a runnable form
parameterised by a real shell variable.

Everything else here — central package management, the two mandatory package pins, the hand-authored
test projects, the one-solution-file-per-directory rule — exists so that this single command **will**
work **verbatim, from a clean checkout, for each of the four services independently** (C-I).

> **State of that command today, measured rather than assumed.** It works, for all four services. Every
> one of them restores, builds in Release with `0 Warning(s)` and `0 Error(s)`, and runs its full test
> suite green — and each clears the 80% line floor of §10 on its own assembly's package within its own
> Cobertura report. The `CS5001` an earlier revision of this document reported
> for the four service *application* projects is no longer reachable: every one now carries a
> `Program.cs` and produces an entry point.
>
> What the command does **not** prove is anything about a running system: it builds and tests each
> service **in process**, so no image is built by it, no service is started by it, and no request crosses
> a network boundary because of it. The container path was exercised separately — all four images and a
> four-service bring-up, recorded in [§1.3](#13-the-canonical-verification-record) — and that is a
> different exercise from this command. §8.3 and §13 hold that line at their own points of use.

**Read §2 before anything else.** It carries two findings that were observed directly and that break the
build if a reader misses them. A reader who stops after the first screen must still have seen both.

**Audience.** Anyone who has to build, test, containerize or add a project to this repository. Every
claim below is either reproducible with a command given in this document or carries a locator into the
read-only legacy tree.

**What this document deliberately does not duplicate.** Cross-reference rather than restatement is the
rule here, because a figure repeated in two places is a figure that will eventually disagree with
itself:

| For | See |
| --- | --- |
| Service topology, transport rationale, the port map, the orchestration decision and its rejected alternative | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| The characterization model, the fixture corpus, coverage mechanics in context and the determinism seams | [`docs/PARITY.md`](PARITY.md) |
| The legacy build-definition anomalies in full, and the full-estate object mapping | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| Secret locators, severities, required actions and the token-topology register | [`SECRETS.md`](SECRETS.md) |
| Compose bring-up detail, the readiness gates step by step, and **the single statement of what has and has not been exercised** | [`orchestration/README.md`](../orchestration/README.md) |
| The cross-service contract inventory | [`CONTRACTS.md`](CONTRACTS.md) |
| The four deferred destinations, which receive no project and no container in this phase | [`DEFERRED.md`](DEFERRED.md) |

## Current state of the artifacts this document references

**Every artifact referenced below is present in the tree.** That includes the solution and project files,
the shared libraries, the protocol and OpenAPI definitions under `shared/PowerFramework.Contracts/`, **all
four service applications with their handler trees and test projects**, **all four container definitions**
(§7.1), `.github/workflows/ci.yml` (§10), the per-service settings,
[`PARITY.md`](PARITY.md), the complete `orchestration/` set — the manifest, `orchestration/.env.example`
and `orchestration/README.md` — the Playwright bootstrap and specs under `tests/e2e/`, the
`characterization/` scaffolding, and the read-only legacy tree.

One artifact is present as **structure without content**, and the distinction matters because a reader
looking for a recording will not find one:

| Artifact | What is present | What is not |
| --- | --- | --- |
| `characterization/` | The capture specification (`README.md`), the workflow definitions (`workflows/`, fifteen of them) and their JSON Schema, and the two recording roots | **No recording, on either side.** `recordings/legacy/` and `recordings/dotnet/` are empty, so no paired comparison has been made |

**Present in the tree is not the same claim as exercised.** For what has actually been run, this document
defers to one place —
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
— rather than keeping a second account that would drift from it.

### The status vocabulary this documentation set uses

**This matters most in this document**, because conflating a command that runs with one that describes an
intention is how a build reference becomes misleading. Four labels are used, here and in
[`ARCHITECTURE.md`](ARCHITECTURE.md), [`PARITY.md`](PARITY.md), [`SECRETS.md`](SECRETS.md) and
`orchestration/.env.example`, and they mean exactly this:

| Label | Meaning |
| --- | --- |
| **Present and verified** | The artifact is in the tree **and** a command was run over it in this repository, with its output quoted |
| **Present but unexercised** | The artifact is in the tree; no run stands behind the claim. Static review only |
| **Validated only on a throwaway skeleton** | The **command shape** was proven against a one-test skeleton project outside this repository. It is evidence about the command and the toolchain, never about this repository's code |
| **Planned — not yet present** | No artifact exists. The text is the specification the work will be built against. **Exactly one thing in this repository still carries this label**: the paired characterization recordings, whose per-workflow directories are labelled so in `characterization/recordings/legacy/README.md` and `characterization/recordings/dotnet/README.md` because they need a PowerBuilder oracle run. Nothing *this* document references carries it |

Applied to the commands in this document. **No figure appears in this list**: §1.3 is the single canonical
record of every measured number, and repeating one here is how the last set of them came to disagree with
each other across six documents.

1. **Present and verified — the build and test path.** `dotnet restore` (audit-clean), the whole-solution
   Release build, and every one of the ten test projects. Counts, coverage rates and the exact commands are
   in [§1.3](#13-the-canonical-verification-record).
2. **Present and verified — the container path.** All four images build, and the four-service Compose
   bring-up reached full health with its ordered readiness gates observed. [§1.3](#13-the-canonical-verification-record)
   records what was run and what answered.
3. **Present and verified — the coverage gate's arithmetic.** The gate step was extracted verbatim from
   `.github/workflows/ci.yml` and run locally against all four real reports, passing for all four and
   failing correctly under four different injected faults.
4. **Present but unexercised.** The CI workflow **as a pipeline** — it is authored and its gate is
   expressed, but it has not run on a GitHub-hosted runner — and the Playwright suite's full run, which
   needs an issuance identity provisioned against a live stack.
5. **Present but unexercised, and the one that matters most.** Behavioural parity against the
   PowerBuilder oracle. The capture model and 15 workflow definitions exist; no recording does, because
   the oracle needs the PowerBuilder runtime. **No parity claim is made anywhere in this document.**

---

## Table of contents

1. [Position: what is verified, what is not, and under which constraints](#1-position-what-is-verified-what-is-not-and-under-which-constraints)
2. [The two findings that must reach every reader](#2-the-two-findings-that-must-reach-every-reader)
3. [The build architecture, and why it is shaped this way](#3-the-build-architecture-and-why-it-is-shaped-this-way)
4. [Prerequisites](#4-prerequisites)
5. [Per-service build and test — the primary path](#5-per-service-build-and-test--the-primary-path)
6. [Whole-solution build — a developer convenience](#6-whole-solution-build--a-developer-convenience)
7. [Container build](#7-container-build)
8. [Local orchestration](#8-local-orchestration)
9. [End-to-end tests](#9-end-to-end-tests)
10. [Continuous integration](#10-continuous-integration)
11. [Toolchain constraints a project author must respect](#11-toolchain-constraints-a-project-author-must-respect)
12. [Why the build is authored from scratch](#12-why-the-build-is-authored-from-scratch)
13. [Closing note: what this document claims and does not claim](#13-closing-note-what-this-document-claims-and-does-not-claim)
14. [Markdown lint policy for this documentation set](#14-markdown-lint-policy-for-this-documentation-set)
15. [Per-service configuration keys](#15-per-service-configuration-keys)
16. [The authoritative target-file inventory](#16-the-authoritative-target-file-inventory)

---

## 1. Position: what is verified, what is not, and under which constraints

### 1.1 No user-specified rules exist

The project's rules document was retrieved and contains exactly one statement: **no user rules were
provided.** That is a finding, not an omission, and three consequences follow:

- **No rule is invented, inferred, or back-filled from convention.** Nothing below is presented as a
  requirement because it is a common practice.
- **Enterprise-standard best practice applies in the rules' place**, and for a build reference it is
  stated explicitly rather than assumed, so that it can be checked: central package management with **no
  floating version, no wildcard and no `latest`**; a **clean dependency audit at restore**; nullable
  reference types and warnings-as-errors on **every** project, test projects included; multi-stage
  container definitions running as a **non-root** user; and a hard coverage gate evaluated **per
  service**.
- **Zero files enter scope because of a rule.** This document exists because the migration plan requires
  a build reference.

The binding constraints therefore come from elsewhere — the migration brief's own clauses and the
attached environment's setup instructions. They are referred to below by their identifiers (C-A through
C-L) rather than reproduced.

### 1.2 What was verified by execution

This build architecture was **validated by execution, not designed on paper.** A throwaway skeleton was
constructed with the pinned .NET SDK and the exact documented per-service command of §5 was run
against it end to end. It produced:

- **`Build succeeded.` with `0 Warning(s)` and `0 Error(s)`**, under `TreatWarningsAsErrors` and central
  package management;
- **`Passed!  - Failed:     0, Passed:     1`**;
- **`coverage.cobertura.xml`** — the exact artifact the coverage gate of §10 is measured from.

The diagnostic codes quoted in §2, §3.2 to §3.4 and §11 were obtained the same way — by provoking each failure on
the pinned SDK and reading what it printed — rather than recalled from memory. §2 additionally
distinguishes the solution-filter code that the repository records repository-wide from the codes a
misused filter surfaces on this SDK, and §13 summarises that distinction.

### 1.3 The canonical verification record

**This subsection is the single source of every measured figure about this repository.** No other
document in this set — and no other section of this one — carries its own copy of a test total, a coverage
rate or a container result; each links here instead. That rule exists because the alternative was tried:
the same figures were maintained by hand in six documents and ended up publishing three different test
totals and two different coverage rates for the same tree, which made the prose an unauditable log rather
than a record. **A figure anywhere else in this documentation set that disagrees with the block below is a
defect in that document.**

The block is machine-readable on purpose, so a reader can diff it against a fresh run rather than
re-reading paragraphs, and so the next person to change it has to change one thing:

```json
{
  "recordVersion": 1,
  "measuredOn": "2026-08-13",
  "environment": {
    "dotnetSdk": "10.0.302",
    "netCoreAppRuntime": "10.0.10",
    "aspNetCoreAppRuntime": "10.0.10",
    "targetFramework": "net10.0",
    "docker": "29.7.0",
    "dockerStorageDriver": "overlay2",
    "host": "linux-x64 container"
  },
  "build": {
    "command": "dotnet build PowerFramework.slnx -c Release",
    "result": "Build succeeded.",
    "warnings": 0,
    "errors": 0,
    "projects": 20
  },
  "tests": {
    "commandPerService": "cd services/<service-name> && dotnet test -c Release --collect:\"XPlat Code Coverage\"",
    "commandPerSharedProject": "cd shared/<project>.Tests && dotnet test -c Release",
    "projects": [
      { "project": "shared/PowerFramework.Shared.Kernel.Tests",                      "passed": 1750, "skipped": 0, "failed": 0 },
      { "project": "shared/PowerFramework.Shared.Diagnostics.Tests",                 "passed":  637, "skipped": 0, "failed": 0 },
      { "project": "shared/PowerFramework.Shared.Eventful.Tests",                    "passed":  777, "skipped": 0, "failed": 0 },
      { "project": "shared/PowerFramework.Shared.Localization.Tests",                "passed":  526, "skipped": 0, "failed": 0 },
      { "project": "shared/PowerFramework.Shared.Containers.Tests",                  "passed":  202, "skipped": 0, "failed": 0 },
      { "project": "shared/PowerFramework.Contracts.Tests",                          "passed": 4671, "skipped": 0, "failed": 0 },
      { "project": "services/gateway-service/PowerFramework.Gateway.Tests",          "passed": 1044, "skipped": 0, "failed": 0 },
      { "project": "services/dataservices-service/PowerFramework.DataServices.Tests","passed": 5745, "skipped": 0, "failed": 0 },
      { "project": "services/persistence-service/PowerFramework.Persistence.Tests",  "passed": 4506, "skipped": 0, "failed": 0 },
      { "project": "services/security-service/PowerFramework.Security.Tests",        "passed": 2052, "skipped": 0, "failed": 0 }
    ],
    "sharedSubtotalPassed": 8563,
    "serviceSubtotalPassed": 13347,
    "totalPassed": 21910,
    "totalSkipped": 0,
    "totalFailed": 0,
    "skipReason": "None. NOTHING SKIPS. The pinyin oracle characterization hooks used to skip unless a paired legacy recording existed, which meant they protected nothing on the shipped tree; they now assert an equivalence true in both worlds - characterized if and only if recorded - so they execute on every run and assert today's BLOCKED state, and they fail the day a recording lands without being wired in."
  },
  "coverage": {
    "gate": "0.80 line rate, per service, from that service's own coverage.cobertura.xml",
    "scopedBy": "the gate selects this service's own package from the report by assembly name, inline in .github/workflows/ci.yml; no settings file exists anywhere in this repository",
    "perService": [
      { "service": "gateway-service",      "assembly": "PowerFramework.Gateway",      "lineRate": 0.8908, "linesCovered":  4205, "linesValid":  4720, "branchRate": 0.7361, "packagesInReport": 5 },
      { "service": "dataservices-service", "assembly": "PowerFramework.DataServices", "lineRate": 0.9323, "linesCovered": 18466, "linesValid": 19805, "branchRate": 0.8354, "packagesInReport": 7 },
      { "service": "persistence-service",  "assembly": "PowerFramework.Persistence",  "lineRate": 0.9175, "linesCovered": 12833, "linesValid": 13986, "branchRate": 0.8300, "packagesInReport": 6 },
      { "service": "security-service",     "assembly": "PowerFramework.Security",     "lineRate": 0.9174, "linesCovered":  4266, "linesValid":  4650, "branchRate": 0.8113, "packagesInReport": 3 }
    ],
    "allFourClearTheFloor": true
  },
  "containers": {
    "imageBuildCommand": "docker build -f services/<service-name>/Dockerfile -t <tag> .",
    "imagesBuilt": ["security-service", "persistence-service", "dataservices-service", "gateway-service"],
    "imagesBuiltCount": 4,
    "bringUpCommand": "docker compose -f orchestration/docker-compose.yml --env-file <file outside the working tree> up -d",
    "bringUpOperatorPrecondition": "The manifest declares three top-level Compose secrets sourced from TLS_CERTIFICATE_PATH, TLS_CERTIFICATE_KEY_PATH and INTERNAL_TLS_CA_PATH and projects them read-only at /run/secrets/internal-tls/{server.crt,server.key,ca.crt} in all four services; all three sources carry the :? form, so an unset or absent path aborts bring-up by name. The persistence-db volume needs no operator step - the schema provisioner applies pending migrations at startup.",
    "allFourReachedDockerHealthy": true,
    "healthOrderObserved": ["security-service", "persistence-service", "dataservices-service", "gateway-service"],
    "observed": {
      "healthAnonymous200": [5101, 5102, 5104, 5105],
      "pingWithoutToken401": [5101, 5102, 5104, 5105],
      "pingWithToken200": [5102, 5105],
      "gatewayAggregateNamedUpstreamsHealthy": ["persistence", "dataservices", "security"],
      "tokenMintedByScheme": ["clientCredential", "mutualTls"],
      "outOfRosterAudienceRefused": 403,
      "jwksAndDiscoveryAnonymous200": true,
      "deferredRoutes501": ["/v1/design/**", "/v1/documents/**", "/v1/integration/**", "/v1/scripting/**"],
      "failFastConfirmed": [
        "An unreadable client-CA made Security refuse to start, with a names-only message publishing no path.",
        "A fresh persistence-db volume made Persistence answer 503 naming the unprovisioned database until the migration was applied, and the dependency chain held DataServices and Gateway back."
      ]
    }
  },
  "notVerified": [
    "Behavioural parity against the PowerBuilder oracle. characterization/recordings/ is empty on both sides; the oracle needs the PowerBuilder runtime, which this environment does not have.",
    "The CI workflow as a pipeline. .github/workflows/ci.yml is authored and its gate is expressed, but it has not run on a GitHub-hosted runner; the gate's arithmetic was checked by running the same commands locally.",
    "A full Playwright end-to-end run, which needs an issuance identity provisioned against a live stack."
  ]
}
```

Three things about that record deserve saying in prose, because they are the parts a reader is most
likely to misread.

**A coverage report carries one package per instrumented assembly, and the gate SELECTS the service's own
by name.** That selection is the measurement-scope filter, and it is what makes the number trustworthy
rather than merely plausible: a service's test project reaches its application project and, through it, the
shared libraries and the generated protocol stubs that arrive by `ProjectReference`, so each report carries
several packages and `packagesInReport` above records how many. The CI gate of §10 therefore checks three
things, not one: that exactly one report *file* was produced, so it is unambiguous which one is read; that
the service's own assembly appears among the packages at all, because a leg whose report does not name it
measured something other than the service it claims to; and that THAT package's line rate clears the floor.
Every other package's rate is printed and never gated on — holding a service's verdict to the shared
libraries' rate would be the masking C-H forbids, in the other direction. The report's own top-level rate
is not the gate and must never be read as one.

**"All four containers healthy" is a claim about the dependency chain, not about four independent
processes.** The order in `healthOrderObserved` is the order Compose opened the gates in, and it is the
chain §8 describes: Security first because it is the root of the trust graph and depends on nothing;
Gateway last because it reports healthy only once its three upstreams do. A stack where all four happened
to be healthy but the conditions were absent would look identical in `docker compose ps` and prove
nothing.

**What an in-process test host still does not do.** Every service test drives its own service in process,
through `WebApplicationFactory` or a test host, so handler behaviour, status translation, the capability
gate, the reserved routes and token issuance are covered by passing tests — but an in-process host performs
no TLS handshake, no ALPN negotiation, no real gRPC channel setup and no client-certificate exchange.
Those gaps are closed by the container evidence above rather than by any test, and the distinction is worth
keeping: a passing test suite and a healthy stack are different kinds of evidence about different things.

### 1.4 No performance claim appears in this document

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment anywhere, so none is asserted here — not for the build, not for the test run,
not for any service (C-B). The only quantitative non-functional requirement in the whole brief is the
80% line-coverage gate of §10. Where a package inclusion needs justifying, the justification given is a
**correctness** argument; see §11.5.

### 1.5 Nothing in the .NET build touches the legacy tree

The legacy PowerBuilder tree is read-only and is the behavioural oracle (C-C). No build, test, container
or CI step defined in this document reads it as an input, writes to it, or requires it to be present:

- `ws_objects/**`, the `*.pbl`/`*.pbt`/`*.pbw`/`*.pbr`/`*.pbd` artifacts, `oldversion/125/**` and
  `pack/**` are inputs to **parity work only**, never to the build. See [`docs/PARITY.md`](PARITY.md).
- The two PowerBuilder project objects are cited in §12 as **REFERENCE for build *intent* only**. They
  are not translated, and §12 shows why neither could be.
- The five pre-existing Chinese documents in this folder — `docs/README.md`, `docs/Blink交互.md`,
  `docs/Sciter交互.md`, `docs/PB多线程绕坑提示.md` and `docs/n_cst_dwsvc_columnexp.md` — are read-only
  reference. They are not edited, translated, re-encoded, renamed or link-rewritten. This document is
  purely additive alongside them.

### 1.6 Deviations from the attached environment's setup instructions

The attached environment's setup instructions are **binding operational constraints** (C-L), so every
place the delivered build departs from them is enumerated here rather than left for a reader to discover
by running a command that fails. Five deviations are recorded in the migration plan itself (§0.8.3); two
more are recorded here, because they are properties of the delivered code that the plan's own register did
not carry, and both were found by running the instructions verbatim and watching them fail.

| # | What the instructions say | What the delivered system requires | Why |
| --- | --- | --- | --- |
| **D6** | Generate each signing key with `openssl rand -base64 32` | An **RSA private key** — see §8.1 for the exact `openssl genpkey` form | Security signs **RS256** and publishes an **RSA-only** JWK set, so symmetric random bytes cannot be imported and cannot be published as an RSA JWK. The instruction produces material the configured algorithm cannot use, and the host **refuses to start** on it. Answering that failure by switching to an HMAC family would publish the signing secret itself to all three verifiers, since the key set is anonymous. **The key is additionally required to be at least 2048 bits** (§8.1) |
| **D7** | Probe readiness with `curl -sf http://localhost:<port>/health`, and reach the composition root at `http://localhost:5105` | `https://` on every port, and a trusted CA for the local certificate: `curl -sf --cacert <ca> https://localhost:<port>/health`, composition root at `https://localhost:5105` | Every service binds **HTTPS only** — there is no plaintext listener on any port, by design rather than by omission (`ARCHITECTURE.md` §9.4). A plaintext probe therefore fails at the transport layer, before any handler runs, and reports nothing about readiness. §7.1 and §8.2 carry the corrected forms; the container probes use `openssl s_client` for the same reason, and §7.1 explains why a bash `/dev/tcp` write cannot work against a TLS listener |

Everything else in the instructions is honoured without deviation: the per-service build-command shape
(§5.1, run verbatim), the Compose bring-up shape with its example environment file (§8.1), the anonymous
health and authenticated ping contract with `401` absent a token (§8.2), the gateway-reports-healthy-only-
after-its-upstreams gate (§8.2), the 5101–5105 port band with Gateway on 5105 (§8.2), the end-to-end test
directory and its install-and-run path (§9), and the read-only status of the legacy tree (§1.5).

**Neither deviation is a choice this build made to be different.** Each is a consequence of a requirement
the instructions and the plan both impose — RS256 with an anonymously published key set, and an
authenticated transport on every new boundary (C-G) — so honouring the instruction literally would break
the requirement it exists to serve. That is the test applied to both: a deviation is documented when the
instruction and the constraint cannot both be satisfied, never merely because another form was more
convenient.

---

## 2. The two findings that must reach every reader

Both were observed directly. Each breaks the build if it is ignored.

### ⚠️ Finding 1 — `.slnx` is the .NET 10 solution format, and solution filters are not a supported path here

**.NET 10's `dotnet new sln` emits `.slnx`**, the XML solution format — not `.sln`. Reproduce it in a
throwaway directory, never in your working tree:

```bash
set -euo pipefail
probe="$(mktemp -d)"
( cd "$probe" && dotnet new sln -n Probe && ls Probe.* )
rm -rf -- "$probe"
# -> Probe.slnx
```

> **Run this in `mktemp -d`, never where you are standing.** `dotnet new sln` writes into the *current*
> directory, so running it at the repository root creates a second root solution — and a directory with
> two solution files breaks every bare `dotnet restore`, `dotnet build` and `dotnet test` with
> `MSB1011`, which is the exact failure §3.4 tells you to avoid. The subshell keeps the `cd` from
> outliving the snippet, and the trailing `rm -rf --` removes only the temporary directory the command
> above created. If you would rather not run it at all, the output shown in the comment is the whole
> result.

Every solution file in this repository is consequently `.slnx`: the root `PowerFramework.slnx` of §6 and
the four per-service solutions of §3.4.

**Legacy solution filters (`.slnf`) are incompatible with the `.slnx` format.** A hand-authored filter
pointing at a `.slnx`-format solution **fails with `MSB4014` during restore, during build *and* during
test** — all three verbs, not just one, so a filter that appears to have restored will still fail later.

**There is no supported filter path in this repository. Do not author one, now or as a follow-up.**

This prohibition is recorded in four places that all say the same thing, so that whichever file you are
reading when the idea occurs to you, it tells you not to:

| Where | What it records |
| --- | --- |
| This document | The build commands at length, and the diagnostics below |
| `PowerFramework.slnx` | The format rule — `.slnx`, never a `.sln`, never a `.slnf` |
| `Directory.Build.props` | Solution-filter mechanisms among the things deliberately not configured |
| `.editorconfig` | Its MSBuild-file section notes that no filter is authored for the format to match |

**Additional diagnostics you may encounter on the pinned SDK.** Recorded because a build
reference should tell you what you will actually see, and because these confirm the prohibition rather
than soften it — a misused filter fails in **all three verbs** in every case:

| Situation | Diagnostic |
| --- | --- |
| The filter lists a project that does not match the solution's project list | **`MSB4025`** — `InvalidProjectFileException` raised from `SolutionFile.ValidateProjectsInSolutionFilter()` |
| The filter names a solution file that does not exist | **`MSB5026`** |

The practical point behind every one of these codes is the same: a filter's project list has to be kept
in exact agreement with the solution **by hand**, and any drift fails hard. That maintenance would be
spent reproducing a scoping the per-service solutions already give for free, by construction.

**The resolution is the per-service `.slnx` design of §3.4.** Each service directory carries exactly one
solution file listing exactly that service's projects, so scoping is structural rather than curated. Use
those. Any agent or developer who reaches for a solution filter will break the build.

### ⚠️ Finding 2 — bare `dotnet test` builds **Debug**, even immediately after a Release build

`dotnet test` with no configuration flag builds and tests **Debug**, even when it directly follows
`dotnet build -c Release`. It does not inherit the preceding command's configuration.

Observed, in exactly this order, in the skeleton of §1.2:

```text
$ dotnet build -c Release          # bin/ now contains: Release
$ dotnet test --collect:"XPlat Code Coverage"
  Probe.Tests -> .../Probe.Tests/bin/Debug/net10.0/Probe.Tests.dll      # <-- Debug
Test run for .../Probe.Tests/bin/Debug/net10.0/Probe.Tests.dll (.NETCoreApp,Version=v10.0)
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1
                                   # bin/ now contains: Debug  AND  Release
```

**Why this matters:** a reader who assumes the preceding Release build is what gets tested will be
measuring coverage against a **Debug** build without being told.

Both forms are therefore documented, and which is which is stated:

```text
# THE VERBATIM DOCUMENTED FORM - preserved exactly as the environment specifies it (C-L).
# Fenced `text` because <service-name> is a placeholder, not shell syntax; see the note in the intro.
# Note: the test step builds and measures DEBUG.
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

```bash
# THE RELEASE-CONFIGURATION VARIANT - use when coverage must be measured against the Release build.
# Recorded ALONGSIDE the verbatim form, never in place of it.
dotnet test -c Release --collect:"XPlat Code Coverage"
```

The variant was confirmed to test `bin/Release/net10.0` and to emit `coverage.cobertura.xml` just as the
verbatim form does.

---

## 3. The build architecture, and why it is shaped this way

Three components make the single command of §5 work. Each is load-bearing; none is stylistic.

### 3.1 Repository-root `Directory.Build.props` — one settings source for all twenty projects

MSBuild imports this file automatically into every project in the tree — **all twenty of them,
application and test projects alike** — so the per-service command inherits identical settings with no
per-service duplication. That is the mechanism behind per-service build independence (C-I): there is no
way for one service's build to be configured differently from another's by accident.

| Property | Value | Why it is set here |
| --- | --- | --- |
| `TargetFramework` | `net10.0` | The validated target framework; nothing multi-targets, so the singular property is correct |
| `LangVersion` | `latest` | Resolves to **C# 14** on this SDK. Safe to leave floating precisely because `global.json` pins the SDK band, so it cannot silently cross a major language version |
| `Nullable` | `enable` | Part of the enterprise baseline (§1.1), not a per-project preference. The ported return-code algebra is explicitly tri-state and depends on null being representable and visible |
| `ImplicitUsings` | `enable` | Applied uniformly, applications and tests alike |
| `TreatWarningsAsErrors` | `true` | See below — this is a quality gate with three consequences |
| `EnableNETAnalyzers` | `true` | States the SDK's own default explicitly, because a warnings-as-errors gate is only reproducible if the diagnostic set producing those errors is stated rather than inferred |
| `AnalysisLevel` | `latest` | As above; bounded by the SDK pin, so the rule set cannot shift under the gate |
| `ManagePackageVersionsCentrally` | `true` | Enables the mechanism of §3.2 |
| `CentralPackageVersionOverrideEnabled` | `false` | Closes the one hole §3.2's two guards leave open. `NU1008` catches a bare `Version` attribute and `NU1010` catches a package with no central entry, but a `VersionOverride` is neither, so it restores cleanly while re-establishing a per-project version authority. With this set, any `VersionOverride` anywhere in the tree is a hard restore error (**`NU1013`**) |
| `ShouldUnsetParentConfigurationAndPlatform` | `false` | Makes `dotnet build -c Release` mean Release for a service's **whole** dependency closure, not only for the two projects its solution lists. See §3.3 |

It builds **warning-clean** against both the ASP.NET Core web template and xunit.v3 — verified, not
assumed.

`TreatWarningsAsErrors` has three consequences worth stating, because each surfaces elsewhere:

1. **It is what makes the NuGet dependency audit fatal rather than advisory.** The remedy for the
   advisories it exposes is the pair of mandatory pins in §11.1, not a suppression.
2. **It applies to analyzer diagnostics shipped with packages, not only to compiler diagnostics.**
   Concretely, the xunit.v3 diagnostic raised when an awaited call that accepts a cancellation token is
   not given the ambient test cancellation token is an **error** in this repository. Test authors must
   pass it rather than expect a warning.
   Its boundary is worth knowing precisely: the property becomes the compiler switch `warnaserror+`, so
   it governs **compiler and analyzer** diagnostics only. The **MSBuild task** warning channel is
   separate and stays warnings — `MSB9008`, raised when a `ProjectReference` names a project file that
   does not exist, is the one to expect while the tree is still being filled in. **Read the build log
   rather than trusting the exit code alone**, and resolve an `MSB9008` by creating the referenced
   project or correcting the reference.
3. **It is exactly why the repository-root `.editorconfig` carries scoped naming-analyzer
   suppressions.** This refactor deliberately preserves the legacy SCREAMING_SNAKE constant identifier
   spellings verbatim — `RetCode.OK`, `RetCode.E_INVALID_ARGUMENT`, `RetCode.CANCELLED`,
   `Enums.SQL_MS_REPLACE`, `Enums.INIT_FLAG_ENABLE_SQLITE`, `Categories.CAT_DWSVC` and their kin —
   because those exact spellings appear in serialized payloads, in log records and in characterization
   recordings, where a rename would silently invalidate every stored comparison. Under this gate the
   naming analyzer would turn each of them into a **build error**, so `CA1707` and `IDE1006` are set to
   `none` in `.editorconfig` — **scoped by file glob to the individually named files that genuinely carry
   those identifiers**, deliberately not applied globally. That file's BAND 3 roster is the single source
   of truth for which files those are, which is why no count is restated here or in any of the source
   comments that point at it. Adding a new file with such identifiers requires adding its own scoped
   section AND an entry on that roster; it will otherwise fail the build. See [`docs/PARITY.md`](PARITY.md) for why
   the spellings are preserved.

### 3.2 Repository-root `Directory.Packages.props` — central package management is mandatory

Central package management is **mandatory here, not stylistic.** All package versions live in this one
file as `PackageVersion` entries, and **every `PackageReference` in every `.csproj` is versionless** —
no `Version` attribute and no `VersionOverride` attribute anywhere in the twenty project files, which
is checkable in one command:

```bash
# Expect no output. Any hit is a project supplying its own version and defeating central management.
grep -rnE '<PackageReference[^>]*(Version|VersionOverride)=' --include='*.csproj' shared services
```

```xml
<!-- In any project file: NO Version attribute, and NO VersionOverride attribute. -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" />
```

```xml
<!-- In Directory.Packages.props, once, for the whole repository: -->
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.11" />
```

**The reason is drift prevention.** This is the mechanism that forces the two mandatory security pins of
§11.1 onto all four services **from one place, so no service can drift** onto a vulnerable or
incompatible version. A per-project version attribute would reintroduce exactly the divergence the pins
exist to prevent.

Two practical consequences:

- Adding a version attribute to a `PackageReference` while central management is enabled is an error, not
  a local override.
- Referencing a package that has **no** `PackageVersion` entry in `Directory.Packages.props` fails
  restore with **`NU1010`**. The fix is to add the entry centrally — never to add a version locally.
- **`VersionOverride` is not the escape hatch it looks like, and it no longer restores at all.** It used
  to slip past both guards above and build cleanly, so it announced nothing, while taking that one
  package's version out of the central manifest: nothing pinned it in the one authoritative place, and
  anyone auditing versions or third-party licences from `Directory.Packages.props` — including the root
  `NOTICE` — would not see the package at all. One reference did carry it
  (`Microsoft.OpenApi.YamlReader` in `shared/PowerFramework.Contracts.Tests`); it was removed, the
  version moved into `Directory.Packages.props`, and the package added to `NOTICE`. The bypass is now
  closed mechanically rather than by convention: `CentralPackageVersionOverrideEnabled` is `false`, so
  an override fails restore with **`NU1013`** — *"The following PackageReference items cannot specify a
  value for VersionOverride … configured to disable this functionality."* Verified by reinstating one
  deliberately. If a project genuinely needs a version no other project can take, that is a
  conversation about the pin, not a reason to bypass the manifest.

`Directory.Packages.props` therefore contains **`PackageVersion` entries and nothing else** — no
`PropertyGroup`, and in particular no restatement of either switch above. A switch declared in two files
is two executable declarations of one setting, with the losing copy silently inert; both live in
`Directory.Build.props`, which is the only place changing one has any effect.

### 3.3 Configuration propagation across the solution boundary

Because each service solution lists only its two own projects (§3.4), the shared projects it consumes are
reached purely by `ProjectReference`. MSBuild's default behaviour when building through a solution is to
**unset** the parent configuration for any referenced project it cannot find in the solution
configuration table, so that project falls back to its own default.

Measured **before this was addressed**: `dotnet build -c Release` in a service directory
built the two listed projects into `bin/Release` while every shared project it references landed in
`bin/Debug` — and those Debug assemblies were then copied into the application's Release output. The
command reported a clean Release build throughout, so nothing surfaced the mismatch.

That is a real defect, not a quirk: it makes the meaning of `-c Release` depend on solution membership,
so the same command yields a different artifact depending on which solution resolved it.

Two things fix it, both at the repository root:

- **`ShouldUnsetParentConfigurationAndPlatform` is `false`**, so the building project's configuration is
  passed through to a referenced project the solution does not list. Re-measured after the change: every
  shared project lands in `bin/Release`, with zero warnings. Solution *members* are unaffected, because
  the solution configuration still maps them explicitly — so the root solution of §6 behaves as before.
- **An assertion target, `PowerFrameworkAssertProjectReferenceConfiguration`**, runs after project
  references are resolved and **fails the build with `PFW0001`** if any referenced output was not
  produced by the configuration being built. Verified both ways: silent across the whole tree with
  propagation on, and failing with the offending assembly paths named when propagation is forced off.

Neither of the two obvious "corrections" is right, and both are called out at the point of temptation in
the service solution files. Do **not** add the shared projects to a service solution — C-A forbids it and
it is now unnecessary. Do **not** override `ShouldUnsetParentConfigurationAndPlatform` back to `true`;
`PFW0001` exists so that doing so fails loudly instead of silently.

This is a different matter from Finding 2 in §2, which is about a bare `dotnet test` choosing Debug. That
finding still stands: this property never *chooses* a configuration, it only stops MSBuild from
discarding the one the caller already chose.

### 3.4 Per-service `services/<service-name>/<service-name>.slnx` — and why exactly one is load-bearing

Each service directory carries exactly one solution file, listing exactly that service's application and
test projects:

```xml
<!-- services/gateway-service/gateway-service.slnx -->
<Solution>
  <Project Path="PowerFramework.Gateway/PowerFramework.Gateway.csproj" />
  <Project Path="PowerFramework.Gateway.Tests/PowerFramework.Gateway.Tests.csproj" />
</Solution>
```

**With exactly one solution file in the directory, bare `dotnet restore`, `dotnet build` and
`dotnet test` resolve it automatically.** That is precisely what makes the parameterised
`cd services/<service-name> && …` command of §5 work verbatim, with no `-p`, no `--project` and no path
argument — and it is what satisfies the requirement that each service build and test independently from a
clean checkout (C-I). Transitive `shared/` project references are restored along with it; the service
solution does not need to enumerate them.

> **"Exactly one solution file per service directory" is load-bearing.** Add a second and the bare
> commands stop resolving anything. Verified: with two solution files present, `dotnet build` fails with
>
> ```text
> MSBUILD : error MSB1011: Specify which project or solution file to use because this folder
> contains more than one project or solution file.
> ```
>
> Removing the second file restores the bare command immediately. Do not add a second solution file, a
> `.sln`, or a solution filter (§2, Finding 1) to a service directory.

This four-solution layout is also the structural answer to C-A: the four per-service solutions are the
**load-bearing build unit**, and the root solution of §6 is a convenience layered on top of them, not a
replacement for them. A single collapsed solution would be a failure mode, not a cautious choice.

---

## 4. Prerequisites

| Requirement | Version | Needed for |
| --- | --- | --- |
| .NET SDK | **10.0.303** | Everything. Pinned by repository-root `global.json`. It is the SDK of the 2026-08-11 security release of the 10.0 channel (runtime **10.0.11**) — see §11.3 |
| Node.js | **`>=22.12.0`**, declared as the floor in `tests/e2e/package.json`; v22.23.2 verified on the authoring host | The end-to-end tests of §9 **only** |
| npm | **`npm@11.18.0`**, pinned as `packageManager` in `tests/e2e/package.json`; verified sufficient on the authoring host | The end-to-end tests of §9 **only** |

The Node floor is **>= 22.12.0** rather than the looser `>= 22.0.0` an earlier manifest declared, and
rather than `@playwright/test`'s own `>= 20`. It is set to the version the environment's setup
documents as its requirement, so that a host satisfying `engines` also satisfies the environment: a
manifest whose floor is *below* the environment's would let `npm ci` succeed on a host the environment
considers unsupported, which is a check that passes without checking anything.
| Docker + Compose v2 | — | The Compose path of §8 **only**. Nothing else in this document needs it |

The SDK pin, in full:

```json
{
  "sdk": {
    "version": "10.0.303",
    "rollForward": "latestFeature"
  }
}
```

`rollForward: latestFeature` confines roll-forward to 10.0 feature bands. That is deliberate and it is
what makes the floating `LangVersion` and `AnalysisLevel` of §3.1 safe: the band cannot cross a major
language version or silently change the analyzer rule set beneath the warnings-as-errors gate.

**Nothing in the .NET tree depends on the PowerBuilder toolchain, the PowerBuilder runtime, or any of the
shipped native binaries.** This is worth confirming explicitly for a reader who sees `pfw.dll`,
`pfwx.dll`, `sciter.dll`, `blink.dll` or `sqlite3.dll` sitting in the checkout root: no restore, build,
test, container or CI step in this document loads, links against, or requires any of them. They are part
of the read-only oracle (§1.5), and `.dockerignore` keeps them out of every image layer (§7.3).

---

## 5. Per-service build and test — the primary path

This is the primary build path, and it is the path that must work from a clean checkout for each service
independently (C-I). It is also the path the CI workflow of §10 runs, in four independent matrix legs —
that workflow is present in the tree, and §10 is precise about which parts of it have been run here.

### 5.1 The verbatim command

Preserved exactly as the attached environment specifies it (C-L), and therefore fenced `text`: the
placeholder is not shell syntax, and a block that cannot be run must not claim it can.

```text
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

The same command with `<service-name>` replaced by a real shell variable, which **does** run verbatim —
set the variable to any one of the four directory names in §5.2:

```bash
set -euo pipefail
service=gateway-service   # or dataservices-service, persistence-service, security-service
cd "services/$service"
dotnet restore
dotnet build -c Release
dotnet test --collect:"XPlat Code Coverage"
```

Recall Finding 2 (§2): the `dotnet test` step in this form builds and measures **Debug**, not the Release
build produced by the preceding step.

### 5.2 The four services, concretely

Service directory names, project names and ports are identical to those in
[`ARCHITECTURE.md`](ARCHITECTURE.md), and the root [`README.md`](../README.md) carries the same roster with
the same ports, so the two can be cross-checked against each other.

| Service directory | Application project | Test project | Port | Listener |
| --- | --- | --- | --- | --- |
| `services/persistence-service` | `PowerFramework.Persistence` | `PowerFramework.Persistence.Tests` | **5101** | `https://+:5101`, `Http1AndHttp2` — gRPC C-05..C-08, REST and the readiness probe |
| `services/dataservices-service` | `PowerFramework.DataServices` | `PowerFramework.DataServices.Tests` | **5102** | `https://+:5102`, `Http1AndHttp2` — gRPC C-03/C-04, REST, the readiness probe and the thin projection |
| *(reserved)* | — | — | **5103** | commented-out Phase-2 slot |
| `services/security-service` | `PowerFramework.Security` | `PowerFramework.Security.Tests` | **5104** | `https://+:5104`, `Http1` |
| `services/gateway-service` | `PowerFramework.Gateway` | `PowerFramework.Gateway.Tests` | **5105** | `https://+:5105`, `Http1` |

Port 5103 is left reserved rather than reassigned; see [`ARCHITECTURE.md`](ARCHITECTURE.md) for the
reasoning and for the transport chosen per service.

**EVERY LISTENER TERMINATES TLS, EACH SERVICE BINDS EXACTLY ONE, AND FOR THE TWO GRPC-CARRYING SERVICES
TLS IS WHAT MAKES ONE ENOUGH.** TLS is not hardening here: every boundary in this list is created by the
decomposition itself, every request across one carries a bearer token, and Security additionally publishes
the key set the whole estate verifies against — so a cleartext listener would make every token replayable
and the key set substitutable (CWE-319). It is also a functional dependency on 5101 and 5102, because
`Http1AndHttp2` is resolved by TLS application-protocol negotiation: over TLS one endpoint serves an
HTTP/1.1 probe and an HTTP/2 gRPC call, while on cleartext Kestrel disables HTTP/2 outright and serves
HTTP/1.1 only — which would take every gRPC contract off the air while `/health` kept answering 200.

**Three earlier revisions of this arrangement are recorded because all three were withdrawn.** One put a
second listener per service on a parallel 5151–5155 band plus a third for the token endpoint; one declared
TLS everywhere and rewrote the documented gate's scheme to match; and one gave each gRPC-carrying service a
second `Http2`-only endpoint on 5111 and 5112 so that every listener pinned a single protocol version. The
first contradicted the fixed port map, the second made the documented bring-up unstartable, and the third
served published contracts on ports AAP 0.3.2.2 never names while the ports it does assign them carried
only the probe. None of this affects the build commands in this section;
[`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1 carries the map with its measurements, and it is the map a
*caller* must configure against.

```bash
# Gateway - the composition root and sole ingress
cd services/gateway-service && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

```bash
# DataServices
cd services/dataservices-service && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

```bash
# Persistence - the only service that generates or executes SQL
cd services/persistence-service && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

```bash
# Security - the sole JWT issuer
cd services/security-service && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

### 5.3 The release-configuration variant

Use this when coverage must be measured against the Release build (§2, Finding 2). It is recorded
**alongside** the verbatim form of §5.1, never in place of it. Template form first, then the runnable
form:

```text
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test -c Release --collect:"XPlat Code Coverage"
```

```bash
set -euo pipefail
service=gateway-service   # or dataservices-service, persistence-service, security-service
cd "services/$service"
dotnet restore
dotnet build -c Release
dotnet test -c Release --collect:"XPlat Code Coverage"
```

### 5.4 Where the coverage report lands

Either test form writes the coverage report to:

```text
services/<service-name>/<project>.Tests/TestResults/<run-guid>/coverage.cobertura.xml
```

`coverage.cobertura.xml` is the exact artifact the 80%-per-service gate of §10 reads, and every one of the
four services now produces one. The collector runs **unconfigured** — no settings file exists anywhere in
this repository and none may be added — so the report covers the shared libraries and the generated protobuf
stubs as well as the service, and carries **one Cobertura `package` per instrumented assembly**. Its
top-level rate is therefore **not** the per-service number. CI **selects the package named for that
service's own assembly** and reads its line rate, which is the measurement scope expressed inline in the
workflow; §5.5 gives both sets of figures. For what the coverage number is
expected to cover and which values are masked for determinism, see [`docs/PARITY.md`](PARITY.md).

### 5.5 What has actually been run in this repository

**All ten test projects build and pass, and the per-project counts live in the canonical record of
[§1.3](#13-the-canonical-verification-record) rather than here.** That is the one deliberate omission in
this subsection: a table of ten counts restated at its point of use is a table that drifts from the record
the moment either is edited, and this document set has already published three mutually contradictory
totals that way. Read §1.3 for the numbers; read on here for what they mean.

The whole-solution build reports `0 Warning(s)` and `0 Error(s)`. The `CS5001` an earlier revision of this
section reported for the four service application projects is no longer reachable: each now carries a
`Program.cs`.

**Nothing skips, and the one place that used to is worth recording.** The pinyin oracle characterization
hooks were `Skip`ped unless a paired legacy recording existed, so on the shipped state of this refactor
they contributed no executable protection at all. They now assert an EQUIVALENCE that is true in both
worlds — characterized if and only if recorded — so they execute on every run: today every row asserts the
BLOCKED state, including that the composition still hands the expression evaluator
`PinyinFirstLetterMatcher.Blocked`, which is what stops a third-party pinyin table or a guessed flag set
arriving unnoticed; the day a recording lands the same rows fail until it has been wired in. BLOCKED
remains the honest reportable outcome of the single parity risk [`PARITY.md`](PARITY.md) records, and it is
now asserted rather than skipped past.

**Why the same run yields two very different coverage numbers, and why that is the point.** The gated
figure is the `line-rate` of the one `package` in the report named for that service's own assembly. The
other figure is the report's own top-level rate over every package the test host loaded:

| # | Service | Assembly | Its own package | Report top level | Packages in the report |
| --- | --- | --- | ---: | ---: | ---: |
| 1 | `gateway-service` | `PowerFramework.Gateway` | **clears the floor** | 23.83% | 5 |
| 2 | `dataservices-service` | `PowerFramework.DataServices` | **clears the floor** | 61.26% | 7 |
| 3 | `persistence-service` | `PowerFramework.Persistence` | **clears the floor** | 38.58% | 6 |
| 4 | `security-service` | `PowerFramework.Security` | **clears the floor** | 13.54% | 3 |

The scoped rates themselves are in §1.3, for the reason above; what belongs here is the **comparison**,
because it is an argument rather than a measurement. All four services clear the 80% floor on their own
code, and **all four would fail** a gate that also counted the shared libraries and the thousands of
generated protobuf sequence points in `PowerFramework.Contracts` — which is why selecting the service's own
package by name is a correctness requirement of the gate and not a convenience. The package count in the
last column is the other half of that argument: the report carries several packages, only one of which is
the subject, and CI fails outright if that one is **absent** rather than silently reporting whatever else
was measured. Every other package's rate is printed by the gate and never gated on. Those six shared
projects are not thereby unchecked — CI's `solution` job builds the whole tree and runs all six of their
test suites.

**What the service numbers are and are not.** They are real service tests: each of the four drives its own
service **in process**, through `WebApplicationFactory` or a test host, so the handlers, the status
translation, the capability gate, the reserved routes and token issuance are all covered. They are **not**
evidence about a deployed topology — an in-process host performs no TLS handshake, no ALPN negotiation, no
real gRPC channel setup and no client-certificate exchange. What the deployed topology *has* shown is
reported in one place, and §1.3 names it.

### 5.6 Provisioning the Persistence database — automatic under Compose, manual otherwise

Persistence is the only service with storage. **Under the documented Compose bring-up it provisions its own
schema and no operator step is required**: `orchestration/docker-compose.yml` sets
`Schema__ApplyMigrationsOnStartup` to true (from `PERSISTENCE_APPLY_MIGRATIONS_ON_STARTUP`), so between its
startup gate and its request pipeline the service applies the pending migrations under `Data/Migrations/` by
calling `Database.Migrate` and nothing else, and then answers `/health` 200 — which is what opens the
dependency gate for DataServices and Gateway. `docker compose --env-file .env up --build -d` therefore
reaches a healthy stack from an empty volume in one command.

**The switch defaults to FALSE in the service's own `appsettings.json`, so anything that is not the Compose
stack still provisions manually.** A `dotnet run`, a `dotnet test` and any deployment that does not set the
variable behave exactly as they did before the switch existed. The asymmetry is deliberate: a
characterization operator must be able to rely on the volume being untouched, and the code default is the
opted-out one so that reliance never depends on remembering to disable something.

**What the automatic step is, precisely, and why it is safe to leave on.** It is `Database.Migrate` alone —
additive and idempotent, creating what the migration history table does not already record and dropping,
deleting and reseeding nothing. There is no `EnsureCreated`, no `EnsureDeleted` and no `DROP` anywhere in the
service, and `Data/SchemaProvisioner.cs` is held to that by a suite that scans its source. Concurrent
replicas are serialized by an exclusive lock file on the mounted volume, and a failure **terminates the
process** with a named cause rather than starting a service that could answer nothing.

**When the switch is off**, the service opens the database file, probes it, and reports `/health` as not
ready until the `COMPANY` table exists; every C-05 through C-08 verb then answers a defined refusal rather
than a fabricated success. Applying the migration is then a deployment step, and this is the command:

```bash
cd services/persistence-service/PowerFramework.Persistence
dotnet build -c Release
dotnet ef database update \
  --no-build \
  --configuration Release \
  -c PowerFrameworkDbContext \
  --connection "Data Source=<data-directory>/<database-file-name>"
```

Four things about it are not optional, and each is a property of this repository rather than a preference:

- **`--connection` is mandatory.** `PowerFrameworkDbContextFactory.CreateDbContext` calls `UseSqlite()`
  with no connection string on purpose — a design-time factory that embedded one would put a storage path
  in source. Supply the same directory and file name the service is configured with
  (`Sqlite:DataDirectory` and `Sqlite:DatabaseFileName`).
- **`--configuration Release` matches `--no-build`.** Without it the tool looks for a Debug assembly that
  a Release-only build has not produced.
- **`-c PowerFrameworkDbContext`** names the single context explicitly, so the command does not depend on
  discovery order.
- **It is idempotent and non-destructive.** Re-running it reports *"No migrations were applied. The
  database is already up to date."* and leaves existing rows byte-identical — the same property the startup
  path relies on, which is why the startup path is safe to leave on for everything except a capture.

**The journal mode the migration leaves behind, and why it no longer matters.** EF Core's migration lock
leaves the file in **WAL** journal mode, which is not the `DELETE` the legacy URI grammar defaults to
(`Sqlite:Journal`, from `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L452-L455`). Converting *out of*
WAL requires exclusive access, so with any other connection open on the file — a readiness probe is
enough — the conversion is impossible. The service therefore reads the mode in force, issues the pragma
only when it differs, and records a warning instead of failing the connection when the engine refuses the
conversion: the journal mode changes how the engine journals, not the result of any statement. Configure
`Sqlite:Journal=WAL` if you want the configured mode and the provisioned mode to agree and the warning to
disappear.

---

## 6. Whole-solution build — a developer convenience

The repository-root `PowerFramework.slnx` enumerates **twenty projects**: six shared libraries, their six
sibling test projects, the four service applications and their four test projects.

| # | Project | Kind |
| --- | --- | --- |
| 1 | `shared/PowerFramework.Shared.Kernel` | shared library |
| 2 | `shared/PowerFramework.Shared.Diagnostics` | shared library |
| 3 | `shared/PowerFramework.Shared.Eventful` | shared library |
| 4 | `shared/PowerFramework.Shared.Localization` | shared library |
| 5 | `shared/PowerFramework.Shared.Containers` | shared library |
| 6 | `shared/PowerFramework.Contracts` | shared library — the published boundary definitions |
| 7 | `shared/PowerFramework.Shared.Kernel.Tests` | tests |
| 8 | `shared/PowerFramework.Shared.Diagnostics.Tests` | tests |
| 9 | `shared/PowerFramework.Shared.Eventful.Tests` | tests |
| 10 | `shared/PowerFramework.Shared.Localization.Tests` | tests |
| 11 | `shared/PowerFramework.Shared.Containers.Tests` | tests |
| 12 | `shared/PowerFramework.Contracts.Tests` | tests |
| 13 | `services/gateway-service/PowerFramework.Gateway` | service application |
| 14 | `services/dataservices-service/PowerFramework.DataServices` | service application |
| 15 | `services/persistence-service/PowerFramework.Persistence` | service application |
| 16 | `services/security-service/PowerFramework.Security` | service application |
| 17 | `services/gateway-service/PowerFramework.Gateway.Tests` | tests |
| 18 | `services/dataservices-service/PowerFramework.DataServices.Tests` | tests |
| 19 | `services/persistence-service/PowerFramework.Persistence.Tests` | tests |
| 20 | `services/security-service/PowerFramework.Security.Tests` | tests |

From the repository root:

```bash
dotnet restore && dotnet build -c Release && dotnet test
```

> **Chained with `&&`, deliberately.** Run as three separate lines, a failed Release build is followed by
> a test run that succeeds against the *previous* Debug output — so the shell's last exit status is `0`
> and a broken build reads as a passing one. Reproduced deliberately to confirm it: unchained, the pair
> exits `0`; chained, it exits non-zero. Chaining makes the first failure the outcome, and the same
> applies to every multi-step snippet in this document, which is why they are all chained or guarded.
>
> **This is a developer convenience. It does not supersede the per-service solutions.**
>
> The four per-service solutions of §3.4 are what make independent per-service build and test work, and
> they are what CI exercises (C-A, C-I). The root solution exists so that a developer can open or build
> the whole tree in one step; it is never the path by which a service's independence is demonstrated. If
> the root build and a per-service build ever disagree, the per-service build is authoritative.

Note that `dotnet test` at the root is subject to Finding 2 (§2) exactly as the per-service form is: it
builds Debug. Add `-c Release` when that matters.

---

## 7. Container build

### 7.1 One image per service, multi-stage, non-root

> **ALL FOUR `Dockerfile`s ARE AUTHORED, AND ALL FOUR IMAGES BUILD.** `gateway-service`,
> `dataservices-service`, `persistence-service` and `security-service` are each a real file a reader can
> open, the probe table below reports what each one actually does rather than what it should do, and every
> one of the four has been built and started here as part of the four-service bring-up
> ([§1.3](#13-the-canonical-verification-record)). §7.2 in particular is a constraint the manifest and the
> definitions have to agree on, and it is the one most easily got wrong — all four definitions are authored
> against it, with every `COPY` path repository-root-relative, and the bring-up is what confirms they
> agree.

Each service has its own container definition at `services/<service-name>/Dockerfile`, each producing
**one image per service** — four images, matching the four independently deployable services (C-J). All
four exist today.

Every image is **multi-stage**: an SDK image (`mcr.microsoft.com/dotnet/sdk:10.0.400`) for restore and build,
and an ASP.NET runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0.11`) for the final stage. The final
stage runs as a **non-root** user, per the baseline of §1.1.

One runtime-image property is worth knowing before writing a health probe: the ASP.NET runtime image
ships **without `curl` and without `wget`** — but it **does** ship `/usr/bin/openssl`, because the .NET
TLS stack depends on it. Both facts were measured on `mcr.microsoft.com/dotnet/aspnet:10.0.11` rather than
assumed. A probe that expects `curl` without installing it can never succeed, the `service_healthy`
condition of §8 can never be satisfied, and the readiness gate silently never opens.

**Installing a tool is one answer and it is not the one taken: every listener in this estate terminates
TLS, so all four definitions build their probe from what the image already ships and install nothing.**

| Definition | Probe mechanism | Installs anything? | Why that choice |
| --- | --- | --- | --- |
| `gateway-service` | `openssl s_client` piped a hand-written request, matching the status line | **No** | The 5105 ingress is **TLS-terminated**, so `/dev/tcp` cannot perform the handshake and a probe built that way would fail permanently. `openssl`, `bash` and `printf` are already in the image |
| `dataservices-service` | `openssl s_client` piped a hand-written request, reading the first response line | **No** | The 5102 listener is **TLS-terminated** and `/dev/tcp` cannot perform a handshake — a probe built that way would fail permanently. `openssl` 3.0.13 is already present, so the handshake and the request need nothing installed |
| `security-service` | `openssl s_client`, the same way | **No** | The 5104 listener is TLS too. An earlier revision installed `curl` here; because the base image is referenced by FAMILY tag its package set advances with every security rebuild, so that dependency's version **cannot** be pinned without the build failing the moment the archive supersedes it — which collides with the baseline of §1.1. The definition documents the pipeline clause by clause, including why `-quiet` is required (it implies `-ign_eof`, without which the response is never read) and why no SNI is sent (the target is an IP literal) |
| `persistence-service` | `openssl s_client`, the same way, against 5101 | **No** | Its listener is TLS. An earlier revision installed `curl` here and it was **withdrawn** for exactly the reason recorded against Security one row above: an unpinnable apt version against a family-tagged base is a floating dependency, which collides with the baseline of §1.1. It also removed a runtime package from the one image in the system that holds a storage provider. There is no second listener to probe — 5101 carries the gRPC contracts and the probe alike, so 5101 answering proves the whole inbound surface is serving |

A compose manifest should **inherit** these `HEALTHCHECK` declarations rather than declare a `curl`-based
one of its own, which would reintroduce the missing-tool problem for every one of the four images. Every one
of the four verifies the presented chain against the mounted anchor; none passes `-k` or `--insecure`, because
a probe that skips verification reports healthy for a listener the rest of the stack cannot talk to.
[`ARCHITECTURE.md`](ARCHITECTURE.md) §10.3 carries the same table.

### 7.2 The build context is the repository root — not the service directory

This is the one thing about the container build that is easy to get wrong. The Compose manifest sets:

```yaml
build:
  context: ..
  dockerfile: services/<service-name>/Dockerfile
```

**The context is the repository root (`..` from `orchestration/`), while the Dockerfile lives in the
service directory.** The reason is structural: every service project references the shared libraries and
the generated contract stubs under `shared/`, and a Docker build cannot reach outside its own context.
**A per-service-directory context cannot see `shared/` and will fail to restore.** If you are debugging a
restore failure inside `docker build`, check the context before anything else.

### 7.3 `.dockerignore` — layer hygiene that doubles as a secrets control

A repository-root `.dockerignore` keeps the legacy tree and the native binaries out of every image layer:
`ws_objects/`, `oldversion/`, `pack/`, every `*.pbl`/`*.pbd`/`*.pbt`/`*.pbw`/`*.pbr`/`*.pbg`, the shipped
DLLs, `res/`, `samples/`, `sciter_control/`, the legacy browser-asset directories under `tests/`, plus
all build output (`**/bin/`, `**/obj/`, `**/TestResults/`).

Worth noting in passing: **this also functions as a secrets control.** The excluded legacy regions contain
most of the repository's in-source hardcoded-secret sites, and the file additionally excludes credential
shapes outright — `**/.env`, `**/.env.*`, `**/*.pem`, `**/*.key`, `**/*.pfx`, `**/*.p12`, `**/*.jks`,
`**/*.crt`, `**/*.cer`, `**/*.der`, `**/id_rsa`, `**/secrets.json`. So no image layer can carry them even
if such a file were present in a working tree. See [`SECRETS.md`](SECRETS.md) for the locator register and
the remediation posture.

---

## 8. Local orchestration

**Looking for a key name?** [§15](#15-per-service-configuration-keys) tabulates every configuration key
each of the four services actually binds. No two services spell the internal TLS anchor or the client
identity the same way, and there is no prefix that can be assumed, so consult that table rather than
inferring a name from a sibling service.

> ### ✅ EVERY COMMAND IN THIS SECTION RUNS AGAINST THE TREE AS IT STANDS
>
> An earlier revision of this callout said the opposite — that
> [`orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) did not exist and that every
> `docker compose` command below would fail at its first line. **The manifest exists**, alongside all
> four container definitions (§7.1) and [`orchestration/README.md`](../orchestration/README.md), and a
> bring-up from it has been run.
>
> **What that run covered, and what it did not, is stated in exactly one place:**
> [`orchestration/README.md`](../orchestration/README.md) §10, the only execution-status statement in this
> repository. This document defers to it rather than restating it — an execution claim restated in nine
> files is nine claims to keep true, and several of them had already drifted apart.
>
> §8.0 below is kept as a separate step for a different reason than it was written for: preparing the
> environment file and generating the local material are **prerequisites** of the bring-up, not the only
> runnable things here.

### 8.0 First — the environment file and the local material

Both steps below are prerequisites of the bring-up in §8.1. Neither starts anything.

```bash
set -euo pipefail
# The environment file is kept OUTSIDE the working tree -- see the warning below for why.
PFW_ENV="${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env"
install -d -m 700 "$(dirname "$PFW_ENV")"
# Guarded, because this file carries a filled-in signing key once it has been edited: seeding it a
# second time would overwrite that value. An explicit test is used rather than `cp -n`, which current
# coreutils warns is non-portable on every invocation.
if [ -e "$PFW_ENV" ]; then
  echo "Keeping the existing environment file at $PFW_ENV"
else
  cp orchestration/.env.example "$PFW_ENV"
  chmod 600 "$PFW_ENV"
  echo "Seeded $PFW_ENV from orchestration/.env.example"
fi
# Then fill in SECURITY_JWT_SIGNING_KEY and the certificate paths, using the roster below and the
# generation commands in ARCHITECTURE.md section 9.3.1.
```

### 8.1 The bring-up command

One hand-authored Compose manifest brings all four services up together (C-J), building the four container
definitions of §7.1 as it goes. From the repository root:

```bash
set -euo pipefail
cd orchestration
docker compose --env-file "${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/pfw.env" up --build -d
```

`--env-file` is used rather than a `.env` beside the manifest on purpose: `.gitignore` carries no `.env`
entry, so a filled-in file inside the working tree is one `git add -A` away from being committed. The
same reasoning drives the guarded seeding in §8.0 and the quick start in the root
[`README.md`](../README.md).

The template is `orchestration/.env.example`. It declares the JWT signing key by the variable name
**`SECURITY_JWT_SIGNING_KEY`** and nothing else as a signing secret, because Security is the sole token
issuer — the other three services hold verification material only. **No value for it appears in this
document, in `.env.example`, in any `appsettings.json` or in any container definition.** Generate one
locally.

**The active roster, typed — one variable carries material and the rest carry paths.** Conflating the two
kinds is the mistake this table exists to prevent:

| Variable | Kind | What it receives |
| --- | --- | --- |
| `SECURITY_JWT_SIGNING_KEY` | **Material, not a path** | The signing key **value**: base64 of the PKCS#8 DER encoding on one line, because the Compose dotenv format has no line continuation and a PEM block cannot be written there. PEM is also accepted, and tried first, for a secret store that can carry newlines |
| `TLS_CERTIFICATE_PATH` / `TLS_CERTIFICATE_KEY_PATH` | **Host** paths | The **shared multi-SAN server certificate and key**, on *your machine*. The manifest names them as the sources of two Compose secrets and projects both read-only into every container; the `Kestrel:Certificates:Default:Path` and `:KeyPath` each service binds are the **literal projected container paths**, not these values. All three TLS listeners terminate with the same default material |
| `INTERNAL_TLS_CA_PATH` | **Host** path | The authority that issued that server certificate, on your machine — the third Compose secret. Each service's own internal-anchor key points at the projected copy, so a locally issued chain verifies without touching platform trust. Left empty, the services fall back to platform trust |
| `GATEWAY_HOST_PORT`, `DATASERVICES_HOST_PORT`, `PERSISTENCE_HOST_PORT`, `SECURITY_HOST_PORT` | Numbers | The **host** side of each published port, defaulted to 5105, 5102, 5101 and 5104. Overriding them is what lets a second stack run beside the first — `orchestration/README.md` §6.3 carries the recipe; the container-side ports never move |
| `SECURITY_MTLS_CLIENT_CA_PATH` | Path | The authority whose client certificates Security accepts on `POST /v1/tokens`. It feeds **both** client-certificate anchors: `Security:MutualTls:ClientCaPath` directly, so the handshake completes, and `Security:ClientCertificateAuthorityPath` by adoption when that key is unset, so the certificate establishes an identity |
| `GATEWAY_MTLS_CERT_PATH` / `GATEWAY_MTLS_KEY_PATH` | Paths | Gateway's client certificate and key for the issuance edge |
| `DATASERVICES_MTLS_CERT_PATH` / `DATASERVICES_MTLS_KEY_PATH` | Paths | DataServices' client certificate and key for the same edge |

**`SECURITY_MTLS_CERT_PATH` and `SECURITY_MTLS_KEY_PATH` are not in that roster and must not be
reintroduced as service settings** — they belonged to a withdrawn second mutual-TLS listener. That
prohibition is about the .NET services and this roster, and it is *not* a prohibition on the names
themselves: `tests/e2e` reads exactly those two for the suite's **own client** pair
(`tests/e2e/fixtures/service-endpoints.ts`), which is the opposite half of the same handshake. The hazard
that creates is stated in [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1 and in `tests/e2e/README.md` §4.6;
neither surface may be reconciled by renaming the other's variable, because each is correct for its own
consumer. The mutual-TLS entries above are
**optional**, and that is the correction: `POST /v1/tokens` is protected by a caller credential and by no
bearer token — a caller cannot present a bearer token in order to obtain its first bearer token — and it
accepts **either** a shared secret as an HTTP `Basic` credential (`SECURITY_CLIENT_SECRET_GATEWAY` and
`SECURITY_CLIENT_SECRET_DATASERVICES` above, which the documented bring-up supplies) **or** a client
certificate. So a deployment that supplies the secrets and leaves all four certificate paths empty is a
supported one, and every service reports ready under it; a deployment presenting **neither** scheme is
refused at startup. **Persistence has no client pair and no secret**, because it reads Security's anonymous
key set and calls nothing else there. See [`SECRETS.md`](SECRETS.md) §4 for the token topology and the full handling
rule, and §4.1.1 there for what is and is not enforced about the signing key.

**What IS validated about that key, and what deliberately is not.** Security's `appsettings.json` carries
`Security:SigningKeyFormat` (`PemOrPkcs8Base64`), which `Configuration/SecurityOptions.cs` binds and
validates. It carries **no** minimum-size setting, and that omission is a requirement rather than an
oversight.

> ⚠ **THE SIZE FLOOR WAS WITHDRAWN, AND WITHDRAWING IT IS THE REQUIREMENT.** An intermediate revision
> declared `Security:SigningKeyMinimumSizeBits`, defaulted it to 2048, refused anything shorter at startup,
> and said so here and in §8.3. AAP §0.6.6.4 requires the legacy cryptographic defaults to be preserved
> **as annotated defaults**, and names 1024-bit RSA as one that "remains a legal key size"; §0.2.2.5
> forbids correcting a legacy weakness. A floor that refused a legacy-legal key was therefore a behaviour
> change dressed as hardening — and on the one service that mints, it turned a preserved allowance into a
> refusal to start. **The behaviour changed with the correction:** a 1024-bit RSA signing key now starts the
> host and mints tokens, and the host logs one warning naming the measured size when the key is below the
> 2048-bit annotation threshold.

- **The accepted format is a validated setting over a fixed acceptance sequence.**
  `Tokens/SigningKeyProvider.cs` always attempts the same closed sequence — PEM first, both PKCS#8 and the
  older PKCS#1, then base64 of the DER encoding — and no configuration reorders or extends it, so a value
  that is none of those shapes makes the host refuse to start because the import fails. Separately,
  `SecurityOptionsValidator` refuses any `SigningKeyFormat` outside the recognised set — which has exactly
  one member — by name at startup, so a deployment naming `Pkcs12` or `Jwk` is told so rather than ignored.
- **The 2048-bit figure is an ANNOTATION THRESHOLD, not a floor.**
  `SecurityOptions.LegacyWeakSigningKeySizeBits` is `2048`, and `Tokens/SigningKeyProvider` measures the
  imported modulus, exposes it as `SigningKeySizeBits`, sets `SigningKeyIsLegacyWeak` when it falls below
  that threshold, and **logs a warning** — nothing refuses, nothing narrows and nothing is silently
  substituted. `SecurityOptionsValidator` raises no size failure at any size, and there is no setting to
  configure. **A 1024-bit RSA key starts the host and mints**, with that warning in the log. What still
  fails closed is material that cannot be imported at all: an unusable value is reported as unusable and
  the host does not start. The generation command in §1.5 produces 2048 bits, so a deployment that follows
  it draws no remark; the `/v1/crypto` key-generation surface likewise accepts 1024 bits, preserving the
  same legacy allowance [`ws_objects/pfw.shared.pbl.src/enums.sru:L965`], and both are held to it by test.

**The signing key is an RSA private key, not random bytes.**
This is worth stating in a build document because getting it wrong produces a stack that starts and then
fails on its first token, with a cause nowhere near the symptom. Security's `appsettings.json` sets
`Security:SigningAlgorithm` to **RS256**, and
`shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` publishes an **RSA-only** key set at
`/.well-known/jwks.json` — `kty` `RSA` with the modulus and exponent members, and no symmetric member in
the schema at all. RS256 signs with an RSA private key, so a random symmetric string cannot sign it and
cannot be published as an RSA JWK. **An earlier revision of this section prescribed
`openssl rand -base64 32` for this variable. That instruction was wrong, produced material the configured
algorithm cannot use, and is corrected here.** No HMAC key-length guidance belongs on this variable
either, for the same reason.

Two identities are generated, because the signing identity and the transport identity are different keys
with different lifetimes. The signing key ends up in the environment file **as a value**; every
certificate and every private key stays on disk and is named **by path**:

```bash
set -euo pipefail
install -d -m 700 "$HOME/.config/powerframework/secrets"
cd "$HOME/.config/powerframework/secrets"

# 1. The RS256 SIGNING identity. SECURITY_JWT_SIGNING_KEY receives the base64 of this key's PKCS#8
#    DER encoding as its VALUE - it is not a path, because the dotenv format cannot hold a PEM block.
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out security-signing.key
openssl pkey -in security-signing.key -outform DER 2>/dev/null | base64 -w0 > security-signing.b64
# Paste the single line in security-signing.b64 after SECURITY_JWT_SIGNING_KEY= and never echo it.

# 2. The mutual-TLS trust anchor for POST /v1/tokens -> SECURITY_MTLS_CLIENT_CA_PATH. This one
#    variable is enough: Security reads it as the listener's anchor and, when
#    Security:ClientCertificateAuthorityPath is unset, adopts it as the issuance anchor too.
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=powerframework-local-ca" \
        -keyout mtls-ca.key -out mtls-ca.crt

# 3. The shared multi-SAN server certificate -> TLS_CERTIFICATE_PATH / TLS_CERTIFICATE_KEY_PATH, and
#    one client certificate per caller -> GATEWAY_MTLS_CERT_PATH / GATEWAY_MTLS_KEY_PATH and
#    DATASERVICES_MTLS_CERT_PATH / DATASERVICES_MTLS_KEY_PATH. Those commands are NOT duplicated
#    here: ARCHITECTURE.md section 9.3.1 carries them, including the subjectAltName set the server
#    certificate must cover and the -copy_extensions copyall that stops openssl dropping it.

# Name the files rather than globbing ./*.key. THE SERVER KEY MUST NOT BE 0600 -- Compose ignores
# `mode:`, `uid:` and `gid:` on a secret outside Swarm, so a 0600 host key arrives inside the container
# as `-rw------- root root` while every image runs as the unprivileged `app` account, and Kestrel then
# refuses to start for want of read permission. ARCHITECTURE.md section 9.3.1 step 4b sets the split:
# the server key 0644 inside a 0700 directory, every other key 0600.
chmod 600 security-signing.key security-signing.b64 mtls-ca.key
```

**One variable carries key material and the rest carry paths.** The Compose dotenv format has no line
continuation, so `SECURITY_JWT_SIGNING_KEY`'s shape is the single-line base64-of-DER value the second
command above produces rather than a PEM block; a secret store that can carry newlines may supply PEM
instead, and Security tries PEM first. An earlier revision of this section described that variable as a
mounted file path and named two withdrawn `SECURITY_MTLS_*` path variables — `.env.example` is the
authority, it declares a value for the signing key, and its mutual-TLS roster is the one in the table
above. **The public half is derived, never configured**: Security computes the public JWK
from the private key and publishes it under the `kid` in `Security:SigningKeyId`, so there is no
public-key variable to set and there must not be one.
[`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1 and [`SECRETS.md`](SECRETS.md) §4.1 carry the identical
procedure; a change to one is a change to all three.

**The material is asymmetric, and the wrong shape fails closed rather than quietly.** Security's
algorithm is `RS256` over a closed `RS256`/`RS384`/`RS512` allow-list, and it imports the configured
value as an RSA private key — PEM first, then a base64 of the DER encoding. Symmetric random bytes
cannot be imported that way, so a key produced by `openssl rand` makes the host **refuse to start**,
with a message that names the variable and never echoes the value. Do not answer that failure by
switching the algorithm to an HMAC family: the JWK set Security publishes is anonymous verification
material, so an HMAC key there would publish the signing secret itself and make all three verifiers
co-signers. `.env.example` §1 carries the full note, including the PEM alternative and why the
single-line form is what an environment file can hold. **The size is measured and never judged**: Security
records the imported modulus, sets `SigningKeyIsLegacyWeak` when it falls below the 2048-bit annotation
threshold `SecurityOptions.LegacyWeakSigningKeySizeBits`, and logs one warning naming the measured size —
then signs with the key. Nothing refuses a short key, because AAP §0.6.6.4 keeps 1024-bit RSA a legal size
across this estate [`ws_objects/pfw.shared.pbl.src/enums.sru:L965`] and requires the weakness to be
annotated rather than corrected. The `/v1/crypto` key-generation surface accepts the same size for the same
reason, and the two are held **together** by test.

> ### ⚠️ Why the filled-in environment file is written outside the working tree
>
> **The root ignore rules do not exclude an environment file, and this refactor does not change them**
> (the plan records that no root `.gitignore` change is required). One nested ignore file *is* added,
> `tests/e2e/.gitignore`, and it covers that directory's generated output — `node_modules/`, the report
> and trace directories, and `.env` files beneath it — but it has no effect on `orchestration/`. A key
> written to `orchestration/.env` is therefore an untracked file inside the working tree that
> `git add -A` would stage and a careless commit would publish. Being untracked is not a control;
> it is one command away from being tracked.
>
> The attached environment's own instruction is the in-tree form — `cp .env.example .env` followed by
> `docker compose --env-file .env up` — and it remains supported, **with one precondition: add an ignore
> rule covering `orchestration/.env` before writing any key into it.** The command above avoids the
> question entirely by keeping the file on a path version control does not reach, which is why it is the
> documented default here.
>
> **If you do use the in-tree form, guard the copy and fail fast.** `set -euo pipefail` is what
> stops the last line running regardless: without it a failed `cd` or a failed `cp` still falls
> through to `docker compose`, which then reads whatever `.env` happens to be in whatever
> directory you are actually standing in. And an unguarded `cp` overwrites a populated `.env`
> with the template silently, destroying the key and leaving a stack that starts and then rejects
> every token — a failure whose cause is nowhere near its symptom. Use
> `[ -f .env ] || cp .env.example .env` so the first bring-up copies the template and every later
> one leaves your file alone.
>
> Two things this is *not* a substitute for. `.env` files are excluded from every image layer by the
> repository-root `.dockerignore` (§7.3) — that control is about images, not about version control. And
> file permissions on a developer's machine are not secret management; the deployment path for real key
> material is the orchestration secret layer, injected as environment configuration and never written
> into the repository at all.

### 8.2 The readiness model

Every bullet below describes what the manifest and the endpoints do. The endpoints are implemented and are
covered by tests against an in-process host; the gates were additionally probed over real sockets during
the bring-up, and §8.3 points at the one place that reports which of them were and were not.

- `/health` is **anonymous on all four services**.
- `/v1/ping` **requires a JWT on all four** and returns `401` without one.
- **Gateway reports healthy only after Persistence, DataServices and Security do.** The manifest expresses
  this with `depends_on` using a **health condition**, so Compose gates Gateway behind its three upstreams
  rather than merely behind their container start.
- **Gateway and DataServices report `Degraded` — and therefore `503` — until they hold one accepted
  caller credential**, so the two gates on 5105 and 5102 depend on `SECURITY_CLIENT_SECRET_GATEWAY` /
  `SECURITY_CLIENT_SECRET_DATASERVICES` **or** the `GATEWAY_MTLS_*` / `DATASERVICES_MTLS_*` pairs being
  supplied. This is not hardening bolted onto readiness; it is readiness answering its own question
  truthfully. Both services forward every operation with a bearer token, the only way to obtain one is
  Security's issuance edge, and contract C-01 protects that edge with **either of two caller
  credentials — an HTTP `Basic` client credential or a client certificate — requiring one of them**,
  because a caller cannot present a bearer token in order to obtain its first bearer token. The shared
  secret is the alternative the documented bring-up supplies, and `orchestration/.env.example` leaves
  both certificate pairs deliberately empty; a certificate-only deployment needs **both** halves of its
  pair, because half a pair cannot complete a handshake. Presenting neither scheme is refused at startup
  rather than reported, which is why the readiness verdict is about the material and not about intent: a
  service that starts and then refuses every request is not ready, and a `200` there would open the
  dependency gate onto an instance that can serve nothing. Each service names the unmet setting in a
  `credentials` component entry of its `/health` body, and generating the local certificate set is one
  command block — see [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1.
- The `curl` health gates address one listener per service — 5101, 5102, 5104 and 5105 — and that is the
  whole of the estate's addressing. The gRPC surfaces of Persistence and DataServices answer on those same
  5101 and 5102 endpoints, which declare `Http1AndHttp2`, so the HTTP/1.1 `GET` the gate makes and the
  HTTP/2 call a caller makes negotiate separately over TLS on one port. All **four** gates are `https`; see
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1.

**All four gates must verify the chain, which means naming the local CA.** The certificate the four TLS
listeners present is issued by the throwaway CA generated in §8.0 and is trusted by nothing by default, so
a probe that does not name it fails on chain validation rather than on readiness — a false negative that
reads exactly like a service that never came up:

```bash
set -euo pipefail
CA="${XDG_CONFIG_HOME:-$HOME/.config}/powerframework/secrets/mtls-ca.crt"

# Host-side ports, so they honour the four *_HOST_PORT overrides of the roster in section 8.1. The
# container-side ports they map to never move.
curl -sf --cacert "$CA" "https://localhost:${PERSISTENCE_HOST_PORT:-5101}/health"    # Persistence
curl -sf --cacert "$CA" "https://localhost:${DATASERVICES_HOST_PORT:-5102}/health"   # DataServices
curl -sf --cacert "$CA" "https://localhost:${SECURITY_HOST_PORT:-5104}/health"       # Security
curl -sf --cacert "$CA" "https://localhost:${GATEWAY_HOST_PORT:-5105}/health"        # Gateway ingress
```

`localhost` is used deliberately and is not interchangeable with an arbitrary alias: the server
certificate of [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1 carries `localhost` and `127.0.0.1` as subject
alternative names alongside the four Compose service names, and hostname verification reads **only** that
extension. A name outside the set fails even with `--cacert` supplied. Installing `mtls-ca.crt` into the
host trust store instead is equally valid and lets `--cacert` be dropped.

> **Never answer a probe failure with `-k` or `--insecure`.** It suppresses the whole of chain and hostname
> validation, so the gate stops distinguishing the intended service from any listener on the port and stops
> being evidence of anything. If a gate fails, fix the trust anchor or the name — the two failures above
> are the two things worth learning from this probe.

For the port map, the transport chosen per service and the reasoning behind the reserved 5103 slot, see
[`ARCHITECTURE.md`](ARCHITECTURE.md). Bring-up detail and the readiness gates step by step belong in
[`orchestration/README.md`](../orchestration/README.md), which carries them; nothing is duplicated here.

### 8.3 What stands behind this path — restated at the point of use

**This path has been exercised, and exactly one document reports on that.** Every artifact the command
above invokes is present: the manifest `orchestration/docker-compose.yml`, **all four container
definitions** (`services/gateway-service/Dockerfile`, `services/dataservices-service/Dockerfile`,
`services/persistence-service/Dockerfile` and `services/security-service/Dockerfile`), and **an entry
point in all four service applications** — every one builds to a runnable executable and starts under
`dotnet run`.

**Read the execution status in one place, not here.**
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
is the single statement of what has and has not been exercised for the whole repository, and this section
defers to it rather than restating it — a second account is how two accounts drift apart. It records what
the orchestrated bring-up did, gate by gate, and it is equally explicit about the parts that were not
exercised. Correctness of this path additionally rests on manifest review and the CI pipeline of §10 of
this document, which is present and defines the four-service matrix. §1.3 points a reader who started at
the top at the same single statement.

---

## 9. End-to-end tests

Cross-service workflow verification lives in `tests/e2e/` and uses `@playwright/test`. The documented
install-and-run path:

```bash
set -euo pipefail
cd tests/e2e
npm ci && npm test
```

**What exists in `tests/e2e/` today, and what it can and cannot verify.** The bootstrap is present —
`package.json`, `package-lock.json`, `playwright.config.ts`, `tsconfig.json`, `.gitignore` and
`fixtures/` — so `npm ci` succeeds and the runner loads its configuration, and `specs/` carries the
cross-service workflow suites — **six** of them, `01-` through `06-`, pinned by a narrowly scoped
`testMatch` verified against the directory at config load. What has **not** been done is a full run of
those specs against a live stack: the stack itself is now brought up by
[`orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) (§8.1), so the blocker is no
longer a missing manifest but the suite's own precondition — an issuance identity provisioned against the
running Security instance (§9.1). Without it every assertion that needs a live endpoint fails its issuance
precondition or **skips with an explicit reason** naming what was probed and the bring-up command, and
only the static gates below prove anything on their own. The
stack-free assertions carry a `@no-stack` tag that exempts them from the reachability probe and can be
listed on their own with `npx playwright test --list --grep "@no-stack"`.

> **`npm test`, not `npx playwright test`, and the difference is a supply-chain one.** The `test` script
> in `tests/e2e/package.json` runs `playwright test` through the local binary that `npm ci` just
> installed from the lockfile, at the pinned `1.62.1`. `npx` prefers a local binary too, but when there
> is not one — which is precisely the state left behind by an `npm ci` that failed — it will go and
> **acquire** a package to run instead. Chained with `&&` and under `set -euo pipefail`, a failed
> install ends the snippet; `npm test` then guarantees that what runs is the locked local executable
> and nothing else. The pinned floor the snippet expects is `node >=22.12.0` and `npm@11.18.0`, both
> declared in `tests/e2e/package.json` (§4).

### 9.1 Type-checking is a separate command, because `--list` does not do it

```bash
cd tests/e2e && npm run typecheck        # tsc --noEmit
```

**`npx playwright test --list` does not type-check.** Playwright transpiles each file with esbuild,
which strips type annotations without checking them, so a genuine type error transpiles cleanly and
`--list` reports success. This was verified directly rather than assumed: a file containing
`const n: number = "a string"` left `--list` reporting `Total: 0 tests in 0 files` with no complaint,
while `npm run typecheck` failed it with
`error TS2322: Type 'string' is not assignable to type 'number'`.

`tsconfig.json` configures that gate with `strict` plus `noUnusedLocals`, `noUnusedParameters`,
`noImplicitOverride`, `noFallthroughCasesInSwitch` and `exactOptionalPropertyTypes`, and `noEmit` so
nothing is ever written beside the sources. The last of those is not incidental: the endpoint fixtures
distinguish an **absent** optional property from one **present and `undefined`** — the
client-certificate and issuance-credential resolvers each return `undefined` when their variables are
unset, which is the ordinary case for a bring-up that supplies neither — and
`exactOptionalPropertyTypes` is what keeps that distinction checked. The suite currently passes the
gate with zero errors.

### 9.2 Generated output is ignored, by a nested rule

`tests/e2e/.gitignore` excludes `node_modules/`, `test-results/`, `playwright-report/`, `blob-report/`,
`playwright/.cache/`, `*.tsbuildinfo` and `.env` files. It is nested rather than added to the root
ignore file because the plan records that no root `.gitignore` change is required, and because these
rules should apply to this directory alone.

**The trace and report rules are a secrets control, not housekeeping.** A Playwright trace records
request and response bodies, so a run against a configured environment can capture a bearer token
verbatim. The reporter is also configured to generate nothing (§9, `reporter: [['list']]`), which makes
this defence in depth: not generating the artifact is the control, and the ignore rule is the safety
net. What is deliberately **not** ignored is `package.json`, `package-lock.json`,
`playwright.config.ts`, `tsconfig.json`, `fixtures/` and `specs/` — the lock file especially, since
`npm ci` requires it and fails without it.

> ### ⚠️ `tests/` is not a greenfield directory
>
> `tests/e2e/` sits beside **three pre-existing, read-only legacy directories**:
>
> - `tests/blink/`
> - `tests/sciter/`
> - `tests/webview/`
>
> These are legacy browser-harness assets and part of the behavioural oracle (§1.5). **The end-to-end
> root is purely additive.** An agent or script that treats `tests/` as a greenfield root — or that
> "cleans" it, empties it, or scaffolds over it — **will destroy read-only oracle assets.** Never run a
> recursive delete, a `git clean`, or a scaffolding tool against `tests/`. Scope every operation to
> `tests/e2e/` explicitly.

The end-to-end project is API-level only: it drives the services over HTTP through Gateway. No
presentation surface exists in this phase, so no browser binaries are required — see
[`ARCHITECTURE.md`](ARCHITECTURE.md).

Because these tests exercise a running stack, they depend on the Compose path of §8 and therefore inherit
its unverified status (§8.3).

---

## 10. Continuous integration

`.github/workflows/ci.yml` **exists and does the following.** It runs a **four-service matrix** —
`gateway-service`, `dataservices-service`, `persistence-service`, `security-service` — and for each matrix
leg, in that service's own directory:

1. `dotnet restore`
2. `dotnet build --configuration Release`
3. `dotnet test --configuration Release` with the coverage collector, and no other argument — no
   settings file, no build-skipping switch
4. enforces the **80% line-coverage gate** by reading that service's own `coverage.cobertura.xml`
5. uploads the report as a build artifact, whatever the verdict, so a failure publishes its own evidence

A second matrix then builds **one container image per in-scope service per build** from that service's
`Dockerfile` with the repository root as the build context, and pushes it to the GitHub container registry
— on a push to the default branch only, authenticated with the automatically provisioned token, because on
a pull request from a fork that token cannot write. A pull-request run therefore builds all four images,
which is the assertion that matters for a change under review, and pushes none. The image matrix depends on
the service matrix, so no image is produced for a service whose gate has not passed.

A third job builds the whole repository solution and runs the six shared and contracts test suites, which
the per-service gate deliberately does not measure; and a final single-job verdict inspects the *result* of
all three so that one required status can stand for the whole workflow.

Each service leg runs the per-service path of §5 in that service's own directory, which is what makes the
matrix a genuine test of per-service independence rather than a partition of a single root build (C-I). The
one addition to that path is the gate's own package selection, expressed inline in the workflow: the report
also covers the shared libraries and the generated protobuf stubs, so its top-level rate reads between 10%
and 45% for services whose own assemblies are all above 88% — see §5.5 for both sets of figures.

**Three checks, not one, and the first and third are what make the second trustworthy.** Each leg asserts
that the service's own assembly **appears in the report at all** — if it does not, the leg measured
something other than the service it claims to and fails outright rather than reporting a number for
whatever else was present — that this assembly's line rate clears the floor, and that every other package's
rate is printed but never gated on, so code the `solution` job owns can neither raise nor lower the
verdict.

**What has not happened:** a run of this workflow on a GitHub-hosted runner. The gate step was extracted
verbatim from the workflow and executed locally against all four real reports — it passes for all four and
fails correctly when the floor is raised above a measured rate, when handed an unfiltered report, when
handed a report for the wrong assembly, and when no report is produced at all. The `actions/*` and
`docker/*` step versions, the registry authentication and the artifact upload are reviewed rather than
exercised (§13).

> **The coverage gate is evaluated per service, not repository-wide.** This is deliberate: a
> repository-wide average lets a well-covered service mask a poorly covered one, and the requirement is
> that each in-scope service clear the bar on its own (C-H). A leg fails if *its* service is below 80%
> line coverage, regardless of how the other three scored.

Only the four in-scope services appear in the matrix. The four deferred services have no project, no test
project and no container definition in this phase, so there is nothing for CI to build for them; see
[`DEFERRED.md`](DEFERRED.md).

For coverage mechanics in context — what the number is expected to cover, and which non-deterministic
values are masked so that a coverage run is repeatable — see [`docs/PARITY.md`](PARITY.md).

---

## 11. Toolchain constraints a project author must respect

This section discharges C-K: every technology-specific decision below is recorded with the reason it was
made, and §11.4 records the alternatives that were **rejected** and why — because a decision documented
without its rejected alternatives is a conclusion, not a decision.

### 11.1 The two mandatory package pins

**These are not preferences.** Omitting either produces a build that is either vulnerable or broken. Both
live in the repository-root `Directory.Packages.props` so that all four services inherit them from one
place and none can drift (§3.2).

#### `Microsoft.OpenApi` = 2.11.0

- The stock web template on `net10.0` emits **`NU1903`** — a known **high-severity** advisory against the
  **2.0.0** version pulled transitively by `Microsoft.AspNetCore.OpenApi` 10.0.11. Under the
  warnings-as-errors gate of §3.1 that advisory is fatal, not advisory.
- The obvious fix — moving forward to the current major line — **breaks the build.** Version **3.9.0** was
  tested directly and produces **two `error CS0200` diagnostics**, reporting that a media-type example
  property cannot be assigned because it is **read-only**. They are raised inside the SDK's *own generated
  OpenAPI XML-comment support file*, not in repository code, so there is nothing local to fix.
- The cause is that the 10.0.11 source generator is compiled against the **2.x** object model.
- **2.11.0 — the highest published 2.x — is therefore the only value that is simultaneously
  non-vulnerable and compatible.** Every REST service must respect this pin.

#### `SQLitePCLRaw.bundle_e_sqlite3` = 3.0.5

- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11 transitively pulls a **2.1.x** native library that raises
  **`NU1903`**.
- An **explicit direct reference** to the 3.0.5 bundle overrides the transitive resolution and clears the
  audit. The resolved graph was inspected and confirms the core, configuration and provider packages all
  land at **3.0.5**.

**After both pins the build reports zero warnings and zero errors** (§1.2). Removing either one does not
merely reintroduce a warning — under `TreatWarningsAsErrors` it fails the restore outright.

### 11.2 xunit.v3 test projects are hand-authored

- **xunit.v3 requires `<OutputType>Exe</OutputType>`.** A v3 test project without it will not run.
- **`dotnet new xunit3` is not available in this SDK.** Verified: the SDK responds
  `No templates or subcommands found matching: 'xunit3'`.
- **`dotnet new xunit` scaffolds the v2 line**, and its package set is older than this repository's pins:
  `xunit` 2.9.3 instead of `xunit.v3`, `Microsoft.NET.Test.Sdk` **17.14.1** instead of 18.8.1,
  `coverlet.collector` **6.0.4** instead of 10.0.1 — and no `OutputType`. It also writes version
  attributes onto its `PackageReference` items, which conflicts with central package management (§3.2).
- **The v3 test projects must therefore be hand-authored rather than templated.** The minimal shape:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
</Project>
```

  Everything else — `TargetFramework`, `Nullable`, `ImplicitUsings`, the warnings-as-errors gate — is
  inherited from `Directory.Build.props` (§3.1) and must not be repeated. Package versions are inherited
  from `Directory.Packages.props` (§3.2) and must not be added.

- This was **validated end to end**: the hand-authored project restores, builds warning-clean, runs, and
  emits Cobertura.
- Remember the analyzer consequence from §3.1: pass the ambient test cancellation token to every awaited
  call that accepts one, or the build fails.

### 11.3 The pinned dependency set

**The platform patch level is 10.0.11, and it is pinned in three places that must move together.**
The .NET platform ships as one release: a runtime, an SDK, a set of `Microsoft.*` platform packages and
a set of container base images, all published on the same day and all carrying the same fixes. So a
patch advance is not six independent version bumps — it is one decision recorded in three files:

| Coordinate | Value | Where |
| --- | --- | --- |
| SDK | **10.0.303** | `global.json` (`rollForward: latestFeature`) |
| Runtime | **10.0.11** | implied by the SDK, and by the runtime base image below |
| Platform packages | **10.0.11** | the six `10.0.x` entries in `Directory.Packages.props` |
| Build base image | **`sdk:10.0.400`** | all four `services/*/Dockerfile` |
| Runtime base image | **`aspnet:10.0.11`** | all four `services/*/Dockerfile` |

The 2026-08-11 release of the 10.0 channel is a **security** release: its release metadata marks it
`security: true` and lists ten CVEs fixed against 10.0.10. Staying on 10.0.10 was therefore a
known-vulnerable platform (CWE-1104), which is why the level advanced. Two details of the table are
worth stating rather than leaving to be rediscovered:

- **The SDK pin is 10.0.303 while the build image is 10.0.400.** Both are the same 10.0.11 platform
  release; 10.0.303 is that release's patch of the `3xx` feature band this repository already pinned, and
  10.0.400 is its `4xx` band. Microsoft publishes an exact MCR tag for the latest band only, so the image
  is `sdk:10.0.400` — and `rollForward: latestFeature` is precisely what lets it satisfy a `10.0.303` pin.
  Nothing here relies on the roll-forward being silent: both numbers are written down, in this table.
- **A pinned tag is a name, not a fetch.** A builder holding an image under a pinned tag reuses it
  without consulting the registry, so a patch pin alone does not evict a stale base layer. CI therefore
  builds with `pull: true`, which is what makes the pin true of the image and not only of the file.

Transcribed from `Directory.Packages.props` and `tests/e2e/package.json` — **both authored by this
refactor**, so this table reports what those manifests pin rather than a version inherited from anywhere.
No version here came from a **pre-existing** manifest, because the repository had none: see the note
following the table. **Registry is nuget.org for every .NET entry and npmjs.org for the three npm ones;
there are no private or internal feeds** in this refactor.

| Package | Version | Purpose | Consumed by |
| --- | --- | --- | --- |
| `Grpc.AspNetCore` | 2.83.0 | gRPC server, client and protocol-definition code generation. Pulls `Grpc.Tools` 2.83.0 and `Google.Protobuf` 3.31.1 transitively, so **neither needs an explicit reference** | Contracts, DataServices, Persistence, and the Gateway/DataServices client sides |
| `Microsoft.AspNetCore.OpenApi` | 10.0.11 | OpenAPI document generation for the REST surfaces | Gateway, Security, DataServices REST projection |
| `Microsoft.OpenApi` | **2.11.0 — mandatory pin** | OpenAPI object model (§11.1) | all REST services |
| `Microsoft.OpenApi.YamlReader` | 2.11.0 | YAML reader for the object model above, which ships a JSON reader only. Version locked to the `Microsoft.OpenApi` release it pairs with, so it cannot drag the mandatory pin off 2.11.0 | `shared/PowerFramework.Contracts.Tests` only — the two contract definitions it loads are YAML |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.11 | Inbound JWT validation on `/v1/ping` and every internal edge | all four services |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.22.0 | Token **minting** — **Security only**, because Security is the sole issuer | Security |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.11 | EF Core provider for the only evidenced storage engine | Persistence |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.11 | Design-time migration generation. The project owning migrations needs this with `PrivateAssets="all"` | Persistence |
| `Microsoft.Data.Sqlite` | 10.0.11 | ADO-level access for the legacy connection-URI grammar and raw command execution with positional binding, neither of which EF Core alone expresses | Persistence |
| `SQLitePCLRaw.bundle_e_sqlite3` | **3.0.5 — mandatory pin** | Native SQLite bundle (§11.1) | Persistence |
| `Microsoft.Extensions.Http.Resilience` | 10.8.0 | Retry and circuit-breaker on the newly created network edges (§11.5). Pulls `Microsoft.Extensions.Resilience` 10.8.0 and the Polly family | Gateway, DataServices |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.11 | In-process host for service-level tests | all four `*.Tests` projects |
| `xunit.v3` | 3.2.2 | Unit and service test framework (§11.2) | all `*.Tests` projects |
| `xunit.runner.visualstudio` | 3.1.5 | Test adapter, so the standard test command discovers xunit.v3 tests | all `*.Tests` projects |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | Test host | all `*.Tests` projects |
| `coverlet.collector` | 10.0.1 | Emits `coverage.cobertura.xml`, the artifact the §10 gate reads | all `*.Tests` projects |

**npm — `tests/e2e` only:**

| Registry | Package | Version | Purpose |
| --- | --- | --- | --- |
| npmjs.org | `@playwright/test` | 1.62.1 | Cross-service workflow verification (§9) |
| npmjs.org | `typescript` | 5.9.3 | The `npm run typecheck` gate — `tsc --noEmit`. **Deliberately the 5.x line, not `latest`**: 5.9.3 is the mature compiler, whereas the current `latest` is the 7.x native rewrite, which `@playwright/test` 1.62.1's own type definitions are not validated against |
| npmjs.org | `@types/node` | 22.20.1 | Node globals for the type gate — the endpoint fixtures read `process.env`. **Deliberately the 22.x line to match the Node 22 runtime**; the 26.x line would type-check against APIs the runtime does not have |

**Container images:**

| Stage | Image |
| --- | --- |
| Build | `mcr.microsoft.com/dotnet/sdk:10.0.400` |
| Runtime | `mcr.microsoft.com/dotnet/aspnet:10.0.11` (non-root; no `curl`/`wget` — see §7.1) |

**Transitive versions worth recording**, because they differ from the direct pins and will appear in lock
files:

- `Google.Protobuf` resolves to **3.31.1** rather than the newer published line, constrained by
  `Grpc.AspNetCore` 2.83.0.
- The Polly family resolves to **8.4.2**.
- The identity-model graph **mixes 8.19.2** (pulled by the bearer handler) **with 8.22.0** (the direct
  minting reference) — verified compatible, with zero warnings.
- `SharpYaml` resolves to **2.1.4** through `Microsoft.OpenApi.YamlReader`, and is the only package that
  reader adds to the graph. The reader's third declared dependency, `System.Text.Json` 8.0.5, resolves
  to the `Microsoft.NETCore.App` shared framework on `net10.0` rather than to a package, so it appears
  in no lock file.

**No version above was inherited from a PRE-EXISTING dependency manifest, because the repository had
none.** Before this refactor there was no `*.csproj`, no `packages.config`, no `package.json` and no lock
file of any kind anywhere in the tree — the legacy estate is PowerBuilder libraries and native binaries,
which declare no managed dependency. Every manifest and every lock file that exists today
(`Directory.Packages.props`, the twenty project files, `tests/e2e/package.json` and its
`package-lock.json`) **was created by this refactor**, so each pin above was chosen and verified here
rather than carried forward. That is why §11.1 records how the two mandatory pins were arrived at: there
was no prior decision to defer to.

The only version facts the repository asserts about **itself**, independent of this refactor, are the
legacy ones:

| Legacy version fact | Locator |
| --- | --- |
| PowerBuilder runtime `21.0.0.1311` | `ws_objects/pfw.pbl.src/pfw.sra:L34` |
| DataWindow `release 12.5` | `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L2` |
| Framework `3.0.7.2062` | `logfile.md:L1` |

**None of these constrains the .NET side.** They are recorded so that a reader who finds them does not
mistake them for build inputs.

### 11.4 Packages deliberately excluded

Recorded so a future reader does not add them as "obvious" choices. Each would be scope creep against the
requirement that no behaviour improvement be introduced beyond what the technology transition strictly
requires (C-B).

| Excluded | Why |
| --- | --- |
| Structured-logging and distributed-tracing package families | Not in the mandated stack. The built-in logging abstractions of the shared framework cover the requirement |
| A validation framework | The five type validators are **ported logic with exact legacy semantics**, not a rule set. A framework would obscure parity, which is the property that matters most here |
| Interactive OpenAPI UI packages | `Microsoft.AspNetCore.OpenApi` already produces the document; an interactive UI is not required |
| gRPC server reflection | A command-line convenience, not a requirement |
| Health-check packages | Health-check registration ships in the `Microsoft.AspNetCore.App` shared framework, so no package reference is needed for `/health` or for Gateway's upstream aggregation |
| The Aspire hosting package | The rejected orchestration alternative. Reasoning in [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| A managed SQL parser | SQL-Server-dialect-only, so it cannot serve the Oracle rewriter, and the acceptance criterion is byte-exact clause output. Implemented in-repo instead |
| **Any third-party cryptography package** | `System.Security.Cryptography` in the BCL covers every operation the legacy surface performs |
| **Any pinyin package** | The legacy lookup table exists only inside a closed binary, so bit-exact parity requires characterizing from the oracle rather than trusting a third-party table. See [`docs/PARITY.md`](PARITY.md) |

### 11.5 One inclusion that needs justifying — and it is a correctness argument

`Microsoft.Extensions.Http.Resilience` is the one package added that has no legacy counterpart, so it is
worth stating why it is not scope creep:

**An in-process call cannot fail in transit. A network call can.** Handling a failure mode that
decomposition itself creates is required **by** the transition, not layered on top of it. Without it, the
first transient network fault would surface as a defect the legacy could not have had — a regression
*introduced* by the refactor rather than a preserved behaviour.

To be unambiguous: this is a **correctness** argument, not a performance one. No claim is made or implied
about throughput, latency or availability, here or anywhere else in this document (§1.4).

---

## 12. Why the build is authored from scratch

Every build artifact in this repository is a **new file**. That looks like an omission unless the reason is
recorded, so here it is: **the legacy offers no translatable build definition.**

The two PowerBuilder project objects that would be the obvious source contradict each other, and both are
broken independently of the contradiction:

| | `ws_objects/pfw.pbl.src/project.srj` | `ws_objects/pfw.pbl.src/p_pfw.srj` |
| --- | --- | --- |
| `PBD:` lines | **29** | **28** |
| `pfw.utility.sqlite.pbl` | **included** (`:L39`) | **omitted** |
| Vendor at line 4 | `COM:Appeon` | `COM:Sybase, Inc.` |
| References `pfw.utility.imgcodec.pbl` | yes (`:L41`) | yes (`:L40`) |

- They **disagree on library count** and **disagree on the vendor string**, so there is no single build
  intent to translate even before correctness is considered.
- **Both reference `pfw.utility.imgcodec.pbl`, and a full-depth search for that name returns zero files
  anywhere in the repository.** Therefore **neither would build as written.**
- `p_pfw.srj` additionally embeds **developer-workstation absolute paths** in its `OBJ:` lines
  (`:L41`–`:L43`, rooted at `F:\pfw\`), one of which names **`pfwUtils.pbl`** — a retired library name that
  no longer exists in the tree either.
- There are **zero Git tags across the 333 commits** of legacy history, so no release is marked and no
  commit can be identified as a shipped build.
- `logfile.md` stops at **`3.0.7.2062(2022-04-14)`** (`logfile.md:L1`) while the commit history runs years
  later, so the changelog cannot adjudicate a build either.

**Conclusion.** The two project objects are **REFERENCE for build *intent* only.** They are read, never
translated and never edited (C-C), and the .NET build and CI are authored as a clean creation. This
strengthens rather than weakens the finding that every build artifact here is a new file. See
[`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) for the anomaly detail, including the compiled library that has
no source export.

### 12.1 Background: the legacy toolchain had its own environment sensitivities

The read-only legacy documentation records, at `docs/README.md` §常见问题, that after connecting to Oracle a
second run inside the PowerBuilder IDE could fail to load `pfw.dll`. The documented cause is that the
Oracle client driver alters the DLL load search path; the documented workaround is to add that binary's
directory to the `PATH` environment variable; and the documentation notes that **compiled executables were
unaffected**.

This is recorded **purely as background**, to explain why the .NET tree deliberately has **no** dependency
on that toolchain (§4). Nothing about it is ported, reproduced, or worked around in the .NET build, and the
legacy document itself is not edited.

---

## 13. Closing note: what this document claims and does not claim

**Claimed, because it was exercised.** Every figure behind these bullets is in
[§1.3](#13-the-canonical-verification-record) and appears nowhere else, this list included — a claim that
carries its own copy of a number is how a claim and its evidence drift apart:

- **The command *shape* of §5 works** — restore, release build, and coverage-collecting test — run end to
  end on the pinned .NET SDK, producing `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`,
  `Passed!  - Failed:     0, Passed:     1`, and a `coverage.cobertura.xml` report. **That run was against
  a throwaway skeleton project, not against this repository's services** — the single passing test is the
  giveaway — and it is quoted here as evidence that the command and the coverage collector work, not as a
  result for these four services.
- **In this repository today**, restore is audit-clean and **the whole solution builds** with zero
  warnings and zero errors — the six shared libraries, the contracts project, all four service
  applications and all ten test projects. Measured with `dotnet build PowerFramework.slnx -c Release`.
- **All ten test projects run, all pass, and nothing skips.** The pinyin oracle characterization hooks
  used to skip unless a paired legacy recording existed; they now assert an equivalence true in both
  worlds — characterized if and only if recorded — so they execute on every run and assert today's BLOCKED
  state instead of standing aside from it. Counts per project, and the shared-versus-service split, are
  in §1.3.
- **The four per-service coverage gates pass, measured.** Each service's own Cobertura report carries one
  `package` per instrumented assembly, and the `package` named for that service's own assembly clears the
  80% floor of §10. The report's own top-level rate reads far lower, because it also covers the shared
  libraries and the generated protobuf stubs; §5.5 gives that comparison and the reasoning.
- **All four images build, and the four-service stack comes up.** Every image was built from the
  repository-root context, all four containers reached Docker health `healthy` in the order the
  health-conditioned dependencies dictate, `/health` answered `200` anonymously over TLS on all four
  services, `/v1/ping` answered `401` without a token and `200` with one, Gateway's aggregate answered
  against three live upstreams, both issuance schemes minted a token, and all four reserved routes answered
  `501` with their capability names. Two operator preconditions apply and are stated in §8: the certificate
  material is mounted from the deployment's own secret layer, and the database is provisioned once per
  volume.
- **That the four services build and that their tests run.** Every application project produces a runnable
  executable, so `dotnet run` starts each one, and each service test project drives its own service **in
  process** through `WebApplicationFactory` or a test host. That is coverage of handlers, status
  translation, the capability gate, the reserved routes and token issuance — it is **not** evidence about a
  deployed topology (§5.5).
- `dotnet new sln` emits `.slnx` (§2, Finding 1).
- Bare `dotnet test` builds Debug after a Release build, and `-c Release` changes that (§2, Finding 2).
- A second solution file in a service directory breaks the bare commands with `MSB1011` (§3.4).
- `dotnet new xunit3` does not exist in this SDK, `dotnet new xunit` scaffolds v2, and a hand-authored
  xunit.v3 project restores, builds, runs and emits Cobertura (§11.2).
- With both mandatory pins applied, restore is audit-clean and the build reports zero warnings and zero
  errors (§11.1).

**Not claimed, because it was not exercised:**

- **Anything about the running stack, *in this document*.** Not because nothing was run — the bring-up was
  run, all four images were built and all four services reached `healthy` — but because this document does
  not keep a second account of it. Read
  [`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised),
  which is the single execution-status statement for the repository and is explicit about the parts that
  remain open there, the mutual-TLS arm and any gRPC call across a container boundary among them.
- **That the coverage gate has run on GitHub's runners.** `.github/workflows/ci.yml` now exists and
  enforces the 80%-per-service floor of §10 from each service's own Cobertura report, and the gate step
  was extracted verbatim from that workflow and executed locally against all four real reports — it passes
  for all four and fails correctly when the floor is raised above a measured rate, when handed an
  unfiltered report, when handed a report for the wrong assembly, and when no report is produced. What has
  not happened is a run of the workflow itself on a GitHub-hosted runner, so the `actions/*` and
  `docker/*` step versions, the registry authentication and the artifact upload are reviewed rather than
  exercised.
- **That any image has been *published*.** The image job builds all four and pushes them to the GitHub
  container registry only on a push to the default branch, authenticated with the automatically
  provisioned token. **No push has occurred from here.** Building an image locally and publishing one are
  different acts, and only the first is claimed.
- **That any service has served a request across a network *in a test*.** Every service test drives its
  host **in process**, so no test performs a TLS handshake, ALPN negotiation, real gRPC channel setup or
  client-certificate exchange. Requests crossed real boundaries in the bring-up of §1.3, over TLS with the
  chain verified against the local CA — but that is evidence gathered outside the test suites, and the
  suites themselves remain in-process (§5.5).
- **Characterization parity.** `characterization/` and its 15 workflow definitions exist, but
  `characterization/recordings/` is empty on both sides: no PowerBuilder oracle run has been performed, so
  no paired recording has been captured and **no parity comparison has been performed** (§1.3). This is the
  one gap a healthy stack does nothing to close.
- Anything about build or runtime performance. No such objective is published in this repository, so none
  is asserted (§1.4, §11.5).

**Recorded rather than hidden:**

- §2 gives `MSB4014` as the solution-filter incompatibility code, matching the three sibling files listed
  there, **and** additionally records `MSB4025` and `MSB5026` as diagnostics a misused filter surfaces on
  the pinned SDK. All of them fail in all three verbs, and the operative guidance is identical in every
  case — **do not author a solution filter.** Both sets are given so that a reader who tests the claim
  finds what this document says they will find, which is the standard this document holds itself to
  throughout.

If you are adding a project, the four things to get right are: put it in the right per-service `.slnx` and
add no second solution file (§3.4); reference packages **versionlessly — no `Version`, and no
`VersionOverride` either** (§3.2); if it is a test project, hand-author it with
`<OutputType>Exe</OutputType>` (§11.2); and expect the warnings-as-errors gate to be enforced, including
analyzer diagnostics (§3.1). If the project takes a package no other project takes, add its
`PackageVersion` to `Directory.Packages.props` and add the package to the root `NOTICE` in the same
change — the licence inventory there is derived from that manifest, so a pin that is missing from one is
missing from both.

---

## 14. Markdown lint policy for this documentation set

The seven Markdown documents this refactor authors are linted, and the policy is **declared in the
files themselves** rather than described here and hoped for.

**The policy.** `MD013` (line length) is set to **120 characters**, and is **disabled for tables and
for fenced code blocks**. Every other `markdownlint` rule is left at its default and is satisfied.

**Why 120 and not the 80-character default.** Prose in these documents *is* wrapped, and is held to the
120 limit — the exemptions are not a licence to stop wrapping. The two exempt constructs cannot be
wrapped without damage:

- **Tables.** An evidence row typically carries a legacy locator, the finding it proves and the action
  it implies. Markdown has no continuation syntax for a table cell, so wrapping means splitting the
  locator away from what it proves — turning one checkable row into two half-rows. The evidence tables
  in [`SECRETS.md`](SECRETS.md) and the RPC inventories in [`CONTRACTS.md`](CONTRACTS.md) are the cases
  that decided this.
- **Fenced code blocks.** A wrapped command is a command that does not run. Every code block here is
  meant to be copied and executed verbatim, which is the standard this document holds itself to
  throughout.

**How it is declared, and why that way.** Each document carries a `markdownlint-configure-file`
directive in an HTML comment at its head. Three properties made that the right mechanism rather than a
repository-root configuration file:

1. **It is enforceable, not advisory.** Any `markdownlint` invocation on these files honours it, with no
   flags to remember and no external file to locate. Verified with no configuration file present
   anywhere: the same document that reports `MD013` violations against the 80-character default reports
   **zero** issues once the directive is in place.
2. **It leaves the read-only legacy documents alone.** The five pre-existing Chinese documents in
   `docs/` are the behavioural oracle and are never edited (C-C). A root configuration would have
   silently changed how they are linted too; a per-file directive cannot.
3. **It adds no repository-root artifact.** The plan enumerates the root files this refactor creates,
   and a lint configuration is not among them.

**Verifying, and why no command is published for it.** The head-of-file directive *is* the verification
mechanism: a markdownlint-compatible tool that is already provisioned reads it and reports zero issues,
with no flags, no file list and no external configuration file to locate.

**No linter command line appears in this documentation set, and that is a supply-chain control rather
than an omission.** The entire approved npm dependency set for this repository is the exact, locked
manifest under `tests/e2e` (§11.3), and **no Markdown linter is in it**. Publishing an on-demand
package-runner invocation for one would instruct an unpinned version to be resolved from the network and
executed outside that lockfile every time somebody followed this document — the same failure mode §9
warns against for the end-to-end runner, and the reason that section insists on `npm test` over a
package-runner invocation. The two cases differ only in that Playwright at least *is* pinned and locked;
a linter that is in no manifest at all has no version to resolve to but "whatever is newest today". If a
deployment wants the check automated, it adds an exact, locked development dependency in its own manifest
and invokes the local binary through a package script — which is a change to that manifest and to the
`NOTICE` licence inventory derived from it (§13), not a documentation line.

**No file list and no glob is needed, which is the stronger property.** A repository-root configuration
file or a glob such as `docs/*.md` would also reach the five read-only legacy documents, which carry **55
pre-existing violations** of their own — 21 `MD040` unlabelled code fences, 16 `MD010` hard tabs, and the
remainder across `MD032`, `MD031`, `MD041`, `MD029` and `MD009`. Those are **out of scope and are
deliberately not fixed**, because the files are read-only. A sweep that reported them would report a
failure that must not be acted on, which is worse than no check at all. One of the legacy files is not
even UTF-8 — `docs/Blink交互.md` is GBK-encoded — so tooling that assumes UTF-8 across `docs/` will fault
on it. A per-file directive cannot reach any of them.

[`docs/PARITY.md`](PARITY.md) carries the same directive; its §11 restates this policy for readers who
arrive there first.

---

## 15. Per-service configuration keys

**Why this section exists.** Every service reads the same four kinds of setting — the internal TLS trust
anchor, the identity it presents to Security, the token-signing key, and the SQLite data directory — but
**no two of them spell those settings the same way**, and there is no single prefix a reader can assume.
Someone bringing a service up therefore had to read four `Configuration/*Options.cs` files and four
`appsettings.json` files to discover the key names, and a plausible guess produced a service that started
and then failed at its first outbound call. The table below is the authoritative list, so the guess is
never necessary.

**The keys are tabulated, not renamed.** Harmonising them onto one prefix would ripple through four
`appsettings.json` files, the orchestration manifest, `orchestration/.env.example` and the coherence tests
in `PowerFramework.Contracts.Tests` that pin each spelling — a change with real regression risk and no
behavioural benefit. Documenting the actual shapes carries the same information at none of that cost.

`:` is the configuration-path separator. In an environment variable each `:` becomes `__`, so
`Gateway:InternalTls:TrustedCaPath` is set as `Gateway__InternalTls__TrustedCaPath`.

### 15.1 The four cross-cutting concerns

| Concern | Gateway | DataServices | Persistence | Security |
| --- | --- | --- | --- | --- |
| **Internal TLS trust anchor** — the CA bundle used to verify the certificate an upstream presents | `Gateway:InternalTls:TrustedCaPath` | `DataServices:InternalTls:TrustedCaPath` | `InternalTls:TrustedCaPath` — **no service prefix** | *n/a — reaches no upstream* |
| **Client identity** — the certificate this service presents when it calls Security | `Gateway:MutualTls:CertificatePath`, `Gateway:MutualTls:CertificateKeyPath` | `DataServices:Security:MutualTls:CertificatePath`, `DataServices:Security:MutualTls:CertificateKeyPath` — **nested one level deeper** | *n/a — fetches JWKS anonymously and mints nothing* | *n/a — it is the issuer* |
| **Token signing key** | *n/a — holds verification material only* | *n/a* | *n/a* | `SECURITY_JWT_SIGNING_KEY` — **a flat key, deliberately not `Security:SigningKey`** |
| **SQLite data directory** | *n/a* | *n/a — holds no storage provider* | `Sqlite:DataDirectory` | *n/a* |

Three shapes for one concern, and each difference is real rather than a typo in this table:

- **Gateway** prefixes with its own name and puts `MutualTls` directly under that prefix.
- **DataServices** prefixes with its own name but nests the same settings under a further `Security`
  sub-section, because that service groups everything about its relationship with Security together.
- **Persistence** does not prefix at all: `InternalTls` and `Jwt` sit at the configuration root.

**Only Persistence holds a storage provider and only Security holds a signing key.** Those are
architectural invariants (AAP §0.1.1), not accidents of configuration, which is why the *n/a* cells are
worth as much as the populated ones: a key supplied where the table says *n/a* is inert, and a deployment
that supplies one has misunderstood the topology rather than mis-typed a name.

### 15.2 Inbound token validation

The section each service binds its JWT bearer validation from also differs:

| Service | Section |
| --- | --- |
| Gateway | `Authentication:Schemes:Bearer` |
| DataServices | `Authentication:Jwt` |
| Persistence | `Jwt` — **at the configuration root** |
| Security | `Authentication:Jwt` |

### 15.3 Security's own additional keys

Security carries the issuance surface, so it binds settings no other service has. All are under the
`Security:` prefix **except the signing key**, which is flat:

| Key | Purpose |
| --- | --- |
| `Security:Issuer` | The `iss` value minted tokens carry |
| `Security:Audiences` | The audiences issuance will accept a request for |
| `Security:TokenLifetime`, `Security:SigningAlgorithm` | Lifetime and algorithm of a minted token |
| `Security:Clients[n]:Subject` | A client permitted to request a token |
| `Security:Clients[n]:SecretConfigurationKey` | **The NAME of a flat configuration key holding that client's secret** — never the secret itself, so no secret appears in `appsettings.json` |
| `Security:Clients[n]:Audiences[m]`, `:Scopes[m]` | **RETIRED, AND THEIR PRESENCE NOW REFUSES THE HOST.** They used to be bound and frozen onto the registered client and then consulted by nothing — a second surface describing a decision only the `Security:Callers` + `Security:CallerAuthorizations` matrix below takes — so a value here could neither grant nor withhold anything, and the shipped configuration had drifted away from the matrix in both files. Editing them to fix an authorization problem changed nothing at all. They are **removed rather than enforced**, because enforcing them would put a second permission gate in front of the matrix, able to withhold what the matrix grants, and divided authority over one decision is the defect itself. Because a binder silently drops a key no property matches, a settings file carrying either member forward would read as working configuration and do nothing — so the composition root reads the configuration root for both key paths and **refuses to start**, naming every offending key in one message together with the matrix that replaced it. State per-caller permissions in the matrix row below, and nowhere else |
| Roster/matrix cross-reference | Separate from the above, and deliberately **reported rather than refused**, under the log category `PowerFramework.Security.IssuanceRosterAuthority`. `Security:Clients` answers who may authenticate *by shared secret*; the matrix answers what an authenticated identity may *request*. A matrix grant naming a caller no `Clients` entry names is therefore usually a caller that authenticates by **client certificate** — `POST /v1/tokens` reads such a caller's identity from the certificate's common name without consulting the roster, which is why `:SecretConfigurationKey` is optional. Refusing that would make a supported topology unstartable, so it is logged at `Warning` for an operator to judge |
| `Security:CallerAuthorizations[n]:Caller`, `:Audience`, `:Scopes[m]` | Which caller may obtain which audience with which scopes. **This matrix is the effective authority** — with `Security:Callers`, whose nested grants are folded first and which these flat rows add to. An empty matrix refuses every issuance request |
| `Security:MutualTls:ClientCaPath` | Trust anchor for a caller presenting a certificate to `POST /v1/tokens` |
| `Security:MutualTls:Identity` | The identity attributed to a verified caller certificate |
| `Security:ClientCertificateAuthorityPath` | Trust anchor a caller certificate must chain to before `POST /v1/tokens` will honour the identity it carries — the ISSUANCE anchor, and **not** the Kestrel-layer one above it. Unset means the composition root adopts `Security:MutualTls:ClientCaPath`, so `SECURITY_MTLS_CLIENT_CA_PATH` alone is sufficient; set explicitly, it wins |
| `Security:ClientCertificateRevocationMode` | Revocation checking mode for those certificates |
| `Security:KeyStore:PermittedKeyRefs[n]` | The opaque `keyRef` values `C-02` will resolve — callers pass a reference, never key material |
| `SECURITY_JWT_SIGNING_KEY` | **The only signing secret in the entire system.** Flat and fixed: do not rewrite it as `Security__SigningKey` or add such an alias. `orchestration/.env.example` records why, and that it takes the base64 of a PKCS#8 DER RSA private key rather than random bytes |

### 15.4 Hosting keys, identical across all four

These are ASP.NET Core's own and are spelled the same everywhere:

| Key | Purpose |
| --- | --- |
| `Kestrel:Endpoints:*` | Listener addresses and protocols — the 5101/5102/5104/5105 allocation |
| `Kestrel:Certificates:Default:Path`, `:KeyPath` | The server certificate each service presents |
| `Logging:LogLevel:*` | Log level filters |
| `AllowedHosts` | Host filtering |

### 15.5 Verifying this table

It was produced from the running estate rather than by reading the options types alone: each key above is
one a service actually binds, confirmed by bringing all four up over HTTPS with these keys and no others
set, and observing `/health` answer `200` for every one of the four with no configuration refusal. Two
qualifications, because the difference matters: the callers authenticated to the issuance edge with
**shared secrets**, the four `*_MTLS_*` certificate paths being empty in the documented bring-up; and the
probes addressed the **host** side of each published port, which the four `*_HOST_PORT` variables may move
even though the listener ports do not. If a key is added or respelled, `PowerFramework.Contracts.Tests`
carries the coherence tests that
pin the spellings — `ServiceConfigurationCoherenceTests` and `OperationalTopologyCoherenceTests` — and
they, not this table, are what fail first.

---

## 16. The authoritative target-file inventory

This section is the **single authoritative statement of what this refactor's target scope contains**.
There is no second inventory anywhere in the documentation set; where another document needs a count it
cites this one rather than restating it.

It exists because a count was previously carried in two places that disagreed: a checkpoint manifest
declaring **338** targets against a literal wildcard scope containing **520** tracked ones. The 182-file
gap was not scope creep — it was the manifest schema not recognising helper and test files that the build
and the coverage gate both depend on — but a gate that cannot be audited as declared is not a gate, so the
two are reconciled here into one number with its derivation attached.

### 16.1 How these numbers are produced

Every figure below is **measured from git**, never transcribed from an earlier snapshot. Transcription is
precisely how the 338/520 divergence arose, so the derivation is given so any reader can reproduce it:

```bash
# The pre-refactor baseline: the last upstream PowerBuilder commit, before any .NET file existed.
BASE=a80ac35

# Operation counts against that baseline. A = CREATE, M = UPDATE, D = DELETE.
git diff --name-status "$BASE" HEAD | awk '{print $1}' | sort | uniq -c

# The one and only UPDATE in the whole refactor.
git diff --name-status "$BASE" HEAD | awk '$1=="M"{print $2}'

# Files authored but not yet committed, excluding build output and scratch.
git status --porcelain --untracked-files=all \
  | awk '/^\?\?/{print $2}' \
  | grep -vE '/(bin|obj)/|^blitzy_adhoc_test'
```

`bin/` and `obj/` are **not** git-ignored in this repository, so any count taken without excluding them is
wrong by thousands of files. That is a property of this checkout worth stating rather than discovering.

### 16.2 The reconciliation

| Quantity | Count |
| --- | ---: |
| Tracked files at the pre-refactor baseline `a80ac35` | 934 |
| Tracked files at `HEAD` | 1465 |
| **CREATE** vs baseline | **531** |
| **UPDATE** vs baseline — `README.md`, and nothing else | **1** |
| **DELETE** vs baseline | **0** |
| Authored, not yet committed | 0 |
| **Total target files** | **532** |

531 + 1 + 0 = 532, and 934 + 531 = 1465, so the operation counts and the tracked totals close against
each other independently. `DELETE` is zero because the .NET tree is **purely additive**: it is created
alongside the read-only legacy tree in the same checkout and removes nothing (constraint C-C).

### 16.3 Per-group counts

| # | Group | Files | CREATE | UPDATE | Not yet committed |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Root build and solution plumbing | 8 | 7 | 1 | 0 |
| 2 | Continuous integration | 1 | 1 | 0 | 0 |
| 3 | Orchestration | 3 | 3 | 0 | 0 |
| 4 | Documentation (authored) | 7 | 7 | 0 | 0 |
| 5 | Shared libraries and contracts | 124 | 124 | 0 | 0 |
| 6 | Gateway service | 46 | 46 | 0 | 0 |
| 7 | DataServices service | 112 | 112 | 0 | 0 |
| 8 | Persistence service | 126 | 126 | 0 | 0 |
| 9 | Security service | 62 | 62 | 0 | 0 |
| 10 | End-to-end tests | 23 | 23 | 0 | 0 |
| 11 | Characterization | 20 | 20 | 0 | 0 |
| | **Total** | **532** | **531** | **1** | **0** |

**Nothing is unclassified.** Every one of the 532 paths falls into exactly one group above, and every
group corresponds to an entry in the migration plan's target structure. A path that matched no group would
be reported as scope creep; the classifier finds none.

**No target path lies inside the read-only legacy tree.** `ws_objects/**`, `oldversion/125/**`, `pack/**`,
`res/**`, `samples/**`, `sciter_control/**`, `tests/blink|sciter|webview/**` and the five pre-existing
Chinese documents under `docs/` contribute zero paths to the inventory, which is constraint C-C measured
rather than asserted.

### 16.4 Per-project counts

The four services and the seven shared projects, each with its sibling test project. A service's
"service root" is its `Dockerfile` and its per-service `.slnx` — the two files that make it independently
buildable (constraint C-I).

| Project | Files |
| --- | ---: |
| `services/gateway-service` (service root) | 2 |
| `services/gateway-service/PowerFramework.Gateway` | 18 |
| `services/gateway-service/PowerFramework.Gateway.Tests` | 26 |
| `services/dataservices-service` (service root) | 2 |
| `services/dataservices-service/PowerFramework.DataServices` | 44 |
| `services/dataservices-service/PowerFramework.DataServices.Tests` | 66 |
| `services/persistence-service` (service root) | 2 |
| `services/persistence-service/PowerFramework.Persistence` | 58 |
| `services/persistence-service/PowerFramework.Persistence.Tests` | 66 |
| `services/security-service` (service root) | 2 |
| `services/security-service/PowerFramework.Security` | 24 |
| `services/security-service/PowerFramework.Security.Tests` | 36 |
| `shared/PowerFramework.Contracts` | 6 |
| `shared/PowerFramework.Contracts.Tests` | 27 |
| `shared/PowerFramework.Shared.Kernel` | 10 |
| `shared/PowerFramework.Shared.Kernel.Tests` | 10 |
| `shared/PowerFramework.Shared.Diagnostics` | 6 |
| `shared/PowerFramework.Shared.Diagnostics.Tests` | 12 |
| `shared/PowerFramework.Shared.Eventful` | 5 |
| `shared/PowerFramework.Shared.Eventful.Tests` | 17 |
| `shared/PowerFramework.Shared.Localization` | 8 |
| `shared/PowerFramework.Shared.Localization.Tests` | 16 |
| `shared/PowerFramework.Shared.Containers` | 3 |
| `shared/PowerFramework.Shared.Containers.Tests` | 4 |
| | **470** |

This table is a breakdown of **groups 5–9 only** — the four services and the seven shared projects — and it
sums to 470: Gateway 46, DataServices 112, Persistence 126, Security 62 and shared 124. Every remaining
target file sits outside any .NET project: 8 root files, 1 CI workflow, 3 orchestration files, 7 authored
documents, 23 end-to-end files and 20 characterization files, which is 62. 470 + 62 = **532**, closing
against §16.2 and §16.3.

### 16.5 The eight root files, named

| File | Operation | Why it is a target |
| --- | --- | --- |
| `PowerFramework.slnx` | CREATE | The repository solution. `.slnx` is the .NET 10 format — see §2, Finding 1 |
| `Directory.Build.props` | CREATE | One settings source for all twenty projects — §3.1 |
| `Directory.Packages.props` | CREATE | Central package management, which is what forces the two mandatory pins — §3.2, §11.1 |
| `global.json` | CREATE | The SDK pin |
| `.editorconfig` | CREATE | Carries the analyzer suppressions that let the legacy `SCREAMING_SNAKE` constant identifiers be preserved verbatim |
| `.dockerignore` | CREATE | Layer hygiene that doubles as a secrets control — §7.3 |
| `NOTICE` | CREATE | BSD-2-Clause, its four-condition Chinese restatement and the eleven upstream attributions |
| `README.md` | **UPDATE** | The single UPDATE in the entire refactor. Its pre-existing licence text and Chinese restatement are preserved verbatim, whitespace included |

**There is deliberately no ninth root file.** A `coverage.runsettings` was carried here by an earlier
revision, and it is gone: everything the coverage gate needs is an inline step in
`.github/workflows/ci.yml`, so a settings file would have been a second artifact governing that workflow's
behaviour from outside it — the one shape the CI brief rules out. Its whole content was a collector filter,
and the gate now performs the same narrowing by selecting the service's own Cobertura package by name,
where the selection is legible in the step that acts on it (§5.4, §10). The answer to the 338/520 question
is therefore the **test projects and shared-library helpers** that made up the 182 — each is either
exercised by the build, required by the coverage gate, or required by C-I's "each service builds and tests
independently from a clean checkout" — and not a root-level helper.

### 16.6 What keeps this section honest

A count written in prose is a fact with no owner, which is exactly how a manifest came to declare 338
against a scope of 520. Three mechanisms own the properties that matter:

* **The `hygiene` CI job's target-scope audit** takes every tracked file, removes the read-only legacy
  oracle, and fails the build if anything is left that belongs to none of the groups in §16.3. A new
  top-level directory, or a stray file dropped at the repository root, is reported by name as
  UNCLASSIFIED. Verified both ways: it passes against this tree, and it fails when a file is planted
  outside every group.
* **The same job's `git diff --check` leg** enumerates the target paths directly, so a target directory
  that nobody added to the gate is visible as an unchecked path rather than silently outside CI.
* **`PowerFramework.Contracts.Tests`** derives the contract and route counts it asserts from the compiled
  protobuf descriptors and the route table rather than restating them — the same discipline applied to a
  different set of numbers.

**What the audit deliberately does NOT do is assert a file count.** A pinned total would fail on the next
legitimate file and would then be bumped or deleted without thought, which is the failure mode that
produced the divergence in the first place. It asserts the two invariants that survive growth instead:
that no target lies inside the legacy tree, and that nothing sits outside every declared group.

The consequence is worth stating plainly rather than leaving to be assumed: **the totals in §16.2, §16.3
and §16.4 are a measurement taken at a point in time, and no mechanism re-derives them.** When a group
gains or loses files, re-measure with the commands in §16.1 and update those three tables. The audit will
tell you that a new file is in scope; it will not tell you that a number here is stale.
