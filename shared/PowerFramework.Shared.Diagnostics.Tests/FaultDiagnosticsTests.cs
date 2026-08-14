// ==============================================================================================
//  FaultDiagnosticsTests - the suite for the two primitives that make a log record safe to write
//  --------------------------------------------------------------------------------------------
//  UNDER TEST     shared/PowerFramework.Shared.Diagnostics/FaultDiagnostics.cs
//
//  NO ORACLE, AND THAT IS A FINDING RATHER THAN A GAP. The legacy library has no log pipeline: its
//  diagnostic channel is a modal dialog on the operator's own screen. Both types under test exist
//  because decomposition replaced that dialog with a record that is retained, shipped off the host
//  and read by parties who are not the caller, so there is nothing in ws_objects/** to compare
//  against and every assertion below is derived from the boundary rather than from a recording.
//
//  WHAT THESE TESTS ARE ACTUALLY GUARDING
//  --------------------------------------------------------------------------------------------
//  Both defects they close were INVISIBLE to the tests that existed. A capturing test logger
//  records `formatter(state, exception)`, which renders the message TEMPLATE and its arguments and
//  does NOT render the exception argument - so a suite asserting "the statement is absent from the
//  record" passed while every real provider printed the statement, its whole inner chain and the
//  stack through ToString(). That is why the assertions here read the exception argument itself
//  wherever a record is involved, and why the escaping assertions are written as round trips
//  rather than as "does not contain a newline".
// ==============================================================================================

using System.Globalization;
using System.Reflection;
using PowerFramework.Shared.Diagnostics;
using Xunit;

namespace PowerFramework.Shared.Diagnostics.Tests;

/// <summary>
/// The suite for <see cref="ExceptionChain"/>.
/// </summary>
public sealed class ExceptionChainTests
{
    /// <summary>
    /// A single fault is named by its namespace-qualified type and nothing else.
    /// </summary>
    /// <remarks>
    /// THE MESSAGE IS ASSERTED ABSENT, not merely unasserted. The whole purpose of the member is that
    /// it reads no message, so a row that only checked the type name would pass against an
    /// implementation that appended the message too.
    /// </remarks>
    [Fact]
    public void ASingleFaultIsNamedByItsQualifiedType()
    {
        InvalidOperationException fault = new("a message nobody may publish");

        string described = ExceptionChain.DescribeTypes(fault);

        Assert.Equal(typeof(InvalidOperationException).FullName, described);
        Assert.DoesNotContain("nobody may publish", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A wrapped fault names every link, outermost first, separated by the published separator.
    /// </summary>
    /// <remarks>
    /// OUTERMOST FIRST IS THE CONTRACT AND IS ASSERTED BY POSITION rather than by containment: a
    /// description that listed the links innermost-first would contain both names and would tell a
    /// reader the causation ran the other way.
    /// </remarks>
    [Fact]
    public void AWrappedFaultNamesEveryLinkOutermostFirst()
    {
        Exception fault = new InvalidOperationException(
            "outer",
            new FormatException("middle", new TimeoutException("inner")));

        string described = ExceptionChain.DescribeTypes(fault);

        Assert.Equal(
            string.Join(
                ExceptionChain.Separator,
                typeof(InvalidOperationException).FullName,
                typeof(FormatException).FullName,
                typeof(TimeoutException).FullName),
            described);
    }

    /// <summary>
    /// A chain at the bound is described in full and carries NO truncation marker.
    /// </summary>
    /// <remarks>
    /// THE OFF-BY-ONE IS THE POINT. A marker appended at exactly the bound would tell every reader of
    /// every eight-deep chain that something had been withheld when nothing had, and a marker omitted
    /// one link too late would hide a real omission. Both boundaries are therefore stated: this row
    /// for the bound and the next for one past it.
    /// </remarks>
    [Fact]
    public void AChainExactlyAtTheBoundIsCompleteAndUnmarked()
    {
        string described = ExceptionChain.DescribeTypes(Nest(ExceptionChain.MaximumDepth));

        Assert.Equal(
            ExceptionChain.MaximumDepth,
            described.Split(ExceptionChain.Separator, StringSplitOptions.None).Length);
        Assert.DoesNotContain(ExceptionChain.TruncationMarker, described, StringComparison.Ordinal);
    }

    /// <summary>
    /// A chain one link deeper than the bound is truncated AND says so.
    /// </summary>
    [Fact]
    public void AChainPastTheBoundIsTruncatedAndSaysSo()
    {
        string described = ExceptionChain.DescribeTypes(Nest(ExceptionChain.MaximumDepth + 1));

        string[] links = described.Split(ExceptionChain.Separator, StringSplitOptions.None);

        // The bound's worth of links plus the marker, which occupies a link position of its own.
        Assert.Equal(ExceptionChain.MaximumDepth + 1, links.Length);
        Assert.Equal(ExceptionChain.TruncationMarker, links[^1]);
    }

    /// <summary>
    /// A cyclic chain terminates at the bound rather than building a string until memory runs out.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS THE REASON THE BOUND EXISTS AND IT IS NOT HYPOTHETICAL.</b> Nothing in the platform
    /// prevents an exception from being its own ancestor - aggregation is enough - and an unbounded
    /// walk over one would exhaust memory WHILE HANDLING A FAULT, replacing the diagnosis being
    /// written with a second, worse failure. The cycle is built by reflection because the inner
    /// exception is otherwise settable only through a constructor, which cannot name the object it is
    /// constructing.
    /// </remarks>
    [Fact]
    public void ACyclicChainTerminatesAtTheBound()
    {
        InvalidOperationException fault = new("self");

        typeof(Exception)
            .GetField("_innerException", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(fault, fault);

        string described = ExceptionChain.DescribeTypes(fault);

        string[] links = described.Split(ExceptionChain.Separator, StringSplitOptions.None);

        Assert.Equal(ExceptionChain.MaximumDepth + 1, links.Length);
        Assert.Equal(ExceptionChain.TruncationMarker, links[^1]);
    }

    /// <summary>
    /// No fault describes as the published absence rather than as a word or a throw.
    /// </summary>
    /// <remarks>
    /// NULL IS ACCEPTED SO THAT TWENTY CALL SITES DO NOT EACH GUARD, and twenty guards written twenty
    /// times is twenty chances to write one wrongly. A throw here would be the worst of the three
    /// behaviours: it would raise a second fault from inside the handling of the first.
    /// </remarks>
    [Fact]
    public void NoFaultDescribesAsTheAbsence()
    {
        Assert.Equal(ExceptionChain.Absent, ExceptionChain.DescribeTypes(null));
        Assert.Equal(
            ExceptionChain.Absent,
            ExceptionChain.DescribeMessages(null, static text => text));
    }

    /// <summary>
    /// EVERY message in a chain goes through the redactor, not only the outermost one.
    /// </summary>
    /// <remarks>
    /// <b>THE INNER LINK IS WHERE THE SECRET ACTUALLY IS.</b> A provider fault arrives wrapped - a
    /// task fault around a command fault around the driver's own - so the generated statement with its
    /// interpolated literal values sits at the BOTTOM of the chain. An implementation that redacted
    /// only the outermost message would leave the ordinary case fully exposed and would still satisfy
    /// a test written with a single flat exception, which is why this row nests one.
    /// </remarks>
    [Fact]
    public void EveryMessageInTheChainGoesThroughTheRedactor()
    {
        const string Literal = "O'Hara-super-secret-salary-99999";

        Exception fault = new InvalidOperationException(
            "the update task faulted",
            new InvalidOperationException(
                $"UPDATE COMPANY SET NAME = '{Literal}' WHERE ID = 7"));

        int calls = 0;

        string described = ExceptionChain.DescribeMessages(
            fault,
            text =>
            {
                calls++;

                return text.Replace(Literal, "<redacted>", StringComparison.Ordinal);
            });

        Assert.Equal(2, calls);
        Assert.DoesNotContain(Literal, described, StringComparison.Ordinal);
        Assert.Contains("<redacted>", described, StringComparison.Ordinal);

        // The surrounding text SURVIVES, which is what makes the record still worth reading: an
        // implementation that dropped every message would also pass the absence assertion above.
        Assert.Contains("the update task faulted", described, StringComparison.Ordinal);
        Assert.Contains("UPDATE COMPANY SET NAME", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// The redactor is mandatory: there is no way to reach a message without naming a policy.
    /// </summary>
    /// <remarks>
    /// ASSERTED BECAUSE THE ALTERNATIVE WAS AVAILABLE AND WAS REJECTED. An overload defaulting to
    /// pass-through would have been convenient and would have been forgotten, and the failure would
    /// have been silent - an unredacted message looks exactly like a redacted one that had nothing to
    /// remove.
    /// </remarks>
    [Fact]
    public void TheRedactorIsMandatory() =>
        Assert.Throws<ArgumentNullException>(() =>
            ExceptionChain.DescribeMessages(new InvalidOperationException("x"), null!));

    /// <summary>
    /// A redactor that answers null contributes an empty link rather than faulting the description.
    /// </summary>
    /// <remarks>
    /// THIS RUNS INSIDE FAULT HANDLING, so a null-reference raised here would replace the diagnosis
    /// being written with one about the diagnostics - the second failure would be the only one anybody
    /// ever saw.
    /// </remarks>
    [Fact]
    public void ARedactorThatAnswersNullContributesAnEmptyLink()
    {
        string described = ExceptionChain.DescribeMessages(
            new InvalidOperationException("outer", new FormatException("inner")),
            static _ => null);

        Assert.Equal(ExceptionChain.Separator, described);
    }

    /// <summary>
    /// The message walk is bounded by the same depth as the type walk.
    /// </summary>
    /// <remarks>
    /// THE TWO BOUNDS MUST AGREE, because the two members are read as a pair on one record: a reader
    /// who saw eight types and nine messages, or the marker on one and not the other, would have no
    /// way to line them up.
    /// </remarks>
    [Fact]
    public void TheMessageWalkIsBoundedByTheSameDepth()
    {
        string described = ExceptionChain.DescribeMessages(
            Nest(ExceptionChain.MaximumDepth + 3),
            static text => text);

        string[] links = described.Split(ExceptionChain.Separator, StringSplitOptions.None);

        Assert.Equal(ExceptionChain.MaximumDepth + 1, links.Length);
        Assert.Equal(ExceptionChain.TruncationMarker, links[^1]);
    }

    /// <summary>
    /// Builds a chain of the requested depth, each link a distinct message.
    /// </summary>
    /// <param name="depth">How many exceptions to nest.</param>
    /// <returns>The outermost exception.</returns>
    private static Exception Nest(int depth)
    {
        Exception current = new InvalidOperationException("link-0");

        for (int level = 1; level < depth; level++)
        {
            current = new InvalidOperationException(
                "link-" + level.ToString(CultureInfo.InvariantCulture),
                current);
        }

        return current;
    }
}

/// <summary>
/// The suite for <see cref="LogSafeText"/>.
/// </summary>
public sealed class LogSafeTextTests
{
    /// <summary>
    /// Ordinary printable text passes through byte for byte.
    /// </summary>
    /// <remarks>
    /// <b>ASSERTED FIRST BECAUSE IT IS THE COMMON CASE AND THE MOST EASILY BROKEN ONE.</b> Every
    /// legitimate handle, session identifier, column name and database name in this estate is ordinary
    /// printable text, so a rendering that altered them - by percent-encoding, quoting or
    /// allowlisting - would make every record harder to read in exchange for nothing. This is not a
    /// sanitiser and does not decide which characters belong in an identifier.
    /// </remarks>
    [Theory]
    [InlineData("dw-company-001")]
    [InlineData("s-1/expression/7")]
    [InlineData("COMPANY.NAME")]
    [InlineData("powerframework-gateway")]
    [InlineData("张三")]
    public void OrdinaryTextPassesThroughUnchanged(string value) =>
        Assert.Equal(value, LogSafeText.Render(value));

    /// <summary>
    /// A null or empty value renders as the published marker rather than as nothing.
    /// </summary>
    /// <remarks>
    /// A BLANK SLOT IN A RECORD IS AMBIGUOUS: it reads as a log statement whose template lost an
    /// argument rather than as a caller that sent an empty value, and those two call for opposite
    /// investigations.
    /// </remarks>
    [Fact]
    public void AnAbsentValueRendersAsTheMarker()
    {
        Assert.Equal(LogSafeMarker, LogSafeText.Render(null));
        Assert.Equal(LogSafeMarker, LogSafeText.Render(string.Empty));
    }

    /// <summary>
    /// Every line terminator is escaped, so a caller cannot append a record of its own.
    /// </summary>
    /// <param name="terminator">The terminator a caller would inject.</param>
    /// <remarks>
    /// <para>
    /// <b>THE FORGED RECORD IS THE WHOLE ATTACK AND IT NEEDS NO SOPHISTICATION.</b> Every console,
    /// file and syslog provider renders a structured record to a LINE, so a value carrying a line
    /// break appends a complete second record - same shape, same channel, with whatever severity,
    /// service name and outcome the caller wrote into it - and nothing downstream can tell the two
    /// apart.
    /// </para>
    /// <para>
    /// THE LAST TWO ROWS ARE THE ONES A HAND-WRITTEN FILTER MISSES. <c>U+2028</c> and <c>U+2029</c>
    /// are NOT control characters by <see cref="char.IsControl(char)"/>, and they are line breaks to
    /// JSON-adjacent and JavaScript-adjacent consumers - so a pipeline that ships records as JSON is
    /// forgeable through them alone.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void EveryLineTerminatorIsEscaped(string terminator)
    {
        string rendered = LogSafeText.Render("head" + terminator + "warn: forged record");

        // The terminator itself is gone from the rendering, character by character.
        foreach (char character in terminator)
        {
            Assert.DoesNotContain(character.ToString(), rendered, StringComparison.Ordinal);
            Assert.Contains(
                "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                rendered,
                StringComparison.Ordinal);
        }

        // AND THE PAYLOAD IS STILL THERE, on one line. Dropping it would also pass the assertion
        // above while destroying the record's only usable content.
        Assert.Contains("head", rendered, StringComparison.Ordinal);
        Assert.Contains("warn: forged record", rendered, StringComparison.Ordinal);
        Assert.Single(rendered.Split('\n'));
    }

    /// <summary>
    /// The escaping is INJECTIVE: two distinct values never render alike.
    /// </summary>
    /// <remarks>
    /// <b>THIS IS WHY IT IS AN ESCAPE AND NOT A FILTER.</b> Replacing every offending character with
    /// one fixed substitute would be simpler and would destroy the record's usefulness: an operator
    /// investigating two failures could no longer tell whether they involved one handle or two, and a
    /// caller could deliberately make its handle render as another caller's. The doubled backslash is
    /// what closes the last gap - without it the literal six characters of an escape and a real
    /// escaped newline would render identically.
    /// </remarks>
    [Fact]
    public void TheEscapingIsInjective()
    {
        string[] distinct =
        [
            "a\nb",
            "a\rb",
            @"a\u000Ab",
            @"a\\u000Ab",
            "a\\b",
            "ab",
        ];

        string[] rendered = [.. distinct.Select(static value => LogSafeText.Render(value))];

        Assert.Equal(distinct.Length, rendered.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// A value longer than the bound is truncated and its ORIGINAL length is recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE SECOND ATTACK IS SIZE. These values are read from request messages, so a caller chooses
    /// their length: one request carrying a multi-megabyte handle produced a multi-megabyte record,
    /// and a loop of them fills whatever the records are written to - taking the service down by way
    /// of its diagnostics rather than by way of its endpoints.
    /// </para>
    /// <para>
    /// THE LENGTH IS EVIDENCE RATHER THAN DECORATION. "Longer than the bound" tells an operator almost
    /// nothing; "4096 characters" distinguishes an unusual name from a payload aimed at the log.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOversizeValueIsTruncatedAndItsLengthRecorded()
    {
        string oversize = new('h', 4096);

        string rendered = LogSafeText.Render(oversize);

        Assert.StartsWith(new string('h', LogSafeText.DefaultMaximumLength), rendered, StringComparison.Ordinal);
        Assert.Contains(
            LogSafeText.TruncationPrefix + "4096" + LogSafeText.TruncationSuffix,
            rendered,
            StringComparison.Ordinal);

        // The BODY is bounded even though the evidence is appended past it, which is the point: the
        // appended text is a fixed shape whose length a caller cannot influence beyond the digits of
        // its own length.
        Assert.Equal(
            LogSafeText.DefaultMaximumLength,
            rendered.IndexOf(LogSafeText.TruncationPrefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// A truncation never splits an escape sequence.
    /// </summary>
    /// <remarks>
    /// <b>THE BOUND IS CHECKED BEFORE EACH UNIT IS APPENDED, NOT AFTER, AND THIS IS THE ROW THAT
    /// PROVES IT.</b> Truncating the finished text would cut a six-character escape in half and emit
    /// something like <c>\u00</c> - neither the value nor a valid escape, and exactly the kind of
    /// half-rendered token a consumer parses wrongly. The value is built so that the bound falls
    /// inside an escape rather than between two.
    /// </remarks>
    [Fact]
    public void ATruncationNeverSplitsAnEscape()
    {
        // Ten newlines render as sixty characters, so a bound of 57 falls three characters into the
        // tenth escape.
        string rendered = LogSafeText.Render(new string('\n', 10), maximumLength: 57);

        string body = rendered[..rendered.IndexOf(LogSafeText.TruncationPrefix, StringComparison.Ordinal)];

        // Nine whole escapes, and no partial tenth.
        Assert.Equal(54, body.Length);
        Assert.Equal(string.Concat(Enumerable.Repeat(@"\u000A", 9)), body);
    }

    /// <summary>
    /// A bound below the published minimum is refused rather than silently widened.
    /// </summary>
    /// <param name="maximumLength">The bound a caller asked for.</param>
    /// <remarks>
    /// A SILENTLY WIDENED BOUND IS A BOUND NOBODY IS ENFORCING, and a bound too small to hold one
    /// escape plus evidence of truncation would produce a record that says nothing at all.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(LogSafeText.MinimumMaximumLength - 1)]
    public void ABoundBelowTheMinimumIsRefused(int maximumLength) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LogSafeText.Render("value", maximumLength));

    /// <summary>
    /// The published minimum itself is accepted, so the boundary is inclusive.
    /// </summary>
    [Fact]
    public void ThePublishedMinimumIsAccepted() =>
        Assert.Equal(
            "value",
            LogSafeText.Render("value", LogSafeText.MinimumMaximumLength));

    /// <summary>The marker an absent value renders as, named once.</summary>
    private const string LogSafeMarker = LogSafeText.EmptyMarker;
}
