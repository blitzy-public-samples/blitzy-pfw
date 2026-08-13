// ==============================================================================================
//  FaultDiagnostics - the two primitives that make a log record safe to write
//  --------------------------------------------------------------------------------------------
//  NO LEGACY SOURCE. Both types below exist BECAUSE of the decomposition and have no counterpart
//  in ws_objects/**, so neither is a port and neither carries a locator.
//
//  The legacy is a library with no process, no listener and no log pipeline: its diagnostic
//  channel is a modal dialog shown to the operator sitting at the machine, and the operator was
//  already entitled to everything on the screen. Decomposition replaces that dialog with a log
//  record that is retained, shipped off the host, indexed and searched by people and systems that
//  are NOT the caller - which is a new trust boundary and therefore a new obligation. AAP 0.1.4
//  states the general form of this: every newly created surface is authenticated from the outset;
//  a log record is the one new surface that cannot be, so its CONTENT is what has to be bounded.
//
//  TWO DISTINCT PROBLEMS, TWO TYPES, AND THEY ARE NOT INTERCHANGEABLE
//  --------------------------------------------------------------------------------------------
//      ExceptionChain  Describes a fault WITHOUT reading anything a caller or an upstream chose.
//                      The problem it solves: passing an exception OBJECT to the logging
//                      abstraction makes every provider render it with ToString(), which prints
//                      the unredacted message, EVERY inner exception's unredacted message and the
//                      stack. A service that carefully redacts one placeholder and then attaches
//                      the exception beside it has redacted nothing.
//
//      LogSafeText     Renders a string a caller chose so that it cannot forge, split or flood a
//                      record. The problem it solves: a message template argument is written
//                      verbatim, so a caller-supplied handle containing a line break can append a
//                      whole fabricated record of its own, and a megabyte-long one can fill a
//                      disk one call at a time.
//
//  WHY THEY LIVE HERE RATHER THAN IN A SERVICE
//  --------------------------------------------------------------------------------------------
//  Persistence and DataServices both need them, Gateway's client edge needs the first, and three
//  independent copies of a security control drift - which is exactly how the estate came to have
//  two identical private fault-chain walkers with the same three constants in two services.
//  Neither type carries a policy: ExceptionChain never reads a message except through a redactor
//  its CALLER supplies, so Persistence's SQL-redaction rules stay in Persistence and this
//  assembly stays free of any knowledge of statements, credentials or connection strings.
//
//  AAP 0.8.5 - no duration, no size and no count below is a performance target. Every bound here
//  is a correctness bound on what a record may contain.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace PowerFramework.Shared.Diagnostics;

/// <summary>
/// Describes an exception chain for a log record without rendering the exception itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>A TYPE NAME IS ALLOWLISTED CONTENT AND A MESSAGE IS NOT.</b> Every name
/// <see cref="DescribeTypes"/> produces comes from this codebase, the framework or a package, so
/// none of them can carry a value a caller sent, a statement a service generated, a file-system
/// path or a configuration key. The chain therefore identifies WHICH failure occurred and where it
/// came from while disclosing nothing, which is what makes it publishable where the exception is
/// not.
/// </para>
/// <para>
/// <b>THE CHAIN IS WALKED BECAUSE THE INTERESTING TYPE IS RARELY THE OUTERMOST ONE.</b> A provider
/// fault arrives wrapped - a task fault wrapping a command fault wrapping the driver's own - so a
/// description of the outermost exception alone routinely says only that something inside a task
/// failed. The same is true of the messages: the statement text sits at the BOTTOM of the chain,
/// which is why <see cref="DescribeMessages"/> redacts every link rather than the first.
/// </para>
/// <para>
/// Every member is a pure function of its arguments. There is no mutable state, so all members are
/// safe to call concurrently from any thread.
/// </para>
/// </remarks>
public static class ExceptionChain
{
    /// <summary>
    /// The separator between links of a described chain, outermost towards innermost.
    /// </summary>
    /// <remarks>
    /// AN ARROW POINTING BACKWARDS, because the order is outermost first and the arrow is what tells
    /// a reader which way the causation runs without a legend. The exact text is fixed rather than
    /// derived so that a log pipeline can split on it.
    /// </remarks>
    public const string Separator = " <- ";

    /// <summary>
    /// The marker appended when a chain is deeper than <see cref="MaximumDepth"/>.
    /// </summary>
    /// <remarks>
    /// PRESENT SO THAT A SHORTENED CHAIN IS NEVER MISTAKEN FOR A COMPLETE ONE. Without it a
    /// truncated description reads as a full diagnosis that happens to stop at an uninteresting
    /// type, and a reader would conclude the cause was the eighth link when it was the ninth.
    /// </remarks>
    public const string TruncationMarker = "...";

    /// <summary>
    /// How many links of a chain are described before truncation.
    /// </summary>
    /// <remarks>
    /// <b>BOUNDED BECAUSE A CHAIN CAN BE CYCLIC.</b> Nothing prevents an exception from being its
    /// own ancestor through aggregation, and an unbounded walk over one would build a string until
    /// the process ran out of memory - while handling a fault, which is the worst possible moment to
    /// raise a second one. Eight is the depth the two services that already had private copies of
    /// this walk both chose, and it is preserved rather than re-derived so that no existing record's
    /// shape changes.
    /// </remarks>
    public const int MaximumDepth = 8;

    /// <summary>
    /// The value returned when there is no exception to describe.
    /// </summary>
    /// <remarks>
    /// EMPTY RATHER THAN A WORD, because both members are called from a message template argument
    /// where an absent chain should render as an absence. A caller that wants a visible marker owns
    /// that choice; see <see cref="LogSafeText.EmptyMarker"/> for the case where a blank slot in a
    /// record WOULD be ambiguous.
    /// </remarks>
    public const string Absent = "";

    /// <summary>
    /// Names the types in an exception chain, outermost first, without reading any message.
    /// </summary>
    /// <param name="error">The fault to describe, or <see langword="null"/>.</param>
    /// <returns>
    /// The namespace-qualified type names joined outermost-first, with
    /// <see cref="TruncationMarker"/> appended when the chain is deeper than
    /// <see cref="MaximumDepth"/>; <see cref="Absent"/> when <paramref name="error"/> is
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// THE NAMESPACE-QUALIFIED NAME IS USED, NOT THE SHORT ONE, because the short name is what makes
    /// two unrelated failures look like one: this estate and its packages between them declare
    /// several same-named exception types, and a record naming only the last segment sends a reader
    /// to the wrong assembly. <see cref="Type.FullName"/> is null for a few constructed generic
    /// forms, so <see cref="MemberInfo.Name"/> is the fallback rather than an empty slot.
    /// </para>
    /// <para>
    /// NULL IS ACCEPTED SO THAT CALL SITES DO NOT EACH GUARD. This is reached from catch blocks and
    /// from fault-capture fields that may or may not hold one, and twenty guards written twenty
    /// times is twenty chances to write one wrongly.
    /// </para>
    /// </remarks>
    public static string DescribeTypes(Exception? error)
    {
        if (error is null)
        {
            return Absent;
        }

        StringBuilder chain = new();
        Exception? current = error;

        for (int depth = 0; depth < MaximumDepth && current is not null; depth++)
        {
            if (depth > 0)
            {
                _ = chain.Append(Separator);
            }

            Type type = current.GetType();

            _ = chain.Append(type.FullName ?? type.Name);
            current = current.InnerException;
        }

        if (current is not null)
        {
            _ = chain.Append(Separator).Append(TruncationMarker);
        }

        return chain.ToString();
    }

    /// <summary>
    /// Redacts the message of every exception in a chain and joins them outermost first.
    /// </summary>
    /// <param name="error">The fault whose chain is described, or <see langword="null"/>.</param>
    /// <param name="redact">
    /// The caller's redaction policy, applied to EVERY message in the chain.
    /// </param>
    /// <returns>
    /// The redacted messages joined outermost-first, with <see cref="TruncationMarker"/> appended
    /// when the chain is deeper than <see cref="MaximumDepth"/>; <see cref="Absent"/> when
    /// <paramref name="error"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="redact"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>THE POLICY IS THE CALLER'S AND IS MANDATORY, WHICH IS THE ENTIRE DESIGN OF THIS MEMBER.</b>
    /// There is deliberately no overload that omits it and no default that passes text through:
    /// a redactor that could be forgotten would be forgotten, and the failure would be silent
    /// because an unredacted message looks exactly like a redacted one that had nothing to remove.
    /// Making it a required argument means the only way to reach a message is to name a policy.
    /// </para>
    /// <para>
    /// AND THE POLICY STAYS OUT OF THIS ASSEMBLY. Persistence hands its SQL redactor, which knows
    /// about statements, connection strings and driver envelopes; this assembly knows about none of
    /// them and cannot be made to disagree with the wire path, because both go through the same
    /// object.
    /// </para>
    /// <para>
    /// A REDACTOR THAT RETURNS NULL IS TREATED AS HAVING RETURNED EMPTY rather than being allowed to
    /// throw. This runs inside fault handling, so a second fault raised here would replace the
    /// diagnosis being written with one about the diagnostics.
    /// </para>
    /// </remarks>
    public static string DescribeMessages(Exception? error, Func<string, string?> redact)
    {
        ArgumentNullException.ThrowIfNull(redact);

        if (error is null)
        {
            return Absent;
        }

        StringBuilder messages = new();
        Exception? current = error;

        for (int depth = 0; depth < MaximumDepth && current is not null; depth++)
        {
            if (depth > 0)
            {
                _ = messages.Append(Separator);
            }

            _ = messages.Append(redact(current.Message) ?? string.Empty);
            current = current.InnerException;
        }

        if (current is not null)
        {
            _ = messages.Append(Separator).Append(TruncationMarker);
        }

        return messages.ToString();
    }
}

/// <summary>
/// Renders a caller-supplied string so that it cannot forge, split or flood a log record.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE ATTACK IS A LINE BREAK AND IT NEEDS NO SOPHISTICATION.</b> A structured log record is
/// still rendered to a line of text by every console, file and syslog provider, so a caller that
/// puts a newline inside a value it controls - a DataWindow handle, a session identifier, a column
/// name, a database name, a token subject - appends a complete record of its own choosing after the
/// real one. The fabricated record is indistinguishable from a genuine one to everything downstream:
/// it is the same shape, it arrives on the same channel, and it carries whatever severity, service
/// name and outcome the caller wrote into it.
/// </para>
/// <para>
/// <b>AND THE SECOND ONE IS SIZE.</b> These values are read from request messages, so a caller
/// chooses their length. One request carrying a multi-megabyte handle produces a multi-megabyte
/// record; a loop of them fills whatever the records are written to, which takes the service down
/// by way of its diagnostics rather than by way of its endpoints.
/// </para>
/// <para>
/// <b>THE ESCAPE IS INJECTIVE, WHICH IS WHY IT IS AN ESCAPE AND NOT A FILTER.</b> Replacing every
/// offending character with a single fixed substitute would make distinct inputs render identically,
/// so a record could no longer be trusted to identify WHICH handle a caller sent - and an operator
/// investigating two failures could not tell whether they involved one value or two. Every rendered
/// form here maps back to exactly one input: a backslash becomes a doubled backslash and everything
/// unsafe becomes a <c>\uXXXX</c> escape, so the rendering is reversible and no two values collide.
/// </para>
/// <para>
/// Every member is a pure function of its arguments. There is no mutable state, so all members are
/// safe to call concurrently from any thread.
/// </para>
/// </remarks>
public static class LogSafeText
{
    /// <summary>
    /// The rendered length at which a value is truncated when no bound is given.
    /// </summary>
    /// <remarks>
    /// A BOUND ON A RECORD, NOT A LIMIT ON A REQUEST. Nothing here refuses a long value or changes
    /// what reaches the wire; it changes only how much of one is written to a log. The value is
    /// generous relative to every identifier this estate actually issues - a session identifier, a
    /// task handle, a column name and a database name are all far shorter - so a truncated record
    /// signals a value that was not one of those.
    /// </remarks>
    public const int DefaultMaximumLength = 128;

    /// <summary>
    /// The smallest bound <see cref="Render"/> accepts.
    /// </summary>
    /// <remarks>
    /// A BOUND BELOW THIS COULD NOT HOLD ONE ESCAPE UNIT PLUS EVIDENCE OF TRUNCATION, so a caller
    /// asking for one has asked for a record that says nothing at all. Refused rather than silently
    /// widened, because a silently widened bound is a bound nobody is enforcing.
    /// </remarks>
    public const int MinimumMaximumLength = 8;

    /// <summary>
    /// The rendering of a value that is <see langword="null"/> or empty.
    /// </summary>
    /// <remarks>
    /// <b>A WORD RATHER THAN A BLANK, BECAUSE A BLANK SLOT IN A RECORD IS AMBIGUOUS.</b> An empty
    /// argument renders as nothing at all, which reads as a log statement whose template lost an
    /// argument rather than as a caller that sent an empty value - and those two call for opposite
    /// investigations. The spelling matches the marker the estate's own log sites already used for
    /// this case before it was centralised, so no existing record changes shape.
    /// </remarks>
    public const string EmptyMarker = "(none)";

    /// <summary>
    /// The opening of the evidence appended to a truncated rendering.
    /// </summary>
    /// <remarks>
    /// THE ORIGINAL LENGTH IS PART OF THE EVIDENCE, not decoration. "This value was longer than the
    /// bound" tells an operator almost nothing; "this value was 4096 characters" distinguishes a name
    /// that is merely unusual from a payload aimed at the log itself.
    /// </remarks>
    public const string TruncationPrefix = "...(length=";

    /// <summary>
    /// The closing of the evidence appended to a truncated rendering.
    /// </summary>
    public const string TruncationSuffix = ")";

    /// <summary>
    /// Renders a value for a log record: control characters escaped, length bounded.
    /// </summary>
    /// <param name="value">The value a caller supplied, or <see langword="null"/>.</param>
    /// <param name="maximumLength">
    /// The greatest rendered length before truncation. Defaults to
    /// <see cref="DefaultMaximumLength"/> and must be at least
    /// <see cref="MinimumMaximumLength"/>.
    /// </param>
    /// <returns>
    /// <see cref="EmptyMarker"/> for a null or empty value; otherwise the escaped value, followed by
    /// <see cref="TruncationPrefix"/>, the value's original length and
    /// <see cref="TruncationSuffix"/> when it did not fit.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maximumLength"/> is less than <see cref="MinimumMaximumLength"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>WHAT IS ESCAPED, AND WHY THE SET IS WIDER THAN "CONTROL CHARACTER".</b> Three families are
    /// unsafe and all three are escaped:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// Every character <see cref="char.IsControl(char)"/> reports - which covers the line
    /// terminators that forge a record, the tab that forges a column in a delimited format, and the
    /// C1 range including <c>U+0085</c> NEXT LINE, which several renderers treat as a break.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>U+2028</c> LINE SEPARATOR and <c>U+2029</c> PARAGRAPH SEPARATOR, which are NOT control
    /// characters by that predicate and are exactly the two a filter written against it would let
    /// through. They are line breaks to JSON-adjacent and JavaScript-adjacent consumers, so a
    /// pipeline that ships records as JSON is forgeable through them alone.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// The backslash itself, doubled. Not because a backslash is dangerous but because the escapes
    /// above use one: without this a caller could send the literal six characters of a
    /// <c>\u000A</c> escape and a reader could not tell that from an escaped newline, which would
    /// give back the ambiguity the escaping exists to remove.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// <b>THE BOUND IS CHECKED BEFORE EACH UNIT IS APPENDED, NOT AFTER.</b> Truncating the finished
    /// text would cut a six-character escape in half and emit something like <c>\u00</c>, which is
    /// neither the value nor a valid escape and is precisely the kind of half-rendered token a
    /// consumer parses wrongly. Appending whole units only means the rendering is always a
    /// well-formed prefix of the full one.
    /// </para>
    /// <para>
    /// EVERYTHING ELSE PASSES THROUGH UNCHANGED, deliberately. This is not a sanitiser and does not
    /// try to decide which characters belong in an identifier: a handle of ordinary printable text -
    /// which is what every legitimate value is - renders byte for byte as itself, so the common case
    /// is unaffected and a record stays readable.
    /// </para>
    /// </remarks>
    public static string Render([AllowNull] string value, int maximumLength = DefaultMaximumLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumLength, MinimumMaximumLength);

        if (string.IsNullOrEmpty(value))
        {
            return EmptyMarker;
        }

        StringBuilder rendered = new(Math.Min(value.Length, maximumLength));
        bool truncated = false;

        foreach (char character in value)
        {
            if (NeedsEscaping(character))
            {
                // A backslash costs two and every other escape costs six, so the capacity test is
                // made against the unit that is actually about to be written.
                int width = character == '\\' ? 2 : 6;

                if (rendered.Length + width > maximumLength)
                {
                    truncated = true;

                    break;
                }

                if (character == '\\')
                {
                    _ = rendered.Append(@"\\");
                }
                else
                {
                    _ = rendered.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                }

                continue;
            }

            if (rendered.Length + 1 > maximumLength)
            {
                truncated = true;

                break;
            }

            _ = rendered.Append(character);
        }

        if (truncated)
        {
            _ = rendered
                .Append(TruncationPrefix)
                .Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(TruncationSuffix);
        }

        return rendered.ToString();
    }

    /// <summary>
    /// Whether a character must be escaped rather than written through.
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>Whether it is escaped.</returns>
    /// <remarks>
    /// The two Unicode separators are named explicitly because
    /// <see cref="char.IsControl(char)"/> answers false for both - see the remarks on
    /// <see cref="Render"/> for why letting them through would defeat the whole rendering.
    /// </remarks>
    private static bool NeedsEscaping(char character) =>
        char.IsControl(character)
            || character == '\\'
            || character == LineSeparator
            || character == ParagraphSeparator;

    /// <summary>U+2028 LINE SEPARATOR, a line break to JSON-adjacent consumers.</summary>
    private const char LineSeparator = '\u2028';

    /// <summary>U+2029 PARAGRAPH SEPARATOR, a line break to JSON-adjacent consumers.</summary>
    private const char ParagraphSeparator = '\u2029';
}
