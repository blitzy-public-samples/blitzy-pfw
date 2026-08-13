// ==============================================================================================
//  FaultRecord - how this service names a fault in a log record
//  --------------------------------------------------------------------------------------------
//  NO LEGACY SOURCE. The legacy library has no log pipeline at all: its diagnostic channel is a
//  modal dialog on the operator's own screen, and the operator was already entitled to everything
//  on it. This type exists because decomposition replaced that dialog with a record that is
//  retained, shipped off the host and searched by people who are not the caller.
//
//  WHY IT EXISTS AS A TYPE RATHER THAN AS A HABIT
//  --------------------------------------------------------------------------------------------
//  Persistence is the ONLY service that generates or executes SQL (AAP 0.1.1), so it is the only
//  one whose exceptions routinely carry a complete generated statement with its literal values
//  interpolated - the legacy runs without bind variables whenever DisableBind is set
//  [ws_objects/pfw.thread.ext.pbl.src/n_cst_thread_task_sqlbase.sru:L128-L129] - and the only one
//  whose connection-string faults can quote a database PATH or a PASSWORD. Constraint C-F says
//  none of that may reach a log.
//
//  THE DEFECT THIS CLOSES WAS PRESENT AT FIFTEEN SITES AND WAS INVISIBLE AT EVERY ONE. Each of
//  them wrote a carefully bounded message - numeric codes, fixed prose, an operation name - and
//  then passed the exception OBJECT as the logging abstraction's exception argument. Every provider
//  renders that argument by calling ToString(), which prints the unredacted message, every inner
//  exception's unredacted message and the stack. The bounded message was correct and irrelevant:
//  the argument beside it published everything the message had withheld.
//
//  ONE PLACE NAMES THE REDACTOR, WHICH IS THE POINT
//  --------------------------------------------------------------------------------------------
//  The shared primitive refuses to read an exception message without being handed a policy, and
//  this type is where this service's policy is named. Fifteen call sites therefore cannot each
//  choose a different one, and they cannot forget: there is no member here that returns a message
//  unredacted.
//
//  Instance rather than a fresh SqlRedactor, deliberately: Program.cs registers ISqlRedactor as
//  `SqlRedactor.Instance`, so a site reached through the container and a site reached through this
//  type are the SAME object with the same placeholder. A `new SqlRedactor()` here would be equal
//  in behaviour today and free to diverge tomorrow.
// ==============================================================================================

using PowerFramework.Shared.Diagnostics;

namespace PowerFramework.Persistence.Errors;

/// <summary>
/// Describes a fault for a log record under this service's redaction policy.
/// </summary>
/// <remarks>
/// <para>
/// PAIRED ON PURPOSE - the two members are meant to be used together, as
/// <c>FaultTypes={FaultTypes} RedactedMessage={RedactedMessage}</c>. The types locate the fault in
/// code and the redacted messages say what it reported; either alone leaves an operator guessing at
/// the half that is missing, and the pair together is what replaces the exception object the sites
/// used to attach.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY LOST IS THE MANAGED STACK, and the trade is stated rather than hidden. A
/// stack's frames carry argument values in no sanitised form, so it is upstream content in exactly
/// the sense that matters here; the type chain plus the method or operation name already locates the
/// fault precisely enough to find it in source. The one arm that still attaches an exception is the
/// structural-assertion arm in <c>Program.cs</c>, which is written at most once per process,
/// terminates the host, and reproduces the legacy application object's system-error dialog that
/// constraint C-B requires be preserved in full
/// [<c>ws_objects/pfw.pbl.src/pfw.sra:L111-L144</c>].
/// </para>
/// <para>
/// Both members are pure functions of their argument and hold no state, so they are safe to call
/// concurrently from any thread - which matters because most of their call sites are inside catch
/// blocks on worker threads.
/// </para>
/// </remarks>
internal static class FaultRecord
{
    /// <summary>
    /// Names the types in a fault's chain, outermost first, reading no message at all.
    /// </summary>
    /// <param name="error">The fault, or <see langword="null"/>.</param>
    /// <returns>The namespace-qualified type names joined outermost-first.</returns>
    internal static string Types(Exception? error) => ExceptionChain.DescribeTypes(error);

    /// <summary>
    /// Redacts every message in a fault's chain and joins them outermost first.
    /// </summary>
    /// <param name="error">The fault, or <see langword="null"/>.</param>
    /// <returns>The redacted messages joined outermost-first.</returns>
    /// <remarks>
    /// THE WHOLE CHAIN, NOT THE OUTERMOST MESSAGE. A provider fault is habitually wrapped - a task
    /// fault wrapping a command fault wrapping the driver's own - and the interpolated statement sits
    /// at the BOTTOM, so redacting only the outer message would leave the common case fully exposed.
    /// </remarks>
    internal static string RedactedMessages(Exception? error) =>
        ExceptionChain.DescribeMessages(error, SqlRedactor.Instance.Redact);
}
