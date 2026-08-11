// --------------------------------------------------------------------------------------------------
//  THE SECURITY RESPONSE HEADERS THIS SERVICE SETS ON EVERY RESPONSE IT WRITES
//
//  WHY THIS EXISTS. Decomposition created this system's first-ever ingress, and constraint C-G reads as
//  "every newly created surface is authenticated FROM THE OUTSET" (AAP 0.1.4) - authentication was in
//  place, but the responses themselves carried no protective directive at all. Measured on this service
//  before this file existed, its REST surface - /health and /v1/ping - answered with only Content-Type,
//  Date, Server and Transfer-Encoding. This is the only service in the estate that holds a storage
//  provider, so a cached response of its is a cached row.
//
//  THREE DIRECTIVES, EACH WITH ITS OWN REASON.
//
//    Cache-Control: no-store, no-cache, must-revalidate + Pragma: no-cache
//      RFC 6749 section 5.1 requires exactly this pair on a credential response and RFC 9700 restates it;
//      this service mints nothing, and the directive is applied all the same because this whole estate is
//      an authenticated API whose payloads are per-caller - a DataWindow row, a decrypted plaintext and a
//      problem document are all things a shared cache must not hold. Pragma is the HTTP/1.0 companion and
//      is sent only where Cache-Control is, so the two can never disagree.
//
//    X-Content-Type-Options: nosniff
//      Every response here declares its own media type - application/json or application/problem+json -
//      and content sniffing is a browser overriding that declaration. There is no case in which a
//      consumer of this estate benefits from a type this service did not state.
//
//    Strict-Transport-Security: max-age=31536000
//      Sent ONLY on a request that arrived over HTTPS, which is what RFC 6797 section 7.2 requires: a
//      host must not assert transport security over an insecure transport, and a compliant client ignores
//      it there anyway. Written directly rather than through UseHsts, and that is deliberate - UseHsts
//      excludes localhost, 127.0.0.1 and [::1] by default, so on the loopback estate this repository
//      actually runs it emits nothing and the header could never be observed. `includeSubDomains` is NOT
//      asserted: this service does not speak for its siblings' host names, and `preload` is not asserted
//      because that is a registration this repository has not made.
//
//  NO ROUTE HERE IS EXEMPTED, AND NONE NEEDS TO BE. Every directive below is applied ONLY when the
//  response does not already carry that header, so a route that ever does need to be cacheable can say so
//  itself and this middleware will not contradict it.
//
//  WHY IT IS A FILE PER SERVICE RATHER THAN ONE SHARED HELPER, stated so it does not read as an
//  accident. The AAP permits exactly one form of cross-service coupling - the published contracts
//  project - and its shared-library layer is defined as carrying "only pure behaviour and no I/O"
//  (AAP 0.1.5). Middleware over HttpResponse is neither, and AAP 0.3.1 enumerates the shared projects
//  exhaustively, so a seventh one would be a structural change to the plan rather than a fix. Each
//  service therefore owns its own copy, which is the isolation constraint C-A asks for made concrete.
// --------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Primitives;

namespace PowerFramework.Persistence.Endpoints;

/// <summary>
/// Sets this service's protective response headers on every response, without overriding a directive a
/// route set for itself.
/// </summary>
internal static class SecurityResponseHeaders
{
    /// <summary>
    /// The cache directive every response carries unless its route set one. RFC 6749 section 5.1's
    /// required value for a credential response, with <c>must-revalidate</c> added for an HTTP/1.0 cache
    /// that honours neither of the first two.
    /// </summary>
    internal const string CacheControlValue = "no-store, no-cache, must-revalidate";

    /// <summary>The HTTP/1.0 companion directive, sent only where the one above is.</summary>
    internal const string PragmaValue = "no-cache";

    /// <summary>The only value this header has.</summary>
    internal const string ContentTypeOptionsValue = "nosniff";

    /// <summary>
    /// One year, in seconds, with neither <c>includeSubDomains</c> nor <c>preload</c> - see the banner.
    /// </summary>
    internal const string StrictTransportSecurityValue = "max-age=31536000";

    /// <summary>Installs the headers for every response this application writes.</summary>
    /// <param name="app">The application being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// REGISTERED AS A RESPONSE-STARTING CALLBACK RATHER THAN WRITTEN INLINE, AND THAT IS THE LOAD-BEARING
    /// DETAIL. Headers can only be set before the response starts, so a middleware that wrote them on the
    /// way in would be overwritten by a handler that sets its own status and headers later, and a
    /// middleware that wrote them on the way out would be too late for a response that had already begun
    /// streaming. A starting callback runs at exactly the right moment for every response - a handler's
    /// own, a problem document written by the exception handler, and a bodiless challenge produced by the
    /// authorization middleware alike.
    /// </remarks>
    internal static void Use(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        _ = app.Use(static (context, next) =>
        {
            context.Response.OnStarting(
                static state =>
                {
                    Apply((HttpResponse)state);

                    return Task.CompletedTask;
                },
                context.Response);

            return next(context);
        });
    }

    /// <summary>Applies the headers to one response.</summary>
    /// <param name="response">The response about to start.</param>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// EVERY HEADER IS SET ONLY WHEN ABSENT, so a route that made its own decision keeps it. That is what
    /// makes this middleware a floor rather than a ceiling.
    /// </para>
    /// <para>
    /// <c>internal</c> so the sibling test project can drive it directly against a bare response, which is
    /// what makes each arm - including the HTTPS condition and the already-set case - provable without
    /// booting a host (constraint C-H).
    /// </para>
    /// </remarks>
    internal static void Apply(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        IHeaderDictionary headers = response.Headers;

        if (StringValues.IsNullOrEmpty(headers.XContentTypeOptions))
        {
            headers.XContentTypeOptions = ContentTypeOptionsValue;
        }

        if (StringValues.IsNullOrEmpty(headers.CacheControl))
        {
            headers.CacheControl = CacheControlValue;
            headers.Pragma = PragmaValue;
        }

        // RFC 6797 section 7.2 - never asserted over an insecure transport.
        if (response.HttpContext.Request.IsHttps
            && StringValues.IsNullOrEmpty(headers.StrictTransportSecurity))
        {
            headers.StrictTransportSecurity = StrictTransportSecurityValue;
        }
    }
}
