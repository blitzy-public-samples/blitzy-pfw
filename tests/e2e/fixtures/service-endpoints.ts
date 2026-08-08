/**
 * Service addresses and endpoint paths for the PowerFramework cross-service
 * end-to-end suite — the suite's single source of truth.
 *
 * WHY THIS MODULE EXISTS
 * ----------------------
 * Every other fixture and every spec reaches the running stack through this
 * module. It is declared before `specs/` for exactly that reason: an address
 * or a path that appears in two files is an address that will eventually
 * disagree with itself, and a suite whose expectations are scattered across
 * six spec files cannot be corrected in one edit.
 *
 * That last point is not hypothetical. When this suite was specified,
 * `services/` had no declared children, so the precise JSON shape of the
 * responses behind these paths could not be read from the source tree.
 * Centralising the addressing here is precisely what makes a single edit
 * sufficient if a path turns out to differ from the contract as published.
 *
 * The module is pure data plus two trivial pure join helpers. It performs no
 * network access, reads no file, holds no state, and imports nothing at all —
 * not even `@playwright/test`.
 *
 * THE PORT MAP AND ITS AUTHORITY
 * ------------------------------
 * `docs/ARCHITECTURE.md` §4.1 is the authority for the map below, and AAP
 * §0.3.2.2 is the plan of record behind it. This module must not contradict
 * either; where a value here and a value there ever differ, those are right
 * and this is wrong.
 *
 *   5101  Persistence   PowerFramework.Persistence   gRPC, plus REST /health and /v1/ping
 *   5102  DataServices  PowerFramework.DataServices  gRPC primary, plus a thin REST projection
 *   5104  Security      PowerFramework.Security      REST, and the sole token issuer
 *   5105  Gateway       PowerFramework.Gateway       REST + OpenAPI, the sole ingress
 *
 * The band runs 5101 to 5105 and the composition root is published on 5105,
 * both preserved from the attached environment (C-L) so the environment's
 * access URL still resolves.
 *
 * The one port in the middle of that band — between DataServices and
 * Security — is absent from this table on purpose. The attached environment
 * had allocated it to a capability service that is one of the four deferred
 * out of this phase, so the orchestration manifest keeps that slot
 * commented out rather than reassigning it, and this table declares no
 * entry, no base URL and no environment variable for it (C-D).
 * `docs/ARCHITECTURE.md` §4.3 names the port and states the reasoning in
 * full; that document and `docs/DEFERRED.md` are where the deferred half of
 * the roster is described, not here.
 *
 * For the same reason this module declares no path constant for any of the
 * four reserved Gateway extension points. Exactly one spec exercises them,
 * and it declares them locally — a constant published here would be a
 * standing invitation for a second consumer to appear.
 *
 * TOKEN TOPOLOGY: ONE ISSUER, THREE VERIFIERS
 * -------------------------------------------
 * Security is the **sole** token issuer in the system. Gateway, DataServices
 * and Persistence hold verification material only: no signing key and no
 * minting capability. `docs/SECRETS.md` §4 is the register for this, and it
 * records that the system's single signing secret is held by Security and by
 * no other component, is supplied by configuration injection from the
 * orchestration secret layer, and does not appear in source.
 *
 * This suite is a consumer of that topology, never a participant in it. It
 * requests tokens from the issuer over the published path below and reads
 * the published verification material; it never reads, derives, reproduces
 * or logs the signing secret, and it names no secret at all (C-F). Nothing
 * in this file is a credential — the constants here are addresses.
 *
 * IMPORT-SAFE, AND WHY THAT MATTERS
 * ---------------------------------
 * The run path includes a collection-only invocation, exposed by the sibling
 * `package.json` as the `test:list` script, which loads and typechecks every
 * fixture and every spec **with no stack running** (C-L). This module is
 * therefore free of module-scope side effects by construction: every
 * environment read has a working default, and there is no top-level `throw`,
 * `await`, `fetch`, request or `process.exit` anywhere in it, and no
 * assertion that anything is reachable.
 *
 * A consequence worth stating plainly: importing this module proves nothing
 * about whether the stack is up. Readiness is asserted by the specs against
 * the anonymous health path, and it is asserted nowhere else.
 *
 * WHAT THIS MODULE DELIBERATELY DOES NOT DO
 * -----------------------------------------
 * - It never starts, spawns, builds, seeds or health-gates a service. The
 *   single local bring-up path is the orchestration manifest under
 *   `orchestration/`, whose health-condition dependency chain is what makes
 *   Gateway report healthy only after its upstreams do (C-J). The sibling
 *   `playwright.config.ts` omits a `webServer` block for the same reason.
 * - It declares no timeout, latency budget, throughput target, availability
 *   figure or service-level constant. The repository publishes none anywhere
 *   (AAP §0.8.5), so asserting one here would be fabricated (C-B).
 * - It reads nothing from the read-only legacy tree, at build time or at run
 *   time. That tree is the behavioural oracle and is never an input to this
 *   module (C-C); the three sibling directories under `tests/` that hold
 *   oracle browser assets are untouched by this suite.
 *
 * NO USER RULES GOVERN THIS FILE
 * ------------------------------
 * The project's rules document contains exactly one statement: no user rules
 * were provided. That is a finding, not an omission, and it is not licence to
 * lower the bar — the enterprise-standard baseline of AAP §0.7.2 applies in
 * its place, which for this module means strict typing with no implicit
 * `any`, no secret in source, content that is deterministic and reviewable,
 * and no dependency beyond the one the suite already declares.
 */

// ---------------------------------------------------------------------------
// Environment resolution
// ---------------------------------------------------------------------------

/**
 * Resolves one base URL from the environment, falling back to a default.
 *
 * The four base URLs below all resolve through this one helper so that their
 * behaviour is uniform and there is a single place to read it. The rules, in
 * order:
 *
 * 1. An unset variable yields the default.
 * 2. The configured value is trimmed, because a value that arrives from an
 *    environment file or a shell export frequently carries stray whitespace.
 * 3. A value that is empty once trimmed is treated as **absent**, not as an
 *    error, and yields the default.
 * 4. Exactly one trailing `/` is stripped, so that a caller may concatenate a
 *    leading-slash path onto the result without producing a double slash.
 *
 * Rule 3 is a deliberate divergence from the sibling `playwright.config.ts`,
 * which treats an empty `GATEWAY_BASE_URL` as a fatal misconfiguration and
 * throws. Both behaviours are correct in their own place: the runner config
 * is entitled to fail fast because it is the entry point, whereas this module
 * must stay importable under the collection-only invocation described above,
 * so it cannot throw at module scope (C-L). The divergence is unobservable in
 * practice — the config is evaluated first, so an empty value ends the run
 * there, before any fixture is imported.
 *
 * Rule 4 strips one slash rather than all of them, so a pathological value
 * ending in `//` normalises to a single trailing slash rather than being
 * silently rewritten further. Aggressive normalisation of an obviously wrong
 * value would hide the mistake instead of surfacing it in the request URL.
 *
 * @param variableName the environment variable to read, named exactly as it
 *                     appears in the orchestration environment file
 * @param fallback the default used when the variable is unset or blank; it is
 *                 returned as authored and needs no normalisation
 * @returns an absolute base URL with no trailing slash
 */
function resolveBaseUrl(variableName: string, fallback: string): string {
  const configured: string | undefined = process.env[variableName];

  if (configured === undefined) {
    return fallback;
  }

  const trimmed: string = configured.trim();

  if (trimmed.length === 0) {
    return fallback;
  }

  return trimmed.endsWith('/') ? trimmed.slice(0, -1) : trimmed;
}

// ---------------------------------------------------------------------------
// Base URLs, one per in-scope service
// ---------------------------------------------------------------------------

/**
 * Persistence, on 5101 — the only service that generates or executes SQL and
 * the only one holding a storage provider.
 *
 * Its primary transport is gRPC, so this suite drives no business operation
 * against it directly; the address exists so the anonymous health path can be
 * probed and so a readiness walk can name which upstream is not ready.
 */
export const PERSISTENCE_BASE_URL: string = resolveBaseUrl(
  'PERSISTENCE_BASE_URL',
  'http://localhost:5101',
);

/**
 * DataServices, on 5102 — the DataWindow retrieval, validation and update
 * triple, the event chain, and the column-expression engine.
 *
 * Its primary transport is also gRPC, with a thin REST projection consumed
 * only by Gateway, so this suite reaches its behaviour through Gateway rather
 * than through this address. The address is here for the health probe.
 */
export const DATASERVICES_BASE_URL: string = resolveBaseUrl(
  'DATASERVICES_BASE_URL',
  'http://localhost:5102',
);

/**
 * Security, on 5104 — the sole token issuer, reached over REST.
 *
 * This is the one non-Gateway address the suite calls functionally rather
 * than only probing: token issuance and the published verification material
 * live here (contract C-01), and they are plain HTTP precisely so that a
 * consumer's stock bearer handler can self-configure with no bespoke code.
 */
export const SECURITY_BASE_URL: string = resolveBaseUrl(
  'SECURITY_BASE_URL',
  'http://localhost:5104',
);

/**
 * Gateway, on 5105 — the composition root and the sole ingress.
 *
 * Every functional workflow in this suite enters here. The topology is
 * layered and acyclic and nothing but an external client calls Gateway, so a
 * suite that enters through Gateway exercises the real ingress instead of
 * reaching around it.
 *
 * ⚠ INTENTIONAL DUPLICATION — CHANGE BOTH TOGETHER ⚠
 *
 * The variable name `GATEWAY_BASE_URL` and the default `http://localhost:5105`
 * are duplicated verbatim in `tests/e2e/playwright.config.ts`, which resolves
 * `use.baseURL` from the same variable with the same inlined default. That
 * config deliberately does **not** import from this folder: coupling the
 * runner's bootstrap to test data would make the config depend on a module it
 * cannot verify, and the runner must load even when the fixtures layer does
 * not. The two are therefore kept consistent **by review, not by the
 * compiler** — if either the variable name or the default string is ever
 * changed, it must be changed in both files in the same edit. Verified equal
 * character-for-character at the time of writing.
 */
export const GATEWAY_BASE_URL: string = resolveBaseUrl(
  'GATEWAY_BASE_URL',
  'http://localhost:5105',
);

// ---------------------------------------------------------------------------
// Endpoint paths
//
// These strings are the published contract surface recorded in
// `docs/CONTRACTS.md` and `docs/ARCHITECTURE.md`, which are jointly the
// authority for them. Each constant below was checked against those two files
// character by character, including every leading slash and the leading dot in
// the well-known paths. Do not adjust one of these to make a test pass — if an
// implementation and a path here disagree, one of them contradicts the
// published contract and that is the thing to resolve.
// ---------------------------------------------------------------------------

/**
 * The readiness probe — contract C-10.
 *
 * **Anonymous on all four services.** It has to be: whatever probes it holds
 * no token, and requiring one would make readiness depend on the very service
 * being probed. Gateway's response aggregates its three upstreams and names
 * each one with its individual state, so an operator can tell which upstream
 * is unready rather than reading a single opaque verdict.
 *
 * This constant is spelled once and composed into every `healthUrl` in the
 * service table below, so there is exactly one place where the health path
 * exists in this suite.
 */
export const HEALTH_PATH: string = '/health';

/**
 * The authenticated probe endpoint — contract C-10.
 *
 * **Requires a JWT and returns `401` without one.** That is the whole point
 * of it: it is the standing proof that every newly created boundary is
 * authenticated, which is why it exists on all four services rather than only
 * at the ingress (AAP §0.3.2.2).
 *
 * This suite drives it through Gateway, because Gateway on 5105 is the
 * suite's sole functional base URL. The other three services expose it too,
 * and a spec may probe any of them by composing this path onto the relevant
 * base URL above.
 */
export const PING_PATH: string = '/v1/ping';

/**
 * Gateway's projection of the framework's eight-bit capability gate —
 * contract C-09. Requires a token.
 *
 * The gate is the legacy framework's own module-gating bitmask, exposed as
 * configuration rather than hard-wired. Only the path lives here: the
 * authoritative bit values are transcribed in the sibling `capability-flags.ts`,
 * and a spec should assert against those rather than inlining a number.
 */
export const CAPABILITIES_PATH: string = '/v1/capabilities';

/**
 * The prefix of Gateway's REST projection of contracts C-03 and C-04 —
 * contract C-09. Requires a token.
 *
 * A prefix rather than a whole path, because the projection covers several
 * operations beneath it and each spec appends its own segment.
 *
 * The status mapping is the substantive part of this projection, and one row
 * of it matters more than the rest: an optimistic-concurrency mismatch
 * arrives from the gRPC contract as `Aborted` and surfaces here as **HTTP
 * `409`**, carrying a structured conflict detail with the current row state.
 * A caller retries or surfaces; there is no silent overwrite anywhere in the
 * system, so a spec asserting on a stale update must assert `409` and must
 * never be made to pass by retrying.
 *
 * Note that the two streaming methods of C-03 and the two inverted streams of
 * C-04 are deliberately not projected as REST, so they are not reachable
 * through this prefix. That is a recorded gap in the projection rather than an
 * oversight: a bidirectional ordered event chain with a per-message veto has
 * no faithful REST representation, and a partial projection would leave a
 * consumer unable to know what it had missed.
 */
export const DATAWINDOW_PATH_PREFIX: string = '/v1/datawindow';

/**
 * Token issuance on Security — contract C-01, `POST`, caller identity
 * required.
 *
 * Issues a short-lived service token from a caller identity, an audience and
 * a scope set. **Security is the sole token issuer**; no other service mints,
 * so this path is meaningful only against {@link SECURITY_BASE_URL}.
 */
export const TOKEN_PATH: string = '/v1/tokens';

/**
 * The JSON Web Key Set on Security — contract C-01, `GET`, **anonymous**.
 *
 * Publishes verification material, and verification material only. This is
 * what lets the other three services validate inbound tokens with a stock
 * bearer handler and no bespoke retrieval code, which is the reason Security
 * speaks REST rather than gRPC.
 */
export const JWKS_PATH: string = '/.well-known/jwks.json';

/**
 * OpenID discovery metadata on Security — contract C-01, `GET`, **anonymous**.
 *
 * Lets a consumer's stock bearer handler self-configure. A spec asserting on
 * it is asserting that the framework-supplied security path stays framework
 * code rather than becoming hand-written code in three services.
 */
export const OIDC_DISCOVERY_PATH: string = '/.well-known/openid-configuration';

// ---------------------------------------------------------------------------
// The service endpoint table
// ---------------------------------------------------------------------------

/**
 * The four services implemented in this phase.
 *
 * Exactly four, and these four. The roster is deliberately closed: no key
 * exists for any deferred capability area, so a spec cannot address one by
 * accident and a reviewer can see at a glance that none is addressable
 * (C-D).
 */
export type ServiceKey = 'gateway' | 'security' | 'dataservices' | 'persistence';

/**
 * Everything the suite knows about one service.
 *
 * Every member is `readonly`, and the table below is frozen, so a spec cannot
 * mutate shared state that a later spec then reads. That matters more here
 * than it usually would: the runner executes with a single worker and no
 * retries because the mutating workflows share row state, so a spec that
 * quietly rewrote an address would corrupt every spec after it in the same
 * process.
 */
export interface ServiceEndpoint {
  /** The stable identifier used as the table key and in readiness reporting. */
  readonly key: ServiceKey;

  /** Human-readable name, for assertion messages and readiness output. */
  readonly displayName: string;

  /**
   * The .NET project that implements this service, exactly as it is named in
   * `docs/ARCHITECTURE.md` §4.1. Carried so that a failing readiness probe can
   * name the project an operator has to go and look at, rather than only a
   * port.
   */
  readonly projectName: string;

  /**
   * The published port from the 5101–5105 band. Informational for reporting:
   * requests are issued against {@link baseUrl}, which an environment
   * override may repoint entirely.
   */
  readonly port: number;

  /** Resolved base URL, with no trailing slash. */
  readonly baseUrl: string;

  /** Convenience composition of {@link baseUrl} and {@link HEALTH_PATH}. */
  readonly healthUrl: string;
}

/**
 * Completes one table entry by composing its health URL.
 *
 * Exists so that {@link HEALTH_PATH} is spelled in exactly one place rather
 * than four times across the table. A named-member argument is used rather
 * than positional parameters because three of the five members are strings,
 * and positional strings are the kind of thing that gets transposed silently.
 *
 * @param service the entry's authored members, health URL excluded
 * @returns a frozen, complete endpoint record
 */
function describeService(service: Omit<ServiceEndpoint, 'healthUrl'>): ServiceEndpoint {
  return Object.freeze({
    ...service,
    healthUrl: `${service.baseUrl}${HEALTH_PATH}`,
  });
}

/**
 * The address table — the single source of truth this module exists to be.
 *
 * Keyed rather than an array so a spec can name the service it means, and
 * frozen so it cannot be mutated. The port, display name and project name of
 * each entry are transcribed from `docs/ARCHITECTURE.md` §4.1.
 */
export const SERVICE_ENDPOINTS: Readonly<Record<ServiceKey, ServiceEndpoint>> = Object.freeze({
  persistence: describeService({
    key: 'persistence',
    displayName: 'Persistence',
    projectName: 'PowerFramework.Persistence',
    port: 5101,
    baseUrl: PERSISTENCE_BASE_URL,
  }),

  dataservices: describeService({
    key: 'dataservices',
    displayName: 'DataServices',
    projectName: 'PowerFramework.DataServices',
    port: 5102,
    baseUrl: DATASERVICES_BASE_URL,
  }),

  security: describeService({
    key: 'security',
    displayName: 'Security',
    projectName: 'PowerFramework.Security',
    port: 5104,
    baseUrl: SECURITY_BASE_URL,
  }),

  gateway: describeService({
    key: 'gateway',
    displayName: 'Gateway',
    projectName: 'PowerFramework.Gateway',
    port: 5105,
    baseUrl: GATEWAY_BASE_URL,
  }),
});

/**
 * All four service keys in ascending port order: Persistence, DataServices,
 * Security, Gateway.
 *
 * The order is meaningful, not incidental. It is the order a readiness walk
 * should probe in, because it runs from the deepest dependency outwards to the
 * ingress — and Gateway reports healthy only after the other three do, so
 * probing Gateway first would report a failure whose real cause is one of the
 * services behind it.
 */
export const ALL_SERVICE_KEYS: readonly ServiceKey[] = Object.freeze([
  'persistence',
  'dataservices',
  'security',
  'gateway',
] as const);

/**
 * The three upstreams Gateway's health response aggregates — contract C-10.
 *
 * `/health` is anonymous on all four services, and **Gateway reports healthy
 * only after Persistence, DataServices and Security report healthy**. That
 * ordering is expressed in the orchestration manifest as a `depends_on`
 * dependency with a `service_healthy` condition, and `docs/ARCHITECTURE.md`
 * §4.2 records it as the most important readiness property in the
 * orchestration — it is the specific requirement that ruled out the
 * alternative orchestration approach.
 *
 * Gateway itself is deliberately **not** a member: the health-aggregation
 * spec iterates this array to check that each named upstream appears in
 * Gateway's aggregate, and including Gateway would have it assert that Gateway
 * reports on itself.
 */
export const GATEWAY_HEALTH_AGGREGATION_UPSTREAMS: readonly ServiceKey[] = Object.freeze([
  'persistence',
  'dataservices',
  'security',
] as const);

// ---------------------------------------------------------------------------
// Join helpers
// ---------------------------------------------------------------------------

/**
 * Joins a base URL and a path without ever producing a double slash.
 *
 * Shared by the two exported helpers below. Both operands are already
 * normalised in practice — the base URLs have their trailing slash stripped on
 * resolution and every path constant in this module carries a leading slash —
 * so this is belt and braces for a caller that passes a literal of its own.
 *
 * @param baseUrl a base URL, with or without a trailing slash
 * @param path a path, with or without a leading slash
 * @returns the two joined by exactly one `/`
 */
function joinUrl(baseUrl: string, path: string): string {
  const base: string = baseUrl.endsWith('/') ? baseUrl.slice(0, -1) : baseUrl;

  if (path.length === 0) {
    return base;
  }

  return path.startsWith('/') ? `${base}${path}` : `${base}/${path}`;
}

/**
 * Builds an absolute URL against Gateway, the sole ingress.
 *
 * Most specs will not need this: `playwright.config.ts` sets `use.baseURL` to
 * the same Gateway address, so a request issued with a relative path already
 * resolves against Gateway. Use this where an absolute URL is genuinely
 * required — for instance when a single assertion compares a Gateway address
 * with another service's address, and leaving one of the two relative would
 * make the comparison unreadable.
 *
 * @param path a path such as {@link PING_PATH}, with or without a leading
 *             slash; an empty string yields the base URL unchanged
 * @returns the absolute URL
 */
export function gatewayUrl(path: string): string {
  return joinUrl(GATEWAY_BASE_URL, path);
}

/**
 * Builds an absolute URL against Security, the sole token issuer.
 *
 * This one earns its place rather than mirroring the Gateway helper for
 * symmetry's sake. Security is the only service other than Gateway that this
 * suite calls functionally, and every one of those calls is an absolute URL
 * against a non-default base — token issuance and the two anonymous
 * publication paths of contract C-01. Without this helper the authentication
 * fixture would rebuild the same concatenation at each call site, which is the
 * duplication this module exists to remove.
 *
 * @param path a path such as {@link TOKEN_PATH}, {@link JWKS_PATH} or
 *             {@link OIDC_DISCOVERY_PATH}, with or without a leading slash; an
 *             empty string yields the base URL unchanged
 * @returns the absolute URL
 */
export function securityUrl(path: string): string {
  return joinUrl(SECURITY_BASE_URL, path);
}

