/**
 * Reserved deferred-capability routes — assertion group 4 of 6. Constraint C-D,
 * contract C-09.
 *
 * WHAT IS UNDER TEST, STATED BEFORE ANYTHING ELSE
 * ----------------------------------------------
 * Gateway declares four reserved extension points. Each answers `501 Not
 * Implemented` with a machine-readable body that names the capability area a
 * later phase would own, and carries the Phase-2 reservation marker the contract
 * fixes as a constant.
 *
 *   /v1/design/**        DesignSystem
 *   /v1/documents/**     Documents
 *   /v1/integration/**   Integration
 *   /v1/scripting/**     ScriptBridge
 *
 * **What is under test is the DECLARATION.** Not a capability, not a feature,
 * not a service — the declaration, and the fact that it declines. Four route
 * entries exist in Gateway's routing table; behind each one there is no service
 * directory, no project file, no container definition, no test project, no
 * partial implementation and no exception-throwing placeholder class. A route
 * declaration is not a stub of the thing it names, and this file is the only
 * automated check in the repository that the four behave exactly as declared.
 *
 * That distinction is the governing constraint of the entire file (C-D). Four
 * of the eight services in the target roster are deliberately not built in this
 * phase, and the prohibition is absolute rather than a priority ordering: not
 * partially, and not "stubbed out". A half-built capability is worse than a
 * documented gap, because a gap is legible and a half-build is not. The four are
 * therefore represented in exactly two places in the whole refactor — as
 * destination assignments in the full-estate mapping (`docs/SERVICE_MAPPING.md`,
 * `docs/DEFERRED.md`) and as these four routes — and this spec verifies the
 * second of the two. It asserts the status and the body shape, and it asserts
 * nothing else, because there is nothing else that may exist to assert.
 *
 * WHY THE BODY ASSERTIONS MATTER AS MUCH AS THE STATUS
 * --------------------------------------------------
 * A bare `501` proves only that something answered. It does not distinguish a
 * deliberate reservation from a route that half-exists and fails, and those two
 * are precisely what has to be told apart here. So three body properties are
 * asserted, and each rules out a different way of getting `501` for the wrong
 * reason:
 *
 * 1. The body parses as JSON and is an object — a client branches on members
 *    rather than parsing prose, which is what "machine-readable" means. An HTML
 *    error page is a failure however correct its status line.
 * 2. The body names its OWN capability area and NONE of the other three. Four
 *    distinctly declared routes each name one destination; a single generic
 *    handler answering everything would name the same one, or all four, on every
 *    route. This is the assertion that proves the declarations are distinct.
 * 3. The body carries the Phase-2 reservation marker as a constant, so the
 *    reservation is machine-detectable and not inferred from a status code that
 *    a hundred other causes also produce.
 *
 * THE NEGATIVE GUARDS, AND WHY THEY ARE NOT DECORATION
 * ---------------------------------------------------
 * The body is also asserted to leak no implementation detail: no exception type
 * name, no stack frame, no project or assembly identifier, no filesystem path.
 * This is the assertion that keeps C-D *auditable* rather than merely asserted.
 * The most likely way the prohibition gets violated is not somebody building a
 * whole deferred service — it is somebody adding a class that throws on the
 * route, which produces a `501` too and satisfies every check above while being
 * exactly the placeholder the constraint forbids. A framework-rendered exception
 * dump is what that looks like on the wire, so its absence is asserted here
 * rather than assumed. Nothing in the body should ever come from an exception,
 * because nothing behind the route should ever run.
 *
 * The complementary half of the same audit — that no deferred project,
 * container or test directory exists — is deliberately NOT attempted here. That
 * is a repository-structure fact rather than an HTTP behaviour, and it belongs
 * to the build and CI layer, which is where a missing directory can actually be
 * observed. An HTTP suite guessing at the shape of the source tree would assert
 * something it cannot see.
 *
 * ONE TEST PER ROUTE, GENERATED FROM ONE TABLE
 * -------------------------------------------
 * `RESERVED_ROUTES` below is the single edit point for the four pairings, and it
 * has to agree with `docs/DEFERRED.md`, `docs/CONTRACTS.md` and the reserved
 * path items in `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml`. The
 * fixture barrel deliberately publishes no constant for any of these prefixes —
 * exactly one spec exercises them, and a shared constant would be a standing
 * invitation for a second consumer to appear — so declaring them here is the
 * intended design rather than a local workaround.
 *
 * The four assertions are generated by iterating that table rather than folded
 * into one test, because a failure has to name the route that regressed. A
 * single test looping four routes reports "the reserved routes are broken"; four
 * generated tests report which one, and stop at the first failing assertion of
 * that one only.
 *
 * WHY THE PROBE IS A CONCRETE SUB-PATH
 * -----------------------------------
 * `**` in the specification is a route PATTERN, not a path anybody requests. The
 * probe therefore joins a concrete segment onto each prefix, because a concrete
 * sub-path is what the pattern unambiguously covers. The bare prefix is not
 * probed: whether a catch-all route also matches its own empty remainder is a
 * routing-framework detail rather than a published behaviour, and asserting it
 * would test the framework instead of the declaration.
 *
 * WHY THE REQUEST IS AUTHENTICATED (C-G)
 * -------------------------------------
 * Every reserved route requires a token, and that is not incidental to this
 * file — an unauthenticated caller must not be able to enumerate which
 * capability areas are deferred, so the guard runs before the declaration
 * answers. A probe without a credential is therefore turned away at the
 * authentication boundary and never reaches the route, which would leave the
 * declaration itself unverified. Each test consequently mints a token at run
 * time and presents it per request, following the pattern the authentication
 * spec establishes. The anonymous half of that boundary is asserted there, on
 * `/v1/ping`, and is not repeated here.
 *
 * Every token in this suite is minted at run time by the sole issuer (C-F). No
 * token is embedded, none is signed locally, no key material or credential-
 * shaped literal appears below, and neither a token nor an `Authorization`
 * header is ever rendered into an assertion message, a console line or a report.
 *
 * WHAT IS DELIBERATELY NOT ASSERTED (C-B, C-H)
 * -------------------------------------------
 * `501` is asserted as exactly `501` — never as "5xx" and never as "not 200",
 * both of which pass on a `500` that means the opposite of a deliberate
 * reservation, and on a `404` that means the route was never declared at all. No
 * deep schema is asserted beyond the invariants listed above: the members a
 * client actually branches on are fixed by the published contract, and
 * re-stating a whole schema here would duplicate a conformance check that
 * belongs with the contract. No latency, throughput, availability or
 * service-level figure is asserted, because the repository publishes none
 * anywhere; the runner's timeouts are hang guards, not budgets. Nothing about
 * test coverage is asserted — that gate is a .NET concern measured from a
 * coverage report.
 *
 * And nothing here is skipped or marked pending. The behaviour these four tests
 * assert exists NOW: the declaration is present, it answers today, and it must
 * keep answering. A pending test would encode an expectation about a later
 * phase, which is the one thing this file must never do.
 *
 * LEGACY PROVENANCE
 * ----------------
 * The capability split these four routes reserve is not invented by the
 * refactor; the framework being decomposed already had it. Its initializer takes
 * a bitmask of eight capability flags, and a module that is not explicitly
 * enabled is unusable and need not ship at all
 * [ws_objects/pfw.shared.pbl.src/enums.sru:L41-L49]. The demonstration
 * application turned every one of them on in a single process
 * [ws_objects/pfw.pbl.src/pfw.sra:L91], which is the fused arrangement being
 * taken apart. Of those eight flags exactly one has a consumer in this phase;
 * the browser-engine and web-view flags belong to the capability area reserved
 * at `/v1/scripting/**`, and the user-interface and DPI flags to the one
 * reserved at `/v1/design/**`. These citations are provenance only. No legacy
 * file is read, globbed or fetched at run time by this suite, and the read-only
 * oracle assets that sit beside this directory are untouched by it (C-C).
 *
 * HOW IT IS RUN, AND WHAT IT NEVER DOES (C-J, C-L)
 * ----------------------------------------------
 *   cd tests/e2e && npm ci && npx playwright test
 *
 * The stack is brought up beforehand, and exclusively, by
 * `orchestration/docker-compose.yml`. Nothing in this file starts, stops,
 * restarts, builds, seeds or health-gates a service. Traffic enters through the
 * composition root on its documented ingress, because a suite that reaches
 * around the ingress stops exercising it — and Gateway is the only service that
 * declares these routes at all. Nothing here calls DataServices or Persistence.
 * Every import resolves inside `tests/e2e`.
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules
 * were provided. That is a finding, not an omission, and it is not licence to
 * lower the bar — the enterprise-standard baseline applies in its place, and its
 * operative clauses here are strict typing with no implicit `any`, no secret in
 * source or in any artifact this run produces, and an assertion set that is
 * deterministic and reviewable.
 */

import { expect, test } from '@playwright/test';

import {
  bearerHeaders,
  gatewayUrl,
  type ServiceToken,
} from '../fixtures';

import {
  assertTokenIssuanceProvisioned,
  requireServiceToken,
} from '../fixtures/token-issuance';

import { probeStackAvailability } from '../fixtures/live-stack';

// ---------------------------------------------------------------------------
// The reserved-route table — the single edit point of this file
// ---------------------------------------------------------------------------

/**
 * One reserved extension point: a route prefix, and the name of the capability
 * area a later phase would own behind it.
 *
 * Two fields, and there is deliberately nothing else. The name is the whole of
 * what the contract permits a reserved declaration to carry, because anything
 * further would begin to describe a capability that must not be modelled (C-D).
 */
interface ReservedRoute {
  /** The versioned route prefix, with a leading slash and no trailing slash. */
  readonly prefix: string;

  /**
   * The capability-area name the body must carry. Matched case-insensitively,
   * so this table fixes the spelling without making the assertion brittle about
   * it.
   */
  readonly deferredService: string;
}

/**
 * The four reserved extension points, and the only place in this suite where
 * they are written down.
 *
 * Exactly four, and these four. The table must agree with `docs/DEFERRED.md`,
 * `docs/CONTRACTS.md` and the reserved path items in
 * `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml`; where this and
 * those ever differ, those are right and this is wrong.
 *
 * `as const` freezes the shape at compile time so the entries cannot be mutated
 * by one test and read by another, and `satisfies` checks the table against the
 * declared type without widening the literal types away.
 */
const RESERVED_ROUTES = [
  { prefix: '/v1/design', deferredService: 'DesignSystem' },
  { prefix: '/v1/documents', deferredService: 'Documents' },
  { prefix: '/v1/integration', deferredService: 'Integration' },
  { prefix: '/v1/scripting', deferredService: 'ScriptBridge' },
] as const satisfies readonly ReservedRoute[];

/**
 * The concrete segment appended to each prefix to form the probed path.
 *
 * The specification writes each route as a prefix followed by `**`, which is a
 * pattern rather than a literal path. A single fixed segment is joined on here
 * so that all four probes have the identical shape and one edit changes all
 * four.
 *
 * The value is deliberately inert: it names no capability, requests no
 * operation and carries no payload, so the probe cannot be read as an attempt
 * to exercise anything behind the route. It also collides with none of the four
 * capability-area names, which matters because the assertion that a body names
 * its own area and not the others is made against the serialized body — and the
 * body echoes the path that was requested.
 */
const RESERVED_ROUTE_PROBE_SEGMENT = '/status';

/**
 * The Phase-2 reservation marker, as a pattern, and the ONLY place in this file
 * where the marker text is written.
 *
 * Tolerant by design on the two things that carry no meaning — letter case and
 * the separator between the word and the digit, so `Phase 2`, `phase 2`,
 * `phase-2` and `Phase2` all match — and strict on the thing that does: the
 * marker phrase itself. A pattern rather than an equality check because the
 * assertion is made against the serialized body, where the marker sits inside
 * JSON punctuation.
 *
 * No `g` flag: a global regular expression carries `lastIndex` between calls,
 * and this one is reused across four generated tests.
 */
const PHASE_2_MARKER_PATTERN = /reserved[\s-]+for[\s-]+phase[\s-]*2/i;

/**
 * The leak guards: patterns that must NOT match a reserved-route body, each
 * with a label so a failure says what was found rather than only that something
 * was.
 *
 * Every entry is a signature of code having RUN behind the route. Nothing
 * behind a reserved route runs, so none of these can appear unless something
 * was built there — which makes this table the executable form of the C-D
 * audit. The patterns are deliberately specific: each targets a distinctive
 * shape rather than a bare word, because a guard that fires on a legitimate
 * body would be removed by the next person who saw it fail, and a removed guard
 * audits nothing.
 */
const IMPLEMENTATION_DETAIL_PATTERNS = [
  {
    label:
      'the name of a throwing placeholder, which is exactly the form of ' +
      'partial implementation the constraint forbids',
    pattern: /not[\s_-]*implemented[\s_-]*exception/i,
  },
  {
    label: 'an exception type name',
    pattern: /\b[A-Z][A-Za-z0-9]*Exception\b/,
  },
  {
    label: 'a stack frame of the form "at Namespace.Type.Member("',
    pattern: /(?:^|[\s"'\\])at\s+[A-Za-z_][A-Za-z0-9_.]*\.[A-Za-z_][A-Za-z0-9_]*\s*\(/,
  },
  {
    label: 'a stack-trace or nested-error member',
    pattern: /stack[\s_-]*trace|inner[\s_-]*exception/i,
  },
  {
    label: 'a project, assembly or source-file name',
    pattern: /\.(?:cs|csproj|sln|slnx|dll|pdb)\b/i,
  },
  {
    label: 'a namespace-qualified identifier from the implementation',
    pattern: /\bPowerFramework\.[A-Za-z]/,
  },
  {
    label: 'a Windows filesystem path',
    pattern: /[A-Za-z]:\\{1,2}/,
  },
  {
    label: 'a build or container filesystem path',
    pattern: /(?:^|[\s"'])\/(?:app|src|usr|home|root|tmp|var|etc|opt|build|workspace)\//,
  },
] as const;

/**
 * `failOnStatusCode` is pinned to `false` on every request below, and here that
 * is a requirement rather than a preference: `501` is the EXPECTED outcome of
 * every request this file makes.
 *
 * Left to throw on a non-2xx, the runner would raise on all four probes before
 * a single assertion ran, and its message can quote a response body verbatim.
 * Pinning it false means every status is handled by an assertion in this file,
 * so exactly one message shape is produced, it quotes nothing, and a failure
 * reports which status actually arrived.
 */
const NEVER_THROW_ON_STATUS = { failOnStatusCode: false } as const;

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/**
 * Builds the path probed for one reserved route.
 *
 * @param route an entry of {@link RESERVED_ROUTES}
 * @returns the prefix with the probe segment joined on
 */
function reservedProbePath(route: ReservedRoute): string {
  return `${route.prefix}${RESERVED_ROUTE_PROBE_SEGMENT}`;
}

/**
 * The capability-area names belonging to the OTHER reserved routes.
 *
 * Derived from the same table the assertion under test was generated from, so
 * the "names its own area and none of the others" check cannot fall out of step
 * with the roster: adding a fifth entry extends both halves at once.
 *
 * @param route the entry being asserted
 * @returns the three remaining names, in table order
 */
function otherDeferredServiceNames(route: ReservedRoute): readonly string[] {
  return RESERVED_ROUTES.filter(
    (candidate) => candidate.deferredService !== route.deferredService,
  ).map((candidate) => candidate.deferredService);
}

/**
 * Case-insensitive containment, spelled out so the call sites read as intent.
 *
 * Case is ignored deliberately. The table fixes the spelling of each name, but
 * the assertion's subject is whether the body identifies its destination at
 * all — failing a run over a capitalisation difference would report a naming
 * quibble as a contract violation.
 *
 * @param haystack the serialized response body
 * @param needle a capability-area name from {@link RESERVED_ROUTES}
 * @returns whether the body mentions the name
 */
function mentionsCaseInsensitively(haystack: string, needle: string): boolean {
  return haystack.toLowerCase().includes(needle.toLowerCase());
}

/**
 * Classifies a response body as JSON, without throwing and without quoting it.
 *
 * Four outcomes rather than a boolean, because the three failure modes are
 * genuinely different and a reader of a failed run needs to know which one
 * occurred: `unparseable` is the HTML-error-page case, `array` and `primitive`
 * are valid JSON that no client can branch on by member name.
 *
 * The parser error is deliberately discarded rather than attached. A JSON
 * parser quotes the text it choked on, and a message that carried a response
 * body would write it into the console and into whatever collects that.
 *
 * @param text the serialized response body
 * @returns the JSON shape of the body
 */
function jsonShapeOf(text: string): 'object' | 'array' | 'primitive' | 'unparseable' {
  let parsed: unknown;

  try {
    parsed = JSON.parse(text) as unknown;
  } catch {
    return 'unparseable';
  }

  if (Array.isArray(parsed)) {
    return 'array';
  }

  return typeof parsed === 'object' && parsed !== null ? 'object' : 'primitive';
}

// ---------------------------------------------------------------------------
// The assertions
// ---------------------------------------------------------------------------

test.describe('Reserved deferred-capability routes (constraint C-D)', () => {
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

  // Every route asserted in this block is a DECLARATION in Gateway's routing
  // metadata and nothing more. There is no service directory, no project file,
  // no container definition, no test project, no partial implementation and no
  // exception-throwing placeholder class behind any of them. The declaration
  // exists so that the shape of the eventual system is legible from Gateway's
  // published contract; declining is the whole of its behaviour, and declining
  // is the whole of what is asserted.
  //
  // Deliberately NOT `mode: 'serial'`. Each assertion reads one route, mutates
  // nothing, shares no state with its siblings and acquires whatever credential
  // it needs for itself, so no order between them is meaningful. Serial
  // execution belongs to the specs that share row state.

  for (const route of RESERVED_ROUTES) {
    test(`${route.prefix}/** is declared as a reserved route and answers 501 naming ${route.deferredService}`, async ({
      request,
    }) => {
      const probePath: string = reservedProbePath(route);

      // A run-time credential from the sole issuer, acquired inside the test
      // that needs it and left to fall out of scope (C-G, C-F). The route
      // requires a token before it will answer, so an anonymous probe would be
      // turned away at the authentication boundary and would leave the
      // declaration itself unverified — the anonymous half of that boundary is
      // asserted in the authentication spec, on a Phase-1 endpoint, and is not
      // repeated here. The helper's own failures name the method, the URL and
      // the status without quoting a response value, so nothing wraps it.
      const token: ServiceToken = await requireServiceToken(request);

      const response = await request.get(gatewayUrl(probePath), {
        headers: bearerHeaders(token),
        ...NEVER_THROW_ON_STATUS,
      });

      // EXACTLY 501, and the exactness is the point. "5xx" would pass on a 500,
      // which means code ran and faulted — the opposite of a route that
      // deliberately declines. "Not 200" would additionally pass on a 404,
      // which means the declaration is absent altogether, and an absent
      // declaration is the failure this suite exists to catch: the reserved
      // routes are the only place in the running system where the deferred half
      // of the roster is visible at all.
      expect(
        response.status(),
        `${probePath} must answer exactly 501. A 404 would mean the reserved ` +
          `route is not declared at all, so nothing in the running system ` +
          `records that this capability area is out of scope for the phase. A ` +
          `500 would mean something behind the route ran and faulted, when ` +
          `nothing behind it should run. A 2xx would mean something is ` +
          `implemented there, which is the constraint violation this ` +
          `assertion exists to detect. A 401 would mean the token minted for ` +
          `this request was not accepted, and the fault would be at the ` +
          `authentication boundary rather than in the declaration.`,
      ).toBe(501);

      // Asserted separately and on purpose. The status check above is exact
      // today; this is the guard that a future edit loosening it still cannot
      // let a success through. `ok()` is true for any 2xx and false for 501, so
      // the two assertions fail for different reasons and neither subsumes the
      // other.
      expect(
        response.ok(),
        `${probePath} must not report success. A successful response means ` +
          `something exists behind a route that is only declared.`,
      ).toBe(false);

      // Machine-readable begins with the media type. A framework error page is
      // the classic way a route "answers" without carrying a body any client
      // can branch on, and it announces itself here. Matched on the substring
      // rather than on equality so that a JSON-family type is accepted while
      // HTML and plain text are not.
      const contentType: string = response.headers()['content-type'] ?? '';

      expect(
        contentType.toLowerCase().includes('json'),
        `${probePath} must answer with a JSON media type, because a client is ` +
          `required to branch on the body rather than parse prose. An HTML or ` +
          `plain-text response indicates a framework-rendered error page ` +
          `rather than the declared reservation.`,
      ).toBe(true);

      const body: string = await response.text();

      // Valid JSON AND an object. An array or a bare primitive parses
      // perfectly and still cannot be read by member name, so neither is
      // machine-readable in the sense the contract means.
      expect(
        jsonShapeOf(body),
        `${probePath} must answer with a JSON object. 'unparseable' means the ` +
          `body is not JSON at all — an HTML error page being the usual cause; ` +
          `'array' and 'primitive' mean it is valid JSON that no client can ` +
          `branch on by member name. The body is deliberately not quoted in ` +
          `this message.`,
      ).toBe('object');

      // The declaration names its own destination. That naming is what makes
      // the eventual shape of the system legible from the contract, and it is
      // the only thing about the destination the body is permitted to carry.
      expect(
        mentionsCaseInsensitively(body, route.deferredService),
        `the 501 body for ${probePath} must name ${route.deferredService} as ` +
          `the capability area reserved behind it. Without the name the ` +
          `response says only that something is unimplemented, and the ` +
          `reserved roster stops being legible from the running system.`,
      ).toBe(true);

      // And names NONE of the other three. This is the assertion that proves
      // the four are distinctly declared rather than sharing one generic
      // answer: a single handler answering every reserved path would name the
      // same area on all four, or would enumerate all four on each.
      for (const otherName of otherDeferredServiceNames(route)) {
        expect(
          mentionsCaseInsensitively(body, otherName),
          `the 501 body for ${probePath} must not mention ${otherName}. Each ` +
            `reserved route is declared separately and answers for its own ` +
            `capability area only; a body naming another area means the four ` +
            `share one generic response, so the route that was asked for ` +
            `cannot be told from the routing metadata. It would also let a ` +
            `single request enumerate the whole reserved roster.`,
        ).toBe(false);
      }

      // The reservation marker, so the reservation is machine-detectable
      // instead of inferred from a status code that many other causes also
      // produce.
      expect(
        PHASE_2_MARKER_PATTERN.test(body),
        `the 501 body for ${probePath} must carry the reservation marker the ` +
          `contract fixes as a constant, matching ${String(PHASE_2_MARKER_PATTERN)}. ` +
          `It is what distinguishes a deliberately reserved route from any ` +
          `other unimplemented one, and a client branches on it rather than on ` +
          `prose.`,
      ).toBe(true);

      // The leak guards. Each pattern is a signature of code having run behind
      // the route, and nothing behind a reserved route runs. The most likely
      // way the prohibition is violated is not a whole service appearing but a
      // class that throws on the route — which also answers 501 and satisfies
      // every assertion above while being exactly the placeholder that is
      // forbidden. Its absence is asserted here rather than assumed, which is
      // what keeps the prohibition auditable.
      for (const guard of IMPLEMENTATION_DETAIL_PATTERNS) {
        expect(
          guard.pattern.test(body),
          `the 501 body for ${probePath} matched ${String(guard.pattern)}, ` +
            `which is ${guard.label}. A reserved route has no handler beyond a ` +
            `constant body: it calls nothing and can reach nothing, so no ` +
            `implementation detail can legitimately appear in its response. A ` +
            `match here indicates that something was built behind the route ` +
            `and is reporting its own failure. The body is deliberately not ` +
            `quoted in this message.`,
        ).toBe(false);
      }

      // Nothing further is asserted, and the omission is the constraint rather
      // than an oversight. No capability behind the route is exercised, no
      // follow-up request is made, no header beyond the media type is read, and
      // nothing is asserted about WHEN any of this might change. The
      // declaration answered as declared; that is the entire contract.
    });
  }
});
