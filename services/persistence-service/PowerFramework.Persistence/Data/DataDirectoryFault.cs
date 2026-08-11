// ==================================================================================================
//  DataDirectoryFault - THE ONE DESCRIPTION OF A STORAGE-DIRECTORY FAILURE, AND WHY IT WITHHOLDS THE
//  PATH
//  ------------------------------------------------------------------------------------------------
//  WHAT THIS TYPE EXISTS FOR. Two places in this service can fail to reach the configured storage
//  directory - the startup gate's writability probe in `Program.cs`, and the connection factory's
//  create-if-absent step - and both used to describe the failure in their own words while QUOTING THE
//  CONFIGURED PATH and NAMING NO CONFIGURATION KEY. Measured on a running host, one such failure put
//  the path into the startup output FOUR times and the key ZERO times, so an operator was handed the
//  value they already knew and not the setting they had to change.
//
//  WHY THE PATH IS WITHHELD, GIVEN THAT A DIRECTORY PATH IS NOT ITSELF A CREDENTIAL. The rest of this
//  estate had already settled the question and this site was the outlier:
//
//    * `Program.cs`, InternalTlsTrust - "The path is not reproduced here, because a startup record must
//      not publish a container's secret mount layout ... the message names the configuration key
//      instead - the same rule Configuration/PersistenceOptions.cs applies to its own validation
//      messages."
//    * DataServices' MutualTls group - "NO PATH IS EVER ECHOED INTO A MESSAGE. A path is not itself a
//      credential, but it names where one is mounted, and a startup log is exactly the wrong place to
//      publish that."
//    * Security's caller-authority loader - the same sentence again.
//
//  A storage mount is the same class of fact: it tells a reader where this deployment's data lives, and
//  the startup channel is read by more parties than the operator who set it. The KEY is what makes the
//  fault actionable, and the key is the one thing the old messages omitted.
//
//  AND THE ATTACHED EXCEPTION IS ITSELF A DISCLOSURE CHANNEL, WHICH IS THE PART THAT MAKES REWORDING
//  INSUFFICIENT. `IOException` and `UnauthorizedAccessException` from the file APIs carry the path in
//  their OWN Message, so a message that carefully withholds the path and then attaches the cause
//  publishes it anyway - which is precisely how one omission became four occurrences. So the cause is
//  described BY TYPE ALONE and not attached, following the same rule Gateway's system-error handler
//  applies to a fault it cannot safely render.
//
//  THE FAILURE CLASS IS ESTABLISHED RATHER THAN GUESSED, because "not writable" is the wrong sentence
//  for three of the four ways this can fail and would send an operator to check permissions on a path
//  that is occupied by a file or whose parent does not exist at all.
// ==================================================================================================

namespace PowerFramework.Persistence.Data
{
    /// <summary>
    /// Why the configured storage directory could not be reached.
    /// </summary>
    /// <remarks>
    /// FOUR NAMED CLASSES AND ONE FALLBACK, chosen because each sends an operator somewhere different.
    /// A single "not writable" sentence - which is what both call sites used to emit for every one of
    /// them - is correct for exactly one and misleading for the rest.
    /// </remarks>
    internal enum DataDirectoryFaultKind
    {
        /// <summary>The classification could not be established from the file system.</summary>
        Unknown = 0,

        /// <summary>A file occupies the configured path, so no directory can exist there.</summary>
        PathIsAFile = 1,

        /// <summary>The containing directory does not exist, so the leaf cannot be created in it.</summary>
        ParentMissing = 2,

        /// <summary>The directory exists but this process cannot write inside it.</summary>
        NotWritable = 3,

        /// <summary>
        /// The directory is absent and its parent exists but refused its creation.
        /// </summary>
        CannotCreate = 4,
    }

    /// <summary>
    /// Builds the operator-facing description of a storage-directory failure, naming the configuration
    /// key and never the configured path.
    /// </summary>
    internal static class DataDirectoryFault
    {
        /// <summary>
        /// The configuration key every description names.
        /// </summary>
        /// <remarks>
        /// SPELLED ONCE HERE so both call sites name the identical key. The section and property are the
        /// ones <c>Configuration/PersistenceOptions.cs</c> binds; composing the string from those
        /// symbols would be preferable, but this file deliberately takes no dependency on the options
        /// types so that it can be called from the connection factory's construction path, where the
        /// options graph is not available. The pairing is asserted by
        /// <c>PowerFramework.Persistence.Tests</c> rather than left to inspection.
        /// </remarks>
        internal const string ConfigurationKey = "Sqlite:DataDirectory";

        /// <summary>
        /// Establishes why the directory could not be reached.
        /// </summary>
        /// <param name="directory">The configured path. Inspected, never returned or rendered.</param>
        /// <returns>The class of failure, or <see cref="DataDirectoryFaultKind.Unknown"/>.</returns>
        /// <remarks>
        /// <para>
        /// EVERY PROBE HERE IS TOTAL. <see cref="File.Exists(string)"/> and
        /// <see cref="Directory.Exists(string)"/> answer <see langword="false"/> rather than throwing on
        /// an unreadable or malformed path, and the one call that can throw -
        /// <see cref="Path.GetDirectoryName(string)"/> on a path containing an invalid character - is
        /// guarded. A classifier that could itself fail while describing a failure would replace a poor
        /// diagnostic with none.
        /// </para>
        /// <para>
        /// THE ORDER IS THE DIAGNOSTIC ORDER, not an arbitrary one. A file occupying the path is checked
        /// first because it makes every later question meaningless; a missing parent second because it
        /// explains a creation failure that has nothing to do with permissions; and only then is the
        /// answer about writability, which is the one an operator will act on by changing ownership.
        /// </para>
        /// </remarks>
        internal static DataDirectoryFaultKind Classify(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return DataDirectoryFaultKind.Unknown;
            }

            string trimmed = directory.Trim();

            if (File.Exists(trimmed))
            {
                return DataDirectoryFaultKind.PathIsAFile;
            }

            bool exists = Directory.Exists(trimmed);

            if (exists)
            {
                // It is there and the caller's write failed, so the write is the fault.
                return DataDirectoryFaultKind.NotWritable;
            }

            string? parent;

            try
            {
                parent = Path.GetDirectoryName(trimmed);
            }
            catch (ArgumentException)
            {
                // A malformed path cannot be classified further, and saying so is better than asserting
                // a class that happens to be the last branch reached.
                return DataDirectoryFaultKind.Unknown;
            }

            if (string.IsNullOrEmpty(parent))
            {
                // A rooted or relative leaf with no separator: there is no parent to blame, so the
                // creation attempt itself is the fault.
                return DataDirectoryFaultKind.CannotCreate;
            }

            if (File.Exists(parent))
            {
                // A file stands where the containing directory should be. Reported as the parent's
                // problem rather than as PathIsAFile, which would point an operator at the wrong
                // component of the path.
                return DataDirectoryFaultKind.ParentMissing;
            }

            return Directory.Exists(parent)
                ? DataDirectoryFaultKind.CannotCreate
                : DataDirectoryFaultKind.ParentMissing;
        }

        /// <summary>
        /// Builds the operator-facing description of a storage-directory failure.
        /// </summary>
        /// <param name="directory">The configured path. Classified, never rendered.</param>
        /// <param name="cause">The failure the file system reported. Named by TYPE only.</param>
        /// <returns>A description that names the configuration key and no configured value.</returns>
        /// <remarks>
        /// <para>
        /// THE RETURNED TEXT CARRIES NO PATH, BY CONSTRUCTION RATHER THAN BY CARE. Neither
        /// <paramref name="directory"/> nor <c>cause.Message</c> is interpolated anywhere below - the
        /// former is only ever passed to <see cref="Classify"/>, and only the exception's TYPE NAME is
        /// used. That is what makes the guarantee checkable by reading this one method instead of by
        /// auditing every call site.
        /// </para>
        /// <para>
        /// THE CAUSE IS NAMED BY TYPE BECAUSE ITS MESSAGE IS THE LEAK. <see cref="IOException"/> and
        /// <see cref="UnauthorizedAccessException"/> from the file APIs quote the path they failed on, so
        /// attaching the cause - or interpolating its message - reintroduces exactly what the rest of the
        /// sentence withholds. The type plus the established class is what an operator can act on, and it
        /// is what a log pipeline can group by.
        /// </para>
        /// </remarks>
        internal static string Describe(string directory, Exception? cause)
        {
            DataDirectoryFaultKind kind = Classify(directory);

            string diagnosis = kind switch
            {
                DataDirectoryFaultKind.PathIsAFile =>
                    "a FILE occupies the configured path, so no directory can exist there. Point the "
                    + "setting at a directory, or remove the file that stands in its place",

                DataDirectoryFaultKind.ParentMissing =>
                    "the CONTAINING DIRECTORY does not exist, so the configured directory cannot be "
                    + "created inside it. This is usually a volume that was not mounted, or a mount "
                    + "point that differs from the configured path",

                DataDirectoryFaultKind.NotWritable =>
                    "the directory EXISTS but this process cannot write inside it. The container image "
                    + "runs as a NON-ROOT user, so the usual cause is a mounted volume owned by another "
                    + "user; correct the volume's ownership or permissions",

                DataDirectoryFaultKind.CannotCreate =>
                    "the directory is ABSENT and its parent refused to create it. The container image "
                    + "runs as a NON-ROOT user, so the usual cause is a parent directory that user "
                    + "cannot write; correct the parent's ownership or permissions",

                _ =>
                    "the file system refused the operation and the reason could not be established from "
                    + "the path itself. Check that the setting names a directory this process can write",
            };

            return string.Concat(
                "The storage directory configured at '",
                ConfigurationKey,
                "' cannot be used: ",
                diagnosis,
                ". This service has no other storage, so the process is terminating rather than "
                + "starting and failing every retrieval, update and command "
                + "[ws_objects/pfw.pbl.src/pfw.sra:L111-L144]. Nothing here removes or replaces existing "
                + "contents - only a missing directory is created. The file system reported ",
                cause?.GetType().Name ?? "no exception",
                ". THE CONFIGURED PATH IS DELIBERATELY NOT REPRODUCED, and neither is the reported "
                + "message, because both name where this deployment's data is mounted and a startup "
                + "record must not publish that; the configuration key above is what identifies the "
                + "setting to change.");
        }
    }
}
