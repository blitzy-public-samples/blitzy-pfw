<!-- Markdown lint policy for this file, matching the directive every document this refactor authors
     carries. The rationale in full is docs/BUILD.md section 14, which is the authority; this is only its
     application here. MD013 is 120 rather than the 80-character default, and is disabled for tables and
     code blocks: an evidence row carrying a legacy locator and the behaviour it proves cannot be wrapped
     without splitting the locator from what it proves, and a wrapped command is a command that does not
     run. Prose IS wrapped, and is held to the 120 limit.

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

# PowerFramework → .NET 10 — the paired characterization capture store

This directory is the **evidence store for behaviour preservation**. It holds paired golden-master
recordings: one half produced by the legacy Appeon PowerBuilder framework, one half produced by the .NET 10
implementation that replaces it, joined by a workflow identifier. A pair is the only artifact in this
repository that can settle whether behaviour was actually preserved.

This document is the **operational, in-folder companion** to the parity model. It states how the store is
laid out, how a capture is taken, and what must never enter a recording.
[`../docs/PARITY.md`](../docs/PARITY.md) is the **authoritative parity model** — the fixture inventory, the
determinism seams, the test shape, the catalogue of behaviours to reproduce and the coverage-gate mechanics
all belong to it. Where this document and that one could drift, that one wins.

## Current state of what this document describes

Stated first, because the store's machinery and the store's *results* are in different states and a reader
who conflates them will draw the wrong conclusion from everything below. The labels are the four this
documentation set uses, defined in [`../docs/BUILD.md`](../docs/BUILD.md).

| Artifact | State |
| --- | --- |
| `characterization/README.md` — this document | **Present but unexercised.** It is the specification the captures will be taken against |
| [`workflows/`](workflows) — [`README.md`](workflows/README.md), 15 definitions and [`workflow.schema.json`](workflows/workflow.schema.json) | **Present but unexercised.** Every definition validates against the schema, and each carries its own determinism mask ([§5.1](#51-the-seams)) and the defects its pair must prove. The folder readme holds the canonical roster and the correction register |
| [`recordings/legacy/`](recordings/legacy) | **Present but unexercised**, and empty of recordings. The directory and its [readme](recordings/legacy/README.md) exist; no `<workflowId>` directory and no legacy recording does, because the PowerBuilder oracle has not been executed here |
| [`recordings/dotnet/`](recordings/dotnet) | **Present but unexercised**, and empty of recordings. Same shape, same reason — a candidate with no master to compare against is not worth capturing |

**Fifteen workflows are specified; zero have been captured.** So no paired recording exists, therefore no
parity result exists, and none is claimed anywhere below.
[§7](#7-what-has-not-been-exercised--stated-plainly) states the limitation in full rather than in passing,
and separates the parts that are genuinely blocked from the parts that merely have not been run yet.

## What this document deliberately does not duplicate

Cross-reference rather than restatement is the rule across this documentation set, because a figure
repeated in two places is a figure that will eventually disagree with itself. **One duplication is
deliberate and mandated** — the capture rule of [§2](#2-the-shared-volume-capture-rule) — and
[`../docs/PARITY.md`](../docs/PARITY.md) §4.3 records why it must not be consolidated.

| For | See |
| --- | --- |
| The parity model itself: fixture inventory, determinism seams, test shape, the twelve behaviour groups to reproduce, coverage-gate mechanics, the known risks | [`../docs/PARITY.md`](../docs/PARITY.md) |
| Compose bring-up, the ordered readiness gates, teardown, and the `persistence-db` volume's operator-facing rules | [`../orchestration/README.md`](../orchestration/README.md) |
| The declaration of the `persistence-db` volume, and which service mounts it | [`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) |
| Secret locators, severities and required actions; the token topology; the statement-redaction control | [`../docs/SECRETS.md`](../docs/SECRETS.md) |
| The four-service roster, the transport chosen per service, and the port map | [`../docs/ARCHITECTURE.md`](../docs/ARCHITECTURE.md) |
| Build and test commands, where `coverage.cobertura.xml` lands, and the coverage gate in CI | [`../docs/BUILD.md`](../docs/BUILD.md) |
| Licence text and third-party attribution | [`../README.md`](../README.md) and [`../NOTICE`](../NOTICE) |

## Table of contents

- [1. Purpose and method](#1-purpose-and-method)
- [2. The shared-volume capture rule](#2-the-shared-volume-capture-rule)
- [3. Structure of the store](#3-structure-of-the-store)
- [4. The oracle already exists](#4-the-oracle-already-exists)
- [5. Determinism seams, and the defects a pair must prove](#5-determinism-seams-and-the-defects-a-pair-must-prove)
- [6. What must not appear in this store](#6-what-must-not-appear-in-this-store)
- [7. What has not been exercised — stated plainly](#7-what-has-not-been-exercised--stated-plainly)
- [8. Governing constraints](#8-governing-constraints)

---

## 1. Purpose and method

### 1.1 Why this store exists

The refactor's central mandate is to **preserve behaviour exactly, including documented defects**. Nothing
else in the repository can adjudicate whether that succeeded: the legacy tree is simultaneously read-only
and the *only* statement of intended behaviour, the changelog is stale and is not a specification, and
neither PowerBuilder build definition would build as written. So the legacy tree is the **behavioural
oracle**, and this store holds the recordings that prove the target agrees with it.

### 1.2 The technique, and its one hard prerequisite

The technique is **characterization testing**, also called **golden-master testing**, described by Michael
Feathers in *Working Effectively with Legacy Code* (Addison-Wesley, 2004), chapter 13. You exercise the
existing system, record what it does, and promote that recording to the status of expected output — the
*golden master*. The candidate is then run through identical inputs and compared against the master. A
difference is a finding. **The master is never edited to make a comparison pass.**

The property that makes it the right technique here, rather than a technique chosen for want of a better
one:

> **A characterization test asserts observed behaviour even when that behaviour is wrong.**

That is the replicate-defects-verbatim mandate expressed as a testing method. A conventional unit test
encodes what its author believes the code *should* do; where the legacy is defective, such a test encodes
the *corrected* behaviour and then passes against an implementation that has silently diverged from the
system it replaces.

The technique has exactly one hard prerequisite, and it admits no workaround:

> **The comparison must be repeatable, with every non-deterministic value masked from *both* the master
> and the candidate.**

Both sides. Masking only the candidate leaves the master holding a value that can never be matched;
masking only the master inverts the same problem. Every rule in this document is a consequence of that one
sentence — which is why the rules are stated with their reasons rather than as house style.

### 1.3 Here, a better-looking result is a bug report

This inversion is genuinely counter-intuitive and it is the single most likely way a well-meaning engineer
misreads this store, so it is stated twice — here and again in
[§5.3](#53-the-defect-roster):

> **A recording that shows corrected behaviour is a FAILURE, not an improvement.**

If a comparison shows the .NET side producing output that looks *more correct* than the legacy side, the
finding is a divergence in the .NET implementation and it is filed as a defect. Correcting a legacy defect
is forbidden (C-B). A defect is reproduced and annotated at its point of reproduction, never repaired.

### 1.4 This document is the companion, not the model

To be unambiguous about precedence: [`../docs/PARITY.md`](../docs/PARITY.md) defines the model and this
document applies it inside this folder. If a count, a seam, a defect or a coverage figure appears in both
and they disagree, `PARITY.md` is correct and this file is stale — fix this file, and do not "reconcile"
the model to match it.

---

## 2. The shared-volume capture rule

This is the most operationally important section in this document. It is the attached environment's
binding instruction (C-L), and it is independently what the technique of
[§1.2](#12-the-technique-and-its-one-hard-prerequisite) requires.

### 2.1 The rule, canonical text

Reproduced **word for word** from [`../docs/PARITY.md`](../docs/PARITY.md) §4.2, which is the canonical
text. That text is itself the environment's own two sentences with one substitution and no other change —
every occurrence of the volume name `data-service-db` becomes `persistence-db`, for the reason
[§2.4](#24-the-volume-is-named-persistence-db-and-the-rename-is-deliberate) records:

> **Persistence rule: legacy characterization recordings (PowerBuilder behavioral oracle output, keyed
> per workflow ID) and this scaffold's target-side recordings must be captured against the SAME
> `persistence-db` Docker volume state for a given workflow ID comparison to be valid. Do not recreate or
> reseed `persistence-db` between the legacy-side capture and the .NET-side capture for the same workflow
> ID, or the paired recordings required by the Agent Action Plan's success criteria will not be
> comparable.**

Nothing above is paraphrase. It is not shortened, not re-ordered, not re-punctuated and not summarised.

### 2.2 The same rule as the migration plan states it

The migration plan carries its own condensation of the identical rule, and it is reproduced here verbatim
too, because it is the form the plan's success criteria are written against:

> For a given workflow identifier, the legacy-side and target-side captures must be taken against the
> **same `persistence-db` volume state**, and the volume must not be recreated or reseeded between them,
> or the paired recordings are not comparable.

The two quotations state one rule, not two. Where they differ in length they do not differ in effect: both
require the *same* volume state across both halves of one workflow identifier, and both make a recreated
or reseeded volume fatal to the pair.

### 2.3 Wording provenance, recorded rather than reconciled

Two renderings appear above because two authoritative sources carry two lengths, and quietly picking one
would be exactly the silent reconciliation this documentation set forbids. The provenance, so a reader can
audit it:

| Rendering | Carried by | Status |
| --- | --- | --- |
| The environment's full wording, volume name substituted | [`../docs/PARITY.md`](../docs/PARITY.md) §4.2 and [`../orchestration/README.md`](../orchestration/README.md) §7.2 — verified byte-identical to each other | **Canonical.** Copy this text; do not re-derive it |
| The migration plan's condensation | The plan's own parity-evidence section, and [§2.2](#22-the-same-rule-as-the-migration-plan-states-it) above | A faithful restatement of the canonical rule, quoted verbatim |
| A capitalised restatement in the manifest that declares the volume | [`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml), in the decision comment above `volumes:` | A third restatement, placed where the volume is declared |

**The rule therefore appears in exactly three documents — this one, `../docs/PARITY.md` and
`../orchestration/README.md` — and the canonical text of [§2.1](#21-the-rule-canonical-text) is identical
in all three.** An edit to one is an edit to all three. If a future reader finds them disagreeing, that
divergence is a defect to report and repair deliberately, not to settle in favour of whichever copy they
happened to open.

### 2.4 The volume is named `persistence-db`, and the rename is deliberate

The attached environment names the persistence volume after a `data-service`, one of the five directories
in its own placeholder roster — a roster its own STEP 0 labels placeholders lifted from a *not
prescriptive* example grouping. The service that actually owns storage in this phase is
`persistence-service`, so the volume is renamed to match its owner: a volume named after a service that
this phase never builds is a standing invitation to mount it on the wrong thing.

The volume is declared in [`../orchestration/docker-compose.yml`](../orchestration/docker-compose.yml) and
is mounted by **`persistence-service` alone**. No other service, and no bind mount, touches it. Restating
the rule against the new name in this file, in [`../docs/PARITY.md`](../docs/PARITY.md) and in
[`../orchestration/README.md`](../orchestration/README.md) is how the rule's intent survives the rename:
renaming a volume in an instruction while paraphrasing the instruction is precisely how an operational
rule quietly loses its force.

### 2.5 Breaking it is dangerous because nothing errors

This is not merely local compliance, and the distinction matters under pressure. Repeatability is the
golden-master technique's own hard prerequisite ([§1.2](#12-the-technique-and-its-one-hard-prerequisite)),
and the input state of a workflow that touches storage *is* part of its input. A volume reseeded between
the two captures changes that input, so the two recordings answer two different questions and the diff
between them is noise wearing the costume of a finding.

> **Breaking the rule silently invalidates every comparison taken across the break. Nothing fails,
> nothing warns, and the pairs simply stop meaning anything.**

An operator who understands *why* the rule exists will not be tempted to work around it, because the
work-around does not produce a weaker comparison — it produces no comparison. In operational terms, for
one workflow identifier: capture the legacy side, capture the target side, and run nothing between them
that destroys, recreates, re-initializes or re-seeds the volume. **If the volume state has moved, the pair
is void — discard both halves and start the workflow again.** Do not repair a broken pair by re-taking one
half of it; a pair re-taken in halves is two recordings of two different worlds.

One corollary for parallel work: **both halves of a pair belong to one working tree.** Where several
clones run concurrently, each needs its own Compose project and its own volume, so that one clone's
teardown cannot invalidate another clone's half-finished pair.

### 2.6 The teardown corollary

`docker compose down -v` removes named volumes, and so does `docker volume rm`. Either one discards the
provisioned schema and every row in it — and therefore invalidates any paired capture taken against that
state. **Between the two halves of a pair, tear the stack down with a plain `down`, never with `-v`.**
[`../orchestration/README.md`](../orchestration/README.md) §6.1 is the authority for teardown and states
the same warning at the point of use.

### 2.7 The one exemption, stated so it is not ambiguous

Some workflows touch **no storage at all**, and those are exempt from this section. The clearest case is
the SQL Server and Oracle paging behaviour: **no instance of either dialect is provisioned, required or
reachable** anywhere in this refactor (C-E). Neither has a schema, a connection string or any DDL in the
repository; only two dialect constants and two statement generators exist, and those are characterized as
**pure string transforms** — statement plus page size and index in, statement text out.

Such a workflow needs no database, mounts no volume, and therefore has no volume state to keep still. The
exemption is narrow and it is not a loophole: it applies to a workflow **only** when the workflow reads
and writes nothing. The moment a workflow touches `persistence-db`, [§2.1](#21-the-rule-canonical-text)
applies to it in full.

---

## 3. Structure of the store

### 3.1 Layout

```text
characterization/
├── recordings/
│   ├── legacy/                 README.md only; <workflowId>/ appears when a capture lands
│   │   └── <workflowId>/       PowerBuilder oracle output (none exists yet)
│   └── dotnet/                 README.md only; same shape, same state
│       └── <workflowId>/       target-side output (none exists yet)
├── workflows/                  15 definitions, one per workflow, each carrying its own mask
│   ├── README.md               the canonical roster and the correction register
│   └── workflow.schema.json    the schema every definition validates against
└── README.md                   this file
```

| Path | Contents |
| --- | --- |
| [`workflows/`](workflows) | The 15 workflow definitions, each **with** its determinism mask, plus the schema they validate against and the roster readme |
| [`recordings/legacy/`](recordings/legacy) `<workflowId>/` | The PowerBuilder oracle's output for that workflow — the golden master. The parent exists; no `<workflowId>` directory does |
| [`recordings/dotnet/`](recordings/dotnet) `<workflowId>/` | The target-side output for the same workflow — the candidate. Likewise |
| `characterization/README.md` | This document |

**The 15 workflows, and how they distribute.** Six characterize DataServices, five Persistence, one Gateway,
one Security, and two the shared libraries — `shared-diagnostics-assert-payload` and
`shared-eventful-broker-ordering`. Fourteen are subject to the capture rule of
[§2.1](#21-the-rule-canonical-text); exactly one, `persistence-sql-paging-rewrite`, declares itself exempt
for the reason [§2.7](#27-the-one-exemption-stated-so-it-is-not-ambiguous) gives. Every definition names its
oracle fixtures by repository locator and every one declares a non-empty mask.
[`workflows/README.md`](workflows/README.md) is the authority for the roster and for why a capability with
no oracle window has no workflow.

### 3.2 `<workflowId>` is the pairing key

The `<workflowId>` segment is the join key, and it is the only thing that makes two recordings comparable.
The same identifier names one directory on each side, and a pair is meaningful **only** when both halves
were captured under the rule of [§2.1](#21-the-rule-canonical-text).

> **A recording whose workflow identifier does not exist on the other side is not a partial comparison — it
> is not a comparison at all, and it must not be reported as a pass.**

**The naming convention is fixed in two places that agree on the principle and differ on one spelling, and
the divergence is registered rather than glossed over.**

The principle both hold to is that an identifier must be traceable to its oracle: **an invented identifier is
arbitrary, a window-derived one is checkable.** The canonical grammar is
[`workflows/workflow.schema.json`](workflows/workflow.schema.json)'s `workflowId` pattern — lower-case
alphanumeric segments separated by single hyphens, derived from the capability area plus the legacy oracle
window with that window's underscores rendered as hyphens. The 15 roster identifiers in
[`workflows/README.md`](workflows/README.md) all conform, and **that roster is the only source of a
directory name in this store.**

The one divergence is a test constant.
`services/dataservices-service/PowerFramework.DataServices.Tests/PinyinFirstLetterMatcherTests.cs` pins
`WorkflowId` to the oracle window's own name, `w_test_dwsvc_dropdownsearch`, and derives its two recording
paths from it — a spelling the schema pattern rejects, because it carries underscores. **The roster
identifier `dataservices-dwsvc-dropdownsearch` is canonical**; the correction register in
[`workflows/README.md`](workflows/README.md) owns the reconciliation and records when the constant changes,
which is the moment a real recording lands rather than now, because renaming a pairing key that no recording
uses yet buys nothing and renaming one that a recording *does* use orphans both halves silently.

That same test class is still the worked example of how paths into this store are resolved: the repository
root is located by walking up to the `PowerFramework.slnx` marker, and the two directories are spelled with
forward slashes exactly as this document writes them.

### 3.3 The mask lives with the workflow, not with a recording

`workflows/` holds each workflow definition **together with its determinism mask**, so a mask is versioned
alongside the workflow it applies to rather than living inside test code or beside one of the two
recordings. The placement is deliberate and it is load-bearing: a mask stored beside one half could be
applied to that half alone, and **a mask applied to one side only is worse than no mask at all**, because
it produces a passing comparison out of mismatched data. One mask, one workflow, applied identically to
both captures.

### 3.4 Recording-file conventions

Recordings must be **text-based and diffable**, so that a parity failure is legible in review rather than
merely detectable. A reviewer has to be able to see *what* differed, not just that something did.

- **Use** `.json`, `.jsonl`, `.txt`, `.csv`, `.sql`, `.md` or `.yaml`.
- **Prefer a stable serialization order** inside a recording, for the same reason the modify-call ordering
  seam exists ([§5.1](#51-the-seams)): an unpinned order surfaces as a difference where no behaviour
  changed.
- **Keep one workflow's output in its own directory** and do not share files between workflow identifiers.
  A file that two pairs both read is a file that a change to either pair can invalidate.

### 3.5 The extension trap — `.log` and the archive patterns

> **Never name a capture `*.log`, and never archive one as `*.zip`, `*.rar`, `*.bak` or `*.dmp`.**

The reason is specific and it is the kind that costs an afternoon. The repository's root `.gitignore`
carries those patterns **unanchored**, so they match at every depth — including inside this directory —
alongside `*.bat` and `thumbs.db`. Its only negations are anchored elsewhere (`/res` and `/samples`), so
they do not rescue anything here. A capture named that way is therefore **silently untracked**: it exists
on the machine that produced it, `git status` says nothing about it, and it is simply absent from review
and from CI. The comparison looks like it was taken and cannot be found by anybody else.

**The root `.gitignore` is not to be modified** — the fix is to name the file correctly, not to weaken a
repository-wide ignore rule to accommodate a badly named one.

### 3.6 The placeholder trap — an ordinary file can activate a skipped test

There is a second silent trap, in the opposite direction, and it is why this store ships with **no
pre-created `<workflowId>` directory** under either side of `recordings/` — and why the two files it does
ship there are named `README.md`, which the predicate below treats as a placeholder by design.

The hook named in [§3.2](#32-workflowid-is-the-pairing-key) is a skipped test matrix — one case per closed
input of [§5.5](#55-the-one-genuine-parity-risk--pinyin-first-letter-matching) — that **un-skips itself as
soon as a legacy recording appears** for its workflow. Its activation predicate is precise: the legacy
directory must exist *and* contain at least one file that is neither dot-prefixed nor named `README.md` —
dot-files and a readme are treated as placeholders and deliberately keep the matrix skipped. Once
activated, the matrix asserts the pair rule of [§3.2](#32-workflowid-is-the-pairing-key) and that the
shipped expression evaluator no longer carries its BLOCKED pinyin matcher.

The consequences are worth being blunt about:

- **A `.gitkeep` or a `README.md` inside `recordings/legacy/<workflowId>/` is safe.** It keeps the matrix
  skipped, which is the correct state while the oracle is unexercised.
- **Any other file in that directory activates the matrix.** If it was dropped there as scaffolding rather
  than as a real capture, the suite turns a truthfully reported BLOCKED state into a failing build.
- **Landing a real recording is therefore a review moment, not a file copy.** The activating half, the
  paired half and the composition change move together.

### 3.7 Newline normalization is part of the mask, not an accident

The legacy master is produced on Windows by PowerBuilder — the framework's own assertion payload is split
on CRLF — while the candidate runs in a Linux container. Raw line endings therefore differ **by platform,
not by behaviour**, and a byte-exact comparison of unnormalized text reports a difference on every single
line of every multi-line recording.

**Declare an explicit newline-normalization convention in the workflow's mask and apply it to both sides.**
It belongs in the mask precisely because it is a platform artifact rather than a behaviour, masked
symmetrically for the same reason the seams of [§5.1](#51-the-seams) are.

Two notes so this is not solved in the wrong place. The repository's root `.gitattributes` declares only
`linguist-language` mappings and carries **no `text` or `eol` rules**, so Git performs no line-ending
translation here; **changing that file is not in scope.** And normalization is a comparison-time
convention: it does not license editing a stored master, which [§1.2](#12-the-technique-and-its-one-hard-prerequisite)
forbids.

---

## 4. The oracle already exists

### 4.1 It needs no invention

A characterization effort normally begins by constructing inputs that drive the legacy system through its
interesting states. Here that work is already done: the legacy ships two libraries of exercise windows,
each bound to DataWindow definitions that configure the very behaviours a pair has to prove.

> **The DataWindow corpus is the test corpus.**

Verified counts, and they are exact rather than approximate:

| Source | Windows | DataWindow definitions | Objects in total |
| --- | --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src` | **47** `w_test_*.srw` | **11** `.srd` | 68 |
| `ws_objects/pfw.demos.pbl.src` | **9** `.srw` | **1** `.srd` (`dw_test.srd`) | 42 |

Reproduce them from the repository root:

```bash
ls ws_objects/pfw.tests.pbl.src/w_test_*.srw | wc -l   # -> 47
ls ws_objects/pfw.tests.pbl.src/*.srd        | wc -l   # -> 11
ls ws_objects/pfw.demos.pbl.src/*.srw        | wc -l   # -> 9
ls ws_objects/pfw.demos.pbl.src/*.srd        | wc -l   # -> 1
find ws_objects -name "*.srd"                | wc -l   # -> 12
```

In `pfw.tests` every window matches `w_test_*`, so the 47 is both the pattern count and the total and no
window is unaccounted for. **Twelve `.srd` definitions exist repository-wide**, which is what makes the
primary-fixture claim of [§4.2](#42-the-primary-fixture-dw_sqlitesrd) auditable rather than asserted.

### 4.2 The primary fixture: `dw_sqlite.srd`

`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` is 37 lines long, declares `release 12.5;` at `:L2`, and is
**the only updatable DataWindow among all twelve in the repository.** That claim was tested rather than
assumed — exactly one of the twelve contains `updatewhere=`:

```bash
grep -rl "updatewhere=" --include="*.srd" ws_objects/   # -> ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
```

Its table specification at `:L14` reads as follows, reproduced exactly — including the leading space, the
**double space** before `sort=` and the **trailing space** inside the sort value, because parity here is
measured on exact text:

```text
 retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no  sort="age A salary A " )
```

All six columns carry `update=yes updatewhereclause=yes` at `:L8-L13`:

| Position | Column | Declared type | Extra flags |
| ---: | --- | --- | --- |
| 1 | `id` | `number` | **`key=yes identity=yes`** [`:L8`] |
| 2 | `name` | `char(100)` | — [`:L9`] |
| 3 | `age` | `number` | — [`:L10`] |
| 4 | `address` | `char(200)` | — [`:L11`] |
| 5 | `salary` | `decimal(2)` | — [`:L12`] |
| 6 | `birth` | `date` | — [`:L13`], with `editmask.mask="yyyy-mm-dd"` on its column control at `:L26` |

And a footer computation at `:L27` carries `expression="sum(salary for page)"`, so the fixture exercises
the expression engine as well as the update path — and specifically a **page-scoped** aggregate, the harder
of the aggregate forms because its scope is a paginated presentation concept rather than a result-set one.

### 4.3 What a recording of an update workflow must carry

`updatewhere=1` is the key-plus-updateable-columns concurrency mode: the generated statement's where-clause
carries the key column **plus the original values of every marked updateable column**. With all six columns
marked, the optimistic-concurrency check therefore spans **all six columns' original values**.

> **A recording of an update workflow must carry both the current and the original value of every marked
> column, per row. A flat rowset capture is insufficient — it silently discards the state the concurrency
> contract is built on.**

### 4.4 The seed phase and the capture phase are separate

**State this explicitly, because it is easy to get wrong and the oracle's own setup fights the rule.** The
fixture's `CONNECT` button drives a destructive setup: its `clicked` event at
`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L448` calls `FileDelete("test.db")` at `:L450` *before*
opening the database at `:L456`, then creates the `COMPANY` table at `:L463-L469`. That delete-and-recreate
is precisely the reseed [§2.1](#21-the-rule-canonical-text) forbids between the two halves of a pair, and
running the oracle window's setup a second time to "start clean" is the single likeliest way a well-meaning
engineer voids a comparison.

A workflow definition therefore separates two phases:

| Phase | What it does | When it runs |
| --- | --- | --- |
| **Seed** | The file delete, the DDL, and any row seeding — everything that establishes the starting state | **Once, before the pair begins.** Never between its two halves |
| **Capture** | The behaviour under characterization, and nothing else | **Twice** — once on the legacy side, once on the target side, both against the already-seeded state |

On the target side the equivalent seeding step is the schema provisioning described in
[`../docs/BUILD.md`](../docs/BUILD.md) §5.6. It is idempotent and non-destructive — re-running it reports
that the database is already up to date and leaves existing rows byte-identical — which is exactly what
makes it safe to re-run between the two halves of a pair, unlike the legacy fixture's own setup.

**Persistence now performs that step ITSELF on the documented Compose bring-up, and that does not weaken the
rule.** `Schema__ApplyMigrationsOnStartup` is true in the manifest, so the service applies its pending
migrations at startup rather than waiting for an operator command. What it calls is `Database.Migrate` and
nothing else: on an already-current schema it performs no write at all and reports that it found nothing
pending, so a container restart in the middle of a pair neither recreates nor reseeds the volume — the two
things [§2.1](#21-the-rule-canonical-text) actually forbids. The distinction worth holding onto is that the
rule is about the VOLUME's state, not about which process establishes it: what would break a pair is
recreating the volume, re-running the legacy fixture's own `FileDelete` and DDL, or reseeding rows, and none
of those is on this path.

**A capture operator who wants the schema step out of the picture entirely can have that**, without editing
the manifest: set `PERSISTENCE_APPLY_MIGRATIONS_ON_STARTUP=false` in `orchestration/.env` and provision once,
before the pair begins, exactly as the Seed row above prescribes. Persistence's own settings default the
switch to false, so the opted-out behaviour is the code default rather than an override — which is the point
of that default: a parity run's reliance on an untouched volume never depends on remembering to disable
something.

### 4.5 The connection URI grammar the fixture documents

The same fixture documents its connection grammar in its own comments at `:L452-L455`, and a workflow that
opens storage reproduces it:

| Element | Meaning |
| --- | --- |
| `test.db?mode=rwc` | Read-write-create, the form the fixture opens at `:L456`; an optional password parameter sits in the same position and the repository carries no value for it |
| `check[=quick]` | Integrity check, with a quick variant |
| `journal[=DELETE\|TRUNCATE\|PERSIST\|MEMORY\|WAL\|OFF]` | Journal mode across six values, defaulting to `DELETE` |

**The assembled URI is itself sensitive.** Where a password parameter is supplied, the URI *is* a
credential: it is never logged, never returned in an error payload and **never captured into a recording**
([§6.2](#62-no-secret-value)).

### 4.6 Fixture-to-capability map

Only a minority of the corpus is in scope, and naming which prevents both over-capture and under-capture.

| Capability area | Oracle windows and fixtures |
| --- | --- |
| DataServices | `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_rowselect.srw`, `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnexp.srw`, `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_columnsort.srw`, `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_contextmenu.srw`, `ws_objects/pfw.tests.pbl.src/w_test_dwsvc_dropdownsearch.srw`, with the `dw_test_dwsvc*.srd` and `dw_svc_sample*.srd` fixtures in the same directory |
| Persistence | `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` (with `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd`), `ws_objects/pfw.tests.pbl.src/w_test_thread_sqlquery.srw`, `ws_objects/pfw.tests.pbl.src/w_test_sqlparser.srw`, `ws_objects/pfw.tests.pbl.src/w_test_thread.srw` |
| Shared libraries | `ws_objects/pfw.tests.pbl.src/w_test_assert.srw` (Diagnostics), `ws_objects/pfw.tests.pbl.src/w_test_eventful.srw` (the event broker), `ws_objects/pfw.tests.pbl.src/w_test_static_map.srw` (the ordered map) |
| Gateway | **No dedicated test window.** The lifecycle oracle is `ws_objects/pfw.pbl.src/pfw.sra`, driven with `ws_objects/pfw.demos.pbl.src/w_demo_selector.srw` |
| Security | **No oracle test window exists at all** — see the risk below |

**Security has no oracle test window, and that is a named risk rather than an omission.** There is no
`w_test_crypto.srw` among the 47. The only oracle for the cryptographic surface is
`ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru` — and that object is **itself one of the
repository's hardcoded-secret sites**. Characterizing the cryptographic surface therefore means driving a
secret-bearing fixture, so:

> **Drive it with injected test key material only, and never echo, capture or store the key material
> embedded in that fixture.**

### 4.7 Both source libraries are read-only

`ws_objects/pfw.tests.pbl.src` and `ws_objects/pfw.demos.pbl.src` are **REFERENCE and
characterization-fixture source only — never ported as-is, never edited** (C-C). They are permanently out
of scope as *ported code* precisely so that they can serve as fixtures. Capture from them; do not modify
them, and do not copy them into this tree ([§6.3](#63-no-edit-to-any-fixture)).

---

## 5. Determinism seams, and the defects a pair must prove

### 5.1 The seams

Non-deterministic values must be masked from **both** the master and the candidate
([§1.2](#12-the-technique-and-its-one-hard-prerequisite)). Masking requires knowing exactly where variation
enters, so the sources are enumerated rather than described in the abstract. Each is to be **injected** — a
dependency a test replaces with a deterministic double while production takes the platform implementation.
[`../docs/PARITY.md`](../docs/PARITY.md) §5.1 is the authoritative register: it carries the injection point
and the doubles for each of the first four rows below, and their statuses are reproduced from it. The fifth
row is this document's own and is stated with the same discipline. **The status column is kept now that all
five rows agree**, because "present" and "exercised" are the distinction it draws: each seam has a named
injection point in source and a double in a unit test, and **none has yet carried a paired capture** — which
is the only thing that would make it exercised in this store's sense.

| Seam | Where variation enters | Status |
| --- | --- | --- |
| GUID generation, random string generation, random blob generation | Security's cryptographic surface — the **primary** non-determinism sources in the in-scope estate. Every value differs on every run by design | **Present but unexercised** |
| Transaction-pool idle expiry | CPU-clock based, keyed on the two keep-alive settings. Whether a pooled transaction is reused or discarded is observable, so the decision must be reproducible even though the elapsed time itself is never asserted | **Present but unexercised** — `Transactions/TransactionPool.cs` takes one injected `TimeProvider` and reads no ambient clock, so a fake drives every expiry; no capture has gone through it |
| Every clock read | Anywhere a timestamp reaches an output or a decision. A timestamp in recorded output differs on every run | **Present but unexercised** |
| Modify-call ordering | The DataWindow modify path, where a sequence of property modifications reaches a target state. Two orderings that reach the same state can emit different intermediate output | **Present but unexercised** |
| Line endings | A platform artifact rather than a behaviour: the master is produced on Windows and the candidate in a Linux container, so the normalization convention of [§3.7](#37-newline-normalization-is-part-of-the-mask-not-an-accident) is declared in the workflow's mask and applied to both sides | **Present but unexercised.** All 15 masks carry a line-endings entry, and each names the multi-line fields it bites on for that workflow |

### 5.2 A mask is not a substitute for a seam

Two distinctions worth keeping sharp, because conflating them is how a suite quietly loses its ability to
detect anything:

- **Masking removes a field from the comparison; seaming makes the field reproducible.** Prefer the seam.
  Mask only where a value cannot be made deterministic without changing observable behaviour — every masked
  field is a field in which a regression can hide.
- **A mask applies symmetrically or not at all**, which is the reason
  [§3.3](#33-the-mask-lives-with-the-workflow-not-with-a-recording) puts it with the workflow rather than
  beside either recording.

And determinism is a property of the **pair**, not of a run: a single reproducible run proves nothing. The
question a seam answers is not "does this run twice the same?" but "do the recorded legacy value and the
produced target value agree after identical masking?" Configure each seam **once per workflow** and use
that configuration for both captures, alongside the volume state [§2.1](#21-the-rule-canonical-text) pins.

### 5.3 The defect roster

The store exists to prove that the defects **survived**. Restating the inversion of
[§1.3](#13-here-a-better-looking-result-is-a-bug-report) at the point where it bites:

> **A recording that shows corrected behaviour is a FAILURE, not an improvement. Here, "the output looks
> more correct" is a bug report.**

These are the behaviours paired captures must demonstrate. [`../docs/PARITY.md`](../docs/PARITY.md) §7 is
the authoritative catalogue — twelve groups, each with its locators — and this list is the reviewer's
summary of what a pair is *for*, not a second catalogue:

- **The tri-state return algebra.** A prevention reads as a success; cancelled and null are **neither**
  succeeded nor failed. The boolean overloads make prevent and failed indistinguishable while the numeric
  forms keep them distinct — so a recording must carry the numeric code, never a boolean projection of it.
- **The four-value item-change alphabet** `{0,1,2,3}`, four distinct arms: `case 1` is an **EMPTY arm that
  does NOT fall through** [`se_cst_dw.sru:L212`] — PowerScript `choose case` is not a C `switch`, so `1`
  returns with value and status **untouched** and the restore belongs to the `ItemValidationError` handler
  that returning 1 raises; `case 2` restores value and status, but only if the earlier equality test held;
  `case 3` keeps the value, does not move focus, and rewrites its result to 1; and the default arm coerces
  by column type and then **forcibly returns 2**. It is its own alphabet and is never mapped onto the
  return-code algebra. A recording that shows `1` restoring is a port that read the empty arm as a
  fall-through — [`../docs/PARITY.md`](../docs/PARITY.md) §7 step 6 and
  [`../docs/CONTRACTS.md`](../docs/CONTRACTS.md) §6.5 settle it the same way.
- **The tri-valued broker veto** — prevent-once, prevent-deep, and continue — never flattened to a boolean,
  because flattening silently converts a deep prevention into a shallow one.
- **The silent-passthrough localization fallback.** With no provider installed the text is returned
  unchanged: never throwing, never logging, never marking the string untranslated. Alongside it, the **two
  mistranslations** in the resource table are reproduced, and the commented-out pre-resource table that
  holds the *correct* wording must not be revived.
- **Static versus dynamic expansion.** A static reference stays permanently fixed at its bind-time value
  while a dynamic reference re-evaluates at calculation time — so a recording must carry the unexpanded
  expression, the bind-time snapshot and the live environment, with the expansion mode tagged per
  reference. An already-expanded string makes a static binding indistinguishable from a literal.
- **The four cross-thread transfer defects** in the buffer codecs, including row loss on a sorted
  multi-block carrier and the prohibition on using a reset to clear data.
- **The self-assignment workaround** in the update path — the legacy assigns a modified key column to
  itself purely to flip its item status — and the **inverted filter-buffer iteration** that runs backwards
  because that buffer's row order is inverted relative to the source. Both look like bugs and are not:
  "correcting" the iteration direction produces wrong data that a row-count assertion still passes.
- **Byte-exact generated SQL** from the paging rewriters, including every sentinel identifier and the
  count-wrapper alias. These are **pure string transforms** needing no instance of either dialect, which is
  the exemption [§2.7](#27-the-one-exemption-stated-so-it-is-not-ambiguous) records.
- **The fail-fast posture.** Worker-session creation failure is fatal, and a decoded assertion failure
  terminates the application after unpacking a seven-field payload. Never graceful degradation — a warning
  where the legacy halted is a behavioural change dressed as robustness.

### 5.4 What is compared, and what is never compared

Compared: returned values and return codes; generated SQL text; DataWindow buffer contents including
original values; item statuses; event sequences and their ordering; structured error payloads field by
field; identity values and their array positions; expression results.

**Never compared: execution time** ([§6.4](#64-no-performance-measurement)).

### 5.5 The one genuine parity risk — pinyin first-letter matching

Stated without softening, because an approximation here is worse than a documented gap.

Chinese pinyin first-letter matching **cannot be proven bit-exact from the repository alone.** The
boundary of the risk is narrower than it first looks and getting it right decides how much characterization
is needed:

- **The flag value the call site passes needs no characterization.** The flag constants are declared in
  `ws_objects/pfw.shared.pbl.src/enums.sru:L1146-L1149` under a comment naming the function, so the value
  `7` passed at the drop-down search call site decodes as ignore-case, ignore-width and fuzzy-sound all
  enabled. [`../docs/PARITY.md`](../docs/PARITY.md) R1 records the same decoding.
- **What remains unprovable is enough to keep the risk live.** The lookup table exists **only inside the
  closed `pfw.dll`** — there is no table, no data file and no source mapping any character to its pinyin
  initial anywhere in the tree, and no C++ source for the native library at all. The matching relation
  (prefix, substring or subsequence; the treatment of non-Han and multi-reading characters) is unspecified.
  The fuzzy-sound equivalence set is illustrative rather than provably closed. And the default flag set of
  the two-argument overload is unrecorded.

Bit-exact parity therefore requires characterizing **the lookup table, the matching relation, the
fuzzy-equivalence set and the two-argument default flags** from this oracle, over a deliberately broad
input set. And when the oracle cannot be exercised:

> **Report the pinyin filter as BLOCKED. Do not approximate it.**

The reasoning is specific rather than cautious. A substitute pinyin table gets most characters right and a
minority wrong, so an approximated filter returns **subtly different result sets**: mostly correct,
occasionally missing a row, occasionally including one. That is a regression that looks like correct
behaviour, it passes review, and it is found by a user rather than by a test. A BLOCKED report is a known
gap; an approximation is an unknown one. It is also why no third-party pinyin package was selected. The
four skipped theory cases described in [§3.6](#36-the-placeholder-trap--an-ordinary-file-can-activate-a-skipped-test)
are the reportable form of that BLOCKED state, and they are waiting on exactly these four closed inputs.

### 5.6 Encrypted SQLite is out of Phase-1 scope

The repository ships a plain and a cipher-enabled native SQLite library at materially different versions,
the cipher one being the older; its key-derivation and per-page integrity options are not reachable through
any framework API, and the target provider tracks a current SQLite and so cannot produce the older page
format at all. **The unencrypted path only is provisioned, and no workflow in this store targets the
encrypted one.** [`../docs/PARITY.md`](../docs/PARITY.md) R3 is the authority.

---

## 6. What must not appear in this store

### 6.1 Nothing for a deferred service

**No recording, no workflow and no determinism mask exists for DesignSystem, Documents, Integration or
ScriptBridge.** Those four capability areas receive no code, no test and no partial implementation in this
phase — not even a stub — so there is nothing to characterize: a pair needs a candidate as well as a
master, and they have no candidate.

Consequently **no workflow targets** any of the following fourteen windows, all of them under
`ws_objects/pfw.tests.pbl.src/` — `w_test_websocket_mqtt.srw`, `w_test_json.srw`, `w_test_xml.srw`,
`w_test_zip.srw`, `w_test_barcode.srw`, `w_test_qrcode.srw`, `w_test_regex.srw`, `w_test_logger.srw`,
`w_test_filescanner.srw`, `w_test_devinfo.srw`, `w_test_ftpclient.srw`, `w_test_websocket.srw`,
`w_test_compiler.srw` and `w_test_invoker.srw` — nor any sciter, blink, webview or visual-control window,
nor the `dw_barcode.srd` and `dw_qrcode.srd` definitions that belong to a deferred capability area. Naming
them makes the boundary enforceable rather than aspirational.
[`../docs/DEFERRED.md`](../docs/DEFERRED.md) carries the four destinations and their assigned objects.

### 6.2 No secret value

**No recording may capture, echo or store key material, a certificate, a password or a token.** This is a
live risk rather than a theoretical one for one specific reason: **several of the repository's in-source
hardcoded-secret sites live in `ws_objects/pfw.tests.pbl.src/` and `ws_objects/pfw.demos.pbl.src/` — the
very libraries that supply these fixtures.** The secret-bearing fixtures are
`ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw`,
`ws_objects/pfw.tests.pbl.src/n_cst_appconfig.sru` and
`ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru`. The first is additionally excluded by
[§6.1](#61-nothing-for-a-deferred-service) and the second by capability scope; the third is the Security
oracle of [§4.6](#46-fixture-to-capability-map), which is why that section requires injected test key
material.

[`../docs/SECRETS.md`](../docs/SECRETS.md) is the full locator inventory, with a severity and a required
action per site. **Cite it; reproduce no value in any form** — not a key, not a certificate, not a
credential, not a fragment of one, and not "an obviously expired" one.

Three field-level rules follow from the same constraint and they apply to every recording:

- **The transaction descriptor's password field is write-only.** The legacy in-process accessor moves it in
  both directions; across a boundary the outbound half would place a database password into a response
  body, and therefore into any log of one. It is accepted inbound and **never populated outbound — never
  echoed in a response, never logged, therefore never recorded.**
- **The connection URI is a credential when it carries one.** Its optional password parameter's value comes
  from configuration injection, and the assembled URI is never logged, never returned in an error payload
  and never captured ([§4.5](#45-the-connection-uri-grammar-the-fixture-documents)).
- **The database error payload's statement field is recorded redacted.** The legacy carries the complete
  generated statement with interpolated literals in that field — and, where the connection disables bind
  variables, the runtime interpolates values directly rather than binding them, so every parameter value
  the failing statement touched is present as a literal. The legacy logger performs **no** redaction at
  all. The published contract therefore carries **one redacted string field and nothing beside it**: the
  alternative of splitting it into statement-plus-parameters was considered and **rejected**, because a
  parameter collection is itself the sensitive data, so splitting would produce two fields to redact
  instead of one. [`../docs/SECRETS.md`](../docs/SECRETS.md) §6 is the record of that decision.

The generalisation, stated once so a field not yet enumerated is still governed: **any field whose value is
a credential, a key, or a statement containing interpolated literals is write-only inbound and redacted
outbound — and is therefore never recorded.**

### 6.3 No edit to any fixture

The 47 test windows, the 12 DataWindow definitions and both source libraries are read-only (C-C).

> **Capture from them; never modify them.**

No fixture is edited, moved, renamed, reformatted or re-encoded, and **no copy of one is vendored into this
tree** — a vendored copy is a second master that can drift from the oracle without anybody noticing.
Reference fixtures by repository locator, exactly as this document does.

### 6.4 No performance measurement

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment anywhere, so **no performance objective may be asserted and no timing may be
recorded as an assertion.**

> **Characterization compares observable outputs. It never compares execution time.**

A recording holds returned values, generated statements, buffer states, item statuses, event sequences and
error payloads. It does not hold a duration. This is not merely compliance: a timing assertion in a
characterization suite is a defect *in the suite*, because it fails for reasons unrelated to behaviour and
trains readers to ignore red results.

The only quantitative non-functional requirement in the whole brief is the **80% per-service line-coverage
gate**, and it is **not measured from this store**. It is measured in CI from `coverage.cobertura.xml`, per
service, and [`../docs/BUILD.md`](../docs/BUILD.md) is the authority for the command and the report path.
Note the corollary, which is the reason both mechanisms exist: **line coverage is not a parity measure.** A
suite can reach the floor without pinning a single behaviour from
[§5.3](#53-the-defect-roster), and a pair can pin a behaviour that coverage never counts.

### 6.5 No large binary captures

Keep recordings text-based and diffable, per [§3.4](#34-recording-file-conventions). A committed binary
capture cannot be reviewed — a reviewer sees that two files differ and never what differed — and it defeats
the purpose of storing the master in version control at all. Where a workflow's output is genuinely binary,
record a stable textual projection of it (a canonical encoding, a field-by-field dump or a digest of a
masked normalization) and record how that projection is produced in the workflow definition.

---

## 7. What has not been exercised — stated plainly

**No paired recording exists, therefore no parity result exists**, and nothing in this document should be
read as reporting one. The obstacle is not a missing store, a missing definition or a missing mask — all
three are here. Specifically, and without softening any of it:

- **No capture has been taken**, on either side. `workflows/` carries fifteen definitions and the schema
  they validate against, and both `recordings/` roots exist — but each root is **empty**, so there is no
  recording to compare and no pair to compare under a mask. The layout of [§3.1](#31-layout) is the shape
  the first capture fills in, not an inventory of captures.
- **The legacy side of the oracle has not been executed here.** Running it requires a PowerBuilder
  toolchain that is not present, which is exactly what makes the pinyin risk of
  [§5.5](#55-the-one-genuine-parity-risk--pinyin-first-letter-matching) live rather than theoretical.
- **The stack has been brought up, and that is not a capture.** The bring-up, its ordered health gates and
  the fresh-volume provisioning path *were* exercised — reported gate by gate in
  [`../orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised),
  which is the only execution-status statement in this repository and which this store defers to rather
  than restating. What that run established is the **volume seam** a paired capture needs: a fresh
  `persistence-db` provisions itself and survives a plain `down`. It produced no recording, and no
  comparison may be claimed from it.
- **A passing service test does not substitute for a capture either.** An in-process test host mounts no
  volume, so it cannot be the target half of a pair no matter how thorough it is — the rule of
  [§2.1](#21-the-rule-canonical-text) is expressed against a Docker volume.

What **was** empirically confirmed on the build side is narrower and is worth stating precisely so the two
are not confused: the **per-service restore, release build and coverage-collecting test path** runs and
produces `coverage.cobertura.xml`, the exact artifact the coverage gate reads.
[`../docs/BUILD.md`](../docs/BUILD.md) is the authority for what that run covered and what it did not.

This position is not an expectation that things will work. It is the current state, and it is recorded here
so that the first person to take a real capture knows they are the first — and knows that the only thing
standing between the roster and a first pair is an oracle run.

---

## 8. Governing constraints

**No user-specified rules exist.** The project's rules document returns exactly one line — *"No user rules
provided."* No rule is invented, inferred or back-filled from convention here, and the absence is not
treated as licence to lower the bar: **enterprise-standard best practice applies in its place**, and the
binding constraints come from the migration plan's own constraint inventory instead. The ones that govern
this folder, each with what it requires *here*:

| Constraint | What it requires of this store |
| --- | --- |
| **C-B** — no behaviour improvements; defects reproduced, never corrected | A recording that shows corrected behaviour is a **failure**, not an improvement. The roster of [§5.3](#53-the-defect-roster) is what a pair is expected to prove, and no timing is ever asserted ([§6.4](#64-no-performance-measurement)) |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | Capture **from** the fixtures; never edit, move, reformat or vendor-copy one. Both source libraries are read-only, and every fixture is referenced by repository locator ([§4.7](#47-both-source-libraries-are-read-only), [§6.3](#63-no-edit-to-any-fixture)) |
| **C-D** — nothing for the four deferred services, not even a stub | No recording, workflow or mask for DesignSystem, Documents, Integration or ScriptBridge, and the excluded fixtures are named so the boundary is enforceable ([§6.1](#61-nothing-for-a-deferred-service)) |
| **C-E** — SQLite only; no fabricated database | The SQL Server and Oracle paging behaviours are characterized as pure string transforms with no instance of either provisioned, so those workflows need no volume and are **exempt** from the capture rule ([§2.7](#27-the-one-exemption-stated-so-it-is-not-ambiguous)) |
| **C-F** — no secret value anywhere | No recording captures key material, a certificate, a password or a token; the password field is write-only, the connection URI is a credential, and the statement field is recorded redacted ([§6.2](#62-no-secret-value)) |
| **C-K** — document every technology-specific and boundary-specific decision | The `persistence-db` rename, the seams, the newline-normalization convention, the seed-versus-capture split, the file-extension restriction and the pinyin disposition are all written down **with their reasons** rather than left as folklore |
| **C-L** — the environment's setup instructions are binding operational constraints | The paired-capture shared-volume rule is restated **verbatim** against the renamed volume, and its provenance is recorded rather than reconciled ([§2](#2-the-shared-volume-capture-rule)) |

One closing note on precedence, because this file is the junior partner: where this document and
[`../docs/PARITY.md`](../docs/PARITY.md) disagree, that document is authoritative and this one is stale.
