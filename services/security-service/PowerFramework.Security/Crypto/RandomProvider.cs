// ==============================================================================================
//  RandomProvider - the random blob, random string and GUID surface, and this folder's
//                   DETERMINISM SEAM
//  --------------------------------------------------------------------------------------------
//  LEGACY SOURCE  ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14-L18, five declarations:
//
//      L14   public function blob   GenRandomBlob(readonly ulong size)
//      L15   public function string GenRandomString(readonly ulong size)
//      L16   public function string GenRandomString(readonly ulong size,readonly ulong flags)
//      L17   public function string GenGUID()
//      L18   public function string GenGUID(readonly ulong flags)
//
//                 plus the two global-function wrappers that sit in front of them:
//
//      ws_objects/pfw.crypto.pbl.src/randomstring.srf:L11, L14-L15
//      ws_objects/pfw.crypto.pbl.src/guid.srf:L11, L14-L15
//
//  NO LEGACY BODY EXISTS. n_crypto.sru:L8 declares
//  `global type n_crypto from nonvisualobject native "pfw.dll"`, so all five behaviours live
//  inside a closed native binary for which no C++ source exists in this repository or anywhere it
//  can be read from. Every member below is therefore a DOCUMENTED SUBSTITUTION against
//  System.Security.Cryptography and the Base Class Library, never a translation of legacy code.
//  No third-party package is added; the BCL covers the whole surface.
//
//  ORACLE STATUS  Every ws_objects/** path cited anywhere in this file is READ ONLY. It is the
//                 behavioural oracle for parity testing, never an edit target. Locators are
//                 provenance for a decision and nothing more: no legacy file is edited, moved,
//                 reformatted or re-exported by this port.
//
//  CENSUS - SEVEN MEMBERS, 5 NATIVE-DERIVED + 2 WRAPPER-DERIVED
//  --------------------------------------------------------------------------------------------
//      member                            legacy declaration              locator
//      ------------------------------    ---------------------------     ---------------------
//      GenRandomBlob(uint)               GenRandomBlob(ulong)            n_crypto.sru:L14
//      GenRandomString(uint)             GenRandomString(ulong)          n_crypto.sru:L15
//      GenRandomString(uint, uint)       GenRandomString(ulong, ulong)   n_crypto.sru:L16
//      GenGuid()                         GenGUID()                       n_crypto.sru:L17
//      GenGuid(uint)                     GenGUID(ulong)                  n_crypto.sru:L18
//      RandomString(uint)                randomstring(unsignedlong)      randomstring.srf:L11
//      NewGuid()                         guid()                          guid.srf:L11
//
//  Those five are RandomProvider's share of the 63-of-65 reconciliation that
//  Crypto/LegacyDefaults.cs records declaration by declaration: 65 declarations, 2 deliberately
//  not ported (Copyright at L9 and GetVersion at L10), 63 ported across six providers, of which
//  this file owns 5. Adding an eighth member here, or dropping one, breaks that reconciliation.
//
//  TWO NAMING RULINGS, BOTH DELIBERATE
//  --------------------------------------------------------------------------------------------
//  1. THE LEGACY SPELLS THE GUID FUNCTION TWO WAYS. n_crypto.sru:L17-L18 declares `GenGUID`,
//     while guid.srf:L15 calls `GenGuid` and
//     ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L682 also calls `GenGuid`.
//     PowerScript identifiers are case-insensitive, so both spellings name ONE function and
//     neither is a bug. The C# member is `GenGuid`, which matches the two CALL SITES and the .NET
//     convention for a three-letter acronym. A reader who searches this repository for `GenGUID`
//     lands on this paragraph, which is the reason it is written down.
//  2. THE `guid.srf` WRAPPER IS NAMED `NewGuid`, NOT `Guid`. A C# member named `Guid` inside a
//     type that also mentions System.Guid reads as a type reference at every call site and would
//     need qualifying forever after. `NewGuid` says what the member does and cannot be misread.
//     Its legacy object is ws_objects/pfw.crypto.pbl.src/guid.srf. The `randomstring.srf` wrapper
//     carries no such collision, so it keeps its legacy name directly as `RandomString`.
//
//  ==============================================================================================
//  THIS FILE CARRIES THE FOLDER'S DETERMINISM SEAM
//  ==============================================================================================
//  A golden-master characterization suite compares a recording of the legacy behaviour against a
//  recording of this port for the same workflow. That comparison is only meaningful if it is
//  repeatable, and repeatability requires that every non-deterministic value be masked from BOTH
//  the master and the candidate. Masking requires SUBSTITUTABILITY: a value that is drawn from a
//  static, process-wide source cannot be substituted by a test, so it cannot be masked, so any
//  workflow that touches it can never be compared at all.
//
//  GUID generation, random-string generation and random-blob generation are the three sources of
//  non-determinism in this service's cryptographic surface. They are all here, in this one file,
//  which is why this file - and not a sibling - declares the seam.
//
//  THE SEAM, AND THE FOUR RULES THAT KEEP IT INTACT
//      * `IEntropySource` below is the whole abstraction: fill this span with random bytes, and
//        nothing else. It has one method deliberately. A wider interface would invite an
//        implementation to make a decision that belongs in RandomProvider, and the seam would stop
//        being a seam and start being a strategy.
//      * `RandomProvider` receives it THROUGH ITS CONSTRUCTOR. There is no static field, no
//        singleton accessor, no service locator, and no optional parameter that quietly constructs
//        a real generator when the caller omits one. An optional fallback is the specific
//        anti-pattern that makes a seam useless, because the untested path is then the default.
//      * NO STATIC RANDOMNESS API IS CALLED FROM ANY BUSINESS LOGIC in this file. Not
//        `RandomNumberGenerator.GetBytes`, not `RandomNumberGenerator.GetItems`, not
//        `Random.Shared`, and specifically NOT `System.Guid.NewGuid()`. The single call to a static
//        fill method in this file is inside `CryptographicEntropySource`, which exists to BE the
//        production end of the seam - that is the one place where the boundary is crossed on
//        purpose.
//      * PRODUCTION ENTROPY IS NOT DEGRADED. The seam exists for testability, and testability may
//        not cost security. `CryptographicEntropySource` is backed by
//        `System.Security.Cryptography.RandomNumberGenerator`, a cryptographically secure
//        generator. `System.Random` is never used here for anything, not even for a length.
//
//  WHY `Guid.NewGuid()` IS REJECTED, WHICH IS THE ONE PLACE THE OBVIOUS BCL IDIOM LOSES
//  --------------------------------------------------------------------------------------------
//  `Guid.NewGuid()` is the idiomatic way to make a GUID in .NET and it would be the right call in
//  almost any other file. It is rejected here because it is a STATIC method with no injectable
//  source: a test cannot make it return a chosen value, so a workflow whose output contains a
//  generated GUID could never be masked, and could therefore never be compared against the
//  legacy recording. `GenGuid` below draws sixteen bytes from the injected `IEntropySource` and
//  composes the GUID from them instead. That is strictly more code for exactly one benefit, and
//  the benefit is the one the parity model cannot do without.
//
//  ==============================================================================================
//  TYPE WIDTHS - THE MAPPING THAT IS EASY TO GET WRONG
//  ==============================================================================================
//  PowerBuilder and C# use the same two words for different widths, so a literal reading of the
//  legacy signatures produces the wrong C# types. The mapping this port uses throughout, and which
//  shared/PowerFramework.Shared.Kernel/Bits.cs states at its own L42-L43:
//
//      PowerBuilder ulong / unsignedlong    32-bit unsigned   ->   C# uint     NOT C# ulong
//      PowerBuilder uint  / unsignedinteger 16-bit unsigned   ->   C# ushort   NOT C# uint
//
//  So `readonly ulong size` and `readonly ulong flags` at n_crypto.sru:L14-L18 are C# `uint`.
//  Note also that the two legacy files spell the same type two different ways -
//  randomstring.srf:L7-L8 writes `unsignedlong` while guid.srf:L8 writes `ulong` - which is a
//  cosmetic inconsistency in the legacy, not a width difference: both are the 32-bit unsigned type
//  and both map to `uint`. The flag constants they pass are declared `Constant Ulong`
//  [enums.sru:L954-L962] and are ported as `uint` in Enums accordingly, so every composition below
//  is `uint + uint`, which yields `uint` and needs no cast.
//
//  The legacy `readonly` parameter modifier maps to C# `in` in general, but `in` on a 32-bit scalar
//  costs an indirection and buys nothing, so the five scalar parameters here are plain by-value
//  parameters. The same choice is made in Bits.cs for the same reason.
//
//  ==============================================================================================
//  THE THIRTY LEGACY CONSTANTS ARE REFERENCED, NEVER REDECLARED
//  ==============================================================================================
//  ws_objects/pfw.shared.pbl.src/enums.sru carries the CRYPTO_* constants inside the block
//  delimited by `/*--- Crypto ---*/` at L921 and `/*--- End Crypto ---*/` at L969. A second, stray
//  `/*--- End Crypto ---*/` marker appears far below at L999 and is a trap; nothing after L969
//  belongs to this block. All of them are already ported into `Enums` in the shared kernel with
//  their SCREAMING_SNAKE spellings preserved verbatim, and the two default COMBINATIONS are
//  annotated once in `LegacyDefaults`.
//
//  This file references them and redeclares none of them. That is not a style preference: the root
//  .editorconfig scopes its CA1707 and IDE1006 suppressions to the individual files that genuinely
//  declare a preserved identifier, and inside PowerFramework.Security the only such file is
//  Crypto/LegacyDefaults.cs. RandomProvider.cs is deliberately NOT scoped, so under
//  TreatWarningsAsErrors a SCREAMING_SNAKE DECLARATION here would be a BUILD ERROR rather than a
//  style note. Every identifier declared below is PascalCase, including the character-class
//  alphabets, and every occurrence below that NAMES one of those constants is an `Enums.`-qualified
//  reference - the only unqualified appearances of the prefix anywhere in this file are the two
//  places, here and above, that describe the family as a whole rather than naming a member of it.
//  References are safe regardless: CA1707 reports declarations only, never uses, which the
//  .editorconfig header states explicitly.
//
//  The two default COMBINATIONS are reached through `LegacyDefaults` instead, whose members are
//  themselves `const` values taken from the corresponding `Enums` member. Those identifiers are
//  SCREAMING_SNAKE too, and referencing them is equally safe for the same reason; what matters is
//  that this file DECLARES no such identifier, and it declares none.
//
//  ==============================================================================================
//  WHAT THIS FILE DELIBERATELY DOES NOT DO
//  ==============================================================================================
//  * IT HOLDS NO KEY MATERIAL, and it never will. There is no key, no initialization vector, no
//    passphrase, no certificate, no PEM block and no credential here as a value, as a default
//    argument, as sample data or as an example in documentation. Nothing from any of the
//    repository's known hardcoded-secret sites is reproduced in any form; those sites are read as
//    reference only and their remediation posture is never-replicate-document-and-rotate.
//  * IT LOGS NOTHING, and takes no logger. This is a hard rule rather than an omission: the values
//    this file returns become passwords, tokens, initialization vectors and key bytes at the call
//    site, so a generated value written to a log at any level - including trace - is a leaked
//    secret that outlives the request in a log store. Not accepting a logger makes the leak
//    unwritable rather than merely forbidden.
//  * IT READS NO CONFIGURATION. There is no IOptions, no IConfiguration and no environment lookup.
//    The alphabets are algorithm data derived from character ranges, not settings.
//  * IT REGISTERS NO ROUTE. These operations sit behind the composition root's default-deny
//    authorization policy; nothing here opens a path.
//  * IT CONTAINS NOTHING FOR A DEFERRED SERVICE. No design-system, document, integration or
//    scripting concern appears here in any form.
//
//  ==============================================================================================
//  THE FOUR SUBSTITUTION DECISIONS, AND THE HONEST LIMIT ON ALL FOUR
//  ==============================================================================================
//  Because the native binary is closed, four behaviours of these five declarations are
//  UNOBSERVABLE from this repository. Each is decided once below, applied uniformly, and recorded
//  with its reason. NONE IS CLAIMED AS VERIFIED. Byte-exact parity for any of the four is
//  assertable only against the behavioural oracle, by capturing legacy output for a workflow and
//  comparing it with target output for the same workflow. Until such a capture exists, each is a
//  reasoned choice and this file says so rather than implying more.
//
//      D-R1  the default flag values the one-argument forms use          see GenRandomString(uint)
//                                                                            and GenGuid()
//      D-R2  the three character-class alphabets, and the meaning of a
//            flag value that selects no class at all                     see AlphabetsByFlagBits
//      D-R3  GUID composition, the RFC 4122 bits, and the four
//            formatting combinations                                     see GenGuid(uint)
//      D-R4  that a random blob is RAW BYTES and is never encoded        see GenRandomBlob
// ==============================================================================================

using System.Security.Cryptography;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Crypto;

/// <summary>
/// The single seam through which every non-deterministic value in this service's cryptographic
/// surface is drawn: fill a span with random bytes.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction exists so that the random blob, random string and GUID behaviours of
/// <see cref="RandomProvider"/> are SUBSTITUTABLE. A golden-master characterization comparison can
/// only be made for a workflow whose non-deterministic values can be masked on both sides, and a
/// value drawn from a static, process-wide generator cannot be masked at all. Injecting the source
/// is what makes those workflows comparable.
/// </para>
/// <para>
/// The interface is deliberately as narrow as it can be. One method, one job, no formatting
/// decision, no length policy and no algorithm choice: all of those belong to
/// <see cref="RandomProvider"/>, and moving any of them behind this interface would turn a test
/// seam into a strategy that a substituted implementation could get wrong.
/// </para>
/// <para>
/// It is declared in this file rather than in a folder of its own because this folder admits
/// exactly seven files - the six providers and the defaults catalogue - and the seam belongs beside
/// the only provider that draws from it.
/// </para>
/// <para>
/// An implementation MUST be safe to call concurrently from any number of requests, because the
/// providers that consume it are registered once and shared. An implementation intended for
/// production MUST be cryptographically secure; see <see cref="CryptographicEntropySource"/>.
/// </para>
/// </remarks>
public interface IEntropySource
{
    /// <summary>
    /// Fills <paramref name="destination"/> completely with random bytes.
    /// </summary>
    /// <param name="destination">
    /// The buffer to fill. Every byte of it is overwritten. An empty span is a valid request and
    /// completes without doing anything, which is what lets a caller pass a zero-length buffer
    /// without special-casing it.
    /// </param>
    /// <remarks>
    /// Implementations must fill the whole span or throw; a partial fill that returns normally
    /// would silently leave caller-visible bytes at whatever value they already held, which for a
    /// freshly allocated buffer is zero and is indistinguishable from a legitimate draw.
    /// </remarks>
    void Fill(Span<byte> destination);
}

/// <summary>
/// The production <see cref="IEntropySource"/>: a cryptographically secure generator backed by
/// <see cref="RandomNumberGenerator"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is the production END of the determinism seam, and it is the ONLY place in this file
/// where a static randomness API is called. That single crossing is the entire point of the type:
/// isolating it here is what leaves the rest of the file free of unseammable calls.
/// </para>
/// <para>
/// <see cref="RandomNumberGenerator.Fill(Span{byte})"/> draws from the operating system's
/// cryptographically secure generator and is documented as thread-safe, so this type needs no
/// synchronization and holds no state. It is safe to register as a singleton and to call
/// concurrently from any number of requests.
/// </para>
/// <para>
/// <see cref="System.Random"/> is deliberately not used, here or anywhere else in this file. It is
/// a deterministic pseudo-random generator whose output is predictable from a small amount of
/// observed output, which would silently reduce every key, password, initialization vector and
/// token derived from this surface to a guessable value.
/// </para>
/// </remarks>
public sealed class CryptographicEntropySource : IEntropySource
{
    /// <summary>
    /// Fills <paramref name="destination"/> with cryptographically strong random bytes.
    /// </summary>
    /// <param name="destination">The buffer to fill. An empty span completes without effect.</param>
    public void Fill(Span<byte> destination)
    {
        // The one deliberate static-randomness call in this file. Everything else draws through
        // IEntropySource so that it can be substituted; this method is what it is substituted FOR.
        RandomNumberGenerator.Fill(destination);
    }
}

/// <summary>
/// The random blob, random string and GUID surface of the legacy cryptographic class: five
/// declarations at ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14-L18 plus the two global-function
/// wrappers at randomstring.srf:L11 and guid.srf:L11.
/// </summary>
/// <remarks>
/// <para>
/// The legacy class is native to a closed binary [n_crypto.sru:L8], so no legacy body exists for
/// any of the five. Every member here is a documented substitution against
/// System.Security.Cryptography and the Base Class Library, and the four behaviours the closed
/// binary hides are decided in this file's header as D-R1 through D-R4. None of the four is claimed
/// as verified; each is assertable only against the behavioural oracle.
/// </para>
/// <para>
/// EVERY RANDOM BYTE COMES FROM THE INJECTED <see cref="IEntropySource"/>. This type calls no
/// static randomness API - not <see cref="RandomNumberGenerator"/> directly, not
/// <see cref="System.Random"/>, and not <see cref="Guid.NewGuid"/>. That is what makes the values it
/// produces maskable in a golden-master comparison, and it is the reason the constructor has no
/// parameterless overload and no defaulted argument: an optional fallback would make the
/// unsubstituted path the default one.
/// </para>
/// <para>
/// The type is immutable after construction and holds no mutable state, so a single instance is
/// safe to share across any number of concurrent requests provided the injected source is itself
/// thread-safe, which <see cref="IEntropySource"/> requires. It accepts no logger and never emits
/// a generated value anywhere: the strings and blobs it returns become passwords, tokens,
/// initialization vectors and key bytes at the call site, so writing one to a log at any level
/// would be a leaked secret. It also reads no configuration.
/// </para>
/// <para>
/// TWO LEGACY IDIOMS HAVE NO ANALOGUE HERE, DELIBERATELY. The wrappers guard their use of the
/// native class with `if Not IsValid(n_crypto) then n_crypto = Create n_crypto`
/// [randomstring.srf:L14, guid.srf:L14], lazily constructing the global auto-instance declared at
/// n_crypto.sru:L75 - a global whose name shadows its own type name. Constructor injection replaces
/// both: the instance's lifetime is owned by the composition root, so there is nothing to check for
/// validity and nothing to construct on demand, and the shadowing global has no counterpart because
/// there is no global. One observable of that idiom DOES survive, and it survives for free: the
/// one-argument <c>randomstring</c> and no-argument <c>guid</c> forms carry no lazy-initialization
/// guard of their own because they delegate to their sibling overload, which holds it
/// [randomstring.srf:L11 delegating to :L14; guid.srf:L11 delegating to :L14]. That is exactly the
/// shape one C# overload calling another already has.
/// </para>
/// </remarks>
public sealed class RandomProvider
{
    // ==========================================================================================
    //  DECISION D-R2 - THE THREE CHARACTER-CLASS ALPHABETS
    //  ORACLE  ws_objects/pfw.shared.pbl.src/enums.sru:L953-L957 declares the three flags;
    //          n_crypto.sru:L16 consumes them. The alphabets themselves are inside the closed
    //          binary and are therefore UNOBSERVABLE from this repository.
    //  ------------------------------------------------------------------------------------------
    //  The flags are exactly three, additive, and named for character CLASSES rather than for
    //  particular characters:
    //
    //      Enums.CRYPTO_RNDSTRING_NUMBER    = 1   [enums.sru:L954]
    //      Enums.CRYPTO_RNDSTRING_ALPHABET  = 2   [enums.sru:L955]
    //      Enums.CRYPTO_RNDSTRING_SYMBOL    = 4   [enums.sru:L956]
    //
    //  THE SETS ADOPTED, AND WHY EACH
    //
    //      NUMBER      the ten decimal digits, '0' through '9'. Unambiguous.
    //
    //      ALPHABET    BOTH CASES, 'A'-'Z' followed by 'a'-'z', 52 characters. The flag is a single
    //                  bit named for the alphabet as a whole; there is no case sub-flag anywhere in
    //                  the constant block, so a reading that admitted only one case would have to
    //                  invent a reason to prefer upper over lower. Taking the letters to mean the
    //                  letters needs no such invention, and it is also the choice that maximises
    //                  entropy per character, which is the purpose a random-string generator
    //                  serves. THE ALTERNATIVES ARE UPPER-ONLY AND LOWER-ONLY, and the oracle would
    //                  settle which the closed binary uses. This is a decision, not a measurement.
    //
    //      SYMBOL      EVERY PRINTABLE ASCII CHARACTER THAT IS NEITHER A LETTER NOR A DIGIT: 32
    //                  characters across four contiguous ranges, '!'-'/', ':'-'@', '['-'`' and
    //                  '{'-'~'. This is the complete, self-describing complement of the other two
    //                  classes inside printable ASCII, so it requires no arbitrary inclusion or
    //                  exclusion and can be stated as a rule rather than as a list. THE ALTERNATIVE
    //                  IS A SHORTER "SAFE" SUBSET that omits quotes, backslashes or shell
    //                  metacharacters; such a subset would be a judgement about downstream
    //                  consumers that nothing in the repository authorises. The oracle would settle
    //                  it.
    //
    //  THE SETS ARE DERIVED FROM THEIR RANGES, NOT TYPED OUT. `BuildAsciiRange` below expands the
    //  ranges, so the definition of each class is the rule itself rather than a transcription of
    //  it. That is deliberate twice over: a mistyped character cannot silently narrow or widen a
    //  class, and no long alphanumeric literal appears in this file that a secret scan could
    //  reasonably mistake for encoded material. These alphabets are algorithm data, not secrets.
    //
    //  A FLAG VALUE THAT SELECTS NO CLASS RETURNS AN EMPTY STRING, AND NEVER THROWS. The flags are
    //  an additive bitmask, and how the closed binary treats a zero or unrecognised value is
    //  unobservable, so Crypto/LegacyDefaults.cs rules that no validation is imposed on a
    //  caller-supplied flag value - rejecting one would narrow the legacy contract on a guess. This
    //  file honours that ruling: bits outside the three known ones are IGNORED, and if the
    //  surviving selection is empty then there is no character to draw and the result is
    //  string.Empty. The alternative - silently substituting the default alphabet - would invent a
    //  fallback the legacy may not have, which is the worse of the two guesses because it produces
    //  a plausible value rather than an obviously empty one. The oracle would settle it.
    // ==========================================================================================

    /// <summary>
    /// The union of the three defined random-string flag bits, used to ignore any other bit a
    /// caller may pass, per the no-validation ruling in <see cref="LegacyDefaults"/>.
    /// </summary>
    private const uint SelectableRandomStringFlags =
        Enums.CRYPTO_RNDSTRING_NUMBER | Enums.CRYPTO_RNDSTRING_ALPHABET | Enums.CRYPTO_RNDSTRING_SYMBOL;

    /// <summary>
    /// The number of distinct flag combinations, which is every value of the three defined bits
    /// inclusive of none of them: eight rows, indexed 0 through 7.
    /// </summary>
    private const uint AlphabetCombinationCount = SelectableRandomStringFlags + 1;

    /// <summary>
    /// The number of distinct values a single byte can take, used to compute the unbiased
    /// acceptance window for rejection sampling.
    /// </summary>
    private const int ByteValueCount = 256;

    /// <summary>
    /// The length of a GUID in octets [RFC 4122 section 4.1.2].
    /// </summary>
    private const int GuidOctetCount = 16;

    /// <summary>
    /// The octet carrying the four-bit version field in the RFC 4122 layout, whose high nibble is
    /// the thirteenth hexadecimal digit of the canonical text form.
    /// </summary>
    private const int GuidVersionOctetIndex = 6;

    /// <summary>
    /// The octet carrying the two-bit variant field in the RFC 4122 layout, whose high bits open the
    /// seventeenth hexadecimal digit of the canonical text form.
    /// </summary>
    private const int GuidVariantOctetIndex = 8;

    /// <summary>
    /// Clears the four version bits, preserving the four random bits beside them.
    /// </summary>
    private const int GuidVersionMask = 0x0F;

    /// <summary>
    /// Marks the version as 4, meaning "generated from random or pseudo-random numbers"
    /// [RFC 4122 section 4.4].
    /// </summary>
    private const int GuidVersion4Marker = 0x40;

    /// <summary>
    /// Clears the two variant bits, preserving the six random bits beside them.
    /// </summary>
    private const int GuidVariantMask = 0x3F;

    /// <summary>
    /// Marks the variant as the RFC 4122 one, which is the binary pattern 10 in the two high bits
    /// [RFC 4122 section 4.1.1].
    /// </summary>
    private const int GuidVariantRfc4122Marker = 0x80;

    /// <summary>
    /// The standard format specifier for the bracketed, hyphenated GUID form,
    /// <c>{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}</c>.
    /// </summary>
    private const string GuidBracketedSeparatedFormat = "B";

    /// <summary>
    /// The standard format specifier for the hyphenated, unbracketed GUID form,
    /// <c>xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx</c>.
    /// </summary>
    private const string GuidSeparatedFormat = "D";

    /// <summary>
    /// The standard format specifier for the bare thirty-two-digit GUID form, with neither brackets
    /// nor hyphens.
    /// </summary>
    private const string GuidBareFormat = "N";

    /// <summary>
    /// The opening brace of the bracketed forms, needed as a literal because the bracketed form
    /// WITHOUT hyphens has no standard format specifier; see DECISION D-R3.
    /// </summary>
    private const string GuidOpeningBracket = "{";

    /// <summary>
    /// The closing brace of the bracketed forms; see <see cref="GuidOpeningBracket"/>.
    /// </summary>
    private const string GuidClosingBracket = "}";

    /// <summary>
    /// The largest length this type will allocate for, which is the largest array a .NET index can
    /// address.
    /// </summary>
    /// <remarks>
    /// The legacy parameter is a 32-bit unsigned value and so admits lengths above
    /// <see cref="int.MaxValue"/>. Such a request cannot be satisfied on this platform at all; the
    /// guard converts what would otherwise surface as an <see cref="OutOfMemoryException"/> from
    /// deep inside an allocation into a named argument error at the boundary. That is a defined
    /// error in place of an undefined failure, not a policy limit: no minimum is imposed and no
    /// satisfiable length is rejected.
    /// </remarks>
    private const uint MaximumRequestedLength = int.MaxValue;

    /// <summary>
    /// The number of times a draw may be resampled before the injected entropy source is declared
    /// degenerate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rejection sampling is what keeps the character draw unbiased, and it is unbounded in
    /// principle: a source that returned only values outside the acceptance window would loop
    /// forever. The narrowest acceptance window any of the seven non-empty alphabets produces is
    /// 188 of 256 values, so a genuine generator clears each round with probability of at least
    /// roughly 0.73 and the chance of exhausting this many rounds is far beyond negligible.
    /// </para>
    /// <para>
    /// A bounded loop with a defined error is nevertheless the correct shape, because the source is
    /// injected: a substituted implementation that returns a constant byte is entirely possible,
    /// and a service that hangs is worse in every way than one that reports the fault. What the
    /// closed binary does with a degenerate generator is unobservable, so this is an implementation
    /// detail rather than a behavioural change.
    /// </para>
    /// </remarks>
    private const int MaximumResamplingRounds = 256;

    /// <summary>
    /// The eight character-class alphabets, indexed by the three defined flag bits. Index 0 - no
    /// class selected - is deliberately the empty string; see DECISION D-R2 above.
    /// </summary>
    /// <remarks>
    /// Computed once at type initialization and never mutated afterwards. The elements are strings,
    /// which are immutable, so concurrent readers cannot observe a torn or altered alphabet. This
    /// is why the table is precomputed rather than composed per call: it removes both an allocation
    /// and a mutable-shared-array hazard from the request path.
    /// </remarks>
    private static readonly string[] AlphabetsByFlagBits = BuildAlphabetTable();

    /// <summary>
    /// The injected seam. Every random byte this type produces is drawn through it.
    /// </summary>
    private readonly IEntropySource _entropy;

    /// <summary>
    /// Creates a provider that draws every random byte from <paramref name="entropy"/>.
    /// </summary>
    /// <param name="entropy">
    /// The entropy seam. In production this is <see cref="CryptographicEntropySource"/>; a test
    /// substitutes a deterministic implementation so that generated values can be masked in a
    /// golden-master comparison.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entropy"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// There is deliberately no parameterless constructor and no defaulted parameter that would
    /// construct a real generator when the argument is omitted. Either would make the unsubstituted
    /// path the default one, which is precisely how a test seam stops being usable.
    /// </remarks>
    public RandomProvider(IEntropySource entropy)
    {
        ArgumentNullException.ThrowIfNull(entropy);

        _entropy = entropy;
    }

    /// <summary>
    /// Generates <paramref name="size"/> random bytes.
    /// </summary>
    /// <param name="size">
    /// The number of bytes to generate. Zero is valid and yields an empty array; see the remarks.
    /// </param>
    /// <returns>A new array of exactly <paramref name="size"/> random bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the largest length this platform can allocate as an array.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> failed to fill the buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L14,
    /// <c>public function blob GenRandomBlob(readonly ulong size)</c>. The legacy return type is
    /// <c>blob</c>, which is raw bytes, and the parameter is the 32-bit unsigned PowerBuilder
    /// <c>ulong</c>, hence C# <see cref="uint"/>.
    /// </para>
    /// <para>
    /// DECISION D-R4 - THE RESULT IS RAW BYTES AND IS NEVER ENCODED. The blob is returned exactly as
    /// drawn, with no Base64 and no hexadecimal applied, because the caller owns that choice: the
    /// oracle's own demo wraps this call in the caller-driven encoder,
    /// <c>BlobToString(GenRandomBlob(10), Enums.CRYPTO_ENCODING_HEX)</c>
    /// [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L652]. Pre-encoding here would
    /// double-encode that call site, so this member takes no dependency on the encoding provider at
    /// all. This is the one of the four decisions that the repository DOES evidence directly.
    /// </para>
    /// <para>
    /// A ZERO LENGTH RETURNS AN EMPTY ARRAY RATHER THAN THROWING. The legacy declares no minimum,
    /// and a request for no bytes is well defined; rejecting it would narrow the legacy contract on
    /// a guess, which is the same reasoning <see cref="LegacyDefaults"/> applies to flag values. The
    /// empty array is not a shared instance being handed out twice, because an empty array has no
    /// contents to observe or mutate.
    /// </para>
    /// <para>
    /// The returned array IS the generated material, so it is deliberately not zeroed here - the
    /// caller owns its lifetime and is the only party that can know when it is spent.
    /// </para>
    /// </remarks>
    public byte[] GenRandomBlob(uint size)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaximumRequestedLength);

        if (size == 0)
        {
            return [];
        }

        byte[] blob = new byte[size];
        _entropy.Fill(blob);

        return blob;
    }

    /// <summary>
    /// Generates a random string of <paramref name="size"/> characters using the preserved default
    /// character classes, which are the digits and the letters with symbols excluded.
    /// </summary>
    /// <param name="size">
    /// The number of characters to generate. Zero is valid and yields an empty string.
    /// </param>
    /// <returns>A string of exactly <paramref name="size"/> characters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the largest length this platform can allocate as an array.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> is degenerate; see
    /// <see cref="GenRandomString(uint, uint)"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L15,
    /// <c>public function string GenRandomString(readonly ulong size)</c>.
    /// </para>
    /// <para>
    /// DECISION D-R1 - THE DEFAULT THE CLOSED BINARY APPLIES IS INFERRED, NOT READ. This overload
    /// takes no flags, so the native implementation chooses them inside the DLL where they cannot be
    /// observed. The value adopted is <see cref="LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT"/>, which
    /// is defined as <see cref="Enums.CRYPTO_RNDSTRING_DEFAULT"/> and therefore resolves to
    /// <see cref="Enums.CRYPTO_RNDSTRING_NUMBER"/> plus
    /// <see cref="Enums.CRYPTO_RNDSTRING_ALPHABET"/> [enums.sru:L957], with
    /// <see cref="Enums.CRYPTO_RNDSTRING_SYMBOL"/> excluded.
    /// </para>
    /// <para>
    /// The inference rests on two independent pieces of in-repository evidence, which is what makes
    /// it an inference rather than a guess. First, enums.sru itself NAMES that combination
    /// <c>DEFAULT</c> [L957] - the legacy declares what its own default is. Second, the global
    /// wrapper in front of this surface composes exactly the same combination when it too has no
    /// flags to pass [randomstring.srf:L11]. Both point at the same value. It remains unverified in
    /// the strict sense, because only the oracle can show what the DLL actually does.
    /// </para>
    /// <para>
    /// The flag value is taken from <see cref="LegacyDefaults"/> rather than from
    /// <see cref="Enums"/> directly so that the annotation of this weak-by-modern-standards default
    /// - a narrowed alphabet reduces entropy per character - lives in exactly one place for all six
    /// providers of this folder. The two are the same value by construction.
    /// </para>
    /// <para>
    /// This overload carries no guard of its own before delegating, which mirrors the legacy: the
    /// one-argument wrapper at randomstring.srf:L11 likewise holds no lazy-initialization guard,
    /// because the sibling it delegates to holds it [randomstring.srf:L14].
    /// </para>
    /// </remarks>
    public string GenRandomString(uint size)
    {
        return GenRandomString(size, LegacyDefaults.RANDOM_STRING_FLAGS_DEFAULT);
    }

    /// <summary>
    /// Generates a random string of <paramref name="size"/> characters drawn from the character
    /// classes selected by <paramref name="flags"/>.
    /// </summary>
    /// <param name="size">
    /// The number of characters to generate. Zero is valid and yields an empty string.
    /// </param>
    /// <param name="flags">
    /// Any additive combination of <see cref="Enums.CRYPTO_RNDSTRING_NUMBER"/>,
    /// <see cref="Enums.CRYPTO_RNDSTRING_ALPHABET"/> and
    /// <see cref="Enums.CRYPTO_RNDSTRING_SYMBOL"/>. Bits outside those three are ignored, and a
    /// value that selects no class at all yields an empty string; see the remarks.
    /// </param>
    /// <returns>
    /// A string of exactly <paramref name="size"/> characters, or an empty string when
    /// <paramref name="size"/> is zero or <paramref name="flags"/> selects no character class.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the largest length this platform can allocate as an array.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> returned no value inside the unbiased acceptance
    /// window across the bounded number of resampling rounds this type permits, which indicates a
    /// degenerate or misconfigured implementation rather than an unlucky draw.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L16,
    /// <c>public function string GenRandomString(readonly ulong size,readonly ulong flags)</c>. Both
    /// parameters are the 32-bit unsigned PowerBuilder <c>ulong</c>, hence C# <see cref="uint"/>. The
    /// oracle's demo exercises the all-three-classes combination directly with
    /// <c>GenRandomString(50, NUMBER + ALPHABET + SYMBOL)</c>
    /// [ws_objects/pfw.demos.pbl.src/u_cst_tabpage_utility_crypto.sru:L667].
    /// </para>
    /// <para>
    /// THE ALPHABETS AND THE EMPTY-SELECTION ARM ARE DECISION D-R2, recorded in full above
    /// <see cref="AlphabetsByFlagBits"/>: the digits, both cases of the letters, and every printable
    /// ASCII character that is neither, with unrecognised bits ignored and an empty selection
    /// producing an empty string rather than an exception or a silent fallback to the default
    /// alphabet.
    /// </para>
    /// <para>
    /// THE DRAW IS UNBIASED, BY REJECTION SAMPLING. Taking a random byte modulo the alphabet length
    /// is the obvious approach and it is subtly wrong whenever the length does not divide 256: the
    /// first <c>256 mod n</c> characters of the alphabet would each be drawn once more often than the
    /// rest, so the distribution would be skewed towards the digits, which sort first. The
    /// acceptance window here is the largest multiple of the alphabet length that fits in a byte, and
    /// a byte at or above it is discarded and redrawn, which makes every character exactly equally
    /// likely. A single byte always suffices because the widest alphabet is 94 characters, far below
    /// 256.
    /// </para>
    /// <para>
    /// The bias this removes is UNOBSERVABLE from the repository - what the closed binary does is
    /// unknown - so this is an implementation detail chosen for correctness, not a behavioural
    /// change. It is recorded here because "we chose the unbiased method" is exactly the kind of
    /// decision that looks like an accident when it is not written down.
    /// </para>
    /// <para>
    /// Bytes are drawn in blocks sized to the characters still needed rather than one at a time,
    /// which keeps the number of calls into the seam proportional to the result rather than to the
    /// number of draws, and remains fully deterministic for a deterministic source: the same byte
    /// sequence always yields the same string.
    /// </para>
    /// </remarks>
    public string GenRandomString(uint size, uint flags)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaximumRequestedLength);

        // Unrecognised bits are masked away rather than rejected, per the no-validation ruling in
        // LegacyDefaults. Row 0 of the table is the empty alphabet, so a flag value of zero - or one
        // carrying only undefined bits - lands there rather than on a fallback.
        string alphabet = AlphabetsByFlagBits[flags & SelectableRandomStringFlags];

        // Two independent empty arms, both DECISION D-R2. A zero length is a well-defined request
        // for nothing, and an empty alphabet leaves nothing to draw. Neither is an error.
        if (size == 0 || alphabet.Length == 0)
        {
            return string.Empty;
        }

        // The largest multiple of the alphabet length representable in a byte. A drawn byte at or
        // above this value is discarded; below it, the modulo is uniform. When the length divides
        // 256 exactly this equals 256, which no byte can reach, so nothing is ever discarded.
        int acceptanceLimit = ByteValueCount - (ByteValueCount % alphabet.Length);

        char[] drawn = new char[size];
        byte[] block = new byte[drawn.Length];
        try
        {
            int produced = 0;
            int rounds = 0;
            while (produced < drawn.Length)
            {
                rounds++;
                if (rounds > MaximumResamplingRounds)
                {
                    // A defined error rather than an unbounded loop. See MaximumResamplingRounds for
                    // why a genuine source cannot reach this, and why an injected one can. The
                    // message deliberately reveals nothing about any drawn value.
                    throw new InvalidOperationException(
                        "The injected entropy source produced no value inside the unbiased " +
                        "acceptance window across the permitted number of resampling rounds. A " +
                        "cryptographically secure source clears that window for roughly three " +
                        "bytes in four, so this indicates a degenerate or misconfigured " +
                        "IEntropySource implementation rather than an unlucky draw.");
                }

                // Ask for exactly what is still missing. On the first round that is the whole
                // result; on any later round it is only the characters that were discarded.
                Span<byte> window = block.AsSpan(0, drawn.Length - produced);
                _entropy.Fill(window);

                for (int index = 0; index < window.Length; index++)
                {
                    int value = window[index];
                    if (value >= acceptanceLimit)
                    {
                        continue;
                    }

                    drawn[produced] = alphabet[value % alphabet.Length];
                    produced++;
                }
            }

            // new string(char[]) copies, so the buffers cleared below cannot affect the result.
            return new string(drawn);
        }
        finally
        {
            // Hygiene, not a behavioural requirement: the transient buffers held material that is
            // about to become a password, a token or key bytes at the call site, so they are wiped
            // rather than left for whatever reads that memory next.
            CryptographicOperations.ZeroMemory(block);
            Array.Clear(drawn);
        }
    }

    /// <summary>
    /// Generates a GUID in the preserved default text form, which is bracketed and hyphenated.
    /// </summary>
    /// <returns>A GUID formatted as <c>{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> failed to fill the buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L17,
    /// <c>public function string GenGUID()</c>. THE LEGACY DECLARATION IS SPELLED
    /// <c>GenGUID</c> there, while both call sites spell it <c>GenGuid</c>
    /// [guid.srf:L15 and u_cst_tabpage_utility_crypto.sru:L682]. PowerScript is case-insensitive, so
    /// the two spellings name one function; the C# member follows the call sites and the .NET
    /// convention for a three-letter acronym. A search for <c>GenGUID</c> is meant to arrive here.
    /// </para>
    /// <para>
    /// DECISION D-R1 - THE DEFAULT IS INFERRED FROM THE SAME TWO PIECES OF EVIDENCE as the
    /// random-string default. The value adopted is <see cref="LegacyDefaults.GUID_FLAGS_DEFAULT"/>,
    /// defined as <see cref="Enums.CRYPTO_GUID_DEFAULT"/> and therefore
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_BRACKET"/> plus
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_SEPARATOR"/> [enums.sru:L962]: enums.sru names that
    /// combination <c>DEFAULT</c>, and the global wrapper composes the identical combination when it
    /// has no flags to pass [guid.srf:L11]. Unverified in the strict sense; only the oracle can show
    /// what the DLL does.
    /// </para>
    /// <para>
    /// As with the random-string default, no guard precedes the delegation, mirroring guid.srf:L11
    /// whose lazy-initialization guard lives in the sibling it delegates to [guid.srf:L14].
    /// </para>
    /// </remarks>
    public string GenGuid()
    {
        return GenGuid(LegacyDefaults.GUID_FLAGS_DEFAULT);
    }

    /// <summary>
    /// Generates a GUID and formats it according to <paramref name="flags"/>.
    /// </summary>
    /// <param name="flags">
    /// Any additive combination of <see cref="Enums.CRYPTO_GUID_INCLUDE_BRACKET"/> and
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_SEPARATOR"/>. Bits outside those two are ignored, and a
    /// value selecting neither yields the bare thirty-two-digit form.
    /// </param>
    /// <returns>
    /// The GUID as text in one of four shapes: bracketed and hyphenated, hyphenated only, bracketed
    /// only, or bare.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> failed to fill the buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/n_crypto.sru:L18,
    /// <c>public function string GenGUID(readonly ulong flags)</c>; see
    /// <see cref="GenGuid()"/> for the spelling note. The parameter is the 32-bit unsigned
    /// PowerBuilder <c>ulong</c>, hence C# <see cref="uint"/>, and guid.srf:L8 spells the same type
    /// <c>ulong</c> where randomstring.srf spells it <c>unsignedlong</c> - one type, two spellings.
    /// </para>
    /// <para>
    /// DECISION D-R3, PART ONE - THE SIXTEEN OCTETS COME FROM THE SEAM, NOT FROM
    /// <see cref="Guid.NewGuid"/>. <see cref="Guid.NewGuid"/> is the idiomatic .NET call and is
    /// rejected here for one reason: it is static, so a test cannot make it return a chosen value,
    /// so any workflow whose output contains a generated GUID could never be masked and therefore
    /// never compared against the legacy recording. Drawing the octets through
    /// <see cref="IEntropySource"/> costs a few lines and buys the whole parity model.
    /// </para>
    /// <para>
    /// DECISION D-R3, PART TWO - THE RFC 4122 VERSION AND VARIANT BITS ARE SET. Six of the 128 bits
    /// are overwritten: the four-bit version field becomes 4, meaning "generated from random
    /// numbers" [RFC 4122 section 4.4], and the two-bit variant field becomes the RFC 4122 pattern
    /// [section 4.1.1]. The remaining 122 bits are random. The reason is that the legacy almost
    /// certainly delegated to the platform GUID API, which produces version-4 variant-1 values, so a
    /// port that left those bits random would emit text that is distinguishable from every GUID the
    /// legacy ever produced - the thirteenth digit would not be 4 and the seventeenth would range
    /// over all sixteen values instead of four. That is a visible difference in stored data, so
    /// setting the bits is the behaviour-preserving choice. The octets are then read as big-endian so
    /// that octet six and octet eight line up with the thirteenth and seventeenth digits of the text
    /// form, which is what makes the two assignments above mean what they say.
    /// </para>
    /// <para>
    /// DECISION D-R3, PART THREE - FOUR COMBINATIONS, ONE OF WHICH HAS NO STANDARD SPECIFIER. .NET
    /// covers only three of the four: <c>B</c> is bracketed AND hyphenated, <c>D</c> is hyphenated
    /// alone, and <c>N</c> is bare. THERE IS NO STANDARD SPECIFIER FOR BRACKETS WITHOUT HYPHENS,
    /// which is exactly what <see cref="Enums.CRYPTO_GUID_INCLUDE_BRACKET"/> alone asks for, so that
    /// one shape is composed by hand from the bare form. Reaching for <c>P</c> instead would emit
    /// parentheses, which is a different character and a silent defect.
    /// </para>
    /// <para>
    /// DECISION D-R3, PART FOUR - THE HEXADECIMAL DIGITS ARE LOWERCASE, which is what .NET's GUID
    /// formatting emits. The Windows GUID-to-string API renders them UPPERCASE, and the legacy ran on
    /// Windows, so case is a genuine open observable that the oracle would settle. It is called out
    /// rather than quietly assumed because it affects every stored value and every recorded
    /// comparison, while being invisible to any test that only checks the shape.
    /// </para>
    /// </remarks>
    public string GenGuid(uint flags)
    {
        // Sixteen octets is a fixed, tiny size, so the buffer is stack-allocated: it never reaches
        // the heap, which means it cannot be moved, copied by a collector, or left behind for the
        // next allocation to read.
        Span<byte> octets = stackalloc byte[GuidOctetCount];
        try
        {
            _entropy.Fill(octets);

            // DECISION D-R3, PART TWO. Clear then set, so that the six bits are forced regardless of
            // what was drawn while the surrounding random bits in the same octets are preserved.
            octets[GuidVersionOctetIndex] =
                (byte)((octets[GuidVersionOctetIndex] & GuidVersionMask) | GuidVersion4Marker);
            octets[GuidVariantOctetIndex] =
                (byte)((octets[GuidVariantOctetIndex] & GuidVariantMask) | GuidVariantRfc4122Marker);

            // bigEndian: true reads the octets in RFC 4122 order, which is what makes the two
            // indices above correspond to the version and variant digits of the text form. The
            // default little-endian reading would scatter them across other digits on every
            // platform, and the two assignments would then be quietly meaningless.
            Guid value = new Guid(octets, bigEndian: true);

            bool bracketed = (flags & Enums.CRYPTO_GUID_INCLUDE_BRACKET) != 0;
            bool separated = (flags & Enums.CRYPTO_GUID_INCLUDE_SEPARATOR) != 0;

            if (bracketed && separated)
            {
                return value.ToString(GuidBracketedSeparatedFormat);
            }

            if (separated)
            {
                return value.ToString(GuidSeparatedFormat);
            }

            if (bracketed)
            {
                // DECISION D-R3, PART THREE: the one shape .NET has no specifier for.
                return string.Concat(
                    GuidOpeningBracket, value.ToString(GuidBareFormat), GuidClosingBracket);
            }

            return value.ToString(GuidBareFormat);
        }
        finally
        {
            // Hygiene: the octets are spent once the value is constructed.
            CryptographicOperations.ZeroMemory(octets);
        }
    }

    /// <summary>
    /// The <c>randomstring</c> global function: a random string of <paramref name="size"/>
    /// characters drawn from the digits and the letters.
    /// </summary>
    /// <param name="size">
    /// The number of characters to generate. Zero is valid and yields an empty string.
    /// </param>
    /// <returns>A string of exactly <paramref name="size"/> characters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> exceeds the largest length this platform can allocate as an array.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> is degenerate; see
    /// <see cref="GenRandomString(uint, uint)"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/randomstring.srf, the one-argument global function
    /// at :L11, whose whole body composes a flag pair and forwards. Its two-argument sibling at
    /// :L14-L15 adds only the lazy-initialization guard and the native call, both of which
    /// constructor injection and <see cref="GenRandomString(uint, uint)"/> already cover - which is
    /// why this file has seven members rather than eight. The parameter is spelled
    /// <c>unsignedlong</c> there [:L7], the same 32-bit unsigned type <c>ulong</c> names elsewhere.
    /// </para>
    /// <para>
    /// THE FLAG COMPOSITION IS REPRODUCED THE WAY THE LEGACY WRITES IT, as
    /// <see cref="Enums.CRYPTO_RNDSTRING_NUMBER"/> plus
    /// <see cref="Enums.CRYPTO_RNDSTRING_ALPHABET"/>, rather than collapsed into the named default
    /// constant. The two are numerically identical - <see cref="Enums.CRYPTO_RNDSTRING_DEFAULT"/> IS
    /// that sum [enums.sru:L957] - so this is a legibility decision, not a behavioural one: the
    /// wrapper's own source spells the sum out [randomstring.srf:L11], and spelling it out here keeps
    /// visible the fact that the wrapper states its classes explicitly instead of deferring to a
    /// default it might not have meant. Both <c>uint</c> operands sum to <c>uint</c>, so no cast
    /// appears.
    /// </para>
    /// <para>
    /// Named <c>RandomString</c>, matching its legacy object name directly; unlike the GUID wrapper
    /// it collides with nothing in the Base Class Library.
    /// </para>
    /// </remarks>
    public string RandomString(uint size)
    {
        return GenRandomString(
            size, Enums.CRYPTO_RNDSTRING_NUMBER + Enums.CRYPTO_RNDSTRING_ALPHABET);
    }

    /// <summary>
    /// The <c>guid</c> global function: a GUID in the bracketed, hyphenated text form.
    /// </summary>
    /// <returns>A GUID formatted as <c>{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}</c>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The injected <see cref="IEntropySource"/> failed to fill the buffer.
    /// </exception>
    /// <remarks>
    /// <para>
    /// SUBSTITUTES ws_objects/pfw.crypto.pbl.src/guid.srf, the no-argument global function at :L11.
    /// Its one-argument sibling at :L14-L15 adds only the lazy-initialization guard and the native
    /// call, which constructor injection and <see cref="GenGuid(uint)"/> already cover.
    /// </para>
    /// <para>
    /// NAMED <c>NewGuid</c> RATHER THAN <c>Guid</c>, DELIBERATELY. A member named <c>Guid</c> would
    /// read as a reference to <see cref="System.Guid"/> at every call site and would need qualifying
    /// wherever both appear; <c>NewGuid</c> says what it does and cannot be misread. The legacy
    /// object it stands for is guid.srf, recorded here because the names differ.
    /// </para>
    /// <para>
    /// THE FLAG COMPOSITION IS REPRODUCED THE WAY THE LEGACY WRITES IT, as
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_BRACKET"/> plus
    /// <see cref="Enums.CRYPTO_GUID_INCLUDE_SEPARATOR"/> [guid.srf:L11], rather than collapsed into
    /// <see cref="Enums.CRYPTO_GUID_DEFAULT"/>, which is the identical value [enums.sru:L962]. Same
    /// legibility reasoning as <see cref="RandomString(uint)"/>.
    /// </para>
    /// </remarks>
    public string NewGuid()
    {
        return GenGuid(
            Enums.CRYPTO_GUID_INCLUDE_BRACKET + Enums.CRYPTO_GUID_INCLUDE_SEPARATOR);
    }

    /// <summary>
    /// Expands a set of inclusive character ranges into a single string, in the order given.
    /// </summary>
    /// <param name="ranges">The inclusive ranges to expand, each a first and last character.</param>
    /// <returns>Every character of every range, concatenated in order.</returns>
    /// <remarks>
    /// The loop counter is an <see cref="int"/> rather than a <see cref="char"/> so that a range
    /// ending at the last representable character cannot wrap and spin. None of this file's ranges
    /// comes near that boundary, but a helper that is only correct for its current callers is a
    /// defect waiting for its second caller.
    /// </remarks>
    private static string BuildAsciiRange(ReadOnlySpan<(char First, char Last)> ranges)
    {
        int length = 0;
        foreach ((char first, char last) in ranges)
        {
            length += last - first + 1;
        }

        char[] characters = new char[length];
        int next = 0;
        foreach ((char first, char last) in ranges)
        {
            for (int code = first; code <= last; code++)
            {
                characters[next] = (char)code;
                next++;
            }
        }

        return new string(characters);
    }

    /// <summary>
    /// Builds the eight-row alphabet table of DECISION D-R2, one row per combination of the three
    /// defined random-string flag bits.
    /// </summary>
    /// <returns>
    /// A table indexed by the flag bits, whose row 0 is the empty string because no character class
    /// is selected by a value of zero.
    /// </returns>
    private static string[] BuildAlphabetTable()
    {
        // The three classes, each derived from its defining ranges rather than transcribed. See
        // DECISION D-R2 for why ALPHABET carries both cases and why SYMBOL is the complete
        // printable-ASCII complement of the other two classes.
        string digits = BuildAsciiRange([('0', '9')]);
        string letters = BuildAsciiRange([('A', 'Z'), ('a', 'z')]);
        string symbols = BuildAsciiRange([('!', '/'), (':', '@'), ('[', '`'), ('{', '~')]);

        string[] table = new string[AlphabetCombinationCount];
        for (uint bits = 0; bits < AlphabetCombinationCount; bits++)
        {
            // Ordering is fixed as digits, then letters, then symbols. It has no effect on the
            // distribution of a draw, because every character of the composed alphabet is equally
            // likely; it is fixed only so that the alphabet for a given flag value is stable across
            // runs and processes, which a characterization recording depends on.
            string alphabet = string.Empty;
            if ((bits & Enums.CRYPTO_RNDSTRING_NUMBER) != 0)
            {
                alphabet += digits;
            }

            if ((bits & Enums.CRYPTO_RNDSTRING_ALPHABET) != 0)
            {
                alphabet += letters;
            }

            if ((bits & Enums.CRYPTO_RNDSTRING_SYMBOL) != 0)
            {
                alphabet += symbols;
            }

            table[bits] = alphabet;
        }

        return table;
    }
}
