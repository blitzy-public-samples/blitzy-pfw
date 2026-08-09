/**
 * Run-time service-token acquisition, and the explicitly unauthenticated
 * request path, for the PowerFramework cross-service end-to-end suite.
 *
 * This is the suite's only authentication surface. Everything the specs know
 * about obtaining and presenting a credential is here, and nothing else in the
 * suite builds an `Authorization` header.
 *
 * ONE ISSUER, THREE VERIFIERS — AND THIS SUITE IS NEITHER
 * ------------------------------------------------------
 * Security is the **sole** token issuer in the system. Gateway, DataServices
 * and Persistence hold **verification material only**: no signing key, no
 * minting capability, no independent signing authority. They validate inbound
 * tokens with the framework's stock bearer handler against the key set Security
 * publishes, which is the property that keeps the security-critical validation
 * path framework code instead of hand-written code. AAP §0.6.6.3 is the plan of
 * record; `docs/SECRETS.md` §4.1 and §4.2 are the register, and
 * `docs/CONTRACTS.md` §4.2 is the contract statement of it.
 *
 * **Exactly one signing secret exists in the entire system, it is held by
 * Security, and it arrives there by configuration injection from the
 * orchestration secret layer.** This suite never reads it, never names it,
 * never derives anything from it and has no legitimate use for it. That is the
 * whole design of this module: it *asks the issuer* for a token over the
 * published contract, and it can therefore do its job while holding no secret
 * at all.
 *
 * Consequently **no token, key, certificate, passphrase or other credential is
 * stored in this repository** on the suite's behalf. Tokens are acquired at run
 * time, used for the request in hand, and dropped. Nothing here writes a
 * credential to disk, to a log, to a reporter, to an annotation, to an
 * attachment or to a module-level cache (C-F).
 *
 * THE ANTI-PATTERN THIS HELPER REPLACES
 * -------------------------------------
 * One of the legacy browser assets in the read-only oracle tree embeds key
 * material in plaintext and signs a token client-side with it. It is the first
 * of the eight in-source hardcoded-secret sites the repository-wide sweep found,
 * and the .NET tree cites it explicitly as *the anti-pattern to replace*.
 *
 * **`docs/SECRETS.md` is the register that locates it, value-free, and is the
 * only place a locator belongs — so this file cites that document rather than
 * restating any locator detail of its own** (C-F). What matters here is the
 * contrast, not the address: that asset signs its own token, and this module
 * cannot. It asks the issuer instead.
 *
 * **Nothing in this module reads, imports, points at, mirrors or reproduces any
 * fragment of that asset or its signing library**, and nothing in this suite
 * reaches into any of the legacy browser-asset directories that sit beside it
 * under `tests/`. They are read-only behavioural-oracle assets and are never an
 * input to this suite, at build time or at run time (C-C). Signing a token
 * client-side is precisely the capability this module refuses to have: a test
 * suite that could mint its own tokens would be a second issuer, and a
 * sole-issuer topology is only worth anything while there is exactly one.
 *
 * THE ANONYMOUS PATH IS DELIBERATE AND LOAD-BEARING (C-G)
 * ------------------------------------------------------
 * {@link anonymousHeaders} and {@link ANONYMOUS_HEADERS} are not an oversight
 * and are not dead code. The mandatory assertion that `/v1/ping` answers `401`
 * without a token is only writable if an anonymous request is expressible, so
 * the unauthenticated path is a first-class export of the authentication
 * fixture. A suite that could only make authenticated requests could prove that
 * a boundary *accepts* a good token but never that it *rejects* the absence of
 * one — which is the half that actually demonstrates the boundary is closed.
 *
 * For the same reason the token is applied **per request** and never globally:
 * `playwright.config.ts` deliberately sets no global `Authorization` header, so
 * that an anonymous request is the default and authentication is something a
 * spec opts into visibly. It also sets `ignoreHTTPSErrors: false`. Neither
 * decision is compensated for here.
 *
 * WHY THE CALLER PASSES THE REQUEST CONTEXT IN
 * --------------------------------------------
 * {@link acquireServiceToken} takes Playwright's `request` fixture as its first
 * parameter rather than building an `APIRequestContext` of its own, and that
 * serves three separate purposes:
 *
 * 1. **Import-safety (C-L).** `playwright test --list` loads and collects every
 *    fixture with no stack running. This module therefore has no module-scope
 *    `await`, no module-scope request and no module-scope `throw`, and it never
 *    terminates the process; importing it does nothing observable. The only
 *    `@playwright/test` import is a type-only one, so nothing is even pulled in
 *    at run time.
 * 2. **It never manages a service (C-J).** Nothing here starts, spawns, builds,
 *    seeds, health-gates or waits for anything. The single local bring-up path
 *    is `orchestration/docker-compose.yml`.
 * 3. **It is the only design that can authenticate on the token endpoint at
 *    all.** Token issuance is the system's single mutual-TLS edge — a caller
 *    cannot present a bearer token in order to obtain its first bearer token —
 *    and `playwright.config.ts` supplies the client certificate through
 *    `use.clientCertificates`. The injected `request` fixture therefore already
 *    presents it; a context this module built for itself would not, unless it
 *    rebuilt that configuration and drifted from it thereafter.
 *
 * WHAT THIS MODULE DELIBERATELY DOES NOT DO
 * -----------------------------------------
 * - **It does not decode, verify or introspect a token.** Verification is the
 *   services' job, performed by the stock bearer handler against the published
 *   key set. A client-side decoder here would invite assertions on claim
 *   internals that no contract publishes, and it would need a JWT library — so
 *   there is none, and the suite's only dependency stays the one it already
 *   declares.
 * - **It does not wrap the two anonymous publication endpoints.** Their paths
 *   are already exported by `./service-endpoints`, both are anonymous `GET`s a
 *   spec can issue directly, and a wrapper would add surface without removing
 *   duplication.
 * - **It declares no timeout, retry, backoff, latency budget, throughput target
 *   or availability figure.** The repository publishes none anywhere (AAP
 *   §0.8.5), so any number here would be fabricated (C-B). The request inherits
 *   the runner's configured timeout, which is the one place such a value is
 *   legitimately set.
 * - **It names no deferred capability area.** No audience, scope, header or
 *   helper below refers to DesignSystem, Documents, Integration or ScriptBridge,
 *   and no signing, verification or mutual-TLS setting is scaffolded for any of
 *   them (C-D).
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules
 * were provided. That is a finding rather than an omission, and it is not
 * licence to lower the bar — the enterprise-standard baseline of AAP §0.7.2
 * applies in its place. For this file the operative clause of that baseline is
 * the absolute one: **no secret in source, in configuration, or in any
 * artifact.**
 */

import type { APIRequestContext, APIResponse } from '@playwright/test';

import {
  SECURITY_CLIENT_CERTIFICATE,
  SERVICE_ENDPOINTS,
  TOKEN_PATH,
  securityUrl,
} from './service-endpoints';

// ---------------------------------------------------------------------------
// The token request — the shape, and the one place to edit it
// ---------------------------------------------------------------------------

/**
 * Overrides for a single token request.
 *
 * Every member is optional, and an omitted member takes the corresponding
 * default below. A spec that wants the suite's ordinary token passes nothing at
 * all; a spec that is specifically exercising issuance — a narrowed scope set,
 * a subject the presented certificate does not establish, an audience the
 * recipient will refuse — overrides only the member it is testing, so the
 * intent of the test is visible in the one field it names.
 *
 * The three members are the three things a token request carries, and the only
 * three: a caller identity, an intended audience, and a requested scope set.
 * There is deliberately **no credential member of any kind** — no shared
 * secret, no client credential, no assertion, no passphrase and no key
 * material. Caller identity on this endpoint is established by the transport,
 * not by a field in a JSON body, and the published request schema sets
 * `additionalProperties: false` so a credential-shaped field cannot be smuggled
 * in even if one were added here (C-F, C-G).
 */
export interface TokenRequest {
  /**
   * The caller identity to request a token for.
   *
   * This is a **claim, not a credential**. The identity actually honoured is
   * the one the presented client certificate establishes, and a mismatch
   * between the two is refused with `403` rather than quietly downgraded.
   */
  readonly subject?: string;

  /**
   * The single intended audience for the token.
   *
   * One audience per request, by contract. A caller needing tokens for two
   * audiences requests two tokens, so that a token is never valid somewhere its
   * holder did not intend it to be.
   */
  readonly audience?: string;

  /**
   * The requested scope set.
   *
   * The **granted** set returned by the issuer may be narrower, and a narrowing
   * is a successful outcome rather than an error. A spec that cares which
   * scopes it actually received must read them from the response rather than
   * assume this request was honoured in full.
   */
  readonly scopes?: readonly string[];
}

/**
 * The caller identity this suite requests tokens for.
 *
 * An **identifier, not a credential**: it is a stable, deterministic, obviously
 * non-secret name for "the end-to-end suite", it authenticates nothing on its
 * own, and it is safe in a log line. No credential of any kind appears anywhere
 * in this module — no shared secret, no client credential and nothing
 * resembling either — because the issuance edge authenticates its caller by
 * client certificate and the request body carries none.
 *
 * Because the honoured identity is the one the certificate establishes, a
 * deployment whose certificate maps to some other name will refuse this value
 * with `403`. That is the contract behaving correctly, and this constant is the
 * single place to change if a deployment's certificate establishes a different
 * identity.
 */
export const E2E_TOKEN_SUBJECT: string = 'pfw-e2e-suite';

/**
 * The audience this suite's tokens are issued for: **Gateway**.
 *
 * Gateway is the audience because Gateway is the sole ingress and the only
 * service this suite calls functionally. The topology is layered and acyclic —
 * nothing but an external client calls Gateway — so a suite that enters through
 * Gateway exercises the real ingress rather than reaching around it, and a token
 * scoped to that one audience cannot be replayed against a service the suite
 * never intended to call.
 *
 * The value is **derived from the Gateway service identity** in
 * `./service-endpoints` rather than written out as an unrelated literal, so the
 * roster stays the single source of truth for who the four services are. It was
 * additionally verified against the recipient that has to accept it: Gateway's
 * `appsettings.json` declares exactly this string as its sole valid audience,
 * and its options tests assert on it. If the two ever diverge, the service
 * configuration is right and this is wrong — an audience the recipient does not
 * accept is refused with `401`, which is the recipient behaving correctly.
 */
export const DEFAULT_TOKEN_AUDIENCE: string = `powerframework-${SERVICE_ENDPOINTS.gateway.key}`;

/**
 * The minimum scope set this suite needs.
 *
 * One entry per Gateway surface the specs actually call, and nothing else:
 *
 * - `ping` — the authenticated probe, the standing proof that every new
 *   boundary is authenticated.
 * - `capabilities` — Gateway's projection of the framework's capability gate.
 * - `datawindow` — the REST projection of the DataWindow contracts.
 *
 * Deliberately **no wildcard and no administrative scope**: a suite that asked
 * for everything would stop being able to demonstrate that least privilege is
 * enforceable, and it would mask a service that granted more than it should.
 *
 * The set is small, deterministic and frozen, and it is a **request** rather
 * than an entitlement. The granted set may be narrower, and a narrowing is a
 * `200`. No service publishes a scope vocabulary yet, and the issuance contract
 * requires at least one non-empty, unique entry; these three names mirror the
 * path segments they authorise, which is the only convention the repository
 * currently evidences.
 */
export const DEFAULT_TOKEN_SCOPES: readonly string[] = Object.freeze([
  'ping',
  'capabilities',
  'datawindow',
]);

/**
 * The request body exactly as it goes on the wire.
 *
 * Declared locally, and with every member required, because the published
 * schema marks all three as required and forbids additional properties. Keeping
 * the wire shape in its own type — separate from the all-optional
 * {@link TokenRequest} a caller supplies — is what makes "the caller may omit
 * anything" and "the wire body may omit nothing" both true and both checked.
 */
interface TokenRequestBody {
  readonly subject: string;
  readonly audience: string;
  readonly scopes: readonly string[];
}

/**
 * ===========================================================================
 * THE SINGLE EDIT POINT FOR THE TOKEN REQUEST CONTRACT
 * ===========================================================================
 *
 * **If the issuance request contract differs from what is built here, this
 * function is the only place that has to change.** No other function in this
 * module names a request field, and no spec builds a token request of its own.
 *
 * The honest provenance of these three field names, because it changes how much
 * weight to put on them:
 *
 * - When this fixture was specified, `services/` had no declared children, so
 *   the request and response bodies of the issuance contract could not be read
 *   from the tree at all. `docs/CONTRACTS.md` fixed the method and the path and
 *   described the payload in prose, but not the field spellings.
 * - By the time it was implemented, the published definition
 *   `shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` existed, and
 *   these three names were read from it directly: all three are required, all
 *   three are the spellings below, and `additionalProperties: false` means a
 *   fourth cannot be added. The schema also fixes the constraints the guard
 *   below enforces.
 * - What still has **not** been observed is a running service answering this
 *   request, because the issuing service is not yet built out. So the spellings
 *   are contract-confirmed rather than merely guessed, but they are not yet
 *   run-confirmed — which is exactly why the response side stays tolerant and
 *   why this remains a single, clearly marked edit point.
 *
 * The scope array is copied into a fresh mutable array so that the frozen
 * default cannot be handed to a serializer that might mutate it, and so a
 * caller's array is never aliased into a request that outlives the call.
 *
 * @param overrides the caller's overrides; every member is optional
 * @returns the complete wire body, with defaults applied
 */
function buildTokenRequestBody(overrides?: TokenRequest): TokenRequestBody {
  return {
    subject: overrides?.subject ?? E2E_TOKEN_SUBJECT,
    audience: overrides?.audience ?? DEFAULT_TOKEN_AUDIENCE,
    scopes: [...(overrides?.scopes ?? DEFAULT_TOKEN_SCOPES)],
  };
}

/**
 * Rejects a request the published schema would refuse, before it is sent.
 *
 * The schema constrains all three members: the caller identity and the audience
 * must each be a non-empty string, and the scope set must hold at least one
 * entry, every entry non-empty, with no duplicates. A request that breaks any of
 * those comes back as an opaque `400`, which tells a spec author that something
 * was malformed but not what. Failing here instead names the member and the rule
 * at the call site, one line from the mistake.
 *
 * This validates only the suite's own outgoing request. It asserts nothing about
 * the service, changes no behaviour of the contract, and adds no requirement the
 * schema does not already impose.
 *
 * No message quotes the offending value. These three fields carry no credential
 * by contract, so this is consistency with the sibling fixtures rather than
 * necessity — a validator cannot know which of its inputs is sensitive, so none
 * of them is echoed, and the member name is what locates the mistake anyway.
 *
 * @param body the wire body about to be sent
 * @throws Error when a member would violate the published schema
 */
function assertUsableTokenRequest(body: TokenRequestBody): void {
  if (body.subject.trim().length === 0) {
    throw new Error(
      'The token request subject is empty. The issuance contract requires a ' +
        'non-empty caller identity; pass a subject override or leave it unset ' +
        'to use the suite default.',
    );
  }

  if (body.audience.trim().length === 0) {
    throw new Error(
      'The token request audience is empty. The issuance contract requires a ' +
        'single non-empty audience; pass an audience override or leave it ' +
        'unset to use the suite default.',
    );
  }

  if (body.scopes.length === 0) {
    throw new Error(
      'The token request scope set is empty. The issuance contract requires ' +
        'at least one scope; pass a scopes override or leave it unset to use ' +
        'the suite default.',
    );
  }

  if (body.scopes.some((scope: string): boolean => scope.trim().length === 0)) {
    throw new Error(
      'The token request scope set contains an empty entry. Every requested ' +
        'scope must be a non-empty string.',
    );
  }

  if (new Set(body.scopes).size !== body.scopes.length) {
    throw new Error(
      'The token request scope set contains a duplicate entry. The issuance ' +
        'contract requires the requested scopes to be unique.',
    );
  }
}

// ---------------------------------------------------------------------------
// Response handling — tolerant about shape, loud about mismatch, silent about
// every value
// ---------------------------------------------------------------------------

/** A parsed JSON object whose members are unknown until each is narrowed. */
type JsonObject = Record<string, unknown>;

/**
 * The response members that may carry the issued token, in the order they are
 * tried.
 *
 * The first name is the one the published response schema actually declares —
 * it is required there, and it is the RFC 6749 §5.1 spelling, chosen so that a
 * stock client library parses the response with no bespoke code. It is
 * therefore not a guess.
 *
 * The two after it are **belt and braces, and they are temporary.** The
 * spellings are contract-confirmed but not yet run-confirmed: the issuing
 * service is not built out, so no implementation has been observed answering
 * this request. The two alternatives cover the only plausible ways an
 * implementation could differ from its own contract — the camel-cased form and
 * the bare form — so that a spelling mismatch surfaces as a passing test rather
 * than as a day of debugging a `401` from a downstream service that was handed
 * `undefined`.
 *
 * **Once a running service has been observed answering, narrow this to the
 * single real member.** Tolerance that outlives its justification stops being
 * caution and becomes a way for two spellings to both look correct.
 */
const TOKEN_FIELD_CANDIDATES: readonly string[] = Object.freeze([
  'access_token',
  'accessToken',
  'token',
]);

/**
 * The one-line reminder of how the stack is started, appended to every
 * acquisition failure.
 *
 * A spec author who sees an acquisition failure needs exactly two facts: which
 * request failed, and what to run. Naming the single orchestration path here
 * means the second one never has to be looked up, and it keeps this module from
 * being tempted to start anything itself (C-J).
 */
const BRING_UP_HINT: string =
  'Security must be running and reachable — the four services are brought up ' +
  'together by orchestration/docker-compose.yml.';

/**
 * The standing reminder of why no error message in this module quotes a value.
 *
 * A thrown message reaches the console, the reporter and the CI log, all of
 * which are retained. A token endpoint's response body carries a credential by
 * definition, so a message that quoted it would publish the credential to every
 * one of those places — reintroducing through the diagnostic exactly the leak
 * this module exists to avoid.
 */
const NAMES_ONLY_NOTE: string =
  'member names only — no value from a token endpoint is ever placed in an ' +
  'error message, because the message reaches the console and the CI log';

/**
 * Narrows an unknown parsed value to a JSON object.
 *
 * Arrays are rejected as well as primitives and `null`: an array has indexable
 * members and would pass a naive `typeof === 'object'` test, and reading a token
 * field off one silently yields `undefined`.
 *
 * @param value a value from `JSON.parse`
 * @returns the value as a JSON object, or `undefined` if it is not one
 */
function asJsonObject(value: unknown): JsonObject | undefined {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return undefined;
  }

  return value as JsonObject;
}

/**
 * Lists a JSON object's top-level member names, sorted.
 *
 * Sorted so that a diagnostic is deterministic and two runs are comparable.
 * **Names only — never a value.**
 *
 * @param value a parsed JSON object
 * @returns its top-level member names in ascending order
 */
function topLevelKeyNames(value: JsonObject): readonly string[] {
  return Object.keys(value).sort();
}

/**
 * Renders member names for a diagnostic.
 *
 * @param names the names to render, possibly empty
 * @returns a comma-separated list, or an explicit note that none was readable
 */
function formatKeyNames(names: readonly string[]): string {
  return names.length === 0 ? 'none readable' : names.join(', ');
}

/**
 * Reads a response's top-level member names without ever failing.
 *
 * Used on the error path, where the body is a problem document rather than a
 * token and the names are genuinely useful — they say at a glance whether the
 * expected problem shape came back or something else did. A body that is not
 * JSON is not itself an error worth reporting here, since the status code is
 * the substance of that diagnostic, so an unparseable body yields no names.
 *
 * @param response the response to inspect
 * @returns the sorted top-level member names, or an empty list
 */
async function readResponseKeyNames(response: APIResponse): Promise<readonly string[]> {
  try {
    const parsed: unknown = (await response.json()) as unknown;
    const payload: JsonObject | undefined = asJsonObject(parsed);

    return payload === undefined ? [] : topLevelKeyNames(payload);
  } catch {
    return [];
  }
}

/**
 * Describes a thrown value without assuming it is an `Error`.
 *
 * `useUnknownInCatchVariables` is enabled and a rejected request can carry
 * anything, so the shape is narrowed explicitly rather than interpolated and
 * rendered as `[object Object]`.
 *
 * @param cause the caught value, of unknown type
 * @returns a single-line description
 */
function describeCause(cause: unknown): string {
  if (cause instanceof Error) {
    return `${cause.name}: ${cause.message}`;
  }

  return `non-Error rejection: ${String(cause)}`;
}

/**
 * Explains a refusal on the one edge that is not bearer-authenticated.
 *
 * Token issuance is the system's **single mutual-TLS edge**, and it is the one
 * operation a bearer token cannot protect: a caller cannot present a token in
 * order to obtain its first token. A `401` or `403` here therefore means
 * something quite different from the same status anywhere else in the system,
 * and a diagnostic that did not say so would send a reader looking for a
 * missing bearer token that was never applicable.
 *
 * The one thing this reads from the certificate setting is **whether one is
 * configured at all** — a boolean. No path, no passphrase and no other member of
 * it is read, rendered or logged, here or anywhere else in this module.
 *
 * @param status the refusal status
 * @returns an explanatory clause, or an empty string for any other status
 */
function callerAuthenticationHint(status: number): string {
  if (status !== 401 && status !== 403) {
    return '';
  }

  const preamble: string =
    ' Token issuance is the single mutual-TLS edge in this system: it is' +
    ' authenticated by a client certificate and by nothing else, because a' +
    ' caller cannot present a bearer token in order to obtain its first one.';

  if (status === 403) {
    return (
      `${preamble} The certificate was trusted, but this caller is not` +
      ' permitted the requested subject or audience — most often the claimed' +
      ' subject does not match the identity the certificate establishes.'
    );
  }

  return SECURITY_CLIENT_CERTIFICATE === undefined
    ? `${preamble} No client certificate is configured for this suite, so none` +
        ' was presented. Configure the client-certificate settings the endpoint' +
        ' table documents, or run against the documented loopback bring-up,' +
        ' where there is no handshake and none is required.'
    : `${preamble} A client certificate IS configured, so either the issuer` +
        ' does not trust it or the request context did not present it — a' +
        " context built outside the runner's configuration carries none, which" +
        " is why this helper uses the caller's own request fixture.";
}

/**
 * Issues the POST, converting a transport failure into an actionable error.
 *
 * `failOnStatusCode` is pinned to `false` for a reason that is about secrets
 * rather than style: left to throw on a non-2xx, the runner raises an error of
 * its own whose message can quote the response body verbatim. Every non-success
 * outcome is therefore handled by this module explicitly, so that exactly one
 * message shape reaches a log and it is one that quotes nothing.
 *
 * A transport failure — a refused connection, an unresolvable host, a rejected
 * handshake — is wrapped so the message names the method and the URL and says
 * what to run. The original is attached as `cause`, which is safe here in a way
 * it is not on the parse path below: no response was received, so there is no
 * body and no token material to carry forward.
 *
 * No timeout is passed. The request inherits the runner's configured timeout,
 * and inventing one here would be a latency figure the repository does not
 * publish (C-B). No retry is attempted either, for the same reason.
 *
 * @param request the caller's request context, which carries any configured
 *                client certificate
 * @param url the absolute token-endpoint URL
 * @param body the validated wire body
 * @returns the response, whatever its status
 * @throws Error when the request never reached a response
 */
async function postTokenRequest(
  request: APIRequestContext,
  url: string,
  body: TokenRequestBody,
): Promise<APIResponse> {
  try {
    return await request.post(url, {
      data: body,
      failOnStatusCode: false,
    });
  } catch (cause: unknown) {
    throw new Error(
      `POST ${url} did not reach Security (${describeCause(cause)}). ` +
        BRING_UP_HINT,
      { cause },
    );
  }
}

/**
 * Obtains a bearer token from Security at run time.
 *
 * This is the only way a token enters this suite. It mints nothing, signs
 * nothing, derives nothing and reads no signing key: it asks the sole issuer
 * over the published contract and returns what the issuer produced.
 *
 * The returned value is a **credential**. The caller should place it in a
 * per-request header with {@link bearerHeaders} and let it fall out of scope
 * afterwards. It is deliberately **not cached** anywhere in this module — a
 * fresh token per call. There is no performance argument to answer, because the
 * runner executes with a single worker and no retries, and a cached short-lived
 * token is a flake source: the one that was fine for the first assertion is the
 * one that expires midway through the fifth. A spec that wants a single token
 * for several assertions holds it in its own scope, where its lifetime is
 * visible.
 *
 * @param request Playwright's `request` fixture. Passed in rather than created
 *                here so that module import stays side-effect free (C-L), so
 *                that this helper never manages a service (C-J), and so that
 *                any client certificate the runner configured is actually
 *                presented on the mutual-TLS issuance edge.
 * @param overrides optional per-call overrides; omit for the suite's ordinary
 *                  token
 * @returns the token, as a non-empty string
 * @throws Error when the request cannot be sent, when the issuer refuses, or
 *         when the response carries no recognisable token member. Every message
 *         names the method, the URL and the status or cause, and none of them
 *         quotes a response value.
 */
export async function acquireServiceToken(
  request: APIRequestContext,
  overrides?: TokenRequest,
): Promise<string> {
  const url: string = securityUrl(TOKEN_PATH);
  const body: TokenRequestBody = buildTokenRequestBody(overrides);

  assertUsableTokenRequest(body);

  const response: APIResponse = await postTokenRequest(request, url, body);

  if (!response.ok()) {
    const keyNames: readonly string[] = await readResponseKeyNames(response);

    throw new Error(
      `POST ${url} answered HTTP ${response.status()} ` +
        `${response.statusText()} instead of issuing a token.` +
        `${callerAuthenticationHint(response.status())} ${BRING_UP_HINT} ` +
        `Response carried [${formatKeyNames(keyNames)}] (${NAMES_ONLY_NOTE}).`,
    );
  }

  // A success body that will not parse is reported without attaching the parser
  // error. That is not caution for its own sake: a JSON parser quotes the text
  // it choked on, and on this endpoint that text is a credential. Node prints an
  // error's `cause` chain, so attaching it would publish the body to every log
  // that renders the failure.
  let parsed: unknown;
  try {
    parsed = (await response.json()) as unknown;
  } catch {
    throw new Error(
      `POST ${url} answered HTTP ${response.status()} but its body is not ` +
        'parseable JSON, so no token could be read. The parser error is ' +
        'deliberately omitted: it quotes the body it failed on, and on this ' +
        'endpoint that body is a credential.',
    );
  }

  const payload: JsonObject | undefined = asJsonObject(parsed);

  if (payload === undefined) {
    throw new Error(
      `POST ${url} answered HTTP ${response.status()} with a JSON value that ` +
        'is not an object, so it cannot carry a token member. The published ' +
        'response schema declares an object.',
    );
  }

  const token: string | undefined = extractToken(payload);

  if (token === undefined) {
    throw new Error(
      `POST ${url} answered HTTP ${response.status()} but carried no usable ` +
        `token. Members tried, in order: ` +
        `[${TOKEN_FIELD_CANDIDATES.join(', ')}]. Response carried ` +
        `[${formatKeyNames(topLevelKeyNames(payload))}] (${NAMES_ONLY_NOTE}). ` +
        'If the real member name is absent from the list tried, correct the ' +
        'candidate list in this fixture rather than working around it in a spec.',
    );
  }

  return token;
}

/**
 * Reads the token out of a parsed response body.
 *
 * Each candidate must be a string with non-whitespace content. A present but
 * empty member is treated as absent rather than returned, because an empty
 * credential would produce a syntactically valid `Authorization` header that
 * every service refuses — a `401` whose cause is three layers away from the
 * response that actually caused it.
 *
 * The value is returned exactly as issued. A credential is never trimmed,
 * re-encoded or otherwise adjusted on its way through: if an issuer ever
 * surrounds a token with whitespace, that is a defect to see rather than one to
 * paper over.
 *
 * @param payload the parsed response body
 * @returns the token, or `undefined` when no candidate member carries one
 */
function extractToken(payload: JsonObject): string | undefined {
  for (const field of TOKEN_FIELD_CANDIDATES) {
    const candidate: unknown = payload[field];

    if (typeof candidate === 'string' && candidate.trim().length > 0) {
      return candidate;
    }
  }

  return undefined;
}

// ---------------------------------------------------------------------------
// Header helpers — the authenticated path, and the deliberately anonymous one
// ---------------------------------------------------------------------------

/**
 * The request header a bearer credential is presented in.
 *
 * Spelled once, here, so that a spec never writes the name itself and a
 * misspelling cannot produce a request that is silently anonymous while looking
 * authenticated.
 */
export const AUTHORIZATION_HEADER: string = 'Authorization';

/**
 * The credential scheme, which the published response schema fixes as a
 * constant. No other credential type is issued, so this is a fixed token rather
 * than something read from a response.
 */
export const BEARER_SCHEME: string = 'Bearer';

/**
 * Builds the per-request headers that present a token.
 *
 * **Per request, deliberately.** `playwright.config.ts` sets no global
 * `Authorization` header, so authentication is never ambient: an anonymous
 * request is the default and every authenticated request opts in visibly at the
 * call site. That is what keeps the `401` assertion writable at all, and it is
 * why this returns headers to pass to one call rather than mutating anything
 * shared (C-G).
 *
 * A **fresh object every call**, never a shared or frozen one, because
 * Playwright merges the returned object into per-request options and a spec may
 * reasonably spread additional headers alongside it. Two calls can therefore
 * never interfere, and nothing a spec does to the result is visible to any other
 * spec.
 *
 * An empty or whitespace-only token **throws** rather than producing a header.
 * A header carrying an empty credential is the worst available outcome: the
 * request looks authenticated at the call site, is refused as unauthenticated by
 * the service, and the resulting `401` reads exactly like a genuine
 * authorization defect. Failing here names the real problem instead. If an
 * unauthenticated request is what a spec wants, that must be said out loud with
 * {@link anonymousHeaders}.
 *
 * The token never appears in the throw message — there is nothing useful to say
 * about a value that is empty, and a message shape that interpolated the token
 * at all would be one edit away from doing it on a non-empty one.
 *
 * @param token a token from {@link acquireServiceToken}
 * @returns a new headers object presenting the token
 * @throws Error when the token is empty or contains only whitespace
 */
export function bearerHeaders(token: string): Record<string, string> {
  if (token.trim().length === 0) {
    throw new Error(
      'bearerHeaders was given an empty token, and refuses to build an ' +
        'Authorization header with an empty credential: the request would look ' +
        'authenticated here and be refused as unauthenticated by the service, ' +
        'so the resulting 401 would misreport its own cause. Obtain a token ' +
        'with acquireServiceToken, or state an unauthenticated request ' +
        'explicitly with anonymousHeaders().',
    );
  }

  return { [AUTHORIZATION_HEADER]: `${BEARER_SCHEME} ${token}` };
}

/**
 * The empty header set — **an intentional, load-bearing export, not an
 * oversight and not dead code.**
 *
 * This is the suite's explicitly unauthenticated request path, and it exists
 * because the mandatory assertion that the guarded probe answers `401` without a
 * token is only writable if an anonymous request can be expressed. A suite that
 * could only make authenticated requests could show that a boundary accepts a
 * good token, but never that it rejects the absence of one — and that second
 * half is the half that demonstrates the boundary is actually closed (C-G).
 *
 * **A spec that uses this must assert `401`.** Anonymous access to a guarded
 * endpoint is not a tolerated outcome to be skipped past: if an anonymous
 * request to the guarded probe on any of the four services returns `200`, that
 * is not a flaky test and not a fixture defect — it is a **real violation of the
 * requirement that every newly created boundary is authenticated**, and it must
 * be reported as such. The legacy framework opened no listening socket and had
 * no authentication of any kind, so every one of these boundaries was created by
 * the decomposition itself; this is the assertion that proves each was created
 * closed.
 *
 * Frozen, so that a spec cannot add a header to the shared instance and quietly
 * authenticate every later anonymous request in the same process. Where a
 * mutable object is wanted — to spread other non-credential headers alongside —
 * use {@link anonymousHeaders} instead.
 */
export const ANONYMOUS_HEADERS: Readonly<Record<string, string>> = Object.freeze({});

/**
 * A fresh, empty header set for a deliberately unauthenticated request.
 *
 * The mutable counterpart of {@link ANONYMOUS_HEADERS}, with the same purpose
 * and the same obligation on its caller: **assert `401`**. Reading as
 * `anonymousHeaders()` at a call site is the point of it — it states in the test
 * that the absence of a credential is the condition under test, where a bare
 * `{}` would read as something somebody forgot to fill in.
 *
 * @returns a new empty headers object
 */
export function anonymousHeaders(): Record<string, string> {
  return {};
}
