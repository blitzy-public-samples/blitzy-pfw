/**
 * Live-stack detection and service-token acquisition for the cross-service
 * specs.
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
  SECURITY_BASE_URL,
  TOKEN_PATH,
  gatewayUrl,
  securityUrl,
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

/**
 * Reset the memoised probe.
 *
 * Exists for the benefit of a spec that deliberately wants a fresh probe after
 * changing the environment. Not used by the current specs, and kept minimal
 * rather than omitted because the memo is otherwise unobservable and a future
 * reader would have no way to clear it.
 */
export function resetStackAvailabilityCache(): void {
  cachedProbe = undefined;
}

/**
 * A service token as issued by Security, the sole issuer.
 */
export interface IssuedToken {
  /** The bearer token, ready to place in an `Authorization` header. */
  readonly accessToken: string;

  /** The token type Security reported; the contract fixes this to `Bearer`. */
  readonly tokenType: string;
}

/**
 * The subset of Security's token response these specs read.
 *
 * Declared locally rather than imported because the OpenAPI document is not
 * code-generated into this project; the field names mirror
 * `security.v1.yaml`'s `TokenResponse` exactly, including its snake_case
 * spelling, which is the OAuth-style spelling the contract publishes.
 */
interface TokenResponseBody {
  readonly access_token?: unknown;
  readonly token_type?: unknown;
}

/**
 * Ask Security to mint a service token.
 *
 * Security is the system's only token minter (G7): no other service holds a
 * signing key, so every authenticated call in this suite ultimately depends on
 * this one request succeeding.
 *
 * Returns `undefined` rather than throwing when a token cannot be obtained, so
 * a caller can distinguish "Security declined to issue" from "the endpoint
 * under test rejected a valid token" — two failures that would otherwise
 * present identically. Note that `POST /v1/tokens` is declared as
 * mutual-TLS-protected in `security.v1.yaml`; a stack that enforces that will
 * decline this request, which is correct behaviour on its part and is reported
 * as such rather than treated as a defect.
 *
 * @param subject the caller identity to mint for
 * @param audience the intended audience, normally the service being called
 * @param scopes the scope set being requested
 * @returns the issued token, or `undefined` when Security did not issue one
 */
export async function issueServiceToken(
  subject: string,
  audience: string,
  scopes: readonly string[],
): Promise<IssuedToken | undefined> {
  let context: APIRequestContext | undefined;

  try {
    context = await playwrightRequest.newContext({
      baseURL: SECURITY_BASE_URL,
      timeout: PROBE_TIMEOUT_MS,
    });

    const response = await context.post(TOKEN_PATH, {
      failOnStatusCode: false,
      timeout: PROBE_TIMEOUT_MS,
      data: {
        subject,
        audience,
        scopes: [...scopes],
      },
    });

    if (!response.ok()) {
      return undefined;
    }

    const body = (await response.json()) as TokenResponseBody;
    const accessToken = body.access_token;
    const tokenType = body.token_type;

    if (typeof accessToken !== 'string' || accessToken.length === 0) {
      return undefined;
    }

    return {
      accessToken,
      tokenType: typeof tokenType === 'string' ? tokenType : '',
    };
  } catch {
    // A transport failure here is indistinguishable in effect from a refusal
    // to issue, and both mean "no token to test with". The caller reports it.
    return undefined;
  } finally {
    await context?.dispose();
  }
}

/**
 * Build the `Authorization` header value for an issued token.
 *
 * Uses the token type Security reported rather than hardcoding `Bearer`, so
 * that if a stack ever reports something else the resulting failure names the
 * real mismatch instead of hiding it behind a hardcoded assumption. Falls back
 * to `Bearer` only when the type is absent, which is what the contract's
 * `const: Bearer` says it must be anyway.
 *
 * @param token the token issued by Security
 * @returns the header value, e.g. `Bearer eyJ...`
 */
export function authorizationHeader(token: IssuedToken): string {
  const scheme = token.tokenType.length > 0 ? token.tokenType : 'Bearer';

  return `${scheme} ${token.accessToken}`;
}

/**
 * The Security token endpoint, for a spec that needs to report it.
 *
 * Re-exported so a skip reason can name the URL it tried without every spec
 * importing two more symbols to reassemble it.
 */
export function tokenEndpointUrl(): string {
  return securityUrl(TOKEN_PATH);
}
