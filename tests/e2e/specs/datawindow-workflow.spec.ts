/**
 * C-03 / C-04 projected over C-09 — the cross-service DataWindow workflow.
 *
 * WHY THIS IS THE WORKFLOW WORTH VERIFYING END TO END
 * --------------------------------------------------
 * This is the one path that traverses every service boundary the decomposition
 * created: an external caller reaches Gateway over REST, Gateway reaches
 * DataServices over gRPC, DataServices reaches Persistence over gRPC, and both
 * validate tokens issued by Security. A unit test can prove each hop in
 * isolation; only an end-to-end run proves the hops compose.
 *
 * Two behaviours here have no in-process analogue and are therefore the ones
 * most likely to be wrong:
 *
 *   1. THE VALIDATION SESSION. The legacy event chain keeps four pieces of
 *      mutable state BETWEEN events — most consequentially the item-changed
 *      return value stashed for the validation-error event to consume. A
 *      stateless request boundary has nowhere to put that, so the contract
 *      materialises it as an explicitly opened and closed server-held session
 *      correlated by id. If session lifecycle is broken, the item-change
 *      protocol silently loses its history.
 *
 *   2. THE EXPRESSION SESSION. Cross-DataWindow variables are resolved by a
 *      session-scoped handle, because the legacy holds a live object pointer
 *      that cannot be serialized. References spanning sessions are BLOCKED
 *      with a defined error rather than silently answered wrongly — a
 *      deliberate, documented narrowing of the legacy contract.
 *
 * WHAT THIS SPEC ASSERTS VERSUS WHAT IT CANNOT
 * -------------------------------------------
 * It asserts the projection SHAPE and the session LIFECYCLE, which are
 * properties of the boundary. It does not attempt to assert legacy-parity of
 * expression VALUES: that is a characterization concern requiring paired
 * recordings against the PowerBuilder oracle, which lives in
 * `characterization/` and is explicitly not something an HTTP suite can
 * adjudicate. Claiming otherwise here would overstate the evidence.
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

/**
 * The projected routes this spec drives, as declared in `gateway.v1.yaml`.
 *
 * Spelled out rather than derived so that a route rename shows up here as a
 * failing assertion instead of being silently followed.
 */
const ROUTES = {
  validationSessions: `${DATAWINDOW_PATH_PREFIX}/sessions`,
  expressionSessions: `${DATAWINDOW_PATH_PREFIX}/expression/sessions`,
  eventGate: `${DATAWINDOW_PATH_PREFIX}/event-gate`,
  eventGateDisable: `${DATAWINDOW_PATH_PREFIX}/event-gate/disable`,
  eventGateEnable: `${DATAWINDOW_PATH_PREFIX}/event-gate/enable`,
  update: `${DATAWINDOW_PATH_PREFIX}/update`,
  addForeignVariable: `${DATAWINDOW_PATH_PREFIX}/expression/foreign-variables/add`,
} as const;

/**
 * The event-gate bit values, preserved verbatim from the legacy declaration.
 *
 * Composable bits, not an enumeration. The coupling noted inline in the legacy
 * source — disabling item-change ALSO suppresses column-expression evaluation —
 * is a documented behaviour of the gate rather than of these values.
 */
const EVENT_GATE = {
  rowFocusChange: 1,
  itemFocusChange: 2,
  itemChange: 4,
} as const;

/** A response body carrying a session identifier. */
interface SessionBody {
  readonly sessionId?: unknown;
}

test.describe('DataWindow projection surface (no running stack required)', () => {
  test('every projected route sits under the single versioned prefix', () => {
    expect(DATAWINDOW_PATH_PREFIX).toBe('/v1/datawindow');

    for (const route of Object.values(ROUTES)) {
      expect(route.startsWith(`${DATAWINDOW_PATH_PREFIX}/`)).toBe(true);
    }
  });

  test('the two session families are distinct routes', () => {
    // A validation session and an expression session hold different state and
    // have different lifetimes; sharing one route would conflate them.
    expect(ROUTES.validationSessions).not.toBe(ROUTES.expressionSessions);
    expect(
      ROUTES.expressionSessions.startsWith(`${ROUTES.validationSessions}/`),
    ).toBe(false);
  });

  test('the event-gate bits are the legacy composable values', () => {
    expect(EVENT_GATE.rowFocusChange).toBe(1);
    expect(EVENT_GATE.itemFocusChange).toBe(2);
    expect(EVENT_GATE.itemChange).toBe(4);

    // Composable: each bit is independent, so a combination names a set rather
    // than a fourth state.
    const all =
      EVENT_GATE.rowFocusChange |
      EVENT_GATE.itemFocusChange |
      EVENT_GATE.itemChange;

    expect(all).toBe(7);
    expect(all & EVENT_GATE.itemChange).toBe(EVENT_GATE.itemChange);
  });
});

test.describe('DataWindow workflow over HTTP (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  test('every projected DataWindow route is guarded', async ({ request }) => {
    // The projection is not a public surface. Each route must answer 401
    // without a token, including the read-only gate query.
    const gateResponse = await request.get(gatewayUrl(ROUTES.eventGate), {
      failOnStatusCode: false,
    });
    expect(gateResponse.status()).toBe(401);

    for (const route of [
      ROUTES.validationSessions,
      ROUTES.expressionSessions,
      ROUTES.update,
      ROUTES.eventGateDisable,
      ROUTES.eventGateEnable,
    ]) {
      const response = await request.post(gatewayUrl(route), {
        failOnStatusCode: false,
        data: {},
      });

      expect(response.status(), `${route} must require a token`).toBe(401);
    }
  });

  test('a validation session opens, is addressable, and closes', async ({
    request,
  }) => {
    const opened = await request.post(gatewayUrl(ROUTES.validationSessions), {
      failOnStatusCode: false,
      data: {},
    });

    test.skip(
      opened.status() === 401,
      `Opening a validation session requires a token; Gateway answered 401 as ` +
        `contracted. The guard assertions ran independently.`,
    );

    // The session is the mechanism that carries the four pieces of cross-event
    // state. If it cannot be opened, the item-change protocol has nowhere to
    // keep its history.
    expect(opened.status()).toBe(200);

    const body = (await opened.json()) as SessionBody;
    const sessionId = body.sessionId;

    expect(
      typeof sessionId,
      'an opened session must return a correlatable identifier',
    ).toBe('string');
    expect(String(sessionId).length).toBeGreaterThan(0);

    // Closing is an explicit call, not a timeout: the legacy pairing is
    // mandatory, and an unclosed session leaks the state it holds.
    const closed = await request.delete(
      gatewayUrl(`${ROUTES.validationSessions}/${String(sessionId)}`),
      { failOnStatusCode: false },
    );

    expect([200, 204]).toContain(closed.status());
  });

  test('closing an unknown validation session is refused, not silently accepted', async ({
    request,
  }) => {
    const response = await request.delete(
      gatewayUrl(`${ROUTES.validationSessions}/session-that-never-existed`),
      { failOnStatusCode: false },
    );

    test.skip(
      response.status() === 401,
      `An authenticated call is required to distinguish an unknown session ` +
        `from an unauthenticated one.`,
    );

    // Accepting this would mean the caller cannot tell whether the state it
    // thought it had was ever there.
    expect(response.status()).toBeGreaterThanOrEqual(400);
    expect([404, 400]).toContain(response.status());
  });

  test('the event gate reports the composable bitmask', async ({ request }) => {
    const response = await request.get(gatewayUrl(ROUTES.eventGate), {
      failOnStatusCode: false,
    });

    test.skip(
      response.status() !== 200,
      `Reading the event gate requires a token; Gateway answered ` +
        `${response.status()}.`,
    );

    const body = (await response.json()) as Record<string, unknown>;

    // The gate is a mask, not a set of booleans: the legacy encodes it as
    // composable bits and the coupling between item-change and expression
    // evaluation depends on that representation.
    const numeric = Object.values(body).filter(
      (value) => typeof value === 'number',
    );

    expect(
      numeric.length,
      'the event-gate report must carry at least one numeric mask',
    ).toBeGreaterThan(0);
  });

  test('an expression session scopes foreign variables, and cross-session use is blocked', async ({
    request,
  }) => {
    const opened = await request.post(gatewayUrl(ROUTES.expressionSessions), {
      failOnStatusCode: false,
      data: {},
    });

    test.skip(
      opened.status() === 401,
      `Opening an expression session requires a token; Gateway answered 401 ` +
        `as contracted.`,
    );

    expect(opened.status()).toBe(200);

    const body = (await opened.json()) as SessionBody;
    const sessionId = body.sessionId;
    expect(typeof sessionId).toBe('string');

    // The documented narrowing: a foreign reference naming a session other
    // than this one must be REFUSED with a defined error, never answered with
    // a guess. Approximating a pointer dereference across a network would
    // produce results wrong in a way no assertion could catch.
    const crossSession = await request.post(
      gatewayUrl(ROUTES.addForeignVariable),
      {
        failOnStatusCode: false,
        data: {
          sessionId,
          name: 'foreign',
          foreignSessionId: 'a-different-session-entirely',
        },
      },
    );

    expect(
      crossSession.status(),
      'a cross-session foreign reference must be refused, not answered',
    ).toBeGreaterThanOrEqual(400);
    expect([400, 404, 409, 422, 501]).toContain(crossSession.status());

    await request.delete(
      gatewayUrl(`${ROUTES.expressionSessions}/${String(sessionId)}`),
      { failOnStatusCode: false },
    );
  });
});
