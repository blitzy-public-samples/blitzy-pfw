/**
 * The run mode — one place that decides whether this run is a full acceptance run
 * or a deliberately partial one, and says so where every reader will see it.
 *
 * WHY THIS EXISTS AS ITS OWN MODULE
 * --------------------------------
 * Two consumers need the same answer and they cannot share a module with anything
 * else:
 *
 * - `live-stack.ts` needs it to decide whether an absent stack FAILS the run or
 *   skips a test. It imports the runner, so it cannot be the source of truth for
 *   the config.
 * - `playwright.config.ts` needs it to NAME the project, so that a partial run is
 *   labelled in every reported line rather than only inside a skip reason. The
 *   config is loaded before any spec and must not import the runner's `test`
 *   object, so it cannot read the answer out of `live-stack.ts`.
 *
 * So the decision lives here, in a module that imports NOTHING: no runner, no
 * fixture, no filesystem. It reads two environment variables and computes two
 * booleans, and it is safe to import from either side.
 *
 * WHY THE DEFAULT IS THE STRICT MODE
 * ----------------------------------
 * A full acceptance run is what an unconfigured invocation gets. That is the whole
 * point: the state a misconfigured pipeline is in — nothing set, nothing running —
 * must be the state that FAILS, because it is indistinguishable from a correctly
 * configured pipeline whose stack did not come up. Inferring "the operator meant a
 * partial run" from the absence of a stack is precisely the design that produces a
 * green report over a suite that exercised nothing.
 *
 * THE PARALLEL WITH `token-issuance.ts` IS DELIBERATE
 * --------------------------------------------------
 * That module already applies this exact shape to the OTHER precondition this suite
 * has — a missing caller credential — with its own opt-in variable, its own
 * accepted values, and a setup failure by default. The stack precondition is now
 * expressed the same way rather than in a second idiom, because two preconditions
 * that behave differently is how a reader comes to believe the strict one is the
 * exception. The two variables are separate on purpose: acknowledging that no
 * credential is provisioned says nothing about whether a stack is running, and
 * conflating them would let one acknowledgement suppress two different findings.
 *
 * SECRET-FREE (C-F). No value read here is a credential, and no message built from
 * it echoes a value — only variable names and fixed prose.
 */

/**
 * The environment variable a run sets to declare that it knows the stack may be
 * absent and accepts a partial result.
 *
 * Named rather than inlined so the hook, the skip reason, the project label and the
 * documentation all say the same string.
 */
export const ABSENT_STACK_VARIABLE: string = 'E2E_ALLOW_ABSENT_STACK';

/**
 * The values {@link ABSENT_STACK_VARIABLE} accepts as an acknowledgement.
 *
 * Exported because the operator-facing message that reports an unrecognised value has
 * to quote them, and a message quoting its own second copy of this list is how the
 * two come to disagree. DELIBERATELY THE SAME TWO, AND THE SAME CASE-INSENSITIVE
 * MATCHING RULE, AS `E2E_ALLOW_MISSING_ISSUANCE_IDENTITY` in `token-issuance.ts`:
 * that variable is the credential axis of the same question and it established the
 * convention, and two opt-ins in one suite behaving differently is how an operator
 * ends up setting one correctly and the other not.
 */
export const ABSENT_STACK_ACKNOWLEDGEMENT_VALUES: readonly string[] = Object.freeze([
  '1',
  'true',
]);

const ACKNOWLEDGEMENT_VALUES: readonly string[] = ABSENT_STACK_ACKNOWLEDGEMENT_VALUES;

/**
 * The raw acknowledgement value, trimmed, or `undefined` when unset or blank.
 *
 * Bracket notation because `noPropertyAccessFromIndexSignature` is enabled.
 */
const rawAcknowledgement: string | undefined = ((): string | undefined => {
  const configured: string | undefined = process.env[ABSENT_STACK_VARIABLE];

  if (configured === undefined) {
    return undefined;
  }

  const trimmed: string = configured.trim();

  return trimmed.length === 0 ? undefined : trimmed;
})();

/**
 * Whether this run has declared that it accepts an absent stack.
 *
 * `false` for an unset variable and `false` for an unrecognised value. The
 * unrecognised case is reported separately by {@link ABSENT_STACK_VALUE_UNRECOGNISED}
 * so that a typo such as `ture` cannot quietly mean "full acceptance run" and then
 * produce the very setup failure the author was opting out of.
 */
export const ABSENT_STACK_ACKNOWLEDGED: boolean =
  rawAcknowledgement !== undefined &&
  ACKNOWLEDGEMENT_VALUES.includes(rawAcknowledgement.toLowerCase());

/** Whether the variable is set to something this module does not recognise. */
export const ABSENT_STACK_VALUE_UNRECOGNISED: boolean =
  rawAcknowledgement !== undefined && !ABSENT_STACK_ACKNOWLEDGED;

/**
 * The label every reported line carries, which is how a partial run announces
 * itself to a reader of a summary rather than only to a reader of a skip reason.
 *
 * A NAME AND NOT A SUFFIX ON THE SKIP REASON, because a summary line shows counts
 * and a project name, and nothing else. `33 passed, 27 skipped` under a project
 * called `api` is indistinguishable from an acceptance run that happened to skip
 * some tests; the same counts under `api-partial-no-stack` cannot be mistaken for
 * one.
 */
export const PROJECT_LABEL: string = ABSENT_STACK_ACKNOWLEDGED
  ? 'api-partial-no-stack'
  : 'api';

/**
 * The command that brings the stack up, quoted in every message so an operator
 * never has to go and find it.
 */
export const BRING_UP_COMMAND: string =
  'cd orchestration && cp .env.example .env && ' +
  'docker compose --env-file .env up --build -d';

/**
 * The command that runs the suite in the acknowledged partial mode.
 *
 * Quoted in the failure message, so the message that says "this run is not valid"
 * also says what to run instead if a partial one was what was wanted.
 */
export const PARTIAL_RUN_COMMAND: string = 'npm run test:partial';
