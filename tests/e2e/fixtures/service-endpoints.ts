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
 *   5101  Persistence   PowerFramework.Persistence   https, Http1AndHttp2 — gRPC (C-05..C-08)
 *                                                      plus REST /health and /v1/ping
 *   5102  DataServices  PowerFramework.DataServices  https, Http1AndHttp2 — gRPC (C-03, C-04)
 *                                                      plus /health, /v1/ping, thin projection
 *   5104  Security      PowerFramework.Security      https, Http1AndHttp2, AllowCertificate —
 *                                                      the sole token issuer
 *   5105  Gateway       PowerFramework.Gateway       http — REST + OpenAPI, the sole ingress
 *
 * ONE PORT PER SERVICE, AND THERE IS NO SECOND LISTENER ANYWHERE. Each service
 * declares exactly one Kestrel endpoint, and the three that serve gRPC or
 * terminate a client-certificate handshake do so over TLS with both protocol
 * versions enabled, so ALPN selects the version per connection and the one port
 * carries the gRPC contracts and this suite's HTTP/1.1 `fetch` probes together.
 * An earlier form of this comment noted a parallel h2c band beside the map; that
 * band was **withdrawn**, because it contradicted the fixed port map and left
 * every caller holding two addresses for one service to keep in step. A `fetch`
 * speaks HTTP/1.1 and is served on the same port a gRPC caller dials.
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
 * therefore free of module-scope side effects: it performs no `await`, no
 * `fetch`, no request, no file read and no `process.exit`, and it asserts
 * nothing about reachability. Every environment variable has a working
 * default, so the collection-only run — which sets none of them — resolves
 * all four addresses from those defaults and validates nothing.
 *
 * The one thing this module will do at module scope is **refuse a value that
 * is configured but structurally unusable**, by throwing. That is not a side
 * effect and it does not compromise the property above: it is unreachable
 * unless an operator has explicitly set a variable to something no service
 * could be reached at, and in that case stopping immediately with the
 * variable named is the only useful behaviour. Silently substituting a
 * default would point the suite at a stack nobody asked for and let a green
 * run attest to a system that was never exercised. The rules are stated in
 * full on `resolveBaseUrl` below, and the sibling `playwright.config.ts`
 * already applies the same posture to the ingress URL.
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
 * Resolves one base URL from the environment, falling back to a default, and
 * refuses to resolve a value that is configured but unusable.
 *
 * All four base URLs below resolve through this one helper, so the rules are
 * uniform across every service rather than strict for the ingress and lax for
 * the other three. In order:
 *
 * 1. An **unset** variable yields the default. Nothing is validated, because
 *    nothing was configured; the defaults are authored correct.
 * 2. A **set** variable is trimmed, because a value arriving from an
 *    environment file or a shell export frequently carries stray whitespace.
 * 3. A value that is **empty once trimmed** is a fatal misconfiguration and
 *    throws. It is not silently treated as absent — see below.
 * 4. The value must be an **absolute URL**, and its scheme must be `http:` or
 *    `https:`. Anything else throws.
 * 5. The value must not carry **credentials, a query string or a fragment**.
 *    Any of the three throws.
 * 6. Every trailing `/` is stripped, so that a caller may concatenate a
 *    leading-slash path onto the result without ever producing a double
 *    slash, and so that two spellings of the same address normalise to one
 *    string. This is deterministic: `http://h:1//` and `http://h:1` yield the
 *    identical result.
 *
 * WHY RULE 3 THROWS RATHER THAN FALLING BACK. An earlier form of this module
 * treated a blank value as absent, on the reasoning that the module must stay
 * importable under the collection-only invocation. Silently substituting a
 * default for a value an operator deliberately set is the worst of the
 * available behaviours: it points the suite at a stack nobody asked for, and
 * a green run then attests to a system that was never exercised. The blank
 * value itself is the evidence that something upstream — an unpopulated
 * environment file, a typo in a variable name, a substitution that produced
 * nothing — is broken, and swallowing it discards that evidence.
 *
 * IMPORT-SAFETY IS PRESERVED, NOT TRADED AWAY. The collection-only run sets
 * no service variable at all, so all four constants take rule 1 and nothing
 * is validated and nothing throws: the module stays importable with no stack
 * running, which is the property C-L actually requires. A throw is reachable
 * only when an operator has explicitly configured a structurally unusable
 * value, which is precisely the case that must stop the run. The sibling
 * `playwright.config.ts` already behaves this way for the Gateway URL and is
 * evaluated first, so for that one variable the run ends there; these rules
 * extend the same posture to the other three, which previously had none.
 *
 * NO MESSAGE ECHOES THE CONFIGURED VALUE. A rejected address may carry
 * credentials — rule 5 exists precisely because one can — and a diagnostic
 * that quoted it would write them into the console and into whatever
 * collects it, reintroducing through the error message the leak the rule
 * exists to prevent. A validator cannot know which of its inputs is
 * sensitive, so none is quoted: the variable name is what an operator needs
 * in order to find the offending setting, and the parsed scheme is quoted
 * where relevant because it is a fixed token that carries nothing.
 *
 * @param variableName the environment variable to read, named exactly as it
 *                     appears in the orchestration environment file
 * @param fallback the default used when the variable is unset; it is returned
 *                 as authored and needs no normalisation
 * @returns an absolute http/https base URL with no trailing slash
 * @throws Error when the variable is set to a blank, unparseable,
 *         non-http/https, credential-bearing, query-bearing or
 *         fragment-bearing value
 */
function resolveBaseUrl(variableName: string, fallback: string): string {
  const configured: string | undefined = process.env[variableName];

  if (configured === undefined) {
    return fallback;
  }

  const trimmed: string = configured.trim();

  if (trimmed.length === 0) {
    throw new Error(
      `${variableName} is set but empty. Unset it to use the default service ` +
        'address, or set it to an absolute http/https URL.',
    );
  }

  let parsed: URL;
  try {
    parsed = new URL(trimmed);
  } catch {
    throw new Error(
      `${variableName} is not a valid absolute URL. Expected a value such as ` +
        'http://host:port. The configured value is deliberately not quoted ' +
        'here, because a rejected address may carry a credential.',
    );
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error(
      `${variableName} must use the http or https scheme, got ` +
        `"${parsed.protocol}".`,
    );
  }

  if (parsed.username.length > 0 || parsed.password.length > 0) {
    throw new Error(
      `${variableName} must not embed credentials in the address. Remove the ` +
        '"user:password@" portion: every service in this system is reached ' +
        'with a bearer token issued by the Security service, and an address ' +
        'carrying credentials would leak them into logs and reports.',
    );
  }

  if (parsed.search.length > 0) {
    throw new Error(
      `${variableName} is a base address and must not carry a query string. ` +
        'Per-request paths are composed onto it, so a query on the base ' +
        'would be dropped rather than merged.',
    );
  }

  if (parsed.hash.length > 0) {
    throw new Error(
      `${variableName} is a base address and must not carry a fragment. A ` +
        'fragment is never sent to a server, so one here can only be a ' +
        'mistake.',
    );
  }

  return stripTrailingSlashes(trimmed);
}

/**
 * Removes every trailing `/` from an address.
 *
 * Deterministic by design, and that is the whole point of it being separate:
 * two spellings of one address must normalise to one string, so that a
 * `baseUrl + path` concatenation cannot produce a double slash for one
 * operator and not for another. An earlier form of this module stripped
 * exactly one slash, on the reasoning that leaving a second one visible in
 * the request URL surfaced the mistake rather than hiding it. In practice it
 * produced two different normalised forms for the same intended address, and
 * the "surfaced" mistake was a 404 whose cause was a doubled separator — a
 * worse diagnostic than the value simply working.
 *
 * @param value an address that has already been validated
 * @returns the same address with no trailing `/`
 */
function stripTrailingSlashes(value: string): string {
  let end: number = value.length;

  while (end > 0 && value.charAt(end - 1) === '/') {
    end -= 1;
  }

  return value.slice(0, end);
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
 *
 * **The scheme is `https`, and that is functional rather than a hardening
 * preference.** This service serves its gRPC contracts *and* the HTTP/1.1
 * `/health` this suite probes on the one port the port map assigns it. Kestrel
 * cannot do that on a cleartext listener: configured for both protocol versions
 * without TLS it disables HTTP/2 outright and logs that it is doing so, and
 * configured for HTTP/2 alone it answers an HTTP/1.1 probe with `400`. With TLS
 * the ambiguity does not arise, because ALPN selects the version per connection —
 * so this suite's HTTP/1.1 probe and the service's gRPC callers share port 5101
 * without a second, undeclared port existing anywhere.
 *
 * A consequence for a local run: the listener presents the local ASP.NET Core
 * development certificate unless the orchestration layer mounts one, so trust it
 * once with `dotnet dev-certs https --trust`. `playwright.config.ts` keeps
 * `ignoreHTTPSErrors` **false** deliberately — an untrusted certificate is a real
 * finding about the stack, not noise to suppress.
 */
export const PERSISTENCE_BASE_URL: string = resolveBaseUrl(
  'PERSISTENCE_BASE_URL',
  'https://localhost:5101',
);

/**
 * DataServices, on 5102 — the DataWindow retrieval, validation and update
 * triple, the event chain, and the column-expression engine.
 *
 * Its primary transport is also gRPC, with a thin REST projection consumed
 * only by Gateway, so this suite reaches its behaviour through Gateway rather
 * than through this address. The address is here for the health probe.
 *
 * **The scheme is `https` for exactly the reason given on
 * {@link PERSISTENCE_BASE_URL}** — one port carrying both gRPC over HTTP/2 and
 * REST over HTTP/1.1 requires ALPN, and ALPN requires TLS.
 */
export const DATASERVICES_BASE_URL: string = resolveBaseUrl(
  'DATASERVICES_BASE_URL',
  'https://localhost:5102',
);

/**
 * Security, on 5104 — the sole token issuer.
 *
 * This is the one non-Gateway address the suite calls functionally rather
 * than only probing: token issuance and the published verification material
 * live here (contract C-01).
 *
 * ⚠ THE DEFAULT IS `https`, IN EVERY ENVIRONMENT, AND THE SCHEME IS FUNCTIONAL ⚠
 *
 * Two independent reasons, either of which alone would settle it, and neither of
 * which is a hardening preference:
 *
 *   1. `POST /v1/tokens` authenticates its caller with a **client certificate**
 *      (see {@link TOKEN_PATH}), and mutual TLS *is* TLS — a client certificate
 *      cannot be requested, presented or validated on a plaintext listener at
 *      all. Over `http` the issuance operation would have no caller
 *      authentication available whatsoever, so it would not be weakly
 *      authenticated but **uncallable**, and no service could obtain its first
 *      token.
 *   2. The key set at `/.well-known/jwks.json` is the system's **trust
 *      bootstrap**. Fetched in the clear it is substitutable on path: an attacker
 *      who replaces it makes all three verifiers accept tokens the attacker
 *      signed, each of them behaving exactly as designed while doing it.
 *
 * All three sides now agree, which is what makes this a default rather than an
 * assumption. `services/security-service/PowerFramework.Security/appsettings.json`
 * declares exactly ONE Kestrel endpoint — `https://+:5104`, `Http1AndHttp2`,
 * `ClientCertificateMode: AllowCertificate` — in the base file, so it applies to
 * every environment including Development;
 * `shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` publishes
 * `https://localhost:5104` as its single canonical server; and
 * `docs/ARCHITECTURE.md` §4.1 records the same listener. An earlier form of this
 * constant defaulted to `http` on the reasoning that the service had no TLS
 * listener to reach — which was true of a configuration that has since been
 * **withdrawn**, along with the development-only cleartext `/health` listener that
 * accompanied it. Defaulting to `http` now would name a listener nobody binds.
 *
 * `AllowCertificate` rather than `RequireCertificate` is why this suite can still
 * probe `/health` and read the key set with no certificate at all: Kestrel
 * *requests* a certificate during the handshake and hands whatever it gets to the
 * application, so a caller presenting none still completes the handshake and the
 * per-operation authorization on the token path is what refuses it. Requiring one
 * at the listener would abort the handshake and take the anonymous probe with it.
 *
 * Persistence and DataServices are `https` too, for the unrelated
 * protocol-multiplexing reason recorded on {@link PERSISTENCE_BASE_URL}; Gateway
 * alone stays on `http`, because it is the published ingress whose documented
 * access URL is `http://localhost:5105` and it serves REST only, so no version
 * negotiation arises there. Those are differences in what each edge carries, not
 * an inconsistency.
 *
 * A consequence for a local run, the same one {@link PERSISTENCE_BASE_URL}
 * carries: the listener presents the local ASP.NET Core development certificate
 * unless the orchestration layer mounts one, so trust it once with
 * `dotnet dev-certs https --trust`. `playwright.config.ts` keeps
 * `ignoreHTTPSErrors` **false** deliberately.
 *
 * One consequence to be honest about: a TLS handshake happening is not the same
 * as a client certificate being presented. Unless a spec supplies one, the
 * mutual-TLS caller authentication on `POST /v1/tokens` is negotiated but not
 * exercised; see {@link SECURITY_CLIENT_CERTIFICATE} for how a spec supplies it.
 *
 * Nothing here restricts the scheme: `SECURITY_BASE_URL` accepts `http` exactly
 * as it accepts `https`, so a deployment that genuinely terminates TLS elsewhere
 * states that in one environment variable and no code change. What this module
 * will not do is *default* to a listener the repository does not declare.
 */
export const SECURITY_BASE_URL: string = resolveBaseUrl(
  'SECURITY_BASE_URL',
  'https://localhost:5104',
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
 *
 * The **validation rules** are duplicated too, and deliberately so. Both
 * files reject a blank, unparseable, non-http/https, credential-bearing,
 * query-bearing or fragment-bearing value, both normalise trailing slashes
 * the same way, and neither echoes the configured value in a diagnostic. They
 * were equal in behaviour when written, and a change to one is a change to
 * both. The config is evaluated first, so for this one variable its copy is
 * what an operator will actually see fire.
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
 * Token issuance on Security — contract C-01, `POST`, **mutual TLS only**.
 *
 * Issues a short-lived service token from a caller identity, an audience and
 * a scope set. **Security is the sole token issuer**; no other service mints,
 * so this path is meaningful only against {@link SECURITY_BASE_URL}.
 *
 * **This is the single mutual-TLS edge in the system, and it is the only
 * operation in the contract a bearer token cannot protect** — a caller cannot
 * present a token in order to obtain its first token. The OpenAPI definition
 * declares a `mutualTLS` scheme and applies it here as an override of the
 * document-level bearer requirement, so the identity that is honoured is the
 * one the presented **client certificate** establishes. The request body
 * carries no credential of any kind, and `additionalProperties: false` means
 * one cannot be added.
 *
 * Two statuses are therefore part of the contract rather than implementation
 * detail, and a spec asserting on them is asserting the boundary is
 * authenticated:
 *
 * * `401` — no client certificate presented, or the certificate is not
 *   trusted. There is no bearer-token alternative to fall back to.
 * * `403` — the certificate is trusted but the caller is not permitted the
 *   requested subject or audience; in particular the claimed `subject` does
 *   not match the identity the certificate establishes.
 *
 * See {@link SECURITY_CLIENT_CERTIFICATE} for how a spec supplies the
 * certificate, and why the local plain-http bring-up needs none.
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
// The client certificate for the one mutual-TLS edge
// ---------------------------------------------------------------------------

/**
 * A client certificate for {@link TOKEN_PATH}, resolved from the environment.
 *
 * WHY THIS EXISTS AT ALL
 * ----------------------
 * `POST /v1/tokens` is protected by mutual TLS and by nothing else, so a spec
 * that calls it must present a client certificate or be refused with `401`.
 * Without this resolver the suite would have no way to exercise the issuance
 * edge as it is actually specified — which is precisely the gap between the
 * contract and the test that lets an authentication requirement rot unnoticed.
 *
 * THIS APPLIES ON EVERY TOPOLOGY, INCLUDING THE LOCAL BRING-UP
 * ------------------------------------------------------------
 * Security declares exactly ONE listener — `https://+:5104`, `Http1AndHttp2`,
 * `ClientCertificateMode` `AllowCertificate` — in its **base** settings file, so
 * it applies to Development as well, and there is no cleartext port anywhere.
 * `AllowCertificate` means Kestrel requests a certificate without demanding one,
 * so `/health`, `/v1/ping`, the crypto operations and the two anonymous documents
 * are reachable on that listener with none, while `POST /v1/tokens` enforces the
 * requirement **per operation** and refuses a caller without one. So there is no
 * address — local or deployed — at which a token is minted without a client
 * certificate, and this suite must never be written as though the local
 * bring-up were the exception. See `docs/ARCHITECTURE.md` §4.1 and §9.3.1.
 *
 * WHAT IT DOES *NOT* DO
 * ---------------------
 * It resolves **paths**, never material. No certificate, key, passphrase or any
 * other credential appears in this file or anywhere else in this suite; the
 * files these paths point at are mounted from the orchestration secret layer and
 * are not part of this repository. The passphrase, if the key needs one, is read
 * from the environment and is never defaulted, never logged and never included
 * in an assertion message.
 *
 * ABSENT IS THE COMMON CASE, AND IT IS NOT AN ERROR — IT IS A CLOSED EDGE
 * ----------------------------------------------------------------------
 * A developer who has not generated the local certificate set has nothing to
 * present, so this resolver returns `undefined`. It does so deliberately rather
 * than throwing: the readiness, capability and 401-without-a-token specs need no
 * certificate at all and a suite that refused to start without one would be
 * unrunnable for them.
 *
 * `undefined` means **the issuance edge is not exercisable in this run**. It does
 * not mean the edge is open, and it does not mean no certificate is required —
 * those are the two readings this comment exists to rule out. A spec that needs
 * a token skips itself when this is `undefined` and says so in the skip reason;
 * it never falls back to calling `POST /v1/tokens` without a certificate, which
 * would assert a behaviour the contract does not offer. `fixtures/auth.ts`
 * states the same policy in the same terms, and the two must not drift.
   *
   * With **exactly one** of the pair set, it throws instead. That state is a
   * mistake rather than a topology, and merging it into `undefined` sends an
   * operator to debug a `401` at Security that a typo in one variable name caused.
   * {@link SECURITY_CLIENT_CERTIFICATE} sets out the reasoning in full.
 *
 * Shape matches Playwright's `use.clientCertificates` entry so a config or a
 * spec can pass it through unchanged: the `origin` it applies to, a certificate
 * and key path pair, and an optional passphrase.
 */
export interface ClientCertificate {
  /**
   * The origin the certificate is presented to — Security's base URL.
   *
   * Playwright matches this against the request origin exactly, and a client
   * certificate only exists inside a TLS handshake, so in practice this is an
   * `https` origin. It is taken from {@link SECURITY_BASE_URL} rather than
   * hardcoded so that the certificate follows wherever `SECURITY_BASE_URL`
   * points; if that variable still names the plain-http loopback default then no
   * handshake occurs and the entry is inert.
   */
  readonly origin: string;

  /** Path to the PEM certificate, from `SECURITY_MTLS_CERT_PATH`. */
  readonly certPath: string;

  /** Path to the PEM private key, from `SECURITY_MTLS_KEY_PATH`. */
  readonly keyPath: string;

  /** Passphrase from `SECURITY_MTLS_KEY_PASSPHRASE`, when the key needs one. */
  readonly passphrase?: string;
}

/**
 * Reads an environment variable holding a **filesystem path**, treating blank as
 * absent.
 *
 * Trimming is the ONE normalization applied, and it is applied because a path is
 * the kind of value that legitimately cannot begin or end with whitespace:
 * values arriving from an environment file or a shell export frequently carry a
 * stray space or a trailing newline, and a value that is empty once trimmed means
 * "not set" rather than "set to empty" — rule 2 and rule 3 of
 * {@link resolveBaseUrl}.
 *
 * The path is deliberately **not resolved to an absolute one**. Playwright
 * resolves a relative `clientCertificates` path against the process working
 * directory, and resolving it here as well would produce a second, possibly
 * different absolute path depending on where the runner was launched from — one
 * more place for the same setting to mean two things.
 *
 * **This reader is for paths only.** The passphrase has its own reader
 * immediately below, and the separation is the whole point of there being two:
 * see {@link readOptionalSecret}.
 *
 * @param variableName the environment variable to read
 * @returns the trimmed value, or `undefined` when unset or blank
 */
function readOptionalPath(variableName: string): string | undefined {
  const configured: string | undefined = process.env[variableName];

  if (configured === undefined) {
    return undefined;
  }

  const trimmed: string = configured.trim();

  return trimmed.length === 0 ? undefined : trimmed;
}

/**
 * Reads an environment variable holding **secret material**, preserving every
 * character of it exactly.
 *
 * ⚠ THIS READER MUST NOT TRIM, AND THAT IS THE REASON IT EXISTS ⚠
 *
 * An earlier form of this module read the key passphrase with the path reader
 * above, so a passphrase with a leading or trailing space was silently altered
 * before it ever reached the TLS stack. The consequence is about as
 * unhelpful as a failure gets: the handshake fails with a key-decryption error,
 * which reads as *wrong key* or *corrupt file*, while the key and the file are
 * both fine and the only thing wrong is that the fixture edited the passphrase
 * on the way past. Whitespace is a legal passphrase character, and nothing here
 * is entitled to decide it was accidental.
 *
 * So the only distinction drawn is **unset versus set**. A variable that is not
 * present is absent; a variable present with any content at all is that content,
 * verbatim. A variable set to a zero-length value is treated as absent too, since
 * an empty passphrase is the same statement as "the key needs none" and passing
 * `''` through would merely move the failure downstream.
 *
 * No value read here is ever logged, echoed, defaulted or placed in a diagnostic
 * (C-F). Only the variable NAME appears in any message this module produces.
 *
 * @param variableName the environment variable to read
 * @returns the value exactly as set, or `undefined` when unset or zero-length
 */
function readOptionalSecret(variableName: string): string | undefined {
  const configured: string | undefined = process.env[variableName];

  if (configured === undefined || configured.length === 0) {
    return undefined;
  }

  return configured;
}

/**
 * The client certificate to present to Security, or `undefined` when none is
 * configured.
 *
 * ⚠ A HALF-CONFIGURED PAIR FAILS FAST — IT IS NOT TREATED AS "NO CERTIFICATE" ⚠
 *
 * An earlier form of this resolver returned `undefined` whenever either path was
 * missing, on the reasoning that a certificate without its key cannot complete a
 * handshake so half a configuration is as good as none. That reasoning holds for
 * the handshake and fails completely for the operator. The two states it merged
 * are not alike:
 *
 * - **Neither set** is the documented local bring-up. Plain http on loopback, no
 *   handshake, nothing to present. Returning `undefined` is correct, and it stays
 *   correct — a suite that refused to start without certificates would be
 *   unrunnable on the one topology the setup instructions actually document.
 * - **Exactly one set** is a mistake, every time. Nobody configures a
 *   certificate path and no key on purpose. Silently downgrading it to "no
 *   certificate" means the suite runs anonymously against an edge that is
 *   authenticated by certificate and by nothing else, so the mutual-TLS issuance
 *   call comes back `401` — and that `401` is indistinguishable from a genuine
 *   authorization defect at the issuer. The operator then debugs Security while
 *   the fault is a typo in one variable name.
 *
 * This resolver therefore throws on the second, naming both variables and which
 * one is missing. A passphrase configured with no pair is the same class of
 * mistake and is reported the same way, because a passphrase with nothing to
 * unlock cannot be anything but half-finished configuration.
 *
 * Throwing at module scope is safe here and does not compromise import-safety:
 * this evaluates during collection only when a variable is set, and the state it
 * refuses is one no run could succeed on. Nothing is thrown for the default,
 * fully-unset case, so `playwright test --list` collects with no stack running
 * and no certificates configured (C-L).
 *
 * **Names only in every message.** No path and no passphrase is echoed (C-F).
 */
export const SECURITY_CLIENT_CERTIFICATE: ClientCertificate | undefined = (():
  | ClientCertificate
  | undefined => {
  const certVariable: string = 'SECURITY_MTLS_CERT_PATH';
  const keyVariable: string = 'SECURITY_MTLS_KEY_PATH';
  const passphraseVariable: string = 'SECURITY_MTLS_KEY_PASSPHRASE';

  const certPath: string | undefined = readOptionalPath(certVariable);
  const keyPath: string | undefined = readOptionalPath(keyVariable);
  const passphrase: string | undefined = readOptionalSecret(passphraseVariable);

  if (certPath === undefined && keyPath === undefined) {
    if (passphrase !== undefined) {
      throw new Error(
        `${passphraseVariable} is set but neither ${certVariable} nor ` +
          `${keyVariable} is. A passphrase unlocks a private key, so one ` +
          'configured with no key and no certificate is half-finished ' +
          'configuration rather than an optional extra. Set both paths, or ' +
          'unset the passphrase to run against the documented plain-http ' +
          'loopback bring-up where no certificate is presented at all. (Variable ' +
          'names only — no path or passphrase is ever echoed.)',
      );
    }

    return undefined;
  }

  if (certPath === undefined || keyPath === undefined) {
    const missing: string = certPath === undefined ? certVariable : keyVariable;
    const present: string = certPath === undefined ? keyVariable : certVariable;

    throw new Error(
      `${present} is set but ${missing} is not. A client certificate and its ` +
        'private key are presented as a pair and neither is usable alone, so ' +
        'this is reported rather than silently downgraded to "no certificate": ' +
        'running anonymously against the mutual-TLS issuance edge produces a ' +
        '401 that is indistinguishable from a real authorization defect at ' +
        `Security. Set ${missing} as well, or unset ${present} to run against ` +
        'the documented plain-http loopback bring-up. (Variable names only — no ' +
        'path or passphrase is ever echoed.)',
    );
  }

  // ⚠ NOT FROZEN, AND THIS IS THE ONE VALUE IN THIS MODULE THAT MUST NOT BE ⚠
  //
  // Everything else here is frozen, so this exception needs its reason recorded
  // or it reads as an omission. `playwright.config.ts` passes this entry
  // straight into `use.clientCertificates`, and Playwright REWRITES IT IN PLACE:
  // resolveClientCerticates() in playwright/lib/index.js assigns `cert.certPath`,
  // `cert.keyPath` and `cert.pfxPath` unconditionally while resolving each
  // against the config directory. The assignment happens even for an already
  // absolute path — resolveFileToConfig returns the value unchanged, and the
  // property is still written — so a frozen entry throws
  // `TypeError: Cannot assign to read only property 'certPath'` in strict mode
  // and takes the whole run down at config load. Measured directly against
  // @playwright/test 1.62.1 with two absolute paths.
  //
  // The practical effect of freezing it was therefore that the mutual-TLS path
  // could not run AT ALL: with no certificate configured nothing touched this
  // value, and with one configured the runner crashed before the first test. It
  // stayed invisible because the only state anyone exercised was the unset one.
  //
  // Immutability is not lost so much as relocated: the members are `readonly` on
  // {@link ClientCertificate}, so a spec cannot reassign one through the type,
  // and the only writer in practice is the runner performing the path
  // resolution it is entitled to perform.
  return passphrase === undefined
    ? { origin: SECURITY_BASE_URL, certPath, keyPath }
    : { origin: SECURITY_BASE_URL, certPath, keyPath, passphrase };
})();

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
