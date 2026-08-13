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
 *  🔴 WHY THE BODY IS NOW READ AGAINST THE PUBLISHED SHAPE, NOT DEFENSIVELY
 *  -----------------------------------------------------------------------
 *  This file used to state the opposite, and the reasoning was wrong on the facts. It read the body as
 *  TEXT, parsed it as JSON only if it happened to parse, and inspected the serialized form either way -
 *  on the argument that "the invariant contract C-10 actually publishes is 'the aggregate names each
 *  upstream and reports a verdict'; the exact member spelling around it is the implementation's to
 *  choose and to version".
 *
 *  `gateway.v1.yaml` says otherwise. `AggregateHealthReport` marks `status`, `service` and `upstreams`
 *  REQUIRED, sets `additionalProperties: false`, fixes `service` as a `const`, pins `upstreams` at
 *  exactly three items, and declares both status enumerations as closed sets. The member spelling is
 *  not the implementation's to choose - it is what every client generated from the document reads.
 *
 *  What the tolerance bought was a suite that passed against documents no client can consume: a
 *  plain-text `ok`, a bare quoted `"up"`, an upstream's own verdict mistaken for the aggregate's
 *  because the last fallback searched the WHOLE body, and an upstream counted as "named" because the
 *  word appeared in an unrelated member. Every one of those is now a failure.
 *
 *  ADDING A MEMBER IS STILL NOT A FAILURE, which was the legitimate half of the original concern: the
 *  required members are asserted as present rather than as the complete key set, so a
 *  contract-conformant document that grew `checkedAt` or `checks` still passes. The tolerance removed
 *  is tolerance of a DIFFERENT shape, not of a richer one.
 *
 *  One assertion stays a text search and stays broad on purpose - the deferred-service guard. A
 *  NEGATIVE assertion may be broader than the contract; a positive one may not.
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

import { expect, test, type APIResponse } from '@playwright/test';

import {
  ALL_SERVICE_KEYS,
  GATEWAY_HEALTH_AGGREGATION_UPSTREAMS,
  HEALTH_PATH,
  SERVICE_ENDPOINTS,
  anonymousHeaders,
  gatewayUrl,
} from '../fixtures';

import { requireLiveStack } from '../fixtures/live-stack';

import {
  AGGREGATE_HEALTH_REPORT,
  ALL_MEMBER_CONTRACTS,
  CAPABILITY_ALL_MASK,
  CAPABILITY_DESTINATION_TOKENS,
  CAPABILITY_NAME_TOKENS,
  CAPABILITY_VALUE_TOKENS,
  DEFERRED_SERVICE_TOKENS,
  GATEWAY_CONTRACT_PATH,
  GATEWAY_SERVICE_TOKEN,
  HEALTHY_STATUS_TOKEN,
  HEALTH_CHECK_RESULT,
  HEALTH_STATUS_TOKENS,
  JSON_MEDIA_TYPE,
  PING_AUTHENTICATED,
  RESERVED_ROUTE_MARKER,
  RESERVED_ROUTE_RET_CODE,
  RESERVED_ROUTE_STATUS,
  UPSTREAM_HEALTH,
  UPSTREAM_SERVICE_TOKENS,
  UPSTREAM_STATUS_TOKENS,
  assertContractDeclaresShape,
  assertContractDeclaresTokens,
  assertContractTokensExhaustive,
  assertEnumToken,
  assertMediaType,
  assertMembers,
  describeShapeForFailure,
  readRepositoryText,
} from '../fixtures/contract-shape';

/**
 * THE VERDICT IS ONE EXACT TOKEN, AND IT USED TO BE A VOCABULARY.
 *
 * This file once matched the aggregate verdict against `/\b(healthy|ok|up|pass)\b/i`, described as
 * "deliberately tolerant across spellings ... the token an implementation chooses for the healthy end
 * of it is not the thing this file exists to pin down." That reasoning was wrong about the contract.
 * `AggregateHealthReport.status` is a CLOSED ENUMERATION of exactly three case-sensitive tokens
 * — `Healthy`, `Degraded`, `Unhealthy` — and `Healthy` is the one that means ready. The pattern
 * accepted three tokens the contract does not declare (`ok`, `up`, `pass`) in any casing, so a
 * projection that changed its verdict vocabulary passed the readiness assertion, and an operator's
 * gate keyed on `Healthy` would have broken while this suite stayed green.
 *
 * The two properties the pattern was written for are preserved and strengthened rather than lost:
 * `Unhealthy` cannot be read as healthy because the comparison is now equality against `Healthy`, and
 * the `upstreams` member cannot be mistaken for the verdict because the verdict is read from the
 * `status` member by name and from nowhere else.
 *
 * {@link HEALTHY_STATUS_TOKEN} and {@link HEALTH_STATUS_TOKENS} live in `fixtures/contract-shape.ts`
 * beside every other member set and token set this suite asserts, and the guard test at the end of
 * this file re-derives them from the published contract so the two cannot drift.
 */

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
 * THE DEFENSIVE READER IS GONE, AND ITS JOB SURVIVES AS A DIAGNOSTIC.
 *
 * A `HealthDocument` pair — the parsed value and a re-serialized searchable string — used to be the
 * subject of every assertion in this file, with a header explaining that "these assertions are
 * deliberately schema-agnostic ... a deep structural equality here would fail on a
 * contract-conformant document that had merely added a member." The second half of that is true and is
 * why `assertMembers` distinguishes REQUIRED from OPTIONAL members rather than demanding equality; the
 * first half was the defect. `AggregateHealthReport` sets `additionalProperties: false`, so a member
 * the schema does not declare is not an addition a conformant document may make — it is drift.
 *
 * What the pair genuinely bought was a good failure message for a body that did not parse, and that is
 * preserved: `describeShapeForFailure` reports a body's structure — parsed or not, object or array,
 * member names, length — WITHOUT quoting any value, and it is used only inside failure messages after
 * an exact assertion has already decided the outcome.
 */

/**
 * Read a health body as the EXACT aggregate the contract publishes.
 *
 * THIS REPLACED THREE TOLERANT READERS, and each is worth naming because each was the acceptance
 * criterion for a claim it could not actually establish:
 *
 *   * `identityTokens(key)` accepted an upstream "named" under any of THREE spellings — the table
 *     key, the display name or the project name — on the reasoning that "the wire form is the
 *     implementation's choice". It is not: `UpstreamHealth.service` is a closed enumeration of
 *     `persistence`, `dataservices` and `security`, so a document naming `PowerFramework.Persistence`
 *     is a document a consumer branching on the token cannot read.
 *   * `documentNames(serialized, token)` looked for a case-insensitive SUBSTRING ANYWHERE IN THE WHOLE
 *     SERIALIZED BODY. A document that mentioned `persistence` inside a description, a URL or an error
 *     string satisfied "Gateway aggregates Persistence" without carrying an upstream entry at all.
 *   * `readAggregateStatus(document)` fell back from the `status` member to a bare JSON string and
 *     finally to THE ENTIRE SERIALIZED BODY, which — combined with the healthy-vocabulary pattern —
 *     meant a body containing the word "healthy" anywhere passed the readiness assertion.
 *
 * The exact read asserts the media type, then the aggregate's member set against
 * `AggregateHealthReport`, then `status` against its closed enumeration, then every entry of
 * `upstreams` against `UpstreamHealth` including both of its enumerations. Drift in any of those is
 * now a failure rather than a tolerated spelling.
 *
 * @param response the health response, already known to have answered
 * @returns the aggregate, narrowed, together with the raw text for diagnostics
 */
async function readAggregate(
  response: APIResponse,
): Promise<{ readonly aggregate: Record<string, unknown>; readonly bodyText: string }> {
  const bodyText: string = await response.text();

  assertMediaType(
    response.headers()['content-type'],
    JSON_MEDIA_TYPE,
    `${SERVICE_ENDPOINTS.gateway.displayName} ${HEALTH_PATH}`,
  );

  let parsed: unknown;

  try {
    parsed = JSON.parse(bodyText) as unknown;
  } catch {
    throw new Error(
      `${SERVICE_ENDPOINTS.gateway.displayName} answered ${HEALTH_PATH} with a body that is not ` +
        'parseable JSON, so it published no aggregate to inspect. Contract C-10 declares an ' +
        `AggregateHealthReport object. ${describeShapeForFailure(bodyText)}.`,
    );
  }

  const aggregate: Record<string, unknown> = assertMembers(
    parsed,
    AGGREGATE_HEALTH_REPORT,
    `Gateway's ${HEALTH_PATH} aggregate`,
  );

  return { aggregate, bodyText };
}

/**
 * The `upstreams` array, asserted to be exactly the three the contract declares.
 *
 * `minItems: 3` and `maxItems: 3` on the published schema are what make the count assertable: the
 * aggregate names three upstreams, never two and never four, so a Gateway that had stopped observing
 * one of them fails here instead of passing on the two it still watched.
 *
 * @param aggregate the narrowed aggregate
 * @param bodyText the raw body, for the structural diagnostic only
 * @returns each upstream entry keyed by its exact `service` token
 */
function readUpstreams(
  aggregate: Record<string, unknown>,
  bodyText: string,
): ReadonlyMap<string, Record<string, unknown>> {
  const upstreams: unknown = aggregate['upstreams'];

  if (!Array.isArray(upstreams)) {
    throw new Error(
      `Gateway's ${HEALTH_PATH} aggregate carries a non-array 'upstreams' member, so it names no ` +
        `upstream individually. ${describeShapeForFailure(bodyText)}.`,
    );
  }

  const byService = new Map<string, Record<string, unknown>>();

  for (const entry of upstreams) {
    const upstream: Record<string, unknown> = assertMembers(
      entry,
      UPSTREAM_HEALTH,
      `an entry of Gateway's ${HEALTH_PATH} 'upstreams'`,
    );

    const service: string = assertEnumToken(
      upstream['service'],
      UPSTREAM_SERVICE_TOKENS,
      "an upstream entry's 'service'",
    );

    if (byService.has(service)) {
      throw new Error(
        `Gateway's ${HEALTH_PATH} aggregate names the upstream '${service}' more than once, so one ` +
          'of the three it must report on is missing and a duplicate stands in its place.',
      );
    }

    byService.set(service, upstream);
  }

  return byService;
}

/**
 * One `describe`, and deliberately not a serial one.
 *
 * These three assertions are independent single-request reads that mutate nothing, so no ordering
 * relationship exists between them and declaring one would be a false statement about the file.
 *
 * NO SPEC IN THIS SUITE DECLARES `mode: 'serial'` ANY LONGER. The two state-mutating workflows once
 * did, to order tests that shared module-scope state; the concurrency workflow is now one atomic test
 * with a step per former step, and the DataWindow workflow's tests each arrange their own row, so
 * neither has an order left to declare. Serialization is still real, but it comes from where it always
 * belonged — `fullyParallel: false` and `workers: 1` in the runner configuration, which is a
 * repository-wide correctness decision about a shared `persistence-db` volume rather than a per-file
 * one.
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

    const { aggregate, bodyText } = await readAggregate(response);

    // THE BODY IS ASSERTED TO BE THE PUBLISHED MESSAGE BEFORE ANYTHING IS READ OUT OF IT, and that
    // happens inside `readAggregate` above rather than here: it asserts the media type, refuses a body
    // that is not parseable JSON with a structural diagnostic, and narrows the value through
    // `assertMembers(AGGREGATE_HEALTH_REPORT)` — which requires `status`, `service` and `upstreams`
    // spelled exactly as the schema declares them and rejects any member the schema does not declare,
    // because `additionalProperties: false` makes those spellings the contract. A document carrying
    // `state` or `Status` is a different message that no generated client can read, not a variant of
    // this one, and it fails before a single assertion below runs. Restating those checks here would be
    // a second copy of the contract to keep true; `aggregate` is already narrowed.

    // THE PRIMARY ASSERTION OF THIS FILE.
    //
    // Gateway reports healthy only after Persistence, DataServices and Security do, and it names each
    // of them with its own state rather than collapsing three verdicts into one — because an operator
    // reading a failed aggregate needs to know WHICH upstream is responsible. That naming is the
    // observable consequence of the aggregation, and it is what a bare "Gateway returned 200" check
    // cannot establish.
    //
    // ASSERTED STRUCTURALLY NOW, NOT BY SUBSTRING. Each upstream must appear as an ENTRY of the
    // `upstreams` array carrying its exact enumerated `service` token and its own `status` from the
    // upstream verdict set. The previous form searched the whole serialized body for any of three
    // spellings of the name, which a description, a URL or an error string could satisfy without the
    // aggregate carrying an upstream entry at all.
    //
    // Gateway is deliberately not in this roster: it aggregates its upstreams, and asserting that it
    // names itself would make the readiness property circular. Its own identity is asserted separately
    // below, as the `service` member.
    const upstreams: ReadonlyMap<string, Record<string, unknown>> = readUpstreams(
      aggregate,
      bodyText,
    );

    expect(
      [...upstreams.keys()].sort(),
      `${gateway.displayName} must name exactly the three upstreams contract C-10 declares, each as ` +
        'its own entry. The published schema pins the array at three entries, so a Gateway that had ' +
        'stopped observing one of them must fail here rather than pass on the two it still watched. ' +
        `${describeShapeForFailure(bodyText)}.`,
    ).toEqual([...UPSTREAM_SERVICE_TOKENS].sort());

    for (const key of GATEWAY_HEALTH_AGGREGATION_UPSTREAMS) {
      const upstream = SERVICE_ENDPOINTS[key];
      const entry: Record<string, unknown> | undefined = upstreams.get(upstream.key);

      expect(
        entry,
        `${gateway.displayName} published an aggregate with no entry for its upstream ` +
          `${upstream.displayName} (${upstream.projectName}), which the contract identifies by the ` +
          `exact token '${upstream.key}'. An unnamed upstream means Gateway is not aggregating it.`,
      ).toBeDefined();

      // Its own state, from the upstream verdict set — which adds `Unreachable` to the aggregate's
      // three, because "Gateway could not reach it" is a distinct finding from "it reported failed"
      // and an operator needs the two apart.
      const upstreamStatus: string = assertEnumToken(
        entry?.['status'],
        UPSTREAM_STATUS_TOKENS,
        `the '${upstream.key}' upstream's 'status'`,
      );

      expect(
        upstreamStatus,
        `${gateway.displayName} reports its upstream ${upstream.displayName} as ` +
          `'${upstreamStatus}'. Gateway answers 200 only once all three upstreams are ` +
          `'${HEALTHY_STATUS_TOKEN}', so a 200 carrying any other upstream verdict is an aggregate ` +
          'that disagrees with its own parts.',
      ).toBe(HEALTHY_STATUS_TOKEN);
    }

    // The reporting service's own identity. `const: gateway` on the published schema, so there is
    // exactly one conforming value: this is Gateway's aggregate and not an upstream's own report
    // forwarded verbatim, which is a confusion a document without this member could not rule out.
    expect(
      aggregate['service'],
      `the aggregate must identify its reporter as '${GATEWAY_SERVICE_TOKEN}', which the published ` +
        'schema pins as a single constant. Any other value means the body being read is not the ' +
        'composition root\u2019s own report.',
    ).toBe(GATEWAY_SERVICE_TOKEN);

    // The aggregate's own verdict, read from the `status` member BY NAME and from nowhere else — no
    // fallback to a bare string and none to the whole body. Only Gateway's verdict is consulted, so a
    // healthy upstream inside an unhealthy aggregate must still fail, and it does.
    //
    // Compared against ONE EXACT TOKEN. The former pattern accepted `ok`, `up` and `pass` in any
    // casing, none of which the contract declares.
    //
    // The converse direction — a healthy aggregate published over an upstream that is NOT ready — is
    // deliberately not inferred from this document at all. Gateway's own report is the wrong evidence
    // for it, because the report is exactly what would be wrong. The following test establishes it at
    // first hand instead, by asking each upstream directly.
    const aggregateStatus: string = assertEnumToken(
      aggregate['status'],
      HEALTH_STATUS_TOKENS,
      `Gateway's ${HEALTH_PATH} aggregate 'status'`,
    );

    expect(
      aggregateStatus,
      `${gateway.displayName} did not report '${HEALTHY_STATUS_TOKEN}'. Gateway reports healthy only ` +
        'once all three upstreams do, so a verdict that is anything else is a statement about an ' +
        `upstream and not about Gateway alone. ${describeShapeForFailure(bodyText)}.`,
    ).toBe(HEALTHY_STATUS_TOKEN);

    // Every individual check, when the aggregate carries the optional `checks` member. Optional on the
    // schema and therefore optional here: requiring it would invent a requirement (C-B). Present or
    // absent, each entry that IS there must conform.
    const checks: unknown = aggregate['checks'];

    if (checks !== undefined) {
      expect(
        Array.isArray(checks),
        "the aggregate's optional 'checks' member is declared an array, so a non-array value is a " +
          'shape change rather than an omission.',
      ).toBe(true);

      for (const check of Array.isArray(checks) ? checks : []) {
        const result: Record<string, unknown> = assertMembers(
          check,
          HEALTH_CHECK_RESULT,
          "an entry of the aggregate's 'checks'",
        );

        assertEnumToken(
          result['status'],
          HEALTH_STATUS_TOKENS,
          "a health check result's 'status'",
        );
      }
    }

    // The scope boundary, asserted rather than assumed. The four deferred capability areas receive no
    // project, no container, no test and no partial implementation in this phase; they exist only as
    // reserved routing metadata on Gateway. One of them appearing in a health roster would mean
    // something had been built that must not be, and it would mean it silently.
    //
    // KEPT AS A WHOLE-BODY SUBSTRING SCAN, and deliberately so. Here the substring form is the
    // CONSERVATIVE direction: it is the harder test to pass, so it cannot miss a scope violation by
    // being too literal about punctuation or about which member the name appeared in. The exact
    // member-set and enumeration assertions above already establish the positive claim; this is an
    // additional prohibition on top of them, not a substitute for one.
    const serialized: string = bodyText.toLowerCase();

    for (const deferred of DEFERRED_SERVICE_NAMES) {
      expect(
        serialized.includes(deferred.toLowerCase()),
        `${gateway.displayName} named the deferred capability area ${deferred} anywhere in its ` +
          'health document. Nothing is built for it in this phase, so it has no health to report: a ' +
          'name here means a reserved routing declaration has grown an implementation behind it. ' +
          `${describeShapeForFailure(bodyText)}.`,
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

  test(
    'every expected member set and enum token is re-derived from the published contract (no stack)',
    { tag: '@no-stack' },
    () => {
      // ===================================================================
      //  THE ANTI-DRIFT GUARD FOR EVERY EXACT ASSERTION IN THIS SUITE
      //
      //  The exact assertions this suite now makes are only as good as the
      //  member sets and enum tokens they compare against, and those live in
      //  `fixtures/contract-shape.ts` as constants. A constant is a SECOND
      //  DESCRIPTION of the contract, and a second description drifts: rename
      //  a member in `gateway.v1.yaml`, and without this test the suite would
      //  keep asserting the old name and keep failing for a reason nobody
      //  could act on — or worse, keep passing against a service that had
      //  followed the contract while the test had not.
      //
      //  So the constants are RE-DERIVED here from the published document. The
      //  check runs in both directions for members, which is what makes it a
      //  guard rather than a spot check:
      //
      //    * every member the fixture names must still be declared by the
      //      schema, and every member the fixture REQUIRES must still be
      //      `required` there — requiring more than the contract does would
      //      invent a requirement (C-B);
      //    * every member the SCHEMA declares must be named by the fixture, so
      //      a member added to the contract cannot go unasserted; and
      //    * every token the fixture names must still be declared inside the
      //      schema it was taken from.
      //
      //  IT NEEDS NO STACK, which is why it carries `@no-stack`: the contract
      //  is a file in this repository. It is therefore the one assertion in
      //  this file that runs before any bring-up, and it is deliberately in
      //  the readiness spec rather than in a spec of its own — the suite's
      //  inventory is a closed six-file set for a reviewed reason, and a
      //  seventh file to hold one guard would trade that for nothing.
      //
      //  READ-ONLY AGAINST THE REPOSITORY, and nowhere near the legacy tree:
      //  the single path read is the published OpenAPI document (C-C).
      // ===================================================================
      const document: string = readRepositoryText(GATEWAY_CONTRACT_PATH);

      // Member sets, every one this suite asserts anywhere — not merely the
      // ones this file uses. A guard that covered only its own file would
      // leave the capability, reserved-route, retrieval and conflict shapes
      // unguarded, and those are asserted by four other specs that have no
      // reason to each re-read the contract.
      for (const memberContract of ALL_MEMBER_CONTRACTS) {
        expect(
          () => assertContractDeclaresShape(document, memberContract),
          `the member set fixtures/contract-shape.ts declares for schema ` +
            `${memberContract.schema} must still agree with ` +
            `${GATEWAY_CONTRACT_PATH}. The thrown message states which member ` +
            'moved and in which direction.',
        ).not.toThrow();
      }

      // Token sets, each verified inside the schema it was taken from. The
      // schema name is part of the check: a token that had moved to another
      // schema would satisfy a document-wide search and would still be wrong.
      const tokenSets: readonly (readonly [string, readonly string[]])[] = [
        ['AggregateHealthReport', HEALTH_STATUS_TOKENS],
        ['AggregateHealthReport', [GATEWAY_SERVICE_TOKEN]],
        ['UpstreamHealth', UPSTREAM_STATUS_TOKENS],
        ['UpstreamHealth', UPSTREAM_SERVICE_TOKENS],
        ['HealthCheckResult', HEALTH_STATUS_TOKENS],
        ['PingResponse', [GATEWAY_SERVICE_TOKEN, String(PING_AUTHENTICATED)]],
        ['ReservedRouteBody', DEFERRED_SERVICE_TOKENS],
        ['ReservedRouteBody', [RESERVED_ROUTE_MARKER, String(RESERVED_ROUTE_STATUS), String(RESERVED_ROUTE_RET_CODE)]],
        ['CapabilityReport', [String(CAPABILITY_ALL_MASK)]],
        ['Capability', CAPABILITY_DESTINATION_TOKENS],
        ['Capability', CAPABILITY_NAME_TOKENS],
        ['Capability', CAPABILITY_VALUE_TOKENS.map((value: number) => String(value))],
      ];

      for (const [schema, tokens] of tokenSets) {
        expect(
          () => assertContractDeclaresTokens(document, schema, tokens),
          `the tokens fixtures/contract-shape.ts declares for schema ${schema} ` +
            `must still be declared inside that schema in ${GATEWAY_CONTRACT_PATH}.`,
        ).not.toThrow();
      }

      // AND THE OPPOSITE DIRECTION, which is the one a probe proved was missing.
      // Adding `Starting` to `UpstreamHealth.status` in the contract left the
      // check above perfectly happy: every token it expected was still declared.
      // But `assertEnumToken` compares against a CLOSED set, so a conformant
      // response carrying the new token would have been rejected as
      // non-conforming — a false accusation traceable to a stale fixture this
      // guard had just pronounced healthy.
      //
      // Judged per SCHEMA against the union of every set drawn from it, because a
      // schema may contribute several sets and the extractor attributes tokens to
      // the schema rather than to the property. The unions are assembled from the
      // same `tokenSets` table above, so a set added there is covered here without
      // a second edit.
      const unionBySchema = new Map<string, string[]>();

      for (const [schema, tokens] of tokenSets) {
        const union: string[] = unionBySchema.get(schema) ?? [];

        union.push(...tokens);
        unionBySchema.set(schema, union);
      }

      for (const [schema, knownTokens] of unionBySchema) {
        expect(
          () => assertContractTokensExhaustive(document, schema, knownTokens),
          `schema ${schema} must declare no closed-enumeration token that ` +
            'fixtures/contract-shape.ts does not name, or this suite would reject ' +
            'a conforming response as non-conforming.',
        ).not.toThrow();
      }

      // The healthy token must be a member of the closed set it is drawn from.
      // Trivially true today and worth stating: were the enumeration ever
      // renarrowed, a `HEALTHY_STATUS_TOKEN` left behind would compare against
      // a value no conforming service could send, and every readiness assertion
      // in this suite would fail with no indication that the fixture was the
      // thing at fault.
      expect(
        HEALTH_STATUS_TOKENS,
        `the token this suite treats as "ready" (${HEALTHY_STATUS_TOKEN}) must be ` +
          'one of the verdicts the contract declares.',
      ).toContain(HEALTHY_STATUS_TOKEN);
    },
  );
});
