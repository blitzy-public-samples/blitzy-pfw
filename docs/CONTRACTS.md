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

# PowerFramework → .NET 10 — Cross-Service Contract Inventory

This document is the complete inventory of the **cross-service contracts** and the **reserved
Gateway extension points** of the four-service .NET 10 decomposition of PowerFramework. It is one of
only **two** deliverables the brief asks to be **reviewed before code generation begins** — the
other being the full-estate mapping in [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md).

That ordering is deliberate and it is the reason this document exists as prose rather than only as
schema. The `.proto` and OpenAPI definitions under `shared/PowerFramework.Contracts/` are authored
**against** this inventory, so a message shape agreed here propagates directly into generated stubs,
into every service that consumes them, and into the characterization recordings that compare the two
systems. A contract that is wrong here is wrong everywhere downstream, and it is wrong in a way that
compiles.

**Audience.** A reviewer deciding whether these ten boundaries are the right ten, shaped correctly;
and the implementer who then writes the schema. Every design decision below is stated together with
the legacy interface shape that forced it and a locator into the read-only legacy tree, so neither
reader has to take this document's word for anything.

**What this document deliberately does not duplicate.** Cross-reference rather than restatement is
the rule, because a figure repeated in two places is a figure that will eventually disagree with
itself:

| For | See |
| --- | --- |
| Service topology, the port map, transport rationale per service, capability gating | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| The full-estate mapping — all 39 libraries and all 544 objects assigned to a destination | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| The four deferred destinations in detail and the objects assigned to each | [`DEFERRED.md`](DEFERRED.md) |
| Secret locators, severities, required actions, and the token-topology register | [`SECRETS.md`](SECRETS.md) |
| The characterization model, the fixture corpus, and the determinism seams | [`docs/PARITY.md`](PARITY.md) |
| Build and test commands, the solution layout, per-service build independence | [`BUILD.md`](BUILD.md) |

**How the method surfaces in this document are produced, and why that matters.** Every method-surface
table below is **derived from the generated artifacts** — the compiled protobuf file descriptors for
the six gRPC contracts, and the OpenAPI document itself for the two REST ones — rather than written
from the design notes. An inventory that claims completeness has to be checkable, and a hand-written
list of a 26-method service is a list that silently falls behind the schema — this document has already
proved that twice, and in OPPOSITE directions: it carried `26` for C-04 across four derived totals after
`LoadRows` was added to the schema, and `27` across the same four after `LoadRows` was withdrawn from it.
Which is why the counts below are regenerated from the descriptors rather than edited by hand. The command that regenerates them is in
[§16.3](#163-regenerating-the-counts-because-a-hand-maintained-inventory-has-already-drifted-once).
The register:

| Contract | Surface | Count | Streaming shapes |
| --- | --- | --- | --- |
| C-01 `TokenService` | REST | 3 operations | — (one takes `clientCredential` OR `mutualTls`, two anonymous) |
| C-02 `CryptoService` | REST | 18 operations: 17 covering all **63** legacy overloads, plus one authored release operation | — (all `bearerAuth`) |
| C-03 `DataWindowService` | gRPC | 16 methods | 1 server stream, 1 bidirectional |
| C-04 `ColumnExpressionService` | gRPC | 26 methods | 1 server stream, 2 bidirectional (both **inverted**) |
| C-05 `QueryService` | gRPC | 11 methods | 1 server stream |
| C-06 `UpdateService` | gRPC | 5 methods | — |
| C-07 `CommandService` | gRPC | 6 methods | — |
| C-08 `TransactionService` | gRPC | 13 methods | — |
| C-09 `gateway.v1` | REST | see [§12.1](#121-c-09--gatewayv1-rest-ingress) | — |
| C-10 Health and readiness | REST | 2 operations per service | — |

The Security listener publishes **23 operations in total**: C-02's 18, C-01's 3, and C-10's 2.

## Current state of the artifacts this document references

**Every artifact this document references is present in the tree today** — the solution and project
files, the shared libraries, the protocol and OpenAPI definitions under
`shared/PowerFramework.Contracts/` including both `gateway.v1.yaml` and `security.v1.yaml`, the
per-service settings, [`PARITY.md`](PARITY.md) and the read-only legacy tree. Nothing named below has
to be imagined.

---

## Table of contents

1. [Status: every contract in this document is NEW](#1-status-every-contract-in-this-document-is-new)
2. [Topology and the single coupling rule](#2-topology-and-the-single-coupling-rule)
3. [The ten contracts at a glance](#3-the-ten-contracts-at-a-glance)
4. [C-01 — `security.v1.TokenService`](#4-c-01--securityv1tokenservice)
5. [C-02 — `security.v1.CryptoService`](#5-c-02--securityv1cryptoservice)
6. [C-03 — `dataservices.v1.DataWindowService`](#6-c-03--dataservicesv1datawindowservice)
7. [C-04 — `dataservices.v1.ColumnExpressionService`](#7-c-04--dataservicesv1columnexpressionservice)
8. [C-05 — `persistence.v1.QueryService`](#8-c-05--persistencev1queryservice)
9. [C-06 — `persistence.v1.UpdateService`](#9-c-06--persistencev1updateservice)
10. [C-07 — `persistence.v1.CommandService`](#10-c-07--persistencev1commandservice)
11. [C-08 — `persistence.v1.TransactionService`](#11-c-08--persistencev1transactionservice)
12. [C-09 and C-10 — the ingress and readiness contracts](#12-c-09-and-c-10--the-ingress-and-readiness-contracts)
13. [The four reserved Gateway extension points](#13-the-four-reserved-gateway-extension-points)
14. [Cross-cutting contract rules](#14-cross-cutting-contract-rules)
15. [Rejected alternatives](#15-rejected-alternatives)
16. [Versioning, compatibility, and the reviewer checklist](#16-versioning-compatibility-and-the-reviewer-checklist)
17. [Evidence index, and what this document does not claim](#17-evidence-index-and-what-this-document-does-not-claim)

---

## 1. Status: every contract in this document is NEW

### 1.1 There is no prior art to check the design against

> **Every one of the ten contracts below is NEW. None of them translates an existing wire format.**

This is the single most important fact about this inventory, and it frames everything that follows.

PowerFramework is a **library**, not a server. It has no process of its own, no listening socket, no
route table, no serialization layer, and no authentication of any kind — because there is nothing to
authenticate against. Thirty-nine libraries are loaded into one desktop process and every call
between them is an in-process, synchronous method or event invocation. Decomposition therefore
creates the system's first-ever process boundary, its first-ever wire format, and its first-ever
ingress.

The consequence for this document is precise: there is **no legacy protocol to be faithful to**.
There is only the *shape of the in-process interface* each contract replaces — a `ref string`
out-parameter, an ordered event chain, a live object pointer, a raw clause string — and the question
of whether that shape survives being cut in half by a network. Sometimes it does. Twice in this
inventory it does not, and both places are named explicitly rather than discovered later:

- the cross-session foreign expression variable in [C-04](#7-c-04--dataservicesv1columnexpressionservice),
  which is an in-process pointer dereference;
- the `updatewhereclause` original-value comparison in
  [C-06](#9-c-06--persistencev1updateservice), which forces every marked column's *original* value onto
  the wire alongside its current one.

That is exactly why the brief asks for this inventory to be **reviewed** rather than generated
silently. A schema generator has no way to notice that a structure field is a pointer, or that a
concurrency mode compares six columns rather than one.

### 1.2 The governing principle for anything that cannot cross

> **Where a legacy behaviour cannot be reproduced across a network boundary, the contract is
> narrowed with a defined error — never widened with a guess.**

A narrowed contract fails loudly at a documented boundary. A guessed one returns a plausible wrong
answer that no assertion catches, because the assertion was written against the guess. Every
narrowing in this inventory is stated at the point it applies, with the mechanism that forces it.

### 1.3 No user-specified rules exist

The project's rules document was retrieved and it contains exactly one statement: **no user rules
were provided.** That is a finding, not an omission, and it is recorded here so that its absence is
never mistaken for latitude. Three consequences bear on this document:

- **No rule is invented, inferred, or back-filled from convention.** Enterprise-standard best practice
  applies in their place, and for a contract inventory that means three things concretely: explicitly
  versioned contracts as the **only** cross-service coupling; no behaviour crossing a boundary except
  through a published contract; and every contract decision traceable to the legacy interface shape
  that forced it.
- **Zero contracts and zero message fields exist because a rule demanded them.** Every element below
  traces either to an explicit clause of the brief or to a legacy interface shape cited by locator.
- **The binding constraints therefore come from elsewhere**, and they are cited by name throughout —
  C-B, C-C, C-D, C-E, C-F, C-G and C-K. Their full text lives in the plan of record and is not
  reproduced here; each is invoked where it bites, with the specific thing it requires of this
  document.

### 1.4 Evidence discipline and citation convention

Every behavioural claim carries a locator of the form
`ws_objects/<library>.pbl.src/<object>:L<from>-L<to>`, and within a subsection the object is cited
once in full and then abbreviated to `:L<n>`. Nothing else in this repository can adjudicate
behaviour:

- `logfile.md` is stale — its first line is exactly `## 3.0.7.2062(2022-04-14)` while the commit
  history runs years later, and there are **zero Git tags across 333 commits**, so no release is
  marked;
- the two PowerBuilder build definitions contradict each other on library count and vendor, and
  **both reference `pfw.utility.imgcodec.pbl`, which exists nowhere in the repository**, so neither
  would build as written.

Source is therefore the only oracle, and it is **read-only** (C-C). No path under `ws_objects/**` is
edited, moved, renamed, or reformatted by this refactor, and neither are the five pre-existing
Chinese documents in this folder — `README.md`, `Blink交互.md`, `Sciter交互.md`, `PB多线程绕坑提示.md` and
`n_cst_dwsvc_columnexp.md`. The last of these is the authoritative specification for C-04 and is
cited heavily below by section marker and line range; it is **read, never touched**. This document
is purely additive.

Where a figure in an upstream planning summary disagreed with the source, **the source wins**; the
seven such corrections are listed in [§17.2](#172-corrections-applied-during-verification) so a
reviewer can see that they were checked rather than copied.

**No secret value of any kind is reproduced in this document** (C-F) — no key, no certificate, no
credential, no token, in any encoding. Where a field carries such material, only the *field* is
named and its handling rule stated. Locators for the material itself live in
[`SECRETS.md`](SECRETS.md).

---

## 2. Topology and the single coupling rule

### 2.1 The call graph the contracts realize

The graph is strictly layered and acyclic. It is stated here only as the frame for the contracts;
the port map, the transport rationale per service and the diagram belong to
[`ARCHITECTURE.md`](ARCHITECTURE.md) §3–§5 and are not restated.

| Caller | Callees | Contracts used |
| --- | --- | --- |
| External clients | Gateway only | C-09, C-10 |
| **Gateway** | DataServices, Security | C-03, C-04, C-01, C-10 |
| **DataServices** | Persistence, Security | C-05, C-06, C-07, C-08, C-01, C-02 |
| **Persistence** | Security's published verification material only | C-01 (key set only) |

- Nothing calls Gateway except external clients.
- Nothing but Gateway calls DataServices.
- Nothing but DataServices calls Persistence.
- Persistence never calls Security's issuance endpoint; it reads the published key set.

### 2.2 The only cross-service coupling

> **The published contracts project is the *only* coupling between services. It is the boundary
> definition, not a shared-code back door.**

`shared/PowerFramework.Contracts` carries protocol definitions, OpenAPI documents and the stubs
generated from them. It carries **no behaviour**: no validator, no expression evaluator, no SQL
builder, no cryptographic primitive. Anything a service does, it does inside itself.

The rule is worth stating as a prohibition, because the failure mode is gradual rather than sudden.
The first helper added to the contracts project "because both sides need it" makes two services
share an implementation without either declaring a dependency on the other, and from then on the
boundary is decorative. If two services need the same behaviour, either it belongs to one of them
and the other calls it through a contract, or it is duplicated deliberately and the duplication is
documented.

### 2.3 Where the definitions live

For the reader's orientation. Namespaces are
`PowerFramework.Contracts.{Security, DataServices, Persistence}.V1`.

| File under `shared/PowerFramework.Contracts/` | Carries | Present in the tree today |
| --- | --- | --- |
| `Proto/dataservices.v1.proto` | C-03, C-04 | **yes** |
| `Proto/persistence.v1.proto` | C-05, C-06, C-07, C-08 | **yes** |
| `Proto/common.v1.proto` | Messages shared across the protocol definitions: the return code, the DataWindow buffer selector, the item status, the database error, and the conflict detail | **yes** |
| `OpenApi/security.v1.yaml` | C-01, C-02 | **yes** |
| `OpenApi/gateway.v1.yaml` | C-09 (and the ingress half of C-10) | **yes** |

**All five definition files exist**, so every filename referenced below names a file a reader can
open. The distinction is worth stating because it did not always hold: C-09 was specified in this
document before its schema was authored, and a reference to a planned filename reads exactly like a
reference to an existing one.

---

## 3. The ten contracts at a glance

| ID | Contract | Transport | Served by | Consumed by | Shape that decided it |
| --- | --- | --- | --- | --- | --- |
| **C-01** | `security.v1.TokenService` | REST + JWKS over HTTPS; issuance takes **`clientCredential` OR `mutualTls`** and no bearer token | Security | Gateway, DataServices, Persistence | Stateless issuance; stock bearer handlers must self-configure over plain HTTP semantics, and the channel must be TLS because a client certificate cannot be presented without it — while the `Basic` alternative is what keeps issuance authenticated where an intermediary terminates TLS |
| **C-02** | `security.v1.CryptoService` | REST | Security | DataServices | 63 stateless cryptographic overloads across 17 operations, plus one authored release operation; no ordering, nothing to stream |
| **C-03** | `dataservices.v1.DataWindowService` | gRPC (server + bidirectional streaming) | DataServices | Gateway | A 22-event ordered chain with a `ref string` out-parameter, an `any` return, and cross-event mutable state |
| **C-04** | `dataservices.v1.ColumnExpressionService` | gRPC (two inverted streams) | DataServices | Gateway | A macro protocol the *application* implements, plus a seven-structure expression model containing a live object pointer |
| **C-05** | `persistence.v1.QueryService` | gRPC (server streaming) | Persistence | DataServices | Progressive chunked recordset delivery; clause modification by raw string |
| **C-06** | `persistence.v1.UpdateService` | gRPC | Persistence | DataServices | Optimistic concurrency over the original values of every marked column, plus a multi-table descriptor array |
| **C-07** | `persistence.v1.CommandService` | gRPC | Persistence | DataServices | Action-oriented `Exec` with positional binding and a three-valued commit mode |
| **C-08** | `persistence.v1.TransactionService` | gRPC | Persistence | DataServices | Session lifecycle over a nine-field descriptor and a reference-counted pool |
| **C-09** | `gateway.v1` REST ingress | REST + OpenAPI | Gateway | External clients | Sole ingress; must be reachable and discoverable by third parties |
| **C-10** | Health and readiness | REST | All four | Orchestrator, operators, all four services | An ordered readiness gate expressible as a Compose health condition |

Two of the ten carry almost all of the risk in this refactor, and they are the two the brief singles
out. **C-06** must move both the current *and* the original value of six columns per row, and must
reproduce a workaround the legacy documents against itself. **C-04** must move the unexpanded
expression, the bind-time snapshot *and* the live variable environment, tagging every reference with
its expansion mode, and must invert two streams. They are given proportionately more room below.

---

## 4. C-01 — `security.v1.TokenService`

| | |
| --- | --- |
| **Identifier** | `security.v1.TokenService` |
| **Transport** | REST, OpenAPI-described, with a standard key-set endpoint |
| **Served by** | Security |
| **Consumed by** | Gateway, DataServices, Persistence |
| **Definition** | `OpenApi/security.v1.yaml` |
| **Status** | NEW — no legacy equivalent exists; the legacy has no token concept and no boundary to protect |

### 4.1 Method surface

| # | Operation | `operationId` | Auth | Purpose |
| --- | --- | --- | --- | --- |
| 1 | `POST /v1/tokens` | `issueToken` | **`clientCredential` OR `mutualTls`** — an HTTP `Basic` credential naming a subject on the issuance roster, or a chain-verified client certificate. Either satisfies the operation; neither is optional | Issues a short-lived service token from a caller identity, an audience, and a scope set |
| 2 | `GET /.well-known/jwks.json` | `getJsonWebKeySet` | **anonymous** | Publishes the verification material — public members only, never a private one |
| 3 | `GET /.well-known/openid-configuration` | `getOpenIdConfiguration` | **anonymous** | Discovery metadata, so a consumer's stock bearer handler self-configures |

**Row 1 accepts two schemes because the caller identity must not be contingent on how TLS is
terminated.** A client certificate reaches the application only when the listener requests one and
nothing between the caller and this service re-terminates the connection; a reverse proxy, a service mesh
sidecar or an ingress controller that terminates TLS on this service's behalf leaves `mutualTls`
unpresentable while the channel is still encrypted. Publishing `mutualTls` alone would therefore make the
authentication of this boundary contingent on a deployment topology the contract cannot see, which is how
an authenticated edge quietly becomes an unauthenticated one in the deployment that actually runs.
`clientCredential` is a header the operation reads itself, so it works on every topology; `mutualTls` is
published for the deployment that terminates TLS here, which is what this repository's own listener does.
**A request presenting neither is refused, so there is no address on any topology at which a token is
minted without a caller credential — and TLS is the channel, never the caller identity.**

**The service declares exactly ONE listener, and every row above is published on it.**
`https://+:5104`, `Http1`, in the base settings file, with the Development overlay overriding the same
endpoint key rather than adding a second. `Http1` rather than `Http1AndHttp2` because Security publishes
no gRPC contract, and because a listener that accepts only what it is for cannot be misaddressed
silently. Two earlier revisions are recorded because both were withdrawn: one bound a
cleartext endpoint beside a TLS one, carrying everything except row 1, which placed an unauthenticated
listener on the one service holding the system's signing key; the other bound TLS alone and rewrote the
documented readiness gate's scheme to match, which made the documented bring-up unable to start this
service at all.

**Where a deployment does terminate TLS here, `ClientCertificateMode` is `AllowCertificate`.** Kestrel
then *requests* a client certificate during the handshake and hands whatever it receives to the
application **without demanding one**, so row 1 validates it *per operation* while rows 2 and 3,
`/health`, `/v1/ping` and all of C-02 stay reachable with no certificate at all. `RequireCertificate` at
the listener was measured and rejected: it aborts the handshake for any client presenting none, which
takes the anonymous `/health` probe with it so the readiness chain gating Gateway could never open.

**Row 3 publishes ONE identity and TWO kinds of location, and a consumer has to know which is which.**
The `issuer` member is a configured identity: it is the same on every response and is what a bearer
handler compares the `iss` claim of every token against byte for byte. `jwks_uri` and `token_endpoint` are
locations, and **they may legitimately differ between two consumers of the same service**, because one
service on this topology is reachable on two addresses — its in-network name, which is also the issuer,
and the published host port an operator, the end-to-end suite or a third party reaches it through. Both
were previously composed from the issuer alone, so a consumer outside the service network was handed a
`jwks_uri` naming a host that resolves only inside it: the document parsed and looked correct, and the
handler following it failed to *resolve* the address rather than being told anything was wrong. Each
address is now composed from a base address **the deployment declared**, chosen by matching the origin the
request arrived on, with the issuer as the fallback — so the set of publishable addresses is exactly the
configured set and no caller-supplied host is ever advertised. `orchestration/.env.example` declares the
additional address as `SECURITY_PUBLIC_BASE_URL`. A declared entry may carry a path prefix, which is
how a path-prefixing proxy is expressed: the well-known path is *concatenated* onto the declared base, so
the prefix survives into the published address. The host of every declared origin must also be admitted by
Security's `AllowedHosts`, which is the coarser control deciding whether the request is answered at all. Note that the canonical server entry this schema
publishes, `https://localhost:5104`, is itself the host-side address rather than the in-network one, which
is precisely the second location that had no way to be advertised.

Rows 2 and 3 are the verification material every other service trusts, so a channel an attacker can
rewrite would let that attacker choose the keys used to validate every token in the system. **That is why
this listener terminates TLS in every environment** — the exposure is closed rather than accepted, and
[`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1 and §9.4 state what it would have cost so a later deployment
cannot reintroduce it as a convenience. Anonymous does not mean unprotected: it means no credential is
*required to read* material that is public by design, on a channel whose integrity is still guaranteed.
[`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1 carries the listener map and §9.3.1 the credential bootstrap.

The issuance request carries the caller identity, the intended audience, and the requested scope
set; the response carries the token, its type, its expiry, and the granted scope set. **On a `200`
the granted set equals the requested set**, because a scope the caller may not have is *refused*
rather than quietly dropped — see the refuse-don't-narrow rule below. A caller still reads the
granted set rather than inferring it, because that is the set the token actually carries and the set
every downstream route will be compared against.

**A scope the roster does not grant is refused, never narrowed.** Narrowing is contract-legal and it
was rejected on purpose: it hands back a `200` and a usable token that is missing one capability, so
the caller proceeds, and the loss surfaces later as a `403` from whichever downstream route needed
the dropped scope — at a service that did nothing wrong, in a request that no longer names the
misconfiguration that caused it. Refusing at issuance puts the error where the wrong configuration
is, names which scope was not permitted, and leaves no half-privileged token in circulation.

**Issuance is authenticated by a credential the operation checks, and the request body carries none.**
The schema declares `clientCredential` and `mutualTls` and applies them to `POST /v1/tokens` as an
**override** of the document-level bearer requirement, because a caller cannot present a bearer token in
order to obtain its first bearer token. The `subject` field names the identity the caller *claims*; the
identity actually honoured is the one the presented credential establishes. The request schema sets
`additionalProperties: false` and carries no `clientSecret`, `password`, `apiKey`, `assertion` or key
material of any kind, so a credential cannot be smuggled into the **body** either — it travels in the
`Authorization` header or in the handshake, where a proxy log records a header name rather than a body
value. The two failure modes are part of the contract, not implementation detail:

**Closure is enforced on the receiving side, and the generated document carries it.** Two properties
make the paragraph above a rule rather than a description, and both are stated here because either one
alone leaves the other unenforced. First, Security's JSON binder refuses an undeclared member instead
of discarding it: a body carrying one — or a body that is not well-formed JSON — is answered `400`
with the single published problem shape carrying `RetCode.E_INVALID_ARGUMENT`, and the detail is fixed
prose that echoes no part of the body, because on this surface the body is plaintext, ciphertext or a
key reference. This is disjoint from the absent-member refusal: a member that is *missing* still
reaches its handler and is still named in its wire spelling, which is what the nullable request
members exist for. Second, the document Security serves from `/openapi/v1.json` — the artifact a
client generator actually reads — is brought back to the requiredness and the closure declared in
`OpenApi/security.v1.yaml`, including `ProblemDetails` staying **open**, since RFC 9457 problem
details are extensible by design and every error body here carries the `retCode` extension member.

| Status | Meaning on `POST /v1/tokens` |
| --- | --- |
| `401` | No credential was presented at all, or one this service does not hold — an unknown roster subject, a secret that does not match, or a certificate that does not chain to the configured issuer. There is **no bearer-token alternative** on this operation to fall back to, and the response does not distinguish which condition was hit |
| `403` | The caller **was** authenticated but is not permitted what it asked for. Four distinct conditions share this status: the claimed `subject` does not match the identity the presented credential establishes; the requested `audience` is not one this deployment serves at all; the audience is served but is not among those the roster entry for that subject grants; or one of the requested scopes is not among those that entry grants. Each answers a different sentence, and none names the expected identity, the audiences the deployment serves, or any part of the stored configuration |

**Issuance policy is per-caller, not deployment-wide, and both gates are checked before any clock read
or signing operation.** Gate one is the set of audiences the deployment serves at all — one answer for
every caller. Gate two is the issuance roster: for the authenticated subject, which of those audiences
it may address and which scopes it may request. A deployment-wide list alone would mean every caller
that can authenticate can mint for every audience with every scope, which makes one leaked credential
a credential for the whole system rather than for one caller's edges. The roster is configuration, its
shape is validated at startup, and a grant naming an audience the deployment does not serve is a
**startup failure** rather than an unreachable permission that reads as if it were granted.

The two anonymous publications are unauthenticated by design. Every C-02 operation and `/v1/ping`
authenticate under the ordinary bearer rule instead of a caller credential — and additionally require
a **scope**, which is [§5.1](#51-method-surface)'s subject for C-02 and stated with C-10 for
`/v1/ping`. Only issuance is credential-authenticated.

### 4.2 Security is the sole token authority

> **Security is the sole minter. Gateway, DataServices and Persistence hold verification material
> only. Exactly one signing secret exists in the entire system.**

This discharges C-G, and each clause of it is load-bearing:

- **No service other than Security can mint a token.** None of the other three holds a signing key or
  any independent signing authority. Where per-service key names are retained at all they are
  verification-side names.
- **A token is required on every internal edge, not merely at the ingress.** The legacy opens no
  listening socket and receives no unsolicited request, so decomposition creates every one of these
  boundaries from nothing. An internal edge that trusted its caller because "it is internal" would be
  a *new unauthenticated surface* — precisely what the requirement exists to prevent. `/v1/ping` is
  the standing proof of the property on all four services: it requires a token and returns `401`
  without one (see [C-10](#12-c-09-and-c-10--the-ingress-and-readiness-contracts)).
- **A token authenticates; a scope authorizes. Where an operation declares one, both are required.**
  A bearer requirement alone makes every token this deployment issues equivalent at every protected
  operation, so one caller's credential reaches capabilities that caller never needed. Each protected
  operation on Security therefore declares the single scope it requires — `ping` on `/v1/ping`,
  `security.crypto` on the whole C-02 group — and the composition root builds a named policy from the
  name the *route* declares, so a policy nobody requires cannot sit registered and silently enforce
  nothing. **`401` without a token is unchanged by this**; the scope check is reached only after a
  token has been accepted, and its refusal is `403`.
- **The ingress enforces it too, and it is the surface where enforcing it matters most.** Gateway
  declares three scopes across the surface it publishes — `ping` on `/v1/ping`, `capabilities` on
  `/v1/capabilities`, and `datawindow` on the whole C-03 and C-04 projection, applied to the parent
  route group so no operation added beneath it can be added without one. Three distinct scopes rather
  than one reused: three routes sharing a scope would be a single entitlement wearing three names,
  which is what requiring only an authenticated principal already was. Both `/v1/ping` and
  `/v1/capabilities` therefore declare `403` alongside `401` in
  [`gateway.v1.yaml`](../shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml), because an
  undeclared response on the ingress is an undocumented surface.
- **And the requirement is published per operation, because the security scheme cannot express it.**
  Every operation in `gateway.v1.yaml` carries an `x-required-scope` extension whose value is one of
  four: `ping`, `capabilities`, `datawindow`, or `none` for the anonymous probe and the eight reserved
  operations. The extension exists because `bearerAuth` is applied as `bearerAuth: []` throughout and
  **that empty array says nothing about scope** — OpenAPI defines the security-requirement array as a
  scope list for `oauth2` and `openIdConnect` schemes only, so for an `http`/`bearer` scheme an empty
  array is the sole meaningful value. Reading the silence as "no scope required" is a mistake that was
  made once, in Security's provisioning guidance, and it cost a documented bring-up: the operator
  granted a placeholder scope, issuance succeeded because the granted set is the *overlap* with the
  request and a narrowing is a success, and every route the caller then reached answered `403`. The
  worked grant is caller `pfw-e2e-suite`, audience `powerframework-gateway`, all three scopes —
  published identically in Security's `appsettings.Development.json`, `orchestration/.env.example` and
  `orchestration/docker-compose.yml`, and held to the routes' own constants by a coherence test.
- **The four reserved extension points deliberately require authentication and nothing further.**
  Requiring a capability scope for a route that reaches no capability would invent an entitlement for
  a service this phase must not implement even in metadata (C-D), and the issuance roster grants no
  such scope to anyone — so every caller would be answered `403` and the reserved `501` would become
  unreachable, which would defeat the one thing those routes exist for. Authenticated-only is the
  whole requirement there: the reserved roster is not anonymously enumerable, and every authenticated
  caller receives the same `501`.
- **Mutual TLS is the per-pair fallback, and the token-issuance edge is that pair.** Where a token
  issuer is inappropriate for some service pair, mutual TLS applies **for that pair only**, adding
  certificate and key path settings for those two services rather than changing the system-wide
  model, and tokens remain the default on every other edge. That rule has exactly one instance in
  this system and it is named rather than left abstract: **`POST /v1/tokens`**, the one operation a
  bearer token cannot protect, for the structural reason in §4.1. It is published there as one of **two**
  accepted schemes rather than as the only one, because a client certificate reaches the application only
  where the TLS handshake is terminated by this service itself — so the roster `clientCredential`
  authenticates the edge on every topology, including one where a proxy or a mesh sidecar re-terminates
  the connection, and `mutualTls` authenticates it wherever Security terminates TLS directly, which is
  what this repository's own listener does. Any certificate or key path it needs points at
  material mounted from the orchestration secret layer — nothing is committed to this repository and
  nothing is embedded in an image, and every such variable defaults to empty so nothing looks configured
  that is not.
- **No signing, verification or mutual-TLS material is scaffolded for any deferred service.**
  Provisioning a credential for a service that does not exist creates an unowned secret.

The secret's name and the full token register are in [`SECRETS.md`](SECRETS.md). **No value appears
in any document.**

### 4.3 Why this contract is REST, and why that is not a style preference

Token issuance and key publication must speak **ordinary HTTP** — rather than a bespoke protocol — so
that a consumer's **stock bearer handler** fetches `/.well-known/jwks.json` and the discovery
document with **zero bespoke code**. That keeps the security-critical retrieval path inside framework
code rather than hand-written code, on three services rather than one.

**"REST" here names HTTP semantics, not a scheme, and the choice would hold on either.** The schema
publishes exactly **one** canonical server entry, `https://localhost:5104`, matching the one listener the
service binds; a local bring-up changes only the host. Exactly one entry rather than two is deliberate: a
second entry in the other scheme would make both a *published, selectable* base URL for the whole API,
and a caller picking the wrong one would
be following the contract. The contract names the address the repository actually serves, and a deployment
that serves another republishes it. Two consequences a deployment has to honour:

- **The issuance path must not be terminated by an intermediary.** Mutual TLS authenticates the
  client to Security itself, so a proxy that terminates TLS on `POST /v1/tokens` either discards the
  client certificate or leaves Security trusting a forwarded assertion it cannot verify — either of
  which defeats the sole-issuer topology. Bearer-protected and anonymous operations may sit behind a
  terminating proxy in the ordinary way.
- **On plain HTTP, issuance would have no caller authentication at all**, because there would be no
  client certificate to present. That is why no configuration in this repository puts this service on
  plaintext, in any environment: consumers keep an https authority and `RequireHttpsMetadata` true in
  base *and* development settings, and **no deployed `appsettings.json` or `appsettings.Development.json`
  anywhere in the tree sets `RequireHttpsMetadata: false`.** Test hosts are a deliberate exception and
  are not deployed configuration: `Gateway.Tests` and `DataServicesTestHostFactory` set it to `false`
  explicitly, because each stands up an isolated in-process plain-HTTP authority with no certificate,
  and the option is mandatory there. That is the setting doing its job in the one topology it exists for,
  not a relaxation with no consumer. A deployment that genuinely does run Security on plaintext has to
  state that relaxation explicitly, and gets a named startup failure until it does.
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §9.4 carries the full model.

**Rejected alternative — gRPC for Security** (recorded per C-K). Choosing gRPC here would force a
custom key-set retrieval implementation into each of the three consuming services. That is a net
*increase* in hand-written security code, which is the opposite of what the requirement is trying to
achieve. It is the wrong direction, not merely a less convenient one. The same reasoning is recorded
from the architectural side in [`ARCHITECTURE.md`](ARCHITECTURE.md) §5.4.

### 4.4 Legacy anchor

The signing primitives Security will own already exist in the legacy cryptographic surface:
`RSASign` in its string and blob forms and `VerifyRSASign` in both, at
`ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73`. Concentrating that surface behind one boundary
is what makes a single signing authority possible at all — the primitives were already in one
object; what did not exist was a boundary around them.

---

## 5. C-02 — `security.v1.CryptoService`

| | |
| --- | --- |
| **Identifier** | `security.v1.CryptoService` |
| **Transport** | REST, OpenAPI-described |
| **Served by** | Security |
| **Consumed by** | DataServices |
| **Definition** | `OpenApi/security.v1.yaml` |
| **Status** | NEW — the legacy surface is an in-process object with no boundary of any kind |

### 5.1 Method surface

Grouped as the legacy groups them, with the legacy overload count that each group must cover.

| Group | Operations | Legacy locator | Legacy overloads |
| --- | --- | --- | --- |
| Unkeyed hash | Hash over a string or blob; hash of a file | `n_crypto.sru:L21-L22`, `:L27` | 3 |
| Keyed hash | Keyed hash over the string/blob cross-product of data and key; keyed file hash | `:L23-L26`, `:L28-L29` | 6 |
| Symmetric encrypt | String and blob plaintext × string and blob key × with and without an initialization vector × with and without an explicit mode | `:L30-L45` | 16 |
| Symmetric decrypt | The same cross-product over ciphertext | `:L46-L61` | 16 |
| RSA encrypt / decrypt | String and blob payload, with and without an explicit padding selector | `:L62-L69` | 8 |
| RSA sign / verify | String and blob forms of both | `:L70-L73` | 4 |
| RSA key generation | Bit length, with an optional PEM-format switch | `:L19-L20` | 2 |
| Random generation | Random blob, random string with and without a character-class flag set, GUID with and without a formatting flag set | `:L14-L18` | 5 |
| Encoding conversions | String-to-blob, blob-to-string, blob reversal | `:L11-L13` | 3 |

`ws_objects/pfw.crypto.pbl.src/n_crypto.sru` declares **65 functions at `:L9-L73`** — the 63
cryptographic operations tabulated above, plus two accessors (`Copyright` at `:L9` and `GetVersion`
at `:L10`) which are not part of this contract. The object is bound to a closed native library at
`:L8`; the operations themselves are substituted from the base class library, and no third-party
cryptography package is introduced.

**63 is the legacy overload count, not the published wire surface. The two numbers are different and
neither substitutes for the other:**

| | Count | What it counts |
| --- | ---: | --- |
| Legacy overloads | **63** | Distinct PowerScript declarations across the nine groups above, summing the final column |
| Projected overloads | **63** | Legacy overloads that have a landing site on the wire — every one of them |
| Not projected | **0** | Nothing is excluded. Every overload family, the three `HashFile` declarations [`n_crypto.sru:L27-L29`] included, has a landing site |
| Wire operations | **17** | The `POST /v1/crypto/**` operations they collapse onto |
| Authored operations | **1** | `POST /v1/crypto/rsa/keys/release`, which covers no legacy overload — see below |

Overloads collapse onto operations because what varies across a legacy overload group — string versus
blob payload, present versus absent initialization vector, present versus absent explicit mode — is
expressed on the wire as **fields of one request body** rather than as separate endpoints. So 16
`SymEncrypt` overloads become one `POST /v1/crypto/symmetric/encrypt`, and the 63-to-17 ratio is that
collapse, not a loss of capability.

**The `HashFile` family IS projected, and the way it is projected is what removes the path-traversal
concern.** A caller-supplied *server-side filename* arriving over HTTP would create an attack surface
the in-process legacy could not have had, because in-process the filename came from the same address
space as the caller (C-G). Excluding the family was one way to answer that, and an earlier revision of
this document took it; the published contract takes the better one. The two operations
`POST /v1/crypto/hash-file` and `POST /v1/crypto/hmac-file` accept an **opaque server-resolved
`fileRef`, never a path** — the same discipline `keyRef` follows for the same reason
([§5.2](#52-the-contract-level-secrets-rule)) — so no caller-supplied path reaches a filesystem call and
the capability is preserved rather than dropped. **There is consequently no excluded operation anywhere
in C-02.**

**The accounting to audit against, and where it lives.**
[`OpenApi/security.v1.yaml`](../shared/PowerFramework.Contracts/OpenApi/security.v1.yaml) is the
authority and carries the same 63-to-17 arithmetic: **if a future edit makes the legacy count anything
other than 63, or leaves an overload family with no landing site, the boundary has silently drifted from
the oracle.** Re-verify by counting `operationId` occurrences in that file and declarations in
`n_crypto.sru`, never by reading prose — including this prose. An earlier revision of this section, and
of that file's own header, recorded a superseded 63 / −3 / 60 / 15 accounting from before the file family
was projected; both are corrected, and only one inventory now exists.

**The shape is what decided the transport.** Every one of the 63 operations is a stateless
request/response with no ordering requirement between calls and nothing to stream. REST fits that
shape without remainder.

**THE ONE AUTHORED OPERATION, AND WHY IT DOES NOT DISTURB THE 63-TO-17 ARITHMETIC.**
`POST /v1/crypto/rsa/keys/release` covers **no legacy overload**, and that is not an accident of
counting — the legacy has nothing for it to cover. `GenRSAKey` [`n_crypto.sru:L19-L20`] hands the
private half straight back through a `ref` parameter, so the caller owns it from that moment and there
is no store to release from. `generateRsaKey` **retains** the private half instead, which is what makes
its response safe across a network boundary ([§5.2](#52-the-contract-level-secrets-rule)) — and a
retained thing needs a way to be given back, or the only way a caller frees its share of the store is to
wait out the retention lifetime. So the release operation is the *cost of the narrowing*, paid at the
same place the narrowing was made. It is counted separately above for exactly that reason: 63 legacy
overloads still land on 17 operations, and the 18th is authored.

**The 18 operations, enumerated.** The table below is derived from `OpenApi/security.v1.yaml` itself,
so it is what the document exposes rather than what it intends to, and every one of the 63 legacy
overloads lands in exactly one of rows 1 to 17. A legacy *overload* becomes a request FIELD, not an
operation, wherever the overloads differ only in argument shape — string versus blob, with or without
an initialization vector, with or without an explicit mode — because those are PowerScript's way of
expressing optional and alternative parameters and a JSON request expresses them directly.

| # | Operation | `operationId` | Legacy group it covers |
| --- | --- | --- | --- |
| 1 | `POST /v1/crypto/hash` | `hash` | Unkeyed hash over a string or a blob |
| 2 | `POST /v1/crypto/hash-file` | `hashFile` | Unkeyed **file** hash |
| 3 | `POST /v1/crypto/hmac` | `hmac` | Keyed hash, the four data × key overloads |
| 4 | `POST /v1/crypto/hmac-file` | `hmacFile` | Keyed **file** hash, both overloads |
| 5 | `POST /v1/crypto/symmetric/encrypt` | `symmetricEncrypt` | All 16 encrypt overloads |
| 6 | `POST /v1/crypto/symmetric/decrypt` | `symmetricDecrypt` | All 16 decrypt overloads |
| 7 | `POST /v1/crypto/rsa/encrypt` | `rsaEncrypt` | RSA encrypt, with and without an explicit padding selector |
| 8 | `POST /v1/crypto/rsa/decrypt` | `rsaDecrypt` | RSA decrypt, the same |
| 9 | `POST /v1/crypto/rsa/sign` | `rsaSign` | RSA sign, string and blob |
| 10 | `POST /v1/crypto/rsa/verify` | `rsaVerify` | RSA verify, string and blob |
| 11 | `POST /v1/crypto/rsa/keys` | `generateRsaKey` | Key generation, both arities |
| 12 | `POST /v1/crypto/random/blob` | `generateRandomBlob` | Random blob |
| 13 | `POST /v1/crypto/random/string` | `generateRandomString` | Random string, with and without the character-class flags |
| 14 | `POST /v1/crypto/random/guid` | `generateGuid` | GUID, with and without the formatting flags |
| 15 | `POST /v1/crypto/encoding/string-to-blob` | `stringToBlob` | String to blob |
| 16 | `POST /v1/crypto/encoding/blob-to-string` | `blobToString` | Blob to string |
| 17 | `POST /v1/crypto/encoding/blob-reverse` | `reverseBlob` | Blob reversal |
| 18 | `POST /v1/crypto/rsa/keys/release` | `releaseRsaKey` | **None — authored.** Releases a key row 11 retained |

**NO OPERATION ON THIS CONTRACT CARRIES A CALLER-SUPPLIED VALUE IN ITS PATH, and row 18 is why that
sentence had to be written.** The release was published as `DELETE /v1/crypto/rsa/keys/{keyRef}`, chosen
because the reference *names* the resource being removed and justified on the grounds that the reference is
never logged. **The justification was false, and not because any handler logged one: the request path is
recorded by the host.** ASP.NET Core's hosting diagnostics open a log scope carrying `RequestPath` for every
request, and Security renders scopes deliberately so a caller's `traceId` reaches an operator — so a sweep
of a running deployment found a freshly generated `keyRef` in **four** records of its own release window. A
request line also reaches a reverse proxy's access log, an ingress trace and a browser history, none of them
this service's to configure. A `keyRef` is a credential-like handle to a retained private key, so the
exposure is removed at source rather than redacted at one outlet: the reference is carried in a request body
like every other reference here, and the operation sits beside its generation counterpart as a `POST`. It is
a `POST` rather than a body-carrying `DELETE` because HTTP assigns a `DELETE` body no semantics, so an
intermediary may drop it — which would turn a release into a request naming nothing, answered `404`,
indistinguishable from "no such key". Being a `POST` does not make it replayable: no consumer's replay-safe
policy admits any Security path, and the method gate admits only the four safe methods.

**One consequence for the counts: all 18 operations are now `POST`, so the verb no longer identifies the
authored one — the `operationId` does.** Row 18 also gained a `400`: a reference that is a request member
can be omitted, and an omitted reference is a malformed request rather than a request naming nothing, so it
is refused distinctly from the `404` a reference resolving to nothing receives. It remains the one operation
answering no `200` and declaring no `500`.

All 18 require `bearerAuth`; none is anonymous. Two properties of the file operations are contract
rather than convenience: they take an **opaque server-resolved file reference**, never a
caller-supplied path — the same rule `keyRef` follows in [§5.2](#52-the-contract-level-secrets-rule),
for the same reason — and each has a **defined response for the case where the deployment resolves no
such reference**, which is the narrow-with-a-defined-error rule of
[§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess) rather than a silent success.

Counting the whole service rather than only C-02: `security.v1.yaml` publishes **23 operations** — the
18 above, C-01's three ([§4.1](#41-method-surface)), and the two of C-10, `GET /health` (anonymous)
and `GET /v1/ping` (`bearerAuth`, 401 without a token).

**THE RETAINED-KEY STORE IS BOUNDED IN THREE INDEPENDENT WAYS, AND ALL THREE ARE CONTRACT.** Retaining
private key material is what makes row 11's response safe, so how much of it is retained, by whom, and
for how long are properties a caller can rely on rather than implementation detail:

- **A total cap**, reserved **before** a key is generated. Checking capacity afterwards would let an
  authenticated caller drive unbounded RSA generation and have every pair discarded, which makes a full
  store an amplifier rather than a limit. Unbounded growth on the service that holds the system's only
  signing key would terminate authentication for Gateway, DataServices and Persistence at once.
- **A per-caller allowance**, charged to the `sub` claim of the presented token. A total cap alone lets
  one caller occupy the store and starve its peers using nothing but legitimate calls. A request with no
  subject claim is charged to one shared bucket rather than exempted — the unattributable caller must not
  be the one with no allowance.
- **A retention lifetime**, after which a reference stops resolving and its slot returns. Row 18 is how
  a caller frees a slot deliberately; the lifetime is the backstop for a caller that crashed.

A caller at either bound receives `500` from row 11 **with no key generated**. Which bound is binding is
deliberately not reported, because that would describe how much of the store other callers hold.

### 5.2 The contract-level secrets rule

> **Raw key material never crosses the wire from a caller. Callers pass an opaque *key reference* —
> a `keyRef` — that Security resolves against its configured key store.**

This is the central design decision of C-02 and it discharges C-F at the contract level rather than
by review. It is also a deliberate, documented **narrowing** of the legacy signature, and the reason
is visible in the legacy declarations themselves: every keyed operation takes the key as an ordinary
in-parameter — `readonly string key` or `readonly blob key` on the keyed hash and symmetric families
at `n_crypto.sru:L23-L26` and `:L30-L61`, and `readonly string prikey` on the RSA decrypt and sign
families at `:L66-L71`. In-process that is unremarkable. On a wire it would put key material into
request bodies, into any log that records them, and into every characterization recording.

Concretely:

- Every keyed operation takes a **`keyRef`** — an opaque, caller-meaningless handle — in the position
  the legacy took key bytes.
- Security resolves `keyRef` against its own configured key store, which is populated exclusively
  through configuration injection.
- **RSA key generation returns the public key and a `keyRef` for the private key.** The generated
  private key is retained by Security and never returned. Callers that need to sign call the sign
  operation; they do not obtain the key.
- An unresolvable `keyRef` returns a defined error that distinguishes "no such reference" from "not
  permitted for this caller", and in neither case echoes any part of the stored material.

### 5.3 The eight weak defaults are preserved and *annotated*, never corrected

The legacy cryptographic defaults are weak by modern standards. Correcting them would violate the
behaviour-preservation mandate (C-B), which forbids improvements as firmly as it forbids
regressions. The resolution is therefore to **preserve each as the default and annotate it in the
contract description as a known legacy weakness**, so a caller can see the risk without the
behaviour changing.

**Annotation is the whole remediation.** No default below is silently strengthened, and no "safe
mode" is added alongside it.

| # | Preserved legacy behaviour | Evidence | Annotation the contract description carries |
| --- | --- | --- | --- |
| 1 | **The default symmetric mode is ECB.** Overloads that omit the mode selector run in ECB | `Enums.CRYPTO_SYMCRYPT_MODE_DEFAULT` is declared equal to `CRYPTO_SYMCRYPT_MODE_ECB` at `ws_objects/pfw.shared.pbl.src/enums.sru:L946`; the mode-omitting overloads are `n_crypto.sru:L30`, `:L32`, `:L34`, `:L36` and their blob counterparts | ECB encrypts identical plaintext blocks to identical ciphertext blocks and preserves structure. Callers who need otherwise must pass the mode explicitly |
| 2 | **The default RSA padding is PKCS#1 v1.5** | The default is declared equal to `CRYPTO_RSA_PADDING_PKCS1` at `enums.sru:L949-L951` | PKCS#1 v1.5 remains the default and is not silently upgraded to OAEP |
| 3 | **No-padding is not selectable**, and the refusal is the legacy's own | Established by **absence**: only two padding values exist — `CRYPTO_RSA_PADDING_PKCS1` = 0 and `CRYPTO_RSA_PADDING_OAEP` = 1 at `enums.sru:L949-L950`. **There is no "none" value to select** | The enum has exactly two members. A request for no padding is refused with a defined error rather than honoured |
| 4 | **PKCS#5-family symmetric padding only, and not selectable** | Established by **absence**: there is **no symmetric padding constant of any kind** anywhere in `enums.sru`; the cipher block at `:L936-L946` exposes type and mode and nothing else, and not one of the 32 symmetric overloads at `n_crypto.sru:L30-L61` carries a padding parameter | The padding scheme is fixed. The contract carries no field for it, because offering one would imply a choice the legacy never had |
| 5 | **No key-derivation function is reachable at all**, and there is no salt concept — so a passphrase is used as raw key bytes | Established by **absence**: no PBKDF2, scrypt, bcrypt, Argon2 or salt constant exists in `enums.sru`, and no `n_crypto` signature accepts an iteration count or a salt (`n_crypto.sru:L30-L61`) | Whatever the caller supplies as key material *is* the key. The contract does not derive, stretch, or salt |
| 6 | **No authenticated encryption** — no GCM, CCM or Poly1305 — so ciphertext carries no integrity tag | Established by **absence**: the mode set is exactly ECB, CBC and CFB at `enums.sru:L943-L945` | Ciphertext integrity is not protected by this contract. A caller needing it must obtain it separately, e.g. through the keyed-hash operations |
| 7 | **1024-bit RSA remains a legal key size**, and the legacy demonstration uses it | `Enums.CRYPTO_RSA_BITS_1024 = 1024` is a declared, first-class value alongside 2048 and 4096 at `enums.sru:L965-L967` | 1024 is accepted. It is annotated as a legacy-compatibility value, and it is not removed from the accepted set |
| 8 | **The same hash set is declared for the RSA signature hash**, so `CRYPTO_HASH_MD5` = 0 is a legal signature-hash selector | The declaring comment at `enums.sru:L927` names the set's consumers as `Hash`, **`RSASign` and `VerifyRSASign`** — one set, three consumers. All four signature overloads take it as `readonly long ntype` [`n_crypto.sru:L70-L73`] | MD5 is accepted on the signature operations and is annotated rather than removed. `CRYPTO_HASH_CRC32` = 5 is the one declared member the **keyed and signing** operations do not accept, because no HMAC-over-checksum or RSA-over-checksum construction exists to implement — see the capability-narrowing row below. It remains accepted on the two **unkeyed** digest operations |

Items **3, 4, 5 and 6** are established by **absence** — there is no constant to select, and no
signature that accepts one. Absence is weaker evidence than presence in general, but here it is
exactly the right kind: a capability the legacy cannot express is a capability the port must not
offer, because offering it would be a new feature (C-B). Each of the four carries its mechanical proof
in the evidence column above, so none has to be taken on trust.

Item 8 is the one most easily missed, and it is a genuine weakness rather than a naming quirk: nothing
in the legacy restricts the signature hash to the cryptographically sound members of the set, so the
weakness is the *scope* of an otherwise ordinary constant set. It is preserved because narrowing the
accepted set would refuse input the legacy accepts.

**Separately from the eight — and not itself a weakness — the algorithm identifier sets are preserved
exactly**, values and spellings alike, because a rename would silently invalidate every stored
characterization comparison ([§14.2](#142-constant-identifiers-are-preserved-verbatim)). This is a
preservation *rule*, which is why it is stated here rather than counted as a ninth weakness.

The identifier sets that rule covers, verified value by value:

| Set | Identifiers and values | Locator |
| --- | --- | --- |
| Hash type | `CRYPTO_HASH_MD5` = 0, `CRYPTO_HASH_SHA1` = 1, `CRYPTO_HASH_SHA256` = 2, `CRYPTO_HASH_SHA384` = 3, `CRYPTO_HASH_SHA512` = 4, `CRYPTO_HASH_CRC32` = 5 | `enums.sru:L928-L933` |
| Symmetric cipher | `CRYPTO_SYMCRYPT_TYPE_DES` = 0, `..._3DES` = 1, `..._AES128` = 2, `..._AES192` = 3, `Enums.CRYPTO_SYMCRYPT_TYPE_AES256` = 4 | `enums.sru:L936-L940` |
| Symmetric mode | `CRYPTO_SYMCRYPT_MODE_ECB` = 0, `..._CBC` = 1, `..._CFB` = 2, `..._DEFAULT` = ECB | `enums.sru:L943-L946` |
| RSA padding | `CRYPTO_RSA_PADDING_PKCS1` = 0, `CRYPTO_RSA_PADDING_OAEP` = 1, `..._DEFAULT` = PKCS#1 | `enums.sru:L949-L951` |
| Encoding | `CRYPTO_ENCODING_BASE64` = 0, `CRYPTO_ENCODING_HEX` = 1 | `enums.sru:L924-L925` |
| Random-string classes | `CRYPTO_RNDSTRING_NUMBER` = 1, `..._ALPHABET` = 2, `..._SYMBOL` = 4, `..._DEFAULT` = number + alphabet | `enums.sru:L954-L957` |
| GUID formatting | `CRYPTO_GUID_INCLUDE_BRACKET` = 1, `..._INCLUDE_SEPARATOR` = 2, `..._DEFAULT` = both | `enums.sru:L960-L962` |
| RSA key size | `CRYPTO_RSA_BITS_1024` = 1024, `..._2048` = 2048, `..._4096` = 4096 | `enums.sru:L965-L967` |

#### C-02 capability narrowings — declared by the legacy, not reproducible here

Distinct from the weaknesses above, and the distinction matters. A **weakness** is behaviour the legacy
had that this port reproduces and annotates. A **narrowing** is behaviour the legacy *declared* but whose
parameters live only inside the closed binary: `n_crypto` is `native "pfw.dll"` [`n_crypto.sru:L8`] and its
86 lines are declarations only — there is no PowerScript body for any of its 63 operations, and a
Linux container cannot execute the binary. Where a parameter cannot be observed, the contract is
**narrowed with a defined error rather than widened with a guess**.

| # | Cell | Missing evidence | Published as |
| --- | --- | --- | --- |
| N1 | `hmac`, `hmacFile`, `rsaSign`, `rsaVerify` with `CRYPTO_HASH_CRC32` | None needed — the construction itself does not exist. A checksum has no compression function for HMAC to key and no algorithm identifier for a signature scheme to name, so HMAC-CRC32 and RSA-over-CRC32 were never defined | `CryptoKeyedHashType` — the same identifiers minus the checksum, on the **keyed** operations only. Refused at schema validation, and by the calling client at request construction. The unkeyed `hash` and `hash-file` operations keep the full `CryptoHashType`, CRC32 included |
| N2 | Any mode `CRYPTO_SYMCRYPT_MODE_CFB` | The **feedback width**. The legacy publishes one unqualified CFB value with no feedback-size argument, and full-block versus 8-bit feedback produce entirely different ciphertext | `x-blocked-cells` on `CryptoSymCryptMode`, reason `SYMMETRIC_FEEDBACK_WIDTH_UNPROVABLE`. HTTP **`500`** with a `ProblemDetails` body carrying `retCode` `E_NO_IMPLEMENTATION` (−2001) and that reason |
| N3 | `CRYPTO_SYMCRYPT_MODE_CBC` through one of the eight overloads that supply a mode but no vector [`n_crypto.sru:L31, L35, L39, L43, L47, L51, L55, L59`] | The **initialization vector** the binary substituted | `x-blocked-cells` on `CryptoSymCryptMode`, reason `SYMMETRIC_VECTOR_UNPROVABLE`. HTTP **`500`** with a `ProblemDetails` body carrying `retCode` `E_NO_IMPLEMENTATION` (−2001) and that reason |

**Why the refusal is `500` and not `501`, and why `501` is reserved.** It is not `400`: the request is
well-formed and would have succeeded, and the limitation is this port's rather than the caller's. It is
not `501` either, and that is a constraint rather than a preference. **`501` belongs exclusively to
Gateway's four deferred-capability routes** ([§13](#13-the-four-reserved-gateway-extension-points)),
whose whole purpose is to declare that an entire capability area is unbuilt. A `501` on a Security
operation would present this service the same way, when in fact 30 of the 32 symmetric cells are
implemented and answer normally — so C-D forbids the status here, Security declares it on no operation,
and the DataServices and Persistence contracts do not use it either. The legacy vocabulary carries the
distinction the status cannot: `E_NO_IMPLEMENTATION` names the missing cell without demoting the surface
hosting it. Each affected operation enumerates the `500` in its own `responses` block, so the status is
readable from the machine-readable half of the contract and not only from prose.

**Why refusing beats choosing.** Both symmetric guesses are undetectable by any test this repository can
run: encrypt and decrypt under the same wrong assumption and the plaintext returns intact. The caller
would receive ciphertext that passes every available check and **that the legacy cannot decrypt** — data
loss presented as success.

**What the narrowing costs: nothing the oracle demonstrates.** The only code in the repository that
invokes the symmetric surface is the framework's own demo, and all six of its call sites pass an explicit
vector with CBC — DES at `u_cst_tabpage_utility_crypto.sru:L504, L547`, AES256 at `:L592, L607`, 3DES at
`:L622, L637`. It never selects CFB and never uses a vector-less arm. Every cell the oracle exercises
remains fully supported.

**The identifier sets are unreduced.** All three modes and all six hash types remain declared, numbered
exactly as the oracle numbers them. Only the *capability* is withdrawn, on the cells named above.

#### C-02 platform divergence — weak and degenerate DES and 3DES keys

Not a narrowing and not reproducible either way: this platform's cryptographic library refuses the known
weak and semi-weak DES keys and refuses 3DES keys whose adjacent sub-keys coincide, raising from the key
assignment rather than encrypting. An OpenSSL-based implementation — which the framework's attribution
[`w_about.srw:L118`] suggests the binary used — does not. Because short key material is right-padded with
zero bytes, an **empty** DES key and **any 3DES key shorter than 16 bytes** normalize into buffers this
platform rejects, so a 3DES caller with a fifteen-character passphrase is refused here where the legacy
would have encrypted. No workaround is applied and no key is substituted; the failure is loud, surfaces
as HTTP `500`, and is published on the `CryptoSymCryptType` schema. It carries neither
`E_NO_IMPLEMENTATION` nor a `SYMMETRIC_*_UNPROVABLE` reason, which is how a caller tells it apart from
the two capability narrowings above that share its status: this one is a property of the **key material
supplied**, those are properties of the **port**.

### 5.4 The random generators are determinism seams

`GenRandomBlob`, `GenRandomString` and `GenGUID` at `n_crypto.sru:L14-L18` are the **primary
non-determinism sources** in the in-scope estate. Characterization compares a recorded legacy run
against a target run, and a value that differs on every execution makes the comparison meaningless
unless it is masked on **both** sides.

The contract therefore treats these three as **seamed**: the provider behind them is injected, so a
test substitutes a deterministic double while production uses the platform generator. The seam is a
property of the implementation, but it is recorded in the contract description because a consumer
writing a parity test needs to know which fields to mask. The full seam register is in
[`docs/PARITY.md`](PARITY.md).

**The requested length is capped, and the cap is a boundary requirement rather than a hardening
preference.** The legacy takes an unsigned 32-bit length, so its domain reaches 4 294 967 295. In
process that domain is harmless: the caller and the callee share one process and one fate, and a
caller asking for four gigabytes is asking to crash itself. Across a network boundary the same request
is a **denial-of-service primitive aimed at the sole token issuer** — a few concurrent calls of a few
bytes each can exhaust the process that every other service depends on for its tokens, and a random
*string* is the worst case because it materializes a character array and a same-length byte array
together. Three things are therefore true of this contract at once, and all three are needed:

- the request schema publishes a **`maximum`**, so the limit is discoverable rather than discovered;
- the legacy's own 32-bit maximum is published beside it as an **`x-legacy-domain-maximum`**
  annotation, so narrowing the domain does not erase the record of what the legacy accepted; and
- a request above the cap is refused with a **defined error before any allocation is attempted**,
  which is what separates a cap from a guard that merely converts an out-of-memory crash into a named
  one after the damage is done.

This is narrowing 4 of [§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess).

---

## 6. C-03 — `dataservices.v1.DataWindowService`

| | |
| --- | --- |
| **Identifier** | `dataservices.v1.DataWindowService` |
| **Transport** | gRPC — unary, server streaming, and one bidirectional stream |
| **Served by** | DataServices |
| **Consumed by** | Gateway (and, through Gateway's thin REST projection, external clients) |
| **Definition** | `Proto/dataservices.v1.proto` |
| **Primary legacy source** | `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru` (616 lines) |
| **Status** | NEW — the legacy is an in-process control extension dispatched by the PowerBuilder runtime |

### 6.1 Method surface, and the reason for each choice

**Sixteen methods, and this table is the complete list.** It is derived from the generated file
descriptor rather than written from memory, so the count and the streaming shapes are what
`Proto/dataservices.v1.proto` actually compiles to. Every method is authenticated; none is anonymous.

| # | Method | Kind | Request → response | Why this kind |
| --- | --- | --- | --- | --- |
| 1 | `Retrieve` | **server stream** | `RetrieveRequest` → `RetrieveChunk` | Reproduces progressive result delivery, and keeps a large result set off a single message |
| 2 | `OpenValidationSession` | unary | `OpenValidationSessionRequest` → `…Response` | Materializes the four pieces of cross-event mutable state as an explicit, correlated session ([§6.4](#64-four-pieces-of-cross-event-mutable-state-and-why-the-session-exists)) |
| 3 | `CloseValidationSession` | unary | `CloseValidationSessionRequest` → `…Response` | Releases that session explicitly, mirroring the legacy's instance-scoped state |
| 4 | `EventChain` | **bidirectional stream** | `EventChainRequest` → `EventChainResponse` | Carries all 13 raw and all 9 semantic events with sequencing tokens, in one ordered conversation ([§6.6](#66-ordering-pattern-assigned-per-capability-area)) |
| 5 | `Update` | unary | `UpdateRequest` → `UpdateResponse` | Returns **`Aborted`** on an optimistic-concurrency conflict, carrying the conflict detail; it is the one method on this contract that declares the rich-error binding ([§9.8](#98-on-concurrency-mismatch-aborted-and-no-silent-overwrite)) |
| 6 | `GetEventGate` | unary | `GetEventGateRequest` → `…Response` | Reads the event bitmask, **including the coupling by which disabling item-change also suppresses column-expression evaluation** ([§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling)) |
| 7 | `DisableEvent` | unary | `DisableEventRequest` → `…Response` | Sets bits in that mask |
| 8 | `EnableEvent` | unary | `EnableEventRequest` → `…Response` | Clears bits in that mask |
| 9 | `GetDropDownSearchState` | unary | `GetDropDownSearchStateRequest` → `…Response` | Reads the headless drop-down-search model — filter expression, raw search values, counts and search state ([§6.9](#69-the-four-headless-models-and-their-reachable-operations)) |
| 10 | `ApplyDropDownSearch` | unary | `ApplyDropDownSearchRequest` → `…Response` | Applies a search to that model |
| 11 | `GetColumnSortState` | unary | `GetColumnSortStateRequest` → `…Response` | Reads the headless sort model — sort expression and per-column sort state |
| 12 | `ApplyColumnSort` | unary | `ApplyColumnSortRequest` → `…Response` | Applies a sort to that model |
| 13 | `GetContextMenuModel` | unary | `GetContextMenuModelRequest` → `…Response` | Reads the headless menu item model — labels, ids, enabled and split flags, computed logical widths |
| 14 | `ApplyContextMenuModel` | unary | `ApplyContextMenuModelRequest` → `…Response` | Applies a menu configuration, including the five built-in toggles whose legacy default is *true* |
| 15 | `GetRowSelectState` | unary | `GetRowSelectStateRequest` → `…Response` | Reads the row-selection state machine — style, selected rows, current row and the two in-flight flags |
| 16 | `ApplyRowSelectStyle` | unary | `ApplyRowSelectStyleRequest` → `…Response` | Sets the selection style, rejecting style 0 exactly as the legacy does |

Methods 9 through 16 are the reachable half of the four headless models. They exist because a wire
type that no method mentions is not an API: the models were declared before these operations were
added and were consequently unreachable, which is the specific defect [§6.9](#69-the-four-headless-models-and-their-reachable-operations)
records and closes.

`Update` on this contract is the DataWindow-facing projection of
[C-06](#9-c-06--persistencev1updateservice); the concurrency semantics, the conflict payload and the
identity round-trip are specified there and are not duplicated here.

### 6.2 The event gate, and its one non-obvious coupling

Three composable bits are declared at `se_cst_dw.sru:L41-L43`:

| Identifier | Value | Suppresses |
| --- | --- | --- |
| `EID_ROWFOCUSCHANGE` | 1 | Row-focus changing and changed |
| `EID_ITEMFOCUSCHANGE` | 2 | Item-focus changed |
| `EID_ITEMCHANGE` | 4 | Item change — **and, per the inline comment at `:L43`, column-expression calculation as well** |

The gate is a bitmask, tested with a bit test in each handler's first line — at `:L124` and `:L130`
for row focus, `:L176` for item focus, `:L187` for item change. Disabling is a bit-or and enabling
is a bit-clear, and a zero argument is rejected with `RetCode.E_INVALID_ARGUMENT` [`:L506-L511`,
`:L530-L535`]; the query accessor is a bare bit test [`:L486`].

**The coupling must be carried in the contract description, not left as an implementation detail.**
A consumer that disables item change to suppress a UI notification will also silently stop every
bound column expression from recalculating. That is legacy behaviour and it is preserved; what is
added is that the contract *says so*, so the consequence is discoverable before it is observed. The
mechanism is visible at `:L313-L315`, where the column-expression service's changed handler is
invoked from inside the item-changed path — a path the gate short-circuits.

**The two mutators return DIFFERENT types, deliberately, and a reviewer has already read that as a defect
once.** The oracle declares `public function long of_disableevent` [`:L110`] against
`public function integer of_enableevent` [`:L111`] — two widths, for mirror-image bodies. It is almost
certainly an oversight in the original, and C-B forbids repairing an oversight: under AAP §0.4.5.2
PowerScript `long` maps to `long` and `integer` to `int`, so `DisableEvent` returns `long` and
`EnableEvent` returns `int` in every layer that exposes them — the gate, the validation session and the
event chain alike. The suggestion to align them on one type "matching the legacy return-code width"
cannot be acted on even in principle, because **the legacy has two widths and therefore names no single
one**. It is observationally benign, and saying so is part of preserving it honestly: both members return
only `RetCode.OK` or `RetCode.E_INVALID_ARGUMENT`, and both values fit either width without truncation, so
what is preserved is the *contract's* fidelity rather than a value's. `EventGateTests` holds it with three
assertions, one of which asserts the two types are **not equal** — so the obvious tidy-up fails the build
instead of passing unnoticed. [§17.2](#172-corrections-applied-during-verification) row 14 records the
same finding from the corrections side.

### 6.3 The 22-event surface, enumerated

`se_cst_dw.sru` declares exactly 22 events at `:L11-L32`. The split the brief cites is real and it
is precisely 13 raw against 9 semantic.

**Nine semantic events** — the framework's own vocabulary:

| Event | Signature | Locator | Note |
| --- | --- | --- | --- |
| `oninitcontextmenu` | `(long row, dwobject dwo) → long` | `:L11` | |
| `oncontextmenu` | `(long row, dwobject dwo, long mid) → long` | `:L12` | |
| `onddsgetfilter` | `(long row, dwobject dwo, string data, ref string filter)` | `:L13` | **`ref` out-parameter** — the result is produced by mutating a reference, not returned |
| `oncolumnexpinvokemethod` | `(long row, dwobject dwo, string name, string args[]) → any` | `:L14` | **`any` return over a string array** — untyped in both directions |
| `ondoitemchange` | `(long row, dwobject dwo, string data) → long` | `:L24` | Returns the four-value alphabet of [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
| `onitemchanged` | `(long row, dwobject dwo)` | `:L25` | |
| `ondoitemchanged` | `(long row, dwobject dwo)` | `:L26` | |
| `onddsfiltered` | `(long row, dwobject dwo, long rowcount, long filteredcount)` | `:L28` | |
| `oncolumnexptrace` | `(long row, dwobject dwo, string stack, string expr, string value)` | `:L32` | Diagnostics; also surfaced on [C-04](#7-c-04--dataservicesv1columnexpressionservice) |

**Thirteen raw `pbm_dwn*` events**, in declaration order — these are the runtime's own messages,
mapped one for one:

| # | Event | Locator | # | Event | Locator |
| --- | --- | --- | --- | --- | --- |
| 1 | `ondwnrbuttondown` | `:L15` | 8 | `ondwnitemchangefocus` | `:L22` |
| 2 | `ondwnrbuttonup` | `:L16` | 9 | `ondwnitemchange` | `:L23` |
| 3 | `ondwnrowchange` | `:L17` | 10 | `ondwnitemvalidationerror` | `:L27` |
| 4 | `ondwnrowchanging` | `:L18` | 11 | `ondwnkillfocus` | `:L29` |
| 5 | `ondwnlbuttondblclk` | `:L19` | 12 | `ondwnlbuttonup` | `:L30` |
| 6 | `ondwnlbuttonclk` | `:L20` | 13 | `ondwnsetfocus` | `:L31` |
| 7 | `ondwnchanging` | `:L21` | | | |

**Each raw event delegates to a semantic one and then to the broker**, and both delegations are
vetoable. `ondwnrbuttondown` returns 1 if the semantic handler returns 1 and otherwise consults the
broker [`:L115-L117`]; `ondwnrbuttonup` consults only the broker [`:L120-L121`]. Both edges must be
representable on the wire, because a consumer can veto at either.

The contract therefore carries all 22 as distinct message types on the `EventChain` stream, tagged
by event identity, and does **not** collapse a raw event into the semantic one it delegates to.
Collapsing them would erase the two-stage veto.

### 6.4 Four pieces of cross-event mutable state, and why the session exists

`se_cst_dw.sru:L89-L96` declares four private fields that the event chain reads and writes *between*
events:

| Field | Locator | Role |
| --- | --- | --- |
| `_nDisabledEvent` | `:L89` | The event-gate bitmask of [§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling) |
| `_bDoItemChange` | `:L92` | Records that execution is currently inside the item-change handler |
| `_bDwnItemValidationError` | `:L94` | Records that execution is currently inside the validation-error handler |
| `_nItemChangeRetCode` | `:L96` | **The item-changed return value, stashed for the validation-error event to consume** |

The fourth is the consequential one. The validation-error handler does not merely read it — it reads
*and clears* it [`:L331-L332`] and then pre-sets its own result when the stashed value was 1 or 3
[`:L338-L340`]. One event's behaviour is a function of the previous event's return value.

A stateless request boundary has nowhere to put these four fields. They therefore become explicit
fields of a **server-held `ValidationSession`**, correlated by an identifier, opened by
`OpenValidationSession` and closed by `CloseValidationSession`. Every message on the `EventChain`
stream carries that identifier.

Two obligations follow and belong in the contract description:

- **A session is required for the item-change group.** Sending an item-change event without one is a
  defined error, not an implicit session creation — an implicit session would silently give each event
  its own state and make the stash unreadable.
- **Sessions are explicitly closed, and closure is idempotent.** The legacy state dies with the
  control; a server-held session needs a defined end, and `CloseValidationSession` returns the same
  result whether the session was open or already gone.

### 6.5 The item-change alphabet is its own enumeration

`se_cst_dw.sru:L182-L253` is the most intricate behaviour in the in-scope set, and its return
alphabet is **not** the return-code algebra. The values are `{0, 1, 2, 3}`, and the contract models
them as a distinct wire enumeration with those numeric values preserved.

Modelling them onto `RetCode` would be a category error with a concrete failure: in the return-code
algebra 1 is `PREVENT` and −1 is `FAILED`, whereas here 1 means "reject the edit and hold focus" and
2 means "the buffer has already been written, do not re-apply the edit text". The two alphabets
share digits and share nothing else.

The observable behaviour the enumeration must support, in source order:

1. **Early-out** on the item-change gate bit [`:L187`].
2. **Snapshot** both the original value and the row's item status [`:L189-L190`].
3. Set the re-entrancy flag, fire the semantic handler, **stash its result** [`:L195`], restore the flag
   [`:L192-L196`].
4. **Equality test with an explicit null-and-null arm** [`:L198-L202`] — two nulls compare equal here,
   which a naive equality would not reproduce.
5. If unequal, re-read the value and fire the **nested** changing event [`:L204-L208`].
6. Dispatch on the stashed result [`:L211-L251`]. **Four arms, and every one of them is distinct,
   because PowerScript `choose case` does not fall through the way a C `switch` does** — a `case`
   label with no statements simply runs nothing:
   - **case 1 is an EMPTY arm** [`:L212`]. Nothing executes, so 1 is returned exactly as the semantic
     handler produced it, and value and status are left **untouched**. That is deliberate rather than
     an omission: the source's own comment immediately above the dispatch reads
     `//*return 1触发OnDwnItemValidationError` [`:L210`] — returning 1 is how the DataWindow is made to
     raise `ItemValidationError`, and it is *that* handler which restores value and status
     [`:L369-L379`]. Restoring here as well would restore twice and would do it before the
     validation-error handler has read the stashed code.
   - **case 2 restores the value and status** [`:L217-L218`], but **only if the earlier equality test
     held** [`:L216`], because the buffer may already have been changed and must not be overwritten
     (the two `Describe` calls that once gated it are commented out at [`:L217-L218`], so the guard is
     now the equality test alone). It returns 2.
   - **case 3 keeps the value, does not move focus, and rewrites the result to 1** [`:L223-L225`].
   - **the default arm** performs manual type-directed coercion by the **first five characters** of the
     column type, across the fixed set `char` / `char(` / `decim` / `real` / `numbe` / `long` /
     `ulong` / `datet` / `date` / `time` [`:L231-L244`], fires the changed event [`:L247`], and then
     **forcibly returns 2** [`:L250`] so the runtime will not re-apply the edit text over the buffer.
     Note it also catches **any** value outside `{1,2,3}`, not only 0.

So: 3 collapses into 1, the default arm rewrites to 2, and 1 passes through untouched. All three
outcomes are observable and all three are preserved. **An implementation that treated case 1 as case
2 would restore value and status where the legacy leaves them alone** — the precise failure this
contract exists to prevent, and the reason the four arms are spelled out one by one rather than
grouped.

> **The case-1 question, settled from the source.** It is tempting to describe the empty `case 1` arm as
> "falling through" to `case 2`, and earlier drafts of this document did. **That is a C-family reading,
> and PowerScript `CHOOSE CASE` does not fall through the way a C `switch` does.** The empty arm is a
> deliberate no-op whose purpose is to stop 1 from reaching `case else`, so a result of 1 returns
> unchanged with **no restore and no coercion**.
>
> The distinction is **observable** — whether a restore happens before the validation-error event fires —
> so it was settled rather than left open, and settled from the dispatch text itself. Two facts decide
> it. The source comment at [`:L210`] states the intent: returning 1 is what triggers
> `OnDwnItemValidationError`, and it is *that* handler which restores value and status [`:L369-L379`]
> after reading the stashed code, so restoring in `case 1` would restore twice and would do it before
> the stash has been read. And the empty arm is load-bearing **while empty**: delete it and 1 reaches
> `case else`, is coerced, fires the changed event and is rewritten to 2.
>
> `dataservices.v1.proto`'s `ItemChangeResult` carries the same reading with the verbatim dispatch text
> and its locators, and [`PARITY.md`](PARITY.md) §7 teaches it. Two consequences remain worth stating.
> **An implementation must not merge or alias 1 and 2** — they are distinct on the wire and the
> distinction is exactly the restore. And the wire alphabet is `{0,1,2,3}` with all four values carried
> separately, which is what makes the settled reading assertable rather than merely documented.

Two adjacent orderings are also contract, and both are ordering guarantees the stream must honour:

- **`ondoitemchanged`** [`:L295-L320`] fires in strict sequence: the column-expression service's
  changed handler **if that service is enabled** [`:L313-L315`], then the broker trigger **if the topic
  has a subscriber** [`:L316-L318`], then the semantic changed event [`:L319`]. Both conditions are
  part of the sequence, not incidental steps within it — a consumer observing only two of the three
  cannot conclude the third was skipped in error.
- **`ondwnitemvalidationerror`** [`:L322-L385`] guards re-entrancy by returning 1 immediately
  [`:L327`], consumes and clears the stashed code [`:L331-L332`], snapshots value and status again
  [`:L334-L335`], pre-sets its result when the stashed code was 1 or 3 [`:L338-L340`], otherwise fires
  the error event with a null-to-zero coercion [`:L342-L345`]; then on a zero result **with non-empty
  data** it reads the column's validation message [`:L350`], **strips the outer two characters**
  [`:L351-L353`], falls back to a localized string when the message is empty or a single question mark
  [`:L354-L356`], surfaces a localized error [`:L357`] and sets its result to 1 [`:L358`] — while on
  **empty** data it clears the guard and **returns 3** [`:L361-L362`]. Finally it restores value and
  status for results 1 and 3 **only when the row still exists and the stashed code was not 3**
  [`:L369-L379`]. That row-existence guard is defensive because the dialog's own queued message may
  have deleted the row [`:L368`], and it must survive.

The two localized strings on that path are the only ones in this object that route through the
localization category `Categories.CAT_DWSVC` [`:L355`, `:L357`] — a contrast that matters when
reading [§7.8](#78-parse-errors-carry-a-caret-position-and-bypass-localization).

One further detail is carried as-is: `ondoitemchange` [`:L256-L293`] contains a **commented-out**
byte-length check with its own dialog [`:L280-L290`]. It is a dormant validation path. It is carried
across as commented and inert; it is **not** revived, because reviving it would add a validation the
legacy does not perform (C-B).

### 6.6 Ordering pattern assigned per capability area

The brief requires choosing, per capability area, between **(a)** a sequencing token on every event
so consumers can detect and reorder out-of-order delivery, and **(b)** a synchronous
request/response chain in which no reordering is permitted. The assignment, with the evidence for
each:

| Capability area | Events | Pattern | Why |
| --- | --- | --- | --- |
| **Item-change and validation chain** | `ondwnitemchange` → `ondoitemchange` → `onitemchanged` / `ondoitemchanged` → `ondwnitemvalidationerror` → `ondwnkillfocus` | **(b) strictly synchronous, no reordering** | Reordering is not merely undesirable, it is **semantically impossible**. The validation-error handler reads *and clears* the code stashed by the preceding item-change event and pre-sets its own result when that code was 1 or 3 [`se_cst_dw.sru:L331-L332`, `:L338-L340`], so its behaviour is a function of the prior event's return value. The item-change handler also fires a **nested** event from inside itself [`:L182-L253`, nested call at `:L207`], and the kill-focus handler queues a deferred accept **only when the item-change flag is clear** [`:L387-L393`] |
| **Focus, mouse and row-focus notification** | `ondwnsetfocus`, `ondwnkillfocus` *as pure notification*, `ondwnrowchange`, `ondwnrowchanging`, `ondwnlbuttonclk`, `ondwnlbuttondblclk`, `ondwnlbuttonup`, `ondwnrbuttondown`, `ondwnrbuttonup` | **(a) sequencing token** | These are notifications whose only return contract is the prevent convention. They carry no cross-event state, so a monotonic token is sufficient for a consumer to detect and reorder |
| **Context menu** | `oninitcontextmenu` → `oncontextmenu` | **(b) synchronous** | Initialization must complete before the menu identifier passed to the second event can be meaningful [`:L11-L12`] |
| **Drop-down search** | `onddsgetfilter` → `onddsfiltered` | **(b) synchronous** | The first event produces its result through a **`ref string` out-parameter** [`:L13`], which has no asynchronous representation — the caller blocks on the produced filter |
| **Macro invocation** | `oncolumnexpinvokemethod` | **(b) synchronous** | The calculation cannot proceed without the returned value [`:L14`]; see [§7.6](#76-two-inverted-streams-structurally-required) |
| **Expression trace** | `oncolumnexptrace` | **(a) sequencing token** | Pure diagnostics, fire-and-forget [`:L32`] |

**The operational rule for pattern (b), stated so it cannot be misread.** The entire group runs
inside **one validation session on one bidirectional stream**, and the monotonic sequence numbers on
those messages exist **for detection only**. An out-of-order arrival within a pattern-(b) group is a
**hard error** that fails the session — never a reorder opportunity. A consumer that buffered and
re-sorted them would reconstruct an order the server never sent and produce a result the legacy
could not.

**The operational rule for pattern (a) is the opposite one, and that is why it needs stating too.**
Under (a) the same monotonic token is *reorder authority*: a gap or a reversal is a delivery artifact
a consumer MAY buffer on and re-sort, because these events carry no cross-event state and no handler
reads what a predecessor stashed. So the two patterns give the identical field two contradictory
meanings — reorder on it under (a), fail the session on it under (b) — and a consumer cannot infer
which applies from the token's shape.

**On `EventChain` the consumer holding that authority is DataServices itself, and what it does with
it is stated here rather than left to a client's guess.** It *enforces* the order rather than
repairing it. The delivered behaviour:

| Arriving pattern-(a) token | What the service does |
| --- | --- |
| **Above** the ordering mark, successor or not | Dispatched. A **gap is legitimate**: the sequence space is shared with the outbound direction, so the numbers the server consumed are numbers the client never sends |
| **At or below** the ordering mark | Refused `FAILED_PRECONDITION` — a reversal or a duplicate. The events after that position have already been dispatched, so there is nowhere left to place it. Before this rule both were dispatched silently |
| Absent (`0`) | Refused, on every discipline |

**Why it does not buffer and re-sort, given that the pattern grants the authority to.** A
hold-and-release buffer was built for this and then removed after measurement, and the measurement is
the reason: **one dispatch of token 1 leaves the next expected token at 4**, because the chain's
outcome report and the result write each take one from the same shared counter. A message held
awaiting token 2 would wait for a number no client will ever send, so every hold would strand. It
follows too that a client cannot know its *second* token without reading the *first* response — so it
cannot pipeline — and one gRPC stream delivers a sender's messages in the order it wrote them.
A displaced pattern-(a) arrival is therefore **a sender defect rather than a transport artifact**, and
the predecessor a buffer would wait for does not exist. Narrowing the contract with a defined error is
the honest answer and is what AAP §0.1.5 prescribes. Sound reordering would require contiguous inbound
tokens, i.e. a separate sequence space per direction, which changes the published meaning of the token
("strictly increasing within one stream") and is deliberately not done here.

`DataServices:EventChain:StrictOrdering` governs whether an ordering violation fails the stream or is
recorded while the message is processed in arrival order. **Neither mode ever reorders or buffers.**

**"It cannot pipeline" above is a statement about a CONFORMING client, and the server does not rely on
it.** A notification is accepted on the request-stream loop in constant time and handed to a single
ordered consumer — the handover exists because nine of the 22 events are questions the chain asks back
and blocks on, and only that loop can read the answer — so a client that simply declines the discipline
and writes without reading could enqueue without limit while one dispatch waited out
`DataServices:EventChain:AnswerTimeout`. `DataServices:EventChain:MaxPendingNotifications` is the
server's own bound on that: it counts the notifications queued-or-in-flight and refuses a further one
with **`ResourceExhausted`**, naming the ceiling and the setting. It defaults to 64 against the ONE
outstanding notification the discipline itself admits, and its smallest legal value is 2 — one slot for
the conversation and one for the instant between a result being written, at which point the client may
legitimately send the next, and that slot being released.

Two properties of that refusal are contractual rather than incidental. **A notification is never
dropped to stay under the ceiling**: a skipped event would leave the chain's four cross-event fields
describing an event that did not run, so the call ends instead and nothing is half-applied. And **the
queue itself stays unbounded**: the ceiling is counted rather than imposed by a bounded queue, because a
full queue would stall the read loop, and the read loop is the only thing that can deliver the answer
the consumer is waiting for — which is the deadlock the handover exists to break.

**Which is why the discipline travels as data rather than as prose.** The assignment in the table
above is published on the wire as `dataservices.v1.OrderingDiscipline`, whose three members are
`ORDERING_DISCIPLINE_UNSPECIFIED` (0, a fault on any stream carrying the type),
`ORDERING_DISCIPLINE_SYNCHRONOUS` (1, pattern (b)) and `ORDERING_DISCIPLINE_SEQUENCED` (2, pattern
(a)). It is carried as `EventStreamRequest.discipline` and `EventStreamResponse.discipline` on C-04's
sequenced stream, and C-09's REST projection of that stream both marks the schema with
`x-ordering-discipline` and declares `discipline` a **required** response member. This table remains
the authority for the assignment; the enum is how a running consumer reads that assignment without
having read this document.

Conformance tests are written per workflow against this table; the workflow corpus and the recording
model are the subject of [`PARITY.md`](PARITY.md).

### 6.7 The three-encoding topic string, and why naive serialization fails

`se_cst_dw.sru:L47-L76` declares exactly **twelve** broker topics. Two of them carry an **ordering
prefix baked into the topic string itself**:

| Topic constant | Spelling | Locator |
| --- | --- | --- |
| `EVT_ROWFOCUSCHANGING` | `rowfocuschanging` | `:L47` |
| `EVT_ROWFOCUSCHANGED` | `rowfocuschanged` | `:L49` |
| `EVT_ITEMFOCUSCHANGED` | `itemfocuschanged` | `:L52` |
| **`EVT_ITEMCHANGED`** | **`0-itemchanged`** | `:L54` |
| **`EVT_EDITCHANGED`** | **`1-editchanged`** | `:L57` |
| `EVT_CLICKED` | `clicked` | `:L60` |
| `EVT_DOUBLECLICKED` | `doubleclicked` | `:L63` |
| `EVT_LBUTTONUP` | `lbuttonup` | `:L66` |
| `EVT_RBUTTONDOWN` | `rbuttondown` | `:L69` |
| `EVT_RBUTTONUP` | `rbuttonup` | `:L72` |
| `EVT_GETFOCUS` | `getfocus` | `:L74` |
| `EVT_LOSEFOCUS` | `losefocus` | `:L76` |

**The leading digits are load-bearing, and this was verified in the broker rather than inferred.**
The broker keeps its subscription registry in **ascending lexical order of the subscription name**:
the insertion scan at `ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:L407-L422` places a
new subscription before the first existing entry whose name compares greater, breaking ties by
priority, and maintains first/last name bounds at `:L439-L440`. Because ASCII digits sort before
letters, `0-itemchanged` and `1-editchanged` occupy a deterministic position relative to each other
and to every unprefixed topic. **The dispatch order is therefore a function of the string's
spelling.**

Separately, the same string carries a **lifetime namespace suffix**: `.^persistent` appears in the
subscription and unsubscription paths at `ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L544`
and `:L596`.

And the broker's own parser shows the string is even denser than those two encodings suggest. At
`n_cst_eventful.sru:L339-L381` it consumes, in order: a leading `-` meaning *prepend within equal
priority*, `%` and `*` selecting which handled-state of an event to capture, `@` and `!` selecting
lowest and highest dispatch priority, then an optional numeric priority terminated by `:`, then the
logical name, then an optional `.`-delimited namespace. The symbol constants are declared at
`:L91-L98` and the priority and capture constants at `:L100-L106`.

So one opaque string fuses **ordering, logical identity and lifetime** — and, in the general case,
capture mode and explicit priority as well. That is the concrete mechanism behind the warning that
this event model does not survive naive serialization:

- **Transmit the string as-is** and the ordering becomes invisible to the consumer. It cannot tell that
  `0-itemchanged` is dispatched before `1-editchanged` for a reason.
- **Parse the string at the far end** and the contract has acquired an undocumented grammar that every
  consumer must reimplement identically or diverge.

> **The wire contract carries every one of those encodings as its own field** — `sequence` (integer),
> `name` (string), `lifetime` (enum), the `namespace` (a string **with explicit presence**, because an
> absent namespace and an empty one are different), the capture mode, the explicit priority, the
> prepend flag, and the two negation flags below — and reconstitutes the fused legacy spelling **only
> at the compatibility edge**, the one place that must hand a string to legacy-shaped code. The
> reconstituted spelling is also carried as its own `legacy_name` field so that a recording made
> against the legacy can be compared without a consumer having to re-derive it.

**One field pair exists because the negation semantics are the reverse of what they look like.** The
subscribe grammar and the *filter* grammar are different grammars, and the filter one supports
negation: `n_cst_eventful.sru:L992-L1014` parses a filter, and the matcher at `:L1029-L1042` reads
the negation flags. So the filter `.^persistent` — a filter with **no name** and a **negated
namespace** — matches every name whose namespace is **not** `persistent`. Applied to an unsubscribe
sweep it therefore **removes the non-persistent subscriptions and SPARES the persistent ones**, which
is the opposite of what "persistent namespace" reads like at a glance. The contract carries
`negate_name` and `negate_namespace` as separate booleans, and the grammar the topic is expressed in
as its own enumeration, so a consumer never has to infer either from the string. A wire model that
recorded only "the persistent namespace" would invert this behaviour while looking correct.

### 6.8 The veto is tri-valued, never boolean

The broker's veto has **three** states, declared at `n_cst_eventful.sru:L111-L112` and set at
`:L1295` and `:L1297`:

| State | Value | Meaning |
| --- | --- | --- |
| `PREVENT_ONCE` | 1 | Stop this dispatch |
| `PREVENT_DEEP` | 2 | Stop the entire dispatch chain, to its full depth |
| *continue* | — | Neither is set |

The distinction is honoured at dispatch time [`:L956`], and the prevention entry point takes a
`deep` flag to select between them [`:L1276-L1300`].

**Flattening this to a boolean would silently convert a deep prevention into a shallow one** — the
chain would resume after the vetoing handler, and the events the caller intended to stop would fire.
The contract therefore carries the tri-valued result as an enumeration with those numeric values.

One trap for the reader of the legacy source: the inline comments beside the topic constants read
`0:continue,1:prevent` [`:L46`, `:L51`, `:L56`, and similarly at `:L59`, `:L62`, `:L65`, `:L68`,
`:L71` of `se_cst_dw.sru`], which suggests a binary convention. **Those comments are misleading; the
broker's tri-valued behaviour is authoritative.** The comments describe what a handler on those
particular topics is expected to return, not what the broker is capable of carrying.

### 6.9 The four headless models, and their reachable operations

Three of the five legacy DataWindow services are irreducibly presentational in part, and one — row
selection — is almost entirely headless. Each ships as a **headless half** whose data crosses this
contract, with the rendering half named as a reserved Gateway extension point
([§13](#13-the-four-reserved-gateway-extension-points)). Four properties of that split are contract:

| Model | What crosses this contract | What is deferred | Legacy anchor |
| --- | --- | --- | --- |
| `DropDownSearchState` | The constructed filter expression, the **raw search values carried separately and typed**, the row and filtered counts, and the search state machine | Window positioning [`ShowWindow`, `GetWindowRect`, `OffsetRect`, `SetWindowPos`], input-method text entry | `n_cst_dwsvc_dropdownsearch.sru` |
| `ColumnSortState` | The sort expression and the per-column sort state | Sort-indicator geometry via DPI conversion [`:L359`, `:L361`] | `n_cst_dwsvc_columnsort.sru` |
| `ContextMenuModel` | The complete item model — labels, ids, enabled and split flags, computed **logical** text widths | DPI-to-pixel conversion, font measurement, menu rendering | `n_cst_dwsvc_contextmenu.sru` |
| `RowSelectState` | Style, selected rows, current row, and the two in-flight flags | Nothing but its single dialog, which becomes a structured error [`:L239`] | `n_cst_dwsvc_rowselect.sru` |

**A wire type no method mentions is not an API.** All four models were declared before any operation
referenced them, so none was reachable and none could be called; three defaults would additionally
have been silently wrong. Both problems are closed, and the closure has two parts:

- **Eight reachable operations**, methods 9 through 16 of
  [§6.1](#61-method-surface-and-the-reason-for-each-choice) — a read and an apply for each model.
- **Presence, not zero, for every field whose legacy default is non-zero.** Protobuf 3 gives an
  absent scalar the zero value, which would have turned the drop-down filter type from its legacy
  default of **3** into 0, and the five built-in `ContextMenu` toggles from **true** into false. Those
  fields, and the row-selection style, are declared with explicit presence so that "not supplied" and
  "supplied as zero" are distinguishable and the legacy default applies to the former. The row-select
  style additionally reproduces the legacy rejection of style 0 with `E_INVALID_ARGUMENT`
  [`n_cst_dwsvc_rowselect.sru:L272`], and its two values are composable bits, `RS_SINGLE` = 1 and
  `RS_MULTIPLE` = 2 [`:L20-L21`], with `RS_SINGLE` as the `#Style` default [`:L23`].

One field on `DropDownSearchState` carries an explicit prohibition. **The filter expression is
compatibility and diagnostic data, and a server must never execute it.** The legacy builds it by
interpolating the search value into an expression string unescaped
[`n_cst_dwsvc_dropdownsearch.sru:L323`], and an unbalanced quote in that value merely breaks the
expression while a *balanced* one extends it into a well-formed expression that means something the
caller did not ask for. The raw values therefore travel separately and typed, and evaluation binds
them; the expression string exists so a recording can be compared and a trace can be read. The same
rule and the same reasoning apply to the value-to-literal rendering the validators produce
([§14.5](#145-legacy-defects-are-reproduced-and-annotated-at-the-contract-level)).

---

## 7. C-04 — `dataservices.v1.ColumnExpressionService`

| | |
| --- | --- |
| **Identifier** | `dataservices.v1.ColumnExpressionService` |
| **Transport** | gRPC — unary, plus **two inverted streams** |
| **Served by** | DataServices |
| **Consumed by** | Gateway |
| **Definition** | `Proto/dataservices.v1.proto` |
| **Authoritative specification** | `docs/n_cst_dwsvc_columnexp.md` (166 lines, read-only, Chinese) |
| **Implementation source** | `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru` (2,435 lines — the largest in-scope object) |
| **Status** | NEW |

**Why this is a separate contract from C-03.** The expansion engine is kept apart from the
DataWindow event surface **so that it can version independently**. It is the single most intricate
piece of the in-scope estate, it is the one most likely to need contract revision, and it has its
own authoritative legacy specification. Folding it into C-03 would tie every future revision of the
expression grammar to a revision of the event chain.

### 7.1 What the service is for

It binds expressions to ordinary DataWindow columns, replacing the traditional practice of computing
values inside an item-changed handler. The legacy specification's own feature list
[`docs/n_cst_dwsvc_columnexp.md` L5-L11] names seven capabilities, all of which the contract must
carry: binding expressions to plain columns; macro variables and macro function calls; control over
what triggers a recalculation, including whether the trigger must be manual user input, and whether
recursion is permitted; **multiple expressions per column, selected by which column changed**;
**referencing another DataWindow's expressions in a mixed calculation**; emitting the calculation
call stack; and modifying expressions at run time.

**All twenty-six RPCs the service declares**, in definition order. Locators are into
`n_cst_dwsvc_columnexp.sru` unless stated otherwise. Where one RPC covers several legacy overloads
the overload count is given, because that collapse is the contract's main design decision here and
the per-arity detail behind it is [§7.9](#79-the-contract-covers-the-source-surface-not-the-documented-nine):

| # | RPC | Request → Response | Kind | Legacy anchor and what the contract must preserve |
| ---: | --- | --- | --- | --- |
| 1 | `OpenExpressionSession` | `OpenExpressionSessionRequest` → `OpenExpressionSessionResponse` | unary | **The session exists for exactly one reason**: to scope the DataWindow handles that replace `foreignvardata.expsvc`. See [§7.7](#77-the-one-hard-limit--cross-session-foreign-variables) |
| 2 | `CloseExpressionSession` | `CloseExpressionSessionRequest` → `CloseExpressionSessionResponse` | unary | Ends that scope; foreign references cannot outlive it |
| 3 | `AddExpression` | `AddExpressionRequest` → `AddExpressionResponse` | unary | `of_addexp` [`:L172`] |
| 4 | `SetExpression` | `SetExpressionRequest` → `SetExpressionResponse` | unary | `of_setexp` — **4 overloads** [`:L174-L177`]: by index and by name, each with and without an explicit recalculate flag |
| 5 | `GetExpression` | `GetExpressionRequest` → `GetExpressionResponse` | unary | `of_getexp` — **2 overloads** [`:L127`, `:L130`]. Returns the **unexpanded source** text, which is what makes static-versus-dynamic recoverable ([§7.3](#73-the-contract-consequence-which-is-exact-and-non-negotiable)) |
| 6 | `RemoveExpression` | `RemoveExpressionRequest` → `RemoveExpressionResponse` | unary | `of_remove` — **2 overloads** [`:L131-L132`] |
| 7 | `RemoveAllExpressions` | `RemoveAllExpressionsRequest` → `RemoveAllExpressionsResponse` | unary | `of_removeall` [`:L125`] |
| 8 | `AddVariable` | `AddVariableRequest` → `AddVariableResponse` | unary | `of_addvar` across the **seven typed overloads** [`:L153-L159`] — time, string, long, double, datetime, date, boolean. The type travels as a **discriminated union**, never as a string |
| 9 | `SetVariable` | `SetVariableRequest` → `SetVariableResponse` | unary | `of_setvar` across **seven types × three arities = 21 setters** [`:L160-L166`, `:L179-L185`, `:L188-L194`] — value; value plus recalculate; value plus recalculate plus force |
| 10 | `AddVariableExpression` | `AddVariableExpressionRequest` → `AddVariableExpressionResponse` | unary | `of_addvarexp` [`:L173`]. A variable whose value is **itself an expression**, which is why `localvardata` is recursive |
| 11 | `SetVariableExpression` | `SetVariableExpressionRequest` → `SetVariableExpressionResponse` | unary | `of_setvarexp` — **3 overloads** [`:L178`, `:L186`, `:L187`] |
| 12 | `GetVariableExpression` | `GetVariableExpressionRequest` → `GetVariableExpressionResponse` | unary | `of_getvarexp` [`:L152`] |
| 13 | `AddForeignVariable` | `AddForeignVariableRequest` → `AddForeignVariableResponse` | unary | `of_addforeignvar` [`:L201`]. **The legacy takes a live control; this takes a session-scoped handle** — the substance of the one narrowing in this contract ([§7.7](#77-the-one-hard-limit--cross-session-foreign-variables)) |
| 14 | `SetRelativeColumns` | `SetRelativeColumnsRequest` → `SetRelativeColumnsResponse` | unary | **8 legacy entry points** [`:L128-L129`, `:L135-L140`] — singular and plural, by index and by name, for both the change-triggered and the **input-triggered** sets. The two sets are distinct and must not be merged |
| 15 | `SetExpressionFlag` | `SetExpressionFlagRequest` → `SetExpressionFlagResponse` | unary | **4 flags × 2 addressing modes = 8 entry points**: always-calculate [`:L133-L134`], recursive [`:L146-L147`], trigger-event [`:L148-L149`] and **cacheable** [`:L196-L197`] — the last declared in the source and **absent from the documented nine** |
| 16 | `Calc` | `CalcRequest` → `CalcResponse` | unary | `of_calc` — **4 overloads** [`:L143`, `:L144`, `:L168`, `:L169`] |
| 17 | `CalcAll` | `CalcAllRequest` → `CalcAllResponse` | unary | `of_calcall` — **2 overloads** [`:L145`, `:L170`] |
| 18 | `CalcEmpty` | `CalcEmptyRequest` → `CalcEmptyResponse` | unary | `of_calcempty` — **2 overloads** [`:L199-L200`], for one row and for all |
| 19 | `CalcItem` | `CalcItemRequest` → `CalcItemResponse` | unary | `_of_calcitem` [`:L142`] — **public despite its private-convention underscore prefix, and returning `boolean` rather than a `RetCode`.** Both anomalies are preserved, not tidied |
| 20 | `SetEnabled` | `SetEnabledRequest` → `SetEnabledResponse` | unary | `of_setenabled` [`n_cst_dwsvc.sru:L89-L95`]. **Vetoable, and therefore returns a `RetCode` rather than a boolean** |
| 21 | `SetTrace` | `SetTraceRequest` → `SetTraceResponse` | unary | `of_settrace` [`:L208`]. **Not vetoable** — the asymmetry with `SetEnabled` is deliberate |
| 22 | `GetServiceState` | `GetServiceStateRequest` → `GetServiceStateResponse` | unary | Exposes the `#Enabled` and `#Trace` state the two setters write [`:L98`, `:L2409`] |
| 23 | `GetExpressionState` | `GetExpressionStateRequest` → `GetExpressionStateResponse` | unary | **The engine-state snapshot.** Publishes the seven structures of [§7.5](#75-seven-structures-that-are-the-contract) — the expression table, the **reverse dependency index** [`:L44-L48`], the grammar sentinels, and on request the global variable table and the three-part `$` / `$$` bindings. Read-only by design: state is mutated through the typed operations above, never by posting a snapshot back, because an engine restorable from a caller-supplied graph would accept a dependency index inconsistent with the expressions it indexes |
| 24 | `EventStream` | `EventStreamRequest` → **stream** `EventStreamResponse` | **server streaming** | The three events the engine declares on **itself** [`:L86-L88`] — item-changed, do-item-changed with its `frominput` flag, and var-changed with its `forcecalc` flag. Declaring them without a delivery mechanism would have left them unreachable; a server stream is the delivery mechanism, and it carries a **sequencing token** because these are notifications rather than an ordered chain |
| 25 | `InvokeMethodChannel` | **stream** `InvokeMethodResponse` → **stream** `InvokeMethodRequest` | **bidirectional streaming** | **Inverted stream 1 — macro invocation.** Note the message names look backwards and are not: DataServices asks its *client* to evaluate a macro, because the legacy expects the **application** to implement the macro switch [`docs/n_cst_dwsvc_columnexp.md:L106`; source at `:L2263`, `:L2287`]. Synchronous within the stream — the calculation cannot proceed without the value ([§7.6](#76-two-inverted-streams-structurally-required)) |
| 26 | `TraceChannel` | **stream** `TraceChannelRequest` → **stream** `TraceRecord` | **bidirectional streaming** | **Inverted stream 2 — the expression trace**, including the call stack the recursion vector builds [`:L110`, `:L296-L297`, `:L318`, `:L753-L757`, emitted at `:L758`]. Server-initiated and fire-and-forget, so it carries a **sequencing token** rather than strict ordering. Gated on `#Trace` [`:L98`] |

**Twenty-six RPCs cover roughly sixty legacy entry points**, and the collapse is deliberate: what
varies across a legacy overload group — index versus name addressing, the arity of the recalculate and
force flags, which of seven scalar types a variable holds — becomes **fields of one request** rather
than separate RPCs. Preserving one RPC per overload would have produced a contract nobody could
version. [§7.9](#79-the-contract-covers-the-source-surface-not-the-documented-nine) enumerates the
legacy arities that sit behind each row, and is the audit trail for this table rather than a duplicate
of it.

**Three of the twenty-six stream, and the two bidirectional ones are inverted** — the server calls
back into the client. That is not a stylistic choice: it is forced by the legacy expecting the
application to supply macro implementations and to receive trace records. Any implementation that makes
these ordinary client-initiated calls has inverted the contract, not simplified it. `EventStream`, the
third, is an ordinary server stream: the engine notifies and the client listens.

Row 23 is the one addition that is not an overload collapse and not a stream, and it is the row that
makes [§7.5](#75-seven-structures-that-are-the-contract) an API rather than a description. The same
enumeration derived from the compiled file descriptor, with the wire kinds rather than the legacy
anchors, is [§7.10](#710-the-wire-method-surface-enumerated); the two agree at twenty-six, and a
disagreement between them is a defect in this document.

### 7.2 Why `$` and `$$` differ mechanically

This distinction is the one the brief calls out as not surviving naive serialization, and the legacy
specification is unambiguous about it. Both halves of its worked example were read at source.

**Static expansion — syntax `$name`** [`docs/n_cst_dwsvc_columnexp.md` §静态展开 L37-L54]. The
variable's value is substituted **at the moment the expression is set**, so later mutation of that
variable does not change the result. The worked example defines an expression variable valued `"5"`
[L45] and a numeric variable valued `0` [L47], then binds `"$上月读数 + $本月读数"` [L49]. The document
states at **L48** that the result is **permanently fixed at 5**, and at **L50** that a subsequent
set-and-recalculate still yields **5** [L51-L53].

**Dynamic expansion — syntax `$$name`** [§动态展开 L56-L73]. The variable is retained **as a
reference**, evaluated at calculation time, so later mutation propagates. **The identical example
with `"$$上月读数 + $本月读数"` [L68] yields 6** [L69].

Same inputs, same mutation, different results: 5 against 6. The difference is one extra `$`.

**Mechanically**, the parse routine rewrites the expression **in place** and emits the reference
arrays — `_of_parseexp (ref string exp, ref vardata vars[], ref funcdata fns[])` takes all three
parameters by reference [`n_cst_dwsvc_columnexp.sru:L171`, implemented at `:L1211`]. From there:

- A **static** reference is resolved and substituted into the rewritten text at bind time. **The
  variable name is gone from the stored expression**, so no later assignment can reach it.
- A **dynamic** reference leaves an entry whose `index` points into the live global-variable table, so
  mutation propagates on the next calculation.

### 7.3 The contract consequence, which is exact and non-negotiable

> **The payload must transmit all three of (i) the unexpanded source expression text, (ii) the
> bind-time variable snapshot, and (iii) the live variable environment — plus, per reference, which
> expansion mode applied.**

Transmitting an already-expanded string is the obvious design and it is wrong in two independent
ways:

- a **static** binding becomes indistinguishable from a literal, so no consumer can tell that `5` in
  the stored text was ever a variable, or which one;
- a **dynamic** binding is stripped of its resolution environment, so the reference has nothing left to
  resolve against and the expression can never be recalculated correctly again.

The unexpanded text is preserved in the model as `columnexpdata.exp` — the field the seven-structure
table below marks as *the source expression* — and it is that field, not the rewritten one, that the
contract carries.

### 7.4 Five expansion modes, and the mode is per-reference

| # | Mode | Syntax | Locator | Note |
| --- | --- | --- | --- | --- |
| 1 | **static** | `$name` | §静态展开 L41 | Substituted at bind time |
| 2 | **dynamic** | `$$name` | §动态展开 L60 | Resolved at calculation time |
| 3 | **dynamic-indirect** | `$$('nameString')` | §4 L75-L94, syntax at L79 | The variable *name* is computed at run time and **may itself be a DataWindow expression**. The legacy example selects between two variables by comparing two columns: `of_SetExp("n1","$$(if(n2 > n3 ,'num1','num2'))")` [L92] |
| 4 | **macro-direct** | `$FunctionName(args)` | §宏函数 §1 L108-L128, syntax at L110 | Invoked on the client; see [§7.6](#76-two-inverted-streams-structurally-required) |
| 5 | **macro-dynamic** | `$$Invoke('FunctionName', args)` | §宏函数 §2 L130-L151, syntax at L134 | **The *function* name is itself a variable** [example at L139-L141] |

> **Expansion mode is a per-reference property, never a per-expression one.**

The legacy specification settles this by example: `"$$上月读数 + $本月读数"` [L68] mixes a dynamic and a
static reference **inside a single expression**, and the consolidated example does the same with
`of_SetExp("n1", "$$bb + $aa")` [L160]. A contract that tagged the expression rather than each
reference could not represent either line.

The internal grammar is encoded by two sentinel constants, both of which the contract's reference
model must reproduce:

| Constant | Value | Locator | Models |
| --- | --- | --- | --- |
| `FUNC_VAR` | `""` (empty string) | `n_cst_dwsvc_columnexp.sru:L120` | A bare variable reference — a function entry whose name is empty |
| `FUNC_INVOKE` | `"Invoke"` | `:L121` | Dynamic dispatch — mode 5 above |

Calculation state uses a **tri-state cache** rather than a boolean: `CLC_UNKNOWN` = 0, `CLC_YES` =
1, `CLC_NO` = 2 [`:L113-L115`]. "Not yet determined" is a distinct state from "determined not to
calculate", and collapsing them would turn an undetermined column into a suppressed one. Alongside
it sit re-entrancy flags [`:L105-L106`], a cached column identifier [`:L108`], and the recursion
stack [`:L110`].

### 7.5 Seven structures that *are* the contract

`n_cst_dwsvc_columnexp.sru` forward-declares them at `:L6-L19` and defines them at `:L22-L83`.
Collectively they are the wire model; nothing about this contract can be designed without them.

| Structure | Locator | Fields that matter for serialization |
| --- | --- | --- |
| `columnexpdata` | `:L22-L42` | **19 fields.** `name`, `id`, `dwo`, `coltype`, **`exp`** — *the source, unexpanded expression text* — **`vars[]`**, **`fns[]`**, `relativecolids[]`, `relativeinputcolids[]`, **`dupexps[]`** (the mechanism behind **multiple expressions per column, selected by which column changed**), `emptystringisnull`, `hasmacro`, `alwayscalc`, `triggerevent`, `recursive`, `dirty`, `empty`, `cacheable`, `computename` |
| `columndata` | `:L44-L48` | `flag`, `indexes[]`, `varindexes[]` — the **reverse dependency index** from a column to the expressions and variables that depend on it. **This is the graph that makes dirty-propagation work**, and it must be transmitted, not recomputed: a consumer cannot derive it from the expression text without reimplementing the parser |
| `vardata` | `:L50-L56` | `name`, `fullname`, `index`, **`isctx`** (the reference is context-scoped), **`ismacro`** |
| `funcdata` | `:L58-L63` | `name`, `fullname`, `args[]`, `builtin` |
| `globalvardata` | `:L65-L71` | `name`, **`vartype`** — `VAR_LOCAL` = 0, `VAR_FOREIGN` = 1 [`:L117-L118`] — `local`, `foreign`, **`links[]`** |
| `localvardata` | `:L73-L78` | `exp`, `vars[]`, `fns[]`, `hasmacro`. **Recursive**: a variable's value may itself be an expression with its own variable and function references, so the wire message nests |
| `foreignvardata` | `:L80-L83` | `index`, **`expsvc`** — **a live object pointer** to another DataWindow's expression service. See [§7.7](#77-the-one-hard-limit--cross-session-foreign-variables) |

Three service-level events are declared on the object itself and are surfaced on this contract:
`onitemchanged(row, dwo)` [`:L86`], `ondoitemchanged(row, colname, colid, frominput)` [`:L87`] —
note the `frominput` flag, which is how the *manual input only* trigger constraint is expressed —
and `onvarchanged(index, forcecalc)` [`:L88`]. The compute-name suffix the service attaches to
generated compute objects is `DWOSUFFIX` = `"_columnexp"` [`:L95`], and it is observable in
`columnexpdata.computename`.

### 7.6 Two inverted streams, structurally required

Both streams invert the ordinary direction of a gRPC call: DataServices, the *server*, calls back
into its *client*. This is not a stylistic preference and it is not a convenience — a unidirectional
design cannot express it at all.

**`InvokeMethodChannel` — macro invocation.** The proof is in the legacy specification, and it is
explicit: **both** macro forms are implemented **by the application**, in the DataWindow's
`OnColumnExpInvokeMethod` event, via a `choose case name` switch that returns the computed value
[§宏函数 L120-L128 for the direct form and L144-L151 for the dynamic form]. The engine does not own the
macro implementations; it asks for them.

The source confirms the inversion twice over: `#DataWindow.Event OnColumnExpInvokeMethod(...)` is
invoked from inside the expression preprocessor at `n_cst_dwsvc_columnexp.sru:L2263` — the dynamic
form, taking the function name from the first argument — and at `:L2287` — the direct form, taking
it from the function entry's own name. The event's declared signature is
`(long row, dwobject dwo, string name, string args[]) → any` [`se_cst_dw.sru:L14`].

Across a network boundary, therefore, **DataServices must call back into its client** to obtain a
macro result before the calculation can proceed. The stream carries the row, the column identity,
the function name and the argument array outbound, and the computed value inbound. It is
**synchronous within the stream** — the calculation cannot continue without the value — which is why
macro invocation is assigned pattern (b) in
[§6.6](#66-ordering-pattern-assigned-per-capability-area).

**`TraceChannel` — the expression trace.** It carries the trace record **including the call stack**
that the vector container builds. In `n_cst_dwsvc_columnexp.sru` the vector is declared at `:L110`
and created at `:L2421` with an initial reservation of 20 [`:L2422`]; entries are appended on entry
to a calculation [`:L296`], its depth is read [`:L297`], and the entry is removed on exit [`:L318`].
The recursion guard scans the same stack for a repeated column name [`:L684-L686`], and the stack is
flattened into a `>`-delimited string for the trace [`:L753-L755`].

Emission is gated on the `#Trace` flag [`:L98`, set by `of_settrace` at `:L2409-L2411`], and the
trace event fires at `:L758` with the row, the column object, the call-stack string, the expression,
and the value. One formatting detail is observable and preserved: the value is reported as the
literal `(null)` when it is empty **and** either `emptystringisnull` is set or the column is not a
string column [`:L758`].

`TraceChannel` is fire-and-forget and is assigned pattern (a).

**The `#Enabled` property is also exposed on this contract.** It is inherited — declared
`protectedwrite boolean` at `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru:L38` with
its setter at `:L89-L92` — and it matters to a consumer for a reason beyond introspection: C-03's
item-changed sequence invokes this service **only when `#Enabled` is set**
[`se_cst_dw.sru:L313-L315`], so a consumer that sees no recalculation needs to be able to
distinguish "disabled" from "nothing to do".

### 7.7 THE ONE HARD LIMIT — cross-session foreign variables

> **`foreignvardata.expsvc` is an in-process object pointer and cannot be serialized.**

The field is typed `n_cst_dwsvc_columnexp` [`n_cst_dwsvc_columnexp.sru:L82`] — a direct reference to
*another live instance of this same service*, belonging to another DataWindow. It is not a handle,
not a name, and not an identifier: it is a pointer, and it is dereferenced as one. At `:L2384-L2386`
the foreign path reads

- `if GlobalVars[index].varType = VAR_FOREIGN then`
- `return GlobalVars[index].foreign.expSvc._of_CalcVarExpValue(...)`

— a **synchronous call through the stored pointer into a private method of the other instance**.
Both `of_addforeignvar(name, se_cst_dw dw)` [`:L201`, implemented at `:L2097`] and the `links[]`
array [`:L70`] depend on that access, and the legacy specification itself notes that a foreign
variable **requires dynamic expansion** [`docs/n_cst_dwsvc_columnexp.md:L34`, comment `//需要使用动态展开`]
— because a static substitution would have to read the foreign value at bind time and would then
never see it change.

**Resolution.**

- Cross-DataWindow variables are resolved by a **session-scoped DataWindow handle**, allocated and
  owned by the expression session.
- They are supported **only when both DataWindows are co-resident in the same expression session inside
  one DataServices instance**.
- **Foreign references spanning sessions or service instances are BLOCKED and return a defined error**
  that names the unreachable DataWindow handle. They are never resolved to a substituted value, a
  default, an empty string, or a stale reading.

This is a **genuine, unavoidable narrowing of the legacy contract**, and it is recorded here rather
than discovered during implementation precisely because that is what the brief asks for. The
alternative — approximating a pointer dereference across a network — would produce results that are
wrong in a way no test would obviously catch: the expression would evaluate, return a number of the
right type in the right range, and be silently stale. A defined error is worse ergonomics and better
engineering.

It is also the **only** place in this inventory where a legacy *behaviour* cannot cross, and therefore
the only place the narrowing principle of
[§1.2](#12-the-governing-principle-for-anything-that-cannot-cross) is applied to a behaviour. The
principle is applied five further times, each to a *field* or a *domain* rather than to a behaviour, and
[§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess) is the canonical register of all six
— one behavioural, five field or domain. The distinction is the whole point of the register: a
behavioural narrowing changes what a caller can accomplish, while a field or domain narrowing changes
what a caller may put on the wire to accomplish it, and only the first is a capability the legacy had
and this system does not.

### 7.8 Parse errors carry a caret position and bypass localization

Parse and evaluation failures are reported through a builder taking the expression, a **caret
position**, and a message. Its three-argument form is at `n_cst_dwsvc_columnexp.sru:L2402-L2404` and
its two-argument delegation at `:L2406`; it renders the message, then the expression, then a run of
underscores, then `^`, then the numeric position in parentheses. Call sites include `:L1308`,
`:L1383` and `:L1390`, each computing the caret from a macro start position and the current scan
position.

There are **28 live dialog call sites in this object and zero commented ones** — verified by
enumeration. They include cache-creation failure [`:L713`], a cache error **carrying the text of a
caught runtime exception** [`:L739`], a general expression error [`:L762`], duplicate variable
definition [`:L1607`, `:L2122`], an undefined foreign variable [`:L2133`], cache-update and
macro-unsupported failures [`:L1671`, `:L1882`, `:L1888`], and eleven preprocessing failures
[`:L2192`, `:L2208`, `:L2232`, `:L2242`, `:L2254`, `:L2282`, `:L2306`, `:L2392`] alongside the parse
sites at `:L1395`, `:L1399`, `:L1417`, `:L1422`, `:L1426`, `:L1431`, `:L1486`, `:L1505`.

Each becomes a **structured error** on the wire, preserving:

- the original message text,
- the expression text,
- the **caret position** as a number, so a consumer can render the marker itself rather than parse the
  legacy's underscore-and-caret string,
- the failure category — cache creation, cache update, parse, preprocessing, undefined name, invalid
  value, macro unsupported, duplicate definition,
- and, where the legacy captured one, the text of the caught runtime exception.

> **A defect to preserve, not harmonize: these 28 messages are hardcoded Chinese and do NOT route
> through localization.**

This was verified directly — the count of localization calls in `n_cst_dwsvc_columnexp.sru` is
**zero**. That is in deliberate contrast with the equivalent messages elsewhere in the same
DataWindow service layer, which *do* route through the localization category `Categories.CAT_DWSVC`
— for instance the two on C-03's validation-error path at `se_cst_dw.sru:L355` and `:L357`.

The inconsistency is legacy behaviour and **it is reproduced rather than harmonized** (C-B). Routing
these through localization would change observable output text — which is exactly the kind of
improvement the mandate forbids. The contract carries a per-error `localized` flag so a consumer can
tell which messages are translatable, without any message changing.

### 7.9 The contract covers the *source* surface, not the documented nine

The legacy document describes **nine** methods. Its consolidated example [L153-L164] names
`of_AddVar`, `of_AddVarExp`, `of_SetVar`, `of_SetVarExp`, `of_SetExp`, `of_SetRelativeColumn`,
`of_SetRelativeInputColumn` and `of_Calc`, with `of_AddForeignVar` introduced earlier [L33] — and it
closes at **L166** by inviting the reader to explore further. **It is deliberately incomplete, and
it says so.**

> **The wire contract must cover what the source declares, not merely the documented nine.**

The full public surface, read from the prototype block at `n_cst_dwsvc_columnexp.sru:L124-L209`:

| Group | Operations and arities |
| --- | --- |
| Expressions | **add** (1); **set** in **four** arities — by index and by name, each with and without an explicit recalculate flag; **get** in **two** — by index and by name; **remove** in **two** — by index and by name; **remove-all** |
| Variables | **add across seven typed overloads** — time, string, long, double, datetime, date, boolean; **set across the same seven types in three arities each** (value; value plus recalculate; value plus recalculate plus force) — **21 setters** |
| Expression variables | **add**; **set** in **three** arities (expression; plus recalculate; plus recalculate plus force); **get** |
| Foreign variables | **add**, taking a name and a DataWindow [`:L201`] |
| Relative columns | relative-column and relative-input-column setters in **both singular and plural array forms, by index and by name** — eight entry points in total |
| Per-expression flags | always-calculate, recursive, trigger-event **and cacheable**, each **by index and by name** — eight entry points |
| Calculation | **calculate in four arities** — `(row, index)`, `(row, colname)`, `(row, force)`, `(row)`; **calculate-all in two** — with and without force; **calculate-empty in two** — for one row and for all |
| Trace | set-trace [`:L208`], plus the `#Trace` state it writes [`:L98`, `:L2409`] |
| Item calculation | An item-calculation entry point **declared public despite carrying the private-convention underscore prefix** — `public function boolean _of_calcitem (readonly long row, readonly integer index)` at `:L142` |

The last row is an anomaly **to carry rather than tidy**. It is public in the legacy, callers may
depend on it, and renaming it to drop the underscore would break a name that appears in recordings.
Two further groups the earlier planning summary omitted are included above on the same principle:
the **cacheable** flag setters and the **calculate-empty** arities are declared in the source, so
they are in the contract.

**The variable environment must be a discriminated union, not a stringly-typed map.** It is strongly
typed across **seven distinct scalar types**, and the coercion behaviour depends on that type
information: an expression that adds a `long` variable to a `date` variable behaves differently from
one that concatenates their string renderings. Collapsing the environment to strings would lose
exactly the information the coercion needs, and the loss would be invisible until a result was
subtly wrong.

### 7.10 The wire method surface, enumerated

**Twenty-six methods.** The table is derived from the generated file descriptor, so it is what
`Proto/dataservices.v1.proto` compiles to rather than a summary of intent. Note the relationship to
§7.9: the legacy's *arities* do not become separate RPCs. A legacy method that exists in four arities
becomes **one** RPC whose request carries the optional fields those arities differ by, because an
arity is a PowerScript overload convention while a protobuf request is a record — collapsing them
loses nothing, whereas collapsing two differently *named* legacy operations would.

| # | Method | Kind | Notes |
| --- | --- | --- | --- |
| 1 | `OpenExpressionSession` | unary | Scopes the foreign-variable DataWindow handles ([§7.7](#77-the-one-hard-limit--cross-session-foreign-variables)) |
| 2 | `CloseExpressionSession` | unary | |
| 3 | `AddExpression` | unary | **The only Add\* operation that returns an index**, because it is the only one whose legacy counterpart does [`n_cst_dwsvc_columnexp.sru:L172`, `:L1583`] |
| 4 | `SetExpression` | unary | Carries the four legacy arities as request fields |
| 5 | `GetExpression` | unary | By index or by name |
| 6 | `RemoveExpression` | unary | By index or by name |
| 7 | `RemoveAllExpressions` | unary | |
| 8 | `AddVariable` | unary | The seven typed overloads collapse into one typed union field; returns no index |
| 9 | `SetVariable` | unary | Seven types × three arities, as fields |
| 10 | `AddVariableExpression` | unary | Returns no index |
| 11 | `SetVariableExpression` | unary | Three arities, as fields |
| 12 | `GetVariableExpression` | unary | |
| 13 | `AddForeignVariable` | unary | Returns no index; **requires dynamic expansion** and is subject to the one hard limit |
| 14 | `SetRelativeColumns` | unary | Covers relative-column and relative-input-column, singular and plural, by index and by name |
| 15 | `SetExpressionFlag` | unary | Covers always-calculate, recursive, trigger-event and cacheable, by index and by name |
| 16 | `Calc` | unary | The four legacy calculate arities |
| 17 | `CalcAll` | unary | Both legacy arities |
| 18 | `CalcEmpty` | unary | One row and all rows |
| 19 | `CalcItem` | unary | Annotated `legacy_name = "_of_calcitem"`, preserving the legacy spelling **including its private-convention underscore** without inflicting it on the generated C# member name. Returns a boolean, as the legacy declares at `:L142` |
| 20 | `SetEnabled` | unary | The `#Enabled` property |
| 21 | `SetTrace` | unary | The `#Trace` property |
| 22 | `GetServiceState` | unary | Reads service-level state |
| 23 | `GetExpressionState` | unary | Returns the **seven-structure expression model** of [§7.5](#75-seven-structures-that-are-the-contract) — expressions, the reverse dependency index, the sentinels, the global variable table and the bindings. Without it the seven structures would be declared but unreachable |
| 24 | `EventStream` | **server stream** | The service's own three events, delivered as a defined stream instead of being declared with no way to receive them |
| 25 | `InvokeMethodChannel` | **bidirectional stream, INVERTED** | Its request message is `InvokeMethodResponse` and its response message is `InvokeMethodRequest` — deliberately, because the *engine* asks and the *client* answers ([§7.6](#76-two-inverted-streams-structurally-required)) |
| 26 | `TraceChannel` | **bidirectional stream** | Carries the expression trace including the call stack the vector builds |

Three of the twenty-six carry the properties that made this contract worth separating from C-03: 23
publishes the expression model, and 25 and 26 are the two inverted streams. If C-04 were folded into
C-03, all three would have to version in lockstep with the event chain
([§15.5](#155-folding-c-04-into-c-03--rejected)).

### 7.11 The boundary this contract does *not* cross: calculation over retrieved data

**No method above loads rows into an expression session, and that is a property of the published contract
rather than a defect in a service.** It is stated here because the runtime consequence is easy to
misdiagnose, and because closing it would take a new capability rather than a fix.

The three shapes of the boundary, each read off the definitions themselves:

- `OpenExpressionSessionRequest` carries **logical DataWindow handles only** — names that scope
  foreign-variable resolution ([§7.7](#77-the-one-hard-limit--cross-session-foreign-variables)). A handle
  is not a rowset and does not fetch one.
- `CalcRequest`, `CalcAllRequest` and `CalcItemRequest` carry **no row payload**. They name what to
  calculate, never the data to calculate it over.
- C-03's `RetrieveRequest` carries **no expression-session identifier**, so a retrieval cannot deposit its
  result into a session even though both live in the same service.

**What a caller therefore observes at runtime, and both answers are truthful.** Against a session whose
host holds no rows, `Calc` for row 1 answers `E_OUT_OF_RANGE` — there is no row 1 — and `CalcAll` answers
success with **zero** results, because calculating every row of an empty host is a complete piece of work
with an empty answer. Neither is an error report about the engine, and neither should be read as one:
performing a large retrieval first does not change either answer, because nothing connects the two.

**Why it is declared rather than closed.** A row-loading RPC — or a session identifier on
`RetrieveRequest` — would be a **new capability**, and this refactor adds none: the legacy engine reads a
DataWindow control in the caller's own address space, so there is no legacy behaviour being withheld here
and nothing to preserve by adding one. Constraint C-B and AAP §0.2.2.5 forbid the addition; the honest
diagnostics above are the correct answer under the contract as published.

**What is consequently covered elsewhere rather than here.** The expansion engine's behaviour over
populated data — the seven structures, the five expansion modes, static versus dynamic expansion, the
macro channel and the trace — is exercised against in-process hosts by this service's own test suites,
which can populate a host directly. What no runtime exercise of the published contract can reach is that
same engine over data **fetched by this system**, and
[`PARITY.md` §9 R9](PARITY.md#r9--expression-calculation-over-retrieved-data-is-unreachable-through-the-published-contract)
carries that as a declared coverage boundary.

---

## 8. C-05 — `persistence.v1.QueryService`

| | |
| --- | --- |
| **Identifier** | `persistence.v1.QueryService` |
| **Transport** | gRPC — unary configuration plus one server stream |
| **Served by** | Persistence |
| **Consumed by** | DataServices |
| **Definition** | `Proto/persistence.v1.proto` |
| **Primary legacy source** | `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` (883 lines) |
| **Status** | NEW |

### 8.1 One storage engine is provisioned, and it is SQLite

Stated before the method surface, because a reader who meets the paging strategies first could draw
the wrong conclusion:

> **Persistence provisions SQLite and only SQLite.** No other database engine is provisioned,
> configured, containerized, connected to, or required — not in development, not in CI, not in the
> orchestration manifest, and not by any test. **This contract implies exactly one backing store.**

The two other engine *dialects* named anywhere in this document appear **only** as **paging-rewriter
strategies**, which are **pure string transforms**: an input statement plus a page size and a page
index yields an output statement, with no connection involved at any point. They are unit-testable
with no instance of either engine in existence, and that is precisely how both behaviours are
preserved without fabricating a schema for either (C-E). The evidence for the single evidenced
schema, and the reasoning, are in [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.

Two engine dialects reproduced as text generators; one storage engine provisioned. Those are
different claims, and only the second involves a running database.

### 8.2 Method surface

Locators in this subsection are into `n_cst_thread_task_sqlquery.sru` unless stated otherwise.

**All eleven RPCs the service declares**, in definition order. The table is the complete inventory —
if `Proto/persistence.v1.proto` and this table ever disagree, the `.proto` is right and this table is
a defect:

| # | RPC | Request → Response | Kind | Behaviour the contract must preserve |
| ---: | --- | --- | --- | --- |
| 1 | `CreateQueryTask` | `CreateQueryTaskRequest` → `CreateQueryTaskResponse` | unary | Creates a **server-held** query task and returns its handle — the wire form of legacy object creation. The task is stateful, which is why the next ten calls take a handle |
| 2 | `ReleaseQueryTask` | `ReleaseQueryTaskRequest` → `ReleaseQueryTaskResponse` | unary | The wire form of the legacy `Destroy`, releasing the task and its carrier. **Releasing an already-released handle is `E_INVALID_HANDLE`, not a silent success** |
| 3 | `Reset` | `ResetQueryTaskRequest` → `ResetQueryTaskResponse` | unary | `of_reset` [`:L50`, `:L237`]. **Returns `E_BUSY` while the task is running** |
| 4 | `SetChunkSize` | `SetChunkSizeRequest` → `SetChunkSizeResponse` | unary | `of_setchunksize` [`:L57`]. **A value at or below 1000 yields `RetCode.E_INVALID_ARGUMENT`** [`:L410`] — reproduced verbatim, including the fact that the boundary is *inclusive* |
| 5 | `SetMaxRows` | `SetMaxRowsRequest` → `SetMaxRowsResponse` | unary | `of_setmaxrows` [`:L61`]. **A negative value yields `RetCode.E_INVALID_ARGUMENT`** [`:L433`]. Note the boundary differs from `SetChunkSize`'s deliberately: this one rejects only below zero, so **zero is accepted here and rejected there** |
| 6 | `SetWhereClause` | `SetWhereClauseRequest` → `SetWhereClauseResponse` | unary | `of_setwhereclause` [`:L53`]. Carries the select index, the modification style, and the clause. **A zero-or-negative index or an empty clause yields `RetCode.E_INVALID_ARGUMENT`** [`:L271`] |
| 7 | `SetOrderByClause` | `SetOrderByClauseRequest` → `SetOrderByClauseResponse` | unary | `of_setorderbyclause` [`:L54`]. The same three fields and the same validation [`:L288`] |
| 8 | `SetPaging` | `SetPagingRequest` → `SetPagingResponse` | unary | Groups five legacy setters — `of_setpaged`, `of_setpagesize`, `of_setpageindex`, `of_setpagenative` and `of_setpagecounting` [`:L62-L66`] — into one call, **because the legacy re-validates size and index together** [`:L308`], so a caller setting one without the other has configured a task that cannot run. **A page size or index at or below zero yields `RetCode.E_INVALID_ARGUMENT`** [`:L307-L310`] |
| 9 | `SetPagedUniqueIndexColumns` | `SetPagedUniqueIndexColumnsRequest` → `SetPagedUniqueIndexColumnsResponse` | unary | `of_setpageduniqueindexcolumns` [`:L56`]. A non-empty list is what selects the inner-join paging strategy — forms 1 and 2 of [§8.4](#84-paging-parity-is-byte-exact-generated-sql) [`:L406`] |
| 10 | `Query` | `QueryRequest` → **stream** `QueryResponse` | **server streaming** | Runs the retrieve, reproducing the legacy's progressive recordset delivery. The legacy chunk count is `ceiling((rowCount + filteredCount) / chunkSize)` [`:L145`] |
| 11 | `Count` | `CountRequest` → `CountResponse` | unary | Total record and page counts, including the no-statement short-circuit; see [§8.5](#85-the-count-wrapper-and-its-short-circuit) |

**Three of the eleven are task-lifecycle calls** — create, release, reset — and they exist because the
legacy query object is *stateful*: a caller configures it across several calls and then runs it. That
state has to live somewhere once the boundary is a network, so it lives in a server-held task
addressed by a handle. Omitting the lifecycle from the inventory would leave a reader unable to
explain where the handle in the other eight calls comes from.

The remaining per-task settings are carried as **fields on `QuerySpec`** rather than as separate
calls, because they are configuration rather than operations: source selection by statement [`:L67`]
or by data-object name [`:L59`], a filter [`:L69`], a sort [`:L70`], a result-cache switch [`:L58`],
and a hook class name [`:L60`] — all declared in the prototype block at `:L49-L71`. **The maximum row
count is the one setting available both ways**: it is a `QuerySpec` field *and* has the dedicated
`SetMaxRows` RPC above, mirroring the legacy, which likewise lets it be configured up front or
changed on an existing task.

### 8.3 Clause modification, and the injection site named plainly

The modification style is a three-valued enumeration whose identifiers and values are preserved
verbatim from `ws_objects/pfw.shared.pbl.src/enums.sru:L718-L720`:

| Identifier | Value |
| --- | --- |
| `Enums.SQL_MS_REPLACE` | 1 |
| `Enums.SQL_MS_APPEND` | 2 |
| `Enums.SQL_MS_PREPEND` | 3 |

Clauses are applied by parsing the original statement and calling the parser's clause modifiers —
`ModifyWhere(index, style, clause)` at `:L691` and `ModifyOrder(index, style, clause)` at `:L698` —
then reading the rewritten statement back [`:L703`] and splicing it into the DataWindow's select
property [`:L709`]. A modifier that fails raises an internal error naming the clause kind [`:L692`,
`:L699`].

> **This is the SQL-injection site, and the contract says so rather than quietly repairing it: the
> clause arrives as a raw string and is spliced into the statement.**

The exposure is mechanical rather than hypothetical, and its root is recorded in
[`ARCHITECTURE.md`](ARCHITECTURE.md) §8.5. The implementation uses parameterized commands internally
while keeping the **observable generated statement** unchanged, and each site is annotated as a
known legacy defect. What is *not* done is silently altering the generated statement, because that
would change observable behaviour (C-B).

The clause body itself is **not** accepted unconditionally, though. `Sql/ClauseModifier.cs` refuses a
body that carries statement structure — a semicolon, a comment introducer, an unbalanced delimiter or a
statement verb outside a quoted context — and answers `E_INVALID_ARGUMENT`, the same code the legacy's own
guard already answers for a non-positive index or an empty clause. It **refuses rather than rewrites**, so
an accepted clause is stored byte for byte and statement parity is untouched.

### 8.3a The read-only statement grammar, and why the scope needed enforcing

C-05 is published under the `persistence.read` scope. An earlier revision of this section justified that
by observing that the legacy retrieval task generates `SELECT` statements and nothing else — which is true
of the **task** and says nothing about the **surface**. Three of this contract's inputs are caller-authored
SQL rather than task-generated:

| Field | What it reaches | Gated by |
| --- | --- | --- |
| `QuerySpec.sql` | the statement the carrier retrieves through | `Sql/ReadOnlyStatementGuard.cs` |
| `QuerySpec.sql_syntax` | its `retrieve="..."` clause becomes the registered statement | `Sql/ReadOnlyStatementGuard.cs` |
| `SqlClauseSpec.clause` | spliced into the parsed statement | `Sql/ClauseModifier.cs` (§8.3) |

> **A read-scoped credential that can reach an arbitrary statement is not a read scope.** The provider
> executes every statement in a batch it is handed, so one spliced semicolon turns a retrieval into a
> retrieval plus a `DELETE`; and even without a batch, `WITH x AS (...) INSERT INTO ...` is valid SQL whose
> leading keyword is not a mutation at all.

The legacy needed no gate because it is a **library**: it opens no socket and has no caller whose rights
are narrower than the process's. This contract does, so the scope is now **enforced** rather than assumed.
A statement is accepted only when all of the following hold; otherwise it is refused with
`E_INVALID_SQL`, nothing is stored, and the diagnostic names the rule **without quoting the rejected
text**:

1. It is **one** statement. A semicolon is admitted only as a trailing terminator with nothing but
   whitespace after it.
2. Its first bare word is `SELECT`, `WITH` or `VALUES`.
3. Outside string literals, quoted identifiers and bracketed identifiers it contains none of `INSERT`,
   `UPDATE`, `DELETE`, `UPSERT`, `MERGE`, `INTO`, `CREATE`, `ALTER`, `DROP`, `TRUNCATE`, `RENAME`,
   `REINDEX`, `VACUUM`, `ANALYZE`, `ATTACH`, `DETACH`, `PRAGMA`, `BEGIN`, `COMMIT`, `ROLLBACK`,
   `SAVEPOINT`, `GRANT`, `REVOKE`, `DENY`, `EXEC`, `EXECUTE`, `CALL`, `DECLARE`, `WAITFOR`, `SHUTDOWN`,
   `RECONFIGURE`, `BACKUP`, `RESTORE`, `OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE`, `LOAD_EXTENSION`, or
   any name beginning `xp_` or `sp_`. `REPLACE` is admitted only as a function call — immediately followed
   by `(` — and refused as the SQLite DML verb. Rule 2 alone is insufficient, which is why rule 3 spans
   the whole statement.
4. It carries no SQL comment: neither `--` nor `/* */`.
5. Its parentheses balance and every quoted run is terminated.
6. It contains no control character other than tab, carriage return and line feed.

`LOAD_EXTENSION` is the one entry that is not a statement verb, and it is the most important: it is a
scalar **function**, reachable from inside an ordinary select list, and what it does is load and execute
arbitrary native code. The provider disables extension loading by default, but a guard that depends on a
provider default is a guard that breaks when the default is changed somewhere else.

**What is deliberately not refused**, so a caller can predict the boundary exactly: an **empty** statement
— the legacy defers that check to run time, where it answers `E_INVALID_SQL` with `SQL为空!`
[`:L615-L617`], and moving it forward would be the behavioural change C-B forbids; a **malformed**
statement, which the provider still rejects with the provider's own diagnostic; an unknown table or column;
and a statement whose literals or identifiers happen to contain a refused word as **data** —
`WHERE note = 'delete me'` and `WHERE [delete] = 1` are both accepted.

The same grammar gates C-08's `GridSyntaxFromSql`, which is the other read-scoped RPC taking
caller-authored SQL. Its derivation asks the engine for a result schema rather than executing the
statement, so the gate there is defence in depth — but a guard that relies on a collaborator's behaviour
is a guard that breaks when the collaborator changes.

**Send state-changing statements to C-07 instead.** `CommandService` is published under
`persistence.write` and DML is its entire purpose [`n_cst_thread_task_sqlupdate.sru:L204`]; this is a scope
boundary rather than a capability the system lacks.

### 8.4 Paging parity is byte-exact generated SQL

The dispatch is a `choose case` on the connection's dialect selector at
`n_cst_thread_task_sqlquery.sru:L320`, with three `choose case` arms — of which the first subdivides
further, so three arms do **not** mean three generated statements:

| Arm | Selector value | Locators | Generated form |
| --- | --- | --- | --- |
| First dialect arm | `DBT_MSSQL` = 0 | `:L321-L385` | **Four** distinct generated forms, enumerated below — not three |
| Second dialect arm | `DBT_ORACLE` = 1 | `:L386-L395` | A single **triple-nested row-number** form [`:L394-L395`] |
| `case else` | anything else | `:L396-L398` | **`RetCode.E_NO_IMPLEMENTATION`**, raised through the error event — preserved exactly, including the fact that it is a *distinct* code from `E_NO_SUPPORT` |

**The first dialect arm generates four different statements, and a rewriter must implement all four.**
It is easy to read as three because the source nests the branches, but the arm is governed by **two
independent booleans**, so it has a two-by-two cross-product of outcomes: whether unique-index columns
have been supplied [`:L323`], and whether the caller has selected the native paging implementation
through `of_SetPageNative` [`:L39`, `:L457`], tested at [`:L343`] and [`:L366`]. Every cell produces
observably different SQL:

| # | Unique-index columns | Native paging | Locators | What it generates |
| ---: | --- | --- | --- | --- |
| 1 | supplied | yes | `:L343-L350` | An **offset-fetch inner query** over the unique-index columns only [`:L345-L346`], then an `INNER JOIN` of that subquery back to the original statement as `pfwPagedSQL_OutterTbl` on the unique-index equality predicate [`:L350`]. The original column list is restored between the two steps [`:L348`] |
| 2 | supplied | no | `:L351-L363` | A **`TOP` plus `ROW_NUMBER()` inner query** over the same columns [`:L355-L356`], wrapped as `SELECT TOP n * FROM (…) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN …`, then the same `INNER JOIN` back as `pfwPagedSQL_OutterTbl` [`:L362`]. The order-by is stripped before the wrap [`:L353`] and restored after it [`:L360`], and the column list likewise [`:L358`] |
| 3 | not supplied | yes | `:L366-L373` | A plain **offset-fetch**: the existing order-by, or the `(SELECT 0)` substitution when there is none [`:L370`], with `OFFSET … ROWS FETCH NEXT … ROWS ONLY` appended to it [`:L372`] |
| 4 | not supplied | no | `:L374-L384` | A plain **`TOP` plus `ROW_NUMBER()`** wrap as `SELECT TOP n * FROM (…) pfwPagedSQL_Tbl WHERE pfwPagedSQL_RN BETWEEN …` [`:L381-L382`], with `(SELECT 0)` substituted when there is no order-by [`:L379`] and — uniquely among the four — a trailing **`ORDER BY pfwPagedSQL_RN`** appended to the outer statement [`:L383`] |

Forms 1 and 2 share a prelude that neither of forms 3 and 4 executes [`:L327-L342`]: it concatenates the
unique-index column list, builds the equality predicate one column at a time [`:L333`] — where the
left side is `pfwPagedSQL_OutterTbl.` followed by only the portion of the column name *after* the
first dot while the right side is the full qualified name, so a supplied `t.id` yields
`pfwPagedSQL_OutterTbl.id = t.id` — collects into the order-by only those unique-index columns not
already present in it [`:L334-L337`], snapshots the original column list [`:L339`], and appends the
collected columns to the order-by [`:L341`]. Both forms then converge on the same epilogue, re-reading
the mutated statement out of the parser [`:L364`]. A rewriter that omits the prelude's *conditional*
order-by collection will emit a different `ORDER BY` than the legacy for any statement that already
sorts on a key column, and one that mishandles the dot-splitting will emit an unresolvable join
predicate for every qualified column name.

Forms 1 and 2 differ from 3 and 4 in *shape*, not only in detail: the first two emit an `INNER JOIN`
against a subquery of key columns, which is the whole point of supplying unique-index columns, while
the last two wrap the statement whole. And form 4's trailing `ORDER BY pfwPagedSQL_RN` appears in that
form alone, so a rewriter that shares one code path between forms 2 and 4 will emit it in the wrong
place. Counting these as three strategies rather than four is what hides that.

Counting Oracle's single form and the `case else` error, one dispatch therefore has **six** observable
outcomes, and a parity matrix needs a case for each.

The two selector constants are named because a rewriter has to be selected by *something*, and the
identifiers are preserved verbatim like every other constant
([§14.2](#142-constant-identifiers-are-preserved-verbatim)). **Naming a selector value is not
provisioning an engine** — each arm is a string-to-string function and neither has a connection, a
schema, or an instance behind it ([§8.1](#81-one-storage-engine-is-provisioned-and-it-is-sqlite)).
The selector itself is a string test on the descriptor's `dbms` field with the first arm as the
fallback [`n_cst_thread_trans.sru:L356-L361`].

Both arms first parse the original statement and fail with an internal error if parsing fails
[`n_cst_thread_task_sqlquery.sru:L312-L317`].

**Parity is byte-exact generated SQL**, which makes the sentinel identifiers part of the contract.
All six were read from source, with per-arm attribution — reproduce the spellings exactly,
**including the doubled `t` in `OutterTbl`, which is the legacy spelling**. Locators are into
`n_cst_thread_task_sqlquery.sru`:

| Sentinel | Emitted by — attributed per form, not per arm | Locators |
| --- | --- | --- |
| `pfwPagedSQL_OutterTbl` | first arm, **forms 1 and 2 only** — the two that supply unique-index columns | `:L333` (shared prelude), `:L350` (form 1), `:L362` (form 2) |
| `pfwPagedSQL_RN` | **forms 2 and 4, and Oracle** — the three non-native wraps. **Forms 1 and 3 never emit it**, because native offset-fetch needs no row-number column | `:L355`, `:L356` (form 2), `:L381`, `:L382`, `:L383` (form 4), `:L394`, `:L395` (Oracle) |
| `pfwPagedSQL_Tbl` | **forms 2 and 4**, and the count wrapper | `:L356` (form 2), `:L382` (form 4), `:L834` (count) |
| `pfwPagedSQL_TblInnerInner` | Oracle only | `:L394` |
| `pfwPagedSQL_TblInner` | Oracle only | `:L394` |
| `pfwPagedSQL_TblOuter` | Oracle only | `:L394` |

Attributing these per *form* rather than per *arm* is what makes them usable: an implementation that
emits `pfwPagedSQL_RN` in the native forms, or omits `pfwPagedSQL_OutterTbl` from form 2, produces
statements that still run and still return plausible rows while diverging from the legacy byte-for-byte.

Parity also covers the **empty-order-by substitutions**, which differ per arm and are not
interchangeable: the first arm substitutes `(SELECT 0)` in **forms 3 and 4** [`:L370`, `:L379`] —
forms 1 and 2 have no such substitution because their prelude has already appended an order-by —
and Oracle substitutes `''` [`:L392`]. And it covers the count-wrapper aliases of
[§8.5](#85-the-count-wrapper-and-its-short-circuit).

### 8.5 The count wrapper and its short-circuit

`Count` reproduces two behaviours, and the second is the kind that a straightforward implementation
omits:

- **The wrapper form.** The statement's column list is replaced with the literal **`1 AS _`**
  [`n_cst_thread_task_sqlquery.sru:L830`], any order-by is stripped [`:L831-L833`], and the result is
  wrapped as
  **`SELECT COUNT(1) AS CNT FROM ( … ) pfwPagedSQL_Tbl`** [`:L834`]. Both `1 AS _` and the `CNT` alias
  are observable and both are part of parity. Parameter binding is then applied to the wrapper, and a
  binding failure yields `RetCode.E_SQL_BIND_ARG_FAILED` [`:L836-L841`].
- **The short-circuit.** When the current page returned fewer rows than the page size, **or** returned
  no rows at all on page one, **no counting statement is issued** [`:L818-L823`]. Instead the page count
  is taken to be the current page index and the record count is computed as
  `(pageIndex − 1) × pageSize + rowsReturned`, with a zero record count forcing a zero page count.

The short-circuit is observable through the absence of a statement, so the contract states it: a
consumer must not infer from a missing count query that counting failed. Counting is additionally
suppressed entirely for a stored-procedure source or when the page-counting switch is off [`:L818`].

### 8.6 Errors, and the two fields that must be redacted

Failures return a structured `DbError` mirroring the legacy error event field for field. The legacy
structure is `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L3-L9`:

| Legacy field | Locator | Carried as |
| --- | --- | --- |
| `sqldbcode` | `:L4` | The driver's own numeric code |
| `sqlerrtext` | `:L5` | **A redacted string field — see below** |
| `sqlsyntax` | `:L6` | **A redacted string field — see below** |
| `buffer` | `:L7` | The offending buffer selector: primary, delete, or filter |
| `row` | `:L8` | The offending row number |

> **Both provider-derived strings are redacted. Neither is echoed verbatim, and the wire carries no
> separate parameter collection beside the statement.**

**The message field was added to this rule after the statement field, and the correction matters.** An
earlier revision of this section redacted the statement alone, on the reasoning that the message is
opaque display text a consumer must not parse. That reasoning was incomplete: **opacity describes how a
consumer may read a field, not what the provider puts in it.** What SQLite puts in the message routinely
includes the caller's own data — a uniqueness violation names the duplicated column, a constraint or type
failure quotes the offending value, and a bad identifier echoes the text the caller sent. The legacy
could publish that safely because it published nothing at all: PowerFramework is a library and the
message never left the process. Across this boundary it reaches a network peer.

The same correction applies to **every** provider-derived string on **every** contract, not only to this
payload, and the published fields are enumerated so the rule is checkable rather than implied:

| Contract | Field | Source | Redacted |
| --- | --- | --- | --- |
| all | `common.v1.DbError.sqlsyntax` | the generated statement | yes |
| all | `common.v1.DbError.sqlerrtext` | the provider's message | yes |
| C-07 | `ExecResponse.sql_err_text` | the transaction's message [`:L101`, `:L111`] | yes |
| C-07 | `OperationStatus.error_text` on a driver arm | the transaction's or the acquired payload's message [`:L72`, `:L101`, `:L111`] | yes |
| C-06 | `OperationStatus.error_text` on a driver arm | the transaction's message [`:L390`] | yes |
| C-05 | `OperationStatus.error_text` on a terminal stream status | whatever the worker raised | yes |
| C-08 | `GetSessionStateResponse.sql_err_text` | the transaction's message | yes |
| C-08 | `GetSessionStateResponse.sql_return_data` | a stored-procedure OUT value | **no** — it is a caller-requested RESULT, not a diagnostic |
| all | `OperationStatus.error_text` on a framework arm | a fixed sentence this codebase authored | passes through unchanged |

**The redaction is literal-scoped, which is why it costs no diagnostic value.** String literals, radix
literals, numeric literals and comment bodies become placeholders; every other byte is copied through
unchanged. So a driver message that quotes no value arrives byte for byte — `near "FROM": syntax error`
and `NOT NULL constraint failed: COMPANY.NAME` are unaltered — and every framework-authored Chinese
diagnostic the legacy synthesizes is unaltered too. An operator still learns which condition failed and
on which column; what no longer travels is the row data. Consumers must not treat a redacted field as
reproducing the provider's text byte for byte, because where the provider quoted a value it deliberately
does not.

**The shape is decided, not open.** Two shapes were permitted when this control was specified — one
redacted field, or a statement plus a separate parameter collection — and **the single redacted field is
the one implemented and published**: `Proto/common.v1.proto` declares `string sqlsyntax = 3` with the
redaction rule stated on the field, and `Errors/SqlRedactor.cs` is the one component that produces it.
There is no `parameters` member on `DbError` and none may be added without a contract revision. Earlier
revisions of this section and of [`SECRETS.md`](SECRETS.md) §6.3 described the alternative as still open;
that wording is withdrawn. The reason the single field won is that a parameter collection is itself the
sensitive data — separating a literal from its statement moves it, it does not protect it — so splitting
would have produced two fields to redact instead of one.

The reason is specific rather than precautionary. The legacy places the **complete generated
statement, including interpolated literal values, into that field** — visible directly at
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru:L110`, where the failing command
text is passed straight into the error event — and the legacy logger performs **no** redaction at
all. Every literal in a failing statement therefore reaches whatever consumes the error. Since one
of the newly created consumers is a network peer, the field is handled per the redaction rule
recorded in [`SECRETS.md`](SECRETS.md).

This is a narrowing of an in-process field on a **new** boundary, not a change to an existing wire
format — there was no wire format. The unredacted text remains available to the service's own
diagnostics under its own controls.

---

## 9. C-06 — `persistence.v1.UpdateService`

| | |
| --- | --- |
| **Identifier** | `persistence.v1.UpdateService` |
| **Transport** | gRPC — unary |
| **Served by** | Persistence |
| **Consumed by** | DataServices (and, through C-03's `Update`, ultimately Gateway) |
| **Definition** | `Proto/persistence.v1.proto` |
| **Primary legacy source** | `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru` (409 lines) |
| **Primary fixture** | `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` (37 lines) |
| **Status** | NEW |

This is the contract carrying optimistic concurrency. It is given the most room of the ten, because
every one of the behaviours below is one a straightforward implementation gets wrong, and several of
them are exercised by the primary fixture rather than being rare branches.

**All five RPCs the service declares**, in definition order. Locators are into
`n_cst_thread_task_sqlupdate.sru`:

| # | RPC | Request → Response | Kind | Behaviour the contract must preserve |
| ---: | --- | --- | --- | --- |
| 1 | `CreateUpdateTask` | `CreateUpdateTaskRequest` → `CreateUpdateTaskResponse` | unary | Creates a **server-held** update task and returns its handle — the wire form of legacy object creation |
| 2 | `ReleaseUpdateTask` | `ReleaseUpdateTaskRequest` → `ReleaseUpdateTaskResponse` | unary | The wire form of the legacy `Destroy` |
| 3 | `Reset` | `ResetUpdateTaskRequest` → `ResetUpdateTaskResponse` | unary | `of_reset` [`:L43`, `:L58-L72`]. **`E_BUSY` while running** [`:L60`], and it clears the descriptor array, the autocommit and multi-table flags, the source, the payload and the row count [`:L62-L69`] — so a reset task is not a partially configured one |
| 4 | `PrepareUpdate` | `PrepareUpdateRequest` → `PrepareUpdateResponse` | unary | Carries the repeated table descriptor and the multi-table switch (§9.1), and re-derives the update contract at run time rather than trusting the DataWindow definition (§9.2) |
| 5 | `Update` | `UpdateRequest` → `UpdateResponse` | unary | Applies the changeset. Returns **`Aborted` plus `common.v1.ConflictDetail`** on an optimistic-concurrency mismatch (§9.8), and carries the two result-inverting overrides of §9.6 |

**Only five, and that is the point:** this contract is small in surface and large in semantics. The two
task-lifecycle calls exist because the legacy update object is stateful — a caller prepares it, then
runs it — and everything difficult about C-06 lives inside `PrepareUpdate` and `Update`, which is why
the subsections below are given more room than the inventory.

### 9.1 `PrepareUpdate` carries a repeated table descriptor

The legacy descriptor is a structure declared in `n_cst_thread_task_sqlupdate.sru`, and `Tables[]`
is an **array** of it. Every locator in this subsection and in §9.2 and §9.4 through §9.7 is into
that object unless stated otherwise:

| Field | Type | Locator |
| --- | --- | --- |
| `name` | string | `:L11` |
| `updatablecolumns[]` | string array | `:L12` |
| `keycolumns[]` | string array | `:L13` |
| `identitycolumn` | string | `:L14` |
| `updatewhere` | long, **nullable** | `:L15` |
| `updatekeyinplace` | boolean, **nullable** | `:L16` |

Declared at `:L10-L17`, held as `TABLEDATA Tables[]` at `:L30`, cleared wholesale by the reset path
[`:L58-L72`, array replaced at `:L67`], and appended by the add path
`of_addupdatabletable(name, updatablecolumns[], keycolumns[], identitycolumn, updatewhere, updatekeyinplace)`
[`:L82-L96`, appending at `:L86`]. The add path rejects an empty table name, an empty
updatable-column list, or an empty key-column list with `RetCode.E_INVALID_ARGUMENT` [`:L84`].

> **Multi-table update from one DataWindow is a real legacy capability the contract must carry, not
> a theoretical one.**

It is gated by an explicit switch [`of_setmultitableupdate`, `:L265-L268`] and, when on, the worker
loops the descriptor array **in array order**, calling prepare and then update once per table and
stopping at the first failure [`:L356-L369`]; an empty descriptor array in multi-table mode is
`RetCode.E_INVALID_ARGUMENT` [`:L359-L363`]. `PrepareUpdate` therefore takes a **repeated**
descriptor and a multi-table flag, and the ordering of that repeated field is significant.

**The two nullable fields must be nullable on the wire.** The legacy writes each setting into the
DataWindow **only when it is not null** [`:L131-L133` for update-where and `:L135-L141` for
key-in-place]. A proto3 scalar with an implicit zero default cannot express "leave the DataWindow's
own setting alone", and conflating unset with zero would silently force a concurrency mode the
caller never asked for.

**Four of the six fields are identifier positions, and the contract admits them by shape.** `name`,
`updatablecolumns`, `keycolumns` and `identitycolumn` are concatenated into the DML the update path
generates. Every *value* on that path travels as a bound parameter and no dialect has a parameter form
for a table or a column, so those four fields are the only text a caller sends that reaches the engine
as SQL rather than as data. Each is therefore admitted only if it can occupy an identifier position —
letters, digits, the underscore and the dollar, hash and at signs, with `name` additionally allowed up
to two leading period-separated qualifier parts — and anything else is refused with
`RetCode.E_INVALID_ARGUMENT` **before any descriptor is recorded**, with a diagnostic that quotes none
of the caller's own text. An empty `identitycolumn` stays legal, because it means "no identity column"
[`:L127-L129`].

This is a boundary rule with no legacy counterpart, and it is one rather than an oversight on the
legacy's part: PowerFramework is a library, so the identifiers that reached a generated statement came
from a compiled DataWindow shipped inside the application and the only caller that could name a column
was code already in the same process. It is **not** an existence test — a well-shaped name the
definition does not declare still gets the oracle's own `E_INTERNAL_ERROR` with the invalid-column-name
diagnostic [`:L118-L122`], so "this table has no such column" and "no statement can carry that text as
a name" stay distinguishable — and a reserved word is admitted, because whether a provider accepts it
unquoted is the provider's answer to give. The same gate is applied again at the generator itself,
which is what covers a carrier whose column model came from a supplied `sql_syntax` rather than from a
descriptor; `ARCHITECTURE.md` §8.5 records both sites and why nothing is quoted.

### 9.2 The update contract is re-derived at run time, not trusted

`_of_updateprepare` [`n_cst_thread_task_sqlupdate.sru:L98-L170`] does not trust the DataWindow's
static definition. In order:

1. **It resets update, key and identity to off on *every* column** [`:L103-L108`], building the
   modification string column by column by ordinal.
2. It re-enables the updatable columns from the descriptor [`:L111-L114`].
3. It re-enables the key columns, **resolving each name through a describe call** and failing with
   `RetCode.E_INTERNAL_ERROR` and a diagnostic naming the column when resolution yields a non-positive
   identifier [`:L118-L122`]. The resolved identifiers are also retained for step 6 of
   [§9.4](#94-updatekeyinplaceno-and-the-legacys-own-documented-workaround) [`:L123`].
4. It marks the identity column, if one was named [`:L127-L129`].
5. It writes the two nullable settings and the update table name [`:L131-L143`].
6. **It applies the whole modification string in one call, and that call returns an error *string*, with
   empty meaning success** [`:L145-L149`]. Not a code — a string. The contract's failure detail
   therefore carries that text.

The reset-then-re-enable order is contract, not implementation detail: a column marked updatable in
the DataWindow definition but absent from the descriptor **is not updatable** for this operation. A
consumer sending a partial descriptor gets a partial update, deterministically.

### 9.3 What `updatewhere=1` requires of the wire

The only updatable DataWindow in the entire repository is
`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd`. Its table specification reads

```text
retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no  sort="age A salary A "
```

at `:L14`, and **all six of its columns carry `update=yes updatewhereclause=yes`** at `:L8-L13` —
the identifier column additionally `key=yes identity=yes` [`:L8`], then name [`:L9`], age [`:L10`],
address [`:L11`], salary [`:L12`] and birth [`:L13`].

`updatewhere=1` is the **"key and updateable columns"** concurrency mode: the generated update
statement's where-clause carries the key column **plus the original values of every updateable
column**. Combined with all six columns being marked, the optimistic-concurrency check spans **all
six columns' original values**.

> **Consequence: the payload must transmit, per row, both the current *and* the original value of
> every marked column.**

A naive rowset is insufficient, and this is the single most important message-shape decision in the
whole inventory. Concretely the row message carries, per marked column: the current value, the
original value, and a per-value null flag on each — because null and empty are distinguishable in
this concurrency comparison and collapsing them would match rows the legacy would not. It
additionally carries the row's own item status and each column's item status, because those select
which statement kind is generated.

This is what the buffer anti-corruption layer exists to carry, and it is why the legacy result
carrier matters: the legacy carrier derives from a datastore, so it **is** a DataWindow, complete
with buffers and item statuses, not a flat result set.

The fixture also exercises the expression engine even here: its footer computation is
`expression="sum(salary for page)"` [`:L27`], a page-scoped aggregate. And the fixture's declared
column types disagree with the evidenced schema in **four** places, not three — `name`, `address`,
`salary` and `birth`. The one easiest to overlook is `name`, which the DataWindow bounds at
`char(100)` [`:L9`] over a column the DDL declares as unbounded `TEXT NOT NULL`
[`w_test_sqlite.srw:L465`], making the DataWindow *stricter* than the schema there while `address`
makes it *looser*. All four mismatches are **preserved as defects** (C-B) and are tabulated with their
locators and the direction of each disagreement in [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.4.

### 9.4 `updatekeyinplace=no` and the legacy's own documented workaround

`updatekeyinplace=no` means a key change is performed as **delete-plus-insert** rather than an
in-place update — and it is precisely the trigger for a workaround the legacy documents *against
itself*.

The comment at `n_cst_thread_task_sqlupdate.sru:L151-L154` is explicit: when the requested key field
is not marked as a key, the modified state will not generate the delete and insert statements, so
the internal modified state must be force-refreshed to compensate. The implementation follows at
`:L155-L167` and is gated on the **runtime** value of the key-in-place setting read back through a
describe call [`:L155`], not on the descriptor. Within that gate it walks the rows whose **row
status** — the status at column index zero, which is the legacy's row-status convention — is the
modified value [`:L160`], then each resolved key column whose own status is modified [`:L162`], and
then:

```text
Data.Object.Data[nRow, keyColumnId] = Data.Object.Data[nRow, keyColumnId]
```

at **`:L163`** — **it assigns each modified key column to itself**, purely to flip its item status.

There is no .NET analogue for a self-assignment that mutates hidden state. The port must therefore
model **original-value and status tracking explicitly** and reproduce the same statement generation
from that explicit model. The contract's part of that obligation is the per-column status field of
[§9.3](#93-what-updatewhere1-requires-of-the-wire): without it, the far side has nothing to flip.

> **And because the primary fixture sets `updatekeyinplace=no` itself [`dw_sqlite.srd:L14`], this
> path is exercised by the fixture. It is not a rare branch that can be deferred.**

### 9.5 What `Update` returns

On success the response carries the **inserted, updated and deleted counts** — the legacy fires them
together as one callback at `n_cst_thread_task_sqlupdate.sru:L247` — plus the **identity column**
and the **two identity value arrays** of
[§9.7](#97-the-identity-round-trip-and-its-inverted-iteration), which the legacy delivers through a
separate callback taking two `ref` array out-parameters at `:L243`.

The two callbacks are folded into one response message because the boundary makes them a single
reply, but the identity block remains **optional**: the legacy fires it only when at least one
identity value was collected [`:L242`], and only when rows were inserted at all [`:L215`] and an
identity column was found [`:L226`]. A consumer must treat an absent identity block as "none
collected", not as an error.

The update payload itself travels as a **changeset**, not as a full state: the worker applies the
caller's changeset to its own carrier [`:L336`] and the caller supplies it as a blob with a row
count [`:L74-L77`]. Two consequences the contract records: the sort condition is deliberately
cleared before the update so that rows are submitted in the order they were copied [`:L334`], and a
changeset that reports "no data" when the row count is zero is treated as **success**, not as
failure [`:L339-L342`] — while a genuine changeset failure is `RetCode.E_INVALID_DATA`
[`:L343-L345`].

### 9.6 Two overrides that invert the obvious result

Both must be documented, because a straightforward port gets both backwards.

**First: success is the value 1, not the zero of the return-code algebra.** The gate on the whole
success path is `if rtCode = 1 then` [`n_cst_thread_task_sqlupdate.sru:L214`], with anything else
falling to `RetCode.E_DB_ERROR` [`:L249-L250`]. The update call itself is made with **accept-text
true and reset-flag false** [`:L204`] — so the caller owns the carrier's state afterwards, and the
contract must not imply that the server reset it.

**Second: a claimed success is defensively rewritten into a failure.** At `:L208-L210`, if the
transaction's own SQL code indicates an error while the update call claimed success, the result is
overwritten with failure:

```text
if TransObject.SQLCode = -1 and rtCode = 1 then rtCode = -1
```

**An implementation that trusts the update call's own return value will report success on a failed
update.** The contract therefore specifies that the result is the *reconciled* one, and that a
consumer may not reconstruct it from any single field.

Two further result determinations sit around these:

- **An empty or placeholder update table** — empty, `!`, or `?` — synthesizes a database-error event and
  returns `RetCode.E_DB_ERROR` before any statement is generated [`:L188-L193`].
- **A vetoed before-update hook discriminates by consulting the transaction's own failure predicate**: a
  veto *with* a transaction failure yields a database error, while a clean veto yields
  `RetCode.CANCELLED` [`:L195-L202`]. The contract carries both outcomes as distinct statuses, because a
  consumer must be able to tell a deliberate cancellation from a failure. Cancellation is also checked
  before and after the update itself [`:L182`, `:L212`].
- **The after-update hook fires with the result** [`:L206`], before the reconciliation of `:L208-L210` —
  so a hook observes the unreconciled value. That ordering is preserved.

### 9.7 The identity round-trip, and its inverted iteration

The identity column is **discovered at run time, not taken from the descriptor**
[`n_cst_thread_task_sqlupdate.sru:L217-L225`]: the lowercased update table name plus a dot is
prefix-matched against each column's database name, with a **first-wins fallback** that accepts the
first identity column found if no name matched [`:L221`]. Values are then collected for
newly-modified rows:

- from the **primary** buffer **forward**, rows 1 upward [`:L228-L233`];
- from the **filter** buffer **backward**, `for nIndex = nCount to 1 step -1` at **`:L237`**.

The justification is in the source immediately above it, at `:L235`: the filter buffer's row order
is **inverted** relative to the data source that produced the changeset.

> **This is the most dangerous single line in the refactor for one-based-to-zero-based translation.
> It looks like a bug. It is not a bug. "Correcting" the direction produces wrong identity values
> that a row-count assertion would not catch** — the same number of values comes back, paired with
> the wrong rows.

The contract's role in protecting this is to keep the two arrays **separate and separately
ordered**: one for the primary buffer and one for the filter buffer, each in the legacy's collection
order, rather than one merged array. Merging them would destroy the only evidence a consumer has of
which order each was collected in.

**The identity field is `repeated`, because one `Update` can report one block per update table.**
`_of_Update` fires the identity callback at most once [`:L243`] — but the task that drives it calls it
**once per table**:

```text
if _bMultiTableUpdate then
    nCount = UpperBound(Tables)                    [:L358]
    for nIndex = 1 to nCount                       [:L364]
        rtCode = _of_UpdatePrepare(data,nIndex)     [:L365]
        rtCode = _of_Update(data)                  [:L367]
    next
```

and the caller-side proxy **appends** every firing to an ordered array rather than replacing anything
— `IDCOLDATA _idColDatas[]` [`n_cst_threading_task_sqlupdate.sru:L45`], `nIndex = UpperBound(_idColDatas) + 1`
[`:L73-L76`] — then **replays every element** when it writes the generated values back onto the
caller's DataWindow [`:L128`, `:L142-L195`]. Each block carries **its own column ordinal**, because
`_of_UpdatePrepare` re-describes the one carrier per table [`:L98-L145`], so the discovered identity
column legitimately differs between tables.

> A singular field would keep the **first** block and drop the rest — and the response would still look
> correct, because the counts are summed across tables [`n_cst_threading_task_sqlupdate.sru:L66-L68`]
> and so would not disagree with a truncated identity payload. That is the same class of defect as the
> inverted iteration above: **right count, wrong data**.

So the ordering obligation has **two levels and both bind**: the *blocks* travel in the order the
tables were declared on `PrepareUpdate`, and within each block the two arrays travel element for
element as collected. C-03's relay declares the identical repeated shape, so the cardinality cannot be
narrowed on the way to Gateway. An **empty** list is the "none collected" reading — the emit guard is a
guard on the *call*, not on the contents [`:L242-L244`] — so absence is expressed by the list being
shorter, never by a placeholder block.

### 9.8 On concurrency mismatch: `Aborted`, and no silent overwrite

> **On an optimistic-concurrency mismatch the contract returns gRPC `Aborted`** — the canonical
> mapping to HTTP `409` — **with a conflict detail carrying the current row state.** Gateway's REST
> projection surfaces it as **HTTP `409`** with the same payload (C-09). **Callers implement an
> explicit retry-or-surface policy. There is no silent overwrite anywhere in the system.**

The conflict detail carries the buffer selector and row number of the offending row, the current
server-side values of the marked columns, and the values the caller believed were current. That is
what lets a caller decide between retrying against the new state and surfacing the conflict, and it
is why the detail is a structured message rather than a message string.

`Aborted` is chosen over `FailedPrecondition` deliberately: the operation may succeed if retried at
a higher level after the caller re-reads, which is exactly the semantic distinction between the two
statuses.

**How the detail actually reaches the caller, stated because a status code alone cannot carry it.**
A gRPC error carries a code and a message string; a structured payload has to travel in a trailer,
and a trailer is only interoperable if every participant agrees on its key and its type. The contract
therefore declares that agreement **in the schema rather than in prose**, as a method option on every
RPC that can return a conflict:

| Element | Value | Why exactly this |
| --- | --- | --- |
| Trailing metadata key | `powerframework-rich-error-v1-bin` | The **`-bin` suffix is mandatory**: gRPC transports a `-bin` key as raw binary and base64-encodes it on the wire, while a non-`-bin` key is ASCII-only and would corrupt a serialized protobuf payload. The `v1` segment is part of the key so a future incompatible payload can be introduced under a new key rather than by reinterpreting this one |
| Status code | `ABORTED` (10) | The canonical mapping to HTTP `409` |
| Payload type | `common.v1.RichErrorTrailer` | A `oneof` over the conflict detail and the database-error detail, plus the numeric return code, so one mechanism serves both error shapes |

Two RPCs in the whole system declare that binding — C-06's `Update` and C-03's `Update` — and they
declare **identical** values, which is what makes the DataWindow-facing projection able to pass a
conflict through unchanged. The binding is expressed as a custom `MethodOptions` extension in
`common.v1` rather than through `google.rpc.Status`, because the Google RPC status protos are not part
of this solution's dependency closure and adding a package for one message would widen the closure for
no behavioural gain; the extension field numbers sit in the 50000–99999 range reserved for in-house
use.

---

## 10. C-07 — `persistence.v1.CommandService`

| | |
| --- | --- |
| **Identifier** | `persistence.v1.CommandService` |
| **Transport** | gRPC — unary |
| **Served by** | Persistence |
| **Consumed by** | DataServices |
| **Definition** | `Proto/persistence.v1.proto` |
| **Primary legacy sources** | `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru` (116 lines); `ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru` |
| **Status** | NEW |

**All six RPCs the service declares**, in definition order. Locators are into
`n_cst_thread_task_sqlcommand.sru`:

| # | RPC | Request → Response | Kind | Behaviour the contract must preserve |
| ---: | --- | --- | --- | --- |
| 1 | `CreateCommandTask` | `CreateCommandTaskRequest` → `CreateCommandTaskResponse` | unary | Creates a **server-held** command task and returns its handle — the wire form of legacy object creation |
| 2 | `ReleaseCommandTask` | `ReleaseCommandTaskRequest` → `ReleaseCommandTaskResponse` | unary | The wire form of the legacy `Destroy` |
| 3 | `Reset` | `ResetCommandTaskRequest` → `ResetCommandTaskResponse` | unary | `of_reset` [`:L27`, `:L32-L38`]. **`E_BUSY` while running**, and **it restores `AC_OFF` on success** — so a reset task returns to the no-transaction-handling commit mode rather than keeping whatever was set |
| 4 | `SetAutoCommit` | `SetCommandAutoCommitRequest` → `SetCommandAutoCommitResponse` | unary | `of_setautocommit` [`:L28`]. **Three-valued, not boolean** — see [§10.2](#102-the-commit-mode-is-three-valued-not-boolean) |
| 5 | `SetSql` | `SetCommandSqlRequest` → `SetCommandSqlResponse` | unary | `of_setsql` [`:L29`]. **An empty statement is `E_INVALID_SQL`, and the legacy checks it twice** — the redundant second check is preserved rather than tidied away |
| 6 | `Exec` | `ExecRequest` → `ExecResponse` | unary | Executes a command returning no result set, preserving all **four** of the behaviours the AAP names for this verb: positional binding, **the leading-`@` prefix execution mode**, batch execution and error-text retrieval — [§10.1](#101-exec-and-the-four-behaviours-it-preserves) |

### 10.1 `Exec` and the four behaviours it preserves

`Exec` executes a command that returns no result set. AAP §0.4.3 names four behaviours for this verb, and
all four are carried:

- **Positional `?` binding.** Arguments are supplied positionally and substituted into the statement in
  order. The legacy demonstrates it directly:
  `Exec("… VALUES (?, ?, ?, ?, ?)", "Paul", 32, "California", 20000, "1999-05-08")` at
  `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L398-L400`, and a single-placeholder update at
  `:L313`. Binding is applied before execution and a binding failure yields
  `RetCode.E_SQL_BIND_ARG_FAILED` [`n_cst_thread_task_sqlcommand.sru:L81-L86`].
- **The leading-`@` prefix execution mode** — §10.1a below.
- **Multi-statement batch execution**, demonstrated at `w_test_sqlite.srw:L381-L392`, where a batch is
  submitted and rolled back as a unit on failure.
- **Error-text retrieval.** The result carries the numeric code and the driver text, drawn from the
  accessors the legacy exposes for exactly this purpose [`n_sqlite.sru:L26-L29` — row count, code, driver
  code, and error text].

### 10.1a The leading-`@` prefix execution mode, and the two meanings behind one character

A statement whose **first character is `@`** selects the statement-caching execution mode. The prefix
travels inside the statement string — `ExecRequest.sql` or `SetCommandSqlRequest.sql` — and is
deliberately not hoisted into a separate boolean field, because it is the statement text that selects the
mode and splitting it would create two sources of truth for one decision.

**The character carries two unrelated legacy meanings, and telling them apart is the whole subtlety.**
Conflating them is how this section previously came to declare the mode absent from a verb that carries it:

- a **DataWindow-object selector**, on the *retrieve* verb —
  `if Left(sql,1) = "@" then ds.DataObject = Mid(sql,2)` [`n_cst_thread_trans.sru:L309`], where the rest
  of the string is an object name rather than SQL. That verb is C-05's, and its managed translation is
  C-05's own `data_object` field rather than a prefix, so **this** meaning is genuinely not on C-07; and
- a **statement-caching execution mode** on the SQLite binding's own `Exec` — the demonstration at
  `w_test_sqlite.srw:L398` is written `"@INSERT INTO …"` with `?` placeholders and five bound values,
  inside a ten-iteration loop [`:L396-L406`], and the comment at `:L397` records exactly what the prefix
  does: 语句缓存 — statement caching — 空间换时间, trading space for time. That is `n_sqlite`
  [`n_sqlite.sru:L32-L43`], and it is a **command**.

**The second meaning is C-07's**, and three other things on this service come from the same object: the
four driver accessors `ExecResponse` publishes are `n_sqlite`'s [`n_sqlite.sru:L26-L29`] and so is the
recorded eleven-argument ceiling [`:L33-L43`]. C-07 is therefore the *union* of the two command surfaces —
the transaction object's `of_Exec` [`n_cst_thread_trans.sru:L219-L238`] and the SQLite binding's `Exec` —
and reading only the first half is what produced the earlier claim that the mode was not on this verb.
A producer sending the documented prefix has its statement **run**, not refused with a syntax error.

The grammar, exactly, so a producer can predict what the provider receives:

| # | Rule | Consequence |
| ---: | --- | --- |
| 1 | The selector is **position one**, with no whitespace tolerance — the oracle's own `Left(sql,1) = "@"` | `" @INSERT …"` does **not** select the mode and is forwarded whole, leading space and all |
| 2 | **Exactly one character** is removed — the oracle's own `Mid(sql,2)` | `"@@INSERT …"` yields `"@INSERT …"`; a doubled selector is not an escape sequence, and the survivor reaches the provider and fails there |
| 3 | The **remainder** is what every observer sees | The before- and after-command hooks, the `DbError` statement field, a SQL preview and any log record all carry the statement the provider ran — never the selector |
| 4 | A **lone `"@"`** is answered by the transaction, not by a statement-shaped code | It is not the empty string, so it passes both emptiness guards [`:L45`, `:L65`]; the empty remainder is then refused with `E_INVALID_ARGUMENT` [`n_cst_thread_trans.sru:L219`] — the same route a null statement takes |
| 5 | The mode **composes** with everything else on the request | Compatible with positional binding, with a multi-statement batch, and with all three autocommit values, because none of those is decided by the statement's first character |

**No performance promise is made or implied (AAP §0.8.5).** The repository publishes no latency budget, no
throughput target and no availability commitment anywhere. The mode changes how often the provider parses
the statement text and nothing a caller can observe in the *result*, so a storage engine that keeps no
prepared form may decline the request and execute immediately. What is guaranteed is that the mode is
**carried** rather than dropped, and that the selector never reaches the provider as statement text.

The shipped SQLite engine honours it by retaining the prepared command, keyed on the **placeholder** form
of the statement so one retained entry serves every value set — which is what makes the oracle's
ten-iteration loop one retention and nine matches. The store is bounded and evicts the least recently
matched entry, because the legacy comment itself describes the mode as trading space for time, and it is
released whenever the connection closes, because a prepared statement cannot outlive the connection it was
prepared against.

### 10.2 The commit mode is three-valued, not boolean

The legacy autocommit setting has three values, declared at
`n_cst_thread_task_sqlcommand.sru:L16-L18`:

| Identifier | Value | Behaviour |
| --- | --- | --- |
| `AC_OFF` | 0 | No transaction handling by this call; the caller's session governs |
| `AC_ON` | 1 | Open a transaction and commit it on success — the legacy comment's own words at `:L17` |
| `AC_NATIVE` | 2 | **Do not open a transaction at all** [`:L18`]; the driver's own autocommit is switched on for the duration and back off afterwards |

The three arms are visibly distinct in the worker: `AC_NATIVE` toggles the connection's own
autocommit before and after and raises the commit event inline [`:L88-L90`, `:L95-L97`,
`:L105-L106`], `AC_ON` performs an explicit commit through the task's commit helper and reports a
commit failure through the error event [`:L98-L103`], and any other value falls to an explicit
rollback on failure [`:L107-L109`]. **A boolean field could not express the third value**, so the
contract carries the enumeration with those numeric values preserved.

**Both of the first two arms commit, and both raise the same event.** The inline raise on the
`AC_NATIVE` arm is not the only one: `AC_ON`'s commit helper raises `OnCommitted` itself when the
commit succeeds [`n_cst_thread_task_sqlbase.sru:L230-L237`], so the two arms differ in *how* they
commit rather than in *whether* they report it. `ExecResponse.committed` is therefore defined from
that event rather than from either arm:

| Mode | Statement | Commit | `committed` |
| --- | --- | --- | --- |
| `AC_NATIVE` | succeeded | performed by the driver as part of the statement | **true** |
| `AC_ON` | succeeded | succeeded | **true** |
| `AC_ON` | succeeded | **failed** | false — and the call reports the commit's failure, because the worker overwrites its own return code with it [`:L99-L102`] |
| `AC_OFF` | succeeded | nothing is committed | false |
| any | failed | not attempted | false |

Reporting a successful `AC_ON` commit as uncommitted would understate durability, and a caller
acting on that would re-commit work that was already durable.

An empty statement is rejected before anything else with `RetCode.E_INVALID_SQL` [`:L45`, and again
in the worker at `:L65-L68`].

### 10.3 The eleven-argument ceiling is a legacy limit, not a semantic one

The legacy `Exec` is overloaded from zero up to **eleven** positional arguments and stops there —
`n_sqlite.sru:L32-L43`, with `:L43` carrying `arg11` as the last. There is no twelve-argument form.

That ceiling exists because **PowerScript cannot forward an arbitrary-length argument list**, so the
surface has to be written out by hand one arity at a time. It is a language limitation, not a
statement about what the database accepts.

> **The contract therefore carries a repeated argument field with no fixed upper bound. Exceeding
> eleven arguments is not a behavioural regression, because no legacy behaviour depends on the
> eleventh being the last** — the twelfth simply had no overload to call.

The ceiling is nonetheless recorded, because it explains an otherwise-puzzling shape in the legacy
and because a parity test replaying a legacy recording will never contain more than eleven.

---

## 11. C-08 — `persistence.v1.TransactionService`

| | |
| --- | --- |
| **Identifier** | `persistence.v1.TransactionService` |
| **Transport** | gRPC — unary |
| **Served by** | Persistence |
| **Consumed by** | DataServices |
| **Definition** | `Proto/persistence.v1.proto` |
| **Primary legacy sources** | `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs`; `n_cst_thread_trans.sru` (545 lines); `n_cst_thread_trans_pool.sru` |
| **Status** | NEW |

### 11.1 Method surface

Session lifecycle, commit control, state inspection and one syntax utility — the action-oriented
vocabulary the legacy exposes in its prototype block at `n_cst_thread_trans.sru:L72-L101`. **All
thirteen RPCs the service declares**, in definition order, with locators into that object:

| # | RPC | Request → Response | Legacy anchor | Behaviour the contract must preserve |
| ---: | --- | --- | --- | --- |
| 1 | `BeginSession` | `BeginSessionRequest` → `BeginSessionResponse` | `of_connect` [`:L73`, `:L111`] | Resolves the descriptor **through the reference-counted pool** and returns a session handle. **A broken session is rejected up front with `RetCode.E_INVALID_TRANSACTION`** [`:L113`] |
| 2 | `EndSession` | `EndSessionRequest` → `EndSessionResponse` | `of_disconnect` [`:L74`] | Releases the pool reference. **Whether the session is parked or closed is the pool's decision, not the caller's** — see [§11.3](#113-pool-lifecycle-reference-counting-and-a-seamed-clock) |
| 3 | `GetTransactionData` | `GetTransactionDataRequest` → `GetTransactionDataResponse` | `of_gettransdata`, both overloads [`:L92`, `:L93`] | **Returns the view type, which has no password field at all** — this is the mechanism enforcing the write-only rule of [§11.2](#112-the-transaction-descriptor-mirrors-the-legacy-structure-field-for-field), rather than a redaction applied on the way out |
| 4 | `SetAutoCommit` | `SetTransactionAutoCommitRequest` → `SetTransactionAutoCommitResponse` | the session autocommit flag [`:L88`] | **A plain boolean — not C-07's three-valued commit mode.** The two must not be conflated: the same words name different types on the two contracts. **And it can answer `E_DB_ERROR`, which is a port-created arm rather than a legacy one:** moving out of autocommit obliges the server to open an *explicit* transaction, because .NET has no implicit one after connecting, so the provider can refuse the transition — `OK` therefore means the mode is in force **and** a transaction is open, and the statement doors refuse rather than apply a write outside the transaction the mode promised |
| 5 | `AutoCommit` | `AutoCommitRequest` → `AutoCommitResponse` | `of_autocommit` [`:L88`, `:L370-L380`] | Commit-or-rollback decided **by statement status**, preserved as three arms |
| 6 | `Commit` | `CommitRequest` → `CommitResponse` | `of_commit`, both overloads [`:L79`, `:L89`, `:L240-L257`] | **Auto-rollback defaults to TRUE when unset.** **Returns `RetCode.FAILED` when autocommit is already on** [`:L240`] and `E_INVALID_TRANSACTION` when broken; on failure it rolls back if the auto-rollback flag is set [`:L250-L252`] |
| 7 | `Rollback` | `RollbackRequest` → `RollbackResponse` | `of_rollback` [`:L76`] | **The same two guards as `Commit`, returning different codes** — the asymmetry is legacy behaviour and is preserved |
| 8 | `IsConnected` | `IsConnectedRequest` → `IsConnectedResponse` | `of_isconnected` [`:L77`] | **Reports whether the answer came from the liveness cache or from a live probe**, so a caller can tell a cached yes from a verified one |
| 9 | `GetDatabaseType` | `GetDatabaseTypeRequest` → `GetDatabaseTypeResponse` | `of_getdbtype` [`:L86`] | **Two values only, and SQLite is not among them** [`:L60-L61`] — the selector chooses a paging text generator, not a provisioned engine ([§8.1](#81-one-storage-engine-is-provisioned-and-it-is-sqlite)) |
| 10 | `GetSessionState` | `GetSessionStateRequest` → `GetSessionStateResponse` | `of_isfailed`, `of_issucceeded`, `of_isbroken` [`:L83`, `:L84`, `:L100`] | Collapses three legacy predicates into one call, **carrying the five status values they are computed from** rather than only the three answers |
| 11 | `ClearState` | `ClearStateRequest` → `ClearStateResponse` | `of_clearstate` [`:L87`] | Clears the accumulated statement status |
| 12 | `SetBroken` | `SetBrokenRequest` → `SetBrokenResponse` | `of_setbroken` [`:L99`] | **One-way until a successful reconnect** — a session cannot be marked un-broken directly |
| 13 | `GridSyntaxFromSql` | `GridSyntaxFromSqlRequest` → `GridSyntaxFromSqlResponse` | `of_gridsyntaxfromsql`, both overloads [`:L81`, `:L90`] | **Reports failure through diagnostic text rather than a return code**, which is why the response carries that text as a field |

Thirteen is larger than the four-verb summary this section previously gave, and the extra nine are not
incidental: five of them (**3, 8, 9, 10, 12**) exist so a caller can *inspect* session state that was
free to read in-process and now has to be asked for over a boundary. That is the shape decomposition
imposes on a stateful object, and an inventory that lists only the verbs hides it.

Two quirks on the command path are also carried here because they are transaction-scoped: the
command execution veto and its discrimination [`:L224-L227`, matching the pattern of
[§9.6](#96-two-overrides-that-invert-the-obvious-result)], and the fact that **a driver code of 100
is treated as success, not as an error** [`:L233`] — a "nothing found" outcome is not a failure.

### 11.2 The transaction descriptor mirrors the legacy structure field for field

`transactiondata.srs:L3-L13` declares nine fields, and the contract carries all nine:

| # | Field | Handling |
| --- | --- | --- |
| 1 | `dbms` | Carried. Note that it is also the **dialect selector**: the legacy resolves the paging dialect by testing this string, defaulting to the first dialect when the test does not match [`n_cst_thread_trans.sru:L356-L361`]. The selector values themselves are `DBT_MSSQL` = 0 and `DBT_ORACLE` = 1 [`:L60-L61`], and they are preserved. This selects a **text generator**, not a provisioned engine — see [§8.1](#81-one-storage-engine-is-provisioned-and-it-is-sqlite) |
| 2 | `servername` | Carried |
| 3 | `database` | Carried |
| 4 | `logid` | Carried |
| 5 | **`logpass`** | **Write-only. Never echoed in a response, never logged.** See below |
| 6 | `dbparm` | Carried, and it carries the two connection flags of [§11.4](#114-the-two-connection-parameter-flags) |
| 7 | `lock` | Carried |
| 8 | `autocommit` | Carried — but **not moved by either default accessor**; see the seven-of-nine note below. It is also **force-cleared at the task level**, `_transData.AutoCommit = false` [`n_cst_thread_task_sqlbase.sru:L119`], so a descriptor's value does not survive into a task. Per-statement commit policy is C-07's `AutoCommitMode`, and direct control of the transaction object's own flag is C-08's `SetAutoCommit` |
| 9 | `userparm` | Carried — but, like `autocommit`, **not moved by either default accessor**; see the seven-of-nine note below |

> **The request side carries all nine fields. The RESPONSE side is a different message that
> structurally forbids three of them**, and the difference is deliberate: `GetTransactionData`
> returns a *view*, not a mirror.

This is a deliberate narrowing, and the honest statement of it matters — which means being precise
about what the legacy accessors actually move. **The two default accessors each copy SEVEN of the nine
fields, not all nine.** `of_settransdata` [`n_cst_thread_trans.sru:L343-L352`] and `of_gettransdata`
[`:L402-L416`] both move `dbms`, `servername`, `database`, `logid`, `logpass`, `dbparm` and `lock`, and
**touch neither `autocommit` nor `userparm`**. The request side of this contract still carries all nine,
because it is the *structure* that is mirrored and a consumer setting `autocommit` through the
descriptor is expressing something the structure can hold — the asymmetry is recorded so nobody
"discovers" it later and deletes two fields to match the accessors. `persistence.v1.proto` states the
same asymmetry at `TransactionDescriptor`.

Two consequences follow, and they pull in opposite directions:

- **`logpass` genuinely does round-trip, which is why the narrowing is needed at all.** It is one of the
  seven: read in at `n_cst_thread_trans.sru:L349` and written back out at `:L414`. On a wire, echoing
  any of the three fields below would place credential material in a response body and in every
  recording of one.
- **A subclass may still populate what the default accessor does not.** `of_gettransdata` fires
  `Event OnGetTransData(ref data, ref errInfo)` before copying anything [`:L404`], and that override
  receives the structure by reference — so an application-supplied handler can fill `autocommit`,
  `userparm`, or any other field, and when it returns `1` the seven-field copy is skipped entirely
  [`:L404-L407`]. The response-side reservations below therefore have to hold against a populated
  structure as well as against the default path, which is precisely why they are structural rather than
  a rule about what the default accessor happens to write.

| Slot | Field forbidden on the response | What it can contain |
| --- | --- | --- |
| 5 | `logpass` | The database account's password, in clear |
| 6 | `dbparm` | Provider-specific connection parameters, which in practice carry credentials — the legacy's own parameter string is where `DisableBind` and `NCharBind` live [§11.4](#114-the-two-connection-parameter-flags), alongside whatever else a deployment puts there |
| 9 | `userparm` | An application-defined opaque string, which the contract cannot constrain and therefore cannot vouch for |

**The three are `reserved` by both field number and field name in the response message.** That choice
is the point: prose asking a server to sanitize `dbparm` before returning it is a request for good
behaviour, and the first straightforward implementation — copy the descriptor, clear `logpass` — would
satisfy the prose while leaking two fields. A reserved slot cannot be reintroduced by a later edit
without the compiler objecting, and one structural rule closes all three legacy getters at once.

What the response carries **instead** of the opaque parameter string is a typed allowlist: a
`ConnectionParameterFlags` message with the two flags of [§11.4](#114-the-two-connection-parameter-flags)
and nothing else. A consumer that needed to know whether bind variables are disabled — which is
behaviourally significant, since it decides whether the generated SQL interpolates literals — gets
that answer without the credential material that used to be its only carrier.

Because this contract is new and no legacy wire format exists
([§1.1](#11-there-is-no-prior-art-to-check-the-design-against)), narrowing here changes no
observable protocol; it changes only what a **newly created** boundary is willing to emit. The
equivalent rule for the connection URI's optional credential parameter, and for every other field of
this kind, is in [`SECRETS.md`](SECRETS.md). **No value of any such field appears in this
document.**

### 11.3 Pool lifecycle: reference counting, and a seamed clock

The legacy pool is **reference counted**, and the contract's session lifecycle maps onto it
directly. Its surface is add-reference, remove-reference, get, release, exists, remove-all and
collect [`n_cst_thread_trans_pool.sru:L64-L70`].

Removing a reference decrements the count [`:L91`], and when the count reaches zero one of two
things happens [`:L94-L114`]:

- if keep-alive is enabled and the connection is not broken, the entry is **parked** with an idle start
  time recorded from the process clock [`:L97`];
- otherwise the connection is disconnected, destroyed, and the entry compacted out of the array
  [`:L101-L114`].

Parked entries are reaped by an idle handler that runs a collection pass [`:L73`]. Idle expiry is
keyed on **two settings**, both read at initialization [`:L76-L79`]: a keep-alive switch, and an
expiry time in seconds which is multiplied by 1000 and falls back to the built-in default of 30000
milliseconds [`:L53`] when it is zero or negative. A third setting names the transaction class
[`:L83`].

> **The clock must be seamed for deterministic tests.**

Stated as a requirement on the implementation, not as a description of it: the transaction-pool type this
paragraph specifies is not in the tree yet, and [`docs/PARITY.md`](PARITY.md) §5.1 carries that seam's
status as planned rather than present.

Three distinct clock reads must be substitutable, and a parity test needs to know about all three:
the idle start time above [`:L97`], the last-successful-connection stamp
[`n_cst_thread_trans.sru:L107`, `:L212`], and the **liveness cache** that reports a connection as
connected **without probing it** when the last success was recent [`:L196-L198`]. The last of these
is observable through the *absence* of a probe, so a test that does not control the clock cannot
reproduce it. The seam register is in [`docs/PARITY.md`](PARITY.md).

### 11.4 The two connection-parameter flags

The descriptor's connection-parameter string carries two flags the contract surfaces **explicitly as
fields** rather than leaving buried in a string, because both change observable statement
generation: a **bind-disabling** flag and a **national-character-binding** flag. The legacy extracts
both by regular expression from the connection-parameter string at
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129`, and the nesting there
is itself contract: national-character binding is only consulted **when binding is disabled**
[`:L127-L132`].

The bind-disabling flag is the one that matters for
[§8.3](#83-clause-modification-and-the-injection-site-named-plainly): when binding is disabled the
runtime interpolates literals into the statement rather than using bind variables, which is the
mechanical root of the injection exposure. The mechanism is analysed in
[`ARCHITECTURE.md`](ARCHITECTURE.md) §8.5. Both flags remain settable through the string as well, so
a caller supplying only the string is not broken.

---

## 12. C-09 and C-10 — the ingress and readiness contracts

### 12.1 C-09 — `gateway.v1` REST ingress

| | |
| --- | --- |
| **Identifier** | `gateway.v1` |
| **Transport** | REST, OpenAPI-described |
| **Served by** | Gateway |
| **Consumed by** | External clients |
| **Definition** | `OpenApi/gateway.v1.yaml` ([§2.3](#23-where-the-definitions-live)) |
| **Status** | NEW — and this is the system's **first-ever ingress** |

| Route | Auth | Purpose |
| --- | --- | --- |
| `/health` | **anonymous** | Aggregated readiness; see [§12.2](#122-c-10--health-and-readiness) |
| `/v1/ping` | **token required** | The standing proof that the boundary is authenticated; returns `401` without a token |
| `/v1/capabilities` | token required | Projects the capability gate. The eight capability bits and their semantics are [`ARCHITECTURE.md`](ARCHITECTURE.md) §6's subject; the contract carries them by their preserved identifiers, of which `Enums.INIT_FLAG_ENABLE_SQLITE` is the only one with an in-scope consumer |
| `/v1/datawindow/**` | token required | The REST projection of [C-03](#6-c-03--dataservicesv1datawindowservice) and [C-04](#7-c-04--dataservicesv1columnexpressionservice) |

**The projection is a translation layer, not a second implementation.** Gateway holds no DataWindow
logic, no expression engine and no validator; it maps requests onto the gRPC contracts and maps
statuses back. The status mapping is the substantive part:

| gRPC status from C-03 / C-04 | HTTP status | Note |
| --- | --- | --- |
| `OK` | `200` | |
| **`Aborted`** | **`409`** | An optimistic-concurrency conflict, carrying the conflict detail of [§9.8](#98-on-concurrency-mismatch-aborted-and-no-silent-overwrite) unchanged |
| `InvalidArgument` | `400` | Carries the originating `RetCode` value so the specific legacy validation is identifiable |
| `NotFound` | `404` | |
| `Unauthenticated` | `401` | |
| `PermissionDenied` | `403` | |
| `Unimplemented` | `500` | An upstream reporting a method its own contract publishes as unimplemented — deployment or version skew, carrying `E_NO_IMPLEMENTATION` on `retCode`. **Never `501`**: that status belongs exclusively to the four reserved routes of [§13](#13-the-four-reserved-gateway-extension-points), and no projected operation declares it |
| `ResourceExhausted` | `429` | A capacity ceiling declining to take more work, carrying the legacy `E_BUSY` code. A refusal rather than a fault |
| `Unavailable` | `503` | The upstream answered that it is not currently serving — distinct from `502`, where it answered nothing at all |
| `DeadlineExceeded` | `504` | The deadline this service sets on the outbound call elapsed |
| `AlreadyExists` | `409` | Shares the status with the concurrency conflict, and stays distinguishable from it by problem type, title and `retCode` |
| `Internal` / `Unknown` | `500` | With the statement field redacted per [§8.6](#86-errors-and-the-two-fields-that-must-be-redacted) |
| *(no gRPC call)* | **`413`** | The ingress refused the request body for exceeding the configured size bound, before any upstream was called and before the body was read. Not a projection of any gRPC status: it is Gateway's own bound, and it is declared on the **thirty-six** projected operations that read a body rather than on all thirty-nine, because an operation that reads none cannot produce it. Carries `E_INVALID_ARGUMENT` (-3). The configured limit is deliberately published nowhere: that a bound exists is contract, its value is a property of a deployment |

Each operation declares the responses it can **actually** produce rather than the whole table, because a
status every generated client must branch on but no operation can return hides the real surface. That rule
cuts both ways, and applying it honestly settles every row of the table above.

**The declared surface is identical across all thirty-nine projected operations except for the two
statuses that are conditional on reading a body, and getting there closed a defect in the harder direction
of that rule.** A status declared but unreachable is noise; a status
**reachable but undeclared** leaves a generated client with no branch for a response it will receive, and it
survives review precisely because nothing about it fails until the response arrives. Two statuses were in
that second class, and in both cases the suppression was deliberate and its reasoning was right about a
narrower question than the one it decided:

- **`409` was declared on `updateDataWindow` alone.** The reasoning — that the optimistic-concurrency check
  belongs to the update third of the triple and to nothing else — is correct, and it decides where a
  conflict **detail** can come from, not where the **status** can. Three arms answer `409`, and two of them
  are operation-independent: an upstream `Aborted` whose detail did not decode, and the in-band `E_RETRY`.
  So thirty-eight operations could return the one status a caller must branch on in order to construct a
  retry, while publishing that they could not. All thirty-nine now declare it, referencing the same
  `ConflictProblemDetails` schema, which stays truthful on all of them because its `conflict` member is
  **optional** — declaring it publishes "a conflict member may be present", never "one will be". The update
  remains the only operation that populates it, and its summary is the only one promising it.
- **`404` was suppressed on the two session-opening operations**, on the reasoning that neither carries a
  prior session identifier to fail to resolve. True of the identifier, false of the status: the not-found
  family is wider than one parameter. `openValidationSession` refuses a `datawindowHandle` **in its body**
  that no DataWindow resolves and answers `E_INVALID_HANDLE`; `openExpressionSession` answers
  `E_OBJECT_NOT_FOUND` for a name no host binds and `E_NOT_EXISTS` for a session that closed underneath the
  open. All three are `404` in the in-band map, so both operations really produced a status neither declared.

**A refused write now names the offending column, and closing that gap needed a producer rather than a new
status.** `400` with `retCode` `E_INVALID_DATA` was already the right answer for a row the caller can
correct — a row omitting a value for a `NOT NULL` column, for instance — but the body named nothing: fixed
prose saying the data was refused, the numeric outcome, the upstream, a trace identifier. The failing column
reached the ingress and was discarded there, because the in-band failure renderer deliberately attaches no
part of the upstream message. So the caller was told its payload was wrong and not told which part, and the
corrective action was available to the caller and to nobody else. Two problem extension members now carry
the **identity** of the refusal, one per upstream path:

- **`dbError`** — the storage engine's refusal, as the closed `DbError` shape: the provider code, the
  condition line (`NOT NULL constraint failed: COMPANY.AGE`, which the upstream redaction rule keeps legible
  precisely because a column name is schema metadata), the buffer and the one-based row. **The member was
  published by this contract before anything produced it**, which is worse than an absent member because it
  documents a capability the system did not have; it now has a producer. Its `sqlsyntax` member is emitted
  **empty** rather than omitted — the shape is closed and requires it — so the generated statement never
  crosses even though the upstream redacts it, because a disclosure control that depends on another service
  having got it right is not a control at the only external ingress.
- **`validationErrors`** — the DataWindow service's own row validator refusal, as an array of
  `RowValidationIdentity`: buffer, one-based row, column name, column ordinal, column type. A deliberate
  **narrowing** of `dataservices.v1.RowValidationError`, dropping that message's structured-error field so
  that no upstream prose reaches a caller through the member added to keep prose out of the body. Bounded at
  32 elements by the gateway rather than by whatever an upstream produced, and each relayed string is
  length-bounded, because an unbounded relay is a response size the caller controls.

**The line both members are drawn on is identity versus data.** A column name, ordinal and type are schema
metadata; a buffer and row ordinal are the caller's own addressing of the row it just sent. A **value** is
different in kind — it may not have originated with this caller, and the generated statement interpolates
literals — so identity crosses and values do not. Neither member is fabricated when the upstream reported
no identity: absence keeps meaning "not told", never "told there was none".

**Exactly one status remains conditional, and it is the `413` — on the three operations that bind no
request body at all.** The `413` reports a body refused at the ingress size bound before it was read, so an
operation that reads no body can never produce one. That this is genuinely unreachable there was measured
rather than reasoned: a 9 MiB body sent to `POST /v1/datawindow/retrieve` answered `413`, and the same body
sent to `DELETE /v1/datawindow/sessions/{sessionId}` answered `200`, because a body a route never reads is
never measured against the bound. Declaring `413` on those three would publish a status they can never
answer, which is the defect this section refuses in the other direction.

🔴 **The `400` is NOT in that company, and an earlier revision of this section said it was.** It read that
the `400` too was conditional "because three operations bind no request body at all", reasoning that a
status which reports an unbindable body cannot arise where there is no body to bind. That reasoning is
sound about *bodies* and wrong about *operations*: a bodiless operation still binds a **parameter**, and a
parameter it declares `required` is one it can be asked without. All three of these operations carry
`sessionId` — two in the path, one in the query — so all three have a refusal of their own to publish, and
all three now declare it. The query-bound one is where the cost of the older reading was actually paid: a
runtime probe of the deployed stack drove `GET /v1/datawindow/event-gate` with no query string and got a
`500` out of the framework's own binder, because a document that declared no `400` had been implemented as
though it could not need one. Both statuses are therefore stated the same way now — declared where the
mapping can produce them, absent where it cannot — and the difference between them is that a route can
decline to read a body and cannot decline to be missing a parameter. Both directions are asserted rather
than assumed — against the authored contract by
`GatewayContractTests.NoProjectedOperationDeclaresAStatusItCannotProduce`, and against each generated
document by `DataServicesRouteCensusTests.EveryProjectedRoutePublishesExactlyTheStatusSurfaceItsMappingProduces`
on Gateway and its counterpart on the DataServices projection.

**Four statuses are declared on every projected operation, because every projected operation can really
produce them.** `429` when a handle registry behind [C-05](#8-c-05--persistencev1queryservice) through
[C-08](#11-c-08--persistencev1transactionservice) refuses to hold more work — a real, reachable ceiling
rather than a theoretical one. `503` when an upstream answers that it is not currently serving. `504` when
the deadline this service sets on **every** outbound call elapses, which makes its expiry an ordinary
outcome of a slow upstream rather than a hypothetical. And `502`, the one case the table cannot describe,
because it is the case where **no gRPC response arrived at all**: an exhausted retry or an unreachable
upstream, which is a failure mode decomposition itself creates.

**One legacy code has one status across the whole estate, and `RetCode.E_INVALID_HANDLE` is the one that
had to be settled to make that true.** A handle naming nothing — an expression session that was never
opened, a DataWindow handle bound to no chain, an upstream work handle already released — is `NotFound`
and therefore `404`, on every path that can raise it: DataServices' unary outcome map, both of its
upstream-failure maps, its streaming-resolution map, and the in-band map its own REST projection applies.
Three of those five used to answer `FailedPrecondition`, which this table declares no row for, so that arm
fell to the canonical gRPC mapping and reached the caller as **`400` carrying `E_INVALID_ARGUMENT`** — the
originating code replaced rather than re-spelled, and a caller told its argument was malformed when what it
had actually named was gone. A caller's retry-or-surface policy keys on the status, so one code answering
in two statuses depending on which internal helper happened to raise it is not a cosmetic divergence: it
makes the policy unwritable. `FailedPrecondition` remains what it always was — an ordering violation under
strict ordering, an unknown validation session, a transaction the upstream will not accept, and the
400-class group DataServices' unary outcome map spells that way — and still projects to `400` through the
canonical mapping.

**`RetCode.E_RETRY` was the second code that had to be settled the same way, and it was divided against
itself inside a single file.** DataServices carries two return-code maps: `BuildUpstreamFailure`, which
runs when an upstream outcome arrived carrying a database error, and `MapOutcomeToStatus`, which runs when
the identical outcome arrived without one. The first has always given `E_RETRY` → `Aborted` → **`409`**;
the second had it grouped with `E_BUSY` in the capacity arm, giving `ResourceExhausted` → **`429`**. So one
upstream code left the service as two different statuses **decided by which helper happened to raise it**,
and a caller's retry-or-surface policy cannot key on a status that changes with the reporting path. The
direction was fixed by this table rather than chosen: `Aborted` → `409` is the concurrency answer and
`E_BUSY` → `429` is the capacity answer, both already asserted in both projections' suites, and
harmonising the other way would have made `429` mean two unrelated things. The two remain **distinct
codes with distinct statuses**, which is the point — a shed request and a conflict are different events,
and the legacy declares a separate member for each.

**`AlreadyExists` used to be the one row translated but declared nowhere, and that gap closed with the
`409`.** It is produced by exactly one method in the estate — the macro channel reporting that a channel is
already attached — and that method is bidirectional and therefore **not projected**, which is why the earlier
revision argued the status unreachable and declined to declare it. That argument was load-bearing for a
declaration it should never have been load-bearing for: it reasoned about one arm of a status with three, and
it depended on an upstream implementation detail rather than on the upstream's published contract. The `409`
now declared on every projected operation covers all three arms, so the reachability question no longer has
to be answered correctly for the contract to be truthful, and the three outcomes stay readable apart by
problem type, title and `retCode` — the concurrency conflict carrying `E_RETRY` with a detail, the
detail-free `Aborted` carrying `E_RETRY` without one, and `AlreadyExists` carrying `E_INVALID_ARGUMENT`.
`503` additionally appears on `/health` on C-10's own account rather than from this mapping, and carries the
aggregate report rather than a problem document.

**The `dbError` member was the same defect one level down — declared on `ProblemDetails` since the contract
was authored and populated by nothing — and it is now populated on exactly one path.** A `NOT NULL` refusal
on the update path is an **in-band** failure: the transport answers OK and the body answers
`E_INVALID_DATA`, so it never travels the success path that forwards an upstream message whole. Gateway's
failure renderer discarded the whole message, and with it the only thing a caller who omitted a required
column could act on — the provider diagnostic naming the column, which Persistence had preserved through its
provider-envelope redaction rule and DataServices had relayed intact. So the identity survived two service
boundaries and was dropped at the third, and the caller received `400` with fixed prose naming nothing.
`RenderInBandFailure` now attaches that one declared member and nothing else.

**Attaching one declared member is not the same act as attaching the upstream message, and the distinction is
what keeps the earlier disclosure closed.** The retired `response` extension was unbounded, undeclared and
unscreened. `dbError` is a `$ref` to the mirrored five-member `DbError` shape with `additionalProperties:
false`; its one dangerous member is a **published commitment** rather than an assumption — `sqlsyntax`
carries placeholders only, empty is valid and common (C-G) — and it is the same kind of payload the
`conflict` member already forwards one status along, which is richer still and is forwarded for the identical
reason: a caller that cannot see what the database objected to cannot construct a corrected request. The
upstream's free-text diagnostic remains unrelayed; only the declared, schema-bounded payload travels. The
member is **optional**, so declaring it publishes "a database payload may be present", never "one will be" —
and a default-valued payload is treated as absent rather than published as an empty diagnosis.

**The two authenticated diagnostic operations declare two statuses that no code in their own files
produces.** `/v1/ping` and `/v1/capabilities` project no gRPC method, so the mapping table above does not
apply to them, and both statuses reach a caller from middleware:

- **`429`** from Gateway's own ingress request bound, which applies to every route except `/health`. Both
  operations reference their own `IngressBusy` response rather than the projection's `UpstreamBusy`, whose
  first sentence names a gRPC status neither of them can receive.
- **`500`** from the exception handler. Neither handler can fail on its own account — the ping falls back to
  the system clock rather than requiring a registration, and the capability projection reads a mask that
  failed validation at startup if it was going to fail at all — but both routes are **authenticated**, so the
  bearer handler must obtain the issuer's key set before either handler is reached, and a retrieval that
  fails with no last-known-good configuration cached faults inside the authentication middleware. That is
  precisely the condition `UseLastKnownGoodConfiguration` is left enabled in order to survive.

**`503` is deliberately NOT declared on either of them, and stating why is what keeps the addition above
from being a licence to declare the rest of the table.** On the thirty-nine projected operations `503` means
*an upstream answered that it is not currently serving* — it is a projection of gRPC `Unavailable`, and these
two operations call no upstream, so nothing can produce it. The ingress layer does not produce one either:
the request-layer limiter's rejection status is `429` and only `429`, and no middleware in either pipeline
answers `503`. Declaring it would be the same defect as the two above with its sign reversed — a status a
generated client must branch on and can never receive — which is why the closed set for these two operations
is `{200, 401, 403, 429, 500}` and no wider. `/health` is the one route in the document that answers `503`,
on C-10's own account, and it carries the aggregate report rather than a problem document.

**`/health` declares neither, and correctly does not.** It is exempt from the ingress bound, because
rate-limiting the gate three dependents are held behind would make a busy service a permanently unready one;
and it is anonymous, so no key set is needed to reach it, while its own handler converts every fault into a
degraded component entry and a `503` rather than letting one escape.

**`501` is not in the projected mapping at all, and the reason is a constraint rather than an omission.**
It belongs exclusively to the four reserved routes of [§13](#13-the-four-reserved-gateway-extension-points),
whose whole purpose is to declare that an entire capability area is unbuilt (**C-D**) — so it must not be
reachable from an operation this document publishes as implemented, and
`gateway.v1.yaml` declares it on the eight reserved-route operations and on no other. Two conditions were
answering it and now answer `500`:

- **An upstream `Unimplemented`.** A method the projection publishes reported unimplemented by the
  deployment answering is deployment or version skew, not a capability boundary.
- **The in-band legacy pair `E_NO_SUPPORT` (−2000) and `E_NO_IMPLEMENTATION` (−2001).** These are reachable
  in ordinary operation, which is what makes the distinction matter rather than being academic: the pinyin
  comparison is blocked because its lookup table exists only inside the closed binary
  ([§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess)), the column-expression engine
  declines a macro or foreign-variable arm, and a supplied `pinyinFlags` mask that disagrees with the
  deployment's configured one is refused rather than silently ignored.

Both now answer **`500` with a `ProblemDetails` body carrying the originating legacy code on `retCode`** —
the status every projected operation already declares, so a generated client has a branch for it. This is
the identical resolution [§5.3](#53-the-eight-weak-defaults-are-preserved-and-annotated-never-corrected)
applies to C-02's two symmetric-cipher narrowings, and for the identical reason: the legacy vocabulary
carries the distinction the status cannot. It is not `400`, because the request is well formed and the
limitation is this port's rather than the caller's. It is not `502`, because no upstream failed — it
answered normally and reported a capability it does not have.
`GatewayContractTests.NotImplementedIsDeclaredOnlyByTheReservedDeferredCapabilityOperations` asserts the
exclusivity in both directions, and the two projections' in-band suites pin the `500`, so this paragraph is
checkable rather than a convention.

**Every unary and every server-streaming method of C-03 and C-04 is projected, and the three
bidirectional ones are not.** That is thirty-nine projected operations: fifteen of C-03's sixteen and
twenty-four of C-04's twenty-six, alongside `/health`, `/v1/ping` and `/v1/capabilities` and the four
reserved routes of [§13](#13-the-four-reserved-gateway-extension-points) — **forty-six routes carrying
fifty operations**, the four extra operations being the second declared method on each reserved route.

Completeness in that direction is not optional. Gateway is the sole ingress, so an operation the gRPC
contract publishes and the projection omits is unreachable from outside the cluster, and a consumer
would read that as a defect in its own client rather than as a boundary of the contract. **The eight
read-and-apply operations of the four headless models ([§6.9](#69-the-four-headless-models-and-their-reachable-operations))
are therefore projected like the rest of C-03**, and `GetExpressionState` like the rest of C-04.

The three excluded streams are `EventChain` on C-03, and `InvokeMethodChannel` and `TraceChannel` on
C-04 — all three **bidirectional**. `EventChain` carries the item-change and validation chain, which is
strictly synchronous with no reordering permitted: the validation-error handler *reads and clears* the
result the preceding item-change event stashed, so its behaviour is a function of the prior event's
return value ([§6.5](#65-the-item-change-alphabet-is-its-own-enumeration)). JSON over independent REST
requests would lose both that ordering and the tri-valued typed veto
([§6.8](#68-the-veto-is-tri-valued-never-boolean)), and a partial projection of an ordered chain would
be worse than none: a consumer would receive some of it with no way to know what it had missed.
`InvokeMethodChannel` and `TraceChannel` are additionally **inverted**
([§7.6](#76-two-inverted-streams-structurally-required)), so there is no request/response direction to
project at all. Consumers needing any of the three use the gRPC contract. This is a documented gap with
an enumerable boundary, and the schema names all three individually so the boundary is checkable rather
than asserted.

**The two server streams — `Retrieve` on C-03 and `EventStream` on C-04 — *are* projected**, because
the property that decides projectability is a single request and a determinate response sequence, not
whether the RPC streams. A server stream's ordering is the trivial one: the server produces a sequence
and the client consumes it in order. Each therefore projects to one operation whose response is that
same sequence as an ordered collection, **with the chunking contract intact** — the chunk index, the
final-chunk flag and the cumulative row count all travel exactly as the stream carries them, and each
operation marks itself with `x-grpc-streaming: server` so a consumer knows the body is the whole
sequence rather than one message. Omitting them would have left the retrieval third of the triple
unreachable from outside the cluster, which for the sole ingress is a functional hole rather than a
documented gap.

**`EventStream`'s projection is a BOUNDED POLL, and that is the one place a projection deliberately does
not mirror its gRPC twin.** The distinction the paragraph above rests on — a determinate response
sequence — holds for `Retrieve`, which ends with its final-marked chunk. It does **not** hold for
`EventStream`, which is a *subscription*: the server ends it only when the client goes away, so
"the whole sequence" has no meaning and a projection that waited for it never answered at all. The
projected operation therefore collects for a **finite window** configured on the serving side and answers
with the records that arrived within it. Three consequences are contract rather than implementation:

- **An empty array is a complete, successful `200`.** It means *no event was emitted during the window*,
  never *the subscription ended* — there is no status for the latter, because the subscription does not end.
- **A response is not the whole sequence, so a consumer polls again.** Each element carries its own
  monotonic `sequence`, which is how a gap between one poll and the next is detected — the same token that
  detects a gap within one response.
- **A consumer needing continuous delivery uses the gRPC stream**, which carries no window because it needs
  none. The projection exists so the capability is reachable over HTTP, not so that HTTP becomes the better
  transport for it.

The window is a deployment setting and is deliberately **not** published as a number in the contract: it is
a completeness rule, and no latency budget, throughput target or availability commitment is published
anywhere in this system ([§14.7](#147-no-service-level-objective-is-asserted-and-none-exists-to-assert)), so a number there
would read as one. Both the ingress and the service behind it refuse to start on a window that could not
fire before the request it bounds is abandoned.

**Every projected body is published as a concrete, closed schema — 133 of the contract's 145 schemas,
covering the complete transitive closure of 118 messages and 15 enums.** Each carries `x-proto-message` or
`x-proto-enum` naming the descriptor it publishes, `additionalProperties: false`, canonical
lowerCamelCase member names, the canonical scalar encodings — 64-bit integers as `[integer, string]`
because the mapping *emits* them as JSON strings, `bytes` as base64 — and a `required` list that states
what the wire actually carries.

An earlier revision delegated every projected body — 75 of them on today's projection, 36 requests and 39
responses — to **one open schema** with no members, pointing a consumer at the `x-proto-*` extension to
find the real message. That was wrong in the one direction that
matters: the projection binds every request with the **strict** canonical parser, which *rejects* a member
the target message does not declare and answers `400`. The document therefore promised a permissiveness
the runtime does not have, and a consumer generating a client from it could not see a single member it was
required to send — `UpdateResponse.rowsInserted`, `.rowsUpdated`, `.rowsDeleted` and `.identity` were
invisible, on the one operation whose entire purpose is to report what it changed and what the engine
assigned to it.

The delegation's stated objection was sound and is **answered rather than overruled**: transcribing the
shapes by hand would create a second source of truth with nothing keeping the two in step, so the first
divergence would be silent. The schemas are therefore **generated** from the compiled descriptors, and
`shared/PowerFramework.Contracts.Tests/GeneratedSchemaFidelityTests.cs` compares every one of the 133
against its descriptor on every build — member set, JSON name, type, format, repeatedness, map shape,
closedness, enum spellings, enum numbers and the `required` rule. A divergence is a build failure.

`required` is asymmetric, and the asymmetry is the measured wire truth rather than a compromise. A
**response-only** shape declares every member *without* explicit protobuf presence, because the projection
formats default values and the formatter writes such a member whether or not it is set — so it is present
on every response even holding `0`, `""`, `false` or `[]`. A shape carried in a **request** declares
nothing required, because the parser reads an absent member as its default: absence and a default-valued
member are indistinguishable to the operation, so a `required` list there would publish a check nothing
performs and would make a validator reject a body the runtime accepts. A shape travelling both ways can
only honour the weaker guarantee, and names the response-side one in its description instead.

### 12.2 C-10 — Health and readiness

| | |
| --- | --- |
| **Identifier** | The health and readiness contract |
| **Transport** | REST on all four services |
| **Served by** | All four |
| **Consumed by** | The orchestrator, operators, and Gateway (for aggregation) |
| **Status** | NEW |

- **`/health` is anonymous on all four services.** It has to be: the thing that probes it holds no
  token, and requiring one would make readiness depend on the very service being probed.
- **`/v1/ping` requires a token on all four services and returns `401` without one.** This is the
  contract's authentication proof obligation, and it is why it exists on all four rather than only at
  the ingress.
- **Gateway aggregates its three upstreams.** Gateway reports healthy **only after** Persistence,
  DataServices and Security report healthy. The ordering is expressed in the orchestration manifest with
  a dependency condition on upstream health, and it is the readiness property that ruled out the
  alternative orchestration approach recorded in [`ARCHITECTURE.md`](ARCHITECTURE.md) §10.4.
- **The response distinguishes "not ready" from "unhealthy".** A service still completing startup
  validation is not the same as one whose dependency has failed, and an operator reading the aggregate
  needs to be able to tell which upstream is which — so the aggregate names each upstream and its
  individual state rather than returning a single opaque verdict.

The port map is [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1's subject and is not restated.

---

## 13. The four reserved Gateway extension points

Phase 1 implements exactly four of an eight-service target roster. The other four are mapped to a
destination in [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) and detailed in
[`DEFERRED.md`](DEFERRED.md), and they appear in this inventory in exactly one way: as four named
routes on Gateway's routing and contract metadata.

| Route | Deferred service | Capabilities it will eventually reach |
| --- | --- | --- |
| `/v1/design/**` | **DesignSystem** | Theming, geometry structures, colour functions, the DPI conversion family, canvas, painter, font, image, image list, popup menu, tooltip, tray icon, timer, `win32` interop, the logo control, and the **presentational halves** of ColumnSort, ContextMenu and DropDownSearch |
| `/v1/documents/**` | **Documents** | JSON and its helpers, the XML object family, ZIP, barcode and QR, file scanning, logging, and the date/number conversion set |
| `/v1/integration/**` | **Integration** | HTTP client and its extensions, FTP, WebSocket, MQTT, and the `pfwx.*` transports |
| `/v1/scripting/**` | **ScriptBridge** | Sciter and its extensions, MiniBlink, WebView embedding, the PowerScript compiler and evaluator, dynamic object and script invocation, and global-variable access |

Each is a named route returning **`501 Not Implemented`** with a **machine-readable body** naming
the deferred service it will eventually reach and carrying the marker **"reserved for Phase 2"**.
The body is structured rather than a text message so that a client can branch on it:

The body carries **six** members, and `ReservedRouteBody` in
[`gateway.v1.yaml`](../shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml) requires all six:

| Body field | Content |
| --- | --- |
| `status` | `501` — a constant, repeated in the body so a logged body is self-describing |
| `service` | The deferred service name, under the member spelling this contract published for `v1` **first** |
| `deferredService` | The **same value** as `service`, under the unambiguous spelling: `DesignSystem` \| `Documents` \| `Integration` \| `ScriptBridge` |
| `marker` | `reserved for Phase 2` — a constant, so a conformance test and a client can both match on it |
| `route` | **The requested path**, exactly as received, with the query string excluded |
| `retCode` | `E_NO_IMPLEMENTATION` = **-2001**, the legacy framework's own not-implemented code |

**Two members name the deferred service and they carry the identical value. That is deliberate, and the
reason is a wire-compatibility repair rather than an oversight.** `service` is the member `v1` published
first. `deferredService` was added later for a real reason — `service` already means the *responding*
service on the ping body and an *upstream* service on the health body, and a third meaning on the same
word makes this body ambiguous in exactly the place a client branches on it — but the revision that
introduced it also **removed** `service`, silently breaking every `v1` consumer reading that member under
a version number promising nothing had changed. Both are therefore emitted. **New code should read
`deferredService`**, which also matches the `x-deferred-service` extension each reserved operation
carries; retiring `service` is a `v2` decision rather than a tidying one.

**`route` echoes the requested path, never the route template.** The catch-all parameter's value "is
echoed in the `route` field and is otherwise unused", so echoing the literal template would leave that
value appearing nowhere in the body and make the statement false. The **query string is excluded** on two
grounds: it is not part of a route, and a caller who mistakenly placed a credential in one must not have
it reflected back.

**The template itself is published, per operation, as `x-route-template`, and the level it sits at is a
correction.** OpenAPI 3.1 defines a path parameter as matching a single segment and offers no conformant way
to widen it, so each reserved family's real matching behaviour is carried in the `x-catch-all` vendor
extension on the shared `ReservedPath` parameter: the captured value may contain `/`, it may be empty so the
bare prefix resolves, and every HTTP method answers identically. All three statements hold for all four
families, which is what makes one shared parameter the right place for them. A fourth member used to stand
beside them naming the route template as `/v1/{area}/{**path}` — **a template that exists nowhere**, because
the four families are literal prefixes and no `area` route parameter has ever been declared, so a tool
reading the field as what it is presented as would model a bindable parameter the server does not have. A
template is per-family, so a shared field could only ever hold a generalisation; it now lives on each
operation, carrying that operation's own literal template, in both the authored contract and the generated
document.

`retCode` reuses the legacy vocabulary rather than inventing a parallel one, so a client that already
branches on `retCode` handles a reserved route with the code it knows. Its neighbour `E_NO_SUPPORT`
(-2000) is a different statement — "this framework does not support that" rather than "this is not
implemented here" — and is deliberately not used.

**Each of the four declares `get` and `post`, and each operation declares two responses — `401` and
`501`.** Declaring two methods makes "nothing here is implemented" cover more than one verb, and
*every other* method on the route answers the same way, so no verb appears implemented. **Neither
declares a request body**: a request schema would model a deferred capability, and modelling one is
precisely what the prohibition forbids. The four declarations are structurally identical, differing only
in the path segment and the service they name — an asymmetry between them would itself be the evidence
that capability modelling had crept in.

Each is **authenticated**: none overrides the document-level bearer requirement, so an unauthenticated
caller cannot enumerate the deferred roster.

**`401` and `501` are not two outcomes of one handler — they are the two things a caller can receive, and
both are declared.** The `401` comes from the authentication middleware *before* any handler runs. The
`501` is the only result the handler computes, and it computes it unconditionally: every method, every
path remainder, nothing evaluated first. **That unconditionality is the property the compliance position
of [§13.1](#131-the-compliance-note-stated-so-it-is-auditable-rather-than-argued) rests on, and declaring
the pre-handler refusal beside it leaves it untouched.**

An earlier revision of this section declared the set as exactly `{501}`, reasoning that a status the
security scheme produces is not a response the *route* produces. That is right about the origin and wrong
about the obligation: a client generated from a set omitting `401` is told these operations cannot return
the status they demonstrably do return, and a conformance tool checking response coverage reports a
violation against a correct server. The authored contract, the runtime-generated description, the contract
tests and the end-to-end specs all now agree on `{401, 501}`.

**What would still be a violation, stated so the line stays auditable:** any `2xx`, which would say part
of a deferred service had been built; and any `4xx` **other** than that `401` — a `400`, a `404` or a
`409` would each say the route inspects the request before answering. Neither appears on any of the eight
operations.

### 13.1 The compliance note, stated so it is auditable rather than argued

> **A routing declaration is not a stub of the deferred service.**

For DesignSystem, Documents, Integration and ScriptBridge there is:

- **no service directory and no project file**,
- **no container definition**,
- **no test project**,
- **no partial implementation**, and
- **no exception-throwing placeholder class**.

The route exists so that the shape of the eventual system is **legible from Gateway's contract**,
which is what the brief asks for. The prohibition in C-D is on *implementing* the deferred services
— not even partially, not even to stub them out — and a declaration in a route table implements
nothing. It has no handler beyond the constant response above, it calls nothing, and it can reach
nothing, because there is nothing behind it to reach.

The distinction is worth spelling out because it is the one an auditor should be able to check
without ambiguity. The test is mechanical: search the repository for a project, container
definition, test project or type belonging to any of the four names. Finding none is the pass
condition, and the four `501` routes do not change that result. The same statement is made from the
architectural side in [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.4, deliberately, so that neither
document can be read as the only place it was claimed.

**A related discipline that belongs here.** Three DataWindow services in scope are irreducibly part
presentational, and each is split: the **headless half ships** on C-03 — the data model, the filter
and sort expression construction, the row-selection state machine, and the menu *item* model
including labels, identifiers, enabled and split flags and computed logical text widths — while the
**rendering half is deferred** and reached, eventually, through `/v1/design/**`. That is a
documented capability gap with an enumerable boundary, not a silent omission, and the split is
tabulated in [`ARCHITECTURE.md`](ARCHITECTURE.md) §12.3. A half-built deferred service would be
worse than a documented gap; this is the documented gap.

---

## 14. Cross-cutting contract rules

Collected so a reader does not have to infer them from ten subsections.

### 14.1 Versioned contracts are the only cross-service coupling

Restated from [§2.2](#22-the-only-cross-service-coupling) because it is the rule most easily eroded:
no shared **behavioural** code crosses a service boundary. The contracts project carries the
boundary definition and nothing else. Every contract carries an explicit `v1` in its package or
path, so a second version can coexist with the first rather than replacing it.

### 14.2 Constant identifiers are preserved verbatim

Constant identifiers keep their original SCREAMING_SNAKE spelling on the wire and in generated code
— `RetCode.OK`, `RetCode.E_INVALID_ARGUMENT`, `RetCode.CANCELLED`, `Enums.SQL_MS_REPLACE`,
`Enums.INIT_FLAG_ENABLE_SQLITE`, `Enums.CRYPTO_SYMCRYPT_TYPE_AES256`, `Categories.CAT_DWSVC`.

This deliberately departs from ordinary C# naming convention, and the reason is not sentiment:
**these values appear in serialized payloads, in log records, and in characterization recordings**,
where a rename would silently invalidate every stored comparison. A recording taken before a rename
and replayed after it would fail on a field name while the behaviour was identical — or, worse, pass
while comparing the wrong fields. The analyzer suppression that accompanies this decision is scoped
to the files that carry the constants.

The values, too, are preserved: `OK` / `SUCCESS` / `ALLOW` all equal 0
[`ws_objects/pfw.shared.pbl.src/retcode.sru:L39-L41`], `PREVENT` = 1 [`:L42`], `FAILED` = −1
[`:L43`], and **both spellings** `CANCELED` and `CANCELLED` equal −2 [`:L44-L45`], through the
contiguous error block to `E_RETRY` = −33 [`:L46-L76`] and then `E_NO_SUPPORT` = −2000,
`E_NO_IMPLEMENTATION` = −2001 and `UNKNOWN` = −4000 [`:L77-L79`]. Both spellings of the cancelled
constant are carried, because either may appear in a recording.

### 14.3 The tri-state return algebra survives on the wire

The return-code algebra is not boolean and must not be flattened into one:

- **A prevention reads as a success.** `PREVENT` is 1 and the success predicate tests
  greater-than-or-equal-to zero, so a prevention satisfies it.
- **Cancelled and null are *neither* succeeded nor failed.** The failure predicate tests less-than-zero
  **with an explicit exclusion of the cancelled value**, so cancelled fails both predicates; and both
  predicates return false on a null input, so null does too. That is a tri-state hole in a nominally
  boolean algebra, and it is deliberate.

> **Do not collapse null to zero. Doing so converts "neither succeeded nor failed" into
> "succeeded".**

The contract therefore represents a return code as a **nullable** signed integer and never as a
boolean or an unsigned value, and a consumer must evaluate the two predicates rather than testing
for zero.

### 14.4 Narrow with a defined error; never widen with a guess

Restated from [§1.2](#12-the-governing-principle-for-anything-that-cannot-cross). The **one** place
in Phase 1 where a legacy behaviour genuinely cannot cross the boundary is the cross-session foreign
expression variable of [§7.7](#77-the-one-hard-limit--cross-session-foreign-variables), and it is
narrowed with a defined error.

Five further places narrow a *field* or a *domain* rather than a behaviour, and every one is a
new-boundary decision rather than a behavioural change:

| # | What is narrowed | Where | Why the narrowing is not a behavioural change |
| --- | --- | --- | --- |
| 1 | Key material becomes an opaque reference | [§5.2](#52-the-contract-level-secrets-rule) | The legacy passes raw keys between in-process objects; there is no wire to preserve |
| 2 | The log-password, connection-parameter and user-parameter fields are forbidden on the response | [§11.2](#112-the-transaction-descriptor-mirrors-the-legacy-structure-field-for-field) | Same reason, plus a typed flag allowlist supplies the one behaviourally significant value the parameter string carried |
| 3 | The generated-statement field is redacted or split | [§8.6](#86-errors-and-the-two-fields-that-must-be-redacted) | The legacy field carries interpolated literals, the legacy message echoes offending values, and the legacy logger redacts neither |
| 4 | The random-generation length is capped, with the legacy maximum recorded | [§5.4](#54-the-random-generators-are-determinism-seams) | The legacy is an in-process call whose caller and callee share a process and a fate; across a boundary an authenticated caller could ask the **sole token issuer** for a multi-gigabyte allocation, so an unbounded domain is a denial-of-service primitive the legacy could not have had |
| 5 | Expression parity text must not be executed | [§6.9](#69-the-four-headless-models-and-their-reachable-operations) | The text is preserved byte-exactly and still travels; only the *sink* changes, from "evaluate this string" to "bind these values" |

Narrowing 4 is the one that carries a visible number, so it is stated exactly rather than gestured at:
the cap is published in the OpenAPI schema as the field's `maximum`, the legacy's own 32-bit domain is
published alongside it as an `x-legacy-domain-maximum` annotation so nothing is lost from the record,
and a request above the cap is refused with a defined error **before any allocation is attempted** —
which is the property that distinguishes a cap from a guard that merely renames an out-of-memory
failure.

### 14.5 Legacy defects are reproduced and annotated at the contract level

Every defect the contracts carry is annotated in the contract description at the point a consumer
meets it, so that a future reader cannot mistake it for an implementation error. The register, with
where each is specified:

| Preserved defect or quirk | Contract | Where specified |
| --- | --- | --- |
| Disabling item-change also suppresses column-expression evaluation | C-03 | [§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling) |
| Item-change case 1 is an empty arm that returns 1 untouched; case 2 restores conditionally; case 3 rewrites to 1; the default arm forces 2 | C-03 | [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
| Validation-error handler returns 3 on empty data and pre-sets 1 from a stashed code | C-03 | [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
| Dormant commented-out byte-length validation, carried inert | C-03 | [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
| Misleading `0:continue,1:prevent` comments over a tri-valued veto | C-03 | [§6.8](#68-the-veto-is-tri-valued-never-boolean) |
| 28 hardcoded Chinese parse messages that bypass localization | C-04 | [§7.8](#78-parse-errors-carry-a-caret-position-and-bypass-localization) |
| A public method carrying the private-convention underscore prefix | C-04 | [§7.9](#79-the-contract-covers-the-source-surface-not-the-documented-nine) |
| Chunk size rejected at or below 1000, inclusive | C-05 | [§8.2](#82-method-surface) |
| Clause modification splices a raw string — the injection site | C-05 | [§8.3](#83-clause-modification-and-the-injection-site-named-plainly) |
| `pfwPagedSQL_OutterTbl` — the doubled `t` is the legacy spelling | C-05 | [§8.4](#84-paging-parity-is-byte-exact-generated-sql) |
| The count short-circuit issues no statement on a short page | C-05 | [§8.5](#85-the-count-wrapper-and-its-short-circuit) |
| Success is 1, not the algebra's zero | C-06 | [§9.6](#96-two-overrides-that-invert-the-obvious-result) |
| A claimed success is rewritten into a failure from the driver code | C-06 | [§9.6](#96-two-overrides-that-invert-the-obvious-result) |
| Self-assignment to flip an item status, exercised by the fixture | C-06 | [§9.4](#94-updatekeyinplaceno-and-the-legacys-own-documented-workaround) |
| Filter-buffer identity collection iterates **backward** | C-06 | [§9.7](#97-the-identity-round-trip-and-its-inverted-iteration) |
| Identity column discovered by prefix match with a first-wins fallback | C-06 | [§9.7](#97-the-identity-round-trip-and-its-inverted-iteration) |
| Commit returns `FAILED` when autocommit is already on | C-08 | [§11.1](#111-method-surface) |
| Driver code 100 treated as success | C-08 | [§11.1](#111-method-surface) |
| Eight weak cryptographic defaults, four of them established by absence | C-02 | [§5.3](#53-the-eight-weak-defaults-are-preserved-and-annotated-never-corrected) |
| Value-to-literal rendering does not escape an embedded quote — reproduced byte-exactly as parity text, never as an executed expression | C-03 | [§6.9](#69-the-four-headless-models-and-their-reachable-operations) |
| Only one of the four expression-model `Add*` operations returns an index, because only one legacy counterpart does | C-04 | [§7.10](#710-the-wire-method-surface-enumerated) |

Four cross-thread transfer defects the legacy documents against itself also bear on the shape of the
carrier messages, and they are recorded here so a consumer knows why the update payload is a
changeset and the retrieval payload is chunked:

| Legacy comment | Locator |
| --- | --- |
| Full-state transfer requires sort and filter conditions to be synchronized | `n_cst_thread_task_sqlquery.sru:L562-L563` |
| Changeset transfer does not require them | `:L577-L578` |
| **Changeset application may lose rows on a sorted DataWindow larger than one chunk** — worked around with a temporary carrier | `:L147-L149` |
| **Reset must not be used to clear data**, because it makes changeset application fail to apply — a row-discard is used instead | `:L175-L176`, with the discard at `:L178`, `:L181` |
| **Full-state transfer can crash when a crosstab has too many columns** — worked around by suppressing the runtime's own prompting | `:L672-L673`, workaround at `:L674-L676` |

### 14.6 One-based indexing is a contract hazard, not an implementation detail

PowerBuilder arrays are one-based and its upper-bound function returns the **last valid index**,
whereas the target language's arrays are zero-based with a length one past the end. Every index that
crosses these contracts — a row number, a column identifier, a select index, a buffer position, an
expression index, a variable index — is therefore specified as **one-based on the wire**, matching
the legacy, and converted only inside an implementation.

This is stated as a contract rule rather than left to implementers because a silent off-by-one here
is indistinguishable from a behavioural regression, and because the reverse-iteration case of
[§9.7](#97-the-identity-round-trip-and-its-inverted-iteration) is a live example of an index
expression that looks wrong and is right.

### 14.7 No service-level objective is asserted, and none exists to assert

No contract choice in this document rests on how quickly anything runs, and no figure of that kind
appears anywhere in it. The repository publishes no service-level objective of any kind — no time
budget, no rate target, no uptime commitment — so there is no baseline to compare against, and
asserting one would be a fabricated requirement (C-B). Where a legacy source comment happens to
remark on the cost of an operation, that remark is the legacy's own, is not repeated here as a
claim, and is not used to justify anything.

The only quantitative non-functional requirement in the whole brief is the coverage gate, which is
the subject of [`BUILD.md`](BUILD.md) and of [`PARITY.md`](PARITY.md).

What *is* claimed is architectural rather than quantitative: each service is independently
deployable and independently scalable, which is a property of the acyclic topology of
[§2.1](#21-the-call-graph-the-contracts-realize) and not of any measurement.

One consequence belongs here rather than only in the orchestration documents, because it is a **contract**
obligation: four operations return an opaque handle whose state lives in the process that issued it — a
transaction session, a query task, an update task and a command task on Persistence, plus a validation
session and an expression session on DataServices. Replicas do not share that state, so a caller talking to a
replicated service must route every follow-up call carrying a handle back to the replica that issued it. The
affinity key is always a named field of the request (`SessionHandle.session_id`, `TaskHandle.task_id`,
`session_id`, `datawindow_handle`), and a replica that never issued the handle refuses it with
`RetCode.E_INVALID_HANDLE` rather than acting on it. `orchestration/README.md` §6.3.2 carries the operational
form of the same statement.

---

## 15. Rejected alternatives

Recorded per C-K, with the reason each was rejected, so that a later reader does not re-propose one
as an obvious improvement.

### 15.1 A managed SQL-parser package as the substitute for the legacy clause parser — REJECTED

The legacy clause parser exposes a `has*` / `get*` / `modify*` triple for each of six clause kinds
in two arities, and C-05 depends on it for both clause modification and paging. Substituting a
published managed parser was considered and **rejected** on two grounds:

- **The obvious candidate is dialect-specific.** It parses one of the two dialects and therefore
  **cannot serve the other arm's rewriter at all**, so adopting it would still leave one arm
  hand-written — and would then leave the two arms built on different foundations, which is worse than
  building both the same way. No parser in the base class library exists as a fallback.
- **The acceptance criterion is byte-exact parity** with the legacy's clause-level string output,
  including every sentinel identifier and the count alias listed in
  [§8.4](#84-paging-parity-is-byte-exact-generated-sql) and [§8.5](#85-the-count-wrapper-and-its-short-circuit).
  A general-purpose parser normalizes as it re-emits — whitespace, casing, parenthesization, alias
  quoting — and every normalization is a parity failure. Reaching byte-exactness through a parser that
  wants to normalize means fighting it.

**Decision: a focused in-repo six-clause model**, which is both sufficient for the two arms and more
verifiable, because its output is the only thing it produces. Documented as a deliberate
**build-not-buy** decision.

### 15.2 gRPC for Security — REJECTED

Recorded in full at [§4.3](#43-why-this-contract-is-rest-and-why-that-is-not-a-style-preference). In
short: it would force a custom key-set retrieval implementation into three services, replacing
framework code with hand-written code on the security-critical path.

### 15.3 gRPC at the ingress — REJECTED

Browser-facing gRPC requires a translating proxy in front of it and supports server streaming only.
Choosing it for C-09 would forfeit exactly the reach and discoverability an ingress needs and would
add an intermediary to the one boundary external clients must reach directly. Recorded from the
architectural side in [`ARCHITECTURE.md`](ARCHITECTURE.md) §5.1.

### 15.4 JSON over REST for the DataWindow event chain — REJECTED

It would lose both the ordering guarantee of
[§6.6](#66-ordering-pattern-assigned-per-capability-area) and the typed tri-valued veto of
[§6.8](#68-the-veto-is-tri-valued-never-boolean), and would leave the `any` return of
`oncolumnexpinvokemethod` untyped at both ends. The losses are semantic, not ergonomic.

### 15.5 Folding C-04 into C-03 — REJECTED

The expansion engine is the most intricate part of the in-scope estate and the most likely to need
contract revision. Folding it in would tie every revision of the expression grammar to a revision of
the event chain. Recorded at the head of [§7](#7-c-04--dataservicesv1columnexpressionservice).

### 15.6 A flat rowset for the update payload — REJECTED

It cannot carry original values, item statuses, or the delete and filter buffers, all three of which
the `updatewhere=1` comparison and the delete-plus-insert statement generation depend on
([§9.3](#93-what-updatewhere1-requires-of-the-wire),
[§9.4](#94-updatekeyinplaceno-and-the-legacys-own-documented-workaround)). A flat rowset would
type-check, serialize cleanly, and produce wrong SQL.

### 15.7 Projecting the streaming methods as REST — REJECTED

Discussed at [§12.1](#121-c-09--gatewayv1-rest-ingress). A partial projection of an ordered,
vetoable chain is worse than none, because a consumer cannot tell what it did not receive.

---

## 16. Versioning, compatibility, and the reviewer checklist

### 16.1 Versioning rules

- **Every contract is `v1`.** The version is in the protocol package name and in the REST path, so a
  `v2` can be introduced beside `v1` rather than replacing it.
- **Field numbers are never reused** in the protocol definitions, and a removed field is reserved rather
  than deleted, because recordings hold serialized payloads that must remain decodable.
- **Adding an enumeration value is a compatible change; changing one's numeric value is not** — the
  numeric values here are legacy constants ([§14.2](#142-constant-identifiers-are-preserved-verbatim))
  and are frozen.
- **The eight contracts consumed only inside the system may version more frequently than the two facing
  outward.**
  C-09 and C-10 are the external surface; C-01 through C-08 are internal edges, and Gateway's projection
  is the shock absorber between them.
- **A narrowing is a breaking change and requires a version bump**, even when it removes only a field.
  The six narrowings of [§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess) — the one
  behavioural narrowing and the five that narrow a field or a domain — are baked into `v1` for exactly
  this reason: they are established before anything consumes it. Do not confuse that register with the
  **three** C-02 capability narrowings N1 to N3 of
  [§5.3](#53-the-eight-weak-defaults-are-preserved-and-annotated-never-corrected)'s neighbouring
  subsection, which are a different thing counted separately: those are cells the legacy *declared* and
  whose parameters live only inside the closed binary, not decisions this boundary took.

### 16.2 Reviewer checklist

The brief asks for this inventory to be reviewed before code generation. These are the questions
worth answering first, in the order in which getting them wrong is most expensive:

1. **Are these the right ten boundaries?** In particular: is C-04 correctly separate from C-03
   ([§15.5](#155-folding-c-04-into-c-03--rejected)), and are C-05 through C-08 correctly four contracts
   rather than one Persistence contract? The four version independently and carry different streaming
   shapes.
2. **Does the C-06 row message carry enough?** Current value, original value, per-value null flags, row
   status and per-column status, for every marked column
   ([§9.3](#93-what-updatewhere1-requires-of-the-wire)). If any of those is missing, the concurrency
   comparison cannot be reproduced.
3. **Does the C-04 payload carry all three parts** — unexpanded text, bind-time snapshot, live
   environment — **with a per-reference expansion mode**
   ([§7.3](#73-the-contract-consequence-which-is-exact-and-non-negotiable),
   [§7.4](#74-five-expansion-modes-and-the-mode-is-per-reference))?
4. **Is the narrowing in [§7.7](#77-the-one-hard-limit--cross-session-foreign-variables) acceptable?**
   It is the only behavioural narrowing in the inventory, and it is the one decision here that a reviewer
   might legitimately want changed — the alternative being to keep every expression session for a
   related set of DataWindows pinned to one instance.
5. **Are the ordering-pattern assignments right per area** ([§6.6](#66-ordering-pattern-assigned-per-capability-area))?
   Moving an area from (b) to (a) is not a tuning choice; for the item-change group it is unsound.
6. **Is the tri-valued veto, the four-value item-change alphabet, and the tri-state return algebra each
   modelled as its own enumeration** ([§6.5](#65-the-item-change-alphabet-is-its-own-enumeration),
   [§6.8](#68-the-veto-is-tri-valued-never-boolean), [§14.3](#143-the-tri-state-return-algebra-survives-on-the-wire))?
7. **Is the opaque key reference of [§5.2](#52-the-contract-level-secrets-rule) sufficient**, and is the
   key store's population path acceptable?
8. **Do the four reserved routes read as metadata rather than as implementation**
   ([§13.1](#131-the-compliance-note-stated-so-it-is-auditable-rather-than-argued))?

### 16.3 Regenerating the counts, because a hand-maintained inventory has already drifted once

**The gRPC half of that regeneration is now also enforced mechanically**, because a recipe only runs
when somebody remembers to run it. `DocumentationCoherenceTests` in
`shared/PowerFramework.Contracts.Tests` reads §2's register and compares every gRPC row against the
compiled descriptor, asserts that no row is missing and none is invented, and separately asserts that
every total this document states as a service's *complete* surface is a total some service actually has
— which is what makes a stale restatement in prose fail a build rather than wait for a reviewer. The
same suite holds §14.4's narrowing count to the register §14.4 itself enumerates. The commands below
remain the way to produce the corrected numbers; the tests are what make their absence visible.

Every method and route count in this document is a **derived** figure, and a derived figure written by
hand goes stale silently. This document has drifted on the same four totals TWICE and in opposite
directions — the register in §3, the §7.1 inventory, the §7.10 wire surface and the §12.1 projection
arithmetic. It carried `26` for C-04 after `LoadRows` was added to the schema, and `27` after `LoadRows`
was withdrawn from it, so each drift was four edits wide and invisible to a reader of any one of them.
Regenerate rather than adjust:

```bash
# gRPC method counts, per service, straight from the definitions.
python3 - <<'PY'
import re, glob
for path in sorted(glob.glob('shared/PowerFramework.Contracts/Proto/*.proto')):
    service, counts = None, {}
    for line in open(path):
        code = line.split('//')[0]
        opened = re.match(r'\s*service\s+(\w+)\s*\{', code)
        if opened:
            service = opened.group(1)
            counts[service] = 0
            continue
        if service:
            if re.match(r'\s*rpc\s+\w+\s*\(', code):
                counts[service] += 1
            if re.match(r'^\}', code):
                service = None
    for name, total in counts.items():
        print(f'{name}: {total} rpcs   [{path}]')
PY

# OpenAPI path and operation counts, per document.
python3 - <<'PY'
import yaml
methods = {'get', 'put', 'post', 'delete', 'patch', 'options', 'head', 'trace'}
for path in ('shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml',
             'shared/PowerFramework.Contracts/OpenApi/security.v1.yaml'):
    paths = yaml.safe_load(open(path)).get('paths', {})
    operations = sum(1 for item in paths.values() for verb in item if verb.lower() in methods)
    print(f'{path}: {len(paths)} paths, {operations} operations')
PY
```

Measured on the tree this revision documents: `DataWindowService` 16, **`ColumnExpressionService` 26**,
`QueryService` 11, `UpdateService` 5, `CommandService` 6, `TransactionService` 13; `gateway.v1.yaml`
**46 paths / 50 operations**; `security.v1.yaml` 23 paths / 23 operations. Any disagreement between
those figures and the tables above is a defect in this document, not in the schema.

---

## 17. Evidence index, and what this document does not claim

### 17.1 Evidence index

Every legacy source this inventory rests on, with the contract it serves. All are **read-only**.

| Legacy source | Lines | Serves |
| --- | --- | --- |
| `docs/n_cst_dwsvc_columnexp.md` | 166 | C-04 — the authoritative specification |
| `ws_objects/pfw.crypto.pbl.src/n_crypto.sru` | 86 | C-01, C-02 |
| `ws_objects/pfw.shared.pbl.src/enums.sru` | 1,162 | C-02, C-05, C-09 |
| `ws_objects/pfw.shared.pbl.src/retcode.sru` | 221 | All ten — the return algebra |
| `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru` | 616 | C-03 |
| `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru` | 864 | C-03, C-04 |
| `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru` | 2,435 | C-04 |
| `ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru` | 1,328 | C-03 — topic grammar and veto |
| `ws_objects/pfw.thread.pbl.src/n_cst_threading.sru` | 1,084 | C-03 — the lifetime namespace suffix |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` | 883 | C-05 |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru` | 409 | C-06 |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru` | 116 | C-07 |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru` | — | C-08 — the connection flags |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru` | 545 | C-08 |
| `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru` | — | C-08 — pool lifecycle |
| `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs` | 14 | C-08 |
| `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs` | 10 | C-05, C-06, C-07 |
| `ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru` | — | C-07 — the argument ceiling |
| `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` | 37 | C-06 — the primary fixture |
| `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` | — | C-07 — binding and prefix evidence |

### 17.2 Corrections applied during verification

Every locator and figure in this document was checked against source. Figures carried forward from an
earlier planning summary disagreed with what the files actually contain, and in each case **the source
won**. They are listed so a reviewer can see that they were verified rather than copied. Rows 1 to 7
came out of the first verification pass; rows 8 to 13 came out of a later review of the schema against
this document, and each of those changed the schema, this document, or both:

| # | Earlier figure | Verified figure |
| --- | --- | --- |
| 1 | The column-expression specification is 167 lines | **166 lines** — every section citation in [§7](#7-c-04--dataservicesv1columnexpressionservice) was re-checked against the corrected numbering, and all of them already landed correctly |
| 2 | The cryptographic surface has "roughly 62 overloads" | **65 declarations at `n_crypto.sru:L9-L73`** — 63 cryptographic operations plus two accessors |
| 3 | Five paging sentinel identifiers | **Six.** `pfwPagedSQL_Tbl` [`n_cst_thread_task_sqlquery.sru:L356`, `:L382`, `:L834`] was missing from the list, and the count wrapper additionally emits the alias `CNT` [`:L834`] |
| 4 | Localization calls on C-03's validation path claimed at `se_cst_dw.sru:L357` and `:L368` | **`:L355` and `:L357`.** Line 368 is a comment, not a call |
| 5 | The fixture's six marked columns span `dw_sqlite.srd:L8-L14` | Columns are **`dw_sqlite.srd:L8-L13`**; `:L14` is the table specification |
| 6 | The column-expression public surface as enumerated | Also includes the **cacheable** flag setters and **two calculate-empty arities**; both are in the contract |
| 7 | Command autocommit is a boolean | **Three-valued** — `AC_OFF`, `AC_ON`, `AC_NATIVE` [`n_cst_thread_task_sqlcommand.sru:L16-L18`] |
| 8 | Item-change `case 1` "falls through" to `case 2` | **It does not.** `case 1` is an EMPTY arm at `se_cst_dw.sru:L212` and PowerScript `choose case` does not fall through, so 1 returns with value and status **untouched** and the restore belongs to the validation-error handler it triggers. Four distinct arms — [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
| 9 | The SQL Server paging arm has three strategies | **Four generated forms**, a two-by-two of unique-index columns against the native-paging flag, enumerated branch by branch with the property that betrays a collapsed branch — [§8.4](#84-paging-parity-is-byte-exact-generated-sql) |
| 10 | C-02 maps 60 of the 63 legacy overloads onto 15 operations | **All 63 onto 17 operations.** The two file-hash forms were unpublished; they are now published, taking an opaque server-resolved reference exactly as `keyRef` does and never a caller-supplied path — [§5.1](#51-method-surface) |
| 11 | Encrypting the channel is enough, so issuance needs no credential | **Authentication on that operation is a per-operation credential, not a transport property.** The service has exactly ONE listener — `https://+:5104`, `Http1` — and TLS establishes that the channel is private, not who is on the other end of it. `POST /v1/tokens` therefore requires a caller credential regardless: an HTTP `Basic` credential naming a subject on the issuance roster, or a client certificate the listener's configured authority trusts. Presenting neither is `401`. So no address on any topology mints a token without a credential, and the transport is a necessary condition rather than a sufficient one — [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.1 and §9.4 — [§4.1](#41-method-surface) |
| 12 | The topic contract carries three decomposed fields | **Every encoding is its own field**, including the namespace with explicit presence and the two *filter* negation flags — and the negated-namespace filter spares the named namespace rather than selecting it, the reverse of how it reads — [§6.7](#67-the-three-encoding-topic-string-and-why-naive-serialization-fails) |
| 13 | The transaction response mirrors the nine-field descriptor with `logpass` removed | **Three slots are `reserved` on the response** — `logpass`, `dbparm` and `userparm` — with a typed flag allowlist carrying the one behaviourally significant value the parameter string held — [§11.2](#112-the-transaction-descriptor-mirrors-the-legacy-structure-field-for-field) |
| 14 | The event gate's two mutators should be aligned on one return type, "matching the legacy return-code width" | **The legacy has TWO widths, so there is no single width to match.** `se_cst_dw.sru:L110` declares `public function long of_disableevent` and `:L111` declares `public function integer of_enableevent`, for mirror-image bodies. Under AAP §0.4.5.2 PowerScript `long` maps to `long` and `integer` to `int`, so the port is faithful and harmonising the two would be the behavioural change to a published signature that C-B and G2 forbid. It is observationally benign — both members return only `OK` or `E_INVALID_ARGUMENT`, which fit either width — so what is preserved is the CONTRACT's fidelity, not a value's. Held by three assertions in `EventGateTests`, one of which fails the build if the two types are ever made equal — [§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling) |

### 17.3 What this document does not claim

Stated plainly, because a reference document that overclaims is worse than one with gaps:

- **It makes no execution claim of its own, in either direction.** One document reports execution status
  for this repository and this is not it:
  [`../orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
  records the bring-up gate by gate and is equally explicit about what that run did not cover. This
  document therefore neither asserts nor denies that the orchestrated stack starts or that the readiness
  gate of [§12.2](#122-c-10--health-and-readiness) fires in the documented order — it defers, because an
  execution claim restated in a second document is a second claim to keep true and an earlier revision of
  this section carried one that had already gone stale: it stated that no `orchestration/docker-compose.yml`
  existed to assemble the four images, which stopped being true when the manifest landed.
- **It does not claim that any of these ten contracts has been exercised across a live network
  boundary.** Three states have to be separated, because they are three different amounts of evidence:
  - **Specified and expressed as a schema — all ten.** Five definition files exist —
    `Proto/common.v1.proto`, `Proto/dataservices.v1.proto`, `Proto/persistence.v1.proto`,
    `OpenApi/security.v1.yaml` and `OpenApi/gateway.v1.yaml` — carrying six gRPC services and two REST
    surfaces between them. So this document no longer "precedes the schema" anywhere: for every one of
    the ten contracts the schema is present and is the artifact this document's inventories are checked
    against ([§2.3](#23-where-the-definitions-live)). Where the two disagree, **the schema is
    authoritative for the wire** and this document is corrected to match it.
  - **Implemented in source and covered by tests — all ten.** An earlier revision of this list said
    "not implemented, all ten", and that is no longer true. Gateway carries the C-09 REST ingress, the
    `/v1/datawindow` projections with the gRPC-to-HTTP status translation, the C-10 health aggregation
    and the four reserved families; Security carries C-01 token issuance, the JWKS and discovery
    documents and the C-02 crypto surface; DataServices carries the two C-03/C-04 gRPC services;
    Persistence carries the four C-05..C-08 gRPC services. All four applications build with zero
    warnings, and each has a test project that exercises its handlers against an in-process host.
  - **Assembled and brought up as a four-service stack — but not one of C-03 to C-08 has been invoked
    across it.** This is the state that matters for a contract inventory and it is the one still open, and
    it is now open for a narrower reason than it once was. All four container definitions exist, so does
    `.github/workflows/ci.yml`, and so does `orchestration/docker-compose.yml`; the bring-up recorded by
    [`../orchestration/README.md` §10.1](../orchestration/README.md#101-the-stack-has-been-brought-up-and-here-is-exactly-what-was-observed)
    reached all four services healthy in the documented order, answered the four `/health` gates over TLS,
    saw Gateway's aggregate report healthy against its three live upstreams, and exercised C-01 issuance
    and C-10 on the shared-secret scheme. What has **not** happened is the part this document is about:
    [§10.2](../orchestration/README.md#102-what-has-not-been-exercised-and-none-of-it-is-glossed) records
    that **no gRPC RPC has been invoked** — every listener was proven reachable at the TLS layer from its
    legitimate in-network caller, and no C-03 to C-08 call has crossed a container boundary. **No
    characterization parity result exists** either ([`docs/PARITY.md`](PARITY.md)). So every statement in
    this document about the *content* of what passes between services is target semantics verified
    in-process rather than an observation of the deployed system, even though the boundary those messages
    would cross has now been stood up and probed.
- **It does not claim byte-exact parity has been achieved anywhere.** It states where byte-exactness is
  the *criterion* — the paging output of [§8.4](#84-paging-parity-is-byte-exact-generated-sql) and the
  count wrapper of [§8.5](#85-the-count-wrapper-and-its-short-circuit) — and leaves the demonstration to
  [`docs/PARITY.md`](PARITY.md).
- **It does not claim the deferred services are partly built.** They are not built at all
  ([§13.1](#131-the-compliance-note-stated-so-it-is-auditable-rather-than-argued)).
- **It does not claim more than one storage engine is provisioned.** Exactly one is
  ([§8.1](#81-one-storage-engine-is-provisioned-and-it-is-sqlite)).
- **It does not claim that any user-specified rule governs these contracts.** None exists
  ([§1.3](#13-no-user-specified-rules-exist)); the bar applied instead is stated there rather than
  assumed.
- **It reproduces no secret value of any kind.** Where a field carries such material, only the field is
  named and its handling rule stated; the locators live in [`SECRETS.md`](SECRETS.md).
