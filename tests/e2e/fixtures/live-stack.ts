/**
 * Live-stack detection for the cross-service specs.
 *
 * SCOPE NOTE - THIS MODULE NO LONGER ACQUIRES TOKENS
 * --------------------------------------------------
 * It once carried a second half that minted a service token, and that half was a
 * DUPLICATE: `./auth` owns token acquisition for this suite and is the path the
 * six canonical specs use. The duplicate existed only to serve an earlier,
 * superseded spec generation which has since been removed, so it was removed with
 * it rather than left as a second way to do one thing. What remains is the one
 * capability nothing else provides: telling an absent stack apart from a broken
 * one.
 *
 * WHY THIS MODULE EXISTS
 * ----------------------
 * The specs in `../specs` are cross-service workflow verification: they drive
 * real HTTP against a running four-service stack, exactly as the environment's
 * setup instructions describe (`npm ci && npx playwright test`, after
 * `docker compose ... up --build -d`). That makes them worth nothing unless
 * they can tell the difference between three outcomes that look alike from the
 * outside:
 *
 *   1. the stack is up and the contract holds            -> pass
 *   2. the stack is up and the contract is violated      -> fail
 *   3. the stack is not up at all                        -> neither
 *
 * Without an explicit third state, case 3 surfaces as a wall of
 * `ECONNREFUSED` failures that read exactly like case 2 — a false accusation
 * against services that are simply absent. Worse, the obvious "fix" for that
 * noise is to soften the assertions until they tolerate an unreachable host,
 * which converts case 2 into a silent pass and destroys the suite's entire
 * value.
 *
 * So case 3 is detected once, up front, and reported as an explicit SKIP whose
 * reason names what was probed and what to run. A skip is visible in every
 * reporter; a vacuous pass is not.
 *
 * WHAT IS AND IS NOT PROVABLE IN THIS CHECKPOINT
 * ---------------------------------------------
 * Stated plainly rather than implied, because overstating it here would
 * mislead every later reader: at the time these specs were authored the
 * repository contains no `orchestration/docker-compose.yml`, no service
 * `Dockerfile`, and no service `Program.cs`. The stack therefore cannot be
 * brought up yet, and these specs have NOT been observed passing against live
 * services. What has been verified is everything that does not need a stack —
 * that the suite type-checks under `tsc --noEmit`, that every spec is
 * discovered by `playwright test --list`, and that the pure-fixture
 * assertions (the capability table, the port map, the mask domains) execute
 * and pass. The HTTP assertions are authored against the published contract
 * in `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` and will run
 * the moment a stack exists.
 *
 * DESIGN NOTES
 * ------------
 * - The probe runs at most once per worker. Its result is memoised as a
 *   promise, so concurrent `beforeEach` hooks share one in-flight request
 *   rather than each issuing their own.
 * - The probe targets Gateway's anonymous `/health`. That is the one endpoint
 *   the contract guarantees needs no credentials (C-10), so a failure to reach
 *   it is unambiguously "no stack" rather than "no token".
 * - Nothing here throws at import time, and nothing here is evaluated at
 *   import time: module load has no side effect, so `playwright test --list`
 *   stays a pure collection step.
 */

import {
  request as playwrightRequest,
  type APIRequestContext,
} from '@playwright/test';

import {
  GATEWAY_BASE_URL,
  HEALTH_PATH,
  gatewayUrl,
} from './service-endpoints';

/**
 * How long the reachability probe waits before concluding "no stack".
 *
 * Short on purpose. This is a liveness question, not a workload: a stack that
 * is up answers its own anonymous health endpoint in milliseconds, and a stack
 * that is absent refuses the connection immediately. The only case this bound
 * affects is a host that accepts the connection and then stalls, and treating
 * that as "not usable for a cross-service workflow" is the correct reading.
 */
const PROBE_TIMEOUT_MS = 4_000;

/**
 * The outcome of the one-time stack reachability probe.
 *
 * A discriminated result rather than a bare boolean, so the skip reason can
 * quote what actually happened instead of asserting a guess about it.
 */
export interface StackAvailability {
  /** Whether Gateway's anonymous health endpoint answered at all. */
  readonly reachable: boolean;

  /**
   * A caller-facing explanation, suitable for use verbatim as a skip reason.
   * Always populated, for both outcomes, so a log line is never empty.
   */
  readonly reason: string;
}

/**
 * Memoised probe result.
 *
 * Deliberately a cached *promise* rather than a cached value: two hooks that
 * start concurrently must share one in-flight probe, which caching the
 * resolved value alone would not achieve.
 */
let cachedProbe: Promise<StackAvailability> | undefined;

/**
 * Describe a thrown value without assuming it is an `Error`.
 *
 * `useUnknownInCatchVariables` is enabled, and a rejected fetch can carry
 * anything. Narrowing explicitly keeps the reported reason truthful for every
 * shape rather than printing `[object Object]` for the awkward ones.
 *
 * @param cause the caught value, of unknown type
 * @returns a single-line human-readable description
 */
function describeCause(cause: unknown): string {
  if (cause instanceof Error) {
    return `${cause.name}: ${cause.message}`;
  }

  return `non-Error rejection: ${String(cause)}`;
}

/**
 * Probe Gateway's anonymous health endpoint exactly once per worker.
 *
 * Any answer at all — including an unhealthy `503` — counts as reachable,
 * because a `503` means Gateway is running and reporting on its upstreams,
 * which is a contract outcome the readiness spec is entitled to assert on. Only
 * a transport-level failure counts as absent.
 *
 * @returns the memoised availability result; never rejects
 */
export function probeStackAvailability(): Promise<StackAvailability> {
  cachedProbe ??= (async (): Promise<StackAvailability> => {
    let context: APIRequestContext | undefined;

    try {
      context = await playwrightRequest.newContext({
        baseURL: GATEWAY_BASE_URL,
        timeout: PROBE_TIMEOUT_MS,
      });

      const response = await context.get(HEALTH_PATH, {
        timeout: PROBE_TIMEOUT_MS,
        failOnStatusCode: false,
      });

      return {
        reachable: true,
        reason:
          `Gateway answered ${gatewayUrl(HEALTH_PATH)} with ` +
          `HTTP ${response.status()}.`,
      };
    } catch (cause: unknown) {
      return {
        reachable: false,
        reason:
          `No live stack: ${gatewayUrl(HEALTH_PATH)} did not answer within ` +
          `${PROBE_TIMEOUT_MS}ms (${describeCause(cause)}). Bring the four ` +
          `services up first — from the repository root: ` +
          `cd orchestration && cp .env.example .env && ` +
          `docker compose --env-file .env up --build -d — then re-run. ` +
          `The stack-independent assertions in this suite still ran.`,
      };
    } finally {
      await context?.dispose();
    }
  })();

  return cachedProbe;
}
