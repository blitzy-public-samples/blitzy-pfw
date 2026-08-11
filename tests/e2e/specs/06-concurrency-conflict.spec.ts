/**
 * The optimistic-concurrency conflict path — assertion group 6 of 6.
 * Contract C-06's conflict semantics, relayed by C-03 and projected by C-09 as
 * HTTP `409` over `/v1/datawindow/update`.
 *
 * THE ONE THING THIS FILE EXISTS TO PROVE
 * ---------------------------------------
 * **THERE IS NO SILENT OVERWRITE ANYWHERE IN THIS SYSTEM.** That is stated as an
 * absolute rather than a preference, and it is the one property no unit test on
 * either side of the boundary can observe, because it is a property OF the
 * boundary. A silent overwrite is invisible by construction: it answers `200`,
 * reports a plausible affected-row count, and destroys data. So it is asserted
 * here, end to end, through the real ingress.
 *
 * On an `updatewhereclause` mismatch, Persistence answers gRPC **`Aborted`**,
 * DataServices relays it, and **Gateway projects it as HTTP `409`** carrying a
 * structured conflict detail with the current row state. `Aborted` rather than
 * `FailedPrecondition` is deliberate — the operation MAY succeed if the caller
 * re-reads and retries, which is what `Aborted` means — and `Aborted` is also the
 * canonical gRPC-to-`409` mapping. Callers therefore implement an explicit
 * **retry-or-surface** policy, and both arms of that policy are exercised below:
 * the conflict is surfaced (steps 3 to 5) and then resolved by an explicit
 * refresh-and-retry (step 6).
 *
 * WHY THE CHECK SPANS ALL SIX COLUMNS, AND WHAT THAT COSTS THE WIRE
 * ----------------------------------------------------------------
 * `ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L14` carries `updatewhere=1` — the
 * "key and updateable columns" concurrency mode — and `:L8-L13` mark **all six**
 * columns `update=yes updatewhereclause=yes`. The generated `WHERE` clause
 * therefore carries the key column PLUS THE ORIGINAL VALUE OF EVERY UPDATEABLE
 * COLUMN, so the optimistic-concurrency check spans all six columns' original
 * values.
 *
 * The consequence for the wire is exact, and it is what the payload assertions in
 * step 4 are about: **the payload carries, per row, BOTH the current and the
 * original value of every marked column.** A flat rowset could not express that.
 * It is also why the legacy result carrier derives from a `datastore` and so IS a
 * DataWindow — complete with buffers and item statuses — rather than a result set,
 * and why a caller that could not see both sides could not tell WHICH column
 * moved underneath it and so could not construct a retry at all.
 *
 * WHY THIS SPEC ASSERTS FAILURE RATHER THAN TRUSTING A RETURNED CODE
 * -----------------------------------------------------------------
 * Two behaviours of `ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlupdate.sru`
 * explain why a naive port gets the result backwards, and they are precisely the
 * reason the outer check below is a status assertion:
 *
 * - The legacy update's SUCCESS IS THE VALUE 1, not the zero of the return-code
 *   algebra [`:L204`]. A port that treated the update's own answer as the
 *   framework's algebra would read a success as a failure, or worse.
 * - A CLAIMED SUCCESS IS DEFENSIVELY REWRITTEN INTO A FAILURE when the
 *   transaction's SQL code indicates an error [`:L208-L210`]. The legacy itself
 *   does not trust the update call's return value — so an implementation that
 *   does would report success on a failed update. This file is the outer check
 *   that it does not.
 *
 * Adjacent, and the reason nothing here changes the key: `updatekeyinplace=no`
 * means a key change executes as delete-plus-insert, and the legacy carries a
 * self-assignment workaround for it under its own fix-me annotation
 * [`:L151-L167`, the self-assignment at `:L163`]. That path is a .NET-side parity
 * concern with no stable HTTP contract stated in the plan of record, so
 * exercising it here would assert undeclared behaviour. **Only non-key columns
 * are changed below.**
 *
 * The error surface this spec guards against leaking is
 * `ws_objects/pfw.thread.ext.pbl.src/dberrordata.srs`, whose `sqlsyntax` member
 * carries the COMPLETE generated statement including interpolated literal values,
 * against a legacy logger that performs no redaction at all. The .NET side
 * redacts or parameter-separates it, and step 4 enforces that rather than
 * assuming it.
 *
 * Every locator above is SPECIFICATION FOR THE AUTHOR, not an input this file
 * loads. Nothing here reads, imports, globs or fetches any `ws_objects/**` path,
 * and nothing here touches the three read-only browser-asset directories beside
 * this one (C-C). Every import resolves inside `tests/e2e`.
 *
 * TWO DOCTRINES, AND WHY THEY MAKE THIS FILE SAFE TO RUN TWICE
 * -----------------------------------------------------------
 * 1. **THIS SPEC CREATES ITS OWN STARTING ROW THROUGH THE PUBLIC WORKFLOW.** A
 *    conflict is a function of a KNOWN row state, and a row this file did not
 *    create is not a known state — the `persistence-db` volume is shared and is
 *    never reseeded, so a row left by an earlier run carries the same
 *    deterministic label. The row is created through Gateway in step 1, addressed
 *    afterwards by the key the ENGINE assigned, and left in place at the end.
 * 2. **NOTHING IS RESET, RESEEDED, DROPPED, RECREATED OR DDL'D, AND STORAGE IS
 *    NEVER TOUCHED DIRECTLY.** The paired-capture rule binds: for one workflow
 *    identifier the legacy-side and target-side recordings must be taken against
 *    the same `persistence-db` volume state, and the volume must not be recreated
 *    or reseeded between them, or the pair is not comparable. A spec that tidied
 *    up after itself would void every pair. No statement text of any kind is
 *    issued from this file — Persistence is the only service that generates or
 *    executes one, and this suite reaches it only through Gateway.
 *
 * Because no absolute row count, retrieve length or page-scoped footer total is
 * asserted anywhere below, and because each run creates its own row, this file is
 * idempotent by construction: running it twice against an untouched volume must
 * pass both times. That is the single most valuable check on it.
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules were
 * provided. That is a finding rather than an omission, and it is not licence to
 * lower the bar — the enterprise-standard baseline applies in its place, together
 * with the non-rule constraints below. Each is named with what it requires HERE.
 *
 * - **C-B — no behaviour improvement, no corrected defect, no performance claim.**
 *   Its sharpest edge in this file: **a stale update must FAIL, and the assertion
 *   is `=== 409` exactly** — not "some 4xx", not "not 200". A caller's
 *   retry-or-surface branch keys off that precise code, so a `400` or a `412`
 *   would send a correct caller down the wrong path. **This spec must never be
 *   retried into a pass**: `retries` is `0` globally in the sibling
 *   `playwright.config.ts`, and it is pinned to `0` again locally below so that a
 *   repository-wide CI retry count introduced later cannot mask a real
 *   concurrency regression. There is no polling loop, no `expect.poll`, no
 *   wait-and-try-again and no second attempt anywhere near the conflict
 *   assertion — retrying a conflict assertion is exactly how a silent overwrite
 *   would slip through undetected. The preserved schema disagreements are honoured
 *   rather than reconciled: every `address` sent is far inside the narrower of the
 *   two declared widths and NO length validation is asserted at 50 or at 200;
 *   `salary` is compared within `SALARY_COMPARISON_TOLERANCE` and never with
 *   strict equality, because a two-place decimal in a `REAL` column is not
 *   guaranteed to return an identical double; and `birth` is compared as a
 *   `yyyy-mm-dd` STRING, because the column is `TEXT` in storage. No latency,
 *   throughput, availability or service-level figure is asserted — the repository
 *   publishes none, so any number here would be fabricated. The runner's timeouts
 *   are hang guards, not budgets.
 * - **C-L — the environment's setup instructions bind.** The shared-volume rule
 *   above is honoured in full; the known starting row is created through the
 *   public workflow instead. The file runs unchanged under the documented path,
 *   `cd tests/e2e && npm ci && npx playwright test`, against Gateway on its
 *   documented ingress port.
 * - **C-G — every new boundary is authenticated.** Every request below carries a
 *   bearer token minted at run time by Security, the sole issuer, following the
 *   pattern spec 02 establishes: acquire inside the test, attach per request, let
 *   it fall out of scope.
 * - **C-F — no secret in source.** No key, certificate, password, pre-minted
 *   token or encoded credential run appears anywhere in this file. Tokens come
 *   only from `requireServiceToken`, and NO TOKEN, `Authorization` HEADER OR
 *   RESPONSE BODY IS EVER RENDERED into an assertion message, because a message
 *   reaches the console and the CI log and both are retained. The leakage guard in
 *   step 4 reports the NAME of the pattern it matched and never the text that
 *   matched it.
 * - **C-D — the deferred services are not implemented, so they are not
 *   exercised.** Only the HEADLESS half of the DataWindow surface is asserted.
 *   Nothing here touches window geometry, DPI conversion, font measurement, menu
 *   rendering or input-method behaviour, and no reserved extension-point route is
 *   requested.
 * - **C-J — one orchestration path.** Nothing here starts, stops, restarts,
 *   builds, seeds or health-gates a service. The stack is brought up beforehand,
 *   and exclusively, by `orchestration/docker-compose.yml`.
 * - **C-H — nothing about coverage is asserted.** That gate is a .NET concern,
 *   measured per service from a Cobertura report.
 * - **Topology.** All traffic goes through Gateway, the sole ingress. There is no
 *   functional call to DataServices or to Persistence, no database access of any
 *   kind, and the reserved Phase-2 port number appears nowhere in this file.
 *
 * HOW THE SIX STEPS COMPOSE
 * -------------------------
 * The describe block runs SERIALLY, because these are one workflow over shared
 * `COMPANY` state rather than six independent checks, and a later step is
 * meaningless if an earlier one failed:
 *
 * 1. Create a known starting row through the public workflow, and capture its
 *    engine-assigned key together with the full six-column snapshot.
 * 2. Apply a LEGITIMATE update. This is what makes the step-1 snapshot STALE.
 * 3. Submit the stale update built by `asStaleUpdate`. Assert `409`.
 * 4. Assert the conflict payload: machine-readable, both value sets for all six
 *    marked columns, current values agreeing with reality, and no statement text
 *    or credential-shaped material.
 * 5. Re-read and prove the refused update changed NOTHING — in both directions:
 *    the legitimate value is still there, and the stale value did not land.
 * 6. Exercise the retry arm: refresh the originals from what is actually stored,
 *    change a value, and confirm it now applies. A single deliberate retry of the
 *    WORKFLOW, which is a different thing entirely from a runner retry of the
 *    assertion — that remains forbidden.
 *
 * The conventions used below — the route table, the request message shapes, the
 * candidate-key response resolution and the original-value discipline — are the
 * ones spec 05 settled, mirrored here so the two cannot drift. **Nothing is
 * imported from spec 05**, so this file remains self-contained and runnable alone
 * with `--grep`.
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
  MARKED_COLUMNS,
  SALARY_COMPARISON_TOLERANCE,
  STALE_ORIGINAL_SENTINEL,
  asStaleUpdate,
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

import { probeStackAvailability } from '../fixtures/live-stack';

/* ------------------------------------------------------------------------- *
 * THE ROUTE TABLE — ONE OF THIS FILE'S TWO EDIT POINTS FOR WIRE SHAPE
 *
 * Every path this spec requests is composed here, from the one imported prefix,
 * and nowhere else, so a renamed route is a one-line change and no path literal
 * appears further down. The segments are the two the ingress contract declares
 * beneath `/v1/datawindow` for the retrieval and update thirds of the triple:
 * `retrieve` projects C-03's `Retrieve` server stream, and `update` projects
 * C-03's `Update` — the operation that declares the `409` this whole file is
 * about.
 *
 * DELIBERATELY DERIVED RATHER THAN SHARED. Spec 05 declares the same table for
 * the same reason, and this one is a mirror rather than an import: importing
 * across spec files would make either file unrunnable alone under `--grep`, and
 * a spec that cannot be run alone cannot be bisected when it fails. The two are
 * kept in step by both being composed from the single imported prefix.
 *
 * NO SESSION ROUTE IS USED, AND THAT IS DELIBERATE. A validation session
 * materializes the four pieces of cross-event mutable state the legacy holds as
 * private fields of one control, and it is required by the EVENT CHAIN, not by a
 * retrieve or an update. The update contract carries the session identifier as an
 * OPTIONAL member precisely because an update may be issued outside a chain, and
 * that is the shape this workflow uses.
 * ------------------------------------------------------------------------- */
const ROUTES = {
  /** `POST` — the RETRIEVAL third of the triple. Answers an ordered chunk sequence. */
  retrieve: `${DATAWINDOW_PATH_PREFIX}/retrieve`,

  /** `POST` — the UPDATE third. The operation that answers `409` on a mismatch. */
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
 * `dw_sqlite.srd`. Naming it from the oracle keeps it traceable rather than
 * arbitrary.
 */
const DATAWINDOW_HANDLE = 'dw_sqlite';

/**
 * The status an optimistic-concurrency mismatch must produce. Asserted with
 * strict equality and never as a range.
 *
 * Named as a constant so that the one number this file turns on is stated once
 * and is greppable. It is the canonical projection of gRPC `Aborted`, fixed by
 * `docs/ARCHITECTURE.md` and by the ingress contract's own status mapping, and it
 * is the code a caller's retry-or-surface branch keys off.
 */
const CONFLICT_STATUS = 409;

/* ------------------------------------------------------------------------- *
 * THE REQUEST SIDE — EXACT CONTRACT NAMES, TYPED
 *
 * The ingress binds a request body with the protobuf JSON parser in its DEFAULT
 * configuration, which REJECTS AN UNKNOWN FIELD rather than ignoring it. A
 * request member is therefore not a place to be approximate: the names below are
 * the canonical protobuf JSON mapping of the message each operation declares —
 * lowerCamelCase of the proto field name — and they are declared once, as types,
 * so the compiler keeps every call site honest.
 *
 * Enumerated values travel as their PROTO ENUMERATOR NAMES rather than their
 * numbers, because that is what the canonical mapping emits and accepts. The
 * SCREAMING_SNAKE spellings are preserved verbatim, exactly as the constant
 * identifiers are preserved on the .NET side: they appear in serialized payloads,
 * log records and characterization recordings, where a restyling would silently
 * invalidate every stored comparison that mentions one.
 * ------------------------------------------------------------------------- */

/** `Primary!` — the live rows. The zero value, and the only buffer this file writes. */
const DW_BUFFER_PRIMARY = 'DW_BUFFER_PRIMARY';

/**
 * `NewModified!` — inserted and edited. The status an INSERT must carry.
 *
 * Not interchangeable with `ITEM_STATUS_NEW`: the legacy identity round-trip
 * collects a value only for a row whose status reads `NewModified!`
 * [`n_cst_thread_task_sqlupdate.sru:L232`, and `:L238` for the filter buffer], so
 * a row tagged otherwise would be inserted without its assigned key ever coming
 * back — and without that key this file could not address the row it created.
 */
const ITEM_STATUS_NEW_MODIFIED = 'ITEM_STATUS_NEW_MODIFIED';

/** `DataModified!` — an existing row that has been edited. The status an UPDATE carries. */
const ITEM_STATUS_DATA_MODIFIED = 'ITEM_STATUS_DATA_MODIFIED';

/**
 * One legacy scalar value on the wire.
 *
 * EXACTLY ONE MEMBER IS PRESENT, which is how the canonical mapping encodes a
 * `oneof`. Three arms carry a distinction this workflow depends on: `isNull` is an
 * arm of its OWN rather than the absence of a value, so "no value" stays
 * distinguishable from "the value zero" — and that distinction is load-bearing
 * here, because the concurrency comparison compares nulls; a decimal travels as
 * exact TEXT rather than as a JSON number, because the legacy type is exact and a
 * double is not; and a date travels as text so that no zone is implied where the
 * legacy implies none.
 *
 * Only the arms this file writes or reads are declared. The wire domain is wider —
 * a boolean, an unsigned 64-bit integer, a time, a timestamp and a blob arm all
 * exist — and declaring the unused ones would be inventing a dependency on
 * members no `COMPANY` column can hold.
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
 * `originalValues` is the member that makes this a DataWindow carrier rather than
 * a flat rowset, and it is the whole reason a conflict is detectable at all: under
 * `updatewhere=1` with all six columns marked, the generated `WHERE` clause
 * carries the key column PLUS the original value of every updateable column. A
 * payload carrying only current values could not express what the statement
 * compared, and nothing could then report which column moved.
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
 * optional in the sense that an update may be issued outside a validation chain,
 * and the canonical mapping's default for a string field is the empty string, so
 * this is the encoding of "no session" rather than a placeholder.
 *
 * Two members the legacy update call took are DELIBERATELY ABSENT because the
 * contract RESERVED them — accept-text and the reset flag. The legacy passes true
 * and false respectively, on every update it performs
 * [`n_cst_thread_task_sqlupdate.sru:L204`], and the contract fixes that behaviour
 * rather than parameterising it, so sending either name would be rejected as an
 * unknown field.
 */
interface WireUpdateRequest {
  readonly datawindowHandle: string;
  readonly sessionId: string;
  readonly rows: readonly WireDataWindowRow[];
}

/* ------------------------------------------------------------------------- *
 * THE RESPONSE SIDE — THE FILE'S SECOND EDIT POINT, ONE CANDIDATE-KEY TABLE
 *
 * Reading is treated differently from writing, and the asymmetry is deliberate. A
 * request must be exact because the parser rejects an unknown field; a RESPONSE is
 * the thing under test, so a reader that assumed its own spelling would report
 * "the member is missing" for a boundary that had merely serialized it
 * differently — the least useful diagnostic available, and the most misleading on
 * a conflict payload, where "missing" is itself a contract violation with real
 * consequences. Every member this file reads therefore resolves through the table
 * below, which lists the canonical mapping's spelling FIRST and the plausible
 * alternatives after it.
 *
 * This is emphatically NOT a licence to guess a shape. Nothing below asserts deep
 * structural equality against a schema, and no member is read that the published
 * contract does not declare — the `409` body is `ConflictProblemDetails`, an RFC
 * 9457 problem document whose `conflict` member is the field-for-field mirror of
 * `common.v1.ConflictDetail`. It is a tolerance in SPELLING only, and it is why a
 * serialization surprise costs one edit here rather than an edit at every call
 * site. Spec 05 resolves its retrieval payload the same way.
 * ------------------------------------------------------------------------- */
const RESPONSE_KEYS = {
  /**
   * The retrieval's row collection.
   *
   * A retrieval answers the ORDERED SEQUENCE OF CHUNKS the server stream would
   * have delivered — a bare collection rather than an invented envelope, because
   * the protocol definition has no envelope message for a stream. `rows` is a
   * member of each chunk; the alternatives cover an envelope appearing anyway.
   */
  chunkCollection: ['chunks', 'items', 'result', 'data'],

  /** Rows within one chunk, and the rows within a conflict detail. Same spelling. */
  rows: ['rows', 'Rows'],

  /** Row-level carrier members. Their presence is what makes this a DataWindow. */
  buffer: ['buffer', 'Buffer'],
  rowOrdinal: ['row', 'Row'],
  itemStatus: ['itemStatus', 'item_status'],
  columns: ['columns', 'Columns'],
  originalValues: ['originalValues', 'original_values'],

  /**
   * The conflict row's CURRENT value set.
   *
   * Named `currentValues` rather than `columns`, and the difference is meaningful
   * rather than cosmetic: on a retrieved row `columns` is what the row holds,
   * while on a conflict row `currentValues` is the SERVER-SIDE state a retry would
   * be rebased onto. Both spellings are accepted for the same value set, in the
   * contract's order first, so a projection that reused the retrieval's member
   * name still resolves.
   */
  conflictCurrentValues: ['currentValues', 'current_values', 'columns'],

  /** Column-level members. */
  columnName: ['columnName', 'column_name'],
  columnId: ['columnId', 'column_id'],
  columnValue: ['value', 'Value'],

  /** The scalar arms this workflow reads, in the order a numeric read prefers them. */
  valueIsNull: ['isNull', 'is_null'],
  valueText: ['stringValue', 'string_value'],
  valueInt64: ['int64Value', 'int64_value'],
  valueDouble: ['doubleValue', 'double_value'],
  valueDecimal: ['decimalValue', 'decimal_value'],
  valueDate: ['dateValue', 'date_value'],
  wrappedValue: ['value', 'Value'],

  /**
   * The identity round-trip on the update response.
   *
   * The response carries the identity column and two value arrays — one collected
   * from the primary buffer FORWARD and one from the filter buffer BACKWARD,
   * because the filter buffer's row order is inverted relative to the source
   * [`n_cst_thread_task_sqlupdate.sru:L235-L241`]. A row inserted into the primary
   * buffer lands in the first of the two, which is the only one read here.
   */
  identity: ['identity', 'Identity'],
  identityColumnId: ['identityColumnId', 'identity_column_id'],
  identityPrimaryValues: ['primaryValues', 'primary_values'],

  /** The affected-row counts. Emitted as strings, because they are 64-bit. */
  rowsInserted: ['rowsInserted', 'rows_inserted'],
  rowsUpdated: ['rowsUpdated', 'rows_updated'],

  /** The legacy return code the response or the problem document carries. */
  retCode: ['retCode', 'ret_code'],

  /**
   * THE CONFLICT DETAIL ITSELF — the member this file exists to read.
   *
   * It is an extension member on the problem document, forwarded UNCHANGED from
   * the upstream rather than reshaped, because a caller deciding between retrying
   * and surfacing needs the current row state exactly as the database reported it.
   * The contract marks it REQUIRED on the conflict body, so its absence is a
   * finding rather than a tolerance — see the diagnosis in step 4, which names the
   * one legitimate shape that carries no such member and why it is still a failure
   * of this workflow.
   */
  conflict: ['conflict', 'Conflict', 'conflictDetail', 'conflict_detail'],

  /** Which table the failing statement targeted. Present because multi-table update is real. */
  conflictUpdateTable: ['updateTable', 'update_table'],

  /** How many rows the statement expected to affect, and how many it matched. */
  conflictRowsExpected: ['rowsExpected', 'rows_expected'],
  conflictRowsMatched: ['rowsMatched', 'rows_matched'],

  /**
   * The problem-document members.
   *
   * A refusal is an RFC 9457 problem document — the shape the framework emits
   * without hand-written code — so the ERROR CONTAINER IS THE BODY ITSELF rather
   * than a nested member. `type` is read for one purpose only: to tell the
   * conflict-with-detail projection apart from the conflict-without-detail one, so
   * that a missing `conflict` member is diagnosed rather than merely reported.
   */
  problemMembers: ['type', 'title', 'status', 'detail', 'instance'],
  problemType: ['type', 'Type'],
  problemStatus: ['status', 'Status'],
} as const;

/**
 * `failOnStatusCode` is pinned false on every request below, and on this file's
 * central case that is a requirement rather than a preference.
 *
 * Left to throw on a non-2xx, the runner raises an error of its own whose message
 * can quote the response body verbatim — and a body from an authenticated endpoint
 * is the one thing that must never reach a log. Pinning it false routes every
 * status through an assertion in this file, so exactly one message shape is
 * produced and it quotes nothing. It is also what makes the `409` ASSERTABLE at
 * all: a thrown transport error is not a status, and a file whose central
 * assertion is a status cannot afford to have the status turned into an exception
 * before it is read.
 */
const NEVER_THROW_ON_STATUS = { failOnStatusCode: false } as const;

/* ------------------------------------------------------------------------- *
 * Reading a response, defensively
 *
 * Everything below is a pure function over a parsed JSON value. None of it
 * asserts; each returns `undefined` where a member is absent or the wrong shape,
 * and the TEST decides what an absence means. Keeping the two apart is what lets a
 * failure message name the missing member and side instead of surfacing as a type
 * error from inside a helper — which on this file's payload assertions is the
 * difference between a diagnosable regression and a puzzle.
 * ------------------------------------------------------------------------- */

/** Narrows a parsed JSON value to a plain object, or `undefined` if it is not one. */
function asRecord(value: unknown): Record<string, unknown> | undefined {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined;
}

/**
 * Reads the first member present out of a candidate list.
 *
 * Presence is tested with `hasOwnProperty` rather than by comparing against
 * `undefined`, so a member that is genuinely present and null is reported as
 * present. That distinction matters on this boundary: null is a value of its own
 * in the legacy scalar domain, not an absence, and the concurrency comparison
 * compares nulls.
 */
function readMember(container: unknown, candidates: readonly string[]): unknown {
  const record = asRecord(container);

  if (record === undefined) {
    return undefined;
  }

  for (const candidate of candidates) {
    if (Object.prototype.hasOwnProperty.call(record, candidate)) {
      return record[candidate];
    }
  }

  return undefined;
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
 * Delegated to the fixture's converter rather than to `Number(...)`, because that
 * converter REFUSES a value the `number` type cannot carry exactly instead of
 * rounding it silently — and a rounded key addresses a different row, or none,
 * while still passing an integer check. In this file that matters more than
 * anywhere: a rounded key would produce a conflict against the wrong row, and the
 * failure would be reported as a concurrency problem rather than as a corrupted
 * identity. A non-integer or absent member yields `undefined` rather than a throw,
 * so the caller can say what was missing.
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
 * The DataWindow declares its detail columns with ids 1 through 6 in the order the
 * DDL declares them [`dw_sqlite.srd:L21-L26`], and the fixture's table is in that
 * same order — so the ordinal IS the position, and deriving it means a column can
 * never be added to the schema description without the ordinals following. Typing
 * the six numbers out would create a second source of truth that agrees until the
 * first edit.
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
 * Locates one column's entry within a collection of column values.
 *
 * Matched on NAME FIRST, case-insensitively — the oracle spells its column names
 * in lower case and SQLite identifiers are case-insensitive, so a projection that
 * upper-cased them would be conforming rather than broken — and on the ONE-BASED
 * ORDINAL as a fallback. The fallback is not defensive padding: the legacy's
 * cross-thread carrier serialization is POSITIONAL and carries column ordinals
 * with no names at all, so a row projected from a carrier changeset legitimately
 * leaves the name empty and the ordinal is then the authoritative identifier.
 *
 * Takes the COLLECTION rather than the row, which is what lets the same resolver
 * serve a retrieved row's `columns`, a conflict row's `currentValues` and a
 * conflict row's `originalValues` — three collections whose members must be
 * located identically for the payload assertions to mean anything.
 */
function findColumnEntry(
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
function findColumnValue(
  columns: readonly unknown[] | undefined,
  column: CompanyColumnName,
): unknown {
  const entry: unknown = findColumnEntry(columns, column);

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
 * Reads a scalar value node as TEXT, from whichever arm the boundary chose.
 *
 * `birth` is the reason this accepts more than one arm. The DataWindow declares it
 * `date` while the column is `TEXT` — a preserved mismatch — so a faithful
 * projection may legitimately carry it as either a date arm or a plain string arm,
 * and both are the same `yyyy-mm-dd` text underneath. Reading it as text in both
 * cases is what lets the comparison be a STRING comparison, which is the only
 * comparison the storage type justifies.
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
 * Reads a scalar value node as a NUMBER, from whichever arm the boundary chose.
 *
 * The arms are tried in order of fidelity: an exact decimal first, then a genuine
 * double, then a widened integer, then text. `salary` is the case that matters —
 * declared `decimal(2)` over a `REAL` column, so it may arrive as exact decimal
 * text or as a double, and either way the comparison that follows must be within a
 * tolerance rather than exact.
 */
function numberOfValue(value: unknown): number | undefined {
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

/** Reads one column of a RETRIEVED row as text. */
function readColumnText(row: unknown, column: CompanyColumnName): string | undefined {
  return textOfValue(findColumnValue(readArray(row, RESPONSE_KEYS.columns), column));
}

/** Reads one column of a RETRIEVED row as a number. */
function readColumnNumber(row: unknown, column: CompanyColumnName): number | undefined {
  return numberOfValue(findColumnValue(readArray(row, RESPONSE_KEYS.columns), column));
}

/* ------------------------------------------------------------------------- *
 * Encoding a fixture row for the wire
 *
 * Row DATA comes from the fixture and only from the fixture; what follows is
 * purely the ENVELOPE, which is the spec's business by design. Nothing here
 * invents a value and nothing here restates the schema.
 * ------------------------------------------------------------------------- */

/**
 * The columns an INSERT supplies — every column except the identity one.
 *
 * Derived by filtering the fixture's own `isIdentity` marker rather than listing
 * five names, so the omission is stated as the REASON for it. The oracle omits
 * `ID` from all four of its batch inserts and from its dynamic path as well
 * [`w_test_sqlite.srw:L381-L388`, `:L396-L400`], because the engine assigns it —
 * and this file depends on that, since the key it addresses the row by afterwards
 * is the one the engine chose.
 */
const INSERT_COLUMNS: readonly CompanyColumnName[] = COMPANY_COLUMNS.filter(
  (column: CompanyColumn) => !column.isIdentity,
).map((column: CompanyColumn) => column.name);

/**
 * Reads one field off an insert-shaped row.
 *
 * Refuses the identity column outright rather than answering null for it. A caller
 * asking an insert-shaped row for its key has made a mistake that would otherwise
 * travel silently into a payload, and the fixture takes the same posture — it
 * rejects a patch carrying an `id` instead of dropping it.
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
 * The DataWindow's declaration governs, not the DDL's, because what travels here is
 * a DataWindow row. That choice is what carries two of the preserved mismatches
 * onto the wire intact rather than quietly reconciling them: `salary` is declared
 * `decimal(2)` and so is sent as EXACT DECIMAL TEXT even though the column is
 * `REAL`, and `birth` is declared `date` and so is sent as a date even though the
 * column is `TEXT`. Reading them back accepts either arm, because the mismatch
 * means either is faithful.
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
 * `originalValues` is empty because an inserted row HAS no prior state to compare
 * against — the concurrency check applies to an update, and the contract says as
 * much: originals are populated on the update path. A `409` in response to this
 * payload would therefore mean originals were being compared for an insert.
 */
function encodeInsertRow(input: CompanyRowInput): WireDataWindowRow {
  return {
    buffer: DW_BUFFER_PRIMARY,
    // ONE-BASED, like every row ordinal in this system. A single-row payload is
    // row 1, not row 0.
    row: 1,
    itemStatus: ITEM_STATUS_NEW_MODIFIED,
    columns: INSERT_COLUMNS.map((column: CompanyColumnName) =>
      encodeColumn(column, insertFieldValue(input, column)),
    ),
    originalValues: [],
  };
}

/**
 * Encodes an update: a `DataModified!` primary-buffer row carrying the current
 * values AND the original value of every marked column.
 *
 * BOTH SETS ARE MANDATORY, and that is the whole substance of the update contract.
 * `updatewhere=1` is the "key and updateable columns" concurrency mode and all six
 * columns carry `updatewhereclause=yes`, so the generated `WHERE` clause compares
 * ALL SIX ORIGINAL VALUES. The originals are drawn from {@link MARKED_COLUMNS}
 * rather than from a list written here, so the payload follows the fixture's
 * declaration of what is marked instead of a copy of it.
 *
 * WHY `verbatimOriginals` EXISTS, AND WHEN IT MUST NOT BE USED. The `WHERE` clause
 * has to match what is STORED, and `salary` is a two-place decimal held in a
 * `REAL` column, so a value re-encoded from a decoded double is not guaranteed to
 * be the text the server would compare against. Re-sending the server's OWN values
 * removes that whole class of doubt, which is what a LEGITIMATE update wants.
 *
 * A STALE update wants the exact opposite, and the distinction is the hinge of this
 * file: the stale payload must carry the falsified originals the fixture built, so
 * it is encoded with this argument OMITTED. Passing verbatim originals there would
 * overwrite the staleness with the truth, the update would apply, and the `409`
 * assertion would fail while reporting nothing about the system — the worst
 * available outcome for a conflict spec, and the reason this is spelled out here
 * rather than left to a call site.
 *
 * @param update the `{ current, original }` pair the fixture built
 * @param verbatimOriginals the row's own values exactly as the retrieval answered
 *   them, for a legitimate update only
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
 * Re-encodes a retrieved row's own column values, so they can be sent back as the
 * originals of a LEGITIMATE update.
 *
 * The values are carried through UNCHANGED; only the envelope is rebuilt, and the
 * ordinal is re-derived from the fixture so a row that arrived from a positional
 * carrier changeset — which legitimately carries no names — still yields a named
 * payload. A column the retrieval did not answer is skipped rather than
 * substituted, because a fabricated member would say something the retrieval did
 * not.
 */
function verbatimColumnsFrom(row: unknown): readonly WireColumnValue[] {
  const columns: readonly unknown[] | undefined = readArray(row, RESPONSE_KEYS.columns);
  const encoded: WireColumnValue[] = [];

  for (const column of MARKED_COLUMNS) {
    const value: unknown = findColumnValue(columns, column);
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
 * Two senders and a small stack of readers, so that no test below composes a URL,
 * builds a header or decides how a body is serialized. EVERY REQUEST GOES TO
 * GATEWAY, the sole ingress; there is no direct call to DataServices or to
 * Persistence anywhere in this file.
 * ------------------------------------------------------------------------- */

/**
 * Issues the retrieval.
 *
 * `buffers` is sent EMPTY, which the contract defines as the primary buffer alone —
 * the legacy default, since a freshly retrieved DataWindow has nothing in
 * `Delete!` and nothing in `Filter!` until a delete or a filter has run. Chunk size
 * is left unsent, so the server chooses: naming a size here would assert a chunking
 * policy this workflow has no stake in.
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
 * The contract publishes the body as a BARE ORDERED COLLECTION rather than an
 * envelope, because the protocol definition has no envelope message for a stream —
 * so the array case is the expected one and the envelope case is only a tolerance.
 * Element order is the stream's order and is never re-sorted here.
 */
async function readChunks(response: APIResponse): Promise<readonly unknown[]> {
  const body: unknown = await response.json();

  if (Array.isArray(body)) {
    return body as readonly unknown[];
  }

  return readArray(body, RESPONSE_KEYS.chunkCollection) ?? [];
}

/** Flattens every chunk's rows into one sequence, preserving arrival order. */
function collectRows(chunks: readonly unknown[]): readonly unknown[] {
  const rows: unknown[] = [];

  for (const chunk of chunks) {
    for (const row of readArray(chunk, RESPONSE_KEYS.rows) ?? []) {
      rows.push(row);
    }
  }

  return rows;
}

/** Retrieves and flattens in one step, which is what every read-back below wants. */
async function retrieveRows(
  request: APIRequestContext,
  token: ServiceToken,
): Promise<readonly unknown[]> {
  const response: APIResponse = await requestRetrieve(request, token);

  expect(
    response.status(),
    `${ROUTES.retrieve} must answer 200 for an authenticated caller. A 401 means ` +
      'the token was not accepted; a 502 means Gateway could not reach ' +
      'DataServices, which is a failure the decomposition itself created and ' +
      'which an in-process call could not have had. Note that a 409 is not a ' +
      'possible answer here: only the update operation carries a conflict status.',
  ).toBe(200);

  return collectRows(await readChunks(response));
}

/**
 * Reads a row's engine-assigned key, LOSSLESSLY.
 *
 * Routed through the fixture's converter rather than through a generic numeric
 * read, because the identity is declared 64-bit on the contract and a value beyond
 * the exactly-representable range has already been ROUNDED by the time a JSON
 * parser hands it over as a number. The converter refuses such a value instead of
 * returning a plausible wrong one — which matters more here than anywhere else in
 * this suite, since a rounded key would address a DIFFERENT row and the resulting
 * failure would be reported as a concurrency conflict rather than as a corrupted
 * identity, sending a reader after exactly the wrong defect.
 */
function readRowIdentity(row: unknown): number | undefined {
  const value: unknown = findColumnValue(readArray(row, RESPONSE_KEYS.columns), 'id');

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
 * Builds the six-column snapshot a later step's `original` half is derived from.
 *
 * Built from what the RETRIEVAL answered rather than from what was sent, because
 * the `WHERE` clause of any subsequent update has to match what is STORED. The
 * identity is passed in rather than re-read, so the snapshot is anchored to the key
 * the caller has already validated.
 */
function snapshotOf(row: unknown, identity: number): CompanyRow {
  return {
    id: identity,
    name: readColumnText(row, 'name') ?? '',
    age: readColumnNumber(row, 'age') ?? 0,
    address: readColumnText(row, 'address') ?? null,
    salary: readColumnNumber(row, 'salary') ?? null,
    birth: readColumnText(row, 'birth') ?? null,
  };
}

/**
 * True when a return code means zero — the value `OK`, `SUCCESS` and `ALLOW` all
 * spell.
 *
 * Three spellings because the oracle declares three constants for zero, and the
 * canonical mapping emits the first of an alias group while accepting any of them;
 * the bare number is accepted too, since the same algebra travels as an integer on
 * a problem document and as an enumerator name on a projected message.
 *
 * THE TEST IS FOR ZERO SPECIFICALLY, NOT FOR "SUCCESS". `PREVENT` is 1 and
 * satisfies the legacy success predicate, and `CANCELED` is -2 and is NEITHER
 * succeeded nor failed, so a two-way success test over this field would classify a
 * prevention as an ordinary success and a cancellation as a failure. Both are
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
 * THE LEAKAGE GUARD
 *
 * The legacy error structure's statement member carries the COMPLETE generated
 * statement including interpolated literal values, and the legacy logger performs
 * no redaction at all — so the .NET side redacts or parameter-separates it before
 * it can reach a caller. Step 4 ENFORCES that rather than assuming it, because an
 * assumption is exactly what a regression here would survive.
 *
 * Everything below is a DETECTOR, not a statement. No statement of any kind is
 * issued, composed or executed anywhere in this file — Persistence is the only
 * service that generates or executes one. The keyword tokens are held as separate
 * pairs and the patterns are assembled at run time, precisely so that no
 * statement-shaped literal exists in this source at all. The credential marker is
 * assembled from its parts for the same reason: a file that hunts for a key
 * material header must not contain one.
 *
 * Every detector reports its NAME only. Nothing that matches is ever rendered into
 * a message, because a message reaches the console and the CI log and both are
 * retained — and the whole point of the guard is that this payload may contain
 * something that must not be published.
 * ------------------------------------------------------------------------- */

/**
 * Ordered keyword pairs whose co-occurrence within a short window is
 * statement-shaped.
 *
 * A pair rather than a single keyword, because every one of these words has an
 * innocent life of its own in prose and in a route path — the request path itself
 * ends in one of them. Requiring the SECOND token to follow the first within a
 * small window is what distinguishes a clause from a sentence, and it is why this
 * guard does not fire on the projection's own explanatory `detail` text.
 */
const STATEMENT_KEYWORD_PAIRS: readonly (readonly [string, string])[] = Object.freeze([
  Object.freeze(['select', 'from'] as [string, string]),
  Object.freeze(['insert', 'into'] as [string, string]),
  Object.freeze(['update', 'set'] as [string, string]),
  Object.freeze(['from', 'where'] as [string, string]),
]);

/** How many characters may separate the two tokens of a pair and still read as one clause. */
const STATEMENT_KEYWORD_WINDOW = 120;

/**
 * The named detectors that are not keyword pairs.
 *
 * - The comparison-against-a-literal detector is the one that catches an
 *   INTERPOLATED value specifically, which is the precise defect the redaction
 *   requirement exists for: a where-clause fragment carrying a quoted literal
 *   rather than a placeholder.
 * - The key-material header is assembled from its parts so that no such header
 *   appears in this source.
 * - The long unbroken alphanumeric run catches an encoded credential or key blob.
 *   The threshold is far above the longest word any prose here contains, so it
 *   cannot fire on the projection's own text.
 * - The credential-shaped member names are NAMES, not values. None of them may
 *   appear on this boundary at all: the conflict payload is column data.
 */
const NAMED_LEAK_DETECTORS: readonly (readonly [string, RegExp])[] = Object.freeze([
  Object.freeze([
    'a comparison against a quoted literal, which is an interpolated value rather ' +
      'than a bound parameter',
    /=\s*'/,
  ] as [string, RegExp]),
  Object.freeze([
    'a key-material block header',
    new RegExp(`${'-'.repeat(5)}\\s*(BEGIN|END)`, 'i'),
  ] as [string, RegExp]),
  Object.freeze([
    'an unbroken alphanumeric run long enough to be an encoded credential',
    /[A-Za-z0-9+/]{64,}/,
  ] as [string, RegExp]),
  Object.freeze([
    'a credential-shaped member name',
    /\b(passwd|password|secret|privatekey|private_key|signingkey|signing_key|apikey|api_key|clientsecret|client_secret|authorization|bearer)\b/i,
  ] as [string, RegExp]),
]);

/**
 * Names the first leak detector that matches, or `undefined` when the text is
 * clean.
 *
 * Returns a NAME and never the matched text, so a failing assertion can say what
 * kind of leak it found without republishing it. Case-insensitive throughout,
 * because a statement is not case-sensitive and a redaction that only handled one
 * case would be no redaction at all.
 */
function findLeak(text: string): string | undefined {
  const haystack: string = text.toLowerCase();

  for (const [first, second] of STATEMENT_KEYWORD_PAIRS) {
    const pattern = new RegExp(
      `\\b${first}\\b[\\s\\S]{0,${STATEMENT_KEYWORD_WINDOW}}?\\b${second}\\b`,
    );

    if (pattern.test(haystack)) {
      return `a statement-shaped clause joining '${first}' to '${second}'`;
    }
  }

  for (const [name, pattern] of NAMED_LEAK_DETECTORS) {
    if (pattern.test(text)) {
      return name;
    }
  }

  return undefined;
}

/* ------------------------------------------------------------------------- *
 * The data this spec writes — deterministic, fixture-built, and never a clock
 *
 * Uniqueness comes from a stable test-scoped label rather than from a timestamp or
 * a random source, so two runs produce identical payloads and a recording taken
 * from one is comparable with a master. The row is identified afterwards by the key
 * the ENGINE assigned and never by a label, so re-running against a volume that
 * already holds a previous run's row is expected rather than tolerated.
 *
 * EVERY `address` BELOW IS A LABEL OF AT MOST 37 CHARACTERS, comfortably inside the
 * narrower of the two widths the oracles declare, so the width disagreement between
 * them is never the accidental subject of an assertion here. EVERY `salary` BELOW
 * IS A QUARTER-STEP VALUE, which is exactly representable as a double, so a
 * tolerance failure can never be caused by the fixture's own arithmetic — and the
 * four values are a full quarter apart, fifty times the comparison tolerance, so
 * "the value changed" and "the value did not change" are unambiguously
 * distinguishable.
 * ------------------------------------------------------------------------- */

/** The scope of the row this spec creates. Greppable as `pfw-e2e-conflict-row-*`. */
const ROW_SCOPE = 'conflict-row';

/**
 * The row created through the public workflow in step 1.
 *
 * Every field choice is a constraint being honoured rather than a preference: `id`
 * is absent because `CompanyRowInput` omits it and the engine assigns it; `name` and
 * `age` are non-null because the DDL declares `NOT NULL` on both; `address` is a
 * label, well inside the narrower declared width; `birth` goes through the fixture's
 * formatter, so it is `yyyy-mm-dd` text produced without constructing a date or
 * consulting a locale; and `salary` is exactly representable.
 *
 * `age` sits above the oldest oracle row, so under the DataWindow's
 * `sort="age A salary A "` this row lands after every seeded one. Nothing below
 * asserts on that ordering — it simply keeps this file's row out of the middle of
 * the shared set.
 */
const CREATED_INPUT: CompanyRowInput = buildCompanyRow({
  name: deterministicLabel(ROW_SCOPE, 0),
  age: 43,
  address: deterministicLabel('conflict-created', 0),
  salary: 20000.25,
  birth: formatBirth(1991, 5, 11),
});

/** The `address` the LEGITIMATE update writes. It must survive the refused attempt. */
const APPLIED_ADDRESS: string = deterministicLabel('conflict-applied', 0);

/** The `salary` the LEGITIMATE update writes. A quarter above the created value. */
const APPLIED_SALARY = 20000.5;

/**
 * The `address` the REFUSED update attempts to write. It must NEVER appear in
 * storage.
 *
 * Distinct from every other address in this file precisely so that step 5 can assert
 * its ABSENCE, which is the second half of proving there was no silent overwrite:
 * "the good value is still there" and "the bad value did not land" are two different
 * claims, and only the pair of them excludes a partial write.
 */
const STALE_ADDRESS: string = deterministicLabel('conflict-stale', 0);

/** The `salary` the REFUSED update attempts to write. Must never appear in storage. */
const STALE_SALARY = 20000.75;

/** The `address` the RETRY writes, after refreshing its originals from what is stored. */
const RETRIED_ADDRESS: string = deterministicLabel('conflict-retried', 0);

/** The `salary` the RETRY writes. */
const RETRIED_SALARY = 20001;

/**
 * The textual shape of a `birth` value, DERIVED from the fixture's format
 * description rather than restated.
 *
 * Each letter of the mask stands for one digit, so replacing the letters yields the
 * pattern and the two can never disagree.
 */
const BIRTH_TEXT_PATTERN = new RegExp(`^${BIRTH_FORMAT.replace(/[a-z]/g, '\\d')}$`);

/* ------------------------------------------------------------------------- *
 * State carried between the steps of the workflow
 *
 * Module scope is legitimate here only because the describe block below runs
 * SERIALLY: the steps are one workflow rather than six independent assertions, and
 * each later step needs what an earlier one learned — above all the key the engine
 * assigned, which is the only stable way to address the row. The runner is already
 * configured `workers: 1` and `fullyParallel: false`, so nothing else can interleave.
 * ------------------------------------------------------------------------- */

/**
 * The row as step 1 created it, read back from the RETRIEVAL.
 *
 * THIS SNAPSHOT IS WHAT GOES STALE. Step 2 changes the stored row without touching
 * this value, and step 3 then submits it as the `original` half — which is precisely
 * the condition `updatewhere=1` exists to detect.
 */
let createdSnapshot: CompanyRow | undefined;

/** The created row's own column values, kept verbatim for the legitimate update's originals. */
let createdVerbatimColumns: readonly WireColumnValue[] = [];

/** The row as step 2's legitimate update left it. The state steps 4, 5 and 6 measure against. */
let appliedSnapshot: CompanyRow | undefined;

/**
 * The `409` response, captured in step 3 and asserted on in step 4.
 *
 * The BODY TEXT is carried rather than the response object, for two reasons. A body
 * may be read once, so reading it in the step that asserts the status and parsing it
 * in the step that asserts the payload is the only arrangement in which both steps
 * can be about one response. And carrying text rather than a parsed object is what
 * lets step 4 assert that the body PARSES at all, which is the machine-readability
 * claim — an HTML error page is a failure, and a page cannot be detected by looking
 * at an object somebody has already parsed.
 */
let conflictBodyText: string | undefined;

/** The `409` response's content type, captured alongside the body for the same reason. */
let conflictContentType = '';

/** Fails with a diagnosis rather than a type error when an earlier step did not run. */
function requireCreatedSnapshot(): CompanyRow {
  if (createdSnapshot === undefined) {
    throw new Error(
      'No created row is available. Serial execution guarantees the creating ' +
        'step runs first, so an absence here means that step failed or was ' +
        'skipped — read its failure rather than this one.',
    );
  }

  return createdSnapshot;
}

/** The same, for the state the legitimate update left behind. */
function requireAppliedSnapshot(): CompanyRow {
  if (appliedSnapshot === undefined) {
    throw new Error(
      'No post-update snapshot is available. Serial execution guarantees the ' +
        'legitimate update runs before anything that measures against it, so an ' +
        'absence here means that step failed or was skipped — read its failure ' +
        'rather than this one.',
    );
  }

  return appliedSnapshot;
}

/** The same, for the captured conflict body. */
function requireConflictBodyText(): string {
  if (conflictBodyText === undefined) {
    throw new Error(
      'No conflict body is available. Serial execution guarantees the step that ' +
        'provokes the 409 runs first, so an absence here means that step failed ' +
        'or was skipped — read its failure rather than this one.',
    );
  }

  return conflictBodyText;
}

test.describe('Optimistic-concurrency conflict: HTTP 409, and no silent overwrite (C-06 over C-09)', () => {
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
  // TESTS TAGGED `@no-stack` ARE EXEMPT, and the tag is why this is a tag rather
  // than a title match: several specs mix pure-fixture assertions in with HTTP
  // ones, those assertions are exactly the part that still holds with nothing
  // running, and skipping them would throw away the only coverage available
  // before a bring-up. A tag is declarative and machine-read; a title substring
  // would silently start skipping the moment someone reworded a test name, and
  // two stack-free tests in this suite never carried the wording at all.
  // ---------------------------------------------------------------------------
  test.beforeEach(async ({}, testInfo) => {
    if (testInfo.tags.includes('@no-stack')) {
      return;
    }

    const availability = await probeStackAvailability();

    test.skip(!availability.reachable, availability.reason);
  });

  // TWO FLAGS, AND BOTH ARE LOAD-BEARING.
  //
  // `mode: 'serial'` because this is one multi-step MUTATION SEQUENCE over shared
  // COMPANY state, not six independent assertions: a conflict is a function of a
  // known row state, so a later step is meaningless if an earlier one failed. The
  // benefit is diagnostic as well — when a step fails, the steps that depended on
  // it are reported as SKIPPED instead of producing a cascade of derived failures
  // that all have the same single cause.
  //
  // `retries: 0` as a DELIBERATE LOCAL PIN, even though the runner is already
  // configured with zero retries globally. The conflict assertion below must never
  // be retried into a pass: a stale update MUST fail, because there is no silent
  // overwrite anywhere in this system, and a retry count introduced repository-wide
  // for CI flake at some later date would silently turn that from an assertion into
  // a coin toss. The runner configuration names this file as one of the two that
  // must pin it back locally, so this line is that pin. It is the only kind of
  // "retry" permitted anywhere near this file — a retry of the WORKFLOW in step 6
  // is a different thing entirely, and a retry of an ASSERTION remains forbidden.
  test.describe.configure({ mode: 'serial', retries: 0 });

  test('step 1 — a known starting row is created through the public workflow', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    // CREATED THROUGH THE PUBLIC WORKFLOW, NEVER SEEDED. Storage is never touched
    // directly and the volume is never reset, reseeded, dropped or recreated: the
    // paired-capture rule forbids recreating or reseeding it between the two halves
    // of a comparison, so the only legitimate way to obtain a KNOWN row state is to
    // create one through the ingress. A pre-existing row is not a known state — the
    // volume is shared and a previous run's row carries this same deterministic
    // label — which is why nothing below looks the row up by name.
    const response: APIResponse = await requestUpdate(request, token, [
      encodeInsertRow(CREATED_INPUT),
    ]);

    expect(
      response.status(),
      `${ROUTES.update} must answer 200 for a well-formed insert. A ${CONFLICT_STATUS} ` +
        'here would be an optimistic-concurrency report against a row that has no ' +
        'prior state to conflict with, which would mean originals were being ' +
        'compared for an insert; a 400 means the payload was refused by binding or ' +
        'by validation, and a 401 that the token was not accepted.',
    ).toBe(200);

    const body: unknown = await response.json();

    const retCode: unknown = readMember(body, RESPONSE_KEYS.retCode);

    if (retCode !== undefined) {
      expect(
        isZeroRetCode(retCode),
        'an applied insert must report the zero return code. Asserted as ZERO ' +
          'specifically rather than through a success test, because a prevention is ' +
          '1 and reads as a success under the legacy predicate while a cancellation ' +
          'is neither succeeded nor failed — both are preserved behaviours and ' +
          'neither is what an applied write reports.',
      ).toBe(true);
    }

    // REQUIRED, NOT CONDITIONAL — a contract distinction rather than a strictness
    // preference. The canonical protobuf JSON mapping omits a field only when it
    // holds its type's DEFAULT, so the single omission this projection may
    // legitimately produce for an int64 is zero. Exactly one row was submitted, so
    // the only correct value is 1 and 1 is not omissible. An absent member means
    // either that nothing was inserted despite the success status, or that the
    // projection dropped a count the caller needs — and this file needs it more
    // than most, because every later step addresses the row this step created.
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

    // THE IDENTITY ROUND-TRIP. The engine assigns the key, so the caller learns it
    // from the response rather than choosing it — which is the whole reason the
    // insert payload omits the column, and the reason every later step in this file
    // can address one specific row in a table it does not own.
    const identityEntries: readonly unknown[] | undefined = readArray(
      body,
      RESPONSE_KEYS.identity,
    );

    expect(
      identityEntries,
      'an insert must report the identity data. Without it this workflow cannot ' +
        'address the row it just created, and a conflict spec that cannot name its ' +
        'own row cannot assert anything about concurrency at all.',
    ).toBeDefined();

    const entries: readonly unknown[] = identityEntries ?? [];

    expect(
      entries.length,
      'the identity report must carry at least one entry for an inserted row',
    ).toBeGreaterThan(0);

    const entry: unknown = entries[0];

    expect(
      readInt64(entry, RESPONSE_KEYS.identityColumnId),
      'the identity column reported must be the one the DataWindow declares as the ' +
        'identity column, addressed by its one-based ordinal',
    ).toBe(columnOrdinal('id'));

    // The PRIMARY buffer's values. The response carries a second array collected
    // from the filter buffer BACKWARDS, because that buffer's order is inverted
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
      'the assigned key must come back as a readable 64-bit integer. It is declared ' +
        'int64 on the contract and the canonical mapping emits such a value as a ' +
        'decimal string, so a value arriving as a number beyond the ' +
        'exactly-representable range has already been rounded and is refused rather ' +
        'than accepted as plausible — a rounded key here would make every later ' +
        'step address the wrong row and would report as a phantom conflict.',
    ).toBeDefined();

    const assignedIdentity: number = Number(identity);

    expect(
      assignedIdentity,
      'the assigned key must be a positive integer — an auto-increment rowid starts ' +
        'at 1, so a zero or negative value means no key was assigned',
    ).toBeGreaterThan(0);

    // Read the row back, and CAPTURE THE FULL SIX-COLUMN SNAPSHOT. Built from what
    // the retrieval answered rather than from what was sent, because any subsequent
    // update's WHERE clause has to match what is STORED. This snapshot is the value
    // step 2 makes stale and step 3 submits.
    const rows: readonly unknown[] = await retrieveRows(request, token);
    const created: unknown = findRowByIdentity(rows, assignedIdentity);

    expect(
      created,
      'the created row must be retrievable by the key the engine assigned. Its ' +
        'absence means the insert reported success without the write reaching ' +
        'storage, which would make every concurrency assertion below meaningless.',
    ).toBeDefined();

    expect(readColumnText(created, 'name'), 'name must round-trip unchanged').toBe(
      CREATED_INPUT.name,
    );

    expect(readColumnNumber(created, 'age'), 'age must round-trip unchanged').toBe(
      CREATED_INPUT.age,
    );

    // Compared as sent. NO LENGTH ASSERTION AT 50 OR AT 200 is made here or anywhere
    // else in this file: the width disagreement between the two oracles is a
    // preserved defect, not a validation rule, and SQLite constrains neither
    // declaration anyway.
    expect(readColumnText(created, 'address'), 'address must round-trip unchanged').toBe(
      CREATED_INPUT.address,
    );

    // Compared as a STRING, because the column is TEXT in storage while the
    // DataWindow declares a date — a preserved mismatch, so the text actually
    // written is the whole of the value's semantics.
    const birth: string | undefined = readColumnText(created, 'birth');

    expect(birth, 'birth must round-trip unchanged, as text').toBe(CREATED_INPUT.birth);

    expect(
      BIRTH_TEXT_PATTERN.test(birth ?? ''),
      'birth must come back in the layout the DataWindow edit mask declares; the ' +
        'pattern is derived from that same format description rather than restated',
    ).toBe(true);

    // WITHIN A TOLERANCE, NEVER EXACT: a two-place decimal stored in a REAL column
    // is not guaranteed to return an identical double.
    const salary: number | undefined = readColumnNumber(created, 'salary');

    expect(salary, 'salary must round-trip as a readable number').toBeDefined();

    expect(
      Math.abs(Number(salary) - Number(CREATED_INPUT.salary)),
      'salary must round-trip within the fixture tolerance, compared with a ' +
        'tolerance rather than exactly because the DataWindow declares a two-place ' +
        'decimal over a floating-point column',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    createdVerbatimColumns = verbatimColumnsFrom(created);
    createdSnapshot = snapshotOf(created, assignedIdentity);

    // The snapshot must be a faithful record of what is stored, because everything
    // that follows is built from it. A snapshot that had silently substituted a
    // default would make step 3 fail for a reason that had nothing to do with
    // concurrency.
    expect(
      createdSnapshot.name,
      'the captured snapshot must carry the stored name; it is the value step 3 ' +
        'falsifies to provoke the conflict',
    ).toBe(CREATED_INPUT.name);

    expect(
      createdVerbatimColumns.length,
      'the verbatim column set must cover every marked column, because it is what ' +
        "the legitimate update sends as its originals — and under updatewhere=1 the " +
        'generated where-clause compares all six of them',
    ).toBe(MARKED_COLUMNS.length);
  });

  test('step 2 — a legitimate update applies, which makes the captured snapshot stale', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);
    const baseline: CompanyRow = requireCreatedSnapshot();

    // `asUpdate` produces the {current, original} PAIR the contract needs, with the
    // original half being the row exactly as retrieved — so the generated
    // where-clause matches what is stored and the update applies. This is the
    // SUCCESS-path fixture; its sibling that falsifies the originals belongs to step
    // 3 and is deliberately not used here.
    const update: CompanyRowUpdate = asUpdate(baseline, {
      address: APPLIED_ADDRESS,
      salary: APPLIED_SALARY,
    });

    expect(
      update.original.address,
      'the original half must still hold the value as retrieved; if a change leaked ' +
        'into it, the where-clause would be built from the new value and would match ' +
        'nothing — which would produce a conflict in the step that is supposed to ' +
        'succeed',
    ).toBe(baseline.address);

    const response: APIResponse = await requestUpdate(request, token, [
      encodeUpdateRow(update, createdVerbatimColumns),
    ]);

    expect(
      response.status(),
      `${ROUTES.update} must answer 200 for an update whose originals match what is ` +
        `stored. A ${CONFLICT_STATUS} here would report a mismatch on a row nothing ` +
        'else has touched — under a single worker with no parallelism that points at ' +
        'the originals in the payload rather than at a genuine concurrent write, and ' +
        'it would leave the conflict this file is about untested.',
    ).toBe(200);

    const body: unknown = await response.json();

    const retCode: unknown = readMember(body, RESPONSE_KEYS.retCode);

    if (retCode !== undefined) {
      expect(
        isZeroRetCode(retCode),
        'an applied update must report the zero return code. The legacy is defensive ' +
          'about exactly this: it REWRITES a claimed success into a failure when the ' +
          'transaction reports an error, so a success here has to be the ' +
          "transaction's answer and not merely the update call's.",
      ).toBe(true);
    }

    // REQUIRED, NOT CONDITIONAL, for the reason recorded on the inserted-row count
    // in step 1 — and it matters most precisely here. This step exists to make the
    // captured snapshot STALE, so a count of zero, or an absent count standing for
    // zero, would mean the row was never moved and the conflict step that follows
    // would be asserting against a row that had not changed. A conditional guard
    // would have let that pass as a green workflow proving nothing.
    const updatedCount: number | undefined = readInt64(body, RESPONSE_KEYS.rowsUpdated);

    expect(
      updatedCount,
      'the response must report the updated-row count. One row was submitted, so ' +
        'the count is non-zero and cannot be a permissible protobuf-default ' +
        'omission: its absence means the update matched no row while still ' +
        'reporting success, which would leave the following conflict step ' +
        'measuring a row nothing had moved.',
    ).toBeDefined();

    expect(updatedCount, 'exactly one row was submitted for update').toBe(1);

    // Re-read and confirm. A read-back is what distinguishes an update that was
    // APPLIED from one that was merely accepted — and it is what establishes the
    // state steps 4, 5 and 6 measure against.
    const rows: readonly unknown[] = await retrieveRows(request, token);
    const stored: unknown = findRowByIdentity(rows, baseline.id);

    expect(
      stored,
      'the updated row must still be retrievable by its key. Its disappearance would ' +
        'mean the update executed as a delete-plus-insert against a new key, which is ' +
        'the documented consequence of a KEY change — and nothing in this file ' +
        'changes the key.',
    ).toBeDefined();

    expect(
      readColumnText(stored, 'address'),
      'address must now hold the new value; the update changed it, and that change ' +
        'is what makes the snapshot captured in step 1 stale',
    ).toBe(APPLIED_ADDRESS);

    const salary: number | undefined = readColumnNumber(stored, 'salary');

    expect(salary, 'salary must be readable after the update').toBeDefined();

    expect(
      Math.abs(Number(salary) - APPLIED_SALARY),
      'salary must now hold the new value, compared within the fixture tolerance ' +
        'for the same reason as on insert',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    // The columns this update did not name must be untouched. An update that
    // rewrote an unmentioned column would itself be a silent overwrite.
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

    appliedSnapshot = snapshotOf(stored, baseline.id);

    // THE POINT OF THIS STEP, STATED AS AN ASSERTION. The snapshot step 1 captured
    // no longer describes the stored row, which is exactly the precondition step 3
    // needs: under updatewhere=1 the where-clause is built from the original values,
    // so a payload carrying step 1's originals can now match nothing.
    expect(
      appliedSnapshot.address,
      "the stored row must now differ from step 1's snapshot. If the two still " +
        'agreed, the payload step 3 sends would not be stale and its 409 assertion ' +
        'would be testing nothing.',
    ).not.toBe(baseline.address);
  });

  test(`step 3 — a stale-original update is refused with HTTP ${CONFLICT_STATUS}`, async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    // THE STALE ORIGINALS COME FROM THE FIXTURE, NEVER FROM HERE. `asStaleUpdate` is
    // the one place in this suite where a stale original set may be constructed, and
    // it exists for this file. It falsifies a NON-KEY column only — so the row is
    // still FOUND by its key and then REJECTED on its original values, which is the
    // branch under test; corrupting the key instead would produce a
    // row-does-not-exist outcome, a different condition entirely. It also refuses to
    // build a payload whose staleness would be a no-op, which is the false pass this
    // step is most exposed to.
    //
    // The originals are additionally still those of step 1, which step 2 has just
    // made obsolete — so the payload is stale twice over, by the sentinel AND by the
    // address and salary the legitimate update rewrote.
    const staleUpdate: CompanyRowUpdate = asStaleUpdate(requireCreatedSnapshot(), {
      address: STALE_ADDRESS,
      salary: STALE_SALARY,
    });

    expect(
      staleUpdate.original.name,
      'the fixture must have falsified a non-key original. That falsified value is ' +
        'the mismatch the where-clause cannot satisfy, and it is what the conflict ' +
        "detail's original side must echo back in step 4.",
    ).toBe(STALE_ORIGINAL_SENTINEL);

    expect(
      staleUpdate.current.id,
      'the key must be untouched, so the row is found and then refused on its ' +
        'original values rather than simply not found',
    ).toBe(requireCreatedSnapshot().id);

    // NO VERBATIM ORIGINALS HERE, DELIBERATELY. Passing the server's own values
    // would overwrite the staleness with the truth, the update would apply, and this
    // assertion would fail while reporting nothing about the system.
    const response: APIResponse = await requestUpdate(request, token, [
      encodeUpdateRow(staleUpdate),
    ]);

    // Captured BEFORE any assertion that could end the test, so step 4 has the body
    // even when the status assertion below is the thing that fails. A body may be
    // read only once, which is why it is read here rather than in step 4.
    conflictContentType = (response.headers()['content-type'] ?? '').toLowerCase();
    conflictBodyText = await response.text();

    // THE ASSERTION THIS WHOLE FILE EXISTS FOR.
    //
    // Strict equality against one number, and never a range: a caller's
    // retry-or-surface branch keys off exactly this code, so a 400 or a 412 would
    // send a correct caller down the wrong path even though both are "some 4xx". A
    // 200, a 201 or a 204 would be the SILENT OVERWRITE the contract forbids
    // outright — a write applied whose optimistic precondition could not have held.
    //
    // THERE IS NO RETRY, NO POLL AND NO SECOND ATTEMPT HERE, and that is a
    // requirement rather than an omission. Retrying a conflict assertion is precisely
    // how a silent overwrite would slip through undetected, so the runner's retry
    // count is pinned to zero for this describe block and nothing in this file wraps
    // the request in a wait-and-try-again.
    expect(
      response.status(),
      `${ROUTES.update} must answer exactly ${CONFLICT_STATUS} for an update whose ` +
        'original values are stale. Persistence answers gRPC Aborted on an ' +
        'updatewhereclause mismatch, DataServices relays it and Gateway projects it ' +
        `as ${CONFLICT_STATUS} — the canonical mapping. A 2xx here would mean the ` +
        'stale write was APPLIED, which is the silent overwrite this system forbids ' +
        'absolutely; a 400 would mean the payload was rejected as malformed rather ' +
        'than recognised as a conflict, and a caller could not distinguish "retry ' +
        'after re-reading" from "this request can never work".',
    ).toBe(CONFLICT_STATUS);

    expect(
      response.ok(),
      'the refused update must not report success. Asserted alongside the status ' +
        'rather than instead of it: the status is what a caller branches on, and this ' +
        'is the coarser guarantee that no success path was taken.',
    ).toBe(false);
  });

  test('step 4 — the conflict payload carries both value sets for all six marked columns', async () => {
    // NO NETWORK IN THIS STEP. It asserts on the response step 3 captured, so the
    // conflict is provoked exactly once: a second request would be a second attempt
    // at the assertion, which is the thing this file must never do.
    const bodyText: string = requireConflictBodyText();
    const baseline: CompanyRow = requireAppliedSnapshot();

    // MACHINE-READABLE, NOT A PAGE. This is the migration of the legacy MessageBox
    // surface into a structured error result: the text, the category, the
    // substitution arguments and the severity are preserved and only the DELIVERY
    // CHANNEL changed. A caller must be able to branch on the body without parsing
    // prose out of markup, and a developer-exception page could carry a stack trace
    // or a file path, neither of which may reach a caller.
    expect(
      conflictContentType,
      'the conflict must be machine-readable JSON — a problem document, which is the ' +
        'shape every refusal on this boundary uses so that one error handler serves ' +
        'them all',
    ).toContain('json');

    expect(
      conflictContentType,
      'the conflict must not be an HTML page. A page here would mean the failure ' +
        'escaped the contract error path entirely.',
    ).not.toContain('text/html');

    let parsed: unknown;
    let parseFailed = false;

    try {
      parsed = JSON.parse(bodyText) as unknown;
    } catch {
      // The parser error is deliberately not attached and the body is deliberately
      // not quoted: this is the one payload in the suite that is being checked for
      // leaked material, so republishing it in a failure message would be the leak.
      parseFailed = true;
    }

    expect(
      parseFailed,
      'the conflict body must parse as JSON. A body that does not parse cannot be ' +
        'branched on, so a caller could not implement the retry-or-surface policy at ' +
        'all. The parser error is omitted from this message on purpose — the body is ' +
        'the thing under examination for leaked material.',
    ).toBe(false);

    expect(
      asRecord(parsed),
      'the conflict body must be a JSON object. An array or a bare string could not ' +
        'carry the members a consumer branches on.',
    ).toBeDefined();

    // The problem document's own members, and the status repeated inside it.
    const declaredMembers: readonly string[] = RESPONSE_KEYS.problemMembers.filter(
      (member: string) => readMember(parsed, [member]) !== undefined,
    );

    expect(
      declaredMembers.length,
      'the conflict must carry at least one of the standard problem members, so that ' +
        'one error handler serves every failure on this boundary',
    ).toBeGreaterThan(0);

    const declaredStatus: number | undefined = readInt64(parsed, RESPONSE_KEYS.problemStatus);

    if (declaredStatus !== undefined) {
      expect(
        declaredStatus,
        'the status repeated in the body must agree with the transport status. The ' +
          'repetition exists so a logged body is self-describing, and a disagreement ' +
          'would make the record misleading rather than redundant.',
      ).toBe(CONFLICT_STATUS);
    }

    const problemType: unknown = readMember(parsed, RESPONSE_KEYS.problemType);

    // THE CONFLICT DETAIL ITSELF. Required by the contract on this body, so an
    // absence is a finding rather than a tolerance — and the diagnosis names the one
    // legitimate shape that carries no such member, because that shape is still a
    // failure of THIS workflow: it means the upstream reported a conflict it could
    // not describe, and a caller cannot rebase against a description it never got.
    const conflict: unknown = readMember(parsed, RESPONSE_KEYS.conflict);

    expect(
      asRecord(conflict),
      'the conflict body must carry the conflict detail. It is the one member a ' +
        'caller must be able to read in order to ACT: without it a caller cannot see ' +
        'which column moved, so it would re-send the same stale originals and receive ' +
        `the same ${CONFLICT_STATUS} for ever. If the problem type instead names the ` +
        'without-detail projection, the status was preserved correctly but the ' +
        'upstream attached nothing decodable — which is an upstream fault to fix, not ' +
        `a tolerated shape. Problem type present: ${typeof problemType === 'string'}.`,
    ).toBeDefined();

    const conflictRows: readonly unknown[] | undefined = readArray(
      conflict,
      RESPONSE_KEYS.rows,
    );

    expect(
      conflictRows,
      'the conflict detail must carry its rows collection. It is a collection rather ' +
        'than a single row because one update submits a whole changeset and more than ' +
        'one row can conflict, and reporting only the first would send a caller round ' +
        'the retry loop once per conflicting row.',
    ).toBeDefined();

    expect(
      (conflictRows ?? []).length,
      'the conflict detail must never be empty. A conflict with no rows tells a ' +
        'caller nothing it can act on, which is the same as not reporting it.',
    ).toBeGreaterThan(0);

    const conflictRow: unknown = (conflictRows ?? [])[0];

    // The DataWindow-carrier members. Their presence is what makes this a buffer-
    // shaped payload rather than a flat rowset — the distinction the whole contract
    // rests on.
    expect(
      readMember(conflictRow, RESPONSE_KEYS.buffer),
      'the conflicting row must name the buffer it is in. Silence would read as the ' +
        'primary buffer, which is a specific buffer a caller is entitled to believe — ' +
        "and the filter buffer's row order is inverted relative to the source, so a " +
        'mis-tagged row would be counted in the wrong direction.',
    ).toBeDefined();

    expect(
      readMember(conflictRow, RESPONSE_KEYS.itemStatus),
      'the conflicting row must carry its item status as the SERVER sees it now, read ' +
        'the legacy way as the row itself rather than as a column',
    ).toBeDefined();

    const rowOrdinal: number | undefined = readInt64(conflictRow, RESPONSE_KEYS.rowOrdinal);

    if (rowOrdinal !== undefined) {
      expect(
        rowOrdinal,
        'the row ordinal must be one-based, like every row ordinal in this system',
      ).toBeGreaterThanOrEqual(1);
    }

    // BOTH VALUE SETS, FOR ALL SIX MARKED COLUMNS. This is the substance of the
    // payload: `updatewhere=1` with all six columns marked means the failed
    // where-clause compared six original values, so a payload that carried fewer —
    // or that carried only current values — could not express what was compared and
    // a caller could not tell which column moved.
    const currentValues: readonly unknown[] | undefined = readArray(
      conflictRow,
      RESPONSE_KEYS.conflictCurrentValues,
    );

    const originalValues: readonly unknown[] | undefined = readArray(
      conflictRow,
      RESPONSE_KEYS.originalValues,
    );

    expect(
      currentValues,
      'the conflicting row must carry the CURRENT server-side values — the state a ' +
        'retry would be rebased onto',
    ).toBeDefined();

    expect(
      originalValues,
      'the conflicting row must carry the ORIGINAL values the caller believed were ' +
        'current — the set that formed the failed where-clause, and the only thing ' +
        'that lets a caller identify which column changed underneath it',
    ).toBeDefined();

    // Iterated per column and per side, so a partial-payload regression names the
    // exact column and side that went missing. A count assertion would report "five
    // instead of six" and leave a reader to work out which five.
    for (const column of MARKED_COLUMNS) {
      expect(
        findColumnEntry(currentValues, column),
        `the conflict detail must report a CURRENT value for '${column}'. All six ` +
          'columns are marked updatewhereclause=yes, so all six participate in the ' +
          'comparison and all six have to be reportable; a caller missing this one ' +
          'could not rebase it.',
      ).toBeDefined();

      expect(
        findColumnEntry(originalValues, column),
        `the conflict detail must report an ORIGINAL value for '${column}'. Under ` +
          'updatewhere=1 the where-clause carried the original value of every marked ' +
          'column, so omitting this one would hide half of what the failed statement ' +
          'actually compared.',
      ).toBeDefined();
    }

    // THE DETAIL MUST REPORT REALITY, NOT ECHO THE CALLER'S STALE INPUT. This is the
    // assertion that distinguishes a conflict payload a caller can rebase against
    // from one that merely reflects the request back.
    expect(
      textOfValue(findColumnValue(currentValues, 'address')),
      "the current side must report what is actually stored — the legitimate update's " +
        'value. Reporting the value the refused request tried to write would make the ' +
        'payload an echo of the caller and useless for a rebase.',
    ).toBe(baseline.address);

    const reportedSalary: number | undefined = numberOfValue(
      findColumnValue(currentValues, 'salary'),
    );

    expect(reportedSalary, 'the current side must report a readable salary').toBeDefined();

    expect(
      Math.abs(Number(reportedSalary) - APPLIED_SALARY),
      'the current side must report the stored salary within the fixture tolerance, ' +
        'compared with a tolerance rather than exactly for the same reason as ' +
        'everywhere else: a two-place decimal in a REAL column',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    expect(
      textOfValue(findColumnValue(currentValues, 'name')),
      'the current side must report the stored name, which the refused request never ' +
        'changed. It must NOT be the falsified original: that value is the mismatch, ' +
        'and finding it on the current side would mean the server was reporting the ' +
        "caller's belief as the truth.",
    ).toBe(baseline.name);

    // The other side of the same coin: the ORIGINAL side must carry the falsified
    // value, because that is what the failed where-clause compared. Together with
    // the assertion above, this is what proves the two sides are genuinely distinct
    // and that the payload identifies the column that moved.
    expect(
      textOfValue(findColumnValue(originalValues, 'name')),
      'the original side must echo the value the request believed was current. ' +
        'Comparing it against the current side is what tells a caller which column ' +
        'moved, so the two sides carrying identical values would defeat the entire ' +
        'diagnostic purpose of the payload.',
    ).toBe(STALE_ORIGINAL_SENTINEL);

    // Which table the failing statement targeted. Present because MULTI-TABLE UPDATE
    // FROM ONE DATAWINDOW IS A REAL LEGACY CAPABILITY — the update contract is
    // re-derived at run time from an ARRAY of table descriptors rather than from the
    // DataWindow's static definition — so "which table" is a genuine question.
    const updateTable: unknown = readMember(conflict, RESPONSE_KEYS.conflictUpdateTable);

    expect(
      updateTable,
      'the conflict detail must name the table the failing statement targeted',
    ).toBeDefined();

    if (typeof updateTable === 'string' && updateTable.length > 0) {
      expect(
        updateTable.toLowerCase(),
        'the targeted table must be the one this workflow updates. Compared without ' +
          'regard to case, because the oracle writes the table name in upper case in ' +
          'its DDL and in lower case in its column metadata, and identifiers are ' +
          'case-insensitive in this engine.',
      ).toBe(COMPANY_TABLE_NAME.toLowerCase());
    }

    // The size of the mismatch, not merely its existence. Reporting both numbers is
    // what keeps "the row changed" distinguishable from "the row was deleted"
    // without a second round trip.
    //
    // THE GAP IS ASSERTED, NOT THE TWO NUMBERS. The contract documents 1 and 0 for
    // the classic single-row failure, but the invariant that DEFINES a mismatch is
    // that fewer rows matched than were expected — and asserting the invariant rather
    // than the pair avoids pinning a number the projection is entitled to report
    // differently for a larger changeset.
    const rowsExpected: number | undefined = readInt64(
      conflict,
      RESPONSE_KEYS.conflictRowsExpected,
    );

    const rowsMatched: number | undefined = readInt64(conflict, RESPONSE_KEYS.conflictRowsMatched);

    expect(
      rowsExpected,
      'the conflict detail must report how many rows the statement expected to affect',
    ).toBeDefined();

    expect(
      rowsMatched,
      'the conflict detail must report how many rows it actually matched',
    ).toBeDefined();

    expect(
      Number(rowsExpected),
      'a statement that expected to affect no rows could not have produced a ' +
        'concurrency mismatch',
    ).toBeGreaterThanOrEqual(1);

    expect(
      Number(rowsMatched),
      'fewer rows must have matched than were expected. That gap IS the optimistic-' +
        'concurrency mismatch: the where-clause carried original values that no ' +
        'stored row satisfies any longer.',
    ).toBeLessThan(Number(rowsExpected));

    // The legacy return code, read for its TYPE and not for its value. The value is
    // deliberately unasserted: the tri-state algebra means a two-way test over this
    // field would misclassify both a prevention and a cancellation, and the plan of
    // record fixes the STATUS for this outcome rather than a particular code.
    const problemRetCode: unknown = readMember(parsed, RESPONSE_KEYS.retCode);

    if (problemRetCode !== undefined) {
      expect(
        typeof problemRetCode,
        'the legacy return code is an integer extension member on the problem ' +
          'document, so a consumer can identify the specific legacy outcome and not ' +
          'only the HTTP class',
      ).toBe('number');
    }

    // NO STATEMENT TEXT, NO CREDENTIAL-SHAPED MATERIAL. The legacy error structure's
    // statement member carries the complete generated statement including
    // interpolated literal values, and the legacy logger performs no redaction at
    // all — so the projection redacts or parameter-separates it. This enforces that
    // rather than assuming it.
    //
    // The detector reports its NAME and never the text it matched, because a failure
    // message reaches the console and the CI log and republishing the payload would
    // be the very leak being detected.
    const leak: string | undefined = findLeak(bodyText);

    expect(
      leak,
      `the conflict body must not leak statement text or credential-shaped material; ` +
        `a detector fired for ${leak ?? 'nothing'}. The legacy statement field carries ` +
        'interpolated literal values against a logger that performs no redaction, so ' +
        'the projection has to redact or parameter-separate it before it reaches a ' +
        'caller. The matched text is deliberately not reproduced here.',
    ).toBeUndefined();
  });

  test('step 5 — the refused update changed nothing: there was no silent overwrite', async ({
    request,
  }) => {
    // THE ASSERTION THE WHOLE FILE EXISTS FOR, in its positive and negative forms.
    // A rejection that still wrote something would be the worst of both worlds: the
    // caller is told to retry while the data has already been changed underneath it.
    const token: ServiceToken = await requireServiceToken(request);
    const baseline: CompanyRow = requireAppliedSnapshot();

    const rows: readonly unknown[] = await retrieveRows(request, token);
    const stored: unknown = findRowByIdentity(rows, baseline.id);

    expect(
      stored,
      'the row must still exist after the refused update. Its disappearance would ' +
        'mean the rejected statement had nonetheless executed as a delete, which is ' +
        'the most destructive outcome a conflict path could produce.',
    ).toBeDefined();

    // DIRECTION ONE: the good value is still there.
    expect(
      readColumnText(stored, 'address'),
      "address must still hold the legitimate update's value. Anything else means the " +
        `refused request wrote to storage despite answering ${CONFLICT_STATUS}.`,
    ).toBe(baseline.address);

    const salary: number | undefined = readColumnNumber(stored, 'salary');

    expect(salary, 'salary must still be readable').toBeDefined();

    expect(
      Math.abs(Number(salary) - APPLIED_SALARY),
      "salary must still hold the legitimate update's value, within the fixture " +
        'tolerance',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    // DIRECTION TWO: the bad value did not land. Both directions are asserted because
    // neither implies the other — a partial write could leave the good value in one
    // column and the bad value in another, and only checking both excludes it.
    expect(
      readColumnText(stored, 'address'),
      'address must NOT hold the value the refused request tried to write. This is ' +
        'the silent overwrite the contract forbids absolutely, stated as its own ' +
        'assertion rather than inferred from the one above.',
    ).not.toBe(STALE_ADDRESS);

    expect(
      Math.abs(Number(salary) - STALE_SALARY),
      'salary must NOT have moved towards the value the refused request tried to ' +
        'write. The two candidate values are a full quarter apart — fifty times the ' +
        'comparison tolerance — so this cannot pass or fail by rounding.',
    ).toBeGreaterThan(SALARY_COMPARISON_TOLERANCE);

    // The columns the refused request did not name must be untouched as well, and
    // `name` matters most: the falsified ORIGINAL was a name, so a boundary that had
    // confused the two value sets would have written the sentinel here.
    expect(
      readColumnText(stored, 'name'),
      'name must be unchanged. The refused payload carried the falsified sentinel as ' +
        'an ORIGINAL value, so finding it stored would mean the boundary had ' +
        'confused the original set with the current set — a defect that would corrupt ' +
        'data while reporting a conflict.',
    ).toBe(baseline.name);

    expect(
      readColumnText(stored, 'name'),
      'name must not have been overwritten with the falsified original value',
    ).not.toBe(STALE_ORIGINAL_SENTINEL);

    expect(readColumnNumber(stored, 'age'), 'age must be unchanged').toBe(baseline.age);

    const birth: string | undefined = readColumnText(stored, 'birth');

    expect(birth, 'birth must be unchanged, and still text').toBe(baseline.birth);

    expect(
      BIRTH_TEXT_PATTERN.test(birth ?? ''),
      'birth must still be in the layout the edit mask declares',
    ).toBe(true);
  });

  test('step 6 — the conflict is recoverable by an explicit refresh-and-retry', async ({
    request,
  }) => {
    // THE OTHER ARM OF THE POLICY. The contract requires a DEFINED
    // retry-or-surface policy rather than a bare rejection, so the retry arm is
    // demonstrated to resolve cleanly: re-read, rebase on what is actually stored,
    // and resubmit. That is also why the upstream status is Aborted rather than
    // FailedPrecondition — Aborted means the operation MAY succeed if retried at a
    // higher level, and this step is that higher level.
    //
    // THIS IS A DELIBERATE RETRY OF THE WORKFLOW, NOT OF AN ASSERTION. The two are
    // different in kind: the workflow retry re-reads first and therefore submits a
    // DIFFERENT payload, whereas a runner retry would re-run the same assertion
    // against the same conditions and could convert a real failure into a pass. The
    // second remains forbidden, and is pinned off for this describe block.
    const token: ServiceToken = await requireServiceToken(request);
    const previous: CompanyRow = requireAppliedSnapshot();

    // Re-read FIRST. Re-sending the same payload would produce the same conflict,
    // because the original values it carries are still stale — the contract says so
    // explicitly, and a retry that skipped the re-read would be a busy loop rather
    // than a policy.
    const beforeRows: readonly unknown[] = await retrieveRows(request, token);
    const current: unknown = findRowByIdentity(beforeRows, previous.id);

    expect(
      current,
      'the row must be re-readable before a retry. A retry is only well-defined ' +
        'against a freshly read state.',
    ).toBeDefined();

    const refreshed: CompanyRow = snapshotOf(current, previous.id);
    const refreshedVerbatimColumns: readonly WireColumnValue[] = verbatimColumnsFrom(current);

    expect(
      refreshedVerbatimColumns.length,
      'the refreshed originals must cover every marked column, because all six are ' +
        'compared',
    ).toBe(MARKED_COLUMNS.length);

    // Rebased on what is ACTUALLY stored, so the where-clause matches. Built with
    // `asUpdate` from the freshly read row — never with the stale-original builder,
    // which exists only for step 3.
    const retry: CompanyRowUpdate = asUpdate(refreshed, {
      address: RETRIED_ADDRESS,
      salary: RETRIED_SALARY,
    });

    const response: APIResponse = await requestUpdate(request, token, [
      encodeUpdateRow(retry, refreshedVerbatimColumns),
    ]);

    expect(
      response.status(),
      `${ROUTES.update} must answer 200 once the originals have been refreshed from ` +
        'storage. This is the retry arm of the retry-or-surface policy, and it is why ' +
        'the upstream status is Aborted rather than FailedPrecondition: the operation ' +
        `may succeed when retried at a higher level. A second ${CONFLICT_STATUS} here ` +
        'would mean the conflict is not recoverable by re-reading, which would leave a ' +
        'caller with no path forward at all.',
    ).toBe(200);

    const body: unknown = await response.json();

    const retCode: unknown = readMember(body, RESPONSE_KEYS.retCode);

    if (retCode !== undefined) {
      expect(
        isZeroRetCode(retCode),
        'the retried update must report the zero return code. The legacy rewrites a ' +
          'claimed success into a failure when the transaction disagrees, so a success ' +
          "here has to be the transaction's answer.",
      ).toBe(true);
    }

    // Confirm the change LANDED. An accepted retry that changed nothing would be as
    // wrong as a rejected update that changed something.
    const afterRows: readonly unknown[] = await retrieveRows(request, token);
    const stored: unknown = findRowByIdentity(afterRows, previous.id);

    expect(stored, 'the row must still be retrievable after the retry').toBeDefined();

    expect(
      readColumnText(stored, 'address'),
      'address must now hold the retried value, which proves the conflict was ' +
        'recoverable by an explicit refresh rather than only reportable',
    ).toBe(RETRIED_ADDRESS);

    const salary: number | undefined = readColumnNumber(stored, 'salary');

    expect(salary, 'salary must be readable after the retry').toBeDefined();

    expect(
      Math.abs(Number(salary) - RETRIED_SALARY),
      'salary must now hold the retried value, within the fixture tolerance',
    ).toBeLessThanOrEqual(SALARY_COMPARISON_TOLERANCE);

    expect(
      readColumnText(stored, 'address'),
      'address must still not hold the value the refused request tried to write. The ' +
        'retry resolved the conflict on its own terms; it did not retroactively let ' +
        'the rejected payload through.',
    ).not.toBe(STALE_ADDRESS);

    expect(readColumnText(stored, 'name'), 'name was not changed by the retry').toBe(
      refreshed.name,
    );

    expect(readColumnNumber(stored, 'age'), 'age was not changed by the retry').toBe(
      refreshed.age,
    );

    expect(readColumnText(stored, 'birth'), 'birth was not changed by the retry').toBe(
      refreshed.birth,
    );

    // THE ROW IS LEFT IN PLACE. Not deleted, not reset, not reseeded, and there is no
    // cleanup hook in this file. The paired-capture rule forbids recreating or
    // reseeding the volume between the two halves of a comparison, so tidying up here
    // would void every pair — and because each run creates its own row and no
    // absolute count is asserted anywhere above, leaving it behind costs the next run
    // nothing.
  });
});
