# PowerFramework → .NET 10 — Build Reference

This document is the authoritative reference for **building, testing and packaging** the four-service
.NET 10 decomposition of PowerFramework. It exists to explain one command:

```bash
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

Everything else here — central package management, the two mandatory package pins, the hand-authored
test projects, the one-solution-file-per-directory rule — exists so that this single command works
**verbatim, from a clean checkout, for each of the four services independently** (C-I).

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
| The characterization model, the fixture corpus, coverage mechanics in context and the determinism seams | [`PARITY.md`](PARITY.md) |
| The legacy build-definition anomalies in full, and the full-estate object mapping | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| Secret locators, severities, required actions and the token-topology register | [`SECRETS.md`](SECRETS.md) |
| Compose bring-up detail and the readiness gates step by step | `orchestration/README.md` |
| The cross-service contract inventory | [`CONTRACTS.md`](CONTRACTS.md) |
| The four deferred destinations, which receive no project and no container in this phase | [`DEFERRED.md`](DEFERRED.md) |

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
constructed with **.NET SDK 10.0.302** and the exact documented per-service command of §5 was run
against it end to end. It produced:

- **`Build succeeded.` with `0 Warning(s)` and `0 Error(s)`**, under `TreatWarningsAsErrors` and central
  package management;
- **`Passed!  - Failed:     0, Passed:     1`**;
- **`coverage.cobertura.xml`** — the exact artifact the coverage gate of §10 is measured from.

The diagnostic codes quoted in §2, §3.3 and §11 were obtained the same way — by provoking each failure on
the pinned SDK and reading what it printed — rather than recalled from memory. §2 additionally
distinguishes the solution-filter code that the repository records repository-wide from the codes a
misused filter surfaces on this SDK, and §13 summarises that distinction.

### 1.3 What was not verified — stated plainly

**No verified container bring-up is claimed anywhere in this document.** Docker was unavailable in the
environment where this migration was planned, so the Compose bring-up of §8 and its ordered health
probes **could not be exercised**. The container definitions and the Compose manifest are authored in
the same change set this document describes.

Container correctness is therefore asserted by **container-definition and Compose manifest review plus
CI**, and this document says so rather than implying an end-to-end run that did not happen. §8 restates
the limitation at the point of use, so a reader who arrives there directly still sees it.

The distinction matters and is held throughout: §1.2 is claimed, §1.3 is disclaimed.

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
  `pack/**` are inputs to **parity work only**, never to the build. See [`PARITY.md`](PARITY.md).
- The two PowerBuilder project objects are cited in §12 as **REFERENCE for build *intent* only**. They
  are not translated, and §12 shows why neither could be.
- The five pre-existing Chinese documents in this folder — `docs/README.md`, `docs/Blink交互.md`,
  `docs/Sciter交互.md`, `docs/PB多线程绕坑提示.md` and `docs/n_cst_dwsvc_columnexp.md` — are read-only
  reference. They are not edited, translated, re-encoded, renamed or link-rewritten. This document is
  purely additive alongside them.

---

## 2. The two findings that must reach every reader

Both were observed directly. Each breaks the build if it is ignored.

### ⚠️ Finding 1 — `.slnx` is the .NET 10 solution format, and solution filters are not a supported path here

**.NET 10's `dotnet new sln` emits `.slnx`**, the XML solution format — not `.sln`. Reproduce it in one
command:

```bash
dotnet new sln -n Probe && ls Probe.*
# -> Probe.slnx
```

Every solution file in this repository is consequently `.slnx`: the root `PowerFramework.slnx` of §6 and
the four per-service solutions of §3.3.

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

**Additional diagnostics you may encounter on the pinned SDK 10.0.302.** Recorded because a build
reference should tell you what you will actually see, and because these confirm the prohibition rather
than soften it — a misused filter fails in **all three verbs** in every case:

| Situation | Diagnostic |
| --- | --- |
| The filter lists a project that does not match the solution's project list | **`MSB4025`** — `InvalidProjectFileException` raised from `SolutionFile.ValidateProjectsInSolutionFilter()` |
| The filter names a solution file that does not exist | **`MSB5026`** |

The practical point behind every one of these codes is the same: a filter's project list has to be kept
in exact agreement with the solution **by hand**, and any drift fails hard. That maintenance would be
spent reproducing a scoping the per-service solutions already give for free, by construction.

**The resolution is the per-service `.slnx` design of §3.3.** Each service directory carries exactly one
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

```bash
# THE VERBATIM DOCUMENTED FORM - preserved exactly as the environment specifies it (C-L).
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
   `none` in `.editorconfig` — **scoped by file glob to the ten files that genuinely carry those
   identifiers**, deliberately not applied globally. Adding a new file with such identifiers requires
   adding its own scoped section; it will otherwise fail the build. See [`PARITY.md`](PARITY.md) for why
   the spellings are preserved.

### 3.2 Repository-root `Directory.Packages.props` — central package management is mandatory

Central package management is **mandatory here, not stylistic.** All package versions live in this one
file as `PackageVersion` entries, and **every `PackageReference` in every `.csproj` is versionless**:

```xml
<!-- In any project file: NO Version attribute. -->
<PackageReference Include="Microsoft.AspNetCore.OpenApi" />
```

```xml
<!-- In Directory.Packages.props, once, for the whole repository: -->
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.10" />
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

### 3.3 Per-service `services/<service-name>/<service-name>.slnx` — and why exactly one is load-bearing

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
| .NET SDK | **10.0.302** | Everything. Pinned by repository-root `global.json` |
| Node.js | v22.23.2 verified sufficient on the authoring host | The end-to-end tests of §9 **only** |
| npm | 11.18.0 verified sufficient on the authoring host | The end-to-end tests of §9 **only** |
| Docker + Compose v2 | — | The Compose path of §8 **only** — and see §1.3: that path is unexercised |

The SDK pin, in full:

```json
{
  "sdk": {
    "version": "10.0.302",
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

This is the primary build path. It is what CI runs (§10), and it is the path that must work from a clean
checkout for each service independently (C-I).

### 5.1 The verbatim command

Preserved exactly as the attached environment specifies it (C-L):

```bash
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test --collect:"XPlat Code Coverage"
```

Recall Finding 2 (§2): the `dotnet test` step in this form builds and measures **Debug**, not the Release
build produced by the preceding step.

### 5.2 The four services, concretely

Service directory names, project names and ports are identical to those in
[`ARCHITECTURE.md`](ARCHITECTURE.md) and root `README.md`.

| Service directory | Application project | Test project | Port |
| --- | --- | --- | --- |
| `services/persistence-service` | `PowerFramework.Persistence` | `PowerFramework.Persistence.Tests` | **5101** |
| `services/dataservices-service` | `PowerFramework.DataServices` | `PowerFramework.DataServices.Tests` | **5102** |
| *(reserved)* | — | — | **5103** — commented-out Phase-2 slot |
| `services/security-service` | `PowerFramework.Security` | `PowerFramework.Security.Tests` | **5104** |
| `services/gateway-service` | `PowerFramework.Gateway` | `PowerFramework.Gateway.Tests` | **5105** |

Port 5103 is left reserved rather than reassigned; see [`ARCHITECTURE.md`](ARCHITECTURE.md) for the
reasoning and for the transport chosen per service.

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
**alongside** the verbatim form of §5.1, never in place of it:

```bash
cd services/<service-name> && dotnet restore && dotnet build -c Release && dotnet test -c Release --collect:"XPlat Code Coverage"
```

### 5.4 Where the coverage report lands

Either test form writes the coverage report to:

```text
services/<service-name>/<project>.Tests/TestResults/<run-guid>/coverage.cobertura.xml
```

`coverage.cobertura.xml` is the exact artifact the 80%-per-service gate of §10 reads. Its emission by
`coverlet.collector` was confirmed empirically (§1.2). For what the coverage number is expected to cover
and which values are masked for determinism, see [`PARITY.md`](PARITY.md).

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
dotnet restore
dotnet build -c Release
dotnet test
```

> **This is a developer convenience. It does not supersede the per-service solutions.**
>
> The four per-service solutions of §3.3 are what make independent per-service build and test work, and
> they are what CI exercises (C-A, C-I). The root solution exists so that a developer can open or build
> the whole tree in one step; it is never the path by which a service's independence is demonstrated. If
> the root build and a per-service build ever disagree, the per-service build is authoritative.

Note that `dotnet test` at the root is subject to Finding 2 (§2) exactly as the per-service form is: it
builds Debug. Add `-c Release` when that matters.

---

## 7. Container build

### 7.1 One image per service, multi-stage, non-root

Each service has its own container definition at `services/<service-name>/Dockerfile`, and each produces
**one image per service** — four images, matching the four independently deployable services (C-J).

Every image is **multi-stage**: an SDK image (`mcr.microsoft.com/dotnet/sdk:10.0`) for restore and build,
and an ASP.NET runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0`) for the final stage. The final
stage runs as a **non-root** user, per the baseline of §1.1.

One runtime-image property is worth knowing before writing a health probe: the ASP.NET runtime image
ships **without `curl` and without `wget`**. A container health probe expressed as a shell command must
therefore install its own probe tool in the runtime stage — otherwise the `service_healthy` condition of
§8 can never be satisfied and the readiness gate silently never opens.

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

One hand-authored Compose manifest brings all four services up together (C-J). From the repository root:

```bash
cd orchestration
cp .env.example .env
# Populate the signing-key variable in .env before bringing the stack up.
docker compose --env-file .env up --build -d
```

The environment file is `orchestration/.env.example`, used as a template. It declares the JWT signing key
by the variable name **`SECURITY_JWT_SIGNING_KEY`** and nothing else as a signing secret, because Security
is the sole token issuer — the other three services hold verification material only. **No value for it
appears in this document, in `.env.example`, in any `appsettings.json` or in any container definition.**
Generate one locally and keep it out of version control; `.env` files are excluded from images by §7.3.
See [`SECRETS.md`](SECRETS.md) for the token topology.

### 8.1 The readiness model

- `/health` is **anonymous on all four services**.
- `/v1/ping` **requires a JWT on all four** and returns `401` without one.
- **Gateway reports healthy only after Persistence, DataServices and Security do.** This is expressed
  with `depends_on` using a **health condition**, so Compose gates Gateway behind its three upstreams
  rather than merely behind their container start.

For the port map, the transport chosen per service and the reasoning behind the reserved 5103 slot, see
[`ARCHITECTURE.md`](ARCHITECTURE.md). For bring-up detail and the readiness gates step by step, see
`orchestration/README.md`. Neither is duplicated here.

### 8.2 This path is unexercised — restated at the point of use

**The Compose bring-up above has not been verified.** Docker was unavailable in the environment where
this migration was planned, so neither the bring-up nor its ordered health probes could be exercised, and
the container definitions and the Compose manifest are authored in the same change set this document
describes.

Correctness of this path is asserted by **container-definition and Compose manifest review plus CI**.
Treat the commands in this section as the intended and reviewed path, not as a transcript of a successful
run. §1.3 states the same limitation for a reader who started at the top.

---

## 9. End-to-end tests

Cross-service workflow verification lives in `tests/e2e/` and uses `@playwright/test`. The documented
install-and-run path:

```bash
cd tests/e2e
npm ci
npx playwright test
```

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
its unverified status (§8.2).

---

## 10. Continuous integration

`.github/workflows/ci.yml` runs a **four-service matrix** — `gateway-service`, `dataservices-service`,
`persistence-service`, `security-service` — and for each matrix leg:

1. `dotnet restore`
2. `dotnet build -c Release`
3. `dotnet test` with coverage collection
4. enforces the **80% line-coverage gate** by reading `coverage.cobertura.xml`
5. builds and publishes **one container image per in-scope service per build**

Each leg runs the per-service path of §5 in that service's own directory, which is what makes the matrix a
genuine test of per-service independence rather than a partition of a single root build (C-I).

> **The coverage gate is evaluated per service, not repository-wide.** This is deliberate: a
> repository-wide average lets a well-covered service mask a poorly covered one, and the requirement is
> that each in-scope service clear the bar on its own (C-H). A leg fails if *its* service is below 80%
> line coverage, regardless of how the other three scored.

Only the four in-scope services appear in the matrix. The four deferred services have no project, no test
project and no container definition in this phase, so there is nothing for CI to build for them; see
[`DEFERRED.md`](DEFERRED.md).

For coverage mechanics in context — what the number is expected to cover, and which non-deterministic
values are masked so that a coverage run is repeatable — see [`PARITY.md`](PARITY.md).

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
  **2.0.0** version pulled transitively by `Microsoft.AspNetCore.OpenApi` 10.0.10. Under the
  warnings-as-errors gate of §3.1 that advisory is fatal, not advisory.
- The obvious fix — moving forward to the current major line — **breaks the build.** Version **3.9.0** was
  tested directly and produces **two `error CS0200` diagnostics**, reporting that a media-type example
  property cannot be assigned because it is **read-only**. They are raised inside the SDK's *own generated
  OpenAPI XML-comment support file*, not in repository code, so there is nothing local to fix.
- The cause is that the 10.0.10 source generator is compiled against the **2.x** object model.
- **2.11.0 — the highest published 2.x — is therefore the only value that is simultaneously
  non-vulnerable and compatible.** Every REST service must respect this pin.

#### `SQLitePCLRaw.bundle_e_sqlite3` = 3.0.5

- `Microsoft.EntityFrameworkCore.Sqlite` 10.0.10 transitively pulls a **2.1.x** native library that raises
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

Sourced from `Directory.Packages.props`. **Registry is nuget.org for every entry; there are no private or
internal feeds** in this refactor.

| Package | Version | Purpose | Consumed by |
| --- | --- | --- | --- |
| `Grpc.AspNetCore` | 2.83.0 | gRPC server, client and protocol-definition code generation. Pulls `Grpc.Tools` 2.83.0 and `Google.Protobuf` 3.31.1 transitively, so **neither needs an explicit reference** | Contracts, DataServices, Persistence, and the Gateway/DataServices client sides |
| `Microsoft.AspNetCore.OpenApi` | 10.0.10 | OpenAPI document generation for the REST surfaces | Gateway, Security, DataServices REST projection |
| `Microsoft.OpenApi` | **2.11.0 — mandatory pin** | OpenAPI object model (§11.1) | all REST services |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.10 | Inbound JWT validation on `/v1/ping` and every internal edge | all four services |
| `Microsoft.IdentityModel.JsonWebTokens` | 8.22.0 | Token **minting** — **Security only**, because Security is the sole issuer | Security |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.10 | EF Core provider for the only evidenced storage engine | Persistence |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | Design-time migration generation. The project owning migrations needs this with `PrivateAssets="all"` | Persistence |
| `Microsoft.Data.Sqlite` | 10.0.10 | ADO-level access for the legacy connection-URI grammar and raw command execution with positional binding, neither of which EF Core alone expresses | Persistence |
| `SQLitePCLRaw.bundle_e_sqlite3` | **3.0.5 — mandatory pin** | Native SQLite bundle (§11.1) | Persistence |
| `Microsoft.Extensions.Http.Resilience` | 10.8.0 | Retry and circuit-breaker on the newly created network edges (§11.5). Pulls `Microsoft.Extensions.Resilience` 10.8.0 and the Polly family | Gateway, DataServices |
| `Microsoft.AspNetCore.Mvc.Testing` | 10.0.10 | In-process host for service-level tests | all four `*.Tests` projects |
| `xunit.v3` | 3.2.2 | Unit and service test framework (§11.2) | all `*.Tests` projects |
| `xunit.runner.visualstudio` | 3.1.5 | Test adapter, so the standard test command discovers xunit.v3 tests | all `*.Tests` projects |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | Test host | all `*.Tests` projects |
| `coverlet.collector` | 10.0.1 | Emits `coverage.cobertura.xml`, the artifact the §10 gate reads | all `*.Tests` projects |

**npm — `tests/e2e` only:**

| Registry | Package | Version | Purpose |
| --- | --- | --- | --- |
| npmjs.org | `@playwright/test` | 1.62.1 | Cross-service workflow verification (§9) |

**Container images:**

| Stage | Image |
| --- | --- |
| Build | `mcr.microsoft.com/dotnet/sdk:10.0` |
| Runtime | `mcr.microsoft.com/dotnet/aspnet:10.0` (non-root; no `curl`/`wget` — see §7.1) |

**Transitive versions worth recording**, because they differ from the direct pins and will appear in lock
files:

- `Google.Protobuf` resolves to **3.31.1** rather than the newer published line, constrained by
  `Grpc.AspNetCore` 2.83.0.
- The Polly family resolves to **8.4.2**.
- The identity-model graph **mixes 8.19.2** (pulled by the bearer handler) **with 8.22.0** (the direct
  minting reference) — verified compatible, with zero warnings.

**No version above was taken from a repository dependency manifest, because there is none.** The
repository contains zero `*.csproj`, zero `packages.config`, zero `package.json` and no lock file of any
kind outside the tree this refactor creates. The only version facts the repository asserts about itself are
the legacy ones:

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
| **Any pinyin package** | The legacy lookup table exists only inside a closed binary, so bit-exact parity requires characterizing from the oracle rather than trusting a third-party table. See [`PARITY.md`](PARITY.md) |

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

**Claimed, because it was exercised:**

- The per-service path of §5 — restore, release build, and coverage-collecting test — was run end to end on
  .NET SDK 10.0.302 and produced `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`,
  `Passed!  - Failed:     0, Passed:     1`, and a `coverage.cobertura.xml` report.
- `dotnet new sln` emits `.slnx` (§2, Finding 1).
- Bare `dotnet test` builds Debug after a Release build, and `-c Release` changes that (§2, Finding 2).
- A second solution file in a service directory breaks the bare commands with `MSB1011` (§3.3).
- `dotnet new xunit3` does not exist in this SDK, `dotnet new xunit` scaffolds v2, and a hand-authored
  xunit.v3 project restores, builds, runs and emits Cobertura (§11.2).
- With both mandatory pins applied, restore is audit-clean and the build reports zero warnings and zero
  errors (§11.1).

**Not claimed, because it was not exercised:**

- **The container bring-up.** Docker was unavailable in the environment where this migration was planned,
  so the Compose path of §8 and its ordered health probes were never run. Correctness there rests on
  container-definition and Compose manifest review plus CI (§1.3, §8.2).
- Anything about build or runtime performance. No such objective is published in this repository, so none
  is asserted (§1.4, §11.5).

**Recorded rather than hidden:**

- §2 gives `MSB4014` as the solution-filter incompatibility code, matching the three sibling files listed
  there, **and** additionally records `MSB4025` and `MSB5026` as diagnostics a misused filter surfaces on
  SDK 10.0.302. All of them fail in all three verbs, and the operative guidance is identical in every
  case — **do not author a solution filter.** Both sets are given so that a reader who tests the claim
  finds what this document says they will find, which is the standard this document holds itself to
  throughout.

If you are adding a project, the four things to get right are: put it in the right per-service `.slnx` and
add no second solution file (§3.3); reference packages **versionlessly** (§3.2); if it is a test project,
hand-author it with `<OutputType>Exe</OutputType>` (§11.2); and expect the warnings-as-errors gate to be
enforced, including analyzer diagnostics (§3.1).
