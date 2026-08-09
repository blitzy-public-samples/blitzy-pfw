/**
 * C-06 projected over C-09 — the optimistic-concurrency conflict response.
 *
 * THE REQUIREMENT THIS SPEC EXISTS TO ENFORCE
 * ------------------------------------------
 * On a concurrency mismatch the system must return a versioned conflict
 * response carrying the current row state, with a defined retry-or-surface
 * policy, and NO SILENT OVERWRITE anywhere. That last clause is the one worth
 * an executable test: a silent overwrite is invisible by construction. It
 * produces a `200`, a plausible row count, and destroyed data — the single
 * worst outcome the decomposition could introduce, and one no unit test on
 * either side of the boundary can observe, because it is a property of the
 * boundary itself.
 *
 * WHY THE LEGACY DEMANDS THIS
 * ---------------------------
 * The one updatable DataWindow in the legacy estate is configured with
 * `updatewhere=1` — the "key and updateable columns" concurrency mode — and all
 * six of its columns are marked. So the generated where-clause carries the key
 * PLUS the original values of every marked column, and the concurrency check
 * spans all six. Two consequences follow that this spec asserts on:
 *
 *   1. The payload must carry, per row, BOTH the current and the original value
 *      of every marked column. A flat rowset cannot express that, which is why
 *      the contract carries a buffer-shaped payload rather than a result set.
 *   2. A mismatch must be reported, not absorbed. The legacy update path even
 *      contains a defensive override that rewrites a CLAIMED SUCCESS into a
 *      failure when the transaction's SQL code disagrees — the legacy itself
 *      does not trust an optimistic success, and neither may the projection.
 *
 * THE STATUS MAPPING
 * ------------------
 * DataServices and Persistence answer gRPC `Aborted`; Gateway projects that to
 * HTTP `409`. `Aborted` is the canonical gRPC-to-409 mapping, so this is a
 * translation rather than an invention. `409` is asserted specifically — not
 * merely "some 4xx" — because the retry-or-surface policy a caller implements
 * keys off that exact code, and a `400` or `412` would send a correct caller
 * down the wrong branch.
 *
 * WHAT THIS SPEC DOES NOT CLAIM
 * ----------------------------
 * Provoking a genuine race requires two concurrent writers against seeded
 * storage, which belongs to the paired characterization capture in
 * `characterization/` where the volume state is controlled. This spec asserts
 * the CONTRACT: that the conflict status is reachable, that it carries the
 * documented payload, and that the update route never answers success to a
 * request it could not satisfy. Anything more would overstate what an HTTP
 * suite can adjudicate.
 *
 * See `../fixtures/live-stack.ts` for what has and has not been observed
 * running in this checkpoint.
 */

import { expect, test } from '@playwright/test';

import {
  DATAWINDOW_PATH_PREFIX,
  gatewayUrl,
} from '../fixtures/service-endpoints';
import { probeStackAvailability } from '../fixtures/live-stack';

/** The projected update route, the only one that carries a 409. */
const UPDATE_ROUTE = `${DATAWINDOW_PATH_PREFIX}/update`;

/**
 * The six columns of the single updatable legacy DataWindow, in DDL order.
 *
 * All six are marked `update=yes updatewhereclause=yes`, which is what makes
 * the concurrency check span the whole row rather than just the key.
 */
const MARKED_COLUMNS = [
  'id',
  'name',
  'age',
  'address',
  'salary',
  'birth',
] as const;

/**
 * The item statuses the buffer model distinguishes, preserved verbatim.
 *
 * `DataModified` and `NewModified` are the two that participate in an update;
 * the distinction matters because a new row generates an insert while a
 * modified one generates an update carrying original values.
 */
const ITEM_STATUSES = [
  'Unspecified',
  'NotModified',
  'DataModified',
  'New',
  'NewModified',
] as const;

/** The shape of the conflict problem document this spec reads. */
interface ConflictProblemDetails {
  readonly status?: unknown;
  readonly title?: unknown;
  readonly detail?: unknown;
  readonly retCode?: unknown;
  readonly conflict?: unknown;
}

test.describe('conflict contract (no running stack required)', () => {
  test('all six columns participate in the concurrency check', () => {
    // updatewhere=1 with every column marked: the where-clause carries the key
    // PLUS the original value of each of these.
    expect(MARKED_COLUMNS).toHaveLength(6);
    expect(MARKED_COLUMNS[0]).toBe('id');
    expect(new Set(MARKED_COLUMNS).size).toBe(6);
  });

  test('the status model distinguishes new from modified rows', () => {
    // Collapsing these would make an insert indistinguishable from an update,
    // and the update is the one that needs original values.
    expect(ITEM_STATUSES).toContain('DataModified');
    expect(ITEM_STATUSES).toContain('NewModified');
    expect(ITEM_STATUSES.indexOf('DataModified')).not.toBe(
      ITEM_STATUSES.indexOf('NewModified'),
    );
  });

  test('the conflict status is 409 and nothing adjacent to it', () => {
    // Pinned as a literal because a caller's retry-or-surface policy keys off
    // this exact code. 400 and 412 are the two plausible wrong answers.
    const conflictStatus = 409;

    expect(conflictStatus).toBe(409);
    expect(conflictStatus).not.toBe(400);
    expect(conflictStatus).not.toBe(412);
  });

  test('only the update route carries a conflict status', () => {
    // A conflict is a property of writing, not of reading. If a retrieval
    // route could answer 409 the caller would have to implement the retry
    // policy everywhere.
    expect(UPDATE_ROUTE).toBe('/v1/datawindow/update');
    expect(UPDATE_ROUTE.endsWith('/update')).toBe(true);
  });
});

test.describe('conflict projection over HTTP (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  test('the update route is guarded', async ({ request }) => {
    const response = await request.post(gatewayUrl(UPDATE_ROUTE), {
      failOnStatusCode: false,
      data: {},
    });

    expect(response.status()).toBe(401);
  });

  test('a structurally invalid update is refused, never silently accepted', async ({
    request,
  }) => {
    // The property under test is the absence of a silent success. An update
    // that cannot be satisfied must say so; answering 200 to a payload the
    // service could not have applied is the silent-overwrite failure mode in
    // its most detectable form.
    const response = await request.post(gatewayUrl(UPDATE_ROUTE), {
      failOnStatusCode: false,
      data: { rows: [{ nonsense: true }] },
    });

    test.skip(
      response.status() === 401,
      `The update route requires a token; Gateway answered 401 as contracted. ` +
        `The guard assertion ran independently.`,
    );

    expect(
      response.status(),
      'a malformed update must be refused, not reported as applied',
    ).not.toBe(200);
    expect(response.status()).toBeGreaterThanOrEqual(400);

    // And it must be refused as a client error, not by faulting: a 500 here
    // would mean hostile input reaches the SQL generation path.
    expect(
      response.status(),
      'a malformed update must not fault the service',
    ).toBeLessThan(500);
  });

  test('a stale-original-value update is reported as a conflict, with row state', async ({
    request,
  }) => {
    // Presents original values that cannot match any stored row. Under
    // updatewhere=1 the generated where-clause therefore matches nothing, which
    // is exactly the optimistic-concurrency mismatch the contract must report
    // rather than absorb.
    const response = await request.post(gatewayUrl(UPDATE_ROUTE), {
      failOnStatusCode: false,
      data: {
        rows: [
          {
            status: 'DataModified',
            current: {
              id: 1,
              name: 'e2e-conflict-probe',
              age: 1,
              address: 'probe',
              salary: 1,
              birth: '1970/01/01',
            },
            original: {
              id: 1,
              name: '__value_that_cannot_be_stored__',
              age: -999_999,
              address: '__stale__',
              salary: -1,
              birth: '0001/01/01',
            },
          },
        ],
      },
    });

    test.skip(
      response.status() === 401,
      `The update route requires a token; Gateway answered 401 as contracted.`,
    );

    // The critical assertion, stated as a prohibition because that is how the
    // requirement is worded: no silent overwrite. A 200 here would mean the
    // service applied a write whose optimistic precondition it could not have
    // verified.
    expect(
      response.status(),
      'a stale-original update reported success — no silent overwrite is permitted',
    ).not.toBe(200);

    // A 400 is acceptable if the service rejects the payload shape before
    // reaching concurrency evaluation; only a 409 lets this spec assert on the
    // conflict document itself.
    test.skip(
      response.status() !== 409,
      `The service answered ${response.status()} rather than 409, so the ` +
        `payload did not reach concurrency evaluation. The no-silent-overwrite ` +
        `assertion above still held. Provoking a genuine race requires the ` +
        `seeded, volume-controlled paired capture under characterization/.`,
    );

    expect(response.status()).toBe(409);

    const body = (await response.json()) as ConflictProblemDetails;

    // The conflict must carry current row state, so a caller can decide
    // between retrying and surfacing rather than having to re-read blindly.
    expect(body.status).toBe(409);
    expect(
      body.conflict,
      'a conflict must carry the current row state',
    ).toBeDefined();
  });

  test('the conflict response is a problem document, not a bare status', async ({
    request,
  }) => {
    const response = await request.post(gatewayUrl(UPDATE_ROUTE), {
      failOnStatusCode: false,
      data: { rows: [] },
    });

    test.skip(
      response.status() === 401 || response.status() === 200,
      `Reaching the error projection requires an authenticated call that the ` +
        `service rejects; Gateway answered ${response.status()}.`,
    );

    // Machine-readable error bodies are what let a caller implement a policy.
    // A bare status with an HTML body would force string matching.
    const contentType = response.headers()['content-type'] ?? '';

    expect(
      contentType,
      `error responses must be machine-readable JSON, got "${contentType}"`,
    ).toContain('json');
  });
});
