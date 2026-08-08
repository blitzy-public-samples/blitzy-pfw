/**
 * The framework's eight-bit capability gate, transcribed for the end-to-end
 * suite.
 *
 * WHY THIS MODULE EXISTS
 * ----------------------
 * Gateway is the composition root, and it exposes the legacy framework's own
 * module-gating bitmask as configuration rather than hard-wiring it; the
 * `/v1/capabilities` endpoint projects that gate (AAP §0.3.4). A spec that
 * asserts on the projection needs the authoritative bit values, and it needs
 * them from ONE place: `services/` had no declared children when this fixture
 * was specified, so the exact JSON shape the endpoint returns could not be
 * read from the source tree. Declaring the table here — rather than inlining
 * numbers in each spec — is precisely what makes a single edit sufficient if
 * Gateway's serialization turns out to differ from expectation. Specs should
 * therefore assert against the values exported here and should carry no
 * assumption of their own about the response envelope.
 *
 * The request path itself is deliberately NOT declared here; endpoint paths
 * live in `service-endpoints.ts`. This module is pure data plus pure bitwise
 * arithmetic: no I/O, no network, no database, no secret, and no environment
 * read of any kind. It imports nothing, and it must stay that way — the
 * documented collection command (`npx playwright test --list`) loads it with
 * no stack running, so it carries zero module-scope side effects (C-L).
 * Nothing here starts, probes or gates a service; the single local bring-up
 * path is the orchestration manifest (C-J).
 *
 * THE AUTHORITATIVE SOURCE
 * ------------------------
 * `ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49`, under that file's own
 * comment `//Initialize flags (pfwInitialize:[flags])` at `:L40`. The values
 * are transcribed into literals with their per-line locator recorded beside
 * each one. The legacy tree is the behavioural oracle and is read-only
 * (C-C): it is never read at run time, and the sibling `blink`, `sciter` and
 * `webview` directories under `tests/` are read-only oracle assets that this
 * module never touches.
 *
 * WHAT A CAPABILITY BIT ACTUALLY CONTROLS
 * ---------------------------------------
 * Per the read-only legacy specification `docs/README.md` §初始化 — and its
 * §高级初始化 subsection at `:L24` — a module that is not explicitly
 * initialized has its related functionality unusable AND its native library
 * need not be shipped at all (`:L26`), while enabling a bit whose library is
 * absent makes initialization FAIL outright (`:L30`). That is a hard failure,
 * not a degradation. The bitmask is therefore the legacy's own statement of
 * modularity, which is why mapping the bits onto the service roster
 * corroborates the Phase-1 boundary rather than merely describing it
 * (AAP §0.1.4).
 *
 * TWO THINGS THAT MUST NOT BE "TIDIED"
 * ------------------------------------
 * 1. `INIT_FLAG_ENABLE_ALL` omits `INIT_FLAG_ENABLE_BLINKFAST`, deliberately.
 *    It is written below as the explicit sum of the seven constants the
 *    source sums, so the omission is visible in the code rather than hidden
 *    inside a total. Completing the sum would change behaviour, not correct a
 *    typo (C-B).
 * 2. Identifier spellings are verbatim (AAP §0.4.5.3): these exact strings
 *    appear in serialized payloads, in log records and in characterization
 *    recordings, where a rename would silently invalidate every stored
 *    comparison. SCREAMING_SNAKE is already idiomatic for TypeScript module
 *    constants, so — unlike the C# tree, which needs scoped analyzer
 *    suppressions for exactly this reason — no lint suppression is required
 *    or permitted in this file.
 *
 * Six of the eight bits name capabilities owned by deferred services. They
 * appear here strictly as numbers in a table, because the gate Gateway
 * exposes is eight bits wide. Nothing in this module reaches a deferred
 * capability: there is no base URL, no reserved-route constant, no client and
 * no per-capability helper (C-D). Each bit's destination is recorded in
 * `docs/ARCHITECTURE.md` §6.1, and the deferred assignment itself belongs to
 * `docs/DEFERRED.md`; this module restates neither as a live list.
 *
 * @see docs/ARCHITECTURE.md §6 — the capability-gate section, and the single
 * place the per-bit values, locators and destinations are tabulated
 * @see docs/DEFERRED.md §9 — capability gating read as corroboration of the
 * Phase-1 boundary
 */

/*
 * ---------------------------------------------------------------------------
 * The eight capability bits, transcribed verbatim
 * ---------------------------------------------------------------------------
 * Each is declared in the source as a `Constant Long` — PowerScript's signed
 * 32-bit integer. That is why the domain guard further down accepts exactly
 * that range, and why 32-bit bitwise arithmetic reproduces the legacy's own
 * combination semantics with nothing lost.
 *
 * The values jump from 8 straight to 256. That gap is real: the source
 * declares no flag at 16, 32, 64 or 128. No flag is invented to fill it, and
 * the values are not compacted to close it (C-B).
 *
 * The "owned by" note on each constant is quoted from `docs/ARCHITECTURE.md`
 * §6.1, the one place those destinations are tabulated — `docs/DEFERRED.md`
 * §9 defers to it explicitly rather than duplicating it.
 */

/** User-interface module. `enums.sru:L41`. Owned by DesignSystem (deferred). */
export const INIT_FLAG_ENABLE_UI = 1;

/** Sciter engine. `enums.sru:L42`. Owned by ScriptBridge (deferred). */
export const INIT_FLAG_ENABLE_SCITER = 2;

/** MiniBlink engine. `enums.sru:L43`. Owned by ScriptBridge (deferred). */
export const INIT_FLAG_ENABLE_BLINK = 4;

/**
 * The alternative MiniBlink build. `enums.sru:L44`. Owned by ScriptBridge
 * (deferred).
 *
 * This is the one bit {@link INIT_FLAG_ENABLE_ALL} does not include. See that
 * constant for why, and for why the omission is reproduced rather than fixed.
 */
export const INIT_FLAG_ENABLE_BLINKFAST = 8;

/**
 * ORCA build tooling. `enums.sru:L45`. Legacy packaging tooling, which is not
 * a service at all and is permanently out of scope.
 */
export const INIT_FLAG_ENABLE_ORCA = 256;

/**
 * SQLite storage. `enums.sru:L46`.
 *
 * Owned by Persistence, and the ONLY one of the eight bits with an in-scope
 * consumer — see {@link IN_SCOPE_CAPABILITY_FLAGS}.
 */
export const INIT_FLAG_ENABLE_SQLITE = 512;

/** DPI awareness. `enums.sru:L47`. Owned by DesignSystem (deferred). */
export const INIT_FLAG_ENABLE_DPIAWARE = 1024;

/** WebView embedding. `enums.sru:L48`. Owned by ScriptBridge (deferred). */
export const INIT_FLAG_ENABLE_WEBVIEW = 2048;

/**
 * The composite "everything on" flag, `enums.sru:L49` — and the value the
 * legacy application actually passes, at `ws_objects/pfw.pbl.src/pfw.sra:L91`
 * (`pfwInitialize(Enums.INIT_FLAG_ENABLE_ALL)`).
 *
 * Written as the explicit sum of the SEVEN constants the source sums, in the
 * source's own order, so that the absence of
 * {@link INIT_FLAG_ENABLE_BLINKFAST} is structurally visible here instead of
 * being buried inside a total:
 *
 *     1 + 2 + 4 + 256 + 512 + 1024 + 2048 = 3847
 *
 * The omission is deliberate legacy design and is reproduced, not corrected
 * (C-B). The fast and the standard MiniBlink binaries are alternative builds
 * of ONE engine rather than two independent capabilities, so an
 * "everything on" constant that enabled both would be incoherent. Adding the
 * eighth bit would therefore change behaviour; it would not fix a typo.
 *
 * This composite is deliberately NOT a member of {@link CAPABILITY_FLAGS} or
 * of {@link CapabilityFlagName}: it is a combination of bits, not a bit.
 */
export const INIT_FLAG_ENABLE_ALL =
  INIT_FLAG_ENABLE_UI +
  INIT_FLAG_ENABLE_SCITER +
  INIT_FLAG_ENABLE_BLINK +
  INIT_FLAG_ENABLE_ORCA +
  INIT_FLAG_ENABLE_SQLITE +
  INIT_FLAG_ENABLE_DPIAWARE +
  INIT_FLAG_ENABLE_WEBVIEW;

/**
 * The name of one capability bit, spelled exactly as the legacy source spells
 * it.
 *
 * Eight members, one per bit. The composite `INIT_FLAG_ENABLE_ALL` is not one
 * of them, because it is not a bit.
 */
export type CapabilityFlagName =
  | 'INIT_FLAG_ENABLE_UI'
  | 'INIT_FLAG_ENABLE_SCITER'
  | 'INIT_FLAG_ENABLE_BLINK'
  | 'INIT_FLAG_ENABLE_BLINKFAST'
  | 'INIT_FLAG_ENABLE_ORCA'
  | 'INIT_FLAG_ENABLE_SQLITE'
  | 'INIT_FLAG_ENABLE_DPIAWARE'
  | 'INIT_FLAG_ENABLE_WEBVIEW';

/**
 * The capability table, keyed by the verbatim legacy identifier.
 *
 * Keyed by name on purpose: a spec can assert by identifier — whichever way
 * Gateway happens to serialize the gate, by name or by number — and read the
 * matching value from the same table, so the two never drift apart. The
 * shorthand initializers below tie every entry to the exported constant
 * above it, making these the single source of truth for both forms.
 *
 * Frozen, and typed `Readonly`, so no spec can mutate the table and leak a
 * changed value into another spec. The `Record<CapabilityFlagName, number>`
 * annotation is also a compile-time completeness check: omitting a bit here
 * fails to type-check.
 */
export const CAPABILITY_FLAGS: Readonly<Record<CapabilityFlagName, number>> =
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
 * All eight flag names in ascending bit-value order, which is also the order
 * the legacy source declares them in.
 *
 * The ordering is a contract, not a convenience: {@link capabilityFlagsIn}
 * inherits it rather than sorting, so a decomposed mask always reads from the
 * least significant bit upwards and two runs are directly comparable.
 *
 * Frozen and `readonly`, for the same reason the table is.
 */
export const ALL_CAPABILITY_FLAG_NAMES: readonly CapabilityFlagName[] =
  Object.freeze([
    'INIT_FLAG_ENABLE_UI',
    'INIT_FLAG_ENABLE_SCITER',
    'INIT_FLAG_ENABLE_BLINK',
    'INIT_FLAG_ENABLE_BLINKFAST',
    'INIT_FLAG_ENABLE_ORCA',
    'INIT_FLAG_ENABLE_SQLITE',
    'INIT_FLAG_ENABLE_DPIAWARE',
    'INIT_FLAG_ENABLE_WEBVIEW',
  ] as const);

/**
 * The capability bits that have a consumer inside the Phase-1 slice.
 *
 * Exactly one: storage. Reading the destination column of the table in
 * `docs/ARCHITECTURE.md` §6.1 makes the Phase-1 boundary fall out of the
 * legacy's own gating vocabulary — `INIT_FLAG_ENABLE_SQLITE` is consumed by
 * Persistence and nothing else in this phase consumes any bit at all. That is
 * independent corroboration that the slice is drawn along a seam the legacy
 * itself recognised, rather than one imposed on it (AAP §0.1.4,
 * `ARCHITECTURE.md` §6.4).
 *
 * There is deliberately no counterpart list of deferred bits. The deferred
 * assignment is documented in `docs/DEFERRED.md`, and duplicating it here as
 * a live list would describe deferred services in a third place (C-D). A spec
 * that needs "the other bits" derives them from
 * {@link ALL_CAPABILITY_FLAG_NAMES}.
 */
export const IN_SCOPE_CAPABILITY_FLAGS: readonly CapabilityFlagName[] =
  Object.freeze(['INIT_FLAG_ENABLE_SQLITE'] as const);

/*
 * ---------------------------------------------------------------------------
 * Pure bitwise helpers
 * ---------------------------------------------------------------------------
 * Both exported functions are deterministic, referentially transparent and
 * free of side effects: the same arguments always produce the same answer,
 * nothing outside the arguments is observed, and nothing is mutated —
 * including the frozen table above. Their one non-local behaviour is the
 * domain rejection described below, which is a thrown error rather than a
 * silent effect.
 */

/**
 * The inclusive upper bound of the legacy domain.
 *
 * Every flag above is declared in the source as a `Constant Long`, and
 * PowerScript's `Long` is a signed 32-bit integer. The bound is enforced
 * rather than assumed for two independent reasons. JavaScript's bitwise
 * operators coerce their operands to 32 bits, so a value beyond this range
 * would be silently truncated to a different number and `&` would answer a
 * question nobody asked. And a capability mask is a combination of
 * non-negative bits, so a negative value cannot have come from the gate at
 * all.
 *
 * Module-private: it describes the legacy numeric type rather than the
 * capability set, so it is not part of this fixture's surface.
 */
const LONG_MAX = 0x7fffffff;

/**
 * Reject a value that cannot be a legacy capability mask or flag.
 *
 * Deliberately fail-fast rather than forgiving. A mask normally arrives from a
 * decoded JSON response, where an absent field yields `undefined` and
 * `Number(undefined)` yields `NaN`; left unchecked, `NaN & flag` is `0`, so
 * every capability test would answer a confident, silent "absent" and the
 * resulting assertion failure would read as a service defect rather than as
 * the malformed payload it actually is. Throwing names the offending
 * parameter instead. The framework being migrated takes the same posture — a
 * structural fault terminates rather than degrades — and the sibling runner
 * configuration validates its own base URL exactly this way.
 *
 * The throw is inside a function body, so merely importing this module still
 * has no side effect of any kind (C-L).
 *
 * @param value the candidate mask or flag
 * @param parameterName the caller-facing parameter name, used in the message
 * @throws TypeError when the value is not an integer within `0 .. LONG_MAX`
 */
function assertMaskDomain(value: number, parameterName: string): void {
  if (!Number.isInteger(value) || value < 0 || value > LONG_MAX) {
    throw new TypeError(
      `capability-flags: \`${parameterName}\` must be an integer in the ` +
        `PowerScript \`Long\` range 0..${LONG_MAX}, received ` +
        `${String(value)}.`,
    );
  }
}

/**
 * Report whether a capability mask has every bit of `flag` set.
 *
 * `flag` may be a single bit or a combination of bits, so
 * `hasCapability(mask, INIT_FLAG_ENABLE_ALL)` answers "are all seven of that
 * composite's bits present", not "is any one of them present". That is the
 * containment test `(mask & flag) === flag`, which is why the comparison is
 * against `flag` rather than against zero.
 *
 * A `flag` of zero returns `false`. `(mask & 0) === 0` holds for every
 * conceivable mask, so an unguarded test would hand back a confident `true`
 * for a flag a caller derived dynamically and got wrong — a vacuous pass, and
 * the worst possible outcome in a test fixture. The guard converts that into
 * an ordinary, visible failure.
 *
 * @param mask a capability mask, typically as projected by Gateway
 * @param flag the bit, or combination of bits, to look for
 * @returns `true` when every bit of `flag` is set in `mask`; `false` when
 * `flag` is zero or any of its bits is missing
 * @throws TypeError when either argument is outside the legacy `Long` domain
 *
 * @example
 * // Storage is the one capability with an in-scope consumer.
 * hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_SQLITE); // true
 * // The composite omits the alternative engine build, deliberately.
 * hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_BLINKFAST); // false
 */
export function hasCapability(mask: number, flag: number): boolean {
  assertMaskDomain(mask, 'mask');
  assertMaskDomain(flag, 'flag');

  if (flag === 0) {
    return false;
  }

  return (mask & flag) === flag;
}

/**
 * Decompose a capability mask into the names of the bits it contains.
 *
 * This exists so a failing assertion reports *which bits* rather than which
 * integer: `expect(capabilityFlagsIn(mask)).toContain('INIT_FLAG_ENABLE_SQLITE')`
 * fails with a readable list, whereas an integer comparison fails with two
 * four-digit numbers and no indication of which bit moved.
 *
 * Ordering is inherited from {@link ALL_CAPABILITY_FLAG_NAMES} — ascending bit
 * value, the legacy declaration order — and is never re-sorted, so results
 * from two different runs line up element by element.
 *
 * Bits outside the eight named flags are NOT reported, because the legacy
 * table gives them no name: the source declares nothing at 16, 32, 64 or 128,
 * and inventing a name to cover such a bit would be exactly the kind of
 * well-meant embellishment the migration forbids (C-B). A mask carrying an
 * unnamed bit therefore decomposes to the named bits it does contain, and a
 * spec that needs to detect the surplus compares the mask numerically.
 *
 * @param mask a capability mask, typically as projected by Gateway
 * @returns a fresh, caller-owned array of the contained flag names, in
 * ascending bit order; empty when no named bit is set
 * @throws TypeError when `mask` is outside the legacy `Long` domain
 *
 * @example
 * capabilityFlagsIn(INIT_FLAG_ENABLE_SQLITE | INIT_FLAG_ENABLE_UI);
 * // => ['INIT_FLAG_ENABLE_UI', 'INIT_FLAG_ENABLE_SQLITE']
 */
export function capabilityFlagsIn(mask: number): CapabilityFlagName[] {
  assertMaskDomain(mask, 'mask');

  return ALL_CAPABILITY_FLAG_NAMES.filter((name) =>
    hasCapability(mask, CAPABILITY_FLAGS[name]),
  );
}
