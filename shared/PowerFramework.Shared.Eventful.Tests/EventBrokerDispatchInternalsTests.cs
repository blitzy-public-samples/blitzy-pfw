// ==================================================================================================
//  EventBrokerDispatchInternalsTests.cs - THE MACHINERY BEHIND ONE DISPATCH
//  ------------------------------------------------------------------------------------------------
//  UNIT UNDER TEST   PowerFramework.Shared.Eventful.EventBroker, exercised through its public surface
//                    to reach the private helpers a dispatch depends on
//  ORACLE            ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru
//
//  WHY A SECOND FILE
//  ------------------------------------------------------------------------------------------------
//  EventBrokerTests covers the broker's OBSERVABLE contract - registration, ordering, capture gating,
//  prevention, posting, and the supporting types. This file covers the four pieces of machinery that
//  contract sits on, each of which has its own behaviour a caller can trip over:
//
//    1. VALUE COMPARISON. Deciding whether a handler's return differs from the established default is
//       what flips the handled state, and PowerScript's `any` comparison is not C# equality: it compares
//       numbers across widths, strings ordinally, and THROWS for two incomparable scalars. The broker
//       catches that throw and treats it as "different", so an incomparable pair silently marks the event
//       handled. That is behaviour, not an implementation detail.
//
//    2. ARGUMENT COERCION AND DEFAULTING. A handler's parameters are pre-filled with type-appropriate
//       initial values and then overwritten from the trigger's arguments, coerced to the declared type.
//       So a handler declaring more parameters than the trigger supplies receives initialized values
//       rather than nulls, and a trigger supplying a widening-compatible value reaches a narrower
//       parameter.
//
//    3. HANDLER RESOLUTION. Name matching is case-insensitive across the whole base-type chain including
//       non-public methods, duplicates are collapsed by parameter type, and an explicit signature accepts
//       PowerScript type spellings and parameter modifiers.
//
//    4. ASSERTION-DETAIL INTEROP. The broker prefers a structured assertion payload over a generic
//       exception rendering, and reaches it two ways: an interface this project publishes, and DUCK
//       TYPING on the type name plus two string properties. The duck-typed route exists because Eventful
//       must not take a project reference on Diagnostics - the two are sibling shared libraries - so the
//       coupling is by shape rather than by type.
//
// ==================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.Shared.Eventful.Tests;

/// <summary>
/// Characterization tests for the machinery a single <see cref="EventBroker"/> dispatch relies on.
/// </summary>
public class EventBrokerDispatchInternalsTests
{
    // ==============================================================================================
    //  TEST TARGETS
    // ==============================================================================================

    /// <summary>
    /// A target whose handler returns a caller-supplied value, used to drive value comparison.
    /// </summary>
    private sealed class Answering
    {
        /// <summary>Initializes a new instance of the <see cref="Answering"/> class.</summary>
        /// <param name="answer">The value the handler returns.</param>
        internal Answering(object? answer) => Answer = answer;

        /// <summary>Gets the value the handler returns.</summary>
        internal object? Answer { get; }

        /// <summary>Gets the number of times the handler ran.</summary>
        internal int CallCount { get; private set; }

        /// <summary>Answers with the configured value.</summary>
        /// <returns>The configured value.</returns>
        public object? OnEvent()
        {
            CallCount++;
            return Answer;
        }
    }

    /// <summary>
    /// A target recording the exact values its typed parameters received.
    /// </summary>
    private sealed class TypedParameters
    {
        /// <summary>Gets the values the last invocation received, keyed by parameter name.</summary>
        internal Dictionary<string, object?> Received { get; } = [];

        /// <summary>A handler with one parameter of each common scalar type.</summary>
        /// <param name="number">A 64-bit integer parameter.</param>
        /// <param name="text">A string parameter.</param>
        /// <param name="flag">A boolean parameter.</param>
        /// <param name="amount">A decimal parameter.</param>
        /// <param name="fraction">A double parameter.</param>
        /// <returns>Always null, so the handled state is not disturbed.</returns>
        public object? OnScalars(long number, string? text, bool flag, decimal amount, double fraction)
        {
            Received["number"] = number;
            Received["text"] = text;
            Received["flag"] = flag;
            Received["amount"] = amount;
            Received["fraction"] = fraction;
            return null;
        }
    }

    /// <summary>
    /// A base target declaring a handler, used to prove resolution walks the base-type chain.
    /// </summary>
    private class InheritedBase
    {
        /// <summary>Gets a value indicating whether the inherited handler ran.</summary>
        internal bool InheritedRan { get; private set; }

        /// <summary>A handler declared on the BASE type.</summary>
        /// <returns>Always null.</returns>
        public object? OnInherited()
        {
            InheritedRan = true;
            return null;
        }

        /// <summary>A NON-PUBLIC handler, which resolution must still find.</summary>
        /// <returns>Always null.</returns>
        private object? OnPrivateHandler()
        {
            PrivateRan = true;
            return null;
        }

        /// <summary>Gets a value indicating whether the non-public handler ran.</summary>
        internal bool PrivateRan { get; private set; }
    }

    /// <summary>
    /// A derived target that declares no handler of its own.
    /// </summary>
    private sealed class InheritedDerived : InheritedBase
    {
    }

    /// <summary>
    /// A target offering overloads that differ only by parameter modifier, to drive signature matching.
    /// </summary>
    private sealed class ModifierOverloads
    {
        /// <summary>Gets the arity of the overload that ran.</summary>
        internal int ChosenArity { get; private set; } = -1;

        /// <summary>A three-parameter overload.</summary>
        /// <param name="first">First parameter.</param>
        /// <param name="second">Second parameter.</param>
        /// <param name="third">Third parameter.</param>
        /// <returns>Always null.</returns>
        public object? OnEvent(long first, string? second, bool third)
        {
            ChosenArity = 3;
            _ = first;
            _ = second;
            _ = third;
            return null;
        }

        /// <summary>A one-parameter overload.</summary>
        /// <param name="only">The only parameter.</param>
        /// <returns>Always null.</returns>
        public object? OnEvent(decimal only)
        {
            ChosenArity = 1;
            _ = only;
            return null;
        }
    }

    /// <summary>
    /// An exception implementing the published assertion-detail interface.
    /// </summary>
    private sealed class InterfaceCarryingException : Exception, IAssertionDetail
    {
        /// <inheritdoc/>
        public string Info => "interface info";

        /// <inheritdoc/>
        public string StackTraceInfo => "interface frames";
    }

    /// <summary>
    /// An exception the broker recognises by NAME and shape rather than by type - the duck-typed route.
    /// </summary>
    /// <remarks>
    /// Deliberately named <c>AssertionFailure</c>, matching the Diagnostics project's type, and carrying
    /// the two public string properties the broker reads. It is NOT that type: this project has no
    /// reference to Diagnostics, which is exactly the situation the duck-typed route exists for.
    /// </remarks>
    private sealed class AssertionFailure : Exception
    {
        /// <summary>Gets the assertion information field.</summary>
        public string Info => "duck-typed info";

        /// <summary>Gets the rendered stack trace field.</summary>
        public string StackTraceInfo => "duck-typed frames";
    }

    // CS8981 is suppressed for exactly one declaration, and the suppression is the point rather than a
    // convenience. The broker recognises the legacy assertion type by the name PowerBuilder gives it -
    // 'assertionfailed', all lower case, as declared at
    // ws_objects/pfw.common.pbl.src/assertionfailed.sru - so a type with any other spelling would not
    // exercise the branch. The analyzer's concern is that all-lower-case names may become language
    // keywords; that risk is accepted here because this type is private to one test file and its name is
    // dictated by the oracle, not chosen. Renaming it would silently stop testing the legacy-name path.
#pragma warning disable CS8981

    /// <summary>
    /// An exception named like the LEGACY assertion type, in the legacy's own lower-case spelling.
    /// </summary>
    private sealed class assertionfailed : Exception
    {
        /// <summary>Gets the assertion information field.</summary>
        public string Info => "legacy-named info";

        /// <summary>Gets the rendered stack trace field.</summary>
        public string StackTraceInfo => "legacy-named frames";
    }

#pragma warning restore CS8981

    /// <summary>
    /// An exception with the right NAME but the wrong SHAPE - one property missing.
    /// </summary>
    private sealed class AssertionFailureWithoutFrames : Exception
    {
        /// <summary>Gets the assertion information field.</summary>
        public string Info => "info only";
    }

    /// <summary>
    /// A target that throws a supplied exception.
    /// </summary>
    private sealed class Thrower
    {
        /// <summary>Initializes a new instance of the <see cref="Thrower"/> class.</summary>
        /// <param name="exception">The exception to throw.</param>
        internal Thrower(Exception exception) => Exception = exception;

        /// <summary>Gets the exception thrown.</summary>
        internal Exception Exception { get; }

        /// <summary>Throws the configured exception.</summary>
        /// <returns>Never returns.</returns>
        public object? OnEvent() => throw Exception;
    }

    /// <summary>
    /// A derived broker whose hooks DELEGATE to the base implementations.
    /// </summary>
    /// <remarks>
    /// The base bodies are unreachable on a plain broker, because the subclassing flag is false there, and
    /// unreachable on a broker that fully overrides them. Delegating is the one route that executes them,
    /// and it is a normal pattern for a derived broker that wants the documented default for a hook it does
    /// not care about.
    /// </remarks>
    private sealed class DelegatingBroker : EventBroker
    {
        /// <summary>Gets the values the base hooks returned.</summary>
        internal Dictionary<string, long> BaseResults { get; } = [];

        /// <inheritdoc/>
        protected override long OnTriggering(string name, bool isPost)
        {
            long result = base.OnTriggering(name, isPost);
            BaseResults[nameof(OnTriggering)] = result;
            return result;
        }

        /// <inheritdoc/>
        protected override void OnTriggered(string name, bool isPost)
        {
            base.OnTriggered(name, isPost);
            BaseResults[nameof(OnTriggered)] = RetCode.OK;
        }

        /// <inheritdoc/>
        protected override long OnException(string name, Exception exception)
        {
            long result = base.OnException(name, exception);
            BaseResults[nameof(OnException)] = result;
            return result;
        }

        /// <inheritdoc/>
        protected override long OnPrepare(string name, object target, EventArgumentContext arguments)
        {
            long result = base.OnPrepare(name, target, arguments);
            BaseResults[nameof(OnPrepare)] = result;
            return result;
        }
    }

    /// <summary>
    /// Runs a dispatch in which a handler returns <paramref name="handlerValue"/> against an established
    /// default of <paramref name="defaultValue"/>, and reports whether a following unhandled subscriber
    /// still ran.
    /// </summary>
    /// <param name="defaultValue">The established default return value.</param>
    /// <param name="handlerValue">The value the first handler returns.</param>
    /// <returns><see langword="true"/> when the values compared as DIFFERENT, flipping the state.</returns>
    /// <remarks>
    /// The comparison is private, so the handled-state transition is the observable it produces: a
    /// difference flips the state and suppresses the following unhandled subscriber. This helper turns
    /// that into a boolean so a theory can assert the comparison directly.
    /// </remarks>
    private static bool ComparesAsDifferent(object? defaultValue, object? handlerValue)
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", defaultValue));

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("!name", new Answering(handlerValue), nameof(Answering.OnEvent)));

        Answering follower = new(null);
        Assert.Equal(RetCode.OK, broker.Subscribe("name", follower, nameof(Answering.OnEvent)));

        broker.Trigger("name");

        // The follower is skipped exactly when the state flipped, which happens exactly when the two
        // values compared as different.
        return follower.CallCount == 0;
    }

    // ==============================================================================================
    //  1. VALUE COMPARISON
    // ==============================================================================================

    /// <summary>
    /// Numbers compare by VALUE across every width and signedness, so a widening difference is not a
    /// difference.
    /// </summary>
    /// <param name="defaultValue">The established default.</param>
    /// <param name="handlerValue">The handler's return.</param>
    /// <param name="expectDifferent">Whether the two should compare as different.</param>
    /// <remarks>
    /// PowerScript compares two numeric <c>any</c> values numerically regardless of their declared types,
    /// and the port reproduces that through a decimal widening - or a double one when either side is
    /// floating point. So an <c>int</c> 5 equals a <c>long</c> 5 and a <c>decimal</c> 5, and a caller
    /// cannot flip the handled state merely by returning the right number in the wrong width.
    /// </remarks>
    [Theory]
    [InlineData(5L, 5, false)]
    [InlineData(5L, (short)5, false)]
    [InlineData(5L, (byte)5, false)]
    [InlineData(5, 5L, false)]
    [InlineData(5L, 5.0, false)]
    [InlineData(5.0, 5L, false)]
    [InlineData(5L, 6, true)]
    [InlineData(5L, -5, true)]
    [InlineData(0L, 0, false)]
    [InlineData(0.5, 0.5, false)]
    [InlineData(0.5, 0.25, true)]
    public void NumbersCompareByValueAcrossWidths(
        object defaultValue,
        object handlerValue,
        bool expectDifferent)
    {
        Assert.Equal(expectDifferent, ComparesAsDifferent(defaultValue, handlerValue));
    }

    /// <summary>
    /// A decimal default and a decimal return compare exactly, without a floating-point round trip.
    /// </summary>
    /// <remarks>
    /// The branch selection matters: the comparison widens to <c>double</c> only when either side IS
    /// floating point, and to <c>decimal</c> otherwise. Two decimals that differ beyond double's precision
    /// must therefore still compare as different - which a blanket double conversion would lose, and which
    /// is exactly the case a monetary default return value would hit.
    /// </remarks>
    [Fact]
    public void TwoDecimalsCompareExactlyWithoutAFloatingPointRoundTrip()
    {
        decimal baseline = 1.0000000000000000000000000001m;
        decimal differsBeyondDoublePrecision = 1.0000000000000000000000000002m;

        Assert.Equal((double)baseline, (double)differsBeyondDoublePrecision);

        Assert.True(ComparesAsDifferent(baseline, differsBeyondDoublePrecision));
        Assert.False(ComparesAsDifferent(baseline, baseline));
    }

    /// <summary>
    /// Strings compare ORDINALLY, so case and culture never make two different strings equal.
    /// </summary>
    /// <param name="defaultValue">The established default.</param>
    /// <param name="handlerValue">The handler's return.</param>
    /// <param name="expectDifferent">Whether the two should compare as different.</param>
    /// <remarks>
    /// Ordinal rather than culture-aware, matching the comparison the subscription name uses. A
    /// culture-aware comparison would make the handled state depend on the host's locale - so a handler
    /// returning a string default would flip the state on one machine and not another.
    /// </remarks>
    [Theory]
    [InlineData("value", "value", false)]
    [InlineData("value", "VALUE", true)]
    [InlineData("value", "other", true)]
    [InlineData("", "", false)]
    [InlineData("", " ", true)]
    [InlineData("数据", "数据", false)]
    [InlineData("数据", "數據", true)]
    public void StringsCompareOrdinally(string defaultValue, string handlerValue, bool expectDifferent)
    {
        Assert.Equal(expectDifferent, ComparesAsDifferent(defaultValue, handlerValue));
    }

    /// <summary>
    /// Booleans and characters compare by value.
    /// </summary>
    /// <remarks>
    /// Their own branches in the comparison, separate from the numeric one - a <c>bool</c> is not numeric
    /// in PowerScript's comparison rules, and neither is a <c>char</c>. Pinning them means a boolean
    /// default return value behaves as a caller expects rather than falling through to the type-equality
    /// branch or the incomparable throw.
    /// </remarks>
    [Fact]
    public void BooleansAndCharactersCompareByValue()
    {
        Assert.False(ComparesAsDifferent(true, true));
        Assert.False(ComparesAsDifferent(false, false));
        Assert.True(ComparesAsDifferent(true, false));
        Assert.True(ComparesAsDifferent(false, true));

        Assert.False(ComparesAsDifferent('a', 'a'));
        Assert.True(ComparesAsDifferent('a', 'b'));
        Assert.True(ComparesAsDifferent('a', 'A'));
    }

    /// <summary>
    /// Two values of the SAME non-scalar type compare with that type's own equality.
    /// </summary>
    /// <remarks>
    /// The fall-through branch, reached when both sides share a type that is not one of the special-cased
    /// families. It defers to the type's <c>Equals</c>, so a value type or a record compares structurally
    /// and a plain class compares by reference - which is the sensible reading and is what a caller
    /// returning a domain object gets.
    /// </remarks>
    [Fact]
    public void SameTypedValuesCompareWithTheirOwnEquality()
    {
        // A value type with structural equality.
        Assert.False(ComparesAsDifferent(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1)));
        Assert.True(ComparesAsDifferent(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));

        Assert.False(ComparesAsDifferent(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(60)));
        Assert.True(ComparesAsDifferent(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(61)));

        // A record: structural.
        DefaultReturnValue left = new() { Name = "n", Value = 1L };
        DefaultReturnValue right = new() { Name = "n", Value = 1L };

        Assert.False(ComparesAsDifferent(left, right));
        Assert.True(ComparesAsDifferent(left, left with { Name = "other" }));
    }

    /// <summary>
    /// The SAME reference compares as NOT different, short-circuiting before any type analysis.
    /// </summary>
    /// <remarks>
    /// The first branch after the null checks, and it is what makes the comparison total for types that
    /// would otherwise throw: a handler returning the very object that was set as the default is never
    /// "different", whatever its type. Demonstrated with an object that has no comparable family at all.
    /// </remarks>
    [Fact]
    public void TheSameReferenceIsNeverDifferent()
    {
        object shared = new();

        Assert.False(ComparesAsDifferent(shared, shared));
    }

    /// <summary>
    /// A NULL on either side makes the comparison indeterminate, and the broker treats a non-null return
    /// against a null default as HANDLING the event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparison answers "unknown" rather than true or false when either side is null, mirroring
    /// PowerScript's null propagation. The broker resolves that at the call site: with no default
    /// established, or a null one, any non-null return handles the event.
    /// </para>
    /// <para>
    /// The reverse - a null RETURN - never handles, whatever the default, because the broker only
    /// considers a non-null value at all. So the two null cases are asymmetric, and both are pinned.
    /// </para>
    /// </remarks>
    [Fact]
    public void NullMakesTheComparisonIndeterminateAndTheBrokerResolvesItAsHandled()
    {
        // Null default, non-null return: handled.
        Assert.True(ComparesAsDifferent(null, "anything"));

        // Non-null default, NULL return: never handled - the broker ignores a null return entirely.
        Assert.False(ComparesAsDifferent("a default", null));

        // Both null: nothing to handle.
        Assert.False(ComparesAsDifferent(null, null));
    }

    /// <summary>
    /// TWO INCOMPARABLE SCALARS make the comparison THROW, and the broker catches it and treats the values
    /// as different - so the event is HANDLED.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PowerScript refuses to compare, say, a string with a date, and the port reproduces the refusal by
    /// throwing. The broker's <c>catch</c> then resolves the unanswerable comparison as "different",
    /// flipping the handled state - so an accidentally mismatched default and return value SILENTLY
    /// suppress every following unhandled subscriber rather than surfacing an error.
    /// </para>
    /// <para>
    /// Reproduced rather than corrected under C-B, and pinned here because the throw is invisible from
    /// outside: nothing propagates, and the only symptom is a subscriber that stopped running. A reader
    /// debugging that would have no reason to suspect a type mismatch in a default value.
    /// </para>
    /// </remarks>
    [Fact]
    public void TwoIncomparableScalarsAreResolvedAsDifferentRatherThanSurfacingAnError()
    {
        // A string against a date: PowerScript cannot compare these.
        Assert.True(ComparesAsDifferent("a string", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        // A boolean against a number: likewise.
        Assert.True(ComparesAsDifferent(true, 1L));
        Assert.True(ComparesAsDifferent(1L, true));

        // A character against a string, which look interchangeable and are not.
        Assert.True(ComparesAsDifferent('a', "a"));

        // Nothing propagated to the caller in any of those cases - the dispatch completed normally.
        EventBroker broker = new();
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("name", "a string"));
        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("name", new Answering(true), nameof(Answering.OnEvent)));

        Assert.Equal(true, broker.Trigger("name"));
    }

    /// <summary>
    /// Two DIFFERENTLY typed non-scalars compare as different without throwing.
    /// </summary>
    /// <remarks>
    /// The last branch before the throw: when NEITHER side is a scalar there is nothing meaningful to
    /// compare, so the answer is "different" rather than an error. Only a scalar-versus-something mismatch
    /// throws - which is what keeps a handler returning a domain object safe while a handler returning the
    /// wrong scalar is not.
    /// </remarks>
    [Fact]
    public void TwoDifferentlyTypedNonScalarsCompareAsDifferentWithoutThrowing()
    {
        Assert.True(ComparesAsDifferent(new object(), new DefaultReturnValue()));
        Assert.True(ComparesAsDifferent(new List<string>(), new Dictionary<string, string>()));
    }

    // ==============================================================================================
    //  2. ARGUMENT COERCION AND DEFAULTING
    // ==============================================================================================

    /// <summary>
    /// A trigger's arguments are coerced to each parameter's declared type.
    /// </summary>
    /// <remarks>
    /// The trigger carries loosely typed values - the legacy passes <c>any</c> - so the broker converts
    /// each to the parameter's declared type before invoking. Without it a handler declaring <c>long</c>
    /// could not be reached by a trigger that supplied an <c>int</c>, which is the common case when a
    /// literal is passed.
    /// </remarks>
    [Fact]
    public void TriggerArgumentsAreCoercedToEachParametersDeclaredType()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        // Every value is supplied in a DIFFERENT type from the parameter's declared one.
        broker.Trigger("name", 42, "text", true, 3, 4);

        Assert.Equal(42L, target.Received["number"]);
        Assert.Equal("text", target.Received["text"]);
        Assert.Equal(true, target.Received["flag"]);
        Assert.Equal(3m, target.Received["amount"]);
        Assert.Equal(4.0d, target.Received["fraction"]);
    }

    /// <summary>
    /// Parameters the trigger does not supply receive a type-appropriate INITIAL value, not null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// PowerScript initializes an unset numeric to zero, a string to the empty string and a boolean to
    /// false, and the port reproduces that rather than leaving nulls. It matters because a value-typed
    /// parameter cannot hold null at all - the invocation would fail - and because a handler reading an
    /// unsupplied string expects empty rather than a null reference.
    /// </para>
    /// <para>
    /// Asserted by triggering with NO arguments against a five-parameter handler, so every initial value
    /// is observed at once.
    /// </para>
    /// </remarks>
    [Fact]
    public void UnsuppliedParametersReceiveTypeAppropriateInitialValues()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        broker.Trigger("name");

        Assert.Equal(0L, target.Received["number"]);
        Assert.Equal(string.Empty, target.Received["text"]);
        Assert.Equal(false, target.Received["flag"]);
        Assert.Equal(0m, target.Received["amount"]);
        Assert.Equal(0.0d, target.Received["fraction"]);
    }

    /// <summary>
    /// A PARTIALLY supplied argument list fills the leading parameters and initializes the rest.
    /// </summary>
    /// <remarks>
    /// The two mechanisms combined, and the shape a real trigger takes: the DataWindow chain passes as many
    /// arguments as the event has, and a subscriber may declare fewer or more. Fewer is covered by the
    /// narrowest-overload rule; more is covered here, and the handler must be able to tell a supplied
    /// value from an unsupplied one only by its content.
    /// </remarks>
    [Fact]
    public void APartiallySuppliedArgumentListFillsTheLeadingParameters()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        broker.Trigger("name", 7L, "given");

        Assert.Equal(7L, target.Received["number"]);
        Assert.Equal("given", target.Received["text"]);
        Assert.Equal(false, target.Received["flag"]);
        Assert.Equal(0m, target.Received["amount"]);
        Assert.Equal(0.0d, target.Received["fraction"]);
    }

    /// <summary>
    /// SURPLUS trigger arguments are discarded rather than causing a failure.
    /// </summary>
    /// <remarks>
    /// The count is clamped to the declared parameter count, so a trigger may broadcast more arguments than
    /// a given subscriber wants. That is what lets several subscribers with different arities share one
    /// topic - which the legacy relies on, since a subscriber declares only the parameters it cares about.
    /// </remarks>
    [Fact]
    public void SurplusTriggerArgumentsAreDiscarded()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        broker.Trigger("name", 1L, "two", true, 4m, 5.0d, "surplus", 7L, 8L);

        Assert.Equal(1L, target.Received["number"]);
        Assert.Equal(5.0d, target.Received["fraction"]);
    }

    /// <summary>
    /// A null argument reaches a reference parameter as null and a value parameter as its initial value.
    /// </summary>
    /// <remarks>
    /// The asymmetry is forced: a value type cannot hold null, so coercing null to one must yield the
    /// initial value or the invocation fails. Pinning it means a trigger that passes null for an optional
    /// argument behaves predictably rather than throwing inside the reflection call.
    /// </remarks>
    [Fact]
    public void ANullArgumentBecomesNullForAReferenceParameterAndZeroForAValueOne()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        broker.Trigger("name", null, null, null, null, null);

        Assert.Equal(0L, target.Received["number"]);
        Assert.Null(target.Received["text"]);
        Assert.Equal(false, target.Received["flag"]);
        Assert.Equal(0m, target.Received["amount"]);
        Assert.Equal(0.0d, target.Received["fraction"]);
    }

    /// <summary>
    /// Numeric conversion between parameter types uses the INVARIANT culture, so a foreign current culture
    /// cannot change a coerced value.
    /// </summary>
    /// <remarks>
    /// The conversion is <c>Convert.ChangeType</c> with an explicit invariant format provider. Under a
    /// culture whose decimal separator is a comma, a culture-sensitive conversion of a fractional value
    /// could round-trip differently - so the provider is pinned by running the whole dispatch under German
    /// and asserting the same values arrive.
    /// </remarks>
    [Fact]
    public void NumericConversionUsesTheInvariantCulture()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new("de-DE");

            EventBroker broker = new();
            TypedParameters target = new();

            Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

            // Every numeric value is supplied in a type other than the parameter's declared one.
            broker.Trigger("name", 42, "text", true, 3.5d, 4.25f);

            Assert.Equal(42L, target.Received["number"]);
            Assert.Equal(3.5m, target.Received["amount"]);
            Assert.Equal(4.25d, target.Received["fraction"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Booleans and characters ARE convertible sources: they reach a numeric parameter as 1/0 and as their
    /// code point.
    /// </summary>
    /// <remarks>
    /// The convertible-source set is numeric, boolean and character - and nothing else. Booleans and
    /// characters are in it because PowerScript treats both as numeric in an argument position, so a
    /// handler declaring a <c>long</c> can be reached by a trigger supplying either. Pinned because the
    /// values are surprising if the conversion is not expected: <c>'a'</c> arrives as 97.
    /// </remarks>
    [Fact]
    public void BooleansAndCharactersConvertToNumericParameters()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        broker.Trigger("name", true, "text", 1, 0, 0);
        Assert.Equal(1L, target.Received["number"]);

        broker.Trigger("name", false, "text", 0, 0, 0);
        Assert.Equal(0L, target.Received["number"]);

        broker.Trigger("name", 'a', "text", true, 0, 0);
        Assert.Equal(97L, target.Received["number"]);
    }

    /// <summary>
    /// A STRING is NOT a convertible source: a numeric string passed to a numeric parameter is left alone
    /// and the invocation FAILS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MEASURED, and the opposite of what a reader would reasonably assume. The coercion's convertible
    /// source set is numeric, boolean and character - a string is excluded - so <c>"42"</c> passed to a
    /// <c>long</c> parameter stays a string, the reflection invocation rejects it, and an
    /// <see cref="ArgumentException"/> propagates out of <c>Trigger</c>.
    /// </para>
    /// <para>
    /// This is faithful rather than accidental: PowerScript does not parse a string into a number in an
    /// argument position either - an <c>any</c> holding a string passed to a numeric event parameter is a
    /// type error there too. So the failure is preserved under C-B rather than softened into a parse.
    /// </para>
    /// <para>
    /// The consequence for a caller is worth stating plainly: a trigger must pass argument values in types
    /// the handler's parameters can accept, and a numeric string is not one of them. The broker's
    /// annotation identifies which subscriber rejected it, which is asserted here because it is the only
    /// diagnostic a caller gets.
    /// </para>
    /// </remarks>
    [Fact]
    public void AStringIsNotCoercedToANumberAndTheInvocationFails()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        ArgumentException thrown = Assert.Throws<ArgumentException>(
            () => broker.Trigger("name", "42", "text", true, 0, 0));

        Assert.Contains(typeof(string).FullName!, thrown.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(long).FullName!, thrown.Message, StringComparison.Ordinal);

        // The handler never ran, so nothing was recorded.
        Assert.Empty(target.Received);

        // And the broker annotated the failure with the subscriber that rejected the argument.
        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;
        Assert.Contains("Subscribe: name", annotation, StringComparison.Ordinal);
        Assert.Contains(nameof(TypedParameters), annotation, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever the coercion cannot convert it passes through UNCHANGED, so success then depends on
    /// ordinary assignability.
    /// </summary>
    /// <remarks>
    /// The pass-through arm, and the reason the string failure above is a type mismatch rather than a
    /// coercion defect. A number reaching a string parameter takes the same path and fails the same way; a
    /// string reaching a string parameter takes it and succeeds.
    /// </remarks>
    [Fact]
    public void AnUnconvertibleValueIsPassedThroughUnchanged()
    {
        EventBroker broker = new();
        TypedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(TypedParameters.OnScalars)));

        Assert.Throws<ArgumentException>(() => broker.Trigger("name", 1L, 42L, true, 0, 0));

        broker.Trigger("name", 1L, "text", true, 0, 0);
        Assert.Equal("text", target.Received["text"]);
    }

    /// <summary>
    /// An ENUM parameter accepts a numeric argument, converted to the enum's own type.
    /// </summary>
    /// <remarks>
    /// The legacy passes an enumerated constant as its numeric value, so a handler declaring the enum type
    /// must be reachable by a trigger supplying the number. Without this arm every such handler would fail
    /// on invocation - and the framework's own capture, veto and lifetime enums are exactly the kinds of
    /// value a topic carries.
    /// </remarks>
    [Fact]
    public void AnEnumParameterAcceptsANumericArgument()
    {
        EventBroker broker = new();
        WidenedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(WidenedParameters.OnWidened)));

        broker.Trigger("name", 2L, null, null);

        Assert.Equal(VetoResult.PreventDeep, target.Veto);
    }

    /// <summary>
    /// A NULLABLE parameter is converted through its underlying type, and a null argument reaches it as
    /// null.
    /// </summary>
    /// <remarks>
    /// The nullable is unwrapped before the conversion decision, so an <c>int</c> argument reaches a
    /// <c>long?</c> parameter as a long - which a conversion that inspected the nullable wrapper directly
    /// would refuse, since <c>Nullable&lt;long&gt;</c> is not a primitive. Both the value and the null case
    /// are asserted because they take different paths: null short-circuits before any type analysis.
    /// </remarks>
    [Fact]
    public void ANullableParameterIsConvertedThroughItsUnderlyingType()
    {
        EventBroker broker = new();
        WidenedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(WidenedParameters.OnWidened)));

        broker.Trigger("name", 0L, 42, null);
        Assert.Equal(42L, target.Optional);

        broker.Trigger("name", 0L, null, null);
        Assert.Null(target.Optional);
    }

    /// <summary>
    /// A reference-typed parameter the trigger does not supply receives NULL, not a constructed instance.
    /// </summary>
    /// <remarks>
    /// The initial-value rule is: the empty string for a string, a zeroed instance for a value type, and
    /// NULL for everything else. Constructing an instance for a reference parameter would be both wrong and
    /// impossible in general - the type may have no accessible parameterless constructor - so null is the
    /// only sound answer and the handler has to be prepared for it.
    /// </remarks>
    [Fact]
    public void AnUnsuppliedReferenceParameterReceivesNull()
    {
        EventBroker broker = new();
        WidenedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(WidenedParameters.OnWidened)));

        broker.Trigger("name");

        Assert.Null(target.Reference);
        Assert.Null(target.Optional);
        Assert.Equal(VetoResult.Continue, target.Veto);
    }

    /// <summary>
    /// A reference-typed parameter accepts any assignable value unchanged.
    /// </summary>
    /// <remarks>
    /// The already-assignable short-circuit, which runs before every conversion arm. It is what lets a
    /// topic carry a domain object - the DataWindow chain passes a control reference this way - without the
    /// coercion having to know anything about the type.
    /// </remarks>
    [Fact]
    public void AReferenceParameterAcceptsAnyAssignableValueUnchanged()
    {
        EventBroker broker = new();
        WidenedParameters target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(WidenedParameters.OnWidened)));

        object payload = new();
        broker.Trigger("name", 0L, null, payload);

        Assert.Same(payload, target.Reference);
    }

    /// <summary>
    /// A value too large for a narrower parameter is left UNCHANGED rather than wrapped, and the invocation
    /// then fails.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The overflow arm returns the original value rather than a truncated one, which is the safe choice:
    /// silently wrapping <see cref="long.MaxValue"/> into a byte would hand the handler a plausible but
    /// meaningless number. Leaving it alone means the mismatch surfaces at the invocation instead.
    /// </para>
    /// <para>
    /// So an out-of-range argument behaves exactly like an unconvertible one - a failed dispatch with an
    /// annotation naming the subscriber - rather than like a successful one with corrupt data. Pinned
    /// because the alternative is the more common implementation and is silently wrong.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOutOfRangeValueIsLeftUnchangedRatherThanWrapped()
    {
        EventBroker broker = new();
        NarrowParameter target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(NarrowParameter.OnNarrow)));

        Assert.Throws<ArgumentException>(() => broker.Trigger("name", long.MaxValue));

        Assert.Equal(-1, target.Received);

        // An in-range value converts normally, so the arm above is the overflow and not the conversion.
        broker.Trigger("name", 7L);
        Assert.Equal((byte)7, target.Received);
    }

    /// <summary>
    /// A target declaring an enum, a nullable and a reference parameter.
    /// </summary>
    private sealed class WidenedParameters
    {
        /// <summary>Gets the enum value received.</summary>
        internal VetoResult Veto { get; private set; } = VetoResult.Continue;

        /// <summary>Gets the nullable value received.</summary>
        internal long? Optional { get; private set; }

        /// <summary>Gets the reference value received.</summary>
        internal object? Reference { get; private set; }

        /// <summary>A handler with an enum, a nullable and a reference parameter.</summary>
        /// <param name="veto">An enum parameter.</param>
        /// <param name="optional">A nullable parameter.</param>
        /// <param name="reference">A reference parameter.</param>
        /// <returns>Always null.</returns>
        public object? OnWidened(VetoResult veto, long? optional, object? reference)
        {
            Veto = veto;
            Optional = optional;
            Reference = reference;
            return null;
        }
    }

    /// <summary>
    /// A target declaring a parameter narrower than the values a trigger may supply.
    /// </summary>
    private sealed class NarrowParameter
    {
        /// <summary>Gets the value received, or -1 when the handler never ran.</summary>
        internal int Received { get; private set; } = -1;

        /// <summary>A handler with a byte parameter.</summary>
        /// <param name="value">The narrow parameter.</param>
        /// <returns>Always null.</returns>
        public object? OnNarrow(byte value)
        {
            Received = value;
            return null;
        }
    }

    // ==============================================================================================
    //  3. HANDLER RESOLUTION
    // ==============================================================================================

    /// <summary>
    /// Resolution walks the BASE-TYPE chain, so a handler declared on a base type is found on a derived
    /// target.
    /// </summary>
    /// <remarks>
    /// PowerScript resolves an event on the whole ancestry, and the framework's own objects rely on it -
    /// <c>n_cst_threading_eventful</c> inherits the broker itself. A resolution that searched only the
    /// declared type would fail for every derived subscriber.
    /// </remarks>
    [Fact]
    public void ResolutionWalksTheBaseTypeChain()
    {
        EventBroker broker = new();
        InheritedDerived target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(InheritedBase.OnInherited)));

        broker.Trigger("name");

        Assert.True(target.InheritedRan);
    }

    /// <summary>
    /// Resolution finds a NON-PUBLIC handler.
    /// </summary>
    /// <remarks>
    /// PowerScript has no accessibility barrier for event resolution, so the port searches non-public
    /// methods too. It is a deliberate widening of what C# would normally allow, and it is required: a
    /// legacy object frequently declares its handlers private and relies on the broker reaching them.
    /// </remarks>
    [Fact]
    public void ResolutionFindsANonPublicHandler()
    {
        EventBroker broker = new();
        InheritedDerived target = new();

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, "OnPrivateHandler"));

        broker.Trigger("name");

        Assert.True(target.PrivateRan);
    }

    /// <summary>
    /// An explicit signature accepts PowerScript type spellings and tolerates whitespace and parameter
    /// modifiers.
    /// </summary>
    /// <param name="signature">The signature as a caller might write it.</param>
    /// <param name="expectedArity">The overload it should select.</param>
    /// <remarks>
    /// <para>
    /// The signature is a legacy string, so it is written in PowerScript's vocabulary - <c>long</c>,
    /// <c>string</c>, <c>boolean</c>, <c>dec</c> - and may carry the <c>ref</c>, <c>out</c>,
    /// <c>readonly</c> and <c>in</c> modifiers a legacy declaration includes. All of them are normalized
    /// away before matching, and matching is case-insensitive.
    /// </para>
    /// <para>
    /// An EMPTY part is a wildcard, which is what lets a caller pin the arity without naming every type -
    /// the last row covers it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("long,string,boolean", 3)]
    [InlineData("LONG,STRING,BOOLEAN", 3)]
    [InlineData("Int64,String,Boolean", 3)]
    [InlineData("  long , string , boolean  ", 3)]
    [InlineData("ref long,out string,in boolean", 3)]
    [InlineData("readonly long,string,boolean", 3)]
    [InlineData(",,", 3)]
    [InlineData("dec", 1)]
    [InlineData("decimal", 1)]
    [InlineData("Decimal", 1)]
    [InlineData("", 1)]
    public void AnExplicitSignatureAcceptsLegacySpellingsAndModifiers(string signature, int expectedArity)
    {
        EventBroker broker = new();
        ModifierOverloads target = new();

        Assert.Equal(
            RetCode.OK,
            broker.Subscribe("name", target, nameof(ModifierOverloads.OnEvent), signature));

        broker.Trigger("name", 1L, "two", true);

        Assert.Equal(expectedArity, target.ChosenArity);
    }

    /// <summary>
    /// A signature whose ARITY matches no overload is rejected even when its type names are valid.
    /// </summary>
    /// <remarks>
    /// Arity is checked before types, so a caller who names the right types in the wrong number gets a
    /// clean rejection rather than a partial match. Pinned because the wildcard behaviour above could
    /// otherwise be read as "arity is optional", and it is not.
    /// </remarks>
    [Fact]
    public void ASignatureWithTheWrongArityIsRejected()
    {
        EventBroker broker = new();
        ModifierOverloads target = new();

        Assert.Equal(
            RetCode.E_EVENT_NOT_FOUND,
            broker.Subscribe("name", target, nameof(ModifierOverloads.OnEvent), "long,string"));

        Assert.Equal(
            RetCode.E_EVENT_NOT_FOUND,
            broker.Subscribe("name", target, nameof(ModifierOverloads.OnEvent), "long,string,boolean,dec"));
    }

    /// <summary>
    /// The annotated dispatch context names the target's full type chain including its enclosing types.
    /// </summary>
    /// <remarks>
    /// The targets in this project are nested private classes, so the chain has more than one link - and
    /// the annotation is what a diagnostic reads to identify which subscriber failed. A bare type name
    /// would be ambiguous between two same-named nested helpers, which is exactly the situation a test
    /// project creates.
    /// </remarks>
    [Fact]
    public void TheAnnotatedContextNamesTheTargetsEnclosingTypeChain()
    {
        EventBroker broker = new();
        Thrower target = new(new InvalidOperationException("boom"));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => broker.Trigger("name"));

        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;

        Assert.Contains(nameof(EventBrokerDispatchInternalsTests), annotation, StringComparison.Ordinal);
        Assert.Contains(nameof(Thrower), annotation, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  4. ASSERTION-DETAIL INTEROP
    // ==============================================================================================

    /// <summary>
    /// An exception implementing <see cref="IAssertionDetail"/> supplies its own annotation detail.
    /// </summary>
    /// <remarks>
    /// The published route, and the one a new implementation should use. The broker prefers the structured
    /// payload over its generic type-name-plus-message rendering, so an assertion arrives with its
    /// information field and rendered frames rather than as an opaque exception.
    /// </remarks>
    [Fact]
    public void AnInterfaceImplementingExceptionSuppliesItsOwnDetail()
    {
        EventBroker broker = new();
        Thrower target = new(new InterfaceCarryingException());

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        InterfaceCarryingException thrown =
            Assert.Throws<InterfaceCarryingException>(() => broker.Trigger("name"));

        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;

        Assert.Contains("interface info", annotation, StringComparison.Ordinal);
        Assert.Contains("interface frames", annotation, StringComparison.Ordinal);
        Assert.Contains("StackTrace:", annotation, StringComparison.Ordinal);

        // The generic rendering is NOT used when the structured one is available.
        Assert.DoesNotContain(
            $"[{nameof(InterfaceCarryingException)}]",
            annotation,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An exception recognised by NAME and SHAPE supplies its detail too - the duck-typed route.
    /// </summary>
    /// <param name="expectedInfo">The information text the exception carries.</param>
    /// <param name="useLegacyName">Whether to throw the legacy-named type rather than the ported one.</param>
    /// <remarks>
    /// <para>
    /// The reason this route exists: Eventful and Diagnostics are sibling shared libraries and Eventful
    /// takes no reference on Diagnostics, so it cannot name that project's <c>AssertionFailure</c> type. It
    /// recognises it by type name plus two readable public string properties instead.
    /// </para>
    /// <para>
    /// BOTH names are accepted - the ported <c>AssertionFailure</c> and the legacy <c>assertionfailed</c>,
    /// the latter case-insensitively - so an assertion raised by either generation of the framework is
    /// rendered structurally. The exception types in this file are deliberately named to match while being
    /// entirely unrelated types, which is what proves the coupling is by shape.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("duck-typed info", false)]
    [InlineData("legacy-named info", true)]
    public void AnExceptionRecognisedByNameAndShapeSuppliesItsDetail(string expectedInfo, bool useLegacyName)
    {
        EventBroker broker = new();
        Exception raised = useLegacyName ? new assertionfailed() : new AssertionFailure();
        Thrower target = new(raised);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        Exception thrown = Assert.ThrowsAny<Exception>(() => broker.Trigger("name"));

        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;

        Assert.Contains(expectedInfo, annotation, StringComparison.Ordinal);
        Assert.Contains("StackTrace:", annotation, StringComparison.Ordinal);
    }

    /// <summary>
    /// An exception with the right NAME but the wrong SHAPE falls back to the generic rendering.
    /// </summary>
    /// <remarks>
    /// The duck-typed route requires BOTH properties, so a type that carries only one is not an assertion
    /// as far as the broker is concerned. Falling back rather than failing is what keeps the recognition
    /// safe: an unrelated type that happens to share the name still produces a usable annotation.
    /// </remarks>
    [Fact]
    public void AnExceptionWithTheRightNameButTheWrongShapeFallsBackToTheGenericRendering()
    {
        EventBroker broker = new();
        Thrower target = new(new AssertionFailureWithoutFrames());

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        AssertionFailureWithoutFrames thrown =
            Assert.Throws<AssertionFailureWithoutFrames>(() => broker.Trigger("name"));

        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;

        Assert.Contains($"[{nameof(AssertionFailureWithoutFrames)}]", annotation, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace:", annotation, StringComparison.Ordinal);
    }

    /// <summary>
    /// An ordinary exception is rendered as its type name followed by its message.
    /// </summary>
    /// <remarks>
    /// The generic path, pinned so the fallback above has something to be compared against. The bracketed
    /// type name is what makes the rendering identifiable in a log - and distinguishes it from a structured
    /// assertion payload, which carries no brackets.
    /// </remarks>
    [Fact]
    public void AnOrdinaryExceptionIsRenderedAsItsTypeNameAndMessage()
    {
        EventBroker broker = new();
        Thrower target = new(new ArgumentOutOfRangeException("paramName", "the message"));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        ArgumentOutOfRangeException thrown =
            Assert.Throws<ArgumentOutOfRangeException>(() => broker.Trigger("name"));

        string annotation = EventBroker.GetDispatchExceptionText(thrown)!;

        Assert.Contains($"[{nameof(ArgumentOutOfRangeException)}]", annotation, StringComparison.Ordinal);
        Assert.Contains("the message", annotation, StringComparison.Ordinal);
    }

    // ==============================================================================================
    //  5. THE BASE HOOK BODIES
    // ==============================================================================================

    /// <summary>
    /// The base hook implementations return their documented defaults, reachable only by a derived broker
    /// that DELEGATES to them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base bodies are dead on a plain broker - the subclassing flag is false - and dead on a broker
    /// that fully overrides them. Delegation is the one route that executes them, and it is a normal
    /// pattern for a derived broker that wants the default behaviour for a hook it does not care about.
    /// </para>
    /// <para>
    /// The values matter: the triggering and prepare hooks must default to a NON-preventing code, or a
    /// derived broker that delegated would silently suppress every dispatch, and the exception hook must
    /// default to NEITHER of its two constants so an unhandled exception still rethrows.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheBaseHookBodiesReturnTheirDocumentedDefaults()
    {
        DelegatingBroker broker = new();
        Answering target = new(null);

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Answering.OnEvent)));

        broker.Trigger("name");

        // Non-preventing, so a delegating derived broker does not suppress its own dispatches.
        Assert.False(Predicates.IsPrevented(broker.BaseResults["OnTriggering"]));
        Assert.False(Predicates.IsPrevented(broker.BaseResults["OnPrepare"]));
        Assert.True(broker.BaseResults.ContainsKey("OnTriggered"));
        Assert.Equal(1, target.CallCount);
    }

    /// <summary>
    /// A delegating exception hook returns neither constant, so the exception still RETHROWS.
    /// </summary>
    /// <remarks>
    /// The base default has to be "I did not decide", because the two constants both suppress the rethrow -
    /// one continuing to the next subscriber and one stopping the dispatch. A base body returning either
    /// would swallow every exception in any derived broker that delegated, which is precisely the failure
    /// mode a default should not have.
    /// </remarks>
    [Fact]
    public void ADelegatingExceptionHookStillRethrows()
    {
        DelegatingBroker broker = new();
        Thrower target = new(new InvalidOperationException("still thrown"));

        Assert.Equal(RetCode.OK, broker.Subscribe("name", target, nameof(Thrower.OnEvent)));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => broker.Trigger("name"));

        Assert.Equal("still thrown", thrown.Message);

        long baseResult = broker.BaseResults["OnException"];

        Assert.NotEqual(EventBroker.ExceptionResultPrevent, baseResult);
        Assert.NotEqual(EventBroker.ExceptionResultContinue, baseResult);
    }

    // ==============================================================================================
    //  6. DEFAULT-VALUE REGISTRATION GUARDS
    // ==============================================================================================

    /// <summary>
    /// <see cref="EventBroker.SetDefaultReturnValue(string, object?)"/> rejects an empty name.
    /// </summary>
    /// <remarks>
    /// An empty name has a different meaning in <c>Trigger</c> - it short-circuits to the GLOBAL default -
    /// so allowing it to register a per-name entry would create a table row nothing could ever read.
    /// Rejecting it points the caller at the single-argument overload, which is what they meant.
    /// </remarks>
    [Fact]
    public void APerNameDefaultRejectsAnEmptyName()
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.SetDefaultReturnValue(string.Empty, "value"));
        Assert.Equal(RetCode.E_INVALID_ARGUMENT, broker.SetDefaultReturnValue(null!, "value"));

        // The global overload accepts anything, including null.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("value"));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue(null));
    }

    /// <summary>
    /// Several per-name defaults coexist, each reachable by its own name.
    /// </summary>
    /// <remarks>
    /// The table holds one entry per name, so registering a second name must not disturb the first. Pinned
    /// because an implementation holding a single entry would pass every single-name test in this project
    /// while making the feature useless for more than one topic.
    /// </remarks>
    [Fact]
    public void SeveralPerNameDefaultsCoexist()
    {
        EventBroker broker = new();

        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("a", "first"));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("b", "second"));
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("c", 3L));

        Assert.Equal("first", broker.Trigger("a"));
        Assert.Equal("second", broker.Trigger("b"));
        Assert.Equal(3L, broker.Trigger("c"));

        // Replacing one leaves the others alone.
        Assert.Equal(RetCode.OK, broker.SetDefaultReturnValue("b", "replaced"));

        Assert.Equal("first", broker.Trigger("a"));
        Assert.Equal("replaced", broker.Trigger("b"));
        Assert.Equal(3L, broker.Trigger("c"));
    }
}
