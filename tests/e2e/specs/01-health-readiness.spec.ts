/**
 * ==================================================================================================
 *  01-health-readiness.spec.ts — contract C-10, the health and readiness half.
 *
 *  Assertion group 1 of 6. Playwright is used here purely as an HTTP workflow driver: no browser is
 *  opened, no page is created, no snapshot is taken and no component library is involved. Phase 1
 *  creates no presentation surface at all, so there is nothing to render and nothing to compare a
 *  rendering against.
 *
 *  WHAT THIS FILE VERIFIES
 *  -----------------------
 *  `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` publishes `GET /health` as the one
 *  operation in the document that overrides the bearer requirement with an empty requirement list,
 *  and it makes three claims this file asserts in order:
 *
 *    A. `/health` is ANONYMOUS on Gateway, and `Healthy` is answered with 200 — the only verdict that
 *       is. A `401` here would mean the single documented anonymous path had been closed; a `503`
 *       would mean the stack is not ready.
 *    B. Gateway's `/health` returns an AGGREGATE that NAMES each of its three upstreams — Persistence,
 *       DataServices and Security — rather than one opaque verdict, and reports healthy only once all
 *       three do. This is the point of the whole file: a test that merely checks Gateway returns 200
 *       does not discharge C-10.
 *    C. The same anonymous `/health` contract holds on all four in-scope services.
 *
 *  WHY THE AGGREGATION IS PROVEN BY DOCUMENT CONTENT AND NEVER BY A RESTART
 *  -----------------------------------------------------------------------
 *  The ordering property — Gateway healthy only after its upstreams — is expressed in the
 *  orchestration manifest as a dependency condition on upstream health, and it is the specific
 *  requirement that ruled out the alternative orchestration publisher, whose generated model could
 *  not express it. The temporal gate is therefore established at bring-up, which has already
 *  happened by the time this suite runs: it CANNOT be observed against an already-running stack.
 *
 *  The tempting way to observe it — stop an upstream, watch Gateway go unhealthy, start it again — is
 *  forbidden, and not as a matter of taste. This suite must never start, stop, restart, scale or
 *  wait for the boot of any service: the single local bring-up path is the compose manifest under
 *  `orchestration/`, and a second, competing path would break the very health-condition chain the
 *  assertion is trying to prove. The sibling `playwright.config.ts` declares no `webServer` block,
 *  no global setup and no global teardown for exactly this reason, and this file honours the same
 *  boundary: it is READ-ONLY against the stack. It writes nothing, resets nothing and reseeds
 *  nothing — which also keeps it compatible with the paired-capture rule, under which a legacy-side
 *  and a target-side recording of one workflow are comparable only when taken against the same
 *  unrecreated `persistence-db` volume state.
 *
 *  So the aggregation is asserted from the CONTENT of the aggregate: each upstream must be named in
 *  the document Gateway publishes, and the aggregate verdict must read healthy. Naming is what makes
 *  the aggregate useful to the operator reading a failure, and it is the observable consequence of
 *  Gateway actually composing three upstream verdicts rather than reporting only on itself.
 *
 *  WHY THE BODY IS READ DEFENSIVELY RATHER THAN DESERIALIZED INTO A FIXED SHAPE
 *  ---------------------------------------------------------------------------
 *  These assertions are deliberately schema-agnostic: the body is read as text, parsed as JSON only
 *  if it parses, and inspected as a serialized document either way. That is a durability decision.
 *  The invariant contract C-10 actually publishes is "the aggregate names each upstream and reports a
 *  verdict"; the exact member spelling around it is the implementation's to choose and to version,
 *  and a deep structural equality here would fail on a contract-conformant document that had merely
 *  added a member. The three invariants below hold across every shape the contract permits.
 *
 *  WHAT THIS FILE DELIBERATELY DOES NOT DO
 *  ---------------------------------------
 *  * NO TIMING ASSERTION OF ANY KIND. No response time is read, no elapsed interval is measured, and
 *    no latency, throughput, availability or service-level figure is asserted — this repository
 *    publishes none, so none may be implied. The runner's per-test and per-assertion timeouts are
 *    hang guards so that a wedged run ends by itself; they are not budgets, and this file adds none
 *    of its own.
 *  * NO SERVICE LIFECYCLE CALL, for the reason given above.
 *  * NO CREDENTIAL. `/health` is anonymous, so this file needs no token, no key, no certificate and
 *    no credential of any kind — and carries none, in source, in a comment or in an assertion
 *    message.
 *  * NO HAND-WRITTEN ADDRESS. Every URL comes from the endpoint table, which is what structurally
 *    prevents an unintended port from ever being reached: the table holds exactly the four in-scope
 *    services and has no entry for the reserved Phase-2 slot, so no such address is even expressible
 *    here.
 *  * NO REACH OUTSIDE THIS DIRECTORY. The two imports resolve to `@playwright/test` and to this
 *    suite's own fixture barrel. The sibling directories under `tests/` hold read-only
 *    behavioural-oracle assets and are neither read, imported, globbed nor referenced.
 *  * NO FUNCTIONAL TRAFFIC OFF GATEWAY. Probing another service's anonymous `/health` directly is the
 *    single permitted exception, and it is permitted only as corroboration of this contract. The
 *    authenticated `/v1/ping` probe is asserted on Gateway alone, by assertion group 2.
 *
 *  LEGACY POSITION
 *  ---------------
 *  There is no legacy wire behaviour to preserve here. C-10 is entirely net-new: the legacy
 *  PowerFramework is an in-process library with no process of its own, no listener, no route table
 *  and no health endpoint, so decomposition created this boundary from nothing. The two legacy
 *  objects cited for this file are scenario references only and are never read at run time —
 *  `ws_objects/pfw.pbl.src/pfw.sra` for the framework lifecycle Gateway's composition root
 *  reproduces (its `open` event initializes, its `close` event finalizes, and its `systemerror`
 *  event terminates on a structural fault rather than degrading), and
 *  `ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw` for the storage scenario behind Persistence.
 *  Both live in the read-only legacy tree and are neither imported nor modified.
 * ==================================================================================================
 */

import { expect, test } from '@playwright/test';

import {
  ALL_SERVICE_KEYS,
  GATEWAY_HEALTH_AGGREGATION_UPSTREAMS,
  HEALTH_PATH,
  SERVICE_ENDPOINTS,
  anonymousHeaders,
  gatewayUrl,
  type ServiceKey,
} from '../fixtures';

import { probeStackAvailability } from '../fixtures/live-stack';

/**
 * THE SINGLE EDIT POINT FOR HEALTHY-STATUS VOCABULARY.
 *
 * Every assertion about a verdict reading "healthy" goes through this one pattern, so that the
 * vocabulary can be narrowed or widened in one place rather than being chased through a file full of
 * scattered string literals.
 *
 * Two properties of the pattern are load bearing and are the reason it is written with word
 * boundaries rather than as a set of substring tests:
 *
 *   * `\bhealthy\b` does NOT match `Unhealthy` — there is no word boundary between the `n` and the
 *     `h` — so a failed aggregate cannot be read as a healthy one. `Degraded` matches nothing here
 *     either, which is correct: not ready is not ready.
 *   * `\bup\b` does NOT match `upstreams`, so the member that carries the aggregate's parts cannot be
 *     mistaken for its verdict.
 *
 * It is deliberately tolerant across spellings rather than pinned to one enumeration value, because
 * what C-10 publishes is the presence of a verdict; the token an implementation chooses for the
 * healthy end of it is not the thing this file exists to pin down.
 */
const HEALTHY_STATUS_PATTERN = /\b(healthy|ok|up|pass)\b/i;

/**
 * The four capability areas that are mapped in discovery but built in no part by this phase.
 *
 * None may appear in a health roster. A deferred service named in the aggregate would mean something
 * had been built that this phase forbids outright — not partially, and not merely stubbed out — so
 * the absence of these four names is asserted rather than assumed. It is a cheap, standing check
 * that the scope boundary is still where it was drawn.
 */
const DEFERRED_SERVICE_NAMES = ['DesignSystem', 'Documents', 'Integration', 'ScriptBridge'] as const;

/**
 * The number of services this phase implements, and therefore the exact size of the endpoint table.
 *
 * Asserted rather than assumed, and it is the structural half of keeping the reserved Phase-2 slot
 * unreachable: this file probes only addresses drawn from the table, and the table has exactly these
 * four entries with no entry for a reserved slot. A fifth entry appearing here would mean a deferred
 * capability area had acquired an address, which is the condition the count exists to catch.
 */
const IN_SCOPE_SERVICE_COUNT = 4;

/**
 * A health response body, read in the two forms the assertions need.
 *
 * Both members are always populated, so a caller never has to branch on whether parsing worked:
 * `serialized` is the canonical text to search, and `parsed` is the structured view when there was
 * one.
 */
interface HealthDocument {
  /**
   * The document in searchable text form — the re-serialized JSON when the body parsed, and the raw
   * body otherwise. Re-serializing rather than searching the raw text normalises insignificant
   * whitespace, so a pretty-printed document and a compact one are searched identically.
   */
  readonly serialized: string;

  /**
   * The parsed value, or `undefined` when the body was not JSON at all. Typed as `unknown` because
   * nothing about the body is known before it is inspected, and asserting a shape onto it here would
   * defeat the point of reading it defensively.
   */
  readonly parsed: unknown;
}

/**
 * Reads a response body into both forms the assertions need, without ever throwing.
 *
 * A parse failure is not an error to be raised here. It is one of the outcomes the assertions must
 * be able to report on, and reporting it as "the document does not name Persistence" — with the body
 * quoted in the failure message — is a far better diagnostic than a `SyntaxError` thrown from inside
 * a fixture with no indication of which service produced it.
 *
 * @param body the response body exactly as it arrived
 * @returns the document in searchable and, where available, structured form
 */
function readHealthDocument(body: string): HealthDocument {
  try {
    const parsed: unknown = JSON.parse(body);

    return { parsed, serialized: JSON.stringify(parsed) };
  } catch {
    return { parsed: undefined, serialized: body };
  }
}

/**
 * The tokens by which one service may legitimately be named in a health document.
 *
 * All three come from the endpoint table rather than from a literal in this file, which is what keeps
 * the naming assertion honest: if the table is ever repointed, this assertion follows it instead of
 * silently continuing to test a name nobody publishes any more. Accepting any of the three is
 * deliberate — the wire form is the implementation's choice, and a document that named
 * `PowerFramework.Persistence` or `Persistence` rather than `persistence` would still have named the
 * upstream, which is the invariant under test.
 *
 * @param key the upstream's stable key
 * @returns the accepted identity tokens for that upstream
 */
function identityTokens(key: ServiceKey): readonly string[] {
  const endpoint = SERVICE_ENDPOINTS[key];

  return [endpoint.key, endpoint.displayName, endpoint.projectName];
}

/**
 * Tests whether a serialized document names a token, ignoring case.
 *
 * A case-insensitive substring test rather than a word-boundary one, and the choice is deliberate in
 * both directions it is used. For an upstream the tokens are distinctive enough that a substring
 * match cannot be satisfied by an unrelated word, and it tolerates a document that embeds the name
 * inside a longer identifier. For a deferred service the substring form is the conservative
 * direction: it is the harder test to pass, so it cannot miss a violation of the scope boundary by
 * being too literal about punctuation.
 *
 * @param document the serialized health document
 * @param token the identity token to look for
 * @returns true when the document names the token
 */
function documentNames(document: string, token: string): boolean {
  return document.toLowerCase().includes(token.toLowerCase());
}

/**
 * Extracts the aggregate verdict — Gateway's own status, never an upstream's.
 *
 * Resolution order, narrowest first, so that a structured document is always read structurally and
 * the whole-body fallback is reached only when there is no verdict member to read:
 *
 *   1. A `status` member on a parsed object. This is the aggregate's own verdict, and reading it
 *      by name is what keeps a healthy upstream from being mistaken for a healthy aggregate — a
 *      document reporting `Unhealthy` overall while one upstream is `Healthy` must fail, and it does,
 *      because only the aggregate member is consulted.
 *   2. A parsed JSON string, for a body that is a bare quoted verdict.
 *   3. The serialized body, for a body that is not JSON at all — a bare plain-text verdict, say.
 *      Widest and last, because it is the only form in which an upstream's verdict could be confused
 *      with the aggregate's, and it is reached only when no better source exists.
 *
 * @param document the health document to read
 * @returns the best available statement of the aggregate verdict
 */
function readAggregateStatus(document: HealthDocument): string {
  const parsed: unknown = document.parsed;

  if (typeof parsed === 'object' && parsed !== null) {
    const status: unknown = (parsed as Record<string, unknown>)['status'];

    if (typeof status === 'string') {
      return status;
    }
  }

  if (typeof parsed === 'string') {
    return parsed;
  }

  return document.serialized;
}

/**
 * One `describe`, and deliberately not a serial one.
 *
 * These three assertions are independent single-request reads that mutate nothing, so no ordering
 * relationship exists between them and declaring one would be a false statement about the file.
 * Serialized execution belongs to the two state-mutating workflows, which share `COMPANY` rows in a
 * single volume and therefore genuinely do depend on order.
 */
test.describe('Health and readiness (contract C-10)', () => {
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

  test('Gateway answers /health anonymously with 200', async ({ request }) => {
    const gateway = SERVICE_ENDPOINTS.gateway;

    // No credential is sent, and that is the condition under test rather than an omission:
    // `anonymousHeaders()` states it at the call site where a bare `{}` would read as something
    // somebody forgot to fill in. `/health` is the ONE documented anonymous path in a system where
    // every other boundary is authenticated, because the thing that probes it holds no token and
    // probes precisely while the service is still starting. Requiring one would make readiness depend
    // on token issuance already being live — a circular dependency that cannot resolve on a cold
    // start.
    //
    // `failOnStatusCode` is pinned off so that an unexpected status reaches the assertion below and is
    // reported as "expected 200, received N" rather than thrown from inside the request call, where
    // the failure would name no service.
    const response = await request.get(gatewayUrl(HEALTH_PATH), {
      headers: anonymousHeaders(),
      failOnStatusCode: false,
    });

    // Exactly 200, because the contract makes 200 the only code for a healthy verdict: not-ready and
    // failed are both answered 503, and the difference between those two is carried in the body. An
    // orchestrator observes the CODE, so anything other than 200 here means this stack is not one a
    // dependent should have been allowed to start against.
    expect(
      response.status(),
      `${gateway.displayName} (${gateway.projectName}) must answer its anonymous ${HEALTH_PATH} ` +
        'with 200. A 401 would mean the one documented anonymous path had been closed; a 503 would ' +
        'mean the stack reported itself not ready, in which case the aggregate body names which ' +
        'participant is responsible.',
    ).toBe(200);

    // Asserted alongside the code rather than instead of it. The two are not redundant: the status
    // pins the exact code the readiness gate observes, while `ok()` is the property every later
    // response read depends on, and stating both keeps a future change to either honest.
    expect(
      response.ok(),
      `${gateway.displayName} answered a non-successful status on its anonymous ${HEALTH_PATH}.`,
    ).toBe(true);

    // No response time is read here and none is asserted. See the file header: this repository
    // publishes no latency, throughput or availability figure, so none may be implied.
  });

  test('Gateway /health aggregates and names each of its three upstreams', async ({ request }) => {
    const gateway = SERVICE_ENDPOINTS.gateway;

    const response = await request.get(gatewayUrl(HEALTH_PATH), {
      headers: anonymousHeaders(),
      failOnStatusCode: false,
    });

    // A precondition, not a repeat of the previous test. The document below is only meaningful when
    // Gateway answered successfully: a not-ready response carries a problem document instead of an
    // aggregate, and searching that for upstream names would report a confusing absence rather than
    // the real finding, which is that the stack is not ready.
    expect(
      response.ok(),
      `${gateway.displayName} did not answer successfully, so it published no aggregate to inspect. ` +
        'The readiness assertion in the preceding test is the one to read first.',
    ).toBe(true);

    const document = readHealthDocument(await response.text());

    // THE PRIMARY ASSERTION OF THIS FILE.
    //
    // Gateway reports healthy only after Persistence, DataServices and Security do, and it names each
    // of them with its own state rather than collapsing three verdicts into one — because an operator
    // reading a failed aggregate needs to know WHICH upstream is responsible. That naming is the
    // observable consequence of the aggregation, and it is what a bare "Gateway returned 200" check
    // cannot establish.
    //
    // Gateway is deliberately not in this roster: it aggregates its upstreams, and asserting that it
    // names itself would make the readiness property circular.
    for (const key of GATEWAY_HEALTH_AGGREGATION_UPSTREAMS) {
      const upstream = SERVICE_ENDPOINTS[key];
      const named: boolean = identityTokens(key).some((token: string) =>
        documentNames(document.serialized, token),
      );

      expect(
        named,
        `${gateway.displayName} published a health document that never names its upstream ` +
          `${upstream.displayName} (${upstream.projectName}). Contract C-10 requires the aggregate ` +
          'to name each upstream with its own state, so an unnamed upstream means Gateway is not ' +
          `aggregating it. The document was: ${document.serialized}`,
      ).toBe(true);
    }

    // The aggregate's own verdict, read through the single status pattern declared at the top of this
    // file. Only Gateway's verdict is consulted — a healthy upstream inside an unhealthy aggregate
    // must still fail, and it does.
    //
    // The converse direction — a healthy aggregate published over an upstream that is NOT ready — is
    // deliberately not inferred from this document at all. Gateway's own report is the wrong evidence
    // for it, because the report is exactly what would be wrong. The following test establishes it at
    // first hand instead, by asking each upstream directly.
    const aggregateStatus: string = readAggregateStatus(document);

    expect(
      aggregateStatus,
      `${gateway.displayName} did not report a healthy aggregate verdict. Gateway reports healthy ` +
        'only once all three upstreams do, so a verdict that is anything else is a statement about ' +
        `an upstream and not about Gateway alone. The document was: ${document.serialized}`,
    ).toMatch(HEALTHY_STATUS_PATTERN);

    // The scope boundary, asserted rather than assumed. The four deferred capability areas receive no
    // project, no container, no test and no partial implementation in this phase; they exist only as
    // reserved routing metadata on Gateway. One of them appearing in a health roster would mean
    // something had been built that must not be, and it would mean it silently.
    for (const deferred of DEFERRED_SERVICE_NAMES) {
      expect(
        documentNames(document.serialized, deferred),
        `${gateway.displayName} named the deferred capability area ${deferred} in its health ` +
          'document. Nothing is built for it in this phase, so it has no health to report: a name ' +
          'here means a reserved routing declaration has grown an implementation behind it. The ' +
          `document was: ${document.serialized}`,
      ).toBe(false);
    }
  });

  test('/health answers 200 anonymously on every in-scope service', async ({ request }) => {
    // Probing a non-Gateway service directly is the SINGLE exception to routing all functional
    // traffic through Gateway, and it is permitted only as corroboration of this contract: `/health`
    // is anonymous on all four services, and Gateway's aggregate is only trustworthy if the
    // upstreams it names answer for themselves. It licenses nothing else — no other path on any of
    // these three addresses is touched by this file, and the authenticated probe is asserted on
    // Gateway alone, by assertion group 2.
    expect(
      ALL_SERVICE_KEYS,
      'The endpoint table must hold exactly the services this phase implements. A different count ' +
        'means either a service is missing an address or a deferred capability area has acquired ' +
        'one — and this file probes every address in the table, so the count is what keeps the ' +
        'reserved Phase-2 slot unreachable.',
    ).toHaveLength(IN_SCOPE_SERVICE_COUNT);

    for (const key of ALL_SERVICE_KEYS) {
      const endpoint = SERVICE_ENDPOINTS[key];

      // Every probed address is drawn from the endpoint table and is the documented anonymous health
      // path on that entry's own base address — never a hand-assembled one. Stating it as an
      // assertion rather than trusting it makes the containment verifiable: an address this file
      // could not have composed from the table is an address it must not reach.
      expect(
        endpoint.healthUrl,
        `${endpoint.displayName} must be probed at its table address plus the shared ${HEALTH_PATH} ` +
          'path, so that no address is ever assembled by hand in a spec.',
      ).toBe(`${endpoint.baseUrl}${HEALTH_PATH}`);

      const response = await request.get(endpoint.healthUrl, {
        headers: anonymousHeaders(),
        failOnStatusCode: false,
      });

      // Probed in the table's own order, which runs from the deepest dependency outwards to the
      // ingress. That ordering is why the first failure reported is the real cause rather than
      // Gateway's downstream symptom of it.
      expect(
        response.status(),
        `${endpoint.displayName} (${endpoint.projectName}) must answer ${HEALTH_PATH} with 200 to a ` +
          'request carrying no credential. A 401 would mean the anonymous readiness contract had ' +
          'been closed on this service; a 503 would mean the service reported itself not ready, ' +
          'which also invalidates the Gateway aggregate that names it.',
      ).toBe(200);
    }
  });
});
