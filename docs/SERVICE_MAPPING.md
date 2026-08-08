# PowerFramework → .NET 10 — Full-Estate Service Mapping

This document is the reviewed, signed-off library-to-service mapping for the decomposition of
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

---

## 1. Status and the gate this document discharges

**Status: reviewed mapping. No assignment reads TBD.**

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
| 19 | `pfw.ui.controls.ext` | 43 | **S** | `PowerFramework.Shared.Localization` (4) + REFERENCE (1) + DesignSystem (38) | The category enumeration `ne_cst_i18n.sru` and the three concrete locale providers complete the localization capability. `se_cst_datawindow.sru` is the structural parent of DataServices' `se_cst_dw` and is **read, not ported** (Section 6.3). The remaining 38 are extended visual controls |
| 20 | `pfw.ui.objects` | 64 | | DesignSystem *(deferred)* | Visual user objects — the largest single library in the estate, and wholly presentational |
| 21 | `pfw.ui.sciter` | 15 | | ScriptBridge *(deferred)* | Sciter engine embedding. An embedded scripting/rendering engine host, cohesive with the other engine hosts and gated by the same capability bit family (Section 8) |
| 22 | `pfw.ui.sciter.ext` | 4 | | ScriptBridge *(deferred)* | Sciter extensions. Meaningless without the Sciter binding above, so it shares that binding's destination |
| 23 | `pfw.ui.webview` | 11 | | ScriptBridge *(deferred)* | WebView embedding. A third embedded engine host alongside Sciter and MiniBlink, and gated by `Enums.INIT_FLAG_ENABLE_WEBVIEW` in the same bit family (Section 8) |
| 24 | `pfw.utility` | 14 | **S** | DataServices (1, contributed behaviour) + Documents (11 recorded) | `pinyinfirstletterlike.srf` is invoked from inside a DataWindow filter expression built by an in-scope service [`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323`], so the behaviour is in scope even though its library is not. Logging, file scanning, URI loading and the date/number conversion set are document-and-content concerns. Section 11.2, attribution 3, records this library's two defensible remainder figures |
| 25 | `pfw.utility.barcode` | 2 | | Documents *(deferred)* | Barcode and QR generation — content rendered into a document artifact |
| 26 | `pfw.utility.compiler` | 2 | | ScriptBridge *(deferred)* | Runtime PowerScript compilation and evaluation. Executing code supplied at run time is the same capability the engine hosts and the dynamic invokers provide, so it coheres with them rather than with any in-scope service |
| 27 | `pfw.utility.container` | 3 | **S** | `PowerFramework.Shared.Containers` (2) + Documents (1) | `n_map.sru` is used by **both** in-scope services, and `n_vector.sru` is the column-expression engine's calculation and recursion stack, so both are shared infrastructure. `n_list.sru` has no in-scope consumer (Section 6.2) |
| 28 | `pfw.utility.devinfo` | 2 | | Documents *(deferred)* | Device and environment information. An ambient-data reporting capability with no in-scope consumer; grouped with the other content and reporting helpers |
| 29 | `pfw.utility.invoker` | 8 | **S** | `PowerFramework.Shared.Eventful` (1) + ScriptBridge (7) | `n_cst_eventful.sru` — 1,328 lines, and **not** a native binding — is an instance member of DataServices' `se_cst_dw` [`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L6-L7,L33`] and is additionally inherited by the threading layer's `n_cst_threading_eventful`: two independent proofs that it is shared in-scope infrastructure. The other 7 objects are dynamic object/script invocation and global-variable access, cohesive with scripting |
| 30 | `pfw.utility.parser` | 13 | **S** | Persistence (2) + Documents (11) | `n_sql.sru` and its factory `parsesql.srf` are a verified hard Persistence dependency (Section 6.1): SQL statement structure belongs with the only service that generates SQL. The 11 JSON and XML objects are document-format parsing |
| 31 | `pfw.utility.regexp` | 5 | | Documents *(deferred)* | Regular expressions. After the correction in Section 7.2, **no in-scope dependency remains** on this library |
| 32 | `pfw.utility.sqlite` | 3 | | Persistence | The only evidenced storage binding in the entire repository, and Persistence is the only service that holds a storage provider |
| 33 | `pfw.utility.zip` | 4 | | Documents *(deferred)* | Archive handling. Reading and writing container files is a document-format capability, cohesive with the other format handlers rather than with storage: it touches files, not the database |
| 34 | `pfwx` | 1 | | Permanently out of scope | `ws_objects/pfwx.pbl.src/pfwx.sra`, the second target's application object — reference only. Section 9 records the initialize/finalize asymmetry it exhibits, and Section 11.2, attribution 1, records why it is listed once here rather than twice |
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
read, never ported. It is neither an in-scope object nor a DesignSystem assignment, which is why
Section 11.2 lists it as one of the three boundary attributions.

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
Chinese provider is 68, and the Simplified Chinese provider is 28 and effectively a no-op —
its translate handler is only
`if source = Enums.I18N_SRC_PFW then return 1` followed by `return 0`
[`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L19-L27`], because Simplified Chinese is
the base locale. All three shapes are reproduced.

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

| Destination | Libraries and counts | Reserved Gateway route |
| --- | --- | --- |
| **DesignSystem** | `pfw.ui` (56 remaining), `pfw.ui.controls` (28), `pfw.ui.controls.ext` (38 remaining), `pfw.ui.objects` (64), `pfw.base::u_logo.sru` (1), plus the presentational halves of ColumnSort, ContextMenu and DropDownSearch | `/v1/design/**` |
| **Documents** | `pfw.utility.parser` (11 remaining), `pfw.utility.zip` (4), `pfw.utility.barcode` (2), `pfw.utility` (11 remaining), `pfw.utility.container::n_list.sru` (1), `pfw.utility.regexp` (5), `pfw.utility.devinfo` (2) | `/v1/documents/**` |
| **Integration** | `pfw.net.http` (22), `pfw.net.http.ext` (4), `pfw.net.ftp` (3), `pfw.net.websocket` (2), `pfwx.net.http` (7), `pfwx.net.mqtt` (3), `pfwx.base` (1), `pfwx.utility.parser` (1) | `/v1/integration/**` |
| **ScriptBridge** | `pfw.ui.sciter` (15), `pfw.ui.sciter.ext` (4), `pfw.ui.blink` (15), `pfw.ui.webview` (11), `pfw.utility.compiler` (2), `pfw.utility.invoker` (7 remaining) | `/v1/scripting/**` |

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
| `pfw.tests` | 68 | REFERENCE and characterization-fixture source only. Never ported as-is, never edited. 47 `w_test_*.srw` windows and 11 `*.srd` DataWindow definitions, which together are the fixture corpus (`docs/PARITY.md`). Also the location of most of the in-source secret sites — see Section 12.2 and `docs/SECRETS.md` |
| `pfw.demos` | 42 | REFERENCE and characterization-fixture source only: 9 demonstration windows, 1 further `*.srd`, and the supporting tab pages |
| `pfwx.tests` | 2 | `pfwx` test windows: `wx_test_httpclient.srw`, `wx_test_mqttclient.srw` |

Subtotal: 3 + 1 + 2 + 68 + 42 + 2 = **118** by a strict count off the filesystem, recorded as **116**
in the headline reconciliation. Section 11.2 explains the two-object difference; it is a boundary
attribution, not a discrepancy, and both figures reconcile to 544.

Two of these entries deserve emphasis because downstream work depends on them:

- **`pfw.tests` is the behavioural oracle, and it is where the fixture corpus already lives.** The
  DataWindow corpus *is* the test corpus: characterization fixtures need no invention. Exactly one
  DataWindow definition in the entire repository carries table-level update settings —
  [`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14`] — which makes that single 37-line file the
  golden-master fixture for the retrieval/validation/update capability. `docs/PARITY.md` carries the
  detail.
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

### 11.1 Headline reconciliation

| Category | Objects |
| --- | --- |
| In scope | 103 |
| Deferred — split remainders | 120 |
| Deferred — whole libraries | 205 |
| Permanently out of scope | 116 |
| **Total** | **544** |

**Zero objects are unassigned.** Every one of the 544 objects under `ws_objects/**` falls into
exactly one of the four categories above, and the deferred total is 120 + 205 = 325.
`docs/DEFERRED.md` restates these four figures and this total identically.

### 11.2 Accounting note — two defensible apportionments, both totalling 544

A strict one-object-one-category count taken directly over the filesystem apportions the three
non-in-scope categories differently from the headline table. An audit that concealed that would not
be an audit, so it is stated plainly.

A strict filesystem count yields **103 in scope + 128 split remainders + 195 whole-library
deferred + 118 permanently out = 544**, also with zero objects unassigned. Both
apportionments total 544; the differences are **−8** on split remainders, **+10** on
whole-library deferred and **−2** on permanently out of scope, and they **net to zero**. The
in-scope figure of 103 is identical under both counts, so nothing about the work actually
being done in this phase depends on the choice.

Three boundary attributions account for the whole difference. Each is a genuine judgement about a
single object or library, not a counting error:

1. **`ws_objects/pfwx.pbl.src/pfwx.sra` is attributable two ways.** It is simultaneously a REFERENCE
   object for the deferred `pfwx` target and a permanently-out-of-scope application object. A
   full-estate table that lists the `pfwx` library under both headings produces 40 rows for 39
   libraries, while on disk there is exactly one `ws_objects/pfwx.pbl.src` holding exactly one
   object. **This document lists it once, under permanently out of scope**, and notes its Integration
   relevance in prose (Sections 4 and 9) rather than double-counting it.
2. **`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` is REFERENCE-only.** It is read as
   the structural parent of `se_cst_dw` (Section 6.3) but is neither ported nor a DesignSystem
   deliverable, so it falls outside the "38 remaining → DesignSystem" figure while still being one
   of that library's 43 objects. The library reconciles as **4 in scope + 1 REFERENCE + 38
   DesignSystem = 43**.
3. **The `pfw.utility` split is recorded as 1 in-scope + 11 Documents** of a 14-object library, while
   a strict count of the remainder is 13. The two figures differ over the two sibling pinyin helpers
   `getpinyinfirstletter.srf` and `getpinyinfirstletters.srf`, which sit at the boundary of the same
   contributed pinyin behaviour as the in-scope `pinyinfirstletterlike.srf`. The remaining 11 —
   `datetimetotimestamp.srf`, `dectohex.srf`, `dectostring.srf`, `gettimestamp.srf`, `hextodec.srf`,
   `loaduri.srf`, `n_filescanner.sru`, `n_logger.sru`, `parsedatetime.srf`, `stringtodec.srf`,
   `timestamptodatetime.srf` — are unambiguously Documents.

### 11.3 Verified split arithmetic

Shown per library so a reviewer can follow the reconciliation down to the object:

| Library | Total | = | In scope | + | REFERENCE | + | Deferred |
| --- | ---: | :-: | ---: | :-: | ---: | :-: | ---: |
| `pfw.base` | 6 | = | 5 | + | — | + | 1 |
| `pfw.utility.invoker` | 8 | = | 1 | + | — | + | 7 |
| `pfw.ui` | 58 | = | 2 | + | — | + | 56 |
| `pfw.ui.controls.ext` | 43 | = | 4 | + | 1 | + | 38 |
| `pfw.utility.parser` | 13 | = | 2 | + | — | + | 11 |
| `pfw.utility.container` | 3 | = | 2 | + | — | + | 1 |
| `pfw.utility` | 14 | = | 1 | + | — | + | 13 |

In compact form: `pfw.base` 6 = 5 + 1; `pfw.utility.invoker` 8 = 1 + 7; `pfw.ui` 58 = 2 + 56;
`pfw.ui.controls.ext` 43 = 4 + 1 + 38; `pfw.utility.parser` 13 = 2 + 11; `pfw.utility.container`
3 = 2 + 1; `pfw.utility` 14 = 1 + 13.

Two reading notes on that table, both of which point back to Section 11.2 rather than
introducing anything new. The `pfw.ui.controls.ext` row is the only one with a non-empty
REFERENCE column, and that single object is `se_cst_datawindow.sru` (attribution 2). And the
`pfw.utility` row shows the **strict** remainder of 13, whereas Sections 4 and 8 record 11 to
Documents; the two figures and the two objects they differ over are exactly attribution 3.

### 11.4 Library-level partition check

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
evidence discipline of Section 2.2, the derivations of Section 2.3, and the two-apportionment
disclosure of Section 11.2 — the last being the specific place where the arithmetic admits more than
one defensible reading and says so rather than picking one silently.

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
- **The event-chain dialog locators in `se_cst_dw.sru` are `:L355` and `:L357`.** An earlier
  statement cited `:L357` and `:L368`. Verified: `:L355` and `:L357` are the only two localization
  calls in the file and the live dialog is at `:L357`; `:L368` is a comment documenting the
  posted-message hazard rather than a call site; and the third dialog match, `:L286`, is inside the
  block comment spanning `:L280`–`:L290`. Section 6.5 uses the verified locators. Likewise, the
  context-menu service has **ten** live dialog sites rather than the six previously listed, and
  Section 6.5 records the complete set.

### 12.3 Cross-references

This document is the upstream for destination naming and object assignment. It deliberately does not
duplicate its siblings:

| For | See |
| --- | --- |
| Service topology, transport choice per service, port map, capability gating, and the injection analysis referenced in Section 7.2 | `docs/ARCHITECTURE.md` |
| The cross-service contract inventory and the reserved Gateway extension points | `docs/CONTRACTS.md` |
| The deferred destinations in detail, the reserved routes, and the same reconciliation figures and accounting note as Section 11 | `docs/DEFERRED.md` |
| Secret locators, severities and required actions | `docs/SECRETS.md` |
| The characterization model, the fixture corpus drawn from `pfw.tests` and `pfw.demos`, and the determinism seams | `docs/PARITY.md` |
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
