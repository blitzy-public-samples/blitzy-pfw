// ==================================================================================================
//  CaptureFailure - the one exception type this tool raises, so a failure is a message rather than a
//  stack trace
// ==================================================================================================

namespace PowerFramework.Characterization.Capture;

/// <summary>
/// A capture could not be taken, or could not be taken honestly.
/// </summary>
/// <remarks>
/// EVERY MESSAGE IS WRITTEN FOR AN OPERATOR AND NAMES WHAT TO DO. A capture driver that fails
/// obscurely gets run once and abandoned, and the store goes back to holding intentions. The entry
/// point prints the message and returns a non-zero exit code without a stack trace, because the
/// interesting information is always the message.
/// </remarks>
/// <param name="message">The refusal or failure, addressed to whoever ran the tool.</param>
internal sealed class CaptureFailure(string message) : Exception(message);
