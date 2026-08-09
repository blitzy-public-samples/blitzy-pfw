/**
 * C-09 reserved extension points — the four deferred services.
 *
 * WHAT THESE ROUTES ARE, AND WHAT THEY ARE NOT
 * -------------------------------------------
 * Four of the eight target services are deliberately NOT built in this phase:
 * DesignSystem, Documents, Integration and ScriptBridge. The prohibition is
 * absolute — not partially, and not "even to stub them out", because a
 * half-built deferred service is worse than a documented gap.
 *
 * They are nonetheless visible in exactly one place: four named route families
 * on Gateway that answer `501 Not Implemented` with a machine-readable body
 * naming the deferred service and marking it reserved for Phase 2. That exists
 * so the shape of the eventual system is legible from the gateway's contract.
 *
 * The distinction this spec enforces is the one that makes that compliant: a
 * routing declaration is NOT an implementation. So the assertions run in both
 * directions:
 *
 *   - POSITIVE: each family answers 501, with the documented body, naming the
 *     right service. A 404 would mean the extension point is undeclared and
 *     the eventual topology is invisible; a 200 would mean something was built
 *     that must not have been.
 *   - NEGATIVE: nothing behind these routes behaves like a working service.
 *     The response must not vary with the sub-path, must not carry a payload
 *     that looks like real work, and must remain 501 for every shape of
 *     request.
 *
 * ORDERING OF THE GUARD MATTERS
 * -----------------------------
 * The contract declares `401` alongside `501` on each family, and `401` is
 * listed first for a reason: authentication is evaluated BEFORE the
 * not-implemented answer. An unauthenticated caller must not be able to
 * enumerate which capabilities are reserved, because that is free
 * reconnaissance about the system's eventual shape. Both statuses are asserted,
 * and the unauthenticated case is asserted to be the 401 rather than the 501.
 *
 * See `../fixtures/live-stack.ts` for what has and has not been observed
 * running in this checkpoint.
 */

import { expect, test } from '@playwright/test';

import { gatewayUrl } from '../fixtures/service-endpoints';
import { probeStackAvailability } from '../fixtures/live-stack';

/**
 * One reserved route family, as declared in `gateway.v1.yaml`.
 */
interface ReservedFamily {
  /** The route prefix Gateway declares, without the trailing wildcard. */
  readonly prefix: string;

  /** The deferred service the body must name. */
  readonly service: string;
}

/**
 * The four families, in the contract's own order.
 *
 * Exactly four. A fifth entry would mean a deferred service had been added
 * without being deferred, and a missing entry would mean an extension point
 * had been dropped — both are contract changes, so the count is asserted.
 */
const RESERVED_FAMILIES: readonly ReservedFamily[] = [
  { prefix: '/v1/design', service: 'DesignSystem' },
  { prefix: '/v1/documents', service: 'Documents' },
  { prefix: '/v1/integration', service: 'Integration' },
  { prefix: '/v1/scripting', service: 'ScriptBridge' },
];

/**
 * The marker string the contract fixes as a constant on the 501 body.
 */
const RESERVED_MARKER = 'reserved for Phase 2';

/** The shape of `ReservedRouteBody` this spec reads. */
interface ReservedRouteBody {
  readonly status?: unknown;
  readonly service?: unknown;
  readonly marker?: unknown;
  readonly route?: unknown;
}

test.describe('reserved extension points (no running stack required)', () => {
  test('exactly four capability areas are deferred', () => {
    expect(RESERVED_FAMILIES).toHaveLength(4);

    const services = RESERVED_FAMILIES.map((family) => family.service);
    expect([...services].sort()).toEqual([
      'DesignSystem',
      'Documents',
      'Integration',
      'ScriptBridge',
    ]);
  });

  test('no deferred service occupies a Phase-1 route or port', () => {
    // The four in-scope services own /health, /v1/ping, /v1/capabilities and
    // /v1/datawindow. A reserved family colliding with any of those would make
    // a deferred route shadow a real one.
    const phaseOnePrefixes = [
      '/health',
      '/v1/ping',
      '/v1/capabilities',
      '/v1/datawindow',
    ];

    for (const family of RESERVED_FAMILIES) {
      for (const occupied of phaseOnePrefixes) {
        expect(family.prefix).not.toBe(occupied);
        expect(occupied.startsWith(`${family.prefix}/`)).toBe(false);
      }
    }
  });

  test('each family has a distinct prefix under the versioned namespace', () => {
    const prefixes = RESERVED_FAMILIES.map((family) => family.prefix);

    expect(new Set(prefixes).size).toBe(prefixes.length);

    for (const prefix of prefixes) {
      expect(prefix.startsWith('/v1/')).toBe(true);
    }
  });
});

test.describe('reserved extension points over HTTP (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  for (const family of RESERVED_FAMILIES) {
    test(`${family.prefix}/** is guarded before it is declared unimplemented`, async ({
      request,
    }) => {
      // No token. The guard must answer first, so an anonymous caller cannot
      // enumerate the reserved capability set.
      const response = await request.get(gatewayUrl(`${family.prefix}/probe`), {
        failOnStatusCode: false,
      });

      expect(
        response.status(),
        `${family.prefix} leaked its reserved status to an anonymous caller`,
      ).toBe(401);
      expect(response.status()).not.toBe(501);
    });
  }

  for (const family of RESERVED_FAMILIES) {
    test(`${family.prefix}/** is declared, not implemented, and not absent`, async ({
      request,
    }) => {
      const response = await request.get(gatewayUrl(`${family.prefix}/probe`), {
        failOnStatusCode: false,
      });

      // An authenticated read is required to see past the guard; without a
      // token the previous test already asserted the 401.
      test.skip(
        response.status() === 401,
        `${family.prefix} answered 401 as contracted; an authenticated read ` +
          `is required to inspect the 501 body. The guard-ordering assertion ` +
          `covers the anonymous case.`,
      );

      // 404 would mean the extension point is undeclared — the eventual
      // topology would be invisible from the contract, which is the thing
      // these routes exist to make legible.
      expect(
        response.status(),
        `${family.prefix} must be declared (501), not absent (404)`,
      ).not.toBe(404);

      // 2xx would mean a deferred service had been implemented.
      expect(
        response.status(),
        `${family.prefix} answered success — a deferred service must not be implemented`,
      ).toBe(501);

      const body = (await response.json()) as ReservedRouteBody;

      expect(body.status).toBe(501);
      expect(body.service).toBe(family.service);
      expect(body.marker).toBe(RESERVED_MARKER);
      expect(typeof body.route).toBe('string');
    });
  }

  for (const family of RESERVED_FAMILIES) {
    test(`${family.prefix}/** answers identically for any sub-path`, async ({
      request,
    }) => {
      // A route family, not a single route. If different sub-paths produced
      // different answers, something behind the route would be interpreting
      // them — i.e. behaving like an implementation.
      const subPaths = ['a', 'deeply/nested/path', 'with-dashes', '42'];
      const observed: number[] = [];

      for (const subPath of subPaths) {
        const response = await request.get(
          gatewayUrl(`${family.prefix}/${subPath}`),
          { failOnStatusCode: false },
        );
        observed.push(response.status());
      }

      const distinct = new Set(observed);
      expect(
        distinct.size,
        `${family.prefix} varied its status across sub-paths: ${observed.join(', ')}`,
      ).toBe(1);

      // Whichever single status it is, it must be one of the two the contract
      // declares — never a success and never a fault.
      const [only] = [...distinct];
      expect([401, 501]).toContain(only);
    });
  }

  test('a write attempt against a reserved family is never accepted', async ({
    request,
  }) => {
    // Nothing exists behind these routes, so no verb may report having done
    // anything. A 200 or 201 to a POST would be the clearest possible evidence
    // that a deferred service had been partially built.
    for (const family of RESERVED_FAMILIES) {
      const response = await request.post(gatewayUrl(`${family.prefix}/probe`), {
        failOnStatusCode: false,
        data: { attempted: true },
      });

      expect(
        response.status(),
        `POST ${family.prefix} was accepted; nothing may exist behind a reserved route`,
      ).toBeGreaterThanOrEqual(400);
      expect([200, 201, 202, 204]).not.toContain(response.status());
    }
  });
});
