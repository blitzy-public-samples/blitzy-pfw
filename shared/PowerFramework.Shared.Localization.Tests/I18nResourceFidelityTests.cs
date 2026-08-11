// ==================================================================================================
//  I18nResourceFidelityTests.cs - THE ORACLE FIXTURE, ASSERTED AT THE BYTE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT           pfw.i18n.xml, as copied into the test working directory by the Content link at
//                    PowerFramework.Shared.Localization.csproj:256-261 - the LIBRARY project, which
//                    declares the item once and flows it here down the ProjectReference edge
//  ORACLE            pfw.i18n.xml at the repository root - read-only legacy tree (C-C)
//
//  WHY THIS FILE EXISTS
//  ------------------------------------------------------------------------------------------------
//  Both project files state that the copy must stay byte identical to the original and enumerate the
//  properties that make it so - the library at PowerFramework.Shared.Localization.csproj:241-243, and
//  this test project restating them at PowerFramework.Shared.Localization.Tests.csproj:239-243: 6824
//  bytes, no byte order mark, no
//  XML declaration, UTF-8 Chinese, LF-only line endings, and a final closing element with no trailing
//  newline. Those were true when written and were STILL only a comment - nothing executed them, so a
//  build step, an editor, a Git configuration or a copy target that normalised the file would have left
//  the documentation reading correctly while the bytes underneath had changed. Documentation that
//  cannot fail is documentation that drifts, and the fixture it describes is the behavioural oracle for
//  every translation assertion in this project.
//
//  WHAT IS ASSERTED, AND WHY EACH ONE IS A REAL FAILURE MODE RATHER THAN A CURIOSITY
//  ------------------------------------------------------------------------------------------------
//      6824 bytes exactly     any transform at all changes the length; this is the cheapest tripwire
//      151 LF, 0 CR           a text=auto checkout or an editor save on Windows would rewrite every
//                             line ending to CRLF, taking the length to 6975 and silently changing
//                             every multi-line string this table can produce
//      final byte '>' (0x3E)  a trailing-newline fixup is the single most common automated edit; it
//                             would make the last byte 0x0A
//      no byte order mark     the reader decodes with the framework's detection, so a BOM would be
//                             tolerated here and NOT by the legacy PowerBuilder loader - the copy
//                             would still pass every lookup test while having diverged from the oracle
//      no XML declaration     the document opens directly with the pfw element; an added declaration
//                             is a content edit to a read-only file
//      276 tabs               the indentation is tabs, so a spaces-conversion is caught too
//      byte-identical to root the strongest statement available: whatever the numbers say, the copy and
//                             the oracle are the same bytes
//
//  HOW THIS INTERACTS WITH A LEGITIMATE UPSTREAM UPDATE
//  ------------------------------------------------------------------------------------------------
//  The csproj notes that PreserveNewest refreshes the copy if the oracle is ever updated upstream. If
//  that happens the counted facts below fail, and that is the DESIGNED outcome: the numbers live in
//  both the csproj comment and this file, so an upstream change forces both to be re-measured together
//  instead of leaving the comment stale. The byte-identity fact, by contrast, keeps passing - it is the
//  drift-proof half, and it is what proves the build is not the thing that changed the file.
//
//  RULES POSITION
//  review_rules returns "No user rules provided.". Constraints cited inline: C-C (the legacy tree is
//  read-only and is the oracle), C-K (document boundary decisions).
// ==================================================================================================

using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Byte-level fidelity tests for the <c>pfw.i18n.xml</c> oracle fixture.
/// </summary>
public class I18nResourceFidelityTests
{
    /// <summary>
    /// The exact byte length of the oracle, measured from the repository root file.
    /// </summary>
    private const int OracleByteCount = 6824;

    /// <summary>
    /// The exact number of line-feed bytes (0x0A) in the oracle.
    /// </summary>
    private const int OracleLineFeedCount = 151;

    /// <summary>
    /// The exact number of tab bytes (0x09) in the oracle - the indentation character.
    /// </summary>
    private const int OracleTabCount = 276;

    /// <summary>
    /// The number of lines the content splits into: one more than the line-feed count, because the
    /// document does not end with a separator.
    /// </summary>
    private const int OracleSplitLineCount = OracleLineFeedCount + 1;

    /// <summary>
    /// The message a fixture-guard failure carries.
    /// </summary>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory. Restore the Content item in the LIBRARY "
            + "project, PowerFramework.Shared.Localization.csproj:256-261 - it is declared there once and "
            + "flows here down the ProjectReference edge. Without it this suite cannot "
            + "assert anything and every lookup test in this project silently misses.";

    /// <summary>
    /// Reads the copied fixture's bytes, having first asserted it is there.
    /// </summary>
    /// <returns>The raw bytes of the copy in the working directory.</returns>
    private static byte[] ReadCopiedFixtureBytes()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
        return File.ReadAllBytes(I18nResourceReader.DefaultResourceFileName);
    }

    /// <summary>
    /// Walks up from the test assembly's directory looking for the repository root - the directory
    /// holding both the root solution and the oracle resource.
    /// </summary>
    /// <param name="oraclePath">The located oracle path, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the oracle was located.</returns>
    /// <remarks>
    /// Deliberately a SEARCH rather than a fixed number of parent hops. The output directory depth
    /// depends on configuration and target framework, and a hard-coded <c>../../../../..</c> would turn
    /// a layout change into a confusing failure in an unrelated test. Anchoring on
    /// <c>PowerFramework.slnx</c> alongside the resource identifies the root unambiguously and fails
    /// closed - if the root cannot be found the caller skips only the comparison, keeping every counted
    /// fact live.
    /// </remarks>
    private static bool TryLocateOracle(out string? oraclePath)
    {
        // STARTS AT THE EMBEDDED REPOSITORY ROOT WHEN THE BUILD SUPPLIED ONE, so this locator works when
        // the test output sits outside the checkout - `dotnet test --artifacts-path` - where no ancestor
        // of the output directory carries the marker below. The walk itself is unchanged and still
        // verifies that marker, so an absent or stale value simply falls back to the previous start.
        // See TestRepositoryRoot.
        DirectoryInfo? directory = new(TestRepositoryRoot.SearchStart);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, I18nResourceReader.DefaultResourceFileName);

            if (File.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "PowerFramework.slnx")))
            {
                oraclePath = candidate;
                return true;
            }

            directory = directory.Parent;
        }

        oraclePath = null;
        return false;
    }

    // ==============================================================================================
    //  1. THE COUNTED FACTS  (DP-4)
    // ==============================================================================================

    /// <summary>
    /// The fixture is exactly <see cref="OracleByteCount"/> bytes.
    /// </summary>
    /// <remarks>
    /// The cheapest tripwire available: every transform anyone might apply - newline conversion, BOM
    /// insertion, re-encoding, reformatting, a trailing newline - changes the length. It is asserted
    /// first so that a failure elsewhere in this file can be read as "the content changed in a
    /// length-preserving way", which is a much narrower diagnosis.
    /// </remarks>
    [Fact]
    public void TheFixtureIsExactlySixThousandEightHundredAndTwentyFourBytes()
    {
        byte[] bytes = ReadCopiedFixtureBytes();

        Assert.Equal(OracleByteCount, bytes.Length);
    }

    /// <summary>
    /// The fixture contains exactly 151 line feeds and NOT ONE carriage return.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The CR count is the assertion that matters. A <c>text=auto</c> or <c>eol=crlf</c> rule, a Windows
    /// editor save, or a copy target with newline handling would rewrite all 151 separators to CRLF -
    /// taking the length to 6975 and, more importantly, changing every value this table can return that
    /// spans a line. Nothing in the reader normalises line endings, so such a change reaches the
    /// caller.
    /// </para>
    /// <para>
    /// The repository <c>.gitattributes</c> declares only <c>linguist-language</c> rules - no
    /// <c>eol</c> and no <c>text=auto</c> - which is why a plain checkout preserves the bytes. That is
    /// the mechanism; this is the verification of it.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixtureUsesLineFeedOnlyWithNoCarriageReturnAnywhere()
    {
        byte[] bytes = ReadCopiedFixtureBytes();

        Assert.Equal(OracleLineFeedCount, bytes.Count(value => value == (byte)'\n'));
        Assert.Equal(0, bytes.Count(value => value == (byte)'\r'));

        // Stated a second way, on the decoded text, so a failure names the offending sequence rather
        // than only a count.
        string text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\r", text, System.StringComparison.Ordinal);
        Assert.Equal(OracleSplitLineCount, text.Split('\n').Length);
    }

    /// <summary>
    /// The final byte is <c>'&gt;'</c>, so the document ends with its closing element and has NO
    /// trailing newline.
    /// </summary>
    /// <remarks>
    /// Adding a trailing newline is the single most common automated edit a text file suffers - most
    /// editors and many lint tools do it on save. It would make the last byte 0x0A. The fixture is
    /// inside the read-only legacy tree, so this test is the thing that notices.
    /// </remarks>
    [Fact]
    public void TheFixtureEndsWithItsClosingElementAndNoTrailingNewline()
    {
        byte[] bytes = ReadCopiedFixtureBytes();

        Assert.Equal((byte)'>', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^1]);

        string text = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("</pfw>", text, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture carries no byte order mark and no XML declaration: it opens directly with the root
    /// element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A BOM is the failure mode that would NOT be caught by the lookup tests, which is why it is
    /// asserted here rather than left to them. The framework XML reader detects and skips one, so every
    /// translation would keep resolving; the legacy PowerBuilder loader is the consumer that would
    /// differ, and it is not runnable in this environment. So the byte assertion is the only available
    /// statement about it.
    /// </para>
    /// <para>
    /// The absent XML declaration is a content fact about the oracle rather than a requirement of the
    /// format - the document is well-formed without one - and adding it would be an edit to a read-only
    /// file.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFixtureHasNoByteOrderMarkAndNoXmlDeclaration()
    {
        byte[] bytes = ReadCopiedFixtureBytes();

        Assert.False(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "The fixture has acquired a UTF-8 byte order mark, so it is no longer byte identical to the "
                + "read-only oracle even though the framework reader would still parse it.");

        Assert.Equal((byte)'<', bytes[0]);
        Assert.Equal((byte)'p', bytes[1]);
        Assert.Equal((byte)'f', bytes[2]);
        Assert.Equal((byte)'w', bytes[3]);
        Assert.Equal((byte)'>', bytes[4]);

        string text = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("<?xml", text, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The fixture is indented with tabs - 276 of them - and its Chinese decodes as UTF-8.
    /// </summary>
    /// <remarks>
    /// The tab count catches a spaces-conversion, which is length-changing but in a way the byte-count
    /// test alone would not attribute. The encoding half is asserted by decoding UTF-8 STRICTLY: a
    /// re-encode to a single-byte code page would leave the length plausible while turning every
    /// Chinese key into replacement characters, and strict decoding throws instead of silently
    /// substituting them.
    /// </remarks>
    [Fact]
    public void TheFixtureIsTabIndentedAndItsChineseIsValidUtf8()
    {
        byte[] bytes = ReadCopiedFixtureBytes();

        Assert.Equal(OracleTabCount, bytes.Count(value => value == (byte)'\t'));

        UTF8Encoding strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        string text = strict.GetString(bytes);

        // Two keys that exist only as multi-byte sequences, one of them a preserved mistranslation's key.
        Assert.Contains("最小化", text, System.StringComparison.Ordinal);
        Assert.Contains("修改数据被拒绝", text, System.StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  2. THE DRIFT-PROOF FACT
    // ==============================================================================================

    /// <summary>
    /// The copy in the working directory is byte-for-byte identical to the oracle at the repository
    /// root.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The strongest statement available, and the one that survives a legitimate upstream update to the
    /// oracle. The counted facts above pin what the file IS; this pins that the BUILD did not change
    /// it - which is the actual guarantee the csproj comment claims when it says MSBuild only ever
    /// copies the file.
    /// </para>
    /// <para>
    /// Skipped rather than failed when the root cannot be located, because a packaged or relocated test
    /// run has no repository to compare against and the counted facts remain fully live there. The skip
    /// is explicit so it cannot be mistaken for a pass.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheCopyIsByteIdenticalToTheOracleAtTheRepositoryRoot()
    {
        byte[] copied = ReadCopiedFixtureBytes();

        Assert.SkipUnless(
            TryLocateOracle(out string? oraclePath),
            "The repository root was not found above the test assembly, so there is no oracle to "
                + "compare the copy against in this run.");

        byte[] oracle = File.ReadAllBytes(oraclePath!);

        Assert.Equal(oracle.Length, copied.Length);
        Assert.True(
            oracle.AsSpan().SequenceEqual(copied),
            $"The copy in the working directory differs from the oracle at {oraclePath}. MSBuild must "
                + "only ever COPY that file - a transform has been introduced, which breaks the "
                + "read-only guarantee the test project documents.");
    }

    // ==============================================================================================
    //  3. THE FACTS THE READER DEPENDS ON
    // ==============================================================================================

    /// <summary>
    /// The reader resolves the fixture by its bare relative name and every category section answers,
    /// so the byte facts above are describing the file the reader actually reads.
    /// </summary>
    /// <remarks>
    /// Without this, the assertions above could all hold against a file the reader never opens - the
    /// two halves are only connected by the reader's default constructor using the same bare name the
    /// legacy providers use [n_cst_i18n_en.sru:L159]. One lookup per element is enough: the point is
    /// the identity of the file, not the completeness of the table, which the reader's own suite covers.
    /// </remarks>
    [Fact]
    public void TheReaderReadsThisExactFile()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);

        I18nResourceReader reader = new();

        Assert.Equal("Close", reader.Lookup("en", "window", "关闭"));
        Assert.Equal("Dock", reader.Lookup("en", "tabcontrol", "固定"));
        Assert.Equal("Expand", reader.Lookup("en", "ribbonbar", "展开功能区"));
        Assert.Equal("OK", reader.Lookup("en", "msgbox", "确定"));
        Assert.Equal("Invalid value", reader.Lookup("en", "dwsvc", "无效的值"));
        Assert.Equal(
            "Double-click to collapse the left panel",
            reader.Lookup("en", "splitcontainer", "双击折叠左侧面板"));
    }
}
