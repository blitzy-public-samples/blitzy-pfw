/**
 * The fixture barrel for the PowerFramework cross-service end-to-end suite —
 * the single import path into this folder.
 *
 * Every spec under `tests/e2e/specs/**` reaches the fixtures through this one
 * module:
 *
 *     import { GATEWAY_BASE_URL, LEGACY_SEED_ROWS } from '../fixtures';
 *
 * One path is the whole point of the file. It is what makes this folder a
 * single source of truth rather than four modules each spec has to locate for
 * itself, and it is what lets a fixture move between siblings without editing
 * a spec.
 *
 * WHAT EACH SIBLING OWNS
 * ----------------------
 * - `service-endpoints` — the four base URLs, the endpoint table and the
 *   in-scope path constants. Gateway on 5105 is the sole functional base URL,
 *   and the reserved Phase-2 port slot deliberately has no entry at all.
 * - `company-rows` — the verified `COMPANY` schema and the deterministic row
 *   builders.
 * - `capability-flags` — the eight `INIT_FLAG_ENABLE_*` values, with the
 *   composite `INIT_FLAG_ENABLE_ALL` at 3847.
 * - `auth` — run-time token acquisition, plus the explicitly unauthenticated
 *   path.
 *
 * Each sibling carries its own documentation. The map above is a directory
 * rather than a summary, and it is held to one line each precisely so that it
 * cannot drift out of step with four headers at once.
 *
 * WHY THIS FILE HOLDS NOTHING BUT RE-EXPORTS
 * -----------------------------------------
 * This is the first module every spec evaluates, so it is the first thing that
 * can break collection. `npx playwright test --list` has to typecheck and
 * collect the whole suite with no stack running (C-L), and it can only do that
 * if loading this file does nothing at all: no top-level asynchronous work, no
 * environment read, no request, no state, no failure raised while loading.
 * A barrel is also the one place no reader thinks to look, so logic put here
 * would be logic nobody finds.
 *
 * It declares no value of its own, which is why it can carry no credential
 * (C-F), and it starts, probes and waits for nothing, because bringing the
 * stack up belongs to the orchestration path and never to a fixture (C-J).
 *
 * WHY `export *` RATHER THAN A NAME LIST
 * --------------------------------------
 * The four surfaces are disjoint — 65 exported identifiers with no name owned
 * by two modules — so a star re-export cannot introduce an ambiguity. It is
 * also the form `isolatedModules` accepts unconditionally: a *named* re-export
 * of an entity that turns out to be a type is rejected under that flag, and
 * eleven of these names are types, so the star form carries values and types
 * alike without splitting the surface into two clauses that would then both
 * need maintaining. Should a collision ever arise, it is resolved here with
 * explicit named re-exports and recorded here in a comment — the sibling
 * names are contract surface the specs refer to, and are never renamed to
 * paper one over.
 *
 * The run-time stack-availability helper that sits beside these four keeps its
 * own import path and is deliberately not re-exported here.
 */

export * from './service-endpoints';
export * from './company-rows';
export * from './capability-flags';
export * from './auth';
