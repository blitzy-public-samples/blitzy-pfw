<!-- Markdown lint policy for this file. It is the same self-declared directive every document this
     refactor authors carries, and the rationale in full belongs to docs/BUILD.md section 14, which is the
     authority; what follows is only its application here. MD013 is 120 rather than the 80-character
     default, and it is switched off for tables and code blocks: a roster row carrying a pairing key, and
     an evidence row carrying an ignore pattern beside the `.gitignore` line that produces it, cannot be
     wrapped without splitting the locator away from the claim that rests on it - and a wrapped command is
     a command that does not run. Prose IS wrapped, and is held to the 120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It cannot reach the five read-only legacy Chinese documents under docs/, which are part of the
     behavioural oracle, are never edited, and carry pre-existing violations of their own that this
     refactor must not act on.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e, and
     no Markdown linter appears in it. Documenting an on-demand package-runner invocation would instruct an
     unpinned version to be resolved and executed from the network, outside that lockfile, every time
     somebody followed this document. Lint with tooling that is already installed; the directive below is
     what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# `characterization/recordings/dotnet` — the candidate half

This directory holds the **target side's output**: the candidate half of every paired characterization
recording, produced **inside a Linux container** by the four Phase-1 .NET services — Gateway, DataServices,
Persistence and Security — and by the shared libraries beneath them.

**This half carries no authority of its own, and that is the one thing not to get backwards.** The master
half under [`characterization/recordings/legacy/<workflowId>/`](../legacy/README.md) records what the
PowerBuilder oracle actually does, and **what is recorded there defines correct**. A recording here is
meaningful *only* as a comparison against its paired master. A difference between the two is a finding
**against this half**, never against the master, and the master is never edited to make a comparison pass.
A reader who treats a file in this directory as evidence that the replacement is correct has misunderstood
the entire store: on its own, a candidate recording is a description of what the new code happens to do.

This document is the **in-folder contract for this half only**. The store-wide rules — the capture rule, the
pairing-key semantics, the oracle inventory, the parity model — each belong to a document that already owns
them, and this file **cites them rather than restating them**. Sections below say what an owned rule *rules
for this half*, and send the reader upstream for the rule itself.

## Current state of what this document describes

First, and not in a footnote: this half specifies a discipline it has not yet carried out, and a reader is
entitled to know that before reading a single instruction. The labels are the ones this documentation set
uses, defined in [`docs/BUILD.md`](../../../docs/BUILD.md).

| Artifact | State |
| --- | --- |
| `characterization/recordings/dotnet/README.md` — this document | **Present but unexercised.** It is the contract the candidate captures will be taken against |
| `characterization/recordings/dotnet/<workflowId>/` | **Planned — not yet present.** No directory exists, for any of the fifteen identifiers |
| A candidate recording under any identifier | **Planned — not yet present.** No capture has been taken on this side |
| A complete pair under any identifier | **Planned — not yet present.** The master half is equally unexercised, so no pair exists to compare |

**This tree contains no capture today.** No file in it may be presented as a verified capture, and
[§10](#10-what-has-not-been-exercised--stated-plainly) states that limitation in full rather than in
passing.

## What this document deliberately does not duplicate

| For | Go to | Which owns |
| --- | --- | --- |
| The contract for the **master** half, whose conventions this file mirrors | [`characterization/recordings/legacy/README.md`](../legacy/README.md) | The other half of the pair. Where a convention appears in both, it is deliberately the same convention and not a parallel wording |
| The paired-capture shared-volume rule — **canonical text** | [`characterization/README.md`](../../README.md) §2 | The rule word for word, its provenance, the teardown corollary and the one exemption. [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) of this file cites it and adds no copy of it |
| The store's layout, the pairing-key semantics, the recording-file conventions and the newline reasoning | [`characterization/README.md`](../../README.md) §3 | Everything about the shape of the store. This file states how each of those binds **this half** |
| The oracle corpus, the primary fixture, the seed/capture split and the fixture-to-capability map | [`characterization/README.md`](../../README.md) §4 | The inventory, with verified counts |
| The fifteen pairing keys, and the correction register behind them | [`characterization/workflows/README.md`](../../workflows/README.md) | The roster. It is the **only** source of a directory name here |
| The machine-checkable shape of a definition, its mask, its seed phase and its recording filenames | [`characterization/workflows/workflow.schema.json`](../../workflows/workflow.schema.json) | The grammar, enforced by validation rather than by review |
| The parity model: technique, seam register, test shape, behaviour catalogue, coverage-gate mechanics, risks | [`docs/PARITY.md`](../../../docs/PARITY.md) | The model itself. Where this file and that one could disagree, **that one wins** |
| Secret locators, their severities and the required action for each | [`docs/SECRETS.md`](../../../docs/SECRETS.md) | The full inventory, and which treatment a credential-bearing field takes. **Cite it; reproduce no value** |
| The four deferred capability areas, the three headless/rendering splits and the reserved routes | [`docs/DEFERRED.md`](../../../docs/DEFERRED.md) | The destination mapping and the reserved-route metadata |
| Compose bring-up, the readiness gates, teardown, and the volume's operator-facing rules | [`orchestration/README.md`](../../../orchestration/README.md) | The operational procedure |
| The declaration of the `persistence-db` volume, and which service mounts it | [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) | The manifest |
| Build and test commands, and where `coverage.cobertura.xml` lands | [`docs/BUILD.md`](../../../docs/BUILD.md) | The commands, the report path and the lint policy this file's head declares |
| Licence text and third-party attribution | [`README.md`](../../../README.md) and [`NOTICE`](../../../NOTICE) | Both. Neither is restated here |

## Table of contents

- [1. Purpose, authority, and why this file exists](#1-purpose-authority-and-why-this-file-exists)
- [2. Layout and the pairing key](#2-layout-and-the-pairing-key)
- [3. The identifier vocabulary is owned upstream](#3-the-identifier-vocabulary-is-owned-upstream)
- [4. File conventions, and two traps that fail silently](#4-file-conventions-and-two-traps-that-fail-silently)
- [5. What a capture must contain](#5-what-a-capture-must-contain)
- [6. Two dispositions, reported rather than approximated](#6-two-dispositions-reported-rather-than-approximated)
- [7. What must never be recorded](#7-what-must-never-be-recorded)
- [8. The shared-volume capture rule is cited here, not restated](#8-the-shared-volume-capture-rule-is-cited-here-not-restated)
- [9. Governing constraints](#9-governing-constraints)
- [10. What has not been exercised — stated plainly](#10-what-has-not-been-exercised--stated-plainly)
- [11. Before you commit a capture](#11-before-you-commit-a-capture)

---

## 1. Purpose, authority, and why this file exists

### 1.1 The candidate is the half under test

A pair is the only artifact in this repository that can settle whether behaviour was preserved, and the two
halves of a pair are not peers. The master records what the legacy framework does, **including the parts
that look wrong**. This half records what the replacement does. Three consequences follow directly, and each
is a rule rather than an observation:

- **A candidate recording on its own proves nothing.** It is not a result, not a baseline, and not a
  substitute for a master. Read alone, it is a transcript of the new implementation's current behaviour.
- **Every difference is a finding against this half first.** The oracle is not a party to the disagreement;
  it is the measure. Where the two disagree the investigation starts here, and the master is never adjusted
  to close the gap.
- **A candidate that looks *better* than its master is a failure.** That inversion is counter-intuitive
  enough, and specific enough to this side, that a whole section
  ([§5.3](#53-a-recording-must-show-the-defect-and-a-better-looking-result-is-a-failure)) is given to it.

### 1.2 This is the folder's single tracked file

**Git tracks files, not directories.** Without exactly one tracked, non-ignored file at this folder's own
root, the entire subtree would exist only on the disk of whoever created it and would be **absent from
review and from CI** — which is the same failure mode as
[§4.2](#42-trap-one--seven-unanchored-ignore-patterns), arriving by a different route.

This document is that file. A bare `.gitkeep` would have held the directory open just as well; a readme
holds it open **and** carries this half's contract, so there is one file here instead of two. As a fact
about the path rather than a claim about the content: `git check-ignore` reports no match for
`characterization/recordings/dotnet/README.md` against the repository's root `.gitignore`, so this file is
tracked normally.

### 1.3 A `<workflowId>/` directory is created by a capture, never in advance

**No `<workflowId>/` directory exists here, and none is created ahead of a real capture.** The master half
forbids pre-creation because an ordinary file dropped into a legacy directory activates a skipped test
matrix; on this side the mechanism is different and, if anything, quieter.
[§4.5](#45-trap-two--the-candidate-side-check-is-not-placeholder-aware) sets it out. The directory arrives
with the recording that justifies it, in the reviewed change that lands it.

### 1.4 The symmetry with the master half is load-bearing, not cosmetic

This file mirrors [`characterization/recordings/legacy/README.md`](../legacy/README.md) deliberately and
closely: the same permitted extensions, the same trapped names, the same never-record list, the same
citation posture, and the same fifteen identifiers spelled the same way. That is not tidiness, and it is
worth saying why, because the reasoning also tells a future editor what they must not do:

> **Two halves authored independently could each be individually correct and still be mutually divergent —
> and divergence is this store's defining failure mode, because it makes every pair incomparable while both
> sides go on looking valid.**

An empty store is honest and harmless. Two subtly different halves are silently useless: masks applied on
different terms, a name spelled two ways, a field excluded here and captured there. Nothing errors, nothing
warns, and every comparison quietly stops meaning anything. So where the master half states a convention,
this file **restates the same convention** rather than inventing a parallel wording that could drift, and
a change to a shared convention is a change to both files in one reviewed edit.

The most important of those shared conventions is machine-enforced rather than left to good intentions: a
workflow's determinism mask is declared inline in its own definition, and its `appliesTo` member is
required to name **both** `legacy` and `dotnet`. **A one-sided mask cannot be written down at all** — which
matters because a mask applied to one side only is worse than no mask, since it manufactures a passing
comparison out of mismatched data.

---

## 2. Layout and the pairing key

### 2.1 Layout

```text
dotnet/
├── README.md         this file - the contract for this half, and the folder's single tracked file
└── <workflowId>/     one directory per captured workflow, created only when a real capture exists
```

[`characterization/README.md`](../../README.md) §3.1 is the authority for the store's layout as a whole,
including the sibling half. This block is the part of it that describes this folder.

### 2.2 `<workflowId>` is the pairing key

Two recordings are joined on that segment alone; no other part of either path takes part in the join.
**One identifier names exactly one directory here and exactly one under
`characterization/recordings/legacy/`** — one on each side, spelled identically.

An identifier is **never renamed once a recording exists under it.** A rename orphans both halves of every
pair already captured under the old name, and nothing in the tooling reports that it happened.

### 2.3 An identifier present on one side only is an incomplete pair

> **A candidate with no master, or a master with no candidate, is not a partial comparison — it is not a
> comparison at all. Surface it as an incomplete pair. Never compare it against nothing, and never report
> it as a pass.**

[`characterization/README.md`](../../README.md) §3.2 is the authority for that rule. Two consequences are
specific to this half, and the first is the one to expect in practice:

- **A candidate without a master is the *likelier* asymmetry here.** This is the side under active
  development, while the master needs a PowerBuilder toolchain that is not present
  ([§10](#10-what-has-not-been-exercised--stated-plainly)). So the ordinary failure will be a directory
  here whose counterpart does not exist yet.
- **It is still an incomplete pair, not a partial result.** "The .NET side ran and the output looks
  reasonable" is not a weaker form of parity evidence; it is not parity evidence. Report it as incomplete,
  and note that nothing in the tooling will notice on your behalf — the one automated hook that watches
  this store keys its activation on the *legacy* directory
  ([§4.5](#45-trap-two--the-candidate-side-check-is-not-placeholder-aware)), so a candidate-only directory
  activates nothing and would sit unpaired and unremarked.

---

## 3. The identifier vocabulary is owned upstream

This file is a **consumer** of the vocabulary and never an author of it. Two upstream documents own it
between them, and they own different halves of the problem:

| Owner | What it owns |
| --- | --- |
| [`characterization/workflows/README.md`](../../workflows/README.md) | **The roster.** Its `workflowId` column publishes the fifteen identifiers and declares them the sole naming input for `characterization/recordings/legacy/<workflowId>/` and `characterization/recordings/dotnet/<workflowId>/` |
| [`characterization/workflows/workflow.schema.json`](../../workflows/workflow.schema.json) | **The form.** Its `workflowId` member pins the spelling to strict lower-kebab-case with the pattern `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`, so a non-conforming identifier fails validation rather than review |

Two further guarantees in that schema bear directly on what may appear here. Its `targetService` enum is
**closed** — nine members, being the four Phase-1 services and the five shared libraries — and no deferred
capability area is among them, which makes a workflow aimed at one structurally unrepresentable. And its
`provesDefects` array carries `minItems: 1`, so a workflow that proves nothing cannot be declared at all.

### 3.1 The fifteen permitted identifiers

In roster order, and identical to the master half's list character for character. These are the **only**
directory names this folder may ever hold:

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

**That the two lists match is itself a checkable property, and it is worth checking.** A single differing
character on either side makes every pair under that identifier incomplete, in the silent way
[§2.3](#23-an-identifier-present-on-one-side-only-is-an-incomplete-pair) describes: the join key simply
fails to join, and nothing reports a mismatch because a mismatch is indistinguishable from a capture that
has not been taken yet.

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
| `ws_objects/pfw.tests.pbl.src/w_test_static_map.srw:L55`, `:L56` | Declares an `n_httpclient` and an `n_httpresponse` | **Integration** — deferred |
| `:L58`, `:L60-L66` | Creates the client and issues an outbound `Request("GET", …)` against a live third-party static-image endpoint | **Integration** — deferred |
| `:L66` | Sizes that request through the `U2PX` and `U2PY` DPI conversions | **DesignSystem** — deferred |
| `:L68` | Calls `p_map.SetPicture(rsp.GetData())` | **DesignSystem** — deferred |
| `:L74-L81` | Declares `p_map` as `from picture` — the control the picture is set into | **DesignSystem** — deferred |

Both areas receive no code, no test and no container in this phase (C-D), so **there is no candidate half
for a pair to be made from** — which is the refusal stated from this side rather than the other. The
outbound call to a live endpoint would rule it out independently, because repeatability is the technique's
own hard prerequisite.

**Its ordered-map coverage is not lost, it is displaced.** The container's insertion-order and positional
`get` contract is characterized inside `dataservices-dwsvc-contextmenu` instead. The correction register in
[`characterization/workflows/README.md`](../../workflows/README.md) is the owner of that decision and of the
evidence behind it.

### 3.4 One known divergence, and the roster wins

The drop-down-search workflow is reachable by two spellings today: the roster's
`dataservices-dwsvc-dropdownsearch`, and an oracle-window-shaped constant pinned in the pinyin
characterization hook under `services/dataservices-service/`. That hook composes **both** of this store's
paths from that constant, so the divergence reaches this half exactly as it reaches the other one.
**The roster identifier is the canonical one** — the schema's pattern admits no underscore, so the
constant's spelling could not be written into a definition at all.

The practical instruction for this folder is therefore unambiguous: **name the directory from the roster.**
The correction register cited above owns the reconciliation, and records that the constant is reconciled in
the same reviewed change that lands the first recording for that workflow — not earlier, because an edit to
a skipped assertion has nothing to verify it against.

---

## 4. File conventions, and two traps that fail silently

### 4.1 Recordings are text-based and diffable

A parity failure has to be **legible in review**, not merely detectable: a reviewer must be able to see
*what* differed, not only that something did. Seven formats are permitted — the same seven the master half
permits — and every one of them was probed against the repository's ignore rules and confirmed trackable
under this folder:

| Permitted extension | Typical candidate-side use |
| --- | --- |
| `.json` | A structured single-document capture — a buffer snapshot, a structured error payload, a set of counts |
| `.jsonl` | One record per line, where the capture is a sequence — an event chain, a stream of gRPC chunks |
| `.txt` | Free-form output that has no better structure, captured as the service emitted it |
| `.csv` | Row-shaped output where a column-per-field diff reads more clearly than nesting |
| `.sql` | Generated statement text, where byte-exact statement output is the thing being compared |
| `.md` | A human-readable capture note that belongs with the recording rather than in a commit message |
| `.yaml` | A structured capture where a reviewer benefits from comments beside the values |

This is not merely a convention here. The
[workflow schema](../../workflows/workflow.schema.json)
enforces it: its `capturePhase.recordingArtifacts[].filename` pattern is the allow-list
`^[A-Za-z0-9][A-Za-z0-9._-]*\.(json|jsonl|txt|csv|sql|md|yaml)$`, which admits no directory separator and
**cannot spell any of the trapped names in [§4.2](#42-trap-one--seven-unanchored-ignore-patterns)** — the
`.db` of `thumbs.db` is not among the seven either. Keep one workflow's output inside its own directory and
share no file between identifiers. **Commit no large binary capture** unless it is genuinely required.

### 4.2 Trap one — seven unanchored ignore patterns

The repository's root `.gitignore` is **read-only and must not be modified.** It carries seven patterns with
**no leading slash**, so each one matches at every depth in the tree — including inside this folder. Each row
below was verified with `git check-ignore -v` against a path under
`characterization/recordings/dotnet/<workflowId>/`:

| Pattern | `.gitignore` line | Verified effect on a file in this folder |
| --- | --- | --- |
| `*.dmp` | `.gitignore:L7` | Silently untracked |
| `*.log` | `.gitignore:L8` | Silently untracked — **the sharpest risk on this side; see below** |
| `*.bak` | `.gitignore:L9` | Silently untracked |
| `*.bat` | `.gitignore:L10` | Silently untracked |
| `*.zip` | `.gitignore:L11` | Silently untracked |
| `*.rar` | `.gitignore:L12` | Silently untracked |
| `thumbs.db` | `.gitignore:L13` | Silently untracked |

> **The failure mode: a capture so named appears present locally and is absent in review and in CI. Nothing
> errors. `git status` says nothing about it, no build step complains, and the comparison looks like it was
> taken while being unfindable by anybody else.**

**`*.log` deserves singling out, because on this side it is not a name somebody has to choose — it is the
name the platform suggests.** The master half is produced by a desktop framework, where calling a capture
`capture.log` takes a deliberate decision. Here, the output comes from ASP.NET Core services whose most
natural writing surface is a logging sink, and a file sink's conventional name is exactly the one
`.gitignore:L8` swallows. A capture piped straight out of a logger therefore disappears from version control
while every local check still passes.

**So route a capture to an explicit `.jsonl`, `.json` or `.txt` artifact written by the recording code, and
never to a logger's file sink and never to a `.log`.** The distinction is worth keeping even where the
content would be identical: a recording is an artifact with a declared filename in its workflow definition,
whereas a log is a side effect with a name chosen by configuration — and configuration is where a trapped
name gets reintroduced by somebody who never read this file.

`.gitignore:L1-L6` are all anchored to specific top-level paths and are harmless here.

### 4.3 The negations are anchored elsewhere and rescue nothing here

The file's only negations are `!/res/*.zip` at `.gitignore:L14` and `!/samples/*.zip` at `.gitignore:L15`.
Both begin with a slash, so both are **anchored to those two directories** and neither can reach this
folder. Proven by contrast, with `git check-ignore` in its non-verbose form, which prints only the paths
that are genuinely ignored:

| Probe path | Winning pattern | Ignored? |
| --- | --- | --- |
| `characterization/recordings/dotnet/<workflowId>/capture.zip` | `.gitignore:L11` | **Yes** |
| `res/a.zip` | `.gitignore:L14` — a negation | No — rescued |
| `samples/a.zip` | `.gitignore:L15` — a negation | No — rescued |

**The fix for a trapped name is to rename the file, never to weaken a repository-wide ignore rule to
accommodate it.** That is not merely a preference: the root `.gitignore` sits inside the read-only legacy
region (C-C), so editing it is not an available option in the first place.

### 4.4 Detection is cheap, so do it before committing

```bash
# From the repository root. Prints the winning pattern when a name is trapped;
# prints nothing and exits 1 when the name is safe.
git check-ignore -v characterization/recordings/dotnet/<workflowId>/<file>

# Verified examples of both outcomes:
git check-ignore -v characterization/recordings/dotnet/wf/capture.log    # => .gitignore:8:*.log   (trapped)
git check-ignore -v characterization/recordings/dotnet/wf/capture.jsonl  # => no output, exit 1    (safe)
```

One reading caveat, so the output is never misinterpreted: `-v` prints a line for a **negated** match as
well, and there the leading `!` in the pattern means the path was rescued rather than ignored. Inside this
folder no negation can ever win ([§4.3](#43-the-negations-are-anchored-elsewhere-and-rescue-nothing-here)),
so here any printed pattern means trapped. The non-verbose form, which lists only genuinely ignored paths,
settles it either way.

### 4.5 Trap two — the candidate-side check is not placeholder-aware

The second trap runs in the opposite direction, and this half's version of it is quieter than the master
half's. A characterization hook in the DataServices test suite is a **conditionally skipped** matrix that
un-skips itself as soon as a master recording appears for its workflow. Its activation predicate reads the
**legacy** directory: that directory must exist **and** contain at least one file that is neither
dot-prefixed nor named `README.md`. So nothing placed in *this* half activates anything.

What happens next is what matters here. Once activated, the matrix asserts the pair rule of
[§2.3](#23-an-identifier-present-on-one-side-only-is-an-incomplete-pair) from both directions — the legacy
directory exists and is non-empty, **and so does the corresponding directory here** — before it asserts
anything about the matcher itself. Two consequences follow:

| Situation | Effect |
| --- | --- |
| A master recording lands and this half has no directory for that identifier | **The build fails**, reporting a legacy recording with no target-side recording. That is the pair rule working, not a defect |
| A master recording lands and this half has a directory holding only a placeholder | **The pair assertion is satisfied by the placeholder.** The candidate-side check tests only that the directory is non-empty, and unlike the legacy-side activation predicate it does **not** exempt dot-files or `README.md` |

The second row is the trap, and it is the reason
[§1.3](#13-a-workflowid-directory-is-created-by-a-capture-never-in-advance) forbids pre-creating a directory
here as firmly as the master half forbids it there — by a different mechanism, to the same end. A
`.gitkeep` dropped into `characterization/recordings/dotnet/<workflowId>/` would let an activated matrix
walk past the pair check on a directory that holds no capture at all, converting a loud, correct failure
into a quiet, wrong pass. Note the shape of the rule as it applies to *this* file: it sits at this half's
root and not inside a `<workflowId>/` directory, so it takes no part in either check.

**Landing a real recording is a review moment, not a file copy.** The activating half, this half and any
composition change move together, in one reviewed change.
[`characterization/README.md`](../../README.md) §3.6 is the authority for the predicate and its
consequences.

### 4.6 Git does not normalize newlines here, and the mask must

Verified, so this is not solved in the wrong place: the root `.gitattributes` declares only six
`linguist-language` mappings and carries **no `text` and no `eol` rule**; `git check-attr text eol` on a
path in this folder returns `unspecified` for both; and neither `core.autocrlf` nor `core.eol` is set.
**Git therefore performs no line-ending translation on anything stored here**, and the root `.gitattributes`
is **not in scope to change** (C-C).

This half runs in a Linux container and emits **LF**. The master is produced on Windows by PowerBuilder —
the framework's own assertion payload is split on CRLF — and emits **CRLF**. The difference is therefore
**by platform, not by behaviour**, and an unnormalized byte-exact comparison would report a difference on
every line of every multi-line recording.

> **Newline normalization belongs to the workflow definition's determinism mask, and must be applied
> identically to both sides.** A mask applied to one side only is worse than no mask at all: it manufactures
> a passing comparison out of mismatched data.

This is also why every definition carries at least one mask entry: line endings alone differ on every
multi-line recording, so there is no such thing as a workflow with nothing to mask. Normalization is a
comparison-time convention, and it never licenses editing a stored master — nor "fixing" this side's line
endings to look like Windows, which would hide a platform difference inside the data rather than declaring
it in the mask. [`characterization/README.md`](../../README.md) §3.7 owns the reasoning.

---

## 5. What a capture must contain

### 5.1 The capture phase's observable outputs, masked identically on both sides

A candidate recording holds **the observable outputs of its workflow's capture phase, and nothing else** —
the returned values and numeric return codes, generated statement text, buffer contents, item statuses,
event sequences and their ordering, structured error payloads field by field, identity values with their
array positions, and expression results. What is **never** compared is execution time
([§7.6](#76-no-timing-value-as-an-assertion)).

Every non-deterministic value that reaches one of those outputs is substituted per that workflow's
determinism mask, and **the same substitution is applied to the master half.**
[`docs/PARITY.md`](../../../docs/PARITY.md) §5.1 is the authoritative seam register, which carries each
seam's injection point and its own status; the four it registers are:

| Seam | Why this half has to seam it rather than mask it |
| --- | --- |
| GUID, random-string and random-blob generation in Security's cryptographic surface | The primary non-determinism sources in the in-scope estate. Every value they produce differs on every run, so a `security-crypto-surface` capture is meaningless unless the generator is injected |
| The transaction-pool idle expiry — CPU-clock based, keyed on the two keep-alive settings | The elapsed time is never asserted, but **whether a pooled transaction is reused or discarded is observable**, so the decision has to be reproducible even though the duration behind it is not recorded |
| Every clock read that reaches an output or a decision | A timestamp in recorded output differs on every run. Seaming the clock is what makes the surrounding output comparable at all |
| Modify-call ordering on the DataWindow modify path | Two orderings that reach the same state can emit different intermediate output, so an unpinned order surfaces as a difference where no behaviour changed |

**Prefer the seam to the mask.** Masking removes a field from the comparison; seaming makes the field
reproducible — and only the second leaves the comparison able to detect anything in that field at all. A
mask is the fallback for a value that cannot be made reproducible without changing observable behaviour, and
[`docs/PARITY.md`](../../../docs/PARITY.md) §5.1 records which seams are injected today and which are not.

Seed-phase activity is **not** capture-phase output and does not belong in the recording. The capture phase
is the half that runs twice — once on the master side, once here — and both runs go against the state the
seed phase already established. **This side never re-seeds first to "start clean"**: that is the reseed the
capture rule of [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) forbids, and it is the
single likeliest way a careful engineer voids a comparison.
[`characterization/README.md`](../../README.md) §4.4 owns the seed/capture split.

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

**On this side that shortcut is a live temptation rather than a hypothetical one, and it is worth naming the
exact shape it takes.** A modern data-access result *is* a flat sequence of current values: serializing the
entity list a query returns is the most natural line of code in the file, it produces a clean, readable,
diffable document, and it is wrong — because the original values it omits are not a detail of the
representation, they are the input to the concurrency check. The published contract makes the same point
structurally: `shared/PowerFramework.Contracts/Proto/common.v1.proto` gives its conflict row **both** a
`current_values` and an `original_values` collection, so a flat capture diverges from the wire contract as
well as from the oracle.

Two further facts about this fixture that a candidate recording has to reflect. `updatekeyinplace=no` at
`:L14` means a key change is performed as delete-plus-insert rather than in place, so that path is
**exercised by the fixture** rather than being a rare branch. And the footer computation
`sum(salary for page)` at `:L27` puts the expression engine in the capture path even here.
[`docs/PARITY.md`](../../../docs/PARITY.md) §3.2 and
[`characterization/README.md`](../../README.md) §4.2-§4.3 are the authorities for the fixture and for this
requirement.

### 5.3 A recording must show the defect, and a better-looking result is a FAILURE

Every workflow exists to prove that specific legacy behaviours survived — which is why the schema's
`provesDefects` array is mandatory and non-empty. A candidate recording is the evidence for this side, so it
has to contain the behaviour in the state the legacy produces it, **including the part that looks wrong**.

> **A recording that shows corrected behaviour is a parity FAILURE, not an improvement. "The output looks
> more correct" is a bug report, and it is filed against this half as a divergence.**

**This is the sharpest rule for the candidate half, and the reason is structural rather than rhetorical.**
The master cannot drift: it is produced by the legacy itself, so whatever it emits is by definition what the
oracle does. This half is a fresh implementation, and everything about how it is built pulls toward
correctness — the language's null handling, the analyzers, the framework's defaults, the reviewer's
instincts, and the entirely reasonable professional reflex that says a tri-state predicate with a hole in it
is a bug to fix. Under this contract it is not a bug to fix; it is the behaviour being preserved.
**A candidate is therefore most likely to fail by succeeding**, which is a failure mode no amount of
ordinary care detects, because ordinary care is what causes it.

What must appear here exactly as wrong as it appears in the master half:

| Behaviour | What the recording has to show |
| --- | --- |
| The three fixture-versus-DDL type mismatches | `address` is `char(200)` in the DataWindow [`dw_sqlite.srd:L11`] against `ADDRESS CHAR(50)` in the DDL [`w_test_sqlite.srw:L467`]; `salary` is `decimal(2)` [`:L12`] against `SALARY REAL` [`:L468`]; `birth` is `date` [`:L13`] against `BIRTH TEXT` [`:L469`]. Reproduce; do not reconcile |
| The tri-state return algebra | A prevention reads as a **success**, and cancelled and null are **neither** succeeded nor failed. Record the **numeric** code: the boolean overloads make prevent and failed indistinguishable, so a boolean projection destroys the distinction the pair exists to prove |
| The four-value item-change alphabet | `{0,1,2,3}`, where case 1 falls through to case 2, case 3 rewrites its result to 1, and the default arm coerces by column type and then **forcibly returns 2**. It is its own alphabet and is never mapped onto the return-code algebra |
| The tri-valued broker veto | Prevent-once, prevent-deep, and continue — never flattened to a boolean, because flattening silently converts a deep prevention into a shallow one |
| The two localization mistranslations | Both reproduced from the resource table. The commented-out pre-resource table that holds the *correct* wording must not be revived — reviving it is the silent correction this rule exists to forbid |
| The inverted Filter-buffer traversal | It runs **backwards**, because that buffer's row order is inverted relative to the source. It looks like a bug and is not: "correcting" the direction produces wrong identity data that a row-count assertion still passes |
| The self-assignment workaround | A modified key column assigned to itself purely to flip its item status. There is no managed analogue for a self-assignment that mutates hidden state, so the recording has to show the resulting statement generation rather than the trick that produced it |
| Byte-exact generated paging SQL | Every sentinel identifier and the count-wrapper alias, byte for byte. These are **pure string transforms** needing no instance of either dialect — the exemption [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) records |

That table is the reviewer's summary of what a pair is *for*, not a second catalogue.
[`docs/PARITY.md`](../../../docs/PARITY.md) §7 is the authoritative catalogue — twelve behaviour groups, each
with its locators — and [`characterization/README.md`](../../README.md) §5.3 is the store-wide roster. Two
notes where those authorities are wider than the row above, recorded rather than reconciled:
[`docs/PARITY.md`](../../../docs/PARITY.md) §3.5 catalogues **four** preserved schema mismatches, adding
`name` as `char(100)` [`dw_sqlite.srd:L9`] against an unbounded `NAME TEXT NOT NULL` [`w_test_sqlite.srw:L465`],
where the bound exists only in the DataWindow; and the same section records a fifth row that is **not** a
mismatch, `id` against `ID INTEGER PRIMARY KEY NOT NULL`, noting that the DDL carries no `AUTOINCREMENT`
keyword, so rowids of deleted rows are reused and the identity round-trip must not assume monotonicity.

---

## 6. Two dispositions, reported rather than approximated

Some legacy behaviour cannot be reproduced on this side. There are exactly two honest outcomes for that, and
the schema names them both: a behaviour is either reported **BLOCKED** with no substitute shipped, or the
contract is deliberately **narrowed with a defined error** returned at the boundary. **Approximating is not
one of them**, and a capture records the defined error rather than a guess.

### 6.1 BLOCKED — pinyin first-letter matching

Workflow: `dataservices-dwsvc-dropdownsearch`. Disposition: reported BLOCKED, no substitute shipped.

The call site is a single line, and the boundary of the risk is narrower than it first looks:
`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323` appends
`PinyinFirstLetterLike(<display column>, '<lower-cased user data>', 7)` to the filter expression, guarded by
a `FILTER_DISP_PY` bit test at `:L321` and by an alphabetic-only `Match` guard at `:L322`.

**The flag argument needs no characterization.** Its constants are declared under a comment naming the
function at `ws_objects/pfw.shared.pbl.src/enums.sru:L1146-L1149`, so the literal `7` decodes as ignore-case,
ignore-width and fuzzy-sound all enabled. What remains unprovable from the repository alone is enough to keep
the risk live, and it is exactly four closed inputs: the **character-to-initial lookup table**, which exists
only inside the closed native binary with no table, data file or mapping anywhere in the tree; the
**matching relation** — prefix, substring or subsequence, and the treatment of non-Han and multi-reading
characters; the **two-argument overload's default flag set**; and the **fuzzy-sound equivalence set's**
closure.

> **If the oracle cannot be exercised, report the pinyin filter as BLOCKED. Do not approximate it.**

The reasoning is specific rather than cautious. A substitute table gets most characters right and a minority
wrong, so an approximated filter returns **subtly different result sets** — mostly correct, occasionally
missing a row, occasionally including one. That is a regression that looks like correct behaviour, passes
review, and is found by a user rather than by a test. **A BLOCKED report is a known gap; an approximation is
an unknown one.** The shipped composition carries an explicit BLOCKED marker for exactly this reason, and the
four skipped theory cases waiting on those four closed inputs are the reportable form of the state.
[`characterization/README.md`](../../README.md) §5.5 and
[`docs/PARITY.md`](../../../docs/PARITY.md) R1 are the authorities.

### 6.2 Narrowed with a defined error — cross-session foreign column-expression variables

Workflow: `dataservices-dwsvc-columnexp`. Disposition: the contract is narrower than the legacy, by design.

The oracle resolves a foreign variable through a **live in-process pointer** to another DataWindow's
expression service, and a pointer has no representation across a service boundary. The port therefore holds
a **session-scoped handle** instead: references are supported while both DataWindows are co-resident in the
same expression session, and a reference that spans sessions or service instances is refused with a
**defined error** rather than resolved to a value that might be wrong. Co-residency is checked when the link
is created as well as when it is resolved, so a cross-session link fails at the point it is written rather
than on every subsequent calculation.

For a recording, that means: capture co-resident references normally, and capture a cross-session reference
**as the defined error, naming the narrowing**. This is a genuine, documented narrowing of the legacy
contract rather than a defect on this side, and it is recorded as such so that a reviewer does not read the
error as a regression. [`docs/PARITY.md`](../../../docs/PARITY.md) R2 is the authority.

### 6.3 Deferred capability gaps — record the headless output, name the gap

Three DataWindow services are irreducibly presentational, so each ships **a headless half only**. A capture
of one of these workflows records what the headless half produces and **names what is missing; it never
synthesizes the rendering half.**

| Workflow | Headless half — **captured** | Rendering half — **deferred, never synthesized** |
| --- | --- | --- |
| `dataservices-dwsvc-dropdownsearch` | Filter-expression construction, including the pinyin filter clause; the search state machine; row and filtered counts | Window positioning, and text input through the input-method editor |
| `dataservices-dwsvc-contextmenu` | The complete menu item model — labels, ids, enabled and split flags, and computed logical text widths | DPI-to-pixel conversion, font measurement, and actual menu rendering |
| `dataservices-dwsvc-columnsort` | Sort-expression construction and sort state | Sort-indicator geometry via DPI conversion |

Each rendering half is reserved behind the Gateway `/v1/design/**` extension point, which answers `501` with
a machine-readable body naming the deferred service. **That is routing metadata about the shape of the
eventual system, not a stub of it** — nothing exists behind it: no project, no container, no test, no partial
implementation and no placeholder that throws. Nothing is silently dropped either: the gap is enumerable by
reading the right-hand column above, and [`docs/DEFERRED.md`](../../../docs/DEFERRED.md) §5 and §6 own the
mapping and the evidence for the split.

The temptation this rule forecloses is the mirror image of the one in
[§5.3](#53-a-recording-must-show-the-defect-and-a-better-looking-result-is-a-failure): a capture that filled
in a plausible pixel geometry or a measured font width would look *more complete* than one that named the
gap, and it would be fabricated. A documented gap can be audited; a fabricated value cannot be distinguished
from a measured one.

---

## 7. What must never be recorded

The same list the master half carries, because a field excluded there and captured here is precisely the
divergence [§1.4](#14-the-symmetry-with-the-master-half-is-load-bearing-not-cosmetic) warns about.
**No value of any of the following appears anywhere in this tree, in any form, including as an "example".**

### 7.1 No secret value of any kind

**No key material, no certificate, no password, no token, and no credential-bearing connection string.**
This is a live risk here rather than a theoretical one: several of the repository's hardcoded-secret sites sit
inside `ws_objects/pfw.tests.pbl.src/` and `ws_objects/pfw.demos.pbl.src/` — **the very two libraries that
supply these fixtures**. [`docs/SECRETS.md`](../../../docs/SECRETS.md) §2.3 holds the locator inventory; it
reproduces no value, and neither may a recording.

One consequence that is easy to miss: the fixture's connection URI takes an optional password element in the
same position as its access mode. Where it is supplied, **the URI itself is a credential** and is never
logged, never returned in an error payload and never captured. The schema encodes the same posture — it
records only that the parameter position exists, and has no member that could hold its value.

### 7.2 The crypto fixture passes key material as call arguments

The mechanism matters, because it is what makes this rule actionable rather than aspirational.
`security-crypto-surface` has no dedicated oracle test window; the only oracle for the cryptographic surface
is `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru`, and that object is **itself one of the
secret sites**. Worse for a naive capture, it passes key material **inline, as call arguments**:

| Locator | Shape at the call site |
| --- | --- |
| `ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L449` | A keyed hash whose key is a literal in the event script |
| `:L470` | An RSA signature over a private-key variable — the key is an argument |
| `:L504`, `:L547`, `:L592`, `:L607`, `:L622`, `:L637` | Symmetric encrypt and decrypt calls where key and IV are both arguments, in all six |
| `:L466`, `:L718` | Two **distinct** embedded private keys, assigned to the variable those calls consume |
| `:L266`, `:L515` | A key field and an IV field defaulted to plain literals, in **unmasked** input controls |

> **Therefore a capture that naively records call arguments will capture key material.** Drive
> `security-crypto-surface` with **injected** test key material only, and never echo, record or store the key
> material embedded in that fixture. Cite the locators; reproduce no value.

[`docs/SECRETS.md`](../../../docs/SECRETS.md) §2.6 owns the assessment of that object, and
[`characterization/README.md`](../../README.md) §4.6 records that this is a named risk rather than an
oversight.

### 7.3 The hazard unique to this half: the system's one signing secret

The master half is produced by a **library**. It opens no listening socket, registers no route, issues no
token and has no issuer — so a token cannot appear in a master recording, because none exists on that side.
This half is produced by **services that authenticate every call**, so a token is present in the capture
environment by construction. That asymmetry makes this a candidate-side rule with no counterpart:

- **Exactly one signing secret exists in the whole system**, `SECURITY_JWT_SIGNING_KEY`, and it is held by
  Security and no other component. It is an RSA private key, not a random symmetric secret, because Security
  signs `RS256` and publishes an RSA key set.
- **No capture may contain it — nor any token minted with it, nor any private key derived from it.** A
  short-lived token is still a bearer credential, and a recording is a file that outlives it in version
  control.
- **Verification material is a different thing and is public by design.** Gateway, DataServices and
  Persistence hold verification material only, validate with the framework's stock bearer handler, and cannot
  mint. Any per-service signing-key *names* that survive are verification-side names and are not independent
  signing authorities. That a published key set is public is exactly what distinguishes it from a signing
  key — and is not licence to capture a token that was validated against it.

[`docs/SECRETS.md`](../../../docs/SECRETS.md) §4.1 and §4.2 own the topology.

### 7.4 Two credential-bearing structure fields, named so they can be excluded

Verified in the transaction descriptor `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs`:

| Field | Locator | Treatment |
| --- | --- | --- |
| `logid` | `ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs:L7` | Equally sensitive and **equally excluded** from every recording |
| `logpass` | `:L8` | **Write-only.** Never echoed in a response, never logged, therefore **never recorded** |

A recording that carries a transaction descriptor carries it with both fields omitted or replaced by a mask
token. Neither is ever present as a value, not even an obviously fake one.

### 7.5 Statement text and driver message text are recorded redacted, never raw

Verified in the database-error descriptor `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs`: the
`sqlsyntax` field at `:L6` structurally carries **the complete generated statement, including interpolated
literal values**, and the legacy logger performs **no** redaction of it. The mechanical root is a connection
setting rather than an accident — where bind variables are disabled the runtime interpolates values straight
into the statement text, so on such a connection every parameter value a failing statement touched is present
in that field as a literal.

**In this half the field is produced by a service whose audience includes a network peer and a log
aggregator, neither of which existed when the legacy filled the same field in process.** So it is recorded
redacted, and never raw. Two points of precision, because the shape is decided rather than open:

- **The single redacted field is the implemented and published shape.**
  [`docs/SECRETS.md`](../../../docs/SECRETS.md) §6 owns that control and records the decision: a statement
  plus a separate parameter collection was the alternative and it is **withdrawn**, because a parameter
  collection *is* the sensitive data — separating a literal from its statement moves the value rather than
  protecting it, and yields two fields to redact instead of one. The published contract matches: its database
  error message carries the statement field and **no** parameter member.
- **The driver's own message text is redacted on the same terms**, because a provider routinely quotes the
  caller's data into it — a uniqueness violation names the duplicated column, a type or constraint failure
  quotes the offending value, a bad identifier echoes the text the caller sent. The redaction is
  **literal-scoped**: literals and comment bodies are replaced with placeholders and every other byte is
  copied through, which is what keeps the field a useful diagnostic *and* keeps a byte-exact comparison
  meaningful.

A workflow declares which treatment each withheld field takes, from a closed set of *record with the value
replaced*, *record the statement and its parameters separately*, or *record nothing for the field*. **That
member records a decision; it does not make one** — and for these two fields the decision is already made and
it is redaction. None of this weakens byte-exact SQL parity, because the workflow that measures it,
`persistence-sql-paging-rewrite`, compares statements produced by pure string transforms that never see a row
value.

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
windows and 11 DataWindow definitions in the first, 9 windows and 1 more DataWindow definition in the second,
12 DataWindow definitions repository-wide (C-C). **Capture *from* them; never modify them.** And never copy
one into this tree: a fixture vendored here becomes **a second, divergent oracle**, and the moment the two
disagree there is no way to tell which one the recordings were taken against. The schema enforces the same
rule by omission — it has no member that can hold fixture content.
[`characterization/README.md`](../../README.md) §4.7 and §6.3 own that rule.

### 7.8 Nothing for a deferred service

**No directory here for DesignSystem, Documents, Integration or ScriptBridge.** Not a capture, not a
placeholder, not an empty directory reserving a name. Those four capability areas receive no code, no test and
no container in this phase, so **this half has nothing to produce for them** and no pair could be made (C-D).
[`docs/DEFERRED.md`](../../../docs/DEFERRED.md) is the owner of that roster; the schema's closed
`targetService` enum makes the same point mechanically; and
[§3.3](#33-one-standing-refusal-the-static-map-window) is this folder's worked example of the refusal.

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

Intent only, in this document's own words and pointedly not in the rule's: a candidate is comparable to its
master only for as long as the storage beneath the two of them stays exactly as the seed phase left it, so
that storage is left undisturbed for the whole time a pair is open. **Treat that as a signpost and not as the
instruction** — decisions are made against the canonical text above.

Three related decisions are **cited here, not re-decided**, because each is a documented decision owned
elsewhere (C-K):

| Decision | Owner | What this half needs to know |
| --- | --- | --- |
| The `data-service-db` → `persistence-db` volume rename | [`characterization/README.md`](../../README.md) §2.4 and [`orchestration/README.md`](../../../orchestration/README.md) §7.2 | The rule is stated against the new name in all three copies, so its intent survives the rename |
| The volume's declaration and its single mount | [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) | `persistence-db` is declared there and is mounted by **`persistence-service` alone** — no other service, and no bind mount, touches it |
| The determinism seams | [`docs/PARITY.md`](../../../docs/PARITY.md) §5.1 | The seam register is the authority; a workflow's mask declares which seams it applies and to which sides |

Two operational corollaries that already have owners, noted here only so nobody has to rediscover them while
holding half a pair: between the two halves, tear a stack down with a plain `down` and never with the
volume-removing form ([`orchestration/README.md`](../../../orchestration/README.md) §6.1), and where clones
run in parallel, **both halves of a pair belong to one working tree** with its own Compose project and its own
volume.

**One workflow is exempt, and it is exactly one.** `persistence-sql-paging-rewrite` takes a statement plus a
page size and index and returns statement text: it reads and writes nothing, provisions no database instance
and mounts no volume, so there is no volume state to hold still (C-E). That definition declares the exemption
explicitly — the shared-volume member is set to *does not apply* with a mandatory reason beside it — so the
exemption can never be inferred from silence. Every other identifier in
[§3.1](#31-the-fifteen-permitted-identifiers) is governed by the rule in full, and the exemption holds only
while a workflow reads and writes nothing.
[`characterization/README.md`](../../README.md) §2.7 is the authority for it.

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
- **No file exists in this folder because of a rule.** This document exists because the store's candidate
  half needs a tracked contract at its root ([§1.2](#12-this-is-the-folders-single-tracked-file)); nothing
  here is present only to satisfy a coding guideline.
- **Enterprise-standard best practice applies in the rules' place**, and their absence is not treated as
  licence to lower the bar. Concretely, in this folder: every claim carries a locator into the oracle or a
  named owner upstream, every prohibition carries its mechanism, no upstream text is duplicated where it
  could drift, and no secret value appears in any form.

### 9.2 The constraints that bind this half, and what each one rules

Cited, not reproduced — the plan's inventory is the authority for their wording.

| Constraint | What it requires | Its ruling for this folder |
| --- | --- | --- |
| **C-B** | Behaviour is preserved and defects are reproduced, never corrected | **The sharpest rule for this half**, because a fresh implementation drifts toward correctness by default while the master cannot drift at all. Hence [§5.3](#53-a-recording-must-show-the-defect-and-a-better-looking-result-is-a-failure): a candidate showing corrected behaviour is a FAILURE, and the schema's `provesDefects` is mandatory and non-empty |
| **C-C** | The legacy tree is read-only, and it is the behavioural oracle | Capture **from** the fixtures only. No edit to any of them, and no vendored copy into this tree — [§7.7](#77-no-edit-to-any-fixture-and-no-vendored-copy-of-one). It is also why the ignore trap is worked around by **naming discipline** and the newline difference by the **mask**: the root `.gitignore` and `.gitattributes` are inside the read-only region, so editing either is not an available option — [§4.2](#42-trap-one--seven-unanchored-ignore-patterns) and [§4.6](#46-git-does-not-normalize-newlines-here-and-the-mask-must) |
| **C-D** | Nothing is built for the four deferred services, not even a stub | No directory here for any of them — [§7.8](#78-nothing-for-a-deferred-service) — the standing refusal of the static-map window in [§3.3](#33-one-standing-refusal-the-static-map-window) is that rule applied to a concrete candidate, and [§6.3](#63-deferred-capability-gaps--record-the-headless-output-name-the-gap) is why a deferred rendering half is named rather than synthesized |
| **C-E** | SQLite only; no database is fabricated, and the two other dialects are pure string transforms | Hence the single volume-rule exemption for the paging rewrites in [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated), and hence the only DDL any seed phase here runs is the fixture's own at `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469`. No workflow targets the encrypted-SQLite path, which is out of Phase-1 scope |
| **C-F** | No secret value is carried forward; write-only fields are never echoed; statement text is redacted | The whole of [§7.1](#71-no-secret-value-of-any-kind) through [§7.5](#75-statement-text-and-driver-message-text-are-recorded-redacted-never-raw), including the inline-argument mechanism that makes the crypto workflow the hazard it is, and the signing-secret rule that exists only on this side |
| **C-K** | Every technology- and boundary-specific decision is documented, with its reason | The volume rename, the determinism seams and the redaction shape are **cited as owned decisions** in [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) and [§7.5](#75-statement-text-and-driver-message-text-are-recorded-redacted-never-raw), and are not re-argued here |
| **C-L** | The attached environment's paired-capture rule is preserved verbatim in its owning files | Hence cite-not-restate: [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) names all three owners and adds no fourth copy |

One closing note on precedence, because this file is a junior partner twice over: where this document and
[`docs/PARITY.md`](../../../docs/PARITY.md) disagree, that document is authoritative and this one is stale;
and where this document and [`characterization/recordings/legacy/README.md`](../legacy/README.md) disagree on
a shared convention, the disagreement is itself the defect
([§1.4](#14-the-symmetry-with-the-master-half-is-load-bearing-not-cosmetic)) and is repaired in both files at
once rather than settled in favour of whichever one a reader happened to open.

---

## 10. What has not been exercised — stated plainly

Without softening, and nothing above should be read as implying otherwise:

- **Docker was not installed and no daemon was available in the environment where this migration was
  planned.** The Compose bring-up was therefore never run there, and neither was any paired capture against
  the `persistence-db` volume — the volume that
  [`orchestration/docker-compose.yml`](../../../orchestration/docker-compose.yml) declares and that
  `persistence-service` alone mounts. **No health gate is claimed as passed, and no multi-service stack has
  been started from that manifest.** [`orchestration/README.md`](../../../orchestration/README.md) §10 is the
  authority for the orchestration position, and it records precisely what *has* been exercised on the
  container side.
- **No capture has been taken on this side**, and none on the master side either. There is no
  `<workflowId>/` directory here, no candidate recording and therefore no pair — so **no parity result exists
  and none is claimed anywhere in this document.** No file that later appears in this tree may be presented as
  a verified capture unless it is one.
- **The legacy oracle has not been executed.** Running it needs a PowerBuilder toolchain that is not present,
  which is exactly what keeps the pinyin risk of [§6.1](#61-blocked--pinyin-first-letter-matching) live rather
  than theoretical: the four closed inputs it names can only be characterized from a run of the oracle.
- **A passing service test is not a substitute for a capture.** An in-process test host mounts no volume, so
  it cannot be this half of a pair however thorough it is — the rule of
  [§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated) is expressed against a Docker volume.
  Every service suite passing is a good thing and a different thing.
- **The honesty is enforced and not merely intended.** A workflow definition's execution-status member has
  exactly one legal value, which says the workflow is specified and has not been run, and every definition
  present carries it. A verified run cannot be written down until the value set is widened, and that is a
  deliberate act taken in the same reviewed change that lands the recordings justifying it.

**What was empirically confirmed, so the honesty cuts both ways:** the per-service path of restore, release
build, and test with coverage collection was exercised and passed, emitting `coverage.cobertura.xml` — the
artifact the coverage gate is measured from, and a different question from the one this store answers
([§7.6](#76-no-timing-value-as-an-assertion)).
[`docs/PARITY.md`](../../../docs/PARITY.md) §1.4 and [`docs/BUILD.md`](../../../docs/BUILD.md) are the
authorities for exactly what that covered and what it did not. Within this folder, the trackability of this
file, the seven trapped names of [§4.2](#42-trap-one--seven-unanchored-ignore-patterns), the seven permitted
formats of [§4.1](#41-recordings-are-text-based-and-diffable), the anchored-negation contrast of
[§4.3](#43-the-negations-are-anchored-elsewhere-and-rescue-nothing-here) and the unset newline attributes of
[§4.6](#46-git-does-not-normalize-newlines-here-and-the-mask-must) were each verified by direct probe against
this repository.

[`characterization/README.md`](../../README.md) §7 states the store-wide position,
[`orchestration/README.md`](../../../orchestration/README.md) §10 states the orchestration position, and
[`docs/PARITY.md`](../../../docs/PARITY.md) §1.5 together with its risk register is the authority for what was
and was not verified across the refactor as a whole.

---

## 11. Before you commit a capture

A short checklist, in the order the mistakes actually happen. Every item is a rule stated above; this is the
form to run through rather than a new requirement.

1. **The identifier came from the roster**, spelled exactly as
   [§3.1](#31-the-fifteen-permitted-identifiers) spells it, and the master half spells it the same way.
2. **The directory exists because a capture exists**, not the other way round
   ([§1.3](#13-a-workflowid-directory-is-created-by-a-capture-never-in-advance)) — and it holds a real
   recording rather than a placeholder, because the pair check here is satisfied by any file at all
   ([§4.5](#45-trap-two--the-candidate-side-check-is-not-placeholder-aware)).
3. **`git check-ignore -v` prints nothing** for every file being added
   ([§4.4](#44-detection-is-cheap-so-do-it-before-committing)). If it prints a pattern, rename the file.
4. **Nothing came out of a logging sink** ([§4.2](#42-trap-one--seven-unanchored-ignore-patterns)).
5. **The mask that was applied is the one in the workflow definition**, and it was applied to the master half
   on identical terms ([§5.1](#51-the-capture-phases-observable-outputs-masked-identically-on-both-sides)).
6. **Nothing between the two captures disturbed the volume**
   ([§8](#8-the-shared-volume-capture-rule-is-cited-here-not-restated)). If it did, the pair is void — take
   the workflow again, whole, rather than re-taking this half.
7. **An update capture carries original values as well as current ones**
   ([§5.2](#52-update-workflows-both-the-current-and-the-original-value-of-every-marked-column)).
8. **The defect the workflow exists to prove is visible in the output**, and nothing in it looks more correct
   than the master ([§5.3](#53-a-recording-must-show-the-defect-and-a-better-looking-result-is-a-failure)).
9. **A behaviour that could not be reproduced is recorded as its defined error**, not approximated
   ([§6](#6-two-dispositions-reported-rather-than-approximated)).
10. **No secret value, no token, no credential-bearing URI, no raw statement text, no timing assertion**
    ([§7](#7-what-must-never-be-recorded)).
11. **The master half of the pair is in the same change.** A candidate landing alone is an incomplete pair
    ([§2.3](#23-an-identifier-present-on-one-side-only-is-an-incomplete-pair)), and on this side nothing will
    tell you so.
