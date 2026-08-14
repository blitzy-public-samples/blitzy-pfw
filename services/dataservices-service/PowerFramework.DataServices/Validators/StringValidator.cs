// ==============================================================================================
//  StringValidator - the dwNvlString null producer, and the CHARACTER ARM of the item-change
//                    type-coercion table
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.datawindow.services.pbl.src/dwnvlstring.srf
//                     13 lines. L2 declares `global type dwnvlstring from function_object` with
//                     NO `native "..."` clause, so the body is readable PowerScript and nothing
//                     here is reconstructed from a closed binary. L6 is the zero-argument
//                     prototype; L9-L11 are the whole body: declare `string nvl`, `SetNull(nvl)`,
//                     `return nvl`.
//                 ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L232-L233
//                     the `case "char","char("` arm of the coercion table at L231-L244, inside
//                     `event ondwnitemchange` (L182-L254).
//
//  READ AS REFERENCE ONLY - NOT PORTED IN THIS FILE
//                 dwvaluetoexp.srf:L47-L51            the ONLY consumer of this file's sentinel
//                                                     spelling, and the source of its exact text
//                 se_cst_dw.sru:L182-L254             the item-change micro-protocol, which OWNS
//                                                     the `choose case` this file only feeds
//                 n_cst_dwsvc.sru:L26-L32             the COL_TYPE_* category this arm maps to
//                 n_cst_dwsvc.sru:L503-L518           a SECOND, independent prefix table
//                 n_cst_dwsvc.sru:L195, L217          Describe("Evaluate(...)"), the mechanism
//                                                     that ever executes the sentinel
//                 n_cst_dwsvc_columnexp.sru:L2390-L2391
//                                                     the caller that detects an expression
//                                                     failure as the returned text "?" or "!"
//                 ws_objects/pfw.shared.pbl.src/retcode.sru:L39-L79
//                                                     the return-code identifiers, consumed from
//                                                     Shared.Kernel rather than redeclared
//                 ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14
//                                                     the primary fixture, the repository's only
//                                                     updatable DataWindow
//                 ws_objects/pfw.tests.pbl.src/w_test_sqlite.srw:L463-L469
//                                                     the repository's only DDL
//
//  ORACLE STATUS  Every path above lives under ws_objects/ and is READ ONLY (constraint C-C).
//                 Those files are the behavioural oracle for parity testing, never an edit
//                 target, and they are the ONLY specification that exists for this behaviour:
//                 logfile.md stops at framework 3.0.7.2062 (2022-04-14) while the commit history
//                 runs years later, and the two PowerBuilder project objects contradict each
//                 other and both name a library that does not exist, so neither can adjudicate
//                 anything. Consequently EVERY behavioural assertion below carries the line
//                 locator it was taken from, and nothing is asserted that a locator cannot
//                 support.
//
//  --------------------------------------------------------------------------------------------
//  THE KEY FINDING: THIS FOLDER IS MISNAMED, AND THAT MUST NOT BE "FIXED"
//  --------------------------------------------------------------------------------------------
//  `dwnvlstring` does not validate anything. It declares a typed local, calls `SetNull` on it and
//  returns it. There is no predicate, no rule, no message and no failure path anywhere in its 13
//  lines. It sits beside the five `dwnvl*` producers and `dwvaluetoexp` in the same legacy library
//  as the genuine validators, so the folder name is inherited rather than descriptive.
//
//  It exists to be called from INSIDE A DATAWINDOW EXPRESSION STRING, not from PowerScript. That
//  is measured, not inferred. A case-insensitive search of all 39 exported libraries and all 544
//  objects finds exactly five occurrences of the identifier: four are its own declaration inside
//  dwnvlstring.srf, and the fifth is the STRING LITERAL at dwvaluetoexp.srf:L49. There is not one
//  PowerScript call site in the entire estate. The same holds for its four siblings, dwNvlNumber,
//  dwNvlDateTime, dwNvlDate and dwNvlTime.
//
//  How the literal is ever executed closes the loop. n_cst_dwsvc.sru:L195 and :L217 hand
//  expression text to `#DataWindow.Describe("Evaluate(" + ... + ")")`, and
//  n_cst_dwsvc_columnexp.sru:L2390 does exactly that with a `dwValueToExp(...)` call embedded in
//  the text. The DataWindow expression evaluator, not the PowerScript compiler, resolves the name.
//
//  Two consequences follow, and both are load bearing:
//
//      1. THE OBSERVABLE CONTRACT IS THE SPELLING. Because resolution happens inside an
//         expression string, a one-character drift is invisible to any compiler. It surfaces at
//         run time as the evaluator returning the text "?" or "!", which
//         n_cst_dwsvc_columnexp.sru:L2391 tests for explicitly before reporting an error. That is
//         why the spelling is published here as a constant and single-sourced (DECISION 1) rather
//         than retyped at each of its two use sites.
//      2. THERE IS NO VALIDATION BEHAVIOUR TO PRESERVE. Inventing one - a length check, a null
//         check, a format check - would be a new feature, which constraint C-B forbids outright.
//
//  --------------------------------------------------------------------------------------------
//  WHAT THIS FILE OWNS, AND WHAT IT DELIBERATELY DOES NOT
//  --------------------------------------------------------------------------------------------
//  OWNS   the sentinel spelling; the typed null value; the two ColType prefix tokens; the
//         truncate-and-compare predicate that answers "is this column mine"; and the arm's
//         write behaviour, which is an identity pass-through.
//
//  DOES NOT OWN, BY DESIGN
//      * The `choose case` itself. se_cst_dw.sru:L231-L244 is reproduced by
//        Domain/ItemChangeProtocol.cs, which also reproduces the ABSENCE of a `case else` at
//        L244. This file supplies one arm's tokens and one arm's behaviour; it does not dispatch.
//      * The COL_TYPE_* constants. n_cst_dwsvc.sru:L26-L32 declares them and
//        Domain/DataWindowServiceHost.cs owns them. This arm corresponds to COL_TYPE_STRING = 1
//        [n_cst_dwsvc.sru:L27], stated here as a COMMENT on purpose: a second declaration would
//        be a second source of truth, and a `using` of Domain/ would couple this folder to a
//        sibling folder it is deliberately sequenced ahead of.
//      * The invalid-value dialog. Verified line by line so it is not mistaken for this file's
//        business: se_cst_dw.sru:L350 reads `Describe(dwo.Name + ".ValidationMsg")`, L351-L353
//        strip the outer two characters with `Mid(sErrMsg,2,Len(sErrMsg) - 2)` when the text is
//        longer than two, L354-L356 fall back to `I18N(ne_cst_i18n.CAT_DWSVC,...)` when the
//        result is empty or a single question mark, and L357 raises `MessageBox` - the plain
//        overload, not `MessageBoxEx`. All of that becomes Domain's structured error result; its
//        rendering half belongs to the deferred DesignSystem service, so constraint C-D bars it
//        from appearing here in any form.
//      * The single-quote wrapping at dwvaluetoexp.srf:L48. Validators/ValueToExpression.cs ports
//        that line, including its unescaped-quote defect. See DECISION 6 for why no escaping
//        helper may appear in this file either.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 1 - THE SPELLING IS PUBLISHED ONCE, AS TWO CONSTANTS
//  --------------------------------------------------------------------------------------------
//  `NullLiteralExpression` is emitted by Validators/ValueToExpression.cs in place of retyping the
//  literal from dwvaluetoexp.srf:L49, and `FunctionName` is what
//  Expressions/DataWindowExpressionEvaluator.cs registers so the evaluator can resolve the call.
//  Those are the only two places the spelling occurs, and they must agree exactly or the
//  expression fails at run time in the manner described above.
//
//  This is an INTERNAL STRUCTURING CHOICE with no observable effect, recorded here because
//  constraint C-K requires such choices to be documented rather than discovered. It changes no
//  emitted byte: the constant's value is the legacy text transcribed character for character, and
//  the sibling test project asserts both constants against dwvaluetoexp.srf:L49 so the
//  transcription itself is pinned rather than trusted.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 2 - IDENTIFIERS ARE PASCAL CASE; THE LEGACY SPELLING LIVES IN THE STRING VALUE
//  --------------------------------------------------------------------------------------------
//  No member here is named `dwNvlString`. The refactor does preserve legacy identifier spellings
//  verbatim, but only for the SCREAMING_SNAKE constant catalogues whose exact text travels in
//  serialized payloads, log records and characterization recordings - and it pays for that with
//  narrowly scoped analyzer suppressions in the repository-root .editorconfig, one section per
//  declaring file, and its BAND 3 roster is the single source of truth for which files those are.
//  This one is not among them, and .editorconfig is
//  not this file's to change, so under the inherited TreatWarningsAsErrors a camelCase public
//  member would be a build failure rather than a style note.
//
//  Nothing is lost. What has to be byte exact is the EMITTED TEXT, and that is preserved exactly
//  where it belongs: in the value of the two constants below.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 3 - A NULL STRING AND AN EMPTY STRING ARE DIFFERENT STATES, ALWAYS
//  --------------------------------------------------------------------------------------------
//  This is the whole reason the sentinel exists, and the mechanism is visible in two lines:
//
//      dwvaluetoexp.srf:L48    sVal = "'" + val + "'"
//      dwvaluetoexp.srf:L49    if IsNull(sVal) then sVal = "dwNvlString()"
//
//  In PowerScript, concatenation with a null operand yields null. So L48 produces null if and only
//  if `val` is null, and L49 then substitutes the sentinel. When `val` is the EMPTY string, L48
//  produces the two-character text `''` and L49 does not fire at all. One input therefore yields
//  `dwNvlString()` and the other yields `''`, and they are not interchangeable.
//
//  Collapsing null to the empty string - or to any other default - would silently rewrite one of
//  those outputs into the other. AAP 0.4.5.4 names that collapse as a translation hazard in its
//  own right, because PowerBuilder has null for value types and the ported algebra depends on
//  null remaining representable and visible. Every member below that accepts or produces a value
//  keeps the two states distinct, and the nullable reference type annotations are what make that
//  checkable rather than merely intended.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 4 - THE COERCION ARM IS AN IDENTITY PASS-THROUGH, AND IT CANNOT FAIL
//  --------------------------------------------------------------------------------------------
//  se_cst_dw.sru:L231-L244 dispatches on `Left(dwo.ColType,5)`. Read the arms side by side and the
//  character arm is the odd one out - it is the only one with no conversion function:
//
//      L232-L233   case "char","char("            SetItem(row,Long(dwo.ID),data)
//      L234-L235   case "decim","real","numbe"    SetItem(row,Long(dwo.ID),Dec(data))
//      L236-L237   case "long","ulong"            SetItem(row,Long(dwo.ID),Long(data))
//      L238-L239   case "datet"                   SetItem(row,Long(dwo.ID),DateTime(data))
//      L240-L241   case "date"                    SetItem(row,Long(dwo.ID),Date(data))
//      L242-L243   case "time"                    SetItem(row,Long(dwo.ID),Time(data))
//
//  `data` is the raw edit text and it is written unchanged. Nothing parses it, nothing reformats
//  it, and there is no operation present that could fail. So the port neither throws nor reports:
//  it trims nothing, normalises nothing, truncates nothing, escapes nothing and re-encodes
//  nothing.
//
//  WIDTH IS NOT ENFORCED, AND THAT IS A PRESERVED DEFECT RATHER THAN AN OVERSIGHT. The primary
//  fixture declares its address column as `type=char(200)` [dw_sqlite.srd:L11] against the
//  repository's only DDL, which declares `ADDRESS CHAR(50)` [w_test_sqlite.srw:L467]. The 200
//  versus 50 mismatch is real, it is reachable from this exact arm, and AAP 0.6.4 records it as a
//  defect to preserve. A width check here would be a behaviour change dressed as a validation, and
//  constraint C-B forbids it.
//
//  A Try-shaped overload is offered for call-site symmetry with the sibling arms, which do have
//  something to report. Its own documentation states plainly that it can only ever return
//  RetCode.OK. No failure condition is invented to make the shape look symmetrical - that would
//  be a widening of the contract, and AAP 0.1.5 requires narrowing with a defined error instead.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 5 - THE HAZARD MAP FOR THE PREDICATE
//  --------------------------------------------------------------------------------------------
//  The identifiers are the shared hazard numbering used across this folder's ported arms, kept
//  as-is so a reader comparing the five files sees one scheme.
//
//      H2  `Left(s,5)` IS NOT `s.Substring(0,5)`. PowerBuilder's `Left` truncates safely and
//          returns whatever is there when the string is shorter than the requested length;
//          `Substring(0,5)` throws. The shorter case is not hypothetical: "char" is four
//          characters, so are the sibling arms' "date" and "time" [L240, L242], and L231 reads
//          `dwo.ColType` on whatever `dwobject` the event handed it - an object that is not a
//          column carries no column-type text at all, so an empty or short subject genuinely
//          reaches the truncation. Reproduced by a length guard, never by an unguarded slice.
//      H4  CASE SENSITIVITY IS THE CONTRACT. PowerScript `=` on strings, and therefore
//          `choose case`, is case sensitive. The arm literals at L232 are lowercase, and every
//          ColType observed in the repository is lowercase - `char(100)`, `char(200)`,
//          `decimal(2)`, `date`, `number` [dw_sqlite.srd:L8-L13]. Comparison is
//          StringComparison.Ordinal. OrdinalIgnoreCase would MATCH MORE than the legacy does,
//          which is a widening.
//      H5  NO CULTURE-SENSITIVE OPERATION OCCURS ANYWHERE IN THIS FILE. There is no `ToString`,
//          no `Convert.*`, no `string.Format`, no `ToUpper` or `ToLower`, no `Normalize` and no
//          culture-sensitive comparison overload. Characterization compares a legacy recording
//          against a target recording for the same workflow; a culture-sensitive operation would
//          make those diverge on the host's locale for reasons unrelated to the port. The classic
//          failure this avoids is Turkish casing, where the dotted and dotless `i` behave
//          differently from every other locale.
//      H6  MATCH BY EXACT EQUALITY ON THE TRUNCATED TEXT, NEVER BY `StartsWith`. `choose case`
//          compares for equality, so the arms are mutually exclusive. `StartsWith` is not a
//          harmless simplification, it is a defect: "character".StartsWith("char") is true, so a
//          `character` column would be claimed by this arm when the legacy leaves it to no arm at
//          all. The same mistake in a sibling file would route "datetime" into the "date" arm.
//          Truncate to five, then compare for equality - and keep all five files consistent.
//
//  --------------------------------------------------------------------------------------------
//  DECISION 6 - NO ESCAPING HELPER MAY EXIST IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The temptation is specific to this file because this is the string type, so it is closed off
//  explicitly. dwvaluetoexp.srf:L48 wraps a value in single quotes WITHOUT escaping any single
//  quote already inside it, so a value containing an apostrophe produces malformed expression
//  text. That defect belongs to Validators/ValueToExpression.cs, which ports L48 and preserves it.
//  It is not this file's to fix, and it is not this file's to pre-empt: adding an escaper here
//  would change the bytes that reach the evaluator and break parity in a way no compiler reports.
//  The pass-through fidelity tests assert that a value containing `'` survives untouched.
//
//  --------------------------------------------------------------------------------------------
//  NO PERFORMANCE OBJECTIVE IS ASSERTED ANYWHERE IN THIS FILE
//  --------------------------------------------------------------------------------------------
//  The repository publishes no service-level agreement, no latency budget, no throughput target
//  and no availability commitment, so none may be claimed or used to justify a choice here. Where
//  a member returns the original string instance because nothing needed changing, that is
//  described as WHAT IT DOES and is not offered as a performance property.
//
//  --------------------------------------------------------------------------------------------
//  GOVERNING CONSTRAINTS, AND HOW THIS FILE SATISFIES EACH
//  --------------------------------------------------------------------------------------------
//  THERE ARE NO USER-SPECIFIED RULES FOR THIS PROJECT. The rules document was retrieved and it
//  contains exactly one statement: that no user rules were provided. No rule is therefore
//  invented, inferred or back-filled from convention here, and the absence is not treated as
//  licence to lower the bar. The binding constraints in their place are the plan's own inventory
//  plus its enterprise-standard baseline:
//
//      C-A  Stays inside PowerFramework.DataServices. All 13 objects of
//           pfw.datawindow.services are assigned to this service, so nothing here is promoted to
//           a shared library, and no service reaches into another's internals.
//      C-B  No behaviour improvement. The null-versus-empty distinction, the conversion-free
//           write, the missing `case else`, the case-sensitive match and the tolerated width
//           mismatch are all reproduced as they are. No escaping, trimming, normalisation, width
//           enforcement or validation is added.
//      C-C  Every legacy path cited above is read-only specification. Nothing under ws_objects/
//           is edited, moved or reformatted by this file's existence.
//      C-D  No deferred-service concern appears: no dialog, no message box, no localization call,
//           no DPI conversion, no geometry, no font measurement, no rendering.
//      C-F  No secret. The sentinel spelling and the two prefix tokens are BEHAVIOURAL CONTRACT,
//           not configuration, so they stay compiled in. Moving them to appsettings.json would
//           make a parity-critical value environment dependent, which is the opposite of what
//           C-F is for.
//      C-H  Every member is static, pure and deterministic, with no I/O, no clock, no ambient
//           culture and no mutable state, so the sibling PowerFramework.DataServices.Tests
//           project can drive table-driven parity theories against the 80 percent per-service
//           line-coverage gate.
//      C-I  No PackageReference and no ProjectReference is added. The two imports below are the
//           base class library and one already-referenced shared project.
//      C-K  Every decision above is annotated at its point of reproduction with the legacy
//           locator it came from.
//      Not applicable, stated deliberately rather than omitted: C-E (no database is touched),
//      C-G (no boundary is created, so nothing to authenticate), C-J (no deployable unit is
//      defined) and C-L (no operational surface is configured).
// ==============================================================================================

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using PowerFramework.Shared.Kernel;

namespace PowerFramework.DataServices.Validators;

/// <summary>
/// The <c>dwNvlString</c> null producer and the character arm of the DataWindow item-change
/// type-coercion table.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>ws_objects/pfw.datawindow.services.pbl.src/dwnvlstring.srf</c> (13 lines) and
/// from the <c>case "char","char("</c> arm of the coercion table at
/// <c>se_cst_dw.sru:L232-L233</c>. A PowerScript <c>*.srf</c> global function becomes a
/// <see langword="static"/> method on a role-named static class, so this type has no instance
/// form, no interface and no participation in dependency injection.
/// </para>
/// <para>
/// Despite the folder name, <c>dwnvlstring.srf</c> validates nothing: it declares a typed local,
/// nulls it and returns it. Its only appearance anywhere in the 544-object legacy estate is the
/// string literal at <c>dwvaluetoexp.srf:L49</c>, and it is resolved by the DataWindow expression
/// evaluator reached through <c>Describe("Evaluate(...)")</c> [n_cst_dwsvc.sru:L195, L217] rather
/// than by the PowerScript compiler. The observable contract is therefore the emitted spelling,
/// published here as <see cref="FunctionName"/> and <see cref="NullLiteralExpression"/>.
/// </para>
/// <para>
/// Every member is pure and deterministic: no member performs I/O, reads a clock, consults
/// <c>CultureInfo.CurrentCulture</c>, mutates static state, or throws. The file-level comment
/// above records the decisions and the hazard map behind each of those properties.
/// </para>
/// </remarks>
public static class StringValidator
{
    // ==========================================================================================
    // REGION 1 - THE PUBLISHED SPELLING CONSTANTS
    // Ported from ws_objects/pfw.datawindow.services.pbl.src/dwvaluetoexp.srf:L49
    //     if IsNull(sVal) then sVal = "dwNvlString()"
    // and from the prototype it names, dwnvlstring.srf:L6
    //     global function string dwnvlstring ()
    // ==========================================================================================
    //
    // Both values are transcriptions, character for character, of text that reaches a DataWindow
    // expression evaluator. The casing is the legacy's own and is not normalised: the declaration
    // at dwnvlstring.srf:L6 is all lowercase because PowerScript is case insensitive for
    // identifiers, while the literal that is actually EMITTED at dwvaluetoexp.srf:L49 is mixed
    // case. The emitted form is the one that matters and the one reproduced here, because it is
    // the text a characterization recording will contain.
    //
    // See DECISION 1 for why these are constants rather than literals repeated at each use site,
    // and DECISION 2 for why the C# member names are PascalCase while the values are not.

    /// <summary>
    /// The bare function name the DataWindow expression evaluator resolves,
    /// <c>dwNvlString</c>, without parentheses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered by <c>Expressions/DataWindowExpressionEvaluator.cs</c> so that expression text
    /// containing a call to it can be evaluated. The legacy analogue is the global function
    /// object declared at <c>dwnvlstring.srf:L6</c>.
    /// </para>
    /// <para>
    /// This must agree with <see cref="NullLiteralExpression"/> exactly. The two are consumed at
    /// opposite ends of the same round trip - one registers the name, the other emits a call to
    /// it - and a mismatch is invisible to the compiler because resolution happens inside a
    /// string handed to <c>Describe("Evaluate(...)")</c> [n_cst_dwsvc.sru:L217]. It would surface
    /// only at run time, as the evaluator returning the text <c>"?"</c> or <c>"!"</c>, which is
    /// what <c>n_cst_dwsvc_columnexp.sru:L2391</c> tests for before reporting an error.
    /// </para>
    /// </remarks>
    public const string FunctionName = "dwNvlString";

    /// <summary>
    /// The complete null-sentinel expression, <c>dwNvlString()</c>, exactly as
    /// <c>dwvaluetoexp.srf:L49</c> emits it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Emitted by <c>Validators/ValueToExpression.cs</c> when a string value is null, in place of
    /// the quoted form that <c>dwvaluetoexp.srf:L48</c> produces for a non-null value. The
    /// substitution is what makes a null value expressible inside DataWindow expression text at
    /// all, and it is the reason <see cref="NullValue"/> must stay distinct from the empty string;
    /// see the file comment's DECISION 3.
    /// </para>
    /// <para>
    /// Byte-exact contract. The sibling test project asserts this value character for character
    /// against <c>dwvaluetoexp.srf:L49</c>, so the transcription is pinned rather than trusted.
    /// </para>
    /// </remarks>
    public const string NullLiteralExpression = "dwNvlString()";

    // ==========================================================================================
    // REGION 2 - THE TYPED NULL
    // Ported from ws_objects/pfw.datawindow.services.pbl.src/dwnvlstring.srf
    //     L6    global function string dwnvlstring ()
    //     L9    global function string dwnvlstring ();string nvl
    //     L10   SetNull(nvl)
    //     L11   return nvl
    //     L12   end function
    // ==========================================================================================
    //
    // The entire legacy body is three statements: declare a `string` local, null it, return it.
    // The declared TYPE is the point - PowerScript's `SetNull` nulls a variable of whatever type
    // it was declared as, so the four sibling producers differ from this one only in the type of
    // their local. Preserving the type is preserving the whole behaviour.
    //
    // Shape of the port: a method rather than a property, because AAP 0.4.5.2 maps a *.srf global
    // function to a static METHOD on a role-named static class, and because the legacy is invoked
    // with parentheses in the expression text that NullLiteralExpression emits. Keeping the call
    // shape aligned with the emitted text keeps the two readable together.

    /// <summary>
    /// Produces the typed null that <c>dwnvlstring.srf</c> returns: a <see cref="string"/>
    /// reference whose value is <see langword="null"/>.
    /// </summary>
    /// <returns>Always <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// The complete legacy body is <c>dwnvlstring.srf:L9-L11</c> - declare <c>string nvl</c>,
    /// <c>SetNull(nvl)</c>, <c>return nvl</c> - and this reproduces it exactly. The method takes no
    /// argument because the legacy prototype at <c>L6</c> takes none.
    /// </para>
    /// <para>
    /// NULL IS NOT THE EMPTY STRING, AND THE DIFFERENCE IS THE ENTIRE PURPOSE OF THIS MEMBER.
    /// <c>dwvaluetoexp.srf:L48</c> builds <c>"'" + val + "'"</c>, and PowerScript concatenation
    /// with a null operand yields null, so <c>L49</c> substitutes
    /// <see cref="NullLiteralExpression"/> if and only if the value was null. An empty value takes
    /// neither branch and yields the two-character text <c>''</c> instead. Anything that collapses
    /// null into <see cref="string.Empty"/> silently rewrites one of those two outputs into the
    /// other; AAP 0.4.5.4 names that collapse as a translation hazard in its own right.
    /// </para>
    /// <para>
    /// Returning a nullable reference rather than throwing or returning a sentinel string is what
    /// keeps the distinction checkable by the compiler under the repository-wide nullable
    /// reference type setting.
    /// </para>
    /// </remarks>
    public static string? NullValue()
    {
        // dwnvlstring.srf:L9-L11. A local is declared and nulled in the legacy because PowerScript
        // has no null literal of a given type; C# does, so the three statements collapse to one
        // return without changing what is produced.
        return null;
    }

    // ==========================================================================================
    // REGION 3 - THE ColType PREFIX TOKENS AND THE OWNERSHIP PREDICATE
    // Ported from ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
    //     L231   choose case Left(dwo.ColType,5)
    //     L232       case "char","char("
    // Corroborated by the second, independent prefix table at n_cst_dwsvc.sru
    //     L503   choose case Left(colType,5)
    //     L504       case "char","char("
    //     L505           return COL_TYPE_STRING
    // ==========================================================================================
    //
    // WHY THERE ARE TWO TOKENS AND WHY THE SECOND ENDS WITH AN OPEN PARENTHESIS
    //
    // The legacy never compares the full ColType. It compares only its first five characters, so
    // the token set is the set of five-character-or-shorter PREFIXES that a character column's
    // type text can produce. A bare `char` is four characters and survives truncation intact; a
    // parameterised `char(100)` truncates to exactly `char(`, taking the opening parenthesis with
    // it. The parenthesis is therefore load bearing and is NOT a typo to tidy away.
    //
    // That is measured on the primary fixture, the repository's only updatable DataWindow
    // [ws_objects/pfw.tests.pbl.src/dw_sqlite.srd:L8-L14], whose six columns are:
    //
    //     L8   type=number       ->  "numbe"   (id, key and identity)
    //     L9   type=char(100)    ->  "char("   <-- the parameterised form, in the fixture
    //     L10  type=number       ->  "numbe"
    //     L11  type=char(200)    ->  "char("   <-- and again
    //     L12  type=decimal(2)   ->  "decim"
    //     L13  type=date         ->  "date"
    //
    // Both character columns in the only fixture that exercises this arm take the `char(` token,
    // not the `char` one, which is why omitting it would break the fixture outright rather than
    // some rare edge. Every spelling observed is LOWERCASE, which is the evidence behind H4.
    //
    // Only two `Left(<coltype>,5)` sites exist in the whole 544-object estate - se_cst_dw.sru:L231
    // and n_cst_dwsvc.sru:L503 - and this is the one arm on which those two tables AGREE. The
    // tables are not otherwise identical: n_cst_dwsvc.sru groups "numbe" with "long"/"ulong"
    // [L506] while se_cst_dw.sru groups it with "decim"/"real" [L234], and n_cst_dwsvc.sru has a
    // `case else` [L516-L517] where se_cst_dw.sru has none [L244]. Neither divergence touches the
    // character arm, so nothing here has to choose between them.

    /// <summary>
    /// The number of leading characters of a column's <c>ColType</c> that the legacy compares:
    /// five, from <c>Left(dwo.ColType,5)</c> [se_cst_dw.sru:L231].
    /// </summary>
    /// <remarks>
    /// Published so that <see cref="ColTypePrefix"/>, <see cref="OwnsColType"/> and the sibling
    /// arms all derive the width from one place. The same width appears at
    /// <c>n_cst_dwsvc.sru:L503</c>, and those two sites are the only ones in the estate.
    /// </remarks>
    public const int ColTypePrefixLength = 5;

    /// <summary>
    /// The first token of the arm, <c>char</c> - the truncation of an unparameterised character
    /// column type [se_cst_dw.sru:L232].
    /// </summary>
    /// <remarks>
    /// Four characters, which is shorter than <see cref="ColTypePrefixLength"/>. That is the
    /// concrete reason the truncation in <see cref="ColTypePrefix"/> must be length safe; see
    /// hazard H2 in the file comment.
    /// </remarks>
    public const string ColTypeTokenChar = "char";

    /// <summary>
    /// The second token of the arm, <c>char(</c> - the truncation of a parameterised character
    /// column type such as <c>char(100)</c> [se_cst_dw.sru:L232].
    /// </summary>
    /// <remarks>
    /// THE TRAILING OPEN PARENTHESIS IS DELIBERATE AND MUST NOT BE REMOVED. It is what remains of
    /// <c>char(100)</c> after <c>Left(...,5)</c>, and both character columns of the primary
    /// fixture take this token rather than <see cref="ColTypeTokenChar"/>
    /// [dw_sqlite.srd:L9, L11].
    /// </remarks>
    public const string ColTypeTokenParameterizedChar = "char(";

    /// <summary>
    /// The complete, immutable token set for this arm, in the order
    /// <c>se_cst_dw.sru:L232</c> declares it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Consumed by <c>Domain/ItemChangeProtocol.cs</c>, which owns the <c>choose case</c> at
    /// <c>se_cst_dw.sru:L231-L244</c> and dispatches on it. This type supplies the arm's tokens
    /// and the arm's behaviour; it does not dispatch.
    /// </para>
    /// <para>
    /// Declaration order is preserved for readability against the legacy line, not because order
    /// carries meaning: <c>choose case</c> arms are equality tests and are mutually exclusive, so
    /// no token can shadow another. Membership is decided by exact ordinal equality - see
    /// <see cref="OwnsColType"/>, and hazards H4 and H6 in the file comment.
    /// </para>
    /// </remarks>
    public static ImmutableArray<string> ColTypeTokens { get; } =
        [ColTypeTokenChar, ColTypeTokenParameterizedChar];

    /// <summary>
    /// Reproduces <c>Left(colType, 5)</c> [se_cst_dw.sru:L231] - the length-safe truncation the
    /// legacy dispatches on.
    /// </summary>
    /// <param name="colType">
    /// A column's <c>ColType</c> text, as <c>Describe</c> reports it. May be shorter than
    /// <see cref="ColTypePrefixLength"/>, may be empty, and may be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The first <see cref="ColTypePrefixLength"/> characters of <paramref name="colType"/>, or
    /// the whole of it when it is shorter; <see langword="null"/> when
    /// <paramref name="colType"/> is <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// HAZARD H2 - <c>Left</c> IS NOT <c>Substring</c>. PowerBuilder's <c>Left</c> truncates
    /// safely and returns whatever is present when the input is shorter than the requested length.
    /// <c>colType.Substring(0, 5)</c> would instead throw
    /// <see cref="ArgumentOutOfRangeException"/>, turning a case the legacy handles silently into
    /// an exception. The short case is ordinary rather than exotic: <c>"char"</c> is four
    /// characters, so are the sibling arms' <c>"date"</c> and <c>"time"</c>
    /// [se_cst_dw.sru:L240, L242], and <c>L231</c> reads <c>dwo.ColType</c> on whatever
    /// <c>dwobject</c> the event supplied - an object that is not a column carries no column-type
    /// text at all. The conditional below is that guard.
    /// </para>
    /// <para>
    /// A <see langword="null"/> input propagates as <see langword="null"/>, matching PowerScript,
    /// where <c>Left</c> of a null argument is null and a null subject consequently equals no
    /// <c>case</c> arm at <c>se_cst_dw.sru:L232-L243</c>. That makes the answer here a DEFINED,
    /// non-throwing one that agrees with the legacy outcome - no arm is selected - rather than a
    /// widening of the contract.
    /// </para>
    /// <para>
    /// This member performs no culture-sensitive operation: slicing a string by index consults no
    /// <c>CultureInfo</c>. See hazard H5 in the file comment.
    /// </para>
    /// </remarks>
    [return: NotNullIfNotNull(nameof(colType))]
    public static string? ColTypePrefix(string? colType)
    {
        if (colType is null)
        {
            // PowerScript: Left(null,5) is null, and a null subject matches no `case` arm at
            // se_cst_dw.sru:L232-L243. Answered rather than thrown; see the remarks above.
            return null;
        }

        // se_cst_dw.sru:L231 `Left(dwo.ColType,5)`. HAZARD H2: the `<=` branch is what makes this
        // `Left` and not `Substring` - it returns the whole (possibly empty) string untouched
        // instead of throwing, exactly as PowerBuilder does.
        return colType.Length <= ColTypePrefixLength
            ? colType
            : colType[..ColTypePrefixLength];
    }

    /// <summary>
    /// Answers whether a column's <c>ColType</c> belongs to this arm - that is, whether
    /// <c>se_cst_dw.sru:L232</c>'s <c>case "char","char("</c> would be selected for it.
    /// </summary>
    /// <param name="colType">
    /// A column's <c>ColType</c> text, as <c>Describe</c> reports it. May be shorter than
    /// <see cref="ColTypePrefixLength"/>, may be empty, and may be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the truncated <c>ColType</c> equals one of
    /// <see cref="ColTypeTokens"/> exactly; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// HAZARD H6 - EXACT EQUALITY, NEVER <see cref="string.StartsWith(string)"/>. The legacy
    /// construct is <c>choose case</c>, which compares for equality, so its arms are mutually
    /// exclusive. A prefix test is not a harmless simplification but a genuine defect: a
    /// <c>character</c> column truncates to <c>"chara"</c> and matches no arm in the legacy, yet
    /// <c>"character".StartsWith("char")</c> is <see langword="true"/> and would hand it to this
    /// arm. Truncate first, then compare the truncation for equality.
    /// </para>
    /// <para>
    /// HAZARD H4 - THE COMPARISON IS <see cref="StringComparison.Ordinal"/>, DELIBERATELY CASE
    /// SENSITIVE. PowerScript's string <c>=</c>, and therefore <c>choose case</c>, is case
    /// sensitive; the arm literals at <c>L232</c> are lowercase and every <c>ColType</c> observed
    /// in the repository is lowercase [dw_sqlite.srd:L8-L13].
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> would match MORE inputs than the legacy
    /// does, which is a widening of the contract where AAP 0.1.5 requires narrowing with a defined
    /// error instead. An uppercase <c>"CHAR(50)"</c> therefore returns <see langword="false"/>,
    /// and a test asserts that.
    /// </para>
    /// <para>
    /// SILENCE ON A NON-MATCH IS ALSO CONTRACT. <c>se_cst_dw.sru:L244</c> closes the switch with
    /// no <c>case else</c> arm at all, so an unrecognised prefix performs no write and reports
    /// nothing - not an error, not a default coercion. This member reports only membership;
    /// reproducing that silence is <c>Domain/ItemChangeProtocol.cs</c>'s responsibility, and it
    /// must not be turned into a failure here.
    /// </para>
    /// <para>
    /// The semantic counterpart of a <see langword="true"/> answer is the column-type category
    /// <c>COL_TYPE_STRING = 1</c> [n_cst_dwsvc.sru:L27], reached through the second prefix table
    /// at <c>n_cst_dwsvc.sru:L503-L505</c>. That alignment is recorded as a comment on purpose:
    /// the constant is declared by <c>Domain/DataWindowServiceHost.cs</c> and a second declaration
    /// here would be a second source of truth.
    /// </para>
    /// </remarks>
    public static bool OwnsColType(string? colType)
    {
        string? prefix = ColTypePrefix(colType);
        if (prefix is null)
        {
            return false;
        }

        // se_cst_dw.sru:L232 `case "char","char("`. HAZARD H6: equality against each token, which
        // is what `choose case` does, rather than a prefix test. HAZARD H4: StringComparison
        // .Ordinal, because PowerScript string comparison is case sensitive and the arm literals
        // are lowercase. HAZARD H5: Ordinal reads no CultureInfo, so the answer cannot vary with
        // the host locale.
        foreach (string token in ColTypeTokens)
        {
            if (string.Equals(prefix, token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ==========================================================================================
    // REGION 4 - THE COERCION ARM
    // Ported from ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru
    //     L232       case "char","char("
    //     L233           SetItem(row,Long(dwo.ID),data)
    // ==========================================================================================
    //
    // One statement, and the whole of its behaviour is in what it OMITS. Every sibling arm wraps
    // `data` in a conversion function - Dec() at L235, Long() at L237, DateTime() at L239, Date()
    // at L241, Time() at L243 - and this one writes the raw edit text straight through. There is
    // no parse to fail, no format to reject and no state to consult, so the port is an identity
    // function that cannot fail and does not throw.
    //
    // The four things it must NOT do, each of which would be a behaviour change:
    //     * trim, because leading and trailing spaces are part of the edit text `data` carries;
    //     * escape, because dwvaluetoexp.srf:L48 does not, and preserving that defect is
    //       Validators/ValueToExpression.cs's job, not this file's to pre-empt (DECISION 6);
    //     * normalise or re-encode, because a Unicode normalisation form change would alter bytes
    //       that reach a characterization recording;
    //     * enforce the declared width, because the primary fixture's own 200-versus-50 mismatch
    //       [dw_sqlite.srd:L11 against w_test_sqlite.srw:L467] is a defect AAP 0.6.4 preserves.

    /// <summary>
    /// Reproduces the arm's write behaviour: the raw edit text is passed through unchanged
    /// [se_cst_dw.sru:L233].
    /// </summary>
    /// <param name="data">
    /// The raw edit text the DataWindow supplies for the changed item. May be
    /// <see langword="null"/>, may be empty, and the two are different states.
    /// </param>
    /// <returns>
    /// <paramref name="data"/>, unchanged and unexamined.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The legacy statement is <c>SetItem(row, Long(dwo.ID), data)</c> - and the significant fact
    /// is the ABSENCE of a conversion function around <c>data</c>, unlike every other arm of the
    /// same switch [<c>Dec</c> L235, <c>Long</c> L237, <c>DateTime</c> L239, <c>Date</c> L241,
    /// <c>Time</c> L243]. Because nothing is parsed or reformatted, nothing can fail: this method
    /// never throws for any input.
    /// </para>
    /// <para>
    /// NOTHING IS TRIMMED, ESCAPED, NORMALISED, TRUNCATED OR RE-ENCODED. Leading and trailing
    /// whitespace, embedded single and double quotes, embedded CR/LF and non-ASCII characters all
    /// survive verbatim, and tests assert each of those. In particular the single quote is left
    /// alone deliberately: <c>dwvaluetoexp.srf:L48</c> wraps a value in single quotes without
    /// escaping any quote inside it, and that defect belongs to
    /// <c>Validators/ValueToExpression.cs</c>, which ports <c>L48</c>. Escaping here would change
    /// the bytes that reach the expression evaluator in a way no compiler reports.
    /// </para>
    /// <para>
    /// THE COLUMN'S DECLARED WIDTH IS NOT ENFORCED, AND THAT IS A PRESERVED DEFECT. The primary
    /// fixture declares its address column <c>type=char(200)</c> [dw_sqlite.srd:L11] against the
    /// repository's only DDL, which declares <c>ADDRESS CHAR(50)</c> [w_test_sqlite.srw:L467]. The
    /// mismatch is reachable from this arm and AAP 0.6.4 records it as behaviour to preserve, so a
    /// width check would be a behaviour change dressed as a validation.
    /// </para>
    /// <para>
    /// A <see langword="null"/> input returns <see langword="null"/> rather than
    /// <see cref="string.Empty"/>. The two are not interchangeable; see the file comment's
    /// DECISION 3 for the <c>dwvaluetoexp.srf:L48-L49</c> mechanism that depends on the
    /// difference.
    /// </para>
    /// </remarks>
    [return: NotNullIfNotNull(nameof(data))]
    public static string? Coerce(string? data)
    {
        // se_cst_dw.sru:L233 `SetItem(row,Long(dwo.ID),data)`. The raw edit text, with no
        // conversion function around it, and therefore nothing to do here but hand it back. Null
        // is preserved as null rather than collapsed (DECISION 3), and the value is not inspected
        // at all, which is what makes the no-trim, no-escape, no-normalise and no-width-check
        // guarantees structural rather than merely intended.
        return data;
    }

    /// <summary>
    /// The Try-shaped form of <see cref="Coerce(string?)"/>, offered for call-site symmetry with
    /// the sibling arms. <strong>It can only ever return <see cref="RetCode.OK"/>.</strong>
    /// </summary>
    /// <param name="data">
    /// The raw edit text the DataWindow supplies for the changed item. May be
    /// <see langword="null"/>, may be empty, and the two are different states.
    /// </param>
    /// <param name="value">
    /// Receives <paramref name="data"/> unchanged - the same result
    /// <see cref="Coerce(string?)"/> returns, including <see langword="null"/> for a
    /// <see langword="null"/> input.
    /// </param>
    /// <returns>
    /// Always <see cref="RetCode.OK"/>, which is <c>0</c> [retcode.sru:L39].
    /// </returns>
    /// <remarks>
    /// <para>
    /// THIS OVERLOAD EXISTS FOR SHAPE, NOT FOR OUTCOME, AND CALLERS MUST NOT DEPEND ON ITS RETURN
    /// VALUE. The character arm at <c>se_cst_dw.sru:L233</c> performs no conversion, so it has
    /// nothing that could fail and nothing to report. The sibling arms wrap <c>data</c> in
    /// <c>Dec</c>, <c>Long</c>, <c>DateTime</c>, <c>Date</c> or <c>Time</c> [L235, L237, L239,
    /// L241, L243] and can therefore be given something to report; this one cannot. No failure
    /// condition is invented here to make the five shapes look alike - a fabricated failure would
    /// widen the contract, and AAP 0.1.5 requires narrowing with a defined error rather than
    /// widening with a guess.
    /// </para>
    /// <para>
    /// The item-change path in <c>Domain/ItemChangeProtocol.cs</c> must not branch on this result.
    /// The legacy arm writes and falls through to <c>Event OnDoItemChanged(row, dwo)</c>
    /// [se_cst_dw.sru:L247] and then to the forcible <c>rtCode = 2</c> [L250], with no error path
    /// between them, and a caller that treated a return code here as meaningful would invent a
    /// branch the legacy does not have.
    /// </para>
    /// <para>
    /// The return type is <see cref="long"/> rather than <see cref="bool"/> because the legacy
    /// return-code algebra is numeric and tri-state, and <see cref="RetCode"/> declares its
    /// constants as <see cref="long"/> [retcode.sru:L39-L79]. Reducing it to a Boolean here would
    /// pre-empt that algebra at exactly the point where the plan preserves it.
    /// </para>
    /// </remarks>
    public static long TryCoerce(string? data, [NotNullIfNotNull(nameof(data))] out string? value)
    {
        value = Coerce(data);

        // retcode.sru:L39 `Constant Long OK = 0`. Unconditional, and unconditional by construction
        // rather than by luck: the arm at se_cst_dw.sru:L233 contains no operation that can fail,
        // so there is no state under which any other code would be correct.
        return RetCode.OK;
    }
}
