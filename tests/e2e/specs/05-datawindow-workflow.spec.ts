/**
 * The DataWindow retrieve / validate / update workflow — assertion group 5 of 6.
 * Contracts C-03 and C-04 as projected by C-09 over `/v1/datawindow/**`.
 *
 * WHY THIS WORKFLOW IS THE ONE WORTH DRIVING END TO END
 * ----------------------------------------------------
 * It is the only path that crosses every boundary the decomposition created: an
 * external caller reaches Gateway over REST, Gateway reaches DataServices over
 * gRPC, DataServices reaches Persistence over gRPC, and all three validate
 * tokens minted by Security. Each hop can be proved in isolation by a unit test;
 * only a run like this one proves the hops compose.
 *
 * It is also the FIRST OF THE TWO SPECS THAT MUTATE SHARED STATE, so the
 * conventions settled here — the route table, the request shapes, the
 * candidate-key resolution and the original-value discipline — are the reference
 * that spec 06 builds its conflict assertions on.
 *
 * THE FIXTURE, VERIFIED VERBATIM FROM THE READ-ONLY ORACLE
 * -------------------------------------------------------
 * `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd` is THE ONLY UPDATABLE DATAWINDOW
 * IN THE REPOSITORY, which is what makes it the golden master for the whole
 * retrieval / validation / update triple:
 *
 * - `:L14` — the table specification carries `update="COMPANY"`, `updatewhere=1`,
 *   `updatekeyinplace=no` and `sort="age A salary A "`, and its retrieve clause
 *   projects every column of the one table this suite touches.
 * - `:L8-L13` — all six columns carry `update=yes updatewhereclause=yes`; `id`
 *   alone carries `key=yes identity=yes`.
 * - `:L21-L26` — the detail columns carry one-based ids 1..6 in DDL order, and
 *   `birth` carries the edit mask `yyyy-mm-dd`.
 * - `:L27` — the footer computes `sum(salary for page)`, a PAGE-scoped aggregate.
 *
 * The backing table definition — THE ONLY ONE IN THE REPOSITORY — is at
 * `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469`, and it is
 * transcribed in full by the fixture rather than restated here, because the
 * fixture owns schema facts and this file owns the request envelope. What matters
 * to the assertions below is what it declares: `ID` is the auto-increment
 * integer primary key — the oracle annotates it `自增列` at `:L464` — so an
 * insert MUST NOT supply it and the assigned value ROUND-TRIPS BACK to the
 * caller; `NAME` and `AGE` are declared not-null; and `ADDRESS`, `SALARY` and
 * `BIRTH` are nullable.
 *
 * The legacy sequence at `w_test_sqlite.srw:L189-L203` is: update with
 * accept-text true and reset-flag false, then `Commit()`, then `ResetUpdate()`
 * ONLY after the commit succeeded; on failure `Rollback()` and surface the error
 * without resetting. This spec mirrors that shape over HTTP — a successful
 * update is followed by a re-retrieve that confirms the change, and a refused
 * one is followed by a re-retrieve that confirms nothing changed.
 *
 * Every locator above is SPECIFICATION FOR THE AUTHOR, not an input this file
 * loads. Nothing here reads, imports, globs or fetches any `ws_objects/**` path
 * or any of the three read-only browser-asset directories beside this one (C-C).
 * Every import resolves inside `tests/e2e`.
 *
 * TWO DOCTRINES THIS FILE IS BUILT AROUND
 * ---------------------------------------
 * 1. **NO ABSOLUTE ROW COUNT, NO EXACT RETRIEVE LENGTH, NO FOOTER TOTAL.** The
 *    oracle's seed path inserts four verbatim rows at
 *    `w_test_sqlite.srw:L381-L388` *and then ten more in a loop at `:L396-L400`*,
 *    and the shared `persistence-db` volume is never reseeded. The `COMPANY` row
 *    set is therefore not a fixed quantity, and `sum(salary for page)` depends on
 *    the whole of it. A count assertion here would be flaky and would read like a
 *    product defect. What IS asserted is: invariants about the row this spec
 *    creates itself, self-consistency invariants the chunking contract publishes,
 *    and relative ordering among rows already proved present.
 * 2. **NOTHING IS RESET, RESEEDED, DROPPED, RECREATED OR DDL'D, AND STORAGE IS
 *    NEVER TOUCHED DIRECTLY.** The paired-capture rule forbids recreating or
 *    reseeding the volume between the legacy-side and target-side captures of one
 *    workflow ID, so a spec that cleaned up after itself would void the pair. The
 *    oracle's own file-delete call at `w_test_sqlite.srw:L450` and its
 *    table-creation statement at `:L463` are recorded here only as the two things
 *    this file must never do. Where a known starting row is needed, it is CREATED
 *    THROUGH THE PUBLIC WORKFLOW through Gateway, and it is left in place
 *    afterwards. No statement text of any kind appears in this file: Persistence
 *    is the only service that generates or executes one, and this suite reaches
 *    it only through Gateway.
 *
 * Both doctrines together are what make this file safe to run twice in a row
 * against the same volume, which is the single most valuable check on it.
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules were
 * provided. That is a finding rather than an omission, and it is not licence to
 * lower the bar — the enterprise-standard baseline applies in its place, together
 * with the non-rule constraints below.
 *
 * - **C-B — no behaviour improvement, and documented defects are preserved.**
 *   The DataWindow and the DDL disagree about four columns and every
 *   disagreement is reproduced rather than reconciled. Concretely, here: no
 *   length validation is asserted at 50, 100 or 200 characters anywhere; every
 *   address this file sends is comfortably inside the narrower declared bound, so
 *   a disagreement is never the accidental subject of a happy-path assertion;
 *   `salary` is compared within `SALARY_COMPARISON_TOLERANCE` and never with
 *   strict equality, because a two-place decimal stored in a `REAL` column is not
 *   guaranteed to return an identical double; and `birth` is compared as a
 *   `yyyy-mm-dd` STRING, because the column is `TEXT` in storage. No latency,
 *   throughput, availability or service-level figure is asserted — the repository
 *   publishes none, so any number here would be fabricated. The runner's timeouts
 *   are hang guards, not budgets.
 * - **C-G — every new boundary is authenticated.** Every request below carries a
 *   bearer token minted at run time by Security, the sole issuer, following the
 *   pattern spec 02 establishes: acquire inside the test, attach per request, let
 *   it fall out of scope.
 * - **C-F — no secret in source.** No key, certificate, password, pre-minted
 *   token or encoded credential run appears anywhere in this file. Tokens come
 *   only from `requireServiceToken`, and NO TOKEN, `Authorization` HEADER OR
 *   RESPONSE VALUE IS EVER RENDERED into an assertion message, because a message
 *   reaches the console and the CI log and both are retained.
 * - **C-D — the deferred services are not implemented, so they are not
 *   exercised.** Only the HEADLESS half of the DataWindow surface is asserted.
 *   Nothing here touches window geometry, DPI conversion, font measurement, menu
 *   rendering or input-method behaviour, and no reserved extension-point route is
 *   requested.
 * - **C-J — one orchestration path.** Nothing here starts, stops, restarts,
 *   builds, seeds or health-gates a service. The stack is brought up beforehand,
 *   and exclusively, by `orchestration/docker-compose.yml`.
 * - **C-L — the environment's instructions bind.** The file runs unchanged under
 *   the documented path, `cd tests/e2e && npm ci && npx playwright test`, against
 *   Gateway on its documented ingress port, and it honours the shared-volume rule.
 * - **C-H — nothing about coverage is asserted.** That gate is a .NET concern
 *   measured per service from a Cobertura report.
 * - **Topology.** All functional traffic goes through Gateway, the sole ingress.
 *   There is no direct call to DataServices or Persistence and no database access
 *   of any kind, and the reserved Phase-2 port number appears nowhere.
 *
 * WHAT HAS BEEN VERIFIED, STATED PLAINLY
 * --------------------------------------
 * Every HTTP assertion below is authored against the published contract — the
 * routes, request messages, response messages and status mapping in
 * `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` and the `.proto`
 * definitions it delegates to — rather than against an observed response. All four
 * service `Dockerfile`s exist, but the repository carries no
 * `orchestration/docker-compose.yml`, so nothing assembles them into a stack: the
 * stack could not be brought up and these assertions have NOT been observed
 * passing against live services. What was
 * verified is everything that needs no stack: that the file type-checks under
 * `tsc --noEmit`, that every test is collected by `playwright test --list`, and
 * that the pure-fixture column-contract test below executes and passes with the
 * stack down. Overstating that would mislead every later reader.
 */

import {
  expect,
  test,
  type APIRequestContext,
  type APIResponse,
} from '@playwright/test';

import {
  BIRTH_FORMAT,
  COMPANY_COLUMNS,
  COMPANY_TABLE_NAME,
  DATAWINDOW_PATH_PREFIX,
  EXPECTED_SEED_RETRIEVE_ORDER,
  MARKED_COLUMNS,
  SALARY_COMPARISON_TOLERANCE,
  asUpdate,
  bearerHeaders,
  buildCompanyRow,
  deterministicLabel,
  formatBirth,
  gatewayUrl,
  toIdentity,
  type CompanyColumn,
  type CompanyColumnName,
  type CompanyRow,
  type CompanyRowInput,
  type CompanyRowUpdate,
  type ServiceToken,
} from '../fixtures';

import {
  assertTokenIssuanceProvisioned,
  requireServiceToken,
} from '../fixtures/token-issuance';

import { requireLiveStack } from '../fixtures/live-stack';

import {
  COLUMN_VALUE,
  DATAWINDOW_ROW,
  JSON_MEDIA_TYPE,
  PROBLEM_JSON_MEDIA_TYPE,
  RETRIEVE_CHUNK,
  assertMediaType,
  assertMembers,
  describeShapeForFailure,
  readCanonicalMember,
} from '../fixtures/contract-shape';

/* ------------------------------------------------------------------------- *
 * THE ROUTE TABLE — THE SINGLE EDIT POINT FOR ROUTE SHAPE IN THIS FILE
 *
 * Every path this spec requests is composed here, from the one imported prefix,
 * and nowhere else. If a projected route is ever renamed, this object is the
 * only thing that changes and the change is one line — which is exactly why no
 * path literal appears further down. Spec 06 reads this table rather than
 * re-deriving its own.
 *
 * The segments are the ones the ingress contract declares beneath
 * `/v1/datawindow`: `retrieve` projects C-03's `Retrieve` server stream, and
 * `update` projects C-03's `Update`, which is the operation that declares the
 * `409` an optimistic-concurrency mismatch produces.
 *
 * NO SESSION ROUTE IS USED HERE, AND THAT IS DELIBERATE RATHER THAN AN
 * OMISSION. A validation session materializes the four pieces of cross-event
 * mutable state that the legacy holds as private fields of one control, and it
 * is required by the EVENT CHAIN — not by a retrieve or by an update. The
 * update contract carries the session identifier as an OPTIONAL member,
 * because an update may be issued outside a chain, and that is the shape this
 * workflow uses. Opening a session here would assert session lifecycle in the
 * file that owns the data workflow, and lifecycle already has its own coverage.
 * ------------------------------------------------------------------------- */
const ROUTES = {
  /** `POST` — the RETRIEVAL third of the triple. Answers an ordered chunk sequence. */
  retrieve: `${DATAWINDOW_PATH_PREFIX}/retrieve`,

  /** `POST` — the UPDATE third of the triple. Carries inserts and updates alike. */
  update: `${DATAWINDOW_PATH_PREFIX}/update`,
} as const;

/**
 * The DataWindow this workflow addresses — the file's single edit point for the
 * handle, for the same reason {@link ROUTES} is the single edit point for paths.
 *
 * The handle is an OPAQUE STRING as far as the ingress is concerned: Gateway
 * binds it into the upstream request and holds no state keyed by it, so the
 * value's meaning belongs entirely to DataServices. The value used is the oracle
 * DataWindow object's own name, because that is the only name the repository
 * publishes for the one updatable DataWindow it contains — the object exported as
 * `dw_sqlite.srd`. Naming it from the oracle keeps the value traceable instead of
 * arbitrary.
 */
const DATAWINDOW_HANDLE = 'dw_sqlite';

/* ------------------------------------------------------------------------- *
 * THE REQUEST SIDE — EXACT CONTRACT NAMES, TYPED
 *
 * The ingress binds a request body with the protobuf JSON parser in its
 * DEFAULT configuration, which REJECTS AN UNKNOWN FIELD rather than ignoring
 * it. A request member is therefore not a place to be approximate: the names
 * below are the canonical protobuf JSON mapping of the message each operation
 * declares — lowerCamelCase of the proto field name — and they are declared
 * once, as types, so the compiler keeps every call site honest.
 *
 * Enumerated values travel as their PROTO ENUMERATOR NAMES, not their numbers,
 * because that is what the canonical mapping emits and accepts. The SCREAMING_
 * SNAKE spellings are preserved verbatim, exactly as the constant identifiers
 * are preserved on the .NET side: they appear in serialized payloads, log
 * records and characterization recordings, where a restyling would silently
 * invalidate every stored comparison that mentions one.
 * ------------------------------------------------------------------------- */

/** `Primary!` — the live rows. The zero value, and the buffer this workflow uses. */
const DW_BUFFER_PRIMARY = 'DW_BUFFER_PRIMARY';

/**
 * `NewModified!` — inserted and edited.
 *
 * This is the status an INSERT must carry, and it is not interchangeable with
 * `ITEM_STATUS_NEW`: the legacy identity round-trip collects a value only for a
 * row whose status reads `NewModified!`
 * [`n_cst_thread_task_sqlupdate.sru:L232`, and `:L238` for the filter buffer],
 * so a row tagged otherwise would be inserted without its assigned key ever
 * coming back.
 */
const ITEM_STATUS_NEW_MODIFIED = 'ITEM_STATUS_NEW_MODIFIED';

/** `DataModified!` — an existing row that has been edited. The status an UPDATE carries. */
const ITEM_STATUS_DATA_MODIFIED = 'ITEM_STATUS_DATA_MODIFIED';

/**
 * One legacy scalar value on the wire.
 *
 * EXACTLY ONE MEMBER IS PRESENT, which is how the canonical mapping encodes a
 * `oneof`. Three of the arms carry a distinction this workflow depends on:
 * `isNull` is an arm of its OWN rather than the absence of a value, so "no
 * value" stays distinguishable from "the value zero"; a decimal is carried as
 * exact TEXT rather than as a JSON number, because the legacy type is exact and
 * a double is not; and a date is carried as text so that no zone is implied
 * where the legacy implies none.
 *
 * Only the arms this file actually writes or reads are declared. The wire domain
 * is wider — a boolean, an unsigned 64-bit integer, a time, a timestamp and a
 * blob arm all exist — and declaring the unused ones here would be inventing a
 * dependency on members no `COMPANY` column can hold.
 */
interface WireAnyValue {
  readonly isNull?: boolean;
  readonly stringValue?: string;
  readonly int64Value?: string;
  readonly doubleValue?: number;
  readonly decimalValue?: { readonly value: string };
  readonly dateValue?: { readonly value: string };
}

/** One column's value within a row: its name, its ONE-BASED ordinal, and the value. */
interface WireColumnValue {
  readonly columnName: string;
  readonly columnId: number;
  readonly value: WireAnyValue;
}

/**
 * One row of the carrier, as both directions of the boundary describe it.
 *
 * `originalValues` is the member that makes this a DataWindow carrier rather
 * than a flat rowset, and it is the whole reason the update half works at all:
 * the table specification carries `updatewhere=1` with all six columns marked
 * `updatewhereclause=yes`, so the generated `WHERE` clause carries the key
 * column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE COLUMN. A payload carrying
 * only current values could not express what the statement compared.
 */
interface WireDataWindowRow {
  readonly buffer: string;
  readonly row: number;
  readonly itemStatus: string;
  readonly columns: readonly WireColumnValue[];
  readonly originalValues: readonly WireColumnValue[];
}

/** The retrieve request. Empty `buffers` means the primary buffer, the legacy default. */
interface WireRetrieveRequest {
  readonly datawindowHandle: string;
  readonly buffers: readonly string[];
}

/**
 * The update request.
 *
 * `sessionId` is carried as an empty string rather than omitted: the member is
 * optional in the sense that an update may be issued outside a validation
 * chain, and the canonical mapping's default for a string field is the empty
 * string, so this is the encoding of "no session" rather than a placeholder.
 *
 * Two members the legacy update call took are DELIBERATELY ABSENT because the
 * contract RESERVED them — accept-text and the reset flag. The legacy passes
 * true and false respectively [`w_test_sqlite.srw:L189`], and the contract fixes
 * that behaviour rather than parameterising it, so sending either name would be
 * rejected as an unknown field.
 */
interface WireUpdateRequest {
  readonly datawindowHandle: string;
  readonly sessionId: string;
  readonly rows: readonly WireDataWindowRow[];
}

/* ------------------------------------------------------------------------- *
 * THE RESPONSE SIDE — ONE CANDIDATE-KEY TABLE, RESOLVED CANONICALLY
 *
 * 🔴 THIS TABLE USED TO LIST ALTERNATIVES, AND THAT WAS A DEFECT RATHER THAN A
 * KINDNESS. Each entry carried the canonical spelling plus "plausible"
 * alternatives — `Rows` beside `rows`, `row_count` beside `rowCount`, `isFinal`
 * beside `final`, and an `items`/`result`/`data` envelope beside a body the
 * contract publishes as a bare array. The stated intent was a better diagnostic.
 * The actual effect was that THIS SUITE COULD PASS AGAINST A BOUNDARY NO
 * GENERATED CLIENT CAN CONSUME: a service emitting `row_count` would satisfy
 * every assertion here while a client generated from the OpenAPI document or
 * from protobuf JSON read `undefined`. A contract test that accepts a shape the
 * contract does not publish is not testing the contract.
 *
 * ⚠ THE ALTERNATIVES ARE DETECTED, NOT ACCEPTED. This table used to be resolved
 * TOLERANTLY: whichever spelling appeared was returned, so `ret_code`, `is_final`
 * and `Rows` each satisfied a read and everything downstream passed. The
 * diagnostic reasoning above is sound and is preserved — a boundary that merely
 * serialized a member differently should be told so in those words rather than
 * accused of omitting it — but the VERDICT was wrong: a response-field rename is
 * a contract change, and it is the single most likely drift on a freshly
 * decomposed boundary. The canonical spelling is now required, and an alternative
 * found in its place produces that same good diagnostic AS A FAILURE.
 *
 * This is emphatically NOT a licence to guess a shape: no member is read that the
 * published contract does not declare, and the exact member sets are asserted
 * from `fixtures/contract-shape.ts` where the schema declares
 * `additionalProperties: false`. The table remains the one edit point, so a
 * genuine contract change still costs one edit here rather than an edit at every
 * call site. Spec 06 resolves its conflict payload the same way.
 * ------------------------------------------------------------------------- */
const RESPONSE_KEYS = {
  /**
   * ⚠ THE ENVELOPE ENTRY IS GONE, AND ITS ABSENCE IS THE ASSERTION.
   *
   * It read `chunkCollection: ['chunks', 'items', 'result', 'data']`, described as
   * covering "an envelope appearing anyway" — four member names, not one of which
   * the published contract declares. `RetrieveResult` is `type: array`: a
   * retrieval answers the ORDERED SEQUENCE OF CHUNKS the server stream would have
   * delivered, a bare collection, because the protocol definition has no envelope
   * message for a stream and wrapping one would add a member the gRPC contract
   * does not have.
   *
   * Removing the entry is what makes an invented envelope FAIL rather than be
   * silently unwrapped — and the old fallback was worse than tolerant: when none
   * of the four matched it yielded an EMPTY collection, so an unrecognised envelope
   * produced zero rows and every downstream assertion reported a missing row
   * instead of a wrong shape. `readChunks` now refuses a non-array body outright
   * and says so.
   */

  /** Rows within one chunk. */
  rows: ['rows', 'Rows'],

  /** Per-chunk chunking-contract members, retained rather than flattened away. */
  rowCount: ['rowCount'],
  chunkIndex: ['chunkIndex'],
  cumulativeRowCount: ['cumulativeRowCount'],
  final: ['final'],

  /** Row-level carrier members. Their presence is what makes this a DataWindow. */
  buffer: ['buffer'],
  itemStatus: ['itemStatus'],
  columns: ['columns'],
  originalValues: ['originalValues'],

  /** Column-level members. Schema: `ColumnValue`. */
  columnName: ['columnName'],
  columnId: ['columnId'],
  columnValue: ['value'],

  /**
   * The scalar arms this workflow reads, in the order a numeric read prefers
   * them. Schema: `AnyValue`, which declares exactly one member per value.
   */
  valueIsNull: ['isNull'],
  valueText: ['stringValue'],
  valueInt64: ['int64Value'],
  valueDouble: ['doubleValue'],
  valueDecimal: ['decimalValue'],
  valueDate: ['dateValue'],
  wrappedValue: ['value'],

  /**
   * The identity round-trip.
   *
   * The update response carries the identity column and two value arrays — one
   * collected from the primary buffer FORWARD and one from the filter buffer
   * BACKWARD, because the filter buffer's row order is inverted relative to the
   * source [`n_cst_thread_task_sqlupdate.sru:L235-L241`]. A row inserted into the
   * primary buffer lands in the first of the two.
   */
  identity: ['identity'],
  identityColumnId: ['identityColumnId'],
  identityPrimaryValues: ['primaryValues'],

  /** The affected-row counts. Emitted as strings, because they are 64-bit. */
  rowsInserted: ['rowsInserted'],
  rowsUpdated: ['rowsUpdated'],

  /** The legacy return code the response or the problem document carries. */
  retCode: ['retCode'],

  /**
   * The structured-error members.
   *
   * A refusal is an RFC 9457 problem document — the shape the framework emits
   * without hand-written code — so the ERROR CONTAINER IS THE BODY ITSELF rather
   * than a nested member. These are the members that make it machine-readable,
   * and the one thing this file reads from them is that at least one is present
   * and that `status` agrees with the transport status. `detail` is never
   * rendered into an assertion message.
   */
  problemMembers: ['type', 'title', 'status', 'detail', 'instance'],
  problemStatus: ['status'],
} as const;

/**
 * `failOnStatusCode` is pinned false on every request below, and on the refusal
 * case that is a requirement rather than a preference.
 *
 * Left to throw on a non-2xx, the runner raises an error of its own whose
 * message can quote the response body verbatim — and a body from an
 * authenticated endpoint is the one thing that must never reach a log. Pinning
 * it false routes every status through an assertion in this file, so exactly one
 * message shape is produced and it quotes nothing. It also keeps the diagnostic
 * honest: a failure reports WHICH status arrived rather than an opaque transport
 * error.
 */
const NEVER_THROW_ON_STATUS = { failOnStatusCode: false } as const;

/* ------------------------------------------------------------------------- *
 * Reading a response, defensively
 *
 * Everything below is a pure function over a parsed JSON value. None of it
 * asserts; each returns `undefined` where a member is absent or the wrong shape,
 * and the TEST decides what an absence means. Keeping the two apart is what lets
 * a failure message name the missing member instead of surfacing as a type
 * error from inside a helper.
 * ------------------------------------------------------------------------- */

/** Narrows a parsed JSON value to a plain object, or `undefined` if it is not one. */
function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/**
 * Reads a member by its CANONICAL name, failing when only a variant is present.
 *
 * ⚠ THIS USED TO RETURN THE FIRST CANDIDATE PRESENT, AND THAT WAS THE DEFECT.
 * The table's first entry is the spelling the published contract declares and the
 * rest are plausible re-serializations, so `ret_code`, `is_final` and `Rows` each
 * satisfied a read and every assertion downstream of it passed — meaning a
 * response-field rename, the most likely drift on a freshly decomposed boundary,
 * could not be detected by this file at all.
 *
 * The table and its diagnostic value both survive; only the verdict changed. The
 * canonical name is required, and a variant found in its place is reported as the
 * drift it is, naming both spellings — which is a better message than the
 * "member is missing" the original reasoning was written to avoid, and it now
 * arrives as a failure rather than as a silent pass.
 *
 * Presence is still tested with `hasOwnProperty` rather than against `undefined`,
 * so a member that is genuinely present and null reads as present. That
 * distinction matters on this boundary: null is a value of its own in the legacy
 * scalar domain, not an absence.
 */
function readMember(container: unknown, candidates: readonly string[]): unknown {
  const [canonical = '', ...variants] = candidates;

  return readCanonicalMember(
    container,
    canonical,
    variants,
    `the response member '${canonical}'`,
  );
}

/** Reads a member expected to be an array, or `undefined` when it is not one. */
function readArray(
  container: unknown,
  candidates: readonly string[],
): readonly unknown[] | undefined {
  const member: unknown = readMember(container, candidates);

  return Array.isArray(member) ? (member as readonly unknown[]) : undefined;
}

/**
 * Reads a 64-bit integer member that the canonical mapping emits as a STRING.
 *
 * Delegated to the fixture's converter rather than to `Number(...)`, because
 * that converter REFUSES a value the `number` type cannot carry exactly instead
 * of rounding it silently — and a rounded key addresses a different row, or
 * none, while still passing an integer check. A non-integer or absent member
 * yields `undefined` rather than a throw, so the caller can say what was missing.
 */
function readInt64(container: unknown, candidates: readonly string[]): number | undefined {
  const member: unknown = readMember(container, candidates);

  if (member === undefined || member === null) {
    return undefined;
  }

  try {
    return toIdentity(member, 'value');
  } catch {
    return undefined;
  }
}

/**
 * The one-based ordinal of a column, derived from the fixture rather than typed
 * out.
 *
 * The DataWindow declares its detail columns with ids 1 through 6 in the order
 * the DDL declares them [`dw_sqlite.srd:L21-L26`], and the fixture's table is in
 * that same order — so the ordinal is the position, and deriving it means a
 * column can never be added to the schema description without the ordinals
 * following. Typing the six numbers out instead would create a second source of
 * truth that agrees until the first edit.
 */
function columnOrdinal(column: CompanyColumnName): number {
  const index: number = COMPANY_COLUMNS.findIndex(
    (candidate: CompanyColumn) => candidate.name === column,
  );

  if (index < 0) {
    throw new Error(
      `${column} is not a column of ${COMPANY_TABLE_NAME}. The schema is ` +
        'described in exactly one place, and this name is not in it.',
    );
  }

  return index + 1;
}

/**
 * Locates one column's value within a retrieved row.
 *
 * Matched on NAME FIRST, case-insensitively — the oracle spells its column names
 * in lower case and SQLite identifiers are case-insensitive, so a projection
 * that upper-cased them would be conforming rather than broken — and on the
 * ONE-BASED ORDINAL as a fallback. The fallback is not defensive padding: the
 * legacy's cross-thread carrier serialization is POSITIONAL and carries column
 * ordinals with no names at all, so a row projected from a carrier changeset
 * legitimately leaves the name empty and the ordinal is then the authoritative
 * identifier.
 */
function findColumnValue(row: unknown, column: CompanyColumnName): unknown {
  return findColumnValueIn(readArray(row, RESPONSE_KEYS.columns), column);
}

/**
 * Locates one column's ENTRY within an arbitrary column collection.
 *
 * Separated from the row-scoped reader above because `DataWindowRow` carries TWO
 * such collections — `columns` and `originalValues` — and the concurrency
 * contract is a statement about both: the baseline is required per column, so a
 * reader that could only look inside `columns` could not check it. The matching
 * rule is the row-scoped one unchanged: name first, case-insensitively, then the
 * one-based ordinal, because a carrier-projected row legitimately carries
 * ordinals and no names.
 */
function findColumnEntryIn(
  columns: readonly unknown[] | undefined,
  column: CompanyColumnName,
): unknown {
  if (columns === undefined) {
    return undefined;
  }

  const wanted: string = column.toLowerCase();

  for (const candidate of columns) {
    const name: unknown = readMember(candidate, RESPONSE_KEYS.columnName);

    if (typeof name === 'string' && name.trim().toLowerCase() === wanted) {
      return candidate;
    }
  }

  const ordinal: number = columnOrdinal(column);

  for (const candidate of columns) {
    if (readInt64(candidate, RESPONSE_KEYS.columnId) === ordinal) {
      return candidate;
    }
  }

  return undefined;
}

/** The scalar value node of one column within a collection, or `undefined`. */
function findColumnValueIn(
  columns: readonly unknown[] | undefined,
  column: CompanyColumnName,
): unknown {
  const entry: unknown = findColumnEntryIn(columns, column);

  return entry === undefined ? undefined : readMember(entry, RESPONSE_KEYS.columnValue);
}

/** Reads the `value` member out of a wrapper arm such as a decimal or a date. */
function unwrapValue(arm: unknown): string | undefined {
  const inner: unknown = readMember(arm, RESPONSE_KEYS.wrappedValue);

  return typeof inner === 'string' ? inner : undefined;
}

/** True when the value present is the explicit null arm rather than a scalar. */
function isNullValue(value: unknown): boolean {
  return readMember(value, RESPONSE_KEYS.valueIsNull) === true;
}

/**
 * Reads a column as TEXT, from whichever arm the boundary chose.
 *
 * `birth` is the reason this accepts more than one arm. The DataWindow declares
 * it `date` while the column is `TEXT` — a preserved mismatch — so a faithful
 * projection may legitimately carry it as either a date arm or a plain string
 * arm, and both are the same `yyyy-mm-dd` text underneath. Reading it as text in
 * both cases is what lets the comparison be a STRING comparison, which is the
 * only comparison the storage type justifies.
 */
function readColumnText(row: unknown, column: CompanyColumnName): string | undefined {
  return textOfValue(findColumnValue(row, column));
}

/**
 * Reads one already-located value node as TEXT.
 *
 * Split out of {@link readColumnText} so the same reading applies to a value from
 * `originalValues` as to one from `columns` — the baseline comparison needs both
 * sides read the same way, and re-implementing the arm selection for one of them
 * would compare two different readings rather than two values.
 */
function textOfValue(value: unknown): string | undefined {
  if (value === undefined || isNullValue(value)) {
    return undefined;
  }

  const text: unknown = readMember(value, RESPONSE_KEYS.valueText);

  if (typeof text === 'string') {
    return text;
  }

  return (
    unwrapValue(readMember(value, RESPONSE_KEYS.valueDate)) ??
    unwrapValue(readMember(value, RESPONSE_KEYS.valueDecimal))
  );
}

/**
 * Reads a column as a NUMBER, from whichever arm the boundary chose.
 *
 * The arms are tried in order of fidelity: an exact decimal first, then a
 * genuine double, then a widened integer, then text. `salary` is the case that
 * matters — it is declared `decimal(2)` over a `REAL` column, so it may arrive as
 * exact decimal text or as a double, and either way the comparison that follows
 * must be within a tolerance rather than exact.
 */
function readColumnNumber(row: unknown, column: CompanyColumnName): number | undefined {
  const value: unknown = findColumnValue(row, column);

  if (value === undefined || isNullValue(value)) {
    return undefined;
  }

  const decimalText: string | undefined = unwrapValue(
    readMember(value, RESPONSE_KEYS.valueDecimal),
  );

  if (decimalText !== undefined) {
    const parsedDecimal = Number(decimalText);

    return Number.isFinite(parsedDecimal) ? parsedDecimal : undefined;
  }

  const double: unknown = readMember(value, RESPONSE_KEYS.valueDouble);

  if (typeof double === 'number' && Number.isFinite(double)) {
    return double;
  }

  const widened: unknown =
    readMember(value, RESPONSE_KEYS.valueInt64) ?? readMember(value, RESPONSE_KEYS.valueText);

  if (typeof widened === 'number' && Number.isFinite(widened)) {
    return widened;
  }

  if (typeof widened === 'string') {
    const parsed = Number(widened);

    return Number.isFinite(parsed) ? parsed : undefined;
  }

  return undefined;
}

/* ------------------------------------------------------------------------- *
 * Encoding a fixture row for the wire
 *
 * Row DATA comes from the fixture and only from the fixture; what follows is
 * purely the ENVELOPE, which is the spec's business by design. Nothing here
 * invents a value, and nothing here restates the schema.
 * ------------------------------------------------------------------------- */

/**
 * The columns an INSERT supplies — every column except the identity one.
 *
 * Derived by filtering the fixture's own `isIdentity` marker rather than listing
 * five names, so the omission is stated as the REASON for it. The oracle omits
 * `ID` from all four of its batch inserts and from its dynamic path as well
 * [`w_test_sqlite.srw:L381-L388`, `:L396-L400`], because the engine assigns it.
 */
const INSERT_COLUMNS: readonly CompanyColumnName[] = COMPANY_COLUMNS.filter(
  (column: CompanyColumn) => !column.isIdentity,
).map((column: CompanyColumn) => column.name);

/**
 * Reads one field off an insert-shaped row.
 *
 * Refuses the identity column outright rather than answering null for it. A
 * caller asking an insert-shaped row for its key has made a mistake that would
 * otherwise travel silently into a payload, and the fixture takes the same
 * posture — it rejects a patch carrying an `id` instead of dropping it.
 */
function insertFieldValue(
  input: CompanyRowInput,
  column: CompanyColumnName,
): string | number | null {
  if (column === 'id') {
    throw new Error(
      'id is the auto-increment identity column, assigned by the engine on ' +
        'insert and returned to the caller afterwards. It is never read off an ' +
        'insert-shaped row, and never sent.',
    );
  }

  return input[column];
}

/**
 * Encodes one scalar into the arm its DECLARED DataWindow type calls for.
 *
 * The DataWindow's declaration governs, not the DDL's, because what travels here
 * is a DataWindow row. That choice is what carries two of the four preserved
 * mismatches onto the wire intact rather than quietly reconciling them: `salary`
 * is declared `decimal(2)` and so is sent as EXACT DECIMAL TEXT even though the
 * column is `REAL`, and `birth` is declared `date` and so is sent as a date even
 * though the column is `TEXT`. Reading them back accepts either arm, because the
 * mismatch means either is faithful.
 */
function encodeScalar(column: CompanyColumnName, value: string | number | null): WireAnyValue {
  if (value === null) {
    // An arm of its own, never an omitted member: the legacy has null for value
    // types and the concurrency comparison distinguishes null from empty, so a
    // payload that dropped the member would compare differently from the oracle.
    return { isNull: true };
  }

  if (column === 'salary') {
    return { decimalValue: { value: String(value) } };
  }

  if (column === 'birth') {
    return { dateValue: { value: String(value) } };
  }

  if (typeof value === 'number') {
    // `id` and `age` are the legacy `number` domain, carried widened as a 64-bit
    // integer and emitted as a string by the canonical mapping. Widened rather
    // than narrowed deliberately: a narrower wire type would truncate silently
    // instead of failing.
    return { int64Value: String(value) };
  }

  return { stringValue: value };
}

/** Encodes one column: its name, its one-based ordinal, and its value. */
function encodeColumn(column: CompanyColumnName, value: string | number | null): WireColumnValue {
  return {
    columnName: column,
    columnId: columnOrdinal(column),
    value: encodeScalar(column, value),
  };
}

/**
 * Encodes an insert: a `NewModified!` primary-buffer row carrying the five
 * non-identity columns and no originals.
 *
 * `originalValues` is empty because an inserted row HAS no prior state to
 * compare against — the concurrency check applies to an update, and the contract
 * says as much: originals are populated on the update path.
 *
 * @param input the row to insert, built by the fixture
 * @param nulledColumns columns to send as the explicit null arm regardless of
 *   what the fixture supplied, so that a `NOT NULL` refusal can be provoked with
 *   fixture-built data rather than with a hand-assembled row
 */
function encodeInsertRow(
  input: CompanyRowInput,
  nulledColumns: readonly CompanyColumnName[] = [],
): WireDataWindowRow {
  return {
    buffer: DW_BUFFER_PRIMARY,
    // ONE-BASED, like every row ordinal in this system. A single-row payload is
    // row 1, not row 0.
    row: 1,
    itemStatus: ITEM_STATUS_NEW_MODIFIED,
    columns: INSERT_COLUMNS.map((column: CompanyColumnName) =>
      encodeColumn(
        column,
        nulledColumns.includes(column) ? null : insertFieldValue(input, column),
      ),
    ),
    originalValues: [],
  };
}

/**
 * Encodes an update: a `DataModified!` primary-buffer row carrying the current
 * values AND the original value of every marked column.
 *
 * BOTH SETS ARE MANDATORY HERE, and that is the whole substance of the update
 * contract. `updatewhere=1` is the "key and updateable columns" concurrency
 * mode, and all six columns carry `updatewhereclause=yes`, so the generated
 * `WHERE` clause compares ALL SIX ORIGINAL VALUES. The originals are drawn from
 * {@link MARKED_COLUMNS} rather than from a list written here, so the payload
 * follows the fixture's declaration of what is marked instead of a copy of it.
 *
 * WHY `verbatimOriginals` EXISTS, AND WHY IT IS PREFERRED WHEN AVAILABLE. The
 * `WHERE` clause has to match what is STORED, and `salary` is a two-place decimal
 * held in a `REAL` column — a preserved mismatch — so a value re-encoded from a
 * decoded double is not guaranteed to be the text the server would compare
 * against. Re-sending the server's OWN values for the originals removes that
 * whole class of doubt. The alternative would surface as a `409` in the file that
 * owns the success path, which is both the wrong file for a conflict and a
 * failure that would look like a product defect rather than a payload artefact.
 * The fixture's `original` half remains the fallback, and it remains the
 * semantic definition of what an original IS.
 *
 * @param update the `{ current, original }` pair the fixture built
 * @param verbatimOriginals the row's own values exactly as the retrieval
 *   answered them, when the caller has them
 */
function encodeUpdateRow(
  update: CompanyRowUpdate,
  verbatimOriginals?: readonly WireColumnValue[],
): WireDataWindowRow {
  const originals: readonly WireColumnValue[] =
    verbatimOriginals !== undefined && verbatimOriginals.length > 0
      ? verbatimOriginals
      : MARKED_COLUMNS.map((column: CompanyColumnName) =>
          encodeColumn(column, update.original[column]),
        );

  return {
    buffer: DW_BUFFER_PRIMARY,
    row: 1,
    itemStatus: ITEM_STATUS_DATA_MODIFIED,
    columns: MARKED_COLUMNS.map((column: CompanyColumnName) =>
      encodeColumn(column, update.current[column]),
    ),
    originalValues: originals,
  };
}

/**
 * Re-encodes a retrieved row's own column values, so they can be sent back as
 * the originals of an update.
 *
 * The values are carried through UNCHANGED; only the envelope is rebuilt, and the
 * ordinal is re-derived from the fixture so a row that arrived from a positional
 * carrier changeset — which legitimately carries no names — still yields a named
 * payload. A column the retrieval did not answer is skipped rather than
 * substituted, because the contract permits an original to be omitted when it
 * equals its current value and a fabricated member would say something the
 * retrieval did not.
 */
function verbatimColumnsFrom(row: unknown): readonly WireColumnValue[] {
  const encoded: WireColumnValue[] = [];

  for (const column of MARKED_COLUMNS) {
    const value: unknown = findColumnValue(row, column);
    const record: Record<string, unknown> | undefined = asRecord(value);

    if (record !== undefined) {
      encoded.push({
        columnName: column,
        columnId: columnOrdinal(column),
        value: record as WireAnyValue,
      });
    }
  }

  return encoded;
}

/* ------------------------------------------------------------------------- *
 * Driving the ingress
 *
 * Two senders and two readers, so that no test below composes a URL, builds a
 * header or decides how a body is serialized. Every request goes to Gateway.
 * ------------------------------------------------------------------------- */

/**
 * Issues the retrieval.
 *
 * `buffers` is sent EMPTY, which the contract defines as the primary buffer
 * alone — the legacy default, since a freshly retrieved DataWindow has nothing
 * in `Delete!` and nothing in `Filter!` until a delete or a filter has run.
 * Chunk size is left unsent, so the server chooses: naming a size here would
 * assert a chunking policy this workflow has no stake in.
 */
function requestRetrieve(request: APIRequestContext, token: ServiceToken): Promise<APIResponse> {
  const body: WireRetrieveRequest = {
    datawindowHandle: DATAWINDOW_HANDLE,
    buffers: [],
  };

  return request.post(gatewayUrl(ROUTES.retrieve), {
    headers: bearerHeaders(token),
    data: body,
    ...NEVER_THROW_ON_STATUS,
  });
}

/** Issues an update — an insert and an edit travel through the same operation. */
function requestUpdate(
  request: APIRequestContext,
  token: ServiceToken,
  rows: readonly WireDataWindowRow[],
): Promise<APIResponse> {
  const body: WireUpdateRequest = {
    datawindowHandle: DATAWINDOW_HANDLE,
    sessionId: '',
    rows,
  };

  return request.post(gatewayUrl(ROUTES.update), {
    headers: bearerHeaders(token),
    data: body,
    ...NEVER_THROW_ON_STATUS,
  });
}

/**
 * Reads the chunk sequence out of a retrieval response.
 *
 * The contract publishes the body as a BARE ORDERED COLLECTION — `RetrieveResult`
 * is `type: array` of `RetrieveChunk`, because the protocol definition has no
 * envelope message for a stream. Element order is the stream's order and is never
 * re-sorted here.
 *
 * 🔴 AN ENVELOPE IS NO LONGER TOLERATED. This used to fall back to reading
 * `chunks`/`items`/`result`/`data` off an object body, which meant a projection
 * that wrapped the stream in an invented envelope satisfied every assertion in
 * this file while a client generated from the published document received an
 * object where it expected an array. A non-array body now yields an empty
 * sequence, and the caller reports that as the shape failure it is.
 */
async function readChunks(response: APIResponse): Promise<readonly unknown[]> {
  assertMediaType(
    response.headers()['content-type'],
    JSON_MEDIA_TYPE,
    `${ROUTES.retrieve}`,
  );

  const body: unknown = await response.json();

  // A BARE ARRAY, AND NOTHING ELSE. `RetrieveResult` is `type: array` on the
  // published contract: the retrieval answers the ordered sequence of chunks the
  // server stream would have delivered, and the protocol definition has no
  // envelope message for a stream, so there is no envelope to permit. This used to
  // fall back to `readArray(body, ['chunks','items','result','data'])` when the
  // body was not an array — four invented envelope names, none of which the
  // contract declares — and, worse, it returned `[]` when none matched, so an
  // envelope nobody recognised produced ZERO ROWS and every downstream assertion
  // reported "the row is missing" rather than "the payload is the wrong shape".
  if (!Array.isArray(body)) {
    throw new Error(
      `${ROUTES.retrieve} answered a JSON value that is not an array. The ` +
        'published RetrieveResult is a bare array of chunks — the protocol has no ' +
        'envelope message for a stream, so an object here is an invented envelope ' +
        'rather than a permitted variation. ' +
        `${describeShapeForFailure(JSON.stringify(body))}.`,
    );
  }

  // Each chunk against the exact `RetrieveChunk` member set: five required members
  // — `rows`, `rowCount`, `chunkIndex`, `final`, `cumulativeRowCount` — and
  // `additionalProperties: false`. The chunking contract is the reason those exist,
  // and a chunk that had dropped `final` or renamed `cumulativeRowCount` would
  // still have yielded its rows to every assertion in this file.
  for (const [index, chunk] of body.entries()) {
    assertMembers(chunk, RETRIEVE_CHUNK, `${ROUTES.retrieve} chunk[${index}]`);
  }

  return body as readonly unknown[];
}

/**
 * Flattens every chunk's rows into one sequence, preserving arrival order.
 *
 * Each row is asserted against `DataWindowRow` and each of its columns against
 * `ColumnValue` as it passes through. That is the assertion that makes the payload
 * a DATAWINDOW CARRIER rather than a flat rowset — the distinction the whole
 * update contract rests on, since `buffer`, `row`, `itemStatus` and the optional
 * `originalValues` are exactly what a rowset would have discarded. It was
 * previously unasserted anywhere: the file read `buffer` and `itemStatus` off
 * individual rows where it happened to need them, so a projection that had dropped
 * them from every OTHER row went unnoticed.
 */
function collectRows(chunks: readonly unknown[]): readonly unknown[] {
  const rows: unknown[] = [];

  for (const [chunkIndex, chunk] of chunks.entries()) {
    for (const [rowIndex, row] of (readArray(chunk, RESPONSE_KEYS.rows) ?? []).entries()) {
      const carrier: Record<string, unknown> = assertMembers(
        row,
        DATAWINDOW_ROW,
        `chunk[${chunkIndex}].rows[${rowIndex}]`,
      );

      for (const [columnIndex, column] of (
        readArray(carrier, RESPONSE_KEYS.columns) ?? []
      ).entries()) {
        assertMembers(
          column,
          COLUMN_VALUE,
          `chunk[${chunkIndex}].rows[${rowIndex}].columns[${columnIndex}]`,
        );
      }

      rows.push(row);
    }
  }

  return rows;
}

/** Retrieves and flattens in one step, which is what every assertion below wants. */
async function retrieveRows(
  request: APIRequestContext,
  token: ServiceToken,
): Promise<readonly unknown[]> {
  const response: APIResponse = await requestRetrieve(request, token);

  expect(
    response.status(),
    `${ROUTES.retrieve} must answer 200 for an authenticated caller. A 401 ` +
      'means the token was not accepted; a 502 means Gateway could not reach ' +
      'DataServices, which is a failure the decomposition itself created and ' +
      'which an in-process call could not have had.',
  ).toBe(200);

  return collectRows(await readChunks(response));
}

/**
 * Reads a row's engine-assigned key, LOSSLESSLY.
 *
 * Routed through the fixture's converter rather than through a generic numeric
 * read, because the identity is declared 64-bit on the contract and a value
 * beyond the exactly-representable range has already been ROUNDED by the time a
 * JSON parser hands it over as a number. The converter refuses such a value
 * instead of returning a plausible wrong one — which matters here more than
 * anywhere else in this file, since a rounded key would silently address a
 * different row and the resulting failure would be reported as a concurrency
 * problem rather than as a corrupted identity.
 */
function readRowIdentity(row: unknown): number | undefined {
  const value: unknown = findColumnValue(row, 'id');

  if (value === undefined || isNullValue(value)) {
    return undefined;
  }

  const raw: unknown =
    readMember(value, RESPONSE_KEYS.valueInt64) ??
    readMember(value, RESPONSE_KEYS.valueDouble) ??
    readMember(value, RESPONSE_KEYS.valueText);

  if (raw === undefined || raw === null) {
    return undefined;
  }

  try {
    return toIdentity(raw, 'id');
  } catch {
    return undefined;
  }
}

/** Finds a row by its engine-assigned key — the only stable way to identify one. */
function findRowByIdentity(rows: readonly unknown[], identity: number): unknown {
  return rows.find((row: unknown) => readRowIdentity(row) === identity);
}

/**
 * Collects the `name` of every row, in retrieve order.
 *
 * Used for two things only: proving that a refused insert persisted nothing, and
 * checking RELATIVE ordering among rows already proved present. Never for a
 * count.
 */
function rowNames(rows: readonly unknown[]): readonly string[] {
  const names: string[] = [];

  for (const row of rows) {
    const name: string | undefined = readColumnText(row, 'name');

    if (name !== undefined) {
      names.push(name);
    }
  }

  return names;
}

/**
 * True when a return code means zero — the value `OK`, `SUCCESS` and `ALLOW` all
 * spell.
 *
 * Three spellings because the oracle declares three constants for zero, and the
 * canonical mapping emits the first of an alias group while accepting any of
 * them; the bare number is accepted too, since the same algebra travels as an
 * integer on a problem document and as an enumerator name on a projected
 * message.
 *
 * THE TEST IS FOR ZERO SPECIFICALLY, NOT FOR "SUCCESS". `PREVENT` is 1 and
 * satisfies the legacy success predicate, and `CANCELED` is -2 and is NEITHER
 * succeeded nor failed, so a two-way success test over this field would classify
 * a prevention as an ordinary success and a cancellation as a failure. Both are
 * preserved legacy behaviours and neither is what an applied update reports.
 */
function isZeroRetCode(value: unknown): boolean {
  if (typeof value === 'number') {
    return value === 0;
  }

  if (typeof value !== 'string') {
    return false;
  }

  return value === 'OK' || value === 'SUCCESS' || value === 'ALLOW' || value === '0';
}

/* ------------------------------------------------------------------------- *
 * The data this spec writes — deterministic, fixture-built, and never a clock
 *
 * Uniqueness comes from a stable test-scoped label rather than from a timestamp
 * or a random source, so two runs produce identical payloads and a recording
 * taken from one is comparable with a master. Rows are identified afterwards by
 * the key the ENGINE assigned, never by a label, so re-running against a volume
 * that already holds a previous run's row is expected rather than tolerated.
 * ------------------------------------------------------------------------- */

/**
 * The scope of the row this spec inserts and then updates.
 *
 * Distinct from {@link INVALID_SCOPE} on purpose, and the distinction is
 * load-bearing rather than tidy: the refusal test proves that nothing was
 * persisted by looking for its own label, and a shared scope would make it look
 * for a label the insert test had legitimately persisted a moment earlier — a
 * false failure on every run.
 */
const INSERT_SCOPE = 'dw-workflow-insert';

/** The scope of the row this spec deliberately gets REFUSED. Never persisted. */
const INVALID_SCOPE = 'dw-workflow-invalid';

/**
 * The row inserted through the public workflow.
 *
 * Built by the fixture, not by hand. Every field choice is a constraint being
 * honoured rather than a preference: `id` is absent because
 * `CompanyRowInput` omits it and the engine assigns it; `name` and `age` are
 * non-null because the DDL says `NOT NULL` on both; `address` is ten characters,
 * far inside the narrower of the two widths the oracles declare, so the
 * `CHAR(50)`-versus-`char(200)` disagreement is never the subject of a
 * happy-path assertion; `birth` goes through the fixture's formatter, so it is
 * `yyyy-mm-dd` text produced without constructing a date or consulting a locale;
 * and `salary` is a quarter-step value, which is exactly representable as a
 * double and therefore cannot be the accidental cause of a tolerance failure.
 *
 * `age` sits above the oldest oracle row, so under `sort="age A salary A "` this
 * row lands after every seeded one rather than interleaved with them.
 */
const INSERT_INPUT: CompanyRowInput = buildCompanyRow({
  name: deterministicLabel(INSERT_SCOPE, 0),
  age: 41,
  address: 'California',
  salary: 20000.25,
  birth: formatBirth(1991, 5, 11),
});

/**
 * The row whose insert must be REFUSED.
 *
 * Fixture-built like any other, and made invalid at the wire-encoding step by
 * sending the explicit null arm for `age` — a column the DDL declares `NOT NULL`.
 * Nulled at encoding rather than in the builder because `CompanyRowInput` types
 * `age` as a number, and that type is correct: a null age is not a row the
 * fixture should be able to describe, only one this spec should be able to send.
 */
const INVALID_INPUT: CompanyRowInput = buildCompanyRow({
  name: deterministicLabel(INVALID_SCOPE, 0),
  age: 42,
  address: 'Texas',
  salary: 20000.5,
  birth: formatBirth(1980, 5, 11),
});

/** The column the refused insert nulls. `AGE INT NOT NULL` is the invariant under test. */
const NOT_NULL_VIOLATION_COLUMNS: readonly CompanyColumnName[] = ['age'];

/**
 * The scope of the row the UPDATE test arranges for itself.
 *
 * Distinct from {@link INSERT_SCOPE} for the same load-bearing reason the refusal
 * scope is distinct: the update test mutates its row, and mutating the row the
 * insert test asserted on would couple two tests that are now deliberately
 * independent. Neither test finds a row by label — the engine-assigned key is the
 * only identifier either uses — but two labels keep a store read by hand legible.
 */
const UPDATE_SCOPE = 'dw-workflow-update';

/**
 * The baseline row the update test arranges, inserts and then updates.
 *
 * Identical to {@link INSERT_INPUT} in every constrained field — the same
 * `NOT NULL` obligations, the same ten-character address well inside the narrower
 * declared width, the same fixture-formatted birth text and the same
 * exactly-representable quarter-step salary — and different only in the label,
 * which is what makes it a different row rather than a different kind of row.
 */
const UPDATE_BASELINE_INPUT: CompanyRowInput = buildCompanyRow({
  name: deterministicLabel(UPDATE_SCOPE, 0),
  age: 41,
  address: 'California',
  salary: 20000.25,
  birth: formatBirth(1991, 5, 11),
});

/** The `address` the update writes. A label, and 32 characters — well inside every bound. */
const UPDATED_ADDRESS: string = deterministicLabel('dw-workflow-address', 0);

/**
 * The `salary` the update writes.
 *
 * A quarter-step away from the inserted value, so the difference is fifty times
 * the comparison tolerance: the change is unambiguously detectable, and the
 * assertion cannot pass merely because the two values are close.
 */
const UPDATED_SALARY = 20000.5;

/**
 * The textual shape of a `birth` value, DERIVED from the fixture's format
 * description rather than restated.
 *
 * Each letter of the mask stands for one digit, so replacing the letters yields
 * the pattern and the two can never disagree. Writing the pattern out instead
 * would create a second description of the same format.
 */
const BIRTH_TEXT_PATTERN = new RegExp(`^${BIRTH_FORMAT.replace(/[a-z]/g, '\\d')}$`);

/* ------------------------------------------------------------------------- *
 * ARRANGEMENT, NOT CARRIED STATE
 *
 * This spec once held two module-scope `let` bindings — the inserted row and its
 * verbatim column values — written by the insert test and read by the update
 * test, with the group configured `mode: 'serial'` to guarantee the order. That
 * arrangement had a cost the ordering hid: the update test could not run on its
 * own at all. `npx playwright test -g "an update carrying original values"`
 * reported a contrived failure about a missing binding rather than exercising
 * the contract, and after any insert-test failure the update test was either
 * skipped or meaningless. A test that cannot be run by itself cannot be used to
 * diagnose the thing it tests.
 *
 * So each test now ARRANGES ITS OWN ROW and keeps every value local to itself.
 * {@link arrangeStoredRow} performs the insert as a precondition — not as an
 * assertion, which is the insert test's job and its alone — and hands back the
 * row as the retrieval answered it. Two consequences worth stating:
 *
 *   * THE UPDATE TEST IS NOW SELF-CONTAINED and independently runnable, and its
 *     failure means what it says.
 *   * THE TESTS WRITE DIFFERENT ROWS. The arranged baseline carries its own
 *     scope label, so the row the update test mutates is never the row the
 *     insert test asserted on. Nothing is found by label in either case — the
 *     engine-assigned key is the only identifier used — but two distinct labels
 *     make a store inspected by hand legible rather than ambiguous.
 *
 * The extra insert per test is a write to a store that is never reseeded, which
 * is exactly what this suite already does by design; rows accumulate across runs
 * and identity is what distinguishes them.
 * ------------------------------------------------------------------------- */

/** A row that exists in storage, together with the wire values it came back as. */
interface StoredRow {
  /** The row as the RETRIEVAL answered it, carrying the key the engine assigned. */
  readonly row: CompanyRow;

  /**
   * The retrieved row's own column values, verbatim, for reuse as an update's
   * originals. See {@link encodeUpdateRow} for why re-encoding them would be worse.
   */
  readonly verbatimColumns: readonly WireColumnValue[];
}

/**
 * Insert a row and read it back, as ARRANGEMENT for a test whose subject is
 * something else.
 *
 * Deliberately thin on assertions, and deliberately loud when it fails. Every
 * check here exists so that an arrangement fault is reported AS an arrangement
 * fault — the caller's subject has not been reached yet, and a bare type error
 * or a confusing expectation failure would send a reader to the wrong place. The
 * exhaustive insert assertions — the identity round-trip, the counts, the
 * per-column fidelity and the salary tolerance — belong to the insert test and
 * are NOT duplicated here; duplicating them would make an insert regression fail
 * every test in the file with the same message.
 *
 * @param request the Playwright request context for this test
 * @param token a service token this test already obtained
 * @param input the row to insert; give each caller its own so two tests never
 *              mutate one row
 * @returns the stored row and its verbatim wire columns
 * @throws Error when the insert does not answer 200, does not report an
 *         identity, or the row cannot be read back by that identity
 */
async function arrangeStoredRow(
  request: APIRequestContext,
  token: ServiceToken,
  input: CompanyRowInput,
): Promise<StoredRow> {
  const response: APIResponse = await requestUpdate(request, token, [
    encodeInsertRow(input),
  ]);

  if (response.status() !== 200) {
    throw new Error(
      `ARRANGEMENT FAILED: ${ROUTES.update} answered ${response.status()} for ` +
        'the baseline insert, so the row this test needs does not exist and its ' +
        'own subject was never reached. Read the insert test — it asserts this ' +
        'path directly and will say what is wrong with it.',
    );
  }

  const body: unknown = await response.json();

  const identity: number | undefined = readInt64(
    (readArray(
      (readArray(body, RESPONSE_KEYS.identity) ?? [])[0],
      RESPONSE_KEYS.identityPrimaryValues,
    ) ?? [])[0],
    RESPONSE_KEYS.wrappedValue,
  );

  if (identity === undefined) {
    throw new Error(
      'ARRANGEMENT FAILED: the baseline insert reported no engine-assigned ' +
        'identity, so there is no key to address the row by. The insert test ' +
        'asserts the identity round-trip in full; read its result.',
    );
  }

  const stored: unknown = findRowByIdentity(
    await retrieveRows(request, token),
    Number(identity),
  );

  if (stored === undefined) {
    throw new Error(
      `ARRANGEMENT FAILED: the baseline row reported key ${identity} but is not ` +
        'retrievable by it, so the insert reported success without the write ' +
        'reaching storage. The insert test asserts exactly this and is the one ' +
        'to read.',
    );
  }

  // Built from what the RETRIEVAL answered rather than from what was sent,
  // because an update's WHERE clause has to match what is STORED. The fallbacks
  // are the sent values, which is correct for the two NOT NULL columns and
  // unreachable for them in practice; the three nullable ones fall back to null.
  return {
    row: {
      id: Number(identity),
      name: readColumnText(stored, 'name') ?? input.name,
      age: readColumnNumber(stored, 'age') ?? input.age,
      address: readColumnText(stored, 'address') ?? null,
      salary: readColumnNumber(stored, 'salary') ?? null,
      birth: readColumnText(stored, 'birth') ?? null,
    },
    verbatimColumns: verbatimColumnsFrom(stored),
  };
}

test.describe('DataWindow retrieve / validate / update workflow (C-03 over C-09)', () => {
  // THE TOKEN-ISSUANCE PRECONDITION, and it is the FIRST thing this group does.
  //
  // `POST /v1/tokens` on Security is authenticated by a client certificate and by
  // nothing else, on every topology including the local bring-up, so with no
  // identity provisioned every authenticated assertion below is unrunnable. The
  // hook fails this group's SETUP in a full acceptance run rather than letting
  // fifteen token calls fail one at a time with transport errors that never say
  // why; a run that has explicitly declared itself partial passes straight
  // through here and its token-dependent tests skip themselves instead, with the
  // reason stated. The whole policy lives in `fixtures/token-issuance.ts` — this
  // line only applies it.
  test.beforeAll(assertTokenIssuanceProvisioned);

  // ---------------------------------------------------------------------------
  // MISSING-STACK BEHAVIOUR, MADE UNIFORM AND EXPLICIT ACROSS ALL SIX SPECS
  //
  // This suite drives real HTTP against a running four-service stack, so three
  // outcomes have to stay distinguishable: the contract holds (pass), the
  // contract is violated (fail), and the stack is not up at all (neither).
  // Without an explicit third state the last one arrives as a wall of transport
  // errors that read exactly like the second - a false accusation against
  // services that are merely absent - and the tempting remedy is to soften the
  // assertions until they tolerate an unreachable host, which converts a real
  // violation into a silent pass and destroys the suite's whole value.
  //
  // The probe is memoised per worker, so this costs one request per worker and
  // not one per test.
  //
  // ⚠ AN ABSENT STACK NOW FAILS A FULL ACCEPTANCE RUN RATHER THAN SKIPPING IT.
  // This hook used to probe and then skip, which left the one state a
  // misconfigured pipeline is in - nothing running - as the state that exited
  // zero. `requireLiveStack` fails instead unless the run has explicitly
  // acknowledged an absent stack with E2E_ALLOW_ABSENT_STACK, in which case it
  // skips with a stated reason and the run is labelled api-partial-no-stack in
  // every reported line so its result cannot be read as an acceptance result.
  //
  // THE DECISION LIVES IN ONE PLACE FOR ALL SIX SPECS. It was written out six
  // times, once per spec, so the six could disagree about what an absent stack
  // means - which mattered little while the answer was a skip and matters a great
  // deal now that it gates acceptance. Tests tagged `@no-stack` are still exempt,
  // and the tag is still why this is a tag rather than a title match; that
  // reasoning now lives with the function.
  // ---------------------------------------------------------------------------
  test.beforeEach(async ({}, testInfo) => {
    await requireLiveStack(testInfo);
  });

  // NOT SERIAL, AND THAT IS THE POINT. This group was configured
  // `mode: 'serial'` while two of its five tests shared module-scope state, and
  // the ordering guarantee was what made the sharing work. It also made the
  // update test impossible to run on its own and meaningless after any earlier
  // failure — the cost of a diagnostic convenience being paid in independence.
  //
  // Each test now arranges everything it needs (see `arrangeStoredRow`), so there
  // is no order to guarantee and no reason to declare one. What serial mode
  // bought — no cascade of derived failures from one cause — is bought instead by
  // the arrangement helper, which fails with `ARRANGEMENT FAILED` and names the
  // test that owns the fault.
  //
  // Execution order and isolation are unaffected by the removal: `workers: 1` and
  // `fullyParallel: false` are global, so these tests still run one at a time in
  // declaration order in one worker. `retries` is 0 globally and must stay so — a
  // retry on a mutating workflow would re-issue a write and could convert a real
  // failure into a pass.

  test('the COMPANY column contract carries updatewhere=1 with all six columns marked', { tag: '@no-stack' }, () => {
    // NO NETWORK. This step asserts the precondition the whole concurrency
    // contract rests on, straight from the fixture, so it runs and means
    // something with the stack down.
    expect(
      COMPANY_COLUMNS.map((column: CompanyColumn) => column.name),
      'the schema must be the six columns of the only updatable DataWindow in ' +
        'the repository, in DDL order. The order is not cosmetic: it is the ' +
        'order an unqualified projection returns and the order the one-based ' +
        'column ordinals follow, so a reordering silently renumbers every ' +
        'column on the wire.',
    ).toEqual(['id', 'name', 'age', 'address', 'salary', 'birth']);

    for (const column of COMPANY_COLUMNS) {
      expect(
        column.updatable,
        `${column.name} must be updatable: the DataWindow marks all six ` +
          'columns update=yes, and a column that were not updatable would drop ' +
          'out of the generated statement altogether.',
      ).toBe(true);

      expect(
        column.updateWhereClause,
        `${column.name} must participate in the where clause. updatewhere=1 is ` +
          'the "key and updateable columns" mode, so the generated WHERE ' +
          "carries the key column PLUS the ORIGINAL VALUE of every marked " +
          'column — which is why an update payload has to transmit two values ' +
          'per column and not one.',
      ).toBe(true);

      // Derived, not typed out: this is the guard on the helper that turns a
      // column name into the ordinal every wire payload below carries.
      expect(
        columnOrdinal(column.name),
        `${column.name} must resolve to its declared one-based ordinal`,
      ).toBe(COMPANY_COLUMNS.indexOf(column) + 1);

      // The legacy identity resolver prefix-matches the lower-cased update-table
      // name against each column's database name, so a qualified name is a shape
      // the model must be able to express — and all six of THESE are unqualified.
      expect(
        column.dbName.length,
        `${column.name} must declare a database name; the identity-column ` +
          'resolver reads it',
      ).toBeGreaterThan(0);

      expect(
        column.dbName.toLowerCase().startsWith(`${COMPANY_TABLE_NAME.toLowerCase()}.`),
        `${column.name} is declared with an unqualified database name in the ` +
          `oracle, so it must not arrive here qualified with ${COMPANY_TABLE_NAME}`,
      ).toBe(false);
    }

    const keyColumns: readonly string[] = COMPANY_COLUMNS.filter(
      (column: CompanyColumn) => column.isKey,
    ).map((column: CompanyColumn) => column.name);

    const identityColumns: readonly string[] = COMPANY_COLUMNS.filter(
      (column: CompanyColumn) => column.isIdentity,
    ).map((column: CompanyColumn) => column.name);

    expect(
      keyColumns,
      'id must be the only key column. The key is what the WHERE clause finds ' +
        'the row by before the original values decide whether to accept it.',
    ).toEqual(['id']);

    expect(
      identityColumns,
      'id must be the only identity column. It is the auto-increment column, ' +
        'so an insert never supplies it and the assigned value round-trips back.',
    ).toEqual(['id']);

    expect(
      MARKED_COLUMNS,
      'all six columns must be marked for the concurrency comparison; that is ' +
        'what makes the check span six original values rather than one',
    ).toEqual(['id', 'name', 'age', 'address', 'salary', 'birth']);

    // The three named preserved mismatches. Each is asserted to be RECORDED, not
    // to be absent: they are legacy defects to reproduce, never errors to fix.
    //
    // Deliberately asserted PER COLUMN and never as a count. The fixture records
    // a FOURTH divergence on `name` — char(100) against an unbounded TEXT — which
    // is real, is documented there as the unnamed fourth, and is preserved on the
    // .NET side too. A "there are exactly three" assertion would therefore fail
    // against a fixture that is right, which is the worst kind of test.
    for (const name of ['address', 'salary', 'birth'] as const) {
      const column: CompanyColumn | undefined = COMPANY_COLUMNS.find(
        (candidate: CompanyColumn) => candidate.name === name,
      );

      expect(column, `${name} must be described by the schema fixture`).toBeDefined();

      expect(
        (column?.mismatchNote ?? '').length,
        `${name} must still carry its mismatch note. The note is the ` +
          'machine-readable form of the preserved-defect inventory, so losing ' +
          'it would quietly turn a documented divergence into an undocumented one.',
      ).toBeGreaterThan(0);

      expect(
        column?.dwType,
        `${name} must still declare different types on its two oracles — that ` +
          'disagreement IS the preserved defect, and reconciling the two ' +
          'declarations would diverge from the behavioural oracle on the first ' +
          'value that exercised the difference',
      ).not.toBe(column?.ddlType);
    }

    expect(
      BIRTH_FORMAT,
      'birth must be described by the edit mask the DataWindow declares. The ' +
        'column is TEXT in storage, so the format is the whole of its ordering ' +
        'and comparison semantics rather than a display preference.',
    ).toBe('yyyy-mm-dd');
  });

  test('retrieve answers the DataWindow carrier as an ordered chunk sequence', async ({
    request,
  }) => {
    // Acquired here, used here, discarded here. Never cached in module scope and
    // never promoted to a config-level header: the suite's standing proof that
    // every boundary is closed depends on an unauthenticated request still being
    // possible to write.
    const token: ServiceToken = await requireServiceToken(request);

    const response: APIResponse = await requestRetrieve(request, token);

    expect(
      response.status(),
      `${ROUTES.retrieve} must answer 200 for an authenticated caller. A 401 ` +
        'means the token minted by the sole issuer was not accepted; a 502 ' +
        'means Gateway could not reach DataServices — a failure mode the ' +
        'decomposition itself created, because an in-process call cannot fail ' +
        'in transit and a network call can.',
    ).toBe(200);

    // THE BARE ORDERED COLLECTION, REQUIRED RATHER THAN PREFERRED. The contract
    // publishes this body as `RetrieveResult`, `type: array`, because the protocol
    // definition has no envelope message for a stream — wrapping one would add a
    // member the gRPC contract does not have. The envelope spelling used to be
    // "resolved as a tolerance, never as an expectation", which in practice meant
    // an invented envelope was accepted; `readChunks` now refuses anything that is
    // not an array and asserts every chunk against `RetrieveChunk` as it passes.
    const sequence: readonly unknown[] = await readChunks(response);

    expect(
      sequence.length,
      'the retrieval must answer a collection of chunks. A single flat rowset ' +
        'would mean the chunking contract had been flattened away, and a ' +
        'consumer could then no longer tell a complete retrieval from a ' +
        'truncated one.',
    ).toBeGreaterThan(0);

    // The chunking contract, asserted as SELF-CONSISTENCY rather than as any
    // absolute quantity. Every member below is marked required by the published
    // schema, and none of these assertions says anything about how many rows the
    // shared volume happens to hold — which is precisely why they are safe here.
    let running = 0;

    for (const [index, chunk] of sequence.entries()) {
      const rows: readonly unknown[] | undefined = readArray(chunk, RESPONSE_KEYS.rows);

      expect(rows, `chunk ${String(index + 1)} must carry a rows array`).toBeDefined();

      const carried: readonly unknown[] = rows ?? [];
      running += carried.length;

      expect(
        readInt64(chunk, RESPONSE_KEYS.rowCount),
        `chunk ${String(index + 1)} must report its own row count. The count is ` +
          'redundant with the array length by construction and is carried anyway, ' +
          'because the legacy progressive-delivery event carries it as its own ' +
          'argument and a handler is entitled to read it without walking the carrier.',
      ).toBe(carried.length);

      expect(
        readInt64(chunk, RESPONSE_KEYS.chunkIndex),
        `chunk ${String(index + 1)} must carry its ONE-BASED position, so a ` +
          'consumer can detect a gap rather than assume there was none',
      ).toBe(index + 1);

      expect(
        readInt64(chunk, RESPONSE_KEYS.cumulativeRowCount),
        `chunk ${String(index + 1)} must carry the running total across itself ` +
          'and every chunk before it',
      ).toBe(running);

      // Compared with `=== true` so that a boundary omitting the default `false`
      // is treated as "not final" rather than as a missing member. The positive
      // case is the one that carries information: a stream ending without it was
      // truncated, and a collection merely ending does not say so.
      expect(
        readMember(chunk, RESPONSE_KEYS.final) === true,
        index === sequence.length - 1
          ? 'the last chunk must be marked final; a retrieval that ends without ' +
            'the marker was truncated, and only the marker distinguishes the two'
          : `chunk ${String(index + 1)} must not be marked final — a final ` +
            'marker before the end would tell a consumer to stop reading early',
      ).toBe(index === sequence.length - 1);
    }

    const rows: readonly unknown[] = collectRows(sequence);

    // DEGRADES RATHER THAN FAILS ON AN EMPTY VOLUME. An empty collection is a
    // valid retrieval — the contract says so — and the shared volume is never
    // reseeded, so "no rows yet" is a legitimate state rather than a defect. The
    // column-identity assertion is therefore conditional here and is re-made
    // unconditionally against the row the next step inserts, where presence is
    // guaranteed by construction.
    if (rows.length > 0) {
      const firstRow: unknown = rows[0];

      for (const column of COMPANY_COLUMNS) {
        expect(
          findColumnValue(firstRow, column.name),
          `the projection must expose ${column.name}. A retrieval that dropped ` +
            'a column would leave a caller unable to construct an update at all, ' +
            'because the concurrency check compares every marked column.',
        ).toBeDefined();
      }

      // THE CARRIER IS A DATAWINDOW, NOT A ROWSET, and these three members are
      // how that shows on the wire. The legacy result carrier derives from a
      // datastore, so it carries buffers and per-item statuses; a naive flat
      // rowset would silently discard exactly the state the update contract
      // depends on. Their PRESENCE is asserted; no buffer or status vocabulary is
      // invented here.
      expect(
        readMember(firstRow, RESPONSE_KEYS.buffer),
        'each row must name the buffer it came from. Silence would read as the ' +
          'primary buffer, which is a specific buffer a consumer is entitled to ' +
          "believe — and the filter buffer's row order is inverted relative to " +
          'the source, so a mis-tagged row would be counted in the wrong direction.',
      ).toBeDefined();

      expect(
        readMember(firstRow, RESPONSE_KEYS.itemStatus),
        "each row must carry its own item status, read the legacy way as the " +
          'row itself rather than as a column',
      ).toBeDefined();

      const retrievedOriginals: readonly unknown[] | undefined = readArray(
        firstRow,
        RESPONSE_KEYS.originalValues,
      );

      expect(
        retrievedOriginals,
        'each row must carry an originals collection. It is a REQUIRED member of ' +
          'DataWindowRow in both directions, because it is the baseline the ' +
          "updatewhere=1 predicate compares against and it is the shape an update " +
          'is built in.',
      ).toBeDefined();

      // ONE BASELINE PER COLUMN ON A RETRIEVED ROW, and it must agree with the
      // current value: a retrieval baselines every row, so original and current
      // are the same value stated twice. An earlier revision let a producer omit a
      // column whose original equalled its current one, which meant a freshly
      // retrieved row carried NO baseline at all and a caller had to reconstruct
      // the concurrency predicate from an absence — and the other legal reading of
      // that absence, "no baseline exists", drops the predicate and silently
      // overwrites.
      for (const column of COMPANY_COLUMNS) {
        expect(
          findColumnEntryIn(retrievedOriginals, column.name),
          `a retrieved row must carry an original for ${column.name}. All six ` +
            'columns are marked updatewhereclause=yes, so all six originals go ' +
            'into the generated WHERE clause.',
        ).toBeDefined();

        expect(
          textOfValue(findColumnValueIn(retrievedOriginals, column.name)),
          `${column.name}'s original must equal its current value immediately ` +
            'after retrieval, because the retrieval is what established the baseline',
        ).toBe(textOfValue(findColumnValue(firstRow, column.name)));
      }
    }

    // RELATIVE ordering only, and only over rows already proved present. Never a
    // length, never a membership claim about the seed rows, never the page-scoped
    // footer total — all three depend on the shared row set, which is not a fixed
    // quantity.
    //
    // FIRST OCCURRENCE, NOT EQUALITY, and the reason is specific: the oracle
    // inserts four named rows and then TEN MORE IN A LOOP that all carry the same
    // name as one of them, so a name can legitimately appear eleven times. An
    // equality assertion against the expected order would fail on the duplicates
    // while the ordering was in fact correct.
    const names: readonly string[] = rowNames(rows);
    const presentInExpectedOrder: readonly string[] = EXPECTED_SEED_RETRIEVE_ORDER.filter(
      (name: string) => names.includes(name),
    );

    if (presentInExpectedOrder.length > 1) {
      const positions: readonly number[] = presentInExpectedOrder.map((name: string) =>
        names.indexOf(name),
      );

      const ascending: readonly number[] = [...positions].sort(
        (left: number, right: number) => left - right,
      );

      expect(
        positions,
        'the rows the fixture publishes an expected order for must come back in ' +
          'that relative order. The order follows from sort="age A salary A " on ' +
          'the DataWindow, and the tie-break between the two rows that share an ' +
          'age is exactly where an independent re-derivation would go wrong — ' +
          'which is why the expected order is consumed from the fixture rather ' +
          'than recomputed here.',
      ).toEqual(ascending);
    }
  });

  test('an insert through the public workflow round-trips its engine-assigned identity', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    const response: APIResponse = await requestUpdate(request, token, [
      encodeInsertRow(INSERT_INPUT),
    ]);

    expect(
      response.status(),
      `${ROUTES.update} must answer 200 for a well-formed insert. A 409 here ` +
        'would be an optimistic-concurrency report against a row that has no ' +
        'prior state to conflict with, which would mean the originals were being ' +
        'compared for an insert; a 400 means the payload was refused by binding ' +
        'or by validation.',
    ).toBe(200);

    const body: unknown = await response.json();

    const retCode: unknown = readMember(body, RESPONSE_KEYS.retCode);

    if (retCode !== undefined) {
      expect(
        isZeroRetCode(retCode),
        'an applied update must report the zero return code. Asserted as ZERO ' +
          'specifically rather than through a success test, because a prevention ' +
          'is 1 and reads as a success under the legacy predicate while a ' +
          'cancellation is neither succeeded nor failed — both are preserved ' +
          'behaviours and neither is what an applied update reports.',
      ).toBe(true);
    }

    // THE IDENTITY ROUND-TRIP. The engine assigns the key, so the caller learns
    // it from the response rather than choosing it — which is the whole reason
    // the insert payload omits the column.
    const identityEntries: readonly unknown[] | undefined = readArray(
      body,
      RESPONSE_KEYS.identity,
    );

    expect(
      identityEntries,
      'an insert must report the identity data. Without it a caller cannot ' +
        'address the row it just created, and every later operation on that row ' +
        'would have to guess a key.',
    ).toBeDefined();

    const entries: readonly unknown[] = identityEntries ?? [];

    expect(
      entries.length,
      'the identity report must carry at least one entry for an inserted row',
    ).toBeGreaterThan(0);

    const entry: unknown = entries[0];

    expect(
      readInt64(entry, RESPONSE_KEYS.identityColumnId),
      'the identity column reported must be the one the DataWindow declares as ' +
        'the identity column, addressed by its one-based ordinal',
    ).toBe(columnOrdinal('id'));

    // The PRIMARY buffer's values. The response carries a second array collected
    // from the filter buffer backwards, because that buffer's order is inverted
    // relative to the source; a row inserted into the primary buffer lands in the
    // first array, and the second is not read here.
    const primaryValues: readonly unknown[] | undefined = readArray(
      entry,
      RESPONSE_KEYS.identityPrimaryValues,
    );

    expect(
      primaryValues,
      'the identity entry must carry the values collected from the primary buffer',
    ).toBeDefined();

    const identity: number | undefined = readInt64(
      (primaryValues ?? [])[0],
      RESPONSE_KEYS.wrappedValue,
    );

    expect(
      identity,
      'the assigned key must come back as a readable 64-bit integer. It is ' +
        'declared int64 on the contract and the canonical mapping emits such a ' +
        'value as a decimal string, so a value that arrives as a number beyond ' +
        'the exactly-representable range has already been rounded and is refused ' +
        'rather than accepted as plausible.',
    ).toBeDefined();

    expect(
      Number(identity),
      'the assigned key must be a positive integer — an auto-increment rowid ' +
        'starts at 1, so a zero or negative value means no key was assigned',
    ).toBeGreaterThan(0);

    // REQUIRED, NOT CONDITIONAL — and the distinction is a contract one rather
    // than a strictness preference. The canonical protobuf JSON mapping omits a
    // field only when it holds its type's DEFAULT, so for an int64 the single
    // omission this projection may legitimately produce is zero. This request
    // submitted exactly one row for insert, so the only correct value is 1, and 1
    // is not omissible. An absent member therefore means one of two things and
    // both are failures: the write reported success while inserting nothing, or
    // the projection dropped a count a caller needs. Guarding the assertion
    // behind a presence check let either pass silently.
    const inserted: number | undefined = readInt64(body, RESPONSE_KEYS.rowsInserted);

    expect(
      inserted,
      'the response must report the inserted-row count. One row was submitted, ' +
        'so the count is non-zero and cannot be a permissible protobuf-default ' +
        'omission: its absence means either that nothing was inserted despite ' +
        'the success status, or that the projection dropped a count the caller ' +
        'needs in order to know what was applied.',
    ).toBeDefined();

    expect(inserted, 'exactly one row was submitted for insert').toBe(1);

    // Re-retrieve, and verify BY IDENTITY. Never by position and never by label:
    // the volume is never reseeded, so a previous run's row carries the same
    // deterministic label and only the key distinguishes this one.
    const rows: readonly unknown[] = await retrieveRows(request, token);
    const created: unknown = findRowByIdentity(rows, Number(identity));

    expect(
      created,
      'the inserted row must be retrievable by the key the engine assigned. Its ' +
        'absence means the insert reported success without the write reaching ' +
        'storage, which is the most serious failure this workflow can detect.',
    ).toBeDefined();

    expect(readColumnText(created, 'name'), 'name must round-trip unchanged').toBe(
      INSERT_INPUT.name,
    );

    expect(readColumnNumber(created, 'age'), 'age must round-trip unchanged').toBe(
      INSERT_INPUT.age,
    );

    // Compared as sent. No length assertion at 50 or 200 is made here or anywhere
    // else: the width disagreement between the two oracles is a preserved defect,
    // not a validation rule, and SQLite constrains neither declaration anyway.
    expect(readColumnText(created, 'address'), 'address must round-trip unchanged').toBe(
      INSERT_INPUT.address,
    );

    // Compared as a STRING, because the column is TEXT in storage while the
    // DataWindow declares a date — preserved mismatch, so the text actually
    // written is the whole of the value's semantics.
    const birth: string | undefined = readColumnText(created, 'birth');

    expect(birth, 'birth must round-trip unchanged, as text').toBe(INSERT_INPUT.birth);

    expect(
      BIRTH_TEXT_PATTERN.test(birth ?? ''),
      'birth must come back in the layout the DataWindow edit mask declares; the ' +
        'pattern is derived from that same format description rather than restated',
    ).toBe(true);

    // WITHIN A TOLERANCE, NEVER EXACT. A two-place decimal stored in a REAL
    // column is not guaranteed to return an identical double — that is preserved
    // mismatch 3, and it is the reason the fixture publishes a tolerance at all.
    const salary: number | undefined = readColumnNumber(created, 'salary');

    expect(salary, 'salary must round-trip as a readable number').toBeDefined();

    expect(
      Math.abs(Number(salary) - Number(INSERT_INPUT.salary)),
      'salary must round-trip within the fixture tolerance. Compared with a ' +
        'tolerance rather than exactly because the DataWindow declares a ' +
        'two-place decimal over a floating-point column; the tolerance is half a ' +
        'cent, far below the precision the schema claims and far above the error ' +
        'the storage introduces.',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    // NOTHING IS CARRIED OUT OF THIS TEST. It once assigned the retrieved row and
    // its verbatim columns to two module-scope bindings for the update test to
    // read, which made that test unrunnable on its own. The update test now
    // arranges its own baseline through `arrangeStoredRow`, so this test ends
    // where its subject ends: the identity round-trip and the per-column fidelity
    // of one insert.
  });

  test('a NOT NULL violation is refused as a structured error and persists nothing', async ({
    request,
  }) => {
    // THE VALIDATE THIRD OF THE TRIPLE. The validation invariant that is actually
    // EVIDENCED in the repository is the DDL's NOT NULL on NAME and AGE — the one
    // constraint both oracles agree on — so that is what is exercised. Inventing
    // a richer validation surface would be inventing a requirement.
    const token: ServiceToken = await requireServiceToken(request);

    const response: APIResponse = await requestUpdate(request, token, [
      encodeInsertRow(INVALID_INPUT, NOT_NULL_VIOLATION_COLUMNS),
    ]);

    expect(
      response.ok(),
      'an insert nulling a NOT NULL column must not succeed. A 2xx here would ' +
        'mean the constraint the DDL declares is not being enforced anywhere in ' +
        'the chain, and the row would be either written or silently dropped — ' +
        'both worse than a refusal.',
    ).toBe(false);

    // 🔴 THE REFUSAL MUST BE ATTRIBUTED TO THE CALLER, WHICH IS A 4xx AND NOT A 5xx.
    // This used to be asserted only as ">= 400", and the chain answered 502 with a
    // body reading "SQLite Error <redacted>: '<redacted>'." — so a caller who
    // omitted a required field was told the database had failed, that the fault lay
    // behind the gateway, and nothing said which column. A row omitting a NOT NULL
    // column is the CALLER's payload being wrong, and the whole corrective action is
    // available to the caller, so the class of the status is part of the contract.
    //
    // STILL NO SPECIFIC NUMBER. The class is what carries the attribution; the exact
    // code inside it is not fixed by the plan of record, and naming one would be
    // inventing a requirement. The one status that IS fixed is the 409 an
    // optimistic-concurrency mismatch produces, and that belongs to the conflict
    // spec rather than to this file.
    expect(
      response.status(),
      'a payload the caller can correct must be refused as a CLIENT error. A 5xx ' +
        'attributes the fault to the service or to something behind it, which sends ' +
        'the caller looking for an outage instead of at its own request',
    ).toBeGreaterThanOrEqual(400);

    expect(
      response.status(),
      'a payload the caller can correct must not be reported as a server or ' +
        'upstream failure',
    ).toBeLessThan(500);

    // STRUCTURED, NOT A DIALOG AND NOT A PAGE. This is the migration of the
    // legacy MessageBox surface into a machine-readable error result: the text,
    // the category, the substitution arguments and the severity are preserved and
    // only the DELIVERY CHANNEL changed. A caller must be able to branch on the
    // body without parsing prose out of markup.
    // ASSERTED EXACTLY, AND AS THE PROBLEM SUBTYPE. This was `toContain('json')`
    // plus `not.toContain('text/html')`, which a plain `application/json` body
    // satisfied as readily as a problem document — and the two are not
    // interchangeable: every refusal on this boundary declares
    // `application/problem+json` so that ONE client-side error handler serves them
    // all without first parsing a body to discover whether it is one.
    assertMediaType(
      response.headers()['content-type'],
      PROBLEM_JSON_MEDIA_TYPE,
      `${ROUTES.update} refusal`,
    );

    const problem: unknown = await response.json();

    expect(
      asRecord(problem),
      'the refusal body must be a JSON object. An array or a bare string could ' +
        'not carry the members a consumer branches on.',
    ).toBeDefined();

    const declaredMembers: readonly string[] = RESPONSE_KEYS.problemMembers.filter(
      (member: string) => readMember(problem, [member]) !== undefined,
    );

    expect(
      declaredMembers.length,
      'the refusal must carry at least one of the standard problem members, so ' +
        'that one error handler serves every failure on this boundary',
    ).toBeGreaterThan(0);

    const declaredStatus: number | undefined = readInt64(problem, RESPONSE_KEYS.problemStatus);

    if (declaredStatus !== undefined) {
      expect(
        declaredStatus,
        'the status repeated in the body must agree with the transport status; ' +
          'the repetition exists so that a logged body is self-describing, and a ' +
          'disagreement would make the record misleading rather than redundant',
      ).toBe(response.status());
    }

    // The legacy return code the problem document carries is read for its TYPE
    // and not for its value. The value is deliberately unasserted: the plan of
    // record fixes no particular code for this refusal, and the tri-state algebra
    // means a two-way test over the field would misclassify both a prevention and
    // a cancellation. NOTHING FROM THIS BODY IS RENDERED INTO A MESSAGE.
    const problemRetCode: unknown = readMember(problem, RESPONSE_KEYS.retCode);

    if (problemRetCode !== undefined) {
      expect(
        typeof problemRetCode,
        'the legacy return code is an integer extension member on the problem ' +
          'document, so a consumer can identify the specific legacy validation ' +
          'rather than only the HTTP class',
      ).toBe('number');
    }

    // 🔴 THE REFUSAL MUST NAME THE OFFENDING COLUMN.
    // The provider composes its diagnostic as `SQLite Error 19: '<message>'.`, and
    // the service's statement-masking scan read that wrapper as a numeric literal
    // beside a quoted string and masked BOTH — so every constraint refusal reached a
    // caller as "SQLite Error <redacted>: '<redacted>'.", a sentence that says a
    // database error happened and refuses to say what would fix it. A caller cannot
    // correct a payload it is not told is wrong.
    //
    // ASSERTED OVER THE WHOLE SERIALIZED BODY rather than one member, because which
    // member carries the driver's text is an implementation detail of the relay and
    // the requirement is only that the identity reaches the caller at all.
    const refusalBody: string = JSON.stringify(problem);

    expect(
      refusalBody.toUpperCase(),
      'the refusal must name the column that failed. Masking the whole diagnostic ' +
        'leaves a caller with nothing to act on, and the column name is schema ' +
        'metadata rather than row data — it is not the thing masking exists to hide',
    ).toContain(NOT_NULL_VIOLATION_COLUMNS[0]!.toUpperCase());

    // AND THE VALUES THE CALLER SENT MUST NOT BE ECHOED BACK IN THE DIAGNOSTIC.
    // The narrowing preserves the provider's ENVELOPE only; anything the provider
    // quoted inside its own message still goes through the identical scan, so this is
    // what keeps "name the column" from having widened into "echo the statement".
    expect(
      refusalBody,
      'a refusal must not carry the generated statement or the literal values it ' +
        'interpolated; the failing column identity is the whole of what a caller needs',
    ).not.toContain(INVALID_INPUT.address);

    // NOTHING WAS PERSISTED. Proved by the label, which is safe here for one
    // reason only: the refused row uses a DIFFERENT scope from the inserted one,
    // so this assertion cannot be tripped by the row the previous step
    // legitimately created.
    const rows: readonly unknown[] = await retrieveRows(request, token);

    expect(
      rowNames(rows),
      'a refused insert must leave the table exactly as it was. The legacy ' +
        'sequence rolls back and surfaces the error WITHOUT resetting, so a ' +
        'partially applied write is not a state this workflow may reach.',
    ).not.toContain(INVALID_INPUT.name);
  });

  test('an update carrying original values for all six marked columns applies', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    // ARRANGED HERE, BY THIS TEST, rather than inherited from the insert test.
    // That is what makes this test independently runnable and its failure
    // attributable: a fault in the arrangement throws with `ARRANGEMENT FAILED`
    // and points at the insert test, while everything below this line is about
    // the update contract and nothing else.
    const arranged: StoredRow = await arrangeStoredRow(
      request,
      token,
      UPDATE_BASELINE_INPUT,
    );
    const baseline: CompanyRow = arranged.row;

    // asUpdate produces the {current, original} PAIR the contract needs: the
    // original half is the row exactly as retrieved, and it is what the generated
    // WHERE clause is built from. asStaleUpdate — the sibling that deliberately
    // falsifies the originals to provoke the 409 — belongs to the conflict spec
    // and is deliberately not used here.
    const update: CompanyRowUpdate = asUpdate(baseline, {
      address: UPDATED_ADDRESS,
      salary: UPDATED_SALARY,
    });

    expect(
      update.original.address,
      'the original half must still hold the value as retrieved; if a change ' +
        'leaked into it, the WHERE clause would be built from the new value and ' +
        'would match nothing',
    ).toBe(baseline.address);

    const response: APIResponse = await requestUpdate(request, token, [
      encodeUpdateRow(update, arranged.verbatimColumns),
    ]);

    expect(
      response.status(),
      `${ROUTES.update} must answer 200 for an update whose originals match ` +
        'what is stored. A 409 here would report an optimistic-concurrency ' +
        'mismatch on a row nothing else has touched — under a single worker with ' +
        'no parallelism, that points at the originals in the payload rather than ' +
        'at a genuine concurrent write.',
    ).toBe(200);

    const body: unknown = await response.json();

    const retCode: unknown = readMember(body, RESPONSE_KEYS.retCode);

    if (retCode !== undefined) {
      expect(
        isZeroRetCode(retCode),
        'an applied update must report the zero return code. The legacy is ' +
          'defensive about exactly this: it REWRITES a claimed success into a ' +
          'failure when the transaction reports an error, so a success here has ' +
          'to be the transaction\u2019s answer and not merely the update call\u2019s.',
      ).toBe(true);
    }

    // REQUIRED, NOT CONDITIONAL, for the reason recorded on the inserted-row
    // count above: one row was submitted, so the only correct value is 1, and the
    // canonical mapping omits an int64 only when it is zero. An absent member
    // means the update reported success while matching no row — which is exactly
    // the stale-original outcome this workflow must NOT produce, and the one a
    // conditional guard would have hidden.
    const updatedCount: number | undefined = readInt64(body, RESPONSE_KEYS.rowsUpdated);

    expect(
      updatedCount,
      'the response must report the updated-row count. One row was submitted, so ' +
        'the count is non-zero and cannot be a permissible protobuf-default ' +
        'omission: its absence means the update matched no row while still ' +
        'reporting success.',
    ).toBeDefined();

    expect(updatedCount, 'exactly one row was submitted for update').toBe(1);

    // Re-retrieve and confirm, which is the HTTP shape of the legacy sequence:
    // update, then commit, then reset only once the commit succeeded. A read-back
    // is what distinguishes an update that was applied from one that was merely
    // accepted.
    const rows: readonly unknown[] = await retrieveRows(request, token);
    const stored: unknown = findRowByIdentity(rows, baseline.id);

    expect(
      stored,
      'the updated row must still be retrievable by its key. Its disappearance ' +
        'would mean the update was executed as a delete-plus-insert against a ' +
        'new key, which is the documented consequence of a KEY change and not of ' +
        'this one — nothing here changes the key.',
    ).toBeDefined();

    expect(
      readColumnText(stored, 'address'),
      'address must now hold the new value; the update changed it',
    ).toBe(UPDATED_ADDRESS);

    const salary: number | undefined = readColumnNumber(stored, 'salary');

    expect(salary, 'salary must be readable after the update').toBeDefined();

    expect(
      Math.abs(Number(salary) - UPDATED_SALARY),
      'salary must now hold the new value, compared within the fixture ' +
        'tolerance for the same reason as on insert: a two-place decimal in a ' +
        'REAL column is not guaranteed to return an identical double',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    // The columns the update did NOT change must be untouched. An update that
    // rewrote an unmentioned column would be a silent overwrite, which is exactly
    // what the concurrency contract exists to prevent.
    expect(readColumnText(stored, 'name'), 'name was not changed by this update').toBe(
      baseline.name,
    );

    expect(readColumnNumber(stored, 'age'), 'age was not changed by this update').toBe(
      baseline.age,
    );

    const birth: string | undefined = readColumnText(stored, 'birth');

    expect(birth, 'birth was not changed by this update, and is still text').toBe(baseline.birth);

    expect(
      BIRTH_TEXT_PATTERN.test(birth ?? ''),
      'birth must still be in the layout the edit mask declares',
    ).toBe(true);

    // THE ROW IS LEFT IN PLACE. Not deleted, not reset, not reseeded, and no
    // cleanup hook exists in this file. The paired-capture rule forbids
    // recreating or reseeding the volume between the two halves of a comparison,
    // so tidying up here would void every pair. The conflict spec creates its own
    // row rather than depending on this one.
  });
});
