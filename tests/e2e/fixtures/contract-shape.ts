/**
 * EXACT CONTRACT-SHAPE ASSERTIONS — the one place this suite decides what "the
 * boundary conformed" means.
 *
 * WHY THIS MODULE EXISTS
 * ----------------------
 * Every spec in this suite used to read a response TOLERANTLY: a media type
 * accepted by `toContain('json')`, a verdict matched by
 * `/\b(healthy|ok|up|pass)\b/i`, an upstream "named" by a case-insensitive
 * substring anywhere in the serialized body, a service identity accepted under
 * any of three spellings, a member resolved through a candidate list of three or
 * four alternative spellings, and an advertised key-set URI checked by
 * `endsWith`.
 *
 * Each tolerance had a defensible local reason — usually that a spec should
 * report "the boundary serialized this differently" rather than "the field is
 * missing" — and together they added up to a suite that could not fail for the
 * single most likely defect on a freshly decomposed boundary: A RESPONSE FIELD
 * RENAME, OR WIRE-SHAPE DRIFT. `retCode` becoming `ret_code`, `Healthy` becoming
 * `ok`, the aggregate collapsing three named upstreams into one opaque verdict, a
 * bare array acquiring an envelope, `application/json` becoming `text/html` with
 * JSON inside it — every one of those passed. A contract test that cannot detect
 * a contract change is not a contract test.
 *
 * THE RULE THIS MODULE ENFORCES, AND THE ONE IT KEEPS
 * --------------------------------------------------
 * The exact assertion is the ACCEPTANCE criterion; the tolerant read survives
 * only as DIAGNOSIS AFTER a failure. So a drifted spelling still produces the
 * good diagnostic it always did — "the contract declares `retCode`; the response
 * carried `ret_code`" — but it now produces it as a FAILURE rather than as a
 * silent pass. Nothing was made harder to diagnose; one thing was made
 * impossible to miss.
 *
 * WHERE THE EXPECTED VALUES COME FROM
 * -----------------------------------
 * `shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml` and its Security
 * sibling — the published contracts, which are the only authority for the shape
 * of these boundaries. Every member set and every enum token below carries the
 * schema name it was taken from, and {@link assertContractDeclaresShape} reads
 * the YAML at run time and re-derives them, so a contract edit that renames a
 * member or adds an enum value fails this suite instead of quietly diverging
 * from it. That check needs no running stack and no YAML parser: it locates a
 * named schema block by indentation and reads the member names and enum tokens
 * out of it, which is sufficient for a document this suite does not author.
 *
 * WHAT THIS MODULE DOES NOT DO
 * ----------------------------
 * It asserts no timing, no ordering and no latency of any kind, and it invents no
 * requirement the contract does not state (C-B): an OPTIONAL member is optional
 * here too, and a member the contract does not mention is not required to be
 * absent unless the schema sets `additionalProperties: false` — which most of
 * these do, and which is exactly what makes the exact member-set assertion
 * legitimate rather than over-strict.
 *
 * SECRET-FREE (C-F). No assertion message built here echoes a response value.
 * Member NAMES, enum TOKENS and URIs composed from this suite's own configuration
 * are named; values read from a service are not, and the two diagnostic helpers
 * at the end exist precisely so that a failure can describe a body's SHAPE
 * without quoting its contents.
 */

import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';

/* ------------------------------------------------------------------------- *
 * Media types
 * ------------------------------------------------------------------------- */

/** The media type every successful JSON response on these boundaries declares. */
export const JSON_MEDIA_TYPE = 'application/json';

/**
 * The media type every REFUSAL on these boundaries declares.
 *
 * RFC 9457 problem documents are what the framework emits without hand-written
 * code, and the distinct subtype is what lets one client-side error handler serve
 * every refusal. A refusal that arrived as plain `application/json` would still
 * parse, which is exactly why the distinction has to be asserted rather than
 * assumed.
 */
export const PROBLEM_JSON_MEDIA_TYPE = 'application/problem+json';

/** The only media-type parameter these boundaries are permitted to add. */
const PERMITTED_MEDIA_TYPE_PARAMETERS: readonly string[] = Object.freeze([
  'charset=utf-8',
]);

/* ------------------------------------------------------------------------- *
 * Member-set contracts, each taken from a named schema of gateway.v1.yaml
 * ------------------------------------------------------------------------- */

/**
 * The members a response object may and must carry.
 *
 * `required` is asserted present. `optional` is permitted and not required.
 * ANYTHING ELSE IS A FAILURE, which is the half that detects a rename: a
 * response that dropped `retCode` and added `ret_code` satisfies no member set
 * here, because the new name is in neither list and the old one is missing.
 */
export interface MemberContract {
  /** The schema in the published contract this set was taken from. */
  readonly schema: string;

  /** Members the schema marks `required`. Each must be present. */
  readonly required: readonly string[];

  /** Members the schema declares but does not require. Permitted, not required. */
  readonly optional: readonly string[];

  /**
   * Whether member names must be lowerCamelCase.
   *
   * True for everything this refactor publishes. FALSE for the OIDC discovery
   * document, whose `jwks_uri` and `issuer` spellings are fixed by RFC 8414 and
   * are not this refactor's to re-case — a standardized wire field keeps its
   * standard spelling, and asserting otherwise would invent a requirement.
   */
  readonly lowerCamelCase: boolean;
}

/** Build a member contract, defaulting the naming rule to this refactor's own. */
function contract(
  schema: string,
  required: readonly string[],
  optional: readonly string[] = [],
  lowerCamelCase = true,
): MemberContract {
  return Object.freeze({
    schema,
    required: Object.freeze([...required]),
    optional: Object.freeze([...optional]),
    lowerCamelCase,
  });
}

/** `AggregateHealthReport` — Gateway's `/health` body (C-10). */
export const AGGREGATE_HEALTH_REPORT: MemberContract = contract(
  'AggregateHealthReport',
  ['status', 'service', 'upstreams'],
  ['checkedAt', 'checks'],
);

/** `UpstreamHealth` — one named upstream inside the aggregate. */
export const UPSTREAM_HEALTH: MemberContract = contract(
  'UpstreamHealth',
  ['service', 'status'],
  ['detail'],
);

/** `HealthCheckResult` — one individual check inside the aggregate. */
export const HEALTH_CHECK_RESULT: MemberContract = contract(
  'HealthCheckResult',
  ['name', 'status'],
  ['detail'],
);

/** `PingResponse` — the authenticated placeholder endpoint's body. */
export const PING_RESPONSE: MemberContract = contract(
  'PingResponse',
  ['service', 'authenticated'],
  ['timestamp'],
);

/** `ReservedRouteBody` — the `501` body of a reserved deferred-capability route (C-D). */
export const RESERVED_ROUTE_BODY: MemberContract = contract(
  'ReservedRouteBody',
  ['status', 'service', 'deferredService', 'marker', 'route', 'retCode'],
);

/** `CapabilityReport` — the eight-bit capability gate projected as configuration. */
export const CAPABILITY_REPORT: MemberContract = contract(
  'CapabilityReport',
  ['effectiveMask', 'allMask', 'capabilities'],
  ['unrecognizedBits'],
);

/** `Capability` — one bit of the gate. */
export const CAPABILITY: MemberContract = contract(
  'Capability',
  ['name', 'value', 'enabled', 'phaseOneDestination'],
);

/** `RetrieveChunk` — one chunk of the retrieval's ordered sequence (C-03). */
export const RETRIEVE_CHUNK: MemberContract = contract(
  'RetrieveChunk',
  ['rows', 'rowCount', 'chunkIndex', 'final', 'cumulativeRowCount'],
  ['buffer', 'error'],
);

/** `DataWindowRow` — the carrier that makes a result a DataWindow rather than a rowset. */
export const DATAWINDOW_ROW: MemberContract = contract(
  'DataWindowRow',
  ['buffer', 'row', 'itemStatus', 'columns'],
  ['originalValues'],
);

/** `ColumnValue` — one column of one row. */
export const COLUMN_VALUE: MemberContract = contract(
  'ColumnValue',
  ['columnName', 'columnId', 'value'],
  ['itemStatus'],
);

/** `ConflictDetail` — the `409` payload's detail (C-06 over C-09). */
export const CONFLICT_DETAIL: MemberContract = contract(
  'ConflictDetail',
  ['rows', 'updateTable', 'rowsExpected', 'rowsMatched'],
);

/** `ConflictRow` — one conflicted row, carrying BOTH value sets. */
export const CONFLICT_ROW: MemberContract = contract(
  'ConflictRow',
  ['buffer', 'row', 'itemStatus', 'currentValues', 'originalValues'],
);

/**
 * Every member contract this suite asserts, anywhere.
 *
 * The list the anti-drift guard walks. Declared here rather than assembled in the
 * spec that runs the guard, so adding a member contract above and forgetting to
 * guard it is one omission instead of two — a new entry is guarded the moment it
 * is added to this array, and an entry that is never added is visibly absent from
 * a single list rather than invisibly absent from a spec.
 */
export const ALL_MEMBER_CONTRACTS: readonly MemberContract[] = Object.freeze([
  AGGREGATE_HEALTH_REPORT,
  UPSTREAM_HEALTH,
  HEALTH_CHECK_RESULT,
  PING_RESPONSE,
  RESERVED_ROUTE_BODY,
  CAPABILITY_REPORT,
  CAPABILITY,
  RETRIEVE_CHUNK,
  DATAWINDOW_ROW,
  COLUMN_VALUE,
  CONFLICT_DETAIL,
  CONFLICT_ROW,
]);

/* ------------------------------------------------------------------------- *
 * Enum tokens, spelled EXACTLY as the contract spells them
 * ------------------------------------------------------------------------- */

/** `AggregateHealthReport.status` — the closed verdict set. Case-sensitive. */
export const HEALTH_STATUS_TOKENS: readonly string[] = Object.freeze([
  'Healthy',
  'Degraded',
  'Unhealthy',
]);

/**
 * The ONE verdict token that means ready.
 *
 * A single token rather than a vocabulary. The former
 * `/\b(healthy|ok|up|pass)\b/i` accepted four words in any casing, so an
 * implementation that answered `ok` — a token the contract does not declare —
 * passed the readiness assertion, and so would one that answered `Healthy` when
 * the contract had been changed to something else. What C-10 publishes is a
 * CLOSED enumeration, and `Healthy` is its ready member.
 */
export const HEALTHY_STATUS_TOKEN = 'Healthy';

/** `UpstreamHealth.status` — the aggregate's per-upstream set, which adds `Unreachable`. */
export const UPSTREAM_STATUS_TOKENS: readonly string[] = Object.freeze([
  'Healthy',
  'Degraded',
  'Unhealthy',
  'Unreachable',
]);

/**
 * `UpstreamHealth.service` — the three upstreams Gateway aggregates, spelled exactly.
 *
 * Gateway is deliberately absent: it aggregates these three, and requiring it to
 * name itself would make the readiness property circular.
 */
export const UPSTREAM_SERVICE_TOKENS: readonly string[] = Object.freeze([
  'persistence',
  'dataservices',
  'security',
]);

/** `ReservedRouteBody.deferredService` — the four deferred capability areas. */
export const DEFERRED_SERVICE_TOKENS: readonly string[] = Object.freeze([
  'DesignSystem',
  'Documents',
  'Integration',
  'ScriptBridge',
]);

/** `Capability.phaseOneDestination` — where each bit's capability lands in this phase. */
export const CAPABILITY_DESTINATION_TOKENS: readonly string[] = Object.freeze([
  'Persistence',
  'DesignSystem',
  'ScriptBridge',
  'PackagingTooling',
]);

/**
 * `Capability.name` — the eight capability bits, spelled exactly as the legacy
 * declares them.
 *
 * SCREAMING_SNAKE and not re-cased, deliberately and repository-wide: these
 * identifiers appear in serialized payloads, in log records and in
 * characterization recordings, so a rename would silently invalidate every stored
 * comparison. The order is the legacy declaration order, which is also ascending
 * bit order.
 */
export const CAPABILITY_NAME_TOKENS: readonly string[] = Object.freeze([
  'INIT_FLAG_ENABLE_UI',
  'INIT_FLAG_ENABLE_SCITER',
  'INIT_FLAG_ENABLE_BLINK',
  'INIT_FLAG_ENABLE_BLINKFAST',
  'INIT_FLAG_ENABLE_ORCA',
  'INIT_FLAG_ENABLE_SQLITE',
  'INIT_FLAG_ENABLE_DPIAWARE',
  'INIT_FLAG_ENABLE_WEBVIEW',
]);

/**
 * `Capability.value` — the eight bit values, positionally aligned with
 * {@link CAPABILITY_NAME_TOKENS}.
 *
 * Note the gap between 8 and 256: the legacy enumeration is not contiguous, and
 * reproducing the gap is part of reproducing the enumeration.
 */
export const CAPABILITY_VALUE_TOKENS: readonly number[] = Object.freeze([
  1, 2, 4, 8, 256, 512, 1024, 2048,
]);

/** `PingResponse.authenticated` — `const: true`. The endpoint answers only to a bearer. */
export const PING_AUTHENTICATED = true;

/* ------------------------------------------------------------------------- *
 * Single-value members — the contract's `const:` declarations
 *
 * A `const` is a closed set of one, and it is the sharpest assertion available:
 * there is exactly one conforming value and everything else is drift. The
 * document uses it for the reporting service's own name, the ping response's
 * authentication claim, the all-capabilities mask, and three members of the
 * reserved-route body. Each is cross-checked by the same mechanism as an enum,
 * because `readSchemaFacts` collects `const:` values alongside `enum:` tokens.
 * ------------------------------------------------------------------------- */

/** `AggregateHealthReport.service` and `PingResponse.service` — both `const: gateway`. */
export const GATEWAY_SERVICE_TOKEN = 'gateway';

/**
 * `ReservedRouteBody.marker` — `const: reserved for Phase 2`.
 *
 * The machine-readable declaration that a route is a ROUTING DECLARATION and not a
 * stub. Asserting the exact string is what makes C-D auditable from the wire: a
 * reserved route that had grown an implementation would stop saying this.
 */
export const RESERVED_ROUTE_MARKER = 'reserved for Phase 2';

/** `ReservedRouteBody.status` — `const: 501`, echoing the transport status in the body. */
export const RESERVED_ROUTE_STATUS = 501;

/** `ReservedRouteBody.retCode` — `const: -2001`, the legacy `E_NO_IMPLEMENTATION`. */
export const RESERVED_ROUTE_RET_CODE = -2001;

/** `CapabilityReport.allMask` — `const: 3847`, the legacy `INIT_FLAG_ENABLE_ALL`. */
export const CAPABILITY_ALL_MASK = 3847;

/* ------------------------------------------------------------------------- *
 * The exact assertions
 * ------------------------------------------------------------------------- */

/** lowerCamelCase, as the JSON members this refactor publishes are spelled. */
const LOWER_CAMEL_CASE = /^[a-z][A-Za-z0-9]*$/;

/**
 * Raise a shape failure.
 *
 * A thrown `Error` rather than an `expect` call, so this module needs no runner
 * import and can be used from a fixture as readily as from a spec. Playwright
 * reports a throw from a test or a step as a failure with the message intact,
 * which is all these assertions need.
 *
 * @param message the diagnostic, which never contains a response value
 */
function fail(message: string): never {
  throw new Error(`CONTRACT SHAPE: ${message}`);
}

/**
 * Assert a response's media type EXACTLY.
 *
 * Parses `Content-Type` into its type/subtype and its parameters and compares the
 * type/subtype for equality, case-insensitively as RFC 9110 requires of the
 * subtype itself. `toContain('json')` used to stand in for this, and it accepted
 * `text/html` with the word JSON anywhere in it, `application/problem+json` where
 * a success body was expected, and a missing header on some paths.
 *
 * Only `charset=utf-8` is permitted as a parameter, because it is the only one
 * these boundaries emit and an unexpected parameter is worth seeing.
 *
 * @param headerValue the raw `Content-Type` header, or undefined when absent
 * @param expected {@link JSON_MEDIA_TYPE} or {@link PROBLEM_JSON_MEDIA_TYPE}
 * @param context what was being read, for the diagnostic
 */
export function assertMediaType(
  headerValue: string | undefined,
  expected: string,
  context: string,
): void {
  if (headerValue === undefined || headerValue.trim().length === 0) {
    fail(
      `${context} declared no Content-Type. The contract declares ${expected}, ` +
        'and a client that cannot tell a problem document from a success body ' +
        'has to guess.',
    );
  }

  const [rawType = '', ...rawParameters] = headerValue.split(';');
  const mediaType: string = rawType.trim().toLowerCase();

  if (mediaType !== expected) {
    fail(
      `${context} declared Content-Type "${mediaType}" where the contract ` +
        `declares "${expected}". A refusal must arrive as ` +
        `"${PROBLEM_JSON_MEDIA_TYPE}" and a success body as "${JSON_MEDIA_TYPE}", ` +
        'because one client-side error handler serves every refusal only if the ' +
        'two are distinguishable without parsing.',
    );
  }

  for (const rawParameter of rawParameters) {
    const parameter: string = rawParameter.trim().toLowerCase().replace(/\s+/g, '');

    if (parameter.length > 0 && !PERMITTED_MEDIA_TYPE_PARAMETERS.includes(parameter)) {
      fail(
        `${context} declared the unexpected Content-Type parameter ` +
          `"${parameter}". Only ${PERMITTED_MEDIA_TYPE_PARAMETERS.join(', ')} is ` +
          'expected on these boundaries.',
      );
    }
  }
}

/**
 * Narrow a value to a JSON object, or fail saying what arrived instead.
 *
 * @param value the parsed value
 * @param context what was being read, for the diagnostic
 * @returns the value as a record
 */
export function assertJsonObject(
  value: unknown,
  context: string,
): Record<string, unknown> {
  if (Array.isArray(value)) {
    fail(`${context} is a JSON array where the contract declares an object.`);
  }

  if (typeof value !== 'object' || value === null) {
    fail(
      `${context} is ${value === null ? 'null' : typeof value} where the ` +
        'contract declares an object.',
    );
  }

  return value as Record<string, unknown>;
}

/**
 * Assert an object's member set against a contract EXACTLY, in both directions.
 *
 * Three failures, and the third is the one that catches a rename:
 *   1. A REQUIRED MEMBER IS ABSENT.
 *   2. A MEMBER NAME IS NOT lowerCamelCase, when the contract says it must be.
 *      This is what catches a snake_case projection: `ret_code` fails the naming
 *      rule as well as the membership one, so the diagnostic names both.
 *   3. A MEMBER IS PRESENT THAT THE CONTRACT DOES NOT DECLARE. Legitimate
 *      because these schemas set `additionalProperties: false`; it is the half
 *      that makes a rename a failure rather than merely a missing member, and it
 *      is what a candidate-key list could never do.
 *
 * @param value the parsed object
 * @param memberContract the schema's member set
 * @param context what was being read, for the diagnostic
 * @returns the object, narrowed, so a caller can go on to read members from it
 */
export function assertMembers(
  value: unknown,
  memberContract: MemberContract,
  context: string,
): Record<string, unknown> {
  const record: Record<string, unknown> = assertJsonObject(value, context);
  const present: readonly string[] = Object.keys(record);

  const missing: readonly string[] = memberContract.required.filter(
    (member: string) => !Object.prototype.hasOwnProperty.call(record, member),
  );

  if (missing.length > 0) {
    fail(
      `${context} is missing the required member(s) ${missing.join(', ')} that ` +
        `schema ${memberContract.schema} declares. Present: ` +
        `${present.join(', ') || '(none)'}.`,
    );
  }

  if (memberContract.lowerCamelCase) {
    const misnamed: readonly string[] = present.filter(
      (member: string) => !LOWER_CAMEL_CASE.test(member),
    );

    if (misnamed.length > 0) {
      fail(
        `${context} carries the member name(s) ${misnamed.join(', ')}, which are ` +
          'not lowerCamelCase. Every member this refactor publishes is ' +
          'lowerCamelCase, so a snake_case or PascalCase spelling means the ' +
          'projection changed its serialization contract.',
      );
    }
  }

  const permitted: readonly string[] = [
    ...memberContract.required,
    ...memberContract.optional,
  ];

  const unexpected: readonly string[] = present.filter(
    (member: string) => !permitted.includes(member),
  );

  if (unexpected.length > 0) {
    fail(
      `${context} carries the member(s) ${unexpected.join(', ')}, which schema ` +
        `${memberContract.schema} does not declare. That schema sets ` +
        'additionalProperties: false, so an undeclared member is a contract ' +
        `change. Declared: ${permitted.join(', ')}.`,
    );
  }

  return record;
}

/**
 * Assert a value is one exact token of a closed enumeration.
 *
 * CASE-SENSITIVE, and that is the point: `healthy` is not `Healthy`, and the
 * contract declares the latter. A case-insensitive comparison here would accept
 * a projection that had changed its casing convention — which is a wire change a
 * consumer branching on the token would break on.
 *
 * @param value the value read from the response
 * @param allowed the closed token set, spelled as the contract spells it
 * @param context what was being read, for the diagnostic
 * @returns the token, narrowed to string
 */
export function assertEnumToken(
  value: unknown,
  allowed: readonly string[],
  context: string,
): string {
  if (typeof value !== 'string') {
    fail(
      `${context} is ${value === null ? 'null' : typeof value} where the ` +
        `contract declares one of the string tokens ${allowed.join(', ')}.`,
    );
  }

  if (!allowed.includes(value)) {
    fail(
      `${context} is not one of the tokens the contract declares. Declared: ` +
        `${allowed.join(', ')}. A token outside that set means the closed ` +
        'enumeration changed, and a consumer branching on it would not know.',
    );
  }

  return value;
}

/**
 * Assert a URI equals the FULL expected URI rather than merely ending with it.
 *
 * `endsWith(JWKS_PATH)` used to stand in for this, and it accepted an advertised
 * key set on ANY origin — including one this suite never probed, which is
 * precisely the drift that would make a consumer's stock bearer handler fetch
 * keys from somewhere nobody verified. Origin, port, path and the absence of a
 * query are all part of the identity of the endpoint.
 *
 * Compared through `URL` rather than by string equality so that an equivalent
 * spelling — a default port written out, a trailing slash — is not reported as a
 * mismatch, while a different origin or path still is.
 *
 * @param actual the URI the service advertised
 * @param expected the URI this suite composed from its own configuration
 * @param context what was being read, for the diagnostic
 */
export function assertFullUri(
  actual: unknown,
  expected: string,
  context: string,
): void {
  if (typeof actual !== 'string' || actual.trim().length === 0) {
    fail(
      `${context} is not a non-empty string, so it advertises no absolute URI. ` +
        `The expected value is ${expected}.`,
    );
  }

  let actualUrl: URL;

  try {
    actualUrl = new URL(actual);
  } catch {
    fail(
      `${context} is not an absolute URI. A relative value cannot configure a ` +
        `consumer that holds no base address. The expected value is ${expected}.`,
    );
  }

  const expectedUrl = new URL(expected);
  const normalize = (url: URL): string =>
    `${url.protocol}//${url.host}${url.pathname.replace(/\/$/, '')}`;

  if (normalize(actualUrl) !== normalize(expectedUrl)) {
    fail(
      `${context} advertises ${normalize(actualUrl)} where this suite's own ` +
        `configuration composes ${normalize(expectedUrl)}. A suffix match used ` +
        'to accept this: any origin would satisfy it, including one no test ' +
        'ever probes.',
    );
  }

  if (actualUrl.search.length > 0 || actualUrl.hash.length > 0) {
    fail(
      `${context} carries a query or fragment. The published endpoint has ` +
        'neither, and a consumer that appends one is not fetching the endpoint ' +
        'this suite verified.',
    );
  }
}

/* ------------------------------------------------------------------------- *
 * Canonical member reads — the alias tables become alias DETECTORS
 * ------------------------------------------------------------------------- */

/**
 * Read a member by its CANONICAL name, failing when only a variant is present.
 *
 * This is the replacement for the candidate-key resolution both workflow specs
 * used. Those tables listed the canonical mapping's spelling first and plausible
 * alternatives after it, and returned whichever appeared — so `ret_code`,
 * `is_final` and `Rows` all satisfied a read, and a projection that renamed a
 * field passed every assertion downstream of it.
 *
 * The table survives, and so does its diagnostic value; only the verdict
 * changed. The canonical name is now REQUIRED, and a variant found in its place
 * is reported as the drift it is, naming both spellings. That is the tolerant
 * reader in its proper role: it tells you what happened AFTER the exact
 * assertion has already failed the run.
 *
 * Presence is tested with `hasOwnProperty` rather than against `undefined`, so a
 * member that is present and null reads as present — null is a value of its own
 * in the legacy scalar domain, not an absence.
 *
 * @param container the object to read from
 * @param canonical the contract's spelling — the only accepted one
 * @param variants spellings that are NOT accepted, named in the failure
 * @param context what was being read, for the diagnostic
 * @returns the member's value, or undefined when the container is not an object
 *          or carries neither the canonical name nor any variant
 */
export function readCanonicalMember(
  container: unknown,
  canonical: string,
  variants: readonly string[],
  context: string,
): unknown {
  if (typeof container !== 'object' || container === null || Array.isArray(container)) {
    return undefined;
  }

  const record = container as Record<string, unknown>;

  if (Object.prototype.hasOwnProperty.call(record, canonical)) {
    return record[canonical];
  }

  const drifted: string | undefined = variants.find((variant: string) =>
    Object.prototype.hasOwnProperty.call(record, variant),
  );

  if (drifted !== undefined) {
    fail(
      `${context} carries "${drifted}" where the contract declares ` +
        `"${canonical}". This suite once accepted either. It no longer does: a ` +
        'response-field rename is the most likely drift on a freshly decomposed ' +
        'boundary, and a reader that accepts both spellings cannot detect it.',
    );
  }

  return undefined;
}

/* ------------------------------------------------------------------------- *
 * Contract cross-check — the expected values are re-derived, not just restated
 * ------------------------------------------------------------------------- */

/** The published contract documents this module reads. */
export const GATEWAY_CONTRACT_PATH =
  'shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml';

/** Security's published contract, read by the authentication spec's coherence check. */
export const SECURITY_CONTRACT_PATH =
  'shared/PowerFramework.Contracts/OpenApi/security.v1.yaml';

/**
 * Walk upwards from this file until the repository root is found.
 *
 * Identified by the solution file rather than by `.git`, because a worktree or a
 * submodule checkout does not always carry a `.git` DIRECTORY, and because the
 * solution is the artifact being reasoned about — the same tree that holds the
 * services and their contracts.
 *
 * @returns the absolute repository-root path
 * @throws Error when no root is found above this file
 */
export function repositoryRoot(): string {
  let candidate: string = __dirname;

  for (;;) {
    if (existsSync(join(candidate, 'PowerFramework.slnx'))) {
      return candidate;
    }

    const parent: string = dirname(candidate);

    if (parent === candidate) {
      throw new Error(
        'No repository root carrying PowerFramework.slnx was found above this ' +
          'suite. The contract coherence assertions cannot read the published ' +
          'contracts or the service settings without it.',
      );
    }

    candidate = parent;
  }
}

/**
 * Read a repository file as text.
 *
 * READ-ONLY against the repository, and confined by the caller to the paths the
 * coherence assertions name — none of which is inside `ws_objects/` or any other
 * read-only behavioural-oracle region (C-C).
 *
 * @param relativePath repository-root-relative path
 * @returns the file contents
 */
export function readRepositoryText(relativePath: string): string {
  return readFileSync(join(repositoryRoot(), relativePath), 'utf8');
}

/**
 * The member names and enum tokens found inside one named schema of a contract.
 */
interface SchemaFacts {
  /** Every `propertyName:` declared directly under the schema's `properties:`. */
  readonly members: readonly string[];

  /** Every token of every `enum:` sequence anywhere inside the schema block. */
  readonly enumTokens: readonly string[];

  /** The `required:` list, however the document spells it (block or flow). */
  readonly required: readonly string[];
}

/**
 * Extract one named schema's facts from an OpenAPI document, read as TEXT.
 *
 * NO YAML PARSER, and none may be added: this suite's dependencies are pinned to
 * the runner, its types and the compiler, and the refactor forbids adding a
 * package for convenience. A parser is not needed either. The document is
 * machine-authored with stable two-space indentation, schemas sit at a known
 * depth under `components: schemas:`, and the facts wanted are member names,
 * `required` entries and `enum` tokens — all of which are readable by walking the
 * block with indentation as the only structure.
 *
 * The scan is deliberately literal about what it will believe: it takes the block
 * from the schema's own line until the next line at the same or shallower
 * indentation, so it cannot bleed into a sibling schema, and it reads member
 * names only at the one depth directly beneath `properties:`, so a nested
 * object's members are not mistaken for the schema's own.
 *
 * @param documentText the contract document
 * @param schemaName the schema to locate, e.g. `AggregateHealthReport`
 * @returns the schema's members, required list and enum tokens
 * @throws Error when the schema is not present in the document
 */
export function readSchemaFacts(
  documentText: string,
  schemaName: string,
): SchemaFacts {
  const lines: readonly string[] = documentText.split(/\r?\n/);
  const header = new RegExp(`^(\\s+)${schemaName}:\\s*$`);

  let start = -1;
  let indent = '';

  for (let index = 0; index < lines.length; index += 1) {
    const match: RegExpMatchArray | null = (lines[index] ?? '').match(header);

    if (match) {
      start = index;
      indent = match[1] ?? '';
      break;
    }
  }

  if (start < 0) {
    throw new Error(
      `The published contract declares no schema named "${schemaName}". This ` +
        'suite asserts against that schema, so either the contract renamed it ' +
        'or the assertion names a schema that never existed.',
    );
  }

  const block: string[] = [];

  for (let index = start + 1; index < lines.length; index += 1) {
    const line: string = lines[index] ?? '';

    if (line.trim().length === 0) {
      block.push(line);
      continue;
    }

    const leading: number = line.length - line.trimStart().length;

    if (leading <= indent.length) {
      break;
    }

    block.push(line);
  }

  const members: string[] = [];
  const enumTokens: string[] = [];
  const required: string[] = [];

  let propertiesIndent: number | undefined;
  let enumIndent: number | undefined;

  for (const line of block) {
    if (line.trim().length === 0) {
      continue;
    }

    const leading: number = line.length - line.trimStart().length;
    const text: string = line.trim();

    // `properties:` opens the one depth at which member names are read.
    if (text === 'properties:') {
      propertiesIndent = leading;
      continue;
    }

    if (
      propertiesIndent !== undefined &&
      leading === propertiesIndent + 2 &&
      /^[A-Za-z_][A-Za-z0-9_]*:/.test(text)
    ) {
      members.push(text.slice(0, text.indexOf(':')));
    }

    // `required:` in flow form on one line, or in block form as `- name` entries.
    const flowRequired: RegExpMatchArray | null = text.match(/^required:\s*\[(.*)\]$/);

    if (flowRequired) {
      for (const entry of (flowRequired[1] ?? '').split(',')) {
        const name: string = entry.trim().replace(/^['"]|['"]$/g, '');

        if (name.length > 0) {
          required.push(name);
        }
      }
      continue;
    }

    if (text === 'required:') {
      enumIndent = undefined;
      continue;
    }

    // `enum: [a, b, c]` — the FLOW form, which this document uses for most of its
    // closed sets. Handled before the block form because the block form's opener
    // is the same keyword with nothing after it.
    const flowEnum: RegExpMatchArray | null = text.match(/^enum:\s*\[(.*)\]$/);

    if (flowEnum) {
      for (const entry of (flowEnum[1] ?? '').split(',')) {
        const token: string = entry.trim().replace(/^['"]|['"]$/g, '');

        if (token.length > 0) {
          enumTokens.push(token);
        }
      }
      enumIndent = undefined;
      continue;
    }

    // `const: value` — a one-token closed set, which is what the document uses
    // where a member may hold exactly one value: the reporting service's own
    // name, the reserved routes' `501` and their marker string. Collected
    // alongside the enum tokens because a caller asserting such a value needs
    // the identical cross-check that the contract still declares it.
    const constant: RegExpMatchArray | null = text.match(/^const:\s*(.+)$/);

    if (constant) {
      enumTokens.push((constant[1] ?? '').trim().replace(/^['"]|['"]$/g, ''));
      enumIndent = undefined;
      continue;
    }

    // `enum:` alone opens a BLOCK sequence of tokens at the next depth.
    if (text === 'enum:') {
      enumIndent = leading;
      continue;
    }

    if (enumIndent !== undefined) {
      if (leading <= enumIndent) {
        enumIndent = undefined;
      } else if (text.startsWith('- ')) {
        enumTokens.push(text.slice(2).trim().replace(/^['"]|['"]$/g, ''));
        continue;
      }
    }
  }

  return {
    members: Object.freeze(members),
    required: Object.freeze(required),
    enumTokens: Object.freeze(enumTokens),
  };
}

/**
 * Cross-check one member contract against the schema it claims to come from.
 *
 * This is what keeps the constants above from becoming a second, drifting
 * description of the contract. Every required member must still be required in
 * the document, every optional member must still be declared, and — the direction
 * that matters most — every member the DOCUMENT declares must appear in this
 * module, so a member added to the contract cannot go unasserted.
 *
 * @param documentText the contract document
 * @param memberContract the member set to verify
 * @throws Error when the module and the document disagree
 */
export function assertContractDeclaresShape(
  documentText: string,
  memberContract: MemberContract,
): void {
  const facts: SchemaFacts = readSchemaFacts(documentText, memberContract.schema);
  const declared: readonly string[] = [
    ...memberContract.required,
    ...memberContract.optional,
  ];

  const undeclaredHere: readonly string[] = facts.members.filter(
    (member: string) => !declared.includes(member),
  );

  if (undeclaredHere.length > 0) {
    throw new Error(
      `Schema ${memberContract.schema} declares the member(s) ` +
        `${undeclaredHere.join(', ')} that fixtures/contract-shape.ts does not ` +
        'list, so this suite would neither require nor permit them. Add them to ' +
        'the member contract as required or optional, per the schema.',
    );
  }

  const absentFromSchema: readonly string[] = declared.filter(
    (member: string) => !facts.members.includes(member),
  );

  if (absentFromSchema.length > 0) {
    throw new Error(
      `fixtures/contract-shape.ts lists the member(s) ` +
        `${absentFromSchema.join(', ')} for schema ${memberContract.schema}, ` +
        'which the published contract no longer declares.',
    );
  }

  const requiredMismatch: readonly string[] = memberContract.required.filter(
    (member: string) => !facts.required.includes(member),
  );

  if (requiredMismatch.length > 0) {
    throw new Error(
      `fixtures/contract-shape.ts requires ${requiredMismatch.join(', ')} on ` +
        `schema ${memberContract.schema}, which the published contract does not ` +
        'mark required. Requiring more than the contract does would invent a ' +
        'requirement.',
    );
  }

  const optionalButRequired: readonly string[] = facts.required.filter(
    (member: string) => !memberContract.required.includes(member),
  );

  if (optionalButRequired.length > 0) {
    throw new Error(
      `The published contract marks ${optionalButRequired.join(', ')} required ` +
        `on schema ${memberContract.schema}, and ` +
        'fixtures/contract-shape.ts treats it as optional, so this suite would ' +
        'pass over a response that omitted it.',
    );
  }
}

/**
 * Cross-check a token set against the enum tokens the named schema declares.
 *
 * ONE DIRECTION ONLY: every token this module names must still be declared inside
 * that schema. The opposite direction — every token the schema declares must be
 * named here — is {@link assertContractTokensExhaustive}, and it is separate
 * because the two have different granularity. This one is per token SET, of which
 * a schema may have several; exhaustiveness can only be judged per SCHEMA, against
 * the union of every set drawn from it.
 *
 * @param documentText the contract document
 * @param schemaName the schema whose enum is being verified
 * @param tokens the token set this module declares
 * @throws Error when a token this module names is not declared in that schema
 */
export function assertContractDeclaresTokens(
  documentText: string,
  schemaName: string,
  tokens: readonly string[],
): void {
  const facts: SchemaFacts = readSchemaFacts(documentText, schemaName);

  const missing: readonly string[] = tokens.filter(
    (token: string) => !facts.enumTokens.includes(token),
  );

  if (missing.length > 0) {
    throw new Error(
      `fixtures/contract-shape.ts expects the token(s) ${missing.join(', ')} ` +
        `inside schema ${schemaName}, which the published contract does not ` +
        'declare.',
    );
  }
}

/**
 * The direction that matters most: EVERY token the schema declares must be named
 * here.
 *
 * WITHOUT THIS, ADDING A VALUE TO A CLOSED ENUMERATION IS INVISIBLE. That was
 * measured rather than reasoned about: a probe added `Starting` to
 * `UpstreamHealth.status` in the published contract and the guard passed, because
 * checking that every EXPECTED token is declared says nothing about tokens the
 * contract has that the expectation does not. A suite asserting
 * `assertEnumToken(value, UPSTREAM_STATUS_TOKENS, …)` would then have rejected a
 * perfectly conformant `Starting` as "not one of the tokens the contract
 * declares" — a false accusation, arising from a stale fixture the guard had
 * declared healthy.
 *
 * Judged per SCHEMA against the union of every token set drawn from it, because
 * {@link readSchemaFacts} collects the tokens of every `enum:` and every `const:`
 * inside a schema block without attributing them to a property. That is coarser
 * than per-property attribution and it is sufficient: the question here is whether
 * this module knows about every token the schema mentions, and a token that moved
 * from one property of a schema to another is caught by
 * {@link assertContractDeclaresTokens} plus the member-set check.
 *
 * @param documentText the contract document
 * @param schemaName the schema being verified
 * @param knownTokens the union of every token this module draws from that schema
 * @throws Error when the schema declares a token this module does not name
 */
export function assertContractTokensExhaustive(
  documentText: string,
  schemaName: string,
  knownTokens: readonly string[],
): void {
  const facts: SchemaFacts = readSchemaFacts(documentText, schemaName);

  const unknown: readonly string[] = facts.enumTokens.filter(
    (token: string) => !knownTokens.includes(token),
  );

  if (unknown.length > 0) {
    throw new Error(
      `Schema ${schemaName} declares the token(s) ${[...new Set(unknown)].join(', ')} ` +
        'that fixtures/contract-shape.ts does not name. A closed enumeration ' +
        'this suite asserts against has grown a value, so a conforming response ' +
        'carrying it would be rejected as non-conforming. Add the token to the ' +
        'set it belongs to.',
    );
  }
}

/* ------------------------------------------------------------------------- *
 * Diagnostics — tolerant reads, permitted ONLY after an exact assertion fails
 * ------------------------------------------------------------------------- */

/**
 * Describe a body's SHAPE without quoting its contents.
 *
 * The tolerant reader in the only role it may still play. When an exact assertion
 * has already failed, a reader needs to know what arrived — but a conflict
 * payload is the one body in this suite being checked for leaked material, and a
 * token response is a credential, so republishing either in a failure message
 * would be the leak. So this reports STRUCTURE: whether the body parsed, whether
 * it is an object or an array, its member names, and its length. Never a value.
 *
 * @param bodyText the body exactly as it arrived
 * @returns a single-line structural description, free of any response value
 */
export function describeShapeForFailure(bodyText: string): string {
  let parsed: unknown;

  try {
    parsed = JSON.parse(bodyText) as unknown;
  } catch {
    return `body did not parse as JSON (${bodyText.length} characters)`;
  }

  if (Array.isArray(parsed)) {
    return `body is a JSON array of ${parsed.length} element(s)`;
  }

  if (typeof parsed === 'object' && parsed !== null) {
    const names: readonly string[] = Object.keys(parsed);

    return `body is a JSON object with member(s): ${names.join(', ') || '(none)'}`;
  }

  return `body is a bare JSON ${parsed === null ? 'null' : typeof parsed}`;
}
