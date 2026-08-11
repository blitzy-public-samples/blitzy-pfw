// ==================================================================================================
//  DataServicesOptionsTests - THE DATAWINDOW SERVICE'S CONFIGURATION CONTRACT
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   PowerFramework.DataServices.Configuration.DataServicesOptions and its ten nested groups
//            PowerFramework.DataServices.Configuration.DataServicesOptionsValidator
//
//  WHAT MAKES THIS VALIDATOR WORTH TESTING CAREFULLY
//  ------------------------------------------------------------------------------------------------
//  It validates through TWO mechanisms that overlap, and it deliberately leaves two groups
//  value-unvalidated. Both facts are easy to get wrong from reading either half alone:
//
//   * DataAnnotation attributes on the group types - `[Required]`, `[Range]` - are collected by
//     `AppendAnnotationFailures`, and
//   * hand-written checks in the validator body cover the rules an attribute cannot express.
//
//  The two are COMPLEMENTARY rather than redundant, and one pair genuinely covers each other's gap -
//  see the circuit-breaker ratio tests, where `[Range(0,1)]` permits zero and the explicit check
//  rejects it, while the explicit check permits values above one and the attribute rejects them.
//
//  The two unvalidated groups - ContextMenu and DropDownSearch - configure the HEADLESS HALVES of
//  capabilities whose rendering half is deferred to DesignSystem. Constraining values whose consumer
//  does not exist yet would be inventing a contract for Phase 2, so the absence is asserted as
//  deliberately as the presences. An untested absence reads downstream as an oversight.
//
//  EVERY COUNT AND MESSAGE BELOW WAS MEASURED against the real validator before being asserted. The
//  intuitive expectations were wrong in three places, each noted at the test that records it.
// ==================================================================================================

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Expressions;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class DataServicesOptionsTests
{
    private static readonly DataServicesOptionsValidator Validator = new();

    /// <summary>
    /// An options instance that validates cleanly, for isolating one rule at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two upstream addresses must be supplied: both carry <c>[Required]</c> and both default to
    /// empty, so a default-constructed instance does NOT validate. That is asserted directly in
    /// <see cref="TheDefaultConfigurationRequiresBothUpstreamAddressesAndAnIssuanceCredential"/> rather
    /// than worked around silently here.
    /// </para>
    /// <para>
    /// An issuance credential must be supplied too, and for the same reason it is set here rather than
    /// hidden: a deployment that can present NEITHER of the two credentials contract C-01 accepts obtains
    /// no service token at all, so the validator refuses it. The secret half is the one the documented
    /// topology uses; <see cref="AnIssuanceCredentialIsRequiredAndEitherSchemeSatisfiesIt"/> asserts the
    /// rule itself, including that the certificate pair satisfies it equally.
    /// </para>
    /// </remarks>
    private static DataServicesOptions ValidOptions()
    {
        var options = new DataServicesOptions();
        options.Persistence.Address = "http://persistence:5101";
        options.Security.BaseAddress = "http://security:5104";
        options.Security.ClientSecret = "an-issuance-secret-shaped-value";
        return options;
    }

    private static string[] Failures(DataServicesOptions options, string? name = null) =>
        Validator.Validate(name, options).Failures?.ToArray() ?? [];

    private static bool Succeeds(DataServicesOptions options) =>
        Validator.Validate(name: null, options).Succeeded;

    // ==============================================================================================
    //  SECTION NAMES AND THE TEN GROUPS
    // ==============================================================================================

    [Fact]
    public void TheSectionNamesAreTheKeysAnOperatorConfiguresAgainst()
    {
        Assert.Equal("DataServices", DataServicesOptions.SectionName);

        // THE JWT SECTION NAME IS DELIBERATELY *NOT* THE FRAMEWORK'S OWN.
        //
        // Gateway binds bearer settings at "Authentication:Schemes:Bearer" - the key ASP.NET Core's own
        // handler reads. DataServices uses "Authentication:Jwt" instead, because its validation surface
        // is its own typed options rather than the stock handler's. Recorded so the asymmetry between
        // the two services reads as a decision rather than as a copy-paste divergence.
        Assert.Equal("Authentication:Jwt", JwtAuthenticationOptions.SectionName);
    }

    [Fact]
    public void AllTenGroupsAreNonNullByDefaultSoAPartialConfigurationBinds()
    {
        var options = new DataServicesOptions();

        // EVERY GROUP IS PRE-INSTANTIATED, WHICH IS WHAT MAKES A PARTIAL SECTION SAFE.
        //
        // An operator configuring only `DataServices:Localization` leaves the other nine absent.
        // Pre-instantiating them means the absent nine take their defaults rather than binding to null
        // and throwing on first read - which is why the validator's null check exists only for the case
        // that CAN still produce null: a section explicitly DECLARED with a null value.
        Assert.NotNull(options.Localization);
        Assert.NotNull(options.RowSelect);
        Assert.NotNull(options.ContextMenu);
        Assert.NotNull(options.ColumnExpression);
        Assert.NotNull(options.DropDownSearch);
        Assert.NotNull(options.Persistence);
        Assert.NotNull(options.Security);
        Assert.NotNull(options.Resilience);
        Assert.NotNull(options.Sessions);
        Assert.NotNull(options.EventChain);
    }

    [Fact]
    public void TheDefaultConfigurationRequiresBothUpstreamAddressesAndAnIssuanceCredential()
    {
        // MEASURED, AND NOT WHAT A DEFAULT-CONSTRUCTED OPTIONS USUALLY DOES: THE DEFAULTS DO NOT PASS.
        //
        // `Persistence.Address` and `Security.BaseAddress` both carry `[Required]` and both default to
        // the empty string; and neither issuance credential is configured either. So a DataServices
        // started with no configuration fails startup validation with exactly three failures, each
        // naming the key an operator must set.
        //
        // That is the correct posture rather than an awkward default. DataServices cannot serve C-03 or
        // C-04 without reaching Persistence, cannot validate a token without Security's keys, and cannot
        // OBTAIN a token without a caller credential for the one operation a bearer token cannot protect
        // - so a service that started without any of the three would accept requests and fail every one.
        // Failing at startup with the missing keys named is the fail-fast behaviour the legacy's own
        // `HALT CLOSE` on a decoded assertion failure establishes
        // [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
        string[] failures = Failures(new DataServicesOptions());

        Assert.Equal(3, failures.Length);
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Persistence:Address", StringComparison.Ordinal)
            && f.Contains("required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Security:BaseAddress", StringComparison.Ordinal)
            && f.Contains("required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, static f =>
            f.Contains(
                SecurityClientOptions.ClientSecretConfigurationKey,
                StringComparison.Ordinal)
            && f.Contains("MutualTls", StringComparison.Ordinal));
    }

    [Fact]
    public void AConfigurationWithBothAddressesSuppliedValidatesCleanly()
    {
        Assert.True(Succeeds(ValidOptions()), string.Join(" | ", Failures(ValidOptions())));
    }

    [Fact]
    public void ANullOptionsIsAProgrammingErrorRatherThanAValidationFailure()
    {
        // THROWS, NOT FAILS.
        //
        // A null options object cannot have come from configuration binding - the options infrastructure
        // always supplies an instance. It means a caller passed null, which is a defect in the caller
        // rather than a misconfiguration, so reporting it as a validation failure would send an operator
        // looking for a settings key that does not exist.
        Assert.Throws<ArgumentNullException>(() => Validator.Validate(name: null, options: null!));
    }

    // ==============================================================================================
    //  THE PRESERVED HARDCODED LOCALE, AND THE THREE-VALUE ACCEPTED SET
    // ==============================================================================================

    [Fact]
    public void TheLocaleDefaultsToEnglishReproducingTheHardcodedLegacyValue()
    {
        // ws_objects/pfw.pbl.src/pfw.sra:L94 sets `lang = "en"` literally inside the open event, then
        // selects one of three provider classes from it [:L95-L102]. The observable default is preserved
        // exactly; the UN-CONFIGURABILITY is not, because that is a structural property rather than a
        // behaviour - and the AAP is explicit that the value is "preserved as the default but made
        // overridable".
        Assert.Equal("en", new DataServicesOptions().Localization.Locale);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("chs")]
    [InlineData("cht")]
    public void TheThreeLegacyLocalesAreAccepted(string locale)
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = locale;

        // EXACTLY THREE, MATCHING THE THREE PROVIDER CLASSES the legacy selects between. There is no
        // fourth locale anywhere in the estate, and `pfw.i18n.xml` carries only the `en` and `cht`
        // translation tables - `chs` needs none, because Simplified Chinese is the BASE locale and its
        // provider is a genuine no-op.
        Assert.True(Succeeds(options));
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("En")]
    [InlineData("CHS")]
    [InlineData("Cht")]
    public void TheLocaleComparisonIsCaseSensitiveSoAnUppercaseVariantIsRejected(string locale)
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = locale;

        string failure = Assert.Single(Failures(options));

        // ORDINAL, DELIBERATELY - AND THE REASON IS AT THE OTHER END OF THE MAPPING.
        //
        // The check is `AcceptedLocales.Contains(locale, StringComparer.Ordinal)`, case-SENSITIVE,
        // because the value is mapped onto one of three provider classes exactly as
        // ws_objects/pfw.pbl.src/pfw.sra:L95-L102 maps it, and that legacy comparison is itself exact.
        //
        // Accepting "EN" here would mean accepting a value the mapping cannot resolve, which turns a
        // clear startup failure into a silent fallback to NO provider - and the localization facade's
        // documented fallback is SILENT PASSTHROUGH, returning text unchanged without throwing or
        // logging. So the mistake would surface as untranslated UI rather than as an error, which is the
        // worst of both outcomes.
        Assert.Contains("must be one of", failure, StringComparison.Ordinal);
        Assert.Contains("case-sensitively", failure, StringComparison.Ordinal);

        // THE OFFENDING VALUE IS ECHOED, so an operator sees what was actually read from configuration.
        Assert.Contains($"\"{locale}\"", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankLocaleIsRejectedByTheRequiredAnnotationRatherThanBySetMembership(string locale)
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = locale;

        string failure = Assert.Single(Failures(options));

        // MEASURED, AND THE OPPOSITE OF THE INTUITIVE READING OF THE VALIDATOR BODY.
        //
        // The hand-written membership check is guarded by `!IsNullOrWhiteSpace(locale)`, so reading only
        // the validator body suggests blank is SKIPPED and therefore accepted. It is not: `Locale`
        // carries `[Required]`, and `AppendAnnotationFailures` runs FIRST. So blank is rejected - by the
        // annotation, with the annotation's own message, not by the membership rule.
        //
        // Whitespace-only is rejected too, because `RequiredAttribute` trims before testing unless
        // `AllowEmptyStrings` is set. Both were measured.
        //
        // This is the clearest example of why the two mechanisms have to be read together: neither half
        // alone predicts the behaviour, and asserting from either one in isolation produces a wrong test.
        Assert.Contains("required", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DataServices:Localization:Locale", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("must be one of", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownLocaleFailureCitesTheOracleLocatorForTheProviderMapping()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = "de";

        string failure = Assert.Single(Failures(options));

        // THE LOCATOR IS IN THE RUNTIME MESSAGE, WHICH IS UNUSUAL AND DELIBERATE.
        //
        // Every behavioural assertion in this port must be traceable to a `ws_objects/**` locator,
        // because the legacy tree is the only statement of intended behaviour that exists - `logfile.md`
        // stops at the 2022 framework version while the commit history runs years later, and the two
        // PowerBuilder build definitions contradict each other. Putting the locator in the message means
        // an operator hitting the rejection can see WHY three values and not more.
        Assert.Contains("ws_objects/pfw.pbl.src/pfw.sra:L95-L102", failure, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  THE PRESERVED LEGACY REJECTION - RowSelect.Style
    // ==============================================================================================

    [Fact]
    public void TheRowSelectStyleDefaultsToOne()
    {
        Assert.Equal(1L, new DataServicesOptions().RowSelect.Style);
    }

    [Fact]
    public void AZeroRowSelectStyleIsRejectedBecauseTheLegacySetterRejectsIt()
    {
        DataServicesOptions options = ValidOptions();
        options.RowSelect.Style = 0L;

        string failure = Assert.Single(Failures(options));

        // THIS IS A LEGACY REJECTION REPRODUCED, NOT A .NET-SIDE TIGHTENING - AND THE DIFFERENCE MATTERS.
        //
        // n_cst_dwsvc_rowselect.sru:L169 returns an invalid-argument code for a zero style. Had this
        // check been invented on the .NET side it would be a behavioural change, because a configuration
        // the legacy accepted would fail to start. Because the legacy rejects it too, reproducing the
        // rejection is the faithful choice - and moving it from first-call to STARTUP is the fail-fast
        // posture rather than a new constraint.
        Assert.Contains("must not be zero", failure, StringComparison.Ordinal);
        Assert.Contains("n_cst_dwsvc_rowselect.sru:L169", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(4L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public void AnyNonZeroRowSelectStyleIsAcceptedIncludingNegatives(long style)
    {
        DataServicesOptions options = ValidOptions();
        options.RowSelect.Style = style;

        // ONLY ZERO IS REJECTED - INCLUDING NEGATIVES, WHICH LOOK WRONG AND ARE DELIBERATELY NOT CHECKED.
        //
        // The legacy rejects exactly zero and nothing else, so the port rejects exactly zero and nothing
        // else. Adding a positivity check would be the .NET side inventing a constraint the oracle does
        // not have - the style is a bitmask-shaped long and the legacy never bounds it. Negative values
        // are included in this theory precisely because excluding them would have been the plausible
        // mistake.
        Assert.True(Succeeds(options));
    }

    // ==============================================================================================
    //  THE DELIBERATELY UNVALIDATED GROUPS
    // ==============================================================================================

    [Fact]
    public void TheContextMenuGroupIsDeliberatelyNotValueValidated()
    {
        DataServicesOptions options = ValidOptions();

        // ALL FIVE FLAGS FLIPPED, AND NOTHING IS REJECTED.
        //
        // ContextMenu configures the HEADLESS half of a capability whose rendering half is deferred:
        // DataServices produces the menu ITEM MODEL - labels, ids, enabled and split flags, computed
        // logical text widths - while DPI-to-pixel conversion, font measurement and actual rendering
        // belong to DesignSystem and are reached, eventually, through Gateway's reserved
        // `/v1/design/**` route.
        //
        // Constraining these would mean inventing a contract for a consumer that does not exist yet,
        // which is Phase-2 design work forbidden by C-D. The absence is asserted so it reads as a
        // decision rather than as a gap someone should close.
        options.ContextMenu.ColAutoWidth = false;
        options.ContextMenu.ColCheck = false;
        options.ContextMenu.ColCopy = false;
        options.ContextMenu.ColPaste = false;
        options.ContextMenu.ItemCopy = false;

        Assert.True(Succeeds(options));
    }

    [Fact]
    public void TheContextMenuDefaultsEnableTheFiveColumnBehaviours()
    {
        ContextMenuOptions menu = new DataServicesOptions().ContextMenu;

        Assert.True(menu.ColAutoWidth);
        Assert.True(menu.ColCheck);
        Assert.True(menu.ColCopy);
        Assert.True(menu.ColPaste);
        Assert.True(menu.ItemCopy);
    }

    [Fact]
    public void TheDropDownSearchGroupIsDeliberatelyNotValueValidated()
    {
        DataServicesOptions options = ValidOptions();

        // INCLUDING VALUES OUTSIDE THE LEGACY'S OWN DECLARED RANGE.
        //
        // `FilterType` corresponds to the legacy FILTER_* set, whose declared values are 0, 1, 2, 4 and
        // 7 - so 999 is not a legal filter type, and it is still accepted. Same reasoning as ContextMenu:
        // the search state machine's headless half ships, its window-positioning and IME half is
        // deferred, and the value's full consumer does not exist yet.
        //
        // `PinyinMatchFlags` is left unconstrained for a sharper reason, and it is NOT that the flags are
        // unknown - they are named at `enums.sru:L1146-L1149`, so the legacy call site's literal 7 is
        // exactly PY_LIKE_IGNORE_CASE | PY_LIKE_IGNORE_WIDTH | PY_LIKE_FUZZY_SOUND. What is unknown is
        // what the closed `pfw.dll` DOES with any bit outside those three, because the lookup table and
        // the matching rule live only inside that binary. `PinyinFirstLetterLike` accordingly IGNORES an
        // undefined bit rather than rejecting it - throwing would invent a validation the oracle does not
        // perform - so an options validator that rejected one here would contradict the matcher it
        // configures. The native table and rule remain the single genuine parity risk in the in-scope
        // set, tracked as such rather than papered over; the flag contract is not part of it.
        options.DropDownSearch.FilterType = 999u;
        options.DropDownSearch.PinyinMatchFlags = -1L;

        Assert.True(Succeeds(options));
    }

    [Fact]
    public void TheShowFilteredRowsSettingIsTriStateRatherThanBoolean()
    {
        var options = new DataServicesOptions();

        // `bool?` AND IT DEFAULTS TO null, WHICH IS A THIRD STATE AND NOT AN OVERSIGHT.
        //
        // null means "leave the legacy default alone"; true and false are explicit overrides. Collapsing
        // it to `bool` would force a choice at startup and make "unspecified" unrepresentable - the same
        // class of mistake as collapsing the tri-state return algebra, where null is neither succeeded
        // nor failed.
        Assert.Null(options.DropDownSearch.ShowFilteredRows);

        options.DropDownSearch.ShowFilteredRows = true;
        Assert.True(options.DropDownSearch.ShowFilteredRows);

        options.DropDownSearch.ShowFilteredRows = false;
        Assert.False(options.DropDownSearch.ShowFilteredRows);
    }

    [Fact]
    public void TheEventChainGroupIsCheckedOnlyForHavingBeenBound()
    {
        DataServicesOptions options = ValidOptions();

        // STRICT ORDERING DEFAULTS TO TRUE, and flipping it is accepted without complaint.
        //
        // The default is the important half. Ordering is the highest risk in this refactor: the
        // item-change chain's discipline is STRICTLY SYNCHRONOUS with no reordering permitted, because
        // the validation-error handler reads and clears the code stashed by the preceding item-change
        // event - so its behaviour is a function of the prior event's return value. Enforcement therefore
        // defaults ON.
        //
        // It is configurable because the focus, mouse and row-focus notifications use the SEQUENCED
        // discipline instead, where a monotonic token is sufficient for detection.
        Assert.True(options.EventChain.StrictOrdering);

        options.EventChain.StrictOrdering = false;
        Assert.True(Succeeds(options));
    }

    [Fact]
    public void ANullGroupIsReportedWithGuidanceToOmitTheSectionInstead()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization = null!;

        string failure = Assert.Single(Failures(options));

        // THE MESSAGE TELLS THE OPERATOR WHAT TO DO, not just what is wrong.
        //
        // A group binds to null only when a section is DECLARED with an explicit null value - writing
        // `"Localization": null` in a settings file. The remedy is to omit the section entirely, which
        // accepts the defaults, and saying so turns a puzzling failure into a one-line fix.
        Assert.Contains("bound to null", failure, StringComparison.Ordinal);
        Assert.Contains("Omit the section entirely", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullGroupSuppressesOnlyItsOwnFurtherChecks()
    {
        DataServicesOptions options = ValidOptions();
        options.RowSelect = null!;

        // ONE FAILURE, NOT TWO.
        //
        // `EnsureGroupBound` short-circuits, so a null RowSelect does not additionally report that its
        // Style is zero - which it would, since a null group has no Style to read. Reporting a derived
        // error from an absent group sends an operator looking for a value they never set.
        string failure = Assert.Single(Failures(options));
        Assert.Contains("RowSelect was bound to null", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("Style must not be zero", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void SeveralNullGroupsAreAllReportedTogether()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization = null!;
        options.RowSelect = null!;
        options.Sessions = null!;

        // ALL THREE, BECAUSE EACH IS AN INDEPENDENT FIX.
        //
        // Contrast with the short-circuit above: suppressing a group's OWN derived checks is helpful,
        // whereas suppressing OTHER groups would make an operator fix and restart three times to
        // discover three independent mistakes.
        Assert.Equal(3, Failures(options).Length);
    }

    // ==============================================================================================
    //  THE TWO UPSTREAM ADDRESSES
    // ==============================================================================================

    [Theory]
    [InlineData("http://persistence:5101")]
    [InlineData("https://persistence:5101")]
    [InlineData("http://localhost:5101")]
    [InlineData("https://persistence.example.internal")]
    public void AWellFormedUpstreamAddressIsAccepted(string address)
    {
        DataServicesOptions options = ValidOptions();
        options.Persistence.Address = address;

        Assert.True(Succeeds(options));
    }

    [Theory]
    [InlineData("persistence")]
    [InlineData("5101")]
    [InlineData("not a uri")]
    [InlineData("//persistence:5101")]
    public void AnUpstreamAddressThatIsNotAnAbsoluteUriIsRejected(string address)
    {
        DataServicesOptions options = ValidOptions();
        options.Persistence.Address = address;

        string failure = Assert.Single(Failures(options));

        Assert.Contains("must be an absolute address", failure, StringComparison.Ordinal);
        Assert.Contains("DataServices:Persistence:Address", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://persistence:5101", "ftp")]
    [InlineData("grpc://persistence:5101", "grpc")]

    // MEASURED, AND COUNTER-INTUITIVE. See the remarks below - both of these reach the SCHEME check
    // rather than the absolute-address check, which is not what either input looks like it would do.
    [InlineData("persistence:5101", "persistence")]
    [InlineData("/var/run/persistence.sock", "file")]
    public void AnUpstreamAddressWithAnUnsupportedSchemeIsRejectedAndTheSchemeIsNamed(
        string address,
        string expectedScheme)
    {
        DataServicesOptions options = ValidOptions();
        options.Security.BaseAddress = address;

        string failure = Assert.Single(Failures(options));

        // THE SCHEME CHECK CARRIES MORE WEIGHT THAN IT APPEARS TO, AND TWO MEASURED CASES SHOW WHY.
        //
        //   * `"persistence:5101"` - which reads like a bare host and port with no scheme - IS a valid
        //     absolute URI. It parses as scheme `persistence` with path `5101`. So the absolute-address
        //     check passes it and only the scheme check stops it. Anyone reasoning that "the absolute
        //     check catches a missing scheme" is wrong: a colon is all it takes to look like a scheme.
        //
        //   * `"/var/run/persistence.sock"` is ALSO an absolute URI on this platform, resolving to scheme
        //     `file`, because .NET treats an absolute filesystem path as a file URI. So a socket path -
        //     an entirely plausible thing for someone to configure - reaches the scheme check too.
        //
        // `grpc://` is in the list deliberately as well: it LOOKS right for a gRPC upstream and is not a
        // real scheme. gRPC runs over HTTP/2 and its channels take an http or https target, so accepting
        // it would produce a channel that never connects.
        Assert.Contains("must use the http or https scheme", failure, StringComparison.Ordinal);

        // THE OFFENDING SCHEME IS NAMED, which matters most for the `file` case - where the scheme
        // appears nowhere in what the operator typed.
        Assert.Contains($"\"{expectedScheme}\"", failure, StringComparison.Ordinal);
        Assert.Contains("DataServices:Security:BaseAddress", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoBadAddressesAreBothReported()
    {
        DataServicesOptions options = ValidOptions();
        options.Persistence.Address = "ftp://persistence";
        options.Security.BaseAddress = "not a uri";

        Assert.Equal(2, Failures(options).Length);
    }

    // ==============================================================================================
    //  RESILIENCE - the settings that exist BECAUSE of decomposition
    // ==============================================================================================

    [Fact]
    public void TheResilienceDefaultsAreDeclaredIdenticallyForBothTypedClients()
    {
        ResilienceOptions resilience = new DataServicesOptions().Resilience;

        // THESE SETTINGS EXIST BECAUSE AN IN-PROCESS CALL CANNOT FAIL IN TRANSIT AND A NETWORK CALL CAN.
        //
        // Handling that is required BY the transition rather than being a robustness improvement layered
        // on top - without it the first transient fault would surface as a defect the legacy could not
        // have had, which is a regression introduced by the refactor rather than a preserved behaviour.
        foreach (ClientResilienceOptions client in (ClientResilienceOptions[])
            [resilience.Persistence, resilience.Security])
        {
            Assert.Equal(3, client.MaxRetryAttempts);
            Assert.Equal(TimeSpan.FromSeconds(2), client.RetryBaseDelay);
            Assert.Equal(0.1d, client.CircuitBreakerFailureRatio);
            Assert.Equal(100, client.CircuitBreakerMinimumThroughput);
            Assert.Equal(TimeSpan.FromSeconds(30), client.CircuitBreakerSamplingDuration);
            Assert.Equal(TimeSpan.FromSeconds(5), client.CircuitBreakerBreakDuration);
            Assert.Equal(TimeSpan.FromSeconds(30), client.RequestTimeout);

            // THE STREAM BOUND IS DERIVED RATHER THAN CHOSEN, which is why the value is worth pinning:
            // it is Persistence's own handle idle expiry (Persistence:Handles:IdleExpirySeconds, 900
            // seconds), so a stream still open past it is holding a handle the upstream's own policy
            // would already have released. It is longer than RequestTimeout because a retrieval that
            // runs for minutes is correct while a unary call that does so is not.
            Assert.Equal(TimeSpan.FromSeconds(900), client.StreamDeadline);
            Assert.True(client.StreamDeadline > client.RequestTimeout);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveStreamDeadlineIsRejectedWithItsOwnPath(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.StreamDeadline = TimeSpan.FromSeconds(seconds);

        string[] failures = Failures(options);

        // A non-positive stream bound would expire before the retrieval reached the upstream, so every
        // stream would report a deadline failure. The coherence rule between the two bounds deliberately
        // does NOT also fire here: it is guarded on both values being positive, so an operator with one
        // bad value is told about that value rather than about a comparison they did not write.
        string failure = Assert.Single(failures);

        Assert.Contains("must be a positive duration", failure, StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:Resilience:Persistence:StreamDeadline",
            failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStreamDeadlineBelowTheRequestTimeoutIsRejectedOnBothEdges()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.RequestTimeout = TimeSpan.FromSeconds(30);
        options.Resilience.Persistence.StreamDeadline = TimeSpan.FromSeconds(29);
        options.Resilience.Security.RequestTimeout = TimeSpan.FromSeconds(30);
        options.Resilience.Security.StreamDeadline = TimeSpan.FromSeconds(29);

        string[] failures = Failures(options);

        // TWO EDGES, TWO INDEPENDENT FAILURES, each naming its own section - the same
        // tuned-independently property the rest of this group asserts. The inversion is a contradiction
        // rather than a tighter policy: the two settings exist separately only because a stream is
        // legitimately longer-lived, so a shorter stream bound abandons retrievals sooner than the
        // ordinary calls beside them, silently, as a deadline failure that reads as an upstream fault.
        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Resilience:Persistence:StreamDeadline", StringComparison.Ordinal));
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Resilience:Security:StreamDeadline", StringComparison.Ordinal));
    }

    [Fact]
    public void AZeroCircuitBreakerRatioIsCaughtOnlyByTheExplicitCheckBecauseTheRangeAllowsIt()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.CircuitBreakerFailureRatio = 0d;

        string failure = Assert.Single(Failures(options));

        // MEASURED: EXACTLY ONE FAILURE, FROM THE HAND-WRITTEN CHECK.
        //
        // The property carries `[Range(0, 1)]`, and zero is INSIDE that range - so the attribute passes
        // it. Only the explicit `<= 0` check in the validator body rejects it. This is the clearest
        // illustration that the two validation mechanisms are complementary rather than redundant: the
        // attribute cannot express an EXCLUSIVE lower bound, so the hand-written check exists precisely
        // to close that gap.
        //
        // And the gap is worth closing: a zero failure ratio would mean "break on any failure at all",
        // which is not a resilience policy but an outage amplifier - one transient fault would open the
        // circuit and take the upstream out of service for the whole break duration.
        Assert.Contains("CircuitBreakerFailureRatio", failure, StringComparison.Ordinal);
        Assert.Contains("greater than zero", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeCircuitBreakerRatioIsCaughtByBothMechanismsAtOnce()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.CircuitBreakerFailureRatio = -1d;

        string[] failures = Failures(options);

        // MEASURED: TWO FAILURES FOR ONE MISTAKE, AND THAT IS ACCEPTABLE RATHER THAN A DEFECT.
        //
        // A negative ratio is outside `[Range(0, 1)]` AND fails the explicit `<= 0` check, so both
        // mechanisms report it. Duplicate reporting of a single bad value is a small cost; the
        // alternative - suppressing one mechanism when the other fires - would require the validator to
        // reason about which rules overlap, and would risk suppressing a genuinely different failure.
        //
        // Asserted rather than tolerated silently, so the count is a known property rather than a
        // surprise the next person has to re-derive.
        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, static f => f.Contains("must be between 0 and 1", StringComparison.Ordinal));
        Assert.Contains(failures, static f => f.Contains("greater than zero", StringComparison.Ordinal));
    }

    [Fact]
    public void ARatioAboveOneIsCaughtOnlyByTheRangeAnnotationBecauseTheExplicitCheckAllowsIt()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.CircuitBreakerFailureRatio = 1.5d;

        string failure = Assert.Single(Failures(options));

        // MEASURED: THE COMPLEMENT OF THE ZERO CASE, AND IT COMPLETES THE PICTURE.
        //
        // The explicit check tests only `<= 0`, so it passes 1.5 - even though its own message promises
        // "greater than zero AND AT MOST ONE". The upper bound it advertises is enforced by the `[Range]`
        // attribute, not by the check itself.
        //
        // So the two together cover the whole invalid domain and NEITHER covers it alone. That is worth
        // pinning explicitly, because a future reader who removed the "redundant-looking" attribute would
        // silently allow a failure ratio above 1 - a policy that can never trip and therefore disables
        // the circuit breaker entirely.
        Assert.Contains("must be between 0 and 1", failure, StringComparison.Ordinal);
        Assert.DoesNotContain("greater than zero", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void EachClientsResilienceIsValidatedIndependentlyAndPathedSeparately()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.CircuitBreakerFailureRatio = 0d;
        options.Resilience.Security.CircuitBreakerFailureRatio = -1d;

        string[] failures = Failures(options);

        // MEASURED AS THREE, NOT TWO: one from the zero (explicit check only) and two from the negative
        // (both mechanisms). The arithmetic follows directly from the three tests above.
        Assert.Equal(3, failures.Length);

        // AND THE TWO CLIENTS ARE PATHED SEPARATELY.
        //
        // They reach different services with different characteristics - Persistence over gRPC and
        // Security over REST - so their policies are configured and validated independently. A shared
        // policy would force one service's tuning onto the other.
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Resilience:Persistence:", StringComparison.Ordinal));
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Resilience:Security:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveResilienceDurationIsRejectedAndTheValueIsEchoed(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Security.RequestTimeout = TimeSpan.FromSeconds(seconds);

        string failure = Assert.Single(Failures(options));

        Assert.Contains("must be a positive duration", failure, StringComparison.Ordinal);
        Assert.Contains("DataServices:Resilience:Security:RequestTimeout", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AllFourResilienceDurationsAreChecked()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence.RetryBaseDelay = TimeSpan.Zero;
        options.Resilience.Persistence.CircuitBreakerSamplingDuration = TimeSpan.Zero;
        options.Resilience.Persistence.CircuitBreakerBreakDuration = TimeSpan.Zero;
        options.Resilience.Persistence.RequestTimeout = TimeSpan.Zero;

        string[] failures = Failures(options);

        // FOUR DURATIONS, FOUR INDEPENDENT FAILURES.
        //
        // `TimeSpan` cannot carry a `[Range]` attribute usefully, which is why all four go through the
        // hand-written positive-duration helper rather than through annotations. Checking each separately
        // means an operator with one bad duration is told which one.
        Assert.Equal(4, failures.Length);
        foreach (string name in (string[])
            ["RetryBaseDelay", "CircuitBreakerSamplingDuration", "CircuitBreakerBreakDuration", "RequestTimeout"])
        {
            Assert.Contains(failures, f => f.Contains(name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ANullClientResilienceGroupIsReportedWithItsOwnPath()
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Persistence = null!;

        string failure = Assert.Single(Failures(options));

        // THE PATH IS THE NESTED ONE, so an operator knows which of the two clients' sections is at
        // fault rather than only that "Resilience" is.
        Assert.Contains(
            "DataServices:Resilience:Persistence was bound to null",
            failure,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void ANonPositiveCircuitBreakerThroughputIsRejectedByItsRangeAnnotation(int throughput)
    {
        DataServicesOptions options = ValidOptions();
        options.Resilience.Security.CircuitBreakerMinimumThroughput = throughput;

        string failure = Assert.Single(Failures(options));

        // A MINIMUM THROUGHPUT OF ZERO WOULD LET THE BREAKER TRIP ON A SINGLE SAMPLE.
        //
        // The threshold exists so a failure RATIO is only meaningful once enough calls have been
        // observed; without it, one failed call out of one is a 100% failure ratio and opens the circuit.
        Assert.Contains("CircuitBreakerMinimumThroughput", failure, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  SESSION LIFETIMES - the server-held state a stateless boundary cannot carry
    // ==============================================================================================

    [Fact]
    public void BothSessionKindsHaveTheirOwnLifetimeSettings()
    {
        SessionsOptions sessions = new DataServicesOptions().Sessions;

        // TWO SESSION KINDS, SEPARATELY CONFIGURED, BECAUSE THEY HOLD DIFFERENT THINGS.
        //
        // A VALIDATION session holds the four pieces of cross-event state from se_cst_dw.sru:L89-L96 -
        // the disabled-event bitmask, two re-entrancy flags, and the item-changed return value STASHED
        // for the validation-error event to consume. An EXPRESSION session scopes foreign-variable
        // DataWindow handles, and its lifetime governs when a cross-DataWindow reference becomes
        // unresolvable. Tying them to one timeout would couple an interactive edit's idle window to an
        // expression graph's.
        Assert.NotNull(sessions.ValidationSession);
        Assert.NotNull(sessions.ExpressionSession);

        Assert.Equal(TimeSpan.FromMinutes(5), sessions.ValidationSession.IdleTimeout);
        Assert.Equal(100, sessions.ValidationSession.MaxConcurrentSessions);
        Assert.Equal(TimeSpan.FromMinutes(5), sessions.ExpressionSession.IdleTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveSessionIdleTimeoutIsRejectedAndTheValueIsEchoed(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.Sessions.ValidationSession.IdleTimeout = TimeSpan.FromSeconds(seconds);

        string failure = Assert.Single(Failures(options));

        // A ZERO IDLE TIMEOUT WOULD EXPIRE EVERY SESSION IMMEDIATELY, WHICH IS WORSE THAN A LEAK.
        //
        // The session is the only place the cross-event state can live, so expiring it instantly would
        // break the item-change chain on every edit - and it would do so depending on timing, which is
        // the hardest possible failure to diagnose.
        Assert.Contains("IdleTimeout", failure, StringComparison.Ordinal);
        Assert.Contains("must be a positive duration", failure, StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:Sessions:ValidationSession",
            failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EachSessionKindIsValidatedIndependently()
    {
        DataServicesOptions options = ValidOptions();
        options.Sessions.ValidationSession.IdleTimeout = TimeSpan.Zero;
        options.Sessions.ExpressionSession.IdleTimeout = TimeSpan.FromSeconds(-5);

        string[] failures = Failures(options);

        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, static f => f.Contains(":ValidationSession:", StringComparison.Ordinal));
        Assert.Contains(failures, static f => f.Contains(":ExpressionSession:", StringComparison.Ordinal));
    }

    [Fact]
    public void AZeroMaxConcurrentSessionsIsRejectedByItsRangeAnnotation()
    {
        DataServicesOptions options = ValidOptions();
        options.Sessions.ValidationSession.MaxConcurrentSessions = 0;

        string failure = Assert.Single(Failures(options));

        // ZERO WOULD ADMIT NO SESSIONS AT ALL, so every validation chain would be refused - a service
        // that starts and then rejects all work.
        Assert.Contains("MaxConcurrentSessions", failure, StringComparison.Ordinal);
        Assert.Contains("must be between 1", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullSessionLifetimeGroupIsReportedWithItsOwnPath()
    {
        DataServicesOptions options = ValidOptions();
        options.Sessions.ExpressionSession = null!;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:Sessions:ExpressionSession was bound to null",
            failure,
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  THE COLUMN-EXPRESSION GROUP
    // ==============================================================================================

    [Fact]
    public void TheColumnExpressionDefaultsLeaveTracingOffAndSizeTheCalcStack()
    {
        ColumnExpressionOptions expression = new DataServicesOptions().ColumnExpression;

        // TRACE DEFAULTS OFF. Trace records arrive on C-04's `TraceChannel` - one of the two INVERTED
        // streams - and emitting them unconditionally would put a diagnostic stream behind every
        // calculation in normal operation.
        Assert.False(expression.Trace);

        // THE CALC STACK IS THE VECTOR CONTAINER FROM n_cst_dwsvc_columnexp.sru:L110, created at :L2421 -
        // the recursion stack that detects a cycle, and the structure behind the trace event's call
        // stack. Its initial capacity is a sizing hint rather than a bound.
        Assert.Equal(20, expression.CalcStackInitialCapacity);

        Assert.Equal(200, expression.RedrawSuppressionRowThreshold);

        // THE MACRO BACKSTOP IS DERIVED FROM THE SESSION LIFETIME, and asserting the IDENTITY rather
        // than the literal is what records the derivation: an invocation still outstanding past the
        // point at which its session would have been reclaimed is holding something this service had
        // already given up on. The two are separate keys so that shortening one does not silently
        // shorten the other, and they start equal so a reader can see where the value came from.
        Assert.Equal(SessionLifetimeOptions.DefaultIdleTimeout, expression.MacroInvocationTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), expression.MacroInvocationTimeout);
        Assert.Equal(
            new DataServicesOptions().Sessions.ExpressionSession.IdleTimeout,
            expression.MacroInvocationTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveMacroInvocationTimeoutIsRejectedWithItsOwnPath(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.MacroInvocationTimeout = TimeSpan.FromSeconds(seconds);

        string failure = Assert.Single(Failures(options));

        // A zero or negative backstop abandons every macro invocation before the client could possibly
        // answer, turning the inverted channel into one that always times out - and it does so QUIETLY,
        // as a defined timeout outcome rather than as an error, which is why a validator has to catch it
        // instead of a caller discovering it.
        Assert.Contains("must be a positive duration", failure, StringComparison.Ordinal);
        Assert.Contains(
            "DataServices:ColumnExpression:MacroInvocationTimeout",
            failure,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroCalcStackCapacityIsRejectedByItsRangeAnnotation()
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.CalcStackInitialCapacity = 0;

        string failure = Assert.Single(Failures(options));

        Assert.Contains("CalcStackInitialCapacity", failure, StringComparison.Ordinal);
        Assert.Contains("DataServices:ColumnExpression:", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroRedrawSuppressionThresholdIsAcceptedBecauseZeroMeansNeverSuppress()
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.RedrawSuppressionRowThreshold = 0;

        // MEASURED, AND THE OPPOSITE OF THE NEIGHBOURING SETTING.
        //
        // `CalcStackInitialCapacity` rejects zero because a zero-capacity stack is meaningless. This
        // threshold ACCEPTS zero, because zero is a meaningful value: it is the "suppress redraw at every
        // row count, i.e. always" or "never" end of the scale rather than an absent value, so its range
        // starts at 0 while the capacity's starts at 1.
        //
        // Two adjacent int settings in the same group with different lower bounds is exactly the sort of
        // thing an assertion written from symmetry would get wrong - so both bounds are pinned.
        Assert.True(Succeeds(options));

        options.ColumnExpression.RedrawSuppressionRowThreshold = -1;
        Assert.Contains(
            "RedrawSuppressionRowThreshold",
            Assert.Single(Failures(options)),
            StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  THE VALIDATOR'S PATH PREFIXING
    // ==============================================================================================

    [Fact]
    public void AFailurePathIsPrefixedWithTheSectionNameWhenNoNamedInstanceIsGiven()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = "de";

        string failure = Assert.Single(Failures(options));

        // THE PATH IS THE CONFIGURATION KEY, NOT THE C# MEMBER PATH.
        //
        // An operator reading a startup failure needs the key they can edit.
        // "DataServices:Localization:Locale" is that key; "options.Localization.Locale" would not be.
        Assert.StartsWith("DataServices:Localization:Locale", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamedOptionsInstanceIsIdentifiedInTheFailurePath()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = "de";

        string failure = Assert.Single(Failures(options, name: "secondary"));

        // NAMED OPTIONS ARE DISAMBIGUATED, which matters the moment two configurations of the same type
        // are registered - otherwise two identical messages would name the same key and an operator
        // could not tell which instance failed.
        Assert.StartsWith("DataServices[secondary]:Localization:Locale", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyNameIsTreatedAsTheUnnamedDefaultRatherThanAsAnEmptyBracket()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = "de";

        // `Options.DefaultName` IS THE EMPTY STRING, which is why the check is IsNullOrEmpty rather than
        // a null check. Producing "DataServices[]:..." for the default instance would put noise in every
        // message in the common case.
        string failure = Assert.Single(Failures(options, name: Options.DefaultName));

        Assert.StartsWith("DataServices:Localization:Locale", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void TheValidatorParticipatesThroughTheFrameworkInterfaceSoStartupCanEnforceIt()
    {
        // IValidateOptions<T> IS WHAT LETS `ValidateOnStart` RUN THIS AND TERMINATE THE PROCESS.
        //
        // That is the fail-fast posture the legacy establishes with `HALT CLOSE` on a decoded assertion
        // failure [ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. A validator that were merely a public
        // method nobody called would leave the requirement unsatisfied while appearing satisfied.
        Assert.True(
            typeof(IValidateOptions<DataServicesOptions>)
                .IsAssignableFrom(typeof(DataServicesOptionsValidator)));
    }

    [Fact]
    public void MultipleIndependentFailuresAreAllReportedInOnePass()
    {
        DataServicesOptions options = ValidOptions();
        options.Localization.Locale = "de";
        options.RowSelect.Style = 0L;
        options.Persistence.Address = "ftp://persistence";
        options.Resilience.Security.CircuitBreakerFailureRatio = 0d;
        options.Sessions.ExpressionSession.IdleTimeout = TimeSpan.Zero;

        string[] failures = Failures(options);

        // ALL FIVE, IN ONE PASS - measured.
        //
        // Startup validation that stopped at the first failure would make an operator restart five times
        // to find five independent mistakes. Collecting them is what makes fail-fast usable rather than
        // merely strict.
        Assert.Equal(5, failures.Length);
        Assert.False(Succeeds(options));
    }

    [Fact]
    public void NoConfigurationSurfaceCarriesASigningSecret()
    {
        // C-F AND C-G SELF-AUDIT ACROSS THE WHOLE OPTIONS SURFACE.
        //
        // Security is the SOLE token issuer and exactly one signing secret exists in the system. A
        // signing-key property on DataServices would be a second signing authority, which the token
        // topology forbids: DataServices holds VERIFICATION material only and validates with the stock
        // bearer handler.
        Type[] surface =
        [
            typeof(DataServicesOptions), typeof(LocalizationOptions), typeof(RowSelectOptions),
            typeof(ContextMenuOptions), typeof(ColumnExpressionOptions), typeof(DropDownSearchOptions),
            typeof(PersistenceClientOptions), typeof(SecurityClientOptions), typeof(ResilienceOptions),
            typeof(ClientResilienceOptions), typeof(SessionsOptions), typeof(SessionLifetimeOptions),
            typeof(EventChainOptions), typeof(JwtAuthenticationOptions),
        ];

        foreach (Type type in surface)
        {
            foreach (var property in type.GetProperties())
            {
                // A BOOLEAN CANNOT HOLD KEY MATERIAL, AND EXCLUDING THEM IS NOT A LOOPHOLE.
                //
                // This exclusion exists because the naive name scan produced a real false positive:
                // `JwtAuthenticationOptions.ValidateIssuerSigningKey` contains "SigningKey" and is a
                // verification-side FLAG - it controls whether the inbound token's signature is checked
                // against the published key. That is the opposite of holding a signing key, and it is
                // exactly the setting that must stay true.
                //
                // Narrowing to value-bearing types keeps the scan meaningful: what would violate the
                // token topology is a STRING or byte array carrying material, not a bool deciding whether
                // to validate. Recorded rather than silently allowlisted, because the false positive is
                // informative - a name scan alone cannot tell a credential from a policy switch.
                if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
                {
                    continue;
                }

                // THE ONE CREDENTIAL-SHAPED MEMBER THAT LEGITIMATELY EXISTS IS PINNED BY NAME RATHER
                // THAN BY DROPPING THE MARKER.
                //
                // `SecurityClientOptions.ClientSecret` carries this service's own issuance credential -
                // the password half of the Basic credential it presents on POST /v1/tokens. It is
                // material, so the exclusion above cannot cover it; and it is emphatically NOT signing
                // material: it mints nothing, it signs nothing, it is not accepted by any other service,
                // and its only use is proving to Security which caller is asking. C-01 requires this
                // service to hold one, and C-G is what requires the edge to be authenticated at all - so
                // removing it would not tighten the topology, it would leave this service unable to
                // obtain any credential.
                //
                // Naming the single exception keeps the scan sharp: a SECOND credential-shaped member
                // appearing anywhere on this surface still fails this test, which is exactly what a
                // blanket relaxation of the marker list would have stopped detecting.
                bool isTheOneIssuanceCredential =
                    type == typeof(SecurityClientOptions)
                    && string.Equals(
                        property.Name,
                        nameof(SecurityClientOptions.ClientSecret),
                        StringComparison.Ordinal);

                if (isTheOneIssuanceCredential)
                {
                    continue;
                }

                foreach (string marker in (string[])
                    ["SigningKey", "PrivateKey", "Secret", "Password", "Credential"])
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} looks like signing material. DataServices "
                            + "validates tokens and never issues them; Security is the sole issuer.");
                }
            }
        }

        // AND THE EXCEPTION IS NOT A HOLE, BECAUSE IT IS ASSERTED TO EXIST. If the property were renamed
        // or removed the skip above would stop matching, so this line is what keeps the exception honest
        // rather than open-ended.
        Assert.NotNull(
            typeof(SecurityClientOptions).GetProperty(nameof(SecurityClientOptions.ClientSecret)));

        // AND THE PROPERTY THAT ONCE TRIPPED THE SCAN FOR THE WRONG REASON IS GONE. An earlier form of
        // JwtAuthenticationOptions declared ValidateIssuerSigningKey - a boolean switch, not material, but
        // a name a scanner cannot tell apart from one. It has been removed for a stronger reason than the
        // scan, recorded on the next test.
    }

    [Theory]
    [InlineData("ValidateIssuer")]
    [InlineData("ValidateAudience")]
    [InlineData("ValidateLifetime")]
    [InlineData("ValidateIssuerSigningKey")]
    [InlineData("ClockSkew")]
    [InlineData("MapInboundClaims")]
    public void NoSettingCanRelaxAValidation(string forbiddenMember)
    {
        // NO CONFIGURED VALUE CAN TURN A VERIFIER CHECK OFF, AND THERE ARE TWO WAYS FOR THAT TO BE TRUE.
        //
        // Four properties for issuer, audience, lifetime and signing-key validation once defaulted to true
        // and were READ by the handler, so the shape looked safe and was not: a settings file or one
        // environment variable could set any of them false, and each removes a different guarantee while
        // the service keeps reporting itself healthy. Issuer off accepts a token minted by anyone, which
        // ends the sole-issuer property the whole topology rests on. Audience off accepts a token minted
        // for a different service. Lifetime off accepts an expired token forever, turning a leaked
        // credential from a time-boxed exposure into a permanent one. Signing-key validation off accepts a
        // signature that was never checked, which is the whole of the verification. A test asserting
        // `true` BY DEFAULT would have passed against every one of those deployments, because the default
        // was never the problem.
        //
        // ⚠ THIS ROW ONCE ASSERTED ONLY THE FIRST OF THE TWO WAYS, AND THAT IS WHY IT IS WRITTEN LIKE THIS ⚠
        //
        // It required the member to be ABSENT. Absence does make the guarantee structural, but it has a
        // cost the delivered design deliberately refuses to pay: an unknown configuration key is SILENTLY
        // IGNORED by the binder, so a deployment writing `"ValidateIssuer": false` would start cleanly and
        // its operator would believe the check was off while it was on. The handler therefore assigns all
        // four LITERALLY - Program.cs section 5, "ALL FOUR ARE ASSIGNED LITERALLY, NOT READ" - and the
        // bound properties SURVIVE so that a configured false is REFUSED at startup, by name, with the
        // remedy stated. InvariantTokenValidationTests pins those refusals one switch at a time.
        //
        // So the rule this row enforces is the invariant rather than one implementation of it: for each
        // name, EITHER no such setting exists, OR it exists and no value of it can relax anything - which,
        // for a boolean switch, means the validator refuses `false`. Both discharge the finding; neither is
        // permitted to quietly become "the switch is read again".
        System.Reflection.PropertyInfo? property =
            typeof(JwtAuthenticationOptions).GetProperty(forbiddenMember);

        if (property is null)
        {
            // Structural: there is no key to write. ClockSkew and MapInboundClaims are this case - both are
            // compiled in at Program.cs section 5, an unbounded skew being lifetime validation switched off
            // under another name and a claim-name remapping switch changing which spelling every scope
            // check reads.
            return;
        }

        Assert.Equal(typeof(bool), property.PropertyType);

        JwtAuthenticationOptions relaxed = ValidJwt();
        property.SetValue(relaxed, false);

        ValidateOptionsResult refusal = new JwtAuthenticationOptionsValidator()
            .Validate(name: null, relaxed);

        Assert.True(
            refusal.Failed,
            $"JwtAuthenticationOptions.{forbiddenMember} is a settable switch that the validator accepts "
                + "as false. A deployment could then turn a mandatory verifier check off, or - if the "
                + "handler ignores it - believe it had.");

        Assert.Contains(
            refusal.Failures ?? [],
            failure => failure.Contains(forbiddenMember, StringComparison.Ordinal));
    }

    /// <summary>An inbound-token configuration that validates cleanly, for isolating one relaxation.</summary>
    /// <returns>The options.</returns>
    /// <remarks>
    /// The authority and audience are required, so a row that only turned a switch off would otherwise be
    /// unable to tell its own refusal from the two missing identifiers.
    /// </remarks>
    private static JwtAuthenticationOptions ValidJwt()
    {
        JwtAuthenticationOptions options = new()
        {
            Authority = "https://security-service:5104",
            Audience = "powerframework-dataservices",
        };

        // The permitted-caller roster is required and defaults to empty, so a default-constructed instance
        // does not validate at all; it is populated here so a row observes only the relaxation it names.
        options.PermittedCallers.Add("powerframework-gateway");

        return options;
    }

    [Fact]
    public void TheJwtSettingsThatRemainSelectWhomToTrustAndNeverWhetherToCheck()
    {
        var jwt = new JwtAuthenticationOptions();

        // WHAT IS LEFT IS THE DEPLOYMENT DECISION, AND ONLY THAT. Which authority to trust, which
        // audience this service answers to, where discovery lives, and whether that metadata must be
        // fetched over HTTPS. Each selects WHOM to trust; none skips a check. That line is the whole
        // distinction the removal above draws.
        Assert.True(jwt.RequireHttpsMetadata);

        // AND NO KEY MATERIAL IS EMBEDDED - the authority and audience are identifiers, and
        // `MetadataAddress` is an optional override for a topology where discovery lives elsewhere.
        Assert.Equal(string.Empty, jwt.Authority);
        Assert.Equal(string.Empty, jwt.Audience);
        Assert.Null(jwt.MetadataAddress);
    }

    // ==============================================================================================
    //  THE TOKEN-ISSUANCE EDGE'S CLIENT CREDENTIAL
    //
    //  This service is one of the two that request tokens, and POST /v1/tokens is the one operation a
    //  bearer token cannot protect - a caller cannot present a bearer token in order to obtain its first
    //  bearer token. Contract C-01 therefore publishes TWO schemes for it and accepts either: an HTTP
    //  Basic credential naming a subject on Security's issuance roster, or a client certificate. This
    //  service must be able to present ONE of them; presenting neither means it obtains no credential, so
    //  every one of the seventeen C-02 cryptographic calls and every call to the Persistence audience is
    //  unreachable - which is why that state is refused at startup rather than discovered on the first
    //  request.
    // ==============================================================================================

    [Fact]
    public void TheMutualTlsPairIsOptionalAsAGroupAndInseparableWhenPresent()
    {
        // BOTH EMPTY IS VALID AND IS THE DEFAULT: this deployment presents no client certificate, and
        // authenticates the issuance edge with the Basic credential instead - which is the documented
        // topology, because the frozen environment fixes Security on a cleartext listener where a client
        // certificate cannot exist at all. It still requests tokens; it just does not present a
        // certificate to obtain them.
        Assert.True(Succeeds(ValidOptions()));
        Assert.False(new DataServicesOptions().Security.MutualTls.IsConfigured);

        // BOTH SET IS VALID.
        DataServicesOptions both = ValidOptions();
        both.Security.MutualTls.CertificatePath = "/run/secrets/powerframework/dataservices.crt";
        both.Security.MutualTls.CertificateKeyPath = "/run/secrets/powerframework/dataservices.key";
        Assert.True(Succeeds(both));
        Assert.True(both.Security.MutualTls.IsConfigured);

        // EITHER ONE ALONE IS REFUSED, AT STARTUP RATHER THAN AT THE FIRST TOKEN REQUEST. A certificate
        // cannot complete a TLS handshake without its key, and a key has nothing to present without its
        // certificate, so half a client identity is unusable rather than merely weaker.
        foreach ((string certificate, string key) in ((string, string)[])
            [
                ("/run/secrets/powerframework/dataservices.crt", ""),
                ("", "/run/secrets/powerframework/dataservices.key"),
            ])
        {
            DataServicesOptions half = ValidOptions();
            half.Security.MutualTls.CertificatePath = certificate;
            half.Security.MutualTls.CertificateKeyPath = key;

            string failure = Assert.Single(Failures(half));

            Assert.Contains(
                "DataServices:Security:MutualTls:CertificatePath",
                failure,
                StringComparison.Ordinal);

            // NEITHER PATH IS ECHOED. A path is not itself a credential, but it names where one is
            // mounted, and a startup log is the wrong place to publish that.
            Assert.DoesNotContain("dataservices.crt", failure, StringComparison.Ordinal);
            Assert.DoesNotContain("dataservices.key", failure, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnIssuanceCredentialIsRequiredAndEitherSchemeSatisfiesIt()
    {
        // THE SECRET ALONE IS ENOUGH - and it is the documented topology's credential.
        DataServicesOptions secretOnly = ValidOptions();
        Assert.True(Succeeds(secretOnly));
        Assert.True(secretOnly.Security.HasIssuanceCredential);

        // THE CERTIFICATE PAIR ALONE IS ENOUGH TOO, which is what makes the two genuine alternatives
        // rather than one scheme with a fallback that never applies. A deployment that terminates TLS at
        // Security configures this and no secret.
        DataServicesOptions certificateOnly = ValidOptions();
        certificateOnly.Security.ClientSecret = string.Empty;
        certificateOnly.Security.MutualTls.CertificatePath = "/run/secrets/pf/dataservices.crt";
        certificateOnly.Security.MutualTls.CertificateKeyPath = "/run/secrets/pf/dataservices.key";
        Assert.True(Succeeds(certificateOnly));
        Assert.True(certificateOnly.Security.HasIssuanceCredential);

        // BOTH IS LEGAL AND IS NOT A CONFLICT. The Basic credential travels on the request and the
        // certificate is available to whatever handshake the transport performs, so a deployment moving
        // from one to the other can configure both across the transition.
        DataServicesOptions both = ValidOptions();
        both.Security.MutualTls.CertificatePath = "/run/secrets/pf/dataservices.crt";
        both.Security.MutualTls.CertificateKeyPath = "/run/secrets/pf/dataservices.key";
        Assert.True(Succeeds(both));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PresentingNeitherIssuanceCredentialIsRefusedAtStartup(string secret)
    {
        // THE RULE THAT MATTERS MOST IN THIS SECTION, AND THE ONE WHOSE ABSENCE WAS THE DEFECT. With no
        // credential of either kind this service starts, answers its anonymous health probe, reports
        // itself ready - and then fails every cryptographic call and every call to the audience beneath
        // it, because it can obtain no token at all. The failure would arrive as a 401 from Security with
        // nothing in it to say that this side never presented anything.
        //
        // WHITESPACE IS TREATED AS ABSENT, because a blank secret cannot authenticate and accepting one
        // would produce exactly that invisible 401.
        DataServicesOptions options = ValidOptions();
        options.Security.ClientSecret = secret;

        Assert.False(options.Security.HasIssuanceCredential);

        string failure = Assert.Single(Failures(options));

        // THE MESSAGE NAMES BOTH WAYS OUT, because an operator reading it must be able to tell which one
        // their topology calls for rather than guess.
        Assert.Contains(
            SecurityClientOptions.ClientSecretConfigurationKey,
            failure,
            StringComparison.Ordinal);
        Assert.Contains("DataServices:Security:MutualTls", failure, StringComparison.Ordinal);

        // AND IT STATES THE CONSEQUENCE, not just the missing key. An operator who reads only that a key
        // is unset has to work out for themselves that this service will start, report healthy, and then
        // fail every cryptographic call and every call to the audience beneath it. The message says so.
        Assert.Contains("obtain no service token", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void TheIssuanceSecretIsNamedByAFlatConfigurationKeyThatMatchesSecuritysRoster()
    {
        // ONE SPELLING, FIXED ON BOTH SIDES OF THE EDGE. Security's issuance roster names this same key
        // as the source of the secret it compares against for the subject this service presents, and
        // orchestration/.env.example declares it once for both. Two spellings would be two ways for one
        // deployment to be half configured, and the failure would present as an authentication refusal
        // rather than as the configuration mismatch it is.
        Assert.Equal(
            "SECURITY_CLIENT_SECRET_DATASERVICES",
            SecurityClientOptions.ClientSecretConfigurationKey);

        // FLAT, NOT SECTIONED, AND THAT IS WHY IT CANNOT BE BOUND. The environment-variable provider maps
        // a DOUBLE underscore onto the ':' separator; this name contains none, so it is a top-level key
        // rather than a path into `DataServices` and the composition root reads it with an explicit
        // post-configure step. A name containing '__' here would bind silently and this assertion is what
        // stops one being introduced.
        Assert.DoesNotContain(
            "__",
            SecurityClientOptions.ClientSecretConfigurationKey,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ":",
            SecurityClientOptions.ClientSecretConfigurationKey,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheMutualTlsGroupCarriesPathsAndHasNoMemberThatCouldHoldMaterial()
    {
        // THE ABSENCE IS THE CONTROL, exactly as it is on the keyed cryptographic surface where a caller
        // passes an opaque reference and never key bytes. There is no certificate body, no key body and
        // no passphrase member here, so C-F cannot be violated by filling one in - there is no such
        // member to fill.
        System.Reflection.PropertyInfo[] properties = typeof(MutualTlsClientOptions).GetProperties();

        foreach (var property in properties)
        {
            if (property.PropertyType == typeof(bool))
            {
                continue;
            }

            Assert.EndsWith("Path", property.Name, StringComparison.Ordinal);
            Assert.Equal(typeof(string), property.PropertyType);
        }

        foreach (string forbidden in (string[])
            ["Passphrase", "Password", "Pem", "Body", "Material", "Content", "SigningKey", "PrivateKey"])
        {
            Assert.DoesNotContain(
                properties,
                p => p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AMissingMutualTlsGroupIsReportedRatherThanIgnored()
    {
        DataServicesOptions options = ValidOptions();
        options.Security.MutualTls = null!;

        string failure = Assert.Single(Failures(options));

        Assert.Contains("DataServices:Security:MutualTls", failure, StringComparison.Ordinal);
    }
    // ==============================================================================================
    //  THE PAGE-RESOLUTION PAIR - NOT A LEGACY DEFAULT, AND THE ONLY COUPLED RULE IN THIS GROUP
    //  --------------------------------------------------------------------------------------------
    //  These two settings have no legacy counterpart: a PowerBuilder application owned its own band
    //  geometry, so `for page` asked the runtime which rows were on the page. Decomposition put that
    //  geometry in the deferred DesignSystem, so the headless engine has to be TOLD the pagination -
    //  and until it was, the deployed service answered the malformed sentinel for the only `for page`
    //  expression in the repository, dw_sqlite.srd:L27's `sum(salary for page)`.
    // ==============================================================================================

    /// <summary>
    /// The default pairing resolves <c>for page</c> and validates cleanly.
    /// </summary>
    /// <remarks>
    /// THE DEFAULT IS THE ONE THAT MATTERS, because a default-constructed group is exactly what binding
    /// an absent <c>DataServices:ColumnExpression</c> section produces - so this is what a deployment
    /// that says nothing gets. WholeBuffer is an explicit statement that the surface is unpaginated,
    /// evidenced by dw_sqlite.srd declaring no page-break band, rather than a guess made in the absence
    /// of evidence.
    /// </remarks>
    [Fact]
    public void ThePageResolutionDefaultStatesAnUnpaginatedSurfaceAndValidates()
    {
        ColumnExpressionOptions expression = new DataServicesOptions().ColumnExpression;

        Assert.Equal(ExpressionPageResolution.WholeBuffer, expression.PageResolution);
        Assert.Equal(0, expression.PageRowsPerPage);
        Assert.True(Succeeds(ValidOptions()));
    }

    /// <summary>
    /// All three declared modes are accepted, each with the row count its mode requires.
    /// </summary>
    /// <param name="resolution">The mode.</param>
    /// <param name="rowsPerPage">The row count to pair with it.</param>
    [Theory]
    [InlineData(ExpressionPageResolution.Unresolved, 0)]
    [InlineData(ExpressionPageResolution.WholeBuffer, 0)]
    [InlineData(ExpressionPageResolution.FixedRowsPerPage, 1)]
    [InlineData(ExpressionPageResolution.FixedRowsPerPage, 50)]
    public void EveryDeclaredPageResolutionIsAcceptedWithItsOwnRowCount(
        ExpressionPageResolution resolution,
        int rowsPerPage)
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.PageResolution = resolution;
        options.ColumnExpression.PageRowsPerPage = rowsPerPage;

        Assert.True(Succeeds(options));
    }

    /// <summary>
    /// An undeclared mode is rejected here rather than being left to the factory.
    /// </summary>
    /// <remarks>
    /// CONFIGURATION BINDING WILL HAPPILY PRODUCE AN UNDECLARED ENUM VALUE from a numeric string, and the
    /// factory's unrecognised arm throws for it - which is the right backstop and the wrong FIRST
    /// failure, because an operator would get an exception from a resolver instead of a named
    /// configuration path. Rejecting it here means a mistyped mode fails startup naming the setting.
    /// </remarks>
    [Fact]
    public void AnUndeclaredPageResolutionIsRejectedNamingTheSetting()
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.PageResolution = (ExpressionPageResolution)7;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:ColumnExpression:PageResolution",
            failure,
            StringComparison.Ordinal);

        // AND THE COUPLING RULE DOES NOT ALSO FIRE, so one fault produces one message. A second message
        // about a row count measured against a mode that is not a mode would read as two faults.
        Assert.DoesNotContain("PageRowsPerPage", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// A fixed-page mode with no usable row count is rejected.
    /// </summary>
    /// <param name="rowsPerPage">The unusable row count.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FixedRowsPerPageRequiresAPositiveRowCount(int rowsPerPage)
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.PageResolution = ExpressionPageResolution.FixedRowsPerPage;
        options.ColumnExpression.PageRowsPerPage = rowsPerPage;

        Assert.Contains(
            Failures(options),
            failure => failure.Contains(
                "DataServices:ColumnExpression:PageRowsPerPage",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// The exact strings <c>appsettings.json</c> carries bind to the members they name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN ENUM SETTING IS A STRING IN A FILE, AND THAT SEAM IS WORTH ONE TEST. Every other test in this
    /// region assigns the enum member directly, which proves the validator and the factory but says
    /// nothing about whether the word in the deployed configuration file reaches them - a typo, a renamed
    /// member or a binder that expected a number would all pass those tests and fail at startup.
    /// </para>
    /// <para>
    /// The three names asserted here are exactly the three the file's own comment offers an operator, and
    /// the numeric form is asserted alongside them because configuration binding accepts it and an
    /// operator may well write it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("WholeBuffer", ExpressionPageResolution.WholeBuffer)]
    [InlineData("Unresolved", ExpressionPageResolution.Unresolved)]
    [InlineData("FixedRowsPerPage", ExpressionPageResolution.FixedRowsPerPage)]
    [InlineData("1", ExpressionPageResolution.WholeBuffer)]
    public void ThePageResolutionNamesInTheConfigurationFileBindToTheirMembers(
        string configured,
        ExpressionPageResolution expected)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataServices:ColumnExpression:PageResolution"] = configured,
                ["DataServices:ColumnExpression:PageRowsPerPage"] = "2",
            })
            .Build();

        DataServicesOptions options = new();
        configuration.GetSection(DataServicesOptions.SectionName).Bind(options);

        Assert.Equal(expected, options.ColumnExpression.PageResolution);
        Assert.Equal(2, options.ColumnExpression.PageRowsPerPage);
    }

    /// <summary>
    /// A stated row count is REFUSED rather than ignored when the mode would not apply it.
    /// </summary>
    /// <param name="resolution">A mode that does not read the row count.</param>
    /// <remarks>
    /// THE DIRECTION THAT CATCHES A REAL MISTAKE. An operator who wrote a page size and left the mode
    /// alone has stated a pagination that would not be applied, and passing over it silently is how a
    /// page total becomes a grand total with nothing in the configuration to show why. Both non-fixed
    /// modes are covered, because the rule is about the mode not reading the value rather than about
    /// which mode it is.
    /// </remarks>
    [Theory]
    [InlineData(ExpressionPageResolution.WholeBuffer)]
    [InlineData(ExpressionPageResolution.Unresolved)]
    public void AStatedRowCountIsRefusedWhenTheModeWouldNotApplyIt(ExpressionPageResolution resolution)
    {
        DataServicesOptions options = ValidOptions();
        options.ColumnExpression.PageResolution = resolution;
        options.ColumnExpression.PageRowsPerPage = 50;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:ColumnExpression:PageRowsPerPage",
            failure,
            StringComparison.Ordinal);
        Assert.Contains("FixedRowsPerPage", failure, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  THE PERSISTENCE SESSION DESCRIPTOR, and the two groups whose annotations were never walked
    // ==============================================================================================

    /// <summary>
    /// The shipped configuration keys bind to the descriptor members they name.
    /// </summary>
    /// <remarks>
    /// THE SEAM IS THE KEY SPELLING, NOT THE PROPERTY. Every other test here assigns the members directly,
    /// which says nothing about whether the words in the deployed file reach them. Two of these matter more
    /// than the rest: <c>NCharBind</c> has an unusual capitalization that a binder must match, and
    /// <c>LogPass</c> is the credential an operator overrides from the secret layer as
    /// <c>DataServices__PersistenceSession__LogPass</c> - if that key did not bind, the override would be
    /// accepted silently and the session would open without it.
    /// </remarks>
    [Fact]
    public void ThePersistenceSessionKeysBindToTheDescriptorMembers()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataServices:PersistenceSession:Dbms"] = "SQLite",
                ["DataServices:PersistenceSession:ServerName"] = "host",
                ["DataServices:PersistenceSession:Database"] = "powerframework",
                ["DataServices:PersistenceSession:LogId"] = "operator",
                ["DataServices:PersistenceSession:LogPass"] = "from-the-secret-layer",
                ["DataServices:PersistenceSession:DbParm"] = "DisableBind=1",
                ["DataServices:PersistenceSession:Lock"] = "RU",
                ["DataServices:PersistenceSession:AutoCommit"] = "true",
                ["DataServices:PersistenceSession:UserParm"] = "u",
                ["DataServices:PersistenceSession:DisableBind"] = "true",
                ["DataServices:PersistenceSession:NCharBind"] = "true",
            })
            .Build();

        DataServicesOptions options = new();
        configuration.GetSection(DataServicesOptions.SectionName).Bind(options);

        PersistenceSessionOptions session = options.PersistenceSession;

        Assert.Equal("SQLite", session.Dbms);
        Assert.Equal("host", session.ServerName);
        Assert.Equal("powerframework", session.Database);
        Assert.Equal("operator", session.LogId);
        Assert.Equal("from-the-secret-layer", session.LogPass);
        Assert.Equal("DisableBind=1", session.DbParm);
        Assert.Equal("RU", session.Lock);
        Assert.True(session.AutoCommit);
        Assert.Equal("u", session.UserParm);
        Assert.True(session.DisableBind);
        Assert.True(session.NCharBind);
    }

    /// <summary>
    /// The descriptor's defaults are the safe arm of every choice the legacy leaves open.
    /// </summary>
    /// <remarks>
    /// ASSERTED AS DEFAULTS BECAUSE A DEPLOYMENT THAT SETS NOTHING GETS THEM. <c>DisableBind</c> false
    /// keeps the runtime binding parameters rather than interpolating literals - the mechanical root of the
    /// legacy injection exposure - and <c>AutoCommit</c> false is the preserved legacy posture that keeps a
    /// partially applied multi-row update recoverable. An empty password is correct for the only evidenced
    /// engine rather than a placeholder.
    /// </remarks>
    [Fact]
    public void ThePersistenceSessionDefaultsAreTheSafeArmOfEachChoice()
    {
        PersistenceSessionOptions session = new();

        Assert.Equal("SQLite", session.Dbms);
        Assert.False(session.DisableBind);
        Assert.False(session.NCharBind);
        Assert.False(session.AutoCommit);
        Assert.Equal(string.Empty, session.LogPass);
    }

    /// <summary>
    /// A blank DBMS is refused at startup rather than defaulted at runtime.
    /// </summary>
    /// <remarks>
    /// THE FALLBACK IS WHY THIS IS FAIL-FAST. Persistence substring-tests the value and falls back to SQL
    /// Server when the test does not match [<c>n_cst_thread_trans.sru:L356-L362</c>], so a blank value does
    /// not fail there - it silently SELECTS a dialect. Starting and then generating statements for the
    /// wrong dialect is exactly the graceful degradation the ported fail-fast posture forbids.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPersistenceSessionDbmsIsRefused(string dbms)
    {
        DataServicesOptions options = ValidOptions();
        options.PersistenceSession.Dbms = dbms;

        string failure = Assert.Single(Failures(options));

        Assert.Contains("DataServices:PersistenceSession:Dbms", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The streamed-element bound is enforced, not merely annotated.
    /// </summary>
    /// <remarks>
    /// THE ANNOTATION ALONE DOES NOTHING. A range attribute is only applied where the validator walks the
    /// group, and this group was not walked - so a zero would have bound silently and then refused EVERY
    /// streamed response at its first element, which reads as an upstream fault rather than a configuration
    /// mistake.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveStreamedElementBoundIsRefused(int bound)
    {
        DataServicesOptions options = ValidOptions();
        options.RestProjection.MaxStreamedElements = bound;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:RestProjection:MaxStreamedElements",
            failure,
            StringComparison.Ordinal);
    }
}
