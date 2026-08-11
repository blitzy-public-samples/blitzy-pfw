// --------------------------------------------------------------------------------------------------
// THE REFUSAL FOR A BODY THIS SERVICE CANNOT READ AS THE OPERATION DECLARES IT
//
// WHY THIS EXISTS AT ALL. shared/PowerFramework.Contracts/OpenApi/security.v1.yaml closes every one of
// this service's request schemas with `additionalProperties: false`, and closure is a statement about what
// the SERVICE accepts - a document-validating client is only one of the two parties that has to honour it.
// The serializer is therefore configured to refuse an unknown member rather than discard it. Refusing
// leaves a question this file answers: what does the caller receive?
//
// THREE PROPERTIES THE ANSWER HAS TO HAVE, AND EACH ONE FAILED WITHOUT THIS FILE.
//
//   1. IT IS A CLIENT ERROR. A body the caller composed wrongly is a 400, never a 500. Measured before
//      this file existed: an unknown member and a truncated body both answered 500 with the generic
//      unhandled-fault body, which tells a caller its own request was fine and this service is broken.
//
//   2. IT IS THE SAME IN EVERY ENVIRONMENT. RouteHandlerOptions.ThrowOnBadRequest defaults to TRUE in
//      Development and FALSE elsewhere, so the framework's own two answers to one malformed body are an
//      exception in one deployment and a bodiless 400 in another. Contract fidelity cannot depend on which
//      one a caller happens to reach, so the setting is pinned in Program.cs and the exception is handled
//      here - one answer, everywhere.
//
//   3. IT CARRIES NO CALLER VALUE AND NO SERIALIZER TEXT. The detail is FIXED PROSE authored here. A
//      System.Text.Json exception message quotes the offending fragment of the payload, and its Path names
//      the member the caller sent; on a cryptographic surface the payload is plaintext, ciphertext or a key
//      reference, so echoing either into a problem body or a log record would publish request content. The
//      sibling absent-member refusal makes the same commitment for the same reason and states it in the
//      same terms: every name it prints is a compile-time nameof from its own file, never a caller value.
//
// WHAT IT DOES NOT CHANGE. It never runs for a body that DESERIALIZED. An absent member still reaches its
// handler and is still refused with E_INVALID_ARGUMENT naming its wire spelling, because absent and
// present-but-unknown are disjoint conditions and only the second one reaches the serializer's refusal.
// Every rejection arm the crypto surface owns is untouched.
// --------------------------------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using PowerFramework.Shared.Kernel;

namespace PowerFramework.Security.Endpoints;

/// <summary>
/// Converts a request body this service cannot read into the single published problem shape, carrying
/// <see cref="RetCode.E_INVALID_ARGUMENT"/> and a fixed detail.
/// </summary>
/// <remarks>
/// <para>
/// INSTALLED INSIDE THE EXCEPTION HANDLER RATHER THAN AS ONE OF ITS HANDLERS, AND THAT PLACEMENT IS THE
/// POINT. The framework's exception-handling middleware treats what it catches as an unhandled server
/// fault and records it as such, with the exception object attached - and a serializer exception's message
/// carries a fragment of the payload. Catching the fault before it can reach that middleware is what keeps
/// request content out of the log entirely, rather than trusting a downstream renderer not to print it.
/// </para>
/// <para>
/// A fault it does not recognise is rethrown untouched, so the structural-fault path and the ordinary
/// unhandled-fault path both behave exactly as they did.
/// </para>
/// </remarks>
internal static class MalformedRequestBody
{
    /// <summary>
    /// The detail every refusal of this kind carries. FIXED PROSE: no member name, no payload fragment and
    /// no serializer text, because all three are caller-supplied on this surface.
    /// </summary>
    internal const string RefusalDetail =
        "The request body could not be read as this operation declares it. It is either not well-formed "
        + "JSON, or it carries a member this operation does not declare - every request schema in the "
        + "published contract sets additionalProperties to false, so an undeclared member is refused "
        + "rather than ignored. No part of the body is echoed here.";

    /// <summary>Installs the refusal, inside the surrounding exception handler.</summary>
    /// <param name="app">The application being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    internal static void Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ILoggerFactory loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

        app.Use(async (context, next) =>
        {
            try
            {
                await next(context).ConfigureAwait(false);
            }
            catch (Exception exception) when (Describes(exception))
            {
                if (!await TryWriteRefusalAsync(context, loggerFactory).ConfigureAwait(false))
                {
                    throw;
                }
            }
        });
    }

    /// <summary>
    /// Decides whether a fault is a body this service could not read.
    /// </summary>
    /// <param name="exception">The fault to classify.</param>
    /// <returns><see langword="true"/> when the caller's request is at fault rather than this service.</returns>
    /// <remarks>
    /// <para>
    /// TWO SHAPES, BECAUSE THE FRAMEWORK RAISES TWO. Minimal-API body binding wraps a serializer failure
    /// in a bad-request exception carrying the status it intends; a serializer failure raised outside that
    /// wrapping - a handler reading the body itself - arrives as the serializer's own exception. Both are
    /// the same fact about the request.
    /// </para>
    /// <para>
    /// THE STATUS IS CHECKED RATHER THAN ASSUMED. A bad-request exception also carries 413 for a body over
    /// the configured limit and 408 for a client that stopped sending, and answering either of those with
    /// an argument fault would misdescribe it. Only the 400 arm is claimed here.
    /// </para>
    /// </remarks>
    internal static bool Describes(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            BadHttpRequestException badRequest => badRequest.StatusCode == StatusCodes.Status400BadRequest,
            JsonException => true,
            _ => false,
        };
    }

    /// <summary>
    /// Writes the refusal, unless the response is no longer viable.
    /// </summary>
    /// <param name="context">The request being answered.</param>
    /// <param name="loggerFactory">The factory the problem record is written through.</param>
    /// <returns>
    /// <see langword="true"/> when the refusal was written; <see langword="false"/> when the response had
    /// already started, in which case the caller rethrows so the fault is not silently swallowed.
    /// </returns>
    /// <remarks>
    /// Body binding completes before a handler writes anything, so the started-response arm is not
    /// reachable through the ordinary path. It is here because swallowing a fault that could not be
    /// answered would turn a visible failure into a truncated response with no record of why.
    /// </remarks>
    private static async Task<bool> TryWriteRefusalAsync(HttpContext context, ILoggerFactory loggerFactory)
    {
        if (context.Response.HasStarted)
        {
            return false;
        }

        ProblemHttpResult refusal = ProblemResults.Create(
            RetCode.E_INVALID_ARGUMENT,
            RefusalDetail,
            ProblemSeverity.None,
            loggerFactory: loggerFactory);

        await refusal.ExecuteAsync(context).ConfigureAwait(false);

        return true;
    }
}
