<!-- Markdown lint policy for this file. Rationale is in section 11, and the policy itself is shared with
     the other six documents this refactor authors — see docs/BUILD.md section 14, which directs that this
     document carry the same directive. MD013 is 120 rather than the 80-character default, and is disabled
     for tables and code blocks: an evidence row carrying a legacy locator and the behaviour it proves
     cannot be wrapped without splitting the locator from what it proves, and a wrapped command is a
     command that does not run. Prose IS wrapped, and is held to the 120 limit.

     THE POLICY IS SELF-DECLARED, SO IT NEEDS NO COMMAND, NO FILE LIST AND NO GLOB. The directive on the
     next line travels with the document: any markdownlint-compatible tool already provisioned on a
     reader's machine honours it, with no flags to remember and no external configuration file to locate.
     It applies to the seven documents this refactor authored and CANNOT reach the five read-only legacy
     Chinese documents in this folder, which are the behavioural oracle, are never edited, and carry
     pre-existing violations of their own (hard tabs, unlabelled code fences and others) that this
     refactor must not act on. That unreachability is precisely why a per-file directive was chosen over a
     repository-root configuration artifact the plan does not provide for.

     NO LINT COMMAND IS PUBLISHED, AND THAT IS A SUPPLY-CHAIN CONTROL RATHER THAN AN OMISSION. The entire
     approved npm dependency set for this repository is the exact, locked one declared under tests/e2e,
     and no Markdown linter appears in it. A documented on-demand package-runner invocation would
     therefore instruct an unpinned version to be resolved and executed from the network outside that
     lockfile every time somebody followed the documentation, which the deterministic-automation baseline
     forbids. Lint with tooling that is already installed; the directive below is what it reads. -->
<!-- markdownlint-configure-file { "MD013": { "line_length": 120, "tables": false, "code_blocks": false } } -->

# PowerFramework → .NET 10 — Behavioural Parity and Characterization Model

This document defines how behavioural parity with the legacy Appeon PowerBuilder framework is
**established** and **evidenced** for the four in-scope services. It answers one question:

> When the .NET implementation and the legacy framework disagree, how do we know which one is wrong —
> and what do we do when the answer is *neither, because the legacy behaviour is itself defective*?

The answer runs through this whole document, and it is worth stating on the first screen because every
later section is a consequence of it: **the legacy behaviour wins, including where it is defective.**
Correcting a legacy defect is forbidden (C-B). A defect is therefore reproduced and annotated at its
point of reproduction, never repaired — and §7, the catalogue of behaviours to reproduce, is this
document's real payload.

**Audience.** Anyone writing an in-scope implementation or a test for one; anyone capturing or comparing
a characterization recording; anyone reviewing whether a behavioural claim in the .NET tree is
substantiated. Every behavioural claim below carries a locator into the read-only legacy tree, and every
locator in this document was resolved against the file on disk and its line numbers checked.

**What this document deliberately does not duplicate.** Cross-reference rather than restatement is the
rule across this documentation set, because a figure repeated in two places is a figure that will
eventually disagree with itself:

| For | See |
| --- | --- |
| Build, test and coverage commands; the `.slnx` and Debug-versus-Release findings; the hand-authored xunit.v3 requirement | [`BUILD.md`](BUILD.md) |
| Service roster, transport rationale per service, the port map, the storage decision, the orchestration decision and the volume rename | [`ARCHITECTURE.md`](ARCHITECTURE.md) |
| The wire consequences of every behaviour preserved here — the ten contracts, the expansion modes, the conflict response, the byte-exact paging forms | [`CONTRACTS.md`](CONTRACTS.md) |
| Secret locators, severities and required actions; the token-topology register | [`SECRETS.md`](SECRETS.md) |
| Which legacy library and which of the 544 objects each fixture belongs to | [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) |
| The four deferred destinations, which receive no project, no container and no test in this phase | [`DEFERRED.md`](DEFERRED.md) |
| Compose bring-up detail, the readiness gates step by step, and **the single statement of what has and has not been exercised** | [`orchestration/README.md`](../orchestration/README.md) |

One duplication in this document is **deliberate and mandated** rather than an oversight: the
shared-volume capture rule in [§4.2](#42-the-rule-stated-in-full) is restated word for word in
`characterization/README.md`. §4.3 records why, and why it must not be consolidated.

## Current state of the artifacts this document references

**Every artifact named below exists in the tree, with one exception that is the point of this section.**
The `characterization/` store is present as structure — its readme, its fifteen workflow definitions and
the JSON Schema they validate against, and both recording roots — but **no recording has been captured on
either side**:

| Artifact | State |
| --- | --- |
| `characterization/README.md` | **Present.** The paired-capture store's own readme, restating the shared-volume rule verbatim |
| `characterization/workflows/` | **Present.** Fifteen workflow definitions with their determinism masks, plus `workflow.schema.json` and a readme |
| `characterization/recordings/legacy/<workflowId>/`, `characterization/recordings/dotnet/<workflowId>/` | **Present as empty roots.** No recording exists on either side, so **no paired comparison has been made** |
| `orchestration/docker-compose.yml`, `orchestration/README.md`, `orchestration/.env.example` | **Present.** Local orchestration, the `persistence-db` volume and the readiness gates |

Everything else this document references is present too: the read-only legacy tree including the entire
fixture corpus, the six shared library projects and their test projects, the four service applications with
entry points and their four test projects, the protocol and OpenAPI definitions under
`shared/PowerFramework.Contracts/`, the Playwright specs under `tests/e2e/specs/`, and the six sibling
documents in this folder. All twenty projects build in Release with zero warnings and zero errors and all
ten test projects pass.

**Present is not the same claim as exercised, and this document does not keep its own account of what was
run.** One place does, for the whole repository:
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised).
It is explicit that the orchestrated bring-up was exercised and that **no characterization capture was**,
which is exactly the gap this document is the specification for.

The distinction matters here more than it looks. A parity model whose *inputs* were also hypothetical
would be unfalsifiable — but the inputs are not hypothetical. **The oracle and its fixtures exist
today, in this checkout, and have been counted.** What is missing is the recordings themselves and the
pipeline that will gate them.

---

## Table of contents

1. [Position: what is verified, what is not, and under which constraints](#1-position-what-is-verified-what-is-not-and-under-which-constraints)
2. [The technique, and why it is the right one here](#2-the-technique-and-why-it-is-the-right-one-here)
3. [The oracle and its fixture corpus](#3-the-oracle-and-its-fixture-corpus)
4. [Paired recordings and the shared-volume capture rule](#4-paired-recordings-and-the-shared-volume-capture-rule)
5. [Determinism seams](#5-determinism-seams)
6. [Test shape](#6-test-shape)
7. [The catalogue of behaviours to reproduce](#7-the-catalogue-of-behaviours-to-reproduce)
8. [Coverage gate mechanics](#8-coverage-gate-mechanics)
9. [Known parity risks and limitations](#9-known-parity-risks-and-limitations)
10. [What this document claims and does not claim](#10-what-this-document-claims-and-does-not-claim)
11. [Markdown lint policy for this documentation set](#11-markdown-lint-policy-for-this-documentation-set)

---

## 1. Position: what is verified, what is not, and under which constraints

### 1.1 No user-specified rules exist

The project's rules document was retrieved and contains exactly one statement: **no user rules were
provided.** That is a finding, not an omission, and three consequences follow:

- **No rule is invented, inferred, or back-filled from convention.** Nothing in this document is
  presented as a requirement because it is common practice somewhere else.
- **Enterprise-standard best practice applies in the rules' place.** For a parity document that means
  three specific things: a test project per shippable project, a hard coverage gate evaluated per
  service rather than in aggregate, and honest separation of what was empirically verified from what
  was not.
- **Zero files enter scope because of a rule.** This document exists because the migration plan
  requires it, and every fixture it names is in scope because a dependency edge or an explicit
  requirement put it there.

The binding constraints therefore come from elsewhere — the migration brief's own clauses and the
attached environment's setup instructions. They are referred to below by their identifiers (C-A through
C-L) rather than reproduced. Six govern this document directly:

| Constraint | What it requires of *this* document | Where it is discharged |
| --- | --- | --- |
| **C-B** — no behaviour improvements, no performance objective | Frame every defect as reproduce-and-annotate, never repair; assert no performance claim of any kind | §1.6, and the whole of §7 |
| **C-C** — the legacy tree is read-only and is the behavioural oracle | Treat the fixture corpus as read-only input; never edit, translate, re-encode, rename or link-rewrite any legacy path, including the five Chinese documents in this folder | §1.2, §3.1 |
| **C-E** — no fabricated storage engine | Target SQLite only; present the paging rewriters as pure-function matrices needing no engine | §3.4, §6.2 |
| **C-H** — 80% line coverage per in-scope service | State the gate mechanics precisely, and that evaluation is per service | §8 |
| **C-K** — document every technology and boundary decision | Record the technique choice with its one hard prerequisite, and the volume rename as a deliberate deviation | §2, §4.3 |
| **C-L** — the environment's setup instructions are binding | Restate the paired-capture shared-volume rule verbatim against the renamed volume | §4.2 |

### 1.2 The legacy tree is read-only, and it is the behavioural oracle

`ws_objects/**`, the compiled libraries and target files, `oldversion/125/**`, `pack/**`, the native
binaries, `res/**` and the legacy browser assets under `tests/blink`, `tests/sciter` and `tests/webview`
are the **behavioural oracle**. They are never edited, moved, renamed or reformatted (C-C).

That applies with particular force to the fixture corpus, because a fixture is the one kind of file a
reader is most tempted to adjust. The 47 test windows and 11 DataWindow definitions enumerated in §3
are **inputs** to the oracle. They are REFERENCE and characterization-fixture source only: **never
edited, never ported as-is.** Editing an object in that corpus does not adjust a test — it silently
moves the standard the test measures against, which is the one thing a golden master must never do.

The read-only status extends explicitly to the five pre-existing Chinese documents in this very
folder — [`README.md`](README.md), [`Blink交互.md`](Blink交互.md), [`Sciter交互.md`](Sciter交互.md),
[`PB多线程绕坑提示.md`](PB多线程绕坑提示.md) and
[`n_cst_dwsvc_columnexp.md`](n_cst_dwsvc_columnexp.md). This document **cites** two of them as
authoritative behavioural specifications — see [§7.3](#73-static-versus-dynamic-expansion) and
[§7.11](#711-the-two-threading-hazards-that-constrain-the-port) — and it does not touch them, translate
them, re-encode them, rename them or rewrite their links. This document is purely **additive**: it
creates one new file and changes nothing that already exists.

### 1.3 Locator convention

Every behavioural claim carries a `path:Lnn` locator, and the discipline is not ceremony. No other
artifact in the repository can settle a question of behaviour:

- **The changelog is stale and is not a specification.** `logfile.md:L1` opens at
  `## 3.0.7.2062(2022-04-14)` while the commit history runs years later. See
  [R6](#r6--the-changelog-is-not-a-specification-and-is-stale).
- **Neither PowerBuilder build definition would build as written**, so the oracle cannot be
  reconstituted from repository metadata alone. See
  [R5](#r5--there-is-no-authoritative-legacy-build-definition-to-translate) and
  [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md).
- **There are zero Git tags** across the repository's history, so no release is marked and no
  known-good point exists to characterize against by version.

Consequently every behavioural assertion in this document, and every behavioural assertion in the .NET
code this document governs, traces to a `ws_objects/**` locator. Where a claim rests on a count rather
than a line, the command that produced the count is given so a reader can re-run it.

### 1.4 What was verified by execution

Stated narrowly, because a parity document that overclaims its own verification is self-defeating:

- **The per-service restore, release build and coverage-collecting test COMMAND SHAPE was exercised and
  passed** with **zero warnings and zero errors**, and it produced `coverage.cobertura.xml` — the exact
  artifact the coverage gate in [§8](#8-coverage-gate-mechanics) is measured from. **That run was against
  a throwaway skeleton project, not against this repository's services**, and it is evidence that the
  command and the coverage collector work rather than a result for the four services.
  [`BUILD.md`](BUILD.md) §13 states the same caveat and is the authority for it.
- **Separately, in this repository:** restore is audit-clean, all twenty projects build with zero warnings
  and zero errors, all ten test suites pass, all four images build, and the **four-service stack has been
  brought up** — every container reaching Docker health `healthy` in dependency order, with `/health`
  answering anonymously over TLS on all four and Gateway's aggregate answering against three live
  upstreams. [`BUILD.md`](BUILD.md) §1.3 is the canonical record of what was run; no figure is restated
  here. **None of that is a parity result** — it establishes that the capture *environment* works, which is
  a precondition of a comparison rather than a comparison.
- **Every locator in this document was resolved against the file on disk**, and the cited line numbers
  were checked against their content rather than trusted.
- **Every count in [§3](#3-the-oracle-and-its-fixture-corpus) was produced by counting the files**, and
  the counting command is published alongside the count.
- **The claim that `dw_sqlite.srd` is the only updatable DataWindow in the repository was tested**, not
  assumed: all twelve `.srd` files under `ws_objects/` were searched for `updatewhere=` and exactly one
  matched.
- **The claim that the column-expression parse messages bypass localization was tested** by searching
  that object for any localization call at all. There are none. See
  [§7.8](#78-the-non-localized-expression-messages).

### 1.5 What this document does not claim — stated plainly

**No parity result is claimed anywhere in this document, and that is the claim that matters here.** The
services build, their suites pass and the stack has been brought up — but **no characterization recording
exists on either side**, so nothing has been compared. `characterization/recordings/legacy/` and
`characterization/recordings/dotnet/` are present and empty. This document is the model and the method; it
reports no comparison, because the legacy oracle has not been exercised here
([R1](#r1--pinyin-first-letter-matching-cannot-be-proven-bit-exact-from-the-repository-alone)) and no
target-side capture has been taken either.

**This document keeps no account of what was run — one place does.**
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
is the single execution-status statement for the repository. It records that the orchestrated bring-up was
exercised, and it lists the absence of any characterization capture first among the things that were not.

The distinction matters more in this document than in any other, because the capture rule of
[§4.2](#42-the-rule-stated-in-full) is expressed in terms of a Docker volume — and that volume seam has now
been observed rather than assumed. Persistence provisions a **fresh** `persistence-db` volume during
startup, additively and idempotently, and survives a plain `down`; the single statement above quotes the
log lines. **Observing the seam is not taking a capture**, and no comparison is claimed from it: what the
seam establishes is that a paired capture *can* be taken against one unrecreated volume, which is precisely
what the rule requires of the work that produces the first pair.

- `characterization/recordings/legacy/` holds **no recording**, and none can be produced here.
- `characterization/recordings/dotnet/` holds **no recording** either, because a target-side capture with
  nothing to pair against is not half a comparison — it is an artifact with no counterpart, and
  [§4](#4-paired-recordings-and-the-shared-volume-capture-rule) explains why storing one is worse than
  storing none.
- **No behavioural claim in this documentation set rests on a comparison.** Every one rests on a source
  locator into the read-only legacy tree, which is the discipline [§1.4](#14-what-was-verified-by-execution)
  describes and the reason it exists.

That is the whole of the gap, and it is tracked as
[R4](#r4--the-stack-has-been-brought-up-but-no-capture-has-been-taken-against-it) rather than as a
footnote. What this document is, therefore, remains what it always was: **the model, the fixture corpus,
the determinism seams and the method** — with the capture environment now proven and the oracle still
unrun.

One consequence for a reader in a position to run the oracle: everything on the target side is ready. The
15 workflow definitions, their masks, the schema and the store are authored; the stack comes up; the seams
are injected. The next step is a legacy run, not more scaffolding.

### 1.6 No performance claim appears in this document

The repository publishes no service-level agreement, no latency budget, no throughput target and no
availability commitment anywhere. **No performance objective may therefore be asserted, claimed as met,
or used to justify a design choice** (C-B). The only quantitative non-functional requirement in the
entire brief is the 80% line-coverage gate of [§8](#8-coverage-gate-mechanics).

There is a subtlety here that is specific to characterization work and easy to get wrong:

> **Characterization compares observable outputs. It never compares execution time.**

A recording holds returned values, generated statements, buffer states, item statuses, event sequences,
error payloads and their fields. It does not hold a duration, and a duration is never an assertion. That
is not merely compliance with C-B — a timing assertion in a characterization suite is a defect in the
suite, because it fails for reasons unrelated to behaviour and trains readers to ignore red results.

One consequence worth stating: two legacy source comments encountered during discovery volunteer
rationale of a kind this document may not assert. Where that happens — the clearest case is
`n_cst_thread_task_sqlquery.sru:L578`, discussed in
[§7.5](#75-the-four-cross-thread-transfer-defects) — only the **behavioural** half of the comment is
carried across, and the other half is deliberately not restated.

### 1.7 Constant identifiers keep their legacy spelling, and this document is why

Constants keep their original SCREAMING_SNAKE spelling in C# — `RetCode.OK`, `RetCode.PREVENT`,
`RetCode.FAILED`, `RetCode.CANCELLED`, `RetCode.E_INVALID_ARGUMENT`, `RetCode.E_NO_IMPLEMENTATION`,
`Enums.INIT_FLAG_ENABLE_ALL`, `Enums.SQL_MS_REPLACE`, `Categories.CAT_DWSVC` — contrary to C# naming
convention. `.editorconfig` suppresses the relevant analyzer rules, scoped to the files that carry
them.

**The reason is this document's subject matter, which is why it is stated here rather than only
cross-referenced.** These exact strings appear in three places that outlive any single test run:

1. **Serialized payloads.** A contract field carrying an enumeration name transmits the spelling.
2. **Log records.** A diagnostic line naming a return code names it by spelling.
3. **Characterization recordings.** A stored legacy-side capture contains the spelling as it was at
   capture time.

A rename is therefore not a cosmetic change. It **silently invalidates every stored comparison**: the
candidate emits `Cancelled`, the master holds `CANCELLED`, the diff reports a behavioural difference
that does not exist, and the natural response — re-recording the master — destroys the only evidence of
what the legacy actually did. Renaming a constant in this system is not a refactor; it is an erasure of
the oracle. The spellings are frozen.

Two spellings deserve individual mention because they look like errors and are not:

- **`CANCELED` and `CANCELLED` both exist and both equal −2** [`retcode.sru:L44-L45`]. Both are
  preserved. Neither is the "right" one.
- **`pfwPagedSQL_OutterTbl` has a doubled `t`** [`n_cst_thread_task_sqlquery.sru:L333`]. It is the
  legacy spelling, it appears in generated SQL that parity is measured against byte for byte, and it is
  reproduced exactly. See [§6.2](#62-the-paging-rewriters-are-pure-function-matrices-requiring-no-storage-engine).

---

## 2. The technique, and why it is the right one here

### 2.1 Characterization testing, and its provenance

The technique is **characterization testing**, also called **golden-master testing**. It is described by
Michael Feathers in *Working Effectively with Legacy Code* (Addison-Wesley, 2004), chapter 13, "Do I
Have to Change All Those Tests?" — the chapter that introduces characterization tests as the instrument
for putting legacy code under test when no specification of intended behaviour exists.

The shape is simple and the discipline is where the value lies. You exercise the existing system,
**record what it does**, and promote that recording to the status of expected output — the *golden
master*. The candidate implementation is then run through the identical inputs and its output is
compared against the master. A difference is a finding. The master is never edited to make a comparison
pass.

### 2.2 Why it fits this refactor exactly

This is not a generic choice made for want of a better one. It is the only technique whose defining
property matches this refactor's defining constraint.

> **A characterization test asserts observed behaviour even when that behaviour is wrong.**

That is precisely the mandate. A conventional unit test encodes what the author believes the code
*should* do; where the legacy is defective, such a test would encode the corrected behaviour and pass
against an implementation that has silently diverged from the system it is replacing. A
characterization test encodes what the code **actually does**, defects included — and because
correcting a legacy defect is forbidden (C-B), "what it actually does" *is* the specification.

Three properties of this particular refactor make the fit exact rather than approximate:

- **There is no other specification to consult.** The legacy tree is simultaneously read-only and the
  only statement of intended behaviour (§1.2, §1.3). Nothing else in the repository can adjudicate a
  behavioural question, so a technique that derives its expectations from observation rather than from
  documentation is not a fallback — it is the only option that is sound.
- **The defects are numerous, load-bearing, and reachable from in-scope paths.** §7 catalogues twelve
  groups of them. Several are not incidental quirks but the behaviour that downstream logic depends
  on — the tri-state return algebra in [§7.1](#71-the-tri-state-return-algebra) is consumed by every
  caller in the framework, and the inverted buffer iteration in
  [§7.12](#712-one-based-to-zero-based-translation-is-the-sharpest-hazard) produces *correct* output
  precisely because it looks wrong.
- **The language, the runtime and the process boundary all change at once.** When everything moves,
  structural similarity between old and new code proves nothing. Only observable output can carry the
  comparison, and observable output is exactly what a golden master holds.

### 2.3 The one hard prerequisite: repeatability

The technique has exactly one hard prerequisite, and it admits no workaround:

> **The comparison must be repeatable, with every non-deterministic value masked from *both* the master
> and the candidate.**

Both sides. Masking only the candidate leaves the master holding a value that can never be matched;
masking only the master inverts the same problem. A value that differs per run makes the comparison
meaningless in either direction, and — worse — makes it *intermittently* meaningless, which trains
readers to dismiss failures.

### 2.4 What follows from the prerequisite

Two things in this document exist because of §2.3, and reading them as process overhead is the most
likely way to get this wrong:

- **The shared-volume capture rule** ([§4](#4-paired-recordings-and-the-shared-volume-capture-rule)) is
  the environment's binding operational instruction (C-L), *and* it is independently what the technique
  itself requires. A volume reseeded between the two captures of a workflow changes the input state, so
  the two recordings answer different questions. Compliance and correctness coincide here — the rule
  would have to be invented if the environment had not supplied it.
- **The determinism seams** ([§5](#5-determinism-seams)) are not an abstraction indulgence. Each one is
  a specific, enumerated source of per-run variation, injected so a test can substitute a deterministic
  double while production uses the platform implementation. A seam that is described but not injected is
  a seam that does not exist.

---

## 3. The oracle and its fixture corpus

### 3.1 The corpus already exists — it needs no invention

The central finding of this section, stated plainly:

> **The behavioural oracle is the legacy tree itself, and the fixtures already exist in the
> repository. They need no invention.**

This is unusual and it is worth dwelling on. A characterization effort normally begins by constructing
inputs that drive the legacy system through its interesting states. Here that work is already done: the
legacy ships two libraries of exercise windows, each bound to DataWindow definitions that configure the
very behaviours §7 requires be reproduced. **The DataWindow corpus is the test corpus.**

Verified counts:

| Source | Windows | DataWindow definitions |
| --- | --- | --- |
| `ws_objects/pfw.tests.pbl.src` | **47** `w_test_*.srw` | **11** `.srd` |
| `ws_objects/pfw.demos.pbl.src` | 9 `.srw` | 1 `.srd` (`dw_test.srd`) |

Reproduce the counts from the repository root:

```bash
ls ws_objects/pfw.tests.pbl.src/w_test_*.srw | wc -l   # -> 47
ls ws_objects/pfw.tests.pbl.src/*.srd        | wc -l   # -> 11
ls ws_objects/pfw.demos.pbl.src/*.srw        | wc -l   # -> 9
ls ws_objects/pfw.demos.pbl.src/*.srd        | wc -l   # -> 1
```

Two notes on the counts. In `pfw.tests` **every** window matches `w_test_*` — the 47 is both the
`w_test_*.srw` count and the total window count, so no window is unaccounted for. And the eleven
DataWindow definitions in `pfw.tests` are not interchangeable samples; they are named for the
capabilities they exercise: `dw_sqlite`, `dw_svc_sample`, `dw_svc_sample_columnexp`,
`dw_svc_sample_contextmenu`, `dw_svc_sample_dddw`, `dw_test_dwsvc`, `dw_test_dwsvc_columnexp`,
`dw_test_dwsvc_contextmenu`, `dw_test_dwsvc_dddw`, `dw_barcode` and `dw_qrcode`. The last two belong to
a deferred capability area — see [`DEFERRED.md`](DEFERRED.md) — and the remaining nine map onto in-scope
DataWindow service behaviour.

**Status of every file named in this section: REFERENCE and characterization-fixture source only —
never ported as-is, never edited** (C-C, and §1.2). [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) records
the assignment of these libraries in the full-estate mapping; both are permanently out of scope as
*ported code* precisely so they can serve as fixtures.

One further consequence of C-C that belongs here rather than only in
[`SECRETS.md`](SECRETS.md): **`pfw.tests` and `pfw.demos` are where most of the in-source secret sites
live.** That does not make them editable. The remediation posture is *never replicate, document, and
rotate* — never *edit the legacy file*. This document therefore refers to those sites only by locator
through [`SECRETS.md`](SECRETS.md) and **reproduces no value of any kind**, and no fixture-derived
recording may carry one either.

### 3.2 The primary fixture: `dw_sqlite.srd`

`ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` is **37 lines** long, declares `release 12.5;` at `:L2`,
and is **the only updatable DataWindow in the entire repository.**

That last claim was tested rather than assumed. There are twelve `.srd` files under `ws_objects/`;
exactly one contains `updatewhere=`:

```bash
grep -rl "updatewhere=" --include="*.srd" ws_objects/   # -> ws_objects/pfw.tests.pbl.src/dw_sqlite.srd
find ws_objects -name "*.srd" | wc -l                   # -> 12
```

Its table specification at `:L14` reads — reproduced exactly, including the double space before
`sort=`, because parity here is measured on exact text:

```text
 retrieve="SELECT * FROM COMPANY" update="COMPANY" updatewhere=1 updatekeyinplace=no  sort="age A salary A " )
```

All six columns carry `update=yes updatewhereclause=yes` [`:L8-L14`]:

| Position | Column | Declared type | Extra flags |
| ---: | --- | --- | --- |
| 1 | `id` | `number` | **`key=yes identity=yes`** [`:L8`] |
| 2 | `name` | `char(100)` | — [`:L9`] |
| 3 | `age` | `number` | — [`:L10`] |
| 4 | `address` | `char(200)` | — [`:L11`] |
| 5 | `salary` | `decimal(2)` | — [`:L12`] |
| 6 | `birth` | `date` | — [`:L13`] |

And a footer computation at `:L27` carries `expression="sum(salary for page)"`, so **the fixture
exercises the expression engine as well as the update path** — and specifically a *page-scoped*
aggregate, which is the harder of the aggregate forms because its scope is a paginated presentation
concept rather than a result-set concept.

**Why this single 37-line file matters as much as it does.** Four reasons, each independent:

1. **It is the golden-master fixture for the entire retrieval / validation / update triple.** With
   `updatewhere=1` and all six columns marked, the optimistic-concurrency check spans all six columns'
   *original* values — so a recording of this fixture pins the concurrency contract, not just a rowset.
   [`CONTRACTS.md`](CONTRACTS.md) (C-06) carries the wire consequence.
2. **It sets `updatekeyinplace=no` itself**, which is the exact trigger for the legacy's own documented
   self-assignment workaround. That path is therefore **exercised by the primary fixture** rather than
   being a rare branch reachable only by contrivance. See
   [§7.6](#76-the-self-assignment-workaround).
3. **It carries the identity column**, so the identity round-trip — including the inverted buffer
   iteration of [§7.12](#712-one-based-to-zero-based-translation-is-the-sharpest-hazard) — is reachable
   from it.
4. **It carries an expression**, so a single fixture spans two of the three hardest contracts in the
   refactor.

### 3.3 The only schema in the repository

`ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` (515 lines) carries **the only DDL in the entire
repository**, at `:L463-L469`. Verified:

```bash
grep -rn "CREATE TABLE" --include="*.sru" --include="*.srw" --include="*.srf" \
                        --include="*.srd" --include="*.srs" --include="*.sra" ws_objects/
# -> ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:463  (plus :470, which is a dialog title)
```

The table it creates, `COMPANY`, has six columns, and it is worth quoting the declarations rather than
describing them because two of them are habitually read as saying more than they do:
`ID INTEGER PRIMARY KEY NOT NULL`, `NAME TEXT NOT NULL`, `AGE INT NOT NULL`, `ADDRESS CHAR(50)`,
`SALARY REAL`, `BIRTH TEXT`.

- **`ID` is a rowid alias, not an `AUTOINCREMENT` column.** The keyword is absent; the legacy's own inline
  comment on that line reads `自增列`, "auto-increment column", recording the *intent*. `INTEGER PRIMARY
  KEY` does make SQLite assign a value when none is supplied, but by the largest-existing-rowid-plus-one
  rule, which **reuses the rowids of deleted rows**. The identity round-trip must not assume monotonicity.
- **`CHAR(50)` is not a fifty-character constraint.** SQLite has no fixed-width string type: a declared
  type sets *affinity* only — TEXT affinity here — and the parenthesised length is ignored entirely. A
  longer value is stored in full, unpadded and untruncated, with no error. `REAL` likewise sets affinity
  rather than a scale, and `TEXT` on `BIRTH` carries no date semantics at all.

§3.5 catalogues the four places these declarations disagree with the DataWindow's own, and what each
disagreement makes observable.

The surrounding lines define the connection grammar the implementation must reproduce:

| Locator | What it establishes |
| --- | --- |
| `:L450` | A file delete precedes the open, so each run starts from a known-absent file |
| `:L452` | The URI grammar cites the SQLite URI specification as its authority |
| `:L453-L455` | Two documented URI extensions: an **integrity-check** option with a quick variant, and a **journal-mode** option across six values, defaulting to delete mode |
| `:L456` | The connection is opened through a URI of the form `test.db?mode=rwc`, with an optional password parameter available in the same position — shown at that line as a commented placeholder. The repository carries no value for it, and none is reproduced here |
| `:L461`, `:L473` | Auto-commit is enabled for the DDL and disabled afterwards — the ordering is part of the observable behaviour |

### 3.4 Fixtures target SQLite only

**One storage engine is provisioned, and it is SQLite** (C-E). Stated without hedging so that no reader
draws the opposite conclusion from §6.2:

> **No SQL Server instance and no Oracle instance is provisioned, required, or reachable — not for the
> implementation, not for any test, and not for any characterization capture.** Neither has a schema, a
> connection string or any DDL anywhere in the repository. Only two dialect constants and two statement
> generators exist, and those are exercised as pure string transforms with no engine of any kind. See
> [§6.2](#62-the-paging-rewriters-are-pure-function-matrices-requiring-no-storage-engine) and
> [`ARCHITECTURE.md`](ARCHITECTURE.md) for the storage decision in full.

The encrypted-SQLite path is separately out of Phase-1 scope; the reason is
[R3](#r3--encrypted-sqlite-page-format-parity-is-out-of-phase-1-scope).

### 3.5 Four preserved schema mismatches

The DataWindow definition of §3.2 and the DDL of §3.3 **disagree**, in four places — four of the six
columns. The disagreements are **preserved as defects, and are not reconciled**:

| # | DataWindow declares [`dw_sqlite.srd`] | The table declares [`w_test_sqlite.srw:L463-L469`] | Consequence to reproduce |
| ---: | --- | --- | --- |
| 1 | `name` as `char(100)` [`:L9`] | `NAME TEXT NOT NULL` — **unbounded** | The bound exists **only** in the DataWindow. SQLite stores a longer value in full, so any truncation observed is the DataWindow's, and a port that enforces 100 characters in storage diverges from the oracle |
| 2 | `address` as `char(200)` [`:L11`] | `ADDRESS CHAR(50)` | **`CHAR(50)` is not a width constraint in SQLite.** The declared type sets *affinity* only — TEXT affinity here — and the length is ignored entirely: a 200-character address is stored in full, unpadded and untruncated, with no error. So the two declarations disagree and **the engine enforces neither**; the observable behaviour is whatever the DataWindow does with its own 200-character bound. Do not read `CHAR(50)` as meaning the column "cannot store" the value; SQLite can, and does |
| 3 | `salary` as `decimal(2)` [`:L12`] | `SALARY REAL` | A two-place decimal presented over REAL affinity. The scale exists only in the DataWindow; round-trip values are not guaranteed identical, and the observed result is the specification |
| 4 | `birth` as `date` [`:L13`] | `BIRTH TEXT` | Date semantics over text storage: SQLite has no date type at all, so ordering, comparison and format all follow from the text representation actually written, not from a date type |

**A fifth row that is *not* a mismatch, recorded so nobody adds it.** `id` is `type=number key=yes
identity=yes` in the DataWindow [`:L8`] against `ID INTEGER PRIMARY KEY NOT NULL`. That is consistent:
`INTEGER PRIMARY KEY` makes the column an **alias for the rowid**, so SQLite assigns a value when none is
supplied, which is what `identity=yes` expects. **But note what it is not** — the DDL has **no
`AUTOINCREMENT` keyword**; the legacy's own comment beside the column reads `自增列`, "auto-increment
column", which records the *intent*. Without the keyword, assignment follows the
largest-existing-rowid-plus-one rule, which **reuses the rowids of deleted rows**. The identity round-trip
must therefore not assume monotonicity. `orchestration/.env.example` §2 records the same two facts from the
storage side.

These are not oversights to tidy up on the way through. Each one is reachable from the primary fixture,
each one produces observable behaviour, and each one is therefore something a recording will contain.
An implementation that declares consistent types on both sides would diverge from the oracle on the
first value that exercises the difference. Reproduce; do not reconcile.
[`ARCHITECTURE.md`](ARCHITECTURE.md) records the same mismatches from the storage side.

### 3.6 Two storage-engine behaviours, measured through the published contract

Both were observed at runtime against the primary fixture, through `POST /v1/datawindow/update` on the
composition root and read back through `POST /v1/datawindow/retrieve`. Neither is a defect to repair:
each is the behaviour of the storage engine sitting **below** every layer this refactor owns, and
"correcting" either would mean adding a validation the legacy does not perform, which constraint C-B
forbids exactly as firmly as it forbids removing one.

| # | What was sent | What the engine did | Standing |
| ---: | --- | --- | --- |
| 1 | An `ADDRESS` value **200 characters** long, against `ADDRESS CHAR(50)` | Accepted and stored **in full at length 200** — unpadded, untruncated, no error, no warning | **Confirms §3.5 row 2 by measurement.** `CHAR(50)` sets TEXT affinity and nothing else; the width lives only in the DataWindow. Preserved defect, AAP §0.6.4 — do not enforce 50, and do not enforce 200 in storage either |
| 2 | A `NAME` value containing an **embedded NUL** (`U+0000`) three characters in — `Nul\0Tail` | Stored and returned **in full**. The stored bytes are `4E756C005461696C`, `length(CAST(NAME AS BLOB))` is 8, and the published retrieval answers all eight characters with the NUL intact. But `length(NAME)` — the SQL function over the TEXT value — answers **3** | **The value round-trips byte-exact; only the SQL length function stops at the NUL.** So nothing is lost, and nothing is to be fixed, but any logic *derived* from `length()` — a filter, a computed column, a validation expressed in SQL — sees a shorter string than the one stored. Measured on both sides: the engine the service runs on and the engine the tooling reads with agree |

Row 2 deserves two further notes, because the first reading of it was wrong and the second invites a
"safety" fix.

**What it is not.** It is tempting to read a `length()` of 3 as truncation, and that reading was made
before the bytes were checked. It is wrong: `length()` on a TEXT value counts characters up to the first
NUL, while the value itself is stored and returned whole. The distinction matters for parity in opposite
directions — a port that "corrected" the storage would break a byte-exact round trip that currently
holds, and a port that assumed `length()` reported the stored size would compute a different answer from
the oracle wherever a NUL appears.

**What it is not to be repaired into, either.** The ported `dwnvlstring` validator polices what the
legacy's own validator polices and nothing more, and the legacy has no NUL check anywhere in the write
path. Adding one would refuse a value the oracle accepts — a divergence dressed as hardening. The honest
treatment is the one taken: record it, so that a reader meeting a `length()` that disagrees with the
string they sent finds the reason here rather than filing it as data loss in the port.

---

## 4. Paired recordings and the shared-volume capture rule

### 4.1 Layout

A characterization comparison is always between a **pair** of recordings keyed by the same workflow
identifier. The store reflects that directly:

| Path | Contents |
| --- | --- |
| `characterization/recordings/legacy/<workflowId>/` | The PowerBuilder oracle's output for that workflow |
| `characterization/recordings/dotnet/<workflowId>/` | The target-side output for the same workflow |
| `characterization/workflows/` | Workflow definitions and their determinism masks |
| `characterization/README.md` | The store's own readme, which restates the rule of §4.2 word for word |

The `<workflowId>` segment is the join key and it is the only thing that makes two recordings
comparable. A recording whose workflow identifier does not exist on the other side is not a partial
comparison — it is not a comparison at all, and it must not be reported as a pass.

The mask lives with the **workflow**, not with either recording, and that placement is deliberate: a
mask stored beside one side could be applied to that side alone, which is exactly the failure §2.3
forbids. One mask, one workflow, applied identically to both captures.

### 4.2 The rule, stated in full

This is the most operationally important paragraph in this document. It is the environment's binding
instruction (C-L), reproduced **word for word with one substitution and no other change** — every
occurrence of the volume name `data-service-db` becomes `persistence-db`, for the reason §4.3 records:

> **Persistence rule: legacy characterization recordings (PowerBuilder behavioral oracle output, keyed
> per workflow ID) and this scaffold's target-side recordings must be captured against the SAME
> `persistence-db` Docker volume state for a given workflow ID comparison to be valid. Do not recreate or
> reseed `persistence-db` between the legacy-side capture and the .NET-side capture for the same workflow
> ID, or the paired recordings required by the Agent Action Plan's success criteria will not be
> comparable.**

Nothing above is paraphrase, and two clauses a shortened restatement tends to drop both carry weight:
that the recordings are **keyed per workflow ID** on the legacy side as
well as the target side, and that what a broken pair invalidates is **the Agent Action Plan's own success
criteria** rather than merely a local comparison. A third, `Docker volume` in full, matters because the
rule is about a volume in a specific technology and a "volume state" in the abstract is a weaker
instruction.

In operational terms, for one workflow identifier: capture the legacy side, capture the target side, and
do **not** run anything between them that destroys, recreates, re-initializes or re-seeds the
`persistence-db` volume. If the volume state has moved, the pair is void — discard both captures and
start the workflow again. Do not repair a broken pair by re-taking one half of it; a pair re-taken in
halves is two recordings of two different worlds.

The corollary for parallel work: **both halves of a pair belong to one clone.** Paired captures for a
single workflow identifier must stay inside a single working tree against one unrecreated
`persistence-db` volume. Where several clones run concurrently, each needs its own Compose project and
its own volume so that one clone's teardown cannot invalidate another clone's half-finished pair.

**One thing the rule is regularly misread as forbidding, and does not: Persistence applying its own pending
migrations at startup.** The manifest turns that step on
([`ARCHITECTURE.md`](ARCHITECTURE.md) §8.6), so a `persistence-service` container started or restarted in the
middle of a pair will run it. What it calls is EF Core's `Database.Migrate` and nothing else — additive, and
performing no write at all when the history table already records everything — so it neither recreates nor
reseeds the volume, which are the two acts §4.2 names. The rule is about the **volume's state**, not about
which process establishes it: what voids a pair is recreating the volume, re-running the legacy fixture's own
file delete and DDL, or reseeding rows. A capture operator who would rather have the step out of the picture
altogether sets `PERSISTENCE_APPLY_MIGRATIONS_ON_STARTUP=false` and provisions once before the pair begins;
that is also the code default, so relying on an untouched volume never depends on remembering to switch
something off. `characterization/README.md` §4.4 carries the same ruling at the point of capture.

### 4.3 The rename is a deliberate, documented deviation

The attached environment names the persistence volume after a *data service*. In the reviewed
four-service roster that placeholder service becomes **`persistence-service`**, so the volume becomes
**`persistence-db`**. [`ARCHITECTURE.md`](ARCHITECTURE.md) records the rename on the orchestration side
and deliberately does **not** restate the rule, because the rule belongs with the characterization model.

Two things about the rename are recorded rather than assumed (C-K):

- **The rule is restated *verbatim* against the new name, so its intent survives the rename intact.**
  Renaming a volume in an instruction and paraphrasing the instruction at the same time is how an
  operational rule quietly loses its force. §4.2 is therefore the environment's own two sentences, word
  for word, with `data-service-db` replaced by `persistence-db` in both places it occurs and **no other
  edit** — not a shortening, not a re-ordering, not a summary. Claiming verbatim reproduction while
  carrying a paraphrase that has dropped material wording is the specific failure to guard against, and
  §4.2 names the clauses a paraphrase loses first.
- **`characterization/README.md` is to restate the identical rule, and that duplication is deliberate and
  mandated — not an oversight to consolidate.** The two must say the same thing. The reason is
  situational: an operator capturing a recording is working inside `characterization/`, and a rule that
  lives only in a documentation folder they have no reason to open is a rule that will be broken by
  someone acting in good faith. This is the one place in this documentation set where restatement beats
  cross-reference, and §4.2 is the canonical text both copies carry. **That second copy exists**:
  [`../characterization/README.md`](../characterization/README.md) §2 carries the rule, and a change to
  either copy is a change to both.

### 4.4 The rule is the technique's own prerequisite, not merely a local convention

It would be easy to file §4.2 as environment compliance and move on. That reading understates it.

Golden-master testing requires **repeatability** (§2.3). The input state of a workflow that touches
storage *is* part of its input. A volume reseeded between the two captures changes that input, so the
two recordings answer two different questions and the diff between them is noise wearing the costume of
a finding. **The rule would have to be invented if the environment had not supplied it.**

Compliance and correctness coincide here, which is the useful thing to know about the rule: an operator
who understands *why* it exists will not be tempted to work around it under pressure, because the
work-around does not produce a weaker comparison — it produces no comparison.

---

## 5. Determinism seams

Non-deterministic values must be masked from **both** the master and the candidate (§2.3). Masking
requires knowing exactly where variation enters, so the sources are enumerated rather than described in
the abstract.

### 5.1 The seam register

A seam that is documented but not injected has no effect on a single test (§5.2), so each row below
carries **its own status** in the vocabulary [`BUILD.md`](BUILD.md) §1 declares, rather than a blanket
claim over all four. **All four ARE injected in source**, each at a named injection point, so every
row is checkable rather than asserted — which is the reason the per-row status column exists and the reason
it is kept now that the statuses agree.

**Each seam here is executed by its service's own suite** — all four service test projects build and pass
([§1.4](#14-what-was-verified-by-execution)) — but by unit and in-process host tests rather than by a
paired characterization capture, which is what the store below is for and which holds no recording. Exactly
one piece of *executed* evidence bears on this section, and it is negative:
`shared/PowerFramework.Contracts.Tests/ContractsCarryNoBehaviourTests.cs` lists `System.TimeProvider`
among the ambient capabilities the boundary must not hold (`:L1545-L1556`) and
`NoExportedTypeMentionsIoNetworkDatabaseConfigurationOrAmbientStateInItsSignature` (`:L1595`) fails the
build if any exported contract type mentions one. So the clock seam is provably **not** smuggled into the
published boundary. That project builds and that test passes ([`BUILD.md`](BUILD.md) §1.3 carries the
count), which is why this one can be cited as a result rather than as an intention.

| Seam | Where it originates | Why it must be seamed | Status |
| --- | --- | --- | --- |
| GUID generation, random string generation, random blob generation | The cryptographic surface — `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14-L18` (`GenRandomBlob`, `GenRandomString` in two arities, `GenGUID` in two arities), plus the `guid.srf` and `randomstring.srf` wrappers in the same library | The **primary** non-determinism sources in the in-scope estate. Every value differs on every run by design, so any recording that contains one is unmatchable unless the value is masked on both sides | **Present but unexercised** |
| Transaction-pool idle expiry | CPU-clock based, keyed on the two keep-alive settings — `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans_pool.sru:L76-L79` reads them, `:L97` stamps the idle start, `:L215` compares elapsed against the expiry | Elapsed-time-dependent behaviour cannot be reproduced without a controllable clock. Whether a pooled transaction is reused or discarded is **observable**, so the decision must be reproducible even though the elapsed time itself is never asserted | **Present but unexercised** |
| Every clock read | Throughout the in-scope estate, wherever a timestamp reaches an output or a decision | A timestamp in recorded output differs on every run. Seaming the clock is what makes a recording containing one comparable at all | **Present but unexercised** |
| Modify-call ordering | The DataWindow modify path, where a sequence of property modifications is applied to reach a target state | Two orderings that reach the same state can emit different intermediate output. An unpinned ordering surfaces as a spurious difference — a diff that reports a change where no behaviour changed | **Present but unexercised** |

Each status above is checkable, because the mechanism carrying it is named. Taking the four in order:

- **Randomness — injected through a dedicated abstraction, not through the BCL static.** The seam is
  `IEntropySource`, declared at
  `services/security-service/PowerFramework.Security/Crypto/RandomProvider.cs:L228`; production takes
  `CryptographicEntropySource` (`:L269`), which fills from `RandomNumberGenerator.Fill` at `:L279`; and
  `RandomProvider` reaches entropy *only* through the constructor parameter at `:L578`. That last point is
  what makes the seam total rather than partial — `Guid.NewGuid()` and `Random.Shared` are deliberately
  unreachable from the provider, so no code path can bypass the double. Two doubles are written:
  `SequencedEntropySource` and `ConstantEntropySource`, at
  `services/security-service/PowerFramework.Security.Tests/RandomProviderTests.cs:L81` and `:L108`.
- **Transaction-pool idle expiry — injected as `TimeProvider`, taken by constructor, and it is the SAME
  clock that drives the other two legacy clocks in this service.** The type exists:
  `services/persistence-service/PowerFramework.Persistence/Transactions/TransactionPool.cs`, alongside
  `Transactions/TransactionPoolIdleSweeper.cs` and `Transactions/TransactionData.cs`. The pooled
  transaction holds the clock as a field (`TransactionPool.cs:L1710`), takes it as a constructor parameter
  (`:L1773`) and captures a monotonic origin at construction (`:L1781`); expiry is then decided by
  `_timeProvider.GetElapsedTime(_monotonicOrigin, _timeProvider.GetTimestamp())` (`:L2575`), and the pool
  itself and its options type hold the same clock (`:L2877`/`:L2906`, `:L3418`/`:L3539`). **`GetTimestamp`
  against a construction-time origin rather than a wall-clock read is load-bearing rather than stylistic**,
  and the file says so at `:L107-L108`: the legacy `CPU()` is elapsed processor time since start, so a
  wall-clock substitute would be a different quantity that happened to be measured in milliseconds.
  `TransactionPoolIdleSweeper` builds its `PeriodicTimer` **on the injected clock** (`:L147`), so a
  deterministic test drives the sweep rather than waiting for it.
  The doubles are the framework's own `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`,
  advanced explicitly: `TransactionPoolTests.cs` drives the 30-second idle window and the 10-second
  liveness cache off one shared fake (`:L233`, `:L260`) across **113** facts and theories, and
  `TransactionPoolIdleSweeperTests.cs` advances it in 5-, 20- and 50-second steps and by an hour (`:L98`,
  `:L143`, `:L164`, `:L167`, `:L193`) across **7**. No test in either file waits on real time.
- **Clock reads — injected as `TimeProvider`, taken by constructor, with no ambient read behind it.**
  Present at
  `services/dataservices-service/PowerFramework.DataServices/Domain/ValidationSession.cs:L858,L909`,
  `services/persistence-service/PowerFramework.Persistence/Buffers/DataWindowBuffers.cs:L1969,L2022`,
  `services/persistence-service/PowerFramework.Persistence/Buffers/ChangesetCodec.cs:L1974` and
  `services/gateway-service/PowerFramework.Gateway/Clients/SecurityClient.cs:L746`. Three doubles are
  written: `DeterministicClock`
  (`services/persistence-service/PowerFramework.Persistence.Tests/DataWindowBuffersTests.cs:L70`),
  `ValidationSessionTestClock`
  (`services/dataservices-service/PowerFramework.DataServices.Tests/ValidationSessionParityTests.cs:L48`)
  and `MutableClock`
  (`services/dataservices-service/PowerFramework.DataServices.Tests/SecurityClientTests.cs:L112`). The
  BCL `TimeProvider` is the abstraction rather than a bespoke clock interface, deliberately: it is
  designed to be subclassed for exactly this, so no second clock abstraction is introduced.
- **Modify-call ordering — the injection mechanism is the carrier-surface interface, named here because it
  is the least obvious of the four.** Ordering is not made deterministic by substituting a *clock*; it is
  made **observable** by routing every property call through one injected interface whose calls a test can
  record in order. That interface is `IFullStateCarrierSurface`
  (`services/persistence-service/PowerFramework.Persistence/Buffers/FullStateCodec.cs:L338`), declaring
  `Describe` (`:L353`), `Modify` (`:L367`), `SetSort` (`:L379`) and `SetFilter` (`:L390`), and taken as a
  parameter by `SynchronizeSortAndFilter` (`:L830`) and `ApplyNoUserPromptWorkaround` (`:L959`). The
  double is `RecordingCarrierSurface`
  (`services/persistence-service/PowerFramework.Persistence.Tests/FullStateCodecTests.cs:L70`), which
  appends every call to an ordered `Calls` list (`:L73`, appended at `:L90`, `:L98`, `:L106`, `:L114`) so
  a test asserts the **exact sequence** rather than the end state — for example
  `["SetSort:age A salary A", "SetFilter:age > 1"]` at `:L481`. Whether a modify was issued at all is
  carried separately by `NoUserPromptOutcome.ModifyAttempted` (`FullStateCodec.cs:L529`), because a port
  that emits a modify the legacy would not have made is a divergence even when the resulting state agrees.

### 5.2 How a seam is expressed, and what a mask is not

A seam is an **injected dependency**, not a comment and not a naming convention. The concrete form: the
value's producer is an interface resolved from the container; production registers the platform
implementation; a characterization or unit test registers a deterministic double. A "seam" that is
documented but not injected has no effect on a single test, and
[`CONTRACTS.md`](CONTRACTS.md) records the same three random generators as seams on the contract side
precisely so that a consumer writing a parity test knows which fields to mask.

Two distinctions worth keeping sharp:

- **A mask is not a substitute for a seam.** Masking removes a field from the comparison; seaming makes
  the field reproducible. Prefer the seam. Mask only where the value cannot be made deterministic
  without changing observable behaviour — a mask silently narrows what the suite can detect, and every
  masked field is a field in which a regression can hide.
- **A mask applies symmetrically or not at all.** A mask configured on the target side and not on the
  legacy side is worse than no mask, because it produces a passing comparison from mismatched data. §4.1
  puts the mask with the workflow for this reason.

### 5.3 Determinism is a property of the pair, not of a run

One closing point, because it is where this section is most often misapplied. A single reproducible run
proves nothing about a pair. The question a seam has to answer is not "does this run twice the same?"
but "does the recorded legacy value and the produced target value agree after identical masking?" Each
seam above is therefore to be configured **once per workflow** and used for both captures, alongside the
volume state that §4.2 pins. Stated as a requirement rather than as a practice, because §4 records that no
paired recording exists yet: no seam has been configured for a workflow, for the plain reason that there is
no workflow capture to configure it for.

---

## 6. Test shape

### 6.1 Table-driven parity matrices

Parity assertions take the form of **table-driven matrices expressed as theories with member data** —
one row per input case, one expected output per row. The shape is chosen for three reasons that all
matter here: a new case is a data row rather than a new test method, a failure names the specific row
that diverged rather than a method that covers many cases, and the matrix itself is readable as a
specification of the behaviour it pins.

**The principal case is the six-column marked table of the primary fixture** (§3.2): all six columns
`update=yes updatewhereclause=yes`, `updatewhere=1`, `updatekeyinplace=no`, the identity column present,
and a page-scoped footer aggregate. A matrix over that fixture reaches the concurrency contract, the
identity round-trip, the self-assignment path of [§7.6](#76-the-self-assignment-workaround) and the
expression engine from one place.

Alongside the unit-level matrices, **service-level tests are to run against an in-process host**, so a
test exercises the real endpoint, the real serialization and the real authorization filter rather than a
hand-assembled approximation of them. That distinction matters for a decomposition specifically: several
behaviours in §7 are only observable *across* the boundary, and a test that calls the implementation
class directly cannot see them.

> **This is the current state, not planned architecture.** All four service applications carry an
> entry point and all four service test projects build and pass, and they do use `WebApplicationFactory`
> against the implicitly generated internal `Program` with no `public partial` shim — the Gateway
> authorization, health-aggregation and route-census suites, the Security endpoint suites and the
> Persistence composition-root and endpoint suites all boot an in-process host. `Microsoft.AspNetCore.Mvc.Testing`
> is referenced by all four because all four use it. The per-project totals and the shared-versus-service
> split are in [`BUILD.md`](BUILD.md) §1.3, which is the canonical record; none is restated here.
>
> **What an in-process host still cannot see, and how much of it the bring-up actually covered.** It
> performs no TLS handshake, no ALPN negotiation, no real gRPC channel setup and no client-certificate
> exchange. The container bring-up closed **two** of those four and left two open, and the distinction is
> not pedantic — the two it left open are the two this document's §7 behaviours travel over:
>
> | Gap in an in-process host | Closed by the bring-up? |
> | --- | --- |
> | TLS handshake | **Closed.** Every `/health` gate was answered over TLS against the projected anchor, and in-network reachability was verified with hostname validation |
> | ALPN negotiation | **Closed for HTTP/1.1 only.** The `Http1AndHttp2` listeners answered an HTTP/1.1 request on the very port a gRPC caller negotiates HTTP/2 on; no HTTP/2 negotiation by a gRPC client was observed |
> | Real gRPC channel setup | **Open.** No gRPC RPC has been invoked at all, so no C-03 to C-08 call has crossed a container boundary |
> | Client-certificate exchange | **Open.** The bring-up authenticated callers with the shared-secret scheme and left every `*_MTLS_*` path empty |
>
> Those verdicts are not this document's to publish and are not restated as figures here: they come from
> [`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised), the
> only execution-status record in the repository, and the two open rows are the same two §R4 names. And a
> bring-up is still not a *capture* in any case: no recording has been taken on either side (§1.5).

### 6.2 The paging rewriters are pure-function matrices requiring no storage engine

This is the clearest illustration of how both dialect behaviours are preserved **without fabricating a
schema for either** (C-E, and §3.4).

The legacy paging rewriter is dispatched by a `choose case` on the connection's dialect selector at
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L320`. Every arm is a **pure string
transform**: input statement plus page size plus page index yields output statement. Nothing is
executed, nothing is connected to, and nothing is parsed by an engine. So the entire behaviour is
testable as a function matrix — **input string in, expected string out, no storage engine of any kind
involved.**

The parity criterion is therefore unusually strict and unusually easy to check:

> **Parity is byte-exact generated SQL.**

Which means the sentinel identifiers the legacy emits are part of the contract and are reproduced
exactly, doubled letters and all:

| Sentinel | Verified locators | Note |
| --- | --- | --- |
| `pfwPagedSQL_OutterTbl` | `:L333`, `:L350`, `:L362` | **The doubled `t` is the legacy spelling.** It is reproduced verbatim; "OuterTbl" would be a byte-level difference in every generated statement that uses it |
| `pfwPagedSQL_RN` | `:L355`, `:L356`, `:L381`, `:L382`, `:L383`, `:L394`, `:L395` | The row-number alias, and in one form the outer sort key |
| `pfwPagedSQL_TblInner` | `:L394` | Second dialect arm's triple-nesting, middle level |
| `pfwPagedSQL_TblInnerInner` | `:L394` | Second dialect arm's triple-nesting, innermost level |
| `pfwPagedSQL_TblOuter` | `:L394` | Second dialect arm's triple-nesting, outermost level |
| `pfwPagedSQL_Tbl` | `:L356`, `:L382`, `:L834` | **A sixth sentinel, verified.** Distinct from the four `..._Tbl*` spellings above and easy to miss because it is a prefix of two of them |

Reproduce the register with:

```bash
grep -rho "pfwPagedSQL_[A-Za-z_]*" ws_objects/ | sort | uniq -c
```

And the count wrapper carries its own literal: the count form replaces the select list with the alias
**`"1 AS _"`** [`n_cst_thread_task_sqlquery.sru:L830`], strips the order-by when one is present
[`:L831-L833`], and wraps the result as a subquery aliased `pfwPagedSQL_Tbl` [`:L834`]. All three steps
are observable in the generated text, so all three **must be** pinned.

> **The paging matrix now EXISTS, and the withdrawal recorded here in an earlier revision is itself
> withdrawn.** `SqlServerPagingRewriter`, `OraclePagingRewriter` and `PagingRewriteDispatcher` are present
> in the tree and all three are driven: `SqlServerPagingRewriterTests.cs` carries 27 test methods across the FOUR ARMS OF THE
> SQL Server 2x2 matrix, `PagingRewriterByteExactTests.cs` carries 3 more, and between them they cover the
> dispatcher's `DBT_MSSQL` / `DBT_ORACLE` selection and its `E_NO_IMPLEMENTATION` arm for anything else.
> `PowerFramework.Persistence.Tests` builds and passes in full — its total is in
> [`BUILD.md`](BUILD.md) §1.3 with every other measured figure and is deliberately not repeated here, which
> is the rule this document states for itself two subsections earlier and had broken in this one sentence.
> So the sentinel register, the
> four-form cross-product and the count wrapper above are pinned by byte-exact assertions rather than
> specified for someone else to write — which is what makes them testable with no instance of either DBMS,
> the property that let this matrix be written at all.
>
> **It is a 2×2 matrix, not the three strategies the migration plan's prose names.** The two selectors —
> whether the unique-index column list is non-empty, and the state of the native-implementation flag —
> are independent, and all four arms emit different statement text, so merging any pair breaks byte-exact
> parity outright. `SqlServerPagingRewriter.cs` records the correction at its own point of reproduction.
>
> **The limitation that remains is the one that matters, and the suite states it about itself.** Its
> expectations are **transcribed from a run of the .NET rewriters, not derived from the legacy generator**,
> because `n_sql` is a closed binary with no C++ source in this repository and obtaining the oracle's own
> output would require executing PowerBuilder. It is therefore a **target characterization**: it detects
> drift, it makes any change to a rewriter visible in review, and it does **not** certify agreement with
> `pfw.dll`. Closing that gap needs a legacy-side capture (§4), not another test.
>
> **The rows the matrix must contain**, so the obligation is checkable rather than gestural — and so a
> reader can audit which of them a future oracle capture has to cover:
>
> | # | Case | Expected outcome |
> | ---: | --- | --- |
> | 1 | First dialect arm, unique-index columns supplied, native paging selected | Byte-exact statement for that form |
> | 2 | First dialect arm, unique-index columns supplied, native paging not selected | Byte-exact statement for that form |
> | 3 | First dialect arm, no unique-index columns, native paging selected | Byte-exact statement for that form |
> | 4 | First dialect arm, no unique-index columns, native paging not selected | Byte-exact statement for that form |
> | 5 | First dialect arm, input statement with no order-by | The scalar-subquery substitution [`:L370`] |
> | 6 | Second dialect arm, triple-nested row-number form [`:L394-L395`] | Byte-exact statement, all three nesting aliases present |
> | 7 | Second dialect arm, input statement with no order-by | The empty-string-literal substitution [`:L392`] |
> | 8 | Any other dialect selector | `RetCode.E_NO_IMPLEMENTATION`, **not** `E_NO_SUPPORT` [`:L396-L398`] |
> | 9 | Count form, order-by present | Select list replaced with `"1 AS _"`, order-by stripped, wrapped and aliased `pfwPagedSQL_Tbl` |
> | 10 | Count form, no order-by | Same, with no strip step observable |
> | 11 | Page size or page index at or below zero | The invalid-paging-setting outcome, pre-dispatch |
>
> Each row's expectation must ultimately be derived from the **legacy generator** at
> `n_cst_thread_task_sqlquery.sru:L320-L399` and `:L830-L834`. The present suite asserts the .NET
> rewriter against a transcription of its own output, which is what a target characterization is: it
> detects drift, and on its own it cannot establish agreement with the oracle. That is the precise reason
> the paired capture of §4 is a prerequisite for a parity *claim* rather than a nicety.

Three further points the matrices must cover, all verified at source:

- **The first dialect arm generates four statements, not three.** It is governed by two independent
  booleans — whether unique-index columns were supplied [`:L323`] and whether native paging was selected
  [`:L343`, `:L366`] — so the arm has a two-by-two cross-product of outcomes and a matrix that covers
  three of them leaves a form untested. [`CONTRACTS.md`](CONTRACTS.md) enumerates all four with their
  locators.
- **The second dialect arm is a single triple-nested row-number form** [`:L394-L395`], and the two arms
  substitute *different* placeholders when the input statement has no order-by: one substitutes a
  scalar-subquery form [`:L370`], the other an empty-string literal [`:L392`]. Both substitutions are
  observable, so both are pinned separately.
- **The `case else` arm returns `RetCode.E_NO_IMPLEMENTATION`** [`:L396-L398`], and that is a *distinct*
  code from `RetCode.E_NO_SUPPORT`. A matrix row asserting the wrong one of the two would pass a
  casual review and encode the wrong contract.

### 6.3 A test project per shippable project

Every shippable project has a sibling test project — the six shared libraries and the four service
applications alike. Two reasons, and the second is the one specific to this document:

1. A per-project boundary keeps a test's failure attributable to the project that owns the behaviour.
2. **The coverage gate is evaluated per service** ([§8](#8-coverage-gate-mechanics)), which is only
   meaningful if the measurement boundary matches the deployment boundary.

[`BUILD.md`](BUILD.md) carries the commands, the hand-authored xunit.v3 requirement and the coverage
mechanics as a build concern; this document does not restate them.

### 6.4 What is compared, and what is never compared

Compared: returned values and return codes; generated SQL text; DataWindow buffer contents including
original values; item statuses; event sequences and their ordering; structured error payloads field by
field; identity values and their array positions; expression results.

**Never compared: execution time.** See §1.6. A recording holds no duration and a suite asserts none.

---

## 7. The catalogue of behaviours to reproduce

This section is the document's payload. Every item below is a **legacy behaviour that must survive the
migration**, and the framing throughout is **reproduce and annotate** — never repair. Where an item is a
defect, the annotation exists so that a future reader cannot mistake the reproduction for an
implementation error and "put it right".

Two conventions apply to the whole section. Each item carries the locators that establish it, so a
reviewer can check the claim rather than trust it. And each item states what a *wrong* port would look
like, because in almost every case the wrong port is the intuitive one.

### 7.1 The tri-state return algebra

Two predicate functions define the framework's entire notion of success and failure, and they do not
partition the value space.

The success predicate tests **greater-than-or-equal-to zero**
[`ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13`]. The failure predicate tests
**less-than-zero, with an explicit exclusion of the cancelled value**
[`ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13`]. Both begin with a null guard that returns false.

Three consequences follow, and all three must be replicated exactly:

1. **A prevention is classified as a success.** `PREVENT` is `1` [`retcode.sru:L42`], which satisfies
   `>= 0`. So a caller asking "did that succeed?" about a *prevented* operation is told yes. Callers
   throughout the framework are written against this, so a port that separates prevention from success
   changes the behaviour of every one of them.
2. **Cancelled is neither succeeded nor failed.** `CANCELLED` is `−2` [`retcode.sru:L44-L45`], excluded
   from failure by the explicit guard and failing the success test by sign. This is **a tri-state hole in
   a nominally boolean algebra**, and it is deliberate: cancellation is not an error and is not an
   accomplishment.
3. **Null is likewise neither**, because both predicates return false on null input.
   **Never collapse null to zero.** Zero is `OK`/`SUCCESS`/`ALLOW`
   [`retcode.sru:L39-L41`], so collapsing null to zero converts "neither succeeded nor failed" into
   "succeeded" — a silent state promotion, in the single most widely consumed predicate pair in the
   framework. Value types are therefore ported as nullable value types and the predicates handle null
   explicitly.

**A further quirk to preserve: the boolean overloads collapse a distinction the numeric overloads
keep.** The boolean overload of the failure predicate [`isfailed.srf:L15-L17`] and the boolean overload
of the prevention predicate [`ws_objects/pfw.shared.pbl.src/isprevented.srf:L15-L17`] are **textually
identical negations** of their argument. So in the boolean form, prevented and failed are
**indistinguishable** — while in the numeric form they remain distinct, prevention being an equality
test against `PREVENT` [`isprevented.srf:L11-L13`]. Preserve the collapse. An implementation that
"strengthens" the boolean overloads would make two call sites disagree that today agree.

**One related asymmetry, verified and worth recording** because it is the kind of thing a reviewer
flags as an inconsistency and tries to smooth over: the *allowance* predicate treats null as **allowed**,
and additionally treats any value above 1000 as allowed
[`ws_objects/pfw.shared.pbl.src/isallowed.srf:L11`]. So null is *neither* succeeded nor failed but *is*
allowed. The three predicates genuinely do disagree about null, deliberately — allowance is a permissive
gate and success is an assertion of outcome. Reproduce all three as they are.

### 7.2 The hardcoded default locale

`ws_objects/pfw.pbl.src/pfw.sra:L94` hardcodes the locale to the two-letter English tag, inside an open
event that reads in strict order: initialize the framework with the all-capabilities flag
`Enums.INIT_FLAG_ENABLE_ALL` [`:L91`], set the locale literally [`:L94`], select one of three provider
classes from it [`:L95-L102`], install the selected provider [`:L103`], and open the demonstration
selector [`:L105`].

**The defect is the hardcoding.** The distinction that governs the port is between an *observable
default* and a *structural property*:

- The **observable default** is that the framework comes up in English unless something says otherwise.
  That is behaviour, and it is preserved: English is the default value.
- The **un-configurability** is structural — an artifact of a literal in a source file, not a behaviour a
  caller can detect. Reproducing it would mean shipping a service whose locale cannot be set, which is
  not fidelity to a behaviour; it is fidelity to a limitation.

So the value is preserved as the default and made overridable through configuration. A recording made
without an override sees English, exactly as the legacy does.

### 7.3 Static versus dynamic expansion

The distinction between the two column-expression variable-reference forms — the single-sigil static form
and the double-sigil dynamic form — is the behaviour the requirements correctly identify as one that
"does not survive naive serialization".

The authoritative legacy specification is
[`n_cst_dwsvc_columnexp.md`](n_cst_dwsvc_columnexp.md), and its worked examples are definitive. They have
been read at source and the line references check out:

| Form | Section | Worked example | What the example establishes |
| --- | --- | --- | --- |
| **Static** (`$name`) | §静态展开, `L37-L54` | `L43-L54` | The variable's value is substituted **at the moment the expression is set**. The document states the result is fixed at 5 [`L48`] and that a later assignment to the variable followed by a recalculation still yields that same value [`L50-L53`] |
| **Dynamic** (`$$name`) | §动态展开, `L56-L73` | `L62-L73` | The variable is retained **as a reference** and evaluated at calculation time. The identical sequence yields the updated value — the document states the result changes to 6 [`L69`] |

The mechanism behind the difference is what makes it a serialization problem rather than a formatting
one. The parse routine rewrites the expression **in place**: a static reference is resolved and
substituted into the rewritten text at bind time, so the variable name is *gone* from the stored
expression and no later assignment can reach it; a dynamic reference leaves behind an entry indexing into
the live variable table, so a later assignment propagates.

Two consequences for parity:

- **An already-expanded expression string is not a faithful recording.** It makes a static binding
  indistinguishable from a literal and strips a dynamic binding of the environment it resolves against. A
  recording must carry the unexpanded source text, the bind-time snapshot, the live environment, and the
  expansion mode **per reference** — the specification documents an example mixing both forms inside one
  expression [`L68`], so the mode is a per-reference property and never a per-expression one.
- **The document also defines two further forms** the parity matrices must cover: the foreign-variable
  pattern, which the specification notes *requires* the dynamic form [`L30-L35`], and a dynamic-indirect
  form in which the variable name is itself computed at runtime [`L75-L79`].

[`CONTRACTS.md`](CONTRACTS.md) (C-04) carries the wire consequence, including the one hard limit recorded
here as [R2](#r2--cross-session-foreign-column-expression-variables-are-narrowed-by-design).

### 7.4 The two localization mistranslations, and three shapes around them

**Two entries in the localization resource `pfw.i18n.xml` are wrong.** The minimize label is rendered
with the English word for its *opposite* [`pfw.i18n.xml:L4`], and a hide label is rendered as a corrupted
mixed-language string [`pfw.i18n.xml:L49`]. The locators are given so an implementer can read the exact
values from the read-only source; they are deliberately not re-transcribed here.

**Both are reproduced.** And the reason this is not merely awkward is what sits next to them in the
source:

> **The English provider retains a commented-out, pre-resource translation table that contains the
> *correct* wording** [`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L58-L153`] — the
> right minimize word at `:L64-L65` and the right hide wording at `:L149-L150`.

**That table must not be revived.** It is sitting right there, it is obviously the intended wording, and
uncommenting it is a two-character edit — which is exactly why it is called out. Reviving it would be
precisely the silent correction the requirements forbid, and it would change output that a recording
already pins. The live path is the resource lookup at `:L51`; the commented table is inert history.

Three provider shapes travel with this and are reproduced as they are:

- **The Simplified Chinese provider mutates no text — but it is not a no-op, and the difference is
  observable.** It is 28 lines long and carries **no translation table at all**, because Simplified Chinese
  **is** the base locale and there is nothing to translate into. What it still does is *answer*: its
  translate handler [`ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_chs.sru:L19-L27`] returns **1,
  meaning handled**, for framework-sourced text, and **0, meaning not handled**, for everything else. Those
  are two different answers to the caller, so "no-op" is the wrong word for it: a genuine no-op would
  return one value unconditionally, and collapsing the `return 1` into the `return 0` would discard the
  claim that no further processing is required for framework text. Call it a **no-mutation base-locale
  provider**. The three-provider shape including this one is reproduced; collapsing it to two providers
  would change which provider a locale selects. `SERVICE_MAPPING.md` states the same distinction from the
  mapping side, and the .NET provider preserves both return values with a comment saying why neither may be
  merged into the other.
- **The three providers are of very different sizes, and that is expected**: English 164 lines,
  Traditional Chinese 68 lines [`n_cst_i18n_cht.sru`], Simplified Chinese 28. Both non-base providers
  read the same resource table [`n_cst_i18n_en.sru:L158-L159`, `n_cst_i18n_cht.sru:L62-L63`].
- **The fallback is a silent passthrough.** With no provider installed, the text is returned
  **unchanged** — never throw, never log, never mark the text as untranslated. A port that logs a warning
  here would emit output the legacy never produced, on a path taken every time a translation is
  requested before a provider is installed.

### 7.5 The four cross-thread transfer defects

Cross-thread transfer of DataWindow state is the legacy's own marshalling boundary, and its source
comments document four defects at that boundary. All four survive:

| # | Behaviour | Locator | What the legacy does about it |
| ---: | --- | --- | --- |
| 1 | **Full-state transfer requires the sort and filter conditions to be synchronized** | `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru:L562-L563` | Applies both explicitly in the crosstab/composite arm, and fails with `RetCode.E_INVALID_ARGUMENT` if either application is rejected [`:L565-L575`] |
| 2 | **Changeset application may lose rows on a sorted DataWindow larger than one block** | `:L147-L149` | Moves rows through a temporary datastore to route around it [`:L152`], conditional on both a multi-block result and a non-empty sort [`:L151`] |
| 3 | **Reset must not be used to clear data**, because doing so makes changeset application fail to apply | `:L175-L176` | Discards rows explicitly instead of resetting [`:L178`] |
| 4 | **Full-state transfer crashes when a crosstab has too many columns** | `:L672-L673` | Suppresses the interactive prompt before transferring [`:L674-L676`] |

Defect 1 has a counterpart worth stating precisely, because it is where C-B bites in the middle of a
technical note. The changeset style does **not** require the sort and filter, and the legacy clears both
in that arm [`:L579-L580`]. The comment at `:L577-L578` records that they are unnecessary there and then
volunteers a further rationale of a kind this document may not assert (§1.6). **Only the behavioural half
is carried across** — that the two are cleared, and that clearing them is observable in the transferred
state. The other half is deliberately not restated.

The practical shape of the reproduction: the two transfer styles are **not** interchangeable
optimizations to be unified. They differ in what they carry, in which defects they exhibit, and in what
must be synchronized first. A port that implements one and routes both styles through it will pass a
naive round-trip test and diverge on precisely the cases these four comments exist to describe.

#### 7.5.1 The retrieval stamp — a documented non-defect, recorded because it reads like one

A freshly retrieved, unmodified row arrives on the wire with a **row-level** item status of
`DataModified!` while **every one of its columns** reads `NotModified!`. That looks wrong on first
reading, it has been raised as a finding once, and it is **correct**. It is recorded here so it is not
raised again and, more importantly, so it is not "fixed".

**It is the oracle's own behaviour.** Six loops in
`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlquery.sru` stamp every row of a freshly retrieved
buffer `DataModified!` at column zero **before** the changeset is extracted:

| # | Locator | Which arm |
| ---: | --- | --- |
| 1 | `:L122-L124` | Drop-down child, primary buffer |
| 2 | `:L126-L128` | Drop-down child, filter buffer |
| 3 | `:L161-L163` | Temporary carrier, primary buffer |
| 4 | `:L167-L169` | Temporary carrier, filter buffer |
| 5 | `:L196-L198` | In-place branch, primary buffer |
| 6 | `:L201-L203` | In-place branch, filter buffer |

Column zero is not a column; it addresses the row itself. Every one of the six passes zero and
`DataModified!`.

**And it is load bearing, which is the half that matters.** A changeset carries only rows that changed,
so the stamp is the transport's **precondition** rather than a claim about the data. With the row left
`NotModified!` the projection admits no rows at all, so a retrieval over a populated table answers a
well-formed, entirely **empty** result and reports success — a silent wrong answer far worse than a
status field that reads oddly. Both halves are pinned executably by
`ChangesetCodecReceiveTests.ARowStampedNotModifiedIsDroppedFromThePayloadEntirely`, which encodes the same
carrier twice and changes nothing but the row status.

**What the stamp does not claim** is the reader's real question, and the answer is that nothing downstream
treats such a row as carrying edits. The per-column statuses stay `NotModified!`, and C-06 measures a
submitted row against its stated originals — so a retrieved row echoed straight back is refused with
`E_INVALID_DATA` rather than generating an update. Correcting the stamp would therefore change no
concurrency outcome while breaking every retrieval, and AAP §0.2.2.5 forbids the correction in any case.

### 7.6 The self-assignment workaround

`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L151-L154` carries an explicit
fix-me comment from the legacy authors. It states that when the key field the caller has requested is
not itself marked as a key, the modified state will **not** generate the delete-and-insert pair the
update requires, and that the internal modified state must therefore be force-refreshed to compensate.

Lines `:L155-L167` implement the compensation, and the implementation is the interesting part:

- It is **gated on the runtime value** of the key-in-place setting, read back from the DataWindow rather
  than assumed [`:L155`].
- It walks only rows whose row-level status is "data modified" [`:L160`] and, within those, only key
  columns carrying the same status [`:L162`].
- And for each such column it **assigns the value to itself** [`:L163`] — `x = x`, purely to flip the
  item's status as a side effect.

**There is no .NET analogue for a self-assignment that mutates hidden state.** A C# `x = x` is a no-op
and an analyzer will say so. The port therefore models original-value and status tracking **explicitly**,
as first-class state rather than as a side effect of the assignment operator, and reproduces the same
generated statements. Parity is measured on the statements, not on the mechanism: the legacy's
self-assignment is a means, and the delete-plus-insert pair it produces is the end.

**This path is exercised by the primary fixture, not a rare branch.** `dw_sqlite.srd:L14` sets
`updatekeyinplace=no` itself (§3.2), which is exactly the gate condition at
`n_cst_thread_task_sqlupdate.sru:L155`. So any recording of an update over the primary fixture that
modifies a key column runs straight through this code. Treating it as an edge case is the most likely way
to ship a port that fails on its first characterization run.

---

### 7.7 The item-change micro-protocol

`ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L182-L253` is the most intricate behaviour in
the in-scope set, and the first thing to understand about it is what its return values are **not**:

> **The item-change return alphabet is not the return-code constants.** It is `{0, 1, 2, 3}`, with its own
> meanings, and it must be modelled as its own enumeration. Mapping it onto the return-code algebra of
> §7.1 would give `1` the meaning "prevented, which reads as succeeded" — which is not what `1` means
> here at all.

The handler in order, with locators:

1. **Gate early-out** [`:L187`]. If the item-change bit is disabled the handler returns 0 immediately.
   The bit is `EID_ITEMCHANGE = 4` [`:L43`], and the source comment on that line records the coupling
   that matters: **disabling item-change also suppresses column-expression calculation.**
2. **Snapshot both the original value and the row's item status** [`:L189-L190`]. Two snapshots, not one;
   the status is restored independently of the value later.
3. **Set the re-entrancy flag, fire the semantic handler, stash its result, restore the flag**
   [`:L192-L196`]. The flag is saved and restored rather than merely set and cleared, so nesting works.
   The stashed result is consumed by a *different* event — see below.
4. **Equality test with an explicit null-and-null arm** [`:L198-L202`]. Two nulls compare equal here.
   This is not incidental: without that arm the null case would fall into the unequal branch and fire an
   event the legacy does not fire.
5. **If unequal, re-read the value from the buffer and fire the nested changing event** [`:L204-L208`].
   The value is re-read rather than reused, because the handler may have altered the buffer.
6. **Dispatch on the stashed result** [`:L211-L251`]:
   - **`case 1` is an EMPTY arm and does NOT fall through** [`:L212`]. PowerScript `choose case` is not a
     C `switch`: an arm with no statements executes nothing and control leaves the construct. So `1`
     returns exactly as the semantic handler produced it, with value and status **untouched**. The empty
     arm is load-bearing precisely *because* it is empty — it stops `1` from reaching `case else` — and
     the source comment immediately above the dispatch [`:L210`] says why that matters: returning 1 is
     what raises `ItemValidationError`, and it is *that* handler which restores value and status
     [`:L369-L379`], after reading the stashed code. Restoring here as well would restore twice and would
     do it too early. A port that gives `1` a body changes behaviour; a port that omits `1` from the
     switch changes it differently; and a port that treats `1` as `2` restores where the legacy leaves
     things alone. `Proto/dataservices.v1.proto` and [`CONTRACTS.md`](CONTRACTS.md) §6.5 settle this the
     same way — reading the arm as falling through is the mistake to avoid.
   - **`case 2` restores the value and status, but only if the earlier equality test held**
     [`:L216-L222`]. The guard exists because the buffer may already have been changed and must not be
     overwritten.
   - **`case 3` keeps the value, does not move focus, and rewrites the result to `1`** [`:L223-L225`]. So
     `3` never leaves this handler — it is observable only through the rewrite and through the *other*
     event that reads the stashed original.
   - **The default arm performs manual type-directed coercion** [`:L226-L250`], switching on the **first
     five characters of the column type** across a fixed prefix set [`:L231-L244`] — the character, decimal
     and numeric, integral, datetime, date and time prefixes, each with its own conversion. It then fires
     the changed event [`:L247`] and **forcibly returns `2`** [`:L250`] so the runtime will not re-apply the
     edit text over the buffer. The comment at `:L248-L249` states the reason: the changed handler may
     itself have altered the buffer, and the forced `2` is what stops that work being overwritten.

**Two adjacent orderings are also contract.**

The changed-event dispatcher [`:L295-L320`] fires in **strict sequence**, and the sequence is the
behaviour: the column-expression service's changed handler first, **and only if that service is enabled**
[`:L313-L315`]; then the event-broker trigger, **and only if the topic has a subscriber** [`:L316-L318`];
then the semantic changed event unconditionally [`:L319`]. Both conditions are observable — a
column-expression recalculation that happens before a subscriber sees the change is a different
observable order from one that happens after.

The validation-error handler [`:L322-L385`] is the most defect-laden single event in the set, and every
step is load-bearing:

- **Re-entrancy guard returns `1` immediately** [`:L327`], before anything else.
- **Consumes and clears the stashed item-change code** [`:L331-L332`] — this is the cross-event state
  from step 3 above, and clearing it is part of the behaviour, not housekeeping.
- **Snapshots value and status again** [`:L334-L335`].
- **Pre-sets its result to `1` when the stashed code was `1` or `3`** [`:L338-L340`], so its behaviour is a
  function of the *preceding* event's return value. This is why the item-change chain cannot be reordered
  (see [`CONTRACTS.md`](CONTRACTS.md) for the ordering pattern assigned to it).
- **Otherwise fires the error event with a null-to-zero coercion** [`:L342-L345`] — here null *is*
  coerced to zero, deliberately and locally, which is not a licence to do so in §7.1's predicates.
- **On a zero result with non-empty data** [`:L347-L358`]: reads the column's validation message
  [`:L350`], **strips the outer two characters** [`:L351-L353`] guarded by a length check, falls back to a
  localized string when the message is empty or a single question mark [`:L354-L356`], shows a localized
  dialog [`:L357`], and sets its result to `1` [`:L358`].
- **On empty data it clears the guard and returns `3`** [`:L359-L363`], with the comment recording the
  intent: empty input is refused here and re-checked at save time.
- **Restores value and status for results `1` and `3` only when the row still exists and the stashed code
  was not `3`** [`:L366-L380`]. **That row-existence check must survive** [`:L369`]. It is defensive
  because the dialog's posted message may have deleted the row in the meantime — the comment at `:L368`
  says so explicitly. It looks redundant; it is not. A port that drops it is one deleted row away from an
  out-of-range access on a path a user reaches by clicking a dialog.

**One dormant path, carried across inert.** The pre-change handler [`:L256-L293`] contains a
**commented-out** byte-length check with its own dialog [`:L280-L290`] — it measures the input against the
column's declared character width and rejects over-long input with a message about byte counts. It is
**carried across as commented and inert, not revived.** Reviving it would add a validation the legacy does
not perform, and it interacts with the address-width mismatch of §3.5 in a way that would change results
on the primary fixture.

Finally, the deferred-accept idiom: the kill-focus handler posts a deferred accept **only when the
item-change flag is clear** [`:L387-L390`]. A headless service has no Win32 message pump, so the post
becomes an explicitly queued continuation on the validation session — but the *condition* is preserved
exactly, because whether the accept happens at all is observable.

### 7.8 The non-localized expression messages

The column-expression engine reports its parse and calculation errors through **28 dialog call sites**,
all of them live, and **none of them routes through localization** — unlike the equivalent messages in
the DataWindow service, row-select and context-menu code, which do (§7.7 shows one of those at
`se_cst_dw.sru:L355-L357`).

Verified rather than asserted:

```bash
F=ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru
grep -c "MessageBox" $F                                  # -> 28
grep -n  "MessageBox" $F | grep -c "^[0-9]*:[[:space:]]*//"   # -> 0   (none are commented)
grep -c "I18N" $F                                        # -> 0   (no localization call anywhere)
```

The 28 sites are at `:L713`, `:L739`, `:L762`, `:L1308`, `:L1383`, `:L1390`, `:L1395`, `:L1399`, `:L1417`,
`:L1422`, `:L1426`, `:L1431`, `:L1486`, `:L1505`, `:L1607`, `:L1671`, `:L1882`, `:L1888`, `:L2122`,
`:L2133`, `:L2192`, `:L2208`, `:L2232`, `:L2242`, `:L2254`, `:L2282`, `:L2306` and `:L2392`. Three are
worth naming individually: cache-creation failure [`:L713`], a cache error that carries the text of a
caught runtime exception [`:L739`], and a general expression error [`:L762`]. Parse failures are built by
a dedicated formatter taking the expression, a **caret position** and a message — the first such site is
`:L1308`.

**That inconsistency is legacy behaviour and it is reproduced rather than harmonized.** Two things follow
for the port. Each dialog becomes a structured error result that preserves the original message text, the
caret position where one exists, and the severity — only the delivery channel changes, because a headless
service cannot show a dialog. And the messages **stay unlocalized**: routing them through the localization
layer "for consistency" would change the text that a recording pins, on 28 paths.

### 7.9 The cryptographic weak defaults

The legacy cryptographic surface is weak by modern standards. Every weakness is **preserved as the
default and *annotated*** as a known legacy weakness, in the contract description as well as in the
implementation. [`CONTRACTS.md`](CONTRACTS.md) (C-02) carries the contract-side annotations; this section
records them as parity obligations:

There are **eight**, and the numbering below is the authoritative register's own — the one in
`shared/PowerFramework.Contracts/OpenApi/security.v1.yaml`, which is where each is annotated and which
`CryptoWeakDefaultAnnotationTests` asserts against. Merging two of the pairs gives six, which is why the
numbering is stated as shared rather than local:

| # | Legacy default or limitation | Parity obligation |
| ---: | --- | --- |
| 1 | **ECB is the default symmetric mode** [`enums.sru:L946`], so every mode-omitting overload runs in ECB | The default stays ECB. Changing it would alter the ciphertext of every call that omits a mode |
| 2 | **PKCS#1 v1.5 is the default RSA padding** [`enums.sru:L951`] | The default is preserved on both RSA cipher operations |
| 3 | **No-padding is not selectable** — the legacy declares no such constant, so the padding enum has exactly two members and a request for no-padding is refused | The refusal is preserved as well as the default. These are two obligations, not one: a port could keep the default and still widen the accepted set |
| 4 | **PKCS#5-family symmetric padding only, and not selectable** — not one of the 32 symmetric overloads has a padding parameter | No padding parameter is introduced. Adding one would widen the contract |
| 5 | **No key-derivation function is reachable at all** — no PBKDF2, scrypt, bcrypt or Argon2, and no salt concept exists | Key material is used as **raw key bytes**, exactly as the legacy does. Introducing derivation would change every key, and therefore every ciphertext |
| 6 | **No authenticated encryption** — the mode set is exactly ECB, CBC and CFB, so no GCM, CCM or Poly1305 | Ciphertext carries **no integrity tag**. Adding one would change the output length and format |
| 7 | **1024-bit RSA remains a legal key size** [`enums.sru:L965`] and is not removed from the accepted set | It stays legal. The legacy demonstration code uses it |
| 8 | **The same six-member hash set governs the RSA signature hash** [`enums.sru:L927`], so MD5 — and CRC32, which is not a cryptographic hash at all — are legal signature-hash selectors | The set is not narrowed for signatures. This is the one most easily missed, because it is a weakness produced by *reusing* a set rather than by declaring a weak default |

Items 3, 4, 5 and 6 are established by **absence** — there is no constant to select and no signature that
accepts one. Absence is weaker evidence than presence in general and exactly the right kind here: a
capability the legacy cannot express is one this port must not offer, because offering it would be a new
feature. Each carries its mechanical proof where it is annotated, so none is taken on trust.

Three cells of that surface are **BLOCKED rather than characterized**, because the parameter each needs
exists only inside `pfw.dll` and no test this repository can run could tell a right choice from a wrong
one: keyed and signed use of the CRC32 identifier, any use of the CFB mode, and the chaining mode through
an overload that supplies no initialization vector. `docs/CONTRACTS.md` records all three as narrowings
N1 to N3 with their reason codes. Every cell the oracle's own demo exercises remains fully supported, so
the parity corpus loses nothing it could have measured.

The algorithm identifier sets are preserved exactly as well — the hash types from MD5 through CRC32, the
ciphers from DES through AES-256, and the ECB, CBC and CFB modes — with their identifier **values** and
their identifier **spellings** both frozen, for the reasons in §1.7.

Two notes specific to parity work. The random and GUID generators on this same surface are the primary
determinism seams (§5.1), so a cryptographic parity matrix must inject them or its every row is
unmatchable. And **no key material of any kind appears in this document, in a recording, or in a
fixture-derived artifact** — [`SECRETS.md`](SECRETS.md) holds the locator register, and the posture is
never-replicate.

### 7.10 The fail-fast posture

Two structural failure modes in the legacy are **fatal**, and they stay fatal.

Worker-session creation failure is fatal. And a decoded assertion failure **terminates the application**:
`ws_objects/pfw.pbl.src/pfw.sra:L111-L144` is the system-error handler, and it reads as follows. When the
error's object field marks it as an assertion [`:L114`], the handler splits the error text on a CRLF
separator into an array [`:L115`], requires at least two fields before it will use any of them
[`:L116`] — taking the number and the message from the first two — and when the split yields **exactly
seven** fields [`:L119`] unpacks the remaining five: window, object, event, line and stack trace
[`:L120-L124`]. It then formats a multi-line diagnostic [`:L129-L139`], displays it [`:L141`], and
executes **`HALT CLOSE`** [`:L143`].

The .NET equivalent is **fail-fast startup validation and process termination on structural faults —
never graceful degradation.** The seven-field payload becomes a structured type, the CRLF-split protocol
and the exact `>= 2` and `= 7` arity conditions are preserved, and the terminal step is process
termination.

State the temptation and refuse it: **softening this into warn-and-continue would be a behavioural change
dressed as robustness.** A service that survives a structural fault has not been made more robust — it
has been made to continue in a state its own authors declared unrecoverable, and every observation taken
after that point is untrustworthy. The fail-fast path is also, incidentally, the easier one to
characterize: a process that terminates on a defined condition is reproducible, and one that limps on is
not.

### 7.11 The two threading hazards that constrain the port

The read-only legacy note [`PB多线程绕坑提示.md`](PB多线程绕坑提示.md) is five lines long and documents two
hazards. Both constrain how the SQL task layer may be re-expressed, so both are recorded here. The
document is cited, not touched (§1.2).

- **Hazard 1** [`:L1-L4`]. A worker thread that **synchronously calls a main-thread object's** function or
  event **whose return type is a string or a blob** may cause a memory exception. The note gives the
  trigger [`:L2-L3`]: a conflict arises when the main thread has an unexecuted posted message for an
  object and that object has itself been destroyed. The stated remedy [`:L4`] is to avoid such calls where
  possible, or to return the value through a `ref` parameter instead of as a return value.
- **Hazard 2** [`:L5`]. After a main-thread object has been passed to a worker thread, the worker
  **must null that reference in its uninitialize event** to release it.

**These hazards are why the legacy proxy pair exists.** Every concurrency class in the legacy exists
twice — a caller-side proxy and a worker-side implementation — so that no object is ever touched from two
threads. That duality is a **contract, not an implementation detail**, and the port reproduces it as an
explicit marshalling boundary rather than flattening the pair into a single async method.

Related, and equally contractual: **the thread-affinity annotations in the legacy source comments are
contract, not commentary.** Six SQL classes are annotated as running on the worker thread, exactly one on
the main thread, and four are calling-thread proxies. A port that erases the distinction erases the reason
the hazards above were survivable in the first place.

### 7.12 One-based to zero-based translation is the sharpest hazard

This is **the single most dangerous mechanical hazard in the refactor**, and it earns its place in a
parity document because of what its failure mode looks like: not a crash, but slightly wrong data.

PowerBuilder arrays are **one-based**, and the upper-bound function returns the **last valid index**. C#
arrays are **zero-based**, with a length one past the end. Every ported loop must therefore go through a
centralized one-based indexing helper or be individually audited — including the append idiom that assigns
to upper-bound-plus-one, which appears twice in the identity round-trip alone
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru:L231`, `:L239`]. **A silent off-by-one
here is indistinguishable from a behavioural regression**, because that is exactly what it is.

**The reverse-iteration cases are the most dangerous of all**, and one line is the sharpest example in the
whole refactor. In the identity round-trip, the primary buffer is walked **forward** [`:L228-L233`] and the
filter buffer is walked **backward** [`:L237`] — `for nIndex = nCount to 1 step -1`. The reason is in the
comment immediately above it [`:L235`]: the filter buffer's row order is **inverted** relative to the data
source it was obtained from.

Say it plainly:

> **It looks like a bug. It is not a bug. And "correcting" the direction produces wrong identity values
> that a row-count assertion would not catch.**

Both directions collect the same *number* of rows, so a test asserting counts passes either way. The
values land in the wrong positions in the returned array, and the array is one of two `ref` out-parameters
handed straight back to the caller [`:L243`] to be applied to rows by position. The corruption is silent,
it is data-dependent, and it surfaces later as records carrying each other's identifiers.

**This is exactly the class of regression a characterization suite flags and a unit test does not.** A
recording of the primary fixture holds the identity values in order; a hand-written expectation holds
whatever the author believed the order to be. That asymmetry is the argument for this whole document in a
single line.

---

## 8. Coverage gate mechanics

Coverage is the only quantitative non-functional requirement in the entire brief (C-H, and §1.6), so its
mechanics are stated precisely rather than as an aspiration.

- **The collector emits `coverage.cobertura.xml`, and it does so for every service.** All four service
  test projects build and run, and each produces that report — the exact artifact the gate is measured
  from, not an inferred one. The measured line rates are in [`BUILD.md`](BUILD.md) §1.3, which is the
  canonical record; **all four clear the floor**, and that is the claim this document needs.
- **What the report covers is scoped deliberately, and those rates depend on it.** The collector runs
  unconfigured, so each service's report carries **one Cobertura package per instrumented assembly** — its
  own, plus the shared libraries and the thousands of generated protobuf sequence points that arrive by
  `ProjectReference` from `PowerFramework.Contracts`. The gate **selects the package named for that
  service's own assembly** and reads its line rate; selecting by name *is* the measurement scope, and it is
  expressed inline in the workflow because **no settings file exists anywhere in this repository and none
  may be added**. The report's own top-level rate is far lower and means nothing about any service, so **a
  top-level gate would fail all four services** for a reason unrelated to any service's tests.
  [`BUILD.md`](BUILD.md) §5.5 carries that comparison.
- **CI enforces 80% line coverage per in-scope service.** `.github/workflows/ci.yml` exists and does it, in
  four independent matrix legs. Each leg reads its own service's report and checks three things, the first
  and third of which are what make the second trustworthy: that service's own assembly **appears in the
  report at all**, which proves the leg measured the service it claims to; that assembly's line rate clears
  the floor; and every other package's rate is **printed but never gated on**, so code the root solution leg
  owns can neither raise nor lower the verdict. The legs do not fail
  fast against one another, so every run yields all four verdicts rather than one failure and three
  unknowns. Four services are in scope; the four deferred destinations have no project, no test and
  therefore no coverage figure at all (see [`DEFERRED.md`](DEFERRED.md)), and none of them appears in the
  workflow.
- **What has not been exercised:** a run of that workflow on a GitHub-hosted runner. The gate step was
  extracted verbatim from the workflow and executed locally against all four real reports — passing for
  all four, and failing correctly when the floor is raised above a measured rate, when handed an unfiltered
  report, when handed a report for the wrong assembly, and when no report exists at all. The `actions/*`
  and `docker/*` step versions, the registry authentication and the artifact upload are reviewed rather
  than run.
- **Evaluation is per service, never repository-wide.** This is the load-bearing detail. A repository-wide
  figure lets one service's coverage **mask** another's: a thoroughly tested shared library and a
  thoroughly tested Security service can carry an under-tested Persistence service over the line, and the
  aggregate number would report health that no individual deployable unit possesses. Since each service is
  independently deployable, each must independently clear the bar.
- **The measurement boundary matches the deployment boundary**, which is why there is a test project per
  shippable project (§6.3).

Two mechanical notes that a reader will otherwise trip over, both recorded in full in
[`BUILD.md`](BUILD.md) rather than restated here: the bare test command builds **Debug** even immediately
after a Release build, so measuring coverage against the Release build requires naming the configuration
explicitly; and the test projects are **hand-authored** rather than templated, with the consequences that
follow from that.

One thing the gate does **not** do, stated so no reader infers otherwise: **line coverage is not a parity
measure.** A suite can reach 80% of the lines in a service without pinning a single behaviour from §7.
Coverage is a floor on how much of the implementation is exercised; the characterization recordings and
the parity matrices of §6 are what establish that what is exercised is *correct*. Both are required and
neither substitutes for the other.

---

## 9. Known parity risks and limitations

Nine risks are carried into implementation. Each states its mitigation, and in the two cases where the
correct engineering answer is *report blocked rather than approximate*, that is what it says. The last
three are **runtime coverage boundaries** rather than parity risks: each was observed by driving the
running stack, each is legacy-faithful or contract-faithful, and for each the correct response is to
declare it here rather than to change behaviour.

| ID | Risk | Class | Correct response |
| --- | --- | --- | --- |
| R1 | Pinyin first-letter matching cannot be proven bit-exact from the repository alone — the **flags are documented**, the lookup table, the matching algorithm and the exact fuzzy-equivalence set are not | Parity — the **single genuine parity risk in the in-scope set** | Characterize the table, the algorithm and the fuzzy set from the oracle, else **report BLOCKED**. The flag decoding needs no characterization |
| R2 | Cross-session foreign column-expression variables cannot cross a process boundary | Deliberate contract narrowing | Support co-resident references; **BLOCK the rest with a defined error** |
| R3 | Encrypted-SQLite page-format parity | Out of Phase-1 scope | Provision the unencrypted path; document the limitation |
| R4 | The stack has been brought up, but no capture has been taken against it | Residual gap, not retired | The bring-up is exercised and reported in one place; what is missing is a **capture** on either side. A bring-up establishes the volume seam a paired capture needs; it is not itself a capture, and no comparison may be claimed from it |
| R5 | No authoritative legacy build definition exists to translate | Reconstituting the behavioural oracle | Author the .NET build clean; read the legacy definitions for intent only |
| R6 | The changelog is stale and is not a specification | Evidence discipline | Derive behaviour from source, with a locator on every claim |
| R7 | Paging is not executable against the one provisioned engine, because the preserved dialect resolver classifies SQLite as SQL Server | Runtime coverage boundary — legacy-faithful | Keep the classification; verify the rewriters as pure string transforms (§6.2). **No code change** |
| R8 | `MaxRows` is a post-retrieval assertion rather than a truncation bound | Runtime coverage boundary — legacy-faithful | Keep the ordering and the diagnostic; bound result size by the transport and container ceilings instead. **No code change** |
| R9 | Expression calculation over retrieved data is unreachable through the published contract | Runtime coverage boundary — contract-faithful | Declare the boundary; keep the honest diagnostics. Closing it would be a **new capability** (C-B) |

### R1 — Pinyin first-letter matching cannot be proven bit-exact from the repository alone

The drop-down search service builds a filter expression that calls a pinyin first-letter matching function
[`ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323`], passing the display
column, the user's input and a **flag value of 7**. The risk is narrower than it first appears, and getting
its boundary right decides how much characterization is actually needed.

**The flags ARE documented, so `7` needs no characterization at all.**
[`ws_objects/pfw.shared.pbl.src/enums.sru:L1146-L1149`] declares them under the comment
`//PinyinFirstLetterLike:[flags]`:

| Constant | Value | Legacy comment | Meaning |
| --- | ---: | --- | --- |
| `PY_LIKE_IGNORE_CASE` | 1 | 忽略大小写 | Ignore case |
| `PY_LIKE_IGNORE_WIDTH` | 2 | 忽略全角半角 | Ignore full-width versus half-width forms |
| `PY_LIKE_FUZZY_SOUND` | 4 | 匹配模糊发音（l=n，f=h，r=l） | Match fuzzy pronunciation, the comment naming `l`/`n`, `f`/`h` and `r`/`l` |

`7` is therefore `1 | 2 | 4` — all three enabled. The flag semantics ARE recorded in the repository, at
the locator above, which narrows this risk rather than widening it.
[`ARCHITECTURE.md`](ARCHITECTURE.md) §13 L1 records the same decoding.

**What genuinely remains unprovable from the repository, and it is enough to keep this risk live:**

- **The lookup table exists only inside the closed binary.** There is no table, no data file and no source
  mapping any character to its pinyin initial anywhere in the tree, and no C++ source for the native
  library at all. `pinyinfirstletterlike.srf` declares only two prototypes bound to `pfw.dll` under the
  alias `pfwPinyinFirstLetterLike`.
- **The matching algorithm is unspecified.** Whether the comparison is a prefix, a substring or a
  subsequence over the derived initials; how a non-Han character is treated; and what a multi-reading
  character resolves to are all unrecorded.
- **The fuzzy-sound equivalence set is illustrative, not provably exhaustive.** The comment names three
  pairs. Whether those are the whole set, whether they are symmetric, and whether the set extends to the
  other pairs a Chinese input method would normally fold together cannot be determined from the source.

Two further details from the same call site belong on the record because they shape the matrix: the pinyin
clause is only appended when the input matches an ASCII-letter test [`:L322`], and it is combined
**disjunctively** with the plain display-column filter [`:L319`, `:L323`] — so the pinyin behaviour is one
term of a compound expression, and a matrix must vary the other terms too.

**Mitigation.** Characterize the **lookup table, the matching algorithm and the fuzzy-equivalence set** from
the behavioural oracle, over a deliberately broad input set — Han characters across readings, mixed-script
input, full-width and half-width forms, and each documented fuzzy pair in both directions. The **flag
decoding needs no characterization**, since `enums.sru` supplies it; what needs characterizing is what each
flag actually *does* to a comparison. If the oracle cannot be exercised — and note §1.5: it has **not** been
exercised in this environment — then:

> **Report the pinyin filter as BLOCKED. Do not approximate it.**

The reasoning is specific rather than cautious. A substitute pinyin table gets most characters right and a
minority wrong, so an approximated filter returns **subtly different result sets**: mostly correct, occasionally
missing a row, occasionally including one. That is a regression that looks like correct behaviour, it will
pass review, and it will be discovered by a user rather than by a test. A BLOCKED report is a known gap; an
approximation is an unknown one. This is why no third-party pinyin package was selected — see
[`BUILD.md`](BUILD.md) for the exclusion and its reasoning.

### R2 — Cross-session foreign column-expression variables are narrowed by design

The column-expression engine supports variables that resolve against **another DataWindow's** expression
service, and the legacy holds that association as a **live object pointer**. A pointer cannot be
serialized, and the legacy specification notes that the foreign-variable form *requires* dynamic expansion
[`n_cst_dwsvc_columnexp.md:L30-L35`] — so the reference must survive to calculation time, which is exactly
what a boundary crossing prevents.

**Mitigation.** Foreign references are resolved by a **session-scoped DataWindow handle**, and are supported
only when both DataWindows are co-resident in the same expression session inside one service instance.
References spanning sessions or service instances are:

> **BLOCKED, returning a defined error. Never a silently wrong value.**

This is a genuine, unavoidable **narrowing** of the legacy contract, and it is recorded here deliberately
rather than discovered during implementation. The alternative — approximating a pointer dereference across a
network — would produce results that are wrong in a way no test would obviously catch, which is the same
failure mode as R1 and is refused for the same reason. [`CONTRACTS.md`](CONTRACTS.md) (C-04) carries the
contract-side statement of the limit.

### R3 — Encrypted-SQLite page-format parity is out of Phase-1 scope

The repository ships two native SQLite libraries: a plain one and a cipher-enabled one, at materially
different versions, the latter being the older of the two. Two things follow. The cipher library's
key-derivation and per-page integrity options are **not configurable through any framework API** — nothing
in the framework exposes the relevant pragmas — and the target provider tracks a current SQLite, so it
cannot produce the older page format at all.

**Mitigation.** Provision the **unencrypted path only**, and document the limitation rather than attempting
a format the target provider cannot produce. An attempt would fail in a way that looks like a data-corruption
defect, which is worse than a documented gap. [`ARCHITECTURE.md`](ARCHITECTURE.md) records the same decision
from the storage side.

### R4 — The stack has been brought up, but no capture has been taken against it

**Why the risk is narrowed rather than retired.** The container set IS assembled:
`orchestration/docker-compose.yml` and `orchestration/README.md`
exist alongside all four container definitions and `.github/workflows/ci.yml`, and the bring-up **has** been
exercised: all four images built, all four services reached Docker health `healthy` in the documented order,
and Persistence provisioned a fresh `persistence-db` volume by itself. That is reported gate by gate in one
place —
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised)
— and this document does not restate it.

**What remains, and it is the half this document is about: no capture exists on either side.** §4.2's
capture rule is expressed in terms of a Docker volume, and the seam **is** observed rather than presumed —
but observing that a fresh volume provisions itself and survives a plain `down` is **not** a recording. No
legacy-side capture has been taken, no target-side capture has been taken, and therefore no comparison
exists.

> **No parity result is claimed anywhere in this document.** No sentence here should be read as reporting a
> comparison, a matched pair, or a masked difference that was actually diffed.

**A passing service test does not retire this risk either** — an in-process host mounts no volume, so it
cannot be the target side of a paired capture no matter how thorough it is. Two further gaps travel with
this one and are recorded in the same single statement rather than here: the *caller* half of the
mutual-TLS arm — Gateway's and DataServices' own configured certificate pairs, the issuance half having
since been exercised — and any gRPC call across a container boundary.

**Mitigation.** Take the first pair against one unrecreated `persistence-db` volume, in the order §4.2
prescribes, and record the workflow identifier on both sides. Until then, treat every parity assertion in
this document as a specification.

### R5 — There is no authoritative legacy build definition to translate

The consequence for parity is direct: **the oracle cannot be rebuilt from repository metadata alone.** The
two PowerBuilder project objects disagree with each other on library count and on vendor, and **both
reference a library that does not exist anywhere in the repository**, so neither would build as written.
There are **zero Git tags** across the history, so no release is marked and no known-good build point exists
to characterize against.

**Mitigation.** The .NET build and CI are authored as clean creations, with the legacy project objects read
for build *intent* only. For parity specifically, this means a legacy-side capture depends on a working
PowerBuilder environment that the repository does not describe — which is the practical reason R1's oracle
may be unavailable. [`SERVICE_MAPPING.md`](SERVICE_MAPPING.md) enumerates the build-definition anomalies in
full and [`BUILD.md`](BUILD.md) records why the build is authored from scratch.

### R6 — The changelog is not a specification and is stale

`logfile.md:L1` reads `## 3.0.7.2062(2022-04-14)` while the commit history runs years later, and the history
carries changes the changelog never recorded.

**Mitigation.** **Behaviour is derived from source, never from the changelog**, and every behavioural claim
carries a source locator (§1.3). The changelog is useful for orientation and is evidence of nothing. The same
discipline applies to the five legacy documents in this folder: two of them are cited here as authoritative
specifications, and where any legacy document and the source disagree, **the source wins** — and the
discrepancy is recorded in the .NET tree, never corrected in the legacy file (C-C).

### R7 — Paging is not executable against the one provisioned engine

**The mechanism, and it is one preserved line.** The legacy resolves its dialect discriminator by asking
whether the upper-cased DBMS identifier *contains* `ORACLE`: if it does the answer is `DBT_ORACLE`, and for
**everything else — including SQLite, and including an empty string — the answer is `DBT_MSSQL`**
[`ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_trans.sru:L356-L361`]. The port reproduces exactly that,
and says so at its own point of reproduction: `Transactions/TransactionPool.cs`, on the `GetDbType` member
of the pooled-object contract. The paging dispatcher then selects a rewriter from that answer, with
`E_NO_IMPLEMENTATION` for anything neither value covers
[`n_cst_thread_task_sqlquery.sru:L320-L399`].

**The runtime consequence, observed by driving the stack.** The only provisioned engine is SQLite (§3.4),
so a paged query on the shipped deployment is rewritten into **SQL Server** syntax — `ROW_NUMBER() OVER`,
`OFFSET … FETCH`, the `pfwPagedSQL_*` sentinels — and SQLite cannot execute it. The failure is a provider
error on a statement the rewriter produced correctly, not a fault in the rewriter.

**Why no code change is right, which is the whole of this entry.** Making the resolver recognise SQLite
would be **correcting a legacy behaviour**, which C-B and AAP §0.2.2.5 forbid outright; it would also mean
inventing a SQLite paging dialect the oracle has never emitted, so the byte-exact parity criterion (§6.2)
would have nothing to compare against. And the two dialects the resolver *does* recognise have no schema,
no connection string and no DDL anywhere in the repository, so provisioning either to execute the output
would be the fabricated storage engine C-E prohibits.

**Mitigation.** The rewriters are verified as **pure string transforms** — input statement plus page size
and index yields output statement — byte for byte, with no instance of either DBMS (§6.2). That is the
whole of what the repository's evidence supports, and it is why the transforms could be characterized at
all. A deployment that genuinely needs paged execution supplies a SQL Server or Oracle connection, at
which point the preserved resolver selects correctly for it without any change here.

### R8 — `MaxRows` is a post-retrieval assertion, not a truncation bound

**What the setting does, in the order it does it.** The row cap is tested **after the retrieval has
completed**: the rows are fetched, the after-retrieve notification fires, the defensive row-count override
is applied, and only then is the cap compared — at which point an exceeded cap answers `E_OUT_OF_RANGE`
carrying the legacy's own diagnostic with the cap interpolated into it
[`n_cst_thread_task_sqlquery.sru:L787-L790`, reproduced in `Tasks/SqlQueryTask.cs` beside those locators].
The boundary condition is preserved too, and it deliberately differs from its neighbour's: `SetMaxRows`
rejects only a **negative** value, so zero is accepted here and means "no limit", while `SetChunkSize`
rejects anything at or below 1000.

**So it bounds what a caller is TOLD, never what the service DOES.** A cap of ten against a
hundred-thousand-row table still fetches a hundred thousand rows, builds the carrier for them and then
refuses the result. It is an assertion about the answer, not a `TOP`, a `LIMIT` or a `FETCH FIRST`, and
reading it as a work bound is the specific misreading this entry exists to prevent.

**Why no code change is right.** Turning it into a truncation bound would change both the generated
statement and the returned row set — a behaviour correction C-B forbids — and it would silently convert a
refusal into a partial answer, which is the more dangerous direction: a caller that asked for a bounded
result and received a truncated one has no way to tell truncation from completeness.

**What bounds result size instead**, stated so the gap is not read as unbounded: the transport ceilings
(`Ingress:MaxReceiveMessageBytes`, `Ingress:MaxSendMessageBytes`, and the streamed-element bound at the
REST projection) and the per-container memory ceilings the orchestration manifest declares. Those are
port-introduced bounds with no legacy counterpart, which is why they may exist at all where a correction
to `MaxRows` may not.

### R9 — Expression calculation over retrieved data is unreachable through the published contract

**The boundary.** No method of C-04 loads rows into an expression session, and C-03's retrieval cannot
deposit its result into one: `OpenExpressionSessionRequest` carries logical DataWindow handles only,
`CalcRequest` / `CalcAllRequest` / `CalcItemRequest` carry no row payload, and `RetrieveRequest` carries no
session identifier. [`CONTRACTS.md` §7.11](CONTRACTS.md#711-the-boundary-this-contract-does-not-cross-calculation-over-retrieved-data)
states it against the definitions.

**What that means at runtime, and both answers are truthful.** Against a session whose host holds no rows,
`Calc` for row 1 answers `E_OUT_OF_RANGE` and `CalcAll` answers success with zero results. Retrieving a
large result first changes neither, because nothing connects the two.

**Why it is declared rather than closed.** A row-loading RPC would be a **new capability**, and the legacy
has no counterpart being withheld — its engine reads a DataWindow control in the caller's own address
space. C-B and AAP §0.2.2.5 forbid adding one.

**What is therefore covered, and what is not.** The engine over populated data — the seven structures, the
five expansion modes, static versus dynamic expansion (§7.3), the macro channel and the trace — is
exercised against in-process hosts, which a test can populate directly. What no exercise of the published
contract can reach is that engine over data **this system fetched**, and this entry is that gap stated
plainly rather than left to be inferred from two truthful diagnostics.

---

## 10. What this document claims and does not claim

**It claims.** That characterization testing is the right technique here and why; that the oracle and its
fixture corpus exist today in this checkout, with counts produced by counting; that `dw_sqlite.srd` is the
only updatable DataWindow in the repository and `w_test_sqlite.srw` carries the only DDL, both tested rather
than assumed; that the shared-volume capture rule of §4.2 is binding and is also the technique's own
prerequisite; that the four determinism seams of §5.1 are the enumerated sources of per-run variation; that
the twelve groups in §7 are legacy behaviours to be reproduced and annotated, each with locators that
resolve; that the coverage gate is measured from `coverage.cobertura.xml` at 80% line coverage per in-scope
service and is enforced by `.github/workflows/ci.yml` in four independent legs, with all four services
measured above the floor; and that all ten test projects build and pass in this repository, with the
counts held in [`BUILD.md`](BUILD.md) §1.3 rather than restated here.

**It does not claim.** That any **paired recording** exists — the store exists and holds none on either
side, which is what leaves the single parity risk of R1 open and is why the four pinyin oracle hooks skip
rather than pass. That the legacy oracle has been executed in this environment — it has not, and it cannot
be here, which is what makes R1 live. That the CI workflow has run on a GitHub-hosted runner —
`.github/workflows/ci.yml` exists and enforces the 80%-per-service floor, and its gate step was extracted
verbatim and exercised locally against all four real reports under four injected faults, but the workflow
itself has not been dispatched. That the pinyin filter can be delivered at bit-exact parity from repository
evidence alone. That cross-session foreign expression variables will be supported. That encrypted SQLite
reaches parity in this phase. And **no performance claim of any kind**, because the repository publishes no
baseline and characterization compares observable outputs only, never execution time (§1.6).

**It reports nothing of its own about the running stack.** The bring-up was exercised and one document says
what that showed and what it did not —
[`orchestration/README.md` §10](../orchestration/README.md#10-what-has-and-has-not-been-exercised).
Nothing here duplicates it, and nothing here contradicts it: a bring-up is not a capture, which is R4.

**One thing it does claim, and it is easy to assume the opposite**: the container path *was* exercised.
All four images build and the four-service stack came up healthy with requests crossing
real boundaries ([`BUILD.md`](BUILD.md) §1.3). **That is not a parity claim** and nothing here should be
read as one — which is precisely why R4 names the oracle gap rather than being closed.

**It also does not claim** that any service has served a request across a network **in a test** — every
service test drives its host in process, so no TLS handshake, ALPN negotiation, real gRPC channel setup or
client-certificate exchange happens inside a suite — nor **that the paging matrix certifies agreement with
the oracle**: its expectations are transcribed from a run of the .NET rewriters, so it detects drift and
nothing more (§6.2).

**It is additive.** This document created one file and changed nothing that already existed. No file under
`ws_objects/**`, and none of the five pre-existing Chinese documents in this folder, was edited, translated,
re-encoded, renamed or link-rewritten — including the two cited here as authoritative specifications (C-C).

**It reproduces no secret value of any kind.** The fixture libraries discussed in §3 hold most of the
in-source secret sites; they are referred to by locator through [`SECRETS.md`](SECRETS.md) only, and no value
appears here or may appear in any recording.

---

## 11. Markdown lint policy for this documentation set

The seven Markdown documents this refactor authors are linted, and the policy is **declared in the files
themselves** rather than described here and hoped for. This document carries the same directive as its six
siblings, at its head, as [`BUILD.md`](BUILD.md) §14 directs.

**The policy.** `MD013` (line length) is set to **120 characters** and is **disabled for tables and for
fenced code blocks**. Every other `markdownlint` rule is left at its default and is satisfied. Prose *is*
wrapped and is held to the 120 limit — the two exemptions are not a licence to stop wrapping. They exist
because neither construct can be wrapped without damage: a table cell has no continuation syntax, so
wrapping an evidence row splits a locator away from the behaviour it proves, and a wrapped command is a
command that does not run.

**Verifying, and why no command is published for it.** The directive at the head of this file *is* the
verification mechanism: a markdownlint-compatible tool that is already provisioned reads it and reports zero
issues, with no flags, no file list and no external configuration file. No command line is published here
because doing so would name a linter that is **not** part of this repository's approved dependency set — the
whole of that set is the exact, locked npm manifest under `tests/e2e` ([`BUILD.md`](BUILD.md) §11.3), and no
Markdown linter is in it. A documented on-demand package-runner invocation would instruct an unpinned
version to be resolved from the network and executed outside that lockfile every time somebody followed
this document, which is exactly the deterministic-automation property [`BUILD.md`](BUILD.md) §9 is built to
protect. Lint with tooling that is already installed.

**No file list and no glob is needed, which is the stronger property.** A repository-root configuration file
or a glob such as `docs/*.md` would also reach the five read-only legacy Chinese documents, which carry
pre-existing violations of their own — unlabelled code fences, hard tabs and others. Those are **out of
scope and are deliberately not addressed**, because the files are read-only (C-C), so a sweep that reported
them would report a failure that must not be acted on, which is worse than no check at all. One of those
files is not even UTF-8 encoded, so tooling that assumes UTF-8 across `docs/` will fault on it. A per-file
directive cannot reach any of them, and that is why the policy is declared this way.
