// ==================================================================================================
//  LookupKeyFormatTests.cs - THE LOOKUP KEY'S CONSTRUCTION, AND ITS UNESCAPED INTERPOLATION
//  ------------------------------------------------------------------------------------------------
//  UNITS UNDER TEST  PowerFramework.Shared.Kernel.Formatting.Sprintf                 (the composer)
//                    PowerFramework.Shared.Localization.EnglishProvider.OnTranslate  (the en caller)
//                    PowerFramework.Shared.Localization.TraditionalChineseProvider.OnTranslate
//                    PowerFramework.Shared.Localization.I18nResourceReader.Lookup    (the resolver)
//  ORACLES           ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_en.sru:L51
//                    ws_objects/pfw.ui.controls.ext.pbl.src/n_cst_i18n_cht.sru:L52
//                    ws_objects/pfw.common.pbl.src/sprintf.srf          (the 20 native overloads)
//                    ws_objects/pfw.datawindow.services.pbl.src/n_cst_dwsvc_rowselect.sru:L239
//                    pfw.i18n.xml                       (the shipped table, read-only under C-C)
//
//  THE TWO STATEMENTS THIS FILE FREEZES
//  ------------------------------------------------------------------------------------------------
//      n_cst_i18n_en.sru:L51
//          sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='en']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
//      n_cst_i18n_cht.sru:L52
//          sTo = _doc.Query(Sprintf("string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)",sCat,text)).GetValueString( )
//
//  Four facts read straight off them, and every one of them is asserted below:
//
//    1. TWO SEQUENTIAL EMPTY-BRACE PLACEHOLDERS, IN A FIXED ORDER. The FIRST receives the category
//       ELEMENT NAME (sCat, already mapped from the numeric category at :L34-L47); the SECOND
//       receives the SOURCE TEXT. The order is part of the contract, not an accident of writing:
//       transposed, the key addresses an element named after the caller's text and compares the
//       element name against a tr entry, so EVERY lookup misses. PART 1 pins both the order and
//       that consequence.
//    2. THE PREDICATE IS SINGLE-QUOTED - tr[@text='{}'], and the language filter [@lang='en']
//       likewise. Which character is dangerous follows entirely from that choice of quote. PART 2.
//    3. NOTHING IS APPLIED TO THE SOURCE TEXT BEFORE SUBSTITUTION. No escaping, no quoting, no
//       entity encoding, no percent encoding, no validation, no rejection. PART 2.
//    4. cht DIFFERS FROM en IN THE LANGUAGE TOKEN AND IN NOTHING ELSE. Byte for byte the two format
//       strings are the same but for 'en' against 'cht'. PART 1.
//
//  THE INTERPOLATION IS UNESCAPED, AND THAT IS WHAT IS ASSERTED (constraint C-B)
//  ------------------------------------------------------------------------------------------------
//  This suite asserts the OBSERVED legacy behaviour, never a hardened one. No assertion anywhere in
//  this file expects the source text to be escaped, doubled, entity-encoded, percent-encoded,
//  stripped, quoted or rejected, and none may ever be added: an assertion that only passed once the
//  interpolation was made safe would encode a behavioural change this refactor forbids. AAP 0.1.5's
//  rule is the one that governs - an implementation may be safer than the legacy only where the
//  change is UNOBSERVABLE, and the composed key is observable, so it must match the legacy character
//  for character. This is the localization-side counterpart of the SQL interpolation exposure AAP
//  0.6.4 documents, and it is handled the same way: pinned and documented, never quietly fixed.
//
//  WHY THE APOSTROPHE, AND WHY THE DOUBLE QUOTE PROVES NOTHING
//  ------------------------------------------------------------------------------------------------
//  Because the predicate is written with SINGLE quotes, only an apostrophe in the source text can
//  leave the string context. A double quote is just one more ordinary character inside a
//  single-quoted literal. Both rows are carried below, and the difference between them is asserted
//  rather than asserted about, by handing each composed key to the framework's own XPath engine:
//
//      text = "it's"        ->  string(pfw/dwsvc[@lang='en']/tr[@text='it's']/@to)
//                              REJECTED - XPathException. The apostrophe closes the literal early
//                              and the remainder is not a legal expression. HAZARDOUS.
//      text = "say \"hi\""  ->  string(pfw/dwsvc[@lang='en']/tr[@text='say "hi"']/@to)
//                              WELL-FORMED, and evaluates to the empty string: an ordinary miss.
//                              HARMLESS - a control, and the reason it is here.
//
//  A suite that reached for a double quote as its "quote character" row would be testing the one
//  character that cannot break this predicate, and would report confidence it had not earned. That
//  is the single most important detail in this file.
//
//  WHERE THE KEY IS PROVED, GIVEN THE PORT DOES NOT PUBLISH ONE
//  ------------------------------------------------------------------------------------------------
//  I18nResourceReader.Lookup takes the expression's three variable parts and walks the location path
//  with LINQ to XML; it never assembles a query string and publishes no member that accepts or
//  returns one. The key is therefore NOT reachable through the system under test, and the system
//  under test is NOT modified to expose it - a test may not reshape its subject for its own
//  convenience, and shared/PowerFramework.Shared.Localization/** is untouched by this file.
//
//  So the key is proved at the two levels that remain, and both are asserted for every row:
//
//    (a) THE COMPOSED KEY. The legacy format string is carried here verbatim and driven through
//        Kernel's Formatting.Sprintf - the same formatter, the same dialect, the same argument
//        order the providers use. This proves what the legacy renders and that the renderer applies
//        no escaping. It asserts nothing about the reader's internals, and it cannot: it is a
//        statement about the FORMAT and the FORMATTER.
//    (b) THE OBSERVABLE CONSEQUENCE. The composed key is then evaluated against the read-only
//        oracle table with System.Xml.XPath, and the port's answer for the same inputs is required
//        to agree. Where the legacy expression is malformed the legacy answered a miss - its
//        n_xmldoc.Query yielded an empty result, the `if sTo <> ""` gate at n_cst_i18n_en.sru:L52
//        declined, and the event fell through to `return 0` at :L155 - so the port must likewise
//        answer not-handled with the caller's text untouched and nothing thrown.
//
//  Measured, not assumed: no exception escapes I18nResourceReader.Lookup or either provider for any
//  input below, so the reader's documented miss-is-not-an-exception contract - the same contract
//  I18nResourceReaderTests.cs asserts - holds, and there is no divergence to report.
//
//  BOUNDARIES WITH THE SIBLING SUITES - NEITHER GAP NOR DUPLICATION
//  ------------------------------------------------------------------------------------------------
//  shared/PowerFramework.Shared.Kernel.Tests/SprintfTests.cs OWNS THE SPRINTF GRAMMAR. Sequential
//  {} substitution, one-based {N} indices, {N,alignment}, {N:mask}, the negative results that %s and
//  %d are not placeholders and that {0} is not a legacy placeholder, culture invariance and the
//  backslash brace escape are all pinned there, and that suite already uses this very XPath as its
//  realistic case. NOTHING IN THIS FILE RE-TESTS THE FORMATTER. What this file adds is the
//  LOCALIZATION-LEVEL key: which format string each provider uses, which argument goes in which
//  position, and what happens to a source text carried through it unescaped.
//
//  I18nXPathInjectionTests.cs OWNS THE CRAFTED-PAYLOAD OUTCOMES - malformed, would-be tautology and
//  would-be union keys, catalogued against the value the legacy expression produced for each. This
//  file is upstream of that one: it fixes the SHAPE of the key those payloads are carried by, adds
//  the double-quote control that separates "contains a metacharacter" from "escapes the literal",
//  and asserts the composed key STRING that no other suite in the project inspects.
//
//  RULES POSITION
//  ------------------------------------------------------------------------------------------------
//  review_rules returns exactly "No user rules provided.", verified for this file. No user rule
//  governs it, none is inferred, and the enterprise baseline of AAP 0.7.2 applies in their place.
//  The binding constraints cited inline are AAP 0.7.3's: C-B (preserve behaviour, correct no legacy
//  defect), C-C (the legacy tree and pfw.i18n.xml are read-only - this file only ever READS the
//  table and adds no entry to make a row pass), C-H (nullable, warnings as errors, coverage) and
//  C-K (document every technology-specific and boundary-specific decision). Table-driven theories
//  with member data are the shape AAP 0.6.7 asks parity matrices to take.
// ==================================================================================================

using System;
using System.IO;
using System.Xml.XPath;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Localization.Tests;

/// <summary>
/// Tests pinning how the localization lookup key is built - the two format strings, the fixed
/// argument order, the language token that is the only difference between the providers - and
/// pinning that the source text travels through that key completely unescaped.
/// </summary>
public class LookupKeyFormatTests
{
    // ==============================================================================================
    //  THE FORMAT STRINGS, CARRIED VERBATIM FROM THE ORACLE
    // ==============================================================================================

    /// <summary>
    /// The English provider's key format, byte for byte as <c>n_cst_i18n_en.sru:L51</c> writes it.
    /// </summary>
    /// <remarks>
    /// Transcribed rather than derived. A helper that assembled this string from parts would pass
    /// whatever it built, which is the one thing a format-string assertion must not do: the literal
    /// is the assertion. The braces are the PowerFramework dialect's sequential placeholders, not
    /// .NET composite-formatting holes - <c>{0}</c> would be a literal here, which is exactly what
    /// <c>SprintfTests.cs</c> pins and what this file therefore does not restate.
    /// </remarks>
    private const string EnglishKeyFormat = "string(pfw/{}[@lang='en']/tr[@text='{}']/@to)";

    /// <summary>
    /// The Traditional Chinese provider's key format, byte for byte as <c>n_cst_i18n_cht.sru:L52</c>
    /// writes it. Identical to <see cref="EnglishKeyFormat"/> but for the language token.
    /// </summary>
    private const string TraditionalChineseKeyFormat = "string(pfw/{}[@lang='cht']/tr[@text='{}']/@to)";

    /// <summary>
    /// Both key formats, for the assertions that must hold of each.
    /// </summary>
    /// <remarks>
    /// Held as a field rather than written inline at each use so that a third locale format, should
    /// one ever be added, is covered by every such assertion the moment it joins this list.
    /// </remarks>
    private static readonly string[] BothKeyFormats = [EnglishKeyFormat, TraditionalChineseKeyFormat];

    /// <summary>The language token the English provider selects on.</summary>
    private const string EnglishLanguageToken = "en";

    /// <summary>The language token the Traditional Chinese provider selects on.</summary>
    private const string TraditionalChineseLanguageToken = "cht";

    // ==============================================================================================
    //  CATEGORY ELEMENT NAMES - the FIRST placeholder's argument
    // ==============================================================================================

    /// <summary>
    /// The element name <c>Categories.CAT_DWSVC</c> maps onto at <c>n_cst_i18n_en.sru:L45-L46</c>.
    /// </summary>
    /// <remarks>
    /// The live one in this phase: the DataWindow service layer reaches it from the row-selection
    /// report at <c>n_cst_dwsvc_rowselect.sru:L239</c> and from the item-validation path, so rows
    /// built on it exercise a real code path rather than a hypothetical one.
    /// </remarks>
    private const string DataWindowServiceElement = "dwsvc";

    /// <summary>
    /// The element name both <c>Enums.I18N_CAT_WINDOW</c> and <c>Enums.I18N_CAT_DATAWINDOW</c> map
    /// onto at <c>n_cst_i18n_en.sru:L35-L36</c>.
    /// </summary>
    private const string WindowElement = "window";

    /// <summary>
    /// The element name <c>Categories.CAT_MSGBOX</c> maps onto at
    /// <c>n_cst_i18n_en.sru:L43-L44</c>.
    /// </summary>
    private const string MessageBoxElement = "msgbox";

    // ==============================================================================================
    //  THE SOURCE TEXTS - the SECOND placeholder's argument
    // ==============================================================================================

    /// <summary>
    /// The hazardous input: a source text containing an APOSTROPHE.
    /// </summary>
    /// <remarks>
    /// The one character class that can leave the single-quoted predicate of both format strings.
    /// Interpolated it yields <c>tr[@text='it's']</c>, whose literal closes after <c>it</c> and
    /// leaves <c>s'</c> dangling, so the expression is no longer legal XPath. An ordinary English
    /// possessive is enough - no crafted payload is needed to reach this, which is precisely why it
    /// matters.
    /// </remarks>
    private const string ApostropheSourceText = "it's";

    /// <summary>
    /// The harmless control: a source text containing DOUBLE QUOTES.
    /// </summary>
    /// <remarks>
    /// Carried specifically so the asymmetry is visible in the code rather than only in a comment.
    /// A double quote inside a single-quoted XPath literal is an ordinary character; the composed
    /// key stays well-formed and merely fails to match. A suite that used this as its
    /// "quote character" case would be exercising the wrong character.
    /// </remarks>
    private const string DoubleQuoteSourceText = "say \"hi\"";

    /// <summary>
    /// A source text carrying the XPath and location-path metacharacters that are NOT quotes.
    /// </summary>
    /// <remarks>
    /// Brackets, an attribute sigil, a step separator, parentheses and a space - every one of them
    /// syntactically significant in an XPath expression, and every one of them inert inside a
    /// string literal. None of them appears as a <c>text</c> attribute in the table, so the lookup
    /// is an ordinary miss.
    /// </remarks>
    private const string SpecialCharacterSourceText = "[a]@b/c (d)";

    /// <summary>
    /// The source text of the entries at <c>pfw.i18n.xml:L52</c> and <c>:L127</c>, which carry
    /// literal braces in BOTH the key text and the translated value.
    /// </summary>
    /// <remarks>
    /// The conflation hazard this file exists partly to pin: the same <c>{}</c> token is a
    /// placeholder in the FORMAT STRING and inert DATA in the resource table. It must be matched and
    /// returned literally at this layer; the substitution that fills it happens later and elsewhere.
    /// </remarks>
    private const string BracedSourceText = "第{}行";

    /// <summary>
    /// The English translation recorded at <c>pfw.i18n.xml:L52</c> - braces intact.
    /// </summary>
    private const string BracedEnglishTranslation = "Line {}";

    /// <summary>
    /// A source text present in the table under <c>window</c> in both languages, used for the rows
    /// whose point is that a well-formed key still resolves.
    /// </summary>
    private const string PresentWindowSourceText = "关闭";

    /// <summary>The English translation of <see cref="PresentWindowSourceText"/>.</summary>
    private const string PresentWindowEnglishTranslation = "Close";

    /// <summary>The Traditional Chinese translation of <see cref="PresentWindowSourceText"/>.</summary>
    private const string PresentWindowTraditionalChineseTranslation = "關閉";

    /// <summary>
    /// The message a fixture-guard failure carries.
    /// </summary>
    /// <remarks>
    /// Without the table every lookup answers empty, and a suite that could not tell a miss from a
    /// hit would pass while asserting nothing. The table reaches this project's output through the
    /// single <c>Content</c> item declared by
    /// <c>PowerFramework.Shared.Localization.csproj</c> and flowing down the project reference;
    /// nothing here declares a second copy of it.
    /// </remarks>
    private const string FixtureMissingMessage =
        "pfw.i18n.xml is not in the test working directory, so every lookup would answer empty and "
            + "this suite could not distinguish a miss from a hit. Restore the Content item in "
            + "PowerFramework.Shared.Localization.csproj, which flows to this project through the "
            + "ProjectReference.";

    // ==============================================================================================
    //  HELPERS
    // ==============================================================================================

    /// <summary>
    /// Composes the lookup key exactly as the legacy providers do.
    /// </summary>
    /// <param name="format">
    /// <see cref="EnglishKeyFormat"/> or <see cref="TraditionalChineseKeyFormat"/>.
    /// </param>
    /// <param name="categoryElement">The category element name - the FIRST placeholder's argument.</param>
    /// <param name="sourceText">The source text - the SECOND placeholder's argument.</param>
    /// <returns>The composed key.</returns>
    /// <remarks>
    /// The argument order here IS the contract under test, which is why this helper takes the two
    /// values as separate parameters in the legacy's own order and forwards them positionally. It
    /// applies nothing to either argument on the way through - no escaping, no quoting, no
    /// validation - because neither does <c>n_cst_i18n_en.sru:L51</c>.
    /// </remarks>
    private static string ComposeKey(string format, string categoryElement, string sourceText)
    {
        return Formatting.Sprintf(format, categoryElement, sourceText);
    }

    /// <summary>
    /// Reports whether <paramref name="key"/> is a legal XPath expression.
    /// </summary>
    /// <param name="key">The composed lookup key.</param>
    /// <returns>
    /// <see langword="true"/> when the framework's XPath engine accepts the expression; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This is how the apostrophe's hazard is demonstrated instead of merely described. The engine
    /// is the arbiter of whether an interpolated key is still the expression its author wrote, and
    /// asking it is the closest a managed test can stand to the legacy's own
    /// <c>n_xmldoc.Query</c>. The catch is narrow on purpose: <see cref="XPathException"/> is what
    /// a malformed expression raises, and nothing else is swallowed.
    /// </remarks>
    private static bool IsLegalXPath(string key)
    {
        try
        {
            XPathExpression.Compile(key);
            return true;
        }
        catch (XPathException)
        {
            return false;
        }
    }

    /// <summary>
    /// Evaluates <paramref name="key"/> against the read-only oracle table and returns its string
    /// value.
    /// </summary>
    /// <param name="key">A legal composed lookup key.</param>
    /// <returns>
    /// The value the legacy expression yields - the matched <c>to</c> attribute, or the empty string
    /// for a miss, because the key is wrapped in XPath's <c>string()</c> and <c>string()</c> of an
    /// empty node set is the empty string.
    /// </returns>
    /// <remarks>
    /// Read-only by construction, and that is constraint C-C honoured structurally rather than by
    /// reviewer diligence: <see cref="XPathDocument"/> is a read-only cursor over the file, this
    /// method opens it and nothing in this suite writes, moves, renames, re-encodes or reformats
    /// that path. A fresh document per call rather than a cached navigator, because
    /// <see cref="XPathNavigator"/> is a stateful cursor and correctness beats reuse here; the
    /// legacy re-evaluated its whole expression per translate and this is not a performance
    /// refactor (AAP 0.8.5).
    /// </remarks>
    private static string EvaluateAgainstOracle(string key)
    {
        XPathDocument document = new(I18nResourceReader.DefaultResourceFileName);
        XPathNavigator navigator = document.CreateNavigator();

        return Convert.ToString(navigator.Evaluate(key), System.Globalization.CultureInfo.InvariantCulture)
            ?? string.Empty;
    }

    /// <summary>
    /// Asserts the real oracle table is reachable before a test relies on its contents.
    /// </summary>
    private static void RequireResourceFixture()
    {
        Assert.True(File.Exists(I18nResourceReader.DefaultResourceFileName), FixtureMissingMessage);
    }

    // ==============================================================================================
    //  PART 1 - THE KEY'S SHAPE: SEQUENTIAL SUBSTITUTION, FIXED ORDER, ONE LANGUAGE TOKEN
    // ==============================================================================================

    /// <summary>
    /// The composed key for a representative spread of categories, source texts and both languages.
    /// </summary>
    /// <remarks>
    /// Each expected key is written out in full rather than assembled, so the row states the whole
    /// answer and a change to the format string cannot be absorbed by a matching change to the
    /// expectation. The spread deliberately mixes the ordinary case with the ones that carry
    /// characters the expression's own grammar uses, because those are where a well-meant escaping
    /// step would show up first.
    /// </remarks>
    public static TheoryData<string, string, string, string> ComposedKeyCases => new()
    {
        // The live in-scope path: dwsvc, both languages, an entry that really exists.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            "修改数据被拒绝",
            "string(pfw/dwsvc[@lang='en']/tr[@text='修改数据被拒绝']/@to)"
        },
        {
            TraditionalChineseKeyFormat,
            DataWindowServiceElement,
            "修改数据被拒绝",
            "string(pfw/dwsvc[@lang='cht']/tr[@text='修改数据被拒绝']/@to)"
        },

        // A different category element, to show the FIRST placeholder really is the element name.
        {
            EnglishKeyFormat,
            WindowElement,
            PresentWindowSourceText,
            "string(pfw/window[@lang='en']/tr[@text='关闭']/@to)"
        },
        {
            TraditionalChineseKeyFormat,
            MessageBoxElement,
            "确定",
            "string(pfw/msgbox[@lang='cht']/tr[@text='确定']/@to)"
        },

        // Braces in the DATA are carried through as data. Kernel's Sprintf is a single-pass scanner
        // that appends each rendered argument without rescanning it, so the argument's own braces
        // never become placeholders - and the resulting key still addresses the real entry at
        // pfw.i18n.xml:L52 and :L127. PART 3 follows this through to the translated value.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            BracedSourceText,
            "string(pfw/dwsvc[@lang='en']/tr[@text='第{}行']/@to)"
        },
        {
            TraditionalChineseKeyFormat,
            DataWindowServiceElement,
            BracedSourceText,
            "string(pfw/dwsvc[@lang='cht']/tr[@text='第{}行']/@to)"
        },

        // The apostrophe reaches the key intact. This row is the "no escaping" assertion stated as a
        // whole-key equality: note the THREE apostrophes inside the predicate, which is exactly what
        // an unescaped interpolation produces and exactly what an escaping implementation would not.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            ApostropheSourceText,
            "string(pfw/dwsvc[@lang='en']/tr[@text='it's']/@to)"
        },
        {
            TraditionalChineseKeyFormat,
            DataWindowServiceElement,
            ApostropheSourceText,
            "string(pfw/dwsvc[@lang='cht']/tr[@text='it's']/@to)"
        },

        // The double quote likewise reaches the key intact - and, being inside a single-quoted
        // literal, changes nothing about the expression's structure.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            DoubleQuoteSourceText,
            "string(pfw/dwsvc[@lang='en']/tr[@text='say \"hi\"']/@to)"
        },

        // Non-quote metacharacters, verbatim.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            SpecialCharacterSourceText,
            "string(pfw/dwsvc[@lang='en']/tr[@text='[a]@b/c (d)']/@to)"
        },

        // The empty source text still produces a well-formed predicate against an empty literal.
        {
            EnglishKeyFormat,
            DataWindowServiceElement,
            "",
            "string(pfw/dwsvc[@lang='en']/tr[@text='']/@to)"
        },
    };

    /// <summary>
    /// The key substitutes the category element name into the first placeholder and the source text
    /// into the second, and applies nothing to either on the way.
    /// </summary>
    /// <param name="format">The provider's key format.</param>
    /// <param name="categoryElement">The category element name.</param>
    /// <param name="sourceText">The source text.</param>
    /// <param name="expectedKey">The whole key the legacy renders.</param>
    /// <remarks>
    /// <para>
    /// The central assertion of the file. It fixes three things at once: that both placeholders are
    /// filled sequentially in the legacy's order, that the surrounding expression is reproduced
    /// character for character, and that the source text arrives UNESCAPED - the apostrophe, double
    /// quote, brace and metacharacter rows are all whole-key equalities, so any escaping, doubling,
    /// entity encoding, percent encoding or stripping applied to an argument fails the row outright.
    /// </para>
    /// <para>
    /// It deliberately does not test the formatter's grammar. That belongs to
    /// <c>shared/PowerFramework.Shared.Kernel.Tests/SprintfTests.cs</c>, which pins sequential and
    /// indexed placeholders, alignment, masks and the negative cases; here the formatter is a
    /// dependency being used exactly as the providers use it.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ComposedKeyCases))]
    public void KeyPlacesTheCategoryElementFirstAndTheSourceTextSecond(
        string format,
        string categoryElement,
        string sourceText,
        string expectedKey)
    {
        Assert.Equal(expectedKey, ComposeKey(format, categoryElement, sourceText));
    }

    /// <summary>
    /// Category element and source text pairs whose transposition is observable, each with the
    /// translation the correctly ordered key resolves to and whether the transposed key is still
    /// legal XPath.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only pairs whose two members differ are useful here, so each row is a genuine element name
    /// and a genuine source text that is not that name.
    /// </para>
    /// <para>
    /// The legality column is measured, not assumed, and it splits into two classes for a reason
    /// worth recording. Transposed, the source text becomes the NAME of a location step, and an XML
    /// element name admits CJK ideographs but not braces. So <c>修改数据被拒绝</c>, <c>关闭</c> and
    /// <c>确定</c> all yield a perfectly legal step that simply matches nothing, whereas
    /// <c>第{}行</c> yields <c>pfw/第{}行[...]</c>, which is not a legal name and so not a legal
    /// expression. Both classes are failures of the same mistake; only one of them is loud. Stating
    /// the split per row is what keeps the quiet class visible.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string, string, bool> TranspositionCases => new()
    {
        // Braces are illegal in an element name, so this transposition is REJECTED outright.
        { DataWindowServiceElement, BracedSourceText, BracedEnglishTranslation, false },

        // CJK ideographs are legal in an element name, so these transpositions are SILENT misses -
        // the dangerous class, because nothing complains.
        { DataWindowServiceElement, "修改数据被拒绝", "Modified data is rejected", true },
        { WindowElement, PresentWindowSourceText, PresentWindowEnglishTranslation, true },
        { MessageBoxElement, "确定", "OK", true },
    };

    /// <summary>
    /// Transposing the two arguments yields a different key that never resolves the translation,
    /// which is why the order is contract rather than convention.
    /// </summary>
    /// <param name="categoryElement">The category element name.</param>
    /// <param name="sourceText">The source text.</param>
    /// <param name="expectedTranslation">What the correctly ordered key resolves to.</param>
    /// <param name="transposedKeyIsLegalXPath">
    /// Whether the transposed key is still a legal expression.
    /// </param>
    /// <remarks>
    /// <para>
    /// Swapped, the key names an element after the caller's text and compares the element name
    /// against a <c>tr</c> entry - <c>string(pfw/关闭[@lang='en']/tr[@text='window']/@to)</c> for the
    /// third row. Asserted against the real oracle table so the failure mode is demonstrated rather
    /// than inferred from the key's appearance.
    /// </para>
    /// <para>
    /// The correctly ordered key is required to resolve its translation in the same test, so an
    /// implementation that ignored argument order altogether could not satisfy both halves: it would
    /// have to make the transposed key miss AND the ordered key hit, and one substitution order
    /// cannot do both.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(TranspositionCases))]
    public void TransposingTheTwoArgumentsProducesADifferentKeyThatNeverResolvesTheTranslation(
        string categoryElement,
        string sourceText,
        string expectedTranslation,
        bool transposedKeyIsLegalXPath)
    {
        RequireResourceFixture();

        string correct = ComposeKey(EnglishKeyFormat, categoryElement, sourceText);
        string transposed = ComposeKey(EnglishKeyFormat, sourceText, categoryElement);

        Assert.NotEqual(correct, transposed);

        Assert.Equal(transposedKeyIsLegalXPath, IsLegalXPath(transposed));

        if (transposedKeyIsLegalXPath)
        {
            // The quiet failure: a legal expression addressing an element the table does not declare.
            Assert.Equal(string.Empty, EvaluateAgainstOracle(transposed));
        }

        // Either way the ordered key is the only one that answers.
        Assert.Equal(expectedTranslation, EvaluateAgainstOracle(correct));
    }

    /// <summary>
    /// Each format paired with the token it must carry, the token it must not, and the sibling format
    /// it becomes when the one is swapped for the other.
    /// </summary>
    public static TheoryData<string, string, string, string> LanguageTokenCases => new()
    {
        {
            EnglishKeyFormat,
            EnglishLanguageToken,
            TraditionalChineseLanguageToken,
            TraditionalChineseKeyFormat
        },
        {
            TraditionalChineseKeyFormat,
            TraditionalChineseLanguageToken,
            EnglishLanguageToken,
            EnglishKeyFormat
        },
    };

    /// <summary>
    /// Each format selects its own language and only its own, which is the sole substantive
    /// difference between <c>n_cst_i18n_en.sru:L51</c> and <c>n_cst_i18n_cht.sru:L52</c>.
    /// </summary>
    /// <param name="format">The provider's key format.</param>
    /// <param name="ownToken">The language token that format must carry.</param>
    /// <param name="foreignToken">The language token it must not carry.</param>
    /// <param name="siblingFormat">The other provider's format.</param>
    /// <remarks>
    /// All three halves are needed. Asserting only that the English format contains <c>en</c> would
    /// be satisfied by a format carrying both tokens; asserting only the absence of the other would
    /// be satisfied by a format carrying neither; and neither of those would establish that the two
    /// formats are otherwise the same string. The third assertion does that by construction -
    /// swapping the token turns one format into the other EXACTLY, which is the byte-level statement
    /// of "identical but for the language". The negative half is also what catches the specific
    /// regression of a Traditional Chinese provider left pointing at <c>'en'</c>, a slip that
    /// otherwise surfaces only as translations quietly in the wrong language.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LanguageTokenCases))]
    public void EachFormatCarriesItsOwnLanguageTokenAndNotTheOther(
        string format,
        string ownToken,
        string foreignToken,
        string siblingFormat)
    {
        Assert.Contains($"[@lang='{ownToken}']", format, StringComparison.Ordinal);
        Assert.DoesNotContain($"[@lang='{foreignToken}']", format, StringComparison.Ordinal);

        Assert.Equal(
            siblingFormat,
            format.Replace($"[@lang='{ownToken}']", $"[@lang='{foreignToken}']", StringComparison.Ordinal));
    }

    /// <summary>
    /// The providers' formats agree on everything except the language token, and both quote their
    /// predicates with SINGLE quotes.
    /// </summary>
    /// <remarks>
    /// The single-quoting is asserted here, at the top of the file's argument, because everything in
    /// PART 2 follows from it: which character is hazardous is a consequence of this choice and
    /// nothing else. Were the predicate ever rewritten with double quotes the hazardous character
    /// would swap, and this assertion is what would fail first and say so.
    /// </remarks>
    [Fact]
    public void BothFormatsQuoteTheirPredicatesWithSingleQuotes()
    {
        Assert.Contains("/tr[@text='{}']/", EnglishKeyFormat, StringComparison.Ordinal);
        Assert.Contains("/tr[@text='{}']/", TraditionalChineseKeyFormat, StringComparison.Ordinal);

        // No double quote appears anywhere in either format, so no source text can be "safe by
        // being inside a double-quoted literal".
        Assert.DoesNotContain("\"", EnglishKeyFormat, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", TraditionalChineseKeyFormat, StringComparison.Ordinal);

        // Two placeholders, no more and no fewer - the arity the two-argument Sprintf overload at
        // sprintf.srf:L8 is called with.
        Assert.Equal(2, EnglishKeyFormat.Split("{}").Length - 1);
        Assert.Equal(2, TraditionalChineseKeyFormat.Split("{}").Length - 1);
    }

    // ==============================================================================================
    //  PART 2 - THE UNESCAPED INTERPOLATION, AND WHY THE APOSTROPHE IS THE CHARACTER THAT MATTERS
    // ==============================================================================================

    /// <summary>
    /// The spellings an escaping, encoding or sanitising implementation would have produced for
    /// <see cref="ApostropheSourceText"/> - every one of which must be ABSENT from the composed key.
    /// </summary>
    /// <remarks>
    /// Stated as a list of forbidden spellings rather than left implicit in one equality, because
    /// each entry names a specific plausible hardening and rules it out by name. The equality in
    /// PART 1 already fails if any of them occurs; these rows make the intent legible and pin down
    /// WHICH transformations are being refused.
    /// </remarks>
    public static TheoryData<string, string> ForbiddenEscapeSpellings => new()
    {
        // SQL-style doubling of the quote.
        { "''", "the apostrophe doubled, as a SQL-style escape would render it" },

        // XML/HTML entity forms.
        { "&apos;", "the apostrophe as an XML character entity" },
        { "&#39;", "the apostrophe as a numeric character reference" },

        // Percent encoding, as a URI-minded implementation might apply.
        { "%27", "the apostrophe percent-encoded" },

        // Backslash escaping, as a C-family string escape would apply.
        { "\\'", "the apostrophe backslash-escaped" },

        // XPath's own workaround for an apostrophe in a literal: concat with a double-quoted part.
        { "concat(", "an XPath concat() rewrite of the literal" },
    };

    /// <summary>
    /// The composed key carries the apostrophe verbatim and shows no trace of any escaping,
    /// encoding, quoting or sanitising scheme.
    /// </summary>
    /// <param name="forbiddenSpelling">The substring an escaping implementation would introduce.</param>
    /// <param name="description">What that substring would mean, for the failure message.</param>
    /// <remarks>
    /// <para>
    /// The "unescaped interpolation preserved" assertion, stated negatively so it cannot be
    /// satisfied by accident. The legacy applies nothing to the source text at
    /// <c>n_cst_i18n_en.sru:L51</c>, so neither may the port: constraint C-B forbids correcting a
    /// legacy defect, and hardening this interpolation would be exactly that correction. This test
    /// exists to fail if someone adds it.
    /// </para>
    /// <para>
    /// Note what is NOT asserted here, deliberately: nothing about the reader's internals. The
    /// reader resolves by traversal and publishes no assembled key, so the subject of this test is
    /// the FORMAT and the FORMATTER the providers use - which is where the legacy's escaping
    /// decision lives. The observable end-to-end consequence is asserted separately, immediately
    /// below.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ForbiddenEscapeSpellings))]
    public void TheComposedKeyShowsNoTraceOfEscapingTheApostrophe(
        string forbiddenSpelling,
        string description)
    {
        foreach (string format in BothKeyFormats)
        {
            string key = ComposeKey(format, DataWindowServiceElement, ApostropheSourceText);

            Assert.False(
                key.Contains(forbiddenSpelling, StringComparison.Ordinal),
                $"the composed key must not contain '{forbiddenSpelling}' - that would be "
                    + $"{description}, and the legacy at n_cst_i18n_en.sru:L51 applies no such "
                    + $"transformation. Key was: {key}");

            // ...and positively: the character itself is present, in the exact run the source text
            // supplies it in. Paired with the negative assertion so "absent because the whole
            // apostrophe was stripped" cannot pass either.
            Assert.Contains(ApostropheSourceText, key, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The apostrophe breaks the single-quoted predicate and the double quote does not, which is the
    /// whole reason the apostrophe is the character worth testing with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both keys are handed to the framework's XPath engine, and the engine's verdicts differ. The
    /// apostrophe key is not merely unusual, it is NOT A LEGAL EXPRESSION: the literal closes early
    /// and the remainder does not parse. The double-quote key parses cleanly, because a double quote
    /// inside a single-quoted literal is an ordinary character, and it simply matches nothing.
    /// </para>
    /// <para>
    /// This is the assertion that makes the file's central claim demonstrable rather than asserted.
    /// A reader who wonders "why not just use a double quote?" has the answer in the two verdicts:
    /// the double-quote row would have proved that a lookup misses, and proving a miss proves
    /// nothing at all about the quoting.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheApostropheBreaksThePredicateAndTheDoubleQuoteIsInert()
    {
        RequireResourceFixture();

        string apostropheKey = ComposeKey(
            EnglishKeyFormat, DataWindowServiceElement, ApostropheSourceText);
        string doubleQuoteKey = ComposeKey(
            EnglishKeyFormat, DataWindowServiceElement, DoubleQuoteSourceText);

        // THE HAZARDOUS CHARACTER. Rejected outright.
        Assert.False(
            IsLegalXPath(apostropheKey),
            "an apostrophe in the source text must still break the single-quoted predicate, because "
                + "the legacy does not escape it");

        // THE HARMLESS CONTROL. Legal, and an ordinary miss - so it never leaves the string context
        // and could not have demonstrated anything about the quoting.
        Assert.True(
            IsLegalXPath(doubleQuoteKey),
            "a double quote inside a single-quoted literal is an ordinary character and must leave "
                + "the expression legal");
        Assert.Equal(string.Empty, EvaluateAgainstOracle(doubleQuoteKey));

        // And for contrast, a key built from a source text with no quote at all is legal AND resolves,
        // so "legal" is not standing in for "empty".
        string presentKey = ComposeKey(EnglishKeyFormat, WindowElement, PresentWindowSourceText);
        Assert.True(IsLegalXPath(presentKey));
        Assert.Equal(PresentWindowEnglishTranslation, EvaluateAgainstOracle(presentKey));
    }

    /// <summary>
    /// Every source text whose lookup must resolve as not-found, with the reason each one misses.
    /// </summary>
    /// <remarks>
    /// The apostrophe row is the one this file is built around; the rest establish that it is not
    /// special in its OUTCOME, only in its mechanism. None of these strings appears as a
    /// <c>text</c> attribute anywhere in <c>pfw.i18n.xml</c>, and none is added to make a row pass -
    /// the table is read-only under constraint C-C.
    /// </remarks>
    public static TheoryData<string, string> NotFoundSourceTexts => new()
    {
        { ApostropheSourceText, "the apostrophe closes the predicate's literal early" },
        { "don't touch", "a second, longer apostrophe-bearing ordinary string" },
        { DoubleQuoteSourceText, "the double quote is inert inside a single-quoted literal" },
        { SpecialCharacterSourceText, "brackets, sigil, slash, parentheses and a space are inert" },
        { "", "the empty source text matches no entry, every one of which carries text" },
    };

    /// <summary>
    /// End to end, each of these source texts resolves as not-found: both providers decline, the
    /// caller's text comes back unchanged, and no exception escapes.
    /// </summary>
    /// <param name="sourceText">The source text to look up.</param>
    /// <param name="reason">Why it misses, for the failure message.</param>
    /// <remarks>
    /// <para>
    /// The observable consequence half of the apostrophe assertion, and the legacy outcome it
    /// matches: the malformed query yields an empty result, the <c>if sTo &lt;&gt; ""</c> gate at
    /// <c>n_cst_i18n_en.sru:L52</c> declines, and the event falls through to <c>return 0</c> at
    /// <c>:L155</c> - <c>n_cst_i18n_cht.sru:L53</c> and <c>:L59</c> respectively - leaving the
    /// caller's <c>ref</c> text exactly as it arrived.
    /// </para>
    /// <para>
    /// MISS IS NOT AN EXCEPTION. That is the reader's documented contract, the same one
    /// <c>I18nResourceReaderTests.cs</c> asserts, and it is asserted here at the provider boundary
    /// too because that is the boundary a caller actually holds. This test was written expecting
    /// not-found, and not-found is what the implementation produces; had an XPath exception escaped
    /// the reader or a provider instead, that would be a genuine divergence to report as a finding
    /// rather than a reason to soften this assertion into an expected throw.
    /// </para>
    /// <para>
    /// <c>Enums.I18N_SRC_PFW</c> is spelled out rather than written as <c>0</c> throughout. That is
    /// not decoration: <c>I18N_SRC_PFW</c> and <c>I18N_CAT_WINDOW</c> are BOTH zero, so a bare
    /// literal in either position reads correctly and means nothing, and a transposed pair of zeroes
    /// would still pass. The categories used here are non-zero for the same reason.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(NotFoundSourceTexts))]
    public void AnUnmatchedSourceTextIsDeclinedUnchangedAndNothingThrows(
        string sourceText,
        string reason)
    {
        RequireResourceFixture();

        // The resolver: a miss is the empty string, not a throw.
        I18nResourceReader reader = new();

        Assert.True(
            reader.Lookup(EnglishLanguageToken, DataWindowServiceElement, sourceText).Length == 0,
            $"the en lookup of '{sourceText}' must resolve as not-found, because {reason}");
        Assert.True(
            reader.Lookup(TraditionalChineseLanguageToken, DataWindowServiceElement, sourceText).Length == 0,
            $"the cht lookup of '{sourceText}' must resolve as not-found, because {reason}");

        // The English provider declines and leaves the text alone.
        EnglishProvider english = new();
        string? englishText = sourceText;
        Assert.Equal(
            0L,
            english.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref englishText));
        Assert.Equal(sourceText, englishText);

        // ...and so does the Traditional Chinese provider, so the outcome is a property of the key
        // rather than of one locale's table.
        TraditionalChineseProvider traditionalChinese = new();
        string? traditionalChineseText = sourceText;
        Assert.Equal(
            0L,
            traditionalChinese.OnTranslate(
                Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref traditionalChineseText));
        Assert.Equal(sourceText, traditionalChineseText);
    }

    // ==============================================================================================
    //  PART 3 - BRACES IN THE RESOURCE DATA ARE TEXT, NEVER A PLACEHOLDER
    // ==============================================================================================

    /// <summary>
    /// The two shipped entries whose source text AND translation both carry literal braces.
    /// </summary>
    /// <remarks>
    /// <c>pfw.i18n.xml:L52</c> renders <c>第{}行</c> as <c>Line {}</c> under <c>dwsvc lang="en"</c>,
    /// and <c>:L127</c> renders it as itself under <c>dwsvc lang="cht"</c>. Both are read from the
    /// read-only table; neither is added, edited or supplemented to make a row pass.
    /// </remarks>
    public static TheoryData<string, long, string, string> BracedResourceCases => new()
    {
        { EnglishLanguageToken, 1L, BracedEnglishTranslation, "pfw.i18n.xml:L52" },
        { TraditionalChineseLanguageToken, 1L, BracedSourceText, "pfw.i18n.xml:L127" },
    };

    /// <summary>
    /// A source text carrying braces is matched literally, and the translation comes back with its
    /// own braces intact and unsubstituted.
    /// </summary>
    /// <param name="languageToken">The language token whose entry is being read.</param>
    /// <param name="expectedReturnCode">The handled/not-handled code the provider must return.</param>
    /// <param name="expectedTranslation">The translation, braces included.</param>
    /// <param name="locator">The resource locator the expectation is read from.</param>
    /// <remarks>
    /// <para>
    /// THE CONFLATION HAZARD THIS ROW EXISTS FOR. The token <c>{}</c> is a PLACEHOLDER in the format
    /// string of <c>n_cst_i18n_en.sru:L51</c> and inert DATA in the resource table, and the two meet
    /// in this one lookup: the braced source text is an ARGUMENT to the formatter, and the braced
    /// translation is the formatter's eventual OUTPUT. Kernel's Sprintf is a single-pass scanner
    /// that appends each rendered argument without rescanning it, so the argument's braces never
    /// become holes - and the resource's braces are never seen by a formatter at this layer at all.
    /// An implementation that treated either as a placeholder fails this row.
    /// </para>
    /// <para>
    /// The Traditional Chinese row is a HIT whose translation equals its input, and the return code
    /// is asserted for exactly that reason: unchanged text here means "translated to the same
    /// string", not "declined". Reading it as a decline would invert the entry's meaning.
    /// </para>
    /// <para>
    /// NOTHING HERE FEEDS THE RESULT BACK THROUGH SPRINTF, deliberately. Filling those braces is a
    /// SEPARATE, DOWNSTREAM substitution performed by the call site -
    /// <c>n_cst_dwsvc_rowselect.sru:L239</c> wraps this very lookup in
    /// <c>Sprintf(I18N(ne_cst_i18n.CAT_DWSVC,"第{}行"),nRow)</c> to interpolate the row number - and
    /// that belongs to the DataWindow service layer, not to this one. This layer's whole obligation
    /// is to hand the braces on untouched so the caller still has something to fill.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(BracedResourceCases))]
    public void BracesInTheResourceDataSurviveTheLookupUnsubstituted(
        string languageToken,
        long expectedReturnCode,
        string expectedTranslation,
        string locator)
    {
        RequireResourceFixture();

        // The resolver returns the recorded value byte for byte.
        I18nResourceReader reader = new();
        Assert.Equal(
            expectedTranslation,
            reader.Lookup(languageToken, DataWindowServiceElement, BracedSourceText));

        // The provider agrees, and reports a HIT while doing so.
        //
        // Dispatched with an if/else against the CONCRETE provider types rather than through the
        // II18nProvider interface. That is deliberate: the two providers are what this row is about,
        // the interface contract itself is owned by II18nProviderTests.cs, and naming the concrete
        // types keeps this suite's dependency surface to exactly the units it exercises.
        string? text = BracedSourceText;
        long returnCode;

        if (languageToken == EnglishLanguageToken)
        {
            EnglishProvider provider = new();
            returnCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text);
        }
        else
        {
            TraditionalChineseProvider provider = new();
            returnCode = provider.OnTranslate(Enums.I18N_SRC_PFW, Categories.CAT_DWSVC, ref text);
        }

        Assert.Equal(expectedReturnCode, returnCode);
        Assert.Equal(expectedTranslation, text);

        // The braces are still there, unsubstituted, for the downstream caller to fill.
        Assert.True(
            text is not null && text.Contains("{}", StringComparison.Ordinal),
            $"the braces recorded at {locator} must survive the lookup unsubstituted, because the "
                + "call site fills them afterwards");
    }

    /// <summary>
    /// The composed key for a braced source text is legal XPath and addresses the real entry, so the
    /// oracle itself treats the braces as data.
    /// </summary>
    /// <remarks>
    /// The parity half of the assertion above, and the one that makes it more than an internal
    /// convention: the legacy expression, rendered by the legacy's own format string and evaluated by
    /// an XPath engine over the read-only table, returns <c>Line {}</c> too. So "braces are data" is
    /// a property of the ORACLE and not a choice this port made.
    /// </remarks>
    [Fact]
    public void TheOracleAlsoMatchesTheBracedEntryLiterally()
    {
        RequireResourceFixture();

        string englishKey = ComposeKey(EnglishKeyFormat, DataWindowServiceElement, BracedSourceText);
        string traditionalChineseKey = ComposeKey(
            TraditionalChineseKeyFormat, DataWindowServiceElement, BracedSourceText);

        Assert.True(IsLegalXPath(englishKey));
        Assert.True(IsLegalXPath(traditionalChineseKey));

        Assert.Equal(BracedEnglishTranslation, EvaluateAgainstOracle(englishKey));
        Assert.Equal(BracedSourceText, EvaluateAgainstOracle(traditionalChineseKey));
    }

    // ==============================================================================================
    //  PART 4 - THE PORT AND THE LEGACY EXPRESSION AGREE ON THE SHIPPED TABLE
    // ==============================================================================================

    /// <summary>
    /// Genuine framework-shipped lookups, each with the language, category and expected translation.
    /// </summary>
    /// <remarks>
    /// Chosen to span both languages and three category elements, and to include the braced entry and
    /// one of the two preserved mistranslations, so the sweep covers the rows most likely to be
    /// "helpfully" adjusted by a future edit.
    /// </remarks>
    public static TheoryData<string, string, string, string> OracleAgreementCases => new()
    {
        { EnglishLanguageToken, WindowElement, PresentWindowSourceText, PresentWindowEnglishTranslation },
        {
            TraditionalChineseLanguageToken,
            WindowElement,
            PresentWindowSourceText,
            PresentWindowTraditionalChineseTranslation
        },
        { EnglishLanguageToken, DataWindowServiceElement, BracedSourceText, BracedEnglishTranslation },
        { TraditionalChineseLanguageToken, DataWindowServiceElement, BracedSourceText, BracedSourceText },
        { EnglishLanguageToken, MessageBoxElement, "确定", "OK" },

        // pfw.i18n.xml:L4 - the minimise label rendered as the English word for its OPPOSITE. A
        // preserved defect (AAP §0.8.2), asserted as the table records it. The correct wording
        // survives only in the dormant block at n_cst_i18n_en.sru:L58-L153 and must not be revived.
        { EnglishLanguageToken, WindowElement, "最小化", "Maximize" },
    };

    /// <summary>
    /// For every genuine key, the legacy expression evaluated over the read-only table and the port's
    /// own answer are the same string.
    /// </summary>
    /// <param name="languageToken">The language token.</param>
    /// <param name="categoryElement">The category element name.</param>
    /// <param name="sourceText">The source text.</param>
    /// <param name="expectedTranslation">The translation all three must produce.</param>
    /// <remarks>
    /// <para>
    /// This is what turns the rest of the file from a description of a format into evidence about
    /// behaviour. The key is composed with the legacy format string through the legacy's own
    /// formatter, handed to an XPath engine, and evaluated against the same read-only table the port
    /// reads; the port's <c>Lookup</c> is required to produce the identical string; and the PROVIDER
    /// that owns that language is then required to produce it too, from a numeric category rather
    /// than an element name. Four independent things have to hold for a row to pass: the format is
    /// right, the argument order is right, the reader resolves what the legacy expression resolves,
    /// and the provider maps its category and language onto the same lookup.
    /// </para>
    /// <para>
    /// THE PROVIDER LEG IS NOT REDUNDANT, and it is here because its absence was measured. Driving
    /// only the reader leaves this sweep blind to everything the provider itself decides - its
    /// language token and its category-to-element map - because those are consumed on the way INTO
    /// the reader. A Traditional Chinese provider left pointing at <c>'en'</c>, or a provider passing
    /// the source text where the element name belongs, changes nothing about the reader and so
    /// changes nothing about a reader-only assertion. With this leg in place each of those is a
    /// failure of every row in the language concerned rather than an incidental casualty of one
    /// unrelated test.
    /// </para>
    /// <para>
    /// The provider is dispatched by concrete type for the reason given at the braces row: the
    /// interface contract belongs to <c>II18nProviderTests.cs</c>, and the two locale providers are
    /// what this row is about. <c>Enums.I18N_SRC_PFW</c> is spelled out because it and
    /// <c>I18N_CAT_WINDOW</c> are both zero, so a bare literal would read correctly and assert
    /// nothing.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(OracleAgreementCases))]
    public void TheLegacyExpressionAndThePortAgreeOnEveryGenuineKey(
        string languageToken,
        string categoryElement,
        string sourceText,
        string expectedTranslation)
    {
        RequireResourceFixture();

        bool isEnglish = languageToken == EnglishLanguageToken;
        string format = isEnglish ? EnglishKeyFormat : TraditionalChineseKeyFormat;
        string key = ComposeKey(format, categoryElement, sourceText);

        // 1. The legacy expression is legal for a genuine key...
        Assert.True(IsLegalXPath(key), $"the key for '{sourceText}' must be legal XPath: {key}");

        // 2. ...and it resolves the expected translation against the read-only oracle table.
        Assert.Equal(expectedTranslation, EvaluateAgainstOracle(key));

        // 3. The port's resolver agrees, byte for byte.
        I18nResourceReader reader = new();
        Assert.Equal(expectedTranslation, reader.Lookup(languageToken, categoryElement, sourceText));

        // 4. And so does the provider that owns this language, reached from the NUMERIC category so
        //    that its own language token and category map are exercised rather than bypassed.
        long numericCategory = categoryElement switch
        {
            DataWindowServiceElement => Categories.CAT_DWSVC,
            MessageBoxElement => Categories.CAT_MSGBOX,

            // n_cst_i18n_en.sru:L35-L36 - Enums.I18N_CAT_WINDOW and Enums.I18N_CAT_DATAWINDOW BOTH
            // map onto the window element. I18N_CAT_DATAWINDOW is used here deliberately: it is
            // non-zero, so it cannot be confused with Enums.I18N_SRC_PFW in the argument list, and it
            // exercises the shared arm that makes a DataWindow-category caption resolve out of the
            // window section rather than a 'datawindow' section the table does not declare.
            WindowElement => Enums.I18N_CAT_DATAWINDOW,

            _ => throw new InvalidOperationException(
                $"no numeric category is mapped for the element '{categoryElement}'"),
        };

        string? providerText = sourceText;
        long returnCode;

        if (isEnglish)
        {
            EnglishProvider provider = new();
            returnCode = provider.OnTranslate(Enums.I18N_SRC_PFW, numericCategory, ref providerText);
        }
        else
        {
            TraditionalChineseProvider provider = new();
            returnCode = provider.OnTranslate(Enums.I18N_SRC_PFW, numericCategory, ref providerText);
        }

        Assert.Equal(1L, returnCode);
        Assert.Equal(expectedTranslation, providerText);
    }
}
