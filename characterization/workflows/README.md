<!-- Markdown lint policy for this file, matching the directive every document this refactor authors
     carries. The rationale in full is docs/BUILD.md section 14, which is the authority; this is only its
     application here. MD013 is 120 rather than the 80-character default, and is disabled for tables and
     code blocks: a roster row carrying an identifier, a target service and its oracle locators cannot be
     wrapped without splitting a locator away from the row that relies on it, and a wrapped command is a
     command that does not run. Prose IS wrapped, and is held to the 120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It cannot reach the five read-only legacy Chinese documents under docs/, which are part of the
     behavioural oracle, are never edited, and carry pre-existing violations of their own that this
     refactor must not act on.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e,
     and no Markdown linter appears in it. A documented on-demand package-runner invocation would instruct
     an unpinned version to be resolved and executed from the network outside that lockfile every time
     somebody followed this document. Lint with tooling that is already installed; the directive below is
     what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# characterization/workflows

This directory holds the **workflow definitions** for the paired golden-master capture store, each one
carrying **its own determinism mask inline**. One definition per workflow, one file per definition, and the
mask lives in the same file as the workflow it applies to — so a reviewer sees the behaviour under
characterization and the values excluded from its comparison in one place, and a mask cannot drift away
from the workflow that gave it meaning. [`workflow.schema.json`](workflow.schema.json) is the
machine-checkable shape every definition validates against.

The load-bearing content of this document is the [workflow roster](#workflow-roster). Its `workflowId`
column publishes the **fifteen pairing keys** that name the per-workflow directories on *both* sides of
`characterization/recordings/`. **Both recording roots exist and hold nothing but their own readmes — not one
per-workflow directory has been created on either side**, so this table is the only thing in the repository
that can name them: a directory under `characterization/recordings/legacy/` or
`characterization/recordings/dotnet/` whose name is not in that column has no definition behind it and no
counterpart to be compared with.

## Scope and precedence

This document is deliberately narrow. It is the **authoring contract and roster index for this folder**,
and nothing more — every store-wide rule belongs to a document that already owns it, and is **cited here
rather than restated**. The reason is the one this store exists to defend against: a rule repeated in two
places is a rule that will eventually disagree with itself, and a reader who finds two versions of it has
no way to tell which one is stale.

| For | Go to | Which owns |
| --- | --- | --- |
| The paired-capture shared-volume rule — **canonical text** | [`../README.md`](../README.md) §2 | The rule word for word, its provenance, the teardown corollary and the one exemption. It is **the authority**, and it is quoted verbatim in exactly three documents — that file, [`../../docs/PARITY.md`](../../docs/PARITY.md) and [`../../orchestration/README.md`](../../orchestration/README.md). This file adds no fourth copy |
| The store's directory contract, the pairing-key semantics, the recording-file conventions, the placeholder trap and the newline-normalization reasoning | [`../README.md`](../README.md) §3 | Everything about the shape of the store and what may be written into it |
| The oracle corpus, the primary fixture and the fixture-to-capability map | [`../README.md`](../README.md) §4 | The inventory of legacy windows and DataWindow definitions, with verified counts |
| The parity model: the technique, the determinism-seam register, the test shape, the catalogue of behaviours to reproduce, the coverage-gate mechanics and the known risks | [`../../docs/PARITY.md`](../../docs/PARITY.md) | The model itself. Where this file and that one could disagree, **that one wins** |
| Secret locators, their severities and the required action for each | [`../../docs/SECRETS.md`](../../docs/SECRETS.md) | The full inventory, and the record of which treatment a credential-bearing field takes. **Cite it; reproduce no value** |
| The four deferred capability areas and the legacy objects assigned to each | [`../../docs/DEFERRED.md`](../../docs/DEFERRED.md) | The destination mapping and the reserved-route metadata |
| The cross-service contracts C-01 through C-10 | [`../../docs/CONTRACTS.md`](../../docs/CONTRACTS.md) | Their definitions and method surfaces. Identifiers may be cited here; the contracts are not described here |
| Build and test commands, and where `coverage.cobertura.xml` lands | [`../../docs/BUILD.md`](../../docs/BUILD.md) | The commands, the report path and the lint policy this file's head declares |

**Consequently, do not add any of the above to this file.** If you find yourself explaining the capture
rule, the layout of the store or the parity model here, the explanation belongs upstream and a link belongs
here.

What this document *does* own, in order:

- [Workflow roster](#workflow-roster)
- [Authoring a definition](#authoring-a-definition)
- [Constraints that bind this folder](#constraints-that-bind-this-folder)
- [Correction register](#correction-register)
- [Out of scope: the four deferred services](#out-of-scope-the-four-deferred-services)
- [No timing assertions](#no-timing-assertions)
- [File conventions](#file-conventions)
- [User-specified rules](#user-specified-rules)
- [Honest limitation](#honest-limitation)

---

## Workflow roster

Fifteen workflows. The `workflowId` column is the pairing key; the Oracle fixtures column names the
read-only legacy objects each definition is captured from, by full repository-root-relative locator.

| `workflowId` | Target service | Capability characterized | Oracle fixtures | Shared volume |
| --- | --- | --- | --- | --- |
| `persistence-sqlite-retrieve-update` | Persistence | The retrieve, validate and update triple against the only updatable DataWindow | `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw`, `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` | Applies |
| `persistence-thread-sqlquery-chunked` | Persistence | Asynchronous chunked retrieval and cross-thread buffer transfer | `ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw`, with `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` as the service object | Applies |
| `persistence-sqlparser-clause-model` | Persistence | The six-clause `SELECT` model and byte-exact clause output | `ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw`, with `ws_objects/pfw.utility.parser.pbl.src/n_sql.sru` as the service object | Applies |
| `persistence-sql-paging-rewrite` | Persistence | SQL Server and Oracle paging rewrites as pure string transforms | No fixture window exists; `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` is read as the specification | **Exempt** |
| `persistence-thread-task-lifecycle` | Persistence | Thread and task lifecycle event ordering, and the three deliberate non-ports | `ws_objects/pfw.tests.pbl.src/w_test_thread.srw` | Applies |
| `dataservices-dw-event-chain` | DataServices | The 22-event chain and the item-change micro-protocol | `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru`, `ws_objects/pfw.tests.pbl.src/dw_test_dwsvc.srd` | Applies |
| `dataservices-dwsvc-rowselect` | DataServices | The row-selection state machine | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru`, `ws_objects/pfw.tests.pbl.src/dw_svc_sample.srd` | Applies |
| `dataservices-dwsvc-columnexp` | DataServices | Static versus dynamic expansion, macro invocation and the expression trace | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru`, `ws_objects/pfw.tests.pbl.src/dw_test_dwsvc_columnexp.srd` | Applies |
| `dataservices-dwsvc-columnsort` | DataServices | Headless sort-expression construction and sort state | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnsort.srw`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnsort.sru`, `ws_objects/pfw.tests.pbl.src/dw_svc_sample.srd` | Applies |
| `dataservices-dwsvc-contextmenu` | DataServices | The headless menu item model, and the column-value ordered map | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_contextmenu.srw`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru`, `ws_objects/pfw.tests.pbl.src/dw_svc_sample_contextmenu.srd` | Applies |
| `dataservices-dwsvc-dropdownsearch` | DataServices | Filter-expression construction and the search state machine | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw`, `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru`, `ws_objects/pfw.tests.pbl.src/dw_svc_sample.srd` | Applies |
| `shared-diagnostics-assert-payload` | Shared.Diagnostics | The assertion payload protocol and the fail-fast posture | `ws_objects/pfw.tests.pbl.src/w_test_assert.srw`, `ws_objects/pfw.pbl.src/pfw.sra` | Applies |
| `shared-eventful-broker-ordering` | Shared.Eventful | Subscription ordering, the decomposed topic identity and the tri-valued veto | `ws_objects/pfw.tests.pbl.src/w_test_eventful.srw`, with `ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru` as the service object | Applies |
| `gateway-lifecycle-composition-root` | Gateway | The initialize and finalize pairing, the locale default and the system-error protocol | `ws_objects/pfw.pbl.src/pfw.sra`, `ws_objects/pfw.demos.pbl.src/w_demo_selector.srw` | Applies |
| `security-crypto-surface` | Security | The cryptographic surface with its annotated legacy defaults | `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru` | Applies |

**These fifteen identifiers are the pairing keys, and they are the only source of the directory names on
either side.** `characterization/recordings/legacy/<workflowId>/` and
`characterization/recordings/dotnet/<workflowId>/` take their `<workflowId>` segment from the first column
of the table above and from nowhere else. Three consequences follow, and each is a rule rather than an
observation:

- **An identifier is never renamed once a recording exists under it.** A rename orphans both halves of
  every pair already captured under the old name, and nothing in the tooling reports that it happened.
- **A roster row and a sibling definition file exist together or not at all.** Each row has exactly one
  `<workflowId>.yaml` beside this document, and every such file has exactly one row here — a row without a
  definition is a promise nothing keeps, and a definition without a row is invisible to review.
- **A recording directory whose name is not in that column is not a partial comparison.** It has no
  definition, no mask and no counterpart, so it is not a comparison at all and must never be reported as a
  pass. [`../README.md`](../README.md) §3.2 is the authority for that rule.

### Row 1 is the golden master

`persistence-sqlite-retrieve-update` is the workflow the rest of the store is calibrated against, because
its fixture is the only one of its kind. `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` is **the only
updatable DataWindow among the repository's twelve**, and its table specification sets `updatewhere=1`
while all six of its columns are marked `updatewhereclause=yes`. The optimistic-concurrency check therefore
spans **every marked column's original value**, which fixes what a recording of this workflow has to hold:

> **Both the current and the original value of every marked column, per row.** A flat rowset capture is
> insufficient — it silently discards the state the concurrency contract is built on.

[`../README.md`](../README.md) §4.2 and §4.3 carry the fixture's column table and this requirement in full,
and [`../../docs/PARITY.md`](../../docs/PARITY.md) §3.2 is the authoritative description of the fixture.

### Where a row cites a service object as well as a window

Several rows name a `n_cst_dwsvc_*` service object alongside the `w_test_dwsvc_*` window that hosts it, and
that pairing is deliberate rather than decorative. The host windows are **thin** — 53 to 101 lines each,
enough to instantiate a `se_cst_dw` control, switch a service on and set a style — while the service objects
they exercise run from roughly 8 KB to 96 KB of PowerScript. Capturing the window alone would characterize
the switch, not the behaviour behind it, so a definition cites both and gives each its own role.

Two rows have no host window at all, and say so rather than borrowing one:

- **`dataservices-dw-event-chain`** is captured from `ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru`
  itself, whose `:L11-L32` declares all 22 events — nine semantic and thirteen raw. No window in the corpus
  drives the bare chain; the five service windows each reach it through a `se_cst_dw`-derived control, which
  is why the chain is characterized from the service-extension object and its release-19 fixture.
- **`persistence-sql-paging-rewrite`** has no oracle window because the behaviour is a pure string
  transform, and `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` is read as its
  specification instead.

One fixture-family hazard, because the two families are not interchangeable: the `dw_test_dwsvc*`
definitions declare `release 19` while `dw_svc_sample_columnexp.srd`, `dw_svc_sample_contextmenu.srd` and
`dw_svc_sample_dddw.srd` declare `release 12.5` — and `dw_svc_sample.srd` itself is `release 19` despite the
shared prefix. The roster names the family each workflow actually drives; substituting one for the other
changes the fixture and voids the comparison.

### The one exemption

`persistence-sql-paging-rewrite` is the **only** row exempt from the shared-volume rule, and the exemption
is narrow: the SQL Server and Oracle paging behaviours take a statement plus a page size and index and
return statement text, so they read and write nothing, provision no database instance and mount no volume.
There is no volume state to keep still. [`../README.md`](../README.md) §2.7 is the authority for the
exemption and for why it is not a loophole.

**The exemption is declared in the definition itself, never left implicit.** That row's
`sharedVolume.applies` is `false` and its `sharedVolume.exemptionReason` states the reason — the schema
makes the reason mandatory whenever `applies` is `false`, precisely so that an exemption cannot be inferred
from silence. Every other row declares `sharedVolume.applies: true` and is governed by
[`../README.md`](../README.md) §2 in full.

---

## Authoring a definition

**One file per workflow, named `<workflowId>.yaml`**, so the pairing key is visible in the path and a
definition can be found from a recording directory name without opening anything. The fifteen files beside
this document are exactly the fifteen rows of the roster.

Every definition opens with the language-server directive on its first line, so an editor validates it while
it is being written rather than in CI afterwards:

```yaml
# yaml-language-server: $schema=./workflow.schema.json
schemaVersion: "1"
workflowId: persistence-sqlite-retrieve-update
```

[`workflow.schema.json`](workflow.schema.json) is the authority for shape. It is a JSON Schema 2020-12
document with `additionalProperties: false` at every level, so a misspelled member is a validation error
rather than a silently ignored one.

### The twelve required declarations

Each is described here in one line, for orientation only. **The schema states the constraints and this file
does not restate them** — read the member's own `description` in
[`workflow.schema.json`](workflow.schema.json) before authoring it.

| Declaration | What it is for |
| --- | --- |
| `schemaVersion` | The definition format this document is written against, so an older shape fails loudly instead of validating by accident |
| `workflowId` | The pairing key from the roster's first column, and the name of the two recording directories |
| `title` | One line, for review and report output; narrative belongs in the optional `description` |
| `capabilityArea` | The capability under characterization, in the vocabulary [`../../docs/PARITY.md`](../../docs/PARITY.md) owns |
| `targetService` | The in-scope service or shared library that produces the **candidate** half of the pair |
| `oracleFixtures` | The read-only legacy objects the pair is captured from, each with its role and optionally the lines relied on |
| `seedPhase` | Everything that establishes the starting state, declared apart from the capture so it can run **once** |
| `capturePhase` | The behaviour under characterization and the outputs recorded from it — this phase runs **twice** |
| `determinismMask` | The mask, inline: every non-deterministic value that reaches a recorded output, how it is neutralised, and why |
| `provesDefects` | The legacy behaviours this pair exists to prove **survived** |
| `sharedVolume` | Whether the shared-volume capture rule governs this workflow, with a mandatory reason when it does not |
| `executionStatus` | The honest state of the workflow. One legal value, and [Honest limitation](#honest-limitation) is why |

Four members are optional and are used where they apply: `description` for prose a reviewer needs;
`blockedBehaviours` for a behaviour the pair cannot prove, declared rather than approximated;
`deferredCapabilities` for the half of an in-scope capability that does not ship, so the gap is enumerable;
and `relatedWorkflows` for a sibling identifier that seeds the same fixture or characterizes the adjacent
half of one behaviour.

### The four hard authoring rules

These are not schema notes. They are the rules a definition can satisfy the schema and still break, so they
are stated where an author will read them.

1. **The mask is inline and is never delegated.** Write every seam this workflow needs into its own
   `determinismMask`. Citing the seam register in [`../../docs/PARITY.md`](../../docs/PARITY.md) §5.1 as
   *provenance* is correct and encouraged; pointing at it *instead of* declaring the entry is not. The schema
   offers no reference, path or see-also member by which a mask could be delegated, and that omission is
   deliberate: a mask that drifts away from its workflow silently invalidates every pair captured under it.
2. **The seed phase is separate, and never runs between the two captures.** It runs once, before the pair
   begins. The concrete driver is the oracle's own setup: the `CONNECT` handler of
   `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` calls `FileDelete("test.db")` at `:L450` and then issues
   `CREATE TABLE IF NOT EXISTS COMPANY(...)` at `:L463-L469`, bracketed by autocommit toggles at `:L461` and
   `:L473`. Re-running that handler to "start clean" is exactly the reseed the capture rule forbids between
   the two halves of a pair. [`../README.md`](../README.md) §4.4 carries the fuller rationale.
3. **Every mask entry applies to both sides and carries its reason.** `appliesTo` lists `legacy` *and*
   `dotnet` — the schema requires both members rather than trusting the author to remember, because masking
   one side leaves the other holding a value that can never be matched. The `reason` is required for the
   same kind of motive: a bare list of masked values is folklore, and the next reader cannot tell a necessary
   mask from a convenient one.
4. **`provesDefects` is never empty.** The schema enforces it with `minItems: 1`, so an omitted or empty list
   is a validation failure rather than an incomplete document — a workflow that proves nothing has no reason
   to exist. Each entry names the behaviour and the legacy locator that shows it is really there.

---

## Constraints that bind this folder

No user-specified rules exist ([User-specified rules](#user-specified-rules)), so the binding constraints
come from the migration plan's own inventory. These are the ones that govern **this folder**, each with what
it requires of a definition authored here.

### C-B — behaviour is preserved, never improved

Every workflow names the defect it proves. A definition whose `provesDefects` list is absent or empty is
invalid, and the schema rejects it with `minItems: 1` rather than leaving it to review.

> **A capture that shows corrected behaviour is a parity failure, not an improvement.**

Where the target produces output that looks *more correct* than the oracle, the finding is a divergence in
the target and is filed as a defect. A legacy defect is reproduced and annotated at its point of
reproduction, never repaired. [`../README.md`](../README.md) §1.3 and §5.3 state the inversion and the
roster of behaviours a pair is expected to demonstrate.

### C-C — the legacy tree is read-only, and it is the oracle

A definition cites every fixture by **full repository-root-relative locator** into `ws_objects/**`, so a
citation resolves to exactly one file from the repository root on any platform.

**The `path` pattern is a containment control, and it rejects every form that could name a file outside the
checkout**: a POSIX-absolute path, a UNC path, any leading dot including a `./` prefix, any `..` segment
anywhere, a backslash, whitespace, a leading `~` a shell would expand to a home directory, and **any colon**
— which is what rules out the Windows drive-absolute and drive-relative forms `C:/Windows/System32/…` and
`C:…`, and the NTFS alternate-data-stream syntax `file.srd:stream`. An earlier revision of the pattern
rejected the POSIX forms and **accepted the drive-absolute one**, which read as protection while providing
none on the platform the oracle itself runs on. A colon has no legitimate use in any path in this
repository, so the exclusion costs nothing; **non-ASCII characters are legitimate and are accepted**,
because one cited specification is `docs/PB多线程绕坑提示.md`.

> **A pattern cannot see the filesystem, so a consumer owes two further steps and neither is optional.**
> Resolve the value against the repository root, **canonicalize** the result — following symbolic links,
> because a link inside the checkout can point anywhere — and then **verify the canonical path is still
> beneath the canonical repository root**, refusing it with a named error when it is not. No consumer may
> substitute a string check on the raw value for those two steps: the pattern narrows what can be written
> down, and only canonicalization can decide what a written path actually reaches.

No fixture is edited, moved, renamed, reformatted or re-encoded, and **no copy of one is vendored into this
tree**: a vendored copy is a second master that can drift from the oracle without anybody noticing. The
schema has no member that can hold fixture content, and that omission is deliberate. **This document
likewise contains no fixture content** beyond identifier names and line locators.

### C-D — nothing for the four deferred capability areas

The prohibition is carried in full by the [refusal register](#out-of-scope-the-four-deferred-services) below.
It is worth stating here that the constraint is **enforced, not merely asserted**: the schema's
`targetService` enum is **closed**, and the four deferred names are absent from it, so a definition that
names one of them fails validation. There is no permissive string type to work around, which is the point —
a pair needs a candidate as well as a master, and those four areas have no candidate.

### C-E — SQLite only, and no fabricated database

Exactly one workflow is exempt from the shared-volume rule, and [the one exemption](#the-one-exemption)
above records it: `persistence-sql-paging-rewrite` characterizes the SQL Server and Oracle paging behaviours
as **pure string transforms**, with no instance of either dialect provisioned, required or reachable. The
exemption is declared in that definition's own `sharedVolume` member with its reason, never left implicit.

### C-F — no secret value in a definition, in a mask or in a recording

**No definition may contain key material, a certificate, a password, a token or a fragment of one.**
Definitions carry locators, never values. This is a live risk rather than a theoretical one, because three
of the fixtures this store draws on are themselves hardcoded-secret sites:

| Secret-bearing fixture | Standing in this folder |
| --- | --- |
| `ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw` | Excluded twice over — it belongs to a deferred capability area **and** is on the refusal register |
| `ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru` | Out of capability scope; no workflow cites it |
| `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru` | **In the roster**, as the oracle for `security-crypto-surface`. Drive it with injected test key material only, and never echo, capture or store the key material embedded in it |

Three field-level rules follow, and a definition declares each in its `capturePhase.redaction` member so a
reviewer can see the field was handled deliberately rather than overlooked:

- **The transaction descriptor's password field is write-only.** It is accepted inbound and never populated
  outbound — never echoed in a response, never logged, and therefore **never recorded**. The legacy in-process
  accessor moves it in both directions; across a boundary the outbound half would place a database password
  into a response body and into any log of one.
- **The connection URI is a credential wherever it carries one.** The grammar is recorded; the assembled URI
  is not. The schema's `passwordSupported` member is a boolean for exactly this reason — it records that the
  parameter position exists, never what sits in it.
- **The database error payload's statement field is recorded redacted.** The legacy fills it with the
  complete generated statement including interpolated literal values, and the legacy logger performs no
  redaction at all.

[`../../docs/SECRETS.md`](../../docs/SECRETS.md) is the locator inventory and **the authority for which
treatment a specific field takes** — its §6 records the statement-field decision, and its §5 the
credential-bearing fields on the new boundaries. Cite it; reproduce no value in any form, not even an
obviously expired one. The generalisation, so that a field not yet enumerated is still governed: **any field
whose value is a credential, key material, or a statement carrying interpolated literals is write-only
inbound and redacted outbound, and is therefore never recorded.**

### C-K — a documented decision carries its reason

Every entry in the [correction register](#correction-register) and the
[refusal register](#out-of-scope-the-four-deferred-services) states its reason **and** its evidence locator.
A bare list is not acceptable here: without the reason, the next reader cannot tell a considered exclusion
from an oversight, and will eventually undo it.

### C-L — the capture rule is the environment's binding instruction

Definitions are captured under the paired-capture shared-volume rule whose **canonical text lives in
[`../README.md`](../README.md) §2**. That file is the authority. It is quoted word for word there, in
[`../../docs/PARITY.md`](../../docs/PARITY.md) and in
[`../../orchestration/README.md`](../../orchestration/README.md), and **this document deliberately adds no
fourth copy** — a fourth copy would be a fourth thing to drift, which is the failure this store exists to
prevent rather than to commit.

---

## Correction register

Corrections to the roster as it was first framed, recorded with the evidence that produced each one. They are
written down because a silently dropped candidate is indistinguishable from an oversight, and the next reader
would reasonably try to add it back.

### The static-map window carries no workflow

The window's name suggests the ordered-map container, and it is not that. Read in full — it is 82 lines —
`ws_objects/pfw.tests.pbl.src/w_test_static_map.srw` creates an `n_httpclient` at `:L55` and `:L58`, issues an
HTTP `GET` against a public third-party static-map endpoint across `:L55-L66`, and calls `SetPicture` into a
`picture` control at `:L68`, sizing the request from that control through the `U2PX` and `U2PY` conversions on
the same line. The control itself is declared `from picture` at `:L7` and `:L74`.

Its dependencies are therefore **outbound HTTP** and **a picture control plus DPI conversion** — capability
areas that are both deferred — and it reaches a **live third-party endpoint**, which destroys the
repeatability the golden-master technique requires as its one hard prerequisite. C-D forbids a workflow for
it on the first ground alone; the second would rule it out even if the capability areas shipped. **"static
map" names a static map *image*, not the ordered-map container.**

### There is no ordered-map oracle at all

Searching for `n_map` and `n_vector` across **every** file in `ws_objects/pfw.tests.pbl.src/` and
`ws_objects/pfw.demos.pbl.src/` returns nothing. Neither container is exercised anywhere in the fixture
corpus, so no workflow can be captured for either.

The ordered map's only in-scope exercise is inside the DataWindow service layer:
`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc.sru` declares `_of_getcolumnvaluemap` twice — by
column name at `:L74` and by column number at `:L75` — with the body at `:L561`, the `n_map` declared at
`:L583` and created at `:L585`, and the by-number overload delegating to the by-name one at `:L666`. The
second site is `ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L948`.

Both sites sit under the context-menu service, so the container's insertion-order and positional-`get`
contract is characterized **inside `dataservices-dwsvc-contextmenu`**, which is why that row cites
`n_cst_dwsvc.sru` alongside the context-menu service object. Its pure contract — independent of any
DataWindow — is unit-test matter in `PowerFramework.Shared.Containers.Tests`, on exactly the same ground that
the return-code algebra and the localization providers are unit-tested rather than characterized: a behaviour
with no oracle recording cannot be paired, and a unit test that pins a pure function is not weakened by the
absence of one.

### The transaction pool has no oracle window either

`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru` is not exercised by any window in either
fixture library — a search for `trans_pool` across both returns nothing — so **no workflow is named after
it.** Its CPU-clock-based idle-expiry seam is instead masked inside **`persistence-thread-sqlquery-chunked`**,
which is the workflow that carries the transaction descriptor: the descriptor's own call site, `logpass`
included in its parameter list, is at `ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw:L247`. That
call is commented out in the fixture, so the descriptor is supplied by the workflow's seed phase rather than
by the oracle window — which is precisely why the seam has to be declared in a mask rather than assumed to be
driven for us.

`ws_objects/pfw.tests.pbl.src/w_test_thread.srw` is the threading-substrate oracle in its own right: it
declares the six lifecycle events at `:L29-L34` and handles them at `:L52-L78`, and it drives the three
deliberate non-ports — thread affinity at `:L127`, resume at `:L160` and suspend at `:L182`. Hence
`persistence-thread-task-lifecycle`, named after the substrate rather than after the pool.

### The pinyin hook's identifier constant diverges from the roster, and the roster is canonical

`services/dataservices-service/PowerFramework.DataServices.Tests/PinyinFirstLetterMatcherTests.cs:L2853`
pins `WorkflowId` to the oracle window's own name, `w_test_dwsvc_dropdownsearch`, and derives its two
recording directory paths from it at `:L2856` and `:L2859`. The roster's identifier for the same workflow is
`dataservices-dwsvc-dropdownsearch`.

**The roster identifier is the canonical one**, for a mechanical reason rather than a stylistic one: the
schema's `workflowId` pattern admits lower-case alphanumeric segments separated by single hyphens and
therefore **rejects underscores**, so the constant's spelling could not be written into a definition at all.
The schema's own derivation rule — the capability area followed by the oracle window's name with its
underscores rendered as hyphens — produces exactly the roster spelling, which keeps the identifier traceable
to its oracle while conforming to the grammar.

Nothing is broken by the divergence today, and that is worth being precise about rather than reassuring
about. The hook is a **conditionally skipped** matrix whose activation predicate requires an ordinary file
under `characterization/recordings/legacy/<workflowId>/`, and no such directory exists on either side — both
recording roots hold nothing but their own readmes — so the constant currently resolves to a path nothing
reads. **The constant is reconciled to the roster
identifier in the same reviewed change that lands the first legacy recording** — the change that would
activate the matrix is the change that must agree with the roster, and reconciling it earlier would edit a
skipped assertion without a recording to verify the edit against.

### Two oracle coverage gaps, recorded so they are not mistaken for omissions

Neither gap is a choice made here; both are limits of what the fixture corpus actually exercises. They are
listed because a reader comparing the roster against the behaviours to be preserved will notice the shortfall
and should find it already accounted for.

| Gap | Evidence | Consequence |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw` never exercises the **prepend** clause-modification style | The window reaches the modification styles at `:L380` (replace) and `:L399` (append). The prepend constant appears nowhere in `ws_objects/pfw.tests.pbl.src/` or `ws_objects/pfw.demos.pbl.src/` | `persistence-sqlparser-clause-model` can pair the two styles the oracle drives. The third has no master available from this corpus, so it is pinned by unit test rather than claimed as characterized |
| `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru` never reaches the **ECB default** | All six of its symmetric calls pass the CBC mode explicitly — at `:L504`, `:L547`, `:L592`, `:L607`, `:L622` and `:L637` — and the ECB constant appears nowhere in the object | `security-crypto-surface` cannot pair the mode-omitting overloads, even though ECB **must remain** the preserved default. The default is held by annotated unit test; the fixture proves the explicit-mode path only |

---

## Out of scope: the four deferred services

**No workflow, no determinism mask, no partial definition and no placeholder exists for DesignSystem,
Documents, Integration or ScriptBridge.** Not a stub, not an empty file reserving a name, and not a definition
with its members left blank. Those four capability areas receive no code, no test and no container in this
phase, so they have no candidate — and a pair needs a candidate as well as a master.

**The constraint is enforced rather than asserted.** [`workflow.schema.json`](workflow.schema.json)'s
`targetService` enum is **closed**, and the four deferred names are absent from it, which makes a deferred
target **structurally unrepresentable**: a definition that names one of them cannot be written down without
failing validation, so the boundary holds as a validation error rather than as a review comment. There is no
permissive string type to work around. The four names appear in the schema in one place only —
`deferredCapabilities.deferredTo`, which documents where a gap will eventually be filled and cannot claim that
it has been.

The fixtures below must therefore **never acquire a workflow**. Naming them is what makes the boundary
enforceable instead of aspirational: the alternative is a reader who finds an unused window, assumes it was
missed, and adds the one thing the constraint forbids. All are under `ws_objects/pfw.tests.pbl.src/`.

| Capability the window exercises | Why no workflow exists for it | Windows |
| --- | --- | --- |
| Outbound transports — HTTP, FTP, WebSocket and MQTT | The capability ships no code in this phase, so there is no candidate half to compare a master against. `w_test_static_map.srw` is additionally disqualified on its own evidence — see [The static-map window carries no workflow](#the-static-map-window-carries-no-workflow) — and `w_test_websocket_mqtt.srw` is a hardcoded-secret site, so capturing from it would risk a value reaching a recording | `w_test_websocket_mqtt.srw`, `w_test_websocket.srw`, `w_test_ftpclient.srw`, `w_test_static_map.srw` |
| Document and utility formats — JSON, XML, archives, barcode and QR, regular expressions, logging, file scanning and device information | No candidate ships. The one place an in-scope capability touched this area was resolved by substituting a base-class library facility instead, so no in-scope dependency on it remains and there is nothing for a pair to prove | `w_test_json.srw`, `w_test_xml.srw`, `w_test_zip.srw`, `w_test_barcode.srw`, `w_test_qrcode.srw`, `w_test_regex.srw`, `w_test_logger.srw`, `w_test_filescanner.srw`, `w_test_devinfo.srw` |
| Embedded engines, runtime compilation and dynamic invocation | No candidate ships. These windows drive a browser or scripting engine loaded from a native binary that the target does not host at all, so there is no target-side behaviour to record | `w_test_sciter.srw`, `w_test_sciter_dropdowncalendar.srw`, `w_test_sciter_vm.srw`, `w_test_sciter_wnd.srw`, `w_test_blink.srw`, `w_test_blink_wnd.srw`, `w_test_webview.srw`, `w_test_compiler.srw`, `w_test_invoker.srw` |
| Visual controls, theming and DPI-dependent presentation | No candidate ships, and no presentation surface is created in this phase. Where in-scope logic genuinely touched presentation the split was made explicit — the headless half ships and the rendering half is a named gap — so nothing here is silently dropped | `w_test_graphic.srw`, `w_test_iconfont.srw`, `w_test_newtheme.srw`, `w_test_progressbar.srw`, `w_test_ribbonbar.srw`, `w_test_splitcontainer.srw`, `w_test_splitcontainer_complex.srw`, `w_test_splitcontainer_template.srw`, `w_test_statictext.srw`, `w_test_svg.srw`, `w_test_trayicon.srw`, `w_test_camera_capture.srw` |
| Application configuration and command-line arguments — **out of capability scope rather than deferred** | These two drive `n_cst_appconfig` and `n_cst_appargs`, objects that live inside the read-only test library itself rather than in any of the 39 framework libraries, so they belong to no in-scope service and to no deferred one either. `ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru:L22-L23` is additionally a hardcoded-secret site | `w_test_appconfig.srw`, `w_test_appargs.srw` |

**The arithmetic reconciles, which is what makes the register auditable.** The fixture library holds 47
`w_test_*.srw` windows. Eleven are in-scope oracles cited by the roster — `w_test_sqlite.srw`,
`w_test_thread_sqlquery.srw`, `w_test_sqlparser.srw`, `w_test_thread.srw`, `w_test_dwsvc_rowselect.srw`,
`w_test_dwsvc_columnexp.srw`, `w_test_dwsvc_columnsort.srw`, `w_test_dwsvc_contextmenu.srw`,
`w_test_dwsvc_dropdownsearch.srw`, `w_test_assert.srw` and `w_test_eventful.srw`. The table above names the
other **36**. Eleven plus thirty-six is forty-seven, so **no window is unaccounted for**: every one is either
an oracle with a workflow or a fixture explicitly refused one.

Two further exclusions belong on the record. Two `.srd` definitions —
`ws_objects/pfw.tests.pbl.src/dw_barcode.srd` and `ws_objects/pfw.tests.pbl.src/dw_qrcode.srd` — belong to a
deferred capability area and appear in no roster row. And `ws_objects/pfw.demos.pbl.src/` supplies exactly two
in-scope oracles, `w_demo_selector.srw` and `u_cst_tabpage_utility_crypto.sru`; nothing else in that library
is cited.

[`../../docs/DEFERRED.md`](../../docs/DEFERRED.md) carries the four destinations, the legacy objects assigned
to each and the reserved-route metadata. **That assignment is not restated here**, and this register makes no
claim about which deferred service a given window's capability will eventually land in.

---

## No timing assertions

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment anywhere, so **no performance objective may be asserted and no timing value may be
recorded as an assertion.**

> **Characterization compares observable outputs. It never compares execution time.**

The only quantitative non-functional requirement in the whole brief is the **80% per-service line-coverage
gate**, and it is **not measured from this store at all** — it is measured in CI from
`coverage.cobertura.xml`, per service. [`../../docs/BUILD.md`](../../docs/BUILD.md) is the authority for the
command and the report path.

**The case this bites on is real, not hypothetical.**
`ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw` takes a `CPU()` reading at `:L224` and again at `:L726`,
and prints the difference as a `Time: <n> ms` field in its own displayed output at `:L229` and `:L731`. That
field is part of what the oracle emits, so it cannot simply be dropped: dropping it would leave the recording
disagreeing with the window a reviewer is looking at. Instead, in `persistence-sqlparser-clause-model`:

- The field is **recorded**, with `maskedAsNonBehavioural` set to `true` on that output, so it is present in
  the capture and explicitly excluded from the comparison rather than quietly omitted.
- The seam is declared in the `determinismMask` with `appliesTo` naming **both** sides, because an elapsed
  time differs on every run on either side and masking one half would leave the other holding a value that
  can never be matched.

A timing assertion in a characterization suite is a defect *in the suite*: it fails for reasons unrelated to
behaviour, and it trains readers to ignore red results.

---

## File conventions

**Definitions and recordings are text-based and diffable**, so that a parity failure is legible in review
rather than merely detectable. A reviewer has to be able to see *what* differed, not only that something did.

**Six file extensions are forbidden anywhere in this store.** Never name a definition, a mask or a capture
`*.log`, `*.zip`, `*.rar`, `*.bak`, `*.dmp` or `*.bat`:

| Extension | Ignored by |
| --- | --- |
| `*.dmp` | `.gitignore:L7` |
| `*.log` | `.gitignore:L8` |
| `*.bak` | `.gitignore:L9` |
| `*.bat` | `.gitignore:L10` |
| `*.zip` | `.gitignore:L11` |
| `*.rar` | `.gitignore:L12` |

All six patterns are **unanchored**, so they match at every depth — including inside this directory — and the
file's only negations, at `.gitignore:L14-L15`, are anchored elsewhere in the tree and rescue nothing here. A
file named that way is therefore **silently untracked**: it exists on the machine that produced it, `git
status` says nothing about it, and it is simply absent from review and from CI. The work looks done and
cannot be found by anybody else.

`*.bat` is worth calling out on its own, because the store-wide conventions in
[`../README.md`](../README.md) §3.5 discuss the capture-file cases and this folder additionally holds no batch
file: a helper script named `run.bat` beside a definition would vanish just as quietly as a `.log` capture.

**The root `.gitignore` is not in scope to change.** The fix is to name the file correctly, not to weaken a
repository-wide ignore rule to accommodate a badly named one. Definitions use `.yaml`, which is tracked
normally; the schema's own filename pattern for recording artifacts admits only the seven diffable formats
and cannot spell any of the six above, which is the entire purpose of that pattern.

---

## User-specified rules

**No user-specified rules exist.** The project's rules document returns exactly one line, reporting that no
user rules were provided — it is a single line, with nothing further to page through.

Three consequences, stated explicitly rather than passed over, because the absence is a finding on the record
and not an omission:

- **No rule is invented, inferred or back-filled from convention here.** Where this document states a rule, it
  is either a constraint from the migration plan's own inventory — the ones enumerated under
  [Constraints that bind this folder](#constraints-that-bind-this-folder) — or a limit of the schema, which is
  machine-checkable and can be read directly.
- **Zero files in this folder exist because of a rule.** Every file here traces either to an explicit
  requirement of the migration plan or to a dependency verified by inspection: this document, the schema it
  points at, and one definition per roster row. There is no file present only to satisfy a coding guideline.
- **Enterprise-standard best practice applies in the rules' place**, and the absence is not treated as licence
  to lower the bar. Concretely, in this folder: every claim carries a locator into the read-only oracle; every
  decision carries its reason; no secret value appears in any form; no duplicated upstream text is allowed to
  drift; and every definition is validated against a schema rather than reviewed by eye.

---

## Honest limitation

**No workflow in this folder has been executed, and no paired capture has been taken on either side.**
Every definition therefore declares `executionStatus: specified-not-executed`, and the schema's enum for that
member has exactly **one** legal value, so no definition can claim otherwise even by accident. Widening that
enum is a deliberate act, taken only in the same reviewed change that lands the recordings justifying it.

Stated without softening:

- **The Compose bring-up the shared-volume capture rule is expressed against *has* been exercised — and a
  bring-up is not a capture.** It is reported gate by gate in
  [`../../orchestration/README.md` §10](../../orchestration/README.md#10-what-has-and-has-not-been-exercised),
  the only execution-status statement in this repository, which this folder defers to rather than restating.
  What that run established is the volume seam a pair needs; it produced no recording, and no comparison may
  be claimed from it.
- **The legacy half of the oracle has not been run**, which needs a PowerBuilder toolchain that is not
  present. This is exactly what keeps the pinyin risk live rather than theoretical.
- **Both halves of `characterization/recordings/` hold nothing but their own readmes**, so there is no
  master, no candidate and no comparison — and no parity result is claimed anywhere in this folder.
- **A passing service test is not a substitute for a capture.** An in-process test host mounts no volume, so
  it cannot be the target half of a pair however thorough it is; the capture rule is expressed against a
  Docker volume.

**These fifteen files are authored specifications, not verified runs.** Nothing in this folder may be read as
reporting a parity outcome, and nothing in it should be written that way. The roster, the masks and the seed
phases are the shape the first capture will be taken in — recorded here so that the first person to take a
real capture knows they are the first, and knows exactly which identifier to take it under.

[`../README.md`](../README.md) §7 states the store-wide position, and
[`../../docs/PARITY.md`](../../docs/PARITY.md) §1.5 is the authority for what was and was not verified across
the refactor as a whole.
