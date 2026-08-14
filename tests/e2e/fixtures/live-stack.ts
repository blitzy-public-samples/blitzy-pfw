/**
 * Live-stack detection for the cross-service specs.
 *
 * SCOPE NOTE - THIS MODULE ACQUIRES NO TOKENS
 * -------------------------------------------
 * It deliberately does NOT mint a service token. `./auth` owns token acquisition
 * for this suite and is the path all six specs use, so a second minting half here
 * would be a duplicate - a second way to do one thing, and one of the two free to
 * drift. This module carries the one capability nothing else provides: telling an
 * absent stack apart from a broken one.
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
 * ⚠ CASE 3 FAILS A FULL ACCEPTANCE RUN. IT USED TO SKIP ONE. ⚠
 * -----------------------------------------------------------
 * An earlier form of this module detected case 3 and reported it as an explicit
 * SKIP, on the reasoning that a skip is visible in every reporter while a vacuous
 * pass is not. That reasoning is right about skips and wrong about acceptance: a
 * skipped test is not a passed test, but a RUN whose every HTTP assertion skipped
 * still exits zero, and an exit code is what a pipeline reads. The one state a
 * misconfigured acceptance pipeline is in — nothing running — was therefore the
 * state that reported success. The suite's entire value is cross-service
 * verification, and a run that verified none of it must not be reportable as
 * having verified it.
 *
 * So there are two modes and no third, exactly as `token-issuance.ts` already
 * treats the other precondition this suite has:
 *
 * - **A FULL ACCEPTANCE RUN — the default — FAILS.** {@link requireLiveStack}
 *   throws from `beforeEach`, which fails the test rather than skipping it, and the
 *   message names what was probed, the bring-up command, and how to ask for a
 *   partial run instead.
 * - **A RUN THAT EXPLICITLY ACKNOWLEDGES AN ABSENT STACK** — one environment
 *   variable, `E2E_ALLOW_ABSENT_STACK`, normally set by `npm run test:partial` —
 *   **skips the stack-dependent tests with a stated reason**, runs everything else,
 *   and is labelled `api-partial-no-stack` in every reported line so its result
 *   cannot be read as an acceptance result.
 *
 * The acknowledgement is an opt-in and is deliberately NOT inferred from the stack
 * being absent. Inferring it is the design that produces the silent partial run.
 *
 * A TLS FAULT IS NOT AN ABSENT STACK, AND CONFLATING THEM HID A REAL FINDING
 * ------------------------------------------------------------------------
 * The earlier form also swallowed a trust failure into the same `reachable: false`
 * as a refused connection. Every listener in this estate is `https`, and a Compose
 * bring-up presents a certificate from a throwaway private authority — so the
 * single most likely local misconfiguration, the runner not trusting that
 * authority, was indistinguishable from "no stack" and would have skipped the whole
 * suite while the stack was up and serving. A suite that hides a deployment finding
 * is worse than one that has none. The probe below therefore classifies the fault
 * and quotes the remedy for the class it found, and `ignoreHTTPSErrors` stays
 * `false` on purpose.
 *
 * TWO GATES, ONE DECLARATION OF THE DECISION
 * -----------------------------------------
 * `../global-setup.ts` asks the same question ONCE for the whole run and refuses
 * before any test executes; this module is the per-worker backstop for a service
 * that dies mid-run. Both read the run mode from `./run-mode`, which is the single
 * place the variable name and the accepted values are declared, so the two cannot
 * disagree about what enables the partial mode.
 *
 * WHAT IS AND IS NOT PROVABLE IN THIS CHECKPOINT
 * ---------------------------------------------
 * Stated plainly rather than implied, because overstating it here would mislead
 * every later reader. The bring-up path this module names is real and has been
 * exercised — `orchestration/README.md` §10 is the single place this repository
 * reports execution status, and it records all four services reaching Docker health
 * `healthy` — but that same section records that **this suite has not been run
 * against a live stack**. What HAS been verified is everything that does not need
 * one: the suite type-checks under `tsc --noEmit`, every spec is discovered by
 * `playwright test --list`, and the pure-fixture assertions (the capability table,
 * the port map, the mask domains) execute and pass. The HTTP assertions are authored
 * against the published contract in
 * `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml`.
 *
 * That gap is exactly why the default mode below fails rather than skips: an
 * unexercised suite is a risk to report, and a suite that reported success without
 * having run against a stack would be hiding it.
 *
 * DESIGN NOTES
 * ------------
 * - The probe runs at most once per worker. Its result is memoised as a
 *   promise, so concurrent `beforeEach` hooks share one in-flight request
 *   rather than each issuing their own.
 * - The probe targets Gateway's anonymous `/health`. That is the one endpoint
 *   the contract guarantees needs no credentials (C-10), so a failure to reach
 *   it is never "no token". Whether it is "no stack" or "no trust" is decided by
 *   classifying the fault rather than by assuming, because the remedies differ.
 * - A `503` still counts as reachable. It means Gateway is running and reporting
 *   on its upstreams, which is a contract outcome spec 01 asserts on.
 * - Nothing here throws at import time, and nothing here is evaluated at
 *   import time: module load has no side effect, so `playwright test --list`
 *   stays a pure collection step.
 */

import {
  request as playwrightRequest,
  test,
  type APIRequestContext,
  type TestInfo,
} from '@playwright/test';

import {
  ABSENT_STACK_ACKNOWLEDGED,
  ABSENT_STACK_VARIABLE,
  ABSENT_STACK_VALUE_UNRECOGNISED,
  BRING_UP_COMMAND,
  PARTIAL_RUN_COMMAND,
} from './run-mode';
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
 * The certificate-verification failures that mean "the stack is up and this
 * runner does not trust it".
 *
 * Kept here as well as in `../global-setup.ts` because the two run in different
 * processes and neither may depend on the other's classification having happened
 * first. Both lists exist for the same reason: a trust fault reported as an
 * absent stack sends an operator to bring up a stack that is already running.
 */
const TRUST_FAULT_TOKENS: readonly string[] = Object.freeze([
  'UNABLE_TO_VERIFY_LEAF_SIGNATURE',
  'SELF_SIGNED_CERT_IN_CHAIN',
  'DEPTH_ZERO_SELF_SIGNED_CERT',
  'UNABLE_TO_GET_ISSUER_CERT',
  'CERT_HAS_EXPIRED',
  'ERR_TLS_CERT_ALTNAME_INVALID',
  'self-signed certificate',
  'self signed certificate',
  'unable to verify the first certificate',
  'certificate has expired',
  'Hostname/IP does not match certificate',
]);

/**
 * Whether a described fault is a trust failure rather than an absence.
 *
 * @param described the single-line description produced by {@link describeCause}
 * @returns true when the fault is certificate verification
 */
function isTrustFault(described: string): boolean {
  return TRUST_FAULT_TOKENS.some((token: string) => described.includes(token));
}

/**
 * Probe Gateway's anonymous health endpoint exactly once per worker.
 *
 * Any answer at all — including an unhealthy `503` — counts as reachable,
 * because a `503` means Gateway is running and reporting on its upstreams,
 * which is a contract outcome the readiness spec is entitled to assert on. Only
 * a transport-level failure counts as absent.
 *
 * 🔴 IN THE DEFAULT (STRICT) MODE THIS REJECTS RATHER THAN RETURNING
 * `reachable: false`. A caller that turns the result into `test.skip` therefore
 * cannot silently skip a run nobody asked to have skipped: the rejection carries
 * the fault class and its remedy, and the `test.skip` line becomes a no-op that
 * is only reached in the partial mode. `../global-setup.ts` has already refused
 * the whole run in that case, so reaching this rejection means a service died
 * mid-run — which is a failure, not an absence.
 *
 * @returns the memoised availability result
 * @throws Error in the strict mode when the probe faults
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
      const described: string = describeCause(cause);

      const remedy: string = isTrustFault(described)
        ? `The stack appears to be UP and this runner does not trust the ` +
          `certificate it presents. Trust the issuing authority before running: ` +
          `for a docker compose bring-up export ` +
          `NODE_EXTRA_CA_CERTS="$INTERNAL_TLS_CA_PATH", and for a host ` +
          `dotnet run use dotnet dev-certs https --trust. Node reads that variable ` +
          `only at process start, so export it BEFORE invoking the runner. ` +
          `ignoreHTTPSErrors stays false on purpose.`
        : `Bring the four services up first — from the repository root: ` +
          `${BRING_UP_COMMAND} — then re-run.`;

      const reason: string =
        `${gatewayUrl(HEALTH_PATH)} did not answer within ${PROBE_TIMEOUT_MS}ms ` +
        `(${described}). ${remedy}`;

      // STRICT BY DEFAULT. Throwing rather than reporting an absence is what stops
      // a caller's `test.skip` from converting an unasked-for absence into a
      // vacuous pass. The partial mode is opt-in and is named in the message.
      if (!ABSENT_STACK_ACKNOWLEDGED) {
        throw new Error(
          `${reason} This is a FAILURE rather than a skip: the suite verifies a ` +
            `live four-service topology, and a run that skipped here would exit 0 ` +
            `having proved nothing. To run only the assertions that need no stack, ` +
            `ask for it by name: ${PARTIAL_RUN_COMMAND}, which sets ` +
            `${ABSENT_STACK_VARIABLE}.`,
        );
      }

      return {
        reachable: false,
        reason:
          `No live stack (${ABSENT_STACK_VARIABLE} is set, so this is a skip ` +
          `rather than a failure): ${reason} The stack-independent assertions in ` +
          `this suite still ran.`,
      };
    } finally {
      await context?.dispose();
    }
  })();

  return cachedProbe;
}

/**
 * The stack precondition, applied once per test from every spec's `beforeEach`.
 *
 * ONE FUNCTION FOR ALL SIX SPECS, and that consolidation is part of the fix rather
 * than tidying beside it. The probe-and-decide block was written out six times, once
 * per spec, so the six could disagree about what an absent stack means — and while
 * the behaviour was a skip that mattered only in degree, now that it is a failure it
 * is the difference between a spec that gates acceptance and one that does not.
 *
 * WHAT EACH BRANCH IS FOR
 * -----------------------
 * - `@no-stack`-TAGGED TESTS RETURN IMMEDIATELY. Several specs mix pure-fixture
 *   assertions in with HTTP ones — the capability table, the port map, the mask
 *   domains — and those are exactly the part that still holds with nothing running.
 *   A tag is declarative and machine-read; a title substring would silently stop
 *   matching the moment somebody reworded a test name.
 * - AN UNRECOGNISED ACKNOWLEDGEMENT VALUE IS A CONFIGURATION ERROR, checked before
 *   the probe because it is a mistake in either mode: a value of `ture` would
 *   otherwise mean "full acceptance run" and produce the failure the author was
 *   trying to opt out of.
 * - A REACHABLE STACK RETURNS. Any answer counts, including an unhealthy `503`:
 *   that means Gateway is running and reporting on its upstreams, which is a
 *   contract outcome the readiness spec is entitled to assert on.
 * - AN ABSENT STACK THROWS on a full acceptance run, and skips only on an
 *   acknowledged partial one.
 *
 * @param testInfo the current test's info, read for its tags
 * @throws Error when the acknowledgement variable carries an unrecognised value, or
 *         when the stack is absent and the run has not acknowledged that
 */
export async function requireLiveStack(testInfo: TestInfo): Promise<void> {
  if (testInfo.tags.includes('@no-stack')) {
    return;
  }

  if (ABSENT_STACK_VALUE_UNRECOGNISED) {
    throw new Error(
      `${ABSENT_STACK_VARIABLE} is set to a value this suite does not ` +
        'recognise. Accepted values are 1 and true, matched case-insensitively. ' +
        'Unset it for a full acceptance run, or set it to one of those values to ' +
        'acknowledge a run that tolerates an absent stack. (The configured value ' +
        'is deliberately not quoted here.)',
    );
  }

  const availability: StackAvailability = await probeStackAvailability();

  if (availability.reachable) {
    return;
  }

  if (!ABSENT_STACK_ACKNOWLEDGED) {
    throw new Error(
      `${availability.reason} A full acceptance run FAILS here rather than ` +
        'skipping, because this suite exists to verify cross-service workflows ' +
        'and a run that verified none of them must not exit zero. Bring the ' +
        'stack up and re-run, or run the acknowledged partial suite instead: ' +
        `${PARTIAL_RUN_COMMAND} (which sets ${ABSENT_STACK_VARIABLE} and is ` +
        'labelled api-partial-no-stack in every reported line).',
    );
  }

  test.skip(
    true,
    `${availability.reason} Skipped rather than failed because this run set ` +
      `${ABSENT_STACK_VARIABLE} and is therefore a DELIBERATELY PARTIAL run, ` +
      'not an acceptance run: it exercised no cross-service workflow. Its ' +
      'reported lines carry the api-partial-no-stack project label for exactly ' +
      'that reason.',
  );
}
