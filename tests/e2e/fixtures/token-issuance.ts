/**
 * The token-issuance precondition — one place that decides what a run does when
 * no mutual-TLS client identity has been provisioned.
 *
 * WHY A PRECONDITION RATHER THAN A PER-SPEC CONVENTION
 * ----------------------------------------------------
 * `POST /v1/tokens` on Security is protected by **mutual TLS and by nothing
 * else**, because a caller cannot present a bearer token in order to obtain its
 * first bearer token. Security declares exactly one listener — `https://+:5104`
 * with `ClientCertificateMode` `AllowCertificate` — in its **base** settings
 * file, so the requirement holds in Development too: the handshake completes
 * without a certificate, and the token operation then refuses the caller per
 * operation. There is therefore **no address, local or deployed, at which a
 * token is minted without a client certificate.**
 *
 * Every authenticated workflow in this suite needs a token, so without an
 * identity every one of them is unrunnable. The failure mode this module exists
 * to prevent is the quiet one: a run that omits all of them and still reports
 * green, or — just as bad — a run whose fifteen separate token calls each fail
 * with their own transport error, none of which says *the identity was never
 * provisioned*. Both are states in which "the suite passed" and "the suite
 * proved something about authentication" have come apart, and neither is
 * detectable from the summary line.
 *
 * TWO MODES, AND THERE IS NO THIRD
 * --------------------------------
 * - **A FULL ACCEPTANCE RUN — the default — FAILS ITS SETUP.** `beforeAll`
 *   throws, which fails the group rather than each assertion inside it, so the
 *   report says once and unambiguously that the run was not a valid acceptance
 *   run. The message names the two variables and the command that writes them.
 * - **A RUN THAT EXPLICITLY ACKNOWLEDGES BEING PARTIAL** — one environment
 *   variable, {@link PARTIAL_RUN_VARIABLE} — **skips the token-dependent tests
 *   with a stated reason** and runs everything else. That is what lets a
 *   developer exercise the readiness, capability-table and
 *   `401`-without-a-token assertions without generating certificates first,
 *   while leaving the report honest about what was not exercised.
 *
 * The acknowledgement is deliberately an **opt-in**, and deliberately not
 * inferred from the absence of the certificate itself. Inferring it is the
 * design that produces the silent partial run: absence is exactly the state a
 * misconfigured acceptance pipeline is in.
 *
 * NEITHER MODE FALLS BACK TO AN ANONYMOUS ISSUANCE CALL. Calling
 * `POST /v1/tokens` with no certificate asserts a behaviour the contract does
 * not offer, and the `401` it earns is indistinguishable from a real
 * authorization defect at the issuer.
 *
 * IMPORT-SAFE, WHICH IS WHY THE DECISION IS NOT MADE AT MODULE SCOPE
 * -----------------------------------------------------------------
 * `playwright test --list` must load and typecheck the whole suite with no stack
 * running and nothing configured (C-L). This module therefore reads the
 * environment and computes booleans at load time, and **throws nothing until a
 * hook or a test actually calls in.** There is no top-level `throw`, no request,
 * no `await` and no filesystem access.
 *
 * SECRET-FREE (C-F)
 * -----------------
 * No certificate, key, passphrase or token appears here, and no message this
 * module produces echoes a **value** — only variable NAMES and fixed prose. The
 * paths themselves are resolved by `service-endpoints.ts`, which is equally
 * value-free; the files they name are generated outside version control.
 *
 * WHY IT IS NOT RE-EXPORTED FROM `fixtures/index.ts`
 * -------------------------------------------------
 * It imports the `test` object, so it is runner-coupled in a way the four data
 * fixtures deliberately are not. Keeping it on its own import path — the same
 * treatment `live-stack.ts` gets — means the barrel stays a pure re-export of
 * runner-independent data.
 */

import { test } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';

import { acquireServiceToken } from './auth';
import type { ServiceToken, TokenRequest } from './auth';
import { SECURITY_CLIENT_CERTIFICATE } from './service-endpoints';

/**
 * The environment variable a run sets to declare itself deliberately partial.
 *
 * Named rather than inlined so the hook, the skip reason and the documentation
 * all say the same string. Accepted values are `1` and `true`, matched
 * case-insensitively after trimming; anything else set is reported as a
 * configuration error rather than quietly ignored, because a value of `ture`
 * would otherwise silently mean "full acceptance run" and produce exactly the
 * setup failure the author was trying to opt out of.
 */
export const PARTIAL_RUN_VARIABLE: string = 'E2E_ALLOW_MISSING_ISSUANCE_IDENTITY';

/** The two variables that together provision the suite's issuance identity. */
export const ISSUANCE_IDENTITY_VARIABLES: readonly string[] = Object.freeze([
  'SECURITY_MTLS_CERT_PATH',
  'SECURITY_MTLS_KEY_PATH',
]);

/**
 * The command that writes an ephemeral identity and prints the two paths.
 *
 * Quoted in every message so an operator never has to go and find it. The script
 * writes into a gitignored directory and generates fresh material on every run,
 * so nothing it produces is ever committed or reused across environments.
 */
export const PROVISION_COMMAND: string = 'npm run provision:identity';

/**
 * Whether a client identity for the mutual-TLS issuance edge is configured.
 *
 * Derived from {@link SECURITY_CLIENT_CERTIFICATE} rather than by re-reading the
 * environment, so the half-configured pair keeps failing fast in one place
 * instead of being re-interpreted here.
 */
export const TOKEN_ISSUANCE_PROVISIONED: boolean = SECURITY_CLIENT_CERTIFICATE !== undefined;

/** The values {@link PARTIAL_RUN_VARIABLE} accepts as an acknowledgement. */
const ACKNOWLEDGEMENT_VALUES: readonly string[] = Object.freeze(['1', 'true']);

/** The raw acknowledgement value, trimmed, or `undefined` when unset or blank. */
const rawAcknowledgement: string | undefined = ((): string | undefined => {
  const configured: string | undefined = process.env[PARTIAL_RUN_VARIABLE];

  if (configured === undefined) {
    return undefined;
  }

  const trimmed: string = configured.trim();

  return trimmed.length === 0 ? undefined : trimmed;
})();

/**
 * Whether this run has declared itself deliberately partial.
 *
 * `false` for an unset variable and `false` for an unrecognised value. The
 * unrecognised case is additionally reported by
 * {@link assertTokenIssuanceProvisioned}, so it cannot pass as either mode.
 */
export const PARTIAL_RUN_ACKNOWLEDGED: boolean =
  rawAcknowledgement !== undefined &&
  ACKNOWLEDGEMENT_VALUES.includes(rawAcknowledgement.toLowerCase());

/** Whether the variable is set to something this module does not recognise. */
const acknowledgementIsUnrecognised: boolean =
  rawAcknowledgement !== undefined && !PARTIAL_RUN_ACKNOWLEDGED;

/**
 * The sentence every message shares: what is missing, and what to run.
 *
 * Names only. It states both variables unconditionally rather than only the
 * absent one, because {@link SECURITY_CLIENT_CERTIFICATE} has already refused
 * the half-configured case — reaching here means **neither** is set.
 */
export const MISSING_IDENTITY_REASON: string =
  'No mutual-TLS client identity is configured, so the sole token issuer cannot ' +
  `be called: ${ISSUANCE_IDENTITY_VARIABLES.join(' and ')} are both unset. ` +
  '`POST /v1/tokens` on Security is authenticated by client certificate and by ' +
  'nothing else, on every topology including the local bring-up, so no token can ' +
  `be obtained without one. Run \`${PROVISION_COMMAND}\` to write an ephemeral ` +
  'pair and export the two paths. (Variable names only — no path, key or token ' +
  'value is ever echoed.)';

/**
 * Fails the setup of a token-dependent group when no identity is provisioned.
 *
 * Call it from `test.beforeAll` at the top of every group that acquires a token.
 * A hook failure is the right shape for this: it is reported once, it names the
 * group, and it cannot be mistaken for an assertion about the system under test.
 *
 * @throws Error when {@link PARTIAL_RUN_VARIABLE} is set to a value this module
 *         does not recognise — checked first, because that state is a mistake in
 *         either mode and silently treating it as a full run would produce a
 *         setup failure the author had tried to opt out of
 * @throws Error when no identity is provisioned and the run has not declared
 *         itself partial
 */
export function assertTokenIssuanceProvisioned(): void {
  if (acknowledgementIsUnrecognised) {
    throw new Error(
      `${PARTIAL_RUN_VARIABLE} is set to a value this suite does not ` +
        `recognise. Accepted values are ${ACKNOWLEDGEMENT_VALUES.join(' and ')}, ` +
        'matched case-insensitively. Unset it for a full acceptance run, or set ' +
        'it to one of those values to acknowledge a deliberately partial one. ' +
        '(The configured value is deliberately not quoted here.)',
    );
  }

  if (TOKEN_ISSUANCE_PROVISIONED || PARTIAL_RUN_ACKNOWLEDGED) {
    return;
  }

  throw new Error(
    `${MISSING_IDENTITY_REASON} A full acceptance run fails here rather than ` +
      'skipping, because a run that omitted every authenticated workflow and ' +
      'still reported green would prove nothing about the one property this ' +
      `suite exists to demonstrate. Set ${PARTIAL_RUN_VARIABLE}=1 to ` +
      'acknowledge a deliberately partial run instead, in which case the ' +
      'token-dependent tests skip with this reason and the rest still run.',
  );
}

/**
 * Obtains a bearer token, or skips the calling test when the edge is closed.
 *
 * The **only** way a token enters a spec in this suite. It is a precondition
 * wrapper around {@link acquireServiceToken} and adds nothing else: no caching,
 * no retry, no fallback and no alternative credential.
 *
 * When no identity is provisioned this calls `test.skip`, which aborts the
 * calling test and records the reason in the report. That branch is reachable
 * only in an acknowledged partial run, because
 * {@link assertTokenIssuanceProvisioned} has already failed the group's setup in
 * a full one — so a skip here always corresponds to a run that said it was
 * partial.
 *
 * @param request Playwright's `request` fixture. Passed in rather than created
 *                here so the runner's configured client certificate is actually
 *                presented on the mutual-TLS issuance edge, and so this module
 *                manages no service (C-J).
 * @param overrides optional per-call overrides; omit for the suite's ordinary
 *                  token
 * @returns the issued token together with the scheme, lifetime and granted scope
 *          set the issuer stated
 */
export async function requireServiceToken(
  request: APIRequestContext,
  overrides?: TokenRequest,
): Promise<ServiceToken> {
  test.skip(!TOKEN_ISSUANCE_PROVISIONED, MISSING_IDENTITY_REASON);

  return await acquireServiceToken(request, overrides);
}
