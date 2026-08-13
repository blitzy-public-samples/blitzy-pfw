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
import { readdirSync } from 'node:fs';
import { join } from 'node:path';

import { defineConfig } from '@playwright/test';

import { PROJECT_LABEL } from './fixtures/run-mode';
import { SECURITY_CLIENT_CERTIFICATE } from './fixtures/service-endpoints';

/**
 * The directory discovery is confined to, relative to this file.
 *
 * Named once and used for both `testDir` and the inventory check below, so the
 * check can never be validating a different directory from the one the runner
 * loads.
 */
const SPEC_DIRECTORY = './specs';

/**
 * ⚠ THE SUITE, ENUMERATED BY NAME. THIS LIST *IS* THE RUN. ⚠
 *
 * These six files are the reviewed suite, in the order they are numbered, and the
 * runner discovers **exactly** these and nothing else.
 *
 * WHY AN EXPLICIT INVENTORY RATHER THAN A DIRECTORY SWEEP
 * ------------------------------------------------------
 * An earlier generation of six superseded, unnumbered specs once sat in `specs/`
 * alongside these, and a bare `testDir` sweep collected all twelve — so the
 * documented `npm test` ran twelve files while every report described six. That
 * was not a cosmetic difference: the two generations both wrote `COMPANY` rows in
 * the one `persistence-db` volume, so the row state an optimistic-concurrency
 * assertion depends on was being changed by files nobody had reviewed for that
 * purpose and the `409` became a function of execution order; every pass, fail and
 * skip count covered twice the files the report named; and the two generations
 * disagreed about missing-stack behaviour with nothing selecting which answer was
 * authoritative.
 *
 * **Those six superseded files have since been removed from the tree**, which is
 * why the inventory check below is now EXACT IN BOTH DIRECTIONS rather than
 * one-sided. While they were present, an unenumerated file in `specs/` had to be
 * tolerated — failing on their presence would have forbidden keeping them — and
 * that tolerance was the gap: any `.spec.ts` dropped into the directory was
 * silently excluded from the run with nothing reporting it. With the directory
 * holding exactly the reviewed six, an unenumerated file is unambiguously either a
 * spec somebody forgot to enumerate or one that has not been reviewed, and both are
 * findings.
 *
 * Adding a spec is therefore a deliberate two-part act: create the file **and**
 * add it here. That is the intended friction — a file that appears in `specs/`
 * without review does not silently join the run, and now it does not silently stay
 * out of it either.
 */
const SUITE_SPECS: readonly string[] = Object.freeze([
  '01-health-readiness.spec.ts',
  '02-authentication.spec.ts',
  '03-capability-gating.spec.ts',
  '04-deferred-routes.spec.ts',
  '05-datawindow-workflow.spec.ts',
  '06-concurrency-conflict.spec.ts',
]);

/**
 * Proves the enumerated inventory is EXACTLY what is on disk before the run.
 *
 * TWO DIRECTIONS, AND BOTH ARE REAL NOW. Constraining discovery with a name list
 * creates two symmetrical failure modes, and the check is one comparison that
 * catches both:
 *
 * - **An enumerated file that is not on disk.** Renamed, moved or deleted, it
 *   simply stops being discovered, and the run then reports green over five specs
 *   while appearing to have covered six.
 * - **A spec on disk that is not enumerated.** It never runs, and nothing says so.
 *   A new spec added without touching this file is indistinguishable from one that
 *   ran and passed.
 *
 * ⚠ WHAT THIS REPLACED, BECAUSE THE OLD SHAPE COULD NOT FAIL. The second direction
 * used to be deliberately tolerated, on the correct reasoning that the six
 * superseded unnumbered specs were then still on disk and failing on their presence
 * would have forbidden keeping them. What stood in for it was a count check —
 * `expected.length !== SUITE_SPECS.length` — and that comparison was a TAUTOLOGY:
 * the sole caller passes `SUITE_SPECS` as `expected`, so it compared the list's
 * length with its own and could not fail for any input whatsoever. It read as the
 * count assertion its own comment described and asserted nothing at all. The
 * superseded files have since been removed, so an exact set comparison is now both
 * possible and correct, and it subsumes the count: two sets that are equal have
 * equal size.
 *
 * SORTED AND COMPARED AS TEXT, so the diagnostic can print both sides. Directory
 * order is filesystem-dependent and the enumeration is written in numbered order;
 * sorting both makes the comparison total and the message stable across platforms.
 *
 * ONLY `*.spec.ts` PARTICIPATES. `specs/` may legitimately acquire a shared helper
 * or a readme, and neither is a spec: demanding that every file in the directory be
 * enumerated would forbid a `.md` note while catching nothing a spec-suffix filter
 * misses, because the runner only ever collects `.spec.ts`.
 *
 * Runs at config load, synchronously, reading one directory. That matches this
 * file's existing posture: a structural fault stops the run immediately rather
 * than being discovered part way through it, which is how the framework being
 * migrated treats a structural fault. It contacts nothing, starts nothing and
 * mutates nothing, so `--list` still collects with no stack running (C-L).
 *
 * @param directory the spec directory, relative to this file
 * @param expected the enumerated inventory
 * @returns the same inventory, once proven to match the directory exactly
 * @throws Error when the directory cannot be read, or its `*.spec.ts` set differs
 *         from the enumeration in either direction
 */
function assertSuiteInventory(
  directory: string,
  expected: readonly string[],
): readonly string[] {
  const absolute: string = join(__dirname, directory);

  let present: readonly string[];
  try {
    present = readdirSync(absolute);
  } catch (cause: unknown) {
    throw new Error(
      `The spec directory "${directory}" could not be read, so the suite ` +
        'inventory cannot be verified and the run is stopped rather than ' +
        'executing an unknown set of files.',
      { cause },
    );
  }

  const onDisk: readonly string[] = [...present]
    .filter((candidate: string) => candidate.endsWith('.spec.ts'))
    .sort();
  const enumerated: readonly string[] = [...expected].sort();

  const missing: readonly string[] = enumerated.filter(
    (candidate: string) => !onDisk.includes(candidate),
  );
  const unexpected: readonly string[] = onDisk.filter(
    (candidate: string) => !enumerated.includes(candidate),
  );

  if (missing.length > 0 || unexpected.length > 0) {
    throw new Error(
      `The suite inventory in playwright.config.ts and the contents of ` +
        `"${directory}" disagree, so the run is stopped rather than executing ` +
        'a set of files nobody has described.\n' +
        `  enumerated (${String(enumerated.length)}): ${enumerated.join(', ')}\n` +
        `  on disk (${String(onDisk.length)}): ${onDisk.join(', ')}\n` +
        (missing.length > 0
          ? `  enumerated but absent: ${missing.join(', ')} — a renamed or ` +
            'deleted spec must be updated here as well, because otherwise the ' +
            'run would quietly cover fewer files while still reporting a pass.\n'
          : '') +
        (unexpected.length > 0
          ? `  present but not enumerated: ${unexpected.join(', ')} — add it ` +
            'here once reviewed, or remove it; a spec that is silently excluded ' +
            'from every run is indistinguishable from one that passed.\n'
          : ''),
    );
  }

  return expected;
}

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
const resolvedGatewayBaseUrl = process.env['GATEWAY_BASE_URL'] ?? 'https://localhost:5105';

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
  // ⚠ FAIL-CLOSED GLOBAL SETUP. THE SUITE REFUSES AN ABSENT TOPOLOGY ⚠
  //
  // 🔴 THE DEFAULT USED TO BE BACKWARDS, AND THAT IS WHAT THIS LINE FIXES. Every
  // spec began by probing Gateway and calling `test.skip` when the probe did not
  // answer, so a plain `npx playwright test` - the command the environment's setup
  // instructions document - exited 0 with every live assertion skipped. A green
  // run proved nothing about the four services it exists to verify.
  //
  // It was worse than merely permissive. The probe converted a TLS fault into the
  // same "unreachable" as a refused connection, and every listener in this estate
  // is `https` presenting a certificate from a throwaway private authority - so
  // the single most likely local misconfiguration, an untrusted authority, SKIPPED
  // THE WHOLE SUITE while the stack was up and serving, masking a real deployment
  // finding rather than reporting it.
  //
  // `./global-setup.ts` now probes all four services' anonymous /health once,
  // BEFORE any test runs, and throws when the topology is incomplete - naming the
  // offending service, classifying the fault as unreachable / untrusted / stalled,
  // and quoting the remedy for that class. A `503` is NOT a fault: it means the
  // service is running and reporting on itself, which spec 01 asserts on.
  //
  // Skipping is still available and is now OPT-IN BY NAME:
  //     E2E_ALLOW_ABSENT_STACK=1 npx playwright test
  // An environment variable rather than a config flag, deliberately: the decision
  // belongs to whoever runs the command, and a checked-in flag would grant it to
  // everybody including CI - which is the state this replaces.
  globalSetup: './global-setup.ts',

  // C-C, read-only legacy boundary. Discovery is confined to this directory's
  // own `specs/` folder. The three sibling directories under `tests/` hold
  // read-only behavioural-oracle assets, and the runner must be structurally
  // incapable of discovering, writing into or reporting on them.
  testDir: SPEC_DIRECTORY,

  // ⚠ DISCOVERY IS AN EXPLICIT SIX-FILE INVENTORY, NOT A DIRECTORY SWEEP ⚠
  //
  // `testMatch` entries are resolved relative to `testDir`, so this NARROWS the
  // surface and cannot widen it: nothing outside `specs/` is reachable through a
  // bare filename, and the read-only sibling directories stay structurally
  // undiscoverable. That is the distinction worth being precise about — the
  // hazard a glob here would create is reaching OUTSIDE `tests/e2e/`, and a list
  // of six leaf filenames cannot.
  //
  // Naming the files is what it buys: a sweep once collected an earlier
  // generation of six superseded unnumbered specs as well, so the documented
  // `npm test` ran twelve files and mutated shared COMPANY state from two
  // generations at once. Those files have since been removed. See SUITE_SPECS
  // above for the full reasoning and for why the inventory check is now exact in
  // both directions rather than one-sided.
  //
  // The inventory is verified against the directory before the run, so a renamed
  // spec stops the run instead of quietly shrinking it AND an unenumerated one
  // stops it instead of quietly never running. Spread into a fresh mutable array
  // because that is the shape `testMatch` declares; the source list stays frozen,
  // so the runner receives a copy and cannot reach the inventory itself.
  testMatch: [...assertSuiteInventory(SPEC_DIRECTORY, SUITE_SPECS)],

  // NOTE ON DISCOVERY, WHICH IS DECLARED EXACTLY ONCE ABOVE.
  // Two independent remediations of the twelve-file sweep arrived here: an explicit
  // six-file inventory, and a numbered-spec pattern anchored on the path separator.
  // Both narrow collection to the same six numbered files and both are resolved
  // relative to `testDir`, so neither can reach outside `specs/`. The explicit
  // inventory is the one kept, because it is verified against the directory at
  // config load and therefore STOPS the run when the directory and the enumeration
  // disagree in either direction, instead of quietly shrinking or quietly excluding;
  // the pattern is redundant beside it, not discarded on merit. The reasoning both
  // shared is worth keeping on record: a sweep collected all twelve files (103
  // tests), every workflow was collected twice, and the two generations disagreed
  // about missing-stack behaviour with nothing selecting which was authoritative -
  // which made the result non-canonical rather than merely slow.

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
  // ever introduced for CI flake, it would have to be documented, and the two
  // state-mutating specs — `06-concurrency-conflict` above all — would have to
  // pin it back to zero locally with `test.describe.configure({ retries: 0 })`.
  // That spec carries the pin today, and it matters more since its six steps
  // became one atomic test: a retry would re-run the whole mutation sequence.
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
    //
    // EVERY LISTENER THIS REPOSITORY BINDS IS `https`, so this setting is load
    // bearing today rather than a precaution for later. Each service declares its
    // TLS listener in its BASE settings file - https://+:5101, :5102, :5104,
    // :5105 - and a docker compose bring-up presents a certificate issued by the
    // throwaway private authority whose public half the manifest projects as a
    // Compose secret, named on the host by INTERNAL_TLS_CA_PATH.
    //
    // WHICH MEANS THE RUNNER MUST TRUST THAT AUTHORITY, AND CANNOT BE MADE TO FROM
    // HERE. Node reads NODE_EXTRA_CA_CERTS once at process start, so no code in
    // this suite can install a trust anchor into a run already under way. Export
    // it before invoking the runner:
    //     docker compose bring-up: NODE_EXTRA_CA_CERTS="$INTERNAL_TLS_CA_PATH"
    //     host dotnet run:         dotnet dev-certs https --trust
    // `./global-setup.ts` detects the gap and refuses the run with that remedy
    // quoted, which is the honest alternative to appearing to fix it here.
    ignoreHTTPSErrors: false,

    // The client certificate for the mutual-TLS half of the issuance edge.
    //
    // `POST /v1/tokens` on Security is the one operation a bearer token cannot
    // protect, because a caller cannot present a bearer token in order to obtain
    // its first bearer token. The published contract therefore accepts either of
    // two caller credentials, and they live at different layers:
    //
    //   * an HTTP `Basic` credential naming a subject on Security's issuance
    //     roster. It is a HEADER, so the runner has no part in it — `fixtures/
    //     auth.ts` attaches it to the issuance request and to nothing else,
    //     deliberately NOT through `extraHTTPHeaders`, which would send it to
    //     Gateway, DataServices and Persistence as well. This is the path the
    //     documented bring-up uses — not because the channel is readable (it
    //     is not; every listener here terminates TLS) but because a shared
    //     secret needs no certificate provisioning.
    //   * a client certificate, which exists only inside a TLS handshake and so
    //     can only be supplied by the runner. That is what this entry is for, and
    //     it is reachable only against a deployment that terminates TLS at
    //     Security — the mutual-TLS fallback AAP §0.6.6.3 describes for that one
    //     pair.
    //
    // NO CREDENTIAL IS IN THIS FILE. The entry is resolved by the fixtures
    // layer from `SECURITY_MTLS_CERT_PATH`, `SECURITY_MTLS_KEY_PATH` and
    // `SECURITY_MTLS_KEY_PASSPHRASE`, and it carries file PATHS only. The files
    // are mounted from the orchestration secret layer and are not part of this
    // repository.
    //
    // Absent is the common case and is not an error, though the reason is NOT
    // that there is no handshake to present a certificate inside — every
    // listener this repository binds terminates TLS, so there always is one.
    // The reason is that the OTHER accepted credential needs no provisioning:
    // the documented bring-up supplies a shared secret, so a deployment that
    // has not issued caller certificates simply authenticates with `Basic` and
    // the resolver yields nothing, leaving `[]` to configure none at all. That
    // is exactly right — the readiness, capability and 401-without-a-token
    // specs need no credential of either kind, and a suite that refused to
    // start without certificates would be unrunnable for them. Both schemes
    // have been exercised against the Compose stack; the suite passes on each.
    //
    // WHAT ABSENT DOES NOT MEAN. It does not mean the issuance edge is open.
    // `POST /v1/tokens` refuses a request carrying NEITHER credential with `401`
    // on every topology, so absent here means only that the mutual-TLS half is
    // not exercisable in this run; the `Basic` half may well be. A spec that
    // needs a token skips itself when neither is configured and says so.
    // `fixtures/service-endpoints.ts` and `fixtures/auth.ts` state the identical
    // policy; all three must stay in agreement.
    //
    // This is one of the two places the runner imports from `fixtures/`, and the
    // reason is that the value must reach `use`, which only the config can
    // populate. (The other is `PROJECT_LABEL` at `projects` below, which must
    // reach the project name for the same reason.) The base-URL resolution above
    // stays inlined for the reason stated there.
    clientCertificates: SECURITY_CLIENT_CERTIFICATE ? [SECURITY_CLIENT_CERTIFICATE] : [],

    // Every artifact capture off. Screenshots and video are meaningless for a
    // suite that never opens a page, and a trace would add artifact weight for
    // no diagnostic benefit over the request-level assertions themselves.
    // Setting all three explicitly also documents that this is not a UI suite.
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },

  // One logical project, so every reported line states plainly what kind of run
  // this is. It carries no browser engine, no device preset and no screen
  // geometry, and it inherits `testDir`, `outputDir` and `use` from above. No
  // coverage instrumentation is configured here either: the line-coverage gate
  // this migration must satisfy is a .NET-side obligation measured per service
  // from its own report, and this suite carries none of it.
  //
  // THE NAME IS NOT A CONSTANT, and that is the point. `PROJECT_LABEL` is `api`
  // for a full acceptance run and `api-partial-no-stack` for a run that set
  // `E2E_ALLOW_ABSENT_STACK` to tolerate an absent stack. A summary line shows
  // counts and a project name and nothing else, so `27 skipped` under a project
  // called `api` is indistinguishable from an acceptance run that happened to
  // skip a few tests — while the same counts under `api-partial-no-stack` cannot
  // be mistaken for one. The skip reason each test carries says the same thing,
  // but a reader of a summary never opens a skip reason.
  //
  // `./fixtures/run-mode` is importable from here precisely because it imports
  // nothing itself — no runner, no fixture, no filesystem — so reading the run
  // mode in the config costs no module-load side effect and keeps
  // `playwright test --list` a pure collection step.
  projects: [{ name: PROJECT_LABEL }],

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
