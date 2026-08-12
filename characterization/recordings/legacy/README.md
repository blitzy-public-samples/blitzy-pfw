<!-- Markdown lint policy for this file, matching the directive every document this refactor authors
     carries. The rationale in full is docs/BUILD.md section 14, which is the authority; this is only its
     application here. MD013 is 120 rather than the 80-character default, and is disabled for tables and
     code blocks: a roster row carrying a pairing key, and an evidence row carrying an ignore pattern
     beside the `.gitignore` line that produces it, cannot be wrapped without splitting the locator away
     from the claim that rests on it, and a wrapped command is a command that does not run. Prose IS
     wrapped, and is held to the 120 limit.

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

# `characterization/recordings/legacy` — the golden master half

This directory holds the **PowerBuilder oracle's output**: the master half of every paired characterization
recording. The legacy tree is the only statement of intended behaviour that exists anywhere in this
repository, so **what is recorded here defines correct**, and the candidate half under
`characterization/recordings/dotnet/<workflowId>/` is judged against it rather than the other way round.
That asymmetry is the whole reason this half's contract is written down before the candidate half's: a
candidate authored against a guess at the master contract is a candidate that will be measured against the
wrong thing.

This document is the **in-folder contract for this half only**. The store-wide rules — the capture rule, the
pairing-key semantics, the oracle inventory, the parity model — each belong to a document that already owns
them, and this file **cites them rather than restating them**. The discipline is not fastidiousness: a second
copy of a rule is a second thing that can be edited, and the copy nobody updated is indistinguishable from
the one somebody did. Sections below therefore say what an owned rule *rules for this half*, and send the
reader upstream for the rule itself.

## Current state of what this document describes

First, and not in a footnote: this half specifies a discipline it has not yet carried out, and a reader is
entitled to know that before reading a single instruction. The labels are the ones this documentation set
uses, defined in [`docs/BUILD.md`](../../../docs/BUILD.md).

| Artifact | State |
| --- | --- |
| `characterization/recordings/legacy/README.md` — this document | **Present but unexercised.** It is the contract the master captures will be taken against |
| `characterization/recordings/legacy/<workflowId>/` | **Planned — not yet present.** No directory exists, for any of the fifteen identifiers |
| A master recording under any identifier | **Planned — not yet present.** The PowerBuilder oracle has not been executed here |

**This tree contains no capture today.** No file in it may be presented as a verified capture, and
[§10](#10-what-has-not-been-exercised--stated-plainly) states that limitation in full rather than in passing.

## What this document deliberately does not duplicate

| For | Go to | Which owns |
| --- | --- | --- |
| The paired-capture shared-volume rule — **canonical text** | [`characterization/README.md`](../../README.md) §2 | The rule word for word, its provenance, the teardown corollary and the one exemption. [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) of this file cites it and adds no copy of it |
| The store's layout, the pairing-key semantics, the recording-file conventions and the newline reasoning | [`characterization/README.md`](../../README.md) §3 | Everything about the shape of the store. This file states how each of those binds **this half** |
| The oracle corpus, the primary fixture, the seed/capture split and the fixture-to-capability map | [`characterization/README.md`](../../README.md) §4 | The inventory, with verified counts |
| The fifteen pairing keys, and the correction register behind them | [`characterization/workflows/README.md`](../../workflows/README.md) | The roster. It is the **only** source of a directory name here |
| The machine-checkable shape of a definition, its mask, its seed phase and its recording filenames | [`characterization/workflows/workflow.schema.json`](../../workflows/workflow.schema.json) | The grammar, enforced by validation rather than by review |
| The parity model: technique, seam register, test shape, behaviour catalogue, coverage-gate mechanics, risks | [`docs/PARITY.md`](../../../docs/PARITY.md) | The model itself. Where this file and that one could disagree, **that one wins** |
| Secret locators, their severities and the required action for each | [`docs/SECRETS.md`](../../../docs/SECRETS.md) | The full inventory. **Cite it; reproduce no value** |
| The four deferred capability areas and the objects assigned to each | [`docs/DEFERRED.md`](../../../docs/DEFERRED.md) | The destination mapping and the reserved-route metadata |
| Compose bring-up, the readiness gates, teardown, and the volume's operator-facing rules | [`orchestration/README.md`](../../../orchestration/README.md) | The operational procedure |
| The declaration of the `persistence-db` volume, and which service mounts it | [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) | The manifest |
| Licence text and third-party attribution | [`README.md`](../../../README.md) and [`NOTICE`](../../../NOTICE) | Both. Neither is restated here |

## Table of contents

- [1. Purpose, authority, and why this file exists](#1-purpose-authority-and-why-this-file-exists)
- [2. Layout and the pairing key](#2-layout-and-the-pairing-key)
- [3. The identifier vocabulary is owned upstream](#3-the-identifier-vocabulary-is-owned-upstream)
- [4. File conventions, and two traps that fail silently](#4-file-conventions-and-two-traps-that-fail-silently)
- [5. What a capture must contain](#5-what-a-capture-must-contain)
- [6. The seed phase runs once, and never between the two halves](#6-the-seed-phase-runs-once-and-never-between-the-two-halves)
- [7. What must never be recorded](#7-what-must-never-be-recorded)
- [8. The shared-volume capture rule is cited here, not restated](#8-the-shared-volume-capture-rule-is-cited-here-not-restated)
- [9. Governing constraints](#9-governing-constraints)
- [10. What has not been exercised — stated plainly](#10-what-has-not-been-exercised--stated-plainly)

---

## 1. Purpose, authority, and why this file exists

### 1.1 The master half carries the authority

A pair is the only artifact in this repository that can settle whether behaviour was preserved, and the two
halves of a pair are not peers. This half is the **golden master**: it records what the legacy framework
actually does, including the parts that look wrong. The candidate half records what the replacement does.
A difference between them is therefore a finding **against the candidate**, never against the master, and
the master is never edited to make a comparison pass.

### 1.2 This is the folder's single tracked file

**Git tracks files, not directories.** Without exactly one tracked, non-ignored file at this folder's own
root, the entire subtree would exist only on the disk of whoever created it and would be **absent from
review and from CI** — which is the same failure mode as [§4.2](#42-trap-one--seven-unanchored-ignore-patterns),
arriving by a different route.

This document is that file. A bare `.gitkeep` would have held the directory open just as well; a readme
holds it open **and** carries this half's contract, so there is one file here instead of two. As a fact
about the path rather than a claim about the content: `git check-ignore` reports no match for
`characterization/recordings/legacy/README.md` against the repository's root `.gitignore`, so this file is
tracked normally.

### 1.3 A `<workflowId>/` directory is created by a capture, never in advance

**No `<workflowId>/` directory exists here, and none is created ahead of a real capture.** Pre-creating one
is not a harmless convenience — [§4.5](#45-trap-two--an-ordinary-file-in-a-workflow-directory-activates-a-skipped-test)
records the mechanism by which it can turn a truthfully reported BLOCKED state into a failing build. The
directory arrives with the recording that justifies it, in the reviewed change that lands it.

---

## 2. Layout and the pairing key

### 2.1 Layout

```text
legacy/
├── README.md         this file - the contract for this half, and the folder's single tracked file
└── <workflowId>/     one directory per captured workflow, created only when a real capture exists
```

[`characterization/README.md`](../../README.md) §3.1 is the authority for the store's layout as a whole,
including the sibling half. This block is the part of it that describes this folder.

### 2.2 `<workflowId>` is the pairing key

Two recordings are joined on that segment alone; no other part of either path takes part in the join.
**One identifier names exactly one directory here and exactly one under
`characterization/recordings/dotnet/`** — one on each side, spelled identically.

An identifier is **never renamed once a recording exists under it.** A rename orphans both halves of every
pair already captured under the old name, and nothing in the tooling reports that it happened.

### 2.3 An identifier present on one side only is an incomplete pair

> **A master with no candidate, or a candidate with no master, is not a partial comparison — it is not a
> comparison at all. Surface it as an incomplete pair. Never compare it against nothing, and never report
> it as a pass.**

[`characterization/README.md`](../../README.md) §3.2 is the authority for that rule. The consequence
specific to this half: a directory here whose identifier has no counterpart is **evidence of an unfinished
capture**, and the honest report of it is "incomplete", not "legacy-only pass".

---

## 3. The identifier vocabulary is owned upstream

This file is a **consumer** of the vocabulary and never an author of it. Two upstream documents own it
between them, and they own different halves of the problem:

| Owner | What it owns |
| --- | --- |
| [`characterization/workflows/README.md`](../../workflows/README.md) | **The roster.** Its `workflowId` column publishes the fifteen identifiers and declares them the sole naming input for `characterization/recordings/legacy/<workflowId>/` and `characterization/recordings/dotnet/<workflowId>/` |
| [`characterization/workflows/workflow.schema.json`](../../workflows/workflow.schema.json) | **The form.** Its `workflowId` member pins the spelling to strict lower-kebab-case with the pattern `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`, so a non-conforming identifier fails validation rather than review |

Two further guarantees in that schema bear directly on what may appear here. Its `targetService` enum is
**closed** and contains no deferred capability area, which makes a workflow aimed at one structurally
unrepresentable; and its `provesDefects` array carries `minItems: 1`, so a workflow that proves nothing
cannot be declared at all.

### 3.1 The fifteen permitted identifiers

In roster order. These are the **only** directory names this folder may ever hold:

| # | `workflowId` — the exact directory name |
| --- | --- |
| 1 | `persistence-sqlite-retrieve-update` |
| 2 | `persistence-thread-sqlquery-chunked` |
| 3 | `persistence-sqlparser-clause-model` |
| 4 | `persistence-sql-paging-rewrite` |
| 5 | `persistence-thread-task-lifecycle` |
| 6 | `dataservices-dw-event-chain` |
| 7 | `dataservices-dwsvc-rowselect` |
| 8 | `dataservices-dwsvc-columnexp` |
| 9 | `dataservices-dwsvc-columnsort` |
| 10 | `dataservices-dwsvc-contextmenu` |
| 11 | `dataservices-dwsvc-dropdownsearch` |
| 12 | `shared-diagnostics-assert-payload` |
| 13 | `shared-eventful-broker-ordering` |
| 14 | `gateway-lifecycle-composition-root` |
| 15 | `security-crypto-surface` |

### 3.2 The list is never added to here

**A new capture directory requires a new workflow definition upstream first — never a new name invented
here.** The order is fixed and it is not bureaucratic: a definition carries the seed phase, the capture
phase and the determinism mask without which a recording cannot be compared to anything, so a directory
created ahead of its definition is a directory whose contents have no mask, no counterpart and no meaning.

If a behaviour needs characterizing and no identifier covers it, the change belongs in
[`characterization/workflows/README.md`](../../workflows/README.md) and in a new sibling definition beside
it. This document is then a consumer of the result, exactly as it is a consumer of the fifteen above.

### 3.3 One standing refusal: the static-map window

`ws_objects/pfw.tests.pbl.src/w_test_static_map.srw` carries **no workflow, and no directory may ever be
created for it here.** Its name suggests the ordered-map container and it is not that — "static map" names a
static map *image*. Read in full, its single `clicked` event depends on two deferred capability areas:

| Locator | What it does | Capability area |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src/w_test_static_map.srw:L55`, `:L58` | Declares and creates an `n_httpclient` | **Integration** — deferred |
| `:L58-L66` | Issues an outbound `Request("GET", …)` against a live third-party static-image endpoint | **Integration** — deferred |
| `:L66` | Sizes that request through the `U2PX` and `U2PY` DPI conversions | **DesignSystem** — deferred |
| `:L68` | Calls `p_map.SetPicture(rsp.GetData())` | **DesignSystem** — deferred |
| `:L74-L81` | Declares `p_map` as `from picture` — the control the picture is set into | **DesignSystem** — deferred |

Both areas receive no code, no test and no container in this phase (C-D), so there is no candidate half for
a pair to be made from and nothing here to characterize. The outbound call to a live endpoint would rule it
out independently, because repeatability is the technique's own hard prerequisite.

**Its ordered-map coverage is not lost, it is displaced.** The container's insertion-order and positional
`get` contract is characterized inside `dataservices-dwsvc-contextmenu` instead. The correction register in
[`characterization/workflows/README.md`](../../workflows/README.md) is the owner of that decision and of the
evidence behind it.

### 3.4 One known divergence, and the roster wins

The drop-down-search workflow is reachable by two spellings today: the roster's
`dataservices-dwsvc-dropdownsearch`, and an oracle-window-shaped constant pinned in the pinyin
characterization hook under `services/dataservices-service/`. **The roster identifier is the canonical one**
— the schema's pattern admits no underscore, so the constant's spelling could not be written into a
definition at all.

The practical instruction for this folder is therefore unambiguous: **name the directory from the roster.**
The correction register cited above owns the reconciliation, and records that the constant is reconciled in
the same reviewed change that lands the first master recording for that workflow — not earlier, because an
edit to a skipped assertion has nothing to verify it against.

---

## 4. File conventions, and two traps that fail silently

### 4.1 Recordings are text-based and diffable

A parity failure has to be **legible in review**, not merely detectable: a reviewer must be able to see
*what* differed, not only that something did. Seven formats are permitted, and every one of them was probed
against the repository's ignore rules and confirmed trackable under this folder:

| Permitted extension | Typical master-side use |
| --- | --- |
| `.json` | A structured single-document capture — a buffer snapshot, a structured error payload, a set of counts |
| `.jsonl` | One record per line, where the capture is a sequence — an event chain, a stream of chunks |
| `.txt` | Free-form oracle output that has no better structure, captured as the framework emitted it |
| `.csv` | Row-shaped output where a column-per-field diff reads more clearly than nesting |
| `.sql` | Generated statement text, where byte-exact statement output is the thing being compared |
| `.md` | A human-readable capture note that belongs with the recording rather than in a commit message |
| `.yaml` | A structured capture where a reviewer benefits from comments beside the values |

This is not merely a convention here. [`characterization/workflows/workflow.schema.json`](../../workflows/workflow.schema.json)
enforces it: its `capturePhase.recordingArtifacts[].filename` pattern is the allow-list
`^[A-Za-z0-9][A-Za-z0-9._-]*\.(json|jsonl|txt|csv|sql|md|yaml)$`, which admits no directory separator and
**cannot spell any of the trapped names in [§4.2](#42-trap-one--seven-unanchored-ignore-patterns)** — the
`.db` of `thumbs.db` is not among the seven either. Keep one workflow's output inside its own directory and
share no file between identifiers. **Commit no large binary capture** unless it is genuinely required.

### 4.2 Trap one — seven unanchored ignore patterns

The repository's root `.gitignore` is **read-only and must not be modified.** It carries seven patterns with
**no leading slash**, so each one matches at every depth in the tree — including inside this folder. Each row
below was verified with `git check-ignore -v` against a path under
`characterization/recordings/legacy/<workflowId>/`:

| Pattern | `.gitignore` line | Verified effect on a file in this folder |
| --- | --- | --- |
| `*.dmp` | `.gitignore:L7` | Silently untracked |
| `*.log` | `.gitignore:L8` | Silently untracked |
| `*.bak` | `.gitignore:L9` | Silently untracked |
| `*.bat` | `.gitignore:L10` | Silently untracked |
| `*.zip` | `.gitignore:L11` | Silently untracked |
| `*.rar` | `.gitignore:L12` | Silently untracked |
| `thumbs.db` | `.gitignore:L13` | Silently untracked |

> **The failure mode: a capture so named appears present locally and is absent in review and in CI. Nothing
> errors. `git status` says nothing about it, no build step complains, and the comparison looks like it was
> taken while being unfindable by anybody else.**

Two of the seven are worth calling out because they are routinely overlooked. **`*.bat`** and
**`thumbs.db`** reach beyond the five capture-file extensions the store's conventions discuss elsewhere: a
helper script dropped beside a master recording, or a thumbnail cache left behind by a file browser on the
Windows machine that produced the master, both vanish exactly as quietly as a `.log` capture would. The
schema's own recording-artifact allow-list independently excludes `.bat` for the same reason.

`.gitignore:L1-L6` are all anchored to specific top-level paths and are harmless here.

### 4.3 The negations are anchored elsewhere and rescue nothing here

The file's only negations are `!/res/*.zip` at `.gitignore:L14` and `!/samples/*.zip` at `.gitignore:L15`.
Both begin with a slash, so both are **anchored to those two directories** and neither can reach this
folder. Proven by contrast, with `git check-ignore` in its non-verbose form, which prints only the paths
that are genuinely ignored:

| Probe path | Winning pattern | Ignored? |
| --- | --- | --- |
| `characterization/recordings/legacy/<workflowId>/capture.zip` | `.gitignore:L11` | **Yes** |
| `res/a.zip` | `.gitignore:L14` — a negation | No — rescued |
| `samples/a.zip` | `.gitignore:L15` — a negation | No — rescued |

**The fix for a trapped name is to rename the file, never to weaken a repository-wide ignore rule to
accommodate it.**

### 4.4 Detection is cheap, so do it before committing

```bash
# From the repository root. Prints the winning pattern when a name is trapped;
# prints nothing and exits 1 when the name is safe.
git check-ignore -v characterization/recordings/legacy/<workflowId>/<file>

# Verified examples of both outcomes:
git check-ignore -v characterization/recordings/legacy/wf/capture.log    # => .gitignore:8:*.log   (trapped)
git check-ignore -v characterization/recordings/legacy/wf/capture.json   # => no output, exit 1    (safe)
```

One reading caveat, so the output is never misinterpreted: `-v` prints a line for a **negated** match as
well, and there the leading `!` in the pattern means the path was rescued rather than ignored. Inside this
folder no negation can ever win ([§4.3](#43-the-negations-are-anchored-elsewhere-and-rescue-nothing-here)),
so here any printed pattern means trapped. The non-verbose form, which lists only genuinely ignored paths,
settles it either way.

### 4.5 Trap two — an ordinary file in a workflow directory activates a skipped test

The second trap runs in the opposite direction, and it is the reason
[§1.3](#13-a-workflowid-directory-is-created-by-a-capture-never-in-advance) forbids pre-creating a
directory. A characterization hook in the DataServices test suite is a **conditionally skipped** matrix that
un-skips itself as soon as a master recording appears for its workflow, and its activation predicate is
mechanical: the legacy directory must exist **and** contain at least one file that is neither dot-prefixed
nor named `README.md`.

| File landed in `characterization/recordings/legacy/<workflowId>/` | Effect |
| --- | --- |
| A dot-prefixed file, or a `README.md` | Treated as a placeholder. The matrix stays skipped — the correct state while the oracle is unexercised |
| **Anything else** | **Activates the matrix.** Scaffolding dropped there turns a truthfully reported BLOCKED state into a failing build |

[`characterization/README.md`](../../README.md) §3.6 is the authority for that predicate and its
consequences. Note the shape of the rule as it applies to *this* file: it sits at this half's root and not
inside a `<workflowId>/` directory, so it cannot activate anything. **Landing a real master recording is a
review moment, not a file copy** — the activating half, the paired half and any composition change move
together.

### 4.6 Git does not normalize newlines here, and the mask must

Verified, so this is not solved in the wrong place: the root `.gitattributes` declares only six
`linguist-language` mappings and carries **no `text` and no `eol` rule**; `git check-attr text eol` on a path
in this folder returns `unspecified` for both; and neither `core.autocrlf` nor `core.eol` is set. **Git
therefore performs no line-ending translation on anything stored here**, and the root `.gitattributes` is
**not in scope to change** (C-C).

The master is produced on Windows by PowerBuilder — the framework's own assertion payload is split on CRLF —
while the candidate runs in a Linux container. Raw line endings consequently differ **by platform, not by
behaviour**, and an unnormalized byte-exact comparison would report a difference on every line of every
multi-line recording.

> **Newline normalization belongs to the workflow definition's determinism mask, and must be applied
> identically to both sides.** A mask applied to one side only is worse than no mask at all: it manufactures
> a passing comparison out of mismatched data.

Normalization is a comparison-time convention. It never licenses editing a stored master.
[`characterization/README.md`](../../README.md) §3.7 owns the reasoning.

---

## 5. What a capture must contain

### 5.1 The capture phase's observable outputs, masked identically on both sides

A master recording holds **the observable outputs of its workflow's capture phase, and nothing else** — the
returned values and numeric return codes, generated statement text, buffer contents, item statuses, event
sequences and their ordering, structured error payloads field by field, identity values with their array
positions, and expression results. Every non-deterministic value that reaches one of those outputs is
substituted per that workflow's determinism mask, and **the same substitution is applied to the candidate
half**. [`docs/PARITY.md`](../../../docs/PARITY.md) §5.1 is the authoritative seam register.

Seed-phase activity is **not** capture-phase output and does not belong in the recording; see
[§6](#6-the-seed-phase-runs-once-and-never-between-the-two-halves).

### 5.2 Update workflows: both the current *and* the original value of every marked column

For any workflow that updates through the primary fixture
`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` — 37 lines, `release 12.5` at `:L2` — the recording carries
**both the current and the original value of every marked column, per row.** The fixture's own table
specification is why:

| Locator | Column | Declaration |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8` | `id` | `type=number update=yes updatewhereclause=yes key=yes identity=yes` |
| `:L9` | `name` | `type=char(100) update=yes updatewhereclause=yes` |
| `:L10` | `age` | `type=number update=yes updatewhereclause=yes` |
| `:L11` | `address` | `type=char(200) update=yes updatewhereclause=yes` |
| `:L12` | `salary` | `type=decimal(2) update=yes updatewhereclause=yes` |
| `:L13` | `birth` | `type=date update=yes updatewhereclause=yes` |
| `:L14` | *table* | `retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no sort="age A salary A "` |

`updatewhere=1` is the key-plus-updateable-columns concurrency mode: the generated statement's where-clause
carries the key column **plus the original values of every marked updateable column**. All six columns are
marked, so the optimistic-concurrency check spans **all six original values**.

> **A flat rowset capture is insufficient. It silently discards the state the update contract is built on,
> and a comparison of two flat rowsets can pass while the concurrency behaviour has changed completely.**

Two further facts about this fixture that a master recording has to reflect. `updatekeyinplace=no` at `:L14`
means a key change is performed as delete-plus-insert rather than in place, so that path is **exercised by
the fixture** rather than being a rare branch. And the footer computation `sum(salary for page)` at `:L27`
puts the expression engine in the capture path even here. [`docs/PARITY.md`](../../../docs/PARITY.md) §3.2
and [`characterization/README.md`](../../README.md) §4.2-§4.3 are the authorities for the fixture and for
this requirement.

### 5.3 A recording must show the defect, and a better-looking result is a FAILURE

Every workflow exists to prove that specific legacy behaviours survived — which is why the schema's
`provesDefects` array is mandatory and non-empty. A master recording is the evidence, so it has to contain
the behaviour in the state the legacy actually produces it, **including the part that looks wrong**.

> **A recording that shows corrected behaviour is a parity FAILURE, not an improvement. "The output looks
> more correct" is a bug report, and it is filed against the target as a divergence.**

Legacy defects are reproduced and annotated at the point of reproduction. They are never repaired, and a
master is never edited to make one look better than it is.

---

## 6. The seed phase runs once, and never between the two halves

### 6.1 The oracle's own setup is destructive

This is the trap that bites hardest on this half, because the fixture that produces the master fights the
pairing discipline all by itself. Verified inside the `CONNECT` button's `clicked` event in
`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw`:

| Locator | What the oracle does | Phase |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L448` | Early-out guard — returns immediately if the database is already open | Seed |
| **`:L450`** | **`FileDelete("test.db")` — destructive: the database file is deleted before it is opened** | **Seed** |
| `:L452-L455` | Documents the connection URI extensions in its own comments: `check[=quick]`, and `journal[=DELETE\|TRUNCATE\|PERSIST\|MEMORY\|WAL\|OFF]` defaulting to `DELETE` | Reference |
| `:L456` | Opens `test.db?mode=rwc`, with an optional password element in the same position | Seed |
| `:L461` | `SetAutoCommit(true)` — opens the bracket the DDL runs inside | Seed |
| `:L463-L469` | `CREATE TABLE IF NOT EXISTS COMPANY(...)` — the only DDL in the repository | Seed |
| `:L473` | `SetAutoCommit(false)` — closes that bracket | Seed |

**Every row above is seeding, not behaviour.** None of it is capture-phase output, and none of it belongs in
a recording. [`characterization/README.md`](../../README.md) §4.4 owns the seed/capture split and §4.5 owns
the URI grammar.

### 6.2 The rule, and the silent consequence of breaking it

> **The seed phase runs once, before a pair begins. It never runs between the two halves. The capture phase
> is the part that runs twice — once here, once on the candidate side — both against the state the seed
> phase already established.**

Re-running that `FileDelete` plus DDL to "start clean" between the master capture and the candidate capture
is precisely the reseed the store's capture rule forbids, and it is the single likeliest way a careful
engineer voids a comparison. **The pair then silently stops meaning anything while both files still look
perfectly valid** — two recordings of two different worlds, diffing cleanly enough to be reported as a
result. If the state has moved between the halves, the pair is void: discard both halves and take the
workflow again. Never repair a broken pair by re-taking one half of it.

The split is declared per workflow rather than left to memory: the schema requires a `seedPhase` carrying
`runsOncePerPair` and `neverBetweenCaptures` assertions, so a definition cannot omit its own account of this.

### 6.3 The one exemption, and it is exactly one

`persistence-sql-paging-rewrite` is the **only** workflow exempt from the shared-volume rule. The SQL Server
and Oracle paging behaviours take a statement plus a page size and index and return statement text: they are
**pure string transforms**, they read and write nothing, they provision no database instance and they mount
no volume, so there is no volume state to hold still (C-E). That definition declares the exemption
explicitly — `sharedVolume.applies` is `false` with a mandatory `sharedVolume.exemptionReason` — so it can
never be inferred from silence. Every other identifier in
[§3.1](#31-the-fifteen-permitted-identifiers) is governed by the rule in full.

The exemption is narrow and it is not a loophole: it holds only while a workflow reads and writes nothing.
[`characterization/README.md`](../../README.md) §2.7 is the authority for it.

---

## 7. What must never be recorded

### 7.1 No secret value of any kind

**No key material, no certificate, no password, no token, and no credential-bearing connection string —
in any file in this tree, in any form, including as an "example".** This is a live risk here rather than a
theoretical one: several of the repository's hardcoded-secret sites sit inside
`ws_objects/pfw.tests.pbl.src/` and `ws_objects/pfw.demos.pbl.src/` — **the very two libraries that supply
these fixtures**. [`docs/SECRETS.md`](../../../docs/SECRETS.md) §2.3 holds the locator inventory; it
reproduces no value, and neither may a recording.

One consequence that is easy to miss: where the connection URI of
[§6.1](#61-the-oracles-own-setup-is-destructive) is supplied with its optional password element, **the URI
itself is a credential** and is never logged, never returned in an error payload and never captured.

### 7.2 The crypto fixture passes key material as call arguments

The mechanism matters, because it is what makes this rule actionable rather than aspirational.
`security-crypto-surface` has no dedicated oracle test window; the only oracle for the cryptographic surface
is `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru`, and that object is **itself one of the
secret sites**. Worse for a naive capture, it passes key material **inline, as call arguments**:

| Locator | Shape at the call site |
| --- | --- |
| `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449` | A keyed hash whose key is a literal in the event script |
| `:L470` | `RSASign(<data>, <private-key variable>, …)` — the key is an argument |
| `:L504`, `:L547`, `:L592`, `:L607`, `:L622`, `:L637` | `SymEncrypt`/`SymDecrypt(<text>, <key>, <iv>, …)` — key and IV are both arguments, in all six symmetric calls |
| `:L466`, `:L718` | Two **distinct** embedded private keys, assigned to the variable those calls consume |
| `:L266`, `:L515` | A key field and an IV field defaulted to plain literals, in **unmasked** input controls |

> **Therefore a capture that naively records call arguments will capture key material.** Drive
> `security-crypto-surface` with **injected** test key material only, and never echo, record or store the
> key material embedded in that fixture. Cite the locators; reproduce no value.

[`docs/SECRETS.md`](../../../docs/SECRETS.md) §2.6 owns the assessment of that object, and
[`characterization/README.md`](../../README.md) §4.6 records that this is a named risk rather than an
oversight.

### 7.3 Two credential-bearing structure fields, named so they can be excluded

Verified in the transaction descriptor `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs`:

| Field | Locator | Treatment |
| --- | --- | --- |
| `logid` | `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L7` | Equally sensitive and **equally excluded** from every recording |
| `logpass` | `:L8` | **Write-only.** Never echoed in a response, never logged, therefore **never recorded** |

A recording that carries a transaction descriptor carries it with both fields omitted or replaced by a mask
token. Neither is ever present as a value, not even an obviously fake one.

### 7.4 Statement text is redacted, or split into statement plus parameters

Verified in the database-error descriptor `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs`: the
`sqlsyntax` field at `:L6` structurally carries **the complete generated statement, including interpolated
literal values**, and the legacy logger performs **no** redaction of it.

A master recording of an error path therefore records that field **redacted, or structurally split into a
statement and its parameters** — never raw. The generated statement is exactly what byte-exact parity is
measured on, so the split form is the one that preserves the comparison *and* the control.
[`docs/SECRETS.md`](../../../docs/SECRETS.md) §6 owns that control and explains why it is not a behavioural
change.

### 7.5 Nothing for a deferred service

**No directory here for DesignSystem, Documents, Integration or ScriptBridge.** Not a capture, not a
placeholder, not an empty directory reserving a name. Those four capability areas receive no code, no test
and no container in this phase, so they have no candidate half and therefore nothing that a pair could be
made from (C-D). [`docs/DEFERRED.md`](../../../docs/DEFERRED.md) is the owner of that roster; the schema's
closed `targetService` enum makes the same point mechanically, and
[§3.3](#33-one-standing-refusal-the-static-map-window) is this folder's worked example of the refusal.

### 7.6 No timing value as an assertion

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment anywhere, so **no performance objective may be asserted and no timing value may be
recorded as an assertion.**

> **Characterization compares observable outputs. It never compares execution time.**

One sharp edge, because it is real rather than hypothetical: the SQL-parser oracle emits a `CPU()`-derived
elapsed-milliseconds field as part of its own displayed output, so dropping it would leave the recording
disagreeing with the window a reviewer is looking at. It is therefore **recorded and explicitly masked** —
flagged as non-behavioural, with the mask naming **both** sides, because an elapsed time differs on every run
on either side and masking one half leaves the other holding a value that can never be matched.

**The 80% per-service line-coverage gate is not measured from this store at all.** It is measured in CI from
`coverage.cobertura.xml`, per service; [`docs/BUILD.md`](../../../docs/BUILD.md) owns the command and the
report path and [`docs/PARITY.md`](../../../docs/PARITY.md) §8 owns the gate mechanics. This store answers a
different question — whether behaviour was preserved — and a full store proves nothing about coverage just as
a green gate proves nothing about parity.

### 7.7 No edit to any fixture, and no vendored copy of one

`ws_objects/pfw.tests.pbl.src` and `ws_objects/pfw.demos.pbl.src` are read-only oracle assets — 47 test
windows and 11 DataWindow definitions in the first, 9 windows and 1 more DataWindow definition in the second
(C-C). **Capture *from* them; never modify them.** And never copy one into this tree: a fixture vendored here
becomes **a second, divergent oracle**, and the moment the two disagree there is no way to tell which one the
recordings were taken against. [`characterization/README.md`](../../README.md) §4.7 and §6.3 own that rule.

---

## 8. The shared-volume capture rule is cited here, not restated

The paired-capture shared-volume rule governs this half completely, and its canonical text is owned by
**exactly three** documents, which carry it word for word identically against the renamed `persistence-db`
volume:

1. [`characterization/README.md`](../../README.md) §2.1 — the store's in-folder authority
2. [`docs/PARITY.md`](../../../docs/PARITY.md) §4.2 — the canonical text
3. [`orchestration/README.md`](../../../orchestration/README.md) §7.2 — the operator-facing copy

**This file adds no fourth copy, and that omission is deliberate.** The rule's force depends on its three
copies being byte-identical, so a fourth is simply a fourth thing to drift — and a reader who found two
wordings would have no way to tell which was stale. Read the rule where it lives.

Intent only, in this document's own words and pointedly not in the rule's: a pair is trustworthy only while
the storage underneath its two halves has not moved between them, so nothing may disturb that volume in
between. **Treat that sentence as a signpost and not as the instruction** — decisions are made against the
canonical text above.

Three related decisions are **cited here, not re-decided**, because each is a documented decision owned
elsewhere (C-K):

| Decision | Owner | What this half needs to know |
| --- | --- | --- |
| The `data-service-db` → `persistence-db` volume rename | [`characterization/README.md`](../../README.md) §2.4 and [`orchestration/README.md`](../../../orchestration/README.md) §7.2 | The rule is stated against the new name in all three copies, so its intent survives the rename |
| The volume's declaration and its single mount | [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) | `persistence-db` is declared there and is mounted by **`persistence-service` alone** |
| The determinism seams | [`docs/PARITY.md`](../../../docs/PARITY.md) §5.1 | The seam register is the authority; a workflow's mask declares which seams it applies and to which sides |

Two operational corollaries that already have owners, noted here only so nobody has to rediscover them
while holding half a pair: between the two halves, tear a stack down with a plain `down` and never with the
volume-removing form ([`orchestration/README.md`](../../../orchestration/README.md) §6.1), and where clones
run in parallel, **both halves of a pair belong to one working tree** with its own project and its own
volume.

---

## 9. Governing constraints

### 9.1 No user-specified rules exist

**The project's rules document returns exactly one line, reporting that no user rules were provided.** It is
a single line with nothing further to page through, and any reader can confirm it through the same
rules-review facility. It is recorded here so that the absence reads as a **finding** rather than as an
oversight, and three consequences follow:

- **No rule is invented, inferred or back-filled from convention in this document.** Every instruction in it
  traces to a constraint from the migration plan's own inventory, to a schema member that is
  machine-checkable, or to a locator in the read-only oracle.
- **No file exists in this folder because of a rule.** This document exists because the store's legacy half
  needs a tracked contract at its root ([§1.2](#12-this-is-the-folders-single-tracked-file)); nothing here is
  present only to satisfy a coding guideline.
- **Enterprise-standard best practice applies in the rules' place**, and their absence is not treated as
  licence to lower the bar. Concretely, in this folder: every claim carries a locator into the oracle, every
  prohibition carries its mechanism, no upstream text is duplicated where it could drift, and no secret value
  appears in any form.

### 9.2 The constraints that bind this half, and what each one rules

Cited, not reproduced — the plan's inventory is the authority for their wording.

| Constraint | What it requires | Its ruling for this folder |
| --- | --- | --- |
| **C-B** | Behaviour is preserved and defects are reproduced, never corrected | **This store is the proof.** Hence [§5.3](#53-a-recording-must-show-the-defect-and-a-better-looking-result-is-a-failure): a master showing corrected behaviour is a FAILURE, and the schema's `provesDefects` is mandatory and non-empty |
| **C-C** | The legacy tree is read-only, and it is the behavioural oracle | Capture **from** the fixtures only. No edit to any of them, and no vendored copy into this tree — [§7.7](#77-no-edit-to-any-fixture-and-no-vendored-copy-of-one). The root `.gitignore` and `.gitattributes` are equally not edited: [§4.2](#42-trap-one--seven-unanchored-ignore-patterns) and [§4.6](#46-git-does-not-normalize-newlines-here-and-the-mask-must) work around them instead |
| **C-D** | Nothing is built for the four deferred services, not even a stub | No directory here for any of them — [§7.5](#75-nothing-for-a-deferred-service) — and the standing refusal of the static-map window in [§3.3](#33-one-standing-refusal-the-static-map-window) is that rule applied to a concrete candidate |
| **C-E** | SQLite only; no database is fabricated, and the two other dialects are pure string transforms | Hence the single volume-rule exemption for the paging rewrites in [§6.3](#63-the-one-exemption-and-it-is-exactly-one), and hence the only DDL a seed phase here ever runs is the fixture's own at `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469` |
| **C-F** | No secret value is carried forward; write-only fields are never echoed; statement text is redacted | The whole of [§7.1](#71-no-secret-value-of-any-kind) through [§7.4](#74-statement-text-is-redacted-or-split-into-statement-plus-parameters), including the inline-argument mechanism that makes the crypto workflow the hazard it is |
| **C-K** | Every technology- and boundary-specific decision is documented, with its reason | The volume rename and the determinism seams are **cited as owned decisions** in [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) and are not re-argued here |
| **C-L** | The attached environment's paired-capture rule is preserved verbatim in its owning files | Hence cite-not-restate: [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) names all three owners and adds no fourth copy |

---

## 10. What has not been exercised — stated plainly

Without softening, and nothing above should be read as implying otherwise:

- **Docker was not installed and no daemon was available in the environment this document was authored in.**
  The Compose bring-up was therefore never run, and neither was any paired capture against the
  `persistence-db` volume — the volume that
  [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) declares and that
  `persistence-service` alone mounts. **No health gate is claimed as passed.**
- **The legacy oracle has not been executed.** Running it needs a PowerBuilder toolchain that is not
  present here.
- **This tree contains no capture today, and it must not pretend otherwise.** There is no
  `<workflowId>/` directory, no master recording and therefore no pair — so **no parity result exists and
  none is claimed anywhere in this document.** No file that later appears in this tree may be presented as a
  verified capture unless it is one.
- **A passing service test is not a substitute for a capture.** An in-process test host mounts no volume, so
  it cannot stand in for either half of a pair however thorough it is.

**What was empirically confirmed, so the honesty cuts both ways:** the per-service path of restore, release
build, and test with coverage collection was exercised and passed, emitting `coverage.cobertura.xml` — the
artifact the coverage gate is measured from, and a different question from the one this store answers
([§7.6](#76-no-timing-value-as-an-assertion)). Within this folder, the trackability of this file, the seven
trapped names of [§4.2](#42-trap-one--seven-unanchored-ignore-patterns), the seven permitted formats of
[§4.1](#41-recordings-are-text-based-and-diffable), the anchored-negation contrast of
[§4.3](#43-the-negations-are-anchored-elsewhere-and-rescue-nothing-here) and the unset newline attributes of
[§4.6](#46-git-does-not-normalize-newlines-here-and-the-mask-must) were each verified by direct probe against
this repository.

[`characterization/README.md`](../../README.md) §7 states the store-wide position,
[`orchestration/README.md`](../../../orchestration/README.md) §10 states the orchestration position, and
[`docs/PARITY.md`](../../../docs/PARITY.md) §1.5 is the authority for what was and was not verified across the
refactor as a whole.
