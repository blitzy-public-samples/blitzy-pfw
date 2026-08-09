// ==================================================================================================
//  GlobalScopeCaptureHelper.cs - A CAPTURE SITE WITH NO NAMESPACE
//  ------------------------------------------------------------------------------------------------
//  WHY THIS TYPE IS IN ITS OWN FILE, AND WHY IT HAS NO NAMESPACE
//  ------------------------------------------------------------------------------------------------
//  StackTraceProvider renders a frame's scope by walking the declaring type's enclosing chain and then
//  prefixing the namespace - unless there ISN'T one, in which case the chain is returned bare. That
//  branch is reachable only from a type declared in the GLOBAL namespace, and it is a real scenario
//  rather than a theoretical one: a top-level program class, a type in an assembly authored without a
//  namespace, and much legacy interop code all land there.
//
//  A file-scoped namespace declaration must precede every type declaration in its file, so this helper
//  cannot live alongside the suite that uses it - hence a file of its own. The alternative, a
//  brace-delimited namespace with the helper outside it, would put a namespace-less type in the middle
//  of a namespaced file and read as an accident.
//
//  This type declares no test. It exists so StackTraceProviderTests can assert what a namespace-less
//  frame renders as, which is the one branch of scope rendering that no namespaced call site can reach.
// ==================================================================================================

using System.Runtime.CompilerServices;
using PowerFramework.Shared.Diagnostics;

/// <summary>
/// A capture site declared in the global namespace, used to exercise the namespace-less branch of frame
/// scope rendering.
/// </summary>
internal static class GlobalScopeCaptureHelper
{
    /// <summary>
    /// Captures the call stack from a method whose declaring type has no namespace.
    /// </summary>
    /// <returns>The captured frames, outermost first.</returns>
    /// <remarks>
    /// Marked <see cref="MethodImplOptions.NoInlining"/> for the same reason every capture helper in this
    /// project is: if the runtime inlined it into its caller, the frame the assertion identifies would
    /// not exist and the test would fail only in a release build.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string[] Capture()
    {
        StackTraceProvider.StackTrace(out string[] frames);
        return frames;
    }
}
