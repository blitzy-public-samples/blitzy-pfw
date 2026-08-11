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
 *   5101  Persistence   PowerFramework.Persistence   http, Http1 — REST /health, /v1/ping
 *   5111  Persistence   PowerFramework.Persistence   http, Http2 — gRPC (C-05..C-08)
 *   5102  DataServices  PowerFramework.DataServices  http, Http1 — /health, /v1/ping, thin
 *                                                      projection
 *   5112  DataServices  PowerFramework.DataServices  https, Http2 — gRPC (C-03, C-04)
 *   5104  Security      PowerFramework.Security      https, Http1 — the sole token issuer
 *   5105  Gateway       PowerFramework.Gateway       https — REST + OpenAPI, the sole ingress
 *
 * EVERY LISTENER TERMINATES TLS, AND TWO SERVICES BIND TWO OF THEM. Every boundary
 * in this map is created by the decomposition itself, every request across one
 * carries a bearer token, and Security additionally publishes the key set the whole
 * estate verifies against — so cleartext would make every token replayable and the
 * trust bootstrap substitutable on path (CWE-319). Two endpoints per gRPC-carrying
 * service is retained rather than forced: each endpoint pins ONE protocol version,
 * so a probe and a gRPC channel each address a listener that can only answer the
 * thing it is for, and misaddressing either fails at once instead of later. Security
 * is REST-only and Gateway is the REST ingress, so each binds one.
 *
 * THE FOUR PORTS THIS SUITE USES ARE THE HTTP/1.1 ONES: 5101, 5102, 5104 and
 * 5105. A `fetch` speaks HTTP/1.1, so naming 5111 or 5112 here would produce a
 * `400` from a healthy service on every probe. The two HTTP/2 ports exist for
 * the in-estate gRPC callers — DataServices dialling Persistence, Gateway
 * dialling DataServices — and this suite never dials either directly, which is
 * why they appear in the table for orientation and nowhere else in this module.
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
 * **The scheme is `https` and the port is 5101, which is this service's HTTP/1.1
 * endpoint.** Its gRPC contracts answer on a SECOND endpoint, 5111, and the
 * reason that second endpoint exists is worth keeping even though TLS removes
 * its necessity: on a PLAINTEXT endpoint Kestrel cannot carry both protocol
 * versions at once — configured for both it disables HTTP/2 and logs that it
 * has, and configured for HTTP/2 alone it answers an HTTP/1.1 probe with `400`.
 * Under TLS, ALPN negotiates the version and one endpoint would suffice, so the
 * split is now a deliberate SEPARATION OF SURFACES rather than a workaround: the
 * REST health path and the gRPC contracts have different audiences and different
 * readiness meanings. This suite probes with `fetch`, which speaks HTTP/1.1, so
 * 5101 is the only port it may name; 5111 belongs to DataServices' generated
 * gRPC client and to nothing here.
 *
 * One consequence for a local run, stated rather than glossed: every listener in
 * this estate is TLS, so a local bring-up needs a certificate the runner trusts.
 * The remedy is `dotnet dev-certs https --trust`, or pointing these variables at
 * a deployment whose certificate already chains. `playwright.config.ts` keeps
 * `ignoreHTTPSErrors` **false** deliberately: an untrusted certificate is a real
 * finding about the stack rather than noise to suppress.
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
 * **The scheme is `https` and the port is 5102, its HTTP/1.1 endpoint**, for
 * exactly the reason given on {@link PERSISTENCE_BASE_URL}: its gRPC contracts
 * answer on a separate HTTP/2 endpoint, 5112, which Gateway's generated client
 * dials and this suite never does.
 */
export const DATASERVICES_BASE_URL: string = resolveBaseUrl(
  'DATASERVICES_BASE_URL',
  'https://localhost:5102',
);

/**
 * The default base URL for Security when `SECURITY_BASE_URL` is unset.
 *
 * Declared as its own exported constant for ONE reason: it is the value a
 * coherence assertion compares against Security's published OpenAPI server
 * entry and its Kestrel endpoint. Inlining it would leave that assertion
 * comparing the RESOLVED value, which an environment variable can legitimately
 * override — so the test would pass while the default it was written to protect
 * had drifted back to a scheme the repository does not declare.
 *
 * The scheme is `https` because Security declares exactly one Kestrel endpoint,
 * `https://+:5104`, in its BASE settings file — which means Development too —
 * and its OpenAPI document names one canonical server on the same scheme and
 * port.
 */
export const SECURITY_DEFAULT_BASE_URL: string = 'https://localhost:5104';

/**
 * Security, on 5104 — the sole token issuer.
 *
 * This is the one non-Gateway address the suite calls functionally rather
 * than only probing: token issuance and the published verification material
 * live here (contract C-01).
 *
 * ⚠ THE DEFAULT IS `https`, AND THE SCHEME IS THE ONE THE REPOSITORY BINDS ⚠
 *
 * This constant and the runtime AGREE, which is the whole point of this note, and
 * they have disagreed in BOTH directions at different times — so both superseded
 * readings are recorded below rather than left to be re-argued.
 *
 * THE ONE GENUINE TENSION, AND HOW IT IS RESOLVED (C-L against C-G). The attached
 * environment gates this service's readiness on
 * `curl -sf http://localhost:5104/health` and supplies no certificate material of
 * any kind, and AAP §0.8.3 enumerates the deviations from that contract the
 * refactor may take. That is a real argument for a cleartext listener, and it was
 * once acted on. It is outweighed, and not narrowly: this is the SOLE TOKEN
 * ISSUER, and the key set at `/.well-known/jwks.json` is the whole estate's trust
 * bootstrap. Fetched in the clear it is substitutable on path — an attacker who
 * replaces it makes all three verifiers accept tokens the attacker signed, each
 * behaving exactly as designed while doing it — and the issuance response body IS
 * a credential. A readiness command is a documentation detail that a deployment
 * can restate as
 * `curl -sf --cacert <anchor> https://localhost:5104/health`; a substitutable
 * trust bootstrap is not restatable at all. So the listener is TLS in every
 * environment and the probe command is the thing that adapts.
 *
 * WHAT AUTHENTICATES ISSUANCE. `POST /v1/tokens` accepts EITHER of two caller
 * credentials, and this matters for a local run because only one of them needs
 * certificate material: a shared secret presented as an HTTP `Basic` credential —
 * the `clientCredential` scheme in
 * `shared/PowerFramework.Contracts/OpenApi/security.v1.yaml` — or a client
 * certificate, which is the mutual-TLS fallback AAP §0.6.6.3 describes for that
 * one pair. See {@link SECURITY_CLIENT_CERTIFICATE} for how a spec supplies one.
 * Either way the operation refuses a request carrying no credential at all, so
 * the boundary is authenticated whichever a deployment chooses (C-G).
 *
 * All three sides agree, which is what makes this a default rather than an
 * assumption. `services/security-service/PowerFramework.Security/appsettings.json`
 * declares exactly ONE Kestrel endpoint — `https://+:5104`, `Http1AndHttp2`, with
 * `ClientCertificateMode: AllowCertificate` — in the base file, and the
 * Development overlay overrides that same endpoint key rather than adding a
 * second; and `docs/ARCHITECTURE.md` §4.1 records the same listener.
 *
 * Persistence and DataServices additionally bind a second endpoint for their gRPC
 * contracts, for the reason recorded on {@link PERSISTENCE_BASE_URL}. Security is
 * REST-only, so it needs no second endpoint and this suite calls it directly on
 * 5104.
 *
 * `playwright.config.ts` keeps `ignoreHTTPSErrors` **false** deliberately: an
 * untrusted certificate is a real finding about the stack rather than noise to
 * suppress. The remedy for a local one is `dotnet dev-certs https --trust`.
 *
 * One consequence to be honest about: a TLS handshake happening is not the same
 * as a client certificate being presented. Unless a spec supplies one, the
 * mutual-TLS caller authentication on `POST /v1/tokens` is negotiated but not
 * exercised, and the suite authenticates with the Basic credential instead; see
 * {@link SECURITY_CLIENT_CERTIFICATE} for how a spec supplies a certificate.
 *
 * Nothing here restricts the scheme: `SECURITY_BASE_URL` accepts `http` exactly
 * as it accepts `https`, so a deployment that terminates TLS at a proxy and
 * exposes cleartext behind it states that in one environment variable and no code
 * change. What this module will not do is *default* to a listener the repository
 * does not declare.
 *
 * ⚠ DO NOT "FIX" A LOCAL TLS PROBLEM BY MOVING THIS DEFAULT BACK TO `http` ⚠
 *
 * That change was made once and is a defect rather than a simplification, so it
 * is recorded here as a closed question rather than left to be re-argued. It
 * fails in three separate ways at once, and the first is fatal on its own:
 *
 *   1. **It names a listener nobody binds.** Security's base settings file
 *      declares exactly one Kestrel endpoint and it is `https://+:5104`. There
 *      is no cleartext port, in any environment, so every request in the suite
 *      that reaches Security fails at connect.
 *   2. **If something *did* accept it — a proxy, a hand-added listener — the
 *      minted bearer token would cross the wire in the clear** on the one
 *      request in the whole suite whose response body IS a credential. That is
 *      CWE-319 introduced by a test fixture.
 *   3. **The issuance call would be unauthenticated rather than weakly
 *      authenticated.** A client certificate cannot be requested, presented or
 *      validated on a plaintext listener at all, so `POST /v1/tokens` becomes
 *      uncallable rather than merely less safe.
 *
 * The remedy for an untrusted local certificate is `dotnet dev-certs https
 * --trust`, never `ignoreHTTPSErrors` and never a cleartext default.
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
 * The variable name `GATEWAY_BASE_URL` and the default `https://localhost:5105`
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
  'https://localhost:5105',
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
 * Two schemes establish caller identity and the operation accepts EITHER:
 * {@link SECURITY_CLIENT_CREDENTIAL}, presented as an HTTP `Basic` credential and
 * therefore workable on every topology, or
 * {@link SECURITY_CLIENT_CERTIFICATE}, reachable wherever Security itself
 * terminates the TLS handshake. Neither is optional: a request presenting nothing
 * is refused.
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
 * certificate, and `fixtures/token-issuance.ts` for the precondition that
 * decides what a run does when none has been provisioned.
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
// The two caller credentials the issuance edge accepts
// ---------------------------------------------------------------------------
//
// `POST /v1/tokens` is the one operation a bearer token cannot protect — a caller
// cannot present a token in order to obtain its first token — so it authenticates
// its caller by something other than a token, and `security.v1.yaml` publishes TWO
// schemes for it, EITHER of which satisfies the operation:
//
//   * `clientCredential` — an HTTP `Basic` credential naming a subject on
//     Security's issuance roster. Resolved by {@link SECURITY_CLIENT_CREDENTIAL}.
//     This is the PRIMARY path, because a header the operation reads itself needs
//     no cooperation from whatever terminated the TLS handshake.
//   * `mutualTls` — a client certificate, reachable only where a deployment
//     terminates TLS at Security. Resolved by
//     {@link SECURITY_CLIENT_CERTIFICATE}. This is the fallback AAP §0.6.6.3
//     describes for that one pair.
//
// A request presenting NEITHER is refused with `401`. That is the property that
// keeps this newly created boundary authenticated (C-G), and it holds on both
// topologies rather than only on the one that terminates TLS.

/**
 * A caller credential for {@link TOKEN_PATH}, as an HTTP `Basic` pair.
 *
 * The `clientId` is a **subject on Security's issuance roster**, not a free
 * label: the issuer resolves it to the roster entry that decides which audiences
 * and which scopes that caller may ask for, so a token request naming an audience
 * or a scope the entry does not permit is refused with `403` even though the
 * caller authenticated successfully.
 *
 * The `secret` is secret material and is treated as such everywhere in this
 * suite: never trimmed, never defaulted, never logged, never placed in an
 * assertion message and never written to a trace or a report (C-F).
 */
export interface ClientCredential {
  /**
   * The roster subject this credential authenticates as, from
   * `SECURITY_CLIENT_ID`.
   *
   * It must match the `subject` a token request claims. Security refuses a
   * mismatch with `403` rather than silently issuing for the authenticated
   * identity, so a spec that hardcodes one subject and configures another gets a
   * clear answer instead of a token for the wrong caller.
   */
  readonly clientId: string;

  /** The shared secret for that subject, from `SECURITY_CLIENT_SECRET`. */
  readonly secret: string;
}

/**
 * Reads an environment variable holding a **non-secret identifier**, treating
 * blank as absent.
 *
 * Trimming is applied for the same reason {@link readOptionalPath} applies it —
 * values arriving from an environment file or a shell export routinely carry a
 * stray space or a trailing newline — and it is safe here because a roster
 * subject is an identifier: `gateway-service`, `dataservices-service`. A subject
 * whose identity genuinely depended on surrounding whitespace could not be typed
 * into an environment file reliably in the first place.
 *
 * This reader is deliberately NOT used for the paired secret. Whitespace is a
 * legal secret character and {@link readOptionalSecret} exists precisely so that
 * no reader in this module edits one on the way past.
 *
 * @param variableName the environment variable to read
 * @returns the trimmed value, or `undefined` when unset or blank
 */
function readOptionalIdentifier(variableName: string): string | undefined {
  const configured: string | undefined = process.env[variableName];

  if (configured === undefined) {
    return undefined;
  }

  const trimmed: string = configured.trim();

  return trimmed.length === 0 ? undefined : trimmed;
}

/**
 * The `Basic` credential to present to Security, or `undefined` when none is
 * configured.
 *
 * ⚠ A HALF-CONFIGURED PAIR FAILS FAST — IT IS NOT TREATED AS "NO CREDENTIAL" ⚠
 *
 * The reasoning is the one {@link SECURITY_CLIENT_CERTIFICATE} sets out at
 * length, and it applies here for the same reason: the two states a permissive
 * resolver would merge are not alike.
 *
 * - **Neither set** is the documented bring-up of a checkout that has not had an
 *   issuance roster provisioned. There is nothing to present, the resolver
 *   returns `undefined`, and the specs that need no token — readiness,
 *   capabilities, and every `401`-without-a-token assertion — run unaffected. A
 *   suite that refused to start without a credential would be unrunnable for
 *   them, and `playwright test --list` would fail to collect (C-L).
 * - **Exactly one set** is a mistake, every time. Nobody configures a client id
 *   with no secret on purpose. Downgraded silently to "no credential" it sends
 *   the suite at an authenticated edge with nothing to present, the issuance call
 *   answers `401`, and that `401` is indistinguishable from a real authorization
 *   defect at the issuer — so the operator debugs Security while the fault is a
 *   typo in one variable name.
 *
 * A colon in the client id is refused for a narrower but equally concrete reason:
 * RFC 7617 encodes the pair as `id:secret` and takes everything after the FIRST
 * colon as the secret, so an id containing one silently changes which credential
 * is transmitted. Refusing it here reports the configuration error instead of
 * producing a `401` whose cause is invisible on both sides of the wire.
 *
 * **Names only in every message.** Neither the id nor the secret is echoed (C-F).
 */
export const SECURITY_CLIENT_CREDENTIAL: ClientCredential | undefined = (():
  | ClientCredential
  | undefined => {
  const idVariable: string = 'SECURITY_CLIENT_ID';
  const secretVariable: string = 'SECURITY_CLIENT_SECRET';

  const clientId: string | undefined = readOptionalIdentifier(idVariable);
  const secret: string | undefined = readOptionalSecret(secretVariable);

  if (clientId === undefined && secret === undefined) {
    return undefined;
  }

  if (clientId === undefined || secret === undefined) {
    const missing: string = clientId === undefined ? idVariable : secretVariable;
    const present: string = clientId === undefined ? secretVariable : idVariable;

    throw new Error(
      `${present} is set but ${missing} is not. An issuance credential is a ` +
        'subject and its secret presented together, and neither half is usable ' +
        'alone, so this is reported rather than silently downgraded to "no ' +
        'credential": calling POST /v1/tokens with nothing to present produces a ' +
        '401 that is indistinguishable from a real authorization defect at ' +
        `Security. Set ${missing} as well, or unset ${present} to run the specs ` +
        'that need no token. (Variable names only — neither the identifier nor ' +
        'the secret is ever echoed.)',
    );
  }

  if (clientId.includes(':')) {
    throw new Error(
      `${idVariable} contains a colon. RFC 7617 encodes a Basic credential as ` +
        'identifier:secret and reads everything after the first colon as the ' +
        'secret, so an identifier containing one transmits a different ' +
        'credential than the one configured and Security answers 401 for a ' +
        'reason visible on neither side. Use a roster subject with no colon in ' +
        'it. (Variable name only — no value is echoed.)',
    );
  }

  return Object.freeze({ clientId, secret });
})();

/**
 * Renders a {@link ClientCredential} as an `Authorization` header value.
 *
 * RFC 7617: `Basic ` followed by the base64 of the UTF-8 bytes of
 * `identifier:secret`. `Buffer` is used rather than `btoa` because `btoa`
 * operates on latin1 and would corrupt any non-ASCII byte in the secret — a
 * corruption that surfaces only as a `401`, which is the least diagnosable
 * failure this suite can produce.
 *
 * The returned value **is** the credential in transmissible form, so it is
 * subject to the same rule as the secret itself: a caller passes it to a request
 * header and nowhere else. It must never be logged, attached to a trace, or
 * included in an assertion message (C-F).
 *
 * @param credential the resolved roster subject and secret
 * @returns the complete `Authorization` header value
 */
export function basicAuthorizationHeader(credential: ClientCredential): string {
  const encoded: string = Buffer.from(
    `${credential.clientId}:${credential.secret}`,
    'utf8',
  ).toString('base64');

  return `Basic ${encoded}`;
}

/**
 * A client certificate for {@link TOKEN_PATH}, resolved from the environment.
 *
 * WHY THIS EXISTS AT ALL
 * ----------------------
 * `POST /v1/tokens` accepts a client certificate as ONE OF TWO caller
 * credentials, and this resolver is how a spec supplies it. Without it the suite
 * would have no way to exercise the mutual-TLS half of the issuance edge as it is
 * actually specified — which is precisely the gap between the contract and the
 * test that lets an authentication requirement rot unnoticed.
 *
 * THIS IS THE ALTERNATIVE, NOT THE PRIMARY PATH
 * ---------------------------------------------
 * Security declares exactly ONE listener — `https://+:5104`, `Http1` — in its
 * **base** settings file. A client certificate reaches the application only where
 * Security itself terminates the handshake AND the endpoint requests one, so behind
 * a proxy or a mesh sidecar that re-terminates the connection this entry is INERT
 * and {@link SECURITY_CLIENT_CREDENTIAL} carries the operation. Configure these
 * variables against a deployment whose Security endpoint
 * sets `ClientCertificateMode` to `AllowCertificate` — which requests a
 * certificate without demanding one, so `/health`, `/v1/ping`, the crypto
 * operations and the two anonymous documents stay reachable with none while
 * `POST /v1/tokens` enforces its requirement per operation. On no topology is a
 * token minted without a client
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
 * ABSENT MEANS THE ISSUANCE EDGE IS NOT EXERCISABLE — IT DOES NOT MEAN IT IS OPEN
 * -------------------------------------------------------------------------------
 * A developer who has not generated the local certificate set has nothing to
 * present, so this resolver returns `undefined`. It does so deliberately rather
 * than throwing: this module is imported during collection, and
 * `playwright test --list` must load with nothing configured and nothing running
 * (C-L). Deciding what a *run* does about it is therefore not this module's job.
 *
 * **That decision lives in exactly one place: `fixtures/token-issuance.ts`.** It
 * is a precondition rather than a per-spec convention, and it has two modes and
 * no third:
 *
 * - **A full acceptance run FAILS ITS SETUP** when no identity is provisioned,
 *   because a run that silently omitted every authenticated workflow while
 *   reporting green would be worse than a run that stopped. Provision one with
 *   `npm run provision:identity`.
 * - **A run that explicitly acknowledges being partial** — one environment
 *   variable, named by that module — skips the token-dependent tests with a
 *   reason that says which variables are missing, and runs the rest.
 *
 * In neither mode does anything fall back to calling `POST /v1/tokens` without a
 * certificate, which would assert a behaviour the contract does not offer.
 * `fixtures/auth.ts` and `fixtures/token-issuance.ts` state the identical
 * policy, and the three must not drift.
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
   * certificate only exists inside a TLS handshake, so this is an `https`
   * origin — which the default now is. It is taken from
   * {@link SECURITY_BASE_URL} rather than hardcoded so that the certificate
   * follows wherever `SECURITY_BASE_URL` points; an override that named a
   * cleartext origin would leave the entry inert, and that is one of the three
   * reasons recorded on {@link SECURITY_BASE_URL} for why the default is not
   * cleartext.
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
 * - **Neither set** is the documented local bring-up. The handshake still happens
 *   — Security's only listener is TLS and {@link SECURITY_BASE_URL} defaults to it
 *   — but Kestrel is configured `AllowCertificate` rather than
 *   `RequireCertificate`, so a caller presenting none completes the handshake and
 *   is refused per-operation instead. Returning `undefined` is therefore correct
 *   and stays correct: it leaves the anonymous probes and the key-set read working,
 *   and a suite that refused to start without certificates would be unrunnable on
 *   the one topology the setup instructions document.
 * - **Exactly one set** is a mistake, every time. Nobody configures a
 *   certificate path and no key on purpose. Silently downgrading it to "no
 *   certificate" means the suite presents no certificate to an edge the operator
 *   plainly intended to reach by certificate, so unless a
 *   {@link SECURITY_CLIENT_CREDENTIAL} happens to be configured as well the
 *   issuance call comes back `401` — and that `401` is indistinguishable from a
 *   genuine authorization defect at the issuer. The operator then debugs Security
 *   while the fault is a typo in one variable name. Where a credential *is*
 *   configured the outcome is worse rather than better: the run silently
 *   exercises the `Basic` path while reporting on the certificate path, so the
 *   mutual-TLS edge is recorded as covered by a run that never touched it.
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
          'configuration rather than an optional extra. Set both paths — ' +
          '`npm run provision:identity` writes an ephemeral pair and prints ' +
          'them — or unset the passphrase to run without an issuance identity ' +
          'at all. (Variable names only — no path or passphrase is ever echoed.)',
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
        `Security. Set ${missing} as well — \`npm run provision:identity\` ` +
        `writes an ephemeral pair and prints both — or unset ${present} to run ` +
        'without an issuance identity at all. (Variable names only — no path or ' +
        'passphrase is ever echoed.)',
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
