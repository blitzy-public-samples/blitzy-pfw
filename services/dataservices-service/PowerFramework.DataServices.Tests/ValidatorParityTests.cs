// ==================================================================================================
//  ValidatorParityTests - THE VALIDATOR THIRD OF THE RETRIEVAL / VALIDATION / UPDATE TRIPLE
//  ------------------------------------------------------------------------------------------------
//  SUBJECT   DateValidator      <- ws_objects/pfw.datawindow.services.pbl.src/dwnvldate.srf
//            DateTimeValidator  <- dwnvldatetime.srf
//            NumberValidator    <- dwnvlnumber.srf
//            StringValidator    <- dwnvlstring.srf
//            TimeValidator      <- dwnvltime.srf
//            ValueToExpression  <- dwvaluetoexp.srf
//
//  WHY THESE SIX ARE PORTED LOGIC RATHER THAN A RULE SET
//  ------------------------------------------------------------------------------------------------
//  A validation framework was deliberately NOT introduced for these. They are ported logic with exact
//  legacy semantics, and wrapping them in a rule engine would obscure parity - which is the one
//  property that matters most here. Their behaviour is aligned to the manual type-directed coercion
//  switch at ws_objects/pfw.datawindow.services.pbl.src/se_cst_dw.sru:L228-L243, which dispatches on
//  the FIRST FIVE CHARACTERS of the column type.
//
//  EVERY MATRIX BELOW WAS MEASURED against the real implementation before being asserted. Four
//  behaviours were counter-intuitive enough that a test written from the source comments alone would
//  have asserted them wrongly, and each is called out where it is pinned:
//
//   1. The five validators handle null and blank input FOUR DIFFERENT WAYS. This is not a bug to
//      harmonise - it is the legacy's own inconsistency, preserved under C-B.
//   2. `TimeValidator.Coerce` and `TimeValidator.TryCoerce` DISAGREE on uncoercible input: one returns
//      a sentinel time, the other reports failure with a null.
//   3. Column-type matching is CASE-SENSITIVE, so "DATE" matches nothing.
//   4. `ValueToExpression` does NOT escape an embedded quote, so a value containing an apostrophe
//      produces a malformed expression. That is a preserved legacy defect and an injection vector.
// ==================================================================================================

using PowerFramework.DataServices.Validators;
using PowerFramework.Shared.Kernel;
using Xunit;

namespace PowerFramework.DataServices.Tests;

public sealed class ValidatorParityTests
{
    // ==============================================================================================
    //  THE FUNCTION NAMES AND SENTINELS - preserved verbatim because they appear in expressions
    // ==============================================================================================

    [Fact]
    public void EachValidatorCarriesItsLegacyFunctionNameVerbatim()
    {
        // THE SPELLINGS ARE THE LEGACY'S, CAMEL-CASED EXACTLY AS THE GLOBAL FUNCTIONS ARE.
        //
        // These names are not decoration: they are emitted INTO DataWindow expression text by
        // ValueToExpression, so a stored expression contains the literal string "dwNvlNumber()". A
        // rename would silently invalidate every stored expression and every characterization recording
        // that captured one.
        Assert.Equal("dwNvlDate", DateValidator.FunctionName);
        Assert.Equal("dwNvlDateTime", DateTimeValidator.FunctionName);
        Assert.Equal("dwNvlNumber", NumberValidator.FunctionName);
        Assert.Equal("dwNvlString", StringValidator.FunctionName);
        Assert.Equal("dwNvlTime", TimeValidator.FunctionName);
        Assert.Equal("dwValueToExp", ValueToExpression.FunctionName);
    }

    [Fact]
    public void TheNullLiteralExpressionsAreTheFunctionNameCalledWithNoArguments()
    {
        // THE `dwNvl*()` FORM IS HOW A NULL IS SPELLED INSIDE A DATAWINDOW EXPRESSION.
        //
        // There is no null literal in the expression language, so the framework calls a function that
        // returns a typed null. That is why each validator owns a sentinel string rather than sharing one:
        // the expression is TYPED, and a null date is not interchangeable with a null number.
        Assert.Equal("dwNvlDate()", DateValidator.NullLiteralExpression);
        Assert.Equal("dwNvlDateTime()", DateTimeValidator.NullLiteralExpression);
        Assert.Equal("dwNvlNumber()", NumberValidator.NullLiteralExpression);
        Assert.Equal("dwNvlString()", StringValidator.NullLiteralExpression);
        Assert.Equal("dwNvlTime()", TimeValidator.NullLiteralExpression);
    }

    [Fact]
    public void EveryNullValueAccessorYieldsATypedNull()
    {
        // PowerBuilder HAS NULL FOR VALUE TYPES, and the tri-state predicates depend on it. These are
        // ported as nullable value types with the null preserved - never collapsed to zero, because
        // collapsing null to zero converts "no value" into "the value 0", which for a salary column is
        // the difference between unknown and free.
        Assert.Null(DateValidator.NullValue());
        Assert.Null(DateTimeValidator.NullValue);
        Assert.Null(TimeValidator.NullValue());
        Assert.Null(StringValidator.NullValue());
        Assert.Null(NumberValidator.NullNumber());
    }

    // ==============================================================================================
    //  COLUMN-TYPE DISPATCH - the five-character prefix from se_cst_dw.sru:L228-L243
    // ==============================================================================================

    [Fact]
    public void TheFiveCharacterPrefixLengthIsSharedByEveryValidatorThatDeclaresIt()
    {
        // FIVE, BECAUSE THE LEGACY COERCION SWITCH READS FIVE CHARACTERS.
        //
        // se_cst_dw.sru:L228-L243 dispatches on the first five characters of the column type. Five is
        // exactly the length that distinguishes "date" from "datet" - which is the ONLY reason the number
        // is five rather than four or six, and the reason a date column and a datetime column reach
        // different validators at all.
        Assert.Equal(5, DateValidator.ColumnTypePrefixLength);
        Assert.Equal(5, NumberValidator.ColumnTypePrefixLength);
        Assert.Equal(5, StringValidator.ColTypePrefixLength);
    }

    [Theory]
    // The measured truncation table. A type SHORTER than five characters is returned unchanged.
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("date", "date")]
    [InlineData("datet", "datet")]
    [InlineData("datetime", "datet")]
    [InlineData("dateti", "datet")]
    [InlineData("time", "time")]
    [InlineData("timestamp", "times")]
    [InlineData("char", "char")]
    [InlineData("char(20)", "char(")]
    [InlineData("decimal(2)", "decim")]
    public void TheColumnTypeIsTruncatedToItsFirstFiveCharacters(string? columnType, string expected)
    {
        Assert.Equal(expected, DateValidator.TruncateColumnType(columnType));

        // ALL THREE TRUNCATING VALIDATORS AGREE, which they must: they are three views of ONE legacy
        // switch, and a disagreement would mean a column reaching two validators or none.
        Assert.Equal(expected, NumberValidator.ColumnTypePrefix(columnType));

        // StringValidator returns null rather than empty for a null input - see its own test below.
        if (columnType is not null)
        {
            Assert.Equal(expected, StringValidator.ColTypePrefix(columnType));
        }
    }

    [Fact]
    public void ANullColumnTypeYieldsEmptyFromMostValidatorsButNullFromTheStringOne()
    {
        // A MEASURED INCONSISTENCY, PRESERVED RATHER THAN HARMONISED.
        //
        // Date and Number return the empty string for a null column type; String returns NULL. Both are
        // then treated as "matches nothing", so the observable dispatch outcome is identical - but the
        // return values differ, and a caller comparing them would see a difference.
        //
        // Pinned so the asymmetry is a known property rather than something a future reader tidies into
        // uniformity, which would be a change with no oracle behind it.
        Assert.Equal(string.Empty, DateValidator.TruncateColumnType(null));
        Assert.Equal(string.Empty, NumberValidator.ColumnTypePrefix(null));
        Assert.Null(StringValidator.ColTypePrefix(null));
    }

    [Theory]
    // Exactly one validator owns each of these, and the measured ownership is the assertion.
    [InlineData("date", "date")]
    [InlineData("datet", "datetime")]
    [InlineData("datetime", "datetime")]
    [InlineData("dateti", "datetime")]
    [InlineData("time", "time")]
    [InlineData("char", "string")]
    [InlineData("char(20)", "string")]
    [InlineData("decim", "decimal")]
    [InlineData("decimal(2)", "decimal")]
    [InlineData("real", "decimal")]
    [InlineData("numbe", "decimal")]
    [InlineData("long", "long")]
    [InlineData("ulong", "long")]

    // AND THESE ARE OWNED BY NOBODY - see the remarks.
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("timestamp", "none")]
    [InlineData("chara", "none")]
    [InlineData("dat", "none")]
    [InlineData("DATE", "none")]
    [InlineData("Date", "none")]
    public void EachColumnTypeIsClaimedByExactlyTheExpectedValidator(string? columnType, string owner)
    {
        var claims = new List<string>();

        if (DateValidator.MatchesColumnType(columnType))
        {
            claims.Add("date");
        }

        if (DateTimeValidator.MatchesColumnType(columnType))
        {
            claims.Add("datetime");
        }

        if (TimeValidator.MatchesColumnType(columnType))
        {
            claims.Add("time");
        }

        if (StringValidator.OwnsColType(columnType))
        {
            claims.Add("string");
        }

        if (NumberValidator.IsDecimalCoercionColumnType(columnType))
        {
            claims.Add("decimal");
        }

        if (NumberValidator.IsLongCoercionColumnType(columnType))
        {
            claims.Add("long");
        }

        // NO COLUMN TYPE IS CLAIMED TWICE, WHICH IS THE PROPERTY THAT MAKES DISPATCH DETERMINISTIC.
        //
        // The most important row is "datet": the date validator must NOT claim it and the datetime
        // validator must. Truncating to five characters is what separates them, and a four-character
        // truncation would have made every datetime column reach the date validator - producing a
        // silently truncated time component rather than an error.
        if (owner == "none")
        {
            Assert.Empty(claims);
        }
        else
        {
            Assert.Equal([owner], claims);
        }
    }

    [Theory]
    [InlineData("DATE")]
    [InlineData("Date")]
    [InlineData("DATETIME")]
    [InlineData("CHAR")]
    [InlineData("Long")]
    public void ColumnTypeMatchingIsCaseSensitiveSoAnUppercaseTypeMatchesNothing(string columnType)
    {
        // MEASURED, AND WORTH KNOWING BECAUSE IT LOOKS LIKE A BUG.
        //
        // The legacy compares column-type prefixes exactly, and DataWindow column types are lower case
        // as the runtime reports them - so case sensitivity costs nothing in practice and matches the
        // oracle. Making the comparison case-insensitive would be a widening with no evidence behind it,
        // and would risk claiming a type the legacy would have left unclaimed.
        //
        // The consequence is recorded rather than smoothed over: an uppercase column type falls through
        // every validator and reaches the legacy default arm, which coerces by prefix and then forcibly
        // returns 2.
        Assert.False(DateValidator.MatchesColumnType(columnType));
        Assert.False(DateTimeValidator.MatchesColumnType(columnType));
        Assert.False(TimeValidator.MatchesColumnType(columnType));
        Assert.False(StringValidator.OwnsColType(columnType));
        Assert.False(NumberValidator.IsDecimalCoercionColumnType(columnType));
        Assert.False(NumberValidator.IsLongCoercionColumnType(columnType));
    }

    [Fact]
    public void ATimestampColumnIsOwnedByNoValidatorBecauseItsPrefixIsTimesNotTime()
    {
        // THE SHARPEST CONSEQUENCE OF THE FIVE-CHARACTER RULE.
        //
        // "timestamp" truncates to "times", which is not "time" - so a timestamp column is claimed by
        // NOTHING. That reads like an oversight and is faithful: the legacy switch tests for the
        // five-character token and a timestamp column falls through to its default arm.
        //
        // Recorded explicitly because it is precisely the case someone would "fix" by loosening the time
        // validator's match to a four-character prefix - which would then also claim "time" columns
        // twice and change dispatch for every one of them.
        Assert.Equal("times", DateValidator.TruncateColumnType("timestamp"));
        Assert.False(TimeValidator.MatchesColumnType("timestamp"));
        Assert.False(DateTimeValidator.MatchesColumnType("timestamp"));
    }

    [Fact]
    public void TheStringValidatorOwnsOnlyTheTwoCharTokensAndNotALongerWord()
    {
        // "char" AND "char(" ONLY.
        //
        // A parameterised type arrives as "char(20)", which truncates to "char(" - so BOTH spellings are
        // needed. "chara" is a different type and is correctly not claimed, which is what stops the
        // string validator swallowing any type that happens to begin with those four letters.
        Assert.Equal("char", StringValidator.ColTypeTokenChar);
        Assert.Equal("char(", StringValidator.ColTypeTokenParameterizedChar);

        Assert.True(StringValidator.OwnsColType("char"));
        Assert.True(StringValidator.OwnsColType("char(20)"));
        Assert.True(StringValidator.OwnsColType("char(200)"));
        Assert.False(StringValidator.OwnsColType("chara"));
        Assert.False(StringValidator.OwnsColType("character"));
    }

    [Fact]
    public void TheNumberValidatorSplitsDecimalFromIntegralCoercion()
    {
        // FOUR DECIMAL TOKENS AND TWO INTEGRAL ONES, AND THE SPLIT IS SEMANTIC.
        //
        // decim/decimal(n), real and numbe coerce through `decimal`; long and ulong coerce through
        // `long`. The distinction matters for the primary fixture: its salary column is `decimal(2)` in
        // the DataWindow against a `REAL` database column - a type mismatch preserved as a defect - and
        // coercing it through a floating-point type would introduce representation error into a
        // concurrency comparison that compares original values exactly.
        foreach (string token in (string[])["decim", "decimal(2)", "real", "numbe"])
        {
            Assert.True(NumberValidator.IsDecimalCoercionColumnType(token), token);
            Assert.False(NumberValidator.IsLongCoercionColumnType(token), token);
        }

        foreach (string token in (string[])["long", "ulong"])
        {
            Assert.True(NumberValidator.IsLongCoercionColumnType(token), token);
            Assert.False(NumberValidator.IsDecimalCoercionColumnType(token), token);
        }
    }

    // ==============================================================================================
    //  THE FOUR-WAY NULL AND BLANK INCONSISTENCY - the most important thing in this file
    // ==============================================================================================

    [Fact]
    public void TheFiveValidatorsHandleNullAndBlankInputFourDifferentWays()
    {
        // MEASURED, AND IT IS A LEGACY INCONSISTENCY PRESERVED UNDER C-B RATHER THAN A DEFECT TO FIX.
        //
        // A single table is the clearest way to state it, because no two of these five agree completely:
        //
        //   validator    null input              empty input             blank ("  ") input
        //   ---------    ---------------------   ---------------------   ---------------------
        //   Date         OK, null                OK, null                OK, null
        //   DateTime     E_INVALID_DATA, null    E_INVALID_DATA, null    E_INVALID_DATA, null
        //   Time         OK, null                E_INVALID_DATA, null    (as empty)
        //   String       OK, null                OK, EMPTY STRING        OK, blank preserved
        //   Number       OK, null                E_INVALID_DATA, null    (as empty)
        //
        // Three distinct null-handling behaviours and four distinct empty-handling behaviours. Harmonising
        // them would be exactly the silent correction the requirements forbid - and it would change
        // observable behaviour at the item-change boundary, because the return code decides whether the
        // edit is accepted, rejected, or restored.
        //
        // Note especially that DateTime rejects a NULL outright while Date accepts it as a null value.
        // Two validators for adjacent types, opposite answers to the same input.
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce(null, out DateOnly? date));
        Assert.Null(date);

        Assert.Equal(RetCode.E_INVALID_DATA, DateTimeValidator.TryCoerce(null, out DateTime? dateTime));
        Assert.Null(dateTime);

        Assert.Equal(RetCode.OK, TimeValidator.TryCoerce(null, out TimeOnly? time));
        Assert.Null(time);

        Assert.Equal(RetCode.OK, StringValidator.TryCoerce(null, out string? text));
        Assert.Null(text);

        Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToDecimal(null, out decimal? number));
        Assert.Null(number);
    }

    [Fact]
    public void EmptyInputIsAcceptedByTwoValidatorsAndRejectedByThree()
    {
        // THE EMPTY-STRING HALF OF THE SAME INCONSISTENCY.
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce(string.Empty, out DateOnly? date));
        Assert.Null(date);

        // AND THE STRING VALIDATOR PRESERVES EMPTY AS EMPTY RATHER THAN CONVERTING IT TO NULL.
        //
        // That distinction is load-bearing for the DataWindow buffer model, which tracks an
        // `emptystringisnull` flag per expression precisely because the two are NOT the same value - and
        // whether they are treated alike is a per-column setting rather than a global rule.
        Assert.Equal(RetCode.OK, StringValidator.TryCoerce(string.Empty, out string? text));
        Assert.Equal(string.Empty, text);
        Assert.NotNull(text);

        Assert.Equal(RetCode.E_INVALID_DATA, DateTimeValidator.TryCoerce(string.Empty, out DateTime? dt));
        Assert.Null(dt);

        Assert.Equal(RetCode.E_INVALID_DATA, TimeValidator.TryCoerce(string.Empty, out TimeOnly? time));
        Assert.Null(time);

        Assert.Equal(
            RetCode.E_INVALID_DATA,
            NumberValidator.TryCoerceToDecimal(string.Empty, out decimal? number));
        Assert.Null(number);
    }

    [Fact]
    public void EveryFailedCoercionReportsTheSameLegacyReturnCode()
    {
        // E_INVALID_DATA IS -9 [ws_objects/pfw.shared.pbl.src/retcode.sru:L52].
        //
        // One code across all five, which is what lets the item-change protocol treat a validation
        // failure uniformly regardless of column type. The code is asserted against the KERNEL constant
        // rather than the literal -9, so the two transcriptions of the algebra stay tied together.
        Assert.Equal(-9L, RetCode.E_INVALID_DATA);

        Assert.Equal(RetCode.E_INVALID_DATA, DateValidator.TryCoerce("not a date", out _));
        Assert.Equal(RetCode.E_INVALID_DATA, DateTimeValidator.TryCoerce("not a datetime", out _));
        Assert.Equal(RetCode.E_INVALID_DATA, TimeValidator.TryCoerce("not a time", out _));
        Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToDecimal("not a number", out _));
        Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToLong("not a number", out _));
    }

    [Fact]
    public void TheStringValidatorNeverFailsBecauseAnyTextIsAValidString()
    {
        // NO INPUT REACHES A FAILURE PATH - and this is a semantic fact rather than a missing check.
        //
        // Every one of these is a legal string value, including the ones that are illegal for every other
        // type. That is why the string validator has no E_INVALID_DATA arm at all.
        foreach (string? candidate in (string?[])
            [null, "", "   ", "abc", "not a date", "'; DROP TABLE COMPANY; --", "\t\n", new string('x', 10_000)])
        {
            Assert.Equal(RetCode.OK, StringValidator.TryCoerce(candidate, out string? value));

            // AND THE VALUE PASSES THROUGH UNTOUCHED - no trimming, no normalisation, no truncation.
            //
            // Not trimming matters: a leading space is data in a char column, and the legacy does not
            // strip it. Silently trimming would change stored values.
            Assert.Equal(candidate, value);
        }
    }

    // ==============================================================================================
    //  Coerce VERSUS TryCoerce - and the one place they disagree
    // ==============================================================================================

    [Fact]
    public void TheTimeValidatorsTwoEntryPointsDisagreeOnUncoercibleInput()
    {
        // MEASURED, AND THE MOST SURPRISING SINGLE BEHAVIOUR IN THESE SIX FILES.
        //
        // `TryCoerce` reports E_INVALID_DATA with a null out-value. `Coerce` returns a NON-NULL
        // `TimeOnly.MinValue` - the declared `UncoercibleFallback` - for the very same input. So the two
        // entry points give a caller opposite impressions: one says "this failed", the other hands back
        // midnight.
        //
        // That is not a defect in the port: the fallback is DECLARED as a named public field rather than
        // buried as a literal, precisely so a characterization author can pin it. A caller that needs to
        // distinguish "midnight" from "unparseable" must use `TryCoerce`, and this test is the standing
        // statement of why.
        Assert.Equal(TimeOnly.MinValue, TimeValidator.UncoercibleFallback);

        foreach (string bad in (string[])["", "25:00", "not a time", "99:99:99"])
        {
            Assert.Equal(RetCode.E_INVALID_DATA, TimeValidator.TryCoerce(bad, out TimeOnly? strict));
            Assert.Null(strict);

            TimeOnly? lenient = TimeValidator.Coerce(bad);
            Assert.NotNull(lenient);
            Assert.Equal(TimeValidator.UncoercibleFallback, lenient);
        }

        // A NULL INPUT IS THE EXCEPTION: both entry points agree it is a null rather than a fallback.
        Assert.Null(TimeValidator.Coerce(null));
        Assert.Equal(RetCode.OK, TimeValidator.TryCoerce(null, out TimeOnly? nullResult));
        Assert.Null(nullResult);
    }

    [Fact]
    public void TheOtherValidatorsTwoEntryPointsAgreeOnUncoercibleInput()
    {
        // THE CONTRAST THAT MAKES THE TIME VALIDATOR'S DIVERGENCE VISIBLE.
        //
        // Date, DateTime and Number all return null from `Coerce` for uncoercible input, matching their
        // `TryCoerce` out-value. Only Time substitutes a sentinel - so the divergence is specific rather
        // than a house style, and asserting the others' agreement is what establishes that.
        Assert.Null(DateValidator.Coerce("not a date"));
        Assert.Null(DateTimeValidator.Coerce("not a datetime"));
        Assert.Null(NumberValidator.CoerceToDecimal("not a number"));
        Assert.Null(NumberValidator.CoerceToLong("not a number"));
    }

    // ==============================================================================================
    //  PARSING MATRICES
    // ==============================================================================================

    [Theory]
    [InlineData("2024-03-15", 2024, 3, 15)]
    [InlineData("2024-01-01", 2024, 1, 1)]
    [InlineData("2024-12-31", 2024, 12, 31)]
    [InlineData("2024-02-29", 2024, 2, 29)]   // a real leap day
    public void AWellFormedDateIsCoerced(string data, int year, int month, int day)
    {
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce(data, out DateOnly? value));
        Assert.Equal(new DateOnly(year, month, day), value);
    }

    [Theory]
    [InlineData("2024-13-45")]        // impossible month and day
    [InlineData("2023-02-29")]        // 2023 is not a leap year
    [InlineData("20240315")]          // compact, no separators
    [InlineData("15-03-2024")]        // day-first with dashes
    [InlineData("2024-03-15 10:00")]  // carries a time component, so it is not a date
    [InlineData("not a date")]
    public void AMalformedDateIsRejected(string data)
    {
        Assert.Equal(RetCode.E_INVALID_DATA, DateValidator.TryCoerce(data, out DateOnly? value));
        Assert.Null(value);
    }

    [Theory]
    // MEASURED: THE COERCION IS A TWO-STAGE PARSE, NOT A SINGLE EXACT MATCH.
    //
    // Stage one is `DateOnly.TryParseExact` against the canonical `yyyy-MM-dd`. Stage two - reached only
    // when the first fails - is `DateOnly.TryParse` with `CultureInfo.InvariantCulture`, which accepts a
    // much wider grammar. So these all succeed, and a test written from the canonical format alone would
    // have asserted every one of them as a rejection.
    [InlineData("2024-03-15", 2024, 3, 15)]   // canonical, stage one
    [InlineData("2024/03/15", 2024, 3, 15)]   // slash separators
    [InlineData("2024.03.15", 2024, 3, 15)]   // dot separators
    [InlineData("2024-3-5", 2024, 3, 5)]      // unpadded month and day
    [InlineData("Mar 15 2024", 2024, 3, 15)]  // month name
    [InlineData("  2024-03-15  ", 2024, 3, 15)]   // surrounding whitespace
    public void TheInvariantCultureFallbackAcceptsAWiderGrammarThanTheCanonicalFormat(
        string data, int year, int month, int day)
    {
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce(data, out DateOnly? value));
        Assert.Equal(new DateOnly(year, month, day), value);
    }

    [Fact]
    public void TheFallbackIsMonthFirstSoADayFirstDateIsRejectedWhileAMonthFirstOneIsAccepted()
    {
        // MEASURED, AND THE MOST CONSEQUENTIAL PAIR IN THIS FILE - THE TWO ANSWERS ARE OPPOSITE.
        //
        // `CultureInfo.InvariantCulture` uses the MONTH-FIRST short date pattern, so:
        //
        //   "03/15/2024" -> accepted as 15 March 2024      (month-first, unambiguous because 15 > 12)
        //   "15/03/2024" -> REJECTED                        (day-first, month 15 does not exist)
        //
        // The asymmetry is not a validation rule - it is a side effect of which culture the fallback uses,
        // and it has a genuine hazard behind it. For a date where BOTH components are 12 or below,
        // "03/04/2024" parses successfully as 4 March and never as 3 April. A day-first source system
        // feeding that value in would have its dates silently transposed, with no failure anywhere.
        //
        // This is characterized rather than changed. Narrowing the fallback to the canonical format only
        // would reject inputs the current implementation accepts, and widening it to a day-first culture
        // would transpose the other direction - so the safe action is to PIN the current behaviour and
        // record the hazard, which is what this test does.
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce("03/15/2024", out DateOnly? monthFirst));
        Assert.Equal(new DateOnly(2024, 3, 15), monthFirst);

        Assert.Equal(RetCode.E_INVALID_DATA, DateValidator.TryCoerce("15/03/2024", out DateOnly? dayFirst));
        Assert.Null(dayFirst);

        // THE AMBIGUOUS CASE, PINNED EXPLICITLY: both components are below 13, so it parses - month-first.
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce("03/04/2024", out DateOnly? ambiguous));
        Assert.Equal(new DateOnly(2024, 3, 4), ambiguous);
        Assert.NotEqual(new DateOnly(2024, 4, 3), ambiguous);
    }

    [Theory]
    [InlineData("2024-03-15 13:45:59", 2024, 3, 15, 13, 45, 59)]

    // A DATE-ONLY INPUT IS ACCEPTED AND ITS TIME COMPONENT DEFAULTS TO MIDNIGHT - measured. A caller
    // supplying only a date to a datetime column gets a valid value rather than a rejection.
    [InlineData("2024-03-15", 2024, 3, 15, 0, 0, 0)]
    public void AWellFormedDateTimeIsCoerced(
        string data, int year, int month, int day, int hour, int minute, int second)
    {
        Assert.Equal(RetCode.OK, DateTimeValidator.TryCoerce(data, out DateTime? value));
        Assert.Equal(new DateTime(year, month, day, hour, minute, second), value);
    }

    [Theory]
    [InlineData("13:45:59", 13, 45, 59)]

    // A SECONDS-LESS INPUT IS ACCEPTED with seconds defaulting to zero.
    [InlineData("13:45", 13, 45, 0)]
    [InlineData("00:00:00", 0, 0, 0)]
    [InlineData("23:59:59", 23, 59, 59)]
    public void AWellFormedTimeIsCoerced(string data, int hour, int minute, int second)
    {
        Assert.Equal(RetCode.OK, TimeValidator.TryCoerce(data, out TimeOnly? value));
        Assert.Equal(new TimeOnly(hour, minute, second), value);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-42", -42)]
    [InlineData("0", 0)]
    [InlineData("4.5", 4.5)]
    [InlineData("-4.5", -4.5)]

    // EXPONENTIAL NOTATION IS ACCEPTED, because the coercion uses NumberStyles.Float.
    [InlineData("1e3", 1000)]
    [InlineData("1E3", 1000)]

    // SURROUNDING WHITESPACE IS ACCEPTED AND TRIMMED BY THE PARSE.
    [InlineData(" 7 ", 7)]
    public void AWellFormedDecimalIsCoerced(string data, double expected)
    {
        Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToDecimal(data, out decimal? value));
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    // THOUSANDS SEPARATORS ARE REJECTED - NumberStyles.Float does not include AllowThousands.
    [InlineData("1,234")]
    [InlineData("0x10")]
    [InlineData("42abc")]
    [InlineData("--42")]
    [InlineData("bad")]
    public void AMalformedNumberIsRejectedForBothTargetTypes(string data)
    {
        // REJECTING A THOUSANDS SEPARATOR IS THE RIGHT CALL AND WORTH PINNING.
        //
        // Accepting "1,234" would be locale-dependent: in many cultures the comma is a DECIMAL separator,
        // so the same string would mean 1234 in one locale and 1.234 in another. A coercion that changes
        // a stored value depending on server culture is a data-corruption vector, and the fixed invariant
        // parse avoids it.
        Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToDecimal(data, out decimal? dec));
        Assert.Null(dec);

        Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToLong(data, out long? lng));
        Assert.Null(lng);
    }

    [Theory]
    [InlineData("4.5")]
    [InlineData("1e3")]
    [InlineData("-0.1")]
    public void AFractionalValueIsRejectedByTheIntegralCoercionButAcceptedByTheDecimalOne(string data)
    {
        // THE TWO NUMERIC TARGETS HAVE DIFFERENT ACCEPTED GRAMMARS, WHICH IS THE POINT OF HAVING BOTH.
        //
        // `NumberStyles.Integer` admits no decimal point and no exponent, so a fractional value reaching a
        // `long` column is a validation FAILURE rather than a silent truncation. Truncating would lose
        // data without telling anyone - and for an integral column that data loss is invisible in the
        // result.
        Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToDecimal(data, out decimal? dec));
        Assert.NotNull(dec);

        Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToLong(data, out long? lng));
        Assert.Null(lng);
    }

    // ==============================================================================================
    //  CANONICAL FORMATTING
    // ==============================================================================================

    [Fact]
    public void TheCanonicalFormatsArePaddedAndOrderedMostSignificantFirst()
    {
        Assert.Equal("yyyy-MM-dd", DateValidator.CanonicalValueFormat);
        Assert.Equal("yyyy-MM-dd HH:mm:ss", DateTimeValidator.ExpressionValueFormat);
        Assert.Equal("HH:mm:ss", TimeValidator.CanonicalFormat);

        // ZERO-PADDED AND 24-HOUR, WHICH MATTERS FOR MORE THAN APPEARANCE.
        //
        // A single-digit month rendering as "3" rather than "03" would break the round trip, because the
        // parse expects the canonical form - so formatting and parsing would disagree and a value could
        // not survive a write-then-read. `HH` rather than `hh` avoids the same problem for the afternoon.
        Assert.Equal("2024-03-05", DateValidator.FormatValue(new DateOnly(2024, 3, 5)));
        Assert.Equal(
            "2024-03-05 07:08:09",
            DateTimeValidator.FormatExpressionValue(new DateTime(2024, 3, 5, 7, 8, 9)));
        Assert.Equal("07:08:09", TimeValidator.Format(new TimeOnly(7, 8, 9)));
    }

    [Fact]
    public void AFormattedValueParsesBackToItselfForAllThreeTemporalTypes()
    {
        // THE ROUND TRIP IS THE PROPERTY THAT MATTERS, AND IT IS ASSERTED RATHER THAN ASSUMED.
        //
        // Format and parse are separate code paths with separate format strings, so nothing structural
        // guarantees they agree. A value written into an expression and later read back must be the same
        // value, or a computed column would drift every time it was persisted.
        var date = new DateOnly(2024, 3, 5);
        Assert.Equal(RetCode.OK, DateValidator.TryCoerce(DateValidator.FormatValue(date), out DateOnly? d));
        Assert.Equal(date, d);

        var dateTime = new DateTime(2024, 3, 5, 7, 8, 9);
        Assert.Equal(
            RetCode.OK,
            DateTimeValidator.TryCoerce(DateTimeValidator.FormatExpressionValue(dateTime), out DateTime? dt));
        Assert.Equal(dateTime, dt);

        var time = new TimeOnly(7, 8, 9);
        Assert.Equal(RetCode.OK, TimeValidator.TryCoerce(TimeValidator.Format(time), out TimeOnly? t));
        Assert.Equal(time, t);
    }

    // ==============================================================================================
    //  ValueToExpression - INCLUDING THE PRESERVED UNESCAPED-QUOTE DEFECT
    // ==============================================================================================

    [Fact]
    public void ANullOfAnyTypeConvertsToItsOwnTypedNullSentinel()
    {
        // EACH TYPE GETS ITS OWN SENTINEL, because the expression language is typed and a null date is
        // not interchangeable with a null number.
        Assert.Equal("dwNvlNumber()", ValueToExpression.Convert((decimal?)null));
        Assert.Equal("dwNvlNumber()", ValueToExpression.Convert((short?)null));
        Assert.Equal("dwNvlNumber()", ValueToExpression.Convert((long?)null));
        Assert.Equal("dwNvlNumber()", ValueToExpression.Convert((double?)null));
        Assert.Equal("dwNvlNumber()", ValueToExpression.Convert((float?)null));
        Assert.Equal("dwNvlString()", ValueToExpression.Convert((string?)null));
        Assert.Equal("dwNvlDateTime()", ValueToExpression.Convert((DateTime?)null));
        Assert.Equal("dwNvlDate()", ValueToExpression.Convert((DateOnly?)null));
        Assert.Equal("dwNvlTime()", ValueToExpression.Convert((TimeOnly?)null));
    }

    [Fact]
    public void ANumericValueIsEmittedBareWithoutQuotesAndKeepsItsDecimalScale()
    {
        Assert.Equal("7", ValueToExpression.Convert((short?)7));
        Assert.Equal("-7", ValueToExpression.Convert((long?)-7));
        Assert.Equal("1.5", ValueToExpression.Convert((double?)1.5));
        Assert.Equal("1.5", ValueToExpression.Convert((float?)1.5f));

        // THE TRAILING ZERO SURVIVES, WHICH IS A REAL PROPERTY OF `decimal` AND NOT AN ACCIDENT.
        //
        // `4.50m` carries a SCALE of two, and rendering it as "4.5" would lose that. For the primary
        // fixture's `decimal(2)` salary column the scale is the column's declared precision, so
        // preserving it keeps the emitted expression faithful to the stored value.
        Assert.Equal("4.50", ValueToExpression.Convert((decimal?)4.50m));
    }

    [Fact]
    public void ATemporalValueIsWrappedInItsTypedConstructorForm()
    {
        // THE EXPRESSION LANGUAGE HAS NO DATE LITERAL, so a value is emitted as a constructor call over a
        // canonical string. The prefixes are declared constants rather than inline literals precisely
        // because they are part of the emitted text.
        Assert.Equal(
            "DateTime('2024-03-05 07:08:09')",
            ValueToExpression.Convert((DateTime?)new DateTime(2024, 3, 5, 7, 8, 9)));
        Assert.Equal("Date('2024-03-05')", ValueToExpression.Convert((DateOnly?)new DateOnly(2024, 3, 5)));
        Assert.Equal("Time('07:08:09')", ValueToExpression.Convert((TimeOnly?)new TimeOnly(7, 8, 9)));
    }

    [Fact]
    public void AStringValueIsSingleQuotedAndEmptyStringIsQuotedRatherThanNulled()
    {
        Assert.Equal("'abc'", ValueToExpression.Convert("abc"));

        // AN EMPTY STRING BECOMES `''`, NOT `dwNvlString()`.
        //
        // Empty and null are different values, and only null gets the sentinel. Conflating them here
        // would make the emitted expression disagree with the buffer model's own per-column
        // `emptystringisnull` flag, which exists precisely because whether they are equivalent is a
        // configurable property rather than a fixed one.
        Assert.Equal("''", ValueToExpression.Convert(string.Empty));
        Assert.Equal("' '", ValueToExpression.Convert(" "));
    }

    [Fact]
    public void AnEmbeddedApostropheIsNotEscapedAndProducesAMalformedExpression()
    {
        // A PRESERVED LEGACY DEFECT, AND AN INJECTION VECTOR. IT IS REPRODUCED, NOT FIXED.
        //
        // `dwvaluetoexp.srf` wraps a string in single quotes and performs NO escaping, so a value
        // containing an apostrophe produces `'it's'` - which is not a well-formed expression. The legacy
        // behaves this way and C-B forbids correcting it, so the port behaves this way too.
        //
        // The exposure is real and belongs on the record rather than being quietly patched: this is the
        // same class of defect as the drop-down search service interpolating user data unescaped into a
        // filter expression [n_cst_dwsvc_dropdownsearch.sru:L323], and as `DisableBind=1` meaning the
        // runtime uses literal interpolation instead of bind variables. Each site is documented as a
        // known legacy defect in docs/SECRETS.md and docs/PARITY.md.
        //
        // Escaping here would change the OBSERVABLE emitted expression, which characterization compares
        // byte for byte - so a "fix" would show up as a parity failure against the oracle, correctly.
        Assert.Equal("'it's'", ValueToExpression.Convert("it's"));

        // AND THE EXPRESSION-BREAKING PAYLOAD FORM, recorded for the same reason.
        Assert.Equal("'a' or '1'='1'", ValueToExpression.Convert("a' or '1'='1"));
    }

    [Fact]
    public void ADoubleQuoteAndATildeArePassedThroughUnchanged()
    {
        // NEITHER IS TREATED AS SPECIAL, THOUGH BOTH ARE SPECIAL IN POWERSCRIPT.
        //
        // A double quote is an alternative string delimiter and a tilde is PowerScript's ESCAPE
        // character - so `~` inside an expression is meaningful to the runtime. Neither is escaped here,
        // which is consistent with the apostrophe finding above and equally faithful.
        //
        // Recorded because a reader who knew PowerScript would expect the tilde to be doubled, and its
        // absence is a deliberate non-change rather than an omission.
        Assert.Equal("'a\"b'", ValueToExpression.Convert("a\"b"));
        Assert.Equal("'a~b'", ValueToExpression.Convert("a~b"));
    }

    [Fact]
    public void EveryConversionIsCultureIndependent()
    {
        // THE DECIMAL SEPARATOR MUST NOT FOLLOW THE SERVER'S CULTURE.
        //
        // Under a comma-decimal culture, a culture-sensitive conversion would emit "1,5" for 1.5 - which
        // the expression parser would read as two arguments, or reject. The same value would then produce
        // different expressions on differently-configured hosts, so a characterization recording captured
        // on one machine could never be compared against another.
        //
        // Exercised under a real comma-decimal culture rather than asserted from reading the code, with
        // the culture restored in a finally so the probe cannot leak into other tests.
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.Equal("1.5", ValueToExpression.Convert((double?)1.5));
            Assert.Equal("4.50", ValueToExpression.Convert((decimal?)4.50m));
            Assert.Equal("1.5", ValueToExpression.Convert((float?)1.5f));

            // AND THE TEMPORAL FORMS TOO - a German culture would otherwise render "05.03.2024".
            Assert.Equal(
                "Date('2024-03-05')",
                ValueToExpression.Convert((DateOnly?)new DateOnly(2024, 3, 5)));
            Assert.Equal(
                "DateTime('2024-03-05 07:08:09')",
                ValueToExpression.Convert((DateTime?)new DateTime(2024, 3, 5, 7, 8, 9)));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CoercionIsAlsoCultureIndependent()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            // "4.5" MUST STILL BE FOUR AND A HALF, not forty-five.
            //
            // Under de-DE the period is a THOUSANDS separator, so a culture-sensitive parse would read
            // "4.5" as 45 - a tenfold error that succeeds silently. This is the parsing counterpart of the
            // formatting test above, and the more dangerous direction of the two.
            Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToDecimal("4.5", out decimal? value));
            Assert.Equal(4.5m, value);

            Assert.Equal(RetCode.OK, DateValidator.TryCoerce("2024-03-05", out DateOnly? date));
            Assert.Equal(new DateOnly(2024, 3, 5), date);

            // AND THE COMMA FORM IS STILL REJECTED even in a culture where it is the decimal separator.
            Assert.Equal(RetCode.E_INVALID_DATA, NumberValidator.TryCoerceToDecimal("4,5", out _));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    // ==============================================================================================
    //  THE NON-REPORTING `Coerce` ENTRY POINTS, EXERCISED DIRECTLY
    // ==============================================================================================

    [Fact]
    public void TheNonReportingNumericCoercionsReturnNullForANullInput()
    {
        // THESE ARE THE ENTRY POINTS THE ITEM-CHANGE PROTOCOL ACTUALLY CALLS.
        //
        // `CoerceToDecimal` and `CoerceToLong` are TOTAL - they never throw and never report a failure,
        // reproducing `SetItem(row, Long(dwo.ID), Long(data))` at se_cst_dw.sru:L235,L237, where the
        // legacy coercion reports nothing at all.
        //
        // The distinction from the `TryCoerce*` pair is documented in the source and is important: routing
        // the item-change protocol through the reporting variants and then ACTING on the returned code
        // would add an error path the legacy does not have, changing observable behaviour on exactly the
        // branch the port exists to preserve. So both shapes exist, and only the silent one is on the
        // legacy path.
        Assert.Null(NumberValidator.CoerceToDecimal(null));
        Assert.Null(NumberValidator.CoerceToLong(null));

        // AND A NULL IS A SUCCESS RATHER THAN INVALID DATA on the reporting variants too - which is what
        // lets the silent and reporting forms agree about null while disagreeing about empty.
        Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToLong(null, out long? value));
        Assert.Null(value);
    }

    [Fact]
    public void TheNonReportingNumericCoercionsParseTheSameGrammarAsTheReportingOnes()
    {
        // SAME GRAMMAR, DIFFERENT REPORTING. The two forms must not diverge on what they ACCEPT, only on
        // what they say about a rejection - otherwise the legacy path and the diagnostic path would
        // disagree about whether a value is valid.
        Assert.Equal(42m, NumberValidator.CoerceToDecimal("42"));
        Assert.Equal(4.5m, NumberValidator.CoerceToDecimal("4.5"));
        Assert.Equal(42L, NumberValidator.CoerceToLong("42"));

        // AND A FRACTIONAL VALUE IS STILL NOT A LONG - the silent form returns null rather than
        // truncating, so no data is lost invisibly.
        Assert.Null(NumberValidator.CoerceToLong("4.5"));

        Assert.Equal(RetCode.OK, NumberValidator.TryCoerceToLong("42", out long? reported));
        Assert.Equal(42L, reported);
    }

    [Fact]
    public void TheNonReportingTemporalCoercionsSucceedOnCanonicalInput()
    {
        // THE SUCCESS PATH OF THE SILENT ENTRY POINTS, which the disagreement test above only exercises
        // on FAILING input.
        Assert.Equal(new DateOnly(2024, 3, 15), DateValidator.Coerce("2024-03-15"));
        Assert.Equal(new DateTime(2024, 3, 15, 13, 45, 59), DateTimeValidator.Coerce("2024-03-15 13:45:59"));
        Assert.Equal(new TimeOnly(13, 45, 59), TimeValidator.Coerce("13:45:59"));

        // AND A NULL INPUT IS A NULL RESULT rather than the uncoercible fallback - the one input on which
        // the time validator's two entry points agree.
        Assert.Null(DateValidator.Coerce(null));
        Assert.Null(TimeValidator.Coerce(null));
    }

    [Fact]
    public void TheTimeValidatorPublishesItsSingleColumnTypeTokenAsASet()
    {
        // A SET OF EXACTLY ONE, AND THE ARITY ITSELF MIRRORS THE ORACLE.
        //
        // se_cst_dw.sru:L242 lists ONE literal in the time arm, unlike its sibling arms at :L232, :L234
        // and :L236 which list two or three each. Publishing a one-element set rather than a bare string
        // keeps the shape uniform across the arms while recording that this arm is genuinely singular -
        // so a future reader can see the asymmetry is the legacy's rather than an incomplete port.
        Assert.Equal("time", TimeValidator.ColumnTypeToken);

        string token = Assert.Single(TimeValidator.ColumnTypeTokens);
        Assert.Equal("time", token);

        // ORDINAL MEMBERSHIP, consistent with the case-sensitive matching asserted above.
        Assert.Contains("time", TimeValidator.ColumnTypeTokens);
        Assert.DoesNotContain("TIME", TimeValidator.ColumnTypeTokens);
    }

    [Fact]
    public void EveryValidatorIsAStaticGatewayRatherThanAnInjectableRuleObject()
    {
        // STATIC, DELIBERATELY - THESE ARE PORTED GLOBAL FUNCTIONS, NOT SERVICES.
        //
        // The legacy `dwnvl*.srf` objects are `*.srf` global functions, which map to static methods on a
        // role-named static class per the object-kind mapping. Making them injectable would imply a
        // substitutable policy, and there is no policy here: the coercion rules are fixed by the oracle
        // and a caller must not be able to replace them.
        //
        // It is also why no validation FRAMEWORK was introduced: a rule set would suggest these rules are
        // configurable, and the one property that matters is that they are not.
        foreach (Type type in (Type[])
        [
            typeof(DateValidator), typeof(DateTimeValidator), typeof(NumberValidator),
            typeof(StringValidator), typeof(TimeValidator), typeof(ValueToExpression),
        ])
        {
            Assert.True(type.IsAbstract && type.IsSealed, $"{type.Name} should be a static class.");
        }
    }
}
