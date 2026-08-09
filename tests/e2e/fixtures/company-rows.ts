/**
 * The legacy `COMPANY` schema, its oracle rows, and deterministic row builders.
 *
 * WHY THIS MODULE EXISTS
 * ----------------------
 * This is the suite's ONLY description of the `COMPANY` schema and the ONLY
 * place `COMPANY` test rows are constructed. Both halves of that sentence are
 * deliberate. `services/` had no declared children when this fixture was
 * specified, so the exact JSON field naming of Gateway's `/v1/datawindow/**`
 * projection could not be read from the source tree. Concentrating the schema
 * facts and the row construction here — rather than inlining literals in each
 * spec — is what makes a serialization surprise cost one edit instead of a
 * fixture redesign. Specs own the request and response ENVELOPE; this module
 * owns ROW DATA and SCHEMA FACTS, and the boundary between those two is the
 * whole point.
 *
 * It is pure data plus pure functions: no database access, no DDL execution,
 * no service call, no clock read, no random number, no secret. It imports
 * nothing, and it must stay that way.
 *
 * THE TWO AUTHORITATIVE LOCATORS
 * ------------------------------
 * 1. `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469` — the DDL.
 *    This is THE ONLY DDL IN THE ENTIRE REPOSITORY. Every column type,
 *    nullability and the identity declaration are transcribed from it.
 * 2. `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` — the DataWindow. This is
 *    THE ONLY UPDATABLE DATAWINDOW IN THE REPOSITORY, which makes it the
 *    golden-master fixture for the entire retrieval / validation / update
 *    triple. Its six columns are at `:L8-L13`, its table specification at
 *    `:L14`, the birth edit mask at `:L26` and a page-scoped aggregate at
 *    `:L27`.
 *
 * The legacy tree is the behavioural oracle and it is READ-ONLY (C-C). Values
 * are transcribed into TypeScript literals with their locator recorded beside
 * each one; no legacy file is ever loaded, read, imported or opened at run
 * time. The three sibling legacy browser-asset directories under `tests/` are
 * read-only oracle assets that this module never touches or names.
 *
 * THREE PRESERVED TYPE MISMATCHES — DEFECTS TO REPRODUCE, NEVER CORRECT
 * ---------------------------------------------------------------------
 * The DataWindow and the DDL disagree in three places, and the disagreements
 * are preserved rather than reconciled (C-B):
 *
 * | Column    | DataWindow declares | The table declares |
 * | --------- | ------------------- | ------------------ |
 * | `address` | `char(200)`         | `CHAR(50)`         |
 * | `salary`  | `decimal(2)`        | `REAL`             |
 * | `birth`   | `date`              | `TEXT`             |
 *
 * Each is recorded on its column below as a `mismatchNote`. An implementation
 * that declared consistent types on both sides would diverge from the oracle
 * on the first value that exercised the difference.
 *
 * NO CODE PATH HERE ASSERTS A LENGTH LIMIT OF 50 OR 200, and none may be
 * added. Two independent reasons. First, the mismatch must never become the
 * accidental subject of a happy-path spec, so every address this module
 * produces or transcribes is 50 characters or fewer and the disagreement is
 * simply never reached. Second, and more decisively: SQLite gives a declared
 * type containing `CHAR` TEXT affinity, so the declared `CHAR(50)` length
 * CONSTRAINS NOTHING — the engine neither truncates at 50 nor pads to 50.
 * A truncation assertion would therefore assert behaviour the engine does not
 * exhibit. The disagreement is between two DECLARATIONS, and only the
 * DataWindow's bound has any effect.
 *
 * DETERMINISM IS A HARD PREREQUISITE, NOT A PREFERENCE
 * ----------------------------------------------------
 * Characterization testing compares a recorded master against a candidate, and
 * its one hard requirement is repeatability with every non-deterministic value
 * masked from BOTH sides (AAP §0.6.7). Reinforcing that, the paired-capture
 * rule forbids recreating or reseeding the `persistence-db` volume between the
 * legacy-side and target-side captures of one workflow ID — so a fixture that
 * varied its own output per run would void the pair.
 *
 * Therefore, and without exception in this module: no current-time read, no
 * zero-argument date construction, no pseudo-random number generation, no UUID
 * generation, no counter seeded from a clock, no locale-sensitive formatter,
 * and no timestamp or GUID in any assertion-visible field. Uniqueness comes
 * from a FIXED SEED or a STABLE TEST-SCOPED LABEL — see
 * {@link deterministicLabel} — never from a clock or a random source.
 *
 * The prohibitions above are stated as CONCEPTS rather than as the identifiers
 * that implement them, deliberately: a reviewer auditing this file greps for
 * those identifiers, and a comment quoting one would answer the audit with a
 * false positive. There are none to find.
 *
 * The legacy itself does the opposite: it writes `birth` from
 * `String(ToDay(),"YYYY-MM-DD")` at `w_test_sqlite.srw:L280`, which is a clock
 * read. Every clock read is an enumerated determinism seam, so this fixture
 * uses fixed dates instead. That is TEST-DATA determinism; it is not a
 * behavioural change in the system under test, and it corrects nothing in the
 * legacy.
 *
 * WHAT THIS MODULE NEVER DOES
 * ---------------------------
 * It never creates a table, runs the DDL, seeds a database, connects to
 * SQLite, or starts or stops anything (C-J). Persistence owns all SQL and is
 * the only service holding a storage provider; the single local bring-up path
 * is the orchestration manifest. The DDL is quoted in a comment below and is
 * deliberately NOT exported as an executable string, so nobody can be tempted
 * to run it.
 *
 * It carries no secret of any kind (C-F) — no key, no certificate, no
 * credential of any form, no token, not even as an example. Every name, address
 * and label is obviously innocuous test data.
 *
 * It names no deferred service as a live target (C-D).
 *
 * It has ZERO module-scope side effects (C-L): importing it performs no
 * asynchronous wait, no network call, no error throw and no process
 * termination. Every module-scope initializer below is either a literal or a
 * freeze of one. The documented collection command
 * `npx playwright test --list` loads it with no stack running, and
 * `tsc --noEmit` type-checks it; both must pass on a clean checkout.
 *
 * Validation errors ARE thrown, but only from inside a function and therefore
 * only when a caller supplies something unusable — which is a call-time
 * behaviour, not an import-time one.
 */

/**
 * The one table this suite touches.
 *
 * Transcribed from the DDL at `w_test_sqlite.srw:L463`, and independently
 * confirmed by the DataWindow's own table specification at `dw_sqlite.srd:L14`
 * (`update="COMPANY"`) and by its retrieve statement (`SELECT * FROM COMPANY`).
 *
 * Upper case is the oracle's spelling and is preserved. SQLite identifiers are
 * case-insensitive, so this is a transcription fidelity choice rather than a
 * functional one — but the column names below are lower case in the oracle and
 * are likewise preserved as they appear, so the mixed casing across this module
 * is faithful rather than inconsistent.
 */
export const COMPANY_TABLE_NAME = 'COMPANY';

/*
 * THE DDL, QUOTED FOR REFERENCE ONLY — NEVER EXECUTED, NEVER EXPORTED.
 *
 * `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469`, the only DDL in
 * the repository. Reproduced here so a reader can check every literal in this
 * file against the oracle without opening the legacy tree:
 *
 *     CREATE TABLE IF NOT EXISTS COMPANY(
 *       ID INTEGER PRIMARY KEY NOT NULL,      -- :L464, commented 自增列
 *       NAME           TEXT    NOT NULL,      -- :L465
 *       AGE            INT     NOT NULL,      -- :L466
 *       ADDRESS        CHAR(50),               -- :L467
 *       SALARY         REAL,                   -- :L468
 *       BIRTH          TEXT)                   -- :L469
 *
 * The `自增列` comment on `:L464` is the oracle's own annotation that `ID` is
 * the auto-increment column. `INTEGER PRIMARY KEY` is SQLite's rowid alias, so
 * the value is assigned by the engine on insert — which is why every INSERT in
 * the oracle omits it, and why {@link CompanyRowInput} has no `id`.
 *
 * The surrounding lines are the connection grammar, and they belong to
 * Persistence rather than to this fixture: a file delete precedes the open
 * [`:L450`], the URI form is `test.db?mode=rwc` followed by an optional
 * credential parameter that the oracle shows only as a commented placeholder
 * [`:L456`], two documented URI extensions exist [`:L453-L455`], and
 * auto-commit is enabled for the DDL then disabled afterwards [`:L461`,
 * `:L473`]. The repository carries no value for that optional parameter, and
 * none is reproduced here.
 */

/**
 * One column of the `COMPANY` table, described from BOTH oracles at once.
 *
 * Carrying the DDL type and the DataWindow type as separate members is what
 * makes the three preserved mismatches visible in the data rather than hidden
 * in prose. A single `type` member would have forced a choice between the two
 * oracles, and choosing either one would have silently reconciled a
 * disagreement this suite exists to preserve.
 */
export interface CompanyColumn {
  /** The DataWindow column name, as spelled at `dw_sqlite.srd:L8-L13`. */
  readonly name: CompanyColumnName;
  /**
   * The database column name, from the `dbname=` attribute at
   * `dw_sqlite.srd:L8-L13`.
   *
   * It equals {@link CompanyColumn.name} for all six columns. The member is
   * kept distinct anyway because the two are distinct concepts in the legacy —
   * the identity-column resolver matches a lower-cased update-table prefix
   * against each column's DBName, so a qualified DBName is a shape the model
   * has to be able to express even though this fixture's six are unqualified.
   */
  readonly dbName: string;
  /** The type as the DDL declares it (`w_test_sqlite.srw:L463-L469`). */
  readonly ddlType: string;
  /** The type as the DataWindow declares it (`dw_sqlite.srd:L8-L13`). */
  readonly dwType: string;
  /** Whether the DDL permits NULL — i.e. whether it omits `NOT NULL`. */
  readonly nullable: boolean;
  /** `key=yes` at `dw_sqlite.srd:L8`. True for `id` alone. */
  readonly isKey: boolean;
  /** `identity=yes` at `dw_sqlite.srd:L8`. True for `id` alone. */
  readonly isIdentity: boolean;
  /** `update=yes`. True for all six. */
  readonly updatable: boolean;
  /** `updatewhereclause=yes`. True for all six — see {@link MARKED_COLUMNS}. */
  readonly updateWhereClause: boolean;
  /**
   * Present only on the three columns whose two oracles disagree.
   *
   * The property is OMITTED on the other three rather than set to `undefined`,
   * which `exactOptionalPropertyTypes` requires and which also reads correctly:
   * those columns have no mismatch, rather than an unknown one.
   */
  readonly mismatchNote?: string;
}

/**
 * The six column names, in DDL order.
 *
 * Derived from {@link CompanyRow} with `keyof` rather than written out twice,
 * so a column can never be added to the row shape without appearing here.
 */
export type CompanyColumnName = keyof CompanyRow;

/**
 * The `COMPANY` schema, in DDL order, from both oracles.
 *
 * Ordering is the DDL's and the DataWindow's — they agree — and it is the order
 * `SELECT *` returns. Frozen at both levels: the array cannot be reordered and
 * no entry can be edited, because this table is shared by every spec that
 * touches the schema.
 */
export const COMPANY_COLUMNS: readonly CompanyColumn[] = Object.freeze([
  // `ID INTEGER PRIMARY KEY NOT NULL` [`:L464`] / `key=yes identity=yes` [`dw_sqlite.srd:L8`].
  // The engine assigns this value on insert and it round-trips back to the
  // caller in the update response's identity arrays; no builder supplies it.
  Object.freeze({
    name: 'id',
    dbName: 'id',
    ddlType: 'INTEGER PRIMARY KEY NOT NULL',
    dwType: 'number',
    nullable: false,
    isKey: true,
    isIdentity: true,
    updatable: true,
    updateWhereClause: true,
  }),
  // `NAME TEXT NOT NULL` [`:L465`] / `char(100)` [`dw_sqlite.srd:L9`].
  // The DataWindow bounds a column the DDL leaves unbounded. That is a
  // difference in the same direction as the storage engine's own permissiveness
  // rather than a contradiction, so it is not one of the three mismatches.
  Object.freeze({
    name: 'name',
    dbName: 'name',
    ddlType: 'TEXT NOT NULL',
    dwType: 'char(100)',
    nullable: false,
    isKey: false,
    isIdentity: false,
    updatable: true,
    updateWhereClause: true,
  }),
  // `AGE INT NOT NULL` [`:L466`] / `number` [`dw_sqlite.srd:L10`].
  Object.freeze({
    name: 'age',
    dbName: 'age',
    ddlType: 'INT NOT NULL',
    dwType: 'number',
    nullable: false,
    isKey: false,
    isIdentity: false,
    updatable: true,
    updateWhereClause: true,
  }),
  // `ADDRESS CHAR(50)` [`:L467`] / `char(200)` [`dw_sqlite.srd:L11`]. MISMATCH 1.
  Object.freeze({
    name: 'address',
    dbName: 'address',
    ddlType: 'CHAR(50)',
    dwType: 'char(200)',
    nullable: true,
    isKey: false,
    isIdentity: false,
    updatable: true,
    updateWhereClause: true,
    mismatchNote:
      'PRESERVED DEFECT: the DataWindow declares char(200) while the DDL ' +
      'declares CHAR(50), so the DataWindow admits input the column does not ' +
      'declare room for. Reproduce, never reconcile. Note the declared ' +
      'CHAR(50) constrains nothing in SQLite, which gives any declared type ' +
      'containing CHAR a TEXT affinity: the engine neither truncates at 50 nor ' +
      'pads to 50. Assert no length limit on either number.',
  }),
  // `SALARY REAL` [`:L468`] / `decimal(2)` [`dw_sqlite.srd:L12`]. MISMATCH 2.
  Object.freeze({
    name: 'salary',
    dbName: 'salary',
    ddlType: 'REAL',
    dwType: 'decimal(2)',
    nullable: true,
    isKey: false,
    isIdentity: false,
    updatable: true,
    updateWhereClause: true,
    mismatchNote:
      'PRESERVED DEFECT: the DataWindow declares a fixed two-place decimal ' +
      'over a floating-point column, so a round-trip is not guaranteed to ' +
      'return an identical value and the observed result is the ' +
      'specification. This is why SALARY_COMPARISON_TOLERANCE exists.',
  }),
  // `BIRTH TEXT` [`:L469`] / `date` [`dw_sqlite.srd:L13`]. MISMATCH 3.
  Object.freeze({
    name: 'birth',
    dbName: 'birth',
    ddlType: 'TEXT',
    dwType: 'date',
    nullable: true,
    isKey: false,
    isIdentity: false,
    updatable: true,
    updateWhereClause: true,
    mismatchNote:
      'PRESERVED DEFECT: the DataWindow declares a date over a text column, ' +
      'so ordering, comparison and format all follow from the text actually ' +
      'written rather than from a date type. This is why birth is modelled as ' +
      'a string here and never as a Date.',
  }),
] as const);

/**
 * Every column that participates in the optimistic-concurrency check.
 *
 * All six, because all six carry `updatewhereclause=yes` [`dw_sqlite.srd:L8-L13`]
 * and the table specification carries `updatewhere=1` [`:L14`].
 *
 * `updatewhere=1` is the "key and updateable columns" concurrency mode: the
 * generated `UPDATE` statement's `WHERE` clause carries the key column PLUS the
 * ORIGINAL VALUE of every updateable column. With all six marked, the check
 * therefore spans all six columns' original values (AAP §0.6.3.2).
 *
 * That single fact is the reason {@link CompanyRowUpdate} carries two rows
 * rather than one: THE UPDATE PAYLOAD MUST TRANSMIT, PER ROW, BOTH THE CURRENT
 * AND THE ORIGINAL VALUE OF EVERY MARKED COLUMN. A flat rowset cannot express
 * that, which is precisely what the Persistence buffer anti-corruption layer
 * exists to carry.
 */
export const MARKED_COLUMNS: readonly CompanyColumnName[] = Object.freeze([
  'id',
  'name',
  'age',
  'address',
  'salary',
  'birth',
] as const);

/**
 * A `COMPANY` row as retrieved — that is, including the engine-assigned key.
 *
 * `address`, `salary` and `birth` are nullable because the DDL omits `NOT NULL`
 * on all three [`w_test_sqlite.srw:L467-L469`]. `null` is modelled explicitly
 * rather than as an absent property: null and empty are distinguishable values
 * in the concurrency comparison, so a row that collapsed one into the other
 * would compare differently from the oracle.
 *
 * `birth` is a STRING, never a `Date`. The column is `TEXT` [`:L469`] — that is
 * preserved mismatch 3 — so its ordering and comparison semantics follow from
 * the text actually written. Modelling it as a `Date` would impose date
 * semantics the storage does not have, and would drag host-timezone behaviour
 * into a value that must be byte-stable. See {@link BIRTH_FORMAT}.
 */
export interface CompanyRow {
  /**
   * The key and identity column [`dw_sqlite.srd:L8`].
   *
   * Assigned by the engine on insert — `INTEGER PRIMARY KEY` is SQLite's rowid
   * alias — and returned to the caller afterwards. Present on a retrieved row;
   * never supplied on an inserted one. See {@link CompanyRowInput}.
   */
  readonly id: number;
  /** `NAME TEXT NOT NULL` [`:L465`]. Required. */
  readonly name: string;
  /** `AGE INT NOT NULL` [`:L466`]. Required. */
  readonly age: number;
  /** `ADDRESS CHAR(50)` [`:L467`]. Nullable. Preserved mismatch 1. */
  readonly address: string | null;
  /** `SALARY REAL` [`:L468`]. Nullable. Preserved mismatch 2. */
  readonly salary: number | null;
  /** `BIRTH TEXT` [`:L469`], in {@link BIRTH_FORMAT}. Nullable. Mismatch 3. */
  readonly birth: string | null;
}

/**
 * The INSERT shape: a `COMPANY` row WITHOUT its key.
 *
 * `ID` IS THE AUTO-INCREMENT IDENTITY COLUMN, SO A BUILDER MUST NEVER SUPPLY
 * IT. This is not a stylistic preference, it is what the oracle does, twice
 * over and by two different mechanisms:
 *
 * - The batch insert at `w_test_sqlite.srw:L381-L388` issues four statements,
 *   every one of them with the column list `(NAME,AGE,ADDRESS,SALARY,BIRTH)`.
 *   `ID` is absent from all four.
 * - The dynamic path at `:L396-L400` uses the same five-column list with
 *   positional `?` binding behind an `@` statement-cache prefix. `ID` is absent
 *   there too, which confirms the omission independently of the batch form.
 *
 * The identity value ROUND-TRIPS BACK from Persistence in the update response's
 * identity arrays, so a caller learns the key it was assigned rather than
 * choosing one. The legacy collects those values from the Primary buffer
 * forwards and from the Filter buffer BACKWARDS, because the filter buffer's
 * row order is inverted relative to the source
 * [`n_cst_thread_task_sqlupdate.sru:L235-L241`]. That inversion is a documented
 * legacy behaviour and not a defect to correct; this fixture does not implement
 * the round-trip, it only refrains from pre-empting it.
 *
 * `NAME` and `AGE` are `NOT NULL`, so every happy-path row supplies both.
 * `ADDRESS`, `SALARY` and `BIRTH` are nullable, and A NULL-VALUED ROW IS
 * LEGITIMATE FIXTURE DATA rather than an edge case to avoid — the concurrency
 * comparison has to carry nulls correctly, so specs need to be able to make
 * them.
 */
export type CompanyRowInput = Omit<CompanyRow, 'id'>;

/**
 * The wire format of `birth`: a four-digit year, a two-digit month and a
 * two-digit day, dash-separated.
 *
 * Two independent sources agree on it. The DataWindow's edit mask at
 * `dw_sqlite.srd:L26` is `editmask.mask="yyyy-mm-dd"`, and all four oracle
 * literals are written that way — `'1991-05-11'`, `'1980-05-11'`,
 * `'1995-10-11'`, `'1988-07-28'` [`w_test_sqlite.srw:L382-L388`]. The legacy's
 * programmatic path formats the same layout with the upper-case PowerScript
 * token `"YYYY-MM-DD"` [`:L280`]; the case differs because it is a different
 * formatting language, not because the layout differs.
 *
 * The constant is the FORMAT DESCRIPTION, not a formatter pattern to feed to a
 * library. Use {@link formatBirth} to produce a value.
 */
export const BIRTH_FORMAT = 'yyyy-mm-dd';

/**
 * The four rows the oracle inserts, transcribed verbatim.
 *
 * Source: the batch insert at `w_test_sqlite.srw:L381-L388`, in the order the
 * statements appear. Using the legacy's own fixture data rather than inventing
 * values makes every seed value traceable to the behavioural oracle, which is
 * the posture characterization testing requires — the DataWindow corpus IS the
 * test corpus, and fixtures need no invention (AAP §0.6.7).
 *
 * The order here is INSERT order, which is NOT retrieve order. The DataWindow
 * sorts [`dw_sqlite.srd:L14`]; see {@link EXPECTED_SEED_RETRIEVE_ORDER}.
 *
 * TWO THINGS THAT MUST NOT BE "TIDIED"
 * ------------------------------------
 * 1. `'Rich-Mond '` ENDS WITH A SPACE. That trailing space is in the oracle at
 *    `:L388` and is reproduced byte-for-byte. Do not trim it, do not normalise
 *    it, and do not "fix" it — it is exactly the kind of value whose handling a
 *    parity recording is meant to pin down, and the storage column has TEXT
 *    affinity so nothing in the engine will trim it either.
 * 2. `15000.88`, `20000.32` and `65000.16` are NOT exactly representable as
 *    IEEE-754 doubles. They are transcribed unrounded anyway, because rounding
 *    them to representable neighbours would be a corrected fixture and a
 *    straightforward C-B violation. Compare them within
 *    {@link SALARY_COMPARISON_TOLERANCE}; never with strict equality. Rows the
 *    suite writes and then asserts on strictly should come from
 *    {@link buildCompanyRow} or {@link buildCompanyRows}, whose salaries are
 *    exactly representable by construction.
 *
 * Every address here is 50 characters or fewer — the longest, `'California'`,
 * is ten — so the `CHAR(50)` / `char(200)` disagreement is never reached by a
 * happy-path spec using these rows.
 */
export const LEGACY_SEED_ROWS: readonly CompanyRowInput[] = Object.freeze([
  // `VALUES ('Paul', 32, 'California', 20000.00, '1991-05-11')` [`:L382`].
  // 20000.00 is exactly representable; the other three salaries are not.
  Object.freeze({
    name: 'Paul',
    age: 32,
    address: 'California',
    salary: 20000.0,
    birth: '1991-05-11',
  }),
  // `VALUES ('Allen', 25, 'Texas', 15000.88, '1980-05-11')` [`:L384`].
  Object.freeze({
    name: 'Allen',
    age: 25,
    address: 'Texas',
    salary: 15000.88,
    birth: '1980-05-11',
  }),
  // `VALUES ('Teddy', 23, 'Norway', 20000.32, '1995-10-11')` [`:L386`].
  Object.freeze({
    name: 'Teddy',
    age: 23,
    address: 'Norway',
    salary: 20000.32,
    birth: '1995-10-11',
  }),
  // `VALUES ('Mark', 25, 'Rich-Mond ', 65000.16, '1988-07-28')` [`:L388`].
  // THE TRAILING SPACE IN THE ADDRESS IS INTENTIONAL AND IS IN THE ORACLE.
  Object.freeze({
    name: 'Mark',
    age: 25,
    address: 'Rich-Mond ',
    salary: 65000.16,
    birth: '1988-07-28',
  }),
] as const);

/**
 * The names of {@link LEGACY_SEED_ROWS} in the order the DataWindow returns
 * them.
 *
 * Derived from `sort="age A salary A "` at `dw_sqlite.srd:L14` — age ascending,
 * then salary ascending as the tie-break:
 *
 * | # | Name    | Age | Salary   | Why it lands here                        |
 * | -: | ------- | --: | -------: | ---------------------------------------- |
 * | 1 | `Teddy` |  23 | 20000.32 | Youngest, so no tie-break needed         |
 * | 2 | `Allen` |  25 | 15000.88 | Ties with Mark on 25; lower salary first |
 * | 3 | `Mark`  |  25 | 65000.16 | Ties with Allen on 25; higher salary     |
 * | 4 | `Paul`  |  32 | 20000.00 | Oldest, so last regardless of salary     |
 *
 * PROVIDING THIS AS DATA IS THE POINT OF THE FIXTURE. No spec should re-derive
 * the sort: a spec that recomputed it would be testing its own copy of the
 * ordering rule instead of the service's, and the `Allen`/`Mark` tie-break is
 * exactly where an independent re-derivation would plausibly go wrong.
 *
 * Names are used as the identity here because they are unique across the four
 * rows and, unlike `id`, are known before the insert — the key is assigned by
 * the engine, so it cannot appear in an expectation written ahead of time.
 */
export const EXPECTED_SEED_RETRIEVE_ORDER: readonly string[] = Object.freeze([
  'Teddy',
  'Allen',
  'Mark',
  'Paul',
] as const);

/**
 * The absolute tolerance for any assertion that compares a `salary`.
 *
 * Half a cent. Comfortably below the two decimal places the DataWindow declares
 * [`dw_sqlite.srd:L12`], so it cannot mask a genuine discrepancy at the
 * precision the schema actually claims, and far above the ~1e-12 error that
 * storing a two-place decimal in a `REAL` column introduces.
 *
 * WHY A TOLERANCE IS NECESSARY RATHER THAN TIDY. `SALARY` is `REAL`
 * [`w_test_sqlite.srw:L468`] while the DataWindow declares `decimal(2)` — that
 * is preserved mismatch 2 — so a value's round trip through storage is not
 * guaranteed to return an identical double. Three of the four oracle salaries
 * are not exactly representable to begin with, so even before storage they are
 * held as the nearest double rather than the decimal written in the source.
 *
 * Use it for anything touching {@link LEGACY_SEED_ROWS}. Rows built by
 * {@link buildCompanyRow} and {@link buildCompanyRows} use exactly
 * representable salaries specifically so that a strict equality assertion on
 * suite-written data remains available and cannot be defeated by rounding.
 */
export const SALARY_COMPARISON_TOLERANCE = 0.005;


/* ------------------------------------------------------------------------- *
 * Deterministic construction
 *
 * Everything below produces values from its arguments and from module
 * constants, and from nothing else. There is no clock read, no random source
 * and no environment read anywhere in this section, which is what lets a
 * recording taken from it be compared against a master (AAP §0.6.7).
 * ------------------------------------------------------------------------- */

/** The fixed prefix every generated label carries, so suite data is greppable. */
const LABEL_PREFIX = 'pfw-e2e';

/** Digits the index is padded to, so labels sort lexically as they sort numerically. */
const LABEL_INDEX_DIGITS = 4;

/** Longest sanitized scope a label keeps, so a label stays short enough to use anywhere. */
const LABEL_MAX_SCOPE_LENGTH = 24;

/** Substituted when a caller's scope sanitizes away to nothing. */
const LABEL_FALLBACK_SCOPE = 'scope';

/** Default scope for {@link buildCompanyRow}, whose row has no sequence position. */
const DEFAULT_BUILD_SCOPE = 'fixture';

/**
 * First age in a generated sequence.
 *
 * Chosen ABOVE the oldest oracle row (`Paul`, 32) on purpose. `sort="age A
 * salary A "` therefore places every generated row after all four
 * {@link LEGACY_SEED_ROWS}, so a spec that retrieves a table holding both finds
 * its generated block contiguous and last instead of interleaved.
 */
const BASE_AGE = 40;

/** First salary in a generated sequence. Exactly representable. */
const BASE_SALARY = 20000;

/**
 * Salary increment per index.
 *
 * A quarter is a negative power of two, so every value `BASE_SALARY + n * 0.25`
 * is exactly representable as a double and survives a strict equality
 * assertion. A step of `0.1` would not, and would quietly reintroduce the very
 * float problem {@link SALARY_COMPARISON_TOLERANCE} exists to absorb for the
 * oracle rows.
 */
const SALARY_STEP = 0.25;

/** `address` for {@link buildCompanyRow}. Ten characters; from `w_test_sqlite.srw:L382`. */
const DEFAULT_ADDRESS = 'California';

/** `birth` for {@link buildCompanyRow}. A fixed literal, never a computed today. */
const DEFAULT_BIRTH = '1990-01-01';

/**
 * Addresses a generated sequence cycles through, all traceable to the oracle.
 *
 * `'Rich-Mond '` IS DELIBERATELY ABSENT. Its trailing space belongs to
 * {@link LEGACY_SEED_ROWS}, where it is the oracle's own value and must survive
 * byte-for-byte. Reusing it here would sprinkle a trailing space through data
 * the suite writes itself, so a spec asserting on generated output could end up
 * accidentally testing whitespace preservation instead of whatever it meant to
 * test. A spec that wants to exercise the trailing space uses the seed rows,
 * where the intent is explicit.
 *
 * `'shenzheng'` is the oracle's own spelling at `:L278` and is transcribed
 * rather than corrected, consistent with how every other legacy value here is
 * treated. The longest entry is ten characters, so no generated address
 * approaches the width either oracle declares.
 */
const CYCLE_ADDRESSES: readonly string[] = Object.freeze([
  'California', // `:L382`
  'Texas', // `:L384`
  'Norway', // `:L386`
  'beijing', // `:L271`
  'shenzheng', // `:L278`
] as const);

/** The five insert-shape columns, for iterating a partial without `keyof` gymnastics. */
const COMPANY_INPUT_COLUMNS: readonly (keyof CompanyRowInput)[] = Object.freeze([
  'name',
  'age',
  'address',
  'salary',
  'birth',
] as const);

/**
 * The stale value {@link asStaleUpdate} writes when a caller supplies none.
 *
 * A non-key column, and unmistakably not a value any row could hold — the
 * intent of a conflict spec should be legible from its payload without a
 * comment. It is a plain sentinel string and carries nothing sensitive.
 */
export const STALE_ORIGINAL_SENTINEL = '__pfw_stale_original__';

/** Zero-pads a non-negative integer to at least `width` digits. */
function padNumber(value: number, width: number): string {
  return String(value).padStart(width, '0');
}

/**
 * Rejects a value that is not a finite integer, naming the parameter at fault.
 *
 * Fixture misuse is a programmer error, and a fixture that absorbed it would
 * hand a corrupt row to a spec and turn a mistake here into a confusing failure
 * somewhere else. Failing loudly at the call site is the same fail-fast posture
 * the framework being migrated takes toward a structural fault.
 *
 * This throws from inside a function, which is a call-time behaviour. Module
 * scope stays free of any `throw` so that importing this file cannot fail (C-L).
 */
function requireInteger(parameterName: string, value: number, minimum: number, maximum: number): void {
  if (!Number.isInteger(value)) {
    throw new TypeError(`${parameterName} must be an integer; received ${String(value)}.`);
  }
  if (value < minimum || value > maximum) {
    throw new RangeError(
      `${parameterName} must be between ${String(minimum)} and ${String(maximum)}; received ${String(value)}.`,
    );
  }
}

/**
 * Formats a `birth` value in {@link BIRTH_FORMAT} from its three parts.
 *
 * Pure zero-padding over integer arithmetic. NO DATE OBJECT IS CONSTRUCTED AND
 * NO FORMATTER IS CONSULTED, deliberately: a local-time constructor or any
 * locale-sensitive formatter would make the result depend on the host timezone
 * or locale, which is a hidden non-determinism that surfaces as a flake on one
 * machine only. This function returns the same string on every host, under every
 * `TZ`, in every locale — which is verified rather than assumed.
 *
 * WHAT IS VALIDATED, AND WHAT IS DELIBERATELY NOT. The parts are checked for
 * integrality and for the ranges that keep the four-two-two shape meaningful,
 * so an obvious mistake fails at the call site. CALENDAR VALIDITY IS NOT
 * CHECKED — `formatBirth(2001, 2, 30)` returns `'2001-02-30'` rather than
 * throwing. That is not an oversight. `BIRTH` is a `TEXT` column
 * [`w_test_sqlite.srw:L469`] — preserved mismatch 3 — so the storage accepts
 * any text and applies no date semantics of its own. A fixture that rejected a
 * date the column would happily store would be enforcing a rule the system
 * under test does not have, which is precisely the kind of well-meant
 * correction C-B forbids. Specs that want to probe how the stack handles an
 * impossible date need to be able to construct one.
 *
 * @param year Four-digit year, 1 through 9999.
 * @param month Month, 1 through 12.
 * @param day Day of month, 1 through 31, not checked against the month.
 * @returns The value in `yyyy-mm-dd` form.
 * @throws TypeError If any part is not an integer.
 * @throws RangeError If any part is outside its range.
 */
export function formatBirth(year: number, month: number, day: number): string {
  requireInteger('year', year, 1, 9999);
  requireInteger('month', month, 1, 12);
  requireInteger('day', day, 1, 31);

  return `${padNumber(year, 4)}-${padNumber(month, 2)}-${padNumber(day, 2)}`;
}

/**
 * The sanctioned way to obtain a distinguishable value.
 *
 * UNIQUENESS COMES FROM A FIXED SEED OR A STABLE TEST-SCOPED LABEL, NEVER FROM
 * A CLOCK OR AN RNG. A timestamp or a GUID would make every run's data differ,
 * and a recording containing one is unmatchable against a master unless the
 * value is masked on both sides — so the cheaper and more honest answer is not
 * to generate one. The caller supplies a scope that is stable for its test, the
 * index distinguishes rows within it, and the result is reproducible on every
 * run and every host.
 *
 * The scope is normalised: lower-cased, every run of characters outside
 * `[a-z0-9]` collapsed to a single dash, leading and trailing dashes removed,
 * and the result truncated to {@link LABEL_MAX_SCOPE_LENGTH}. Normalisation is
 * a pure string transform, so it does not weaken determinism. TWO CONSEQUENCES
 * A CALLER SHOULD KNOW: scopes differing only in punctuation or case collapse
 * to the same label, and two scopes sharing their first
 * {@link LABEL_MAX_SCOPE_LENGTH} characters after normalisation also collide.
 * Keep scopes short and distinct. A scope that normalises to nothing at all
 * becomes `'scope'` rather than producing a malformed label.
 *
 * For a scope within the length bound and an index below `10 ** 4`, the label is
 * at most 37 characters. That keeps it comfortably inside the narrower of the
 * two widths either oracle declares for `address`, so a label is safe to use as
 * an address without a happy-path spec ever reaching the preserved width
 * disagreement.
 *
 * @param scope A stable, test-scoped discriminator.
 * @param index Position within that scope; zero-based, and zero is valid.
 * @returns A label of the form `pfw-e2e-<scope>-<index>`.
 * @throws TypeError If `scope` is not a string, or `index` is not an integer.
 * @throws RangeError If `index` is negative.
 */
export function deterministicLabel(scope: string, index: number): string {
  if (typeof scope !== 'string') {
    throw new TypeError(`scope must be a string; received ${typeof scope}.`);
  }
  requireInteger('index', index, 0, Number.MAX_SAFE_INTEGER);

  const normalized = scope
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, LABEL_MAX_SCOPE_LENGTH)
    .replace(/-+$/g, '');

  const effectiveScope = normalized.length > 0 ? normalized : LABEL_FALLBACK_SCOPE;

  return `${LABEL_PREFIX}-${effectiveScope}-${padNumber(index, LABEL_INDEX_DIGITS)}`;
}

/**
 * Builds one insert-shaped row from deterministic defaults.
 *
 * Every default is a module constant or derived from one, so two calls with
 * equal arguments return equal rows — in one process, across processes, and
 * under any `TZ`. `salary` defaults to {@link BASE_SALARY}, which is exactly
 * representable, so a spec may assert on it with strict equality.
 *
 * THE RESULT NEVER CARRIES AN `id`. The shape is assembled from the five known
 * insert columns rather than by spreading whatever the caller passed, so a stray
 * property cannot reach the payload. An `id` is treated more harshly than that
 * and REJECTED OUTRIGHT rather than dropped, because supplying one contradicts
 * the identity column being engine-assigned, and a silent drop would leave a
 * caller believing it had pinned a key it had not.
 *
 * @param overrides Fields to replace. Any subset, including `null` for the
 *   three nullable columns — a null-valued row is legitimate fixture data.
 * @returns A frozen insert-shaped row.
 * @throws TypeError If `overrides` is not an object, or carries an `id`.
 */
export function buildCompanyRow(overrides?: Partial<CompanyRowInput>): CompanyRowInput {
  if (overrides !== undefined) {
    requireInputPatch('overrides', overrides);
  }

  const merged = {
    name: deterministicLabel(DEFAULT_BUILD_SCOPE, 0),
    age: BASE_AGE,
    address: DEFAULT_ADDRESS,
    salary: BASE_SALARY,
    birth: DEFAULT_BIRTH,
    ...overrides,
  };

  return Object.freeze({
    name: merged.name,
    age: merged.age,
    address: merged.address,
    salary: merged.salary,
    birth: merged.birth,
  });
}

/**
 * Builds a deterministic sequence of insert-shaped rows.
 *
 * Identical inputs produce identical output on every run and every host: each
 * row is a pure function of its index and the module constants, with no clock,
 * no random source and no environment read involved.
 *
 * THE SEQUENCE SORTS STRICTLY, WITH NO TIES. `age` is `BASE_AGE + index` and
 * `salary` is `BASE_SALARY + index * SALARY_STEP`, so BOTH increase strictly
 * with the index. Under the DataWindow's `sort="age A salary A "`
 * [`dw_sqlite.srd:L14`] the retrieve order is therefore exactly the generated
 * order, and it stays exactly the generated order even if the tie-break column
 * were ignored — the ordering assertion cannot pass by accident, and it cannot
 * be made ambiguous by two rows sharing a sort key. `age` starting above the
 * oldest oracle row additionally places the whole block after
 * {@link LEGACY_SEED_ROWS}.
 *
 * `birth` is derived from the index by integer arithmetic and is always a real
 * calendar date, because the day component is kept at 28 or below and so cannot
 * fall outside a short month. `name` is a {@link deterministicLabel}, so rows
 * are distinguishable by a value the caller knows in advance.
 *
 * WHAT A CALLER MUST NOT ASSUME. The runner is configured `workers: 1`,
 * `fullyParallel: false`, `retries: 0` precisely because the suite mutates
 * shared `COMPANY` state in one `persistence-db` volume — and that volume is
 * deliberately NOT reset between runs, because the paired-capture rule forbids
 * recreating or reseeding it between the two halves of a comparison. So: DO NOT
 * ASSUME AN EMPTY TABLE, and DO NOT ASSUME A STARTING IDENTITY VALUE. Rows are
 * identified by their generated label, never by row position or by a predicted
 * key. Two calls with the same scope produce the same labels, so a spec needing
 * rows distinct from an earlier run's should vary the scope.
 *
 * @param count How many rows to build. Zero is valid and yields an empty array.
 * @param scope Discriminator passed to {@link deterministicLabel}; defaults to
 *   the same scope {@link buildCompanyRow} uses.
 * @returns A new array, in generated order, of individually frozen rows. The
 *   ARRAY itself is deliberately mutable so a caller may slice, concatenate or
 *   re-sort it while building a request; the ROWS are frozen so no caller can
 *   change data another assertion has already inspected.
 * @throws TypeError If `count` is not an integer.
 * @throws RangeError If `count` is negative.
 */
export function buildCompanyRows(count: number, scope?: string): CompanyRowInput[] {
  requireInteger('count', count, 0, Number.MAX_SAFE_INTEGER);

  const effectiveScope = scope ?? DEFAULT_BUILD_SCOPE;
  const rows: CompanyRowInput[] = [];

  for (let index = 0; index < count; index += 1) {
    // Indexing is guarded because `noUncheckedIndexedAccess` types an element
    // read as possibly undefined. The modulus makes it unreachable in practice;
    // the fallback keeps that a fact rather than an assumption.
    const address = CYCLE_ADDRESSES[index % CYCLE_ADDRESSES.length] ?? DEFAULT_ADDRESS;

    rows.push(
      // Keys are written in DDL order, matching every other row literal in this
      // module, so a serialized row reads the same way wherever it came from.
      Object.freeze({
        name: deterministicLabel(effectiveScope, index),
        age: BASE_AGE + index,
        address,
        salary: BASE_SALARY + index * SALARY_STEP,
        birth: formatBirth(1970 + (index % 50), (index % 12) + 1, (index % 28) + 1),
      }),
    );
  }

  return rows;
}


/* ------------------------------------------------------------------------- *
 * The update and conflict payloads
 *
 * `updatewhere=1` with all six columns marked is what forces two rows onto the
 * wire instead of one. Everything in this section follows from that.
 * ------------------------------------------------------------------------- */

/**
 * One row's contribution to an update: its current state AND its original state.
 *
 * WHY BOTH, ALWAYS. The table specification carries `updatewhere=1`
 * [`dw_sqlite.srd:L14`] and all six columns carry `updatewhereclause=yes`
 * [`:L8-L13`]. `updatewhere=1` is the "key and updateable columns" concurrency
 * mode, so the generated `WHERE` clause carries the key column PLUS the ORIGINAL
 * VALUE of every updateable column — which, with all six marked, means the
 * optimistic-concurrency check spans ALL SIX COLUMNS' ORIGINAL VALUES
 * (AAP §0.6.3.2).
 *
 * The consequence is exact: the payload must transmit, per row, both the current
 * AND the original value of every marked column. A FLAT ROWSET IS INSUFFICIENT —
 * it cannot carry original values, item statuses, or the delete and filter
 * buffers — and that insufficiency is exactly what the Persistence buffer
 * anti-corruption layer exists to overcome.
 *
 * This type is the ROW PAIR ONLY. Anything the transport wraps around it — a
 * per-row item status, a per-value null flag, the enclosing request object — is
 * the spec's business, deliberately, so that a change in the projection's JSON
 * shape costs one spec edit rather than a fixture redesign.
 */
export interface CompanyRowUpdate {
  /** The row as the caller wants it to become. */
  readonly current: CompanyRow;
  /**
   * The row as it was retrieved — the values the `WHERE` clause is built from.
   *
   * If these no longer match what is stored, the update matches no row and the
   * mismatch is reported rather than absorbed. See {@link asStaleUpdate}.
   */
  readonly original: CompanyRow;
}

/**
 * Rejects a non-object, or one carrying the engine-assigned key.
 *
 * Shared by every entry point that accepts a field patch, so the one invariant
 * that must never bend — a caller does not choose the identity value — is
 * enforced in exactly one place and reported the same way everywhere.
 */
function requireInputPatch(parameterName: string, value: unknown): void {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError(`${parameterName} must be an object; received ${typeof value}.`);
  }
  if (Object.prototype.hasOwnProperty.call(value, 'id')) {
    throw new TypeError(
      `${parameterName} must not carry an id: ID is the auto-increment identity ` +
        'column, assigned by the engine on insert and returned to the caller in ' +
        "the update response's identity arrays. It is never set through a field " +
        'patch. Assert on the value that comes back instead of supplying one.',
    );
  }
}

/** Rejects anything that is not a usable retrieved row. */
function requireRow(parameterName: string, value: unknown): void {
  if (typeof value !== 'object' || value === null) {
    throw new TypeError(`${parameterName} must be a CompanyRow object; received ${typeof value}.`);
  }
  const candidate = value as { readonly id?: unknown };
  if (!Number.isInteger(candidate.id)) {
    throw new TypeError(
      `${parameterName}.id must be an integer: an update is keyed on the row's ` +
        'identity, so only a retrieved row can be updated. Insert first, then ' +
        'use the key the engine assigned.',
    );
  }
}

/**
 * Applies a field patch to a retrieved row, returning a new frozen row.
 *
 * The result is assembled from the six known columns rather than from a raw
 * spread, so no stray property can reach the payload, and `id` is taken from the
 * row rather than from the patch so the key cannot be rewritten by a change set.
 * The input is never mutated.
 */
function withChanges(row: CompanyRow, changes: Partial<CompanyRowInput>): CompanyRow {
  const merged = { ...row, ...changes };

  return Object.freeze({
    id: row.id,
    name: merged.name,
    age: merged.age,
    address: merged.address,
    salary: merged.salary,
    birth: merged.birth,
  });
}

/**
 * Builds a well-formed update: `original` as retrieved, `current` with changes.
 *
 * This is the SUCCESS-PATH payload. Because `original` is the row exactly as it
 * was retrieved, the generated `WHERE` clause matches the stored row and the
 * update applies — assuming nothing else has modified it in between, which under
 * `workers: 1` and `fullyParallel: false` nothing in this suite will.
 *
 * NEITHER ARGUMENT IS MUTATED. Both returned rows are new frozen objects, and
 * the wrapper is frozen too, so a payload cannot be altered after the assertion
 * that inspected it — a class of test bug that is unpleasant to find.
 *
 * @param row The row as retrieved, including its engine-assigned `id`.
 * @param changes The fields to change. `id` is not among them by construction.
 * @returns A frozen {@link CompanyRowUpdate}.
 * @throws TypeError If `row` is not a row with an integer `id`, or `changes` is
 *   not an object, or `changes` carries an `id`.
 */
export function asUpdate(row: CompanyRow, changes: Partial<CompanyRowInput>): CompanyRowUpdate {
  requireRow('row', row);
  requireInputPatch('changes', changes);

  return Object.freeze({
    current: withChanges(row, changes),
    original: withChanges(row, {}),
  });
}

/**
 * Builds an update whose `original` values are DELIBERATELY STALE, so the server
 * must detect a mismatch.
 *
 * WHAT THE SUITE EXPECTS IN RESPONSE, stated precisely because this is the one
 * fixture whose entire purpose is a negative outcome:
 *
 * - Persistence and DataServices return gRPC **`Aborted`** — the canonical
 *   mapping to HTTP 409 — carrying a conflict detail with the CURRENT ROW STATE.
 *   `Aborted` rather than `FailedPrecondition` is a deliberate choice: the
 *   operation may succeed if retried against fresh original values.
 * - Gateway's REST projection surfaces it as **HTTP 409** with that same
 *   payload.
 * - Callers implement an explicit RETRY-OR-SURFACE policy, and there is **NO
 *   SILENT OVERWRITE ANYWHERE** in the system (AAP §0.6.3.8). A 200 in response
 *   to this payload is a contract violation, not a passing test: it would mean a
 *   write was applied whose optimistic precondition could not have held.
 *
 * Mechanically the staleness works because `updatewhere=1` puts the original
 * values into the `WHERE` clause. Original values that cannot match any stored
 * row produce a clause that matches nothing, which is exactly the
 * optimistic-concurrency mismatch the contract must report.
 *
 * THE KEY IS LEFT ALONE ON PURPOSE. Staleness is applied only to non-key
 * columns, so the row is still FOUND by its key and then REJECTED on its
 * original values. Corrupting the key instead would produce a
 * row-does-not-exist outcome, which is a different condition and would test the
 * wrong branch.
 *
 * A NO-OP STALENESS IS REJECTED. If `staleOriginals` is supplied but changes
 * nothing — an empty object, or values equal to the row's own — this throws
 * rather than returning a payload. Such a payload would be perfectly valid, the
 * update would SUCCEED, and the conflict spec asserting on it would pass while
 * testing nothing at all. That false pass is worth failing loudly to prevent.
 *
 * @param row The row as retrieved.
 * @param changes The fields `current` should carry.
 * @param staleOriginals Which original values to falsify. Defaults to
 *   {@link STALE_ORIGINAL_SENTINEL} on `name`: one clearly-wrong value on a
 *   non-key column, so the intent is unmistakable.
 * @returns A frozen {@link CompanyRowUpdate} whose `original` cannot match.
 * @throws TypeError If `row` or either patch is unusable, or if a supplied
 *   `staleOriginals` would leave `original` unchanged.
 */
export function asStaleUpdate(
  row: CompanyRow,
  changes: Partial<CompanyRowInput>,
  staleOriginals?: Partial<CompanyRowInput>,
): CompanyRowUpdate {
  requireRow('row', row);
  requireInputPatch('changes', changes);

  if (staleOriginals === undefined) {
    return Object.freeze({
      current: withChanges(row, changes),
      original: withChanges(row, { name: STALE_ORIGINAL_SENTINEL }),
    });
  }

  requireInputPatch('staleOriginals', staleOriginals);

  const actuallyStale = COMPANY_INPUT_COLUMNS.some(
    (column) =>
      Object.prototype.hasOwnProperty.call(staleOriginals, column) &&
      staleOriginals[column] !== row[column],
  );

  if (!actuallyStale) {
    throw new TypeError(
      'staleOriginals would leave original identical to row, so the update ' +
        'would succeed and a conflict assertion would pass without testing ' +
        'anything. Supply at least one non-key value that differs from the row, ' +
        'or omit the argument to use STALE_ORIGINAL_SENTINEL on name.',
    );
  }

  return Object.freeze({
    current: withChanges(row, changes),
    original: withChanges(row, staleOriginals),
  });
}

/*
 * WHY THERE IS NO `keyChangeUpdate` HELPER.
 *
 * A key-change payload was considered and deliberately left out: no spec needs
 * one, and unused fixture surface is a liability rather than an option held in
 * reserve. The behaviour it would have exercised is recorded here instead,
 * because the fixture's own table specification is what makes it reachable.
 *
 * `updatekeyinplace=no` [`dw_sqlite.srd:L14`] means a KEY CHANGE IS EXECUTED AS
 * DELETE-PLUS-INSERT rather than as an in-place update. That setting is also the
 * trigger for a workaround the legacy documents against itself: when a requested
 * key field is not marked as a key, the modified state does not generate the
 * delete and insert statements, so the internal modified state has to be
 * force-refreshed to compensate. The legacy does that by ASSIGNING EACH MODIFIED
 * KEY COLUMN TO ITSELF, purely to flip its item status
 * [`n_cst_thread_task_sqlupdate.sru:L151-L167`, the self-assignment at `:L163`,
 * under the oracle's own fix-me annotation at `:L151-L154`]. That annotation is
 * the legacy documenting a defect against itself; it is quoted here as a
 * finding, and nothing in this module defers work of its own.
 *
 * Because the primary fixture sets `updatekeyinplace=no` ITSELF, this path is
 * exercised by the fixture rather than being a rare branch reachable only by
 * contrivance. None of it is to be "fixed": a self-assignment that mutates
 * hidden state has no .NET analogue, so the port models original-value and
 * status tracking explicitly and reproduces the same statement generation. If a
 * spec ever needs a key-change payload, it belongs here, built on
 * {@link withChanges} — and it must preserve the delete-plus-insert consequence
 * rather than assert an in-place update.
 */

