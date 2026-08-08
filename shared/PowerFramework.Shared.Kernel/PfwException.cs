// ==============================================================================================
//  PfwException - the PowerFramework framework exception type
//  --------------------------------------------------------------------------------------------
//  PORTED FROM    ws_objects/pfw.shared.pbl.src/pfwexception.sru (35 lines)
//  ORACLE STATUS  That .sru is the ONLY specification for this type, and it is READ ONLY: it is
//                 the behavioural oracle for parity testing, never an edit target. Every
//                 behaviour reproduced below therefore carries the pfwexception.sru line
//                 locator it was taken from.
//
//  THE WHOLE OF THE LEGACY TYPE, ENUMERATED
//  --------------------------------------------------------------------------------------------
//  The legacy object is very small, and knowing exactly how small it is matters: it means every
//  member in this file is either a faithful re-expression of one of the lines below or an
//  explicitly annotated .NET convention addition. There is no third category.
//
//      pfwexception.sru:L3, L7    global type pfwexception from runtimeerror
//      pfwexception.sru:L8        string  objectname  = "pfwexception"
//      pfwexception.sru:L9        string  class       = "pfwexception"
//      pfwexception.sru:L10       string  routinename = "create"
//      pfwexception.sru:L11       integer line        = -1
//      pfwexception.sru:L13       global pfwexception pfwexception
//      pfwexception.sru:L15-L17   type variables ... end variables   EMPTY - adds no state
//      pfwexception.sru:L20, L23  public subroutine setmessage (string newmessage)
//                                     super::SetMessage("PowerFramework Runtime Error~n" + newMessage)
//      pfwexception.sru:L26-L34   on create / on destroy - TriggerEvent only, nothing to port
//
//  The pfw.shared library declares zero PBNI class bindings and zero external function
//  prototypes, so this is a pure logic port: there is no pfw.dll or pfwx.dll entry point behind
//  it and nothing here is a substitution for a closed-source binary.
//
//  DECISION 1 - BASE TYPE: runtimeerror BECOMES System.Exception
//  --------------------------------------------------------------------------------------------
//  The legacy ancestor at pfwexception.sru:L7 is PowerBuilder's `runtimeerror`, the base for
//  recoverable runtime faults raised by application code. System.Exception is the closest BCL
//  equivalent and is the base chosen here. The two bases that were considered and rejected:
//
//      System.SystemException     REJECTED. That base is reserved by convention for faults the
//                                 runtime itself raises. This type is only ever raised
//                                 deliberately by framework code - the sole live legacy call
//                                 site is pfwThrowException("Invalid method") at
//                                 ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:860
//                                 - so deriving from it would misrepresent the fault's origin
//                                 to every catch handler and every diagnostic tool.
//      System.ApplicationException REJECTED. It is the historical marker for
//                                 application-originated faults, but the .NET design guidelines
//                                 have deprecated deriving from it because it adds no member and
//                                 no behaviour, and no BCL or framework code branches on it.
//                                 Choosing it would add a layer that means nothing at runtime.
//
//  Exception is therefore the accurate base: it carries the message, the inner-exception chain
//  and the stack trace that `runtimeerror` carries, and nothing more.
//
//  DECISION 2 - THE GLOBAL AUTO-INSTANCE AT L13 IS DELIBERATELY NOT REPRODUCED
//  --------------------------------------------------------------------------------------------
//  pfwexception.sru:L13 declares `global pfwexception pfwexception`: a global auto-instantiated
//  variable whose name shadows its own type name. That is legal only because PowerBuilder has a
//  single flat global namespace with no import statements, where symbol resolution follows the
//  ordering of the library list in the target file. It is the same collision the plan records
//  for `global n_sql n_sql`, and the resolution is the same: the C# TYPE keeps the descriptive
//  name and the shared global instance is not recreated. Concretely, this file declares NO
//  static instance, NO singleton, NO `Instance` and NO `Current` member. A process-wide shared
//  exception instance would be an outright defect in .NET - its message, inner exception and
//  stack trace would be mutated across unrelated threads and unrelated faults - so reproducing
//  it would import a hazard the legacy's threading model happened to tolerate.
//
//  DECISION 3 - THE MESSAGE SEPARATOR IS AN EXPLICIT LF, NEVER Environment.NewLine
//  --------------------------------------------------------------------------------------------
//  In the decoration at pfwexception.sru:L23 the escape `~n` is PowerScript for a SINGLE line
//  feed, U+000A / 0x0A. It is not a carriage-return / line-feed pair. The separator below is
//  therefore the literal "\n", and Environment.NewLine is deliberately NOT used: it resolves to
//  LF on Linux and to CRLF on Windows, which would make the decorated message platform
//  dependent. The legacy message is not platform dependent, so using it would be a behavioural
//  change disguised as portability. No carriage return is emitted anywhere in this decoration.
//
//  DECISION 4 - THE FOUR TYPE-LEVEL DEFAULTS BECOME READ-WRITE PROPERTIES
//  --------------------------------------------------------------------------------------------
//  pfwexception.sru:L8-L11 are instance fields carrying type-level DEFAULT values, which
//  PowerBuilder allows a caller to reassign. Read-write properties seeded with exactly those
//  legacy values are therefore the faithful representation: the defaults are observable and the
//  fields stay assignable. They are exposed rather than dropped because PowerBuilder's
//  `throwable` surface publishes them, and dropping them would narrow this type relative to the
//  object it replaces. A sweep of ws_objects/** found no legacy site that reads or writes any of
//  the four off a throwable, so they are pure surface fidelity, not the servant of a call site.
//
//  DECISION 5 - THE NAME STRING IS A SHARED CONSTANT, NOT A REPEATED LITERAL
//  --------------------------------------------------------------------------------------------
//  ws_objects/pfw.shared.pbl.src/pfwthrowexception.srf:L9 is a one-line body:
//  `ThrowException("pfwexception",text)`, and ws_objects/pfw.shared.pbl.src/throwexception.srf
//  :L26 resolves that string with `Create Using cls`. The lowercase literal "pfwexception" is
//  consequently a two-file contract between this type and ThrowException.cs. It is published
//  here once as TypeName so the two sides cannot drift; ThrowException.cs's name-string resolver
//  must map TypeName to typeof(PfwException).
//
//  CONSTRAINT SELF-AUDIT (no user rules exist for this project - review_rules returns exactly
//  "No user rules provided" - so the binding constraints are the plan's own inventory)
//  --------------------------------------------------------------------------------------------
//  C-A  This type is shared IMPLEMENTATION consumed in process through a ProjectReference. It is
//       not a cross-service channel: no serialization attribute, no ISerializable, no gRPC or
//       HTTP status, no reference to PowerFramework.Contracts, and nothing shaped for a wire.
//       A fault that must cross a service boundary is expressed with the structured error types
//       in the Contracts project instead.
//  C-B  Behaviour is replicated, never improved. The prefix and its single LF are byte exact;
//       the decoration is applied unconditionally on every SetMessage call with no idempotence
//       guard and no prefix stripping; the `line = -1` sentinel is preserved verbatim rather
//       than resolved to a real line number.
//  C-C  The legacy tree is read only. pfwexception.sru and its two consumers were read as the
//       specification and left untouched, and each ported behaviour cites its :L locator.
//  C-K  Every technology-specific decision is documented at its point of reproduction: the base
//       type substitution, the global auto-instance resolution, the explicit LF, the four
//       defaults, the name-string contract, and the mutable-message mechanism below.
//
//  DELIBERATELY ABSENT, EACH FOR A STATED REASON
//  --------------------------------------------------------------------------------------------
//    * [Serializable], ISerializable and a serialization constructor. Nothing serializes this
//      type; BinaryFormatter-based exception serialization is obsolete in modern .NET, and C-A
//      forbids shaping this type for a wire. Adding them speculatively would assert a contract
//      this refactor does not have.
//    * Any RetCode or Enums reference. This type carries no return code: the legacy object has
//      an EMPTY type-variables block at pfwexception.sru:L15-L17, and the return code algebra
//      travels separately through Predicates and RetCode.
//    * A per-file licence header. pfwexception.sru carries only a $PBExportHeader$ line and no
//      licence block, so there is nothing to carry over; the assembly-level copyright is set
//      once in the repository-root Directory.Build.props and the full notice lives in LICENSE
//      and NOTICE.
//    * A GetMessage() method mirroring PowerBuilder's throwable.GetMessage(). The overridden
//      Message property below IS that accessor, and adding both would be a redundant surface
//      whose names collide by analyzer convention.
//    * SCREAMING_SNAKE identifiers. The plan preserves the legacy constant spellings only in
//      RetCode.cs and Enums.cs, which are the only two files the repository-root .editorconfig
//      scopes its naming-analyzer suppressions to. With TreatWarningsAsErrors inherited from
//      Directory.Build.props such an identifier here would be a build ERROR, so the members
//      below use PascalCase identifiers carrying the legacy VALUES.
// ==============================================================================================

namespace PowerFramework.Shared.Kernel;

/// <summary>
/// The PowerFramework framework exception, ported from
/// <c>ws_objects/pfw.shared.pbl.src/pfwexception.sru</c>.
/// </summary>
/// <remarks>
/// <para>
/// Raised by the framework's own throw helpers rather than constructed ad hoc by callers: the
/// legacy entry point is <c>pfwThrowException(text)</c>, whose whole body delegates to
/// <c>ThrowException("pfwexception", text)</c> [pfwthrowexception.srf:L9], which creates the
/// object by name string and then calls <see cref="SetMessage"/> on it [throwexception.srf:L26-L27].
/// </para>
/// <para>
/// The one behaviour that distinguishes this type from a bare exception is message decoration:
/// every message it carries is prefixed with <see cref="MessagePrefix"/> followed by a single
/// line feed [pfwexception.sru:L23]. The type is left unsealed because the legacy object is not
/// final, but it introduces no new virtual member of its own - <see cref="Message"/> is an
/// override of an inherited one and <see cref="SetMessage"/> is deliberately non-virtual -
/// because nothing in the legacy tree derives from <c>pfwexception</c>, so the decoration
/// guarantee cannot be subverted by a descendant.
/// </para>
/// </remarks>
public class PfwException : Exception
{
    /// <summary>
    /// The legacy PowerBuilder type name, <c>"pfwexception"</c>, exactly as spelled at
    /// <c>pfwexception.sru:L1/L3/L7</c> and exactly as passed to the throw helper at
    /// <c>pfwthrowexception.srf:L9</c>.
    /// </summary>
    /// <remarks>
    /// This is a two-file contract, not a decorative constant. The legacy
    /// <c>throwexception(cls, text)</c> overload instantiates by name string with
    /// <c>Create Using cls</c> [throwexception.srf:L26], so the managed
    /// <c>ThrowException</c> helper must resolve this exact lowercase string to
    /// <see cref="PfwException"/>. Publishing it here once is what stops the two sides from
    /// drifting; the value is legacy and lowercase, while the identifier is PascalCase because
    /// this file carries no naming-analyzer suppression.
    /// </remarks>
    public const string TypeName = "pfwexception";

    /// <summary>
    /// The literal decoration prefix <c>"PowerFramework Runtime Error"</c> from
    /// <c>pfwexception.sru:L23</c>, reproduced character for character.
    /// </summary>
    /// <remarks>
    /// Exposed so that tests and diagnostics can assert against the prefix without duplicating
    /// the literal. It is published for recognition only: the decoration is applied in exactly
    /// one place, <see cref="SetMessage"/>, and no consumer may strip it, pre-empt it or apply
    /// it a second time on the way to a throw site.
    /// </remarks>
    public const string MessagePrefix = "PowerFramework Runtime Error";

    /// <summary>
    /// The separator between <see cref="MessagePrefix"/> and the caller's text: a single line
    /// feed, U+000A, which is what the PowerScript escape <c>~n</c> at
    /// <c>pfwexception.sru:L23</c> denotes.
    /// </summary>
    /// <remarks>
    /// Deliberately the literal <c>"\n"</c> and deliberately NOT
    /// <see cref="Environment.NewLine"/>, which is LF on Linux and CRLF on Windows. The legacy
    /// message is identical on every platform, so a platform-dependent separator would change
    /// observable behaviour. A carriage return never appears in this decoration.
    /// </remarks>
    public const string MessageSeparator = "\n";

    /// <summary>
    /// Backing store for the decorated message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This field is the mechanism that reproduces a MUTABLE-message legacy type on an
    /// IMMUTABLE BCL base. PowerBuilder's <c>throwable</c> exposes a settable message, which is
    /// why the legacy type can override <c>setmessage</c> at all, whereas
    /// <see cref="Exception.Message"/> is read-only once constructed. Holding the decorated text
    /// here and overriding the (virtual) <see cref="Message"/> property to return it gives
    /// <see cref="SetMessage"/> somewhere to write while keeping every BCL consumer working:
    /// <see cref="Exception.ToString"/> reads the virtual property, so a decorated message
    /// propagates into logs and into unhandled-exception output without further help.
    /// </para>
    /// <para>
    /// It is <c>null</c> until <see cref="SetMessage"/> or a message-carrying constructor runs,
    /// and that null is meaningful rather than incidental: it selects the
    /// <see cref="Exception.Message"/> fallback, so an instance created without a message
    /// behaves exactly like any other BCL exception created without one.
    /// </para>
    /// </remarks>
    private string? _message;

    /// <summary>
    /// Initializes a new instance that carries no message of its own.
    /// </summary>
    /// <remarks>
    /// A .NET convention addition, not a legacy behaviour: the legacy object has only a
    /// <c>create</c> event whose whole body is <c>call super::create</c> plus a
    /// <c>TriggerEvent(this, "constructor")</c> [pfwexception.sru:L26-L29], so there is no
    /// legacy constructor signature to translate. The parameterless form exists because the
    /// .NET design guidelines expect the standard three-constructor set on an exception type,
    /// and because it is the closest analogue of the bare <c>Create Using cls</c> that
    /// <c>throwexception.srf:L26</c> performs before it calls <c>SetMessage</c>. It applies no
    /// decoration, so <see cref="Message"/> falls back to the base implementation until
    /// <see cref="SetMessage"/> is called - which is precisely the state the legacy object is in
    /// between its creation and its first <c>SetMessage</c>.
    /// </remarks>
    public PfwException()
    {
    }

    /// <summary>
    /// Initializes a new instance whose message is decorated exactly as
    /// <see cref="SetMessage"/> would decorate it.
    /// </summary>
    /// <param name="message">
    /// The caller's undecorated text. A <see langword="null"/> value is tolerated and
    /// contributes nothing, matching PowerScript string concatenation.
    /// </param>
    /// <remarks>
    /// <para>
    /// A .NET convention addition. It DECORATES deliberately, so that
    /// <c>new PfwException(text).Message</c> equals what the legacy
    /// <c>pfwThrowException(text)</c> path produces: that path creates the object and then calls
    /// <c>SetMessage(text)</c> [throwexception.srf:L26-L27], and this constructor collapses the
    /// same two steps into one. The alternative - passing the text through undecorated, as
    /// direct <c>runtimeerror</c> construction would - was rejected because it would give the
    /// framework two message shapes for one type, and a caller could not tell from the call site
    /// which one it was going to get.
    /// </para>
    /// <para>
    /// The undecorated text is still handed to the base constructor, so the caller's original
    /// wording remains available through <c>base.Message</c> for diagnostics even though
    /// <see cref="Message"/> reports the decorated form.
    /// </para>
    /// </remarks>
    public PfwException(string message)
        : base(message)
    {
        // Routed through the private decorator rather than through SetMessage on purpose: it is
        // the identical single code path, so the constructor and the setter cannot drift, and no
        // instance method is invoked during construction.
        _message = Decorate(message);
    }

    /// <summary>
    /// Initializes a new instance whose message is decorated exactly as
    /// <see cref="SetMessage"/> would decorate it, and which wraps the fault that caused it.
    /// </summary>
    /// <param name="message">
    /// The caller's undecorated text. A <see langword="null"/> value is tolerated and
    /// contributes nothing, matching PowerScript string concatenation.
    /// </param>
    /// <param name="innerException">
    /// The underlying fault, or <see langword="null"/> when there is none.
    /// </param>
    /// <remarks>
    /// A .NET convention addition with no legacy counterpart at all: PowerScript's
    /// <c>catch(throwable e) ... throw e</c> idiom used by <c>throwexception.srf:L29-L30</c>
    /// rethrows the original object rather than nesting it, so the legacy has no
    /// inner-exception concept to translate. It is provided because discarding a caught fault
    /// when raising this type would destroy diagnostic information that the network boundaries
    /// introduced by this decomposition make materially more likely to matter. Decoration is
    /// identical to the single-argument form.
    /// </remarks>
    public PfwException(string message, Exception innerException)
        : base(message, innerException)
    {
        _message = Decorate(message);
    }

    // ------------------------------------------------------------------------------------------
    //  The four type-level defaults from pfwexception.sru:L8-L11.
    //
    //  Each is a read-write property seeded with the legacy default value, for the reason given
    //  in DECISION 4 of the file header: the legacy declarations are instance fields with
    //  type-level defaults, so both the value and its assignability are part of the shape.
    //  Identifiers are PascalCase and values are legacy. None of the four hides or shadows a
    //  member of System.Exception - Exception publishes Message, InnerException, StackTrace,
    //  Source, HelpLink, Data, HResult and TargetSite, and no name below collides with any of
    //  them - so no `new` modifier is needed and none is used.
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The legacy object name, defaulting to <c>"pfwexception"</c> [pfwexception.sru:L8].
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Exception.Source"/>, which is deliberately left alone: the BCL
    /// populates Source with the name of the application or assembly that raised the fault,
    /// whereas the legacy field names the PowerBuilder object. Writing one into the other would
    /// invent a correspondence the legacy does not have.
    /// </remarks>
    public string ObjectName { get; set; } = TypeName;

    /// <summary>
    /// The legacy class name, defaulting to <c>"pfwexception"</c> [pfwexception.sru:L9].
    /// </summary>
    /// <remarks>
    /// The legacy field is spelled <c>class</c>, which is a C# keyword, so the identifier is
    /// rendered as <c>ClassName</c>; the VALUE is unchanged. It duplicates
    /// <see cref="ObjectName"/> in the legacy declarations, and that duplication is reproduced
    /// rather than collapsed into one member, because both fields exist on PowerBuilder's
    /// <c>throwable</c> surface and are independently assignable there.
    /// </remarks>
    public string ClassName { get; set; } = TypeName;

    /// <summary>
    /// The legacy routine name, defaulting to <c>"create"</c> [pfwexception.sru:L10].
    /// </summary>
    /// <remarks>
    /// The default names the <c>create</c> event, which is where a PowerBuilder object of this
    /// type comes into being [pfwexception.sru:L26]. It is preserved verbatim; it is not
    /// re-derived from the managed call stack, because substituting a real routine name would
    /// replace a legacy constant with a computed value.
    /// </remarks>
    public string RoutineName { get; set; } = "create";

    /// <summary>
    /// The legacy line number, defaulting to the sentinel <c>-1</c> meaning "unknown line"
    /// [pfwexception.sru:L11].
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>-1</c> is preserved exactly and is never replaced with a real line number: the
    /// legacy value is a constant that says the line is unknown, and resolving it would be a
    /// behaviour change rather than a fix. Line information in the managed port travels through
    /// <see cref="Exception.StackTrace"/> instead.
    /// </para>
    /// <para>
    /// The legacy declaration is a PowerScript <c>integer</c>, a 16-bit signed type. <c>int</c>
    /// is used here as the widening, non-narrowing representation: it holds the <c>-1</c>
    /// sentinel exactly, it is what every .NET consumer expects of a line number, and a sweep of
    /// ws_objects/** found no site that reads or writes this field, so the wider domain is
    /// unobservable. Narrowing to <c>short</c> would buy no fidelity and would force a cast on
    /// every managed caller.
    /// </para>
    /// </remarks>
    public int Line { get; set; } = -1;

    /// <summary>
    /// Gets the decorated message, or the base implementation's message when no message has been
    /// set.
    /// </summary>
    /// <value>
    /// <see cref="MessagePrefix"/> + <see cref="MessageSeparator"/> + the text most recently
    /// supplied to <see cref="SetMessage"/> or to a message-carrying constructor; otherwise
    /// whatever <see cref="Exception.Message"/> would return.
    /// </value>
    /// <remarks>
    /// This override is the read half of the mutable-message mechanism described on
    /// <see cref="_message"/>. It is the managed analogue of PowerBuilder's
    /// <c>throwable.text</c> property, which is what the framework's own live consumer reads:
    /// <c>ws_objects/pfw.utility.invoker.pbl.src/n_cst_eventful.sru:860</c> raises this type and
    /// the enclosing handler reads <c>ex.text</c> off it, so the DECORATED string - not the
    /// caller's raw text - is the observable value, and that is what is returned here. The
    /// fallback is <see cref="Exception.Message"/> rather than an empty string so that an
    /// instance carrying no message of its own is indistinguishable from any other BCL exception
    /// in the same state.
    /// </remarks>
    public override string Message => _message ?? base.Message;

    /// <summary>
    /// Replaces this exception's message with <paramref name="newMessage"/> decorated by
    /// <see cref="MessagePrefix"/> and a single line feed.
    /// </summary>
    /// <param name="newMessage">
    /// The undecorated text. Declared non-nullable because the legacy prototype at
    /// <c>pfwexception.sru:L20</c> declares a plain <c>string</c>, so the annotation states the
    /// intended contract; a caller who forces a null through anyway gets the legacy
    /// concatenation outcome rather than an exception. Both <see langword="null"/> and the empty
    /// string contribute nothing, leaving the prefix and its line feed as the whole message.
    /// </param>
    /// <remarks>
    /// <para>
    /// The direct port of <c>pfwexception.sru:L23</c>, whose entire body is
    /// <c>super::SetMessage("PowerFramework Runtime Error~n" + newMessage)</c>. The legacy member
    /// name is kept - it is already PascalCase, so it needs no analyzer suppression - and the
    /// semantics are kept with it, in three parts that are each easy to get wrong:
    /// </para>
    /// <para>
    /// FIRST, the decoration is applied UNCONDITIONALLY, on every call. There is no idempotence
    /// check, no test for an already-present prefix, and no stripping of one. The visible
    /// consequence is that feeding a decorated message back in decorates it a second time:
    /// <c>SetMessage("foo")</c> followed by <c>SetMessage(ex.Message)</c> yields
    /// <c>"PowerFramework Runtime Error\nPowerFramework Runtime Error\nfoo"</c>. That is legacy
    /// behaviour and it is preserved; guarding against it would be exactly the silent correction
    /// this port forbids.
    /// </para>
    /// <para>
    /// SECOND, the call REPLACES rather than appends, because the legacy body passes the
    /// decorated ARGUMENT to the ancestor's setter and PowerBuilder's <c>throwable.SetMessage</c>
    /// assigns the message rather than extending it. Two identical successive calls therefore
    /// leave exactly one prefix in place, even though the prefix was applied twice - the
    /// double-prefix outcome above requires the current message to be fed back in, which is the
    /// only way the legacy can produce it either.
    /// </para>
    /// <para>
    /// THIRD, a null or empty argument is never rejected. PowerScript concatenation treats an
    /// unset string as empty, so the result is the prefix plus its line feed and nothing more.
    /// Throwing an <see cref="ArgumentNullException"/> here would be a new failure mode that the
    /// legacy does not have, and adding one inside an exception type is doubly hostile: it would
    /// replace the fault a caller is trying to report with a different one.
    /// </para>
    /// <para>
    /// The member is non-virtual by design. Nothing in the legacy tree derives from
    /// <c>pfwexception</c> - verified across ws_objects/** - so virtual dispatch here would have
    /// no legacy consumer to serve, while a non-virtual member makes the decoration contract
    /// unbreakable and lets the constructors share its logic without ever calling an overridable
    /// method during construction.
    /// </para>
    /// </remarks>
    public void SetMessage(string newMessage) => _message = Decorate(newMessage);

    /// <summary>
    /// Applies the legacy decoration from <c>pfwexception.sru:L23</c> to
    /// <paramref name="text"/>.
    /// </summary>
    /// <param name="text">
    /// The undecorated text. Annotated nullable on purpose: the public entry points declare the
    /// non-nullable <c>string</c> that the legacy signature declares, yet a caller can still
    /// force a null through, and this helper is where that null is genuinely tolerated rather
    /// than merely permitted by an annotation.
    /// </param>
    /// <returns>
    /// <see cref="MessagePrefix"/>, then <see cref="MessageSeparator"/>, then
    /// <paramref name="text"/>.
    /// </returns>
    /// <remarks>
    /// The single decoration code path in this type, shared by <see cref="SetMessage"/> and by
    /// both message-carrying constructors so that the setter and the constructors cannot diverge.
    /// String concatenation is used precisely because it substitutes the empty string for a null
    /// operand, which is the same thing PowerScript concatenation does - the null tolerance is
    /// the concatenation's own behaviour, not a special case bolted on top of it.
    /// </remarks>
    private static string Decorate(string? text) => MessagePrefix + MessageSeparator + text;
}
