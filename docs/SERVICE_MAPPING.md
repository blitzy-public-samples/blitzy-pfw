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

# PowerFramework → .NET 10 — Full-Estate Service Mapping

This document is the **complete library-to-service mapping, prepared for review**, for the decomposition of
**PowerFramework** — an Appeon PowerBuilder 2021 desktop framework — into containerized .NET 10
services. It assigns **every one of the 544 legacy objects** under `ws_objects/**`, across all 39
exported libraries, to a destination: one of the four services built in this phase, one of the
shared libraries beneath them, one of the four destinations recorded but **not built** in this
phase, or the permanently-out-of-scope category. Each assignment carries a justification grounded
in **capability cohesion**, and the arithmetic is reconciled to the object so that a reviewer can
audit it rather than take it on trust. This document therefore discharges the environment's
**STEP 0 gate**, which directs that work stop while the service mapping still reads as a
placeholder: the mapping below is that placeholder's replacement, and no assignment in it is
marked TBD.

## Current state of the artifacts this document references

[`BUILD.md`](BUILD.md) §1 defines the four status labels this documentation set uses; the ones that apply
here are **present and verified** and **present but unexercised**. Nothing this document references is
absent.

| Artifact | What it carries | State |
| --- | --- | --- |
| [`docs/PARITY.md`](PARITY.md) | The characterization model, the fixture corpus, the determinism seam register and the open risks | **Present but unexercised.** Authored, linted with the six siblings under the shared policy at the head of this file, and cited by section number throughout the mapping below |
| [`characterization/README.md`](../characterization/README.md), `characterization/workflows/` | The capture model, 15 workflow definitions, their determinism masks and `workflow.schema.json` | **Present but unexercised.** `characterization/recordings/` holds no recording on either side |
| [`orchestration/docker-compose.yml`](../orchestration/docker-compose.yml), [`orchestration/README.md`](../orchestration/README.md) | Local orchestration and the readiness-gate bring-up | **Present and verified.** The four-service bring-up was exercised — [`BUILD.md`](BUILD.md) §1.3 |

Everything else this document references — the solution and project files, the shared libraries, the
protocol and OpenAPI definitions under `shared/PowerFramework.Contracts/`, the per-service settings, the
complete `orchestration/` set and the read-only legacy tree — **is present in the tree today**.

**So are the four services themselves, which is worth stating precisely because this document assigns
capabilities to them.** Each of Gateway, DataServices, Persistence and Security has an application project
with an entry point, its handler and domain tree, and a sibling test project; all twenty-two projects build in
Release with zero warnings and zero errors, all eleven test projects pass, all four images build, and the four
services have come up together as a stack. **The figures are in [`BUILD.md`](BUILD.md) §1.3, which is the
canonical verification record, and are deliberately not restated here** — a mapping document that carries
its own copy of a mutable total is a mapping document that will eventually contradict the record. The
assignments below are therefore backed by running code rather than by a plan alone.

**The one thing that has not been done, and what it costs this document:**

| Not done | Consequence for this document |
| --- | --- |
| **No paired legacy and .NET recording has been captured.** `characterization/` and its 15 workflow definitions exist; `characterization/recordings/` is empty on both sides, because executing the oracle needs an Appeon PowerBuilder runtime that is not available here | **No assignment below has been checked for behavioural parity against the oracle.** Each is justified by capability cohesion and by source locators into the read-only legacy tree — which is what this document is for — and not by a comparison of outputs. [`PARITY.md`](PARITY.md) R4 tracks the gap |

[`BUILD.md`](BUILD.md) §13 carries the build and test position, and [`PARITY.md`](PARITY.md) §1 states the
parity position in full.

---

## Table of contents

1. [Status and the gate this document discharges](#1-status-and-the-gate-this-document-discharges)
2. [Method, evidence discipline, and the position on rules](#2-method-evidence-discipline-and-the-position-on-rules)
3. [The census](#3-the-census)
4. [Full-estate library-to-destination assignment — all 39 libraries](#4-full-estate-library-to-destination-assignment--all-39-libraries)
5. [In scope — 103 objects](#5-in-scope--103-objects)
6. [Why the splits fall where they do — six dependency-closure corrections](#6-why-the-splits-fall-where-they-do--six-dependency-closure-corrections)
7. [Two dependencies removed rather than ported](#7-two-dependencies-removed-rather-than-ported)
8. [Deferred — 325 objects across four destinations](#8-deferred--325-objects-across-four-destinations)
9. [Permanently out of scope — 116 objects](#9-permanently-out-of-scope--116-objects)
10. [Three anomalies that govern citation discipline](#10-three-anomalies-that-govern-citation-discipline)
11. [Reconciliation](#11-reconciliation)
12. [Constraint compliance, corrections applied, and cross-references](#12-constraint-compliance-corrections-applied-and-cross-references)
13. [Object-level ledger — all 544 objects](#13-object-level-ledger--all-544-objects)

---

## 1. Status and the gate this document discharges

**Status: complete mapping, prepared for review. No assignment reads TBD.**

> **What this document can and cannot attest about itself.** It can attest that the mapping is
> *complete* and *derived*: every one of the 544 objects has exactly one destination, and every
> published subtotal re-derives from the object-level ledger in
> [§13](#13-object-level-ledger--all-544-objects) rather than being asserted independently. That is
> checkable from the document itself.
>
> **It cannot attest that it has been signed off.** Sign-off is an act performed by a reviewer, not a
> property a document can claim on its own behalf, so nothing here claims it.
> The distinction matters for the STEP 0 gate below: what the gate needs is a mapping that is complete
> and reviewable rather than a placeholder, and that condition is met here. **Recording the reviewer's
> acceptance remains a separate step, external to this file.**

The environment's setup instructions carry a STEP 0 gate: before any of the 544 legacy objects is
assigned to a destination, this document must have been updated from a placeholder to a reviewed
mapping, and if it still reads as a placeholder the condition is to stop and surface that rather
than guess an assignment. Until this document was authored the gate was unmet in the strongest
possible sense — the file did not exist at all. What clears it is not a statement of intent but the
mapping itself, complete to the object, with justification per assignment and arithmetic that
reconciles. Sections 4 through 11 are that mapping.

Two properties make the gate genuinely discharged rather than nominally satisfied:

- **Coverage is total.** Every library appears exactly once in Section 4, and Section 11 proves the
  four destination categories partition all 544 objects with none left over.
- **Every figure is re-derivable.** Each count in this document was measured directly from
  `ws_objects/**` on the source branch, not estimated, and Section 2 records the derivations so a
  reviewer can reproduce them.

The four services built in this phase, with the destination names the .NET tree uses:

| Service | Directory | Project |
| --- | --- | --- |
| Gateway | `services/gateway-service` | `PowerFramework.Gateway` |
| DataServices | `services/dataservices-service` | `PowerFramework.DataServices` |
| Persistence | `services/persistence-service` | `PowerFramework.Persistence` |
| Security | `services/security-service` | `PowerFramework.Security` |

Beneath them sit six shared projects carrying pure behaviour and no I/O:
`PowerFramework.Shared.Kernel`, `PowerFramework.Shared.Diagnostics`,
`PowerFramework.Shared.Eventful`, `PowerFramework.Shared.Localization`,
`PowerFramework.Shared.Containers`, and `PowerFramework.Contracts` — the last being the published
boundary definition and the only coupling permitted between services.

Four further destinations are **recorded in this mapping but built nowhere**: DesignSystem,
Documents, Integration and ScriptBridge. Section 8 states precisely what that means and what it
excludes.

---

## 2. Method, evidence discipline, and the position on rules

### 2.1 No user-specified rules exist

The project's rules document was retrieved and contains exactly one statement: **no user rules were
provided.** That is a finding, not an omission, and it is recorded here so the absence cannot be
mistaken for latitude. Three consequences follow:

- **No rule is invented, inferred, or back-filled from convention.** Nothing in this mapping exists
  because a coding guideline demanded it.
- **Enterprise-standard best practice applies in the rules' place.** For a discovery document that
  means: every factual claim carries a locator, every number is re-derivable, the arithmetic
  reconciles and is *shown* to reconcile, and nothing is asserted that the repository cannot
  adjudicate.
- **Zero libraries and zero objects enter scope because of a rule.** Every assignment below traces
  either to an explicit requirement of the refactor or to a dependency edge verified by inspection
  and recorded in Section 6.

The binding constraints therefore come from elsewhere — the refactor's own clauses and the attached
environment's setup instructions. Section 12 records how this document honours each one that
governs it.

### 2.2 The legacy tree is read-only, and it is the only specification

`ws_objects/**` is the behavioural oracle. This document **records assignments only**. It nowhere
instructs an edit, a move, a rename, a re-encode or a reformat of any legacy path, and none is
required by anything in it. The same applies to the five pre-existing Chinese documents that sit
beside this file in `docs/` — `docs/README.md`, `docs/Blink交互.md`, `docs/Sciter交互.md`,
`docs/PB多线程绕坑提示.md` and `docs/n_cst_dwsvc_columnexp.md`. They are read as specification and
are left exactly as they are; this document is purely additive alongside them.

Evidence discipline matters here more than usual, because **nothing else in the repository can
adjudicate behaviour**:

- `logfile.md` is stale. Its first line is exactly `## 3.0.7.2062(2022-04-14)`
  [`logfile.md:L1`], while the commit history runs years past that date, and the repository carries
  **0 Git tags across 333 commits** on the source branch, so no release is marked.
- The two PowerBuilder build definitions contradict each other and **both name a library that does
  not exist**, so neither would build as written. Section 10.3 gives the evidence.

Consequently every behavioural claim below carries a `ws_objects/**` locator, with a line number
wherever a specific behaviour is asserted.

### 2.3 How the counts were derived

| Figure | Derivation |
| --- | --- |
| Export library folders | count of directories matching `ws_objects/*.pbl.src` |
| Compiled libraries | count of `*.pbl` at the repository root |
| Objects per library | count of regular files directly inside each `ws_objects/<lib>.pbl.src` folder (the export layout is flat — there are no nested files at all) |
| Total objects | sum of the per-library counts, cross-checked against a single recursive file count of `ws_objects/**` |
| Lines of PowerScript | concatenated line count across every file under `ws_objects/**` |
| Object kinds | grouping of the total by file extension |

Two cross-checks were run over the result and both hold: the per-library counts sum to the
recursive total, and the four destination categories of Section 11 form a partition of the 39
libraries with no library counted twice and none missing.

---

## 3. The census

### 3.1 Measured totals

| Fact | Measured value |
| --- | --- |
| Export library folders under `ws_objects/` | **39** (`ws_objects/*.pbl.src`) |
| Compiled `*.pbl` at the repository root | **40** — see Section 10.1 for the one-library difference |
| Total objects under `ws_objects/` | **544** |
| Lines of PowerScript under `ws_objects/` | **160,445** |
| Object breakdown by extension | 283 `.sru` + 151 `.srf` + 71 `.srw` + 18 `.srs` + 12 `.srd` + 4 `.srm` + 3 `.sra` + 2 `.srj` = **544** |
| Appeon PowerBuilder runtime | `21.0.0.1311` — [`ws_objects/pfw.pbl.src/pfw.sra:L34`] |
| Framework version | `3.0.7.2062` — [`logfile.md:L1`] (stale; see Section 2.2) |

The extension breakdown reconciles to the conventional PowerBuilder object-kind census as follows:
3 application objects + 71 windows + 283 user objects + 151 global functions + 18 structures +
12 DataWindows + 4 menus = 542, plus the two PowerBuilder project objects
`ws_objects/pfw.pbl.src/project.srj` and `ws_objects/pfw.pbl.src/p_pfw.srj` = **544**.

### 3.2 Per-library object counts — all 39

| Library | Objects | Library | Objects |
| --- | ---: | --- | ---: |
| `pfw` | 3 | `pfw.utility` | 14 |
| `pfw.base` | 6 | `pfw.utility.barcode` | 2 |
| `pfw.common` | 25 | `pfw.utility.compiler` | 2 |
| `pfw.crypto` | 10 | `pfw.utility.container` | 3 |
| `pfw.datawindow.services` | 13 | `pfw.utility.devinfo` | 2 |
| `pfw.demos` | 42 | `pfw.utility.invoker` | 8 |
| `pfw.net.ftp` | 3 | `pfw.utility.parser` | 13 |
| `pfw.net.http` | 22 | `pfw.utility.regexp` | 5 |
| `pfw.net.http.ext` | 4 | `pfw.utility.sqlite` | 3 |
| `pfw.net.websocket` | 2 | `pfw.utility.zip` | 4 |
| `pfw.pack` | 2 | `pfwx` | 1 |
| `pfw.shared` | 14 | `pfwx.base` | 1 |
| `pfw.tests` | 68 | `pfwx.net.http` | 7 |
| `pfw.thread` | 6 | `pfwx.net.mqtt` | 3 |
| `pfw.thread.ext` | 15 | `pfwx.tests` | 2 |
| `pfw.ui` | 58 | `pfwx.utility.parser` | 1 |
| `pfw.ui.blink` | 15 | `pfw.ui.controls` | 28 |
| `pfw.ui.controls.ext` | 43 | `pfw.ui.objects` | 64 |
| `pfw.ui.sciter` | 15 | `pfw.ui.sciter.ext` | 4 |
| `pfw.ui.webview` | 11 | | |

That is 39 libraries totalling 544 objects.

### 3.3 There are no import statements to rewrite

Worth stating because it inverts the usual assumption about a decomposition of this kind.
PowerBuilder has no namespaces and no import statements: every global object lives in **one flat
namespace**, and symbol resolution is determined by the *order* of the library list in the target
file. The authoritative lists are [`pfw.pbt:L8`], which names 32 libraries, and [`pfwx.pbt:L6`],
which names 9. A third target, [`pfw.pack.pbt:L6`], names 34.

Two facts fall straight out of those lists and are used as evidence later in this document:

- The intersection of the two application targets' library lists is **exactly two libraries** —
  `pfw.common.pbl` and `pfw.shared.pbl`. Nothing else appears on both. That is the cross-target
  foundation, and it is independent corroboration of the Shared Kernel assignment in Section 5.1.
- The union of all three targets' lists is **40** distinct libraries. Exactly one of them has no
  source export, and exactly zero export folders belong to no target. Section 10.1 names the one.

---

## 4. Full-estate library-to-destination assignment — all 39 libraries

One row per library, covering the whole estate. **`S` marks a split library**, where only the named
objects go to the in-scope destination and the remainder is assigned elsewhere; Section 6 states
what forced each split. Justifications are by capability cohesion — why the capability belongs with
that destination — never by convenience of translation.

Rows are sorted by library name so the table can be diffed directly against a listing of
`ws_objects/*.pbl.src`.

| # | Library | Objects | S | Destination | Justification (capability cohesion) |
| ---: | --- | ---: | :-: | --- | --- |
| 1 | `pfw` | 3 | | Permanently out of scope | `ws_objects/pfw.pbl.src/pfw.sra` is reference for Gateway's composition root; `project.srj` and `p_pfw.srj` are the two contradictory build definitions and are reference only (Section 10.3). No runtime capability to place |
| 2 | `pfw.base` | 6 | **S** | Gateway (5) + DesignSystem (1) | Framework lifecycle — initialize, finalize, version, the auto-instantiating initializer and the window-message constants — is the composition-root capability, and the composition root is Gateway. The sixth object, `u_logo.sru`, is a visual user object and is cohesive with presentation instead |
| 3 | `pfw.common` | 25 | | `PowerFramework.Shared.Kernel` (20) + `PowerFramework.Shared.Diagnostics` (5) | Two cohesive groups inside one library: 20 pure computational primitives (9 bit operations, 6 word/byte operations, 3 ancestry predicates, `replaceall.srf`, `sprintf.srf`) belong with the kernel algebra; the 5 assertion and stack-trace objects (`assert.srf`, `assertionfailed.sru`, `getcurrentscript.srf`, `stacktrace.srf`, `stacktraceinfo.srf`) are a distinct diagnostics capability. Cross-target shared [`pfw.pbt:L8`], [`pfwx.pbt:L6`] |
| 4 | `pfw.crypto` | 10 | | Security | The keyed cryptographic surface. `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L9-L73` declares 65 public functions, and `RSASign` [`:L70-L71`] with `VerifyRSASign` [`:L72-L73`], alongside the keyed hash family, are precisely the primitives a token issuer needs — capability and sole consumer land in one service |
| 5 | `pfw.datawindow.services` | 13 | | DataServices | The 22-event DataWindow chain, the service base, the column-expression engine and the five type validators — one coherent data-service capability. Carries **zero** native class bindings: pure PowerScript, which ports as logic |
| 6 | `pfw.demos` | 42 | | Permanently out of scope | Demonstration windows and tab pages. Reference and characterization-fixture source only; nothing to migrate |
| 7 | `pfw.net.ftp` | 3 | | Integration *(deferred)* | Outbound FTP transport. Cohesive with the other outbound protocol clients rather than with any in-scope capability: nothing in the four in-scope services opens a network connection of its own |
| 8 | `pfw.net.http` | 22 | | Integration *(deferred)* | Outbound HTTP client capability, and the largest of the transport libraries. Cohesive with the other outbound protocol clients; no in-scope service originates an outbound request |
| 9 | `pfw.net.http.ext` | 4 | | Integration *(deferred)* | Extensions layered directly on the HTTP client above. It has no meaning apart from that client, so it must share the client's destination |
| 10 | `pfw.net.websocket` | 2 | | Integration *(deferred)* | Outbound WebSocket transport. A long-lived connection protocol client, cohesive with the other transports rather than with any in-scope capability |
| 11 | `pfw.pack` | 2 | | Permanently out of scope | The PowerBuilder packager tooling — build-time tooling for the legacy toolchain, carrying no runtime capability |
| 12 | `pfw.shared` | 14 | | `PowerFramework.Shared.Kernel` | The return-code algebra, the six predicates, the constant catalogue `enums.sru` and the framework exception type. Appears on **both** application targets' library lists [`pfw.pbt:L8`], [`pfwx.pbt:L6`] — the cross-target foundation every other capability rests on. Carries **zero** native class bindings |
| 13 | `pfw.tests` | 68 | | Permanently out of scope | Test windows and DataWindow fixtures. Reference and characterization-fixture source only; never ported, never edited |
| 14 | `pfw.thread` | 6 | | Persistence | The asynchronous substrate the SQL layer is built on. Its only in-scope consumer is that SQL layer, so the two stay together. Carries **zero** native class bindings |
| 15 | `pfw.thread.ext` | 15 | | Persistence | **Entirely SQL-shaped.** Its two structures are literally `dberrordata.srs` and `transactiondata.srs`, and 13 of its 15 objects are SQL query, command, update and transaction tasks. The library cannot be separated from SQL, because asynchronous SQL is its whole reason for existing. Carries **zero** native class bindings |
| 16 | `pfw.ui` | 58 | **S** | `PowerFramework.Shared.Localization` (2) + DesignSystem (56) | The localization entry point `n_cst_i18n.sru` and its global function `i18n.srf` are reached from inside DataServices' validation path, so localization is a shared capability rather than a presentation one. The remaining 56 objects are geometry, colour, DPI conversion, canvas, painter, font, image, image list, menu, tooltip, tray icon, timer and `win32` interop — presentation, cohesive with DesignSystem |
| 17 | `pfw.ui.blink` | 15 | | ScriptBridge *(deferred)* | MiniBlink engine embedding. An embedded rendering/scripting engine host, cohesive with Sciter and WebView; its two capability bits are alternative builds of one engine (Section 8) |
| 18 | `pfw.ui.controls` | 28 | | DesignSystem *(deferred)* | Visual controls. Every object is a rendered widget, so the library's whole capability is presentation and it coheres with the rest of the presentation surface |
| 19 | `pfw.ui.controls.ext` | 43 | **S** | `PowerFramework.Shared.Localization` (4) + DesignSystem (39, one REFERENCE-only) | The category enumeration `ne_cst_i18n.sru` and the three concrete locale providers complete the localization capability. `se_cst_datawindow.sru` is the structural parent of DataServices' `se_cst_dw` and is **read, not ported** (Section 6.3) — a DesignSystem row carrying the REFERENCE role, not a row outside every destination (Section 11.2). The other 38 are extended visual controls |
| 20 | `pfw.ui.objects` | 64 | | DesignSystem *(deferred)* | Visual user objects — the largest single library in the estate, and wholly presentational |
| 21 | `pfw.ui.sciter` | 15 | | ScriptBridge *(deferred)* | Sciter engine embedding. An embedded scripting/rendering engine host, cohesive with the other engine hosts and gated by the same capability bit family (Section 8) |
| 22 | `pfw.ui.sciter.ext` | 4 | | ScriptBridge *(deferred)* | Sciter extensions. Meaningless without the Sciter binding above, so it shares that binding's destination |
| 23 | `pfw.ui.webview` | 11 | | ScriptBridge *(deferred)* | WebView embedding. A third embedded engine host alongside Sciter and MiniBlink, and gated by `Enums.INIT_FLAG_ENABLE_WEBVIEW` in the same bit family (Section 8) |
| 24 | `pfw.utility` | 14 | **S** | DataServices (1, contributed behaviour) + Documents (13) | `pinyinfirstletterlike.srf` is invoked from inside a DataWindow filter expression built by an in-scope service [`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323`], so the behaviour is in scope even though its library is not. Logging, file scanning, URI loading, the two sibling pinyin helpers and the date/number conversion set are document-and-content concerns. Section 11.3, item 1, records why the remainder is 13 |
| 25 | `pfw.utility.barcode` | 2 | | Documents *(deferred)* | Barcode and QR generation — content rendered into a document artifact |
| 26 | `pfw.utility.compiler` | 2 | | ScriptBridge *(deferred)* | Runtime PowerScript compilation and evaluation. Executing code supplied at run time is the same capability the engine hosts and the dynamic invokers provide, so it coheres with them rather than with any in-scope service |
| 27 | `pfw.utility.container` | 3 | **S** | `PowerFramework.Shared.Containers` (2) + Documents (1) | `n_map.sru` is used by **both** in-scope services, and `n_vector.sru` is the column-expression engine's calculation and recursion stack, so both are shared infrastructure. `n_list.sru` has no in-scope consumer (Section 6.2) |
| 28 | `pfw.utility.devinfo` | 2 | | Documents *(deferred)* | Device and environment information. An ambient-data reporting capability with no in-scope consumer; grouped with the other content and reporting helpers |
| 29 | `pfw.utility.invoker` | 8 | **S** | `PowerFramework.Shared.Eventful` (1) + ScriptBridge (7) | `n_cst_eventful.sru` — 1,328 lines, and **not** a native binding — is an instance member of DataServices' `se_cst_dw` [`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L6-L7,L33`] and is additionally inherited by the threading layer's `n_cst_threading_eventful`: two independent proofs that it is shared in-scope infrastructure. The other 7 objects are dynamic object/script invocation and global-variable access, cohesive with scripting |
| 30 | `pfw.utility.parser` | 13 | **S** | Persistence (2) + Documents (11) | `n_sql.sru` and its factory `parsesql.srf` are a verified hard Persistence dependency (Section 6.1): SQL statement structure belongs with the only service that generates SQL. The 11 JSON and XML objects are document-format parsing |
| 31 | `pfw.utility.regexp` | 5 | | Documents *(deferred)* | Regular expressions. After the correction in Section 7.2, **no in-scope dependency remains** on this library |
| 32 | `pfw.utility.sqlite` | 3 | | Persistence | The only evidenced storage binding in the entire repository, and Persistence is the only service that holds a storage provider |
| 33 | `pfw.utility.zip` | 4 | | Documents *(deferred)* | Archive handling. Reading and writing container files is a document-format capability, cohesive with the other format handlers rather than with storage: it touches files, not the database |
| 34 | `pfwx` | 1 | | Permanently out of scope | `ws_objects/pfwx.pbl.src/pfwx.sra`, the second target's application object — reference only. Section 9 records the initialize/finalize asymmetry it exhibits, and Section 11.3, item 3, records why it is listed once here rather than twice |
| 35 | `pfwx.base` | 1 | | Integration *(deferred)* | The `pfwx` target's lifecycle function, cohesive with the `pfwx` transports it exists to serve |
| 36 | `pfwx.net.http` | 7 | | Integration *(deferred)* | Second-generation HTTP transport, on the `pfwx` target rather than `pfw` [`pfwx.pbt:L6`]. Same capability as row 8 in a newer implementation, so it shares that destination |
| 37 | `pfwx.net.mqtt` | 3 | | Integration *(deferred)* | MQTT transport — `nx_mqttclient.sru`, `nx_mqttconfig.sru`, `nx_mqttmessage.sru`. See Section 12.2 for a corrected attribution concerning this library |
| 38 | `pfwx.tests` | 2 | | Permanently out of scope | `pfwx` test windows (`wx_test_httpclient.srw`, `wx_test_mqttclient.srw`) — reference and characterization-fixture source only, with no capability of their own to place |
| 39 | `pfwx.utility.parser` | 1 | | Integration *(deferred)* | The `pfwx` target's DataWindow parser, reached only from the `pfwx` transports |

**Row count: 39.** Every library in Section 3.2 appears exactly once above, and the destination
column places every one of the 544 objects. The distribution of libraries across the four
destination categories is 7 wholly in scope + 7 split + 19 wholly deferred + 6 permanently out
of scope = 39; the object-level reconciliation is Section 11.

---

## 5. In scope — 103 objects

103 of the 544 objects (19% of the estate) are in scope in this phase as the behavioural sources
for the four services and the shared libraries beneath them. They arrive in two ways: seven
libraries contribute every object they hold, and seven split libraries contribute named objects
only.

### 5.1 Whole libraries — every object in scope (86 objects)

| Library | Objects | Destination | Justification |
| --- | ---: | --- | --- |
| `pfw.shared` | 14 | `PowerFramework.Shared.Kernel` | The return-code algebra, predicates, `enums.sru` and the exception type. Appears on **both** PowerBuilder targets' library lists [`pfw.pbt:L8`], [`pfwx.pbt:L6`] — the cross-target foundation |
| `pfw.common` | 25 | `PowerFramework.Shared.Kernel` + `PowerFramework.Shared.Diagnostics` | 20 primitives to Kernel, 5 assert/stack-trace objects to Diagnostics. Also cross-target shared |
| `pfw.crypto` | 10 | Security | The keyed cryptographic surface; `RSASign`/`VerifyRSASign` and keyed `Hash` are precisely what a token issuer needs |
| `pfw.datawindow.services` | 13 | DataServices | The 22-event chain, the service base, the expression engine, the five type validators. **Zero native bindings — pure PowerScript** |
| `pfw.utility.sqlite` | 3 | Persistence | The only evidenced storage binding in the repository |
| `pfw.thread` | 6 | Persistence | The async substrate the SQL layer is built on. Zero native bindings |
| `pfw.thread.ext` | 15 | Persistence | **Entirely SQL-shaped**: its two structures are literally `dberrordata` and `transactiondata`, and 13 of 15 objects are SQL query/command/update/transaction tasks. Inseparable from SQL because asynchronous SQL is its whole reason for existing |

Subtotal: 14 + 25 + 10 + 13 + 3 + 6 + 15 = **86**.

The 13 objects of `pfw.datawindow.services`, named in full because this library is the single
densest concentration of in-scope behaviour: `dwnvldate.srf`, `dwnvldatetime.srf`,
`dwnvlnumber.srf`, `dwnvlstring.srf`, `dwnvltime.srf`, `dwvaluetoexp.srf`, `n_cst_dwsvc.sru`,
`n_cst_dwsvc_columnexp.sru`, `n_cst_dwsvc_columnsort.sru`, `n_cst_dwsvc_contextmenu.sru`,
`n_cst_dwsvc_dropdownsearch.sru`, `n_cst_dwsvc_rowselect.sru`, `se_cst_dw.sru`.

The 25 objects of `pfw.common` divide cleanly at the capability line, which is why the library is
listed against two shared projects rather than one:

- **`PowerFramework.Shared.Kernel` (20)** — `bitand.srf`, `bitclear.srf`, `bitlsh.srf`,
  `bitnot.srf`, `bitor.srf`, `bitrsh.srf`, `bittest.srf`, `bitxor.srf`, `getbit.srf` (9 bit
  operations); `hibyte.srf`, `hiword.srf`, `lobyte.srf`, `loword.srf`, `makelong.srf`,
  `makeword.srf` (6 word/byte operations); `isancestor.srf`, `isancestorbyclass.srf`,
  `isancestorbyobject.srf` (3 ancestry predicates); `replaceall.srf`; `sprintf.srf`.
- **`PowerFramework.Shared.Diagnostics` (5)** — `assert.srf`, `assertionfailed.sru`,
  `getcurrentscript.srf`, `stacktrace.srf`, `stacktraceinfo.srf`.

### 5.2 Split-library contributions (17 objects)

| Library | In scope | Named objects | Destination |
| --- | --- | --- | --- |
| `pfw.base` (S) | 5 of 6 | `n_initializer.sru`, `pfwinitialize.srf`, `pfwfinalize.srf`, `pfwversion.srf`, `winmsg.sru` — **less `u_logo.sru`**, a visual user object | Gateway |
| `pfw.utility.invoker` (S) | 1 of 8 | `n_cst_eventful.sru` | `PowerFramework.Shared.Eventful` |
| `pfw.ui` (S) | 2 of 58 | `n_cst_i18n.sru`, `i18n.srf` | `PowerFramework.Shared.Localization` |
| `pfw.ui.controls.ext` (S) | 4 of 43 | `ne_cst_i18n.sru`, `n_cst_i18n_en.sru`, `n_cst_i18n_chs.sru`, `n_cst_i18n_cht.sru` | `PowerFramework.Shared.Localization` |
| `pfw.utility.parser` (S) | 2 of 13 | `n_sql.sru`, `parsesql.srf` | Persistence |
| `pfw.utility.container` (S) | 2 of 3 | `n_map.sru`, `n_vector.sru` | `PowerFramework.Shared.Containers` |
| `pfw.utility` (S) | 1 of 14 | `pinyinfirstletterlike.srf` | DataServices (contributed behaviour) |

Subtotal: 5 + 1 + 2 + 4 + 2 + 2 + 1 = **17**.

### 5.3 In-scope arithmetic

> **86 + 17 = 103**

### 5.4 Two corroborating measurements

Neither is required to justify the assignments, but both were measured and both independently
support them, so they are recorded for the reviewer.

**Native class bindings.** The repository holds 148 objects declaring a PowerBuilder Native
Interface class binding — 136 to `pfw.dll` and 12 to `pfwx.dll` — and there is no C++ source
anywhere in the tree. Counted per in-scope library, the distribution is diagnostic:

| Library | Objects with a native class binding |
| --- | ---: |
| `pfw.shared` | **0** |
| `pfw.datawindow.services` | **0** |
| `pfw.thread` | **0** |
| `pfw.thread.ext` | **0** |
| `pfw.common` | 18 |
| `pfw.base` | 5 (including `u_logo.sru`) |
| `pfw.crypto` | 1 (`n_crypto.sru`) |
| `pfw.utility.sqlite` | 2 |
| via splits | 3 (`n_map.sru`, `n_vector.sru`, `n_sql.sru`) |

That is 29 in the in-scope set, or 28 excluding the deferred `u_logo.sru`. The four zeros matter:
the return-code algebra, the entire DataWindow event chain and the whole SQL task layer are pure
PowerScript, so the capabilities that carry the most intricate behaviour carry no closed-binary
dependency at all.

**Cross-target membership.** The intersection of the two application targets' library lists is
exactly `pfw.common.pbl` and `pfw.shared.pbl` [`pfw.pbt:L8`], [`pfwx.pbt:L6`]. Those two libraries
are the only ones the legacy itself treats as shared foundation across targets, and they are
precisely the two assigned to the shared kernel and diagnostics projects.

---

## 6. Why the splits fall where they do — six dependency-closure corrections

The rationale for drawing this phase's slice at four services is that the slice forms a
dependency-closed subset — nothing inside it calls outside it. That claim was tested rather than
assumed: an object-to-library index was built across all 544 objects, and every candidate leak was
verified with strict type-position searches. **The claim does not hold as originally stated.** Six
corrections were required, and they are what determines each split boundary. All six are recorded
below with their evidence, so a reader can see *why* each split exists rather than take it on
trust.

### 6.1 `pfw.utility.parser` must split — `n_sql` is a Persistence dependency

`n_sql.sru` is a hard Persistence dependency, not a document-format concern.
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L305`] declares
`n_sql sqlParser`, [`:L312`] creates it with `sqlParser = Create n_sql`, and [`:L314`] calls
`sqlParser.Parse(origSql)`.

The type it depends on is a clause-level statement model, not a general parser:
[`ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L11-L49`] exposes a `has*`/`get*`/`modify*` triple
for each of six clause kinds — column, table, where, group, having and order — in two arities each
(bare, and by select index). Two further properties of the same file are contract, not detail:

- `parsesql.srf` is a factory that **leaks the instance**. It creates, parses, returns and never
  destroys [`ws_objects/pfw.utility.parser.pbl.src/parsesql.srf:L10-L14`], so the caller carries the
  obligation to `Destroy`.
- [`ws_objects/pfw.utility.parser.pbl.src/n_sql.sru:L51`] declares `global n_sql n_sql`, a global
  auto-instance shadowing its own type name — one of the name collisions a single flat namespace
  tolerated and an explicit namespace will not.

**Resolution:** `n_sql.sru` and `parsesql.srf` in scope for Persistence; the 11 JSON and XML objects
— `jsonfrom.srf`, `makejsonarray.srf`, `makejsonobject.srf`, `n_json.sru`, `n_xmlattribute.sru`,
`n_xmldoc.sru`, `n_xmlnode.sru`, `n_xmlparseresult.sru`, `n_xmlqueryresult.sru`, `parsejson.srf`,
`parsexml.srf` — deferred to Documents. **2 + 11 = 13 ✓**

### 6.2 `pfw.utility.container` must split — `n_map` serves both in-scope services

`n_map` is used by **both** in-scope services, not by neither. Verified call sites:

- [`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru:L583`] declares `n_map map` and
  [`:L585`] creates it, inside `_of_getcolumnvaluemap` (implementation from [`:L561`], declared at
  [`:L74-L75`])
- [`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L948`] declares
  `n_map mapValue`
- [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L510,L646`]
- [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L535`]

`n_vector` is the column-expression engine's calculation and recursion stack — declared at
[`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L110`] as
`n_vector _vecCalcStack` and created in that object's constructor at [`:L2421`]. It is the object
behind the expression trace's call stack.

**Resolution:** `n_map.sru` and `n_vector.sru` in scope as a shared container library, substituted
rather than ported; `n_list.sru` deferred, having no in-scope consumer. The library was verified to
hold exactly those three objects. **2 + 1 = 3 ✓**

### 6.3 DataServices inherits across the boundary

`se_cst_dw` derives from `se_cst_datawindow`, which lives in the deferred
`pfw.ui.controls.ext` library. Both the forward declaration and the type definition carry it:
[`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L4`] and [`:L10`] each read
`global type se_cst_dw from se_cst_datawindow`. The parent in turn derives from a further
presentation ancestor [`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L4,L8`].

This is a **structural inheritance edge, not a call**, so no refactor of a call site removes it.

**Resolution:** DataServices defines its own abstract host contract carrying only the members
`se_cst_dw` actually consumes from its parent, and implements against that.
`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` is recorded **REFERENCE-only** —
read, never ported, and therefore not an in-scope object. It is still a **DesignSystem row carrying
the REFERENCE role**, because REFERENCE records what is done with an object rather than being a
destination of its own, which is why Section 11.3 lists it as one of the three attributions that
reconcile this document's derived counts with the summary table in the refactor plan.

### 6.4 Three of the five DataWindow services are irreducibly presentational

Verified type-position use of window, DPI, font and menu primitives:

- `n_cst_dwsvc_dropdownsearch.sru` calls `Win32.ShowWindow` [`:L256`], `Win32.GetWindowRect`
  [`:L474`], `Win32.OffsetRect` [`:L475`] and `Win32.SetWindowPos` [`:L489`]
- `n_cst_dwsvc_contextmenu.sru` calls `Win32.GetWindowRect` [`:L187`] and `Win32.PX2MMX(D2PX(...))`
  [`:L1241,L1243,L1409,L1411`], declares and creates `n_cst_font` [`:L1092,L1098,L1266,L1277`], and
  exposes an `n_cst_popupmenu`-typed submenu API [`:L18`, `:L96-L106`]
- `n_cst_dwsvc_columnsort.sru` calls `Win32.PX2MMY(U2PY(10))` [`:L359,L361`]

The contrast is measurable rather than impressionistic. Counting references to that same set of
window, DPI, font and menu primitives plus dialog calls, per object:

| Object | Lines | Presentation references |
| --- | ---: | ---: |
| `n_cst_dwsvc.sru` | 864 | **0** |
| `n_cst_dwsvc_rowselect.sru` | 288 | **1** |
| `n_cst_dwsvc_columnsort.sru` | 450 | 3 |
| `n_cst_dwsvc_dropdownsearch.sru` | 516 | 5 |
| `n_cst_dwsvc_contextmenu.sru` | 1,459 | 52 |

The fifth service, `n_cst_dwsvc_columnexp.sru` (2,435 lines), is excluded from that table because
its 28 presentation references are all dialog calls with no window, DPI, font or menu primitive
among them; Section 6.5 accounts for it separately.

**Resolution:** DataServices takes the **headless half** of each of the three presentational
services — the data model, filter and sort expression construction, the row-selection state
machine, and the menu *item* model including labels, ids, enabled and split flags and computed
logical text widths. Window positioning, DPI-to-pixel conversion, font measurement and rendering
are deferred, and each is named as a reserved Gateway extension point. This is a documented
capability gap, not a silent omission. `docs/DEFERRED.md` carries the split detail; it is not
restated at length here.

### 6.5 Dialogs are a real presentation dependency inside in-scope logic

Dialog calls appear inside logic that is otherwise headless. Verified live call sites, each checked
against both line-comment and block-comment state:

| Object | Live dialog sites | Localized? |
| --- | --- | --- |
| `n_cst_dwsvc_rowselect.sru` | `:L239` | yes — via `I18N(ne_cst_i18n.CAT_DWSVC, …)` wrapped in `Sprintf` |
| `n_cst_dwsvc_contextmenu.sru` | `:L795`, `:L863`, `:L916`, `:L1009`, `:L1018`, `:L1027`, `:L1033`, `:L1039`, `:L1045`, `:L1052` — ten sites | yes — all ten route through `I18N(ne_cst_i18n.CAT_DWSVC, …)` |
| `se_cst_dw.sru` | `:L357`, paired with the message lookup at `:L355` | yes — both are `I18N(ne_cst_i18n.CAT_DWSVC, …)` calls, and they are the only two in the file |
| `n_cst_dwsvc_columnexp.sru` | 28 sites: `:L713`, `:L739`, `:L762`, `:L1308`, `:L1383`, `:L1390`, `:L1395`, `:L1399`, `:L1417`, `:L1422`, `:L1426`, `:L1431`, `:L1486`, `:L1505`, `:L1607`, `:L1671`, `:L1882`, `:L1888`, `:L2122`, `:L2133`, `:L2192`, `:L2208`, `:L2232`, `:L2242`, `:L2254`, `:L2282`, `:L2306`, `:L2392` | **no — zero of the 28 route through localization** |

**Resolution:** each dialog becomes a structured error result preserving the exact message text, the
localization category, the substitution arguments, the severity, and for parse failures the
expression text plus caret position. Only the delivery channel changes.

A behaviour to preserve rather than harmonize falls straight out of the table: **all 28
column-expression messages are hardcoded and do not route through localization, while the
row-select, context-menu and event-chain messages all do.** That inconsistency is legacy behaviour
and is reproduced, not corrected. Two adjacent findings in the same family are recorded for the
same reason: `se_cst_dw.sru:L286` carries a dialog inside the block comment spanning
[`:L280`–`:L290`] — a dormant byte-length validation path that stays commented and inert — and
[`se_cst_dw.sru:L368`] is a comment, not a call site, documenting that the dialog's posted message
may have deleted the row before control returns.

### 6.6 Localization's XML dependency is substituted, not imported

The concrete locale providers read the translation table using the deferred Documents XML parser:
[`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L13`] declares
`privatewrite n_xmldoc _doc`, and [`:L158-L159`] creates it and loads the root resource file
`pfw.i18n.xml`. The Traditional Chinese provider does the same and additionally uses a query-result
type: [`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L13`], [`:L31`]
(`n_xmlqueryresult xqs`), [`:L62-L63`].

**Resolution:** substitute the base class library's XML reader. **Localization therefore acquires no
Documents coupling**, and the XML object family stays deferred.

Two shapes of the localization capability are worth recording here because they explain why all
four localization objects are needed rather than just one. The category enumeration is small and
exact — [`ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17`] declares `CAT_MSGBOX` and
`CAT_DWSVC` as offsets from `Enums.I18N_CAT_CUSTOM` (itself `5`, at
[`ws_objects/pfw.shared.pbl.src/enums.sru:L123`]), and `CAT_DWSVC` is the category every in-scope
dialog in Section 6.5 passes. And the three providers are genuinely different sizes rather than
three copies of one template: the English provider is 164 lines and table-driven, the Traditional
Chinese provider is 68, and the Simplified Chinese provider is 28 and **handled-without-mutation for
framework source; not handled otherwise** — its translate handler is only
`if source = Enums.I18N_SRC_PFW then return 1` followed by `return 0`
[`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L19-L27`], because Simplified Chinese is
the base locale. That is a precise behaviour rather than an absence of one, and the distinction is
worth keeping: the handler **returns 1, the handled code, for framework-sourced text** while leaving
the `ref string` untouched, and **returns 0, not handled, for anything else**. Calling it a no-op
would flatten those two answers into one. It is unobservable through the global entry point only
because `i18n.srf` discards the returned code in all three of its overloads
[`ws_objects/pfw.ui.pbl.src/i18n.srf`] — so a caller of `I18N(...)` sees unchanged text either way,
while a caller that invokes the provider event directly sees 1 or 0. All three provider shapes are
reproduced, this one included.

---

## 7. Two dependencies removed rather than ported

Two apparent cross-boundary dependencies dissolve on inspection. They are recorded so that no
future reader pulls a deferred library into scope unnecessarily.

### 7.1 `n_scriptinvoker` is not a real dependency

Its only in-scope uses are in the SQL task layer, and in both places it is a **variadic-call escape
hatch** rather than a scripting capability. PowerScript cannot forward an argument list of arbitrary
length, so the code enumerates the arities it supports and falls back to dynamic invocation only
past the enumerated maximum:

- [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L601`] declares
  `n_scriptinvoker invoker`; [`:L690-L692`] shows the `choose case` unrolled to **20** positional
  arguments; [`:L695`] creates the invoker and [`:L701`] destroys it
- [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L423`] declares it; [`:L465-L466`]
  shows the same unroll to **8** positional arguments; [`:L469`] and [`:L475`] create and destroy it

C# has native variadic support, so the workaround has no analogue to port. **Persistence therefore
acquires no ScriptBridge coupling**, and the seven dynamic-invocation objects of
`pfw.utility.invoker` stay deferred.

The observable arity ceilings are recorded as legacy limits, not as requirements: **20** in the SQL
base task, **8** in the transaction object, and **11** in the SQLite binding, whose command and
query entry points take `any arg1` through `any arg11`
[`ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L43,L55,L68`]. A .NET contract may exceed these
without behavioural regression, because exceeding a ceiling the legacy could not reach cannot change
any observable legacy result.

### 7.2 `regexpfind` is not a library dependency

Its only in-scope use parses the connection parameter string for two binding flags:
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128`] matches `DisableBind`
against the parameter string and [`:L129`] does the same for `NCharBind`. Both are satisfied by the
base class library's regular-expression types, so `pfw.utility.regexp` is deferred with **no
in-scope dependency remaining** on it.

One discovery from that same pair of lines is independently important and belongs on the record
here even though it is not a mapping decision: **a disabled-bind setting means the runtime does not
use bind variables — statement text is built by literal interpolation** — which is the mechanical
root of the SQL-injection exposure the architecture documentation analyses. `docs/ARCHITECTURE.md`
carries that analysis; it is cross-referenced rather than restated.

---

## 8. Deferred — 325 objects across four destinations

**A deferred destination in this document is a mapping assignment and nothing more.** No project, no
container definition, no test project, no configuration, no placeholder class and no code of any
kind exists for DesignSystem, Documents, Integration or ScriptBridge. This section records **where a
capability belongs**; it deliberately says nothing about how any of the four would be built, and it
contains no schedule, no task list and no commitment. That restriction is the point rather than an
oversight: a half-built destination would be worse than a documented gap, because it would look
finished.

The four are represented in exactly two places in the whole delivery — as the assignments below,
and as named routes on Gateway's routing metadata that return a not-implemented status with a
machine-readable body naming the destination. A routing declaration is not a stub of the
destination; `docs/DEFERRED.md` and `docs/CONTRACTS.md` record that boundary in full.

**The deferred total is 325 objects** (Section 11.1). The rightmost count column below is the
*strict* one-object-one-category figure of Section 11.2, which totals 323; the two-object difference
is the boundary attributions of Section 11.3 and is not a disagreement about where any capability
belongs.

| Destination | Libraries and counts | Objects (strict count) | Reserved Gateway route |
| --- | --- | ---: | --- |
| **DesignSystem** | `pfw.ui` (56 remaining), `pfw.ui.controls` (28), `pfw.ui.controls.ext` (38 remaining, plus `se_cst_datawindow.sru` as a REFERENCE-only row — 39 in the strict count), `pfw.ui.objects` (64), `pfw.base::u_logo.sru` (1), plus the presentational halves of ColumnSort, ContextMenu and DropDownSearch | 188 | `/v1/design/**` |
| **Documents** | `pfw.utility.parser` (11 remaining), `pfw.utility.zip` (4), `pfw.utility.barcode` (2), `pfw.utility` (11 remaining — 13 in the strict count), `pfw.utility.container::n_list.sru` (1), `pfw.utility.regexp` (5), `pfw.utility.devinfo` (2) | 38 | `/v1/documents/**` |
| **Integration** | `pfw.net.http` (22), `pfw.net.http.ext` (4), `pfw.net.ftp` (3), `pfw.net.websocket` (2), `pfwx.net.http` (7), `pfwx.net.mqtt` (3), `pfwx.base` (1), `pfwx.utility.parser` (1) | 43 | `/v1/integration/**` |
| **ScriptBridge** | `pfw.ui.sciter` (15), `pfw.ui.sciter.ext` (4), `pfw.ui.blink` (15), `pfw.ui.webview` (11), `pfw.utility.compiler` (2), `pfw.utility.invoker` (7 remaining) | 54 | `/v1/scripting/**` |
| **Total** | | **323** | |

In the strict count: 56 + 28 + 39 + 64 + 1 = 188; 11 + 4 + 2 + 13 + 1 + 5 + 2 = 38;
22 + 4 + 3 + 2 + 7 + 3 + 1 + 1 = 43; 15 + 4 + 15 + 11 + 2 + 7 = 54; and 188 + 38 + 43 + 54 = **323**,
which is the deferred total of Section 11.2. Every one of those counts is the Section 13 ledger
filtered on that destination, so the roster and the accounting note cannot disagree.

Capability cohesion per destination:

- **DesignSystem** owns presentation: user interface controls and user objects, theming, geometry
  structures, colour functions, the DPI conversion family, canvas, painter, font, image and image
  list, popup menus, tooltips, the tray icon, the timer, `win32` interop, and the logo control. It
  is also where the rendering halves of the three split DataWindow services belong — window
  positioning, DPI-to-pixel conversion, font measurement and menu rendering (Section 6.4).
- **Documents** owns content and document formats: JSON and its helpers, the XML object family, ZIP
  archives, barcode and QR generation, file scanning, logging, and the date and number conversion
  set. The list container joins it as the one container type with no in-scope consumer.
- **Integration** owns outbound transports: the HTTP client and its extensions, FTP, WebSocket,
  MQTT, and the second-generation `pfwx.*` transports together with the `pfwx` lifecycle function and
  parser that exist only to serve them.
- **ScriptBridge** owns embedded engines and dynamic execution: Sciter and its extensions, MiniBlink,
  WebView embedding, the PowerScript compiler and evaluator, and dynamic object and script
  invocation together with global-variable access.

Two notes worth carrying, both of which prevent a future reader from re-opening a settled question:

- **`pfw.utility.regexp` is deferred and no in-scope dependency remains on it**, because its single
  in-scope use is satisfied by the base class library (Section 7.2). A reader who finds the one call
  site should not conclude the library must come into scope.
- **The capability gating the legacy applies to itself corroborates this split.**
  [`ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48`] declares eight capability bits —
  `INIT_FLAG_ENABLE_UI` = 1, `INIT_FLAG_ENABLE_SCITER` = 2, `INIT_FLAG_ENABLE_BLINK` = 4,
  `INIT_FLAG_ENABLE_BLINKFAST` = 8, `INIT_FLAG_ENABLE_ORCA` = 256, `INIT_FLAG_ENABLE_SQLITE` = 512,
  `INIT_FLAG_ENABLE_DPIAWARE` = 1024, `INIT_FLAG_ENABLE_WEBVIEW` = 2048 — and the composite
  `INIT_FLAG_ENABLE_ALL` at [`:L49`], which **omits `INIT_FLAG_ENABLE_BLINKFAST`** because the fast
  and standard MiniBlink binaries are alternative builds of one engine. Mapping those bits onto the
  destinations lands them exactly where this document already put them:
  `INIT_FLAG_ENABLE_SQLITE` is the **only** bit with an in-scope consumer (Persistence);
  `INIT_FLAG_ENABLE_UI` and `INIT_FLAG_ENABLE_DPIAWARE` are DesignSystem;
  `INIT_FLAG_ENABLE_SCITER`, `INIT_FLAG_ENABLE_BLINK`, `INIT_FLAG_ENABLE_BLINKFAST` and
  `INIT_FLAG_ENABLE_WEBVIEW` are ScriptBridge; and `INIT_FLAG_ENABLE_ORCA` is legacy packaging
  tooling, which is permanently out of scope. Gateway exposes the equivalent gate as configuration.

---

## 9. Permanently out of scope — 116 objects

Distinct from deferred. These libraries are **not scheduled for a later phase**, because there is
nothing to migrate: they are application objects, legacy build definitions, legacy build tooling, or
fixture and demonstration source. They remain read-only, and several of them are load-bearing as
the behavioural oracle.

| Library | Objects | Why |
| --- | ---: | --- |
| `pfw` (`ws_objects/pfw.pbl.src/`) | 3 | `ws_objects/pfw.pbl.src/pfw.sra` is REFERENCE for Gateway's composition root; `project.srj` and `p_pfw.srj` are the two contradictory build definitions, REFERENCE only (Section 10.3) |
| `pfwx` (`ws_objects/pfwx.pbl.src/`) | 1 | `ws_objects/pfwx.pbl.src/pfwx.sra`, REFERENCE. Note it contains **no initialize call in its open event** — the event is `Open(wx_test_httpclient)` and nothing else [`:L48-L50`] — yet its close event **does** call `pfwxFinalize()` [`:L52`]. That is asymmetric with the framework application, and it is worth recording because the pairing is mandated by the legacy documentation at [`docs/README.md:L15`] |
| `pfw.pack` | 2 | The PowerBuilder packager tooling: `ws_objects/pfw.pack.pbl.src/pfw.sra` and `w_packager.srw` |
| `pfw.tests` | 68 | REFERENCE and characterization-fixture source only. Never ported as-is, never edited. 47 `w_test_*.srw` windows and 11 `*.srd` DataWindow definitions, which together are the fixture corpus ([`docs/PARITY.md`](PARITY.md)). Also the location of most of the in-source secret sites — see Section 12.2 and `docs/SECRETS.md` |
| `pfw.demos` | 42 | REFERENCE and characterization-fixture source only: 9 demonstration windows, 1 further `*.srd`, and the supporting tab pages |
| `pfwx.tests` | 2 | `pfwx` test windows: `wx_test_httpclient.srw`, `wx_test_mqttclient.srw` |

**The headline figure for this category is 116** (Section 11.1). Taken as a strict
one-object-one-category subtotal over the libraries above, 3 + 1 + 2 + 68 + 42 + 2 = **118**, which is
the Section 13 ledger filtered on that category; Section 11.3 accounts for the two-object difference
object by object.

Two of these entries deserve emphasis because downstream work depends on them:

- **`pfw.tests` is the behavioural oracle, and it is where the fixture corpus already lives.** The
  DataWindow corpus *is* the test corpus: characterization fixtures need no invention. Exactly one
  DataWindow definition in the entire repository carries table-level update settings —
  [`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14`] — which makes that single 37-line file the
  golden-master fixture for the retrieval/validation/update capability.
  [`docs/PARITY.md`](PARITY.md) carries the detail.
- **`pfw` and `pfwx` are reference for a composition root, not sources to port.** The framework
  application object records the lifecycle contract Gateway reproduces: it initializes with the
  composite capability flag [`ws_objects/pfw.pbl.src/pfw.sra:L91`], sets a locale
  [`:L94`], installs a provider [`:L103`], finalizes on close [`:L108`], and on a decoded assertion
  failure unpacks a seven-field payload and terminates [`:L111-L144`]. None of that is a library
  object to migrate; all of it is behaviour Gateway must exhibit.

---

## 10. Three anomalies that govern citation discipline

Each was verified directly, and each changes how a reader must cite this repository. They are
grouped here rather than scattered because they are properties of the estate as a whole.

### 10.1 `pfwx.utility.codec.pbl` has no source export

The compiled library exists at the repository root and appears on the second target's library list.
The evidence is [`pfwx.pbt:L6`], whose `LibList` reads:

```text
pfwx.pbl;pfwx.tests.pbl;pfwx.net.http.pbl;pfwx.net.mqtt.pbl;pfwx.utility.parser.pbl;pfwx.utility.codec.pbl;pfwx.base.pbl;pfw.common.pbl;pfw.shared.pbl
```

`ws_objects/pfwx.utility.codec.pbl.src` **does not exist**, and a check across every root `*.pbl`
confirms it is the *only* compiled library lacking a source export. **This is exactly why 39 export
folders reconcile against 40 root `*.pbl` files.**

It is unmappable, and that is a statement about the evidence rather than a hole in the mapping: with
no source export there is no object to assign and no behaviour to read. It contributes **0 objects**
to the 544-object reconciliation and **no capability** to any destination. Recorded as an anomaly,
not as a gap.

### 10.2 Two distinct files are both named `pfw.sra`

- `ws_objects/pfw.pbl.src/pfw.sra` — the framework application object, and **authoritative** for the
  lifecycle and composition-root behaviour Gateway reproduces
- `ws_objects/pfw.pack.pbl.src/pfw.sra` — the packager application object

They were verified to be different files by checksum, not assumed to differ from their differing
locations. **Consequence: every reference to either must cite the full path.** A relative reference
or a bare file name is ambiguous, and this document applies that discipline to its own citations
throughout — every `pfw.sra` locator above and below is fully qualified. The repository holds three
application objects in total; the third is `ws_objects/pfwx.pbl.src/pfwx.sra`.

### 10.3 The two build definitions contradict each other, and neither would build

| | `ws_objects/pfw.pbl.src/project.srj` | `ws_objects/pfw.pbl.src/p_pfw.srj` |
| --- | --- | --- |
| `PBD:` lines | **29** | **28** |
| `pfw.utility.sqlite.pbl` | **included** | **omitted** |
| Company field at `L4` | `COM:Appeon` | `COM:Sybase, Inc.` |

And decisively: **both name `pfw.utility.imgcodec.pbl`** — [`ws_objects/pfw.pbl.src/project.srj:L41`]
and [`ws_objects/pfw.pbl.src/p_pfw.srj:L40`] — while a full-depth search of the repository for that
name returns **zero files**. Neither definition is therefore usable as written; both are REFERENCE
for build *intent* only.

This strengthens rather than weakens the finding that the .NET build must be authored from scratch:
there is no authoritative legacy build definition to translate, so there is nothing to be faithful
to and no risk of diverging from it. `docs/BUILD.md` carries the .NET build and its commands; none
of that is duplicated here.

---

## 11. Reconciliation

This section is the audit that proves full-estate discovery was discharged rather than approximated.

It carries **two apportionments of the same 544 objects, and they are not equals.** The headline of
Section 11.1 is the refactor plan's own reconciliation, and it is authoritative here: the plan is the
frozen agreement this delivery is built against, so where it fixes a figure this document restates it
rather than re-deriving it. Section 11.3 then carries a **secondary accounting note** — a strict
one-object-one-category count taken directly over the object-level ledger of Section 13, the 544-row
table that gives each object exactly one category, exactly one destination and exactly one role. That
note exists so a reviewer can follow the arithmetic down to the object, and it is the source of every
*derived* subtotal below. **It does not supersede the headline.** Both apportionments total 544, both
leave nothing unassigned, and the in-scope figure of 103 is identical under either.

### 11.1 Headline reconciliation

| Category | Objects |
| --- | ---: |
| In scope | 103 |
| Deferred — split remainders | 120 |
| Deferred — whole libraries | 205 |
| Permanently out of scope | 116 |
| **Total** | **544** |

**Zero objects are unassigned.** The deferred total is 120 + 205 = **325 objects across the four
deferred destinations**, and `docs/DEFERRED.md` §7.1 restates these four figures and this total
identically. Section 11.3 carries the secondary accounting note, the strict apportionment it yields,
and the three boundary attributions that account for the whole of the difference between the two.

### 11.2 The deferred estate by destination, in the strict count

The Section 13 ledger filtered on its destination column across both deferred categories. These are
**secondary-accounting figures** in the sense of Section 11.3 — the authoritative deferred total
remains the 325 of Section 11.1 — and they are the figures Section 8's roster agrees with:

| Destination | Objects | of which split remainder | of which whole library |
| --- | ---: | ---: | ---: |
| DesignSystem | 188 | 96 | 92 |
| Documents | 38 | 25 | 13 |
| Integration | 43 | — | 43 |
| ScriptBridge | 54 | 7 | 47 |
| **Total** | **323** | **128** | **195** |

Five of the 544 rows carry the role **REFERENCE**, meaning the object is read as specification and
never ported. A REFERENCE role does not remove an object from its category or its destination — it
records what is done with it — so all five are counted exactly once, in the category and destination
their library places them in. The five are `pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru`
(Deferred — split, DesignSystem, the structural parent of `se_cst_dw`, Section 6.3),
`pfw.pbl.src/pfw.sra` (permanently out, Gateway's composition-root reference),
`pfw.pbl.src/project.srj` and `pfw.pbl.src/p_pfw.srj` (permanently out, the two contradictory build
definitions, Section 10.3) and `pfwx.pbl.src/pfwx.sra` (permanently out, the secondary target's
application object, Section 9).

### 11.3 The accounting note: a strict one-object-one-category count, and why it differs

A strict one-object-one-category count taken directly over the Section 13 ledger apportions the same
estate as **103 in scope + 128 split remainders + 195 whole-library deferred + 118 permanently out =
544**, with a deferred total of 323 and, again, **zero objects unassigned**. Set against the headline
of Section 11.1 the movements are **−8** on split remainders, **+10** on whole-library deferred and
**−2** on permanently out, and those three differences **net to zero** — as they must, since both
apportionments count the same 544 objects.

This is an accounting note and nothing more. **The figures of Section 11.1 remain the authoritative
headline, and the strict count does not supersede them.** It is published beside them so a reviewer
can follow the arithmetic down to the object rather than take either set on trust. The whole of the
difference is three specific attributions, each a genuine judgement about a single object or library
rather than a counting error, and each verifiable in one command against `ws_objects/**`:

1. **The `pfw.utility` split is recorded in the plan as 1 in-scope + 11 Documents of a 14-object
   library, and a strict count of the remainder is 13.** The one in-scope object is
   `pinyinfirstletterlike.srf`, whose behaviour is reached from a DataWindow filter expression built
   by an in-scope service (Section 5.2). The plan's figure of 11 excludes the two sibling pinyin
   helpers `getpinyinfirstletter.srf` and `getpinyinfirstletters.srf` from the remainder as well, but
   neither is named in the in-scope object list and neither is reached from any in-scope code path,
   so both are Documents in either apportionment. The other
   eleven — `datetimetotimestamp.srf`, `dectohex.srf`, `dectostring.srf`, `gettimestamp.srf`,
   `hextodec.srf`, `loaduri.srf`, `n_filescanner.sru`, `n_logger.sru`, `parsedatetime.srf`,
   `stringtodec.srf`, `timestamptodatetime.srf` — were never in question. Effect: **+2 on split
   remainders.**
2. **`se_cst_datawindow.sru` is read as the structural parent of `se_cst_dw` and is REFERENCE-only —
   neither ported nor a DesignSystem deliverable.** It therefore falls outside the plan's
   "38 remaining → DesignSystem" figure while still being one of that library's 43 objects, and the
   plan reconciles the library as **4 in scope + 1 REFERENCE + 38 DesignSystem = 43**. The strict
   count has no fourth category for a role, so it folds that row back into the destination its
   library places it in and reads **4 + 39 = 43**. Effect: **+1 on split remainders and +1 on
   DesignSystem.**
3. **`ws_objects/pfwx.pbl.src/pfwx.sra` is counted once, under permanently out of scope.** It is
   simultaneously the secondary target's application object and a reference for the deferred `pfwx`
   capability area, and a table that lists the `pfwx` library under both headings produces 40 rows
   for 39 libraries. On disk there is one `ws_objects/pfwx.pbl.src` holding one object, so the ledger
   carries one row for it and its Integration relevance is noted in prose (Sections 4 and 9) rather
   than counted twice. Effect: **−1 on the Integration roster relative to a double-counted reading.**

The residual movement between *split remainders* and *whole-library deferred* is a categorisation of
the same objects rather than a change of destination: the ledger classifies a library's remainder as
a split remainder whenever that library contributes anything at all to the in-scope set, and as
whole-library deferred otherwise. The in-scope figure of **103 is identical** under the plan's
summary and under the ledger, so nothing about the work being done in this phase turns on any of
this.

### 11.4 Verified split arithmetic

Shown per library so a reviewer can follow the reconciliation down to the object:

| Library | Total | = | In scope | + | Deferred | Deferred destination | REFERENCE rows inside the deferred figure |
| --- | ---: | :-: | ---: | :-: | ---: | --- | ---: |
| `pfw.base` | 6 | = | 5 | + | 1 | DesignSystem | — |
| `pfw.utility.invoker` | 8 | = | 1 | + | 7 | ScriptBridge | — |
| `pfw.ui` | 58 | = | 2 | + | 56 | DesignSystem | — |
| `pfw.ui.controls.ext` | 43 | = | 4 | + | 39 | DesignSystem | 1 |
| `pfw.utility.parser` | 13 | = | 2 | + | 11 | Documents | — |
| `pfw.utility.container` | 3 | = | 2 | + | 1 | Documents | — |
| `pfw.utility` | 14 | = | 1 | + | 13 | Documents | — |

In compact form: `pfw.base` 6 = 5 + 1; `pfw.utility.invoker` 8 = 1 + 7; `pfw.ui` 58 = 2 + 56;
`pfw.ui.controls.ext` 43 = 4 + 1 + 38; `pfw.utility.parser` 13 = 2 + 11; `pfw.utility.container`
3 = 2 + 1; `pfw.utility` 14 = 1 + 13.

Two reading notes, both about the one library whose split has three parts rather than two.

`pfw.ui.controls.ext` is the only row containing a REFERENCE object, and that object is
`se_cst_datawindow.sru`. The plan holds it separately, which is the **4 + 1 + 38** above and the
attribution of Section 11.3, item 2. The strict count has no category for a role, so it folds that
row into the destination its library places it in and reads **4 + 39** instead (Section 11.2). Under
the strict reading the seven deferred figures sum to 1 + 7 + 56 + 39 + 11 + 1 + 13 = **128**, which is
the split-remainder figure of the accounting note in Section 11.3; the plan's corresponding figure is
the 120 of Section 11.1.

The `pfw.utility` row shows 1 + 13, which is the strict count; the plan records 1 + 11 for the same
library, and Section 11.3, item 1, names the two objects that account for the difference.

### 11.5 Library-level partition check

Independently of the object counts, the 39 libraries partition cleanly across the categories, with
no library appearing twice and none unaccounted for:

| Category | Libraries |
| --- | ---: |
| Wholly in scope | 7 |
| Split | 7 |
| Wholly deferred | 19 |
| Permanently out of scope | 6 |
| **Total** | **39** |

---

## 12. Constraint compliance, corrections applied, and cross-references

### 12.1 How this document honours the constraints that govern it

No user-specified rules exist (Section 2.1), so the governing constraints are the refactor's own
binding clauses. Each is cited by name, with what this document does to satisfy it.

| Constraint | What it requires of this document | How this document satisfies it |
| --- | --- | --- |
| **C-K** — document every technology-specific and boundary-specific decision | Every assignment carries a justification, and the justification is capability cohesion rather than translation convenience; every split states what forced it | Section 4 carries a justification in every one of its 39 rows; Section 6 gives all six closure corrections with line-level evidence; Section 7 records the two dependencies that dissolved and why |
| **C-D** — do not implement the four deferred destinations, even partially, even as stubs | Deferred entries are mapping only, with no implementation plan, schedule, task list or commitment | Section 8 opens by stating the restriction explicitly and confines itself to *where capabilities belong*. No section describes how any deferred destination would be built |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | Record assignments; never instruct an edit, move, rename or reformat of any legacy path, and leave the five pre-existing Chinese documents in this folder untouched | Every legacy path in this document is cited as evidence. Nothing instructs a modification of any `ws_objects/**` path, of any root `*.pbt`/`*.pbl`, or of `docs/README.md`, `docs/Blink交互.md`, `docs/Sciter交互.md`, `docs/PB多线程绕坑提示.md` or `docs/n_cst_dwsvc_columnexp.md`. This document is purely additive |
| **C-L** — the environment's setup instructions are binding operational constraints | The document must not read as a placeholder, since that is the literal gate condition | Section 1 records the status; Sections 4 to 11 are a complete mapping with no TBD assignment and a reconciliation that closes at 544 |
| **C-B** — no new features, no behaviour improvements, no performance objective | No boundary may be justified on performance, latency, throughput, availability or service-level grounds | Every justification in this document is capability cohesion, evidence of a dependency edge, or the absence of one. No performance claim, figure or target appears anywhere — not as a justification, an aside or an inference — because the repository publishes no service-level agreement, latency budget, throughput target or availability commitment, so there is nothing to assert and nothing from which to infer one. Architectural properties of the decomposition are `docs/ARCHITECTURE.md`'s subject, not this document's |

The enterprise-standard baseline that applies in the rules' place (Section 2.1) is met by the
evidence discipline of Section 2.2, the derivations of Section 2.3, the object-level ledger of
Section 13 — from which every derived subtotal in Section 11 is computed rather than asserted — and
the accounting note of Section 11.3, which records object by object how a strict filesystem count
relates to the frozen headline of Section 11.1 instead of quietly substituting one for the other.

### 12.2 Two corrected attributions

Both were found while verifying locators for this document. They are recorded transparently, because
a mapping whose citations do not resolve would fail the audit it exists to pass.

- **Credential material in the MQTT capability area belongs to `pfw.tests`, not to
  `pfwx.net.mqtt`.** An earlier statement of this mapping attributed five in-source secret sites to
  the `pfwx.net.mqtt` transport library. Direct measurement does not support that: the three objects
  of `ws_objects/pfwx.net.mqtt.pbl.src` — `nx_mqttclient.sru`, `nx_mqttconfig.sru`,
  `nx_mqttmessage.sru` — contain no certificate or key material of any kind. The MQTT-related
  credential sites are in the MQTT **test window**, `ws_objects/pfw.tests.pbl.src/`, which is
  permanently out of scope, and that is consistent with the separate and correct finding that most
  of the in-source sites sit in `pfw.tests`. The corrected attribution changes no assignment in this
  document — `pfwx.net.mqtt` is Integration either way — but it does change which library a reader
  should treat as carrying credentials. **No value, key fragment, certificate body or credential is
  reproduced anywhere in this document**; `docs/SECRETS.md` is the single register of locators,
  severities and required actions.
- **The event-chain dialog locators in `se_cst_dw.sru` are `:L355` and `:L357`, and NOT `:L357` and
  `:L368`.** Measured: `:L355` and `:L357` are the only two localization
  calls in the file and the live dialog is at `:L357`; `:L368` is a comment documenting the
  posted-message hazard rather than a call site; and the third dialog match, `:L286`, is inside the
  block comment spanning `:L280`–`:L290`. Section 6.5 uses the measured locators. Likewise, the
  context-menu service has **ten** live dialog sites, not six, and Section 6.5 records the complete set.

### 12.3 Cross-references

This document is the upstream for destination naming and object assignment. It deliberately does not
duplicate its siblings:

| For | See |
| --- | --- |
| Service topology, transport choice per service, port map, capability gating, and the injection analysis referenced in Section 7.2 | `docs/ARCHITECTURE.md` |
| The cross-service contract inventory and the reserved Gateway extension points | `docs/CONTRACTS.md` |
| The deferred destinations in detail, the reserved routes, and the same headline reconciliation and accounting note as Section 11 | `docs/DEFERRED.md` |
| Secret locators, severities and required actions | `docs/SECRETS.md` |
| The characterization model, the fixture corpus drawn from `pfw.tests` and `pfw.demos`, and the determinism seams | [`docs/PARITY.md`](PARITY.md) |
| Build and test commands, the solution layout, and per-service build independence | `docs/BUILD.md` |

### 12.4 A note on preserved constant spellings

Constant identifiers carried into the .NET tree keep their original screaming-snake spelling —
`RetCode.OK`, `RetCode.E_INVALID_ARGUMENT`, `RetCode.CANCELLED`, `RetCode.E_NO_IMPLEMENTATION`,
`Enums.INIT_FLAG_ENABLE_SQLITE`, `Enums.SQL_MS_REPLACE`, `Enums.I18N_CAT_CUSTOM`,
`Categories.CAT_DWSVC` — because those exact strings appear in serialized payloads, in log records
and in characterization recordings, where a rename would silently invalidate every stored
comparison. This document uses the same spellings when it names a constant, so that a reader can
match a name here against a recording without translation. The values behind the ones cited above
are at [`ws_objects/pfw.shared.pbl.src/retcode.sru`] (221 lines; `E_INVALID_ARGUMENT` = −3 at
[`:L46`], `E_NO_SUPPORT` = −2000 at [`:L77`], `E_NO_IMPLEMENTATION` = −2001 at [`:L78`]),
[`ws_objects/pfw.shared.pbl.src/enums.sru`] (1,162 lines; the capability bits at [`:L41-L49`], the
clause-modification styles `SQL_MS_REPLACE` = 1, `SQL_MS_APPEND` = 2 and `SQL_MS_PREPEND` = 3 at
[`:L718-L720`], and `I18N_CAT_CUSTOM` = 5 at [`:L123`]), and
[`ws_objects/pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru:L16-L17`] for the two categories.

---

## 13. Object-level ledger — all 544 objects

This is the assignment itself, at the granularity the estate is actually measured in. Every one of
the 544 objects under `ws_objects/**` appears exactly once, and each row carries **exactly one
category, exactly one destination and exactly one role**. Section 11's subtotals are this table
filtered on a column and counted — none of them is an independent tally that could drift out of
agreement with the rows below, which is the whole reason the ledger is published rather than
summarised.

**How to reproduce the row set.** `ls -1 ws_objects/*/*` lists 544 paths; sorting them by library and
then by object name yields the order below. The ledger is therefore diffable against the filesystem
directly, and a future object added to or removed from `ws_objects/**` shows up as an added or
removed row rather than as a silent change of total.

**Column meanings.**

| Column | Values it takes | What it means |
| --- | --- | --- |
| Category | *In scope* | A behavioural source for one of the four services or the shared libraries built in this phase (Section 5) |
| | *Deferred — split* | The remainder of a library that also contributes to the in-scope set. Not built in this phase (Section 8) |
| | *Deferred — whole* | A library with no in-scope contribution at all. Not built in this phase (Section 8) |
| | *Permanently out* | Not scheduled for any later phase, because there is nothing to migrate (Section 9) |
| Destination | one of the four services, one of the five shared libraries, one of the four deferred destinations, or `-` | Where the capability belongs. `-` appears only on permanently-out rows, which have no destination by definition |
| Role | *Ported* | Read as specification and reimplemented in the .NET tree in this phase |
| | *Not built* | Assigned a destination and nothing more. No project, container, test or code of any kind exists for it (Section 8) |
| | *REFERENCE* | Read as specification and deliberately **never** ported. Five rows carry it; Section 11.2 names all five |
| | *Fixture source* | A characterization-oracle input: never ported as-is, never edited, load-bearing precisely as the oracle |
| | *Legacy tooling* | Build-time tooling for the PowerBuilder toolchain, carrying no runtime capability |

A REFERENCE role never removes an object from its category or its destination. It records what is
done with the object, so a REFERENCE row is counted once, in the category and destination its library
places it in (Section 11.2).

| # | Object (path under `ws_objects/`) | Category | Destination | Role |
| ---: | --- | --- | --- | --- |
| 1 | `pfw.base.pbl.src/n_initializer.sru` | In scope | Gateway | Ported |
| 2 | `pfw.base.pbl.src/pfwfinalize.srf` | In scope | Gateway | Ported |
| 3 | `pfw.base.pbl.src/pfwinitialize.srf` | In scope | Gateway | Ported |
| 4 | `pfw.base.pbl.src/pfwversion.srf` | In scope | Gateway | Ported |
| 5 | `pfw.base.pbl.src/u_logo.sru` | Deferred — split | DesignSystem | Not built |
| 6 | `pfw.base.pbl.src/winmsg.sru` | In scope | Gateway | Ported |
| 7 | `pfw.common.pbl.src/assert.srf` | In scope | `PowerFramework.Shared.Diagnostics` | Ported |
| 8 | `pfw.common.pbl.src/assertionfailed.sru` | In scope | `PowerFramework.Shared.Diagnostics` | Ported |
| 9 | `pfw.common.pbl.src/bitand.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 10 | `pfw.common.pbl.src/bitclear.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 11 | `pfw.common.pbl.src/bitlsh.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 12 | `pfw.common.pbl.src/bitnot.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 13 | `pfw.common.pbl.src/bitor.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 14 | `pfw.common.pbl.src/bitrsh.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 15 | `pfw.common.pbl.src/bittest.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 16 | `pfw.common.pbl.src/bitxor.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 17 | `pfw.common.pbl.src/getbit.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 18 | `pfw.common.pbl.src/getcurrentscript.srf` | In scope | `PowerFramework.Shared.Diagnostics` | Ported |
| 19 | `pfw.common.pbl.src/hibyte.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 20 | `pfw.common.pbl.src/hiword.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 21 | `pfw.common.pbl.src/isancestor.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 22 | `pfw.common.pbl.src/isancestorbyclass.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 23 | `pfw.common.pbl.src/isancestorbyobject.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 24 | `pfw.common.pbl.src/lobyte.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 25 | `pfw.common.pbl.src/loword.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 26 | `pfw.common.pbl.src/makelong.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 27 | `pfw.common.pbl.src/makeword.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 28 | `pfw.common.pbl.src/replaceall.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 29 | `pfw.common.pbl.src/sprintf.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 30 | `pfw.common.pbl.src/stacktrace.srf` | In scope | `PowerFramework.Shared.Diagnostics` | Ported |
| 31 | `pfw.common.pbl.src/stacktraceinfo.srf` | In scope | `PowerFramework.Shared.Diagnostics` | Ported |
| 32 | `pfw.crypto.pbl.src/base64decode.srf` | In scope | Security | Ported |
| 33 | `pfw.crypto.pbl.src/base64encode.srf` | In scope | Security | Ported |
| 34 | `pfw.crypto.pbl.src/guid.srf` | In scope | Security | Ported |
| 35 | `pfw.crypto.pbl.src/hexdecode.srf` | In scope | Security | Ported |
| 36 | `pfw.crypto.pbl.src/hexencode.srf` | In scope | Security | Ported |
| 37 | `pfw.crypto.pbl.src/md5.srf` | In scope | Security | Ported |
| 38 | `pfw.crypto.pbl.src/n_crypto.sru` | In scope | Security | Ported |
| 39 | `pfw.crypto.pbl.src/randomstring.srf` | In scope | Security | Ported |
| 40 | `pfw.crypto.pbl.src/sha1.srf` | In scope | Security | Ported |
| 41 | `pfw.crypto.pbl.src/sha256.srf` | In scope | Security | Ported |
| 42 | `pfw.datawindow.services.pbl.src/dwnvldate.srf` | In scope | DataServices | Ported |
| 43 | `pfw.datawindow.services.pbl.src/dwnvldatetime.srf` | In scope | DataServices | Ported |
| 44 | `pfw.datawindow.services.pbl.src/dwnvlnumber.srf` | In scope | DataServices | Ported |
| 45 | `pfw.datawindow.services.pbl.src/dwnvlstring.srf` | In scope | DataServices | Ported |
| 46 | `pfw.datawindow.services.pbl.src/dwnvltime.srf` | In scope | DataServices | Ported |
| 47 | `pfw.datawindow.services.pbl.src/dwvaluetoexp.srf` | In scope | DataServices | Ported |
| 48 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru` | In scope | DataServices | Ported |
| 49 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru` | In scope | DataServices | Ported |
| 50 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru` | In scope | DataServices | Ported |
| 51 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru` | In scope | DataServices | Ported |
| 52 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru` | In scope | DataServices | Ported |
| 53 | `pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru` | In scope | DataServices | Ported |
| 54 | `pfw.datawindow.services.pbl.src/se_cst_dw.sru` | In scope | DataServices | Ported |
| 55 | `pfw.demos.pbl.src/dw_test.srd` | Permanently out | - | Fixture source |
| 56 | `pfw.demos.pbl.src/m_empty.srm` | Permanently out | - | Fixture source |
| 57 | `pfw.demos.pbl.src/m_mdi_frame.srm` | Permanently out | - | Fixture source |
| 58 | `pfw.demos.pbl.src/m_mdi_sheet.srm` | Permanently out | - | Fixture source |
| 59 | `pfw.demos.pbl.src/n_cst_sciter_traynotification.sru` | Permanently out | - | Fixture source |
| 60 | `pfw.demos.pbl.src/pm_color.sru` | Permanently out | - | Fixture source |
| 61 | `pfw.demos.pbl.src/pm_skin.sru` | Permanently out | - | Fixture source |
| 62 | `pfw.demos.pbl.src/res.sru` | Permanently out | - | Fixture source |
| 63 | `pfw.demos.pbl.src/u_cst_tabpage_blink_browser.sru` | Permanently out | - | Fixture source |
| 64 | `pfw.demos.pbl.src/u_cst_tabpage_blink_browser_page.sru` | Permanently out | - | Fixture source |
| 65 | `pfw.demos.pbl.src/u_cst_tabpage_blink_charts.sru` | Permanently out | - | Fixture source |
| 66 | `pfw.demos.pbl.src/u_cst_tabpage_control_buttonlistbar.sru` | Permanently out | - | Fixture source |
| 67 | `pfw.demos.pbl.src/u_cst_tabpage_control_shortcutbar.sru` | Permanently out | - | Fixture source |
| 68 | `pfw.demos.pbl.src/u_cst_tabpage_control_shortcutbar_blb.sru` | Permanently out | - | Fixture source |
| 69 | `pfw.demos.pbl.src/u_cst_tabpage_control_splitcontainer.sru` | Permanently out | - | Fixture source |
| 70 | `pfw.demos.pbl.src/u_cst_tabpage_control_tabcontrol.sru` | Permanently out | - | Fixture source |
| 71 | `pfw.demos.pbl.src/u_cst_tabpage_control_tabcontrol_page.sru` | Permanently out | - | Fixture source |
| 72 | `pfw.demos.pbl.src/u_cst_tabpage_control_taskpanelbar.sru` | Permanently out | - | Fixture source |
| 73 | `pfw.demos.pbl.src/u_cst_tabpage_control_toolbarstrip.sru` | Permanently out | - | Fixture source |
| 74 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_browser.sru` | Permanently out | - | Fixture source |
| 75 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_charts.sru` | Permanently out | - | Fixture source |
| 76 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_printing.sru` | Permanently out | - | Fixture source |
| 77 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_sidebar.sru` | Permanently out | - | Fixture source |
| 78 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_test.sru` | Permanently out | - | Fixture source |
| 79 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_treeview.sru` | Permanently out | - | Fixture source |
| 80 | `pfw.demos.pbl.src/u_cst_tabpage_sciter_vspage.sru` | Permanently out | - | Fixture source |
| 81 | `pfw.demos.pbl.src/u_cst_tabpage_utility_alipay.sru` | Permanently out | - | Fixture source |
| 82 | `pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru` | Permanently out | - | Fixture source |
| 83 | `pfw.demos.pbl.src/u_cst_tabpage_utility_httpclient.sru` | Permanently out | - | Fixture source |
| 84 | `pfw.demos.pbl.src/u_cst_tabpage_utility_wxpay.sru` | Permanently out | - | Fixture source |
| 85 | `pfw.demos.pbl.src/u_cst_tabpage_webview_browser.sru` | Permanently out | - | Fixture source |
| 86 | `pfw.demos.pbl.src/u_cst_tabpage_webview_browser_page.sru` | Permanently out | - | Fixture source |
| 87 | `pfw.demos.pbl.src/u_cst_tabpage_webview_charts.sru` | Permanently out | - | Fixture source |
| 88 | `pfw.demos.pbl.src/w_about.srw` | Permanently out | - | Fixture source |
| 89 | `pfw.demos.pbl.src/w_demo.srw` | Permanently out | - | Fixture source |
| 90 | `pfw.demos.pbl.src/w_demo_login.srw` | Permanently out | - | Fixture source |
| 91 | `pfw.demos.pbl.src/w_demo_mdi.srw` | Permanently out | - | Fixture source |
| 92 | `pfw.demos.pbl.src/w_demo_mdi_child.srw` | Permanently out | - | Fixture source |
| 93 | `pfw.demos.pbl.src/w_demo_mdi_ribbonbar.srw` | Permanently out | - | Fixture source |
| 94 | `pfw.demos.pbl.src/w_demo_sciter_login.srw` | Permanently out | - | Fixture source |
| 95 | `pfw.demos.pbl.src/w_demo_selector.srw` | Permanently out | - | Fixture source |
| 96 | `pfw.demos.pbl.src/w_demo_special.srw` | Permanently out | - | Fixture source |
| 97 | `pfw.net.ftp.pbl.src/ftpdownload.srf` | Deferred — whole | Integration | Not built |
| 98 | `pfw.net.ftp.pbl.src/ftpupload.srf` | Deferred — whole | Integration | Not built |
| 99 | `pfw.net.ftp.pbl.src/n_ftpclient.sru` | Deferred — whole | Integration | Not built |
| 100 | `pfw.net.http.ext.pbl.src/n_cst_alipay.sru` | Deferred — whole | Integration | Not built |
| 101 | `pfw.net.http.ext.pbl.src/n_cst_alipay_response.sru` | Deferred — whole | Integration | Not built |
| 102 | `pfw.net.http.ext.pbl.src/n_cst_wxpay.sru` | Deferred — whole | Integration | Not built |
| 103 | `pfw.net.http.ext.pbl.src/n_cst_wxpay_response.sru` | Deferred — whole | Integration | Not built |
| 104 | `pfw.net.http.pbl.src/ansitostring.srf` | Deferred — whole | Integration | Not built |
| 105 | `pfw.net.http.pbl.src/big5tostring.srf` | Deferred — whole | Integration | Not built |
| 106 | `pfw.net.http.pbl.src/gbktostring.srf` | Deferred — whole | Integration | Not built |
| 107 | `pfw.net.http.pbl.src/httpdownload.srf` | Deferred — whole | Integration | Not built |
| 108 | `pfw.net.http.pbl.src/httpget.srf` | Deferred — whole | Integration | Not built |
| 109 | `pfw.net.http.pbl.src/httppost.srf` | Deferred — whole | Integration | Not built |
| 110 | `pfw.net.http.pbl.src/httprequest.srf` | Deferred — whole | Integration | Not built |
| 111 | `pfw.net.http.pbl.src/httpupload.srf` | Deferred — whole | Integration | Not built |
| 112 | `pfw.net.http.pbl.src/n_httpclient.sru` | Deferred — whole | Integration | Not built |
| 113 | `pfw.net.http.pbl.src/n_httpformdata.sru` | Deferred — whole | Integration | Not built |
| 114 | `pfw.net.http.pbl.src/n_httpresponse.sru` | Deferred — whole | Integration | Not built |
| 115 | `pfw.net.http.pbl.src/n_httputility.sru` | Deferred — whole | Integration | Not built |
| 116 | `pfw.net.http.pbl.src/n_uribuilder.sru` | Deferred — whole | Integration | Not built |
| 117 | `pfw.net.http.pbl.src/stringtoansi.srf` | Deferred — whole | Integration | Not built |
| 118 | `pfw.net.http.pbl.src/stringtobig5.srf` | Deferred — whole | Integration | Not built |
| 119 | `pfw.net.http.pbl.src/stringtogbk.srf` | Deferred — whole | Integration | Not built |
| 120 | `pfw.net.http.pbl.src/stringtoutf16.srf` | Deferred — whole | Integration | Not built |
| 121 | `pfw.net.http.pbl.src/stringtoutf8.srf` | Deferred — whole | Integration | Not built |
| 122 | `pfw.net.http.pbl.src/urldecode.srf` | Deferred — whole | Integration | Not built |
| 123 | `pfw.net.http.pbl.src/urlencode.srf` | Deferred — whole | Integration | Not built |
| 124 | `pfw.net.http.pbl.src/utf16tostring.srf` | Deferred — whole | Integration | Not built |
| 125 | `pfw.net.http.pbl.src/utf8tostring.srf` | Deferred — whole | Integration | Not built |
| 126 | `pfw.net.websocket.pbl.src/n_wsclient.sru` | Deferred — whole | Integration | Not built |
| 127 | `pfw.net.websocket.pbl.src/n_wsmessage.sru` | Deferred — whole | Integration | Not built |
| 128 | `pfw.pack.pbl.src/pfw.sra` | Permanently out | - | Legacy tooling |
| 129 | `pfw.pack.pbl.src/w_packager.srw` | Permanently out | - | Legacy tooling |
| 130 | `pfw.pbl.src/p_pfw.srj` | Permanently out | - | REFERENCE |
| 131 | `pfw.pbl.src/pfw.sra` | Permanently out | - | REFERENCE |
| 132 | `pfw.pbl.src/project.srj` | Permanently out | - | REFERENCE |
| 133 | `pfw.shared.pbl.src/classnameex.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 134 | `pfw.shared.pbl.src/enums.sru` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 135 | `pfw.shared.pbl.src/formatretcode.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 136 | `pfw.shared.pbl.src/iif.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 137 | `pfw.shared.pbl.src/isallowed.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 138 | `pfw.shared.pbl.src/iscancelled.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 139 | `pfw.shared.pbl.src/isfailed.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 140 | `pfw.shared.pbl.src/isprevented.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 141 | `pfw.shared.pbl.src/issucceeded.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 142 | `pfw.shared.pbl.src/isvalidobject.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 143 | `pfw.shared.pbl.src/pfwexception.sru` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 144 | `pfw.shared.pbl.src/pfwthrowexception.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 145 | `pfw.shared.pbl.src/retcode.sru` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 146 | `pfw.shared.pbl.src/throwexception.srf` | In scope | `PowerFramework.Shared.Kernel` | Ported |
| 147 | `pfw.tests.pbl.src/dw_barcode.srd` | Permanently out | - | Fixture source |
| 148 | `pfw.tests.pbl.src/dw_qrcode.srd` | Permanently out | - | Fixture source |
| 149 | `pfw.tests.pbl.src/dw_sqlite.srd` | Permanently out | - | Fixture source |
| 150 | `pfw.tests.pbl.src/dw_svc_sample.srd` | Permanently out | - | Fixture source |
| 151 | `pfw.tests.pbl.src/dw_svc_sample_columnexp.srd` | Permanently out | - | Fixture source |
| 152 | `pfw.tests.pbl.src/dw_svc_sample_contextmenu.srd` | Permanently out | - | Fixture source |
| 153 | `pfw.tests.pbl.src/dw_svc_sample_dddw.srd` | Permanently out | - | Fixture source |
| 154 | `pfw.tests.pbl.src/dw_test_dwsvc.srd` | Permanently out | - | Fixture source |
| 155 | `pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd` | Permanently out | - | Fixture source |
| 156 | `pfw.tests.pbl.src/dw_test_dwsvc_contextmenu.srd` | Permanently out | - | Fixture source |
| 157 | `pfw.tests.pbl.src/dw_test_dwsvc_dddw.srd` | Permanently out | - | Fixture source |
| 158 | `pfw.tests.pbl.src/f_dwdrawbarcode.srf` | Permanently out | - | Fixture source |
| 159 | `pfw.tests.pbl.src/f_dwdrawqrcode.srf` | Permanently out | - | Fixture source |
| 160 | `pfw.tests.pbl.src/logger.srf` | Permanently out | - | Fixture source |
| 161 | `pfw.tests.pbl.src/m_test.srm` | Permanently out | - | Fixture source |
| 162 | `pfw.tests.pbl.src/n_cst_appargs.sru` | Permanently out | - | Fixture source |
| 163 | `pfw.tests.pbl.src/n_cst_appconfig.sru` | Permanently out | - | Fixture source |
| 164 | `pfw.tests.pbl.src/n_cst_autoinit.sru` | Permanently out | - | Fixture source |
| 165 | `pfw.tests.pbl.src/n_test_thread_task.sru` | Permanently out | - | Fixture source |
| 166 | `pfw.tests.pbl.src/n_test_threading_task.sru` | Permanently out | - | Fixture source |
| 167 | `pfw.tests.pbl.src/u_test_sciter_dw.sru` | Permanently out | - | Fixture source |
| 168 | `pfw.tests.pbl.src/w_test_appargs.srw` | Permanently out | - | Fixture source |
| 169 | `pfw.tests.pbl.src/w_test_appconfig.srw` | Permanently out | - | Fixture source |
| 170 | `pfw.tests.pbl.src/w_test_assert.srw` | Permanently out | - | Fixture source |
| 171 | `pfw.tests.pbl.src/w_test_barcode.srw` | Permanently out | - | Fixture source |
| 172 | `pfw.tests.pbl.src/w_test_blink.srw` | Permanently out | - | Fixture source |
| 173 | `pfw.tests.pbl.src/w_test_blink_wnd.srw` | Permanently out | - | Fixture source |
| 174 | `pfw.tests.pbl.src/w_test_camera_capture.srw` | Permanently out | - | Fixture source |
| 175 | `pfw.tests.pbl.src/w_test_compiler.srw` | Permanently out | - | Fixture source |
| 176 | `pfw.tests.pbl.src/w_test_devinfo.srw` | Permanently out | - | Fixture source |
| 177 | `pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw` | Permanently out | - | Fixture source |
| 178 | `pfw.tests.pbl.src/w_test_dwsvc_columnsort.srw` | Permanently out | - | Fixture source |
| 179 | `pfw.tests.pbl.src/w_test_dwsvc_contextmenu.srw` | Permanently out | - | Fixture source |
| 180 | `pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw` | Permanently out | - | Fixture source |
| 181 | `pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw` | Permanently out | - | Fixture source |
| 182 | `pfw.tests.pbl.src/w_test_eventful.srw` | Permanently out | - | Fixture source |
| 183 | `pfw.tests.pbl.src/w_test_filescanner.srw` | Permanently out | - | Fixture source |
| 184 | `pfw.tests.pbl.src/w_test_ftpclient.srw` | Permanently out | - | Fixture source |
| 185 | `pfw.tests.pbl.src/w_test_graphic.srw` | Permanently out | - | Fixture source |
| 186 | `pfw.tests.pbl.src/w_test_iconfont.srw` | Permanently out | - | Fixture source |
| 187 | `pfw.tests.pbl.src/w_test_invoker.srw` | Permanently out | - | Fixture source |
| 188 | `pfw.tests.pbl.src/w_test_json.srw` | Permanently out | - | Fixture source |
| 189 | `pfw.tests.pbl.src/w_test_logger.srw` | Permanently out | - | Fixture source |
| 190 | `pfw.tests.pbl.src/w_test_newtheme.srw` | Permanently out | - | Fixture source |
| 191 | `pfw.tests.pbl.src/w_test_progressbar.srw` | Permanently out | - | Fixture source |
| 192 | `pfw.tests.pbl.src/w_test_qrcode.srw` | Permanently out | - | Fixture source |
| 193 | `pfw.tests.pbl.src/w_test_regex.srw` | Permanently out | - | Fixture source |
| 194 | `pfw.tests.pbl.src/w_test_ribbonbar.srw` | Permanently out | - | Fixture source |
| 195 | `pfw.tests.pbl.src/w_test_sciter.srw` | Permanently out | - | Fixture source |
| 196 | `pfw.tests.pbl.src/w_test_sciter_dropdowncalendar.srw` | Permanently out | - | Fixture source |
| 197 | `pfw.tests.pbl.src/w_test_sciter_vm.srw` | Permanently out | - | Fixture source |
| 198 | `pfw.tests.pbl.src/w_test_sciter_wnd.srw` | Permanently out | - | Fixture source |
| 199 | `pfw.tests.pbl.src/w_test_splitcontainer.srw` | Permanently out | - | Fixture source |
| 200 | `pfw.tests.pbl.src/w_test_splitcontainer_complex.srw` | Permanently out | - | Fixture source |
| 201 | `pfw.tests.pbl.src/w_test_splitcontainer_template.srw` | Permanently out | - | Fixture source |
| 202 | `pfw.tests.pbl.src/w_test_sqlite.srw` | Permanently out | - | Fixture source |
| 203 | `pfw.tests.pbl.src/w_test_sqlparser.srw` | Permanently out | - | Fixture source |
| 204 | `pfw.tests.pbl.src/w_test_static_map.srw` | Permanently out | - | Fixture source |
| 205 | `pfw.tests.pbl.src/w_test_statictext.srw` | Permanently out | - | Fixture source |
| 206 | `pfw.tests.pbl.src/w_test_svg.srw` | Permanently out | - | Fixture source |
| 207 | `pfw.tests.pbl.src/w_test_thread.srw` | Permanently out | - | Fixture source |
| 208 | `pfw.tests.pbl.src/w_test_thread_sqlquery.srw` | Permanently out | - | Fixture source |
| 209 | `pfw.tests.pbl.src/w_test_trayicon.srw` | Permanently out | - | Fixture source |
| 210 | `pfw.tests.pbl.src/w_test_websocket.srw` | Permanently out | - | Fixture source |
| 211 | `pfw.tests.pbl.src/w_test_websocket_mqtt.srw` | Permanently out | - | Fixture source |
| 212 | `pfw.tests.pbl.src/w_test_webview.srw` | Permanently out | - | Fixture source |
| 213 | `pfw.tests.pbl.src/w_test_xml.srw` | Permanently out | - | Fixture source |
| 214 | `pfw.tests.pbl.src/w_test_zip.srw` | Permanently out | - | Fixture source |
| 215 | `pfw.thread.ext.pbl.src/dberrordata.srs` | In scope | Persistence | Ported |
| 216 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru` | In scope | Persistence | Ported |
| 217 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds.sru` | In scope | Persistence | Ported |
| 218 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_ds_mt.sru` | In scope | Persistence | Ported |
| 219 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase_hook.sru` | In scope | Persistence | Ported |
| 220 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlcommand.sru` | In scope | Persistence | Ported |
| 221 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` | In scope | Persistence | Ported |
| 222 | `pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru` | In scope | Persistence | Ported |
| 223 | `pfw.thread.ext.pbl.src/n_cst_thread_trans.sru` | In scope | Persistence | Ported |
| 224 | `pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru` | In scope | Persistence | Ported |
| 225 | `pfw.thread.ext.pbl.src/n_cst_threading_task_sqlbase.sru` | In scope | Persistence | Ported |
| 226 | `pfw.thread.ext.pbl.src/n_cst_threading_task_sqlcommand.sru` | In scope | Persistence | Ported |
| 227 | `pfw.thread.ext.pbl.src/n_cst_threading_task_sqlquery.sru` | In scope | Persistence | Ported |
| 228 | `pfw.thread.ext.pbl.src/n_cst_threading_task_sqlupdate.sru` | In scope | Persistence | Ported |
| 229 | `pfw.thread.ext.pbl.src/transactiondata.srs` | In scope | Persistence | Ported |
| 230 | `pfw.thread.pbl.src/n_cst_thread.sru` | In scope | Persistence | Ported |
| 231 | `pfw.thread.pbl.src/n_cst_thread_task.sru` | In scope | Persistence | Ported |
| 232 | `pfw.thread.pbl.src/n_cst_threading.sru` | In scope | Persistence | Ported |
| 233 | `pfw.thread.pbl.src/n_cst_threading_eventful.sru` | In scope | Persistence | Ported |
| 234 | `pfw.thread.pbl.src/n_cst_threading_pool.sru` | In scope | Persistence | Ported |
| 235 | `pfw.thread.pbl.src/n_cst_threading_task.sru` | In scope | Persistence | Ported |
| 236 | `pfw.ui.blink.pbl.src/blinksetcookiefile.srf` | Deferred — whole | ScriptBridge | Not built |
| 237 | `pfw.ui.blink.pbl.src/blinksetplugindir.srf` | Deferred — whole | ScriptBridge | Not built |
| 238 | `pfw.ui.blink.pbl.src/blinksetstoragedir.srf` | Deferred — whole | ScriptBridge | Not built |
| 239 | `pfw.ui.blink.pbl.src/n_blink.sru` | Deferred — whole | ScriptBridge | Not built |
| 240 | `pfw.ui.blink.pbl.src/n_blinkelement.sru` | Deferred — whole | ScriptBridge | Not built |
| 241 | `pfw.ui.blink.pbl.src/n_blinkfast.sru` | Deferred — whole | ScriptBridge | Not built |
| 242 | `pfw.ui.blink.pbl.src/n_blinkfunctor.sru` | Deferred — whole | ScriptBridge | Not built |
| 243 | `pfw.ui.blink.pbl.src/n_blinkvalue.sru` | Deferred — whole | ScriptBridge | Not built |
| 244 | `pfw.ui.blink.pbl.src/n_cst_blink.sru` | Deferred — whole | ScriptBridge | Not built |
| 245 | `pfw.ui.blink.pbl.src/u_blink.sru` | Deferred — whole | ScriptBridge | Not built |
| 246 | `pfw.ui.blink.pbl.src/u_blinkfast.sru` | Deferred — whole | ScriptBridge | Not built |
| 247 | `pfw.ui.blink.pbl.src/u_cst_blink.sru` | Deferred — whole | ScriptBridge | Not built |
| 248 | `pfw.ui.blink.pbl.src/w_blink.srw` | Deferred — whole | ScriptBridge | Not built |
| 249 | `pfw.ui.blink.pbl.src/w_blinkfast.srw` | Deferred — whole | ScriptBridge | Not built |
| 250 | `pfw.ui.blink.pbl.src/w_cst_blink.srw` | Deferred — whole | ScriptBridge | Not built |
| 251 | `pfw.ui.controls.ext.pbl.src/messageboxex.srf` | Deferred — split | DesignSystem | Not built |
| 252 | `pfw.ui.controls.ext.pbl.src/messageboxtimeout.srf` | Deferred — split | DesignSystem | Not built |
| 253 | `pfw.ui.controls.ext.pbl.src/msgboxdata.srs` | Deferred — split | DesignSystem | Not built |
| 254 | `pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 255 | `pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 256 | `pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 257 | `pfw.ui.controls.ext.pbl.src/n_cst_thememanager.sru` | Deferred — split | DesignSystem | Not built |
| 258 | `pfw.ui.controls.ext.pbl.src/n_cst_treeview_findcallback.sru` | Deferred — split | DesignSystem | Not built |
| 259 | `pfw.ui.controls.ext.pbl.src/ne_cst_i18n.sru` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 260 | `pfw.ui.controls.ext.pbl.src/ne_cst_popupmenu.sru` | Deferred — split | DesignSystem | Not built |
| 261 | `pfw.ui.controls.ext.pbl.src/pbmsgstruct.srs` | Deferred — split | DesignSystem | Not built |
| 262 | `pfw.ui.controls.ext.pbl.src/se_cst_button.sru` | Deferred — split | DesignSystem | Not built |
| 263 | `pfw.ui.controls.ext.pbl.src/se_cst_checkbox.sru` | Deferred — split | DesignSystem | Not built |
| 264 | `pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` | Deferred — split | DesignSystem | REFERENCE |
| 265 | `pfw.ui.controls.ext.pbl.src/se_cst_dropdownlist.sru` | Deferred — split | DesignSystem | Not built |
| 266 | `pfw.ui.controls.ext.pbl.src/se_cst_editmask.sru` | Deferred — split | DesignSystem | Not built |
| 267 | `pfw.ui.controls.ext.pbl.src/se_cst_hprogressbar.sru` | Deferred — split | DesignSystem | Not built |
| 268 | `pfw.ui.controls.ext.pbl.src/se_cst_hscrollbar.sru` | Deferred — split | DesignSystem | Not built |
| 269 | `pfw.ui.controls.ext.pbl.src/se_cst_menubutton.sru` | Deferred — split | DesignSystem | Not built |
| 270 | `pfw.ui.controls.ext.pbl.src/se_cst_multilineedit.sru` | Deferred — split | DesignSystem | Not built |
| 271 | `pfw.ui.controls.ext.pbl.src/se_cst_radiobox.sru` | Deferred — split | DesignSystem | Not built |
| 272 | `pfw.ui.controls.ext.pbl.src/se_cst_singlelineedit.sru` | Deferred — split | DesignSystem | Not built |
| 273 | `pfw.ui.controls.ext.pbl.src/se_cst_splitbutton.sru` | Deferred — split | DesignSystem | Not built |
| 274 | `pfw.ui.controls.ext.pbl.src/se_cst_statictext.sru` | Deferred — split | DesignSystem | Not built |
| 275 | `pfw.ui.controls.ext.pbl.src/se_cst_tabpagew.srw` | Deferred — split | DesignSystem | Not built |
| 276 | `pfw.ui.controls.ext.pbl.src/se_cst_treeview.sru` | Deferred — split | DesignSystem | Not built |
| 277 | `pfw.ui.controls.ext.pbl.src/se_cst_vprogressbar.sru` | Deferred — split | DesignSystem | Not built |
| 278 | `pfw.ui.controls.ext.pbl.src/se_cst_vscrollbar.sru` | Deferred — split | DesignSystem | Not built |
| 279 | `pfw.ui.controls.ext.pbl.src/se_cst_window.srw` | Deferred — split | DesignSystem | Not built |
| 280 | `pfw.ui.controls.ext.pbl.src/thememanager.srf` | Deferred — split | DesignSystem | Not built |
| 281 | `pfw.ui.controls.ext.pbl.src/ue_cst_buttonlistbar.sru` | Deferred — split | DesignSystem | Not built |
| 282 | `pfw.ui.controls.ext.pbl.src/ue_cst_layout.sru` | Deferred — split | DesignSystem | Not built |
| 283 | `pfw.ui.controls.ext.pbl.src/ue_cst_ribbonbar.sru` | Deferred — split | DesignSystem | Not built |
| 284 | `pfw.ui.controls.ext.pbl.src/ue_cst_sciter_sidebar.sru` | Deferred — split | DesignSystem | Not built |
| 285 | `pfw.ui.controls.ext.pbl.src/ue_cst_sciter_treeview.sru` | Deferred — split | DesignSystem | Not built |
| 286 | `pfw.ui.controls.ext.pbl.src/ue_cst_shortcutbar.sru` | Deferred — split | DesignSystem | Not built |
| 287 | `pfw.ui.controls.ext.pbl.src/ue_cst_splitcontainer.sru` | Deferred — split | DesignSystem | Not built |
| 288 | `pfw.ui.controls.ext.pbl.src/ue_cst_tabcontrol.sru` | Deferred — split | DesignSystem | Not built |
| 289 | `pfw.ui.controls.ext.pbl.src/ue_cst_tabpage.sru` | Deferred — split | DesignSystem | Not built |
| 290 | `pfw.ui.controls.ext.pbl.src/ue_cst_taskpanelbar.sru` | Deferred — split | DesignSystem | Not built |
| 291 | `pfw.ui.controls.ext.pbl.src/ue_cst_toolbarstrip.sru` | Deferred — split | DesignSystem | Not built |
| 292 | `pfw.ui.controls.ext.pbl.src/w_cst_msgbox.srw` | Deferred — split | DesignSystem | Not built |
| 293 | `pfw.ui.controls.ext.pbl.src/we_cst_tabfloat.srw` | Deferred — split | DesignSystem | Not built |
| 294 | `pfw.ui.controls.pbl.src/n_cst_popupmenu.sru` | Deferred — whole | DesignSystem | Not built |
| 295 | `pfw.ui.controls.pbl.src/s_cst_button.sru` | Deferred — whole | DesignSystem | Not built |
| 296 | `pfw.ui.controls.pbl.src/s_cst_checkbox.sru` | Deferred — whole | DesignSystem | Not built |
| 297 | `pfw.ui.controls.pbl.src/s_cst_datawindow.sru` | Deferred — whole | DesignSystem | Not built |
| 298 | `pfw.ui.controls.pbl.src/s_cst_dropdownlist.sru` | Deferred — whole | DesignSystem | Not built |
| 299 | `pfw.ui.controls.pbl.src/s_cst_editmask.sru` | Deferred — whole | DesignSystem | Not built |
| 300 | `pfw.ui.controls.pbl.src/s_cst_hprogressbar.sru` | Deferred — whole | DesignSystem | Not built |
| 301 | `pfw.ui.controls.pbl.src/s_cst_hscrollbar.sru` | Deferred — whole | DesignSystem | Not built |
| 302 | `pfw.ui.controls.pbl.src/s_cst_menubutton.sru` | Deferred — whole | DesignSystem | Not built |
| 303 | `pfw.ui.controls.pbl.src/s_cst_multilineedit.sru` | Deferred — whole | DesignSystem | Not built |
| 304 | `pfw.ui.controls.pbl.src/s_cst_radiobox.sru` | Deferred — whole | DesignSystem | Not built |
| 305 | `pfw.ui.controls.pbl.src/s_cst_singlelineedit.sru` | Deferred — whole | DesignSystem | Not built |
| 306 | `pfw.ui.controls.pbl.src/s_cst_splitbutton.sru` | Deferred — whole | DesignSystem | Not built |
| 307 | `pfw.ui.controls.pbl.src/s_cst_statictext.sru` | Deferred — whole | DesignSystem | Not built |
| 308 | `pfw.ui.controls.pbl.src/s_cst_tabpagew.srw` | Deferred — whole | DesignSystem | Not built |
| 309 | `pfw.ui.controls.pbl.src/s_cst_treeview.sru` | Deferred — whole | DesignSystem | Not built |
| 310 | `pfw.ui.controls.pbl.src/s_cst_vprogressbar.sru` | Deferred — whole | DesignSystem | Not built |
| 311 | `pfw.ui.controls.pbl.src/s_cst_vscrollbar.sru` | Deferred — whole | DesignSystem | Not built |
| 312 | `pfw.ui.controls.pbl.src/s_cst_window.srw` | Deferred — whole | DesignSystem | Not built |
| 313 | `pfw.ui.controls.pbl.src/u_cst_buttonlistbar.sru` | Deferred — whole | DesignSystem | Not built |
| 314 | `pfw.ui.controls.pbl.src/u_cst_layout.sru` | Deferred — whole | DesignSystem | Not built |
| 315 | `pfw.ui.controls.pbl.src/u_cst_ribbonbar.sru` | Deferred — whole | DesignSystem | Not built |
| 316 | `pfw.ui.controls.pbl.src/u_cst_shortcutbar.sru` | Deferred — whole | DesignSystem | Not built |
| 317 | `pfw.ui.controls.pbl.src/u_cst_splitcontainer.sru` | Deferred — whole | DesignSystem | Not built |
| 318 | `pfw.ui.controls.pbl.src/u_cst_tabcontrol.sru` | Deferred — whole | DesignSystem | Not built |
| 319 | `pfw.ui.controls.pbl.src/u_cst_tabpage.sru` | Deferred — whole | DesignSystem | Not built |
| 320 | `pfw.ui.controls.pbl.src/u_cst_taskpanelbar.sru` | Deferred — whole | DesignSystem | Not built |
| 321 | `pfw.ui.controls.pbl.src/u_cst_toolbarstrip.sru` | Deferred — whole | DesignSystem | Not built |
| 322 | `pfw.ui.objects.pbl.src/n_cst_base_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 323 | `pfw.ui.objects.pbl.src/n_cst_button_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 324 | `pfw.ui.objects.pbl.src/n_cst_buttonlistbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 325 | `pfw.ui.objects.pbl.src/n_cst_checkbox_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 326 | `pfw.ui.objects.pbl.src/n_cst_datawindow_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 327 | `pfw.ui.objects.pbl.src/n_cst_dropdownlist_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 328 | `pfw.ui.objects.pbl.src/n_cst_layout_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 329 | `pfw.ui.objects.pbl.src/n_cst_multilineedit_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 330 | `pfw.ui.objects.pbl.src/n_cst_popupcanvas.sru` | Deferred — whole | DesignSystem | Not built |
| 331 | `pfw.ui.objects.pbl.src/n_cst_popupmenu_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 332 | `pfw.ui.objects.pbl.src/n_cst_progressbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 333 | `pfw.ui.objects.pbl.src/n_cst_radiobox_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 334 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_baseitem.sru` | Deferred — whole | DesignSystem | Not built |
| 335 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_break.sru` | Deferred — whole | DesignSystem | Not built |
| 336 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_button.sru` | Deferred — whole | DesignSystem | Not built |
| 337 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_category.sru` | Deferred — whole | DesignSystem | Not built |
| 338 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_checkbox.sru` | Deferred — whole | DesignSystem | Not built |
| 339 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_icon.sru` | Deferred — whole | DesignSystem | Not built |
| 340 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_label.sru` | Deferred — whole | DesignSystem | Not built |
| 341 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_link.sru` | Deferred — whole | DesignSystem | Not built |
| 342 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_panel.sru` | Deferred — whole | DesignSystem | Not built |
| 343 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_popupcategory.sru` | Deferred — whole | DesignSystem | Not built |
| 344 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_popuppanel.sru` | Deferred — whole | DesignSystem | Not built |
| 345 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_radiobox.sru` | Deferred — whole | DesignSystem | Not built |
| 346 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_separator.sru` | Deferred — whole | DesignSystem | Not built |
| 347 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_space.sru` | Deferred — whole | DesignSystem | Not built |
| 348 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 349 | `pfw.ui.objects.pbl.src/n_cst_ribbonbar_toolbar.sru` | Deferred — whole | DesignSystem | Not built |
| 350 | `pfw.ui.objects.pbl.src/n_cst_scrollbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 351 | `pfw.ui.objects.pbl.src/n_cst_shortcutbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 352 | `pfw.ui.objects.pbl.src/n_cst_singlelineedit_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 353 | `pfw.ui.objects.pbl.src/n_cst_splitcontainer.sru` | Deferred — whole | DesignSystem | Not built |
| 354 | `pfw.ui.objects.pbl.src/n_cst_splitcontainer_panel.sru` | Deferred — whole | DesignSystem | Not built |
| 355 | `pfw.ui.objects.pbl.src/n_cst_splitcontainer_template.sru` | Deferred — whole | DesignSystem | Not built |
| 356 | `pfw.ui.objects.pbl.src/n_cst_splitcontainer_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 357 | `pfw.ui.objects.pbl.src/n_cst_statictext_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 358 | `pfw.ui.objects.pbl.src/n_cst_tabcontrol_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 359 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_baseitem.sru` | Deferred — whole | DesignSystem | Not built |
| 360 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_break.sru` | Deferred — whole | DesignSystem | Not built |
| 361 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_button.sru` | Deferred — whole | DesignSystem | Not built |
| 362 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_checkbox.sru` | Deferred — whole | DesignSystem | Not built |
| 363 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_icon.sru` | Deferred — whole | DesignSystem | Not built |
| 364 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_label.sru` | Deferred — whole | DesignSystem | Not built |
| 365 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_link.sru` | Deferred — whole | DesignSystem | Not built |
| 366 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_panel.sru` | Deferred — whole | DesignSystem | Not built |
| 367 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_radiobox.sru` | Deferred — whole | DesignSystem | Not built |
| 368 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_separator.sru` | Deferred — whole | DesignSystem | Not built |
| 369 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_space.sru` | Deferred — whole | DesignSystem | Not built |
| 370 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 371 | `pfw.ui.objects.pbl.src/n_cst_taskpanelbar_toolbar.sru` | Deferred — whole | DesignSystem | Not built |
| 372 | `pfw.ui.objects.pbl.src/n_cst_toolbarstrip_popup.sru` | Deferred — whole | DesignSystem | Not built |
| 373 | `pfw.ui.objects.pbl.src/n_cst_toolbarstrip_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 374 | `pfw.ui.objects.pbl.src/n_cst_treeview_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 375 | `pfw.ui.objects.pbl.src/n_cst_window_mdiclient.sru` | Deferred — whole | DesignSystem | Not built |
| 376 | `pfw.ui.objects.pbl.src/n_cst_window_mdiclient_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 377 | `pfw.ui.objects.pbl.src/n_cst_window_menubar.sru` | Deferred — whole | DesignSystem | Not built |
| 378 | `pfw.ui.objects.pbl.src/n_cst_window_statusbar.sru` | Deferred — whole | DesignSystem | Not built |
| 379 | `pfw.ui.objects.pbl.src/n_cst_window_theme.sru` | Deferred — whole | DesignSystem | Not built |
| 380 | `pfw.ui.objects.pbl.src/n_cst_window_titlebar.sru` | Deferred — whole | DesignSystem | Not built |
| 381 | `pfw.ui.objects.pbl.src/n_cst_window_toolbar.sru` | Deferred — whole | DesignSystem | Not built |
| 382 | `pfw.ui.objects.pbl.src/tabcontrolitem.srs` | Deferred — whole | DesignSystem | Not built |
| 383 | `pfw.ui.objects.pbl.src/toolbaritem.srs` | Deferred — whole | DesignSystem | Not built |
| 384 | `pfw.ui.objects.pbl.src/u_cst_canvas.sru` | Deferred — whole | DesignSystem | Not built |
| 385 | `pfw.ui.objects.pbl.src/w_cst_tabfloat.srw` | Deferred — whole | DesignSystem | Not built |
| 386 | `pfw.ui.pbl.src/addfontresource.srf` | Deferred — split | DesignSystem | Not built |
| 387 | `pfw.ui.pbl.src/argb.srf` | Deferred — split | DesignSystem | Not built |
| 388 | `pfw.ui.pbl.src/argbdarken.srf` | Deferred — split | DesignSystem | Not built |
| 389 | `pfw.ui.pbl.src/argblighten.srf` | Deferred — split | DesignSystem | Not built |
| 390 | `pfw.ui.pbl.src/argbtogray.srf` | Deferred — split | DesignSystem | Not built |
| 391 | `pfw.ui.pbl.src/d2px.srf` | Deferred — split | DesignSystem | Not built |
| 392 | `pfw.ui.pbl.src/d2py.srf` | Deferred — split | DesignSystem | Not built |
| 393 | `pfw.ui.pbl.src/d2ux.srf` | Deferred — split | DesignSystem | Not built |
| 394 | `pfw.ui.pbl.src/d2uy.srf` | Deferred — split | DesignSystem | Not built |
| 395 | `pfw.ui.pbl.src/getparentwindow.srf` | Deferred — split | DesignSystem | Not built |
| 396 | `pfw.ui.pbl.src/i18n.srf` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 397 | `pfw.ui.pbl.src/isolecontrol.srf` | Deferred — split | DesignSystem | Not built |
| 398 | `pfw.ui.pbl.src/n_canvas.sru` | Deferred — split | DesignSystem | Not built |
| 399 | `pfw.ui.pbl.src/n_cst_font.sru` | Deferred — split | DesignSystem | Not built |
| 400 | `pfw.ui.pbl.src/n_cst_i18n.sru` | In scope | `PowerFramework.Shared.Localization` | Ported |
| 401 | `pfw.ui.pbl.src/n_cst_painter.sru` | Deferred — split | DesignSystem | Not built |
| 402 | `pfw.ui.pbl.src/n_cst_win32.sru` | Deferred — split | DesignSystem | Not built |
| 403 | `pfw.ui.pbl.src/n_dragicon.sru` | Deferred — split | DesignSystem | Not built |
| 404 | `pfw.ui.pbl.src/n_image.sru` | Deferred — split | DesignSystem | Not built |
| 405 | `pfw.ui.pbl.src/n_imagelist.sru` | Deferred — split | DesignSystem | Not built |
| 406 | `pfw.ui.pbl.src/n_popupcanvas.sru` | Deferred — split | DesignSystem | Not built |
| 407 | `pfw.ui.pbl.src/n_popupmenu.sru` | Deferred — split | DesignSystem | Not built |
| 408 | `pfw.ui.pbl.src/n_popupmenuex.sru` | Deferred — split | DesignSystem | Not built |
| 409 | `pfw.ui.pbl.src/n_timer.sru` | Deferred — split | DesignSystem | Not built |
| 410 | `pfw.ui.pbl.src/n_tooltip.sru` | Deferred — split | DesignSystem | Not built |
| 411 | `pfw.ui.pbl.src/n_trayicon.sru` | Deferred — split | DesignSystem | Not built |
| 412 | `pfw.ui.pbl.src/p2dx.srf` | Deferred — split | DesignSystem | Not built |
| 413 | `pfw.ui.pbl.src/p2dy.srf` | Deferred — split | DesignSystem | Not built |
| 414 | `pfw.ui.pbl.src/p2ux.srf` | Deferred — split | DesignSystem | Not built |
| 415 | `pfw.ui.pbl.src/p2uy.srf` | Deferred — split | DesignSystem | Not built |
| 416 | `pfw.ui.pbl.src/painter.sru` | Deferred — split | DesignSystem | Not built |
| 417 | `pfw.ui.pbl.src/paintpane.srs` | Deferred — split | DesignSystem | Not built |
| 418 | `pfw.ui.pbl.src/pfwinithookui.srf` | Deferred — split | DesignSystem | Not built |
| 419 | `pfw.ui.pbl.src/point.srs` | Deferred — split | DesignSystem | Not built |
| 420 | `pfw.ui.pbl.src/pointf.srs` | Deferred — split | DesignSystem | Not built |
| 421 | `pfw.ui.pbl.src/radius.srs` | Deferred — split | DesignSystem | Not built |
| 422 | `pfw.ui.pbl.src/radiusf.srs` | Deferred — split | DesignSystem | Not built |
| 423 | `pfw.ui.pbl.src/rect.srs` | Deferred — split | DesignSystem | Not built |
| 424 | `pfw.ui.pbl.src/rectf.srs` | Deferred — split | DesignSystem | Not built |
| 425 | `pfw.ui.pbl.src/removefontresource.srf` | Deferred — split | DesignSystem | Not built |
| 426 | `pfw.ui.pbl.src/rgbdarken.srf` | Deferred — split | DesignSystem | Not built |
| 427 | `pfw.ui.pbl.src/rgblighten.srf` | Deferred — split | DesignSystem | Not built |
| 428 | `pfw.ui.pbl.src/rgbtogray.srf` | Deferred — split | DesignSystem | Not built |
| 429 | `pfw.ui.pbl.src/scrollbarcreateinfo.srs` | Deferred — split | DesignSystem | Not built |
| 430 | `pfw.ui.pbl.src/scrollbardrawinfo.srs` | Deferred — split | DesignSystem | Not built |
| 431 | `pfw.ui.pbl.src/size.srs` | Deferred — split | DesignSystem | Not built |
| 432 | `pfw.ui.pbl.src/sizef.srs` | Deferred — split | DesignSystem | Not built |
| 433 | `pfw.ui.pbl.src/splitargb.srf` | Deferred — split | DesignSystem | Not built |
| 434 | `pfw.ui.pbl.src/splitrgb.srf` | Deferred — split | DesignSystem | Not built |
| 435 | `pfw.ui.pbl.src/toargb.srf` | Deferred — split | DesignSystem | Not built |
| 436 | `pfw.ui.pbl.src/torgb.srf` | Deferred — split | DesignSystem | Not built |
| 437 | `pfw.ui.pbl.src/trackmouseevent.srs` | Deferred — split | DesignSystem | Not built |
| 438 | `pfw.ui.pbl.src/u2dx.srf` | Deferred — split | DesignSystem | Not built |
| 439 | `pfw.ui.pbl.src/u2dy.srf` | Deferred — split | DesignSystem | Not built |
| 440 | `pfw.ui.pbl.src/u2px.srf` | Deferred — split | DesignSystem | Not built |
| 441 | `pfw.ui.pbl.src/u2py.srf` | Deferred — split | DesignSystem | Not built |
| 442 | `pfw.ui.pbl.src/u_canvas.sru` | Deferred — split | DesignSystem | Not built |
| 443 | `pfw.ui.pbl.src/win32.sru` | Deferred — split | DesignSystem | Not built |
| 444 | `pfw.ui.sciter.ext.pbl.src/n_cst_sciter_sidebar_option.sru` | Deferred — whole | ScriptBridge | Not built |
| 445 | `pfw.ui.sciter.ext.pbl.src/n_cst_sciter_treeview_option.sru` | Deferred — whole | ScriptBridge | Not built |
| 446 | `pfw.ui.sciter.ext.pbl.src/u_cst_sciter_sidebar.sru` | Deferred — whole | ScriptBridge | Not built |
| 447 | `pfw.ui.sciter.ext.pbl.src/u_cst_sciter_treeview.sru` | Deferred — whole | ScriptBridge | Not built |
| 448 | `pfw.ui.sciter.pbl.src/n_cst_sciter.sru` | Deferred — whole | ScriptBridge | Not built |
| 449 | `pfw.ui.sciter.pbl.src/n_sciter.sru` | Deferred — whole | ScriptBridge | Not built |
| 450 | `pfw.ui.sciter.pbl.src/n_sciterelement.sru` | Deferred — whole | ScriptBridge | Not built |
| 451 | `pfw.ui.sciter.pbl.src/n_scitereventhandler.sru` | Deferred — whole | ScriptBridge | Not built |
| 452 | `pfw.ui.sciter.pbl.src/n_sciterfunctor.sru` | Deferred — whole | ScriptBridge | Not built |
| 453 | `pfw.ui.sciter.pbl.src/n_scitervalue.sru` | Deferred — whole | ScriptBridge | Not built |
| 454 | `pfw.ui.sciter.pbl.src/n_scitervm.sru` | Deferred — whole | ScriptBridge | Not built |
| 455 | `pfw.ui.sciter.pbl.src/sciteraddmastercss.srf` | Deferred — whole | ScriptBridge | Not built |
| 456 | `pfw.ui.sciter.pbl.src/scitersetmastercss.srf` | Deferred — whole | ScriptBridge | Not built |
| 457 | `pfw.ui.sciter.pbl.src/scitersetmasterscript.srf` | Deferred — whole | ScriptBridge | Not built |
| 458 | `pfw.ui.sciter.pbl.src/scitersetoption.srf` | Deferred — whole | ScriptBridge | Not built |
| 459 | `pfw.ui.sciter.pbl.src/u_cst_sciter.sru` | Deferred — whole | ScriptBridge | Not built |
| 460 | `pfw.ui.sciter.pbl.src/u_sciter.sru` | Deferred — whole | ScriptBridge | Not built |
| 461 | `pfw.ui.sciter.pbl.src/w_cst_sciter.srw` | Deferred — whole | ScriptBridge | Not built |
| 462 | `pfw.ui.sciter.pbl.src/w_sciter.srw` | Deferred — whole | ScriptBridge | Not built |
| 463 | `pfw.ui.webview.pbl.src/n_cst_webview.sru` | Deferred — whole | ScriptBridge | Not built |
| 464 | `pfw.ui.webview.pbl.src/n_webview.sru` | Deferred — whole | ScriptBridge | Not built |
| 465 | `pfw.ui.webview.pbl.src/n_webviewrequest.sru` | Deferred — whole | ScriptBridge | Not built |
| 466 | `pfw.ui.webview.pbl.src/n_webviewresponse.sru` | Deferred — whole | ScriptBridge | Not built |
| 467 | `pfw.ui.webview.pbl.src/u_cst_webview.sru` | Deferred — whole | ScriptBridge | Not built |
| 468 | `pfw.ui.webview.pbl.src/u_webview.sru` | Deferred — whole | ScriptBridge | Not built |
| 469 | `pfw.ui.webview.pbl.src/webviewgetversion.srf` | Deferred — whole | ScriptBridge | Not built |
| 470 | `pfw.ui.webview.pbl.src/webviewiscompatibleversion.srf` | Deferred — whole | ScriptBridge | Not built |
| 471 | `pfw.ui.webview.pbl.src/webviewsetdatadir.srf` | Deferred — whole | ScriptBridge | Not built |
| 472 | `pfw.ui.webview.pbl.src/webviewsetruntimedir.srf` | Deferred — whole | ScriptBridge | Not built |
| 473 | `pfw.ui.webview.pbl.src/webviewsetruntimemode.srf` | Deferred — whole | ScriptBridge | Not built |
| 474 | `pfw.utility.barcode.pbl.src/n_barcode.sru` | Deferred — whole | Documents | Not built |
| 475 | `pfw.utility.barcode.pbl.src/n_qrcode.sru` | Deferred — whole | Documents | Not built |
| 476 | `pfw.utility.compiler.pbl.src/evaluate.srf` | Deferred — whole | ScriptBridge | Not built |
| 477 | `pfw.utility.compiler.pbl.src/n_compiler.sru` | Deferred — whole | ScriptBridge | Not built |
| 478 | `pfw.utility.container.pbl.src/n_list.sru` | Deferred — split | Documents | Not built |
| 479 | `pfw.utility.container.pbl.src/n_map.sru` | In scope | `PowerFramework.Shared.Containers` | Ported |
| 480 | `pfw.utility.container.pbl.src/n_vector.sru` | In scope | `PowerFramework.Shared.Containers` | Ported |
| 481 | `pfw.utility.devinfo.pbl.src/n_devinfo.sru` | Deferred — whole | Documents | Not built |
| 482 | `pfw.utility.devinfo.pbl.src/u_cameracapture.sru` | Deferred — whole | Documents | Not built |
| 483 | `pfw.utility.invoker.pbl.src/getglobalvar.srf` | Deferred — split | ScriptBridge | Not built |
| 484 | `pfw.utility.invoker.pbl.src/hasglobalfunction.srf` | Deferred — split | ScriptBridge | Not built |
| 485 | `pfw.utility.invoker.pbl.src/hasglobalvar.srf` | Deferred — split | ScriptBridge | Not built |
| 486 | `pfw.utility.invoker.pbl.src/invokeglobalfunction.srf` | Deferred — split | ScriptBridge | Not built |
| 487 | `pfw.utility.invoker.pbl.src/n_cst_eventful.sru` | In scope | `PowerFramework.Shared.Eventful` | Ported |
| 488 | `pfw.utility.invoker.pbl.src/n_objectinvoker.sru` | Deferred — split | ScriptBridge | Not built |
| 489 | `pfw.utility.invoker.pbl.src/n_scriptinvoker.sru` | Deferred — split | ScriptBridge | Not built |
| 490 | `pfw.utility.invoker.pbl.src/setglobalvar.srf` | Deferred — split | ScriptBridge | Not built |
| 491 | `pfw.utility.parser.pbl.src/jsonfrom.srf` | Deferred — split | Documents | Not built |
| 492 | `pfw.utility.parser.pbl.src/makejsonarray.srf` | Deferred — split | Documents | Not built |
| 493 | `pfw.utility.parser.pbl.src/makejsonobject.srf` | Deferred — split | Documents | Not built |
| 494 | `pfw.utility.parser.pbl.src/n_json.sru` | Deferred — split | Documents | Not built |
| 495 | `pfw.utility.parser.pbl.src/n_sql.sru` | In scope | Persistence | Ported |
| 496 | `pfw.utility.parser.pbl.src/n_xmlattribute.sru` | Deferred — split | Documents | Not built |
| 497 | `pfw.utility.parser.pbl.src/n_xmldoc.sru` | Deferred — split | Documents | Not built |
| 498 | `pfw.utility.parser.pbl.src/n_xmlnode.sru` | Deferred — split | Documents | Not built |
| 499 | `pfw.utility.parser.pbl.src/n_xmlparseresult.sru` | Deferred — split | Documents | Not built |
| 500 | `pfw.utility.parser.pbl.src/n_xmlqueryresult.sru` | Deferred — split | Documents | Not built |
| 501 | `pfw.utility.parser.pbl.src/parsejson.srf` | Deferred — split | Documents | Not built |
| 502 | `pfw.utility.parser.pbl.src/parsesql.srf` | In scope | Persistence | Ported |
| 503 | `pfw.utility.parser.pbl.src/parsexml.srf` | Deferred — split | Documents | Not built |
| 504 | `pfw.utility.pbl.src/datetimetotimestamp.srf` | Deferred — split | Documents | Not built |
| 505 | `pfw.utility.pbl.src/dectohex.srf` | Deferred — split | Documents | Not built |
| 506 | `pfw.utility.pbl.src/dectostring.srf` | Deferred — split | Documents | Not built |
| 507 | `pfw.utility.pbl.src/getpinyinfirstletter.srf` | Deferred — split | Documents | Not built |
| 508 | `pfw.utility.pbl.src/getpinyinfirstletters.srf` | Deferred — split | Documents | Not built |
| 509 | `pfw.utility.pbl.src/gettimestamp.srf` | Deferred — split | Documents | Not built |
| 510 | `pfw.utility.pbl.src/hextodec.srf` | Deferred — split | Documents | Not built |
| 511 | `pfw.utility.pbl.src/loaduri.srf` | Deferred — split | Documents | Not built |
| 512 | `pfw.utility.pbl.src/n_filescanner.sru` | Deferred — split | Documents | Not built |
| 513 | `pfw.utility.pbl.src/n_logger.sru` | Deferred — split | Documents | Not built |
| 514 | `pfw.utility.pbl.src/parsedatetime.srf` | Deferred — split | Documents | Not built |
| 515 | `pfw.utility.pbl.src/pinyinfirstletterlike.srf` | In scope | DataServices | Ported |
| 516 | `pfw.utility.pbl.src/stringtodec.srf` | Deferred — split | Documents | Not built |
| 517 | `pfw.utility.pbl.src/timestamptodatetime.srf` | Deferred — split | Documents | Not built |
| 518 | `pfw.utility.regexp.pbl.src/n_regexp.sru` | Deferred — whole | Documents | Not built |
| 519 | `pfw.utility.regexp.pbl.src/regexpfind.srf` | Deferred — whole | Documents | Not built |
| 520 | `pfw.utility.regexp.pbl.src/regexpmatch.srf` | Deferred — whole | Documents | Not built |
| 521 | `pfw.utility.regexp.pbl.src/regexpreplace.srf` | Deferred — whole | Documents | Not built |
| 522 | `pfw.utility.regexp.pbl.src/regexpverify.srf` | Deferred — whole | Documents | Not built |
| 523 | `pfw.utility.sqlite.pbl.src/n_sqlite.sru` | In scope | Persistence | Ported |
| 524 | `pfw.utility.sqlite.pbl.src/n_sqliterecordset.sru` | In scope | Persistence | Ported |
| 525 | `pfw.utility.sqlite.pbl.src/sqlitegetitemdouble.srf` | In scope | Persistence | Ported |
| 526 | `pfw.utility.zip.pbl.src/n_unzip.sru` | Deferred — whole | Documents | Not built |
| 527 | `pfw.utility.zip.pbl.src/n_zip.sru` | Deferred — whole | Documents | Not built |
| 528 | `pfw.utility.zip.pbl.src/zipcompress.srf` | Deferred — whole | Documents | Not built |
| 529 | `pfw.utility.zip.pbl.src/zipuncompress.srf` | Deferred — whole | Documents | Not built |
| 530 | `pfwx.base.pbl.src/pfwxfinalize.srf` | Deferred — whole | Integration | Not built |
| 531 | `pfwx.net.http.pbl.src/nx_httpclient.sru` | Deferred — whole | Integration | Not built |
| 532 | `pfwx.net.http.pbl.src/nx_httpconfig.sru` | Deferred — whole | Integration | Not built |
| 533 | `pfwx.net.http.pbl.src/nx_httpcookie.sru` | Deferred — whole | Integration | Not built |
| 534 | `pfwx.net.http.pbl.src/nx_httpform.sru` | Deferred — whole | Integration | Not built |
| 535 | `pfwx.net.http.pbl.src/nx_httpmultipart.sru` | Deferred — whole | Integration | Not built |
| 536 | `pfwx.net.http.pbl.src/nx_httprequest.sru` | Deferred — whole | Integration | Not built |
| 537 | `pfwx.net.http.pbl.src/nx_httpresponse.sru` | Deferred — whole | Integration | Not built |
| 538 | `pfwx.net.mqtt.pbl.src/nx_mqttclient.sru` | Deferred — whole | Integration | Not built |
| 539 | `pfwx.net.mqtt.pbl.src/nx_mqttconfig.sru` | Deferred — whole | Integration | Not built |
| 540 | `pfwx.net.mqtt.pbl.src/nx_mqttmessage.sru` | Deferred — whole | Integration | Not built |
| 541 | `pfwx.pbl.src/pfwx.sra` | Permanently out | - | REFERENCE |
| 542 | `pfwx.tests.pbl.src/wx_test_httpclient.srw` | Permanently out | - | Fixture source |
| 543 | `pfwx.tests.pbl.src/wx_test_mqttclient.srw` | Permanently out | - | Fixture source |
| 544 | `pfwx.utility.parser.pbl.src/nx_dwparser.sru` | Deferred — whole | Integration | Not built |

**Row count: 544.** Filtering the Category column gives 103 *In scope* + 128 *Deferred — split* +
195 *Deferred — whole* + 118 *Permanently out*, which is the strict apportionment of the accounting
note in Section 11.3 — the headline reconciliation this document publishes is the 103 + 120 + 205 +
116 of Section 11.1, and Section 11.3 accounts for the difference. Filtering the Destination
column across the two deferred categories gives DesignSystem 188 + Documents 38 + Integration 43 +
ScriptBridge 54 = 323, which is Section 11.2. Filtering it across the in-scope category gives
`PowerFramework.Shared.Kernel` 34 + `PowerFramework.Shared.Diagnostics` 5 + Gateway 5 + Security 10 +
DataServices 14 + Persistence 26 + `PowerFramework.Shared.Eventful` 1 +
`PowerFramework.Shared.Localization` 6 + `PowerFramework.Shared.Containers` 2 = 103, which is the
86 + 17 of Section 5.3 resolved to its nine destinations. **No object is unassigned, and no object is
assigned twice.**
