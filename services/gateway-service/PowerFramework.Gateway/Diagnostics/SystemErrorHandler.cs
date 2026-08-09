// ==============================================================================================
//  SystemErrorHandler - the framework system-error protocol, re-expressed at a service boundary
//  --------------------------------------------------------------------------------------------
//  PORTED FROM ws_objects/pfw.pbl.src/pfw.sra:L111-L144 (the `systemerror` event), together
//                 with its two collaborators in the same file: the split helper it consumes at
//                 :L48-L69 and the `close` event at :L108 that its `HALT CLOSE` runs.
//
//  THE NAME TRAP. TWO DISTINCT FILES IN THIS REPOSITORY ARE NAMED pfw.sra. Every one of the many
//  citations of it below - in this banner, in the documentation of every member, and inline at each
//  statement that reproduces one of its statements - therefore carries the FULL path and never the
//  bare filename, without exception. The other five legacy sources have unique filenames, so they
//  are fully pathed once in the two lists below and cited by filename afterwards:
//      ws_objects/pfw.pbl.src/pfw.sra        145 lines, the framework application. AUTHORITATIVE.
//                                            It is the only file in the repository that carries
//                                            this protocol.
//      ws_objects/pfw.pack.pbl.src/pfw.sra    59 lines, the PowerBuilder packager. IRRELEVANT
//                                            here, and verified so rather than assumed: it has no
//                                            `systemerror` event, no `HALT`, no `assert`, no
//                                            `close` event and no split helper, and its `open`
//                                            event does nothing but open the packager window.
//
//  FOUR FURTHER LEGACY PATHS WERE READ AS SPECIFICATION AND ARE EQUALLY READ ONLY (C-C):
//      ws_objects/pfw.common.pbl.src/assert.srf            the PRODUCER of the payload decoded
//                                                          below. It fixes the delimiter, both
//                                                          payload arities, and the literal value
//                                                          of field 1 [assert.srf:L19,L22,L37-L72].
//      ws_objects/pfw.common.pbl.src/assertionfailed.sru   the seven payload members
//                                                          [assertionfailed.sru:L18-L24].
//      ws_objects/pfw.common.pbl.src/stacktraceinfo.srf    how field 7's frame text is built: the
//                                                          frames are joined with a BARE line feed
//                                                          [stacktraceinfo.srf:L33], which is the
//                                                          reason the field split must be CRLF.
//      ws_objects/pfw.tests.pbl.src/w_test_assert.srw      the behavioural oracle. Its third
//                                                          button wraps the failing call in
//                                                          try / catch(AssertionFailed) and formats
//                                                          the members itself at that file's
//                                                          :L97-L102, proving a CAUGHT assertion
//                                                          never reaches the system-error event.
//                                                          This handler is therefore the UNHANDLED
//                                                          path only and must never intercept a
//                                                          fault that application code already
//                                                          handled.
//      docs/README.md section 初始化                        the warning that the framework finalize
//                                                          call MUST be paired with the initialize
//                                                          call and belongs at the END of the
//                                                          application close event. That single
//                                                          line is why `HALT CLOSE` is reproduced
//                                                          with a host shutdown request rather than
//                                                          with a fail-fast abort; see DECISION 4.
//
//  GOVERNING CONSTRAINTS. review_rules reports "No user rules provided" - that one line is the
//  entire rules document, so NO user rule governs this file and none was invented. In their place
//  AAP section 0.7.2 (the enterprise baseline) and section 0.7.3 (the twelve binding non-rule
//  constraints) apply. Eight of the twelve bind this file, and each is discharged where it applies:
//
//      C-B  no behaviour improvements; documented defects replicated. Discharged by QUIRK 1
//           through QUIRK 4 below, each annotated again at its point of reproduction with its
//           ws_objects/pfw.pbl.src/pfw.sra locator, and by the four preserved boundary conditions:
//           the EXACTLY-seven field test, the inequality-only window guard, the parse-to-zero
//           behaviour of the legacy Long() conversion, and the no-empty-trailing-field split.
//      C-C  the legacy tree is read only and is the only specification. Discharged by the six
//           paths above being read and cited, never written. This .cs file is the only write.
//      C-D  the four deferred capabilities do not exist. Nothing here names, imports, types or
//           routes to any of them, and no placeholder stands in for one. A fault surfacing from
//           one of the four reserved not-implemented routes is an ORDINARY request fault and is
//           deliberately not special-cased by name.
//      C-E  no fabricated database. This file opens no connection, holds no storage provider and
//           references no data-access type of any kind. Diagnostics leave through the logging
//           abstraction and nothing else.
//      C-F  no secret may leak. See DECISION 2. In particular the one upstream field known to
//           carry interpolated literal values is redacted or parameter-separated by the service
//           that owns it, and this file neither echoes nor reconstructs it, and never puts an
//           arbitrary upstream exception's own text into a response body.
//      C-G  no new attack surface, nothing disclosed to a caller. See DECISION 2.
//      C-H  the decode and the format steps are pure, public, static, host-free functions over a
//           payload string, and the termination effect is injectable, so every field-count branch
//           and the terminate path are all reachable from a plain unit test with no host. See
//           DECISION 5.
//      C-K  document every technology-specific and boundary-specific decision. Discharged by
//           DECISION 1 through DECISION 6 below.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 1 - THE DIALOG BECOMES A STRUCTURED RESULT, AND ONLY THE DELIVERY CHANNEL CHANGES.
//
//  The legacy renders its report into a single string and shows it in a modal dialog with a stop
//  icon [ws_objects/pfw.pbl.src/pfw.sra:L141]. A headless service in a container has no dialog and
//  no local operator standing at a screen, so the dialog CHANNEL cannot survive. Everything the
//  dialog carried does survive, byte for byte: the title, the severity, the label text, the label
//  order, the punctuation, the separators, and the two conditional sections. Format() returns them
//  as a structured report rather than displaying them, and the report is what reaches the log.
//
//  Nothing else about the message is touched. In particular this site is NOT localized and must not
//  become localized: the legacy composes literal Chinese and calls the plain message box, never the
//  localizing variant, so unlike the DataWindow service, row-select and context-menu messages
//  elsewhere in the estate there is no localization category here at all. That inconsistency is
//  legacy behaviour and is preserved (C-B), which is why this file takes no dependency on the
//  localization library and no dependency on the shared formatting helpers - the legacy
//  concatenates literally and renders no return code into the text.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 2 - TWO CHANNELS, BECAUSE THE LEGACY HAD EXACTLY ONE AUDIENCE AND IT WAS NOT A CALLER.
//
//  The dialog was shown to the LOCAL OPERATOR. No remote party could ever see it, because the
//  legacy framework is an in-process library with no listener, no route and no ingress at all.
//  Decomposition creates the system's first caller, so the single legacy audience splits in two and
//  each half gets what it is entitled to:
//
//      OPERATOR CHANNEL (the faithful successor to the dialog) - a structured log record carrying
//      all seven decoded fields AND the exact formatted Chinese block, with the title and the
//      severity. Nothing is redacted away from the operator, because the operator is who the dialog
//      was for.
//
//      CALLER CHANNEL - a REDACTED problem-details response. Under C-G a caller learns that the
//      request failed, the legacy return code class, and a correlation identifier with which an
//      operator can find the full record. It learns nothing else. Concretely the response body
//      never carries the stack trace, the object-event name, the window-or-menu name, an upstream
//      address, a host name, a port, a file path, a connection string or any token material, and
//      the arbitrary text of an upstream exception is never copied into it. The problem-details
//      context is deliberately handed to the framework WITHOUT its Exception member set, so that no
//      response customization anywhere in the pipeline can reintroduce exception detail into the
//      body through a channel this file does not control.
//
//  The wire shape is not invented here. shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml
//  publishes the one problem-details body this ingress uses, and it is authoritative: the RFC 9457
//  object plus the legacy return code as an extension member. This file conforms to it and defines
//  no competing shape.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 3 - WHICH RETURN CODE REACHES THE WIRE, AND WHY IT IS NOT ALWAYS THE DECODED NUMBER.
//
//  Two authoritative inputs meet here and they do not trivially agree.
//
//      * The decoded number is the machine-readable half of the protocol and should reach the
//        caller when it can.
//      * gateway.v1.yaml publishes the return-code extension member with a CLOSED value set - the
//        legacy catalogue's 38 distinct values - and states in its own description that the
//        unclassifiable case is precisely what the catalogue's UNKNOWN value exists for.
//
//  The assert payload's field 1 is the fixed sentinel value negative ten thousand [assert.srf:L22].
//  That value is NOT a member of the legacy return-code catalogue and therefore not a member of the
//  published set either. Emitting it would widen the published contract beyond what it declares.
//
//  Resolution, which follows AAP section 0.1.5's uniform rule that a contract is narrowed with a
//  defined value rather than widened with a guess: the wire carries the decoded number when that
//  number is a member of the published closed set, and the catalogue's UNKNOWN value otherwise. The
//  raw decoded number ALWAYS reaches the operator channel, so nothing the legacy observed is lost -
//  which is the same split the legacy itself makes, since it leaves the number observable on its
//  global error object while never rendering it (QUIRK 2). The sentinel is never declared as a
//  constant in this file and is never mapped onto a catalogue member.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 4 - `HALT CLOSE` IS A SHUTDOWN REQUEST PLUS A NON-ZERO EXIT CODE. IT IS NOT FailFast.
//
//  A decoded assertion failure TERMINATES the application. That is not softened here into a
//  warning, a degraded mode or a continue: softening it would be a behavioural change dressed up as
//  robustness (C-B).
//
//  But the faithful reproduction is not an abort. PowerBuilder's `HALT CLOSE`
//  [ws_objects/pfw.pbl.src/pfw.sra:L143] runs the application `close` event and only THEN
//  terminates, and that close event is exactly one statement - the framework finalize call
//  [ws_objects/pfw.pbl.src/pfw.sra:L108]. docs/README.md section 初始化 carries the explicit
//  warning that the finalize call must be paired with the initialize call and belongs at the end of
//  the close event. So the ordered pair initialize-then-finalize is a documented framework
//  invariant, and `HALT CLOSE` HONOURS it.
//
//  Therefore termination is requested through the host application lifetime abstraction, which runs
//  the registered shutdown path - where the composition root's finalize step lives - and the
//  process exit code is set non-zero first so an orchestrator observes a failure rather than a
//  clean stop. Calling a fail-fast abort instead would skip the shutdown path entirely and break
//  the documented pairing, which is why it is not used anywhere in this file. The dependency is on
//  the framework's lifetime abstraction rather than on any concrete initializer type, because the
//  abstraction is the correct seam and because it keeps this file independent of the sibling that
//  happens to own the finalize step today.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 5 - THE STRUCTURAL-FAULT / REQUEST-FAULT LINE, DRAWN EXPLICITLY RATHER THAN IMPLIED.
//
//  The legacy has no request boundary whatsoever, so it had no such line to draw: every system
//  error was terminal because there was nothing smaller than the process to fail. Introducing a
//  request boundary is what forces the distinction, and the line is therefore a necessary
//  consequence of the transition rather than a relaxation of the assertion path. It is drawn here,
//  in one place, so it cannot drift:
//
//      STRUCTURAL FAULT -> TERMINATE. A managed assertion failure reaching the unhandled path is a
//      decoded assertion. Log the full diagnostic, write the redacted body, then request shutdown
//      with a non-zero exit code. This is the `HALT CLOSE` path and it is never softened.
//
//      ORDINARY REQUEST FAULT -> SURFACE, DO NOT KILL THE HOST. Any other unhandled exception is
//      formatted and logged through the SAME block - the legacy formatted every system error, not
//      only asserts, because its formatting statements sit OUTSIDE the decode branch
//      [ws_objects/pfw.pbl.src/pfw.sra:L129 versus :L114-L127] - and the redacted body is returned,
//      and the host lives on to serve the next request.
//
//  The second case is not a licence to weaken the first. It exists only because a request can now
//  fail without the process being at fault, a state the legacy could not represent.
//
//  ----------------------------------------------------------------------------------------------
//  DECISION 6 - WHY THE PROBLEM-DETAILS WRITER AND THE TERMINATION EFFECT ARE BOTH INJECTABLE.
//
//  The framework's problem-details service is taken as an OPTIONAL dependency and a direct write of
//  the identical body is the fallback. Two independent reasons: the service is only present when
//  the composition root opted into it, and even when present its write attempt reports whether it
//  actually wrote, so a fallback is needed for correctness rather than merely for robustness. The
//  body is identical on both paths, so a caller cannot tell which ran.
//
//  The termination effect is taken as an optional callback defaulting to the host-backed behaviour
//  of DECISION 4. That is what lets a unit test assert "termination was requested, with this exit
//  code" without stopping or failing the test host (C-H). There is exactly ONE constructor, so the
//  dependency-injection container has no overload to choose between; the two optional parameters
//  are filled from their defaults when nothing is registered for them.
//
//  ----------------------------------------------------------------------------------------------
//  THE FOUR PRESERVED QUIRKS (C-B). Each is verified against ws_objects/pfw.pbl.src/pfw.sra, each
//  is exactly what a well-meaning implementer tidies away, and each is annotated again at the line
//  that reproduces it:
//
//   QUIRK 1  The type label is the hardcoded literal SYSTEM even on the assert-decoded path
//            [ws_objects/pfw.pbl.src/pfw.sra:L129]. A fully decoded assertion still reports the
//            SYSTEM type. The type is never
//            derived from the payload and no additional type value exists.
//   QUIRK 2  The number is decoded [ws_objects/pfw.pbl.src/pfw.sra:L117] and then NEVER rendered in
//            the formatted block. It is
//            parsed and discarded from display. It is kept as a field of the decoded record - the
//            machine-readable half, which the legacy likewise leaves observable on its global
//            error object - and omitted from the rendered half. That split is the faithful port.
//   QUIRK 3  Separator asymmetry. The payload splits on CRLF [ws_objects/pfw.pbl.src/pfw.sra:L115]
//            while the report joins with a
//            BARE line feed [ws_objects/pfw.pbl.src/pfw.sra:L129-L138]. Neither side is normalized
//            to match the other, and the
//            platform-dependent newline constant is used NOWHERE in this file, because it is a
//            bare line feed on one operating system and a CRLF pair on another and would therefore
//            make both the split and the join silently platform-dependent.
//   QUIRK 4  No localization at this site at all, as set out in DECISION 1.
//
//  THE ONE-BASED ARRAY HAZARD. The legacy split helper is one-based and returns the count, which in
//  a one-based array is also the last valid index [ws_objects/pfw.pbl.src/pfw.sra:L68] - two
//  numbers that coincide there and diverge here. A silent off-by-one would be indistinguishable
//  from a behavioural regression (AAP sections 0.4.5.4 and 0.8.6 R9), so every one of the seven
//  field reads goes through a SINGLE accessor that performs the index translation in one place, and
//  the legacy index appears at each call site as the literal 1 through 7 in legacy order.
//
//  DEPENDENCIES. The Base Class Library and the ASP.NET Core shared framework, plus exactly two
//  internal types: the managed assertion payload from PowerFramework.Shared.Diagnostics and the
//  return-code catalogue from PowerFramework.Shared.Kernel. Both arrive through project references
//  this project already declares. Deliberately NOT imported: the localization library (QUIRK 4),
//  any data-access library (C-E), and any logging package - the logging abstractions used here ship
//  in the shared framework, which is why this project references no logging package at all.
//
//  NO IDENTIFIER IN THIS FILE USES THE PRESERVED SCREAMING-SNAKE SPELLING. The repository root
//  .editorconfig scopes its naming-analyzer suppressions to ten named files and not one of them is
//  in this project, so with warnings treated as errors such a declaration would be a hard build
//  error with no way to grant an exception. Consuming a catalogue constant by name is fine;
//  declaring one here is not.
// ==============================================================================================

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PowerFramework.Shared.Diagnostics;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Gateway.Diagnostics;

/// <summary>
/// The severity a system-error report carries, reproducing the icon argument the legacy passes to
/// its dialog at <c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>.
/// </summary>
/// <remarks>
/// <para>
/// The legacy hardcodes the stop icon at that one call site and offers no other value, so exactly
/// one member has evidence behind it and exactly one member is declared. Adding a warning or an
/// information member would widen the surface past anything the oracle supports, which C-B forbids;
/// the enumeration exists so that severity travels as a typed property of the structured report
/// rather than as a string baked into a log message.
/// </para>
/// <para>
/// The single member carries the value zero deliberately, so the enumeration satisfies the design
/// analyzer that requires a non-flags enumeration to have a zero-valued member.
/// </para>
/// </remarks>
public enum SystemErrorSeverity
{
    /// <summary>
    /// The stop severity - the reproduction of the legacy stop icon
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>]. Every report this file produces carries it,
    /// because the legacy passes it unconditionally.
    /// </summary>
    Stop = 0,
}

/// <summary>
/// The seven fields of the legacy system-error protocol, in the legacy's own field order.
/// </summary>
/// <remarks>
/// <para>
/// This record IS the legacy global error object as the system-error event sees it. The event
/// mutates that object in place - reading the sentinel and the payload from it and writing the
/// decoded fields back onto it [<c>ws_objects/pfw.pbl.src/pfw.sra:L114-L127</c>] - so <see
/// cref="SystemErrorHandler.Decode(SystemErrorInfo?)"/> is shaped as a function from one instance
/// of this record to another, which is the immutable expression of exactly that.
/// </para>
/// <para>
/// The legacy field order is fixed by the payload and is reproduced by the property order below:
/// number, text, window-or-menu, object, object-event, line, stack trace
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L117-L124</c>]. Every string property defaults to the empty
/// string rather than to null, so the formatter never has to test for null and the report never
/// renders a null marker - which matches the legacy, where an unassigned string variable is the
/// empty string.
/// </para>
/// </remarks>
public sealed record SystemErrorInfo
{
    /// <summary>
    /// The failure number. Decoded from payload field 1 through the legacy long conversion
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L117</c>].
    /// </summary>
    /// <remarks>
    /// QUIRK 2. This value is decoded and then never rendered into the formatted block. It is kept
    /// here because it is the machine-readable half of the protocol and because the legacy likewise
    /// leaves it observable on its global error object after decoding it; it is deliberately absent
    /// from <see cref="SystemErrorHandler.Format(SystemErrorInfo?)"/>.
    /// </remarks>
    public long Number { get; init; }

    /// <summary>
    /// The failure text. Before decoding this carries the whole CRLF-joined payload; after a
    /// successful decode it carries payload field 2 alone
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L118</c>].
    /// </summary>
    /// <remarks>
    /// Field 2 legitimately CONTAINS a bare line feed: the producer appends the caller's info text
    /// to it after one [<c>assert.srf:L24-L27</c>]. That is precisely why the field split is CRLF
    /// and not a line feed - see QUIRK 3.
    /// </remarks>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The window-or-menu name. Decoded from payload field 3
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L120</c>].
    /// </summary>
    /// <remarks>
    /// Equality with <see cref="Object"/> is a normal, frequently taken case rather than a
    /// duplication defect: the producer assigns one to the other outright whenever the calling
    /// frame carries a single dot [<c>assert.srf:L49-L51</c>]. That equality is exactly what
    /// suppresses the window line from the rendered report
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L131</c>].
    /// </remarks>
    public string WindowMenu { get; init; } = string.Empty;

    /// <summary>
    /// Before decoding, the sentinel the decode branch tests
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L114</c>]; after a seven-field decode, payload field 4
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L121</c>].
    /// </summary>
    /// <remarks>
    /// The legacy overwrites the very field it discriminated on, so the sentinel is gone from the
    /// record once a seven-field payload has been decoded. Any caller that needs to know whether
    /// the decode engaged must capture the pre-decode value first, which is what <see
    /// cref="SystemErrorHandler"/> does.
    /// </remarks>
    public string Object { get; init; } = string.Empty;

    /// <summary>
    /// The function or event name. Decoded from payload field 5
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L122</c>]. Rendered on the operator channel only, never
    /// in a response body (C-G).
    /// </summary>
    public string ObjectEvent { get; init; } = string.Empty;

    /// <summary>
    /// The source line number. Decoded from payload field 6 through the legacy long conversion
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L123</c>], so a value that does not parse yields zero
    /// rather than an error.
    /// </summary>
    public long Line { get; init; }

    /// <summary>
    /// The call-stack text. Decoded from payload field 7
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L124</c>], whose frames are joined with bare line feeds
    /// [<c>stacktraceinfo.srf:L33</c>].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty is the normal state outside a seven-field decode, and that is load-bearing rather than
    /// incidental: the legacy holds this value in a LOCAL variable that is assigned at one single
    /// place [<c>ws_objects/pfw.pbl.src/pfw.sra:L124</c>], so on every path that does not decode a
    /// seven-field payload it stays empty and the call-stack section of the report is omitted
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L137</c>].
    /// </para>
    /// <para>
    /// Rendered on the operator channel only. A stack trace never reaches a response body (C-G).
    /// </para>
    /// </remarks>
    public string StackTrace { get; init; } = string.Empty;
}

/// <summary>
/// The rendered system-error report: everything the legacy dialog displayed, as data.
/// </summary>
/// <remarks>
/// The legacy passes three things to its dialog - a title, the composed body and an icon
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>] - and all three are carried here rather than two of
/// them being flattened into a log message. Only the delivery channel changes (DECISION 1).
/// </remarks>
public sealed record SystemErrorReport
{
    /// <summary>
    /// The report title, reproducing the legacy dialog title verbatim
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>]. It is literal Chinese and is not localized,
    /// translated or resource-ified (QUIRK 4).
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// The report severity, reproducing the legacy dialog icon
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>].
    /// </summary>
    public SystemErrorSeverity Severity { get; init; }

    /// <summary>
    /// The composed report body, joined with bare line feeds exactly as the legacy composes it
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L129-L139</c>].
    /// </summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Reproduces the legacy framework's system-error protocol
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>] at an HTTP boundary, and doubles as the host's
/// unhandled-exception path.
/// </summary>
/// <remarks>
/// <para>
/// The type is arranged in the order the protocol runs, and the pure half comes first on purpose.
/// <see cref="FromException(Exception?)"/>, <see cref="Decode(SystemErrorInfo?)"/>, <see
/// cref="Format(SystemErrorInfo?)"/> and <see cref="ResolveRetCode(SystemErrorInfo?, bool)"/> are
/// static, host-free and side-effect-free: they need no host, no dependency-injection container, no
/// HTTP context and no input or output of any kind, so every branch in the protocol - including
/// each field-count branch and both sides of the window-line guard - is reachable from a plain unit
/// test (C-H). None of that logic is buried inside the exception-handler entry point.
/// </para>
/// <para>
/// The instance half is the delivery channel and nothing else: it logs the report, writes the
/// redacted body and, on a structural fault, requests termination (DECISION 5).
/// </para>
/// <para>
/// Registration is the idiomatic one - add this type as the exception handler and enable the
/// exception-handler middleware. The composition root owns that call; this file deliberately has no
/// dependency in that direction.
/// </para>
/// </remarks>
public sealed class SystemErrorHandler : IExceptionHandler
{
    /// <summary>
    /// The sentinel the legacy decode branch tests for, spelled exactly as the legacy spells it
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L114</c>]: lower case, compared for exact equality.
    /// </summary>
    /// <remarks>
    /// It is the name of the PowerBuilder function object that throws [<c>assert.srf:L3</c>], which
    /// is what the legacy runtime places on the error object's own object member before the
    /// system-error event fires. The comparison is ordinal and case-sensitive: the legacy performs
    /// a plain string equality test, so a differently cased spelling does NOT engage the decode,
    /// and neither does a value that merely starts with or contains it.
    /// </remarks>
    public const string AssertErrorObject = "assert";

    /// <summary>
    /// The payload field delimiter: CRLF, the two characters carriage return and line feed, and
    /// nothing else [<c>ws_objects/pfw.pbl.src/pfw.sra:L115</c>, <c>assert.srf:L19</c>].
    /// </summary>
    /// <remarks>
    /// QUIRK 3, the input half. This is a compile-time constant precisely so that the
    /// platform-dependent newline constant can never creep in: on one operating system that
    /// constant is a bare line feed, which would shred field 2 and field 7 into fragments because
    /// both legitimately contain bare line feeds [<c>assert.srf:L26,L63</c>].
    /// </remarks>
    private const string PayloadFieldDelimiter = "\r\n";

    /// <summary>
    /// The report line separator: a BARE line feed
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L129-L138</c>].
    /// </summary>
    /// <remarks>
    /// QUIRK 3, the output half. The report is joined with this and never with the delimiter it was
    /// split on, and never with the platform-dependent newline constant.
    /// </remarks>
    private const char ReportLineSeparator = '\n';

    /// <summary>
    /// The first line of the report, verbatim [<c>ws_objects/pfw.pbl.src/pfw.sra:L129</c>].
    /// </summary>
    /// <remarks>
    /// QUIRK 1. The type is the hardcoded literal <c>SYSTEM</c> on EVERY path, including a fully
    /// decoded assertion. It is one constant rather than a label plus a computed value precisely so
    /// that nothing can ever derive it from the payload.
    /// </remarks>
    private const string TypeLine = "类型: SYSTEM";

    /// <summary>The text label, colon plus one ASCII space
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L130</c>].</summary>
    private const string TextLabel = "信息: ";

    /// <summary>The window-or-menu label, colon plus one ASCII space
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L132</c>].</summary>
    private const string WindowMenuLabel = "窗口: ";

    /// <summary>The object label, colon plus one ASCII space
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L134</c>].</summary>
    private const string ObjectLabel = "对象: ";

    /// <summary>The function label, colon plus one ASCII space
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L135</c>].</summary>
    private const string ObjectEventLabel = "函数: ";

    /// <summary>The line-number label, colon plus one ASCII space
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L136</c>].</summary>
    private const string LineLabel = "行号: ";

    /// <summary>
    /// The call-stack label: colon and NO trailing space, because the legacy puts a bare line feed
    /// straight after the colon [<c>ws_objects/pfw.pbl.src/pfw.sra:L138</c>]. This asymmetry with
    /// the six labels above is deliberate and is not harmonized (C-B).
    /// </summary>
    private const string StackTraceLabel = "调用栈:";

    /// <summary>
    /// The report title, verbatim [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>].
    /// </summary>
    private const string ReportTitle = "系统错误";

    /// <summary>
    /// The lowest legacy field index, so the one-based-to-zero-based translation reads as
    /// arithmetic on a named quantity rather than as a bare literal.
    /// </summary>
    /// <remarks>
    /// The legacy split helper is one-based and its return value is simultaneously the field COUNT
    /// and the LAST VALID INDEX [<c>ws_objects/pfw.pbl.src/pfw.sra:L68</c>]. Those two numbers
    /// coincide in a one-based array and diverge here, which is the whole trap.
    /// </remarks>
    private const int FirstLegacyFieldIndex = 1;

    /// <summary>
    /// The minimum field count that sets the number and the text
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L116</c>]. The test is at-least, not exactly.
    /// </summary>
    private const int MinimumDecodableFieldCount = 2;

    /// <summary>
    /// The field count that sets the remaining five fields
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L119</c>].
    /// </summary>
    /// <remarks>
    /// The legacy test is EXACT equality, not at-least. An eight-field payload therefore sets the
    /// number and the text only and discards fields three through eight, and a six-field payload
    /// does the same. Preserved verbatim (C-B); it is not "improved" into an at-least test.
    /// </remarks>
    private const int CompleteFieldCount = 7;

    /// <summary>
    /// The extension member that carries the legacy return code on the wire, named exactly as
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> publishes it.
    /// </summary>
    private const string RetCodeExtensionMember = "retCode";

    /// <summary>
    /// The extension member carrying the correlation identifier that lets an operator find the full
    /// diagnostic record without any of it being disclosed in the response (DECISION 2).
    /// </summary>
    private const string TraceIdExtensionMember = "traceId";

    /// <summary>
    /// The problem type. The published contract uses the specification's own default whenever no
    /// more specific type applies, and none applies here.
    /// </summary>
    private const string ProblemType = "about:blank";

    /// <summary>
    /// The problem title: short, stable across occurrences, and revealing nothing (C-G).
    /// </summary>
    private const string ProblemTitle = "Internal Server Error";

    /// <summary>
    /// The problem detail. Fixed prose, identical for every occurrence, carrying no field value
    /// from the decoded record and no text from the underlying exception (C-F, C-G).
    /// </summary>
    private const string ProblemDetail =
        "An internal error occurred while handling the request. The diagnostic detail is recorded on "
        + "the server's operator channel and is deliberately not disclosed in this response.";

    /// <summary>The media type the published contract uses for every error body.</summary>
    private const string ProblemJsonContentType = "application/problem+json";

    /// <summary>
    /// The process exit code set when a structural fault terminates the service (DECISION 4).
    /// </summary>
    /// <remarks>
    /// Any non-zero value satisfies the requirement that an orchestrator observe a failure rather
    /// than a clean stop. This particular value is the conventional "internal software error" code
    /// from the historical exit-code convention, chosen so a structural fault is distinguishable
    /// from a generic non-zero exit rather than for any other reason.
    /// </remarks>
    private const int StructuralFaultExitCode = 70;

    /// <summary>The operator-channel record for the terminal, assert-decoded path.</summary>
    private const string AssertionFailureLogMessage =
        "{ReportTitle} ({Severity}): a decoded assertion failure reached the unhandled path, so the "
        + "framework halt path runs and the host is asked to shut down. Number={ErrorNumber} "
        + "WireRetCode={WireRetCode} Text={ErrorText} WindowMenu={WindowMenu} ErrorObject={ErrorObject} "
        + "ObjectEvent={ObjectEvent} Line={ErrorLine} LegacyStackTrace={LegacyStackTrace} "
        + "Report={Report}";

    /// <summary>The operator-channel record for an ordinary, non-terminal request fault.</summary>
    private const string RequestFaultLogMessage =
        "{ReportTitle} ({Severity}): an unhandled request fault was formatted through the legacy "
        + "system-error block; the host continues. Number={ErrorNumber} WireRetCode={WireRetCode} "
        + "Text={ErrorText} WindowMenu={WindowMenu} ErrorObject={ErrorObject} "
        + "ObjectEvent={ObjectEvent} Line={ErrorLine} LegacyStackTrace={LegacyStackTrace} "
        + "Report={Report}";

    /// <summary>
    /// The operator-channel record for the one case in which no body can be written.
    /// </summary>
    private const string ResponseAlreadyStartedLogMessage =
        "The response had already started when the system-error handler ran, so no problem-details "
        + "body could be written for this request.";

    // ------------------------------------------------------------------------------------------
    //  6.2  THE PURE DECODER. No host, no container, no input or output, and it never throws.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the pre-decode record - the .NET analogue of the state the legacy runtime places on
    /// its global error object before the system-error event fires.
    /// </summary>
    /// <param name="exception">
    /// The unhandled exception, or <see langword="null"/>, which yields an empty record rather than
    /// an error.
    /// </param>
    /// <returns>The record to hand to <see cref="Decode(SystemErrorInfo?)"/>.</returns>
    /// <remarks>
    /// <para>
    /// Two shapes, matching the legacy's own two shapes.
    /// </para>
    /// <para>
    /// For a managed assertion payload the sentinel is set to <see cref="AssertErrorObject"/> and
    /// the text is set to the exception's message, which IS the CRLF-joined seven-field payload
    /// [<c>assert.srf:L74-L80</c>]. The payload's own strongly typed members are deliberately NOT
    /// read across: the legacy decodes the fields out of the message
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L115-L124</c>], so decoding is the faithful behaviour and
    /// short-circuiting it would quietly skip the very protocol this file exists to reproduce. No
    /// message prefix is stripped, because the payload type carries none.
    /// </para>
    /// <para>
    /// For any other exception the fields are best-effort .NET analogues of what the legacy runtime
    /// would have filled in, and each is chosen for a stated reason: the number is the exception's
    /// own numeric result code, which is the runtime's equivalent of the legacy error number and
    /// which - QUIRK 2 - is never rendered anyway; the text is the message; the object is the type
    /// that declares the throwing member, falling back to the exception's own type when no throwing
    /// member is available; the object-event is the throwing member's name; the window-or-menu is
    /// the exception's source, there being no window and no menu in a headless service; and the
    /// line is the first frame's source line, which is zero when no symbols are present - the same
    /// zero the legacy long conversion produces from an unparseable field.
    /// </para>
    /// <para>
    /// The stack-trace field is left EMPTY on the non-assert path, and that is faithful rather than
    /// lazy: the legacy holds it in a local variable assigned at exactly one place
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L124</c>], so a non-assert system error never has one and
    /// its report never carries a call-stack section. The managed stack trace still reaches the
    /// operator, through the logging abstraction's own exception argument.
    /// </para>
    /// </remarks>
    public static SystemErrorInfo FromException(Exception? exception)
    {
        if (exception is null)
        {
            return new SystemErrorInfo();
        }

        if (exception is AssertionFailure assertion)
        {
            return new SystemErrorInfo
            {
                Number = assertion.HResult,
                Text = assertion.Message,
                Object = AssertErrorObject,
            };
        }

        return new SystemErrorInfo
        {
            Number = exception.HResult,
            Text = exception.Message,
            WindowMenu = exception.Source ?? string.Empty,
            Object = exception.TargetSite?.DeclaringType?.Name ?? exception.GetType().Name,
            ObjectEvent = exception.TargetSite?.Name ?? string.Empty,
            Line = FirstFrameSourceLine(exception),
        };
    }

    /// <summary>
    /// Reproduces the legacy decode branch [<c>ws_objects/pfw.pbl.src/pfw.sra:L114-L127</c>].
    /// </summary>
    /// <param name="error">
    /// The pre-decode record, whose object member carries the sentinel and whose text member
    /// carries the payload. <see langword="null"/> is treated as an empty record.
    /// </param>
    /// <returns>
    /// The record after decoding, or the input unchanged when the sentinel does not match or the
    /// payload does not carry enough fields.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method NEVER throws, for any input: a null record, a null, empty or whitespace payload,
    /// a single field, a trailing delimiter, a non-numeric field 1 or field 6, and more than seven
    /// fields are all handled by returning a record rather than by signalling. That is required
    /// behaviour, not defensiveness - the legacy is a PowerScript event with no exception surface
    /// at all, and its long conversion yields zero from unparseable text rather than failing.
    /// </para>
    /// <para>
    /// When the sentinel does not match, the input is returned untouched rather than blanked,
    /// because the legacy formats BOTH paths: its formatting statements sit outside the decode
    /// branch [<c>ws_objects/pfw.pbl.src/pfw.sra:L129</c> versus
    /// <c>ws_objects/pfw.pbl.src/pfw.sra:L114-L127</c>], so a non-assert system error is reported
    /// from whatever the runtime had already put on the error object.
    /// </para>
    /// </remarks>
    public static SystemErrorInfo Decode(SystemErrorInfo? error)
    {
        SystemErrorInfo decoded = error ?? new SystemErrorInfo();

        // [ws_objects/pfw.pbl.src/pfw.sra:L114] Ordinal, case-sensitive, exact equality against the
        // lower-case sentinel. Not a prefix test, not a containment test, and not case-insensitive.
        if (!string.Equals(decoded.Object, AssertErrorObject, StringComparison.Ordinal))
        {
            return decoded;
        }

        // [ws_objects/pfw.pbl.src/pfw.sra:L115] The payload splits on CRLF - QUIRK 3, the input
        // half.
        IReadOnlyList<string> fields = SplitPayload(decoded.Text);

        // [ws_objects/pfw.pbl.src/pfw.sra:L68] The legacy helper returns UpperBound, which in its
        // one-based array is simultaneously the field count and the last valid index. Here it is
        // the count only, and every field read below goes through LegacyField to keep the
        // translation in one place.
        int fieldCount = fields.Count;

        // [ws_objects/pfw.pbl.src/pfw.sra:L116] At LEAST two fields, not exactly two.
        if (fieldCount >= MinimumDecodableFieldCount)
        {
            decoded = decoded with
            {
                // [ws_objects/pfw.pbl.src/pfw.sra:L117] Long(sMessages[1]) - QUIRK 2: decoded here,
                // never rendered.
                Number = ParseLegacyLong(LegacyField(fields, 1)),

                // [ws_objects/pfw.pbl.src/pfw.sra:L118] sMessages[2].
                Text = LegacyField(fields, 2),
            };

            // [ws_objects/pfw.pbl.src/pfw.sra:L119] EXACTLY seven, never at-least-seven. An
            // eight-field payload keeps only the number and the text, and discards its fields three
            // through eight.
            if (fieldCount == CompleteFieldCount)
            {
                decoded = decoded with
                {
                    // [ws_objects/pfw.pbl.src/pfw.sra:L120] sMessages[3].
                    WindowMenu = LegacyField(fields, 3),

                    // [ws_objects/pfw.pbl.src/pfw.sra:L121] sMessages[4] - this OVERWRITES the
                    // sentinel it discriminated on.
                    Object = LegacyField(fields, 4),

                    // [ws_objects/pfw.pbl.src/pfw.sra:L122] sMessages[5].
                    ObjectEvent = LegacyField(fields, 5),

                    // [ws_objects/pfw.pbl.src/pfw.sra:L123] Long(sMessages[6]) - unparseable text
                    // yields zero.
                    Line = ParseLegacyLong(LegacyField(fields, 6)),

                    // [ws_objects/pfw.pbl.src/pfw.sra:L124] sMessages[7] - the only place the
                    // stack-trace value is set.
                    StackTrace = LegacyField(fields, 7),
                };
            }
        }

        return decoded;
    }

    // ------------------------------------------------------------------------------------------
    //  6.3  THE PURE FORMATTER. No host, deterministic, and byte-exact against the legacy.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Reproduces the legacy report composition and its dialog arguments
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L129-L141</c>].
    /// </summary>
    /// <param name="error">
    /// The decoded record. <see langword="null"/> is treated as an empty record, which yields the
    /// same report the legacy would compose from an untouched error object.
    /// </param>
    /// <returns>The title, the severity and the composed body.</returns>
    /// <remarks>
    /// <para>
    /// The body is joined with bare line feeds and with nothing else (QUIRK 3, the output half).
    /// The six value labels each carry a colon and exactly one ASCII space; the call-stack label
    /// carries a colon and then a line feed with NO space, and that inconsistency is preserved.
    /// </para>
    /// <para>
    /// Two sections are conditional and five lines are not. The window-or-menu line appears only
    /// when the window-or-menu value differs from the object value, and INEQUALITY IS THE ONLY
    /// GUARD [<c>ws_objects/pfw.pbl.src/pfw.sra:L131</c>] - there is deliberately no non-empty
    /// test, so an empty window-or-menu against a non-empty object still emits the label with an
    /// empty value. The call-stack section appears only when the stack value is non-empty
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L137</c>]. The type, text, object, function and
    /// line-number lines are unconditional.
    /// </para>
    /// <para>
    /// The failure number is absent from the body by design (QUIRK 2), and the type is the literal
    /// <c>SYSTEM</c> even for a fully decoded assertion (QUIRK 1).
    /// </para>
    /// </remarks>
    public static SystemErrorReport Format(SystemErrorInfo? error)
    {
        SystemErrorInfo source = error ?? new SystemErrorInfo();

        StringBuilder body = new();

        // [ws_objects/pfw.pbl.src/pfw.sra:L129-L130] QUIRK 1: the type label is the hardcoded
        // literal SYSTEM on every path, including a fully decoded assertion. QUIRK 2: the number is
        // not rendered.
        body.Append(TypeLine)
            .Append(ReportLineSeparator)
            .Append(TextLabel)
            .Append(source.Text);

        // [ws_objects/pfw.pbl.src/pfw.sra:L131-L133] The ONLY guard is inequality. No non-empty
        // test is added.
        if (!string.Equals(source.WindowMenu, source.Object, StringComparison.Ordinal))
        {
            body.Append(ReportLineSeparator)
                .Append(WindowMenuLabel)
                .Append(source.WindowMenu);
        }

        // [ws_objects/pfw.pbl.src/pfw.sra:L134] Unconditional.
        body.Append(ReportLineSeparator)
            .Append(ObjectLabel)
            .Append(source.Object);

        // [ws_objects/pfw.pbl.src/pfw.sra:L135-L136] Both unconditional. The line number renders
        // through the equivalent of the legacy String() conversion: plain invariant digits, no
        // group separator, no padding.
        body.Append(ReportLineSeparator)
            .Append(ObjectEventLabel)
            .Append(source.ObjectEvent)
            .Append(ReportLineSeparator)
            .Append(LineLabel)
            .Append(source.Line.ToString(CultureInfo.InvariantCulture));

        // [ws_objects/pfw.pbl.src/pfw.sra:L137-L139] Non-empty only, and the shape is separator,
        // label, separator, value - the label itself carrying no trailing space.
        if (source.StackTrace.Length != 0)
        {
            body.Append(ReportLineSeparator)
                .Append(StackTraceLabel)
                .Append(ReportLineSeparator)
                .Append(source.StackTrace);
        }

        // [ws_objects/pfw.pbl.src/pfw.sra:L141] The three dialog arguments, carried as data instead
        // of displayed.
        return new SystemErrorReport
        {
            Title = ReportTitle,
            Severity = SystemErrorSeverity.Stop,
            Body = body.ToString(),
        };
    }

    /// <summary>
    /// Chooses the return code the caller channel carries (DECISION 3).
    /// </summary>
    /// <param name="error">The decoded record, or <see langword="null"/>.</param>
    /// <param name="decodedFromAssertPayload">
    /// Whether the sentinel matched before decoding, captured by the caller because a seven-field
    /// decode OVERWRITES the sentinel [<c>ws_objects/pfw.pbl.src/pfw.sra:L121</c>].
    /// </param>
    /// <returns>
    /// The decoded number when it is a member of the published closed value set, and the
    /// catalogue's unknown value in every other case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The published set is closed, so the assert payload's fixed sentinel field 1 cannot be
    /// emitted and is narrowed to the unknown value rather than widening the contract. The raw
    /// number is never lost: it always reaches the operator channel.
    /// </para>
    /// <para>
    /// The non-assert path is always the unknown value, deliberately. An arbitrary exception's
    /// numeric result code is not a legacy return code, and a code that happened to be zero would
    /// otherwise report SUCCESS on a failed request.
    /// </para>
    /// </remarks>
    public static long ResolveRetCode(SystemErrorInfo? error, bool decodedFromAssertPayload)
    {
        if (error is null || !decodedFromAssertPayload)
        {
            return RetCode.UNKNOWN;
        }

        return IsPublishedRetCode(error.Number) ? error.Number : RetCode.UNKNOWN;
    }

    /// <summary>
    /// Tests membership of the closed return-code value set that
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> publishes for its error body.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value is published.</returns>
    /// <remarks>
    /// The membership test is written against the catalogue's own named constants rather than
    /// against literals, so it cannot drift from them. The failure band is contiguous and gap-free
    /// from the first failure code down to the retry code, which is why one range test covers it,
    /// and the cancelled value sits inside that band. Nothing here declares a constant; it only
    /// consumes them.
    /// </remarks>
    private static bool IsPublishedRetCode(long value)
    {
        return value == RetCode.OK
            || value == RetCode.PREVENT
            || (value <= RetCode.FAILED && value >= RetCode.E_RETRY)
            || value == RetCode.E_NO_SUPPORT
            || value == RetCode.E_NO_IMPLEMENTATION
            || value == RetCode.UNKNOWN;
    }

    /// <summary>
    /// Reproduces the legacy split helper [<c>ws_objects/pfw.pbl.src/pfw.sra:L48-L69</c>] for the
    /// one delimiter this protocol uses.
    /// </summary>
    /// <param name="source">The payload, which may be <see langword="null"/> or empty.</param>
    /// <returns>The fields, in payload order, as a zero-based list.</returns>
    /// <remarks>
    /// <para>
    /// Three observable behaviours of the helper are reproduced, and one is unreachable here:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///     <b>The target is reset first</b> [<c>ws_objects/pfw.pbl.src/pfw.sra:L55</c>], so no
    ///     residue from an earlier call can survive. Reproduced by building a fresh list per call,
    ///     which also makes the method re-entrant and safe to call concurrently.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>A trailing delimiter yields NO empty final field</b>
    ///     [<c>ws_objects/pfw.pbl.src/pfw.sra:L64-L66</c>]: the tail is appended only when it is
    ///     non-empty. So a two-field payload that ends with a delimiter splits into two fields and
    ///     not three, which changes which arity branch fires. An empty or null payload yields zero
    ///     fields by the same rule.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>The count is the return value</b> [<c>ws_objects/pfw.pbl.src/pfw.sra:L68</c>]. Here
    ///     the list's own count is that number; it is NOT also the last valid index, which is
    ///     exactly the one-based hazard the single translating accessor exists to contain.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///     <b>The empty-delimiter guard</b> [<c>ws_objects/pfw.pbl.src/pfw.sra:L52-L53</c>], which
    ///     returns zero fields without attempting a split, is UNREACHABLE here: the delimiter is a
    ///     compile-time constant two characters long. It is recorded rather than written, because a
    ///     branch no input can reach could not be covered by a test and writing one would
    ///     misrepresent the protocol.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    private static IReadOnlyList<string> SplitPayload(string? source)
    {
        // [ws_objects/pfw.pbl.src/pfw.sra:L55] A fresh target every call - the reset, expressed
        // immutably.
        List<string> fields = [];

        string remaining = source ?? string.Empty;

        // [ws_objects/pfw.pbl.src/pfw.sra:L57] Pos() returns a one-based position and zero for "not
        // found"; IndexOf returns a zero-based position and negative one for "not found". The loop
        // condition is translated accordingly and this is one of the seven places the one-based
        // hazard bites.
        int position = remaining.IndexOf(PayloadFieldDelimiter, StringComparison.Ordinal);

        // [ws_objects/pfw.pbl.src/pfw.sra:L58-L63]
        while (position >= 0)
        {
            // [ws_objects/pfw.pbl.src/pfw.sra:L59-L60] Left(src, nPos - 1): everything before the
            // delimiter, which is empty when the payload starts with one - and an empty leading
            // field IS appended, unlike an empty trailing one.
            fields.Add(remaining[..position]);

            // [ws_objects/pfw.pbl.src/pfw.sra:L61] Mid(src, nPos + nLenDelimiter): everything after
            // the delimiter.
            remaining = remaining[(position + PayloadFieldDelimiter.Length)..];

            // [ws_objects/pfw.pbl.src/pfw.sra:L62]
            position = remaining.IndexOf(PayloadFieldDelimiter, StringComparison.Ordinal);
        }

        // [ws_objects/pfw.pbl.src/pfw.sra:L64-L66] The tail, and ONLY when non-empty.
        if (remaining.Length != 0)
        {
            fields.Add(remaining);
        }

        // [ws_objects/pfw.pbl.src/pfw.sra:L68] The count.
        return fields;
    }

    /// <summary>
    /// The single one-based field accessor: translates a legacy field index into a zero-based list
    /// index in exactly one place.
    /// </summary>
    /// <param name="fields">The split fields.</param>
    /// <param name="legacyIndex">
    /// The legacy field index, 1 through 7, written at each call site as the literal the legacy
    /// itself uses so the two can be diffed line by line.
    /// </param>
    /// <returns>The field.</returns>
    /// <remarks>
    /// PowerBuilder arrays are one-based; this list is zero-based with a count one past the end, so
    /// the legacy's fields 1 through 7 are this list's elements 0 through 6. Routing all seven
    /// reads through here is what makes a silent off-by-one impossible to introduce in six places
    /// while fixing it in the seventh. No bound check is performed: every call site is already
    /// guarded by the arity test that authorises it
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L116,L119</c>], and adding a check would create a branch
    /// no input can reach.
    /// </remarks>
    private static string LegacyField(IReadOnlyList<string> fields, int legacyIndex)
    {
        return fields[legacyIndex - FirstLegacyFieldIndex];
    }

    /// <summary>
    /// Reproduces the legacy long conversion applied to payload fields 1 and 6
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L117,L123</c>].
    /// </summary>
    /// <param name="value">The field text.</param>
    /// <returns>The parsed value, or zero when the text does not parse.</returns>
    /// <remarks>
    /// The legacy conversion yields zero from unparseable text rather than raising anything, so a
    /// failed parse yields zero here too - never an exception, and never a null propagated out of
    /// the decoder. The invariant culture is used explicitly so the result cannot vary with the
    /// host's locale, which the legacy's own conversion does not do either.
    /// </remarks>
    private static long ParseLegacyLong(string? value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : 0L;
    }

    /// <summary>
    /// The source line of the exception's first stack frame, or zero when none is available.
    /// </summary>
    /// <param name="exception">The exception, never <see langword="null"/> at any call
    /// site.</param>
    /// <returns>The line number, or zero.</returns>
    /// <remarks>
    /// Zero is the same value the legacy long conversion produces from an unparseable field, so an
    /// image built without symbols renders the line-number line exactly as a malformed payload
    /// would. This is only ever read on the operator channel; a line number never reaches a
    /// response body (C-G).
    /// </remarks>
    private static long FirstFrameSourceLine(Exception exception)
    {
        StackTrace trace = new(exception, fNeedFileInfo: true);
        StackFrame? frame = trace.FrameCount > 0 ? trace.GetFrame(0) : null;
        return frame?.GetFileLineNumber() ?? 0;
    }


    // ------------------------------------------------------------------------------------------
    //  6.4 and 6.5 THE DELIVERY CHANNEL AND THE TERMINATION PATH. Everything above this line is
    //  pure; everything below it is plumbing, and it holds no protocol logic of its own.
    // ------------------------------------------------------------------------------------------

    /// <summary>The operator channel (DECISION 2).</summary>
    private readonly ILogger<SystemErrorHandler> _logger;

    /// <summary>
    /// The host lifetime, through which the framework halt path is reproduced (DECISION 4).
    /// </summary>
    private readonly IHostApplicationLifetime _hostLifetime;

    /// <summary>
    /// The framework's problem-details writer when the composition root registered one, otherwise
    /// <see langword="null"/> and the direct write is used instead (DECISION 6).
    /// </summary>
    private readonly IProblemDetailsService? _problemDetailsService;

    /// <summary>The injectable termination effect (DECISION 6).</summary>
    private readonly Action<int> _requestProcessTermination;

    /// <summary>
    /// Creates the handler. This is the ONLY constructor, so the dependency-injection container has
    /// no overload to choose between (DECISION 6).
    /// </summary>
    /// <param name="logger">The operator channel. Required.</param>
    /// <param name="hostLifetime">
    /// The host lifetime used to reproduce the framework halt path. Required.
    /// </param>
    /// <param name="problemDetailsService">
    /// The framework's problem-details writer. Optional: when the composition root did not register
    /// one, the identical body is written directly instead, so a caller cannot tell which path ran.
    /// </param>
    /// <param name="requestProcessTermination">
    /// The termination effect, taking the process exit code. Optional, and defaulting to the
    /// host-backed behaviour of DECISION 4 - set the exit code, then request host shutdown so the
    /// registered shutdown path, which is where the composition root's framework finalize step
    /// lives, actually runs. A test supplies its own callback here to assert that termination was
    /// requested without stopping or failing the test host (C-H).
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logger"/> or <paramref name="hostLifetime"/> is <see langword="null"/>. Both
    /// are structural: a handler that cannot report and cannot halt would silently discharge
    /// neither half of the protocol, so this is one of the two places in this file where failing
    /// loudly is the correct behaviour.
    /// </exception>
    public SystemErrorHandler(
        ILogger<SystemErrorHandler> logger,
        IHostApplicationLifetime hostLifetime,
        IProblemDetailsService? problemDetailsService = null,
        Action<int>? requestProcessTermination = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(hostLifetime);

        _logger = logger;
        _hostLifetime = hostLifetime;
        _problemDetailsService = problemDetailsService;
        _requestProcessTermination = requestProcessTermination ?? RequestHostShutdown;
    }

    /// <summary>
    /// The host's unhandled-exception path: runs the legacy protocol, reports it, answers the
    /// caller and, for a structural fault, reproduces the framework halt path.
    /// </summary>
    /// <param name="httpContext">The request context. Required.</param>
    /// <param name="exception">The unhandled exception. Required.</param>
    /// <param name="cancellationToken">Cancels writing the response body.</param>
    /// <returns>
    /// <see langword="true"/> when the redacted body was written and the fault is fully handled;
    /// <see langword="false"/> when it could not be written because the response had already
    /// started, which leaves the middleware free to fall back to its own behaviour.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The step order is the legacy's own: decode
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L114-L127</c>], compose
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L129-L139</c>], report
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L141</c>], and only then halt
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L143</c>]. Reporting before halting is not incidental -
    /// the legacy shows its dialog and terminates afterwards, so a structural fault is always
    /// recorded before the host is asked to stop.
    /// </para>
    /// <para>
    /// This is the UNHANDLED path and only the unhandled path. The oracle proves the distinction
    /// matters: an assertion the application caught and handled itself never reaches the legacy
    /// system-error event [<c>w_test_assert.srw:L97-L102</c>], so nothing here may intercept a
    /// fault that application code already dealt with. Being registered as the exception handler,
    /// which runs only for exceptions that escaped the pipeline, is what enforces that.
    /// </para>
    /// <para>
    /// A fault surfacing from one of the four reserved not-implemented routes is an ordinary
    /// request fault and is handled as such, with no special case keyed to any deferred capability
    /// (C-D).
    /// </para>
    /// </remarks>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // The structural-versus-request line of DECISION 5, evaluated once. It is captured BEFORE
        // the decode because a seven-field decode overwrites the sentinel
        // [ws_objects/pfw.pbl.src/pfw.sra:L121].
        bool structuralFault = exception is AssertionFailure;

        SystemErrorInfo raised = FromException(exception);
        SystemErrorInfo decoded = Decode(raised);
        SystemErrorReport report = Format(decoded);
        long wireRetCode = ResolveRetCode(decoded, structuralFault);

        // ---- OPERATOR CHANNEL: the faithful successor to the dialog. Nothing is withheld here,
        // because this is the audience the dialog had (DECISION 2). All seven decoded fields plus
        // the exact composed block, with the title and the severity. The exception itself is passed
        // so the managed stack trace reaches the operator through the logging abstraction's own
        // argument rather than through the legacy call-stack field, which stays faithful to the
        // legacy and is empty on this path unless a seven-field payload supplied it.
        //
        // C-F: no field read here is a credential, a key or a connection string, and the one
        // upstream field known to carry interpolated literal statement text is redacted or
        // parameter-separated by the service that owns it - this file never reads, echoes or
        // reconstructs it.
        if (structuralFault)
        {
            _logger.LogCritical(
                exception,
                AssertionFailureLogMessage,
                report.Title,
                report.Severity,
                decoded.Number,
                wireRetCode,
                decoded.Text,
                decoded.WindowMenu,
                decoded.Object,
                decoded.ObjectEvent,
                decoded.Line,
                decoded.StackTrace,
                report.Body);
        }
        else
        {
            _logger.LogError(
                exception,
                RequestFaultLogMessage,
                report.Title,
                report.Severity,
                decoded.Number,
                wireRetCode,
                decoded.Text,
                decoded.WindowMenu,
                decoded.Object,
                decoded.ObjectEvent,
                decoded.Line,
                decoded.StackTrace,
                report.Body);
        }

        // ---- CALLER CHANNEL: the redacted body (DECISION 2).
        bool handled = await WriteRedactedProblemAsync(httpContext, wireRetCode, cancellationToken)
            .ConfigureAwait(false);

        // ---- THE HALT PATH [ws_objects/pfw.pbl.src/pfw.sra:L143]. Requested last, exactly as the
        // legacy halts after its dialog, and requested even when the body could not be written: a
        // structural fault is terminal regardless of what the caller did or did not receive
        // (DECISION 5).
        if (structuralFault)
        {
            _requestProcessTermination(StructuralFaultExitCode);
        }

        return handled;
    }

    /// <summary>
    /// Writes the redacted problem-details body that conforms to
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c>.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="wireRetCode">The return code chosen by <see cref="ResolveRetCode"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when a body was written.</returns>
    /// <remarks>
    /// <para>
    /// Every member is either fixed prose or a value the caller already possesses (C-G). Nothing
    /// derived from the decoded record reaches the body except the return code, which is a closed
    /// published value set and therefore discloses nothing beyond a failure class. In particular
    /// the body carries no stack trace, no object-event name, no window-or-menu name, no line
    /// number, no upstream address, no host name, no port, no file path, no connection string and
    /// no token material, and it never copies the underlying exception's own text.
    /// </para>
    /// <para>
    /// The problem-details context is handed over WITHOUT its exception member set, deliberately. A
    /// response customization registered elsewhere in the pipeline can read that member, so leaving
    /// it unset is what makes the redaction a property of this file rather than a convention other
    /// code is trusted to respect.
    /// </para>
    /// <para>
    /// The correlation identifier is the bridge between the two channels: it is what lets an
    /// operator find the full record that was deliberately not disclosed here. The published body
    /// permits additional members and instructs consumers to ignore any they do not recognise, so
    /// carrying it conforms rather than extends.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> WriteRedactedProblemAsync(
        HttpContext httpContext,
        long wireRetCode,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            // Nothing can be written once the response is on the wire. This is reported rather than
            // swallowed, and it is the one path on which this method returns false.
            _logger.LogWarning(ResponseAlreadyStartedLogMessage);
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        ProblemDetails problem = new()
        {
            Type = ProblemType,
            Title = ProblemTitle,
            Status = StatusCodes.Status500InternalServerError,
            Detail = ProblemDetail,

            // The caller's own request path. It discloses nothing the caller did not send.
            Instance = httpContext.Request.Path.HasValue ? httpContext.Request.Path.Value : null,
        };

        problem.Extensions[RetCodeExtensionMember] = wireRetCode;

        string correlationId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        if (!string.IsNullOrEmpty(correlationId))
        {
            problem.Extensions[TraceIdExtensionMember] = correlationId;
        }

        if (_problemDetailsService is not null)
        {
            ProblemDetailsContext context = new()
            {
                HttpContext = httpContext,
                ProblemDetails = problem,
            };

            if (await _problemDetailsService.TryWriteAsync(context).ConfigureAwait(false))
            {
                return true;
            }
        }

        // Either no writer was registered, or the registered writer declined. The body is identical
        // on this path (DECISION 6).
        await httpContext.Response
            .WriteAsJsonAsync(problem, options: null, contentType: ProblemJsonContentType, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// The default termination effect: the faithful reproduction of the legacy framework halt
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L143</c>] (DECISION 4).
    /// </summary>
    /// <param name="exitCode">The process exit code to report.</param>
    /// <remarks>
    /// <para>
    /// The exit code is set FIRST so that it is already in place while the shutdown path runs, and
    /// shutdown is then REQUESTED rather than the process being aborted. That ordering is what
    /// reproduces the legacy semantics: the legacy halt runs the application close event - one
    /// statement, the framework finalize call [<c>ws_objects/pfw.pbl.src/pfw.sra:L108</c>] - and
    /// terminates only afterwards, and <c>docs/README.md</c> section 初始化 warns that the finalize
    /// call must be paired with the initialize call and belongs at the end of that event.
    /// </para>
    /// <para>
    /// A fail-fast abort is therefore NOT used, here or anywhere in this file: it would bypass the
    /// registered shutdown path and break that documented pairing. The dependency is on the host
    /// lifetime abstraction rather than on whichever component owns the finalize step, because the
    /// abstraction is the seam the shutdown path is registered against.
    /// </para>
    /// </remarks>
    private void RequestHostShutdown(int exitCode)
    {
        Environment.ExitCode = exitCode;
        _hostLifetime.StopApplication();
    }
}
