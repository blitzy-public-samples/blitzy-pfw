// ==============================================================================================
//  I18nResourceReaderTests - the suite that proves the XML SUBSTITUTION is correct, and that it
//  STAYED a substitution.
//  --------------------------------------------------------------------------------------------
//  UNIT UNDER TEST  PowerFramework.Shared.Localization.I18nResourceReader, in
//                   shared/PowerFramework.Shared.Localization/I18nResourceReader.cs
//
//  THE DECISION THIS SUITE EXISTS TO DOCUMENT AND TO POLICE (constraint C-K)
//  --------------------------------------------------------------------------------------------
//  The reader is a SUBSTITUTION, not a port. The two concrete legacy locale providers read the
//  translation table with the Documents XML object family, and this phase DEFERS Documents:
//
//      ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru
//          L13        privatewrite n_xmldoc _doc
//          L51        sTo = _doc.Query(Sprintf(
//                         "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)", sCat, text))
//                         .GetValueString( )
//          L52        if sTo <> "" then            <-- THE GATE. See "THE MISS CONTRACT" below.
//          L158-L159  event constructor; _doc = Create n_xmldoc
//                     _doc.LoadFile("pfw.i18n.xml")
//
//      ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru
//          L13        privatewrite n_xmldoc _doc                  (identical declaration)
//          L31        n_xmlqueryresult xqs                        (declared, never used - dead)
//          L52        ...string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)...
//          L53        if sTo <> "" then                           (the same gate)
//          L62-L63    identical constructor load
//
//  AAP SECTION 0.2.1.3, CORRECTION 6 is the ruling, and it is quoted in substance because the
//  whole shape of this file follows from it:
//
//      "Localization's XML dependency is substituted, not imported. The concrete providers read
//       the translation table with the deferred Documents XML parser ... Resolution: substitute
//       System.Xml.Linq.XDocument.Load plus XPathSelectElements. Localization therefore acquires
//       NO Documents coupling, and the XML object family stays deferred."
//
//  So the substitution is, term for term:
//
//      n_xmldoc            ->  System.Xml.Linq.XDocument
//      n_xmldoc.LoadFile   ->  System.Xml.Linq.XDocument.Load
//      n_xmldoc.Query      ->  a LINQ to XML traversal of that same XDocument. Correction 6 names
//                              XPathSelectElements, and that was the mechanism until the reader's
//                              interpolated expression was found to be an injection sink rather
//                              than a faithfully reproduced defect - a balanced payload SELECTED
//                              an entry the caller never asked for. The framework XML API is
//                              still the substitute, which is what Correction 6 requires; only
//                              the query spelling changed, and it changed nothing observable
//                              (see CraftedArgumentsCannotSelectAnUnaskedForTranslation and
//                              EveryEntryInTheResourceResolvesToItsRecordedTranslation).
//      GetValueString      ->  reading the "to" attribute's Value
//      n_xmlqueryresult    ->  NOTHING, deliberately. Its only in-scope declaration
//                              (n_cst_i18n_cht.sru:L31) is dead code with no observable
//                              behaviour, so porting it would introduce a forbidden type in
//                              order to reproduce nothing.
//
//  WHY THIS FILE CARRIES AN ASSEMBLY-REFERENCE ASSERTION (REGION 7)
//  --------------------------------------------------------------------------------------------
//  Everything above is prose, and prose does not fail a build. Constraint C-D forbids
//  implementing the four deferred services "even partially, even to stub them out", and Documents
//  owns the eleven-object XML family (AAP 0.4.1). A future edit that reached for an XML, XPath,
//  JSON, ZIP, barcode or pinyin PACKAGE would be the first thread of exactly that coupling, and
//  no behavioural test in this file would notice: the reader would keep returning "Restore" for
//  "\u8FD8\u539F" whichever library parsed the document.
//
//  Region 7 is therefore the only assertion in this suite that is STRUCTURAL rather than
//  behavioural. It enumerates the referenced assemblies of the assembly under test and pins both
//  halves of Correction 6:
//
//      the POSITIVE half - the framework XML facades ARE referenced, so the substitution is
//                          demonstrably the mechanism actually in use;
//      the NEGATIVE half - nothing outside the framework and the shared kernel is referenced,
//                          and specifically no Documents-capability and no logging assembly.
//
//  That turns "Localization acquires no Documents coupling" from a claim in a plan into a
//  condition the build checks. It is the reason this suite exists in the shape it does, and it
//  must not be deleted or weakened; see the comments in Region 7 for what a failure means.
//
//  THE MISS CONTRACT, WHICH IS THE ONE BEHAVIOUR MOST AT RISK OF BEING "IMPROVED"
//  --------------------------------------------------------------------------------------------
//  A lookup that finds nothing returns THE EMPTY STRING. It does not throw, it does not return
//  null, and it does not fall back to anything. That is not defensive programming and it is not
//  an oversight to be tidied into a KeyNotFoundException: it is the legacy contract, and the
//  legacy contract is load-bearing. The legacy XPath is wrapped in the XPath string() function,
//  which yields "" for an empty node set, and the provider then branches on exactly that:
//
//      n_cst_i18n_en.sru:L52-L55     if sTo <> "" then / text = sTo / return 1 / end if
//      n_cst_i18n_cht.sru:L53-L56    the same four lines
//
//  A miss is the ORDINARY path, not an exceptional one - every non-framework source, every
//  unmapped category and every untranslated string reaches it - and it is what makes the facade
//  pass the source text through silently. Turning any miss into an exception (constraint C-B)
//  would convert a designed fall-through into a failure at every one of those call sites. No test
//  in this file asserts an exception type for a lookup miss. The only exceptions asserted
//  anywhere here are the CONSTRUCTOR's argument guards, which are caller-contract violations that
//  neither legacy provider can reach, and Region 4 says so where it asserts them.
//
//  THE ONLY SUITE IN THIS FOLDER THAT TOUCHES THE FILE SYSTEM, AND HOW IT BEHAVES THERE
//  --------------------------------------------------------------------------------------------
//  Constraint C-C: the legacy tree is READ ONLY and is the behavioural oracle. pfw.i18n.xml at
//  the repository root is inside that region. This suite therefore:
//
//      * NEVER writes, moves, renames, reformats, re-encodes or "corrects" it. Every access is a
//        read, through a FileStream opened FileMode.Open / FileAccess.Read - the same shape the
//        reader itself uses - so read-only-ness is structural here, not a matter of review.
//      * NEVER makes a second checked-in copy of it. A duplicate could drift from the oracle, and
//        the two documented mistranslations it carries must survive verbatim.
//      * reaches it ONLY by the BARE RELATIVE FILENAME "pfw.i18n.xml". No path arithmetic, no
//        walk up from AppContext.BaseDirectory to the repository root, no absolute path. That is
//        not a convenience: the relative name IS the behaviour under test, because
//        n_cst_i18n_en.sru:L159 and n_cst_i18n_cht.sru:L63 both load by bare relative name. The
//        csproj supplies the file at that name by LINKING the single real resource into the build
//        output (Content Include="../../pfw.i18n.xml" Link="pfw.i18n.xml"), and the xunit.v3 test
//        host's working directory IS that output directory - both facts measured, not assumed.
//      * authors every negative and edge shape it needs as a document written to a UNIQUE
//        TEMPORARY DIRECTORY that the test creates and then deletes in a finally block. Nothing
//        is left behind and the real resource is never involved.
//      * NEVER mutates Environment.CurrentDirectory. xunit runs test classes in parallel and the
//        working directory is process-wide, so mutating it would race. Reading it is safe and is
//        all Region 5 does; the synthetic-shape helpers pass an absolute temporary path to the
//        reader instead, which needs no working-directory change at all.
//
//  A note on why the fixture GUARD in Region 0 matters more than it looks. If the Content link
//  were ever dropped from the csproj, the resource would be absent at run time, the reader would
//  degrade to "every lookup misses" exactly as designed, and a suite full of miss assertions
//  could pass VACUOUSLY while proving nothing. RequireResourceFixture is what makes that
//  impossible: every test that touches the real resource asserts its presence first, and the
//  failure message names the csproj item to restore.
//
//  MEASURED FACTS ABOUT THE RESOURCE, ALL VERIFIED WHILE WRITING THIS SUITE
//  --------------------------------------------------------------------------------------------
//  PHYSICAL   6,824 bytes. NO byte order mark. NO XML declaration - the document opens directly
//             with the pfw element. UTF-8 encoded Chinese. LF-only line endings (151 LF, 0 CRLF).
//             152 lines, the final closing element unterminated by a newline. The repository
//             .gitattributes declares no text=auto and no eol rule, so no checkout normalization
//             occurs. XDocument.Load decodes it correctly BECAUSE there is neither a declaration
//             nor a mark: XmlReader defaults to UTF-8 in that case. Region 1 pins the two
//             physical properties that decoding depends on, and Region 3 pins the round trip, so
//             an encoding regression cannot slip through as a silent mojibake miss.
//  STRUCTURAL Root pfw. Exactly SIX category element names, each appearing exactly ONCE per
//             language:
//                 window          L2   en    L77  cht
//                 splitcontainer  L15  en    L90  cht
//                 tabcontrol      L29  en    L104 cht
//                 ribbonbar       L32  en    L107 cht
//                 msgbox          L36  en    L111 cht
//                 dwsvc           L51  en    L126 cht
//             126 entries, every one a tr element carrying both a text and a to attribute; no
//             entry omits either and no entry has an empty to value. The only lang values present
//             are en and cht.
//  NO chs     There is no lang="chs" section anywhere in the document, and the Simplified Chinese
//             provider corroborates that from the other side. n_cst_i18n_chs.sru declares NO
//             n_xmldoc field at all - contrast n_cst_i18n_en.sru:L13 and n_cst_i18n_cht.sru:L13 -
//             and its whole translate body is two statements, n_cst_i18n_chs.sru:L25-L26: return 1
//             for the framework's own source, otherwise 0. It claims the translation as handled
//             while leaving the text UNCHANGED, because Simplified Chinese is the base locale and
//             there is nothing to translate it into. So the provider reads no resource, and the
//             resource holds no data for it. Region 2 pins the absence from both directions: as a
//             document fact, and as a reader result in all six categories.
//
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  There are NO user-specified rules for this project. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. That is a FINDING, not
//  latitude - nothing is invented or back-filled here, and the bar is the plan's enterprise
//  baseline (AAP 0.7.2) and its non-rule constraint inventory (AAP 0.7.3) instead.
//
//      C-B  No behaviour improvements. A miss yields the empty string, never an exception and
//           never a fallback, so the providers' "if sTo <> """ gate still decides. The two
//           mistranslations and the brace-bearing entry are asserted to come back UNREPAIRED.
//      C-C  The legacy tree is read only and is the oracle. See the file-system section above.
//      C-D  The deferred services are not implemented, not even partially. Region 7 enforces it
//           structurally. No Documents XML type is referenced, mocked, stubbed or even named as a
//           type anywhere in this file; the legacy names appear only inside comments.
//      C-H  Nullable reference types and warnings-as-errors apply to test projects exactly as
//           they apply to the library. This file contains no #pragma, no analyzer suppression and
//           no unused member. Every XElement and XAttribute navigation is null-checked through an
//           assertion that the compiler's flow analysis understands, rather than silenced. The
//           null-forgiving operator appears EXACTLY ONCE, in Region 4, inside an
//           Assert.Throws<ArgumentNullException> whose whole subject IS passing null to a
//           non-nullable parameter - the only way to express a nullable-oblivious caller, and the
//           same convention the sibling VectorTests.cs suite already uses for its own argument
//           guards. It is never used to silence a nullability diagnostic on a value this file then
//           dereferences. XDocument.Root is the one navigation where that temptation is real,
//           because it is nullable by contract; every reader of it goes through
//           RequireRootElement, which answers the null with Assert.NotNull and hands the compiler's
//           flow analysis a non-null XElement. If a future edit reintroduces `Root!`, this claim
//           becomes false - use the helper instead.
//      C-K  Every technology-specific and boundary-specific decision is documented where it is
//           made: the substitution above, why Region 7 is structural, why the fixture is reached
//           by relative name, why the twelve-row matrix is static, and why the constructor
//           guards are not miss assertions.
//      0.6.7  Prescribed test shape: table-driven parity matrices expressed as theories with
//           member data. Regions 2, 3, 4 and 6 are those matrices. The remaining regions are
//           Facts, because each makes a single indivisible observation.
//
//  NAMING, WHICH IS A BUILD REQUIREMENT HERE RATHER THAN A PREFERENCE
//  --------------------------------------------------------------------------------------------
//  The repository root .editorconfig scopes its naming-analyzer suppressions BY FILE GLOB to the
//  ten implementation files that genuinely carry preserved legacy SCREAMING_SNAKE identifiers. No
//  test file is covered by any of them, and TreatWarningsAsErrors is on. Every identifier
//  declared in this file is therefore conventionally named - PascalCase types and members,
//  camelCase locals - and this file declares no underscore-bearing identifier of any kind.
//
//  Every test here is synchronous. Nothing awaits, so the ambient test cancellation token has no
//  call to be threaded through. Nothing consults a clock, a socket, a culture or any shared
//  mutable state, so the suite is deterministic and reproducible - the one hard prerequisite of
//  the characterization model this refactor is measured by.
// ==============================================================================================

using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;

using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Behavioural and structural tests for <see cref="I18nResourceReader"/>, the managed substitute
/// for the legacy <c>n_xmldoc</c> load and XPath query performed inside <c>n_cst_i18n_en</c> and
/// <c>n_cst_i18n_cht</c>.
/// </summary>
/// <remarks>
/// <para>
/// The type under test resolves by simple name without a using directive: this file's namespace,
/// <c>PowerFramework.Shared.Localization.Tests</c>, is nested inside
/// <c>PowerFramework.Shared.Localization</c>, so simple-name lookup walks up into the enclosing
/// namespace and finds <see cref="I18nResourceReader"/> there. The redundant directive is
/// deliberately omitted rather than added and suppressed.
/// </para>
/// <para>
/// This is the only suite in the folder that touches the file system, and it is deliberately the
/// only one: the reader's single narrow I/O responsibility is the AAP-mandated exception to the
/// shared layer being pure behaviour. See the commentary at the head of this file for the rules it
/// observes there.
/// </para>
/// </remarks>
public sealed class I18nResourceReaderTests
{
    // ==========================================================================================
    // REGION 0 - MEASURED CONSTANTS AND FIXTURE ACCESS
    //
    // Everything in this region is a measured fact about pfw.i18n.xml or a helper that keeps the
    // rest of the file free of path arithmetic. Nothing here asserts on its own.
    // ==========================================================================================

    /// <summary>
    /// The six category element names the resource declares, measured across all 152 lines.
    /// </summary>
    /// <remarks>
    /// Declared in document order, which is also the order the English sections appear:
    /// <c>window</c> (L2), <c>splitcontainer</c> (L15), <c>tabcontrol</c> (L29), <c>ribbonbar</c>
    /// (L32), <c>msgbox</c> (L36), <c>dwsvc</c> (L51). These are the same six names the providers
    /// map their numeric categories onto at <c>n_cst_i18n_en.sru:L34-L47</c>, which is why the set
    /// is closed: a seventh name in the document would be unreachable, and a seventh name in the
    /// provider would be a miss.
    /// <para>
    /// Held as a <see langword="static"/> <see langword="readonly"/> array rather than being spelled
    /// as an array literal at each use site, so that no call passes a freshly allocated constant
    /// array.
    /// </para>
    /// </remarks>
    private static readonly string[] CategoryElementNames =
    [
        "window",
        "splitcontainer",
        "tabcontrol",
        "ribbonbar",
        "msgbox",
        "dwsvc",
    ];

    /// <summary>
    /// The only two language tokens the resource declares, one per provider that reads it.
    /// </summary>
    /// <remarks>
    /// <c>en</c> is <c>n_cst_i18n_en</c> (<c>n_cst_i18n_en.sru:L51</c>) and <c>cht</c> is
    /// <c>n_cst_i18n_cht</c> (<c>n_cst_i18n_cht.sru:L52</c>). There is deliberately no third entry;
    /// see <see cref="AbsentLanguageToken"/>.
    /// </remarks>
    private static readonly string[] ResourceLanguages =
    [
        "en",
        "cht",
    ];

    /// <summary>
    /// The language token that is genuinely absent from the resource, and therefore the most
    /// meaningful negative language row available.
    /// </summary>
    /// <remarks>
    /// Simplified Chinese is the framework's BASE locale, so <c>n_cst_i18n_chs</c> has nothing to
    /// translate: it declares no <c>n_xmldoc</c> field, loads no resource, and its whole translate
    /// body is <c>n_cst_i18n_chs.sru:L25-L26</c> - return 1 for the framework's own source,
    /// otherwise 0 - leaving the text unchanged either way. The resource reflects that by carrying
    /// no <c>lang="chs"</c> section at all, which is why a <c>chs</c> lookup is a REAL miss rather
    /// than an invented one, and therefore the most meaningful negative language row this suite can
    /// use.
    /// </remarks>
    private const string AbsentLanguageToken = "chs";

    /// <summary>The resource's root element name.</summary>
    private const string RootElementName = "pfw";

    /// <summary>The element name of a single translation entry.</summary>
    private const string EntryElementName = "tr";

    /// <summary>The attribute naming the language of a category section.</summary>
    private const string LanguageAttributeName = "lang";

    /// <summary>The attribute carrying the source text of an entry.</summary>
    private const string SourceTextAttributeName = "text";

    /// <summary>The attribute carrying the translated text of an entry.</summary>
    private const string TranslationAttributeName = "to";

    /// <summary>
    /// The measured size of the resource in bytes.
    /// </summary>
    /// <remarks>
    /// A constant rather than a moving target, because the resource is a READ-ONLY behavioural
    /// oracle. The build only ever COPIES it into the output directory, so the copy must stay byte
    /// identical; the sibling csproj states that a size mismatch there means the read-only guarantee
    /// has been broken by a transform introduced into the build - an encoding conversion or a newline
    /// fixup, say. If the assertion on this value ever fails, restore the original bytes rather than
    /// updating the number.
    /// </remarks>
    private const long ExpectedResourceByteCount = 6824;

    /// <summary>
    /// The measured number of translation entries across all twelve category sections.
    /// </summary>
    /// <remarks>Asserted for the same reason as <see cref="ExpectedResourceByteCount"/>.</remarks>
    private const int ExpectedEntryCount = 126;

    /// <summary>
    /// The failure message every real-resource test shows when the fixture is not in the test
    /// host's working directory.
    /// </summary>
    /// <remarks>
    /// Worded to name the exact csproj item to restore, because a missing fixture does NOT fail
    /// loudly on its own: the reader is specified to degrade to "every lookup misses" when the
    /// table is unavailable, so without this guard a suite of miss assertions would pass while
    /// proving nothing at all. That vacuous pass is the worst failure mode this file can have.
    /// </remarks>
    private const string FixtureMissingMessage =
        "The localization resource fixture 'pfw.i18n.xml' is not present in the test host's " +
        "working directory, so this suite cannot verify anything and must not be allowed to pass. " +
        "PowerFramework.Shared.Localization.csproj - the LIBRARY project, at lines 256-261 - supplies " +
        "it by LINKING the single repository-root resource into the build output, from where it flows " +
        "to this project down the ProjectReference edge: " +
        "<Content Include=\"$(MSBuildThisFileDirectory)../../pfw.i18n.xml\" Link=\"pfw.i18n.xml\" " +
        "CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />. " +
        "Restore that item - do not add a second, " +
        "checked-in copy of the resource, which would be free to drift from the read-only " +
        "behavioural oracle.";

    /// <summary>
    /// The encoding synthetic fixtures are written in: UTF-8 with NO byte order mark, matching the
    /// real resource so that a synthetic shape differs from the oracle only in the way the test
    /// intends.
    /// </summary>
    private static readonly UTF8Encoding SyntheticResourceEncoding =
        new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Asserts that the resource fixture is reachable by its BARE RELATIVE NAME and returns the
    /// name for the caller to hand straight to the reader or to a read-only stream.
    /// </summary>
    /// <returns>
    /// <see cref="I18nResourceReader.DefaultResourceFileName"/> - deliberately the relative name and
    /// not a resolved absolute path, so that no caller in this file can drift into path arithmetic.
    /// </returns>
    /// <remarks>
    /// The existence check is itself performed on the relative name, which is what proves the name
    /// resolves against the process working directory. Nothing here walks a parent directory or
    /// consults <see cref="AppContext.BaseDirectory"/>, because doing so would let the suite find a
    /// resource the legacy could not have found.
    /// </remarks>
    private static string RequireResourceFixture()
    {
        Assert.True(
            File.Exists(I18nResourceReader.DefaultResourceFileName),
            FixtureMissingMessage);

        return I18nResourceReader.DefaultResourceFileName;
    }

    /// <summary>
    /// Opens the real resource READ-ONLY and parses it, so that a test can assert on the document's
    /// own shape rather than only on what the reader returns.
    /// </summary>
    /// <returns>The parsed resource table.</returns>
    /// <remarks>
    /// Deliberately the same access shape the reader uses: <see cref="FileMode.Open"/> never
    /// creates, <see cref="FileAccess.Read"/> makes a write physically impossible on the handle, and
    /// loading from the stream rather than from a file name keeps the working-directory resolution
    /// explicit instead of delegating it to URI resolution. That is what makes constraint C-C
    /// structural in this file: there is no code path here through which the oracle could be
    /// modified.
    /// <para>
    /// It also matters for constraint C-D that the inspection side uses the SAME framework XML API
    /// the substitution names - <see cref="XDocument"/>, together with the
    /// <c>System.Xml.XPath</c> extensions where a test wants an INDEPENDENT second opinion on the
    /// document's shape - and no other. This suite reaches for no parser the reader could not have
    /// used. Note the deliberate asymmetry: the reader itself no longer evaluates XPath at all, so
    /// where a test does, it is cross-checking the traversal against a different framework API
    /// rather than re-running the reader's own mechanism.
    /// </para>
    /// </remarks>
    private static XDocument LoadResourceForInspection()
    {
        string fileName = RequireResourceFixture();

        using FileStream stream = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);

        return XDocument.Load(stream, LoadOptions.None);
    }

    /// <summary>
    /// Returns the resource's root element, having asserted that it is present.
    /// </summary>
    /// <param name="document">The parsed resource table.</param>
    /// <returns>The root element, guaranteed non-<see langword="null"/>.</returns>
    /// <remarks>
    /// <see cref="XDocument.Root"/> is nullable because a document need not have an element, so the
    /// null is answered with an assertion rather than with a null-forgiving operator. That keeps the
    /// file free of suppressions while still giving the compiler's flow analysis what it needs.
    /// </remarks>
    private static XElement RequireRootElement(XDocument document)
    {
        XElement? root = document.Root;

        Assert.NotNull(root);

        return root;
    }

    /// <summary>
    /// Returns the value of a required attribute, having asserted that the attribute exists.
    /// </summary>
    /// <param name="element">The element to read.</param>
    /// <param name="attributeName">The attribute to read.</param>
    /// <returns>The attribute's value, byte for byte as the document records it.</returns>
    /// <remarks>
    /// Used only where the resource is MEASURED to always carry the attribute: every entry carries
    /// both <c>text</c> and <c>to</c>, and every category section carries <c>lang</c>. A failure
    /// here is therefore a real finding about the oracle rather than a missing guard, which is why
    /// the message quotes the offending element in full.
    /// <para>
    /// The null is answered with <c>Assert.Fail</c> inside the null branch rather than with a
    /// null-forgiving operator. That keeps the custom message AND satisfies the compiler's flow
    /// analysis in one move, because <c>Assert.Fail</c> is annotated as never returning - so the
    /// dereference below is reachable only when the attribute is non-null. No suppression is needed
    /// anywhere (C-H).
    /// </para>
    /// </remarks>
    private static string RequiredAttributeValue(XElement element, string attributeName)
    {
        XAttribute? attribute = element.Attribute(attributeName);

        if (attribute is null)
        {
            Assert.Fail(
                $"The '{element.Name.LocalName}' element is missing its required " +
                $"'{attributeName}' attribute, which every element of its kind in pfw.i18n.xml was " +
                $"measured to carry. Element: {element}");
        }

        return attribute.Value;
    }

    /// <summary>
    /// Selects the single category section for a language, asserting that exactly one exists.
    /// </summary>
    /// <param name="document">The parsed resource table.</param>
    /// <param name="category">The category element name.</param>
    /// <param name="language">The language token.</param>
    /// <returns>The one matching section element.</returns>
    /// <remarks>
    /// "Exactly one" is a measured fact, not an assumption: each of the six category names appears
    /// once with <c>lang="en"</c> and once with <c>lang="cht"</c>. Asserting the count rather than
    /// taking the first match is what makes a duplicated section a failure, and a duplicate would
    /// matter - the legacy XPath is wrapped in <c>string()</c>, which silently takes the first node
    /// in document order, so a second section would shadow the first without any other symptom.
    /// <para>
    /// The path is built by interpolation, exactly as the reader builds its own, so the inspection
    /// side cannot accidentally be more permissive than the code under test.
    /// </para>
    /// </remarks>
    private static XElement RequireCategorySection(
        XDocument document,
        string category,
        string language)
    {
        string expression = $"{RootElementName}/{category}[@{LanguageAttributeName}='{language}']";

        List<XElement> sections = [.. document.XPathSelectElements(expression)];

        Assert.True(
            sections.Count == 1,
            $"Expected exactly one '{category}' section with {LanguageAttributeName}=" +
            $"'{language}' in pfw.i18n.xml but found {sections.Count}. XPath: {expression}. " +
            $"Each of the six category names was measured to appear exactly once per language, " +
            $"and a duplicate would be silently shadowed by the legacy string() wrapper.");

        return sections[0];
    }

    /// <summary>
    /// Writes a synthetic resource document to a unique temporary directory, runs the supplied
    /// assertions against a reader pointed at it, and deletes the directory afterwards.
    /// </summary>
    /// <param name="documentText">The synthetic document's exact text.</param>
    /// <param name="assertions">The assertions to run against the resulting reader.</param>
    /// <remarks>
    /// This is how every negative and malformed shape in Region 6 is authored. Three properties are
    /// deliberate and each answers a specific constraint:
    /// <list type="bullet">
    /// <item>
    /// The real <c>pfw.i18n.xml</c> is never involved, so constraint C-C holds even while the suite
    /// exercises shapes the oracle does not contain. Nor is a second copy of the resource checked
    /// in: a synthetic shape is written at run time and removed again.
    /// </item>
    /// <item>
    /// The directory name carries a fresh <see cref="Guid"/>, so parallel test classes - and
    /// parallel clones of this repository sharing one temporary directory - cannot collide.
    /// </item>
    /// <item>
    /// The reader is given an ABSOLUTE path, so no test needs to mutate
    /// <see cref="Environment.CurrentDirectory"/>. That mutation would be process-wide and would
    /// race against every other test class the runner executes in parallel.
    /// </item>
    /// </list>
    /// The delete is in a <c>finally</c> block, so a failing assertion still leaves nothing behind.
    /// </remarks>
    private static void WithSyntheticResource(string documentText, Action<I18nResourceReader> assertions)
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string path = Path.Combine(directory, I18nResourceReader.DefaultResourceFileName);

            File.WriteAllText(path, documentText, SyntheticResourceEncoding);

            assertions(new I18nResourceReader(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Creates a uniquely named temporary directory for a synthetic fixture.
    /// </summary>
    /// <returns>The absolute path of the created directory.</returns>
    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"pfw-i18n-reader-tests-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        return directory;
    }

    // ==========================================================================================
    // REGION 1 - THE FIXTURE IS THERE, AND IT HAS THE PHYSICAL AND STRUCTURAL SHAPE THE READER
    //            DEPENDS ON.
    //
    // This region runs first by intent. Every later region either asserts a value the resource
    // records or asserts a miss, and a miss is what an ABSENT resource produces too - so without
    // the guard below, dropping the csproj Content link would turn much of this suite into a
    // vacuous pass. Oracle: pfw.i18n.xml, read only.
    // ==========================================================================================

    /// <summary>
    /// Proves the resource fixture is present in the test host's working directory under its bare
    /// relative name, which is the only name the legacy ever uses.
    /// </summary>
    /// <remarks>
    /// The single most important assertion in this file. Delete <c>pfw.i18n.xml</c> from the build
    /// output and this test must fail with <see cref="FixtureMissingMessage"/>; if it ever passes
    /// with the resource absent, the guard has been broken and every other real-resource test in
    /// the suite has stopped meaning anything.
    /// </remarks>
    [Fact]
    public void TheResourceFixtureIsPresentUnderItsBareRelativeName()
    {
        string fileName = RequireResourceFixture();

        // The name the reader defaults to is the name the legacy hardcodes at
        // n_cst_i18n_en.sru:L159 and n_cst_i18n_cht.sru:L63. Pinned here so that renaming the
        // constant cannot silently decouple the port from the oracle.
        Assert.Equal("pfw.i18n.xml", fileName);
        Assert.Equal("pfw.i18n.xml", I18nResourceReader.DefaultResourceFileName);

        // A present-but-empty file would satisfy File.Exists and then behave as a total miss, so
        // presence alone is not enough to call the fixture usable. The exact measured size is
        // asserted instead of a mere non-zero check, for two reasons.
        //
        // First, it is the check the csproj asks for in as many words: the Content item only ever
        // COPIES the repository-root resource, so the copy must stay byte identical to it, and a
        // size mismatch in the build output means that read-only guarantee (C-C) has been broken -
        // by an encoding conversion, a newline fixup or any other transform introduced into the
        // build.
        //
        // Second, the resource is a READ-ONLY behavioural oracle, so its size is a constant rather
        // than a moving target. If this assertion ever fails, the resource changed: restore the
        // original bytes. Do not update the number to match whatever it became.
        FileInfo fixture = new(fileName);

        Assert.True(
            fixture.Length == ExpectedResourceByteCount,
            $"The resource fixture '{fileName}' is {fixture.Length} bytes but " +
            $"{ExpectedResourceByteCount} were measured. The build only COPIES the read-only " +
            $"repository-root resource, so any difference means it was transformed on the way into " +
            $"the output directory, or the oracle itself was edited. Restore the original bytes " +
            $"rather than updating this expectation. {FixtureMissingMessage}");
    }

    /// <summary>
    /// Pins the two physical properties that make UTF-8 decoding of the resource correct: it
    /// carries no byte order mark, and it declares no XML declaration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are not trivia. <c>XmlReader</c> defaults to UTF-8 exactly when there is neither a
    /// byte order mark nor an encoding declaration to tell it otherwise, and that default is the
    /// only reason the Chinese in this document round-trips. Both properties were measured: no mark,
    /// and the document's first bytes being the root element's own opening tag.
    /// </para>
    /// <para>
    /// The read is a raw byte read of the first few bytes through a read-only stream. It is the
    /// cheapest possible way to make an encoding regression - a re-encode that added a mark, or a
    /// well-meant "fix" that added a declaration with a different encoding - fail here with a
    /// specific message instead of surfacing later as an inexplicable lookup miss.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheResourceCarriesNoByteOrderMarkAndNoXmlDeclaration()
    {
        string fileName = RequireResourceFixture();

        byte[] prefix = new byte[8];
        int read;

        using (FileStream stream = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            read = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        }

        Assert.True(
            read == prefix.Length,
            $"Expected to read {prefix.Length} bytes from the resource fixture but read {read}.");

        ReadOnlySpan<byte> utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];

        Assert.False(
            prefix.AsSpan(0, utf8ByteOrderMark.Length).SequenceEqual(utf8ByteOrderMark),
            "pfw.i18n.xml has acquired a UTF-8 byte order mark. The resource was measured to " +
            "have none, it sits inside the read-only legacy region, and it must not be " +
            "re-encoded. Restore the original bytes.");

        string decodedPrefix = Encoding.UTF8.GetString(prefix);

        Assert.StartsWith($"<{RootElementName}>", decodedPrefix, StringComparison.Ordinal);

        Assert.DoesNotContain("<?xml", decodedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves the resource parses and that its root element is <c>pfw</c>, the element every legacy
    /// XPath expression is rooted at.
    /// </summary>
    [Fact]
    public void TheResourceParsesWithPfwAsItsRootElement()
    {
        XDocument document = LoadResourceForInspection();

        XElement root = RequireRootElement(document);

        Assert.Equal(RootElementName, root.Name.LocalName);

        // The legacy expressions are relative paths beginning "pfw/", so the root must carry the
        // category sections as its DIRECT children; a nesting level inserted between them would
        // break every lookup at once.
        Assert.NotEmpty(root.Elements());
    }

    /// <summary>
    /// Proves the resource declares exactly the six measured category element names and no others.
    /// </summary>
    /// <remarks>
    /// The set is closed at both ends, and each end is a real risk. A name in the document that no
    /// provider maps onto (<c>n_cst_i18n_en.sru:L34-L47</c>) would be unreachable dead data; a name
    /// a provider maps onto that the document lacks would make that whole category silently
    /// untranslated. Asserting set equality catches both, and the message reports which side
    /// diverged.
    /// </remarks>
    [Fact]
    public void TheResourceDeclaresExactlyTheSixMeasuredCategoryNames()
    {
        XDocument document = LoadResourceForInspection();
        XElement root = RequireRootElement(document);

        HashSet<string> declared = [.. root.Elements().Select(element => element.Name.LocalName)];
        HashSet<string> expected = [.. CategoryElementNames];

        Assert.True(
            declared.SetEquals(expected),
            $"pfw.i18n.xml declares category element names [{string.Join(", ", declared.Order())}] " +
            $"but exactly [{string.Join(", ", expected.Order())}] were measured. A name the " +
            $"providers do not map onto is unreachable data; a name they do map onto but the " +
            $"document lacks makes that category silently untranslated.");
    }

    /// <summary>
    /// Proves every category section carries a <c>lang</c> attribute and that the only values it
    /// ever takes are the two the providers read.
    /// </summary>
    /// <remarks>
    /// A section without the attribute is not merely untidy: the legacy predicate
    /// <c>[@lang='en']</c> cannot match it, so its entire contents become unreachable. Region 6
    /// pins the reader's behaviour on exactly that shape using a synthetic document; this test pins
    /// that the real resource does not contain one.
    /// </remarks>
    [Fact]
    public void EveryCategorySectionCarriesOneOfTheTwoMeasuredLanguageTokens()
    {
        XDocument document = LoadResourceForInspection();
        XElement root = RequireRootElement(document);

        foreach (XElement section in root.Elements())
        {
            string language = RequiredAttributeValue(section, LanguageAttributeName);

            Assert.Contains(language, ResourceLanguages);
        }
    }

    /// <summary>
    /// Proves every entry in the resource is a <c>tr</c> element carrying both a <c>text</c> and a
    /// <c>to</c> attribute, and that neither value is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All three properties were measured across every entry. The non-empty <c>to</c> check is
    /// the interesting one: because the miss signal IS the empty string, an entry whose translation
    /// were empty would be indistinguishable from an absent entry at every call site. The resource
    /// contains no such entry, and this test is what would report it if one were ever added.
    /// Region 6 pins the reader's behaviour on that shape synthetically, so the property is covered
    /// from both directions without touching the oracle.
    /// </para>
    /// <para>
    /// The count is asserted too. It is a single number that changes whenever the oracle changes,
    /// which makes an unnoticed edit to a read-only file impossible to miss.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryEntryCarriesBothANonEmptySourceTextAndANonEmptyTranslation()
    {
        XDocument document = LoadResourceForInspection();
        XElement root = RequireRootElement(document);

        int entryCount = 0;

        foreach (XElement section in root.Elements())
        {
            foreach (XElement entry in section.Elements())
            {
                Assert.Equal(EntryElementName, entry.Name.LocalName);

                string sourceText = RequiredAttributeValue(entry, SourceTextAttributeName);
                string translation = RequiredAttributeValue(entry, TranslationAttributeName);

                Assert.NotEqual(string.Empty, sourceText);

                Assert.True(
                    translation.Length > 0,
                    $"Entry '{sourceText}' in the '{section.Name.LocalName}' section has an empty " +
                    $"translation. The empty string is the reader's MISS signal " +
                    $"(n_cst_i18n_en.sru:L52), so an empty translation is indistinguishable from " +
                    $"an absent entry at every call site.");

                entryCount++;
            }
        }

        // The measured entry count across the twelve sections. A change here means the read-only
        // oracle changed, which is a finding to investigate rather than a number to update.
        Assert.Equal(ExpectedEntryCount, entryCount);
    }

    // ==========================================================================================
    // REGION 2 - EVERY SECTION RESOLVES THROUGH THE READER, AND chs RESOLVES NOWHERE.
    //
    // Prescribed shape (AAP 0.6.7): a table-driven matrix expressed as a theory with member data.
    // The matrix is the cross product of the six category names with the two language tokens -
    // twelve rows, every one expected to be found.
    //
    // WHY THE MEMBER DATA IS STATIC AND NOT DERIVED FROM THE FILE
    // Deriving the rows from the resource at discovery time would be self-defeating: with the
    // fixture absent, the enumeration would yield NOTHING, the theory would report zero executed
    // cases, and a suite that verified nothing would go green. The rows are therefore hardcoded
    // measured facts, so a missing or truncated resource makes all twelve rows FAIL. The expected
    // TRANSLATION is still read from the document inside the test body, which is what keeps the
    // matrix from having to transcribe all 126 entries.
    // ==========================================================================================

    /// <summary>
    /// The twelve category-and-language rows of the resource, as a static cross product.
    /// </summary>
    /// <returns>Twelve rows: six category names times the two language tokens.</returns>
    /// <remarks>
    /// Typed <see cref="TheoryData{T1,T2}"/> rather than <c>IEnumerable&lt;object[]&gt;</c> so the
    /// compiler checks each row against the test method's signature. If this provider ever reports
    /// fewer than twelve executed cases, the matrix has collapsed and the region is no longer
    /// testing what it claims to.
    /// </remarks>
    public static TheoryData<string, string> CategoryLanguageMatrix()
    {
        TheoryData<string, string> rows = [];

        foreach (string category in CategoryElementNames)
        {
            foreach (string language in ResourceLanguages)
            {
                rows.Add(category, language);
            }
        }

        return rows;
    }

    /// <summary>
    /// Proves each of the twelve category-and-language sections exists exactly once and that the
    /// reader returns its recorded translation for the section's first entry.
    /// </summary>
    /// <param name="category">The category element name.</param>
    /// <param name="language">The language token.</param>
    /// <remarks>
    /// The expected value is taken from the document rather than transcribed, so this test states a
    /// relationship - "the reader agrees with the resource" - across all twelve sections without
    /// duplicating the resource into source code. Region 3 does the transcribing, deliberately and
    /// selectively, for eleven hand-verified rows.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CategoryLanguageMatrix))]
    public void EverySectionResolvesThroughTheReader(string category, string language)
    {
        XDocument document = LoadResourceForInspection();

        XElement section = RequireCategorySection(document, category, language);

        XElement? firstEntry = section.Elements(EntryElementName).FirstOrDefault();

        if (firstEntry is null)
        {
            Assert.Fail(
                $"The '{category}' section for {LanguageAttributeName}='{language}' contains no " +
                $"'{EntryElementName}' entries, so nothing in that category can ever be " +
                $"translated.");
        }

        string sourceText = RequiredAttributeValue(firstEntry, SourceTextAttributeName);
        string expectedTranslation = RequiredAttributeValue(firstEntry, TranslationAttributeName);

        I18nResourceReader reader = new();

        Assert.Equal(expectedTranslation, reader.Lookup(language, category, sourceText));
    }

    /// <summary>
    /// Proves the resource declares no <c>lang="chs"</c> section, in any category.
    /// </summary>
    /// <remarks>
    /// The document-side half of the Simplified Chinese finding. Simplified Chinese is the
    /// framework's base locale, so <c>n_cst_i18n_chs</c> has nothing to translate: it declares no
    /// <c>n_xmldoc</c> field and its entire translate body is <c>n_cst_i18n_chs.sru:L25-L26</c>,
    /// which returns 1 for the framework's own source without altering the text. The resource
    /// corroborates that independently by carrying no <c>chs</c> data at all. Completing the
    /// document's language coverage would be inventing data, which is why this absence is asserted
    /// rather than filled.
    /// </remarks>
    [Fact]
    public void NoSectionDeclaresTheSimplifiedChineseLanguageToken()
    {
        XDocument document = LoadResourceForInspection();

        List<XElement> simplifiedChineseSections =
            [.. document.XPathSelectElements($"//*[@{LanguageAttributeName}='{AbsentLanguageToken}']")];

        Assert.True(
            simplifiedChineseSections.Count == 0,
            $"pfw.i18n.xml has acquired {simplifiedChineseSections.Count} section(s) with " +
            $"{LanguageAttributeName}='{AbsentLanguageToken}'. The resource was measured to carry " +
            $"only 'en' and 'cht', which is why the Simplified Chinese provider is a genuine " +
            $"no-op that needs no resource. Adding chs data would be inventing behaviour the " +
            $"legacy does not have.");
    }

    /// <summary>
    /// The six categories, each expected to miss for the absent Simplified Chinese token.
    /// </summary>
    /// <returns>Six rows, one per measured category element name.</returns>
    public static TheoryData<string> CategoryNames()
    {
        TheoryData<string> rows = [];

        foreach (string category in CategoryElementNames)
        {
            rows.Add(category);
        }

        return rows;
    }

    /// <summary>
    /// Proves that a Simplified Chinese lookup misses in every category, returning the empty string
    /// rather than throwing.
    /// </summary>
    /// <param name="category">The category element name.</param>
    /// <remarks>
    /// The reader-side half of the finding above. The source text used is taken from the English
    /// section, so it is a text the resource genuinely contains - the miss is caused purely by the
    /// absent language token and not by an unknown key, which is what makes this row meaningful
    /// rather than merely true.
    /// </remarks>
    [Theory]
    [MemberData(nameof(CategoryNames))]
    public void SimplifiedChineseLookupsMissInEveryCategory(string category)
    {
        XDocument document = LoadResourceForInspection();

        XElement englishSection = RequireCategorySection(document, category, "en");

        XElement? firstEntry = englishSection.Elements(EntryElementName).FirstOrDefault();

        if (firstEntry is null)
        {
            Assert.Fail(
                $"The '{category}' English section contains no entries, so this row cannot " +
                $"distinguish an absent language from an unknown source text.");
        }

        string sourceText = RequiredAttributeValue(firstEntry, SourceTextAttributeName);

        I18nResourceReader reader = new();

        // C-B - PRESERVED BEHAVIOUR. The empty string is the legacy miss signal, produced by the
        // XPath string() wrapper over an empty node set and tested by the provider's
        // "if sTo <> """ gate at n_cst_i18n_en.sru:L52. Not an exception, not null, and no
        // fallback to the English or Traditional Chinese table.
        Assert.Equal(string.Empty, reader.Lookup(AbsentLanguageToken, category, sourceText));
    }

    // ==========================================================================================
    // REGION 3 - KNOWN-GOOD LOOKUPS, TRANSCRIBED FROM THE RESOURCE AND VERIFIED AGAINST IT.
    //
    // Where Region 2 asserts a RELATIONSHIP (the reader agrees with whatever the document says),
    // this region asserts VALUES. That distinction is the point: a transcription bug in the reader
    // and a transcription bug in a relationship test cancel out, so at least one region has to
    // state the expected text independently. Every row below was read out of pfw.i18n.xml at the
    // cited line and compared character by character before being written here; none was recalled
    // from memory.
    //
    // At least one row per category per language, eleven rows in all - the last row of each language
    // being a lookup the in-scope DataWindow validation path genuinely performs, rather than one
    // merely chosen to fill the category.
    // ==========================================================================================

    /// <summary>
    /// Eleven hand-verified lookups covering all six categories in English and four of them in
    /// Traditional Chinese, each transcribed from the cited line of the resource.
    /// </summary>
    /// <returns>
    /// Rows of language, category, source text and the exact expected translation.
    /// </returns>
    /// <remarks>
    /// Line locators, all verified against <c>pfw.i18n.xml</c> while writing this suite:
    /// <c>L5</c>, <c>L24</c>, <c>L30</c>, <c>L33</c>, <c>L41</c>, <c>L53</c> and <c>L57</c> for
    /// English, and <c>L80</c>, <c>L113</c>, <c>L128</c> and <c>L131</c> for Traditional Chinese.
    /// The last row of each language is one the in-scope DataWindow validation path actually
    /// performs; see the comments on those rows.
    /// </remarks>
    public static TheoryData<string, string, string, string> KnownGoodLookups()
    {
        return new TheoryData<string, string, string, string>
        {
            // pfw.i18n.xml:L5   <tr text="还原" to="Restore"/>
            { "en", "window", "还原", "Restore" },

            // pfw.i18n.xml:L24  <tr text="展开左侧面板" to="Expand the left panel"/>
            { "en", "splitcontainer", "展开左侧面板", "Expand the left panel" },

            // pfw.i18n.xml:L30  <tr text="固定" to="Dock"/>
            { "en", "tabcontrol", "固定", "Dock" },

            // pfw.i18n.xml:L33  <tr text="折叠功能区" to="Collapse"/>
            { "en", "ribbonbar", "折叠功能区", "Collapse" },

            // pfw.i18n.xml:L41  <tr text="确定" to="OK"/>
            { "en", "msgbox", "确定", "OK" },

            // pfw.i18n.xml:L53  <tr text="修改数据被拒绝" to="Modified data is rejected"/>
            { "en", "dwsvc", "修改数据被拒绝", "Modified data is rejected" },

            // The two rows below are not merely "one per category": they are the exact lookups the
            // in-scope DataWindow validation path PERFORMS, and they are the reason the dwsvc
            // category is in scope at all. Verified call sites, both inside se_cst_dw's
            // item-validation-error handler:
            //
            //     se_cst_dw.sru:L355   sErrMsg = I18N(ne_cst_i18n.CAT_DWSVC,"输入了无效的值") + "!"
            //     se_cst_dw.sru:L357   MessageBox(I18N(ne_cst_i18n.CAT_DWSVC,"错误"),sErrMsg,StopSign!)
            //
            // Both arguments resolve, so the category is genuinely reachable rather than reachable
            // in principle. (These two call sites are also where the legacy raises a DIALOG; the
            // port turns the delivery channel into a structured error while preserving the text, so
            // the text has to keep resolving exactly as it does here.)
            //
            // pfw.i18n.xml:L57  <tr text="输入了无效的值" to="Invalid input value"/>
            { "en", "dwsvc", "输入了无效的值", "Invalid input value" },

            // pfw.i18n.xml:L131 <tr text="错误" to="錯誤"/>
            { "cht", "dwsvc", "错误", "錯誤" },

            // pfw.i18n.xml:L80  <tr text="还原" to="還原"/>
            // The same source text as the first row, resolving differently per language - which is
            // what proves the lang predicate actually selects, rather than the first match winning.
            { "cht", "window", "还原", "還原" },

            // pfw.i18n.xml:L113 <tr text="询问" to="詢問"/>
            { "cht", "msgbox", "询问", "詢問" },

            // pfw.i18n.xml:L128 <tr text="修改数据被拒绝" to="修改數據被拒絕"/>
            { "cht", "dwsvc", "修改数据被拒绝", "修改數據被拒絕" },
        };
    }

    /// <summary>
    /// Proves the reader returns the exact translation the resource records, for eleven hand-verified
    /// rows spanning every category and both languages.
    /// </summary>
    /// <param name="language">The language token.</param>
    /// <param name="category">The category element name.</param>
    /// <param name="sourceText">The source text to translate.</param>
    /// <param name="expectedTranslation">The translation transcribed from the cited line.</param>
    [Theory]
    [MemberData(nameof(KnownGoodLookups))]
    public void KnownGoodLookupsReturnTheRecordedTranslation(
        string language,
        string category,
        string sourceText,
        string expectedTranslation)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        Assert.Equal(expectedTranslation, reader.Lookup(language, category, sourceText));
    }

    /// <summary>
    /// Proves the two documented mistranslations come back UNREPAIRED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// C-B - PRESERVED DEFECTS. These are data defects inside the read-only resource, and the
    /// requirements are explicit that legacy defects are replicated and documented, never
    /// corrected:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>pfw.i18n.xml:L4</c> renders the minimize label with the English word for its opposite.
    /// The row immediately above it, <c>L3</c>, maps the maximize label to that same word
    /// correctly, so the two are asserted TOGETHER: only the pair shows that one line is wrong
    /// while its neighbour is right, and only the pair fails if someone "fixes" L4.
    /// </item>
    /// <item>
    /// <c>pfw.i18n.xml:L49</c> renders a hide label as a corrupted mixed-language string.
    /// </item>
    /// </list>
    /// <para>
    /// The correct wording survives only in the commented-out pre-resource translation table at
    /// <c>n_cst_i18n_en.sru:L58-L153</c> - see <c>L64-L65</c> and <c>L149-L150</c> - and reviving
    /// it is exactly the silent correction the requirements forbid. Because the data is read-only,
    /// faithful behaviour is achieved simply by READING HONESTLY, which makes "do not repair on
    /// read" the property this test pins.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheTwoDocumentedMistranslationsAreReturnedUncorrected()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        // pfw.i18n.xml:L3 - correct. The maximize label really does translate to "Maximize".
        Assert.Equal("Maximize", reader.Lookup("en", "window", "最大化"));

        // pfw.i18n.xml:L4 - MISTRANSLATION, PRESERVED DELIBERATELY. The minimize label is recorded
        // as "Maximize", the word for its opposite. It should read "Minimize", and
        // n_cst_i18n_en.sru:L64-L65 shows that it once did. If this assertion is ever seen to fail,
        // the correct response is to restore the resource, never to relax the assertion.
        Assert.Equal("Maximize", reader.Lookup("en", "window", "最小化"));

        // pfw.i18n.xml:L48 - correct, and the immediate neighbour of the defect below.
        Assert.Equal("Show details", reader.Lookup("en", "msgbox", "查看详情"));

        // pfw.i18n.xml:L49 - MISTRANSLATION, PRESERVED DELIBERATELY. A corrupted mixed-language
        // string where n_cst_i18n_en.sru:L149-L150 shows "Hide details" was intended. Preserved
        // verbatim for the same reason as above.
        Assert.Equal("Htexte details", reader.Lookup("en", "msgbox", "隐藏"));
    }

    /// <summary>
    /// Proves that brace characters in both the source text and the translation survive the lookup
    /// untouched.
    /// </summary>
    /// <remarks>
    /// <c>pfw.i18n.xml:L52</c> and <c>:L127</c> are the resource's only brace-bearing entries, and
    /// the braces are a <c>Sprintf</c> placeholder that the CALLER fills in after translation - the
    /// legacy substitution helper the providers themselves use at <c>n_cst_i18n_en.sru:L51</c>. The
    /// reader must therefore treat both the key and the value as opaque text: no formatting, no
    /// escaping, no interpretation. Asserting this explicitly matters because an implementation that
    /// reached for a formatting API on the way out would silently consume or mangle the placeholder,
    /// and every ordinary row in this suite would still pass.
    /// </remarks>
    [Fact]
    public void BracePlaceholdersAreReturnedUninterpreted()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        // pfw.i18n.xml:L52  <tr text="第{}行" to="Line {}"/>
        Assert.Equal("Line {}", reader.Lookup("en", "dwsvc", "第{}行"));

        // pfw.i18n.xml:L127 <tr text="第{}行" to="第{}行"/>
        Assert.Equal("第{}行", reader.Lookup("cht", "dwsvc", "第{}行"));
    }

    /// <summary>
    /// Proves Chinese text round-trips through the reader intact, asserted against explicit Unicode
    /// escapes so that the assertion cannot itself be corrupted by a source-encoding problem.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row chosen is <c>pfw.i18n.xml:L113</c>, the Traditional Chinese enquiry label, because
    /// BOTH its key and its value are Chinese: a reader that decoded the document with the wrong
    /// encoding would fail to match the key, and one that decoded the key correctly but mangled the
    /// value would fail on the value. Either failure lands here rather than surfacing later as an
    /// inexplicable miss.
    /// </para>
    /// <para>
    /// The escapes spell the same two labels the other rows in this region write as literal
    /// characters - the key is the Simplified enquiry label and the value its Traditional form.
    /// Written as escapes on purpose: if this file's own bytes were ever decoded as something other
    /// than UTF-8, a literal comparison could pass while comparing two identically-mangled strings,
    /// whereas an escape sequence is pure ASCII and cannot be mangled at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ChineseTextRoundTripsThroughTheReader()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        // \u8BE2\u95EE is the Simplified enquiry label; \u8A62\u554F is its Traditional form.
        string translation = reader.Lookup("cht", "msgbox", "\u8BE2\u95EE");

        Assert.Equal("\u8A62\u554F", translation);

        // Asserted on the code points as well, so a failure reports WHICH character diverged rather
        // than showing two strings a terminal may render identically.
        Assert.Equal(2, translation.Length);
        Assert.Equal(0x8A62, translation[0]);
        Assert.Equal(0x554F, translation[1]);
    }

    /// <summary>
    /// Proves this test file's own Chinese literals were decoded as UTF-8 by the compiler, so that
    /// every literal row in this suite can be trusted.
    /// </summary>
    /// <remarks>
    /// A guard on the tests rather than on the code under test, and a cheap one. The repository
    /// <c>.editorconfig</c> declares <c>charset = utf-8</c>, meaning UTF-8 WITHOUT a byte order
    /// mark, so this file carries no mark and the compiler must infer the encoding. It does infer
    /// UTF-8, but the inference is worth pinning: if it ever changed, the literal keys in
    /// <see cref="KnownGoodLookups"/> would stop matching the resource and the failures would look
    /// like reader defects rather than a toolchain change. Comparing each literal against its
    /// pure-ASCII escape sequence localises that diagnosis to this one test.
    /// </remarks>
    [Fact]
    public void TheChineseLiteralsInThisFileAreDecodedAsUtf8()
    {
        Assert.Equal("\u8FD8\u539F", "还原");
        Assert.Equal("\u9084\u539F", "還原");
        Assert.Equal("\u6700\u5C0F\u5316", "最小化");
        Assert.Equal("\u6700\u5927\u5316", "最大化");
        Assert.Equal("\u9690\u85CF", "隐藏");
        Assert.Equal("\u56FA\u5B9A", "固定");
        Assert.Equal("\u6298\u53E0\u529F\u80FD\u533A", "折叠功能区");
        Assert.Equal("\u5C55\u5F00\u5DE6\u4FA7\u9762\u677F", "展开左侧面板");
        Assert.Equal("\u786E\u5B9A", "确定");
        Assert.Equal("\u8BE2\u95EE", "询问");
        Assert.Equal("\u4FEE\u6539\u6570\u636E\u88AB\u62D2\u7EDD", "修改数据被拒绝");
        Assert.Equal("\u4FEE\u6539\u6578\u64DA\u88AB\u62D2\u7D55", "修改數據被拒絕");
        Assert.Equal("\u7B2C{}\u884C", "第{}行");
        Assert.Equal("\u67E5\u770B\u8BE6\u60C5", "查看详情");
        Assert.Equal("\u8F93\u5165\u4E86\u65E0\u6548\u7684\u503C", "输入了无效的值");
        Assert.Equal("\u9519\u8BEF", "错误");
        Assert.Equal("\u932F\u8AA4", "錯誤");
    }

    // ==========================================================================================
    // REGION 4 - MISSES YIELD NOT-FOUND. NEVER AN EXCEPTION, NEVER A FALLBACK.
    //
    // The behaviour this whole suite exists to protect from being "improved". A miss is the
    // ORDINARY path in the legacy - it is how every non-framework source, every unmapped category
    // and every untranslated string leaves the provider - and the empty string is the signal the
    // provider's own gate reads:
    //
    //     n_cst_i18n_en.sru:L51    the XPath is wrapped in string(), which yields "" for an empty
    //                              node set
    //     n_cst_i18n_en.sru:L52    if sTo <> "" then          <-- the gate that decides
    //     n_cst_i18n_cht.sru:L53   the same gate
    //
    // If a miss threw, that gate could never be reached: the event would unwind instead of
    // returning 0, and the facade's silent passthrough would become a failure at every call site.
    // NO TEST IN THIS REGION - OR ANYWHERE IN THIS FILE - ASSERTS AN EXCEPTION TYPE FOR A LOOKUP.
    // ==========================================================================================

    /// <summary>
    /// The three kinds of miss, one row each, with the reason the row exists.
    /// </summary>
    /// <returns>Rows of language, category, source text and a description of the miss kind.</returns>
    /// <remarks>
    /// Exactly one dimension is wrong in each row, and the other two are values the resource
    /// genuinely contains. That is what makes each row diagnostic: a single implementation that
    /// mishandled, say, the language predicate would fail one row and pass the other two, naming its
    /// own defect.
    /// </remarks>
    public static TheoryData<string, string, string, string> MissKinds()
    {
        return new TheoryData<string, string, string, string>
        {
            // A source text that appears in no entry of the resource. Category and language are
            // both real, so only the key is at fault.
            {
                "en",
                "window",
                "this source text appears in no entry of pfw.i18n.xml",
                "an unknown source text"
            },

            // A category element name the document does not declare. The language is real and the
            // source text exists in another category, so only the category is at fault. This is
            // also the shape a provider produces when it maps a category it has no name for.
            {
                "en",
                "nosuchcategoryelement",
                "还原",
                "an unknown category element name"
            },

            // The Simplified Chinese token, which is genuinely absent from the resource - the most
            // meaningful negative language row available, because it is a token the framework
            // really has a provider for while the document really has no section for it. Category
            // and source text are both real, so only the language is at fault.
            {
                AbsentLanguageToken,
                "window",
                "还原",
                "a language token the resource does not declare"
            },
        };
    }

    /// <summary>
    /// Proves each kind of miss returns the empty string and does not throw.
    /// </summary>
    /// <param name="language">The language token.</param>
    /// <param name="category">The category element name.</param>
    /// <param name="sourceText">The source text to translate.</param>
    /// <param name="missKind">A description of why this row is expected to miss.</param>
    /// <remarks>
    /// The lookup is called OUTSIDE any exception-handling construct on purpose. If it threw, this
    /// test would fail with that exception rather than with an assertion message, which is exactly
    /// the diagnosis wanted: the contract is "never throws", so there is nothing to catch and
    /// classify.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MissKinds))]
    public void LookupMissesReturnTheEmptyStringRatherThanThrowing(
        string language,
        string category,
        string sourceText,
        string missKind)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        string translation = reader.Lookup(language, category, sourceText);

        // C-B - PRESERVED BEHAVIOUR, AND THE ONE MOST LIKELY TO BE "IMPROVED".
        // The legacy signals a miss with the empty string, which the provider's gate at
        // n_cst_i18n_en.sru:L52 tests before assigning. Replacing this with a thrown
        // KeyNotFoundException, or with a null return, or with a fallback to another language's
        // table, would each be a behavioural change and not a better contract.
        Assert.Equal(
            string.Empty,
            translation);

        Assert.True(
            translation.Length == 0,
            $"A lookup with {missKind} must yield the empty not-found result so the provider's " +
            $"\"if sTo <> \"\"\" gate (n_cst_i18n_en.sru:L52) still decides, but it returned " +
            $"'{translation}'.");
    }

    /// <summary>
    /// Proves that degenerate arguments miss rather than throw, including the two that make the
    /// legacy's own interpolated expression malformed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All four rows are misses in the legacy and all four are misses in the reader, and the two
    /// halves reach that result by different routes - which is the point of grouping them. The
    /// legacy builds its query by UNESCAPED substitution at <c>n_cst_i18n_en.sru:L51</c>, so two of
    /// these inputs produce an expression that is not valid XPath at all:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// An EMPTY CATEGORY yields a path with an empty step. The legacy additionally guards the
    /// category upstream at <c>n_cst_i18n_en.sru:L49</c> with <c>if sCat &lt;&gt; "" then</c>. The
    /// reader reaches the same miss through its well-formed-name guard, which is REQUIRED rather
    /// than incidental: the category names an element, and without the guard an unusable name would
    /// throw out of a method contracted never to throw.
    /// </item>
    /// <item>
    /// A source text containing an APOSTROPHE closes the predicate's string literal early in the
    /// legacy. The reader compares the apostrophe as data instead, and no <c>text</c> attribute in
    /// the resource contains one - the very next test asserts that - so the input matches nothing
    /// and the caller sees no translation either way.
    /// </item>
    /// </list>
    /// <para>
    /// The remaining two rows match nothing under both spellings. Grouping all four here documents
    /// that a MALFORMED query and an UNMATCHED query are indistinguishable at the call site, which
    /// is the legacy's behaviour and is what keeps the facade's passthrough uniform.
    /// </para>
    /// </remarks>
    [Fact]
    public void DegenerateArgumentsMissRatherThanThrow()
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        // An unusable category element name. Malformed in the legacy expression, rejected by the
        // reader's well-formed-name guard, and a miss in both.
        Assert.Equal(string.Empty, reader.Lookup("en", string.Empty, "还原"));

        // The apostrophe closes the predicate's literal early in the legacy. Here it is compared as
        // data, and no entry's source text contains one, so both spellings miss.
        Assert.Equal(string.Empty, reader.Lookup("en", "window", "it's"));

        // Well-formed expressions that match nothing.
        Assert.Equal(string.Empty, reader.Lookup(string.Empty, "window", "还原"));
        Assert.Equal(string.Empty, reader.Lookup("en", "window", string.Empty));
    }

    /// <summary>
    /// Proves no <c>text</c> attribute in the resource table contains an apostrophe, which is the
    /// property that makes the reader's data comparison and the legacy's interpolated expression
    /// observationally identical over every real entry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The load-bearing premise of the query change, asserted rather than assumed. The legacy
    /// interpolates the source text into an XPath string literal, so an apostrophe in a
    /// <c>text</c> attribute would break the legacy's own query for that entry while the reader
    /// resolved it perfectly - a real divergence rather than a theoretical one. Because the table
    /// contains no such entry, the divergence is unreachable for every translation the framework
    /// actually performs.
    /// </para>
    /// <para>
    /// The resource is inside the read-only legacy region, so this cannot regress through an edit
    /// made here; it can regress through an edit made upstream, and this test is what would report
    /// it. The <c>to</c> attribute is deliberately NOT asserted: a translation's content never
    /// reaches a query, only an entry's source text does.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoEntrySourceTextContainsAnApostrophe()
    {
        RequireResourceFixture();

        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);

        List<string> offending =
        [
            .. document
                .Descendants(EntryElementName)
                .Select(entry => (string?)entry.Attribute(SourceTextAttributeName) ?? string.Empty)
                .Where(text => text.Contains('\'', StringComparison.Ordinal))
        ];

        Assert.True(
            offending.Count == 0,
            $"{offending.Count} entry source text(s) in " +
            $"{I18nResourceReader.DefaultResourceFileName} contain an apostrophe: " +
            $"[{string.Join(", ", offending)}]. That is the one input class where the legacy's " +
            $"interpolated XPath (n_cst_i18n_en.sru:L51) and the reader's data comparison differ - " +
            $"the legacy breaks its own query and misses, the reader resolves the entry. While the " +
            $"count is zero the two are observationally identical over the whole table. If this " +
            $"ever fails, re-measure the pair and record the divergence; do NOT reintroduce the " +
            $"interpolated expression, which the reader replaced because a balanced payload " +
            $"selected an entry the caller never asked for.");
    }

    /// <summary>
    /// Proves every entry in the resource table resolves through the reader to the translation the
    /// table records, byte for byte.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole-table equivalence assertion. <see cref="KnownGoodLookupsReturnTheRecordedTranslation"/>
    /// pins a hand-picked sample and the two mistranslations; this walks EVERY section and EVERY
    /// entry the file declares and requires each one to come back out of the reader unchanged, so a
    /// change to the query mechanism cannot pass by resolving most of the table.
    /// </para>
    /// <para>
    /// It also pins the traversal's document-order semantics on the one entry class where order is
    /// observable: two entries can share a source text within a section only if the file declares a
    /// duplicate, and the legacy's <c>string()</c> wrapper takes the FIRST node in document order,
    /// so this test compares against the first declaration of each source text rather than the
    /// last.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryEntryInTheResourceResolvesToItsRecordedTranslation()
    {
        RequireResourceFixture();

        XDocument document = XDocument.Load(I18nResourceReader.DefaultResourceFileName);
        I18nResourceReader reader = new();

        int asserted = 0;

        foreach (XElement section in RequireRootElement(document).Elements())
        {
            string category = section.Name.LocalName;
            string language = (string?)section.Attribute(LanguageAttributeName) ?? string.Empty;

            HashSet<string> seen = new(StringComparer.Ordinal);

            foreach (XElement entry in section.Elements(EntryElementName))
            {
                string source = (string?)entry.Attribute(SourceTextAttributeName) ?? string.Empty;

                // Document order: only the FIRST declaration of a source text is reachable, which
                // is the legacy string(node-set) rule. A later duplicate is unreachable in both.
                if (!seen.Add(source))
                {
                    continue;
                }

                string expected = (string?)entry.Attribute(TranslationAttributeName) ?? string.Empty;
                string actual = reader.Lookup(language, category, source);

                Assert.Equal(expected, actual);
                asserted++;
            }
        }

        Assert.True(
            asserted >= ExpectedEntryCount,
            $"Only {asserted} entries were asserted, fewer than the {ExpectedEntryCount} measured " +
            $"in {I18nResourceReader.DefaultResourceFileName}. Either the resource shrank or the " +
            $"walk stopped early; both invalidate the whole-table equivalence this test provides.");
    }

    /// <summary>
    /// Proves a crafted source text, category or language cannot make the reader return a
    /// translation other than the one asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The security regression test for the query change, and every row is a payload that was
    /// MEASURED returning a real translation while the reader interpolated its arguments into an
    /// XPath expression. Against the shipped table, that mechanism answered:
    /// </para>
    /// <list type="bullet">
    /// <item><c>nosuch' or @text='关闭</c> as the source text returned <c>Close</c> - a specific
    /// entry of the attacker's choosing.</item>
    /// <item><c>nosuch' or '1'='1</c> as the source text returned <c>Maximize</c> - whatever the
    /// section declares first.</item>
    /// <item><c>' or '1'='1</c> as the LANGUAGE returned <c>Restore</c> - the requested text out of
    /// any language section, defeating the language selection entirely.</item>
    /// </list>
    /// <para>
    /// Each must now be a miss. The reader's output is shown to users as validation and error text
    /// (<c>se_cst_dw.sru:L357</c>, <c>:L368</c> route dialog text through the facade), so a caller
    /// able to choose WHICH translation comes back can spoof that text; the enterprise security
    /// baseline of constraint C-G forbids leaving such a sink in place. The last two rows carry
    /// path and predicate syntax in the category and in the source text to show that neither
    /// position accepts syntax at all - only data.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("en", "window", "nosuch' or @text='关闭", "selects a chosen entry")]
    [InlineData("en", "window", "nosuch' or '1'='1", "selects the section's first entry")]
    [InlineData("' or '1'='1", "window", "还原", "defeats the language predicate")]
    [InlineData("en", "window", "window[@lang='en']/tr[@text='关闭']", "path syntax as source text")]
    [InlineData("en", "window[@lang='en'] | pfw/window", "还原", "path syntax as category")]
    [InlineData("en", "window", "还原' and @to='Restore", "closes and extends the predicate")]
    public void CraftedArgumentsCannotSelectAnUnaskedForTranslation(
        string language,
        string category,
        string text,
        string payloadKind)
    {
        RequireResourceFixture();

        I18nResourceReader reader = new();

        string translation = reader.Lookup(language, category, text);

        Assert.True(
            translation.Length == 0,
            $"A crafted argument that {payloadKind} returned '{translation}' instead of the " +
            $"not-found result. The reader must compare language, category and source text as " +
            $"DATA; a spelling in which any of the three can contribute query SYNTAX lets a " +
            $"caller choose which localized string is returned, which constraint C-G forbids.");
    }

    /// <summary>
    /// Proves the constructor rejects a null, empty or whitespace-only file name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ONLY exceptions this suite asserts, and they are deliberately NOT lookup misses. A
    /// null or blank file name is a CALLER-CONTRACT violation - the caller has failed to name a
    /// resource at all - and it is categorically different from a resource that is merely absent,
    /// unreadable or malformed, which the reader is specified to treat as data unavailability and to
    /// degrade into a miss. Neither legacy provider can reach either guard, because both pass the
    /// hardcoded literal (<c>n_cst_i18n_en.sru:L159</c>, <c>n_cst_i18n_cht.sru:L63</c>).
    /// </para>
    /// <para>
    /// Asserting these does not weaken the "a miss never throws" property. It sharpens it: the
    /// boundary between the two behaviours is itself part of the contract, and a test that only
    /// covered the miss side would leave an implementation free to blur it - for instance by
    /// throwing on an absent file, which the very next test proves it must not do.
    /// </para>
    /// <para>
    /// <see cref="ArgumentNullException"/> derives from <see cref="ArgumentException"/>, so the null
    /// row asserts the DERIVED type to show the guard distinguishes the two cases rather than
    /// collapsing both into the base type.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheConstructorRejectsAMissingOrBlankResourceName()
    {
        // The file's only null-forgiving operator, and it is the subject of the assertion rather
        // than a silenced diagnostic: expressing "a nullable-oblivious caller passes null to a
        // non-nullable parameter" has no other spelling under nullable reference types. The sibling
        // VectorTests.cs suite uses the identical form for its own argument guards.
        Assert.Throws<ArgumentNullException>(() => new I18nResourceReader(null!));

        Assert.Throws<ArgumentException>(() => new I18nResourceReader(string.Empty));

        Assert.Throws<ArgumentException>(() => new I18nResourceReader("   "));
    }

    // ==========================================================================================
    // REGION 5 - THE RESOURCE IS RESOLVED BY BARE RELATIVE NAME, AGAINST THE WORKING DIRECTORY,
    //            AND NOWHERE ELSE.
    //
    // Both legacy providers load the table by BARE RELATIVE FILENAME:
    //
    //     n_cst_i18n_en.sru:L158-L159   _doc = Create n_xmldoc / _doc.LoadFile("pfw.i18n.xml")
    //     n_cst_i18n_cht.sru:L62-L63    the identical two lines
    //
    // There is no directory, no probing, no search order and no embedded resource anywhere in the
    // legacy path, and the port preserves that rule exactly. Preserving a resolution rule means
    // preserving where the lookup FAILS as much as where it succeeds, which is why this region
    // asserts the negative cases as carefully as the positive one: a probe added "to be helpful"
    // would let the port find files the legacy could not, and that is a behavioural change (C-B).
    //
    // The csproj is what makes the relative name resolvable under test - it LINKS the single real
    // resource into the build output at that bare name - and the xunit.v3 test host's working
    // directory IS that output directory. Nothing here mutates Environment.CurrentDirectory;
    // reading it is safe, mutating it would be process-wide and would race the runner's parallel
    // test classes.
    // ==========================================================================================

    /// <summary>
    /// Proves the bare relative name resolves against the process working directory, and that a
    /// reader built from that name reads the same document as one built from the equivalent absolute
    /// path.
    /// </summary>
    /// <remarks>
    /// The equivalence is the assertion that matters. It shows the relative name is resolved by
    /// ordinary working-directory rules rather than by anything the reader invents, which is exactly
    /// what <c>LoadFile("pfw.i18n.xml")</c> does. The absolute path is derived here purely to state
    /// the equivalence; every other test in this suite uses the relative name only.
    /// </remarks>
    [Fact]
    public void TheResourceIsResolvedByBareRelativeNameFromTheWorkingDirectory()
    {
        string relativeName = RequireResourceFixture();

        // Path.GetFullPath resolves a relative name against the process working directory and
        // nothing else. Asserting the composition explicitly documents the rule rather than leaving
        // it implied by the fact that the lookups below happen to work.
        string resolved = Path.GetFullPath(relativeName);

        Assert.Equal(
            Path.Combine(Environment.CurrentDirectory, relativeName),
            resolved);

        I18nResourceReader viaRelativeName = new(relativeName);
        I18nResourceReader viaAbsolutePath = new(resolved);

        // Same document, therefore the same answer, therefore the relative name resolved to that
        // absolute path and to nothing else.
        Assert.Equal("Restore", viaRelativeName.Lookup("en", "window", "还原"));
        Assert.Equal("Restore", viaAbsolutePath.Lookup("en", "window", "还原"));

        // And the default constructor uses that same bare name, so the parameterless form - the one
        // the providers use - is resolving identically.
        Assert.Equal("Restore", new I18nResourceReader().Lookup("en", "window", "还原"));
    }

    /// <summary>
    /// Proves the reader does NOT probe parent directories for a resource that is absent from the
    /// directory it was pointed at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constructed so that the answer cannot be accidental: a valid resource - one that would return
    /// a distinctive marker if it were ever read - is placed in a PARENT directory, and the reader is
    /// pointed at a child directory that contains no resource at all. A reader that walked upwards
    /// would find the parent's copy and return the marker; the correct behaviour is a miss.
    /// </para>
    /// <para>
    /// This is the one negative that a test using the real fixture cannot express. Under test the
    /// working directory's parents eventually include the repository root, which really does hold
    /// <c>pfw.i18n.xml</c>, so a probing implementation would silently succeed there and look
    /// correct. Only a synthetic tree with a deliberately distinguishable parent copy can tell the
    /// two implementations apart.
    /// </para>
    /// <para>
    /// The whole tree is created under a uniquely named temporary directory and removed in a
    /// <c>finally</c> block, so nothing touches the read-only oracle (C-C) and nothing is left
    /// behind.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoParentDirectoryIsProbedForAnAbsentResource()
    {
        string root = CreateTemporaryDirectory();

        try
        {
            string parent = Path.Combine(root, "parent");
            string child = Path.Combine(parent, "child");

            Directory.CreateDirectory(child);

            // A perfectly valid resource, one directory ABOVE where the reader will look. Its
            // translation is a marker no real lookup could ever produce, so if it appears in the
            // result the reader has probed upwards.
            File.WriteAllText(
                Path.Combine(parent, I18nResourceReader.DefaultResourceFileName),
                $"<{RootElementName}><window {LanguageAttributeName}=\"en\">" +
                $"<{EntryElementName} {SourceTextAttributeName}=\"还原\" " +
                $"{TranslationAttributeName}=\"PROBED-THE-PARENT-DIRECTORY\"/>" +
                $"</window></{RootElementName}>",
                SyntheticResourceEncoding);

            I18nResourceReader reader =
                new(Path.Combine(child, I18nResourceReader.DefaultResourceFileName));

            string translation = reader.Lookup("en", "window", "还原");

            Assert.True(
                translation.Length == 0,
                $"The reader returned '{translation}' for a resource name that does not exist in " +
                $"the directory it was given, which means it probed a parent directory. The legacy " +
                $"LoadFile (n_cst_i18n_en.sru:L159) performs no probing at all, so finding a file " +
                $"the legacy could not find is a behavioural change (C-B), not a convenience.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Proves an absent resource degrades to "every lookup misses" and never to a thrown exception,
    /// at construction or at lookup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The legacy IGNORES its load result: neither constructor checks what <c>LoadFile</c> returned
    /// (<c>n_cst_i18n_en.sru:L158-L159</c>, <c>n_cst_i18n_cht.sru:L62-L63</c>), so a query against a
    /// document that never loaded yields the empty string, the <c>if sTo &lt;&gt; ""</c> gate fails,
    /// the event returns 0, and the facade passes the source text through unchanged.
    /// </para>
    /// <para>
    /// This is the ONE place in this refactor where the fail-fast posture deliberately does not
    /// apply. Fail-fast is right for a composition root whose initialize/finalize pairing is
    /// structural; it is wrong here, because the legacy demonstrably does not fail fast on this
    /// path, and turning a documented silent degradation into a startup fault would be a
    /// behavioural change dressed up as robustness.
    /// </para>
    /// <para>
    /// The name used is unique per run, so it cannot collide with a real file in the working
    /// directory. No file is created and none is deleted - the point is that the resource is not
    /// there.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentResourceDegradesToEveryLookupMissing()
    {
        string absentName = $"pfw.i18n.absent-{Guid.NewGuid():N}.xml";

        Assert.False(
            File.Exists(absentName),
            $"The name '{absentName}' was expected to be absent from the working directory.");

        // Construction must not throw for a data condition, only for a caller-contract violation.
        I18nResourceReader reader = new(absentName);

        Assert.Equal(string.Empty, reader.Lookup("en", "window", "还原"));
        Assert.Equal(string.Empty, reader.Lookup("cht", "msgbox", "询问"));
        Assert.Equal(string.Empty, reader.Lookup("en", "dwsvc", "修改数据被拒绝"));
    }

    /// <summary>
    /// Proves that naming a DIRECTORY instead of a file degrades to a miss rather than throwing.
    /// </summary>
    /// <remarks>
    /// Worth its own test because the platform reports this case as an
    /// <see cref="UnauthorizedAccessException"/>, which does NOT derive from
    /// <see cref="IOException"/> and so has to be handled in its own right. An implementation that
    /// caught only I/O failures would compile, pass every other test in this suite, and then throw
    /// here - in a constructor, at provider construction time, which is the worst possible moment.
    /// </remarks>
    [Fact]
    public void NamingADirectoryInsteadOfAFileDegradesToAMiss()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            I18nResourceReader reader = new(directory);

            Assert.Equal(string.Empty, reader.Lookup("en", "window", "还原"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ==========================================================================================
    // REGION 6 - SYNTHETIC NEGATIVE SHAPES.
    //
    // Shapes the real resource deliberately does NOT contain, so they have to be authored. Every
    // one is written to a unique temporary directory and deleted in a finally block: the read-only
    // oracle is never mutated (C-C) and no second copy of it is ever checked in.
    //
    // Kept small and purposeful. Each row is a shape the reader could plausibly meet if the
    // resource were ever edited, and every one must resolve to the SAME not-found result as an
    // ordinary miss - because at the call site the provider's gate cannot tell them apart, and the
    // legacy's unchecked LoadFile means it never could. This is NOT a general XML-conformance
    // suite and must not grow into one.
    // ==========================================================================================

    /// <summary>
    /// The synthetic document shapes, each expected to yield not-found for a lookup that would
    /// otherwise hit.
    /// </summary>
    /// <returns>Rows of a shape description and the exact document text to write.</returns>
    /// <remarks>
    /// Every row uses the same lookup - English, <c>window</c>, the restore label - so the shape is
    /// the only variable. The first row is the control: it proves the synthetic harness itself
    /// works, without which a passing row could mean "the shape yields not-found" or equally "the
    /// harness never wrote a readable file at all".
    /// </remarks>
    public static TheoryData<string, string, string> SyntheticShapes()
    {
        const string hit = "SYNTHETIC-HIT";

        return new TheoryData<string, string, string>
        {
            // CONTROL. A well-formed minimal document that MUST hit, proving the harness writes a
            // file the reader can actually read and that the remaining rows fail for their stated
            // reason rather than because nothing was ever written.
            {
                "a well-formed minimal document (the control row)",
                $"<pfw><window lang=\"en\"><tr text=\"还原\" to=\"{hit}\"/></window></pfw>",
                hit
            },

            // An entry with no `to` attribute. The legacy XPath terminates in `/@to`, so string()
            // over that empty attribute node set is "" - indistinguishable from an absent entry.
            {
                "an entry missing its 'to' attribute",
                "<pfw><window lang=\"en\"><tr text=\"还原\"/></window></pfw>",
                ""
            },

            // An entry whose translation is empty. Reaches the same result by a different route,
            // and it is why Region 1 asserts the real resource contains no such entry: the empty
            // string IS the miss signal, so an empty translation is silently unreachable data.
            {
                "an entry whose 'to' attribute is empty",
                "<pfw><window lang=\"en\"><tr text=\"还原\" to=\"\"/></window></pfw>",
                ""
            },

            // A category section with no `lang` attribute. The predicate [@lang='en'] cannot match
            // it, so the section's entire contents become unreachable.
            {
                "a category section with no 'lang' attribute",
                "<pfw><window><tr text=\"还原\" to=\"UNREACHABLE\"/></window></pfw>",
                ""
            },

            // A section carrying the right entries under the wrong language token.
            {
                "a category section whose 'lang' attribute names another language",
                "<pfw><window lang=\"cht\"><tr text=\"还原\" to=\"UNREACHABLE\"/></window></pfw>",
                ""
            },

            // An empty document: the root element and nothing else.
            {
                "a document containing only the root element",
                "<pfw></pfw>",
                ""
            },

            // A document whose root is not `pfw`. Every legacy expression is rooted at `pfw/`, so
            // nothing under any other root is addressable.
            {
                "a document whose root element is not 'pfw'",
                "<notpfw><window lang=\"en\"><tr text=\"还原\" to=\"UNREACHABLE\"/></window></notpfw>",
                ""
            },

            // MALFORMED: an unterminated element, so the document is not well-formed XML at all.
            // The parse fails and the reader is left holding no document - the same state an absent
            // file produces - so this must surface as the reader's DEFINED failure, a miss, and not
            // as an unhandled parse exception that would abort the suite mid-run.
            {
                "a malformed document with an unterminated element",
                "<pfw><window lang=\"en\"><tr text=\"还原\" to=\"UNREACHABLE\"/>",
                ""
            },

            // An empty file. Zero bytes is not well-formed XML either, and it is the shape a
            // truncated or interrupted copy leaves behind.
            {
                "an empty file",
                "",
                ""
            },
        };
    }

    /// <summary>
    /// Proves each synthetic shape resolves to the expected result, with the malformed and empty
    /// documents surfacing as the reader's defined not-found rather than as an exception.
    /// </summary>
    /// <param name="shape">A description of the shape under test.</param>
    /// <param name="documentText">The exact document text to write.</param>
    /// <param name="expected">The expected lookup result, empty for every shape but the control.</param>
    [Theory]
    [MemberData(nameof(SyntheticShapes))]
    public void SyntheticResourceShapesResolveToTheDefinedResult(
        string shape,
        string documentText,
        string expected)
    {
        WithSyntheticResource(
            documentText,
            reader =>
            {
                string translation = reader.Lookup("en", "window", "还原");

                Assert.True(
                    string.Equals(expected, translation, StringComparison.Ordinal),
                    $"For {shape}, expected '{expected}' but the reader returned '{translation}'. " +
                    $"Every shape the resource does not contain must resolve to the same " +
                    $"not-found result an ordinary miss produces, because the provider's " +
                    $"\"if sTo <> \"\"\" gate (n_cst_i18n_en.sru:L52) cannot tell them apart.");
            });
    }

    /// <summary>
    /// Proves that a malformed resource does not throw at CONSTRUCTION either, and that the reader
    /// stays usable afterwards.
    /// </summary>
    /// <remarks>
    /// Separated from the theory above because the theory only observes the RESULT of a lookup,
    /// which cannot distinguish "the constructor swallowed the parse failure" from "the constructor
    /// threw and the theory row failed for that reason". Constructing first and asserting several
    /// lookups afterwards pins the constructor's own posture: a data condition never faults, and the
    /// object it produces is a normal, fully usable reader whose every lookup simply misses. That is
    /// the legacy's unchecked <c>LoadFile</c> reproduced exactly.
    /// </remarks>
    [Fact]
    public void AMalformedResourceLeavesAUsableReaderWhoseEveryLookupMisses()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string path = Path.Combine(directory, I18nResourceReader.DefaultResourceFileName);

            File.WriteAllText(
                path,
                "<pfw><window lang=\"en\"><tr text=\"还原\" to=\"UNREACHABLE\"/>",
                SyntheticResourceEncoding);

            // No exception here is the first half of the assertion.
            I18nResourceReader reader = new(path);

            // And the reader is not poisoned: it answers every question, uniformly, with a miss.
            Assert.Equal(string.Empty, reader.Lookup("en", "window", "还原"));
            Assert.Equal(string.Empty, reader.Lookup("cht", "window", "还原"));
            Assert.Equal(string.Empty, reader.Lookup("en", "msgbox", "确定"));
            Assert.Equal(string.Empty, reader.Lookup("en", "window", "anything at all"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ==========================================================================================
    // REGION 7 - THE C-D STRUCTURAL ASSERTIONS. THE REASON THIS SUITE EXISTS IN THIS SHAPE.
    //
    // Constraint C-D (AAP 0.7.3) forbids implementing the four deferred services "even partially,
    // even to stub them out", and Documents owns the eleven-object XML family (AAP 0.4.1). AAP
    // 0.2.1.3 Correction 6 is the resolution: the legacy n_xmldoc / n_xmlqueryresult pair is
    // SUBSTITUTED with System.Xml.Linq.XDocument.Load plus XPathSelectElements, precisely so that
    // Localization acquires ZERO Documents coupling.
    //
    // Every other region in this file is behavioural, and behaviour cannot see the difference. A
    // future edit that swapped the framework XML stack for a package would keep returning "Restore"
    // for the restore label, and every behavioural test above would still pass. Only the assembly's own
    // reference table can tell the two apart, which is what this region reads.
    //
    // Two halves, and both are needed:
    //     POSITIVE  the framework XML facades ARE referenced, so the substitution named by
    //               Correction 6 is demonstrably the mechanism in use rather than merely the
    //               mechanism intended. The two XPATH facades were part of this half until the
    //               reader stopped interpolating its arguments into a query string; the reason
    //               they were dropped rather than kept is recorded on
    //               RequiredSubstitutionAssemblies, and it is a security fix rather than a
    //               loosening of the assertion.
    //     NEGATIVE  nothing outside the framework and the shared kernel is referenced, and
    //               specifically nothing from the deferred Documents capability area and nothing
    //               that logs.
    //
    // A FAILURE IN THIS REGION IS NOT A TEST PROBLEM. It means Localization has acquired coupling
    // to a deferred service, which C-D forbids outright. The fix is always to remove the coupling,
    // never to widen the allow-list.
    // ==========================================================================================

    /// <summary>
    /// The one non-framework assembly the library under test is permitted to reference.
    /// </summary>
    /// <remarks>
    /// It is permitted rather than required, and the distinction is measured rather than assumed:
    /// the Localization project does reference the shared kernel project, but it consumes it only
    /// through <c>public const</c> values, which the compiler INLINES at the use site. No assembly
    /// reference is emitted for a reference the metadata never needs, so this name is currently
    /// absent from the referenced set - and it may legitimately appear the moment a non-constant
    /// kernel member is used. The allow-list therefore admits it without insisting on it.
    /// </remarks>
    private const string PermittedProjectReference = "PowerFramework.Shared.Kernel";

    /// <summary>
    /// The framework assemblies the substitution named by AAP 0.2.1.3 Correction 6 must appear as,
    /// measured from the built assembly on .NET the pinned SDK.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are COMPILE-TIME reference names and they are deliberately hardcoded, because they
    /// cannot be derived at run time: the reference assemblies are facades that forward their types,
    /// so <c>typeof(XDocument).Assembly</c> reports the implementation assembly
    /// (<c>System.Private.Xml.Linq</c>) and not the facade a compiler recorded. Reading the name
    /// from the runtime type would silently compare the wrong thing and the assertion would be
    /// worthless.
    /// </para>
    /// <para>
    /// Each maps onto one half of Correction 6:
    /// </para>
    /// <list type="bullet">
    /// <item><c>System.Xml.XDocument</c> - where <see cref="XDocument"/>, <see cref="XElement"/>
    /// and <see cref="XName"/> live, so this is both <c>XDocument.Load</c>, the substitute for
    /// <c>n_xmldoc.LoadFile</c>, and the element and attribute traversal that substitutes for
    /// <c>n_xmldoc.Query</c>.</item>
    /// <item><c>System.Xml.ReaderWriter</c> - where <c>System.Xml.XmlReader</c> and
    /// <c>System.Xml.XmlConvert</c> live, so this is the read-only parse the constructor performs and
    /// the well-formed-name guard the category argument passes through.</item>
    /// </list>
    /// <para>
    /// TWO XPATH FACADES ARE DELIBERATELY NOT REQUIRED HERE.
    /// <c>System.Xml.XPath.XDocument</c> (the <c>XPathSelectElements</c> extensions) and
    /// <c>System.Xml.XPath</c> (<c>XPathException</c>) are needed only by a reader that builds its
    /// query by interpolating the language, the category and the source text into an XPath string.
    /// That spelling is an injection sink rather than a faithful reproduction of a defect: a
    /// balanced payload in the source text or the language EXTENDS the expression and selects an
    /// entry the caller never asked for, measured against the real table. The reader compares those
    /// values as data instead, so the two XPath facades are not referenced and requiring them would
    /// fail the build for the security fix. Correction 6 is
    /// still satisfied and still asserted: it mandates that the deferred Documents XML family be
    /// substituted with the FRAMEWORK XML API, and the remaining two names are that API. What the
    /// assertion protects is unchanged - a reader that had stopped reading XML at all, in favour of
    /// an embedded string table or a generated dictionary, still fails it.
    /// </para>
    /// <para>
    /// The SDK is pinned by <c>global.json</c> with <c>rollForward: latestFeature</c>, so roll
    /// forward is confined to 10.0 feature bands and this facade set cannot shift under the
    /// assertion. If it ever does shift, the substitution's shape has changed and must be
    /// re-verified against Correction 6 - the correct response is to re-measure, not to delete the
    /// assertion.
    /// </para>
    /// </remarks>
    private static readonly string[] RequiredSubstitutionAssemblies =
    [
        "System.Xml.XDocument",
        "System.Xml.ReaderWriter",
    ];

    /// <summary>
    /// Assembly name prefixes and identities that would represent coupling to the deferred Documents
    /// capability area, or to a logging stack the localization facade must not have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list is what closes the hole the allow-list cannot. The allow-list admits every
    /// <c>Microsoft.*</c> name, because the shared framework and the SDK ship a great many of them
    /// and enumerating those would be unmaintainable - so a <c>Microsoft.*</c>-named package would
    /// slip straight through it. Two families the plan names explicitly are exactly that shape:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <c>Microsoft.SqlServer.TransactSql.ScriptDom</c> - AAP 0.5.3 rejects it by name as the SQL
    /// parser substitute, and a parser package arriving in the localization library would be the
    /// clearest possible sign that "substitute, do not import" had been abandoned.
    /// </item>
    /// <item>
    /// <c>Microsoft.Extensions.Logging</c> and its siblings - the reader is specified to report
    /// NOTHING: a missing or malformed table degrades to a silent miss because the legacy's
    /// unchecked <c>LoadFile</c> does. Asserting the absence of any logging assembly makes
    /// "nothing is logged" hold at assembly level and not merely by inspection of the current
    /// source.
    /// </item>
    /// </list>
    /// <para>
    /// The remaining entries are third-party XML, XPath, JSON, YAML, ZIP, barcode, QR, document and
    /// pinyin libraries - the capability areas AAP 0.4.1 assigns to Documents
    /// (<c>pfw.utility.parser</c>'s eleven JSON and XML objects, <c>pfw.utility.zip</c>,
    /// <c>pfw.utility.barcode</c> and the rest) - together with the logging and telemetry stacks AAP
    /// 0.5.3 excludes. The pinyin entries matter for a second reason: pinyin first-letter matching is
    /// the refactor's single genuine parity risk, and the decision recorded there is to implement no
    /// third-party pinyin package at all, because the legacy lookup table exists only inside a closed
    /// binary.
    /// </para>
    /// <para>
    /// Matching is by case-insensitive PREFIX, so a versioned or sub-namespaced variant of any entry
    /// is caught too. Note that no entry is a prefix of a genuine framework XML facade name: the
    /// framework's are <c>System.Xml.*</c>, and nothing below begins with <c>System.</c>.
    /// </para>
    /// </remarks>
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        // Logging and telemetry - AAP 0.5.3 excludes these, and the facade reports nothing.
        "Microsoft.Extensions.Logging",
        "Serilog",
        "NLog",
        "log4net",
        "OpenTelemetry",

        // Parser packages, including the one AAP 0.5.3 rejects by name.
        "Microsoft.SqlServer.TransactSql.ScriptDom",
        "Antlr",
        "Irony",
        "Sprache",
        "Superpower",

        // Third-party XML, XPath, HTML and YAML.
        "AngleSharp",
        "HtmlAgilityPack",
        "XmlPrime",
        "Saxon",
        "Wmhelp.XPath2",
        "YamlDotNet",
        "ExtendedXmlSerializer",

        // JSON - Documents owns the legacy JSON object family.
        "Newtonsoft.Json",
        "Utf8Json",
        "Jil",
        "ServiceStack.Text",

        // Archive handling - pfw.utility.zip.
        "ICSharpCode.SharpZipLib",
        "SharpZipLib",
        "SharpCompress",
        "DotNetZip",
        "Ionic.Zip",

        // Barcode and QR - pfw.utility.barcode.
        "ZXing",
        "QRCoder",
        "BarcodeLib",
        "ZintNet",
        "Zint",

        // Office and document formats.
        "DocumentFormat.OpenXml",
        "ExcelDataReader",
        "iTextSharp",
        "itext",
        "PdfSharp",

        // Pinyin - the refactor's single genuine parity risk, and no package may stand in for it.
        "NPinyin",
        "Pinyin",
        "TinyPinyin",
        "ToolGood.Words",
    ];

    /// <summary>
    /// Returns the referenced-assembly simple names of the library under test.
    /// </summary>
    /// <returns>
    /// The simple names, with any null name filtered out - <see cref="AssemblyName.Name"/> is
    /// declared nullable, so the null is filtered rather than forgiven.
    /// </returns>
    /// <remarks>
    /// Reached through <c>typeof(I18nResourceReader).Assembly</c> rather than by loading a file from
    /// disk, so the assembly inspected is exactly the one this suite ran its behavioural assertions
    /// against. There is no possibility of examining a stale copy.
    /// </remarks>
    private static string[] ReferencedAssemblyNamesOfTheLibraryUnderTest()
    {
        Assembly libraryUnderTest = typeof(I18nResourceReader).Assembly;

        // Guards against the whole region silently inspecting the wrong assembly, which would make
        // every assertion below vacuously true.
        Assert.Equal("PowerFramework.Shared.Localization", libraryUnderTest.GetName().Name);

        // OfType<string> both filters the nulls and narrows the sequence's element type, which is
        // how the nullable AssemblyName.Name is handled without a null-forgiving operator.
        return [.. libraryUnderTest
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Determines whether a referenced assembly name belongs to the framework.
    /// </summary>
    /// <param name="name">The referenced assembly's simple name.</param>
    /// <returns><see langword="true"/> when the name is a framework assembly.</returns>
    /// <remarks>
    /// The four shapes cover every assembly the shared framework and the SDK can contribute:
    /// <c>System</c> and <c>System.*</c>, <c>Microsoft.*</c>, <c>netstandard</c> and
    /// <c>mscorlib</c>. <c>Microsoft.*</c> is knowingly broad, which is exactly why
    /// <see cref="ForbiddenAssemblyPrefixes"/> exists alongside it.
    /// </remarks>
    private static bool IsFrameworkAssembly(string name)
    {
        return name.StartsWith("System.", StringComparison.Ordinal)
            || string.Equals(name, "System", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal)
            || string.Equals(name, "netstandard", StringComparison.Ordinal)
            || string.Equals(name, "mscorlib", StringComparison.Ordinal);
    }

    /// <summary>
    /// Proves the library under test references ONLY framework assemblies and, at most, the shared
    /// kernel.
    /// </summary>
    /// <remarks>
    /// The negative half of Correction 6, and the broadest gate in this region: any third-party
    /// package reference at all - XML, parser, pinyin, JSON, ZIP, barcode or otherwise - fails here,
    /// whether or not it appears in <see cref="ForbiddenAssemblyPrefixes"/>. That is deliberate,
    /// because the named list can only ever enumerate packages someone thought of, whereas an
    /// allow-list catches the one nobody did.
    /// </remarks>
    [Fact]
    public void TheLibraryUnderTestReferencesOnlyFrameworkAssembliesAndTheSharedKernel()
    {
        string[] referenced = ReferencedAssemblyNamesOfTheLibraryUnderTest();

        List<string> disallowed =
        [
            .. referenced.Where(name =>
                !IsFrameworkAssembly(name)
                && !string.Equals(name, PermittedProjectReference, StringComparison.Ordinal)),
        ];

        Assert.True(
            disallowed.Count == 0,
            $"PowerFramework.Shared.Localization references [{string.Join(", ", disallowed)}], " +
            $"which is neither a framework assembly nor '{PermittedProjectReference}'. " +
            $"Constraint C-D forbids implementing the deferred services even partially, and AAP " +
            $"0.2.1.3 Correction 6 requires the legacy n_xmldoc/n_xmlqueryresult pair to be " +
            $"SUBSTITUTED with the framework XML API - System.Xml.Linq over System.Xml, which " +
            $"ships inside the shared framework - precisely so this library acquires NO Documents " +
            $"coupling. " +
            $"Remove the reference; do not widen this allow-list. " +
            $"Full referenced set: [{string.Join(", ", referenced)}].");
    }

    /// <summary>
    /// Proves the framework XML substitution is the mechanism actually in use.
    /// </summary>
    /// <remarks>
    /// The positive half of Correction 6, and the half a pure deny-list would miss entirely. An
    /// implementation that had stopped reading XML at all - swapped the resource for an embedded
    /// string table, say, or for a generated dictionary - would satisfy every negative assertion in
    /// this region while no longer being the substitution the plan mandates. Asserting the presence
    /// of the named APIs' facades makes that divergence visible. The set it asserts over, and why
    /// the two XPath facades are no longer in it, are documented on
    /// <see cref="RequiredSubstitutionAssemblies"/>.
    /// </remarks>
    [Fact]
    public void TheLibraryUnderTestReferencesTheFrameworkXmlSubstitution()
    {
        string[] referenced = ReferencedAssemblyNamesOfTheLibraryUnderTest();

        foreach (string required in RequiredSubstitutionAssemblies)
        {
            Assert.True(
                referenced.Contains(required, StringComparer.Ordinal),
                $"PowerFramework.Shared.Localization does not reference '{required}'. AAP 0.2.1.3 " +
                $"Correction 6 mandates that the deferred Documents XML parser be substituted with " +
                $"the framework XML API - XDocument.Load plus a traversal of the loaded document - " +
                $"so those facades must be the mechanism in use. Their absence means the reader is " +
                $"no longer that substitution. " +
                $"Full referenced set: [{string.Join(", ", referenced)}].");
        }

        // Belt and braces against the facade set being reshaped by a future toolchain: whatever the
        // individual names turn out to be, at least one framework XML assembly must be referenced.
        Assert.Contains(referenced, name => name.StartsWith("System.Xml.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Proves the library under test references no assembly from the deferred Documents capability
    /// area and no logging assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The targeted deny-list. It overlaps the allow-list above for third-party names, and that
    /// redundancy is intentional - the two fail with different messages, so a violation names the
    /// specific constraint it broke rather than only the general one. Where it does UNIQUE work is
    /// on <c>Microsoft.*</c> names, which the allow-list admits wholesale: without this test,
    /// <c>Microsoft.SqlServer.TransactSql.ScriptDom</c> and the whole
    /// <c>Microsoft.Extensions.Logging</c> family would pass unnoticed.
    /// </para>
    /// <para>
    /// The logging half also pins a behavioural property structurally. The reader is specified to
    /// report nothing at all - a missing or malformed table degrades to a silent miss, mirroring the
    /// legacy's unchecked <c>LoadFile</c> - and an assembly that cannot log cannot start logging by
    /// accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLibraryUnderTestReferencesNoDeferredCapabilityOrLoggingAssembly()
    {
        string[] referenced = ReferencedAssemblyNamesOfTheLibraryUnderTest();

        foreach (string name in referenced)
        {
            // The same MatchesForbiddenPrefix decision the self-check exercises, reached here for
            // the offending prefix so the message can name it.
            string? forbidden = ForbiddenAssemblyPrefixes.FirstOrDefault(
                prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

            Assert.Equal(forbidden is not null, MatchesForbiddenPrefix(name));

            Assert.True(
                forbidden is null,
                $"PowerFramework.Shared.Localization references '{name}', which matches the " +
                $"forbidden prefix '{forbidden}'. That is coupling to a capability this phase " +
                $"DEFERS - AAP 0.4.1 assigns the XML, JSON, ZIP and barcode object families to " +
                $"Documents - or to a logging stack AAP 0.5.3 excludes and the localization facade " +
                $"must not have. Constraint C-D forbids implementing a deferred service even " +
                $"partially, so remove the reference rather than relaxing this list. " +
                $"Full referenced set: [{string.Join(", ", referenced)}].");
        }
    }

    /// <summary>
    /// Proves the two structural guards above are NOT vacuous: they classify known-good names as
    /// permitted and known-bad names as violations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same class of failure the fixture guard in Region 0 defends against, applied to this
    /// region. Every assertion above is of the form "no referenced assembly is disallowed", and an
    /// allow-list predicate degraded to <c>return true</c> - or a deny-list quietly emptied - would
    /// satisfy all of them while checking nothing whatsoever. A structural gate that cannot fail is
    /// worse than no gate at all, because it reads as protection.
    /// </para>
    /// <para>
    /// So the predicates are exercised directly against names chosen to sit on each side of every
    /// boundary that matters, including the two <c>Microsoft.*</c> names the allow-list deliberately
    /// admits and the deny-list is the only thing catching.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStructuralGuardsRejectKnownViolations()
    {
        // ---- The allow-list admits the framework ----
        Assert.True(IsFrameworkAssembly("System.Runtime"));
        Assert.True(IsFrameworkAssembly("System.Xml.XDocument"));
        Assert.True(IsFrameworkAssembly("System.Xml.XPath.XDocument"));
        Assert.True(IsFrameworkAssembly("System"));
        Assert.True(IsFrameworkAssembly("netstandard"));
        Assert.True(IsFrameworkAssembly("mscorlib"));

        // ---- And rejects everything else, INCLUDING the one project reference that is separately
        // permitted by name. Keeping those two decisions independent is what stops the allow-list
        // from being widened by accident: PermittedProjectReference is an explicit exception, not a
        // shape the predicate recognises.
        Assert.False(IsFrameworkAssembly(PermittedProjectReference));
        Assert.False(IsFrameworkAssembly("Newtonsoft.Json"));
        Assert.False(IsFrameworkAssembly("ZXing.Net"));
        Assert.False(IsFrameworkAssembly("NPinyin.Core"));
        Assert.False(IsFrameworkAssembly("Serilog"));

        // Case matters for the allow-list, so a lookalike cannot sneak past it.
        Assert.False(IsFrameworkAssembly("system.runtime"));
        Assert.False(IsFrameworkAssembly("SystemLike.Parser"));
        Assert.False(IsFrameworkAssembly("NotSystem.Xml"));

        // ---- The deny-list does its UNIQUE work on Microsoft.* names, which the allow-list admits
        // wholesale. These two are the reason it exists at all.
        Assert.True(IsFrameworkAssembly("Microsoft.Extensions.Logging.Abstractions"));
        Assert.True(MatchesForbiddenPrefix("Microsoft.Extensions.Logging.Abstractions"));

        Assert.True(IsFrameworkAssembly("Microsoft.SqlServer.TransactSql.ScriptDom"));
        Assert.True(MatchesForbiddenPrefix("Microsoft.SqlServer.TransactSql.ScriptDom"));

        // ---- The deny-list also catches the third-party Documents-capability families, and matches
        // case-insensitively so a differently-cased or sub-namespaced variant cannot slip through.
        Assert.True(MatchesForbiddenPrefix("Newtonsoft.Json"));
        Assert.True(MatchesForbiddenPrefix("newtonsoft.json"));
        Assert.True(MatchesForbiddenPrefix("ICSharpCode.SharpZipLib"));
        Assert.True(MatchesForbiddenPrefix("ZXing.Net"));
        Assert.True(MatchesForbiddenPrefix("QRCoder"));
        Assert.True(MatchesForbiddenPrefix("HtmlAgilityPack"));
        Assert.True(MatchesForbiddenPrefix("NPinyin.Core"));
        Assert.True(MatchesForbiddenPrefix("OpenTelemetry.Api"));

        // ---- CRITICAL: the deny-list must NOT match the framework XML facades the substitution is
        // built on. If it did, the two assertions above this one would contradict each other and the
        // region could never pass - which is exactly why no forbidden prefix begins with "System.".
        Assert.False(MatchesForbiddenPrefix("System.Xml.XDocument"));
        Assert.False(MatchesForbiddenPrefix("System.Xml.XPath"));
        Assert.False(MatchesForbiddenPrefix("System.Xml.XPath.XDocument"));
        Assert.False(MatchesForbiddenPrefix("System.Xml.ReaderWriter"));
        Assert.False(MatchesForbiddenPrefix("System.Runtime"));
        Assert.False(MatchesForbiddenPrefix("System.Linq"));
        Assert.False(MatchesForbiddenPrefix(PermittedProjectReference));

        // And the real referenced set is non-trivial, so the loops above iterate over something.
        // A guard that runs zero iterations is the other way this region could go quietly vacuous.
        Assert.NotEmpty(ReferencedAssemblyNamesOfTheLibraryUnderTest());
    }

    /// <summary>
    /// Determines whether a referenced assembly name matches any forbidden prefix.
    /// </summary>
    /// <param name="name">The referenced assembly's simple name.</param>
    /// <returns><see langword="true"/> when the name is forbidden.</returns>
    /// <remarks>
    /// Extracted so that the deny-list decision has ONE implementation, shared by the assertion that
    /// applies it to the real assembly and the assertion that proves it is not vacuous. Two copies
    /// could drift, and the copy in the self-check is the one that would keep passing.
    /// </remarks>
    private static bool MatchesForbiddenPrefix(string name)
    {
        return ForbiddenAssemblyPrefixes.Any(
            prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Proves the library under test exposes no XML type on the public surface of the reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The last way Documents coupling could leak, and the one the reference table cannot see.
    /// Referencing the framework XML stack internally is mandated; handing an
    /// <see cref="XDocument"/>, an <see cref="XElement"/> or any other XML type BACK to a caller
    /// would be different in kind - it would make every consumer of this library an XML consumer
    /// too, and the deferred capability would spread by inheritance of the signature rather than by
    /// a package reference.
    /// </para>
    /// <para>
    /// So the reader's public surface is asserted to be exactly what AAP 0.2.1.3 Correction 6 leaves
    /// it as: strings in, a string out. Which is also why the surface is small enough to assert
    /// exhaustively - one constant, one constructor, one method - and asserting the COUNT is what
    /// makes a newly added member have to be considered rather than merely permitted.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReaderExposesNoXmlTypeOnItsPublicSurface()
    {
        Type readerType = typeof(I18nResourceReader);

        MethodInfo[] declaredMethods =
            [.. readerType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                            | BindingFlags.DeclaredOnly)];

        foreach (MethodInfo method in declaredMethods)
        {
            Assert.False(
                IsXmlType(method.ReturnType),
                $"'{method.Name}' returns the XML type '{method.ReturnType.FullName}'. The " +
                $"reader's public surface must be strings in and a string out, so that consuming " +
                $"this library does not make a caller a consumer of the deferred Documents " +
                $"capability by way of the signature.");

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.False(
                    IsXmlType(parameter.ParameterType),
                    $"'{method.Name}' takes the XML type '{parameter.ParameterType.FullName}' as " +
                    $"parameter '{parameter.Name}'. The reader's public surface must be strings in " +
                    $"and a string out.");
            }
        }

        // The surface, asserted exhaustively: one lookup method plus the compiler-generated property
        // accessors that the type does not have, so exactly one declared method.
        Assert.Single(declaredMethods);
        Assert.Equal(nameof(I18nResourceReader.Lookup), declaredMethods[0].Name);
        Assert.Equal(typeof(string), declaredMethods[0].ReturnType);
        Assert.Equal(3, declaredMethods[0].GetParameters().Length);
        Assert.All(
            declaredMethods[0].GetParameters(),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType));

        // No public field, property or event either - the loaded document stays private, so no
        // caller can reach the XML tree behind the reader.
        Assert.Empty(readerType.GetFields(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(readerType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly));
        Assert.Empty(readerType.GetEvents(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly));
    }

    /// <summary>
    /// Determines whether a type belongs to the XML object model.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns><see langword="true"/> when the type is an XML type.</returns>
    /// <remarks>
    /// Classified by namespace rather than by an enumerated type list, so that every member of
    /// <c>System.Xml</c> and its children - <c>System.Xml.Linq</c>, <c>System.Xml.XPath</c>,
    /// <c>System.Xml.Schema</c> - is covered without the list needing maintenance. Unwrapped first,
    /// so an array of XML types, a nullable one or a by-reference parameter is classified by its
    /// element type rather than escaping the check.
    /// </remarks>
    private static bool IsXmlType(Type type)
    {
        Type unwrapped = type;

        while (unwrapped.HasElementType)
        {
            Type? element = unwrapped.GetElementType();

            if (element is null)
            {
                break;
            }

            unwrapped = element;
        }

        string? typeNamespace = unwrapped.Namespace;

        if (typeNamespace is null)
        {
            return false;
        }

        return string.Equals(typeNamespace, "System.Xml", StringComparison.Ordinal)
            || typeNamespace.StartsWith("System.Xml.", StringComparison.Ordinal);
    }
}
