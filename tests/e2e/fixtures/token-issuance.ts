/**
 * The token-issuance precondition — one place that decides what a run does when
 * no caller credential of either accepted kind has been provisioned.
 *
 * WHY A PRECONDITION RATHER THAN A PER-SPEC CONVENTION
 * ----------------------------------------------------
 * `POST /v1/tokens` on Security is protected by **a caller credential and by no
 * bearer token**, because a caller cannot present a bearer token in order to
 * obtain its first bearer token — and it accepts **either** of two: a shared
 * secret presented as an HTTP `Basic` credential naming a subject on Security's
 * issuance roster, or a client certificate. Security declares exactly one
 * listener — `https://+:5104` with `ClientCertificateMode` `AllowCertificate` —
 * in its **base** settings file, so the requirement holds in Development too: the
 * handshake completes with no certificate presented, and the token operation then
 * refuses a caller that presents neither credential, per operation. There is
 * therefore **no address, local or deployed, at which a token is minted for a
 * caller presenting neither.**
 *
 * Every authenticated workflow in this suite needs a token, so without a
 * credential every one of them is unrunnable. The failure mode this module exists
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
 *   run. The message names every variable and the command that writes a
 *   certificate pair.
 * - **A RUN THAT EXPLICITLY ACKNOWLEDGES BEING PARTIAL** — one environment
 *   variable, {@link PARTIAL_RUN_VARIABLE} — **skips the token-dependent tests
 *   with a stated reason** and runs everything else. That is what lets a
 *   developer exercise the readiness, capability-table and
 *   `401`-without-a-token assertions without provisioning a credential first,
 *   while leaving the report honest about what was not exercised.
 *
 * The acknowledgement is deliberately an **opt-in**, and deliberately not
 * inferred from the absence of the credential itself. Inferring it is the
 * design that produces the silent partial run: absence is exactly the state a
 * misconfigured acceptance pipeline is in.
 *
 * NEITHER MODE FALLS BACK TO AN ANONYMOUS ISSUANCE CALL. Calling
 * `POST /v1/tokens` with no credential at all asserts a behaviour the contract
 * does not offer, and the `401` it earns is indistinguishable from a real
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
import {
  SECURITY_CLIENT_CERTIFICATE,
  SECURITY_CLIENT_CREDENTIAL,
} from './service-endpoints';

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

/**
 * The variables that provision a caller credential for the issuance edge — the
 * `Basic` pair first, because that is the scheme the documented bring-up uses.
 *
 * FOUR NAMES AND NOT TWO, BECAUSE THE OPERATION ACCEPTS TWO SCHEMES. Either pair
 * on its own is sufficient: `POST /v1/tokens` declares `clientCredential` and
 * `mutualTls` as ALTERNATIVES. Naming only
 * `SECURITY_MTLS_CERT_PATH` and `SECURITY_MTLS_KEY_PATH` here would mean a run that had
 * provisioned an issuance roster entry — the documented path, and the only one
 * that needs no certificate material at all — was told it could obtain no token
 * and either failed its setup or skipped every authenticated workflow.
 *
 * The certificate names are the ones `service-endpoints.ts` actually resolves. Two
 * hazards about them are recorded in `README.md` §4.6 rather than restated here:
 * the orchestration template uses the same two names for Security's SERVER pair,
 * and handing the suite that pair produces a `403` on subject reconciliation
 * rather than anything that looks like the mistake.
 */
export const ISSUANCE_IDENTITY_VARIABLES: readonly string[] = Object.freeze([
  'SECURITY_CLIENT_ID',
  'SECURITY_CLIENT_SECRET',
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
 * Whether a caller credential the issuance operation accepts is configured.
 *
 * EITHER SCHEME COUNTS, because the operation accepts either. Derived from the two
 * resolvers rather than by re-reading the environment, so each half-configured
 * pair keeps failing fast in one place instead of being re-interpreted here.
 */
export const TOKEN_ISSUANCE_PROVISIONED: boolean =
  SECURITY_CLIENT_CREDENTIAL !== undefined || SECURITY_CLIENT_CERTIFICATE !== undefined;

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
 * Names only. It states every variable unconditionally rather than only the
 * absent ones, because {@link SECURITY_CLIENT_CREDENTIAL} and
 * {@link SECURITY_CLIENT_CERTIFICATE} have each already refused their own
 * half-configured case — reaching here means NEITHER scheme is configured at all.
 */
export const MISSING_IDENTITY_REASON: string =
  'No caller credential is configured, so the sole token issuer cannot be ' +
  `called: none of ${ISSUANCE_IDENTITY_VARIABLES.join(', ')} is set. ` +
  '`POST /v1/tokens` on Security accepts EITHER a shared secret as an HTTP ' +
  '`Basic` credential naming a subject on its issuance roster OR a client ' +
  'certificate, and refuses a request presenting neither — on every topology ' +
  'including the local bring-up. Set the first pair, which is the documented ' +
  `path and needs no certificate material, or run \`${PROVISION_COMMAND}\` to ` +
  'write an ephemeral certificate pair and export its two paths. (Variable ' +
  'names only — no path, key, secret or token value is ever echoed.)';

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
