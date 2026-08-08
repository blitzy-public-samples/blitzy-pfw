// ---------------------------------------------------------------------------------------------
// I18nResourceReader.cs
// PowerFramework.Shared.Localization
// ---------------------------------------------------------------------------------------------
//
// WHY THIS FILE EXISTS AT ALL
// This type is not a convenience wrapper and it is not an abstraction added for tidiness. It
// exists because of one specific scope ruling, and that ruling is the whole design brief.
//
// The two concrete legacy locale providers read the translation table with the Documents XML
// parser, which this phase DEFERS:
//
//     ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru
//         L11-L14   type variables / protected: / privatewrite n_xmldoc _doc
//         L158      event constructor;call super::constructor;_doc = Create n_xmldoc
//         L159      _doc.LoadFile("pfw.i18n.xml")
//         L162      event destructor;call super::destructor;Destroy _doc
//         L51       sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",
//                                            sCat, text)).GetValueString( )
//
//     ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru
//         L13       privatewrite n_xmldoc _doc              (identical declaration)
//         L31       n_xmlqueryresult xqs                    (declared, never used - see below)
//         L52       ...string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)...
//         L62-L63   identical constructor load
//         L66       identical destructor
//
// AAP SECTION 0.2.1.3, CORRECTION 6 - the mandate, quoted in substance:
//     "Localization's XML dependency is substituted, not imported. The concrete providers read
//      the translation table with the deferred Documents XML parser ... Resolution: substitute
//      System.Xml.Linq.XDocument.Load plus XPathSelectElements. Localization therefore acquires
//      NO Documents coupling, and the XML object family stays deferred."
//
// (a) THE SUBSTITUTION, AND WHY IT IS MANDATORY - AAP 0.2.1.3 Correction 6 and 0.4.2.3
//     n_xmldoc            is replaced by System.Xml.Linq.XDocument
//     n_xmldoc.LoadFile   is replaced by System.Xml.Linq.XDocument.Load
//     n_xmldoc.Query      is replaced by the System.Xml.XPath.Extensions.XPathSelectElements
//                         extension method over XDocument
//     n_xmlqueryresult    has NO counterpart here, deliberately. The only in-scope declaration
//                         of it is n_cst_i18n_cht.sru:L31, which is dead: the variable xqs is
//                         declared and never assigned, read or destroyed. A dead declaration has
//                         no observable behaviour, so porting it would add a type this project
//                         must not have in order to reproduce nothing.
//     GetValueString      is replaced by reading the "to" attribute's Value - see Lookup.
//
//     Constraint C-D (AAP 0.7.3) forbids implementing the four deferred services "even
//     partially, even to stub them out". Documents owns the eleven-object XML family
//     (AAP 0.4.1). So the substitution is not a stylistic preference over porting n_xmldoc: a
//     type in this project that wrapped an XML document, or that carried a name or a shape
//     standing in for n_xmldoc or n_xmlqueryresult, would BE a partial Documents implementation
//     and would violate C-D. This file therefore uses the framework XML API directly and exposes
//     no XML type of its own on its public surface: the surface is a language token, a category
//     name, a source string, and a translated string out.
//
// (b) NO PACKAGE REFERENCE IS NEEDED, AND NONE MAY BE ADDED
//     System.Xml.Linq (XDocument, XElement, LoadOptions) and System.Xml.XPath
//     (XPathSelectElements, XPathException) both ship INSIDE the Microsoft.NETCore.App shared
//     framework that net10.0 already targets. PowerFramework.Shared.Localization.csproj therefore
//     carries ZERO PackageReference elements and states that zero is the correct count. If a
//     future change to this file appears to need an XML or XPath package, the substitution has
//     gone wrong: that package would be the first thread of coupling to the deferred capability
//     this phase must not implement.
//
// (c) THE RELATIVE FILENAME IS DELIBERATE LEGACY BEHAVIOUR, NOT AN OVERSIGHT
//     Both providers load the table by BARE RELATIVE FILENAME - LoadFile("pfw.i18n.xml") at
//     n_cst_i18n_en.sru:L159 and n_cst_i18n_cht.sru:L63. There is no directory, no probing, no
//     search order and no embedded resource anywhere in the legacy path. The port preserves that
//     resolution rule exactly: the name is resolved against the PROCESS WORKING DIRECTORY and
//     nowhere else. Specifically it does NOT probe AppContext.BaseDirectory, does NOT walk parent
//     directories, does NOT consult an environment variable or configuration key, and does NOT
//     fall back to an assembly resource. Preserving a resolution rule means preserving where the
//     lookup FAILS as much as where it succeeds; a probe added "to be helpful" would make the
//     port find files the legacy could not, which is a behavioural change (C-B).
//
// (d) THE LEGACY IGNORES THE LOAD RESULT, SO A MISSING FILE IS A MISS, NEVER A FAULT
//     n_xmldoc.LoadFile returns a value and NEITHER call site checks it - the constructor at
//     n_cst_i18n_en.sru:L158-L159 and at n_cst_i18n_cht.sru:L62-L63 simply proceeds. A later
//     Query against a document that never loaded yields the empty string, the provider's
//     "if sTo <> "" then" guard (n_cst_i18n_en.sru:L52) fails, the event returns 0, and the I18n
//     facade returns the source text unchanged. A missing or unreadable resource file therefore
//     degrades to EVERY LOOKUP MISSES and from there to silent passthrough - it never crashes and
//     it never reports anything.
//
//     That is reproduced here verbatim, and it is the ONE place in this refactor where the
//     fail-fast posture of AAP 0.1.4 deliberately does NOT apply. Fail-fast is correct for
//     Gateway's composition root, where pfwinitialize/pfwfinalize pairing is structural and
//     pfw.sra's systemerror event ends in HALT CLOSE. It is wrong here, because the legacy
//     demonstrably does not fail fast on this path, and turning a documented silent degradation
//     into a startup fault would be a behavioural change dressed up as robustness. Construction
//     therefore never throws for a data condition, and neither does a lookup.
//
// WHAT THIS FILE MUST NOT DO - the read-only rulings that bind it
//     C-C (AAP 0.7.3): the legacy tree is read-only and is the behavioural oracle.
//     pfw.i18n.xml sits inside that region, so it is opened READ-ONLY and is never written,
//     re-encoded, reformatted, normalized or saved. There is no Save call, no write path and no
//     temporary file anywhere in this type. It is also NOT copied or embedded into this project:
//     the csproj deliberately declares no Content, None or EmbeddedResource item naming it.
//
//     The two mistranslations that AAP 0.8.2 requires preserved are DATA defects inside that
//     read-only file, not code defects:
//         pfw.i18n.xml:L4    tr text="最小化" to="Maximize"          (should read Minimize)
//         pfw.i18n.xml:L49   tr text="隐藏"   to="Htexte details"    (should read Hide details)
//     The correct wording survives only in the commented-out pre-XML table at
//     n_cst_i18n_en.sru:L58-L153 - see L64-L65 and L149-L150 - and reviving it is exactly the
//     silent correction the requirements forbid. Because the data is read-only, faithful
//     behaviour is achieved simply by READING HONESTLY. That makes "do not repair on read" the
//     single most important property of this file: values are returned byte for byte, with no
//     correction, no normalization, no trimming, no casing change and no culture applied.
//
// FILE I/O BOUNDARY - AAP 0.1.5 and 0.2.1.1
//     AAP 0.1.5 requires the shared layer beneath the four services to carry "only pure
//     behaviour and no I/O". This type is the single, narrow, AAP-mandated exception, and the
//     exception is exactly one read of exactly one file. No directory is enumerated, no second
//     path is touched, nothing is written, and no other file in this project performs I/O.
//
// THREADING AND LIFETIME
//     One document is loaded once, in the constructor, into a readonly field, and is never
//     mutated afterwards - mirroring the legacy's Create-in-constructor / Destroy-in-destructor
//     lifetime, where each provider instance owns its own _doc. Lookups perform read-only
//     traversal of that immutable tree. There is no reload, no file watcher, no cache
//     invalidation and no expiry, because the legacy has none of those; the resource file is
//     read once per reader instance and a later edit on disk is not observed.
//
//     No IDisposable. XDocument holds no unmanaged handle once loaded - the only handle is the
//     FileStream opened and closed inside the constructor - so the legacy destructor's
//     "Destroy _doc" has no managed analogue beyond letting the object be collected. Making this
//     type disposable would invent a lifetime contract the legacy does not have and would put a
//     disposal obligation on every provider that holds one.
//
// DELIBERATELY ABSENT, so the omissions read as decisions rather than gaps
//     * A second, richer lookup API. One method, three parameters, one string out. In
//       particular there is no overload taking a pre-built XPath expression: that would put two
//       ways to ask the same question on the surface and would leak the query language to the
//       callers.
//     * Any "did the resource load" or "how many entries" property. The legacy CANNOT branch on
//       load success, because it never checks (see (d) above). Exposing that state here would let
//       a caller take a decision the legacy has no way to take, and the first such caller would
//       be a behavioural divergence (C-B).
//     * Caching, memoization or a lookup index. The legacy re-evaluates the XPath on every
//       translate, so this does too. AAP 0.1.2 and 0.8.5 are explicit that this is not a
//       performance refactor and that no performance objective may be asserted, so there is
//       nothing here to optimise against.
//     * Schema or DTD validation, an IStringLocalizer adapter, a CultureInfo mapping, logging,
//       telemetry, metrics, a lang="chs" code path, or any static or shared mutable state.
//       On the chs point specifically: pfw.i18n.xml contains ONLY lang="en" and lang="cht"
//       sections - verified across all 151 lines - which is precisely why the Simplified Chinese
//       provider is a genuine no-op and needs no resource document. Completing the file's
//       language coverage here would be inventing data.
// ---------------------------------------------------------------------------------------------

using System.IO;
using System.Security;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PowerFramework.Shared.Localization;

/// <summary>
/// Reads translated strings out of the PowerFramework localization resource table, the
/// <c>pfw.i18n.xml</c> document at the repository root.
/// </summary>
/// <remarks>
/// <para>
/// This is the managed substitute for the legacy <c>n_xmldoc</c> load and XPath query performed
/// inside the two concrete locale providers, <c>n_cst_i18n_en</c> and <c>n_cst_i18n_cht</c>. The
/// substitution is mandated by AAP section 0.2.1.3 Correction 6 so that this library acquires no
/// coupling to the deferred Documents XML object family; see the commentary at the head of this
/// file for the full ruling and its consequences.
/// </para>
/// <para>
/// The reader carries no locale of its own. The language token is a parameter of
/// <see cref="Lookup"/> precisely because the two legacy providers differ in nothing else: their
/// XPath expressions are character-for-character identical apart from <c>'en'</c> versus
/// <c>'cht'</c>. Keeping the token on the call lets both providers share one reader without
/// duplicating the query, while each keeps ownership of its own token.
/// </para>
/// <para>
/// Every failure mode resolves to the empty string rather than to an exception, because that is
/// what the legacy does: a missing resource file, an unparseable one, an unknown language, an
/// unknown category and an untranslated source string are all indistinguishable at the call site
/// and all fall through to the caller's own passthrough. Construction never throws for a data
/// condition.
/// </para>
/// <para>
/// Instances are cheap to hold and immutable once constructed. Hold one per provider, mirroring
/// the legacy's one <c>_doc</c> per provider instance.
/// </para>
/// </remarks>
public sealed class I18nResourceReader
{
    /// <summary>
    /// The resource file name both legacy providers hardcode:
    /// <c>_doc.LoadFile("pfw.i18n.xml")</c> at <c>n_cst_i18n_en.sru:L159</c> and at
    /// <c>n_cst_i18n_cht.sru:L63</c>.
    /// </summary>
    /// <remarks>
    /// Exposed as a constant so that the providers and the sibling test project name the same
    /// literal the legacy names, rather than each repeating it. It is a bare file name and not a
    /// path: see <see cref="I18nResourceReader(string)"/> for the resolution rule.
    /// </remarks>
    public const string DefaultResourceFileName = "pfw.i18n.xml";

    /// <summary>
    /// The attribute carrying the translated text on a <c>tr</c> entry. It is the attribute the
    /// legacy XPath terminates in: <c>.../tr[@text='...']/@to</c>.
    /// </summary>
    private const string TranslationAttributeName = "to";

    /// <summary>
    /// The loaded resource table, or <see langword="null"/> when the file could not be read or
    /// parsed.
    /// </summary>
    /// <remarks>
    /// A null document is not an error state to be reported; it is the reproduction of the legacy
    /// unchecked <c>LoadFile</c>, where a document that never loaded simply makes every
    /// subsequent query return the empty string. Deliberately kept private with no accompanying
    /// status property, so that no caller can branch on something the legacy cannot see.
    /// </remarks>
    private readonly XDocument? _document;

    /// <summary>
    /// Loads the localization resource table once, resolving <paramref name="fileName"/> against
    /// the process working directory.
    /// </summary>
    /// <param name="fileName">
    /// The resource file name, defaulting to <see cref="DefaultResourceFileName"/> - the literal
    /// both legacy providers use. It is resolved relative to the process working directory,
    /// exactly as the legacy <c>LoadFile</c> resolves it; no other location is probed. The
    /// parameter exists so that a test can point the reader at a fixture in its own working
    /// directory without any path arithmetic, which is what keeps this type coverable without
    /// depending on a fixed absolute path. It is not a configuration hook, and supplying a path
    /// does not change the resolution rule.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="fileName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fileName"/> is empty or consists only of white space.
    /// </exception>
    /// <remarks>
    /// The two guards above are caller-contract violations and are deliberately distinct from the
    /// data-availability path: a resource file that is merely absent, unreadable or malformed does
    /// NOT throw, because the legacy ignores its load result (see note (d) at the head of this
    /// file). Neither legacy provider can reach either guard, since both pass the hardcoded
    /// literal. Note that <see cref="ArgumentNullException"/> derives from
    /// <see cref="ArgumentException"/>, so a caller that wants to treat both alike need only catch
    /// the latter.
    /// </remarks>
    public I18nResourceReader(string fileName = DefaultResourceFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _document = TryLoadResourceTable(fileName);
    }

    /// <summary>
    /// Returns the translation recorded for <paramref name="text"/> in the given language and
    /// category, or the empty string when there is none.
    /// </summary>
    /// <param name="language">
    /// The language token as it appears in the resource table's <c>lang</c> attribute -
    /// <c>"en"</c> for <c>n_cst_i18n_en</c> and <c>"cht"</c> for <c>n_cst_i18n_cht</c>. The table
    /// carries no other value; there is no <c>"chs"</c> section, which is why the Simplified
    /// Chinese provider needs no reader at all.
    /// </param>
    /// <param name="category">
    /// The category element name the provider has already mapped its numeric category onto -
    /// one of <c>"window"</c>, <c>"splitcontainer"</c>, <c>"tabcontrol"</c>, <c>"ribbonbar"</c>,
    /// <c>"msgbox"</c> or <c>"dwsvc"</c>. The mapping itself stays with the provider, where the
    /// legacy keeps it (<c>n_cst_i18n_en.sru:L34-L47</c>), because it is the provider that knows
    /// the category constants.
    /// </param>
    /// <param name="text">
    /// The source text to translate, matched against the <c>text</c> attribute of a <c>tr</c>
    /// entry. Treated as an opaque string: brace characters and every other character are matched
    /// literally and never interpreted.
    /// </param>
    /// <returns>
    /// The value of the entry's <c>to</c> attribute, byte for byte as the resource file records
    /// it, or <see cref="string.Empty"/> when no entry matches. Never <see langword="null"/>, and
    /// this method never throws.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The empty string is the miss signal, and it is the legacy's own. The XPath the legacy
    /// evaluates is wrapped in the <c>string(...)</c> function, which yields the empty string for
    /// an empty node set, and the provider tests exactly that:
    /// <c>if sTo &lt;&gt; "" then text = sTo; return 1</c>
    /// (<c>n_cst_i18n_en.sru:L52-L55</c>). A miss is the ordinary path, not an exceptional one -
    /// every source other than the framework's own, every unmapped category and every
    /// untranslated string reaches it - so returning null or throwing would convert a silent
    /// fall-through into a failure.
    /// </para>
    /// <para>
    /// Values are returned exactly as recorded. Nothing here trims, cases, folds, culture-maps,
    /// formats or repairs them, which is what preserves the two documented mistranslations at
    /// <c>pfw.i18n.xml:L4</c> and <c>:L49</c> and keeps the braces in an entry such as
    /// <c>:L52</c> intact for a later <c>Sprintf</c> at the call site to fill.
    /// </para>
    /// <para>
    /// Degenerate arguments are not guarded, because leaving them unguarded is what reproduces the
    /// legacy result. Each falls through the same unescaped interpolation the legacy performs and
    /// each was verified against the legacy expression over the real resource table: an empty
    /// language yields a well-formed expression that matches nothing; an empty source text
    /// likewise, and it would match an entry whose <c>text</c> attribute were empty in both the
    /// legacy and here, so the two cannot diverge; an empty category yields a malformed expression
    /// that the legacy guards upstream at <c>n_cst_i18n_en.sru:L49</c>; and a source text
    /// containing an apostrophe also yields a malformed expression, which is the legacy's own
    /// injection analogue. All four return <see cref="string.Empty"/>. A
    /// <see langword="null"/> argument forced past nullable reference analysis behaves as the
    /// empty string, because that is how interpolation renders it, so it too is a miss rather than
    /// a throw.
    /// </para>
    /// </remarks>
    public string Lookup(string language, string category, string text)
    {
        // Reproduces a Query against a document that never loaded: the legacy does not check
        // LoadFile's result, so this is a miss and not a fault. See note (d) in the file header.
        if (_document is null)
        {
            return string.Empty;
        }

        // ---------------------------------------------------------------------------------
        // The legacy expression, verbatim in shape:
        //
        //     n_cst_i18n_en.sru:L51
        //         Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)", sCat, text)
        //     n_cst_i18n_cht.sru:L52
        //         Sprintf("string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)", sCat, text)
        //
        // Two deliberate reproductions, both load-bearing:
        //
        // 1. THE INTERPOLATION IS UNESCAPED, exactly as the legacy Sprintf leaves it. No quote
        //    escaping, no XPath quoting helper and no sanitiser is applied to language, category
        //    or text. This is the legacy's injection analogue and it is preserved rather than
        //    corrected, because correcting it would change observable results: a source string
        //    containing an apostrophe produces a malformed expression, the query breaks, and the
        //    caller sees a miss. That outcome is reproduced below by catching XPathException and
        //    returning the empty string, which lands the caller in the same silent passthrough
        //    the legacy lands it in. An empty category behaves the same way, producing
        //    "pfw/[@lang='en']/..." - which is why the legacy guards it upstream with
        //    "if sCat <> "" then" at n_cst_i18n_en.sru:L49 and no guard is duplicated here.
        //    Documented at its point of reproduction per constraint C-K.
        //
        // 2. THE string(...) WRAPPER IS EXPRESSED AS A LOCATION PATH plus a first-node read,
        //    which is what lets XPathSelectElements - the substitute AAP 0.2.1.3 Correction 6 and
        //    0.4.2.3 name - carry semantics identical to the legacy's string() call. XPath
        //    string(node-set) takes the string value of the FIRST node in document order and
        //    yields "" for an empty node set; FirstOrDefault plus "?? string.Empty" below is that
        //    rule, term for term. Reading the "to" attribute off the selected tr element rather
        //    than selecting "/@to" is what keeps the expression an element path, and it also
        //    preserves the case where a tr entry carries no "to" attribute at all: the legacy
        //    string() of that empty attribute node set is "", and Attribute(...) returning null
        //    gives "". Verified equivalent against every entry class in the table - both
        //    mistranslations, the brace entry, all three miss kinds, an empty language, an empty
        //    text, an empty category and an apostrophe-bearing text.
        // ---------------------------------------------------------------------------------
        string expression = $"pfw/{category}[@lang='{language}']/tr[@text='{text}']";

        try
        {
            XElement? entry = _document.XPathSelectElements(expression).FirstOrDefault();

            // No repair on read (constraint C-C): whatever the attribute says is what the caller
            // gets. The only transformation applied to the value is the attribute-value
            // normalization the XML specification itself mandates of any conforming parser -
            // entity references resolved, a literal carriage return, line feed or tab inside an
            // attribute value folded to a space. No value in pfw.i18n.xml contains a character
            // that normalization touches, so no value is altered by it.
            return entry?.Attribute(TranslationAttributeName)?.Value ?? string.Empty;
        }
        catch (XPathException)
        {
            // A malformed expression - see reproduction note 1 above. The legacy's query breaks
            // in exactly the same inputs and the caller sees no translation, so a miss is the
            // faithful result. Never rethrown: this method is on the localization path, whose
            // documented posture is silent passthrough.
            return string.Empty;
        }
    }

    /// <summary>
    /// Opens the resource file read-only, parses it once, and returns
    /// <see langword="null"/> if it cannot be read or parsed.
    /// </summary>
    /// <param name="fileName">
    /// The bare resource file name, resolved against the process working directory.
    /// </param>
    /// <returns>
    /// The parsed document, or <see langword="null"/> when the file is absent, unreadable or not
    /// well-formed XML.
    /// </returns>
    /// <remarks>
    /// Returning null rather than throwing is the reproduction of the legacy unchecked
    /// <c>LoadFile</c> - note (d) in the file header. It is behaviour preservation, not defensive
    /// programming, and it is the single documented exception to this refactor's fail-fast
    /// posture.
    /// </remarks>
    private static XDocument? TryLoadResourceTable(string fileName)
    {
        try
        {
            // THE RESOLUTION RULE, stated in code rather than left to a library default:
            // Path.GetFullPath resolves a relative name against the process working directory and
            // nothing else, which is precisely how the legacy LoadFile("pfw.i18n.xml") resolves
            // it. Nothing probes AppContext.BaseDirectory, walks a parent directory, consults
            // configuration or falls back to an assembly resource. See note (c) in the file
            // header.
            string resolvedPath = Path.GetFullPath(fileName);

            // READ-ONLY BY CONSTRUCTION, and provably so at this one call site: FileMode.Open
            // never creates, FileAccess.Read makes a write physically impossible on this handle,
            // and FileShare.Read admits other readers while excluding writers for the duration of
            // the parse. pfw.i18n.xml is inside the read-only legacy region (constraint C-C), and
            // this is the only file this project opens.
            //
            // The handle is opened and closed here rather than being held, so the parsed document
            // outlives no operating-system resource - which is why this type needs no
            // IDisposable.
            //
            // Loading from a stream, rather than handing the file name to the XML loader, is
            // deliberate on two counts. It keeps the working-directory resolution above explicit
            // and auditable instead of delegating it to URI resolution, which would additionally
            // give characters such as '#' a meaning in a file name that they do not have on
            // disk. And the resulting document carries no base URI, so no relative external
            // reference could be resolved even if the table ever declared one; the loader is
            // given no resolver either.
            //
            // LoadOptions.None is stated explicitly: no line information is captured, insignificant
            // whitespace is not preserved and no base URI is recorded. Nothing here needs any of
            // them, and naming the value keeps a future reader from assuming otherwise.
            using FileStream stream = new(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);

            return XDocument.Load(stream, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException
                                                   or IOException
                                                   or UnauthorizedAccessException
                                                   or ArgumentException
                                                   or NotSupportedException
                                                   or SecurityException)
        {
            // Every member of this set is a "the table is not available" condition, and every one
            // of them must land the reader in the same place the legacy lands: a document that
            // never loaded, so every lookup misses and the facade passes the source text through
            // unchanged.
            //
            //   XmlException                    the file exists but is not well-formed XML
            //   IOException                     absent file, absent directory, over-long path,
            //                                   locked file, or any other I/O failure -
            //                                   FileNotFoundException, DirectoryNotFoundException
            //                                   and PathTooLongException all derive from it
            //   UnauthorizedAccessException     permission denied, or the name resolves to a
            //                                   directory; note it does NOT derive from
            //                                   IOException and so must be named in its own right
            //   ArgumentException               the name is not a legal path, for instance because
            //                                   it embeds a null character
            //   NotSupportedException           the name is in a form this platform cannot open
            //   SecurityException               the host denies file access to this code
            //
            // The set is enumerated rather than caught as a bare Exception so that a genuinely
            // unexpected failure - an OutOfMemoryException, say - still propagates instead of
            // being silently reinterpreted as an untranslated resource table.
            return null;
        }
    }
}
