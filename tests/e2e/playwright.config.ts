/**
 * Playwright runner configuration for the PowerFramework cross-service
 * end-to-end suite.
 *
 * WHAT THIS SUITE IS
 * ------------------
 * Playwright is used here purely as an HTTP / API workflow driver. There is no
 * user interface anywhere in this phase of the decomposition, so there is no
 * page object, no rendered screen and no browser engine in play: specs drive
 * the running stack exclusively through the `request` / `APIRequestContext`
 * surface. That is not a simplification, it is the architecture — the service
 * that would own a presentation surface (DesignSystem) is deferred out of this
 * phase, and no design input of any kind exists for it. Consequently this
 * config deliberately declares no browser, and the suite needs no downloaded
 * browser binary to run, which is why the sibling `package.json` correctly
 * installs no browsers.
 *
 * WHAT IT TALKS TO
 * ----------------
 * The Gateway service is the composition root and the sole ingress, published
 * over REST with an OpenAPI description on port 5105. It is therefore this
 * suite's single base URL, and every spec issues relative paths against it.
 * The topology behind it is layered and acyclic — nothing but an external
 * client calls Gateway — so a suite that enters through Gateway exercises the
 * real ingress rather than reaching around it. The remaining services' base
 * URLs are resolved by the fixtures layer from its own environment variables;
 * they are deliberately not resolved here, because they are not the ingress
 * and only the fixtures layer needs them.
 *
 * Endpoints the suite exercises through Gateway: the anonymous `/health`
 * probe; `/v1/ping`, which requires a token and answers `401` without one;
 * `/v1/capabilities`; the `/v1/datawindow/**` projections, on which an
 * optimistic-concurrency mismatch surfaces as HTTP `409` carrying the current
 * row state; and the four reserved deferred-capability routes, which answer
 * `501 Not Implemented` with a machine-readable body. Token issuance and the
 * published verification material are reached on the Security service, which
 * is the sole issuer in the system.
 *
 * HOW IT IS RUN
 * -------------
 * From this directory, unchanged from the documented path:
 *
 *   npm ci && npx playwright test        # or: npm test
 *
 * Two gates need no running stack, and between them they are everything that
 * can be verified without one:
 *
 *   npm run typecheck                    # tsc --noEmit, strict
 *   npm run test:list                    # collect and enumerate every spec
 *   npm run verify                       # both, in that order
 *
 * THE TWO GATES DO DIFFERENT JOBS AND NEITHER SUBSTITUTES FOR THE OTHER.
 * `playwright test --list` loads and collects the spec files, so it catches a
 * syntax error, a missing module and a spec that fails at import time - and it
 * performs NO TYPE CHECKING WHATSOEVER, because the runner transpiles each
 * file with Babel, which strips types without reading them. A misspelled
 * fixture export, a string passed where a number is required, or an ignored
 * possibly-undefined value all survive collection intact. Only `tsc --noEmit`
 * reads the types, and it is configured by the sibling `tsconfig.json` with
 * the strict family plus the additional checks documented there.
 *
 * The stack the specs run against is brought up separately and beforehand, by
 * the one orchestration manifest under `orchestration/`. This config never
 * starts anything; see the `webServer` note below.
 */
import { defineConfig } from '@playwright/test';

import { SECURITY_CLIENT_CERTIFICATE } from './fixtures/service-endpoints';

/**
 * The Gateway composition root, overridable for a non-default host or port.
 *
 * The default matches the documented access URL for the composition root, so
 * the suite runs against a stock local bring-up with no environment set at
 * all.
 *
 * Resolution is inlined here rather than imported, and that is still a
 * deliberate choice even though this file now imports one symbol from the
 * fixtures layer. The base URL is what the runner needs in order to start at
 * all, so keeping its resolution local means a fault in the fixtures layer
 * surfaces as failing specs rather than as a config that will not load. The one
 * import above is confined to `use.clientCertificates`, which only the config
 * can populate and which no amount of duplication here could resolve without
 * also duplicating Security's own base-URL resolution — two resolvers copied
 * instead of one value imported. The `fixtures/service-endpoints.ts` module is
 * deliberately free of side effects at import time and reads only the
 * environment, so importing it cannot start anything or fail on a missing
 * service.
 */
const resolvedGatewayBaseUrl = process.env['GATEWAY_BASE_URL'] ?? 'http://localhost:5105';

/**
 * Fail fast on a structurally unusable base URL rather than degrade.
 *
 * An empty or malformed `GATEWAY_BASE_URL` — easy to produce with a blank
 * assignment in an environment file — would otherwise surface much later as a
 * confusing per-request failure that looks like a service fault. The framework
 * being migrated treats a structural fault as fatal rather than something to
 * continue past, and this suite keeps that posture: a bad configuration stops
 * the run immediately, with a message that names the variable at fault.
 *
 * SIX RULES, and the last three are about the value being a BASE address
 * rather than an arbitrary URL. `use.baseURL` has per-request paths composed
 * onto it, so three components that parse perfectly well are nevertheless
 * wrong here:
 *
 * - **Credentials** (`http://user:secret@host:5105`). Rejecting them is a
 *   secrets control rather than tidiness. Playwright records the request URL
 *   in traces, reports and failure messages, so a credential embedded in the
 *   base address is a credential written into every artifact the run
 *   produces — and the suite has no use for one, because it authenticates
 *   with a bearer token obtained from the Security service.
 * - **A query string.** It is dropped rather than merged when a path is
 *   composed onto the base, so a caller believing it had configured one would
 *   be wrong with no diagnostic at all.
 * - **A fragment.** Never transmitted, so one here can only be a mistake.
 *
 * NO MESSAGE ECHOES THE CONFIGURED VALUE. An earlier form of this function
 * quoted the rejected value in its unparseable-URL message, which would print
 * a credential-bearing address into the console and into whatever collects the
 * run output — reintroducing through the error message the leak the credential
 * rule exists to prevent. The variable name is what an operator needs; the
 * parsed scheme is quoted where relevant because it is a fixed token that
 * carries nothing.
 *
 * TRAILING SLASHES ARE STRIPPED, all of them, so that two spellings of one
 * address normalise to one string and a `baseURL` + leading-slash-path
 * concatenation can never produce a doubled separator.
 *
 * These rules are duplicated in `fixtures/service-endpoints.ts`, which applies
 * them to all four service addresses. The duplication is deliberate — see the
 * note on `resolvedGatewayBaseUrl` above for why this config imports nothing
 * from the fixtures layer — so a change to one is a change to both.
 *
 * @param candidate the resolved base URL, from the environment or the default
 * @returns the same value, normalised, once proven usable as a base address
 * @throws Error when the value is blank, unparseable, not http/https, or
 *         carries credentials, a query string or a fragment
 */
function assertUsableBaseUrl(candidate: string): string {
  const trimmed = candidate.trim();

  if (trimmed.length === 0) {
    throw new Error(
      'GATEWAY_BASE_URL is set but empty. Unset it to use the default ' +
        'composition-root URL, or set it to an absolute http/https URL.',
    );
  }

  let parsed: URL;
  try {
    parsed = new URL(trimmed);
  } catch {
    throw new Error(
      'GATEWAY_BASE_URL is not a valid absolute URL. Expected a value such ' +
        'as http://host:port. The configured value is deliberately not quoted ' +
        'here, because a rejected address may carry a credential.',
    );
  }

  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
    throw new Error(
      `GATEWAY_BASE_URL must use the http or https scheme, got "${parsed.protocol}".`,
    );
  }

  if (parsed.username.length > 0 || parsed.password.length > 0) {
    throw new Error(
      'GATEWAY_BASE_URL must not embed credentials in the address. Remove ' +
        'the "user:password@" portion: the suite authenticates with a bearer ' +
        'token obtained from the Security service, and Playwright records ' +
        'request URLs in traces and reports.',
    );
  }

  if (parsed.search.length > 0) {
    throw new Error(
      'GATEWAY_BASE_URL is a base address and must not carry a query ' +
        'string. Per-request paths are composed onto it, so a query on the ' +
        'base would be dropped rather than merged.',
    );
  }

  if (parsed.hash.length > 0) {
    throw new Error(
      'GATEWAY_BASE_URL is a base address and must not carry a fragment. A ' +
        'fragment is never sent to a server, so one here can only be a ' +
        'mistake.',
    );
  }

  let end = trimmed.length;
  while (end > 0 && trimmed.charAt(end - 1) === '/') {
    end -= 1;
  }

  return trimmed.slice(0, end);
}

export default defineConfig({
  // C-C, read-only legacy boundary. Discovery is confined to this directory's
  // own `specs/` folder. The three sibling directories under `tests/` hold
  // read-only behavioural-oracle assets, and the runner must be structurally
  // incapable of discovering, writing into or reporting on them. No
  // `testMatch` or `testIgnore` glob is declared for the same reason: the
  // built-in spec pattern is evaluated relative to `testDir` and so is
  // contained by construction, whereas any glob added here would be the one
  // way to widen that surface by accident.
  testDir: './specs',

  // Artifact root, kept inside this directory so nothing is ever written at
  // `tests/` level or above. In practice almost nothing lands here, because
  // every artifact capture below is switched off; it is declared explicitly so
  // the containment is stated rather than inherited.
  outputDir: './test-results',

  // Serialized execution. This is a CORRECTNESS decision, not a performance
  // one. The mutating workflows share `COMPANY` rows in a single
  // `persistence-db` volume, and the optimistic-concurrency path depends on a
  // known row state: the legacy fixture marks every one of its six columns as
  // participating in the update where-clause, so a conflict is a function of
  // all six original values. Parallel workers would interleave those
  // mutations and make the `409` assertion non-deterministic. Repeatability is
  // also the one hard prerequisite of the golden-master comparison this suite
  // feeds, and the paired-capture rule forbids recreating or reseeding the
  // volume between a legacy-side and a target-side capture.
  fullyParallel: false,
  workers: 1,

  // No retries. A retry must never be allowed to convert a real failure into
  // a pass, and the sharpest case is the stale update: it MUST fail, because
  // there is no silent overwrite anywhere in this system. Were a retry count
  // ever introduced for CI flake, it would have to be documented, and the
  // concurrency-conflict and event-ordering specs would have to pin it back to
  // zero locally with `test.describe.configure({ retries: 0 })`.
  retries: 0,

  // A stray `test.only` silently narrows a CI run to one test while still
  // reporting green. Fail the run instead when CI is set. Bracket notation for
  // the same reason as the base URL above.
  forbidOnly: !!process.env['CI'],

  // Per-test liveness guard against a hung request or a wedged upstream — it
  // is NOT a latency budget and asserts nothing about response time. No
  // service-level agreement, latency target, throughput target or availability
  // commitment is published anywhere for this system, so none may be implied
  // here. The value is deliberately generous: it exists only so a stuck run
  // ends by itself instead of hanging a pipeline.
  timeout: 60_000,

  expect: {
    // Same intent, one level down: a liveness guard for an individual
    // assertion, explicitly NOT a latency budget.
    timeout: 10_000,
  },

  // Console only. Two independent reasons, and both still hold now that
  // `tests/e2e/.gitignore` exists: report and trace directories ARE ignored, so
  // the accident of committing one is prevented rather than merely discouraged —
  // but a configuration that generates nothing cannot leak anything at all, and
  // a trace records request and response bodies, which for this suite means
  // bearer tokens. Defence in depth: the ignore rule is the safety net, and not
  // generating the artifact is the control. No third-party reporter, no
  // performance tracing and no metrics collection are configured.
  reporter: [['list']],

  // Suppress the built-in slowest-files ranking. It is a duration report, and
  // this project publishes no latency budget for anything to be measured
  // against, so surfacing a timing league table here would read as a
  // performance signal that nothing in the requirements sanctions.
  reportSlowTests: null,

  use: {
    // Gateway, and only Gateway. Validated above so a broken value fails the
    // run immediately rather than halfway through it.
    baseURL: assertUsableBaseUrl(resolvedGatewayBaseUrl),

    // Content negotiation only. Deliberately NO `Authorization` header, and
    // deliberately no stored credential or session state of any kind: a
    // globally applied token would make the standing `401`-without-a-token
    // assertion impossible to write, and that assertion is the standing proof
    // that every new boundary is authenticated. Tokens are requested from the
    // sole issuer at run time and attached per request by the spec and fixture
    // layer. No secret, key or credential appears in this file, by design.
    extraHTTPHeaders: {
      Accept: 'application/json',
    },

    // Certificate and host verification stay ON. Stated as an explicit
    // negative rather than left to a default: the legacy code disables both in
    // a deferred capability area, and that is a documented legacy security
    // defect which this suite must not adopt as its own configuration. It must
    // stay on for a second reason too — see `clientCertificates` below. Turning
    // it off to "make TLS work locally" would silently disable the very
    // verification a mutually authenticated handshake exists to establish.
    ignoreHTTPSErrors: false,

    // The client certificate for the one mutual-TLS edge in the system.
    //
    // `POST /v1/tokens` on Security is protected by mutual TLS and by nothing
    // else, because a caller cannot present a bearer token in order to obtain
    // its first bearer token. Against an https deployment a spec that exercises
    // token issuance must therefore present a client certificate or be refused
    // with `401`, and this is where the runner supplies it.
    //
    // NO CREDENTIAL IS IN THIS FILE. The entry is resolved by the fixtures
    // layer from `SECURITY_MTLS_CERT_PATH`, `SECURITY_MTLS_KEY_PATH` and
    // `SECURITY_MTLS_KEY_PASSPHRASE`, and it carries file PATHS only. The files
    // are mounted from the orchestration secret layer and are not part of this
    // repository.
    //
    // Absent is the normal case and is not an error: the documented local
    // bring-up publishes plain HTTP on loopback, where there is no handshake and
    // so no certificate to present, and the resolver yields nothing. `[]` then
    // configures no certificate at all, which is exactly right — a suite that
    // refused to start without certificates would be unrunnable on the one
    // topology the setup instructions document. A spec that needs the issuance
    // edge should skip itself when the fixtures layer reports none, saying so.
    //
    // This is the one place the runner imports from `fixtures/`, and the reason
    // is that the value must reach `use`, which only the config can populate.
    // The base-URL resolution above stays inlined for the reason stated there.
    clientCertificates: SECURITY_CLIENT_CERTIFICATE ? [SECURITY_CLIENT_CERTIFICATE] : [],

    // Every artifact capture off. Screenshots and video are meaningless for a
    // suite that never opens a page, and a trace would add artifact weight for
    // no diagnostic benefit over the request-level assertions themselves.
    // Setting all three explicitly also documents that this is not a UI suite.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },

  // One logical project, so every reported line states plainly that this is
  // the API suite. It carries no browser engine, no device preset and no
  // screen geometry, and it inherits `testDir`, `outputDir` and `use` from
  // above. No coverage instrumentation is configured here either: the
  // line-coverage gate this migration must satisfy is a .NET-side obligation
  // measured per service from its own report, and this suite carries none of
  // it.
  projects: [{ name: 'api' }],

  // Deliberately absent, and absent for stated reasons:
  //
  // * No `webServer` block. The single local bring-up path is the compose
  //   manifest under `orchestration/`, whose health-condition dependency chain
  //   is what makes Gateway report healthy only after its upstreams do.
  //   Spawning or building anything from here would create a second,
  //   competing bring-up path and break that gate. This config points at an
  //   already-running stack and nothing more.
  // * No global setup or teardown. Anything that seeded, reset or reseeded the
  //   store would violate the paired-capture rule, under which a legacy-side
  //   and a target-side recording for one workflow are comparable only when
  //   taken against the same unrecreated `persistence-db` volume state. The
  //   legacy oracle deletes and recreates its database on every connect; this
  //   suite deliberately does not imitate that. Readiness is asserted by the
  //   anonymous `/health` probes inside the specs, which mutate nothing.
});
