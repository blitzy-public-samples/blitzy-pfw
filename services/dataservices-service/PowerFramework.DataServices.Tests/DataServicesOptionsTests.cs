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

using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PowerFramework.DataServices.Configuration;
using PowerFramework.DataServices.Expressions;
using PowerFramework.DataServices.Services;
using PowerFramework.Shared.Kernel;
using PowerFramework.Shared.Localization;
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
    public void TheEventChainStrictnessDialAcceptsItsWholeDomain()
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

        // THE PENDING-NOTIFICATION CEILING DEFAULTS TO ITS PUBLISHED CONSTANT, not to a literal restated
        // here. It is the server's own bound on the synchronous discipline: a conforming client holds
        // exactly ONE notification queued-or-in-flight, because it must read a response to learn its next
        // token, so the shipped value is headroom rather than a working limit. Asserting the constant means
        // this row follows the value if it ever moves.
        Assert.Equal(
            EventChainOptions.DefaultMaxPendingNotifications,
            options.EventChain.MaxPendingNotifications);
    }

    /// <summary>
    /// The pending-notification ceiling is enforced, not merely annotated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ANNOTATION ALONE DOES NOTHING. A range attribute is only applied where the validator walks the
    /// group, so a validator that walks this group ONLY for its duration lets a zero bind
    /// silently and then refuse the FIRST notification of every event chain, which a caller reads as a
    /// client fault while being a configuration mistake. A negative value is checked with it because that
    /// is what an operator writes when they mean "no limit", and it is precisely the value that must not be
    /// taken to mean that.
    /// </para>
    /// <para>
    /// 🔴 <b>AND 1 IS REFUSED WITH THEM, WHICH IS THE ROW WORTH HAVING.</b> A slot is released when the
    /// dispatch holding it finishes - immediately AFTER its result has been written - so there is an
    /// instant in which the client has already read the response and may legitimately send the next
    /// notification while the previous slot is still counted. A ceiling of 1 would therefore refuse a
    /// CONFORMING client intermittently, and an intermittent refusal is far worse than a value being too
    /// small because it fails only under timing. Two is the least that can never do that.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void APendingNotificationCeilingBelowTwoIsRefused(int ceiling)
    {
        DataServicesOptions options = ValidOptions();
        options.EventChain.MaxPendingNotifications = ceiling;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:EventChain:MaxPendingNotifications",
            failure,
            StringComparison.Ordinal);
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

    /// <summary>
    /// The retry count is accepted across its whole documented domain and refused outside it, on both
    /// edges, with the offending path and the ceiling named.
    /// </summary>
    /// <param name="attempts">The configured count.</param>
    /// <param name="accepted">Whether the validator must accept it.</param>
    /// <remarks>
    /// <para>
    /// 🔴 <b>BOTH ENDS OF THIS DOMAIN WERE DEFECTIVE, IN OPPOSITE DIRECTIONS.</b> Zero was documented as
    /// disabling retrying and annotated as legal while both retry layers consumed it unconditionally, and
    /// neither accepts it: the host FAILED TO START. The upper end was <see cref="int.MaxValue"/>, which
    /// the gRPC layer increments into an attempt count - unchecked, so it wrapped to
    /// <see cref="int.MinValue"/>, a negative count the channel and its retry policy both ACCEPTED,
    /// leaving retry mis-configured on a service that started and reported itself healthy.
    /// </para>
    /// <para>
    /// THE CEILING IS ASSERTED THROUGH ITS OWN CONSTANT RATHER THAN AS A LITERAL, so the annotation, the
    /// documentation and this theory cannot drift apart: a later change to the bound moves all three rows
    /// at once, and a change to the annotation alone fails here.
    /// </para>
    /// <para>
    /// BOTH EDGES ARE EXERCISED PER ROW because the two groups are tuned independently and share a type -
    /// a rule enforced on one path only would leave the other silently unbounded, which is the same
    /// asymmetry the surrounding stream-deadline tests exist to refuse.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(ClientResilienceOptions.MaxRetryAttemptsCeiling, true)]
    [InlineData(ClientResilienceOptions.MaxRetryAttemptsCeiling + 1, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void TheRetryCountIsAcceptedAcrossItsDocumentedDomainAndRefusedOutsideIt(
        int attempts,
        bool accepted)
    {
        foreach (string edge in (string[])["Persistence", "Security"])
        {
            DataServicesOptions options = ValidOptions();

            ClientResilienceOptions client = string.Equals(edge, "Persistence", StringComparison.Ordinal)
                ? options.Resilience.Persistence
                : options.Resilience.Security;

            client.MaxRetryAttempts = attempts;

            string[] failures = Failures(options);

            if (accepted)
            {
                Assert.Empty(failures);
                continue;
            }

            string failure = Assert.Single(failures);

            Assert.Contains(
                $"DataServices:Resilience:{edge}:{nameof(ClientResilienceOptions.MaxRetryAttempts)}",
                failure,
                StringComparison.Ordinal);

            // THE BOUND ITSELF IS IN THE MESSAGE, because a refusal that named only the key would tell an
            // operator that the value is wrong and not what would be right.
            Assert.Contains(
                ClientResilienceOptions.MaxRetryAttemptsCeiling.ToString(CultureInfo.InvariantCulture),
                failure,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The retry count answers whether retrying is enabled, and refuses to overflow into an attempt count.
    /// </summary>
    /// <remarks>
    /// <b>THE GUARD BEHIND THE GUARD.</b> The range annotation makes <see cref="int.MaxValue"/>
    /// unreachable through configuration, so this asserts what happens if it ever became reachable
    /// again - the conversion THROWS rather than silently producing the negative attempt count that both
    /// gRPC layers accepted. A validator can be edited; <c>checked</c> arithmetic cannot be edited by
    /// accident.
    /// </remarks>
    [Fact]
    public void TheRetryCountAnswersWhetherRetryingIsEnabledAndTheInclusiveAttemptCount()
    {
        ClientResilienceOptions client = new();

        // The shipped default: three retries, four attempts.
        Assert.True(client.RetriesEnabled);
        Assert.Equal(4, client.ResolveGrpcAttemptCount());

        client.MaxRetryAttempts = 0;
        Assert.False(client.RetriesEnabled);

        // One attempt, which is why the composition root installs NO configuration instead: the gRPC
        // retry policy refuses an attempt count of one.
        Assert.Equal(1, client.ResolveGrpcAttemptCount());

        client.MaxRetryAttempts = ClientResilienceOptions.MaxRetryAttemptsCeiling;
        Assert.True(client.RetriesEnabled);
        Assert.Equal(
            ClientResilienceOptions.MaxRetryAttemptsCeiling + 1,
            client.ResolveGrpcAttemptCount());

        client.MaxRetryAttempts = int.MaxValue;
        _ = Assert.Throws<OverflowException>(() => client.ResolveGrpcAttemptCount());
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

        // AND THE PROPERTY MOST LIKELY TO TRIP THIS SCAN FOR THE WRONG REASON IS ABSENT. A
        // JwtAuthenticationOptions carrying ValidateIssuerSigningKey declares a boolean switch, not
        // material - but a name a scanner cannot tell apart from one. It is absent for a stronger reason
        // than the scan, recorded on the next test.
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
        // ⚠ ASSERTING ONLY THE FIRST OF THE TWO WAYS IS THE TEMPTING FORM, AND THIS IS WHY IT IS NOT USED ⚠
        //
        // That form requires the member to be ABSENT. Absence does make the guarantee structural, but it has
        // a cost the delivered design deliberately refuses to pay: an unknown configuration key is SILENTLY
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
    //  every one of the eighteen C-02 cryptographic calls and every call to the Persistence audience is
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
    /// <para>
    /// THE SEAM IS THE KEY SPELLING, NOT THE PROPERTY. Every other test here assigns the members directly,
    /// which says nothing about whether the words in the deployed file reach them. <c>LogPass</c> matters
    /// more than the rest: it is the credential an operator overrides from the secret layer as
    /// <c>DataServices__PersistenceSession__LogPass</c> - if that key did not bind, the override would be
    /// accepted silently and the session would open without it.
    /// </para>
    /// <para>
    /// ⚠ TWO KEYS ARE DELIBERATELY ABSENT FROM THIS ROW. Setting
    /// <c>DisableBind</c> and <c>NCharBind</c> alongside <c>DbParm</c> is precisely the shape that
    /// breaks every session: the flags are DERIVED from the connection-parameter string, and
    /// <c>"DisableBind=1"</c> resolves under the oracle's nested reading to <c>nchar_bind=false</c>, so
    /// two such settings CONTRADICT the string they accompany. There is exactly one input,
    /// and a leftover key in a deployed settings file binds to nothing and is silently ignored by the
    /// configuration binder - which is the harmless outcome, and is why the properties are ABSENT rather
    /// than validated. What guards against their return is the reflection row in
    /// <c>DataWindowServiceContractTests</c> that asserts neither property exists.
    /// </para>
    /// <para>
    /// ⚠ <c>AutoCommit</c> IS SET TRUE HERE ONLY TO PROVE THE KEY BINDS, AND IT IS NOT A DEPLOYABLE
    /// VALUE. Binding and validation are separate steps, and the validator refuses true at startup -
    /// see <c>APersistenceSessionAutoCommitOfTrueIsRefused</c> for why.
    /// </para>
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
    }

    /// <summary>
    /// The descriptor's defaults are the safe arm of every choice the legacy leaves open.
    /// </summary>
    /// <remarks>
    /// ASSERTED AS DEFAULTS BECAUSE A DEPLOYMENT THAT SETS NOTHING GETS THEM. An EMPTY
    /// connection-parameter string matches neither of the oracle's two flag patterns
    /// [<c>n_cst_thread_task_sqlbase.sru:L128-L129</c>], so it keeps the runtime BINDING parameters rather
    /// than interpolating literals - the mechanical root of the legacy injection exposure - and that safe
    /// arm is reached from the one input rather than from a second setting that could contradict it.
    /// <c>AutoCommit</c> false is the preserved legacy posture that keeps a partially applied multi-row
    /// update recoverable. An empty password is correct for the only evidenced engine rather than a
    /// placeholder.
    /// </remarks>
    [Fact]
    public void ThePersistenceSessionDefaultsAreTheSafeArmOfEachChoice()
    {
        PersistenceSessionOptions session = new();

        Assert.Equal("SQLite", session.Dbms);
        Assert.Equal(string.Empty, session.DbParm);
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
    /// A session descriptor that asks for connection-level autocommit is refused at startup, because
    /// C-08 refuses it on every request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE REFUSAL IS DOWNSTREAM AND TOTAL, WHICH IS WHY IT BELONGS AT STARTUP. The legacy erases the
    /// descriptor's autocommit member before it reaches either the connection pool or a transaction object
    /// [<c>n_cst_thread_task_sqlbase.sru:L118-L119</c>, <c>n_cst_thread_trans.sru:L343-L354</c>], so
    /// Persistence answers <c>E_INVALID_ARGUMENT</c> to a <c>BeginSession</c> that sets it rather than
    /// accepting it and quietly discarding it. This service opens a session for EVERY retrieval, update and
    /// expression host, so the setting does not degrade one operation - it removes all of them. Binding it
    /// and then failing every request is the graceful degradation the ported fail-fast posture forbids.
    /// </para>
    /// <para>
    /// THE MESSAGE HAS TO NAME THE ROUTE THAT WORKS, or the operator's next move is to conclude that
    /// autocommit is unavailable. It is available twice over, just not from the connection descriptor:
    /// C-06's task-level switch (which this service already sets, so a single-call update commits before
    /// its session ends) and C-08's <c>SetAutoCommit</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void APersistenceSessionAutoCommitOfTrueIsRefused()
    {
        DataServicesOptions options = ValidOptions();
        options.PersistenceSession.AutoCommit = true;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:PersistenceSession:AutoCommit",
            failure,
            StringComparison.Ordinal);
        Assert.Contains("must be false", failure, StringComparison.Ordinal);
        Assert.Contains("SetAutoCommit", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped settings file states the descriptor's autocommit member as false.
    /// </summary>
    /// <remarks>
    /// ASSERTED SEPARATELY FROM THE DEFAULT because the default only holds for a key nobody wrote, and the
    /// deployed document is what actually starts the service. The file states the member rather than
    /// omitting it, which is right for a descriptor that mirrors a legacy structure field by field - the
    /// row is visible where an operator reads the connection settings, next to the password and the
    /// connection-parameter string that govern the same session. What must never appear there is
    /// <c>true</c>: it would refuse every session this service opens, and it would do so on a document the
    /// validator only sees after an operator has deployed it.
    /// </remarks>
    [Fact]
    public void TheShippedSettingsFileStatesThePersistenceSessionAutoCommitMemberAsFalse()
    {
        IConfigurationRoot configuration = DataServicesSettingsDocuments.Base();

        Assert.Equal(
            "False",
            configuration["DataServices:PersistenceSession:AutoCommit"],
            ignoreCase: true);

        Assert.False(DataServicesSettingsDocuments.BindService(configuration)
            .PersistenceSession
            .AutoCommit);

        Assert.Null(
            DataServicesSettingsDocuments.DevelopmentOnly()[
                "DataServices:PersistenceSession:AutoCommit"]);
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

    /// <summary>
    /// The event chain's admission ceiling defaults to a value a conforming client cannot reach, and is
    /// enforced rather than merely annotated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DEFAULT IS THE HALF THAT MATTERS. An UNBOUNDED chain queue with its write outcome
    /// discarded hands whoever opens a stream an unbounded memory commitment: nine of the 22 events
    /// are questions the dispatch BLOCKS on, so a client that keeps sending while one is outstanding grows
    /// the queue for as long as it cares to and nothing in the process objects until it runs out of memory.
    /// A ceiling only closes that if it ships switched on, so the default is a real number rather than a
    /// sentinel meaning "no limit".
    /// </para>
    /// <para>
    /// AND SIXTY-FOUR IS NOT A GUESS. Under the strictly synchronous discipline of AAP 0.6.1.4 a conforming
    /// client must read a response to learn its next token, so its depth in flight is ONE; anything above
    /// that is a client not following the discipline. The value is generous enough that a burst of
    /// notifications a well-behaved client sends between two questions is still admitted, and small enough
    /// that the commitment is a fixed, statable quantity per stream instead of an open one.
    /// </para>
    /// <para>
    /// THE ANNOTATION ALONE WOULD DO NOTHING, exactly as for the streamed-element bound above: a range
    /// attribute is applied only where the validator walks the group. Left unwalked, a zero would bind
    /// silently and then refuse EVERY notification on EVERY stream - the whole event chain unusable, and
    /// reported to each client as though it had misbehaved.
    /// </para>
    /// </remarks>
    /// <param name="ceiling">The configured ceiling.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveEventChainAdmissionCeilingIsRefused(int ceiling)
    {
        Assert.Equal(64, new EventChainOptions().MaxPendingNotifications);

        DataServicesOptions options = ValidOptions();
        options.EventChain.MaxPendingNotifications = ceiling;

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:EventChain:MaxPendingNotifications",
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A collection window that collects nothing is refused.
    /// </summary>
    /// <param name="seconds">The interval, in seconds.</param>
    /// <remarks>
    /// 🔴 <b>ZERO ANSWERS EVERY POLL EMPTY, INCLUDING ONE WITH RECORDS ALREADY WAITING, AND THAT IS SILENT
    /// DATA LOSS RATHER THAN A VISIBLE MISCONFIGURATION.</b> The projected event stream's empty collection
    /// is a legitimate, successful answer meaning "nothing was emitted during the window" - so a window of
    /// zero makes every poll answer 200 with an empty array forever, which no caller can distinguish from a
    /// quiet subscription. Refusing at startup is the only place the mistake is visible.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveStreamCollectionWindowIsRefused(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.RestProjection.StreamCollectionWindow = TimeSpan.FromSeconds(seconds);

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:RestProjection:StreamCollectionWindow",
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A collection window that cannot fire before its consumer gives up is refused.
    /// </summary>
    /// <param name="seconds">The window a deployment configured, in seconds.</param>
    /// <remarks>
    /// <para>
    /// <b>THE BOUND IS A RELATIONSHIP, WHICH IS WHY IT CANNOT BE AN ANNOTATION AND WHY IT IS ASSERTED
    /// HERE.</b> A window at or beyond the per-attempt budget this projection's documented consumer applies
    /// to it cannot fire first: the consumer abandons the attempt while this service is still collecting,
    /// and the caller receives a transport failure instead of the empty collection the operation means -
    /// which is the very defect the window exists to close, moved one hop out rather than fixed.
    /// </para>
    /// <para>
    /// THE AT-THE-BOUND ROW IS THE ONE THAT MATTERS. An exclusive bound is the correct shape - a window
    /// exactly equal to the consumer's budget is a race, and a race that resolves the wrong way produces
    /// exactly the failure the window was added to prevent - so equality is refused as firmly as excess.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(600)]
    public void AStreamCollectionWindowAtOrBeyondTheConsumerBudgetIsRefused(int seconds)
    {
        DataServicesOptions options = ValidOptions();
        options.RestProjection.StreamCollectionWindow = TimeSpan.FromSeconds(seconds);

        string failure = Assert.Single(Failures(options));

        Assert.Contains(
            "DataServices:RestProjection:StreamCollectionWindow",
            failure,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A window inside the bound is accepted, including the shipped default and both extremes of the range.
    /// </summary>
    /// <param name="window">A legal window, as a TimeSpan literal.</param>
    /// <remarks>
    /// THE COMPLEMENT THAT KEEPS THE TWO REFUSAL ROWS FROM PASSING VACUOUSLY. A validator that refused every
    /// window would satisfy both of them; this one proves the legal range is genuinely open, and it includes
    /// the shipped default so the settings file cannot ship a value its own validator rejects.
    /// </remarks>
    [Theory]
    [InlineData("00:00:00.001")]
    [InlineData("00:00:02")]
    [InlineData("00:00:09.999")]
    public void AStreamCollectionWindowInsideTheBoundIsAccepted(string window)
    {
        DataServicesOptions options = ValidOptions();
        options.RestProjection.StreamCollectionWindow = TimeSpan.Parse(
            window,
            CultureInfo.InvariantCulture);

        Assert.Empty(Failures(options));
    }
}

// ==================================================================================================
//  PART TWO - THE TWO SETTINGS DOCUMENTS, WHICH ARE THE DECLARED SOURCE OF EVERY KEY
//  ------------------------------------------------------------------------------------------------
//  Everything above this line tests the options TYPES and their validator in isolation: construct an
//  instance, set a property, ask the validator. That is necessary and it is not sufficient, because a
//  deployed service never constructs an options instance by hand. It BINDS one, from
//  `appsettings.json` and - when the environment name selects it - from `appsettings.Development.json`
//  layered on top. Those two documents are therefore part of the unit under test rather than context
//  around it, and until this point NOTHING in this project read either of them.
//
//  WHY THAT MATTERS RATHER THAN BEING MERELY UNTIDY. A compiled default and a declared default are two
//  independent statements of the same behaviour, and nothing in the language makes them agree. Every
//  preserved legacy default is declared TWICE - once as a property initializer in
//  `Configuration/DataServicesOptions.cs` and once as a key in `appsettings.json` - so a change to
//  either alone silently wins over the other at run time, with no compiler diagnostic and no failing
//  test above. `DataServices:RowSelect:Style` reset to 2 in the document would leave every assertion in
//  Part One passing while the deployed service selected rows the way the legacy never did. Under
//  constraint C-B that is a behavioural regression, and it is exactly the class of regression that has
//  no compile-time signal.
//
//  A SECOND FAILURE MODE THIS PART CLOSES, AND IT IS THE QUIETER OF THE TWO. The configuration binder
//  IGNORES a key it cannot map. A path misspelled in the document - a group renamed, a leaf pluralised
//  - binds nothing at all and reports nothing at all: the service starts, validates cleanly, and runs
//  on the compiled default while its operator reads the document and believes otherwise.
//  `EveryDeclaredKeyUnderTheServiceSectionResolvesToARealMember` is the assertion that turns that
//  silence into a failure.
//
//  THE THIRD IS THE ENVIRONMENT OVERLAY. `appsettings.Development.json` may legitimately move an
//  address, a log level or a developer-ergonomic window. It may NOT redeclare a preserved legacy
//  default, because a developer's run would then exhibit behaviour a deployed instance does not - and
//  the characterization comparison the parity model rests on would be silently invalidated: a recording
//  captured against the overlay would be compared against an oracle that never saw it (AAP 0.6.7).
//  Absence in the overlay is asserted key by key rather than assumed.
//
// ==================================================================================================

/// <summary>
/// The two settings documents this service ships, read from the source tree they are committed in.
/// </summary>
/// <remarks>
/// <para>
/// READ FROM THE CHECKOUT RATHER THAN FROM THE BUILD OUTPUT, DELIBERATELY. What is asserted here is a
/// property of the COMMITTED artifact - the file an operator opens and a reviewer reads - so a copy in
/// an output directory would be one transformation removed from the subject. The locator is the walk
/// every other on-disk locator in this project uses, anchored on
/// <see cref="TestRepositoryRoot.SearchStart"/> so it also works when the test output sits outside the
/// tree under <c>dotnet test --artifacts-path</c>.
/// </para>
/// <para>
/// BOTH DOCUMENTS CARRY <c>//</c> COMMENTS, AND LOADING THEM IS NOT A LIBERTY. The JSON configuration
/// provider parses with comment handling set to skip and trailing commas allowed, so the commented
/// form these files use is the form the deployed host itself reads. Loading them through
/// <see cref="ConfigurationBuilder"/> here is therefore the production parse rather than an
/// approximation of it - which is the only reason a document assertion is worth anything.
/// </para>
/// <para>
/// CONSTRAINT C-I IS NOT WEAKENED BY READING FROM THE TREE, AND THE REASON IS WORTH STATING BECAUSE THE
/// WALK CLIMBS TO THE REPOSITORY ROOT. The root is used only as an ANCHOR: the resolved path descends
/// straight back into THIS service's own project directory and nothing else. No sibling service's
/// project, solution, settings or test asset is referenced from here, so
/// <c>cd services/dataservices-service &amp;&amp; dotnet restore &amp;&amp; dotnet build &amp;&amp; dotnet test</c>
/// still works from a clean checkout exactly as constraints C-A and C-I require - and it is verified to,
/// because that is the command this suite is run with.
/// </para>
/// </remarks>
internal static class DataServicesSettingsDocuments
{
    /// <summary>The file that identifies the repository root.</summary>
    private const string RepositoryRootMarker = "PowerFramework.slnx";

    /// <summary>The base settings document, loaded in every environment.</summary>
    internal const string BaseFileName = "appsettings.json";

    /// <summary>The overlay loaded when the environment name is <c>Development</c>.</summary>
    internal const string DevelopmentFileName = "appsettings.Development.json";

    /// <summary>The project directory both documents sit in, resolved once.</summary>
    internal static string ProjectDirectory { get; } = LocateProjectDirectory();

    /// <summary>The absolute path of the base document.</summary>
    internal static string BasePath => Path.Combine(ProjectDirectory, BaseFileName);

    /// <summary>The absolute path of the Development overlay.</summary>
    internal static string DevelopmentPath => Path.Combine(ProjectDirectory, DevelopmentFileName);

    /// <summary>Loads the base document alone, as a non-Development host does.</summary>
    /// <returns>The configuration root.</returns>
    internal static IConfigurationRoot Base() =>
        new ConfigurationBuilder().AddJsonFile(BasePath, optional: false).Build();

    /// <summary>
    /// Loads the Development overlay ALONE, which is what makes an absence assertion meaningful.
    /// </summary>
    /// <returns>The configuration root.</returns>
    /// <remarks>
    /// Layered on the base document a key is present whichever file declared it, so "the overlay does
    /// not redeclare this" is unanswerable from the layered view. Read on its own, a null lookup means
    /// the overlay is silent about the key and the base document's value survives - which is the
    /// property that keeps a developer's run behaviourally identical to a deployed one.
    /// </remarks>
    internal static IConfigurationRoot DevelopmentOnly() =>
        new ConfigurationBuilder().AddJsonFile(DevelopmentPath, optional: false).Build();

    /// <summary>Loads the base document with the Development overlay on top, as a developer host does.</summary>
    /// <returns>The configuration root.</returns>
    internal static IConfigurationRoot WithDevelopmentOverlay() =>
        new ConfigurationBuilder()
            .AddJsonFile(BasePath, optional: false)
            .AddJsonFile(DevelopmentPath, optional: false)
            .Build();

    /// <summary>
    /// Loads the base document with an in-memory layer on top, reproducing how any later provider -
    /// an environment variable, a command-line switch, a mounted secret - overrides a declared value.
    /// </summary>
    /// <param name="overrides">The paths and values the later provider supplies.</param>
    /// <returns>The configuration root.</returns>
    /// <remarks>
    /// The in-memory provider is added AFTER the JSON file, so it wins on an identical key by the same
    /// last-writer rule the environment-variable provider relies on. That is what makes an
    /// overridability assertion a statement about the real precedence chain rather than about a
    /// hand-built dictionary.
    /// </remarks>
    internal static IConfigurationRoot BaseWithOverrides(
        IEnumerable<KeyValuePair<string, string?>> overrides) =>
        new ConfigurationBuilder()
            .AddJsonFile(BasePath, optional: false)
            .AddInMemoryCollection(overrides)
            .Build();

    /// <summary>Binds the service's own section out of an already-loaded configuration.</summary>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <returns>The bound options.</returns>
    /// <remarks>
    /// Deliberately the same two calls the composition root makes -
    /// <c>GetSection(DataServicesOptions.SectionName)</c> then <c>Bind</c> - and the section name comes
    /// from the published constant rather than from a literal, so a renamed section fails here instead
    /// of silently binding nothing.
    /// </remarks>
    internal static DataServicesOptions BindService(IConfiguration configuration)
    {
        DataServicesOptions options = new();
        configuration.GetSection(DataServicesOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>Binds the inbound-token section out of an already-loaded configuration.</summary>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <returns>The bound options.</returns>
    internal static JwtAuthenticationOptions BindJwt(IConfiguration configuration)
    {
        JwtAuthenticationOptions options = new();
        configuration.GetSection(JwtAuthenticationOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>The raw text of a document, comments and all.</summary>
    /// <param name="path">The document path.</param>
    /// <returns>The file contents.</returns>
    /// <remarks>
    /// USED FOR THE CREDENTIAL SCAN, AND THE RAWNESS IS THE POINT. Constraint C-F forbids a credential
    /// "not as a default, not as an example, not in a comment", so a scan that read only bound VALUES
    /// would miss the one place a well-meaning author is most likely to paste a sample key. Both these
    /// documents carry long explanatory comment blocks, which is precisely why the scan must see them.
    /// </remarks>
    internal static string RawText(string path) => File.ReadAllText(path);

    /// <summary>Every leaf key and value of a document, flattened to configuration paths.</summary>
    /// <param name="configuration">The configuration to walk.</param>
    /// <returns>Each leaf path with its value.</returns>
    /// <remarks>
    /// A leaf is a node with a value and no children. Walking rather than hand-listing is what makes
    /// the census assertions total: a group added to the document tomorrow is walked without anyone
    /// remembering to add it here.
    /// </remarks>
    internal static IReadOnlyList<KeyValuePair<string, string?>> Leaves(IConfiguration configuration)
    {
        List<KeyValuePair<string, string?>> leaves = [];
        Collect(configuration, leaves);
        return leaves;

        static void Collect(
            IConfiguration node,
            List<KeyValuePair<string, string?>> into)
        {
            foreach (IConfigurationSection child in node.GetChildren())
            {
                bool hasChildren = child.GetChildren().Any();

                if (!hasChildren)
                {
                    into.Add(new KeyValuePair<string, string?>(child.Path, child.Value));
                    continue;
                }

                Collect(child, into);
            }
        }
    }

    /// <summary>
    /// Resolves a configuration path against a bound object graph and returns the value it reached.
    /// </summary>
    /// <param name="root">The bound options instance to walk from.</param>
    /// <param name="relativePath">
    /// The path with the root section name already removed - for example
    /// <c>Localization:Locale</c> rather than <c>DataServices:Localization:Locale</c>.
    /// </param>
    /// <returns>The value at that path, which may legitimately be <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// When a segment names no property, which is the failure this method exists to produce.
    /// </exception>
    /// <remarks>
    /// <para>
    /// THIS IS THE MECHANISM BEHIND THE TABLE-DRIVEN ROWS, AND IT IS WHY THEY CAN STAY SERIALIZABLE.
    /// A theory row that carried a lambda would not survive test discovery, so each row carries a
    /// PATH - a plain string, exactly as an operator writes it in the document - and this method turns
    /// that string into the member it addresses. The rows therefore assert against the same spelling
    /// the document uses, which is the spelling that can actually be wrong.
    /// </para>
    /// <para>
    /// It works because the binder's own rule is the one reproduced here: a path segment names a
    /// property of the current object, matched by name. A segment that names nothing is an error rather
    /// than a null, because a silent null is indistinguishable from a key that genuinely binds to a
    /// null - and telling those two apart is the whole purpose of the census.
    /// </para>
    /// </remarks>
    internal static object? ReadBoundValue(object root, string relativePath)
    {
        object? current = root;

        foreach (string segment in relativePath.Split(':'))
        {
            if (current is null)
            {
                return null;
            }

            PropertyInfo? property = current.GetType().GetProperty(
                segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            current = property is null
                ? throw new InvalidOperationException(
                    $"'{segment}' of '{relativePath}' names no public property on "
                    + $"{current.GetType().Name}, so a configuration key spelled that way binds "
                    + "nothing at all and is silently ignored by the binder.")
                : property.GetValue(current);
        }

        return current;
    }

    /// <summary>Walks up to the directory holding the two documents.</summary>
    /// <returns>The absolute project directory.</returns>
    private static string LocateProjectDirectory()
    {
        DirectoryInfo? candidate = new(TestRepositoryRoot.SearchStart);

        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, RepositoryRootMarker)))
            {
                return Path.Combine(
                    candidate.FullName,
                    "services",
                    "dataservices-service",
                    "PowerFramework.DataServices");
            }

            candidate = candidate.Parent;
        }

        throw new InvalidOperationException(
            $"No ancestor of '{TestRepositoryRoot.SearchStart}' carries {RepositoryRootMarker}, so the "
            + "two settings documents cannot be located. See TestRepositoryRoot for the anchor this "
            + "walk starts from.");
    }
}


/// <summary>
/// The declared-source half of the configuration contract: what the two shipped documents say, and
/// that what they say is what the legacy oracle says.
/// </summary>
public sealed class DataServicesSettingsDocumentTests
{
    /// <summary>
    /// Every preserved legacy default, as the base document declares it, with the oracle line that
    /// adjudicates the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROWS ARE THE INVENTORY, AND EACH ONE CITES ITS OWN EVIDENCE. Column one is the path an
    /// operator writes, relative to <see cref="DataServicesOptions.SectionName"/>. Column two is the
    /// value the document must declare, written exactly as the configuration provider yields it -
    /// a string, because that is what a configuration value IS before binding. Column three is the
    /// locator under <c>ws_objects/**</c> that settles the value, which is the only authority there is:
    /// the changelog stops years before the commit history and the two PowerBuilder project objects
    /// contradict each other, so nothing but the source can adjudicate a default (AAP 0.1.4).
    /// </para>
    /// <para>
    /// SERIALIZABLE BY CONSTRUCTION - three strings per row. That is not an accident of style: a row
    /// carrying a delegate would not survive test discovery, and a row carrying a typed value would
    /// need one theory per CLR type. Strings keep the whole inventory in one table, which is what makes
    /// an omission visible.
    /// </para>
    /// <para>
    /// <see cref="DataServicesOptions.DropDownSearch"/>'s <c>ShowFilteredRows</c> is absent from this
    /// table ON PURPOSE. Its declared value is JSON null, which is not a string, and its whole
    /// significance is that null is a THIRD state rather than a value -
    /// <see cref="TheShowFilteredRowsKeyIsDeclaredNullAndBindsToNullRatherThanFalse"/> carries it alone.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string> PreservedLegacyDefaults()
    {
        TheoryData<string, string, string> rows = [];

        foreach ((string path, string value, string locator) in PreservedDefaultInventory)
        {
            rows.Add(path, value, locator);
        }

        return rows;
    }

    /// <summary>
    /// The single hand-maintained copy of the preserved-default inventory, in one shape both member-data
    /// providers project from.
    /// </summary>
    /// <remarks>
    /// ONE LIST, TWO PROJECTIONS, AND THAT IS DELIBERATE. The value table and the overlay-absence table
    /// need the same paths, and maintaining the paths twice would let the two drift - with the absence
    /// check silently covering fewer keys rather than failing. A tuple array is used rather than a
    /// second <see cref="TheoryData{T1,T2,T3}"/> because the enumeration shape of theory data is an
    /// implementation detail of the test framework, and a projection should not depend on it.
    /// </remarks>
    private static readonly (string Path, string Value, string Locator)[] PreservedDefaultInventory =
        [
            // The named User Example. `lang = "en"` is assigned as a literal inside the framework
            // application's open event, immediately before the three-way provider switch it feeds.
            ("Localization:Locale", "en", "ws_objects/pfw.pbl.src/pfw.sra:L94"),

            // `privatewrite long #Style = RS_SINGLE`. The identifier, not the numeral, is the legacy's
            // own spelling - which is why the typed assertion compares against RowSelectService's
            // constant rather than against 1.
            (
                "RowSelect:Style",
                "1",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L25"),

            // The five context-menu column behaviours, each declared `= true` on its own line in one
            // contiguous block.
            (
                "ContextMenu:ColAutoWidth",
                "true",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L49"),
            (
                "ContextMenu:ColCheck",
                "true",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L50"),
            (
                "ContextMenu:ColCopy",
                "true",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L51"),
            (
                "ContextMenu:ColPaste",
                "true",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L52"),
            (
                "ContextMenu:ItemCopy",
                "true",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_contextmenu.sru:L53"),

            // `privatewrite boolean #Trace` with NO initializer - so the legacy default is PowerScript's
            // own boolean zero, false. Tracing off is the preserved behaviour, not a policy choice.
            (
                "ColumnExpression:Trace",
                "false",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L98"),

            // `if nRowCnt > 200 then #DataWindow.SetRedraw(false)` and its matching restore two lines
            // later. The threshold is a bare literal at both sites, and it is a STRICT greater-than -
            // which is why zero is a legal value meaning "always suppress" and is validated as such.
            (
                "ColumnExpression:RedrawSuppressionRowThreshold",
                "200",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L223"),

            // `_vecCalcStack = Create n_vector` followed immediately by `_vecCalcStack.Reserve(20)` in
            // the constructor. The reservation is the calc and recursion stack's initial capacity.
            (
                "ColumnExpression:CalcStackInitialCapacity",
                "20",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L2421"),

            // `privatewrite ulong #FilterType = FILTER_DISP + FILTER_DISP_PY` - the sum is 3, and the
            // data-column bit is deliberately NOT set. See the dedicated test for why 3 and 7 must never
            // be confused here.
            (
                "DropDownSearch:FilterType",
                "3",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L57"),

            // The Pinyin first-letter clause appends its flags argument as the literal 7. Unlike the
            // filter-type bits, these three DO live in the framework-wide constant catalogue, so the
            // typed assertion composes them by name.
            (
                "DropDownSearch:PinyinMatchFlags",
                "7",
                "ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_dropdownsearch.sru:L323"),
        ];

    /// <summary>
    /// The preserved-default paths the Development overlay must stay silent about.
    /// </summary>
    /// <remarks>
    /// Projected from <see cref="PreservedDefaultInventory"/> rather than written a second time, plus
    /// the one row that inventory cannot carry. Two hand-maintained lists would drift, and the drift
    /// would silently shrink the absence check rather than break it.
    /// </remarks>
    public static TheoryData<string> PreservedLegacyDefaultPaths()
    {
        TheoryData<string> paths = [];

        foreach ((string path, _, _) in PreservedDefaultInventory)
        {
            paths.Add(path);
        }

        // The tri-state key, absent from the value table because its declared value is JSON null.
        paths.Add("DropDownSearch:ShowFilteredRows");

        return paths;
    }

    /// <summary>
    /// The base document declares every preserved legacy default, at the value its oracle line states.
    /// </summary>
    /// <param name="relativePath">The key, relative to the service section.</param>
    /// <param name="declaredValue">The value the document must declare.</param>
    /// <param name="oracleLocator">The <c>ws_objects/**</c> line that adjudicates the value.</param>
    /// <remarks>
    /// <para>
    /// THE DOCUMENT IS ASSERTED, NOT THE BOUND OBJECT, WHICH IS THE POINT OF THIS ROW. Binding would
    /// answer with the compiled property initializer whenever the document were silent, so a test that
    /// bound first could not tell a declared value from a missing key. Reading the configuration value
    /// directly distinguishes them: a null here means the document does not declare the key at all.
    /// </para>
    /// <para>
    /// The failure message carries the oracle locator, so a future reader who disagrees with a value has
    /// the line to check rather than an opinion to argue with. That is constraint C-K applied where it
    /// is actually useful - at the moment of failure.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PreservedLegacyDefaults))]
    public void TheBaseDocumentDeclaresEveryPreservedLegacyDefaultAtItsOracleValue(
        string relativePath,
        string declaredValue,
        string oracleLocator)
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();
        string fullPath = $"{DataServicesOptions.SectionName}:{relativePath}";

        string? actual = document[fullPath];

        Assert.NotNull(actual);

        // ⚠ MEASURED, AND NOT WHAT READING THE FILE SUGGESTS: THE JSON PROVIDER RENDERS A BOOLEAN IN THE
        // CLR'S SPELLING, NOT JSON'S. The document contains the JSON literal `true`, and the provider
        // yields the string "True" - because it materialises a value by calling ToString on the parsed
        // element, and a true element renders as `bool.TrueString`. The rows above are written in the
        // spelling an operator actually READS in the file, which is the spelling that can be wrong, so
        // this one comparison is case-insensitive to bridge the two renderings. Every other string
        // comparison in this file is ordinal and exact.
        Assert.True(
            string.Equals(declaredValue, actual, StringComparison.OrdinalIgnoreCase),
            $"'{fullPath}' declares '{actual}' where the oracle states '{declaredValue}'. A preserved "
                + $"legacy default is behaviour, so this is a behavioural change rather than a settings "
                + $"change (constraint C-B). Adjudicate against {oracleLocator}.");

        // AND THE KEY IS NOT AN ORPHAN. A value that matched but bound to nothing would still be a
        // defect: the service would run on the compiled default while the document said otherwise.
        // Resolving the path against the bound graph proves the key addresses a real member.
        DataServicesOptions bound = DataServicesSettingsDocuments.BindService(document);

        object? boundValue = DataServicesSettingsDocuments.ReadBoundValue(bound, relativePath);

        Assert.NotNull(boundValue);

        // CASE-INSENSITIVE ON PURPOSE, AND ONLY HERE. JSON spells a boolean `true` and the CLR renders
        // one `True`, so this single comparison crosses a representation boundary rather than comparing
        // two spellings of the same thing. Every other comparison in this file is ordinal and exact.
        string boundText = Convert.ToString(boundValue, CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.True(
            string.Equals(declaredValue, boundText, StringComparison.OrdinalIgnoreCase),
            $"'{fullPath}' declares '{declaredValue}' and binds to '{boundText}'. A declared value that "
                + $"does not survive binding leaves the compiled default in force while the document "
                + $"says otherwise. Adjudicate against {oracleLocator}.");

        // Named so the locator travels with any future failure rather than only with this source line.
        Assert.False(
            string.IsNullOrWhiteSpace(oracleLocator),
            $"'{fullPath}' has no oracle locator, so its value could not be adjudicated against "
                + "ws_objects/** - which is the only authority for a preserved default.");
    }

    /// <summary>
    /// The document's declared defaults and the options type's compiled defaults are the same values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO-DECLARATION HAZARD, ASSERTED DIRECTLY. Every preserved default exists twice - as a
    /// property initializer and as a document key - and the language enforces no agreement between
    /// them. Whichever is edited alone wins at run time depending only on whether the key is present,
    /// which makes a divergence invisible: Part One's assertions read the initializer, an operator reads
    /// the document, and the running service may follow either.
    /// </para>
    /// <para>
    /// Comparing a default-constructed instance against one bound from the document collapses that
    /// hazard into one failing test. It also means neither declaration is privileged: the row does not
    /// say which is right, only that they cannot disagree - and the oracle locators on the table above
    /// are what decide which to fix.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeclaredDefaultsAndTheCompiledDefaultsCannotDisagree()
    {
        DataServicesOptions compiled = new();
        DataServicesOptions declared =
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base());

        Assert.Equal(compiled.Localization.Locale, declared.Localization.Locale, StringComparer.Ordinal);
        Assert.Equal(compiled.RowSelect.Style, declared.RowSelect.Style);

        Assert.Equal(compiled.ContextMenu.ColAutoWidth, declared.ContextMenu.ColAutoWidth);
        Assert.Equal(compiled.ContextMenu.ColCheck, declared.ContextMenu.ColCheck);
        Assert.Equal(compiled.ContextMenu.ColCopy, declared.ContextMenu.ColCopy);
        Assert.Equal(compiled.ContextMenu.ColPaste, declared.ContextMenu.ColPaste);
        Assert.Equal(compiled.ContextMenu.ItemCopy, declared.ContextMenu.ItemCopy);

        Assert.Equal(compiled.ColumnExpression.Trace, declared.ColumnExpression.Trace);
        Assert.Equal(
            compiled.ColumnExpression.RedrawSuppressionRowThreshold,
            declared.ColumnExpression.RedrawSuppressionRowThreshold);
        Assert.Equal(
            compiled.ColumnExpression.CalcStackInitialCapacity,
            declared.ColumnExpression.CalcStackInitialCapacity);
        Assert.Equal(compiled.ColumnExpression.PageResolution, declared.ColumnExpression.PageResolution);
        Assert.Equal(compiled.ColumnExpression.PageRowsPerPage, declared.ColumnExpression.PageRowsPerPage);
        Assert.Equal(
            compiled.ColumnExpression.MacroInvocationTimeout,
            declared.ColumnExpression.MacroInvocationTimeout);

        Assert.Equal(compiled.DropDownSearch.FilterType, declared.DropDownSearch.FilterType);
        Assert.Equal(compiled.DropDownSearch.ShowFilteredRows, declared.DropDownSearch.ShowFilteredRows);
        Assert.Equal(compiled.DropDownSearch.PinyinMatchFlags, declared.DropDownSearch.PinyinMatchFlags);

        // The boundary-created groups whose values are behaviour-adjacent rather than environmental. The
        // two upstream ADDRESSES are deliberately excluded: they are required, they default to empty by
        // design, and the document supplies them - so a disagreement there is correct rather than a
        // defect.
        Assert.Equal(compiled.EventChain.StrictOrdering, declared.EventChain.StrictOrdering);
        Assert.Equal(compiled.EventChain.AnswerTimeout, declared.EventChain.AnswerTimeout);

        Assert.Equal(
            compiled.EventChain.MaxPendingNotifications,
            declared.EventChain.MaxPendingNotifications);
        Assert.Equal(
            compiled.EventChain.MaxPendingNotifications,
            declared.EventChain.MaxPendingNotifications);
        Assert.Equal(
            compiled.RestProjection.MaxStreamedElements,
            declared.RestProjection.MaxStreamedElements);
        Assert.Equal(
            compiled.RestProjection.StreamCollectionWindow,
            declared.RestProjection.StreamCollectionWindow);
        Assert.Equal(compiled.PersistenceSession.Dbms, declared.PersistenceSession.Dbms, StringComparer.Ordinal);
        Assert.Equal(compiled.PersistenceSession.AutoCommit, declared.PersistenceSession.AutoCommit);
    }


    /// <summary>
    /// Each declared default that has an owning constant equals that constant, never a retyped literal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY A CONSTANT AND NOT THE NUMBER. Three of these values are the sum or the name of a symbol the
    /// legacy declares, and the symbol is what the plan requires be preserved: identifier spellings
    /// travel in serialized payloads, log records and characterization recordings, so a rename would
    /// silently invalidate every stored comparison (AAP 0.4.5.3). Asserting against the owning type's
    /// own constant means the assertion follows the symbol if the symbol ever moves, whereas asserting
    /// against <c>1</c>, <c>3</c> or <c>7</c> would pass while the symbol drifted underneath it.
    /// </para>
    /// <para>
    /// NO CONSTANT IS DECLARED HERE, WHICH IS ITSELF A CONSTRAINT. This file is deliberately NOT on the
    /// repository <c>.editorconfig</c>'s BAND 3 roster - the narrow band that suppresses the naming
    /// analyzer for the files whose SCREAMING_SNAKE identifiers are the recording-visible artifact - so
    /// every legacy identifier below is REFERENCED from the type that owns it and none is restated.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeclaredDefaultsEqualTheOwningTypesConstantsRatherThanRetypedLiterals()
    {
        DataServicesOptions declared =
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base());

        // n_cst_dwsvc_rowselect.sru:L25 - `privatewrite long #Style = RS_SINGLE`. The legacy writes the
        // identifier; the owning service publishes it; this compares against that and not against 1.
        Assert.Equal(RowSelectService.RS_SINGLE, declared.RowSelect.Style);

        // And the OTHER member of that set is not what is declared, which is what makes the row above
        // an assertion rather than a coincidence of value.
        Assert.NotEqual(RowSelectService.RS_MULTIPLE, declared.RowSelect.Style);

        // n_cst_dwsvc_dropdownsearch.sru:L57 - the sum of exactly two of the three filter bits.
        // Composed here the same way the legacy composes it, so the assertion proves the sum rather than
        // restating its total.
        Assert.Equal(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            declared.DropDownSearch.FilterType);

        // enums.sru:L1147-L1149 - ignore case, ignore full-width versus half-width, match fuzzy
        // pronunciation. Their disjunction is the 7 the call site at :L323 hardcodes.
        Assert.Equal(
            Enums.PY_LIKE_IGNORE_CASE | Enums.PY_LIKE_IGNORE_WIDTH | Enums.PY_LIKE_FUZZY_SOUND,
            declared.DropDownSearch.PinyinMatchFlags);

        // The two boundary-created durations that publish their own defaults, compared against those
        // published values rather than against a time-span literal written twice.
        Assert.Equal(
            SessionLifetimeOptions.DefaultIdleTimeout,
            declared.Sessions.ValidationSession.IdleTimeout);
        Assert.Equal(
            SessionLifetimeOptions.DefaultIdleTimeout,
            declared.Sessions.ExpressionSession.IdleTimeout);
        Assert.Equal(
            ClientResilienceOptions.DefaultRequestTimeout,
            declared.Resilience.Persistence.RequestTimeout);
        Assert.Equal(
            ClientResilienceOptions.DefaultRequestTimeout,
            declared.Resilience.Security.RequestTimeout);

        // The boundary-created COUNT that publishes its own default, for the same reason: the document
        // must not carry a second copy of the number.
        Assert.Equal(
            EventChainOptions.DefaultMaxPendingNotifications,
            declared.EventChain.MaxPendingNotifications);
    }

    /// <summary>
    /// The declared filter type is three, and is emphatically not the all-bits value seven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ TWO DIFFERENT SEVENS LIVE IN THE SAME GROUP, AND CONFUSING THEM IS A BEHAVIOURAL CHANGE.
    /// <c>DropDownSearchModel.FILTER_ALL</c> is 7 and <c>DropDownSearch:PinyinMatchFlags</c> is also 7 -
    /// same number, unrelated contracts. The filter type must be 3: the legacy sets the display bit and
    /// the display-Pinyin bit and DELIBERATELY omits the data-column bit
    /// [n_cst_dwsvc_dropdownsearch.sru:L57], and each of the three is read by an independent bit test
    /// guarding one clause of the constructed filter expression. Declaring 7 here would add a
    /// data-column match the legacy does not perform, which under constraint C-B is a new behaviour
    /// rather than a preserved one - and it is the single most plausible "tidying" edit anyone would
    /// make to this key, because 7 looks like the complete value.
    /// </para>
    /// <para>
    /// Both halves are asserted - the equality with the two-bit sum AND the inequality with the all-bits
    /// constant - because the equality alone would still pass if someone redefined the constants such
    /// that the sum became seven.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDeclaredFilterTypeSetsTwoOfThreeBitsAndIsNotTheAllBitsValue()
    {
        DataServicesOptions declared =
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base());

        Assert.Equal(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            declared.DropDownSearch.FilterType);

        Assert.NotEqual(DropDownSearchModel.FILTER_ALL, declared.DropDownSearch.FilterType);

        // THE OMITTED BIT, NAMED. Asserting the absence of the data-column bit states which of the three
        // is off, so a future reader does not have to work it out from a numeral.
        Assert.Equal(
            0u,
            declared.DropDownSearch.FilterType & DropDownSearchModel.FILTER_DATA);

        // AND THE OTHER SEVEN IS STILL SEVEN. Recorded beside its namesake precisely so the two are read
        // together: the Pinyin flags legitimately carry all three of their own bits.
        Assert.Equal(
            Enums.PY_LIKE_IGNORE_CASE | Enums.PY_LIKE_IGNORE_WIDTH | Enums.PY_LIKE_FUZZY_SOUND,
            declared.DropDownSearch.PinyinMatchFlags);
    }

    /// <summary>
    /// The tri-state drop-down key is declared null in the document and binds to null, never to false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE NULL IS A THIRD STATE AND IT GATES A REAL THREE-BRANCH ALGORITHM. The legacy declares
    /// <c>privatewrite boolean #ShowFilteredRows</c> with no initializer
    /// [n_cst_dwsvc_dropdownsearch.sru:L58] and its constructor consists of nothing but a super call and
    /// <c>SetNull(#ShowFilteredRows)</c> [:L503] - so null is not incidental, it is set explicitly. The
    /// resolver then opens by short-circuiting on a non-null value and only otherwise evaluates its
    /// three ordered branches.
    /// </para>
    /// <para>
    /// SO FALSE AND NULL ARE NOT THE SAME ANSWER, AND A <c>bool</c> INSTEAD OF A <c>bool?</c> WOULD
    /// DELETE THE ALGORITHM RATHER THAN CHANGE A DEFAULT. That is why this asserts the declared JSON
    /// value is null, that the bound value is null, and - separately, because it is the failure mode
    /// that would look correct - that it is NOT false.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShowFilteredRowsKeyIsDeclaredNullAndBindsToNullRatherThanFalse()
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();
        string fullPath =
            $"{DataServicesOptions.SectionName}:DropDownSearch:ShowFilteredRows";

        // A JSON null yields a null configuration VALUE while the key itself still exists, so the two
        // facts are asserted apart: the key is declared, and what it declares is nothing.
        Assert.Null(document[fullPath]);

        Assert.Contains(
            fullPath,
            DataServicesSettingsDocuments.Leaves(document).Select(static leaf => leaf.Key),
            StringComparer.Ordinal);

        DataServicesOptions declared = DataServicesSettingsDocuments.BindService(document);

        Assert.Null(declared.DropDownSearch.ShowFilteredRows);

        // NOT FALSE. Written as its own assertion because `Assert.Null` above would also pass on a
        // nullable that had been narrowed to a non-nullable somewhere in between - at which point the
        // property could no longer hold the third state at all.
        Assert.NotEqual(false, declared.DropDownSearch.ShowFilteredRows);

        // AND THE PROPERTY IS STILL NULLABLE. The type is what makes the third state representable, so it
        // is asserted structurally rather than inferred from the value.
        Assert.Equal(
            typeof(bool?),
            typeof(DropDownSearchOptions)
                .GetProperty(nameof(DropDownSearchOptions.ShowFilteredRows))!
                .PropertyType);
    }

    /// <summary>
    /// The Development overlay redeclares no preserved legacy default.
    /// </summary>
    /// <param name="relativePath">The preserved-default key, relative to the service section.</param>
    /// <remarks>
    /// <para>
    /// THIS IS WHAT KEEPS A CHARACTERIZATION RECORDING VALID. A preserved default is behaviour, and
    /// behaviour does not vary by environment. If the overlay moved one, a developer's run would exhibit
    /// behaviour a deployed instance does not - and a recording captured under it would be compared
    /// against an oracle that never saw the changed value. The Golden-Master technique's one hard
    /// prerequisite is repeatability (AAP 0.6.7), so this is the technique's own requirement rather than
    /// a local preference.
    /// </para>
    /// <para>
    /// READ FROM THE OVERLAY ALONE. Layered on the base document every one of these keys is present, so
    /// the layered view cannot answer the question at all - see
    /// <see cref="DataServicesSettingsDocuments.DevelopmentOnly"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(PreservedLegacyDefaultPaths))]
    public void TheDevelopmentOverlayRedeclaresNoPreservedLegacyDefault(string relativePath)
    {
        IConfigurationRoot overlay = DataServicesSettingsDocuments.DevelopmentOnly();
        string fullPath = $"{DataServicesOptions.SectionName}:{relativePath}";

        // NOT `overlay[fullPath] is null` ALONE - a JSON null would satisfy that while still redeclaring
        // the key, and redeclaring a key as null is exactly how a preserved default gets silently
        // blanked. The key must be ABSENT from the document's own leaves.
        Assert.DoesNotContain(
            fullPath,
            DataServicesSettingsDocuments.Leaves(overlay).Select(static leaf => leaf.Key),
            StringComparer.OrdinalIgnoreCase);

        // And the section the key sits in is not present either, so the assertion cannot be satisfied by
        // a group that exists with the leaf spelled differently.
        string groupPath = fullPath[..fullPath.LastIndexOf(':')];

        Assert.False(
            overlay.GetSection(groupPath).Exists(),
            $"'{groupPath}' exists in {DataServicesSettingsDocuments.DevelopmentFileName}. The five "
                + "preserved-legacy-default groups are behaviour, not environment, and an overlay that "
                + "touches one lets a developer's run diverge behaviourally from a deployed instance - "
                + "which silently invalidates every characterization recording captured under it.");
    }

    /// <summary>
    /// The overlay's entire content is the environment-dependent set, enumerated exhaustively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// AN ALLOW-LIST RATHER THAN A DENY-LIST, AND THAT IS THE STRONGER SHAPE. The row above proves no
    /// KNOWN preserved default is redeclared; it cannot prove that nothing ELSE was added, because a key
    /// nobody thought of is by definition not on the deny-list. This row asserts the overlay's leaves
    /// are EXACTLY the expected set, so any addition at all fails here and has to be justified
    /// deliberately - which is the point at which someone would notice it was behaviour.
    /// </para>
    /// <para>
    /// FIVE log levels, one authority host, two upstream addresses and two session idle windows. Nothing
    /// else, and in particular NO relaxation: not one of the four token-validation switches, not the
    /// audience, and not the metadata-transport requirement appears here, so an inbound credential is
    /// verified under Development exactly as strictly as it is deployed (constraint C-G).
    /// </para>
    /// <para>
    /// 🔴 <b>THE FIFTH LOG LEVEL IS <c>Microsoft.AspNetCore.Hosting.Diagnostics</c>, AND IT IS THE ONE
    /// ENTRY HERE THAT LOWERS RATHER THAN RAISES.</b> It was added because a runtime probe found a bearer
    /// token in a query parameter written to the container log in cleartext, and traced it to this
    /// overlay: raising the parent <c>Microsoft.AspNetCore</c> category to <c>Information</c> turns on the
    /// hosting layer's "Request starting"/"Request finished" pair, which are the only records in this
    /// service that render the FULL request URL - every application-authored record uses
    /// <c>Request.Path</c>, and the projection's own refusal record carries only the route PATTERN. The
    /// child category therefore returns to the deployed level while the rest of
    /// <c>Microsoft.AspNetCore</c> - authentication, authorization, routing and Kestrel diagnostics -
    /// stays at <c>Information</c> where a developer needs it. It does not violate the raise-never-silence
    /// property asserted below: the base document declares <c>Microsoft.AspNetCore</c> at
    /// <c>Warning</c>, so a deployed instance emits nothing for this category either, and the overlay
    /// makes a developer run EQUAL to a deployed one rather than quieter than it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDevelopmentOverlayContainsExactlyTheEnvironmentDependentKeysAndNothingElse()
    {
        IReadOnlyList<KeyValuePair<string, string?>> leaves =
            DataServicesSettingsDocuments.Leaves(DataServicesSettingsDocuments.DevelopmentOnly());

        string[] expected =
        [
            "Authentication:Jwt:Authority",
            "DataServices:Persistence:Address",
            "DataServices:Security:BaseAddress",
            "DataServices:Sessions:ExpressionSession:IdleTimeout",
            "DataServices:Sessions:ValidationSession:IdleTimeout",
            "Logging:LogLevel:Grpc",
            "Logging:LogLevel:Microsoft.AspNetCore",
            "Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics",
            "Logging:LogLevel:PowerFramework.DataServices",
            "Logging:LogLevel:System.Net.Http.HttpClient",
        ];

        Assert.Equal(
            expected,
            leaves.Select(static leaf => leaf.Key).OrderBy(static key => key, StringComparer.Ordinal));
    }


    // ==============================================================================================
    //  THE ENVIRONMENT-DEPENDENT KEYS - WHERE TO REACH THE OTHER THREE SERVICES, AND ON WHAT
    // ==============================================================================================

    /// <summary>
    /// The inbound-token identity keys bind from the base document, and every validation switch is on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DIVISION THIS ROW ENFORCES IS THE WHOLE OF CONSTRAINT C-G AT THIS BOUNDARY. Authority,
    /// metadata address and audience decide WHOM this service trusts and are legitimately environmental.
    /// The four <c>Validate*</c> switches and the metadata-transport requirement decide WHETHER it checks
    /// at all, and none of them is environmental: each must be true in the base document and must stay
    /// true wherever the service runs.
    /// </para>
    /// <para>
    /// <c>MetadataAddress</c> is declared as JSON null on purpose, and the null is meaningful rather than
    /// missing: null means DERIVE the discovery address from the authority, which stays correct when the
    /// authority moves between an orchestration hostname and loopback. Writing an explicit address would
    /// have to be re-written every time the authority moved, and forgetting to would point discovery at
    /// the wrong host while the authority looked right.
    /// </para>
    /// <para>
    /// Security is the SOLE issuer, so what is configured here is verification material only - an
    /// authority to fetch a published key set from, never a key. That negative is asserted over both
    /// documents by the credential scan further down.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInboundTokenKeysBindAndEveryValidationSwitchIsDeclaredOn()
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();
        JwtAuthenticationOptions jwt = DataServicesSettingsDocuments.BindJwt(document);

        // WHOM TO TRUST - the environmental half.
        Assert.Equal("https://security-service:5104", jwt.Authority, StringComparer.Ordinal);
        Assert.Equal("powerframework-dataservices", jwt.Audience, StringComparer.Ordinal);

        // DERIVE, DON'T DUPLICATE. Null is the declared value and the correct one.
        Assert.Null(jwt.MetadataAddress);
        Assert.Null(document[$"{JwtAuthenticationOptions.SectionName}:MetadataAddress"]);

        // WHETHER TO CHECK - the half that is not environmental. All five stay on.
        Assert.True(jwt.RequireHttpsMetadata);
        Assert.True(jwt.ValidateIssuer);
        Assert.True(jwt.ValidateAudience);
        Assert.True(jwt.ValidateLifetime);
        Assert.True(jwt.ValidateIssuerSigningKey);

        // The refresh windows, compared against the type's published defaults rather than against a
        // time-span literal restated here.
        Assert.Equal(
            JwtAuthenticationOptions.DefaultMetadataRefreshInterval,
            jwt.MetadataRefreshInterval);
        Assert.Equal(
            JwtAuthenticationOptions.DefaultMetadataAutomaticRefreshInterval,
            jwt.MetadataAutomaticRefreshInterval);

        // THE CALLER ROSTER IS POPULATED, AND ITS ONE ENTRY IS GATEWAY. Nothing but Gateway calls
        // DataServices in the delivered topology (AAP 0.4.3), so a roster naming anything else - or an
        // empty one, which the validator refuses outright - would widen the boundary.
        Assert.Equal(["powerframework-gateway"], jwt.PermittedCallers);
    }

    /// <summary>
    /// The Development overlay moves only the HOST of the authority, never its scheme and never a switch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ THE SCHEME STAYS <c>https</c>, AND THAT IS A REQUIREMENT RATHER THAN A PREFERENCE. Security binds
    /// a TLS listener in every environment because its token endpoint authenticates its caller with a
    /// presented Basic credential OR a trusted client certificate: the certificate cannot be presented,
    /// requested or validated on a plaintext listener at all, and the Basic credential would travel in
    /// clear - so a loopback Security serving plain <c>http</c> is a topology in which nothing can
    /// authenticate safely rather than a convenient one. Relaxing this to <c>http</c>, with or without a
    /// paired metadata-transport relaxation, is therefore not available even for a development run.
    /// </para>
    /// <para>
    /// The consequence asserted here is that the overlay declares NO metadata-transport relaxation. That
    /// matters because a <c>false</c> there with an https authority would be a standing relaxation with
    /// no consumer - the kind of dead setting a later reader reasonably mistakes for a requirement.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDevelopmentOverlayMovesTheAuthorityHostAndRelaxesNothing()
    {
        IConfigurationRoot overlay = DataServicesSettingsDocuments.DevelopmentOnly();
        IConfigurationRoot layered = DataServicesSettingsDocuments.WithDevelopmentOverlay();

        Assert.Equal(
            "https://localhost:5104",
            overlay[$"{JwtAuthenticationOptions.SectionName}:Authority"],
            StringComparer.Ordinal);

        // Loopback host, unchanged scheme, unchanged port - stated as three separate facts because the
        // scheme is the one a well-meaning edit would change.
        Uri baseAuthority = new(
            DataServicesSettingsDocuments.Base()[$"{JwtAuthenticationOptions.SectionName}:Authority"]!);
        Uri developmentAuthority = new(
            overlay[$"{JwtAuthenticationOptions.SectionName}:Authority"]!);

        Assert.Equal(baseAuthority.Scheme, developmentAuthority.Scheme, StringComparer.Ordinal);
        Assert.Equal(baseAuthority.Port, developmentAuthority.Port);
        Assert.Equal(Uri.UriSchemeHttps, developmentAuthority.Scheme, StringComparer.Ordinal);
        Assert.NotEqual(baseAuthority.Host, developmentAuthority.Host, StringComparer.Ordinal);

        // NOT ONE RELAXATION IN THE OVERLAY. Asserted as absence from the document rather than as a
        // value, because a switch that is absent cannot be set to the wrong thing.
        foreach (string switchName in (string[])
            [
                nameof(JwtAuthenticationOptions.RequireHttpsMetadata),
                nameof(JwtAuthenticationOptions.ValidateIssuer),
                nameof(JwtAuthenticationOptions.ValidateAudience),
                nameof(JwtAuthenticationOptions.ValidateLifetime),
                nameof(JwtAuthenticationOptions.ValidateIssuerSigningKey),
                nameof(JwtAuthenticationOptions.Audience),
            ])
        {
            Assert.Null(overlay[$"{JwtAuthenticationOptions.SectionName}:{switchName}"]);
        }

        // AND THE LAYERED VIEW - what a developer host actually binds - still validates every check.
        JwtAuthenticationOptions layeredJwt = DataServicesSettingsDocuments.BindJwt(layered);

        Assert.True(layeredJwt.RequireHttpsMetadata);
        Assert.True(layeredJwt.ValidateIssuer);
        Assert.True(layeredJwt.ValidateAudience);
        Assert.True(layeredJwt.ValidateLifetime);
        Assert.True(layeredJwt.ValidateIssuerSigningKey);
    }

    /// <summary>
    /// Both upstream addresses bind from the base document, and the overlay moves each to loopback.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ THE PERSISTENCE PORT IS 5101, WHICH IS THE PORT AAP 0.3.2.2 ASSIGNS IT, AND NAMING ANYTHING ELSE
    /// IS THE DEFECT. This address builds a gRPC channel. Persistence binds ONE TLS endpoint,
    /// <c>https://+:5101</c> with <c>Protocols: Http1AndHttp2</c>, so ALPN carries the C-05..C-08 gRPC
    /// contracts and the HTTP/1.1 <c>/health</c> and <c>/v1/ping</c> surfaces on that single port. The
    /// tempting alternative is 5111, a second <c>Http2</c>-only endpoint placed above the documented band;
    /// it is not available, because AAP 0.3.2.2 places C-05..C-08 on 5101, so a channel built on 5111
    /// reaches a port the map does not give those contracts. Naming 5111 fails every retrieval and every
    /// update at connect, before the request arrives, with nothing on the far side logging the cause.
    /// The 5101-5105 band of the attached environment (constraint C-L) is therefore the whole of
    /// the estate's addressing, with 5103 still unallocated (constraint C-D).
    /// </para>
    /// <para>
    /// The two addresses stay SEPARATE entries rather than one derived from the other: the Security base
    /// address is the cryptographic API this service calls, the authority is what its inbound handler
    /// trusts, and an environment must be able to move either without the other.
    /// </para>
    /// <para>
    /// NO CREDENTIAL COMPONENT IN EITHER, IN EITHER DOCUMENT. That is a credential control rather than a
    /// shape preference: a user-information component would be copied into every log record, exception
    /// and trace that captures a request URI.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothUpstreamAddressesBindAndTheOverlayMovesEachToLoopback()
    {
        DataServicesOptions baseline =
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base());
        DataServicesOptions development = DataServicesSettingsDocuments.BindService(
            DataServicesSettingsDocuments.WithDevelopmentOverlay());

        Assert.Equal("https://persistence-service:5101", baseline.Persistence.Address, StringComparer.Ordinal);
        Assert.Equal("https://security-service:5104", baseline.Security.BaseAddress, StringComparer.Ordinal);

        Assert.Equal("https://localhost:5101", development.Persistence.Address, StringComparer.Ordinal);
        Assert.Equal("https://localhost:5104", development.Security.BaseAddress, StringComparer.Ordinal);

        // ONLY THE HOST MOVES. Scheme, port and the absence of any credential component are invariant
        // across the two environments, and each is asserted rather than assumed.
        foreach ((string deployed, string local) in ((string, string)[])
            [
                (baseline.Persistence.Address, development.Persistence.Address),
                (baseline.Security.BaseAddress, development.Security.BaseAddress),
            ])
        {
            Uri deployedUri = new(deployed);
            Uri localUri = new(local);

            Assert.Equal(deployedUri.Scheme, localUri.Scheme, StringComparer.Ordinal);
            Assert.Equal(deployedUri.Port, localUri.Port);
            Assert.Equal(Uri.UriSchemeHttps, localUri.Scheme, StringComparer.Ordinal);

            Assert.Equal(string.Empty, deployedUri.UserInfo);
            Assert.Equal(string.Empty, localUri.UserInfo);

            // A base address, so no query and no fragment either - both would be silently dropped or
            // silently appended depending on how a client composed a path onto it.
            Assert.Equal(string.Empty, deployedUri.Query);
            Assert.Equal(string.Empty, localUri.Query);
            Assert.Equal(string.Empty, deployedUri.Fragment);
            Assert.Equal(string.Empty, localUri.Fragment);
        }
    }

    /// <summary>
    /// Every resilience leaf is declared for both edges, and the two edges are declared identically.
    /// </summary>
    /// <param name="edge">The edge group name, <c>Persistence</c> or <c>Security</c>.</param>
    /// <param name="leaf">The setting name within the group.</param>
    /// <param name="declaredValue">The value the document must declare.</param>
    /// <remarks>
    /// <para>
    /// WHY THIS GROUP EXISTS AT ALL, STATED SO IT IS NOT MISTAKEN FOR AN IMPROVEMENT. An in-process call
    /// could not fail in transit and a network call can, so handling that failure mode is required BY the
    /// decomposition rather than layered on top of it (AAP 0.5.3). Without it, the first transient fault
    /// would surface as a defect the legacy could not have had - a regression introduced by the refactor
    /// rather than a preserved behaviour.
    /// </para>
    /// <para>
    /// NO PERFORMANCE CLAIM ATTACHES TO ANY VALUE BELOW. The repository publishes no service-level
    /// agreement, no latency budget, no throughput target and no availability commitment anywhere, so
    /// none may be asserted or used to justify a number (AAP 0.8.5). What is asserted is only that both
    /// edges are configured, configured identically, and configured in the document rather than left to
    /// a compiled default nobody can audit by reading a settings file.
    /// </para>
    /// <para>
    /// The two edges are SEPARATE groups so a deployment can still tune them apart; they are declared the
    /// same because nothing in the estate distinguishes them.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ResilienceLeaves))]
    public void EveryResilienceLeafIsDeclaredForBothEdges(
        string edge,
        string leaf,
        string declaredValue)
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();
        string relativePath = $"Resilience:{edge}:{leaf}";

        Assert.Equal(
            declaredValue,
            document[$"{DataServicesOptions.SectionName}:{relativePath}"],
            StringComparer.Ordinal);

        // AND IT BINDS. A declared leaf that addressed no member would leave the compiled default in
        // force while the document claimed otherwise - the silent failure mode this whole part exists
        // for.
        DataServicesOptions bound = DataServicesSettingsDocuments.BindService(document);

        Assert.NotNull(DataServicesSettingsDocuments.ReadBoundValue(bound, relativePath));

        // The overlay leaves transport-failure handling alone: it is not an environment concern, and
        // shortening a retry locally would hide from a developer the behaviour a deployed instance has.
        Assert.Null(
            DataServicesSettingsDocuments.DevelopmentOnly()[
                $"{DataServicesOptions.SectionName}:{relativePath}"]);
    }

    /// <summary>
    /// The fourteen resilience leaves: seven settings on each of the two edges, plus the stream deadline.
    /// </summary>
    /// <remarks>
    /// Both edges are enumerated from the options type's own group names so a row cannot address a group
    /// the type does not declare.
    /// </remarks>
    public static TheoryData<string, string, string> ResilienceLeaves()
    {
        (string Leaf, string Value)[] leaves =
        [
            (nameof(ClientResilienceOptions.MaxRetryAttempts), "3"),
            (nameof(ClientResilienceOptions.RetryBaseDelay), "00:00:02"),
            (nameof(ClientResilienceOptions.CircuitBreakerFailureRatio), "0.1"),
            (nameof(ClientResilienceOptions.CircuitBreakerMinimumThroughput), "100"),
            (nameof(ClientResilienceOptions.CircuitBreakerSamplingDuration), "00:00:30"),
            (nameof(ClientResilienceOptions.CircuitBreakerBreakDuration), "00:00:05"),
            (nameof(ClientResilienceOptions.RequestTimeout), "00:00:30"),
            (nameof(ClientResilienceOptions.StreamDeadline), "00:15:00"),
        ];

        TheoryData<string, string, string> rows = [];

        foreach (string edge in (string[])
            [nameof(ResilienceOptions.Persistence), nameof(ResilienceOptions.Security)])
        {
            foreach ((string leaf, string value) in leaves)
            {
                rows.Add(edge, leaf, value);
            }
        }

        return rows;
    }


    /// <summary>
    /// The listener declares exactly one endpoint - <c>https://+:5102</c> with
    /// <c>Protocols: Http1AndHttp2</c> - carrying the REST projection and the gRPC contracts together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PORT MAP, HONOURED WITHOUT A DEVIATION TO STATE. The plan's port map gives DataServices 5102
    /// and describes it as "gRPC primary plus a thin REST projection" (AAP 0.3.2.2). The delivered
    /// listener puts both on that one port: <c>https://+:5102</c> with <c>Protocols: Http1AndHttp2</c>, so
    /// ALPN selects HTTP/1.1 for <c>/health</c>, <c>/v1/ping</c> and the REST projection and HTTP/2 for
    /// contracts C-03 and C-04. A measured probe confirmed a single TLS endpoint of exactly this shape
    /// answers an HTTP/1.1 <c>GET</c> with 200 and a gRPC unary call over HTTP/2.
    /// </para>
    /// <para>
    /// SPLITTING THE TWO ACROSS SEPARATE ENDPOINTS IS THE TEMPTING SHAPE, AND IT IS NOT AVAILABLE. That
    /// would declare <c>https://+:5102</c> restricted to <c>Http1</c> for REST and a second
    /// <c>https://+:5112</c> restricted to <c>Http2</c> for gRPC, so each listener accepted only what it
    /// was for. The cost is that C-03 and C-04 would answer on 5112, a port AAP 0.3.2.2 never names, while
    /// 5102 - the port it assigns those contracts - carried only the probe. The assignment governs, so the
    /// two share one endpoint; 5103 stays unallocated (constraint C-D) because nothing moves into it, and
    /// the readiness chain that gates Gateway probes 5102 (constraint C-L).
    /// </para>
    /// <para>
    /// <c>+</c> binds EVERY interface, loopback included, which is why the Development overlay leaves
    /// this section entirely alone: a loopback probe already works, and narrowing the address to
    /// <c>localhost</c> would break the containerised development run, where a container reached by
    /// service name cannot bind loopback only and the readiness probe would never satisfy.
    /// </para>
    /// <para>
    /// THE ENDPOINT TERMINATES TLS in every environment, and that is asserted rather than only the port
    /// number - on cleartext, <c>Http1AndHttp2</c> silently means HTTP/1.1 alone, because Kestrel disables
    /// HTTP/2 without TLS. The scheme is therefore what makes the single-port arrangement work at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheListenerDeclaresOneEndpointCarryingBothProtocolVersionsOnTheDocumentedPort()
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();

        Assert.Equal("https://+:5102", document["Kestrel:Endpoints:Rest:Url"], StringComparer.Ordinal);
        Assert.Equal(
            "Http1AndHttp2",
            document["Kestrel:Endpoints:Rest:Protocols"],
            StringComparer.Ordinal);

        // EXACTLY ONE ENDPOINT, AND A SECOND ONE IS NAMED SO IT CANNOT ARRIVE QUIETLY. A
        // second listener would put a published contract on a port the map does not assign it, and
        // any third would be an unaccounted listening socket on a service whose whole inbound surface is
        // meant to be the two published contracts plus the probe pair.
        Assert.Equal(
            ["Rest"],
            document.GetSection("Kestrel:Endpoints")
                .GetChildren()
                .Select(static endpoint => endpoint.Key)
                .OrderBy(static key => key, StringComparer.Ordinal));

        Assert.Null(document["Kestrel:Endpoints:Grpc:Url"]);

        // THE RESERVED PHASE-TWO SLOT IS NOT TAKEN. 5103 is the port the attached environment had
        // assigned to a design service, and DesignSystem is precisely the deferred service this phase
        // must not implement even partially (constraint C-D). Leaving the port unallocated is what keeps
        // it available as the obvious Phase-2 slot.
        foreach (string endpoint in (string[])["Rest"])
        {
            Uri url = new(
                document[$"Kestrel:Endpoints:{endpoint}:Url"]!.Replace(
                    "+",
                    "placeholder-host",
                    StringComparison.Ordinal));

            Assert.NotEqual(5103, url.Port);
            Assert.Equal(5102, url.Port);
            Assert.Equal(Uri.UriSchemeHttps, url.Scheme, StringComparer.Ordinal);
        }

        // AND THE OVERLAY LEAVES THE WHOLE SECTION ALONE.
        Assert.False(DataServicesSettingsDocuments.DevelopmentOnly().GetSection("Kestrel").Exists());
    }

    /// <summary>
    /// Both server-held session kinds have their own lifetime and admission settings, and both are
    /// boundary-created rather than preserved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// NEITHER KEY HAS A LEGACY ANALOGUE, WHICH IS WHY THE OVERLAY MAY WIDEN THEM. In the legacy library
    /// both kinds of state lived inside an object whose lifetime was the hosting control's, so neither
    /// needed a lifetime of its own: the validation chain's four pieces of cross-event mutable state were
    /// private fields on the DataWindow service extension
    /// [ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L89-L96], and the expression engine's
    /// cross-DataWindow references were LIVE OBJECT POINTERS
    /// [ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_columnexp.sru:L80-L83]. A stateless
    /// request boundary has nowhere to put either, so both became server-held state correlated by an
    /// identifier - and anything server-held needs a lifetime, because a client that never closes its
    /// session must not retain it forever (AAP 0.6.1.3, 0.6.2.3).
    /// </para>
    /// <para>
    /// So widening the idle window in the overlay cannot be a behaviour change under constraint C-B:
    /// there is no legacy value to diverge from. That is a categorically different permission from the
    /// one the five preserved-default groups have, and it is the reason these two keys appear in the
    /// overlay while those groups may not.
    /// </para>
    /// <para>
    /// The two ADMISSION ceilings are deliberately NOT overlaid - a developer opens a handful of
    /// sessions, so the ceiling is never the thing in the way.
    /// </para>
    /// </remarks>
    [Fact]
    public void BothSessionKindsDeclareTheirOwnLifetimeAndAdmissionSettings()
    {
        DataServicesOptions baseline =
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base());

        Assert.Equal(
            SessionLifetimeOptions.DefaultIdleTimeout,
            baseline.Sessions.ValidationSession.IdleTimeout);
        Assert.Equal(
            SessionLifetimeOptions.DefaultIdleTimeout,
            baseline.Sessions.ExpressionSession.IdleTimeout);

        Assert.Equal(100, baseline.Sessions.ValidationSession.MaxConcurrentSessions);
        Assert.Equal(100, baseline.Sessions.ExpressionSession.MaxConcurrentSessions);

        // TWO INDEPENDENT GROUPS, NOT ONE SHARED INSTANCE. Asserted by identity because a shared
        // instance would make the two session kinds impossible to tune apart - and would silently couple
        // an expression session's lifetime to a validation session's.
        Assert.NotSame(baseline.Sessions.ValidationSession, baseline.Sessions.ExpressionSession);

        // THE OVERLAY WIDENS BOTH WINDOWS AND TOUCHES NEITHER CEILING.
        IConfigurationRoot overlay = DataServicesSettingsDocuments.DevelopmentOnly();

        Assert.Equal(
            TimeSpan.FromHours(1),
            TimeSpan.Parse(
                overlay[
                    $"{DataServicesOptions.SectionName}:Sessions:ValidationSession:IdleTimeout"]!,
                CultureInfo.InvariantCulture));
        Assert.Equal(
            TimeSpan.FromHours(1),
            TimeSpan.Parse(
                overlay[
                    $"{DataServicesOptions.SectionName}:Sessions:ExpressionSession:IdleTimeout"]!,
                CultureInfo.InvariantCulture));

        Assert.Null(
            overlay[
                $"{DataServicesOptions.SectionName}:Sessions:ValidationSession:MaxConcurrentSessions"]);
        Assert.Null(
            overlay[
                $"{DataServicesOptions.SectionName}:Sessions:ExpressionSession:MaxConcurrentSessions"]);

        // AND THE WIDENED WINDOW IS WHAT A DEVELOPER HOST ACTUALLY BINDS.
        DataServicesOptions development = DataServicesSettingsDocuments.BindService(
            DataServicesSettingsDocuments.WithDevelopmentOverlay());

        Assert.Equal(TimeSpan.FromHours(1), development.Sessions.ValidationSession.IdleTimeout);
        Assert.Equal(TimeSpan.FromHours(1), development.Sessions.ExpressionSession.IdleTimeout);
        Assert.Equal(100, development.Sessions.ValidationSession.MaxConcurrentSessions);
    }

    /// <summary>
    /// The three sections the framework binds rather than the service's own options types are each
    /// declared, and each is declared the way its own consumer requires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY THESE ARE ASSERTED HERE RATHER THAN LEFT AS APPARENT ORPHANS. <c>Logging</c>,
    /// <c>AllowedHosts</c> and <c>Kestrel</c> bind framework option types - logger filtering, host
    /// filtering and the listener - rather than anything in <c>Configuration/DataServicesOptions.cs</c>.
    /// The key-for-key census two rows down deliberately scopes itself to the service's own section, so
    /// without this row these three would read as keys nobody checks. The listener has its own row
    /// because it is the most load-bearing technical setting in the document.
    /// </para>
    /// <para>
    /// ⚠ <c>AllowedHosts</c> IS OPEN DELIBERATELY, AND SAYING SO IS PART OF NOT MISREADING IT LATER.
    /// Inside the orchestration network this service is reached by its service name, by the health probe
    /// under whatever name that probe uses, and by a developer run over loopback - a host allow-list
    /// would reject one of those three and the failure would present as an unexplained 400 on a readiness
    /// probe. Host filtering is not the control that protects this boundary: every route other than the
    /// anonymous readiness probe requires a bearer credential verified against Security's published
    /// material, which is a control on the CALLER'S IDENTITY rather than on a header the caller chooses
    /// (constraint C-G).
    /// </para>
    /// <para>
    /// THE DIAGNOSTIC LEVELS DECIDE WHETHER A RECORD IS WRITTEN AND NEVER WHETHER ITS CONTENT IS SAFE.
    /// The overlay raises three of them for a developer console, and raising a level grants no
    /// permission: the redaction obligation survives every level here intact, because this service relays
    /// the structured database error of the persistence contract whose statement field carries the
    /// complete generated statement including interpolated literal values - the legacy transaction layer
    /// reads a bind-disabling flag out of its connection parameters
    /// [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129] and the legacy logger
    /// performed no redaction whatsoever. What is asserted here is only that the levels are declared and
    /// that the overlay raises rather than silences.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheThreeFrameworkBoundSectionsAreDeclaredAsTheirConsumersRequire()
    {
        IConfigurationRoot document = DataServicesSettingsDocuments.Base();
        IConfigurationRoot overlay = DataServicesSettingsDocuments.DevelopmentOnly();

        // HOST FILTERING - open, deliberately, for the reason recorded above.
        Assert.Equal("*", document["AllowedHosts"], StringComparer.Ordinal);

        // And the overlay does not narrow it, which would break the containerised developer run where
        // this overlay also loads and the service is reached by name rather than by loopback.
        Assert.Null(overlay["AllowedHosts"]);

        // LOGGER FILTERING - the base document's four categories.
        Assert.Equal("Information", document["Logging:LogLevel:Default"], StringComparer.Ordinal);
        Assert.Equal("Warning", document["Logging:LogLevel:Microsoft.AspNetCore"], StringComparer.Ordinal);
        Assert.Equal(
            "Warning",
            document["Logging:LogLevel:System.Net.Http.HttpClient"],
            StringComparer.Ordinal);
        Assert.Equal("Warning", document["Logging:LogLevel:Grpc"], StringComparer.Ordinal);

        // THE OVERLAY RAISES AND NEVER SILENCES. Asserted as a property rather than value by value: no
        // category may be moved to a level that emits LESS than the base file's, because a developer who
        // saw fewer records than a deployed instance would be debugging a different service.
        string[] ascending =
            ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

        foreach ((string key, string? developmentLevel) in
            DataServicesSettingsDocuments.Leaves(overlay))
        {
            if (!key.StartsWith("Logging:LogLevel:", StringComparison.Ordinal))
            {
                continue;
            }

            string? deployedLevel = document[key];

            if (deployedLevel is null)
            {
                // A category the base file does not name at all - the overlay may add one, and adding one
                // cannot silence anything.
                Assert.NotNull(developmentLevel);
                continue;
            }

            Assert.True(
                Array.IndexOf(ascending, developmentLevel)
                    <= Array.IndexOf(ascending, deployedLevel),
                $"'{key}' is '{developmentLevel}' in the Development overlay and '{deployedLevel}' in "
                    + "the base document, so a developer run emits FEWER records than a deployed one for "
                    + "this category. The overlay exists to make a local run more legible, never less.");
        }
    }

    /// <summary>
    /// Every key either document declares under the service section resolves to a real member.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE QUIETEST FAILURE MODE IN CONFIGURATION, CLOSED. The binder IGNORES a key it cannot map: a
    /// group renamed, a leaf pluralised, a segment misspelled, and the document says one thing while the
    /// service runs on its compiled default. Nothing throws, nothing logs, startup validation passes -
    /// because from the binder's point of view the key simply is not there. An operator reading the file
    /// has no way to tell.
    /// </para>
    /// <para>
    /// Walking every declared leaf and resolving it against the bound object graph turns that silence
    /// into a failure with the offending path named. It is a TOTAL check rather than a list: a group
    /// added to the document tomorrow is walked without anyone remembering to extend this test, which is
    /// the only way a census stays honest.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryDeclaredKeyUnderTheServiceSectionResolvesToARealMember()
    {
        string prefix = $"{DataServicesOptions.SectionName}:";

        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            DataServicesOptions bound = DataServicesSettingsDocuments.BindService(document);

            foreach ((string key, _) in DataServicesSettingsDocuments.Leaves(document))
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relativePath = key[prefix.Length..];

                // Throws with the offending segment named when a path addresses nothing, which is
                // exactly the diagnostic a silent bind failure denies its reader.
                _ = DataServicesSettingsDocuments.ReadBoundValue(bound, relativePath);
            }
        }
    }

    /// <summary>
    /// The same census for the inbound-token section, whose keys the stock handler ultimately consumes.
    /// </summary>
    /// <remarks>
    /// Held apart from the row above because the section is a SEPARATE bindable root at a top-level path
    /// by ASP.NET Core convention, not a group under the service's own section - folding it in would move
    /// its path and silently break the document that declares it.
    /// </remarks>
    [Fact]
    public void EveryDeclaredKeyUnderTheInboundTokenSectionResolvesToARealMember()
    {
        string prefix = $"{JwtAuthenticationOptions.SectionName}:";

        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            JwtAuthenticationOptions bound = DataServicesSettingsDocuments.BindJwt(document);

            foreach ((string key, _) in DataServicesSettingsDocuments.Leaves(document))
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relativePath = key[prefix.Length..];

                // The caller roster is a collection, so its leaves are indexed - `PermittedCallers:0`.
                // The index is not a property name, so the walk stops at the collection itself, which is
                // the member the key genuinely addresses.
                int separator = relativePath.IndexOf(':', StringComparison.Ordinal);
                string memberPath = separator < 0 ? relativePath : relativePath[..separator];

                Assert.NotNull(
                    typeof(JwtAuthenticationOptions).GetProperty(
                        memberPath,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));
            }
        }
    }


    // ==============================================================================================
    //  THE NEGATIVES - WHAT NEITHER DOCUMENT MAY CONTAIN, ASSERTED AS ABSENCE
    // ==============================================================================================

    /// <summary>
    /// Credential material of every recognised shape, absent from both documents including their comments.
    /// </summary>
    /// <param name="marker">A token that appears in credential material and not in English prose.</param>
    /// <remarks>
    /// <para>
    /// SCANNED OVER THE RAW TEXT, COMMENTS INCLUDED, AND THE RAWNESS IS THE REQUIREMENT. Constraint C-F
    /// forbids a credential "not as a default, not as an example, not in a comment", and both these
    /// documents carry long explanatory comment blocks - which is exactly where a well-meaning author
    /// pastes a sample key to illustrate a format. A scan that read only bound values would step straight
    /// past it.
    /// </para>
    /// <para>
    /// THE REPOSITORY-WIDE SWEEP IS WHY THE MARKER LIST IS SHAPED THIS WAY RATHER THAN NAMING THREE
    /// SITES. The requirements named three hardcoded secrets and said explicitly that three is a floor
    /// and not a ceiling; the sweep found EIGHT in-source sites plus three inside vendored binaries
    /// (AAP 0.6.6.1). Every one of the eight sits in a test, demo or legacy-browser-asset region - the
    /// "it is only for development" category - and one of them is a plaintext PEM private key used to
    /// sign a JWS in a page whose whole purpose was demonstration
    /// [tests/blink/test_jws.htm:L8-L22]. Reproducing that pattern in a settings file is the single most
    /// predictable way to reintroduce what this refactor exists to remediate, so the scan is written to
    /// catch the SHAPE rather than the known values - and no value from any of those sites is reproduced
    /// here, in keeping with the never-replicate posture the read-only legacy rule forces.
    /// </para>
    /// <para>
    /// EVERY MARKER WAS CHOSEN TO BE UNSPEAKABLE IN PROSE, AND THE COMPARISON IS CASE-SENSITIVE FOR THE
    /// SAME REASON. Both documents legitimately contain the WORDS "secret", "password" and "signing" in
    /// their explanations of why no such value is present, so a word-based scan would fail on its own
    /// documentation. These markers are structural instead: a PEM armour line, a DER prefix, a provider
    /// key prefix, a compact-token header, or an environment variable name.
    /// </para>
    /// <para>
    /// ⚠ MEASURED: A CASE-INSENSITIVE SCAN FALSE-POSITIVES ON THIS SERVICE'S OWN DOCUMENTATION, WHICH IS
    /// WHY ORDINAL IS THE SHARPER RULE RATHER THAN THE LOOSER ONE. Matching while ignoring case is the
    /// tempting choice, on the reasoning that a credential smuggled in unusual casing would still be a
    /// credential. It fails on
    /// <c>services/dataservices-service/PowerFramework.DataServices/appsettings.json:L796</c>, where the
    /// phrase "private key" appears inside the sentence explaining why this service holds none - so the
    /// looser rule cannot distinguish the prohibition from the thing prohibited. Every token below has
    /// FIXED casing by specification - PEM armour is
    /// upper case, a DER prefix is a base64 rendering, and a provider prefix is exactly as its issuer
    /// mints it - so ordinal comparison is the correct one, not a concession. A credential written in
    /// unusual casing would not be a working credential.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("-----BEGIN")]
    [InlineData("PRIVATE KEY-----")]
    [InlineData("BEGIN CERTIFICATE")]
    [InlineData("SIGNING_KEY")]
    [InlineData("MIIB")]
    [InlineData("MIIE")]
    [InlineData("eyJ")]
    [InlineData("AKIA")]
    [InlineData("ASIA")]
    [InlineData("ghp_")]
    [InlineData("github_pat_")]
    [InlineData("xoxb-")]
    [InlineData("xoxp-")]
    [InlineData("AIza")]
    [InlineData("sk_live_")]
    [InlineData("sk_test_")]
    [InlineData("pk_live_")]
    public void NeitherSettingsDocumentCarriesCredentialMaterialOfAnyRecognisedShape(string marker)
    {
        foreach (string path in (string[])
            [DataServicesSettingsDocuments.BasePath, DataServicesSettingsDocuments.DevelopmentPath])
        {
            Assert.DoesNotContain(
                marker,
                DataServicesSettingsDocuments.RawText(path),
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The system's one signing secret is not named, defaulted or hinted at in either document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EXACTLY ONE SIGNING SECRET EXISTS IN THE WHOLE SYSTEM AND SECURITY HOLDS IT (AAP 0.6.6.3).
    /// Security mints short-lived tokens and publishes verification material at the standard published
    /// key-set path; the other three services hold verification settings only and validate with the
    /// framework's stock bearer handler. A service with no reason to name the signing variable has no
    /// reason to be able to read it, so the name itself is absent here rather than merely unused - which
    /// is the difference between a capability that is unreachable and one that is currently unexercised.
    /// </para>
    /// <para>
    /// THE FLAT ISSUANCE KEY IS A DIFFERENT THING AND IS DELIBERATELY EXEMPT FROM THE VALUE CHECK BELOW.
    /// <see cref="SecurityClientOptions.ClientSecretConfigurationKey"/> is the NAME of the top-level key
    /// this service's issuance credential arrives under. It mints nothing and signs nothing: it proves to
    /// Security which caller is asking, on the one operation a bearer token cannot protect. The base
    /// document names the key in a comment and cannot supply a value for it even if someone tried - the
    /// key is flat rather than sectioned precisely because the environment-variable provider maps only a
    /// double underscore onto the section separator, so no section in this document can reach it. That
    /// arrangement is what lets the value be REQUIRED while nothing leaks in a committed file.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSystemsOneSigningSecretIsNotNamedInEitherDocument()
    {
        foreach (string path in (string[])
            [DataServicesSettingsDocuments.BasePath, DataServicesSettingsDocuments.DevelopmentPath])
        {
            string text = DataServicesSettingsDocuments.RawText(path);

            // The environment variable Security's signing key arrives under, in the spelling the
            // orchestration layer uses. Absent entirely, comments included.
            Assert.DoesNotContain("SECURITY_JWT_SIGNING_KEY", text, StringComparison.OrdinalIgnoreCase);

            // And any per-service spelling of the same convention, so a renamed variable cannot slip in.
            Assert.DoesNotContain("_JWT_SIGNING_KEY", text, StringComparison.OrdinalIgnoreCase);
        }

        // THE FLAT ISSUANCE KEY CARRIES NO VALUE IN EITHER DOCUMENT, which is asserted through the
        // configuration providers rather than through the text: naming the key in a comment is correct and
        // documented, DECLARING it would not be.
        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            Assert.Null(document[SecurityClientOptions.ClientSecretConfigurationKey]);
        }

        // AND BINDING THE DOCUMENT LEAVES THE PROPERTY EMPTY. The composition root fills it from the flat
        // key with an explicit post-configure step; the document contributes nothing.
        DataServicesOptions bound = DataServicesSettingsDocuments.BindService(
            DataServicesSettingsDocuments.WithDevelopmentOverlay());

        Assert.Equal(string.Empty, bound.Security.ClientSecret);
    }

    /// <summary>
    /// No configured value in either document is a key blob, a certificate, or an embedded credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE VALUE-SIDE HALF OF THE SCAN, WALKED RATHER THAN LISTED. Every leaf of both documents is
    /// examined, so a group added tomorrow is covered without anyone extending this test. What it looks
    /// for is material rather than a name: a long run of base64 alphabet is what a key, a certificate or
    /// a compact token looks like once armour and prefixes are stripped, and no legitimate value in
    /// either document comes close - the longest are hostnames, an audience name and a time span.
    /// </para>
    /// <para>
    /// A CREDENTIAL COMPONENT INSIDE AN ADDRESS IS TREATED AS MATERIAL, because it is: an address carrying
    /// user information is copied verbatim into every log record, exception and trace that captures a
    /// request URI, which turns one settings entry into an unbounded number of disclosures. The options
    /// validator refuses one on either upstream address; this asserts the same property over every value
    /// in the documents, including the ones no validator inspects.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoConfiguredValueInEitherDocumentIsCredentialMaterial()
    {
        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            foreach ((string key, string? value) in DataServicesSettingsDocuments.Leaves(document))
            {
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                // A BASE64-SHAPED RUN OF THIRTY-TWO OR MORE CHARACTERS. Thirty-two bytes is the size the
                // orchestration layer generates a signing secret at, so anything at or above that length
                // in that alphabet is treated as material regardless of what its key is called.
                int run = 0;

                foreach (char character in value)
                {
                    bool isBase64Alphabet =
                        char.IsAsciiLetterOrDigit(character) || character is '+' or '/' or '=';

                    run = isBase64Alphabet ? run + 1 : 0;

                    Assert.True(
                        run < 32,
                        $"'{key}' carries a base64-shaped run of {run} characters, which is the shape of "
                            + "a key, a certificate or a compact token. No credential material may "
                            + "appear in a settings document in any form (constraint C-F).");
                }

                // AN EMBEDDED CREDENTIAL INSIDE AN ADDRESS. Checked on every value that parses as an
                // absolute URI rather than only on the two the validator inspects.
                if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
                {
                    Assert.Equal(string.Empty, uri.UserInfo);
                }

                // A CREDENTIAL-SHAPED KEY WITH A NON-EMPTY VALUE, which is the one shape a length or
                // alphabet test would miss entirely: a short password is still a password. The rule is
                // categorical rather than heuristic - a key whose NAME says it carries a credential may
                // not carry a VALUE in a committed document, whatever that value looks like. The
                // descriptor's write-only password field and the mutual-TLS paths are the members this
                // covers, and all three are declared empty for exactly this reason.
                //
                // ⚠ MEASURED: A BOOLEAN VALUE IS EXCLUDED, AND THE EXCLUSION IS THE SAME ONE THE
                // TYPE-LEVEL CENSUS MAKES. Without it this check failed on
                // `Authentication:Jwt:ValidateIssuerSigningKey`, whose declared value is `true` - a
                // verification SWITCH deciding whether the inbound signature is checked against the
                // published key, which is the opposite of holding one and is precisely the setting that
                // must stay true. A boolean cannot carry material, so narrowing to value-bearing leaves
                // keeps the check meaningful rather than blunting it.
                bool isBooleanValue =
                    bool.TryParse(value, out bool _);

                if (!isBooleanValue)
                {
                    string leafName = key[(key.LastIndexOf(':') + 1)..];

                    foreach (string credentialMarker in (string[])
                        ["Secret", "Password", "Passphrase", "Credential", "Token", "SigningKey"])
                    {
                        Assert.False(
                            leafName.Contains(credentialMarker, StringComparison.OrdinalIgnoreCase),
                            $"'{key}' has a credential-shaped name and a non-empty value. Every secret "
                                + "in this system arrives from the orchestration secret layer through the "
                                + "options pattern, never from a literal in source or in a settings "
                                + "document (constraint C-F).");
                    }
                }
            }
        }
    }

    /// <summary>
    /// No option belonging to a deferred service is provisioned, and no key names one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-D IS ABSOLUTE AND IT IS WORTH RESTATING IN CONFIGURATION TERMS. DesignSystem,
    /// Documents, Integration and ScriptBridge receive no code, no test, no container and no partial
    /// implementation in this phase - and a settings key is a partial implementation. It declares that a
    /// capability is on the way, invites an operator to fill it in, and gives a future reader a reason to
    /// believe something behind it exists. A half-built deferred service is worse than a documented gap,
    /// and a settings key is the cheapest possible way to half-build one.
    /// </para>
    /// <para>
    /// THE ATTACHED ENVIRONMENT'S OWN ROSTER IS WHY THIS IS CHECKED RATHER THAN ASSUMED. It named five
    /// service directories and five per-service signing variables, two of which - a design service and a
    /// localization service - belong to no service in the delivered roster at all: the first is precisely
    /// the deferred DesignSystem, and the second's capability content is a shared LIBRARY rather than a
    /// service. Neither is provisioned, here or anywhere (AAP 0.5.4.3, 0.8.3).
    /// </para>
    /// <para>
    /// The deferred capabilities are reachable only as four reserved routes on Gateway's routing metadata,
    /// each answering not-implemented with a machine-readable body. A route declaration is not a stub;
    /// a configuration key would be closer to one.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoOptionBelongingToADeferredServiceIsProvisioned()
    {
        string[] deferredMarkers =
        [
            "design", "designsystem", "theme", "colour", "color", "dpi", "font", "canvas", "painter",
            "popupmenu", "tooltip", "trayicon", "win32", "localization-service", "i18n-service",
            "documents", "zip", "barcode", "xmldoc", "filescanner",
            "integration", "ftp", "websocket", "mqtt", "proxy",
            "scripting", "scriptbridge", "sciter", "blink", "webview", "compiler",
        ];

        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            foreach ((string key, string? value) in DataServicesSettingsDocuments.Leaves(document))
            {
                foreach (string marker in deferredMarkers)
                {
                    Assert.False(
                        key.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"'{key}' names a capability of a deferred service. DesignSystem, Documents, "
                            + "Integration and ScriptBridge receive no implementation in this phase, not "
                            + "even a settings key (constraint C-D).");

                    Assert.False(
                        value?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true,
                        $"'{key}' has a value naming a capability of a deferred service, which would "
                            + "point this service at something that does not exist (constraint C-D).");
                }
            }
        }

        // AND THE ONE LOCALIZATION SETTING THAT DOES EXIST IS NOT A DEFERRED SERVICE. It selects among the
        // three providers of the SHARED localization library, which is in scope precisely because the
        // DataWindow validation path routes its diagnostics through it - so the key is named for the
        // capability rather than for a service, and it addresses a project reference rather than an
        // address.
        Assert.NotNull(
            DataServicesSettingsDocuments.Base()[
                $"{DataServicesOptions.SectionName}:Localization:Locale"]);
        Assert.Null(
            DataServicesSettingsDocuments.Base()[
                $"{DataServicesOptions.SectionName}:Localization:Address"]);
    }

    /// <summary>
    /// There is no storage configuration of any kind, in either document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-E, ASSERTED WHERE A FABRICATED DATABASE WOULD FIRST APPEAR. Persistence is the ONLY
    /// service in the refactor that generates or executes SQL and the only one holding a storage
    /// provider; DataServices reaches data solely over the generated persistence.v1 gRPC client, addressed
    /// by one value. A connection string here would not merely be redundant, it would fabricate a second
    /// path to data - and the repository evidences exactly one storage engine and exactly one schema, so a
    /// second path would have nothing behind it.
    /// </para>
    /// <para>
    /// ⚠ ONE VALUE LOOKS LIKE STORAGE CONFIGURATION AND IS NOT, WHICH IS WHY IT IS HANDLED BY NAME.
    /// <c>DataServices:PersistenceSession:Dbms</c> is <c>SQLite</c>. It is a field of the TRANSACTION
    /// DESCRIPTOR this service sends to Persistence on the session request - the descriptor mirrors the
    /// legacy transaction structure field for field
    /// [ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs] - and Persistence substring-tests it to
    /// select its paging dialect. So it is a wire field and a dialect selector, not a connection: no
    /// data source accompanies it, no provider is named, no file is referenced, and this service opens
    /// nothing. The row below asserts precisely that by requiring every OTHER descriptor field to be
    /// empty in both documents.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThereIsNoStorageConfigurationOfAnyKindInEitherDocument()
    {
        string[] storageMarkers =
        [
            "ConnectionString", "ConnectionStrings", "DataSource", "Data Source", "Initial Catalog",
            "Provider=", "Integrated Security", "Uid=", "Pwd=", "mode=rwc", ".sqlite", ".db3",
            "EntityFramework", "DbContext", "Migrations",
        ];

        foreach (string path in (string[])
            [DataServicesSettingsDocuments.BasePath, DataServicesSettingsDocuments.DevelopmentPath])
        {
            string text = DataServicesSettingsDocuments.RawText(path);

            foreach (string marker in storageMarkers)
            {
                Assert.DoesNotContain(marker, text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // NO SECTION NAMED FOR STORAGE, EITHER - the conventional path a provider would look under.
        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.DevelopmentOnly()])
        {
            Assert.False(document.GetSection("ConnectionStrings").Exists());
            Assert.False(
                document.GetSection($"{DataServicesOptions.SectionName}:Database").Exists());
            Assert.False(
                document.GetSection($"{DataServicesOptions.SectionName}:Storage").Exists());
        }
    }

    /// <summary>
    /// The transaction descriptor carries a dialect name and nothing else - no server, no database, no
    /// account, no password, no connection parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THIS IS THE OTHER HALF OF THE C-E ARGUMENT, AND IT IS THE HALF THAT COULD ACTUALLY GO WRONG. The
    /// descriptor mirrors the legacy structure field for field, so it HAS a server name, a database name,
    /// a login identifier, a login password and a provider parameter string. Every one of them is empty in
    /// the shipped documents, and each empty is correct for a reason worth recording rather than an
    /// oversight:
    /// </para>
    /// <para>
    /// The only evidenced storage engine takes no password on the unencrypted path, and the encrypted path
    /// is out of Phase-1 scope - the shipped cipher library is materially older than the plain one and its
    /// key-derivation and per-page integrity options are not reachable through any framework API, so
    /// page-format parity cannot be reproduced (AAP 0.6.4). The provider parameter string is empty
    /// because it is the single authority for the two connection flags and can carry a whole connection
    /// string, which is exactly why it must not carry one from here.
    /// </para>
    /// <para>
    /// <c>LogPass</c> IS WRITE-ONLY. The legacy structure carries it and the contract carries it, but the
    /// response view reserves its field number permanently so it can never appear on an answer. Empty in
    /// source, empty in the document, and injected from the orchestration secret layer by a deployment
    /// that needs one.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTransactionDescriptorCarriesADialectNameAndNoMaterial()
    {
        foreach (IConfigurationRoot document in (IConfigurationRoot[])
            [DataServicesSettingsDocuments.Base(), DataServicesSettingsDocuments.WithDevelopmentOverlay()])
        {
            PersistenceSessionOptions descriptor =
                DataServicesSettingsDocuments.BindService(document).PersistenceSession;

            // The dialect selector, present and non-blank because Persistence falls back to a dialect
            // rather than failing when it is unset.
            Assert.Equal("SQLite", descriptor.Dbms, StringComparer.Ordinal);

            // Everything that would make it a connection rather than a dialect name: empty, every one.
            Assert.Equal(string.Empty, descriptor.ServerName);
            Assert.Equal(string.Empty, descriptor.Database);
            Assert.Equal(string.Empty, descriptor.LogId);
            Assert.Equal(string.Empty, descriptor.LogPass);
            Assert.Equal(string.Empty, descriptor.DbParm);
            Assert.Equal(string.Empty, descriptor.Lock);
            Assert.Equal(string.Empty, descriptor.UserParm);

            // AUTO-COMMIT OFF. The safe arm: a session that committed each statement on its own would
            // make the transaction contract's commit and rollback calls meaningless, and would break the
            // optimistic-concurrency protocol the update contract carries.
            Assert.False(descriptor.AutoCommit);
        }
    }
}


// ==================================================================================================
//  PART THREE - THE COMPOSITION ROOT'S SIDE OF THE CONTRACT
//  ------------------------------------------------------------------------------------------------
//  Part One tests the types, Part Two tests the documents. Neither can answer the two questions that
//  matter most about a preserved default:
//
//    (1) IS IT ACTUALLY OVERRIDABLE? The plan's resolution for the named User Example is precise -
//        the observable default is reproduced while the UN-CONFIGURABILITY is not, because
//        un-configurability is a structural property rather than a behaviour (AAP 0.8.2). Half of
//        that resolution is the value; the other half is that a deployment can change it. A test that
//        asserted only the value would pass against an implementation that had hardcoded it just as
//        firmly as `pfw.sra:L94` did - reproducing the defect instead of the behaviour.
//
//    (2) DOES A STRUCTURAL FAULT STILL TERMINATE? The legacy's posture is not advisory. Its
//        systemerror handler unpacks a seven-field assertion payload split on a carriage-return and
//        line-feed pair and then executes `HALT CLOSE`
//        [ws_objects/pfw.pbl.src/pfw.sra:L111-L144, terminating at :L143] - it ends the application.
//        The .NET equivalent is fail-fast startup validation with process termination, and
//        SOFTENING IT INTO WARN-AND-CONTINUE WOULD BE A BEHAVIOURAL CHANGE DRESSED AS ROBUSTNESS
//        (AAP 0.1.4). A service that started with no upstream address would accept requests and fail
//        every one, at a point far harder to attribute than startup.
//
//  Both questions are about the composition root, so this part exercises it: the registration group
//  directly where that is sharper, and a real host where only a real host will do.
// ==================================================================================================

/// <summary>
/// The composition root's side of the configuration contract: overridability, provider selection, and
/// the fail-fast posture.
/// </summary>
public sealed class DataServicesOptionsCompositionTests
{
    /// <summary>
    /// A minimal configuration that binds and validates, for isolating one change at a time.
    /// </summary>
    /// <param name="overrides">Paths and values layered over the shipped base document.</param>
    /// <returns>The configuration root.</returns>
    /// <remarks>
    /// Built on the SHIPPED base document rather than on a hand-written dictionary, so a row that
    /// changes one key is observing the real precedence chain against the real declared defaults. The
    /// issuance credential is supplied because the validator refuses a deployment that can present
    /// neither of the two credentials the token contract accepts - and it is generated per test process
    /// rather than written down, for the same reason the deployed one is (constraint C-F).
    /// </remarks>
    private static IConfigurationRoot Configuration(
        params KeyValuePair<string, string?>[] overrides)
    {
        List<KeyValuePair<string, string?>> layered =
        [
            new(SecurityClientOptions.ClientSecretConfigurationKey, GeneratedIssuanceCredential),
            .. overrides,
        ];

        return DataServicesSettingsDocuments.BaseWithOverrides(layered);
    }

    /// <summary>
    /// A placeholder issuance credential, composed at run time and never written down.
    /// </summary>
    /// <remarks>
    /// NOT A LITERAL, DELIBERATELY. A credential-shaped literal in a test file is a credential-shaped
    /// string in version control, and a scanner cannot tell one from a real one - which is exactly the
    /// pattern the repository-wide sweep found eight times over. No assertion in this file reads its
    /// value; what is asserted is the effect of its presence or absence.
    /// </remarks>
    private static readonly string GeneratedIssuanceCredential =
        Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    /// <summary>Builds a provider over the registration group under test.</summary>
    /// <param name="configuration">The configuration the group binds from.</param>
    /// <returns>The provider, which the caller disposes.</returns>
    /// <remarks>
    /// THE REAL REGISTRATION GROUP, NOT A RECONSTRUCTION OF IT. <c>AddDataServicesOptions</c> and
    /// <c>AddDataServicesLocalization</c> are the composition root's own methods, visible to this project
    /// through the internals item in the service project's project file. Calling them directly is what
    /// makes an assertion about startup validation an assertion about the shipped wiring rather than about
    /// a copy of it - and it costs no host, so a table of rows stays cheap.
    /// </remarks>
    private static ServiceProvider Compose(IConfiguration configuration)
    {
        ServiceCollection services = new();

        _ = services.AddDataServicesOptions(configuration);
        _ = services.AddDataServicesLocalization();

        return services.BuildServiceProvider();
    }

    // ==============================================================================================
    //  THE NAMED USER EXAMPLE - BOTH HALVES OF ITS RESOLUTION
    // ==============================================================================================

    /// <summary>
    /// The locale default reaches a running host as <c>en</c>, and the host resolves the English
    /// provider.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OBSERVABLE HALF OF THE PRESERVED DEFECT, ASSERTED WHERE IT IS OBSERVABLE. The legacy assigns
    /// <c>lang = "en"</c> as a literal inside the framework application's open event
    /// [ws_objects/pfw.pbl.src/pfw.sra:L94] and immediately switches on it to create one of three
    /// provider classes [:L95-L102], installing the result at [:L103]. What a running framework
    /// EXHIBITED was English, and that is what a running host must exhibit with no configuration
    /// supplied.
    /// </para>
    /// <para>
    /// A real host is used rather than a bound options object because the claim is about the whole path:
    /// the document declares the value, the binder binds it, the composition root maps it onto a provider
    /// type, and the container hands that provider to the DataWindow validation surface that routes its
    /// diagnostics through it [se_cst_dw.sru:L357, :L368]. Any one of those four links could be broken
    /// while the other three looked correct.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLocaleDefaultReachesARunningHostAndSelectsTheEnglishProvider()
    {
        using DataServicesTestHostFactory factory = new();
        using HttpClient client = factory.CreateClient();

        DataServicesOptions options =
            factory.Services.GetRequiredService<IOptions<DataServicesOptions>>().Value;

        Assert.Equal("en", options.Localization.Locale, StringComparer.Ordinal);

        // THE PROVIDER, NOT JUST THE TOKEN. A locale string that bound correctly but selected the wrong
        // provider would emit every framework message in a language nobody asked for while this option
        // read as correct.
        II18nProvider provider = factory.Services.GetRequiredService<II18nProvider>();

        Assert.IsType<EnglishProvider>(provider);
    }

    /// <summary>
    /// The locale is genuinely overridable: a later configuration provider changes the bound value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE OTHER HALF OF THE RESOLUTION, AND THE HALF A VALUE ASSERTION CANNOT REACH. The DEFECT at
    /// <c>pfw.sra:L94</c> is the hardcoding, not the value: a framework whose locale could only be
    /// changed by editing and recompiling its application object. The plan's resolution keeps the value
    /// and removes the un-configurability, because un-configurability is a structural property rather
    /// than a behaviour (AAP 0.8.2). So this row is not a nicety beside the default - it is the second
    /// half of the same requirement, and without it an implementation that hardcoded <c>en</c> just as
    /// firmly as the legacy did would pass every other assertion in this file.
    /// </para>
    /// <para>
    /// Asserted through the REAL override channel: an in-memory provider added after the JSON file, which
    /// is the same last-writer-wins precedence the environment-variable provider relies on when a
    /// container manifest supplies <c>DataServices__Localization__Locale</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLocaleIsOverridableByALaterConfigurationProvider()
    {
        DataServicesOptions overridden = DataServicesSettingsDocuments.BindService(
            Configuration(
                new KeyValuePair<string, string?>(
                    $"{DataServicesOptions.SectionName}:Localization:Locale",
                    "cht")));

        Assert.Equal("cht", overridden.Localization.Locale, StringComparer.Ordinal);

        // AND THE SHIPPED DOCUMENT IS UNCHANGED BY THAT, which is what makes the override an override
        // rather than an edit. Stated because the two are easy to conflate when one test does both.
        Assert.Equal(
            "en",
            DataServicesSettingsDocuments.Base()[
                $"{DataServicesOptions.SectionName}:Localization:Locale"],
            StringComparer.Ordinal);

        // AND IT REACHES A RUNNING HOST THE SAME WAY. The binder honouring an override in isolation is
        // necessary and not sufficient: the composition root could still read the locale from somewhere
        // else, which is precisely the shape the legacy defect had.
        using DataServicesTestHostFactory factory = new();
        factory.AdditionalSettings[$"{DataServicesOptions.SectionName}:Localization:Locale"] = "cht";

        using HttpClient client = factory.CreateClient();

        Assert.Equal(
            "cht",
            factory.Services
                .GetRequiredService<IOptions<DataServicesOptions>>()
                .Value.Localization.Locale,
            StringComparer.Ordinal);

        Assert.IsType<TraditionalChineseProvider>(
            factory.Services.GetRequiredService<II18nProvider>());
    }

    /// <summary>
    /// Each of the three legacy locales selects its own provider, and the set has exactly three members.
    /// </summary>
    /// <param name="locale">The locale token, as the legacy spells it.</param>
    /// <param name="providerTypeName">The provider type the composition root must resolve.</param>
    /// <remarks>
    /// <para>
    /// THREE ARMS, MATCHING THE THREE THE LEGACY DECLARES. <c>pfw.sra:L95-L102</c> is a
    /// <c>choose case</c> over exactly three values creating exactly three classes, and there is no
    /// fourth locale anywhere in the estate - the shipped resource table carries only the English and
    /// Traditional Chinese sections, because Simplified Chinese needs none.
    /// </para>
    /// <para>
    /// The row carries the provider's type NAME rather than its <see cref="Type"/> so the theory data
    /// stays serializable, and the assertion resolves the name against the actual instance - which also
    /// makes a failure read as "got the wrong provider" rather than as a cast exception.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en", nameof(EnglishProvider))]
    [InlineData("chs", nameof(SimplifiedChineseProvider))]
    [InlineData("cht", nameof(TraditionalChineseProvider))]
    public void EachLegacyLocaleSelectsItsOwnProvider(string locale, string providerTypeName)
    {
        using ServiceProvider composed = Compose(
            Configuration(
                new KeyValuePair<string, string?>(
                    $"{DataServicesOptions.SectionName}:Localization:Locale",
                    locale)));

        II18nProvider provider = composed.GetRequiredService<II18nProvider>();

        Assert.Equal(providerTypeName, provider.GetType().Name, StringComparer.Ordinal);

        // A SINGLETON, so the whole service shares one provider and the installed translation surface
        // cannot differ between two callers within a host.
        Assert.Same(provider, composed.GetRequiredService<II18nProvider>());
    }

    /// <summary>
    /// The Simplified Chinese provider is selectable and leaves the text exactly as supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE DELIBERATE NO-OP, PRESERVED AS A THIRD ARM RATHER THAN COLLAPSED INTO TWO. Its legacy
    /// translate body is entirely commented out because Simplified Chinese is the BASE locale - the text
    /// the framework ships is already correct - so there is nothing for it to do and it does nothing:
    /// no translation, no trim, no normalization, and not even a copy-back of the value it was given,
    /// since a self-assignment through a by-reference parameter is itself an observable write.
    /// </para>
    /// <para>
    /// ⚠ AND YET IT IS NOT THE SAME AS DECLINING. It answers HANDLED for the framework's own source,
    /// which STOPS the lookup chain, and that is a different claim from answering not-handled: one says
    /// "already correct", the other says "not mine". Collapsing the three providers to two - or making
    /// this one return not-handled - would change which provider a chained lookup reached next. The
    /// numerals of that two-value alphabet coincide with the tri-state return-code algebra's and the
    /// semantics do not, so nothing here is compared against a return code.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSimplifiedChineseProviderIsSelectableAndLeavesTheTextExactlyAsSupplied()
    {
        using ServiceProvider composed = Compose(
            Configuration(
                new KeyValuePair<string, string?>(
                    $"{DataServicesOptions.SectionName}:Localization:Locale",
                    "chs")));

        II18nProvider provider = composed.GetRequiredService<II18nProvider>();

        Assert.IsType<SimplifiedChineseProvider>(provider);

        // HANDLED, AND THE TEXT UNTOUCHED. The framework's own source is the one it answers for.
        string? text = "确定";

        long handled = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text);

        Assert.Equal(1L, handled);
        Assert.Equal("确定", text, StringComparer.Ordinal);

        // NOT HANDLED for any other source, and STILL untouched - the two paths differ in their answer
        // and not in their effect on the text.
        string? custom = "确定";

        long declined = provider.OnTranslate(Enums.I18N_SRC_CUSTOM, Categories.CAT_DWSVC, ref custom);

        Assert.Equal(0L, declined);
        Assert.Equal("确定", custom, StringComparer.Ordinal);

        // A NULL SURVIVES AS NULL. The provider never dereferences the text, so a null lookup key cannot
        // throw and is not collapsed to an empty string - which AAP 0.4.5.4 forbids.
        string? absent = null;

        _ = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref absent);

        Assert.Null(absent);
    }

    /// <summary>
    /// An unrecognised locale fails at STARTUP, not on first use, and names the oracle line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHERE THE FAILURE HAPPENS IS THE ASSERTION, NOT MERELY THAT IT HAPPENS. The provider factory has a
    /// throwing default arm, so an unrecognised locale would eventually fail whatever this test did -
    /// but "eventually" would mean on the first request that needed a translated message, which is
    /// somewhere inside a DataWindow validation path and reads to an operator as a data fault rather
    /// than a configuration one. Refusing it at startup with the key and the accepted set named is the
    /// fail-fast posture, and it is the reason the options validator restricts the value as well as the
    /// factory switching on it.
    /// </para>
    /// <para>
    /// A QUIET FALLBACK WOULD BE WORSE THAN EITHER. A service that defaulted to another locale would emit
    /// validation messages in a language no caller asked for, and every characterization comparison for
    /// every localized message would silently diverge - a wrong answer that looks like a right one.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUnrecognisedLocaleFailsStartupRatherThanFirstUse()
    {
        IConfigurationRoot configuration = Configuration(
            new KeyValuePair<string, string?>(
                $"{DataServicesOptions.SectionName}:Localization:Locale",
                "de"));

        using ServiceProvider composed = Compose(configuration);

        // THE STARTUP VALIDATOR IS WHAT REFUSES IT, which is what makes this a startup failure. Resolving
        // it and invoking it is precisely what the host does during start, before Kestrel binds.
        IStartupValidator startupValidator = composed.GetRequiredService<IStartupValidator>();

        OptionsValidationException refusal =
            Assert.Throws<OptionsValidationException>(startupValidator.Validate);

        Assert.Contains(
            $"{DataServicesOptions.SectionName}:Localization:Locale",
            refusal.Message,
            StringComparison.Ordinal);

        // THE ACCEPTED SET AND THE ORACLE LINE TRAVEL WITH THE REFUSAL, so an operator reading the
        // failure has both the permitted values and the line that decides them.
        Assert.Contains("ws_objects/pfw.pbl.src/pfw.sra:L95-L102", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("de", refusal.Message, StringComparison.Ordinal);

        // AND A REAL HOST DOES NOT START. Asserted separately because the row above proves the validator
        // refuses and this proves the host is actually gated on it.
        using DataServicesTestHostFactory factory = new();
        factory.AdditionalSettings[$"{DataServicesOptions.SectionName}:Localization:Locale"] = "de";

        _ = Assert.ThrowsAny<OptionsValidationException>(factory.CreateClient);
    }

    // ==============================================================================================
    //  EVERY PRESERVED DEFAULT IS OVERRIDABLE - THE SAME REQUIREMENT, ONE ROW PER KEY
    // ==============================================================================================

    /// <summary>
    /// Every preserved legacy default can be changed by a later configuration provider.
    /// </summary>
    /// <param name="relativePath">The key, relative to the service section.</param>
    /// <param name="suppliedValue">The value a deployment supplies.</param>
    /// <param name="expectedBoundValue">What the bound member must then read as.</param>
    /// <remarks>
    /// <para>
    /// WHY EVERY KEY AND NOT ONLY THE LOCALE. The locale is the NAMED User Example, but the plan's
    /// resolution for it is a rule rather than an exception: a value the legacy hardcoded moves into an
    /// options type bound from configuration, everywhere, because constraint C-F is repository-wide. A
    /// preserved default that could not be changed would have reproduced the hardcoding rather than the
    /// behaviour - and it would do so silently, since the value would still be right.
    /// </para>
    /// <para>
    /// EACH SUPPLIED VALUE IS A LEGAL ONE, chosen so a row observes overridability and not a validation
    /// refusal. The refusals are Part One's subject and are tested there one rule at a time; a row here
    /// that tripped one would fail for a reason that has nothing to do with what it asserts.
    /// </para>
    /// <para>
    /// NO ROW SUPPLIES A SECOND ACCEPTABLE VALUE FOR THE DEFAULT ITSELF. The supplied value is always
    /// DIFFERENT from the declared default, so a binder that ignored the override entirely would fail
    /// rather than pass by coincidence.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OverridablePreservedDefaults))]
    public void EveryPreservedLegacyDefaultIsOverridableByALaterConfigurationProvider(
        string relativePath,
        string suppliedValue,
        string expectedBoundValue)
    {
        string fullPath = $"{DataServicesOptions.SectionName}:{relativePath}";

        // The declared default first, so the row proves a CHANGE rather than a coincidence.
        object? declared = DataServicesSettingsDocuments.ReadBoundValue(
            DataServicesSettingsDocuments.BindService(DataServicesSettingsDocuments.Base()),
            relativePath);

        string declaredText = Convert.ToString(declared, CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.False(
            string.Equals(declaredText, expectedBoundValue, StringComparison.OrdinalIgnoreCase),
            $"'{fullPath}' already reads as '{expectedBoundValue}' before the override is applied, so "
                + "this row cannot distinguish an honoured override from an ignored one. Choose a "
                + "supplied value that differs from the declared default.");

        DataServicesOptions overridden = DataServicesSettingsDocuments.BindService(
            Configuration(new KeyValuePair<string, string?>(fullPath, suppliedValue)));

        object? bound = DataServicesSettingsDocuments.ReadBoundValue(overridden, relativePath);

        string boundText = Convert.ToString(bound, CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.True(
            string.Equals(expectedBoundValue, boundText, StringComparison.OrdinalIgnoreCase),
            $"'{fullPath}' was supplied '{suppliedValue}' by a later provider and bound to "
                + $"'{boundText}'. The plan preserves each legacy default as a DEFAULT and removes its "
                + "un-configurability, so a value that cannot be overridden reproduces the defect rather "
                + "than the behaviour (AAP 0.8.2).");
    }

    /// <summary>
    /// One legal override per preserved default, each different from the declared value.
    /// </summary>
    /// <remarks>
    /// The Pinyin flag row supplies a single flag rather than a made-up number, so even the override is
    /// composed from the framework catalogue's own constants
    /// [ws_objects/pfw.shared.pbl.src/enums.sru:L1147-L1149] rather than from an arbitrary integer.
    /// </remarks>
    public static TheoryData<string, string, string> OverridablePreservedDefaults()
    {
        string singleFilterBit =
            DropDownSearchModel.FILTER_DISP.ToString(CultureInfo.InvariantCulture);
        string multipleSelection =
            RowSelectService.RS_MULTIPLE.ToString(CultureInfo.InvariantCulture);
        string ignoreCaseOnly =
            Enums.PY_LIKE_IGNORE_CASE.ToString(CultureInfo.InvariantCulture);

        return new TheoryData<string, string, string>
        {
            // The named User Example - the other two accepted locales are both legal overrides.
            { "Localization:Locale", "chs", "chs" },

            // The other member of the row-selection set, referenced by name rather than typed as 2.
            { "RowSelect:Style", multipleSelection, multipleSelection },

            // The five context-menu toggles, each turned off. Off is legal: the legacy declares them as
            // plain public booleans with no setter and no guard, so any deployment may clear one.
            { "ContextMenu:ColAutoWidth", "false", "False" },
            { "ContextMenu:ColCheck", "false", "False" },
            { "ContextMenu:ColCopy", "false", "False" },
            { "ContextMenu:ColPaste", "false", "False" },
            { "ContextMenu:ItemCopy", "false", "False" },

            // Tracing on. The expression trace is fire-and-forget diagnostics carrying the call stack the
            // calc vector builds, so enabling it changes what is emitted and not what is calculated.
            { "ColumnExpression:Trace", "true", "True" },

            // A different suppression threshold. Zero is legal and means always suppress - the legacy
            // test is a STRICT greater-than - but a non-zero value is used here so the row cannot be
            // confused with the zero-acceptance rule Part One already covers.
            { "ColumnExpression:RedrawSuppressionRowThreshold", "500", "500" },

            // A different calc-stack reservation. The reservation is a capacity hint on the vector the
            // engine creates, so a deployment may size it differently without changing a result.
            { "ColumnExpression:CalcStackInitialCapacity", "64", "64" },

            // One filter bit instead of two. Composed from the owning model's constant, so the row
            // follows the symbol rather than restating its value.
            { "DropDownSearch:FilterType", singleFilterBit, singleFilterBit },

            // The tri-state key, moved OFF its automatic third state. This is the row that proves the
            // null is a default rather than the only reachable value.
            { "DropDownSearch:ShowFilteredRows", "true", "True" },

            // A single Pinyin flag instead of all three.
            { "DropDownSearch:PinyinMatchFlags", ignoreCaseOnly, ignoreCaseOnly },
        };
    }

    // ==============================================================================================
    //  THE FAIL-FAST POSTURE - `HALT CLOSE` IN ITS MANAGED FORM
    // ==============================================================================================

    /// <summary>
    /// Startup validation is genuinely wired, not merely declared on the options type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A VALIDATION ATTRIBUTE WITH NO <c>ValidateOnStart</c> CALL WOULD NEVER RUN, AND THAT IS THE DEFECT
    /// THIS ROW EXISTS FOR. Options validation is lazy by default: it fires the first time something reads
    /// the options value, which for several of these groups is the first request that happens to need
    /// them. A service could therefore carry a fully specified validator, a complete set of annotations
    /// and no startup enforcement at all - and every unit test of the validator would still pass, because
    /// the validator itself works. What would be missing is the wiring that makes it fatal.
    /// </para>
    /// <para>
    /// <see cref="IStartupValidator"/> IS THE PROOF, because the options infrastructure registers it ONLY
    /// in response to a <c>ValidateOnStart</c> call. Resolving it from the composition root's own
    /// registration group therefore asserts the wiring rather than the validator, and asserting that it
    /// PASSES on a good configuration and THROWS on a bad one asserts that it is connected to both roots.
    /// </para>
    /// </remarks>
    [Fact]
    public void StartupValidationIsWiredForBothBindableRootsRatherThanMerelyDeclared()
    {
        using ServiceProvider composed = Compose(Configuration());

        // REGISTERED AT ALL - the marker that ValidateOnStart was called.
        IStartupValidator startupValidator = composed.GetRequiredService<IStartupValidator>();

        // AND IT PASSES ON THE SHIPPED CONFIGURATION, so the negative rows below cannot be passing
        // because startup validation refuses everything.
        startupValidator.Validate();

        // CONNECTED TO THE SERVICE'S OWN ROOT.
        using ServiceProvider withBadService = Compose(
            Configuration(
                new KeyValuePair<string, string?>(
                    $"{DataServicesOptions.SectionName}:Persistence:Address",
                    string.Empty)));

        OptionsValidationException serviceRefusal = Assert.Throws<OptionsValidationException>(
            withBadService.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(
            $"{DataServicesOptions.SectionName}:Persistence:Address",
            serviceRefusal.Message,
            StringComparison.Ordinal);

        // AND TO THE INBOUND-TOKEN ROOT, which is a SEPARATE bindable root at a top-level path - so a
        // service could easily have wired one and not the other, and an unauthenticated boundary is a
        // structural fault under constraint C-G rather than a misconfiguration to discover later.
        using ServiceProvider withBadToken = Compose(
            Configuration(
                new KeyValuePair<string, string?>(
                    $"{JwtAuthenticationOptions.SectionName}:Authority",
                    string.Empty)));

        OptionsValidationException tokenRefusal = Assert.Throws<OptionsValidationException>(
            withBadToken.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(
            $"{JwtAuthenticationOptions.SectionName}:Authority",
            tokenRefusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Blanking any structurally required key stops the host from starting, with the key named.
    /// </summary>
    /// <param name="configurationKey">The key to blank or corrupt.</param>
    /// <param name="suppliedValue">The value that makes the configuration structurally invalid.</param>
    /// <param name="expectedInMessage">The text the refusal must carry, so an operator can act on it.</param>
    /// <remarks>
    /// <para>
    /// THE HOST, NOT THE VALIDATOR - AND THE DISTINCTION IS THE ASSERTION. Part One proves the validator
    /// refuses each of these; this proves the HOST is gated on that refusal. Those are different claims,
    /// and only the second one reproduces the legacy posture:
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c> decodes a seven-field assertion payload and then
    /// executes <c>HALT CLOSE</c> at <c>:L143</c>, ending the application rather than reporting and
    /// continuing.
    /// </para>
    /// <para>
    /// ⚠ SOFTENING THIS INTO WARN-AND-CONTINUE WOULD BE A BEHAVIOURAL CHANGE DRESSED AS ROBUSTNESS
    /// (AAP 0.1.4), and it would be worse operationally than it looks. A service with no upstream address
    /// that started anyway would satisfy a readiness probe that reads a connection, which is enough for
    /// the orchestration health condition to let Gateway start behind it - and then fail every request
    /// from a position where the fault is far harder to attribute than startup.
    /// </para>
    /// <para>
    /// The message content is asserted as well as the throw. A refusal that named no key would be
    /// fail-fast and useless: an operator would know the host would not start and not which of a dozen
    /// keys to fix.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(StructurallyInvalidConfigurations))]
    public void BlankingAStructurallyRequiredKeyStopsTheHostFromStarting(
        string configurationKey,
        string suppliedValue,
        string expectedInMessage)
    {
        using DataServicesTestHostFactory factory = new();
        factory.AdditionalSettings[configurationKey] = suppliedValue;

        OptionsValidationException refusal =
            Assert.ThrowsAny<OptionsValidationException>(factory.CreateClient);

        Assert.Contains(expectedInMessage, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// One structurally invalid configuration per required key, with the text its refusal must carry.
    /// </summary>
    /// <remarks>
    /// Every key is composed from a published constant or from <c>nameof</c>, so a row cannot address a
    /// setting the options types do not declare - a mistyped key would simply be ignored by the binder
    /// and the row would fail for the wrong reason.
    /// </remarks>
    public static TheoryData<string, string, string> StructurallyInvalidConfigurations()
    {
        string service = DataServicesOptions.SectionName;
        string token = JwtAuthenticationOptions.SectionName;

        return new TheoryData<string, string, string>
        {
            // NO UPSTREAM ADDRESS. Without it this service cannot serve a retrieval or an update at all,
            // because every path to data runs over the persistence contract.
            {
                $"{service}:{nameof(DataServicesOptions.Persistence)}:"
                    + $"{nameof(PersistenceClientOptions.Address)}",
                string.Empty,
                $"{service}:Persistence:Address"
            },

            // A PRESENT BUT MALFORMED ADDRESS, which is the likelier mistake than a blank one - and the
            // one a "required" annotation alone would let through.
            {
                $"{service}:{nameof(DataServicesOptions.Persistence)}:"
                    + $"{nameof(PersistenceClientOptions.Address)}",
                "persistence-service:5101",
                $"{service}:Persistence:Address"
            },

            // NO SECURITY ADDRESS. Without it the cryptographic contract is unreachable and no service
            // token can be obtained.
            {
                $"{service}:{nameof(DataServicesOptions.Security)}:"
                    + $"{nameof(SecurityClientOptions.BaseAddress)}",
                string.Empty,
                $"{service}:Security:BaseAddress"
            },

            // NO AUTHORITY. An inbound token could not be validated against Security's published
            // verification material, which leaves a created boundary unauthenticated - a structural fault
            // under constraint C-G rather than a degraded mode.
            {
                $"{token}:{nameof(JwtAuthenticationOptions.Authority)}",
                string.Empty,
                $"{token}:Authority"
            },

            // NO AUDIENCE. The service would accept a token minted for a different service.
            {
                $"{token}:{nameof(JwtAuthenticationOptions.Audience)}",
                string.Empty,
                $"{token}:Audience"
            },

            // AN UNRECOGNISED LOCALE, which the provider factory could not map onto any of the three
            // classes the legacy declares.
            {
                $"{service}:{nameof(DataServicesOptions.Localization)}:"
                    + $"{nameof(LocalizationOptions.Locale)}",
                "de",
                $"{service}:Localization:Locale"
            },

            // A ZERO ROW-SELECTION STYLE, which the legacy setter itself rejects
            // [n_cst_dwsvc_rowselect.sru:L169] - so refusing it preserves behaviour rather than adding a
            // guard. Note the deliberate asymmetry with the filter type, which the legacy does NOT guard
            // and which is therefore absent from this table.
            {
                $"{service}:{nameof(DataServicesOptions.RowSelect)}:"
                    + $"{nameof(RowSelectOptions.Style)}",
                "0",
                $"{service}:RowSelect:Style"
            },

            // A BLANK DIALECT SELECTOR. Persistence substring-tests it and falls back to a dialect rather
            // than failing, so an unset value would SILENTLY select one - which is why it is refused here
            // instead.
            {
                $"{service}:{nameof(DataServicesOptions.PersistenceSession)}:"
                    + $"{nameof(PersistenceSessionOptions.Dbms)}",
                string.Empty,
                $"{service}:PersistenceSession:Dbms"
            },

            // NO ISSUANCE CREDENTIAL AT ALL. Blanking the flat key leaves this service unable to present
            // either of the two credentials the token contract accepts, so it would obtain no token and
            // reach nothing downstream.
            {
                SecurityClientOptions.ClientSecretConfigurationKey,
                string.Empty,
                SecurityClientOptions.ClientSecretConfigurationKey
            },
        };
    }

    /// <summary>
    /// The startup failure is immediate and total: no request is ever served, on any attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE ROW ABOVE PROVES A THROW; THIS PROVES THERE IS NO RUNNING HOST BEHIND IT. Those are genuinely
    /// different outcomes, and the second is the one the legacy posture requires. A host that logged the
    /// fault, started anyway and answered each request with an error would also throw somewhere - but it
    /// would be reachable, it would satisfy a connection-based readiness probe, and the orchestration
    /// health condition would let Gateway start behind it. Fail-fast means the listener never exists.
    /// </para>
    /// <para>
    /// Asserted three ways over one misconfigured fixture: the client cannot be created, the container
    /// cannot be reached, and a SECOND attempt fails identically rather than succeeding on a retry - which
    /// is what rules out a host that starts on the second try with a partially initialised state.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStartupFailureIsImmediateAndTotalRatherThanReportedAndSurvived()
    {
        using DataServicesTestHostFactory factory = new();
        factory.AdditionalSettings[
            $"{DataServicesOptions.SectionName}:{nameof(DataServicesOptions.Persistence)}:"
                + $"{nameof(PersistenceClientOptions.Address)}"] = string.Empty;

        _ = Assert.ThrowsAny<OptionsValidationException>(factory.CreateClient);

        // NO CONTAINER EITHER, so there is nothing to serve a request from - not a container that exists
        // and holds a broken options instance.
        _ = Assert.ThrowsAny<OptionsValidationException>(() => factory.Services);

        // AND IT DOES NOT BECOME SERVABLE ON A RETRY.
        _ = Assert.ThrowsAny<OptionsValidationException>(factory.CreateClient);
    }

    /// <summary>
    /// The shipped configuration starts cleanly and answers its anonymous readiness probe.
    /// </summary>
    /// <param name="environmentName">The environment the host runs under.</param>
    /// <remarks>
    /// <para>
    /// THE PAIRED POSITIVE CONTROL, AND WITHOUT IT THE NEGATIVES ARE WORTH VERY LITTLE. Every row above
    /// asserts that a host does NOT start. A composition root that could not start under ANY
    /// configuration would satisfy all of them. This row is what distinguishes "refuses a structural
    /// fault" from "refuses everything".
    /// </para>
    /// <para>
    /// Both environments are exercised because they load different documents: Production loads the base
    /// file alone and Development layers the overlay on it. The overlay is where a behavioural divergence
    /// would hide, so starting cleanly under both - and answering the same anonymous probe - is the
    /// property worth asserting.
    /// </para>
    /// <para>
    /// <c>/health</c> IS ANONYMOUS BY DESIGN AND THAT IS NOT A GAP. The readiness chain that gates Gateway
    /// behind its upstreams has to be able to poll it without holding a credential (constraint C-L);
    /// <c>/v1/ping</c> is the authenticated counterpart and answers 401 without one, which the
    /// authorization suites cover.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task TheShippedConfigurationStartsCleanlyInBothEnvironments(string environmentName)
    {
        using DataServicesTestHostFactory factory =
            string.Equals(environmentName, "Development", StringComparison.Ordinal)
                ? DataServicesTestHostFactory.ForDevelopment()
                : new DataServicesTestHostFactory();

        Assert.Equal(environmentName, factory.EnvironmentName, StringComparer.Ordinal);

        using HttpClient client = factory.CreateAnonymousClient();

        // STARTUP VALIDATION HAS ALREADY RUN BY HERE - the host is built and both roots were validated -
        // so reaching this line at all is the positive control. The probe confirms the listener serves.
        using HttpResponseMessage response = await client.GetAsync(
            new Uri("/health", UriKind.Relative),
            TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"The shipped {environmentName} configuration produced a host whose anonymous readiness "
                + $"probe answered {(int)response.StatusCode}. Every fail-fast assertion in this file "
                + "depends on a valid configuration genuinely starting, so this is the control that keeps "
                + "them meaningful.");

        // AND THE PRESERVED DEFAULTS SURVIVED THE ENVIRONMENT. Asserted here rather than only against the
        // documents, because this is the running host that a characterization recording would be captured
        // from - and the whole reason the overlay may not redeclare a preserved default.
        DataServicesOptions options =
            factory.Services.GetRequiredService<IOptions<DataServicesOptions>>().Value;

        Assert.Equal("en", options.Localization.Locale, StringComparer.Ordinal);
        Assert.Equal(RowSelectService.RS_SINGLE, options.RowSelect.Style);
        Assert.Equal(
            DropDownSearchModel.FILTER_DISP + DropDownSearchModel.FILTER_DISP_PY,
            options.DropDownSearch.FilterType);
        Assert.Null(options.DropDownSearch.ShowFilteredRows);
        Assert.False(options.ColumnExpression.Trace);
        Assert.Equal(200, options.ColumnExpression.RedrawSuppressionRowThreshold);
        Assert.Equal(20, options.ColumnExpression.CalcStackInitialCapacity);
        Assert.True(options.ContextMenu.ColAutoWidth);
        Assert.True(options.ContextMenu.ColCheck);
        Assert.True(options.ContextMenu.ColCopy);
        Assert.True(options.ContextMenu.ColPaste);
        Assert.True(options.ContextMenu.ItemCopy);
    }



    // ==============================================================================================
    //  THE STRUCTURAL NEGATIVES - A TOTAL CENSUS OF THE WHOLE OPTIONS SURFACE
    // ==============================================================================================

    /// <summary>
    /// Every option type reachable from either bindable root, discovered rather than listed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WALKED, NOT ENUMERATED, AND THAT IS THE DIFFERENCE BETWEEN A CENSUS AND A CHECKLIST. A
    /// hand-written list of types cannot cover a group added tomorrow - it just quietly stops covering
    /// the surface, while continuing to pass. Walking the property graph from the two roots means a new
    /// group is scanned by every negative below on the day it appears, with no one remembering to extend
    /// anything. That property is why the earlier hand-listed census in Part One is complemented here
    /// rather than merely repeated: this walk reaches the four nested groups a hand list had missed.
    /// </para>
    /// <para>
    /// Only types declared in the service's own assembly are followed, so the walk does not descend into
    /// framework types - a time span has properties too, and none of them is a configuration surface.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Type> OptionTypeGraph()
    {
        HashSet<Type> discovered = [];
        Queue<Type> pending = new([typeof(DataServicesOptions), typeof(JwtAuthenticationOptions)]);

        while (pending.TryDequeue(out Type? type))
        {
            if (!discovered.Add(type))
            {
                continue;
            }

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Type candidate = property.PropertyType;

                if (candidate.Assembly == typeof(DataServicesOptions).Assembly
                    && candidate is { IsClass: true, IsAbstract: false })
                {
                    pending.Enqueue(candidate);
                }
            }
        }

        return [.. discovered];
    }

    /// <summary>
    /// No member anywhere on the configuration surface could hold signing material, and the few that
    /// legitimately hold a credential or a path are named, empty and justified.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINTS C-F AND C-G, AS A TOTAL SELF-AUDIT. Security is the SOLE issuer: exactly one signing
    /// secret exists in the whole system and Security holds it. A signing member on this surface would be
    /// a second signing authority, which ends the sole-issuer property the entire token topology rests on
    /// - and this service could not even use one, since its project file deliberately omits the minting
    /// package, making the capability unreachable rather than merely unused.
    /// </para>
    /// <para>
    /// FOUR MEMBERS LEGITIMATELY CARRY A CREDENTIAL OR A PATH, AND EACH IS PINNED BY NAME RATHER THAN BY
    /// WEAKENING THE MARKER LIST. Naming them keeps the scan sharp: a FIFTH such member appearing anywhere
    /// on the surface still fails, which is exactly what a blanket relaxation would have stopped detecting.
    /// </para>
    /// <para>
    /// (1) The issuance secret - the password half of the identity this service presents to obtain its
    /// first service token, on the one operation a bearer token cannot protect. It mints nothing and is
    /// accepted by no other service. (2) and (3) The two mutual-TLS PATHS, which are paths and not
    /// material: mutual TLS is the documented per-pair fallback where a token issuer is inappropriate, and
    /// the plan provides for certificate and key path settings for such a pair (AAP 0.6.6.3). (4) The
    /// internal trust anchor path, which addresses PUBLIC material - a certificate authority certificate
    /// is published by construction. A fifth, <c>LogPass</c>, is covered by its own row because it is a
    /// wire field of the transaction descriptor rather than a setting this service consumes.
    /// </para>
    /// <para>
    /// EVERY ONE OF THEM IS EMPTY IN SOURCE, which is the structural half of the claim: the declaration is
    /// here and the material is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMemberAnywhereOnTheSurfaceCouldHoldSigningMaterial()
    {
        string[] forbiddenMarkers =
        [
            "SigningKey", "IssuerSigningKey", "SymmetricKey", "PrivateKey", "PublicKey", "KeyMaterial",
            "Jwk", "Pfx", "Pkcs12", "ApiKey", "AccessToken", "BearerToken", "RefreshToken", "Passphrase",
            "Secret", "Password", "Credential",
        ];

        // The members that legitimately carry a credential or a path, each pinned to its declaring type so
        // the same NAME on a different type would still fail.
        (Type Type, string Member)[] justified =
        [
            (typeof(SecurityClientOptions), nameof(SecurityClientOptions.ClientSecret)),
            (typeof(MutualTlsClientOptions), nameof(MutualTlsClientOptions.CertificatePath)),
            (typeof(MutualTlsClientOptions), nameof(MutualTlsClientOptions.CertificateKeyPath)),
            (typeof(InternalTlsTrustOptions), nameof(InternalTlsTrustOptions.TrustedCaPath)),
            (typeof(PersistenceSessionOptions), nameof(PersistenceSessionOptions.LogPass)),
        ];

        foreach (Type type in OptionTypeGraph())
        {
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                // A BOOLEAN CANNOT HOLD MATERIAL, AND EXCLUDING THEM IS NOT A LOOPHOLE. The naive scan
                // produced a real false positive here: `ValidateIssuerSigningKey` contains "SigningKey"
                // and is a verification-side SWITCH deciding whether the inbound signature is checked
                // against the published key - the opposite of holding one, and precisely the setting that
                // must stay true.
                if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
                {
                    continue;
                }

                bool isJustified = justified.Any(entry =>
                    entry.Type == type
                    && string.Equals(entry.Member, property.Name, StringComparison.Ordinal));

                if (isJustified)
                {
                    continue;
                }

                foreach (string marker in forbiddenMarkers)
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} could hold credential material. DataServices "
                            + "validates tokens and never issues them, Security is the sole issuer, and "
                            + "the four members that legitimately carry a credential or a path are named "
                            + "explicitly in this test (constraints C-F and C-G).");
                }
            }
        }

        // EVERY JUSTIFIED MEMBER STILL EXISTS, so the exemptions cannot silently become dead entries that
        // stop covering anything - and every one is EMPTY in source, which is the structural half of the
        // no-hardcoded-credential claim.
        DataServicesOptions fresh = new();

        foreach ((Type type, string member) in justified)
        {
            PropertyInfo? property = type.GetProperty(member);

            Assert.NotNull(property);
            Assert.Equal(typeof(string), property.PropertyType);
        }

        Assert.Equal(string.Empty, fresh.Security.ClientSecret);
        Assert.Equal(string.Empty, fresh.Security.MutualTls.CertificatePath);
        Assert.Equal(string.Empty, fresh.Security.MutualTls.CertificateKeyPath);
        Assert.Equal(string.Empty, fresh.InternalTls.TrustedCaPath);
        Assert.Equal(string.Empty, fresh.PersistenceSession.LogPass);
    }

    /// <summary>
    /// No member anywhere on the surface is storage configuration, and the descriptor fields that look
    /// like it are named and empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-E AT THE TYPE LEVEL. Persistence is the only service in the refactor that generates
    /// or executes SQL and the only one holding a storage provider. A connection-string member here would
    /// fabricate a second path to data - and the repository evidences exactly one storage engine, one
    /// schema and one connection, so a second path would have nothing behind it. The project file
    /// corroborates the position by referencing no data-access package at all.
    /// </para>
    /// <para>
    /// ⚠ THE TRANSACTION DESCRIPTOR IS THE EXCEPTION AND IT IS NOT A STORAGE CONFIGURATION. It mirrors
    /// the legacy transaction structure field for field
    /// [ws_objects/pfw.thread.ext.pbl.src/transactiondata.srs], so it necessarily declares a database
    /// name, a server name and a provider parameter string - as WIRE FIELDS sent to Persistence on the
    /// session request, because the task-scoped query and update contracts are session-scoped and a
    /// session needs a descriptor. Nothing in this service opens a connection with them. Their emptiness
    /// in both shipped documents is asserted by the settings-document suite; here it is their being
    /// confined to that one type that is asserted.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMemberAnywhereOnTheSurfaceIsStorageConfiguration()
    {
        string[] storageMarkers =
        [
            "ConnectionString", "DataSource", "Provider", "SqlClient", "Sqlite", "DbContext",
            "Migration", "Schema", "Table", "Journal", "InitialCatalog",
        ];

        foreach (Type type in OptionTypeGraph())
        {
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (string marker in storageMarkers)
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} looks like storage configuration. Persistence is "
                            + "the only service that holds a storage provider; DataServices reaches data "
                            + "solely over the persistence gRPC contract (constraint C-E).");
                }
            }
        }

        // THE DESCRIPTOR'S CONNECTION-SHAPED FIELDS ARE CONFINED TO THE DESCRIPTOR, and are empty by
        // default. Named here so a reader meets them beside the rule they are the exception to.
        DataServicesOptions fresh = new();

        Assert.Equal(string.Empty, fresh.PersistenceSession.Database);
        Assert.Equal(string.Empty, fresh.PersistenceSession.ServerName);
        Assert.Equal(string.Empty, fresh.PersistenceSession.DbParm);
        Assert.Equal(string.Empty, fresh.PersistenceSession.LogId);

        // AND NO OTHER GROUP DECLARES ANYTHING OF THE KIND - asserted by name, because the walk above
        // matches substrings and these four spellings are the ones that would read as innocuous.
        foreach (Type type in OptionTypeGraph().Where(static candidate =>
            candidate != typeof(PersistenceSessionOptions)))
        {
            foreach (string member in (string[])["Database", "ServerName", "DbParm", "LogId"])
            {
                Assert.Null(type.GetProperty(member));
            }
        }
    }

    /// <summary>
    /// No member anywhere on the surface belongs to a deferred service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CONSTRAINT C-D AT THE TYPE LEVEL, AND THE PROHIBITION IS ABSOLUTE RATHER THAN A PRIORITY.
    /// DesignSystem, Documents, Integration and ScriptBridge receive no code, no test, no container and
    /// no partial implementation in this phase - the requirements are explicit that they must not be
    /// implemented "even partially, even to stub them out", because a half-built deferred service is
    /// worse than a documented gap. An options member is a partial implementation: it declares the
    /// capability, invites configuration of it, and gives a reader reason to believe something exists
    /// behind it.
    /// </para>
    /// <para>
    /// THE THREE UI CAPABILITIES THIS SERVICE GENUINELY TOUCHES ARE HANDLED BY THE HEADLESS SPLIT, WHICH
    /// IS WHY NO OPTION IS NEEDED FOR THEM. Drop-down search contributes filter-expression construction
    /// and its search state while window positioning and input-method handling are deferred; the context
    /// menu contributes the item model - labels, identifiers, flags and computed logical widths - while
    /// scale conversion, font measurement and rendering are deferred; column sort contributes the sort
    /// expression and state while indicator geometry is deferred. Each deferred half is surfaced as DATA
    /// over the contract and named as a reserved Gateway extension point, so the gap is explicit and
    /// enumerable rather than silently filled by a setting here.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoMemberAnywhereOnTheSurfaceBelongsToADeferredService()
    {
        // ⚠ TWO MARKERS WERE MEASURED WRONG BEFORE THIS LIST SETTLED, AND BOTH CORRECTIONS SHARPENED THE
        // RULE RATHER THAN LOOSENING IT. A substring scan over member names is the right instrument here -
        // it is total, and it cannot be out-manoeuvred by a new group - but it is only as good as the
        // markers, and a marker that matches an in-scope member forbids the wrong thing.
        //
        //   * A bare "Http" failed on `JwtAuthenticationOptions.RequireHttpsMetadata`. HTTP is THIS
        //     service's own transport: its REST projection, its two typed clients and its discovery fetch
        //     all run over it. What Integration owns is the deferred outbound TRANSPORT FAMILY - the HTTP
        //     client objects and their extensions, FTP, WebSocket, MQTT and the second-generation
        //     transports - so the marker has to name the family, not the protocol. Forbidding the protocol
        //     would stop this service configuring the boundary it is required to authenticate, inverting
        //     constraint C-G in the name of constraint C-D.
        //
        //   * A bare "Ime" - for the deferred input-method interop - failed on `ValidateLifetime`, and on
        //     five duration members besides: "lifetime", "timeout" and "runtime" all contain it. A
        //     three-letter marker is simply too short to be a marker at all, so the capability is named in
        //     full instead.
        //
        //   * A bare "Window" is the third, and it collides with the CENTRAL IN-SCOPE NOUN OF THIS SERVICE.
        //     What DesignSystem owns is the WIN32 WINDOW - positioning and geometry, reached through
        //     ShowWindow, GetWindowRect, OffsetRect and SetWindowPos in the three presentational halves -
        //     and "Window" names neither that nor only that. It matches `DataWindow`, which is the subject
        //     of both projected contracts, so a legitimate member like a default DataWindow handle would
        //     be forbidden; and it matched `StreamCollectionWindow`, a TIME interval bounding one REST
        //     projection's collection, which names no user interface at all. The capability is therefore
        //     named by its geometry spellings, which is STRICTER than the bare marker rather than looser:
        //     five specific forms replace one that could not tell a screen from a stopwatch. `Win32`
        //     remains in the list and is what catches the interop family wholesale - and the SIBLING guard
        //     over settings KEYS in this same file has always relied on exactly that, listing `win32` and
        //     no bare `window`.
        string[] deferredMarkers =
        [
            "Dpi", "Scale", "Font", "Theme", "Colour", "Color", "Canvas", "Painter",
            "WindowPos", "WindowRect", "WindowGeometry", "WindowPlacement", "ShowWindow",
            "PopupMenu", "Tooltip", "TrayIcon", "Win32", "InputMethod",
            "Zip", "Barcode", "Xml", "Json", "FileScan", "Logger",
            "HttpClientObject", "OutboundHttp", "Ftp", "WebSocket", "Mqtt",
            "Sciter", "Blink", "WebView", "Compiler", "ScriptInvoker",
        ];

        foreach (Type type in OptionTypeGraph())
        {
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (string marker in deferredMarkers)
                {
                    Assert.False(
                        property.Name.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"{type.Name}.{property.Name} names a capability of a deferred service. "
                            + "DesignSystem, Documents, Integration and ScriptBridge receive no "
                            + "implementation in this phase, not even an options member (constraint "
                            + "C-D).");
                }
            }
        }

        // AND THE WALK ACTUALLY REACHED THE WHOLE SURFACE, which is what makes the absences above worth
        // anything. A walk that had silently found only the two roots would pass every negative in this
        // section while scanning almost nothing.
        IReadOnlyList<Type> graph = OptionTypeGraph();

        foreach (Type expected in (Type[])
            [
                typeof(DataServicesOptions), typeof(JwtAuthenticationOptions), typeof(LocalizationOptions),
                typeof(RowSelectOptions), typeof(ContextMenuOptions), typeof(ColumnExpressionOptions),
                typeof(DropDownSearchOptions), typeof(PersistenceClientOptions),
                typeof(SecurityClientOptions), typeof(MutualTlsClientOptions),
                typeof(InternalTlsTrustOptions), typeof(ResilienceOptions),
                typeof(ClientResilienceOptions), typeof(SessionsOptions), typeof(SessionLifetimeOptions),
                typeof(EventChainOptions), typeof(RestProjectionOptions), typeof(PersistenceSessionOptions),
            ])
        {
            Assert.Contains(expected, graph);
        }
    }
}


/// <summary>
/// The address-shape rules, which are credential controls first and shape rules second.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE HAVE THEIR OWN SUITE. Three of the four rules below look like tidiness and are not. An
/// address that embeds a credential is copied verbatim into every log record, every exception and every
/// trace that captures a request URI, so ONE settings entry becomes an unbounded number of disclosures -
/// which makes the refusal a control under constraint C-F rather than a preference about URI shape.
/// The blank-but-present rules are the other recurring shape: a key written and left empty is a
/// configuration entry begun and not finished, and treating it as absent would silently hide a mistake
/// behind a default.
/// </para>
/// <para>
/// EVERY ROW GOES THROUGH THE SHIPPED VALIDATORS rather than through a reimplementation of their rules,
/// so what is asserted is the refusal an operator would actually meet - message text included, because a
/// refusal that named no key would be fail-fast and useless.
/// </para>
/// </remarks>
public sealed class DataServicesAddressShapeTests
{
    /// <summary>An options instance that validates cleanly, for isolating one address fault.</summary>
    /// <returns>The options.</returns>
    private static DataServicesOptions ValidOptions()
    {
        DataServicesOptions options = new();
        options.Persistence.Address = "https://persistence-service:5101";
        options.Security.BaseAddress = "https://security-service:5104";
        options.Security.ClientSecret = GeneratedIssuanceCredential;
        return options;
    }

    /// <summary>An inbound-token configuration that validates cleanly.</summary>
    /// <returns>The options.</returns>
    private static JwtAuthenticationOptions ValidJwt()
    {
        JwtAuthenticationOptions options = new()
        {
            Authority = "https://security-service:5104",
            Audience = "powerframework-dataservices",
        };

        options.PermittedCallers.Add("powerframework-gateway");

        return options;
    }

    /// <summary>A placeholder issuance credential, composed at run time and never written down.</summary>
    private static readonly string GeneratedIssuanceCredential =
        Convert.ToBase64String(Guid.NewGuid().ToByteArray());

    private static string[] ServiceFailures(DataServicesOptions options) =>
        new DataServicesOptionsValidator().Validate(name: null, options).Failures?.ToArray() ?? [];

    private static string[] TokenFailures(JwtAuthenticationOptions options) =>
        new JwtAuthenticationOptionsValidator().Validate(name: null, options).Failures?.ToArray() ?? [];

    /// <summary>
    /// An address that embeds a credential is refused, on every address the service configures.
    /// </summary>
    /// <param name="address">The offending address.</param>
    /// <remarks>
    /// <para>
    /// THE MECHANISM IS WHAT MAKES THIS A SECRETS CONTROL. The two typed clients log their request URIs -
    /// the base document raises their category to make exactly that visible on a developer console - so a
    /// credential in the address is written out on every call, into records that are retained, shipped and
    /// searched. That is a categorically worse exposure than the same value in a settings file, because it
    /// escapes the file.
    /// </para>
    /// <para>
    /// The repository-wide sweep is the reason to check rather than assume: it found eight in-source
    /// credential sites where three were named, and one of them is a live broker host address paired with
    /// an administrator account name and its cleartext password, committed inside a comment block
    /// [ws_objects/pfw.tests.pbl.src/w_test_websocket_mqtt.srw:L176-L178]. An address-with-credentials is
    /// precisely that pattern in configuration form.
    /// </para>
    /// </remarks>
    [Theory]
    // BOTH VALUES ARE UNMISTAKEABLE PLACEHOLDERS, and that is deliberate rather than fussy: a
    // credential-SHAPED literal in a test file is a credential-shaped string in version control, and a
    // scanner cannot tell one from a real one. Neither token below can match any provider's key format.
    [InlineData("https://not-a-real-account:not-a-real-value@persistence-service:5101")]
    [InlineData("https://not-a-real-account@persistence-service:5101")]
    public void AnAddressThatEmbedsACredentialIsRefusedWithTheRemedyStated(string address)
    {
        DataServicesOptions options = ValidOptions();
        options.Persistence.Address = address;

        string failure = Assert.Single(ServiceFailures(options));

        Assert.Contains(
            $"{DataServicesOptions.SectionName}:Persistence:Address",
            failure,
            StringComparison.Ordinal);

        // THE REMEDY AND THE REASON BOTH TRAVEL WITH THE REFUSAL. An operator who is told only "invalid
        // address" removes the wrong part of it.
        Assert.Contains("credentials", failure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("logs", failure, StringComparison.OrdinalIgnoreCase);

        // AND THE OFFENDING VALUE IS NOT ECHOED BACK, which would put the credential into the very
        // startup record the refusal produces.
        Assert.DoesNotContain("not-a-real-value", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// A base address carrying a query or a fragment is refused, because neither can have any effect.
    /// </summary>
    /// <param name="address">The offending address.</param>
    /// <param name="expectedFragmentOfMessage">The reason the refusal must state.</param>
    /// <remarks>
    /// NOT PEDANTRY - BOTH ARE SILENTLY LOST. A query on a base address is DROPPED rather than merged when
    /// a per-request path is composed onto it, and a fragment is never transmitted to a server at all. So
    /// either one would have no effect and produce no diagnostic: an operator would configure something,
    /// observe nothing, and have nowhere to look. Refusing them at startup converts a silent no-op into a
    /// named fault.
    /// </remarks>
    [Theory]
    [InlineData("https://security-service:5104?tenant=alpha", "query")]
    [InlineData("https://security-service:5104#section", "fragment")]
    public void ABaseAddressCarryingAQueryOrFragmentIsRefused(
        string address,
        string expectedFragmentOfMessage)
    {
        DataServicesOptions options = ValidOptions();
        options.Security.BaseAddress = address;

        string failure = Assert.Single(ServiceFailures(options));

        Assert.Contains(
            $"{DataServicesOptions.SectionName}:Security:BaseAddress",
            failure,
            StringComparison.Ordinal);
        Assert.Contains(expectedFragmentOfMessage, failure, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The optional metadata address is refused when present and blank, and validated when supplied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ABSENT AND BLANK ARE DIFFERENT ANSWERS, WHICH IS THE WHOLE RULE. Unset means "derive the discovery
    /// address from the authority" and is the normal case - and is what the shipped base document declares,
    /// as JSON null. A key written and then left empty means an entry was begun and not finished, and
    /// treating it as unset would hide the mistake behind a default that happens to work.
    /// </para>
    /// <para>
    /// A QUERY IS PERMITTED HERE AND FORBIDDEN ON THE AUTHORITY, and the asymmetry is evidenced rather
    /// than incidental: this is a COMPLETE document address transmitted as written, whereas the authority
    /// is a base onto which paths are composed. Both halves are asserted so the asymmetry cannot be
    /// harmonised in either direction.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheOptionalMetadataAddressIsRefusedWhenPresentAndBlank()
    {
        string metadataPath =
            $"{JwtAuthenticationOptions.SectionName}:{nameof(JwtAuthenticationOptions.MetadataAddress)}";

        // ABSENT IS CORRECT AND IS THE SHIPPED STATE.
        JwtAuthenticationOptions absent = ValidJwt();

        Assert.Null(absent.MetadataAddress);
        Assert.Empty(TokenFailures(absent));

        // PRESENT BUT BLANK IS A FAULT, and the remedy is to remove the key rather than to fill it in.
        JwtAuthenticationOptions blank = ValidJwt();
        blank.MetadataAddress = "   ";

        string blankFailure = Assert.Single(TokenFailures(blank));

        Assert.Contains(metadataPath, blankFailure, StringComparison.Ordinal);
        Assert.Contains("Remove the key entirely", blankFailure, StringComparison.Ordinal);

        // PRESENT AND MALFORMED IS ALSO A FAULT - the handler would otherwise discover it on its first
        // metadata fetch, which is exactly what startup validation exists to pre-empt.
        JwtAuthenticationOptions malformed = ValidJwt();
        malformed.MetadataAddress = "security-service/.well-known/openid-configuration";

        Assert.Contains(
            TokenFailures(malformed),
            failure => failure.Contains(metadataPath, StringComparison.Ordinal));

        // A QUERY IS PERMITTED ON THIS ONE, because it is a complete address rather than a base.
        JwtAuthenticationOptions withQuery = ValidJwt();
        withQuery.MetadataAddress =
            "https://security-service:5104/.well-known/openid-configuration?v=2";

        Assert.Empty(TokenFailures(withQuery));

        // AND FORBIDDEN ON THE AUTHORITY, which is the base. Asserted beside its counterpart so the
        // asymmetry reads as a decision.
        JwtAuthenticationOptions authorityWithQuery = ValidJwt();
        authorityWithQuery.Authority = "https://security-service:5104?v=2";

        Assert.Contains(
            TokenFailures(authorityWithQuery),
            failure => failure.Contains(
                $"{JwtAuthenticationOptions.SectionName}:{nameof(JwtAuthenticationOptions.Authority)}",
                StringComparison.Ordinal));
    }

    /// <summary>
    /// A plain-http authority is refused while the metadata-transport requirement stands, and the refusal
    /// names the settings file the relaxation belongs in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE TWO SETTINGS ARE ONE DECISION AND THE VALIDATOR TREATS THEM AS ONE. A plain-http authority with
    /// the transport requirement left on describes a topology in which discovery metadata could never be
    /// retrieved at all - the service would start, report itself healthy, and fail to validate the first
    /// token it received. Refusing the PAIR at startup is what turns a runtime mystery into a named
    /// configuration fault.
    /// </para>
    /// <para>
    /// AND THE REFUSAL POINTS AT THE DEVELOPMENT FILE RATHER THAN THE BASE ONE, which matters under
    /// constraint C-G: a relaxation written into the base document reaches every environment including a
    /// deployed one, whereas the same value in the Development overlay reaches only the environment that
    /// asked for it. The shipped documents need neither, because Security terminates TLS everywhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void APlainHttpAuthorityIsRefusedWhileTheMetadataTransportRequirementStands()
    {
        JwtAuthenticationOptions plainHttp = ValidJwt();
        plainHttp.Authority = "http://security-service:5104";

        string failure = Assert.Single(TokenFailures(plainHttp));

        Assert.Contains(
            nameof(JwtAuthenticationOptions.RequireHttpsMetadata),
            failure,
            StringComparison.Ordinal);
        Assert.Contains("development settings", failure, StringComparison.OrdinalIgnoreCase);

        // AND THE SUPPORTED LOOPBACK TOPOLOGY IS THE PAIR, so the refusal is about the combination rather
        // than about the scheme. This is the arrangement the in-process test fixture itself uses.
        JwtAuthenticationOptions relaxedPair = ValidJwt();
        relaxedPair.Authority = "http://security-service:5104";
        relaxedPair.RequireHttpsMetadata = false;

        Assert.Empty(TokenFailures(relaxedPair));

        // BUT THE SHIPPED DOCUMENTS TAKE NEITHER HALF OF THAT RELAXATION - asserted against the real
        // documents so this row cannot be read as licence for one.
        JwtAuthenticationOptions shipped = DataServicesSettingsDocuments.BindJwt(
            DataServicesSettingsDocuments.WithDevelopmentOverlay());

        Assert.True(shipped.RequireHttpsMetadata);
        Assert.StartsWith("https://", shipped.Authority, StringComparison.Ordinal);
    }

    /// <summary>
    /// The internal trust anchor is optional, and refused only when present and whitespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THREE STATES, AND THE MIDDLE ONE IS THE FAULT. Empty means "use the platform's default trust
    /// store", a path means "narrow trust to this mounted anchor", and whitespace means neither - a key
    /// begun and not finished. The shipped documents declare the empty form, so a deployment that mounts
    /// no anchor is a supported state rather than an omission.
    /// </para>
    /// <para>
    /// THE VALUE IS DELIBERATELY NOT ECHOED IN THE REFUSAL, and that is a secrets decision rather than a
    /// message-length one: a startup record must not publish a container's secret mount layout, because
    /// the layout tells a reader where to look for everything else that is mounted.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheInternalTrustAnchorIsOptionalAndRefusedOnlyWhenPresentAndBlank()
    {
        // EMPTY - the shipped state, and not a fault.
        DataServicesOptions unset = ValidOptions();

        Assert.Equal(string.Empty, unset.InternalTls.TrustedCaPath);
        Assert.Empty(ServiceFailures(unset));
        Assert.False(unset.InternalTls.IsConfigured);

        // A PATH - narrowed trust, and not a fault. The path names nothing that must exist here: whether
        // the file loads is decided by the loader at startup, so the failure carries the loader's own
        // diagnosis rather than a weaker second copy of it.
        DataServicesOptions configured = ValidOptions();
        configured.InternalTls.TrustedCaPath = "/etc/powerframework/internal-ca.pem";

        Assert.Empty(ServiceFailures(configured));
        Assert.True(configured.InternalTls.IsConfigured);

        // WHITESPACE - the one fault, named with its remedy.
        DataServicesOptions blank = ValidOptions();
        blank.InternalTls.TrustedCaPath = "   ";

        string failure = Assert.Single(ServiceFailures(blank));

        Assert.Contains(
            nameof(InternalTlsTrustOptions.TrustedCaPath),
            failure,
            StringComparison.Ordinal);
        Assert.Contains("remove the key entirely", failure, StringComparison.OrdinalIgnoreCase);

        // AND THE VALUE IS NOT QUOTED BACK. Asserted because the refusal explicitly promises it is not.
        Assert.DoesNotContain("   ", failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// A per-attempt bound that outlasts the total that contains it is refused, on both edges.
    /// </summary>
    /// <param name="attemptSeconds">The per-attempt bound, in seconds.</param>
    /// <remarks>
    /// <para>
    /// UNSET IS THE NORMAL CASE, and it means "bound one attempt by the total" rather than "no bound".
    /// A supplied value has to fit inside the total, because one attempt cannot outlast the request that
    /// contains it - the resilience pipeline refuses that arrangement outright, so catching it here names
    /// the settings key instead of surfacing a framework validation failure against a generated handler
    /// name that appears in no settings file.
    /// </para>
    /// <para>
    /// NO LATENCY CLAIM ATTACHES TO ANY NUMBER HERE. The repository publishes no latency budget anywhere,
    /// so the rule asserted is a RELATIONSHIP between two configured values and not a target for either
    /// (AAP 0.8.5).
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void APerAttemptBoundThatCannotFitInsideTheTotalIsRefused(int attemptSeconds)
    {
        DataServicesOptions options = ValidOptions();

        // The total is the shipped default, so the row observes the relationship rather than two changes.
        Assert.Equal(ClientResilienceOptions.DefaultRequestTimeout, options.Resilience.Persistence.RequestTimeout);

        options.Resilience.Persistence.AttemptTimeout = TimeSpan.FromSeconds(attemptSeconds);

        string failure = Assert.Single(ServiceFailures(options));

        Assert.Contains(
            $"{DataServicesOptions.SectionName}:Resilience:Persistence:AttemptTimeout",
            failure,
            StringComparison.Ordinal);
        Assert.Contains("Leave it unset", failure, StringComparison.Ordinal);

        // AND UNSET IS ACCEPTED, so the row above is about the supplied value and not about the property.
        DataServicesOptions unset = ValidOptions();
        unset.Resilience.Persistence.AttemptTimeout = null;

        Assert.Empty(ServiceFailures(unset));

        // A VALUE THAT FITS IS ACCEPTED TOO, on the other edge, so neither edge is validated by accident.
        DataServicesOptions fits = ValidOptions();
        fits.Resilience.Security.AttemptTimeout =
            ClientResilienceOptions.DefaultRequestTimeout;

        Assert.Empty(ServiceFailures(fits));
    }
}
