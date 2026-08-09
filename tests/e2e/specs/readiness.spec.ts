/**
 * C-10 — health and readiness across the four-service stack.
 *
 * WHAT THIS FILE VERIFIES
 * -----------------------
 * The contract published in `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml`
 * makes four claims about readiness, and each is asserted below:
 *
 *   1. `/health` is ANONYMOUS on every service — no token, no 401.
 *   2. Gateway's `/health` returns an aggregate report naming its three
 *      upstreams, not a bare "ok".
 *   3. Gateway reports healthy ONLY once Persistence, DataServices and
 *      Security do. That ordering is the specific property the orchestration
 *      chain exists to enforce, and it is the one the environment's readiness
 *      gates check by hand.
 *   4. Gateway's aggregate names exactly the three upstreams the topology has —
 *      no fourth, and no deferred service.
 *
 * TWO CLASSES OF ASSERTION, KEPT SEPARATE ON PURPOSE
 * -------------------------------------------------
 * The first `describe` block needs no running stack: it checks the topology
 * this suite was configured against — the port band, the upstream roster, the
 * anonymous health path. Those are properties of the fixtures and of the
 * contract, so they execute and pass in every environment, including this one.
 *
 * The second block drives real HTTP and is gated on a live stack. See
 * `../fixtures/live-stack.ts` for why the gate is an explicit skip rather than
 * a softened assertion, and for the honest statement of what has and has not
 * been observed running.
 */

import { expect, test } from '@playwright/test';

import {
  ALL_SERVICE_KEYS,
  GATEWAY_HEALTH_AGGREGATION_UPSTREAMS,
  HEALTH_PATH,
  SERVICE_ENDPOINTS,
  type ServiceKey,
} from '../fixtures/service-endpoints';
import { probeStackAvailability } from '../fixtures/live-stack';

/**
 * The status vocabulary `AggregateHealthReport.status` is closed over.
 * Mirrors the contract's enum exactly.
 */
const AGGREGATE_STATUSES = ['Healthy', 'Degraded', 'Unhealthy'] as const;

/**
 * The status vocabulary `UpstreamHealth.status` is closed over. Wider than the
 * aggregate's by one value: an upstream can be `Unreachable`, whereas the
 * aggregate expresses that as `Unhealthy`.
 */
const UPSTREAM_STATUSES = [
  'Healthy',
  'Degraded',
  'Unhealthy',
  'Unreachable',
] as const;

/** The shape of `AggregateHealthReport` this spec reads. */
interface AggregateHealthReport {
  readonly status?: unknown;
  readonly service?: unknown;
  readonly upstreams?: unknown;
}

/** The shape of `UpstreamHealth` this spec reads. */
interface UpstreamHealth {
  readonly service?: unknown;
  readonly status?: unknown;
}

test.describe('C-10 readiness topology (no running stack required)', () => {
  test('the suite targets exactly the four Phase-1 services', () => {
    // Four services ship in Phase 1. A fifth entry here would mean a deferred
    // service had acquired a test surface, which the scope boundary forbids.
    expect(ALL_SERVICE_KEYS).toHaveLength(4);
    expect([...ALL_SERVICE_KEYS].sort()).toEqual([
      'dataservices',
      'gateway',
      'persistence',
      'security',
    ]);
  });

  test('every service is pinned to its documented port in the 5101-5105 band', () => {
    // 5103 is deliberately absent: it was assigned to the deferred
    // DesignSystem service and is left reserved rather than reassigned, so the
    // Phase-2 slot stays obvious.
    expect(SERVICE_ENDPOINTS.persistence.port).toBe(5101);
    expect(SERVICE_ENDPOINTS.dataservices.port).toBe(5102);
    expect(SERVICE_ENDPOINTS.security.port).toBe(5104);
    expect(SERVICE_ENDPOINTS.gateway.port).toBe(5105);

    const ports = ALL_SERVICE_KEYS.map((key) => SERVICE_ENDPOINTS[key].port);
    expect(ports).not.toContain(5103);
    expect(new Set(ports).size).toBe(ports.length);
  });

  test('Gateway aggregates exactly its three upstreams, and never itself', () => {
    expect([...GATEWAY_HEALTH_AGGREGATION_UPSTREAMS].sort()).toEqual([
      'dataservices',
      'persistence',
      'security',
    ]);

    // Self-aggregation would make the readiness gate circular: Gateway would
    // report healthy because Gateway reported healthy.
    expect(GATEWAY_HEALTH_AGGREGATION_UPSTREAMS).not.toContain('gateway');
  });

  test('the health path is the same anonymous path on every service', () => {
    expect(HEALTH_PATH).toBe('/health');

    for (const key of ALL_SERVICE_KEYS) {
      const endpoint = SERVICE_ENDPOINTS[key];
      expect(endpoint.healthUrl).toBe(`${endpoint.baseUrl}${HEALTH_PATH}`);
    }
  });
});

test.describe('C-10 readiness over HTTP (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  for (const key of ['persistence', 'dataservices', 'security'] as const) {
    test(`${key} answers /health anonymously`, async ({ request }) => {
      const endpoint = SERVICE_ENDPOINTS[key satisfies ServiceKey];

      // No Authorization header is sent. C-10 makes /health anonymous on all
      // four services, so a 401 here is a contract violation, not a config
      // problem.
      const response = await request.get(endpoint.healthUrl, {
        failOnStatusCode: false,
      });

      expect(
        response.status(),
        `${endpoint.displayName} /health must be anonymous`,
      ).not.toBe(401);
      expect([200, 503]).toContain(response.status());
    });
  }

  test('Gateway /health is anonymous and returns the aggregate report', async ({
    request,
  }) => {
    const response = await request.get(SERVICE_ENDPOINTS.gateway.healthUrl, {
      failOnStatusCode: false,
    });

    expect(response.status()).not.toBe(401);
    expect([200, 503]).toContain(response.status());

    const body = (await response.json()) as AggregateHealthReport;

    expect(body.service).toBe('gateway');
    expect(AGGREGATE_STATUSES).toContain(body.status);
    expect(Array.isArray(body.upstreams)).toBe(true);

    const upstreams = body.upstreams as readonly UpstreamHealth[];
    const reported = upstreams.map((entry) => entry.service);

    // Exactly the three upstreams, no more and no fewer. A deferred service
    // appearing here would mean something was built that must not be.
    expect([...reported].sort()).toEqual([
      'dataservices',
      'persistence',
      'security',
    ]);

    for (const entry of upstreams) {
      expect(UPSTREAM_STATUSES).toContain(entry.status);
    }
  });

  test('Gateway reports healthy only when all three upstreams are healthy', async ({
    request,
  }) => {
    const gatewayResponse = await request.get(
      SERVICE_ENDPOINTS.gateway.healthUrl,
      { failOnStatusCode: false },
    );
    const report = (await gatewayResponse.json()) as AggregateHealthReport;
    const upstreams = (report.upstreams ?? []) as readonly UpstreamHealth[];

    const everyUpstreamHealthy =
      upstreams.length === GATEWAY_HEALTH_AGGREGATION_UPSTREAMS.length &&
      upstreams.every((entry) => entry.status === 'Healthy');

    if (report.status === 'Healthy') {
      // The direction that matters. Gateway claiming health while an upstream
      // is down is precisely the failure the dependency chain exists to
      // prevent, so it is asserted as an implication rather than a correlation.
      expect(
        everyUpstreamHealthy,
        'Gateway reported Healthy while at least one upstream did not',
      ).toBe(true);
      expect(gatewayResponse.status()).toBe(200);
    } else {
      // The converse: an unhealthy aggregate must be explained by its parts,
      // not merely asserted.
      expect(
        everyUpstreamHealthy,
        `Gateway reported ${String(report.status)} while every upstream was Healthy`,
      ).toBe(false);
    }
  });

  test('each upstream self-report agrees with Gateway aggregate view', async ({
    request,
  }) => {
    const gatewayResponse = await request.get(
      SERVICE_ENDPOINTS.gateway.healthUrl,
      { failOnStatusCode: false },
    );
    const report = (await gatewayResponse.json()) as AggregateHealthReport;
    const upstreams = (report.upstreams ?? []) as readonly UpstreamHealth[];

    for (const key of GATEWAY_HEALTH_AGGREGATION_UPSTREAMS) {
      const endpoint = SERVICE_ENDPOINTS[key];
      const direct = await request.get(endpoint.healthUrl, {
        failOnStatusCode: false,
      });
      const aggregated = upstreams.find((entry) => entry.service === key);

      expect(
        aggregated,
        `Gateway omitted ${endpoint.displayName} from its aggregate`,
      ).toBeDefined();

      // A direct 200 and an aggregated non-Healthy would mean Gateway is
      // reading a different service than this suite is, which invalidates
      // every other aggregate assertion.
      if (direct.status() === 200) {
        expect(aggregated?.status).toBe('Healthy');
      } else {
        expect(aggregated?.status).not.toBe('Healthy');
      }
    }
  });
});
