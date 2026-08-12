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

# PowerFramework → .NET 10 — Target Architecture

This document is the authoritative reference for the **service topology**, the **transport chosen for
each service**, the **port map**, and **capability gating** of the four-service .NET 10 decomposition
of PowerFramework. Several sibling documents are written against it.

It describes **Phase 1**, which implements exactly four of an eight-service target roster. The other
four are mapped to a destination but receive no code, no test and no container; see
[`DEFERRED.md`](DEFERRED.md).

**Audience.** Anyone who needs to know *why* a boundary is where it is, not merely where it is. Every
architectural claim below carries a locator into the read-only legacy tree so a reader can check it
without taking this document's word for anything.

**What this document deliberately does not duplicate.** Cross-reference rather than restatement is the
rule here, because a figure repeated in two places is a figure that will eventually disagree with
itself:

| For | See |
| --- | --- |
| The full-estate mapping — all 39 libraries and all 544 objects assigned to a destination | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| The cross-service contract inventory and the four reserved Gateway extension points | [`CONTRACTS.md`](CONTRACTS.md) |
| Build and test commands, the solution layout, and per-service build independence | [`BUILD.md`](BUILD.md) |
| The characterization model, the fixture corpus and the determinism seams | [`docs/PARITY.md`](PARITY.md) |
| Secret locators, severities, required actions and the token-topology register | [`SECRETS.md`](SECRETS.md) |
| The four deferred destinations in detail and their assigned objects | [`DEFERRED.md`](DEFERRED.md) |

## Current state of the artifacts this document references

Some artifacts referenced below are **planned and not yet present in this repository**. They are named
because they are where the corresponding work belongs, not because a reader can open them today:

| Artifact | What it will carry | State |
| --- | --- | --- |
| `orchestration/docker-compose.yml`, `orchestration/README.md` | Local orchestration and the readiness-gate bring-up. `orchestration/.env.example` is already present; the manifest and its readme are not | **Planned — not yet present** |
| `characterization/` | The paired legacy and target recordings | **Planned — not yet present** |
| `characterization/recordings/` | The paired capture store, once a legacy oracle run exists to pair against | **Planned — not yet present** |

Everything else this document references — the solution and project files, the shared libraries, the
protocol and OpenAPI definitions under `shared/PowerFramework.Contracts/`, **all four service
applications with their handler trees and test projects**, **all four container definitions**,
`.github/workflows/ci.yml` and `coverage.runsettings`, the per-service settings,
[`PARITY.md`](PARITY.md), `orchestration/.env.example` and the read-only legacy tree — **is present in the
tree today**. What is missing is the thing that ASSEMBLES the four images into a stack, which is why the
bring-up claim below stays disclaimed.

**What that does and does not license this document to claim.** The four services are real: each builds
with zero warnings, each has a test project that exercises its handlers **against an in-process host**,
and every boundary behaviour described below — status translation, the capability gate, the reserved
routes, health aggregation, token issuance — is implemented and covered by tests at that level.

What has **never happened** is the whole stack running. With no Compose manifest, no Persistence image
and no CI pipeline, **no request in this system has crossed a real network boundary between two
services**, no aggregated `/health` has answered against three live upstreams, and no paired
characterization recording exists. So every statement below about behaviour **within** a service
describes tested code, and every statement about what happens **between** services is a design verified
in-process rather than an observation of a deployed system. §10.6 is the authority for that distinction
and restates it at its point of use.

---

## Table of contents

1. [Position, method and evidence discipline](#1-position-method-and-evidence-discipline)
2. [The starting point: a library, not a server](#2-the-starting-point-a-library-not-a-server)
3. [Target topology](#3-target-topology)
4. [Port, transport and endpoint map](#4-port-transport-and-endpoint-map)
5. [Transport chosen per service](#5-transport-chosen-per-service)
6. [Capability gating: the legacy's own decomposition intent](#6-capability-gating-the-legacys-own-decomposition-intent)
7. [The composition root](#7-the-composition-root)
8. [Storage](#8-storage)
9. [Security and token topology](#9-security-and-token-topology)
10. [Orchestration](#10-orchestration)
11. [Design patterns and the legacy-to-target mapping](#11-design-patterns-and-the-legacy-to-target-mapping)
12. [Deliberately absent](#12-deliberately-absent)
13. [Known limitations and anomalies](#13-known-limitations-and-anomalies)
14. [Closing note: what this architecture does and does not claim](#14-closing-note-what-this-architecture-does-and-does-not-claim)

---

## 1. Position, method and evidence discipline

### 1.1 No user-specified rules exist

The project's rules document was retrieved and contains exactly one statement: **no user rules were
provided.** That is a finding, not an omission, and three consequences follow:

- **No rule is invented, inferred, or back-filled from convention.** Nothing in this document is
  presented as a requirement because it is a common practice.
- **Enterprise-standard best practice applies in the rules' place.** For an architecture document
  that means every claim is traceable to evidence, every decision records the alternative that was
  rejected and the reason it was rejected, and nothing is asserted that the repository cannot
  support.
- **Zero files enter scope because of a rule.** This document exists because the migration plan
  requires it.

The binding constraints therefore come from elsewhere — the migration brief's own clauses and the
attached environment's setup instructions. They are referred to below by their identifiers (C-A
through C-L) rather than reproduced.

### 1.2 The legacy tree is read-only, and it is the only specification

`ws_objects/**`, the compiled libraries and target files, `oldversion/125/**`, `pack/**`, the native
binaries, `res/**` and the legacy browser assets under `tests/blink`, `tests/sciter` and
`tests/webview` are the **behavioural oracle**. They are never edited, moved, renamed or reformatted
(C-C).

That read-only status extends explicitly to the five pre-existing Chinese documents in this very
folder — [`README.md`](README.md), [`Blink交互.md`](Blink交互.md), [`Sciter交互.md`](Sciter交互.md),
[`PB多线程绕坑提示.md`](PB多线程绕坑提示.md) and
[`n_cst_dwsvc_columnexp.md`](n_cst_dwsvc_columnexp.md). This document **cites** them; it does not
touch them, translate them, re-encode them or rewrite their links. Where one of them is inaccurate —
and §7.3 records two places where one is — the correction is stated *here*, never applied *there*.

### 1.3 Locator convention and why nothing else can adjudicate behaviour

Every behavioural claim carries a `path:Lnn` locator. That discipline is not ceremony; it is forced
by the fact that no other artifact in the repository can settle a question of behaviour:

- **The changelog is stale.** `logfile.md` opens at `## 3.0.7.2062(2022-04-14)` while the commit
  history runs years later, and there are **zero Git tags** across the repository's history, so no
  release is marked.
- **Neither PowerBuilder build definition would build as written.** `ws_objects/pfw.pbl.src/project.srj`
  declares 29 `PBD:` lines and `ws_objects/pfw.pbl.src/p_pfw.srj` declares 28; they disagree on the
  vendor string; and **both reference `pfw.utility.imgcodec.pbl`**
  [`project.srj:L41`, `p_pfw.srj:L40`] — a library that exists nowhere in the repository. They are
  read for build *intent* only.

Consequently the .NET build, CI and orchestration are authored as clean creations rather than
translated from an existing definition, and every behavioural assertion in the generated code traces
to a `ws_objects/**` locator.

---

## 2. The starting point: a library, not a server

The target topology only makes sense against what it replaces, so this section establishes the
starting point first.

### 2.1 What one PowerBuilder process actually is

The estate is **39 exported library folders** under `ws_objects/` against **40 compiled root `*.pbl`
files** — reconciled by the one library that appears on a target's library list with no corresponding
source export, `pfwx.utility.codec.pbl` [`pfwx.pbt`, `LibList`].

An application composes its world by *listing* libraries in its target file, and the primary target
does so explicitly: `pfw.pbt`'s `LibList` names **32** libraries in a single ordered string; the
secondary target `pfwx.pbt` names **9**, of which exactly two — `pfw.common.pbl` and `pfw.shared.pbl`
— are shared with the primary target, which is what makes those two the cross-target foundation.

The consequences of that mechanism are the architectural facts that matter:

- **One flat global namespace.** PowerBuilder has no namespaces and no import statements. Every
  global object lives in one namespace, and **symbol resolution is determined by the order of the
  library list** [`pfw.pbt`, `LibList`]. There are therefore no import statements to rewrite in this
  refactor — the work is namespace *creation* plus resolving the collisions a flat namespace
  tolerated.
- **Every call is in-process and synchronous** unless explicitly posted to the Win32 message queue.
- **Object identity is a pointer.** Structures hold live object references, and §13 records the one
  place where that genuinely cannot cross a process boundary.
- **Events are dispatched by the PowerBuilder runtime**, not by application code.
- **Cross-thread work uses a hand-built proxy-pair pattern** in which every concurrency class exists
  twice — a caller-side `n_cst_threading*` and a worker-side `n_cst_thread*` — so that no object is
  ever touched from two threads.

### 2.2 The two consequences that shape every boundary

Now the fact that makes this refactor unusual, stated plainly:

> **PowerFramework is a library, not a server.** It has no process of its own, no listener, no route
> table, no serialization layer, and no authentication of any kind — because there is nothing to
> authenticate against.

Two consequences follow, and both are load-bearing for everything below:

1. **Every cross-service contract in this refactor is net-new.** None of them translates an existing
   wire format, because no wire format exists to translate. This is why the contract inventory is
   published for review as its own artifact rather than emitted silently as a by-product of code
   generation — see [`CONTRACTS.md`](CONTRACTS.md).
2. **Decomposition creates the system's first-ever ingress.** The legacy opens no listening socket,
   registers no route and receives no unsolicited request. That is precisely why Security exists as a
   Phase-1 service, and why a JSON Web Token is required on **every internal edge** rather than only
   at the outer edge. §9.1 works through why the "no new attack surface" requirement cannot be read
   literally.

### 2.3 The threading hazards that constrain the Persistence design

The legacy carries its own threading note, [`PB多线程绕坑提示.md`](PB多线程绕坑提示.md) (read-only),
and it documents two hazards that directly constrain how the SQL layer may be re-expressed:

- **A worker thread synchronously calling a main-thread object function or event whose return type is
  `string` or `blob` may raise a memory exception** [`docs/PB多线程绕坑提示.md:L1-L4`]. The stated
  trigger is a main-thread object having an unexecuted `Post` message while the object itself has
  been destroyed. The stated remedies are to avoid such calls, or to return the value through a `ref`
  parameter instead.
- **A worker thread that receives a main-thread object must release it explicitly** — the note
  requires the worker to set the referenced main-thread object to null (`SetNull`) in its `OnUninit`
  event [`docs/PB多线程绕坑提示.md:L5`].

These two hazards are *why* the proxy pair of §2.1 exists. They are the reason the port reproduces an
**explicit marshalling boundary** between the caller side and the worker side rather than flattening
the pair into a single async method: the thread-affinity split is a contract that the legacy encodes
in its own object comments, not an implementation detail that a modern async idiom subsumes. The same
reasoning is why the buffer carrier is an anti-corruption layer rather than a rowset (§11.1).

---

## 3. Target topology

### 3.1 The four services

Four ASP.NET Core services on `net10.0`, **each with its own per-service solution file and each
destined for its own container image**, so that every one restores, builds and tests from a clean
checkout without reference to any other service's project (C-A). A single collapsed solution containing
four folders is a failure mode, not a cautious choice, and is not the shape here.

**Two states, kept apart deliberately.** The four per-service `.slnx` files, project files, settings, test
projects and `Dockerfile`s are **present in the tree**, all four applications compile in Release with zero
warnings, and all four test suites pass. What is still **planned** is running them as a stack:
`orchestration/docker-compose.yml` does not exist, so only one image — Security's — has been built and
started here (§10.6), no aggregated `/health` has had a stack to aggregate, and the documented bring-up
and its health gates remain unexercised. This
section describes the target structure; [`BUILD.md`](BUILD.md) §5.5 and §13 record exactly what has been
built and run and what has not.

| Service | Directory | Project | Capability it owns | Legacy anchor |
| --- | --- | --- | --- | --- |
| **Gateway** | `services/gateway-service` | `PowerFramework.Gateway` | Sole ingress and composition root; request routing; capability gating; the reserved extension points | The application objects and the framework lifecycle contract [`ws_objects/pfw.pbl.src/pfw.sra:L88-L108`], [`docs/README.md` §初始化] |
| **DataServices** | `services/dataservices-service` | `PowerFramework.DataServices` | The DataWindow retrieval / validation / update triple, the 22-event DataWindow chain, and the column-expression engine | `ws_objects/pfw.datawindow.services.pbl.src/` (13 objects) |
| **Persistence** | `services/persistence-service` | `PowerFramework.Persistence` | **The only service that generates or executes SQL, and the only one holding a storage provider** | `ws_objects/pfw.utility.sqlite.pbl.src/` (3), `ws_objects/pfw.thread.pbl.src/` (6), `ws_objects/pfw.thread.ext.pbl.src/` (15) |
| **Security** | `services/security-service` | `PowerFramework.Security` | The keyed cryptographic surface; **sole JSON Web Token issuer** for all four services | `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73` |

These directory and project names are canonical, and they are used identically in
[`BUILD.md`](BUILD.md) and [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md).

**The root `README.md` does not yet carry them.** It still holds only its original legacy content, and
appending a .NET section that names these services and their ports is **planned work that has not been
done** — so a reader should not expect the root README to corroborate this table today. That update is
the one and only modification this refactor makes to a pre-existing file, and it is deliberately
scoped so the existing licence text and its Chinese restatement are preserved verbatim.

Two boundary justifications are worth drawing out, because they are the ones a reader is most likely
to question:

- **Why Persistence owns SQL exclusively.** `ws_objects/pfw.thread.ext.pbl.src/` is entirely
  SQL-shaped: its two structures are literally `dberrordata.srs` and `transactiondata.srs`, and 13 of
  its 15 objects are SQL query, command, update and transaction tasks. Asynchronous SQL is the whole
  reason that library exists, so it cannot be separated from SQL without dissolving it.
- **Why Security is a Phase-1 service rather than a library.** The legacy already contains the
  signing primitives a token issuer needs — `RSASign` and `VerifyRSASign`
  [`ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73`] — inside a surface of 65 declared functions
  at `:L9-L73`, of which 63 are cryptographic operations and two are version and copyright
  accessors. Concentrating that surface behind one boundary is what makes a single signing authority
  possible at all (§9.2).

The full-estate capability assignment — which of the 544 objects goes where, including the deferred
majority — is [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md)'s subject and is not restated here.

### 3.2 The call graph — layered and acyclic

The graph is strictly layered, and it is acyclic. **Two kinds of edge exist and they must not be
conflated**, because one carries capability and the other carries only readiness:

**Capability calls** — a service invoking another service's contract:

- **Gateway** calls DataServices and Security.
- **DataServices** calls Persistence and Security.
- **Persistence** reads Security's published verification keys only.
- **Nothing calls Gateway except external clients; nothing but Gateway calls DataServices; nothing
  but DataServices calls Persistence.**

**Readiness observation** — an anonymous `GET /health` probe and nothing else:

- **Gateway probes all three upstreams**, Persistence included, because C-10's aggregate names each of
  the three individually so an operator reading a failed aggregate learns *which* one is responsible.
- **The Persistence edge is readiness-only, and that is enforced by construction rather than by
  convention.** The three probe addresses live in a separate configuration group, `Gateway:HealthProbes`,
  from the two services Gateway invokes, `Gateway:Upstreams` — which deliberately has **no** Persistence
  member. Gateway holds no Persistence client, no channel and no generated stub, so **nothing beyond
  `GET /health` is reachable on that address**. §4.2 carries the same statement at the readiness model.
- The probe is anonymous on all four services, so it needs no token and works while a service is still
  starting — which is the whole point of a readiness probe.

Gateway probing Persistence is therefore **not** a violation of the layering above: observing that a
service is alive is not reaching into it. This is what satisfies the constraint that no service reaches
into another service's internals. The **only** cross-service coupling anywhere in the system is the
published contracts project: it is the boundary *definition*, not a shared-code back door. No
behavioural code crosses a service boundary.

Because there is no cycle, each service can be deployed and scaled independently of the others — an
architectural property of the topology, and the whole point of C-J.

### 3.3 Diagram

Solid boxes are the four in-scope services; the four dashed boxes reached by dotted edges are reserved
routes. **Each solid box names a service directory, project, settings, application and test project that
are all present in the tree and build clean** — what none of them has is a *running process*: all four
container definitions exist, but no manifest assembles them, and only Security's image has been built and
started here (§10.6 and [`BUILD.md`](BUILD.md) §13). The edge labels carry the **actual listener
ports** of §4.1, gRPC included.

**Three edge styles, and the difference between the first two is the point.** A solid arrow is a
**capability call** — one service invoking another's contract. A thick dashed arrow is **readiness
observation only** — an anonymous `GET /health` probe, with no client, channel or stub behind it. A thin
dotted arrow is a **reserved route**: routing metadata with nothing behind it at all. Gateway's edge to
Persistence is the readiness kind, which is why the layering rule of §3.2 still holds.

```mermaid
graph TB
    EXT["External clients"]
    GW["gateway-service<br/>PowerFramework.Gateway<br/>sole ingress + composition root"]
    DS["dataservices-service<br/>PowerFramework.DataServices"]
    PER["persistence-service<br/>PowerFramework.Persistence<br/>only SQL, only storage provider"]
    SEC["security-service<br/>PowerFramework.Security<br/>SOLE TOKEN ISSUER"]
    DB[("persistence-db<br/>SQLite")]

    EXT -->|"REST / OpenAPI :5105"| GW
    GW -->|"gRPC :5112"| DS
    GW -->|"REST :5104"| SEC
    DS -->|"gRPC :5111"| PER
    DS -->|"REST :5104"| SEC
    PER -->|"JWKS :5104"| SEC
    PER --> DB

    GW ==>|"health :5101"| PER
    GW ==>|"health :5102"| DS
    GW ==>|"health :5104"| SEC

    D1["/v1/design/**<br/>DesignSystem"]
    D2["/v1/documents/**<br/>Documents"]
    D3["/v1/integration/**<br/>Integration"]
    D4["/v1/scripting/**<br/>ScriptBridge"]

    GW -.->|"501"| D1
    GW -.->|"501"| D2
    GW -.->|"501"| D3
    GW -.->|"501"| D4

    classDef built fill:#e8eaf6,stroke:#3949ab,stroke-width:1px,color:#1a237e
    classDef store fill:#e0f2f1,stroke:#00695c,stroke-width:1px,color:#004d40
    classDef reserved fill:#fafafa,stroke:#9e9e9e,stroke-width:1px,stroke-dasharray:5 4,color:#616161
    classDef ext fill:#ffffff,stroke:#616161,stroke-width:1px,color:#212121

    class GW,DS,PER,SEC built
    class DB store
    class D1,D2,D3,D4 reserved
    class EXT ext
```

**The three thick edges labelled `health` are readiness observation and nothing more.** They are drawn
separately from the capability calls because Gateway probes **all three** upstreams — Persistence
included — while calling only two of them. Gateway holds no Persistence client, channel or generated
stub, so `GET /health` is the whole of what is reachable on `:5101` from Gateway; §3.2 and §4.2 carry the
configuration split (`Gateway:HealthProbes` versus `Gateway:Upstreams`) that makes that structural rather
than conventional.

**The dotted edges are routing metadata only.** No project, no container, no test and no
placeholder class exists behind any of them — which is why those four nodes are drawn dashed and
unfilled rather than as services. §4.4 states that distinction in full, because it is the one an
auditor should be able to check without ambiguity.

---

## 4. Port, transport and endpoint map

### 4.1 The map

The attached environment fixes a 5101–5105 band and publishes the composition root at port 5105. That
band and that port are preserved (C-L) so the documented access URL still resolves; the band is
re-mapped onto the four real services.

| Directory | Project | Port | Listener | Transport | Token role |
| --- | --- | --- | --- | --- | --- |
| `services/persistence-service` | `PowerFramework.Persistence` | **5101** | `https://+:5101`, `Http1` | REST `/health` and `/v1/ping` — **the documented readiness address** | verification only |
| `services/persistence-service` | `PowerFramework.Persistence` | 5111 | `https://+:5111`, `Http2` | gRPC (C-05..C-08) | verification only |
| `services/dataservices-service` | `PowerFramework.DataServices` | **5102** | `https://+:5102`, `Http1` | REST `/health`, `/v1/ping` and the thin projection for Gateway | verification only |
| `services/dataservices-service` | `PowerFramework.DataServices` | 5112 | `https://+:5112`, `Http2` | gRPC (C-03, C-04) | verification only |
| *(reserved)* | — | **5103** | — | — | **commented-out DesignSystem Phase-2 slot** |
| `services/security-service` | `PowerFramework.Security` | **5104** | `https://+:5104`, `Http1` | REST + `/.well-known/jwks.json` + OIDC discovery, and the issuance edge | **SOLE ISSUER** |
| `services/gateway-service` | `PowerFramework.Gateway` | **5105** | `https://+:5105`, `Http1` | REST + OpenAPI | verification only |

**Every documented address is preserved, and nothing the environment fixes is moved.** The environment
publishes `/health` on 5101–5105 and the composition root on 5105; all five keep exactly the meaning
they were given, 5103 stays unallocated, and the composition root is reachable at
`https://localhost:5105`. The environment documents **no gRPC address at all**, which is why the two
services carrying gRPC contracts can add one without deviating from anything: 5111 and 5112 are new
addresses rather than reassigned ones.

**All four services declare their own listener, Gateway included — and Gateway's is a correction.** An
earlier revision left the ingress address to the orchestration layer on the reasoning that the published
address belongs to whatever fronts it. The reasoning is defensible; its consequence was not. No Compose
manifest exists yet, and the orchestration template's variable roster carries no `ASPNETCORE_URLS`, so
**nothing in the repository bound 5105**: a plain `dotnet run` bound Kestrel's own default of 5000
— measured directly — and the one address this document, the attached environment and the end-to-end
suite all name as the composition root was reachable by no means the repository provided. A documented
address that nothing binds is worse than an undocumented one, because a reader has no reason to doubt it.
An overriding environment still wins, since `ASPNETCORE_URLS` and `Kestrel__Endpoints__Default__Url` both
take precedence over a settings file.

**All four listeners terminate TLS, and on a decomposition that is a correctness property rather than
a hardening option (CWE-319).** Every one of these boundaries is created by this refactor — the legacy
opens no listening socket and receives no unsolicited request — and every request that crosses one
carries a bearer token, while Security additionally publishes the key set the whole estate verifies
against. On cleartext, every token is replayable by anyone on path and the key set is substitutable,
which makes an attacker's signature indistinguishable from Security's. §0.1.4 of the plan settles the
reading of the no-new-attack-surface requirement: it cannot mean "no new surface", because decomposition
creates the system's first-ever ingress; it means every newly created surface is authenticated from the
outset — and a surface whose channel is readable authenticates nothing an observer cannot replay.

The attached environment gates readiness on `curl -sf http://localhost:<port>/health` and supplies no
certificate material. Neither fact is a licence to ship cleartext: the gate is a *probe shape*, and the
corrected form below is the same probe with the trust anchor named, while the absent material is
supplied the same way every other secret is — from the deployment's own secret layer through
`TLS_CERTIFICATE_PATH` and `TLS_CERTIFICATE_KEY_PATH` (§9.3.1), with nothing committed here (C-F).

**Why Persistence and DataServices each declare two endpoints.** Both serve gRPC, which **requires
HTTP/2**, and both serve REST `/health` and `/v1/ping`, which are probed with **HTTP/1.1**. On a
cleartext endpoint those two cannot share a port, because HTTP/2 negotiation is an ALPN feature of the
TLS handshake. Measured, not assumed, on SDK 10.0.302:

- A **cleartext** endpoint with `Protocols: Http1AndHttp2` disables HTTP/2 outright and says so at
  startup — *"HTTP/2 is not enabled … TLS is not enabled. HTTP/2 requires TLS application protocol
  negotiation. Connections to this endpoint will use HTTP/1.1."* An HTTP/2 prior-knowledge request to
  it fails with *"Remote peer returned unexpected data while we expected SETTINGS frame"*; every gRPC
  call would be lost.
- A **cleartext** endpoint with `Protocols: Http2` serves gRPC but answers a plain HTTP/1.1
  `GET /health` with **`400`**, so the readiness gate could never satisfy and Gateway would never
  report healthy.
- A **TLS** endpoint with `Protocols: Http1AndHttp2` serves **both** on one port — an HTTP/1.1 request
  and an HTTP/2 request against the same listener each returned `200` — because ALPN selects the
  version per connection.

The third row is what a TLS estate makes available, and the split is **retained anyway** — which is a
choice, so here is the reason. Each endpoint pins ONE protocol version explicitly, so a probe and a
gRPC channel each address a listener that can only answer the thing it is for: a gRPC channel aimed at
the HTTP/1.1 port fails with `HTTP_1_1_REQUIRED` before the request arrives, and an HTTP/1.1 probe aimed
at the HTTP/2 port gets a `400`. Under `Http1AndHttp2` both mistakes would instead *succeed at the
transport layer* and fail later, somewhere less obviously attributable. The port map, the `_GRPC_URL` /
`_BASE_URL` variable naming and Gateway's separate `Upstreams` and `HealthProbes` groups are all built
on that distinction and are asserted by tests, so collapsing it would be a change with no benefit and a
measurable cost. Security and Gateway publish no gRPC contract and so declare a single `Http1` endpoint
each, for the same reason: a listener that accepts only what it is for cannot be misaddressed silently.

**What it costs, and where the cost is paid.** Persistence and DataServices each have two addresses a
caller must keep straight, so every address variable is named for what it carries: `_GRPC_URL` for the
HTTP/2 endpoint a *service* dials, `_BASE_URL` for the HTTP/1.1 endpoint a *probe* and the end-to-end
suite read. Mixing them fails loudly rather than subtly — a gRPC channel pointed at the HTTP/1.1
endpoint fails every call with `HTTP_1_1_REQUIRED` before the request arrives, and an HTTP/1.1 probe
pointed at the HTTP/2 endpoint gets a `400`.

**Issuance is still authenticated, by a credential rather than by the transport.** `POST /v1/tokens` is
the one operation a bearer token cannot protect, because a caller cannot present a token in order to
obtain its first token. `security.v1.yaml` therefore publishes **two** schemes for it and accepts
either: a `clientCredential` — an HTTP `Basic` credential naming a subject on Security's issuance
roster — or `mutualTls`, a client certificate the listener's configured authority trusts. A request
presenting **neither** is refused with `401`. TLS is the channel and never the caller identity: a
transport that merely encrypts says nothing about who is calling, which is precisely why a credential is
required on top of it. So the newly created boundary is authenticated in both senses, which is what C-G
requires; §9.4 records this in full.

**What plaintext would have cost, which is why none is shipped.** The key set at
`/.well-known/jwks.json` is the system's trust bootstrap, and fetched in the clear it is substitutable
on path: an attacker who replaces it makes all three verifiers accept tokens the attacker signed, each
behaving exactly as designed while doing it. The issuance credential and every bearer token would
likewise be capturable. None of that is accepted anywhere in this estate — every listener terminates TLS
and every authority, upstream and probe address is `https` — and the exposure is recorded here so that a
later deployment cannot reintroduce it as a convenience without knowing what it is trading away.

**What a deployment must supply, since no code change is involved.**
`Kestrel__Certificates__Default__Path` and `__KeyPath` from the orchestration secret layer, and
`ClientCertificateMode: AllowCertificate` on Security's endpoint for the mutual-TLS alternative. Because
ALPN multiplexes, each gRPC-carrying service *may* collapse its two endpoints into one `Http1AndHttp2`
endpoint on its documented port; this repository keeps them separate deliberately, for the
misaddressing reason §4.1 records. Kestrel resolves an HTTPS endpoint's certificate in a
fixed order — an explicit certificate on the endpoint, then `Kestrel:Certificates:Default`, then the
local ASP.NET Core development certificate — and **fails to start** if none is found, never falling
back to plaintext, so a half-configured TLS deployment is a fail-fast startup error rather than a
silent downgrade.

**`AllowCertificate` and not `RequireCertificate`, for any deployment that adopts TLS.**
`AllowCertificate` requests the certificate during the handshake and hands it to the application, which
lets `POST /v1/tokens` validate it per operation while `/health`, the JWK set and the discovery
document stay anonymously reachable. `RequireCertificate` was measured and rejected: it aborts the
handshake for any client presenting none, so the anonymous `/health` probe fails and the readiness
chain that gates Gateway can never open.

**No certificate, key or passphrase appears in any settings file**, on either topology. All such
material is injected from the orchestration secret layer as environment configuration pointing at files
mounted read-only.

### 4.2 The endpoint contract common to all four

- **`/health` is anonymous on all four services.** **Gateway reports healthy only after Persistence,
  DataServices and Security report healthy** — expressed in the Compose manifest with `depends_on`
  using a `service_healthy` condition. This is the most important readiness property in the
  orchestration, and §10.4 records the alternative that was rejected precisely because it could not
  express it.
- **Gateway's aggregate names all three upstreams individually, and the Persistence entry is health
  observation only.** C-09's `AggregateHealthReport` bounds its `upstreams` array at exactly three items
  and closes the reporting-service name over `persistence`, `dataservices` and `security`, so an operator
  reading a failed aggregate learns *which* upstream is responsible. That coexists with §3.2's rule that
  Gateway never calls Persistence, because the three addresses live in a **separate configuration group**
  from the two Gateway calls: `Gateway:HealthProbes` carries three probe addresses and authorises exactly
  one anonymous endpoint on each, while `Gateway:Upstreams` carries the two services Gateway invokes and
  deliberately has no Persistence member. Gateway holds no Persistence client, channel or generated stub,
  so nothing beyond `GET /health` is reachable on that address.
- **`Degraded` and `Unhealthy` are both answered with `503`, and only `Healthy` with `200`.** The three
  status tokens remain distinct in the body — a service still completing startup validation is not the
  same as one whose dependency has failed, and an operator needs to tell them apart — but `Degraded`
  means *not ready*, and the readiness gate observes the status **code**. Answering 200 for a not-ready
  service would let `depends_on: condition: service_healthy` open on a service that had just said it was
  not ready, which is the exact ordering property the gate exists to enforce.
- **`/v1/ping` requires a token on all four services** and returns **`401`** without one. It is the
  standing proof that the authenticated-boundary requirement holds on every service, not just at the
  ingress.
- **Every probe is answered on the port it always was, and the only thing that changes is the gate's
  scheme and its trust configuration.** All four listeners are TLS, so the attached environment's
  `curl -sf http://localhost:<port>/health` cannot answer on any of them. **The corrected gate needs the
  local certificate authority, not just an `https` scheme**, because the certificate each listener
  presents is issued by the local CA of §9.3.1 and `curl` verifies by default:

  ```bash
  # All four listeners terminate TLS with the same multi-SAN certificate, so all four gates take the
  # same --cacert and a SAN-covered hostname.
  CA="$HOME/.config/powerframework/secrets/mtls-ca.crt"
  curl -sf --cacert "$CA" https://localhost:5101/health   # Persistence
  curl -sf --cacert "$CA" https://localhost:5102/health   # DataServices
  curl -sf --cacert "$CA" https://localhost:5104/health   # Security
  curl -sf --cacert "$CA" https://localhost:5105/health   # Gateway ingress
  ```

  `localhost` is usable **only because §9.3.1 issues the certificate with `DNS:localhost` and
  `IP:127.0.0.1` among its subject alternative names**; a certificate carrying a common name alone
  cannot authenticate it, whatever the scheme. The alternative to `--cacert` is installing that CA into
  the host trust store (`/usr/local/share/ca-certificates` plus `update-ca-certificates` on Debian and
  Ubuntu), after which the bare `curl -sf https://…` form works. **`-k` / `--insecure` is not an
  alternative and must not appear in a readiness gate**: it turns a probe that proves the service
  presents the identity it should into one that proves only that something answered, which is exactly the
  property a trust bootstrap cannot afford to lose. `ClientCertificateMode` is `AllowCertificate`
  precisely so that this probe stays anonymous on the same listener that carries the mutual-TLS
  issuance edge — a certificate is requested but not demanded, so a probe presenting none still
  completes the handshake and receives its `200`. An earlier revision answered the same constraint
  with a second, development-only cleartext listener on an undeclared port; it was **withdrawn**,
  because it contradicted the one-port-per-service map of §4.1, it put an unauthenticated listener in
  the one service that holds the signing key, and it made the local gate address a port that no
  deployment ever serves. Security now declares exactly ONE Kestrel endpoint,
  `https://+:5104`, `Http1` — `Http1` rather than `Http1AndHttp2` because this service publishes no gRPC
  contract — and `security.v1.yaml` publishes exactly one `servers` entry to match. §9.4 records why TLS on 5104 is functional rather than a preference.
- **Conflict mapping.** On an optimistic-concurrency mismatch, Persistence and DataServices return
  gRPC `StatusCode.Aborted` — the canonical gRPC-to-HTTP mapping — and Gateway's REST projection
  surfaces it as **HTTP `409`** carrying a structured conflict detail with the current row state.
  Callers implement an explicit retry-or-surface policy. **There is no silent overwrite anywhere in
  the system.**

### 4.3 Why 5103 is reserved rather than reassigned

The attached environment assigned 5103 to a design service. DesignSystem is precisely one of the four
deferred services, so the slot is left in the Compose manifest as a **commented-out placeholder**
rather than reassigned to one of the four services that do exist.

That is a deliberate choice with a reason: leaving it commented is literally the obvious Phase-2 slot
the manifest should provide, and it keeps the shape of the eventual system legible. Reassigning the
port would erase that signal, and a future reader would have no way to tell that a service had been
planned there.

### 4.4 The four reserved deferred routes are metadata, not stubs

Gateway's routing metadata declares four reserved routes. Each returns **`501 Not Implemented`** with
a machine-readable body naming the deferred service it will eventually reach and the marker
*reserved for Phase 2*:

| Route | Deferred service |
| --- | --- |
| `/v1/design/**` | DesignSystem |
| `/v1/documents/**` | Documents |
| `/v1/integration/**` | Integration |
| `/v1/scripting/**` | ScriptBridge |

**Stated explicitly so it is auditable: a routing declaration is not a stub of the deferred service.**
For DesignSystem, Documents, Integration and ScriptBridge there is:

- no service directory and no project file,
- no container definition,
- no test project,
- no partial implementation, and
- no exception-throwing placeholder class.

The route exists so that the shape of the eventual system is legible from Gateway's contract, which
is what the brief asks for. The prohibition in C-D is on *implementing* the deferred services — not
even partially, not even to stub them out — and a declaration in a route table implements nothing.
The capabilities each route will eventually reach are enumerated in [`DEFERRED.md`](DEFERRED.md).

---

## 5. Transport chosen per service

Transport is decided **per service, from the shape of that service's current interface** — not by
blanket policy. Each of the four decisions is recorded below with the interface evidence that drove
it, and each rejected alternative is recorded with its reason (C-K).

No transport decision here rests on a speed argument, and none is available to rest on: the repository
publishes no service-level objective of any kind (§12 and C-B).

### 5.1 Gateway → REST (Minimal APIs + OpenAPI)

**Interface shape.** Gateway's legacy analogue is the coarse, human-facing application lifecycle —
`open`, `close` and `systemerror` [`ws_objects/pfw.pbl.src/pfw.sra:L88-L108`, `:L111-L144`] — plus a
navigation surface. That is a request/response shape, not a streamed or ordered one.

**Additionally required because Gateway is the sole ingress.** REST gives browser and third-party
reach, alignment with HTTP-standard caching and proxying, and mature OpenAPI tooling for a surface that
external clients must be able to discover. Note carefully what "REST" names here and what it does not:
it names **HTTP semantics** — request/response, resource paths, status codes, an OpenAPI description a
stock client consumes — and says nothing about the scheme. **Every service in this system speaks those
semantics over HTTPS in a deployed topology**; the plain-HTTP loopback addresses in §4.1 and in the
documented bring-up are a local-development convenience, and §9.4 states the transport-security model
in full.

**Rejected alternative — gRPC at the edge.** gRPC-Web requires a translating proxy in front of it and
supports server streaming only. Choosing it for the ingress would forfeit exactly the properties an
ingress needs, and would add an intermediary component to the one boundary that external clients must
reach directly.

### 5.2 DataServices → gRPC primary, with a thin REST projection consumed only by Gateway

**Interface shape.** `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru` declares exactly 22
events at `:L11-L32` — 9 semantic and 13 raw `pbm_dwn*` — forming an ordered chain with veto
semantics. Three features of that declaration decide the transport between them:

- an **ordered event chain** whose sequence is itself contract, including nested and deferred
  dispatch;
- a **`ref string` out-parameter** — `onddsgetfilter` produces its result by mutating a reference
  rather than returning a value [`se_cst_dw.sru:L13`];
- an **`any` return over a `string[]` argument** — `oncolumnexpinvokemethod` [`se_cst_dw.sru:L14`].

That shape demands compile-time contract enforcement, bidirectional streaming to carry the ordered
chain, and a status model rich enough for a four-value item-change alphabet and a tri-valued veto.
Protobuf over gRPC carries all three. JSON over REST would lose both the ordering and the typed veto,
and would leave the `any` return untyped at both ends.

**Why a REST projection exists as well.** Gateway is REST at the edge (§5.1), so a thin projection of
the DataWindow surface is exposed for Gateway alone to consume. It is a projection, not a second
implementation, and it is the layer where gRPC `Aborted` becomes HTTP `409` (§4.2).

### 5.3 Persistence → gRPC

**Interface shape.** The legacy surface is `Query`, `Update`, `Exec`, `Commit`, `Rollback` and
`SQLErrText`. That vocabulary is **action-oriented, and therefore RPC-shaped rather than
resource-oriented** — there is no resource whose representation these methods manipulate; they are
operations. Four further properties confirm the choice:

- the 13 SQL task classes carry **mandatory thread affinity** encoded in their own source comments,
  which the contract must express as an explicit marshalling boundary (§2.3);
- `dberrordata.srs` requires a **structured error payload**, not a string;
- the mandated conflict response requires **rich status detail** — the current row state travels with
  the status (§4.2);
- **server streaming** reproduces the progressive result delivery of the legacy recordset object.

### 5.4 Security → REST (minimal, OpenAPI-described)

**Interface shape.** The cryptographic surface at
`ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73` is stateless request/response throughout, with no
ordering requirement and nothing to stream.

**Additionally required by how tokens are consumed.** Token issuance and key publication must speak
**ordinary HTTP** — not a bespoke protocol — so that consumers' stock JSON Web Token bearer handlers
fetch `/.well-known/jwks.json` and OpenID Connect discovery metadata with **zero bespoke code**. HTTP
*semantics*, over HTTPS: the scheme is not what makes a stock handler work, and §9.4 records both the
transport-security model and the one edge on which it is load-bearing. That keeps the security-critical
retrieval path
inside framework code rather than hand-written code.

**Rejected alternative — gRPC for Security.** Choosing gRPC here would force custom key-set retrieval
into three separate services. That is a net *increase* in hand-written security code, which is the
opposite of what the requirement is trying to achieve. It is the wrong direction, not merely a less
convenient one.

### 5.5 Summary

| Service | Transport | Decided by | Rejected alternative |
| --- | --- | --- | --- |
| Gateway | REST + OpenAPI | Coarse lifecycle shape; sole-ingress reach and tooling | gRPC-Web at the edge — needs a translating proxy, server streaming only |
| DataServices | gRPC + thin REST projection | 22-event ordered chain, `ref string` out-parameter, `any` return | JSON over REST — loses ordering and the typed veto |
| Persistence | gRPC | Action-oriented verbs, thread affinity, structured errors, rich status, server streaming | — |
| Security | REST + JWKS | Stateless surface; stock bearer handlers self-configure from the discovery document with no bespoke code. The choice is about interface shape, not about scheme: it holds identically on the cleartext listener the environment fixes and on an `https` one — see §4.1 and §9.4 | gRPC — would force bespoke key retrieval into three services |

### 5.6 Outbound call policy: retry admission, circuit breaking and deadlines

Choosing a transport is only half of a boundary decision. The other half is what happens when the
transport fails — a question the legacy never had to answer, because an in-process call cannot fail in
transit. `Microsoft.Extensions.Http.Resilience` is in the stack for exactly that reason, and it is the
only resilience dependency permitted.

**Why a generic HTTP pipeline is not sufficient on a gRPC channel.** Two facts, both of which cut
against the stock behaviour:

1. A gRPC call the **server refused** arrives as **HTTP `200`**, with the refusal in `grpc-status` — in
   the response headers for a trailers-only answer, in the trailers otherwise. An HTTP-level predicate
   therefore reads a server-declared `Unavailable` as a **success** and never retries it.
2. A **transport** fault arrives as an exception, which every stock transient predicate treats as
   retryable. Every gRPC call is an HTTP `POST`, so a method-based gate is all-or-nothing: a stock
   predicate happily **replays** an update, a session open, an `Exec` or a transaction commit.

The second is the serious one. Replaying an update after an unknown outcome is precisely the silent
double-apply that the conflict contract of §4.2 exists to prevent.

**The classification rule.** A transport-level replay is *invisible* to the caller — it happens below
the client member, so the caller cannot observe it, compensate for it, or decline it. Two families of
operation are therefore admitted and nothing else:

- a pure **read**, whose repetition changes nothing and is unobservable; and
- an idempotent **teardown** — a task release, a session close — whose repetition is not merely
  harmless but useful, because a leaked server-held handle is exactly what a lost teardown response
  produces, and Persistence bounds those handles with a quota that a leak consumes.

Everything else is attempted exactly once. Three exclusions are worth naming because they are not
obvious: `SetWhereClause`/`SetOrderByClause` carry a **modification style**, so the append form applied
twice appends twice; `EndSession` is excluded even though it looks like a teardown, because the legacy
transaction pool is **reference counted** and a repeated end decrements twice; and the expression
service's `Set*`/`Calc*` families are excluded because a recalculation can invoke macros back across the
inverted channel, so a replay is observable in the caller's own macro handler as an invocation it never
asked for. An operation nobody classified inherits "attempted exactly once", so adding a method to a
contract is safe by default.

**gRPC statuses.** Retry admits `Unavailable` and `ResourceExhausted` and nothing else. `Aborted` is
excluded by name rather than by omission — it is the definitive concurrency answer of §4.2, and
retrying it is the silent overwrite this system forbids. `Cancelled`, the authorization statuses and
the validation statuses are excluded because they are decisions rather than faults: a replay presents
the same request to the same policy and is refused identically. The circuit breaker is **narrower
still** and counts only `Unavailable`: `ResourceExhausted` is a healthy, deliberate refusal aimed at one
caller's quota, so breaking the channel on it would let one caller's over-use deny the read path to
everybody.

**The classification is code, not configuration**, and is built from the contract descriptors — so a
renamed or removed method is a startup failure rather than an operation that silently loses its retry.
Which operations may be replayed is a property of the *contracts*; only how long to spend is a property
of a deployment.

**Deadlines are correctness bounds, and no duration is invented.** No performance objective is asserted
anywhere in this system, and none is asserted here. Without a deadline an upstream keeps working — and
keeps the session or handle behind that work alive — for as long as the transport *appears* open,
including after the caller has gone away in a way the transport has not yet noticed. A half-open
connection is exactly that case. The deadline is the mechanism by which the upstream learns the bound
the caller is honouring and releases what it is holding, so it protects the upstream's admission limits
rather than the caller's latency.

A gRPC deadline is enforced by the client as a **total** bound across every HTTP attempt, because the
resilience pipeline sits beneath the gRPC call and its retries are invisible to it. Each unary deadline
is therefore the pipeline's own total request timeout, taken from the same setting, so the two cannot
disagree:

| Edge | Unary bound | Stream bound | Where the stream bound comes from |
| --- | --- | --- | --- |
| Gateway → DataServices | `Gateway:Outbound:RequestTimeout` | `Gateway:Outbound:StreamDeadline` | DataServices' own session idle lifetime (`DataServices:Sessions:*:IdleTimeout`) — a stream open past it is holding a session the upstream would already have released |
| DataServices → Persistence | `DataServices:Resilience:Persistence:RequestTimeout` | `DataServices:Resilience:Persistence:StreamDeadline` | Persistence's own handle idle expiry (`Persistence:Handles:IdleExpirySeconds`) — same reasoning, one layer down |
| Gateway / DataServices → Security | the corresponding `RequestTimeout` | not applicable | the Security edge is REST and carries no streaming call |

Both stream bounds default to the upstream value they are derived from, and each is validated as being
no shorter than its unary sibling — a stream is legitimately longer-lived than a single request, so the
inversion would abandon retrievals sooner than the ordinary calls beside them.

**One inbound bound belongs with these**, because it is the same argument seen from the other side.
DataServices' macro invocations travel on an *inverted* stream: the service calls back into its client
and waits. The ordinary bound on that wait is the calculating call's own cancellation, which is real
now that every shipped client attaches a deadline — but this service cannot *require* a caller to
attach one, and against a caller that does not, an unserviced macro channel would hold the session, its
engines and the calculating call open indefinitely. `DataServices:ColumnExpression:MacroInvocationTimeout`
is the backstop, defaulting to the expression session's own idle lifetime for the same
"the-service-had-already-given-up" reason. It is a **backstop rather than a budget**: an elapsed
invocation produces a defined timeout outcome and never a fabricated macro result, which is the
substitution the inverted channel exists to avoid.

**One outbound client deliberately carries no resilience at all**: Gateway's readiness-probe channel.
A probe exists to report what an upstream is doing *now*, so retrying inside it would convert an
upstream that is genuinely not ready into one that merely looks slow — and Gateway's own readiness gate,
which §4.2 requires to open only after its three upstreams open theirs, would then open on a stale
answer. It shares the trust anchor with every other channel and nothing else.

**What is deliberately left at the package's defaults**: the circuit-breaker thresholds. Choosing
values for those would be asserting an availability posture this repository publishes nothing to derive
from, whereas every value above has a stated derivation. The backoff *shape* — exponential, with jitter
— is written down explicitly even though it matches the package default, because a requirement that
holds only because a dependency's default happens to satisfy it is not being enforced by anything.

**The retry attempt count and the backoff base are NOT left at the defaults, and the reason is
arithmetic rather than posture.** Retry admission on these edges exists at **two** layers — the HTTP
pipeline and, because a gRPC channel connects inside the balancer where an HTTP handler cannot see it,
the channel's own service configuration. Two layers each retrying independently *multiply*: four
attempts at each becomes sixteen. So each service reads one attempt count and one backoff base from its
own options — `MaxRetryAttempts` (3) and `RetryBaseDelay` (2 s), both validated at startup — and hands
the same pair to both layers, with the HTTP layer standing its own retry down on the channels where the
gRPC layer owns admission. One value, one place, two consumers: the alternative is not a default, it is
a number nobody chose.

**And one bound sits inside the total.** A per-attempt timeout is derived rather than declared, as
`min(RequestTimeout, SamplingDuration / 2)`, because the resilience package refuses a pipeline whose
sampling duration is less than twice its attempt timeout — so the two cannot be chosen independently.
With the shipped settings that resolves to 15 seconds inside the 30-second total. Its purpose is the
same as every other bound here: without it a single stalled attempt consumes the whole total and no
retry ever happens, which is a resilience policy that exists only on paper.

---

## 6. Capability gating: the legacy's own decomposition intent

This section carries a genuinely interesting finding, so it is given room.

The framework's own module-gating bitmask **is the legacy's own decomposition intent**. It was written
years before this refactor, for its own reasons, and mapping it onto the Phase-1 service roster
independently corroborates that the four-service slice is drawn along the same seams the framework's
authors already recognised.

### 6.1 The eight capability bits

`ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49` declares eight capability bits, each as a
`Constant Long`, under the comment `//Initialize flags (pfwInitialize:[flags])` at `:L40`. Verified
verbatim:

| Constant | Value | Locator | Maps to |
| --- | --- | --- | --- |
| `INIT_FLAG_ENABLE_UI` | 1 | `enums.sru:L41` | DesignSystem (deferred) |
| `INIT_FLAG_ENABLE_SCITER` | 2 | `enums.sru:L42` | ScriptBridge (deferred) |
| `INIT_FLAG_ENABLE_BLINK` | 4 | `enums.sru:L43` | ScriptBridge (deferred) |
| `INIT_FLAG_ENABLE_BLINKFAST` | 8 | `enums.sru:L44` | ScriptBridge (deferred) |
| `INIT_FLAG_ENABLE_ORCA` | 256 | `enums.sru:L45` | packaging tooling |
| `INIT_FLAG_ENABLE_SQLITE` | 512 | `enums.sru:L46` | **Persistence — the only bit with an in-scope consumer** |
| `INIT_FLAG_ENABLE_DPIAWARE` | 1024 | `enums.sru:L47` | DesignSystem (deferred) |
| `INIT_FLAG_ENABLE_WEBVIEW` | 2048 | `enums.sru:L48` | ScriptBridge (deferred) |

These identifier spellings are preserved exactly, in SCREAMING_SNAKE, contrary to C# naming
convention — because these strings appear in serialized payloads, in log records and in
characterization recordings, where a rename would silently invalidate every stored comparison. An
analyzer suppression scoped to the files that carry them accompanies the decision; see
[`BUILD.md`](BUILD.md).

### 6.2 `INIT_FLAG_ENABLE_ALL` omits `BLINKFAST`, deliberately

`INIT_FLAG_ENABLE_ALL` is declared at `enums.sru:L49` as the sum of

```powerscript
INIT_FLAG_ENABLE_UI + INIT_FLAG_ENABLE_SCITER + INIT_FLAG_ENABLE_BLINK + INIT_FLAG_ENABLE_ORCA
  + INIT_FLAG_ENABLE_SQLITE + INIT_FLAG_ENABLE_DPIAWARE + INIT_FLAG_ENABLE_WEBVIEW
```

— seven of the eight bits. **`INIT_FLAG_ENABLE_BLINKFAST` is omitted**, so the constant evaluates to
`3847` rather than `3855`.

This is stated explicitly **because it looks like an oversight and is not**: `blink.dll` and
`blinkfast.dll` are alternative builds of one engine rather than two independent capabilities, so
enabling both in an "everything on" constant would be incoherent. The omission is verified in the
source and is reproduced exactly in the ported constant. A future maintainer who "fixes" it by adding
the eighth bit would be changing behaviour, not correcting a typo.

### 6.3 The operational consequence

The read-only legacy specification is explicit about what a capability bit actually controls
[`docs/README.md` §高级初始化, `:L26`]: a module that is **not** explicitly initialized has its
related functionality **unusable**, and its DLL **need not be shipped** at all. The same section shows
the pattern

```powerscript
pfwInitialize(Enums.INIT_FLAG_ENABLE_SCITER)
```

at `docs/README.md:L28`, and warns at `:L30` that the corresponding DLL must accompany the application
at runtime **or initialization fails**. That is a hard failure, not a degradation — and it is the same
posture §7.4 requires the .NET composition root to preserve.

That evidence is why the equivalent gate is exposed as **configuration on Gateway's composition
root** rather than hard-wired: the legacy already treated its capability set as a deployment-time
decision that determines which artifacts even need to be present.

### 6.4 What the mapping corroborates

Read down the right-hand column of the table in §6.1 and the Phase-1 slice falls out of it:

- **`INIT_FLAG_ENABLE_SQLITE` is the only bit with an in-scope consumer.** Persistence consumes it;
  nothing else in Phase 1 does.
- `INIT_FLAG_ENABLE_UI` and `INIT_FLAG_ENABLE_DPIAWARE` are DesignSystem — deferred.
- `INIT_FLAG_ENABLE_SCITER`, `INIT_FLAG_ENABLE_BLINK`, `INIT_FLAG_ENABLE_BLINKFAST` and
  `INIT_FLAG_ENABLE_WEBVIEW` are ScriptBridge — deferred.
- `INIT_FLAG_ENABLE_ORCA` is packaging tooling, which is not a service at all.

The framework's own gating vocabulary therefore already separates storage from presentation from
scripting from tooling. The Phase-1 boundary agrees with a distinction the legacy drew itself, which
is a stronger argument for the boundary than any amount of design preference.

---

## 7. The composition root

Gateway is the composition root, and its contract is inherited directly from the legacy application
lifecycle.

### 7.1 The mandatory initialize/finalize pairing

The read-only legacy specification [`docs/README.md` §初始化] states that from version 2.0 onward the
framework **must** be initialized before use; that `pfwInitialize([flags])` belongs at the very
**start** of the Application `open` event and `pfwFinalize` at the very **end** of `close`
[`docs/README.md:L11-L12`]; and it carries an explicit warning at `:L15` that the two **must be
paired**.

In the .NET target this becomes ASP.NET Core **host startup and shutdown**, with the pairing enforced
by the host lifetime rather than by convention, and with **fail-fast validation** at startup (§7.4).

The legacy honours its own rule in the primary application — `pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`
is the first statement of the `open` event [`ws_objects/pfw.pbl.src/pfw.sra:L91`] and the `close` event
is exactly `pfwFinalize()` [`:L108`]. It does **not** honour it in the secondary application:
`ws_objects/pfwx.pbl.src/pfwx.sra`'s `open` event contains no initialize call at all [`:L48-L50`] while
its `close` event still calls `pfwxFinalize()` [`:L52`]. That asymmetry is recorded as an observed
anomaly (§13); the .NET host cannot reproduce an unpaired lifecycle because the pairing is structural
there, and the secondary application is reference-only in any case.

### 7.2 Verified native signatures

Both lifecycle functions are PowerBuilder Native Interface entry points over the closed
`pfw.dll`, and both return `long` — so the initialization result flows through the framework's
return-code algebra rather than through an exception:

| Locator | Declaration |
| --- | --- |
| `ws_objects/pfw.base.pbl.src/pfwinitialize.srf:L3` | `global type pfwinitialize from function_object native "pfw.dll"` |
| `…/pfwinitialize.srf:L7` | `global function long pfwInitialize ()` |
| `…/pfwinitialize.srf:L8` | `global function long pfwInitialize (readonly unsignedlong flags)` |
| `ws_objects/pfw.base.pbl.src/pfwfinalize.srf:L3` | `global type pfwfinalize from function_object native "pfw.dll"` |
| `…/pfwfinalize.srf:L7` | `global function long pfwFinalize ()` |

Initialize has two overloads — a no-argument form and a flags form taking `readonly unsignedlong` —
which is exactly the shape the capability gate of §6 needs. Finalize has one.

### 7.3 Two discrepancies in the legacy documentation

The second initialization mechanism the legacy offers is to inherit an initializer object and let
automatic instantiation drive the lifecycle [`docs/README.md:L17-L21`]. Two statements in that
paragraph do not survive verification. Both are recorded here and **neither is corrected in the legacy
document**, which is read-only (C-C).

- **The library attribution is wrong.** `docs/README.md:L19` attributes `n_initializer` to
  `pfw.common.pbl`. The only `n_initializer.sru` anywhere in the tree is at
  **`ws_objects/pfw.base.pbl.src/n_initializer.sru`**, and `ws_objects/pfw.common.pbl.src/` contains
  no initialization object at all — its 25 objects are bit and word operations, assertion, stack
  trace, `sprintf`, ancestry predicates and string helpers. **Cite the real location.** The
  attribution `pfw.common.pbl::n_initializer` is not repeated as fact anywhere in this
  documentation set.
- **The equivalence claim is not exact.** `docs/README.md:L20` states that the initializer's property
  switches are equivalent to the `Enums.INIT_FLAG_XXX` values. The object declares **seven**
  switches — `#UI` (defaulting to `true`), `#Sciter`, `#Blink`, `#BlinkFast`, `#ORCA`, `#SQLite` and
  `#DPIAware`, the last six defaulting to `false` [`ws_objects/pfw.base.pbl.src/n_initializer.sru:L14-L20`]
  — against the **eight** bits of §6.1. There is no `#WebView` switch, so the two mechanisms are not
  interchangeable for a WebView-enabled application. The .NET capability gate is modelled on the
  eight-bit enum, which is the authoritative set.

The same object is also one of the flat-namespace collisions §2.1 refers to: it declares
`global n_initializer n_initializer` at `:L10`, an auto-instance shadowing its own type name. In the
target the type keeps a descriptive name and the instance becomes an injected dependency rather than a
global.

### 7.4 Fail-fast, preserved as fail-fast

`ws_objects/pfw.pbl.src/pfw.sra:L111-L144` implements a `systemerror` event that, when the error
originates from an assertion [`:L114`], splits the error text on a CRLF separator into an array
[`:L115`], and — when the split yields exactly **seven** fields [`:L119`] — repopulates the error
number, text, window, object, event, line and stack trace from them [`:L117-L124`]. It then formats a
report [`:L129-L139`], displays it [`:L141`], and executes **`HALT CLOSE`** [`:L143`].

The .NET equivalent is **fail-fast startup validation and process termination on structural faults**.
Worker-session creation failure is likewise fatal.

**Softening this into warn-and-continue would be a behavioural change dressed as robustness**, and it
is worth naming that temptation directly, because graceful degradation is normally the better instinct
in a service and here it is not. The legacy's contract is that a structural fault stops the process;
preserving that is preserving behaviour, and replacing it would silently convert a loud failure into a
quiet one. The seven-field payload protocol itself is carried across as a structured type rather than a
CRLF-delimited string.

### 7.5 One preserved defect: the hardcoded locale

`ws_objects/pfw.pbl.src/pfw.sra:L94` sets `lang = "en"` as a literal, inside an `open` event that
initializes with the all-capabilities flag [`:L91`], sets the locale literally [`:L94`], selects one of
three provider classes from it [`:L95-L102`], installs the provider [`:L103`], and opens the demo
selector [`:L105`].

**The defect is the hardcoding, not the value.** It is therefore preserved as the **default** value and
made **overridable through configuration** — which reproduces the observable default without
re-creating the un-configurability. That distinction matters and is applied consistently: the
observable default is a *behaviour* and is preserved; being impossible to configure is a *structural
property* of a compiled literal, not a behaviour, and is not something a reader could observe from
outside.

### 7.6 Theming is disabled by the legacy itself

For completeness, and because it bears on §12: `ws_objects/pfw.pbl.src/pfw.sra:L25` sets
`themename = "Do Not Use Themes"`. The legacy application disables its own theming outright. This is
one of the three independent reasons no design system is introduced in this phase.

The same object records the legacy runtime it was built against — `appruntimeversion = "21.0.0.1311"`
[`:L34`] — which constrains nothing on the .NET side but is the version any characterization capture
must be taken against.

---

## 8. Storage

This section carries constraint C-E — **no fabricated database** — so it is stated with more precision
than brevity would prefer. The single most important sentence in it is §8.2's first line.

### 8.1 Two mutually independent storage paths

The repository contains two storage paths that have nothing to do with each other. Conflating them
would fabricate a database that does not exist.

**Path 1 — the SQLite binding. The only path with a real, evidenced connection.**

`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` opens a SQLite file through a URI:

- a file delete precedes the open [`:L450`];
- the surrounding comments [`:L452-L455`] cite the SQLite URI specification and document two
  framework-specific URI extensions — a `check` option with a `quick` variant, selecting between an
  ordinary and a quick integrity check, and a `journal` option across six values (`DELETE`,
  `TRUNCATE`, `PERSIST`, `MEMORY`, `WAL`, `OFF`) **defaulting to `DELETE`**;
- the open itself is `test.db?mode=rwc` with an optional password parameter shown inline [`:L456`],
  guarded by the framework's failure predicate;
- auto-commit is toggled on and off around the statement [`:L461`, `:L473`].

The same file carries **the only DDL in the entire repository** [`:L463-L469`], creating a `COMPANY`
table with an `INTEGER PRIMARY KEY` identifier, a required text name, a required integer age, an
address column **declared** `CHAR(50)`, a real salary and a text birth field.

Two properties of that DDL are stated precisely here rather than loosely, because both are easy to
overstate and the entity model depends on getting them right:

- **The declared `CHAR(50)` length is not enforced.** SQLite assigns a declared type containing
  `CHAR` **TEXT affinity** and ignores the parenthesised length entirely, so the engine neither
  truncates at 50 nor pads to 50. The address column is therefore *declared* `CHAR(50)` with TEXT
  affinity, and a longer value round-trips intact — a fact the seeded literal `'Rich-Mond '`
  [`:L388`], with its trailing space preserved, demonstrates directly. Calling it a fixed-width
  50-character field would describe a constraint the engine does not apply.
- **`INTEGER PRIMARY KEY` is a rowid alias, and the `AUTOINCREMENT` keyword is absent.** The column
  is declared `ID INTEGER PRIMARY KEY NOT NULL` [`:L464`], which makes it an alias for the table's
  rowid, so SQLite assigns a value automatically on an insert that omits it. That automatic
  assignment is **not** the SQLite `AUTOINCREMENT` keyword, which the DDL never uses: `AUTOINCREMENT`
  would additionally guarantee monotonically increasing values and create a `sqlite_sequence` entry,
  and neither applies here. The legacy's own inline comment on that line marks it the auto-increment
  column, and that annotation is what the DataWindow's `key=yes identity=yes` corresponds to — but
  the mechanism is rowid assignment, not the keyword.

**Path 2 — the transaction-object path.**

`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru` declares **exactly two** engine-type
constants — `DBT_MSSQL` as `0` [`:L60`] and `DBT_ORACLE` as `1` [`:L61`] — resolved by a type accessor
that inspects the connection's DBMS string [`:L356-L360`]. **SQLite is not in this enumeration at
all.** The paging rewriter dispatches on that accessor
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L320-L399`], with the SQL Server arm
at `:L321`, the Oracle arm at `:L386`, and a **not-implemented arm for anything else** at
`:L396-L398` returning `RetCode.E_NO_IMPLEMENTATION`.

**Neither SQL Server nor Oracle has any schema, any connection string or any DDL anywhere in the
repository** — only those two constants and the two statement generators that branch on them.

### 8.2 The resolution, stated unambiguously

> **Persistence provisions SQLite and only SQLite.** No SQL Server instance and no Oracle instance is
> provisioned, configured, containerized, connected to, or required — not in development, not in CI,
> not in the Compose manifest, and not by any test.

Persistence targets the single evidenced schema of §8.1 through EF Core and the SQLite provider,
reproducing the legacy URI grammar including both documented extensions.

The SQL Server and Oracle behaviours are preserved as **paging-rewriter strategies that are pure string
transforms**: an input statement plus a page size and a page index yields an output statement, with no
connection involved at any point. They are therefore fully unit-testable **with no instance of either
engine in existence**, which is exactly how both behaviours are preserved without fabricating a schema
for either. Parity is byte-exact generated SQL, down to every sentinel identifier the legacy emits.
Those sentinels differ per arm, so the attribution matters:

| Sentinel | Emitted by | Locators in `n_cst_thread_task_sqlquery.sru` |
| --- | --- | --- |
| `pfwPagedSQL_OutterTbl` | SQL Server arm only | `:L333`, `:L350`, `:L362` |
| `pfwPagedSQL_RN` | both arms | `:L355`, `:L356`, `:L381-L383` and `:L394-L395` |
| `pfwPagedSQL_Tbl` | SQL Server arm, and the count wrapper | `:L356`, `:L382`, `:L834` |
| `pfwPagedSQL_TblInnerInner` | Oracle arm only | `:L394` |
| `pfwPagedSQL_TblInner` | Oracle arm only | `:L394` |
| `pfwPagedSQL_TblOuter` | Oracle arm only | `:L394` |

**The SQL Server arm has FOUR generated forms, not three, and each must be reproduced separately.**
It is a two-by-two: the outer test is whether unique-index columns were supplied [`:L323`] and the
inner test is the native-paging flag [`:L343`, `:L366`].

| # | Unique-index columns | Native paging | Locators | Generated form |
| --- | --- | --- | --- | --- |
| 1 | supplied | yes | `:L343-L350`, result at `:L364` | Unique columns appended to `ORDER BY`, column list swapped for the unique columns, `OFFSET … ROWS FETCH NEXT … ROWS ONLY` on the inner query, column list restored, inner query appended as `INNER JOIN (…) pfwPagedSQL_OutterTbl ON …` |
| 2 | supplied | no | `:L351-L362`, result at `:L364` | `ORDER BY` stripped, column list swapped for `TOP … <unique columns>,ROW_NUMBER() OVER (…) AS pfwPagedSQL_RN`, wrapped in `SELECT TOP … FROM (…) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN …`, column list and `ORDER BY` restored, wrapper appended as the same `INNER JOIN`. **No trailing `ORDER BY pfwPagedSQL_RN`** |
| 3 | none | yes | `:L366-L373` | `ORDER BY` kept or `(SELECT 0)` substituted, then replaced by `<order> OFFSET … ROWS FETCH NEXT … ROWS ONLY`. No join, no row-number column |
| 4 | none | no | `:L374-L383` | `ORDER BY` captured and stripped or `(SELECT 0)` substituted, column list swapped for `TOP … ,ROW_NUMBER() OVER (…) AS pfwPagedSQL_RN`, wrapped as in branch 2, and — **uniquely** — a trailing `ORDER BY pfwPagedSQL_RN` appended [`:L383`] |

Branches 1 and 2 return the re-parsed original statement [`:L364`], so the outer column list survives;
branches 3 and 4 return the constructed string. Together with the branch-4-only trailing `ORDER BY`,
those are the two properties by which a collapsed branch betrays itself.

Parity also covers the empty-order-by substitutions, which are **not interchangeable between arms** —
SQL Server substitutes `(SELECT 0)` [`:L370`, `:L379`] while Oracle substitutes `''` [`:L392`] — and
the count wrapper, whose column list is replaced by the alias `1 AS _` [`:L830`] before the statement
is wrapped as `SELECT COUNT(1) AS CNT FROM (…) pfwPagedSQL_Tbl` [`:L834`].

Oracle contributes a fifth generated form and the `case else` arm a sixth outcome, so one dispatch has
**six** observable results and a parity matrix needs a case for each.
[`CONTRACTS.md`](CONTRACTS.md) §8.4 carries the per-form detail; it is not duplicated here.

So: two engine *dialects* are reproduced as text generators; one storage engine is provisioned. Those
are different claims, and only the second one involves a running database.

### 8.3 Encrypted SQLite is out of Phase-1 scope

The plain SQLite library and the cipher-enabled one shipped in this repository are at materially
different versions, the cipher build being the older of the two. More decisively, the cipher library's
key-derivation and per-page integrity options are **not configurable through any framework API** —
nothing in the framework exposes the relevant pragmas, so there is no evidence of what settings a
legacy encrypted file was created with.

Since the target provider tracks a current SQLite, encrypted-file parity cannot reproduce the older
page format without an explicit compatible provider. The encrypted path is therefore **recorded as a
known limitation rather than silently attempted** (§13). The URI grammar's optional password parameter
is preserved in the connection-string model; what is not claimed is byte-format parity for an encrypted
file.

### 8.4 Schema-versus-DataWindow type mismatches are preserved as defects

The one updatable DataWindow in the repository, `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd`
(`release 12.5` at `:L2`), declares column types that do not agree with the DDL of §8.1. All six
columns carry `update=yes updatewhereclause=yes` and the identifier column is additionally
`key=yes identity=yes` [`:L8-L13`], with table settings
`update="COMPANY" updatewhere=1 updatekeyinplace=no` [`:L14`]:

| Column | DataWindow declares | DDL declares | Nature of the disagreement | Locators |
| --- | --- | --- | --- | --- |
| `name` | `char(100)` | `TEXT NOT NULL` — **unbounded** | The DataWindow imposes a 100-character bound on a column the DDL leaves unbounded, so the DataWindow is **stricter** than the schema | `dw_sqlite.srd:L9`, `w_test_sqlite.srw:L465` |
| `address` | `char(200)` | `CHAR(50)` — **declared, with TEXT affinity; the length is not enforced** | The DataWindow bounds the column at 200 characters where the DDL *declares* 50 but constrains nothing, so the two declarations disagree while only the DataWindow's bound has any effect — the opposite direction to `name`, and the only row where both oracles name a finite width | `dw_sqlite.srd:L11`, `w_test_sqlite.srw:L467` |
| `salary` | `decimal(2)` | `REAL` | A fixed two-place decimal declared over a floating-point column | `dw_sqlite.srd:L12`, `w_test_sqlite.srw:L468` |
| `birth` | `date` | `TEXT` | A date type declared over a text column, so the date format is a convention rather than a constraint | `dw_sqlite.srd:L13`, `w_test_sqlite.srw:L469` |

**There are four, not three.** `name` is the one most easily missed, because a bound over an unbounded
column looks benign next to the other three — but it is a disagreement in the *opposite direction* to
`address`, and the two together are why the entity cannot simply adopt either side's types wholesale.

These are **preserved, not corrected** (C-B). They are reproduced in the entity and DataWindow models
and annotated at the point of reproduction so a future reader cannot mistake them for a porting error.
The entity records all four against the same locators, and explicitly declines to impose the
DataWindow's `char(100)` and `char(200)` bounds on the entity because doing so would correct the
defect rather than preserve it
[`services/persistence-service/PowerFramework.Persistence/Data/CompanyEntity.cs`].

Two further facts about the same DDL belong with the table, because they are part of the same
comparison. The DDL marks only `ID`, `NAME` and `AGE` as `NOT NULL` [`w_test_sqlite.srw:L464-L466`],
leaving `ADDRESS`, `SALARY` and `BIRTH` nullable [`:L467-L469`]; and the identifier column is declared
`INTEGER PRIMARY KEY NOT NULL` [`:L464`], which makes it a **rowid alias** whose value SQLite assigns
automatically on an insert that omits it — the mechanism behind the legacy's own inline "auto-increment
column" annotation on that line, and what the DataWindow's `key=yes identity=yes` corresponds to.
**The SQLite `AUTOINCREMENT` keyword is not used**, so the stronger guarantees it would add — strictly
monotonic values and a `sqlite_sequence` row — are absent, as §8.1 sets out.

One consequence for the `address` row above is worth stating separately, because it is the difference
between a divergence and a bug: since the declared `CHAR(50)` constrains nothing, **the disagreement is
between two descriptions of one column and never a runtime truncation.** A value longer than 50
characters is stored and returned intact by the legacy and must be by this port too. `CompanyEntity.cs`
records the same ruling at its `Address` property, and cites the trailing space in the seeded literal
`'Rich-Mond '` [`w_test_sqlite.srw:L388`] as the characterization evidence that the column is never
padded or trimmed.

### 8.5 The SQL-injection exposure, mechanically explained

This exposure is documented rather than silently repaired, and the mechanism matters because it
explains why the .NET implementation can be safer internally while remaining observably identical.

**The root mechanism is a connection setting.**
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129` parses the connection
parameter string for a bind-disabling flag and a national-character-binding flag, by regular
expression. **A disabled-bind setting means the runtime does not use bind variables — values are
interpolated as literals into the statement text — and that is the mechanical root of the exposure.**
It is not an incidental coding slip; it is a configured mode of the data layer.

Two call paths then carry untrusted text into generated statements:

- **The where-clause setter takes a raw clause string.** `of_setwhereclause`
  [`n_cst_thread_task_sqlquery.sru:L269-L284`] validates only that the select index is positive and the
  clause is non-empty, returning `RetCode.E_INVALID_ARGUMENT` otherwise [`:L271`], then stores the
  clause verbatim [`:L281`]. It is spliced into the statement through the parser's modify path at
  `:L691`.
- **The drop-down search service interpolates user data unescaped into a filter expression.**
  `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L319` concatenates the
  search text into a `LIKE` clause, and `:L323` concatenates it into a pinyin matching call with a
  literal flag value of `7`.

**The .NET implementation uses parameterized commands internally while preserving observable
behaviour**, and documents each site as a known legacy defect rather than silently changing the
generated SQL. The implementation may be safer than the legacy exactly where the change is
unobservable; where the generated statement is observable, it matches. The complete security register —
locators, severities and required actions, with no value ever reproduced — is
[`SECRETS.md`](SECRETS.md).

---

## 9. Security and token topology

### 9.1 Why "no new attack surface" cannot mean "no new surface" literally

The requirement that decomposition introduce no new attack surface cannot be read literally, and it is
worth working through why, because the literal reading would forbid the refactor outright.

As §2.2 establishes, the legacy **opens no listening socket, registers no route, and receives no
unsolicited request**. It is a library loaded into a desktop process. There is no surface to preserve
the size of, and no authentication anywhere in it — because there is nothing to authenticate against.
Decomposition therefore creates the system's **first-ever ingress**, and any four-service topology
creates several internal edges that likewise never existed.

The requirement must therefore be read as: **every newly created surface is authenticated from the
outset** (C-G). That reading is what makes Security a Phase-1 service rather than a later addition, and
it is why a token is required on every internal edge rather than only at the boundary an external
client touches. An internal edge that trusted its caller because "it is internal" would be a new
unauthenticated surface, which is what the requirement exists to prevent.

### 9.2 Sole-issuer topology

| Role | Held by | What it means |
| --- | --- | --- |
| **Issuer** | Security only | The **only** component in the system that mints a token |
| **Verifier** | Gateway, DataServices, Persistence | Hold **verification material only**; no independent signing authority |

Concretely:

- **Security is the sole issuer.** It mints short-lived service tokens on request and publishes
  verification material at the standard key-set path together with discovery metadata (§4.1, §5.4).
- **The other three services hold verification material only** and validate inbound tokens with the
  framework's stock bearer handler. None of them can mint a token, and none of them holds a signing
  key. Where per-service key names are retained at all they are verification-side names and are **not
  independent signing authorities**.
- **Exactly one signing secret exists in the whole system**, held by Security and injected through the
  options pattern from the orchestration secret layer. No key material appears in source, in
  application settings, or in any container definition. The name of that secret and the full token
  register are in [`SECRETS.md`](SECRETS.md); no value appears in any document.
- **`/v1/ping` is the standing proof.** It requires a token on all four services and returns `401`
  without one (§4.2), so the property is testable rather than merely asserted.

This topology is also the reason Security speaks REST: the stock bearer handler consumes a published
key set with zero bespoke code, which keeps the security-critical path inside framework code (§5.4).

### 9.3 The issuance edge is the one edge a token cannot protect, and it accepts two credentials

Every other edge in the system is authenticated by a bearer token. Exactly one cannot be, and the reason
is structural rather than a hardening preference:

> **A caller cannot present a bearer token in order to obtain its first bearer token.**

So caller identity on `POST /v1/tokens` has to come from somewhere other than a token. The contract says
where in a machine-readable way rather than in prose:
`shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` declares **two** security schemes for that
operation as an **override** of the document-level bearer requirement, and **either one satisfies it**:

| Scheme | What it is | Where it applies |
| --- | --- | --- |
| `clientCredential` | HTTP `Basic`, naming a subject on Security's issuance roster | **Every topology.** It is the only scheme a cleartext listener can carry, and the listeners this repository binds are cleartext (§4.1) |
| `mutualTls` | A client certificate | Only where a deployment terminates TLS at Security. This is the per-pair fallback the plan describes for a pair where a token issuer is inappropriate, and it applies **for that pair only** |

A request presenting **neither** is refused. Both failure modes are part of the contract rather than
implementation detail:

| Status | Meaning on `POST /v1/tokens` |
| --- | --- |
| `401` | No credential was presented at all, or one this service does not hold — an unknown roster subject, a secret that does not match, or a certificate that does not chain to the configured issuer. There is no bearer-token alternative on this operation to fall back to, and the response does not distinguish which condition was hit |
| `403` | The caller **was** authenticated but is not permitted what it asked for. Four conditions share this status and answer four distinct sentences: the claimed `subject` disagrees with the identity the presented credential establishes; the requested `audience` is not one this deployment serves at all; it is served but is not among those the roster entry for that subject grants; or one of the requested scopes is not among that entry's grants. An ungranted scope is **refused, not narrowed** — a `200` carrying a token quietly missing one capability moves the failure to a downstream service that did nothing wrong. The response names neither the expected identity, the audiences served, nor any stored configuration |

**Why two schemes rather than one.** Naming only the certificate would have made the authentication of
this boundary contingent on a transport the frozen environment does not provide — which is how an
authenticated edge quietly becomes an unauthenticated one in the deployment that actually runs. Naming
only the credential would have discarded the per-pair mutual-TLS fallback the plan explicitly sanctions.
Publishing both, with the operation refusing a request that carries neither, is what makes C-G hold on
the topology the environment documents *and* on the one a hardened deployment would choose.

Adopting the certificate half adds a certificate path setting and a key path setting **for those two
participants only**, and any such path points at material mounted from the orchestration secret layer:
no certificate and no key is committed to this repository or embedded in an image.

**No signing, verification or mutual-TLS material is scaffolded for any deferred service.** The
attached environment named five per-service signing secrets against a placeholder roster; the two that
correspond to services which do not exist in this phase are deliberately **not provisioned**, because
provisioning a credential for a service that does not exist creates an unowned secret.

#### 9.3.1 The bootstrap, end to end and in one place

The rule above is a policy. This is the configuration that makes it operable, stated as six parts so that
none of them is left to be inferred. Parts 1 and 2 are **present in the tree** as framework-bound
configuration; parts 3 to 6 name where each remaining piece lands and its state.

| # | Part | Where it is expressed | State |
| --- | --- | --- | --- |
| 1 | **The TLS listener.** `Kestrel:Endpoints:Default`, `https://+:5104`, `Http1`, `ClientCertificateMode: AllowCertificate`, `SslProtocols: [Tls12, Tls13]`. It is the service's ONLY endpoint and it carries every route: `POST /v1/tokens`, the C-02 crypto operations, the anonymous key set and discovery documents, and the anonymous `GET /health` | `services/security-service/PowerFramework.Security/appsettings.json` | **Present and statically verified** — `Url`, `Protocols`, `ClientCertificateMode` and `SslProtocols` are all real `KestrelServerOptions` endpoint keys, confirmed by reflecting over the shared framework's `EndpointConfig` on SDK 10.0.302 |
| 2 | **The server certificate.** `Kestrel:Certificates:Default:Path` and `:KeyPath`, supplied per environment by `TLS_CERTIFICATE_PATH` / `TLS_CERTIFICATE_KEY_PATH` — **one pair for the whole stack, and therefore a pair that MUST carry subject alternative names for every origin it is presented under**: `persistence-service`, `dataservices-service`, `security-service` and `localhost`, plus the loopback IP entries. Current TLS stacks ignore the common name for host matching and read `subjectAltName` only, so a single-CN certificate matches none of the four and every internal channel fails name validation. Per-service certificates are the equally correct alternative — see the recipe below | `orchestration/.env.example` §5 declares the two variables and the key each maps onto; no settings file declares either, because both carry a path to key material (C-F) | **Present as the declared contract** (paths only — no material, here or anywhere). Absence is fail-fast: Kestrel refuses to start an HTTPS endpoint it cannot find a certificate for, and never downgrades to plaintext |
| 3 | **Client-certificate trust.** `ClientCertificateMode` `AllowCertificate` makes Kestrel **request** a certificate and hand it to the application without demanding one, so the token operation can require it per operation while `/health`, the key set and the discovery document stay anonymously reachable. *Which* issuers may have signed that certificate is decided by `Security:MutualTls:ClientCaPath`: the anchor is loaded at startup and installed as Kestrel's `ClientCertificateValidation` callback, which builds the caller's chain under `X509ChainTrustMode.CustomRootTrust` against that anchor alone. Unset defers to the platform's verdict; set-but-unreadable refuses to start. `AllowAnyClientCertificate` is never called. **Completing the handshake and establishing an identity are two decisions taken by two anchors, and only the first has a published variable:** `Security:ClientCertificateAuthorityPath` is the ISSUANCE anchor read by `Tokens/ClientCertificateTrust`, and with it unset a certificate that had just completed the handshake was refused `401 E_ACCESS_DENIED` while `Basic` callers kept minting — so the documented bootstrap could not work. The composition root now ADOPTS `Security:MutualTls:ClientCaPath` as the issuance anchor when the issuance key is unset, which makes `SECURITY_MTLS_CLIENT_CA_PATH` sufficient on its own; an explicitly configured issuance anchor still wins, because a deployment may complete handshakes for a broader authority than issuance honours | `services/security-service/PowerFramework.Security/Program.cs` (`CallerCertificateTrust`, and the `PostConfigure` on `AddOptions<SecurityOptions>()` that performs the adoption), configured from `SECURITY_MTLS_CLIENT_CA_PATH` | **Present, and exercised end to end.** A caller certificate issued by the documented local authority is minted a token by a Security instance configured with `SECURITY_MTLS_CLIENT_CA_PATH` alone; a certificate whose common name names a different roster subject is refused `403`, and a self-signed certificate spoofing a roster name is refused during the handshake and never reaches the operation. An OS-trust-store mount is not required, which is what makes it operable without a root-privileged step in the runtime image |
| 4 | **Subject-to-caller mapping.** The certificate establishes the identity; a `subject` in the request body that disagrees with it is refused `403`, per the table above. The certificate's common name is compared ordinally against the claimed subject, and the refusal names neither the expected identity nor any stored configuration | `Endpoints/TokenEndpoints.cs` | **Present** |
| 5 | **The caller side.** Gateway and DataServices present a client certificate when they call the issuance endpoint, from `Gateway:MutualTls:{CertificatePath, CertificateKeyPath}` and `DataServices:Security:MutualTls:{CertificatePath, CertificateKeyPath}` respectively, supplied by the four `*_MTLS_CERT_PATH` / `*_MTLS_KEY_PATH` variables. Each pair is **both-or-neither and that is enforced rather than documented**: half-configured fails startup with a names-only message, entirely unset is a legitimate state meaning that service cannot reach the issuance edge in this run | `Clients/SecurityClient.cs` in both services; the two settings groups and the four variables | **Present, and the certificate is genuinely attached in both services**: each loads the PEM pair once at startup as a singleton and presents it on the primary handler of its Security channel, so a configured-but-unreadable pair is a refusal to start rather than a first-request failure. What remains undemonstrated is an end-to-end handshake against a running Security instance, which is different from being callable without authentication — nothing anywhere in this repository offers an unauthenticated mint |
| 6 | **JWKS and discovery transport.** Both documents are anonymous and public by design, and they are the *verification* half rather than the issuance half — so they are served by the SAME single listener as the issuance edge, alongside `/health` and `/v1/ping`, and stay anonymous on it because `AllowCertificate` does not demand a certificate. Over HTTPS in every environment; behind the terminating proxy of §9.4 in a deployed topology, with the one exception §9.4 names | each service's bearer authority settings | **Present and statically verified** as configuration, with the backchannel's own trust anchor applied so the fetch can succeed against a locally issued certificate; unexercised end to end, because no paired bring-up has been run |

**What a developer generates locally.** On the documented topology there are **two kinds** of required
artifact — the signing identity, and one shared secret per roster entry — and they are produced by
different commands for a reason that is easy to get wrong in the other direction. The certificate set is
optional and is generated only by a deployment that chooses to terminate TLS.

```bash
set -euo pipefail
# Kept OUTSIDE the working tree - the root ignore rules do not exclude key material (§9.2).
install -d -m 700 "$HOME/.config/powerframework/secrets"
cd "$HOME/.config/powerframework/secrets"

# 1. THE ONE REQUIRED ARTIFACT - the RS256 SIGNING identity. Security is configured for RS256 and
#    publishes an RSA-only JWK set, so this must be an RSA private key. A random symmetric string
#    cannot sign RS256 at all, which is why `openssl rand` is the wrong tool here.
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out security-signing.key
chmod 600 security-signing.key

# 2. The local certificate authority. One per environment; it signs both the server certificate
#    below and the two caller certificates, and its PUBLIC half is what every service trusts.
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=powerframework-local-ca" \
        -keyout mtls-ca.key -out mtls-ca.crt

# 3. THE SERVER CERTIFICATE, AND IT MUST CARRY SUBJECT ALTERNATIVE NAMES.
#
#    ONE certificate serves all three TLS listeners - 5101, 5102 and 5104 - because
#    TLS_CERTIFICATE_PATH is one pair for the whole stack (part 2 above). A certificate is
#    therefore presented under FOUR different names depending on which listener a caller reached
#    and from where: `persistence-service`, `dataservices-service` and `security-service` inside a
#    Compose network, and `localhost` from the host during a loopback bring-up or an end-to-end run.
#
#    A common name alone will not do, and this is not a style point. RFC 6125 deprecated deriving a
#    host identity from the common name, and every current TLS stack - including the one .NET uses -
#    IGNORES CN for name validation and reads subjectAltName only. A certificate with `CN=security-
#    service` and no SAN extension therefore matches NOTHING: it fails name validation on all four
#    names, including the one it appears to carry. An earlier revision of this recipe issued exactly
#    that certificate, which made every internal channel unusable while looking correct.
#
#    So the SAN list below names every origin a caller may legitimately address, and the loopback
#    addresses are included as IP entries because an IP literal is matched from `IP:` and never from
#    `DNS:`. Per-service certificates are the alternative and are equally correct; if you take that
#    route, issue one certificate per service with that service's DNS name plus the loopback entries,
#    and point each container's TLS_CERTIFICATE_PATH at its own pair instead of at a shared one.
cat > server-san.cnf <<'SAN'
[req]
distinguished_name = dn
[dn]
[ext]
basicConstraints = critical, CA:FALSE
keyUsage         = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName   = DNS:persistence-service, DNS:dataservices-service, DNS:security-service, DNS:gateway-service, DNS:localhost, IP:127.0.0.1, IP:::1
SAN
openssl req -newkey rsa:2048 -nodes -subj "/CN=powerframework-internal" \
        -keyout server.key -out server.csr
openssl x509 -req -in server.csr -CA mtls-ca.crt -CAkey mtls-ca.key -days 30 \
        -extfile server-san.cnf -extensions ext -out server.crt

# 4. The CALLER certificates - one per service that calls the issuance endpoint, which is two.
#    These need NO subjectAltName: they are validated as CLIENT identities, so their common name is
#    read as the caller's name by Endpoints/TokenEndpoints.cs and no host matching is performed on
#    them. The name must equal the `subject` that service claims, or issuance answers 403.
cat > client-ext.cnf <<'CLIENT'
[ext]
basicConstraints = critical, CA:FALSE
keyUsage         = critical, digitalSignature
extendedKeyUsage = clientAuth
CLIENT
for caller in powerframework-gateway powerframework-dataservices; do
  openssl req -newkey rsa:2048 -nodes -subj "/CN=$caller" \
          -keyout "$caller.key" -out "$caller.csr"
  openssl x509 -req -in "$caller.csr" -CA mtls-ca.crt -CAkey mtls-ca.key -days 30 \
          -extfile client-ext.cnf -extensions ext -out "$caller.crt"
done
chmod 600 ./*.key

# 5. Verify before deploying, because a name mismatch is silent until the first connection and then
#    presents as an unreachable upstream rather than as a certificate problem.
for name in persistence-service dataservices-service security-service localhost; do
  openssl verify -CAfile mtls-ca.crt -verify_hostname "$name" server.crt
done
openssl verify -CAfile mtls-ca.crt -purpose sslclient powerframework-gateway.crt
openssl verify -CAfile mtls-ca.crt -purpose sslclient powerframework-dataservices.crt
```

**`mtls-ca.crt` is mounted into all four containers, and that is what makes the topology work.** It is
the public half of the authority above, so it is not secret — but it is not in any container's
operating-system trust store either, so each service is told about it explicitly through one setting:

| Consumer | Setting | Variable | What it decides |
| --- | --- | --- | --- |
| Gateway | `Gateway:InternalTls:TrustedCaPath` | `INTERNAL_TLS_TRUSTED_CA_PATH` | Which authority Gateway accepts when it calls Security and DataServices, and when it probes their readiness |
| DataServices | `DataServices:InternalTls:TrustedCaPath` | `INTERNAL_TLS_TRUSTED_CA_PATH` | Which authority DataServices accepts on its four Persistence channels and its Security channel |
| Persistence | `InternalTls:TrustedCaPath` | `INTERNAL_TLS_TRUSTED_CA_PATH` | Which authority Persistence accepts when its bearer handler fetches the published key set |
| Security | `Security:MutualTls:ClientCaPath`, and `Security:ClientCertificateAuthorityPath` by adoption when that key is unset | `SECURITY_MTLS_CLIENT_CA_PATH` | Which authority may issue a CALLER's certificate on `POST /v1/tokens` — the first key decides whether the handshake completes, the second whether the certificate establishes an identity, and one variable now feeds both |

Each of the four **narrows** trust rather than relaxing it: the configured anchor becomes the only
acceptable root for internal traffic under `X509ChainTrustMode.CustomRootTrust`, and the machine's
public roots stop being acceptable for it. Chain building, name validation and validity dates remain
the platform's throughout. There is no validation callback that accepts an invalid certificate, no
`ServerCertificateCustomValidationCallback`, no `DangerousAcceptAnyServerCertificate` and no
`AllowAnyClientCertificate` anywhere in this repository. Leaving a path unset is a legitimate posture
meaning *use the platform's trust store*, which is right for a deployment whose internal certificates
come from an authority the image already trusts; a path that is set but unreadable is a structural
fault and the service refuses to start.

**Revocation is not checked on these channels, and that follows from the topology rather than from
convenience.** A CA generated by the two `openssl` invocations above publishes no revocation list and
runs no responder, so an online check has nothing to ask and would make every internal handshake wait
for a lookup that must fail. The 30-day certificate lifetime above is the control that substitutes for
it. A deployment whose authority does publish revocation information leaves these paths unset and uses
platform trust, where the platform's own revocation behaviour applies.

Point `SECURITY_JWT_SIGNING_KEY`, `TLS_CERTIFICATE_PATH`, `TLS_CERTIFICATE_KEY_PATH` and
`SECURITY_MTLS_CLIENT_CA_PATH` at those files from the environment file — those four are the names
`orchestration/.env.example` actually declares — plus the two client pairs, `GATEWAY_MTLS_CERT_PATH` /
`GATEWAY_MTLS_KEY_PATH` and `DATASERVICES_MTLS_CERT_PATH` / `DATASERVICES_MTLS_KEY_PATH`, for a deployment
choosing the certificate alternative on the issuance edge. Every one carries a **path or a PEM value
supplied at deployment time**, and none has a value in this repository.

**One naming hazard, stated because the failure it produces looks like something else.** The two
`SECURITY_MTLS_*` certificate names above are the **server** half — they record where Security's own
certificate went, and `orchestration/.env.example` declares neither, because the server material is
supplied once for the whole stack through `TLS_CERTIFICATE_PATH` / `TLS_CERTIFICATE_KEY_PATH`. The
end-to-end suite reads *the same two names* for its **client** pair
(`tests/e2e/fixtures/service-endpoints.ts`). Server and client are opposite halves of one handshake: the
server certificate identifies Security to its callers, a client certificate identifies a caller to
Security. Handing the suite `server.crt` therefore completes the handshake, establishes the identity
`security-service`, and is refused `403` at row 4's reconciliation against the requested subject — a
refusal that is entirely correct and resembles nothing about its cause. Point the suite at
`pfw-e2e-suite.crt` / `.key` from the loop above, and see `tests/e2e/README.md` §4.6.
[`SECRETS.md`](SECRETS.md) §4.1 is the canonical statement of the same procedure and
`orchestration/.env.example` §1 and §5 declare the variables; the three must agree word for word, and a
change to one is a change to all three.

> **The trust half of part 3 IS implemented, by the second of the two options that were open — and the
> mount is the one piece still outstanding.** `SECURITY_MTLS_CLIENT_CA_PATH` above names `mtls-ca.crt`, and
> `Security:MutualTls:ClientCaPath` is **read at startup**, before `Build()`, by
> `CallerCertificateTrust.Load` in `services/security-service/PowerFramework.Security/Program.cs`; the
> loaded anchor is then installed as Kestrel's `ClientCertificateValidation` callback, which builds a
> presented caller certificate's chain under `X509ChainTrustMode.CustomRootTrust` against that anchor
> alone. A configured-but-unreadable anchor is a **refusal to start**, not a handshake failure discovered
> by the first caller, and `AllowAnyClientCertificate` is called nowhere in the repository. So chain
> verification is application code against a mounted file rather than an OS trust store — which is why
> `services/security-service/Dockerfile` deliberately performs no `ca-certificates` install and no
> `update-ca-certificates`, and why the runtime stage needs no root-privileged step.
>
> **What is still outstanding is the mount, not the validation.** `orchestration/docker-compose.yml` —
> which would mount `mtls-ca.crt` into the container — does not exist, so nothing places the file the
> variable names. With the path unset the callback defers to the platform's own verdict, which is what
> Kestrel would have done unaided; with it set and mounted, caller authentication on `POST /v1/tokens`
> is operable. Until a manifest mounts it, **the certificate alternative on the issuance edge is
> unexercised end to end**, and the `clientCredential` scheme is the alternative the documented bring-up
> supplies — so this is a gap in one of two schemes rather than an unauthenticated boundary.
>
> The option NOT taken is recorded because it was a real choice: installing the anchor in the image and
> letting the OS trust store decide would have kept the validation path framework code, at the cost of
> baking a deployment-specific anchor into an image layer and requiring a root step in the runtime stage.
>
> [`SECRETS.md`](SECRETS.md) §4.1.1 and §4.3 carry the same statement from the secrets side.

**What is honestly unavailable, and what is not.** No stack has been brought up in this clone (§10.6), so
no token exchange has been *exercised* and `/v1/ping` has not been driven to `200` against a running
service. `/v1/ping` returning `401` without a token — the property C-G actually requires — needs no
credential at all and is testable now. Nothing in this repository offers an unauthenticated mint on
either topology, so "unexercised" is not a synonym for "open".

### 9.4 Transport security: TLS is the shipped contract, and the loopback forms differ only in the host

This subsection exists because two facts elsewhere in this document read as a contradiction if the
distinction underneath them is left implicit, and they are not one.

**"REST" names an interface shape, not a scheme.** §5.1 and §5.4 choose REST for Gateway and Security
because their interfaces are request/response and because stock tooling consumes an HTTP surface with
no bespoke code. Neither statement is a claim about `http` versus `https`, and neither would change if
the scheme did.

**The loopback addresses differ from the deployed ones only in the HOST, and no settings file relaxes a
scheme.** The `localhost` forms in §4.1, in the documented bring-up and in the end-to-end suite's
defaults are what the local bring-up publishes on the loopback interface. In the .NET configuration they
are expressed as **`appsettings.Development.json` overrides that change the host and nothing else**: the
base `appsettings.json` of each service carries https authorities, https upstreams and
`RequireHttpsMetadata` set to true, and every Development overlay keeps all three. **No deployed
configuration file in this repository — base or Development, in any of the four services — sets
`RequireHttpsMetadata: false`.** There is nothing there for it to relax: every authority those files name
is `https`, so waiving the requirement would permit a plaintext metadata address that no configuration
declares — and a standing relaxation with no consumer is the kind of dead setting a later reader mistakes
for a requirement.

**Test hosts are the exception, and they are the case the setting exists for.** `Gateway.Tests` and
`DataServicesTestHostFactory` set `RequireHttpsMetadata` to `false` explicitly, because each stands up an
isolated in-process plain-HTTP authority with no certificate; the option is **mandatory** there and
`DataServicesTestHostFactory` records it as such. That is a test fixture configuring a topology it fully
controls, not deployed configuration, and the two must not be read as the same claim: what this document
asserts is the absence of a *standing* relaxation in shipped settings.

**`RequireHttpsMetadata` is therefore `true` in every settings file that declares it, base and overlay
alike, and it agrees with the authority beside it.** The setting does not make a fetch secure; it
*requires* the metadata address to be `https`, and against an `http` authority it makes the fetch
**fail** — so its correct value is a function of the authority in the same file, never a policy choice
made independently of it. Every authority in this repository is `https`, so `true` is both the safe value
and the working one. The coherence test in `shared/PowerFramework.Contracts.Tests` asserts the pair can
never disagree inside one file, in either direction: a settings file may not require secure metadata
retrieval from a plaintext authority, and may not waive it for a secure one.

**No token-validation check is configurable from a settings file at all.** Issuer, audience, lifetime
and signing-key validation are hardcoded on in every service, precisely so that no environment overlay,
no environment variable and no operator error can turn one off. That is the property `RequireHttpsMetadata`
is often mistaken for, and unlike `RequireHttpsMetadata` it is genuinely a security control — so it is
placed where configuration cannot reach it.

**Security's own authority is the one address that is https in EVERY configuration, including
Development, and no consumer relaxes `RequireHttpsMetadata` anywhere.** The convenience above applies to
loopback addresses whose scheme is a deployment detail; it does not apply here, because for this
particular address the scheme is functional. Mutual TLS on `POST /v1/tokens` cannot be presented on a
plaintext listener at all, and the JWKS and discovery documents are the material the other three
services trust — so a Development override to `http` would not be a relaxed setting, it would be a
listener that cannot issue a first token and a trust bootstrap an attacker on path can rewrite. There is
consequently **no development exception for Security at all**: it declares one TLS listener on 5104 in
every environment, and the only thing development changes is that the local readiness gate speaks `https`
to it, exactly as §4.2 records. The `RequireHttpsMetadata` setting lives on the three CONSUMING services,
which perform the metadata fetch; Security configures no authority and no metadata address — it validates
the tokens it minted itself from its own in-process key — so it fetches no metadata and carries no such
setting. It briefly did, read from configuration and governing nothing, which is a false assurance rather
than a harmless symmetry, and it was removed.

**Adopting TLS is a configuration change, and its shape is fixed here so it is not redesigned later.**
Point the address variables at `https` addresses, supply `Kestrel__Certificates__Default__Path` and
`__KeyPath` from the orchestration secret layer, add `ClientCertificateMode: AllowCertificate` to
Security's endpoint, and — because ALPN then multiplexes — optionally collapse each gRPC-carrying
service's two endpoints back into one `Http1AndHttp2` endpoint on its documented port. Flip
`RequireHttpsMetadata` to true in the same edit, since the authorities are then `https` and the coherence
rule permits it. No code changes, in any service.

**One path constrains deployment either way, and it is the issuance path.** Bearer-protected and
anonymous operations may sit behind a TLS-terminating proxy in the ordinary way. **`POST /v1/tokens` may
not, wherever the `mutualTls` scheme is the one in use.** A client certificate authenticates the client
to Security itself, so an intermediary that terminates TLS on that path either discards the certificate
or leaves Security trusting a forwarded assertion of an identity it cannot verify — and either outcome
defeats the sole-issuer topology of §9.2. A proxy fronting the issuance path passes the connection
through rather than terminating it. Where the `clientCredential` scheme is in use the constraint does not
arise, because the credential travels in a header the operation reads itself; that is a further reason the
contract publishes both.

### 9.5 Token lifetime tolerance: all four boundaries, in one place

`ValidateLifetime = true` is assigned literally on all four services (§9.2), but a lifetime check is only
as tight as the tolerance beside it. `TokenValidationParameters.ClockSkew` **defaults to five minutes** in
the bearer handler, so a boundary that says nothing about it is not enforcing "no tolerance" — it is
silently extending every token's usable life by five minutes. Security issues with a five-minute lifetime,
which means the default is as long as the lifetime it qualifies: a token accepted under it is usable for
twice as long as it claims to be.

This is stated here, once, because the value is a **per-boundary constant in four different files** and
the only way to see that they agree is to compare them. They did not always agree:

| Boundary | Tolerance | Assigned at | Why this value |
| --- | --- | --- | --- |
| Security | **0 s** | `PowerFramework.Security/Program.cs` | It validates only tokens **it minted itself**, moments earlier, on the same host from the same clock. There is no second clock for a tolerance to accommodate, so any tolerance here is pure relaxation |
| Gateway | **30 s** | `PowerFramework.Gateway/Program.cs` | Absorbs ordinary clock drift between containers on one host — the topology the frozen environment describes — and no more |
| DataServices | **30 s** | `PowerFramework.DataServices/Program.cs` | As Gateway |
| Persistence | **30 s** | `PowerFramework.Persistence/Program.cs` | As Gateway |

Three properties of that table are deliberate and are the reason it exists:

- **The three verifiers agree exactly.** Before these values were made explicit, Gateway and Persistence
  assigned nothing and therefore inherited five minutes, while DataServices used thirty seconds and
  Security used zero — four validators of **one issuer's** tokens, each with a different answer, so
  whether an expired credential was accepted depended only on which service it happened to reach.
- **The worst case was the ingress.** Gateway is the sole edge an external client can reach (§3.2), so
  the loosest tolerance in the system sat on the one boundary facing the untrusted network, and a token
  Security itself would have refused was admitted there and then forwarded inward.
- **Security's zero is not an inconsistency and is not copied.** It is the issuer; the asymmetry follows
  from what each boundary validates rather than from a difference of opinion about clocks.

**Every value is a compile-time constant, not a setting**, for the same reason the four validation flags
are literals: a configurable tolerance is lifetime validation switched off by another name, since nothing
would stop a deployment setting it beyond the token lifetime — at which point the expiry check does not
expire. Security mints with a truncated whole-second timestamp, so no sub-second allowance is required on
any boundary.

### 9.6 Key-set retrieval: the two intervals a rotation is bounded by

The tolerance above governs *when a token expires*. Two further durations govern *which keys a boundary
will accept a signature from*, and leaving them unassigned was a rotation decision taken by omission in
exactly the way §9.5 describes for the tolerance — with the difference that the library's two defaults
fail in **opposite directions at once**.

| Setting | Shipped | Library default | What it bounds |
| --- | --- | --- | --- |
| `MetadataRefreshInterval` | **5 s** | 5 min | The floor before a refresh that a **failed** validation asked for is performed. `RefreshOnIssuerKeyNotFound` is true by default, so an unknown `kid` requests one at once — this decides how long a **newly minted** token stays refused after a rotation |
| `MetadataAutomaticRefreshInterval` | **5 min** (the library's own minimum) | **12 h** | The background interval. A **successful** validation provokes no refresh, so nothing else ever drops a **retired** key from the *current* configuration |
| `LastKnownGoodLifetime` — **derived from the row above, not separately configured** | **5 min** | **1 h** | How long a **superseded** key set stays acceptable as a fallback. The token handler retries a failed validation against a cache of recently-good configurations, so this is what actually decides how long a retired key keeps working. Bounded, not switched off: `UseLastKnownGoodConfiguration` stays `true`, because that fallback is what keeps a boundary validating through a transient inability to *fetch* the key set |

Both are declared in each verifier's settings file, bound to its verification options, read by its
composition root, and **validated at startup against the library's own published floors** (1 second and 5
minutes): the configuration manager throws on a value below either, and it throws while the handler is
built — on the first authenticated request rather than at startup — so a host would otherwise report
healthy and then fail every authenticated call with a message naming neither the setting nor the file.

The third row was found by measurement rather than by reading: with the first two configured, a token
signed by a **retired** key was still accepted **nine and a half minutes** after a rotation — past a
background refresh that had already replaced the current configuration — because the fallback cache still
held it. [`SECRETS.md`](SECRETS.md) §4.2.1 carries the measured before-and-after figures for both halves of
a rotation.

Unlike the tolerance, the first two **are** settings, and the distinction is principled rather than
inconsistent: a wider tolerance weakens expiry checking, whereas a wider retrieval interval only delays
convergence — it cannot make an invalid signature acceptable. The lower bound is where the trade sits: this
floor is also the only rate limit on the fetch a **rejected** token can provoke, so a value near zero
would make a forged `kid` a request amplifier aimed at Security's published key set.

**Security configures neither**, and that is not an omission: it builds no configuration manager at all,
because it validates the tokens it minted itself and takes its verification key in process from its own
signing-key layer. It therefore converges immediately on restart while the three verifiers converge on the
intervals above. [`SECRETS.md`](SECRETS.md) §4.2.1 carries the rotation runbook that follows from this —
what each boundary does at the instant of rotation, the order to rotate in, and why an overlapping key set
is not the answer here (AAP §0.6.6.3 fixes exactly one signing secret in the estate).

---

## 10. Orchestration

### 10.1 The built path

A hand-authored `orchestration/docker-compose.yml` plus `orchestration/.env.example` is the **primary
and only** orchestration path (C-J). **`orchestration/.env.example` is present in the tree and carries
the variable roster; the manifest has not been authored yet** — so this subsection is a description of
the environment template and the specification for the manifest, and §1's state table is the authority
for which is which. The manifest declares:

- the four services of §3.1, **one container per service**;
- a `persistence-db` volume, attached to Persistence alone — it is the only service with a storage
  provider (§3.1);
- the **commented-out 5103 slot** of §4.3;
- a `depends_on` chain using **health conditions**, so that Gateway is gated behind Persistence,
  DataServices and Security (§4.2).

One manifest will bring all four services up together, which is the local-orchestration requirement; and
because the four are separate images with no in-process dependency on one another, instance counts can
vary per service independently. **Independent scalability is a structural property of one-container-per-service
and holds by construction; it is not a measured result, and no throughput figure is claimed for it.**

### 10.2 Container build context

Each service's container definition lives at `services/<service-name>/Dockerfile`. **All four exist** —
`gateway-service`, `dataservices-service`, `persistence-service` and `security-service`. The Compose
manifest, which is still to be written, sets the build **context to the repository root**: `context: ..`
with `dockerfile: services/<service-name>/Dockerfile`, and all four definitions are authored against
exactly that assumption — every `COPY` path in them is repository-root-relative.

This is not stylistic. Every service project references the shared libraries and the generated contract
stubs under `shared/`, and a Docker build cannot reach outside its own context, so **a
per-service-directory context would not work**: restore would fail on the missing shared projects. A
repository-root `.dockerignore` keeps the legacy tree, the native binaries and all build output out of
every image layer, so the root context does not drag the read-only oracle into the images.

### 10.3 Images

Each image is **multi-stage** — an SDK image for restore and build, an ASP.NET runtime image for the
final stage — and the final stage runs as a **non-root** user. The runtime image ships **without `curl`
and without `wget`** but it **does** carry `openssl` (the .NET TLS stack depends on it), which constrains
every container health probe: a probe that assumes `curl` without installing it can never succeed, the
`service_healthy` condition of §10.1 can never be satisfied, and the readiness gate silently never opens.

**The four definitions solve that in two ways — three work with what the image already has, one installs a
tool** — and the deciding fact is the same for all four: **every listener terminates TLS**, so no probe can
be a plaintext socket write.

| Definition | Probe mechanism | Installs anything? | Why that one |
| --- | --- | --- | --- |
| `gateway-service` | `openssl s_client` piped a hand-written request, matching the status line | **No** | The 5105 ingress is **TLS-terminated**, so a `/dev/tcp` socket redirection writes plaintext bytes into a TLS endpoint, never produces a status line, and fails **permanently** — leaving the `service_healthy` chain closed for ever. `openssl`, `bash` and `printf` are already in the image |
| `dataservices-service` | `openssl s_client` piped a hand-written request | **No** | The 5102 listener is TLS-terminated, for the same reason and with the same consequence |
| `security-service` | `openssl s_client`, likewise from the base image alone | **No** | The 5104 listener is TLS too. An earlier revision installed `curl` here with an unpinnable apt version; using the `openssl` already present adds no package and no layer |
| `persistence-service` | `curl --cacert` | **Yes** — `curl` only, in one layer with the apt lists removed, before the `USER` switch | The 5101 listener is TLS, and this is the one definition that accepts an install rather than working around it |

Every one of the four **verifies the presented chain against the mounted anchor**; none passes `-k`,
`--insecure` or `-noverify`, because a probe that skips verification reports healthy for a listener the rest
of the stack cannot talk to.

> ⚠ **CORRECTED — this subsection previously described the Gateway probe as a `/dev/tcp` socket write and
> called the 5105 ingress "plaintext".** Neither is true: `services/gateway-service/Dockerfile` uses
> `openssl s_client`, and its own comments explain why a raw socket cannot work against a TLS listener. The
> error mattered beyond this table, because "the ingress is plaintext" is the premise behind every
> documented `http://localhost:5105` probe — see [`BUILD.md`](BUILD.md) §1.6 **D7**, which enumerates the
> https-only transport as a deviation from the attached environment's instructions and carries the corrected
> `curl --cacert https://…` forms. [`BUILD.md`](BUILD.md) §7.1 already carried this table correctly and was
> the model for the version above.

### 10.4 Rejected alternative — .NET Aspire's Docker Compose publishing

Recorded with its precise reason, because C-K requires the rejected alternative and not only the
conclusion.

Aspire's Compose publishing does generate a manifest and an environment file from an AppHost model, so
it was a genuine candidate. It was rejected on two findings:

1. The **Dockerfile-builder APIs remain experimental** and require suppressing an experimental-API
   diagnostic to use at all.
2. **`depends_on` with a `service_healthy` condition was not expressible through the generated
   model.**

The second is decisive. That condition is what makes Gateway report healthy only once its three
upstreams do — the single most important readiness property in the orchestration (§4.2) and an explicit
requirement of the attached environment. An orchestration path that cannot express it would break the
gate it exists to enforce.

Hand-authored Compose is therefore the one built path, and Aspire is documented here as a considered
and rejected alternative rather than quietly omitted.

### 10.5 One deliberate deviation — the volume name

The attached environment names the persistence volume after a *data service*. Since that placeholder
service becomes `persistence-service` in the reviewed roster (§3.1), the volume becomes
**`persistence-db`**.

The environment's paired-capture persistence rule survives the rename intact: it is restated verbatim
against the new name in [`docs/PARITY.md`](PARITY.md), and it is restated again in
`characterization/README.md` once that directory is created (it is not yet present). The rule itself
is not restated here — it belongs with the characterization model, and duplicating it is exactly how a
rule drifts.

### 10.6 What was not verified — stated plainly

**No verified container bring-up is claimed anywhere in this document.** The reason is no longer that
nothing exists to bring up — **all four container definitions are authored, and so is
`.github/workflows/ci.yml`** — but that nothing assembles them into a stack:
`orchestration/docker-compose.yml` and `orchestration/README.md` are absent, so the health-condition
dependency chain has no expression anywhere in the tree. **No image has been built from three of the four
definitions, and no multi-service stack has been started.**

**Container correctness is asserted for exactly one image and for nothing else.** The Security image was
**built and run** here: `docker build --check` reported no warnings, the build completed, and the running
container answered 200 on the anonymous `/health` over TLS and 401 on `/v1/ping`, reached Docker health
status `healthy`, and was confirmed to carry `openssl` and neither `curl` nor `wget`. Its probe was driven
negative as well — a dead port and the 401 route both fail it — so it is a probe and not a formality.
That run is also what fixes the probe idiom for the other images: the runtime base carries `openssl` and
neither `curl` nor `wget`, so a probe may only use what the image actually ships, and the Gateway,
DataServices and Security definitions all use the `openssl s_client` pipeline while Persistence installs
`curl` explicitly in its runtime stage and may therefore use it. The Gateway, DataServices and Persistence
definitions exist but have not been built here.

**The multi-service bring-up remains unexercised, and that is the claim that matters most.** No
`orchestration/docker-compose.yml` exists, so the health-condition chain that reports Gateway healthy only
once its three upstreams are has **never** been run, and the four `depends_on` conditions have no
expression anywhere in the tree. One image passing its own probe says nothing about that chain. When the
manifest is authored, correctness for the chain will rest on manifest review plus the CI pipeline; naming
that mechanism is not the same as reporting it has run.

**What *was* exercised**, in this repository: the per-service restore, release build and
coverage-collecting test path, for every one of the four services. All ten test projects build and pass —
**18,607 tests, 4 skipped, zero failing** — and each service clears the 80% line-coverage floor on its own
Cobertura report, which `.github/workflows/ci.yml` enforces per service. What was **not** exercised is
anything requiring a running stack, and anything requiring a GitHub-hosted runner. See [`BUILD.md`](BUILD.md) §5.5 and §13 for the exact figures and for what each was
measured against.

**What that leaves unverified, precisely.** An in-process host is not a network: it does not exercise
TLS, ALPN protocol negotiation, a real gRPC channel, a client certificate handshake, a container health
probe, or Compose's `depends_on` ordering. **No request in this system has crossed a real network boundary
between two services**, and no aggregated `/health` has answered against three live upstreams. See
[`BUILD.md`](BUILD.md) §5.5 and §13 for the per-project figures and for what each was measured against.

---

## 11. Design patterns and the legacy-to-target mapping

### 11.1 Patterns applied, each tied to its evidence

No pattern below is applied for its own sake; each is required by something in the legacy source. Wire
detail lives in [`CONTRACTS.md`](CONTRACTS.md).

- **Retrieval / validation / update as three explicit contracts.** Mirrors the legacy query task, the
  five type validators and the update task. They are three separate contracts so they can version
  independently — which matters because the update contract is the one carrying optimistic-concurrency
  semantics, and it will change on a different cadence from retrieval.
- **Anti-corruption layer for the DataWindow buffer model.** Required, not stylistic: the legacy result
  carrier derives from a datastore, so it **is** a DataWindow — complete with the primary, delete and
  filter buffers and per-item modification statuses. A naive rowset would silently discard exactly the
  state the update contract depends on, and the loss would not be visible until a concurrency check
  produced the wrong answer.
- **Strategy plus provider dispatch for paging.** Replaces the legacy `choose case` on engine type
  [`n_cst_thread_task_sqlquery.sru:L320`], preserving both type constants and the not-implemented arm
  [`:L396-L398`]. Pure string transforms, fully testable with no storage instance of any kind (§8.2).
- **Repository plus unit of work over EF Core and SQLite**, for the only evidenced schema, reproducing
  the documented URI grammar including both extensions (§8.1).
- **API gateway plus composition root.** Reproduces the mandatory initialize/finalize pairing as host
  startup and shutdown with fail-fast validation (§7.1, §7.4), and the eight-bit capability gate as
  configuration (§6.3).
- **Sole-issuer token authority.** Security mints; the other three validate against the published key
  set. This is *why* Security is REST: the stock bearer handler consumes that endpoint with zero
  bespoke code, so the security-critical path is framework code rather than hand-written code (§5.4,
  §9.2).
- **Event broker with decomposed topic identity.** The legacy topic string fuses several independent
  encodings into one opaque value — a lexical ordering prefix, the logical name, a
  persistence-namespace suffix, a capture mode, an explicit priority, a prepend flag, and two negation
  flags belonging to the *filter* grammar. The contract carries each as its own field and reconstitutes
  the fused legacy form only at the compatibility edge, because transmitting the fused string makes the
  ordering invisible while parsing it at the far end gives the contract an undocumented grammar. The
  negation flags matter most: a negated-namespace filter removes the subscriptions that are **not** in
  the named namespace, so a model that recorded only "the persistent namespace" would invert the
  behaviour while looking correct — see [`CONTRACTS.md`](CONTRACTS.md) §6.7.
- **Explicit validation-session context.** Four pieces of cross-event mutable state
  [`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L89-L96`] — the disabled-event mask, two
  re-entrancy flags, and the item-change return value stashed for the validation-error event to
  consume — have nowhere to live on a stateless boundary, so they become fields of a server-held
  session correlated by identifier and opened and closed by dedicated calls.
- **Options pattern for all secret and configuration injection.** No literal appears in source, in
  application settings, or in any container definition. This discharges the secrets mandate
  *structurally* rather than by review, which is the only way it stays discharged.
- **Structured-error results replacing dialogs.** Every legacy message box becomes a typed error
  carrying the original text, the localization category, the substitution arguments, the severity, and
  for expression parse failures the caret position. **Only the delivery channel changes**; no message
  text is reworded and no severity is reclassified.

### 11.2 Architecture mapping, current to target

The right-hand column is the point of this table: in every row the translation is *not* mechanical, and
the reason it is not is what a downstream implementer needs.

| Legacy mechanism | Target mechanism | Why the translation is not mechanical |
| --- | --- | --- |
| One flat global namespace resolved by library-list order [`pfw.pbt`, `LibList`] | Explicit C# namespaces under `PowerFramework.*` | There are no import statements to rewrite. The work is namespace *creation* plus resolving the collisions a flat namespace tolerated — including auto-instances that shadow their own type names (§7.3) and two distinct files both named `pfw.sra` (§13) |
| In-process synchronous call | gRPC or REST call across a process boundary | A call that **could not fail in transit** now can. Handling that failure mode is required **by** the transition; it is not an improvement layered on top of it |
| PowerBuilder event dispatch, 22 events on one control [`se_cst_dw.sru:L11-L32`] | Bidirectional gRPC stream plus an explicit validation session | Four fields of cross-event mutable state have nowhere to live on a stateless boundary [`se_cst_dw.sru:L89-L96`] |
| `Post` to the Win32 message queue | An explicitly queued continuation on the validation session | **A headless Linux container has no message pump.** The deferred behaviour must be modelled explicitly because the mechanism it relied on does not exist |
| Proxy pair `n_cst_threading*` / `n_cst_thread*` | Async request/response across an explicit marshalling boundary, with cancellation | The thread-affinity split is a contract the legacy documents in its own comments and threading note (§2.3), not an implementation detail to flatten into one async method |
| Native class over a closed DLL | Base-class-library substitution, in-repo reimplementation, or a documented non-port | Three capabilities have no portable equivalent and are deliberately not ported; one has no substitute whose fidelity can be proven without the behavioural oracle (§13) |
| Modal dialog | Structured, machine-readable error result | Only the channel changes: text, category, severity and caret position are all preserved (§11.1) |
| Hardcoded literal | Options-pattern binding from the orchestration secret layer | The mandate is repository-wide, while the read-only rule forbids editing the offending legacy files — so the remediation posture is never-replicate rather than remove-from-source (§9.2) |
| Return codes through a tri-state algebra | The same algebra, preserved including its boundary hole | A prevention reads as a success and a cancellation is neither succeeded nor failed. Collapsing null to zero would convert "neither" into "succeeded" |

---

## 12. Deliberately absent

### 12.1 No presentation surface is created in this phase

This refactor is **API and service-level only**. Three mutually reinforcing pieces of evidence support
that, and they are recorded so that no future reader assumes an omission:

1. **The attached environment states it directly** — the environment is API and service-level only
   until a presentation surface is discovered or migrated in a later phase, and no UI exists yet.
2. **DesignSystem is deferred.** The service that would own any presentation surface is precisely one
   of the four the brief forbids implementing, even partially, even to stub out (C-D, §4.4).
3. **The legacy application disables its own theming** — `themename = "Do Not Use Themes"`
   [`ws_objects/pfw.pbl.src/pfw.sra:L25`].

### 12.2 No component library or design system is introduced

This is a determination with evidence, not an omission:

- **No component library or design system is named anywhere in the requirements** for the in-scope
  work.
- **No design attachment was provided**, so there is no design-to-implementation mapping to perform.
- **The repository contains no design-system artifacts** — no package manifest predating this refactor,
  no utility-CSS configuration, and no theme, token or variables file.
- The capability area that would own one is the deferred one (§12.1).

Accordingly, no design-system alignment exercise is performed in Phase 1; doing so would be work on a
forbidden target.

### 12.3 The three UI capabilities that in-scope logic genuinely touches are split

Three DataWindow services are irreducibly part-presentational. Rather than approximate the rendering
half or drop the capability, each is **split**: the headless half ships in DataServices and is returned
over the contract as data; the rendering half is deferred and named as a reserved Gateway extension
point under `/v1/design/**` (§4.4).

| Capability | Headless half — ships in DataServices | Rendering half — deferred |
| --- | --- | --- |
| Drop-down search | Filter-expression construction including the pinyin clause; the search state machine; row and filtered counts | Window positioning and input-method text entry |
| Context menu | The complete menu item model — labels, identifiers, enabled and split flags, computed logical text widths | DPI-to-pixel conversion, font measurement, and actual menu rendering |
| Column sort | Sort-expression construction and sort state | Sort-indicator geometry |

**Nothing is silently dropped.** The gap is explicit and enumerable, which is the whole point: a
documented gap is better than a half-built capability. [`DEFERRED.md`](DEFERRED.md) carries the detail
and the object-level assignment.

---

## 13. Known limitations and anomalies

Carried into implementation openly rather than discovered during it. Where the honest answer is "report
blocked rather than approximate", that is what it says.

| # | Limitation or anomaly | Position taken |
| --- | --- | --- |
| L1 | **Pinyin first-letter matching cannot be proven exact from the repository alone**, and the risk is narrower than it first appears. The **flags are documented**: [`ws_objects/pfw.shared.pbl.src/enums.sru:L1146-L1149`] declares them under the comment `//PinyinFirstLetterLike:[flags]` as `PY_LIKE_IGNORE_CASE` = 1 (ignore case), `PY_LIKE_IGNORE_WIDTH` = 2 (ignore full-width versus half-width) and `PY_LIKE_FUZZY_SOUND` = 4 (fuzzy sound matching, `l`/`n`, `f`/`h`, `r`/`l`), so the literal `7` the call site passes [`n_cst_dwsvc_dropdownsearch.sru:L323`] enables **all three**. What remains unavailable is the **lookup table and the matching algorithm**, which exist only inside the closed native binary with no C++ source anywhere in the tree — and in particular the exact fuzzy-sound equivalence set, whose three documented pairs are illustrative rather than provably exhaustive | Characterize the table and the matching behaviour from the behavioural oracle; the flag decoding needs no characterization. **If the oracle cannot be exercised, report the pinyin filter as blocked rather than approximating it** — an approximation returns subtly different result sets, which is a regression that looks like correct behaviour |
| L2 | **Cross-session foreign column-expression variables are narrowed by design.** The legacy holds a live object pointer to another DataWindow's expression service, which cannot be serialized | Co-resident references are supported through a session-scoped handle; references spanning sessions or service instances are **blocked with a defined error**, never given a silently wrong value. A deliberate, documented narrowing — see [`CONTRACTS.md`](CONTRACTS.md) |
| L3 | **Encrypted-SQLite page-format parity is out of Phase-1 scope** (§8.3) | Provision the unencrypted path only and record the limitation, rather than attempting a format the target provider cannot produce |
| L4 | **Nothing assembles the container set, so no multi-service bring-up has occurred** (§10.6). All four definitions exist and so does `.github/workflows/ci.yml`; `orchestration/docker-compose.yml` and `orchestration/README.md` are absent, so the health-condition chain has no expression in the tree. One image (Security) was built and run and reached Docker health `healthy`; no request has crossed a real network boundary between two services | Definition-and-manifest review plus CI is the **intended** assurance mechanism once the manifest is authored, not a step that has been taken. **No verified multi-service bring-up is claimed, and no completed review is claimed either.** What is claimed is in-process: twenty projects build with zero warnings and ten test projects pass 18,607 tests with zero failures |
| L5 | **There is no authoritative legacy build definition to translate.** The two project objects disagree on library count and vendor, and both reference a library that exists nowhere [`project.srj:L41`, `p_pfw.srj:L40`]; there are zero Git tags, so no release is marked (§1.3) | Author the .NET build and CI as clean creations, using the project objects as reference for *intent* only |
| L6 | **One compiled library has no source export.** `pfwx.utility.codec.pbl` appears on the secondary target's library list [`pfwx.pbt`, `LibList`] with no corresponding source directory | Recorded as an anomaly. It contributes no objects to the estate reconciliation and no capability to any service — see [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| L7 | **Two distinct files share the name `pfw.sra`.** `ws_objects/pfw.pbl.src/pfw.sra` is the framework application and is authoritative for the composition root; `ws_objects/pfw.pack.pbl.src/pfw.sra` is the packager | Every reference cites the full path. A relative reference to either would be ambiguous, so none is used |
| L8 | **The secondary application's lifecycle is unpaired.** `pfwx.sra`'s `open` event contains no initialize call [`:L48-L50`] while `close` still finalizes [`:L52`], asymmetric with the primary application (§7.1) | Recorded as an observed anomaly. The .NET host cannot reproduce it because the pairing is structural there, and the secondary application is reference-only |
| L9 | **The legacy documentation is inaccurate in two places** about the initializer object (§7.3) | Cite the verified locations; note the discrepancies here; never edit the legacy document (C-C) |
| L10 | **One-based to zero-based array translation is a silent-defect hazard.** PowerBuilder arrays are one-based and its upper-bound function returns the last valid index | Route every ported loop through a centralized one-based indexing helper or audit it individually. The reverse-iteration cases are the most dangerous: one buffer is traversed backwards *because* its row order is inverted, so "correcting" the direction produces wrong data that still passes a row-count assertion |
| L11 | **The changelog is not a specification and is stale** (§1.3) | Derive behaviour from source only. Every behavioural claim in this document set carries a source locator |

---

## 14. Closing note: what this architecture does and does not claim

Two facts anchor everything above, and both are checkable:

- **The legacy is a library with no process** (§2.2), so every boundary and every contract in this
  design is genuinely new rather than a translation of something that already existed. That is why the
  contract inventory and the full-estate mapping were published for review *before* code generation
  rather than emitted as by-products of it.
- **The legacy's own eight-bit capability bitmask already expresses a decomposition intent** (§6), and
  the Phase-1 slice agrees with it. Storage, presentation, scripting and packaging were separated by
  the framework's own authors; this refactor draws its first four boundaries along those same seams.

And one thing this document deliberately does not claim: **no performance objective is asserted
anywhere in it**, because the repository publishes no service-level agreement, no latency budget, no
throughput target and no availability commitment of any kind (C-B). Each service is **independently
deployable and independently scalable** — architectural properties of the topology in §3.2 — and no
statement is made about what any instance achieves, because no baseline exists to compare one against
and inventing one would be a fabricated requirement. The only quantitative gate in the whole refactor
is the per-service line-coverage threshold, which the **planned** CI pipeline will enforce — the workflow
file does not exist yet, so the gate is specified and currently unenforced; see [`BUILD.md`](BUILD.md)
§10.
