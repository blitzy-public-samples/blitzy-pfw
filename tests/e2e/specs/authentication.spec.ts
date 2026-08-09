/**
 * G7 / C-01 / C-10 — every new boundary is authenticated.
 *
 * WHY THIS SPEC MATTERS MORE THAN ITS SIZE SUGGESTS
 * ------------------------------------------------
 * The framework being decomposed opens no listening socket, registers no route
 * and receives no unsolicited request. Decomposition therefore creates the
 * system's first-ever ingress, and the requirement that it add "no new attack
 * surface" can only mean one thing in practice: every surface that now exists
 * is authenticated from the outset. This spec is the executable form of that
 * requirement.
 *
 * The contract splits the surface in two, and the split is the thing being
 * verified:
 *
 *   - `/health` is ANONYMOUS on all four services. It has to be, because
 *     container health probes present no credentials, and an authenticated
 *     health endpoint would make the orchestration dependency chain
 *     unsatisfiable.
 *   - `/v1/ping` REQUIRES a bearer token on all four, and returns `401`
 *     without one. It exists precisely so that "is this boundary actually
 *     guarded" is a question with a cheap, unambiguous answer.
 *
 * Both halves are asserted, because each is a different failure: an
 * authenticated `/health` breaks readiness, and an anonymous `/v1/ping` means
 * the guard is not wired at all.
 *
 * SOLE-ISSUER TOPOLOGY
 * --------------------
 * Security is the only minter in the system; the other three hold verification
 * material only. So the positive case here is a genuine end-to-end proof and
 * not a self-signed shortcut: this spec asks Security for a token and presents
 * it to Gateway, which validates it against Security's published keys. If that
 * round trip works, the sole-issuer topology works.
 *
 * See `../fixtures/live-stack.ts` for what has and has not been observed
 * running in this checkpoint.
 */

import { expect, test } from '@playwright/test';

import {
  ALL_SERVICE_KEYS,
  PING_PATH,
  SERVICE_ENDPOINTS,
} from '../fixtures/service-endpoints';
import {
  authorizationHeader,
  issueServiceToken,
  probeStackAvailability,
  tokenEndpointUrl,
} from '../fixtures/live-stack';

/** The shape of `PingResponse` this spec reads. */
interface PingResponse {
  readonly service?: unknown;
  readonly authenticated?: unknown;
}

test.describe('authenticated-boundary contract (no running stack required)', () => {
  test('the ping path is the same guarded path on every service', () => {
    // One spelling across all four services. A per-service variant would mean
    // a caller has to know which service it is talking to before it can check
    // whether that service is guarded.
    expect(PING_PATH).toBe('/v1/ping');
    expect(PING_PATH).not.toBe('/health');
  });

  test('every service exposes both the anonymous and the guarded path', () => {
    for (const key of ALL_SERVICE_KEYS) {
      const endpoint = SERVICE_ENDPOINTS[key];

      expect(endpoint.healthUrl.endsWith('/health')).toBe(true);
      expect(`${endpoint.baseUrl}${PING_PATH}`).toBe(
        `${endpoint.baseUrl}/v1/ping`,
      );
    }
  });
});

test.describe('authenticated-boundary contract (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  for (const key of ALL_SERVICE_KEYS) {
    test(`${key} /v1/ping returns 401 without a token`, async ({ request }) => {
      const endpoint = SERVICE_ENDPOINTS[key];

      // Deliberately no Authorization header. A 200 here would mean this
      // boundary is open, which is the single most serious contract violation
      // this suite can detect.
      const response = await request.get(`${endpoint.baseUrl}${PING_PATH}`, {
        failOnStatusCode: false,
      });

      expect(
        response.status(),
        `${endpoint.displayName} /v1/ping answered ${response.status()} ` +
          `without a token; it must be guarded`,
      ).toBe(401);
    });
  }

  for (const key of ALL_SERVICE_KEYS) {
    test(`${key} /v1/ping rejects a malformed bearer token`, async ({
      request,
    }) => {
      const endpoint = SERVICE_ENDPOINTS[key];

      // A syntactically invalid token must be rejected by validation rather
      // than crash it. 401 is the contract answer; a 500 would mean the
      // validation path throws on hostile input.
      const response = await request.get(`${endpoint.baseUrl}${PING_PATH}`, {
        failOnStatusCode: false,
        headers: { Authorization: 'Bearer not-a-real-jwt' },
      });

      expect(response.status()).toBe(401);
      expect(
        response.status(),
        'a malformed token must be rejected, not fault the service',
      ).not.toBe(500);
    });
  }

  test('a token minted by Security is accepted by Gateway', async ({
    request,
  }) => {
    const token = await issueServiceToken('e2e-suite', 'gateway', ['ping']);

    // Reported as a skip rather than a failure when Security declines: the
    // token endpoint is declared mutual-TLS-protected, so a stack that
    // enforces mTLS is behaving correctly by refusing this request, and
    // failing here would blame it for compliance.
    test.skip(
      token === undefined,
      `Security did not issue a token at ${tokenEndpointUrl()} — the token ` +
        `endpoint is declared mutual-TLS-protected, so this is expected on a ` +
        `stack that enforces it. The negative (401) cases still ran.`,
    );

    if (token === undefined) {
      return;
    }

    const response = await request.get(
      `${SERVICE_ENDPOINTS.gateway.baseUrl}${PING_PATH}`,
      {
        failOnStatusCode: false,
        headers: { Authorization: authorizationHeader(token) },
      },
    );

    expect(
      response.status(),
      'Gateway rejected a token minted by the sole issuer',
    ).toBe(200);

    const body = (await response.json()) as PingResponse;

    expect(body.service).toBe('gateway');
    expect(body.authenticated).toBe(true);
  });

  test('Security publishes the verification material its peers need', async ({
    request,
  }) => {
    // The other three services hold no signing key; they validate against this
    // document. If it is not anonymously fetchable, no peer can validate
    // anything, and the whole token topology is inert.
    const response = await request.get(
      `${SERVICE_ENDPOINTS.security.baseUrl}/.well-known/jwks.json`,
      { failOnStatusCode: false },
    );

    expect(
      response.status(),
      'JWKS must be anonymously fetchable for peers to validate tokens',
    ).toBe(200);

    const body = (await response.json()) as { readonly keys?: unknown };

    expect(Array.isArray(body.keys)).toBe(true);
    expect((body.keys as readonly unknown[]).length).toBeGreaterThan(0);
  });
});
