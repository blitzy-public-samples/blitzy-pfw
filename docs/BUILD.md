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
> suite green — and with the repository-root `coverage.runsettings` applied, each clears the 80% line
> floor of §10 on its own Cobertura report. The `CS5001` an earlier revision of this document reported
> for the four service *application* projects is no longer reachable: every one now carries a
> `Program.cs` and produces an entry point. §13 records exactly what has and has not been exercised, and
> the runtime claims it still does **not** make.
>
> What the command does **not** prove is anything about a running system: it builds and tests each
> service **in process**, so no image has been built from it, no service has been started, and no request
> has crossed a network boundary. §8.3 and §13 hold that line at their own points of use.

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
| Compose bring-up detail and the readiness gates step by step | `orchestration/README.md` |
| The cross-service contract inventory | [`CONTRACTS.md`](CONTRACTS.md) |
| The four deferred destinations, which receive no project and no container in this phase | [`DEFERRED.md`](DEFERRED.md) |

## Current state of the artifacts this document references

Some artifacts referenced below are **planned and not yet present in this repository**. They are named
because they are where the corresponding work belongs, not because a reader can open them today:

| Artifact | What it will carry | State |
| --- | --- | --- |
| `orchestration/docker-compose.yml`, `orchestration/README.md` | Local orchestration and the readiness-gate bring-up. `orchestration/.env.example` is already present; the manifest and its readme are not | **Planned — not yet present** |
| `characterization/` | The paired legacy and target recording store | **Planned — not yet present** |

Everything else this document references — the solution and project files, the shared libraries, the
protocol and OpenAPI definitions under `shared/PowerFramework.Contracts/`, **all four service
applications with their handler trees and test projects**, **all four container definitions** (§7.1),
`.github/workflows/ci.yml` and `coverage.runsettings` (§10), the per-service settings,
[`PARITY.md`](PARITY.md), `orchestration/.env.example`, the Playwright specs under `tests/e2e/specs/` and
the read-only legacy tree — **is present in the tree today**.

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
| **Planned — not yet present** | No artifact exists. The text is the specification the work will be built against |

Applied to the commands in this document:

1. **Present and verified.** `dotnet restore` (audit-clean), and the build and test of the six shared and
   contracts projects — `0 Warning(s)`, `0 Error(s)`, and **8,278 tests passing, 0 failing**, measured by
   running `dotnet test <project> -c Release` once per test project. §5.5 lists the six with their
   individual counts.
2. **The four services, likewise built and tested.** Each application project compiles in Release with
   zero warnings and zero errors, each test project runs, and together they add **10,325 passing tests**
   and 4 skipped — bringing the whole solution to **18,603 passing, 4 skipped, 0 failing**. The four skips
   are the pinyin oracle characterization hooks, which require paired legacy recordings that do not exist
   yet. Every one of the four also clears its coverage floor: Gateway 88.77%, DataServices 91.19%,
   Persistence 88.05%, Security 91.68%.
3. **Enforced in CI, and separately verified by hand.** `.github/workflows/ci.yml` runs the per-service
   command shape above in four independent matrix legs and gates each on its own report; the gate step was
   also extracted verbatim from that workflow and run locally against all four real reports, passing for
   all four and failing correctly under four different injected faults. §13 says plainly what has and has
   not been exercised — notably that the workflow has not yet run on a GitHub-hosted runner.
4. **Planned — not yet present.** The Compose bring-up, the health probes, the CI pipeline, the container
   images and the Playwright run. Where a command belongs to this kind, the surrounding text says so.

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

The diagnostic codes quoted in §2, §3.2 to §3.4 and §11 were obtained the same way — by provoking each failure on
the pinned SDK and reading what it printed — rather than recalled from memory. §2 additionally
distinguishes the solution-filter code that the repository records repository-wide from the codes a
misused filter surfaces on this SDK, and §13 summarises that distinction.

### 1.3 What was not verified — stated plainly

**No verified multi-service container bring-up is claimed anywhere in this document.** The reason is no
longer that nothing exists to bring up — **all four container definitions are authored (§7.1) and so is
`.github/workflows/ci.yml` (§10)** — but that nothing assembles them into a stack:
`orchestration/docker-compose.yml` and `orchestration/README.md` are absent, so the §8 bring-up and its
ordered health probes have not been exercised and the health-condition chain has no expression anywhere in
the tree. One image, Security, was built and run and reached Docker health `healthy`; the other three have
not been built here.

**Container correctness for the stack is therefore not asserted, by review or by anything else.** Once the
Compose manifest is authored, correctness will rest on definition-and-manifest review plus the CI pipeline
of §10 — that is the intended assurance mechanism, and naming it is not the same as reporting that it has
run. Neither has happened. §8.3 restates this at the point of use, so a reader who
arrives there directly still sees it.

**What "unexercised" does and does not mean here, because the four services themselves ARE tested.** Every
service test drives its own service **in process**, through `WebApplicationFactory` or a test host, so the
handler behaviour, the status translation, the capability gate, the reserved routes and token issuance are
all covered by passing tests. What an in-process host does **not** exercise is the deployed topology: no
TLS handshake, no ALPN protocol negotiation, no real gRPC channel, no client-certificate presentation, no
container health probe and no Compose `depends_on` ordering. **No request in this system has crossed a
real network boundary between two services.**

The distinction matters and is held throughout: §1.2 is claimed and quotes its output, §1.3 is disclaimed.

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

Measured on SDK 10.0.302 **before this was addressed**: `dotnet build -c Release` in a service directory
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
| .NET SDK | **10.0.302** | Everything. Pinned by repository-root `global.json` |
| Node.js | **`>=22.12.0`**, declared as the floor in `tests/e2e/package.json`; v22.23.2 verified on the authoring host | The end-to-end tests of §9 **only** |
| npm | **`npm@11.18.0`**, pinned as `packageManager` in `tests/e2e/package.json`; verified sufficient on the authoring host | The end-to-end tests of §9 **only** |

The Node floor is **>= 22.12.0** rather than the looser `>= 22.0.0` an earlier manifest declared, and
rather than `@playwright/test`'s own `>= 20`. It is set to the version the environment's setup
documents as its requirement, so that a host satisfying `engines` also satisfies the environment: a
manifest whose floor is *below* the environment's would let `npm ci` succeed on a host the environment
considers unsupported, which is a check that passes without checking anything.
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

This is the primary build path, and it is the path that must work from a clean checkout for each service
independently (C-I). It is also the path the **planned** CI workflow of §10 will run — that workflow does
not exist yet, so nothing in this section is evidence that a pipeline has executed it.

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
[`ARCHITECTURE.md`](ARCHITECTURE.md). **The root `README.md` does not yet list them** — appending a
.NET section to it is planned and not yet done, so it cannot currently be used to cross-check this
table.

| Service directory | Application project | Test project | Port | Listener |
| --- | --- | --- | --- | --- |
| `services/persistence-service` | `PowerFramework.Persistence` | `PowerFramework.Persistence.Tests` | **5101** | `https://+:5101`, `Http1` — REST and the readiness probe |
| `services/persistence-service` | `PowerFramework.Persistence` | — | 5111 | `https://+:5111`, `Http2` — gRPC C-05..C-08 |
| `services/dataservices-service` | `PowerFramework.DataServices` | `PowerFramework.DataServices.Tests` | **5102** | `https://+:5102`, `Http1` — REST, the readiness probe and the thin projection |
| `services/dataservices-service` | `PowerFramework.DataServices` | — | 5112 | `https://+:5112`, `Http2` — gRPC C-03, C-04 |
| *(reserved)* | — | — | **5103** | commented-out Phase-2 slot |
| `services/security-service` | `PowerFramework.Security` | `PowerFramework.Security.Tests` | **5104** | `https://+:5104`, `Http1` |
| `services/gateway-service` | `PowerFramework.Gateway` | `PowerFramework.Gateway.Tests` | **5105** | `https://+:5105`, `Http1` |

Port 5103 is left reserved rather than reassigned; see [`ARCHITECTURE.md`](ARCHITECTURE.md) for the
reasoning and for the transport chosen per service.

**EVERY LISTENER TERMINATES TLS, AND THE TWO GRPC-CARRYING SERVICES DECLARE TWO ENDPOINTS EACH.** TLS
is not hardening here: every boundary in this list is created by the decomposition itself, every request
across one carries a bearer token, and Security additionally publishes the key set the whole estate
verifies against — so a cleartext listener would make every token replayable and the key set
substitutable (CWE-319). Two endpoints per gRPC-carrying service is retained rather than forced: each
endpoint pins ONE protocol version, so a probe and a gRPC channel each address a listener that can only
answer the thing it is for, and misaddressing either fails immediately instead of at a later layer. The documented ports keep exactly the meaning
the environment gave them and 5111/5112 add addresses it never named. Two earlier revisions are recorded
because both were withdrawn: one put a second listener per service on a parallel 5151–5155 band plus a
third for the token endpoint, and one declared TLS everywhere and rewrote the documented gate's scheme to
match — the first contradicted the fixed port map, the second made the documented bring-up unstartable.
None of this affects the build commands in this section; [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1 carries
the map with its measurements, and it is the map a *caller* must configure against.

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
four services now produces one. Note that the command above passes no settings file, which is deliberate:
it is the documented per-service command and must keep working with no extra argument. A report produced
that way covers the shared libraries and the generated protobuf stubs as well as the service, so its
top-level rate is **not** the per-service number. CI adds `--settings coverage.runsettings` to scope the
report to the service assembly alone; §5.5 gives both sets of figures. For what the coverage number is
expected to cover and which values are masked for determinism, see [`docs/PARITY.md`](PARITY.md).

### 5.5 What has actually been run in this repository, and what has not

All ten test projects build and pass. Measured by running `dotnet test <project> -c Release` once per
project, after `dotnet build PowerFramework.slnx -c Release`:

| # | Test project | Passed | Skipped | Failed |
| --- | --- | ---: | ---: | ---: |
| 1 | `shared/PowerFramework.Shared.Kernel.Tests` | 1,750 | 0 | 0 |
| 2 | `shared/PowerFramework.Shared.Diagnostics.Tests` | 607 | 0 | 0 |
| 3 | `shared/PowerFramework.Shared.Eventful.Tests` | 777 | 0 | 0 |
| 4 | `shared/PowerFramework.Shared.Localization.Tests` | 524 | 0 | 0 |
| 5 | `shared/PowerFramework.Shared.Containers.Tests` | 202 | 0 | 0 |
| 6 | `shared/PowerFramework.Contracts.Tests` | 4,418 | 0 | 0 |
| 7 | `services/gateway-service/PowerFramework.Gateway.Tests` | 883 | 0 | 0 |
| 8 | `services/dataservices-service/PowerFramework.DataServices.Tests` | 4,748 | 4 | 0 |
| 9 | `services/persistence-service/PowerFramework.Persistence.Tests` | 2,742 | 0 | 0 |
| 10 | `services/security-service/PowerFramework.Security.Tests` | 1,952 | 0 | 0 |
| | **Total** | **18,603** | **4** | **0** |

The four skips are the pinyin oracle characterization hooks. They are skipped rather than passed because
the paired legacy recordings they read do not exist yet, which is the honest state of the single parity
risk [`PARITY.md`](PARITY.md) records — a hook that passed without its oracle would be worse than one that
skips.

The whole-solution build reports `0 Warning(s)` and `0 Error(s)`. The `CS5001` an earlier revision of this
section reported for the four service application projects is no longer reachable: each now carries a
`Program.cs`.

**Per-service coverage, and why the same run yields two very different numbers.** Each figure is the
top-level `line-rate` of that service's own `coverage.cobertura.xml`:

| # | Service | Assembly | With `coverage.runsettings` | Without it |
| --- | --- | --- | ---: | ---: |
| 1 | `gateway-service` | `PowerFramework.Gateway` | **88.77%** | 22.77% |
| 2 | `dataservices-service` | `PowerFramework.DataServices` | **91.19%** | 50.93% |
| 3 | `persistence-service` | `PowerFramework.Persistence` | **88.05%** | 34.30% |
| 4 | `security-service` | `PowerFramework.Security` | **91.68%** | 13.39% |

The right-hand column is what an unscoped gate would read, and it is why the settings file exists rather
than being a convenience: all four services clear the 80% floor on their own code, and all four would fail
a gate that also counted the shared libraries and the thousands of generated protobuf sequence points in
`PowerFramework.Contracts`. Those six projects are not thereby unchecked — CI's `solution` job builds the
whole tree and runs all six of their test suites.

**What the service numbers are and are not.** They are real service tests: each of the four drives its own
service **in process**, through `WebApplicationFactory` or a test host, so the handlers, the status
translation, the capability gate, the reserved routes and token issuance are all covered. They are **not**
evidence about a deployed topology — an in-process host performs no TLS handshake, no ALPN negotiation, no
real gRPC channel setup and no client-certificate exchange (§1.3).

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

> **Three of the four `Dockerfile`s are authored; `services/persistence-service/Dockerfile` is not.**
> The three that exist — `gateway-service`, `dataservices-service` and `security-service` — are real files
> a reader can open, and the probe table below reports what each one actually does rather than what it
> should do. **No image has been built from any of them and no container has been started** (§1.3), so
> nothing in this section is a report of runtime behaviour. §7.2 in particular is a constraint the manifest
> and the definitions have to agree on, and it is the one most easily got wrong — the three existing
> definitions are authored against it, with every `COPY` path repository-root-relative.

Each service has its own container definition at `services/<service-name>/Dockerfile`, each producing
**one image per service** — four images, matching the four independently deployable services (C-J). Three
exist today; Persistence's does not.

Every image is **multi-stage**: an SDK image (`mcr.microsoft.com/dotnet/sdk:10.0`) for restore and build,
and an ASP.NET runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0`) for the final stage. The final
stage runs as a **non-root** user, per the baseline of §1.1.

One runtime-image property is worth knowing before writing a health probe: the ASP.NET runtime image
ships **without `curl` and without `wget`** — but it **does** ship `/usr/bin/openssl`, because the .NET
TLS stack depends on it. Both facts were measured on `mcr.microsoft.com/dotnet/aspnet:10.0` rather than
assumed. A probe that expects `curl` without installing it can never succeed, the `service_healthy`
condition of §8 can never be satisfied, and the readiness gate silently never opens.

**Installing a tool is one answer and it is not the only one, and the four definitions each chose by
whether the listener terminates TLS:**

| Definition | Probe mechanism | Installs anything? | Why that choice |
| --- | --- | --- | --- |
| `gateway-service` | `openssl s_client` piped a hand-written request, matching the status line | **No** | The 5105 ingress is **TLS-terminated**, so `/dev/tcp` cannot perform the handshake and a probe built that way would fail permanently. `openssl`, `bash` and `printf` are already in the image |
| `dataservices-service` | `openssl s_client` piped a hand-written request, reading the first response line | **No** | The 5102 listener is **TLS-terminated** and `/dev/tcp` cannot perform a handshake — a probe built that way would fail permanently. `openssl` 3.0.13 is already present, so the handshake and the request need nothing installed |
| `security-service` | `openssl s_client`, the same way | **No** | The 5104 listener is TLS too. An earlier revision installed `curl` here; because the base image is referenced by FAMILY tag its package set advances with every security rebuild, so that dependency's version **cannot** be pinned without the build failing the moment the archive supersedes it — which collides with the baseline of §1.1. The definition documents the pipeline clause by clause, including why `-quiet` is required (it implies `-ign_eof`, without which the response is never read) and why no SNI is sent (the target is an IP literal) |
| `persistence-service` | `curl --cacert` | **Yes** — `curl` only, in a single layer with the apt lists removed, before the `USER` switch | The 5101 listener is TLS; this is the one definition that accepts an install rather than working around it, and having `curl` it verifies the chain the same way the other three do |

A compose manifest should **inherit** these `HEALTHCHECK` declarations rather than declare a `curl`-based
one of its own, which would reintroduce the missing-tool problem for three of the four images. Every one of
the four verifies the presented chain against the mounted anchor; none passes `-k` or `--insecure`, because
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

> ### ⚠️ THIS ENTIRE SECTION IS A SPECIFICATION, NOT A RUNBOOK — NOTHING IN IT CAN BE RUN TODAY
>
> **`orchestration/docker-compose.yml` does not exist**, and neither does
> `services/persistence-service/Dockerfile`. There is therefore no manifest to invoke and no complete set
> of images to build, so **every `docker compose` command below will fail at the first line** — not
> because of a configuration mistake but because its subject is absent.
>
> What IS runnable today is exactly two things, and they are separated out into §8.0 below so a reader is
> never handed a command whose target does not exist: preparing the environment file, and generating the
> local key and certificate material. Everything from §8.1 onward is the specification the Compose work
> will be written against. §8.3 restates this at the end of the section for a reader who arrives there
> directly.

### 8.0 What can be run now — environment file and local material

Both steps below work against the tree as it stands. Neither starts anything.

```bash
set -euo pipefail
# The environment file is kept OUTSIDE the working tree -- see the warning below for why.
install -d -m 700 "$HOME/.config/powerframework"
cp orchestration/.env.example "$HOME/.config/powerframework/pfw.env"
chmod 600 "$HOME/.config/powerframework/pfw.env"
# Then fill in SECURITY_JWT_SIGNING_KEY and the six certificate paths, using the roster below and the
# generation commands in ARCHITECTURE.md section 9.3.1.
```

### 8.1 The bring-up command — PLANNED, and it cannot be run yet

Once the manifest exists — all four container definitions already do — one hand-authored Compose manifest
will bring all four services up together (C-J). From the repository root:

```bash
# PLANNED. orchestration/docker-compose.yml does not exist yet, so this fails today.
set -euo pipefail
cd orchestration
docker compose --env-file "$HOME/.config/powerframework/pfw.env" up --build -d
```

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
| `TLS_CERTIFICATE_PATH` / `TLS_CERTIFICATE_KEY_PATH` | Paths | The **shared multi-SAN server certificate and key**, consumed by Persistence, DataServices and Security as `Kestrel:Certificates:Default:Path` and `:KeyPath`. Not Security-specific: all three TLS listeners terminate with the same default material |
| `SECURITY_MTLS_CLIENT_CA_PATH` | Path | The authority whose client certificates Security accepts on `POST /v1/tokens` |
| `GATEWAY_MTLS_CERT_PATH` / `GATEWAY_MTLS_KEY_PATH` | Paths | Gateway's client certificate and key for the issuance edge |
| `DATASERVICES_MTLS_CERT_PATH` / `DATASERVICES_MTLS_KEY_PATH` | Paths | DataServices' client certificate and key for the same edge |

**`SECURITY_MTLS_CERT_PATH` and `SECURITY_MTLS_KEY_PATH` are not in that roster and must not be
reintroduced** — they belonged to a withdrawn second mutual-TLS listener. The mutual-TLS entries are
required rather than optional for any topology that serves a request: `POST /v1/tokens` is protected by
mutual TLS and by nothing else, because a caller cannot present a bearer token in order to obtain its
first bearer token. **Persistence has no client pair**, because it reads Security's anonymous key set and
calls nothing else there. See [`SECRETS.md`](SECRETS.md) §4 for the token topology and the full handling
rule, and §4.1.1 there for what is and is not enforced about the signing key.

**Two settings leaves look like validation of that key and are not.** Security's `appsettings.json`
carries `Security:SigningKeyFormat` (`PemOrPkcs8Base64`) and `Security:SigningKeyMinimumSizeBits` (2048),
and **`Configuration/SecurityOptions.cs` binds neither**: both leaves are inert, and changing either value
changes nothing. What each records is still true, but it is a record rather than a control:

- **The accepted format is fixed code.** `Tokens/SigningKeyProvider.cs` always attempts the same closed
  sequence — PEM first, both PKCS#8 and the older PKCS#1, then base64 of the DER encoding — and no
  configuration alters it. A value that is none of those **does** make the host refuse to start, because
  the import fails; not because the leaf was consulted.
- **The 2048-bit figure is operator guidance, not an enforced floor.** No key-size rule is applied
  anywhere: **a 1024-bit RSA key starts the host and mints tokens**, measured on the pinned toolchain and
  asserted as correct by `PowerFramework.Security.Tests`. Supply 2048 or more — this document recommends
  it — but do not cite the setting as a control, and enforce a real minimum in the process that issues
  the secret if one is required.

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

# 2. The mutual-TLS trust anchor for POST /v1/tokens -> SECURITY_MTLS_CLIENT_CA_PATH.
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=powerframework-local-ca" \
        -keyout mtls-ca.key -out mtls-ca.crt

# 3. The shared multi-SAN server certificate -> TLS_CERTIFICATE_PATH / TLS_CERTIFICATE_KEY_PATH, and
#    one client certificate per caller -> GATEWAY_MTLS_CERT_PATH / GATEWAY_MTLS_KEY_PATH and
#    DATASERVICES_MTLS_CERT_PATH / DATASERVICES_MTLS_KEY_PATH. Those commands are NOT duplicated
#    here: ARCHITECTURE.md section 9.3.1 carries them, including the subjectAltName set the server
#    certificate must cover and the -copy_extensions copyall that stops openssl dropping it.

chmod 600 ./*.key ./*.b64
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
single-line form is what an environment file can hold. **The size is checked too**: Security enforces
`Security:SigningKeyMinimumSizeBits` — 2048 by default, raisable and not lowerable — on its own signing
identity before any credential exists, and refuses a shorter key naming the measured size and the
setting. That floor applies to the ISSUER KEY ONLY; the `/v1/crypto` key-generation surface still
accepts 1024 bits, preserving the legacy allowance
[`ws_objects/pfw.shared.pbl.src/enums.sru:L965`], and the two are held apart by test.

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

### 8.2 The readiness model — the specification, not an observed behaviour

Every bullet below describes what the **planned** manifest and the endpoints must do. None of it has been
observed **across a network**: there is no Compose manifest, so nothing has been probed over a socket
(§8.3). The endpoints themselves are implemented and are covered by tests against an in-process host.

- `/health` is **anonymous on all four services**.
- `/v1/ping` **requires a JWT on all four** and returns `401` without one.
- **Gateway reports healthy only after Persistence, DataServices and Security do.** This is to be
  expressed with `depends_on` using a **health condition**, so Compose gates Gateway behind its three
  upstreams rather than merely behind their container start.
- The health gates address each service's **single** listener over **HTTP/1.1** — 5101, 5102,

- **Gateway and DataServices report `Degraded` — and therefore `503` — until their client certificates
  are mounted**, so the two gates on 5105 and 5102 depend on `GATEWAY_MTLS_*` and `DATASERVICES_MTLS_*`
  being supplied. This is not hardening bolted onto readiness; it is readiness answering its own
  question truthfully. Both services forward every operation with a bearer token, the only way to obtain
  one is Security's issuance edge, and contract C-01 protects that edge with **mutual TLS and nothing
  else** — because a caller cannot present a bearer token in order to obtain its first bearer token. An
  unset pair is deliberately **not** a startup failure, which is precisely why the readiness verdict has
  to be the thing that notices: a service that starts and then refuses every request is not ready, and a
  `200` there would open the dependency gate onto an instance that can serve nothing. Each service names
  the unmet setting in a `credentials` component entry of its `/health` body, and generating the local
  set is one command block — see [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.3.1.
- The `curl` health gates address the **HTTP/1.1** listener of each service — 5101, 5102, 5104 and
  5105. The gRPC surfaces of Persistence and DataServices are on 5111 and 5112 and are not probed, because
  an HTTP/1.1 `GET` against an `Http2` endpoint answers `400`. All **four** gates are `https`; see
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1.

**All four gates must verify the chain, which means naming the local CA.** The certificate the four TLS
listeners present is issued by the throwaway CA generated in §8.0 and is trusted by nothing by default, so
a probe that does not name it fails on chain validation rather than on readiness — a false negative that
reads exactly like a service that never came up:

```bash
set -euo pipefail
CA="$HOME/.config/powerframework/secrets/mtls-ca.crt"

curl -sf --cacert "$CA" https://localhost:5101/health   # Persistence
curl -sf --cacert "$CA" https://localhost:5102/health   # DataServices
curl -sf --cacert "$CA" https://localhost:5104/health   # Security
curl -sf --cacert "$CA" https://localhost:5105/health   # Gateway ingress
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
`orchestration/README.md`, which is **planned and not yet present**; nothing is duplicated here.

### 8.3 This path is unexercised — restated at the point of use

**The Compose bring-up above cannot be run today, and has not been verified.** What is missing is now a
short list rather than everything: the manifest it invokes does not exist, and one of the four container
definitions it would build — `services/persistence-service/Dockerfile` — does not exist either. The other
three are present, and **all four service applications have an entry point**: every one of them builds to a
runnable executable and starts under `dotnet run`. Docker was additionally unavailable in the environment
where this migration was planned, so neither the bring-up nor its ordered health probes could have been
exercised in any case.

**Correctness of this path is not asserted at present.** Once the manifest and the fourth container
definition are authored, it will rest on definition-and-manifest review plus the CI pipeline of §10;
neither has occurred, because the manifest is absent and no workflow file exists. Treat the commands in
this section as the **intended** path — the specification the remaining work will be written against — and
not as a transcript of a successful run. §1.3 states the same limitation for a reader who started at the
top.

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
`testMatch` verified against the directory at config load. What those specs cannot do yet is exercise a
running stack: all four services build and test clean, but no orchestration manifest exists to bring them
up together (`orchestration/docker-compose.yml` is absent — §13), so every assertion that needs a live
endpoint fails its issuance precondition or **skips with an explicit reason** naming what was probed and
the bring-up command, and only the static gates below actually prove anything without a stack. The
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
2. `dotnet build --configuration Release --no-restore`
3. `dotnet test --configuration Release --no-build` with the coverage collector and
   `--settings coverage.runsettings`
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
one addition to that path is `--settings`: without it the report also covers the shared libraries and the
generated protobuf stubs, and the top-level rate reads between 10% and 45% for services whose own
assemblies are all above 88% — see §5.5 for both sets of figures, and `coverage.runsettings` for the
reasoning.

**Three checks, not one, and the second and third are what make the first trustworthy.** Each leg asserts
that the line rate clears the floor, that the report contains **exactly one** package — which proves the
settings file applied — and that the package is that service's own assembly, which proves the leg measured
the service it claims to. A filter that silently stopped matching would otherwise turn the gate into a pass
for whatever happened to be measured instead.

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

Transcribed from `Directory.Packages.props` and `tests/e2e/package.json` — **both authored by this
refactor**, so this table reports what those manifests pin rather than a version inherited from anywhere.
No version here came from a **pre-existing** manifest, because the repository had none: see the note
following the table. **Registry is nuget.org for every .NET entry and npmjs.org for the three npm ones;
there are no private or internal feeds** in this refactor.

| Package | Version | Purpose | Consumed by |
| --- | --- | --- | --- |
| `Grpc.AspNetCore` | 2.83.0 | gRPC server, client and protocol-definition code generation. Pulls `Grpc.Tools` 2.83.0 and `Google.Protobuf` 3.31.1 transitively, so **neither needs an explicit reference** | Contracts, DataServices, Persistence, and the Gateway/DataServices client sides |
| `Microsoft.AspNetCore.OpenApi` | 10.0.10 | OpenAPI document generation for the REST surfaces | Gateway, Security, DataServices REST projection |
| `Microsoft.OpenApi` | **2.11.0 — mandatory pin** | OpenAPI object model (§11.1) | all REST services |
| `Microsoft.OpenApi.YamlReader` | 2.11.0 | YAML reader for the object model above, which ships a JSON reader only. Version locked to the `Microsoft.OpenApi` release it pairs with, so it cannot drag the mandatory pin off 2.11.0 | `shared/PowerFramework.Contracts.Tests` only — the two contract definitions it loads are YAML |
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
| npmjs.org | `typescript` | 5.9.3 | The `npm run typecheck` gate — `tsc --noEmit`. **Deliberately the 5.x line, not `latest`**: 5.9.3 is the mature compiler, whereas the current `latest` is the 7.x native rewrite, which `@playwright/test` 1.62.1's own type definitions are not validated against |
| npmjs.org | `@types/node` | 22.20.1 | Node globals for the type gate — the endpoint fixtures read `process.env`. **Deliberately the 22.x line to match the Node 22 runtime**; the 26.x line would type-check against APIs the runtime does not have |

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

**Claimed, because it was exercised:**

- **The command *shape* of §5 works** — restore, release build, and coverage-collecting test — run end to
  end on .NET SDK 10.0.302, producing `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`,
  `Passed!  - Failed:     0, Passed:     1`, and a `coverage.cobertura.xml` report. **That run was against
  a throwaway skeleton project, not against this repository's services** — the single passing test is the
  giveaway — and it is quoted here as evidence that the command and the coverage collector work, not as a
  result for these four services.
- **In this repository today**, restore is audit-clean and **the whole solution builds** with
  `0 Warning(s)` and `0 Error(s)` — the six shared libraries, the contracts project, all four service
  applications and all ten test projects. Measured with `dotnet build PowerFramework.slnx -c Release`.
- **All ten test projects run, and all pass.** `dotnet test -c Release` over the repository solution reports
  **18,603 passing, 4 skipped, zero failing**. Tied to the command that produced it rather than quoted
  loose, and split so the sum is checkable: shared layer **8,278** (Kernel 1,750; Diagnostics 607;
  Eventful 777; Localization 524; Containers 202; Contracts 4,418) and service layer **10,325**
  (DataServices 4,748 plus the 4 skipped; Persistence 2,742; Security 1,952; Gateway 883). The four
  skips are the pinyin oracle characterization hooks, which are skipped rather than passed because the
  paired legacy recordings they require do not exist yet. Any total quoted without naming the command
  that produced it should be treated as stale.
- **The four per-service coverage gates pass, measured.** With the repository-root `coverage.runsettings`
  applied, each service's own Cobertura report carries exactly one package and its top-level line rate is
  that service's number: **Gateway 88.77%, DataServices 91.19%, Persistence 88.05%, Security 91.68%** —
  every one above the 80% floor of §10. Without that settings file the same four reports read 22.77%,
  50.93%, 34.30% and 13.39%, because they then also cover the shared libraries and the generated protobuf
  stubs; `coverage.runsettings` records both sets of figures and the reasoning.
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

- **That any service starts, or serves a request, in a container.** Every one of the four **compiles**, and
  its tests and coverage gate pass, and its image **builds** — those three are claimed above and in §8.2.
  What is not claimed is runtime behaviour inside a container: no service was started from its image and
  no request was served through one here.
- **The Compose bring-up, and any review of it.** `orchestration/docker-compose.yml` **does not exist**;
  only `orchestration/.env.example` does. So the §8 Compose path could not be run and there was nothing to
  review. All four `Dockerfile`s now exist, which is what makes the definition review of §8.2 possible at
  all, but the five ordered health-probe gates of the documented bring-up remain unexercised. Nothing here should be read as evidence that the four services come up together or
  that Gateway's readiness chain behaves as designed (§1.3, §8.2).
- **That the coverage gate has run on GitHub's runners.** `.github/workflows/ci.yml` now exists and
  enforces the 80%-per-service floor of §10 from each service's own Cobertura report, and the gate step
  was extracted verbatim from that workflow and executed locally against all four real reports — it passes
  for all four and fails correctly when the floor is raised above a measured rate, when handed an
  unfiltered report, when handed a report for the wrong assembly, and when no report is produced. What has
  not happened is a run of the workflow itself on a GitHub-hosted runner, so the `actions/*` and
  `docker/*` step versions, the registry authentication and the artifact upload are reviewed rather than
  exercised.
- **That any image has been published.** The image job builds all four and pushes them to the GitHub
  container registry only on a push to the default branch, authenticated with the automatically
  provisioned token. No push has occurred from here; the four images were built locally to prove the
  Dockerfiles, and one of them — `services/persistence-service/Dockerfile` — was the last of the four to
  exist and is verified to produce a non-root image on port 5101 with a declared volume and a TLS health
  probe.
- **That any service has served a request across a network.** Every service test drives its host **in
  process**, so no test performs a TLS handshake, ALPN negotiation, real gRPC channel setup or
  client-certificate exchange. Nothing in this document should be read as evidence about a deployed
  topology (§5.5, §1.3).
- **Characterization parity.** `characterization/` does not exist, so no paired legacy and .NET recording
  has been captured and no parity comparison has been performed (§1.3).
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
