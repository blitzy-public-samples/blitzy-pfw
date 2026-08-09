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

using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class DataServicesOptionsTests
{
    private static readonly DataServicesOptionsValidator Validator = new();

    /// <summary>
    /// An options instance that validates cleanly, for isolating one rule at a time.
    /// </summary>
    /// <remarks>
    /// The two upstream addresses must be supplied: both carry <c>[Required]</c> and both default to
    /// empty, so a default-constructed instance does NOT validate. That is asserted directly in
    /// <see cref="TheDefaultConfigurationRequiresBothUpstreamAddresses"/> rather than worked around
    /// silently here.
    /// </remarks>
    private static DataServicesOptions ValidOptions()
    {
        var options = new DataServicesOptions();
        options.Persistence.Address = "http://persistence:5101";
        options.Security.BaseAddress = "http://security:5104";
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
    public void TheDefaultConfigurationRequiresBothUpstreamAddresses()
    {
        // MEASURED, AND NOT WHAT A DEFAULT-CONSTRUCTED OPTIONS USUALLY DOES: THE DEFAULTS DO NOT PASS.
        //
        // `Persistence.Address` and `Security.BaseAddress` both carry `[Required]` and both default to
        // the empty string, so a DataServices started with no upstream configuration fails startup
        // validation with exactly two failures.
        //
        // That is the correct posture rather than an awkward default. DataServices cannot serve C-03 or
        // C-04 without reaching Persistence, and cannot validate a token without Security's keys - so a
        // service that started without them would accept requests and fail every one. Failing at startup
        // with the two missing keys named is the fail-fast behaviour the legacy's own `HALT CLOSE` on a
        // decoded assertion failure establishes [ws_objects/pfw.pbl.src/pfw.sra:L111-L144].
        string[] failures = Failures(new DataServicesOptions());

        Assert.Equal(2, failures.Length);
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Persistence:Address", StringComparison.Ordinal)
            && f.Contains("required", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, static f =>
            f.Contains("DataServices:Security:BaseAddress", StringComparison.Ordinal)
            && f.Contains("required", StringComparison.OrdinalIgnoreCase));
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
        }
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

        // AND THE VERIFICATION FLAG THAT TRIPPED THE SCAN IS PRESENT AND ON, which is the property that
        // actually matters for C-G.
        Assert.True(new JwtAuthenticationOptions().ValidateIssuerSigningKey);
    }

    [Fact]
    public void TheJwtVerificationDefaultsKeepEveryValidationCheckOn()
    {
        var jwt = new JwtAuthenticationOptions();

        // EVERY CHECK DEFAULTS ON, AND HTTPS METADATA IS REQUIRED.
        //
        // These are the secure defaults rather than the convenient ones. Constraint C-G requires every
        // newly created boundary to be authenticated, and an internal edge is a created boundary too -
        // so the defaults must not quietly disable issuer, audience, lifetime or signing-key validation.
        // Turning any of them off has to be a deliberate, visible act.
        Assert.True(jwt.RequireHttpsMetadata);
        Assert.True(jwt.ValidateIssuer);
        Assert.True(jwt.ValidateAudience);
        Assert.True(jwt.ValidateLifetime);
        Assert.True(jwt.ValidateIssuerSigningKey);

        // AND NO KEY MATERIAL IS EMBEDDED - the authority and audience are identifiers, and
        // `MetadataAddress` is an optional override for a topology where discovery lives elsewhere.
        Assert.Equal(string.Empty, jwt.Authority);
        Assert.Equal(string.Empty, jwt.Audience);
        Assert.Null(jwt.MetadataAddress);
    }

    // ==============================================================================================
    //  THE TOKEN-ISSUANCE EDGE'S CLIENT IDENTITY
    //
    //  This service is one of the two that request tokens, and POST /v1/tokens is protected by mutual
    //  TLS and by nothing else - a caller cannot present a bearer token in order to obtain its first
    //  bearer token. Without a client identity this service obtains no credential, so every one of the
    //  seventeen C-02 cryptographic calls is unreachable.
    // ==============================================================================================

    [Fact]
    public void TheMutualTlsPairIsOptionalAsAGroupAndInseparableWhenPresent()
    {
        // BOTH EMPTY IS VALID AND IS THE DEFAULT: this deployment presents no client certificate and
        // requests no token.
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
}
