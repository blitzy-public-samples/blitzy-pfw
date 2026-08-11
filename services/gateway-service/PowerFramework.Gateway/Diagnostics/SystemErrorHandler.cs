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
//           DECISION 1 through DECISION 7 below.
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
//      OPERATOR CHANNEL (the faithful successor to the dialog) - a structured log record. It has
//      TWO forms, and which form is written is decided by the same structural-versus-request line
//      DECISION 5 draws, never by anything else:
//
//        * STRUCTURAL (a decoded assertion). All seven decoded fields AND the exact formatted
//          Chinese block, with the title and the severity, plus the managed exception itself so its
//          stack trace reaches the operator. Nothing is withheld, because this record IS the dialog
//          and the operator is who the dialog was for. This form is written at most once per
//          process, since the path it belongs to terminates the host (DECISION 4).
//
//        * ORDINARY REQUEST FAULT. An ALLOWLISTED record: the title and the severity, the
//          correlation identifier, the request method, the matched ROUTE PATTERN, the response
//          status, the raw decoded number, the wire return code, the exception TYPE CHAIN, and the
//          fault site as a declaring-type name, a member name and a source line. Every one of those
//          is either a compile-time constant, an integer, a name drawn from this system's own code,
//          or a value the caller itself sent. Deliberately ABSENT, and this is the whole point of
//          the form: the exception object, the exception message, any inner exception's message,
//          `Exception.Source`, and the formatted block - because the block embeds the message.
//
//      WHY THE TWO FORMS MAY DIFFER WITHOUT BREACHING C-B. There is no legacy behaviour on the
//      ordinary path to preserve. DECISION 5 records the reason in full: the legacy has no request
//      boundary at all, so every legacy system error was terminal and the ordinary form is a record
//      the legacy never produced. Choosing its content is therefore not a relaxation of anything
//      the oracle does - the terminal form, which IS the oracle's behaviour, keeps every field. What
//      the ordinary form avoids is a leak the oracle could not have had either: the legacy rendered
//      its text to one screen in front of one person, whereas a log record is retained, shipped and
//      indexed, so arbitrary upstream exception text in it can carry a URL, request data, statement
//      text, personal data, a token embedded by a dependency, or an internal path past a boundary
//      the legacy had no way to cross (C-F).
//
//      Every field EXCLUDED from the ordinary form remains recoverable by an operator who needs it:
//      the correlation identifier ties this record to the host's own request log, and to a debugger
//      or an exception-tracking sink attached deliberately and configured to hold exception content
//      under its own retention rules. Redaction here is about what this file writes UNCONDITIONALLY
//      into general-purpose logging, not about denying an operator access to a fault.
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
//      still FORMATTED through the SAME block - the legacy formatted every system error, not only
//      asserts, because its formatting statements sit OUTSIDE the decode branch
//      [ws_objects/pfw.pbl.src/pfw.sra:L129 versus :L114-L127], so Format() runs unconditionally
//      here too and its behaviour is identical on both paths - and the redacted body is returned,
//      and the host lives on to serve the next request. What differs is only which operator record
//      is written: the allowlisted form of DECISION 2, because the formatted block embeds the
//      arbitrary exception message and a retained log record is not the one-operator-one-screen
//      channel the legacy dialog was.
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
//  DECISION 7 - THE CORRELATION IDENTIFIER IS MINTED ONCE, BEFORE ANY WRITE, AND IS THE SAME VALUE
//  ON BOTH CHANNELS.
//
//  DECISION 2 gives the caller a correlation identifier and nothing else, and states that an
//  operator can find the full record with it. That promise is only true if the identifier the caller
//  is handed is ALSO in the record - otherwise the response advertises a bridge that leads nowhere,
//  and the redaction becomes a refusal rather than a redirection.
//
//  So the identifier is resolved ONCE, in TryHandleAsync, BEFORE the operator record is written, and
//  the single resolved value is then used in three places: as the {TraceId} field of whichever
//  operator record is written, as the {TraceId} field of the response-already-started record, and as
//  the problem-details correlation extension member. It is passed down as a parameter rather than
//  re-resolved at each site, because re-resolving is exactly how the two channels drift apart: the
//  ambient activity identifier is not guaranteed to be stable for the lifetime of a request, so two
//  reads can legitimately return two values and a caller would then quote an identifier that
//  appears in no record at all.
//
//  The value itself is the ambient distributed-trace identifier when one exists, because that is
//  what correlates this record with every other service's record for the same operation, and the
//  host's own per-request identifier otherwise. Neither is caller-supplied content: the trace
//  identifier is generated or parsed by the tracing infrastructure into a fixed hexadecimal shape,
//  and the host's identifier is host-generated. Nothing about the resolution discloses anything, and
//  nothing about it can fail - the fallback is always available.
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
//  .editorconfig scopes its naming-analyzer suppressions to the files on its BAND 3 roster - which that
//  file publishes once, and which this comment deliberately does not restate - and not one of them is
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
/// How many fields a system error's payload actually carried, and how many of the seven legacy error
/// fields the decode was consequently able to populate.
/// </summary>
/// <remarks>
/// <para>
/// <b>THIS TYPE EXISTS BECAUSE THE LEGACY DECODE IS LOSSY BY DESIGN AND THE LOSS WAS PREVIOUSLY
/// SILENT.</b> Two legacy behaviours combine to discard fields, and both are reproduced verbatim
/// rather than corrected (C-B):
/// </para>
/// <list type="number">
///   <item>
///     <description>
///     The legacy split helper appends its final field ONLY when that field is non-empty
///     [<c>ws_objects/pfw.pbl.src/pfw.sra:L64-L66</c>], so a payload whose last field is empty
///     splits into one field fewer than it was written with.
///     </description>
///   </item>
///   <item>
///     <description>
///     The arity test is EXACT equality against seven, never at-least-seven
///     [<c>ws_objects/pfw.pbl.src/pfw.sra:L119</c>], so any other count populates the number and the
///     text alone and leaves the window-or-menu, object, object-event, line-number and call-stack
///     fields at their pre-decode values.
///     </description>
///   </item>
/// </list>
/// <para>
/// Together those two mean a payload written with seven fields whose call-stack field happens to be
/// EMPTY splits into six, fails the arity test, and loses four diagnostic fields - and the legacy
/// loses them in exactly the same way, which is why the decode may not be changed. What the legacy has
/// no equivalent of is an operator channel: it renders one dialog and halts. So the loss is made
/// VISIBLE here instead of being repaired, on the channel that is net-new, and an operator reading a
/// terminal fault can tell a genuinely shallow payload from a seven-field one that lost its tail.
/// </para>
/// <para>
/// Every member is an integer or a boolean. Nothing derived from payload CONTENT appears on this type,
/// so it can be published on any channel, including the allowlisted one (C-F).
/// </para>
/// </remarks>
public readonly record struct SystemErrorPayloadShape
{
    /// <summary>
    /// Whether the pre-decode object member matched the legacy assert sentinel, which is the only
    /// condition under which any decoding is attempted at all
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L114</c>].
    /// </summary>
    public bool SentinelMatched { get; init; }

    /// <summary>
    /// The number of fields the payload split into, counted exactly as the legacy helper counts them
    /// [<c>ws_objects/pfw.pbl.src/pfw.sra:L68</c>] - so a trailing empty field is already absent from
    /// this number. Zero when the sentinel did not match, because nothing is split in that case.
    /// </summary>
    public int FieldCount { get; init; }

    /// <summary>
    /// How many of the seven legacy error fields the decode populated: seven for a complete payload,
    /// two for any other decodable count, and zero when the sentinel did not match.
    /// </summary>
    /// <remarks>
    /// A value of two against a <see cref="FieldCount"/> of six is the diagnosis this type exists to
    /// surface: four fields were discarded, and whether they were discarded because the payload was
    /// genuinely shallow or because its call-stack field was empty is a question only the producer can
    /// answer - but an operator can now see that it happened.
    /// </remarks>
    public int DecodedFieldCount { get; init; }
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

    /// <summary>
    /// The problem title for a fault the CALLER's request caused, carrying its own client status.
    /// </summary>
    /// <remarks>
    /// SEPARATE FROM <see cref="ProblemTitle"/> BECAUSE THAT ONE IS A STATEMENT ABOUT THIS SERVICE.
    /// "Internal Server Error" on a 400 tells a caller to look for an outage when the answer is in its own
    /// request, and a title that disagrees with the status is a defect a consumer cannot work around.
    /// </remarks>
    private const string ClientErrorProblemTitle = "Bad Request";

    /// <summary>
    /// The problem detail for a request this service could not accept. Fixed prose, naming nothing.
    /// </summary>
    /// <remarks>
    /// THE FRAMEWORK'S OWN MESSAGE IS DELIBERATELY NOT USED. It names the parameter it could not bind and
    /// its type, which is shaped by the caller's request, and this file's whole redaction commitment is
    /// that no caller-derived text reaches the body (C-F, C-G). A caller that omitted a required parameter
    /// already knows what it sent; what it needs from this response is the classification.
    /// </remarks>
    private const string ClientErrorProblemDetail =
        "The request could not be accepted as this operation declares it. A required parameter is absent, "
        + "or a value supplied cannot be bound to the shape the operation publishes. No part of the "
        + "request is echoed here; the published contract states what the operation accepts.";

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

    /// <summary>
    /// The operator-channel record for the terminal, assert-decoded path - the unredacted form of
    /// DECISION 2, carrying all seven decoded fields and the exact formatted block.
    /// </summary>
    /// <remarks>
    /// The correlation identifier leads this record deliberately: it is the value the caller was
    /// handed, so an operator searching for it must be able to match it without knowing which of the
    /// two record forms was written (DECISION 7).
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <c>PayloadFieldCount</c> and <c>PayloadDecodedFieldCount</c> are the F11 observation: the legacy
    /// decode discards fields silently in two documented ways, and this pair is what makes the
    /// discarding visible without altering it. A pair reading <c>6</c> and <c>2</c> says four fields
    /// were dropped; <c>7</c> and <c>7</c> says none were. Both are integers derived from the payload's
    /// SHAPE and never from its content, so neither can carry upstream text (C-F). See
    /// <see cref="SystemErrorPayloadShape"/> for why the decode itself is not changed.
    /// </para>
    /// </remarks>
    private const string AssertionFailureLogMessage =
        "{ReportTitle} ({Severity}): a decoded assertion failure reached the unhandled path, so the "
        + "framework halt path runs and the host is asked to shut down. TraceId={TraceId} "
        + "Number={ErrorNumber} WireRetCode={WireRetCode} PayloadFieldCount={PayloadFieldCount} "
        + "PayloadDecodedFieldCount={PayloadDecodedFieldCount} Text={ErrorText} "
        + "WindowMenu={WindowMenu} ErrorObject={ErrorObject} ObjectEvent={ObjectEvent} "
        + "Line={ErrorLine} LegacyStackTrace={LegacyStackTrace} Report={Report}";

    /// <summary>
    /// The operator-channel record for an ordinary, non-terminal request fault - the ALLOWLISTED
    /// form of DECISION 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every placeholder here is filled from a compile-time constant, an integer, a name drawn from
    /// this system's own code, or a value the caller itself sent. There is deliberately no
    /// placeholder for the exception message, for any inner exception's message, for
    /// <see cref="Exception.Source"/>, or for the formatted block - the block embeds the message, so
    /// including it would reintroduce exactly what the allowlist exists to keep out (C-F).
    /// </para>
    /// <para>
    /// Adding a placeholder to this template is therefore a security decision, not a formatting one.
    /// The rule for a future editor is short: a value may be added only if it is constant, numeric,
    /// an identifier from this codebase, or already known to the caller.
    /// </para>
    /// </remarks>
    private const string RequestFaultLogMessage =
        "{ReportTitle} ({Severity}): an unhandled request fault was formatted through the legacy "
        + "system-error block; the host continues. TraceId={TraceId} Method={RequestMethod} "
        + "Route={RoutePattern} Status={ResponseStatus} Number={ErrorNumber} "
        + "WireRetCode={WireRetCode} FaultTypes={FaultTypes} FaultObject={ErrorObject} "
        + "FaultMember={ObjectEvent} Line={ErrorLine}";

    /// <summary>
    /// The operator-channel record for the one case in which no body can be written.
    /// </summary>
    /// <remarks>
    /// This record carries the correlation identifier too, and for a reason specific to it: it is
    /// written precisely when the caller receives NO body, so the caller cannot have been handed the
    /// identifier. Without it here, the two records this request produced - the fault record and
    /// this one - could not be tied to each other at all (DECISION 7).
    /// </remarks>
    private const string ResponseAlreadyStartedLogMessage =
        "The response had already started when the system-error handler ran, so no problem-details "
        + "body could be written for this request. TraceId={TraceId}";

    /// <summary>
    /// The operator-channel record for a response the caller has abandoned.
    /// </summary>
    /// <remarks>
    /// DISTINCT FROM THE ALREADY-STARTED RECORD BECAUSE THE CAUSE IS DIFFERENT AND SO IS THE ACTION.
    /// An already-started response is a pipeline-ordering fact about this service; an aborted request
    /// is a fact about the caller, and the correct response to it is to write nothing at all rather
    /// than to attempt a write that can only fault. Sharing one message would tell an operator that
    /// their own pipeline had gone wrong when it had not.
    /// </remarks>
    private const string ResponseAbandonedLogMessage =
        "The caller abandoned the request before the system-error handler could write a "
        + "problem-details body, so none was written. TraceId={TraceId}";

    /// <summary>
    /// The operator-channel record for a caller-attributable cancellation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WRITTEN AT DEBUG, AND THE LEVEL IS THE POINT. A caller that hangs up is not a fault of this
    /// service: it is an ordinary, expected event that a browser tab closing produces. Recording it
    /// at error - which is what happened while every cancellation fell into the generic path -
    /// manufactures a server-fault signal out of normal client behaviour, and a service whose error
    /// rate tracks how often callers navigate away cannot be monitored.
    /// </para>
    /// <para>
    /// Nothing derived from the exception is read. The record names the request's own method and
    /// route PATTERN and nothing else, on the same allowlist reasoning as the request-fault record
    /// (C-F): a cancellation can carry an arbitrary upstream message just as any other fault can.
    /// </para>
    /// </remarks>
    private const string RequestCancelledLogMessage =
        "The caller cancelled the request, so no problem-details body was written and the host was "
        + "not asked to stop. TraceId={TraceId} Method={RequestMethod} Route={RequestRoute} "
        + "FaultTypes={FaultTypes}";

    /// <summary>
    /// The operator-channel record for a body write that faulted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A WRITE FAILURE MUST NEVER DISPLACE THE FAULT THAT CAUSED IT. Before the structural-fault path
    /// was made finally-safe, a fault here propagated out of the handler and took the termination
    /// request with it, so an assertion failure could leave the host running - the write, which is a
    /// best-effort courtesy to the caller, was able to cancel the one action that is not optional.
    /// This record exists so the secondary failure is still visible without being confused for the
    /// primary one.
    /// </para>
    /// <para>
    /// The exception TYPE CHAIN is recorded rather than the exception, on the allowlist reasoning of
    /// C-F: a transport fault's message can carry an address, and this record is written on both the
    /// structural and the request path.
    /// </para>
    /// </remarks>
    private const string ResponseWriteFailedLogMessage =
        "Writing the problem-details body faulted, so the caller received no body for this request. "
        + "The originating fault is recorded separately and is unaffected. TraceId={TraceId} "
        + "WriteFaultTypes={WriteFaultTypes}";

    /// <summary>
    /// The separator between links of the exception type chain the allowlisted record carries,
    /// pointing from the outermost type towards the innermost cause.
    /// </summary>
    private const string ExceptionTypeChainSeparator = " <- ";

    /// <summary>
    /// The marker appended when an exception chain is deeper than
    /// <see cref="MaximumDescribedExceptionDepth"/>, so a truncated chain is never mistaken for a
    /// complete one.
    /// </summary>
    private const string ExceptionTypeChainTruncationMarker = "...";

    /// <summary>
    /// How many links of an exception chain the allowlisted record describes.
    /// </summary>
    /// <remarks>
    /// A chain is bounded rather than walked to its end because its depth is not under this
    /// service's control - a nesting library can produce an arbitrarily deep one - and an unbounded
    /// walk would make the size of a log record a function of upstream behaviour. Eight links reach
    /// the root cause of every chain this system produces while keeping the record a fixed-size
    /// object.
    /// </remarks>
    private const int MaximumDescribedExceptionDepth = 8;

    /// <summary>
    /// The route description used when no endpoint matched, so the placeholder is never filled with
    /// a caller-supplied request path.
    /// </summary>
    /// <remarks>
    /// A raw request path is caller-controlled text and can carry a token or an identifier in a
    /// segment or a query, so it is not allowlisted (C-F). Nothing is lost: the host's own
    /// per-request log records the path, and the correlation identifier of DECISION 7 ties the two
    /// records together.
    /// </remarks>
    private const string UnroutedRouteDescription = "(unrouted)";

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

    /// <summary>
    /// Reports how many fields a pre-decode record's payload carried and how many of the seven legacy
    /// error fields <see cref="Decode(SystemErrorInfo?)"/> will therefore populate, WITHOUT decoding
    /// anything and without reading any field's content.
    /// </summary>
    /// <param name="error">
    /// The PRE-DECODE record - the one whose object member still carries the sentinel. Passing a
    /// post-decode record answers about the wrong input, because a complete decode OVERWRITES the
    /// sentinel [<c>ws_objects/pfw.pbl.src/pfw.sra:L121</c>]. <see langword="null"/> is treated as an
    /// empty record.
    /// </param>
    /// <returns>The shape, whose every member is an integer or a boolean.</returns>
    /// <remarks>
    /// <para>
    /// THE THREE ARITY CONDITIONS ARE THE DECODE'S OWN, EXPRESSED ONCE MORE RATHER THAN GUESSED. The
    /// sentinel test, the at-least-two test and the exactly-seven test are read from the same three
    /// constants the decode reads, so the two cannot drift: if the decode's arity branches change, this
    /// changes with them.
    /// </para>
    /// <para>
    /// This is a separate method rather than an out-parameter on the decode because the decode's
    /// signature is the legacy protocol's own shape and every existing caller and test is written
    /// against it. Nothing about the decode changes; an observation about it is added beside it.
    /// </para>
    /// </remarks>
    public static SystemErrorPayloadShape DescribePayloadShape(SystemErrorInfo? error)
    {
        SystemErrorInfo source = error ?? new SystemErrorInfo();

        // [ws_objects/pfw.pbl.src/pfw.sra:L114] Nothing is split unless the sentinel matches, so a
        // non-assert system error has no payload shape to report rather than a shape of zero fields.
        if (!string.Equals(source.Object, AssertErrorObject, StringComparison.Ordinal))
        {
            return new SystemErrorPayloadShape
            {
                SentinelMatched = false,
                FieldCount = 0,
                DecodedFieldCount = 0,
            };
        }

        int fieldCount = SplitPayload(source.Text).Count;

        // [ws_objects/pfw.pbl.src/pfw.sra:L116,L119] The two arity gates, in the decode's own order.
        // Seven fields populate all seven; at least two populate exactly the number and the text; and
        // fewer than two populate none.
        int decodedFieldCount = fieldCount switch
        {
            CompleteFieldCount => CompleteFieldCount,
            >= MinimumDecodableFieldCount => MinimumDecodableFieldCount,
            _ => 0,
        };

        return new SystemErrorPayloadShape
        {
            SentinelMatched = true,
            FieldCount = fieldCount,
            DecodedFieldCount = decodedFieldCount,
        };
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
    /// Chooses the HTTP status the caller channel answers with.
    /// </summary>
    /// <param name="exception">The fault that escaped the pipeline.</param>
    /// <param name="structuralFault">Whether the fault is a decoded assertion failure.</param>
    /// <returns>
    /// The client status the framework attached to a request it could not accept, or
    /// <c>500</c> in every other case.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <b>A REQUEST THIS SERVICE COULD NOT ACCEPT IS THE CALLER'S ERROR, AND THE FRAMEWORK ALREADY SAID
    /// SO.</b> Minimal-API parameter binding raises <see cref="BadHttpRequestException"/> carrying the
    /// status it intended - <c>400</c> for an absent required parameter or a value it cannot convert - and
    /// discarding that status answered <c>500</c> with the catalogue's unknown code, telling a caller that
    /// this service had failed when nothing here had.
    /// </para>
    /// <para>
    /// TWO NARROWING CONDITIONS, EACH LOAD BEARING. A structural fault is this service's own and is
    /// terminal (DECISION 5), so nothing carried on an exception may reclassify it. And only a status in
    /// the client range is honoured: a carried <c>5xx</c> is already a server fault, and a value outside
    /// the range is not a classification this handler can act on, so the test is a range check rather than
    /// trust in whatever integer arrives.
    /// </para>
    /// <para>
    /// <see langword="internal"/> so the sibling test project can drive every arm directly, which is what
    /// makes the reclassification provable without producing a real binding failure through a host.
    /// </para>
    /// </remarks>
    internal static int ResolveResponseStatus(Exception exception, bool structuralFault)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // A STRUCTURAL FAULT IS ALWAYS THIS SERVICE'S OWN AND IS ALWAYS TERMINAL (DECISION 5), so nothing
        // a framework exception carries may reclassify it.
        if (structuralFault)
        {
            return StatusCodes.Status500InternalServerError;
        }

        // ONLY A 4XX IS HONOURED. A carried 5xx is already a server fault and would change nothing, and a
        // carried value outside the client range is not a classification this handler can act on - so the
        // range test is what keeps the arm narrow rather than trusting whatever integer arrives.
        return exception is BadHttpRequestException malformed
            && malformed.StatusCode >= StatusCodes.Status400BadRequest
            && malformed.StatusCode < StatusCodes.Status500InternalServerError
            ? malformed.StatusCode
            : StatusCodes.Status500InternalServerError;
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
    /// The decoded number when it is a member of the published closed value set AND the kernel's own
    /// failure predicate accepts it, and the catalogue's unknown value in every other case.
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
    /// <para>
    /// <b>AND THE ASSERT PATH IS HELD TO THE SAME RULE, WHICH IS THE POINT OF THE SECOND CHECK.</b>
    /// Membership of the published set is NOT the same question as "is this a failure". The legacy
    /// algebra is tri-state and has a documented hole that this system preserves verbatim:
    /// <c>PREVENT</c> is 1 and <c>IsSucceeded</c> tests greater-than-or-equal-to zero, so a
    /// prevention reads as a SUCCESS [<c>ws_objects/pfw.shared.pbl.src/issucceeded.srf:L11-L13</c>,
    /// <c>retcode.sru:L42</c>], and <c>CANCELLED</c> is excluded from <c>IsFailed</c>, so a
    /// cancellation is NEITHER [<c>ws_objects/pfw.shared.pbl.src/isfailed.srf:L11-L13</c>]. The
    /// payload's first field is attacker-shaped rather than trustworthy - it is whatever text arrived
    /// on the wire, and <see cref="ParseLegacyLong"/> maps anything unparseable to ZERO, which is
    /// <c>OK</c> - so without this check a fault fatal enough to terminate the process could publish
    /// <c>retCode</c> 0 or 1 in a 500 body and a machine consumer applying the project's own
    /// <c>IsSucceeded</c> would read the crash as a success.
    /// </para>
    /// <para>
    /// THE KERNEL PREDICATE IS CONSUMED RATHER THAN THE COMPARISON RE-DERIVED, and it is the same
    /// guard the four service composition roots already apply to a framework-generated failure status
    /// (<c>ClassifyFailure</c> in each <c>Program.cs</c>). Two paths produce the <c>retCode</c> member
    /// of an error body - that one and this one - so they answer the question the same way or the
    /// published contract has two meanings. Nothing observable about the LEGACY protocol changes: the
    /// decode is untouched, the operator report is untouched, the decoded number still reaches the
    /// operator channel unmodified, and the process still terminates. Only the value published in a
    /// net-new HTTP body - which the legacy has no analogue for at all, having no listener - is
    /// narrowed to a code that cannot be misread.
    /// </para>
    /// </remarks>
    public static long ResolveRetCode(SystemErrorInfo? error, bool decodedFromAssertPayload)
    {
        if (error is null || !decodedFromAssertPayload)
        {
            return RetCode.UNKNOWN;
        }

        long candidate = IsPublishedRetCode(error.Number) ? error.Number : RetCode.UNKNOWN;

        return Predicates.IsFailed(candidate) ? candidate : RetCode.UNKNOWN;
    }

    /// <summary>
    /// Tests membership of the closed return-code value set that
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c> publishes for its error body.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true"/> when the value is published.</returns>
    /// <remarks>
    /// <para>
    /// The membership test is written against the catalogue's own named constants rather than
    /// against literals, so it cannot drift from them. The failure band is contiguous and gap-free
    /// from the first failure code down to the retry code, which is why one range test covers it,
    /// and the cancelled value sits inside that band. Nothing here declares a constant; it only
    /// consumes them.
    /// </para>
    /// <para>
    /// <b>THIS DELIBERATELY ADMITS <c>OK</c>, <c>PREVENT</c> AND <c>CANCELLED</c>, AND IT IS NOT THE
    /// PLACE THAT DECIDES WHETHER A CODE MAY APPEAR IN AN ERROR BODY.</b> The published contract
    /// declares all three, and narrowing the set here would make this method answer a question it is
    /// not asked - "is this value published" is a schema question, and "is this value a failure" is an
    /// algebra question. The second question is answered exactly once, by the kernel predicate in
    /// <see cref="ResolveRetCode(SystemErrorInfo?, bool)"/>, so a future reader looking for the reason
    /// a fatal fault cannot publish a success code finds one guard rather than two overlapping ones.
    /// </para>
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

    /// <summary>
    /// Describes an exception chain by TYPE ALONE, for the allowlisted operator record of
    /// DECISION 2.
    /// </summary>
    /// <param name="exception">The exception, which may be <see langword="null"/>.</param>
    /// <returns>
    /// The chain from the outermost type towards its innermost cause, joined by
    /// <see cref="ExceptionTypeChainSeparator"/>, bounded at
    /// <see cref="MaximumDescribedExceptionDepth"/> links and marked with
    /// <see cref="ExceptionTypeChainTruncationMarker"/> when it was cut short; the empty string for
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A type chain is what makes an ordinary-fault record actionable while carrying no content: a
    /// type name is chosen by whoever wrote the throwing code, never by a caller and never by data,
    /// so it cannot carry a credential, a statement, a URL or personal data. The nesting is what
    /// matters diagnostically - a timeout wrapped in a transport failure wrapped in an invalid
    /// operation is a different fault from any one of the three alone - which is why the chain is
    /// walked rather than only the outermost type being reported.
    /// </para>
    /// <para>
    /// The assembly-qualified name is deliberately not used and the namespace-qualified name is: the
    /// namespace is what distinguishes same-named types, whereas the version, culture and public key
    /// token add length without adding distinction.
    /// </para>
    /// <para>
    /// Only the primary <see cref="Exception.InnerException"/> chain is followed. An aggregate fault
    /// exposes its first inner exception through that member as well, so its root is still reached;
    /// enumerating every branch of a fan-out would make the record's size a function of how many
    /// parallel operations failed, which is the same unbounded-growth problem
    /// <see cref="MaximumDescribedExceptionDepth"/> exists to prevent.
    /// </para>
    /// <para>
    /// This method is public and static so that every branch - null, single, nested, and deeper than
    /// the bound - is reachable from a plain unit test with no host (C-H).
    /// </para>
    /// </remarks>
    public static string DescribeExceptionTypes(Exception? exception)
    {
        if (exception is null)
        {
            return string.Empty;
        }

        StringBuilder chain = new();
        Exception? current = exception;

        for (int depth = 0; depth < MaximumDescribedExceptionDepth && current is not null; depth++)
        {
            if (depth != 0)
            {
                chain.Append(ExceptionTypeChainSeparator);
            }

            Type type = current.GetType();

            // The namespace-qualified name, falling back to the bare name for a type that reports
            // none - a generic parameter or a type emitted without a namespace.
            chain.Append(type.FullName ?? type.Name);

            current = current.InnerException;
        }

        if (current is not null)
        {
            chain.Append(ExceptionTypeChainSeparator).Append(ExceptionTypeChainTruncationMarker);
        }

        return chain.ToString();
    }

    /// <summary>
    /// Resolves the ONE correlation identifier both channels carry (DECISION 7).
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>
    /// The ambient distributed-trace identifier when one exists, and the host's own per-request
    /// identifier otherwise. Never <see langword="null"/>; the empty string only in the pathological
    /// case where the host supplies no identifier either.
    /// </returns>
    /// <remarks>
    /// Called exactly once per fault, from <see cref="TryHandleAsync"/>, and its result is then
    /// passed to every site that needs it. Re-resolving instead of passing is what let the two
    /// channels disagree before: the ambient activity is not guaranteed to be the same object for
    /// the whole of a request, so two reads can legitimately differ and a caller would then hold an
    /// identifier that appears in no record.
    /// </remarks>
    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        string? activityId = Activity.Current?.Id;
        return string.IsNullOrEmpty(activityId) ? httpContext.TraceIdentifier ?? string.Empty : activityId;
    }

    /// <summary>
    /// Describes which route the fault occurred on, using the route PATTERN and never the request
    /// path.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <returns>
    /// The matched endpoint's raw route pattern, or <see cref="UnroutedRouteDescription"/> when no
    /// endpoint matched or the matched endpoint carries no pattern.
    /// </returns>
    /// <remarks>
    /// The pattern is the allowlisted form of "where did this happen": it is authored in this
    /// codebase and its parameter placeholders stand where caller values would be, so it identifies
    /// the route while disclosing none of the values bound to it. The concrete path, which does carry
    /// those values, is deliberately not read here (C-F).
    /// </remarks>
    private static string DescribeRoute(HttpContext httpContext)
    {
        string? pattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        return string.IsNullOrEmpty(pattern) ? UnroutedRouteDescription : pattern;
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

        // Format() runs on BOTH paths, unconditionally, because the legacy's formatting statements
        // sit outside its decode branch [ws_objects/pfw.pbl.src/pfw.sra:L129 versus :L114-L127]
        // (C-B, DECISION 5). Which of its members reach a log record is a separate question, decided
        // by DECISION 2.
        SystemErrorReport report = Format(decoded);

        // ==========================================================================================
        //  ⚠ A FAULT THAT CARRIES ITS OWN CLIENT STATUS KEEPS IT, INSTEAD OF BECOMING A 500.
        //
        //  The framework raises BadHttpRequestException for a request IT could not accept - a required
        //  query parameter that is absent, a route value that will not convert, a body over the
        //  configured size - and that exception carries the status the framework intended. This handler
        //  discarded it and answered 500 with UNKNOWN, so a caller that omitted a query parameter was
        //  told THIS SERVICE had failed. Measured: GET /v1/datawindow/event-gate with no sessionId
        //  answered 500 / -4000, while the same route with an EMPTY sessionId answered 400 / -3 - two
        //  spellings of one mistake, one of them blamed on the wrong party.
        //
        //  IT IS A DEFENCE IN DEPTH RATHER THAN THE PRIMARY FIX. The projection now binds that
        //  parameter nullable and applies the operation's own declared parameter contract, so the
        //  measured case no longer reaches this handler at all. This arm is what makes every OTHER
        //  binding refusal - present and future, on any route - answer the caller's own status instead
        //  of a server fault.
        //
        //  NOT FOR A STRUCTURAL FAULT, AND ONLY FOR A 4XX. A structural fault is terminal and is always
        //  reported as this service's own (DECISION 5), and a carried 5xx is already a server fault, so
        //  neither can be reclassified by a value the framework put on an exception.
        // ==========================================================================================
        int responseStatus = ResolveResponseStatus(exception, structuralFault);

        long wireRetCode = responseStatus == StatusCodes.Status500InternalServerError
            ? ResolveRetCode(decoded, structuralFault)

            // THE CALLER'S OWN ARGUMENT IS WHAT FAILED, so the code is the one the projection answers
            // for exactly the same condition - which is what makes an absent parameter and an empty one
            // indistinguishable to a caller, as two spellings of one mistake should be.
            : RetCode.E_INVALID_ARGUMENT;

        // The single correlation identifier, resolved BEFORE anything is written and then passed to
        // every site that needs it, so the value the caller is handed is provably the value in the
        // record (DECISION 7). Resolving it here rather than at each site is the fix, not an
        // optimization.
        string correlationId = ResolveCorrelationId(httpContext);

        // ---- CANCELLATION, CLASSIFIED BEFORE THE GENERIC PATH AND NOT INSIDE IT.
        //
        // A caller that hangs up produces an OperationCanceledException here, and every one of them
        // used to fall through to the request-fault path: recorded at error as a server fault, and
        // then handed to a body write against a socket that is no longer there. Both halves are wrong.
        // A cancellation is not this service's fault, and a service whose error rate tracks how often
        // callers navigate away cannot be monitored; and the write can only fault, which before the
        // structural path was made finally-safe was enough to skip termination altogether.
        //
        // THE ATTRIBUTION TEST IS THE WHOLE DISTINCTION, and it is deliberately not "is this an
        // OperationCanceledException". A cancellation with NEITHER the request's own abort token nor
        // the handler's token signalled did not come from the caller - it came from something inside
        // this process abandoning the work, an internal deadline or a linked token of our own - and
        // that IS a fault worth a 500 and an operator record. Only a cancellation the caller can be
        // held responsible for takes this arm.
        //
        // GUARDED ON !structuralFault FOR COMPLETENESS RATHER THAN NECESSITY. AssertionFailure is not
        // an OperationCanceledException today, so the guard changes nothing now; it is here so that a
        // future type which is both can never take the quiet arm, because a structural fault is
        // terminal (DECISION 5) whatever else is true of it.
        if (!structuralFault
            && exception is OperationCanceledException
            && (httpContext.RequestAborted.IsCancellationRequested
                || cancellationToken.IsCancellationRequested))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    RequestCancelledLogMessage,
                    correlationId,
                    httpContext.Request.Method,
                    DescribeRoute(httpContext),
                    DescribeExceptionTypes(exception));
            }

            // REPORTED AS HANDLED, so nothing downstream attempts a body of its own. There is nobody
            // left to receive one, and a middleware fallback writing to an abandoned socket produces
            // a second, equally pointless fault. The host is NOT asked to stop: a caller hanging up is
            // not a structural fault and never was.
            return true;
        }

        // ---- OPERATOR CHANNEL (DECISION 2), in one of its two forms.
        if (structuralFault)
        {
            // THE UNREDACTED FORM: the faithful successor to the dialog, for the audience the dialog
            // had. All seven decoded fields plus the exact composed block, with the title and the
            // severity. The exception itself is passed so the managed stack trace reaches the
            // operator through the logging abstraction's own argument rather than through the legacy
            // call-stack field, which stays faithful to the legacy and is empty on this path unless
            // a seven-field payload supplied it.
            //
            // This form is entitled to the full detail for two independent reasons. It IS the
            // legacy's own behaviour, which C-B requires be preserved rather than trimmed. And the
            // path it belongs to terminates the host (DECISION 4), so it is written at most once per
            // process and cannot become a volume channel through which upstream content accumulates.
            //
            // C-F: no field read here is a credential, a key or a connection string, and the one
            // upstream field known to carry interpolated literal statement text is redacted or
            // parameter-separated by the service that owns it - this file never reads, echoes or
            // reconstructs it.
            // THE SHAPE IS DESCRIBED FROM THE PRE-DECODE RECORD, NOT THE DECODED ONE, because a
            // complete decode overwrites the sentinel it discriminated on
            // [ws_objects/pfw.pbl.src/pfw.sra:L121] and the shape would then read as "not an assert".
            SystemErrorPayloadShape payloadShape = DescribePayloadShape(raised);

            _logger.LogCritical(
                exception,
                AssertionFailureLogMessage,
                report.Title,
                report.Severity,
                correlationId,
                decoded.Number,
                wireRetCode,
                payloadShape.FieldCount,
                payloadShape.DecodedFieldCount,
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
            // THE ALLOWLISTED FORM. The exception object is NOT passed to the logger, and neither
            // the decoded text, the source field nor the formatted block is read: all four are
            // arbitrary upstream content, and the logging abstraction renders a passed exception
            // through its own string conversion, which includes every message in the chain
            // (C-F). What is passed instead identifies the fault without quoting it - the request's
            // own method, the route PATTERN rather than the path, the status the caller is about to
            // receive, the two numeric codes, the exception TYPE chain, and the fault site as a
            // declaring-type name, a member name and a source line, all three of which name this
            // system's own code.
            // THE LEVEL FOLLOWS THE CLASSIFICATION, and it has to: a service whose error rate tracks how
            // often callers send malformed requests cannot be monitored, which is the same reasoning the
            // cancellation arm above already applies. A client error is recorded at warning, a server
            // fault at error, and the STATUS is now the one the caller actually receives rather than a
            // hardcoded 500 that could disagree with the response.
            _logger.Log(
                responseStatus >= StatusCodes.Status500InternalServerError
                    ? LogLevel.Error
                    : LogLevel.Warning,
                RequestFaultLogMessage,
                report.Title,
                report.Severity,
                correlationId,
                httpContext.Request.Method,
                DescribeRoute(httpContext),
                responseStatus,
                decoded.Number,
                wireRetCode,
                DescribeExceptionTypes(exception),
                decoded.Object,
                decoded.ObjectEvent,
                decoded.Line);
        }

        // ---- CALLER CHANNEL: the redacted body (DECISION 2), carrying the SAME correlation
        // identifier that was just written to the operator record (DECISION 7).
        //
        // THE WRITE IS BEST EFFORT AND THE HALT IS NOT, WHICH IS WHY THEY ARE SEPARATED BY A finally.
        // Until they were, the halt ran only if the write returned: a client disconnect mid-write, an
        // IOException from a broken socket, or the write's own cancellation propagated out of this
        // method and took the termination request with it, so a decoded assertion failure could leave
        // the host running and the framework finalize step unrun. The legacy has no equivalent
        // opportunity to skip its halt - the dialog it shows cannot fail in a way that skips the
        // statement after it - so a write that can cancel the halt is a behaviour the refactor
        // introduced and this removes.
        bool handled = false;

        try
        {
            handled = await WriteRedactedProblemAsync(
                    httpContext,
                    responseStatus,
                    wireRetCode,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception writeFailure) when (!ReferenceEquals(writeFailure, exception))
        {
            // THE SECONDARY FAILURE IS RECORDED, NOT SWALLOWED, AND IT DOES NOT REPLACE THE FIRST.
            // Rethrowing would restore exactly the defect being fixed on the structural path and would
            // replace a diagnosed fault with a transport detail on the request path, so the caller's
            // channel is abandoned - it is already unreachable, or the write would have succeeded -
            // and `handled` stays false so the middleware can decide for itself what to do next.
            //
            // The `when` filter exists so that the ORIGINAL exception, if it somehow re-emerges from
            // the write path, is not quietly reclassified as a write failure.
            _logger.LogWarning(
                ResponseWriteFailedLogMessage,
                correlationId,
                DescribeExceptionTypes(writeFailure));
        }
        finally
        {
            // ---- THE HALT PATH [ws_objects/pfw.pbl.src/pfw.sra:L143]. Requested last, exactly as the
            // legacy halts after its dialog, and requested on EVERY path out of the write above: when
            // it succeeded, when it declined because the response had started or been abandoned, and
            // when it faulted. A structural fault is terminal regardless of what the caller did or did
            // not receive (DECISION 5).
            if (structuralFault)
            {
                _requestProcessTermination(StructuralFaultExitCode);
            }
        }

        return handled;
    }

    /// <summary>
    /// Writes the redacted problem-details body that conforms to
    /// <c>shared/PowerFramework.Contracts/OpenApi/gateway.v1.yaml</c>.
    /// </summary>
    /// <param name="httpContext">The request context.</param>
    /// <param name="wireRetCode">The return code chosen by <see cref="ResolveRetCode"/>.</param>
    /// <param name="correlationId">
    /// The correlation identifier already written to the operator record, passed in rather than
    /// resolved here so that the two channels cannot disagree (DECISION 7).
    /// </param>
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
    /// carrying it conforms rather than extends. It arrives as a PARAMETER, already written to the
    /// operator record, which is what makes the bridge load-bearing rather than advertised
    /// (DECISION 7).
    /// </para>
    /// </remarks>
    private async ValueTask<bool> WriteRedactedProblemAsync(
        HttpContext httpContext,
        int responseStatus,
        long wireRetCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (httpContext.Response.HasStarted)
        {
            // Nothing can be written once the response is on the wire. This is reported rather than
            // swallowed. The correlation identifier is carried here too, because on this path the
            // caller receives no body and therefore never learns it (DECISION 7).
            _logger.LogWarning(ResponseAlreadyStartedLogMessage, correlationId);
            return false;
        }

        if (httpContext.RequestAborted.IsCancellationRequested)
        {
            // THE SECOND WAY A RESPONSE STOPS BEING VIABLE, and it is not the same as the first. The
            // response has not started, so nothing above would have refused - but the caller has gone,
            // so the write below can only fault. Attempting it and catching the fault would produce
            // identical behaviour with a worse record: an operator would see a transport failure and
            // have to work out that it was expected. Refusing here says so directly.
            //
            // THIS IS NOT THE CANCELLATION CLASSIFICATION IN TryHandleAsync. That arm handles a fault
            // that IS a cancellation; this one handles any fault at all arriving on a request the
            // caller has since abandoned - a structural assertion failure on an abandoned request
            // takes this path, is recorded in full on the operator channel, and still terminates the
            // host, because the halt does not depend on the caller being there to hear about it.
            _logger.LogWarning(ResponseAbandonedLogMessage, correlationId);
            return false;
        }

        httpContext.Response.StatusCode = responseStatus;

        bool serverFault = responseStatus >= StatusCodes.Status500InternalServerError;

        ProblemDetails problem = new()
        {
            Type = ProblemType,

            // THE TITLE AND THE DETAIL FOLLOW THE STATUS, because the server-fault prose is a statement
            // about this service and would be a false one on a client error: telling a caller that "an
            // internal error occurred" for a request IT composed wrongly sends it to look for an outage.
            // The client-error prose names no value and echoes nothing - in particular not the framework
            // exception's own message, which names the parameter it could not bind and is therefore
            // shaped by caller content (C-F).
            Title = serverFault ? ProblemTitle : ClientErrorProblemTitle,
            Status = responseStatus,
            Detail = serverFault ? ProblemDetail : ClientErrorProblemDetail,

            // The caller's own request path. It discloses nothing the caller did not send.
            Instance = httpContext.Request.Path.HasValue ? httpContext.Request.Path.Value : null,
        };

        problem.Extensions[RetCodeExtensionMember] = wireRetCode;

        // The identical value that was just written to the operator record - not a second resolution
        // of it (DECISION 7). The emptiness guard remains because a host that supplies no identifier
        // at all would otherwise publish an empty member, which advertises a bridge with no far side.
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
