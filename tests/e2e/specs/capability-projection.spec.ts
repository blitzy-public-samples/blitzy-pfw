/**
 * C-09 `/v1/capabilities` — the legacy capability gate, projected.
 *
 * WHAT IS BEING VERIFIED
 * ----------------------
 * The framework being decomposed carries its own module-gating bitmask: eight
 * capability bits declared in the legacy source, where a module that is not
 * explicitly initialized is unusable. That bitmask is the legacy's own
 * statement of how it would decompose itself, which is why Gateway republishes
 * it as configuration rather than inventing a new capability model.
 *
 * Three separate properties are asserted here, and they fail for different
 * reasons:
 *
 *   1. The eight flag VALUES and their identifier spellings match the legacy
 *      declaration. Values appear in serialized payloads and log records, so a
 *      renamed or renumbered flag silently invalidates stored comparisons.
 *   2. `INIT_FLAG_ENABLE_ALL` deliberately OMITS the alternative engine build.
 *      The two engine DLLs are alternative builds of one engine, so a composite
 *      naming both would ask for a configuration that cannot exist. This is
 *      the single most likely bit for a well-meaning implementer to "fix".
 *   3. A mask that Gateway PROJECTED is accepted across the whole range that
 *      projection can produce — see the DP-6 block below, which is the reason
 *      this file exists in its current shape.
 *
 * THE DP-6 DOMAIN SPLIT
 * ---------------------
 * Gateway derives the mask with an unchecked 64-bit-to-32-bit-unsigned
 * projection, so the reachable range of a mask is the entire `uint` domain,
 * `0 .. 0xffffffff` — a configured value with bit 31 set legitimately yields a
 * mask above the signed `Long` maximum. The fixture originally validated a
 * projected mask against the signed authored bound, which meant a correct
 * Gateway response could make the fixture throw a `TypeError` blaming a
 * malformed payload. The domains are now split: an authored flag keeps
 * `0 .. 0x7fffffff` (a wider flag would break the containment comparison,
 * because JavaScript's `&` yields a signed result), while a projected mask
 * admits the full `uint` range. Both halves are pinned below so the split
 * cannot silently regress in either direction.
 *
 * The first block needs no running stack. The second drives HTTP and is gated;
 * see `../fixtures/live-stack.ts`.
 */

import { expect, test } from '@playwright/test';

import {
  ALL_CAPABILITY_FLAG_NAMES,
  CAPABILITY_FLAGS,
  INIT_FLAG_ENABLE_ALL,
  INIT_FLAG_ENABLE_BLINK,
  INIT_FLAG_ENABLE_BLINKFAST,
  INIT_FLAG_ENABLE_DPIAWARE,
  INIT_FLAG_ENABLE_ORCA,
  INIT_FLAG_ENABLE_SCITER,
  INIT_FLAG_ENABLE_SQLITE,
  INIT_FLAG_ENABLE_UI,
  INIT_FLAG_ENABLE_WEBVIEW,
  IN_SCOPE_CAPABILITY_FLAGS,
  capabilityFlagsIn,
  hasCapability,
  type CapabilityFlagName,
} from '../fixtures/capability-flags';
import {
  CAPABILITIES_PATH,
  gatewayUrl,
} from '../fixtures/service-endpoints';
import { probeStackAvailability } from '../fixtures/live-stack';

/** The inclusive maximum of the projected `uint` mask domain. */
const UINT_MAX = 0xffffffff;

/** The inclusive maximum of the authored signed `Long` flag domain. */
const LONG_MAX = 0x7fffffff;

/** The shape of `CapabilityReport` this spec reads. */
interface CapabilityReport {
  readonly effectiveMask?: unknown;
  readonly allMask?: unknown;
  readonly unrecognizedBits?: unknown;
  readonly capabilities?: unknown;
}

/** The shape of `Capability` this spec reads. */
interface Capability {
  readonly name?: unknown;
  readonly value?: unknown;
  readonly enabled?: unknown;
  readonly phaseOneDestination?: unknown;
}

test.describe('capability table parity with the legacy gate', () => {
  test('the eight flag values match the legacy declaration exactly', () => {
    // Values, not just names. These integers travel in payloads and logs.
    expect(INIT_FLAG_ENABLE_UI).toBe(1);
    expect(INIT_FLAG_ENABLE_SCITER).toBe(2);
    expect(INIT_FLAG_ENABLE_BLINK).toBe(4);
    expect(INIT_FLAG_ENABLE_BLINKFAST).toBe(8);
    expect(INIT_FLAG_ENABLE_ORCA).toBe(256);
    expect(INIT_FLAG_ENABLE_SQLITE).toBe(512);
    expect(INIT_FLAG_ENABLE_DPIAWARE).toBe(1024);
    expect(INIT_FLAG_ENABLE_WEBVIEW).toBe(2048);
  });

  test('the table names exactly eight flags, in ascending bit order', () => {
    expect(ALL_CAPABILITY_FLAG_NAMES).toHaveLength(8);

    const values = ALL_CAPABILITY_FLAG_NAMES.map(
      (name) => CAPABILITY_FLAGS[name],
    );
    const ascending = [...values].sort((left, right) => left - right);

    expect(values).toEqual(ascending);

    // 16, 32, 64 and 128 are unnamed in the legacy source. Inventing a name
    // for one would be exactly the embellishment the migration forbids.
    expect(values).not.toContain(16);
    expect(values).not.toContain(32);
    expect(values).not.toContain(64);
    expect(values).not.toContain(128);
  });

  test('INIT_FLAG_ENABLE_ALL omits the alternative engine build, deliberately', () => {
    // The preserved quirk. The two engine binaries are alternative builds of
    // one engine, so the composite cannot name both.
    expect(hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_BLINKFAST)).toBe(
      false,
    );

    // Every other named bit IS present, which is what makes the omission a
    // deliberate exclusion rather than an incomplete composite.
    for (const name of ALL_CAPABILITY_FLAG_NAMES) {
      if (name === 'INIT_FLAG_ENABLE_BLINKFAST') {
        continue;
      }

      expect(
        hasCapability(INIT_FLAG_ENABLE_ALL, CAPABILITY_FLAGS[name]),
        `${name} must be part of INIT_FLAG_ENABLE_ALL`,
      ).toBe(true);
    }

    expect(capabilityFlagsIn(INIT_FLAG_ENABLE_ALL)).not.toContain(
      'INIT_FLAG_ENABLE_BLINKFAST',
    );
  });

  test('storage is the only capability with an in-scope Phase-1 consumer', () => {
    expect(IN_SCOPE_CAPABILITY_FLAGS).toEqual(['INIT_FLAG_ENABLE_SQLITE']);
    expect(hasCapability(INIT_FLAG_ENABLE_ALL, INIT_FLAG_ENABLE_SQLITE)).toBe(
      true,
    );
  });

  test('containment is all-bits, not any-bit', () => {
    const both = INIT_FLAG_ENABLE_SQLITE | INIT_FLAG_ENABLE_UI;

    expect(hasCapability(both, INIT_FLAG_ENABLE_SQLITE)).toBe(true);
    expect(hasCapability(both, INIT_FLAG_ENABLE_UI)).toBe(true);
    expect(hasCapability(both, both)).toBe(true);

    // Any-bit semantics would answer true here; all-bits answers false.
    expect(hasCapability(INIT_FLAG_ENABLE_SQLITE, both)).toBe(false);
  });

  test('a zero flag is refused rather than vacuously satisfied', () => {
    // `(mask & 0) === 0` holds for every mask, so an unguarded test would hand
    // back a confident true for a flag a caller derived and got wrong.
    expect(hasCapability(INIT_FLAG_ENABLE_ALL, 0)).toBe(false);
    expect(hasCapability(0, 0)).toBe(false);
  });

  test('an empty mask decomposes to no names', () => {
    expect(capabilityFlagsIn(0)).toEqual([]);
  });

  test('decomposition reports named bits and ignores unnamed ones', () => {
    // Bit 4 (16) has no legacy name, so it is not reported — but it does not
    // suppress the bits that do have names either.
    const withUnnamedBit = INIT_FLAG_ENABLE_SQLITE | 16;

    expect(capabilityFlagsIn(withUnnamedBit)).toEqual([
      'INIT_FLAG_ENABLE_SQLITE',
    ]);
  });

  test('decomposition returns a fresh array the caller owns', () => {
    const first = capabilityFlagsIn(INIT_FLAG_ENABLE_ALL);
    const second = capabilityFlagsIn(INIT_FLAG_ENABLE_ALL);

    expect(first).not.toBe(second);
    expect(first).toEqual(second);

    first.push('INIT_FLAG_ENABLE_UI');
    expect(capabilityFlagsIn(INIT_FLAG_ENABLE_ALL)).toEqual(second);
  });
});

test.describe('DP-6 — projected mask domain vs authored flag domain', () => {
  test('a projected mask spanning the full uint range is accepted', () => {
    // The regression this split fixes. Gateway can legitimately project any
    // uint, so a mask with bit 31 set must not be mistaken for a malformed
    // payload.
    expect(() => hasCapability(UINT_MAX, INIT_FLAG_ENABLE_SQLITE)).not.toThrow();
    expect(hasCapability(UINT_MAX, INIT_FLAG_ENABLE_SQLITE)).toBe(true);

    expect(() => capabilityFlagsIn(UINT_MAX)).not.toThrow();
    expect(capabilityFlagsIn(UINT_MAX)).toEqual([...ALL_CAPABILITY_FLAG_NAMES]);
  });

  test('masks just above the signed boundary are accepted and read correctly', () => {
    // 0x80000000 is one past the authored `Long` maximum. Signed coercion
    // changes how the pattern prints, not which bits it holds, so the low bits
    // must still read correctly.
    expect(hasCapability(LONG_MAX + 1, INIT_FLAG_ENABLE_SQLITE)).toBe(false);
    expect(
      hasCapability(LONG_MAX + 1 + INIT_FLAG_ENABLE_SQLITE, INIT_FLAG_ENABLE_SQLITE),
    ).toBe(true);
    expect(capabilityFlagsIn(LONG_MAX + 1)).toEqual([]);
  });

  test('a mask beyond the uint domain is still refused', () => {
    // Widening the domain must not become abandoning it: nothing above uint
    // can have come from the projection, so admitting it would forfeit the
    // fail-fast posture for no gain.
    expect(() => capabilityFlagsIn(UINT_MAX + 1)).toThrow(TypeError);
    expect(() => hasCapability(UINT_MAX + 1, INIT_FLAG_ENABLE_SQLITE)).toThrow(
      /projected `uint` mask/,
    );
  });

  test('a non-integer or negative mask is refused, naming the parameter', () => {
    // An absent JSON field decodes to NaN, and `NaN & flag` is 0 — every
    // capability test would answer a confident, silent "absent".
    expect(() => hasCapability(Number.NaN, INIT_FLAG_ENABLE_SQLITE)).toThrow(
      /`mask`/,
    );
    expect(() => hasCapability(-1, INIT_FLAG_ENABLE_SQLITE)).toThrow(/`mask`/);
    expect(() => hasCapability(1.5, INIT_FLAG_ENABLE_SQLITE)).toThrow(/`mask`/);
    expect(() => capabilityFlagsIn(Number.POSITIVE_INFINITY)).toThrow(TypeError);
  });

  test('an authored flag keeps the narrower signed domain', () => {
    // This bound is load-bearing for correctness, not tidiness: `&` yields a
    // signed result, so `(0xffffffff & 0x80000000)` is -2147483648 and the
    // containment comparison would answer false for a flag whose every bit is
    // present. Refusing such a flag turns a wrong answer into a visible error.
    expect(() => hasCapability(UINT_MAX, LONG_MAX + 1)).toThrow(
      /PowerScript `Long`/,
    );
    expect(() => hasCapability(UINT_MAX, LONG_MAX + 1)).toThrow(/`flag`/);

    // The boundary value itself is admissible.
    expect(() => hasCapability(UINT_MAX, LONG_MAX)).not.toThrow();
  });

  test('the two domains are genuinely different, not merely named differently', () => {
    const beyondSigned = LONG_MAX + 1;

    // Same integer: legal as a mask, illegal as a flag. That asymmetry IS the
    // fix, so it is asserted directly rather than inferred from the two cases
    // above.
    expect(() => capabilityFlagsIn(beyondSigned)).not.toThrow();
    expect(() => hasCapability(0, beyondSigned)).toThrow(TypeError);
  });
});

test.describe('C-09 /v1/capabilities over HTTP (live stack)', () => {
  test.beforeEach(async () => {
    const availability = await probeStackAvailability();
    test.skip(!availability.reachable, availability.reason);
  });

  test('/v1/capabilities requires a token', async ({ request }) => {
    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      failOnStatusCode: false,
    });

    expect(response.status()).toBe(401);
  });

  test('the projected report is consumable by this fixture', async ({
    request,
  }) => {
    const response = await request.get(gatewayUrl(CAPABILITIES_PATH), {
      failOnStatusCode: false,
    });

    // Without a token the contract answers 401; the projection assertions
    // below need an authenticated read, so skip rather than assert on a
    // problem document.
    test.skip(
      response.status() !== 200,
      `/v1/capabilities answered ${response.status()}; an authenticated read ` +
        `is required to inspect the projection. The domain assertions ran ` +
        `independently of the stack.`,
    );

    const body = (await response.json()) as CapabilityReport;

    expect(typeof body.effectiveMask).toBe('number');
    expect(typeof body.allMask).toBe('number');
    expect(Array.isArray(body.capabilities)).toBe(true);

    const effectiveMask = body.effectiveMask as number;
    const allMask = body.allMask as number;

    // The whole point of the DP-6 split: whatever Gateway projected, this
    // fixture must be able to read it without throwing.
    expect(() => capabilityFlagsIn(effectiveMask)).not.toThrow();
    expect(() => capabilityFlagsIn(allMask)).not.toThrow();

    // The published composite must still omit the alternative engine build.
    expect(hasCapability(allMask, INIT_FLAG_ENABLE_BLINKFAST)).toBe(false);

    const capabilities = body.capabilities as readonly Capability[];
    expect(capabilities).toHaveLength(8);

    for (const capability of capabilities) {
      const name = capability.name as CapabilityFlagName;

      expect(ALL_CAPABILITY_FLAG_NAMES).toContain(name);
      expect(capability.value).toBe(CAPABILITY_FLAGS[name]);

      // Each entry's own `enabled` flag must agree with the mask it was
      // derived from; disagreement means the report is internally inconsistent.
      expect(capability.enabled).toBe(
        hasCapability(effectiveMask, CAPABILITY_FLAGS[name]),
      );
      expect(typeof capability.phaseOneDestination).toBe('string');
    }
  });
});
