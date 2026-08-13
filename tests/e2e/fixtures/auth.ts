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
 *    all.** Token issuance is the one operation a bearer token cannot protect — a
 *    caller cannot present a bearer token in order to obtain its first bearer
 *    token — so the operation accepts either of two caller credentials, and the
 *    injected fixture is what carries the one that lives at the transport layer.
 *    `playwright.config.ts` supplies any configured client certificate through
 *    `use.clientCertificates`, so the injected `request` fixture already presents
 *    it; a context this module built for itself would not, unless it rebuilt that
 *    configuration and drifted from it thereafter. The `Basic` credential is
 *    applied per request by {@link postTokenRequest} instead, because a header is
 *    the one part of a credential a request context cannot be relied upon to
 *    carry for us.
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
  SECURITY_CLIENT_CREDENTIAL,
  SERVICE_ENDPOINTS,
  TOKEN_PATH,
  basicAuthorizationHeader,
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
 * with `403`. That is the contract behaving correctly.
 *
 * ⚠ SO IT IS READ FROM THE ENVIRONMENT, AND ONLY DEFAULTS TO THIS NAME ⚠
 *
 * The default matches the client identity the documented recipe generates
 * (`docs/ARCHITECTURE.md` §9.3.1 issues a certificate for this exact common
 * name alongside Gateway's and DataServices'), so the out-of-the-box
 * configuration agrees with itself. `E2E_TOKEN_SUBJECT` exists for the case it
 * cannot cover: a deployment whose certificate authority issues under a
 * different naming convention. Aligning the two is then one variable rather than
 * an edit to this file, which matters because the alternative is a spec change
 * that looks like a behavioural change and would be reviewed as one.
 *
 * The value is trimmed and an all-whitespace override is treated as absent,
 * because the published schema requires a non-empty subject and refusing here —
 * with the variable named — is a better failure than sending a blank subject and
 * reading the schema violation back off the wire.
 *
 * It remains an **identifier, not a credential**, however it is supplied: it
 * authenticates nothing on its own, and the certificate is what does.
 */
export const E2E_TOKEN_SUBJECT: string = ((): string => {
  const configured: string | undefined = process.env['E2E_TOKEN_SUBJECT'];

  const trimmed: string = configured === undefined ? '' : configured.trim();

  return trimmed.length === 0 ? 'pfw-e2e-suite' : trimmed;
})();

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
 *   run-confirmed — which is why this remains a single, clearly marked edit
 *   point. It is **not** a reason for the response side to be tolerant: the
 *   response side parses the published `TokenResponse` and nothing else, for the
 *   reasons set out where it does so.
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
// Response handling — strict about shape, loud about mismatch, silent about
// every value
// ---------------------------------------------------------------------------

/** A parsed JSON object whose members are unknown until each is narrowed. */
type JsonObject = Record<string, unknown>;

/**
 * ===========================================================================
 * THE RESPONSE IS PARSED AS THE PUBLISHED `TokenResponse` AND AS NOTHING ELSE
 * ===========================================================================
 *
 * `shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` declares
 * `TokenResponse` with `additionalProperties: false`, four required members and
 * one optional one. These constants are that declaration, restated once:
 *
 * - `access_token` — string, `minLength: 1`
 * - `token_type`   — string, `const: Bearer`
 * - `expires_in`   — integer, `format: int64`, `minimum: 1`
 * - `scope`        — string, space-delimited **granted** set, possibly empty
 * - `issued_at`    — optional integer, `format: int64`
 *
 * **An earlier form of this fixture accepted `accessToken` and `token` as well,
 * and that tolerance was wrong in a way worth stating so it is not reintroduced.**
 * The justification offered for it was that the spellings were
 * contract-confirmed but not run-confirmed — but tolerating three spellings does
 * not resolve that uncertainty, it hides it. A service answering with
 * `accessToken` violates its own published contract, and the suite whose job is
 * to verify the boundary would have reported that violation as a pass. Worse, it
 * validated none of the other three members at all: a response carrying an
 * expired-on-arrival `expires_in`, a `token_type` of `Basic`, or no granted
 * `scope` would have been accepted silently, and the resulting `401` from
 * Gateway would have pointed at Gateway rather than at the issuer that caused
 * it.
 *
 * A later form narrowed the parse to the single `access_token` member instead.
 * That was the right direction and still short of the contract: it read one of
 * the four required members and left the other three unchecked, so the same
 * class of issuer defect stayed invisible. The inventory below is the whole
 * declaration, which is what makes the check exact rather than merely stricter.
 *
 * So the parse is exact in both directions: every required member must be
 * present and well-formed, and no member outside this inventory may appear,
 * because the contract forbids one. A contract violation fails **here**, at the
 * issuer, naming the member — which is the whole reason a contract is published.
 */
const ACCESS_TOKEN_MEMBER: string = 'access_token';

const TOKEN_TYPE_MEMBER: string = 'token_type';

const EXPIRES_IN_MEMBER: string = 'expires_in';

const SCOPE_MEMBER: string = 'scope';

const ISSUED_AT_MEMBER: string = 'issued_at';

/** The four members `TokenResponse` marks required, in the order it declares them. */
const TOKEN_RESPONSE_REQUIRED_MEMBERS: readonly string[] = Object.freeze([
  ACCESS_TOKEN_MEMBER,
  TOKEN_TYPE_MEMBER,
  EXPIRES_IN_MEMBER,
  SCOPE_MEMBER,
]);

/**
 * Every member `TokenResponse` permits — the four required plus the one optional.
 *
 * Used to enforce `additionalProperties: false`. A member outside this set is a
 * contract violation on a credential-bearing response, which is the one place an
 * unreviewed field is least acceptable.
 */
const TOKEN_RESPONSE_PERMITTED_MEMBERS: readonly string[] = Object.freeze([
  ...TOKEN_RESPONSE_REQUIRED_MEMBERS,
  ISSUED_AT_MEMBER,
]);

/**
 * One issued token, exactly as the published `TokenResponse` describes it.
 *
 * The whole response is carried rather than just the token string, for two
 * reasons that are both about not lying to a caller:
 *
 * 1. **The scheme is the issuer's to state, not this fixture's to assume.**
 *    {@link bearerHeaders} presents `token_type` as it was issued. The contract
 *    fixes that value as the constant `Bearer` and {@link BEARER_SCHEME} is
 *    checked against it during the parse — so the header is built from a value
 *    that has been *verified* to equal the constant, rather than from the
 *    constant while the issued value went unread. Those look identical while the
 *    contract holds and differ the moment it does not, and the second is the
 *    only case worth writing a test for.
 * 2. **The granted scope set is not the requested one.** The contract says so
 *    explicitly, and a narrowing is a `200`. A caller handed only the token
 *    string cannot tell whether it received what it asked for, so the granted
 *    set travels with the credential that carries it.
 *
 * The token itself is a **credential**: use it for the request in hand and let it
 * fall out of scope. Nothing in this module caches, logs, writes or reports it.
 */
export interface ServiceToken {
  /** The signed token, exactly as issued — never trimmed, re-encoded or adjusted. */
  readonly accessToken: string;

  /**
   * The credential scheme the issuer stated, verified during the parse to be the
   * contract's constant. Presented verbatim by {@link bearerHeaders}.
   */
  readonly tokenType: string;

  /** The token lifetime in seconds from issuance; at least 1 by contract. */
  readonly expiresInSeconds: number;

  /** The granted scope set exactly as issued, space-delimited and possibly empty. */
  readonly scope: string;

  /**
   * The granted scope set split on whitespace — empty when nothing was granted.
   *
   * Frozen, because a caller comparing what it asked for against what it got has
   * no business mutating the record of the second.
   */
  readonly grantedScopes: readonly string[];

  /**
   * The issuance time in seconds since the Unix epoch, when the issuer supplied
   * it. Optional by contract, so `undefined` here means "not stated" rather than
   * "zero".
   */
  readonly issuedAtEpochSeconds?: number;
}

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
 * Token issuance is the one operation a bearer token cannot protect: a caller
 * cannot present a token in order to obtain its first token. A `401` or `403`
 * here therefore means something quite different from the same status anywhere
 * else in the system, and a diagnostic that did not say so would send a reader
 * looking for a missing bearer token that was never applicable.
 *
 * The published contract accepts **either** of two caller credentials, so the
 * message has to name which ones this run actually had available; a hint that
 * mentioned only one would send an operator to configure the wrong thing. The one
 * thing this reads from either setting is **whether it is configured at all** — a
 * boolean each. No identifier, secret, path or passphrase is read, rendered or
 * logged, here or anywhere else in this module (C-F).
 *
 * @param status the refusal status
 * @returns an explanatory clause, or an empty string for any other status
 */
function callerAuthenticationHint(status: number): string {
  if (status !== 401 && status !== 403) {
    return '';
  }

  const preamble: string =
    ' Token issuance is the one operation in this system a bearer token cannot' +
    ' protect, because a caller cannot present a token in order to obtain its' +
    ' first one. It authenticates its caller by one of two credentials instead:' +
    ' an HTTP Basic credential naming a subject on the issuance roster, or a' +
    ' client certificate where a deployment terminates TLS at Security.';

  if (status === 403) {
    return (
      `${preamble} The caller WAS authenticated, so this is an authorization` +
      ' refusal rather than an authentication one: the claimed subject does not' +
      ' match the identity the presented credential establishes, or the roster' +
      ' entry for that subject does not permit the requested audience or one of' +
      ' the requested scopes.'
    );
  }

  const hasCredential: boolean = SECURITY_CLIENT_CREDENTIAL !== undefined;
  const hasCertificate: boolean = SECURITY_CLIENT_CERTIFICATE !== undefined;

  if (!hasCredential && !hasCertificate) {
    return (
      `${preamble} Neither is configured for this suite, so nothing was` +
      ' presented and a 401 is the specified answer rather than a defect.' +
      ' Configure the Basic credential — that is the path the documented' +
      ' bring-up uses because it needs no certificate provisioning, and the' +
      ' endpoint table names its two variables — or the client-certificate' +
      ' settings, which the documented bring-up also supports since every' +
      ' listener here terminates TLS. A spec that needs a token skips itself' +
      ' until one of them is set.'
    );
  }

  const configured: string = hasCredential
    ? hasCertificate
      ? 'Both a Basic credential and a client certificate ARE configured'
      : 'A Basic credential IS configured'
    : 'A client certificate IS configured';

  return (
    `${preamble} ${configured}, so the credential reached Security and was` +
    ' refused: the subject is not on the issuance roster, or the secret does' +
    ' not match, or the certificate does not chain to the configured issuer.' +
    ' The response deliberately does not distinguish those, so check the' +
    " roster Security is running with. If only a certificate is configured," +
    ' note that a request context built outside the runner\u2019s configuration' +
    " presents none, which is why this helper uses the caller\u2019s own request" +
    ' fixture.'
  );
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
 * THE `Basic` CREDENTIAL IS APPLIED HERE, PER REQUEST, AND NOWHERE ELSE
 * --------------------------------------------------------------------
 * The published contract accepts either of two caller credentials on this
 * operation. The client certificate lives at the transport layer and the runner
 * presents it for us; the `Basic` credential is a header, so exactly one place
 * has to attach it, and this is that place. Attaching it per request rather than
 * configuring it as an `extraHTTPHeaders` default is deliberate: an
 * `extraHTTPHeaders` entry would ride along on **every** request the suite makes,
 * including the ones to Gateway, DataServices and Persistence, sending Security's
 * issuance secret to three services that have no business receiving it.
 *
 * When no credential is configured the header is simply absent and the request
 * goes out anonymously. That is not a fallback to an unauthenticated call — it is
 * the request whose specified answer is `401`, which
 * `specs/02-authentication.spec.ts` asserts on directly, and
 * {@link callerAuthenticationHint} explains when it arrives unexpectedly.
 *
 * The header value never appears in a diagnostic. The `catch` below names the
 * method and the URL only, and the object it is built from is discarded with the
 * call (C-F).
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
  const headers: Record<string, string> | undefined =
    SECURITY_CLIENT_CREDENTIAL === undefined
      ? undefined
      : { [AUTHORIZATION_HEADER]: basicAuthorizationHeader(SECURITY_CLIENT_CREDENTIAL) };

  try {
    return await request.post(url, {
      data: body,
      failOnStatusCode: false,
      ...(headers === undefined ? {} : { headers }),
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
 * The returned value carries a **credential**. The caller should place it in a
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
 * @returns the issued token together with the scheme, lifetime and granted scope
 *          set the issuer stated
 * @throws Error when the request cannot be sent, when the issuer refuses, or
 *         when the response is not a conforming `TokenResponse`. Every message
 *         names the method, the URL and the status, cause or member at fault,
 *         and none of them quotes a response value.
 */
export async function acquireServiceToken(
  request: APIRequestContext,
  overrides?: TokenRequest,
): Promise<ServiceToken> {
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

  return parseTokenResponse(payload, url);
}

/**
 * Parses a success body as the published `TokenResponse`, or throws.
 *
 * Every check below is a clause of the published schema rather than a preference
 * of this fixture, and each failure names the member and the rule while quoting
 * **no value** — the body of a token endpoint is a credential by definition, and
 * a thrown message reaches the console, the reporter and the CI log.
 *
 * The order is deliberate: the member set is checked first, so that a response
 * carrying an alias like `accessToken` is diagnosed as the contract violation it
 * is rather than as a missing `access_token` with no explanation of what arrived
 * instead.
 *
 * `access_token` is returned exactly as issued. A credential is never trimmed,
 * re-encoded or otherwise adjusted on its way through: if an issuer ever
 * surrounds a token with whitespace, that is a defect to see rather than one to
 * paper over. It is checked for non-whitespace content because an empty
 * credential produces a syntactically valid `Authorization` header that every
 * service refuses — a `401` whose cause is three layers away from the response
 * that caused it.
 *
 * @param payload the parsed success body
 * @param url the token-endpoint URL, so a message locates the issuer
 * @returns the issued token and its metadata
 * @throws Error when the body is not a conforming `TokenResponse`
 */
function parseTokenResponse(payload: JsonObject, url: string): ServiceToken {
  const present: readonly string[] = topLevelKeyNames(payload);
  const arrived: string = formatKeyNames(present);

  const unexpected: readonly string[] = present.filter(
    (name: string): boolean => !TOKEN_RESPONSE_PERMITTED_MEMBERS.includes(name),
  );

  if (unexpected.length > 0) {
    throw new Error(
      `POST ${url} answered with member(s) the published TokenResponse does ` +
        `not permit: [${formatKeyNames(unexpected)}]. The schema sets ` +
        'additionalProperties: false, so this is a contract violation at the ' +
        `issuer. Permitted members are ` +
        `[${TOKEN_RESPONSE_PERMITTED_MEMBERS.join(', ')}]; the response ` +
        `carried [${arrived}] (${NAMES_ONLY_NOTE}). If the contract changed, ` +
        'change it in security.v1.yaml and here together — never work around ' +
        'it in a spec.',
    );
  }

  const missing: readonly string[] = TOKEN_RESPONSE_REQUIRED_MEMBERS.filter(
    (name: string): boolean => !present.includes(name),
  );

  if (missing.length > 0) {
    throw new Error(
      `POST ${url} answered without required member(s) ` +
        `[${formatKeyNames(missing)}] of the published TokenResponse. ` +
        `Required members are [${TOKEN_RESPONSE_REQUIRED_MEMBERS.join(', ')}]; ` +
        `the response carried [${arrived}] (${NAMES_ONLY_NOTE}). The RFC 6749 ` +
        'section 5.1 spellings are the contract, and a camel-cased or bare ' +
        'alias is a violation of it rather than an accepted variant.',
    );
  }

  const accessToken: unknown = payload[ACCESS_TOKEN_MEMBER];

  if (typeof accessToken !== 'string' || accessToken.trim().length === 0) {
    throw new Error(
      `POST ${url} answered with a '${ACCESS_TOKEN_MEMBER}' that is not a ` +
        'non-empty string. The schema declares a string with minLength 1, and ' +
        'an empty credential would build an Authorization header every service ' +
        `refuses (${NAMES_ONLY_NOTE}).`,
    );
  }

  const tokenType: unknown = payload[TOKEN_TYPE_MEMBER];

  if (tokenType !== BEARER_SCHEME) {
    throw new Error(
      `POST ${url} answered with a '${TOKEN_TYPE_MEMBER}' that is not the ` +
        `contract's constant '${BEARER_SCHEME}'. The schema fixes it as a ` +
        'const because no other credential type is issued, so a different ' +
        'scheme is not a variant to accommodate: presenting a bearer header ' +
        'for a non-bearer credential would be refused downstream and would ' +
        `report the refusal at the wrong service (${NAMES_ONLY_NOTE}).`,
    );
  }

  const expiresIn: unknown = payload[EXPIRES_IN_MEMBER];

  if (
    typeof expiresIn !== 'number' ||
    !Number.isInteger(expiresIn) ||
    expiresIn < 1
  ) {
    throw new Error(
      `POST ${url} answered with an '${EXPIRES_IN_MEMBER}' that is not an ` +
        'integer of at least 1. The schema declares an int64 with minimum 1, ' +
        'and a token that is already expired on arrival produces a downstream ' +
        '401 that looks exactly like an authorization defect in the service ' +
        `that refused it (${NAMES_ONLY_NOTE}).`,
    );
  }

  const scope: unknown = payload[SCOPE_MEMBER];

  if (typeof scope !== 'string') {
    throw new Error(
      `POST ${url} answered with a '${SCOPE_MEMBER}' that is not a string. ` +
        'The schema declares the GRANTED set as a space-delimited string, ' +
        'which may legitimately be empty; a non-string means the granted set ' +
        `cannot be read at all (${NAMES_ONLY_NOTE}).`,
    );
  }

  const issuedAtRaw: unknown = payload[ISSUED_AT_MEMBER];

  if (
    issuedAtRaw !== undefined &&
    (typeof issuedAtRaw !== 'number' || !Number.isInteger(issuedAtRaw))
  ) {
    throw new Error(
      `POST ${url} answered with an '${ISSUED_AT_MEMBER}' that is present but ` +
        'not an integer. The member is optional, so omitting it is correct and ' +
        'supplying a non-integer is not: the schema declares an int64 of ' +
        `seconds since the Unix epoch (${NAMES_ONLY_NOTE}).`,
    );
  }

  // Split on any run of whitespace, and drop the empty segments a leading,
  // trailing or doubled separator produces. An empty granted set is a legitimate
  // outcome the contract names explicitly, so it yields an empty list rather
  // than a list holding one empty string.
  const grantedScopes: readonly string[] = Object.freeze(
    scope.split(/\s+/u).filter((entry: string): boolean => entry.length > 0),
  );

  return {
    accessToken,
    tokenType,
    expiresInSeconds: expiresIn,
    scope,
    grantedScopes,
    ...(issuedAtRaw === undefined
      ? {}
      : { issuedAtEpochSeconds: issuedAtRaw as number }),
  };
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
 * The credential scheme the published response schema fixes as a `const`.
 *
 * **This is the value the issued `token_type` is CHECKED AGAINST, not the value
 * the header is built from.** The distinction is the point of it: an earlier form
 * of this fixture wrote this constant straight into the `Authorization` header
 * and never read `token_type` at all, so an issuer answering with a different
 * scheme would have had a bearer header built for it anyway — and the resulting
 * downstream `401` would have accused the wrong service. Now the parse asserts
 * `token_type === BEARER_SCHEME` and {@link bearerHeaders} presents the issued
 * value, so the two agree because they were compared rather than because one was
 * ignored.
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
 * **The scheme presented is the one the ISSUER stated**, taken from the parsed
 * `token_type` rather than from {@link BEARER_SCHEME}. The parse has already
 * proven the two are equal — the contract fixes `token_type` as a `const` — so
 * this reads the issued value on principle rather than out of doubt: a fixture
 * that writes the scheme it expected, while never reading the one it was given,
 * cannot notice the day they differ.
 *
 * An empty or whitespace-only token **throws** rather than producing a header.
 * A header carrying an empty credential is the worst available outcome: the
 * request looks authenticated at the call site, is refused as unauthenticated by
 * the service, and the resulting `401` reads exactly like a genuine
 * authorization defect. Failing here names the real problem instead. If an
 * unauthenticated request is what a spec wants, that must be said out loud with
 * {@link anonymousHeaders}. An empty scheme throws for the same reason: it would
 * yield a header whose value begins with a space.
 *
 * Neither the token nor the scheme appears in a throw message — there is nothing
 * useful to say about a value that is empty, and a message shape that
 * interpolated the token at all would be one edit away from doing it on a
 * non-empty one.
 *
 * @param token a token from {@link acquireServiceToken}
 * @returns a new headers object presenting the token under the issued scheme
 * @throws Error when the token or the scheme is empty or whitespace-only
 */
export function bearerHeaders(token: ServiceToken): Record<string, string> {
  if (token.accessToken.trim().length === 0) {
    throw new Error(
      'bearerHeaders was given an empty token, and refuses to build an ' +
        'Authorization header with an empty credential: the request would look ' +
        'authenticated here and be refused as unauthenticated by the service, ' +
        'so the resulting 401 would misreport its own cause. Obtain a token ' +
        'with acquireServiceToken, or state an unauthenticated request ' +
        'explicitly with anonymousHeaders().',
    );
  }

  if (token.tokenType.trim().length === 0) {
    throw new Error(
      'bearerHeaders was given a token whose scheme is empty, and refuses to ' +
        'build an Authorization header that would begin with a space. The ' +
        'published contract fixes token_type as the constant ' +
        `'${BEARER_SCHEME}', and acquireServiceToken enforces it, so an empty ` +
        'scheme here means the value was assembled somewhere other than by the ' +
        'parse.',
    );
  }

  return { [AUTHORIZATION_HEADER]: `${token.tokenType} ${token.accessToken}` };
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
