/**
 * Fail-closed global setup for the cross-service end-to-end suite.
 *
 * WHY THIS FILE EXISTS
 * --------------------
 * Every spec in `./specs` used to begin by probing Gateway and calling
 * `test.skip` when the probe did not answer. That was written to keep three
 * outcomes distinguishable — the contract holds, the contract is violated, and
 * the stack is not up — and the third state is a genuine requirement. But it was
 * wired the wrong way round: **absence was the default and running was the
 * exception**, so a plain `npx playwright test` — the command the environment's
 * setup instructions document — exited 0 with every live assertion skipped, and
 * a green run said nothing at all about the topology.
 *
 * Worse, the probe converted a TLS failure into the same `reachable: false` as a
 * refused connection. Every listener in this estate is `https` and a Compose
 * bring-up presents a certificate from a throwaway private authority, so the
 * single most likely local misconfiguration — the runner not trusting that
 * authority — was indistinguishable from "no stack", and it would have SKIPPED
 * THE ENTIRE SUITE while the stack was up and serving. That is the shape of
 * defect that masks a deployment finding rather than reporting it.
 *
 * SO THE DEFAULT IS INVERTED, AND THAT IS THE WHOLE FIX
 * ----------------------------------------------------
 * A full-topology run is now the default and it FAILS when the topology is not
 * there. Skipping is available, but only when an operator asks for it by name:
 *
 *   npx playwright test                  # strict. All four services must answer.
 *   E2E_ALLOW_ABSENT_STACK=1 \
 *     npx playwright test                # partial. Stack-free assertions only.
 *
 * The opt-in is an environment variable rather than a config flag because the
 * decision belongs to the person running the command, not to the checked-in
 * configuration — a file that permitted skipping would permit it for everybody,
 * including CI, which is exactly the state this replaces.
 *
 * WHAT IT PROBES, AND WHY ALL FOUR RATHER THAN ONLY GATEWAY
 * --------------------------------------------------------
 * All four services' anonymous `/health`. Gateway alone is not sufficient: it
 * aggregates its three upstreams, so its own `200` already implies they answered
 * — but its `503` does NOT tell an operator which one is responsible, and the
 * suite's own readiness spec asserts each upstream directly. Probing all four
 * here means the setup failure names the offending service instead of leaving
 * that to be rediscovered from a wall of downstream symptoms.
 *
 * A `503` is NOT a setup failure. It means the service is running and reporting
 * on itself, which is a contract outcome spec 01 is entitled to assert on — and
 * on a fresh Persistence volume it is the documented answer until the migration
 * has been applied once. Only a TRANSPORT failure means "not there".
 *
 * HOW A TRUST FAILURE IS REPORTED, WHICH IS THE POINT OF SEPARATING IT
 * -------------------------------------------------------------------
 * A certificate fault is classified and reported as its own condition, with the
 * remedy for the bring-up that produced it. `ignoreHTTPSErrors` stays `false` in
 * the config, deliberately: an untrusted certificate is a real finding about the
 * stack, and suppressing it here to get a green run would be the same mistake in
 * a different place.
 *
 * Node reads `NODE_EXTRA_CA_CERTS` **once, at process start**, so this file
 * cannot inject trust into a run that is already going — it can only detect the
 * gap and name the exact export that closes it. That is stated rather than
 * worked around, because a file that appeared to install a CA and did not would
 * be worse than one that refuses.
 *
 * IT STARTS NOTHING AND WRITES NOTHING. No container is brought up, no file is
 * written, no credential is read and no token is minted: bringing the stack up
 * belongs to the orchestration path, and token acquisition belongs to
 * `fixtures/auth.ts`. This file answers one question — is the topology there —
 * and either proceeds or refuses.
 */

import { request as playwrightRequest, type APIRequestContext } from '@playwright/test';

import {
  ABSENT_STACK_ACKNOWLEDGED,
  ABSENT_STACK_ACKNOWLEDGEMENT_VALUES,
  ABSENT_STACK_VALUE_UNRECOGNISED,
  ABSENT_STACK_VARIABLE,
} from './fixtures/run-mode';
import { ALL_SERVICE_KEYS, SERVICE_ENDPOINTS } from './fixtures/service-endpoints';

/**
 * The run mode is NOT declared here.
 *
 * `fixtures/run-mode.ts` owns the variable name, the accepted values and the two
 * booleans derived from them, and it imports nothing — no runner, no fixture, no
 * filesystem — precisely so that this hook, `fixtures/live-stack.ts` and
 * `playwright.config.ts` can all read one answer. An earlier revision of this file
 * carried its own copy of that variable, its own accepted-value list and its own
 * predicate; two declarations of one decision is how a suite comes to refuse a run
 * in one place and permit it in another, so the copy is gone and the import below is
 * the only source.
 *
 * This hook and `live-stack.ts` still both apply the decision, and that is not
 * duplication: this one asks once for the whole run and refuses before any test
 * executes, while that one is the per-worker backstop for a service that dies
 * mid-run.
 */

/** How long each service's health probe waits before it counts as absent. */
const PROBE_TIMEOUT_MS = 8_000;

/**
 * The fault classes this setup distinguishes.
 *
 * Three rather than two, because the remedy differs: a refused connection needs
 * a bring-up, an untrusted certificate needs a trust anchor, and a stall needs
 * neither and is reported as itself.
 */
type FaultKind = 'unreachable' | 'untrusted' | 'timeout';

/** One service's probe outcome. */
interface ProbeOutcome {
  readonly serviceKey: string;
  readonly displayName: string;
  readonly url: string;
  readonly status?: number;
  readonly fault?: FaultKind;
  readonly detail?: string;
}

/**
 * The certificate-verification failures Node reports by code or by message.
 *
 * Matched on both, because the surface differs by how the request was made:
 * Playwright's request context surfaces the underlying message, and a raw Node
 * TLS socket surfaces the code. Matching either is what keeps a trust failure
 * from being misfiled as an absent stack.
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
 * Classify a thrown probe fault.
 *
 * @param cause the caught value, of unknown type
 * @returns the fault class and a single-line detail
 */
function classify(cause: unknown): { readonly fault: FaultKind; readonly detail: string } {
  const detail: string =
    cause instanceof Error ? `${cause.name}: ${cause.message}` : `non-Error rejection: ${String(cause)}`;

  if (TRUST_FAULT_TOKENS.some((token: string) => detail.includes(token))) {
    return { fault: 'untrusted', detail };
  }

  if (detail.includes('imeout') || detail.includes('ETIMEDOUT')) {
    return { fault: 'timeout', detail };
  }

  return { fault: 'unreachable', detail };
}

/**
 * Probe one service's anonymous health endpoint.
 *
 * @param serviceKey the endpoint-table key
 * @returns the outcome; never rejects
 */
async function probe(serviceKey: (typeof ALL_SERVICE_KEYS)[number]): Promise<ProbeOutcome> {
  const endpoint = SERVICE_ENDPOINTS[serviceKey];
  let context: APIRequestContext | undefined;

  try {
    context = await playwrightRequest.newContext({ timeout: PROBE_TIMEOUT_MS });

    const response = await context.get(endpoint.healthUrl, {
      timeout: PROBE_TIMEOUT_MS,
      failOnStatusCode: false,
    });

    return {
      serviceKey,
      displayName: endpoint.displayName,
      url: endpoint.healthUrl,
      status: response.status(),
    };
  } catch (cause: unknown) {
    const { fault, detail } = classify(cause);

    return {
      serviceKey,
      displayName: endpoint.displayName,
      url: endpoint.healthUrl,
      fault,
      detail,
    };
  } finally {
    await context?.dispose();
  }
}

/** The bring-up remedy, quoted verbatim in a refusal so no operator has to look it up. */
const BRING_UP_REMEDY: string =
  'Bring the four services up first — from the repository root:\n' +
  '    cd orchestration && cp .env.example .env   # then fill in the required values\n' +
  '    docker compose --env-file .env up --build -d\n' +
  '  Readiness: docker compose ps shows all four healthy, and curl -sf ' +
  'https://localhost:5105/health answers 200.';

/** The trust remedy, which differs by bring-up and says so. */
const TRUST_REMEDY: string =
  'Trust the authority that issued the certificate the listeners present.\n' +
  '    docker compose bring-up: export ' +
  'NODE_EXTRA_CA_CERTS="$INTERNAL_TLS_CA_PATH"\n' +
  '    host dotnet run:        dotnet dev-certs https --trust\n' +
  '  Node reads NODE_EXTRA_CA_CERTS only at process start, so export it BEFORE ' +
  'invoking the runner.\n' +
  '  ignoreHTTPSErrors stays false on purpose: an untrusted certificate is a ' +
  'finding about the stack, not noise.';

/**
 * Refuse the run, or permit it in the partial mode, based on what answered.
 *
 * @throws Error when the topology is incomplete and the partial mode was not requested
 */
export default async function globalSetup(): Promise<void> {
  // CHECKED FIRST, BECAUSE IT IS A MISTAKE IN EITHER MODE. A mistyped opt-out
  // would otherwise produce the strict refusal below and read as "the opt-out does
  // not work", sending an operator to look for a different mechanism. The
  // configured value is deliberately not quoted back.
  if (ABSENT_STACK_VALUE_UNRECOGNISED) {
    throw new Error(
      `${ABSENT_STACK_VARIABLE} is set to a value this suite does not recognise. Accepted ` +
        `values are ${ABSENT_STACK_ACKNOWLEDGEMENT_VALUES.join(' and ')}, matched case-insensitively. Unset it ` +
        'for a strict full-topology run, or set it to one of those values to acknowledge a ' +
        'deliberately partial one. The same two values and the same rule govern ' +
        'E2E_ALLOW_MISSING_ISSUANCE_IDENTITY, which is the credential half of the same question.',
    );
  }

  const outcomes: readonly ProbeOutcome[] = await Promise.all(
    ALL_SERVICE_KEYS.map((key: (typeof ALL_SERVICE_KEYS)[number]) => probe(key)),
  );

  const faulted: readonly ProbeOutcome[] = outcomes.filter(
    (outcome: ProbeOutcome) => outcome.fault !== undefined,
  );

  if (faulted.length === 0) {
    const roster: string = outcomes
      .map((outcome: ProbeOutcome) => `${outcome.displayName} ${outcome.status}`)
      .join(', ');

    // eslint-disable-next-line no-console
    console.log(`[e2e] Full topology reachable — ${roster}. Running in STRICT mode.`);

    return;
  }

  const report: string = faulted
    .map(
      (outcome: ProbeOutcome) =>
        `  - ${outcome.displayName} at ${outcome.url}: ${outcome.fault} (${outcome.detail ?? 'no detail'})`,
    )
    .join('\n');

  if (ABSENT_STACK_ACKNOWLEDGED) {
    // eslint-disable-next-line no-console
    console.warn(
      `[e2e] ${ABSENT_STACK_VARIABLE} is set, so the run continues in PARTIAL mode and every ` +
        `live assertion will SKIP. This run proves nothing about the topology.\n${report}`,
    );

    return;
  }

  const untrusted: boolean = faulted.some((outcome: ProbeOutcome) => outcome.fault === 'untrusted');

  throw new Error(
    'The end-to-end suite runs against a live four-service stack and ' +
      `${faulted.length} of ${outcomes.length} services did not answer:\n${report}\n\n` +
      (untrusted ? `${TRUST_REMEDY}\n\n` : `${BRING_UP_REMEDY}\n\n`) +
      `This is a REFUSAL rather than a skip, deliberately: a run that skipped here would exit 0 ` +
      `having proved nothing about the topology, and it would hide a stack that is up but ` +
      `untrusted. To run only the assertions that need no stack, ask for it by name:\n` +
      `    ${ABSENT_STACK_VARIABLE}=1 npx playwright test`,
  );
}
