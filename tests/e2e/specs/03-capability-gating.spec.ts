/**
 * Capability gating — assertion group 3 of 6. Contract C-09, `GET /v1/capabilities`.
 *
 * WHAT THIS FILE PINS, AND WHY IT IS ONE NUMBER
 * --------------------------------------------
 * Gateway is the composition root, and it exposes the legacy framework's own
 * eight-bit module gate as **configuration** rather than hard-wiring it. The
 * authoritative declaration is `ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49`,
 * under that file's own comment `//Initialize flags (pfwInitialize:[flags])`:
 * eight `Constant Long` bits at `:L41-L48`, then an aggregate at `:L49`.
 *
 * The aggregate is the whole point of this spec. `:L49` declares it as a
 * **seven-term sum** — UI + SCITER + BLINK + ORCA + SQLITE + DPIAWARE + WEBVIEW
 * — which computes to **3847**. `INIT_FLAG_ENABLE_BLINKFAST` (8) is **not one of
 * the terms**, so 3847 is *not* the bitwise union of the eight declared bits;
 * that union is 3855.
 *
 * The omission is deliberate and must never be "completed": `blink.dll` and
 * `blinkfast.dll` are alternative **builds of one engine** rather than two
 * independent capabilities, so an "everything on" constant naming both at once
 * would be incoherent. It looks like an oversight and it is not one. Arriving at
 * 3855 would fold the eighth bit in and reverse a deliberate legacy decision —
 * a behavioural change, not the repair of a typo, and precisely what the
 * replicate-verbatim constraint forbids (C-B). **If any number in this file
 * reads 3855 as the aggregate, this file is wrong.**
 *
 * That the sum is the *effective* one rather than a curiosity is settled by the
 * legacy application itself: `ws_objects/pfw.pbl.src/pfw.sra:L91` calls
 * `pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`, so the runtime-effective
 * capability set genuinely is the BLINKFAST-omitting sum.
 *
 * 3855 does appear below exactly once, derived rather than written, and for an
 * entirely different job: detecting bits nothing declares. See
 * {@link KNOWN_CAPABILITY_BITS}, which is annotated at length precisely so the
 * two values can never be confused. The service tree draws the same distinction
 * in the same way, between its `AllCapabilitiesMask` and its
 * `KnownCapabilityMask`.
 *
 * WHY THE GATE IS REPORTED RATHER THAN PROBED
 * ------------------------------------------
 * Per the read-only legacy specification `docs/README.md` §初始化, a module that
 * is not explicitly initialized has its functionality unusable **and its native
 * library need not ship at all**. A capability bit is therefore a statement
 * about how the deployment was configured, not a live feature detector, which is
 * why this endpoint answers with a mask and a flag table and why nothing below
 * tries to *exercise* a capability in order to prove a bit. Attempting to would
 * be the one thing this spec must never do, because six of the eight bits name
 * capabilities owned by services that do not exist in this phase (C-D).
 *
 * Only `INIT_FLAG_ENABLE_SQLITE` has an in-scope consumer, and the storage path
 * it gates is the one with real evidence behind it —
 * `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456` opens
 * `test.db?mode=rwc`. That single-consumer fact is asserted below, because it is
 * independent corroboration that the Phase-1 slice was drawn along a seam the
 * legacy itself recognised rather than one imposed on it.
 *
 * TWO GROUPS OF TESTS, AND ONLY ONE NEEDS A STACK
 * ----------------------------------------------
 * Every static test title ends with the marker `(no stack)`. Those tests perform
 * no I/O whatsoever and can be run on their own, against nothing:
 *
 *     npx playwright test specs/03-capability-gating.spec.ts --grep "no stack"
 *
 * They guard the shared fixture table against drift from the legacy source, so
 * that the network assertions — which derive their expectations from that same
 * table — cannot silently begin comparing against wrong numbers. A drifted table
 * would otherwise make a network test agree enthusiastically with a service that
 * had also drifted.
 *
 * The remaining tests read `/v1/capabilities` over HTTP and need the stack up.
 *
 * THE TABLE IS CONSUMED, NEVER RESTATED
 * ------------------------------------
 * The bit values live in `tests/e2e/fixtures/capability-flags.ts` and reach this
 * file through the fixture barrel. The one deliberate exception is
 * {@link LEGACY_FLAG_VALUES} below, which restates the eight numbers as literals
 * *on purpose*: a guard that imported both sides of its own comparison would
 * assert that a value equals itself. Those literals are the independent half,
 * transcribed from the legacy source with a per-line locator on each, and they
 * are the only numeric literals in this file that describe the gate.
 *
 * Identifier spellings are verbatim SCREAMING_SNAKE and are never renamed,
 * re-cased or camelCased: these exact strings appear in serialized payloads, in
 * log records and in characterization recordings, where a rename would silently
 * invalidate every stored comparison.
 *
 * WHAT IS DELIBERATELY NOT ASSERTED (C-B)
 * --------------------------------------
 * - **No individual bit is asserted enabled or disabled.** Whether a bit is
 *   switched on is deployment configuration, not a behavioural invariant.
 *   Asserting a deferred bit *enabled* would imply a deferred capability exists
 *   (C-D); asserting it *disabled* would invent a requirement (C-B). What is
 *   asserted is the *relationship* between the mask and the reported flags,
 *   which the contract does state, and which holds under every configuration.
 * - **No `401` behaviour on this route.** The published contract does declare a
 *   401 here, and the route does require authorization — but the
 *   unauthenticated-boundary assertion belongs to assertion group 2 and is made
 *   there, once, on `/v1/ping`. Restating it here would duplicate an assertion
 *   rather than add one. Every request below carries a token, which is the
 *   correct posture whether or not the endpoint were anonymous, so these tests
 *   are robust either way.
 * - **No latency, throughput, availability or service-level figure.** The
 *   repository publishes none, so any number here would be fabricated. The
 *   runner's timeouts are hang guards, not budgets.
 * - **Nothing about test coverage.** That gate is a .NET concern measured from a
 *   Cobertura report and has no expression here.
 * - **No meaning for bit positions 16, 32, 64 or 128.** The legacy declares
 *   nothing there — the values run 1, 2, 4, 8 and then jump to 256 — so no name
 *   is invented for that gap and no entry is expected in it.
 *
 * HOW IT IS RUN, AND WHAT IT NEVER DOES (C-J, C-L)
 * -----------------------------------------------
 *     cd tests/e2e && npm ci && npx playwright test
 *
 * The stack is brought up beforehand, and exclusively, by
 * `orchestration/docker-compose.yml`. Nothing in this file starts, stops,
 * restarts, builds, seeds or health-gates a service, and no port literal appears
 * anywhere below — neither Gateway's own, which the fixtures own, nor the slot
 * held in reserve for the deferred presentation service. Every import resolves
 * inside `tests/e2e`, so nothing here reaches the read-only behavioural-oracle
 * assets that sit beside this directory (C-C).
 *
 * CREDENTIALS (C-F, C-G)
 * ---------------------
 * Every token is minted at run time by Security, the sole issuer, and is reached
 * through `requireServiceToken`, which applies the issuance precondition before
 * asking. No token is embedded, none is signed locally, no
 * signing key is read or named, and no credential-shaped literal appears
 * anywhere below. No token and no `Authorization` header is ever rendered into
 * an assertion message, and one test below turns that discipline on the response
 * itself, asserting that a configuration-projecting endpoint leaks no
 * secret-shaped material of its own.
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules
 * were provided. That is a finding rather than an omission, and it is not licence
 * to lower the bar — the enterprise-standard baseline applies in its place, and
 * its operative clauses here are that no secret appears in source and that the
 * published contract, not convenience, decides what may be asserted.
 */

import { expect, test } from '@playwright/test';

import {
  ALL_CAPABILITY_FLAG_NAMES,
  CAPABILITIES_PATH,
  CAPABILITY_FLAGS,
  INIT_FLAG_ENABLE_ALL,
  INIT_FLAG_ENABLE_BLINK,
  INIT_FLAG_ENABLE_BLINKFAST,
  INIT_FLAG_ENABLE_DPIAWARE,
  INIT_FLAG_ENABLE_ORCA,
  INIT_FLAG_ENABLE_SCITER,
  INIT_FLAG_ENABLE_SQLITE,
  INIT_FLAG_ENABLE_UI,
  INIT_FLAG_ENABLE_WEBVIEW,
  IN_SCOPE_CAPABILITY_FLAGS,
  bearerHeaders,
  capabilityFlagsIn,
  gatewayUrl,
  hasCapability,
  type CapabilityFlagName,
  type ServiceToken,
} from '../fixtures';

import {
  assertTokenIssuanceProvisioned,
  requireServiceToken,
} from '../fixtures/token-issuance';

import { probeStackAvailability } from '../fixtures/live-stack';

/*
 * ===========================================================================
 * THE SINGLE EDIT POINT — candidate member names on the wire
 * ===========================================================================
 * Everything this file expects to find *by name* in the response is listed in
 * this one block, and every read below goes through it. If Gateway's
 * serialization ever differs from what is listed, this block is the only place
 * that needs editing: no guessed key string appears anywhere else in the file,
 * and no assertion below performs a deep structural comparison against a shape
 * this suite does not own.
 *
 * The first entry of each list is the member the published contract
 * `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` actually declares
 * for the `CapabilityReport` and `Capability` schemas. The alternatives that
 * follow are the plausible casings and synonyms a re-serialization might land
 * on. Accepting them is not laxity about the contract — it is what keeps a
 * *capability* assertion from failing for a *naming* reason, so that a genuine
 * gate regression is never masked by a renamed field. Whichever member is
 * found, the invariants asserted against it are identical.
 */

/**
 * The mask actually in effect in this deployment.
 *
 * Contract member: `effectiveMask`, declared over the full unsigned 32-bit
 * domain because the configured value is projected with an unchecked
 * conversion.
 */
const EFFECTIVE_MASK_KEYS: readonly string[] = Object.freeze([
  'effectiveMask',
  'effective_mask',
  'capabilityMask',
  'mask',
]);

/**
 * The aggregate — the value of `INIT_FLAG_ENABLE_ALL`.
 *
 * Contract member: `allMask`, which the schema fixes as a constant rather than
 * a range, because it is settled by the legacy declaration and not by this
 * deployment.
 */
const ALL_MASK_KEYS: readonly string[] = Object.freeze([
  'allMask',
  'all_mask',
  'allCapabilitiesMask',
]);

/**
 * Bits set in the effective mask that none of the eight declared capabilities
 * claims.
 *
 * Contract member: `unrecognizedBits`, and the one member of the report the
 * schema does **not** mark required — so every read of it below tolerates its
 * absence rather than demanding it.
 */
const UNRECOGNIZED_BITS_KEYS: readonly string[] = Object.freeze([
  'unrecognizedBits',
  'unrecognized_bits',
]);

/**
 * The array of per-capability entries.
 *
 * Contract member: `capabilities`, declared with both `minItems` and `maxItems`
 * at eight, because the legacy declaration closes the set.
 */
const CAPABILITY_CONTAINER_KEYS: readonly string[] = Object.freeze([
  'capabilities',
  'capabilityFlags',
  'flags',
]);

/** One entry's legacy identifier. Contract member: `name`. */
const CAPABILITY_NAME_KEYS: readonly string[] = Object.freeze([
  'name',
  'capability',
  'flag',
]);

/** One entry's numeric bit value. Contract member: `value`. */
const CAPABILITY_VALUE_KEYS: readonly string[] = Object.freeze([
  'value',
  'bit',
  'bitValue',
]);

/** Whether one entry's bit is set in the effective mask. Contract member: `enabled`. */
const CAPABILITY_ENABLED_KEYS: readonly string[] = Object.freeze([
  'enabled',
  'isEnabled',
]);

/**
 * Where one capability's implementation lives in the Phase-1 slice.
 *
 * Contract member: `phaseOneDestination`, whose closed value set is
 * `[Persistence, DesignSystem, ScriptBridge, PackagingTooling]`. This list is
 * load-bearing for the deferred-name guard rather than merely descriptive —
 * see {@link DEFERRED_SERVICE_NAMES} for the reconciliation it makes possible.
 */
const CAPABILITY_DESTINATION_KEYS: readonly string[] = Object.freeze([
  'phaseOneDestination',
  'phase_one_destination',
  'destination',
]);

/*
 * ===========================================================================
 * The independent half of the parity guard
 * ===========================================================================
 */

/**
 * The eight bit values, transcribed as literals straight from the legacy
 * declaration, each with the line that declares it.
 *
 * This is the ONE place this file restates a number the fixture already
 * exports, and the duplication is the entire mechanism rather than an
 * oversight. A guard that read both sides of its comparison from the fixture
 * would assert that a value equals itself and would pass just as happily after
 * the fixture drifted. These literals are the independent witness; the fixture
 * is the value under test.
 *
 * The values are sparse — 1, 2, 4, 8, 256, 512, 1024, 2048 — rather than the
 * first eight powers of two. The gap at 16, 32, 64 and 128 is the legacy's own:
 * it declares nothing there. No flag is invented to fill it and the values are
 * not compacted to close it (C-B).
 *
 * The `Record<CapabilityFlagName, number>` annotation is also a compile-time
 * completeness check: omitting a bit here, or misspelling one, fails to
 * type-check rather than silently narrowing the guard.
 */
const LEGACY_FLAG_VALUES: Readonly<Record<CapabilityFlagName, number>> =
  Object.freeze({
    INIT_FLAG_ENABLE_UI: 1, //        enums.sru:L41
    INIT_FLAG_ENABLE_SCITER: 2, //    enums.sru:L42
    INIT_FLAG_ENABLE_BLINK: 4, //     enums.sru:L43
    INIT_FLAG_ENABLE_BLINKFAST: 8, // enums.sru:L44 — the bit the aggregate omits
    INIT_FLAG_ENABLE_ORCA: 256, //    enums.sru:L45
    INIT_FLAG_ENABLE_SQLITE: 512, //  enums.sru:L46
    INIT_FLAG_ENABLE_DPIAWARE: 1024, // enums.sru:L47
    INIT_FLAG_ENABLE_WEBVIEW: 2048, // enums.sru:L48
  });

/**
 * The eight constants as the barrel exports them **individually**, gathered so
 * they can be compared against both the fixture's own record and the legacy
 * literals above.
 *
 * A spec may import `INIT_FLAG_ENABLE_SQLITE` directly or read
 * `CAPABILITY_FLAGS['INIT_FLAG_ENABLE_SQLITE']`, and the two must agree. Both
 * forms are published surface, so both are pinned here; checking only the record
 * would leave the named exports — the form specs reach for most often —
 * unguarded.
 */
const DIRECTLY_IMPORTED_FLAGS: Readonly<Record<CapabilityFlagName, number>> =
  Object.freeze({
    INIT_FLAG_ENABLE_UI,
    INIT_FLAG_ENABLE_SCITER,
    INIT_FLAG_ENABLE_BLINK,
    INIT_FLAG_ENABLE_BLINKFAST,
    INIT_FLAG_ENABLE_ORCA,
    INIT_FLAG_ENABLE_SQLITE,
    INIT_FLAG_ENABLE_DPIAWARE,
    INIT_FLAG_ENABLE_WEBVIEW,
  });

/**
 * The seven terms the legacy aggregate actually sums, in the source's own
 * order.
 *
 * Naming the seven pins *which* bits are summed, not merely what they total. A
 * test that only checked the total would pass on a sum that dropped
 * `INIT_FLAG_ENABLE_ORCA` (256) and added `INIT_FLAG_ENABLE_BLINKFAST` (8)
 * twice — arithmetically 3847, structurally wrong. `INIT_FLAG_ENABLE_BLINKFAST`
 * is deliberately absent from this list, which is the same absence
 * `enums.sru:L49` has.
 */
const SEVEN_SUMMED_FLAG_NAMES: readonly CapabilityFlagName[] = Object.freeze([
  'INIT_FLAG_ENABLE_UI',
  'INIT_FLAG_ENABLE_SCITER',
  'INIT_FLAG_ENABLE_BLINK',
  'INIT_FLAG_ENABLE_ORCA',
  'INIT_FLAG_ENABLE_SQLITE',
  'INIT_FLAG_ENABLE_DPIAWARE',
  'INIT_FLAG_ENABLE_WEBVIEW',
]);

/**
 * The bitwise union of all eight declared bits — which genuinely is 3855.
 *
 * **This is not the aggregate and must never be used as one.** It exists for
 * exactly one job: detecting bits that *nothing declares*. A residual computed
 * against it is the only way an unknown high bit can be made to fail loudly,
 * because the fixture's decomposition helper reports named bits only and gives
 * an unnamed bit no name to report.
 *
 * The two values differ by precisely the BLINKFAST bit, and conflating them is
 * the single likeliest way to break parity with the framework:
 *
 *   - `INIT_FLAG_ENABLE_ALL`     = 3847 — the seven-term legacy aggregate, and
 *                                        the value the framework initializes
 *                                        with. Asserted as 3847 below.
 *   - `KNOWN_CAPABILITY_BITS`    = 3855 — the union of the eight declared bits,
 *                                        used only to compute "is any set bit
 *                                        undeclared". Never asserted as the
 *                                        aggregate, anywhere.
 *
 * Derived by folding the fixture table rather than written as a literal, so that
 * 3855 appears nowhere in this file as executable code — only in prose such as
 * the note above, where it cannot be mistaken for a transcription of
 * `enums.sru:L49` or picked up by a search for the aggregate. The service tree
 * keeps the same two values apart for the same reason and with the same warning.
 */
const KNOWN_CAPABILITY_BITS: number = ALL_CAPABILITY_FLAG_NAMES.reduce(
  (union: number, name: CapabilityFlagName): number =>
    union | CAPABILITY_FLAGS[name],
  0,
);

/**
 * The four capability areas that are mapped in discovery and deliberately left
 * unbuilt in this phase.
 *
 * ONE RECONCILIATION, STATED OPENLY BECAUSE IT LOOKS LIKE A CONFLICT
 * -----------------------------------------------------------------
 * The published contract makes `phaseOneDestination` a **required** member of
 * every capability entry and closes its value set to four tokens, two of which
 * name deferred areas. A conforming response therefore *does* contain the
 * strings `DesignSystem` and `ScriptBridge`, and a naive "no deferred name
 * appears anywhere in the body" assertion would fail against correct code —
 * accusing a service that is behaving exactly to its own contract, which is the
 * worst failure a suite can produce.
 *
 * The constraint being honoured is about **coupling and implementation**: no
 * deferred service may be built, reached, or presented as reachable. A
 * destination *name* in a payload creates no coupling — there is no client, no
 * import, no registration and no call behind it — and the member exists
 * precisely so that a caller can tell that enabling a bit whose destination is
 * deferred has no runtime effect in this phase. That legibility is required of
 * the gateway contract, not forbidden by it.
 *
 * So the guard below is narrowed to what the constraint actually demands, and
 * in that narrowed form it is *stronger* than the naive version:
 *
 *   1. No capability **identity** is a deferred name — the reported names are a
 *      subset of the eight legacy identifiers, so no capability masquerades as
 *      a service.
 *   2. `Documents` and `Integration` appear **nowhere at all**. Neither is a
 *      member of the destination value set, so for those two the strict
 *      whole-body reading holds exactly, and it is asserted.
 *   3. `DesignSystem` and `ScriptBridge` appear **only** as the value of a
 *      contract-declared destination member. Anywhere else — another member,
 *      any object key, any nested position — is a failure.
 *   4. No **object key** anywhere is a deferred name.
 *   5. No deferred **route family** appears, so nothing in the payload
 *      advertises a deferred capability as something a caller could call. Those
 *      four routes are declared once, elsewhere, as reserved metadata, and this
 *      report is not the place they belong.
 */
const DEFERRED_SERVICE_NAMES: readonly string[] = Object.freeze([
  'DesignSystem',
  'Documents',
  'Integration',
  'ScriptBridge',
]);

/**
 * The subset of {@link DEFERRED_SERVICE_NAMES} that the contract's destination
 * value set does **not** contain, and which may therefore appear nowhere in the
 * response at all.
 *
 * Derived from the two lists rather than written out, so it cannot drift away
 * from either. Deriving it is what makes clause 2 of the reconciliation above
 * self-maintaining: were the destination set ever widened, this list would
 * narrow to match instead of producing a false failure.
 */
const NEVER_MENTIONED_DEFERRED_NAMES: readonly string[] = Object.freeze(
  DEFERRED_SERVICE_NAMES.filter(
    (name: string): boolean =>
      name !== 'DesignSystem' && name !== 'ScriptBridge',
  ),
);

/**
 * The four reserved Phase-2 route families.
 *
 * Listed so their **absence** from this payload can be asserted. A capability
 * report is configuration; it is not a route table, and it must not become one.
 * The reserved routes are declared exactly once in the service tree, as routing
 * metadata that answers "not implemented", and a capability report that also
 * advertised them would give a caller a second, weaker place to discover them.
 */
const DEFERRED_ROUTE_FAMILIES: readonly string[] = Object.freeze([
  '/v1/design',
  '/v1/documents',
  '/v1/integration',
  '/v1/scripting',
]);

/**
 * Markers that must never appear in a response body from this endpoint.
 *
 * A cheap guard, and a targeted one: `/v1/capabilities` projects
 * *configuration*, and a configuration-projecting endpoint is exactly the kind
 * that acquires a leak by accident — one careless addition that serializes the
 * whole options object rather than the gate, and the signing key travels with
 * it. The markers are the header of a credential rather than any credential
 * value, so listing them introduces nothing sensitive: each is a name or a
 * structural prefix, and no key, certificate, password or token appears here or
 * anywhere else in this file (C-F).
 *
 * Matched case-insensitively, because a leak does not respect casing.
 */
const SECRET_SHAPED_MARKERS: readonly string[] = Object.freeze([
  'authorization',
  'bearer ',
  'signingkey',
  'signing_key',
  'jwt_signing',
  'begin rsa private key',
  'begin private key',
  'begin certificate',
  'private_key',
  'privatekey',
  'client_secret',
  'password',
]);

/**
 * Pinned false on every request below.
 *
 * Left to throw on a non-2xx, the runner raises an error of its own whose
 * message can quote the response body verbatim. Pinning it false means every
 * status is handled by an assertion in this file, so exactly one message shape
 * is produced, it quotes no body, and a failure reports *which* status arrived
 * rather than an opaque transport error.
 */
const NEVER_THROW_ON_STATUS = { failOnStatusCode: false } as const;

/**
 * The inclusive upper bound of the effective mask, as the contract declares it.
 *
 * The domain is the full **unsigned** 32-bit range rather than the signed one,
 * because the configured value is projected with an unchecked conversion: every
 * one of the 2^32 values is representable and a negative configured value wraps
 * rather than failing. Asserted rather than assumed, because the fixture's
 * bitwise helpers reject a mask outside this range with a `TypeError`, and a
 * `TypeError` thrown from inside a helper reads as a fixture defect rather than
 * as the malformed payload it would actually be.
 */
const MAX_UINT32 = 4_294_967_295;

/*
 * ===========================================================================
 * Pure local helpers
 * ===========================================================================
 * All of them are deterministic and side-effect free, and none performs I/O:
 * the network calls stay visible in the test bodies, where a reader can see
 * exactly which requests a test makes. Everything below operates on an
 * already-decoded body.
 *
 * No helper renders a response value into a message. They report *shape* — a
 * member name, a count, a type — and never content, so nothing a service
 * returns can reach the console or the CI log through a diagnostic path (C-F).
 */

/** One capability as this file reads it, independent of the shape it arrived in. */
interface CapabilitySighting {
  /** The legacy identifier, or `undefined` when the entry carries none. */
  readonly name: string | undefined;
  /** The numeric bit value, when the shape supplies one. */
  readonly value: number | undefined;
  /** Whether the bit is set in the effective mask, when the shape supplies it. */
  readonly enabled: boolean | undefined;
  /** The declared Phase-1 destination, when the shape supplies one. */
  readonly destination: string | undefined;
}

/** One string found in the body, together with the member name it sat under. */
interface StringSighting {
  readonly key: string;
  readonly value: string;
}

/** Every member name and every string in a decoded body, gathered by one walk. */
interface BodyInventory {
  readonly keys: readonly string[];
  readonly strings: readonly StringSighting[];
}

/** The synthetic member name given to the root, which sits under no member. */
const ROOT_MEMBER = '<root>';

/**
 * Narrows an unknown decoded value to a JSON object.
 *
 * Arrays are rejected alongside primitives and `null`: an array has indexable
 * members and would pass a naive `typeof === 'object'` test, after which reading
 * a named member off it yields a silent `undefined`.
 */
function asJsonObject(value: unknown): Record<string, unknown> | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }

  return value as Record<string, unknown>;
}

/**
 * Returns the value of the first candidate member the object actually declares.
 *
 * Presence is tested with `Object.hasOwn` rather than by comparing the read
 * against `undefined`, so a member that is present and explicitly null is
 * treated as present. That distinction matters: an explicit null is a service
 * emitting the member badly, which should fail, whereas an absent member is a
 * different — and for one member, legitimate — condition.
 */
function readCandidate(
  source: Record<string, unknown>,
  candidates: readonly string[],
): unknown {
  for (const candidate of candidates) {
    if (Object.hasOwn(source, candidate)) {
      return source[candidate];
    }
  }

  return undefined;
}

/** Reads the first candidate member that holds a finite number. */
function readNumber(
  source: Record<string, unknown>,
  candidates: readonly string[],
): number | undefined {
  const value: unknown = readCandidate(source, candidates);

  return typeof value === 'number' && Number.isFinite(value)
    ? value
    : undefined;
}

/** Reads the first candidate member that holds a string. */
function readString(
  source: Record<string, unknown>,
  candidates: readonly string[],
): string | undefined {
  const value: unknown = readCandidate(source, candidates);

  return typeof value === 'string' ? value : undefined;
}

/** Reads the first candidate member that holds a boolean. */
function readBoolean(
  source: Record<string, unknown>,
  candidates: readonly string[],
): boolean | undefined {
  const value: unknown = readCandidate(source, candidates);

  return typeof value === 'boolean' ? value : undefined;
}

/**
 * Renders a candidate list for a failure message.
 *
 * Every "member not found" message names the whole list, so a reader is pointed
 * straight at the single edit point instead of having to guess which spelling
 * the file expected.
 */
function formatCandidates(candidates: readonly string[]): string {
  return candidates.join(' | ');
}

/** Reports whether a string is one of the eight verbatim legacy identifiers. */
function isCapabilityFlagName(value: string): value is CapabilityFlagName {
  return (ALL_CAPABILITY_FLAG_NAMES as readonly string[]).includes(value);
}

/**
 * Computes the bits of a mask that no declared capability claims.
 *
 * The shift is what makes the answer readable rather than merely correct.
 * JavaScript's bitwise operators coerce to a **signed** 32-bit integer and hand
 * back a signed result, so a residual in the high bits would otherwise surface
 * as a negative number and a failure message would report something like
 * `-3856` where the honest answer is `4294963440`. The unsigned shift converts
 * the result back into the domain the contract actually declares.
 */
function unrecognizedBitsIn(mask: number): number {
  return (mask & ~KNOWN_CAPABILITY_BITS) >>> 0;
}

/**
 * Decodes a response body into a JSON object.
 *
 * Throws rather than returning a failure value, so that a malformed body is
 * reported once, in one shape, at the point of decoding. The parser's own error
 * is deliberately **not** attached: a JSON parser quotes the text it choked on,
 * and Node prints an error's `cause` chain, so attaching it would publish the
 * body to every log that renders the failure. Only the failure category is
 * reported.
 */
function decodeReport(bodyText: string): Record<string, unknown> {
  let parsed: unknown;

  try {
    parsed = JSON.parse(bodyText) as unknown;
  } catch {
    throw new Error(
      `GET ${CAPABILITIES_PATH} answered with a body that is not parseable ` +
        'JSON, so no capability report could be read. The parser error is ' +
        'omitted deliberately: it quotes the text it failed on, and no ' +
        'response value is placed in a message by this file.',
    );
  }

  const report: Record<string, unknown> | undefined = asJsonObject(parsed);

  if (report === undefined) {
    throw new Error(
      `GET ${CAPABILITIES_PATH} answered with a JSON value that is not an ` +
        'object, so it cannot carry the capability report members. The ' +
        'published schema declares an object.',
    );
  }

  return report;
}

/**
 * Normalizes whichever container shape arrived into one list of sightings.
 *
 * Three shapes are accepted, and accepting all three is what lets the
 * invariants below be written once:
 *
 *   - an **array of objects** — what the published schema declares, each entry
 *     carrying a name, a value, an enabled flag and a destination;
 *   - an **array of strings** — bare identifiers, with no value to compare;
 *   - an **object map** keyed by identifier, whose values may be numeric bit
 *     values or booleans.
 *
 * Every member name is resolved through the single edit point, so no key string
 * is written here. A shape that is neither an array nor an object yields
 * `undefined`, which the caller reports as a shape failure rather than passing
 * over in silence.
 */
function normalizeCapabilityEntries(
  container: unknown,
): readonly CapabilitySighting[] | undefined {
  if (Array.isArray(container)) {
    return container.map((element: unknown): CapabilitySighting => {
      if (typeof element === 'string') {
        return {
          name: element,
          value: undefined,
          enabled: undefined,
          destination: undefined,
        };
      }

      const entry: Record<string, unknown> | undefined = asJsonObject(element);

      if (entry === undefined) {
        return {
          name: undefined,
          value: undefined,
          enabled: undefined,
          destination: undefined,
        };
      }

      return {
        name: readString(entry, CAPABILITY_NAME_KEYS),
        value: readNumber(entry, CAPABILITY_VALUE_KEYS),
        enabled: readBoolean(entry, CAPABILITY_ENABLED_KEYS),
        destination: readString(entry, CAPABILITY_DESTINATION_KEYS),
      };
    });
  }

  const map: Record<string, unknown> | undefined = asJsonObject(container);

  if (map === undefined) {
    return undefined;
  }

  return Object.entries(map).map(
    ([name, value]: [string, unknown]): CapabilitySighting => ({
      name,
      value: typeof value === 'number' && Number.isFinite(value)
        ? value
        : undefined,
      enabled: typeof value === 'boolean' ? value : undefined,
      destination: undefined,
    }),
  );
}

/**
 * Walks a decoded body once, collecting every member name and every string
 * together with the member it sat under.
 *
 * One walk rather than several targeted reads, because the deferred-name guard
 * has to be exhaustive: a name appearing in a member nobody thought to check is
 * exactly the case a targeted read would miss. Array elements inherit their
 * parent's member name, so a string inside `capabilities[3].phaseOneDestination`
 * is attributed to the destination member and a string inside a bare array is
 * attributed to the array's own member.
 */
function inventoryOf(node: unknown): BodyInventory {
  const keys: string[] = [];
  const strings: StringSighting[] = [];

  const walk = (value: unknown, key: string): void => {
    if (typeof value === 'string') {
      strings.push({ key, value });
      return;
    }

    if (Array.isArray(value)) {
      for (const element of value) {
        walk(element, key);
      }
      return;
    }

    const object: Record<string, unknown> | undefined = asJsonObject(value);

    if (object === undefined) {
      return;
    }

    for (const [childKey, childValue] of Object.entries(object)) {
      keys.push(childKey);
      walk(childValue, childKey);
    }
  };

  walk(node, ROOT_MEMBER);

  return { keys, strings };
}

/**
 * Asserts that a required numeric member was found, and narrows it.
 *
 * The narrowing is the reason this exists rather than a bare `toBeDefined()` at
 * each call site. Playwright's `expect` throws on failure but is not a
 * TypeScript assertion function, so a checked value stays `number | undefined`
 * to the compiler and every subsequent bitwise read would need re-checking. The
 * unreachable throw below converts the runtime guarantee into a compile-time one
 * once, here, instead of at a dozen call sites.
 *
 * The failure message names the whole candidate list, so a mismatch caused by a
 * *renamed* member points the reader straight at the single edit point rather
 * than reading as a capability regression.
 */
function requireNumber(
  value: number | undefined,
  candidates: readonly string[],
  memberDescription: string,
): number {
  expect(
    value,
    `GET ${CAPABILITIES_PATH} must report ${memberDescription}, and none of ` +
      `the candidate members [${formatCandidates(candidates)}] was present ` +
      'with a numeric value. The published schema marks this member required, ' +
      'so either the report is incomplete or the serialization uses a name ' +
      'this file does not yet list — in which case add it to the candidate ' +
      'block at the top of this file, which is the single edit point for every ' +
      'member name on the wire.',
  ).toBeDefined();

  if (value === undefined) {
    // Unreachable: the assertion above throws first. Present only so the return
    // type can be `number`.
    throw new Error(`unreachable: ${memberDescription} was not reported`);
  }

  return value;
}

test.describe('Capability gating', () => {
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

  // Deliberately NOT `mode: 'serial'`. Every assertion below is independent,
  // mutates nothing, and acquires whatever credential it needs for itself.
  // Serial execution belongs to the two mutating workflow specs, where row state
  // genuinely is shared between tests; imposing it here would couple tests that
  // have no relationship and would make a single failure cascade into skips.

  /*
   * -------------------------------------------------------------------------
   * The static invariant guard — no network, no stack, no I/O
   * -------------------------------------------------------------------------
   * Run these on their own with:
   *
   *   npx playwright test specs/03-capability-gating.spec.ts --grep "no stack"
   *
   * They exist because every network assertion further down derives its
   * expectations from the shared fixture table. If that table drifted from the
   * legacy source, those assertions would keep passing while comparing against
   * the wrong numbers — and a service that had drifted the same way would be
   * congratulated rather than caught. These tests are the independent check that
   * closes that gap, and they are the only part of this file that can be
   * verified with nothing running at all.
   */

  test('the eight capability bits match the legacy declaration exactly (no stack)', { tag: '@no-stack' }, () => {
    // Three-way agreement, per bit: the individually exported constant, the
    // fixture's own record, and the literal transcribed from the legacy source.
    // Iterating the published name list rather than writing eight blocks means a
    // ninth name appearing in the fixture is checked automatically instead of
    // being quietly ignored.
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      const legacyValue: number = LEGACY_FLAG_VALUES[name];

      expect(
        DIRECTLY_IMPORTED_FLAGS[name],
        `the exported constant ${name} must equal the value ` +
          'ws_objects/pfw.shared.pbl.src/enums.sru:L41-L48 declares for it. A ' +
          'mismatch here means the fixture has drifted from the behavioural ' +
          'oracle, and every capability assertion in this suite is comparing ' +
          'against a wrong number.',
      ).toBe(legacyValue);

      expect(
        CAPABILITY_FLAGS[name],
        `CAPABILITY_FLAGS['${name}'] must agree with the constant of the same ` +
          'name. Both forms are published fixture surface and specs reach for ' +
          'either, so a disagreement would make two correct-looking specs ' +
          'assert different things.',
      ).toBe(legacyValue);
    }

    // Called out on its own because it is the one bit with an in-scope consumer,
    // so it is the one whose value a reader is most likely to look for here.
    expect(
      INIT_FLAG_ENABLE_SQLITE,
      'INIT_FLAG_ENABLE_SQLITE must be 512 [enums.sru:L46]. It is the only ' +
        'capability with a Phase-1 consumer, so it is the only bit whose value ' +
        'this phase actually acts on.',
    ).toBe(512);

    // The gap is the legacy's own. Nothing is declared at 16, 32, 64 or 128, and
    // this asserts that no flag has been invented to fill it (C-B).
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      expect(
        [1, 2, 4, 8, 256, 512, 1024, 2048],
        `${name} must be one of the eight sparse values the legacy declares. ` +
          'The values jump from 8 to 256 and nothing occupies 16, 32, 64 or ' +
          '128; a flag appearing in that gap would be an invention.',
      ).toContain(CAPABILITY_FLAGS[name]);
    }
  });

  test('INIT_FLAG_ENABLE_ALL is 3847 and omits BLINKFAST deliberately (no stack)', { tag: '@no-stack' }, () => {
    // ===================================================================
    //  THE AGGREGATE IS 3847. IT IS NOT 3855. THAT IS CORRECT.
    //  -------------------------------------------------------------------
    //  enums.sru:L49 declares the aggregate as a SEVEN-term sum over the
    //  EIGHT bits declared immediately above it:
    //
    //      1 + 2 + 4 + 256 + 512 + 1024 + 2048  =  3847
    //
    //  INIT_FLAG_ENABLE_BLINKFAST (8) is deliberately absent, because
    //  blink.dll and blinkfast.dll are alternative BUILDS of one engine
    //  rather than two independent capabilities, so an "everything on"
    //  constant naming both would be incoherent.
    //
    //  Arriving at 3855 means the eighth bit was folded in and a deliberate
    //  legacy decision reversed. That is a behavioural change, not the
    //  repair of a typo, and it is exactly what C-B forbids.
    // ===================================================================
    expect(
      INIT_FLAG_ENABLE_ALL,
      'INIT_FLAG_ENABLE_ALL must be 3847 [enums.sru:L49]. If this reads 3855, ' +
        'INIT_FLAG_ENABLE_BLINKFAST has been folded into the aggregate and a ' +
        'deliberate legacy decision has been reversed: the two MiniBlink ' +
        'binaries are alternative builds of one engine, so enabling both is ' +
        'meaningless. That is a behavioural change, not a corrected typo.',
    ).toBe(3847);

    // The omission pinned as a property rather than left implicit in the total.
    // A total can be reached by several wrong routes; this cannot.
    expect(
      INIT_FLAG_ENABLE_ALL & INIT_FLAG_ENABLE_BLINKFAST,
      'the aggregate must not carry the BLINKFAST bit. Asserting the total ' +
        'alone would leave this incidental; asserting it directly makes the ' +
        'omission a pinned property of the value.',
    ).toBe(0);

    expect(
      hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_BLINKFAST),
      'the containment helper must agree that the aggregate excludes ' +
        'BLINKFAST. This is the same fact as the bitwise test above, asserted ' +
        'through the helper every other consumer actually calls.',
    ).toBe(false);

    // The two easily-confused values, held apart explicitly. KNOWN_CAPABILITY_BITS
    // is the union of all eight declared bits and genuinely is 3855; it exists
    // only to detect undeclared bits and is never the aggregate.
    expect(
      KNOWN_CAPABILITY_BITS - INIT_FLAG_ENABLE_ALL,
      'the union of the eight declared bits must exceed the seven-term ' +
        'aggregate by exactly the BLINKFAST bit. That difference is the whole ' +
        'distinction between the two values, and this is the assertion that ' +
        'keeps them from being conflated.',
    ).toBe(INIT_FLAG_ENABLE_BLINKFAST);

    expect(
      INIT_FLAG_ENABLE_ALL,
      'the aggregate must not equal the union of all eight bits. They differ ' +
        'by the BLINKFAST bit, and a file that used one where the other ' +
        'belongs would break parity with the framework by precisely that bit.',
    ).not.toBe(KNOWN_CAPABILITY_BITS);
  });

  test('the aggregate sums exactly the seven bits the legacy sums (no stack)', { tag: '@no-stack' }, () => {
    // This pins WHICH seven, not merely the total. A sum that dropped ORCA (256)
    // and counted BLINKFAST (8) twice also totals 3847 while being structurally
    // wrong, and only an assertion over the named terms rejects it.
    expect(
      SEVEN_SUMMED_FLAG_NAMES.length,
      'the legacy aggregate has exactly seven terms over eight declared bits ' +
        '[enums.sru:L49].',
    ).toBe(7);

    expect(
      SEVEN_SUMMED_FLAG_NAMES,
      'the summed terms must not include BLINKFAST — that absence IS the ' +
        'behaviour being preserved.',
    ).not.toContain('INIT_FLAG_ENABLE_BLINKFAST');

    const summedTotal: number = SEVEN_SUMMED_FLAG_NAMES.reduce(
      (total: number, name: CapabilityFlagName): number =>
        total + LEGACY_FLAG_VALUES[name],
      0,
    );

    expect(
      summedTotal,
      'the seven named terms, added with the legacy literals, must reproduce ' +
        'the aggregate. This is the arithmetic of enums.sru:L49 performed over ' +
        'the independent transcription rather than over the fixture.',
    ).toBe(INIT_FLAG_ENABLE_ALL);

    // Sum and bitwise union agree only because the seven bits are pairwise
    // disjoint. Asserting both therefore also asserts that no term is repeated —
    // a duplicated term would inflate the sum while leaving the union unchanged.
    const summedUnion: number = SEVEN_SUMMED_FLAG_NAMES.reduce(
      (union: number, name: CapabilityFlagName): number =>
        union | LEGACY_FLAG_VALUES[name],
      0,
    );

    expect(
      summedUnion,
      'the bitwise union of the seven terms must equal their arithmetic sum. ' +
        'They coincide only while the terms are distinct single bits, so this ' +
        'assertion rejects a repeated term that happened to total correctly.',
    ).toBe(summedTotal);

    // Every one of the seven is genuinely present in the aggregate, checked
    // through the helper consumers use.
    for (const name of SEVEN_SUMMED_FLAG_NAMES) {
      expect(
        hasCapability(INIT_FLAG_ENABLE_ALL, CAPABILITY_FLAGS[name]),
        `${name} is one of the seven terms of the aggregate and must be ` +
          'contained in it.',
      ).toBe(true);
    }
  });

  test('storage is the only capability with an in-scope Phase-1 consumer (no stack)', { tag: '@no-stack' }, () => {
    // Independent corroboration that the Phase-1 slice follows a seam the legacy
    // itself recognised. Six of the eight bits belong to deferred capability
    // areas and one to packaging tooling that is not a service at any phase, so
    // exactly one bit is left with something in this phase that consumes it.
    expect(
      IN_SCOPE_CAPABILITY_FLAGS.length,
      'exactly one of the eight capabilities has a consumer inside the ' +
        'Phase-1 slice. A second entry would mean a deferred capability had ' +
        'acquired an in-scope consumer, which is the boundary this phase is ' +
        'defined by.',
    ).toBe(1);

    const [onlyInScope] = IN_SCOPE_CAPABILITY_FLAGS;

    expect(
      onlyInScope,
      'the single in-scope capability must be storage. It is the only one of ' +
        'the eight with evidence behind it in the legacy tree — ' +
        'ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L456 opens ' +
        "'test.db?mode=rwc' — and the only one this phase provisions.",
    ).toBe('INIT_FLAG_ENABLE_SQLITE');

    // Guarded so the read below is a checked one rather than an assumption; the
    // assertion above has already established the value.
    expect(onlyInScope).toBeDefined();

    if (onlyInScope !== undefined) {
      expect(
        CAPABILITY_FLAGS[onlyInScope],
        'the in-scope capability must resolve through the table to the same ' +
          'bit its exported constant carries.',
      ).toBe(INIT_FLAG_ENABLE_SQLITE);
    }
  });

  test('the table names exactly eight bits, spelled verbatim (no stack)', { tag: '@no-stack' }, () => {
    expect(
      ALL_CAPABILITY_FLAG_NAMES.length,
      'the capability set is closed by the legacy declaration at ' +
        'enums.sru:L41-L48, which declares eight bits. A ninth name would be ' +
        'a change to the published contract, not an addition to a fixture.',
    ).toBe(8);

    expect(
      Object.keys(CAPABILITY_FLAGS).length,
      'the keyed table and the name list must describe the same closed set, ' +
        'or a spec iterating one would silently skip a bit the other declares.',
    ).toBe(ALL_CAPABILITY_FLAG_NAMES.length);

    expect(
      new Set(ALL_CAPABILITY_FLAG_NAMES).size,
      'the eight names must be distinct. A duplicate would make every ' +
        'iteration below assert one bit twice and another not at all.',
    ).toBe(ALL_CAPABILITY_FLAG_NAMES.length);

    // Verbatim SCREAMING_SNAKE, asserted rather than trusted. These exact
    // strings appear in serialized payloads, log records and characterization
    // recordings, so a rename — however idiomatic it looked — would silently
    // invalidate every stored comparison.
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      expect(
        name,
        `${name} must keep the verbatim legacy spelling: the INIT_FLAG_ENABLE_ ` +
          'prefix and an upper-case suffix. Re-casing or camelCasing a ' +
          'capability identifier breaks every characterization recording that ' +
          'already stores it.',
      ).toMatch(/^INIT_FLAG_ENABLE_[A-Z0-9]+$/);

      expect(
        Object.keys(CAPABILITY_FLAGS),
        `${name} must appear as a key of the capability table with the same ` +
          'spelling the name list uses.',
      ).toContain(name);
    }
  });

  test('the pure bitwise helpers agree with the table (no stack)', { tag: '@no-stack' }, () => {
    // Storage is contained in the aggregate; the alternative engine build is not.
    // These two are the helper-level statement of the omission this file exists
    // to pin.
    expect(
      hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_SQLITE),
      'the aggregate must contain the storage bit — it is one of the seven ' +
        'terms enums.sru:L49 sums.',
    ).toBe(true);

    expect(
      hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_BLINKFAST),
      'the aggregate must not contain the BLINKFAST bit.',
    ).toBe(false);

    // Decomposition must yield exactly the seven summed names, in ascending bit
    // order. Compared as an ordered list rather than as a set, because the order
    // is a documented property of the helper: two runs are meant to line up
    // element by element, which a set comparison would stop guarding.
    expect(
      capabilityFlagsIn(INIT_FLAG_ENABLE_ALL),
      'decomposing the aggregate must yield exactly the seven summed bits, in ' +
        'ascending bit order. A result of eight names would mean BLINKFAST had ' +
        'been folded in; a different order would break the element-by-element ' +
        'comparability the helper promises.',
    ).toEqual(SEVEN_SUMMED_FLAG_NAMES);

    expect(
      capabilityFlagsIn(INIT_FLAG_ENABLE_ALL),
      'the decomposed aggregate must not name BLINKFAST.',
    ).not.toContain('INIT_FLAG_ENABLE_BLINKFAST');

    // The aggregate sets no bit the legacy leaves undeclared, so its residual
    // against the union of the eight is empty. This also exercises the residual
    // arithmetic the network assertions depend on, with a value known in advance.
    expect(
      unrecognizedBitsIn(INIT_FLAG_ENABLE_ALL),
      'the aggregate is built only from declared bits, so nothing in it can ' +
        'be unrecognized. This checks the residual arithmetic itself against a ' +
        'known input before it is used against a live mask.',
    ).toBe(0);

    // Every declared bit is individually recognized, and an undeclared bit is
    // detected. 16 sits in the legacy's own gap between 8 and 256, which makes
    // it the smallest genuinely undeclared value and the sharpest test of the
    // residual — no name is invented for it, its presence is merely detected.
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      expect(
        unrecognizedBitsIn(CAPABILITY_FLAGS[name]),
        `${name} is a declared bit, so on its own it must leave no residual.`,
      ).toBe(0);
    }

    expect(
      unrecognizedBitsIn(16),
      'a bit in the undeclared gap between 8 and 256 must be reported as a ' +
        'residual. The decomposition helper cannot surface it — it reports ' +
        'named bits only, and the legacy names nothing at 16 — so the residual ' +
        'is the sole mechanism that makes an unknown bit fail loudly.',
    ).toBe(16);
  });

  /*
   * -------------------------------------------------------------------------
   * The projection over HTTP — these need the stack up
   * -------------------------------------------------------------------------
   * Each test acquires its own token and makes its own request. The repetition
   * is deliberate: any one of these can be run alone with `--grep`, and a token
   * held in module scope would be shared state in a file that otherwise has
   * none, expiring midway through a run for reasons no failure message would
   * explain.
   *
   * A token is sent on every request, which is the correct posture whether or
   * not this endpoint were anonymous, so these tests are robust to that either
   * way. The unauthenticated-boundary assertion is *not* repeated here — it is
   * made once, in assertion group 2, on `/v1/ping`.
   */

  test('/v1/capabilities reports the aggregate as 3847 with BLINKFAST clear', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      headers: bearerHeaders(token),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'an authenticated read of /v1/capabilities must answer 200. A 401 would ' +
        'mean the token minted by the sole issuer was not accepted here; a 404 ' +
        'would mean the route is not mapped; a 5xx would mean the composition ' +
        'root faults while projecting its own configuration.',
    ).toBe(200);

    const report: Record<string, unknown> = decodeReport(await response.text());

    // ---------------------------------------------------------------------
    // The aggregate. This is the assertion the whole file exists for.
    // ---------------------------------------------------------------------
    const allMask: number = requireNumber(
      readNumber(report, ALL_MASK_KEYS),
      ALL_MASK_KEYS,
      'the aggregate INIT_FLAG_ENABLE_ALL',
    );

    expect(
      allMask,
      'the reported aggregate must be 3847. The published schema fixes it as a ' +
        'constant precisely because it is settled by ' +
        'ws_objects/pfw.shared.pbl.src/enums.sru:L49 rather than by this ' +
        'deployment. A reported 3855 would mean the service folded ' +
        'INIT_FLAG_ENABLE_BLINKFAST into a seven-term sum that deliberately ' +
        'omits it — a behavioural change, not a corrected total.',
    ).toBe(INIT_FLAG_ENABLE_ALL);

    expect(
      allMask & INIT_FLAG_ENABLE_BLINKFAST,
      'the reported aggregate must not carry the BLINKFAST bit. Asserted on ' +
        'the value that came off the wire, not on the local constant, so the ' +
        'omission is pinned in the projection rather than only in this suite.',
    ).toBe(0);

    // ---------------------------------------------------------------------
    // The effective mask, and the accounting of every bit in it.
    // ---------------------------------------------------------------------
    const effectiveMask: number = requireNumber(
      readNumber(report, EFFECTIVE_MASK_KEYS),
      EFFECTIVE_MASK_KEYS,
      'the effective capability mask',
    );

    expect(
      Number.isInteger(effectiveMask),
      'the effective mask must be an integer; a fractional value cannot be a ' +
        'bitmask and would make every containment test below meaningless.',
    ).toBe(true);

    // The domain is the full UNSIGNED 32-bit range, as the schema declares:
    // the configured value is projected with an unchecked conversion, so a
    // negative configured value wraps rather than failing. Asserted because the
    // fixture's helpers reject an out-of-domain mask with a TypeError, which
    // would read as a fixture defect rather than as the malformed payload it is.
    expect(
      effectiveMask,
      'the effective mask must not be negative. The wire domain is unsigned ' +
        '32-bit, so a negative value means the projection was serialized as a ' +
        'signed integer somewhere along the way.',
    ).toBeGreaterThanOrEqual(0);

    expect(
      effectiveMask,
      'the effective mask must fit the unsigned 32-bit domain the schema ' +
        'declares. A larger value cannot have come from that projection at all.',
    ).toBeLessThanOrEqual(MAX_UINT32);

    // No bit is set that the legacy leaves undeclared. The decomposition helper
    // cannot detect this on its own — it reports named bits only, so an unknown
    // bit is simply absent from its result rather than flagged — which is why
    // the residual is computed bitwise and asserted separately.
    const unrecognized: number = unrecognizedBitsIn(effectiveMask);

    expect(
      unrecognized,
      'the effective mask must set no bit outside the eight the legacy ' +
        'declares. The legacy declares nothing at 16, 32, 64 or 128 and ' +
        'nothing above 2048, so a residual here means the gate was configured ' +
        'with a value that names no capability — reported by the service as ' +
        'data rather than rejected, and surfaced here as a failure because ' +
        'this suite configures the stack through the documented compose path.',
    ).toBe(0);

    // Cross-check against the member the service computes for itself, when it is
    // present. The schema does not mark it required, so its absence is tolerated
    // and only its disagreement is a failure.
    const reportedUnrecognized: number | undefined = readNumber(
      report,
      UNRECOGNIZED_BITS_KEYS,
    );

    if (reportedUnrecognized !== undefined) {
      expect(
        reportedUnrecognized,
        'the unrecognized-bit count the service reports must agree with the ' +
          'residual computed here from the same mask. A disagreement means one ' +
          'of the two is using a different idea of which bits are declared.',
      ).toBe(unrecognized);
    }

    // Complete accounting: every bit of the mask is either one of the eight
    // named bits or part of the residual, with nothing left over and nothing
    // counted twice.
    const namedBits: number = capabilityFlagsIn(effectiveMask).reduce(
      (union: number, name: CapabilityFlagName): number =>
        union | CAPABILITY_FLAGS[name],
      0,
    );

    expect(
      (namedBits | unrecognized) >>> 0,
      'the named bits and the residual together must reconstitute the mask ' +
        'exactly. This is what proves the decomposition lost nothing: a bit ' +
        'that appeared in neither would be silently invisible to every other ' +
        'assertion in this file.',
    ).toBe(effectiveMask);

    // Conditional on purpose. Whether the deployment enables everything is
    // configuration, and no individual bit is asserted on or off anywhere in
    // this file (C-B). But *if* the gate is the legacy aggregate, then the
    // omission must hold on the wire too.
    if (effectiveMask === INIT_FLAG_ENABLE_ALL) {
      expect(
        hasCapability(effectiveMask, INIT_FLAG_ENABLE_BLINKFAST),
        'a gate configured to the legacy aggregate must not carry BLINKFAST. ' +
          'The aggregate is a seven-term sum, so a mask equal to it and yet ' +
          'containing the eighth bit would be arithmetically impossible and ' +
          'would mean the reported mask and the reported aggregate were ' +
          'computed from different tables.',
      ).toBe(false);
    }
  });

  test('/v1/capabilities reports only the eight declared capabilities, consistent with the mask', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      headers: bearerHeaders(token),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'an authenticated read of /v1/capabilities must answer 200 before its ' +
        'contents can be asserted.',
    ).toBe(200);

    const report: Record<string, unknown> = decodeReport(await response.text());

    const effectiveMask: number = requireNumber(
      readNumber(report, EFFECTIVE_MASK_KEYS),
      EFFECTIVE_MASK_KEYS,
      'the effective capability mask',
    );

    const entries: readonly CapabilitySighting[] | undefined =
      normalizeCapabilityEntries(
        readCandidate(report, CAPABILITY_CONTAINER_KEYS),
      );

    expect(
      entries,
      'the report must carry a capability container under one of the candidate ' +
        `members [${formatCandidates(CAPABILITY_CONTAINER_KEYS)}], as either ` +
        'an array or a keyed map. The published schema marks it required, so ' +
        'its absence is a contract failure; a different name belongs in the ' +
        'candidate block at the top of this file.',
    ).toBeDefined();

    const sightings: readonly CapabilitySighting[] = entries ?? [];

    // The set is CLOSED by the legacy declaration — eight bits, no more and no
    // fewer — and the schema fixes the array at exactly eight items for that
    // reason. Note this is the closed set of DECLARED bits, which is eight, and
    // not the seven the aggregate sums: BLINKFAST is a declared capability that
    // the aggregate omits, so it is reported here while contributing nothing to
    // the aggregate.
    expect(
      sightings.length,
      'the report must describe exactly the eight declared capabilities. This ' +
        'is the closed set of bits the legacy declares at enums.sru:L41-L48, ' +
        'which is eight — not the seven the aggregate sums. A ninth entry ' +
        'would be an invented capability; a missing entry would hide a ' +
        'declared one.',
    ).toBe(ALL_CAPABILITY_FLAG_NAMES.length);

    const reportedNames: string[] = [];

    for (const sighting of sightings) {
      const name: string | undefined = sighting.name;

      expect(
        name,
        'every capability entry must carry an identifier under one of the ' +
          `candidate members [${formatCandidates(CAPABILITY_NAME_KEYS)}]. An ` +
          'entry without one cannot be matched against the legacy table at all.',
      ).toBeDefined();

      if (name === undefined) {
        continue;
      }

      reportedNames.push(name);

      // THE SUBSET PROPERTY. Every reported identity is one of the eight
      // verbatim legacy names — so no capability is invented, and no capability
      // is named after a service (C-D).
      expect(
        isCapabilityFlagName(name),
        `'${name}' is not one of the eight capability identifiers the legacy ` +
          'declares. The reported identities must be a subset of that closed ' +
          'set: an unknown identity is either an invented capability or a ' +
          'renamed one, and a rename silently invalidates every ' +
          'characterization recording that already stores the old spelling.',
      ).toBe(true);

      if (!isCapabilityFlagName(name)) {
        continue;
      }

      const expectedValue: number = CAPABILITY_FLAGS[name];

      // Where the shape carries a value alongside the name, it must be the
      // legacy value for that name.
      if (sighting.value !== undefined) {
        expect(
          sighting.value,
          `the reported value for ${name} must be the value ` +
            'enums.sru:L41-L48 declares. A name and a value that disagree ' +
            'would let a consumer keyed on one diverge from a consumer keyed ' +
            'on the other.',
        ).toBe(expectedValue);
      }

      // Where the shape carries an enabled flag, it must agree with the mask.
      // This is a RELATIONSHIP between two reported members, so it holds under
      // every deployment configuration and asserts nothing about which bits are
      // switched on (C-B).
      if (sighting.enabled !== undefined) {
        expect(
          sighting.enabled,
          `the enabled flag for ${name} must agree with the effective mask, ` +
            'which the schema defines it in terms of. This is a consistency ' +
            'check between two members of the same report — it says nothing ' +
            'about whether this deployment enables the bit, only that the two ' +
            'ways of reading the gate cannot contradict each other.',
        ).toBe(hasCapability(effectiveMask, expectedValue));
      }
    }

    expect(
      new Set(reportedNames).size,
      'the reported identifiers must be distinct. A duplicated entry would ' +
        'make one bit assert twice and leave another unasserted.',
    ).toBe(reportedNames.length);

    // Every declared bit appears. Combined with the subset property and the
    // distinctness check above, this makes the reported set exactly the eight.
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      expect(
        reportedNames,
        `${name} is one of the eight declared capabilities and must appear in ` +
          'the report. The report describes the closed declared set, so a bit ' +
          'the aggregate omits is still reported — its omission from the ' +
          'aggregate is a separate fact, asserted separately.',
      ).toContain(name);
    }
  });

  test('/v1/capabilities names no deferred service and no deferred route family', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      headers: bearerHeaders(token),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'an authenticated read of /v1/capabilities must answer 200 before its ' +
        'contents can be asserted.',
    ).toBe(200);

    const bodyText: string = await response.text();
    const report: Record<string, unknown> = decodeReport(bodyText);
    const inventory: BodyInventory = inventoryOf(report);

    // Clause 1 — no MEMBER NAME anywhere is a deferred service name. A member
    // named after a deferred service would be that service acquiring a presence
    // in a published payload, which is what C-D forbids.
    for (const deferredName of DEFERRED_SERVICE_NAMES) {
      expect(
        inventory.keys,
        `no member of the capability report may be named '${deferredName}'. ` +
          'The four deferred capability areas have no project, no container, ' +
          'no test and no implementation in this phase, and a member named ' +
          'after one would give a consumer somewhere to look for it.',
      ).not.toContain(deferredName);
    }

    // Clause 2 — the two deferred names that the destination value set does NOT
    // contain may not appear at all, in any position. For these the strict
    // whole-body reading holds exactly, so it is applied to the raw text.
    for (const deferredName of NEVER_MENTIONED_DEFERRED_NAMES) {
      expect(
        bodyText,
        `'${deferredName}' must not appear anywhere in the capability report. ` +
          'It is not a member of the contract-declared destination value set, ' +
          'so there is no legitimate position for it in this payload at all.',
      ).not.toContain(deferredName);
    }

    // Clause 3 — the two deferred names the destination value set DOES contain
    // may appear ONLY as the value of a contract-declared destination member.
    //
    // This is the reconciliation described on DEFERRED_SERVICE_NAMES above,
    // made executable. The contract requires the destination member and closes
    // its value set to four tokens, two of which name deferred areas, so a
    // conforming body does contain those strings and a blanket prohibition
    // would fail against correct code. What is forbidden is a deferred name
    // anywhere ELSE — which would be that area appearing as something other
    // than a documented future destination.
    for (const sighting of inventory.strings) {
      if (!DEFERRED_SERVICE_NAMES.includes(sighting.value)) {
        continue;
      }

      expect(
        CAPABILITY_DESTINATION_KEYS,
        `'${sighting.value}' names a deferred capability area and may appear ` +
          `only as a declared Phase-1 destination, but it was found under the ` +
          `member '${sighting.key}'. The destination member is a documented ` +
          'statement of where a capability will eventually live and creates no ' +
          'coupling; the same name in any other member would be the deferred ' +
          'area acquiring a real presence in the payload.',
      ).toContain(sighting.key);
    }

    // Clause 4 — no reserved Phase-2 route family. A capability report is
    // configuration, not a route table. The four reserved routes are declared
    // exactly once elsewhere, as metadata that answers "not implemented", and a
    // capability report that also advertised them would give a caller a second
    // and weaker place to discover them.
    for (const routeFamily of DEFERRED_ROUTE_FAMILIES) {
      expect(
        bodyText,
        `the capability report must not advertise the reserved route family ` +
          `'${routeFamily}'. Nothing in this payload may present a deferred ` +
          'capability as something a caller could call.',
      ).not.toContain(routeFamily);
    }
  });

  test('/v1/capabilities exposes no secret-shaped material', async ({
    request,
  }) => {
    const token: ServiceToken = await requireServiceToken(request);

    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      headers: bearerHeaders(token),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'an authenticated read of /v1/capabilities must answer 200 before its ' +
        'contents can be asserted.',
    ).toBe(200);

    // A cheap guard, and a targeted one. This endpoint projects CONFIGURATION,
    // and a configuration-projecting endpoint is exactly the kind that acquires
    // a leak by accident: one careless change that serializes the whole options
    // object instead of the gate, and the signing key travels with it. The
    // system holds exactly one signing secret, so there is exactly one thing
    // this guard is protecting.
    const lowerCasedBody: string = (await response.text()).toLowerCase();

    for (const marker of SECRET_SHAPED_MARKERS) {
      expect(
        lowerCasedBody,
        `the capability report must not contain '${marker}'. Only the marker ` +
          'is named here and never the surrounding text, because an assertion ' +
          'message reaches the console and the CI log and both are retained — ' +
          'quoting the body in order to report a suspected leak would publish ' +
          'the leak.',
      ).not.toContain(marker);
    }
  });
});
