# PowerFramework → .NET 10 — Deferred Services and Documented Capability Gaps

This document is the **record of scope** for the four capability destinations that this refactor
maps but does **not** build. Phase 1 of the decomposition of **PowerFramework** — an Appeon
PowerBuilder 2021 desktop framework — implements exactly four of an eight-service target roster. The
other four — **DesignSystem**, **Documents**, **Integration** and **ScriptBridge** — receive **no
code, no test and no container** in this refactor. They exist here as destination assignments and
nowhere else in the delivery except as four named routes on Gateway's routing metadata.

That gives this document an unusual double duty. It is the place where the four deferred
destinations are described *most fully*, because an object-level assignment is what makes the shape
of the eventual system legible; and it is simultaneously the place where the prohibition on building
them must be stated *most clearly*, because a reader who mistook an assignment for a mandate would
produce exactly the half-built service the brief forbids. The resolution applied throughout is to be
**precise about scope and silent about method**: this document says exactly which objects belong
where and exactly which artifacts must not exist, and says nothing at all about how any deferred
destination would be built.

---

## Table of contents

1. [Position: what this document is, and what it is not](#1-position-what-this-document-is-and-what-it-is-not)
2. [Method and evidence discipline](#2-method-and-evidence-discipline)
3. [The four deferred services](#3-the-four-deferred-services)
4. [Four dependencies that do not pull a deferred library into scope](#4-four-dependencies-that-do-not-pull-a-deferred-library-into-scope)
5. [The reserved Gateway routes are metadata, not stubs](#5-the-reserved-gateway-routes-are-metadata-not-stubs)
6. [Three capability gaps inside in-scope logic](#6-three-capability-gaps-inside-in-scope-logic)
7. [Reconciliation](#7-reconciliation)
8. [The native surface behind the deferred estate](#8-the-native-surface-behind-the-deferred-estate)
9. [Capability gating corroborates the boundary](#9-capability-gating-corroborates-the-boundary)
10. [Constraint compliance, cross-references, and what this document does not claim](#10-constraint-compliance-cross-references-and-what-this-document-does-not-claim)

---

## 1. Position: what this document is, and what it is not

### 1.1 Deferral is a hard scope boundary, not a soft priority

The four destinations named above are **outside this refactor's scope entirely**. They are not
de-prioritised work, not a backlog, and not a later stage of the work being done now. The
distinction carries weight because the refactor executes in **one** phase: root plumbing, the shared
libraries, all four in-scope services, orchestration, continuous integration, documentation and the
parity scaffolding are produced together. There is no second stage of this refactor into which a
deferred destination could slide.

Treating deferral as sequencing rather than as a boundary is precisely the reading that would invite
a partial implementation — a placeholder project "to be filled in", an empty test project "ready for
later", a class that throws so that something compiles. Every one of those is prohibited. The
governing judgement principle is stated in the brief and applied without exception here: **a
half-built deferred service is worse than a documented gap**, because a half-built service looks
finished and a documented gap does not.

### 1.2 Exactly two permitted representations

The four deferred destinations may be represented in **two ways, and no others**:

| # | Representation | Where it lives |
| --- | --- | --- |
| 1 | **Destination assignments** in the full-estate mapping — which library and which objects belong to which destination | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md), summarised at object level in Section 3 below |
| 2 | **Four named routes** on Gateway's routing and contract metadata, each returning a not-implemented status | [`CONTRACTS.md`](CONTRACTS.md) §13, with the architectural statement in [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.4; restated in auditable form in Section 5 below |

Anything beyond those two is out of bounds. Section 5 states the boundary as a checkable list of
artifacts that must not exist, so that a reviewer can verify compliance mechanically rather than
argue about intent.

### 1.3 Discovery rigor was not relaxed on the deferred majority

Of the 544 legacy objects under `ws_objects/**`, **441 — roughly 81% — are not implemented in this
phase**: 325 deferred across the four destinations, and 116 permanently out of scope. That majority
does not license shallow mapping, and it did not receive it. All **39** exported libraries and all
**544** objects are assigned to a destination with a justification grounded in capability cohesion,
and the arithmetic reconciles to the object with **zero objects unassigned**. Section 7 carries the
reconciliation and the accounting note that lets a reviewer follow it.

Every assignment below was measured rather than estimated. The library and object counts in Section
3 were taken directly off the filesystem, and each was checked against the library lists the legacy
targets declare for themselves at [`pfw.pbt`] and [`pfwx.pbt`].

### 1.4 No user-specified rules exist

The project's rules document was retrieved and it contains exactly one statement: no user rules were
provided. It is a single line, and re-verification returned the same result.

Three consequences, stated so the absence cannot be mistaken for latitude. No rule is invented,
inferred or back-filled from convention. **Zero files enter scope because of a rule** — every
assignment in this document traces to an explicit clause of the brief or to a dependency edge
verified by inspection. And enterprise-standard best practice applies in the rules' place, which for
a document of this kind means an honest, enumerable statement of what is *not* being built rather
than an optimistic account of what might be. The binding constraints therefore come from the
refactor's own clauses; Section 10.1 records how this document honours each of the four that govern
it.

---

## 2. Method and evidence discipline

### 2.1 Every behavioural claim carries a locator

Claims in this document about what the legacy *does* are cited to a path under `ws_objects/**` with
line numbers, in the form [`ws_objects/…/object.sru:L123`], and subsequent citations of the same
object shorten to [`:L123`]. Structural facts — object counts, library membership — are cited to the
directory or to the target file that declares the library list.

The discipline is not stylistic. **Nothing else in the repository can adjudicate behaviour:**

- The changelog is stale. `logfile.md` opens at `## 3.0.7.2062(2022-04-14)` and stops there, while
  the commit history runs years past it, and the history carries changes the changelog never
  recorded. There are **zero Git tags** in the entire history, so no release is marked either.
- The two PowerBuilder build definitions contradict each other and neither would build as written.
  They disagree on library count and on the vendor field, and decisively **both name
  `pfw.utility.imgcodec.pbl`** — [`ws_objects/pfw.pbl.src/project.srj:L41`] and
  [`ws_objects/pfw.pbl.src/p_pfw.srj:L40`] — while a full-depth search for that library returns
  nothing. [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §10.3 carries the comparison.

So a locator is the only admissible evidence, and that applies with equal force to the deferred
estate: an assignment made without one would be a guess dressed as discovery.

### 2.2 The legacy tree is read-only, and that includes the deferred 81%

Every path cited in this document is **read as evidence and never modified**. Nothing here instructs
an edit, a deletion, a move, a rename or a reformat of any `ws_objects/**` path, of any root
`*.pbl`, `*.pbt`, `*.pbw`, `*.pbr` or `*.pbd`, of `oldversion/125/**`, of `pack/**`, of any shipped
native binary, or of the legacy browser assets under `tests/blink/`, `tests/sciter/` and
`tests/webview/`. The deferred majority is read-only on exactly the same terms as the in-scope
minority; being unimplemented does not make an object editable.

**The five pre-existing Chinese documents in this very folder are read-only too** —
`docs/README.md`, `docs/Blink交互.md`, `docs/Sciter交互.md`, `docs/PB多线程绕坑提示.md` and
`docs/n_cst_dwsvc_columnexp.md`. None of them is edited, translated, re-encoded, renamed or
link-rewritten by this refactor, and this document is purely **additive** beside them.

Two of the five are natural references for the deferred estate, because they document capability
areas that belong to ScriptBridge: [`Blink交互.md`](Blink交互.md) and [`Sciter交互.md`](Sciter交互.md) each
describe the JavaScript-to-PowerScript bridge that the corresponding embedded engine exposes — the
entry object, the global-variable and global-function accessor, and the host object bound to each
engine window or control. They are cited here as read-only references for what ScriptBridge's
capability area covers; they are not touched.

One concrete reason the re-encoding prohibition is not theoretical: the two files are not even
stored in the same encoding as each other. `docs/Sciter交互.md` is UTF-8, whereas `docs/Blink交互.md` is
**not** — it fails a UTF-8 decode outright and decodes cleanly only under a legacy Chinese code
page. A well-meaning tool that "normalised" the second to match the first would destroy its content,
and that content is behavioural reference material. Read both; convert neither.

### 2.3 What this document deliberately does not contain

Because C-D governs it, this document contains **no** task list, no schedule, no ordering, no effort
estimate, no commitment about when a deferred destination would be built, and no design proposal for
any of the four. Naming a destination and the capabilities it will eventually own is discovery.
Describing how to build it is not, and none of that appears here.

It also contains no service-level figure of any kind, per C-B, and reproduces **no** credential,
key, certificate or secret value in any form — see Section 4.5 and [`SECRETS.md`](SECRETS.md).

---

## 3. The four deferred services

**A destination assignment in this section is a mapping statement and nothing more.** It records
*where a capability belongs*. It is not an instruction to build, and no subsection below describes
how any of the four would be built.

Every assignment is justified by **capability cohesion** — the capability is grouped with the other
capabilities it is inseparable from, and separated from those it is genuinely independent of. No
assignment is made for translation convenience, and none is made on any operational ground.

### 3.1 The roster

| Deferred service | Libraries and object counts assigned | Reserved Gateway route |
| --- | --- | --- |
| **DesignSystem** | `pfw.ui` (56 remaining of 58), `pfw.ui.controls` (28), `pfw.ui.controls.ext` (38 remaining of 43), `pfw.ui.objects` (64 — the largest deferred library), `pfw.base::u_logo.sru` (1), plus the **presentational halves** of ColumnSort, ContextMenu and DropDownSearch | `/v1/design/**` |
| **Documents** | `pfw.utility.parser` (11 remaining of 13), `pfw.utility.zip` (4), `pfw.utility.barcode` (2), `pfw.utility` (11 remaining of 14), `pfw.utility.container::n_list.sru` (1), `pfw.utility.regexp` (5), `pfw.utility.devinfo` (2) | `/v1/documents/**` |
| **Integration** | `pfw.net.http` (22), `pfw.net.http.ext` (4), `pfw.net.ftp` (3), `pfw.net.websocket` (2), `pfwx.net.http` (7), `pfwx.net.mqtt` (3), `pfwx.base` (1), `pfwx.utility.parser` (1) | `/v1/integration/**` |
| **ScriptBridge** | `pfw.ui.sciter` (15), `pfw.ui.sciter.ext` (4), `pfw.ui.blink` (15), `pfw.ui.webview` (11), `pfw.utility.compiler` (2), `pfw.utility.invoker` (7 remaining of 8) | `/v1/scripting/**` |

Every count in that table was taken directly off the filesystem. Where a library is split, the
in-scope contribution and the deferred remainder are shown together in Section 7.3 so the split
arithmetic can be followed per library.

### 3.2 DesignSystem — `/v1/design/**`

**Capabilities it owns.** User interface controls and user objects, theming, geometry structures,
colour functions, the DPI conversion family, canvas, painter, font, image and image list, popup
menus, tooltips, the tray icon, the timer, `win32` interop, and the logo control.

**Why these cohere.** Every one of them exists to put something on a screen or to measure something
that will be put on a screen, and each is unusable without the others: a popup menu needs font
measurement to size itself, font measurement needs DPI conversion to be meaningful, and DPI
conversion is only meaningful against a real device context. The two largest deferred libraries —
`pfw.ui.objects` at 64 objects and `pfw.ui` at 58 — are almost entirely visual user objects and
presentation primitives respectively, and there is no seam inside them at which a headless subset
could be cut without leaving both halves incomplete.

`pfw.base::u_logo.sru` joins DesignSystem for the same reason and against the grain of its own
library: `pfw.base` is otherwise the framework-lifecycle library and its other five objects are in
scope for Gateway's composition root, but [`ws_objects/pfw.base.pbl.src/u_logo.sru`] is a visual
user object, so capability cohesion places it here rather than library membership placing it there.

DesignSystem is also where the **rendering halves** of the three part-presentational DataWindow
services belong — window positioning, DPI-to-pixel conversion, font measurement and actual menu
rendering. Section 6 enumerates those three gaps in full, because they are the only place where
deferral removes something from inside a capability that otherwise ships.

### 3.3 Documents — `/v1/documents/**`

**Capabilities it owns.** JSON and its helpers, the XML object family, ZIP archives, barcode and QR
generation, file scanning, logging, and the date and number conversion set.

**Why these cohere.** These are content and document-format concerns: each takes a byte stream or a
scalar and produces a differently-shaped representation of the same information. `pfw.utility.zip`
(4 objects), `pfw.utility.barcode` (2) and `pfw.utility.devinfo` (2) are whole libraries with no
in-scope consumer at all. The two split contributions are narrower and are worth naming precisely:

- **`pfw.utility.parser` splits 13 = 2 + 11.** The two in-scope objects are the SQL clause parser
  and its factory function, which are a hard Persistence dependency; the remaining **11** are
  exactly five JSON objects and six XML objects, and all eleven are Documents. The split is a
  capability boundary, not a convenience: parsing a `SELECT` statement into clauses and parsing a
  JSON document share a library in the legacy but share no behaviour.
- **`pfw.utility` splits 14 = 1 + 11 recorded** (13 by a strict count of the remainder — see Section
  7.2, attribution 3). The one contributed in-scope behaviour is the pinyin first-letter match
  invoked from inside a DataWindow filter expression; `n_logger.sru`, `n_filescanner.sru`,
  `loaduri.srf` and the date and number conversion functions are Documents.

`pfw.utility.container::n_list.sru` joins Documents as the one container type in that three-object
library with no in-scope consumer; the ordered map and the vector are in scope as a shared container
library.

### 3.4 Integration — `/v1/integration/**`

**Capabilities it owns.** Outbound HTTP and its extensions, FTP, WebSocket, MQTT, and the `pfwx.*`
transports.

**Why these cohere.** Every object here opens an outbound connection to something outside the
process. `pfw.net.http` (22 objects) is the largest, with `pfw.net.http.ext` (4) extending it,
`pfw.net.ftp` (3) and `pfw.net.websocket` (2) alongside. The second-generation transports —
`pfwx.net.http` (7) and `pfwx.net.mqtt` (3) — come with the two small libraries that exist only to
serve them: `pfwx.base` (1), the `pfwx` lifecycle function, and `pfwx.utility.parser` (1). Grouping
those two elsewhere would separate a lifecycle function from the only transports that call it.

No in-scope service performs any outbound network call to a third party, which is what makes this
grouping clean: there is no in-scope consumer to strand.

### 3.5 ScriptBridge — `/v1/scripting/**`

**Capabilities it owns.** Sciter and its extensions, MiniBlink, WebView embedding, the PowerBuilder
compiler and evaluator, dynamic object and script invocation, and global-variable access.

**Why these cohere.** Each of these hands control to an engine outside PowerScript and marshals
values back — three embedded engines across four libraries (`pfw.ui.sciter` 15 objects with
`pfw.ui.sciter.ext` 4 extending it, `pfw.ui.blink` 15, `pfw.ui.webview` 11), a runtime PowerScript
compiler and evaluator (`pfw.utility.compiler` 2), and the dynamic-invocation family that reaches
objects, scripts and global variables by name rather than by type (`pfw.utility.invoker`, 7 of 8
objects).

`pfw.utility.invoker` splits 8 = 1 + 7. The single in-scope object is the event broker,
[`ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru`] — 1,328 lines with no native binding
at all, so pure PowerScript — and it is not a dynamic-invocation facility in any sense. Two
independent structural facts require it to be a shared in-scope library rather than a ScriptBridge
object: it is an instance member of the in-scope DataWindow service extension
([`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L6-L7`] declares the nested type and
[`:L33`] the instance), and it is the **base class** of one of the in-scope threading classes
([`ws_objects/pfw.thread.pbl.src/n_cst_threading_eventful.sru:L4`], [`:L10`]). The other seven —
object invocation, script invocation, and the four global-variable and global-function accessors —
are ScriptBridge. Section 4.2 records why the apparent in-scope dependency on script invocation is
not a real one.

The two Chinese documents [`Blink交互.md`](Blink交互.md) and [`Sciter交互.md`](Sciter交互.md) are read-only
references for this destination's capability area, as recorded in Section 2.2.

---

## 4. Four dependencies that do not pull a deferred library into scope

Four findings each look, on a first reading, like a reason to bring a deferred library into scope.
None of them is. They are recorded here so that a future reader who finds the call site does not
re-open a settled question — and so that nobody widens the Phase-1 slice on the strength of a
dependency that dissolves on inspection.

### 4.1 `pfw.utility.regexp` is deferred, and no in-scope dependency remains on it

The whole five-object library goes to Documents. Its single in-scope use is one pair of lines in the
SQL task layer that parses the connection parameter string for two binding flags:
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128`] matches `DisableBind`
against the parameter string and [`:L129`] does the same for `NCharBind`. Both are satisfied by the
base class library's regular-expression types, so the library is deferred with **no in-scope
dependency remaining** on it.

### 4.2 The dynamic-invocation objects are a variadic-call escape hatch, not a scripting dependency

Script invocation appears in two in-scope objects, and in both it is a workaround for a language
limitation rather than a scripting capability. PowerScript cannot forward an argument list of
arbitrary length, so the code enumerates the arities it supports and falls back to dynamic
invocation only past the enumerated maximum:

| In-scope call site | Declaration | Unrolled ceiling | Create / destroy |
| --- | --- | ---: | --- |
| [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru`] | [`:L601`] | **20** positional arguments, at [`:L690-L692`] | [`:L695`], [`:L701`] |
| [`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru`] | [`:L423`] | **8** positional arguments, at [`:L465-L466`] | [`:L469`], [`:L475`] |

C# has native variadic support, so the workaround has **no analogue to port**, and **Persistence
acquires no ScriptBridge coupling**. The seven dynamic-invocation objects of `pfw.utility.invoker`
stay deferred with nothing in scope depending on them.

The observable arity ceilings are recorded as **legacy limits, not requirements**: 20 in the SQL
base task, 8 in the transaction object, and 11 in the SQLite binding, whose command and two query
entry points each accept `any arg1` through `any arg11`
[`ws_objects/pfw.utility.sqlite.pbl.src/n_sqlite.sru:L43`], [`:L55`], [`:L68`]. A .NET contract may
exceed any of these without a behavioural regression, because exceeding a ceiling the legacy could
never reach cannot change an observable legacy result.

### 4.3 Localization acquires no Documents coupling

The legacy locale providers read the translation table with the XML object family that is deferred
to Documents. [`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L13`] declares
`privatewrite n_xmldoc _doc` and [`:L158-L159`] creates it and loads the root resource file; the
Traditional Chinese provider does the same and additionally uses a query-result type, at
[`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L13`], [`:L31`] and [`:L62-L63`].

The base class library's XML reader is substituted for both. **The XML object family therefore stays
deferred with nothing in scope depending on it**, and the shared localization library carries no
Documents coupling of any kind.

### 4.4 `pfwx.utility.codec.pbl` is assigned to no destination, because it has no source export

The compiled library exists at the repository root and appears on the second target's library list —
the evidence is [`pfwx.pbt:L6`], whose `LibList` names it between the parser and base libraries —
but **`ws_objects/pfwx.utility.codec.pbl.src` does not exist**. It is the only compiled library in
the repository lacking a source export, which is exactly why 39 export folders reconcile against 40
root `*.pbl` files.

With no source export there is no object to assign and no behaviour to read, so it contributes **0
objects** to the 544-object reconciliation and **no capability** to any destination. It is an
anomaly in the evidence rather than a hole in the mapping;
[`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §10.1 records it as such.

### 4.5 A corrected attribution about credential material in the MQTT capability area

An earlier statement of this mapping placed five in-source secret sites in the `pfwx.net.mqtt`
transport library. **Direct measurement does not support that.** The three objects of
`ws_objects/pfwx.net.mqtt.pbl.src` — `nx_mqttclient.sru`, `nx_mqttconfig.sru` and
`nx_mqttmessage.sru` — contain no certificate, key or credential material of any kind. The
MQTT-related credential sites are in the MQTT **test window** under `ws_objects/pfw.tests.pbl.src/`,
which is permanently out of scope (Section 7.1), with further sites in the demonstration library.

The correction changes **no assignment** in this document — `pfwx.net.mqtt` is Integration either
way — but it does change which library a reader should treat as carrying credentials, which is why
it is recorded rather than quietly fixed. [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §12.2 records
the same correction from the mapping side.

**No value, key fragment, certificate body or credential appears anywhere in this document.**
[`SECRETS.md`](SECRETS.md) is the single register of locators, severities and required actions, and
locators are deliberately not restated here.

---

## 5. The reserved Gateway routes are metadata, not stubs

This is the section an auditor should read first, because it is where the prohibition becomes
checkable.

### 5.1 What the four routes are

Gateway's routing metadata declares four reserved routes. Each returns **`501 Not Implemented`**
with a **machine-readable body** naming the deferred service it will eventually reach and carrying
the marker `reserved for Phase 2`. The body is structured rather than a text message so a client can
branch on it; [`CONTRACTS.md`](CONTRACTS.md) §13 defines the field set.

| Route | Deferred service | Capabilities it will eventually reach |
| --- | --- | --- |
| `/v1/design/**` | **DesignSystem** | Theming, geometry structures, colour functions, the DPI conversion family, canvas, painter, font, image, image list, popup menu, tooltip, tray icon, timer, `win32` interop, the logo control, and the **presentational halves** of ColumnSort, ContextMenu and DropDownSearch |
| `/v1/documents/**` | **Documents** | JSON and its helpers, the XML object family, ZIP, barcode and QR, file scanning, logging, and the date/number conversion set |
| `/v1/integration/**` | **Integration** | HTTP client and its extensions, FTP, WebSocket, MQTT, and the `pfwx.*` transports |
| `/v1/scripting/**` | **ScriptBridge** | Sciter and its extensions, MiniBlink, WebView embedding, the PowerScript compiler and evaluator, dynamic object and script invocation, and global-variable access |

### 5.2 The compliance note, stated so it is auditable rather than argued

> **A routing declaration is not a stub of the deferred service.**

For each of DesignSystem, Documents, Integration and ScriptBridge there is:

- **no service directory** — no `services/design-service/`, no `services/documents-service/`, no
  `services/integration-service/`, no `services/scripting-service/`;
- **no project file** — no `PowerFramework.DesignSystem.csproj`, no
  `PowerFramework.Documents.csproj`, no `PowerFramework.Integration.csproj`, no
  `PowerFramework.ScriptBridge.csproj`;
- **no container definition**, not even a placeholder `Dockerfile`;
- **no test project**, not even an empty one;
- **no partial implementation** of any capability assigned to it;
- **no exception-throwing placeholder class** of any kind — no type that throws
  `NotImplementedException` to make something compile;
- **no entry in the root solution** and no entry in any per-service solution;
- **no signing, verification or mutual-TLS configuration variable** scaffolded — see
  [`SECRETS.md`](SECRETS.md); and
- **no port allocated.** The 5103 slot is left in the orchestration manifest as a **commented-out
  placeholder** rather than reassigned, because the attached environment had assigned that port to a
  design service and DesignSystem is precisely one of the deferred four; leaving it commented keeps
  the reserved slot legible instead of erasing the signal. [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.3
  records that decision, and §4.1 carries the port map.

### 5.3 Why the distinction holds

The route exists so that **the shape of the eventual system is legible from Gateway's contract**,
which is what the brief asks for. The prohibition is on *implementing* the deferred services — not
even partially, not even to stub them out — and a declaration in a route table implements nothing.
It has no handler beyond the constant response described above, it calls nothing, and it can reach
nothing, because there is nothing behind it to reach.

The test is mechanical, which is the point: search the repository for a project, container
definition, test project, solution entry or type belonging to any of the four names. **Finding none
is the pass condition**, and the four `501` routes do not change that result. The same statement is
made from the architectural side in [`ARCHITECTURE.md`](ARCHITECTURE.md) §4.4 and from the contract
side in [`CONTRACTS.md`](CONTRACTS.md) §13.1 — deliberately, so that no single document can be read
as the only place the boundary was claimed.

---

## 6. Three capability gaps inside in-scope logic

This is the one place where deferral removes something from inside a capability that otherwise
ships, so it is documented at the greatest depth. Three of the five DataWindow services are
**irreducibly presentational**. Each is therefore **split**: a **headless half** ships in
DataServices, and a **rendering half** is deferred to DesignSystem and named as a reserved Gateway
extension point under `/v1/design/**`.

### 6.1 The split is evidence-driven, not arbitrary

Verified type-position use of window, DPI, font and menu primitives, all in
`ws_objects/pfw.datawindow.services.pbl.src/`:

- **`n_cst_dwsvc_dropdownsearch.sru`** calls `Win32.ShowWindow` [`:L256`], `Win32.GetWindowRect`
  [`:L474`], `Win32.OffsetRect` [`:L475`] and `Win32.SetWindowPos` [`:L489`]. It also declares a
  window prototype of its own, `IsWindowVisible` bound to `user32.dll` [`:L43`].
- **`n_cst_dwsvc_contextmenu.sru`** calls `Win32.GetWindowRect` [`:L187`] and
  `Win32.PX2MMX(D2PX(…))` [`:L1241`], [`:L1243`], [`:L1409`], [`:L1411`]; declares and creates
  `n_cst_font` [`:L1092`], [`:L1098`], [`:L1266`], [`:L1277`]; and exposes an
  `n_cst_popupmenu`-typed submenu API — the member declaration at [`:L18`] and the overload block at
  [`:L96-L106`].
- **`n_cst_dwsvc_columnsort.sru`** calls `Win32.PX2MMY(U2PY(10))` [`:L359`], [`:L361`].

And the contrast that proves the other two need no split at all: **`n_cst_dwsvc.sru` is 864 lines
with zero presentation references**, and **`n_cst_dwsvc_rowselect.sru` is 288 lines with exactly
one** — the dialog at [`:L239`], which Section 6.3 accounts for. Both figures were counted directly
over the same primitive set; [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §6.4 carries the full
per-object count table.

### 6.2 What ships and what does not

| Legacy UI capability | Headless half — **ships** in DataServices | Rendering half — **deferred** to DesignSystem |
| --- | --- | --- |
| Drop-down search | Filter-expression construction, **including the pinyin filter clause**; the search state machine; row and filtered counts | Window positioning via `ShowWindow` / `GetWindowRect` / `OffsetRect` / `SetWindowPos`, and input-method text entry |
| Context menu | The **complete menu item model** — labels, ids, enabled and split flags, and computed logical text widths | DPI-to-pixel conversion, font measurement, and actual menu rendering |
| Column sort | Sort-expression construction and sort state | Sort-indicator geometry via DPI conversion |

**Nothing is silently dropped.** Each rendering half is surfaced as **data over the contract** —
menu item models, computed logical text widths, and filter and sort expressions — and the rendering
itself is named as a reserved extension point under `/v1/design/**`. The gap is explicit and
enumerable, and a reader can list precisely what does not ship by reading the right-hand column
above. That is the whole point of documenting it this way: a documented gap is genuinely better than
a half-built service, because the gap can be audited and the half-built service cannot.

### 6.3 Dialogs are a real presentation dependency inside in-scope logic

Dialog calls appear inside logic that is otherwise headless. They are handled by changing **only the
delivery channel**: each becomes a structured error result preserving the exact message text, the
localization category, the substitution arguments, the severity, and for expression parse failures
the expression text plus the caret position.

Verified live call sites, each checked against both line-comment and block-comment state:

| Object | Live dialog sites | Routed through localization? |
| --- | --- | --- |
| `n_cst_dwsvc_rowselect.sru` | [`:L239`] | yes — `I18N(ne_cst_i18n.CAT_DWSVC, …)` wrapped in `Sprintf` |
| `n_cst_dwsvc_contextmenu.sru` | [`:L795`], [`:L863`], [`:L916`], [`:L1009`], [`:L1018`], [`:L1027`], [`:L1033`], [`:L1039`], [`:L1045`], [`:L1052`] — ten sites | yes — all ten route through `I18N(ne_cst_i18n.CAT_DWSVC, …)` |
| `se_cst_dw.sru` | [`:L357`], paired with its message lookup at [`:L355`] | yes — both are `I18N(ne_cst_i18n.CAT_DWSVC, …)` calls, and they are the only two in the file |
| `n_cst_dwsvc_columnexp.sru` | **28** sites: [`:L713`], [`:L739`], [`:L762`], [`:L1308`], [`:L1383`], [`:L1390`], [`:L1395`], [`:L1399`], [`:L1417`], [`:L1422`], [`:L1426`], [`:L1431`], [`:L1486`], [`:L1505`], [`:L1607`], [`:L1671`], [`:L1882`], [`:L1888`], [`:L2122`], [`:L2133`], [`:L2192`], [`:L2208`], [`:L2232`], [`:L2242`], [`:L2254`], [`:L2282`], [`:L2306`], [`:L2392`] | **no — zero of the 28 route through localization** |

**A defect to preserve rather than harmonize** falls straight out of that table: **all 28
column-expression messages are hardcoded and do not route through localization, while the
row-select, context-menu and event-chain messages all do.** The measurement is stronger than the
site count suggests — `n_cst_dwsvc_columnexp.sru` contains **no localization call at all** across
its 2,435 lines, so this is not a handful of sites that were missed but a whole object that never
adopted the mechanism its siblings use. That inconsistency is legacy behaviour and is reproduced,
not corrected.

Two adjacent findings in the same family are recorded for the same reason, and both are the kind a
naive locator count would get wrong.
[`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L286`] carries a dialog **inside** the
block comment spanning [`:L280`]–[`:L290`] — a dormant byte-length validation path that stays
commented and inert. And [`:L368`] is a **comment, not a call site**: it documents that the dialog's
posted message may have deleted the row before control returns, which is why the surrounding guard
re-checks row existence.

### 6.4 `se_cst_dw` inherits across the deferred boundary

The in-scope DataWindow service extension derives from a parent that lives in a deferred library.
Both the forward declaration and the type definition carry it:
[`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L4`] and [`:L10`] each read `global type
se_cst_dw from se_cst_datawindow`, and the parent in turn derives from a further presentation
ancestor at [`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru:L4`], [`:L8`].

This is a **structural inheritance edge, not a call**, so no refactor of a call site removes it.

**Resolution.** DataServices defines its own abstract host contract carrying only the members
`se_cst_dw` actually consumes from its parent, and implements against that.
`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` is recorded **REFERENCE-only** —
read, never ported. It is therefore **neither an in-scope deliverable nor a DesignSystem
deliverable**, which is not a bookkeeping nicety: it is one of the three boundary attributions that
explain the reconciliation in Section 7.2, and the reason `pfw.ui.controls.ext` reconciles as
4 + 1 + 38 = 43 rather than 5 + 38.

### 6.5 One more idiom the deferred boundary removes

The legacy defers work by posting it to the platform message queue. The clearest in-scope instance
is the kill-focus handler at [`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L387-L393`],
which posts a deferred accept at [`:L389`] only when the item-change re-entrancy flag is clear. A
headless service has no message pump, so the posted call becomes an explicitly queued continuation
on the validation session. Section 8.2 records that as one of the three deliberate non-ports,
because it is a decision rather than a gap.

---

## 7. Reconciliation

This section restates the reconciliation from [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §11
identically. The two documents are cross-checked against each other, so any divergence between them
is a defect rather than a difference of emphasis.

### 7.1 Headline reconciliation

| Category | Objects |
| --- | --- |
| In scope | 103 |
| Deferred — split remainders | 120 |
| Deferred — whole libraries | 205 |
| Permanently out of scope | 116 |
| **Total** | **544** |

**Zero objects are unassigned.** Every one of the 544 objects under `ws_objects/**` falls into
exactly one of the four categories above, and the deferred total is 120 + 205 = **325 objects across
the four deferred services**.

**"Permanently out of scope" is a distinct category from deferred, not a synonym for it.** Those 116
objects are **not scheduled for a later phase**, because there is nothing to migrate. They are the
repository's three application objects, the two contradictory legacy build definitions and the
PowerBuilder packager window — all REFERENCE only, read for the lifecycle and composition-root
behaviour they record — together with the test and demonstration libraries, which are
characterization-fixture source only: never ported as-is, never edited, and load-bearing precisely
*as* the behavioural oracle. The deferred 325, by contrast, are objects whose capability has a named
destination. [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §9 carries the per-library detail and is not
reproduced here.

### 7.2 Accounting note — two defensible apportionments, both totalling 544

A strict one-object-one-category count taken directly over the filesystem apportions the three
non-in-scope categories differently from the headline table. An audit that concealed that would not
be an audit, so it is stated plainly, and it is worded here as it is worded in
[`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) §11.2.

A strict filesystem count yields **103 in scope + 128 split remainders + 195 whole-library
deferred + 118 permanently out = 544**, also with zero objects unassigned. Both apportionments
total 544; the differences are **−8** on split remainders, **+10** on whole-library deferred and
**−2** on permanently out of scope, and they **net to zero**. The in-scope figure of 103 is
identical under both counts, so nothing about the work actually being done in this phase depends on
the choice.

Three boundary attributions account for the whole difference. Each is a genuine judgement about a
single object or library, not a counting error:

1. **`ws_objects/pfwx.pbl.src/pfwx.sra` is attributable two ways.** It is simultaneously a REFERENCE
   object for the deferred `pfwx` target and a permanently-out-of-scope application object. A
   full-estate table that lists the `pfwx` library under both headings produces 40 rows for 39
   libraries, while on disk there is exactly one `ws_objects/pfwx.pbl.src` holding exactly one
   object. It is counted **once, under permanently out of scope**, with its Integration relevance
   noted in prose rather than double-counted.
2. **`ws_objects/pfw.ui.controls.ext.pbl.src/se_cst_datawindow.sru` is REFERENCE-only.** It is read
   as the structural parent of `se_cst_dw` (Section 6.4) but is neither ported nor a DesignSystem
   deliverable, so it falls outside the "38 remaining → DesignSystem" figure while still being one
   of that library's 43 objects. The library reconciles as **4 in scope + 1 REFERENCE + 38
   DesignSystem = 43**.
3. **The `pfw.utility` split is recorded as 1 in-scope + 11 Documents** of a 14-object library,
   while a strict count of the remainder is 13. The two figures differ over the two sibling pinyin
   helpers `getpinyinfirstletter.srf` and `getpinyinfirstletters.srf`, which sit at the boundary of
   the same contributed pinyin behaviour as the in-scope `pinyinfirstletterlike.srf`. The remaining
   11 are unambiguously Documents.

### 7.3 Verified split arithmetic

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
`pfw.ui.controls.ext` 43 = 4 + 1 + 38; `pfw.utility.parser` 13 = 2 + 11; `pfw.utility.container` 3 =
2 + 1; `pfw.utility` 14 = 1 + 13.

Two reading notes, both pointing back to Section 7.2 rather than introducing anything new. The
`pfw.ui.controls.ext` row is the only one with a non-empty REFERENCE column, and that single object
is `se_cst_datawindow.sru` (attribution 2). And the `pfw.utility` row shows the **strict** remainder
of 13, whereas Sections 3.1 and 3.3 record 11 to Documents; the two figures, and the two objects
they differ over, are exactly attribution 3.

---

## 8. The native surface behind the deferred estate

### 8.1 The deferred native population is a documented decision only

The repository holds **148** objects declaring a PowerBuilder Native Interface class binding — 136
to `pfw.dll` and 12 to `pfwx.dll` — and there is no C++ source anywhere in the tree. **The great
majority of them are deferred with their services, and no work of any kind was performed on any of
them**: no substitute was written, no interface was declared, and no decision was made about how any
of them would be reimplemented. Recording where they belong is the entire extent of the treatment.

What makes the Phase-1 slice tractable is the shape of the in-scope remainder, which was measured
per library:

| Group | Objects with a native class binding | Treatment |
| --- | ---: | --- |
| `pfw.shared`, `pfw.datawindow.services`, `pfw.thread`, `pfw.thread.ext` | **0** each | nothing to substitute — pure PowerScript |
| `pfw.common` | 18 | in scope, substituted |
| `pfw.base` | 5, including the deferred `u_logo.sru` | in scope, substituted (less the logo control) |
| `pfw.crypto` | 1 | in scope, substituted |
| `pfw.utility.sqlite` | 2 | in scope, substituted |
| via the library splits | 3 — the ordered map, the vector and the SQL clause parser | in scope, substituted or reimplemented |
| **Deferred libraries, plus `u_logo.sru`** | **120** | **documented decision only — no work performed** |

That is **29** inside the in-scope set, or **28** excluding the deferred `u_logo.sru`, and
148 = 28 + 120. **The four zeros are the decisive figures:** the return-code algebra, the entire
22-event DataWindow chain and the whole SQL task layer are pure PowerScript, so the capabilities
carrying the
most intricate behaviour carry no closed-binary dependency at all and present no native substitution
problem.

The in-scope set is also **graphics-free**. Counting the classic external prototypes declared inside
it — the second and separate native mechanism, bound by library name rather than by class — yields
46 declarations across four libraries: 35 to `kernel32.dll`, 9 to `pfw.dll`, 1 to `user32.dll` (the
drop-down search service's `IsWindowVisible` at
[`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L43`]) and 1 to
`Imm32.dll` (an input-method call at [`ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L41`]) — and
**zero to any graphics library**. The two window and input-method prototypes are presentational and
are covered by the headless/deferred split of Section 6.

> **Measurement note.** The plan this document implements records the deferred native population as
> 142 and the in-scope classic prototypes as 44. A direct count over the filesystem yields **120**
> and **46** respectively; the in-scope class-binding figures of 29 and 28, and the four zeros, are
> identical under both counts. The measured figures are used above because a locator-checkable count
> is the only admissible evidence (Section 2.1), and the difference is recorded rather than absorbed
> for the same reason Section 7.2 discloses its two apportionments. No assignment and no decision in
> this document changes either way.

### 8.2 Three deliberate non-ports — decisions, not gaps

Three legacy mechanisms are not carried across. Each is a **decision with a stated reason**, which
is categorically different from the capability gaps of Section 6: a gap is a capability that exists
and does not ship, whereas these have no target-side counterpart to ship.

| Mechanism | Legacy evidence | Why it is not ported |
| --- | --- | --- |
| Thread-affinity mask | [`ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L39`] declares `SetThreadAffinityMask` against `Kernel32.dll`; it is exposed at [`:L172`] and applied at [`:L484`] and [`:L489`] | No portable equivalent, Windows-only, and it has **no observable behavioural contract** — it is a tuning knob. The target is Linux containers |
| Message-pump processing, and the `Post` idiom it enables | [`ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L50`] declares `ThreadProcessMessage` aliased to the framework binary's pump entry point, used at [`:L343`] beside `Yield()` at [`:L339`]; the in-scope instance is the posted deferred accept at [`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L387-L393`] | A headless service has no message pump. The posted call becomes an explicitly queued continuation on the validation session |
| Thread suspend and resume | [`ws_objects/pfw.thread.pbl.src/n_cst_threading.sru:L41-L42`] declare both, used at [`:L516`], [`:L520`] and [`:L865`]; [`ws_objects/pfw.thread.pbl.src/n_cst_thread.sru:L33`] declares suspend, used at [`:L722`] | Unsupported in .NET. Re-expressed as a cooperative pause on a wait handle |

Dropping the affinity mask is **not** a performance decision: the mask has no observable behavioural
contract, so removing a knob whose effect cannot be observed is not a behavioural change and there
is nothing for a parity comparison to detect.

### 8.3 One genuine parity risk touches the deferred boundary

Pinyin first-letter matching is the single genuine parity risk in the in-scope set, and it sits
exactly on the deferred boundary — which is why it is recorded here rather than left to be
discovered.

Its **behaviour is in scope**, because it is invoked from inside a DataWindow filter expression: the
drop-down search service appends a pinyin clause to the filter it builds at
[`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323`], passing the
searched text and a flag value of `7`. Its **implementation is unavailable**: the lookup table
exists only inside the closed native binary, there is no C++ source anywhere in the repository, and
the meaning of that flag value is documented nowhere in the tree.

Bit-exact parity therefore requires characterizing both the table and the flag behaviour from the
behavioural oracle. **If the oracle cannot be exercised, the pinyin filter is reported as BLOCKED
rather than approximated.** An approximation would return subtly different result sets — a
regression that a characterization comparison would flag but that a unit test would not, and one
that would look like correct behaviour to a reader. [`PARITY.md`](PARITY.md) carries the oracle, the
fixture corpus and this risk.

---

## 9. Capability gating corroborates the boundary

The framework gates its own modules with a capability bitmask, and mapping those bits onto the
destination roster lands them exactly where Sections 3 and 7 already put them. That is independent
corroboration that the Phase-1 slice is drawn along a seam the legacy itself recognised, rather than
along one imposed on it.

[`ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48`] declares eight capability bits, each a `Constant
Long`, and the composite at [`:L49`]. Five of the eight belong to deferred destinations:
`Enums.INIT_FLAG_ENABLE_UI` and `Enums.INIT_FLAG_ENABLE_DPIAWARE` to DesignSystem, and
`Enums.INIT_FLAG_ENABLE_SCITER`, `Enums.INIT_FLAG_ENABLE_BLINK` and `Enums.INIT_FLAG_ENABLE_WEBVIEW`
to ScriptBridge. A sixth, `Enums.INIT_FLAG_ENABLE_BLINKFAST`, is also ScriptBridge, and the
composite deliberately **omits** it because the fast and standard MiniBlink binaries are alternative
builds of one engine. A seventh, `Enums.INIT_FLAG_ENABLE_ORCA`, is legacy packaging tooling and is
permanently out of scope. That leaves **`Enums.INIT_FLAG_ENABLE_SQLITE` as the only bit with an
in-scope consumer.**

Those identifier spellings are reproduced verbatim in SCREAMING_SNAKE wherever this document names
one, contrary to C# naming convention, because these exact strings appear in serialized payloads, in
log records and in characterization recordings, where a rename would silently invalidate every
stored comparison. The per-bit values, locators and destinations are tabulated once, in
[`ARCHITECTURE.md`](ARCHITECTURE.md) §6.1, and are deliberately not duplicated here.

One operational consequence is worth stating because it explains why the deferred majority costs
nothing to leave unbuilt: the legacy documentation records that a module which is not explicitly
initialized is unusable **and that its DLL need not be bundled** — `docs/README.md:L26`, under the
advanced-initialization heading at `:L24`. The legacy's own modularity mechanism therefore already
treats these capability areas as separable, which is exactly the property the decomposition relies
on. That document is read-only reference, cited and not modified (Section 2.2).

---

## 10. Constraint compliance, cross-references, and what this document does not claim

### 10.1 How this document honours the constraints that govern it

No user-specified rules exist (Section 1.4), so the governing constraints are the refactor's own
binding clauses. Each is cited by name, with what this document does to satisfy it.

| Constraint | What it requires of this document | How this document satisfies it |
| --- | --- | --- |
| **C-D** — do not implement the four deferred services, even partially, even to stub them out | Describe the four as **unimplemented**, and state the prohibition in auditable implementation terms rather than as a general intention. Read as a record of scope, never as a plan | Section 1 opens with the prohibition rather than the roster and states that deferral is a boundary rather than a stage. Section 5.2 lists the concrete artifacts that must not exist — service directory, project file, container definition, test project, partial implementation, exception-throwing placeholder class, solution entry, configuration variable, port — and Section 5.3 gives the mechanical pass condition. No section contains a task list, schedule, ordering, effort estimate or design proposal for any of the four |
| **C-K** — document every technology-specific and boundary-specific decision | The three headless/rendering splits must be **deliberate, enumerable capability gaps** rather than silent omissions | Section 6.1 gives the line-level evidence that forced each split and the contrast proving the other two services need none; Section 6.2 tabulates exactly what ships and exactly what does not, so a reader can enumerate the gap by reading one column; Sections 6.3 to 6.5 add the dialog dependency, the inheritance edge and the posted-call idiom. Section 8.2 records the three deliberate non-ports with their reasons |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | The deferred majority is read-only on the same terms as the in-scope minority, and discovery rigor is not relaxed on it. The five pre-existing Chinese documents in this folder must not be edited, translated, re-encoded, renamed or link-rewritten | Section 2.2 states the boundary path by path and records that this document is purely additive; the two Chinese ScriptBridge references are cited and untouched, with the encoding finding recorded as a concrete reason not to normalise them. Section 1.3 records that all 39 libraries and all 544 objects are assigned with justification. Every legacy path in this document is cited as evidence; nothing instructs a modification of any of them |
| **C-B** — no new features, no behaviour improvements, no performance objective | Apply the principle that a half-built deferred service is worse than a documented gap, and assert no service-level figure | Section 1.1 states the principle and Section 6.2 is where it is applied — the data half ships and the rendering half is named as an enumerable gap rather than approximated. Section 6.3 preserves the non-localized column-expression messages as a defect rather than harmonizing them. No latency figure, throughput target, availability commitment or service-level agreement appears anywhere, because the repository publishes none and there is nothing from which to infer one; the only quantitative non-functional requirement in the brief is the per-service coverage gate, which is [`BUILD.md`](BUILD.md)'s subject |

The enterprise-standard baseline that applies in the rules' place (Section 1.4) is met by the
evidence discipline of Section 2.1, the measured counts of Sections 3 and 8, and the two disclosures
where the arithmetic admits more than one defensible reading — Section 7.2 and the measurement note
in Section 8.1 — each of which states the difference rather than picking a figure silently.

### 10.2 Cross-references

This document is the detail view of the deferred estate. It deliberately does not duplicate its
siblings:

| For | See |
| --- | --- |
| The full-estate library-to-destination assignment for all 39 libraries and all 544 objects, the per-library detail, and the three citation anomalies | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| Service topology, transport choice per service, the port map and the reserved 5103 slot, the capability bit table, and the architectural statement that the four routes are metadata | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| The cross-service contract inventory, the four reserved Gateway extension points, and the machine-readable body the reserved routes return | [`CONTRACTS.md`](CONTRACTS.md) |
| Secret locators, severities and required actions — the single register, and the reason none is restated here | [`SECRETS.md`](SECRETS.md) |
| The characterization model, the fixture corpus, the determinism seams, and the pinyin parity risk | [`PARITY.md`](PARITY.md) |
| Build and test commands, the solution layout, per-service build independence, and the coverage gate | [`BUILD.md`](BUILD.md) |
| The legacy JavaScript-to-PowerScript bridges for the ScriptBridge capability area — read-only reference, never edited | [`Blink交互.md`](Blink交互.md), [`Sciter交互.md`](Sciter交互.md) |

### 10.3 What this document does not claim

- **It does not claim any of the four deferred services exists in any form.** It claims the
  opposite, and Section 5.3 gives the mechanical test by which the claim can be falsified.
- **It does not schedule, sequence, size or design any of the four.** Naming a destination and the
  capabilities it will eventually own is the whole of what it does.
- **It does not claim the container bring-up was verified.** Container and orchestration correctness
  is asserted by definition review plus continuous integration; [`ARCHITECTURE.md`](ARCHITECTURE.md)
  §10.6 records what was and was not exercised.
- **It does not reproduce any credential, key, certificate or secret value**, and it does not
  restate secret locators; Section 4.5 records only the corrected attribution about which library
  carries them, and [`SECRETS.md`](SECRETS.md) is the register.
- **It asserts no service-level objective**, because the repository publishes none.
- **It does not treat any legacy path as editable**, including the roughly 81% of the estate that is
  not implemented in this phase and including all five pre-existing Chinese documents in this
  folder, three of which it cites.
