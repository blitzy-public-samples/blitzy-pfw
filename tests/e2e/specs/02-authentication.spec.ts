/**
 * Authentication — assertion group 2 of 6. Constraint C-G, contracts C-01 and C-10.
 *
 * WHY THIS SPEC IS THE ONE THAT CANNOT BE SKIPPED
 * ----------------------------------------------
 * The framework being decomposed opens no listening socket, registers no route
 * and receives no unsolicited request of any kind. Decomposition therefore
 * creates the system's first-ever ingress, and the requirement that it add "no
 * new attack surface" cannot be read literally — there was no surface before, so
 * there is nothing to hold constant. The only reading that means anything is the
 * one this file makes executable: **every surface that now exists is
 * authenticated from the outset.**
 *
 * `/v1/ping` exists for precisely this purpose. It computes nothing worth
 * computing; its entire value is that "is this boundary actually guarded" becomes
 * a question with a cheap, unambiguous answer — `401` without a credential, `200`
 * with one. Both halves are asserted here, because each is a different failure
 * and only one of them is visible from the other: a suite that could make only
 * authenticated requests would prove that the boundary *accepts* a good token
 * while never proving that it *rejects* the absence of one, and the second is the
 * half that shows the boundary is closed.
 *
 * THE TOKEN PATTERN THIS FILE ESTABLISHES
 * --------------------------------------
 * The four remaining specs follow the shape used below, so it is kept
 * deliberately small and obvious:
 *
 *   const token = await requireServiceToken(request);   // once, per test
 *   ... { headers: bearerHeaders(token) }               // per request
 *
 * Acquire inside the test that needs it, attach per request, let it fall out of
 * scope. **Never** cached in module scope, never promoted to a shared context and
 * never placed in a config-level header: `playwright.config.ts` sets no global
 * `Authorization` header, and that omission is load-bearing rather than an
 * oversight. It is what keeps the anonymous assertion below writable at all, so
 * nothing here compensates for it with `storageState` or `extraHTTPHeaders`.
 *
 * SOLE ISSUER, THREE VERIFIERS — AND THIS SUITE IS NEITHER
 * -------------------------------------------------------
 * Security is the **only** minter in the system; Gateway, DataServices and
 * Persistence hold verification material only and validate with a stock bearer
 * handler against the key set Security publishes. So the positive case here is a
 * genuine end-to-end proof rather than a self-signed shortcut: the token is
 * requested from the issuer and presented to Gateway, which validates it against
 * the issuer's published keys. If that round trip works, the topology works.
 *
 * **Every token in this suite is minted at run time by Security** (C-F). No token
 * is embedded, none is signed locally, no signing key is read or named, and no
 * credential-shaped literal appears anywhere below. A test suite able to mint its
 * own tokens would be a second issuer, and a sole-issuer topology is worth
 * something only while there is exactly one. The legacy anti-pattern this
 * replaces — a browser asset in the read-only oracle tree that embeds key
 * material and signs a token with it — is located, value-free, in
 * `docs/SECRETS.md`; that register is the only place a locator belongs, so
 * nothing here restates one, reads it, or reproduces any fragment of it. The
 * legacy behavioural anchor for the capability Security now owns is the
 * `RSASign` / `VerifyRSASign` pair at `ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L70-L73`,
 * cited as provenance and nothing more.
 *
 * **No value read from Security is ever rendered.** Not a token, not an
 * `Authorization` header, not a key set body, not a URL returned by discovery —
 * not into an assertion message, a console line, a reporter annotation or an
 * attachment. Every failure message below names *what* was wrong without quoting
 * the value that was wrong, because a message reaches the console and the CI log
 * and both are retained.
 *
 * GATEWAY ONLY, WITH TWO NAMED EXCEPTIONS
 * --------------------------------------
 * `/v1/ping` requires a token on all four services, and it is asserted **here on
 * Gateway alone**. Functional traffic in this suite enters through the sole
 * ingress, because a suite that reaches around the ingress stops exercising it.
 * The only permitted off-Gateway calls are Security's issuance operation and its
 * two anonymous publication endpoints (contract C-01), both of which appear
 * below; the anonymous `/health` probes of contract C-10 are the other exception
 * and belong to the readiness spec. There is deliberately **no loop over the four
 * services** here and no `/v1/ping` call against DataServices or Persistence.
 *
 * WHAT IS DELIBERATELY NOT ASSERTED (C-B)
 * --------------------------------------
 * Only behaviours the published contracts state are asserted, and `401` is
 * asserted as exactly `401` — never as "4xx" and never as "not 200", both of
 * which would pass on a `403` or a `500` that means something entirely
 * different. Not asserted, because nothing declares them: any
 * `WWW-Authenticate` challenge format; any body, schema or problem document for
 * the `401`; a token lifetime, claim set, segment count or length; the issuer
 * identifier or the advertised algorithm list; and any key parameter value.
 * No latency, throughput, availability or service-level figure is asserted
 * either — the repository publishes none, so any number here would be
 * fabricated. The runner's timeouts are hang guards, not budgets, and nothing
 * about test coverage is asserted: that gate is a .NET concern measured from a
 * Cobertura report.
 *
 * HOW IT IS RUN, AND WHAT IT NEVER DOES (C-J, C-L)
 * -----------------------------------------------
 *   cd tests/e2e && npm ci && npx playwright test
 *
 * The stack is brought up beforehand, and exclusively, by
 * `orchestration/docker-compose.yml`. Nothing in this file starts, stops,
 * restarts, builds, seeds or health-gates a service. Every import resolves inside
 * `tests/e2e`, so nothing here reaches the read-only behavioural-oracle assets
 * that sit beside this directory (C-C).
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules were
 * provided. That is a finding, not an omission, and it is not licence to lower the
 * bar — the enterprise-standard baseline applies in its place, and its operative
 * clause here is the absolute one: **no secret in source, in configuration, or in
 * any artifact this run produces.**
 */

import { expect, test } from '@playwright/test';

import {
  anonymousHeaders,
  bearerHeaders,
  gatewayUrl,
  JWKS_PATH,
  OIDC_DISCOVERY_PATH,
  PING_PATH,
  SECURITY_BASE_URL,
  SECURITY_DEFAULT_BASE_URL,
  type ServiceToken,
} from '../fixtures';

import {
  assertTokenIssuanceProvisioned,
  requireServiceToken,
} from '../fixtures/token-issuance';

import { requireLiveStack } from '../fixtures/live-stack';

import {
  JSON_MEDIA_TYPE,
  PING_AUTHENTICATED,
  PING_RESPONSE,
  PROBLEM_JSON_MEDIA_TYPE,
  GATEWAY_SERVICE_TOKEN,
  assertFullUri,
  assertMediaType,
  assertMembers,
  describeShapeForFailure,
  readRepositoryText,
} from '../fixtures/contract-shape';

/**
 * THE REPOSITORY READERS MOVED, and both callers here now share them.
 *
 * `repositoryRoot()` and `readRepositoryText()` used to be declared in this file
 * and nowhere else. They are now in `fixtures/contract-shape.ts` beside the
 * contract-shape assertions that need them, because the readiness spec's
 * anti-drift guard reads the published Gateway contract for exactly the same
 * reason this spec reads the published Security contract — and two copies of a
 * repository-root walk is one copy too many.
 *
 * The confinement discipline is unchanged and is the caller's to keep: this suite
 * is READ-ONLY against the repository and must never reach the read-only
 * behavioural-oracle assets (C-C). The paths read below are the published Security
 * contract and two service settings files; none is inside `ws_objects/`.
 */

/**
 * The one member of a JSON Web Key Set this spec reads.
 *
 * Declared as `unknown` rather than as an array of a key type, so that every
 * narrowing below is performed explicitly at run time. The published schema marks
 * `keys` required and forbids additional properties, but a spec whose job is to
 * verify a boundary must not *assume* the boundary conformed — that is the thing
 * under test.
 */
interface JsonWebKeySetDocument {
  readonly keys?: unknown;
}

/**
 * The one member of the discovery document this spec reads.
 *
 * `jwks_uri` keeps its wire spelling: it is a standardized discovery field, and
 * renaming it here would hide which field is actually being asserted.
 */
interface ProviderMetadataDocument {
  readonly jwks_uri?: unknown;
}

/**
 * `failOnStatusCode` is pinned to `false` on every request below, and on the
 * `401` case that is a requirement rather than a preference.
 *
 * Left to throw on a non-2xx, the runner raises an error of its own whose message
 * can quote the response body verbatim — on an authenticated endpoint that is the
 * one place a body must never reach a log. Pinning it false means every status is
 * handled by an assertion in this file, so exactly one message shape is produced
 * and it quotes nothing. It also keeps the diagnostic honest: a failure reports
 * *which* status arrived instead of an opaque transport error.
 */
const NEVER_THROW_ON_STATUS = { failOnStatusCode: false } as const;

test.describe('Authentication (constraint C-G)', () => {
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

  // Deliberately NOT `mode: 'serial'`. These four assertions share no state, in
  // no order, and each acquires whatever it needs for itself; serial execution
  // belongs to the two mutating workflow specs, where row state really is shared.

  test('/v1/ping rejects an unauthenticated request with 401', async ({
    request,
  }) => {
    // No credential, said out loud. `anonymousHeaders()` reads as intent at the
    // call site where a bare `{}` would read as something left unfinished.
    const response = await request.get(gatewayUrl(PING_PATH), {
      headers: anonymousHeaders(),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      '/v1/ping must reject an unauthenticated request with 401. Anything else ' +
        'is a contract failure, and a 2xx is the most serious violation this ' +
        'suite can detect: it would mean the ingress boundary is open. The ' +
        'legacy framework had no listening socket and no authentication of any ' +
        'kind, so this boundary was created by the decomposition itself — this ' +
        'assertion is the proof it was created closed. A 404 instead would mean ' +
        'the endpoint is not mapped; a 5xx would mean the authentication layer ' +
        'faults on an anonymous request rather than refusing it.',
    ).toBe(401);

    // Asserted separately and on purpose. The status check above is exact today,
    // but this is the guard that a future edit loosening it cannot let a 2xx
    // through: `ok()` is true for any 2xx and false for 401, so the two
    // assertions fail for different reasons and neither subsumes the other.
    expect(
      response.ok(),
      'an unauthenticated /v1/ping must not report success; a successful ' +
        'response here means the guard is not wired at all',
    ).toBe(false);

    // THE BODY IS ASSERTED, AND THE REASON THIS COMMENT ONCE SAID OTHERWISE IS WORTH RECORDING.
    // It read: no schema is published for this response, the 401 is a cross-cutting outcome of the
    // bearer scheme answered before the route runs, so inventing a shape would be inventing a
    // requirement (C-B). The premise was simply wrong. `gateway.v1.yaml` publishes a shared
    // `Unauthorized` response whose only content is `application/problem+json` carrying
    // `ProblemDetails`, and every authenticated operation in the document references it — so the
    // shape is not invented here, it is quoted. Worse, the omission MASKED A REAL DEFECT: the
    // service advertised that body and returned an empty one, because neither diagnostics
    // middleware was installed, and this was the assertion positioned to catch it.
    // EXACTLY the problem media type, parameters and all. This was
    // `toContain('application/problem+json')`, which a `text/html` page mentioning
    // the string would have satisfied and which said nothing about an unexpected
    // media-type parameter. `assertMediaType` compares the type/subtype for
    // equality and permits only `charset=utf-8` beside it.
    assertMediaType(
      response.headers()['content-type'],
      PROBLEM_JSON_MEDIA_TYPE,
      `Gateway ${PING_PATH} refusal`,
    );

    const problemText: string = await response.text();
    let problem: unknown;

    try {
      problem = JSON.parse(problemText) as unknown;
    } catch {
      throw new Error(
        `Gateway answered ${PING_PATH} with an unparseable refusal body while declaring ` +
          `${PROBLEM_JSON_MEDIA_TYPE}. ${describeShapeForFailure(problemText)}.`,
      );
    }

    expect(
      typeof problem === 'object' && problem !== null && !Array.isArray(problem),
      'the refusal body must be a JSON object matching the published ProblemDetails schema. ' +
        `${describeShapeForFailure(problemText)}.`,
    ).toBe(true);

    // The status is read back out of the BODY as well as off the response line. A document that
    // disagreed with its own status would be worse than no document: a caller branching on the
    // parsed value would take the wrong branch while the transport said the right thing.
    expect(
      (problem as { status?: unknown }).status,
      'the published ProblemDetails schema carries the status as a number, and it must agree with ' +
        'the response status',
    ).toBe(401);

    // Deliberately NOT asserted: any particular title or detail wording. The document constrains
    // the shape, not the sentences, and pinning a framework-authored phrase here would fail on a
    // runtime upgrade that changed nothing a caller depends on.
  });

  test('a token minted by Security is accepted on /v1/ping', async ({
    request,
  }) => {
    // The ONLY way a token enters this suite. The helper asks the sole issuer
    // over the published issuance contract and returns what the issuer produced;
    // it mints nothing, signs nothing and reads no signing key. Its own failure
    // messages name the method, the URL and the status, so no wrapper is needed
    // here — and a wrapper would risk quoting a body it must not.
    const token: ServiceToken = await requireServiceToken(request);

    // A non-empty credential, asserted without ever rendering it. Nothing is
    // asserted about its structure, its claims, its segment count or its length:
    // validation is the services' job, performed by their stock bearer handler
    // against the published key set, and a client-side decoder here would invite
    // assertions on internals no contract publishes.
    expect(
      typeof token.accessToken,
      'the issuer must return the credential as a string; the value is ' +
        'deliberately not rendered in this message',
    ).toBe('string');

    expect(
      token.accessToken.length,
      'the issuer returned an empty credential. An empty token builds a ' +
        'syntactically valid Authorization header that every service refuses, ' +
        'so the resulting 401 would be reported against the recipient rather ' +
        'than against the issuer that caused it.',
    ).toBeGreaterThan(0);

    // Per request, never ambient. The scheme presented is the one the issuer
    // stated, which the fixture has already checked against the contract's
    // constant.
    const response = await request.get(gatewayUrl(PING_PATH), {
      headers: bearerHeaders(token),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'Gateway must accept a token minted by Security, the sole issuer. A 401 ' +
        'here means the round trip that the whole token topology rests on is ' +
        'broken: either Gateway cannot reach or cannot use the published ' +
        'verification material, or the audience the token was issued for is not ' +
        'the audience Gateway accepts. A 403 would mean the credential was ' +
        'valid but the granted scope set was insufficient.',
    ).toBe(200);

    // THE ACCEPTED RESPONSE'S SHAPE, WHICH WAS PREVIOUSLY UNASSERTED ALTOGETHER.
    // A 200 alone establishes that the credential was accepted and nothing about
    // what the endpoint answered, so `PingResponse` could have drifted to any
    // shape at all without this suite noticing. Its two constants make the
    // assertion sharp: `service` is `const: gateway`, so a body forwarded from
    // an upstream cannot pass as Gateway's own, and `authenticated` is
    // `const: true`, so the endpoint cannot answer 200 while reporting that it
    // did not authenticate the caller — which is exactly the confusion an
    // always-200 placeholder would produce.
    assertMediaType(
      response.headers()['content-type'],
      JSON_MEDIA_TYPE,
      `Gateway ${PING_PATH}`,
    );

    const ping: Record<string, unknown> = assertMembers(
      (await response.json()) as unknown,
      PING_RESPONSE,
      `Gateway ${PING_PATH} response`,
    );

    expect(
      ping['service'],
      `the ping response must identify its service as '${GATEWAY_SERVICE_TOKEN}', which the ` +
        'published schema pins as a single constant',
    ).toBe(GATEWAY_SERVICE_TOKEN);

    expect(
      ping['authenticated'],
      `the ping response must report authenticated as ${String(PING_AUTHENTICATED)}. The endpoint ` +
        'exists to prove a credential was required and accepted, so a 200 that reported otherwise ' +
        'would contradict the status it just returned.',
    ).toBe(PING_AUTHENTICATED);
  });

  test('Security publishes its verification material anonymously', async ({
    request,
  }) => {
    // Anonymous BY DESIGN, and the design is the thing being verified.
    // Verification material is public by definition — publishing it is what
    // allows a signature to be checked, which is exactly what distinguishes it
    // from a signing key. A consumer forced to authenticate in order to fetch
    // the material it needs in order to authenticate would have a circular
    // dependency on this very service, which is why this endpoint being
    // anonymous is what lets three services validate tokens with stock
    // framework code and no bespoke retrieval logic — and, in turn, why Security
    // speaks REST rather than gRPC.
    const response = await request.get(`${SECURITY_BASE_URL}${JWKS_PATH}`, {
      headers: anonymousHeaders(),
      ...NEVER_THROW_ON_STATUS,
    });

    expect(
      response.status(),
      'the key set must be fetchable anonymously. A 401 here would be the ' +
        'circular dependency described above: no peer could validate anything, ' +
        'and the token topology would be inert.',
    ).toBe(200);

    assertMediaType(
      response.headers()['content-type'],
      JSON_MEDIA_TYPE,
      `Security ${JWKS_PATH}`,
    );

    const document = (await response.json()) as JsonWebKeySetDocument;
    const keys: unknown = document.keys;

    // Only the standardized structural invariants of a JWK Set are asserted:
    // a `keys` array, holding at least one entry, whose first entry declares its
    // key type. These are stable facts fixed by the standard and restated by the
    // published schema, rather than a guess at this service's bespoke shape.
    expect(
      Array.isArray(keys),
      "the key set must carry a 'keys' array; that member is the one thing " +
        'both the standard and the published schema mark required',
    ).toBe(true);

    const entries: readonly unknown[] = Array.isArray(keys) ? keys : [];

    expect(
      entries.length,
      'the key set must publish at least one key. An empty set parses ' +
        'perfectly and validates nothing, so every inbound token would be ' +
        'refused with no indication that the key set was the cause.',
    ).toBeGreaterThan(0);

    const first: unknown = entries[0];
    const firstKey: Record<string, unknown> | undefined =
      typeof first === 'object' && first !== null && !Array.isArray(first)
        ? (first as Record<string, unknown>)
        : undefined;

    expect(
      firstKey,
      'each entry of the key set must be a JSON object; a primitive or an ' +
        'array cannot carry key parameters',
    ).toBeDefined();

    expect(
      typeof firstKey?.['kty'],
      "each published key must declare its key type ('kty'). A stock bearer " +
        'handler dispatches on it to choose a signature algorithm, so a key ' +
        'without one is unusable however well formed the rest of it is.',
    ).toBe('string');

    // NO KEY VALUE IS READ, COMPARED, SNAPSHOTTED, LOGGED OR WRITTEN TO A
    // FIXTURE FILE. Public verification material is not a secret, but the
    // discipline against carrying long encoded runs in spec source applies
    // regardless: the document is read at run time, its structure is asserted,
    // and it is discarded.
  });

  test('OIDC discovery advertises the key set for stock bearer handlers', async ({
    request,
  }) => {
    // Anonymous for the same reason: a consumer's bearer handler fetches this
    // document in order to LEARN how to authenticate, so it cannot already hold
    // a token when it asks.
    const response = await request.get(
      `${SECURITY_BASE_URL}${OIDC_DISCOVERY_PATH}`,
      {
        headers: anonymousHeaders(),
        ...NEVER_THROW_ON_STATUS,
      },
    );

    expect(
      response.status(),
      'the discovery document must be fetchable anonymously; a consumer reads ' +
        'it before it has any credential to present',
    ).toBe(200);

    assertMediaType(
      response.headers()['content-type'],
      JSON_MEDIA_TYPE,
      `Security ${OIDC_DISCOVERY_PATH}`,
    );

    const document = (await response.json()) as ProviderMetadataDocument;
    const jwksUri: unknown = document.jwks_uri;

    expect(
      typeof jwksUri,
      "the discovery document must advertise 'jwks_uri' as a string. This is " +
        'the single member that makes zero-bespoke-code verification possible: ' +
        'a consumer points its stock handler here and never fetches a key ' +
        'itself. Without it, key retrieval becomes hand-written code in three ' +
        'separate services.',
    ).toBe('string');

    // THE FULL URI, NOT A SUFFIX. This assertion used to be
    // `jwksUri.endsWith(JWKS_PATH)`, which accepted the key set on ANY ORIGIN:
    // a document advertising `https://somewhere-else.example/.well-known/jwks.json`
    // satisfied it completely. That is precisely the drift that matters here,
    // because a consumer's stock bearer handler fetches whatever this member
    // says and then trusts the keys it finds — so an advertised origin nobody
    // verified is an unverified trust anchor for three services.
    //
    // The expected value is COMPOSED from this suite's own configuration rather
    // than written out, so the key set still has exactly one spelling here and
    // the two assertions cannot drift apart. `assertFullUri` compares through
    // `URL`, so an equivalent spelling — a default port written out, a trailing
    // slash — is not reported as a mismatch while a different origin or path
    // still is, and it additionally refuses a query or fragment.
    //
    // The advertised value IS named in the failure message, and that is a
    // deliberate exception to this file's no-response-values rule with a stated
    // reason: a URI is the one thing here that is not a credential, it is
    // published anonymously to every caller by design, and a mismatch is
    // undiagnosable without seeing what was advertised.
    assertFullUri(
      jwksUri,
      `${SECURITY_BASE_URL}${JWKS_PATH}`,
      "the discovery document's 'jwks_uri'",
    );

    // The rest of the document is deliberately unasserted. The issuer
    // identifier, the advertised algorithm list and the remaining informational
    // members are not fixed by anything this refactor states, so asserting one
    // would invent a requirement (C-B).
  });

  test(
    'the Security base-URL default agrees with the published listener and OpenAPI server (no stack)',
    { tag: '@no-stack' },
    () => {
      // ===================================================================
      //  WHY THIS ASSERTION EXISTS, AND WHY IT COMPARES THE *DEFAULT*
      //
      //  SECURITY_BASE_URL defaulted to `http://localhost:5104` while Security
      //  declares exactly one listener and it is TLS. Nothing failed loudly:
      //  every token, key-set and discovery request in this suite simply
      //  addressed a listener that does not exist, and the configured client
      //  certificate could never be presented, because a client certificate
      //  only exists inside a TLS handshake. The mutual-TLS issuance edge was
      //  therefore untestable while appearing to be configured.
      //
      //  The comparison is made against SECURITY_DEFAULT_BASE_URL and NOT
      //  against the resolved SECURITY_BASE_URL, deliberately. An environment
      //  variable may legitimately point this suite at another origin, so
      //  asserting the resolved value would either forbid that or pass
      //  vacuously the moment it was used - and in the latter case the default
      //  could drift back to a scheme the repository does not declare with no
      //  test objecting. The default is the thing that has to stay true of the
      //  repository, so the default is what is compared.
      //
      //  THREE SOURCES ARE READ, because agreeing with only one would not be
      //  coherence: the OpenAPI document publishes where callers send requests,
      //  the base settings file declares the listener, and the Development
      //  overlay is where a relaxation would be introduced if one ever were.
      // ===================================================================
      const defaultUrl = new URL(SECURITY_DEFAULT_BASE_URL);

      expect(
        defaultUrl.protocol,
        'the default scheme must be https: Security declares one TLS listener ' +
          'and a client certificate cannot be presented without a handshake',
      ).toBe('https:');
      expect(defaultUrl.port).toBe('5104');

      // ---- source 1: the published contract ----
      const publishedContract = readRepositoryText(
        'shared/PowerFramework.Contracts/OpenApi/security.v1.yaml',
      );

      // Matched with a pattern rather than parsed, because this suite installs
      // no YAML parser and adding one to read a single scalar would be a
      // dependency for an assertion. The pattern is anchored on the `url:` key
      // inside the servers block, and comment lines cannot satisfy it because
      // the capture requires the key at the start of a list entry.
      const publishedServer = /^\s*-\s*url:\s*(\S+)\s*$/m.exec(publishedContract);

      expect(
        publishedServer,
        'security.v1.yaml must publish a servers entry; without one there is ' +
          'nothing for the fixture default to agree with',
      ).not.toBeNull();

      const publishedUrl = new URL((publishedServer as RegExpExecArray)[1] as string);

      expect(publishedUrl.protocol).toBe(defaultUrl.protocol);
      expect(publishedUrl.port).toBe(defaultUrl.port);

      // ---- sources 2 and 3: the listener, in base settings and in the overlay ----
      for (const settingsPath of [
        'services/security-service/PowerFramework.Security/appsettings.json',
        'services/security-service/PowerFramework.Security/appsettings.Development.json',
      ]) {
        const settings = readRepositoryText(settingsPath);

        // Read with a pattern for the same reason, and additionally because
        // these files are JSON WITH COMMENTS: JSON.parse rejects them outright,
        // so a structural read would need a tolerant parser this suite does not
        // have. The Kestrel endpoint URL is unambiguous in both files.
        const declaredListener = /"Url"\s*:\s*"([^"]+)"/.exec(settings);

        expect(
          declaredListener,
          `${settingsPath} must declare a Kestrel endpoint URL`,
        ).not.toBeNull();

        const declared = (declaredListener as RegExpExecArray)[1] as string;

        expect(
          declared.startsWith('https://'),
          `${settingsPath} must declare an https listener. A cleartext ` +
            'listener here would make the client-certificate issuance edge ' +
            'unreachable and would publish the verification material every ' +
            'other service trusts over a channel an attacker can rewrite.',
        ).toBe(true);

        expect(
          declared.endsWith(`:${defaultUrl.port}`),
          `${settingsPath} must declare the listener on port ${defaultUrl.port}`,
        ).toBe(true);
      }

      // The resolved value is not asserted, only reported as agreeing when it
      // has not been overridden - which keeps an override legal while still
      // catching the case where the default itself was never applied.
      if (process.env['SECURITY_BASE_URL'] === undefined) {
        expect(SECURITY_BASE_URL).toBe(SECURITY_DEFAULT_BASE_URL);
      }
    },
  );
});
