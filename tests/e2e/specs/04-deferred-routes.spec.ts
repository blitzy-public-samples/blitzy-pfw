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

import { requireLiveStack } from '../fixtures/live-stack';

import {
  DEFERRED_SERVICE_TOKENS,
  JSON_MEDIA_TYPE,
  RESERVED_ROUTE_BODY,
  RESERVED_ROUTE_MARKER,
  RESERVED_ROUTE_RET_CODE,
  RESERVED_ROUTE_STATUS,
  assertEnumToken,
  assertMediaType,
  assertMembers,
  describeShapeForFailure,
} from '../fixtures/contract-shape';

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
 * 🔴 A TOLERANT PATTERN HERE IS THE SAME DEFECT, IN THE PLACE IT COSTS MOST.
 * `/reserved[\s-]+for[\s-]+phase[\s-]*2/i` matched against the serialized body
 * accepts `phase-2`, `Phase2` and `PHASE 2`, on the reasoning that separators and
 * casing are not what the assertion is about.
 * `ReservedRouteBody.marker` is a schema `const` — the exact string below — and a
 * client branching on a reserved route compares it ORDINALLY. A Gateway emitting
 * `phase-2` would therefore satisfy such an assertion and fail every generated
 * client, which is precisely what a contract test must not permit.
 *
 * So the constant is stated once, exactly as the schema fixes it, and the member
 * is read by name off the parsed body rather than searched for in its text.
 */
const PHASE_2_MARKER = 'reserved for Phase 2';

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
  // `POST /v1/tokens` on Security is authenticated by a presented caller
  // credential - an HTTP Basic credential or a trusted client certificate - and
  // never by a bearer token, on every topology including the local bring-up, so
  // with no identity provisioned every authenticated assertion below is
  // unrunnable. The
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

      // Machine-readable begins with the media type, and it is asserted
      // EXACTLY. `contentType.toLowerCase().includes('json')` is the tempting form, chosen
      // "so that a JSON-family type is accepted while HTML and plain text are
      // not" — but a `text/html` page whose type parameter mentioned json
      // satisfied it, and a family match cannot tell a problem document from a
      // plain JSON body. The published response declares one of the two JSON
      // media types, so one of the two is what is accepted, exactly, with only
      // `charset=utf-8` permitted beside it.
      //
      // `application/json` AND NOT `application/problem+json`, which is a
      // distinction the contract makes deliberately rather than an accident of
      // implementation. `components/responses/ReservedForPhaseTwo` declares
      // exactly one content type and it is `application/json`, while every
      // genuine refusal on this service — `Unauthorized`, `Conflict` and the
      // rest — declares `application/problem+json`. Gateway's own decision D1
      // records the reason: THE RESERVED BODY IS A DECLARATION WITH A FIXED
      // SHAPE, NOT A PROBLEM DOCUMENT. A reserved route answering with a problem
      // document would be reporting a fault where the contract says it is
      // reporting a plan, so asserting the exact type is asserting that
      // distinction survived.
      assertMediaType(
        response.headers()['content-type'],
        JSON_MEDIA_TYPE,
        `${probePath}`,
      );

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

      // THE BODY'S MEMBER SET, EXACTLY, WHICH ASSERTING AGAINST THE SERIALIZED TEXT
      // LEAVES ENTIRELY OPEN: the destination "named" by a case-insensitive substring
      // anywhere in it, the marker matched by a tolerant pattern, the status inferred
      // from the response line. A body of `{"message":"DesignSystem is reserved for
      // Phase 2"}` satisfies every one of those — prose in a JSON wrapper, which is
      // exactly what "machine-readable" is supposed to rule out.
      //
      // `ReservedRouteBody` declares six required members and sets
      // `additionalProperties: false`, so the member set is assertable and a
      // response that carried its meaning in prose instead now fails.
      const reserved: Record<string, unknown> = assertMembers(
        JSON.parse(body) as unknown,
        RESERVED_ROUTE_BODY,
        `the 501 body for ${probePath}`,
      );

      // The declaration names its own destination, and the name is read from
      // the member that carries it and checked against the closed enumeration —
      // not looked for anywhere in the text. `deferredService` is the member the
      // contract declares for this, so a body that mentioned the area only in a
      // human-readable sentence no longer satisfies the claim that the reserved
      // roster is legible to a MACHINE.
      const deferredService: string = assertEnumToken(
        reserved['deferredService'],
        DEFERRED_SERVICE_TOKENS,
        `the 501 body's 'deferredService' for ${probePath}`,
      );

      expect(
        deferredService,
        `the 501 body for ${probePath} must name ${route.deferredService} as ` +
          `the capability area reserved behind it. Without the name the ` +
          `response says only that something is unimplemented, and the ` +
          `reserved roster stops being legible from the running system.`,
      ).toBe(route.deferredService);

      // `service` carries the same enumeration and must agree with it. Two
      // members for one fact is the contract's choice, and a body whose two
      // halves disagreed would be a body no client could trust either half of.
      expect(
        assertEnumToken(
          reserved['service'],
          DEFERRED_SERVICE_TOKENS,
          `the 501 body's 'service' for ${probePath}`,
        ),
        `the 501 body for ${probePath} carries the destination twice — as ` +
          `'service' and as 'deferredService' — and the two must agree.`,
      ).toBe(route.deferredService);

      // The echoed route, exact — and it is the REQUESTED PATH rather than the
      // route pattern. The contract calls the member "the matched route pattern,
      // echoed so a client can log what it asked for", and Gateway's decision D6
      // resolves the ambiguity in favour of the request path without its query
      // string. That is the more useful of the two for the stated purpose: four
      // handlers each echoing their own pattern would be indistinguishable from
      // one handler echoing a pattern it derived, whereas an echoed path a client
      // can compare with what it sent correlates the refusal exactly.
      //
      // A single handler answering every reserved path could name the right area
      // while echoing the wrong path, and a client correlating a refusal with its
      // request would then correlate it with the wrong one — which is what this
      // assertion rules out.
      expect(
        reserved['route'],
        `the 501 body for ${probePath} must echo the path that was requested, ` +
          `which is how a client correlates the refusal with the request it made.`,
      ).toBe(probePath);

      // The status echoed in the body, as a NUMBER, agreeing with the response
      // line. `const: 501` on the schema. A document disagreeing with its own
      // transport status is worse than no document: a client branching on the
      // parsed value takes a different path from one branching on the status.
      expect(
        reserved['status'],
        `the 501 body for ${probePath} must echo status ${RESERVED_ROUTE_STATUS} ` +
          `as a number, agreeing with the response line.`,
      ).toBe(RESERVED_ROUTE_STATUS);

      // The legacy return code, exact. `const: -2001` is `E_NO_IMPLEMENTATION`
      // from `retcode.sru`, so the reservation reports itself in the SAME algebra
      // the migrated framework uses everywhere else rather than inventing a
      // parallel vocabulary for the one case that has no implementation.
      expect(
        reserved['retCode'],
        `the 501 body for ${probePath} must carry retCode ` +
          `${RESERVED_ROUTE_RET_CODE}, the legacy E_NO_IMPLEMENTATION, so a ` +
          `caller reading the framework's own return-code algebra sees the same ` +
          `answer the transport gave.`,
      ).toBe(RESERVED_ROUTE_RET_CODE);

      // The marker, compared for EQUALITY against the contract's constant, and read
      // by member name off the parsed body rather than searched for in its text. The
      // former pattern accepted `Phase 2`, `phase-2` and `Phase2` in any casing
      // "because the assertion is made against the serialized body, where the
      // marker sits inside JSON punctuation" — true of a text search, and no
      // longer relevant now that the member is read by name. `const: reserved
      // for Phase 2` means there is exactly one conforming value, so a separator or
      // casing variant is a DIFFERENT message that no generated client can branch on
      // rather than another spelling of this one.
      expect(
        reserved['marker'],
        `the 501 body for ${probePath} must carry the reservation marker the ` +
          `contract fixes as a constant: '${RESERVED_ROUTE_MARKER}'. It is what ` +
          `distinguishes a deliberately reserved route from any other ` +
          `unimplemented one, and a client branches on it rather than on prose.`,
      ).toBe(RESERVED_ROUTE_MARKER);

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

      // THE TEXT-PRESENCE COROLLARY IS KEPT, AND IT IS NOT TOLERANT. The
      // exact equality against `marker` above is the acceptance criterion; this is a
      // corroboration that the exact constant appears in the serialized body at all,
      // which catches the one case equality cannot: a projection that had moved the
      // marker into a different member would fail the exact check with "missing
      // member" while this one shows the text is present, and the two messages
      // together name the drift precisely. It searches ORDINALLY for the constant the
      // schema fixes, not for a pattern — the tolerant form,
      // `/reserved[\s-]+for[\s-]+phase[\s-]*2/i`, accepts `phase-2` and `Phase2`,
      // and a separator or casing variant is a different message that no generated
      // client branching on a schema `const` can read, not a spelling of this one. It
      // is deliberately the weaker of the two checks and would be worthless alone.
      expect(
        body.includes(PHASE_2_MARKER),
        `the 501 body for ${probePath} must carry the reservation marker text ` +
          `'${PHASE_2_MARKER}' somewhere in it as well as in its 'marker' member. ` +
          `If the exact assertion above passed and this one failed, the body is not ` +
          `what it was serialized from. ${describeShapeForFailure(body)}.`,
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
