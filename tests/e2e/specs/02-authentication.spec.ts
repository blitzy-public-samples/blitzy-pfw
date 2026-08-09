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
 *   const token = await acquireServiceToken(request);   // once, per test
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
  acquireServiceToken,
  anonymousHeaders,
  bearerHeaders,
  gatewayUrl,
  JWKS_PATH,
  OIDC_DISCOVERY_PATH,
  PING_PATH,
  SECURITY_BASE_URL,
  type ServiceToken,
} from '../fixtures';

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

    // Nothing is asserted about the body. No schema is published for this
    // response — the 401 is a cross-cutting outcome of the bearer scheme,
    // answered before the route runs — so inventing a shape for it would be
    // inventing a requirement (C-B).
  });

  test('a token minted by Security is accepted on /v1/ping', async ({
    request,
  }) => {
    // The ONLY way a token enters this suite. The helper asks the sole issuer
    // over the published issuance contract and returns what the issuer produced;
    // it mints nothing, signs nothing and reads no signing key. Its own failure
    // messages name the method, the URL and the status, so no wrapper is needed
    // here — and a wrapper would risk quoting a body it must not.
    const token: ServiceToken = await acquireServiceToken(request);

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

    // The path is resolved from the imported constant rather than written out
    // again, so the key set has exactly one spelling in this suite and the two
    // assertions cannot drift apart.
    expect(
      typeof jwksUri === 'string' && jwksUri.endsWith(JWKS_PATH),
      `the advertised jwks_uri must end with ${JWKS_PATH}, so that the ` +
        'document a consumer self-configures from points at the key set this ' +
        'suite just proved is anonymously reachable. The advertised value is ' +
        'deliberately not rendered in this message: it is read from a response ' +
        'and no value read from Security is placed in a log line.',
    ).toBe(true);

    // The rest of the document is deliberately unasserted. The issuer
    // identifier, the advertised algorithm list and the remaining informational
    // members are not fixed by anything this refactor states, so asserting one
    // would invent a requirement (C-B).
  });
});
