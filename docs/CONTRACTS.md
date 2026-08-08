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
| The characterization model, the fixture corpus, and the determinism seams | [`PARITY.md`](PARITY.md) |
| Build and test commands, the solution layout, per-service build independence | [`BUILD.md`](BUILD.md) |

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

| File under `shared/PowerFramework.Contracts/` | Carries |
| --- | --- |
| `Proto/dataservices.v1.proto` | C-03, C-04 |
| `Proto/persistence.v1.proto` | C-05, C-06, C-07, C-08 |
| `Proto/common.v1.proto` | Messages shared across the protocol definitions: the return code, the DataWindow buffer selector, the item status, the database error, and the conflict detail |
| `OpenApi/gateway.v1.yaml` | C-09 (and the ingress half of C-10) |
| `OpenApi/security.v1.yaml` | C-01, C-02 |

---

## 3. The ten contracts at a glance

| ID | Contract | Transport | Served by | Consumed by | Shape that decided it |
| --- | --- | --- | --- | --- | --- |
| **C-01** | `security.v1.TokenService` | REST + JWKS | Security | Gateway, DataServices, Persistence | Stateless issuance; stock bearer handlers must self-configure from plain HTTP |
| **C-02** | `security.v1.CryptoService` | REST | Security | DataServices | 63 stateless cryptographic operations, no ordering, nothing to stream |
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

| Operation | Auth | Purpose |
| --- | --- | --- |
| `POST /v1/tokens` | caller identity required | Issues a short-lived service token from a caller identity, an audience, and a scope set |
| `GET /.well-known/jwks.json` | **anonymous** | Publishes the verification material |
| `GET /.well-known/openid-configuration` | **anonymous** | Discovery metadata, so a consumer's stock bearer handler self-configures |

The issuance request carries the caller identity, the intended audience, and the requested scope
set; the response carries the token, its type, its expiry, and the granted scope set — which may be
narrower than the requested one. A caller must read the granted set rather than assume its request
was honoured in full.

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
- **Mutual TLS is the documented per-pair fallback.** Where a token issuer is inappropriate for some
  service pair, mutual TLS applies **for that pair only**, adding certificate and key path settings
  for those two services rather than changing the system-wide model. Tokens remain the default for
  every edge.
- **No signing, verification or mutual-TLS material is scaffolded for any deferred service.**
  Provisioning a credential for a service that does not exist creates an unowned secret.

The secret's name and the full token register are in [`SECRETS.md`](SECRETS.md). **No value appears
in any document.**

### 4.3 Why this contract is REST, and why that is not a style preference

Token issuance and key publication must be plain HTTP so that a consumer's **stock bearer handler**
fetches `/.well-known/jwks.json` and the discovery document with **zero bespoke code**. That keeps
the security-critical retrieval path inside framework code rather than hand-written code, on three
services rather than one.

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

**The shape is what decided the transport.** Every one of the 63 operations is a stateless
request/response with no ordering requirement between calls and nothing to stream. REST fits that
shape without remainder.

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

### 5.3 The seven weak defaults are preserved and *annotated*, never corrected

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
| 2 | **The default RSA padding is PKCS#1, and no-padding is explicitly unsupported.** The legacy rejects it, and so does the port | Only two padding values exist — `CRYPTO_RSA_PADDING_PKCS1 = 0` and `CRYPTO_RSA_PADDING_OAEP = 1` — with the default declared equal to PKCS#1, at `enums.sru:L949-L951`. **There is no "none" value to select**, so the absence is the legacy's own rejection | PKCS#1 v1.5 remains the default. A request for no padding is refused with a defined error rather than honoured |
| 3 | **PKCS#5-family symmetric padding only, and not selectable** | There is **no symmetric padding constant of any kind** anywhere in `enums.sru`; the cipher block at `:L936-L946` exposes type and mode and nothing else | The padding scheme is fixed. The contract carries no field for it, because offering one would imply a choice the legacy never had |
| 4 | **No key-derivation function is reachable at all**, and there is no salt concept — so a passphrase is used as raw key bytes | No PBKDF2, scrypt, bcrypt, Argon2 or salt constant exists in `enums.sru`, and no `n_crypto` signature accepts an iteration count or a salt (`n_crypto.sru:L30-L61`) | Whatever the caller supplies as key material *is* the key. The contract does not derive, stretch, or salt |
| 5 | **No authenticated encryption** — no GCM, CCM or Poly1305 — so ciphertext carries no integrity tag | The mode set is exactly ECB, CBC and CFB at `enums.sru:L943-L945` | Ciphertext integrity is not protected by this contract. A caller needing it must obtain it separately, e.g. through the keyed-hash operations |
| 6 | **1024-bit RSA remains a legal key size**, and the legacy demonstration uses it | `Enums.CRYPTO_RSA_BITS_1024 = 1024` is a declared, first-class value alongside 2048 and 4096 at `enums.sru:L965-L967` | 1024 is accepted. It is annotated as a legacy-compatibility value, and it is not removed from the accepted set |
| 7 | **The algorithm identifier sets are preserved exactly**, values and spellings alike | See the table below | A rename would silently invalidate every stored characterization comparison ([§14.2](#142-constant-identifiers-are-preserved-verbatim)) |

The identifier sets required by item 7, verified value by value:

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

Note that items 3, 4 and 5 are established by **absence** — there is no constant to select, and no
signature that accepts one. Absence is weaker evidence than presence in general, but here it is
exactly the right kind: a capability the legacy cannot express is a capability the port must not
offer, because offering it would be a new feature (C-B).

### 5.4 The random generators are determinism seams

`GenRandomBlob`, `GenRandomString` and `GenGUID` at `n_crypto.sru:L14-L18` are the **primary
non-determinism sources** in the in-scope estate. Characterization compares a recorded legacy run
against a target run, and a value that differs on every execution makes the comparison meaningless
unless it is masked on **both** sides.

The contract therefore treats these three as **seamed**: the provider behind them is injected, so a
test substitutes a deterministic double while production uses the platform generator. The seam is a
property of the implementation, but it is recorded in the contract description because a consumer
writing a parity test needs to know which fields to mask. The full seam register is in
[`PARITY.md`](PARITY.md).

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

| Method | Kind | Why this kind |
| --- | --- | --- |
| `Retrieve` | **server stream** of buffer-shaped chunks | Reproduces progressive result delivery, and keeps a large result set off a single message |
| `OpenValidationSession` / `CloseValidationSession` | unary | Materializes the four pieces of cross-event mutable state as an explicit, correlated session ([§6.4](#64-four-pieces-of-cross-event-mutable-state-and-why-the-session-exists)) |
| `EventChain` | **bidirectional stream** | Carries all 13 raw and all 9 semantic events with sequencing tokens, in one ordered conversation ([§6.6](#66-ordering-pattern-assigned-per-capability-area)) |
| `Update` | unary | Returns **`Aborted`** on an optimistic-concurrency conflict, carrying the conflict detail |
| `GetEventGate`, `DisableEvent`, `EnableEvent` | unary | Expose the event bitmask, **including the coupling by which disabling item-change also suppresses column-expression evaluation** ([§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling)) |

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
6. Dispatch on the stashed result [`:L211-L251`]:
   - **case 1 falls through to case 2** — case 1 has no body of its own [`:L212-L213`]. Both restore
     the value and status, but **only if the earlier equality test held** [`:L216-L222`], because the
     buffer may already have been changed and must not be overwritten.
   - **case 3 keeps the value, does not move focus, and rewrites the result to 1** [`:L223-L225`].
   - **the default arm** performs manual type-directed coercion by the **first five characters** of the
     column type, across the fixed set `char` / `char(` / `decim` / `real` / `numbe` / `long` /
     `ulong` / `datet` / `date` / `time` [`:L231-L244`], fires the changed event [`:L247`], and then
     **forcibly returns 2** [`:L250`] so the runtime will not re-apply the edit text over the buffer.

So: 3 collapses into 1, and the default arm rewrites to 2. Both rewrites are observable and both are
preserved.

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

Conformance tests are written per workflow against this table; the workflow corpus and the recording
model are [`PARITY.md`](PARITY.md)'s subject.

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

> **The wire contract carries `sequence` (integer), `name` (string) and `lifetime` (enum) as three
> separate fields**, and reconstitutes the fused legacy spelling **only at the compatibility edge**
> — the one place that must hand a string to legacy-shaped code. Where a subscription also carries a
> capture mode or an explicit priority, those travel as their own fields on the same principle.

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

It is also the **only** place in this inventory where the narrowing principle of
[§1.2](#12-the-governing-principle-for-anything-that-cannot-cross) has to be applied.

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

| Method | Kind | Behaviour the contract must preserve |
| --- | --- | --- |
| `Query` | **server stream of chunks** | Reproduces the legacy's progressive recordset delivery. The legacy chunk count is `ceiling((rowCount + filteredCount) / chunkSize)` [`:L145`] |
| `SetChunkSize` | unary | **A value at or below 1000 yields `RetCode.E_INVALID_ARGUMENT`** [`:L410`] — reproduced verbatim, including the fact that the boundary is *inclusive* |
| `SetWhereClause` | unary | Carries the select index, the modification style, and the clause. **A zero-or-negative index or an empty clause yields `RetCode.E_INVALID_ARGUMENT`** [`:L271`] |
| `SetOrderByClause` | unary | The same three fields and the same validation [`:L288`] |
| `SetPaging` | unary | Page size, page index, a native-paging switch, and a page-counting switch. **A page size or index at or below zero yields `RetCode.E_INVALID_ARGUMENT`** [`:L307-L310`] |
| `SetPagedUniqueIndexColumns` | unary | The unique-index column list that selects the inner-join paging strategy [`:L406`] |
| `Count` | unary | Total record and page counts; see [§8.5](#85-the-count-wrapper-and-its-short-circuit) |

The legacy object exposes further per-task settings which the contract carries as **fields on the
query request** rather than as separate calls, because they are configuration rather than
operations: source selection by statement [`:L67`] or by data-object name [`:L59`], a filter
[`:L69`], a sort [`:L70`], a maximum row count [`:L61`, rejecting a negative value with
`RetCode.E_INVALID_ARGUMENT` at `:L433`], a result-cache switch [`:L58`], and a hook class name
[`:L60`] — all declared in the prototype block at `:L49-L71`.

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

### 8.4 Paging parity is byte-exact generated SQL

The dispatch is a `choose case` on the connection's dialect selector at
`n_cst_thread_task_sqlquery.sru:L320`, with three arms:

| Arm | Selector value | Locators | Generated form |
| --- | --- | --- | --- |
| First dialect arm | `DBT_MSSQL` = 0 | `:L321-L385` | Three strategy kinds: a **unique-index inner-join** wrapper selected when unique-index columns have been supplied [`:L323-L364`], which itself generates either an offset-fetch inner query [`:L343-L350`] or a top-plus-row-number inner query [`:L351-L363`]; a plain **offset-fetch** form [`:L366-L373`]; and a **top-plus-row-number** fallback [`:L374-L384`] |
| Second dialect arm | `DBT_ORACLE` = 1 | `:L386-L395` | A single **triple-nested row-number** form [`:L394-L395`] |
| `case else` | anything else | `:L396-L398` | **`RetCode.E_NO_IMPLEMENTATION`**, raised through the error event — preserved exactly, including the fact that it is a *distinct* code from `E_NO_SUPPORT` |

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

| Sentinel | Emitted by | Locators |
| --- | --- | --- |
| `pfwPagedSQL_OutterTbl` | first arm only | `:L333`, `:L350`, `:L362` |
| `pfwPagedSQL_RN` | **both arms** | `:L355`, `:L356`, `:L381`, `:L382`, `:L383`, `:L394`, `:L395` |
| `pfwPagedSQL_Tbl` | first arm, and the count wrapper | `:L356`, `:L382`, `:L834` |
| `pfwPagedSQL_TblInnerInner` | second arm only | `:L394` |
| `pfwPagedSQL_TblInner` | second arm only | `:L394` |
| `pfwPagedSQL_TblOuter` | second arm only | `:L394` |

Parity also covers the **empty-order-by substitutions**, which differ per arm and are not
interchangeable: the first arm substitutes `(SELECT 0)` [`:L370`, `:L379`] and the second
substitutes `''` [`:L392`]. And it covers the count-wrapper aliases of
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

### 8.6 Errors, and the one field that must be redacted

Failures return a structured `DbError` mirroring the legacy error event field for field. The legacy
structure is `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs:L3-L9`:

| Legacy field | Locator | Carried as |
| --- | --- | --- |
| `sqldbcode` | `:L4` | The driver's own numeric code |
| `sqlerrtext` | `:L5` | The driver's message text |
| `sqlsyntax` | `:L6` | **Redacted or parameter-separated — see below** |
| `buffer` | `:L7` | The offending buffer selector: primary, delete, or filter |
| `row` | `:L8` | The offending row number |

> **The statement field is redacted or structurally split into statement plus parameters. It is
> never echoed verbatim.**

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
column types disagree with the evidenced schema in three places; those mismatches are **preserved as
defects** and are tabulated in [`ARCHITECTURE.md`](ARCHITECTURE.md) §8.4.

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

### 10.1 `Exec` and the four behaviours it preserves

`Exec` executes a command that returns no result set. The four behaviours the contract carries:

- **Positional `?` binding.** Arguments are supplied positionally and substituted into the statement in
  order. The legacy demonstrates it directly:
  `Exec("… VALUES (?, ?, ?, ?, ?)", "Paul", 32, "California", 20000, "1999-05-08")` at
  `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L398-L400`, and a single-placeholder update at
  `:L313`. Binding is applied before execution and a binding failure yields
  `RetCode.E_SQL_BIND_ARG_FAILED` [`n_cst_thread_task_sqlcommand.sru:L81-L86`].
- **The leading-`@` prefix execution mode.** A statement whose first character is `@` selects the
  statement-caching execution mode — the same call at `w_test_sqlite.srw:L398` is written
  `"@INSERT INTO …"`, and the comment at `:L397` records what the prefix does. The prefix is part of the
  statement string on the wire, exactly as in the legacy, because it is the statement text that selects
  the mode.
- **Multi-statement batch execution**, demonstrated at `w_test_sqlite.srw:L381-L392`, where a batch is
  submitted and rolled back as a unit on failure.
- **Error-text retrieval.** The result carries the numeric code and the driver text, drawn from the
  accessors the legacy exposes for exactly this purpose [`n_sqlite.sru:L26-L29` — row count, code, driver
  code, and error text].

### 10.2 The commit mode is three-valued, not boolean

The legacy autocommit setting has three values, declared at
`n_cst_thread_task_sqlcommand.sru:L16-L18`:

| Identifier | Value | Behaviour |
| --- | --- | --- |
| `AC_OFF` | 0 | No transaction handling by this call; the caller's session governs |
| `AC_ON` | 1 | Open a transaction and commit it on success — the legacy comment's own words at `:L17` |
| `AC_NATIVE` | 2 | **Do not open a transaction at all** [`:L18`]; the driver's own autocommit is switched on for the duration and back off afterwards |

The three arms are visibly distinct in the worker: `AC_NATIVE` toggles the connection's own
autocommit before and after and reports a commit [`:L88-L90`, `:L95-L97`, `:L105-L106`], `AC_ON`
performs an explicit commit and reports a failure through the error event [`:L98-L103`], and any
other value falls to an explicit rollback on failure [`:L107-L109`]. **A boolean field could not
express the third value**, so the contract carries the enumeration with those numeric values
preserved.

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

Session begin and end, autocommit control, commit, and rollback — the action-oriented vocabulary the
legacy exposes in its prototype block at `n_cst_thread_trans.sru:L72-L101`, expressed as:

| Method | Legacy anchor | Notes |
| --- | --- | --- |
| `BeginSession` | `of_connect` [`:L111`] | Returns a session handle; a broken connection is rejected up front with `RetCode.E_INVALID_TRANSACTION` [`:L113`] |
| `EndSession` | `of_disconnect` | Releases the pool reference; see [§11.3](#113-pool-lifecycle-reference-counting-and-a-seamed-clock) |
| `SetAutoCommit` | the autocommit accessor [`:L88`] | |
| `Commit` | `of_commit(autorollback)` [`:L240-L257`] | **Returns `RetCode.FAILED` when autocommit is already on** [`:L240`] — preserved; and on failure performs a rollback when the auto-rollback flag is set [`:L250-L252`] |
| `Rollback` | `of_rollback` | |

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
| 8 | `autocommit` | Carried |
| 9 | `userparm` | Carried |

> **The log-password field (`logpass`) is write-only on this contract: it may be supplied on a
> request and is never returned on a response and never written to a log.**

This is a deliberate narrowing, and the honest statement of it matters. **The legacy in-process
accessor round-trips the field in both directions** — it is read in at `n_cst_thread_trans.sru:L349`
and written back out at `:L414`. On a wire, echoing it would place credential material in a response
body and in every recording of one. Because this contract is new and no legacy wire format exists
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

> **The clock is seamed for deterministic tests.**

Three distinct clock reads must be substitutable, and a parity test needs to know about all three:
the idle start time above [`:L97`], the last-successful-connection stamp
[`n_cst_thread_trans.sru:L107`, `:L212`], and the **liveness cache** that reports a connection as
connected **without probing it** when the last success was recent [`:L196-L198`]. The last of these
is observable through the *absence* of a probe, so a test that does not control the clock cannot
reproduce it. The seam register is in [`PARITY.md`](PARITY.md).

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
| **Definition** | `OpenApi/gateway.v1.yaml` |
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
| `Unimplemented` | `501` | Including the four reserved routes of [§13](#13-the-four-reserved-gateway-extension-points) |
| `Internal` | `500` | With the statement field redacted per [§8.6](#86-errors-and-the-one-field-that-must-be-redacted) |

The two streaming methods of C-03 and the two inverted streams of C-04 are **not** projected as
REST. A bidirectional stream carrying an ordered event chain with a per-message veto has no faithful
REST representation, and a partial projection would be worse than none: a consumer would receive
some of the chain and have no way to know what it had missed. Consumers needing the event chain use
the gRPC contract. This is a documented gap in the projection, not an oversight.

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

| Body field | Content |
| --- | --- |
| `status` | `501` |
| `service` | `DesignSystem` \| `Documents` \| `Integration` \| `ScriptBridge` |
| `marker` | `reserved for Phase 2` |
| `route` | The matched route pattern |

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

Three further places narrow a *field* rather than a behaviour, and all three are new-boundary
decisions rather than behavioural changes: the opaque key reference of
[§5.2](#52-the-contract-level-secrets-rule), the write-only log-password field of
[§11.2](#112-the-transaction-descriptor-mirrors-the-legacy-structure-field-for-field), and the
redacted statement field of [§8.6](#86-errors-and-the-one-field-that-must-be-redacted).

### 14.5 Legacy defects are reproduced and annotated at the contract level

Every defect the contracts carry is annotated in the contract description at the point a consumer
meets it, so that a future reader cannot mistake it for an implementation error. The register, with
where each is specified:

| Preserved defect or quirk | Contract | Where specified |
| --- | --- | --- |
| Disabling item-change also suppresses column-expression evaluation | C-03 | [§6.2](#62-the-event-gate-and-its-one-non-obvious-coupling) |
| Item-change case 1 falls through to case 2; case 3 rewrites to 1; the default arm forces 2 | C-03 | [§6.5](#65-the-item-change-alphabet-is-its-own-enumeration) |
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
| Seven weak cryptographic defaults | C-02 | [§5.3](#53-the-seven-weak-defaults-are-preserved-and-annotated-never-corrected) |

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
[`BUILD.md`](BUILD.md)'s and [`PARITY.md`](PARITY.md)'s subject.

What *is* claimed is architectural rather than quantitative: each service is independently
deployable and independently scalable, which is a property of the acyclic topology of
[§2.1](#21-the-call-graph-the-contracts-realize) and not of any measurement.

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
  The three narrowings of [§14.4](#144-narrow-with-a-defined-error-never-widen-with-a-guess) are baked
  into `v1` for exactly this reason: they are established before anything consumes it.

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

Every locator and figure in this document was checked against source. Seven figures carried forward
from an earlier planning summary disagreed with what the files actually contain, and in each case
**the source won**. They are listed so a reviewer can see that they were verified rather than
copied:

| # | Earlier figure | Verified figure |
| --- | --- | --- |
| 1 | The column-expression specification is 167 lines | **166 lines** — every section citation in [§7](#7-c-04--dataservicesv1columnexpressionservice) was re-checked against the corrected numbering, and all of them already landed correctly |
| 2 | The cryptographic surface has "roughly 62 overloads" | **65 declarations at `n_crypto.sru:L9-L73`** — 63 cryptographic operations plus two accessors |
| 3 | Five paging sentinel identifiers | **Six.** `pfwPagedSQL_Tbl` [`n_cst_thread_task_sqlquery.sru:L356`, `:L382`, `:L834`] was missing from the list, and the count wrapper additionally emits the alias `CNT` [`:L834`] |
| 4 | Localization calls on C-03's validation path claimed at `se_cst_dw.sru:L357` and `:L368` | **`:L355` and `:L357`.** Line 368 is a comment, not a call |
| 5 | The fixture's six marked columns span `dw_sqlite.srd:L8-L14` | Columns are **`dw_sqlite.srd:L8-L13`**; `:L14` is the table specification |
| 6 | The column-expression public surface as enumerated | Also includes the **cacheable** flag setters and **two calculate-empty arities**; both are in the contract |
| 7 | Command autocommit is a boolean | **Three-valued** — `AC_OFF`, `AC_ON`, `AC_NATIVE` [`n_cst_thread_task_sqlcommand.sru:L16-L18`] |

### 17.3 What this document does not claim

Stated plainly, because a reference document that overclaims is worse than one with gaps:

- **It does not claim the container bring-up was verified.** Whether the orchestrated stack starts, and
  whether the readiness gate of [§12.2](#122-c-10--health-and-readiness) fires in the documented order,
  is asserted by container-definition and manifest review plus CI. See
  [`ARCHITECTURE.md`](ARCHITECTURE.md) §10.6, which records what was and was not exercised.
- **It does not claim that any of these ten contracts has been implemented.** This document precedes the
  schema and the services; it is the input to them.
- **It does not claim byte-exact parity has been achieved anywhere.** It states where byte-exactness is
  the *criterion* — the paging output of [§8.4](#84-paging-parity-is-byte-exact-generated-sql) and the
  count wrapper of [§8.5](#85-the-count-wrapper-and-its-short-circuit) — and leaves the demonstration to
  [`PARITY.md`](PARITY.md).
- **It does not claim the deferred services are partly built.** They are not built at all
  ([§13.1](#131-the-compliance-note-stated-so-it-is-auditable-rather-than-argued)).
- **It does not claim more than one storage engine is provisioned.** Exactly one is
  ([§8.1](#81-one-storage-engine-is-provisioned-and-it-is-sqlite)).
- **It does not claim that any user-specified rule governs these contracts.** None exists
  ([§1.3](#13-no-user-specified-rules-exist)); the bar applied instead is stated there rather than
  assumed.
- **It reproduces no secret value of any kind.** Where a field carries such material, only the field is
  named and its handling rule stated; the locators live in [`SECRETS.md`](SECRETS.md).
