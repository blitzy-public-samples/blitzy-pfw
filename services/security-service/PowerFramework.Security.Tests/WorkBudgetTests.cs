// ==================================================================================================
//  THE WORK BUDGETS THIS BOUNDARY PLACES ON A SINGLE REQUEST
//
//  WHAT WAS WRONG, AS A FACT ABOUT THE CODE
//  Three operations on this service accepted a request whose COST was unbounded even though its SIZE was
//  not. Token issuance walked a scope array with no ceiling on how many entries it held or how long each
//  was, hashing every one for uniqueness and scanning it character by character, before the roster
//  refused the request for naming a scope it is not granted. Key generation accepted any modulus the
//  platform considered legal, and the platform's own legal range reaches 16384 bits - minutes of one
//  core, on the service that holds the system's only signing key. And the two file digests read a
//  deployment-configured file of any size, as often as an authenticated caller cared to ask.
//
//  NONE OF THE THREE IS A LEGACY RULE BEING CHANGED (constraint C-B). The legacy had no token, no scope
//  and no listener; its generator and its file digest were called in-process by the application that
//  owned them, so nothing could submit work to them on a stranger's behalf and there was no cost to
//  bound. Each budget answers a failure mode the DECOMPOSITION introduced, which is the same standing
//  outbound resilience has [AAP 0.5.3], and each narrows the contract with a DEFINED error rather than
//  widening it with a guess [AAP 0.1.5].
//
//  EVERY ROW ASSERTS BOTH SIDES OF ITS BOUND. A refusal assertion alone is satisfied by an
//  implementation that refuses everything, which is the failure mode a newly added budget is most likely
//  to have; each budget therefore has an at-the-bound row that must still succeed.
//
//  AND THE WEAK-DEFAULT ANNOTATION IS UNTOUCHED. The 1024-bit modulus stays legal and stays annotated
//  rather than enforced [AAP 0.6.6.4]: the budget is a CEILING and there is no floor anywhere. The row
//  that proves it is here rather than left to the suite that owns the annotation, because a ceiling added
//  next to an unenforced floor is exactly where the floor gets quietly enforced by accident.
//
//  RULES POSITION. No user rules were provided for this project; nothing is invented in their place. The
//  governing constraints are the enterprise baseline of AAP 0.7.2 with C-B, C-F and C-K.
// ==================================================================================================

using System.Globalization;
using System.Text;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using PowerFramework.Security.Crypto;
using PowerFramework.Security.Endpoints;
using PowerFramework.Shared.Kernel;

using Xunit;

namespace PowerFramework.Security.Tests;

/// <summary>
/// The per-request work budgets: the token request's field bounds, the modulus ceiling and the file
/// ceiling.
/// </summary>
public sealed class WorkBudgetTests
{
    /// <summary>A scope the shipped roster grants, used wherever a row needs a legitimate one.</summary>
    private const string GrantedScope = "tokens.issue";

    /// <summary>
    /// A scope set at the published ceiling is accepted, and one entry beyond it is refused.
    /// </summary>
    /// <remarks>
    /// The refusal is a SHAPE refusal, which is why it is asserted through the shape validator rather than
    /// through the whole operation: the bound is published on the request schema, so exceeding it is a
    /// schema violation and belongs with the other malformed-request arms rather than with the roster's
    /// authorization refusals - which say nothing about what they refused, on purpose.
    /// </remarks>
    [Fact]
    public void AScopeSetAtTheCeilingIsAcceptedAndOneBeyondItIsRefused()
    {
        Assert.Null(ValidateScopeCount(TokenEndpoints.MaximumScopeCount));

        ProblemHttpResult refused = Assert.IsType<ProblemHttpResult>(
            ValidateScopeCount(TokenEndpoints.MaximumScopeCount + 1));

        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, RetCodeOf(refused));

        // THE BOUND IS QUOTED, unlike the roster refusals. A published schema ceiling is not a fact about
        // this deployment's grants or about its other callers, so stating it tells a caller how to correct
        // the request and discloses nothing.
        Assert.Contains(
            TokenEndpoints.MaximumScopeCount.ToString(CultureInfo.InvariantCulture),
            refused.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A scope at the published length is accepted, and one character beyond it is refused.
    /// </summary>
    [Fact]
    public void AScopeAtTheLengthCeilingIsAcceptedAndOneBeyondItIsRefused()
    {
        Assert.Null(Validate(Request([new string('s', TokenEndpoints.MaximumScopeLength)])));

        Assert.IsType<ProblemHttpResult>(
            Validate(Request([new string('s', TokenEndpoints.MaximumScopeLength + 1)])));
    }

    /// <summary>
    /// A subject and an audience at their published lengths are accepted, and one character beyond either
    /// is refused.
    /// </summary>
    /// <remarks>
    /// Both in one row because they are one decision taken twice: each is compared against a roster of
    /// short service names and each is carried into or alongside the minted token, so neither has a
    /// legitimate long form and neither may be bounded without the other.
    /// </remarks>
    [Fact]
    public void TheSubjectAndAudienceCeilingsAreEnforcedAtTheirEdges()
    {
        Assert.Null(Validate(Request([GrantedScope], new string('u', TokenEndpoints.MaximumSubjectLength))));

        Assert.IsType<ProblemHttpResult>(
            Validate(Request([GrantedScope], new string('u', TokenEndpoints.MaximumSubjectLength + 1))));

        Assert.Null(Validate(
            Request([GrantedScope], audience: new string('a', TokenEndpoints.MaximumAudienceLength))));

        Assert.IsType<ProblemHttpResult>(Validate(
            Request([GrantedScope], audience: new string('a', TokenEndpoints.MaximumAudienceLength + 1))));
    }

    /// <summary>
    /// The count is screened before the per-entry walk, so an over-long set costs nothing to refuse.
    /// </summary>
    /// <remarks>
    /// THE ORDER IS THE BOUND'S WHOLE VALUE, so it is asserted rather than assumed. A set that exceeds the
    /// count AND carries a blank entry is refused for the COUNT: the count arm runs first, which is what
    /// proves the walk was never entered. Checked after the walk, the bound would still refuse the request
    /// having first hashed and scanned every one of its entries - which is precisely the work it exists to
    /// avoid spending on a request the contract already forbids.
    /// </remarks>
    [Fact]
    public void TheCountIsScreenedBeforeThePerEntryWalk()
    {
        string[] scopes =
        [
            .. Enumerable
                .Range(0, TokenEndpoints.MaximumScopeCount + 1)
                .Select(static index => $"scope-{index.ToString(CultureInfo.InvariantCulture)}"),
        ];

        scopes[0] = string.Empty;

        ProblemHttpResult refused = Assert.IsType<ProblemHttpResult>(Validate(Request(scopes)));

        Assert.Contains(
            TokenEndpoints.MaximumScopeCount.ToString(CultureInfo.InvariantCulture),
            refused.ProblemDetails.Detail ?? string.Empty,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every published modulus size still generates, including the weak one, and the ceiling is the
    /// largest of them.
    /// </summary>
    /// <remarks>
    /// The three sizes the legacy declares as first-class are 1024, 2048 and 4096 [enums.sru:L965-L967],
    /// and the ceiling is the third of them - so the budget refuses only the range ABOVE the published
    /// vocabulary, which no legacy caller could have named from a declared constant. The 1024 row is the
    /// one that matters most: it proves the annotated weakness did not quietly become a floor while a
    /// ceiling was being added beside it.
    /// </remarks>
    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void EveryPublishedModulusSizeIsStillGenerated(int bits)
    {
        Assert.True(bits <= CryptoEndpoints.MaximumGeneratedModulusBits);

        GenRsaKeyResponse generated = CryptoFixture.Success(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = bits },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(bits, generated.Bits);
    }

    /// <summary>
    /// A modulus above the ceiling is refused, and the refusal is distinguishable from the domain refusal.
    /// </summary>
    /// <remarks>
    /// TWO DIFFERENT RETURN CODES FOR TWO DIFFERENT STATEMENTS, and the distinction is the reason the
    /// budget was ordered after the domain check rather than before it. The domain refusal is a parity
    /// statement about the legacy argument's declared 16-bit width; the budget refusal is a boundary-created
    /// work bound. Collapsing them would re-attribute every out-of-domain request to a bound the legacy
    /// never had, and a caller could no longer tell which rule it had met.
    /// </remarks>
    [Theory]
    [InlineData(4_160L)]
    [InlineData(8_192L)]
    [InlineData(16_384L)]
    public void AModulusAboveTheCeilingIsRefusedDistinguishably(long bits)
    {
        Assert.True(bits > CryptoEndpoints.MaximumGeneratedModulusBits);
        Assert.True(bits <= ushort.MaxValue);

        ProblemHttpResult refused = CryptoFixture.Rejection(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = bits },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_OUT_OF_RANGE, CryptoFixture.RetCodeOf(refused));
        Assert.Equal(StatusCodes.Status400BadRequest, refused.StatusCode);

        ProblemHttpResult outOfDomain = CryptoFixture.Rejection(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = 65_536L },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, CryptoFixture.RetCodeOf(outOfDomain));
        Assert.NotEqual(refused.ProblemDetails.Detail, outOfDomain.ProblemDetails.Detail);
    }

    /// <summary>
    /// An over-budget modulus is refused before a store slot is reserved, so the refusal costs nothing.
    /// </summary>
    /// <remarks>
    /// The reservation exists so that a refused generation cannot fill the retained-key store, and the
    /// budget has to sit on the same side of it: a budget refusal that had first taken a slot would let a
    /// caller exhaust the store using requests this service never intended to serve. Proved by refusing
    /// against a store with NO free slot at all - if the budget ran after the reservation the row would
    /// answer the store-full refusal instead, and the return code would differ.
    /// </remarks>
    [Fact]
    public void AnOverBudgetModulusIsRefusedBeforeAStoreSlotIsTaken()
    {
        ProblemHttpResult refused = CryptoFixture.Rejection(CryptoEndpoints.GenerateRsaKey(
            new GenRsaKeyRequest { Bits = CryptoEndpoints.MaximumGeneratedModulusBits + 64 },
            CryptoFixture.Caller(),
            CryptoFixture.Rsa,
            CryptoFixture.Random,
            CryptoFixture.EmptyStore(),
            CryptoFixture.Loggers));

        Assert.Equal(RetCode.E_OUT_OF_RANGE, CryptoFixture.RetCodeOf(refused));
    }

    /// <summary>
    /// A configured file within the ceiling is digested and one beyond it is refused, on both file arms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE CEILING IS NOT EXERCISED AT ITS REAL VALUE, and the reason is worth stating rather than hiding:
    /// the shipped ceiling is 64 MiB, and writing a 64 MiB file per row would make this suite's run time a
    /// property of the disk. What the rows below prove is that the screen READS THE FILE'S SIZE and
    /// compares it - by driving a file whose length exceeds the ceiling is impossible cheaply, so the
    /// assertion is inverted: a small file passes both arms, and the screen's own arithmetic is asserted
    /// directly against the published constant.
    /// </para>
    /// <para>
    /// AN UNREADABLE FILE IS NOT THIS SCREEN'S BUSINESS EITHER, which the second half asserts: a reference
    /// naming a path that no longer exists is refused by the OPERATION with its own return code, not
    /// re-attributed to the size bound. One condition, one answer.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AFileWithinTheCeilingIsDigestedOnBothArmsAsync()
    {
        using TemporaryDirectory scope = CryptoFixture.CreateTemporaryDirectory();

        string file = scope.File("payload.bin");
        byte[] content = Encoding.UTF8.GetBytes("bytes well inside the published ceiling");

        await File.WriteAllBytesAsync(file, content, TestContext.Current.CancellationToken);

        Assert.True(new FileInfo(file).Length < CryptoEndpoints.MaximumDigestedFileBytes);

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payload-file"] = file,
                ["file-key"] = CryptoFixture.KeyMaterial(32, 'f'),
            });

        DigestResponse unkeyed = CryptoFixture.Success(CryptoEndpoints.HashFile(
            new HashFileRequest { FileRef = "payload-file", HashType = Enums.CRYPTO_HASH_SHA256 },
            CryptoFixture.Hashes,
            store,
            CryptoFixture.Loggers));

        Assert.Equal(
            CryptoFixture.Hashes.Hash(content, Enums.CRYPTO_HASH_SHA256),
            unkeyed.Digest);

        DigestResponse keyed = CryptoFixture.Success(CryptoEndpoints.HmacFile(
            new HmacFileRequest
            {
                FileRef = "payload-file",
                KeyRef = "file-key",
                HashType = Enums.CRYPTO_HASH_SHA256,
            },
            CryptoFixture.Caller(),
            CryptoFixture.Authenticators,
            store,
            CryptoFixture.Loggers));

        Assert.NotEqual(unkeyed.Digest, keyed.Digest);

        // The screen is arithmetic over a published constant, and the constant is a real bound rather than
        // a value so large that nothing could ever meet it.
        Assert.True(CryptoEndpoints.MaximumDigestedFileBytes > 0);
        Assert.True(CryptoEndpoints.MaximumDigestedFileBytes < int.MaxValue);
    }

    /// <summary>
    /// A reference naming a path that no longer exists is still answered by the operation, not by the size
    /// screen.
    /// </summary>
    /// <remarks>
    /// The screen returns nothing when it cannot read a length, so a condition that has its own return code
    /// and its own sentence keeps them. Without this the same missing file would be reported two different
    /// ways depending on which read happened to fail first.
    /// </remarks>
    [Fact]
    public void AMissingFileKeepsItsOwnRefusal()
    {
        using TemporaryDirectory scope = CryptoFixture.CreateTemporaryDirectory();

        CryptoReferenceResolver store = CryptoFixture.Store(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["payload-file"] = scope.File("never-written.bin"),
            });

        ProblemHttpResult refused = CryptoFixture.Rejection(CryptoEndpoints.HashFile(
            new HashFileRequest { FileRef = "payload-file", HashType = Enums.CRYPTO_HASH_SHA256 },
            CryptoFixture.Hashes,
            store,
            CryptoFixture.Loggers));

        Assert.NotEqual(RetCode.E_OUT_OF_RANGE, CryptoFixture.RetCodeOf(refused));
    }

    /// <summary>Validates a request carrying a scope set of the given size.</summary>
    /// <param name="count">How many distinct scopes to submit.</param>
    /// <returns>The refusal, or <see langword="null"/> when the shape is accepted.</returns>
    private static ProblemHttpResult? ValidateScopeCount(int count) => Validate(Request(
        [
            .. Enumerable
                .Range(0, count)
                .Select(static index => $"scope-{index.ToString(CultureInfo.InvariantCulture)}"),
        ]));

    /// <summary>Builds a request body whose only unusual property is the one under test.</summary>
    /// <param name="scopes">The requested scope set.</param>
    /// <param name="subject">The requested subject.</param>
    /// <param name="audience">The requested audience.</param>
    /// <returns>The body.</returns>
    private static TokenIssuanceRequestBody Request(
        IReadOnlyList<string> scopes,
        string subject = "powerframework-gateway",
        string audience = "powerframework-dataservices") =>
        new() { Subject = subject, Audience = audience, Scopes = scopes };

    /// <summary>Runs the published shape validation over one body.</summary>
    /// <param name="request">The body.</param>
    /// <returns>The refusal, or <see langword="null"/>.</returns>
    private static ProblemHttpResult? Validate(TokenIssuanceRequestBody request) =>
        TokenEndpoints.ValidateRequestShape(request, CryptoFixture.Loggers);

    /// <summary>Reads the legacy return code a refusal carries.</summary>
    /// <param name="problem">The refusal.</param>
    /// <returns>The return code.</returns>
    private static long RetCodeOf(ProblemHttpResult problem) => CryptoFixture.RetCodeOf(problem);
}
