// ==============================================================================================
//  ThrowExceptionTests - the parity suite for the PowerFramework throw helpers
//  --------------------------------------------------------------------------------------------
//  UNITS UNDER TEST
//      PowerFramework.Shared.Kernel.Exceptions   from shared/PowerFramework.Shared.Kernel/
//                                                     ThrowException.cs
//      PowerFramework.Shared.Kernel.PfwException from shared/PowerFramework.Shared.Kernel/
//                                                     PfwException.cs, reached through the
//                                                     helpers and asserted for its message
//                                                     decoration
//
//  THE ORACLE, AND THE FACT THAT IT IS THE WHOLE ORACLE
//  --------------------------------------------------------------------------------------------
//  Three read-only legacy files specify every behaviour asserted below:
//
//      ws_objects/pfw.shared.pbl.src/throwexception.srf      33 lines, two subroutines
//      ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf   11 lines, one subroutine
//      ws_objects/pfw.shared.pbl.src/pfwexception.sru        35 lines, the exception type
//
//  There is NO legacy test window for these primitives. A sweep of the 47 w_test_*.srw files in
//  ws_objects/pfw.tests.pbl.src finds no w_test_throwexception; the nearest, w_test_assert.srw,
//  exercises the separate Diagnostics assert path and not this one. The three source bodies above
//  are therefore the entire specification, which is why every assertion below either cites the
//  :L locator it was derived from or is explicitly labelled CHARACTERIZES THE PORT - meaning the
//  legacy does not settle the point and this suite is pinning the implementation's documented
//  choice so a later run against the behavioural oracle can revisit it without archaeology.
//
//  Those files are READ ONLY and are never opened at run time. Nothing in this suite touches a
//  ws_objects path, reads a file, or reaches the network: every expected value is a literal typed
//  here from the source, and every locator is a comment.
//
//  THE TWO NAMED OBLIGATIONS
//  --------------------------------------------------------------------------------------------
//   1. THE EMPTY-CLASS-NAME FORM RETURNS WITHOUT THROWING. Pinned by
//      TwoArgumentForm_WithEmptyClassName_SilentlyReturns_PreservedLegacyDefect and its three
//      companions in region 3. That is a PRESERVED DEFECT, and ThrowException.cs:L411-L412 names
//      this very test as the thing standing between the guard and a future "fix".
//   2. THE FRAMEWORK EXCEPTION'S MESSAGE CARRIES THE MANDATORY PREFIX. Pinned by
//      FrameworkForm_MessageCarriesTheMandatoryPrefix_SeparatedBySingleLineFeed and its
//      companions in region 2.
//
//  WHY THE EXPECTED VALUES ARE RETYPED FROM THE ORACLE RATHER THAN READ OFF THE IMPLEMENTATION
//  --------------------------------------------------------------------------------------------
//  ExpectedMessagePrefix, ExpectedMessageSeparator and LegacyClassName below are typed out from
//  pfwexception.sru and pfwthrowexception.srf, NOT aliased to PfwException.MessagePrefix,
//  PfwException.MessageSeparator and PfwException.TypeName. That is deliberate and it is the
//  difference between a test and a tautology: asserting PfwException.MessagePrefix equals
//  PfwException.MessagePrefix would pass no matter what the constant said, so an edit to the
//  wording would sail through. The constants ARE additionally asserted equal to these literals -
//  see PrefixConstants_MatchTheOracleCharacterForCharacter - which is what makes it safe for the
//  rest of the suite to build expected strings from either side.
//
//  CONSTRAINT SELF-AUDIT
//  --------------------------------------------------------------------------------------------
//  No user rules exist for this project: review_rules returns exactly "No user rules provided",
//  a single line. Nothing here is written to satisfy a user rule and none is inferred. The
//  binding constraints are the refactor plan's own non-rule inventory (section 0.7.3) and its
//  enterprise-standard baseline (section 0.7.2), held to in the rules' absence rather than
//  treating that absence as licence to lower the bar.
//
//  C-B  Behaviour is replicated, never improved, and every reproduced defect is annotated where
//       it is reproduced. Region 3 asserts that an empty class name raises NOTHING, and states
//       the consequence in full at the assertion. No test here asserts argument validation,
//       because the implementation must not have any: an ArgumentException on the empty name is
//       the "helpful" fix this suite exists to block. Region 6 likewise pins the unconditional,
//       non-idempotent decoration - including the double-prefix outcome - rather than treating it
//       as a bug.
//  C-C  The legacy tree is read only and is the oracle. Locators are cited in comments; no legacy
//       file is opened at run time; no legacy licence header and no legacy Chinese comment block
//       is copied into this file.
//  C-H  Nullable reference types and warnings-as-errors are inherited from the repository-root
//       Directory.Build.props and are not relaxed. There is no null-forgiving `!` operator
//       anywhere in this file, no #pragma, no #nullable directive and no NoWarn. The two places
//       that must push a null through a non-nullable parameter - because the implementation
//       documents what happens when a nullable-oblivious caller does exactly that - go through
//       NullReferenceTypedAsString and NullReferenceTypedAsFactory, which obtain the null from an
//       uninitialised array element. See the remarks on those two helpers.
//  C-K  Every technology-specific decision is documented where it is made: the single line feed
//       and why the assertion is written against an explicit "\n" escape, the retyped-literal
//       policy above, the ordinal-versus-culture comparison and the Turkish-i premise that makes
//       it observable, the null-laundering technique, the process-global registry and the unique
//       naming that keeps its tests independent, and the limits of what a stack-trace assertion
//       can portably observe.
//
//  NAMING DISCIPLINE, WHICH WOULD OTHERWISE BREAK THE BUILD
//  --------------------------------------------------------------------------------------------
//  The repository-root .editorconfig scopes its naming-analyzer suppressions by single-file glob
//  to the ten implementation files that genuinely carry the preserved SCREAMING_SNAKE constant
//  spellings. No test file is covered, and TreatWarningsAsErrors is on. Every identifier declared
//  here is therefore conventional C#, and the legacy lowercase object name appears only as a
//  string VALUE - LegacyClassName - never as an identifier.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * A test for the UnreachableException at ThrowException.cs:L466. It guards a state the
//      compiler cannot prove impossible but which is impossible in fact - PfwException.TypeName
//      is a non-empty compile-time constant, so the empty-name guard can never return on that
//      path. There is no input that reaches it, so there is nothing to assert. Region 5 pins the
//      property that keeps it unreachable instead: the seeded registry entry cannot be displaced.
//    * A test for GetExportedTypesOrEmpty's four absorbed inspection faults. Reaching them needs
//      an assembly whose public signatures reference a missing or unloadable dependency, which
//      cannot be produced from inside this project without writing to disk and loading it - and
//      the implementation deliberately performs no assembly loading at all. Recorded as a
//      genuine coverage gap rather than simulated with a mock that would prove nothing.
//    * An assertion that base.Message retains the caller's UNDECORATED wording. PfwException
//      overrides Message, and the base property is not reachable from outside the type, so the
//      claim in PfwException.cs is not externally observable. Asserting it would require
//      reflection over a private BCL field, which pins an implementation detail of the runtime
//      rather than a behaviour of this port.
//    * A generic-exception probe for the IsGenericTypeDefinition filter. A generic type's
//      Type.Name carries its arity suffix, so a plain class name can never match one in the
//      first place; the guard is defence in depth and has no reachable input.
//    * Any use of Environment.NewLine. See region 2.
// ==============================================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Kernel.Tests;

/// <summary>
/// Parity tests for <see cref="Exceptions"/> and for the message decoration
/// <see cref="PfwException"/> applies on its behalf.
/// </summary>
/// <remarks>
/// Sealed because nothing derives from it, and stateless because every member under test is
/// static. The one piece of process-global state either unit owns is the class-name registry
/// inside <see cref="Exceptions"/>, which has no removal operation; region 5 explains how these
/// tests stay independent of one another in spite of that.
/// </remarks>
public sealed class ThrowExceptionTests
{
    /// <summary>
    /// The legacy class-name string, typed out from
    /// <c>ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf:L9</c>, whose entire body is
    /// <c>ThrowException("pfwexception",text)</c>.
    /// </summary>
    /// <remarks>
    /// Lowercase because PowerBuilder normalises object names that way, and it is spelled
    /// lowercase at <c>pfwexception.sru:L8-L9</c> too. This is a VALUE, not an identifier, so it
    /// needs no naming-analyzer suppression. Whether the port's resolution is case sensitive is
    /// an implementation decision rather than an assumption, and it is asserted in region 4
    /// rather than assumed here.
    /// </remarks>
    private const string LegacyClassName = "pfwexception";

    /// <summary>
    /// The decoration prefix, typed out character for character from
    /// <c>ws_objects/pfw.shared.pbl.src/pfwexception.sru:L23</c>.
    /// </summary>
    /// <remarks>
    /// Three words, one space between each, and no trailing space before the escape. Verified
    /// against the source byte by byte;
    /// <see cref="PrefixConstants_MatchTheOracleCharacterForCharacter"/> re-verifies the spacing
    /// structurally so a stray space cannot hide inside a string comparison a reader skims past.
    /// </remarks>
    private const string ExpectedMessagePrefix = "PowerFramework Runtime Error";

    /// <summary>
    /// The separator between the prefix and the caller's text: a single line feed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>pfwexception.sru:L23</c> writes the escape <c>~n</c>, which is PowerScript for ONE line
    /// feed, U+000A. It is not a carriage-return / line-feed pair, and the legacy message is
    /// identical on every platform.
    /// </para>
    /// <para>
    /// This is written as the escape <c>"\n"</c> for two reasons that are both real portability
    /// traps rather than style preferences. <see cref="Environment.NewLine"/> is LF on Linux and
    /// CRLF on Windows, so an assertion written against it would pass on Linux CI and fail on a
    /// Windows developer's machine - or, if the implementation were wrong in the same direction,
    /// the reverse, which is worse because CI would go green. And a verbatim or raw string literal
    /// containing a real line break carries whatever line ending the file happens to be checked
    /// out with, which is governed by <c>.gitattributes</c> and by the local
    /// <c>core.autocrlf</c> setting rather than by this source. The escape is immune to both.
    /// </para>
    /// </remarks>
    private const string ExpectedMessageSeparator = "\n";

    /// <summary>
    /// The parameter-type list of the one-argument <c>throwexception</c> overload
    /// [throwexception.srf:L6].
    /// </summary>
    /// <remarks>
    /// Hoisted into a static field rather than written inline at each reflection call site, so the
    /// array is allocated once and no constant array is passed as an argument.
    /// </remarks>
    private static readonly Type[] OneStringParameter = [typeof(string)];

    /// <summary>
    /// The parameter-type list of the two-argument <c>throwexception</c> overload
    /// [throwexception.srf:L7].
    /// </summary>
    private static readonly Type[] TwoStringParameters = [typeof(string), typeof(string)];

    /// <summary>
    /// The three words of <see cref="ExpectedMessagePrefix"/>, in order.
    /// </summary>
    /// <remarks>
    /// Hoisted into a static field rather than written inline so that no constant array is passed as
    /// an argument. Splitting the prefix on a single space must yield exactly these three entries: a
    /// double space anywhere would produce an empty entry and a trailing space would produce a
    /// fourth, so this catches the two spacing mistakes a whole-string comparison reports only as an
    /// opaque inequality.
    /// </remarks>
    private static readonly string[] ExpectedPrefixWords = ["PowerFramework", "Runtime", "Error"];

    /// <summary>
    /// Message texts exercised against every entry point.
    /// </summary>
    /// <value>
    /// The empty string; the exact wording of the framework's own live call site,
    /// <c>pfwThrowException("Invalid method")</c> at
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:860</c>; a two-line text,
    /// because the decoration introduces a line feed of its own and the two must not be confused;
    /// a text with significant leading and trailing spaces, because neither entry point may trim;
    /// and a non-ASCII text, because the legacy framework's own diagnostics are Chinese and a
    /// decoration that mishandled multi-byte text would otherwise go unnoticed. That last row is
    /// test input written for this suite, NOT a string lifted from a legacy file: C-C keeps the
    /// legacy tree read-only and this file copies no legacy licence header and no legacy comment
    /// block, only <c>:L</c> locators in comments.
    /// </value>
    /// <remarks>
    /// No row contains <see cref="ExpectedMessagePrefix"/>. That is a precondition of the
    /// prefix-absence assertion in region 1, and the case of a text that DOES already look
    /// decorated is covered separately by
    /// <see cref="OneArgumentForm_PassesTextThroughVerbatim_EvenWhenItAlreadyLooksDecorated"/>.
    /// The type argument is <see cref="string"/> throughout: xunit's analyzer rejects theory data
    /// whose type arguments may not be serializable, and under warnings-as-errors that rejection
    /// is a build failure, so no delegate or <see cref="Type"/> is ever carried in theory data in
    /// this file.
    /// </remarks>
    public static TheoryData<string> MessageTexts =>
    [
        "",
        "Invalid method",
        "first line\nsecond line",
        "  padded  ",
        "对象不存在",
    ];

    // ==========================================================================================
    //  REGION 1 - THE ONE-ARGUMENT FORM
    //  ws_objects/pfw.shared.pbl.src/throwexception.srf:L6, L10-L18
    //
    //      global subroutine throwexception (readonly string text)
    //          runtimeerror ex
    //          try
    //              ex = Create RuntimeError
    //              ex.SetMessage(text)
    //              throw ex
    //          catch(throwable e)
    //              throw e
    //          end try
    //
    //  A plain runtime error carrying the text unchanged. This region exists as much for the
    //  CONTRAST it establishes as for its own sake: it is what gives region 2's prefix assertion
    //  its meaning. If the one-argument form also decorated, "the framework form applies the
    //  prefix" would be an empty statement, so the ABSENCE of the prefix here is asserted
    //  explicitly rather than left implied.
    // ==========================================================================================

    /// <summary>
    /// The one-argument form raises a plain <see cref="Exception"/> carrying the caller's text
    /// verbatim and WITHOUT the framework prefix.
    /// </summary>
    /// <param name="text">The message text, from <see cref="MessageTexts"/>.</param>
    /// <remarks>
    /// Derived from <c>throwexception.srf:L13-L15</c>: create, set the message, throw. The three
    /// legacy steps are one construction in the port because <see cref="Exception"/> takes its
    /// message through its constructor, which is the managed equivalent of that
    /// <c>SetMessage</c> call.
    /// <para>
    /// CHARACTERIZES THE PORT for the exception TYPE only. The legacy creates PowerBuilder's
    /// <c>RuntimeError</c>, which has no single unambiguous BCL counterpart;
    /// ThrowException.cs records choice 1 - <see cref="Exception"/> itself, by the same
    /// correspondence PfwException.cs uses for <c>from runtimeerror</c> - and names this test as
    /// what pins it. The exact-type assertion is therefore the point: a future edit to
    /// <see cref="SystemException"/>, to <see cref="ApplicationException"/> or to
    /// <see cref="PfwException"/> must fail here rather than pass silently.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(MessageTexts))]
    public void OneArgumentForm_ThrowsPlainException_WithoutFrameworkPrefix_InferredBaseTypeMapping(
        string text)
    {
        Exception thrown = Assert.Throws<Exception>(() => Exceptions.ThrowException(text));

        // Exact type, not merely assignable: Assert.Throws matches the type exactly, so this also
        // establishes that the one-argument form does NOT raise PfwException.
        Assert.IsType<Exception>(thrown);
        Assert.IsNotType<PfwException>(thrown);

        // The text survives byte for byte - no trimming, no normalisation, no decoration.
        Assert.Equal(text, thrown.Message);

        // The contrast that gives region 2 its meaning. No row of MessageTexts contains the
        // prefix, so this is a genuine absence rather than an accident of the data.
        Assert.DoesNotContain(ExpectedMessagePrefix, thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-argument form does not decorate even when the caller's text already looks like a
    /// decorated message.
    /// </summary>
    /// <remarks>
    /// The complement of the row-level precondition in <see cref="MessageTexts"/>, and an
    /// adversarial guard in its own right: an implementation that "helpfully" prefixed here would
    /// produce the prefix twice for this input and would be caught, whereas a suite whose data
    /// never contains the prefix could only catch the single-prefix case.
    /// </remarks>
    [Fact]
    public void OneArgumentForm_PassesTextThroughVerbatim_EvenWhenItAlreadyLooksDecorated()
    {
        string alreadyDecorated =
            ExpectedMessagePrefix + ExpectedMessageSeparator + "handed back in";

        Exception thrown = Assert.Throws<Exception>(
            () => Exceptions.ThrowException(alreadyDecorated));

        Assert.Equal(alreadyDecorated, thrown.Message);

        // Exactly one occurrence. Counting is what distinguishes "not decorated" from
        // "decorated once more", which a substring test alone cannot do.
        Assert.Equal(1, CountOccurrences(thrown.Message, ExpectedMessagePrefix));
    }

    // ==========================================================================================
    //  REGION 2 - NAMED OBLIGATION TWO: THE MANDATORY MESSAGE PREFIX
    //  ws_objects/pfw.shared.pbl.src/pfwexception.sru:L23
    //
    //      public subroutine setmessage (string newmessage)
    //          super::SetMessage("PowerFramework Runtime Error~n" + newMessage)
    //
    //  and ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf:L9
    //
    //      ThrowException("pfwexception",text)
    //
    //  C-K, STATED HERE BECAUSE THIS IS WHERE THE DECISION BITES: the separator is ONE LINE FEED,
    //  U+000A. PowerScript's ~n is LF, not CRLF. Every assertion below is written against the
    //  explicit escape "\n" through ExpectedMessageSeparator.
    //
    //  Environment.NewLine IS DELIBERATELY NOT USED ANYWHERE IN THIS FILE. It resolves to LF on
    //  Linux and to CRLF on Windows, so an assertion written against it would agree with a
    //  CRLF implementation on Windows and with an LF implementation on Linux - which means the
    //  suite would pass on this Linux CI while failing on a Windows developer's machine, or pass
    //  on Windows while failing here. Either way the disagreement would look like an environment
    //  problem rather than the behavioural regression it actually is.
    //
    //  Note what is NOT asserted, and why: this file never asserts that the separator DIFFERS from
    //  Environment.NewLine. On Linux they are equal, so such an assertion would itself be
    //  platform dependent - exactly the trap being avoided. What is asserted instead is
    //  structural and platform independent: length one, character code ten, and no carriage
    //  return anywhere in the decorated message.
    // ==========================================================================================

    /// <summary>
    /// NAMED OBLIGATION TWO. The framework-specific form raises <see cref="PfwException"/> whose
    /// message is exactly the prefix, then a single line feed, then the caller's text.
    /// </summary>
    /// <param name="text">The message text, from <see cref="MessageTexts"/>.</param>
    /// <remarks>
    /// Derived from <c>pfwthrowexception.srf:L9</c> together with <c>pfwexception.sru:L23</c>. The
    /// expected string is composed from the two literals typed out of the oracle at the top of
    /// this file, not from the implementation's own constants, so that a change to either constant
    /// fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MessageTexts))]
    public void FrameworkForm_MessageCarriesTheMandatoryPrefix_SeparatedBySingleLineFeed(
        string text)
    {
        PfwException thrown = Assert.Throws<PfwException>(
            () => Exceptions.PfwThrowException(text));

        string expected = ExpectedMessagePrefix + ExpectedMessageSeparator + text;
        Assert.Equal(expected, thrown.Message);

        // The same expectation restated three ways, because each catches a different mistake that
        // string equality alone reports identically as "not equal".
        //
        //   The prefix is a PREFIX - it leads, it is not appended or interleaved.
        Assert.StartsWith(ExpectedMessagePrefix, thrown.Message, StringComparison.Ordinal);

        //   The character immediately after the prefix is the line feed itself, so a CRLF
        //   implementation fails on this line and names the exact offending position.
        Assert.Equal('\n', thrown.Message[ExpectedMessagePrefix.Length]);

        //   Exactly one character separates prefix from text: prefix + 1 + text, no more. This is
        //   what a CRLF separator would break even if the surrounding text also contained '\n'.
        Assert.Equal(ExpectedMessagePrefix.Length + 1 + text.Length, thrown.Message.Length);

        // No carriage return is introduced anywhere. Asserted over the WHOLE message rather than
        // just the separator, because a decoration that emitted "\r\n" would otherwise only be
        // caught by the length check above, and this states the intent directly.
        Assert.DoesNotContain("\r", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prefix and separator constants published by <see cref="PfwException"/> match the oracle
    /// character for character.
    /// </summary>
    /// <remarks>
    /// This is the test that makes it safe for the rest of the suite to compose expected messages
    /// from either side of the contract. The wording is asserted whole and then decomposed, so a
    /// double space, a trailing space or a changed word is reported precisely rather than as one
    /// opaque inequality. Derived from <c>pfwexception.sru:L23</c>.
    /// </remarks>
    [Fact]
    public void PrefixConstants_MatchTheOracleCharacterForCharacter()
    {
        Assert.Equal(ExpectedMessagePrefix, PfwException.MessagePrefix);
        Assert.Equal(ExpectedMessageSeparator, PfwException.MessageSeparator);

        // Structural decomposition of the wording: three words, single-space separated.
        Assert.Equal(ExpectedPrefixWords, PfwException.MessagePrefix.Split(' '));

        // No leading or trailing whitespace. The legacy literal ends at "Error", immediately
        // before the ~n escape, with nothing between them.
        Assert.Equal(PfwException.MessagePrefix, PfwException.MessagePrefix.Trim());

        // The separator, structurally: one character, code point ten.
        Assert.Equal(1, PfwException.MessageSeparator.Length);
        Assert.Equal('\n', PfwException.MessageSeparator[0]);
        Assert.Equal(10, (int)PfwException.MessageSeparator[0]);
        Assert.DoesNotContain("\r", PfwException.MessageSeparator, StringComparison.Ordinal);
    }


    // ==========================================================================================
    //  REGION 3 - NAMED OBLIGATION ONE: THE PRESERVED DEFECT
    //  ws_objects/pfw.shared.pbl.src/throwexception.srf:L21-L32, and specifically :L23
    //
    //      global subroutine throwexception (readonly string cls, readonly string text)
    //          throwable ex
    //          if cls = "" then return          <-- :L23  THE DEFECT
    //          try
    //              ex = Create Using cls
    //              ex.SetMessage(text)
    //              throw ex
    //          catch(throwable e)
    //              throw e
    //          end try
    //
    //  C-B - THIS IS A DEFECT THAT IS DELIBERATELY PINNED, NOT A BUG THAT ESCAPED REVIEW.
    //  --------------------------------------------------------------------------------------
    //  A subroutine whose entire purpose is to raise silently does nothing at all for one input.
    //  THE CONSEQUENCE, STATED PLAINLY: a caller written on the assumption that this always
    //  raises simply CARRIES ON EXECUTING past the call site. Whatever invariant the caller was
    //  relying on the throw to enforce is not enforced, and the code after the call runs with
    //  that invariant broken. There is no log record, no return code and no diagnostic of any
    //  kind - the call is indistinguishable from a no-op.
    //
    //  It looks exactly like a forgotten argument check, and the obvious "fix" is an
    //  ArgumentException on the empty name. That fix is FORBIDDEN. Constraint C-B requires
    //  documented legacy defects to be replicated rather than corrected, and the fail-fast posture
    //  the plan mandates elsewhere governs STRUCTURAL faults - the Gateway composition root and
    //  the Diagnostics assert path, where pfw.sra:L111-L144 unpacks a seven-field assert payload
    //  and then executes HALT CLOSE. It does not license hardening this path, whose legacy
    //  behaviour is to return.
    //
    //  ThrowException.cs:L411-L412 names TwoArgumentForm_WithEmptyClassName_SilentlyReturns_
    //  PreservedLegacyDefect as the test that stops a future contributor from removing the guard.
    //  This is that test. Nothing in this region asserts argument validation, because the
    //  implementation must not have any.
    //
    //  THE ASSERTION IS POSITIVE, NOT AN ABSENT Assert.Throws. An empty test body with no
    //  Assert.Throws technically "passes" for a silent return, but it reads like a forgotten test
    //  and would pass equally if the call were deleted. So each test below proves the call RETURNED
    //  by recording a fact that only a normal return can record, and then also confirms that
    //  nothing was thrown.
    // ==========================================================================================

    /// <summary>
    /// NAMED OBLIGATION ONE. An empty class name makes the two-argument form return normally and
    /// raise nothing at all.
    /// </summary>
    /// <param name="text">
    /// The message text, from <see cref="MessageTexts"/>. It is discarded by the legacy guard
    /// before it is ever used, and the theory carries the full row set precisely to establish that
    /// no text - not the empty string, not a multi-line one, not a non-ASCII one - reaches past the
    /// guard.
    /// </param>
    /// <remarks>
    /// PRESERVED LEGACY DEFECT [ws_objects/pfw.shared.pbl.src/throwexception.srf:L23]. See the
    /// region header for the full analysis and for the consequence to callers. The guard is
    /// <c>if cls = "" then return</c> and nothing wider, which is why
    /// <see cref="TwoArgumentForm_WithWhitespaceOnlyClassName_IsNotTreatedAsEmpty_ReachesResolution"/>
    /// exists alongside this test.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MessageTexts))]
    public void TwoArgumentForm_WithEmptyClassName_SilentlyReturns_PreservedLegacyDefect(
        string text)
    {
        // The positive half. This local can only become true if control reached the statement
        // AFTER the call, which is exactly the observable a silent return produces and a throw
        // does not. It is the caller's-eye view of the defect: execution continues.
        bool executionContinuedPastTheCall = false;

        Exception? captured = Record.Exception(() =>
        {
            Exceptions.ThrowException(string.Empty, text);
            executionContinuedPastTheCall = true;
        });

        Assert.True(
            executionContinuedPastTheCall,
            "throwexception.srf:L23 returns without raising on an empty class name, so execution "
            + "must continue past the call. A caller relying on this to raise carries on with its "
            + "invariant unenforced - that is the preserved defect, and it must not be 'fixed'.");

        // The negative half, which also rules out the specific "helpful" fix this suite exists to
        // block: an ArgumentException on the empty name would be captured here and named in the
        // failure output rather than passing as some unrelated success.
        Assert.Null(captured);
    }

    /// <summary>
    /// The empty-class-name guard applies for every message text, including a text that would
    /// otherwise have produced a fully decorated framework exception.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFECT [throwexception.srf:L23]. The complement of region 2: the identical
    /// text raises a decorated <see cref="PfwException"/> when the class name is the legacy one and
    /// raises nothing whatever when the class name is empty. Asserting both halves side by side is
    /// what shows the guard is about the CLASS NAME alone and has nothing to do with the text.
    /// </remarks>
    [Fact]
    public void TwoArgumentForm_EmptyClassNameSuppressesTheRaiseThatANamedClassWouldProduce()
    {
        const string text = "Invalid method";

        // With the legacy class name: raises, and decorates.
        PfwException raised = Assert.Throws<PfwException>(
            () => Exceptions.ThrowException(LegacyClassName, text));
        Assert.Equal(ExpectedMessagePrefix + ExpectedMessageSeparator + text, raised.Message);

        // With an empty class name and the SAME text: nothing at all.
        int statementsReachedAfterTheCall = 0;
        Exception? captured = Record.Exception(() =>
        {
            Exceptions.ThrowException(string.Empty, text);
            statementsReachedAfterTheCall++;
        });

        Assert.Null(captured);
        Assert.Equal(1, statementsReachedAfterTheCall);
    }

    /// <summary>
    /// A whitespace-only class name is NOT treated as empty: it falls through the guard and reaches
    /// name resolution, where it fails like any other unresolvable name.
    /// </summary>
    /// <param name="whitespaceClassName">A class name consisting only of whitespace.</param>
    /// <remarks>
    /// <para>
    /// Derived from the guard's exact form at <c>throwexception.srf:L23</c>, which is
    /// <c>if cls = "" then return</c>. PowerScript compares against the empty string and nothing
    /// else, so a space is not empty by that test and execution proceeds to
    /// <c>ex = Create Using cls</c> at <c>:L26</c>. The port must therefore NOT trim, and must not
    /// widen the guard to a whitespace test - either change would extend a silent-return defect to
    /// inputs the legacy raises on, which hides a caller bug that the legacy surfaces.
    /// </para>
    /// <para>
    /// CHARACTERIZES THE PORT for the exception TYPE only: that resolution FAILS for these inputs
    /// follows from the legacy, while <see cref="InvalidOperationException"/> is the
    /// implementation's documented choice 4. The distinction that matters, and the one this test
    /// pins, is that whitespace THROWS while the empty string does not.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(WhitespaceOnlyClassNames))]
    public void TwoArgumentForm_WithWhitespaceOnlyClassName_IsNotTreatedAsEmpty_ReachesResolution(
        string whitespaceClassName)
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Exceptions.ThrowException(whitespaceClassName, "Invalid method"));

        // The name reached the resolver and was reported verbatim, quoted and untrimmed - proof
        // that neither the guard nor the resolver normalised it away.
        Assert.Contains(
            "\"" + whitespaceClassName + "\"",
            thrown.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Class names that consist only of whitespace.
    /// </summary>
    /// <value>
    /// A single space; a tab; two spaces; and a line feed - the last because the legacy escape
    /// <c>~n</c> is what a hand-built class-name string is most likely to pick up by accident.
    /// </value>
    /// <remarks>
    /// Kept as its own member rather than folded into
    /// <see cref="UnresolvableClassNames"/> so that the whitespace-is-not-empty finding is
    /// documented next to the defect it is contrasted with, rather than buried among unrelated
    /// unresolvable names.
    /// </remarks>
    public static TheoryData<string> WhitespaceOnlyClassNames =>
    [
        " ",
        "\t",
        "  ",
        "\n",
    ];

    /// <summary>
    /// The two-argument overload is deliberately NOT marked as never returning, while the other
    /// two entry points are.
    /// </summary>
    /// <remarks>
    /// PRESERVED LEGACY DEFECT, expressed in the type system [throwexception.srf:L23]. The
    /// asymmetry is the honest way to keep the defect: attributing the two-argument overload as
    /// never returning would tell the compiler and the analyzers something untrue, and callers
    /// would then write code after the call that looks unreachable but in fact executes - which
    /// converts a documented defect into a silent one. This test is what keeps a well-meaning
    /// contributor from "completing" the attribute set.
    /// </remarks>
    [Fact]
    public void EntryPoints_DoesNotReturnAttribute_MarksOnlyTheTwoFormsThatAlwaysThrow()
    {
        // Always throws [throwexception.srf:L15].
        Assert.NotNull(
            ResolveEntryPoint(nameof(Exceptions.ThrowException), OneStringParameter)
                .GetCustomAttribute<DoesNotReturnAttribute>());

        // Always throws [pfwthrowexception.srf:L9 delegating with a non-empty constant name].
        Assert.NotNull(
            ResolveEntryPoint(nameof(Exceptions.PfwThrowException), OneStringParameter)
                .GetCustomAttribute<DoesNotReturnAttribute>());

        // CAN RETURN, because of the guard at throwexception.srf:L23. Absence here is the point.
        Assert.Null(
            ResolveEntryPoint(nameof(Exceptions.ThrowException), TwoStringParameters)
                .GetCustomAttribute<DoesNotReturnAttribute>());
    }


    // ==========================================================================================
    //  REGION 4 - NAME RESOLUTION: THE `Create Using cls` SUBSTITUTION
    //  ws_objects/pfw.shared.pbl.src/throwexception.srf:L26  ex = Create Using cls
    //
    //  PowerBuilder instantiates a class from a NAME STRING against one flat global namespace in
    //  which symbol resolution follows the ordering of the library list in the target file, so
    //  every global object in every loaded library is creatable by bare name. C# has no
    //  equivalent, because .NET requires a resolution SCOPE. ThrowException.cs substitutes a
    //  two-stage resolver - an explicit registry, then a best-effort scan of the ALREADY-LOADED
    //  assemblies - and this region pins the observable consequences of that substitution.
    //
    //  Measured consumer pressure, which is why the registry needs only one seeded entry:
    //  "pfwexception" is the ONLY class-name string passed anywhere in the legacy tree. The live
    //  site is pfwThrowException("Invalid method") at
    //  ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:860, whose enclosing handler reads
    //  ex.text off the raised object; the only other occurrences are a commented-out call at
    //  ws_objects/pfw.tests.pbl.src/w_test_webview.srw:288 and the delegation at
    //  pfwthrowexception.srf:L9 itself.
    // ==========================================================================================

    /// <summary>
    /// The legacy class-name string resolves to <see cref="PfwException"/> and the raised object
    /// carries the decorated message.
    /// </summary>
    /// <param name="text">The message text, from <see cref="MessageTexts"/>.</param>
    /// <remarks>
    /// Derived from <c>throwexception.srf:L26-L28</c> reached with the literal from
    /// <c>pfwthrowexception.srf:L9</c>. The class name is written here as the lowercase literal the
    /// legacy declares, NOT as <see cref="PfwException.TypeName"/>, so that this test would still
    /// fail if the published constant were changed;
    /// <see cref="Resolution_UsesPfwExceptionTypeName_NotALiteral_TwoFileContract"/> asserts the
    /// two are the same string.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MessageTexts))]
    public void TwoArgumentForm_WithLegacyClassName_ThrowsDecoratedPfwException(string text)
    {
        PfwException thrown = Assert.Throws<PfwException>(
            () => Exceptions.ThrowException(LegacyClassName, text));

        Assert.Equal(ExpectedMessagePrefix + ExpectedMessageSeparator + text, thrown.Message);

        // The resolved object is a real PfwException with its legacy defaults intact, not a bare
        // Exception that merely happens to carry a prefixed string.
        Assert.Equal(LegacyClassName, thrown.ClassName);
    }

    /// <summary>
    /// The name string the resolver is keyed on is <see cref="PfwException.TypeName"/> itself, and
    /// the framework form and the two-argument form are two doors onto one behaviour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the two-file contract. <c>pfwthrowexception.srf:L9</c> passes the literal
    /// <c>"pfwexception"</c> and <c>throwexception.srf:L26</c> resolves it, so the string binds
    /// PfwException.cs to ThrowException.cs. PfwException.cs publishes it once as
    /// <see cref="PfwException.TypeName"/> and ThrowException.cs seeds its registry from that
    /// constant rather than retyping it; this test asserts the constant still equals the legacy
    /// spelling and that both entry points behave identically through it.
    /// </para>
    /// <para>
    /// Named by ThrowException.cs's documented-choice audit as what pins choice 7.
    /// </para>
    /// </remarks>
    [Fact]
    public void Resolution_UsesPfwExceptionTypeName_NotALiteral_TwoFileContract()
    {
        // The published constant is the legacy spelling, lowercase, exactly as
        // pfwexception.sru:L8-L9 declares it and pfwthrowexception.srf:L9 passes it.
        Assert.Equal(LegacyClassName, PfwException.TypeName);

        const string text = "Invalid method";

        PfwException viaConstant = Assert.Throws<PfwException>(
            () => Exceptions.ThrowException(PfwException.TypeName, text));
        PfwException viaLegacyLiteral = Assert.Throws<PfwException>(
            () => Exceptions.ThrowException(LegacyClassName, text));
        PfwException viaFrameworkForm = Assert.Throws<PfwException>(
            () => Exceptions.PfwThrowException(text));

        // One behaviour, three doors. If the framework form ever stopped routing through the
        // registry entry - by constructing PfwException directly, say, and thereby drifting from
        // pfwthrowexception.srf:L9's literal delegation - these messages would diverge.
        Assert.Equal(viaConstant.Message, viaLegacyLiteral.Message);
        Assert.Equal(viaConstant.Message, viaFrameworkForm.Message);
        Assert.Equal(ExpectedMessagePrefix + ExpectedMessageSeparator + text, viaConstant.Message);

        // Decorated exactly once by each door. The registry stores a FACTORY so the resolved type
        // applies its own decoration through its own constructor; a resolver that decorated as
        // well would double the prefix here.
        Assert.Equal(1, CountOccurrences(viaFrameworkForm.Message, ExpectedMessagePrefix));
    }

    /// <summary>
    /// Class-name matching is case insensitive.
    /// </summary>
    /// <param name="classNameCasing">The legacy type name in some casing.</param>
    /// <remarks>
    /// <para>
    /// CHARACTERIZES THE PORT, on evidence rather than on preference. PowerBuilder identifiers are
    /// case insensitive, and this repository demonstrates it in situ:
    /// <c>pfwthrowexception.srf:L9</c> calls <c>ThrowException</c> with capitals while the object
    /// it resolves to is declared all lowercase at <c>throwexception.srf:L2</c>, and
    /// <c>pfwexception.sru:L8-L9</c> spell the type name lowercase while the demo call sites
    /// capitalise it. Case-insensitive matching is therefore the faithful reading, and it is
    /// ThrowException.cs's documented choice 3.
    /// </para>
    /// <para>
    /// The mixed-case rows are not padding: they distinguish genuine case-insensitive comparison
    /// from an implementation that merely lower-cases or upper-cases the incoming name before a
    /// case-sensitive lookup, which would fail for a registry key stored in the other case.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(LegacyClassNameCasings))]
    public void TwoArgumentForm_WithTypeNameInAnyCasing_ThrowsPfwException_InferredCaseInsensitivity(
        string classNameCasing)
    {
        const string text = "Invalid method";

        PfwException thrown = Assert.Throws<PfwException>(
            () => Exceptions.ThrowException(classNameCasing, text));

        Assert.Equal(ExpectedMessagePrefix + ExpectedMessageSeparator + text, thrown.Message);
    }

    /// <summary>
    /// The legacy type name in every casing that matters.
    /// </summary>
    /// <value>
    /// The legacy lowercase spelling; all upper case; the .NET PascalCase spelling of the managed
    /// type; and an alternating casing that no simple normalisation would produce by accident.
    /// </value>
    public static TheoryData<string> LegacyClassNameCasings =>
    [
        "pfwexception",
        "PFWEXCEPTION",
        "PfwException",
        "pFwExCePtIoN",
    ];

    /// <summary>
    /// Case-insensitive matching is ORDINAL, so resolution does not change with the ambient
    /// culture - demonstrated under tr-TR, where culture-sensitive matching gives a different
    /// answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZES THE PORT: ThrowException.cs's documented choice 3 states the comparison is
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> and names this test as what pins it. The
    /// parity model's determinism requirement forbids a resolution outcome that depends on the
    /// ambient culture, and Turkish is the standard demonstration because its casing rules map
    /// <c>i</c> to a dotted capital and <c>I</c> to a dotless lower case - and
    /// <c>pfwexception</c> contains an <c>i</c>.
    /// </para>
    /// <para>
    /// The premise is asserted rather than assumed. Without the first assertion this test would
    /// still pass on an implementation that used culture-sensitive comparison in a culture where
    /// the two agree, and the reader would have no way to tell that the test had stopped proving
    /// anything. The <c>InvariantGlobalization</c> build property is deliberately not enabled
    /// anywhere in this repository - the port carries a localization surface plus culture-sensitive
    /// date and number parity - so ICU is live and the Turkish casing rules are genuinely in effect.
    /// </para>
    /// <para>
    /// The culture is restored in a <c>finally</c>. <see cref="CultureInfo.CurrentCulture"/> is
    /// per-thread and this test is synchronous, so the change is confined to this thread; restoring
    /// it keeps a pooled thread from carrying tr-TR into an unrelated test afterwards.
    /// </para>
    /// <para>
    /// The test has THREE behavioural legs because the two resolver stages are exposed to the
    /// ambient culture differently, and one of them is not exposed to it at all. Stage 2 compares at
    /// call time and is therefore genuinely discriminated by the tr-TR legs; stage 1's comparer is
    /// fixed once at static initialisation and is not, so it needs the culture-independent leg after
    /// the <c>finally</c>. Each leg carries its own explanation at the point it appears.
    /// </para>
    /// </remarks>
    [Fact]
    public void CaseInsensitiveResolution_IsOrdinal_UnderTurkishCulture()
    {
        const string upperCasedName = "PFWEXCEPTION";
        CultureInfo previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");

            // THE PREMISE. Under tr-TR a culture-sensitive ignore-case comparison says these two
            // names are DIFFERENT, because of the dotted and dotless i. If this ever became true,
            // the assertion below would no longer distinguish ordinal from culture-sensitive
            // matching and this test would be silently vacuous.
            //
            // The two comparisons below go through StringComparer rather than string.Equals for a
            // concrete reason: xunit's analyzer rewrites Assert.True/Assert.False over a
            // string.Equals call into Assert.Equal - reasonable advice in general, and wrong here,
            // because Assert.Equal's own ignoreCase option would pick the comparison semantics for
            // us and this test's entire subject is WHICH comparison semantics apply. Naming the
            // comparer explicitly keeps the distinction under test rather than delegating it to the
            // assertion library. StringComparer.CurrentCultureIgnoreCase binds to the ambient
            // culture at the point of access, which is tr-TR inside this block.
            Assert.False(
                StringComparer.CurrentCultureIgnoreCase.Equals(upperCasedName, LegacyClassName),
                "The Turkish dotted/dotless i is what makes this test meaningful; if the two names "
                + "compare equal culture-sensitively under tr-TR the premise has gone.");

            // ... while an ordinal ignore-case comparison says they are the same.
            Assert.True(
                StringComparer.OrdinalIgnoreCase.Equals(upperCasedName, LegacyClassName),
                "Ordinal ignore-case comparison must treat the two casings as the same name; this "
                + "is the comparison the resolver is specified to use.");

            // THE BEHAVIOUR, STAGE 1. The registry lookup succeeds under tr-TR. This guards against
            // a lookup that consults the ambient culture at CALL time.
            PfwException thrown = Assert.Throws<PfwException>(
                () => Exceptions.ThrowException(upperCasedName, "Invalid method"));

            Assert.Equal(
                ExpectedMessagePrefix + ExpectedMessageSeparator + "Invalid method",
                thrown.Message);

            // THE BEHAVIOUR, STAGE 2. The loaded-assembly scan compares each candidate type's
            // simple name at call time, so its comparison mode IS exposed to the ambient culture -
            // which makes this the leg that genuinely discriminates for that stage. The probe type's
            // name contains an `i`, so a culture-sensitive comparison under tr-TR would not match
            // the upper-cased form and resolution would fail.
            //
            // ToUpperInvariant, not ToUpper: under tr-TR the culture-aware upper-casing of `i`
            // produces the DOTTED capital U+0130, which ordinal matching would correctly reject -
            // so using it here would break the test for a reason that has nothing to do with the
            // resolver.
            Assert.Throws<ProbeResolvableException>(
                () => Exceptions.ThrowException(
                    nameof(ProbeResolvableException).ToUpperInvariant(),
                    "scanned under tr-TR"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }

        // THE LEG THAT CLOSES THE REMAINING HOLE, AND WHY IT IS NEEDED - a finding worth stating,
        // because it is the reason the tr-TR legs above are not sufficient on their own.
        //
        // The registry's comparer is supplied to the dictionary ONCE, when the Exceptions class is
        // initialised, and a culture-based StringComparer binds to whichever culture is current AT
        // THAT MOMENT. In a test host that is the invariant culture, long before this method can set
        // tr-TR - so a registry accidentally built with StringComparer.CurrentCultureIgnoreCase
        // would behave like invariant ignore-case for the rest of the process, agree with ordinal on
        // every ASCII name, and slip past the assertions above unnoticed. Verified by observation:
        // that exact mutation passes every other test in this suite.
        //
        // A collation-IGNORABLE character closes it without depending on any culture. Every
        // culture-based comparison, invariant included, treats U+00AD SOFT HYPHEN as having no
        // weight and therefore reports this name equal to the registered one, while ordinal
        // comparison sees a different string. So an ordinal registry must NOT resolve this name, and
        // any culture-based one would. U+200B and U+200D behave identically and would serve equally
        // well.
        InvalidOperationException unresolved = Assert.Throws<InvalidOperationException>(
            () => Exceptions.ThrowException(
                "pfw\u00ADexception",
                "an ignorable character makes this a different name ordinally"));

        Assert.Contains(
            nameof(Exceptions) + "." + nameof(Exceptions.TryRegisterExceptionType),
            unresolved.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A non-empty class name that names no reachable exception type raises
    /// <see cref="InvalidOperationException"/>, and the diagnostic says everything a reader needs.
    /// </summary>
    /// <param name="unresolvableClassName">A class name nothing loaded can satisfy.</param>
    /// <remarks>
    /// <para>
    /// That resolution FAILURE surfaces as a raise follows from the legacy: <c>Create Using cls</c>
    /// at <c>throwexception.srf:L26</c> fails and the surrounding
    /// <c>catch(throwable e) throw e</c> at <c>:L29-L30</c> rethrows it, so something is raised.
    /// </para>
    /// <para>
    /// CHARACTERIZES THE PORT for the exception TYPE. The legacy's exact fault identity is not
    /// observable from the source, and ThrowException.cs records
    /// <see cref="InvalidOperationException"/> as documented choice 4 - naming this test as what
    /// pins it - having rejected <see cref="TypeLoadException"/> and
    /// <see cref="TypeAccessException"/> as runtime-reserved, and
    /// <see cref="ArgumentException"/> as misattributing the fault to the caller's argument in a
    /// way that would blur the line against the empty-name path that must NOT throw. THAT
    /// DISTINCTION IS THE WHOLE POINT OF THIS TEST SITTING NEXT TO REGION 3: a non-empty
    /// unresolvable name throws, and the empty string does not.
    /// </para>
    /// <para>
    /// The message content is asserted because the diagnostic is load-bearing: it is what tells
    /// somebody investigating a MISSING exception that the empty-name path is a different path.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnresolvableClassNames))]
    public void TwoArgumentForm_WithUnresolvableClassName_ThrowsInvalidOperationException_InferredChoice(
        string unresolvableClassName)
    {
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Exceptions.ThrowException(unresolvableClassName, "Invalid method"));

        // The offending name, quoted and verbatim.
        Assert.Contains(
            "\"" + unresolvableClassName + "\"",
            thrown.Message,
            StringComparison.Ordinal);

        // The legacy locator of the construct being substituted for, so a reader can go straight
        // to the oracle.
        Assert.Contains(
            "ws_objects/pfw.shared.pbl.src/throwexception.srf:L26",
            thrown.Message,
            StringComparison.Ordinal);

        // The remedy, named as a real member rather than described in prose.
        Assert.Contains(
            nameof(Exceptions) + "." + nameof(Exceptions.TryRegisterExceptionType),
            thrown.Message,
            StringComparison.Ordinal);

        // And the pointer to the OTHER path, which is what keeps the two from being conflated
        // while somebody is mid-investigation.
        Assert.Contains("[throwexception.srf:L23]", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Class names that no stage of the resolver can satisfy.
    /// </summary>
    /// <value>
    /// A name nothing declares; the managed type's namespace-qualified name, because matching is on
    /// the SIMPLE name and a full name therefore does not match; a public BCL type that is not an
    /// exception; a nested probe type that is abstract; one with no single-string constructor; one
    /// that is not publicly visible; and one that is not an exception at all.
    /// </value>
    /// <remarks>
    /// The last four rows are the four conditions of the resolver's creatability filter, each
    /// exercised by a probe type declared at the bottom of this file. They are theory rows carrying
    /// only strings - never a <see cref="Type"/> - because xunit's analyzer rejects theory data
    /// whose type arguments may not be serializable, and warnings are errors here.
    /// </remarks>
    public static TheoryData<string> UnresolvableClassNames =>
    [
        "noSuchClassNameExistsAnywhere",
        "PowerFramework.Shared.Kernel.PfwException",
        nameof(TimeSpan),
        nameof(ProbeAbstractException),
        nameof(ProbeExceptionWithoutTextConstructor),
        nameof(ProbeNonPublicException),
        nameof(ProbeTypeThatIsNotAnException),
    ];

    /// <summary>
    /// A null class name is NOT treated as empty: it falls through the guard and raises the
    /// documented resolution fault, with the null rendered explicitly in the diagnostic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from PowerScript's three-valued comparison semantics. At
    /// <c>throwexception.srf:L23</c> the test is <c>if cls = "" then return</c>; comparing a NULL
    /// string against the empty string yields NULL, an <c>if NULL then</c> branch is not taken, so
    /// the legacy also falls through on a null class name and fails inside
    /// <c>Create Using cls</c>. Widening the port's guard to a null-or-empty test would extend a
    /// silent-return defect to a case the legacy raises on, which is a behaviour change in the more
    /// dangerous of the two directions. ThrowException.cs records this as choice 2 and names this
    /// test as what pins it.
    /// </para>
    /// <para>
    /// The parameter is declared non-nullable, which states the intended contract, so getting a
    /// null to it requires the same thing a nullable-oblivious caller does - see
    /// <see cref="NullReferenceTypedAsString"/> for how that is done without the null-forgiving
    /// operator, which C-H forbids.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoArgumentForm_WithNullClassName_Throws_InferredFromPowerScriptNullSemantics()
    {
        string nullClassName = NullReferenceTypedAsString();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => Exceptions.ThrowException(nullClassName, "Invalid method"));

        // Rendered as <null> rather than as an empty pair of quotes, precisely so it cannot be
        // mistaken for the empty-string case that returns silently.
        Assert.Contains("<null>", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("\"\"", thrown.Message, StringComparison.Ordinal);
    }


    // ==========================================================================================
    //  REGION 5 - THE TWO RESOLVER STAGES AND THEIR DOCUMENTED SCOPE
    //
    //  STAGE 1 is the explicit registry, seeded with the one name the legacy uses. STAGE 2 is a
    //  best-effort scan of the ALREADY-LOADED assemblies, which is the closest honest analogue of
    //  PowerBuilder's flat global namespace. The honest limitation is named in ThrowException.cs
    //  rather than papered over: a type in an assembly that has not been loaded, or that is neither
    //  registered nor publicly visible, does NOT resolve, and no assembly is ever loaded to close
    //  that gap. This region pins both stages and the four creatability conditions.
    //
    //  A NOTE ON TEST INDEPENDENCE, WHICH IS A REAL CONCERN HERE AND NOT A THEORETICAL ONE.
    //  --------------------------------------------------------------------------------------
    //  The registry is process-global static state with NO removal operation - deliberately, since
    //  first registration winning is what makes the seeded framework entry undisplaceable. So a
    //  registration made by one test persists for the rest of the process. Two rules keep these
    //  tests independent of one another and of execution order: every name registered below is
    //  unique to the test that registers it, and no test asserts that a name it did not itself
    //  register is unresolvable. That is why the unresolvable rows in region 4 are either names
    //  nothing could ever register or probe types that fail the creatability filter, and never a
    //  name some other test might have claimed.
    // ==========================================================================================

    /// <summary>
    /// Stage 2 resolves a public exception type that was never registered, by its simple name, from
    /// an already-loaded assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZES THE PORT, and specifically its SCOPE. The legacy reaches every global object in
    /// every loaded library [throwexception.srf:L26]; the managed substitute reaches every public,
    /// non-abstract, non-generic <see cref="Exception"/> subclass with a public single-string
    /// constructor in an assembly that is already loaded. ThrowException.cs records that scope as
    /// choice 6 and names this test as what pins it.
    /// </para>
    /// <para>
    /// The probe type lives in this test assembly, which is loaded by definition while the test is
    /// running, so the scan can genuinely reach it - and that is the point: it demonstrates the
    /// stage is real rather than dead code, without loading anything from disk.
    /// </para>
    /// <para>
    /// The message arrives UNDECORATED, because decoration belongs to the resolved type. Only
    /// <see cref="PfwException"/> decorates [pfwexception.sru:L23]; the resolver must not.
    /// </para>
    /// </remarks>
    [Fact]
    public void LoadedAssemblyFallback_ResolvesUnregisteredExceptionTypeByName_InferredScope()
    {
        const string text = "resolved through the loaded-assembly scan";

        ProbeResolvableException thrown = Assert.Throws<ProbeResolvableException>(
            () => Exceptions.ThrowException(nameof(ProbeResolvableException), text));

        Assert.Equal(text, thrown.Message);
        Assert.DoesNotContain(ExpectedMessagePrefix, thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Stage 2 resolves the same type on every call, so the outcome does not depend on assembly
    /// enumeration order.
    /// </summary>
    /// <remarks>
    /// CHARACTERIZES THE PORT. Assembly enumeration order reflects load order, which is not stable
    /// across runs, so ThrowException.cs picks the ordinal minimum of assembly full name then type
    /// full name rather than the first match it sees, and caches nothing in either direction.
    /// Repeatability within a process is the observable consequence of that choice; the
    /// cross-process half cannot be observed from a single test run and is recorded as a documented
    /// property rather than asserted.
    /// </remarks>
    [Fact]
    public void LoadedAssemblyFallback_ResolvesTheSameTypeOnEveryCall()
    {
        Exception first = Assert.Throws<ProbeResolvableException>(
            () => Exceptions.ThrowException(nameof(ProbeResolvableException), "first"));
        Exception second = Assert.Throws<ProbeResolvableException>(
            () => Exceptions.ThrowException(nameof(ProbeResolvableException), "second"));

        Assert.Equal(first.GetType(), second.GetType());
        Assert.Equal("first", first.Message);
        Assert.Equal("second", second.Message);
    }

    /// <summary>
    /// A registration for a previously unknown name is accepted, and that name then resolves
    /// through stage 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZES THE PORT. <see cref="Exceptions.TryRegisterExceptionType"/> has no legacy
    /// counterpart at all - it is the extension point that lets a dependent assembly add a name
    /// without Kernel referencing it, whose motivating case is
    /// <c>PowerFramework.Shared.Diagnostics.AssertionFailure</c>, the port of
    /// <c>assertionfailed.sru</c>, since Diagnostics depends on Kernel and the reverse edge would be
    /// circular.
    /// </para>
    /// <para>
    /// The registered name is unique to this test, for the reason given in the region header.
    /// </para>
    /// </remarks>
    [Fact]
    public void TryRegisterExceptionType_AcceptsANewName_WhichThenResolvesThroughTheRegistry()
    {
        const string freshName = "probeNameRegisteredByTheAcceptsANewNameTest";

        Assert.True(Exceptions.TryRegisterExceptionType(
            freshName,
            static text => new ProbeResolvableException(text)));

        ProbeResolvableException thrown = Assert.Throws<ProbeResolvableException>(
            () => Exceptions.ThrowException(freshName, "registered"));
        Assert.Equal("registered", thrown.Message);

        // Registered names are matched the same way seeded ones are: ordinally, ignoring case.
        Assert.Throws<ProbeResolvableException>(
            () => Exceptions.ThrowException(freshName.ToUpperInvariant(), "registered"));
    }

    /// <summary>
    /// Registration reports failure - and never raises - for a null name, an empty name, a null
    /// factory, or a name that is already registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CHARACTERIZES THE PORT. ThrowException.cs states the reasoning: this member is new .NET-only
    /// surface with no legacy raising behaviour to reproduce, and a registry that raised
    /// <see cref="ArgumentException"/> would put one into the very file whose subject is a path that
    /// must return silently, where the two could be confused.
    /// </para>
    /// <para>
    /// FIRST REGISTRATION WINS is a safety property rather than a convenience, which is why the
    /// duplicate case is asserted against the SEEDED framework entry: if a later registration could
    /// displace it, a caller could quietly redirect the framework form at some other type and break
    /// the two-file contract with PfwException.cs.
    /// </para>
    /// </remarks>
    [Fact]
    public void TryRegisterExceptionType_RejectsNullEmptyDuplicate_AndNeverRaises()
    {
        Assert.False(Exceptions.TryRegisterExceptionType(
            NullReferenceTypedAsString(),
            static text => new ProbeResolvableException(text)));

        Assert.False(Exceptions.TryRegisterExceptionType(
            string.Empty,
            static text => new ProbeResolvableException(text)));

        Assert.False(Exceptions.TryRegisterExceptionType(
            "probeNameRejectedForItsNullFactory",
            NullReferenceTypedAsFactory()));

        // The seeded entry cannot be displaced, in either casing.
        Assert.False(Exceptions.TryRegisterExceptionType(
            LegacyClassName,
            static text => new ProbeResolvableException(text)));
        Assert.False(Exceptions.TryRegisterExceptionType(
            LegacyClassName.ToUpperInvariant(),
            static text => new ProbeResolvableException(text)));
    }

    /// <summary>
    /// After a rejected attempt to displace the seeded entry, the framework form still raises
    /// <see cref="PfwException"/>.
    /// </summary>
    /// <remarks>
    /// The behavioural consequence of first-registration-wins, and the property that keeps the
    /// unreachable state at ThrowException.cs:L466 unreachable: the framework form can only return
    /// if the empty-name guard fires, and it can only reach a non-PfwException type if the seeded
    /// entry were displaced. Asserting the outcome rather than only the boolean return value is
    /// what makes this a behavioural test instead of a restatement of the previous one.
    /// </remarks>
    [Fact]
    public void TryRegisterExceptionType_CannotRedirectTheFrameworkForm()
    {
        Exceptions.TryRegisterExceptionType(
            LegacyClassName,
            static text => new ProbeResolvableException(text));

        PfwException thrown = Assert.Throws<PfwException>(
            () => Exceptions.PfwThrowException("Invalid method"));

        Assert.Equal(
            ExpectedMessagePrefix + ExpectedMessageSeparator + "Invalid method",
            thrown.Message);
    }

    // ==========================================================================================
    //  REGION 6 - THE PfwException SURFACE
    //  ws_objects/pfw.shared.pbl.src/pfwexception.sru
    //
    //      :L3, :L7   global type pfwexception from runtimeerror
    //      :L8        string  objectname  = "pfwexception"
    //      :L9        string  class       = "pfwexception"
    //      :L10       string  routinename = "create"
    //      :L11       integer line        = -1
    //      :L13       global pfwexception pfwexception        <-- deliberately not reproduced
    //      :L15-L17   type variables ... end variables       <-- EMPTY, adds no state
    //      :L20,:L23  public subroutine setmessage (string newmessage)
    //                     super::SetMessage("PowerFramework Runtime Error~n" + newMessage)
    // ==========================================================================================

    /// <summary>
    /// The four type-level legacy defaults are carried, with their legacy values.
    /// </summary>
    /// <remarks>
    /// Derived from <c>pfwexception.sru:L8-L11</c>. All four are present in the port - none was
    /// dropped - so all four are asserted rather than noted as omitted. The legacy field spelled
    /// <c>class</c> is a C# keyword, so its identifier is rendered <c>ClassName</c> while its VALUE
    /// is unchanged; the duplication between it and <c>ObjectName</c> is reproduced rather than
    /// collapsed, because both exist independently on PowerBuilder's <c>throwable</c> surface.
    /// The <c>-1</c> line is a sentinel meaning "unknown line" and is preserved verbatim rather than
    /// resolved to a real line number, which would replace a legacy constant with a computed value.
    /// </remarks>
    [Fact]
    public void PfwExceptionDefaults_CarryTheFourLegacyTypeLevelValues()
    {
        PfwException instance = new("Invalid method");

        Assert.Equal(LegacyClassName, instance.ObjectName);
        Assert.Equal(LegacyClassName, instance.ClassName);
        Assert.Equal("create", instance.RoutineName);
        Assert.Equal(-1, instance.Line);
    }

    /// <summary>
    /// The four defaults are read-write, because the legacy declarations are assignable instance
    /// fields carrying type-level default values.
    /// </summary>
    /// <remarks>
    /// Derived from <c>pfwexception.sru:L8-L11</c>, where the values are DEFAULTS on instance fields
    /// rather than constants: PowerBuilder lets a caller reassign them, so both the value and its
    /// assignability are part of the shape being preserved. A port that exposed them as get-only
    /// would narrow the type relative to the object it replaces.
    /// </remarks>
    [Fact]
    public void PfwExceptionDefaults_AreAssignable_LikeTheLegacyInstanceFields()
    {
        PfwException instance = new("Invalid method")
        {
            ObjectName = "reassignedObjectName",
            ClassName = "reassignedClassName",
            RoutineName = "reassignedRoutineName",
            Line = 42,
        };

        Assert.Equal("reassignedObjectName", instance.ObjectName);
        Assert.Equal("reassignedClassName", instance.ClassName);
        Assert.Equal("reassignedRoutineName", instance.RoutineName);
        Assert.Equal(42, instance.Line);
    }

    /// <summary>
    /// <see cref="PfwException"/> derives directly from <see cref="Exception"/> and is catchable as
    /// a plain exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from <c>pfwexception.sru:L7</c>, <c>global type pfwexception from runtimeerror</c>.
    /// CHARACTERIZES THE PORT for the choice of BCL ancestor: PfwException.cs records
    /// <see cref="Exception"/> as decision 1, having rejected <see cref="SystemException"/> as
    /// reserved by convention for faults the runtime itself raises - this type is only ever raised
    /// deliberately by framework code - and <see cref="ApplicationException"/> as deprecated by the
    /// .NET design guidelines, adding no member and no behaviour that anything branches on.
    /// </para>
    /// <para>
    /// The catchability half is not decoration. The legacy's passthrough at
    /// <c>throwexception.srf:L29-L30</c> is <c>catch(throwable e) throw e</c>, so the type must sit
    /// inside the throwable hierarchy for that idiom to have applied to it at all; a plain
    /// <c>catch (Exception)</c> is the managed equivalent, and it must catch this type.
    /// </para>
    /// </remarks>
    [Fact]
    public void PfwException_DerivesDirectlyFromException_AndIsCatchableAsAPlainException()
    {
        Assert.Equal(typeof(Exception), typeof(PfwException).BaseType);
        Assert.False(typeof(SystemException).IsAssignableFrom(typeof(PfwException)));
        Assert.False(typeof(ApplicationException).IsAssignableFrom(typeof(PfwException)));

        bool caughtAsPlainException = false;
        try
        {
            Exceptions.PfwThrowException("Invalid method");
        }
        catch (Exception caught)
        {
            caughtAsPlainException = true;
            Assert.IsType<PfwException>(caught);
        }

        Assert.True(caughtAsPlainException);
    }


    /// <summary>
    /// PRESERVED LEGACY BEHAVIOUR. The decoration lives in the SETTER, so feeding a decorated
    /// message back in decorates it a SECOND time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from <c>pfwexception.sru:L23</c>, whose whole body is
    /// <c>super::SetMessage("PowerFramework Runtime Error~n" + newMessage)</c>. There is no
    /// idempotence check, no test for an already-present prefix and no stripping of one, so the
    /// prefix accumulates. The as-built port keeps the message settable, which is why this fact is
    /// reachable and is asserted here rather than recorded as an unreachable narrowing.
    /// </para>
    /// <para>
    /// C-B: this is not a bug to guard against. Adding an idempotence check would be exactly the
    /// silent correction the refactor forbids, and it would change an observable message that
    /// characterization recordings compare byte for byte.
    /// </para>
    /// </remarks>
    [Fact]
    public void SetMessage_DecoratesUnconditionally_SoFeedingItsOwnMessageBackInPrefixesTwice()
    {
        PfwException instance = new("Invalid method");
        Assert.Equal(
            ExpectedMessagePrefix + ExpectedMessageSeparator + "Invalid method",
            instance.Message);

        instance.SetMessage(instance.Message);

        Assert.Equal(
            ExpectedMessagePrefix + ExpectedMessageSeparator
            + ExpectedMessagePrefix + ExpectedMessageSeparator
            + "Invalid method",
            instance.Message);

        // Counted as well as compared, because the count is what a reader checks first when this
        // test fails and it names the failure mode directly.
        Assert.Equal(2, CountOccurrences(instance.Message, ExpectedMessagePrefix));
    }

    /// <summary>
    /// The setter REPLACES rather than appends, so two identical successive calls leave exactly one
    /// prefix.
    /// </summary>
    /// <remarks>
    /// Derived from <c>pfwexception.sru:L23</c>: the legacy body passes the decorated ARGUMENT to
    /// the ancestor's setter, and PowerBuilder's <c>throwable.SetMessage</c> assigns the message
    /// rather than extending it. The pairing with the previous test is deliberate and is the whole
    /// subtlety of this behaviour - the prefix is applied on EVERY call, yet the double-prefix
    /// outcome requires the current message to be fed back in. An implementation that appended
    /// would pass the previous test and fail this one.
    /// </remarks>
    [Fact]
    public void SetMessage_ReplacesRatherThanAppends_SoRepeatedCallsLeaveOnePrefix()
    {
        PfwException instance = new("first");

        instance.SetMessage("second");
        instance.SetMessage("second");

        Assert.Equal(
            ExpectedMessagePrefix + ExpectedMessageSeparator + "second",
            instance.Message);
        Assert.Equal(1, CountOccurrences(instance.Message, ExpectedMessagePrefix));
        Assert.DoesNotContain("first", instance.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty message leaves the prefix and its line feed as the entire message, and is never
    /// rejected.
    /// </summary>
    /// <remarks>
    /// Derived from PowerScript concatenation semantics at <c>pfwexception.sru:L23</c>, which treat
    /// an unset string as empty. Raising an argument fault here would be a new failure mode the
    /// legacy does not have, and inside an exception type it is doubly hostile: it would replace the
    /// fault a caller is trying to report with a different one.
    /// </remarks>
    [Fact]
    public void SetMessage_WithEmptyText_LeavesPrefixAndSeparatorOnly()
    {
        PfwException instance = new("Invalid method");

        instance.SetMessage(string.Empty);

        Assert.Equal(ExpectedMessagePrefix + ExpectedMessageSeparator, instance.Message);
        Assert.EndsWith(ExpectedMessageSeparator, instance.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message-carrying constructors decorate exactly as the setter does, and the two-argument
    /// form keeps the inner exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both constructors are .NET convention additions: the legacy object has only a <c>create</c>
    /// event whose body is <c>call super::create</c> plus a constructor trigger
    /// [pfwexception.sru:L26-L29], so there is no legacy constructor signature to translate. The
    /// single-argument form decorates so that <c>new PfwException(text).Message</c> equals what the
    /// legacy <c>pfwThrowException(text)</c> path produces, that path being create-then-SetMessage
    /// [throwexception.srf:L26-L27] collapsed into one step.
    /// </para>
    /// <para>
    /// CHARACTERIZES THE PORT for the inner-exception overload, which has no legacy counterpart at
    /// all: PowerScript's <c>catch(throwable e) ... throw e</c> idiom rethrows the original object
    /// rather than nesting it, so the legacy has no inner-exception concept.
    /// </para>
    /// </remarks>
    [Fact]
    public void Constructors_DecorateIdenticallyToTheSetter_AndPreserveAnInnerException()
    {
        const string text = "Invalid method";
        string expected = ExpectedMessagePrefix + ExpectedMessageSeparator + text;

        PfwException viaConstructor = new(text);
        Assert.Equal(expected, viaConstructor.Message);

        PfwException viaSetter = new();
        viaSetter.SetMessage(text);
        Assert.Equal(expected, viaSetter.Message);

        // One shared decoration path, so the constructor and the setter cannot drift.
        Assert.Equal(viaConstructor.Message, viaSetter.Message);

        InvalidOperationException inner = new("the underlying fault");
        PfwException withInner = new(text, inner);
        Assert.Equal(expected, withInner.Message);
        Assert.Same(inner, withInner.InnerException);
    }

    /// <summary>
    /// An instance created without a message is UNDECORATED and falls back to the base
    /// implementation's message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the state the legacy object is in between <c>Create Using cls</c> and its first
    /// <c>SetMessage</c> [throwexception.srf:L26-L27], and it is why the port's overridden message
    /// falls back rather than returning an empty string: an instance carrying no message of its own
    /// stays indistinguishable from any other BCL exception in the same state.
    /// </para>
    /// <para>
    /// The fallback text itself is asserted structurally rather than compared whole. The BCL's
    /// "Exception of type '...' was thrown." string is localizable, so a literal comparison would
    /// be a test that fails under a translated framework rather than under a real regression. What
    /// is stable across cultures is that the message names the type and carries no prefix.
    /// </para>
    /// </remarks>
    [Fact]
    public void ParameterlessConstructor_LeavesTheMessageUndecorated_FallingBackToTheBase()
    {
        PfwException instance = new();

        Assert.DoesNotContain(ExpectedMessagePrefix, instance.Message, StringComparison.Ordinal);

        // Type.FullName is annotated nullable because it is genuinely null for a few reflection
        // constructs - an open generic parameter, for instance. It cannot be null for this closed,
        // non-generic type, so the fallback below is unreachable in practice; it is written out
        // rather than asserted away with the null-forgiving operator, which C-H forbids.
        string typeFullName = typeof(PfwException).FullName ?? typeof(PfwException).Name;
        Assert.Contains(typeFullName, instance.Message, StringComparison.Ordinal);

        // And the fallback is a fallback, not a permanent state: the first set decorates.
        instance.SetMessage("Invalid method");
        Assert.Equal(
            ExpectedMessagePrefix + ExpectedMessageSeparator + "Invalid method",
            instance.Message);
    }

    // ==========================================================================================
    //  REGION 7 - THE OMITTED no-op catch/rethrow
    //  ws_objects/pfw.shared.pbl.src/throwexception.srf:L16-L17 and :L29-L30
    //
    //      catch(throwable e)
    //          throw e
    //
    //  Both wrappers catch anything the creation or the throw produces and rethrow it unchanged, so
    //  they add nothing observable. ThrowException.cs omits them rather than transcribing them,
    //  because the literal C# transcription `catch (Exception e) { throw e; }` RESETS the stack
    //  trace - an observable regression - whereas omitting the wrapper preserves observable
    //  behaviour exactly and keeps the original throw site intact.
    //
    //  WHAT A STACK-TRACE ASSERTION CAN AND CANNOT PORTABLY OBSERVE, STATED SO THE TEST BELOW IS
    //  NOT READ AS CLAIMING MORE THAN IT PROVES.
    //  --------------------------------------------------------------------------------------
    //  In the legacy shape the original throw and the rethrow sit in the SAME subroutine, so a
    //  transcribed wrapper would relocate the recorded throw position within one method rather than
    //  drop whole frames. Line positions are not portable to assert. What IS portable, and is
    //  asserted below, is (a) that a trace was captured at all, (b) that the throw site is the
    //  PUBLIC entry point and not the private creation helper - which is what
    //  `throw CreateByClassName(...)` buys and what moving the throw into that helper would break -
    //  and (c) that the caller chain above the throw is intact. Asserting the ABSENCE of the helper
    //  frame is additionally immune to inlining, since inlining can only remove frames.
    // ==========================================================================================

    /// <summary>
    /// Every entry point raises from its own public frame, with the caller chain intact and the
    /// private creation helper absent from the trace.
    /// </summary>
    /// <remarks>
    /// CHARACTERIZES THE PORT. Named by ThrowException.cs's documented-choice audit as what pins
    /// choice 5, the omitted no-op catch/rethrow. See the region header for exactly what this can
    /// and cannot observe. All three entry points are exercised in one fact rather than as a theory
    /// because the alternative would carry delegates in theory data, which xunit's analyzer rejects
    /// as possibly non-serializable - and warnings are errors here.
    /// </remarks>
    [Fact]
    public void EntryPoints_PreserveThrowSiteInStackTrace()
    {
        AssertThrowSiteIsIntact(static () => Exceptions.ThrowException("Invalid method"));
        AssertThrowSiteIsIntact(
            static () => Exceptions.ThrowException(LegacyClassName, "Invalid method"));
        AssertThrowSiteIsIntact(static () => Exceptions.PfwThrowException("Invalid method"));
    }

    /// <summary>
    /// Raises through <paramref name="raise"/> and asserts the captured stack trace's shape.
    /// </summary>
    /// <param name="raise">The entry-point invocation under test.</param>
    /// <remarks>
    /// <para>
    /// Marked as not inlinable so that its own frame is guaranteed to appear in the captured trace,
    /// which is what lets the caller-chain assertion be deterministic rather than dependent on the
    /// tiered compiler's inlining decisions.
    /// </para>
    /// <para>
    /// The exception is caught HERE with an explicit <c>try</c>/<c>catch</c> rather than through
    /// <c>Record.Exception</c>, and that is a correctness requirement rather than a style choice.
    /// A stack trace accumulates frames only while the exception is unwinding, up to and including
    /// the frame that catches it - so <c>Record.Exception</c> truncates the trace at its own frame
    /// and this method's frame never appears in it, which would make the caller-chain assertion
    /// below unsatisfiable. Verified by observation: with <c>Record.Exception</c> the trace ends
    /// inside xunit and names nothing from this class. <c>Record.Exception</c> is still the right
    /// tool where the subject is whether anything was raised at all, which is why region 3 uses it.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertThrowSiteIsIntact(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception captured)
        {
            string trace = captured.StackTrace ?? string.Empty;

            // (a) A trace was captured.
            Assert.NotEqual(0, trace.Length);

            // (b) The throw site is the public entry point, so the private creation helpers never
            //     appear. A `throw` moved down into either of them would surface here.
            Assert.DoesNotContain("CreateByClassName", trace, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateFromLoadedAssemblies", trace, StringComparison.Ordinal);

            // (c) The caller chain above the throw is intact, all the way up to the frame that
            //     caught it - which is this one.
            Assert.Contains(nameof(AssertThrowSiteIsIntact), trace, StringComparison.Ordinal);
            return;
        }

        // Reached only if the entry point returned. Two of the three always throw and the third is
        // called here with a non-empty class name, so this is a genuine failure rather than a
        // tolerated outcome.
        Assert.Fail(
            "The entry point under test returned instead of raising. Only an empty class name may "
            + "do that [throwexception.srf:L23], and no case exercised here passes one.");
    }


    // ==========================================================================================
    //  HELPERS
    // ==========================================================================================

    /// <summary>
    /// Counts non-overlapping occurrences of <paramref name="value"/> in <paramref name="text"/>,
    /// ordinally.
    /// </summary>
    /// <param name="text">The string to search.</param>
    /// <param name="value">The non-empty substring to count.</param>
    /// <returns>The number of non-overlapping occurrences.</returns>
    /// <remarks>
    /// Counting is what distinguishes "not decorated" from "decorated once more", which a substring
    /// test alone reports identically. Ordinal because every string in this suite is a fixed literal
    /// and a culture-sensitive search could match differently under a culture such as tr-TR - the
    /// same determinism concern that governs the resolver's own comparisons.
    /// </remarks>
    private static int CountOccurrences(string text, string value)
    {
        int occurrences = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            occurrences++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return occurrences;
    }

    /// <summary>
    /// Resolves one public static overload of <see cref="Exceptions"/> by name and parameter list.
    /// </summary>
    /// <param name="memberName">The member name.</param>
    /// <param name="parameterTypes">The exact parameter type list.</param>
    /// <returns>The resolved method.</returns>
    /// <exception cref="InvalidOperationException">
    /// The overload does not exist, which means the public surface has changed shape and the
    /// attribute assertions that depend on it can no longer be made.
    /// </exception>
    /// <remarks>
    /// The parameter list is passed explicitly because <c>ThrowException</c> is overloaded and a
    /// name-only lookup would raise an ambiguity fault. Failing with a stated reason rather than
    /// returning null keeps the calling test's failure message meaningful and keeps this helper free
    /// of the null-forgiving operator that C-H forbids.
    /// </remarks>
    private static MethodInfo ResolveEntryPoint(string memberName, Type[] parameterTypes) =>
        typeof(Exceptions).GetMethod(
            memberName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            parameterTypes,
            modifiers: null)
        ?? throw new InvalidOperationException(
            "Exceptions." + memberName + " with " + parameterTypes.Length
            + " string parameter(s) was not found. The public surface asserted by this suite has "
            + "changed shape.");

    /// <summary>
    /// Yields a null reference whose static type is the non-nullable <see cref="string"/>.
    /// </summary>
    /// <returns>A null reference.</returns>
    /// <remarks>
    /// <para>
    /// C-H forbids the null-forgiving <c>!</c> operator, any <c>#pragma</c> and any
    /// <c>#nullable</c> directive, yet two behaviours under test are DEFINED for a null argument:
    /// the class-name guard at <c>throwexception.srf:L23</c> deliberately does not treat null as
    /// empty, and the registry deliberately reports failure rather than raising. Both are documented
    /// as what happens when a nullable-oblivious caller forces a null through a parameter declared
    /// non-nullable, so testing them requires reproducing exactly that.
    /// </para>
    /// <para>
    /// A freshly allocated array of a non-nullable reference type has null elements while its
    /// element type is annotated non-nullable, and the compiler's flow analysis does not track array
    /// element initialisation - so reading element zero produces a null with a non-nullable static
    /// type and no diagnostic. That is the whole trick, and it is honest about what it is: it
    /// simulates a caller from a nullable-oblivious assembly rather than asserting that null is
    /// legal.
    /// </para>
    /// <para>
    /// A caller must not null-test the returned value before passing it on. Doing so splits the flow
    /// state, and the merge afterwards makes the value maybe-null again, which reintroduces the very
    /// nullable diagnostic this helper exists to avoid.
    /// </para>
    /// </remarks>
    private static string NullReferenceTypedAsString() => (new string[1])[0];

    /// <summary>
    /// Yields a null reference whose static type is the non-nullable factory delegate that
    /// <see cref="Exceptions.TryRegisterExceptionType"/> accepts.
    /// </summary>
    /// <returns>A null reference.</returns>
    /// <remarks>
    /// The same technique and the same rationale as <see cref="NullReferenceTypedAsString"/>, for
    /// the registry's documented null-factory behaviour: it reports failure and never raises.
    /// </remarks>
    private static Func<string, Exception> NullReferenceTypedAsFactory() =>
        (new Func<string, Exception>[1])[0];

    // ==========================================================================================
    //  PROBE TYPES FOR THE LOADED-ASSEMBLY SCAN
    //
    //  These exist so that the four creatability conditions of the resolver's stage 2 filter can be
    //  exercised against real types in a real loaded assembly - this one - without loading anything
    //  from disk, which the implementation deliberately never does.
    //
    //  Each name is distinctive on purpose. Matching is by SIMPLE name across every loaded
    //  assembly, so a generic name such as "ProbeException" risks colliding with an unrelated type
    //  in some future dependency and turning one of these tests into a false pass or a false
    //  failure. The `Probe` prefix plus a description of the condition keeps them unique.
    //
    //  They are nested rather than top level so they cannot be mistaken for production types. A
    //  nested PUBLIC type is still exported, and its Type.Name is the simple name, so the scan
    //  reaches it exactly as it would a top-level type - which is also why the non-public probe is
    //  nested INTERNAL: an internal nested type is not exported, so it demonstrates the
    //  public-visibility condition.
    // ==========================================================================================

    /// <summary>
    /// Satisfies all four creatability conditions, so the scan resolves it: public, non-abstract,
    /// non-generic, derived from <see cref="Exception"/>, with a public single-string constructor.
    /// </summary>
    /// <param name="message">The undecorated message text.</param>
    /// <remarks>
    /// It deliberately does NOT decorate. Decoration belongs to <see cref="PfwException"/> alone
    /// [pfwexception.sru:L23], and a probe that decorated could not distinguish "the resolver left
    /// the text alone" from "the resolver decorated it".
    /// </remarks>
    public sealed class ProbeResolvableException(string message) : Exception(message);

    /// <summary>
    /// Fails the non-abstract condition, so the scan must not resolve it.
    /// </summary>
    /// <param name="message">The undecorated message text.</param>
    public abstract class ProbeAbstractException(string message) : Exception(message);

    /// <summary>
    /// Fails the public-single-string-constructor condition, so the scan must not resolve it.
    /// </summary>
    /// <remarks>
    /// Treating such a type as unresolved rather than creating it message-less is deliberate:
    /// silently discarding the caller's text would be worse than the clear failure the caller gets
    /// instead.
    /// </remarks>
    public sealed class ProbeExceptionWithoutTextConstructor : Exception;

    /// <summary>
    /// Fails the public-visibility condition - a nested internal type is not exported - so the scan
    /// must not resolve it.
    /// </summary>
    /// <param name="message">The undecorated message text.</param>
    /// <remarks>
    /// The legacy mechanism reaches GLOBAL objects, whose managed counterpart is a publicly visible
    /// type, and restricting the scan that way also keeps it from surfacing a type no consumer could
    /// name.
    /// </remarks>
    internal sealed class ProbeNonPublicException(string message) : Exception(message);

    /// <summary>
    /// Fails the derives-from-<see cref="Exception"/> condition, so the scan must not resolve it.
    /// </summary>
    /// <remarks>
    /// The legacy variable at <c>throwexception.srf:L21</c> is declared <c>throwable</c>, and only a
    /// throwable can be thrown.
    /// </remarks>
    public sealed class ProbeTypeThatIsNotAnException;
}
